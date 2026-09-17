using Visionary.Sim.Numerics;
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
    /// 【核心】テスト表 #6。工具の目標在庫15(耐久値)・N=30・移動時間8・機会費用3 →
    /// 個数換算は CeilDiv(15,30)=1 なので 24。耐久値のまま(15)割ると 2。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-17)。</b><c>TradeSystem.RunOneHouseholdsShopping</c> の
    /// <c>targetInUnits</c> の算出(<c>line.Purpose == DemandPurpose.Durable ?
    /// CeilDiv(line.TargetStock, N) : line.TargetStock</c>)を、耐久でも変換せず
    /// <c>line.TargetStock</c> をそのまま渡す変異に変えたところ、目標在庫15(未変換)を
    /// <c>TravelCostPerUnit</c> に渡す経路が有効になり、結果が 2(移動費が 1/N になる)に
    /// なった。<c>Assert.Equal(24, converted)</c> は変換後の値(1)を渡した結果であり、
    /// この2つの数値差(24 と 2)がそのまま「個数へ直さずに <c>line.TargetStock</c> を渡す」
    /// 変異の効果を表している(赤を確認:変換を怠ると 24 のかわりに 2 が出る)。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void DurableTravelCostConvertsTheTargetStockToUnits()
    {
        const int TargetStockInDurabilityUnits = 15; // 耐久値
        const int ProductionRunsPerToolWear = 30;    // N
        const int TravelHours = 8;
        const int ErrandOpportunityCost = 3;

        int convertedTargetInUnits =
            IntegerMath.CeilDiv(TargetStockInDurabilityUnits, ProductionRunsPerToolWear); // = 1

        int correct = StoreChoice.TravelCostPerUnit(TravelHours, ErrandOpportunityCost, convertedTargetInUnits);
        int ifNotConverted = StoreChoice.TravelCostPerUnit(
            TravelHours, ErrandOpportunityCost, TargetStockInDurabilityUnits);

        Assert.Equal(24, correct);
        Assert.Equal(2, ifNotConverted);
        Assert.NotEqual(correct, ifNotConverted);
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
    /// <c>Knowledge[household.Id]</c> で引く実装ミス(#36引き継ぎ表Bと同型)は、この世界では
    /// <c>household.Id == 0 == HeadNpcId</c> なので偶然一致してしまう可能性がある。そこで
    /// 非世帯主(NpcId=1)と世帯主(NpcId=0)を明確に分け、観測をどちらに置くかで結果が変わる
    /// ことを直接見る。
    /// </remarks>
    [Fact]
    public void UsesTheHeadObservationsForMemory()
    {
        const int HeadNpcId = 0;
        const int NonHeadNpcId = 1;
        const int SellerId = 1;

        var world = new World(npcCount: 3, householdCount: 2, itemCount: Item.Count);
        world.Npcs[HeadNpcId].Rank = NpcRank.Master;
        world.Npcs[NonHeadNpcId].Rank = NpcRank.Apprentice;
        world.Npcs[2].Rank = NpcRank.Master;

        world.Households[0] = new HouseholdState(
            id: 0, districtId: 0, headNpcId: HeadNpcId,
            memberNpcIds: new[] { HeadNpcId, NonHeadNpcId }, itemCount: Item.Count);
        world.Households[0].Occupation = Occupation.Miller;

        world.Households[SellerId] = new HouseholdState(
            id: SellerId, districtId: 2, headNpcId: 2, memberNpcIds: new[] { 2 }, itemCount: Item.Count);
        world.Households[SellerId].Occupation = Occupation.Miller;
        world.Households[SellerId].WorkshopInventory[ItemA] = 10;

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
            world, world.Households[0], ItemA,
            targetStockInUnits: 1, errandOpportunityCost: 1, out _);
        Assert.False(foundWithNonHeadOnly);

        // 同じ観測を世帯主に置くと候補に入る。
        world.Knowledge[HeadNpcId].Add(Observation(ItemA, SellerId, Tick.Zero));

        bool foundWithHead = storeChoice.TrySelect(
            world, world.Households[0], ItemA,
            targetStockInUnits: 1, errandOpportunityCost: 1, out var selected);
        Assert.True(foundWithHead);
        Assert.Equal(SellerId, selected.SellerId);
    }
}
