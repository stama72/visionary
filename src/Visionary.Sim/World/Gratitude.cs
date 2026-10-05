namespace Visionary.Sim;

/// <summary>
/// 感謝の段階(GDD01 §3.6)。取引の成立の台詞(<see cref="Dialogue.LineKind.TradeAccepted"/>)の
/// 照合軸に使う。段階の計算はシム側(#253)が持ち、台詞側は引数で受け取るだけである。
/// </summary>
/// <remarks>0 を使わない理由は <see cref="NeedType"/> と同じ。</remarks>
public enum Gratitude
{
    Reluctant = 1,       // 渋々
    Grateful = 2,        // 感謝
    DeepGratitude = 3,   // 深い感謝
}
