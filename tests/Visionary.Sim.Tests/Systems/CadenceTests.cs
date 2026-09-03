using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Systems;

public sealed class CadenceTests
{
    private static int CountMatches(Cadence cadence, long fromTickValueInclusive, long toTickValueInclusive)
    {
        int count = 0;

        for (long value = fromTickValueInclusive; value <= toTickValueInclusive; value++)
        {
            if (cadence.ShouldRunAt(new Tick(value)))
            {
                count++;
            }
        }

        return count;
    }

    [Fact]
    public void EveryTickCadenceRunsOnEveryTick()
    {
        Assert.Equal(24, CountMatches(Cadence.EveryTick(), fromTickValueInclusive: 1, toTickValueInclusive: 24));
    }

    [Fact]
    public void DailyCadenceRunsOncePerDayAtGivenHour()
    {
        const int hour = 5;
        var cadence = Cadence.Daily(hour);

        int matches = 0;

        for (long value = 1; value <= 72; value++)
        {
            var tick = new Tick(value);

            if (cadence.ShouldRunAt(tick))
            {
                matches++;
                Assert.Equal(hour, tick.HourOfDay);
            }
        }

        Assert.Equal(3, matches);
    }

    [Fact]
    public void WeeklyCadenceRunsEverySevenDays()
    {
        var cadence = Cadence.Weekly(dayOfWeekIndex: 0, hour: 0);

        Assert.Equal(3, CountMatches(cadence, fromTickValueInclusive: 1, toTickValueInclusive: 21 * Tick.HoursPerDay));
    }

    /// <summary>
    /// 1季 = 30日は7で割り切れないため、週内日は季節の境界をまたいで漂う(GDD03 §1.3)。
    /// ここでは「季節内で最初に発火する日が、季節ごとに違う day-of-season になる」ことで示す。
    /// </summary>
    [Fact]
    public void WeeklyCadenceDriftsAcrossSeasonBoundary()
    {
        var cadence = Cadence.Weekly(dayOfWeekIndex: 0, hour: 0);

        // 2季分(60日)を見る。season0 = day-of-year 0〜29、season1 = 30〜59。
        long firstMatchDayOfSeasonInSeason0 = -1;
        long firstMatchDayOfSeasonInSeason1 = -1;

        for (long day = 0; day < Calendar.DaysPerSeason * 2; day++)
        {
            var tick = Tick.FromDays(day);

            if (!cadence.ShouldRunAt(tick))
            {
                continue;
            }

            if (day < Calendar.DaysPerSeason)
            {
                firstMatchDayOfSeasonInSeason0 = day;
                break;
            }
        }

        for (long day = Calendar.DaysPerSeason; day < Calendar.DaysPerSeason * 2; day++)
        {
            var tick = Tick.FromDays(day);

            if (cadence.ShouldRunAt(tick))
            {
                firstMatchDayOfSeasonInSeason1 = day - Calendar.DaysPerSeason;
                break;
            }
        }

        Assert.NotEqual(-1, firstMatchDayOfSeasonInSeason0);
        Assert.NotEqual(-1, firstMatchDayOfSeasonInSeason1);
        Assert.NotEqual(firstMatchDayOfSeasonInSeason0, firstMatchDayOfSeasonInSeason1);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void CadenceRejectsOutOfRangeArguments_Daily(int hour)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Cadence.Daily(hour));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(7, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 24)]
    public void CadenceRejectsOutOfRangeArguments_Weekly(int dayOfWeekIndex, int hour)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Cadence.Weekly(dayOfWeekIndex, hour));
    }

    /// <summary>
    /// 既定値の <see cref="Cadence"/> は使えない。0 をいずれかの周期に割り当てると、
    /// 初期化を忘れたシステムが黙ってその周期で走る。実行時刻は乱数の鍵に入る
    /// (TDD01 §3.1)ため、周期の取り違えは A/B 比較の前提を壊す。
    /// </summary>
    [Fact]
    public void DefaultCadenceIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => default(Cadence).ShouldRunAt(Tick.Zero));
    }
}
