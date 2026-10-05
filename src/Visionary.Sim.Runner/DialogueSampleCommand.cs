using System.Globalization;
using System.Text;
using Visionary.Sim.Dialogue;
using Visionary.Sim.Metrics;
using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Runner;

/// <summary>
/// <c>vsim dialogue-sample --npc &lt;int&gt; [--seed &lt;long&gt;] [--start-day &lt;int&gt;] [--repeat &lt;int&gt;] [--out &lt;path&gt;]</c>
/// (TDD01 §4.1 / W5-01 §7)。実際のシムを回し、その日に立っている Need を使って同一 NPC との会話を並べる。
/// 開発者が §6.2 #3(同じ NPC と10回話して反復感を判定する)に使う資料を出す。
/// </summary>
internal static class DialogueSampleCommand
{
    private const int ExitSuccess = 0;
    private const int ExitUsage = 64;

    // 会話の時刻は毎日 12時(hour 0 に組み直された Need が見え、日中の時間帯の語を台詞に持たない)。
    private const int ConversationHour = 12;

    // 取引の結果を巡らせる周期: 渋々 → 感謝 → 深い感謝 → 不成立。
    private const int OutcomeCycle = 4;

    private static readonly LineKind[] AllKinds =
    {
        LineKind.Greeting, LineKind.SmallTalk, LineKind.NeedDisclosure,
        LineKind.NoNeed, LineKind.TradeAccepted, LineKind.TradeRefused,
    };

    public static int Run(string[] args)
    {
        int? npc = null;
        long seed = 1;
        int startDay = 0;
        int repeat = 10;
        string? outPath = null;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--npc":
                    if (!TryInt(args, ref i, out int npcValue))
                    {
                        return Usage();
                    }

                    npc = npcValue;
                    break;

                case "--seed":
                    if (!TryNext(args, ref i, out string? seedText)
                        || !long.TryParse(seedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
                    {
                        return Usage();
                    }

                    break;

                case "--start-day":
                    if (!TryInt(args, ref i, out startDay))
                    {
                        return Usage();
                    }

                    break;

                case "--repeat":
                    if (!TryInt(args, ref i, out repeat))
                    {
                        return Usage();
                    }

                    break;

                case "--out":
                    if (!TryNext(args, ref i, out outPath))
                    {
                        return Usage();
                    }

                    break;

                default:
                    Console.Error.WriteLine($"未知のオプション: {args[i]}");
                    return Usage();
            }
        }

        var definition = WorldDefinition.M0;

        if (npc is null)
        {
            Console.Error.WriteLine("--npc は必須。");
            return Usage();
        }

        if (npc < 0 || npc >= definition.NpcCount)
        {
            Console.Error.WriteLine($"--npc は 0〜{definition.NpcCount - 1}。");
            return Usage();
        }

        if (startDay < 0)
        {
            Console.Error.WriteLine("--start-day は0以上。");
            return Usage();
        }

        if (repeat < 1)
        {
            Console.Error.WriteLine("--repeat は1以上。");
            return Usage();
        }

        string text = Build(definition, seed, npc.Value, startDay, repeat);

        if (outPath is null)
        {
            Console.Out.Write(text);
        }
        else
        {
            File.WriteAllText(outPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return ExitSuccess;
    }

    private static string Build(WorldDefinition definition, long seed, int npc, int startDay, int repeat)
    {
        // 世界と系列は vsim hash と同じ。
        var world = WorldGenerator.Generate(definition, new RandomSource(seed));
        var scheduler = new SimScheduler(
            Program.BuildPipeline(definition, new NullDailyMetricsSink()), new RandomSource(seed));
        var corpus = DialogueCorpus.M0;
        var memory = new DialogueMemory();

        var sb = new StringBuilder();
        var tally = new Tally[AllKinds.Length];

        for (int k = 0; k < tally.Length; k++)
        {
            tally[k] = new Tally();
        }

        Append(sb, string.Create(
            CultureInfo.InvariantCulture,
            $"dialogue-sample seed={seed} npc={npc} start-day={startDay} repeat={repeat}"));

        // 巡る添字は「Need があった会話」だけで進む。
        int tradeIndex = 0;

        for (int i = 0; i < repeat; i++)
        {
            long day = (long)startDay + i;
            long t = (day * Tick.HoursPerDay) + ConversationHour;
            scheduler.Advance(world, checked((int)(t - world.Now.Value)));

            // 職業は付け替えで変わるので会話ごとに読み直す。
            int householdId = world.Npcs[npc].HouseholdId;
            var occupation = world.Households[householdId].Occupation;
            var tick = new Tick(t);
            var date = GameDate.FromTick(tick);
            var random = DialogueSelector.OpenConversation(seed, tick, npc);

            Append(sb, string.Create(
                CultureInfo.InvariantCulture,
                $"== {i + 1}/{repeat} day {day} {date} household={householdId} occupation={occupation}"));

            var baseContext = new DialogueContext(occupation, date.Season, null, null, null, null);

            AppendLine(sb, tally, corpus, memory, ref random, npc, LineKind.Greeting, baseContext, "挨拶", null);
            AppendLine(sb, tally, corpus, memory, ref random, npc, LineKind.SmallTalk, baseContext, "世間話", null);

            var needs = DisclosedNeeds.Of(world, householdId);

            if (needs.Count == 0)
            {
                AppendLine(sb, tally, corpus, memory, ref random, npc, LineKind.NoNeed, baseContext, "困りごとは無い", null);
                continue;
            }

            foreach (var need in needs)
            {
                var context = baseContext with { ItemId = need.ItemId, Quantity = need.Quantity };
                string tag = string.Create(
                    CultureInfo.InvariantCulture,
                    $"<need #{need.Id} item={need.ItemId} qty={need.Quantity} reason={need.ReasonCode}>");
                AppendLine(sb, tally, corpus, memory, ref random, npc, LineKind.NeedDisclosure, context, "開示", tag);
            }

            var first = needs[0];
            int outcome = tradeIndex % OutcomeCycle;
            tradeIndex++;

            if (outcome == OutcomeCycle - 1)
            {
                var refused = baseContext with { ItemId = first.ItemId };
                AppendLine(sb, tally, corpus, memory, ref random, npc, LineKind.TradeRefused, refused, "取引の不成立", null);
            }
            else
            {
                var gratitude = (Gratitude)(outcome + 1);
                var accepted = baseContext with { Gratitude = gratitude, ItemId = first.ItemId, Quantity = first.Quantity };
                string label = "取引の成立/" + gratitude switch
                {
                    Gratitude.Reluctant => "渋々",
                    Gratitude.Grateful => "感謝",
                    _ => "深い感謝",
                };
                AppendLine(sb, tally, corpus, memory, ref random, npc, LineKind.TradeAccepted, accepted, label, null);
            }
        }

        Append(sb, "== 集計");

        for (int k = 0; k < AllKinds.Length; k++)
        {
            Append(sb, SummaryLine(AllKinds[k], tally[k]));
        }

        return sb.ToString();
    }

    private static void AppendLine(
        StringBuilder sb,
        Tally[] tally,
        DialogueCorpus corpus,
        DialogueMemory memory,
        ref RandomSequence random,
        int npc,
        LineKind kind,
        in DialogueContext context,
        string label,
        string? suffix)
    {
        var line = DialogueSelector.Say(corpus, memory, ref random, npc, kind, in context);
        tally[Array.IndexOf(AllKinds, kind)].Add(line.TemplateId);

        sb.Append('[').Append(label).Append("] (").Append(line.TemplateId).Append(") ").Append(line.Text);

        if (suffix is not null)
        {
            sb.Append(' ').Append(suffix);
        }

        sb.Append('\n');
    }

    private static string SummaryLine(LineKind kind, Tally tally)
    {
        string label = kind switch
        {
            LineKind.Greeting => "挨拶",
            LineKind.SmallTalk => "世間話",
            LineKind.NeedDisclosure => "開示",
            LineKind.NoNeed => "困りごとは無い",
            LineKind.TradeAccepted => "取引の成立",
            _ => "取引の不成立",
        };

        if (tally.Total == 0)
        {
            return $"{label}: 0行";
        }

        var (topId, topCount) = tally.Top();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{label}: {tally.Total}行 / {tally.DistinctCount}種 / 最多 {topId} ×{topCount}");
    }

    private static void Append(StringBuilder sb, string line) => sb.Append(line).Append('\n');

    private static int Usage()
    {
        Program.PrintUsage();
        return ExitUsage;
    }

    private static bool TryNext(string[] args, ref int index, out string? value)
    {
        value = null;

        if (index + 1 >= args.Length)
        {
            return false;
        }

        index++;
        value = args[index];
        return true;
    }

    private static bool TryInt(string[] args, ref int index, out int value)
    {
        value = 0;
        return TryNext(args, ref index, out string? text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>種類ごとの使用の集計。「最多」は使用回数が最大のテンプレート、同数なら最初に出たもの。</summary>
    private sealed class Tally
    {
        private readonly List<(string Id, int Count)> _entries = new();

        public int Total { get; private set; }

        public int DistinctCount => _entries.Count;

        public void Add(string templateId)
        {
            Total++;
            int index = _entries.FindIndex(e => string.Equals(e.Id, templateId, StringComparison.Ordinal));

            if (index < 0)
            {
                _entries.Add((templateId, 1));
            }
            else
            {
                _entries[index] = (templateId, _entries[index].Count + 1);
            }
        }

        public (string Id, int Count) Top()
        {
            var best = _entries[0];

            foreach (var entry in _entries)
            {
                if (entry.Count > best.Count)
                {
                    best = entry;
                }
            }

            return best;
        }
    }
}
