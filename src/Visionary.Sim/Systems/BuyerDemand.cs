using Visionary.Sim.Numerics;

namespace Visionary.Sim.Systems;

/// <summary>1つの (用途, 品目) の組についての需要(GDD02c §2 / GDD02b §5)。</summary>
public readonly record struct DemandLine
{
    public DemandPurpose Purpose { get; init; }

    public int ItemId { get; init; }

    /// <summary>相場項 = ApplyPermille(相場基準, 許容乖離‰)。<b>在庫圧力を掛ける前</b>(GDD02c §2.1)。</summary>
    public int MarketTerm { get; init; }

    /// <summary>相場基準が立ったか。false なら <see cref="MarketTerm"/> は0で、min から落ちる。</summary>
    public bool HasMarketTerm { get; init; }

    /// <summary>現金上限 = FloorDiv(用途に使える資金, max(1日分の数量, 1))。<b>常にある</b>(GDD02c §2.1)。</summary>
    public int CashCap { get; init; }

    /// <summary>利潤上限(GDD02c §2.3)。生産の入力にだけある。</summary>
    public int ProfitCap { get; init; }

    public bool HasProfitCap { get; init; }

    /// <summary>目標在庫。単位: 個。<b>耐久だけ耐久値</b>(GDD02b §2)。</summary>
    public int TargetStock { get; init; }

    /// <summary>予想在庫。単位は <see cref="TargetStock"/> と揃う(GDD02b §5.1)。</summary>
    public int ExpectedStock { get; init; }

    /// <summary>在庫圧力‰(GDD02b §5.1)。500〜1500、目標在庫の2倍超で0。</summary>
    public int StockPressurePermille { get; init; }

    /// <summary>線形解の基礎値。相場項、無ければ現金上限(GDD02b §5.2)。<b>在庫圧力を掛けない</b>。</summary>
    public int BaseValue { get; init; }

    /// <summary>予算 = min( ApplyPermille(相場項, 在庫圧力‰) , 現金上限 , 利潤上限 )(GDD02c §2.1)。</summary>
    public int Budget { get; init; }
}

/// <summary>世帯1戸ぶんの需要(GDD02c §2 / GDD02b §3・§5)。</summary>
public readonly record struct HouseholdDemand
{
    /// <summary>GDD02b §3.2 の走査順(必需 → 耐久 → 生産の入力 → 嗜好、同一用途は品目Id昇順)に並んだ組。</summary>
    public IReadOnlyList<DemandLine> Lines { get; init; }

    /// <summary>必需の取り置き = Σ_(必需, 品目)(目標在庫 × 相場基準)(GDD02b §3.1)。単位: 貨幣。</summary>
    public long NecessityReserve { get; init; }

    /// <summary>運転資金 = Σ_(生産の入力, 品目)(目標在庫 × 相場基準)(同上)。単位: 貨幣。</summary>
    public long WorkingCapital { get; init; }
}

/// <summary>
/// 世帯の (用途, 品目) の組をすべて作る(GDD02c §2 / GDD02b §3・§5)。<see cref="World"/> と
/// <see cref="WorldDefinition"/> を読んで <see cref="BuyerBudget"/> を呼ぶ。<b>書き込みは
/// 一切しない。</b>
/// </summary>
public sealed class BuyerDemand
{
    private readonly WorldDefinition _definition;

    public BuyerDemand(WorldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _definition = definition;
    }

    /// <summary>世帯の (用途, 品目) の組をすべて作る。</summary>
    /// <param name="hasPreviousOutputOfferPrice">
    /// <b>前日の</b>自世帯の出力品目の提示価格があるか。<b>呼び出し側が <see cref="World.Market"/> を
    /// <c>Clear()</c> する前に控えた値を渡すこと</b>(GDD08 §6.3。当日の値を渡すと同一tick内で
    /// 循環する)。
    /// </param>
    public HouseholdDemand Build(
        World world,
        HouseholdState household,
        bool hasPreviousOutputOfferPrice,
        int previousOutputOfferPrice)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(household);

        var definition = _definition;
        var recipe = definition.Recipes[(int)household.Occupation];

        // 誰の観測かは世帯主(親方)である。selfHouseholdIdにはhousehold.Idを渡す ──
        // HeadNpcIdではない(TDD01 §3.2「取り違えを型で防げない」経路)。
        var headObservations = world.Knowledge[household.HeadNpcId];
        int itemCount = definition.ItemCount;

        // 0. 相場基準を品目ごとに1回だけ引く(買い手側、遅い。GDD02c §1.2)。薪は必需と生産の
        // 入力の両方に現れるが、相場基準そのものは品目に対して1つでよい。
        var isReferenceRelevant = new bool[itemCount];

        foreach (var input in recipe.Inputs)
        {
            isReferenceRelevant[input.ItemId] = true;
        }

        for (int itemId = 0; itemId < itemCount; itemId++)
        {
            if (definition.NecessityTargetStockDays[itemId] > 0
                || definition.PreferenceTargetStockDays[itemId] > 0)
            {
                isReferenceRelevant[itemId] = true;
            }
        }

        isReferenceRelevant[Item.Tools] = true;

        var hasReference = new bool[itemCount];
        var reference = new int[itemCount];

        for (int itemId = 0; itemId < itemCount; itemId++)
        {
            if (!isReferenceRelevant[itemId])
            {
                continue;
            }

            // TryBuyer(遅い側)を呼ぶ。TrySeller は呼んではならない ── 買い手が速くなると
            // GDD02c §1.2 の天井が消える。
            hasReference[itemId] = MarketReference.TryBuyer(
                headObservations, itemId, household.Id, world.Now,
                definition.ObservationRetentionDays,
                out reference[itemId]);
        }

        var lines = new List<DemandLine>();
        long necessityReserve = 0L;
        long workingCapital = 0L;

        // 1. 必需(品目Id昇順)。取り置きは相場基準が立った品目だけを足す(GDD02b §3.1)。
        for (int itemId = 0; itemId < itemCount; itemId++)
        {
            if (definition.NecessityTargetStockDays[itemId] <= 0)
            {
                continue;
            }

            int target = DailyConsumption.Lookahead(
                definition, world, household, itemId, world.Now, definition.NecessityTargetStockDays[itemId]);
            int expected = household.HouseholdInventory[itemId];
            int dailyQuantity = DailyConsumption.Lookahead(definition, world, household, itemId, world.Now, days: 1);

            if (hasReference[itemId])
            {
                necessityReserve += (long)target * reference[itemId];
            }

            // Necessityの母数は常に流動資金そのものなので、necessityReserve/workingCapitalは
            // AvailableFundsの中では読まれない(呼び出しの形を他の用途と揃えるために渡す)。
            int availableFunds = BuyerBudget.AvailableFunds(
                DemandPurpose.Necessity, household.LiquidFunds, necessityReserve, workingCapital);

            lines.Add(BuildLine(
                DemandPurpose.Necessity, itemId, hasReference[itemId], reference[itemId],
                definition.TolerancePermille, target, expected, dailyQuantity, availableFunds,
                hasProfitCap: false, profitCap: 0));
        }

        // 2. 耐久(全世帯に常に1行。Item.Toolsは鍛冶自身の入力ではない)。必需の取り置きは
        // ここまでに確定している(必需のループが先に終わっているため)。
        {
            int headRank = (int)world.Npcs[household.HeadNpcId].Rank;
            int target = IntegerMath.ApplyPermille(
                IntegerMath.ApplyPermille(definition.ToolDurabilityPerUnit, definition.ToolTargetStockPermille),
                definition.RankCoefficientPermille[headRank]);
            int expected = (household.WorkshopInventory[Item.Tools] * definition.ToolDurabilityPerUnit)
                - household.ToolWear;
            int availableFunds = BuyerBudget.AvailableFunds(
                DemandPurpose.Durable, household.LiquidFunds, necessityReserve, workingCapital: 0);

            lines.Add(BuildLine(
                DemandPurpose.Durable, Item.Tools, hasReference[Item.Tools], reference[Item.Tools],
                definition.TolerancePermille, target, expected, dailyQuantity: 1, availableFunds,
                hasProfitCap: false, profitCap: 0));
        }

        // 3. 生産の入力の利潤上限をまとめて求める(入力の行を組み立てる前に)。
        var hasProfitCap = new bool[itemCount];
        var profitCap = new int[itemCount];
        int wearCostPerRun = BuyerBudget.WearCostPerRun(
            household.PurchaseUnitCostAverage[Item.Tools], recipe.LaborPermille, definition.ToolLifeLaborDays);

        BuyerBudget.ProfitCaps(
            recipe, hasPreviousOutputOfferPrice, previousOutputOfferPrice,
            definition.MinimumMarginPermille, wearCostPerRun,
            hasReference, reference, hasProfitCap, profitCap);

        // 生産の入力(品目Id昇順)。recipe.Inputsの並びに依存しない(Recipeは入力が昇順であることを
        // 保証していない)。運転資金は相場基準が立った品目だけを足す(GDD02b §3.1)。
        for (int itemId = 0; itemId < itemCount; itemId++)
        {
            bool isInputItem = false;

            foreach (var input in recipe.Inputs)
            {
                if (input.ItemId == itemId)
                {
                    isInputItem = true;
                    break;
                }
            }

            if (!isInputItem)
            {
                continue;
            }

            int target = definition.InputTargetStock(household.Occupation, itemId);
            int expected = household.WorkshopInventory[itemId];
            int dailyQuantity = definition.DailyInputQuantity(household.Occupation, itemId);

            if (hasReference[itemId])
            {
                workingCapital += (long)target * reference[itemId];
            }

            int availableFunds = BuyerBudget.AvailableFunds(
                DemandPurpose.ProductionInput, household.LiquidFunds, necessityReserve, workingCapital: 0);

            lines.Add(BuildLine(
                DemandPurpose.ProductionInput, itemId, hasReference[itemId], reference[itemId],
                definition.TolerancePermille, target, expected, dailyQuantity, availableFunds,
                hasProfitCap[itemId], profitCap[itemId]));
        }

        // 4. 嗜好(品目Id昇順)。運転資金が確定した後でないと母数が求まらない。
        for (int itemId = 0; itemId < itemCount; itemId++)
        {
            if (definition.PreferenceTargetStockDays[itemId] <= 0)
            {
                continue;
            }

            int target = DailyConsumption.Lookahead(
                definition, world, household, itemId, world.Now, definition.PreferenceTargetStockDays[itemId]);
            int expected = household.HouseholdInventory[itemId];
            int dailyQuantity = DailyConsumption.Lookahead(definition, world, household, itemId, world.Now, days: 1);
            int availableFunds = BuyerBudget.AvailableFunds(
                DemandPurpose.Preference, household.LiquidFunds, necessityReserve, workingCapital);

            lines.Add(BuildLine(
                DemandPurpose.Preference, itemId, hasReference[itemId], reference[itemId],
                definition.TolerancePermille, target, expected, dailyQuantity, availableFunds,
                hasProfitCap: false, profitCap: 0));
        }

        return new HouseholdDemand
        {
            Lines = lines,
            NecessityReserve = necessityReserve,
            WorkingCapital = workingCapital,
        };
    }

    private static DemandLine BuildLine(
        DemandPurpose purpose, int itemId, bool hasReference, int reference, int tolerancePermille,
        int targetStock, int expectedStock, int dailyQuantity, int availableFunds,
        bool hasProfitCap, int profitCap)
    {
        int stockPressure = BuyerBudget.StockPressurePermille(expectedStock, targetStock);
        int cashCap = BuyerBudget.CashCap(availableFunds, dailyQuantity);
        int marketTerm = hasReference ? IntegerMath.ApplyPermille(reference, tolerancePermille) : 0;
        int baseValue = BuyerBudget.BaseValue(hasReference, marketTerm, cashCap);
        int budget = BuyerBudget.Budget(hasReference, marketTerm, stockPressure, cashCap, hasProfitCap, profitCap);

        return new DemandLine
        {
            Purpose = purpose,
            ItemId = itemId,
            MarketTerm = marketTerm,
            HasMarketTerm = hasReference,
            CashCap = cashCap,
            ProfitCap = profitCap,
            HasProfitCap = hasProfitCap,
            TargetStock = targetStock,
            ExpectedStock = expectedStock,
            StockPressurePermille = stockPressure,
            BaseValue = baseValue,
            Budget = budget,
        };
    }
}
