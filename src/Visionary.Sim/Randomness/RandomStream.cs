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
}
