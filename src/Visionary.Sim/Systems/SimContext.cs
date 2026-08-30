using Visionary.Sim.Randomness;
using Visionary.Sim.Time;

namespace Visionary.Sim.Systems;

/// <summary>
/// システムが乱数と時刻に触る唯一の口(TDD01 §3.1)。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OpenRandom(int)"/> は呼び出し側に系統を選ばせない。常に「今 <see cref="SimScheduler"/>
/// が <see cref="ISimSystem.Step"/> を呼んでいるシステムの <see cref="ISimSystem.Stream"/>」で開く。
/// これにより系統をまたいだ乱数の借用が<b>アセンブリの外からは</b>できなくなる(ADR-0002)。
/// </para>
/// <para>
/// <b>ただしアセンブリ内からは防げない。</b>実際のシステムは <c>Visionary.Sim</c> 内に置かれ、
/// そこからは <c>internal</c> が素通しなので <see cref="CurrentStream"/> や <see cref="Now"/> を
/// 書き換えて他系統の列を開けてしまう。この型が <c>class</c> である以上、
/// システムが <c>context</c> を保持して <see cref="ISimSystem.Step"/> の外から呼ぶ経路も残る。
/// 「API として不可能」ではなく「正しい使い方を1つだけ楽にする」装置である
/// (TDD01 §3.1「機械で守れていない残りの危険」)。
/// </para>
/// </remarks>
public sealed class SimContext
{
    private readonly RandomSource _random;

    // 同一tick内の二重オープン検出(TDD01 §3.1「機械で守れていない残りの危険」)。
    //
    // キーは (系統, entityId) の組である。乱数の鍵は (シード, 系統, tick, エンティティ) の
    // 4つ組なので、Production が NPC#7 を開くのと Consumption が NPC#7 を開くのは
    // 別の組・別の値列であり、まったく正当な使い方になる(TDD01 §3.3 の日次フェーズは
    // 同一tickに複数システムが走る)。entityId だけを鍵にすると、1tickで最初の
    // 1システムしか乱数を引けなくなる。
    //
    // 列挙はしない(Add のみ)ので Dictionary/HashSet でも実害は無いが、
    // ADR-0002の「列挙順が不定なコレクションを状態に持たない」規約に揃えておく。
    private readonly SortedSet<(RandomStream Stream, int EntityId)> _openedThisTick = new();

    internal SimContext(RandomSource random) => _random = random;

    /// <summary>スケジューラが進める現在tick。</summary>
    public Tick Now { get; internal set; }

    /// <summary>スケジューラが Step の直前に設定する、今実行中のシステムの系統。</summary>
    internal RandomStream CurrentStream { get; set; }

    /// <summary>
    /// 現在実行中のシステムの系統で、<paramref name="entityId"/> の乱数列を開く。
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// 同一tick内で、同じ系統・同じ <paramref name="entityId"/> の組を2度開いたとき。
    /// 同じ組を2度開くと同じ値列が返るため(TDD01 §3.1)。
    /// 系統が違えば別の組なので、複数システムが同一tickに同じエンティティを開くのは正当。
    /// </exception>
    public RandomSequence OpenRandom(int entityId)
    {
        if (!Enum.IsDefined(CurrentStream))
        {
            throw new InvalidOperationException(
                "システムの Step の外で乱数を開こうとしている。"
                    + "SimContext を保持して後から使うことはできない(TDD01 §3.1)。");
        }

        if (!_openedThisTick.Add((CurrentStream, entityId)))
        {
            throw new InvalidOperationException(
                $"系統 {CurrentStream} のエンティティ {entityId} は tick {Now} で既に乱数を開いている。"
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
        _openedThisTick.Clear();
    }

    /// <summary>
    /// <see cref="ISimSystem.Step"/> を抜けた後に系統を無効値へ戻す。<see cref="SimScheduler"/> が呼ぶ。
    /// </summary>
    /// <remarks>
    /// システムが <c>context</c> を保持して Step の外から <see cref="OpenRandom(int)"/> を
    /// 呼んだときに、直前のシステムの系統で静かに開いてしまうのを防ぐ。
    /// アセンブリ内からの書き換えは防げないが、事故のうち最も起きやすい形は塞げる。
    /// </remarks>
    internal void ClearCurrentStream() => CurrentStream = default;
}
