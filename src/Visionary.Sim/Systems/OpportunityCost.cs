using Visionary.Sim.Numerics;

namespace Visionary.Sim.Systems;

/// <summary>
/// 機会費用(GDD08 §5・§9)。<b>M0 は職業 × 階層の固定値なので、システムではなく純関数である</b>
/// (TDD01 §3.3「M0 の OpportunityCost はシステムを持たない」)。
/// </summary>
public static class OpportunityCost
{
    /// <summary>1人あたりの機会費用。単位: 貨幣/1時間(GDD08 §9)。</summary>
    public static int ForNpc(WorldDefinition definition, Occupation occupation, NpcRank rank)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return IntegerMath.ApplyPermille(
            definition.OpportunityCostBaseByOccupation[(int)occupation],
            definition.RankCoefficientPermille[(int)rank]);
    }

    /// <summary>
    /// 買いに行く者の機会費用(GDD08 §7.2・§9)。世帯構成員のうち<b>最小</b>を採る。
    /// </summary>
    /// <remarks>
    /// <b>職業は世帯の現在の値を読む</b>(#39 の付け替えで変わる)。<c>household.Occupation</c> を
    /// ここで引く ── 呼び出し側に定数で持たせると付け替え後に追随しない(#36 引き継ぎ表 J と同型)。
    /// <para>
    /// <b>構成員は <see cref="HouseholdState.MemberNpcIds"/> の並びそのまま走査し、<c>&lt;</c> で
    /// 更新する。</b>昇順は構築時に保証されているので、同値なら先に見た(NpcId 最小の)者が残る
    /// (GDD08 §9 の M0 の単純化「手が空いているを判定しない」)。
    /// </para>
    /// </remarks>
    public static int ForErrand(WorldDefinition definition, World world, HouseholdState household)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(household);

        int minimum = int.MaxValue;

        foreach (int npcId in household.MemberNpcIds)
        {
            int cost = ForNpc(definition, household.Occupation, world.Npcs[npcId].Rank);

            if (cost < minimum)
            {
                minimum = cost;
            }
        }

        return minimum;
    }
}
