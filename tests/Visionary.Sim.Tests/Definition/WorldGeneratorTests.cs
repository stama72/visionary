using Visionary.Sim.Determinism;
using Visionary.Sim.Randomness;

namespace Visionary.Sim.Tests.Definition;

/// <summary>
/// <see cref="WorldGenerator"/>(GDD02 §2.2・§2.4・§4.3・§8.1)の初期配置の検査。
/// </summary>
/// <remarks>
/// 構造制約の検査(#1・#2・#4・#7、タスク仕様の番号)は masterSeed を1〜200まで回す。
/// 1シードだけだと、たまたま通った配置と構造で保証された配置が区別できない。
/// </remarks>
public sealed class WorldGeneratorTests
{
    private const long FirstSeed = 1;
    private const long LastSeed = 200;

    private static World Generate(long seed, WorldDefinition? definition = null) =>
        WorldGenerator.Generate(definition ?? WorldDefinition.M0, new RandomSource(seed));

    /// <summary>
    /// 【核心】区画 0〜8 の世帯数がいずれも1以上2以下(200シード)。
    /// </summary>
    /// <remarks>
    /// GDD02 §4.3 の密度が崩れると、§2.4 が密度から導いている「中心に隣接する4区画
    /// (1/3/5/7)は必ず埋まる」= §4.2 の観測の広がりの下限が消える。
    /// </remarks>
    [Fact]
    public void EveryDistrictHoldsOneOrTwoHouseholds()
    {
        for (long seed = FirstSeed; seed <= LastSeed; seed++)
        {
            var world = Generate(seed);

            // 区画ごとの世帯数をテスト側で組み立てる(核心印)。
            var countByDistrict = new int[District.Count];

            foreach (var household in world.Households)
            {
                countByDistrict[household.DistrictId]++;
            }

            for (int districtId = 0; districtId < District.Count; districtId++)
            {
                Assert.InRange(countByDistrict[districtId], 1, 2);
            }
        }
    }

    /// <summary>
    /// 【核心】同じ職業の2世帯の区画Idが異なる(200シード)。
    /// </summary>
    /// <remarks>
    /// 崩れると、当該品目の売り手が1区画に集まり、GDD02 §12-4(区画間の価格差が消えない)が
    /// 「消える」のではなく最初から存在しなくなる。
    /// </remarks>
    [Fact]
    public void SameOccupationHouseholdsNeverShareADistrict()
    {
        for (long seed = FirstSeed; seed <= LastSeed; seed++)
        {
            var world = Generate(seed);

            // 区画ごとに世帯を集める(核心印: 中間の集合をテスト側が構築する)。
            var householdsByDistrict = new List<HouseholdState>[District.Count];
            for (int districtId = 0; districtId < District.Count; districtId++)
            {
                householdsByDistrict[districtId] = new List<HouseholdState>();
            }

            foreach (var household in world.Households)
            {
                householdsByDistrict[household.DistrictId].Add(household);
            }

            foreach (var householdsInDistrict in householdsByDistrict)
            {
                for (int i = 0; i < householdsInDistrict.Count; i++)
                {
                    for (int j = i + 1; j < householdsInDistrict.Count; j++)
                    {
                        Assert.NotEqual(
                            householdsInDistrict[i].Occupation, householdsInDistrict[j].Occupation);
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(42L)]
    [InlineData(123456789L)]
    public void PlacementIsDeterministicForTheSameSeed(long seed)
    {
        var first = Generate(seed);
        var second = Generate(seed);

        Assert.Equal(StateHasher.Compute(first), StateHasher.Compute(second));
    }

    /// <summary>
    /// 乱数を引いておきながら結果に使わない(固定配置に落ちる)実装ミスを捕まえる。
    /// #3 は固定配置でも緑になるので、これが無いと乱数の存在が検証されない。
    /// </summary>
    [Fact]
    public void PlacementVariesAcrossSeeds()
    {
        var hashes = new HashSet<ulong>();

        for (long seed = FirstSeed; seed <= LastSeed; seed++)
        {
            hashes.Add(StateHasher.Compute(Generate(seed)));
        }

        Assert.True(
            hashes.Count >= 2,
            "200シードすべてで同じ配置になった。乱数が配置に反映されていない可能性がある。");
    }

    [Fact]
    public void EachHouseholdHasOneMasterAndOneApprentice()
    {
        var world = Generate(seed: 1);

        foreach (var household in world.Households)
        {
            Assert.Equal(2, household.MemberNpcIds.Length);

            var members = household.MemberNpcIds.Select(npcId => world.Npcs[npcId]).ToArray();

            Assert.Single(members, npc => npc.Rank == NpcRank.Master);
            Assert.Single(members, npc => npc.Rank == NpcRank.Apprentice);

            Assert.Equal(NpcRank.Master, world.Npcs[household.HeadNpcId].Rank);
        }
    }

    [Fact]
    public void HouseholdMembershipAgreesInBothDirections()
    {
        var world = Generate(seed: 1);

        foreach (var npc in world.Npcs)
        {
            Assert.Contains(npc.Id, world.Households[npc.HouseholdId].MemberNpcIds);
        }

        foreach (var household in world.Households)
        {
            foreach (int memberNpcId in household.MemberNpcIds)
            {
                Assert.Equal(household.Id, world.Npcs[memberNpcId].HouseholdId);
            }
        }
    }

    /// <summary>
    /// 1品目の売り手が1世帯になると GDD02 §8.1.1 の相場基準が「他の売り手の観測0件」に落ち、
    /// 同節が名指しで警告する純粋な自己ループが成立する。
    /// </summary>
    [Fact]
    public void EveryOccupationGetsExactlyTheDefinedNumberOfHouseholds()
    {
        var definition = WorldDefinition.M0;

        for (long seed = FirstSeed; seed <= LastSeed; seed++)
        {
            var world = Generate(seed, definition);
            var countByOccupation = new int[definition.OccupationCount];

            foreach (var household in world.Households)
            {
                countByOccupation[(int)household.Occupation]++;
            }

            foreach (int count in countByOccupation)
            {
                Assert.Equal(definition.HouseholdsPerOccupation, count);
            }
        }
    }

    /// <summary>
    /// 工具を与えないと GDD02 §5.3 の設備係数が全世帯0‰になり初日から全生産が停止する。
    /// 鍛冶自身も工具が無いと工具を作れないので回復経路が無く、GDD02 §4.2「詰みは作らない」に反する。
    /// </summary>
    [Fact]
    public void EveryWorkshopStartsWithAtLeastOneTool()
    {
        var world = Generate(seed: 1);

        Assert.All(world.Households, household => Assert.True(household.WorkshopInventory[Item.Tools] >= 1));
    }

    /// <summary>
    /// GDD02 §8.1「与えないと初日の原価が0になる」、§8.1.1「0が恒久に固定され、下流の原価が
    /// 移動平均で0へ向かい、原価下限が消えて半減ループに歯止めがなくなる」経路を初日に踏まない。
    /// </summary>
    [Fact]
    public void InitialCostAverageIsSeededFromTheCatalogAndNeverZero()
    {
        var definition = WorldDefinition.M0;
        var world = Generate(seed: 1, definition);

        foreach (var household in world.Households)
        {
            for (int itemId = 0; itemId < definition.ItemCount; itemId++)
            {
                Assert.Equal(
                    definition.InitialAcquisitionCost[itemId], household.PurchaseUnitCostAverage[itemId]);
                Assert.True(household.PurchaseUnitCostAverage[itemId] >= 1);
            }
        }
    }

    /// <summary>
    /// 【核心】各世帯の工房在庫が自職業のレシピの入力ぶんだけ積まれ、レシピに現れない品目
    /// (工具を除く)は0であること。#34 の生産が初日に入力切れを起こさない前提。
    /// </summary>
    [Fact]
    public void WorkshopStocksTheInputsOfItsOwnRecipe()
    {
        var definition = WorldDefinition.M0;
        var world = Generate(seed: 1, definition);

        foreach (var household in world.Households)
        {
            var recipe = definition.Recipes[(int)household.Occupation];

            // 自職業のレシピの入力から期待される在庫量をテスト側で組み立てる(核心印)。
            var expectedByItemId = new int[definition.ItemCount];
            foreach (var input in recipe.Inputs)
            {
                expectedByItemId[input.ItemId] = definition.InitialWorkshopInputDays * input.Quantity;
            }

            expectedByItemId[Item.Tools] += definition.InitialToolStock;

            for (int itemId = 0; itemId < definition.ItemCount; itemId++)
            {
                Assert.Equal(expectedByItemId[itemId], household.WorkshopInventory[itemId]);
            }
        }
    }

    /// <summary>
    /// <c>Occupation</c> の enum 化で <c>(int)</c> の書き出しを落とす、または仕入れ移動平均単価を
    /// <c>Compute</c> に書き足し忘れる実装ミスを捕まえる。後者は2プロセス比較では検出できない
    /// (同一ビルド同士なので、見ていない状態があっても一致は成立する)。
    /// </summary>
    [Theory]
    [InlineData("occupation")]
    [InlineData("cost")]
    public void HashChangesWhenOccupationOrCostAverageChanges(string field)
    {
        var world = Generate(seed: 1);
        ulong before = StateHasher.Compute(world);

        switch (field)
        {
            case "occupation":
                world.Households[0].Occupation = world.Households[0].Occupation == Occupation.Miller
                    ? Occupation.Smith
                    : Occupation.Miller;
                break;

            case "cost":
                world.Households[0].PurchaseUnitCostAverage[0] += 1;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, "未知のフィールド。");
        }

        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }
}
