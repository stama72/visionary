using Visionary.Sim.Commands;
using Visionary.Sim.Determinism;
using Visionary.Sim.Metrics;
using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim;

/// <summary>
/// シムの入力口(TDD01 §3.5)。時間は <see cref="Advance"/> だけが進め、プレイヤーの操作は
/// <see cref="Execute"/> だけが World を書く。
/// </summary>
public sealed class SimHost
{
    private readonly SimScheduler _scheduler;
    private readonly PlayerCommandExecutor _executor;
    private int _drainedEventCount;   // 読み出し位置。World の状態ではない(ハッシュに乗せない)

    /// <param name="startDay">
    /// 開始日。単位: 日。0 より大きければ生成直後に <c>startDay × 24</c> tick 先回しする。
    /// 先回しは通常の <see cref="Advance"/> と同じ経路を通る(別の乱数源を使わない)。
    /// </param>
    public SimHost(WorldDefinition definition, long masterSeed, int startDay = 0, IDailyMetricsSink? metricsSink = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (startDay < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startDay), startDay, "開始日は非負。");
        }

        World = WorldGenerator.Generate(definition, new RandomSource(masterSeed));
        _scheduler = new SimScheduler(
            SimPipeline.Build(definition, metricsSink ?? new DiscardDailyMetricsSink()),
            new RandomSource(masterSeed));
        _executor = new PlayerCommandExecutor(definition);
        Query = new ReadModels(definition, World);

        if (startDay > 0)
        {
            Advance(checked(startDay * Tick.HoursPerDay));
        }
    }

    /// <summary>現在 tick(= <see cref="World"/> の <c>Now</c>)。</summary>
    public Tick Now => World.Now;

    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ticks"/> が0以下のとき。</exception>
    public void Advance(int ticks) => _scheduler.Advance(World, ticks);

    /// <summary>
    /// コマンドを1つ実行する。検証エラーは <see cref="Result"/> で返す。例外は呼び出し側の誤り
    /// (null・未知の型)とモデルの番人(<c>InvalidOperationException</c>)だけ。
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> が null のとき。</exception>
    /// <exception cref="ArgumentException"><paramref name="command"/> が未知の型のとき。</exception>
    public Result Execute(ICommand command) => _executor.Execute(World, command);

    public ulong StateHash() => StateHasher.Compute(World);

    /// <summary>読み出しモデル(TDD01 §3.5)。World を書かない。</summary>
    public ReadModels Query { get; }

    /// <summary>
    /// 前回の <see cref="DrainEvents"/> 以降に追記されたイベントを、追記順に新しい List へ写して返す。
    /// <see cref="World.EventLog"/> は消さない(追記専用。TDD01 §3.4)。読み出し位置は World の状態ではない。
    /// </summary>
    public IReadOnlyList<DomainEvent> DrainEvents()
    {
        var log = World.EventLog;
        var result = log.GetRange(_drainedEventCount, log.Count - _drainedEventCount);

        _drainedEventCount = log.Count;
        return result;
    }

    /// <summary>テスト用。読み出しの公開口は #275 の Query。</summary>
    internal World World { get; }
}
