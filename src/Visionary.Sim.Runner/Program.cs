using System.Globalization;
using Visionary.Sim.Determinism;
using Visionary.Sim.Randomness;
using Visionary.Sim.Runner.Determinism;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Runner;

/// <summary>
/// ヘッドレス実験ハーネス(TDD01 §4)のエントリポイント。
/// M0 の各サブコマンド(run / promise-table / dialogue-sample)は W2 以降に実装する。
/// </summary>
internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitUsage = 64;

    // NPC 30〜50体(TDD01 §3.6)の中央。
    private const int DefaultNpcCount = 40;

    private static int Main(string[] args)
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

            default:
                Console.Error.WriteLine($"未実装のコマンド: {args[0]}");
                PrintUsage();
                return ExitUsage;
        }
    }

    /// <summary>
    /// <c>vsim hash --seed &lt;long&gt; --ticks &lt;int&gt; [--npcs &lt;int&gt;]</c>。
    /// 状態ハッシュ(TDD01 §3.8)を標準出力に1行だけ書く。診断情報は stderr へ
    /// — CI がシェルで stdout を比較するため、他の文字を混ぜない(仕様)。
    /// </summary>
    private static int RunHash(string[] args)
    {
        long? seed = null;
        long? ticks = null;
        long npcs = DefaultNpcCount;

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

                case "--npcs":
                    if (!TryParseLongArgument(args, ref i, out var npcsValue))
                    {
                        PrintUsage();
                        return ExitUsage;
                    }

                    npcs = npcsValue;
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

        // ticks は SimScheduler.Advance が0以下を拒否する(1以上)。npcs は
        // SyntheticLoadSystem が rng.NextInt(0, Npcs.Length) で相手NPCを選ぶため2以上(仕様)。
        if (ticks < 1 || ticks > int.MaxValue || npcs < 2 || npcs > int.MaxValue)
        {
            Console.Error.WriteLine("--ticks は1以上、--npcs は2以上。");
            PrintUsage();
            return ExitUsage;
        }

        var world = new World((int)npcs);
        var random = new RandomSource(seed.Value);

        // `hash` は TDD01 §4.1 と CI に載る恒久コマンドだが、中身は W1 限りの合成システムに
        // 依存している。W2 で TDD01 §3.3 の本物のシステム群が揃ったら、ここを §3.3 の登録順で
        // 差し替えること(合成システム側のファイル冒頭の注記だけでは、削除の起点であるこの配線に
        // 気づけないため、ここにも書く)。
        var scheduler = new SimScheduler(
            new ISimSystem[] { new SyntheticLoadSystem(), new SyntheticDecaySystem() }, random);

        scheduler.Advance(world, (int)ticks.Value);

        ulong hash = StateHasher.Compute(world);
        Console.WriteLine(hash.ToString("X16", CultureInfo.InvariantCulture));

        return ExitSuccess;
    }

    /// <summary>
    /// <paramref name="args"/>[<paramref name="index"/> + 1] を消費して <see cref="long"/> として解釈する。
    /// <see cref="long.TryParse(string?, NumberStyles, IFormatProvider?, out long)"/> +
    /// <see cref="CultureInfo.InvariantCulture"/> で解釈する(<c>InvariantGlobalization</c> が
    /// 有効なので実質不変だが明示する)。
    /// </summary>
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

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            使い方: vsim <command> [options]

            実装済み:
              version            ハーネスのバージョンを表示する
              hash               --seed <n> --ticks <n> [--npcs <n>]  状態ハッシュを標準出力に1行(TDD01 §3.8)

            未実装(TDD01 §4.1 / M0 W2以降):
              run                比較実験を実行する         --config <path> --out <dir>
              promise-table      §2.8 信用式の感度表を出力する
              dialogue-sample    同一NPCとの会話サンプルを出力する  --npc <id> --repeat <n>
            """);
    }
}
