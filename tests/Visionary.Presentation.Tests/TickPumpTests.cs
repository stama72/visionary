namespace Visionary.Presentation.Tests;

public class TickPumpTests
{
    [Fact]
    public void Elapse_BelowOneTick_ReturnsZeroAndThenOneAtThreshold()
    {
        var pump = new TickPump(25_000);

        Assert.Equal(0, pump.Elapse(24_999));
        Assert.Equal(999, pump.ProgressPermille);

        Assert.Equal(1, pump.Elapse(1));
        Assert.Equal(0, pump.ProgressPermille);
    }

    [Fact]
    public void Elapse_MultipleTicksAtOnce_ReturnsAllOfThemAndKeepsRemainder()
    {
        var pump = new TickPump(25_000);

        Assert.Equal(1, pump.Elapse(25_000));
        Assert.Equal(2, pump.Elapse(60_000));
        Assert.Equal(400, pump.ProgressPermille);
    }

    /// <summary>
    /// 核心(#3): <c>Elapse</c> の <c>Paused</c> の早期 return を消すと、止めている間に端数が積まれて
    /// 再開時に一気に進む。変異の実測結果は mutator の報告を待つ。
    /// </summary>
    [Fact]
    public void Elapse_WhilePaused_ReturnsZeroAndDoesNotAccumulate()
    {
        var pump = new TickPump(25_000);
        pump.Elapse(60_000);
        Assert.Equal(400, pump.ProgressPermille);

        pump.Paused = true;
        Assert.Equal(0, pump.Elapse(30_000));
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
    public void Constructor_LessThanOneMillisecond_Throws(int ms)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TickPump(ms));
    }
}
