using Visionary.Sim.Determinism;
using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="ConsumptionSystem"/>(GDD02b §1 / GDD02d §5、#34 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class ConsumptionSystemTests
{
    private const int Firewood = Item.Firewood;
    private const int Bread = Item.Bread;
    private const int Beer = Item.Beer;

    /// <summary>1品目だけ非0にした消費表を作る。添字 = (int)NpcRank。</summary>
    private static int[][] ConsumptionTable(int masterQty, int apprenticeQty, int itemId)
    {
        var master = new int[Item.Count];
        var journeyman = new int[Item.Count];
        var apprentice = new int[Item.Count];

        master[itemId] = masterQty;
        journeyman[itemId] = masterQty; // M0はJourneymanを使わないが欄は埋める(WorldDefinitionと同じ規律)
        apprentice[itemId] = apprenticeQty;

        return new[] { master, journeyman, apprentice };
    }

    private static WorldDefinition DefinitionFor(
        int masterQty, int apprenticeQty, int itemId, int[]? firewoodSeasonPermille = null)
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = 0, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 1);

        return EconomySystemTestFixtures.BuildDefinition(
            recipe,
            dailyConsumptionPerNpcByRank: ConsumptionTable(masterQty, apprenticeQty, itemId),
            firewoodConsumptionSeasonPermille: firewoodSeasonPermille);
    }

    /// <summary>テスト表 #14。1人あたりパン1の定義で、2人世帯の世帯在庫が1日に2減る。</summary>
    [Fact]
    public void ConsumptionScalesWithHouseholdSize()
    {
        var definition = DefinitionFor(masterQty: 1, apprenticeQty: 1, itemId: Bread);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Apprentice });
        world.Households[0].HouseholdInventory[Bread] = 10;

        EconomySystemTestFixtures.RunDays(world, new ConsumptionSystem(definition), days: 1);

        Assert.Equal(8, world.Households[0].HouseholdInventory[Bread]);
    }

    /// <summary>テスト表 #15。親方1・徒弟0のビールの定義で、親方+徒弟の世帯が1だけ減る。</summary>
    [Fact]
    public void ConsumptionDiffersByRank()
    {
        var definition = DefinitionFor(masterQty: 1, apprenticeQty: 0, itemId: Beer);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Apprentice });
        world.Households[0].HouseholdInventory[Beer] = 10;

        EconomySystemTestFixtures.RunDays(world, new ConsumptionSystem(definition), days: 1);

        Assert.Equal(9, world.Households[0].HouseholdInventory[Beer]);
    }

    /// <summary>
    /// 【核心】テスト表 #16。基礎2・係数{800,400,1000,2000}の定義で、夏の世帯消費が2、冬が8
    /// (親方+徒弟)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-15)。</b>季節を見ずに基礎量をそのまま使う変異
    /// (<c>requiredQuantity += baseQuantity;</c> のみに置き換え)を当てたところ、
    /// 夏の <c>Assert.Equal(100 - 2, ...)</c> が実際値96(基礎量2×2人=4がそのまま引かれた)
    /// で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void FirewoodConsumptionFollowsTheSeason()
    {
        var seasonPermille = new[] { 800, 400, 1000, 2000 }; // Spring, Summer, Autumn, Winter
        var definition = DefinitionFor(
            masterQty: 2, apprenticeQty: 2, itemId: Firewood, firewoodSeasonPermille: seasonPermille);

        // 夏(季節Idx1): エポック(Y1春1日)から30日進めると夏1日目。
        var summerWorld = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Apprentice });
        summerWorld.Households[0].HouseholdInventory[Firewood] = 100;
        EconomySystemTestFixtures.AdvanceClockOnly(summerWorld, ticks: 30 * 24);
        EconomySystemTestFixtures.RunDays(summerWorld, new ConsumptionSystem(definition), days: 1);
        Assert.Equal(100 - 2, summerWorld.Households[0].HouseholdInventory[Firewood]);

        // 冬(季節Idx3): 90日進めると冬1日目。
        var winterWorld = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Apprentice });
        winterWorld.Households[0].HouseholdInventory[Firewood] = 100;
        EconomySystemTestFixtures.AdvanceClockOnly(winterWorld, ticks: 90 * 24);
        EconomySystemTestFixtures.RunDays(winterWorld, new ConsumptionSystem(definition), days: 1);
        Assert.Equal(100 - 8, winterWorld.Households[0].HouseholdInventory[Firewood]);
    }

    /// <summary>
    /// 【核心】テスト表 #17。基礎量1・係数500‰・2人世帯 → 消費2(世帯合計に先に掛けると1)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-15)。</b>構成員ごとに <c>ApplyPermille</c> を適用する代わりに、
    /// 基礎量を先に合計してから1回だけ <c>ApplyPermille</c> を適用する変異
    /// (<c>ApplyPermille(2人分の合計1+1=2, 500‰)=1</c>)を当てたところ、
    /// <c>Assert.Equal(100 - 2, ...)</c> が実際値99(1しか引かれなかった)で失敗した(赤を確認)。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void SeasonCoefficientIsRoundedPerMember()
    {
        // エポックは春(季節Idx0)。
        var seasonPermille = new[] { 500, 1000, 1000, 1000 };
        var definition = DefinitionFor(
            masterQty: 1, apprenticeQty: 1, itemId: Firewood, firewoodSeasonPermille: seasonPermille);

        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Apprentice });
        world.Households[0].HouseholdInventory[Firewood] = 100;

        EconomySystemTestFixtures.RunDays(world, new ConsumptionSystem(definition), days: 1);

        Assert.Equal(100 - 2, world.Households[0].HouseholdInventory[Firewood]);
    }

    /// <summary>【核心】テスト表 #18。在庫1・必要2 → 在庫0。翌日も0のまま負にならない。</summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-15)。</b><c>Math.Min(requiredQuantity, ...HouseholdInventory)</c> を
    /// 外し必要量をそのまま引く変異(<c>consumedQuantity = requiredQuantity;</c>)を当てたところ、
    /// <c>Assert.Equal(0, ...Bread)</c> が実際値-1(在庫が負になった)で失敗した(赤を確認)。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ConsumptionStopsAtZeroAndDoesNotGoNegative()
    {
        var definition = DefinitionFor(masterQty: 2, apprenticeQty: 0, itemId: Bread);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].HouseholdInventory[Bread] = 1;

        var system = new ConsumptionSystem(definition);

        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(0, world.Households[0].HouseholdInventory[Bread]);

        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(0, world.Households[0].HouseholdInventory[Bread]);
    }

    /// <summary>テスト表 #19。在庫1・必要2 → UnmetConsumption[itemId] == 1。</summary>
    [Fact]
    public void UnmetConsumptionRecordsTheShortfall()
    {
        var definition = DefinitionFor(masterQty: 2, apprenticeQty: 0, itemId: Bread);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].HouseholdInventory[Bread] = 1;

        EconomySystemTestFixtures.RunDays(world, new ConsumptionSystem(definition), days: 1);

        Assert.Equal(1, world.Households[0].UnmetConsumption[Bread]);
    }

    /// <summary>テスト表 #20。前日に不足した世帯へ在庫を補充して1日進めると0に戻る。</summary>
    [Fact]
    public void UnmetConsumptionIsClearedWhenDemandIsMet()
    {
        var definition = DefinitionFor(masterQty: 2, apprenticeQty: 0, itemId: Bread);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].HouseholdInventory[Bread] = 1;

        var system = new ConsumptionSystem(definition);

        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(1, world.Households[0].UnmetConsumption[Bread]);

        world.Households[0].HouseholdInventory[Bread] = 10;
        EconomySystemTestFixtures.RunDays(world, system, days: 1);

        Assert.Equal(0, world.Households[0].UnmetConsumption[Bread]);
    }

    /// <summary>テスト表 #21。薪を世帯在庫と工房在庫の両方に置いて1日進め、工房在庫が動かない。</summary>
    [Fact]
    public void ConsumptionTouchesOnlyTheHouseholdInventory()
    {
        var definition = DefinitionFor(masterQty: 2, apprenticeQty: 0, itemId: Firewood);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].HouseholdInventory[Firewood] = 10;
        world.Households[0].WorkshopInventory[Firewood] = 10;

        EconomySystemTestFixtures.RunDays(world, new ConsumptionSystem(definition), days: 1);

        Assert.Equal(8, world.Households[0].HouseholdInventory[Firewood]);
        Assert.Equal(10, world.Households[0].WorkshopInventory[Firewood]);
    }

    /// <summary>
    /// テスト表 #22。パンの基礎量を1にしたまま冬(2000‰)に進め、パンの消費が変わらない。
    /// </summary>
    [Fact]
    public void SeasonalItemIsOnlyFirewood()
    {
        var seasonPermille = new[] { 1000, 1000, 1000, 2000 };
        var definition = DefinitionFor(
            masterQty: 1, apprenticeQty: 0, itemId: Bread, firewoodSeasonPermille: seasonPermille);

        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].HouseholdInventory[Bread] = 100;
        EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 90 * 24); // 冬1日目

        EconomySystemTestFixtures.RunDays(world, new ConsumptionSystem(definition), days: 1);

        Assert.Equal(100 - 1, world.Households[0].HouseholdInventory[Bread]); // 季節係数×2 なら 98 になる
    }

    /// <summary>テスト表 #23。#13と同じ形をConsumptionに当てる。</summary>
    [Fact]
    public void ConsumptionDrawsNoRandomNumbers()
    {
        var definition = DefinitionFor(masterQty: 2, apprenticeQty: 1, itemId: Bread);

        ulong RunWithSeed(long seed)
        {
            var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
                new[] { NpcRank.Master, NpcRank.Apprentice });
            world.Households[0].HouseholdInventory[Bread] = 1; // 不足も発生させ、UnmetConsumptionの経路も通す

            var scheduler = new SimScheduler(
                new ISimSystem[] { new ConsumptionSystem(definition) }, new RandomSource(seed));
            scheduler.Advance(world, ticks: 24);

            return StateHasher.Compute(world);
        }

        Assert.Equal(RunWithSeed(1), RunWithSeed(999999));
    }
}
