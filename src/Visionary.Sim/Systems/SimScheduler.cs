using Visionary.Sim.Randomness;

namespace Visionary.Sim.Systems;

/// <summary>
/// 登録されたシステムを、tickを1つずつ進めながら実行する(TDD01 §3.1 / §3.3)。
/// </summary>
public sealed class SimScheduler
{
    private readonly ISimSystem[] _systemsInPipelineOrder;
    private readonly SimContext _context;

    /// <summary>
    /// <paramref name="systemsInPipelineOrder"/> の並びで登録する。
    /// **登録順が仕様**(TDD01 §3.3)。呼び出し側が §3.3 の順に並べた配列を渡すこと。
    /// </summary>
    /// <exception cref="ArgumentException">同じ <see cref="ISimSystem.Stream"/> を持つシステムが2つ以上あるとき。</exception>
    public SimScheduler(IReadOnlyList<ISimSystem> systemsInPipelineOrder, RandomSource random)
    {
        ArgumentNullException.ThrowIfNull(systemsInPipelineOrder);

        // 系統の重複登録は共通乱数法を壊す(SimContext.OpenRandomが同じ鍵を2システムに配ることになる)。
        // 要素数が小規模(系統の総数程度)なので O(n^2) の直接比較で足り、
        // 列挙順が問題になる中間コレクションを一切要らない。
        for (int i = 0; i < systemsInPipelineOrder.Count; i++)
        {
            for (int j = i + 1; j < systemsInPipelineOrder.Count; j++)
            {
                if (systemsInPipelineOrder[i].Stream == systemsInPipelineOrder[j].Stream)
                {
                    throw new ArgumentException(
                        $"系統 {systemsInPipelineOrder[i].Stream} を持つシステムが重複登録されている"
                            + "(共通乱数法が壊れる)。",
                        nameof(systemsInPipelineOrder));
                }
            }
        }

        // 防御的コピー。呼び出し側が List を渡していると、構築後に系統を重複追加して
        // 検証を素通りさせたり、順序を変えたりできてしまう。「登録順が仕様」(TDD01 §3.3)は
        // 構築時点で固定する。
        _systemsInPipelineOrder = systemsInPipelineOrder.ToArray();
        _context = new SimContext(random);
    }

    /// <summary>
    /// 1tickずつ、<paramref name="ticks"/> 回進める。各tickで登録順に全システムを見て、
    /// <see cref="Cadence.ShouldRunAt"/> が真のものだけ <see cref="ISimSystem.Step"/> する。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ticks"/> が0以下のとき。</exception>
    public void Advance(World world, int ticks)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (ticks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "進めるtick数は正の値。");
        }

        // 現在tickを処理してから次へ進める。先に進めてから処理すると、エポック
        // (1年 春1日 0時 = Tick.Zero)が永久に処理されず、Daily(hour: 0) のシステムが
        // 春1日を飛ばして春2日から始まる。
        for (int i = 0; i < ticks; i++)
        {
            _context.AdvanceTo(world.Now);

            foreach (var system in _systemsInPipelineOrder)
            {
                if (!system.Cadence.ShouldRunAt(world.Now))
                {
                    continue;
                }

                _context.CurrentStream = system.Stream;

                try
                {
                    system.Step(world, _context);
                }
                finally
                {
                    // Step を抜けたら系統を無効値に戻す
                    _context.ClearCurrentStream();
                }
            }

            world.Now = world.Now.AddHours(1);
        }
    }
}
