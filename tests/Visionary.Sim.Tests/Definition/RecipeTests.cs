namespace Visionary.Sim.Tests.Definition;

/// <summary><see cref="Recipe"/>(GDD02 §2.3)の検証の検査。</summary>
public sealed class RecipeTests
{
    /// <summary>
    /// 検証は <see cref="ArgumentException"/> か <see cref="ArgumentOutOfRangeException"/>
    /// のいずれかで例外になる(タスク仕様は例外の種別まで固定していない)。
    /// <see cref="ArgumentOutOfRangeException"/> は <see cref="ArgumentException"/> を
    /// 継承するので、基底で受ける(<c>HouseholdStateTests</c> と同じ方式)。
    /// </summary>
    private static void AssertRejected(Func<Recipe> factory, string why)
    {
        var thrown = Record.Exception(() => factory());

        Assert.True(
            thrown is ArgumentException,
            $"Recipe の検証が効いていない({why})。実際: {thrown?.GetType().Name ?? "例外なし"}");
    }

    [Fact]
    public void RecipeRejectsDuplicateItemIdsAndEmptyOutputs()
    {
        AssertRejected(
            () => new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
                inputs: new[]
                {
                    new ItemQuantity { ItemId = Item.Grain, Quantity = 2 },
                    new ItemQuantity { ItemId = Item.Grain, Quantity = 3 },
                },
                laborPermille: 1000),
            "入力に同じItemIdが2度現れる");

        AssertRejected(
            () => new Recipe(
                Occupation.Miller,
                outputs: new[]
                {
                    new ItemQuantity { ItemId = Item.Flour, Quantity = 1 },
                    new ItemQuantity { ItemId = Item.Flour, Quantity = 2 },
                },
                inputs: Array.Empty<ItemQuantity>(),
                laborPermille: 1000),
            "出力に同じItemIdが2度現れる");

        AssertRejected(
            () => new Recipe(
                Occupation.Miller,
                outputs: Array.Empty<ItemQuantity>(),
                inputs: Array.Empty<ItemQuantity>(),
                laborPermille: 1000),
            "出力が空");

        AssertRejected(
            () => new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 0 } },
                inputs: Array.Empty<ItemQuantity>(),
                laborPermille: 1000),
            "数量が1未満");

        AssertRejected(
            () => new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
                inputs: Array.Empty<ItemQuantity>(),
                laborPermille: 0),
            "所要労働‰が1未満");
    }
}
