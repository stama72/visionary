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
    /// 実測(出所: mutator の実測(7ab797e)):
    /// 横と縦の順を入れ替える変異 → 赤(0→8 の traveled 1500・2500、8→0 の traveled 1500 の3ケースが落ちた)。
    /// <c>符号(Δ列)</c> を常に +1 にする変異 → 赤(8→0 の traveled 1500 の1ケースが落ちた)。
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

    /// <summary>
    /// 核心(#8): 割り算を消す変異で、1区画 2 時間の例が落ちる想定。
    /// 実測(出所: mutator の実測(7ab797e)): <c>/ travelHoursPerDistrict</c> を消す変異 → 赤(2 時間のケースが落ちた)。
    /// </summary>
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

    [Theory]
    [InlineData(4, 0, 1125, 1875)]
    [InlineData(4, 3, 1875, 1875)]
    [InlineData(4, 4, 1125, 1625)]
    [InlineData(0, 1, 375, 875)]
    public void StationaryPosition_PacksFromBottomLeftFourPerRow(int district, int slot, int x, int y)
    {
        Assert.Equal(new MapPoint(x, y), MapLayout.StationaryPosition(district, slot));
    }

    [Fact]
    public void StationaryPosition_NegativeSlot_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MapLayout.StationaryPosition(0, -1));
    }

    private static TimetableEntry Entry(TimetableActivity activity, int district) => new()
    {
        Activity = activity,
        DistrictId = district,
        DestinationDistrictId = district,
    };

    /// <summary>
    /// 核心(#9''): 区画の一致の条件を消す変異で、区画 1 の npc 7 が 0 でなく 3 などになり落ちる想定。
    /// 実測(出所: mutator の実測、2026-10-29、HEAD b132c08):
    /// <c>StationarySlots</c> から <c>&amp;&amp; entries[j].DistrictId == entries[k].DistrictId</c> を削除する変異
    /// (区画をまたいで順位を数える) → 赤(期待 [1, 0, -1, 0] に対し実測 [1, 0, -1, 2]。
    /// Presentation.Tests 51 件中、失敗 1・合格 50)。
    /// </summary>
    [Fact]
    public void StationarySlots_RanksByNpcIdWithinDistrictSkippingTraveling()
    {
        int[] ids = [5, 2, 9, 7];
        TimetableEntry[] entries =
        [
            Entry(TimetableActivity.Working, 3),
            Entry(TimetableActivity.Resting, 3),
            Entry(TimetableActivity.Traveling, 3),
            Entry(TimetableActivity.AtDestination, 1),
        ];

        Assert.Equal([1, 0, -1, 0], MapLayout.StationarySlots(ids, entries));

        // 入力の並びを入れ替えても、NpcId ごとの順位は同じ。
        int[] ids2 = [7, 9, 2, 5];
        TimetableEntry[] entries2 = [entries[3], entries[2], entries[1], entries[0]];
        Assert.Equal([0, -1, 0, 1], MapLayout.StationarySlots(ids2, entries2));
    }

    [Fact]
    public void StationarySlots_LengthMismatch_Throws()
    {
        Assert.Throws<ArgumentException>(() => MapLayout.StationarySlots([1, 2], [Entry(TimetableActivity.Working, 0)]));
    }

    [Fact]
    public void NpcPosition_StationaryActivities_MatchStationaryPosition()
    {
        var working = Entry(TimetableActivity.Working, 4);
        var away = Entry(TimetableActivity.AtDestination, 2);

        Assert.Equal(MapLayout.StationaryPosition(4, 5), MapLayout.NpcPosition(working, 5, 0, 1));
        Assert.Equal(MapLayout.StationaryPosition(2, 1), MapLayout.NpcPosition(away, 1, 0, 1));
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
