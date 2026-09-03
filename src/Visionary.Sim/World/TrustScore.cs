using Visionary.Sim.Time;

namespace Visionary.Sim;

/// <summary>
/// 信用スコア(GDD01 §2.1)。<see cref="World.TrustLedger"/> の値。
/// </summary>
/// <remarks>
/// GDD01 §2.1 は from/to も含めた4フィールドで定義しているが、from/to は
/// <see cref="World.TrustLedger"/> のキー(<see cref="TrustKey"/>)に既に含まれる。
/// 同じ値を2箇所に持つと更新時に不整合を起こしうるため、ここでは持たせていない。
/// </remarks>
public readonly record struct TrustScore
{
    /// <summary>信用スコア。0〜100(GDD01 §2.1。他の比率係数と異なり千分率ではない)。</summary>
    public int Value { get; init; }

    /// <summary>最後に接触したtick。</summary>
    public Tick LastMet { get; init; }
}
