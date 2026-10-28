using Visionary.Sim.Metrics;

namespace Visionary.Sim.Systems;

/// <summary>
/// システムの登録順(TDD01 §3.3 / §3.5)。Runner と <see cref="SimHost"/> が同じ組み立てを使う。
/// </summary>
public static class SimPipeline
{
    /// <summary>
    /// TDD01 §3.3 の登録順(順1〜順5、順10)。順6〜順9(Promise / Trust /
    /// UnfairPriceDetection / Rumor)は W3・W4 で実装されるまで存在しないので登録しない。
    /// 順0(OpportunityCost)は M0 ではシステムを持たない(同節)。
    /// </summary>
    public static ISimSystem[] Build(WorldDefinition definition, IDailyMetricsSink sink) => new ISimSystem[]
    {
        new ProductionSystem(definition),
        new ConsumptionSystem(definition),
        new HouseholdSystem(definition),
        new NeedGenerationSystem(definition),
        new TradePlanningSystem(definition),
        new TradeSystem(definition),
        new MetricsSystem(definition, sink),
    };
}
