using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="ProductionSystem"/> と <see cref="ConsumptionSystem"/> を
/// TDD01 §3.3 の順1・順2として1本のパイプラインで走らせたときの検査。
/// </summary>
public sealed class ProductionAndConsumptionPipelineTests
{
    /// <summary>
    /// テスト表 #29。両システムを順1・順2で登録して1日進めると、その日に生産した出力が
    /// 同じ日の消費の対象になっていない(生産は工房在庫、消費は世帯在庫)。
    /// </summary>
    /// <remarks>
    /// 木材加工(木材→薪、入力0件で常に生産できるよう単純化)の世帯を1戸用意し、
    /// 消費表は同じ薪を要求するように仕立てる。生産の出力は工房在庫にしか乗らない ──
    /// <see cref="ConsumptionSystem"/> の通常の消費ループが直接読むのは世帯在庫だけであり、
    /// 同じ日の生産分を横から見ることはできない(下記remarks「W2-17 追随」参照)。
    /// </remarks>
    /// <remarks>
    /// <b>W2-17 追随(2026-09-22)。</b>自家消費(#174)が
    /// <see cref="ConsumptionSystem.Step"/> の先頭(順2の中の最初の一手)に入ったことで、
    /// 「その日の生産分は同じ日の消費の対象にならない」という旧い前提は<b>自分のレシピの
    /// 出力品目については成り立たなくなった</b>。本テストの薪はまさにその出力品目
    /// (Millerを薪の生産者に仕立てている)なので、自家消費が工房在庫から世帯在庫へ1個
    /// (今日の消費量ぶん)を移してから通常の消費が引く。<b>「生産システムの出力は工房在庫に
    /// しか乗らない」という不変条件そのものは変わっていない</b> ── 変わったのは、消費システムの
    /// 中に工房在庫から世帯在庫へ移す専用の経路(自家消費)が新設されたことである。期待値を
    /// 新しい経路の計算どおりに更新した:
    /// 移動量 = min(販売在庫3, max(0, 目標在庫0 + 今日の消費量1 − 世帯在庫0)) = 1 →
    /// 工房在庫 3−1=2、世帯在庫 0+1=1 → 消費 min(1,1)=1 → 世帯在庫0、UnmetConsumption 0。
    /// </remarks>
    [Fact]
    public void ProductionAndConsumptionRunInPipelineOrder()
    {
        const int Firewood = Item.Firewood;

        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Firewood, Quantity = 3 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 1000);

        var master = new int[Item.Count];
        master[Firewood] = 1;
        var journeyman = new int[Item.Count];
        var apprentice = new int[Item.Count];

        var definition = EconomySystemTestFixtures.BuildDefinition(
            recipe,
            laborPermilleByRank: new[] { 1000, 0, 0 },
            dailyConsumptionPerNpcByRank: new[] { master, journeyman, apprentice });

        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Tools] = 1;
        world.Households[0].HouseholdInventory[Firewood] = 0; // 世帯在庫は空(不足が出る設定)
        world.Households[0].WorkshopInventory[Firewood] = 0;

        var scheduler = new SimScheduler(
            new ISimSystem[] { new ProductionSystem(definition), new ConsumptionSystem(definition) },
            new RandomSource(1));
        scheduler.Advance(world, ticks: 24);

        // 生産は実際に走った(工房在庫が増えている)。自家消費(#174)が今日の消費量ぶん(1個)を
        // 世帯在庫へ移すので、工房在庫に残るのは 3−1=2。
        Assert.Equal(2, world.Households[0].WorkshopInventory[Firewood]);

        // 自家消費が移した1個がその日のうちに消費され、世帯在庫は0に戻る。不足は自家消費で
        // まかなわれたので0(旧「不足1」は自家消費が無かった時代の期待値。上記remarks参照)。
        Assert.Equal(0, world.Households[0].HouseholdInventory[Firewood]);
        Assert.Equal(0, world.Households[0].UnmetConsumption[Firewood]);
    }
}
