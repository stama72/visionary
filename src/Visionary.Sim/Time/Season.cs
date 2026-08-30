namespace Visionary.Sim.Time;

/// <summary>季節。1年は<see cref="Spring"/>から始まる。</summary>
/// <remarks>
/// <para>
/// 数値は暦の計算(<see cref="GameDate"/>)で年内の季節位置として直接使うため、
/// 順序と値が仕様である。並べ替えたり値を振り直したりしてはならない。
/// </para>
/// <para>
/// この順序と割り当ての仕様は <c>docs/03-gdd/03-seasons-and-city.md</c> §1.2 が持つ。
/// 変えたい場合はそちらを改訂してからコードを直す。選定理由は ADR-0003。
/// </para>
/// <para>
/// 表示名(「春」/ "Spring")はプレゼンテーション層の関心事であり、ここには持たない。
/// </para>
/// </remarks>
public enum Season
{
    Spring = 0,
    Summer = 1,
    Autumn = 2,
    Winter = 3,
}
