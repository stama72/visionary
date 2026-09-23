using Visionary.Sim.Time;

namespace Visionary.Sim;

/// <summary>Need の種別(GDD02b §8 の表の左2列)。</summary>
/// <remarks>
/// <b>0 を使わない。</b>既定値が有効な種別に見えると <c>default(Need)</c> が本物の Need として
/// 通ってしまう(<see cref="Randomness.RandomStream"/> / <see cref="Determinism.StateHasher"/> の
/// 区分タグと同じ規律)。
/// </remarks>
public enum NeedType
{
    StockShortage = 1,   // 在庫不足
    MoneyShortage = 2,   // 金銭不足
    LaborShortage = 3,   // 労働力不足
}

/// <summary>Need の理由コード(GDD02b §8 の表の右列)。</summary>
/// <remarks>0 を使わない理由は <see cref="NeedType"/> と同じ。</remarks>
public enum NeedReason
{
    ProductionStopped = 1,        // 生産停止
    Distress = 2,                 // 困窮
    CannotExpandProduction = 3,   // 増産できない
    ToolsExhausted = 4,           // 工具切れ
    DistantStock = 5,             // 遠方在庫
}

/// <summary>
/// ニーズ(GDD01 §3.2 / GDD02b §8 / TDD01 §3.2)。<see cref="Systems.NeedGenerationSystem"/>
/// が毎日組み直す。
/// </summary>
public readonly record struct Need
{
    /// <summary>非負。<see cref="World.NextNeedId"/> が払い出す。失効した Id は再利用しない。</summary>
    public int Id { get; init; }

    public NeedType TypeCode { get; init; }

    /// <summary>
    /// 不足している主体。世帯である理由は TDD01 §3.2「経済主体は世帯である」が持つ。
    /// </summary>
    public int TargetHouseholdId { get; init; }

    /// <summary>品目。</summary>
    public int ItemId { get; init; }

    /// <summary>
    /// 数量。単位は <see cref="ReasonCode"/> による(GDD02b §8.1)。<b><see cref="NeedReason.CannotExpandProduction"/>
    /// だけ ‰人日、他の4理由は個。</b>型は <c>int</c> のままなので、取り違えても例外は出ない
    /// (<see cref="Systems.BuyerBudget.QuantityInUnits"/> の耐久と同じ注意)。
    /// </summary>
    public int Quantity { get; init; }

    /// <summary>期限。M0 は <see cref="Tick.Zero"/> 固定(値を入れる規則は本タスクのスコープ外)。</summary>
    public Tick Deadline { get; init; }

    /// <summary>緊急度。M0 は0固定(値を入れる規則は本タスクのスコープ外)。</summary>
    public int Urgency { get; init; }

    /// <summary>ニーズが生じた理由。</summary>
    public NeedReason ReasonCode { get; init; }

    /// <summary>
    /// 理由コードから種別を引く(GDD02b §8 の表)。対応は1対3を含む(在庫不足に3理由)ため、
    /// 実装の各所で手で綴らせない ── 手で綴ると表に無い組(例: 金銭不足/工具切れ)が静かに作れる。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="reason"/> が未知の値のとき。</exception>
    public static NeedType TypeOf(NeedReason reason) => reason switch
    {
        NeedReason.ProductionStopped => NeedType.StockShortage,
        NeedReason.Distress => NeedType.MoneyShortage,
        NeedReason.CannotExpandProduction => NeedType.LaborShortage,
        NeedReason.ToolsExhausted => NeedType.StockShortage,
        NeedReason.DistantStock => NeedType.StockShortage,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "未知の理由コード(GDD02b §8)。"),
    };
}
