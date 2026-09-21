using Visionary.Sim.Systems;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="StoreChoice"/>(GDD06 §3、#98 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class StoreChoiceTests
{
    private const int ItemA = 0;

    /// <summary>
    /// 1次産品(#38用)。<see cref="Definition"/> のレシピ(Miller: Flour←Grain、Baker〜Smith
    /// (UnusedRecipe): Grain←Timber)のどれも Timber を出力しない。<b>ItemA(Grain)は
    /// UnusedRecipe が出力するため都市生産品である</b>(#149 で窓口も候補になった。
    /// テスト表 #10・#11 参照)。
    /// </summary>
    private const int PrimaryItem = Item.Timber;

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

    private static readonly WorldDefinition Definition = EconomySystemTestFixtures.BuildDefinition(
        new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = Item.Grain, Quantity = 1 } },
            laborPermille: 1000));

    /// <summary>
    /// 【核心】テスト表 #22。距離1(R以内)で当日の提示価格が見えている店でも、
    /// visitedDistrictIdsに無ければ約定しない。同じ店の区画を渡すと約定する。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-20)。</b><c>StoreChoice.TrySelect</c> の
    /// <c>isInReach</c> の判定を「<c>District.Distance(buyer.DistrictId, seller.DistrictId)
    /// &lt;= District.VisionRadius</c>」(旧「知っている店」の判定)へ差し替える変異を当てたところ、
    /// <c>Assert.False(storeChoice.TrySelect(world, buyer, ItemA, Array.Empty&lt;int&gt;(), out _))</c>
    /// が実際値true(距離1の店が訪問区画を渡さずに選ばれた。移動を払わずに隣区画で買えてしまい、
    /// 空間の摩擦が購入の側から抜ける経路)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void PurchaseIsLimitedToTheHomeAndVisitedDistricts()
    {
        const int SellerId = 1;
        const int SellerDistrictId = 1; // 買い手(区画0)から距離1。

        var world = BuildWorld(0, SellerDistrictId);
        world.Households[SellerId].WorkshopInventory[ItemA] = 10;
        world.Market[new MarketKey(ItemA, SellerId)] = 30;

        var storeChoice = new StoreChoice(Definition);
        var buyer = world.Households[0];

        Assert.False(storeChoice.TrySelect(world, buyer, ItemA, Array.Empty<int>(), out _));

        bool found = storeChoice.TrySelect(
            world, buyer, ItemA, new[] { SellerDistrictId }, out var selected);
        Assert.True(found);
        Assert.Equal(SellerId, selected.SellerId);
    }

    /// <summary>テスト表 #23。訪問区画の2店(提示価格30と20) → 20の店。同値なら売り手Id最小。</summary>
    [Fact]
    public void PurchasePicksTheCheapestEffectivePriceThenTheLowestSellerId()
    {
        const int VisitedDistrictId = 3;
        const int ExpensiveSellerId = 1;
        const int CheapSellerId = 2;

        var world = BuildWorld(0, VisitedDistrictId, VisitedDistrictId);
        world.Households[ExpensiveSellerId].WorkshopInventory[ItemA] = 10;
        world.Households[CheapSellerId].WorkshopInventory[ItemA] = 10;
        world.Market[new MarketKey(ItemA, ExpensiveSellerId)] = 30;
        world.Market[new MarketKey(ItemA, CheapSellerId)] = 20;

        var storeChoice = new StoreChoice(Definition);
        var buyer = world.Households[0];

        bool found = storeChoice.TrySelect(
            world, buyer, ItemA, new[] { VisitedDistrictId }, out var selected);
        Assert.True(found);
        Assert.Equal(CheapSellerId, selected.SellerId);
        Assert.Equal(20, selected.UnitEffectivePrice);

        // 同値(ともに20) → 売り手Id最小。
        world.Market[new MarketKey(ItemA, ExpensiveSellerId)] = 20;
        bool tieFound = storeChoice.TrySelect(
            world, buyer, ItemA, new[] { VisitedDistrictId }, out var tieSelected);
        Assert.True(tieFound);
        Assert.Equal(ExpensiveSellerId, tieSelected.SellerId); // Id 1 < Id 2
    }

    /// <summary>
    /// 【核心】テスト表 #5(#38)。自区画が中心 / 訪問に中心を含む / どちらでもない、の3枝。
    /// 3つ目は候補0件。
    /// </summary>
    /// <remarks>
    /// M-2: <c>IsWithinReach</c> の呼び出しを外して常に候補にする変異は、3つ目の枝(候補0件)を
    /// 崩す(全世帯が移動せずに輸入でき、空間の摩擦が輸入の側から抜ける。タスク仕様)。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(2026-09-21、<c>mutator</c> が使い捨てworktreeで測定、対象コミット
    /// <c>39e367a</c>、M-2)。</b>期待どおり赤になった。落ちたのはこの1件だけ ──
    /// 到達判定(<c>IsWithinReach</c>)を守っているのは本テストだけである。
    /// </remarks>
    [Fact]
    public void WindowIsACandidateOnlyWhenTheCentreIsReachable()
    {
        var storeChoice = new StoreChoice(Definition);

        // 1. 自区画が中心。
        var worldAtCentre = BuildWorld(District.ExternalMarketDistrictId);
        bool foundAtCentre = storeChoice.TrySelect(
            worldAtCentre, worldAtCentre.Households[0], PrimaryItem, Array.Empty<int>(), out var atCentre);
        Assert.True(foundAtCentre);
        Assert.Equal(HouseholdState.ExternalMarketSellerId, atCentre.SellerId);

        // 2. 訪問区画に中心を含む(自区画は中心ではない)。
        var worldElsewhere = BuildWorld(0);
        bool foundWithVisit = storeChoice.TrySelect(
            worldElsewhere, worldElsewhere.Households[0], PrimaryItem,
            new[] { District.ExternalMarketDistrictId }, out var withVisit);
        Assert.True(foundWithVisit);
        Assert.Equal(HouseholdState.ExternalMarketSellerId, withVisit.SellerId);

        // 3. どちらでもない → 候補0件(都市内にもTimberの売り手は居ない)。
        bool foundWithoutVisit = storeChoice.TrySelect(
            worldElsewhere, worldElsewhere.Households[0], PrimaryItem, Array.Empty<int>(), out _);
        Assert.False(foundWithoutVisit);
    }

    /// <summary>テスト表 #6(#38)。都市内の売り手と窓口の実効価格が同値のとき都市内が選ばれる。</summary>
    /// <remarks>
    /// 窓口を走査の前に足す・更新を <c>&lt;=</c> にする、のどちらの実装ミスでも本テストが落ちる
    /// (タスク仕様)。
    /// </remarks>
    [Fact]
    public void CitySellerWinsTheTieAgainstTheWindow()
    {
        const int SellerId = 1;

        // 買い手・都市内の売り手ともに中心区画(窓口も自動的に候補になる)。
        var world = BuildWorld(District.ExternalMarketDistrictId, District.ExternalMarketDistrictId);
        world.Households[SellerId].WorkshopInventory[PrimaryItem] = 10;
        // 窓口の提示価格(既定の基準値1、季節係数1000‰ずつ)と同値にする。
        world.Market[new MarketKey(PrimaryItem, SellerId)] = 1;

        var storeChoice = new StoreChoice(Definition);
        var buyer = world.Households[0];

        bool found = storeChoice.TrySelect(world, buyer, PrimaryItem, Array.Empty<int>(), out var selected);

        Assert.True(found);
        Assert.Equal(SellerId, selected.SellerId); // 窓口(ExternalMarketSellerId)ではなく都市内。
    }

    /// <summary>
    /// 【核心】テスト表 #10(#149)。都市内の提示価格が外部売値(天井)を上回るとき、中心区画に
    /// 居る買い手が選ぶのは窓口(<see cref="HouseholdState.ExternalMarketSellerId"/>)であり、
    /// 実効価格は外部売値(導出した天井)そのもの。
    /// </summary>
    /// <remarks>
    /// <b>#38当時のテスト(<c>WindowIsNotACandidateForCityGoods</c>)を置き換える。</b>
    /// 「都市生産品では窓口が候補に出ない」は#149が反転させた規則そのものである
    /// (窓口は全品目を並べる。タスク仕様「作るもの2」)。
    /// </remarks>
    [Fact]
    public void BuyerAtTheCentreChoosesTheWindowWhenLocalOffersExceedTheCeiling()
    {
        const int SellerId = 1;

        // 買い手・都市内の売り手ともに中心区画(窓口も自動的に候補になる)。
        var world = BuildWorld(District.ExternalMarketDistrictId, District.ExternalMarketDistrictId);
        world.Households[SellerId].WorkshopInventory[ItemA] = 10;

        ExternalMarket.TryOfferPrice(Definition, world.Now, ItemA, out int ceiling);
        world.Market[new MarketKey(ItemA, SellerId)] = ceiling + 1; // 天井を上回る提示価格。

        var storeChoice = new StoreChoice(Definition);
        var buyer = world.Households[0];

        bool found = storeChoice.TrySelect(world, buyer, ItemA, Array.Empty<int>(), out var selected);

        Assert.True(found);
        Assert.Equal(HouseholdState.ExternalMarketSellerId, selected.SellerId);
        Assert.Equal(ceiling, selected.UnitEffectivePrice); // trust=0なので実効価格=提示価格。
    }

    /// <summary>
    /// テスト表 #11(#149)。都市生産品でも、都市内の提示価格が外部売値(天井)と同値のときは
    /// 都市内の売り手が選ばれる(<see cref="CitySellerWinsTheTieAgainstTheWindow"/> と同じ規則を
    /// 都市生産品で確かめる)。
    /// </summary>
    [Fact]
    public void CitySellerWinsTheTieAgainstTheWindowForCityGoods()
    {
        const int SellerId = 1;

        var world = BuildWorld(District.ExternalMarketDistrictId, District.ExternalMarketDistrictId);
        world.Households[SellerId].WorkshopInventory[ItemA] = 10;

        ExternalMarket.TryOfferPrice(Definition, world.Now, ItemA, out int ceiling);
        world.Market[new MarketKey(ItemA, SellerId)] = ceiling; // 天井と同値。

        var storeChoice = new StoreChoice(Definition);
        var buyer = world.Households[0];

        bool found = storeChoice.TrySelect(world, buyer, ItemA, Array.Empty<int>(), out var selected);

        Assert.True(found);
        Assert.Equal(SellerId, selected.SellerId); // 窓口(ExternalMarketSellerId)ではなく都市内。
    }

    /// <summary>
    /// 【核心】W2-14 タスク仕様テスト表 #8。工房在庫1の鍛冶(工具、留保1)は工具の店の候補に
    /// 入らない(他に候補が無ければ <c>TrySelect</c> が false)。在庫2なら候補に入る
    /// (<see cref="SellableStock"/> の等式分岐は品目が <see cref="Item.Tools"/> なら職業に
    /// 依らないので、<see cref="Definition"/>(Millerのレシピ)のままで検査できる)。
    /// </summary>
    [Fact]
    public void SmithWithOnlyTheReservedToolIsNotACandidate()
    {
        const int SellerId = 1;

        var world = BuildWorld(0, 0); // 買い手・売り手とも区画0(中心ではない → 窓口は候補外)。
        world.Households[SellerId].WorkshopInventory[Item.Tools] = 1;
        world.Market[new MarketKey(Item.Tools, SellerId)] = 30;

        var storeChoice = new StoreChoice(Definition);
        var buyer = world.Households[0];

        Assert.False(storeChoice.TrySelect(world, buyer, Item.Tools, Array.Empty<int>(), out _));

        // 対照: 在庫2(留保1を超える)なら候補に入る。
        world.Households[SellerId].WorkshopInventory[Item.Tools] = 2;
        bool found = storeChoice.TrySelect(world, buyer, Item.Tools, Array.Empty<int>(), out var selected);
        Assert.True(found);
        Assert.Equal(SellerId, selected.SellerId);
    }

    /// <summary>テスト表 #24(既存を維持)。販売在庫0の店は候補外。自分の売り注文も候補外。</summary>
    [Fact]
    public void PurchaseSkipsEmptyStoresAndOwnOffer()
    {
        const int EmptySellerId = 1;
        const int StockedSellerId = 2;

        var world = BuildWorld(0, 0, 0); // 買い手(0)・在庫0の売り手(1)・在庫ありの売り手(2)、いずれも同区画。
        world.Households[EmptySellerId].WorkshopInventory[ItemA] = 0;
        world.Households[StockedSellerId].WorkshopInventory[ItemA] = 10;
        world.Households[0].WorkshopInventory[ItemA] = 10; // 自分の売り注文(候補外)。

        world.Market[new MarketKey(ItemA, EmptySellerId)] = 10;
        world.Market[new MarketKey(ItemA, StockedSellerId)] = 999;
        world.Market[new MarketKey(ItemA, 0)] = 1; // 自分自身の売り注文。最安だが候補に入らない。

        var storeChoice = new StoreChoice(Definition);

        bool found = storeChoice.TrySelect(
            world, world.Households[0], ItemA, Array.Empty<int>(), out var selected);

        Assert.True(found);
        Assert.Equal(StockedSellerId, selected.SellerId);
    }
}
