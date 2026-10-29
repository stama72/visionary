namespace Visionary.Presentation;

/// <summary>
/// 実時間から「進める tick の数」を作る時間のポンプ(TDD01 §3.10 / GDD05 §1.1・§1.4)。
/// </summary>
/// <remarks>
/// <b>端数だけを状態に持つ。</b>今が何 tick かは知らず、<see cref="SimHost"/> も持たない
/// (端数はゲーム時間ではない)。呼び出し側が返された数だけ <c>SimHost.Advance(1)</c> を呼ぶ。
/// </remarks>
public sealed class TickPump
{
    /// <summary>実時間 ms / tick(GDD05 §1.1。1日 = 10分)。単位: ミリ秒。</summary>
    public const int DefaultMillisecondsPerTick = 25_000;

    private readonly int _millisecondsPerTick;

    // 常に 0 ≤ 端数 < _millisecondsPerTick。単位: ミリ秒。
    private long _remainderMilliseconds;

    public TickPump(int millisecondsPerTick = DefaultMillisecondsPerTick)
    {
        if (millisecondsPerTick < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(millisecondsPerTick), millisecondsPerTick, "1 tick あたりの実時間は 1 ms 以上。");
        }

        _millisecondsPerTick = millisecondsPerTick;
    }

    /// <summary>立てている間は <see cref="Elapse"/> が 0 を返し、端数も増えない(GDD05 §1.4)。</summary>
    public bool Paused { get; set; }

    /// <summary>tick の中の進み。単位: ‰(0〜999)= 端数 × 1000 ÷ millisecondsPerTick(切り捨て)。</summary>
    public int ProgressPermille => (int)(_remainderMilliseconds * 1000 / _millisecondsPerTick);

    /// <summary>実時間を足し、進める tick の数を返す。上限で丸めない。</summary>
    public int Elapse(long elapsedMilliseconds)
    {
        if (elapsedMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsedMilliseconds), elapsedMilliseconds, "経過時間は 0 以上。");
        }

        if (Paused)
        {
            return 0;
        }

        _remainderMilliseconds += elapsedMilliseconds;
        int ticks = checked((int)(_remainderMilliseconds / _millisecondsPerTick));
        _remainderMilliseconds %= _millisecondsPerTick;

        return ticks;
    }
}
