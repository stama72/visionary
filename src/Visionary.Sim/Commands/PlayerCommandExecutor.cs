using Visionary.Sim.Dialogue;
using Visionary.Sim.Numerics;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Commands;

/// <summary>
/// コマンドの実行(TDD01 §3.4 / GDD06 §6)。<b>乱数を開かない</b> ── NPC の取引コード(<c>StoreChoice</c> など)を
/// 流用しない。拒否は <see cref="Result"/> で返し、<b>拒否は World を一切書かない</b>。
/// </summary>
/// <remarks>
/// <b>検査の順序は仕様である</b>(前の段で拒否したら後ろは見ない)。
/// 1. 引数 → 2. 移動中 → 3. 時間帯(移動以外) → 4. 居合わせ(移動以外) → 5. コマンドごとの段。
/// </remarks>
internal sealed class PlayerCommandExecutor
{
    private readonly WorldDefinition _definition;
    private readonly PlayerSale _playerSale;

    public PlayerCommandExecutor(WorldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _definition = definition;
        _playerSale = new PlayerSale(definition);
    }

    public Result Execute(World world, ICommand command)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(command);

        return command switch
        {
            MoveCommand move => Move(world, move),
            AskNeedsCommand ask => AskNeeds(world, ask),
            SellToHouseholdCommand sell => SellToHousehold(world, sell),
            BuyFromHouseholdCommand buy => BuyFromHousehold(world, buy),
            BuyFromWindowCommand buy => BuyFromWindow(world, buy),
            SellToWindowCommand sell => SellToWindow(world, sell),
            _ => throw new ArgumentException(
                $"未知のコマンドの型: {command.GetType().Name}", nameof(command)),
        };
    }

    private static Result Reject(CommandStatus status) => new() { Status = status };

    private bool IsHouseholdId(World world, int householdId) =>
        householdId >= 0 && householdId < world.Households.Length;

    private bool IsItemId(int itemId) => itemId >= 0 && itemId < _definition.ItemCount;

    /// <summary>段2〜4。拒否なら結果を返す。</summary>
    private static Result? CheckTransitHoursAndPresence(World world, int requiredDistrictId)
    {
        var player = world.Player;

        if (player.LocationAt(world.Now).InTransit)
        {
            return Reject(CommandStatus.InTransit);
        }

        int hour = world.Now.HourOfDay;

        if (hour < WorldDefinition.WorkdayStartHour || hour >= WorldDefinition.EveningTradeHour)
        {
            return Reject(CommandStatus.OutsideTradingHours);
        }

        if (player.LocationAt(world.Now).DistrictId != requiredDistrictId)
        {
            return Reject(CommandStatus.NotPresent);
        }

        return null;
    }

    private Result Move(World world, MoveCommand command)
    {
        int destination = command.DestinationDistrictId;

        if (destination < 0 || destination >= District.Count)
        {
            return Reject(CommandStatus.InvalidArgument);
        }

        var player = world.Player;
        var location = player.LocationAt(world.Now);

        if (location.InTransit)
        {
            return Reject(CommandStatus.InTransit);
        }

        if (destination == location.DistrictId)
        {
            return Reject(CommandStatus.AlreadyThere);
        }

        // 到着済みの移動をここで正規化する(状態は到着時に書き換えない。TDD01 §3.2)。
        int distance = District.Distance(location.DistrictId, destination);
        long arrival = checked(world.Now.Value + ((long)distance * _definition.TravelHoursPerDistrict));

        player.DistrictId = location.DistrictId;
        player.MoveDestinationDistrictId = destination;
        player.MoveArrivalTick = arrival;

        return new Result { Status = CommandStatus.Accepted, ArrivalAt = new Tick(arrival) };
    }

    private Result AskNeeds(World world, AskNeedsCommand command)
    {
        if (!IsHouseholdId(world, command.HouseholdId))
        {
            return Reject(CommandStatus.InvalidArgument);
        }

        var rejection = CheckTransitHoursAndPresence(world, world.Households[command.HouseholdId].DistrictId);

        if (rejection is not null)
        {
            return rejection;
        }

        return new Result
        {
            Status = CommandStatus.Accepted,
            DisclosedNeeds = DisclosedNeeds.Of(world, command.HouseholdId),
        };
    }

    private Result SellToHousehold(World world, SellToHouseholdCommand command)
    {
        if (!IsHouseholdId(world, command.HouseholdId) || !IsItemId(command.ItemId)
            || command.UnitPrice < 1 || command.Quantity < 1)
        {
            return Reject(CommandStatus.InvalidArgument);
        }

        var rejection = CheckTransitHoursAndPresence(world, world.Households[command.HouseholdId].DistrictId);

        if (rejection is not null)
        {
            return rejection;
        }

        var player = world.Player;

        // 所持品の検査は PlayerSale の前に置く ── 後ろに置くと、売る物が無いのに1日1回を消費する。
        int held = player.Inventory[command.ItemId];

        if (held == 0)
        {
            return Reject(CommandStatus.NotEnoughGoods);
        }

        int offered = Math.Min(command.Quantity, held);
        var sale = _playerSale.Execute(world, command.HouseholdId, command.ItemId, command.UnitPrice, offered);

        if (sale.Outcome != PlayerSaleOutcome.Accepted)
        {
            var status = sale.Outcome switch
            {
                PlayerSaleOutcome.NoNeed => CommandStatus.NoNeed,
                PlayerSaleOutcome.AlreadyOfferedToday => CommandStatus.AlreadyOfferedToday,
                PlayerSaleOutcome.OverBudget => CommandStatus.OverBudget,
                _ => throw new InvalidOperationException($"未知の PlayerSaleOutcome: {sale.Outcome}"),
            };

            return new Result { Status = status, NeedId = sale.NeedId };
        }

        // 所持品を減らすのは PlayerSale の結果の数量(Need の数量で切られうるので offered ではない)。
        player.Funds = checked(player.Funds + sale.Payment);
        player.Inventory[command.ItemId] -= sale.Quantity;

        world.EventLog.Add(new PlayerTradeExecuted(
            world.Now, PlayerTradeKind.SellToHousehold, command.HouseholdId, command.ItemId,
            sale.Quantity, sale.UnitPrice, sale.Gratitude, sale.NeedId));

        return new Result
        {
            Status = CommandStatus.Accepted,
            Quantity = sale.Quantity,
            UnitPrice = sale.UnitPrice,
            Payment = sale.Payment,
            Gratitude = sale.Gratitude,
            NeedId = sale.NeedId,
        };
    }

    private Result BuyFromHousehold(World world, BuyFromHouseholdCommand command)
    {
        if (!IsHouseholdId(world, command.HouseholdId) || !IsItemId(command.ItemId) || command.Quantity < 1)
        {
            return Reject(CommandStatus.InvalidArgument);
        }

        var household = world.Households[command.HouseholdId];
        var rejection = CheckTransitHoursAndPresence(world, household.DistrictId);

        if (rejection is not null)
        {
            return rejection;
        }

        if (!world.Market.TryGetValue(new MarketKey(command.ItemId, command.HouseholdId), out int price))
        {
            return Reject(CommandStatus.NoOffer);
        }

        int stock = SellableStock.Of(_definition, household, command.ItemId);

        if (stock == 0)
        {
            return Reject(CommandStatus.SellerOutOfStock);
        }

        var player = world.Player;
        int affordable = IntegerMath.FloorDiv(player.Funds, price);

        if (affordable == 0)
        {
            return Reject(CommandStatus.InsufficientFunds);
        }

        int quantity = Math.Min(command.Quantity, Math.Min(stock, affordable));

        // 観測を作らない・Market を書かない(GDD06 §6.2)。
        TradeSettlement.ExecutePlayerPurchase(
            world, household, command.ItemId, quantity, price,
            SellableStock.ReserveQuantity(_definition, household, command.ItemId));

        int payment = checked((int)((long)quantity * price));
        player.Funds -= payment;
        player.Inventory[command.ItemId] += quantity;

        world.EventLog.Add(new PlayerTradeExecuted(
            world.Now, PlayerTradeKind.BuyFromHousehold, command.HouseholdId, command.ItemId,
            quantity, price, null, -1));

        return new Result
        {
            Status = CommandStatus.Accepted,
            Quantity = quantity,
            UnitPrice = price,
            Payment = payment,
        };
    }

    private Result BuyFromWindow(World world, BuyFromWindowCommand command)
    {
        if (!IsItemId(command.ItemId) || command.Quantity < 1)
        {
            return Reject(CommandStatus.InvalidArgument);
        }

        var rejection = CheckTransitHoursAndPresence(world, District.ExternalMarketDistrictId);

        if (rejection is not null)
        {
            return rejection;
        }

        // M0 では常に true(ExternalMarket.TryOfferPrice の remarks)。false の日は窓口に値が並ばない日。
        if (!ExternalMarket.TryOfferPrice(_definition, world.Now, command.ItemId, out int price))
        {
            return Reject(CommandStatus.NoOffer);
        }

        var player = world.Player;
        int affordable = IntegerMath.FloorDiv(player.Funds, price);

        if (affordable == 0)
        {
            return Reject(CommandStatus.InsufficientFunds);
        }

        int quantity = Math.Min(command.Quantity, affordable);
        int payment = checked((int)((long)quantity * price));

        // 帳簿に何も書かない(GDD06 §6.3。窓口は在庫・帳簿・流動資金を持たない)。
        player.Funds -= payment;
        player.Inventory[command.ItemId] += quantity;

        world.EventLog.Add(new PlayerTradeExecuted(
            world.Now, PlayerTradeKind.BuyFromWindow, HouseholdState.ExternalMarketSellerId, command.ItemId,
            quantity, price, null, -1));

        return new Result
        {
            Status = CommandStatus.Accepted,
            Quantity = quantity,
            UnitPrice = price,
            Payment = payment,
        };
    }

    private Result SellToWindow(World world, SellToWindowCommand command)
    {
        if (!IsItemId(command.ItemId) || command.Quantity < 1)
        {
            return Reject(CommandStatus.InvalidArgument);
        }

        var rejection = CheckTransitHoursAndPresence(world, District.ExternalMarketDistrictId);

        if (rejection is not null)
        {
            return rejection;
        }

        // ExternalBuyPrice は1次産品で投げるので、呼ぶ前に弾く。
        if (_definition.IsPrimaryItem(command.ItemId))
        {
            return Reject(CommandStatus.WindowDoesNotBuy);
        }

        var player = world.Player;
        int held = player.Inventory[command.ItemId];

        if (held == 0)
        {
            return Reject(CommandStatus.NotEnoughGoods);
        }

        int price = _definition.ExternalBuyPrice(command.ItemId);
        int quantity = Math.Min(command.Quantity, held);
        int payment = checked((int)((long)quantity * price));

        // 帳簿に何も書かない(GDD06 §6.3)。
        player.Funds = checked(player.Funds + payment);
        player.Inventory[command.ItemId] -= quantity;

        world.EventLog.Add(new PlayerTradeExecuted(
            world.Now, PlayerTradeKind.SellToWindow, HouseholdState.ExternalMarketSellerId, command.ItemId,
            quantity, price, null, -1));

        return new Result
        {
            Status = CommandStatus.Accepted,
            Quantity = quantity,
            UnitPrice = price,
            Payment = payment,
        };
    }
}
