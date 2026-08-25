namespace Visionary.Sim.Time;

/// <summary>
/// ゲーム内時刻。ADR-0002 により 1 tick = ゲーム内1時間、エポック(<see cref="Zero"/>)からの単調増加。
/// </summary>
/// <remarks>
/// 暦(年・季節)への写像は <see cref="GameDate"/> が持つ(ADR-0003)。
/// この型は暦の定数に依存しない — 暦を変えても Tick の意味は変わらない。
/// </remarks>
public readonly record struct Tick : IComparable<Tick>
{
    public const int HoursPerDay = 24;

    public static readonly Tick Zero = new(0);

    /// <summary>エポックからの経過時間数。</summary>
    public long Value { get; }

    public Tick(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value, "Tick はエポックからの単調増加であり、負値を取らない。");
        }

        Value = value;
    }

    /// <summary>エポックからの経過日数(0始まり)。</summary>
    public long DayIndex => Value / HoursPerDay;

    /// <summary>その日の時刻(0〜23)。</summary>
    public int HourOfDay => (int)(Value % HoursPerDay);

    public static Tick FromDays(long days) => new(checked(days * HoursPerDay));

    public Tick AddHours(long hours) => new(checked(Value + hours));

    public Tick AddDays(long days) => AddHours(checked(days * HoursPerDay));

    /// <summary>2時点の間隔(時間数)。</summary>
    public static long operator -(Tick left, Tick right) => checked(left.Value - right.Value);

    public int CompareTo(Tick other) => Value.CompareTo(other.Value);

    public static bool operator <(Tick left, Tick right) => left.Value < right.Value;

    public static bool operator >(Tick left, Tick right) => left.Value > right.Value;

    public static bool operator <=(Tick left, Tick right) => left.Value <= right.Value;

    public static bool operator >=(Tick left, Tick right) => left.Value >= right.Value;

    public override string ToString() => $"D{DayIndex}T{HourOfDay:00}";
}
