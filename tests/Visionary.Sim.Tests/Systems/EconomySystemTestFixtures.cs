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
    /// <summary>テストが直接操作しない4職業ぶんの、実行しても無害な最小レシピ。</summary>
    private static Recipe UnusedRecipe(Occupation occupation) =>
        new(
            occupation,
            outputs: new[] { new ItemQuantity { ItemId = 0, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 1);

    /// <summary>
    /// <paramref name="primaryRecipe"/> を <c>Occupation.Miller</c>(添字0)として登録した定義。
    /// </summary>
    internal static WorldDefinition BuildDefinition(
        Recipe primaryRecipe,
        int[]? laborPermilleByRank = null,
        int productionRunsPerToolWear = 30,
        int[][]? dailyConsumptionPerNpcByRank = null,
        int[]? firewoodConsumptionSeasonPermille = null)
    {
        var recipes = new[]
        {
            primaryRecipe,
            UnusedRecipe(Occupation.Baker),
            UnusedRecipe(Occupation.Brewer),
            UnusedRecipe(Occupation.Woodworker),
            UnusedRecipe(Occupation.Smith),
        };

        return new WorldDefinition(
            itemCount: Item.Count,
            householdsPerOccupation: 2, // 5職業 × 2 = 10戸(区画数9〜18を満たす)
            recipes: recipes,
            initialLiquidFunds: 0,
            initialAcquisitionCost: Enumerable.Repeat(1, Item.Count).ToArray(),
            initialHouseholdInventory: new int[Item.Count],
            initialWorkshopInputDays: 0,
            initialToolStock: 1,
            initialSkillPermilleByRank: new[] { 0, 0, 0 },
            laborPermilleByRank: laborPermilleByRank ?? new[] { 1000, 800, 300 },
            productionRunsPerToolWear: productionRunsPerToolWear,
            dailyConsumptionPerNpcByRank: dailyConsumptionPerNpcByRank ?? ZeroConsumptionTable(),
            firewoodConsumptionSeasonPermille:
                firewoodConsumptionSeasonPermille ?? new[] { 1000, 1000, 1000, 1000 });
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
