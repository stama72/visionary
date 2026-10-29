using Visionary.Sim;
using Visionary.Sim.Commands;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Presentation.Tests;

public class MapLayoutTests
{
    [Theory]
    [InlineData(0, 500, 500)]
    [InlineData(5, 2500, 1500)]
    [InlineData(8, 2500, 2500)]
    public void CenterOf_UsesColumnForXAndRowForY(int id, int x, int y)
    {
        Assert.Equal(new MapPoint(x, y), MapLayout.CenterOf(id));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1000, 999, 1)]
    [InlineData(2999, 2999, 8)]
    [InlineData(3000, 0, -1)]
    [InlineData(-1, 0, -1)]
    public void DistrictAt_MapsPointsAndRejectsOutside(int x, int y, int expected)
    {
        Assert.Equal(expected, MapLayout.DistrictAt(new MapPoint(x, y)));
    }

    /// <summary>
    /// 核心(#7): 横と縦の順を入れ替える変異、<c>符号(Δ列)</c> を常に +1 にする変異で落ちる想定。
    /// 変異の実測結果は mutator の報告を待つ。
    /// </summary>
    [Theory]
    [InlineData(0, 8, 1500, 2000, 500)]
    [InlineData(0, 8, 2500, 2500, 1000)]
    [InlineData(0, 8, 5000, 2500, 2500)]
    [InlineData(0, 8, -10, 500, 500)]
    [InlineData(8, 0, 1500, 1000, 2500)]
    public void Along_GoesColumnFirstThenRowAndClamps(int from, int to, int traveled, int x, int y)
    {
        Assert.Equal(new MapPoint(x, y), MapLayout.Along(from, to, traveled));
    }

    /// <summary>核心(#8): 割り算を消す変異で、1区画 2 時間の例が落ちる想定。</summary>
    [Theory]
    [InlineData(1, 2000, 500)]
    [InlineData(2, 1250, 500)]
    public void Traveling_DividesByTravelHoursPerDistrict(int travelHours, int x, int y)
    {
        Assert.Equal(new MapPoint(x, y), MapLayout.Traveling(0, 2, 1, 500, travelHours));
    }

    [Fact]
    public void Traveling_ZeroTravelHours_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MapLayout.Traveling(0, 2, 1, 500, 0));
    }

    [Fact]
    public void NpcPosition_StationaryActivities_OffsetBySlot()
    {
        var working = new TimetableEntry
        {
            Activity = TimetableActivity.Working,
            DistrictId = 4,
            DestinationDistrictId = 4,
        };
        var away = new TimetableEntry
        {
            Activity = TimetableActivity.AtDestination,
            DistrictId = 2,
            DestinationDistrictId = 2,
        };

        Assert.Equal(new MapPoint(1250, 1750), MapLayout.NpcPosition(working, 6, 0, 1));
        Assert.Equal(new MapPoint(2750, 250), MapLayout.NpcPosition(away, 1, 0, 1));
        Assert.Equal(new MapPoint(1250, 1250), MapLayout.NpcPosition(working, 4, 0, 1));
        Assert.Equal(new MapPoint(1750, 1250), MapLayout.NpcPosition(working, 5, 0, 1));
        Assert.Equal(new MapPoint(1750, 1750), MapLayout.NpcPosition(working, 7, 0, 1));
    }

    [Fact]
    public void NpcPosition_Traveling_IsNotOffsetAndMatchesTraveling()
    {
        var traveling = new TimetableEntry
        {
            Activity = TimetableActivity.Traveling,
            DistrictId = 0,
            DestinationDistrictId = 2,
            ElapsedHours = 1,
        };

        var expected = MapLayout.Traveling(0, 2, 1, 500, 1);

        Assert.Equal(expected, MapLayout.NpcPosition(traveling, 3, 500, 1));
        Assert.Equal(expected, MapLayout.NpcPosition(traveling, 0, 500, 1));
    }

    [Fact]
    public void PlayerPosition_ThroughSimHost_FollowsDeparturePath()
    {
        const int Hours = 1;
        var host = new SimHost(WorldDefinition.M0, 1);
        host.Advance(8);

        var before = host.Query.Player();
        int from = before.Location.DistrictId;
        int dest = 0;
        int best = -1;
        for (int d = 0; d < District.Count; d++)
        {
            int dist = District.Distance(from, d);
            if (dist > best)
            {
                best = dist;
                dest = d;
            }
        }

        var result = host.Execute(new MoveCommand(dest));
        Assert.Equal(CommandStatus.Accepted, result.Status);

        host.Advance(1);
        var player = host.Query.Player();
        Assert.True(player.Location.InTransit);
        Assert.Equal(
            MapLayout.Along(from, dest, 1000),
            MapLayout.PlayerPosition(player, host.Now, 0, Hours));

        host.Advance(best - 1);
        Assert.Equal(
            MapLayout.CenterOf(dest),
            MapLayout.PlayerPosition(host.Query.Player(), host.Now, 0, Hours));
    }

    [Fact]
    public void PlayerPosition_NotInTransit_IsDistrictCenter()
    {
        var host = new SimHost(WorldDefinition.M0, 1);
        var player = host.Query.Player();

        Assert.Equal(
            MapLayout.CenterOf(player.Location.DistrictId),
            MapLayout.PlayerPosition(player, host.Now, 300, 1));
    }
}
