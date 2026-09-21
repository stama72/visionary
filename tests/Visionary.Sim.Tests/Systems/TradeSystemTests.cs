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
        int minimumMarginPermille = 0,
        int shipmentDays = 1,
        bool isExportEnabled = true)
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
            shipmentDays: shipmentDays,
            inputBufferDays: 1,
            externalBuyPriceOverride: externalBuyPrice,
            isExportEnabled: isExportEnabled);
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
        // 区画0(中心ではない)。#149で窓口が都市生産品も観測するようになったため、中心区画
        // (District.ExternalMarketDistrictId)に置くと窓口自身の観測がMarketReference.TrySellerの
        // 「他の売り手」に混ざり、本テストが検証したい「他の売り手の観測だけ」の構成が崩れる。
        AddHousehold(world, id: 0, districtId: 0, Occupation.Miller);
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

        // 区画0(中心ではない)。#149で窓口の観測が都市生産品にも及ぶため、中心区画に置くと
        // 窓口自身の観測がMarketReference.TrySellerの「他の売り手」に混ざる(SellerAnchorsOn…と
        // 同じ理由)。

        // household0: 売り手(パン)。1日目に床(10)で1個売れるだけの在庫を持つ。
        AddHousehold(world, id: 0, districtId: 0, Occupation.Miller, liquidFunds: 0);
        world.Households[0].WorkshopInventory[Item.Bread] = 2;

        // household1: 買い手。1日目に床(10)で1単位だけ買える資金を持つ。
        AddHousehold(world, id: 1, districtId: 0, Occupation.Woodworker, liquidFunds: 10);

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
    /// <remarks>
    /// <b>#38 追随(2026-09-20)。</b><c>isExportEnabled: false</c> で構成し直す。輸出が入ると、
    /// 破産中は閾在庫0(<see cref="ExternalMarket.ExportThresholdStock"/>)になり、1日目の夕方に
    /// 販売在庫を全量窓口へ持ち込んでしまい、2日目の朝は売り注文が立たず
    /// <c>world.Market[key]</c> が <c>KeyNotFoundException</c> になる(実測)。半値の枝そのものは
    /// GDD02c §1.4 の規則であって輸出とは別である。輸出が入ると投げ売りが床へ吸収される論点は
    /// GDD02c §1.4 が GDD02b へ送っている(<a href="https://github.com/stama72/visionary/issues/30">#30</a>)。
    /// </remarks>
    [Fact]
    public void BankruptSellerPostsTheHalvedFloorInThePipeline()
    {
        const int OtherSellerId = 999;

        var definition = BuildShoppingDefinition(breadFloor: 10, isExportEnabled: false);
        var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);
        // 区画0(中心ではない)。#149で窓口の観測が都市生産品にも及ぶため(SellerAnchorsOn…と
        // 同じ理由。isExportEnabled: falseは輸出だけを止め、窓口の観測・提示は止めない)。
        AddHousehold(world, id: 0, districtId: 0, Occupation.Miller);
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
    /// 【核心】タスク仕様テスト表 #5。段1 が求めた <c>hasSettled</c> がそのまま
    /// <c>OfferPrice.Calculate</c> の第6引数まで届くこと(TDD01 §3.2)。パン屋(世帯0)に在庫1
    /// (出荷目標在庫2 → 在庫比500‰ → 係数1250‰)を持たせ、買い手を置かずに1日目・2日目を回す。
    /// 2日目は頭打ちで200(1250‰なら250)。<b>対照</b>: 2日目を回す前に1日目の帳簿へ自分の
    /// 約定(Sale)を直接置くと hasSettled=true になり、頭打ちが外れて132になる。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-20、<c>mutator</c> による測定、対象コミット <c>87d4837</c>)。</b>
    /// §1.1 の頭打ちを削除する変異(M-1)、<c>TradeSystem</c> 段1が <c>OfferPrice.Calculate</c> の
    /// 第6引数(<c>hasSettledYesterday</c>)を <c>true</c> 定数に固定する変異(M-2)の両方で、
    /// 本体の <c>Assert.Equal(200, world.Market[key])</c> が期待200/実際250で失敗した(赤を確認)。
    /// <b>この2つを分けているのは <see cref="OfferPriceTests.UnsoldSellerDoesNotRaiseAboveTheReference"/>
    /// が M-2 で緑のままであること</b> ── 段1が求めた <c>hasSettled</c> が
    /// <c>OfferPrice.Calculate</c> まで届く配線の経路を踏んでいるのは本テストだけであり、単体テストは
    /// 配線を経由せず <c>OfferPrice.Calculate</c> を直接呼ぶため、配線が切れる変異を検出しない。
    /// </remarks>
    [Fact]
    public void UnsoldSellerIsCappedAtTheReferenceInThePipeline()
    {
        const int OtherSellerId = 999;

        // shipmentDays=2で出荷目標在庫2(生産能力1×出力数量1×出荷日数2)にする ──
        // 在庫1/目標2 → 在庫比500‰ → 係数1250‰(1000‰の頭打ちと区別できる値)。
        var definition = BuildShoppingDefinition(breadFloor: 10, shipmentDays: 2);

        void SeedOtherSellersObservation(World world)
        {
            world.Knowledge[0].Add(new PriceObservation
            {
                ItemId = Item.Bread,
                LocationId = 0,
                Price = 200,
                SellerId = OtherSellerId,
                ObservedAt = Tick.Zero,
                Source = ObservationSource.Direct,
            });
        }

        // 本体: 1日目は誰も買わないので売れ残る(hasSettled=false)。
        {
            var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);
            // 区画0(中心ではない)。#149で窓口の観測が都市生産品にも及ぶため(SellerAnchorsOn…と
            // 同じ理由)。
            AddHousehold(world, id: 0, districtId: 0, Occupation.Miller);
            world.Households[0].WorkshopInventory[Item.Bread] = 1;

            var system = new TradeSystem(definition);
            var key = new MarketKey(Item.Bread, 0);

            EconomySystemTestFixtures.RunDays(world, system, days: 1); // 1日目: 床のまま
            Assert.Equal(10, world.Market[key]);

            SeedOtherSellersObservation(world);
            EconomySystemTestFixtures.RunDays(world, system, days: 1); // 2日目

            // hasSettled=falseなので係数はmin(1250,1000)=1000: ApplyPermille(200,1000)=200。
            Assert.Equal(200, world.Market[key]);
        }

        // 対照: 2日目を回す前に自分の約定(Sale)を帳簿へ直接置く。
        {
            var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);
            // 区画0(中心ではない)。#149で窓口の観測が都市生産品にも及ぶため(SellerAnchorsOn…と
            // 同じ理由)。
            AddHousehold(world, id: 0, districtId: 0, Occupation.Miller);
            world.Households[0].WorkshopInventory[Item.Bread] = 1;

            var system = new TradeSystem(definition);
            var key = new MarketKey(Item.Bread, 0);

            EconomySystemTestFixtures.RunDays(world, system, days: 1); // 1日目
            Assert.Equal(10, world.Market[key]);

            SeedOtherSellersObservation(world);
            world.Ledgers[0].Add(new LedgerEntry
            {
                CounterpartyId = 1,
                ItemId = Item.Bread,
                Quantity = 1,
                UnitPrice = 10,
                OccurredAt = Tick.Zero,
                Terms = LedgerTerms.Cash,
                Direction = LedgerDirection.Sale,
            });

            EconomySystemTestFixtures.RunDays(world, system, days: 1); // 2日目

            // hasSettled=trueなので頭打ちが外れる。相場基準 = CeilDiv(200+10,2) = 105、
            // 係数1250‰: ApplyPermille(105,1250) = CeilDiv(131250,1000) = 132。
            Assert.Equal(132, world.Market[key]);
        }
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
            // 区画0(中心ではない)。#149で窓口の観測が都市生産品にも及ぶため(SellerAnchorsOn…と
            // 同じ理由)。
            AddHousehold(world, id: 0, districtId: 0, Occupation.Miller);
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
        // 区画0(中心ではない)。#149で窓口の観測が都市生産品にも及ぶため(SellerAnchorsOn…と
        // 同じ理由)。
        world.Households[0] = new HouseholdState(
            id: 0, districtId: 0, headNpcId: HeadNpcId, memberNpcIds: new[] { HeadNpcId }, itemCount: Item.Count);
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

    /// <summary>
    /// 【核心】テスト表 #8(#38)。中心区画の世帯が1次産品(木材)を窓口から買う
    /// (段5bをパイプラインで踏む)。
    /// </summary>
    /// <remarks>
    /// <c>world.Households[store.SellerId]</c> を無条件に引くと <c>IndexOutOfRangeException</c>
    /// (<c>store.SellerId</c> が <c>int.MaxValue</c> のとき)。在庫で切り詰めると窓口は無限在庫
    /// なのに0個になる(タスク仕様)。
    /// </remarks>
    [Fact]
    public void ImportDoesNotIndexTheHouseholdArray()
    {
        // MillerBreadRecipe(Bread←Timber)+UnusedRecipe(Grain←Timber)。Woodworkerの入力(木材)は
        // 誰も出力しない1次産品(既定でBuildDefinitionが基準値1を与える)。
        var definition = BuildShoppingDefinition();
        var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);
        AddHousehold(
            world, id: 0, districtId: District.ExternalMarketDistrictId, Occupation.Woodworker,
            liquidFunds: 100_000);

        var exception = Record.Exception(
            () => EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1));

        Assert.Null(exception);

        Assert.True(world.Households[0].WorkshopInventory[Item.Timber] > 0);
        Assert.Contains(
            world.Ledgers[0],
            entry => entry.Direction == LedgerDirection.Purchase
                && entry.ItemId == Item.Timber
                && entry.CounterpartyId == HouseholdState.ExternalMarketSellerId);
    }

    /// <summary>
    /// #38タスク仕様のテスト表 #14〜16 が使う世界。世帯Id0(E、Occupation.Miller)は自分の出力
    /// (パン)を窓口へ輸出する候補であり、同時に必需(穀物)を世帯Id1(Occupation.Baker、
    /// UnusedRecipeの出力)から買う。距離と <paramref name="definition"/> の
    /// <c>TravelHoursPerDistrict</c> の組み合わせで、段5aの買い物往復時間 + 段6の輸出往復時間の
    /// 合計がTを超えるかどうかを作り分ける(<see cref="BuildExportAndShoppingDefinition"/>)。
    /// </summary>
    private static World BuildExportAndShoppingWorld(
        WorldDefinition definition, int exporterDistrictId, int sellerDistrictId)
    {
        var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);

        AddHousehold(world, id: 0, districtId: exporterDistrictId, Occupation.Miller, liquidFunds: 1_000_000);
        world.Households[0].WorkshopInventory[Item.Bread] = 100; // 輸出の対象(自分の出力)。
        world.Households[0].WorkshopInventory[Item.Timber] = 100_000; // 自分の生産入力を中立化する。
        world.Households[0].WorkshopInventory[Item.Tools] = 5; // 耐久の需要を中立化する。

        AddHousehold(world, id: 1, districtId: sellerDistrictId, Occupation.Baker, liquidFunds: 0);
        world.Households[1].WorkshopInventory[Item.Grain] = 100; // 世帯0が買う穀物。
        world.Households[1].WorkshopInventory[Item.Timber] = 100_000;
        world.Households[1].WorkshopInventory[Item.Tools] = 5;

        return world;
    }

    private static WorldDefinition BuildExportAndShoppingDefinition(int travelHoursPerDistrict)
    {
        var necessityTargetStockDays = new int[Item.Count];
        necessityTargetStockDays[Item.Grain] = 10;

        return EconomySystemTestFixtures.BuildDefinition(
            new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = 1 } },
                inputs: new[] { new ItemQuantity { ItemId = Item.Timber, Quantity = 1 } },
                laborPermille: 1000),
            dailyConsumptionPerNpcByRank: GrainConsumptionTable(),
            necessityTargetStockDays: necessityTargetStockDays,
            tolerancePermille: 1200,
            opportunityCostBaseByOccupation: new[] { 1, 1, 1, 1, 1 },
            rankCoefficientPermille: new[] { 1000, 1000, 1000 },
            travelHoursPerDistrict: travelHoursPerDistrict,
            shipmentDays: 1,
            inputBufferDays: 1,
            disposableHours: 12);
    }

    /// <summary>
    /// 【核心】テスト表 #14(#38)。GDD02d §2.3 の具体例。段5aの往復時間の合計 + 段6の輸出の
    /// 往復時間がTを超える日は持ち込まない。超えない(Tちょうどを含む)日は持ち込む。
    /// </summary>
    /// <remarks>
    /// M-7非対象。Tの検査が無い(1日12時間以上歩く)・比較を<c>&gt;=</c>にする(12時間ちょうどで
    /// 持ち込まなくなる)、いずれの実装ミスでもケースA・ケースBの少なくとも一方が崩れる
    /// (タスク仕様)。
    /// </remarks>
    [Fact]
    public void ExportErrandIsSkippedWhenTheDayIsFull()
    {
        // ケースA: 段5a(買い物、往復8時間)+段6(輸出、往復8時間)=16>T(12) → 持ち込まない。
        {
            var definition = BuildExportAndShoppingDefinition(travelHoursPerDistrict: 2);
            var world = BuildExportAndShoppingWorld(definition, exporterDistrictId: 0, sellerDistrictId: 2);

            EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1);

            // 前提: 買い物は実際に成立している(穀物を買えた)。
            Assert.True(world.Households[0].HouseholdInventory[Item.Grain] > 0);

            // 輸出は起きない(在庫は減らず、外部Saleの帳簿も無い)。
            Assert.Equal(100, world.Households[0].WorkshopInventory[Item.Bread]);
            Assert.DoesNotContain(
                world.Ledgers[0],
                entry => entry.Direction == LedgerDirection.Sale
                    && entry.CounterpartyId == HouseholdState.ExternalMarketSellerId);
        }

        // ケースB: 段5a(買い物、往復8時間)+段6(輸出、往復4時間)=12、Tちょうど(>12ではない)
        // → 持ち込む。
        {
            var definition = BuildExportAndShoppingDefinition(travelHoursPerDistrict: 1);
            var world = BuildExportAndShoppingWorld(definition, exporterDistrictId: 0, sellerDistrictId: 8);

            EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1);

            Assert.True(world.Households[0].HouseholdInventory[Item.Grain] > 0);

            Assert.True(world.Households[0].WorkshopInventory[Item.Bread] < 100);
            Assert.Contains(
                world.Ledgers[0],
                entry => entry.Direction == LedgerDirection.Sale
                    && entry.CounterpartyId == HouseholdState.ExternalMarketSellerId);
        }
    }

    /// <summary>
    /// テスト表 #15(#38)。中心へ買い物には行かずに輸出した世帯が、その日に窓口の観測を得る
    /// (GDD02d §2.3 追記「持ち込んだ日は中心に居たことになる」)。
    /// </summary>
    [Fact]
    public void ExportTripAddsTheCentreToTheObservedDistricts()
    {
        // ケースBと同じ配置(買い物は区画8、輸出は往復4時間でTに収まる)。
        var definition = BuildExportAndShoppingDefinition(travelHoursPerDistrict: 1);
        var world = BuildExportAndShoppingWorld(definition, exporterDistrictId: 0, sellerDistrictId: 8);

        EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1);

        // 前提: 輸出が実際に成立している。
        Assert.True(world.Households[0].WorkshopInventory[Item.Bread] < 100);

        // 中心へは買い物に行っていない(穀物の売り手は区画8)のに、窓口の観測が生まれる
        // (段6が訪問区画へ中心を足したことの証拠)。
        Assert.Contains(
            world.Knowledge[world.Households[0].HeadNpcId],
            o => o.SellerId == HouseholdState.ExternalMarketSellerId);
    }

    /// <summary>
    /// テスト表 #16(#38)。既に外出している世帯の労働損失に足される(上書きされない)。
    /// </summary>
    [Fact]
    public void ExportAddsToTheErrandLaborLoss()
    {
        // ケースBと同じ配置。
        var definition = BuildExportAndShoppingDefinition(travelHoursPerDistrict: 1);
        var world = BuildExportAndShoppingWorld(definition, exporterDistrictId: 0, sellerDistrictId: 8);

        // 配置の変更(2026-09-21、#149)。この定義の必需(穀物、Item.Grain)は
        // <see cref="EconomySystemTestFixtures.UnusedRecipe"/>(Baker用の埋めレシピ)がitemId0を
        // 出力に持つため、「どのレシピも出力しない」1次産品の定義から外れ、都市生産品として
        // 扱われる(#149前は窓口が都市生産品を並べなかったため無害だった構成上の偶然)。
        // #149で窓口が都市生産品も並べるようになると、買い手(区画0)から見て窓口(距離2)は
        // district8(距離4)より近く、記憶が無ければ3段目(未知価格の床=1)がdistrict8の
        // 見積もりとタイになるため、より近い窓口が1周目で選ばれてしまい、本テストが見たい
        // 「買い物(district8、往復8時間)と輸出(窓口、往復4時間)が別区画で加算される」という
        // 構成そのものが崩れる(判別力が窓口に吸収される)。<b>買い手に窓口の穀物価格の記憶
        // (高値999)を持たせ、窓口を穀物の候補としては実質的に外した</b>(district8の床=1は
        // 動かしていない)。
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

        EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1);

        // 前提: 輸出が実際に成立している。
        Assert.True(world.Households[0].WorkshopInventory[Item.Bread] < 100);

        int shoppingTravelHours = Errand.TravelHours(0, 8, hoursPerDistrict: 1); // 8。
        int exportTravelHours = Errand.TravelHours(0, District.ExternalMarketDistrictId, hoursPerDistrict: 1); // 4。
        const int LaborPermille = 1000; // Masterの労働力係数‰(既定)。

        int expectedLoss = Errand.LaborLossPermille(LaborPermille, shoppingTravelHours, disposableHours: 12)
            + Errand.LaborLossPermille(LaborPermille, exportTravelHours, disposableHours: 12);

        // 段5aが書いた損失に段6が加算していること(`=`で上書きしていないこと)。
        Assert.Equal(expectedLoss, world.Households[0].ErrandLaborLossPermille);
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
    /// 【核心】別表D-1([#98](https://github.com/stama72/visionary/issues/98) タスク仕様)。
    /// 手順9(<c>fundsCap == 0</c>)は用途が <see cref="DemandPurpose.Necessity"/> の行だけを
    /// 数える。必需の行が資金をほぼ使い切ったうえで、嗜好の行が段4の古い現金上限(値下げ前の
    /// 流動資金で計算済み)のゲートを通ってから、段8で現在の(必需の決済で減った)流動資金に
    /// 対する <c>fundsCap == 0</c> を踏む世帯を手で組む。<b>必需の行自体は <c>fundsCap</c> が
    /// 1 のままで、この経路を踏まない。</b> W2-08 のテスト #27(<see cref="NecessityShortfallIsCountedOnBothPaths"/>)は
    /// 必需の2経路、#28(<see cref="TooExpensiveIsNotCountedAsShortfall"/>)は理由コードを
    /// 押さえているが、「非必需が経路(2)で数えられないこと」はどちらも押さえていない。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-20)。</b><c>TradeSystem.RunOneHouseholdsShopping</c> の手順9
    /// (<c>if (line.Purpose == DemandPurpose.Necessity &amp;&amp; fundsCap == 0)</c>)から
    /// <c>line.Purpose == DemandPurpose.Necessity &amp;&amp;</c> を外す変異(タスク仕様 別表D-1が
    /// 名指し)を当てたところ、<c>Assert.Equal(0, world.Households[0].UnaffordableNecessityCount)</c>
    /// が実際値1(嗜好(穀物)の行が段4の古い現金上限100のゲートを通った後、段8で現在の流動資金0に
    /// 対する <c>fundsCap == 0</c> を踏んで数えられる)で失敗した(赤を確認)。変異を戻して
    /// 緑に復帰させた。
    /// </remarks>
    [Fact]
    public void NonNecessityFundsShortfallIsNotCounted()
    {
        var definition = BuildShoppingDefinition(
            breadFloor: 100,
            grainFloor: 50,
            necessityTargetStockDays: TargetStockDaysFor(Item.Bread),
            preferenceTargetStockDays: TargetStockDaysFor(Item.Grain));

        var world = new World(npcCount: 3, householdCount: 3, itemCount: Item.Count);
        // 必需(パン)の代金ちょうどの流動資金 ── 段4のCashCap(=100)と段8のfundsCap(=1)が
        // ともに約定を通し、資金をほぼ使い切る(fundsCap==0は踏まない)。
        AddHousehold(world, id: 0, districtId: 4, Occupation.Woodworker, liquidFunds: 100);
        AddHousehold(world, id: 1, districtId: 4, Occupation.Miller); // パン(必需)の売り手。床100。
        AddHousehold(world, id: 2, districtId: 4, Occupation.Baker); // 穀物(嗜好)の売り手。床50。
        world.Households[1].WorkshopInventory[Item.Bread] = 100;
        world.Households[2].WorkshopInventory[Item.Grain] = 100;

        EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1);

        var buyer = world.Households[0];

        // 必需(パン)は約定し(段4のCashCap=100、価格100で数量1)、流動資金をちょうど使い切る。
        Assert.Equal(1, buyer.HouseholdInventory[Item.Bread]);
        Assert.Equal(0, buyer.LiquidFunds);

        // 嗜好(穀物)は段4のゲート(古いCashCap=100、価格50)を通り数量2の解が立つが、
        // 段8で現在の流動資金0に対するfundsCap==0に切り詰められて0個になる。
        Assert.Equal(0, buyer.HouseholdInventory[Item.Grain]);

        // 経路(2)は用途がNecessityの行だけを数える。嗜好の資金不足はここに現れない。
        Assert.Equal(0, buyer.UnaffordableNecessityCount);
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
    /// <remarks>
    /// <b>#38 追随(2026-09-20)。</b>買い手(Woodworker)自身のレシピ(<c>UnusedRecipe</c>)が
    /// 1次産品(木材)を入力に持つため、窓口が候補に足されたことで生産の入力(木材)が新たに
    /// 約定するようになった(実測: <c>LiquidFunds</c> が期待5に対し実際3)。走査順
    /// (必需→耐久→<b>生産の入力</b>→嗜好)では生産の入力が必需の後・嗜好の前に決済されるため、
    /// <c>UnaffordableNecessityCount</c> と各品目の数量(パン1・穀物0)は変わらないが、
    /// <c>LiquidFunds</c> だけが窓口での木材の代金ぶん動く。<see cref="BuildShoppingDefinition"/> の
    /// 定数(流動資金15など)は動かさない。<b>判別力は変異M-5で測り直す</b>。
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
        // 15 − 10(パンの代金) − 窓口での木材(生産の入力)の代金(#38追随。上のremarks参照)。
        Assert.Equal(3, buyer.LiquidFunds);
    }

    /// <summary>
    /// 【核心】テスト表 #25。旧テスト「BudgetGateUsesEffectivePriceNotRealCost」(#30)の置き換え。
    /// 距離2の訪問区画の店で約定し、数量が距離0の店で同じ提示価格のときと一致する
    /// (外出の費用は数量の解にも予算にも混ざらない。GDD02b §7)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-20)。</b><c>TradeSystem.RunOneHouseholdsShopping</c> の
    /// <c>BuyerBudget.Decide(line, store.UnitEffectivePrice)</c> を
    /// <c>BuyerBudget.Decide(line, store.UnitEffectivePrice + Errand.Cost(travelHours,
    /// errand.CostPerHour))</c>(外出の費用を単価へ足して渡す変異、#85で消した二重計上が戻る形)に
    /// 変えたところ、距離2のケースの <c>Assert.Equal(2,
    /// distantBuyer.HouseholdInventory[Item.Bread])</c> が実際値1(単価が実質的に上がり
    /// 数量の解が減る)で失敗し、<c>homeQuantity</c>(距離0、常に2)と食い違った(赤を確認)。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    /// <remarks>
    /// <b>配置の変更(2026-09-21、#149)。</b>買い手(Woodworker)は自分のレシピ(<c>UnusedRecipe</c>)の
    /// 入力(木材)と耐久(工具)も毎日買う。#149前は窓口が都市生産品(パン)を並べなかったので
    /// この背景需要は無害だったが、#149後は2つの経路で本テストの比較が崩れる(判別力が窓口に
    /// 吸収される):
    /// <list type="bullet">
    /// <item>窓口(距離2、district2のパンと同じ費用)は「パン単独」のdistrict2よりパン+木材+
    /// 工具をまとめて買える分だけ総価値が高くなり、1周目に窓口がdistrict2に勝って、遠方の
    /// 買い手がdistrict2の床(10)ではなく窓口の実際の天井(20)でパンを買ってしまう。
    /// → <b>買い手に窓口のパンの値の記憶(高値999)を持たせ、窓口をパンの候補としては
    /// 実質的に外した。</b></item>
    /// <item>中心に住む買い手(home)は窓口が常に距離0(追加の外出費用が要らない)なので工具を
    /// 「無料で」買えるが、遠方の買い手(distant)は同じ工具を買うには単独で追加の外出をする
    /// 価値が無い(窓口の総価値からパンを除いた分だけでは費用を超えない)。この非対称性は
    /// パンの比較とは無関係な副作用である。
    /// → <b>両世帯の木材・工具の背景需要を中立化した</b>(<see cref="BuildExportAndShoppingWorld"/>
    /// と同じ配合: 木材100,000・工具5)。</item>
    /// </list>
    /// district2の床10・窓口の天井の導出式そのものは動かしていない。
    /// </remarks>
    [Fact]
    public void BudgetGateUsesTheEffectivePriceOnly()
    {
        var definition = BuildShoppingDefinition(
            breadFloor: 10, necessityTargetStockDays: TargetStockDaysFor(Item.Bread));

        // 距離0(自区画に売り手)。
        var homeWorld = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);
        AddHousehold(homeWorld, id: 0, districtId: 4, Occupation.Woodworker, liquidFunds: 1000);
        AddHousehold(homeWorld, id: 1, districtId: 4, Occupation.Miller);
        homeWorld.Households[1].WorkshopInventory[Item.Bread] = 100;
        // 買い手自身の入力(木材)・耐久(工具)の需要を中立化する(下記remarks参照。本テストの
        // 関心はパンだけである)。
        homeWorld.Households[0].WorkshopInventory[Item.Timber] = 100_000;
        homeWorld.Households[0].WorkshopInventory[Item.Tools] = 5;

        EconomySystemTestFixtures.RunDays(homeWorld, new TradeSystem(definition), days: 1);

        var homeBuyer = homeWorld.Households[0];

        // 距離2(訪問区画に売り手。他区画には売り手がいないので、余剰が費用を上回れば
        // その区画だけへ外出する)。
        var distantWorld = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);
        AddHousehold(distantWorld, id: 0, districtId: 0, Occupation.Woodworker, liquidFunds: 1000);
        AddHousehold(distantWorld, id: 1, districtId: 2, Occupation.Miller); // District.Distance(0,2)=2
        distantWorld.Households[1].WorkshopInventory[Item.Bread] = 100;
        distantWorld.Households[0].WorkshopInventory[Item.Timber] = 100_000;
        distantWorld.Households[0].WorkshopInventory[Item.Tools] = 5;

        EconomySystemTestFixtures.AdvanceClockOnly(distantWorld, ticks: 24);
        distantWorld.Knowledge[0].Add(new PriceObservation
        {
            ItemId = Item.Bread,
            LocationId = 0,
            Price = 999,
            SellerId = HouseholdState.ExternalMarketSellerId,
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        });

        EconomySystemTestFixtures.RunDays(distantWorld, new TradeSystem(definition), days: 1);

        var distantBuyer = distantWorld.Households[0];

        // 外出が実際に起きたこと(訪問区画にしか売り手がいない以上、外出しなければ約定しえない)。
        Assert.True(distantBuyer.HouseholdInventory[Item.Bread] > 0);

        // 数量が一致する(外出の費用は数量の解にも予算にも混ざらない。GDD02b §7)。
        Assert.Equal(homeBuyer.HouseholdInventory[Item.Bread], distantBuyer.HouseholdInventory[Item.Bread]);

        // 決済額も一致する(実効価格のみを使う。Errand.Costは価値の比較にだけ使われ、
        // LiquidFundsからは一切引かれない)。
        Assert.Equal(homeBuyer.LiquidFunds, distantBuyer.LiquidFunds);
    }

    /// <summary>
    /// 【核心】テスト表 #26。行ったが1つも買えなかった区画(全店の販売在庫0)でも、
    /// 翌日その区画の店が「有効な記憶」になる(観測は買い物の副産物ではなく、見た時点で
    /// 生まれる。GDD06 §3.1)。
    /// </summary>
    /// <remarks>
    /// <b>設計:</b> 売り手(Id0)の在庫はちょうど1個。同区画の depleter(Id1)が Household Id
    /// 昇順で先に処理され、外出なし(自区画)で買い尽くす。遠方の買い手(Id2)は depleter より
    /// 後に処理される。5a の計画は在庫を見ない(見積もり価格は床から作る。#17と同じ理由)ので、
    /// depleter に買い尽くされた後でも遠方の買い手はその区画へ外出する。5b では
    /// <c>WorkshopInventory &lt;= 0</c> で候補から外れ0個しか買えないが、段1で投稿された
    /// <c>world.Market</c> のエントリ自体は日中クリアされないので、段6の観測は生まれる。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(2026-09-20)。</b>段5を1つのループへ畳み、5bで実際に購入した
    /// (quantity ≥ 1 の)品目の区画だけを <c>visitedDistrictIds</c> へ積む変異(旧実装の
    /// 「訪問記録を約定時だけ積む」)を当てたところ、<c>Assert.Contains(
    /// world.Knowledge[distantBuyer.HeadNpcId], o =&gt; o.SellerId == SellerId)</c> が
    /// 実際値なし(Collection was empty。観測が買い物の副産物になり、0個しか買えなかった
    /// 区画の観測が生まれない経路)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    /// <remarks>
    /// <b>配置の変更(2026-09-21、#149)。</b>窓口が都市生産品(パン)も売るようになると、遠方の
    /// 買い手(Baker、区画0)は自分のUnusedRecipe(木材の入力)・耐久(工具)の背景需要ぶん窓口の
    /// 総価値がdistrict2(パン単独)より高くなり、district2そのものを一度も訪問しなくなる ──
    /// 本テストの前提(訪問はしたが1個も買えなかった)が成立しなくなる(判別力が窓口に吸収
    /// される)。<b>買い手の木材・工具の背景需要を中立化し、窓口のパンの値の記憶(高値999)を
    /// 持たせて窓口を実質的に外した</b>(<see cref="BuildExportAndShoppingWorld"/>と同じ配合・
    /// 手法)。断定そのものは元のまま(Miller在庫0のまま・観測は生まれる)である。
    /// </remarks>
    [Fact]
    public void ObservationsCoverEveryVisitedDistrictEvenWithoutAPurchase()
    {
        const int SellerId = 0;
        const int DepleterId = 1;
        const int DistantBuyerId = 2;
        const int SellerDistrictId = 2; // District.Distance(0,2)=2(Rの外)。

        var definition = BuildShoppingDefinition(
            breadFloor: 10, necessityTargetStockDays: TargetStockDaysFor(Item.Bread));

        var world = new World(npcCount: 3, householdCount: 3, itemCount: Item.Count);
        AddHousehold(world, id: SellerId, districtId: SellerDistrictId, Occupation.Miller, liquidFunds: 0);
        world.Households[SellerId].WorkshopInventory[Item.Bread] = 1; // ちょうど1個。depleterが買い尽くす。

        // depleterは売り手と同区画(自区画なので、外出なしで即座に買い尽くせる)。
        AddHousehold(
            world, id: DepleterId, districtId: SellerDistrictId, Occupation.Woodworker, liquidFunds: 100_000);

        // 遠方の買い手。Household Id昇順でdepleterより後に処理される。
        AddHousehold(world, id: DistantBuyerId, districtId: 0, Occupation.Baker, liquidFunds: 1000);
        // 配置の変更(2026-09-21、#149)。上記docコメントのとおり窓口が候補に入ると、遠方の
        // 買い手はBaker自身のUnusedRecipe(木材の入力)・耐久(工具)の背景需要ぶん窓口の総価値が
        // district2より高くなり、district2そのものを一度も訪問しなくなる ── 本テストの前提
        // (訪問はしたが1個も買えなかった)がそもそも成立しなくなる。買い手の木材・工具の背景
        // 需要を中立化し(<see cref="BuildExportAndShoppingWorld"/>と同じ配合)、窓口のパンの
        // 値の記憶(高値999)を持たせて、窓口をパンの候補としては実質的に外した。
        world.Households[DistantBuyerId].WorkshopInventory[Item.Timber] = 100_000;
        world.Households[DistantBuyerId].WorkshopInventory[Item.Tools] = 5;
        EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 24);
        world.Knowledge[DistantBuyerId].Add(new PriceObservation
        {
            ItemId = Item.Bread,
            LocationId = 0,
            Price = 999,
            SellerId = HouseholdState.ExternalMarketSellerId,
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        });

        EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1);

        var distantBuyer = world.Households[DistantBuyerId];

        // 買えなかったこと(depleterが先に買い尽くしている)。
        Assert.Equal(0, distantBuyer.HouseholdInventory[Item.Bread]);

        // それでも観測は生まれる(見た時点で生まれる。買い物の成否に依らない)。
        Assert.Contains(
            world.Knowledge[distantBuyer.HeadNpcId],
            o => o.ItemId == Item.Bread && o.SellerId == SellerId);
    }

    /// <summary>
    /// 【核心】テスト表 #31。見積もり価格(<see cref="ErrandPlanner"/> 5.5)と当日の提示価格が
    /// 一致する配置(売り手が床のまま提示する初日)で、段5b の実際の約定数量が、独立に
    /// <c>BuyerBudget.Decide</c> + <c>BuyerBudget.QuantityInUnits</c> で予測した q_個 と一致する。
    /// 必需(単位変換が恒等)と耐久(単位変換がCeilDiv)の両方で確かめる。
    /// </summary>
    /// <remarks>
    /// <b>レビュー2巡目 I-a-1 の訂正。</b>旧版は買い手・売り手を同区画(距離0)に置いていた。
    /// 同区画だと <see cref="ErrandPlanner"/> が候補区画を1件も持たず<c>VisitedDistrictIds</c>
    /// が常に空になるので、計画側が算出した q_個 がどのassertにも現れず、比べているのが
    /// 「段5bの約定数量」と「テスト内の独立予測」であって計画と購入の一致になっていなかった。
    /// 本版は<b>売り手を買い手と別区画に置く</b>。耐久の組は、正しい変換(q_個=2・余剰1500)なら
    /// 行き、取り違え(FloorDiv・q_個=1・余剰750)なら行かない境界(費用1000)に外出の費用を
    /// 置く ── 行かなければ約定自体が起きず(訪問区画に売り手が無い)、独立予測(非0)と実際の
    /// 約定数量(0)が食い違って落ちる。必需の組は変換が恒等なので同じ境界は作れない
    /// (<c>QuantityInUnits</c>がDurable以外でCeilDiv/FloorDivの分岐を持たない)。
    /// </remarks>
    /// <remarks>
    /// <b>耐久側の設計。</b>買い手に工具の市場参照(平均2000)を仕込み、基礎値を流動資金から
    /// 切り離す ── 参照が無いと基礎値=現金上限=流動資金となり、ゲートを通る実効価格の範囲では
    /// 資金上限が数量を1個に切り詰めてしまい、CeilDiv(1500,1000)=2 と FloorDiv(1500,1000)=1 の
    /// 分岐が資金上限の背後に隠れて見えなくなる(実測で確認した構造的な制約)。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(2026-09-20)。</b><c>BuyerBudget.QuantityInUnits</c> の <c>CeilDiv</c> を
    /// <c>FloorDiv</c> に変える変異を当てたところ、本テストの
    /// <c>Assert.Equal(2, predictedQuantity)</c> が実際値1(FloorDiv(1500,1000)=1)で失敗した
    /// (赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(2026-09-20、レビュー2巡目)。</b><see cref="ErrandPlanner"/> の
    /// <c>SurplusFor</c>(耐久の変換の呼び出し箇所)を、<c>BuyerBudget.QuantityInUnits</c>を
    /// 呼ばずローカルに<c>IntegerMath.FloorDiv(decision.Quantity, _definition.ToolDurabilityPerUnit)</c>
    /// を書き戻す変異(計画側だけ古い変換が残る「2か所に書いて片方だけ直す」の具体形)を
    /// 当てたところ、耐久の組の
    /// <c>Assert.Equal(predictedQuantity(=2), world.Households[0].WorkshopInventory[Item.Tools])</c>
    /// が実際値0(計画側の余剰が750&lt;費用1000で行かない判定になり、区画2が訪問されず
    /// 約定自体が起きない)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(2026-09-21、<c>mutator</c> が使い捨てworktreeで測定、対象コミット
    /// <c>39e367a</c>、M-5)。</b><c>TradeSystem</c> 段5bで <c>BuyerBudget.QuantityInUnits</c> を
    /// 外す変異を当てたところ本テストを含む5件が赤になった。<b>本テストは耐久の換算を
    /// 直接見ている意図的な検出器であり、これが主たる守り手である</b>
    /// (<see cref="SellerReferenceIsTakenBeforeTheSellableStockGate"/> の
    /// 前提行が拾うのは副産物としての巻き添えにすぎない)。
    /// </remarks>
    /// <remarks>
    /// <b>配置の変更(2026-09-21、#149)。</b>耐久(工具)の組は、買い手(Woodworker)の
    /// レシピ(<c>UnusedRecipe</c>)が木材(Timber、1次産品)を入力に取るため、窓口(距離2、
    /// district2のToolsと同じ費用)へ木材を安価に大量補充しに行く強い誘因が元々あった。
    /// #149で窓口がTools(都市生産品)も並べるようになると、買い手のTools見積もり(窓口の
    /// 記憶が無ければ床1500、district2の見積もりと同値)がタイになり、木材目当てで
    /// 1周目に確定する窓口の訪問だけでToolsの需要が(タイの価格で)「満たされた」ことになって、
    /// 2周目にdistrict2を追加する限界価値が0になる ── 本テストが見たい外出
    /// (district2でTools、q_個=2)が一度も起きなくなる(判別力が窓口に吸収される)。
    /// <b>買い手に窓口のToolsの値の記憶(高値999999)を持たせ、窓口をToolsの候補としては
    /// 実質的に外した</b>(SellerFloorPrice=1500そのものは動かしていない)。
    /// </remarks>
    [Fact]
    public void ErrandPlannerAndSettlementAgreeOnQuantity()
    {
        // 必需(パン、単位変換は恒等)。買い手と売り手を別区画(距離1、R以内)に置き、計画が
        // 実際に外出を選ぶことを確かめる。目標6・床1・流動資金大 → 上側clampで数量12。
        {
            const int BuyerDistrictId = 4;
            const int SellerDistrictId = 3; // 距離1(R以内)。

            var necessityTargetStockDays = new int[Item.Count];
            necessityTargetStockDays[Item.Bread] = 6;

            var definition = BuildShoppingDefinition(breadFloor: 1, necessityTargetStockDays: necessityTargetStockDays);
            var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);

            AddHousehold(world, id: 0, districtId: BuyerDistrictId, Occupation.Woodworker, liquidFunds: 100_000);
            AddHousehold(world, id: 1, districtId: SellerDistrictId, Occupation.Miller);
            world.Households[1].WorkshopInventory[Item.Bread] = 1000;

            EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1);

            // 独立予測: baseValue=cashCap=100000(参照なし)・実効価格1(床、距離1はR以内なので
            // 当日の提示価格をそのまま使う)・target6・expected0 ── PurchaseQuantityが上側
            // clamp(2×target)に当たり12。QuantityInUnits(Necessity)は恒等。
            var line = new DemandLine
            {
                Purpose = DemandPurpose.Necessity,
                ItemId = Item.Bread,
                HasMarketTerm = false,
                MarketTerm = 0,
                CashCap = 100_000,
                HasProfitCap = false,
                ProfitCap = 0,
                TargetStock = 6,
                ExpectedStock = 0,
                StockPressurePermille = 0, // HasMarketTerm=falseのゲートでは読まれない。
                BaseValue = 100_000,
                Budget = 1,
            };
            int predictedQuantity = BuyerBudget.QuantityInUnits(
                line.Purpose, BuyerBudget.Decide(line, effectivePrice: 1).Quantity, durabilityPerTool: 1);

            // 買い手と売り手が別区画になったことで計画が実際に外出を選んでいる(労働損失‰>0)。
            Assert.True(world.Households[0].ErrandLaborLossPermille > 0);
            Assert.Equal(predictedQuantity, world.Households[0].HouseholdInventory[Item.Bread]);
        }

        // 耐久(工具、単位変換はCeilDiv)。買い手と売り手を別区画(距離2、Rの外)に置く。
        // N=1(耐久値1000)・市場参照2000・床1500 → 数量1500、q_個=CeilDiv(1500,1000)=2
        // (FloorDivなら1)。費用1000は正しい変換(余剰1500)なら行き、取り違え(余剰750)なら
        // 行かない境界(上のremarks参照)。
        {
            const int ToolDurabilityPerUnit = 1000; // toolLifeLaborDays=1 × PermilleScale(1000)。
            const int MarketReferencePrice = 2000;  // 買い手の工具の市場参照(平均)。
            const int SellerFloorPrice = 1500;       // 床(参照なし初日の提示価格そのもの)。
            const int BuyerDistrictId = 0;
            const int SellerDistrictId = 2; // 距離2(Rの外) → 往復4時間(travelHoursPerDistrict=1)。
            const int WoodworkerOpportunityCostPerHour = 250; // 費用=4時間×250=1000。

            var toolRecipe = new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = Item.Tools, Quantity = 1 } },
                inputs: new[] { new ItemQuantity { ItemId = Item.IronOre, Quantity = 1 } },
                laborPermille: 1000);

            var externalBuyPrice = new int[Item.Count];
            externalBuyPrice[Item.Tools] = SellerFloorPrice;
            externalBuyPrice[Item.Grain] = 1; // UnusedRecipe(Baker等)が出力する品目。0だと構築時に投げる。

            var definition = EconomySystemTestFixtures.BuildDefinition(
                toolRecipe,
                toolLifeLaborDays: 1,
                // Woodworker(買い手の職業。Miller=0,Baker=1,Brewer=2,Woodworker=3,Smith=4)だけ
                // 機会費用を上げ、外出の費用を1000に固定する。
                opportunityCostBaseByOccupation: new[] { 1, 1, 1, WoodworkerOpportunityCostPerHour, 1 },
                rankCoefficientPermille: new[] { 1000, 1000, 1000 },
                travelHoursPerDistrict: 1,
                shipmentDays: 1,
                inputBufferDays: 1,
                externalBuyPriceOverride: externalBuyPrice);

            var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);
            AddHousehold(world, id: 0, districtId: BuyerDistrictId, Occupation.Woodworker, liquidFunds: 100_000);
            AddHousehold(world, id: 1, districtId: SellerDistrictId, Occupation.Miller);
            world.Households[1].WorkshopInventory[Item.Tools] = 100;

            // 買い手の工具の市場参照(前日以前の観測。基礎値を流動資金から切り離す。上の<remarks>参照)。
            // SellerId=999は実在しない売り手 ── ErrandPlannerの見積もり(5.3)は売り手Id一致を
            // 要求するのでこの観測には当たらず、距離2(Rの外)は床(1500)へ落ちる。
            EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 24);
            world.Knowledge[0].Add(new PriceObservation
            {
                ItemId = Item.Tools,
                LocationId = 0,
                Price = MarketReferencePrice,
                SellerId = 999, // 実在しない売り手。参照は売り手を問わず平均するので無関係でよい。
                ObservedAt = Tick.Zero,
                Source = ObservationSource.Direct,
            });

            // 配置の変更(2026-09-21、#149)。窓口はTools(都市生産品)も並べるようになったため、
            // 買い手の見積もり(距離2、Rの外)は窓口も候補にする。窓口の記憶が無いと3段目
            // (未知価格の床)= SellerFloorPrice(1500)と同値になり district2 の見積もりと
            // タイになる。買い手はどのみち窓口へ木材(Timber、1次産品)を買いに行く
            // (UnusedRecipe(Woodworker)の入力補充。この誘因は#149と無関係に既に強い)ため、
            // 窓口の訪問はTools抜きでも1周目で確定してしまい、Toolsの窓口見積もりが
            // district2と同値である以上、2周目でdistrict2を追加する限界価値が0になって
            // 本テストが見たい外出(district2、Tools)が一度も起きなくなる(判別力が窓口に
            // 吸収される)。<b>買い手に窓口のToolsの値の記憶(高値。SellerFloorPriceを
            // 大きく上回る)を持たせ、窓口をToolsの候補としては実質的に外す</b> ──
            // SellerFloorPrice(1500、テストの構成値)そのものは動かさない。
            world.Knowledge[0].Add(new PriceObservation
            {
                ItemId = Item.Tools,
                LocationId = 0,
                Price = 999_999,
                SellerId = HouseholdState.ExternalMarketSellerId,
                ObservedAt = Tick.Zero,
                Source = ObservationSource.Direct,
            });

            EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1);

            // 独立予測: 目標=耐久値そのもの(ToolTargetStockPermille=1000・RankCoefficient=1000)、
            // 予想在庫0(WorkshopInventory[Tools]=0・ToolWear=0)、基礎値=市場参照(生。2000)。
            var line = new DemandLine
            {
                Purpose = DemandPurpose.Durable,
                ItemId = Item.Tools,
                HasMarketTerm = true,
                MarketTerm = MarketReferencePrice,
                CashCap = 100_000,
                HasProfitCap = false,
                ProfitCap = 0,
                TargetStock = ToolDurabilityPerUnit,
                ExpectedStock = 0,
                StockPressurePermille = BuyerBudget.StockPressurePermille(expectedStock: 0, targetStock: ToolDurabilityPerUnit),
                BaseValue = MarketReferencePrice,
                Budget = 1,
            };
            var decision = BuyerBudget.Decide(line, effectivePrice: SellerFloorPrice);
            int predictedQuantity = BuyerBudget.QuantityInUnits(
                line.Purpose, decision.Quantity, ToolDurabilityPerUnit);

            Assert.Equal(2, predictedQuantity); // CeilDiv(1500,1000)=2(FloorDivなら1、分岐の確認)。

            // 正しい変換なら価値500(余剰1500-費用1000)で行く。行かなければ約定自体が起きない。
            Assert.True(world.Households[0].ErrandLaborLossPermille > 0);
            Assert.Equal(predictedQuantity, world.Households[0].WorkshopInventory[Item.Tools]);
        }
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

    /// <summary>
    /// 【核心】テスト表 #18。外出しない日に、前日の値(100)が0で上書きされる
    /// (UnaffordableNecessityCountを毎日0で上書きするのと同じ理由。書かない日があると
    /// 前日の損失が翌日以降も効き続ける)。
    /// </summary>
    [Fact]
    public void ErrandLaborLossIsWrittenEveryDayIncludingZero()
    {
        // 需要が一切無い(必需・嗜好の目標在庫日数がすべて0)世帯 → どの行も需要リストに残らず、
        // ErrandPlanner.Planは必ずLaborLossPermille=0を返す。
        var definition = BuildShoppingDefinition();
        var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);
        AddHousehold(world, id: 0, districtId: 4, Occupation.Woodworker, liquidFunds: 1000);

        // 前日の(古い)損失を直接立てておく。
        world.Households[0].ErrandLaborLossPermille = 100;

        EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1);

        Assert.Equal(0, world.Households[0].ErrandLaborLossPermille);
    }

    /// <summary>
    /// 【核心】テスト表 #21。issue #98の閉じる条件。パイプラインを2日走らせ、1日目に外出した
    /// 世帯の2日目のProductionRunsが、同条件で外出しなかった世帯より少ない。1日目のProductionRuns
    /// は同じ(損失は翌日に効く)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-20)。</b><c>TradeSystem.Step</c> の段5aで
    /// <c>household.ErrandLaborLossPermille = plan.LaborLossPermille;</c> の代入を消す変異
    /// (書かない。<c>ProductionSystem</c> は常に0を読む)を当てたところ、
    /// <c>Assert.True(travelerDay2Runs &lt; controlDay2Runs)</c> が実際値false
    /// (travelerDay2Runsがcontrolと同じになる。外出しても翌日の生産が減らない経路。
    /// issue #98の閉じる条件そのもの)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void NextDaysProductionDropsByTheErrandLaborLoss()
    {
        const int TravelerId = 0;
        const int ControlId = 1;
        const int SellerId = 2;
        const int SellerDistrictId = 2; // 距離2。

        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = Item.Grain, Quantity = 2 } },
            laborPermille: 300);

        var externalBuyPrice = new int[Item.Count];
        externalBuyPrice[Item.Grain] = 1; // 床(安い。travelerを誘引する)。
        externalBuyPrice[Item.Flour] = 1; // recipeの出力(都市生産品)。値そのものは使わない。

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe,
            laborPermilleByRank: new[] { 1000, 800, 300 },
            dailyConsumptionPerNpcByRank: GrainConsumptionTable(),
            necessityTargetStockDays: TargetStockDaysFor(Item.Grain),
            tolerancePermille: 1200,
            opportunityCostBaseByOccupation: new[] { 1, 1, 1, 1, 1 },
            rankCoefficientPermille: new[] { 1000, 1000, 1000 },
            travelHoursPerDistrict: 1,
            shipmentDays: 1,
            inputBufferDays: 1,
            disposableHours: 12,
            externalBuyPriceOverride: externalBuyPrice);

        var world = new World(npcCount: 3, householdCount: 3, itemCount: Item.Count);

        // traveler: 遠方(distance2)のGrain売り手のため必ず外出する(必需・現金潤沢)。
        AddHousehold(world, id: TravelerId, districtId: 0, Occupation.Miller, liquidFunds: 1000);
        world.Households[TravelerId].WorkshopInventory[Item.Grain] = 1000; // 生産の入力切れにしない。
        world.Households[TravelerId].WorkshopInventory[Item.Tools] = 1; // 設備係数1000‰。

        // control: 外出しない(資金0で現金上限0、需要が立たない)。
        AddHousehold(world, id: ControlId, districtId: 0, Occupation.Miller, liquidFunds: 0);
        world.Households[ControlId].WorkshopInventory[Item.Grain] = 1000;
        world.Households[ControlId].WorkshopInventory[Item.Tools] = 1;

        // 売り手(traveler専用の遠方の穀物売り手)。
        AddHousehold(world, id: SellerId, districtId: SellerDistrictId, Occupation.Baker, liquidFunds: 0);
        world.Households[SellerId].WorkshopInventory[Item.Grain] = 1000;

        var scheduler = new SimScheduler(
            new ISimSystem[] { new ProductionSystem(definition), new TradeSystem(definition) },
            new RandomSource(1));

        scheduler.Advance(world, ticks: 24); // 1日目

        int travelerDay1Runs = world.Households[TravelerId].ProductionRuns;
        int controlDay1Runs = world.Households[ControlId].ProductionRuns;

        // 1日目のProductionRunsは同じ(損失はまだ効かない。前日=初期値0を順1が読む)。
        Assert.Equal(controlDay1Runs, travelerDay1Runs);

        // 1日目に実際に外出したことを確かめる(前提)。
        Assert.True(world.Households[TravelerId].ErrandLaborLossPermille > 0);
        Assert.Equal(0, world.Households[ControlId].ErrandLaborLossPermille);

        scheduler.Advance(world, ticks: 24); // 2日目

        int travelerDay2Runs = world.Households[TravelerId].ProductionRuns;
        int controlDay2Runs = world.Households[ControlId].ProductionRuns;

        Assert.True(travelerDay2Runs < controlDay2Runs);
    }

    /// <summary>
    /// 【核心】テスト表 #22(<a href="https://github.com/stama72/visionary/issues/107">#107</a>、
    /// #38 相乗り)。世帯A(Id0)が世帯B(Id1)から買い、その売上でBの<c>DemandLine.CashCap</c>が
    /// 閾(穀物の床100)をまたぐ帯に置く。Bの予算はその日の朝の資金(95)から決まり、Aの支払いを
    /// 受け取った後の資金では決まらない。
    /// </summary>
    /// <remarks>
    /// <b>M-6(実測されたら書く)。</b>段4のループを消し、段5のループの先頭で
    /// <c>_buyerDemand.Build</c> を呼ぶ変異(段4を段5へ畳む)を当てると、Aの支払いを受け取った
    /// <b>後</b>のBの流動資金(105)でCashCapが計算され、Bの必需(穀物)のゲートが誤って開くことを
    /// 期待する(タスク仕様)。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(2026-09-21、<c>mutator</c> が使い捨てworktreeで測定、対象コミット
    /// <c>39e367a</c>、M-6)。</b>段4を段5の先頭へ畳む変異は期待どおり赤になった
    /// (<a href="https://github.com/stama72/visionary/issues/107">#107</a> の閉じる条件の実測)。
    /// </remarks>
    [Fact]
    public void DemandIsBuiltBeforeAnyHouseholdShops()
    {
        const int BreadFloor = 10;
        const int GrainFloor = 100;

        var necessityTargetStockDays = new int[Item.Count];
        necessityTargetStockDays[Item.Bread] = 1;
        necessityTargetStockDays[Item.Grain] = 1;

        var dailyConsumptionRow = new int[Item.Count];
        dailyConsumptionRow[Item.Bread] = 1;
        dailyConsumptionRow[Item.Grain] = 1;
        var dailyConsumptionPerNpcByRank = new[]
        {
            (int[])dailyConsumptionRow.Clone(),
            (int[])dailyConsumptionRow.Clone(),
            (int[])dailyConsumptionRow.Clone(),
        };

        var externalBuyPrice = new int[Item.Count];
        externalBuyPrice[Item.Bread] = BreadFloor;
        externalBuyPrice[Item.Grain] = GrainFloor;

        var breadRecipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 1000);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            breadRecipe,
            dailyConsumptionPerNpcByRank: dailyConsumptionPerNpcByRank,
            necessityTargetStockDays: necessityTargetStockDays,
            opportunityCostBaseByOccupation: new[] { 1, 1, 1, 1, 1 },
            rankCoefficientPermille: new[] { 1000, 1000, 1000 },
            travelHoursPerDistrict: 1,
            inputBufferDays: 1,
            shipmentDays: 1,
            externalBuyPriceOverride: externalBuyPrice);

        var world = new World(npcCount: 3, householdCount: 3, itemCount: Item.Count);

        // A(Id0): 潤沢な資金でBからパンを買う(同区画。移動の摩擦は本テストの関心の外)。
        AddHousehold(world, id: 0, districtId: 0, Occupation.Woodworker, liquidFunds: 1000);
        world.Households[0].WorkshopInventory[Item.Timber] = 1_000_000; // 自分の入力を中立化。
        world.Households[0].WorkshopInventory[Item.Tools] = 5; // 耐久の需要を中立化。

        // B(Id1): パンの売り手。朝の資金95(穀物の床100未満) → ゲートが閉じるはず。
        AddHousehold(world, id: 1, districtId: 0, Occupation.Miller, liquidFunds: 95);
        world.Households[1].WorkshopInventory[Item.Bread] = 1000;

        // C(Id2): 穀物の売り手(UnusedRecipe)。
        AddHousehold(world, id: 2, districtId: 0, Occupation.Baker, liquidFunds: 0);
        world.Households[2].WorkshopInventory[Item.Grain] = 1000;
        world.Households[2].WorkshopInventory[Item.Timber] = 1_000_000;
        world.Households[2].WorkshopInventory[Item.Tools] = 5;

        EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1);

        // 前提: Aが実際にBからパンを買い、Bの流動資金が朝の95から増えている。
        Assert.True(world.Households[0].HouseholdInventory[Item.Bread] > 0);
        Assert.True(world.Households[1].LiquidFunds > 95, "Bの流動資金が増えていない(Aの支払いが届いていない)。");

        // Bの必需(穀物)は朝の資金95(床100未満)でゲートが閉じ、資金不足に数えられる。
        Assert.Equal(1, world.Households[1].UnaffordableNecessityCount);
        Assert.Equal(0, world.Households[1].HouseholdInventory[Item.Grain]);
    }

    /// <summary>
    /// 【核心】テスト表 #23(<a href="https://github.com/stama72/visionary/issues/107">#107</a>、
    /// #38 相乗り)。世帯A(Id0)に閾在庫を超える販売在庫を持たせ、世帯B(Id1)がその品目を買う。
    /// BはAの在庫(輸出で減る前の全量)を買えている。
    /// </summary>
    /// <remarks>
    /// <b>M-7(実測されたら書く)。</b>段6のループを消し、段5のループの末尾で
    /// <c>RunOneHouseholdsExport</c> を呼ぶ変異(段6を段5へ畳む)を当てると、Aが自分の順番で
    /// 閾在庫を残して即座に輸出してしまい、Bが着いたときには在庫が閾在庫(1)まで減っていることを
    /// 期待する(タスク仕様)。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(2026-09-21、<c>mutator</c> が使い捨てworktreeで測定、対象コミット
    /// <c>39e367a</c>、M-7)。</b>段6を段5の末尾へ畳む変異は期待どおり赤になった。
    /// </remarks>
    [Fact]
    public void ExportRunsAfterEveryHouseholdHasShopped()
    {
        const int BreadFloor = 10;

        var necessityTargetStockDays = new int[Item.Count];
        necessityTargetStockDays[Item.Bread] = 50; // dailyConsumption=1なので目標在庫=50。

        var dailyConsumptionRow = new int[Item.Count];
        dailyConsumptionRow[Item.Bread] = 1;
        var dailyConsumptionPerNpcByRank = new[]
        {
            (int[])dailyConsumptionRow.Clone(),
            (int[])dailyConsumptionRow.Clone(),
            (int[])dailyConsumptionRow.Clone(),
        };

        var externalBuyPrice = new int[Item.Count];
        externalBuyPrice[Item.Bread] = BreadFloor;
        externalBuyPrice[Item.Grain] = 1; // UnusedRecipe(Baker等)が出力する都市生産品。0だと構築時に投げる。

        var breadRecipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 1000);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            breadRecipe,
            dailyConsumptionPerNpcByRank: dailyConsumptionPerNpcByRank,
            necessityTargetStockDays: necessityTargetStockDays,
            opportunityCostBaseByOccupation: new[] { 1, 1, 1, 1, 1 },
            rankCoefficientPermille: new[] { 1000, 1000, 1000 },
            travelHoursPerDistrict: 1,
            inputBufferDays: 1,
            shipmentDays: 1,
            externalBuyPriceOverride: externalBuyPrice);

        var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);

        // A(Id0): パンの売り手。出荷目標在庫(閾在庫の代用、相場基準が無い初日)はごく小さい
        // (生産能力1×出力数量1×出荷日数1=1)のに対し、実際の在庫は200(閾在庫を大きく超える)。
        AddHousehold(world, id: 0, districtId: 0, Occupation.Miller, liquidFunds: 0);
        world.Households[0].WorkshopInventory[Item.Bread] = 200;
        world.Households[0].WorkshopInventory[Item.Tools] = 5; // 耐久の需要を中立化。

        // B(Id1): パンの買い手(同区画。移動の摩擦は本テストの関心の外)。潤沢な資金。
        AddHousehold(world, id: 1, districtId: 0, Occupation.Woodworker, liquidFunds: 1_000_000);
        world.Households[1].WorkshopInventory[Item.Timber] = 1_000_000; // 自分の入力を中立化。
        world.Households[1].WorkshopInventory[Item.Tools] = 5;

        EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1);

        // Bは閾在庫(1)ではなく、Aの全量(200)を前提にした量を買えている。
        Assert.True(
            world.Households[1].HouseholdInventory[Item.Bread] > 1,
            $"Bの購入量が{world.Households[1].HouseholdInventory[Item.Bread]}個(閾在庫1個を超えない"
                + "= 段6が段5へ畳まれ、Aが自分の順番で先に輸出してしまった可能性)。");

        // 別表R-3(レビュー1巡目)。上の表明はBの在庫だけを見ており、Aが実際に輸出したことを
        // 確かめていない ── 空振り防止(閾在庫の式・出荷目標在庫・Tの検査のいずれかが将来動いて
        // 輸出そのものが起きなくなっても、上の表明だけでは緑のままになる)。
        int externalSaleCount = world.Ledgers[0].Count(
            entry => entry.Direction == LedgerDirection.Sale
                && entry.CounterpartyId == HouseholdState.ExternalMarketSellerId);
        Assert.True(externalSaleCount >= 1, "Aの外部Saleの行が1件も無い(輸出が実際には起きていない)。");
    }

    /// <summary>
    /// 別表(続き)R-5(レビュー2巡目)。段6の閾在庫は<b>売り手側(速い側、
    /// <see cref="MarketReference.TrySeller"/>)</b>の相場基準で決まり、買い手側(遅い側、
    /// <see cref="MarketReference.TryBuyer"/>)を読み直してはならない(GDD02c §1.2末尾)。
    /// 2日目に、他の売り手の観測だけの平均(<c>TryBuyer</c>)と、それに自分の前日の
    /// 約定単価(2)を混ぜた平均(<c>TrySeller</c>)が異なる値になる構成を作り、閾在庫を挟む
    /// 位置に販売在庫を置く。
    /// </summary>
    /// <remarks>
    /// <b>M-11(実測されたら書く)。</b>段6が<c>hasSellerReference</c>/<c>sellerReference</c>を
    /// 捨てて<c>MarketReference.TryBuyer</c>を呼び直す変異(タスク仕様が名指し)を当てると、
    /// 相場基準が20(買い手側)になり閾在庫が20(出荷目標在庫10の2倍・上限)へ跳ね上がる。
    /// 販売在庫18はこの閾を超えないため輸出が起きなくなり、本テストの表明(在庫16・数量2の
    /// 外部Sale)が崩れることを期待する。<b>取り違えても例外は出ない</b>
    /// ([GDD02c §1.2](../../../docs/03-gdd/02c-price-and-budget.md)末尾)。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(2026-09-21、<c>mutator</c> が使い捨てworktreeで測定、対象コミット
    /// <c>39e367a</c>、M-11)。</b>段6が<c>MarketReference.TryBuyer</c>を呼び直す変異は
    /// 期待どおり赤になった(この実測は下記#149追随の期待値変更より前に行われている。
    /// 変異が壊す不変条件そのものは変わっていない)。
    /// </remarks>
    /// <remarks>
    /// <b>#149 追随(2026-09-21)。期待値の変更。</b>本テストの世帯は「段6のIsWithinReachを
    /// 常に真にする」ため中心区画に固定しており(上記コメント)、これは動かせない。
    /// #149で窓口が都市生産品(パン)も売るようになったため、中心に居るこの世帯は毎日
    /// 窓口自身のパン価格(この定義では20)も観測するようになり(決定5、
    /// <see cref="MarketReference.TrySeller"/>が畳む「他の売り手」に窓口が加わる)、
    /// 元々手で仕込んだ他の売り手の観測(20)に加えて窓口の観測(20)も1日目の終わりに
    /// 積まれる。<c>TrySeller</c>の平均は{20(other), 20(window), 2(own settled)}の3項
    /// (<c>CeilDiv(42,3)</c>=14)になり、旧来の2項平均(11)ではなくなった ── 閾在庫は
    /// 12→16に変わる。<c>TryBuyer</c>側は窓口の観測も同値(20)なので平均は変わらず20のまま
    /// (判別力は保たれる)。販売在庫を15→18(新しい閾16と20の間)、期待在庫を12→16、
    /// 期待数量を3→2へ実測して置き直した。
    /// </remarks>
    [Fact]
    public void ExportUsesTheSellerSideMarketReference()
    {
        const int OtherSellerId = 999;

        var externalBuyPrice = new int[Item.Count];
        externalBuyPrice[Item.Bread] = 10;
        externalBuyPrice[Item.Grain] = 1; // UnusedRecipe(Baker等)が出力する都市生産品。0だと構築時に投げる。

        var breadRecipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 1000);

        // shipmentDays=10 → 出荷目標在庫(生産能力1×出力数量1×出荷日数10)=10。
        var definition = EconomySystemTestFixtures.BuildDefinition(
            breadRecipe,
            opportunityCostBaseByOccupation: new[] { 1, 1, 1, 1, 1 },
            rankCoefficientPermille: new[] { 1000, 1000, 1000 },
            travelHoursPerDistrict: 1,
            inputBufferDays: 1,
            shipmentDays: 10,
            externalBuyPriceOverride: externalBuyPrice);

        var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);
        // 中心区画に置く(段6のIsWithinReachを常に真にし、Tの検査を本テストの関心の外にする)。
        AddHousehold(world, id: 0, districtId: District.ExternalMarketDistrictId, Occupation.Miller);

        var system = new TradeSystem(definition);

        EconomySystemTestFixtures.RunDays(world, system, days: 1); // 1日目: 在庫0で何も起きない。

        // 他の売り手の観測(20)を1件仕込む(前日=1日目の日付)。TryBuyerはこれだけを平均する。
        world.Knowledge[0].Add(new PriceObservation
        {
            ItemId = Item.Bread,
            LocationId = 0,
            Price = 20,
            SellerId = OtherSellerId,
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        });

        // 自分の前日の約定単価(2)を帳簿へ直接置く。TrySellerだけがこれを平均へ混ぜる
        // (TryBuyerは帳簿を読まない)── 同じ観測集合から2つの異なる相場基準を作る一手。
        world.Ledgers[0].Add(new LedgerEntry
        {
            CounterpartyId = HouseholdState.ExternalMarketSellerId,
            ItemId = Item.Bread,
            Quantity = 1,
            UnitPrice = 2,
            OccurredAt = Tick.Zero,
            Terms = LedgerTerms.Cash,
            Direction = LedgerDirection.Sale,
        });

        // 2日目の朝、閾在庫(売り手側16/買い手側20)の間に来る在庫(18)を直接与える
        // (段5の買い物は本テストの関心の外)。
        world.Households[0].WorkshopInventory[Item.Bread] = 18;

        EconomySystemTestFixtures.RunDays(world, system, days: 1); // 2日目

        // 売り手側の相場基準(14)を使えば閾在庫16・超過分2が輸出される。
        Assert.Equal(16, world.Households[0].WorkshopInventory[Item.Bread]);
        Assert.Contains(
            world.Ledgers[0],
            entry => entry.Direction == LedgerDirection.Sale
                && entry.CounterpartyId == HouseholdState.ExternalMarketSellerId
                && entry.ItemId == Item.Bread
                && entry.Quantity == 2
                && entry.UnitPrice == 10);
    }

    /// <summary>
    /// 別表(続き)R-6(レビュー2巡目)。段1の相場基準(<see cref="MarketReference.TrySeller"/>)の
    /// 算出は<c>sellableStock &lt;= 0</c>の<c>continue</c>より<b>上</b>にある(タスク仕様
    /// 「決めたこと」)。段1時点で販売在庫0・有効な相場基準ありの世帯(工具を作るMiller)が、
    /// 段5bで自分の出力品目(工具)を耐久として買い入れ(M0では工具を買った鍛冶が踏む経路)、
    /// 段6の閾在庫が相場基準ベース(0)で決まることを、出荷目標在庫ベース(1、相場基準が
    /// 無い日の代用)とは異なる結果(輸出の有無)で見る。
    /// </summary>
    /// <remarks>
    /// <b>M-12(実測されたら書く)。</b><c>MarketReference.TrySeller</c>の呼び出しと2つの配列
    /// (<c>hasSellerReference</c>/<c>sellerReference</c>)への代入を、<c>sellableStock &lt;= 0</c>の
    /// <c>continue</c>の<b>下</b>へ戻す変異(タスク仕様が名指し)を当てると、段1時点で在庫0の
    /// この世帯は控えを残せず(既定値<c>false</c>/<c>0</c>のまま)、段6が出荷目標在庫(1)を
    /// 代用の閾在庫として使う。買い入れた工具はちょうど1個で、超過分は
    /// <c>max(0, 1-1)=0</c>となり輸出が起きなくなることを期待する。戻しても提示価格は
    /// 変わらない(<c>Market</c>への書き込みは<c>continue</c>の下のまま)ので、値付けのテストは
    /// この実装ミスを検出しない。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(2026-09-21、<c>mutator</c> が使い捨てworktreeで測定、対象コミット
    /// <c>39e367a</c>、M-12)。</b>段1の相場基準の算出を<c>sellableStock &lt;= 0</c>の
    /// <c>continue</c>の下へ戻す変異は期待どおり赤になった。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(2026-09-21、<c>mutator</c> が使い捨てworktreeで測定、対象コミット
    /// <c>39e367a</c>、M-5)。</b><c>BuyerBudget.QuantityInUnits</c>(耐久の換算)を外す変異は
    /// 本テストを含む5件を赤にした。<b>耐久の換算を守っているテストは本テストだけではない</b> ──
    /// <see cref="ErrandPlannerAndSettlementAgreeOnQuantity"/> が耐久の換算を直接見ている
    /// 意図的な検出器であり、こちらが主たる守り手である(下の<c>Quantity == 1</c>に付けた
    /// レビュー3巡目の注記は、R-6が段1の相場基準の位置を拘束しているという事実自体は
    /// 実測どおりだが、「換算を守っているテストは他に無い」という前提は本実測で否定された)。
    /// </remarks>
    /// <remarks>
    /// <b>初期条件の組み直し(2026-09-21、#149・仕様の訂正。フェーズ2)。</b>household0は
    /// 「段6のIsWithinReachを常に真にする」ため中心区画に固定されており動かせず、距離0では
    /// <see cref="ErrandPlanner.EstimateWindowPrice"/>が常に「今日の知覚」を使うため記憶での
    /// 窓口の無効化も効かない。#149で窓口が工具も売るようになった結果、household0は中心に
    /// 居るだけで1日目から窓口の工具の当日価格(天井200)を知覚し、旧来の「1日目: 双方とも
    /// 工具在庫0で何も起きない」という前提そのものが崩れる。<b>保たれるべき判別力は具体値
    /// (相場基準51・床100・数量1)ではなく順序である</b>(相場基準がsellableStock&lt;=0の
    /// ゲートより先に取られること)ため、初期条件を次のように組み直した:
    /// <list type="bullet">
    /// <item><c>liquidFunds</c>を150に絞った(旧1,000,000)。窓口の1日目の実価格(天井200)は
    /// 耐久の現金上限ゲート(<c>CashCap=liquidFunds</c>)で弾かれ(200&gt;150)、household1が
    /// 2日目に床100で出品したときだけ通る(100≦150)。窓口を品目ゲートで外すのではなく
    /// 資金で塞ぐことで、値付け・見積もりの経路(このテストの対象外)を動かさずに済む。</item>
    /// <item>他の売り手の観測を1件(100)から3件(997・998・999、いずれも1)へ増やした。
    /// household0は中心に居るため2日目には窓口自身の工具観測(天井200)も自動的に
    /// <see cref="MarketReference.TrySeller"/>/<see cref="MarketReference.TryBuyer"/>の
    /// 「他の売り手」に畳まれる(決定5)。旧来の2件(他1件+自分の前日約定)のままだと
    /// 窓口の200が混ざって相場基準が101前後になり、閾在庫が2(出荷目標在庫の代用1を上回る)
    /// になって「相場基準を使う/使わない」の差が消える(<b>組み替えても緑になる</b>=
    /// 判別力の死)。<see cref="ExternalMarket.ExportThresholdStock"/>を直接呼んで実測した
    /// ところ、相場基準51(=(1+1+1+200)/4を切り上げ)なら閾在庫0、相場基準を使わない
    /// フォールバック(出荷目標在庫1)なら閾在庫1になり、どちらも household0 が2日目に
    /// 買う1個(下記)に対して異なる結果(輸出する/しない)を生む ── <b>判別力は死んでいない</b>。</item>
    /// <item><c>tolerancePermille</c>を3000へ上げた。相場基準を意図的に低く保った副作用で、
    /// 買い物側(<see cref="MarketReference.TryBuyer"/>)の許容乖離ゲートが締まりすぎて
    /// household1からの購入(床100)自体が通らなくなる(相場基準51に既定の許容乖離1000‰を
    /// 掛けても51 &lt; 100)。許容乖離を広げて購入そのものは通す。</item>
    /// </list>
    /// </remarks>
    [Fact]
    public void SellerReferenceIsTakenBeforeTheSellableStockGate()
    {
        const int OtherSellerId1 = 997;
        const int OtherSellerId2 = 998;
        const int OtherSellerId3 = 999;

        var externalBuyPrice = new int[Item.Count];
        externalBuyPrice[Item.Grain] = 1; // UnusedRecipe(Baker等)が出力する都市生産品。0だと構築時に投げる。
        externalBuyPrice[Item.Tools] = 100;

        var toolsRecipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Tools, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 1000);

        // shipmentDays=1(既定) → 出荷目標在庫(生産能力1×出力数量1×出荷日数1)=1。
        //
        // 配置の変更(2026-09-21、#149・仕様の訂正。フェーズ2)。household0は「段6の
        // IsWithinReachを常に真にする」ため中心区画に固定されており(下記)、中心では
        // ErrandPlanner.EstimateWindowPriceが常に「今日の知覚」を使うため、窓口を記憶で
        // 無効化する手が使えない。窓口が工具(都市生産品)も売るようになった#149以降、
        // household0は中心に居るだけで毎日窓口の工具価格(床100の天井、200)を観測してしまい
        // (決定5、MarketReference.TrySellerが畳む)、初期条件をゼロから組み直す必要がある。
        // tolerancePermilleを大きく上げているのは、後述のとおり相場基準を意図的に低く
        // 保つ(輸出の閾在庫を0にする)ことの副作用で買い物側の許容乖離ゲートが締まりすぎる
        // ことを防ぐためである(下記「相場基準の設計」参照)。
        var definition = EconomySystemTestFixtures.BuildDefinition(
            toolsRecipe,
            externalBuyPriceOverride: externalBuyPrice,
            tolerancePermille: 3000);

        var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);

        // household0: 工具を作るMiller。段1時点で工具在庫0(相場基準はあるが売り注文は無い)。
        // 中心区画に置く(段6のIsWithinReachを常に真にし、Tの検査を本テストの関心の外にする)。
        //
        // liquidFundsを150に絞る(#149前は1,000,000だった)。窓口の今日の実価格(天井200)は
        // 1日目のうちに耐久の現金上限ゲート(CashCap=liquidFunds)で弾かれ(200>150)、
        // household1が2日目に床100で出品したときだけ通る(100<=150)。これにより、
        // #149で新たに生まれた「1日目のうちに窓口から工具を買ってしまう」経路を、窓口を
        // 品目ゲートで外すのではなく資金で塞ぐ(このテストは値付け・見積もりの経路を
        // 動かしたくないため)。
        AddHousehold(
            world, id: 0, districtId: District.ExternalMarketDistrictId, Occupation.Miller,
            liquidFunds: 150);

        // household1: 工具の売り手。1日目は在庫0(オファーを立てない)。
        AddHousehold(world, id: 1, districtId: District.ExternalMarketDistrictId, Occupation.Miller);

        var system = new TradeSystem(definition);

        EconomySystemTestFixtures.RunDays(world, system, days: 1); // 1日目: 双方とも工具在庫0。

        // 相場基準の設計(2026-09-21、#149・仕様の訂正)。household0は中心に居るため、1日目の
        // うちに窓口自身の工具観測(天井200、SellerId=窓口の予約Id)を自動的に得ている
        // (決定5)。これは「他の売り手」として2日目のMarketReference.TrySeller/TryBuyerの
        // 両方に畳まれる。旧テストが使った「他の売り手1件(100)+自分の前日約定(1)」の2項では、
        // 窓口の200が混ざると相場基準が101前後にしかならず、輸出の閾在庫が2(出荷目標在庫の
        // 代用1を上回る)になって「相場基準を使う/使わない」の差が消える(組み替えても
        // 緑になる=判別力が死ぬ)。そこで他の売り手をさらに2件(997・998、いずれも安値1)
        // 追加し、窓口の200を薄める ── 4件(997・998・999・窓口)の平均で相場基準を
        // 十分低くし、輸出の閾在庫をちょうど0にする(下記assert直前のコメントで実測値を書く)。
        // TryBuyer(買い物側)も同じ観測集合を読むため相場基準が下がり、床100が「許容乖離
        // (tolerancePermille=3000)を掛けた相場基準」を上回って買い物のゲートが締まる ──
        // それを避けるために上でtolerancePermilleを大きく上げてある。
        world.Knowledge[0].Add(new PriceObservation
        {
            ItemId = Item.Tools,
            LocationId = 0,
            Price = 1,
            SellerId = OtherSellerId1,
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        });
        world.Knowledge[0].Add(new PriceObservation
        {
            ItemId = Item.Tools,
            LocationId = 0,
            Price = 1,
            SellerId = OtherSellerId2,
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        });
        world.Knowledge[0].Add(new PriceObservation
        {
            ItemId = Item.Tools,
            LocationId = 0,
            Price = 1,
            SellerId = OtherSellerId3,
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        });

        // 2日目の朝、household1に工具在庫を持たせる(段1で相場基準の無いhousehold1は
        // 床=100で出品する。既定の在庫比の下限係数500‰により、相場基準がいくら高くても
        // 提示価格は床のまま ── OfferPrice.PriceCoefficientPermilleの下限)。
        world.Households[1].WorkshopInventory[Item.Tools] = 50;

        EconomySystemTestFixtures.RunDays(world, system, days: 1); // 2日目

        // 前提: household0が実際に工具を1個買っている(段5b、耐久)。実際の資金(150)が
        // 実効価格(100)の1個ぶんしか無いため(FundsCap=FloorDiv(150,100)=1)、需要側の
        // 線形解(2個相当)より少なく約定する ── #149前は資金を絞っていなかったので
        // この経路も新たに踏むようになった。
        Assert.Contains(
            world.Ledgers[0],
            entry => entry.Direction == LedgerDirection.Purchase
                && entry.ItemId == Item.Tools
                && entry.CounterpartyId == 1
                && entry.Quantity == 1);

        // 相場基準(実測: (1+1+1+200)/4を切り上げて51)を使えば閾在庫0・買った1個が
        // そのまま輸出される(household0の工具在庫が0に戻る)。相場基準を使わない
        // (誤って段6のゲートの後で控えを取る)実装なら、出荷目標在庫(1)が代わりに使われ、
        // 買った1個は閾を超えず輸出されない(工具在庫は1のまま) ── 本テストはこの差を見る。
        Assert.Equal(0, world.Households[0].WorkshopInventory[Item.Tools]);
        Assert.Contains(
            world.Ledgers[0],
            entry => entry.Direction == LedgerDirection.Sale
                && entry.CounterpartyId == HouseholdState.ExternalMarketSellerId
                && entry.ItemId == Item.Tools
                && entry.Quantity == 1
                && entry.UnitPrice == 100);
    }

    private static int[][] GrainConsumptionTable()
    {
        var row = new int[Item.Count];
        row[Item.Grain] = 1;

        return new[] { (int[])row.Clone(), (int[])row.Clone(), (int[])row.Clone() };
    }
}
