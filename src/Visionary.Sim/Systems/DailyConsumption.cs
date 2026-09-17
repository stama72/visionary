using Visionary.Sim.Numerics;
using Visionary.Sim.Time;

namespace Visionary.Sim.Systems;

/// <summary>
/// 世帯の1日消費量(GDD02 §6.1・§9)。<see cref="ConsumptionSystem"/> の私有メソッドを
/// 切り出したものであり、複製ではない。
/// </summary>
/// <remarks>
/// <b>置き場所は1か所にしか無い。</b>GDD02 §8.2.1 の目標在庫(必需・嗜好)は「今後 N 日に
/// 消費する予定の量の合計」であり、その1日ぶんは <see cref="ConsumptionSystem"/> が実際に
/// 引く量と同じ式である(タスク仕様)。書き分けると、目標在庫と実消費が静かにずれる ──
/// 構成員ごとの切り上げを片方だけ忘れても、どちらのテストも単独では緑のままになる。
/// </remarks>
public static class DailyConsumption
{
    /// <summary>世帯の1日消費量(GDD02 §6.1・§9)。</summary>
    public static int Quantity(
        WorldDefinition definition, World world, HouseholdState household, int itemId, Season season)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(household);

        int requiredQuantity = 0;

        // 構成員は先頭から(MemberNpcIdsの昇順は構築時に検証済み)。
        foreach (int npcId in household.MemberNpcIds)
        {
            int baseQuantity = definition.DailyConsumptionPerNpcByRank[(int)world.Npcs[npcId].Rank][itemId];

            // 季節係数を掛けるのは薪だけ(GDD02 §9)。構成員ごとに切り上げてから合計する ──
            // 世帯合計に先に掛けると、基礎量が奇数の場合に結果がずれる(GDD02 §6.1)。
            requiredQuantity += itemId == Item.Firewood
                ? IntegerMath.ApplyPermille(baseQuantity, definition.FirewoodConsumptionSeasonPermille[(int)season])
                : baseQuantity;
        }

        return requiredQuantity;
    }

    /// <summary>
    /// 今日から <paramref name="days"/> 日ぶんの消費量の合計(GDD02 §8.2.1「N日分は先読みで数える」)。
    /// </summary>
    /// <remarks>
    /// <b>日ごとに季節係数を適用して切り上げてから足す。</b>合計してから切り上げない
    /// (GDD02 §8.2.1)。<paramref name="now"/> の時刻(<see cref="Tick.HourOfDay"/>)は捨てる ──
    /// 「今日」は日単位の概念である。
    /// </remarks>
    public static int Lookahead(
        WorldDefinition definition, World world, HouseholdState household, int itemId, Tick now, int days)
    {
        if (days <= 0)
        {
            return 0;
        }

        int total = 0;

        for (int k = 0; k < days; k++)
        {
            var season = GameDate.FromTick(Tick.FromDays(now.DayIndex + k)).Season;
            total = checked(total + Quantity(definition, world, household, itemId, season));
        }

        return total;
    }
}
