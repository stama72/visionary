using Visionary.Sim.Numerics;

namespace Visionary.Sim.Systems;

/// <summary>1つの (用途, 品目) の組についての需要(GDD02 §8.2)。</summary>
public readonly record struct DemandLine
{
    public DemandPurpose Purpose { get; init; }

    public int ItemId { get; init; }

    /// <summary>基礎値。単位: 貨幣/1単位(耐久は1個あたり)。</summary>
    public int BaseValue { get; init; }

    /// <summary>目標在庫。単位: 個。<b>耐久だけ耐久値</b>(GDD02 §8.2.1)。</summary>
    public int TargetStock { get; init; }

    /// <summary>予想在庫。単位は <see cref="TargetStock"/> と揃う(GDD02 §8.2.2)。</summary>
    public int ExpectedStock { get; init; }

    public int StockPressurePermille { get; init; }

    /// <summary>予算 = ApplyPermille(基礎値, 在庫圧力‰)(GDD02 §8.2)。</summary>
    public int Budget { get; init; }
}

/// <summary>世帯1戸ぶんの需要(GDD02 §8.2)。</summary>
public readonly record struct HouseholdDemand
{
    /// <summary>GDD02 §6.2.1 の走査順に並んだ組。</summary>
    public IReadOnlyList<DemandLine> Lines { get; init; }

    /// <summary>必要運転資金(GDD02 §8.2.1)。単位: 貨幣。</summary>
    public long WorkingCapital { get; init; }

    /// <summary>余剰資金。単位: 貨幣。</summary>
    public int SurplusFunds { get; init; }
}

/// <summary>
/// 世帯の (用途, 品目) の組をすべて作る(GDD02 §8.2〜§8.2.7)。<see cref="World"/> と
/// <see cref="WorldDefinition"/> を読んで <see cref="BuyerBudget"/> を呼ぶ。<b>書き込みは
/// 一切しない。</b>
/// </summary>
/// <remarks>
/// <b>本タスクでは誰も呼ばない。</b>買うには店の選択(#37)が要り、それを本タスクへ引き込むと
/// 1タスクで順5 を丸ごと作ることになる(タスク仕様「スコープ」)。#37 が値付けの段の後、
/// 買い物の前に1世帯1回呼ぶ想定である。
/// </remarks>
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
    /// <c>Clear()</c> する前に控えた値を渡すこと</b>(GDD02 §8.2.1。当日の値を渡すと同一tick内で
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
        var headObservations = world.Knowledge[household.HeadNpcId];
        int itemCount = definition.ItemCount;

        // 0. 相場基準を品目ごとに1回だけ引く(薪は必需と生産の入力の両方に現れる)。引くのは
        // { 生産の入力の品目 } ∪ { 必需の品目 } ∪ { Item.Tools } だけでよい(嗜好の基礎値は
        // 相場基準を使わない。必要運転資金も嗜好・耐久を走査しない)。
        var isReferenceRelevant = new bool[itemCount];

        foreach (var input in recipe.Inputs)
        {
            isReferenceRelevant[input.ItemId] = true;
        }

        for (int itemId = 0; itemId < itemCount; itemId++)
        {
            if (definition.NecessityTargetStockDays[itemId] > 0)
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

            // 相場基準は世帯主(親方)の観測から作り、自分の前日の提示価格は混ぜない
            // (GDD02 §8.2・GDD06 §3.1)。selfHouseholdIdにはhousehold.Idを渡す ── HeadNpcIdでは
            // ない(TDD01 §3.2「取り違えを型で防げない」経路)。
            hasReference[itemId] = OfferPrice.TryMarketReference(
                headObservations, itemId, household.Id, world.Now,
                definition.ObservationRetentionDays,
                hasOwnPreviousPrice: false, ownPreviousPrice: 0,
                out reference[itemId]);
        }

        var lines = new List<DemandLine>();
        long workingCapital = 0L;

        // 1・2・5・6(必需)。走査順は品目Id昇順(GDD02 §6.2.1)。必要運転資金は
        // (生産の入力, 品目)と(必需, 品目)だけを走査する(下の生産の入力ループと合わせて完結する)。
        for (int itemId = 0; itemId < itemCount; itemId++)
        {
            if (definition.NecessityTargetStockDays[itemId] <= 0)
            {
                continue;
            }

            int target = DailyConsumption.Lookahead(
                definition, world, household, itemId, world.Now, definition.NecessityTargetStockDays[itemId]);
            int expected = household.HouseholdInventory[itemId];

            if (hasReference[itemId])
            {
                workingCapital += (long)target * reference[itemId];
            }

            int baseValue = BuyerBudget.NecessityBaseValue(
                hasReference[itemId], reference[itemId], household.LiquidFunds,
                definition.NecessityTolerancePermille,
                definition.BudgetRatioPermilleByPurpose[(int)DemandPurpose.Necessity]);

            lines.Add(BuildLine(DemandPurpose.Necessity, itemId, baseValue, target, expected));
        }

        // 3(派生需要)。生産の入力の行を組み立てる前に基礎値をまとめて求める。
        var derivedBaseValues = new int[itemCount];

        BuyerBudget.DerivedDemand(
            recipe, hasPreviousOutputOfferPrice, previousOutputOfferPrice,
            definition.MinimumMarginPermille, household.LiquidFunds,
            definition.BudgetRatioPermilleByPurpose[(int)DemandPurpose.Necessity],
            hasReference, reference, derivedBaseValues);

        // 1・2・5・6(生産の入力)。品目Id昇順に並べる ── recipe.Inputsの並びに依存しない
        // (Recipeは入力が昇順であることを保証していない)。
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

            if (hasReference[itemId])
            {
                workingCapital += (long)target * reference[itemId];
            }

            lines.Add(BuildLine(DemandPurpose.ProductionInput, itemId, derivedBaseValues[itemId], target, expected));
        }

        int surplus = BuyerBudget.SurplusFunds(household.LiquidFunds, workingCapital);

        // 1・5・6(耐久)。全世帯に常に1行(鍛冶を含む。Item.Toolsは鍛冶自身の入力ではない)。
        {
            int headRank = (int)world.Npcs[household.HeadNpcId].Rank;
            int target = IntegerMath.ApplyPermille(
                IntegerMath.ApplyPermille(definition.ToolDurabilityPerUnit, definition.ToolTargetStockPermille),
                definition.RankCoefficientPermille[headRank]);
            int expected = (household.WorkshopInventory[Item.Tools] * definition.ToolDurabilityPerUnit)
                - household.ToolWear;
            int baseValue = BuyerBudget.DurableBaseValue(
                hasReference[Item.Tools], reference[Item.Tools], household.LiquidFunds,
                definition.BudgetRatioPermilleByPurpose[(int)DemandPurpose.Durable]);

            lines.Add(BuildLine(DemandPurpose.Durable, Item.Tools, baseValue, target, expected));
        }

        // 1・5・6(嗜好)。品目Id昇順。余剰資金が確定した後でないと基礎値が求まらない。
        for (int itemId = 0; itemId < itemCount; itemId++)
        {
            if (definition.PreferenceTargetStockDays[itemId] <= 0)
            {
                continue;
            }

            int target = DailyConsumption.Lookahead(
                definition, world, household, itemId, world.Now, definition.PreferenceTargetStockDays[itemId]);
            int expected = household.HouseholdInventory[itemId];
            int baseValue = BuyerBudget.PreferenceBaseValue(
                surplus, definition.BudgetRatioPermilleByPurpose[(int)DemandPurpose.Preference]);

            lines.Add(BuildLine(DemandPurpose.Preference, itemId, baseValue, target, expected));
        }

        return new HouseholdDemand
        {
            Lines = lines,
            WorkingCapital = workingCapital,
            SurplusFunds = surplus,
        };
    }

    private static DemandLine BuildLine(
        DemandPurpose purpose, int itemId, int baseValue, int targetStock, int expectedStock)
    {
        int pressure = BuyerBudget.StockPressurePermille(expectedStock, targetStock);
        int budget = BuyerBudget.Budget(baseValue, pressure);

        return new DemandLine
        {
            Purpose = purpose,
            ItemId = itemId,
            BaseValue = baseValue,
            TargetStock = targetStock,
            ExpectedStock = expectedStock,
            StockPressurePermille = pressure,
            Budget = budget,
        };
    }
}
