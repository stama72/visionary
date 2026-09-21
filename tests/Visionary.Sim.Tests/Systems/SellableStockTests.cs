using Visionary.Sim.Systems;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="SellableStock"/>(GDD02c §1.3、W2-14 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class SellableStockTests
{
    /// <summary>単一構成員(headNpcId == 0)の世帯を、指定の職業で作る。</summary>
    private static HouseholdState BuildHousehold(Occupation occupation, int itemCount = Item.Count)
    {
        var household = new HouseholdState(
            id: 0, districtId: 0, headNpcId: 0, memberNpcIds: new[] { 0 }, itemCount: itemCount);
        household.Occupation = occupation;

        return household;
    }

    /// <summary>テスト表 #1。M0 の鍛冶について、工具の留保量が1。</summary>
    [Fact]
    public void ReserveOfTheSmithsOutputIsOneTool()
    {
        var definition = WorldDefinition.M0;
        var household = BuildHousehold(Occupation.Smith, definition.ItemCount);

        Assert.Equal(1, SellableStock.ReserveQuantity(definition, household, Item.Tools));
    }

    /// <summary>
    /// テスト表 #2。M0 の5レシピすべてについて、出力品目の留保量が鍛冶だけ1・他は0
    /// (パン屋のパンは出力だが設備でも入力でもないので0)。
    /// </summary>
    [Fact]
    public void ReserveOfAnItemThatIsNeitherEquipmentNorInputIsZero()
    {
        var definition = WorldDefinition.M0;

        foreach (var recipe in definition.Recipes)
        {
            var household = BuildHousehold(recipe.Occupation, definition.ItemCount);
            int outputItemId = recipe.Outputs[0].ItemId;
            int expected = recipe.Occupation == Occupation.Smith ? 1 : 0;

            Assert.Equal(expected, SellableStock.ReserveQuantity(definition, household, outputItemId));
        }
    }

    /// <summary>
    /// テスト表 #3。合成レシピ(出力品目を自分の入力にも持つ)で、留保量がその入力の
    /// <c>Quantity</c> と等しい(#148 決定3。M0 では発火しないが、規則としては置いてある)。
    /// </summary>
    [Fact]
    public void ReserveOfAnOutputThatIsAlsoItsOwnInputIsTheRecipeQuantity()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = 1 } },
            inputs: new[]
            {
                new ItemQuantity { ItemId = Item.Bread, Quantity = 5 }, // 自分の出力を自分の入力にも持つ。
                new ItemQuantity { ItemId = Item.Grain, Quantity = 1 },
            },
            laborPermille: 1000);

        var definition = EconomySystemTestFixtures.BuildDefinition(recipe);
        var household = BuildHousehold(Occupation.Miller, definition.ItemCount);

        Assert.Equal(5, SellableStock.ReserveQuantity(definition, household, Item.Bread));
    }

    /// <summary>
    /// 【核心】テスト表 #4。鍛冶の工房在庫0・1のどちらでも販売在庫が0(負にしない)。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SellableStockNeverGoesNegative(int toolStock)
    {
        var definition = WorldDefinition.M0;
        var household = BuildHousehold(Occupation.Smith, definition.ItemCount);
        household.WorkshopInventory[Item.Tools] = toolStock;

        Assert.Equal(0, SellableStock.Of(definition, household, Item.Tools));
    }

    /// <summary>
    /// 規則6の具体例(鍛冶、留保1)。工房在庫0→0/1→0/2→1/5→4。
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(5, 4)]
    public void SellableStockIsWorkshopInventoryMinusReserve(int toolStock, int expectedSellableStock)
    {
        var definition = WorldDefinition.M0;
        var household = BuildHousehold(Occupation.Smith, definition.ItemCount);
        household.WorkshopInventory[Item.Tools] = toolStock;

        Assert.Equal(expectedSellableStock, SellableStock.Of(definition, household, Item.Tools));
    }
}
