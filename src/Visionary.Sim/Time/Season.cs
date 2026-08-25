namespace Visionary.Sim.Time;

/// <summary>季節。1年は<see cref="Spring"/>から始まる(ADR-0003)。</summary>
/// <remarks>
/// 数値は暦の計算(<see cref="GameDate"/>)で年内の季節位置として直接使うため、
/// 順序と値が仕様である。並べ替えたり値を振り直したりしてはならない。
/// 表示名(「春」/ "Spring")はプレゼンテーション層の関心事であり、ここには持たない。
/// </remarks>
public enum Season
{
    Spring = 0,
    Summer = 1,
    Autumn = 2,
    Winter = 3,
}
