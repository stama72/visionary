using Visionary.Sim.Time;

namespace Visionary.Sim;

/// <summary>
/// ドメインイベントの基底(TDD01 §3.4)。種別ごとに派生型を作る。
/// 追記先は <see cref="World.EventLog"/>(ハッシュ対象外。TDD01 §3.8)。
/// </summary>
/// <param name="At">発生時刻。</param>
public abstract record DomainEvent(Tick At);

/// <summary><see cref="PlayerTradeExecuted"/> の売買の向き。</summary>
public enum PlayerTradeKind
{
    SellToHousehold = 1,
    BuyFromHousehold = 2,
    BuyFromWindow = 3,
    SellToWindow = 4,
}   // 0 を使わない(既定値が有効な種別に見えるのを避ける。CommandStatus と同じ)

/// <summary>
/// プレイヤーの売買の約定(Accepted のみ)。値は返す <see cref="Commands.Result"/> と同じ。
/// </summary>
/// <param name="At">約定時刻。</param>
/// <param name="Kind">売買の向き。</param>
/// <param name="CounterpartyId">世帯 Id。窓口なら <see cref="HouseholdState.ExternalMarketSellerId"/>。</param>
/// <param name="ItemId">品目 Id。</param>
/// <param name="Quantity">約定した個数。単位: 個(コマンドの要求数量ではない)。</param>
/// <param name="UnitPrice">単価。単位: 貨幣/個。</param>
/// <param name="Gratitude"><see cref="PlayerTradeKind.SellToHousehold"/> のときだけ。それ以外は null。</param>
/// <param name="NeedId"><see cref="PlayerTradeKind.SellToHousehold"/> のときだけ。それ以外は −1。</param>
public sealed record PlayerTradeExecuted(
    Tick At,
    PlayerTradeKind Kind,
    int CounterpartyId,
    int ItemId,
    int Quantity,
    int UnitPrice,
    Gratitude? Gratitude,
    int NeedId)
    : DomainEvent(At);
