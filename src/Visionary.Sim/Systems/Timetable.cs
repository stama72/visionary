using Visionary.Sim.Time;

namespace Visionary.Sim.Systems;

/// <summary>時間割の1時間ぶんの居場所の種類(GDD05 §1.5 / TDD01 §3.9)。</summary>
public enum TimetableActivity
{
    /// <summary>自区画の家で休む。</summary>
    Resting = 1,

    /// <summary>自区画の工房で家業。</summary>
    Working = 2,

    /// <summary>外出の移動中。</summary>
    Traveling = 3,

    /// <summary>外出先(着いた時刻に居て、そのまま帰る)。</summary>
    AtDestination = 4,
}

/// <summary>ある NPC のある時刻の居場所(GDD05 §1.5 / TDD01 §3.9)。</summary>
public readonly record struct TimetableEntry
{
    public TimetableActivity Activity { get; init; }

    /// <summary>居場所の区画Id。Traveling のときは出発区画。</summary>
    public int DistrictId { get; init; }

    /// <summary>Traveling のときの行き先の区画Id。それ以外は DistrictId と同じ値。</summary>
    public int DestinationDistrictId { get; init; }

    /// <summary>Traveling のときの、出発からの経過時間。単位: 時間。それ以外は 0。</summary>
    public int ElapsedHours { get; init; }
}

/// <summary>
/// NPC の時間割(GDD05 §1.5 / TDD01 §3.9)。<b>純関数。</b>書き込みは一切せず、乱数を引かない。
/// </summary>
/// <remarks>
/// <b>同じ日の tick だけを受け付ける。</b>World が持つ計画は当日の1日分だけで、過去・未来の日の
/// 時間割は World から復元できない。残る穴: <c>world.Now</c> がその日の0時ちょうど(0時をまだ処理していない)
/// とき、同じ日の6〜17時を問うと前日の計画で答える。また <see cref="HouseholdState.ExportErrandTaken"/> は
/// 18時の段6 が書くので、当日の6〜17時を問うた時点では常に 0 で、輸出の別の外出は日中の時間割に現れない
/// (上位文書の扱いは #268)。
/// </remarks>
public static class Timetable
{
    public static TimetableEntry Of(WorldDefinition definition, World world, int npcId, Tick tick)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(world);

        if (tick.DayIndex != world.Now.DayIndex)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tick), tick, "時間割は world.Now と同じ日の tick だけを問える(TDD01 §3.9)。");
        }

        var household = world.Households[world.Npcs[npcId].HouseholdId];
        int home = household.DistrictId;
        int hour = tick.HourOfDay;

        // 0〜5時と18時以降は計画を読まない。
        if (hour < WorldDefinition.WorkdayStartHour || hour >= WorldDefinition.EveningTradeHour)
        {
            return Stay(TimetableActivity.Resting, home);
        }

        if (npcId != household.ErrandDelegateNpcId)
        {
            return Stay(TimetableActivity.Working, home);
        }

        // 外出の列: 計画の区画の順、続けて輸出の別の外出の中心。
        var destinations = new List<int>(household.PlannedErrandDistrictIds);

        if (household.ExportErrandTaken == 1)
        {
            destinations.Add(District.ExternalMarketDistrictId);
        }

        // 片道 = 往復 / 2(TravelHours は往復。割り切れる)。
        var oneWayHours = new int[destinations.Count];
        int totalRoundTrip = 0;

        for (int i = 0; i < destinations.Count; i++)
        {
            int roundTrip = Errand.TravelHours(home, destinations[i], definition.TravelHoursPerDistrict);
            oneWayHours[i] = roundTrip / 2;
            totalRoundTrip += roundTrip;
        }

        // 外出は夕方の実行時刻に帰り着くように詰める。最初の出発 = EveningTradeHour − Σ往復。
        int departure = WorldDefinition.EveningTradeHour - totalRoundTrip;

        if (hour < departure)
        {
            return Stay(TimetableActivity.Working, home);
        }

        for (int i = 0; i < destinations.Count; i++)
        {
            int roundTrip = oneWayHours[i] * 2;

            if (hour < departure + roundTrip)
            {
                int elapsed = hour - departure;
                int oneWay = oneWayHours[i];

                if (elapsed < oneWay)
                {
                    return new TimetableEntry
                    {
                        Activity = TimetableActivity.Traveling,
                        DistrictId = home,
                        DestinationDistrictId = destinations[i],
                        ElapsedHours = elapsed,
                    };
                }

                if (elapsed == oneWay)
                {
                    return Stay(TimetableActivity.AtDestination, destinations[i]);
                }

                return new TimetableEntry
                {
                    Activity = TimetableActivity.Traveling,
                    DistrictId = destinations[i],
                    DestinationDistrictId = home,
                    ElapsedHours = elapsed - oneWay,
                };
            }

            departure += roundTrip;
        }

        // 18時未満では外出の列の最後の帰着が18時なのでここには来ない。
        throw new InvalidOperationException("時間割が外出の列に収まらない。");
    }

    private static TimetableEntry Stay(TimetableActivity activity, int districtId) => new()
    {
        Activity = activity,
        DistrictId = districtId,
        DestinationDistrictId = districtId,
        ElapsedHours = 0,
    };
}
