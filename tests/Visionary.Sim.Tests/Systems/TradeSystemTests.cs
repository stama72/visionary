using Visionary.Sim.Determinism;
using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="TradeSystem"/>(GDD02c §1・§1.4 / GDD02b §3.2、W2-08 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class TradeSystemTests
{
    // Millerのレシピはパン(必需)を出力し木材(1次産品)を消費する。木材以外(小麦粉4)には触れない
    // ── UnusedRecipe(Baker〜Smith)が品目0(穀物)を出力・品目1(木材)を消費するので、木材を
    // 共有しても整合する。穀物は必需/嗜好のもう一方の品目として使う。
    private static Recipe MillerBreadRecipe() =>
        new(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = Item.Timber, Quantity = 1 } },
            laborPermille: 1000);

    private static int[][] ConsumptionTable()
    {
        var row = new int[Item.Count];
        row[Item.Bread] = 1;
        row[Item.Grain] = 1;

        return new[] { (int[])row.Clone(), (int[])row.Clone(), (int[])row.Clone() };
    }

    private static int[] TargetStockDaysFor(params int[] itemIds)
    {
        var row = new int[Item.Count];
        foreach (int itemId in itemIds)
        {
            row[itemId] = 1;
        }

        return row;
    }

    /// <summary>
    /// パン(必需の候補)・穀物(必需/嗜好のもう一方の候補、UnusedRecipeが生産する)の2品目に
    /// 床を持たせた定義。既定は両方とも床10(価格1だとFundsCapがCashCapを下回れず段5の
    /// 経路(2)を再現できないため)。
    /// </summary>
    private static WorldDefinition BuildShoppingDefinition(
        int breadFloor = 10,
        int grainFloor = 10,
        int[]? necessityTargetStockDays = null,
        int[]? preferenceTargetStockDays = null,
        int tolerancePermille = 1200,
        int minimumMarginPermille = 0)
    {
        var externalBuyPrice = new int[Item.Count];
        externalBuyPrice[Item.Bread] = breadFloor;
        externalBuyPrice[Item.Grain] = grainFloor;

        return EconomySystemTestFixtures.BuildDefinition(
            MillerBreadRecipe(),
            dailyConsumptionPerNpcByRank: ConsumptionTable(),
            necessityTargetStockDays: necessityTargetStockDays ?? new int[Item.Count],
            preferenceTargetStockDays: preferenceTargetStockDays ?? new int[Item.Count],
            tolerancePermille: tolerancePermille,
            minimumMarginPermille: minimumMarginPermille,
            opportunityCostBaseByOccupation: new[] { 1, 1, 1, 1, 1 },
            rankCoefficientPermille: new[] { 1000, 1000, 1000 },
            travelHoursPerDistrict: 1,
            shipmentDays: 1,
            inputBufferDays: 1,
            externalBuyPriceOverride: externalBuyPrice);
    }

    /// <summary>世帯を1戸、指定の区画・職業で作る(単独NPC世帯)。</summary>
    private static void AddHousehold(
        World world, int id, int districtId, Occupation occupation, int liquidFunds = 0)
    {
        world.Npcs[id].Rank = NpcRank.Master;
        world.Households[id] = new HouseholdState(
            id: id, districtId: districtId, headNpcId: id, memberNpcIds: new[] { id }, itemCount: Item.Count);
        world.Households[id].Occupation = occupation;
        world.Households[id].LiquidFunds = liquidFunds;
    }

    /// <summary>
    /// テスト表 #7。相場基準が立たない日の提示価格が床ちょうど。販売在庫をどう動かしても床のまま
    /// (<c>OfferPrice.Calculate</c> を呼ばない ── 呼び出し側が床をそのまま使う。GDD02c §1)。
    /// </summary>
    [Fact]
    public void OfferPriceIsFloorWithoutReference()
    {
        var definition = BuildShoppingDefinition(breadFloor: 80);
        int floor = 80;

        foreach (int sellableStock in new[] { 0, 1, 100, 1000 })
        {
            var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);
            AddHousehold(world, id: 0, districtId: 4, Occupation.Miller);
            world.Households[0].WorkshopInventory[Item.Bread] = sellableStock;

            EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1);

            if (sellableStock <= 0)
            {
                Assert.False(world.Market.ContainsKey(new MarketKey(Item.Bread, 0)));
                continue;
            }

            Assert.Equal(floor, world.Market[new MarketKey(Item.Bread, 0)]);
        }
    }

    /// <summary>テスト表 #24(旧番)。出力在庫0(入力切れで生産停止)の売り手のMarketKeyがMarketに無い。</summary>
    [Fact]
    public void NoOfferIsPostedWhenSellableStockIsZero()
    {
        var definition = BuildShoppingDefinition();
        var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);
        AddHousehold(world, id: 0, districtId: 4, Occupation.Miller);
        world.Households[0].WorkshopInventory[Item.Bread] = 0;

        EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1);

        Assert.False(world.Market.ContainsKey(new MarketKey(Item.Bread, 0)));
    }

    /// <summary>テスト表 #25(旧番)。前日出品した売り手の在庫を0にして1日進めると、エントリが消える。</summary>
    [Fact]
    public void StaleOfferIsRemovedWhenStockRunsOut()
    {
        var definition = BuildShoppingDefinition();
        var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);
        AddHousehold(world, id: 0, districtId: 4, Occupation.Miller);
        world.Households[0].WorkshopInventory[Item.Bread] = 5;

        var system = new TradeSystem(definition);
        var key = new MarketKey(Item.Bread, 0);

        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.True(world.Market.ContainsKey(key));

        world.Households[0].WorkshopInventory[Item.Bread] = 0;
        EconomySystemTestFixtures.RunDays(world, system, days: 1);

        Assert.False(world.Market.ContainsKey(key));
    }

    /// <summary>
    /// 1日目に売れ残った売り手(hasSettled=false)は、2日目も自分の錨を使わない ──
    /// 前日の提示価格ではなく約定単価だけが候補である半分だけをここで確かめる(TDD01 §3.2・§3.3)。
    /// <b>hasSettled=true の日、すなわち錨が実際に効く経路は
    /// <see cref="SellerAnchorUsesYesterdaysSettledPriceWhenASaleOccurred"/>(別表B-1)が持つ。</b>
    /// </summary>
    [Fact]
    public void SellerAnchorsOnSettledPriceNotOnItsOwnPreviousOffer()
    {
        const int OtherSellerId = 999;

        var definition = BuildShoppingDefinition(breadFloor: 10);
        var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);
        AddHousehold(world, id: 0, districtId: 4, Occupation.Miller);
        // 出荷目標在庫ちょうど(生産能力1×出力数量1×出荷日数1 = 1)に合わせる ── 在庫比を
        // 1000‰(価格係数1000‰)に保ち、相場基準がそのまま提示価格に出るようにする。
        world.Households[0].WorkshopInventory[Item.Bread] = 1;

        var system = new TradeSystem(definition);
        var key = new MarketKey(Item.Bread, 0);

        EconomySystemTestFixtures.RunDays(world, system, days: 1); // 1日目: 買い手不在で売れ残る
        Assert.Equal(10, world.Market[key]); // 床のまま

        // 他の売り手の観測を1件仕込む(前日=1日目の日付)。
        world.Knowledge[0].Add(new PriceObservation
        {
            ItemId = Item.Bread,
            LocationId = 0,
            Price = 200,
            SellerId = OtherSellerId,
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        });

        EconomySystemTestFixtures.RunDays(world, system, days: 1); // 2日目

        // household0は1日目に1件も売れていない(hasSettled=false)ので、自分の約定単価は
        // 平均に混ざらない。相場基準 = 200(他の売り手の観測のみ)。在庫比1000‰(目標どおり)。
        Assert.Equal(200, world.Market[key]);
    }

    /// <summary>
    /// 【核心】別表B-1。1日目に実際に約定した売り手は、2日目の相場基準に自分の前日の約定単価が
    /// 混ざり、「他の売り手の観測だけ」から作った値とは異なる価格になる(TDD01 §3.2・§3.3、
    /// GDD02c §1.2「売り手2世帯の交互振動モードを消す」仕掛け)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-21)。</b><c>TradeSystem.Step</c> が段1 で
    /// <c>MarketReference.TrySeller</c> へ渡す <c>hasSettled, settledPrice</c> を
    /// 常に <c>false, 0</c> に置換する変異(タスク仕様 別表B-1 が名指し)を当てたところ、
    /// <c>Assert.Equal(105, world.Market[sellerKey])</c> が実際値200(他の売り手の観測200だけの
    /// 平均になり、自分の約定単価10が混ざらない)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void SellerAnchorUsesYesterdaysSettledPriceWhenASaleOccurred()
    {
        const int OtherSellerId = 999;

        var definition = BuildShoppingDefinition(
            breadFloor: 10, necessityTargetStockDays: TargetStockDaysFor(Item.Bread));
        var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);

        // household0: 売り手(パン)。1日目に床(10)で1個売れるだけの在庫を持つ。
        AddHousehold(world, id: 0, districtId: 4, Occupation.Miller, liquidFunds: 0);
        world.Households[0].WorkshopInventory[Item.Bread] = 2;

        // household1: 買い手。1日目に床(10)で1単位だけ買える資金を持つ。
        AddHousehold(world, id: 1, districtId: 4, Occupation.Woodworker, liquidFunds: 10);

        var system = new TradeSystem(definition);
        var sellerKey = new MarketKey(Item.Bread, 0);

        EconomySystemTestFixtures.RunDays(world, system, days: 1); // 1日目

        // 1日目に実際に1個約定したことを帳簿で確かめる(hasSettled=trueの前提)。
        Assert.Contains(
            world.Ledgers[0],
            entry => entry.Direction == LedgerDirection.Sale && entry.ItemId == Item.Bread
                && entry.Quantity == 1 && entry.UnitPrice == 10);
        // 売れた1個ぶん在庫が減り、出荷目標在庫(1)ちょうどに戻る(在庫比1000‰を2日目も保つ)。
        Assert.Equal(1, world.Households[0].WorkshopInventory[Item.Bread]);

        // 他の売り手の観測を1件仕込む(前日=1日目の日付)。
        world.Knowledge[0].Add(new PriceObservation
        {
            ItemId = Item.Bread,
            LocationId = 0,
            Price = 200,
            SellerId = OtherSellerId,
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        });

        EconomySystemTestFixtures.RunDays(world, system, days: 1); // 2日目

        // 相場基準 = CeilDiv(200 + 10, 2) = 105(他の売り手の観測200 + 自分の前日の約定単価10)。
        // 「他の売り手の値だけ」(200)とは異なる。
        Assert.Equal(105, world.Market[sellerKey]);
    }

    /// <summary>
    /// 【核心】レビュー指摘C-1。<c>household.IsBankrupt</c> が段1から <c>OfferPrice.Calculate</c>
    /// まで実際に届いていること。破産中フラグを直接立てた売り手の提示価格が、価格係数‰を
    /// 500に固定した値(在庫比を無視)で出る。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-21)。</b><c>TradeSystem.Step</c> が <c>OfferPrice.Calculate</c> へ渡す
    /// 第5引数 <c>household.IsBankrupt</c> を <c>0</c> に置換する変異(タスク仕様C-1が名指し)を
    /// 当てたところ、<c>Assert.Equal(100, world.Market[key])</c> が実際値200(在庫比1000‰・
    /// 健全時の係数のまま、<c>ApplyPermille(200,1000)=200</c>)で失敗した(赤を確認)。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void BankruptSellerPostsTheHalvedFloorInThePipeline()
    {
        const int OtherSellerId = 999;

        var definition = BuildShoppingDefinition(breadFloor: 10);
        var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);
        AddHousehold(world, id: 0, districtId: 4, Occupation.Miller);
        // 出荷目標在庫ちょうど(1)に合わせる ── 破産中でなければ在庫比1000‰(健全時の係数1000‰)に
        // なる配置で、破産中の固定係数500‰との差が観測できるようにする。
        world.Households[0].WorkshopInventory[Item.Bread] = 1;
        world.Households[0].IsBankrupt = 1;

        var system = new TradeSystem(definition);
        var key = new MarketKey(Item.Bread, 0);

        EconomySystemTestFixtures.RunDays(world, system, days: 1); // 1日目: 相場基準が無く床のまま
        Assert.Equal(10, world.Market[key]);

        // 他の売り手の観測を1件仕込む(前日=1日目の日付)。
        world.Knowledge[0].Add(new PriceObservation
        {
            ItemId = Item.Bread,
            LocationId = 0,
            Price = 200,
            SellerId = OtherSellerId,
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        });

        EconomySystemTestFixtures.RunDays(world, system, days: 1); // 2日目

        // 相場基準 = 200(自分は1日目に売れていないのでhasSettled=false、他の売り手の観測のみ)。
        // 破産中は価格係数‰を500に固定する: ApplyPermille(200,500) = 100。
        // 健全なら在庫比1000‰(係数1000‰)で200になるはずなので、100はその半分である。
        Assert.Equal(100, world.Market[key]);
    }

    /// <summary>
    /// 【核心】レビュー指摘C-2。販売在庫は工房在庫の出力品目だけであり、世帯在庫(消費財として
    /// 持つぶん)は含まない(GDD02c §1.3)。パンは工房在庫(出力)と世帯在庫(必需の消費財)の
    /// 両方に載りうる品目なので、世帯在庫を足しても提示価格が変わらないことを確かめる。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-21)。</b><c>TradeSystem.Step</c> の
    /// <c>sellableStock = household.WorkshopInventory[outputItemId]</c> を
    /// <c>+ household.HouseholdInventory[outputItemId]</c> する変異(タスク仕様C-2が名指し)を
    /// 当てたところ、世帯在庫100を積んだケースの2日目の提示価格が100(在庫比が跳ね上がり
    /// 係数がclampの下限500‰へ落ちる: <c>ApplyPermille(200,500)=100</c>)になり、積まない場合の
    /// 200と食い違って <c>Assert.Equal(priceWithoutHouseholdStock, priceWithHouseholdStock)</c> が
    /// 失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void SellableStockIsOnlyTheWorkshopOutputInventory()
    {
        const int OtherSellerId = 999;
        var definition = BuildShoppingDefinition(breadFloor: 10);

        int RunAndGetPrice(int householdInventoryBread)
        {
            var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);
            AddHousehold(world, id: 0, districtId: 4, Occupation.Miller);
            // 出荷目標在庫ちょうど(1)。世帯在庫(householdInventoryBread)は別軸として与える。
            world.Households[0].WorkshopInventory[Item.Bread] = 1;
            world.Households[0].HouseholdInventory[Item.Bread] = householdInventoryBread;

            var system = new TradeSystem(definition);
            EconomySystemTestFixtures.RunDays(world, system, days: 1); // 1日目: 床のまま

            world.Knowledge[0].Add(new PriceObservation
            {
                ItemId = Item.Bread,
                LocationId = 0,
                Price = 200,
                SellerId = OtherSellerId,
                ObservedAt = Tick.Zero,
                Source = ObservationSource.Direct,
            });

            EconomySystemTestFixtures.RunDays(world, system, days: 1); // 2日目: 相場基準が立つ

            return world.Market[new MarketKey(Item.Bread, 0)];
        }

        int priceWithoutHouseholdStock = RunAndGetPrice(householdInventoryBread: 0);
        int priceWithHouseholdStock = RunAndGetPrice(householdInventoryBread: 100);

        // 絶対値も固定する(相場基準200、在庫比1000‰なので係数1000‰でそのまま200)。
        // 2回の実行の相等だけで判定すると、在庫比が常にclampの外に出る変異(両者とも係数500‰の
        // 同じ値に落ちて偶然一致する)を見逃す。
        Assert.Equal(200, priceWithoutHouseholdStock);
        Assert.Equal(priceWithoutHouseholdStock, priceWithHouseholdStock);
    }

    /// <summary>
    /// 段1の値付けが <c>MarketReference.TrySeller</c> へ渡す2つの異なるId(観測を読む
    /// <c>household.HeadNpcId</c> と、自分を除外する <c>household.Id</c>)を取り違えないこと
    /// (TDD01 §3.2「取り違えを型で防げない」経路)。世帯主のNpcIdを世帯Idとわざと違える。
    /// </summary>
    [Fact]
    public void SellerReferenceReadsHeadNpcKnowledgeAndExcludesTheHouseholdIdNotTheNpcId()
    {
        const int HeadNpcId = 2; // household.Id(0)とわざと違える。

        var definition = BuildShoppingDefinition(breadFloor: 10);
        var world = new World(npcCount: 3, householdCount: 1, itemCount: Item.Count);
        world.Npcs[HeadNpcId].Rank = NpcRank.Master;
        world.Households[0] = new HouseholdState(
            id: 0, districtId: 4, headNpcId: HeadNpcId, memberNpcIds: new[] { HeadNpcId }, itemCount: Item.Count);
        world.Households[0].Occupation = Occupation.Miller;
        world.Households[0].WorkshopInventory[Item.Bread] = 1; // 出荷目標在庫ちょうど(係数1000‰)。

        // 世帯主(NpcId=2)のKnowledgeへ2件仕込む。自分の売り注文(SellerId=household.Id=0)は
        // 除外され、他の売り手(SellerId=headNpcId=2。この世界には存在しない世帯Idだが、
        // 取り違えを検出するための値)は含まれるべき。
        world.Knowledge[HeadNpcId].Add(new PriceObservation
        {
            ItemId = Item.Bread,
            LocationId = 0,
            Price = 999,
            SellerId = 0,
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        });
        world.Knowledge[HeadNpcId].Add(new PriceObservation
        {
            ItemId = Item.Bread,
            LocationId = 0,
            Price = 200,
            SellerId = HeadNpcId,
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        });

        EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1); // 1日目
        EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1); // 2日目

        // 相場基準 = 200(SellerId=0の自己観測999は除外される)。household.Idで
        // Knowledgeを引く変異では観測そのものが見つからず床(10)のまま、selfHouseholdIdに
        // headNpcIdを渡す変異では999のほうが残って999になる。
        Assert.Equal(200, world.Market[new MarketKey(Item.Bread, 0)]);
    }

    /// <summary>テスト表 #28(旧番)。入力0件は許容される(原価が値付けに入らないため)。出力2件以上でNotSupportedException。</summary>
    [Fact]
    public void TradeSystemRejectsMultiOutputRecipesAtConstruction()
    {
        var noInputRecipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 1000);

        var noInputDefinition = EconomySystemTestFixtures.BuildDefinition(noInputRecipe);
        // 入力0件は許容される(#97で「入力0件のレシピを拒む検査」は消えた)。
        _ = new TradeSystem(noInputDefinition);

        var manyOutputsRecipe = new Recipe(
            Occupation.Miller,
            outputs: new[]
            {
                new ItemQuantity { ItemId = Item.Flour, Quantity = 1 },
                new ItemQuantity { ItemId = Item.Bread, Quantity = 1 },
            },
            inputs: new[] { new ItemQuantity { ItemId = Item.Grain, Quantity = 1 } },
            laborPermille: 1000);

        var manyOutputsDefinition = EconomySystemTestFixtures.BuildDefinition(manyOutputsRecipe);
        Assert.Throws<NotSupportedException>(() => new TradeSystem(manyOutputsDefinition));
    }

    /// <summary>テスト表 #32。同じシードで2回走らせると状態ハッシュが一致する。乱数を引かない。</summary>
    [Fact]
    public void TradePipelineStillRunsDeterministically()
    {
        var definition = BuildShoppingDefinition();

        ulong RunWithSeed(long seed)
        {
            var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);
            AddHousehold(world, id: 0, districtId: 4, Occupation.Miller, liquidFunds: 1000);
            AddHousehold(world, id: 1, districtId: 1, Occupation.Baker); // Grainの売り手(UnusedRecipe)
            world.Households[0].WorkshopInventory[Item.Bread] = 5;
            world.Households[1].WorkshopInventory[Item.Grain] = 5;

            var scheduler = new SimScheduler(
                new ISimSystem[] { new TradeSystem(definition) }, new RandomSource(seed));
            scheduler.Advance(world, ticks: 24);

            return StateHasher.Compute(world);
        }

        Assert.Equal(RunWithSeed(1), RunWithSeed(999999));
    }

    /// <summary>
    /// 【核心】テスト表 #27。必需1品目で (1) 現金上限が実効価格を下回ってゲートで0 →
    /// UnaffordableNecessityCount == 1、(2) ゲートは開くがFundsCapが0に切り詰める → 同じく1。
    /// 同じlineで2にならない。
    /// </summary>
    [Fact]
    public void NecessityShortfallIsCountedOnBothPaths()
    {
        // 経路(1): 現金上限のゲートで0。
        {
            var definition = BuildShoppingDefinition(
                necessityTargetStockDays: TargetStockDaysFor(Item.Bread));
            var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);
            AddHousehold(world, id: 0, districtId: 4, Occupation.Woodworker, liquidFunds: 0);
            AddHousehold(world, id: 1, districtId: 4, Occupation.Miller);
            world.Households[1].WorkshopInventory[Item.Bread] = 100;

            EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1);

            Assert.Equal(1, world.Households[0].UnaffordableNecessityCount);
        }

        // 経路(2): 資金上限の切り詰めで0(2品目が同じ流動資金を奪い合う)。
        {
            var definition = BuildShoppingDefinition(
                necessityTargetStockDays: TargetStockDaysFor(Item.Bread, Item.Grain));
            var world = new World(npcCount: 3, householdCount: 3, itemCount: Item.Count);
            AddHousehold(world, id: 0, districtId: 4, Occupation.Woodworker, liquidFunds: 10);
            AddHousehold(world, id: 1, districtId: 4, Occupation.Miller); // Breadの売り手
            AddHousehold(world, id: 2, districtId: 4, Occupation.Baker); // Grainの売り手
            world.Households[1].WorkshopInventory[Item.Bread] = 100;
            world.Households[2].WorkshopInventory[Item.Grain] = 100;

            EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1);

            // 品目Id昇順(穀物0 → パン6)で穀物を先に買い切り、流動資金が尽きてパンがFundsCapで
            // 切り詰められる。同じlineで二重に数えられないので、結果は1のままである。
            Assert.Equal(1, world.Households[0].UnaffordableNecessityCount);
        }
    }

    /// <summary>
    /// テスト表 #28。必需で相場項が実効価格を下回ってゲートが閉じた日 →
    /// UnaffordableNecessityCount == 0。売り手の在庫が0で約定できなかった日・店を1つも
    /// 知らない日も0。
    /// </summary>
    [Fact]
    public void TooExpensiveIsNotCountedAsShortfall()
    {
        const int PhantomSellerId = 999;

        // ケース1: 相場項が実効価格を下回る(高すぎて買わなかった)。
        {
            var definition = BuildShoppingDefinition(
                breadFloor: 10, necessityTargetStockDays: TargetStockDaysFor(Item.Bread));
            var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);
            AddHousehold(world, id: 0, districtId: 4, Occupation.Woodworker, liquidFunds: 1000);
            AddHousehold(world, id: 1, districtId: 4, Occupation.Miller);
            world.Households[1].WorkshopInventory[Item.Bread] = 100;

            var system = new TradeSystem(definition);
            EconomySystemTestFixtures.RunDays(world, system, days: 1); // 1日目: 観測なし

            // 安い観測(価格1)を仕込む。1日目の日付(Tick.Zero)で、2日目に前日として有効になる。
            world.Knowledge[0].Add(new PriceObservation
            {
                ItemId = Item.Bread,
                LocationId = 0,
                Price = 1,
                SellerId = PhantomSellerId,
                ObservedAt = Tick.Zero,
                Source = ObservationSource.Direct,
            });

            EconomySystemTestFixtures.RunDays(world, system, days: 1); // 2日目

            // 相場項(許容乖離1200‰を掛けても最大3程度)が実効価格(床10、売り手には他の売り手の
            // 観測が無いので相場基準が立たず常に床)を下回る。資金は潤沢(1000)なので資金不足ではない。
            Assert.Equal(0, world.Households[0].UnaffordableNecessityCount);
        }

        // ケース2: 売り手の在庫が尽きている(売り注文そのものが無い)。
        {
            var definition = BuildShoppingDefinition(
                necessityTargetStockDays: TargetStockDaysFor(Item.Bread));
            var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);
            AddHousehold(world, id: 0, districtId: 4, Occupation.Woodworker, liquidFunds: 1000);
            AddHousehold(world, id: 1, districtId: 4, Occupation.Miller);
            world.Households[1].WorkshopInventory[Item.Bread] = 0;

            EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1);

            Assert.Equal(0, world.Households[0].UnaffordableNecessityCount);
        }

        // ケース3: 店を1つも知らない(売り手が存在しない)。
        {
            var definition = BuildShoppingDefinition(
                necessityTargetStockDays: TargetStockDaysFor(Item.Bread));
            var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);
            AddHousehold(world, id: 0, districtId: 4, Occupation.Woodworker, liquidFunds: 1000);

            EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1);

            Assert.Equal(0, world.Households[0].UnaffordableNecessityCount);
        }
    }

    /// <summary>
    /// 【核心】テスト表 #29([#81](https://github.com/stama72/visionary/issues/81))。流動資金が
    /// 「必需1日分」と「嗜好1日分」の片方しか払えない帯に置いた世帯で、必需が約定し
    /// UnaffordableNecessityCount == 0、嗜好の約定が0個であること。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-20)。</b>テスト側で <c>demand.Lines</c> を仮に <c>Reverse()</c> して
    /// 段5相当を走らせる形(<c>TradeSystem.Step</c> のLines走査を品目Id順・用途を無視した順に
    /// 変える製品コードの変異に相当する検証)を当てたところ、嗜好(穀物)が先に約定して流動資金15が
    /// 5へ減り、必需(パン)がFundsCapで0に切り詰められて <c>UnaffordableNecessityCount == 1</c>
    /// になった(期待0、赤を確認)。この帯(流動資金15、パン・穀物とも単価10)は必需と嗜好の
    /// どちらが先に決済されるかで結果が変わる判別力を持つ。変異を戻して(製品コードの走査順を
    /// 必需→耐久→入力→嗜好のまま)緑に復帰させた。
    /// </remarks>
    [Fact]
    public void NecessityIsSettledBeforePreference()
    {
        var definition = BuildShoppingDefinition(
            breadFloor: 10,
            grainFloor: 10,
            necessityTargetStockDays: TargetStockDaysFor(Item.Bread),
            preferenceTargetStockDays: TargetStockDaysFor(Item.Grain));

        var world = new World(npcCount: 3, householdCount: 3, itemCount: Item.Count);
        // 必需1日分(10)は払えるが、必需+嗜好1日分(20)は払えない帯。
        AddHousehold(world, id: 0, districtId: 4, Occupation.Woodworker, liquidFunds: 15);
        AddHousehold(world, id: 1, districtId: 4, Occupation.Miller); // パン(必需)の売り手
        AddHousehold(world, id: 2, districtId: 4, Occupation.Baker); // 穀物(嗜好)の売り手
        world.Households[1].WorkshopInventory[Item.Bread] = 100;
        world.Households[2].WorkshopInventory[Item.Grain] = 100;

        EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1);

        var buyer = world.Households[0];

        Assert.Equal(0, buyer.UnaffordableNecessityCount);
        Assert.Equal(1, buyer.HouseholdInventory[Item.Bread]); // 必需は約定する
        Assert.Equal(0, buyer.HouseholdInventory[Item.Grain]); // 嗜好はFundsCapで0個
        Assert.Equal(5, buyer.LiquidFunds); // 15 − 10(パンの代金)
    }

    /// <summary>
    /// 【核心】テスト表 #30。移動費が乗る区画の店で、UnitRealCost &gt; 予算 ≥ UnitEffectivePrice に
    /// なる配置 → 約定する(数量は実効価格で解いた値)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-20)。</b><c>TradeSystem</c> の段5手順3で
    /// <c>BuyerBudget.Decide(line, store.UnitRealCost)</c>(実質コストを渡す変異)に変えたところ、
    /// 実効価格10・実質コスト12・現金上限10の配置で <c>Assert.Equal(1,
    /// buyer.HouseholdInventory[Item.Bread])</c> が実際値0(12&gt;10でCashCapゲートが閉じ、
    /// 約定しない)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void BudgetGateUsesEffectivePriceNotRealCost()
    {
        var definition = BuildShoppingDefinition(
            breadFloor: 10, necessityTargetStockDays: TargetStockDaysFor(Item.Bread));

        var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);
        AddHousehold(world, id: 0, districtId: 4, Occupation.Woodworker, liquidFunds: 10);
        // 距離1(移動費が乗る)。実効価格10・現金上限10(予算=10)・実質コスト = 10 + 移動費。
        AddHousehold(world, id: 1, districtId: 1, Occupation.Miller);
        world.Households[1].WorkshopInventory[Item.Bread] = 100;

        EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1);

        var buyer = world.Households[0];

        // 実効価格(10)で解いた数量が約定し、実質コスト(10+移動費)は予算を超えているが
        // ゲートには使われない。
        Assert.Equal(1, buyer.HouseholdInventory[Item.Bread]);
        Assert.Equal(0, buyer.LiquidFunds); // 10 − 1×10(実効価格で決済)
    }

    /// <summary>
    /// 【核心】別表B-2(c)。段1が段4へ渡す「前日の出力提示価格」が実際に効いていること。
    /// 2日目、生産の入力(穀物)の利潤上限が実効価格を下回るゲートで、入力の約定が0個になる。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-21)。</b><c>TradeSystem.Step</c> が段4(<c>BuyerDemand.Build</c>)へ
    /// 渡す <c>hasOwnPreviousOffer[household.Id], ownPreviousOfferPrice[household.Id]</c> を
    /// 常に <c>false, 0</c> に置換する変異(タスク仕様 別表B-2(c) が名指し)を当てたところ、
    /// <c>Assert.DoesNotContain(...Direction == Purchase &amp;&amp; ItemId == Item.Grain...)</c> が
    /// 実際に1個の穀物購入(帳簿にPurchaseの行が現れる)で失敗した(赤を確認 ──
    /// 利潤上限が常に無い扱いになり、相場では儲からない値でも入力を買い続ける経路)。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ProfitCapGateUsesYesterdaysOutputOfferPrice()
    {
        const int BreadFloor = 30;
        const int GrainFloor = 100;

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

        // household0: パン屋(穀物→パン)。1日目は資金0で何も買わず、パンの提示価格(床30)だけを
        // Marketへ残す。2日目の直前に資金を積み、利潤上限だけがゲートを判定する帯を作る。
        AddHousehold(world, id: 0, districtId: 4, Occupation.Miller, liquidFunds: 0);
        world.Households[0].WorkshopInventory[Item.Bread] = 1;

        // household1: 穀物の売り手(UnusedRecipeがBaker〜Smithで品目0=穀物を生産する)。
        AddHousehold(world, id: 1, districtId: 4, Occupation.Baker, liquidFunds: 0);
        world.Households[1].WorkshopInventory[Item.Grain] = 100;

        var system = new TradeSystem(definition);

        EconomySystemTestFixtures.RunDays(world, system, days: 1); // 1日目: 資金0で何も買わない

        Assert.Equal(0, world.Households[0].WorkshopInventory[Item.Grain]);
        Assert.Equal(30, world.Market[new MarketKey(Item.Bread, 0)]); // 前日の出力提示価格(段4が読む)

        // 2日目の直前に資金を積む。見込み収益 = 前日価格30×出力数量2 = 60、
        // 許容原価合計 = FloorDiv(60×1000,1000) − 摩耗費0 = 60、単一入力なので利潤上限 = 60。
        // 穀物の実効価格は床100のまま(他の穀物売り手が居ないため相場基準が立たない)。
        // 60 < 100 なので利潤上限がゲートを閉じる。
        world.Households[0].LiquidFunds = 10_000;

        EconomySystemTestFixtures.RunDays(world, system, days: 1); // 2日目

        Assert.DoesNotContain(
            world.Ledgers[0],
            entry => entry.Direction == LedgerDirection.Purchase && entry.ItemId == Item.Grain);
        Assert.Equal(0, world.Households[0].WorkshopInventory[Item.Grain]);
    }

    /// <summary>
    /// 【核心】レビュー指摘C-3。販売在庫が0の日(出品しない日)でも、前日の出力提示価格は
    /// 控え続け、生産の入力の利潤上限が引き続き立つこと(タスク仕様§9段1「hasOwnPreviousOffer
    /// は販売在庫0の世帯についても控える。その世帯も買い手として利潤上限を持つ」)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-21)。</b><c>TradeSystem.Step</c> の
    /// <c>hasOwnPreviousOffer[household.Id] = hasOwnOffer;</c> を
    /// <c>hasOwnOffer &amp;&amp; sellableStock &gt; 0</c> に置換する変異(タスク仕様C-3が名指し。
    /// 控えを <c>if (sellableStock &lt;= 0) continue;</c> の後ろへ移したのと同じ意味)を当てたところ、
    /// <c>Assert.DoesNotContain(...Direction == Purchase &amp;&amp; ItemId == Item.Grain...)</c> が
    /// 実際に1個の穀物購入(帳簿にPurchaseの行が現れる)で失敗した(赤を確認 ──
    /// 販売在庫0の日に <c>hasOwnPreviousOffer</c> がfalseへ落ち、利潤上限が無い扱いになって
    /// 相場では儲からない値でも入力を買い続ける経路。「まさに仕入れたい日の工房」で保証が外れる)。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ProfitCapSurvivesWhenSellableStockIsZeroOnTheFollowingDay()
    {
        const int BreadFloor = 30;
        const int GrainFloor = 100;

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

        // household0: パン屋(穀物→パン)。1日目は資金0で何も買わず、パンの提示価格(床30)だけを
        // Marketへ残す。
        AddHousehold(world, id: 0, districtId: 4, Occupation.Miller, liquidFunds: 0);
        world.Households[0].WorkshopInventory[Item.Bread] = 1;

        // household1: 穀物の売り手。
        AddHousehold(world, id: 1, districtId: 4, Occupation.Baker, liquidFunds: 0);
        world.Households[1].WorkshopInventory[Item.Grain] = 100;

        var system = new TradeSystem(definition);

        EconomySystemTestFixtures.RunDays(world, system, days: 1); // 1日目: 資金0で何も買わない

        Assert.Equal(30, world.Market[new MarketKey(Item.Bread, 0)]); // 前日の出力提示価格

        // 2日目の直前: 資金を積み、かつ「売り切った/入力切れで作れなかった」状態(販売在庫0)を
        // 作る。まさに仕入れたい日の工房である。
        world.Households[0].LiquidFunds = 10_000;
        world.Households[0].WorkshopInventory[Item.Bread] = 0;

        EconomySystemTestFixtures.RunDays(world, system, days: 1); // 2日目

        // 販売在庫0なので2日目は新しい売り注文を出さない(旧エントリも消える)。
        Assert.False(world.Market.ContainsKey(new MarketKey(Item.Bread, 0)));

        // それでも前日の出力提示価格(30)は控え続け、利潤上限(60)が実効価格(床100)を
        // 下回るゲートを閉じたままにする。
        Assert.DoesNotContain(
            world.Ledgers[0],
            entry => entry.Direction == LedgerDirection.Purchase && entry.ItemId == Item.Grain);
        Assert.Equal(0, world.Households[0].WorkshopInventory[Item.Grain]);
    }
}
