using Visionary.Sim.Commands;
using Visionary.Sim.Time;

namespace Visionary.Sim.Runner;

/// <summary>
/// 台本が見るシムの面(TDD01 §4.1)。<b>Advance も SimHost も World も晒さない</b> ──
/// 台本には時間を進めさせない。
/// </summary>
internal interface IPlayerSession
{
    Tick Now { get; }

    Result Execute(ICommand command);

    ReadModels Query { get; }

    IReadOnlyList<DomainEvent> DrainEvents();
}

/// <summary>プレイヤーの台本。毎時、その tick の処理の前に呼ばれる。</summary>
internal interface IPlayerScript
{
    void OnHour(IPlayerSession session);
}

/// <summary><see cref="SimHost"/> の4メンバーへ委譲するだけ。</summary>
internal sealed class SimHostPlayerSession : IPlayerSession
{
    private readonly SimHost _host;

    public SimHostPlayerSession(SimHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        _host = host;
    }

    public Tick Now => _host.Now;

    public Result Execute(ICommand command) => _host.Execute(command);

    public ReadModels Query => _host.Query;

    public IReadOnlyList<DomainEvent> DrainEvents() => _host.DrainEvents();
}

internal static class ScriptedRun
{
    /// <param name="ticks">回す tick 数。単位: 時間。1 以上。</param>
    /// <remarks>
    /// <c>OnHour</c> が見る時刻 T の tick はまだ処理されていない(コマンドの時刻と同じ意味)。
    /// 台本なしは <c>Advance(ticks)</c> 1回 ── 1時間ずつ刻んでも結果は同じだが、台本なしの経路を
    /// 台本ありの経路と別に保つ(<c>vsim run</c> の既存の速度を変えない)。
    /// </remarks>
    public static void Run(SimHost host, IPlayerScript? script, int ticks)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(ticks, 1);

        if (script is null)
        {
            host.Advance(ticks);
            return;
        }

        var session = new SimHostPlayerSession(host);

        for (int i = 0; i < ticks; i++)
        {
            script.OnHour(session);
            host.Advance(1);
        }
    }
}
