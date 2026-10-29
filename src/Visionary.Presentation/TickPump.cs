namespace Visionary.Presentation;

/// <summary>
/// 実時間から「進める tick の数」を作る時間のポンプ(TDD01 §3.10 / GDD05 §1.1・§1.4)。
/// </summary>
/// <remarks>
/// <b>端数だけを状態に持つ。</b>今が何 tick かは知らず、<see cref="SimHost"/> も持たない
/// (端数はゲーム時間ではない)。呼び出し側が返された数だけ <c>SimHost.Advance(1)</c> を呼ぶ。
/// <b>単位がマイクロ秒なのは、フレームごとの丸めでゲームの速さがフレームレートに依存しないため。</b>
/// </remarks>
public sealed class TickPump
{
    /// <summary>実時間 µs / tick(GDD05 §1.1。1 tick = 25秒、1日 = 10分)。単位: マイクロ秒。</summary>
    public const long DefaultMicrosecondsPerTick = 25_000_000;

    private readonly long _microsecondsPerTick;

    // 常に 0 ≤ 端数 < _microsecondsPerTick。単位: マイクロ秒。
    private long _remainderMicroseconds;

    public TickPump(long microsecondsPerTick = DefaultMicrosecondsPerTick)
    {
        if (microsecondsPerTick < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(microsecondsPerTick), microsecondsPerTick, "1 tick あたりの実時間は 1 µs 以上。");
        }

        _microsecondsPerTick = microsecondsPerTick;
    }

    /// <summary>立てている間は <see cref="Elapse"/> が 0 を返し、端数も増えない(GDD05 §1.4)。</summary>
    public bool Paused { get; set; }

    /// <summary>tick の中の進み。単位: ‰(0〜999)= 端数 × 1000 ÷ microsecondsPerTick(切り捨て)。</summary>
    public int ProgressPermille => (int)(_remainderMicroseconds * 1000 / _microsecondsPerTick);

    /// <summary>実時間(µs)を足し、進める tick の数を返す。上限で丸めない。</summary>
    public int Elapse(long elapsedMicroseconds)
    {
        if (elapsedMicroseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsedMicroseconds), elapsedMicroseconds, "経過時間は 0 以上。");
        }

        if (Paused)
        {
            return 0;
        }

        _remainderMicroseconds += elapsedMicroseconds;
        int ticks = checked((int)(_remainderMicroseconds / _microsecondsPerTick));
        _remainderMicroseconds %= _microsecondsPerTick;

        return ticks;
    }
}
