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
        int? householdsPerOccupation = null,
        int[]? laborPermilleByRank = null,
        int? productionRunsPerToolWear = null,
        int[][]? dailyConsumptionPerNpcByRank = null,
        int[]? firewoodConsumptionSeasonPermille = null,
        int? minimumMarginPermille = null,
        int[][]? shipmentTargetStockByOccupation = null,
        int? observationRetentionDays = null,
        int[][]? inputTargetStockByOccupation = null,
        int[]? necessityTargetStockDays = null,
        int[]? preferenceTargetStockDays = null,
        int? toolTargetStockPermille = null,
        int[]? rankCoefficientPermille = null,
        int? necessityTolerancePermille = null,
        int[]? budgetRatioPermilleByPurpose = null,
        int[]? opportunityCostBaseByOccupation = null,
        int? travelHoursPerDistrict = null,
        int? acquisitionCostSmoothingPermille = null)
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
            initialSkillPermilleByRank: m0.InitialSkillPermilleByRank,
            laborPermilleByRank: laborPermilleByRank ?? m0.LaborPermilleByRank,
            productionRunsPerToolWear: productionRunsPerToolWear ?? m0.ProductionRunsPerToolWear,
            dailyConsumptionPerNpcByRank: dailyConsumptionPerNpcByRank ?? m0.DailyConsumptionPerNpcByRank,
            firewoodConsumptionSeasonPermille:
                firewoodConsumptionSeasonPermille ?? m0.FirewoodConsumptionSeasonPermille,
            minimumMarginPermille: minimumMarginPermille ?? m0.MinimumMarginPermille,
            shipmentTargetStockByOccupation:
                shipmentTargetStockByOccupation ?? m0.ShipmentTargetStockByOccupation,
            observationRetentionDays: observationRetentionDays ?? m0.ObservationRetentionDays,
            inputTargetStockByOccupation:
                inputTargetStockByOccupation ?? m0.InputTargetStockByOccupation,
            necessityTargetStockDays: necessityTargetStockDays ?? m0.NecessityTargetStockDays,
            preferenceTargetStockDays: preferenceTargetStockDays ?? m0.PreferenceTargetStockDays,
            toolTargetStockPermille: toolTargetStockPermille ?? m0.ToolTargetStockPermille,
            rankCoefficientPermille: rankCoefficientPermille ?? m0.RankCoefficientPermille,
            necessityTolerancePermille: necessityTolerancePermille ?? m0.NecessityTolerancePermille,
            budgetRatioPermilleByPurpose:
                budgetRatioPermilleByPurpose ?? m0.BudgetRatioPermilleByPurpose,
            opportunityCostBaseByOccupation:
                opportunityCostBaseByOccupation ?? m0.OpportunityCostBaseByOccupation,
            travelHoursPerDistrict: travelHoursPerDistrict ?? m0.TravelHoursPerDistrict,
            acquisitionCostSmoothingPermille:
                acquisitionCostSmoothingPermille ?? m0.AcquisitionCostSmoothingPermille);
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

    /// <summary>
    /// テスト表 #27。#34 が足した4欄それぞれの検証を確かめる。
    /// 長さを検査しないと実行時に <c>IndexOutOfRangeException</c> になり、
    /// N=0 を通すと <c>ProductionSystem</c> の摩耗計算でゼロ除算になる。
    /// </summary>
    [Fact]
    public void WorldDefinitionRejectsMalformedConsumptionTables()
    {
        var m0 = WorldDefinition.M0;

        // 階層の行数 ≠ 3(2行しかない)
        Assert.Throws<ArgumentException>(() => BuildDefinition(
            dailyConsumptionPerNpcByRank: new[] { m0.DailyConsumptionPerNpcByRank[0], m0.DailyConsumptionPerNpcByRank[1] }));

        // 行の長さ ≠ itemCount
        var shortRow = (int[][])m0.DailyConsumptionPerNpcByRank.Clone();
        shortRow[0] = new[] { 0, 0 };
        Assert.Throws<ArgumentException>(() => BuildDefinition(dailyConsumptionPerNpcByRank: shortRow));

        // 季節の長さ ≠ 4
        Assert.Throws<ArgumentException>(() => BuildDefinition(
            firewoodConsumptionSeasonPermille: new[] { 800, 400, 1000 }));

        // Nが0(ゼロ除算の元)
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(productionRunsPerToolWear: 0));

        // 負の消費係数
        var negativeRow = (int[][])m0.DailyConsumptionPerNpcByRank.Clone();
        negativeRow[0] = (int[])m0.DailyConsumptionPerNpcByRank[0].Clone();
        negativeRow[0][0] = -1;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(dailyConsumptionPerNpcByRank: negativeRow));

        // 負の労働力係数
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(laborPermilleByRank: new[] { -1, 800, 300 }));

        // 負の季節係数
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(firewoodConsumptionSeasonPermille: new[] { -1, 400, 1000, 2000 }));
    }

    /// <summary>
    /// テスト表 #28。渡した jagged 配列の行の中身を後から書き換えても定義が変わらないこと。
    /// 外側だけ <c>ToArray()</c> すると、検証を通した後に呼び出し側が元の行を書き換えられてしまう。
    /// </summary>
    [Fact]
    public void WorldDefinitionCopiesEachConsumptionRow()
    {
        var m0 = WorldDefinition.M0;

        var givenRows = new int[3][];
        for (int rank = 0; rank < 3; rank++)
        {
            givenRows[rank] = (int[])m0.DailyConsumptionPerNpcByRank[rank].Clone();
        }

        var definition = BuildDefinition(dailyConsumptionPerNpcByRank: givenRows);

        // 検証を通した後に、渡した行の中身を書き換える。
        givenRows[0][0] = 999;

        Assert.Equal(m0.DailyConsumptionPerNpcByRank[0], definition.DailyConsumptionPerNpcByRank[0]);
    }

    /// <summary>
    /// テスト表 #30。#35 が足した3欄(最低利幅‰・出荷目標在庫・保持期間)それぞれの検証を確かめる。
    /// 出力品目の欄の0を通すと在庫比‰(CeilDiv(1000×販売在庫, 出荷目標在庫))がゼロ除算になり、
    /// 表の形を検査しないと「どの欄が効くか」が読めない値(出力品目でない欄の非0)が住み着く。
    /// </summary>
    [Fact]
    public void WorldDefinitionRejectsMalformedShipmentTargets()
    {
        var m0 = WorldDefinition.M0;

        // 行数 ≠ 職業数(5行のうち4行しかない)
        Assert.Throws<ArgumentException>(() => BuildDefinition(
            shipmentTargetStockByOccupation: m0.ShipmentTargetStockByOccupation.Take(4).ToArray()));

        // 行の長さ ≠ itemCount
        var shortRow = CloneShipmentTargets(m0);
        shortRow[0] = new[] { 1 };
        Assert.Throws<ArgumentException>(
            () => BuildDefinition(shipmentTargetStockByOccupation: shortRow));

        // 出力品目(Miller: 小麦粉)の欄が0(ゼロ除算の元)
        var zeroedOutput = CloneShipmentTargets(m0);
        zeroedOutput[(int)Occupation.Miller][Item.Flour] = 0;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(shipmentTargetStockByOccupation: zeroedOutput));

        // 出力品目でない欄(Miller: 穀物は入力であって出力ではない)が非0
        var nonOutputFilled = CloneShipmentTargets(m0);
        nonOutputFilled[(int)Occupation.Miller][Item.Grain] = 1;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(shipmentTargetStockByOccupation: nonOutputFilled));

        // 保持期間が0(有効な観測が永久に0件になる)
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(observationRetentionDays: 0));

        // 負の最低利幅‰
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(minimumMarginPermille: -1));
    }

    /// <summary>
    /// テスト表 #31。渡した jagged 配列の行の中身を後から書き換えても定義が変わらないこと。
    /// 外側だけ <c>ToArray()</c> すると、検証を通した後に呼び出し側が元の行を書き換えられてしまう。
    /// </summary>
    [Fact]
    public void WorldDefinitionCopiesEachShipmentTargetRow()
    {
        var m0 = WorldDefinition.M0;
        var givenRows = CloneShipmentTargets(m0);

        var definition = BuildDefinition(shipmentTargetStockByOccupation: givenRows);

        // 検証を通した後に、渡した行の中身を書き換える。
        givenRows[0][Item.Flour] = 999;

        Assert.Equal(
            m0.ShipmentTargetStockByOccupation[0], definition.ShipmentTargetStockByOccupation[0]);
    }

    /// <summary>
    /// テスト表 #38。#36 が足した7欄それぞれの検証を確かめる。使われない調整軸(ProductionInput
    /// の予算比率)と単位の違う2本の行(Item.Toolsを必需・嗜好に置く)が定義から入るのを防ぐ。
    /// </summary>
    [Fact]
    public void WorldDefinitionRejectsMalformedBudgetTables()
    {
        var m0 = WorldDefinition.M0;

        // 入力品目(Miller: 穀物)の欄が0(「この入力は要らない」と読めてしまう)
        var zeroedInput = CloneInputTargets(m0);
        zeroedInput[(int)Occupation.Miller][Item.Grain] = 0;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(inputTargetStockByOccupation: zeroedInput));

        // 入力品目でない欄(Miller: 小麦粉は出力であって入力ではない)が非0
        var nonInputFilled = CloneInputTargets(m0);
        nonInputFilled[(int)Occupation.Miller][Item.Flour] = 1;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(inputTargetStockByOccupation: nonInputFilled));

        // 行数 ≠ 職業数(5行のうち4行しかない)
        Assert.Throws<ArgumentException>(() => BuildDefinition(
            inputTargetStockByOccupation: m0.InputTargetStockByOccupation.Take(4).ToArray()));

        // 必需と嗜好で同じ品目(薪)が両方で正
        var necessityWithFirewood = (int[])m0.NecessityTargetStockDays.Clone();
        var preferenceWithFirewood = (int[])m0.PreferenceTargetStockDays.Clone();
        preferenceWithFirewood[Item.Firewood] = 1; // 必需は既にFirewood=7(M0)
        Assert.Throws<ArgumentException>(() => BuildDefinition(
            necessityTargetStockDays: necessityWithFirewood,
            preferenceTargetStockDays: preferenceWithFirewood));

        // Item.Toolsを必需に置く(耐久と単位が異なる)
        var necessityWithTools = (int[])m0.NecessityTargetStockDays.Clone();
        necessityWithTools[Item.Tools] = 1;
        Assert.Throws<ArgumentException>(
            () => BuildDefinition(necessityTargetStockDays: necessityWithTools));

        // 予算比率の長さ ≠ 4
        Assert.Throws<ArgumentException>(() => BuildDefinition(
            budgetRatioPermilleByPurpose: new[] { 0, 50, 200 }));

        // ProductionInputの比率が非0(母数を持たない派生需要に調整軸を作らない)
        var productionInputRatioSet = (int[])m0.BudgetRatioPermilleByPurpose.Clone();
        productionInputRatioSet[(int)DemandPurpose.ProductionInput] = 1;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(budgetRatioPermilleByPurpose: productionInputRatioSet));

        // Necessityの比率が0(詰みを作る)
        var necessityRatioZero = (int[])m0.BudgetRatioPermilleByPurpose.Clone();
        necessityRatioZero[(int)DemandPurpose.Necessity] = 0;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(budgetRatioPermilleByPurpose: necessityRatioZero));

        // Durableの比率が0(詰みを作る)
        var durableRatioZero = (int[])m0.BudgetRatioPermilleByPurpose.Clone();
        durableRatioZero[(int)DemandPurpose.Durable] = 0;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(budgetRatioPermilleByPurpose: durableRatioZero));

        // 階層係数‰が0(その階層の世帯主の工具の目標在庫が0になる)
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(rankCoefficientPermille: new[] { 1000, 600, 0 }));

        // 必需の許容乖離‰が0
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(necessityTolerancePermille: 0));
    }

    /// <summary>
    /// テスト表 #39。渡した jagged 配列の行の中身を後から書き換えても定義が変わらないこと。
    /// 外側だけ <c>ToArray()</c> すると、検証を通した後に呼び出し側が元の行を書き換えられてしまう。
    /// </summary>
    [Fact]
    public void WorldDefinitionCopiesEachInputTargetRow()
    {
        var m0 = WorldDefinition.M0;
        var givenRows = CloneInputTargets(m0);

        var definition = BuildDefinition(inputTargetStockByOccupation: givenRows);

        // 検証を通した後に、渡した行の中身を書き換える。
        givenRows[(int)Occupation.Miller][Item.Grain] = 999;

        Assert.Equal(
            m0.InputTargetStockByOccupation[(int)Occupation.Miller],
            definition.InputTargetStockByOccupation[(int)Occupation.Miller]);
    }

    /// <summary>
    /// #37 が足した3欄(機会費用の基準値・1区画あたりの移動時間・仕入れ移動平均単価の
    /// 平滑化係数‰)それぞれの検証を確かめる。0を通すと移動費が全区画で0になるか
    /// (GDD08 §7.4「信用インフレを止める唯一の絞り」が恒偽になる)、移動平均が凍るか、
    /// 1000超で高値で仕入れるほど原価が下がる経路が入る。
    /// </summary>
    [Fact]
    public void WorldDefinitionRejectsMalformedTradeFields()
    {
        var m0 = WorldDefinition.M0;

        // 行数(長さ) ≠ 職業数(5要素のうち4要素しかない)
        Assert.Throws<ArgumentException>(() => BuildDefinition(
            opportunityCostBaseByOccupation: m0.OpportunityCostBaseByOccupation.Take(4).ToArray()));

        // 機会費用の基準値が0(移動費が全区画で0になる)
        var zeroedBase = (int[])m0.OpportunityCostBaseByOccupation.Clone();
        zeroedBase[0] = 0;
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(opportunityCostBaseByOccupation: zeroedBase));

        // 1区画あたりの移動時間が0(空間の摩擦が消える)
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(travelHoursPerDistrict: 0));

        // 平滑化係数‰が0(移動平均が初期値に凍る)
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(acquisitionCostSmoothingPermille: 0));

        // 平滑化係数‰が1000超(高値で仕入れるほど原価が下がる)
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(acquisitionCostSmoothingPermille: 1001));
    }

    private static int[][] CloneInputTargets(WorldDefinition definition)
    {
        var rows = new int[definition.InputTargetStockByOccupation.Length][];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = (int[])definition.InputTargetStockByOccupation[i].Clone();
        }

        return rows;
    }

    private static int[][] CloneShipmentTargets(WorldDefinition definition)
    {
        var rows = new int[definition.ShipmentTargetStockByOccupation.Length][];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = (int[])definition.ShipmentTargetStockByOccupation[i].Clone();
        }

        return rows;
    }
}
