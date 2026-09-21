using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="DailyConsumption"/>(GDD02b §1 / GDD02b §2 / GDD02d §5、#36 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class DailyConsumptionTests
{
    private static WorldDefinition BuildDefinition(int[] firewoodSeasonPermille, int firewoodQuantity = 2)
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = 0, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 1);

        // 薪の基礎量を親方・徒弟(職人欄も埋める)全階層に置く。GDD08 §9検証項目4と同じ形。
        var consumptionTable = new[]
        {
            FirewoodRow(firewoodQuantity), // Master
            FirewoodRow(firewoodQuantity), // Journeyman(M0は使わないが欄は埋める)
            FirewoodRow(firewoodQuantity), // Apprentice
        };

        return EconomySystemTestFixtures.BuildDefinition(
            recipe,
            dailyConsumptionPerNpcByRank: consumptionTable,
            firewoodConsumptionSeasonPermille: firewoodSeasonPermille);
    }

    private static int[] FirewoodRow(int quantity)
    {
        var row = new int[Item.Count];
        row[Item.Firewood] = quantity;

        return row;
    }

    /// <summary>
    /// 【核心】テスト表 #21。薪・親方+徒弟(各基礎量2)・季節係数{春800,夏400,秋1000,冬2000}、
    /// now = 秋30日(DayIndex 89)、days=3 → 20(秋4 + 冬8 + 冬8)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>Lookahead</c> の日ごとのループを外し、
    /// <c>days * Quantity(...当日の季節...)</c>(合計してから季節係数を掛ける変異、
    /// 「3 × 当日の1日消費量」)に変えたところ、当日(秋、1日消費量4)基準で
    /// <c>Assert.Equal(20, ...)</c> が実際値12(3×4、冬支度が帰結として現れなくなる)で
    /// 失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void LookaheadAddsEachDaySeparatelyWithItsOwnSeason()
    {
        var seasonPermille = new[] { 800, 400, 1000, 2000 }; // Spring, Summer, Autumn, Winter
        var definition = BuildDefinition(seasonPermille);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Apprentice });

        // エポック(Y1春1日)から89日進めると秋30日目(DayIndex 89)。
        var now = Tick.FromDays(89);

        int total = DailyConsumption.Lookahead(
            definition, world, world.Households[0], Item.Firewood, now, days: 3);

        Assert.Equal(20, total);
    }

    /// <summary>
    /// 【核心】テスト表 #22。同じ世界で ConsumptionSystem を1日走らせた実消費量と
    /// Lookahead(days: 1) が一致する。
    /// </summary>
    /// <remarks>
    /// <b>この乖離はどちらの単独テストでも捕まらない。</b>切り出しの目的そのものを検査する。
    /// <para>
    /// <b>変異の実測(2026-09-16)。</b>意図的に <see cref="ConsumptionSystem"/> を複製する形
    /// (<c>DailyConsumption.Quantity</c> を呼ばず、構成員ごとの合計を先に取ってから
    /// <c>ApplyPermille</c> を1回だけ適用する実装)を模して <c>Lookahead</c> 側の計算を
    /// 書き換えたところ、実消費量(構成員ごとに切り上げて合計=2)と Lookahead の結果
    /// (世帯合計1回切り上げ=1)が食い違い、<c>Assert.Equal(actualConsumed, lookaheadTotal)</c>
    /// が「Expected: 2, Actual: 1」で失敗した(赤を確認、基礎量1・季節係数500‰の丸めで
    /// 初めて分岐が現れる)。変異を戻して緑に復帰させた(現在の実装は両者とも
    /// <c>DailyConsumption.Quantity</c> を呼ぶ1つの式なので、この変異は「片方だけ書き換える」
    /// 形でしか再現できない)。
    /// </para>
    /// </remarks>
    [Fact]
    public void LookaheadMatchesWhatConsumptionActuallyEatsForOneDay()
    {
        // 冬の季節係数を500‰にする ── 基礎量1と組み合わせると、構成員ごとに切り上げてから
        // 合計する(2)のと、世帯合計してから1回切り上げる(1)のとで結果が分かれる
        // (端数を作らない偶数の組み合わせでは、この2つの実装が偶然一致してしまう)。
        var seasonPermille = new[] { 1000, 1000, 1000, 500 };
        var definition = BuildDefinition(seasonPermille, firewoodQuantity: 1);

        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Apprentice });
        world.Households[0].HouseholdInventory[Item.Firewood] = 1000;

        // 冬(季節Idx3)へ進める。90日進めると冬1日目。
        EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 90 * 24);

        int before = world.Households[0].HouseholdInventory[Item.Firewood];
        var now = world.Now;

        EconomySystemTestFixtures.RunDays(world, new ConsumptionSystem(definition), days: 1);

        int actualConsumed = before - world.Households[0].HouseholdInventory[Item.Firewood];
        int lookaheadTotal = DailyConsumption.Lookahead(
            definition, world, world.Households[0], Item.Firewood, now, days: 1);

        Assert.Equal(actualConsumed, lookaheadTotal);
    }

    /// <summary>テスト表 #23。days 0 / −1 → 0。</summary>
    [Fact]
    public void LookaheadIsZeroForNonPositiveDays()
    {
        var seasonPermille = new[] { 1000, 1000, 1000, 1000 };
        var definition = BuildDefinition(seasonPermille);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });

        Assert.Equal(
            0,
            DailyConsumption.Lookahead(
                definition, world, world.Households[0], Item.Firewood, world.Now, days: 0));
        Assert.Equal(
            0,
            DailyConsumption.Lookahead(
                definition, world, world.Households[0], Item.Firewood, world.Now, days: -1));
    }
}
