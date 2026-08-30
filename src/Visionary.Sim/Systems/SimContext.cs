using Visionary.Sim.Randomness;
using Visionary.Sim.Time;

namespace Visionary.Sim.Systems;

/// <summary>
/// システムが乱数と時刻に触る唯一の口(TDD01 §3.1)。
/// </summary>
/// <remarks>
/// <see cref="OpenRandom(int)"/> は呼び出し側に系統を選ばせない。常に「今 <see cref="SimScheduler"/>
/// が <see cref="ISimSystem.Step"/> を呼んでいるシステムの <see cref="ISimSystem.Stream"/>」で開く。
/// これにより系統をまたいだ乱数の借用が API として不可能になる(ADR-0002)。
/// </remarks>
public sealed class SimContext
{
    private readonly RandomSource _random;

    // 同一tick内の二重オープン検出(TDD01 §3.1「機械で守れていない残りの危険」)。
    // 列挙はしない(Contains/Addのみ)ので Dictionary/HashSet でも実害は無いが、
    // ADR-0002の「列挙順が不定なコレクションを状態に持たない」規約に揃えておく。
    private readonly SortedSet<int> _openedEntitiesThisTick = new();

    internal SimContext(RandomSource random) => _random = random;

    /// <summary>スケジューラが進める現在tick。</summary>
    public Tick Now { get; internal set; }

    /// <summary>スケジューラが Step の直前に設定する、今実行中のシステムの系統。</summary>
    internal RandomStream CurrentStream { get; set; }

    /// <summary>
    /// 現在実行中のシステムの系統で、<paramref name="entityId"/> の乱数列を開く。
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// 同一tick内で同じ <paramref name="entityId"/> に対して2度目の呼び出しをしたとき。
    /// 同じ組を2度開くと同じ値列が返るため(TDD01 §3.1)。
    /// </exception>
    public RandomSequence OpenRandom(int entityId)
    {
        if (!_openedEntitiesThisTick.Add(entityId))
        {
            throw new InvalidOperationException(
                $"エンティティ {entityId} は tick {Now} で既に乱数を開いている。"
                    + "同一組の乱数列は同じ値を返すため、同一tick内での二重オープンは禁止(TDD01 §3.1)。");
        }

        return _random.Open(CurrentStream, Now, entityId);
    }

    /// <inheritdoc cref="OpenRandom(int)"/>
    public RandomSequence OpenRandom() => OpenRandom(RandomSource.NoEntity);

    /// <summary>tick を進め、二重オープンの記録をクリアする。<see cref="SimScheduler"/> が呼ぶ。</summary>
    internal void AdvanceTo(Tick tick)
    {
        Now = tick;
        _openedEntitiesThisTick.Clear();
    }
}
