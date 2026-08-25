namespace Visionary.Sim.Time;

/// <summary>
/// 暦の構造(ADR-0003)。1年 = 4季 × 30日 = 120日。月の概念は持たない。
/// </summary>
public static class Calendar
{
    /// <summary>1季の日数。28日(週と整除)ではなく30日を採った経緯は ADR-0003 を参照。</summary>
    public const int DaysPerSeason = 30;

    public const int SeasonsPerYear = 4;

    public const int DaysPerYear = DaysPerSeason * SeasonsPerYear;

    public const int HoursPerYear = DaysPerYear * Tick.HoursPerDay;
}
