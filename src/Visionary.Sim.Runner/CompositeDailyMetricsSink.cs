using Visionary.Sim.Metrics;

namespace Visionary.Sim.Runner;

/// <summary>
/// 複数の <see cref="IDailyMetricsSink"/> へ同じスナップショットを流す(W2-21 タスク仕様「6.」)。
/// </summary>
/// <remarks>
/// <b>順序は構築時に渡した配列の順。</b>CSV を先に置くのは、accumulator が例外を投げても
/// CSV の書き出しが失われないほうがよいためである(タスク仕様「呼び出し側を持たないコード」)。
/// </remarks>
internal sealed class CompositeDailyMetricsSink : IDailyMetricsSink, IDisposable
{
    private readonly IDailyMetricsSink[] _sinks;

    public CompositeDailyMetricsSink(params IDailyMetricsSink[] sinks)
    {
        ArgumentNullException.ThrowIfNull(sinks);

        _sinks = sinks;
    }

    public void Write(in DailySnapshot snapshot)
    {
        foreach (var sink in _sinks)
        {
            sink.Write(in snapshot);
        }
    }

    /// <summary><see cref="IDisposable"/> なものだけを、同じ順で捨てる。</summary>
    public void Dispose()
    {
        foreach (var sink in _sinks)
        {
            if (sink is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
