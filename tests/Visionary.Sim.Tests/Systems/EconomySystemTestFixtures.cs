using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="ProductionSystem"/> / <see cref="ConsumptionSystem"/> のテスト用の小さな世界を組み立てる。
/// </summary>
/// <remarks>
/// タスク仕様(#34)が明示的に禁じている通り、<b><see cref="WorldDefinition.M0"/> の値には
/// 依存しない</b>(#28が値を動かす)。<see cref="WorldDefinition"/> のコンストラクタが要求する
/// 密度制約(世帯数 = 区画数9〜18)を満たすためだけに4つの「使わない」レシピを埋め、
/// 実際にテストが触るのは <c>household.Occupation = Occupation.Miller</c> の1世帯だけである。
/// </remarks>
internal static class EconomySystemTestFixtures
{
    /// <summary>
    /// テストが直接操作しない4職業ぶんの、実行しても無害な最小レシピ。
    /// </summary>
    /// <remarks>
    /// <b>入力を1件持たせる。</b><see cref="TradeSystem"/> のコンストラクタが全レシピ
    /// (この「使わない」4件を含む)の入力0件/出力2件以上を拒む(#35 タスク仕様)ため。
    /// <see cref="BuildWorldWithOneHousehold"/> がこれらの職業の世帯を作らない以上、
    /// <see cref="Systems.ProductionSystem"/> / <see cref="Systems.ConsumptionSystem"/> の
    /// テストの挙動には影響しない。
    /// </remarks>
    private static Recipe UnusedRecipe(Occupation occupation) =>
        new(
            occupation,
            outputs: new[] { new ItemQuantity { ItemId = 0, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = 1, Quantity = 1 } },
            laborPermille: 1);

    /// <summary>
    /// <paramref name="primaryRecipe"/> を <c>Occupation.Miller</c>(添字0)として登録した定義。
    /// </summary>
    /// <remarks>
    /// <b>既定の外部価格表はレシピを見て組み立てる</b>(#96 タスク仕様 §7)。1次産品
    /// (どのレシピも出力しない品目)か都市生産品かはレシピから決まるので、表の側で
    /// 決め打ちすると <paramref name="primaryRecipe"/> の品目次第で整合しなくなる。
    /// </remarks>
    internal static WorldDefinition BuildDefinition(
        Recipe primaryRecipe,
        int[]? laborPermilleByRank = null,
        int[][]? dailyConsumptionPerNpcByRank = null,
        int[]? firewoodConsumptionSeasonPermille = null,
        int minimumMarginPermille = 0,
        int observationRetentionDays = 7,
        int[]? necessityTargetStockDays = null,
        int[]? preferenceTargetStockDays = null,
        int toolTargetStockPermille = 1000,
        int[]? rankCoefficientPermille = null,
        int necessityTolerancePermille = 1000,
        int[]? budgetRatioPermilleByPurpose = null,
        int[]? opportunityCostBaseByOccupation = null,
        int travelHoursPerDistrict = 1,
        int acquisitionCostSmoothingPermille = 250,
        int inputBufferDays = 5,
        int shipmentDays = 1,
        int toolLifeLaborDays = 30,
        int equipmentPermilleWithoutTools = 0,
        int disposableHours = 12,
        int initialWorkshopInputDays = 0,
        int[]? externalSellPriceBaseOverride = null,
        int[][]? externalSellPriceSeasonPermilleOverride = null,
        int[]? externalBuyPriceOverride = null)
    {
        var recipes = new[]
        {
            primaryRecipe,
            UnusedRecipe(Occupation.Baker),
            UnusedRecipe(Occupation.Brewer),
            UnusedRecipe(Occupation.Woodworker),
            UnusedRecipe(Occupation.Smith),
        };

        var produced = ComputeProducedFlags(recipes);

        // 1次産品(=どのレシピも出力しない品目)だけが外部売値を持ち、都市生産品だけが
        // 外部買値を持つ(WorldDefinitionのコンストラクタの検証と同じ規則)。
        // *Override は WorldDefinition 自身の価格表の検証(WorldDefinitionTests)だけが使う ──
        // わざと崩した表を渡すため、既定の自動組み立てを丸ごと差し替える。
        var externalSellPriceBase = new int[Item.Count];
        var externalBuyPrice = new int[Item.Count];
        var externalSellPriceSeasonPermille = new int[Item.Count][];

        for (int itemId = 0; itemId < Item.Count; itemId++)
        {
            if (produced[itemId])
            {
                externalBuyPrice[itemId] = 1;
            }
            else
            {
                externalSellPriceBase[itemId] = 1;
            }

            // 年平均1000‰(合計4000)を満たす、季節性の無い既定の行。
            externalSellPriceSeasonPermille[itemId] = new[] { 1000, 1000, 1000, 1000 };
        }

        return new WorldDefinition(
            itemCount: Item.Count,
            householdsPerOccupation: 2, // 5職業 × 2 = 10戸(区画数9〜18を満たす)
            recipes: recipes,
            initialLiquidFunds: 0,
            initialAcquisitionCost: Enumerable.Repeat(1, Item.Count).ToArray(),
            initialHouseholdInventory: new int[Item.Count],
            initialWorkshopInputDays: initialWorkshopInputDays,
            initialToolStock: 1,
            initialSkillPermilleByRank: new[] { 0, 0, 0 },
            laborPermilleByRank: laborPermilleByRank ?? new[] { 1000, 800, 300 },
            dailyConsumptionPerNpcByRank: dailyConsumptionPerNpcByRank ?? ZeroConsumptionTable(),
            firewoodConsumptionSeasonPermille:
                firewoodConsumptionSeasonPermille ?? new[] { 1000, 1000, 1000, 1000 },
            minimumMarginPermille: minimumMarginPermille,
            observationRetentionDays: observationRetentionDays,
            necessityTargetStockDays: necessityTargetStockDays ?? new int[Item.Count],
            preferenceTargetStockDays: preferenceTargetStockDays ?? new int[Item.Count],
            toolTargetStockPermille: toolTargetStockPermille,
            rankCoefficientPermille: rankCoefficientPermille ?? new[] { 1000, 1000, 1000 },
            necessityTolerancePermille: necessityTolerancePermille,
            budgetRatioPermilleByPurpose: budgetRatioPermilleByPurpose ?? new[] { 0, 1, 0, 1 },
            opportunityCostBaseByOccupation:
                opportunityCostBaseByOccupation ?? Enumerable.Repeat(1, recipes.Length).ToArray(),
            travelHoursPerDistrict: travelHoursPerDistrict,
            acquisitionCostSmoothingPermille: acquisitionCostSmoothingPermille,
            externalSellPriceBase: externalSellPriceBaseOverride ?? externalSellPriceBase,
            externalSellPriceSeasonPermille:
                externalSellPriceSeasonPermilleOverride ?? externalSellPriceSeasonPermille,
            externalBuyPrice: externalBuyPriceOverride ?? externalBuyPrice,
            inputBufferDays: inputBufferDays,
            shipmentDays: shipmentDays,
            toolLifeLaborDays: toolLifeLaborDays,
            equipmentPermilleWithoutTools: equipmentPermilleWithoutTools,
            disposableHours: disposableHours);
    }

    /// <summary>どのレシピも出力しない品目か(= 1次産品か)を、渡されたレシピ表から求める。</summary>
    private static bool[] ComputeProducedFlags(Recipe[] recipes)
    {
        var produced = new bool[Item.Count];

        foreach (var recipe in recipes)
        {
            foreach (var output in recipe.Outputs)
            {
                produced[output.ItemId] = true;
            }
        }

        return produced;
    }

    private static int[][] ZeroConsumptionTable() =>
        new[] { new int[Item.Count], new int[Item.Count], new int[Item.Count] };

    /// <summary>
    /// 構成員 <paramref name="memberRanks"/>(先頭が世帯主)を持つ、世帯1戸だけの世界を作る。
    /// 職業は <c>Occupation.Miller</c>(<see cref="BuildDefinition"/> の <c>primaryRecipe</c> の添字)。
    /// </summary>
    internal static World BuildWorldWithOneHousehold(NpcRank[] memberRanks)
    {
        var world = new World(npcCount: memberRanks.Length, householdCount: 1, itemCount: Item.Count);

        for (int i = 0; i < memberRanks.Length; i++)
        {
            world.Npcs[i].Rank = memberRanks[i];
        }

        var memberNpcIds = Enumerable.Range(0, memberRanks.Length).ToArray();

        world.Households[0] = new HouseholdState(
            id: 0, districtId: 0, headNpcId: 0, memberNpcIds: memberNpcIds, itemCount: Item.Count);
        world.Households[0].Occupation = Occupation.Miller;

        return world;
    }

    /// <summary>
    /// システムを一切登録せず、時計だけを <paramref name="ticks"/> 進める
    /// (<see cref="StateHasherTests.HashChangesWhenClockAdvances"/> と同じ手法)。
    /// 季節をまたぐテストで、経路上の日を生産・消費させずに世界を早送りするために使う。
    /// </summary>
    internal static void AdvanceClockOnly(World world, int ticks) =>
        new SimScheduler(Array.Empty<ISimSystem>(), new RandomSource(1)).Advance(world, ticks);

    /// <summary>1つのシステムだけを登録し、<paramref name="days"/> 日ぶん(1日 = 24tick)進める。</summary>
    internal static void RunDays(World world, ISimSystem system, int days, long seed = 1)
    {
        var scheduler = new SimScheduler(new[] { system }, new RandomSource(seed));
        scheduler.Advance(world, ticks: days * 24);
    }
}
