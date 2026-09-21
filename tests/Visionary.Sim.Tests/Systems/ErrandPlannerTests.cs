using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="ErrandPlanner"/>(GDD06 §3、#98 タスク仕様のテスト表)の検査。
/// </summary>
/// <remarks>
/// <b>需要リストは <see cref="BuyerDemand"/> を経由せず、<see cref="DemandLine"/> を直接組み立てる</b>
/// (<see cref="BuyerBudgetTests"/> と同じ方式)。<see cref="ErrandPlanner"/> の入力である
/// <see cref="HouseholdDemand"/> の中身を精密に制御できることが、GDD06 §3 の数値例をそのまま
/// 再現するために必要である。
/// </remarks>
public sealed class ErrandPlannerTests
{
    private const int ItemA = Item.Flour; // Occupation.Millerが出力する品目(primaryRecipe)。
    private const int ItemB = Item.Grain; // Occupation.Baker等(UnusedRecipe)が出力する品目。
    private const int PrimaryItem = Item.Timber; // どのレシピも出力しない品目(#11用)。

    private static WorldDefinition BuildDefinition(
        int travelHoursPerDistrict = 1,
        int disposableHours = 12,
        int[]? externalBuyPriceOverride = null,
        int[]? opportunityCostBaseByOccupation = null) =>
        EconomySystemTestFixtures.BuildDefinition(
            new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = ItemA, Quantity = 1 } },
                inputs: new[] { new ItemQuantity { ItemId = PrimaryItem, Quantity = 1 } },
                laborPermille: 1000),
            opportunityCostBaseByOccupation: opportunityCostBaseByOccupation ?? new[] { 1, 1, 1, 1, 1 },
            rankCoefficientPermille: new[] { 1000, 1000, 1000 },
            travelHoursPerDistrict: travelHoursPerDistrict,
            disposableHours: disposableHours,
            externalBuyPriceOverride: externalBuyPriceOverride);

    /// <summary>既定の外部買値(itemAとitemBだけ差し替え、他は既定のまま)。</summary>
    private static int[] BuildExternalBuyPrice(int itemAPrice = 1, int itemBPrice = 1)
    {
        var price = new int[Item.Count];
        price[ItemA] = itemAPrice;
        price[ItemB] = itemBPrice;
        return price;
    }

    /// <summary>
    /// 単一構成員の世帯を、買い手(区画<paramref name="buyerDistrictId"/>)を先頭に、
    /// 続けて<paramref name="sellers"/>(区画, 職業)の並びで作る。
    /// </summary>
    private static World BuildWorld(int buyerDistrictId, params (int DistrictId, Occupation Occupation)[] sellers)
    {
        int count = sellers.Length + 1;
        var world = new World(npcCount: count, householdCount: count, itemCount: Item.Count);

        world.Npcs[0].Rank = NpcRank.Master;
        world.Households[0] = new HouseholdState(
            id: 0, districtId: buyerDistrictId, headNpcId: 0, memberNpcIds: new[] { 0 }, itemCount: Item.Count);
        world.Households[0].Occupation = Occupation.Smith; // 買い手の職業は何でもよい(手組みのDemandLineが使う)。

        for (int i = 0; i < sellers.Length; i++)
        {
            int id = i + 1;
            world.Npcs[id].Rank = NpcRank.Master;
            world.Households[id] = new HouseholdState(
                id: id, districtId: sellers[i].DistrictId, headNpcId: id,
                memberNpcIds: new[] { id }, itemCount: Item.Count);
            world.Households[id].Occupation = sellers[i].Occupation;
        }

        return world;
    }

    private static DemandLine BuildLine(
        int itemId, int budget, int targetStock, int expectedStock, int baseValue, int cashCap = 1_000_000,
        DemandPurpose purpose = DemandPurpose.Necessity) =>
        new()
        {
            Purpose = purpose,
            ItemId = itemId,
            HasMarketTerm = false,
            MarketTerm = 0,
            CashCap = cashCap,
            HasProfitCap = false,
            ProfitCap = 0,
            TargetStock = targetStock,
            ExpectedStock = expectedStock,
            StockPressurePermille = BuyerBudget.StockPressurePermille(expectedStock, targetStock),
            BaseValue = baseValue,
            Budget = budget,
        };

    private static HouseholdDemand DemandOf(params DemandLine[] lines) => new() { Lines = lines };

    private static ErrandDelegate Delegate(int costPerHour, int laborPermille = 300) =>
        new() { NpcId = 0, CostPerHour = costPerHour, LaborPermille = laborPermille };

    /// <summary>世帯主(HeadNpcId=0)のKnowledgeへ観測を1件足す。</summary>
    private static void AddObservation(World world, int itemId, int sellerId, int price, Tick observedAt) =>
        world.Knowledge[0].Add(new PriceObservation
        {
            ItemId = itemId,
            LocationId = 0,
            Price = price,
            SellerId = sellerId,
            ObservedAt = observedAt,
            Source = ObservationSource.Direct,
        });

    /// <summary>
    /// 【核心】テスト表 #7。R以内の店に当日の売り注文(30)。同じ店の前日の観測を90、
    /// 床を90に置いても見積もりは30(w=50を挟んで travel/no-travel が入れ替わる)。
    /// </summary>
    /// <remarks>
    /// <b>設計:</b> w=50固定(BaseValue=50・StockPressurePermille=1000)。正しく当日の30を使えば
    /// 余剰&gt;0(行く)。誤って記憶90や床90を使うと <c>Errand.Surplus</c> の
    /// <c>willingness ≤ price</c> ガードで余剰が0になり(行かない)、行く/行かないが入れ替わる。
    /// </remarks>
    [Fact]
    public void EstimateUsesTodaysOfferWithinVisionRadius()
    {
        const int SellerDistrictId = 1; // District.Distance(0,1)=1(R以内)。
        const int SellerId = 1;

        var definition = BuildDefinition(externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 90));
        var world = BuildWorld(buyerDistrictId: 0, (SellerDistrictId, Occupation.Miller));
        world.Households[SellerId].WorkshopInventory[ItemA] = 10;
        world.Market[new MarketKey(ItemA, SellerId)] = 30; // 当日の売り注文。

        EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 3 * 24);
        AddObservation(world, ItemA, SellerId, price: 90, observedAt: Tick.Zero); // 前日以前の記憶。

        var line = BuildLine(ItemA, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 50);
        var planner = new ErrandPlanner(definition);

        var plan = planner.Plan(world, world.Households[0], DemandOf(line), Delegate(costPerHour: 1));

        Assert.Equal(new[] { SellerDistrictId }, plan.VisitedDistrictIds);
    }

    /// <summary>
    /// 【核心】テスト表 #8。R以内の店が当日の売り注文を出していない → 前日の観測(10)があっても、
    /// 床(10)が安くても、その店は候補に入らない。他に候補が無ければどこへも行かない。
    /// </summary>
    [Fact]
    public void EstimateDropsNearbyStoreWithoutTodaysOffer()
    {
        const int SellerDistrictId = 1; // R以内。
        const int SellerId = 1;

        // ItemAの床を高くする(1000)。#149で窓口が都市生産品も売るようになったため、低い床
        // (10)のままだと窓口の天井(ApplyPermille(10,2000)=20)がw(75)を下回り、「候補0件で
        // 行かない」はずの本テストが窓口(中心区画)へ行ってしまう。
        var definition = BuildDefinition(externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 1000));
        var world = BuildWorld(buyerDistrictId: 0, (SellerDistrictId, Occupation.Miller));
        world.Households[SellerId].WorkshopInventory[ItemA] = 10;
        // 当日の売り注文は無い(world.Marketに何も置かない)。

        EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 3 * 24);
        AddObservation(world, ItemA, SellerId, price: 10, observedAt: Tick.Zero); // 安い記憶(誘惑)。

        var line = BuildLine(ItemA, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 50);
        var planner = new ErrandPlanner(definition);

        var plan = planner.Plan(world, world.Households[0], DemandOf(line), Delegate(costPerHour: 1));

        Assert.Empty(plan.VisitedDistrictIds);
    }

    /// <summary>
    /// 【核心】テスト表 #9。Rの外の店について、3日前(90)と1日前(50)の観測 → 50(最新)を使う
    /// (平均・最古ではない)。当日(差0)の観測は使わない(使うと落ちるべき床のケースが行ってしまう)。
    /// </summary>
    /// <remarks>
    /// <b>別表E-4(3巡目の網羅パス)。</b>サブケースAは <c>targetStock: 1・expectedStock: 0</c> の
    /// 在庫圧力が1500‰であり、実際の <c>w = ApplyPermille(70, 1500) = 105</c> である
    /// (旧doc コメントは誤って「w=70」と書いていた)。旧費用(往復4時間×機会費用1=4)では
    /// 平均(70)でも <c>105 &gt; 70</c> で余剰が正になり行ってしまうため、「平均する」変異
    /// (<c>MarketReference</c> を流用して店ごとの差を潰す)を当てても赤にならなかった。
    /// <b>費用を20(=往復4時間×機会費用5)へ動かし、余剰(70)=17 ≤ 費用20 &lt; 余剰(50)=27</b>
    /// を満たす配置にした。
    /// <para>
    /// <b>変異の実測(2026-09-20、別表E-4)。</b>2件の観測の平均(70)を見積もり価格として使う変異
    /// を当てたところ、価値 = 17−20 = −3 ≤ 0 で <c>Assert.Equal(new[] { SellerDistrictId }, ...)</c>
    /// が実際値 <c>[]</c>(行かない)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </para>
    /// </remarks>
    [Fact]
    public void EstimateUsesTheLatestValidMemoryOutsideVisionRadius()
    {
        const int SellerDistrictId = 2; // 距離2(Rの外)。
        const int SellerId = 1;

        // サブケースA: 3日前(90)・1日前(50)。実際のw=105(ApplyPermille(70,1500)。<remarks>参照)。
        // 平均70→余剰17、最新50→余剰27。費用20を挟むことで平均/最新が行く/行かないを分ける。
        {
            var definition = BuildDefinition(externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 200));
            var world = BuildWorld(buyerDistrictId: 0, (SellerDistrictId, Occupation.Miller));
            world.Households[SellerId].WorkshopInventory[ItemA] = 10;

            EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 4 * 24);
            AddObservation(world, ItemA, SellerId, price: 90, observedAt: Tick.FromDays(1));
            AddObservation(world, ItemA, SellerId, price: 50, observedAt: Tick.FromDays(3));

            var line = BuildLine(ItemA, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 70);
            var planner = new ErrandPlanner(definition);

            // 距離2 → 往復4時間 × 機会費用5 = 費用20。
            var plan = planner.Plan(world, world.Households[0], DemandOf(line), Delegate(costPerHour: 5));

            Assert.Equal(new[] { SellerDistrictId }, plan.VisitedDistrictIds);
        }

        // サブケースB: 当日(差0)の観測しか無い → 除外され、有効な記憶0件で床(200、行かない)。
        {
            var definition = BuildDefinition(externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 200));
            var world = BuildWorld(buyerDistrictId: 0, (SellerDistrictId, Occupation.Miller));
            world.Households[SellerId].WorkshopInventory[ItemA] = 10;

            AddObservation(world, ItemA, SellerId, price: 10, observedAt: world.Now); // 差0(安い誘惑)。

            var line = BuildLine(ItemA, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 50);
            var planner = new ErrandPlanner(definition);

            var plan = planner.Plan(world, world.Households[0], DemandOf(line), Delegate(costPerHour: 1));

            Assert.Empty(plan.VisitedDistrictIds);
        }
    }

    /// <summary>
    /// テスト表 #10。Rの外・記憶なし → ExternalBuyPrice。当日の実際の提示価格が床より高くても
    /// 床で見積もる(距離の外は見えない)。
    /// </summary>
    [Fact]
    public void EstimateFallsBackToTheFloor()
    {
        const int SellerDistrictId = 2; // 距離2(Rの外)。
        const int SellerId = 1;

        // 床(30、安い)。当日の実際の提示価格は200(高い) ── Rの外なので見えない。
        var definition = BuildDefinition(externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 30));
        var world = BuildWorld(buyerDistrictId: 0, (SellerDistrictId, Occupation.Miller));
        world.Households[SellerId].WorkshopInventory[ItemA] = 10;
        world.Market[new MarketKey(ItemA, SellerId)] = 200;

        var line = BuildLine(ItemA, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 50);
        var planner = new ErrandPlanner(definition);

        var plan = planner.Plan(world, world.Households[0], DemandOf(line), Delegate(costPerHour: 1));

        // 床(30<w=50)を使えば行く。もし当日の実価格(200≥50)を誤って読めば行かない。
        Assert.Equal(new[] { SellerDistrictId }, plan.VisitedDistrictIds);
    }

    /// <summary>
    /// テスト表 #9(#38)。1次産品の需要行を持つ世帯の <c>VisitedDistrictIds</c> が中心(区画4)を
    /// 含む。都市内に売り手が居なくても例外は出ない。
    /// </summary>
    /// <remarks>
    /// <b>#38 で規則が反転した。</b>旧テスト「PrimaryItemLinesCreateNoErrand」は、窓口が
    /// まだ無かった時点での「1次産品はどこへも行かず、例外も出ない」を固定していた
    /// (都市内に売り手が居ないので、<see cref="ErrandPlanner.TryCheapestEstimate"/> は
    /// 常に候補0件だった)。窓口が候補に足された今、中心区画は毎日すべての1次産品を並べる
    /// (GDD02d §2.2)ので、買い手から見て中心への外出が価値を持つ ── 「行かない」から
    /// 「中心へ行く」へ規則が反転した。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(2026-09-21、<c>mutator</c> が使い捨てworktreeで測定、対象コミット
    /// <c>39e367a</c>、M-4)。</b><c>TryCheapestEstimate</c> の窓口の枝を消す変異は
    /// 期待どおり赤になった。
    /// </remarks>
    [Fact]
    public void PrimaryItemLinesCreateAnErrandToTheCentre()
    {
        var definition = BuildDefinition();
        var world = BuildWorld(buyerDistrictId: 0); // 売り手なし(窓口だけが候補)。

        var line = BuildLine(PrimaryItem, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 50);
        var planner = new ErrandPlanner(definition);

        var exception = Record.Exception(
            () => planner.Plan(world, world.Households[0], DemandOf(line), Delegate(costPerHour: 1)));

        Assert.Null(exception);

        var plan = planner.Plan(world, world.Households[0], DemandOf(line), Delegate(costPerHour: 1));
        Assert.Equal(new[] { District.ExternalMarketDistrictId }, plan.VisitedDistrictIds);
    }

    /// <summary>
    /// テスト表 #10(#38)。距離Rの外・記憶なしの世帯の窓口の見積もりが1
    /// (<see cref="ExternalMarket.UnknownPriceFloor"/>)。
    /// </summary>
    /// <remarks>
    /// 当日の外部売値(距離Rの外からは見えないはずの高値999)を誤って使えば、余剰が0になり
    /// 行かなくなる(GDD06 §3.1「距離Rの外からは見えない」違反)。0を返せば
    /// <see cref="BuyerBudget.Decide"/> が実効価格1以上の前提で投げる(タスク仕様)。
    /// </remarks>
    [Fact]
    public void WindowEstimateFallsBackToOneWithoutAMemory()
    {
        // 当日の外部売値をわざと高くする(999)。距離Rの外なのでこの値は見えないはず。
        // 1次産品(Miller/UnusedRecipeのどちらも出力しない品目)はすべて基準値1(既定と同じ)、
        // PrimaryItemだけ999にする ── 都市生産品(ItemA・ItemB)は0のまま(検証を満たす)。
        var externalSellPriceBase = new int[Item.Count];
        for (int itemId = 0; itemId < Item.Count; itemId++)
        {
            if (itemId != ItemA && itemId != ItemB)
            {
                externalSellPriceBase[itemId] = 1;
            }
        }

        externalSellPriceBase[PrimaryItem] = 999;

        var externalSellPriceSeasonPermille = new int[Item.Count][];
        for (int itemId = 0; itemId < Item.Count; itemId++)
        {
            externalSellPriceSeasonPermille[itemId] = new[] { 1000, 1000, 1000, 1000 };
        }

        var definition = EconomySystemTestFixtures.BuildDefinition(
            new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = ItemA, Quantity = 1 } },
                inputs: new[] { new ItemQuantity { ItemId = PrimaryItem, Quantity = 1 } },
                laborPermille: 1000),
            opportunityCostBaseByOccupation: new[] { 1, 1, 1, 1, 1 },
            rankCoefficientPermille: new[] { 1000, 1000, 1000 },
            travelHoursPerDistrict: 1,
            disposableHours: 12,
            externalBuyPriceOverride: BuildExternalBuyPrice(),
            externalSellPriceBaseOverride: externalSellPriceBase,
            externalSellPriceSeasonPermilleOverride: externalSellPriceSeasonPermille);

        var world = BuildWorld(buyerDistrictId: 0); // 距離2(中心。Rの外)。売り手なし・記憶なし。

        // baseValue=50・target=1・expected=0 → w=75。床(1)なら余剰74・費用4で行く。
        // 999(当日の実売値)を誤って使えば w<=999 で余剰0となり行かない。
        var line = BuildLine(PrimaryItem, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 50);
        var planner = new ErrandPlanner(definition);

        var plan = planner.Plan(world, world.Households[0], DemandOf(line), Delegate(costPerHour: 1));

        Assert.Equal(new[] { District.ExternalMarketDistrictId }, plan.VisitedDistrictIds);
    }

    /// <summary>
    /// 【核心】テスト表 #12(#149)。都市生産品(ItemA)でも窓口の見積もりが最安候補に入る
    /// (<c>TryCheapestEstimate</c> の品目ゲートが外れている)。都市内に売り手が居ない世界で、
    /// 窓口だけを頼りに中心区画へ外出することを確かめる。
    /// </summary>
    /// <remarks>
    /// <b>#38当時の規則(1次産品だけが窓口の候補になる)を反転する</b>(#149タスク仕様「作るもの3」)。
    /// <c>:242</c> の品目ゲートが残っていれば、都市内にも窓口にも候補が無く外出そのものが
    /// 起きない。
    /// </remarks>
    [Fact]
    public void WindowIsACandidateForCityGoods()
    {
        var definition = BuildDefinition();
        var world = BuildWorld(buyerDistrictId: 0); // 距離2(中心。Rの外)。売り手なし。

        // baseValue=50・target=1・expected=0 → w=75。床(1、既定の外部買値)なら余剰は大きく、
        // 費用(距離2の往復4時間×機会費用1=4)を上回って行く。
        var line = BuildLine(ItemA, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 50);
        var planner = new ErrandPlanner(definition);

        var plan = planner.Plan(world, world.Households[0], DemandOf(line), Delegate(costPerHour: 1));

        Assert.Equal(new[] { District.ExternalMarketDistrictId }, plan.VisitedDistrictIds);
    }

    /// <summary>
    /// 【核心】テスト表 #13(#149)。窓口の記憶も知覚(距離R以内)も無い買い手の、都市生産品の
    /// 窓口見積もりは <see cref="WorldDefinition.ExternalBuyPrice"/>(床。<b>1ではない</b>)。
    /// </summary>
    /// <remarks>
    /// 3段目を都市生産品でも <see cref="ExternalMarket.UnknownPriceFloor"/>(1)にする実装ミスは、
    /// 床を高くした本テストで顕在化する ── 1を使えば余剰が生まれて行ってしまう
    /// (タスク仕様、核心M-3)。
    /// </remarks>
    [Fact]
    public void UnknownWindowPriceOfCityGoodsUsesTheFloor()
    {
        // 床(外部買値)を高くする(999)。距離Rの外なので、誤って1(UnknownPriceFloor)を使えば
        // 余剰が生まれて行ってしまう。
        var definition = BuildDefinition(externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 999));
        var world = BuildWorld(buyerDistrictId: 0); // 距離2(中心。Rの外)。売り手なし・記憶なし。

        // baseValue=50・target=1・expected=0 → w=75。床(999)を正しく使えば余剰0・行かない。
        // 1(UnknownPriceFloor)を誤って使えば余剰74・費用4で行ってしまう。
        var line = BuildLine(ItemA, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 50);
        var planner = new ErrandPlanner(definition);

        var plan = planner.Plan(world, world.Households[0], DemandOf(line), Delegate(costPerHour: 1));

        Assert.Empty(plan.VisitedDistrictIds);
    }

    /// <summary>
    /// 別表R-2(レビュー1巡目)。買い手が中心から距離R以内に居るとき、窓口の見積もりは
    /// <b>その日の外部売値</b>である(GDD02d §2.2 見積もり3段の段1)。
    /// </summary>
    /// <remarks>
    /// <b>#10(<see cref="WindowEstimateFallsBackToOneWithoutAMemory"/>)は段3、
    /// #11(<see cref="WindowEstimateUsesTheMemoryBeforeTheFloor"/>)は段2 の検出器であり、
    /// どちらも買い手を区画0(距離2、Rの外)に置いているため段1 を一度も通らない。</b>
    /// 当日の外部売値(300)・前日の記憶(50)・床(1、<see cref="ExternalMarket.UnknownPriceFloor"/>)を
    /// すべて異なる値にし、w=75 を挟むことで3値のどれが使われたかを一意に判別できる配置にする
    /// (段1=300を正しく使えば w≤300 で余剰0、行かない。段1を誤って飛ばし記憶50や床1を使えば
    /// w&gt;価格で余剰が生まれ、行ってしまう)。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(2026-09-21、<c>mutator</c> が使い捨てworktreeで測定、対象コミット
    /// <c>39e367a</c>、M-9)。</b>段1(距離≤Rの枝)を消す変異は期待どおり赤になった。
    /// 落ちたのはこの1件だけ ── 段1を守っているのは本テストだけである。
    /// </remarks>
    [Fact]
    public void WindowEstimateUsesTodaysPriceWithinTheVisionRadius()
    {
        const int BuyerDistrictId = 1; // District.Distance(1,4)=1 ≤ VisionRadius(R以内)。

        // 1次産品はすべて基準値1(既定と同じ)、PrimaryItemだけ300(今日の外部売値)にする ──
        // 都市生産品(ItemA・ItemB)は0のまま(検証を満たす)。
        var externalSellPriceBase = new int[Item.Count];
        for (int itemId = 0; itemId < Item.Count; itemId++)
        {
            if (itemId != ItemA && itemId != ItemB)
            {
                externalSellPriceBase[itemId] = 1;
            }
        }

        externalSellPriceBase[PrimaryItem] = 300;

        var externalSellPriceSeasonPermille = new int[Item.Count][];
        for (int itemId = 0; itemId < Item.Count; itemId++)
        {
            externalSellPriceSeasonPermille[itemId] = new[] { 1000, 1000, 1000, 1000 };
        }

        var definition = EconomySystemTestFixtures.BuildDefinition(
            new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = ItemA, Quantity = 1 } },
                inputs: new[] { new ItemQuantity { ItemId = PrimaryItem, Quantity = 1 } },
                laborPermille: 1000),
            opportunityCostBaseByOccupation: new[] { 1, 1, 1, 1, 1 },
            rankCoefficientPermille: new[] { 1000, 1000, 1000 },
            travelHoursPerDistrict: 1,
            disposableHours: 12,
            externalBuyPriceOverride: BuildExternalBuyPrice(),
            externalSellPriceBaseOverride: externalSellPriceBase,
            externalSellPriceSeasonPermilleOverride: externalSellPriceSeasonPermille);

        var world = BuildWorld(buyerDistrictId: BuyerDistrictId); // 売り手なし(窓口だけが候補)。

        // 前日の記憶(50)を仕込む ── 段1を飛ばして段2へ落ちる実装ミスでも、床(1)を使う
        // 実装ミスとは異なる値になるようにする(3値がすべて異なることを保証する)。
        EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 3 * 24);
        AddObservation(
            world, PrimaryItem, HouseholdState.ExternalMarketSellerId, price: 50, observedAt: Tick.Zero);

        // baseValue=50・target=1・expected=0 → w=75。段1(300)を使えば w≤300 で余剰0、行かない。
        var line = BuildLine(PrimaryItem, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 50);
        var planner = new ErrandPlanner(definition);

        var plan = planner.Plan(world, world.Households[0], DemandOf(line), Delegate(costPerHour: 1));

        Assert.Empty(plan.VisitedDistrictIds);

        // 別表(続き)R-7(レビュー2巡目)。上の表明は`Assert.Empty`だけなので、段1の枝
        // (距離R以内→当日の外部売値)そのものが丸ごと消えても緑になる。同じ構成で
        // 外部売値をw(75)より低くすると中心へ行くことを、肯定形のサブケースとして足す。
        //
        // レビュー3巡目: このサブケースはM-9(段1の枝を消す変異)を分ける検出器ではない。
        // 記憶を仕込まない新しいworldを使うため、M-9を当てても床(1)<w(75)でやはり中心へ行き、
        // 緑のまま通る。M-9を分けているのは前半(上の`Assert.Empty`側、床(1)を使わせずに
        // 段1(300)を通させて行かせない構成)である。このサブケースが担うのは、段1の枝が
        // 丸ごと消えて上の`Assert.Empty`が空振りで緑になっていないこと(空振り防止)である。
        var externalSellPriceBaseBelowW = (int[])externalSellPriceBase.Clone();
        externalSellPriceBaseBelowW[PrimaryItem] = 50; // w(75)より低い。

        var definitionBelowW = EconomySystemTestFixtures.BuildDefinition(
            new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = ItemA, Quantity = 1 } },
                inputs: new[] { new ItemQuantity { ItemId = PrimaryItem, Quantity = 1 } },
                laborPermille: 1000),
            opportunityCostBaseByOccupation: new[] { 1, 1, 1, 1, 1 },
            rankCoefficientPermille: new[] { 1000, 1000, 1000 },
            travelHoursPerDistrict: 1,
            disposableHours: 12,
            externalBuyPriceOverride: BuildExternalBuyPrice(),
            externalSellPriceBaseOverride: externalSellPriceBaseBelowW,
            externalSellPriceSeasonPermilleOverride: externalSellPriceSeasonPermille);

        var worldBelowW = BuildWorld(buyerDistrictId: BuyerDistrictId);
        var lineBelowW = BuildLine(PrimaryItem, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 50);
        var plannerBelowW = new ErrandPlanner(definitionBelowW);

        var planBelowW = plannerBelowW.Plan(
            worldBelowW, worldBelowW.Households[0], DemandOf(lineBelowW), Delegate(costPerHour: 1));

        Assert.Equal(new[] { District.ExternalMarketDistrictId }, planBelowW.VisitedDistrictIds);
    }

    /// <summary>テスト表 #11(#38)。前日の観測があればその価格、当日の観測は使わない。</summary>
    /// <remarks>
    /// 記憶の段を飛ばす(常に床を使う)・当日(差0)の観測を使う、いずれの実装ミスでも
    /// サブケースの少なくとも一方が崩れる(タスク仕様)。
    /// </remarks>
    [Fact]
    public void WindowEstimateUsesTheMemoryBeforeTheFloor()
    {
        var definition = BuildDefinition();

        // サブケースA: 前日の観測(500)があれば、床(1)ではなくその価格を使う。
        // w=75、記憶500(>=w) → 余剰0、費用4 → 行かない。床(1)を誤って使えば余剰74で行ってしまう。
        {
            var world = BuildWorld(buyerDistrictId: 0); // 距離2(中心。Rの外)。

            EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 3 * 24);
            world.Knowledge[0].Add(new PriceObservation
            {
                ItemId = PrimaryItem,
                LocationId = District.ExternalMarketDistrictId,
                Price = 500,
                SellerId = HouseholdState.ExternalMarketSellerId,
                ObservedAt = Tick.Zero,
                Source = ObservationSource.Direct,
            });

            var line = BuildLine(PrimaryItem, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 50);
            var planner = new ErrandPlanner(definition);

            var plan = planner.Plan(world, world.Households[0], DemandOf(line), Delegate(costPerHour: 1));

            Assert.Empty(plan.VisitedDistrictIds);
        }

        // サブケースB: 当日(差0)の観測しか無い → 除外され、有効な記憶0件で床(1、行く)。
        {
            var world = BuildWorld(buyerDistrictId: 0);

            world.Knowledge[0].Add(new PriceObservation
            {
                ItemId = PrimaryItem,
                LocationId = District.ExternalMarketDistrictId,
                Price = 500,
                SellerId = HouseholdState.ExternalMarketSellerId,
                ObservedAt = world.Now, // 差0(安くはないが「今日」を使ってはいけないことを確かめる)。
                Source = ObservationSource.Direct,
            });

            var line = BuildLine(PrimaryItem, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 50);
            var planner = new ErrandPlanner(definition);

            var plan = planner.Plan(world, world.Households[0], DemandOf(line), Delegate(costPerHour: 1));

            Assert.Equal(new[] { District.ExternalMarketDistrictId }, plan.VisitedDistrictIds);
        }
    }

    /// <summary>
    /// 【核心】テスト表 #12。GDD06 §3の例。徒弟の機会費用4・距離2(往復4時間)→費用16。
    /// パン(相場54・基礎値65・目標6): 在庫6→余剰11で行かない、在庫4→余剰44で行く。
    /// </summary>
    [Fact]
    public void ErrandIsTakenOnlyWhenTheGainExceedsTheCost()
    {
        const int SellerDistrictId = 2; // 距離2 → 往復4時間(travelHoursPerDistrict=1)。
        const int SellerId = 1;

        var definition = BuildDefinition(externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 54));
        var errand = Delegate(costPerHour: 4); // 費用 = 4 × 4 = 16。

        World BuildScenario()
        {
            var world = BuildWorld(buyerDistrictId: 0, (SellerDistrictId, Occupation.Miller));
            world.Households[SellerId].WorkshopInventory[ItemA] = 10;
            return world;
        }

        var planner = new ErrandPlanner(definition);

        // 在庫6 → 余剰11 < 費用16 → 行かない。
        var staysHome = BuildScenario();
        var lineStock6 = BuildLine(ItemA, budget: 1, targetStock: 6, expectedStock: 6, baseValue: 65);
        var planStock6 = planner.Plan(staysHome, staysHome.Households[0], DemandOf(lineStock6), errand);
        Assert.Empty(planStock6.VisitedDistrictIds);

        // 在庫4 → 余剰44 > 費用16 → 行く。
        var goes = BuildScenario();
        var lineStock4 = BuildLine(ItemA, budget: 1, targetStock: 6, expectedStock: 4, baseValue: 65);
        var planStock4 = planner.Plan(goes, goes.Households[0], DemandOf(lineStock4), errand);
        Assert.Equal(new[] { SellerDistrictId }, planStock4.VisitedDistrictIds);
    }

    /// <summary>
    /// 【核心】テスト表 #13。自区画と遠方に同じ品目を同じ提示価格で売る店 → 差が0なので行かない。
    /// 自区画の店の価格だけを上げると行く。
    /// </summary>
    [Fact]
    public void ErrandValueIsMeasuredAgainstTheHomeDistrict()
    {
        const int HomeDistrictId = 0;
        const int HomeSellerId = 1;
        const int DistantDistrictId = 2;
        const int DistantSellerId = 2;

        var errand = Delegate(costPerHour: 4); // 費用 = 4 × 4 = 16。
        var line = BuildLine(ItemA, budget: 1, targetStock: 6, expectedStock: 4, baseValue: 65); // w=76。

        World BuildScenario(int homePrice)
        {
            var definition0 = BuildDefinition(externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 54));
            var world = BuildWorld(
                buyerDistrictId: HomeDistrictId,
                (HomeDistrictId, Occupation.Miller),
                (DistantDistrictId, Occupation.Miller));
            world.Households[HomeSellerId].WorkshopInventory[ItemA] = 10;
            world.Households[DistantSellerId].WorkshopInventory[ItemA] = 10;
            world.Market[new MarketKey(ItemA, HomeSellerId)] = homePrice; // 自区画(距離0)は当日の市場。
            // 遠方(距離2)は床(54)を使う ── definition0の床がそのまま見積もりになる。
            return world;
        }

        var definition = BuildDefinition(externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 54));
        var planner = new ErrandPlanner(definition);

        // 自区画も54(同値) → 差0 → 行かない。
        var sameWorld = BuildScenario(homePrice: 54);
        var samePlan = planner.Plan(sameWorld, sameWorld.Households[0], DemandOf(line), errand);
        Assert.Empty(samePlan.VisitedDistrictIds);

        // 自区画を100(w=76以上、余剰0)に上げる → 遠方(54、余剰44)が上回り、行く。
        var raisedWorld = BuildScenario(homePrice: 100);
        var raisedPlan = planner.Plan(raisedWorld, raisedWorld.Households[0], DemandOf(line), errand);
        Assert.Equal(new[] { DistantDistrictId }, raisedPlan.VisitedDistrictIds);
    }

    /// <summary>
    /// 【核心】テスト表 #14。前半: 2つの遠方区画が同じ安値で同じ品目を売る → 1区画だけ行く
    /// (2つ目は差0−費用&lt;0。同値はId最小)。後半(訂正後、A-1): 1周目で選ばれなかった区画に
    /// しか無い別品目の余剰が費用を超えるときだけ、2周目でその区画も追加される。
    /// 追加(別表E-2): 両方の遠方区画が同じ品目(item A)を売り、district2のほうが厳密に安いとき、
    /// 「1周目で見積もりが得られた行を需要リストから外す」という却下済み設計が黙って戻っても、
    /// 後半のケースだけでは検出できないことを埋める。
    /// </summary>
    /// <remarks>
    /// <b>単一品目では2区画目は構造的に起こらない。</b>同じ品目を2区画が売るとき、1周目は
    /// 全候補の中の真の最良(argmax)を選ぶ。2周目でもう一方を足す条件を整理すると
    /// <c>−Errand.Cost(1周目の往復,…) ≤ 0</c> に帰着し(<c>Errand.Cost</c>は非負)、恒に成り立たない。
    /// <b>後半は別品目(item B)が1周目で選ばれなかった区画にしか無いケースで確かめる</b> ──
    /// item Bはitem Aと競合しないので、2周目の増分はitem B単独の余剰そのものになる。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(2026-09-20)。</b>基準(baseline)の走査条件を
    /// <c>districtId == buyer.DistrictId || visitedDistrictIds.Contains(districtId)</c> から
    /// <c>districtId == buyer.DistrictId</c>(差の相手を自区画に固定)へ戻す変異を当てたところ、
    /// 前半(<c>Assert.Equal(new[] { District1 }, plan.VisitedDistrictIds)</c>)が実際値
    /// <c>[District1, District2]</c>(同じ余剰を2度取り、2区画とも行ってしまう)で失敗し、
    /// <c>ErrandPicksTheLowestDistrictIdOnTies</c>(#16)も同様の理由で赤になった(赤を確認)。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    /// <remarks>
    /// <b>別表E-2。</b>後半(item B専用のdistrict2)は、item Bが1周目で一度も見積もられていない
    /// ので「1周目で見積もりが得られた行を需要リストから外す」という却下済み設計を戻しても
    /// 影響を受けない(3巡目の網羅パスが指摘)。<b>追加のブロックは district1・district2の
    /// 両方にitem Aを置き(district2のほうが安い)、この却下済み設計が戻ると2周目の
    /// district2評価からitem Aの行が消える</b>ことを直接突く。district1はitem B(1周目の
    /// 主動因)も売る ── item A単独では「1周目の勝者が2周目でも安値によって改善されうる」
    /// ことが上の証明と同じ理由で構造的に起こらないため、district1の勝利を別品目で確保する
    /// 必要がある。
    /// <para>
    /// <b>変異の実測(2026-09-20、別表E-2)。</b>1周目で選んだ区画で見積もりが得られた行
    /// (item Bとitem A、どちらもdistrict1で売っている)を2周目以降の需要リストから除く変異を
    /// 当てたところ、2周目のdistrict2評価からitem Aの行が消えてitem Bも売っていないため
    /// 価値が−16(費用のみ)になり、<c>Assert.Equal(new[] { District1, District2 }, ...)</c> が
    /// 実際値 <c>[District1]</c> で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </para>
    /// </remarks>
    [Fact]
    public void SecondErrandIsMeasuredAgainstTheDistrictsAlreadyChosen()
    {
        const int District1 = 2; // 距離2。
        const int District2 = 6; // 距離2(dist(0,6)=2)。
        const int Seller1Id = 1;
        const int Seller2Id = 2;

        var errand = Delegate(costPerHour: 4); // 距離2 → 費用16。
        var line = BuildLine(ItemA, budget: 1, targetStock: 6, expectedStock: 4, baseValue: 65); // w=76。

        World BuildScenario()
        {
            var world = BuildWorld(
                buyerDistrictId: 0, (District1, Occupation.Miller), (District2, Occupation.Miller));
            world.Households[Seller1Id].WorkshopInventory[ItemA] = 10;
            world.Households[Seller2Id].WorkshopInventory[ItemA] = 10;
            return world;
        }

        // 前半: 両区画とも床30(同値) → 1区画だけ(district1、Id最小)。
        {
            var definition = BuildDefinition(externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 30));
            var world = BuildScenario();

            var planner = new ErrandPlanner(definition);
            var plan = planner.Plan(world, world.Households[0], DemandOf(line), errand);

            Assert.Equal(new[] { District1 }, plan.VisitedDistrictIds);
        }

        // 後半: district1(item A、床54、余剰44)が1周目で必ず選ばれる。district2にだけ
        // item B(target6/expected4/baseValue65 ── item Aと同じ形。w=76)を持たせ、その床価格を
        // 動かして「district2の余剰(=item Aと競合しないので2周目の増分そのもの)が費用16を
        // 超えるときだけdistrict2も追加される」ことを確かめる(価格60→余剰16=費用と同値で
        // 価値0となり追加されない。価格58→余剰27で価値11となり追加される)。
        //
        // 期待値の変更ではなく配置の変更(2026-09-21、#149)。窓口は全品目を並べる
        // ようになったため、距離2(district1・district2と同じ費用16)から item A と item B を
        // "1回の訪問"でまとめて買える窓口(仮想の区画4)が候補に加わる。買い手に窓口の記憶が
        // 無いと3段目(未知価格の床)が使われ、それは district1・district2 の床(54・58/60)と
        // 同じ導出元になるため、窓口が2品目分の余剰を1回の費用で総取りしてしまい、
        // 本テストが見たい「district1→district2の2周目追加」の分岐に窓口が一度も入らなくなる
        // (判別力が窓口に吸収される)。<b>したがって買い手に窓口の値の記憶(高値999)を
        // 持たせ、窓口を実質的に候補から外す</b> ── これは district1・district2 の床(仕様値)を
        // 動かさない配置の変更であり、断定そのものは変えていない。
        World BuildTwoItemScenario()
        {
            var world = BuildWorld(
                buyerDistrictId: 0, (District1, Occupation.Miller), (District2, Occupation.Baker));
            world.Households[Seller1Id].WorkshopInventory[ItemA] = 10;
            world.Households[Seller2Id].WorkshopInventory[ItemB] = 10;

            // 窓口(距離2、Rの外)の記憶を高値(999)にし、床基準の未知価格見積もりに
            // 頼らせない(district1/district2の床54・58/60より十分高く、窓口が候補として
            // 選ばれることはなくなる)。
            EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 3 * 24);
            AddObservation(world, ItemA, HouseholdState.ExternalMarketSellerId, price: 999, observedAt: Tick.Zero);
            AddObservation(world, ItemB, HouseholdState.ExternalMarketSellerId, price: 999, observedAt: Tick.Zero);

            return world;
        }

        var lineB = BuildLine(ItemB, budget: 1, targetStock: 6, expectedStock: 4, baseValue: 65); // w=76。

        // district2の余剰16(=費用16。価値0。等号は入らない) → district2は追加されない。
        {
            var definition = BuildDefinition(
                externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 54, itemBPrice: 60));
            var world = BuildTwoItemScenario();

            var planner = new ErrandPlanner(definition);
            var plan = planner.Plan(world, world.Households[0], DemandOf(line, lineB), errand);

            Assert.Equal(new[] { District1 }, plan.VisitedDistrictIds);
        }

        // district2の余剰27(費用16を超える) → district1(1周目)に続けてdistrict2も追加される。
        {
            var definition = BuildDefinition(
                externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 54, itemBPrice: 58));
            var world = BuildTwoItemScenario();

            var planner = new ErrandPlanner(definition);
            var plan = planner.Plan(world, world.Households[0], DemandOf(line, lineB), errand);

            Assert.Equal(new[] { District1, District2 }, plan.VisitedDistrictIds);
        }

        // 追加(別表E-2): district1・district2の両方がitem Aを売り、district2のほうが厳密に
        // 安い(70→50)。district1はitem B(床40、余剰108)も売り、これが1周目の主動因になる
        // (item A単独ではdistrict1は1周目に勝てない ── 上のremarksの証明のとおり)。
        // district1・district2はいずれも距離2(Rの外)なので、item Aの見積もりは記憶で与える
        // (床は品目単位なので、同じ品目に区画ごと異なる価格を表現できない)。
        {
            const int SellerBId = 1;     // district1: item B(1周目の主動因、床40)。
            const int SellerANearId = 2; // district1: item A(高値70)。
            const int SellerAFarId = 3;  // district2: item A(安値50)。

            var definition = BuildDefinition(
                externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 999, itemBPrice: 40));
            var world = BuildWorld(
                buyerDistrictId: 0,
                (District1, Occupation.Baker),
                (District1, Occupation.Miller),
                (District2, Occupation.Miller));

            world.Households[SellerBId].WorkshopInventory[ItemB] = 10;
            world.Households[SellerANearId].WorkshopInventory[ItemA] = 10;
            world.Households[SellerAFarId].WorkshopInventory[ItemA] = 10;

            EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 3 * 24);
            AddObservation(world, ItemA, SellerANearId, price: 70, observedAt: Tick.Zero);
            AddObservation(world, ItemA, SellerAFarId, price: 50, observedAt: Tick.Zero);

            var lineDriver = BuildLine(ItemB, budget: 1, targetStock: 6, expectedStock: 4, baseValue: 65);
            var lineShared = BuildLine(ItemA, budget: 1, targetStock: 6, expectedStock: 4, baseValue: 65); // w=76。

            var planner = new ErrandPlanner(definition);
            var plan = planner.Plan(world, world.Households[0], DemandOf(lineDriver, lineShared), errand);

            // 1周目: district1(item B余剰108 + item A余剰3 − 費用16 = 95)がdistrict2
            // (item A余剰52 − 費用16 = 36)に勝つ。2周目: district1に既にいる状態で
            // district2のitem Aの増分(52−3=49)が費用16を上回る(価値33) → district2も追加。
            Assert.Equal(new[] { District1, District2 }, plan.VisitedDistrictIds);
        }
    }

    /// <summary>
    /// 【核心】テスト表 #15。前半(境界): 往復時間がTを超えると外出そのものが除外され、
    /// ちょうどTなら除外されない(等号は入る)。後半(訂正後、A-1): T=12。距離4(往復8)と
    /// 距離3(往復6)がともに価値&gt;0でも、合計14でTを超えるため距離4だけ。距離4と距離2
    /// (往復4)なら合計12(等号が入る)で両方行く。
    /// </summary>
    [Fact]
    public void ErrandStopsAtTheDisposableHoursCap()
    {
        const int FarDistrictId = 8; // 距離4 → travelHoursPerDistrict=1で往復8時間。
        const int FarSellerId = 1;

        var errand = Delegate(costPerHour: 1);
        // baseValue=1000,target=1,expected=0 → w=1500,q=2,price=10 → 余剰1490(費用を大きく
        // 上回る値)。Tの境界だけで行く/行かないが決まるようにする。
        var line = BuildLine(ItemA, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 1000);

        // 配置の変更(2026-09-21、#149)。窓口は距離2(往復4時間)にあり、記憶が無ければ
        // 3段目(未知価格の床)= FarDistrictIdの床(10)と同値の見積もりになる。往復4時間はT=7の
        // 内側なので、窓口が「Tの境界で除外されるはずの遠い区画」の代わりに選ばれてしまい、
        // 本テストが見たい「Tを超えたら除外される」分岐に一度も入らなくなる(判別力が窓口に
        // 吸収される)。<b>買い手に窓口の値の記憶(高値5000。w=1500を大きく上回り、窓口経由の
        // 余剰を0にする)を持たせ、窓口を実質的に候補から外す</b> ── FarDistrictIdの床(10、
        // 仕様値ではなくテストの構成値)は動かさない。
        World BuildScenario()
        {
            var world = BuildWorld(buyerDistrictId: 0, (FarDistrictId, Occupation.Miller));
            world.Households[FarSellerId].WorkshopInventory[ItemA] = 10;

            EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 3 * 24);
            AddObservation(world, ItemA, HouseholdState.ExternalMarketSellerId, price: 5000, observedAt: Tick.Zero);

            return world;
        }

        // T=7: 往復8時間 > 7 → 除外(価値がどれほど大きくても行かない)。
        {
            var definition = BuildDefinition(
                disposableHours: 7, externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 10));
            var world = BuildScenario();

            var planner = new ErrandPlanner(definition);
            var plan = planner.Plan(world, world.Households[0], DemandOf(line), errand);

            Assert.Empty(plan.VisitedDistrictIds);
        }

        // T=8: 往復8時間 = 8(等号は入る) → 除外されない。
        {
            var definition = BuildDefinition(
                disposableHours: 8, externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 10));
            var world = BuildScenario();

            var planner = new ErrandPlanner(definition);
            var plan = planner.Plan(world, world.Households[0], DemandOf(line), errand);

            Assert.Equal(new[] { FarDistrictId }, plan.VisitedDistrictIds);
        }

        // 後半: 距離4はitem A(床10、余剰1490)で1周目に必ず選ばれる(圧倒的に大きい)。
        // もう一方の区画は別品目item B(target6/expected4/baseValue65。w=76、床54で余剰44)を
        // 持ち、それ自体の価値は正だが、往復時間の合計がTを超えるときだけ除外されることを
        // 確かめる(price/valueではなくTの検査で落ちることを直接切り分ける)。
        const int MediumDistrictId = 5; // 距離3 → 往復6時間。
        const int CloseDistrictId = 2;  // 距離2 → 往復4時間。
        const int OtherSellerId = 2;    // BuildWorldは2番目の売り手にId=2を割り当てる。

        var lineB = BuildLine(ItemB, budget: 1, targetStock: 6, expectedStock: 4, baseValue: 65);

        World BuildTwoDistrictScenario(int otherDistrictId)
        {
            var world = BuildWorld(
                buyerDistrictId: 0, (FarDistrictId, Occupation.Miller), (otherDistrictId, Occupation.Baker));
            world.Households[FarSellerId].WorkshopInventory[ItemA] = 10;
            world.Households[OtherSellerId].WorkshopInventory[ItemB] = 10;

            // 上のBuildScenarioと同じ理由(#149)で窓口を候補から外す(item A・item Bとも)。
            EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 3 * 24);
            AddObservation(world, ItemA, HouseholdState.ExternalMarketSellerId, price: 5000, observedAt: Tick.Zero);
            AddObservation(world, ItemB, HouseholdState.ExternalMarketSellerId, price: 5000, observedAt: Tick.Zero);

            return world;
        }

        // T=12、距離4(往復8)と距離3(往復6) → 合計14でTを超える → 距離4だけ。
        {
            var definition = BuildDefinition(
                disposableHours: 12,
                externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 10, itemBPrice: 54));
            var world = BuildTwoDistrictScenario(MediumDistrictId);

            var planner = new ErrandPlanner(definition);
            var plan = planner.Plan(world, world.Households[0], DemandOf(line, lineB), errand);

            Assert.Equal(new[] { FarDistrictId }, plan.VisitedDistrictIds);
        }

        // T=12、距離4(往復8)と距離2(往復4) → 合計12(等号が入る) → 両方行く。
        {
            var definition = BuildDefinition(
                disposableHours: 12,
                externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 10, itemBPrice: 54));
            var world = BuildTwoDistrictScenario(CloseDistrictId);

            var planner = new ErrandPlanner(definition);
            var plan = planner.Plan(world, world.Households[0], DemandOf(line, lineB), errand);

            Assert.Equal(new[] { FarDistrictId, CloseDistrictId }, plan.VisitedDistrictIds);
        }
    }

    /// <summary>
    /// テスト表 #16。価値が等しい2区画 → 区画Idが小さい方(距離も等しくして費用で差が付かない
    /// ようにする)。
    /// </summary>
    [Fact]
    public void ErrandPicksTheLowestDistrictIdOnTies()
    {
        const int HomeDistrictId = 4; // 中心。距離1の区画が複数ある。
        const int SmallDistrictId = 1; // 距離1。
        const int LargeDistrictId = 3; // 距離1。
        const int SmallSellerId = 1;
        const int LargeSellerId = 2;

        // ItemAの床を高くする(1000)。買い手が中心区画(HomeDistrictId)に住むため、#149で窓口が
        // 都市生産品も売るようになったことで、既定の床(1、窓口の天井=2)だと窓口が無条件の
        // 最安候補になり、SmallDistrictId/LargeDistrictIdのどちらにも行かなくなる
        // (窓口は自区画で外出なしに使えるため)。
        var definition = BuildDefinition(externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 1000));
        var world = BuildWorld(
            buyerDistrictId: HomeDistrictId,
            (SmallDistrictId, Occupation.Miller),
            (LargeDistrictId, Occupation.Miller));
        world.Households[SmallSellerId].WorkshopInventory[ItemA] = 10;
        world.Households[LargeSellerId].WorkshopInventory[ItemA] = 10;
        world.Market[new MarketKey(ItemA, SmallSellerId)] = 30; // ともにR以内・同値。
        world.Market[new MarketKey(ItemA, LargeSellerId)] = 30;

        var line = BuildLine(ItemA, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 1000);
        var planner = new ErrandPlanner(definition);

        var plan = planner.Plan(world, world.Households[0], DemandOf(line), Delegate(costPerHour: 1));

        Assert.Equal(new[] { SmallDistrictId }, plan.VisitedDistrictIds);
    }

    /// <summary>
    /// 【核心】テスト表 #17。行き先の売り手のWorkshopInventoryを0にしても、VisitedDistrictIdsと
    /// LaborLossPermilleが変わらない(買えないだけ)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-20)。</b><c>TryCheapestEstimate</c> の店の候補判定に
    /// <c>seller.WorkshopInventory[itemId] &lt;= 0</c> なら候補から外す分岐(計画が販売在庫を見て
    /// しまう)を足したところ、本テストが在庫あり側で <c>VisitedDistrictIds = [2]</c>、
    /// 在庫なし側で <c>[]</c> になり一致せず失敗した(赤を確認。区画間の価格差が機構の帰結か
    /// 走査順の産物かを切り分けられなくなる経路)。<c>ErrandGainIsNeverNegative</c>(#29)も同じ
    /// 分岐で在庫0の遠方売り手が候補から消え、期待した区画に行かなくなって赤になった。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ErrandPlanIgnoresTheSellersStock()
    {
        const int SellerDistrictId = 2;
        const int SellerId = 1;

        var definition = BuildDefinition(externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 54));
        var errand = Delegate(costPerHour: 4);
        var line = BuildLine(ItemA, budget: 1, targetStock: 6, expectedStock: 4, baseValue: 65);
        var planner = new ErrandPlanner(definition);

        var worldWithStock = BuildWorld(buyerDistrictId: 0, (SellerDistrictId, Occupation.Miller));
        worldWithStock.Households[SellerId].WorkshopInventory[ItemA] = 100;
        var planWithStock = planner.Plan(worldWithStock, worldWithStock.Households[0], DemandOf(line), errand);

        var worldWithoutStock = BuildWorld(buyerDistrictId: 0, (SellerDistrictId, Occupation.Miller));
        worldWithoutStock.Households[SellerId].WorkshopInventory[ItemA] = 0;
        var planWithoutStock =
            planner.Plan(worldWithoutStock, worldWithoutStock.Households[0], DemandOf(line), errand);

        Assert.Equal(planWithStock.VisitedDistrictIds, planWithoutStock.VisitedDistrictIds);
        Assert.Equal(planWithStock.LaborLossPermille, planWithoutStock.LaborLossPermille);
        Assert.NotEmpty(planWithStock.VisitedDistrictIds); // 前提(実際に外出している)を確かめる。
    }

    /// <summary>
    /// 【核心】テスト表 #19。親方(階層係数1000‰・労働力係数1000‰)と徒弟(階層係数200‰・
    /// 労働力係数300‰)の世帯 → 委託先は徒弟で、損失は300‰基準(階層係数200‰で数えると2/3になる)。
    /// </summary>
    [Fact]
    public void ErrandLaborLossUsesTheDelegatesLaborCoefficient()
    {
        const int SellerDistrictId = 1; // 距離1。travelHours=2。
        const int SellerId = 1;

        var definition = EconomySystemTestFixtures.BuildDefinition(
            new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = ItemA, Quantity = 1 } },
                inputs: new[] { new ItemQuantity { ItemId = PrimaryItem, Quantity = 1 } },
                laborPermille: 1000),
            laborPermilleByRank: new[] { 1000, 800, 300 }, // Master, Journeyman, Apprentice
            rankCoefficientPermille: new[] { 1000, 600, 200 },
            opportunityCostBaseByOccupation: new[] { 5, 5, 5, 5, 5 },
            travelHoursPerDistrict: 1,
            disposableHours: 12,
            // #149で窓口が都市生産品(ItemA)も売るようになったため、床を高くして窓口の天井
            // (ApplyPermille(床,2000))を都市内の提示価格(30)より十分高く保つ ── 低い床のままだと
            // 窓口が中心区画(距離2)を経由して都市内(距離1)より安く見え、本テストが検証したい
            // 委託先の労働力係数(徒弟か否か)ではなく店選択の勝敗で行き先が決まってしまう。
            externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 1000));

        var world = new World(npcCount: 3, householdCount: 2, itemCount: Item.Count);
        world.Npcs[0].Rank = NpcRank.Master;
        world.Npcs[1].Rank = NpcRank.Apprentice;
        world.Npcs[2].Rank = NpcRank.Master;

        world.Households[0] = new HouseholdState(
            id: 0, districtId: 0, headNpcId: 0, memberNpcIds: new[] { 0, 1 }, itemCount: Item.Count);
        world.Households[0].Occupation = Occupation.Smith;

        world.Households[1] = new HouseholdState(
            id: 1, districtId: SellerDistrictId, headNpcId: 2, memberNpcIds: new[] { 2 }, itemCount: Item.Count);
        world.Households[1].Occupation = Occupation.Miller;
        world.Households[1].WorkshopInventory[ItemA] = 10;
        world.Market[new MarketKey(ItemA, SellerId)] = 30;

        var errand = OpportunityCost.SelectErrandDelegate(definition, world, world.Households[0]);
        Assert.Equal(1, errand.NpcId); // 徒弟が委託先。
        Assert.Equal(300, errand.LaborPermille);

        var line = BuildLine(ItemA, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 1000);
        var planner = new ErrandPlanner(definition);

        var plan = planner.Plan(world, world.Households[0], DemandOf(line), errand);

        Assert.Equal(new[] { SellerDistrictId }, plan.VisitedDistrictIds);
        // CeilDiv(300×2,12) = 50。RankCoefficientPermille(200)で数えるとCeilDiv(200×2,12) = 34。
        Assert.Equal(50, plan.LaborLossPermille);
    }

    /// <summary>
    /// 別表E-3。テスト表 #20 を <see cref="ErrandPlanner.Plan"/> 越しに確かめる。T=12・労働力係数
    /// 100‰で、往復2時間(距離1)と往復4時間(距離2)の2区画へ行く計画が成立し、
    /// <c>plan.LaborLossPermille</c> が「外出ごとに切り上げてから合計する」
    /// (<c>CeilDiv(200,12)+CeilDiv(400,12)=17+34=51</c>)であって、「往復時間を先に合計してから
    /// 1回だけ切り上げる」(<c>CeilDiv(600,12)=50</c>)ではないことを確かめる。
    /// </summary>
    /// <remarks>
    /// <b>置き場所について。</b><see cref="ErrandTests.ErrandLaborLossCeilsEachTripSeparately"/> は
    /// <see cref="Errand.LaborLossPermille"/> を2回直接呼ぶだけで、製品コードの集計
    /// (<see cref="ErrandPlanner"/> の周回)を一度も通らない ──
    /// <c>Σ_d CeilDiv(労働力係数‰×往復_d,T)</c> を <c>CeilDiv(労働力係数‰×Σ_d 往復_d,T)</c> に
    /// 変える変異を当てても動かない(3巡目の網羅パスの指摘、別表E-3)。<b>労働力係数300‰と
    /// T=12の組では2区画の往復時間(2+4=6・2+8=10 等)のいずれも12の約数になり、2通りの数え方が
    /// 恒に一致してしまう</b>ので、労働力係数を100‰へ動かしてこの一致を崩した。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(2026-09-20、別表E-3)。</b><c>ErrandPlanner</c> の労働損失の集計を
    /// <c>Σ_d CeilDiv(labor×travelHours_d,T)</c> から <c>CeilDiv(labor×Σ_d travelHours_d,T)</c>
    /// へ変える変異を当てたところ、期待値51に対し実際値50(<c>CeilDiv(600,12)</c>)で失敗した
    /// (赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ErrandLaborLossCeilsEachTripSeparatelyThroughThePlanner()
    {
        const int NearDistrictId = 1; // 距離1(R以内) → 往復2時間。
        const int FarDistrictId = 2;  // 距離2(Rの外) → 往復4時間。
        const int NearSellerId = 1;
        const int FarSellerId = 2;

        // #149で窓口が都市生産品も売るようになったため、ItemAの床は高いまま保つ(既定の1だと
        // 窓口の天井(ApplyPermille(1,2000)=2)がNearの当日価格10を下回り、店選択の勝敗で
        // 行き先が変わってしまう)。ItemBの床(10)はFarの見積もり(Rの外・記憶なしで床を使う)の
        // 材料としてそのまま使うので動かさない。
        var definition = BuildDefinition(
            externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 1000, itemBPrice: 10));
        var world = BuildWorld(
            buyerDistrictId: 0, (NearDistrictId, Occupation.Miller), (FarDistrictId, Occupation.Baker));
        world.Households[NearSellerId].WorkshopInventory[ItemA] = 10;
        world.Households[FarSellerId].WorkshopInventory[ItemB] = 10;
        world.Market[new MarketKey(ItemA, NearSellerId)] = 10; // R以内なので当日の市場を使う。

        var lineA = BuildLine(ItemA, budget: 1, targetStock: 6, expectedStock: 4, baseValue: 65); // w=76。
        var lineB = BuildLine(ItemB, budget: 1, targetStock: 6, expectedStock: 4, baseValue: 65);  // w=76。
        var errand = Delegate(costPerHour: 1, laborPermille: 100);
        var planner = new ErrandPlanner(definition);

        var plan = planner.Plan(world, world.Households[0], DemandOf(lineA, lineB), errand);

        // 1周目: near(余剰264−費用2=262)がfar(余剰264−費用4=260)に勝つ。2周目:
        // farのitem Bの増分(264)が費用4を上回る(価値260) → 両方行く(合計往復6≤T=12)。
        Assert.Equal(new[] { NearDistrictId, FarDistrictId }, plan.VisitedDistrictIds);
        Assert.Equal(51, plan.LaborLossPermille);
    }

    /// <summary>
    /// 段5bの1行分だけを、TradeSystem.RunOneHouseholdsShoppingの骨子を公開APIだけで
    /// 複製して実行する(privateメソッドを直接呼べないため)。
    /// <see cref="ErrandPlanIsDeterministicAcrossHouseholdOrder"/>専用 ── 資金・在庫を
    /// 実際に動かして2つの実行順のあいだに差を作るのが目的であり、帳簿の細部
    /// (UnaffordableNecessityCount等)は再現しない。
    /// </summary>
    private static void Purchase(
        World world, StoreChoice storeChoice, HouseholdState buyer, DemandLine line,
        IReadOnlyList<int> visitedDistrictIds)
    {
        if (!storeChoice.TrySelect(world, buyer, line.ItemId, visitedDistrictIds, out var store))
        {
            return;
        }

        var decision = BuyerBudget.Decide(line, store.UnitEffectivePrice);

        if (decision.Quantity <= 0)
        {
            return;
        }

        int quantityInUnits = BuyerBudget.QuantityInUnits(line.Purpose, decision.Quantity, durabilityPerTool: 1);

        if (quantityInUnits <= 0)
        {
            return;
        }

        var seller = world.Households[store.SellerId];
        int fundsCap = TradeSettlement.FundsCap(buyer.LiquidFunds, store.UnitEffectivePrice);
        int actualQuantity = Math.Min(Math.Min(quantityInUnits, fundsCap), seller.WorkshopInventory[line.ItemId]);

        if (actualQuantity >= 1)
        {
            TradeSettlement.Execute(
                world, buyer, seller, line.Purpose, line.ItemId, actualQuantity, store.UnitEffectivePrice,
                acquisitionCostSmoothingPermille: 0);
        }
    }

    /// <summary>
    /// テスト表 #27。世帯Id昇順に段5(5a→5b)を回した結果と、段5aを全世帯ぶん先に回してから
    /// 段5bを回した結果とで、VisitedDistrictIdsとLaborLossPermilleが一致する
    /// (計画がMarket以外の可変状態を読まないことの確認)。
    /// </summary>
    /// <remarks>
    /// <b>レビュー2巡目 I-a-2 の訂正。</b>旧版は<c>Plan</c>を2順で呼ぶだけで段5bを一度も
    /// 走らせておらず、2回の呼び出しのあいだで<see cref="World"/>の可変状態が何も変わらない
    /// ── <see cref="ErrandPlanner"/>がインスタンス状態を持たない限り恒に一致してしまい、
    /// 「<see cref="HouseholdState.LiquidFunds"/>など<see cref="HouseholdState.WorkshopInventory"/>
    /// 以外の可変状態を読む」変異を検出できなかった。
    /// <para>
    /// 本版はH0がH1から品目Aを買う(H1のLiquidFundsが動く)配置にし、H1自身の計画(品目Bを
    /// 遠方のH2へ買いに行くか)を、「H0→H1の順に5a→5bを交互に回す」実行と「5aを全世帯先に
    /// 回してから5bを回す」実行の両方で比べる。前者はH1のPlan呼び出し時点でH0の支払いを
    /// 受け取った<b>後</b>、後者は受け取る<b>前</b> ── H1.LiquidFundsが呼び出し時点で
    /// 実際に異なる(<c>Assert.NotEqual(fundsBeforePlanA1, fundsBeforePlanB1)</c>で確認)。
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(2026-09-20、レビュー2巡目)。</b><see cref="ErrandPlanner.Plan"/> の候補
    /// 区画のループへ、区画候補を「<c>buyer.LiquidFunds &lt;= 0</c> なら飛ばす」という
    /// <see cref="HouseholdState.LiquidFunds"/> を読む変異(可変状態を読み込む具体例)を
    /// 当てたところ、順序A(<c>VisitedDistrictIds = [2]</c>。H1がH0の支払いを受け取った後で
    /// 資金が正)と順序B(<c>VisitedDistrictIds = []</c>。支払いを受け取る前で資金が0のため
    /// 候補が飛ばされる)が食い違い、<c>Assert.Equal(planA1.VisitedDistrictIds, planB1.VisitedDistrictIds)</c>
    /// が失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ErrandPlanIsDeterministicAcrossHouseholdOrder()
    {
        const int HomeDistrictId = 0; // H0とH1の区画(H0がH1から品目Aを買う。距離0)。
        const int RemoteDistrictId = 2; // H2の区画(距離2 → 往復4時間。H1が品目Bを買いに行く)。

        var definition = BuildDefinition(externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 1, itemBPrice: 30));

        // H0がH1から買う品目Aの需要行(在庫・資金を動かす側)。
        var lineA = BuildLine(ItemA, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 1000, cashCap: 1000);

        // H1がH2へ外出して買う品目Bの需要行(検査対象)。
        var lineB = BuildLine(ItemB, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 1000);

        World BuildScenario()
        {
            var world = new World(npcCount: 3, householdCount: 3, itemCount: Item.Count);
            world.Npcs[0].Rank = NpcRank.Master;
            world.Npcs[1].Rank = NpcRank.Master;
            world.Npcs[2].Rank = NpcRank.Master;

            world.Households[0] = new HouseholdState(
                id: 0, districtId: HomeDistrictId, headNpcId: 0, memberNpcIds: new[] { 0 }, itemCount: Item.Count);
            world.Households[0].Occupation = Occupation.Smith;
            world.Households[0].LiquidFunds = 1000;

            world.Households[1] = new HouseholdState(
                id: 1, districtId: HomeDistrictId, headNpcId: 1, memberNpcIds: new[] { 1 }, itemCount: Item.Count);
            world.Households[1].Occupation = Occupation.Miller; // 品目Aの売り手(かつ品目Bの買い手)。
            world.Households[1].WorkshopInventory[ItemA] = 10;
            world.Households[1].LiquidFunds = 0; // H0の支払いを受け取る前は0。

            world.Households[2] = new HouseholdState(
                id: 2, districtId: RemoteDistrictId, headNpcId: 2, memberNpcIds: new[] { 2 }, itemCount: Item.Count);
            world.Households[2].Occupation = Occupation.Baker; // 品目Bの売り手。
            world.Households[2].WorkshopInventory[ItemB] = 10;

            // 段5b(店選択)は距離を見ずMarketの提示価格を読む(GDD06 §3)ので、Rの外でも要る。
            world.Market[new MarketKey(ItemA, 1)] = 1;
            world.Market[new MarketKey(ItemB, 2)] = 30;

            return world;
        }

        var planner = new ErrandPlanner(definition);
        var storeChoice = new StoreChoice(definition);

        // 順序A: 世帯Id昇順に段5(5a→5b)を交互に回す(TradeSystemの実際の順)。
        var worldA = BuildScenario();
        var errandA0 = OpportunityCost.SelectErrandDelegate(definition, worldA, worldA.Households[0]);
        var planA0 = planner.Plan(worldA, worldA.Households[0], DemandOf(lineA), errandA0);
        Purchase(worldA, storeChoice, worldA.Households[0], lineA, planA0.VisitedDistrictIds);

        int fundsBeforePlanA1 = worldA.Households[1].LiquidFunds; // H0の支払いを受け取った後。
        var errandA1 = OpportunityCost.SelectErrandDelegate(definition, worldA, worldA.Households[1]);
        var planA1 = planner.Plan(worldA, worldA.Households[1], DemandOf(lineB), errandA1);
        Purchase(worldA, storeChoice, worldA.Households[1], lineB, planA1.VisitedDistrictIds);

        // 順序B: 段5aを全世帯ぶん先に回してから段5bを回す。
        var worldB = BuildScenario();
        var errandB0 = OpportunityCost.SelectErrandDelegate(definition, worldB, worldB.Households[0]);
        var planB0 = planner.Plan(worldB, worldB.Households[0], DemandOf(lineA), errandB0);

        int fundsBeforePlanB1 = worldB.Households[1].LiquidFunds; // H0の支払いを受け取る前。
        var errandB1 = OpportunityCost.SelectErrandDelegate(definition, worldB, worldB.Households[1]);
        var planB1 = planner.Plan(worldB, worldB.Households[1], DemandOf(lineB), errandB1);

        Purchase(worldB, storeChoice, worldB.Households[0], lineA, planB0.VisitedDistrictIds);
        Purchase(worldB, storeChoice, worldB.Households[1], lineB, planB1.VisitedDistrictIds);

        // 前提: H1のPlan呼び出し時点でLiquidFundsが順序間で実際に異なる。これが無いと、
        // Marketの列挙順に依存する変異(#28相当)以外は何も検出できない(レビュー2巡目 I-a-2)。
        Assert.NotEqual(fundsBeforePlanA1, fundsBeforePlanB1);

        Assert.Equal(planA0.VisitedDistrictIds, planB0.VisitedDistrictIds);
        Assert.Equal(planA0.LaborLossPermille, planB0.LaborLossPermille);
        Assert.Equal(planA1.VisitedDistrictIds, planB1.VisitedDistrictIds);
        Assert.Equal(planA1.LaborLossPermille, planB1.LaborLossPermille);
        Assert.NotEmpty(planA1.VisitedDistrictIds); // 前提(H1が実際に外出している)を確かめる。
    }

    /// <summary>
    /// 【核心】テスト表 #29。自区画にitem A・item Bの両方の最安店がある世帯を置く。遠方区画は
    /// item Aを自区画より悪い価格(58 vs 54)で売る ── 利得への寄与は0であって負にはならない
    /// (自区画の余剰44がそのまま残る)。item Bを自区画と同額以上(58・52)にすると利得は0(境界
    /// 52は増分4=費用4でちょうど価値0、行かない)。item Bだけ自区画より安く(50)すると、
    /// 利得はitem Bの増分(52→44の差8)ぶんだけ正になり(価値4>0)、行く。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-20)。</b>候補側の集合から「既に居る区画」を落とす変異
    /// (A-1で直した誤り。候補の見積もりを<c>{d}</c>単独に戻す)を当てたところ、item Aの
    /// 遠方価格(58、自区画54より悪い)が寄与<c>27−44=−17</c>という負の項を作り、item B価格50の
    /// ケース(<c>Assert.Equal(new[] { RemoteDistrictId }, plan.VisitedDistrictIds)</c>、正しい価値
    /// <c>8−4=4</c>)が空の結果(実際の価値 <c>8+(−17)−4=−13</c>)で失敗した(赤を確認)。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ErrandGainIsNeverNegative()
    {
        const int HomeDistrictId = 0;
        const int RemoteDistrictId = 2; // 距離2 → 往復4時間。
        const int HomeSellerAId = 1;
        const int HomeSellerBId = 2;

        var errand = Delegate(costPerHour: 1); // 距離2 → 費用4。
        // target6/expected4/baseValue65(w=76)。自区画は両品目とも54(余剰44)。
        var lineA = BuildLine(ItemA, budget: 1, targetStock: 6, expectedStock: 4, baseValue: 65);
        var lineB = BuildLine(ItemB, budget: 1, targetStock: 6, expectedStock: 4, baseValue: 65);

        World BuildScenario()
        {
            var world = BuildWorld(
                buyerDistrictId: HomeDistrictId,
                (HomeDistrictId, Occupation.Miller), (HomeDistrictId, Occupation.Baker),
                (RemoteDistrictId, Occupation.Miller), (RemoteDistrictId, Occupation.Baker));

            // 自区画(距離0、R以内)は当日の市場で両品目とも54(w=76に対して余剰44)。
            world.Market[new MarketKey(ItemA, HomeSellerAId)] = 54;
            world.Market[new MarketKey(ItemB, HomeSellerBId)] = 54;

            return world;
        }

        // 遠方item Aは常に58(自区画54より悪い、固定)。遠方item Bのfloorだけを動かす。

        // item Bのfloorも58(自区画より悪い) → 両品目とも寄与0 → 利得0 → 行かない。
        {
            var definition = BuildDefinition(
                externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 58, itemBPrice: 58));
            var world = BuildScenario();

            var planner = new ErrandPlanner(definition);
            var plan = planner.Plan(world, world.Households[0], DemandOf(lineA, lineB), errand);

            Assert.Empty(plan.VisitedDistrictIds);
        }

        // 境界: item Bのfloorを52に下げる → 余剰48、増分4=費用4 → 価値0(等号は入らない) → 行かない。
        {
            var definition = BuildDefinition(
                externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 58, itemBPrice: 52));
            var world = BuildScenario();

            var planner = new ErrandPlanner(definition);
            var plan = planner.Plan(world, world.Households[0], DemandOf(lineA, lineB), errand);

            Assert.Empty(plan.VisitedDistrictIds);
        }

        // item Bのfloorを50に下げる → 余剰52、増分8>費用4 → 価値4 → 行く。
        {
            var definition = BuildDefinition(
                externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 58, itemBPrice: 50));
            var world = BuildScenario();

            var planner = new ErrandPlanner(definition);
            var plan = planner.Plan(world, world.Households[0], DemandOf(lineA, lineB), errand);

            Assert.Equal(new[] { RemoteDistrictId }, plan.VisitedDistrictIds);
        }
    }

    /// <summary>
    /// 【核心】テスト表 #30。GDD06 §3の例(別表C)。耐久(工具)の行だけを需要に持つ世帯で、
    /// N×1000=13,000・目標6,500・予想0・基礎値100・見積もり50のとき、線形解は耐久値で13,000を
    /// 返すが q_個=CeilDiv(13000,13000)=1。余剰はFloorDiv(1×(w-50),2)=50であって
    /// FloorDiv(13000×(w-50),2)=650000ではない。外出の費用を両者の間(300)に置くと、
    /// 直っていれば行かず(50&lt;300)、取り違えていれば行く(650000&gt;300)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-20)。</b><c>SurplusFor</c> で <c>decision.Quantity</c> を
    /// <c>BuyerBudget.QuantityInUnits</c> に通さず直接 <c>Errand.Surplus</c> へ渡す変異
    /// (C-1で直した誤り。段5.5が耐久だけ単位を取り違えていた本体)を当てたところ、
    /// <c>Assert.Empty(plan.VisitedDistrictIds)</c> が実際値 <c>[SellerDistrictId]</c>
    /// (13,000倍に膨らんだ余剰650000が費用300を圧倒的に上回り、行ってしまう)で失敗した
    /// (赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void DurableSurplusIsMeasuredInUnitsNotDurability()
    {
        const int SellerDistrictId = 2; // 距離2 → 往復4時間(travelHoursPerDistrict=1)。
        const int ToolItem = Item.Tools;

        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = ToolItem, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = PrimaryItem, Quantity = 1 } },
            laborPermille: 1000);

        var externalBuyPrice = new int[Item.Count];
        externalBuyPrice[ToolItem] = 50; // 見積もり50(記憶なし・Rの外 → 床)。
        externalBuyPrice[Item.Grain] = 1; // UnusedRecipe(Baker等)が出力する品目。0だと構築時に投げる。

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe,
            opportunityCostBaseByOccupation: new[] { 1, 1, 1, 1, 1 },
            rankCoefficientPermille: new[] { 1000, 1000, 1000 },
            travelHoursPerDistrict: 1,
            disposableHours: 12,
            toolLifeLaborDays: 13, // N=13 → ToolDurabilityPerUnit = 13 × 1000 = 13,000。
            externalBuyPriceOverride: externalBuyPrice);

        var world = BuildWorld(buyerDistrictId: 0, (SellerDistrictId, Occupation.Miller));

        var line = BuildLine(
            ToolItem, budget: 1, targetStock: 6500, expectedStock: 0, baseValue: 100,
            purpose: DemandPurpose.Durable);

        // 費用 = 往復4時間 × 機会費用75 = 300(50と650000の間。<remarks>参照)。
        var errand = Delegate(costPerHour: 75);
        var planner = new ErrandPlanner(definition);

        var plan = planner.Plan(world, world.Households[0], DemandOf(line), errand);

        Assert.Empty(plan.VisitedDistrictIds);
    }

    /// <summary>
    /// C-3。3回目のレビュー I-1 は「<see cref="ErrandPlannerTests"/> が
    /// <see cref="DemandPurpose.Necessity"/> の行しか通していない」ことに守られていた。
    /// <see cref="DurableSurplusIsMeasuredInUnitsNotDurability"/>(Durable)と既存の各テスト
    /// (Necessity)に加え、残る2種(<see cref="DemandPurpose.ProductionInput"/> ・
    /// <see cref="DemandPurpose.Preference"/>)でも <c>BuyerBudget.QuantityInUnits</c> が恒等
    /// (単位を変えない)であることを、#30 とまったく同じ数量(耐久値13,000相当)を使って確かめる
    /// ── Durableだけが変換で縮み、他の3種は縮まないので費用300を超えて行く。
    /// </summary>
    [Theory]
    [InlineData(DemandPurpose.ProductionInput)]
    [InlineData(DemandPurpose.Necessity)]
    [InlineData(DemandPurpose.Preference)]
    public void NonDurablePurposesDoNotScaleTheQuantity(DemandPurpose purpose)
    {
        const int SellerDistrictId = 2; // 距離2 → 往復4時間(travelHoursPerDistrict=1)。
        const int ToolItem = Item.Tools;

        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = ToolItem, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = PrimaryItem, Quantity = 1 } },
            laborPermille: 1000);

        var externalBuyPrice = new int[Item.Count];
        externalBuyPrice[ToolItem] = 50; // #30と同じ見積もり50(記憶なし・Rの外 → 床)。
        externalBuyPrice[Item.Grain] = 1; // UnusedRecipe(Baker等)が出力する品目。0だと構築時に投げる。

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe,
            opportunityCostBaseByOccupation: new[] { 1, 1, 1, 1, 1 },
            rankCoefficientPermille: new[] { 1000, 1000, 1000 },
            travelHoursPerDistrict: 1,
            disposableHours: 12,
            toolLifeLaborDays: 13, // #30と同じ13,000(ただしDurable以外は換算に使われない)。
            externalBuyPriceOverride: externalBuyPrice);

        var world = BuildWorld(buyerDistrictId: 0, (SellerDistrictId, Occupation.Miller));

        // #30と同じ数量13,000(目標6,500・予想0・基礎値100)。Durableなら q_個=1 に縮むが、
        // それ以外は13,000のまま(恒等)。
        var line = BuildLine(
            ToolItem, budget: 1, targetStock: 6500, expectedStock: 0, baseValue: 100, purpose: purpose);

        // 費用300は#30の閾値と同じ。恒等変換なら余剰650000 > 300で行く。
        var errand = Delegate(costPerHour: 75);
        var planner = new ErrandPlanner(definition);

        var plan = planner.Plan(world, world.Households[0], DemandOf(line), errand);

        Assert.Equal(new[] { SellerDistrictId }, plan.VisitedDistrictIds);
    }
}
