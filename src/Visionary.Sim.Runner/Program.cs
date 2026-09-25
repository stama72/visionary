using System.Globalization;
using Visionary.Sim.Determinism;
using Visionary.Sim.Metrics;
using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;
using Visionary.Sim.Verification;

namespace Visionary.Sim.Runner;

/// <summary>
/// ヘッドレス実験ハーネス(TDD01 §4)のエントリポイント。
/// </summary>
internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitUsage = 64;

    private static int Main(string[] args) => Execute(args);

    /// <summary>
    /// <see cref="Main"/> の実体。テスト(<c>Visionary.Sim.Tests</c>、
    /// <c>InternalsVisibleTo</c> 経由)がプロセスを起動せずに直接呼ぶための入口を兼ねる
    /// (W2-20 タスク仕様のテスト表 #20〜#23)。
    /// </summary>
    internal static int Execute(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return ExitUsage;
        }

        switch (args[0])
        {
            case "version":
                Console.WriteLine($"vsim (Visionary.Sim experiment harness) — epoch {Tick.Zero}");
                return ExitSuccess;

            case "hash":
                return RunHash(args);

            case "run":
                return RunRun(args);

            default:
                Console.Error.WriteLine($"未実装のコマンド: {args[0]}");
                PrintUsage();
                return ExitUsage;
        }
    }

    /// <summary>
    /// <c>vsim hash --seed &lt;long&gt; --ticks &lt;int&gt;</c>。
    /// 状態ハッシュ(TDD01 §3.8)を標準出力に1行だけ書く。診断情報は stderr へ
    /// — CI がシェルで stdout を比較するため、他の文字を混ぜない(仕様)。
    /// </summary>
    /// <remarks>
    /// <b>世界は <see cref="WorldDefinition.M0"/> + <see cref="WorldGenerator.Generate"/>、システムは
    /// TDD01 §3.3 の本物の登録順。</b>規模(NPC数・世帯数・品目数)は定義が持つので、CLI から
    /// 規模を渡す選択肢は無い ── 定義と食い違う規模を渡せると同一シードでも別の世界のハッシュが
    /// 出る(W2-20 タスク仕様)。<c>--npcs</c> / <c>--households</c> / <c>--items</c> は W1 の
    /// 合成システム(<c>SyntheticLoadSystem</c> 等)専用だったため、ここで役目を終えて落とす。
    /// </remarks>
    private static int RunHash(string[] args)
    {
        long? seed = null;
        long? ticks = null;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seed":
                    if (!TryParseLongArgument(args, ref i, out var seedValue))
                    {
                        PrintUsage();
                        return ExitUsage;
                    }

                    seed = seedValue;
                    break;

                case "--ticks":
                    if (!TryParseLongArgument(args, ref i, out var ticksValue))
                    {
                        PrintUsage();
                        return ExitUsage;
                    }

                    ticks = ticksValue;
                    break;

                default:
                    Console.Error.WriteLine($"未知のオプション: {args[i]}");
                    PrintUsage();
                    return ExitUsage;
            }
        }

        if (seed is null || ticks is null)
        {
            Console.Error.WriteLine("--seed と --ticks は必須。");
            PrintUsage();
            return ExitUsage;
        }

        // ticks は SimScheduler.Advance が0以下を拒否する(1以上)。
        if (ticks < 1 || ticks > int.MaxValue)
        {
            Console.Error.WriteLine("--ticks は1以上。");
            PrintUsage();
            return ExitUsage;
        }

        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(seed.Value));

        var scheduler = new SimScheduler(BuildPipeline(definition, new NullDailyMetricsSink()), new RandomSource(seed.Value));
        scheduler.Advance(world, (int)ticks.Value);

        ulong hash = StateHasher.Compute(world);
        Console.WriteLine(hash.ToString("X16", CultureInfo.InvariantCulture));

        return ExitSuccess;
    }

    /// <summary>
    /// <c>vsim run --config &lt;path&gt; --out &lt;dir&gt;</c>。設定ファイルの <c>masterSeeds</c>
    /// ごとに <see cref="WorldDefinition.M0"/> を <c>durationDays</c> 日回し、5つの CSV を
    /// <c>&lt;out&gt;/&lt;シード&gt;/</c> の下へ書く(W2-20 タスク仕様「5. vsim run と vsim hash」)。
    /// </summary>
    private static int RunRun(string[] args)
    {
        string? configPath = null;
        string? outDirectory = null;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--config":
                    if (!TryParseStringArgument(args, ref i, out configPath))
                    {
                        PrintUsage();
                        return ExitUsage;
                    }

                    break;

                case "--out":
                    if (!TryParseStringArgument(args, ref i, out outDirectory))
                    {
                        PrintUsage();
                        return ExitUsage;
                    }

                    break;

                default:
                    Console.Error.WriteLine($"未知のオプション: {args[i]}");
                    PrintUsage();
                    return ExitUsage;
            }
        }

        if (configPath is null || outDirectory is null)
        {
            Console.Error.WriteLine("--config と --out は必須。");
            PrintUsage();
            return ExitUsage;
        }

        string json;

        try
        {
            json = File.ReadAllText(configPath);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"設定ファイルを読めない({configPath}): {ex.Message}");
            return ExitUsage;
        }

        string? error = RunConfig.TryParse(json, out var config);

        if (error is not null || config is null)
        {
            Console.Error.WriteLine(error ?? "設定ファイルの解析に失敗した。");
            return ExitUsage;
        }

        var definition = WorldDefinition.M0;
        int ticks = checked(config.DurationDays * Tick.HoursPerDay);
        var verifications = new List<SeedVerification>();

        foreach (long seed in config.MasterSeeds!)
        {
            string seedOutputDirectory = Path.Combine(outDirectory, seed.ToString(CultureInfo.InvariantCulture));

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var world = WorldGenerator.Generate(definition, new RandomSource(seed));

            // accumulator は using の外で作り、Advance の後に Build(seed) を呼ぶ(csvSink の
            // Dispose と順序を絡めない。W2-21 タスク仕様「6.」)。
            var accumulator = new VerificationAccumulator(definition);

            using (var sink = new CsvMetricsSink(seedOutputDirectory))
            {
                var composite = new CompositeDailyMetricsSink(sink, accumulator);
                var scheduler = new SimScheduler(BuildPipeline(definition, composite), new RandomSource(seed));
                scheduler.Advance(world, ticks);
            }

            verifications.Add(accumulator.Build(seed));

            stopwatch.Stop();

            // シードごとに1行(シード・日数・経過ミリ秒・出力先。W2-20 タスク仕様)。
            Console.WriteLine(string.Join(
                ' ',
                seed.ToString(CultureInfo.InvariantCulture),
                config.DurationDays.ToString(CultureInfo.InvariantCulture),
                stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
                seedOutputDirectory));
        }

        // summary.json は <out> 直下に1つ(シードごとのディレクトリの下ではない。TDD01 §4.1)。
        var summary = RunSummaryBuilder.Build(config.DurationDays, verifications);
        SummaryJsonWriter.Write(Path.Combine(outDirectory, "summary.json"), summary);

        return ExitSuccess;
    }

    /// <summary>
    /// TDD01 §3.3 の登録順(順1〜順5、順10)。順6〜順9(Promise / Trust /
    /// UnfairPriceDetection / Rumor)は W3・W4 で実装されるまで存在しないので登録しない。
    /// 順0(OpportunityCost)は M0 ではシステムを持たない(同節)。
    /// </summary>
    private static ISimSystem[] BuildPipeline(WorldDefinition definition, IDailyMetricsSink sink) => new ISimSystem[]
    {
        new ProductionSystem(definition),
        new ConsumptionSystem(definition),
        new HouseholdSystem(definition),
        new NeedGenerationSystem(definition),
        new TradeSystem(definition),
        new MetricsSystem(definition, sink),
    };

    private static bool TryParseLongArgument(string[] args, ref int index, out long value)
    {
        value = 0;

        if (index + 1 >= args.Length)
        {
            return false;
        }

        index++;

        return long.TryParse(
            args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseStringArgument(string[] args, ref int index, out string? value)
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

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            使い方: vsim <command> [options]

            実装済み:
              version            ハーネスのバージョンを表示する
              hash               --seed <n> --ticks <n>
                                 状態ハッシュを標準出力に1行(TDD01 §3.8)
              run                --config <path> --out <dir>
                                 比較実験を実行し、5つのCSVを<out>/<シード>/へ書く

            未実装(TDD01 §4.1 / M0 W3以降):
              promise-table      §2.8 信用式の感度表を出力する
              dialogue-sample    同一NPCとの会話サンプルを出力する  --npc <id> --repeat <n>
            """);
    }
}
