using System.Globalization;
using Visionary.Sim;

namespace Visionary.Presentation.Tests;

public class SmokeRunTests
{
    /// <summary>
    /// 核心(#12): ループの中で <c>host.Advance(1)</c> をもう1回呼ぶ変異で、別に作ったホストとのハッシュが食い違う想定。
    /// 変異の実測結果は mutator の報告を待つ。
    /// </summary>
    [Fact]
    public void Run_Hash_EqualsPlainAdvanceOfSameTicks()
    {
        const long Seed = 20260904;
        const int Ticks = 720;

        var expectedHost = new SimHost(WorldDefinition.M0, Seed);
        expectedHost.Advance(Ticks);
        string expected = expectedHost.StateHash().ToString("X16", CultureInfo.InvariantCulture);

        var host = new SimHost(WorldDefinition.M0, Seed);
        string actual = SmokeRun.Run(host, WorldDefinition.M0.TravelHoursPerDistrict, Ticks);

        Assert.Equal(expected, actual);
        Assert.Equal(Ticks, host.Now.Value);
    }

    [Fact]
    public void Run_ZeroTicks_Throws()
    {
        var host = new SimHost(WorldDefinition.M0, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => SmokeRun.Run(host, 1, 0));
    }
}
