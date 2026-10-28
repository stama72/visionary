namespace Visionary.Sim.Commands;

/// <summary>コマンドの結果の種別(TDD01 §3.4)。0 を使わない(既定値が有効な結果に見えるのを避ける)。</summary>
public enum CommandStatus
{
    Accepted = 1,
    InvalidArgument = 2,      // Id が範囲外・数量 < 1・価格 < 1
    InTransit = 3,            // 移動中
    OutsideTradingHours = 4,  // 6〜17時の外(移動には掛けない)
    NotPresent = 5,           // 居場所が相手の世帯の区画(窓口なら中心)でない
    AlreadyThere = 6,         // 移動: 行き先が今の居場所
    NoOffer = 7,              // 世帯から買う: Market にその (品目, 世帯) の提示価格が無い
    SellerOutOfStock = 8,     // 世帯から買う: 販売在庫が 0
    InsufficientFunds = 9,    // 買う: 1個も払えない
    NotEnoughGoods = 10,      // 売る: 所持品がその品目を 0 個しか持たない
    WindowDoesNotBuy = 11,    // 窓口へ売る: 1次産品(外部買値を持たない)
    NoNeed = 12,              // 世帯へ売る: PlayerSaleOutcome.NoNeed
    AlreadyOfferedToday = 13, // 世帯へ売る: PlayerSaleOutcome.AlreadyOfferedToday
    OverBudget = 14,          // 世帯へ売る: PlayerSaleOutcome.OverBudget
}

/// <summary>コマンドの結果(TDD01 §3.4)。</summary>
public sealed record Result
{
    public CommandStatus Status { get; init; }

    /// <summary>約定した個数。単位: 個。Accepted の売買以外は 0。</summary>
    public int Quantity { get; init; }

    /// <summary>約定単価。単位: 貨幣/個。Accepted の売買以外は 0。</summary>
    public int UnitPrice { get; init; }

    /// <summary>Quantity × UnitPrice。単位: 貨幣。Accepted の売買以外は 0。</summary>
    public int Payment { get; init; }

    /// <summary>世帯へ売って Accepted のときだけ。</summary>
    public Gratitude? Gratitude { get; init; }

    /// <summary>世帯へ売る: <see cref="Systems.PlayerSaleResult.NeedId"/> をそのまま。</summary>
    public int NeedId { get; init; } = -1;

    /// <summary>Need を聞く: Accepted のときだけ。</summary>
    public IReadOnlyList<Need> DisclosedNeeds { get; init; } = Array.Empty<Need>();

    /// <summary>移動: Accepted のときだけ。</summary>
    public Time.Tick? ArrivalAt { get; init; }
}
