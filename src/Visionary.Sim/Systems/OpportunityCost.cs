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
    /// 買いに行く者(GDD08 §7.2・§9)。機会費用が最小の構成員。同値は NpcId 昇順。
    /// </summary>
    public static ErrandDelegate SelectErrandDelegate(
        WorldDefinition definition, World world, HouseholdState household)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(household);

        var best = new ErrandDelegate { CostPerHour = int.MaxValue };

        foreach (int npcId in household.MemberNpcIds)
        {
            var rank = world.Npcs[npcId].Rank;

            // 職業は世帯の現在の値を読む(#39 の付け替えで変わる)。household.Occupation を
            // ここで引く ── 呼び出し側に定数で持たせると付け替え後に追随しない(#36 引き継ぎ表 J と同型)。
            int cost = ForNpc(definition, household.Occupation, rank);

            if (cost < best.CostPerHour)
            {
                best = new ErrandDelegate
                {
                    NpcId = npcId,
                    CostPerHour = cost,
                    // 労働力係数‰(GDD02a §2)。階層係数‰(GDD08 §9)ではない ── 取り違えても
                    // 例外は出ず、値としては自然に見えるので気付けない(タスク仕様)。
                    LaborPermille = definition.LaborPermilleByRank[(int)rank],
                };
            }
        }

        return best;
    }
}

/// <summary>買いに行く者(GDD08 §7.2・§9)。機会費用が最小の構成員。同値は NpcId 昇順。</summary>
public readonly record struct ErrandDelegate
{
    public int NpcId { get; init; }

    /// <summary>機会費用。単位: 貨幣/1時間(GDD08 §9)。</summary>
    public int CostPerHour { get; init; }

    /// <summary>
    /// 労働力係数‰(GDD02a §2)。<b>階層係数‰(GDD08 §9)ではない。</b>
    /// <see cref="OpportunityCost.SelectErrandDelegate"/> が機会費用を選んだのと同じ NPC の値。
    /// </summary>
    public int LaborPermille { get; init; }
}
