using Visionary.Sim.Determinism;
using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="ProductionSystem"/>(GDD02 §5.2・§5.3、#34 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class ProductionSystemTests
{
    // レシピの入力・出力に使う品目Id。テストの可読性のため Item.* とは別に短い名前を持つ。
    private const int InputA = 0;
    private const int InputB = 1;
    private const int Output = 2;

    /// <summary>
    /// テスト表 #3。所要労働400‰の定義で、親方(1000‰)+徒弟(300‰)=1300‰の世帯は
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
    /// 【核心】テスト表 #4。労働力1300‰・所要労働400‰ → floor(1300/400)=3(切り上げなら4)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-15)。</b><c>ProductionSystem.RunOneHousehold</c> の生産能力の
    /// <c>IntegerMath.FloorDiv</c> を <c>IntegerMath.CeilDiv</c> に変える変異を当てたところ、
    /// <c>Assert.Equal(3, ...)</c> が実際値4(floor(1300/400)=3の代わりにceil=4)で失敗した
    /// (赤を確認)。変異を戻して緑に復帰させた。
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
    /// テスト表 #5。生産能力3、入力Aが2回ぶん(qty1×stock2)・入力Bが5回ぶん(qty1×stock5)
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
    /// 【核心】テスト表 #6。入力在庫0 → 実行回数0。工房在庫が1つも動かない(出力も増えない)。
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
        Assert.Equal(0, world.Households[0].ToolWearCount);
        Assert.Equal(1, world.Households[0].WorkshopInventory[Item.Tools]);
    }

    /// <summary>
    /// 【核心】テスト表 #7。入力が十分あっても工具在庫0なら実行回数0、在庫が動かない。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-15)。</b>設備係数の判定 <c>household.WorkshopInventory[Item.Tools]
    /// &gt;= 1</c> を <c>&gt;= 0</c> に変える変異を当てたところ、工具0個でも設備係数1000‰が
    /// 立って生産能力5が成立し、<c>Assert.Equal(10, ...InputA)</c> が実際値5(5回ぶん消費された)
    /// で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ProductionStopsWhenToolStockIsZero()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = InputA, Quantity = 1 } },
            laborPermille: 1000);

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe, laborPermilleByRank: new[] { 5000, 0, 0 });
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Tools] = 0;
        world.Households[0].WorkshopInventory[InputA] = 10;

        EconomySystemTestFixtures.RunDays(world, new ProductionSystem(definition), days: 1);

        Assert.Equal(10, world.Households[0].WorkshopInventory[InputA]);
        Assert.Equal(0, world.Households[0].WorkshopInventory[Output]);
        Assert.Equal(0, world.Households[0].ToolWearCount);
    }

    /// <summary>
    /// テスト表 #8。N=3、1回/日の生産で、2日目までは工具在庫が減らず、
    /// 3日目に1個減って <c>ToolWearCount</c> が0に戻る。
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
            recipe, laborPermilleByRank: new[] { 1000, 0, 0 }, productionRunsPerToolWear: 3);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Tools] = 5;

        var system = new ProductionSystem(definition);

        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(1, world.Households[0].ToolWearCount);
        Assert.Equal(5, world.Households[0].WorkshopInventory[Item.Tools]);

        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(2, world.Households[0].ToolWearCount);
        Assert.Equal(5, world.Households[0].WorkshopInventory[Item.Tools]);

        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(0, world.Households[0].ToolWearCount);
        Assert.Equal(4, world.Households[0].WorkshopInventory[Item.Tools]);
    }

    /// <summary>
    /// テスト表 #9。N=3・工具2個・1日に5回実行できる定義 → 工具1個減り ToolWearCount=2 が残る。
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
            recipe, laborPermilleByRank: new[] { 5000, 0, 0 }, productionRunsPerToolWear: 3);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Tools] = 2;

        EconomySystemTestFixtures.RunDays(world, new ProductionSystem(definition), days: 1);

        Assert.Equal(1, world.Households[0].WorkshopInventory[Item.Tools]);
        Assert.Equal(2, world.Households[0].ToolWearCount);
    }

    /// <summary>
    /// テスト表 #10。工具1個・N=3・1日に7回実行できる定義 → 工具0個、ToolWearCount=0。
    /// 翌日は設備係数0で停止する(出力が増えない)。
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
            recipe, laborPermilleByRank: new[] { 7000, 0, 0 }, productionRunsPerToolWear: 3);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Tools] = 1;

        var system = new ProductionSystem(definition);

        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(0, world.Households[0].WorkshopInventory[Item.Tools]);
        Assert.Equal(0, world.Households[0].ToolWearCount);
        Assert.Equal(7, world.Households[0].WorkshopInventory[Output]);

        EconomySystemTestFixtures.RunDays(world, system, days: 1);
        Assert.Equal(7, world.Households[0].WorkshopInventory[Output]); // 翌日は増えない
    }

    /// <summary>テスト表 #11。木材加工型(入力1→出力3)を1回実行 → 出力+3、入力-1。</summary>
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

    /// <summary>テスト表 #12。入力0件のレシピで、生産能力ぶん実行される。</summary>
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
    /// テスト表 #13。マスターシードだけを変えた2つの <c>RandomSource</c> で同じ世界を1日進め、
    /// 状態ハッシュが一致する。<see cref="SimContext.OpenRandom(int)"/> を使えば
    /// (二重オープン以外は)乱数消費列がシードで変わるため、一致すれば乱数を引いていない。
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
            recipe, laborPermilleByRank: new[] { 3000, 0, 0 }, productionRunsPerToolWear: 2);

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
    /// 【核心】別表 #30。N=3・工具在庫3個・1日に7回実行できる定義 → 工具が2個減って1個残り、
    /// ToolWearCount == 1(= 7 − 2×3)。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 表 #9(<c>ToolWearLeavesTheRemainderForTheNextTool</c>)は置き換えない ──
    /// あちらは <c>consumed == 1</c>(端数を次の工具へ持ち越す経路)、このテストは
    /// <c>consumed == 2</c>(複数個の工具を1日で消費する経路)を押さえる。
    /// <c>consumed == 1</c> では <c>consumed × N</c> と <c>N</c> が同値になり、
    /// 「<c>consumed × N</c> を引かずに <c>N</c> だけ引く」変異を判別できない
    /// (2巡目レビュー象限I-a)。
    /// </para>
    /// <para>
    /// <b>変異の実測(2026-09-16)。</b><c>ProductionSystem.WearTools</c> の
    /// <c>household.ToolWearCount -= consumed * _definition.ProductionRunsPerToolWear;</c> を
    /// <c>household.ToolWearCount -= _definition.ProductionRunsPerToolWear;</c>(掛け忘れ)に
    /// 変える変異を当てたところ、<c>Assert.Equal(1, ...ToolWearCount)</c> が実際値4
    /// (7 − 3 = 4。「工具在庫がある間は 0 ≤ ToolWearCount &lt; N」の後条件が破れる)で
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
            recipe, laborPermilleByRank: new[] { 7000, 0, 0 }, productionRunsPerToolWear: 3);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Tools] = 3;

        EconomySystemTestFixtures.RunDays(world, new ProductionSystem(definition), days: 1);

        Assert.Equal(1, world.Households[0].WorkshopInventory[Item.Tools]);
        Assert.Equal(1, world.Households[0].ToolWearCount);
    }

    /// <summary>
    /// 【核心】別表 #31。入力A(数量2・在庫5)と入力B(数量3・在庫15)を持つレシピで
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
    /// 向きが分かれる。GDD02 §5.2 が「切り上げ規約の意図的な例外」と明記した除算なので、
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
}
