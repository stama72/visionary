using Visionary.Sim.Time;

namespace Visionary.Sim;

/// <summary>
/// ニーズ(GDD01 §3.2 / TDD01 §3.2)。W1 では型の宣言のみで、生成ロジックは持たない。
/// </summary>
/// <remarks>
/// <see cref="TypeCode"/> と <see cref="ReasonCode"/> は W2 で変わる。今の仮の形と
/// 正すべき方向は TDD01 §3.6「W1 で置いた仮決め」の表が持つ(ここに複製しない)。
/// </remarks>
public readonly record struct Need
{
    /// <summary>ニーズの種別。区分未確定(上記remarks参照)。</summary>
    public int TypeCode { get; init; }

    /// <summary>
    /// 不足している主体。世帯である理由は TDD01 §3.2「経済主体は世帯である」が持つ。
    /// </summary>
    public int TargetHouseholdId { get; init; }

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
