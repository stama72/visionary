using Visionary.Sim.Time;

namespace Visionary.Sim;

/// <summary>
/// ドメインイベント(TDD01 §3.4)の追記専用ログ(<see cref="World.EventLog"/>)の1要素。
/// W1 では型の宣言のみ。
/// </summary>
/// <remarks>
/// この平坦な形は W2 で変わる。今の仮の形と正すべき方向は
/// TDD01 §3.6「W1 で置いた仮決め」の表が持つ(ここに複製しない)。
/// </remarks>
public readonly record struct DomainEvent
{
    /// <summary>イベントの種別。TDD01 §3.4 の列挙は未確定のため int のプレースホルダ。</summary>
    public int KindCode { get; init; }

    /// <summary>発生時刻。</summary>
    public Tick At { get; init; }

    /// <summary>主体(例: TrustChanged の from)。</summary>
    public int SubjectId { get; init; }

    /// <summary>相手・対象(例: TrustChanged の to)。</summary>
    public int RelatedId { get; init; }

    /// <summary>種別ごとに意味が変わる汎用の値(例: delta、penalty)。</summary>
    public long Payload { get; init; }
}
