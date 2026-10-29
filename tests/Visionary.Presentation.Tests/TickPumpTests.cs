namespace Visionary.Presentation.Tests;

public class TickPumpTests
{
    private const long MicrosecondsPerTick = 25_000_000;

    [Fact]
    public void Elapse_BelowOneTick_ReturnsZeroAndThenOneAtThreshold()
    {
        var pump = new TickPump(MicrosecondsPerTick);

        Assert.Equal(0, pump.Elapse(24_999_999));
        Assert.Equal(999, pump.ProgressPermille);

        Assert.Equal(1, pump.Elapse(1));
        Assert.Equal(0, pump.ProgressPermille);
    }

    [Fact]
    public void Elapse_MultipleTicksAtOnce_ReturnsAllOfThemAndKeepsRemainder()
    {
        var pump = new TickPump(MicrosecondsPerTick);

        Assert.Equal(1, pump.Elapse(25_000_000));
        Assert.Equal(2, pump.Elapse(60_000_000));
        Assert.Equal(400, pump.ProgressPermille);
    }

    /// <summary>
    /// 核心(#2b): <c>Elapse</c> の先頭で <c>elapsed = elapsed / 1000 * 1000</c> とする(ミリ秒に丸める)変異で、
    /// 240 Hz の例が 1 tick に届かず、両方の例が赤になる想定。
    /// 実測(出所: mutator の実測(7ab797e)): 検査の後に <c>elapsed = elapsed / 1000 * 1000</c> → 赤。
    /// 落ちたのは本テストの 60 Hz 側 <c>Assert.Equal(1, total60)</c>(Expected 1 / Actual 0)と
    /// <c>Elapse_BelowOneTick_ReturnsZeroAndThenOneAtThreshold</c>。
    /// 240 Hz 側の assert は 60 Hz 側で止まるため実行されておらず、単独では未測。
    /// </summary>
    [Fact]
    public void Elapse_SameRealTimeAtDifferentFrameRates_YieldsOneTickEach()
    {
        var pump60 = new TickPump();
        int total60 = 0;
        for (int i = 0; i < 1_500; i++)
        {
            total60 += pump60.Elapse(16_667);   // 60 Hz の 1 フレーム。計 25,000,500 µs
        }

        var pump240 = new TickPump();
        int total240 = 0;
        for (int i = 0; i < 6_000; i++)
        {
            total240 += pump240.Elapse(4_167);   // 240 Hz の 1 フレーム。計 25,002,000 µs
        }

        Assert.Equal(1, total60);
        Assert.Equal(1, total240);
    }

    /// <summary>
    /// 核心(#3): <c>Elapse</c> の <c>Paused</c> の早期 return を消すと、止めている間に端数が積まれて
    /// 再開時に一気に進む。
    /// 実測(出所: mutator の実測(7ab797e)): <c>Paused</c> の早期 return を消す変異 → 赤(本テストが落ちた)。
    /// </summary>
    [Fact]
    public void Elapse_WhilePaused_ReturnsZeroAndDoesNotAccumulate()
    {
        var pump = new TickPump(MicrosecondsPerTick);
        pump.Elapse(60_000_000);
        Assert.Equal(400, pump.ProgressPermille);

        pump.Paused = true;
        Assert.Equal(0, pump.Elapse(30_000_000));
        Assert.Equal(400, pump.ProgressPermille);

        pump.Paused = false;
        Assert.Equal(0, pump.Elapse(0));
        Assert.Equal(400, pump.ProgressPermille);
    }

    [Fact]
    public void Elapse_Negative_Throws()
    {
        var pump = new TickPump();

        Assert.Throws<ArgumentOutOfRangeException>(() => pump.Elapse(-1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Constructor_LessThanOneMicrosecond_Throws(long microseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TickPump(microseconds));
    }
}
