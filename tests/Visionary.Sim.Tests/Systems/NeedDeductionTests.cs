using Visionary.Sim.Systems;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="NeedDeduction"/>(GDD02b §8.1 の表)の検査。段8 の既存テストが緑のままであることが
/// <c>TradeSystem</c> 側の置き換えの保護で、ここは対応表そのものを独立に書き写して照合する。
/// </summary>
public sealed class NeedDeductionTests
{
    private static readonly DemandPurpose[] AllPurposes =
    {
        DemandPurpose.ProductionInput, DemandPurpose.Necessity, DemandPurpose.Preference, DemandPurpose.Durable,
    };

    private static readonly NeedReason[] AllReasons =
    {
        NeedReason.ProductionStopped, NeedReason.Distress, NeedReason.CannotExpandProduction,
        NeedReason.ToolsExhausted, NeedReason.DistantStock,
    };

    /// <summary>GDD02b §8.1 の表を、実装を見ずに書き写したもの(引く用途の集合)。</summary>
    private static DemandPurpose[] ExpectedPurposes(NeedReason reason) => reason switch
    {
        NeedReason.ProductionStopped => new[] { DemandPurpose.ProductionInput },
        NeedReason.ToolsExhausted => new[] { DemandPurpose.Durable },
        NeedReason.Distress => new[] { DemandPurpose.Necessity },
        NeedReason.DistantStock => new[]
        {
            DemandPurpose.ProductionInput, DemandPurpose.Necessity, DemandPurpose.Preference, DemandPurpose.Durable,
        },
        NeedReason.CannotExpandProduction => Array.Empty<DemandPurpose>(),
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    /// <summary>
    /// テスト表 #14。5理由 × 4用途の20組すべてを表と照合する。
    /// <b>核心</b>: 遠方在庫の行から <c>Preference</c> を外すと、その1組で赤になる。
    /// </summary>
    [Fact]
    public void NeedDeduction_SubtractsMatchesTheTable()
    {
        int checkedPairs = 0;

        foreach (var reason in AllReasons)
        {
            var expected = ExpectedPurposes(reason);

            foreach (var purpose in AllPurposes)
            {
                Assert.True(
                    expected.Contains(purpose) == NeedDeduction.Subtracts(reason, purpose),
                    $"{reason} × {purpose}");
                checkedPairs++;
            }
        }

        Assert.Equal(20, checkedPairs);
    }

    [Fact]
    public void Subtracts_RejectsUnknownReasonOrPurpose()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NeedDeduction.Subtracts((NeedReason)0, DemandPurpose.Necessity));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NeedDeduction.Subtracts(NeedReason.DistantStock, (DemandPurpose)99));
    }

    [Fact]
    public void SubtractPurchase_TouchesOnlyMatchingNeedsAndRemovesEmptyOnesInPlace()
    {
        var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);

        Need Make(int id, int household, int item, NeedReason reason, int quantity) => new()
        {
            Id = id,
            TypeCode = Need.TypeOf(reason),
            TargetHouseholdId = household,
            ItemId = item,
            Quantity = quantity,
            ReasonCode = reason,
        };

        world.Needs.Add(Make(0, 0, Item.Firewood, NeedReason.ProductionStopped, 3)); // 引く → 消える
        world.Needs.Add(Make(1, 1, Item.Firewood, NeedReason.ProductionStopped, 3)); // 別世帯
        world.Needs.Add(Make(2, 0, Item.Bread, NeedReason.ProductionStopped, 3));    // 別品目
        world.Needs.Add(Make(3, 0, Item.Firewood, NeedReason.Distress, 3));          // 用途が違う
        world.Needs.Add(Make(4, 0, Item.Firewood, NeedReason.DistantStock, 5));      // 引く → 残る
        int nextNeedIdBefore = world.NextNeedId;

        NeedDeduction.SubtractPurchase(world, 0, DemandPurpose.ProductionInput, Item.Firewood, 3);

        Assert.Equal(new[] { 1, 2, 3, 4 }, world.Needs.Select(n => n.Id).ToArray());
        Assert.Equal(new[] { 3, 3, 3, 2 }, world.Needs.Select(n => n.Quantity).ToArray());
        Assert.Equal(nextNeedIdBefore, world.NextNeedId);
    }
}
