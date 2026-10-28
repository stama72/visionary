using Visionary.Sim.Determinism;
using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="PlayerSale"/>(GDD06 §6.1 / GDD01 §3.6、W5-03 タスク仕様のテスト表)の検査。
/// 世界は 世帯2戸・Miller(出力 パン・入力 薪)で、薪は必需と生産の入力の両方に行を持つ。
/// 時刻は 1日目 10時(<c>Now.Value = 34</c>)から始める ── 相場基準の観測は前日までしか効かないので、
/// 0日目では必需の取り置きが立たない。
/// </summary>
public sealed class PlayerSaleTests
{
    private const int FirewoodWindowPrice = 40;  // 貨幣。薪の窓口の当日価格(相場項が無い行の基礎値)
    private const int ToolWindowPrice = 290;     // 貨幣。工具の窓口の当日価格
    private const int BreadReference = 54;       // 貨幣。パンの相場基準(観測1件)。必需の取り置きを立てる

    private static readonly WorldDefinition Definition = BuildDefinition();

    private static WorldDefinition BuildDefinition()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = Item.Firewood, Quantity = 1 } },
            laborPermille: 1000);

        // 薪2個/日・パン1個/日(全階層)。必需の目標在庫日数は薪・パンとも2日。
        var consumption = new int[3][];

        for (int rank = 0; rank < 3; rank++)
        {
            consumption[rank] = new int[Item.Count];
            consumption[rank][Item.Firewood] = 2;
            consumption[rank][Item.Bread] = 1;
        }

        var necessityDays = new int[Item.Count];
        necessityDays[Item.Firewood] = 2;
        necessityDays[Item.Bread] = 2;

        // 都市生産品(穀物=未使用レシピの出力、パン)だけが外部買値、他は1次産品で外部売値の基準値を持つ。
        var sellBase = new int[Item.Count];
        var buy = new int[Item.Count];

        for (int itemId = 0; itemId < Item.Count; itemId++)
        {
            sellBase[itemId] = 10;
        }

        sellBase[Item.Firewood] = FirewoodWindowPrice;
        sellBase[Item.Tools] = ToolWindowPrice;
        sellBase[Item.Grain] = 0;
        sellBase[Item.Bread] = 0;
        buy[Item.Grain] = 5;
        buy[Item.Bread] = BreadReference;

        return EconomySystemTestFixtures.BuildDefinition(
            recipe,
            dailyConsumptionPerNpcByRank: consumption,
            necessityTargetStockDays: necessityDays,
            tolerancePermille: 1000,
            inputBufferDays: 5,
            externalSellPriceBaseOverride: sellBase,
            externalBuyPriceOverride: buy);
    }

    /// <summary>
    /// 世帯0(買い手)と世帯1(他人)の世界。朝の計画を1回走らせて控えの欄を埋めたあと、1日目 10時にする。
    /// </summary>
    private static World MakeWorld(int funds = 1000)
    {
        var world = EconomySystemTestFixtures.BuildWorldWithHouseholds(2);

        world.Households[0].LiquidFunds = funds;
        world.Households[0].PurchaseUnitCostAverage[Item.Firewood] = 7;
        world.Households[0].PurchaseUnitCostAverage[Item.Tools] = 100;
        world.Households[1].LiquidFunds = 500;
        world.Households[1].HouseholdInventory[Item.Bread] = 3;
        world.Households[1].WorkshopInventory[Item.Bread] = 2;
        world.Households[1].WorkshopInventory[Item.Firewood] = 4;

        // 世帯0の世帯主が、前の日にパンの相場を見ている ── 必需の取り置き(2 × 54 = 108)が立つ。
        world.Knowledge[0].Add(new PriceObservation
        {
            ItemId = Item.Bread,
            LocationId = 0,
            Price = BreadReference,
            SellerId = HouseholdState.ExternalMarketSellerId,
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        });

        EconomySystemTestFixtures.AdvanceClockOnly(world, 24);
        new SimScheduler(new ISimSystem[] { new TradePlanningSystem(Definition) }, new RandomSource(1))
            .Advance(world, 1);
        EconomySystemTestFixtures.AdvanceClockOnly(world, 9);

        Assert.Equal(34, world.Now.Value);

        return world;
    }

    private static void AddNeed(World world, int id, NeedReason reason, int itemId, int quantity, int householdId = 0)
    {
        world.Needs.Add(new Need
        {
            Id = id,
            TypeCode = Need.TypeOf(reason),
            TargetHouseholdId = householdId,
            ItemId = itemId,
            Quantity = quantity,
            ReasonCode = reason,
        });
    }

    /// <summary>約定前の、世帯0の (用途, 品目) の行。<c>Execute</c> の前に呼ぶ。</summary>
    private static DemandLine LineOf(World world, DemandPurpose purpose, int itemId)
    {
        var household = world.Households[0];
        var demand = new BuyerDemand(Definition).Build(
            world, household,
            household.MemoHasPreviousOffer[Item.Bread] == 1,
            household.MemoPreviousOfferPrice[Item.Bread]);

        return demand.Lines.First(line => line.Purpose == purpose && line.ItemId == itemId);
    }

    /// <summary>
    /// 値を出した記録(<c>PlayerOfferDayIndex</c>)以外、世界が何も変わっていないこと。
    /// 記録が当日になっていることを確かめてから −1 に戻し、ハッシュ全体で比べる。
    /// </summary>
    private static void AssertOnlyTheOfferIsRecorded(World world, int itemId, ulong hashBefore)
    {
        var household = world.Households[0];

        Assert.Equal((int)world.Now.DayIndex, household.PlayerOfferDayIndex[itemId]);
        household.PlayerOfferDayIndex[itemId] = -1;
        Assert.Equal(hashBefore, StateHasher.Compute(world));
    }

    /// <summary>テスト表 #1。境目は上側を含む(150 以上で感謝、400 以上で深い感謝)。</summary>
    [Fact]
    public void GratitudeOf_SplitsAtThresholdsInclusiveOnTheUpperSide()
    {
        Assert.Equal(Gratitude.Reluctant, PlayerSale.GratitudeOf(0));
        Assert.Equal(Gratitude.Reluctant, PlayerSale.GratitudeOf(149));
        Assert.Equal(Gratitude.Grateful, PlayerSale.GratitudeOf(150));
        Assert.Equal(Gratitude.Grateful, PlayerSale.GratitudeOf(399));
        Assert.Equal(Gratitude.DeepGratitude, PlayerSale.GratitudeOf(400));
        Assert.Equal(Gratitude.DeepGratitude, PlayerSale.GratitudeOf(1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerSale.GratitudeOf(-1));
    }

    /// <summary>テスト表 #2。余剰の比率は切り上げ(149.55… → 150)。</summary>
    [Fact]
    public void SurplusPermille_RoundsUp()
    {
        Assert.Equal(150, PlayerSale.SurplusPermille(evaluation: 1003, price: 853));
        Assert.Equal(426, PlayerSale.SurplusPermille(evaluation: 522, price: 300));
        Assert.Equal(0, PlayerSale.SurplusPermille(evaluation: 10, price: 10));

        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerSale.SurplusPermille(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerSale.SurplusPermille(10, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerSale.SurplusPermille(10, 11));
    }

    /// <summary>
    /// テスト表 #3。評価額は現金上限を含まず、利潤上限は <c>HasProfitCap</c> が真のときだけ入る。
    /// GDD01 §3.6 の工具の例(基礎値 348・在庫圧力 1500‰・現金上限 300 → 予算 300・評価額 522)。
    /// </summary>
    [Fact]
    public void Evaluation_ExcludesCashCapButKeepsProfitCap()
    {
        var line = new DemandLine
        {
            BaseValue = 348,
            StockPressurePermille = 1500,
            CashCap = 300,
            HasProfitCap = false,
            ProfitCap = 0,
        };

        // (a) 予算は現金上限で 300 になるが、評価額は 522。
        Assert.Equal(
            300, BuyerBudget.Budget(line.BaseValue, line.StockPressurePermille, line.CashCap, false, 0));
        Assert.Equal(522, PlayerSale.Evaluation(line));

        // (b) 利潤上限が評価額より低ければ、それが評価額。
        Assert.Equal(200, PlayerSale.Evaluation(line with { HasProfitCap = true, ProfitCap = 200 }));

        // (c) HasProfitCap が偽なら ProfitCap の値は読まない(0 が min に入って 0 にならない)。
        Assert.Equal(522, PlayerSale.Evaluation(line with { HasProfitCap = false, ProfitCap = 0 }));
    }

    /// <summary>テスト表 #4。価格 = 予算ちょうどは通り、予算 + 1 は通らない。</summary>
    [Fact]
    public void Execute_AcceptsAtExactlyTheBudgetAndRejectsOneAbove()
    {
        var accepted = MakeWorld();
        var rejected = MakeWorld();

        AddNeed(accepted, 0, NeedReason.ProductionStopped, Item.Firewood, 3);
        AddNeed(rejected, 0, NeedReason.ProductionStopped, Item.Firewood, 3);

        int budget = LineOf(accepted, DemandPurpose.ProductionInput, Item.Firewood).Budget;

        Assert.True(budget >= 1);

        var sale = new PlayerSale(Definition);

        var okResult = sale.Execute(accepted, 0, Item.Firewood, unitPrice: budget, offeredQuantity: 3);

        Assert.Equal(PlayerSaleOutcome.Accepted, okResult.Outcome);
        Assert.True(okResult.Quantity >= 1);

        ulong hashBefore = StateHasher.Compute(rejected);
        var overResult = sale.Execute(rejected, 0, Item.Firewood, unitPrice: budget + 1, offeredQuantity: 3);

        Assert.Equal(PlayerSaleOutcome.OverBudget, overResult.Outcome);
        Assert.Equal(0, overResult.Quantity);
        Assert.Equal(0, overResult.Payment);
        Assert.Null(overResult.Gratitude);
        Assert.Equal(budget + 1, overResult.UnitPrice);
        AssertOnlyTheOfferIsRecorded(rejected, Item.Firewood, hashBefore);
    }

    /// <summary>
    /// テスト表 #5。数量は 提示数量 / Need の数量 / 用途に使える資金で払える数 の最小。
    /// 3つ目は生産の入力の行で、必需の取り置き(108)で用途に使える資金(192)が流動資金(300)より
    /// 小さい。価格 40 では 流動資金なら 7 個、用途に使える資金なら 4 個。
    /// </summary>
    [Theory]
    [InlineData(2, 10, 2)]  // 提示数量が最小
    [InlineData(10, 3, 3)]  // Need の数量が最小
    [InlineData(10, 10, 4)] // 用途に使える資金が最小(流動資金なら 7)
    public void Execute_QuantityIsBoundByOfferNeedAndPurposeFunds(int offered, int needQuantity, int expected)
    {
        var world = MakeWorld(funds: 300);
        AddNeed(world, 0, NeedReason.ProductionStopped, Item.Firewood, needQuantity);

        var line = LineOf(world, DemandPurpose.ProductionInput, Item.Firewood);

        Assert.Equal(192, line.AvailableFunds);
        Assert.Equal(300, world.Households[0].LiquidFunds);
        Assert.True(FirewoodWindowPrice <= line.Budget);

        var result = new PlayerSale(Definition).Execute(
            world, 0, Item.Firewood, unitPrice: FirewoodWindowPrice, offeredQuantity: offered);

        Assert.Equal(PlayerSaleOutcome.Accepted, result.Outcome);
        Assert.Equal(expected, result.Quantity);
        Assert.Equal(expected * FirewoodWindowPrice, result.Payment);
    }

    /// <summary>
    /// テスト表 #6。工具(1日分の数量 1)は現金上限 = 用途に使える資金。予算がそれで決まる状態で
    /// 価格 = 予算なら、用途の資金を使い切る 1 個が必ず成立する(番人は発火しない)。
    /// </summary>
    [Fact]
    public void Execute_PriceAtBudgetAlwaysAffordsAtLeastOne()
    {
        // 必需の取り置き 108 を除いた用途に使える資金が 200 になる流動資金。
        var world = MakeWorld(funds: 308);
        AddNeed(world, 0, NeedReason.ToolsExhausted, Item.Tools, 2);

        var line = LineOf(world, DemandPurpose.Durable, Item.Tools);

        Assert.Equal(200, line.AvailableFunds);
        Assert.Equal(line.AvailableFunds, line.CashCap);
        Assert.Equal(line.CashCap, line.Budget);

        var result = new PlayerSale(Definition).Execute(
            world, 0, Item.Tools, unitPrice: line.Budget, offeredQuantity: 2);

        Assert.Equal(PlayerSaleOutcome.Accepted, result.Outcome);
        Assert.Equal(1, result.Quantity);
        Assert.Equal(line.Budget, result.Payment);
        Assert.Equal(308 - line.Budget, world.Households[0].LiquidFunds);
    }

    /// <summary>
    /// テスト表 #7。1日1回は (世帯, 品目) ごとで、最初の結果(ここでは OverBudget)にかかわらず数え、
    /// 翌日には戻る。同じ日でも別の品目は通る。
    /// </summary>
    [Fact]
    public void Execute_SecondOfferOnTheSameDayIsRefusedRegardlessOfTheFirstOutcome()
    {
        var world = MakeWorld();
        AddNeed(world, 0, NeedReason.ProductionStopped, Item.Firewood, 5);
        AddNeed(world, 1, NeedReason.ToolsExhausted, Item.Tools, 2);

        var sale = new PlayerSale(Definition);
        int budget = LineOf(world, DemandPurpose.ProductionInput, Item.Firewood).Budget;

        // 1日目 10時: 予算超過。
        var first = sale.Execute(world, 0, Item.Firewood, unitPrice: budget + 1, offeredQuantity: 3);

        Assert.Equal(PlayerSaleOutcome.OverBudget, first.Outcome);

        // 1日目 17時: 予算以下の値でも、今日はもう出した。状態は変わらない。
        EconomySystemTestFixtures.AdvanceClockOnly(world, 7);
        Assert.Equal(17, world.Now.HourOfDay);

        ulong hashBefore = StateHasher.Compute(world);
        var second = sale.Execute(world, 0, Item.Firewood, unitPrice: FirewoodWindowPrice, offeredQuantity: 3);

        Assert.Equal(PlayerSaleOutcome.AlreadyOfferedToday, second.Outcome);
        Assert.Equal(0, second.Quantity);
        Assert.Equal(hashBefore, StateHasher.Compute(world));

        // 同じ日の別の品目は通る。
        var otherItem = sale.Execute(world, 0, Item.Tools, unitPrice: 300, offeredQuantity: 2);

        Assert.Equal(PlayerSaleOutcome.Accepted, otherItem.Outcome);

        // 2日目 6時(Now.Value = 54): 戻る。
        EconomySystemTestFixtures.AdvanceClockOnly(world, 13);
        Assert.Equal(2, world.Now.DayIndex);

        var nextDay = sale.Execute(world, 0, Item.Firewood, unitPrice: FirewoodWindowPrice, offeredQuantity: 3);

        Assert.Equal(PlayerSaleOutcome.Accepted, nextDay.Outcome);
    }

    /// <summary>
    /// テスト表 #8。開示される Need が無い (世帯, 品目) は <c>NoNeed</c> で、1日1回に数えない
    /// (記録が −1 のまま)。困窮だけ・増産できないだけの品目も開示されない。
    /// </summary>
    [Fact]
    public void Execute_NoNeedDoesNotConsumeTheDailyOffer()
    {
        var world = MakeWorld();
        AddNeed(world, 0, NeedReason.Distress, Item.Firewood, 5);
        AddNeed(world, 1, NeedReason.CannotExpandProduction, Item.Bread, 500);

        var sale = new PlayerSale(Definition);
        ulong hashBefore = StateHasher.Compute(world);

        // (a) Need がまったく無い品目。
        var none = sale.Execute(world, 0, Item.Timber, unitPrice: 10, offeredQuantity: 1);

        Assert.Equal(PlayerSaleOutcome.NoNeed, none.Outcome);
        Assert.Equal(-1, none.NeedId);
        Assert.Equal(-1, world.Households[0].PlayerOfferDayIndex[Item.Timber]);

        // (b) 開示しない理由だけの品目。
        foreach (int itemId in new[] { Item.Firewood, Item.Bread })
        {
            var result = sale.Execute(world, 0, itemId, unitPrice: 10, offeredQuantity: 1);

            Assert.Equal(PlayerSaleOutcome.NoNeed, result.Outcome);
            Assert.Equal(-1, result.NeedId);
            Assert.Equal(-1, world.Households[0].PlayerOfferDayIndex[itemId]);
        }

        // 別の世帯の Need は当たらない。
        AddNeed(world, 2, NeedReason.ProductionStopped, Item.Firewood, 5, householdId: 1);
        hashBefore = StateHasher.Compute(world);

        var otherHousehold = sale.Execute(world, 0, Item.Firewood, unitPrice: 10, offeredQuantity: 1);

        Assert.Equal(PlayerSaleOutcome.NoNeed, otherHousehold.Outcome);
        Assert.Equal(hashBefore, StateHasher.Compute(world));
    }

    /// <summary>
    /// テスト表 #9。優先順は 工具切れ → 生産停止 → 遠方在庫(<c>world.Needs</c> の格納順に依らない)。
    /// 購入は当てた Need だけでなく、表が引く Need すべてから引く。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_PrefersTheReasonThatNamesThePurpose(bool reverseStorageOrder)
    {
        void AddInOrder(World world, params (int Id, NeedReason Reason, int Item, int Quantity)[] needs)
        {
            var ordered = reverseStorageOrder ? needs.Reverse() : needs;

            foreach (var n in ordered)
            {
                AddNeed(world, n.Id, n.Reason, n.Item, n.Quantity);
            }
        }

        var sale = new PlayerSale(Definition);

        // (a) 生産停止 + 遠方在庫 → 生産の入力(工房在庫)。2件とも減る。
        var a = MakeWorld();
        AddInOrder(a, (0, NeedReason.ProductionStopped, Item.Firewood, 5), (1, NeedReason.DistantStock, Item.Firewood, 5));

        var ra = sale.Execute(a, 0, Item.Firewood, unitPrice: 40, offeredQuantity: 2);

        Assert.Equal(PlayerSaleOutcome.Accepted, ra.Outcome);
        Assert.Equal(DemandPurpose.ProductionInput, ra.Purpose);
        Assert.Equal(0, ra.NeedId);
        Assert.Equal(2, a.Households[0].WorkshopInventory[Item.Firewood]);
        Assert.Equal(0, a.Households[0].HouseholdInventory[Item.Firewood]);
        Assert.Equal(new[] { 3, 3 }, a.Needs.OrderBy(n => n.Id).Select(n => n.Quantity).ToArray());

        // (b) 遠方在庫だけ → 走査順で最初の行(必需)。世帯在庫に入り、遠方在庫だけが減る。
        var b = MakeWorld();
        AddInOrder(b, (1, NeedReason.DistantStock, Item.Firewood, 5));

        var rb = sale.Execute(b, 0, Item.Firewood, unitPrice: 40, offeredQuantity: 2);

        Assert.Equal(PlayerSaleOutcome.Accepted, rb.Outcome);
        Assert.Equal(DemandPurpose.Necessity, rb.Purpose);
        Assert.Equal(2, b.Households[0].HouseholdInventory[Item.Firewood]);
        Assert.Equal(0, b.Households[0].WorkshopInventory[Item.Firewood]);
        Assert.Equal(3, Assert.Single(b.Needs).Quantity);

        // (c) 工具切れ + 遠方在庫 → 耐久(工房在庫)。2件とも減って消える。
        var c = MakeWorld();
        AddInOrder(c, (0, NeedReason.ToolsExhausted, Item.Tools, 2), (1, NeedReason.DistantStock, Item.Tools, 2));

        var rc = sale.Execute(c, 0, Item.Tools, unitPrice: 300, offeredQuantity: 2);

        Assert.Equal(PlayerSaleOutcome.Accepted, rc.Outcome);
        Assert.Equal(DemandPurpose.Durable, rc.Purpose);
        Assert.Equal(0, rc.NeedId);
        Assert.Equal(2, c.Households[0].WorkshopInventory[Item.Tools]);
        Assert.Empty(c.Needs);
    }

    /// <summary>
    /// テスト表 #10。買い手の流動資金だけが減り、帳簿は買い手側の1行(相手 = プレイヤー)だけ増える。
    /// 他の世帯・市場は動かない。
    /// </summary>
    [Fact]
    public void Execute_SettlesOnlyTheBuyerSideWithThePlayerAsCounterparty()
    {
        var world = MakeWorld();
        AddNeed(world, 0, NeedReason.ProductionStopped, Item.Firewood, 5);

        var other = world.Households[1];
        int otherFunds = other.LiquidFunds;
        var otherWorkshop = other.WorkshopInventory.ToArray();
        var otherHousehold = other.HouseholdInventory.ToArray();
        var otherCost = other.PurchaseUnitCostAverage.ToArray();
        int otherLedger = world.Ledgers[1].Count;
        var market = world.Market.ToArray();
        int fundsBefore = world.Households[0].LiquidFunds;
        int ledgerBefore = world.Ledgers[0].Count;

        var result = new PlayerSale(Definition).Execute(
            world, 0, Item.Firewood, unitPrice: 40, offeredQuantity: 3);

        Assert.Equal(PlayerSaleOutcome.Accepted, result.Outcome);
        Assert.Equal(3, result.Quantity);
        Assert.Equal(120, result.Payment);
        Assert.Equal(fundsBefore - result.Payment, world.Households[0].LiquidFunds);

        Assert.Equal(ledgerBefore + 1, world.Ledgers[0].Count);
        var entry = world.Ledgers[0][^1];

        Assert.Equal(HouseholdState.PlayerId, entry.CounterpartyId);
        Assert.Equal(LedgerDirection.Purchase, entry.Direction);
        Assert.Equal(LedgerTerms.Cash, entry.Terms);
        Assert.Equal(Item.Firewood, entry.ItemId);
        Assert.Equal(3, entry.Quantity);
        Assert.Equal(40, entry.UnitPrice);
        Assert.Equal(world.Now, entry.OccurredAt);

        Assert.Equal(otherFunds, other.LiquidFunds);
        Assert.Equal(otherWorkshop, other.WorkshopInventory);
        Assert.Equal(otherHousehold, other.HouseholdInventory);
        Assert.Equal(otherCost, other.PurchaseUnitCostAverage);
        Assert.Equal(otherLedger, world.Ledgers[1].Count);
        Assert.Equal(market, world.Market.ToArray());
    }

    /// <summary>
    /// テスト表 #11。仕入れ移動平均は生産の入力と耐久のときだけ更新する。必需(暖房用の薪)は動かない。
    /// </summary>
    [Fact]
    public void Execute_UpdatesAcquisitionCostOnlyForInputAndDurable()
    {
        int smoothing = Definition.AcquisitionCostSmoothingPermille;
        var sale = new PlayerSale(Definition);

        var input = MakeWorld();
        AddNeed(input, 0, NeedReason.ProductionStopped, Item.Firewood, 5);
        sale.Execute(input, 0, Item.Firewood, unitPrice: 40, offeredQuantity: 2);

        int expectedInput = OfferPrice.UpdatedAcquisitionCost(7, 40, smoothing);

        Assert.NotEqual(7, expectedInput);
        Assert.Equal(expectedInput, input.Households[0].PurchaseUnitCostAverage[Item.Firewood]);

        var durable = MakeWorld();
        AddNeed(durable, 0, NeedReason.ToolsExhausted, Item.Tools, 2);
        sale.Execute(durable, 0, Item.Tools, unitPrice: 300, offeredQuantity: 2);

        int expectedDurable = OfferPrice.UpdatedAcquisitionCost(100, 300, smoothing);

        Assert.NotEqual(100, expectedDurable);
        Assert.Equal(expectedDurable, durable.Households[0].PurchaseUnitCostAverage[Item.Tools]);

        var necessity = MakeWorld();
        AddNeed(necessity, 0, NeedReason.DistantStock, Item.Firewood, 5);
        var result = sale.Execute(necessity, 0, Item.Firewood, unitPrice: 40, offeredQuantity: 2);

        Assert.Equal(DemandPurpose.Necessity, result.Purpose);
        Assert.Equal(7, necessity.Households[0].PurchaseUnitCostAverage[Item.Firewood]);
    }

    /// <summary>
    /// テスト表 #12。段階は約定前の行から出す。生産の入力の目標在庫ちょうどまで売ると在庫圧力が
    /// 1500‰ から 1000‰ に落ち、約定後に作り直した行の評価額は 60 から 40 になる。価格 34 の余剰は
    /// 約定前なら 434‰(深い感謝)、約定後なら 150‰(感謝)。
    /// </summary>
    [Fact]
    public void Execute_GratitudeUsesTheLineBeforeSettlement()
    {
        const int price = 34; // 貨幣

        var world = MakeWorld();
        AddNeed(world, 0, NeedReason.ProductionStopped, Item.Firewood, 100);

        var before = LineOf(world, DemandPurpose.ProductionInput, Item.Firewood);
        int quantityToTarget = before.TargetStock;

        Assert.True(quantityToTarget >= 1);
        Assert.True(price <= before.Budget);

        var expected = PlayerSale.GratitudeOf(PlayerSale.SurplusPermille(PlayerSale.Evaluation(before), price));

        var result = new PlayerSale(Definition).Execute(
            world, 0, Item.Firewood, unitPrice: price, offeredQuantity: quantityToTarget);

        Assert.Equal(PlayerSaleOutcome.Accepted, result.Outcome);
        Assert.Equal(quantityToTarget, result.Quantity);

        var after = LineOf(world, DemandPurpose.ProductionInput, Item.Firewood);
        var fromAfter = PlayerSale.GratitudeOf(PlayerSale.SurplusPermille(PlayerSale.Evaluation(after), price));

        Assert.Equal(Gratitude.DeepGratitude, expected);
        Assert.NotEqual(expected, fromAfter);
        Assert.Equal(expected, result.Gratitude);
    }

    /// <summary>テスト表 #13。0 円・0 個・範囲外の Id は例外で、状態を変えない。</summary>
    [Theory]
    [InlineData(0, Item.Firewood, 0, 1)]   // 0 円
    [InlineData(0, Item.Firewood, 40, 0)]  // 0 個
    [InlineData(-1, Item.Firewood, 40, 1)] // 世帯 Id が負
    [InlineData(2, Item.Firewood, 40, 1)]  // 世帯 Id が世帯数以上
    [InlineData(0, -1, 40, 1)]             // 品目 Id が負
    [InlineData(0, Item.Count, 40, 1)]     // 品目 Id が品目数以上
    public void Execute_RejectsInvalidArguments(int householdId, int itemId, int price, int quantity)
    {
        var world = MakeWorld();
        AddNeed(world, 0, NeedReason.ProductionStopped, Item.Firewood, 5);

        ulong hashBefore = StateHasher.Compute(world);
        var sale = new PlayerSale(Definition);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => sale.Execute(world, householdId, itemId, price, quantity));
        Assert.Equal(hashBefore, StateHasher.Compute(world));
    }

    /// <summary>#13 の対照。無効な引数の元になった有効な引数は通る(無効だから落ちている証拠)。</summary>
    [Fact]
    public void Execute_AcceptsTheValidArgumentsTheInvalidCasesAreDerivedFrom()
    {
        var world = MakeWorld();
        AddNeed(world, 0, NeedReason.ProductionStopped, Item.Firewood, 5);

        var result = new PlayerSale(Definition).Execute(world, 0, Item.Firewood, 40, 1);

        Assert.Equal(PlayerSaleOutcome.Accepted, result.Outcome);
    }
}
