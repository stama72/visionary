using Visionary.Sim;
using Visionary.Sim.Commands;
using Visionary.Sim.Dialogue;
using Visionary.Sim.Systems;

namespace Visionary.Presentation.Tests;

public class PlayerSessionTests
{
    private const long Seed = 1;

    private sealed record Scene(SimHost Host, TickPump Pump, PlayerSession Session);

    private static Scene Fresh()
    {
        var host = new SimHost(WorldDefinition.M0, Seed);
        var pump = new TickPump();

        return new Scene(host, pump, new PlayerSession(host, WorldDefinition.M0, pump, Seed, DialogueCorpus.M0));
    }

    /// <summary>筋書き S(seed 1)。窓口で薪 19 個を買い、区画 0 へ移動して世帯 1(パン屋)に話しかけた直後。</summary>
    private static Scene ScenarioS(bool alsoBuyBeer = false)
    {
        var scene = Fresh();

        scene.Host.Advance(54);

        var window = scene.Session.OpenWindow();
        var buy = window.Buy(5, 19);
        Assert.Equal(CommandStatus.Accepted, buy.Status);
        Assert.Equal(19, buy.Quantity);
        Assert.Equal(20, buy.UnitPrice);

        // 取引の台詞を 2 回言わせる #16 用。世帯 1 の 2 件目の Need(ビール 1 個)を解ける品を持たせる。
        if (alsoBuyBeer)
        {
            Assert.Equal(CommandStatus.Accepted, window.Buy(7, 1).Status);
        }

        scene.Session.Close();

        var move = scene.Session.Move(0);
        Assert.Equal(CommandStatus.Accepted, move!.Status);
        Assert.Equal(56, move.ArrivalAt!.Value.Value);

        scene.Host.Advance(2);

        var talk = scene.Session.Talk(1);
        Assert.Equal(CommandStatus.Accepted, talk.Status);

        return scene;
    }

    [Fact]
    public void Entries_AtStart_ListsHouseholdThenWindow()
    {
        var scene = Fresh();
        var household0 = scene.Host.Query.Households()[0];

        Assert.Equal(
            [
                new DistrictEntry(DistrictEntryKind.Household, 0, $"{Labels.Of(household0.Occupation)}(世帯 0)"),
                new DistrictEntry(DistrictEntryKind.Window, -1, "窓口"),
            ],
            scene.Session.Entries());
    }

    [Fact]
    public void Entries_InTransitIsEmpty_AfterArrivalListsDestinationHouseholdsWithoutWindow()
    {
        var scene = Fresh();

        scene.Session.Move(0);
        Assert.Empty(scene.Session.Entries());

        scene.Host.Advance(2);
        Assert.Equal(
            [new DistrictEntry(DistrictEntryKind.Household, 1, "パン屋(世帯 1)")],
            scene.Session.Entries());
    }

    [Fact]
    public void Entries_StaysTheSameWhilePanelIsOpen()
    {
        var scene = Fresh();
        var before = scene.Session.Entries();

        scene.Session.OpenWindow();

        Assert.Equal(before, scene.Session.Entries());
    }

    /// <summary>
    /// 核心: <c>OpenWindow</c> の <c>pump.Paused = true</c> を消す変異と、<c>Close</c> の <c>pump.Paused = false</c> を消す変異。
    /// 実測(出所: mutator の実測(a608b36、2026-10-29)、期待との食い違い 0 件):
    /// M1 <c>OpenWindow</c> の <c>_pump.Paused = true</c> を削除 → 赤(本テストが落ちた)。
    /// M2 <c>Close</c> の <c>_pump.Paused = false</c> を削除 → 赤(本テストと
    /// <c>Pause_OpenConversationPausesAndClose_Resumes</c> が落ちた)。
    /// </summary>
    [Fact]
    public void Pause_RefusedTalkDoesNotPause_WindowPausesAndCloseResumes()
    {
        var scene = Fresh();
        var loop = new GameLoop(scene.Host, scene.Pump);

        // 0時なので区画 4 の世帯への話しかけは時間帯で拒否される。
        var refused = scene.Session.Talk(0);
        Assert.Equal(CommandStatus.OutsideTradingHours, refused.Status);
        Assert.Null(scene.Session.Conversation);
        Assert.False(scene.Pump.Paused);

        // 何も開いていない Close は Paused を変えない。
        scene.Pump.Paused = true;
        scene.Session.Close();
        Assert.True(scene.Pump.Paused);
        scene.Pump.Paused = false;

        scene.Session.OpenWindow();
        Assert.True(scene.Pump.Paused);

        long now = scene.Host.Now.Value;
        Assert.Equal(0, loop.Frame(TickPump.DefaultMicrosecondsPerTick));
        Assert.Equal(now, scene.Host.Now.Value);

        scene.Session.Close();
        Assert.False(scene.Pump.Paused);
        Assert.Equal(1, loop.Frame(TickPump.DefaultMicrosecondsPerTick));
    }

    [Fact]
    public void Pause_OpenConversationPausesAndClose_Resumes()
    {
        var scene = ScenarioS();

        Assert.True(scene.Pump.Paused);
        Assert.True(scene.Session.IsPanelOpen);

        scene.Session.Close();

        Assert.False(scene.Pump.Paused);
        Assert.Null(scene.Session.Conversation);
        Assert.False(scene.Session.IsPanelOpen);
    }

    [Fact]
    public void Constructor_DoesNotChangePaused()
    {
        Assert.False(Fresh().Pump.Paused);
    }

    /// <summary>
    /// 核心: <c>Move</c> のパネルの検査を消す変異。
    /// 実測(出所: mutator の実測(a608b36、2026-10-29)): M3 <c>Move</c> の <c>IsPanelOpen</c> 検査を削除 → 赤(本テストが落ちた)。
    /// </summary>
    [Fact]
    public void Move_WhilePanelOpen_ReturnsNullAndDoesNotMove_AndReopeningThrows()
    {
        var scene = ScenarioS();

        Assert.Null(scene.Session.Move(4));

        var player = scene.Host.Query.Player();
        Assert.False(player.Location.InTransit);
        Assert.Equal(0, player.Location.DistrictId);

        Assert.Throws<InvalidOperationException>(() => scene.Session.Talk(1));
        Assert.Throws<InvalidOperationException>(() => scene.Session.OpenWindow());
    }

    [Fact]
    public void Talk_OpeningLines_AreGreetingSmallTalkAndTwoDisclosures_AndReproducible()
    {
        var scene = ScenarioS();
        var conversation = scene.Session.Conversation!;

        Assert.Equal(2, conversation.SpeakerNpcId);
        Assert.Equal([3, 19], conversation.DisclosedNeeds.Select(n => n.Id));
        Assert.Equal(
            [LineKind.Greeting, LineKind.SmallTalk, LineKind.NeedDisclosure, LineKind.NeedDisclosure],
            conversation.Lines.Select(l => l.Kind));
        Assert.Equal(
            ["greet.baker.-.01", "small.baker.-.02", "need.any.first.04", "need.any.cont.07"],
            conversation.Lines.Select(l => l.TemplateId));

        var again = ScenarioS().Session.Conversation!;
        Assert.Equal(conversation.Lines, again.Lines);
    }

    /// <summary>
    /// 核心: <c>Sell</c> の引き捨ての <c>NextUInt64()</c> を消す変異。
    /// 実測(出所: mutator の実測(a608b36、2026-10-29)): M4 <c>ReopenAfterSpokenLines</c> の引き捨てを 0 回に → 赤
    /// (本テストと <c>Sell_OverBudget_AddsRefusalLineAndNote_AndKeepsGoods</c>、
    /// <c>Sell_SecondTradeLine_MatchesASingleSequenceReference</c> が落ちた)。
    /// </summary>
    [Fact]
    public void Sell_Accepted_AddsGratitudeLineAndNoteAndPaysFunds()
    {
        var scene = ScenarioS();
        var conversation = scene.Session.Conversation!;
        int fundsBefore = scene.Host.Query.Player().Funds;

        var result = conversation.Sell(5, 1, 19);

        Assert.Equal(CommandStatus.Accepted, result.Status);
        Assert.Equal(19, result.Quantity);
        Assert.Equal(Gratitude.DeepGratitude, result.Gratitude);
        Assert.Equal(6, conversation.Lines.Count);
        Assert.Equal(LineKind.TradeAccepted, conversation.Lines[4].Kind);
        Assert.Equal("thanks.baker.deep.01", conversation.Lines[4].TemplateId);
        Assert.Equal(new ConversationLine(null, null, "薪 × 19 を単価 1 で売った(計 19)"), conversation.Lines[5]);
        Assert.Equal(fundsBefore + 19, scene.Host.Query.Player().Funds);
        Assert.Equal(2020 + 19, scene.Host.Query.Player().Funds);
    }

    [Fact]
    public void Sell_OverBudget_AddsRefusalLineAndNote_AndKeepsGoods()
    {
        var scene = ScenarioS();
        var conversation = scene.Session.Conversation!;

        var result = conversation.Sell(5, 1_000_000, 19);

        Assert.Equal(CommandStatus.OverBudget, result.Status);
        Assert.Equal(LineKind.TradeRefused, conversation.Lines[4].Kind);
        Assert.Equal("refuse.baker.-.02", conversation.Lines[4].TemplateId);
        Assert.Equal(new ConversationLine(null, null, "予算を超える"), conversation.Lines[5]);
        Assert.Equal(19, scene.Host.Query.Player().Inventory[5]);
    }

    /// <summary>
    /// 核心: 引き捨ての数を <c>_lines.Count</c>(地の文も数える)にする変異と、会話を開いた時点の台詞の行数で固定する変異。
    /// 参照は同じ鍵で列を 1 回だけ開き、同じ順に Say して作る。
    /// 実測(出所: mutator の実測(a608b36、2026-10-29)、期待との食い違い 0 件):
    /// M5 引き捨て数を <c>_lines.Count</c>(地の文込み)に → 赤(本テストだけが落ちた)。
    /// M6 引き捨て数を開いた時点の台詞の行数で固定 → 赤(本テストだけが落ちた)。
    /// M7 台詞 1 行ごとに <c>OpenConversation</c> を開き直す → 赤(本テストと
    /// <c>Talk_OpeningLines_AreGreetingSmallTalkAndTwoDisclosures_AndReproducible</c> が落ちた)。
    /// </summary>
    [Fact]
    public void Sell_SecondTradeLine_MatchesASingleSequenceReference()
    {
        var scene = ScenarioS(alsoBuyBeer: true);
        var conversation = scene.Session.Conversation!;

        var refused = conversation.Sell(5, 1_000_000, 19);
        Assert.Equal(CommandStatus.OverBudget, refused.Status);

        // 不成立も同じ (世帯, 品目) の「1日1回」を消費する(同じ薪では AlreadyOfferedToday になる)。
        // そのため 2 回目は別の品目(ビール)で作る。
        var accepted = conversation.Sell(7, 1, 1);
        Assert.Equal(CommandStatus.Accepted, accepted.Status);
        // 台詞の行は 0〜3(頭の 4 行)、4(不成立)、そして地の文を挟んだ 6(成立)。
        var speech = conversation.Lines.Where(l => l.Kind != null).ToList();
        Assert.Equal(6, speech.Count);

        var household = scene.Host.Query.Households()[1];
        var season = Visionary.Sim.Time.GameDate.FromTick(scene.Host.Now).Season;
        var memory = new DialogueMemory();
        var random = DialogueSelector.OpenConversation(Seed, scene.Host.Now, conversation.SpeakerNpcId);

        DialogueContext Context(Gratitude? g, DisclosureOrder? o, int? item, int? qty) =>
            new(household.Occupation, season, null, g, o, item, qty);

        var needs = conversation.DisclosedNeeds;
        var expected = new List<string>
        {
            DialogueSelector.Say(DialogueCorpus.M0, memory, ref random, conversation.SpeakerNpcId, LineKind.Greeting, Context(null, null, null, null)).TemplateId,
            DialogueSelector.Say(DialogueCorpus.M0, memory, ref random, conversation.SpeakerNpcId, LineKind.SmallTalk, Context(null, null, null, null)).TemplateId,
            DialogueSelector.Say(DialogueCorpus.M0, memory, ref random, conversation.SpeakerNpcId, LineKind.NeedDisclosure, Context(null, DisclosureOrder.First, needs[0].ItemId, needs[0].Quantity)).TemplateId,
            DialogueSelector.Say(DialogueCorpus.M0, memory, ref random, conversation.SpeakerNpcId, LineKind.NeedDisclosure, Context(null, DisclosureOrder.Continued, needs[1].ItemId, needs[1].Quantity)).TemplateId,
            DialogueSelector.Say(DialogueCorpus.M0, memory, ref random, conversation.SpeakerNpcId, LineKind.TradeRefused, Context(null, null, 5, null)).TemplateId,
            DialogueSelector.Say(DialogueCorpus.M0, memory, ref random, conversation.SpeakerNpcId, LineKind.TradeAccepted, Context(accepted.Gratitude, null, 7, accepted.Quantity)).TemplateId,
        };

        Assert.Equal(expected, speech.Select(l => l.TemplateId!));
    }

    [Fact]
    public void Window_AfterCrossingSeasons_PricesFollowTheCurrentSeason()
    {
        var scene = Fresh();
        var spring = scene.Host.Now;

        scene.Host.Advance(30 * 24);

        var options = scene.Session.OpenWindow().BuyOptions();
        bool anyDiffersFromSpring = false;

        Assert.Equal(9, options.Count);
        foreach (var option in options)
        {
            Assert.True(ExternalMarket.TryOfferPrice(WorldDefinition.M0, scene.Host.Now, option.ItemId, out int now));
            Assert.Equal(now, option.UnitPrice);

            Assert.True(ExternalMarket.TryOfferPrice(WorldDefinition.M0, spring, option.ItemId, out int then));
            anyDiffersFromSpring |= then != now;
        }

        Assert.True(anyDiffersFromSpring);
    }

    [Fact]
    public void Sell_OtherRejection_AddsOnlyANote()
    {
        var scene = ScenarioS();
        var conversation = scene.Session.Conversation!;

        var result = conversation.Sell(8, 1, 1);

        Assert.Equal(CommandStatus.NotEnoughGoods, result.Status);
        Assert.Equal(5, conversation.Lines.Count);
        Assert.Null(conversation.Lines[4].Kind);
        Assert.Equal(Labels.Of(CommandStatus.NotEnoughGoods), conversation.Lines[4].Text);
    }

    [Fact]
    public void SellOptions_DefaultPriceIsLastPurchaseUnitPrice_OrOneWhenBoughtOutsideTheSession()
    {
        var scene = ScenarioS();

        Assert.Equal([new SellOption(5, 19, 20)], scene.Session.Conversation!.SellOptions());

        // セッションを通さずに持たせたときは「買った単価」を知らないので 1。
        var host = new SimHost(WorldDefinition.M0, Seed);
        var pump = new TickPump();
        var session = new PlayerSession(host, WorldDefinition.M0, pump, Seed, DialogueCorpus.M0);

        host.Advance(54);
        Assert.Equal(CommandStatus.Accepted, host.Execute(new BuyFromWindowCommand(5, 19)).Status);
        session.Move(0);
        host.Advance(2);
        Assert.Equal(CommandStatus.Accepted, session.Talk(1).Status);

        Assert.Equal([new SellOption(5, 19, 1)], session.Conversation!.SellOptions());
    }

    [Fact]
    public void BuyFromHousehold_ListsOnlyThisHouseholdsOffers_AndRecordsPurchasePrice()
    {
        var scene = ScenarioS();
        var conversation = scene.Session.Conversation!;

        Assert.Equal([new OfferView(6, 1, 0, 94, 10)], conversation.BuyOptions());
        Assert.All(conversation.BuyOptions(), o => Assert.Equal(1, o.HouseholdId));

        var result = conversation.Buy(6, 1);

        Assert.Equal(CommandStatus.Accepted, result.Status);
        Assert.Equal(1, result.Quantity);
        Assert.Equal(94, result.UnitPrice);
        Assert.Equal(1, scene.Host.Query.Player().Inventory[6]);
        Assert.Equal("パン × 1 を単価 94 で買った(計 94)", conversation.Lines[^1].Text);
        Assert.Equal(94, conversation.SellOptions().Single(o => o.ItemId == 6).DefaultUnitPrice);
    }

    [Fact]
    public void Window_AtStart_OffersEveryItemAndSellsNothingAndRefusesOutsideHours()
    {
        var scene = Fresh();
        var window = scene.Session.OpenWindow();
        var buyOptions = window.BuyOptions();

        Assert.Equal(9, buyOptions.Count);
        for (int itemId = 0; itemId < 9; itemId++)
        {
            Assert.True(ExternalMarket.TryOfferPrice(WorldDefinition.M0, scene.Host.Now, itemId, out int price));
            Assert.Equal(itemId, buyOptions[itemId].ItemId);
            Assert.Equal(price, buyOptions[itemId].UnitPrice);
        }

        Assert.Empty(window.SellOptions());

        var result = window.Buy(5, 1);
        Assert.Equal(CommandStatus.OutsideTradingHours, result.Status);
        Assert.Equal("取引時間外", window.Lines[^1].Text);
    }

    [Fact]
    public void Window_AfterBuyingFirewood_SellsOnlyThatAtTheExternalBuyPrice()
    {
        var scene = Fresh();

        scene.Host.Advance(54);

        var window = scene.Session.OpenWindow();
        Assert.Equal(CommandStatus.Accepted, window.Buy(5, 19).Status);

        Assert.Equal(
            [new WindowPrice(5, WorldDefinition.M0.ExternalBuyPrice(5), 19)],
            window.SellOptions());
    }

    [Fact]
    public void ClosedPanels_RefuseTradesWithoutWritingTheWorld()
    {
        var scene = ScenarioS();
        var conversation = scene.Session.Conversation!;

        scene.Session.Close();

        ulong hash = scene.Host.StateHash();
        var player = scene.Host.Query.Player();

        Assert.Throws<InvalidOperationException>(() => conversation.Sell(5, 1, 19));
        Assert.Throws<InvalidOperationException>(() => conversation.Buy(6, 1));
        AssertUnchanged(scene, hash, player);

        var window = scene.Session.OpenWindow();
        scene.Session.Close();

        Assert.Throws<InvalidOperationException>(() => window.Buy(5, 1));
        Assert.Throws<InvalidOperationException>(() => window.Sell(5, 1));
        AssertUnchanged(scene, hash, player);
    }

    [Fact]
    public void TalkingThenClosing_DoesNotChangeTheWorld()
    {
        var scene = Fresh();

        scene.Host.Advance(54);
        scene.Session.Move(0);
        scene.Host.Advance(2);

        ulong before = scene.Host.StateHash();

        Assert.Equal(CommandStatus.Accepted, scene.Session.Talk(1).Status);
        scene.Session.Close();

        Assert.Equal(before, scene.Host.StateHash());
    }

    [Fact]
    public void Constructor_Null_Throws()
    {
        var host = new SimHost(WorldDefinition.M0, Seed);
        var pump = new TickPump();

        Assert.Throws<ArgumentNullException>(() => new PlayerSession(null!, WorldDefinition.M0, pump, Seed, DialogueCorpus.M0));
        Assert.Throws<ArgumentNullException>(() => new PlayerSession(host, null!, pump, Seed, DialogueCorpus.M0));
        Assert.Throws<ArgumentNullException>(() => new PlayerSession(host, WorldDefinition.M0, null!, Seed, DialogueCorpus.M0));
        Assert.Throws<ArgumentNullException>(() => new PlayerSession(host, WorldDefinition.M0, pump, Seed, null!));
    }

    private static void AssertUnchanged(Scene scene, ulong hash, PlayerView player)
    {
        var now = scene.Host.Query.Player();

        Assert.Equal(hash, scene.Host.StateHash());
        Assert.Equal(player.Funds, now.Funds);
        Assert.Equal(player.Inventory, now.Inventory);
    }
}
