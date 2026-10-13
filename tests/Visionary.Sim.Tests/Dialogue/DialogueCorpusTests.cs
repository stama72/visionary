using Visionary.Sim.Dialogue;
using Visionary.Sim.Time;
using static Visionary.Sim.Tests.Dialogue.DialogueTestHelpers;

namespace Visionary.Sim.Tests.Dialogue;

/// <summary>
/// 台詞の資料の検査と差し込みの展開(W5-01 テスト表 #12〜#14)、および M0 の資料の検査(#16・#17)。
/// </summary>
public sealed class DialogueCorpusTests
{
    private static DialogueLine Say(
        DialogueCorpus corpus, LineKind kind, DialogueContext context, long tick = 12)
    {
        var random = DialogueSelector.OpenConversation(1, new Tick(tick), 0);
        return DialogueSelector.Say(corpus, new DialogueMemory(), ref random, 0, kind, in context);
    }

    [Fact]
    public void PlaceholdersExpandToWordsAndKanjiCounts()
    {
        var disclosure = Template("d", LineKind.NeedDisclosure, text: "{item}を{qty}");
        var corpus = Corpus(disclosure, Template("s", LineKind.SmallTalk, text: "{season}だね"));

        var toolsTwo = Plain() with { ItemId = Item.Tools, Quantity = 2 };
        Assert.Equal("工具を二本", Say(corpus, LineKind.NeedDisclosure, toolsTwo).Text);

        Assert.Equal("冬だね", Say(corpus, LineKind.SmallTalk, Plain(season: Season.Winter)).Text);

        // 品目表の工具(8)と薪(5)を入れ替えた資料では、同じ ItemId が別の語になる(添字の取り違えを値で見る)
        var swapped = (ItemWording[])StandardItems.Clone();
        (swapped[Item.Firewood], swapped[Item.Tools]) = (swapped[Item.Tools], swapped[Item.Firewood]);
        var swappedCorpus = Corpus(swapped, disclosure);

        Assert.Equal("薪を二束", Say(swappedCorpus, LineKind.NeedDisclosure, toolsTwo).Text);
    }

    [Theory]
    [InlineData(1, "一")]
    [InlineData(9, "九")]
    [InlineData(10, "十")]
    [InlineData(11, "十一")]
    [InlineData(19, "十九")]
    [InlineData(20, "二十")]
    [InlineData(99, "九十九")]
    [InlineData(100, "百")]
    [InlineData(101, "百一")]
    [InlineData(105, "百五")]
    [InlineData(110, "百十")]
    [InlineData(999, "九百九十九")]
    [InlineData(1000, "千")]
    [InlineData(1001, "千一")]
    [InlineData(2345, "二千三百四十五")]
    [InlineData(9999, "九千九百九十九")]
    [InlineData(10000, "10000")]
    public void KanjiNumeralsFollowThePositionRules(int quantity, string expected)
    {
        // 助数詞を空にして、数量の展開だけを見る
        var items = Enumerable.Repeat(new ItemWording("x", string.Empty), Item.Count).ToArray();
        var corpus = Corpus(items, Template("d", LineKind.NeedDisclosure, text: "{item}{qty}"));

        var line = Say(corpus, LineKind.NeedDisclosure, Plain() with { ItemId = 0, Quantity = quantity });

        Assert.Equal("x" + expected, line.Text);
    }

    [Fact]
    public void CorpusRejectsMalformedTemplates()
    {
        var validDisclosure = Template("ok", LineKind.NeedDisclosure, text: "{item}を{qty}");

        void Rejects(params DialogueTemplate[] templates) =>
            Assert.Throws<ArgumentException>(() => Corpus(templates));

        // Id 重複 / 空の Id
        Rejects(Template("dup", LineKind.Greeting), Template("dup", LineKind.Greeting));
        Rejects(Template(string.Empty, LineKind.Greeting));

        // 種類に許されない差し込み
        Rejects(Template("budget", LineKind.Greeting, text: "{budget}"));
        Rejects(Template("qtyInRefusal", LineKind.TradeRefused, text: "{item}を{qty}"));
        Rejects(Template("seasonInGreeting", LineKind.Greeting, text: "{season}"));

        // 開示に {qty} / {item} が無い
        Rejects(Template("noQty", LineKind.NeedDisclosure, text: "{item}が要る"));
        Rejects(Template("noItem", LineKind.NeedDisclosure, text: "{qty}が要る"));

        // 季節を持てるのは世間話だけ
        Rejects(Template("seasonal", LineKind.Greeting, season: Season.Spring));

        // 取引の成立は段階が必須 / 段階を持てるのは取引の成立だけ
        Rejects(Template("noTier", LineKind.TradeAccepted));
        Rejects(Template("tierOnRefusal", LineKind.TradeRefused, gratitude: Gratitude.Grateful, text: "{item}"));

        // 対応の取れない括弧
        Rejects(Template("open", LineKind.NeedDisclosure, text: "{item"));
        Rejects(Template("close", LineKind.Greeting, text: "}"));
        Rejects(Template("nested", LineKind.NeedDisclosure, text: "{it{item}}"));
        Rejects(Template("empty", LineKind.Greeting, text: "{}"));

        // 品目表・季節名の長さ
        Assert.Throws<ArgumentException>(
            () => new DialogueCorpus(new[] { validDisclosure }, StandardItems.Take(8).ToArray(), StandardSeasons));
        Assert.Throws<ArgumentException>(
            () => new DialogueCorpus(new[] { validDisclosure }, StandardItems, StandardSeasons.Take(3).ToArray()));

        // 対照: 正しい資料は通る
        _ = Corpus(validDisclosure);
    }

    // ---- M0 の資料(#16・#17)。軸の当たり方はテスト側で独立に書く ----

    private static int Union(
        LineKind kind, Occupation occupation, Season season, Gratitude? gratitude, bool occupationSpecificOnly)
    {
        return DialogueCorpus.M0.Templates.Count(t =>
            t.Kind == kind
            && (t.Occupation is null || t.Occupation == occupation)
            && (t.Season is null || t.Season == season)
            && (t.Gratitude is null || t.Gratitude == gratitude)
            && t.Personality is null
            && (!occupationSpecificOnly || t.Occupation is not null));
    }

    [Fact]
    public void M0CorpusMeetsTheCoverageFloor()
    {
        // 種類 → (和集合の下限, 職業指定の下限)
        var floors = new Dictionary<LineKind, (int Union, int Specific)>
        {
            [LineKind.Greeting] = (8, 2),
            [LineKind.SmallTalk] = (8, 2),
            [LineKind.NeedDisclosure] = (6, 2),
            [LineKind.NoNeed] = (6, 2),
            [LineKind.TradeAccepted] = (4, 1),
            [LineKind.TradeRefused] = (4, 2),
        };

        var violations = new List<string>();

        foreach (var kind in floors.Keys.OrderBy(k => k))
        {
            var (unionFloor, specificFloor) = floors[kind];
            var tiers = kind == LineKind.TradeAccepted
                ? new Gratitude?[] { Gratitude.Reluctant, Gratitude.Grateful, Gratitude.DeepGratitude }
                : new Gratitude?[] { null };

            foreach (var occupation in Enum.GetValues<Occupation>())
            {
                foreach (var season in Enum.GetValues<Season>())
                {
                    foreach (var tier in tiers)
                    {
                        int union = Union(kind, occupation, season, tier, false);
                        int specific = Union(kind, occupation, season, tier, true);

                        if (union < unionFloor || specific < specificFloor)
                        {
                            violations.Add($"{kind}/{occupation}/{season}/{tier}: 和集合 {union}(下限 {unionFloor}) 職業指定 {specific}(下限 {specificFloor})");
                        }
                    }
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static readonly string[] BannedWords =
        { "おはよう", "こんにちは", "こんばんは", "来月", "ヶ月", "か月", "カ月" };

    [Fact]
    public void M0CorpusFollowsTheWritingRules()
    {
        var corpus = DialogueCorpus.M0;

        foreach (var template in corpus.Templates)
        {
            Assert.True(template.Text.Length <= 60, $"{template.Id}: {template.Text.Length}字");

            foreach (string word in BannedWords)
            {
                Assert.DoesNotContain(word, template.Text, StringComparison.Ordinal);
            }

            // 性格の軸は M0 ではどれも使わない(決定2)
            Assert.Null(template.Personality);
        }

        // 品目表: 開示の台詞は {item}{qty} を必ず含むので、展開結果を表から組んだ期待値と一字一句比べる
        var disclosure = corpus.Templates.First(t => t.Kind == LineKind.NeedDisclosure);

        for (int itemId = 0; itemId < Item.Count; itemId++)
        {
            var line = Say(corpus, LineKind.NeedDisclosure, Plain() with { ItemId = itemId, Quantity = 2 });
            var template = corpus.Templates.Single(t => t.Id == line.TemplateId);
            var wording = StandardItems[itemId];
            string expected = template.Text
                .Replace("{item}", wording.Name, StringComparison.Ordinal)
                .Replace("{qty}", "二" + wording.Counter, StringComparison.Ordinal);

            Assert.Equal(expected, line.Text);
        }

        Assert.NotNull(disclosure);

        // 季節名: 季節指定の世間話は、同じ NPC と繰り返せば一巡の中で必ず出る
        for (int s = 0; s < 4; s++)
        {
            var season = (Season)s;
            var memory = new DialogueMemory();
            DialogueLine? seasonal = null;

            for (int i = 0; i < 40 && seasonal is null; i++)
            {
                var random = DialogueSelector.OpenConversation(1, new Tick((i * 24) + 12), 0);
                var context = Plain(season: season);
                var line = DialogueSelector.Say(corpus, memory, ref random, 0, LineKind.SmallTalk, in context);
                var template = corpus.Templates.Single(t => t.Id == line.TemplateId);

                if (template.Season == season)
                {
                    seasonal = line;
                    Assert.StartsWith(StandardSeasons[s], line.Text, StringComparison.Ordinal);
                }
            }

            Assert.NotNull(seasonal);
        }
    }
}
