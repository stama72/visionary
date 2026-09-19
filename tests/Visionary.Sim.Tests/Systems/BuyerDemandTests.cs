using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="BuyerDemand"/>(GDD02c §2 / GDD02b §3・§5、W2-08 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class BuyerDemandTests
{
    /// <summary>
    /// パン屋を模したレシピ。小麦粉+薪 → パン。<c>Occupation.Miller</c>(添字0)として登録される
    /// (<see cref="EconomySystemTestFixtures.BuildDefinition"/> の仕様)。
    /// </summary>
    private static Recipe BakerLikeRecipe(int outputQuantity = 2) =>
        new(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = outputQuantity } },
            inputs: new[]
            {
                new ItemQuantity { ItemId = Item.Flour, Quantity = 1 },
                new ItemQuantity { ItemId = Item.Firewood, Quantity = 1 },
            },
            laborPermille: 1000);

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

    private static int[][] ConsumptionTable(int firewoodQty, int breadQty, int beerQty = 0)
    {
        var row = new int[Item.Count];
        row[Item.Firewood] = firewoodQty;
        row[Item.Bread] = breadQty;
        row[Item.Beer] = beerQty;

        return new[] { (int[])row.Clone(), (int[])row.Clone(), (int[])row.Clone() };
    }

    /// <summary>
    /// 生産の入力の目標在庫(小麦粉・薪とも5)。生産能力1(<see cref="WorldDefinition.NominalLaborPermille"/>
    /// 1300‰・レシピの所要労働‰ 1000 → 1実行/日) × 必要数量1 × <c>inputBufferDays</c>5 = 5
    /// (WorldDefinition が導出する。EconomySystemTestFixtures の既定値のまま)。
    /// </summary>
    private static WorldDefinition BuildDefinition(
        int firewoodConsumptionQty = 2,
        int breadConsumptionQty = 1,
        int beerConsumptionQty = 0,
        int toolTargetStockPermille = 500,
        int[]? rankCoefficientPermille = null,
        int tolerancePermille = 1200,
        int[]? necessityTargetStockDays = null,
        int[]? preferenceTargetStockDays = null,
        int toolLifeLaborDays = 30,
        int minimumMarginPermille = 0,
        int outputQuantity = 2) =>
        EconomySystemTestFixtures.BuildDefinition(
            BakerLikeRecipe(outputQuantity),
            toolLifeLaborDays: toolLifeLaborDays,
            dailyConsumptionPerNpcByRank:
                ConsumptionTable(firewoodConsumptionQty, breadConsumptionQty, beerConsumptionQty),
            necessityTargetStockDays: necessityTargetStockDays ?? NecessityDays(),
            preferenceTargetStockDays: preferenceTargetStockDays ?? PreferenceDays(),
            toolTargetStockPermille: toolTargetStockPermille,
            rankCoefficientPermille: rankCoefficientPermille ?? new[] { 1000, 600, 200 },
            tolerancePermille: tolerancePermille,
            minimumMarginPermille: minimumMarginPermille);

    private static DemandLine FindLine(HouseholdDemand demand, DemandPurpose purpose, int itemId) =>
        demand.Lines.Single(line => line.Purpose == purpose && line.ItemId == itemId);

    /// <summary>
    /// 【核心】テスト表 #23。薪(必需)・パン(必需)・工具(耐久)・穀物(入力)・薪(入力)・ビール(嗜好)を
    /// 持つ世帯で、<c>Lines</c> の <c>(Purpose, ItemId)</c> の並びが必需の品目Id昇順 → 耐久 →
    /// 入力の品目Id昇順 → 嗜好 ちょうどであること。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-20)。</b><c>BuyerDemand.Build</c> の走査順を旧の
    /// (必需 → 入力 → 耐久 → 嗜好)に戻す変異(耐久のブロックと生産の入力のループの位置を
    /// 入れ替える)を当てたところ、<c>Assert.Equal(expectedPairs, actualPairs)</c> が
    /// 「耐久が3番目ではなく5番目に来る」形で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void DemandLinesFollowNecessityDurableInputPreference()
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
            (DemandPurpose.Durable, Item.Tools),
            (DemandPurpose.ProductionInput, Item.Flour),
            (DemandPurpose.ProductionInput, Item.Firewood),
            (DemandPurpose.Preference, Item.Beer),
        };

        Assert.Equal(expectedPairs, actualPairs);
    }

    /// <summary>
    /// 【核心】テスト表 #24。薪の2行が (必需, 世帯在庫, 先読み7日) と (生産の入力, 工房在庫, 定数表)
    /// で別の値を持つ。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-20)。</b>薪の必需の行と生産の入力の行を1本に畳む変異
    /// (生産の入力の行を作らず、必需の行の在庫だけを世帯在庫+工房在庫の合計にする)を当てたところ、
    /// <c>Assert.Equal(5, inputLine.TargetStock)</c>(定数表の値)に対応する行が存在しなくなり
    /// <c>InvalidOperationException</c>(Single が要素0件で失敗)で落ちた(赤を確認)。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void FirewoodGetsSeparateNecessityAndInputLines()
    {
        var definition = BuildDefinition(firewoodConsumptionQty: 2);
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

        // 生産の入力: 定義から導出(生産能力1 × 必要数量1 × inputBufferDays5 = 5)。
        Assert.Equal(5, inputLine.TargetStock);
        Assert.Equal(8, inputLine.ExpectedStock);
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

    /// <summary>
    /// テスト表 #25。必需2品目のうち1つだけ相場基準がある世帯で、<c>NecessityReserve</c> が
    /// その1品目ぶんだけ。入力側も同じく <c>WorkingCapital</c> が立った品目ぶんだけ。
    /// </summary>
    [Fact]
    public void ReservesSkipItemsWithoutReference()
    {
        var definition = BuildDefinition();
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].LiquidFunds = 1000;

        // 必需: パンにだけ観測を置く(薪は無観測のまま)。
        SetReference(world, world.Households[0].HeadNpcId, Item.Bread, price: 20);
        // 生産の入力: 小麦粉にだけ観測を置く(薪は無観測のまま)。
        SetReference(world, world.Households[0].HeadNpcId, Item.Flour, price: 7);

        var demand = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);

        // 必需の取り置き = パンの目標3 × 観測20 = 60(薪の目標14は無観測なので加算されない)。
        Assert.Equal(60, demand.NecessityReserve);
        // 運転資金 = 小麦粉の目標5 × 観測7 = 35(薪の入力目標5は無観測なので加算されない)。
        Assert.Equal(35, demand.WorkingCapital);
    }

    /// <summary>
    /// 【核心】テスト表 #26。同一売り手の観測が3件ある世帯で、<c>DemandLine.MarketTerm</c> が
    /// 全件平均(<see cref="MarketReference.TryBuyer"/>)から作られていること
    /// (<see cref="MarketReferenceTests.BuyerReferenceAveragesEveryValidObservation"/> の
    /// 買い手側の値を使う)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-20)。</b><c>BuyerDemand.Build</c> が呼ぶ相場基準の取得を
    /// <c>MarketReference.TryBuyer</c> から <c>MarketReference.TrySeller</c> に変える変異
    /// (<c>hasOwnSettledPrice: true, ownSettledPrice: 0</c> を渡す)を当てたところ、
    /// <c>Assert.Equal(35, line.MarketTerm)</c>(許容乖離1000‰なので相場基準そのもの)が
    /// 実際値25(買い手側の値のまま)で失敗した(赤を確認、GDD02c §1.2 の天井が消える経路)。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void DemandUsesBuyerReferenceNotSeller()
    {
        const int SellerA = 1;
        const int SellerB = 2;

        // 許容乖離‰を1000にして、MarketTerm(= ApplyPermille(相場基準, 許容乖離‰))が
        // 相場基準そのものになるようにする(丸めの影響を避ける)。
        var definition = BuildDefinition(tolerancePermille: 1000);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });

        EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 10 * 24); // now = D10

        int headNpcId = world.Households[0].HeadNpcId;

        // MarketReferenceTests.BuyerReferenceAveragesEveryValidObservationと同じ観測。
        // 買い手側の平均 = CeilDiv(10+20+60+10, 4) = 25。売り手側の畳み込みなら35になる。
        world.Knowledge[headNpcId].Add(new PriceObservation
        {
            ItemId = Item.Firewood,
            LocationId = 0,
            Price = 10,
            SellerId = SellerA,
            ObservedAt = Tick.FromDays(5),
            Source = ObservationSource.Direct,
        });
        world.Knowledge[headNpcId].Add(new PriceObservation
        {
            ItemId = Item.Firewood,
            LocationId = 0,
            Price = 20,
            SellerId = SellerA,
            ObservedAt = Tick.FromDays(7),
            Source = ObservationSource.Direct,
        });
        world.Knowledge[headNpcId].Add(new PriceObservation
        {
            ItemId = Item.Firewood,
            LocationId = 0,
            Price = 60,
            SellerId = SellerA,
            ObservedAt = Tick.FromDays(9),
            Source = ObservationSource.Direct,
        });
        world.Knowledge[headNpcId].Add(new PriceObservation
        {
            ItemId = Item.Firewood,
            LocationId = 0,
            Price = 10,
            SellerId = SellerB,
            ObservedAt = Tick.FromDays(9),
            Source = ObservationSource.Direct,
        });

        var demand = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        var line = FindLine(demand, DemandPurpose.Necessity, Item.Firewood);

        Assert.True(line.HasMarketTerm);
        Assert.Equal(25, line.MarketTerm);
        Assert.NotEqual(35, line.MarketTerm); // 売り手側の畳み込み値ではない。
    }
}
