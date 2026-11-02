using Visionary.Sim;
using Visionary.Sim.Commands;
using Visionary.Sim.Dialogue;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Presentation.Tests;

/// <summary>W5-09(日誌・表情・効果音)。変異の実測は mutator の報告を待つ。</summary>
public class JournalAndGratitudeTests
{
    private const long Seed = 1;

    private sealed record Scene(SimHost Host, PlayerSession Session);

    /// <summary>筋書き S(seed 1)。PlayerSessionTests.ScenarioS と同じ。</summary>
    private static Scene ScenarioS(bool alsoBuyBeer = false)
    {
        var host = new SimHost(WorldDefinition.M0, Seed);
        var pump = new TickPump();
        var session = new PlayerSession(host, WorldDefinition.M0, pump, Seed, DialogueCorpus.M0);

        host.Advance(54);

        var window = session.OpenWindow();
        Assert.Equal(CommandStatus.Accepted, window.Buy(5, 19).Status);

        if (alsoBuyBeer)
        {
            Assert.Equal(CommandStatus.Accepted, window.Buy(7, 1).Status);
        }

        session.Close();
        Assert.Equal(CommandStatus.Accepted, session.Move(0)!.Status);
        host.Advance(2);
        Assert.Equal(CommandStatus.Accepted, session.Talk(1).Status);

        return new Scene(host, session);
    }

    private const string DeepText = "1年 春 3日 パン屋(世帯 1)の品切れに薪を売った — 深く感謝された";

    [Fact]
    public void Labels_NeedReasonAndGratitude_HaveSpecifiedTextsAndRejectOthers()
    {
        Assert.Equal("材料切れ", Labels.Of(NeedReason.ProductionStopped));
        Assert.Equal("工具切れ", Labels.Of(NeedReason.ToolsExhausted));
        Assert.Equal("品切れ", Labels.Of(NeedReason.DistantStock));
        Assert.Throws<ArgumentOutOfRangeException>(() => Labels.Of(NeedReason.Distress));
        Assert.Throws<ArgumentOutOfRangeException>(() => Labels.Of(NeedReason.CannotExpandProduction));
        Assert.Throws<ArgumentOutOfRangeException>(() => Labels.Of((NeedReason)99));

        Assert.Equal("渋々だった", Labels.Of(Gratitude.Reluctant));
        Assert.Equal("感謝された", Labels.Of(Gratitude.Grateful));
        Assert.Equal("深く感謝された", Labels.Of(Gratitude.DeepGratitude));
        Assert.Throws<ArgumentOutOfRangeException>(() => Labels.Of((Gratitude)99));
    }

    [Fact]
    public void NotesJournal_FormatsTheSpecifiedLine()
    {
        string text = Notes.Journal(
            GameDate.FromTick(new Tick(56)), Occupation.Baker, 1, NeedReason.DistantStock, 5, Gratitude.DeepGratitude);

        Assert.Equal(DeepText, text);
    }

    [Fact]
    public void Sell_AddsOneJournalLine()
    {
        var scene = ScenarioS();

        Assert.Equal(CommandStatus.Accepted, scene.Session.Conversation!.Sell(5, 1, 19).Status);

        var entry = Assert.Single(scene.Session.Journal);
        Assert.Equal(DeepText, entry.Text);
        Assert.Equal(56, entry.At.Value);
        Assert.Equal(1, entry.HouseholdId);
        Assert.Equal(3, entry.NeedId);
        Assert.Equal(Gratitude.DeepGratitude, entry.Gratitude);
    }

    [Fact]
    public void Sell_ReluctantAndGratefulAreAlsoJournaled()
    {
        var reluctant = ScenarioS();
        reluctant.Session.Conversation!.Sell(5, 17, 19);
        var entry = Assert.Single(reluctant.Session.Journal);
        Assert.Equal(Gratitude.Reluctant, entry.Gratitude);
        Assert.EndsWith("— 渋々だった", entry.Text, StringComparison.Ordinal);

        var grateful = ScenarioS();
        grateful.Session.Conversation!.Sell(5, 12, 19);
        entry = Assert.Single(grateful.Session.Journal);
        Assert.Equal(Gratitude.Grateful, entry.Gratitude);
        Assert.EndsWith("— 感謝された", entry.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Journal_ExcludesBuysAndRefusals_ThenSellAddsExactlyOne()
    {
        var scene = ScenarioS(alsoBuyBeer: true);
        var conversation = scene.Session.Conversation!;

        Assert.Empty(scene.Session.Journal);

        Assert.Equal(CommandStatus.Accepted, conversation.Buy(6, 1).Status);
        Assert.Empty(scene.Session.Journal);

        Assert.Equal(CommandStatus.OverBudget, conversation.Sell(5, 1_000_000, 19).Status);
        Assert.Empty(scene.Session.Journal);

        // 予算超過も (世帯, 品目) の「今日の値出し」に数えるので、同じ薪では続けて売れない。ビールで売る。
        Assert.Equal(CommandStatus.Accepted, conversation.Sell(7, 140, 1).Status);
        Assert.Single(scene.Session.Journal);
    }

    [Fact]
    public void Sell_TwoSalesInOneConversation_AreJournaledInOrderWithoutDuplicates()
    {
        var scene = ScenarioS(alsoBuyBeer: true);
        var conversation = scene.Session.Conversation!;

        Assert.Equal(Gratitude.DeepGratitude, conversation.Sell(5, 1, 19).Gratitude);
        Assert.Equal(Gratitude.Grateful, conversation.Sell(7, 140, 1).Gratitude);

        Assert.Equal(2, scene.Session.Journal.Count);
        Assert.Equal(3, scene.Session.Journal[0].NeedId);
        Assert.Equal(19, scene.Session.Journal[1].NeedId);
        Assert.Equal("1年 春 3日 パン屋(世帯 1)の品切れにビールを売った — 感謝された", scene.Session.Journal[1].Text);
    }

    [Fact]
    public void Journal_SurvivesCloseAndReopen()
    {
        var scene = ScenarioS(alsoBuyBeer: true);

        scene.Session.Conversation!.Sell(5, 1, 19);
        scene.Session.Close();

        Assert.Equal(CommandStatus.Accepted, scene.Session.Talk(1).Status);
        Assert.Single(scene.Session.Journal);

        // 拒否(予算超過)しても増えない。
        Assert.Equal(CommandStatus.OverBudget, scene.Session.Conversation!.Sell(7, 1_000_000, 1).Status);
        Assert.Single(scene.Session.Journal);
    }

    [Fact]
    public void Expression_FollowsSellResult()
    {
        var scene = ScenarioS();
        var conversation = scene.Session.Conversation!;

        Assert.Equal("ふつう", conversation.Expression);
        conversation.Sell(5, 1, 19);
        Assert.Equal("晴れやかな顔", conversation.Expression);

        var grateful = ScenarioS().Session.Conversation!;
        grateful.Sell(5, 12, 19);
        Assert.Equal("ほっとした顔", grateful.Expression);

        var reluctant = ScenarioS().Session.Conversation!;
        reluctant.Sell(5, 17, 19);
        Assert.Equal("渋い顔", reluctant.Expression);

        var refused = ScenarioS().Session.Conversation!;
        Assert.Equal(CommandStatus.OverBudget, refused.Sell(5, 1_000_000, 19).Status);
        Assert.Equal("首を振る", refused.Expression);

        // 台詞を出さない拒否では変えない。
        Assert.Equal(CommandStatus.NotEnoughGoods, refused.Sell(8, 1, 1).Status);
        Assert.Equal("首を振る", refused.Expression);
    }

    [Fact]
    public void TakeChime_ReturnsPendingOnce_RefusalMakesNone()
    {
        var scene = ScenarioS();
        var conversation = scene.Session.Conversation!;

        Assert.Null(conversation.TakeChime());
        conversation.Sell(5, 1, 19);
        Assert.Equal(Gratitude.DeepGratitude, conversation.TakeChime());
        Assert.Null(conversation.TakeChime());

        var refused = ScenarioS().Session.Conversation!;
        refused.Sell(5, 1_000_000, 19);
        Assert.Null(refused.TakeChime());
    }

    [Fact]
    public void Pcm16_HasSpecifiedLengthsAndShape()
    {
        var reluctant = Chime.Pcm16(Gratitude.Reluctant);
        var grateful = Chime.Pcm16(Gratitude.Grateful);
        var deep = Chime.Pcm16(Gratitude.DeepGratitude);

        Assert.Equal(8820, reluctant.Length);
        Assert.Equal(10584, grateful.Length);
        Assert.Equal(15876, deep.Length);
        Assert.NotEqual(reluctant, grateful);
        Assert.NotEqual(grateful, deep);
        Assert.NotEqual(reluctant, deep);
        Assert.Equal(reluctant, Chime.Pcm16(Gratitude.Reluctant));

        foreach (var pcm in new[] { reluctant, grateful, deep })
        {
            for (int i = 0; i < pcm.Length; i += 2)
            {
                short sample = (short)(pcm[i] | (pcm[i + 1] << 8));
                Assert.InRange(sample, -6000, 6000);
            }
        }

        Assert.Equal(6000, (short)(reluctant[0] | (reluctant[1] << 8)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Chime.Pcm16((Gratitude)99));
    }

    [Fact]
    public void Journal_DoesNotChangeWorld_SameHashAsDirectExecute()
    {
        var viaConversation = ScenarioS();
        var direct = ScenarioS();

        viaConversation.Session.Conversation!.Sell(5, 1, 19);
        direct.Host.Execute(new SellToHouseholdCommand(1, 5, 1, 19));

        Assert.Equal(direct.Host.StateHash(), viaConversation.Host.StateHash());
    }
}
