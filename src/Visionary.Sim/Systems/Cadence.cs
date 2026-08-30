using Visionary.Sim.Time;

namespace Visionary.Sim.Systems;

/// <summary>
/// システムの実行周期(TDD01 §3.1)。
/// </summary>
public readonly struct Cadence
{
    private enum Kind
    {
        // 0 は未設定を表す。既定値をいずれかの周期にすると、Cadence を初期化し忘れた
        // システムが黙ってその周期で走る。実行時刻は乱数の鍵に入る(TDD01 §3.1)ので、
        // 周期の取り違えは A/B 比較の前提そのものを壊す。
        Unset = 0,
        EveryTick,
        Daily,
        Weekly,
    }

    private readonly Kind _kind;
    private readonly int _hour;
    private readonly int _dayOfWeekIndex;

    private Cadence(Kind kind, int hour, int dayOfWeekIndex)
    {
        _kind = kind;
        _hour = hour;
        _dayOfWeekIndex = dayOfWeekIndex;
    }

    /// <summary>毎tick実行する。</summary>
    public static Cadence EveryTick() => new(Kind.EveryTick, hour: 0, dayOfWeekIndex: 0);

    /// <summary>毎日 <paramref name="hour"/> 時に実行する。</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hour"/> が0〜23の外。</exception>
    public static Cadence Daily(int hour)
    {
        ValidateHour(hour);

        return new Cadence(Kind.Daily, hour, dayOfWeekIndex: 0);
    }

    /// <summary>
    /// 週内日 <paramref name="dayOfWeekIndex"/> の <paramref name="hour"/> 時に実行する。
    /// </summary>
    /// <remarks>
    /// 週は暦の一部ではなく単なる7日周期(GDD03 §1.3)。<c>dayIndex % 7 == dayOfWeekIndex</c> で
    /// 判定するため、季節の境界(30日)をまたいで週内日が漂う。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="hour"/> が0〜23の外、または <paramref name="dayOfWeekIndex"/> が0〜6の外。
    /// </exception>
    public static Cadence Weekly(int dayOfWeekIndex, int hour)
    {
        ValidateHour(hour);

        if (dayOfWeekIndex is < 0 or > 6)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dayOfWeekIndex), dayOfWeekIndex, "週内日は0〜6。");
        }

        return new Cadence(Kind.Weekly, hour, dayOfWeekIndex);
    }

    /// <summary>この周期が <paramref name="tick"/> で実行されるべきかどうか。</summary>
    public bool ShouldRunAt(Tick tick) => _kind switch
    {
        Kind.EveryTick => true,
        Kind.Daily => tick.HourOfDay == _hour,
        Kind.Weekly => tick.DayIndex % 7 == _dayOfWeekIndex && tick.HourOfDay == _hour,
        Kind.Unset => throw new InvalidOperationException(
            "Cadence が未設定。既定値の Cadence は使えない。"
                + "EveryTick / Daily / Weekly のいずれかで明示的に構築すること。"),
        _ => throw new InvalidOperationException($"未知の Cadence.Kind: {_kind}"),
    };

    private static void ValidateHour(int hour)
    {
        if (hour < 0 || hour >= Tick.HoursPerDay)
        {
            throw new ArgumentOutOfRangeException(nameof(hour), hour, $"時刻は0〜{Tick.HoursPerDay - 1}。");
        }
    }
}
