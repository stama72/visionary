using Visionary.Sim.Commands;
using Visionary.Sim.Dialogue;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Commands;

/// <summary>
/// プレイヤーのコマンド(W5-04 タスク仕様のテスト表 #1〜#20、TDD01 §3.4 / GDD06 §6)。
/// 世界は <c>SimHost(M0, シード 1)</c> を <c>Advance</c> で進めて作る。居場所・所持品・所持金だけは
/// internal の <c>World.Player</c> を直接書いて整える(時刻は <c>World.Now</c> を書かない)。
/// 日付の表記 <c>D1T06</c> は <c>Tick.ToString()</c> と同じ(1日目 6時)。
/// </summary>
public sealed class PlayerCommandTests
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

    private static HouseholdState HouseholdIn(SimHost host, int districtId) =>
        host.World.Households.First(household => household.DistrictId == districtId);

    private static void PlacePlayerIn(SimHost host, int districtId) => host.World.Player.DistrictId = districtId;

    /// <summary>開示される Need を持つ最初の世帯(Id 昇順)と、その先頭の Need。</summary>
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

    private static int LedgerRowCount(SimHost host) => host.World.Ledgers.Sum(entries => entries.Count);

    // ---- 1〜10 移動・時間帯・居合わせ・検査の順序 ----

    /// <summary>#1。D0T06 に 4→0。到着 = D0T08(距離 2 × 1時間/区画)。出発地の区画は書き換わらない。</summary>
    [Fact]
    public void MoveArrivalIsNowPlusDistanceTimesTravelHours()
    {
        var host = HostAt(6);

        var result = host.Execute(new MoveCommand(0));

        Assert.Equal(CommandStatus.Accepted, result.Status);
        Assert.Equal(8L, result.ArrivalAt!.Value.Value);
        var player = host.World.Player;
        Assert.Equal(8L, player.MoveArrivalTick);
        Assert.Equal(0, player.MoveDestinationDistrictId);
        Assert.Equal(Center, player.DistrictId);
    }

    /// <summary>
    /// 【核心】#2。D0T07(移動中)は <c>InTransit</c>、到着 tick ちょうどの D0T08 は通る。
    /// 到着の比較が <c>&gt;</c> だと D0T08 も <c>InTransit</c> になる。
    /// </summary>
    [Fact]
    public void ArrivalTickItselfCountsAsArrived()
    {
        var host = HostAt(6);
        int destination = 0;
        var household = HouseholdIn(host, destination);
        host.Execute(new MoveCommand(destination));

        host.Advance(1);
        Assert.Equal(CommandStatus.InTransit, host.Execute(new AskNeedsCommand(household.Id)).Status);

        host.Advance(1);
        Assert.Equal(8L, host.Now.Value);
        Assert.Equal(CommandStatus.Accepted, host.Execute(new AskNeedsCommand(household.Id)).Status);
    }

    /// <summary>
    /// 【核心】#3。移動中は出発地(中心)の窓口でも取引できない。検査の段2 を消すと、
    /// 出発地の <c>DistrictId</c> だけを見て Accepted になる。
    /// </summary>
    [Fact]
    public void NothingTradesWhileInTransitEvenAtTheDeparture()
    {
        var host = HostAt(6);
        host.Execute(new MoveCommand(0));
        host.Advance(1);

        var result = host.Execute(new BuyFromWindowCommand(Item.Grain, 1));

        Assert.Equal(CommandStatus.InTransit, result.Status);
        Assert.Equal(WorldDefinition.PlayerInitialFunds, host.World.Player.Funds);
        Assert.Equal(0, host.World.Player.Inventory[Item.Grain]);
    }

    /// <summary>#4。移動中の付け替えは拒否され、状態は変わらない。</summary>
    [Fact]
    public void MoveWhileInTransitIsRejectedAndChangesNothing()
    {
        var host = HostAt(6);
        host.Execute(new MoveCommand(0));
        host.Advance(1);
        ulong before = host.StateHash();

        var result = host.Execute(new MoveCommand(8));

        Assert.Equal(CommandStatus.InTransit, result.Status);
        Assert.Equal(before, host.StateHash());
    }

    /// <summary>
    /// #5。到着後の再移動は、出発地を行き先(0)に正規化してから距離を測る。行き先は 8 でなければならない
    /// (0→8 は距離 4 で D0T12、中心 4→8 は距離 2 で D0T10 ── 正規化を忘れると D0T10 になる)。
    /// </summary>
    [Fact]
    public void MoveAfterArrivalNormalizesTheDepartureDistrict()
    {
        var host = HostAt(6);
        host.Execute(new MoveCommand(0));
        host.Advance(2);

        var result = host.Execute(new MoveCommand(8));

        Assert.Equal(CommandStatus.Accepted, result.Status);
        Assert.Equal(0, host.World.Player.DistrictId);
        Assert.Equal(8, host.World.Player.MoveDestinationDistrictId);
        Assert.Equal(12L, host.World.Player.MoveArrivalTick);
        Assert.Equal(12L, result.ArrivalAt!.Value.Value);
    }

    /// <summary>#6。今の居場所への移動は <c>AlreadyThere</c>。到着済みの移動の行き先も「今の居場所」である。</summary>
    [Fact]
    public void MoveToTheCurrentLocationIsRejectedAndChangesNothing()
    {
        var host = HostAt(6);
        ulong before = host.StateHash();

        Assert.Equal(CommandStatus.AlreadyThere, host.Execute(new MoveCommand(Center)).Status);
        Assert.Equal(before, host.StateHash());

        host.Execute(new MoveCommand(0));
        host.Advance(2);
        ulong afterArrival = host.StateHash();

        Assert.Equal(CommandStatus.AlreadyThere, host.Execute(new MoveCommand(0)).Status);
        Assert.Equal(afterArrival, host.StateHash());
    }

    /// <summary>#7。移動は時間帯を見ない(D0T02 と D0T20 でも通る)。</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(20)]
    public void MoveIgnoresTheTradingHours(int hour)
    {
        var host = HostAt(hour);

        Assert.Equal(CommandStatus.Accepted, host.Execute(new MoveCommand(0)).Status);
    }

    /// <summary>
    /// 【核心】#8。時間帯の境界: D0T05 は拒否、D0T06 と D0T17 は通る、D0T18 は拒否。
    /// 段3 の <c>&lt; EveningTradeHour</c> を <c>&lt;=</c> にすると D0T18 が通る。
    /// </summary>
    [Fact]
    public void TradingHoursAreSixInclusiveToEighteenExclusive()
    {
        var host = HostAt(5);
        var buy = new BuyFromWindowCommand(Item.Grain, 1);

        Assert.Equal(CommandStatus.OutsideTradingHours, host.Execute(buy).Status);

        host.Advance(1);
        Assert.Equal(6, host.Now.HourOfDay);
        Assert.Equal(CommandStatus.Accepted, host.Execute(buy).Status);

        host.Advance(11);
        Assert.Equal(17, host.Now.HourOfDay);
        Assert.Equal(CommandStatus.Accepted, host.Execute(buy).Status);

        host.Advance(1);
        Assert.Equal(18, host.Now.HourOfDay);
        Assert.Equal(CommandStatus.OutsideTradingHours, host.Execute(buy).Status);
    }

    /// <summary>
    /// #9。居合わせ。中心に居て区画 0 の世帯へは <c>NotPresent</c>、移動して到着後は通る。
    /// 区画 0 に居るときの窓口は <c>NotPresent</c>(窓口の区画は 4)。
    /// </summary>
    [Fact]
    public void PresenceIsCheckedAgainstTheHouseholdDistrictOrTheCentreForTheWindow()
    {
        var host = HostAt(6);
        var household = HouseholdIn(host, 0);

        Assert.Equal(CommandStatus.NotPresent, host.Execute(new AskNeedsCommand(household.Id)).Status);

        host.Execute(new MoveCommand(0));
        host.Advance(2);

        Assert.Equal(CommandStatus.Accepted, host.Execute(new AskNeedsCommand(household.Id)).Status);
        Assert.Equal(CommandStatus.NotPresent, host.Execute(new BuyFromWindowCommand(Item.Grain, 1)).Status);
        Assert.Equal(CommandStatus.NotPresent, host.Execute(new SellToWindowCommand(Item.Bread, 1)).Status);
    }

    /// <summary>
    /// #10。検査の順序。移動中かつ D0T20 の <c>AskNeeds</c> は <c>InTransit</c>(<c>OutsideTradingHours</c> ではない)。
    /// 範囲外の世帯 Id は D0T20 でも <c>InvalidArgument</c>(引数の検査が先)。
    /// </summary>
    [Fact]
    public void ChecksRunInTheSpecifiedOrder()
    {
        var host = HostAt(19);
        var household = HouseholdIn(host, 0);
        host.Execute(new MoveCommand(0));
        host.Advance(1);
        Assert.Equal(20, host.Now.HourOfDay);

        Assert.Equal(CommandStatus.InTransit, host.Execute(new AskNeedsCommand(household.Id)).Status);
        Assert.Equal(
            CommandStatus.InvalidArgument,
            host.Execute(new AskNeedsCommand(Definition.HouseholdCount)).Status);
        Assert.Equal(CommandStatus.InvalidArgument, host.Execute(new AskNeedsCommand(-1)).Status);
    }

    // ---- 11 Need を聞く ----

    /// <summary>#11。Need を聞くは World を書かず、<c>DisclosedNeeds.Of</c> と同じ並びを返す。</summary>
    [Fact]
    public void AskNeedsWritesNothingAndReturnsTheDisclosedNeedsInOrder()
    {
        var host = HostAtDay1Morning();
        var (household, _) = FirstHouseholdWithDisclosedNeed(host);
        PlacePlayerIn(host, household.DistrictId);
        ulong before = host.StateHash();

        var result = host.Execute(new AskNeedsCommand(household.Id));

        Assert.Equal(CommandStatus.Accepted, result.Status);
        Assert.Equal(before, host.StateHash());
        var expected = DisclosedNeeds.Of(host.World, household.Id);
        Assert.NotEmpty(expected);
        Assert.Equal(expected, result.DisclosedNeeds);
    }

    // ---- 12〜14 世帯へ売る ----

    /// <summary>
    /// #12。成立。所持金が <c>Payment</c> 増え、所持品が結果の数量だけ減り、<c>Gratitude</c> が入る。
    /// 世帯側の変化(と全状態)は <c>PlayerSale.Execute</c> を直接呼んだときと一致する。
    /// 提示数量を Need の数量より多くして、<c>q</c> で減らす実装(Need の数量で切られたとき食い違う)を落とす。
    /// </summary>
    [Fact]
    public void SellToHouseholdMovesPlayerFundsAndGoodsByTheSaleResult()
    {
        var host = HostAtDay1Morning();
        var (household, need) = FirstHouseholdWithDisclosedNeed(host);
        int offered = need.Quantity + 5;
        int held = need.Quantity + 7;
        PlacePlayerIn(host, household.DistrictId);
        host.World.Player.Inventory[need.ItemId] = held;
        int fundsBefore = host.World.Player.Funds;

        var result = host.Execute(new SellToHouseholdCommand(household.Id, need.ItemId, 1, offered));

        Assert.Equal(CommandStatus.Accepted, result.Status);
        Assert.InRange(result.Quantity, 1, need.Quantity);
        Assert.Equal(result.Quantity, result.Payment);
        Assert.Equal(1, result.UnitPrice);
        Assert.NotNull(result.Gratitude);
        Assert.Equal(need.Id, result.NeedId);
        Assert.Equal(fundsBefore + result.Payment, host.World.Player.Funds);
        Assert.Equal(held - result.Quantity, host.World.Player.Inventory[need.ItemId]);

        // 直接呼んだ場合(世帯側の変化の基準)。プレイヤー側は結果の数量で手で合わせる。
        var reference = HostAtDay1Morning();
        int referenceOffered = Math.Min(offered, held);
        var direct = new PlayerSale(Definition).Execute(
            reference.World, household.Id, need.ItemId, 1, referenceOffered);
        reference.World.Player.DistrictId = household.DistrictId;
        reference.World.Player.Inventory[need.ItemId] = held - direct.Quantity;
        reference.World.Player.Funds += direct.Payment;

        Assert.Equal(direct.Quantity, result.Quantity);
        Assert.Equal(reference.StateHash(), host.StateHash());
    }

    /// <summary>
    /// #12(続き)。提示数量は所持品で切る(<c>q = min(Quantity, 所持品)</c>)。所持品を、PlayerSale が受ける上限
    /// (Need の数量と予算で決まる)より小さくして、約定数量が所持品ちょうどになり、所持品が 0 で負にならない。
    /// 上限は同じ世界で <c>PlayerSale</c> を直接呼んで測る。<c>offered</c> を所持品で切らないと、上限まで約定して負になる。
    /// </summary>
    [Fact]
    public void SellToHouseholdIsCutByTheHeldQuantity()
    {
        int requested = 1_000;

        // 上限が 2 以上になる (世帯, Need) を探す(所持品を 1 以上、上限未満にできる)。
        for (int householdId = 0; householdId < Definition.HouseholdCount; householdId++)
        {
            var probe = HostAtDay1Morning();
            var needs = DisclosedNeeds.Of(probe.World, householdId);

            if (needs.Count == 0)
            {
                continue;
            }

            var need = needs[0];
            int cap = new PlayerSale(Definition).Execute(probe.World, householdId, need.ItemId, 1, requested).Quantity;

            if (cap < 2)
            {
                continue;
            }

            var host = HostAtDay1Morning();
            int held = cap - 1;
            PlacePlayerIn(host, host.World.Households[householdId].DistrictId);
            host.World.Player.Inventory[need.ItemId] = held;

            Assert.True(held < cap);
            Assert.True(cap < requested);

            var result = host.Execute(new SellToHouseholdCommand(householdId, need.ItemId, 1, requested));

            Assert.Equal(CommandStatus.Accepted, result.Status);
            Assert.Equal(held, result.Quantity);
            Assert.Equal(0, host.World.Player.Inventory[need.ItemId]);
            Assert.Equal(WorldDefinition.PlayerInitialFunds + held, host.World.Player.Funds);
            return;
        }

        Assert.Fail("上限が 2 以上になる Need を持つ世帯が無い(値の問題の可能性)。");
    }

    /// <summary>
    /// #13。所持品 0 は <c>NotEnoughGoods</c> で、1日1回に数えない(<c>PlayerSale</c> を呼ばない)。
    /// 同じ日に所持品を持たせて売ると <c>AlreadyOfferedToday</c> ではなく判定まで進む。
    /// </summary>
    [Fact]
    public void SellWithoutGoodsDoesNotSpendTheOncePerDayAllowance()
    {
        var host = HostAtDay1Morning();
        var (household, need) = FirstHouseholdWithDisclosedNeed(host);
        PlacePlayerIn(host, household.DistrictId);
        ulong before = host.StateHash();
        var command = new SellToHouseholdCommand(household.Id, need.ItemId, 1, 1);

        Assert.Equal(CommandStatus.NotEnoughGoods, host.Execute(command).Status);
        Assert.Equal(before, host.StateHash());

        host.World.Player.Inventory[need.ItemId] = 3;

        Assert.Equal(CommandStatus.Accepted, host.Execute(command).Status);
    }

    /// <summary>
    /// #14。結果の写し。Need の無い (世帯, 品目) は <c>NoNeed</c>(<c>NeedId</c> = −1)、同じ日に2回目は
    /// <c>AlreadyOfferedToday</c>、予算を超える値は <c>OverBudget</c>。どれも所持金・所持品は不変。
    /// </summary>
    [Fact]
    public void SellRejectionsMapTheSaleOutcomeAndLeavePlayerUntouched()
    {
        var host = HostAtDay1Morning();
        var (household, need) = FirstHouseholdWithDisclosedNeed(host);
        PlacePlayerIn(host, household.DistrictId);
        var player = host.World.Player;
        player.Inventory[need.ItemId] = 20;

        // NoNeed: 同じ世帯でも Need の無い品目。
        int otherItem = Enumerable.Range(0, Definition.ItemCount).First(
            itemId => DisclosedNeeds.Of(host.World, household.Id).All(n => n.ItemId != itemId));
        player.Inventory[otherItem] = 5;
        var noNeed = host.Execute(new SellToHouseholdCommand(household.Id, otherItem, 1, 1));
        Assert.Equal(CommandStatus.NoNeed, noNeed.Status);
        Assert.Equal(-1, noNeed.NeedId);
        AssertPlayerUntouched(player, otherItem, 5);

        // OverBudget(1日1回に数える)。
        var over = host.Execute(new SellToHouseholdCommand(household.Id, need.ItemId, 1_000_000, 1));
        Assert.Equal(CommandStatus.OverBudget, over.Status);
        Assert.Equal(need.Id, over.NeedId);
        AssertPlayerUntouched(player, need.ItemId, 20);

        // 同じ (世帯, 品目) の2回目。
        var again = host.Execute(new SellToHouseholdCommand(household.Id, need.ItemId, 1, 1));
        Assert.Equal(CommandStatus.AlreadyOfferedToday, again.Status);
        Assert.Equal(need.Id, again.NeedId);
        AssertPlayerUntouched(player, need.ItemId, 20);
    }

    private static void AssertPlayerUntouched(PlayerState player, int itemId, int expectedHeld)
    {
        Assert.Equal(WorldDefinition.PlayerInitialFunds, player.Funds);
        Assert.Equal(expectedHeld, player.Inventory[itemId]);
    }

    // ---- 15〜17 世帯から買う ----

    /// <summary>世帯の出力品目(Market に提示価格が立つ品目)。</summary>
    private static int OutputItem(HouseholdState household) =>
        Definition.Recipes[(int)household.Occupation].Outputs[0].ItemId;

    /// <summary>D1T06 で提示価格があり販売在庫が 2 以上の最初の世帯(Id 昇順)。</summary>
    private static (HouseholdState Seller, int ItemId, int Price, int Stock) SellerWithStock(SimHost host)
    {
        foreach (var household in host.World.Households)
        {
            int itemId = OutputItem(household);

            if (host.World.Market.TryGetValue(new MarketKey(itemId, household.Id), out int price)
                && SellableStock.Of(Definition, household, itemId) >= 2)
            {
                return (household, itemId, price, SellableStock.Of(Definition, household, itemId));
            }
        }

        throw new InvalidOperationException("販売在庫のある売り手が居ない(値の問題の可能性)。");
    }

    /// <summary>
    /// #15。成立。数量 = min(要求, 販売在庫, 払える数)。売り手の帳簿に <c>Sale</c> 1行
    /// (相手 <c>PlayerId</c>・<c>OccurredAt == Now</c>・単価 = 提示価格)、流動資金 +、工房在庫 −。
    /// プレイヤーの所持金 −、所持品 +。
    /// </summary>
    [Fact]
    public void BuyFromHouseholdSettlesTheSellerSideAndMovesThePlayer()
    {
        var host = HostAtDay1Morning();
        var (seller, itemId, price, stock) = SellerWithStock(host);
        PlacePlayerIn(host, seller.DistrictId);
        var player = host.World.Player;
        int ledgerBefore = host.World.Ledgers[seller.Id].Count;
        int sellerFundsBefore = seller.LiquidFunds;
        int workshopBefore = seller.WorkshopInventory[itemId];
        int fundsBefore = player.Funds;

        var result = host.Execute(new BuyFromHouseholdCommand(seller.Id, itemId, 1_000));

        int expectedQuantity = Math.Min(stock, fundsBefore / price);
        Assert.Equal(CommandStatus.Accepted, result.Status);
        Assert.Equal(expectedQuantity, result.Quantity);
        Assert.Equal(price, result.UnitPrice);
        Assert.Equal(expectedQuantity * price, result.Payment);

        var row = host.World.Ledgers[seller.Id][^1];
        Assert.Equal(ledgerBefore + 1, host.World.Ledgers[seller.Id].Count);
        Assert.Equal(LedgerDirection.Sale, row.Direction);
        Assert.Equal(HouseholdState.PlayerId, row.CounterpartyId);
        Assert.Equal(host.Now, row.OccurredAt);
        Assert.Equal(price, row.UnitPrice);
        Assert.Equal(expectedQuantity, row.Quantity);
        Assert.Equal(itemId, row.ItemId);

        Assert.Equal(sellerFundsBefore + result.Payment, seller.LiquidFunds);
        Assert.Equal(workshopBefore - expectedQuantity, seller.WorkshopInventory[itemId]);
        Assert.Equal(fundsBefore - result.Payment, player.Funds);
        Assert.Equal(expectedQuantity, player.Inventory[itemId]);
    }

    /// <summary>
    /// #15(続き)。払える数で切られる。前提 払える数 &lt; 販売在庫 &lt; 要求 をテスト内で表明するので、
    /// 払える数を数量の min から抜くと販売在庫まで買えてしまい落ちる。
    /// </summary>
    [Fact]
    public void BuyFromHouseholdIsCutByAffordability()
    {
        var host = HostAtDay1Morning();
        var (seller, itemId, price, stock) = SellerWithStock(host);
        PlacePlayerIn(host, seller.DistrictId);
        int requested = stock + 5;
        int affordable = stock - 1;
        host.World.Player.Funds = (price * affordable) + price - 1;

        Assert.True(affordable >= 1);
        Assert.Equal(affordable, host.World.Player.Funds / price);
        Assert.True(affordable < stock);
        Assert.True(stock < requested);

        var result = host.Execute(new BuyFromHouseholdCommand(seller.Id, itemId, requested));

        Assert.Equal(CommandStatus.Accepted, result.Status);
        Assert.Equal(affordable, result.Quantity);
        Assert.Equal(price - 1, host.World.Player.Funds);
        Assert.Equal(affordable, host.World.Player.Inventory[itemId]);
    }

    /// <summary>
    /// #15(続き)。数量は工房在庫ではなく販売在庫(留保を除く)で切る。工具は留保 1 個。要求が多くても
    /// 販売在庫ちょうどまでで、工房在庫を直読みすると留保を割って番人が投げる。
    /// </summary>
    [Fact]
    public void BuyFromHouseholdLeavesTheReservedStock()
    {
        var host = HostAtDay1Morning();
        var smith = host.World.Households.First(household =>
            household.Occupation == Occupation.Smith
            && host.World.Market.ContainsKey(new MarketKey(Item.Tools, household.Id))
            && household.WorkshopInventory[Item.Tools] >= 2);
        PlacePlayerIn(host, smith.DistrictId);
        int stock = SellableStock.Of(Definition, smith, Item.Tools);
        int price = host.World.Market[new MarketKey(Item.Tools, smith.Id)];
        int affordable = host.World.Player.Funds / price;
        Assert.True(stock >= 1);

        var result = host.Execute(new BuyFromHouseholdCommand(smith.Id, Item.Tools, 100));

        Assert.Equal(CommandStatus.Accepted, result.Status);
        Assert.Equal(Math.Min(stock, affordable), result.Quantity);
        Assert.Equal(
            SellableStock.ReserveQuantity(Definition, smith, Item.Tools),
            smith.WorkshopInventory[Item.Tools]);
    }

    /// <summary>
    /// #16。拒否の3種(提示価格なし / 販売在庫 0 / 払えない)は World を一切書かない。
    /// </summary>
    [Fact]
    public void BuyFromHouseholdRejectionsWriteNothing()
    {
        var host = HostAtDay1Morning();
        var (seller, itemId, price, _) = SellerWithStock(host);
        PlacePlayerIn(host, seller.DistrictId);
        ulong before = host.StateHash();

        // 提示価格が無い: 売り手の出力品目ではない品目(Market に載らない)。
        int notOffered = Enumerable.Range(0, Definition.ItemCount).First(
            candidate => candidate != itemId
                && !host.World.Market.ContainsKey(new MarketKey(candidate, seller.Id)));
        Assert.Equal(CommandStatus.NoOffer, host.Execute(new BuyFromHouseholdCommand(seller.Id, notOffered, 1)).Status);
        Assert.Equal(before, host.StateHash());

        // 払えない: 所持金が価格に1足りない。
        host.World.Player.Funds = price - 1;
        before = host.StateHash();
        Assert.Equal(CommandStatus.InsufficientFunds, host.Execute(new BuyFromHouseholdCommand(seller.Id, itemId, 1)).Status);
        Assert.Equal(before, host.StateHash());

        // 販売在庫 0: 工房在庫を留保ちょうどまで減らす。
        host.World.Player.Funds = WorldDefinition.PlayerInitialFunds;
        seller.WorkshopInventory[itemId] = SellableStock.ReserveQuantity(Definition, seller, itemId);
        before = host.StateHash();
        Assert.Equal(CommandStatus.SellerOutOfStock, host.Execute(new BuyFromHouseholdCommand(seller.Id, itemId, 1)).Status);
        Assert.Equal(before, host.StateHash());
    }

    /// <summary>#17。世帯から買っても観測を作らず、Market も書かない(GDD06 §6.2)。</summary>
    [Fact]
    public void BuyFromHouseholdCreatesNoObservationsAndLeavesTheMarket()
    {
        var host = HostAtDay1Morning();
        var (seller, itemId, _, _) = SellerWithStock(host);
        PlacePlayerIn(host, seller.DistrictId);
        var knowledgeBefore = host.World.Knowledge.Select(list => list.Count).ToArray();
        var marketBefore = host.World.Market.ToArray();

        var result = host.Execute(new BuyFromHouseholdCommand(seller.Id, itemId, 1));

        Assert.Equal(CommandStatus.Accepted, result.Status);
        Assert.Equal(knowledgeBefore, host.World.Knowledge.Select(list => list.Count).ToArray());
        Assert.Equal(marketBefore, host.World.Market.ToArray());
    }

    // ---- 18〜19 窓口 ----

    /// <summary>
    /// #18。窓口から買う。1次産品(穀物)の単価はその日の季節の <c>ExternalSellPrice</c>(春と夏で違う)。
    /// 所持金で切られる。どの世帯の帳簿も増えない。
    /// </summary>
    [Fact]
    public void BuyFromWindowUsesTheSeasonalPriceCutsByFundsAndWritesNoLedger()
    {
        var host = HostAtDay1Morning();
        int springPrice = Definition.ExternalSellPrice(Item.Grain, Season.Spring);
        int summerPrice = Definition.ExternalSellPrice(Item.Grain, Season.Summer);
        Assert.NotEqual(springPrice, summerPrice);
        Assert.Equal(Season.Spring, GameDate.FromTick(host.Now).Season);
        int ledgerRows = LedgerRowCount(host);

        var spring = host.Execute(new BuyFromWindowCommand(Item.Grain, 3));

        Assert.Equal(CommandStatus.Accepted, spring.Status);
        Assert.Equal(springPrice, spring.UnitPrice);
        Assert.Equal(3, spring.Quantity);
        Assert.Equal(3 * springPrice, spring.Payment);
        Assert.Equal(3, host.World.Player.Inventory[Item.Grain]);
        Assert.Equal(WorldDefinition.PlayerInitialFunds - spring.Payment, host.World.Player.Funds);
        Assert.Equal(ledgerRows, LedgerRowCount(host));

        host.Advance(30 * 24);
        Assert.Equal(Season.Summer, GameDate.FromTick(host.Now).Season);
        ledgerRows = LedgerRowCount(host);

        var summer = host.Execute(new BuyFromWindowCommand(Item.Grain, 1));

        Assert.Equal(summerPrice, summer.UnitPrice);
        Assert.Equal(ledgerRows, LedgerRowCount(host));

        host.World.Player.Funds = (2 * summerPrice) + 3;
        var cut = host.Execute(new BuyFromWindowCommand(Item.Grain, 10));

        Assert.Equal(2, cut.Quantity);
        Assert.Equal(3, host.World.Player.Funds);

        Assert.Equal(
            CommandStatus.InsufficientFunds,
            host.Execute(new BuyFromWindowCommand(Item.Grain, 1)).Status);
    }

    /// <summary>
    /// #19。窓口へ売る。都市生産品は単価 = <c>ExternalBuyPrice</c>(季節に依らない)で所持金 +、所持品 −。
    /// 1次産品は <c>WindowDoesNotBuy</c>(所持品 0 でも <c>NotEnoughGoods</c> より先)。帳簿は増えない。
    /// </summary>
    [Fact]
    public void SellToWindowPaysTheExternalBuyPriceAndRefusesPrimaryItems()
    {
        var host = HostAtDay1Morning();
        var player = host.World.Player;
        player.Inventory[Item.Bread] = 5;
        int ledgerRows = LedgerRowCount(host);

        var result = host.Execute(new SellToWindowCommand(Item.Bread, 3));

        Assert.Equal(CommandStatus.Accepted, result.Status);
        Assert.Equal(Definition.ExternalBuyPrice(Item.Bread), result.UnitPrice);
        Assert.Equal(3, result.Quantity);
        Assert.Equal(WorldDefinition.PlayerInitialFunds + result.Payment, player.Funds);
        Assert.Equal(2, player.Inventory[Item.Bread]);
        Assert.Equal(ledgerRows, LedgerRowCount(host));

        // 要求が所持品より多いときは所持品まで。
        Assert.Equal(2, host.Execute(new SellToWindowCommand(Item.Bread, 10)).Quantity);

        Assert.Equal(CommandStatus.NotEnoughGoods, host.Execute(new SellToWindowCommand(Item.Bread, 1)).Status);

        ulong before = host.StateHash();
        Assert.Equal(CommandStatus.WindowDoesNotBuy, host.Execute(new SellToWindowCommand(Item.Grain, 1)).Status);
        Assert.Equal(before, host.StateHash());
    }

    // ---- 20 記帳時刻 ----

    /// <summary>
    /// #20。D0T10 に世帯 s から買う。s の帳簿の当日の行は <c>OccurredAt</c> が非減少で、プレイヤーとの行は
    /// D0T10、NPC との行は D0T18。s は「同じシードで D0T18 に NPC との行が立つ世帯」から選ぶ
    /// (立たない世帯だと非減少が空虚に通る)。コマンドが記帳時刻を当日 18時にすると、プレイヤーの行が D0T10 でなくなる。
    /// </summary>
    [Fact]
    public void PlayerLedgerRowsKeepTheirOwnTimestampAndTheDayStaysNonDecreasing()
    {
        // 基準の走行(コマンドなし)で、D0T18 に NPC との Sale 行が立つ世帯を選ぶ。
        var reference = HostAt(24);
        var seller = reference.World.Households.First(household =>
            reference.World.Ledgers[household.Id].Any(entry =>
                entry.Direction == LedgerDirection.Sale
                && entry.CounterpartyId != HouseholdState.ExternalMarketSellerId
                && entry.OccurredAt.Value == 18));

        var host = HostAt(10);
        var sellerNow = host.World.Households[seller.Id];
        int itemId = OutputItem(sellerNow);
        PlacePlayerIn(host, sellerNow.DistrictId);

        var result = host.Execute(new BuyFromHouseholdCommand(seller.Id, itemId, 1));
        Assert.Equal(CommandStatus.Accepted, result.Status);

        host.Advance(14);
        Assert.Equal(24L, host.Now.Value);

        var rows = host.World.Ledgers[seller.Id].Where(entry => entry.OccurredAt.DayIndex == 0).ToArray();
        var playerRows = rows.Where(entry => entry.CounterpartyId == HouseholdState.PlayerId).ToArray();
        var npcRows = rows.Where(entry =>
            entry.CounterpartyId != HouseholdState.PlayerId
            && entry.CounterpartyId != HouseholdState.ExternalMarketSellerId
            && entry.Direction == LedgerDirection.Sale).ToArray();

        // 空虚に通らない前提: NPC との行が実際に立っている。
        Assert.NotEmpty(npcRows);
        Assert.Single(playerRows);
        Assert.Equal(10L, playerRows[0].OccurredAt.Value);
        Assert.All(npcRows, entry => Assert.Equal(18L, entry.OccurredAt.Value));

        for (int i = 1; i < rows.Length; i++)
        {
            Assert.True(rows[i - 1].OccurredAt <= rows[i].OccurredAt, $"行 {i} で記帳時刻が減っている。");
        }
    }
}
