namespace Visionary.Sim.Dialogue;

/// <summary>
/// NPC が会話で開示する Need(GDD01 §6.1)。理由の絞り込みを呼ぶ側に綴らせない
/// (<see cref="Need.TypeOf"/> と同じ理由)。
/// </summary>
public static class DisclosedNeeds
{
    /// <summary>
    /// <paramref name="world"/> の <see cref="World.Needs"/> のうち、<c>TargetHouseholdId == householdId</c> で
    /// 理由が生産停止・工具切れ・遠方在庫のもの。<see cref="Need.Id"/> 昇順
    /// (<see cref="World.Needs"/> の並びには依存しない)。
    /// </summary>
    public static IReadOnlyList<Need> Of(World world, int householdId)
    {
        ArgumentNullException.ThrowIfNull(world);

        var result = new List<Need>();

        foreach (var need in world.Needs)
        {
            if (need.TargetHouseholdId == householdId && IsDisclosed(need.ReasonCode))
            {
                result.Add(need);
            }
        }

        result.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        return result;
    }

    private static bool IsDisclosed(NeedReason reason) =>
        reason is NeedReason.ProductionStopped or NeedReason.ToolsExhausted or NeedReason.DistantStock;
}
