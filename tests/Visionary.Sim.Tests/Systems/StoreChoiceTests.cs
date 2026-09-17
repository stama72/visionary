using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="StoreChoice"/>(GDD06 §2・§3・§3.1、#37 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class StoreChoiceTests
{
    private const int ItemA = 0;

    /// <summary>
    /// 単一構成員(<c>headNpcId == npcId == 0</c>)の世帯を、渡した区画Idの並びで作る。
    /// 添字0が買い手、以降が売り手という前提でテストが組む。
    /// </summary>
    private static World BuildWorld(params int[] districtIds)
    {
        int count = districtIds.Length;
        var world = new World(npcCount: count, householdCount: count, itemCount: Item.Count);

        for (int id = 0; id < count; id++)
        {
            world.Npcs[id].Rank = NpcRank.Master;
            world.Households[id] = new HouseholdState(
                id: id, districtId: districtIds[id], headNpcId: id,
                memberNpcIds: new[] { id }, itemCount: Item.Count);
            world.Households[id].Occupation = Occupation.Miller;
        }

        return world;
    }

    private static PriceObservation Observation(int itemId, int sellerId, Tick observedAt) =>
        new()
        {
            ItemId = itemId,
            LocationId = 0,
            Price = 0,
            SellerId = sellerId,
            ObservedAt = observedAt,
            Source = ObservationSource.Direct,
        };

    /// <summary>テスト表 #4。区画0→区画8(距離4)・1時間/区画 → 8。同一区画 → 0。</summary>
    [Fact]
    public void TravelHoursCountsTheRoundTrip()
    {
        Assert.Equal(8, StoreChoice.TravelHours(fromDistrictId: 0, toDistrictId: 8, hoursPerDistrict: 1));
        Assert.Equal(0, StoreChoice.TravelHours(fromDistrictId: 3, toDistrictId: 3, hoursPerDistrict: 1));
    }

    /// <summary>
    /// 【核心】テスト表 #5。移動時間8・機会費用3・目標在庫5 → CeilDiv(24,5) = 5。目標在庫0 → 24。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-17)。</b><c>StoreChoice.TravelCostPerUnit</c> の
    /// <c>Math.Max(targetStockInUnits, 1)</c> を外し <c>targetStockInUnits</c> をそのまま
    /// 除数に渡す変異を当てたところ、目標在庫0のケースで <c>DivideByZeroException</c>
    /// (<c>Assert.Equal(24, ...)</c> が例外で失敗)を確認した(赤を確認)。変異を戻して
    /// 緑に復帰させた。
    /// </remarks>
    [Fact]
    public void TravelCostIsDividedByTheTargetStock()
    {
        Assert.Equal(
            5, StoreChoice.TravelCostPerUnit(travelHours: 8, opportunityCost: 3, targetStockInUnits: 5));
        Assert.Equal(
            24, StoreChoice.TravelCostPerUnit(travelHours: 8, opportunityCost: 3, targetStockInUnits: 0));
    }

    /// <summary>
    /// 【核心】テスト表 #6。<see cref="TradeSystem.TargetStockInUnits"/> /
    /// <see cref="TradeSystem.PurchaseQuantityInUnits"/> ヘルパーの<b>内側の用途分岐</b>だけを
    /// 直接呼んで確かめる(レビュー2巡目の訂正)。<c>Durable</c> は N で割り、<c>Necessity</c> は
    /// 換算しない。手順1・手順5 のどちらも同じ形の分岐を持つので両方見る。
    /// <b>段5(<see cref="TradeSystem.Step"/>)がこのヘルパーを実際に呼んでいることまでは
    /// 確かめない</b>(下記remark)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-17)。</b>
    /// <c>TradeSystem.TargetStockInUnits</c> / <c>TradeSystem.PurchaseQuantityInUnits</c> の
    /// 分岐(<c>purpose == DemandPurpose.Durable ? CeilDiv(...) : そのまま</c>)を
    /// <c>常に CeilDiv(...) を返す</c>(用途を見ずに常に換算する)へ変えたところ、
    /// <c>Necessity</c> の行(<c>Assert.Equal(15, TargetStockInUnits(Necessity, 15, 30))</c>・
    /// <c>Assert.Equal(15, PurchaseQuantityInUnits(Necessity, 15, 30))</c>)が実際値1で失敗した
    /// (赤を確認:必需の移動費が1/Nになる経路)。次に<c>常に targetStock/quantity をそのまま返す</c>
    /// (換算しない)へ変えたところ、<c>Durable</c> の行(期待値1)が実際値15で失敗した
    /// (赤を確認:耐久の移動費が空間の摩擦を持たなくなる経路)。いずれも変異を戻して緑に復帰させた。
    /// </remarks>
    /// <remarks>
    /// <b>呼び出し側がヘルパーを通ることは、このテストを含めどのテストも守っていない</b>
    /// (レビュー2巡目の実測)。<c>TradeSystem.RunOneHouseholdsShopping</c> の段5 手順1・手順5 から
    /// <c>TargetStockInUnits</c> / <c>PurchaseQuantityInUnits</c> の呼び出しを外し、
    /// <c>int targetInUnits = line.TargetStock;</c> / <c>int purchaseQuantityInUnits = purchaseQuantity;</c>
    /// (換算せず耐久値をそのまま個数として扱う)へ置き換えたところ、単体・統合を合わせた全317件が
    /// 緑のままだった。<b>理由は W2 では耐久の約定が構造的に起きないから</b>(タスク仕様
    /// 「耐久(工具)の約定は本タスクでは検証しない」節)。1次産品を誰も売っていないため生産は
    /// 初期入力5日分で止まり、摩耗も5回で止まる。耐久の需要が立つには摩耗15回が要るので、
    /// 換算漏れが観測できる
    /// 挙動を一切変えない。<b>この穴はW2では原理的に塞げない</b> ── #38 で耐久の約定が起きるように
    /// なって初めて、呼び出し側がヘルパーを通っていることを踏める検出器を足せる(issue #37の
    /// 「先のタスクへ」#38 の項に引き取り条項がある)。
    /// </remarks>
    [Fact]
    public void DurableTravelCostConvertsTheTargetStockToUnits()
    {
        const int TargetStockInDurabilityUnits = 15; // 耐久値
        const int PurchaseQuantityInDurabilityUnits = 15; // 耐久値
        const int ProductionRunsPerToolWear = 30;    // N

        // 手順1(目標在庫 → 個数)。
        Assert.Equal(
            1, TradeSystem.TargetStockInUnits(
                DemandPurpose.Durable, TargetStockInDurabilityUnits, ProductionRunsPerToolWear));
        Assert.Equal(
            15, TradeSystem.TargetStockInUnits(
                DemandPurpose.Necessity, TargetStockInDurabilityUnits, ProductionRunsPerToolWear));

        // 手順5(購入量 → 個数)。
        Assert.Equal(
            1, TradeSystem.PurchaseQuantityInUnits(
                DemandPurpose.Durable, PurchaseQuantityInDurabilityUnits, ProductionRunsPerToolWear));
        Assert.Equal(
            15, TradeSystem.PurchaseQuantityInUnits(
                DemandPurpose.Necessity, PurchaseQuantityInDurabilityUnits, ProductionRunsPerToolWear));
    }

    /// <summary>
    /// 【核心】テスト表 #10。近い店(距離0・提示価格30)と遠い店(距離2・提示価格26、移動費5)→ 近い店。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-17)。</b><c>StoreChoice.TrySelect</c> の <c>realCost</c> の比較を
    /// <c>price</c>(提示価格そのもの)の比較に変える変異を当てたところ、
    /// <c>Assert.Equal(NearSellerId, selected.SellerId)</c> が実際値2(遠い店の提示価格26が
    /// 近い店の30より安いため選ばれた。GDD06 §3「遠い店は不利」が丸ごと消える)で失敗した
    /// (赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void SelectsTheCheapestRealCostNotTheCheapestPrice()
    {
        const int NearSellerId = 1;
        const int FarSellerId = 2;

        // 買い手(0)・近い売り手(1、距離0)・遠い売り手(2、距離2)。
        var world = BuildWorld(0, 0, 2);
        var definition = EconomySystemTestFixtures.BuildDefinition(
            new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
                inputs: new[] { new ItemQuantity { ItemId = Item.Grain, Quantity = 1 } },
                laborPermille: 1000),
            travelHoursPerDistrict: 1);

        world.Households[NearSellerId].WorkshopInventory[ItemA] = 10;
        world.Households[FarSellerId].WorkshopInventory[ItemA] = 10;

        world.Market[new MarketKey(ItemA, NearSellerId)] = 30;
        world.Market[new MarketKey(ItemA, FarSellerId)] = 26;

        // 遠い店(距離2)は「今日の知覚」の外なので、前日の観測で知る。
        EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 24);
        world.Knowledge[0].Add(Observation(ItemA, FarSellerId, Tick.Zero));

        var storeChoice = new StoreChoice(definition);

        // 移動時間 = TravelHours(0,2,1) = 4。移動費5 = CeilDiv(4×5,4)。
        bool found = storeChoice.TrySelect(
            world, world.Households[0], ItemA,
            targetStockInUnits: 4, errandOpportunityCost: 5, out var selected);

        Assert.True(found);
        Assert.Equal(NearSellerId, selected.SellerId);
        Assert.Equal(30, selected.UnitRealCost);
        Assert.Equal(30, selected.UnitEffectivePrice);
    }

    /// <summary>テスト表 #11。実質コストが同値の売り手2件 → Id の小さい方。</summary>
    /// <remarks>
    /// <c>&lt;=</c> で更新すると Id 最大が残る(ADR-0002 違反)。#38 の都市外市場が同値のとき
    /// 都市内より優先されるべきところ、逆順で残ると都市外が先に選ばれてしまう。
    /// </remarks>
    [Fact]
    public void SelectsTheSmallestSellerIdOnTies()
    {
        var world = BuildWorld(0, 0, 0); // 買い手(0)・売り手(1,2)、いずれも同区画(距離0)。

        world.Households[1].WorkshopInventory[ItemA] = 10;
        world.Households[2].WorkshopInventory[ItemA] = 10;

        world.Market[new MarketKey(ItemA, 1)] = 50;
        world.Market[new MarketKey(ItemA, 2)] = 50;

        var definition = EconomySystemTestFixtures.BuildDefinition(
            new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
                inputs: new[] { new ItemQuantity { ItemId = Item.Grain, Quantity = 1 } },
                laborPermille: 1000));

        var storeChoice = new StoreChoice(definition);

        bool found = storeChoice.TrySelect(
            world, world.Households[0], ItemA,
            targetStockInUnits: 1, errandOpportunityCost: 5, out var selected);

        Assert.True(found);
        Assert.Equal(1, selected.SellerId);
    }

    /// <summary>
    /// 【核心】テスト表 #12。距離2の売り手のみ → 候補0件。前日の観測を1件置くと候補に入る。
    /// 当日(差0)の観測では入らない。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-17)。</b><c>StoreChoice.HasValidMemory</c> の
    /// <c>now.DayIndex - observation.ObservedAt.DayIndex &gt;= 1</c> を <c>&gt;= 0</c> に
    /// 変える変異(当日の観測も有効にする)を当てたところ、<c>Assert.False(sameDay)</c> が
    /// 実際値trueで失敗した(赤を確認、GDD06 §3.1の日内の相互参照が戻る経路)。
    /// また 4b の判定を丸ごと削る変異(距離だけで判定する)を当てたところ、
    /// <c>Assert.True(previousDay)</c> が実際値falseで失敗した(赤を確認、知識が永久に
    /// 自宅のR以内に閉じる経路)。いずれも変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void SeesDistantStoresOnlyThroughYesterdaysObservation()
    {
        const int SellerId = 1;

        var definition = EconomySystemTestFixtures.BuildDefinition(
            new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
                inputs: new[] { new ItemQuantity { ItemId = Item.Grain, Quantity = 1 } },
                laborPermille: 1000));
        var storeChoice = new StoreChoice(definition);

        World BuildKnownWorld()
        {
            var w = BuildWorld(0, 2); // 買い手(0)・売り手(1、距離2)。
            w.Households[SellerId].WorkshopInventory[ItemA] = 10;
            w.Market[new MarketKey(ItemA, SellerId)] = 10;

            return w;
        }

        bool TrySelectIn(World w) => storeChoice.TrySelect(
            w, w.Households[0], ItemA, targetStockInUnits: 1, errandOpportunityCost: 1, out _);

        // 観測なし → 候補0件。
        var withoutObservation = BuildKnownWorld();
        Assert.False(TrySelectIn(withoutObservation));

        // 前日の観測 → 候補に入る。
        var withPreviousDayObservation = BuildKnownWorld();
        EconomySystemTestFixtures.AdvanceClockOnly(withPreviousDayObservation, ticks: 24);
        withPreviousDayObservation.Knowledge[0].Add(Observation(ItemA, SellerId, Tick.Zero));
        Assert.True(TrySelectIn(withPreviousDayObservation));

        // 当日(差0)の観測 → 候補に入らない。
        var withSameDayObservation = BuildKnownWorld();
        EconomySystemTestFixtures.AdvanceClockOnly(withSameDayObservation, ticks: 24);
        withSameDayObservation.Knowledge[0].Add(Observation(ItemA, SellerId, withSameDayObservation.Now));
        Assert.False(TrySelectIn(withSameDayObservation));
    }

    /// <summary>
    /// テスト表 #13。<c>Market</c> にエントリはあるが在庫0の売り手は候補に入らない。
    /// 距離0(在庫0)と距離2(観測あり・在庫あり)なら後者が選ばれる。
    /// </summary>
    [Fact]
    public void IgnoresSellersWithoutSellableStock()
    {
        const int EmptySellerId = 1;
        const int StockedSellerId = 2;

        var world = BuildWorld(0, 0, 2); // 買い手(0)・在庫0の売り手(1、距離0)・在庫ありの売り手(2、距離2)。
        world.Households[EmptySellerId].WorkshopInventory[ItemA] = 0;
        world.Households[StockedSellerId].WorkshopInventory[ItemA] = 10;

        world.Market[new MarketKey(ItemA, EmptySellerId)] = 10;
        world.Market[new MarketKey(ItemA, StockedSellerId)] = 999; // 高くても在庫がある方しか選べない

        EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 24);
        world.Knowledge[0].Add(Observation(ItemA, StockedSellerId, Tick.Zero));

        var definition = EconomySystemTestFixtures.BuildDefinition(
            new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
                inputs: new[] { new ItemQuantity { ItemId = Item.Grain, Quantity = 1 } },
                laborPermille: 1000));
        var storeChoice = new StoreChoice(definition);

        bool found = storeChoice.TrySelect(
            world, world.Households[0], ItemA,
            targetStockInUnits: 1, errandOpportunityCost: 1, out var selected);

        Assert.True(found);
        Assert.Equal(StockedSellerId, selected.SellerId);
    }

    /// <summary>テスト表 #14。自分が売っている品目について、自分は候補に入らない。</summary>
    [Fact]
    public void IgnoresOwnOffer()
    {
        var world = BuildWorld(0); // 買い手=売り手(自分自身のみ)。
        world.Households[0].WorkshopInventory[ItemA] = 10;
        world.Market[new MarketKey(ItemA, 0)] = 10;

        var definition = EconomySystemTestFixtures.BuildDefinition(
            new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
                inputs: new[] { new ItemQuantity { ItemId = Item.Grain, Quantity = 1 } },
                laborPermille: 1000));
        var storeChoice = new StoreChoice(definition);

        bool found = storeChoice.TrySelect(
            world, world.Households[0], ItemA,
            targetStockInUnits: 1, errandOpportunityCost: 1, out _);

        Assert.False(found);
    }

    /// <summary>
    /// テスト表 #15。観測を世帯主以外の構成員にだけ置く → 候補に入らない。世帯主に置くと入る。
    /// </summary>
    /// <remarks>
    /// <b>買い手世帯は <c>Id(=1) != HeadNpcId(=2)</c> にする</b>(レビュー1巡目 I-b の訂正)。
    /// <c>Id == HeadNpcId</c> の世帯では <c>Knowledge[household.Id]</c> と
    /// <c>Knowledge[household.HeadNpcId]</c> が同じ添字を指すため、実装ミス
    /// (#36引き継ぎ表Bと同型)を当てても両方の assert が偶然通ってしまう。<c>household.Id(1)</c>
    /// を Knowledge の添字にすると届く先は誰の観測も置かない空の配列要素になるので、
    /// 世帯主(NpcId=2)にしか観測を置かないこのテストで実際に違いが出る。
    /// </remarks>
    [Fact]
    public void UsesTheHeadObservationsForMemory()
    {
        const int BuyerHouseholdId = 1;
        const int HeadNpcId = 2;
        const int NonHeadNpcId = 3;
        const int SellerId = 0;

        var world = new World(npcCount: 4, householdCount: 2, itemCount: Item.Count);
        world.Npcs[0].Rank = NpcRank.Master; // 売り手世帯主
        world.Npcs[HeadNpcId].Rank = NpcRank.Master;
        world.Npcs[NonHeadNpcId].Rank = NpcRank.Apprentice;

        world.Households[SellerId] = new HouseholdState(
            id: SellerId, districtId: 2, headNpcId: 0, memberNpcIds: new[] { 0 }, itemCount: Item.Count);
        world.Households[SellerId].Occupation = Occupation.Miller;
        world.Households[SellerId].WorkshopInventory[ItemA] = 10;

        world.Households[BuyerHouseholdId] = new HouseholdState(
            id: BuyerHouseholdId, districtId: 0, headNpcId: HeadNpcId,
            memberNpcIds: new[] { HeadNpcId, NonHeadNpcId }, itemCount: Item.Count);
        world.Households[BuyerHouseholdId].Occupation = Occupation.Miller;

        world.Market[new MarketKey(ItemA, SellerId)] = 10;

        EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 24);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
                inputs: new[] { new ItemQuantity { ItemId = Item.Grain, Quantity = 1 } },
                laborPermille: 1000));
        var storeChoice = new StoreChoice(definition);

        // 非世帯主(徒弟)にだけ観測を置く → 候補に入らない。
        world.Knowledge[NonHeadNpcId].Add(Observation(ItemA, SellerId, Tick.Zero));

        bool foundWithNonHeadOnly = storeChoice.TrySelect(
            world, world.Households[BuyerHouseholdId], ItemA,
            targetStockInUnits: 1, errandOpportunityCost: 1, out _);
        Assert.False(foundWithNonHeadOnly);

        // 同じ観測を世帯主に置くと候補に入る。
        world.Knowledge[HeadNpcId].Add(Observation(ItemA, SellerId, Tick.Zero));

        bool foundWithHead = storeChoice.TrySelect(
            world, world.Households[BuyerHouseholdId], ItemA,
            targetStockInUnits: 1, errandOpportunityCost: 1, out var selected);
        Assert.True(foundWithHead);
        Assert.Equal(SellerId, selected.SellerId);
    }
}
