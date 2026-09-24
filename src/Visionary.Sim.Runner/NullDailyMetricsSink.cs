using Visionary.Sim.Metrics;

namespace Visionary.Sim.Runner;

/// <summary>
/// 何も書かない <see cref="IDailyMetricsSink"/>。<c>vsim hash</c> が
/// <see cref="Visionary.Sim.Systems.MetricsSystem"/> を TDD01 §3.3 の本物の登録順に含めるため
/// だけに使う ── ハッシュには影響しない(<see cref="Visionary.Sim.Systems.MetricsSystem"/> は
/// <see cref="World"/> を書き換えない)ので、CSV を書く必要が無い。
/// </summary>
internal sealed class NullDailyMetricsSink : IDailyMetricsSink
{
    public void Write(in DailySnapshot snapshot)
    {
        // 何もしない。
    }
}
