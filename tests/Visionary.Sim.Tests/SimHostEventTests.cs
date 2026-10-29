using Visionary.Sim.Commands;
using Visionary.Sim.Dialogue;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests;

/// <summary>
/// プレイヤーの売買のドメインイベントと <c>SimHost.DrainEvents</c>(W5-05 タスク仕様のテスト表 #1〜#6、
/// TDD01 §3.4 / §3.5)。世界は <c>SimHost(M0, シード 1)</c> を <c>Advance</c> で進めて作り、居場所・所持品は
/// internal の <c>World.Player</c> を直接書いて整える。日付の表記 <c>D1T06</c> は <c>Tick.ToString()</c> と同じ。
/// </summary>
public sealed class SimHostEventTests
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

    /// <summary>D1T06。前日の夕方に Need と提示価格が立っている最初の取引可能時刻。</summary>
    private static SimHost HostAtDay1Morning() => HostAt(24 + 6);

    private static void PlacePlayerIn(SimHost host, int districtId) => host.World.Player.DistrictId = districtId;

    private static (HouseholdState Household, Need Need) FirstHouseholdWithDisclosedNeed(SimHost host)
    {
        foreach (var household in host.World.Households)
        {
            var needs = DisclosedNeeds.Of(host.World, household.Id);

            if (needs.Count > 0)
            {
                return (household, needs[0]);
            }
        }

        throw new InvalidOperationException("D1T06 に開示される Need を持つ世帯が無い(値の問題の可能性)。");
    }

    private static (HouseholdState Seller, int ItemId) SellerWithStock(SimHost host)
    {
        foreach (var household in host.World.Households)
        {
            int itemId = Definition.Recipes[(int)household.Occupation].Outputs[0].ItemId;

            if (host.World.Market.ContainsKey(new MarketKey(itemId, household.Id))
                && SellableStock.Of(Definition, household, itemId) >= 1)
            {
                return (household, itemId);
            }
        }

        throw new InvalidOperationException("販売在庫のある売り手が居ない(値の問題の可能性)。");
    }

    /// <summary>
    /// #1。世帯へ売る。要求数量(Need の数量 + 5)が Need の数量で切られる前提で、イベントの
    /// <c>Quantity</c> は約定した個数(<c>Result.Quantity</c>)であって要求数量ではない。感謝と Need Id も写る。
    /// </summary>
    [Fact]
    public void SellToHouseholdEventCarriesTheSettledValuesNotTheRequest()
    {
        var host = HostAtDay1Morning();
        var (household, need) = FirstHouseholdWithDisclosedNeed(host);
        int requested = need.Quantity + 5;
        PlacePlayerIn(host, household.DistrictId);
        host.World.Player.Inventory[need.ItemId] = requested + 2;

        var result = host.Execute(new SellToHouseholdCommand(household.Id, need.ItemId, 1, requested));

        Assert.Equal(CommandStatus.Accepted, result.Status);
        Assert.True(result.Quantity < requested, "要求数量が約定数量より大きい前提が崩れている(値の問題の可能性)。");
        var drained = Assert.Single(host.DrainEvents());
        var trade = Assert.IsType<PlayerTradeExecuted>(drained);
        Assert.Equal(host.Now, trade.At);
        Assert.Equal(PlayerTradeKind.SellToHousehold, trade.Kind);
        Assert.Equal(household.Id, trade.CounterpartyId);
        Assert.Equal(need.ItemId, trade.ItemId);
        Assert.Equal(result.Quantity, trade.Quantity);
        Assert.Equal(result.UnitPrice, trade.UnitPrice);
        Assert.Equal(result.Gratitude, trade.Gratitude);
        Assert.NotNull(trade.Gratitude);
        Assert.Equal(result.NeedId, trade.NeedId);
        Assert.Equal(need.Id, trade.NeedId);
    }

    /// <summary>
    /// 【核心】#2。窓口から買う・窓口へ売る・世帯から買う・世帯へ売るを1回ずつ Accepted にし、4件が実行順に並ぶ。
    /// 窓口の相手は <c>ExternalMarketSellerId</c>、世帯から買うの相手は売り手の世帯 Id。
    /// 世帯へ売る以外は <c>Gratitude == null</c>・<c>NeedId == -1</c>。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-10-29、<c>mutator</c> が使い捨てworktreeで測定、基点コミット <c>788d82d</c>)。</b>
    /// M1: <c>PlayerCommandExecutor</c> の <c>BuyFromWindow</c> の Accepted 経路の
    /// <c>EventLog.Add</c> を削除 → 赤(期待どおり)。
    /// </remarks>
    [Fact]
    public void AllFourTradeDirectionsEmitEventsInExecutionOrderWithTheirCounterparties()
    {
        var host = HostAtDay1Morning();
        var (seller, sellerItem) = SellerWithStock(host);
        var (needy, need) = FirstHouseholdWithDisclosedNeed(host);

        Assert.Equal(CommandStatus.Accepted, host.Execute(new BuyFromWindowCommand(Item.Grain, 2)).Status);

        host.World.Player.Inventory[Item.Bread] = 3;
        Assert.Equal(CommandStatus.Accepted, host.Execute(new SellToWindowCommand(Item.Bread, 2)).Status);

        PlacePlayerIn(host, seller.DistrictId);
        var bought = host.Execute(new BuyFromHouseholdCommand(seller.Id, sellerItem, 1));
        Assert.Equal(CommandStatus.Accepted, bought.Status);

        PlacePlayerIn(host, needy.DistrictId);
        host.World.Player.Inventory[need.ItemId] = need.Quantity + 1;
        var sold = host.Execute(new SellToHouseholdCommand(needy.Id, need.ItemId, 1, need.Quantity));
        Assert.Equal(CommandStatus.Accepted, sold.Status);

        var events = host.DrainEvents().Cast<PlayerTradeExecuted>().ToArray();

        Assert.Equal(
            new[]
            {
                PlayerTradeKind.BuyFromWindow,
                PlayerTradeKind.SellToWindow,
                PlayerTradeKind.BuyFromHousehold,
                PlayerTradeKind.SellToHousehold,
            },
            events.Select(e => e.Kind).ToArray());
        Assert.Equal(HouseholdState.ExternalMarketSellerId, events[0].CounterpartyId);
        Assert.Equal(HouseholdState.ExternalMarketSellerId, events[1].CounterpartyId);
        Assert.Equal(seller.Id, events[2].CounterpartyId);
        Assert.Equal(sellerItem, events[2].ItemId);
        Assert.Equal(needy.Id, events[3].CounterpartyId);

        for (int i = 0; i < 3; i++)
        {
            Assert.Null(events[i].Gratitude);
            Assert.Equal(-1, events[i].NeedId);
        }

        Assert.Equal(Item.Grain, events[0].ItemId);
        Assert.Equal(2, events[0].Quantity);
        Assert.Equal(2, events[1].Quantity);
    }

    /// <summary>
    /// #3。拒否(<c>InTransit</c>・<c>OutsideTradingHours</c>・<c>NoNeed</c>・<c>OverBudget</c>・
    /// <c>InsufficientFunds</c>・<c>NotEnoughGoods</c>)と、Accepted の <c>Move</c>・<c>AskNeeds</c> は発行しない。
    /// </summary>
    [Fact]
    public void RejectionsMoveAndAskNeedsEmitNothing()
    {
        // OutsideTradingHours(D1T05)。
        var early = HostAt(24 + 5);
        Assert.Equal(CommandStatus.OutsideTradingHours, early.Execute(new BuyFromWindowCommand(Item.Grain, 1)).Status);
        Assert.Empty(early.World.EventLog);

        var host = HostAtDay1Morning();
        var (household, need) = FirstHouseholdWithDisclosedNeed(host);
        var player = host.World.Player;

        // InsufficientFunds / NotEnoughGoods(窓口)。
        player.Funds = 0;
        Assert.Equal(CommandStatus.InsufficientFunds, host.Execute(new BuyFromWindowCommand(Item.Grain, 1)).Status);
        Assert.Equal(CommandStatus.NotEnoughGoods, host.Execute(new SellToWindowCommand(Item.Bread, 1)).Status);
        player.Funds = WorldDefinition.PlayerInitialFunds;

        // NoNeed / OverBudget(世帯)。
        PlacePlayerIn(host, household.DistrictId);
        int otherItem = Enumerable.Range(0, Definition.ItemCount).First(
            itemId => DisclosedNeeds.Of(host.World, household.Id).All(n => n.ItemId != itemId));
        player.Inventory[otherItem] = 5;
        player.Inventory[need.ItemId] = 5;
        Assert.Equal(CommandStatus.NoNeed, host.Execute(new SellToHouseholdCommand(household.Id, otherItem, 1, 1)).Status);
        Assert.Equal(
            CommandStatus.OverBudget,
            host.Execute(new SellToHouseholdCommand(household.Id, need.ItemId, 1_000_000, 1)).Status);
        Assert.Empty(host.World.EventLog);

        // Accepted の AskNeeds / Move は発行しない。
        Assert.Equal(CommandStatus.Accepted, host.Execute(new AskNeedsCommand(household.Id)).Status);
        var move = host.Execute(new MoveCommand(household.DistrictId == Center ? 0 : Center));
        Assert.Equal(CommandStatus.Accepted, move.Status);
        Assert.Empty(host.World.EventLog);

        // InTransit。
        host.Advance(1);
        Assert.Equal(CommandStatus.InTransit, host.Execute(new BuyFromWindowCommand(Item.Grain, 1)).Status);
        Assert.Empty(host.World.EventLog);
    }

    /// <summary>
    /// 【核心】#4。読み出し位置。1件 → <c>DrainEvents()</c> 1件 → 0件 → 2件発行 → 2件(買い・売りの順)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-10-29、<c>mutator</c> が使い捨てworktreeで測定、基点コミット <c>788d82d</c>)。</b>
    /// M2: <c>SimHost.DrainEvents</c> の <c>_drainedEventCount = log.Count;</c> を削除 → 赤(期待どおり)。
    /// 削るとフィールドが未代入になり CS0649 になるため、<c>TreatWarningsAsErrors</c> を外して測定した。
    /// </remarks>
    [Fact]
    public void DrainEventsReturnsOnlyEventsAddedSinceTheLastCallInOrder()
    {
        var host = HostAtDay1Morning();
        host.World.Player.Inventory[Item.Bread] = 5;

        host.Execute(new BuyFromWindowCommand(Item.Grain, 1));
        var first = host.DrainEvents();
        Assert.Single(first);

        Assert.Empty(host.DrainEvents());

        host.Execute(new BuyFromWindowCommand(Item.Grain, 1));
        host.Execute(new SellToWindowCommand(Item.Bread, 1));
        var second = host.DrainEvents();

        Assert.Equal(2, second.Count);
        Assert.Equal(PlayerTradeKind.BuyFromWindow, Assert.IsType<PlayerTradeExecuted>(second[0]).Kind);
        Assert.Equal(PlayerTradeKind.SellToWindow, Assert.IsType<PlayerTradeExecuted>(second[1]).Kind);
        Assert.Empty(host.DrainEvents());
    }

    /// <summary>
    /// #5。<c>DrainEvents()</c> の後も <c>EventLog</c> の件数は変わらない(追記専用)。受け取ったリストは
    /// 写しで、後の発行で件数が変わらない。
    /// </summary>
    [Fact]
    public void DrainEventsDoesNotClearTheLogAndReturnsACopy()
    {
        var host = HostAtDay1Morning();
        host.Execute(new BuyFromWindowCommand(Item.Grain, 1));

        var drained = host.DrainEvents();

        Assert.Single(host.World.EventLog);
        Assert.Single(drained);

        host.Execute(new BuyFromWindowCommand(Item.Grain, 1));

        Assert.Equal(2, host.World.EventLog.Count);
        Assert.Single(drained);
    }

    /// <summary>#6。コマンドを打たない走行(60日)で、NPC のシステムは何も発行しない。</summary>
    [Fact]
    public void NpcSystemsNeverEmitEvents()
    {
        var host = new SimHost(Definition, Seed);

        host.Advance(60 * 24);

        Assert.Empty(host.World.EventLog);
        Assert.Empty(host.DrainEvents());
    }
}
