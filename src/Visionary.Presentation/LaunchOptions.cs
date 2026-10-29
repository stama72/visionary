using System.Globalization;

namespace Visionary.Presentation;

/// <summary>
/// 起動引数(TDD01 §3.10)。<c>--名前=値</c> の形で、名前は seed / start-day / smoke-ticks の3つ。
/// </summary>
/// <param name="Seed">マスターシード。</param>
/// <param name="StartDay">開始日(0 以上。TDD01 §3.5)。</param>
/// <param name="SmokeTicks">煙試験で進める tick 数(1 以上)。null なら通常起動。</param>
public sealed record LaunchOptions(long Seed, int StartDay, int? SmokeTicks)
{
    public const long DefaultSeed = 1;

    public static readonly LaunchOptions Default = new(DefaultSeed, 0, null);

    /// <summary>
    /// 失敗なら false と空でない <paramref name="error"/>(<paramref name="options"/> は <see cref="Default"/>)。
    /// </summary>
    public static bool TryParse(IReadOnlyList<string> args, out LaunchOptions options, out string error)
    {
        ArgumentNullException.ThrowIfNull(args);

        long seed = DefaultSeed;
        int startDay = 0;
        int? smokeTicks = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string arg in args)
        {
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                return Fail($"引数は --名前=値 の形: {arg}", out options, out error);
            }

            int eq = arg.IndexOf('=', StringComparison.Ordinal);

            if (eq < 0)
            {
                return Fail($"値がない(= が無い): {arg}", out options, out error);
            }

            string name = arg.Substring(2, eq - 2);
            string value = arg.Substring(eq + 1);

            if (name != "seed" && name != "start-day" && name != "smoke-ticks")
            {
                return Fail($"知らない引数: {arg}", out options, out error);
            }

            if (!seen.Add(name))
            {
                return Fail($"同じ引数が2回: --{name}", out options, out error);
            }

            switch (name)
            {
                case "seed":
                    if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
                    {
                        return Fail($"整数でない: {arg}", out options, out error);
                    }

                    break;

                case "start-day":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out startDay)
                        || startDay < 0)
                    {
                        return Fail($"0 以上の整数でない: {arg}", out options, out error);
                    }

                    break;

                default:
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ticks)
                        || ticks < 1)
                    {
                        return Fail($"1 以上の整数でない: {arg}", out options, out error);
                    }

                    smokeTicks = ticks;
                    break;
            }
        }

        options = new LaunchOptions(seed, startDay, smokeTicks);
        error = string.Empty;
        return true;
    }

    private static bool Fail(string message, out LaunchOptions options, out string error)
    {
        options = Default;
        error = message;
        return false;
    }
}
