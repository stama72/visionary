using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Systems;

/// <summary><see cref="Timetable.Of"/>(GDD05 §1.5 / TDD01 §3.9、W5-02 タスク仕様のテスト表 #10〜12)の検査。</summary>
public sealed class TimetableTests
{
    /// <summary>
    /// 家(区画0)に親方(NpcId 0)と徒弟(NpcId 1)が住む世帯1戸。<c>TravelHoursPerDistrict = 1</c>。
    /// 区画は 3×3 の行優先で中心 = 4、距離はマンハッタン。
    /// </summary>
    private static (WorldDefinition Definition, World World, HouseholdState Household) BuildWorld()
    {
        var definition = EconomySystemTestFixtures.BuildDefinition(
            new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = 1 } },
                inputs: new[] { new ItemQuantity { ItemId = Item.Timber, Quantity = 1 } },
                laborPermille: 1000),
            travelHoursPerDistrict: 1);

        var world = new World(npcCount: 2, householdCount: 1, itemCount: Item.Count);
        world.Households[0] = new HouseholdState(
            id: 0, districtId: 0, headNpcId: 0, memberNpcIds: new[] { 0, 1 }, itemCount: Item.Count);
        world.Households[0].Occupation = Occupation.Miller;

        return (definition, world, world.Households[0]);
    }

    private static TimetableEntry Parse(string token)
    {
        // 書式: R@d / W@d / A@d(居場所 d)、T a>b:e(a から b へ向かう途中、経過 e 時間)。
        char kind = token[0];

        if (kind == 'T')
        {
            string[] parts = token[1..].Split('>', ':');

            return new TimetableEntry
            {
                Activity = TimetableActivity.Traveling,
                DistrictId = int.Parse(parts[0]),
                DestinationDistrictId = int.Parse(parts[1]),
                ElapsedHours = int.Parse(parts[2]),
            };
        }

        int districtId = int.Parse(token[2..]);

        return new TimetableEntry
        {
            Activity = kind switch
            {
                'R' => TimetableActivity.Resting,
                'W' => TimetableActivity.Working,
                'A' => TimetableActivity.AtDestination,
                _ => throw new ArgumentOutOfRangeException(nameof(token), token, "未知の記号。"),
            },
            DistrictId = districtId,
            DestinationDistrictId = districtId,
            ElapsedHours = 0,
        };
    }

    /// <summary>0〜23時の24点を、記号の列(空白区切り)で書いた期待と比べる。</summary>
    private static void AssertDay(
        WorldDefinition definition, World world, int npcId, string expectedHours)
    {
        string[] expected = expectedHours.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(24, expected.Length);

        for (int hour = 0; hour < 24; hour++)
        {
            var actual = Timetable.Of(definition, world, npcId, new Tick(hour));

            Assert.True(
                Parse(expected[hour]) == actual,
                $"{hour}時: 期待 {expected[hour]}、実際 {actual}");
        }
    }

    /// <summary>
    /// テスト表 #10(#257)。仕様の具体例の全行・全時刻 0〜23。6〜S₀−1 の Working の端を含めて
    /// 24点ぜんぶ並べる ── 出発を 6時起点で数える誤りは Working の範囲の端でしか出ない。
    /// </summary>
    /// <remarks>
    /// 落ちるべき条件: 片道に往復(<c>TravelHours</c>)をそのまま使う(<c>/ 2</c> を外す) /
    /// 出発を 6時起点で数える / 輸出の外出を先に置く・数えない / 帰着と次の出発の tick を1ずらす。
    /// </remarks>
    [Theory]
    // H = 0、計画 [1]、輸出 0(往復 2)。S0 = 16。
    [InlineData(
        "1", false,
        "R@0 R@0 R@0 R@0 R@0 R@0 W@0 W@0 W@0 W@0 W@0 W@0 W@0 W@0 W@0 W@0 T0>1:0 A@1 R@0 R@0 R@0 R@0 R@0 R@0")]
    // H = 0、計画 [8, 1]、輸出 0(往復 8 + 2 = 10)。S0 = 8。帰り着いた16時に次の出発。
    [InlineData(
        "8,1", false,
        "R@0 R@0 R@0 R@0 R@0 R@0 W@0 W@0 T0>8:0 T0>8:1 T0>8:2 T0>8:3 A@8 T8>0:1 T8>0:2 T8>0:3 T0>1:0 A@1 R@0 R@0 R@0 R@0 R@0 R@0")]
    // H = 0、計画 [1]、輸出 1(往復 2 + 4 = 6)。S0 = 12。輸出の外出は買い物の外出の後。
    [InlineData(
        "1", true,
        "R@0 R@0 R@0 R@0 R@0 R@0 W@0 W@0 W@0 W@0 W@0 W@0 T0>1:0 A@1 T0>4:0 T0>4:1 A@4 T4>0:1 R@0 R@0 R@0 R@0 R@0 R@0")]
    public void TimetableFollowsTheWorkedExamples(string plannedDistricts, bool exportErrandTaken, string expectedHours)
    {
        var (definition, world, household) = BuildWorld();
        household.ErrandDelegateNpcId = 0;
        household.PlannedErrandDistrictIds.AddRange(plannedDistricts.Split(',').Select(int.Parse));
        household.ExportErrandTaken = exportErrandTaken ? 1 : 0;

        AssertDay(definition, world, npcId: 0, expectedHours);
    }

    /// <summary>テスト表 #10(#257)。委託先でない構成員は 6〜17時が Working @H、それ以外は Resting @H。</summary>
    [Fact]
    public void TimetableOfANonDelegateMemberStaysAtTheWorkshop()
    {
        var (definition, world, household) = BuildWorld();
        household.ErrandDelegateNpcId = 0;
        household.PlannedErrandDistrictIds.Add(1);

        AssertDay(
            definition, world, npcId: 1,
            "R@0 R@0 R@0 R@0 R@0 R@0 W@0 W@0 W@0 W@0 W@0 W@0 W@0 W@0 W@0 W@0 W@0 W@0 R@0 R@0 R@0 R@0 R@0 R@0");
    }

    /// <summary>
    /// テスト表 #10(#257)。<c>ErrandDelegateNpcId == −1</c>(計画前)は「誰も外出しない」と読む。
    /// 計画の区画列が残っていても Working @H。
    /// </summary>
    [Fact]
    public void TimetableBeforePlanningHasNobodyOutOnAnErrand()
    {
        var (definition, world, household) = BuildWorld();
        household.PlannedErrandDistrictIds.Add(1);
        Assert.Equal(-1, household.ErrandDelegateNpcId);

        AssertDay(
            definition, world, npcId: 0,
            "R@0 R@0 R@0 R@0 R@0 R@0 W@0 W@0 W@0 W@0 W@0 W@0 W@0 W@0 W@0 W@0 W@0 W@0 R@0 R@0 R@0 R@0 R@0 R@0");
    }

    /// <summary>テスト表 #11(#257)。<c>world.Now</c> と別の日の tick を問うと例外を投げる。</summary>
    [Fact]
    public void TimetableRejectsATickOfAnotherDay()
    {
        var (definition, world, _) = BuildWorld();
        EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 48);

        // 同じ日(DayIndex 2 = tick 48〜71)は通る。
        _ = Timetable.Of(definition, world, npcId: 0, new Tick(48));
        _ = Timetable.Of(definition, world, npcId: 0, new Tick(71));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => Timetable.Of(definition, world, npcId: 0, new Tick(47))); // 前日
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Timetable.Of(definition, world, npcId: 0, new Tick(72))); // 翌日
    }

    /// <summary>
    /// テスト表 #12(#257)。0〜5時と18〜23時は計画の欄を読まない ── 計画の欄に外出の時間帯に
    /// 食い込む任意の値(往復の合計が T を超える列と輸出)を入れても、全員 Resting @H。
    /// </summary>
    [Fact]
    public void TimetableDoesNotReadThePlanOutsideWorkingHours()
    {
        var (definition, world, household) = BuildWorld();
        household.ErrandDelegateNpcId = 0;
        household.PlannedErrandDistrictIds.AddRange(new[] { 8, 2, 6, 1 });
        household.ExportErrandTaken = 1;

        int[] outsideHours = { 0, 1, 2, 3, 4, 5, 18, 19, 20, 21, 22, 23 };

        foreach (int npcId in new[] { 0, 1 })
        {
            foreach (int hour in outsideHours)
            {
                var entry = Timetable.Of(definition, world, npcId, new Tick(hour));

                Assert.Equal(TimetableActivity.Resting, entry.Activity);
                Assert.Equal(0, entry.DistrictId);
                Assert.Equal(0, entry.DestinationDistrictId);
                Assert.Equal(0, entry.ElapsedHours);
            }
        }
    }
}
