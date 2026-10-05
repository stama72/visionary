namespace Visionary.Sim.Dialogue;

/// <summary>台詞の種類(GDD01 §3.3.1 の表の行)。</summary>
/// <remarks>0 を使わない理由は <see cref="NeedType"/> と同じ。</remarks>
public enum LineKind
{
    Greeting = 1,         // 挨拶
    SmallTalk = 2,        // 世間話
    NeedDisclosure = 3,   // 開示
    NoNeed = 4,           // 困りごとは無い
    TradeAccepted = 5,    // 取引の成立
    TradeRefused = 6,     // 取引の不成立
}

/// <summary>性格(GDD01 §3.3)。M0 の資料はどれも使わない(GDD01 §3.3.1)。0 を使わない。</summary>
public enum Personality
{
    Taciturn = 1,    // 寡黙
    Talkative = 2,   // 饒舌
    Servile = 3,     // 卑屈
    Haughty = 4,     // 尊大
}
