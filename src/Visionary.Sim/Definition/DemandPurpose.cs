namespace Visionary.Sim;

/// <summary>需要の用途(GDD02 §8.2.1)。値は §8.2.1 の表の順。</summary>
/// <remarks>
/// <b>走査順(GDD02 §6.2.1)を兼ねさせない。</b>実際の走査順は 必需 → 生産の入力 → 耐久 → 嗜好
/// であり、<see cref="Systems.BuyerDemand"/> が<b>並び</b>として持つ。兼ねさせると、後から用途を
/// 1つ足したときに走査順が黙って変わる(タスク仕様)。
/// </remarks>
public enum DemandPurpose
{
    /// <summary>生産の入力。基礎値は派生需要(GDD02 §8.2.1)。</summary>
    ProductionInput = 0,

    /// <summary>必需の消費。基礎値は 相場基準 × 許容乖離‰。</summary>
    Necessity = 1,

    /// <summary>嗜好・奢侈の消費。基礎値は 余剰資金 × 予算比率‰。</summary>
    Preference = 2,

    /// <summary>耐久の消費。基礎値は min(相場基準, 流動資金 × 予算比率‰)。</summary>
    Durable = 3,
}
