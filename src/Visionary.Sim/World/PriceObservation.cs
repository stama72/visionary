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
/// <b>所有者は個人である</b> — 「その人が何を見たか」に依存するため、
/// <see cref="World.Knowledge"/> は NpcId 別に持つ(TDD01 §3.2 / GDD08 §2.2)。
/// </summary>
public readonly record struct PriceObservation
{
    public int ItemId { get; init; }

    public int LocationId { get; init; }

    public int Price { get; init; }

    /// <summary>
    /// 誰の売り値を見たか。<b>世帯 Id である</b>(TDD01 §3.2「売り手は世帯である」)。NpcId ではない。
    /// </summary>
    /// <remarks>
    /// GDD01 §4.1 の定義に売り手の欄は無いが、GDD02c §1.2「売り手ごとに最新の1件」と
    /// GDD06 §3.1「有効な記憶」の両方が売り手の同定を要求する(TDD01 §3.6)。
    /// 都市外市場の窓口で観測した場合は <see cref="HouseholdState.ExternalMarketSellerId"/>。
    /// </remarks>
    public int SellerId { get; init; }

    /// <summary>観測時刻(鮮度の基準)。</summary>
    public Tick ObservedAt { get; init; }

    public ObservationSource Source { get; init; }
}
