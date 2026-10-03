using Visionary.Sim.Dialogue;
using Visionary.Sim.Randomness;
using Visionary.Sim.Time;
using static Visionary.Sim.Tests.Dialogue.DialogueTestHelpers;

namespace Visionary.Sim.Tests.Dialogue;

/// <summary>
/// 台詞の照合と選択(W5-01 テスト表 #1〜#11、#15)。合成した小さな資料だけを使い、
/// <c>DialogueCorpus.M0</c> に依存しない。
/// </summary>
/// <remarks>
/// 「核心」印(#1・#5・#7)の変異は <c>mutator</c> が実測する(ADR-0013)。ここには実測の主張を書かない。
/// </remarks>
public sealed class DialogueSelectorTests
{
    private const long Seed = 1;

    /// <summary>会話を1回開いて1行だけ話す(会話ごとに tick を変える呼び方)。</summary>
    private static DialogueLine SayOnce(
        DialogueCorpus corpus,
        DialogueMemory memory,
        long seed,
        long tick,
        int npc,
        LineKind kind,
        DialogueContext context)
    {
        var random = DialogueSelector.OpenConversation(seed, new Tick(tick), npc);
        return DialogueSelector.Say(corpus, memory, ref random, npc, kind, in context);
    }

    private static List<string> Collect(
        DialogueCorpus corpus,
        DialogueMemory memory,
        long seed,
        int npc,
        LineKind kind,
        DialogueContext context,
        int count)
    {
        var ids = new List<string>();

        for (int i = 0; i < count; i++)
        {
            ids.Add(SayOnce(corpus, memory, seed, (i * 24) + 12, npc, kind, context).TemplateId);
        }

        return ids;
    }

    [Fact]
    public void CandidatesAreTheUnionOfGenericAndOccupationSpecific()
    {
        var corpus = Corpus(
            Template("A", LineKind.Greeting),
            Template("B", LineKind.Greeting, Occupation.Baker),
            Template("C", LineKind.Greeting, Occupation.Smith));

        for (long seed = 1; seed <= 20; seed++)
        {
            var ids = Collect(corpus, new DialogueMemory(), seed, 0, LineKind.Greeting, Plain(Occupation.Baker), 2);

            Assert.Equal(new[] { "A", "B" }, ids.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        }
    }

    [Fact]
    public void PersonalityAxisMatchesOnlyAnEqualValue()
    {
        var corpus = Corpus(
            Template("A", LineKind.Greeting),
            Template("B", LineKind.Greeting, personality: Personality.Taciturn));

        var withoutPersonality = Collect(
            corpus, new DialogueMemory(), Seed, 0, LineKind.Greeting, Plain(personality: null), 8);
        Assert.DoesNotContain("B", withoutPersonality);

        var withPersonality = Collect(
            corpus, new DialogueMemory(), Seed, 0, LineKind.Greeting, Plain(personality: Personality.Taciturn), 2);
        Assert.Equal(new[] { "A", "B" }, withPersonality.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void SeasonAxisFiltersSmallTalk()
    {
        var corpus = Corpus(
            Template("A", LineKind.SmallTalk),
            Template("B", LineKind.SmallTalk, season: Season.Spring));

        var summer = Collect(corpus, new DialogueMemory(), Seed, 0, LineKind.SmallTalk, Plain(season: Season.Summer), 8);
        Assert.DoesNotContain("B", summer);

        var spring = Collect(corpus, new DialogueMemory(), Seed, 0, LineKind.SmallTalk, Plain(season: Season.Spring), 2);
        Assert.Equal(new[] { "A", "B" }, spring.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void GratitudeSelectsOnlyTemplatesOfThatTier()
    {
        var corpus = Corpus(
            Template("rel1", LineKind.TradeAccepted, gratitude: Gratitude.Reluctant),
            Template("rel2", LineKind.TradeAccepted, gratitude: Gratitude.Reluctant),
            Template("gra1", LineKind.TradeAccepted, gratitude: Gratitude.Grateful),
            Template("gra2", LineKind.TradeAccepted, gratitude: Gratitude.Grateful),
            Template("deep1", LineKind.TradeAccepted, gratitude: Gratitude.DeepGratitude),
            Template("deep2", LineKind.TradeAccepted, gratitude: Gratitude.DeepGratitude));

        foreach (var (tier, prefix) in new[]
        {
            (Gratitude.Reluctant, "rel"), (Gratitude.Grateful, "gra"), (Gratitude.DeepGratitude, "deep"),
        })
        {
            var context = new DialogueContext(Occupation.Baker, Season.Spring, null, tier, Item.Tools, 1);
            var ids = Collect(corpus, new DialogueMemory(), Seed, 0, LineKind.TradeAccepted, context, 4);

            Assert.All(ids, id => Assert.StartsWith(prefix, id, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void UnusedTemplatesComeFirstWithinOneNpc()
    {
        var corpus = Corpus(Many(LineKind.Greeting, "g", 5));

        for (long seed = 1; seed <= 20; seed++)
        {
            var ids = Collect(corpus, new DialogueMemory(), seed, 1, LineKind.Greeting, Plain(), 5);

            Assert.Equal(5, ids.Distinct().Count());
        }
    }

    [Fact]
    public void UsageIsCountedPerNpc()
    {
        var corpus = Corpus(Many(LineKind.Greeting, "g", 5));

        for (long seed = 1; seed <= 20; seed++)
        {
            var memory = new DialogueMemory();
            Collect(corpus, memory, seed, 1, LineKind.Greeting, Plain(), 5);
            var second = Collect(corpus, memory, seed, 2, LineKind.Greeting, Plain(), 5);

            Assert.Equal(5, second.Distinct().Count());
        }
    }

    [Fact]
    public void NoImmediateRepeatAfterACycle()
    {
        var corpus = Corpus(Many(LineKind.Greeting, "g", 2));

        for (long seed = 1; seed <= 20; seed++)
        {
            var ids = Collect(corpus, new DialogueMemory(), seed, 1, LineKind.Greeting, Plain(), 20);

            for (int i = 1; i < ids.Count; i++)
            {
                Assert.NotEqual(ids[i - 1], ids[i]);
            }
        }
    }

    private static List<DialogueLine> Converse(DialogueCorpus corpus, long seed, long tick, int npc)
    {
        var memory = new DialogueMemory();
        var random = DialogueSelector.OpenConversation(seed, new Tick(tick), npc);
        var context = Plain();
        var lines = new List<DialogueLine>();

        foreach (var kind in new[] { LineKind.Greeting, LineKind.SmallTalk, LineKind.Greeting, LineKind.SmallTalk })
        {
            lines.Add(DialogueSelector.Say(corpus, memory, ref random, npc, kind, in context));
        }

        return lines;
    }

    [Fact]
    public void SameKeyGivesSameLines()
    {
        var corpus = Corpus(
            Many(LineKind.Greeting, "g", 6).Concat(Many(LineKind.SmallTalk, "s", 6)).ToArray());

        Assert.Equal(Converse(corpus, 7, 36, 3), Converse(corpus, 7, 36, 3));
    }

    [Fact]
    public void NpcsOfOneOccupationDoNotShareOneOrder()
    {
        var corpus = Corpus(Many(LineKind.Greeting, "g", 8));
        var orders = new List<string>();

        for (int npc = 0; npc < 10; npc++)
        {
            var ids = Collect(corpus, new DialogueMemory(), Seed, npc, LineKind.Greeting, Plain(), 8);
            orders.Add(string.Join(",", ids));
        }

        Assert.True(orders.Distinct().Count() > 1, "10人全員が同じ順で台詞を巡っている。");
    }

    [Fact]
    public void EveryLineDrawsExactlyOnce()
    {
        var smallTalks = Many(LineKind.SmallTalk, "s", 8);
        var one = Corpus(Many(LineKind.Greeting, "g", 1).Concat(smallTalks).ToArray());
        var three = Corpus(Many(LineKind.Greeting, "g", 3).Concat(smallTalks).ToArray());

        for (long seed = 1; seed <= 20; seed++)
        {
            Assert.Equal(SmallTalkAfterGreeting(one, seed), SmallTalkAfterGreeting(three, seed));
        }
    }

    private static string SmallTalkAfterGreeting(DialogueCorpus corpus, long seed)
    {
        var memory = new DialogueMemory();
        var random = DialogueSelector.OpenConversation(seed, new Tick(12), 4);
        var context = Plain();

        DialogueSelector.Say(corpus, memory, ref random, 4, LineKind.Greeting, in context);
        return DialogueSelector.Say(corpus, memory, ref random, 4, LineKind.SmallTalk, in context).TemplateId;
    }

    [Fact]
    public void ConversationUsesTheDialogueStream()
    {
        const long seed = 42;
        var tick = new Tick(60);

        var conversation = DialogueSelector.OpenConversation(seed, tick, 9);
        var dialogue = new RandomSource(seed).Open(RandomStream.Dialogue, tick, 9);
        var trade = new RandomSource(seed).Open(RandomStream.Trade, tick, 9);

        ulong first = conversation.NextUInt64();

        Assert.Equal(dialogue.NextUInt64(), first);
        Assert.NotEqual(trade.NextUInt64(), first);
    }

    [Fact]
    public void SayRejectsInconsistentArguments()
    {
        var corpus = Corpus(Many(LineKind.Greeting, "g", 1));
        var memory = new DialogueMemory();

        void Call(LineKind kind, DialogueContext context)
        {
            var random = DialogueSelector.OpenConversation(Seed, new Tick(12), 0);
            DialogueSelector.Say(corpus, memory, ref random, 0, kind, in context);
        }

        var plain = Plain();

        // 取引の成立に段階なし
        Assert.Throws<ArgumentException>(() => Call(LineKind.TradeAccepted, plain with { ItemId = 0, Quantity = 1 }));

        // 挨拶に段階あり
        Assert.Throws<ArgumentException>(() => Call(LineKind.Greeting, plain with { Gratitude = Gratitude.Grateful }));

        // 開示に ItemId なし
        Assert.Throws<ArgumentException>(() => Call(LineKind.NeedDisclosure, plain with { Quantity = 1 }));

        // 不成立に Quantity あり
        Assert.Throws<ArgumentException>(() => Call(LineKind.TradeRefused, plain with { ItemId = 0, Quantity = 1 }));

        // 数量 0
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Call(LineKind.NeedDisclosure, plain with { ItemId = 0, Quantity = 0 }));

        // 品目 Id が範囲外
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Call(LineKind.TradeRefused, plain with { ItemId = Item.Count }));

        // 候補 0 本(資料に SmallTalk が無い)
        Assert.Throws<InvalidOperationException>(() => Call(LineKind.SmallTalk, plain));
    }
}
