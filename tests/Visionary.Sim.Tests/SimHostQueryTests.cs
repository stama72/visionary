using Visionary.Sim.Commands;
using Visionary.Sim.Dialogue;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests;

/// <summary>
/// <c>SimHost.Query</c>(読み出しモデル。W5-05 タスク仕様のテスト表 #7〜#13、TDD01 §3.5)。
/// 世界は <c>SimHost(M0, シード 1)</c> を <c>Advance</c> で進めて作る。日付の表記 <c>D1T06</c> は
/// <c>Tick.ToString()</c> と同じ。
/// </summary>
public sealed class SimHostQueryTests
{
    private const long Seed = 1;
    private const int Center = District.ExternalMarketDistrictId;

    private static readonly WorldDefinition Definition = WorldDefinition.M0;

    private static SimHost HostAt(int tick)
    {
        var host = new SimHost(Definition, Seed);

        if (tick > 0)
        {
            host.Advance(tick);
        }

        return host;
    }

    /// <summary>D<paramref name="day"/>T<paramref name="hour"/> の host。</summary>
    private static SimHost HostAt(int day, int hour) => HostAt((day * Tick.HoursPerDay) + hour);

    /// <summary>
    /// 日 1〜60 の T06 を順に走査して、<paramref name="predicate"/> を満たす最初の日の host を返す。
    /// 無ければ null(呼び出し側が表明する)。
    /// </summary>
    private static SimHost? FindDay6(Func<SimHost, bool> predicate)
    {
        var host = HostAt(Tick.HoursPerDay + 6);

        for (int day = 1; day <= 60; day++)
        {
            if (predicate(host))
            {
                return host;
            }

            host.Advance(Tick.HoursPerDay);
        }

        return null;
    }

    private static bool HasReservedOffer(SimHost host) =>
        host.World.Market.Keys.Any(key =>
        {
            var household = host.World.Households[key.SellerId];
            int workshop = household.WorkshopInventory[key.ItemId];

            return workshop >= 1 && SellableStock.Of(Definition, household, key.ItemId) != workshop;
        });

    /// <summary>
    /// #7。売り注文が <c>World.Market</c> と件数・各キーの価格で一致し、<c>MarketKey</c> 昇順。
    /// 区画は世帯の区画、販売在庫は <c>SellableStock.Of</c>。販売在庫 ≠ 工房在庫の行が1件以上ある日を選ぶ。
    /// </summary>
    [Fact]
    public void OffersMirrorTheMarketWithDistrictAndSellableStock()
    {
        var host = FindDay6(HasReservedOffer);
        Assert.NotNull(host);

        var offers = host.Query.Offers();
        var market = host.World.Market.ToArray();

        Assert.Equal(market.Length, offers.Count);

        for (int i = 0; i < market.Length; i++)
        {
            var (key, price) = market[i];
            var household = host.World.Households[key.SellerId];

            Assert.Equal(key.ItemId, offers[i].ItemId);
            Assert.Equal(key.SellerId, offers[i].HouseholdId);
            Assert.Equal(household.DistrictId, offers[i].DistrictId);
            Assert.Equal(price, offers[i].UnitPrice);
            Assert.Equal(SellableStock.Of(Definition, household, key.ItemId), offers[i].SellableStock);
        }

        Assert.Contains(
            offers,
            offer => offer.SellableStock != host.World.Households[offer.HouseholdId].WorkshopInventory[offer.ItemId]);

        var keys = offers.Select(o => new MarketKey(o.ItemId, o.HouseholdId)).ToArray();
        Assert.Equal(keys.OrderBy(k => k.ItemId).ThenBy(k => k.SellerId).ToArray(), keys);
    }

    /// <summary>#8。世帯 Id 昇順で、区画・職業・出力品目(レシピの出力)・構成員が World と一致。</summary>
    [Fact]
    public void HouseholdsMirrorTheWorldInIdOrder()
    {
        var host = HostAt(24 + 6);

        var views = host.Query.Households();

        Assert.Equal(host.World.Households.Length, views.Count);

        for (int i = 0; i < views.Count; i++)
        {
            var household = host.World.Households[i];

            Assert.Equal(household.Id, views[i].Id);
            Assert.Equal(i, views[i].Id);
            Assert.Equal(household.DistrictId, views[i].DistrictId);
            Assert.Equal(household.Occupation, views[i].Occupation);
            Assert.Equal(Definition.Recipes[(int)household.Occupation].Outputs[0].ItemId, views[i].OutputItemId);
            Assert.Equal(household.MemberNpcIds, views[i].MemberNpcIds.ToArray());
        }

        Assert.Contains(views, view => view.MemberNpcIds.Count > 1);
    }

    /// <summary>
    /// 【核心】#9。<c>TradableNeeds()</c> が <c>DisclosedNeeds.Of(world, 0)</c>, <c>Of(world, 1)</c>, … の連結と
    /// 並びまで一致。その日の <c>World.Needs</c> に開示されない理由の Need が1件以上、開示される Need も1件以上ある。
    /// プレイヤーは中心に居るまま(居合わせていない世帯の Need も入る)。
    /// </summary>
    [Fact]
    public void TradableNeedsConcatenateTheDisclosedNeedsPerHouseholdInIdOrder()
    {
        // 9a(レビュー1巡)。正しい列が、(a) 開示される Need 全体の Need Id 昇順とも、(b) World.Needs を理由で
        // 絞っただけの列とも異なる日を選ぶ(day 1 は世帯 Id 順・Need Id 順・World.Needs 順が一致しやすい)。
        // シード 1 の60日内に見つかる(見つからなければここで落ちる)。
        var host = FindDay6(h =>
            h.World.Needs.Any(n => !IsDisclosedReason(n.ReasonCode))
            && h.World.Needs.Any(n => IsDisclosedReason(n.ReasonCode))
            && OrderPremiseHolds(h.World, ExpectedTradableNeeds(h.World)));
        Assert.NotNull(host);
        Assert.Equal(Center, host.World.Player.LocationAt(host.Now).DistrictId);

        var expected = ExpectedTradableNeeds(host.World);

        var actual = host.Query.TradableNeeds();

        Assert.NotEmpty(expected);
        Assert.Equal(expected, actual.ToArray());
        Assert.True(actual.Count < host.World.Needs.Count);

        // 居合わせていない世帯の Need も入る。
        Assert.Contains(actual, need => host.World.Households[need.TargetHouseholdId].DistrictId != Center);
    }

    private static List<Need> ExpectedTradableNeeds(World world)
    {
        var expected = new List<Need>();

        for (int householdId = 0; householdId < world.Households.Length; householdId++)
        {
            expected.AddRange(DisclosedNeeds.Of(world, householdId));
        }

        return expected;
    }

    /// <summary>正しい列が、開示 Need 全体の Id 昇順とも、World.Needs を絞っただけの列とも異なる。</summary>
    private static bool OrderPremiseHolds(World world, List<Need> expected)
    {
        var disclosed = world.Needs.Where(n => IsDisclosedReason(n.ReasonCode)).ToList();
        var byId = disclosed.OrderBy(n => n.Id).ToList();

        return !expected.SequenceEqual(byId) && !expected.SequenceEqual(disclosed);
    }

    private static bool IsDisclosedReason(NeedReason reason) =>
        reason is NeedReason.ProductionStopped or NeedReason.ToolsExhausted or NeedReason.DistantStock;

    /// <summary>
    /// #10。D1T09 と D1T20 に、全 NPC について <c>TimetableOf(npc)</c> が <c>Timetable.Of(…, Now)</c> と一致。
    /// D1T09 に <c>Traveling</c> か <c>AtDestination</c> の NPC が1人以上いる(全員が家なら時刻の取り違えが通る)。
    /// </summary>
    [Fact]
    public void TimetableOfDelegatesToTheTimetableAtNow()
    {
        var host = HostAt(1, 9);
        bool anyOut = false;

        for (int npc = 0; npc < host.World.Npcs.Length; npc++)
        {
            var entry = host.Query.TimetableOf(npc);

            Assert.Equal(Timetable.Of(Definition, host.World, npc, host.Now), entry);
            anyOut |= entry.Activity is TimetableActivity.Traveling or TimetableActivity.AtDestination;
        }

        Assert.True(anyOut, "D1T09 に外出中の NPC が居ない(値の問題の可能性)。");

        host.Advance(11);
        Assert.Equal(20, host.Now.HourOfDay);

        for (int npc = 0; npc < host.World.Npcs.Length; npc++)
        {
            Assert.Equal(Timetable.Of(Definition, host.World, npc, host.Now), host.Query.TimetableOf(npc));
        }
    }

    /// <summary>
    /// 【核心】#11。プレイヤー。初期は所持金・所持品 0・<c>(false, 中心)</c>・行き先 −1・到着 null。
    /// D0T06 に 4→0 へ移動(到着 D0T08)。D0T07 は移動中で行き先 0・到着 D0T08。D0T08 は到着済みで
    /// 行き先 −1・到着 null(<c>MoveDestinationDistrictId</c> は 0 のまま残っている)。
    /// </summary>
    [Fact]
    public void PlayerViewFollowsTheMoveAndReportsArrivalWithoutDestination()
    {
        var initial = HostAt(0).Query.Player();

        Assert.Equal(WorldDefinition.PlayerInitialFunds, initial.Funds);
        Assert.Equal(2_400, initial.Funds);
        Assert.Equal(Definition.ItemCount, initial.Inventory.Count);
        Assert.All(initial.Inventory, held => Assert.Equal(0, held));
        Assert.Equal(new PlayerLocation(false, Center), initial.Location);
        Assert.Equal(-1, initial.DestinationDistrictId);
        Assert.Null(initial.ArrivalAt);

        var host = HostAt(6);
        host.Execute(new MoveCommand(0));

        host.Advance(1);
        var moving = host.Query.Player();
        Assert.Equal(new PlayerLocation(true, Center), moving.Location);
        Assert.Equal(0, moving.DestinationDistrictId);
        Assert.Equal(new Tick(8), moving.ArrivalAt);

        host.Advance(1);
        Assert.Equal(0, host.World.Player.MoveDestinationDistrictId);   // 状態は書き換わっていない
        var arrived = host.Query.Player();
        Assert.Equal(new PlayerLocation(false, 0), arrived.Location);
        Assert.Equal(-1, arrived.DestinationDistrictId);
        Assert.Null(arrived.ArrivalAt);
    }

    /// <summary>
    /// #12。写し。<c>Player()</c> を取った後に <c>BuyFromWindow</c> で所持金と所持品を変えても、先に取った
    /// <c>PlayerView</c> は変わらない。<c>Households()</c> の <c>MemberNpcIds</c> と <c>Offers()</c> も、
    /// <c>Advance</c> の後に先に取った値が変わらない。
    /// </summary>
    [Fact]
    public void ViewsAreCopiesThatLaterChangesDoNotReach()
    {
        var host = HostAt(24 + 6);
        var player = host.Query.Player();
        int fundsBefore = player.Funds;

        host.Execute(new BuyFromWindowCommand(Item.Grain, 2));

        Assert.Equal(fundsBefore, player.Funds);
        Assert.Equal(0, player.Inventory[Item.Grain]);
        Assert.Equal(2, host.Query.Player().Inventory[Item.Grain]);

        var households = host.Query.Households();
        var memberIds = households.Select(h => h.MemberNpcIds.ToArray()).ToArray();

        // 世界側の配列を書き換えても写しは動かない。
        var worldMembers = host.World.Households[0].MemberNpcIds;
        int original = worldMembers[0];
        worldMembers[0] = -12345;
        Assert.Equal(memberIds[0], households[0].MemberNpcIds.ToArray());
        worldMembers[0] = original;   // 後の Advance が壊れないように戻す

        var offers = host.Query.Offers();
        var offersBefore = offers.ToArray();
        int countBefore = offers.Count;

        host.Advance(Tick.HoursPerDay);

        Assert.Equal(countBefore, offers.Count);
        Assert.Equal(offersBefore, offers.ToArray());
    }

    /// <summary>#13。5つのメソッドすべてを呼んだ前後で <c>StateHash()</c> と <c>EventLog.Count</c> が一致。</summary>
    [Fact]
    public void QueryDoesNotWriteTheWorld()
    {
        var host = HostAt(6);
        host.Execute(new MoveCommand(0));
        host.Advance(2);   // 到着済み(未正規化)

        ulong hashBefore = host.StateHash();
        int eventsBefore = host.World.EventLog.Count;
        long arrivalBefore = host.World.Player.MoveArrivalTick;
        int destinationBefore = host.World.Player.MoveDestinationDistrictId;
        int districtBefore = host.World.Player.DistrictId;

        _ = host.Query.Offers();
        _ = host.Query.Households();
        _ = host.Query.TradableNeeds();
        _ = host.Query.TimetableOf(0);
        _ = host.Query.Player();
        _ = host.Query.Now;

        Assert.Equal(hashBefore, host.StateHash());
        Assert.Equal(eventsBefore, host.World.EventLog.Count);
        Assert.Equal(arrivalBefore, host.World.Player.MoveArrivalTick);
        Assert.Equal(destinationBefore, host.World.Player.MoveDestinationDistrictId);
        Assert.Equal(districtBefore, host.World.Player.DistrictId);
    }
}
