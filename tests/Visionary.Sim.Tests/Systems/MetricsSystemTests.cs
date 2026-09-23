using Visionary.Sim.Determinism;
using Visionary.Sim.Metrics;
using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="MetricsSystem"/>(順10、W2-20 タスク仕様)の検査。
/// </summary>
public sealed class MetricsSystemTests
{
    /// <summary>1日ぶんのスナップショットをそのまま溜める(本番の <c>CsvMetricsSink</c> と違い捨てない)。</summary>
    private sealed class FakeMetricsSink : IDailyMetricsSink
    {
        public List<Snapshot> Days { get; } = new();

        public void Write(in DailySnapshot snapshot) => Days.Add(new Snapshot(
            snapshot.Economy,
            snapshot.Prices.ToArray(),
            snapshot.Districts.ToArray(),
            snapshot.Households.ToArray(),
            snapshot.Trades));

        public readonly record struct Snapshot(
            EconomyRow Economy, PriceRow[] Prices, DistrictRow[] Districts, HouseholdRow[] Households, TradesRow Trades);
    }

    // ---- W2-08(TradeSystemTests)と同じ最小定義パターン。パン(必需候補)がMillerの出力、
    // 木材(1次産品)がMillerの入力。値を全部この定義側で握るため、金額の計算が手で追える。

    private static Recipe MillerRecipe() =>
        new(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = Item.Timber, Quantity = 1 } },
            laborPermille: 1000);

    private static int[] TargetStockDaysFor(params int[] itemIds)
    {
        var row = new int[Item.Count];

        foreach (int itemId in itemIds)
        {
            row[itemId] = 1;
        }

        return row;
    }

    private static WorldDefinition BuildDefinition(int breadFloor = 10, int grainFloor = 10, int shipmentDays = 1)
    {
        // Grain(itemId 0)はUnusedRecipe(Baker〜Smith)が生産するため、都市生産品として
        // 外部買値を持つ必要がある(WorldDefinitionのコンストラクタの検証)。
        var externalBuyPrice = new int[Item.Count];
        externalBuyPrice[Item.Bread] = breadFloor;
        externalBuyPrice[Item.Grain] = grainFloor;

        return EconomySystemTestFixtures.BuildDefinition(
            MillerRecipe(),
            dailyConsumptionPerNpcByRank: new[] { new int[Item.Count], new int[Item.Count], new int[Item.Count] },
            necessityTargetStockDays: TargetStockDaysFor(Item.Bread),
            preferenceTargetStockDays: new int[Item.Count],
            tolerancePermille: 1200,
            minimumMarginPermille: 0,
            opportunityCostBaseByOccupation: new[] { 1, 1, 1, 1, 1 },
            rankCoefficientPermille: new[] { 1000, 1000, 1000 },
            travelHoursPerDistrict: 1,
            shipmentDays: shipmentDays,
            inputBufferDays: 1,
            externalBuyPriceOverride: externalBuyPrice);
    }

    private static void AddHousehold(
        World world, int id, int districtId, Occupation occupation, int liquidFunds = 0)
    {
        world.Npcs[id].Rank = NpcRank.Master;
        world.Households[id] = new HouseholdState(
            id: id, districtId: districtId, headNpcId: id, memberNpcIds: new[] { id }, itemCount: Item.Count);
        world.Households[id].Occupation = occupation;
        world.Households[id].LiquidFunds = liquidFunds;
    }

    /// <summary><see cref="MetricsSystem"/> だけを1tick(1日)走らせる。段1〜段5bを一切通らない。</summary>
    private static FakeMetricsSink RunMetricsOnly(WorldDefinition definition, World world)
    {
        var sink = new FakeMetricsSink();
        var scheduler = new SimScheduler(
            new ISimSystem[] { new MetricsSystem(definition, sink) }, new RandomSource(1));
        scheduler.Advance(world, ticks: 1);

        return sink;
    }

    private static void AddLedgerEntry(
        World world, int ownerHouseholdId, LedgerDirection direction, int itemId, int counterpartyId,
        int unitPrice, int quantity, Tick occurredAt) =>
        world.Ledgers[ownerHouseholdId].Add(new LedgerEntry
        {
            CounterpartyId = counterpartyId,
            ItemId = itemId,
            Quantity = quantity,
            UnitPrice = unitPrice,
            OccurredAt = occurredAt,
            Terms = LedgerTerms.Cash,
            Direction = direction,
        });

    /// <summary>TDD01 §3.3 の(現時点で実装済みの)登録順。順6〜順9は存在しないので含まない。</summary>
    private static ISimSystem[] FullPipeline(WorldDefinition definition, IDailyMetricsSink sink) => new ISimSystem[]
    {
        new ProductionSystem(definition),
        new ConsumptionSystem(definition),
        new HouseholdSystem(definition),
        new NeedGenerationSystem(definition),
        new TradeSystem(definition),
        new MetricsSystem(definition, sink),
    };

    /// <summary>#1(核心)。MetricsSystem を登録した走行と登録しない走行のハッシュが一致する。</summary>
    /// <remarks>
    /// 変異(MetricsSystem.Step の中で <c>household.UnmetConsumption[0] = 0;</c> を1行足す)を
    /// 当てると、この等値検査は赤になる見込み(順10 が World を書き換えるため)。
    /// </remarks>
    [Fact]
    public void MetricsDoesNotChangeTheStateHash()
    {
        var definition = WorldDefinition.M0;
        var worldWithMetrics = WorldGenerator.Generate(definition, new RandomSource(1));
        var worldWithoutMetrics = WorldGenerator.Generate(definition, new RandomSource(1));

        var sink = new FakeMetricsSink();
        var schedulerWithMetrics = new SimScheduler(FullPipeline(definition, sink), new RandomSource(1));

        var schedulerWithoutMetrics = new SimScheduler(
            new ISimSystem[]
            {
                new ProductionSystem(definition),
                new ConsumptionSystem(definition),
                new HouseholdSystem(definition),
                new NeedGenerationSystem(definition),
                new TradeSystem(definition),
            },
            new RandomSource(1));

        schedulerWithMetrics.Advance(worldWithMetrics, ticks: 30 * 24);
        schedulerWithoutMetrics.Advance(worldWithoutMetrics, ticks: 30 * 24);

        Assert.Equal(StateHasher.Compute(worldWithoutMetrics), StateHasher.Compute(worldWithMetrics));
    }

    /// <summary>#2。走行後に world.Metrics の全欄を任意の値で埋めても状態ハッシュが変わらない。</summary>
    [Fact]
    public void MetricsScratchIsNotHashed()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));
        var sink = new FakeMetricsSink();
        var scheduler = new SimScheduler(FullPipeline(definition, sink), new RandomSource(1));
        scheduler.Advance(world, ticks: 5 * 24);

        ulong before = StateHasher.Compute(world);

        Array.Fill(world.Metrics.SellerHasNoReference, 999);
        Array.Fill(world.Metrics.SellerCoefficientCapped, 999);
        Array.Fill(world.Metrics.DemandLines, 999);
        Array.Fill(world.Metrics.DemandLinesWithoutKnownPrice, 999);
        Array.Fill(world.Metrics.InputBlockedByFunds, 999);
        Array.Fill(world.Metrics.PreviousCounterpartyId, 999);
        Array.Fill(world.Metrics.CurrentCounterpartyId, 999);

        ulong after = StateHasher.Compute(world);

        Assert.Equal(before, after);
    }

    /// <summary>#3(核心)。30日、日次の money_total の差が毎日 export_value − import_value に一致する。</summary>
    [Fact]
    public void MoneyTotalMovesOnlyByExportsAndImports()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));
        long previousMoney = (long)definition.InitialLiquidFunds * definition.HouseholdCount;

        var sink = new FakeMetricsSink();
        var scheduler = new SimScheduler(FullPipeline(definition, sink), new RandomSource(1));
        scheduler.Advance(world, ticks: 30 * 24);

        Assert.Equal(30, sink.Days.Count);

        foreach (var day in sink.Days)
        {
            long expectedDelta = (long)day.Economy.ExportValue - day.Economy.ImportValue;
            long actualDelta = (long)day.Economy.MoneyTotal - previousMoney;

            Assert.Equal(expectedDelta, actualDelta);

            previousMoney = day.Economy.MoneyTotal;
        }
    }

    /// <summary>
    /// #4(核心)。初日、必需(パン)が現金を持っていったあとの生産の入力(木材)の購入が
    /// 資金上限の切り詰め(段5b経路(2))で0になり、input_blocked_households が1以上になる。
    /// </summary>
    [Fact]
    public void InputBlockedCountsTheFirstDay()
    {
        const int BreadFloor = 10;
        var definition = BuildDefinition(breadFloor: BreadFloor);

        var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);
        AddHousehold(world, id: 0, districtId: 4, Occupation.Miller, liquidFunds: BreadFloor);
        AddHousehold(world, id: 1, districtId: 4, Occupation.Miller); // パンの売り手。
        world.Households[1].WorkshopInventory[Item.Bread] = 100;

        var sink = new FakeMetricsSink();
        var scheduler = new SimScheduler(
            new ISimSystem[] { new TradeSystem(definition), new MetricsSystem(definition, sink) },
            new RandomSource(1));
        scheduler.Advance(world, ticks: 24);

        Assert.Single(sink.Days);
        Assert.True(
            sink.Days[0].Economy.InputBlockedHouseholds >= 1,
            "初日に input_blocked_households が立たなかった"
                + "(段5b経路(2)、資金上限の切り詰めの配線が落ちている可能性)。");
    }

    /// <summary>
    /// #5(核心)。流動資金0の世帯は、生産の入力の店が1件も選ばれない日でも
    /// input_blocked_households が1になる(段4の CashCap==0 だけで拾える)。
    /// </summary>
    [Fact]
    public void InputBlockedCountsTheDayWithNoStore()
    {
        var definition = BuildDefinition(breadFloor: 10);

        // 中心区画(4)から離れた区画に1戸だけ置く。資金0なので何も買う見込みが無く、
        // ErrandPlannerが中心へ出向く理由も無い ── 木材(1次産品)は窓口でしか買えないので、
        // 窓口へ到達しない日は店が1件も選ばれない。
        var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);
        AddHousehold(world, id: 0, districtId: 0, Occupation.Miller, liquidFunds: 0);

        var sink = new FakeMetricsSink();
        var scheduler = new SimScheduler(
            new ISimSystem[] { new TradeSystem(definition), new MetricsSystem(definition, sink) },
            new RandomSource(1));
        scheduler.Advance(world, ticks: 24);

        Assert.Single(sink.Days);

        // 前提: 実際に何も約定していない(資金0なので当然)。
        Assert.Empty(world.Ledgers[0]);

        // 前提: 中心区画へ出向いていない(外出の労働損失が0)。窓口は中心に居るか訪問しないと
        // 到達できないので(GDD02d §2.2)、これは段5bが店を1件も選べなかったことの証拠になる ──
        // 段5bを通っていれば、この世帯日のInputBlockedByFundsは経路(1)からも立ちうる。
        Assert.Equal(0, world.Households[0].ErrandLaborLossPermille);

        Assert.Equal(
            1,
            sink.Days[0].Economy.InputBlockedHouseholds);
    }

    /// <summary>
    /// #24(別表。レビュー1巡目 I-a の訂正)。段5b の経路(2)(資金上限の切り詰め)<b>だけ</b>が
    /// input_blocked_households を立てる世帯日を作る。上の #4
    /// (<see cref="InputBlockedCountsTheFirstDay"/>)は経路(2)を核心に指定していたが、
    /// 実際には流動資金0の世帯が段4の CashCap==0 経路で先に1を立てるため、経路(2)の代入を
    /// 丸ごと削っても緑のままだった(実測、下記remarks)。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>流動資金1・耐久(工具)の目標在庫が大きい世帯を1戸だけ置く。</b>必需(パン)は
    /// この定義では1日消費量0(<c>dailyConsumptionPerNpcByRank</c> が全品目0)なので目標在庫0 ──
    /// 在庫圧力‰が0になり相場ゲートで弾かれ、必需は一切購入されない(流動資金を減らさない)。
    /// 段4の時点で生産の入力(木材)の CashCap = FloorDiv(流動資金1 − 必需の取り置き0, 1) = 1
    /// (0 ではない ── 段4経路はここでは立たない)。
    /// </para>
    /// <para>
    /// 段5bは耐久→生産の入力の順に処理する。耐久(工具1個、窓口価格1)を買うと流動資金が
    /// 1→0になり、続く生産の入力(木材、窓口価格1)は「店は見つかる(窓口は中心区画に常に届く)
    /// が資金が尽きている」状態になる ── <c>TradeSettlement.FundsCap(0, 1) == 0</c> で
    /// 経路(2)が立つ。事前に手計算した値(段4のCashCap=1)を <c>BuyerDemand.Build</c> を直接
    /// 呼んで確認済み(2026-09-24、この変更のための実測)。
    /// </para>
    /// </remarks>
    [Fact]
    public void InputBlockedCountsWhenFundsCapTruncates()
    {
        var definition = BuildDefinition(breadFloor: 10);

        // 中心区画(4)に置く ── 窓口(木材・工具はともに1次産品)へ移動せずに届く。
        var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);
        AddHousehold(world, id: 0, districtId: 4, Occupation.Miller, liquidFunds: 1);

        var sink = new FakeMetricsSink();
        var scheduler = new SimScheduler(
            new ISimSystem[] { new TradeSystem(definition), new MetricsSystem(definition, sink) },
            new RandomSource(1));
        scheduler.Advance(world, ticks: 24);

        Assert.Single(sink.Days);

        // 前提: 耐久(工具)は買えている(段4のCashCapが0ではないことの状況証拠)。
        Assert.Equal(1, world.Households[0].WorkshopInventory[Item.Tools]);

        // 前提: 生産の入力(木材)は買えていない(経路(2)で切り詰められたこと)。
        Assert.Equal(0, world.Households[0].WorkshopInventory[Item.Timber]);

        // 前提: 必需(パン)は資金不足に数えられていない(この世帯日を経路(2)だけで説明する)。
        Assert.Equal(0, world.Households[0].UnaffordableNecessityCount);

        Assert.Equal(1, sink.Days[0].Economy.InputBlockedHouseholds);
    }

    /// <summary>#6。売り注文を出さなかった世帯がいる日、seller_days が世帯数を下回る。</summary>
    [Fact]
    public void SellerDaysCountOnlyPostedOffers()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));
        var sink = new FakeMetricsSink();
        var scheduler = new SimScheduler(FullPipeline(definition, sink), new RandomSource(1));
        scheduler.Advance(world, ticks: 20 * 24);

        Assert.Contains(sink.Days, day => day.Economy.SellerDays < definition.HouseholdCount);
    }

    /// <summary>
    /// #7。初日(誰も観測を持たない)は、出品した売り手全員が seller_days_without_reference に
    /// 数えられ、offer_price が床(外部買値)に一致する。
    /// </summary>
    [Fact]
    public void SellerWithoutReferenceIsCountedAtTheFloor()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));
        var sink = new FakeMetricsSink();
        var scheduler = new SimScheduler(FullPipeline(definition, sink), new RandomSource(1));
        scheduler.Advance(world, ticks: 24);

        var day0 = sink.Days[0];

        Assert.True(day0.Economy.SellerDaysWithoutReference >= 1);
        Assert.Equal(day0.Economy.SellerDays, day0.Economy.SellerDaysWithoutReference);

        foreach (var price in day0.Prices)
        {
            if (price.OfferCount == 0)
            {
                continue;
            }

            Assert.Equal(price.ExternalBuyPrice, price.OfferMin);
            Assert.Equal(price.ExternalBuyPrice, price.OfferMax);
        }
    }

    /// <summary>
    /// #8'(上の#8を訂正。レビュー1巡目 I-b)。<see cref="OfferPrice.WasUnsoldCapApplied"/>
    /// 自体の分岐は <c>OfferPriceTests</c> が直接押さえる。ここでは配線を見る ──
    /// <b>相場基準が立たない日(初日)は seller_days_coefficient_capped が立たない</b>ことと、
    /// 相場基準があり・前日の約定が無く・在庫比の係数が1000‰を超える日に実際に1が立つことの
    /// 両方を確かめる(初日を「全員立つ日」として読んだ旧版は、初日が全売り手について
    /// 相場基準を持たない日であることを見落としていた)。
    /// </summary>
    [Fact]
    public void UnsoldCapIsCountedOnlyWhenItBites()
    {
        // 出荷目標在庫 = 生産能力1 × 出力数量1 × 出荷日数5 = 5。販売在庫1で在庫比200‰
        // → 係数1500-CeilDiv(200,2)=1400‰(>1000)。
        var definition = BuildDefinition(breadFloor: 10, shipmentDays: 5);

        var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);
        AddHousehold(world, id: 0, districtId: 0, Occupation.Miller);
        world.Households[0].WorkshopInventory[Item.Bread] = 1;

        // 他の売り手の観測を直接Knowledgeへ注入する(初日ぶん、Tick.Zero)。相場基準
        // (MarketReference.TrySeller)は「前日までの観測」しか有効と認めないので、
        // 初日(day0、dayDifference==0)はまだ無効 ── day1になって初めて有効になる。
        world.Knowledge[0].Add(new PriceObservation
        {
            ItemId = Item.Bread,
            LocationId = 0,
            Price = 10,
            SellerId = 1, // 実在しない世帯でよい(TrySellerはKnowledgeの記録しか読まない)。
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        });

        var sink = new FakeMetricsSink();
        var scheduler = new SimScheduler(
            new ISimSystem[] { new TradeSystem(definition), new MetricsSystem(definition, sink) },
            new RandomSource(1));
        scheduler.Advance(world, ticks: 2 * 24);

        Assert.Equal(2, sink.Days.Count);

        // 初日: 相場基準が無い(誰も他の売り手の観測を持たない)ので0。
        Assert.Equal(0, sink.Days[0].Economy.SellerDaysCoefficientCapped);

        // 2日目: 相場基準あり・前日の約定無し・係数1400‰(>1000) → 1。
        Assert.Equal(1, sink.Days[1].Economy.SellerDaysCoefficientCapped);
    }

    /// <summary>#9。全日・全品目で offer_at_floor_with_export_count <= offer_at_floor_count。</summary>
    [Fact]
    public void FloorWithExportIsASubsetOfFloor()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));
        var sink = new FakeMetricsSink();
        var scheduler = new SimScheduler(FullPipeline(definition, sink), new RandomSource(1));
        scheduler.Advance(world, ticks: 10 * 24);

        bool sawStrictSubset = false;

        foreach (var day in sink.Days)
        {
            Assert.True(day.Economy.SellerDaysAtFloorWithExport <= day.Economy.SellerDaysAtFloor);

            foreach (var price in day.Prices)
            {
                Assert.True(price.OfferAtFloorWithExportCount <= price.OfferAtFloorCount);

                if (price.OfferAtFloorWithExportCount < price.OfferAtFloorCount)
                {
                    sawStrictSubset = true;
                }
            }
        }

        Assert.True(sawStrictSubset, "床に居るが輸出していない売り手日が1件も観測されなかった(値の問題の可能性)。");
    }

    /// <summary>#10。単価10と13の約定が1件ずつの日、settled_median が12になる(切り下げなら11)。</summary>
    [Fact]
    public void SettledMedianIsRoundedUpOnEvenCounts()
    {
        var definition = WorldDefinition.M0;
        var world = new World(npcCount: 1, householdCount: 1, itemCount: definition.ItemCount);

        AddLedgerEntry(world, ownerHouseholdId: 0, LedgerDirection.Purchase, Item.Bread,
            counterpartyId: 1, unitPrice: 10, quantity: 1, occurredAt: Tick.Zero);
        AddLedgerEntry(world, ownerHouseholdId: 0, LedgerDirection.Purchase, Item.Bread,
            counterpartyId: 1, unitPrice: 13, quantity: 1, occurredAt: Tick.Zero);

        var sink = RunMetricsOnly(definition, world);

        var row = sink.Days[0].Prices.Single(price => price.ItemId == Item.Bread);
        Assert.Equal(12, row.SettledMedian);
    }

    /// <summary>#11。都市内の約定が1件(買い手・売り手の2行)ある日、settled_count が1(2ではない)。</summary>
    [Fact]
    public void SettledStatisticsCountBuyerRowsOnly()
    {
        var definition = WorldDefinition.M0;
        var world = new World(npcCount: 2, householdCount: 2, itemCount: definition.ItemCount);

        AddLedgerEntry(world, ownerHouseholdId: 0, LedgerDirection.Purchase, Item.Bread,
            counterpartyId: 1, unitPrice: 50, quantity: 1, occurredAt: Tick.Zero);
        AddLedgerEntry(world, ownerHouseholdId: 1, LedgerDirection.Sale, Item.Bread,
            counterpartyId: 0, unitPrice: 50, quantity: 1, occurredAt: Tick.Zero);

        var sink = RunMetricsOnly(definition, world);

        var row = sink.Days[0].Prices.Single(price => price.ItemId == Item.Bread);
        Assert.Equal(1, row.SettledCount);
    }

    /// <summary>#12。全日・全品目で window_settled_count <= settled_count。窓口からの輸入しか無い日は一致する。</summary>
    [Fact]
    public void WindowPurchasesAreASubsetOfSettlements()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));
        var sink = new FakeMetricsSink();
        var scheduler = new SimScheduler(FullPipeline(definition, sink), new RandomSource(1));
        scheduler.Advance(world, ticks: 2 * 24);

        foreach (var day in sink.Days)
        {
            foreach (var price in day.Prices)
            {
                Assert.True(price.WindowSettledCount <= price.SettledCount);
            }
        }

        // 初日の1次産品(穀物)は窓口からしか買えない ── 両者が一致する。
        var grainDay0 = sink.Days[0].Prices.Single(price => price.ItemId == Item.Grain);
        Assert.True(grainDay0.SettledCount >= 1);
        Assert.Equal(grainDay0.SettledCount, grainDay0.WindowSettledCount);
    }

    /// <summary>#13(核心)。区画Aの買い手が区画Bの売り手から買った日、districts.csv の行の district_id がA。</summary>
    [Fact]
    public void DistrictRowsUseTheBuyerDistrict()
    {
        var definition = WorldDefinition.M0;
        var world = new World(npcCount: 2, householdCount: 2, itemCount: definition.ItemCount);
        world.Households[0] = new HouseholdState(
            id: 0, districtId: 0, headNpcId: 0, memberNpcIds: new[] { 0 }, itemCount: definition.ItemCount);
        world.Households[1] = new HouseholdState(
            id: 1, districtId: 5, headNpcId: 1, memberNpcIds: new[] { 1 }, itemCount: definition.ItemCount);

        AddLedgerEntry(world, ownerHouseholdId: 0, LedgerDirection.Purchase, Item.Bread,
            counterpartyId: 1, unitPrice: 50, quantity: 1, occurredAt: Tick.Zero);

        var sink = RunMetricsOnly(definition, world);

        var row = sink.Days[0].Districts.Single(district => district.ItemId == Item.Bread);
        Assert.Equal(0, row.DistrictId);
    }

    /// <summary>
    /// #14(核心)。パン屋(出力2個/回)が6回実行した日、run_cost × 6 が引かれる(× 12ではない)。
    /// </summary>
    [Fact]
    public void ProfitUsesRunsNotOutputUnits()
    {
        var definition = WorldDefinition.M0;
        var world = new World(npcCount: 1, householdCount: 1, itemCount: definition.ItemCount);

        var household = world.Households[0];
        household.Occupation = Occupation.Baker; // 出力2個/回(GDD02d §4.4)。
        household.ProductionRuns = 6;
        household.PurchaseUnitCostAverage[Item.Flour] = 50;
        household.PurchaseUnitCostAverage[Item.Firewood] = 10;
        household.PurchaseUnitCostAverage[Item.Tools] = 300;

        var recipe = definition.Recipes[(int)Occupation.Baker];
        int wearCostPerRun = BuyerBudget.WearCostPerRun(300, recipe.LaborPermille, definition.ToolLifeLaborDays);
        int expectedRunCost = 50 + 10 + wearCostPerRun; // Flour×1 + Firewood×1 + 摩耗費。
        int expectedProfit = -expectedRunCost * 6; // 売上0。× 6 であって × 12(出力個数)ではない。

        var sink = RunMetricsOnly(definition, world);

        var row = sink.Days[0].Households.Single(h => h.HouseholdId == 0);
        Assert.Equal(expectedRunCost, row.RunCost);
        Assert.Equal(expectedProfit, row.Profit);
    }

    /// <summary>#15。工具の移動平均単価を上げると run_cost が上がる。</summary>
    [Fact]
    public void ProfitIncludesWearCost()
    {
        int RunCostFor(int toolUnitCostAverage)
        {
            var definition = WorldDefinition.M0;
            var world = new World(npcCount: 1, householdCount: 1, itemCount: definition.ItemCount);
            var household = world.Households[0];
            household.Occupation = Occupation.Baker;
            household.ProductionRuns = 1;
            household.PurchaseUnitCostAverage[Item.Tools] = toolUnitCostAverage;

            var sink = RunMetricsOnly(definition, world);

            return sink.Days[0].Households.Single(h => h.HouseholdId == 0).RunCost;
        }

        Assert.True(RunCostFor(1000) > RunCostFor(100));
    }

    /// <summary>#16。予想在庫が目標在庫以上の行は demand_lines に入らない。</summary>
    [Fact]
    public void DemandLinesCountOnlyShortfallLines()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));

        // 全世帯の在庫を目標をはるかに超えるまで積み増す(需要が構造的に無い状態を作る)。
        foreach (var household in world.Households)
        {
            for (int itemId = 0; itemId < definition.ItemCount; itemId++)
            {
                household.HouseholdInventory[itemId] += 1_000_000;
                household.WorkshopInventory[itemId] += 1_000_000;
            }
        }

        var sink = new FakeMetricsSink();
        var scheduler = new SimScheduler(
            new ISimSystem[] { new TradeSystem(definition), new MetricsSystem(definition, sink) },
            new RandomSource(1));
        scheduler.Advance(world, ticks: 24);

        // 前提: BuyerDemand.Build 自体は在庫の過不足に関係なく行を立てる(規則8の表)。
        var buyerDemand = new BuyerDemand(definition);
        bool anyLinesBuilt = world.Households.Any(household =>
            buyerDemand.Build(world, household, hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0)
                .Lines.Count > 0);

        Assert.True(anyLinesBuilt, "BuyerDemand.Build が1行も作らなかった(前提条件が崩れている)。");
        Assert.Equal(0, sink.Days[0].Economy.DemandLines);
    }

    /// <summary>#17。前日も当日も同じ品目を買った (世帯,品目) が0件の日、partner_switch_permille が-1。</summary>
    [Fact]
    public void PartnerSwitchIsUndefinedWithoutABase()
    {
        var definition = WorldDefinition.M0;
        var world = new World(npcCount: 1, householdCount: 1, itemCount: definition.ItemCount);

        var sink = RunMetricsOnly(definition, world);

        Assert.Equal(-1, sink.Days[0].Trades.PartnerSwitchPermille);
    }

    /// <summary>#18。都市内の約定が0件の日、hhi_permille_squared が-1。</summary>
    [Fact]
    public void HhiIsUndefinedWithoutInternalSettlements()
    {
        var definition = WorldDefinition.M0;
        var world = new World(npcCount: 1, householdCount: 1, itemCount: definition.ItemCount);

        var sink = RunMetricsOnly(definition, world);

        Assert.Equal(-1, sink.Days[0].Trades.HhiPermilleSquared);
    }
}
