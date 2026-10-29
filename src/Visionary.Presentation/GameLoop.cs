using Visionary.Sim;

namespace Visionary.Presentation;

/// <summary>
/// 1フレームぶんの進め方。<c>Main._Process</c> はこれを呼ぶだけ(通常の起動も煙試験も同じ経路)。
/// </summary>
/// <remarks>
/// <b><c>SimHost.Advance</c> を呼ぶのはここだけ。</b>Game のノードは直接呼ばない。
/// </remarks>
public sealed class GameLoop
{
    public GameLoop(SimHost host, TickPump pump)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(pump);

        Host = host;
        Pump = pump;
    }

    public SimHost Host { get; }

    public TickPump Pump { get; }

    /// <summary><c>Pump.Elapse(elapsedMicroseconds)</c> の数だけ <c>Host.Advance(1)</c> を呼び、その数を返す。単位: マイクロ秒。</summary>
    public int Frame(long elapsedMicroseconds)
    {
        int ticks = Pump.Elapse(elapsedMicroseconds);

        for (int i = 0; i < ticks; i++)
        {
            Host.Advance(1);
        }

        return ticks;
    }
}
