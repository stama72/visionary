using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Time;

public sealed class TickTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(23, 0, 23)]
    [InlineData(24, 1, 0)]
    [InlineData(25, 1, 1)]
    [InlineData(8_760, 365, 0)]
    public void 時間数から日と時刻を導出する(long value, long expectedDay, int expectedHour)
    {
        var tick = new Tick(value);

        Assert.Equal(expectedDay, tick.DayIndex);
        Assert.Equal(expectedHour, tick.HourOfDay);
    }

    [Fact]
    public void 負のtickは構築できない()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tick(-1));
    }

    [Fact]
    public void 加算と差分が往復する()
    {
        var start = Tick.FromDays(10);
        var later = start.AddDays(3).AddHours(5);

        Assert.Equal((3 * Tick.HoursPerDay) + 5, later - start);
        Assert.True(start < later);
        Assert.True(later >= start);
    }

    /// <summary>
    /// TDD01 §3.1 が性能予算の根拠にしている「100年 = 876,000 tick」を型の側から固定する。
    /// tick 粒度を変える判断をしたとき、この数字を更新し忘れると性能目標が破綻するため。
    /// </summary>
    [Fact]
    public void 百年は876000tickである()
    {
        Assert.Equal(876_000, Tick.FromDays(100 * 365).Value);
    }
}
