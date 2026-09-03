using Visionary.Sim.Randomness;

namespace Visionary.Sim.Systems;

/// <summary>
/// スケジューラに登録する1システム(TDD01 §3.1)。
/// </summary>
/// <remarks>
/// <see cref="Stream"/> を持たせるのが要点。システムの識別子を系統そのものにすることで、
/// <see cref="SimContext"/> は「今動いているシステムの系統」しか開けなくなり、
/// 系統をまたいだ乱数の借用が API として不可能になる(ADR-0002 の規約を規律ではなく型で守る)。
/// </remarks>
public interface ISimSystem
{
    /// <summary>このシステムが属する乱数の系統。<see cref="SimScheduler"/> は系統の重複登録を拒否する。</summary>
    RandomStream Stream { get; }

    Cadence Cadence { get; }

    void Step(World world, SimContext context);
}
