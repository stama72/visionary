using Visionary.Sim.Time;

namespace Visionary.Sim;

/// <summary>
/// ドメインイベント(TDD01 §3.4)の追記専用ログ(<see cref="World.EventLog"/>)の1要素。
/// W1 では型の宣言のみ。
/// </summary>
/// <remarks>
/// TDD01 §3.4 が列挙するイベント(<c>TradeExecuted</c>, <c>TrustChanged{from,to,delta,reason}</c>,
/// <c>NeedCreated</c>, <c>PromiseBroken{e,penalty}</c>, <c>UnfairPriceSuspected{trade,detectorObservation}</c>,
/// <c>PenaltyApplied</c> など)は種類ごとにペイロードの形が異なり、一覧も「など」で閉じていない。
/// W1には経済システム(Production〜Rumor)がまだ無く実際に発行される経路も無いため、
/// <see cref="World.EventLog"/> が型として成立する最小限として、種別コード + 汎用ペイロード
/// (int/long)のフラットな形を仮に置く。イベントごとの正式なフィールド構成は、
/// それらを実際に発行するシステムと合わせて設計し直す(W2以降)。
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
