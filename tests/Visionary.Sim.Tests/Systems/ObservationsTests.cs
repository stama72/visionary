using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="Observations"/>(GDD06 §3.1 / GDD08 §8.1、#36 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class ObservationsTests
{
    /// <summary>単独NPC・単独世帯を <paramref name="districtIds"/> の順に並べた世界を作る。</summary>
    private static World BuildWorldWithHouseholdsAtDistricts(params int[] districtIds)
    {
        int count = districtIds.Length;
        var world = new World(npcCount: count, householdCount: count, itemCount: Item.Count);

        for (int i = 0; i < count; i++)
        {
            world.Households[i] = new HouseholdState(
                id: i, districtId: districtIds[i], headNpcId: i, memberNpcIds: new[] { i },
                itemCount: Item.Count);
        }

        return world;
    }

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
    /// 【核心】テスト表 #31。自区画4・売り手を区画1(距離1)と区画0(距離2)に置く →
    /// 区画1の売り注文だけが観測になる。<c>visitedDistrictIds</c> に 0 を渡すと区画0 も入る。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>IsWithinVisionRadius</c> を常に <c>true</c> を返す実装
    /// (距離を見ない変異)に変えたところ、<c>Assert.Equal(new[] { 1 }, observedSellers)</c> が
    /// 実際値 <c>[1, 2]</c>(区画0の売り手まで見えてしまう)で失敗した(赤を確認、
    /// GDD02 §8-7「情報の摩擦」が構造として消える経路)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ObservationsAreBornOnlyWithinTheVisionRadius()
    {
        World BuildScenario()
        {
            // households[0]=観測者(区画4)、households[1]=近い売り手(区画1、距離1)、
            // households[2]=遠い売り手(区画0、距離2)。
            var world = BuildWorldWithHouseholdsAtDistricts(4, 1, 0);
            world.Market[new MarketKey(Item.Grain, 1)] = 100;
            world.Market[new MarketKey(Item.Grain, 2)] = 200;

            return world;
        }

        var withoutVisit = BuildScenario();
        Observations.CollectAndShare(withoutVisit, withoutVisit.Households[0], Array.Empty<int>());
        var observedSellers = withoutVisit.Knowledge[0].Select(o => o.SellerId).ToArray();
        Assert.Equal(new[] { 1 }, observedSellers);

        var withVisit = BuildScenario();
        Observations.CollectAndShare(withVisit, withVisit.Households[0], new[] { 0 });
        var observedSellersWithVisit = withVisit.Knowledge[0].Select(o => o.SellerId).OrderBy(id => id).ToArray();
        Assert.Equal(new[] { 1, 2 }, observedSellersWithVisit);
    }

    /// <summary>テスト表 #32。自世帯の売り注文が Knowledge に入らない。</summary>
    [Fact]
    public void OwnOffersAreNeverObserved()
    {
        var world = BuildWorldWithHouseholdsAtDistricts(4, 4); // 同区画(距離0)なので観測範囲内
        world.Market[new MarketKey(Item.Grain, 0)] = 100; // household0自身の売り注文

        Observations.CollectAndShare(world, world.Households[0], Array.Empty<int>());

        Assert.Empty(world.Knowledge[0]);
    }

    /// <summary>
    /// テスト表 #33。Price = Market の値、SellerId = 売り手世帯、LocationId = 売り手の区画、
    /// ObservedAt = 当日、Source = Direct。
    /// </summary>
    [Fact]
    public void ObservationRecordsThePriceSellerAndSellerDistrict()
    {
        var world = BuildWorldWithHouseholdsAtDistricts(4, 1); // 観測者(区画4)・売り手(区画1)
        world.Market[new MarketKey(Item.Grain, 1)] = 123;

        // ObservedAtが当日と一致することを判別できるよう、時計を進めて既定値(Tick.Zero)から離す。
        EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 5 * 24);

        Observations.CollectAndShare(world, world.Households[0], Array.Empty<int>());

        var observation = world.Knowledge[0].Single();
        Assert.Equal(Item.Grain, observation.ItemId);
        Assert.Equal(123, observation.Price);
        Assert.Equal(1, observation.SellerId);
        Assert.Equal(1, observation.LocationId); // 売り手の区画(観測者の区画4ではない)
        Assert.Equal(world.Now, observation.ObservedAt);
        Assert.Equal(ObservationSource.Direct, observation.Source);
    }

    /// <summary>
    /// 【核心】テスト表 #34。親方と徒弟の Knowledge に同じ件数・同じ内容が入る。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b>世帯全員への配布ループを <c>household.MemberNpcIds</c> から
    /// <c>new[] { household.HeadNpcId }</c>(世帯主だけに入れる変異)に変えたところ、
    /// <c>Assert.Single(world.Knowledge[ApprenticeNpcId])</c> が実際値0件(Collection was empty)
    /// で失敗した(赤を確認、GDD08 §8.1「見ただけの売り注文も含む」帰宅時共有が消える経路)。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void TodaysObservationIsSharedWithEveryHouseholdMember()
    {
        const int HeadNpcId = 0;
        const int ApprenticeNpcId = 1;
        const int SellerNpcId = 2;

        var world = new World(npcCount: 3, householdCount: 2, itemCount: Item.Count);
        world.Households[0] = new HouseholdState(
            id: 0, districtId: 4, headNpcId: HeadNpcId,
            memberNpcIds: new[] { HeadNpcId, ApprenticeNpcId }, itemCount: Item.Count);
        world.Households[1] = new HouseholdState(
            id: 1, districtId: 1, headNpcId: SellerNpcId, memberNpcIds: new[] { SellerNpcId },
            itemCount: Item.Count);

        world.Market[new MarketKey(Item.Grain, 1)] = 100;

        Observations.CollectAndShare(world, world.Households[0], Array.Empty<int>());

        Assert.Single(world.Knowledge[HeadNpcId]);
        Assert.Single(world.Knowledge[ApprenticeNpcId]);
        Assert.Equal(world.Knowledge[HeadNpcId][0], world.Knowledge[ApprenticeNpcId][0]);
    }

    /// <summary>
    /// 【核心】テスト表 #35。retention=7・now=D10: D3 は残り D2 は消える。当日(D10)に生まれたものは
    /// 消えない。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b>失効の境界条件を <c>&gt;=</c>(<c>world.Now.DayIndex -
    /// observation.ObservedAt.DayIndex &gt;= retentionDays</c>)に変える変異を当てたところ、
    /// D3(差7、保持期間ちょうど)が消えて <c>Assert.Equal(new long[] { 3, 10 }, ...)</c> が
    /// 実際値 <c>[10]</c> で失敗した(赤を確認、<see cref="MarketReference"/> の有効性判定の
    /// 境界と1日ずれる経路)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ObservationsExpireStrictlyAfterTheRetentionPeriod()
    {
        var world = new World(npcCount: 1, householdCount: 0, itemCount: Item.Count);
        EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 10 * 24); // now = D10

        world.Knowledge[0].Add(Observation(Item.Grain, sellerId: 1, price: 10, Tick.FromDays(3)));
        world.Knowledge[0].Add(Observation(Item.Grain, sellerId: 2, price: 20, Tick.FromDays(2)));
        world.Knowledge[0].Add(Observation(Item.Grain, sellerId: 3, price: 30, Tick.FromDays(10)));

        Observations.Expire(world, retentionDays: 7);

        var remainingDays = world.Knowledge[0].Select(o => o.ObservedAt.DayIndex).ToArray();
        Assert.Equal(new long[] { 3, 10 }, remainingDays);
    }

    /// <summary>
    /// テスト表 #36。1世帯の Knowledge の並びが (品目Id, 売り手Id) 昇順。同じ売り注文が2件入らない。
    /// </summary>
    [Fact]
    public void ObservationsAreAppendedInMarketKeyOrder()
    {
        // households[0]=観測者(区画4)、households[1..3]=売り手(すべて区画1)。
        var world = BuildWorldWithHouseholdsAtDistricts(4, 1, 1, 1);

        world.Market[new MarketKey(Item.Grain, 3)] = 10;
        world.Market[new MarketKey(Item.Grain, 1)] = 20;
        world.Market[new MarketKey(Item.IronOre, 2)] = 30;

        // 訪問区画にも売り手の区画(1)を含める ── 自区画(距離1)経由でも訪問区画(距離0)経由でも
        // 条件を満たす売り手が二重に記録されないことを確かめる。
        Observations.CollectAndShare(world, world.Households[0], new[] { 1 });

        var actual = world.Knowledge[0].Select(o => (o.ItemId, o.SellerId)).ToArray();
        var expected = new[] { (Item.Grain, 1), (Item.Grain, 3), (Item.IronOre, 2) };

        Assert.Equal(expected, actual);
    }

    private static Recipe MillerRecipe() =>
        new(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = Item.Grain, Quantity = 2 } },
            laborPermille: 1000);

    /// <summary>
    /// 【核心】テスト表 #37。パイプラインを1日進めて観測が生まれても当日の相場基準は立たない
    /// (床のまま)。2日目に進めると立つ。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>TradeSystem.Step</c> の観測の段(<c>Observations.Expire</c>
    /// / <c>Observations.CollectAndShare</c> の呼び出し)を値付けの段(<c>Step</c> の冒頭、
    /// <c>world.Market</c> を読む前)へ移す変異を当てたところ、2日目の <c>Assert.NotEqual(
    /// floorPrice, world.Market[key0])</c> が「Values are equal」(実際値も床のまま)で
    /// 失敗した(赤を確認)。観測が値付けの前に走ると、1日目の観測は <c>world.Market</c> がまだ
    /// 空のうちに空振りし、2日目の観測は「まだ書き換えていない1日目の価格」を<b>2日目の日付で</b>
    /// 記録するため、2日目の<see cref="MarketReference.TrySeller"/>が読む時点で dayDifference=0
    /// となり除外される(観測が実質1日遅延し、GDD06 §3.1「観測するのは当日の提示価格」が崩れる
    /// 経路)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ObservationBecomesUsableOnTheNextDayNotToday()
    {
        const int ShipmentTarget = 5;
        const int SellableStock = 5; // 在庫比 = 1000‰(目標どおり) → 価格係数1000‰

        var definition = EconomySystemTestFixtures.BuildDefinition(
            MillerRecipe(), minimumMarginPermille: 0, shipmentDays: ShipmentTarget);

        var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);
        world.Households[0] = new HouseholdState(
            id: 0, districtId: 4, headNpcId: 0, memberNpcIds: new[] { 0 }, itemCount: Item.Count);
        world.Households[0].Occupation = Occupation.Miller;
        // 出荷目標在庫(5)より少なく持たせる ── 相場基準が立ったとき、在庫比‰ が1000からずれて
        // 提示価格が床と異なる値になるようにする(床は原価に依らない定数になったため。GDD02c §1)。
        world.Households[0].WorkshopInventory[Item.Flour] = 1;

        world.Households[1] = new HouseholdState(
            id: 1, districtId: 1, headNpcId: 1, memberNpcIds: new[] { 1 }, itemCount: Item.Count); // 距離1(観測範囲内)
        world.Households[1].Occupation = Occupation.Miller;
        world.Households[1].WorkshopInventory[Item.Flour] = SellableStock;

        var system = new TradeSystem(definition);
        var key0 = new MarketKey(Item.Flour, 0);

        EconomySystemTestFixtures.RunDays(world, system, days: 1); // 1日目

        int floorPrice = definition.ExternalBuyPrice(Item.Flour);
        Assert.Equal(floorPrice, world.Market[key0]); // 相場基準が立たない(床のまま)

        EconomySystemTestFixtures.RunDays(world, system, days: 1); // 2日目

        // 1日目に生まれた観測(household1の売り注文)が前日の記憶として使える。household0は
        // 在庫比‰ が1000からずれているので、相場基準が立てば床とは異なる値になる。
        Assert.NotEqual(floorPrice, world.Market[key0]);
    }
}
