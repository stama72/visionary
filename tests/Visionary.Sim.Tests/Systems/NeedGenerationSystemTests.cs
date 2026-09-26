using System.Linq;
using Visionary.Sim.Determinism;
using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="NeedGenerationSystem"/>(GDD02b §8.1、W2-19 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class NeedGenerationSystemTests
{
    // レシピの入力・出力に使う品目Id。Item.Tools(8)と衝突しない範囲で選ぶ
    // (ProductionSystemTestsと同じ流儀)。
    private const int InputA = 0;
    private const int InputB = 1;
    private const int Output = 2;
    private const int NecessityItem = 3;
    private const int PreferenceItem = 4;

    /// <summary>入力0件・所要労働1‰(生産能力を実質無限にし、理由1・3を黙って起こさない)。</summary>
    private static Recipe QuietRecipe() =>
        new(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 1);

    private static WorldDefinition BuildDefinition(
        Recipe recipe,
        int[]? necessityTargetStockDays = null,
        int[]? preferenceTargetStockDays = null,
        int equipmentPermilleWithoutTools = 0,
        int[]? laborPermilleByRank = null) =>
        EconomySystemTestFixtures.BuildDefinition(
            recipe,
            laborPermilleByRank: laborPermilleByRank ?? new[] { 1000, 800, 300 },
            necessityTargetStockDays: necessityTargetStockDays ?? new int[Item.Count],
            preferenceTargetStockDays: preferenceTargetStockDays ?? new int[Item.Count],
            equipmentPermilleWithoutTools: equipmentPermilleWithoutTools);

    /// <summary>
    /// 世帯1戸の世界。工具在庫は既定1(理由4を黙って起こさない)。当日の生産能力は既定1
    /// (#237。順1を回さないテストでは <see cref="HouseholdState.ProductionCapacityRuns"/> が
    /// 既定0のままだと理由3(増産できない)を黙って起こす ── ここで1を既定にし、理由3を対象と
    /// するテスト(#8・#9・#237の新設テスト)は呼び出し側が明示的に上書きする)。
    /// </summary>
    private static World BuildWorld(NpcRank[] memberRanks, int toolStock = 1)
    {
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(memberRanks);
        world.Households[0].WorkshopInventory[Item.Tools] = toolStock;
        world.Households[0].ProductionCapacityRuns = 1;

        return world;
    }

    /// <summary>
    /// テスト表 #1。生産量0・入力2件のうち1件が不足 → その入力にだけ Need が1件、
    /// 数量 = 必要数量 − 在庫。
    /// </summary>
    [Fact]
    public void ProductionStoppedNeedRisesForEachShortInput()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: new[]
            {
                new ItemQuantity { ItemId = InputA, Quantity = 3 },
                new ItemQuantity { ItemId = InputB, Quantity = 2 },
            },
            laborPermille: 1);

        var definition = BuildDefinition(recipe);
        var world = BuildWorld(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[InputA] = 1; // 3必要・1しかない → 不足2
        world.Households[0].WorkshopInventory[InputB] = 5; // 2必要・充足

        EconomySystemTestFixtures.RunDays(world, new NeedGenerationSystem(definition), days: 1);

        var need = Assert.Single(world.Needs, n => n.ReasonCode == NeedReason.ProductionStopped);
        Assert.Equal(InputA, need.ItemId);
        Assert.Equal(2, need.Quantity);
        Assert.Equal(NeedType.StockShortage, need.TypeCode);
    }

    /// <summary>テスト表 #2。生産量0だが入力は足りている(能力0)→ 生産停止は立たない。</summary>
    [Fact]
    public void ProductionStoppedNeedDoesNotRiseWhenInputsSuffice()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = InputA, Quantity = 3 } },
            laborPermille: 1);

        var definition = BuildDefinition(recipe);
        var world = BuildWorld(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[InputA] = 100; // 充足(ProductionRunsは既定0のまま)

        EconomySystemTestFixtures.RunDays(world, new NeedGenerationSystem(definition), days: 1);

        Assert.DoesNotContain(world.Needs, n => n.ReasonCode == NeedReason.ProductionStopped);
    }

    /// <summary>
    /// テスト表 #3。不足する入力が2件で、<c>r.Inputs</c> の並びが品目Id降順のとき、
    /// <c>Needs</c> の並びは品目Id昇順(<c>Recipe</c> は入力の並びを保証しない)。
    /// </summary>
    [Fact]
    public void ProductionStoppedNeedsAreOrderedByItemId()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: new[]
            {
                new ItemQuantity { ItemId = InputB, Quantity = 2 }, // itemId=1(降順で宣言)
                new ItemQuantity { ItemId = InputA, Quantity = 2 }, // itemId=0
            },
            laborPermille: 1);

        var definition = BuildDefinition(recipe);
        var world = BuildWorld(new[] { NpcRank.Master });
        // 両方とも不足させる(在庫0)。

        EconomySystemTestFixtures.RunDays(world, new NeedGenerationSystem(definition), days: 1);

        var productionStoppedNeeds = world.Needs
            .Where(n => n.ReasonCode == NeedReason.ProductionStopped)
            .ToArray();

        Assert.Equal(2, productionStoppedNeeds.Length);
        Assert.Equal(InputA, productionStoppedNeeds[0].ItemId);
        Assert.Equal(InputB, productionStoppedNeeds[1].ItemId);
    }

    /// <summary>テスト表 #4。フラグ1・必需の消費不足3 → 困窮が1件、数量3。</summary>
    [Fact]
    public void DistressNeedUsesUnmetConsumptionAsQuantity()
    {
        var necessityTargetStockDays = new int[Item.Count];
        necessityTargetStockDays[NecessityItem] = 1;

        var definition = BuildDefinition(QuietRecipe(), necessityTargetStockDays: necessityTargetStockDays);
        var world = BuildWorld(new[] { NpcRank.Master });
        world.Households[0].IsBankrupt = 1;
        world.Households[0].UnmetConsumption[NecessityItem] = 3;

        EconomySystemTestFixtures.RunDays(world, new NeedGenerationSystem(definition), days: 1);

        var need = Assert.Single(world.Needs, n => n.ReasonCode == NeedReason.Distress);
        Assert.Equal(NecessityItem, need.ItemId);
        Assert.Equal(3, need.Quantity);
        Assert.Equal(NeedType.MoneyShortage, need.TypeCode);
    }

    /// <summary>
    /// 【核心】テスト表 #5。フラグ1・消費不足0(在庫は満ちている)→ 困窮は立たない。
    /// GDD02b §3.3「フラグが見ているのは現金であって在庫ではない」を落とすと立ってしまう。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(実測日 2026-09-23、<c>mutator</c> が使い捨てworktree(<c>.pipeline/mutation/40</c>)
    /// で対象コミット <c>7cb44e6</c> に当てた。ベースライン532件全緑)。</b>
    /// <c>NeedGenerationSystem.CollectDistress</c> の <c>unmet &lt;= 0</c> の継続チェックを削除する
    /// 変異は<b>赤</b>(本テスト1件のみ落ちた。期待どおり)。
    /// </remarks>
    [Fact]
    public void DistressNeedDoesNotRiseWithoutUnmetConsumption()
    {
        var necessityTargetStockDays = new int[Item.Count];
        necessityTargetStockDays[NecessityItem] = 1;

        var definition = BuildDefinition(QuietRecipe(), necessityTargetStockDays: necessityTargetStockDays);
        var world = BuildWorld(new[] { NpcRank.Master });
        world.Households[0].IsBankrupt = 1;
        world.Households[0].UnmetConsumption[NecessityItem] = 0;

        EconomySystemTestFixtures.RunDays(world, new NeedGenerationSystem(definition), days: 1);

        Assert.DoesNotContain(world.Needs, n => n.ReasonCode == NeedReason.Distress);
    }

    /// <summary>テスト表 #6。フラグ0・消費不足3 → 困窮は立たない(遠方在庫の側が拾う)。</summary>
    [Fact]
    public void DistressNeedDoesNotRiseWhenSolvent()
    {
        var necessityTargetStockDays = new int[Item.Count];
        necessityTargetStockDays[NecessityItem] = 1;

        var definition = BuildDefinition(QuietRecipe(), necessityTargetStockDays: necessityTargetStockDays);
        var world = BuildWorld(new[] { NpcRank.Master });
        world.Households[0].IsBankrupt = 0;
        world.Households[0].UnmetConsumption[NecessityItem] = 3;

        EconomySystemTestFixtures.RunDays(world, new NeedGenerationSystem(definition), days: 1);

        Assert.DoesNotContain(world.Needs, n => n.ReasonCode == NeedReason.Distress);
    }

    /// <summary>テスト表 #7。フラグ1・嗜好品の消費不足3 → 困窮は立たない。</summary>
    [Fact]
    public void DistressNeedIgnoresPreferenceItems()
    {
        var preferenceTargetStockDays = new int[Item.Count];
        preferenceTargetStockDays[PreferenceItem] = 1;

        var definition = BuildDefinition(QuietRecipe(), preferenceTargetStockDays: preferenceTargetStockDays);
        var world = BuildWorld(new[] { NpcRank.Master });
        world.Households[0].IsBankrupt = 1;
        world.Households[0].UnmetConsumption[PreferenceItem] = 3;

        EconomySystemTestFixtures.RunDays(world, new NeedGenerationSystem(definition), days: 1);

        Assert.DoesNotContain(world.Needs, n => n.ReasonCode == NeedReason.Distress);
    }

    /// <summary>
    /// テスト表 #8(W2-24で書き換え)。<see cref="NeedGenerationSystem"/> は順1
    /// (<see cref="ProductionSystem"/>)が書いた「当日の生産能力」と進捗‰を読むだけで、
    /// 労働力・設備係数を自分で計算し直さない(#237)。能力0・進捗650・所要労働1000 →
    /// 労働力不足が1件、品目 = 出力品目、数量 = 1000 − 650 = 350。
    /// </summary>
    [Fact]
    public void LaborShortageNeedRisesWhenCapacityIsZero()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 1000);

        var definition = BuildDefinition(recipe);
        var world = BuildWorld(new[] { NpcRank.Master });
        world.Households[0].ProductionCapacityRuns = 0;
        world.Households[0].ProductionProgressPermille = 650;

        EconomySystemTestFixtures.RunDays(world, new NeedGenerationSystem(definition), days: 1);

        var need = Assert.Single(world.Needs, n => n.ReasonCode == NeedReason.CannotExpandProduction);
        Assert.Equal(Output, need.ItemId);
        Assert.Equal(350, need.Quantity); // 1000 - 650
        Assert.Equal(NeedType.LaborShortage, need.TypeCode);
    }

    /// <summary>
    /// テスト表 #9(W2-24で書き換え)。能力1(0でない)→ 進捗‰の値に関わらず立たない。
    /// </summary>
    [Fact]
    public void LaborShortageNeedDoesNotRiseAtTheBoundary()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 1000);

        var definition = BuildDefinition(recipe);
        var world = BuildWorld(new[] { NpcRank.Master });
        world.Households[0].ProductionCapacityRuns = 1;
        world.Households[0].ProductionProgressPermille = 0; // 能力さえ非0ならこの欄は見ない。

        EconomySystemTestFixtures.RunDays(world, new NeedGenerationSystem(definition), days: 1);

        Assert.DoesNotContain(world.Needs, n => n.ReasonCode == NeedReason.CannotExpandProduction);
    }

    /// <summary>
    /// 【核心】テスト表 #6(#237)。<c>SimScheduler</c> に <see cref="ProductionSystem"/> →
    /// <see cref="NeedGenerationSystem"/> の順で登録し、「鍛冶」表(タスク仕様「順序・境界の
    /// 具体例」)の日0〜1を回す(各日の前に損失を置く)。日0の後に増産できないが1件・数量150、
    /// 日1の後に0件(持ち越した1150は1300未満だが、能力は1)。
    /// </summary>
    /// <remarks>
    /// M3(順4の条件を <c>household.ProductionProgressPermille &lt; recipe.LaborPermille</c> に
    /// する)は、日1も持ち越し1150で立ってしまう(能力1で実際は満たされているのに、進捗‰だけを
    /// 見ると所要労働‰未満に見える)。
    /// </remarks>
    [Fact]
    public void CannotExpandProductionReadsTheRecordedCapacityNotTheCarriedProgress()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 1300);

        var definition = BuildDefinition(recipe);
        var world = BuildWorld(new[] { NpcRank.Master, NpcRank.Apprentice });

        var scheduler = new SimScheduler(
            new ISimSystem[] { new ProductionSystem(definition), new NeedGenerationSystem(definition) },
            new RandomSource(1));

        // 日0。前日の損失150。
        world.Households[0].ErrandLaborLossPermille = 150;
        scheduler.Advance(world, ticks: 24);

        var need = Assert.Single(world.Needs, n => n.ReasonCode == NeedReason.CannotExpandProduction);
        Assert.Equal(Output, need.ItemId);
        Assert.Equal(150, need.Quantity); // 1300 - 1150

        // 日1。前日の損失0。持ち越し1150は1300未満だが能力は1 → 立たない。
        world.Households[0].ErrandLaborLossPermille = 0;
        scheduler.Advance(world, ticks: 24);

        Assert.DoesNotContain(world.Needs, n => n.ReasonCode == NeedReason.CannotExpandProduction);
    }

    /// <summary>テスト表 #10。工具在庫0 → 工具切れが1件・数量1。在庫1 → 立たない。</summary>
    [Fact]
    public void ToolsExhaustedNeedRisesOnlyWhenToolStockIsZero()
    {
        var definition = BuildDefinition(QuietRecipe());

        var worldZero = BuildWorld(new[] { NpcRank.Master }, toolStock: 0);
        EconomySystemTestFixtures.RunDays(worldZero, new NeedGenerationSystem(definition), days: 1);

        var need = Assert.Single(worldZero.Needs, n => n.ReasonCode == NeedReason.ToolsExhausted);
        Assert.Equal(Item.Tools, need.ItemId);
        Assert.Equal(1, need.Quantity);
        Assert.Equal(NeedType.StockShortage, need.TypeCode);

        var worldOne = BuildWorld(new[] { NpcRank.Master }, toolStock: 1);
        EconomySystemTestFixtures.RunDays(worldOne, new NeedGenerationSystem(definition), days: 1);

        Assert.DoesNotContain(worldOne.Needs, n => n.ReasonCode == NeedReason.ToolsExhausted);
    }

    /// <summary>テスト表 #11。UnfilledPurchase[i]=4 → 遠方在庫が1件、数量4。0の品目には立たない。</summary>
    [Fact]
    public void DistantStockNeedMirrorsUnfilledPurchase()
    {
        var definition = BuildDefinition(QuietRecipe());
        var world = BuildWorld(new[] { NpcRank.Master });
        world.Households[0].UnfilledPurchase[NecessityItem] = 4;
        // PreferenceItemは0のまま(立たないことも併せて確かめる)。

        EconomySystemTestFixtures.RunDays(world, new NeedGenerationSystem(definition), days: 1);

        var distantStockNeeds = world.Needs.Where(n => n.ReasonCode == NeedReason.DistantStock).ToArray();
        var need = Assert.Single(distantStockNeeds);
        Assert.Equal(NecessityItem, need.ItemId);
        Assert.Equal(4, need.Quantity);
        Assert.Equal(NeedType.StockShortage, need.TypeCode);
    }

    /// <summary>
    /// 別表 #32(#40訂正。レビュー3巡目 網羅パス)。工具の <c>UnfilledPurchase</c> が正の世界で、
    /// 遠方在庫 Need の <c>Quantity</c> が個数のまま(<c>ToolDurabilityPerUnit</c> を掛け直されて
    /// いない)。
    /// </summary>
    /// <remarks>
    /// 順5(<see cref="TradeSystem"/>)が個で書き、順4(本クラス)が個で読む、という単位の一貫性を
    /// 端から端まで見るテストが無かった(#11 は必需品なので耐久を踏まない)。理由5の実装は
    /// <c>household.UnfilledPurchase[i]</c> をそのまま <c>Need.Quantity</c> に映すだけで、
    /// 単位変換を挟まない(GDD02b §8.1)。
    /// </remarks>
    [Fact]
    public void DistantStockNeedQuantityForToolsIsInUnits()
    {
        var definition = BuildDefinition(QuietRecipe());
        var world = BuildWorld(new[] { NpcRank.Master }); // toolStock既定1(理由4を黙って起こさない)
        world.Households[0].UnfilledPurchase[Item.Tools] = 3; // 個数(耐久値ではない)

        EconomySystemTestFixtures.RunDays(world, new NeedGenerationSystem(definition), days: 1);

        var need = Assert.Single(
            world.Needs, n => n.ReasonCode == NeedReason.DistantStock && n.ItemId == Item.Tools);

        Assert.Equal(3, need.Quantity);
        Assert.True(
            need.Quantity < definition.ToolDurabilityPerUnit,
            $"耐久値へ掛け直されている疑い(Quantity={need.Quantity})。");
    }

    /// <summary>
    /// 【核心】テスト表 #12。1日目に立った Need が、条件を消した2日目に <c>world.Needs</c> から消える。
    /// </summary>
    /// <remarks>
    /// <b>核心。変異: <c>world.Needs.Clear()</c> を消して <c>AddRange</c> だけにする / 期待 赤。</b>
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(実測日 2026-09-23、<c>mutator</c> が使い捨てworktree(<c>.pipeline/mutation/40</c>)
    /// で対象コミット <c>7cb44e6</c> に当てた。ベースライン532件全緑)。</b>
    /// <c>NeedGenerationSystem.Step</c> の <c>world.Needs.Clear()</c> を削除する変異は<b>赤</b>
    /// (期待どおり)。本テストに加え、巻き添えで3件が落ちた:
    /// <see cref="Visionary.Sim.Tests.Determinism.StateHasherTests.HashChangesWhenNextNeedIdChanges"/> /
    /// <see cref="NeedIdIsStableWhileTheConditionHolds"/> / <see cref="ExpiredNeedIdIsNotReused"/>
    /// (計4件)。
    /// </remarks>
    [Fact]
    public void NeedsExpireWhenTheConditionIsGone()
    {
        var definition = BuildDefinition(QuietRecipe());
        var world = BuildWorld(new[] { NpcRank.Master }, toolStock: 0);
        var system = new NeedGenerationSystem(definition);

        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Contains(world.Needs, n => n.ReasonCode == NeedReason.ToolsExhausted);

        world.Households[0].WorkshopInventory[Item.Tools] = 1; // 条件を消す
        EconomySystemTestFixtures.RunDays(world, system, days: 1);

        Assert.DoesNotContain(world.Needs, n => n.ReasonCode == NeedReason.ToolsExhausted);
    }

    /// <summary>
    /// テスト表 #13。2日連続で同じ (世帯, 理由, 品目) の条件が立つと Id が同じ。手前の Need
    /// (理由4)が1件失効して並びが詰まっても、後ろの Need(理由5)の Id は変わらない。
    /// </summary>
    [Fact]
    public void NeedIdIsStableWhileTheConditionHolds()
    {
        var definition = BuildDefinition(QuietRecipe());
        var world = BuildWorld(new[] { NpcRank.Master }, toolStock: 0); // 理由4(手前)
        world.Households[0].UnfilledPurchase[NecessityItem] = 4; // 理由5(後ろ)
        var system = new NeedGenerationSystem(definition);

        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        int day1DistantId = Assert.Single(world.Needs, n => n.ReasonCode == NeedReason.DistantStock).Id;

        // 2日目。手前(理由4、工具切れ)が失効し、並びが詰まる。
        world.Households[0].WorkshopInventory[Item.Tools] = 1;
        EconomySystemTestFixtures.RunDays(world, system, days: 1);

        int day2DistantId = Assert.Single(world.Needs, n => n.ReasonCode == NeedReason.DistantStock).Id;
        Assert.Equal(day1DistantId, day2DistantId);
    }

    /// <summary>テスト表 #14。立つ→失効→また立つ、で3回目のIdが1回目と違う。NextNeedIdは単調増加。</summary>
    [Fact]
    public void ExpiredNeedIdIsNotReused()
    {
        var definition = BuildDefinition(QuietRecipe());
        var world = BuildWorld(new[] { NpcRank.Master }, toolStock: 0);
        var system = new NeedGenerationSystem(definition);

        EconomySystemTestFixtures.RunDays(world, system, days: 1); // 1日目。立つ。
        int firstId = Assert.Single(world.Needs, n => n.ReasonCode == NeedReason.ToolsExhausted).Id;

        world.Households[0].WorkshopInventory[Item.Tools] = 1; // 2日目。失効。
        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.DoesNotContain(world.Needs, n => n.ReasonCode == NeedReason.ToolsExhausted);

        world.Households[0].WorkshopInventory[Item.Tools] = 0; // 3日目。また立つ。
        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        int thirdId = Assert.Single(world.Needs, n => n.ReasonCode == NeedReason.ToolsExhausted).Id;

        Assert.NotEqual(firstId, thirdId);
        Assert.True(thirdId > firstId, $"Idが単調増加していない(1回目={firstId}, 3回目={thirdId})。");
    }

    /// <summary>
    /// テスト表 #15。3世帯・複数理由が同時に立つ世界で、並びが (世帯Id, 理由コード, 品目Id) の昇順。
    /// </summary>
    [Fact]
    public void NeedsAreOrderedByHouseholdThenReasonThenItem()
    {
        var necessityTargetStockDays = new int[Item.Count];
        necessityTargetStockDays[NecessityItem] = 1;

        var definition = BuildDefinition(
            QuietRecipe(), necessityTargetStockDays: necessityTargetStockDays);
        var world = EconomySystemTestFixtures.BuildWorldWithHouseholds(3);

        // 順1(ProductionSystem)を回さないので、当日の生産能力は既定0のまま
        // (#237、労働力不足=理由3が黙って起こる)。3世帯とも1にして理由3をこのテストの対象外にする。
        world.Households[0].ProductionCapacityRuns = 1;
        world.Households[1].ProductionCapacityRuns = 1;
        world.Households[2].ProductionCapacityRuns = 1;

        // 世帯0: 工具切れ(理由4) + 遠方在庫(理由5、品目NecessityItem)。
        world.Households[0].WorkshopInventory[Item.Tools] = 0;
        world.Households[0].UnfilledPurchase[NecessityItem] = 1;

        // 世帯1: 困窮(理由2) + 工具切れ(理由4)。
        world.Households[1].IsBankrupt = 1;
        world.Households[1].UnmetConsumption[NecessityItem] = 2;
        world.Households[1].WorkshopInventory[Item.Tools] = 0;

        // 世帯2: 遠方在庫(理由5)が2品目。降順で書き込み、昇順に並ぶことを確かめる。工具は満杯。
        world.Households[2].WorkshopInventory[Item.Tools] = 1;
        world.Households[2].UnfilledPurchase[6] = 1;
        world.Households[2].UnfilledPurchase[InputB] = 1; // itemId=1

        EconomySystemTestFixtures.RunDays(world, new NeedGenerationSystem(definition), days: 1);

        var actual = world.Needs
            .Select(n => (n.TargetHouseholdId, n.ReasonCode, n.ItemId))
            .ToArray();

        var expected = new[]
        {
            (0, NeedReason.ToolsExhausted, Item.Tools),
            (0, NeedReason.DistantStock, NecessityItem),
            (1, NeedReason.Distress, NecessityItem),
            (1, NeedReason.ToolsExhausted, Item.Tools),
            (2, NeedReason.DistantStock, InputB),
            (2, NeedReason.DistantStock, 6),
        };

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// テスト表 #16。<c>NeedReason</c> の全要素について <c>Need.TypeOf</c> が GDD02b §8 の表と一致し、
    /// 未知の値で <c>ArgumentOutOfRangeException</c>。
    /// </summary>
    [Fact]
    public void NeedTypeMatchesReasonForEveryReason()
    {
        Assert.Equal(NeedType.StockShortage, Need.TypeOf(NeedReason.ProductionStopped));
        Assert.Equal(NeedType.MoneyShortage, Need.TypeOf(NeedReason.Distress));
        Assert.Equal(NeedType.LaborShortage, Need.TypeOf(NeedReason.CannotExpandProduction));
        Assert.Equal(NeedType.StockShortage, Need.TypeOf(NeedReason.ToolsExhausted));
        Assert.Equal(NeedType.StockShortage, Need.TypeOf(NeedReason.DistantStock));

        Assert.Throws<ArgumentOutOfRangeException>(() => Need.TypeOf((NeedReason)999));
    }

    /// <summary>テスト表 #17。<c>NeedType</c> / <c>NeedReason</c> のどの要素も0でない。</summary>
    [Fact]
    public void NeedEnumsDoNotUseZero()
    {
        foreach (NeedType value in Enum.GetValues<NeedType>())
        {
            Assert.NotEqual(0, (int)value);
        }

        foreach (NeedReason value in Enum.GetValues<NeedReason>())
        {
            Assert.NotEqual(0, (int)value);
        }
    }

    /// <summary>
    /// テスト表 #18。順4(NeedGenerationSystem)の前後で全世帯の全欄(在庫・資金・ProductionRuns・
    /// UnmetConsumption・IsBankrupt・UnfilledPurchase・ToolWear・ErrandLaborLossPermille)が不変。
    /// </summary>
    [Fact]
    public void NeedGenerationWritesNoHouseholdState()
    {
        var necessityTargetStockDays = new int[Item.Count];
        necessityTargetStockDays[NecessityItem] = 1;

        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = InputA, Quantity = 3 } },
            laborPermille: 1000);

        var definition = BuildDefinition(recipe, necessityTargetStockDays: necessityTargetStockDays);
        var world = BuildWorld(new[] { NpcRank.Master, NpcRank.Apprentice }, toolStock: 0);

        var household = world.Households[0];
        household.WorkshopInventory[InputA] = 1; // 生産停止の不足も踏ませる
        household.IsBankrupt = 1;
        household.UnmetConsumption[NecessityItem] = 2;
        household.UnfilledPurchase[NecessityItem] = 3;
        household.LiquidFunds = 777;
        household.HouseholdInventory[NecessityItem] = 9;
        household.ToolWear = 5;
        household.ErrandLaborLossPermille = 11;

        int[] Snapshot(int[] source) => (int[])source.Clone();

        var householdInventoryBefore = Snapshot(household.HouseholdInventory);
        var workshopInventoryBefore = Snapshot(household.WorkshopInventory);
        var unmetConsumptionBefore = Snapshot(household.UnmetConsumption);
        var unfilledPurchaseBefore = Snapshot(household.UnfilledPurchase);
        int liquidFundsBefore = household.LiquidFunds;
        int productionRunsBefore = household.ProductionRuns;
        int isBankruptBefore = household.IsBankrupt;
        int toolWearBefore = household.ToolWear;
        int errandLaborLossPermilleBefore = household.ErrandLaborLossPermille;

        EconomySystemTestFixtures.RunDays(world, new NeedGenerationSystem(definition), days: 1);

        // 空振り防止。実際に書き込みうる経路(在庫・破産・不足・遠方在庫)を踏んでいること。
        Assert.NotEmpty(world.Needs);

        Assert.Equal(householdInventoryBefore, household.HouseholdInventory);
        Assert.Equal(workshopInventoryBefore, household.WorkshopInventory);
        Assert.Equal(unmetConsumptionBefore, household.UnmetConsumption);
        Assert.Equal(unfilledPurchaseBefore, household.UnfilledPurchase);
        Assert.Equal(liquidFundsBefore, household.LiquidFunds);
        Assert.Equal(productionRunsBefore, household.ProductionRuns);
        Assert.Equal(isBankruptBefore, household.IsBankrupt);
        Assert.Equal(toolWearBefore, household.ToolWear);
        Assert.Equal(errandLaborLossPermilleBefore, household.ErrandLaborLossPermille);
    }

    /// <summary>
    /// テスト表 #19。順4だけを登録したスケジューラを回しても乱数の状態が動かない
    /// (<c>ConsumptionSystemTests.ConsumptionDrawsNoRandomNumbers</c> に倣う)。
    /// </summary>
    [Fact]
    public void NeedGenerationDrawsNoRandomNumbers()
    {
        var necessityTargetStockDays = new int[Item.Count];
        necessityTargetStockDays[NecessityItem] = 1;
        var definition = BuildDefinition(QuietRecipe(), necessityTargetStockDays: necessityTargetStockDays);

        ulong RunWithSeed(long seed)
        {
            var world = BuildWorld(new[] { NpcRank.Master }, toolStock: 0);
            world.Households[0].IsBankrupt = 1;
            world.Households[0].UnmetConsumption[NecessityItem] = 2;
            world.Households[0].UnfilledPurchase[NecessityItem] = 3;

            var scheduler = new SimScheduler(
                new ISimSystem[] { new NeedGenerationSystem(definition) }, new RandomSource(seed));
            scheduler.Advance(world, ticks: 24);

            return StateHasher.Compute(world);
        }

        Assert.Equal(RunWithSeed(1), RunWithSeed(999999));
    }
}
