using Visionary.Sim.Time;

namespace Visionary.Sim;

/// <summary>自分で見たか、聞いた話か(GDD01 §4.1)。</summary>
public enum ObservationSource
{
    /// <summary>自分で見た。</summary>
    Direct = 0,

    /// <summary>人づてに聞いた。</summary>
    Heard = 1,
}

/// <summary>
/// 価格知識(GDD01 §4.1 / TDD01 §3.2)。プレイヤー・NPC 双方が保持する観測記録。
/// W1 では型の宣言のみ。
/// </summary>
public readonly record struct PriceObservation
{
    public int ItemId { get; init; }

    public int LocationId { get; init; }

    public int Price { get; init; }

    /// <summary>観測時刻(鮮度の基準)。</summary>
    public Tick ObservedAt { get; init; }

    public ObservationSource Source { get; init; }
}
