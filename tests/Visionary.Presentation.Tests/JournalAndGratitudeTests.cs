using Visionary.Sim;
using Visionary.Sim.Commands;
using Visionary.Sim.Dialogue;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Presentation.Tests;

/// <summary>W5-09(日誌・表情・効果音)。「核心」印の変異の実測は mutator が行い、各テストの doc コメントに転記してある。</summary>
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

    /// <summary>
    /// 核心: 日誌に足す条件から渋々を除く変異。
    /// 実測(出所: mutator の実測(2026-11-03、HEAD cd7d9d7、Presentation.Tests 90件)、期待との食い違い 0 件):
    /// <c>Conversation.AppendJournal</c> の日誌に足す条件で渋々(<c>Gratitude.Reluctant</c>)を除く → 赤
    /// (本テスト1件だけが落ちた)。
    /// </summary>
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

    /// <summary>
    /// 核心: 日誌に足す条件の <c>Kind == SellToHousehold</c> の絞りを消す変異。
    /// 実測(出所: mutator の実測(2026-11-03、HEAD cd7d9d7、Presentation.Tests 90件)、期待との食い違い 0 件):
    /// 同じ条件の <c>Kind == SellToHousehold</c> の絞りを消す → 赤(本テストを含む13件が落ちた)。
    /// 買いの約定では <c>Gratitude</c> が null なので、多くは例外で落ちていると見られる(未確認)。
    /// </summary>
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

    /// <summary>
    /// R1(レビュー1巡目 I-a)。seed 1・tick 534 に窓口で鉄鉱石(2)を 1 個買い、世帯 3 の区画へ移って話しかける。
    /// 開示は #63(ビール、品切れ)・#70(鉄鉱石、材料切れ)・#71(木炭、品切れ)で、売る相手の #70 は先頭でなく、
    /// 先頭と理由が異なる。先頭の Need の理由を使う・理由を固定する実装を落とす。
    /// 品目で引く実装は、この筋書きでは品目と Need が 1 対 1 なので落とせない(既知)。
    /// </summary>
    [Fact]
    public void Sell_ReasonIsLookedUpByNeedId_NotFirstNeedOrFixed()
    {
        var host = new SimHost(WorldDefinition.M0, Seed);
        var session = new PlayerSession(host, WorldDefinition.M0, new TickPump(), Seed, DialogueCorpus.M0);

        host.Advance(534);

        var window = session.OpenWindow();
        Assert.Equal(CommandStatus.Accepted, window.Buy(2, 1).Status);
        session.Close();

        var move = session.Move(host.Query.Households()[3].DistrictId);
        Assert.Equal(CommandStatus.Accepted, move!.Status);
        host.Advance((int)(move.ArrivalAt!.Value.Value - host.Now.Value));

        Assert.Equal(CommandStatus.Accepted, session.Talk(3).Status);
        var conversation = session.Conversation!;

        Assert.Equal([63, 70, 71], conversation.DisclosedNeeds.Select(n => n.Id).ToArray());
        Assert.Equal(NeedReason.DistantStock, conversation.DisclosedNeeds[0].ReasonCode);
        Assert.Equal(NeedReason.ProductionStopped, conversation.DisclosedNeeds[1].ReasonCode);

        Assert.Equal(CommandStatus.Accepted, conversation.Sell(2, 1, 1).Status);

        var entry = Assert.Single(session.Journal);
        Assert.Equal(70, entry.NeedId);
        Assert.Contains("の材料切れに", entry.Text, StringComparison.Ordinal);
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
