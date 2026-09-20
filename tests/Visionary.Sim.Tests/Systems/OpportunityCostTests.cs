using Visionary.Sim.Systems;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="OpportunityCost"/>(GDD08 §5・§9、#37 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class OpportunityCostTests
{
    // 職業ごとに違う基準値を置く(添字の取り違えをどのテストにも現れさせるため、
    // タスク仕様「5職業に別々の値を置く」理由と同じ)。Miller=5, Baker=6, Brewer=6,
    // Woodworker=4, Smith=8(M0の実値と同じ組)。
    private static readonly int[] BaseByOccupation = { 5, 6, 6, 4, 8 };

    private static WorldDefinition BuildDefinition(int[]? rankCoefficientPermille = null) =>
        EconomySystemTestFixtures.BuildDefinition(
            new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
                inputs: new[] { new ItemQuantity { ItemId = Item.Grain, Quantity = 1 } },
                laborPermille: 1000),
            rankCoefficientPermille: rankCoefficientPermille ?? new[] { 1000, 600, 200 },
            opportunityCostBaseByOccupation: BaseByOccupation);

    /// <summary>
    /// テスト表 #1。基準値5・親方(1000‰)→ 5、徒弟(200‰)→ 1。基準値4・徒弟 → 1(切り上げ)。
    /// </summary>
    [Fact]
    public void OpportunityCostAppliesTheRankCoefficient()
    {
        var definition = BuildDefinition();

        Assert.Equal(5, OpportunityCost.ForNpc(definition, Occupation.Miller, NpcRank.Master));
        Assert.Equal(1, OpportunityCost.ForNpc(definition, Occupation.Miller, NpcRank.Apprentice));

        // 基準値4(Woodworker、添字3)・徒弟(200‰) → CeilDiv(4×200,1000) = 1。
        // FloorDiv にすると 0 になり移動費が消える(タスク仕様の変異)。
        Assert.Equal(1, OpportunityCost.ForNpc(definition, Occupation.Woodworker, NpcRank.Apprentice));
    }

    /// <summary>
    /// 【核心】テスト表 #2。親方(1000‰)と徒弟(200‰)の2人世帯・基準値5 → 1。
    /// 全員同値なら NpcId 最小のものが残る。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-20)。</b><c>OpportunityCost.SelectErrandDelegate</c> の
    /// <c>if (cost &lt; best.CostPerHour)</c> を <c>if (cost &gt; best.CostPerHour)</c>
    /// (<c>Max</c> を取る変異)に変えたところ、<c>Assert.Equal(1, ...)</c> が実際値5
    /// (親方の機会費用が残った。GDD06 §3「機会費用の低い者を使うと選択肢が広がる」が
    /// 恒偽になる)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ErrandOpportunityCostTakesTheSmallestMember()
    {
        var definition = BuildDefinition();
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Apprentice });

        // Miller(基準値5): 親方=5、徒弟=1。最小の1が採られ、NpcId(徒弟=1)も一致する。
        var selected = OpportunityCost.SelectErrandDelegate(definition, world, world.Households[0]);
        Assert.Equal(1, selected.CostPerHour);
        Assert.Equal(1, selected.NpcId);

        // 全員同値(Master×2) → 先頭(NpcId最小=0)が残ることを、Maxに落ちていないことと
        // あわせて確認する。
        var tiedWorld = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Master });
        var tiedSelected = OpportunityCost.SelectErrandDelegate(definition, tiedWorld, tiedWorld.Households[0]);
        Assert.Equal(5, tiedSelected.CostPerHour);
        Assert.Equal(0, tiedSelected.NpcId);
    }

    /// <summary>
    /// テスト表 #3。同じ構成員(徒弟1名)で <c>Occupation</c> だけ Miller(基準値5)→
    /// Smith(基準値8)に変えると 1 → 2。
    /// </summary>
    /// <remarks>
    /// 職業を定数で引く実装ミス(#36引き継ぎ表Jと同型)は、#39 の職業付け替え後に
    /// 機会費用が追随しないことを見逃す。<c>household.Occupation</c> を書き換えてから
    /// 再計算することで、これを直接踏む。
    /// </remarks>
    [Fact]
    public void ErrandOpportunityCostReadsTheHouseholdOccupation()
    {
        var definition = BuildDefinition();
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Apprentice });

        // Miller(基準値5)・徒弟(200‰) → CeilDiv(5×200,1000) = 1。
        Assert.Equal(1, OpportunityCost.SelectErrandDelegate(definition, world, world.Households[0]).CostPerHour);

        world.Households[0].Occupation = Occupation.Smith;

        // Smith(基準値8)・徒弟(200‰) → CeilDiv(8×200,1000) = 2。
        Assert.Equal(2, OpportunityCost.SelectErrandDelegate(definition, world, world.Households[0]).CostPerHour);
    }
}
