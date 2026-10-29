using Visionary.Sim;

namespace Visionary.Presentation.Tests;

public class GameLoopTests
{
    /// <summary>
    /// 核心(#12): <c>Frame</c> の中で <c>Host.Advance(1)</c> をもう1回呼ぶ変異で、別に作ったホストとのハッシュが
    /// 食い違う想定。変異の実測結果は mutator の報告を待つ。
    /// </summary>
    [Fact]
    public void Frame_720Frames_HashEqualsPlainAdvanceAndReadsDoNotWriteWorld()
    {
        const long Seed = 20260904;
        const int Ticks = 720;
        int travel = WorldDefinition.M0.TravelHoursPerDistrict;

        var expectedHost = new SimHost(WorldDefinition.M0, Seed);
        expectedHost.Advance(Ticks);

        var host = new SimHost(WorldDefinition.M0, Seed);
        var loop = new GameLoop(host, new TickPump());

        for (int i = 0; i < Ticks; i++)
        {
            Assert.Equal(1, loop.Frame(TickPump.DefaultMicrosecondsPerTick));

            // 描画と同じ読み出し。結果は捨てる(World を書かないことを踏む)。
            foreach (var household in host.Query.Households())
            {
                foreach (int npcId in household.MemberNpcIds)
                {
                    MapLayout.NpcPosition(host.Query.TimetableOf(npcId), npcId, loop.Pump.ProgressPermille, travel);
                }
            }

            MapLayout.PlayerPosition(host.Query.Player(), host.Now, loop.Pump.ProgressPermille, travel);
        }

        Assert.Equal(Ticks, host.Now.Value);
        Assert.Equal(expectedHost.StateHash(), host.StateHash());
    }

    [Fact]
    public void Frame_SixtyMillionMicroseconds_AdvancesTwoTicks()
    {
        var host = new SimHost(WorldDefinition.M0, 1);
        var loop = new GameLoop(host, new TickPump());
        long before = host.Now.Value;

        Assert.Equal(2, loop.Frame(60_000_000));
        Assert.Equal(before + 2, host.Now.Value);
    }

    [Fact]
    public void Constructor_Null_Throws()
    {
        var host = new SimHost(WorldDefinition.M0, 1);

        Assert.Throws<ArgumentNullException>(() => new GameLoop(null!, new TickPump()));
        Assert.Throws<ArgumentNullException>(() => new GameLoop(host, null!));
    }
}
