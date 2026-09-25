using System.Linq;
using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="SelfConsumption"/>(GDD02b §1.1、W2-17 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class SelfConsumptionTests
{
    /// <summary>
    /// 指定の職業・階層構成の世帯を1戸だけ持つ世界を作る(<c>itemCount = definition.ItemCount</c>)。
    /// </summary>
    /// <remarks>
    /// <see cref="EconomySystemTestFixtures.BuildWorldWithOneHousehold"/> は職業を
    /// <c>Occupation.Miller</c> に固定しているため使えない ── 本ファイルはパン屋・醸造・鍛冶・
    /// 製粉のいずれも必要とする。
    /// </remarks>
    private static (World World, HouseholdState Household) BuildHousehold(
        WorldDefinition definition, Occupation occupation, NpcRank[] ranks)
    {
        var world = new World(npcCount: ranks.Length, householdCount: 1, itemCount: definition.ItemCount);

        for (int i = 0; i < ranks.Length; i++)
        {
            world.Npcs[i].Rank = ranks[i];
        }

        var memberNpcIds = Enumerable.Range(0, ranks.Length).ToArray();

        var household = new HouseholdState(
            id: 0, districtId: 0, headNpcId: 0, memberNpcIds: memberNpcIds, itemCount: definition.ItemCount);
        household.Occupation = occupation;
        world.Households[0] = household;

        return (world, household);
    }

    /// <summary>M0のパン屋(親方+徒弟)の世帯を作る(「順序・境界」節の具体例と同じ構成)。</summary>
    private static (World World, HouseholdState Household) BuildM0Baker(WorldDefinition definition) =>
        BuildHousehold(definition, Occupation.Baker, new[] { NpcRank.Master, NpcRank.Apprentice });

    /// <summary>1品目だけ非0にした消費表を作る。添字 = (int)NpcRank。全階層に同じ値を入れる。</summary>
    private static int[][] ConsumptionTable(int itemId, int quantity)
    {
        int[] Row()
        {
            var row = new int[Item.Count];
            row[itemId] = quantity;
            return row;
        }

        return new[] { Row(), Row(), Row() };
    }

    /// <summary>1品目だけ非0にした目標日数表を作る。</summary>
    private static int[] TargetDays(int itemId, int days)
    {
        var array = new int[Item.Count];
        array[itemId] = days;
        return array;
    }

    /// <summary>
    /// テスト表 #1。合成 <see cref="WorldDefinition"/> で、M0の実在品目とは別の必需専用・嗜好専用の
    /// 品目を1つずつ持たせ、パン=3、ビール=1、小麦粉=0、工具=0(いずれも必需 xor 嗜好 xor
    /// どちらでもない)を確かめる。
    /// </summary>
    /// <remarks>
    /// <b>仕様に書かれた「必需3日・嗜好2日を両方持つ品目」は構成できない(止まって報告する対象)。</b>
    /// <see cref="WorldDefinition"/> のコンストラクタは「品目を必需と嗜好の両方には置けない
    /// (GDD02b §2、用途は排他)」を <see cref="ArgumentException"/> で強制している
    /// (<c>WorldDefinition.cs:430</c> 付近)。<see cref="SelfConsumption.TargetStockDays"/> が
    /// 2つの配列を足す実装自体はタスク仕様どおりだが、<b>その「足す」が実際に2項とも正になる
    /// 入力は、公開APIからは一度も作れない。</b>このため「合成 WorldDefinition で必需3日・嗜好2日の
    /// 品目の目標日数が5になる」という仕様の断定は削り、必需 xor 嗜好(またはどちらでもない)の
    /// 4通りだけを確かめる。
    /// </remarks>
    [Fact]
    public void TargetStockDaysAddsNecessityAndPreference()
    {
        var necessityDays = new int[Item.Count];
        necessityDays[Item.Bread] = 3;

        var preferenceDays = new int[Item.Count];
        preferenceDays[Item.Beer] = 1;

        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = Item.Grain, Quantity = 1 } },
            laborPermille: 1000);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe,
            necessityTargetStockDays: necessityDays,
            preferenceTargetStockDays: preferenceDays);

        Assert.Equal(3, SelfConsumption.TargetStockDays(definition, Item.Bread));
        Assert.Equal(1, SelfConsumption.TargetStockDays(definition, Item.Beer));
        Assert.Equal(0, SelfConsumption.TargetStockDays(definition, Item.Flour));
        Assert.Equal(0, SelfConsumption.TargetStockDays(definition, Item.Tools));
    }

    /// <summary>
    /// 【核心】テスト表 #2。M0・春・パン屋。<see cref="ConsumptionSystem"/> を1日走らせた後の
    /// 世帯在庫[パン]が目標在庫6ちょうど、工房在庫が移動量ぶん減っている(タスク仕様「順序・境界」
    /// 節の具体例そのもの)。
    /// </summary>
    [Fact]
    public void TransferFillsTheTargetStockMeasuredAfterTodaysConsumption()
    {
        var definition = WorldDefinition.M0;
        var (world, household) = BuildM0Baker(definition);

        // エポック(春)の初期世帯在庫[パン] = 6(M0校正値)。工房在庫は順1(生産)の後の状態
        // (6実行 × 出力2 = 12)を直接与える ── 本テストはConsumptionSystem単体の検査であり、
        // ProductionSystemは走らせない。
        household.HouseholdInventory[Item.Bread] = 6;
        household.WorkshopInventory[Item.Bread] = 12;

        EconomySystemTestFixtures.RunDays(world, new ConsumptionSystem(definition), days: 1);

        Assert.Equal(6, household.HouseholdInventory[Item.Bread]);
        Assert.Equal(10, household.WorkshopInventory[Item.Bread]);
    }

    /// <summary>
    /// テスト表 #3。同じ世帯を5日走らせ、各日の消費後の世帯在庫[パン]が毎日6で、単調増加しない。
    /// </summary>
    [Fact]
    public void TransferDoesNotAccumulateAcrossDays()
    {
        var definition = WorldDefinition.M0;
        var (world, household) = BuildM0Baker(definition);

        household.HouseholdInventory[Item.Bread] = 6;
        household.WorkshopInventory[Item.Bread] = 20; // 5日分(1日2個の移動)を賄うだけの工房在庫。

        var scheduler = new SimScheduler(
            new ISimSystem[] { new ConsumptionSystem(definition) }, new RandomSource(1));

        for (int day = 1; day <= 5; day++)
        {
            scheduler.Advance(world, ticks: 24);

            Assert.Equal(6, household.HouseholdInventory[Item.Bread]);
        }
    }

    /// <summary>
    /// 【核心】テスト表 #4。合成レシピ(出力品目が自分の入力でもある)で、留保量1回分が工房在庫に
    /// 残る。あわせて M0 で工房在庫が目標に届かない日は、あるだけ移る(タスク仕様「順序・境界」節、
    /// 「工房在庫が足りない日」の具体例)。
    /// </summary>
    [Fact]
    public void TransferNeverExceedsTheSellableStock()
    {
        // 合成レシピ(SellableStockTests.ReserveOfAnOutputThatIsAlsoItsOwnInputIsTheRecipeQuantity
        // と同じ形)。パン5個を自分の入力にも持つ ── 留保量5。
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = 1 } },
            inputs: new[]
            {
                new ItemQuantity { ItemId = Item.Bread, Quantity = 5 },
                new ItemQuantity { ItemId = Item.Grain, Quantity = 1 },
            },
            laborPermille: 1000);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe,
            dailyConsumptionPerNpcByRank: ConsumptionTable(Item.Bread, quantity: 100), // 要求量を確実に大きくする
            necessityTargetStockDays: TargetDays(Item.Bread, days: 3));

        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Bread] = 10;

        EconomySystemTestFixtures.RunDays(world, new ConsumptionSystem(definition), days: 1);

        // 留保量(5)ぶんが工房在庫に残る。要求量(目標300+当日100=400)は販売在庫(5)を大きく上回る。
        Assert.Equal(5, world.Households[0].WorkshopInventory[Item.Bread]);

        // M0: 入力切れで工房在庫が目標に届かない日は、あるだけ移る。
        var m0Definition = WorldDefinition.M0;
        var (m0World, m0Household) = BuildM0Baker(m0Definition);
        m0Household.HouseholdInventory[Item.Bread] = 0;
        m0Household.WorkshopInventory[Item.Bread] = 1;

        EconomySystemTestFixtures.RunDays(m0World, new ConsumptionSystem(m0Definition), days: 1);

        // 移動量 = min(1, max(0, 6+2-0)) = 1(販売在庫で頭打ち)。消費 = min(2,1) = 1 → 世帯在庫0。
        Assert.Equal(0, m0Household.WorkshopInventory[Item.Bread]);
        Assert.Equal(0, m0Household.HouseholdInventory[Item.Bread]);
        Assert.Equal(1, m0Household.UnmetConsumption[Item.Bread]);
    }

    /// <summary>
    /// 【核心】テスト表 #5。M0・春・パン屋。1日走らせた後に <see cref="BuyerDemand.Build"/> を呼び、
    /// パンの必需の行が <c>ExpectedStock == TargetStock</c> かつ <c>StockPressurePermille == 1000</c>。
    /// </summary>
    /// <remarks>
    /// <b>本テストが守っているのは「水準の決定」であって「自家消費があること」ではない。</b>自家消費が
    /// 無くても水準の項が全部揃っていれば #2 は緑にできるが、本テストは「段4の予想在庫が目標在庫に
    /// 一致する」という、この水準を選んだ理由そのもの(GDD02b §1.1)を見ている。
    /// </remarks>
    [Fact]
    public void SelfSuppliedHouseholdDoesNotDemandItsOwnOutput()
    {
        var definition = WorldDefinition.M0;
        var (world, household) = BuildM0Baker(definition);

        household.HouseholdInventory[Item.Bread] = 6;
        household.WorkshopInventory[Item.Bread] = 12;

        EconomySystemTestFixtures.RunDays(world, new ConsumptionSystem(definition), days: 1);

        var demand = new BuyerDemand(definition).Build(
            world, household, hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);

        var breadNecessity = demand.Lines.Single(
            line => line.Purpose == DemandPurpose.Necessity && line.ItemId == Item.Bread);

        Assert.Equal(breadNecessity.TargetStock, breadNecessity.ExpectedStock);
        Assert.Equal(1000, breadNecessity.StockPressurePermille);
    }

    /// <summary>
    /// テスト表 #6。M0・パン屋。1日走らせた後、工房在庫[薪](生産の入力)が自家消費で減っていない。
    /// 醸造の工房在庫[穀物]・[薪]も同様。
    /// </summary>
    [Fact]
    public void TransferMovesOnlyTheHouseholdsOwnOutputs()
    {
        var definition = WorldDefinition.M0;

        var (bakerWorld, baker) = BuildM0Baker(definition);
        baker.WorkshopInventory[Item.Firewood] = 5; // 生産の入力として抱えている工房在庫の薪。

        EconomySystemTestFixtures.RunDays(bakerWorld, new ConsumptionSystem(definition), days: 1);

        Assert.Equal(5, baker.WorkshopInventory[Item.Firewood]);

        var (brewerWorld, brewer) = BuildHousehold(definition, Occupation.Brewer, new[] { NpcRank.Master });
        brewer.WorkshopInventory[Item.Grain] = 4;
        brewer.WorkshopInventory[Item.Firewood] = 3;

        EconomySystemTestFixtures.RunDays(brewerWorld, new ConsumptionSystem(definition), days: 1);

        Assert.Equal(4, brewer.WorkshopInventory[Item.Grain]);
        Assert.Equal(3, brewer.WorkshopInventory[Item.Firewood]);
    }

    /// <summary>
    /// テスト表 #8。M0・1日走行の前後(順2の直前直後)で、全世帯の <c>LiquidFunds</c> の合計と
    /// <c>world.Ledgers</c> の行数が変わらない。
    /// </summary>
    [Fact]
    public void SelfConsumptionMovesNoMoneyAndWritesNoLedgerEntry()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));

        // 自家消費が実際に動く母数を作るため、先に生産だけ1日走らせて工房在庫へ出力を積む(順1)。
        EconomySystemTestFixtures.RunDays(world, new ProductionSystem(definition), days: 1);

        bool anyOwnOutputStock = world.Households.Any(household =>
            definition.Recipes[(int)household.Occupation].Outputs.Any(
                output => household.WorkshopInventory[output.ItemId] > 0));
        Assert.True(anyOwnOutputStock, "生産後も工房在庫の出力品目が0のまま(値の問題の可能性)。");

        long fundsBefore = world.Households.Sum(household => (long)household.LiquidFunds);
        int ledgerRowsBefore = world.Ledgers.Sum(rows => rows.Count);

        EconomySystemTestFixtures.RunDays(world, new ConsumptionSystem(definition), days: 1);

        long fundsAfter = world.Households.Sum(household => (long)household.LiquidFunds);
        int ledgerRowsAfter = world.Ledgers.Sum(rows => rows.Count);

        Assert.Equal(fundsBefore, fundsAfter);
        Assert.Equal(ledgerRowsBefore, ledgerRowsAfter);
    }

    /// <summary>
    /// テスト表 #9。M0・製粉(小麦粉)と鍛冶(工具)。30日走っても世帯在庫[小麦粉]と世帯在庫[工具]が
    /// 0のまま、鍛冶の工房在庫[工具]が自家消費で減らない。
    /// </summary>
    [Fact]
    public void NonConsumedOutputsAreNeverMoved()
    {
        var definition = WorldDefinition.M0;

        var (millerWorld, miller) = BuildHousehold(definition, Occupation.Miller, new[] { NpcRank.Master });
        miller.WorkshopInventory[Item.Flour] = 100;

        var (smithWorld, smith) = BuildHousehold(definition, Occupation.Smith, new[] { NpcRank.Master });
        smith.WorkshopInventory[Item.Tools] = 100;

        EconomySystemTestFixtures.RunDays(millerWorld, new ConsumptionSystem(definition), days: 30);
        EconomySystemTestFixtures.RunDays(smithWorld, new ConsumptionSystem(definition), days: 30);

        Assert.Equal(0, miller.HouseholdInventory[Item.Flour]);
        Assert.Equal(100, miller.WorkshopInventory[Item.Flour]);

        Assert.Equal(0, smith.HouseholdInventory[Item.Tools]);
        Assert.Equal(100, smith.WorkshopInventory[Item.Tools]); // 鍛冶の工房在庫[工具]が自家消費で減らない。
    }
}
