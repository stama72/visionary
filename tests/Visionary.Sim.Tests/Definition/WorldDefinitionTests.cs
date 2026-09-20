using Visionary.Sim.Systems;
using Visionary.Sim.Tests.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Definition;

/// <summary><see cref="WorldDefinition"/>(GDD02 §2.2・§2.4 / GDD02c §1、#96 タスク仕様のテスト表)の検査。</summary>
public sealed class WorldDefinitionTests
{
    /// <summary>
    /// <see cref="WorldDefinition.M0"/> を土台に、検査したい1欄だけ差し替えて組み立てる。
    /// </summary>
    /// <remarks>
    /// 既存の欄(取得原価・世帯数・消費表・出荷/入力の予算まわり)の検証だけを確かめるテストが
    /// 使う。<b>#96 が足した検査(#2〜#6・#28)はこちらを使わない</b> ── 「校正テスト(#22〜#27)
    /// 以外は<c>BuildM0</c>の値に依存しないこと」(タスク仕様)なので、それらは
    /// <see cref="EconomySystemTestFixtures.BuildDefinition"/> で自前の最小定義を組む。
    /// </remarks>
    private static WorldDefinition BuildDefinition(
        int? initialToolStock = null,
        int[]? initialAcquisitionCost = null,
        int? householdsPerOccupation = null,
        int[]? laborPermilleByRank = null,
        int[][]? dailyConsumptionPerNpcByRank = null,
        int[]? firewoodConsumptionSeasonPermille = null,
        int? minimumMarginPermille = null,
        int? observationRetentionDays = null,
        int[]? necessityTargetStockDays = null,
        int[]? preferenceTargetStockDays = null,
        int? toolTargetStockPermille = null,
        int[]? rankCoefficientPermille = null,
        int? tolerancePermille = null,
        int[]? opportunityCostBaseByOccupation = null,
        int? travelHoursPerDistrict = null,
        int? acquisitionCostSmoothingPermille = null,
        int? inputBufferDays = null,
        int? shipmentDays = null,
        int? toolLifeLaborDays = null,
        int? equipmentPermilleWithoutTools = null,
        int? disposableHours = null,
        int? trustDiscountPermille = null)
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
            dailyConsumptionPerNpcByRank: dailyConsumptionPerNpcByRank ?? m0.DailyConsumptionPerNpcByRank,
            firewoodConsumptionSeasonPermille:
                firewoodConsumptionSeasonPermille ?? m0.FirewoodConsumptionSeasonPermille,
            minimumMarginPermille: minimumMarginPermille ?? m0.MinimumMarginPermille,
            observationRetentionDays: observationRetentionDays ?? m0.ObservationRetentionDays,
            necessityTargetStockDays: necessityTargetStockDays ?? m0.NecessityTargetStockDays,
            preferenceTargetStockDays: preferenceTargetStockDays ?? m0.PreferenceTargetStockDays,
            toolTargetStockPermille: toolTargetStockPermille ?? m0.ToolTargetStockPermille,
            rankCoefficientPermille: rankCoefficientPermille ?? m0.RankCoefficientPermille,
            tolerancePermille: tolerancePermille ?? m0.TolerancePermille,
            opportunityCostBaseByOccupation:
                opportunityCostBaseByOccupation ?? m0.OpportunityCostBaseByOccupation,
            travelHoursPerDistrict: travelHoursPerDistrict ?? m0.TravelHoursPerDistrict,
            acquisitionCostSmoothingPermille:
                acquisitionCostSmoothingPermille ?? m0.AcquisitionCostSmoothingPermille,
            externalSellPriceBase: DefaultExternalSellPriceBase,
            externalSellPriceSeasonPermille: DefaultExternalSellPriceSeasonPermille(),
            externalBuyPrice: DefaultExternalBuyPrice,
            inputBufferDays: inputBufferDays ?? m0.InputBufferDays,
            shipmentDays: shipmentDays ?? m0.ShipmentDays,
            toolLifeLaborDays: toolLifeLaborDays ?? m0.ToolLifeLaborDays,
            equipmentPermilleWithoutTools: equipmentPermilleWithoutTools ?? m0.EquipmentPermilleWithoutTools,
            disposableHours: disposableHours ?? m0.DisposableHours,
            trustDiscountPermille: trustDiscountPermille ?? m0.TrustDiscountPermille);
    }

    // WorldDefinitionは外部価格表を公開しない(private でよい、タスク仕様)。この
    // BuildDefinition(M0ベース)は m0.Recipes をそのまま使うので、1次産品(Grain/Timber/
    // IronOre/Charcoal)と都市生産品(Flour/Firewood/Bread/Beer/Tools)の分類は
    // EveryItemIsEitherImportedOrProducedByExactlyOneRecipe が押さえる不変条件のまま
    // 固定である。値そのものは意味を持たない(このテスト群は価格を assert しない)。
    private static readonly int[] DefaultExternalSellPriceBase = { 1, 1, 1, 1, 0, 0, 0, 0, 0 };
    private static readonly int[] DefaultExternalBuyPrice = { 0, 0, 0, 0, 1, 1, 1, 1, 1 };

    private static int[][] DefaultExternalSellPriceSeasonPermille()
    {
        var rows = new int[Item.Count][];
        for (int itemId = 0; itemId < Item.Count; itemId++)
        {
            rows[itemId] = new[] { 1000, 1000, 1000, 1000 };
        }

        return rows;
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
    /// 上流(GDD02d §2.2)の決定を写し間違える実装ミスを捕まえる(本タスクが実際に
    /// 鉄鉱石を20と書いた誤りそのもの)。§2.2は「1次産品の仕入単価は外部売値に固定される」
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
        Assert.Equal(9, cost[Item.Timber]);
        Assert.Equal(14, cost[Item.IronOre]);
        Assert.Equal(12, cost[Item.Charcoal]);
    }

    /// <summary>
    /// テスト表 #27(旧番)。既存欄の検証を確かめる。
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
            () => BuildDefinition(toolLifeLaborDays: 0));

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
    /// テスト表 #28(旧番)。渡した jagged 配列の行の中身を後から書き換えても定義が変わらないこと。
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
    /// 既存欄(必需・嗜好・工具の目標在庫まわり)の検証を確かめる。出荷/入力の目標在庫の表は
    /// #96 でメソッドへ置き換わり、コンストラクタ引数ではなくなったので、この検査からは外れる。
    /// </summary>
    [Fact]
    public void WorldDefinitionRejectsMalformedBudgetTables()
    {
        var m0 = WorldDefinition.M0;

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

        // 階層係数‰が0(その階層の世帯主の工具の目標在庫が0になる)
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(rankCoefficientPermille: new[] { 1000, 600, 0 }));

        // 許容乖離‰が0(全用途の相場項が常に0になる)
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(tolerancePermille: 0));

        // 保持期間が0(有効な観測が永久に0件になる)
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(observationRetentionDays: 0));

        // 負の最低利幅‰
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(minimumMarginPermille: -1));
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

    /// <summary>
    /// テスト表 #6。信用による実効価格の割引係数‰(<see cref="WorldDefinition.TrustDiscountPermille"/>)が
    /// 0〜999の外(-1・1000ちょうど)を拒む(GDD06 §2。1000ちょうどだと信用100で係数が0になり
    /// 実効価格が0になって下流(BuyerBudget.Decide / TradeSettlement.FundsCap)が投げる)。
    /// </summary>
    [Fact]
    public void WorldDefinitionRejectsTrustDiscountPermilleOutsideZeroToNineHundredNinetyNine()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(trustDiscountPermille: -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BuildDefinition(trustDiscountPermille: 1000));
    }

    // ── ここから #96 タスク仕様「落ちるべき条件」の新規テスト。
    // 「校正テスト(#22〜#27)以外はBuildM0の値に依存しないこと」(タスク仕様)を守るため、
    // 上のBuildDefinition(M0ベース)ではなく EconomySystemTestFixtures.BuildDefinition で
    // 自前の最小定義を組む。

    private static Recipe MillerLikeRecipe(int inputQuantity, int laborPermille) =>
        new(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = Item.Grain, Quantity = inputQuantity } },
            laborPermille: laborPermille);

    /// <summary>
    /// テスト表 #2。労働力1000/300、所要労働185‰、工具なし係数500‰の定義で
    /// <c>ProductionCapacity</c> = 7(= floor(1300÷185))。
    /// </summary>
    [Fact]
    public void ProductionCapacityUsesNominalLaborAndFullEquipment()
    {
        var definition = EconomySystemTestFixtures.BuildDefinition(
            MillerLikeRecipe(inputQuantity: 1, laborPermille: 185),
            laborPermilleByRank: new[] { 1000, 800, 300 },
            equipmentPermilleWithoutTools: 500);

        Assert.Equal(7, definition.ProductionCapacity(Occupation.Miller));
    }

    /// <summary>
    /// 【核心】テスト表 #3。穀物2→小麦粉1(所要労働185‰、生産能力7)の定義で D=3・出荷日数3 →
    /// <c>DailyInputQuantity(穀物)</c> = 14、<c>InputTargetStock(穀物)</c> = 42、
    /// <c>ShipmentTargetStock(小麦粉)</c> = 21、入力でも出力でもない品目は0。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測・再測(2026-09-19、レビュー1巡目 象限III)。</b><c>WorldDefinition.ShipmentTargetStock</c>
    /// の <c>ProductionCapacity(occupation) * output.Quantity * ShipmentDays</c> から
    /// <c>ProductionCapacity(occupation) *</c> を外す変異(生産能力を掛けるのを出荷側だけ忘れる)を
    /// 当てたところ、<c>Assert.Equal(21, ...ShipmentTargetStock(小麦粉))</c> が実際値3
    /// (出力数量1×出荷日数3)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void TargetStocksAreDerivedFromCapacity()
    {
        var definition = EconomySystemTestFixtures.BuildDefinition(
            MillerLikeRecipe(inputQuantity: 2, laborPermille: 185),
            laborPermilleByRank: new[] { 1000, 800, 300 },
            inputBufferDays: 3,
            shipmentDays: 3);

        Assert.Equal(7, definition.ProductionCapacity(Occupation.Miller));
        Assert.Equal(14, definition.DailyInputQuantity(Occupation.Miller, Item.Grain));
        Assert.Equal(42, definition.InputTargetStock(Occupation.Miller, Item.Grain));
        Assert.Equal(21, definition.ShipmentTargetStock(Occupation.Miller, Item.Flour));

        // 入力でも出力でもない品目(木材)は0。
        Assert.Equal(0, definition.DailyInputQuantity(Occupation.Miller, Item.Timber));
        Assert.Equal(0, definition.InputTargetStock(Occupation.Miller, Item.Timber));
        Assert.Equal(0, definition.ShipmentTargetStock(Occupation.Miller, Item.Timber));
    }

    /// <summary>
    /// 価格表の検査(#4〜#6)専用のレシピ。<see cref="EconomySystemTestFixtures.UnusedRecipe"/> が
    /// 品目Id 0 を出力・1 を入力に固定で使うため、それらと重ならない品目(木材=1次産品のまま、
    /// パン=このレシピだけが出力する都市生産品)を選ぶ。
    /// </summary>
    private static Recipe PriceTestRecipe(int inputQuantity, int laborPermille) =>
        new(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = Item.Timber, Quantity = inputQuantity } },
            laborPermille: laborPermille);

    // PriceTestRecipe + UnusedRecipe(4件)を組み合わせたときの、整合の取れた価格表。
    // 都市生産品は Grain(UnusedRecipeの出力)と Bread(PriceTestRecipeの出力)の2つだけ。
    private static int[] ValidSellPriceBaseForPriceTests()
    {
        var row = new int[Item.Count];
        for (int itemId = 0; itemId < Item.Count; itemId++)
        {
            row[itemId] = itemId is Item.Grain or Item.Bread ? 0 : 1;
        }

        return row;
    }

    private static int[] ValidBuyPriceForPriceTests()
    {
        var row = new int[Item.Count];
        row[Item.Grain] = 1;
        row[Item.Bread] = 1;
        return row;
    }

    /// <summary>
    /// 検証は <see cref="ArgumentException"/> か <see cref="ArgumentOutOfRangeException"/> の
    /// いずれかで例外になる(タスク仕様は例外の種別まで固定していない。<c>RecipeTests</c> と
    /// 同じ方式)。
    /// </summary>
    private static void AssertRejected(Action action, string why)
    {
        var thrown = Record.Exception(action);

        Assert.True(
            thrown is ArgumentException,
            $"WorldDefinition の検証が効いていない({why})。実際: {thrown?.GetType().Name ?? "例外なし"}");
    }

    /// <summary>
    /// テスト表 #4。1次産品の外部買値が1以上 / 1次産品の外部売値が0 / 都市生産品の外部売値が
    /// 1以上 / 都市生産品の外部買値が0を、それぞれ拒む。<see cref="PriceTestRecipe"/> では
    /// 木材(Timber)が1次産品、パン(Bread)が都市生産品。
    /// </summary>
    [Fact]
    public void WorldDefinitionRejectsPricesInconsistentWithRecipes()
    {
        Recipe Recipe() => PriceTestRecipe(inputQuantity: 1, laborPermille: 1000);

        AssertRejected(
            () =>
            {
                var buyPrice = ValidBuyPriceForPriceTests();
                buyPrice[Item.Timber] = 1; // 1次産品なのに非0
                EconomySystemTestFixtures.BuildDefinition(Recipe(), externalBuyPriceOverride: buyPrice);
            },
            "1次産品(Timber)の外部買値が1以上");

        AssertRejected(
            () =>
            {
                var sellBase = ValidSellPriceBaseForPriceTests();
                sellBase[Item.Timber] = 0; // 1次産品なのに0
                EconomySystemTestFixtures.BuildDefinition(Recipe(), externalSellPriceBaseOverride: sellBase);
            },
            "1次産品(Timber)の外部売値が0");

        AssertRejected(
            () =>
            {
                var sellBase = ValidSellPriceBaseForPriceTests();
                sellBase[Item.Bread] = 1; // 都市生産品なのに非0
                EconomySystemTestFixtures.BuildDefinition(Recipe(), externalSellPriceBaseOverride: sellBase);
            },
            "都市生産品(Bread)の外部売値が1以上");

        AssertRejected(
            () =>
            {
                var buyPrice = ValidBuyPriceForPriceTests();
                buyPrice[Item.Bread] = 0; // 都市生産品なのに0
                EconomySystemTestFixtures.BuildDefinition(Recipe(), externalBuyPriceOverride: buyPrice);
            },
            "都市生産品(Bread)の外部買値が0");
    }

    /// <summary>
    /// テスト表 #5。合計3999 / 4001の行、長さ3の行、要素0、都市生産品の行に1200を、
    /// それぞれ拒む。合計4000で通る。
    /// </summary>
    [Fact]
    public void WorldDefinitionRejectsSeasonRowsNotAveragingToBase()
    {
        Recipe Recipe() => PriceTestRecipe(inputQuantity: 1, laborPermille: 1000);

        int[][] SeasonRowsWithTimber(int[] timberRow)
        {
            var rows = new int[Item.Count][];
            for (int itemId = 0; itemId < Item.Count; itemId++)
            {
                rows[itemId] = new[] { 1000, 1000, 1000, 1000 };
            }

            rows[Item.Timber] = timberRow;
            return rows;
        }

        // 合計3999。
        Assert.Throws<ArgumentException>(() => EconomySystemTestFixtures.BuildDefinition(
            Recipe(), externalSellPriceSeasonPermilleOverride: SeasonRowsWithTimber(new[] { 999, 1000, 1000, 1000 })));

        // 合計4001。
        Assert.Throws<ArgumentException>(() => EconomySystemTestFixtures.BuildDefinition(
            Recipe(), externalSellPriceSeasonPermilleOverride: SeasonRowsWithTimber(new[] { 1001, 1000, 1000, 1000 })));

        // 長さ3の行。
        Assert.Throws<ArgumentException>(() => EconomySystemTestFixtures.BuildDefinition(
            Recipe(), externalSellPriceSeasonPermilleOverride: SeasonRowsWithTimber(new[] { 1000, 1000, 2000 })));

        // 要素0(非正)。
        Assert.Throws<ArgumentOutOfRangeException>(() => EconomySystemTestFixtures.BuildDefinition(
            Recipe(), externalSellPriceSeasonPermilleOverride: SeasonRowsWithTimber(new[] { 0, 1000, 2000, 1000 })));

        // 都市生産品(Bread)の行に1200(合計は4000のまま。1000以外は拒む)。
        Assert.Throws<ArgumentException>(() =>
        {
            var rows = SeasonRowsWithTimber(new[] { 1000, 1000, 1000, 1000 });
            rows[Item.Bread] = new[] { 1200, 1000, 900, 900 };
            EconomySystemTestFixtures.BuildDefinition(Recipe(), externalSellPriceSeasonPermilleOverride: rows);
        });

        // 合計4000で通る(既定のまま)。
        var definition = EconomySystemTestFixtures.BuildDefinition(Recipe());
        Assert.NotNull(definition);
    }

    /// <summary>
    /// テスト表 #6。基準10・係数{1000,1300,700,1000} → 春10・夏13・秋7・冬10。
    /// 基準12・係数1250 → 15。都市生産品に対して呼ぶと <c>ArgumentException</c>。
    /// <see cref="Item.Timber"/> と <see cref="Item.Charcoal"/> はどちらも
    /// <see cref="EconomySystemTestFixtures.UnusedRecipe"/> に触れられない1次産品。
    /// </summary>
    [Fact]
    public void ExternalSellPriceAppliesSeasonCoefficient()
    {
        var seasonRows = new int[Item.Count][];
        for (int itemId = 0; itemId < Item.Count; itemId++)
        {
            seasonRows[itemId] = new[] { 1000, 1000, 1000, 1000 };
        }

        seasonRows[Item.Timber] = new[] { 1000, 1300, 700, 1000 };
        seasonRows[Item.Charcoal] = new[] { 1000, 1250, 1000, 750 };

        var sellBase = ValidSellPriceBaseForPriceTests();
        sellBase[Item.Timber] = 10;
        sellBase[Item.Charcoal] = 12;

        var definition = EconomySystemTestFixtures.BuildDefinition(
            PriceTestRecipe(inputQuantity: 1, laborPermille: 1000),
            externalSellPriceBaseOverride: sellBase,
            externalSellPriceSeasonPermilleOverride: seasonRows);

        Assert.Equal(10, definition.ExternalSellPrice(Item.Timber, Season.Spring));
        Assert.Equal(13, definition.ExternalSellPrice(Item.Timber, Season.Summer));
        Assert.Equal(7, definition.ExternalSellPrice(Item.Timber, Season.Autumn));
        Assert.Equal(10, definition.ExternalSellPrice(Item.Timber, Season.Winter));
        Assert.Equal(15, definition.ExternalSellPrice(Item.Charcoal, Season.Summer));

        // 都市生産品(Bread)に対して呼ぶと例外。
        Assert.Throws<ArgumentException>(() => definition.ExternalSellPrice(Item.Bread, Season.Spring));
    }

    /// <summary>
    /// テスト表 #28。D / 出荷日数 / N / T の0、工具なし係数の−1と1001を、それぞれ拒む。
    /// </summary>
    [Fact]
    public void WorldDefinitionRejectsNonPositiveDaysLifeAndHours()
    {
        Recipe Recipe() => MillerLikeRecipe(inputQuantity: 1, laborPermille: 1000);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => EconomySystemTestFixtures.BuildDefinition(Recipe(), inputBufferDays: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EconomySystemTestFixtures.BuildDefinition(Recipe(), shipmentDays: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EconomySystemTestFixtures.BuildDefinition(Recipe(), toolLifeLaborDays: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EconomySystemTestFixtures.BuildDefinition(Recipe(), disposableHours: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EconomySystemTestFixtures.BuildDefinition(Recipe(), equipmentPermilleWithoutTools: -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EconomySystemTestFixtures.BuildDefinition(Recipe(), equipmentPermilleWithoutTools: 1001));
    }

    /// <summary>
    /// テスト表 #31。<c>TolerancePermille</c> が1200で、全用途の相場項がこれを使う(必需と嗜好で
    /// 同じ値になる)。<c>budgetRatioPermilleByPurpose</c> の引数が無いこと(コンパイルで担保)。
    /// </summary>
    [Fact]
    public void WorldDefinitionHasNoBudgetRatios()
    {
        Assert.Equal(1200, WorldDefinition.M0.TolerancePermille);

        // 必需だけに許容乖離‰を掛け、他の用途を素の相場基準にする実装ミスは、この呼び出しの
        // シグネチャそのものが要求する引数(tolerancePermilleのみ、用途別の欄が無い)によって
        // 構造的に防がれる。ここでは実際にBuyerDemand越しに、同じ相場基準を持つ必需と嗜好の
        // MarketTermが一致することを確かめる。
        var necessityDays = new int[Item.Count];
        necessityDays[Item.Bread] = 1;
        var preferenceDays = new int[Item.Count];
        preferenceDays[Item.Beer] = 1;

        var consumption = new int[Item.Count];
        consumption[Item.Bread] = 1;
        consumption[Item.Beer] = 1;
        var consumptionTable = new[]
        {
            (int[])consumption.Clone(), (int[])consumption.Clone(), (int[])consumption.Clone(),
        };

        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = Item.Grain, Quantity = 1 } },
            laborPermille: 1000);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe,
            dailyConsumptionPerNpcByRank: consumptionTable,
            necessityTargetStockDays: necessityDays,
            preferenceTargetStockDays: preferenceDays,
            tolerancePermille: 1200);

        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 10 * 24);

        void AddObservation(int itemId)
        {
            world.Knowledge[world.Households[0].HeadNpcId].Add(new PriceObservation
            {
                ItemId = itemId,
                LocationId = 0,
                Price = 100,
                SellerId = 999,
                ObservedAt = world.Now.AddDays(-1),
                Source = ObservationSource.Direct,
            });
        }

        AddObservation(Item.Bread);
        AddObservation(Item.Beer);

        var demand = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);

        var necessityLine = demand.Lines.Single(
            line => line.Purpose == DemandPurpose.Necessity && line.ItemId == Item.Bread);
        var preferenceLine = demand.Lines.Single(
            line => line.Purpose == DemandPurpose.Preference && line.ItemId == Item.Beer);

        Assert.True(necessityLine.HasMarketTerm);
        Assert.True(preferenceLine.HasMarketTerm);
        Assert.Equal(120, necessityLine.MarketTerm); // ApplyPermille(100, 1200) = 120
        Assert.Equal(necessityLine.MarketTerm, preferenceLine.MarketTerm);
    }
}
