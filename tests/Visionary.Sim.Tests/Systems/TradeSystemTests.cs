using Visionary.Sim.Determinism;
using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="TradeSystem"/>(GDD02 §8.1・§8.1.1・§6.3、#35 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class TradeSystemTests
{
    private static Recipe MillerRecipe() =>
        new(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = Item.Grain, Quantity = 2 } },
            laborPermille: 1000);

    /// <summary>
    /// 出荷目標在庫の表。Miller(添字0)の小麦粉だけ <paramref name="flourTarget"/>、
    /// 他4職業(EconomySystemTestFixtures.UnusedRecipe)は出力itemId 0 に1を置く。
    /// </summary>
    private static int[][] ShipmentTargets(int flourTarget)
    {
        var rows = new int[5][];

        var millerRow = new int[Item.Count];
        millerRow[Item.Flour] = flourTarget;
        rows[0] = millerRow;

        for (int occupationId = 1; occupationId < 5; occupationId++)
        {
            var row = new int[Item.Count];
            row[0] = 1; // UnusedRecipeの出力itemId(EconomySystemTestFixtures参照)
            rows[occupationId] = row;
        }

        return rows;
    }

    private static WorldDefinition BuildDefinition(
        int shipmentTarget, int minimumMarginPermille = 0) =>
        EconomySystemTestFixtures.BuildDefinition(
            MillerRecipe(),
            minimumMarginPermille: minimumMarginPermille,
            shipmentTargetStockByOccupation: ShipmentTargets(shipmentTarget));

    private static PriceObservation Observation(int itemId, int sellerId, int price, Tick observedAt) =>
        new()
        {
            ItemId = itemId,
            LocationId = 0,
            Price = price,
            SellerId = sellerId,
            ObservedAt = observedAt,
            Source = ObservationSource.Direct,
        };

    /// <summary>
    /// 【核心】テスト表 #21。M0 の定義でパイプライン(順1・順2・順5)を1日進める →
    /// 全売り手の <c>Market</c> の値が <c>CostFloor(UnitCost(...))</c> と一致。期待値は定義から計算する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>変異の実測1(2026-09-16)。</b><c>TradeSystem.Step</c> の
    /// <c>household.PurchaseUnitCostAverage</c> を渡す箇所を <c>new int[Item.Count]</c>(取得原価を
    /// 読まない、初日の原価が0に固定される経路)に変える変異を当てたところ、
    /// <c>Assert.Equal(expected, actual)</c> が全売り手で失敗した(期待値は非0の原価下限、
    /// 実際値は0。赤を確認)。変異を戻して緑に復帰させた。
    /// </para>
    /// <para>
    /// <b>変異の実測2(2026-09-16、レビュー1巡目指摘2)。</b><c>TradeSystem.Step</c> の
    /// <c>if (sellableStock &lt;= 0)</c> を <c>if (sellableStock &lt;= 0 || true)</c>
    /// (全世帯が空振りする経路)に変えたところ、<c>Assert.NotEmpty(world.Market)</c> が
    /// 「Collection was empty」で失敗した(赤を確認)。この行を足す前は、下のループが
    /// 「エントリが無い」だけを確認して素通りするため緑のままだった。変異を戻して緑に復帰させた。
    /// </para>
    /// </remarks>
    [Fact]
    public void FirstDayOffersAreExactlyTheCostFloor()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));

        var scheduler = new SimScheduler(
            new ISimSystem[]
            {
                new ProductionSystem(definition),
                new ConsumptionSystem(definition),
                new TradeSystem(definition),
            },
            new RandomSource(1));
        scheduler.Advance(world, ticks: 24);

        // レビュー1巡目指摘2: 空振り(全売り手が販売在庫0)で緑になるのを防ぐ。M0のinitialWorkshopInputDays
        // (現在5、#28が動かす前提)が0になると下のループが値付けを一度も検証せず素通りする。
        Assert.NotEmpty(world.Market);

        foreach (var household in world.Households)
        {
            var recipe = definition.Recipes[(int)household.Occupation];
            int outputItemId = recipe.Outputs[0].ItemId;
            int sellableStock = household.WorkshopInventory[outputItemId];
            var key = new MarketKey(outputItemId, household.Id);

            if (sellableStock <= 0)
            {
                Assert.False(world.Market.ContainsKey(key));
                continue;
            }

            int unitCost = OfferPrice.UnitCost(recipe, household.PurchaseUnitCostAverage);
            int expected = OfferPrice.CostFloor(unitCost, definition.MinimumMarginPermille, household.IsBankrupt);

            Assert.True(world.Market.TryGetValue(key, out int actual));
            Assert.Equal(expected, actual);
        }
    }

    /// <summary>
    /// 【核心】テスト表 #22。1日目で価格が付いた後、世帯主の <c>Knowledge</c> に他の売り手の観測を
    /// 1件仕込んで2日目を進める → 相場基準が CeilDiv(観測 + 自分の前日価格, 2) になっている。
    /// </summary>
    /// <remarks>
    /// <b>在庫比を出荷目標にちょうど揃え(係数1000‰)、原価下限を低く抑えることで、最終提示価格が
    /// 相場基準そのものになるように仕立てる</b>(そうしないと <c>max</c> の第1項に隠れて判別できない)。
    /// <para>
    /// <b>変異の実測(2026-09-16)。</b><c>TradeSystem.Step</c> の手順を「<c>world.Market.Clear()</c>を
    /// 先に呼んでから1件ずつ計算して書く」形(TDD01 §3.2が要求する一括書き込みの崩れ)に変える変異を
    /// 当てたところ、2日目の <c>Assert.Equal(101, ...)</c> が実際値200(自分の前日価格2を失い、
    /// 観測200のみの平均になった)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </para>
    /// </remarks>
    [Fact]
    public void OfferReadsYesterdaysOwnPriceNotAClearedMarket()
    {
        const int ShipmentTarget = 5;
        const int SellableStock = 5; // 在庫比 = 1000‰(目標どおり) → 価格係数1000‰

        var definition = BuildDefinition(ShipmentTarget, minimumMarginPermille: 0);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Apprentice });
        world.Households[0].WorkshopInventory[Item.Flour] = SellableStock;
        world.Households[0].PurchaseUnitCostAverage[Item.Grain] = 1; // 原価 = CeilDiv(1×2,1) = 2

        var system = new TradeSystem(definition);

        // 1日目: 観測が無いので原価下限(2)がそのまま提示価格になり、Marketに書かれる。
        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        var key = new MarketKey(Item.Flour, world.Households[0].Id);
        Assert.Equal(2, world.Market[key]);

        // 2日目の前に、世帯主(HeadNpcId)のKnowledgeへ他の売り手の観測(価格200・1日目)を仕込む。
        const int OtherSellerId = 999;
        world.Knowledge[world.Households[0].HeadNpcId].Add(
            Observation(Item.Flour, OtherSellerId, price: 200, observedAt: Tick.Zero));

        EconomySystemTestFixtures.RunDays(world, system, days: 1);

        // 相場基準 = CeilDiv(200 + 2, 2) = 101。係数1000‰なのでApplyPermille後もそのまま101。
        // 原価下限2 < 101 なのでmaxは相場基準側を採る。
        Assert.Equal(101, world.Market[key]);
    }

    /// <summary>
    /// 【核心】テスト表 #23。同じ品目を世帯在庫に積んでも提示価格が変わらない。
    /// パン屋(ここではMiller)の工房在庫にある入力(穀物)に売り注文が立たない。
    /// </summary>
    /// <remarks>
    /// <b>相場基準が立つ(<c>hasReference == true</c>)場面で比較する。</b>観測が無いと
    /// <c>TradeSystem.Step</c> は常に <c>costFloor</c> をそのまま使い、<c>OfferPrice.Calculate</c>
    /// (在庫比‰の計算経路)を一度も通らないため、<c>sellableStock</c> に世帯在庫を足す変異を
    /// 混入しても判別できない。#22 と同じ2日パターンで相場基準を立てて比較する。
    /// <para>
    /// <b>変異の実測1(2026-09-16)。</b><c>TradeSystem.Step</c> の <c>sellableStock</c> の取得を
    /// <c>household.WorkshopInventory[outputItemId] + household.HouseholdInventory[outputItemId]</c>
    /// (世帯在庫を足す)に変える変異を当てたところ、世帯在庫100を積んだケースの2日目の提示価格が
    /// 51(在庫比105/5=21000‰→係数clamp下限500‰→ApplyPermille(101,500)=51)になり、
    /// 積まない場合の101と食い違って <c>Assert.Equal</c> が失敗した(赤を確認)。
    /// 変異を戻して緑に復帰させた。
    /// </para>
    /// <para>
    /// <b>絶対値の固定について(レビュー2巡目指摘)。</b>2回の実行の相等だけで判定すると、
    /// 相場基準が立たなくなる変異(両者とも <c>costFloor</c> に落ちて一致してしまう)を見逃す
    /// ため、<c>Assert.Equal(101, ...)</c> を足した。
    /// </para>
    /// <para>
    /// <b>変異の実測2(2026-09-16)。</b><c>OfferPrice.TryMarketReference</c> の冒頭で常に
    /// <c>marketReference = 0; return false;</c>(相場基準が常に立たない変異)を当てたところ、
    /// <c>Assert.Equal(101, priceWithoutHouseholdStock)</c> が実際値2(<c>costFloor</c>)で
    /// 失敗した(赤を確認)。絶対値の固定を足す前は、両ケースとも2で一致し
    /// <c>Assert.Equal(priceWithoutHouseholdStock, priceWithHouseholdStock)</c> だけでは
    /// この変異を判別できなかった。変異を戻して緑に復帰させた。
    /// </para>
    /// </remarks>
    [Fact]
    public void SellableStockIsOnlyTheWorkshopOutputInventory()
    {
        const int ShipmentTarget = 5;
        const int OtherSellerId = 999;
        var definition = BuildDefinition(ShipmentTarget, minimumMarginPermille: 0);

        int RunAndGetPrice(int householdInventoryFlour)
        {
            var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
            world.Households[0].WorkshopInventory[Item.Flour] = 5;
            world.Households[0].HouseholdInventory[Item.Flour] = householdInventoryFlour;
            world.Households[0].WorkshopInventory[Item.Grain] = 50; // 入力(穀物)を工房在庫に積む
            world.Households[0].PurchaseUnitCostAverage[Item.Grain] = 1; // 原価2

            var system = new TradeSystem(definition);
            EconomySystemTestFixtures.RunDays(world, system, days: 1); // 1日目: 原価下限2で出品

            // 入力(穀物)には売り注文が立たない。
            Assert.False(world.Market.ContainsKey(new MarketKey(Item.Grain, world.Households[0].Id)));

            world.Knowledge[world.Households[0].HeadNpcId].Add(
                Observation(Item.Flour, OtherSellerId, price: 200, observedAt: Tick.Zero));

            EconomySystemTestFixtures.RunDays(world, system, days: 1); // 2日目: 相場基準が立つ

            return world.Market[new MarketKey(Item.Flour, world.Households[0].Id)];
        }

        int priceWithoutHouseholdStock = RunAndGetPrice(householdInventoryFlour: 0);
        int priceWithHouseholdStock = RunAndGetPrice(householdInventoryFlour: 100);

        // 絶対値も固定する(相場基準101、#22と同じ設定・同じ計算)。2回の実行の相等だけで
        // 判定すると、相場基準が立たなくなる変異(両者ともcostFloor=2に落ちて一致する)を
        // 見逃す(レビュー2巡目指摘)。
        Assert.Equal(101, priceWithoutHouseholdStock);
        Assert.Equal(priceWithoutHouseholdStock, priceWithHouseholdStock);
    }

    /// <summary>テスト表 #24。出力在庫0(入力切れで生産停止)の売り手の MarketKey が Market に無い。</summary>
    [Fact]
    public void NoOfferIsPostedWhenSellableStockIsZero()
    {
        var definition = BuildDefinition(shipmentTarget: 5);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Flour] = 0;

        EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1);

        Assert.False(world.Market.ContainsKey(new MarketKey(Item.Flour, world.Households[0].Id)));
    }

    /// <summary>テスト表 #25。前日出品した売り手の在庫を0にして1日進めると、エントリが消える。</summary>
    [Fact]
    public void StaleOfferIsRemovedWhenStockRunsOut()
    {
        var definition = BuildDefinition(shipmentTarget: 5);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Flour] = 5;
        world.Households[0].PurchaseUnitCostAverage[Item.Grain] = 1;

        var system = new TradeSystem(definition);
        var key = new MarketKey(Item.Flour, world.Households[0].Id);

        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.True(world.Market.ContainsKey(key));

        world.Households[0].WorkshopInventory[Item.Flour] = 0;
        EconomySystemTestFixtures.RunDays(world, system, days: 1);

        Assert.False(world.Market.ContainsKey(key));
    }

    /// <summary>
    /// <see cref="MarketReferenceComesFromTheHeadNpcOnly"/> 専用の世帯1戸の世界。
    /// <b>世帯主の NpcId(2) を世帯 Id(0) とわざと違える。</b>
    /// <see cref="EconomySystemTestFixtures.BuildWorldWithOneHousehold"/> は <c>headNpcId == 0 ==
    /// household.Id</c> に固定されており、それだと「<c>HeadNpcId</c> ではなく世帯 Id で
    /// <c>Knowledge</c> を引く」実装ミス(TDD01 §3.2「取り違えを型で防げない」経路)が
    /// 偶然一致して判別できない。
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
    /// 【核心】テスト表 #26。徒弟の <c>Knowledge</c> にだけ観測を置く → 相場基準が立たない
    /// (原価下限のまま)。同じ観測を世帯主に置くと立つ。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>TradeSystem.Step</c> の
    /// <c>world.Knowledge[household.HeadNpcId]</c> を <c>world.Knowledge[household.Id]</c>
    /// (世帯Idで誤って引く)に変える変異を当てたところ、世帯主(NpcId=2)に観測を置いたケースの
    /// <c>Assert.Equal(101, ...)</c> が実際値2(誤って<c>Knowledge[0]</c>=空を読み、相場基準が
    /// 立たず原価下限のまま)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void MarketReferenceComesFromTheHeadNpcOnly()
    {
        const int ShipmentTarget = 5;
        const int SellableStock = 5;
        const int OtherSellerId = 999;
        const int HeadNpcId = 2;
        const int ApprenticeNpcId = 3;

        int RunSecondDayPrice(int observerNpcId)
        {
            var definition = BuildDefinition(ShipmentTarget, minimumMarginPermille: 0);
            var world = BuildWorldWithHeadNpcIdDifferentFromHouseholdId();
            world.Households[0].WorkshopInventory[Item.Flour] = SellableStock;
            world.Households[0].PurchaseUnitCostAverage[Item.Grain] = 1; // 原価2

            var system = new TradeSystem(definition);
            EconomySystemTestFixtures.RunDays(world, system, days: 1); // 1日目: 原価下限2で出品

            world.Knowledge[observerNpcId].Add(
                Observation(Item.Flour, OtherSellerId, price: 200, observedAt: Tick.Zero));

            EconomySystemTestFixtures.RunDays(world, system, days: 1);

            return world.Market[new MarketKey(Item.Flour, world.Households[0].Id)];
        }

        const int CostFloor = 2;

        // 徒弟のKnowledgeにだけ観測 → 相場基準が立たず、原価下限のまま。
        Assert.Equal(CostFloor, RunSecondDayPrice(ApprenticeNpcId));

        // 同じ観測を世帯主のKnowledgeに置くと相場基準が立つ(101、#22と同じ計算)。
        Assert.Equal(101, RunSecondDayPrice(HeadNpcId));
    }

    /// <summary>
    /// 【核心】レビュー1巡目指摘1。<c>TradeSystem.Step</c> が <c>OfferPrice.TryMarketReference</c> の
    /// <c>selfHouseholdId</c> に渡すのは <c>household.Id</c> であって <c>household.HeadNpcId</c> ではない
    /// ことを、パイプライン側で押さえる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>世帯主のNpcId(2)と世帯Id(0)を意図的に違えた世界</b>
    /// (<see cref="BuildWorldWithHeadNpcIdDifferentFromHouseholdId"/>)を使い、世帯主の
    /// <c>Knowledge</c> へ「<c>SellerId</c> = 自世帯Id(0)」の観測だけを置く。他の #21〜#29 は
    /// 観測の <c>SellerId</c> が999固定・自世帯Idが0固定のため、<c>selfHouseholdId</c> に
    /// <c>household.HeadNpcId</c>(2)を渡す変異が混入しても
    /// <c>SellerId(999) != HeadNpcId(2)</c> で偶然除外が効いてしまい、どのテストも判別できない
    /// (TDD01 §3.2「NpcId と世帯 Id の取り違えは型で防げない」経路)。
    /// </para>
    /// <para>
    /// <b>変異の実測(2026-09-16)。</b><c>TradeSystem.Step</c> の
    /// <c>OfferPrice.TryMarketReference</c> 呼び出しの3引数目(<c>selfHouseholdId</c>)を
    /// <c>household.Id</c> から <c>household.HeadNpcId</c> に変える変異を当てたところ、
    /// <c>Assert.Equal(2, ...)</c> が実際値151(<c>SellerId=0</c> の自己観測300が
    /// <c>selfHouseholdId=2</c> と一致せず除外されず、相場基準 = CeilDiv(300+2,2)=151 が
    /// 立ってしまった)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </para>
    /// </remarks>
    [Fact]
    public void MarketReferenceExcludesTheHouseholdsOwnObservationEvenWhenHeadNpcIdDiffers()
    {
        const int ShipmentTarget = 5;
        const int SellableStock = 5;
        const int SelfHouseholdId = 0; // BuildWorldWithHeadNpcIdDifferentFromHouseholdIdの世帯Id
        const int HeadNpcId = 2;

        var definition = BuildDefinition(ShipmentTarget, minimumMarginPermille: 0);
        var world = BuildWorldWithHeadNpcIdDifferentFromHouseholdId();
        world.Households[0].WorkshopInventory[Item.Flour] = SellableStock;
        world.Households[0].PurchaseUnitCostAverage[Item.Grain] = 1; // 原価2

        var system = new TradeSystem(definition);
        EconomySystemTestFixtures.RunDays(world, system, days: 1); // 1日目: 原価下限2で出品

        // 自世帯(Id=0)の観測だけを世帯主のKnowledgeへ置く。SellerId(0)はhousehold.Idと一致し、
        // household.HeadNpcId(2)とは一致しない。
        world.Knowledge[HeadNpcId].Add(
            Observation(Item.Flour, sellerId: SelfHouseholdId, price: 300, observedAt: Tick.Zero));

        EconomySystemTestFixtures.RunDays(world, system, days: 1);

        var key = new MarketKey(Item.Flour, world.Households[0].Id);

        // 自世帯の観測しかないので相場基準が立たず、原価下限(2)のまま。
        Assert.Equal(2, world.Market[key]);
    }

    /// <summary>テスト表 #27。IsBankrupt = 1 を直接立てて1日進める → 提示価格の下限が500‰になっている。</summary>
    [Fact]
    public void BankruptSellerPostsTheHalvedFloorInThePipeline()
    {
        var definition = BuildDefinition(shipmentTarget: 5, minimumMarginPermille: 200);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Flour] = 5;
        world.Households[0].PurchaseUnitCostAverage[Item.Grain] = 1; // 原価2
        world.Households[0].IsBankrupt = 1;

        EconomySystemTestFixtures.RunDays(world, new TradeSystem(definition), days: 1);

        // 通常時: ApplyPermille(2, 1200) = 3。破産時: ApplyPermille(2, 500) = 1。
        int price = world.Market[new MarketKey(Item.Flour, world.Households[0].Id)];
        Assert.Equal(OfferPrice.CostFloor(unitCost: 2, minimumMarginPermille: 200, isBankrupt: 1), price);
        Assert.NotEqual(OfferPrice.CostFloor(unitCost: 2, minimumMarginPermille: 200, isBankrupt: 0), price);
    }

    /// <summary>テスト表 #28。入力0件 / 出力2件を含む定義でコンストラクタが NotSupportedException。</summary>
    [Fact]
    public void TradeSystemRejectsUnsupportedRecipesAtConstruction()
    {
        var noInputRecipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 1000);

        var noInputDefinition = EconomySystemTestFixtures.BuildDefinition(noInputRecipe);
        Assert.Throws<NotSupportedException>(() => new TradeSystem(noInputDefinition));

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
    /// テスト表 #29。マスターシードだけを変えた2つの <c>RandomSource</c> で同じ世界を1日進め、
    /// 状態ハッシュが一致する。
    /// </summary>
    [Fact]
    public void TradeDrawsNoRandomNumbers()
    {
        var definition = BuildDefinition(shipmentTarget: 5, minimumMarginPermille: 200);

        ulong RunWithSeed(long seed)
        {
            var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
                new[] { NpcRank.Master, NpcRank.Apprentice });
            world.Households[0].WorkshopInventory[Item.Flour] = 5;
            world.Households[0].PurchaseUnitCostAverage[Item.Grain] = 1;
            world.Knowledge[world.Households[0].HeadNpcId].Add(
                Observation(Item.Flour, sellerId: 999, price: 200, observedAt: Tick.Zero));

            var scheduler = new SimScheduler(
                new ISimSystem[] { new TradeSystem(definition) }, new RandomSource(seed));
            scheduler.Advance(world, ticks: 24);

            return StateHasher.Compute(world);
        }

        Assert.Equal(RunWithSeed(1), RunWithSeed(999999));
    }
}
