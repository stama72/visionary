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

            default:
                Console.Error.WriteLine($"未実装のコマンド: {args[0]}");
                PrintUsage();
                return ExitUsage;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            使い方: vsim <command> [options]

            実装済み:
              version            ハーネスのバージョンを表示する

            未実装(TDD01 §4.1 / M0 W2以降):
              run                比較実験を実行する         --config <path> --out <dir>
              promise-table      §2.8 信用式の感度表を出力する
              dialogue-sample    同一NPCとの会話サンプルを出力する  --npc <id> --repeat <n>
            """);
    }
}
