using Visionary.Sim.Time;

namespace Visionary.Sim;

/// <summary>
/// 約束(クエスト)の状態(GDD01 §2.8)。有効/延長中/完了/破棄の4状態は
/// GDD01 §2.8「状態遷移のルール」が閉じた一覧として与えているため enum 化した。
/// </summary>
public enum PromiseState
{
    /// <summary>有効。</summary>
    Active = 0,

    /// <summary>延長中。期限を最大1.25L(t0起点)まで延長した状態。</summary>
    Extended = 1,

    /// <summary>完了。</summary>
    Completed = 2,

    /// <summary>破棄(謝罪破棄・自動破棄のいずれも含む)。</summary>
    Discarded = 3,
}

/// <summary>
/// 約束(クエスト、GDD01 §2.8 / TDD01 §3.2)。W1 では型の宣言のみで、
/// 信用変化の計算式(GDD01 §2.8)は持たない。
/// </summary>
/// <remarks>
/// <see cref="NeedIndex"/> は暫定。<see cref="Need"/> 自体がまだ Id を持たないため、
/// 生成時点の <see cref="World.Needs"/> の添字を仮に指す。Need の参照方式は
/// Need に Id を持たせるかどうかと合わせて W2 で確定する。
/// </remarks>
public readonly record struct Promise
{
    /// <summary>元になった Need への参照(上記remarks参照)。</summary>
    public int NeedIndex { get; init; }

    /// <summary>約束した日付(t0、GDD01 §2.8)。</summary>
    public Tick T0 { get; init; }

    /// <summary>期限の日付(t1、GDD01 §2.8)。</summary>
    public Tick T1 { get; init; }

    /// <summary>基本値(B、GDD01 §2.8)。ニーズの緊急度・規模に比例、5〜20。</summary>
    public int B { get; init; }

    public PromiseState State { get; init; }
}
