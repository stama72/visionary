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
    /// <remarks>
    /// <b>レビュー指摘R4。</b>入力を品目Id<b>降順</b>(薪5 → 小麦粉4)で並べる。昇順(旧)だと、
    /// 品目Id昇順ループをやめて <c>recipe.Inputs</c> をそのまま回す変異が同じ並びを出して
    /// 通ってしまう(タスク仕様テスト#23「recipe.Inputsの並びをそのまま使う」変異が判別できない)。
    /// </remarks>
    private static Recipe BakerLikeRecipe(int outputQuantity = 2) =>
        new(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = outputQuantity } },
            inputs: new[]
            {
                new ItemQuantity { ItemId = Item.Firewood, Quantity = 1 },
                new ItemQuantity { ItemId = Item.Flour, Quantity = 1 },
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

    /// <summary>
    /// 【核心】レビュー指摘R2。耐久(工具)行の目標在庫・予想在庫が耐久値(‰人日)で計算される。
    /// 工具1個・N=30(耐久値30000‰人日)・摩耗5000‰人日・目標在庫比500‰・世帯主の階層係数1000‰
    /// → 予想在庫25000、目標在庫15000。
    /// </summary>
    /// <remarks>
    /// <b>単位が‰人日になった(#96)。</b>旧(回数)の期待値25/15を、耐久値N×1000=30000で
    /// 揃えて1000倍(25000/15000)にした ── 式自体は変えていない。
    /// <para>
    /// <b>変異の実測(2026-09-21)。</b>予想在庫の計算を
    /// <c>household.WorkshopInventory[Item.Tools]</c>(個数のまま、耐久値へ変換しない変異)に
    /// 変えたところ、<c>Assert.Equal(25000, durableLine.ExpectedStock)</c> が実際値1で失敗した
    /// (赤を確認)。<c>- household.ToolWear</c> を外す変異では
    /// <c>Assert.Equal(25000, ...)</c> が実際値30000で失敗した(赤を確認)。変異はいずれも戻して
    /// 緑に復帰させた。
    /// </para>
    /// </remarks>
    [Fact]
    public void DurableLineIsMeasuredInDurabilityNotUnits()
    {
        var definition = BuildDefinition(
            toolTargetStockPermille: 500,
            rankCoefficientPermille: new[] { 1000, 600, 200 },
            toolLifeLaborDays: 30);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Tools] = 1;
        world.Households[0].ToolWear = 5000;

        var demand = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        var durableLine = FindLine(demand, DemandPurpose.Durable, Item.Tools);

        Assert.Equal(25000, durableLine.ExpectedStock);
        Assert.Equal(15000, durableLine.TargetStock);
    }

    /// <summary>
    /// 【核心】レビュー指摘R2。耐久の目標在庫は世帯主の階層係数を使う(構成員の階層ではない)。
    /// 世帯主 Master(1000‰)+ 徒弟 Apprentice(200‰)の混成世帯で、目標在庫が世帯主の係数(15000)
    /// で出る(徒弟の係数(3000)にはならない)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-21)。</b><c>world.Npcs[household.HeadNpcId].Rank</c> を
    /// <c>world.Npcs[household.MemberNpcIds[^1]].Rank</c>(末尾の構成員=徒弟の階層)に変える変異を
    /// 当てたところ、<c>Assert.Equal(15000, durableLine.TargetStock)</c> が実際値3000で失敗した
    /// (赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void DurableTargetUsesTheHeadRankNotAMemberRank()
    {
        var definition = BuildDefinition(
            toolTargetStockPermille: 500,
            rankCoefficientPermille: new[] { 1000, 600, 200 },
            toolLifeLaborDays: 30);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Apprentice }); // 先頭(世帯主)がMaster

        var demand = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        var durableLine = FindLine(demand, DemandPurpose.Durable, Item.Tools);

        // 世帯主(Master,1000‰)の係数で15000。徒弟(200‰)の係数を採ると3000になる。
        Assert.Equal(15000, durableLine.TargetStock);
    }

    /// <summary>
    /// 【核心】レビュー指摘R2。世帯主の階層係数‰が実際に目標在庫へ乗る(恒等元の1000‰では
    /// 「係数‰を掛け忘れる」変異が判別できないため、世帯主をApprentice(200‰)にする)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-21)。</b><c>DurableLineIsMeasuredInDurabilityNotUnits</c> /
    /// <c>DurableTargetUsesTheHeadRankNotAMemberRank</c> はどちらも世帯主がMaster(係数1000‰)
    /// であり、<c>ApplyPermille(x,1000) == x</c> のため「階層係数‰を掛けない」変異
    /// (<c>RankCoefficientPermille[headRank]</c> への<c>ApplyPermille</c>呼び出しを丸ごと外す)が
    /// 両テストとも緑のまま通っていた(赤を確認せずに見つけた)。世帯主をApprentice(200‰)に
    /// した本テストでその変異を当てたところ、<c>Assert.Equal(3000, durableLine.TargetStock)</c> が
    /// 実際値15000(係数を掛けなかった値)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void DurableTargetActuallyAppliesTheRankCoefficient()
    {
        var definition = BuildDefinition(
            toolTargetStockPermille: 500,
            rankCoefficientPermille: new[] { 1000, 600, 200 },
            toolLifeLaborDays: 30);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Apprentice });

        var demand = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        var durableLine = FindLine(demand, DemandPurpose.Durable, Item.Tools);

        // ApplyPermille(ApplyPermille(30000,500),200) = ApplyPermille(15000,200) = 3000。
        Assert.Equal(3000, durableLine.TargetStock);
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

    /// <summary>
    /// 【核心】レビュー指摘R3。<c>MarketReference.TryBuyer</c> へ渡す自己除外のIdは
    /// <c>household.Id</c> であって <c>household.HeadNpcId</c> ではない
    /// (TDD01 §3.2「取り違えを型で防げない」経路)。世帯主のNpcIdを世帯Idとわざと違える。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-21)。</b><c>BuyerDemand.Build</c> の
    /// <c>MarketReference.TryBuyer</c> 呼び出しの第3引数(<c>selfHouseholdId</c>)を
    /// <c>household.Id</c> から <c>household.HeadNpcId</c> に変える変異を当てたところ、
    /// <c>Assert.Equal(120, line.MarketTerm)</c> が実際値1199(<c>ApplyPermille(999,1200)</c>。
    /// 自己観測999が誤って生き残り、他の売り手の観測100が誤って除外された)で失敗した
    /// (赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void DemandExcludesTheHouseholdIdNotTheHeadNpcId()
    {
        const int HeadNpcId = 2; // household.Id(0)とわざと違える。

        var definition = BuildDefinition(tolerancePermille: 1200);
        var world = new World(npcCount: 3, householdCount: 1, itemCount: Item.Count);
        world.Npcs[HeadNpcId].Rank = NpcRank.Master;
        world.Households[0] = new HouseholdState(
            id: 0, districtId: 0, headNpcId: HeadNpcId, memberNpcIds: new[] { HeadNpcId }, itemCount: Item.Count);
        world.Households[0].Occupation = Occupation.Miller;

        EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 10 * 24);

        // 自世帯(SellerId=household.Id=0)の観測 → 除外されるべき。
        world.Knowledge[HeadNpcId].Add(new PriceObservation
        {
            ItemId = Item.Firewood,
            LocationId = 0,
            Price = 999,
            SellerId = 0,
            ObservedAt = world.Now.AddDays(-1),
            Source = ObservationSource.Direct,
        });
        // 世帯主のNpcId(2)をSellerIdとする観測 → この世界に household.Id=2 は存在しないので
        // 「他の売り手」であり、含まれるべき。
        world.Knowledge[HeadNpcId].Add(new PriceObservation
        {
            ItemId = Item.Firewood,
            LocationId = 0,
            Price = 100,
            SellerId = HeadNpcId,
            ObservedAt = world.Now.AddDays(-1),
            Source = ObservationSource.Direct,
        });

        var demand = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        var line = FindLine(demand, DemandPurpose.Necessity, Item.Firewood);

        // 相場基準 = 100(SellerId=0の自己観測999は除外)。MarketTerm = ApplyPermille(100,1200) = 120。
        Assert.True(line.HasMarketTerm);
        Assert.Equal(120, line.MarketTerm);
    }

    /// <summary>
    /// 【核心】レビュー指摘R6。取り置き・運転資金がともに正になる世帯で、
    /// <c>DemandLine.CashCap</c> が用途ごとの母数の段階(必需=流動資金 / 耐久・入力=流動資金-取り置き
    /// / 嗜好=流動資金-取り置き-運転資金)を反映していること。
    /// </summary>
    /// <remarks>
    /// <b>まず「嗜好の母数から運転資金を引かない」変異(<c>BuyerDemand.Build</c> の嗜好の行の
    /// <c>BuyerBudget.AvailableFunds</c> 呼び出しを <c>workingCapital: 0</c> に変える)を当てて
    /// <c>dotnet test</c> を回したところ、既存の324件はすべて緑のままだった(赤にならないことを
    /// 確認、2026-09-21)。<c>DemandLine.CashCap</c> を見るアサートが1件も無かったため。</b>
    /// そこで本テストを追加し、同じ変異を当て直したところ、
    /// <c>Assert.Equal(905, preferenceLine.CashCap)</c> が実際値940
    /// (運転資金35が引かれず、耐久・入力の母数(940)と同じ値になった)で失敗した(赤を確認)。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void CashCapReflectsTheStagedAvailableFundsPerPurpose()
    {
        var definition = BuildDefinition(beerConsumptionQty: 1);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].LiquidFunds = 1000;

        // 必需(パン)・生産の入力(小麦粉)にだけ観測を置き、取り置きと運転資金をともに正にする。
        SetReference(world, world.Households[0].HeadNpcId, Item.Bread, price: 20);
        SetReference(world, world.Households[0].HeadNpcId, Item.Flour, price: 7);

        var demand = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);

        Assert.Equal(60, demand.NecessityReserve);  // パンの目標3 × 観測20
        Assert.Equal(35, demand.WorkingCapital);     // 小麦粉の目標5 × 観測7

        var durableLine = FindLine(demand, DemandPurpose.Durable, Item.Tools);
        var inputLine = FindLine(demand, DemandPurpose.ProductionInput, Item.Flour);
        var preferenceLine = FindLine(demand, DemandPurpose.Preference, Item.Beer);

        // 耐久・入力: FloorDiv(1000 − 60, 1) = 940(運転資金は引かれない)。
        Assert.Equal(940, durableLine.CashCap);
        Assert.Equal(940, inputLine.CashCap);
        // 嗜好: FloorDiv(1000 − 60 − 35, 1) = 905(取り置きと運転資金の両方を引く)。
        Assert.Equal(905, preferenceLine.CashCap);
    }
}
