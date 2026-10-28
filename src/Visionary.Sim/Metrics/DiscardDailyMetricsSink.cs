namespace Visionary.Sim.Metrics;

/// <summary>
/// 何も書かない <see cref="IDailyMetricsSink"/>。<see cref="SimHost"/> が指標の出力先を渡されなかった
/// ときの既定。<see cref="Systems.MetricsSystem"/> は <see cref="World"/> を書かないので、
/// 捨ててもハッシュは変わらない。
/// </summary>
internal sealed class DiscardDailyMetricsSink : IDailyMetricsSink
{
    public void Write(in DailySnapshot snapshot)
    {
        // 何もしない。
    }
}
