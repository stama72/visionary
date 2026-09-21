namespace Visionary.Sim;

/// <summary>需要の用途(GDD02c §2.1 / GDD02b §3.1)。値の順は宣言の都合であり、意味を持たない。</summary>
/// <remarks>
/// <b>走査順(GDD02b §3.2)を兼ねさせない。</b>実際の走査順は 必需 → 耐久 → 生産の入力 → 嗜好
/// であり、<see cref="Systems.BuyerDemand"/> が<b>並び</b>として持つ。兼ねさせると、後から用途を
/// 1つ足したときに走査順が黙って変わる(タスク仕様)。
/// </remarks>
public enum DemandPurpose
{
    /// <summary>生産の入力。利潤上限を持つ唯一の用途(GDD02c §2.3)。</summary>
    ProductionInput = 0,

    /// <summary>必需の消費。用途に使える資金(母数)は流動資金の全額(GDD02c §2.1)。</summary>
    Necessity = 1,

    /// <summary>嗜好・奢侈の消費。母数は余剰資金(流動資金 − 取り置き − 運転資金、GDD02c §2.1)。</summary>
    Preference = 2,

    /// <summary>耐久の消費。母数は流動資金 − 必需の取り置き(GDD02c §2.1)。</summary>
    Durable = 3,
}
