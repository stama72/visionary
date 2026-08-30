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
    public void DerivesDayAndHourFromHours(long value, long expectedDay, int expectedHour)
    {
        var tick = new Tick(value);

        Assert.Equal(expectedDay, tick.DayIndex);
        Assert.Equal(expectedHour, tick.HourOfDay);
    }

    [Fact]
    public void RejectsNegativeValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Tick(-1));
    }

    [Fact]
    public void AdditionAndDifferenceRoundTrip()
    {
        var start = Tick.FromDays(10);
        var later = start.AddDays(3).AddHours(5);

        Assert.Equal((3 * Tick.HoursPerDay) + 5, later - start);
        Assert.True(start < later);
        Assert.True(later >= start);
    }

    /// <summary>
    /// tick 粒度(ADR-0002)を型の側から固定する。ここを変えると TDD01 §3.1 の性能予算が
    /// まるごと変わるため、暦(ADR-0003)とは独立に押さえておく。
    /// 検証地平そのものの tick 数は <see cref="GameDateTests"/> 側で固定する。
    /// </summary>
    [Fact]
    public void DayIsTwentyFourTicks()
    {
        Assert.Equal(24, Tick.HoursPerDay);
        Assert.Equal(24_000, Tick.FromDays(1_000).Value);
    }
}
