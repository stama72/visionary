using Visionary.Sim.Determinism;
using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="ProductionSystem"/>(GDD02a §1〜§4、#96 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class ProductionSystemTests
{
    // レシピの入力・出力に使う品目Id。テストの可読性のため Item.* とは別に短い名前を持つ。
    private const int InputA = 0;
    private const int InputB = 1;
    private const int Output = 2;

    /// <summary>
    /// テスト表 #3(W2-03)。所要労働400‰の定義で、親方(1000‰)+徒弟(300‰)=1300‰の世帯は
    /// floor(1300/400)=3回実行する。入力0件なので生産能力がそのまま実行回数になる(#12と共通の前提)。
    /// </summary>
    /// <remarks>
    /// 世帯主だけを数える実装(1000‰→2回)、または階層を見ず一律1000‰を足す実装
    /// (2000‰→5回)のどちらでも3にはならない。労働力係数‰を階層ごとに違う値にしてあるのが
    /// この判別力の根拠(Master=1000, Apprentice=300)。
    /// </remarks>
    [Fact]
    public void ProductionCapacitySumsAllMembersLaborPermille()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 400);

        var definition = EconomySystemTestFixtures.BuildDefinition(recipe);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Apprentice });
        world.Households[0].WorkshopInventory[Item.Tools] = 1;

        EconomySystemTestFixtures.RunDays(world, new ProductionSystem(definition), days: 1);

        Assert.Equal(3, world.Households[0].WorkshopInventory[Output]);
    }

    /// <summary>
    /// 【核心】テスト表 #4(W2-03)。労働力1300‰・所要労働400‰ → floor(1300/400)=3(切り上げなら4)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測・再測(2026-09-19、レビュー2巡目 象限I-b-1)。</b>生産能力の式は本タスクで
    /// <c>ProductionSystem.RunOneHousehold</c> から <see cref="Recipe.CapacityRuns"/> へ移った。
    /// このテストのレシピは <c>inputs: Array.Empty&lt;ItemQuantity&gt;()</c> なので
    /// <c>RunOneHousehold</c> の入力充足ループ(<c>IntegerMath.FloorDiv</c>)は一度も回らない ──
    /// 旧版の記録はそこへの変異を指しており、当てても緑のままになる(訂正)。<c>CapacityRuns</c>
    /// の外側の除算 <c>IntegerMath.FloorDiv(effectiveLaborPermille, LaborPermille)</c> を
    /// <c>IntegerMath.CeilDiv</c> に変える変異を当てたところ、<c>Assert.Equal(3, ...)</c> が
    /// 実際値4(floor(1300/400)=3の代わりにceil(1300/400)=4)で失敗した(赤を確認)。
    /// 変異を戻して緑に復帰させた。
    /// <para>
    /// この変異は <c>CapacityRunsFloorsBothDivisions</c>(別表 #1)も赤にするが、あちらが
    /// 唯一の守りなのは内側の除算(<c>floor(労働力‰×設備係数‰÷1000)</c>)であって、外側は
    /// このテストも押さえる。
    /// </para>
    /// </remarks>
    [Fact]
    public void ProductionCapacityRoundsDown()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 400);

        var definition = EconomySystemTestFixtures.BuildDefinition(recipe);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Apprentice });
        world.Households[0].WorkshopInventory[Item.Tools] = 1;

        EconomySystemTestFixtures.RunDays(world, new ProductionSystem(definition), days: 1);

        Assert.Equal(3, world.Households[0].WorkshopInventory[Output]);
    }

    /// <summary>
    /// テスト表 #5(W2-03)。生産能力3、入力Aが2回ぶん(qty1×stock2)・入力Bが5回ぶん(qty1×stock5)
    /// → 実行回数は最も少ないA基準の2回。Aは0に、Bは3回ぶん(3個)残る。
    /// </summary>
    [Fact]
    public void ProductionIsLimitedByTheScarcestInput()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: new[]
            {
                new ItemQuantity { ItemId = InputA, Quantity = 1 },
                new ItemQuantity { ItemId = InputB, Quantity = 1 },
            },
            laborPermille: 1000);

        // 生産能力3: Master単独3000‰ ÷ 所要労働1000‰。
        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe, laborPermilleByRank: new[] { 3000, 0, 0 });
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Tools] = 1;
        world.Households[0].WorkshopInventory[InputA] = 2;
        world.Households[0].WorkshopInventory[InputB] = 5;

        EconomySystemTestFixtures.RunDays(world, new ProductionSystem(definition), days: 1);

        Assert.Equal(0, world.Households[0].WorkshopInventory[InputA]);
        Assert.Equal(3, world.Households[0].WorkshopInventory[InputB]);
        Assert.Equal(2, world.Households[0].WorkshopInventory[Output]);
    }

    /// <summary>
    /// 【核心】テスト表 #19(#96)= W2-03 #6 を維持。入力在庫0 → 実行回数0。
    /// 工房在庫が1つも動かない(出力も増えない)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-15)。</b>「入力ごとに <c>FloorDiv(在庫, 必要数量)</c> で
    /// <c>runs</c> を <c>min</c> する」ループを丸ごと削る変異(入力の充足を確かめずに減算する側)を
    /// 当てたところ、<c>runs</c> が労働力由来の生産能力(5)のまま入力を無視して実行され、
    /// <c>Assert.Equal(0, ...InputA)</c> が実際値-5(在庫が負になる)で失敗した(赤を確認)。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ProductionStopsWhenAnInputIsExhausted()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = InputA, Quantity = 1 } },
            laborPermille: 1000);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe, laborPermilleByRank: new[] { 5000, 0, 0 });
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Tools] = 1;
        world.Households[0].WorkshopInventory[InputA] = 0;

        EconomySystemTestFixtures.RunDays(world, new ProductionSystem(definition), days: 1);

        Assert.Equal(0, world.Households[0].WorkshopInventory[InputA]);
        Assert.Equal(0, world.Households[0].WorkshopInventory[Output]);
        Assert.Equal(0, world.Households[0].ToolWear);
        Assert.Equal(1, world.Households[0].WorkshopInventory[Item.Tools]);
    }

    /// <summary>
    /// 【核心】テスト表 #10(#96)。労働力1300‰・所要労働185‰・工具0個・工具なし係数500‰ →
    /// floor(floor(1300×500÷1000)÷185) = floor(650÷185) = 3回実行し、在庫が動く。
    /// </summary>
    /// <remarks>
    /// W2-03 の #7(<c>ProductionStopsWhenToolStockIsZero</c>、「工具が無ければ停止」)は
    /// このテストで置き換える(タスク仕様)。旧仕様は工具切れの設備係数を0にしていたが、
    /// GDD02a §3 の改訂で500‰(初期値)に変わり、生産は止まらず半分の能力で続く。
    /// <para>
    /// <b>変異の実測・再測(2026-09-19、レビュー1巡目 象限III)。</b>
    /// <c>ProductionSystem.RunOneHousehold</c> の設備係数の判定を
    /// <c>household.WorkshopInventory[Item.Tools] &gt;= 1 ? IntegerMath.PermilleScale : 0</c>
    /// (旧仕様。工具なしを常に0回にする)へ戻す変異を当てたところ、
    /// 生産量の <c>Assert.Equal(3, ...Output)</c> が実際値0(生産能力0のまま実行回数0)で
    /// 失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </para>
    /// </remarks>
    [Fact]
    public void ProductionContinuesAtReducedCapacityWithoutTools()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 185);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe, laborPermilleByRank: new[] { 1000, 800, 300 }, equipmentPermilleWithoutTools: 500);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Apprentice });
        world.Households[0].WorkshopInventory[Item.Tools] = 0;

        EconomySystemTestFixtures.RunDays(world, new ProductionSystem(definition), days: 1);

        Assert.Equal(3, world.Households[0].WorkshopInventory[Output]);
        Assert.Equal(3, world.Households[0].ProductionRuns);
    }

    /// <summary>
    /// テスト表 #11(#96)。<see cref="ProductionContinuesAtReducedCapacityWithoutTools"/> と
    /// 同じ定義で工具なし係数0‰ → 0回、在庫も摩耗も動かない。
    /// </summary>
    [Fact]
    public void EquipmentWithoutToolsZeroStopsProduction()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 185);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe, laborPermilleByRank: new[] { 1000, 800, 300 }, equipmentPermilleWithoutTools: 0);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Apprentice });
        world.Households[0].WorkshopInventory[Item.Tools] = 0;

        EconomySystemTestFixtures.RunDays(world, new ProductionSystem(definition), days: 1);

        Assert.Equal(0, world.Households[0].WorkshopInventory[Output]);
        Assert.Equal(0, world.Households[0].ProductionRuns);
        Assert.Equal(0, world.Households[0].ToolWear);
    }

    /// <summary>
    /// テスト表 #8(W2-03)。N=3(耐久値3000‰人日)、1回/日の生産で、2日目までは工具在庫が減らず、
    /// 3日目に1個減って <c>ToolWear</c> が0に戻る。
    /// </summary>
    [Fact]
    public void ToolWearCarriesOverAcrossDaysUntilNIsReached()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 1000);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe, laborPermilleByRank: new[] { 1000, 0, 0 }, toolLifeLaborDays: 3);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Tools] = 5;

        var system = new ProductionSystem(definition);

        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(1000, world.Households[0].ToolWear);
        Assert.Equal(5, world.Households[0].WorkshopInventory[Item.Tools]);

        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(2000, world.Households[0].ToolWear);
        Assert.Equal(5, world.Households[0].WorkshopInventory[Item.Tools]);

        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(0, world.Households[0].ToolWear);
        Assert.Equal(4, world.Households[0].WorkshopInventory[Item.Tools]);
    }

    /// <summary>
    /// テスト表 #9(W2-03)。N=3(耐久値3000)・工具2個・1日に5回実行できる定義 →
    /// 工具1個減り <c>ToolWear</c>=2000 が残る。
    /// </summary>
    [Fact]
    public void ToolWearLeavesTheRemainderForTheNextTool()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 1000);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe, laborPermilleByRank: new[] { 5000, 0, 0 }, toolLifeLaborDays: 3);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Tools] = 2;

        EconomySystemTestFixtures.RunDays(world, new ProductionSystem(definition), days: 1);

        Assert.Equal(1, world.Households[0].WorkshopInventory[Item.Tools]);
        Assert.Equal(2000, world.Households[0].ToolWear);
    }

    /// <summary>
    /// テスト表 #10(W2-03)。工具1個・N=3・1日に7回実行できる定義 → 工具0個、<c>ToolWear</c>=0。
    /// 翌日は設備係数0で停止する(出力が増えない。工具なし係数0を明示した定義)。
    /// </summary>
    [Fact]
    public void ToolWearIsDroppedWhenToolStockRunsOut()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 1000);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe, laborPermilleByRank: new[] { 7000, 0, 0 }, toolLifeLaborDays: 3,
            equipmentPermilleWithoutTools: 0);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Tools] = 1;

        var system = new ProductionSystem(definition);

        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(0, world.Households[0].WorkshopInventory[Item.Tools]);
        Assert.Equal(0, world.Households[0].ToolWear);
        Assert.Equal(7, world.Households[0].WorkshopInventory[Output]);

        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(7, world.Households[0].WorkshopInventory[Output]); // 翌日は増えない
    }

    /// <summary>テスト表 #11(W2-03)。木材加工型(入力1→出力3)を1回実行 → 出力+3、入力-1。</summary>
    [Fact]
    public void ProductionAppliesOutputQuantity()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 3 } },
            inputs: new[] { new ItemQuantity { ItemId = InputA, Quantity = 1 } },
            laborPermille: 1000);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe, laborPermilleByRank: new[] { 1000, 0, 0 });
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Tools] = 1;
        world.Households[0].WorkshopInventory[InputA] = 10;

        EconomySystemTestFixtures.RunDays(world, new ProductionSystem(definition), days: 1);

        Assert.Equal(3, world.Households[0].WorkshopInventory[Output]);
        Assert.Equal(9, world.Households[0].WorkshopInventory[InputA]);
    }

    /// <summary>テスト表 #12(W2-03)。入力0件のレシピで、生産能力ぶん実行される。</summary>
    [Fact]
    public void ProductionRunsWithoutInputsUpToCapacity()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 1000);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe, laborPermilleByRank: new[] { 4000, 0, 0 });
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Tools] = 1;

        EconomySystemTestFixtures.RunDays(world, new ProductionSystem(definition), days: 1);

        Assert.Equal(4, world.Households[0].WorkshopInventory[Output]);
    }

    /// <summary>
    /// テスト表 #20(#96)= W2-03 #13 を維持。マスターシードだけを変えた2つの
    /// <c>RandomSource</c> で同じ世界を1日進め、状態ハッシュが一致する。
    /// <see cref="SimContext.OpenRandom(int)"/> を使えば(二重オープン以外は)乱数消費列が
    /// シードで変わるため、一致すれば乱数を引いていない。
    /// </summary>
    [Fact]
    public void ProductionDrawsNoRandomNumbers()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = InputA, Quantity = 1 } },
            laborPermille: 1000);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe, laborPermilleByRank: new[] { 3000, 0, 0 }, toolLifeLaborDays: 2);

        ulong RunWithSeed(long seed)
        {
            var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
            world.Households[0].WorkshopInventory[Item.Tools] = 1;
            world.Households[0].WorkshopInventory[InputA] = 10;

            var scheduler = new SimScheduler(
                new ISimSystem[] { new ProductionSystem(definition) }, new RandomSource(seed));
            scheduler.Advance(world, ticks: 24);

            return StateHasher.Compute(world);
        }

        Assert.Equal(RunWithSeed(1), RunWithSeed(999999));
    }

    /// <summary>
    /// 【核心】別表 #30(W2-03)。N=3(耐久値3000)・工具在庫3個・1日に7回実行できる定義 →
    /// 工具が2個減って1個残り、<c>ToolWear</c> == 1000(= 7000 − 2×3000)。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 表 #9(<c>ToolWearLeavesTheRemainderForTheNextTool</c>)は置き換えない ──
    /// あちらは <c>consumed == 1</c>(端数を次の工具へ持ち越す経路)、このテストは
    /// <c>consumed == 2</c>(複数個の工具を1日で消費する経路)を押さえる。
    /// <c>consumed == 1</c> では <c>consumed × 耐久値</c> と <c>耐久値</c> が同値になり、
    /// 「<c>consumed × 耐久値</c> を引かずに <c>耐久値</c> だけ引く」変異を判別できない
    /// (2巡目レビュー象限I-a)。
    /// </para>
    /// <para>
    /// <b>変異の実測(2026-09-16)。</b><c>ProductionSystem.WearTools</c> の
    /// <c>household.ToolWear -= consumed * _definition.ToolDurabilityPerUnit;</c> を
    /// <c>household.ToolWear -= _definition.ToolDurabilityPerUnit;</c>(掛け忘れ)に
    /// 変える変異を当てたところ、<c>Assert.Equal(1000, ...ToolWear)</c> が実際値4000
    /// (7000 − 3000 = 4000。「工具在庫がある間は 0 ≤ ToolWear &lt; N×1000」の後条件が破れる)で
    /// 失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </para>
    /// </remarks>
    [Fact]
    public void ToolWearConsumesMultipleToolsInOneDay()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 1000);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe, laborPermilleByRank: new[] { 7000, 0, 0 }, toolLifeLaborDays: 3);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Tools] = 3;

        EconomySystemTestFixtures.RunDays(world, new ProductionSystem(definition), days: 1);

        Assert.Equal(1, world.Households[0].WorkshopInventory[Item.Tools]);
        Assert.Equal(1000, world.Households[0].ToolWear);
    }

    /// <summary>
    /// 【核心】別表 #31(W2-03)。入力A(数量2・在庫5)と入力B(数量3・在庫15)を持つレシピで
    /// 生産能力3 → 実行2回。入力Aは1、入力Bは9が残る。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 表 #5(<c>ProductionIsLimitedByTheScarcestInput</c>)は置き換えない ──
    /// あちらは入力2本の <c>min</c>(最も逼迫した入力で決まること)を押さえ、
    /// このテストは <c>Quantity &gt; 1</c> のときの除算・乗算を押さえる。
    /// 数量が常に1のテストだけでは、「在庫の個数」と「数量で割った回数」が一致してしまい、
    /// 割り忘れ・掛け忘れのどちらも判別できない(2巡目レビュー象限I-a)。
    /// </para>
    /// <para>
    /// <b>入力Aの在庫は5(3巡目レビューI-aで4から訂正)。</b>当初の4は2で割り切れ、
    /// <c>FloorDiv(4,2)=CeilDiv(4,2)=2</c> となるため、この除算の丸めの向き
    /// (<c>FloorDiv</c> と <c>CeilDiv</c> の違い)を一度も判別できていなかった
    /// (3巡目レビューで実測)。5にすると <c>FloorDiv(5,2)=2</c> / <c>CeilDiv(5,2)=3</c> で
    /// 向きが分かれる。GDD02a §1が「切り上げ規約の意図的な例外」と明記した除算なので、
    /// 既定(切り上げ)へ引き戻す変異は書き手の善意からでも起こりうる。
    /// </para>
    /// <para>
    /// <b>変異の実測1(2026-09-16)。</b><c>ProductionSystem.RunOneHousehold</c> の
    /// <c>IntegerMath.FloorDiv(household.WorkshopInventory[input.ItemId], input.Quantity)</c> を
    /// <c>household.WorkshopInventory[input.ItemId]</c>(在庫の個数をそのまま使う。割り忘れ)に
    /// 変える変異を当てたところ、実行回数が3回(min(capacity=3, stockA=5, stockB=15))になり、
    /// <c>Assert.Equal(1, ...InputA)</c> が実際値-1(5 − 2×3 = -1。在庫が負に落ちる)で
    /// 失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </para>
    /// <para>
    /// <b>変異の実測2(2026-09-16)。</b>入力の減算
    /// <c>household.WorkshopInventory[input.ItemId] -= input.Quantity * runs;</c> の
    /// <c>input.Quantity *</c> を落とす変異(掛け忘れ。<c>-= runs;</c>)を当てたところ、
    /// <c>Assert.Equal(1, ...InputA)</c> が実際値3(5 − 2 = 3)で失敗した(赤を確認)。
    /// 変異を戻して緑に復帰させた。
    /// </para>
    /// <para>
    /// <b>変異の実測3(2026-09-16)。</b><c>IntegerMath.FloorDiv</c> を
    /// <c>IntegerMath.CeilDiv</c>(丸めの向きの取り違え)に変える変異を当てたところ、
    /// <c>CeilDiv(5,2)=3</c> で実行回数が3回になり、<c>Assert.Equal(1, ...InputA)</c> が
    /// 実際値-1(5 − 2×3 = -1)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </para>
    /// </remarks>
    [Fact]
    public void ProductionDividesAndMultipliesByInputQuantity()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: new[]
            {
                new ItemQuantity { ItemId = InputA, Quantity = 2 },
                new ItemQuantity { ItemId = InputB, Quantity = 3 },
            },
            laborPermille: 1000);

        // 生産能力3: Master単独3000‰ ÷ 所要労働1000‰。
        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe, laborPermilleByRank: new[] { 3000, 0, 0 });
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Tools] = 1;
        world.Households[0].WorkshopInventory[InputA] = 5;
        world.Households[0].WorkshopInventory[InputB] = 15;

        EconomySystemTestFixtures.RunDays(world, new ProductionSystem(definition), days: 1);

        Assert.Equal(1, world.Households[0].WorkshopInventory[InputA]);
        Assert.Equal(9, world.Households[0].WorkshopInventory[InputB]);
    }

    /// <summary>
    /// テスト表 #7(#96)。所要労働108‰・12回/日・N=13(耐久値13000) →
    /// 10日目まで工具が減らず(累積12960 &lt; 13000)、11日目に1個減って
    /// <c>ToolWear</c> = 14256 − 13000 = 1256。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測・再測(2026-09-19、レビュー1巡目 象限III)。</b><c>ProductionSystem.WearTools</c>
    /// の <c>household.ToolWear += laborPermille * runs;</c> を <c>household.ToolWear += runs;</c>
    /// (回数を足す)に変える変異を当てたところ、<b>10日目の <c>Assert.Equal(12960,
    /// ...ToolWear)</c>が実際値120(12回/日 × 10日)で先に失敗した</b>(赤を確認)。
    /// 11日目の <c>Assert.Equal(4, ...Tools)</c> には xUnit が最初の失敗で止まるため到達しない
    /// (旧版の記録は11日目の assert が失敗すると書いていたが、実測はこの10日目の assert で
    /// 止まる。訂正)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ToolWearAccumulatesLaborNotRuns()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 108);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe, laborPermilleByRank: new[] { 1300, 0, 0 }, toolLifeLaborDays: 13);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Tools] = 5;

        var system = new ProductionSystem(definition);

        EconomySystemTestFixtures.RunDays(world, system, days: 10);
        Assert.Equal(5, world.Households[0].WorkshopInventory[Item.Tools]);
        Assert.Equal(12960, world.Households[0].ToolWear);

        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(4, world.Households[0].WorkshopInventory[Item.Tools]);
        Assert.Equal(1256, world.Households[0].ToolWear);
    }

    /// <summary>
    /// 【核心】テスト表 #8(#96)。N=1(耐久値1000)・工具3個・所要労働350‰・
    /// 労働力係数を親方2000‰/徒弟500‰にして7回/日=2450‰人日/日 →
    /// 1日で2個減り、<c>ToolWear</c> = 450(= 2450 − 2×1000)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測・再測(2026-09-19、レビュー1巡目 象限III)。</b><c>ProductionSystem.WearTools</c>
    /// の <c>household.ToolWear -= consumed * _definition.ToolDurabilityPerUnit;</c> を
    /// <c>household.ToolWear -= _definition.ToolDurabilityPerUnit;</c>(掛け忘れ)に変える
    /// 変異を当てたところ、<c>Assert.Equal(450, ...ToolWear)</c>が実際値1450
    /// (2450 − 1000 = 1450。「工具在庫がある間は 0 ≤ ToolWear &lt; N×1000」の後条件が破れる)
    /// で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ToolWearCarriesOverAndConsumesMultipleTools()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 350);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe, laborPermilleByRank: new[] { 2000, 0, 500 }, toolLifeLaborDays: 1);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Apprentice });
        world.Households[0].WorkshopInventory[Item.Tools] = 3;

        EconomySystemTestFixtures.RunDays(world, new ProductionSystem(definition), days: 1);

        Assert.Equal(1, world.Households[0].WorkshopInventory[Item.Tools]);
        Assert.Equal(450, world.Households[0].ToolWear);
    }

    /// <summary>
    /// テスト表 #9(#96)。N=1(耐久値1000)・工具1個・所要労働400‰×3回/日=1200/日 →
    /// 工具0個・<c>ToolWear</c>=0。翌日は設備係数が工具なしの値(500‰)で続く(0回にはならない)。
    /// </summary>
    [Fact]
    public void ToolWearIsDroppedWhenToolsRunOut()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 400);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe, laborPermilleByRank: new[] { 1200, 0, 0 }, toolLifeLaborDays: 1,
            equipmentPermilleWithoutTools: 500);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Tools] = 1;

        var system = new ProductionSystem(definition);

        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(0, world.Households[0].WorkshopInventory[Item.Tools]);
        Assert.Equal(0, world.Households[0].ToolWear);
        Assert.Equal(3, world.Households[0].WorkshopInventory[Output]);

        // 翌日は設備係数が工具なしの値(500‰)で続く。0回にはならない。
        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(1, world.Households[0].ProductionRuns);
        Assert.Equal(4, world.Households[0].WorkshopInventory[Output]);
    }

    /// <summary>
    /// 【核心】別表 #29(#96)。表 #9 の続き。工具が尽きた翌日以降も、工具0個で生産が続く
    /// あいだ日末の <c>ToolWear</c> が0のままであること(耐久値1000に届かないので
    /// <c>worn == 0</c> の経路を通る)。
    /// </summary>
    /// <remarks>
    /// <b>#9 と同じ工具・労働量の設定を使い、日数だけ伸ばす</b>(タスク仕様の別表)。別の
    /// フィクスチャを立てると、#9 が押さえる「尽きた日の破棄」と #29 が押さえる
    /// 「尽きた翌日以降の破棄」が別の前提の上に乗り、片方を壊してももう片方が緑のまま残る。
    /// <para>
    /// <b>3日目(工具を1個与えて新品が前日までの摩耗を引き継がないことを確かめる部分)は
    /// 削ってある(#96 3巡目 象限Iの訂正)。</b>2日目が <c>ToolWear</c> を0に落とした後では
    /// 3日目の開始状態(工具1個・<c>ToolWear</c> 0)が1日目の開始状態と同一になり、判別力が
    /// 無かった。その性質(摩耗を直接置いた状態からの引き継ぎ)は別表 #30 が押さえる。
    /// </para>
    /// <para>
    /// <b>変異の実測・再測(2026-09-19、#96 3巡目の訂正を受けて再確認)。</b>
    /// <c>ProductionSystem.WearTools</c> の
    /// 破棄(<c>if (household.WorkshopInventory[Item.Tools] == 0) household.ToolWear = 0;</c>)を
    /// <c>if (worn &gt; 0)</c> の内側へ入れ子にする変異(1巡目 象限I-b の指摘そのもの)を
    /// 当てたところ、2日目(工具0個のまま1回実行・<c>worn == 0</c>)終了時点の
    /// <c>Assert.Equal(0, world.Households[0].ToolWear)</c> が実際値400で失敗した(赤を確認 ──
    /// 破棄が <c>worn &gt; 0</c> の枝に入れ子だと、<c>worn == 0</c> の日は破棄を素通りする)。
    /// 変異を戻して緑に復帰させた。
    /// </para>
    /// </remarks>
    [Fact]
    public void ToolWearIsDroppedOnEveryDayWithoutTools()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 400);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe, laborPermilleByRank: new[] { 1200, 0, 0 }, toolLifeLaborDays: 1,
            equipmentPermilleWithoutTools: 500);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Tools] = 1;

        var system = new ProductionSystem(definition);

        // 1日目。#9と同じ:工具1個→0個、ToolWear=0(破棄済み)。
        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(0, world.Households[0].WorkshopInventory[Item.Tools]);
        Assert.Equal(0, world.Households[0].ToolWear);

        // 2日目。工具0個のまま500‰で1回実行(400‰人日)、耐久値1000に届かず worn == 0。
        // 破棄が無条件(worn > 0の外)でなければ、ここでToolWearが400のまま残る。
        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(1, world.Households[0].ProductionRuns);
        Assert.Equal(0, world.Households[0].WorkshopInventory[Item.Tools]);
        Assert.Equal(0, world.Households[0].ToolWear);

        // 耐久の予想在庫(工具在庫×耐久値−ToolWear)が非負であること(元の指摘の実害の経路)。
        var demandAfterDay2 = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        var durableLineAfterDay2 = demandAfterDay2.Lines.Single(
            line => line.Purpose == DemandPurpose.Durable && line.ItemId == Item.Tools);
        Assert.True(
            durableLineAfterDay2.ExpectedStock >= 0,
            $"耐久の予想在庫が負({durableLineAfterDay2.ExpectedStock})。ToolWearの破棄漏れの実害。");
    }

    /// <summary>
    /// 【核心】別表 #30(#96)。実行回数0の日も破棄が走ること。N=2(耐久値2000)・所要労働400‰・
    /// 労働力1300‰(親方1000+徒弟300)・工具なし係数0‰ の定義で、工具在庫0と
    /// <c>ToolWear</c>=1500を直接置いて1日進めると <c>ProductionRuns == 0</c> かつ日末の
    /// <c>ToolWear == 0</c>、予想在庫 <c>0×2000−0</c> が非負。続けて工具を1個与えて1日進めると
    /// 3回実行(floor(1300/400)=3)して <c>ToolWear == 1200</c>(&lt; 2000)で工具在庫は1のまま。
    /// </summary>
    /// <remarks>
    /// <b>工具なし係数を0‰にして実行回数0を作る</b>(タスク仕様)。入力切れで0回にすると、
    /// 破棄が走らない実装でも入力の枯渇が先に目立ち、何が壊れているのか読めなくなる。
    /// <c>ToolWear</c> は表#16・#17と同じ手(状態を直接置いて読み手を確かめる)で1500を置く ──
    /// 「工具を売り切った翌日、実行回数0で古い<c>ToolWear</c>が残る」実害の経路そのもの。
    /// <para>
    /// <b>変異の実測(2026-09-19)。</b>破棄を <c>if (runs &lt;= 0) { ...; return; }</c> の
    /// 早期return内側に戻す変異(3巡目 象限Iの指摘そのもの)を当てたところ、1日目
    /// (<c>ProductionRuns == 0</c>)の末尾で破棄が一度も走らず、
    /// <c>Assert.Equal(0, world.Households[0].ToolWear)</c> が実際値1500で失敗した(赤を確認)。
    /// 仕様の予想(1日目に1500が残り、2日目が1500+1200=2700≥2000で買ったばかりの工具が
    /// 買ったその日に消費される)には届かず、1日目のassertで先に止まる。変異を戻して
    /// 緑に復帰させた。
    /// </para>
    /// </remarks>
    [Fact]
    public void ToolWearIsDroppedOnDaysWithZeroProduction()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 400);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe, laborPermilleByRank: new[] { 1000, 0, 300 }, toolLifeLaborDays: 2,
            equipmentPermilleWithoutTools: 0);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Apprentice });
        world.Households[0].WorkshopInventory[Item.Tools] = 0;
        world.Households[0].ToolWear = 1500;

        var system = new ProductionSystem(definition);

        // 1日目。工具在庫0・工具なし係数0‰なので実行回数0。破棄は実行回数0の日も走る。
        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(0, world.Households[0].ProductionRuns);
        Assert.Equal(0, world.Households[0].ToolWear);

        var demandAfterDay1 = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        var durableLineAfterDay1 = demandAfterDay1.Lines.Single(
            line => line.Purpose == DemandPurpose.Durable && line.ItemId == Item.Tools);
        Assert.True(
            durableLineAfterDay1.ExpectedStock >= 0,
            $"耐久の予想在庫が負({durableLineAfterDay1.ExpectedStock})。ToolWearの破棄漏れの実害。");

        // 2日目。工具を1個与える ── 買ったその日に壊れてはいけない。
        world.Households[0].WorkshopInventory[Item.Tools] = 1;
        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(3, world.Households[0].ProductionRuns);
        Assert.Equal(1, world.Households[0].WorkshopInventory[Item.Tools]);
        Assert.Equal(1200, world.Households[0].ToolWear);
    }

    /// <summary>
    /// 【核心】テスト表 #16(#96)。所要労働216‰・損失100‰ → 5回(損失なしなら6)。
    /// 損失1300‰以上(ここでは1400‰)なら0回で在庫が動かず、負にもならない。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測・再測(2026-09-19、レビュー1巡目 象限III)。</b>
    /// <c>ProductionSystem.RunOneHousehold</c> の
    /// <c>int laborPermille = Math.Max(0, totalLaborPermille - household.ErrandLaborLossPermille);</c>
    /// を <c>int laborPermille = totalLaborPermille - household.ErrandLaborLossPermille;</c>
    /// (<c>Math.Max(0, …)</c> を落とす)に変える変異を当てたところ、損失1400‰のケースで
    /// 負の労働力(-100)が <c>Recipe.CapacityRuns</c> にそのまま渡り、その入口検査
    /// (<c>laborPermille &lt; 0</c>)に引っかかって <c>ArgumentOutOfRangeException</c>
    /// (「労働力合計‰は非負」)が投げられ、テストが例外で失敗した(赤を確認 ── 旧版の記録は
    /// <c>Assert.Equal(0, ...)</c> が実際値-1で失敗すると書いていたが、
    /// <c>CapacityRuns</c> が負の引数を先に例外で拒むため <c>FloorDiv</c> が負の商を返す経路は
    /// 実在しない。訂正)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ProductionSubtractsPreviousDayErrandLaborLoss()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 216);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe, laborPermilleByRank: new[] { 1000, 800, 300 });

        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Apprentice });
        world.Households[0].WorkshopInventory[Item.Tools] = 1;
        world.Households[0].ErrandLaborLossPermille = 100;

        EconomySystemTestFixtures.RunDays(world, new ProductionSystem(definition), days: 1);

        Assert.Equal(5, world.Households[0].WorkshopInventory[Output]);
        Assert.Equal(5, world.Households[0].ProductionRuns);

        var world2 = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Apprentice });
        world2.Households[0].WorkshopInventory[Item.Tools] = 1;
        world2.Households[0].ErrandLaborLossPermille = 1400;

        EconomySystemTestFixtures.RunDays(world2, new ProductionSystem(definition), days: 1);

        Assert.Equal(0, world2.Households[0].WorkshopInventory[Output]);
        Assert.Equal(0, world2.Households[0].ProductionRuns);
    }

    /// <summary>
    /// テスト表 #17(#96)。損失100‰を置いて1日進めても
    /// <c>ErrandLaborLossPermille == 100</c> のまま(書き手は本タスクの範囲に無い順5だけ)。
    /// </summary>
    [Fact]
    public void ProductionDoesNotResetErrandLaborLoss()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 216);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe, laborPermilleByRank: new[] { 1000, 800, 300 });
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Apprentice });
        world.Households[0].WorkshopInventory[Item.Tools] = 1;
        world.Households[0].ErrandLaborLossPermille = 100;

        EconomySystemTestFixtures.RunDays(world, new ProductionSystem(definition), days: 1);

        Assert.Equal(100, world.Households[0].ErrandLaborLossPermille);
    }

    /// <summary>
    /// 【核心】テスト表 #18(#96)。入力6回ぶん・生産能力6 → 1日目 <c>ProductionRuns == 6</c>、
    /// 2日目(入力0)は <c>== 0</c>(前日の6が残らない)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測・再測(2026-09-19、レビュー1巡目 象限III)。</b>
    /// <c>ProductionSystem.RunOneHousehold</c> の
    /// <c>household.ProductionRuns = runs;</c> を <c>household.ProductionRuns += runs;</c>
    /// (累積する変異)に変えたところ、2日目の <c>Assert.Equal(0, ...ProductionRuns)</c>
    /// が実際値6(前日の6に0を足しただけで残った)で失敗した(赤を確認)。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ProductionRecordsRunsEveryDayIncludingZero()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = InputA, Quantity = 1 } },
            laborPermille: 216);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe, laborPermilleByRank: new[] { 1000, 800, 300 });
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Apprentice });
        world.Households[0].WorkshopInventory[Item.Tools] = 1;
        world.Households[0].WorkshopInventory[InputA] = 6;

        var system = new ProductionSystem(definition);

        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(6, world.Households[0].ProductionRuns);

        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(0, world.Households[0].ProductionRuns);
    }

    /// <summary>
    /// テスト表 #25(#40)。<see cref="LaborCapacity"/> へ切り出した後も GDD02a §2 の能力表
    /// (水車小屋番: 1300‰→7 / 1200‰→6 / 1000‰→5 / 650‰(工具なし)→3)が変わらないこと。
    /// M0 の実際のレシピ(所要労働185‰)と工具なし係数(500‰)を使う ── 手組みの値ではなく
    /// 表そのものと突き合わせる。
    /// </summary>
    [Fact]
    public void ProductionSystemStillProducesTheSameAfterExtraction()
    {
        var definition = WorldDefinition.M0;
        var recipe = definition.Recipes[(int)Occupation.Miller];

        World BuildWorld(NpcRank[] memberRanks, bool hasTools, int errandLaborLossPermille)
        {
            var world = new World(
                npcCount: memberRanks.Length, householdCount: 1, itemCount: definition.ItemCount);

            var memberNpcIds = new int[memberRanks.Length];

            for (int i = 0; i < memberRanks.Length; i++)
            {
                world.Npcs[i].Rank = memberRanks[i];
                memberNpcIds[i] = i;
            }

            world.Households[0] = new HouseholdState(
                id: 0, districtId: 0, headNpcId: 0, memberNpcIds: memberNpcIds, itemCount: definition.ItemCount);
            world.Households[0].Occupation = Occupation.Miller;
            world.Households[0].WorkshopInventory[Item.Tools] = hasTools ? 1 : 0;
            world.Households[0].WorkshopInventory[recipe.Inputs[0].ItemId] = 100; // 穀物を十分に
            world.Households[0].ErrandLaborLossPermille = errandLaborLossPermille;

            return world;
        }

        var system = new ProductionSystem(definition);

        // 1300‰: 親方+徒弟、工具あり、損失0 → floor(1300/185)=7。
        var world1300 = BuildWorld(
            new[] { NpcRank.Master, NpcRank.Apprentice }, hasTools: true, errandLaborLossPermille: 0);
        EconomySystemTestFixtures.RunDays(world1300, system, days: 1);
        Assert.Equal(7, world1300.Households[0].ProductionRuns);

        // 1200‰: 前日の外出損失100‰ → floor(1200/185)=6。
        var world1200 = BuildWorld(
            new[] { NpcRank.Master, NpcRank.Apprentice }, hasTools: true, errandLaborLossPermille: 100);
        EconomySystemTestFixtures.RunDays(world1200, system, days: 1);
        Assert.Equal(6, world1200.Households[0].ProductionRuns);

        // 1000‰: 親方だけ → floor(1000/185)=5。
        var world1000 = BuildWorld(new[] { NpcRank.Master }, hasTools: true, errandLaborLossPermille: 0);
        EconomySystemTestFixtures.RunDays(world1000, system, days: 1);
        Assert.Equal(5, world1000.Households[0].ProductionRuns);

        // 650‰: 親方+徒弟、工具なし(設備係数500‰) → floor(floor(1300×500÷1000)÷185)=floor(650÷185)=3。
        var world650 = BuildWorld(
            new[] { NpcRank.Master, NpcRank.Apprentice }, hasTools: false, errandLaborLossPermille: 0);
        EconomySystemTestFixtures.RunDays(world650, system, days: 1);
        Assert.Equal(3, world650.Households[0].ProductionRuns);
    }
}
