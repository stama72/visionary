namespace Visionary.Sim.Tests.Definition;

/// <summary><see cref="Recipe.CapacityRuns"/>(GDD02a §1、#96 タスク仕様のテスト表)の検査。</summary>
public sealed class RecipeCapacityRunsTests
{
    private static Recipe RecipeWithLabor(int laborPermille) =>
        new(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: laborPermille);

    /// <summary>
    /// 【核心】テスト表 #1。<c>CapacityRuns(1275, 500)</c> で所要労働319‰ → 1
    /// (floor(floor(1275×500÷1000)÷319) = floor(637÷319) = 1)。
    /// <c>CapacityRuns(1300, 1000)</c> で所要労働217‰ → 5(floor(1300÷217) = 5)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測・再測(2026-09-19、レビュー1巡目 象限III)。</b><c>Recipe.CapacityRuns</c> の
    /// 内側の除算 <c>IntegerMath.FloorDiv((long)laborPermille * equipmentPermille,
    /// IntegerMath.PermilleScale)</c> を <c>IntegerMath.ApplyPermille(laborPermille,
    /// equipmentPermille)</c>(切り上げ)に変える変異を当てたところ、
    /// <c>Assert.Equal(1, recipeA.CapacityRuns(1275, 500))</c>(32行目)が実際値2で失敗した
    /// (赤を確認)。変異を戻し、続けて外側の除算を <c>IntegerMath.CeilDiv</c> に変える変異を
    /// 単独で当てたところ、この変異は <c>CapacityRuns</c> 内の1か所を通る共通コードなので
    /// recipeA の呼び出しにも同じ式が適用され、<c>Assert.Equal(1, recipeA.CapacityRuns(1275,
    /// 500))</c>(32行目、recipeB の35行目ではない)が実際値2(<c>CeilDiv(637, 319) = 2</c>)で
    /// 先に失敗した(赤を確認 ── xUnit は最初の失敗で止まるので35行目には到達しない)。
    /// いずれも変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void CapacityRunsFloorsBothDivisions()
    {
        var recipeA = RecipeWithLabor(laborPermille: 319);
        Assert.Equal(1, recipeA.CapacityRuns(laborPermille: 1275, equipmentPermille: 500));

        var recipeB = RecipeWithLabor(laborPermille: 217);
        Assert.Equal(5, recipeB.CapacityRuns(laborPermille: 1300, equipmentPermille: 1000));
    }

    /// <summary>負の引数は <see cref="ArgumentOutOfRangeException"/>(タスク仕様 §2)。</summary>
    [Theory]
    [InlineData(-1, 1000)]
    [InlineData(1000, -1)]
    public void CapacityRunsRejectsNegativeArguments(int laborPermille, int equipmentPermille)
    {
        var recipe = RecipeWithLabor(laborPermille: 500);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => recipe.CapacityRuns(laborPermille, equipmentPermille));
    }
}
