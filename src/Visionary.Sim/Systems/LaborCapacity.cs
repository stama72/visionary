using Visionary.Sim.Numerics;

namespace Visionary.Sim.Systems;

/// <summary>
/// 労働力と設備係数の求め方(GDD02a §2・§3)。<see cref="ProductionSystem"/> が読む。
/// </summary>
/// <remarks>
/// <b><see cref="NeedGenerationSystem"/> はもう呼ばない</b>(#237)。理由3(増産できない)は
/// <see cref="ProductionSystem"/> が同じtickで書いた「当日の生産能力」
/// (<see cref="HouseholdState.ProductionCapacityRuns"/>)を読むだけになった ──
/// 前日から持ち越した進捗‰の端数は順4からは見えず、労働力・設備係数から逆算できないため。
/// </remarks>
/// <remarks>
/// <b>式を2か所に置かない</b>(<see cref="Recipe.CapacityRuns"/> の doc コメントと同じ理由。
/// 片方の丸めを直したとき他方が黙ってずれる)。<see cref="EffectiveLaborPermille"/> の内側の
/// 切り下げは <see cref="IntegerMath.FloorPermille"/> を呼ぶだけで、自前では持たない
/// (W2-19訂正。レビュー1巡目象限I-a ── 以前はここに複製があり、この remarks の主張自体が偽だった)。
/// </remarks>
public static class LaborCapacity
{
    /// <summary>設備係数‰(GDD02a §3)。工具在庫が閾値以上なら 1000、無ければ definition の値。</summary>
    public static int EquipmentPermille(WorldDefinition definition, HouseholdState household)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(household);

        return household.WorkshopInventory[Item.Tools] >= ProductionSystem.EquipmentThresholdStock
            ? IntegerMath.PermilleScale
            : definition.EquipmentPermilleWithoutTools;
    }

    /// <summary>労働力合計‰ = max(0, Σ構成員の労働力係数‰ − 前日の外出の労働損失‰)(GDD02a §2)。</summary>
    /// <remarks>構成員は <see cref="HouseholdState.MemberNpcIds"/> の昇順のまま走査する(並べ替えない)。</remarks>
    public static int LaborPermille(WorldDefinition definition, World world, HouseholdState household)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(household);

        int totalLaborPermille = 0;

        foreach (int npcId in household.MemberNpcIds)
        {
            totalLaborPermille += definition.LaborPermilleByRank[(int)world.Npcs[npcId].Rank];
        }

        return Math.Max(0, totalLaborPermille - household.ErrandLaborLossPermille);
    }

    /// <summary>floor(労働力合計‰ × 設備係数‰ ÷ 1000)(GDD02a §1 の内側の切り下げ)。</summary>
    /// <remarks>
    /// <see cref="IntegerMath.FloorPermille"/> を呼ぶだけで、式そのものはここに持たない
    /// (<see cref="Recipe.CapacityRuns"/> の内側の除算と同じ関数。W2-19訂正)。<c>int</c> への
    /// <c>checked</c> キャストはここで行う(<see cref="IntegerMath.FloorPermille"/> は
    /// <see cref="long"/> のまま返す)。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">どちらかの引数が負のとき。</exception>
    public static int EffectiveLaborPermille(int laborPermille, int equipmentPermille)
    {
        if (laborPermille < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(laborPermille), laborPermille, "労働力合計‰は非負(GDD02a §2)。");
        }

        if (equipmentPermille < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(equipmentPermille), equipmentPermille, "設備係数‰は非負(GDD02a §3)。");
        }

        return checked((int)IntegerMath.FloorPermille(laborPermille, equipmentPermille));
    }
}
