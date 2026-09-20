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
        int itemId, int budget, int targetStock, int expectedStock, int baseValue, int cashCap = 1_000_000) =>
        new()
        {
            Purpose = DemandPurpose.Necessity,
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

        var definition = BuildDefinition(externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 10));
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
    [Fact]
    public void EstimateUsesTheLatestValidMemoryOutsideVisionRadius()
    {
        const int SellerDistrictId = 2; // 距離2(Rの外)。
        const int SellerId = 1;

        // サブケースA: 3日前(90)・1日前(50)。w=70(平均70・最古90はどちらも余剰0、最新50だけ行く)。
        {
            var definition = BuildDefinition(externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 200));
            var world = BuildWorld(buyerDistrictId: 0, (SellerDistrictId, Occupation.Miller));
            world.Households[SellerId].WorkshopInventory[ItemA] = 10;

            EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 4 * 24);
            AddObservation(world, ItemA, SellerId, price: 90, observedAt: Tick.FromDays(1));
            AddObservation(world, ItemA, SellerId, price: 50, observedAt: Tick.FromDays(3));

            var line = BuildLine(ItemA, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 70);
            var planner = new ErrandPlanner(definition);

            var plan = planner.Plan(world, world.Households[0], DemandOf(line), Delegate(costPerHour: 1));

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
    /// テスト表 #11。1次産品だけを持つ需要リストの行は、どこへも行かず、例外も出ない。
    /// </summary>
    [Fact]
    public void PrimaryItemLinesCreateNoErrand()
    {
        var definition = BuildDefinition();
        var world = BuildWorld(buyerDistrictId: 0); // 売り手なし。

        var line = BuildLine(PrimaryItem, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 50);
        var planner = new ErrandPlanner(definition);

        var exception = Record.Exception(
            () => planner.Plan(world, world.Households[0], DemandOf(line), Delegate(costPerHour: 1)));

        Assert.Null(exception);

        var plan = planner.Plan(world, world.Households[0], DemandOf(line), Delegate(costPerHour: 1));
        Assert.Empty(plan.VisitedDistrictIds);
        Assert.Equal(0, plan.LaborLossPermille);
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
    /// 【核心】テスト表 #14(前半)。2つの遠方区画が同じ安値で同じ品目を売る → 1区画だけ行く
    /// (2つ目は差0−費用&lt;0。同値はId最小)。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>後半(「2つ目だけさらに安くすると、その差が費用を超えるときだけ2つ目にも行く」)は
    /// 実装していない。</b>報告済み(止まって報告)── 5.6の利得式
    /// (<c>利得 = 余剰({d}) − 余剰({自区画}∪行くと決めた区画)</c>)で1周目にXが選ばれた
    /// (<c>surplus(PX)-CX ≥ surplus(PY)-CY</c>)とき、2周目でYを追加する条件
    /// (<c>surplus(PY)-surplus(PX) &gt; CY</c>)を整理すると <c>CX &lt; 0</c> が要る
    /// (<c>Errand.Cost</c> は非負なので恒に成り立たない)。同一品目の2区画どちらかが
    /// 「1周目で選ばれた」時点で、もう一方が2周目で追加されることは代数的に起こらない
    /// (実測: 価格を追加で下げると1周目の選択そのものが入れ替わるだけで、2区画目は
    /// 常に選ばれなかった)。
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

        // 両区画とも床30(同値) → 1区画だけ(district1、Id最小)。
        {
            var definition = BuildDefinition(externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 30));
            var world = BuildScenario();

            var planner = new ErrandPlanner(definition);
            var plan = planner.Plan(world, world.Households[0], DemandOf(line), errand);

            Assert.Equal(new[] { District1 }, plan.VisitedDistrictIds);
        }

        // district2を安くする(25) → 1周目でdistrict2が選ばれるだけで、常に1区画のまま
        // (district1が2周目で追加されることはない。上記remarks参照)。
        {
            var definition = BuildDefinition(externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 30));
            var world = BuildScenario();

            EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 2 * 24);
            AddObservation(world, ItemA, Seller2Id, price: 25, observedAt: Tick.Zero);

            var planner = new ErrandPlanner(definition);
            var plan = planner.Plan(world, world.Households[0], DemandOf(line), errand);

            Assert.Equal(new[] { District2 }, plan.VisitedDistrictIds);
        }
    }

    /// <summary>
    /// テスト表 #15(境界のみ)。往復時間がTを超えると外出そのものが除外され、
    /// ちょうどTなら除外されない(等号は入る)。
    /// </summary>
    /// <remarks>
    /// <b>「距離4と距離3がともに価値&gt;0のとき距離4だけ選ぶ」「距離4と距離2なら両方行く」は
    /// 実装していない。</b>報告済み(止まって報告)── #14の後半と同じ代数的理由で、
    /// 5.6の利得式(候補は<c>{d}</c>単独、既存分との比較は<c>{自区画}∪行くと決めた区画</c>)の下では
    /// 1周目が真の最良(全候補中の最大値)を選ぶ以上、2周目のどの候補の価値も
    /// <c>-Errand.Cost(1周目の往復, …) ≤ 0</c> を超えられない(厳密証明済み、Tの上限とは無関係に
    /// 恒に成り立つ)。<b>この形の利得式では、そもそも1日に2区画以上へ行くことが構造的に
    /// 起こりえない。</b>本テストはTの境界判定そのもの(<c>往復時間の合計 + 往復 &gt; T</c>)を
    /// 単独の候補で直接確かめる。
    /// </remarks>
    [Fact]
    public void ErrandStopsAtTheDisposableHoursCap()
    {
        const int FarDistrictId = 8; // 距離4 → travelHoursPerDistrict=1で往復8時間。
        const int FarSellerId = 1;

        var errand = Delegate(costPerHour: 1);
        // baseValue=1000,target=1,expected=0 → w=1500,q=2,price=10 → 余剰1490(費用を大きく
        // 上回る値)。Tの境界だけで行く/行かないが決まるようにする。
        var line = BuildLine(ItemA, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 1000);

        World BuildScenario()
        {
            var world = BuildWorld(buyerDistrictId: 0, (FarDistrictId, Occupation.Miller));
            world.Households[FarSellerId].WorkshopInventory[ItemA] = 10;
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

        var definition = BuildDefinition();
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
            externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 10));

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
    /// テスト表 #27。世帯Id昇順に段5aを回した結果と、段5aを全世帯ぶん先に回してから
    /// (別の順で)回した結果とで、VisitedDistrictIdsとLaborLossPermilleが一致する
    /// (計画がMarket以外の可変状態を読まないことの確認)。
    /// </summary>
    [Fact]
    public void ErrandPlanIsDeterministicAcrossHouseholdOrder()
    {
        const int SellerDistrictId = 2;

        var definition = BuildDefinition(externalBuyPriceOverride: BuildExternalBuyPrice(itemAPrice: 30));
        var errand = Delegate(costPerHour: 1);
        var line = BuildLine(ItemA, budget: 1, targetStock: 1, expectedStock: 0, baseValue: 1000);

        World BuildScenario()
        {
            var world = new World(npcCount: 3, householdCount: 3, itemCount: Item.Count);
            world.Npcs[0].Rank = NpcRank.Master;
            world.Npcs[1].Rank = NpcRank.Master;
            world.Npcs[2].Rank = NpcRank.Master;

            world.Households[0] = new HouseholdState(
                id: 0, districtId: 0, headNpcId: 0, memberNpcIds: new[] { 0 }, itemCount: Item.Count);
            world.Households[0].Occupation = Occupation.Smith;

            world.Households[1] = new HouseholdState(
                id: 1, districtId: 0, headNpcId: 1, memberNpcIds: new[] { 1 }, itemCount: Item.Count);
            world.Households[1].Occupation = Occupation.Smith;

            world.Households[2] = new HouseholdState(
                id: 2, districtId: SellerDistrictId, headNpcId: 2, memberNpcIds: new[] { 2 }, itemCount: Item.Count);
            world.Households[2].Occupation = Occupation.Miller;
            world.Households[2].WorkshopInventory[ItemA] = 10;

            return world;
        }

        var planner = new ErrandPlanner(definition);

        // 順序A: 世帯0→1の順にPlanを呼ぶ。
        var worldA = BuildScenario();
        var planA0 = planner.Plan(worldA, worldA.Households[0], DemandOf(line), errand);
        var planA1 = planner.Plan(worldA, worldA.Households[1], DemandOf(line), errand);

        // 順序B: 世帯1→0の順(段5aを別順で回す)。
        var worldB = BuildScenario();
        var planB1 = planner.Plan(worldB, worldB.Households[1], DemandOf(line), errand);
        var planB0 = planner.Plan(worldB, worldB.Households[0], DemandOf(line), errand);

        Assert.Equal(planA0.VisitedDistrictIds, planB0.VisitedDistrictIds);
        Assert.Equal(planA0.LaborLossPermille, planB0.LaborLossPermille);
        Assert.Equal(planA1.VisitedDistrictIds, planB1.VisitedDistrictIds);
        Assert.Equal(planA1.LaborLossPermille, planB1.LaborLossPermille);
        Assert.NotEmpty(planA0.VisitedDistrictIds); // 前提(実際に外出している)を確かめる。
    }
}
