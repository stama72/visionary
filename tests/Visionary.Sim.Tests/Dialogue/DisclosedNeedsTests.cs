using Visionary.Sim.Dialogue;

namespace Visionary.Sim.Tests.Dialogue;

public sealed class DisclosedNeedsTests
{
    /// <remarks>
    /// 変異の実測(<c>mutator</c> の報告、HEAD f7a76bf、2026-10-03): 理由の条件に
    /// <c>CannotExpandProduction</c> を足すと赤(本テスト)。
    /// </remarks>
    [Fact]
    public void DisclosedNeedsAreTheThreeTradableReasonsSortedById()
    {
        var world = new World(npcCount: 2, householdCount: 3, itemCount: Item.Count);
        const int household = 1;

        // Id の逆順に積む。5理由すべてを世帯 1 に、別の世帯 2 に開示対象の1件。
        var reasons = new[]
        {
            NeedReason.DistantStock, NeedReason.ToolsExhausted, NeedReason.CannotExpandProduction,
            NeedReason.Distress, NeedReason.ProductionStopped,
        };

        for (int i = 0; i < reasons.Length; i++)
        {
            world.Needs.Add(NeedOf(id: 20 - i, household, reasons[i]));
        }

        world.Needs.Add(NeedOf(id: 99, householdId: 2, NeedReason.ProductionStopped));

        var disclosed = DisclosedNeeds.Of(world, household);

        // 世帯 1 の開示対象は DistantStock(20)・ToolsExhausted(19)・ProductionStopped(16)。Id 昇順
        Assert.Equal(new[] { 16, 19, 20 }, disclosed.Select(n => n.Id).ToArray());
        Assert.All(disclosed, n => Assert.Equal(household, n.TargetHouseholdId));
    }

    private static Need NeedOf(int id, int householdId, NeedReason reason) => new()
    {
        Id = id,
        TypeCode = Need.TypeOf(reason),
        TargetHouseholdId = householdId,
        ItemId = Item.Grain,
        Quantity = 1,
        ReasonCode = reason,
    };
}
