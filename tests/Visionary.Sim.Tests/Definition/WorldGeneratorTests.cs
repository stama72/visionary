using Visionary.Sim.Determinism;
using Visionary.Sim.Randomness;
using Visionary.Sim.Tests.Systems;

namespace Visionary.Sim.Tests.Definition;

/// <summary>
/// <see cref="WorldGenerator"/>(GDD02 §2.2・§2.4・§4.3 / GDD02c §1)の初期配置の検査。
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
    /// 崩れると、当該品目の売り手が1区画に集まり、GDD02 §8-4(区画間の価格差が消えない)が
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
    /// 1品目の売り手が1世帯になると GDD02c §1.2 の相場基準が「他の売り手の観測0件」に落ち、
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
    /// 工具を与えないと GDD02a §3 の設備係数が全世帯0‰になり初日から全生産が停止する。
    /// 鍛冶自身も工具が無いと工具を作れないので回復経路が無く、GDD02 §4.2「詰みは作らない」に反する。
    /// </summary>
    [Fact]
    public void EveryWorkshopStartsWithAtLeastOneTool()
    {
        var world = Generate(seed: 1);

        Assert.All(world.Households, household => Assert.True(household.WorkshopInventory[Item.Tools] >= 1));
    }

    /// <summary>
    /// GDD02a §5.1「与えないと初日の原価が0になる」、§5.1「0が恒久に固定され、下流の原価が
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
            // #96: × 必要数量ではなく × 1日の投入量(DailyInputQuantity)。
            var expectedByItemId = new int[definition.ItemCount];
            foreach (var input in recipe.Inputs)
            {
                expectedByItemId[input.ItemId] = definition.InitialWorkshopInputDays
                    * definition.DailyInputQuantity(household.Occupation, input.ItemId);
            }

            expectedByItemId[Item.Tools] += definition.InitialToolStock;

            for (int itemId = 0; itemId < definition.ItemCount; itemId++)
            {
                Assert.Equal(expectedByItemId[itemId], household.WorkshopInventory[itemId]);
            }
        }
    }

    /// <summary>
    /// テスト表 #21。<c>InitialWorkshopInputDays = 3</c>・穀物2→小麦粉1・生産能力7の定義 →
    /// 水車小屋番の工房在庫[穀物] = 42(= 3 × DailyInputQuantity(穀物) = 3 × 14)。
    /// 同じシードで2回生成すると配置が一致する(M0 以外の定義でも決定論が保たれること)。
    /// </summary>
    [Fact]
    public void WorldGeneratorSeedsWorkshopInputsFromDailyInputQuantity()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = Item.Grain, Quantity = 2 } },
            laborPermille: 185); // 1300‰ ÷ 185‰ = 7実行/日

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe,
            laborPermilleByRank: new[] { 1000, 800, 300 },
            initialWorkshopInputDays: 3);

        Assert.Equal(7, definition.ProductionCapacity(Occupation.Miller));
        Assert.Equal(14, definition.DailyInputQuantity(Occupation.Miller, Item.Grain));

        var first = WorldGenerator.Generate(definition, new RandomSource(1));
        var second = WorldGenerator.Generate(definition, new RandomSource(1));

        // 水車小屋番(Occupation.Miller、添字0)の世帯を探す(HouseholdsPerOccupation=2なので
        // 複数居るが、どちらも同じ値になるはずなので先頭を見る)。
        var millerHousehold = first.Households.First(h => h.Occupation == Occupation.Miller);
        Assert.Equal(42, millerHousehold.WorkshopInventory[Item.Grain]);

        Assert.Equal(StateHasher.Compute(first), StateHasher.Compute(second));
    }

    /// <summary>
    /// 資金を配り忘れて0のままにする、または定義ではない定数を書く実装ミスを捕まえる。
    /// 初日から全世帯が GDD02b §4 の予算制約に張り付き、#34 の取引が一度も成立しない。
    /// </summary>
    [Fact]
    public void EveryHouseholdStartsWithTheDefinedLiquidFunds()
    {
        var definition = WorldDefinition.M0;
        var world = Generate(seed: 1, definition);

        Assert.All(
            world.Households,
            household => Assert.Equal(definition.InitialLiquidFunds, household.LiquidFunds));
    }

    /// <summary>
    /// 世帯在庫と工房在庫の取り違え / 0のまま放置 / 別の配列(取得原価など)を書き込む実装ミスを
    /// 捕まえる。GDD02 §9.2 の必需品の初期在庫が消えると、#34 の消費が初日に欠乏を起こす。
    /// </summary>
    [Fact]
    public void HouseholdInventoryIsSeededFromTheDefinition()
    {
        var definition = WorldDefinition.M0;
        var world = Generate(seed: 1, definition);

        foreach (var household in world.Households)
        {
            for (int itemId = 0; itemId < definition.ItemCount; itemId++)
            {
                Assert.Equal(
                    definition.InitialHouseholdInventory[itemId], household.HouseholdInventory[itemId]);
            }
        }
    }

    /// <summary>
    /// 階層の添字を取り違える(親方と徒弟の熟練度が入れ替わる)、または全員に同じ値を配る
    /// 実装ミスを捕まえる。検証しているのは、TDD01 §3.2 が「器として先に持つ」と決めた
    /// <c>SkillPermille</c> の、階層への配線が正しいことである。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>SkillPermille</c>(熟練度‰)と GDD02a §2 の労働力係数‰(親方1000 / 徒弟300、
    /// M0 の世帯合計1300‰固定)は別物である。</b>単位(‰)も形(階層別の表)も同型だが、
    /// 労働力係数は本タスクでは持たない(<c>WorldDefinition</c> に労働力係数の欄は無い) —
    /// 持つのは #34 である。GDD08 §9 は熟練度を M0 の対象外とし、§4.2 の生産性式は
    /// 「この式自体を使わない」としているので、<c>SkillPermille</c> を反転しても M0 の
    /// 労働力は一切動かない。#34 が <c>SkillPermille</c> を労働力係数に流用しないこと。
    /// </para>
    /// <para>
    /// <b>捕まえない範囲。</b>このテストが捕まえるのは <c>WorldGenerator</c> の配線だけで、
    /// <c>M0</c> の <c>{ 700, 400, 100 }</c> という値そのもの(親方 &gt; 徒弟 の向きを含む)は
    /// 固定していない。期待値も実装も同じ <c>definition.InitialSkillPermilleByRank</c> を
    /// 読むため、この表を転置しても緑のまま通る。<c>Assert.NotEqual</c> は「異なること」しか
    /// 見ておらず、向きは押さえていない。熟練度‰ には取得原価と違って上流の照合先が無く、
    /// 向きも要求されていないため、この穴は塞がずに残す(#28 の調整と衝突させないため)。
    /// </para>
    /// </remarks>
    [Fact]
    public void SkillPermilleFollowsTheNpcRank()
    {
        var definition = WorldDefinition.M0;
        var world = Generate(seed: 1, definition);

        int masterSkill = definition.InitialSkillPermilleByRank[(int)NpcRank.Master];
        int apprenticeSkill = definition.InitialSkillPermilleByRank[(int)NpcRank.Apprentice];

        // 上の初期値表がハッシュの回帰を鈍らせないために階層で別値を選んでいる前提
        // (両者が異なる値であること)が崩れていないかも、このテストが押さえる。
        Assert.NotEqual(masterSkill, apprenticeSkill);

        foreach (var npc in world.Npcs)
        {
            int expected = npc.Rank switch
            {
                NpcRank.Master => masterSkill,
                NpcRank.Apprentice => apprenticeSkill,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(npc), npc.Rank, "M0にはMaster/Apprenticeしか存在しない。"),
            };

            Assert.Equal(expected, npc.SkillPermille);
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
