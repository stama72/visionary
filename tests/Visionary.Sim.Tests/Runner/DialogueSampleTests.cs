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
/// <item><description>#20 が選ぶ (day 1, npc 1)。npc 1 は世帯 0 で、day 1 に <c>Of(world, 世帯Id 0)</c> と
/// <c>Of(world, NpcId 1)</c>(= 世帯 1 の Need)の結果が異なる最初の組。npc 0 は世帯 0 と NpcId が
/// 同じ番号なので、取り違えても結果が変わらず選ばれない。day 0 は Need が無い。
/// npc 1 の並び: <c>−〇−−−−−−−−−−−−−−−−−−−−−−−−−−−−</c>(Need のある日は day 1 の1日だけ。
/// これは #20 には十分)</description></item>
/// <item><description>#21・#22 が選ぶ npc 2(世帯 1。Id 昇順で最初に <c>〇−+〇</c> を含み、かつ NpcId を
/// 世帯 Id と取り違えると Need のある日の数が変わるもの)。並び:
/// <c>−〇〇〇−−−−−−−−−−〇〇−−−−−−−−−−−−−−</c>(Need のある日は5日)。取り違えた場合の並び
/// (世帯 2 のもの)は <c>−〇−−〇〇−−−−−−−−〇〇−−−−−〇−−−−−−−−</c>(6日)。取引の行は5行
/// (渋々・感謝・深い感謝・不成立・渋々)で、day 3 と day 14 の間の10日が Need の無い日として挟まる
/// ── 空振りではない。取り違えると取引の行が6行になり落ちる</description></item>
/// </list>
/// <para>
/// 変異の実測(<c>mutator</c> の報告、HEAD f7a76bf、2026-10-03): <c>DialogueSampleCommand</c> で
/// <c>DisclosedNeeds.Of(world, householdId)</c> を <c>Of(world, npc)</c> に変えると、#20
/// (<c>DialogueSampleDisclosesTheNeedsStandingThatDay</c>)と #21
/// (<c>DialogueSampleRotatesOutcomesOnlyOnNeedDays</c>)の両方が赤。
/// </para>
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

    private static bool SameNeeds(IReadOnlyList<Need> a, IReadOnlyList<Need> b) =>
        a.Select(n => n.Id).SequenceEqual(b.Select(n => n.Id));

    /// <summary>
    /// テスト内で同じ世界を独立に回し、day 0〜29 で最初の (day, npc) を探す。条件: 世帯の Need が立ち、
    /// かつ <c>Of(world, 世帯Id)</c> と <c>Of(world, NpcId)</c> の結果が異なる(NPC の Id を世帯 Id と
    /// 取り違える実装を、npc と世帯が同じ番号の NPC では見逃すため)。
    /// </summary>
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
                var right = DisclosedNeeds.Of(world, world.Npcs[npc].HouseholdId);

                if (right.Count > 0 && !SameNeeds(right, DisclosedNeeds.Of(world, npc)))
                {
                    return (day, npc);
                }
            }
        }

        Assert.Fail("seed 1 の day 0〜29 に、世帯の Need が立ち NpcId を世帯 Id と取り違えると結果が変わる NPC が無い。");
        return default;
    }

    /// <summary>
    /// 添字 = NpcId、値 = day 0〜29 の「開示する Need があるか」の並び(〇 / −)。テスト内で同じ世界を
    /// 独立に回して作る。
    /// </summary>
    private static (string[] Right, string[] Wrong) NeedPatternsOfThirtyDays()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));
        var scheduler = new SimScheduler(
            Program.BuildPipeline(definition, new NullDailyMetricsSink()), new RandomSource(1));
        var rows = new StringBuilder[definition.NpcCount];
        var wrongRows = new StringBuilder[definition.NpcCount];

        for (int npc = 0; npc < rows.Length; npc++)
        {
            rows[npc] = new StringBuilder();
            wrongRows[npc] = new StringBuilder();
        }

        for (int day = 0; day < 30; day++)
        {
            scheduler.Advance(world, (int)(((day * 24L) + 12) - world.Now.Value));

            for (int npc = 0; npc < rows.Length; npc++)
            {
                rows[npc].Append(DisclosedNeeds.Of(world, world.Npcs[npc].HouseholdId).Count > 0 ? '〇' : '−');

                // NpcId を世帯 Id として引く取り違えた実装が見る並び
                wrongRows[npc].Append(DisclosedNeeds.Of(world, npc).Count > 0 ? '〇' : '−');
            }
        }

        return (rows.Select(r => r.ToString()).ToArray(), wrongRows.Select(r => r.ToString()).ToArray());
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
    private static (int Npc, int NeedDays) FindNpcWithAGapBetweenNeedDays()
    {
        var (right, wrong) = NeedPatternsOfThirtyDays();

        // さらに、NpcId を世帯 Id と取り違えると Need のある日の数が変わる NPC に限る
        // (取引の行の数で取り違えを見分けるため)。
        for (int npc = 0; npc < right.Length; npc++)
        {
            int needDays = right[npc].Count(c => c == '〇');

            if (Regex.IsMatch(right[npc], "〇−+〇") && needDays != wrong[npc].Count(c => c == '〇'))
            {
                return (npc, needDays);
            }
        }

        return (-1, 0);
    }

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
        var (npc, needDays) = FindNpcWithAGapBetweenNeedDays();
        Assert.True(npc >= 0, "seed 1 の30日に、Need のある日の間に Need の無い日が挟まり NpcId の取り違えで日数が変わる NPC が無い(空振り)。");
        string output = RunSample("--npc", npc.ToString(CultureInfo.InvariantCulture), "--start-day", "0", "--repeat", "30");

        var labels = Body(output)
            .Where(l => l.StartsWith("[取引", StringComparison.Ordinal))
            .Select(l => l.StartsWith("[取引の不成立]", StringComparison.Ordinal)
                ? "不成立"
                : Regex.Match(l, @"^\[取引の成立/(.+?)\]").Groups[1].Value)
            .ToList();

        // 前提: 取引の行が2行以上ある(無ければ巡りを確かめられない)
        Assert.True(labels.Count >= 2, $"取引の行が {labels.Count} 行しかない。");

        // 取引の行は世帯の Need のある日ごとに1行(NpcId を世帯 Id と取り違えると日数が変わる)
        Assert.Equal(needDays, labels.Count);

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
        var (npc, _) = FindNpcWithAGapBetweenNeedDays();
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
