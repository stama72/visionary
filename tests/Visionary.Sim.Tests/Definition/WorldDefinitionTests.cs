namespace Visionary.Sim.Tests.Definition;

/// <summary><see cref="WorldDefinition"/>(GDD02 §2.2・§2.4・§8.1)の検査。</summary>
public sealed class WorldDefinitionTests
{
    /// <summary>
    /// <see cref="WorldDefinition.M0"/> を土台に、検査したい1欄だけ差し替えて組み立てる。
    /// </summary>
    private static WorldDefinition BuildDefinition(
        int? initialToolStock = null,
        int[]? initialAcquisitionCost = null,
        int? householdsPerOccupation = null)
    {
        var m0 = WorldDefinition.M0;

        return new WorldDefinition(
            itemCount: m0.ItemCount,
            householdsPerOccupation: householdsPerOccupation ?? m0.HouseholdsPerOccupation,
            recipes: m0.Recipes,
            initialLiquidFunds: m0.InitialLiquidFunds,
            initialAcquisitionCost: initialAcquisitionCost ?? m0.InitialAcquisitionCost,
            initialHouseholdInventory: m0.InitialHouseholdInventory,
            initialWorkshopInputDays: m0.InitialWorkshopInputDays,
            initialToolStock: initialToolStock ?? m0.InitialToolStock,
            initialSkillPermilleByRank: m0.InitialSkillPermilleByRank);
    }

    [Fact]
    public void WorldDefinitionRejectsZeroToolStock()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildDefinition(initialToolStock: 0));
    }

    [Fact]
    public void WorldDefinitionRejectsZeroAcquisitionCost()
    {
        int[] withZero = (int[])WorldDefinition.M0.InitialAcquisitionCost.Clone();
        withZero[0] = 0;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(initialAcquisitionCost: withZero));
    }

    /// <summary>
    /// M0 の職業数は5なので、世帯数 = 5 × <c>householdsPerOccupation</c>。
    /// 1 なら5世帯(区画数9未満)、4 なら20世帯(区画数×2=18超)で密度制約を破る。
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void WorldDefinitionRejectsHouseholdCountsThatBreakTheDensity(int householdsPerOccupation)
    {
        Assert.Throws<ArgumentException>(
            () => BuildDefinition(householdsPerOccupation: householdsPerOccupation));
    }

    [Fact]
    public void M0RecipesMatchTheOccupationTable()
    {
        var recipes = WorldDefinition.M0.Recipes;

        Assert.Equal(5, recipes.Length);

        AssertRecipe(
            recipes[(int)Occupation.Miller], Occupation.Miller,
            inputs: new[] { (Item.Grain, 2) }, outputs: new[] { (Item.Flour, 1) });
        AssertRecipe(
            recipes[(int)Occupation.Baker], Occupation.Baker,
            inputs: new[] { (Item.Flour, 1), (Item.Firewood, 1) }, outputs: new[] { (Item.Bread, 2) });
        AssertRecipe(
            recipes[(int)Occupation.Brewer], Occupation.Brewer,
            inputs: new[] { (Item.Grain, 2), (Item.Firewood, 1) }, outputs: new[] { (Item.Beer, 1) });
        AssertRecipe(
            recipes[(int)Occupation.Woodworker], Occupation.Woodworker,
            inputs: new[] { (Item.Timber, 1) }, outputs: new[] { (Item.Firewood, 3) });
        AssertRecipe(
            recipes[(int)Occupation.Smith], Occupation.Smith,
            inputs: new[] { (Item.IronOre, 2), (Item.Charcoal, 1) }, outputs: new[] { (Item.Tools, 1) });
    }

    private static void AssertRecipe(
        Recipe recipe,
        Occupation expectedOccupation,
        (int ItemId, int Quantity)[] inputs,
        (int ItemId, int Quantity)[] outputs)
    {
        Assert.Equal(expectedOccupation, recipe.Occupation);

        Assert.Equal(inputs.Length, recipe.Inputs.Length);
        for (int i = 0; i < inputs.Length; i++)
        {
            Assert.Equal(inputs[i].ItemId, recipe.Inputs[i].ItemId);
            Assert.Equal(inputs[i].Quantity, recipe.Inputs[i].Quantity);
        }

        Assert.Equal(outputs.Length, recipe.Outputs.Length);
        for (int i = 0; i < outputs.Length; i++)
        {
            Assert.Equal(outputs[i].ItemId, recipe.Outputs[i].ItemId);
            Assert.Equal(outputs[i].Quantity, recipe.Outputs[i].Quantity);
        }
    }

    /// <summary>
    /// 品目 0〜3(穀物・木材・鉄鉱石・木炭)は1次・都市外市場からの輸入のみ(GDD02 §2.2)。
    /// 品目 4〜8(小麦粉・薪・パン・ビール・工具)はちょうど1つのレシピが生産する。
    /// </summary>
    [Fact]
    public void EveryItemIsEitherImportedOrProducedByExactlyOneRecipe()
    {
        var recipes = WorldDefinition.M0.Recipes;
        var producedByCount = new int[Item.Count];

        foreach (var recipe in recipes)
        {
            foreach (var output in recipe.Outputs)
            {
                producedByCount[output.ItemId]++;
            }
        }

        int[] importedItemIds = { Item.Grain, Item.Timber, Item.IronOre, Item.Charcoal };

        for (int itemId = 0; itemId < Item.Count; itemId++)
        {
            int expected = importedItemIds.Contains(itemId) ? 0 : 1;

            Assert.Equal(expected, producedByCount[itemId]);
        }
    }

    /// <summary>
    /// 上流(GDD02 §10.2)の決定を写し間違える実装ミスを捕まえる(本タスクが実際に
    /// 鉄鉱石を20と書いた誤りそのもの)。§10.2は「1次産品の仕入単価は外部売値に固定される」
    /// 「外部売値が1次産品の価格の天井になる」と決定しており、天井を超える取得原価は
    /// 都市内の誰も支払えない。
    /// </summary>
    /// <remarks>
    /// <b>この検査を <see cref="WorldDefinition"/> のコンストラクタに持ち込まない。</b>
    /// 1次産品が0〜3であることは <see cref="Item"/> の採番の知識であり、
    /// <see cref="WorldDefinition"/> は品目の意味を知らない(<c>ItemCount</c> しか持たない)。
    /// <c>M0</c> の表の写し間違いを捕まえるのが目的なので、<c>M0</c> を見るテストが
    /// 正しい置き場所である。
    /// </remarks>
    [Fact]
    public void PrimaryGoodCostsMatchTheExternalSellPrices()
    {
        var cost = WorldDefinition.M0.InitialAcquisitionCost;

        Assert.Equal(10, cost[Item.Grain]);
        Assert.Equal(8, cost[Item.Timber]);
        Assert.Equal(14, cost[Item.IronOre]);
        Assert.Equal(12, cost[Item.Charcoal]);
    }
}
