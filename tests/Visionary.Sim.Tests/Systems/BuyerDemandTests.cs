using Visionary.Sim.Numerics;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="BuyerDemand"/>(GDD02 §8.2〜§8.2.7 / §6.2.1、#36 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class BuyerDemandTests
{
    /// <summary>
    /// パン屋を模したレシピ。小麦粉+薪 → パン。<c>Occupation.Miller</c>(添字0)として登録される
    /// (<see cref="EconomySystemTestFixtures.BuildDefinition"/> の仕様)。
    /// </summary>
    private static Recipe BakerLikeRecipe() =>
        new(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = 2 } },
            inputs: new[]
            {
                new ItemQuantity { ItemId = Item.Flour, Quantity = 1 },
                new ItemQuantity { ItemId = Item.Firewood, Quantity = 1 },
            },
            laborPermille: 1000);

    /// <summary>
    /// 生産の入力の目標在庫。Miller(添字0)の小麦粉・薪だけ指定値、他4職業
    /// (<see cref="EconomySystemTestFixtures.UnusedRecipe"/>)は入力itemId 1 に1を置く。
    /// </summary>
    private static int[][] InputTargets(int flourTarget, int firewoodTarget)
    {
        var millerRow = new int[Item.Count];
        millerRow[Item.Flour] = flourTarget;
        millerRow[Item.Firewood] = firewoodTarget;

        var rows = new int[5][];
        rows[0] = millerRow;

        for (int occupationId = 1; occupationId < 5; occupationId++)
        {
            var row = new int[Item.Count];
            row[1] = 1; // UnusedRecipeの入力itemId(EconomySystemTestFixtures参照)
            rows[occupationId] = row;
        }

        return rows;
    }

    private static int[] NecessityDays(int firewoodDays = 7, int breadDays = 3)
    {
        var row = new int[Item.Count];
        row[Item.Firewood] = firewoodDays;
        row[Item.Bread] = breadDays;

        return row;
    }

    private static int[] PreferenceDays(int beerDays = 1)
    {
        var row = new int[Item.Count];
        row[Item.Beer] = beerDays;

        return row;
    }

    private static int[][] ConsumptionTable(int firewoodQty, int breadQty)
    {
        var row = new int[Item.Count];
        row[Item.Firewood] = firewoodQty;
        row[Item.Bread] = breadQty;

        return new[] { (int[])row.Clone(), (int[])row.Clone(), (int[])row.Clone() };
    }

    private static WorldDefinition BuildDefinition(
        int flourInputTarget = 5,
        int firewoodInputTarget = 5,
        int firewoodConsumptionQty = 2,
        int breadConsumptionQty = 1,
        int toolTargetStockPermille = 500,
        int[]? rankCoefficientPermille = null,
        int necessityTolerancePermille = 1200,
        int[]? budgetRatioPermilleByPurpose = null,
        int[]? necessityTargetStockDays = null,
        int[]? preferenceTargetStockDays = null,
        int productionRunsPerToolWear = 30) =>
        EconomySystemTestFixtures.BuildDefinition(
            BakerLikeRecipe(),
            productionRunsPerToolWear: productionRunsPerToolWear,
            dailyConsumptionPerNpcByRank: ConsumptionTable(firewoodConsumptionQty, breadConsumptionQty),
            inputTargetStockByOccupation: InputTargets(flourInputTarget, firewoodInputTarget),
            necessityTargetStockDays: necessityTargetStockDays ?? NecessityDays(),
            preferenceTargetStockDays: preferenceTargetStockDays ?? PreferenceDays(),
            toolTargetStockPermille: toolTargetStockPermille,
            rankCoefficientPermille: rankCoefficientPermille ?? new[] { 1000, 600, 200 },
            necessityTolerancePermille: necessityTolerancePermille,
            budgetRatioPermilleByPurpose: budgetRatioPermilleByPurpose ?? new[] { 0, 50, 200, 10 });

    private static DemandLine FindLine(HouseholdDemand demand, DemandPurpose purpose, int itemId) =>
        demand.Lines.Single(line => line.Purpose == purpose && line.ItemId == itemId);

    /// <summary>
    /// 【核心】テスト表 #24。パン屋の世帯 → 行が必需(薪→パン)→ 生産の入力(小麦粉→薪)→
    /// 耐久(工具)→ 嗜好(ビール)の順に並ぶ。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>BuyerDemand.Build</c> の耐久の行の追加
    /// (<c>lines.Add(BuildLine(DemandPurpose.Durable, ...))</c>)を丸ごと削る変異
    /// (用途を1つ落とす)を当てたところ、<c>Assert.Equal(expected, actualPairs)</c> が
    /// 期待6件に対し実際5件(耐久が無い)で失敗した(赤を確認)。GDD02 §2.2「どれか1つでも
    /// 落とすと、その式がW2で一度も実行されない」に対応する。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void EveryPurposeProducesALineInTheSpendingScanOrder()
    {
        var definition = BuildDefinition();
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });

        var demand = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);

        var actualPairs = demand.Lines.Select(line => (line.Purpose, line.ItemId)).ToArray();
        var expectedPairs = new[]
        {
            (DemandPurpose.Necessity, Item.Firewood),
            (DemandPurpose.Necessity, Item.Bread),
            (DemandPurpose.ProductionInput, Item.Flour),
            (DemandPurpose.ProductionInput, Item.Firewood),
            (DemandPurpose.Durable, Item.Tools),
            (DemandPurpose.Preference, Item.Beer),
        };

        Assert.Equal(expectedPairs, actualPairs);
    }

    /// <summary>
    /// 【核心】テスト表 #25。薪の2行が (必需, 世帯在庫, 先読み7日) と (生産の入力, 工房在庫, 定数表)
    /// で別の値を持つ。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b>薪の必需の行と生産の入力の行を1本に畳む変異
    /// (生産の入力の行を作らず、必需の行の在庫だけを世帯在庫+工房在庫の合計にする)を当てたところ、
    /// <c>Assert.Equal(5, inputLine.TargetStock)</c>(定数表の値)に対応する行が存在しなくなり
    /// <c>InvalidOperationException</c>(Single が要素0件で失敗)で落ちた(赤を確認、
    /// GDD02 §8.2.1「生産用の薪が運転資金から丸ごと落ちる」に対応)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void FirewoodAppearsAsTwoLinesWithDifferentStocksAndTargets()
    {
        var definition = BuildDefinition(firewoodInputTarget: 5, firewoodConsumptionQty: 2);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].HouseholdInventory[Item.Firewood] = 3;
        world.Households[0].WorkshopInventory[Item.Firewood] = 8;

        var demand = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);

        var necessityLine = FindLine(demand, DemandPurpose.Necessity, Item.Firewood);
        var inputLine = FindLine(demand, DemandPurpose.ProductionInput, Item.Firewood);

        // 必需: 先読み7日 × 基礎量2(季節係数1000‰の据え置き) = 14。
        Assert.Equal(14, necessityLine.TargetStock);
        Assert.Equal(3, necessityLine.ExpectedStock);

        // 生産の入力: 定数表(InputTargets)の値そのもの。
        Assert.Equal(5, inputLine.TargetStock);
        Assert.Equal(8, inputLine.ExpectedStock);
    }

    /// <summary>
    /// 【核心】テスト表 #26。嗜好・耐久の目標在庫と相場基準をいくら大きくしても
    /// <c>WorkingCapital</c> が変わらない。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>BuyerDemand.Build</c> の耐久の行の直前に
    /// <c>workingCapital += (long)durableTarget * reference[Item.Tools];</c>
    /// (全用途を足す変異)を挿入したところ、耐久の目標在庫・相場基準を大きくした側の
    /// <c>Assert.Equal(smallWorkingCapital, largeWorkingCapital)</c> が失敗した(赤を確認、
    /// GDD02 §8.2.1「余剰資金が過小に出て嗜好が恒久に0になる」に対応)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void WorkingCapitalScansOnlyProductionInputAndNecessity()
    {
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].LiquidFunds = 1000;
        SetReference(world, world.Households[0].HeadNpcId, Item.Firewood, price: 10);
        SetReference(world, world.Households[0].HeadNpcId, Item.Flour, price: 7);
        SetReference(world, world.Households[0].HeadNpcId, Item.Tools, price: 99999);

        var smallDefinition = BuildDefinition(
            toolTargetStockPermille: 500,
            preferenceTargetStockDays: PreferenceDays(beerDays: 1));
        var largeDefinition = BuildDefinition(
            toolTargetStockPermille: 100_000,
            preferenceTargetStockDays: PreferenceDays(beerDays: 1000));

        long smallWorkingCapital = new BuyerDemand(smallDefinition)
            .Build(world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0)
            .WorkingCapital;
        long largeWorkingCapital = new BuyerDemand(largeDefinition)
            .Build(world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0)
            .WorkingCapital;

        Assert.Equal(smallWorkingCapital, largeWorkingCapital);
    }

    /// <summary>
    /// 【核心】テスト表 #27。在庫を目標まで満たしても <c>WorkingCapital</c> が変わらない。
    /// 観測の無い品目(薪)は加算されない。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b>必需の運転資金の加算を
    /// <c>workingCapital += (long)(target - expected) * reference[itemId];</c>
    /// (現在庫を差し引く変異)に変えたところ、<c>Assert.Equal(95, zeroStockWorkingCapital)</c> が
    /// 実際値35(パンの寄与3×20=60が消え、小麦粉の35だけになった)で失敗した(赤を確認、
    /// GDD02 §8.2.1「周期的な黒字倒産」に対応)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void WorkingCapitalIgnoresCurrentStockAndUnobservedItems()
    {
        var definition = BuildDefinition();
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].LiquidFunds = 1000;

        // パン(必需)・小麦粉(生産の入力)には観測を置く。薪は必需・生産の入力の両方に
        // 目標在庫を持つが、観測を一切置かない(「観測の無い品目は加算されない」を見る)。
        SetReference(world, world.Households[0].HeadNpcId, Item.Bread, price: 20);
        SetReference(world, world.Households[0].HeadNpcId, Item.Flour, price: 7);

        long zeroStockWorkingCapital = new BuyerDemand(definition)
            .Build(world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0)
            .WorkingCapital;

        // パン(必需:目標3)・小麦粉(生産の入力:目標5)を目標まで満たす。
        world.Households[0].HouseholdInventory[Item.Bread] = 3;
        world.Households[0].WorkshopInventory[Item.Flour] = 5;

        long fullStockWorkingCapital = new BuyerDemand(definition)
            .Build(world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0)
            .WorkingCapital;

        // 期待値 = パンの目標3×観測20 + 小麦粉の目標5×観測7 = 60+35 = 95。
        // 薪(必需目標14・生産の入力目標5、いずれも観測なし)は加算されない。
        Assert.Equal(95, zeroStockWorkingCapital);
        Assert.Equal(zeroStockWorkingCapital, fullStockWorkingCapital);
    }

    /// <summary>
    /// <see cref="BuyerReferenceComesFromTheHeadNpcAndIgnoresOwnOfferPrice"/> 専用の世帯1戸の世界。
    /// 世帯主のNpcId(2)を世帯Id(0)とわざと違える(TradeSystemTestsと同じ理由)。
    /// </summary>
    private static World BuildWorldWithHeadNpcIdDifferentFromHouseholdId()
    {
        const int HeadNpcId = 2;
        const int ApprenticeNpcId = 3;

        var world = new World(npcCount: 4, householdCount: 1, itemCount: Item.Count);
        world.Npcs[HeadNpcId].Rank = NpcRank.Master;
        world.Npcs[ApprenticeNpcId].Rank = NpcRank.Apprentice;

        world.Households[0] = new HouseholdState(
            id: 0, districtId: 0, headNpcId: HeadNpcId,
            memberNpcIds: new[] { HeadNpcId, ApprenticeNpcId }, itemCount: Item.Count);
        world.Households[0].Occupation = Occupation.Miller;

        return world;
    }

    /// <summary>
    /// 【核心】テスト表 #28。徒弟にだけ観測を置くと必需がフォールバックへ落ちる。<c>Market</c> に
    /// 自世帯の当日価格があっても必需の基礎値が変わらない。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>BuyerDemand.Build</c> の
    /// <c>world.Knowledge[household.HeadNpcId]</c> を <c>world.Knowledge[household.Id]</c>
    /// (世帯Idで誤って引く)に変える変異を当てたところ、世帯主(NpcId=2)に観測を置いたケースの
    /// <c>Assert.Equal(120, ...)</c> が実際値10(フォールバックのまま。誤って
    /// <c>Knowledge[0]</c>=空を読んだ)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void BuyerReferenceComesFromTheHeadNpcAndIgnoresOwnOfferPrice()
    {
        const int HeadNpcId = 2;
        const int ApprenticeNpcId = 3;
        const int OtherSellerId = 999;

        var definition = BuildDefinition();
        var world = BuildWorldWithHeadNpcIdDifferentFromHouseholdId();
        world.Households[0].LiquidFunds = 200; // フォールバック = ApplyPermille(200,50) = 10

        // now を進めて、過去日の観測を作れるようにする。
        EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 10 * 24);

        var observation = new PriceObservation
        {
            ItemId = Item.Firewood,
            LocationId = 0,
            Price = 100,
            SellerId = OtherSellerId,
            ObservedAt = Tick.FromDays(9),
            Source = ObservationSource.Direct,
        };

        // 徒弟のKnowledgeにだけ観測 → 相場基準が立たず、フォールバックのまま。
        world.Knowledge[ApprenticeNpcId].Add(observation);

        var withApprenticeOnly = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        int fallback = IntegerMath.ApplyPermille(200, 50);
        Assert.Equal(fallback, FindLine(withApprenticeOnly, DemandPurpose.Necessity, Item.Firewood).BaseValue);

        // 同じ観測を世帯主のKnowledgeに置くと相場基準が立つ(ApplyPermille(100,1200)=120)。
        world.Knowledge[HeadNpcId].Add(observation);

        var withHead = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        Assert.Equal(120, FindLine(withHead, DemandPurpose.Necessity, Item.Firewood).BaseValue);

        // Marketに自世帯の当日価格を置いても基礎値は変わらない(BuyerDemandはMarketを読まない)。
        world.Market[new MarketKey(Item.Firewood, world.Households[0].Id)] = 9999;

        var withOwnMarketEntry = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        Assert.Equal(120, FindLine(withOwnMarketEntry, DemandPurpose.Necessity, Item.Firewood).BaseValue);
    }

    /// <summary>
    /// 【核心】テスト表 #29。工具1個・N=30・摩耗5・500‰・階層1000‰ → 予想在庫25、目標在庫15。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b>耐久の予想在庫の計算を
    /// <c>household.WorkshopInventory[Item.Tools]</c>(個数のまま、耐久値へ変換しない変異)に
    /// 変えたところ、<c>Assert.Equal(25, durableLine.ExpectedStock)</c> が実際値1
    /// (在庫1・目標15で圧力1000‰のまま摩耗が進むまで工具を買い続ける経路)で失敗した
    /// (赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void DurableLineIsMeasuredInDurabilityNotUnits()
    {
        var definition = BuildDefinition(
            toolTargetStockPermille: 500,
            rankCoefficientPermille: new[] { 1000, 600, 200 },
            productionRunsPerToolWear: 30);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Tools] = 1;
        world.Households[0].ToolWearCount = 5;

        var demand = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        var durableLine = FindLine(demand, DemandPurpose.Durable, Item.Tools);

        Assert.Equal(25, durableLine.ExpectedStock);
        Assert.Equal(15, durableLine.TargetStock);
    }

    /// <summary>テスト表 #30。世帯主の階層を Apprentice(200‰)にすると目標在庫が3に縮む。</summary>
    [Fact]
    public void DurableTargetUsesTheHeadRankCoefficient()
    {
        var definition = BuildDefinition(
            toolTargetStockPermille: 500,
            rankCoefficientPermille: new[] { 1000, 600, 200 },
            productionRunsPerToolWear: 30);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Apprentice });

        var demand = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        var durableLine = FindLine(demand, DemandPurpose.Durable, Item.Tools);

        Assert.Equal(3, durableLine.TargetStock);
    }

    /// <summary>世帯主の <c>Knowledge</c> に、過去日(他の売り手999)の観測を1件仕込む。</summary>
    private static void SetReference(World world, int headNpcId, int itemId, int price)
    {
        if (world.Now.DayIndex < 1)
        {
            EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 10 * 24);
        }

        const int OtherSellerId = 999;

        world.Knowledge[headNpcId].Add(new PriceObservation
        {
            ItemId = itemId,
            LocationId = 0,
            Price = price,
            SellerId = OtherSellerId,
            ObservedAt = world.Now.AddDays(-1),
            Source = ObservationSource.Direct,
        });
    }
}
