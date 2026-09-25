namespace Visionary.Sim.Randomness;

/// <summary>
/// 乱数の系統(ADR-0002 論点2)。系統をまたいで乱数を借用しない。
/// </summary>
/// <remarks>
/// 値は鍵の導出(<see cref="RandomSource"/>)に直接使うため仕様である。振り直してはならない。
/// 0 を使わないのは、既定値の <see cref="RandomStream"/> が有効な系統に見えるのを避けるため。
/// </remarks>
public enum RandomStream
{
    WorldGen = 1,
    Production = 2,
    Consumption = 3,
    Household = 4,
    NeedGeneration = 5,
    Trade = 6,
    Promise = 7,
    Trust = 8,
    UnfairPrice = 9,
    Rumor = 10,
    Dialogue = 11,

    // 12(OpportunityCost)は v1.0 まで予約(TDD01 §3.3)。M0はOpportunityCostのシステムを
    // 持たないので飛ばす ── 値は末尾に足すのであって、パイプラインの順(順0)に合わせて
    // 振り直さない。

    /// <summary>
    /// 順10 Metrics(TDD01 §3.3 / §4.2)。乱数は引かないが、登録(<see cref="Systems.SimScheduler"/>
    /// の系統の重複登録の検査)に一意な識別子が要る(W2-20 タスク仕様)。
    /// </summary>
    Metrics = 13,
}
