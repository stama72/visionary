using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Time;

public sealed class GameDateTests
{
    /// <summary>ADR-0003 の決定そのもの。変更は ADR の改訂を伴うべきなので、テストで固定する。</summary>
    [Fact]
    public void 一年は四季かける三十日である()
    {
        Assert.Equal(30, Calendar.DaysPerSeason);
        Assert.Equal(4, Calendar.SeasonsPerYear);
        Assert.Equal(120, Calendar.DaysPerYear);
    }

    [Fact]
    public void エポックは一年の春一日零時である()
    {
        var date = GameDate.FromTick(Tick.Zero);

        Assert.Equal(new GameDate(1, Season.Spring, 1, 0), date);
    }

    [Theory]
    // 季節の境界
    [InlineData(29, 1, Season.Spring, 30)]
    [InlineData(30, 1, Season.Summer, 1)]
    [InlineData(89, 1, Season.Autumn, 30)]
    [InlineData(90, 1, Season.Winter, 1)]
    // 年の境界
    [InlineData(119, 1, Season.Winter, 30)]
    [InlineData(120, 2, Season.Spring, 1)]
    public void 季節と年の境界をまたぐ(
        long dayIndex, int expectedYear, Season expectedSeason, int expectedDayOfSeason)
    {
        var date = GameDate.FromTick(Tick.FromDays(dayIndex));

        Assert.Equal(expectedYear, date.Year);
        Assert.Equal(expectedSeason, date.Season);
        Assert.Equal(expectedDayOfSeason, date.DayOfSeason);
    }

    /// <summary>
    /// 1年ぶんを1時間刻みで総当たりし、Tick → GameDate → Tick が恒等になることを確認する。
    /// 暦の計算は境界の off-by-one が入り込みやすく、そこがずれると
    /// 季節依存の経済(GDD03)が静かに1日ずれる形で壊れる。
    /// </summary>
    [Fact]
    public void 一年分の全時刻で往復が恒等になる()
    {
        for (long hour = 0; hour < Calendar.HoursPerYear; hour++)
        {
            var tick = new Tick(hour);

            Assert.Equal(tick, GameDate.FromTick(tick).ToTick());
        }
    }

    [Fact]
    public void 範囲外の日は構築できない()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new GameDate(1, Season.Spring, Calendar.DaysPerSeason + 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameDate(0, Season.Spring, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new GameDate(1, Season.Spring, 1, Tick.HoursPerDay));
    }

    /// <summary>
    /// TDD01 §3.1 / §5.1 の検証地平。ADR-0003 で「100年」から日数基準に改めたため、
    /// 性能予算の根拠になる tick 数をここで固定する。
    /// </summary>
    [Fact]
    public void 検証地平は三万六千日で八十六万四千tickである()
    {
        const int horizonDays = 36_000;

        Assert.Equal(300, horizonDays / Calendar.DaysPerYear);
        Assert.Equal(864_000, Tick.FromDays(horizonDays).Value);
    }
}
