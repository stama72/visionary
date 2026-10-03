using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Visionary.Sim.Dialogue;
using Visionary.Sim.Metrics;
using Visionary.Sim.Randomness;
using Visionary.Sim.Runner;
using Visionary.Sim.Systems;

namespace Visionary.Sim.Tests.Runner;

/// <summary>
/// <c>vsim dialogue-sample</c>(W5-01 テスト表 #19〜#23)の検査。<see cref="Program.Execute"/> を
/// プロセスを起動せずに直接呼び、<c>--out</c> のファイルを読む(<c>Console</c> を差し替えない)。
/// </summary>
/// <remarks>
/// <para>
/// <b>#21 の検出力(W5-01 の注記)。</b>選んだ NPC で Need のある日が連続していると、巡りの添字の
/// 進め方を誤っても列は変わらない。Need の無い日が Need のある日の間に挟まるときだけ落ちる。
/// 選ばれた (day, npc) と、その NPC の30日の Need の有無の並びは実測で次のとおり
/// (seed 1・<c>WorldDefinition.M0</c>・会話は毎日 12時。実測日 2026-10-03。〇 = 開示する Need あり、− = 無し。
/// 左端が day 0):
/// </para>
/// <list type="bullet">
/// <item><description>#20 が選ぶ (day 1, npc 0)。npc 0 の並び: <c>−〇−−−−−−−−−−−−−−−−−−−−−−−−−−−−</c>
/// (day 0 は Need が無い。Need のある日は day 1 の1日だけ)</description></item>
/// <item><description>#21・#22 が選ぶ npc 2(Id 昇順で最初に <c>〇−+〇</c> を含む)。並び:
/// <c>−〇〇〇−−−−−−−−−−〇〇−−−−−−−−−−−−−−</c>。取引の行は5行
/// (渋々・感謝・深い感謝・不成立・渋々)で、day 3 と day 14 の間の10日が Need の無い日として挟まる
/// ── 空振りではない</description></item>
/// </list>
/// <para>
/// 参考: npc 0・1 は Need のある日が1日だけで、タスク仕様どおり「#20 で選んだ npc」を #21 に使うと
/// 取引の行が1行になり前提が崩れる。
/// </para>
/// </remarks>
public sealed class DialogueSampleTests : IDisposable
{
    private static readonly string[] ItemNames =
        { "穀物", "木材", "鉄鉱石", "木炭", "小麦粉", "薪", "パン", "ビール", "工具" };

    private static readonly string[] KindLabels =
        { "挨拶", "世間話", "開示", "困りごとは無い", "取引の成立", "取引の不成立" };

    private readonly string _workDirectory;
    private int _runCount;

    public DialogueSampleTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "vsim-dialogue-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (IOException)
        {
            // 後始末の失敗はテスト結果に影響させない。
        }
    }

    private string RunSample(params string[] options)
    {
        string path = Path.Combine(_workDirectory, $"out{_runCount++}.txt");
        var args = new[] { "dialogue-sample" }.Concat(options).Concat(new[] { "--out", path }).ToArray();

        Assert.Equal(0, Program.Execute(args));
        return File.ReadAllText(path, new UTF8Encoding(false));
    }

    /// <summary>テスト内で同じ世界を独立に回し、day 0〜29 で最初に開示する Need が立つ (day, npc) を探す。</summary>
    private static (int Day, int Npc) FindFirstNpcWithDisclosedNeed()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));
        var scheduler = new SimScheduler(
            Program.BuildPipeline(definition, new NullDailyMetricsSink()), new RandomSource(1));

        for (int day = 0; day < 30; day++)
        {
            scheduler.Advance(world, (int)(((day * 24L) + 12) - world.Now.Value));

            for (int npc = 0; npc < definition.NpcCount; npc++)
            {
                if (DisclosedNeeds.Of(world, world.Npcs[npc].HouseholdId).Count > 0)
                {
                    return (day, npc);
                }
            }
        }

        Assert.Fail("seed 1 の day 0〜29 に、開示する Need が立つ NPC が無い。");
        return default;
    }

    /// <summary>
    /// 添字 = NpcId、値 = day 0〜29 の「開示する Need があるか」の並び(〇 / −)。テスト内で同じ世界を
    /// 独立に回して作る。
    /// </summary>
    private static string[] NeedPatternsOfThirtyDays()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));
        var scheduler = new SimScheduler(
            Program.BuildPipeline(definition, new NullDailyMetricsSink()), new RandomSource(1));
        var rows = new StringBuilder[definition.NpcCount];

        for (int npc = 0; npc < rows.Length; npc++)
        {
            rows[npc] = new StringBuilder();
        }

        for (int day = 0; day < 30; day++)
        {
            scheduler.Advance(world, (int)(((day * 24L) + 12) - world.Now.Value));

            for (int npc = 0; npc < rows.Length; npc++)
            {
                rows[npc].Append(DisclosedNeeds.Of(world, world.Npcs[npc].HouseholdId).Count > 0 ? '〇' : '−');
            }
        }

        return rows.Select(r => r.ToString()).ToArray();
    }

    /// <summary>
    /// #21・#22 が使う NPC。Id 昇順で最初の、30日の並びに「Need のある日 → 無い日 → Need のある日」を
    /// 含むもの。
    /// </summary>
    /// <remarks>
    /// <b>タスク仕様は「#20 で選んだ NPC」としていたが、それでは足りない。</b>#20 は seed 1 で最初に
    /// Need が立つ NPC(npc 0・day 1)を選ぶが、npc 0 は30日で Need のある日が day 1 の1日だけ
    /// (並び <c>−〇−−…−</c>)で、取引の行が1行しか出ず、#21 の前提(2行以上)が成り立たない。
    /// 仕様の注記(Need の無い日が Need のある日の間に挟まるときだけ落ちる)を満たす NPC を、
    /// 独立に回した並びから選ぶ。
    /// </remarks>
    private static int FindNpcWithAGapBetweenNeedDays() =>
        Array.FindIndex(NeedPatternsOfThirtyDays(), p => Regex.IsMatch(p, "〇−+〇"));

    private static List<string> Body(string output) =>
        output.Split('\n').Where(l => l.StartsWith('[')).ToList();

    private static string SmallKanji(int value)
    {
        Assert.InRange(value, 1, 99);
        const string digits = "〇一二三四五六七八九";
        string tens = value >= 10 ? (value / 10 == 1 ? string.Empty : digits[value / 10].ToString()) + "十" : string.Empty;
        string ones = value % 10 == 0 ? string.Empty : digits[value % 10].ToString();
        return tens + ones;
    }

    [Fact]
    public void DialogueSampleWritesTheRequestedConversations()
    {
        string[] options = { "--npc", "0", "--seed", "1", "--start-day", "2", "--repeat", "3" };
        string output = RunSample(options);

        var headers = output.Split('\n').Where(l => l.StartsWith("== ", StringComparison.Ordinal) && l.Contains(" day ")).ToList();
        Assert.Equal(3, headers.Count);

        var days = new[] { 2, 3, 4 };

        for (int i = 0; i < 3; i++)
        {
            Assert.Matches(new Regex($@"^== {i + 1}/3 day {days[i]} Y\d+-\w+-\d\dT12 "), headers[i]);
        }

        // 各会話に挨拶と世間話が1行ずつ
        string[] blocks = output.Split("\n== ").Skip(1).Take(3).ToArray();

        foreach (string block in blocks)
        {
            var lines = block.Split('\n');
            Assert.Single(lines, l => l.StartsWith("[挨拶]", StringComparison.Ordinal));
            Assert.Single(lines, l => l.StartsWith("[世間話]", StringComparison.Ordinal));
        }

        // 決定論: 同じ引数で2回 → バイト単位で一致
        string first = Path.Combine(_workDirectory, "a.txt");
        string second = Path.Combine(_workDirectory, "b.txt");
        Assert.Equal(0, Program.Execute(new[] { "dialogue-sample", "--npc", "0", "--start-day", "2", "--repeat", "3", "--out", first }));
        Assert.Equal(0, Program.Execute(new[] { "dialogue-sample", "--npc", "0", "--start-day", "2", "--repeat", "3", "--out", second }));
        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
    }

    [Fact]
    public void DialogueSampleDisclosesTheNeedsStandingThatDay()
    {
        var (day, npc) = FindFirstNpcWithDisclosedNeed();

        // 同じ世界を独立にもう一度回して、その日の Need を取り直す(期待値の出所を CLI と分ける)
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));
        var scheduler = new SimScheduler(
            Program.BuildPipeline(definition, new NullDailyMetricsSink()), new RandomSource(1));
        scheduler.Advance(world, (int)((day * 24L) + 12));
        var needs = DisclosedNeeds.Of(world, world.Npcs[npc].HouseholdId);

        string output = RunSample("--npc", npc.ToString(CultureInfo.InvariantCulture),
            "--start-day", day.ToString(CultureInfo.InvariantCulture), "--repeat", "1");
        var disclosures = Body(output).Where(l => l.StartsWith("[開示]", StringComparison.Ordinal)).ToList();

        Assert.Equal(needs.Count, disclosures.Count);

        for (int i = 0; i < needs.Count; i++)
        {
            var need = needs[i];
            string line = disclosures[i];

            Assert.Contains(ItemNames[need.ItemId], line, StringComparison.Ordinal);
            Assert.Contains(SmallKanji(need.Quantity), line, StringComparison.Ordinal);
            Assert.EndsWith(
                $"<need #{need.Id} item={need.ItemId} qty={need.Quantity} reason={need.ReasonCode}>",
                line,
                StringComparison.Ordinal);
        }

        var trades = Body(output).Where(l => l.StartsWith("[取引", StringComparison.Ordinal)).ToList();
        Assert.Single(trades);
        Assert.StartsWith("[取引の成立/渋々]", trades[0], StringComparison.Ordinal);
    }

    [Fact]
    public void DialogueSampleRotatesOutcomesOnlyOnNeedDays()
    {
        int npc = FindNpcWithAGapBetweenNeedDays();
        Assert.True(npc >= 0, "seed 1 の30日に、Need のある日の間に Need の無い日が挟まる NPC が無い(空振り)。");
        string output = RunSample("--npc", npc.ToString(CultureInfo.InvariantCulture), "--start-day", "0", "--repeat", "30");

        var labels = Body(output)
            .Where(l => l.StartsWith("[取引", StringComparison.Ordinal))
            .Select(l => l.StartsWith("[取引の不成立]", StringComparison.Ordinal)
                ? "不成立"
                : Regex.Match(l, @"^\[取引の成立/(.+?)\]").Groups[1].Value)
            .ToList();

        // 前提: 取引の行が2行以上ある(無ければ巡りを確かめられない)
        Assert.True(labels.Count >= 2, $"取引の行が {labels.Count} 行しかない。");

        string[] cycle = { "渋々", "感謝", "深い感謝", "不成立" };
        var expected = Enumerable.Range(0, labels.Count).Select(i => cycle[i % cycle.Length]).ToList();
        Assert.Equal(expected, labels);
    }

    private static List<string> ExpectedSummary(string output)
    {
        var perKind = KindLabels.ToDictionary(label => label, _ => new List<string>());

        foreach (string line in Body(output))
        {
            var m = Regex.Match(line, @"^\[(.+?)\] \((.+?)\) ");
            string label = m.Groups[1].Value;
            string kind = label.StartsWith("取引の成立/", StringComparison.Ordinal) ? "取引の成立" : label;
            perKind[kind].Add(m.Groups[2].Value);
        }

        var result = new List<string>();

        foreach (string label in KindLabels)
        {
            var ids = perKind[label];

            if (ids.Count == 0)
            {
                result.Add($"{label}: 0行");
                continue;
            }

            // 最多: 使用回数が最大のもの、同数なら最初に出たもの
            var distinct = ids.Distinct().ToList();
            string top = distinct.OrderByDescending(id => ids.Count(x => x == id)).ThenBy(id => ids.IndexOf(id)).First();
            result.Add($"{label}: {ids.Count}行 / {distinct.Count}種 / 最多 {top} ×{ids.Count(x => x == top)}");
        }

        return result;
    }

    [Fact]
    public void DialogueSampleSummaryMatchesTheLines()
    {
        int npc = FindNpcWithAGapBetweenNeedDays();
        Assert.True(npc >= 0);

        var outputs = new[]
        {
            RunSample("--npc", "0", "--start-day", "2", "--repeat", "3"),
            RunSample("--npc", npc.ToString(CultureInfo.InvariantCulture), "--start-day", "0", "--repeat", "30"),
        };

        foreach (string output in outputs)
        {
            string[] lines = output.TrimEnd('\n').Split('\n');
            int summaryStart = Array.IndexOf(lines, "== 集計");

            Assert.True(summaryStart >= 0);
            Assert.Equal(ExpectedSummary(output), lines.Skip(summaryStart + 1).ToList());
        }
    }

    [Fact]
    public void DialogueSampleRejectsBadOptions()
    {
        int npcCount = WorldDefinition.M0.NpcCount;
        var bad = new[]
        {
            new[] { "dialogue-sample" },
            new[] { "dialogue-sample", "--npc", npcCount.ToString(CultureInfo.InvariantCulture) },
            new[] { "dialogue-sample", "--npc", "-1" },
            new[] { "dialogue-sample", "--npc", "0", "--repeat", "0" },
            new[] { "dialogue-sample", "--npc", "0", "--start-day", "-1" },
        };

        foreach (var args in bad)
        {
            Assert.Equal(64, Program.Execute(args));
        }
    }
}
