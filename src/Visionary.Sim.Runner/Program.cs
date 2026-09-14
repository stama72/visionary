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

    // 合成負荷の任意の値。M0 の規模は GDD02 §2.4(都市5職業 × 2世帯 = 10世帯・NPC 20体)。
    private const int DefaultNpcCount = 40;

    // M0 の世帯数(GDD02 §2.4「都市5職業 × 2世帯 = 10世帯」)。
    private const int DefaultHouseholdCount = 10;

    // M0 の品目数(GDD02 §2.2「M0 は9品目。品目 Id は 0〜8」)。
    private const int DefaultItemCount = 9;

    // 合成初期配置で使う階層の数(GDD08 §2.1 の親方/職人/徒弟)。
    private const int RankCount = 3;

    // 都市の区画数(GDD02 §4.3 の 3×3)。
    private const int DistrictCount = 9;

    // 合成初期配置で熟練度‰ を散らすための歩幅。1001 と互いに素な任意の値。
    private const int SkillSpread = 37;

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
        long households = DefaultHouseholdCount;
        long items = DefaultItemCount;

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

                case "--households":
                    if (!TryParseLongArgument(args, ref i, out var householdsValue))
                    {
                        PrintUsage();
                        return ExitUsage;
                    }

                    households = householdsValue;
                    break;

                case "--items":
                    if (!TryParseLongArgument(args, ref i, out var itemsValue))
                    {
                        PrintUsage();
                        return ExitUsage;
                    }

                    items = itemsValue;
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

        // households と items は 1以上。SyntheticLoadSystem が世帯在庫の添字を
        // rng.NextInt(0, itemCount) で選び、全 NPC がいずれかの世帯に属するため、
        // どちらも 0 だと合成負荷が成立しない(仕様)。
        if (households < 1 || households > int.MaxValue || items < 1 || items > int.MaxValue)
        {
            Console.Error.WriteLine("--households と --items は1以上。");
            PrintUsage();
            return ExitUsage;
        }

        // 合成初期配置は NPC を npcId % householdCount で世帯へ割り当てる。世帯数が NPC 数を
        // 超えると構成員のいない世帯ができ、世帯主を構成員に含められない(GDD08 §2.1)。
        if (households > npcs)
        {
            Console.Error.WriteLine("--households は --npcs 以下。");
            PrintUsage();
            return ExitUsage;
        }

        var world = new World((int)npcs, (int)households, (int)items);
        PlaceSyntheticPopulation(world);

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
    /// <summary>
    /// 合成負荷用の初期配置。<b>M0 の初期配置ではない</b> — 職業・区画・構成員の値は
    /// GDD02 §2.4 が持ち、本物の初期配置は別タスクで入れる。
    /// </summary>
    /// <remarks>
    /// ここで散らすのは、<see cref="StateHasher"/> の Npcs 区分が全 NPC で同じ値にならない
    /// ようにするためである。<b>全員が既定値のままだと、ハッシュから
    /// <see cref="NpcState.HouseholdId"/> や <see cref="NpcState.Rank"/> を落としても値が変わらず、
    /// 回帰テストが素通りする。</b>乱数は使わない — シードに依存しない配置にしておくことで、
    /// 「同一シード2プロセス実行の一致」が配置の再現性に左右されなくなる。
    /// </remarks>
    private static void PlaceSyntheticPopulation(World world)
    {
        int householdCount = world.Households.Length;
        int itemCount = world.Households[0].HouseholdInventory.Length;

        for (int npcId = 0; npcId < world.Npcs.Length; npcId++)
        {
            var npc = world.Npcs[npcId];

            npc.HouseholdId = npcId % householdCount;
            npc.Rank = (NpcRank)(npcId % RankCount);
            npc.SkillPermille = npcId * SkillSpread % 1001;
        }

        // 世帯は「作り直す」。区画Id・世帯主・構成員は不変なので(GDD02 §4.3 / GDD08 §2.1)、
        // 初期配置は既存インスタンスの変異ではなく構築で表す。
        for (int householdId = 0; householdId < householdCount; householdId++)
        {
            // npcId % householdCount で割り当てたので、構成員は householdId から
            // householdCount 刻みで並ぶ。この生成順がそのまま NpcId 昇順になる。
            var memberNpcIds = new List<int>();

            for (int npcId = householdId; npcId < world.Npcs.Length; npcId += householdCount)
            {
                memberNpcIds.Add(npcId);
            }

            world.Households[householdId] = new HouseholdState(
                id: householdId,
                districtId: householdId % DistrictCount,
                headNpcId: memberNpcIds[0],
                memberNpcIds: memberNpcIds.ToArray(),
                itemCount: itemCount);
        }
    }

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
              hash               --seed <n> --ticks <n> [--npcs <n>] [--households <n>] [--items <n>]
                                 状態ハッシュを標準出力に1行(TDD01 §3.8)

            未実装(TDD01 §4.1 / M0 W2以降):
              run                比較実験を実行する         --config <path> --out <dir>
              promise-table      §2.8 信用式の感度表を出力する
              dialogue-sample    同一NPCとの会話サンプルを出力する  --npc <id> --repeat <n>
            """);
    }
}
