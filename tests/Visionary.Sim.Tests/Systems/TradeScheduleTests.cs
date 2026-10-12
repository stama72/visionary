using Visionary.Sim.Metrics;
using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// 順5 を朝(<see cref="TradePlanningSystem"/>)と夕方(<see cref="TradeSystem"/>)に分けたこと、
/// 計画を World に残すこと、段8(Need の差し引き)の検査(W5-02 タスク仕様のテスト表 #1〜9・#15)。
/// </summary>
public sealed class TradeScheduleTests
{
    private sealed class CountingSink : IDailyMetricsSink
    {
        public int Writes { get; private set; }

        public void Write(in DailySnapshot snapshot) => Writes++;
    }

    private static ISimSystem[] FullPipeline(WorldDefinition definition, IDailyMetricsSink sink) => new ISimSystem[]
    {
        new ProductionSystem(definition),
        new ConsumptionSystem(definition),
        new HouseholdSystem(definition),
        new NeedGenerationSystem(definition),
        new TradePlanningSystem(definition),
        new TradeSystem(definition),
        new MetricsSystem(definition, sink),
    };

    /// <summary>
    /// 世帯0(区画0)が、穀物を区画8の売り手から買う外出(往復8時間)と、パンを窓口へ輸出する別の外出
    /// (往復4時間)をする日の世界。窓口の穀物の高値記憶(999)を仕込んで窓口を買い物の候補から外す
    /// (<see cref="TradeSystemTests.ExportAddsToTheErrandLaborLoss"/> と同じ足場)。
    /// 時計は1日目(tick 24)から始まる。
    /// </summary>
    private static World BuildSeparateExportWorld(WorldDefinition definition)
    {
        var world = TradeSystemTests.BuildExportAndShoppingWorld(definition, exporterDistrictId: 0, sellerDistrictId: 8);
        EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 24);
        world.Knowledge[0].Add(new PriceObservation
        {
            ItemId = Item.Grain,
            LocationId = 0,
            Price = 999,
            SellerId = HouseholdState.ExternalMarketSellerId,
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        });

        return world;
    }

    /// <summary>
    /// テスト表 #1(#257)。本物の登録順で1日を進める。tick 0〜17 を処理した後は値付け・計画が済み
    /// 約定が無い。18 を処理した後に当日の約定と観測(Metrics)が出る。
    /// </summary>
    /// <remarks>
    /// 落ちるべき条件: <c>TradeSystem</c> の <c>Cadence</c> を 0時のまま残す / 段5b を朝へ置く /
    /// <c>MetricsSystem</c> を 0時に残す。
    /// </remarks>
    [Fact]
    public void TradePlanningRunsAtMidnightAndTradeSettlesAtEvening()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));
        var sink = new CountingSink();
        var scheduler = new SimScheduler(FullPipeline(definition, sink), new RandomSource(1));

        scheduler.Advance(world, ticks: WorldDefinition.EveningTradeHour); // tick 0〜17

        Assert.NotEmpty(world.Market);
        Assert.All(world.Households, household => Assert.True(household.ErrandDelegateNpcId >= 0));
        Assert.All(world.Ledgers, ledger => Assert.Empty(ledger));
        Assert.Equal(0, sink.Writes);

        scheduler.Advance(world, ticks: 1); // tick 18

        Assert.Contains(world.Ledgers, ledger => ledger.Count > 0);
        Assert.All(
            world.Ledgers,
            ledger => Assert.All(ledger, entry => Assert.Equal(WorldDefinition.EveningTradeHour, entry.OccurredAt.HourOfDay)));
        Assert.Equal(1, sink.Writes);
    }

    /// <summary>
    /// テスト表 #2(#257)。夕方の段4 は 0時〜18時の間に変わった状態から需要を作り直す
    /// (プレイヤーの売買の代わりに、テストが世帯在庫を書き換える)。
    /// </summary>
    /// <remarks>
    /// 落ちるべき条件: 夕方の段4 を省き、朝の需要をインスタンスのフィールドで持ち越す
    /// (朝の需要では穀物が不足のままなので、書き換えた後でも買ってしまう)。
    /// </remarks>
    [Fact]
    public void EveningDemandIsRebuiltFromTheCurrentState()
    {
        var definition = TradeSystemTests.BuildExportAndShoppingDefinition(travelHoursPerDistrict: 1);

        // 対照。そのまま1日進めると穀物を買う。
        var control = BuildSeparateExportWorld(definition);
        EconomySystemTestFixtures.RunTradeDays(control, definition, days: 1);
        Assert.True(control.Households[0].HouseholdInventory[Item.Grain] > 0);

        // 0時だけを処理し、18時の前に世帯在庫を目標在庫の十分上まで積む。
        const int StockedGrain = 10_000;
        var world = BuildSeparateExportWorld(definition);
        var scheduler = new SimScheduler(EconomySystemTestFixtures.TradeSystems(definition), new RandomSource(1));
        scheduler.Advance(world, ticks: 1);
        Assert.Contains(8, world.Households[0].PlannedErrandDistrictIds); // 朝は穀物の不足で外出を計画した

        world.Households[0].HouseholdInventory[Item.Grain] = StockedGrain;
        scheduler.Advance(world, ticks: 23);

        Assert.Equal(StockedGrain, world.Households[0].HouseholdInventory[Item.Grain]);
    }

    /// <summary>
    /// テスト表 #3(#257・核心)。夕方の段4 が段1 の控え(前日の自分の提示価格)を読む。当日の提示価格は
    /// 相場基準が立たず床(低い)なので、<c>world.Market</c> から引き直すと利潤上限が入力の値段を下回る。
    /// </summary>
    /// <remarks>
    /// 落ちるべき条件: 夕方の段4 が控えの欄ではなく <c>world.Market.TryGetValue(new MarketKey(出力, household.Id), …)</c>
    /// から当日の自分の提示価格を引く。
    /// </remarks>
    [Fact]
    public void EveningDemandReadsYesterdaysOwnOfferFromTheMemo()
    {
        const int BreadFloor = 30;
        const int GrainFloor = 100;
        const int PreviousBreadOffer = 1000;

        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = 2 } },
            inputs: new[] { new ItemQuantity { ItemId = Item.Grain, Quantity = 1 } },
            laborPermille: 1000);

        var externalBuyPrice = new int[Item.Count];
        externalBuyPrice[Item.Bread] = BreadFloor;
        externalBuyPrice[Item.Grain] = GrainFloor;

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe,
            tolerancePermille: 1200,
            minimumMarginPermille: 0,
            opportunityCostBaseByOccupation: new[] { 1, 1, 1, 1, 1 },
            rankCoefficientPermille: new[] { 1000, 1000, 1000 },
            travelHoursPerDistrict: 1,
            shipmentDays: 1,
            inputBufferDays: 1,
            externalBuyPriceOverride: externalBuyPrice);

        var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);
        TradeSystemTests.AddHousehold(world, id: 0, districtId: 4, Occupation.Miller, liquidFunds: 10_000);
        world.Households[0].WorkshopInventory[Item.Bread] = 1;
        TradeSystemTests.AddHousehold(world, id: 1, districtId: 4, Occupation.Baker, liquidFunds: 0);
        world.Households[1].WorkshopInventory[Item.Grain] = 100;

        // 前日の自分の提示価格(高い)。当日は相場基準が立たず床(30)になる。
        world.Market[new MarketKey(Item.Bread, 0)] = PreviousBreadOffer;

        EconomySystemTestFixtures.RunTradeDays(world, definition, days: 1);

        // 前提: 当日の提示価格は床で、前日の高い値とは別。見込み収益 = 当日の値なら 30×2 = 60 < 穀物 100。
        Assert.Equal(BreadFloor, world.Market[new MarketKey(Item.Bread, 0)]);

        // 前日の値(1000×2 = 2000)で利潤上限が立つので、入力(穀物)を買う。
        Assert.Contains(
            world.Ledgers[0],
            entry => entry.Direction == LedgerDirection.Purchase && entry.ItemId == Item.Grain);
    }

    /// <summary>
    /// テスト表 #4(#257・核心)。別の外出で中心へ輸出した日、World の計画の区画列には中心が入らず
    /// (時間割が中心行きを2回数えるため)、フラグだけが立つ。窓口の観測は段7 が複製の列を読んで生む。
    /// </summary>
    /// <remarks>
    /// 落ちるべき条件: 段6 が World の列に中心を足す / フラグを書かない / 段7 が World の列を読む
    /// (窓口の観測が消える)。
    /// </remarks>
    [Fact]
    public void ExportTripDoesNotAddTheCentreToThePlannedDistricts()
    {
        var definition = TradeSystemTests.BuildExportAndShoppingDefinition(travelHoursPerDistrict: 1);
        var world = BuildSeparateExportWorld(definition);

        EconomySystemTestFixtures.RunTradeDays(world, definition, days: 1);

        var household = world.Households[0];

        // 前提: 買い物の外出(区画8)があり、輸出も成立している。
        Assert.Contains(8, household.PlannedErrandDistrictIds);
        Assert.True(household.WorkshopInventory[Item.Bread] < 100);

        Assert.DoesNotContain(District.ExternalMarketDistrictId, household.PlannedErrandDistrictIds);
        Assert.Equal(1, household.ExportErrandTaken);

        // 足場が仕込んだ窓口観測(ObservedAt = Tick.Zero)ではなく、当日の段7 が18時に生んだ観測を見る。
        // 仕込みだけで満たされると、段7 が World の列を読む誤りを捕まえない。
        Assert.Contains(
            world.Knowledge[household.HeadNpcId],
            o => o.SellerId == HouseholdState.ExternalMarketSellerId
                && o.ObservedAt.DayIndex == 1
                && o.ObservedAt.HourOfDay == WorldDefinition.EveningTradeHour);
    }

    /// <summary>テスト表 #5(#257)。輸出フラグは翌朝の段5a が 0 に戻す。</summary>
    /// <remarks>落ちるべき条件: 段5a がフラグを 0 に戻さない。</remarks>
    [Fact]
    public void ExportFlagIsClearedTheNextMorning()
    {
        var definition = TradeSystemTests.BuildExportAndShoppingDefinition(travelHoursPerDistrict: 1);
        var world = BuildSeparateExportWorld(definition);
        var scheduler = new SimScheduler(EconomySystemTestFixtures.TradeSystems(definition), new RandomSource(1));

        scheduler.Advance(world, ticks: 24);
        Assert.Equal(1, world.Households[0].ExportErrandTaken);

        scheduler.Advance(world, ticks: 1); // 翌日の0時

        Assert.Equal(0, world.Households[0].ExportErrandTaken);
    }

    /// <summary>
    /// テスト表 #6(#257)。外出しない日も計画の欄を書く(書かない日があると前日の計画が残る)。
    /// </summary>
    /// <remarks>落ちるべき条件: 段5a が外出の無い日に計画を書かない。</remarks>
    [Fact]
    public void PlanIsWrittenEvenOnDaysWithoutAnErrand()
    {
        var definition = TradeSystemTests.BuildExportAndShoppingDefinition(travelHoursPerDistrict: 1);
        var world = BuildSeparateExportWorld(definition);
        var scheduler = new SimScheduler(EconomySystemTestFixtures.TradeSystems(definition), new RandomSource(1));

        scheduler.Advance(world, ticks: 24); // 1日目: 穀物の買い物の外出がある

        var household = world.Households[0];
        Assert.NotEmpty(household.PlannedErrandDistrictIds);

        // 前日の委託先を、当日の選択(NpcId 0)とは別の値にしておく。
        household.ErrandDelegateNpcId = 7;

        // 2日目は穀物が目標在庫を十分に上回り、外出する価値のある買い物が無い。
        household.HouseholdInventory[Item.Grain] = 10_000;
        scheduler.Advance(world, ticks: 1); // 2日目の0時

        Assert.Empty(household.PlannedErrandDistrictIds);
        Assert.Equal(0, household.ErrandDelegateNpcId);
    }

    // ── 段8(Need の差し引き)。薪(必需 + 生産の入力)・ビール(嗜好)・工具(耐久)を window から買う世帯。

    private const int SeededNeedQuantity = 100_000; // 個。購入量より十分大きく、消えない。

    private static WorldDefinition BuildNeedSubtractionDefinition()
    {
        var consumption = new int[Item.Count];
        consumption[Item.Firewood] = 1;
        consumption[Item.Beer] = 1;

        var necessityDays = new int[Item.Count];
        necessityDays[Item.Firewood] = 10;

        var preferenceDays = new int[Item.Count];
        preferenceDays[Item.Beer] = 10;

        return EconomySystemTestFixtures.BuildDefinition(
            new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = 1 } },
                inputs: new[] { new ItemQuantity { ItemId = Item.Firewood, Quantity = 1 } },
                laborPermille: 1000),
            dailyConsumptionPerNpcByRank: new[]
            {
                (int[])consumption.Clone(), (int[])consumption.Clone(), (int[])consumption.Clone(),
            },
            necessityTargetStockDays: necessityDays,
            preferenceTargetStockDays: preferenceDays,
            tolerancePermille: 1200,
            opportunityCostBaseByOccupation: new[] { 1, 1, 1, 1, 1 },
            rankCoefficientPermille: new[] { 1000, 1000, 1000 },
            travelHoursPerDistrict: 1,
            shipmentDays: 1,
            inputBufferDays: 1);
    }

    /// <summary>
    /// 区画4(中心 = 窓口と同じ区画)の世帯1戸。<paramref name="short"/> に挙げた用途だけ不足させ、
    /// 他の用途は目標在庫を満たす。窓口だけが売り手なので、買い物はすべて輸入である。
    /// </summary>
    private static World BuildNeedSubtractionWorld(params DemandPurpose[] @short)
    {
        var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);
        TradeSystemTests.AddHousehold(world, id: 0, districtId: 4, Occupation.Miller, liquidFunds: 1_000_000);

        var household = world.Households[0];
        household.HouseholdInventory[Item.Firewood] = 1000; // 必需を満たす。
        household.WorkshopInventory[Item.Firewood] = 1000; // 生産の入力を満たす。
        household.HouseholdInventory[Item.Beer] = 1000; // 嗜好を満たす。
        household.WorkshopInventory[Item.Tools] = 5; // 耐久を満たす。

        if (@short.Contains(DemandPurpose.Necessity))
        {
            household.HouseholdInventory[Item.Firewood] = 0;
        }

        if (@short.Contains(DemandPurpose.ProductionInput))
        {
            household.WorkshopInventory[Item.Firewood] = 0;
        }

        if (@short.Contains(DemandPurpose.Preference))
        {
            household.HouseholdInventory[Item.Beer] = 0;
        }

        if (@short.Contains(DemandPurpose.Durable))
        {
            household.WorkshopInventory[Item.Tools] = 0;
        }

        return world;
    }

    private static Need MakeNeed(int id, NeedReason reason, int itemId, int quantity) => new()
    {
        Id = id,
        TypeCode = Need.TypeOf(reason),
        TargetHouseholdId = 0,
        ItemId = itemId,
        Quantity = quantity,
        ReasonCode = reason,
    };

    private readonly record struct Bought(int Necessity, int ProductionInput, int Preference, int Durable);

    /// <summary>1日進めたときの用途別の購入個数を、在庫の差から求める(購入以外で在庫は動かない)。</summary>
    private static Bought MeasurePurchases(WorldDefinition definition, DemandPurpose[] @short)
    {
        var world = BuildNeedSubtractionWorld(@short);
        var household = world.Households[0];
        int firewoodHouse = household.HouseholdInventory[Item.Firewood];
        int firewoodWorkshop = household.WorkshopInventory[Item.Firewood];
        int beer = household.HouseholdInventory[Item.Beer];
        int tools = household.WorkshopInventory[Item.Tools];

        EconomySystemTestFixtures.RunTradeDays(world, definition, days: 1);

        return new Bought(
            Necessity: household.HouseholdInventory[Item.Firewood] - firewoodHouse,
            ProductionInput: household.WorkshopInventory[Item.Firewood] - firewoodWorkshop,
            Preference: household.HouseholdInventory[Item.Beer] - beer,
            Durable: household.WorkshopInventory[Item.Tools] - tools);
    }

    private static int QuantityOf(World world, NeedReason reason, int itemId)
    {
        var matches = world.Needs.Where(n => n.ReasonCode == reason && n.ItemId == itemId).ToArray();

        return matches.Length == 0 ? 0 : matches.Single().Quantity;
    }

    /// <summary>
    /// テスト表 #7(#257・核心)。4理由それぞれについて、用途の合う購入だけが引かれる。
    /// 4用途を1つずつ・すべて不足させた世界で、用途別の購入個数を在庫の差で測り、7つの Need の残りを照合する。
    /// </summary>
    /// <remarks>
    /// 落ちるべき条件: 品目だけで引く(用途を見ない) / <c>DistantStock</c> を1用途だけで引く /
    /// <c>CannotExpandProduction</c> を引く / <c>ProductionStopped</c> の <c>b</c> を4用途すべての和にする
    /// (必需だけ不足の世界で <c>ProductionStopped</c> が減る)。
    /// </remarks>
    [Theory]
    [InlineData("Necessity")]
    [InlineData("ProductionInput")]
    [InlineData("Preference")]
    [InlineData("Durable")]
    [InlineData("All")]
    public void NeedSubtractionMatchesThePurpose(string scenario)
    {
        var definition = BuildNeedSubtractionDefinition();

        var shortPurposes = scenario == "All"
            ? new[]
            {
                DemandPurpose.Necessity, DemandPurpose.ProductionInput, DemandPurpose.Preference,
                DemandPurpose.Durable,
            }
            : new[] { Enum.Parse<DemandPurpose>(scenario) };

        var bought = MeasurePurchases(definition, shortPurposes);

        // 前提: 不足させた用途は実際に買えている。他の用途は買っていない。
        Assert.Equal(shortPurposes.Contains(DemandPurpose.Necessity), bought.Necessity > 0);
        Assert.Equal(shortPurposes.Contains(DemandPurpose.ProductionInput), bought.ProductionInput > 0);
        Assert.Equal(shortPurposes.Contains(DemandPurpose.Preference), bought.Preference > 0);
        Assert.Equal(shortPurposes.Contains(DemandPurpose.Durable), bought.Durable > 0);

        var world = BuildNeedSubtractionWorld(shortPurposes);
        world.Needs.Add(MakeNeed(0, NeedReason.ProductionStopped, Item.Firewood, SeededNeedQuantity));
        world.Needs.Add(MakeNeed(1, NeedReason.Distress, Item.Firewood, SeededNeedQuantity));
        world.Needs.Add(MakeNeed(2, NeedReason.CannotExpandProduction, Item.Bread, SeededNeedQuantity));
        world.Needs.Add(MakeNeed(3, NeedReason.ToolsExhausted, Item.Tools, SeededNeedQuantity));
        world.Needs.Add(MakeNeed(4, NeedReason.DistantStock, Item.Firewood, SeededNeedQuantity));
        world.Needs.Add(MakeNeed(5, NeedReason.DistantStock, Item.Beer, SeededNeedQuantity));
        world.Needs.Add(MakeNeed(6, NeedReason.Distress, Item.Beer, SeededNeedQuantity));

        EconomySystemTestFixtures.RunTradeDays(world, definition, days: 1);

        Assert.Equal(SeededNeedQuantity - bought.ProductionInput, QuantityOf(world, NeedReason.ProductionStopped, Item.Firewood));
        Assert.Equal(SeededNeedQuantity - bought.Necessity, world.Needs.Single(n => n.Id == 1).Quantity);
        Assert.Equal(SeededNeedQuantity, world.Needs.Single(n => n.Id == 2).Quantity); // 買い物では埋まらない
        Assert.Equal(SeededNeedQuantity - bought.Durable, world.Needs.Single(n => n.Id == 3).Quantity);
        Assert.Equal(
            SeededNeedQuantity - bought.Necessity - bought.ProductionInput,
            world.Needs.Single(n => n.Id == 4).Quantity); // 薪は必需と入力の和。嗜好・耐久は薪ではない
        Assert.Equal(SeededNeedQuantity - bought.Preference, world.Needs.Single(n => n.Id == 5).Quantity);
        Assert.Equal(SeededNeedQuantity, world.Needs.Single(n => n.Id == 6).Quantity); // 嗜好の購入は困窮を引かない
    }

    /// <summary>
    /// テスト表 #7(#257・仕様の具体例)。薪が必需と生産の入力の2行に現れる世帯。
    /// 薪を必需で q 個・入力で 0 個買った日、<c>ProductionStopped(q+2)</c> は残り、<c>Distress(q)</c> は消え、
    /// <c>DistantStock(q+3)</c> は 3 が残る(仕様の 4 / 2 / 5 と同じ形を、測った q で組む)。
    /// </summary>
    [Fact]
    public void NeedSubtractionFollowsTheFirewoodExample()
    {
        var definition = BuildNeedSubtractionDefinition();
        var shortPurposes = new[] { DemandPurpose.Necessity };
        var bought = MeasurePurchases(definition, shortPurposes);

        Assert.True(bought.Necessity > 0);
        Assert.Equal(0, bought.ProductionInput);
        int q = bought.Necessity;

        var world = BuildNeedSubtractionWorld(shortPurposes);
        world.Needs.Add(MakeNeed(0, NeedReason.ProductionStopped, Item.Firewood, q + 2));
        world.Needs.Add(MakeNeed(1, NeedReason.Distress, Item.Firewood, q));
        world.Needs.Add(MakeNeed(2, NeedReason.DistantStock, Item.Firewood, q + 3));

        EconomySystemTestFixtures.RunTradeDays(world, definition, days: 1);

        Assert.Equal(q + 2, QuantityOf(world, NeedReason.ProductionStopped, Item.Firewood));
        Assert.DoesNotContain(world.Needs, n => n.ReasonCode == NeedReason.Distress);
        Assert.Equal(3, QuantityOf(world, NeedReason.DistantStock, Item.Firewood));
    }

    /// <summary>
    /// テスト表 #8(#257)。部分的に引かれた Need は Id と格納順を保ったまま数量が減る。ちょうど埋まった
    /// Need は消える。<c>NextNeedId</c> は動かない。
    /// </summary>
    /// <remarks>
    /// 落ちるべき条件: <c>Quantity &lt;= 0</c> ではなく <c>&lt; 0</c> で消す(ちょうど埋まった組が数量0で残る) /
    /// 消さずに0で残す / 並べ替える。
    /// </remarks>
    [Fact]
    public void NeedSubtractionRemovesFilledNeedsAndKeepsTheRest()
    {
        var definition = BuildNeedSubtractionDefinition();
        var shortPurposes = new[] { DemandPurpose.Necessity };
        int q = MeasurePurchases(definition, shortPurposes).Necessity;
        Assert.True(q > 0);

        var world = BuildNeedSubtractionWorld(shortPurposes);

        // 格納順 [A(部分), B(ちょうど埋まる), C(部分), D(買い物で埋まらない)]。Id は昇順ではない並びにする。
        world.Needs.Add(MakeNeed(9, NeedReason.DistantStock, Item.Firewood, q + 5)); // A: 残り 5
        world.Needs.Add(MakeNeed(2, NeedReason.Distress, Item.Firewood, q)); // B: ちょうど埋まる
        world.Needs.Add(MakeNeed(5, NeedReason.Distress, Item.Beer, 7)); // C: 嗜好の購入は引かない
        world.Needs.Add(MakeNeed(3, NeedReason.CannotExpandProduction, Item.Bread, 400)); // D
        int nextNeedIdBefore = world.NextNeedId;

        EconomySystemTestFixtures.RunTradeDays(world, definition, days: 1);

        Assert.Equal(
            new[] { (9, 5), (5, 7), (3, 400) },
            world.Needs.Select(n => (n.Id, n.Quantity)).ToArray());
        Assert.Equal(nextNeedIdBefore, world.NextNeedId);
    }

    /// <summary>
    /// テスト表 #8(#257)。差し引きで消えた組が翌日また立つと新しい Id(<c>NextNeedId</c> から)になり、
    /// 残った組(買い物で埋まらない <c>CannotExpandProduction</c>)は同じ Id を引き継ぐ。
    /// </summary>
    [Fact]
    public void ReappearingNeedGetsAFreshIdAndSurvivingNeedKeepsItsId()
    {
        var definition = BuildNeedSubtractionDefinition();
        var shortPurposes = new[] { DemandPurpose.Necessity };
        int q = MeasurePurchases(definition, shortPurposes).Necessity;
        Assert.True(q > 0);

        var world = BuildNeedSubtractionWorld(shortPurposes);
        var household = world.Households[0];
        household.IsBankrupt = 1;
        household.UnmetConsumption[Item.Firewood] = q; // 困窮: 必需の購入ちょうどで埋まる。

        var systems = new ISimSystem[]
        {
            new NeedGenerationSystem(definition), new TradePlanningSystem(definition), new TradeSystem(definition),
        };
        var scheduler = new SimScheduler(systems, new RandomSource(1));

        scheduler.Advance(world, ticks: 1); // 1日目の0時: 順4 が Need を立てる。

        int distressId = world.Needs.Single(n => n.ReasonCode == NeedReason.Distress).Id;
        int cannotExpandId = world.Needs.Single(n => n.ReasonCode == NeedReason.CannotExpandProduction).Id;
        int nextNeedId = world.NextNeedId;

        scheduler.Advance(world, ticks: 23); // 1日目の18時: 必需を q 個買って困窮が消える。

        Assert.DoesNotContain(world.Needs, n => n.ReasonCode == NeedReason.Distress);
        Assert.Equal(nextNeedId, world.NextNeedId);

        scheduler.Advance(world, ticks: 1); // 2日目の0時。

        var distress = world.Needs.Single(n => n.ReasonCode == NeedReason.Distress);
        Assert.NotEqual(distressId, distress.Id);
        Assert.Equal(nextNeedId, distress.Id);
        Assert.Equal(cannotExpandId, world.Needs.Single(n => n.ReasonCode == NeedReason.CannotExpandProduction).Id);
    }

    /// <summary>
    /// テスト表 #9(#257)。窓口からの輸入だけで買った品目の <c>DistantStock</c> が引かれる
    /// (GDD02b §8.1「売り手が誰かは問わない」)。
    /// </summary>
    /// <remarks>落ちるべき条件: 段8 の記録を <c>TradeSettlement.Execute</c>(都市内)の後にだけ置く。</remarks>
    [Fact]
    public void NeedSubtractionCountsWindowImports()
    {
        var definition = BuildNeedSubtractionDefinition();
        var world = BuildNeedSubtractionWorld(DemandPurpose.Necessity);
        world.Needs.Add(MakeNeed(0, NeedReason.DistantStock, Item.Firewood, SeededNeedQuantity));

        EconomySystemTestFixtures.RunTradeDays(world, definition, days: 1);

        // 前提: 売り手は窓口しか居ない。
        var purchases = world.Ledgers[0].Where(e => e.Direction == LedgerDirection.Purchase && e.ItemId == Item.Firewood).ToArray();
        Assert.NotEmpty(purchases);
        Assert.All(purchases, e => Assert.Equal(HouseholdState.ExternalMarketSellerId, e.CounterpartyId));

        int bought = purchases.Sum(e => e.Quantity);
        Assert.Equal(SeededNeedQuantity - bought, QuantityOf(world, NeedReason.DistantStock, Item.Firewood));
    }

    /// <summary>
    /// テスト表 #15(#257)。<c>TradePlanning</c> の系統は 14。本物の登録順の系統が重複しない
    /// (<c>SimScheduler</c> が構築できる)。
    /// </summary>
    /// <remarks>落ちるべき条件: 値を振り直す(14 以外にする / 他の系統と重ねる)。</remarks>
    [Fact]
    public void TradePlanningStreamIs14()
    {
        var definition = WorldDefinition.M0;

        Assert.Equal((RandomStream)14, new TradePlanningSystem(definition).Stream);

        var systems = FullPipeline(definition, new CountingSink());
        var streams = systems.Select(s => s.Stream).ToArray();

        Assert.Equal(streams.Length, streams.Distinct().Count());
        _ = new SimScheduler(systems, new RandomSource(1));
    }
}
