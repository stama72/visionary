using Visionary.Sim.Time;

namespace Visionary.Sim;

/// <summary>
/// ニーズ(GDD01 §3.2 / TDD01 §3.2)。W1 では型の宣言のみで、生成ロジックは持たない。
/// </summary>
/// <remarks>
/// 種別(<see cref="TypeCode"/>)と理由(<see cref="ReasonCode"/>)は GDD01 §3.2 で
/// 「在庫不足/金銭不足/労働力不足/建設意欲/イベント/病気…」のように末尾が「…」で
/// 閉じられておらず、確定した列挙がまだ無い。ここでは int のプレースホルダとして持ち、
/// 区分が確定した時点で専用の enum に差し替える(W2以降)。
/// </remarks>
public readonly record struct Need
{
    /// <summary>ニーズの種別。区分未確定(上記remarks参照)。</summary>
    public int TypeCode { get; init; }

    /// <summary>対象NPC。</summary>
    public int TargetNpcId { get; init; }

    /// <summary>品目。</summary>
    public int ItemId { get; init; }

    /// <summary>数量。</summary>
    public int Quantity { get; init; }

    /// <summary>期限。</summary>
    public Tick Deadline { get; init; }

    /// <summary>緊急度。</summary>
    public int Urgency { get; init; }

    /// <summary>ニーズが生じた理由。区分未確定(上記remarks参照)。</summary>
    public int ReasonCode { get; init; }
}
