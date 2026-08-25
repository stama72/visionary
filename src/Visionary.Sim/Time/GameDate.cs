namespace Visionary.Sim.Time;

/// <summary>
/// <see cref="Tick"/> を暦(ADR-0003)に写した表現。年は1始まり、日は各季1〜30。
/// </summary>
/// <remarks>
/// 時刻の真実はあくまで <see cref="Tick"/> であり、この型はその読み替えにすぎない。
/// シムの状態として <see cref="GameDate"/> を保存しない — 保存すると暦の定数を変えたときに
/// 過去の状態が別の時刻を指し始める。
/// </remarks>
public readonly record struct GameDate
{
    /// <summary>1始まり。エポック(<see cref="Tick.Zero"/>)は1年の春1日。</summary>
    public int Year { get; }

    public Season Season { get; }

    /// <summary>その季の日(1〜<see cref="Calendar.DaysPerSeason"/>)。</summary>
    public int DayOfSeason { get; }

    /// <summary>その日の時刻(0〜23)。</summary>
    public int HourOfDay { get; }

    public GameDate(int year, Season season, int dayOfSeason, int hourOfDay)
    {
        if (year < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(year), year, "年は1始まり。");
        }

        if (!Enum.IsDefined(season))
        {
            throw new ArgumentOutOfRangeException(nameof(season), season, "未定義の季節。");
        }

        if (dayOfSeason < 1 || dayOfSeason > Calendar.DaysPerSeason)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dayOfSeason), dayOfSeason, $"日は1〜{Calendar.DaysPerSeason}。");
        }

        if (hourOfDay < 0 || hourOfDay >= Tick.HoursPerDay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hourOfDay), hourOfDay, $"時刻は0〜{Tick.HoursPerDay - 1}。");
        }

        Year = year;
        Season = season;
        DayOfSeason = dayOfSeason;
        HourOfDay = hourOfDay;
    }

    public static GameDate FromTick(Tick tick)
    {
        long dayIndex = tick.DayIndex;
        long dayOfYear = dayIndex % Calendar.DaysPerYear;

        return new GameDate(
            year: checked((int)((dayIndex / Calendar.DaysPerYear) + 1)),
            season: (Season)(dayOfYear / Calendar.DaysPerSeason),
            dayOfSeason: (int)(dayOfYear % Calendar.DaysPerSeason) + 1,
            hourOfDay: tick.HourOfDay);
    }

    public Tick ToTick()
    {
        long dayIndex = ((long)(Year - 1) * Calendar.DaysPerYear)
            + ((long)Season * Calendar.DaysPerSeason)
            + (DayOfSeason - 1);

        return Tick.FromDays(dayIndex).AddHours(HourOfDay);
    }

    /// <summary>デバッグ・ログ用。プレイヤーに見せる書式はプレゼンテーション層が持つ。</summary>
    public override string ToString() => $"Y{Year}-{Season}-{DayOfSeason:00}T{HourOfDay:00}";
}
