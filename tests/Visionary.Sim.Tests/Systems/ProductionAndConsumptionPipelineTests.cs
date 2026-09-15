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
    /// 消費表は同じ薪を要求するように仕立てる。生産の出力が工房在庫にしか乗らない以上、
    /// 世帯在庫は生産開始前の値のままのはずで、消費はその値だけを見て不足を記録する。
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

        // 生産は実際に走った(工房在庫が増えている)。
        Assert.Equal(3, world.Households[0].WorkshopInventory[Firewood]);

        // しかしその日に生産した薪は、同じ日の消費の対象になっていない ──
        // 世帯在庫は0のままで、不足がそのまま記録される。
        Assert.Equal(0, world.Households[0].HouseholdInventory[Firewood]);
        Assert.Equal(1, world.Households[0].UnmetConsumption[Firewood]);
    }
}
