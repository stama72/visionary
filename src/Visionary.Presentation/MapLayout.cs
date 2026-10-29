using Visionary.Sim;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Presentation;

/// <summary>地図座標。単位: 地図単位(区画1つ = 1,000)。</summary>
public readonly record struct MapPoint(int X, int Y);

/// <summary>
/// 区画の地図上の配置と、居場所の補間(TDD01 §3.10 / §3.9、GDD02 §4.3)。
/// </summary>
/// <remarks>
/// 割り算は切り捨てでよい。画面の座標であってシムの計算ではない(TDD01 §3.10)。
/// </remarks>
public static class MapLayout
{
    /// <summary>地図単位 / 区画。</summary>
    public const int DistrictSize = 1000;

    /// <summary>止まっている者の下詰めの並びの間隔。単位: 地図単位。</summary>
    public const int SlotSpacing = 250;

    /// <summary>1段に並べる人数。単位: 人。</summary>
    public const int SlotsPerRow = 4;

    private const int MapSize = DistrictSize * District.GridSide;

    public static MapPoint CenterOf(int districtId) => new(
        (District.ColumnOf(districtId) * DistrictSize) + (DistrictSize / 2),
        (District.RowOf(districtId) * DistrictSize) + (DistrictSize / 2));

    /// <summary>地図の外は −1。</summary>
    public static int DistrictAt(MapPoint point)
    {
        if (point.X < 0 || point.X >= MapSize || point.Y < 0 || point.Y >= MapSize)
        {
            return -1;
        }

        return ((point.Y / DistrictSize) * District.GridSide) + (point.X / DistrictSize);
    }

    /// <summary>
    /// 2区画の間を L 字(先に列 = X、次に行 = Y)に <paramref name="traveledUnits"/> だけ進んだ点。
    /// 単位: 地図単位。[0, 距離 × 1000] に丸める。
    /// </summary>
    public static MapPoint Along(int fromDistrictId, int toDistrictId, int traveledUnits)
    {
        var from = CenterOf(fromDistrictId);
        var to = CenterOf(toDistrictId);

        int horizontal = Math.Abs(to.X - from.X);
        int vertical = Math.Abs(to.Y - from.Y);
        int traveled = Math.Clamp(traveledUnits, 0, horizontal + vertical);

        if (traveled <= horizontal)
        {
            return new MapPoint(from.X + (Math.Sign(to.X - from.X) * traveled), from.Y);
        }

        return new MapPoint(to.X, from.Y + (Math.Sign(to.Y - from.Y) * (traveled - horizontal)));
    }

    /// <summary>
    /// 移動中の位置。進んだ距離 = (経過時間 × 1000 + 進み‰) ÷ 1区画あたりの移動時間(切り捨て)。
    /// 単位: 地図単位。
    /// </summary>
    public static MapPoint Traveling(
        int fromDistrictId, int toDistrictId, long elapsedHours, int progressPermille, int travelHoursPerDistrict)
    {
        if (travelHoursPerDistrict < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(travelHoursPerDistrict), travelHoursPerDistrict, "1区画あたりの移動時間は 1 時間以上。");
        }

        long traveled = ((elapsedHours * 1000) + progressPermille) / travelHoursPerDistrict;

        // int への丸めは Along の範囲丸めより前に起こる。極端に大きい値は上限へ寄せる。
        return Along(fromDistrictId, toDistrictId, (int)Math.Clamp(traveled, 0, int.MaxValue));
    }

    /// <summary>
    /// 区画 <paramref name="districtId"/> に止まっている者の順位 <paramref name="slotIndex"/>(0 始まり)の位置。
    /// 下詰め・左詰め、4人で1段。単位: 地図単位。
    /// </summary>
    public static MapPoint StationaryPosition(int districtId, int slotIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slotIndex);

        return new MapPoint(
            (District.ColumnOf(districtId) * DistrictSize) + (SlotSpacing / 2) + ((slotIndex % SlotsPerRow) * SlotSpacing),
            (District.RowOf(districtId) * DistrictSize) + DistrictSize - (SlotSpacing / 2)
                - ((slotIndex / SlotsPerRow) * SlotSpacing));
    }

    public static MapPoint NpcPosition(
        TimetableEntry entry, int slotIndex, int progressPermille, int travelHoursPerDistrict)
    {
        if (entry.Activity == TimetableActivity.Traveling)
        {
            return Traveling(
                entry.DistrictId, entry.DestinationDistrictId, entry.ElapsedHours, progressPermille,
                travelHoursPerDistrict);
        }

        return StationaryPosition(entry.DistrictId, slotIndex);
    }

    /// <summary>
    /// <c>entries[k]</c> は <c>npcIds[k]</c> の時間割。止まっている者に、同じ区画の中で NpcId 昇順の順位を返す。
    /// Traveling は −1。
    /// </summary>
    public static int[] StationarySlots(IReadOnlyList<int> npcIds, IReadOnlyList<TimetableEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(npcIds);
        ArgumentNullException.ThrowIfNull(entries);

        if (npcIds.Count != entries.Count)
        {
            throw new ArgumentException("npcIds と entries は同じ長さ。", nameof(entries));
        }

        var slots = new int[npcIds.Count];
        for (int k = 0; k < slots.Length; k++)
        {
            if (entries[k].Activity == TimetableActivity.Traveling)
            {
                slots[k] = -1;
                continue;
            }

            int rank = 0;
            for (int j = 0; j < slots.Length; j++)
            {
                if (entries[j].Activity != TimetableActivity.Traveling
                    && entries[j].DistrictId == entries[k].DistrictId
                    && npcIds[j] < npcIds[k])
                {
                    rank++;
                }
            }

            slots[k] = rank;
        }

        return slots;
    }

    public static MapPoint PlayerPosition(
        PlayerView player, Tick now, int progressPermille, int travelHoursPerDistrict)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (!player.Location.InTransit)
        {
            return CenterOf(player.Location.DistrictId);
        }

        int from = player.Location.DistrictId;
        int to = player.DestinationDistrictId;
        int distance = District.Distance(from, to);

        // 出発した tick は World に残らないので、到着 tick から逆算する。
        Tick departure = player.ArrivalAt!.Value.AddHours(-((long)distance * travelHoursPerDistrict));
        long elapsedHours = now - departure;

        return Traveling(from, to, elapsedHours, progressPermille, travelHoursPerDistrict);
    }
}
