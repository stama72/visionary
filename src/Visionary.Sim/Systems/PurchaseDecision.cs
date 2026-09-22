namespace Visionary.Sim.Systems;

/// <summary>購入量が0になった理由(GDD02b §5.2「購入量0の理由」)。</summary>
/// <remarks>
/// <b>店を1つも知らない・売り手の在庫が尽きた はここに入れない。</b>どちらもゲートに
/// 到達しておらず、資金不足でもない(GDD02b §3.3)。Need の側が拾う(#40)。
/// </remarks>
public enum NoPurchaseReason
{
    /// <summary>ゲートは開いている(購入量が0でも、それは到達在庫が予想在庫に届いただけ)。</summary>
    None = 0,

    /// <summary>
    /// 実効価格が ApplyPermille(基礎値, 在庫圧力‰) を超えた。「高すぎて買わなかった」。
    /// <b>名前は相場のままだが、指しているのは基礎値の分岐である</b>(相場項が無い日も、
    /// 基礎値が窓口の当日価格に落ちてこの分岐が立つ。決定11。W2-15 タスク仕様が改名を
    /// 見送った ── 既存の呼び出し側がこの名前を前提にしているため)。
    /// </summary>
    MarketTerm = 1,

    /// <summary>実効価格が利潤上限を超えた(生産の入力にしか起きない)。</summary>
    ProfitCap = 2,

    /// <summary>実効価格が現金上限を超えた。<b>必需ではこれだけが「資金不足」に数えられる</b>(GDD02b §3.2 経路(1))。</summary>
    CashCap = 3,
}

/// <summary>購入量とその理由(段5 が呼ぶ <see cref="BuyerBudget.Decide"/> の戻り値)。</summary>
public readonly record struct PurchaseDecision
{
    /// <summary>購入量。用途の単位(耐久は耐久値)。</summary>
    public int Quantity { get; init; }

    /// <summary><see cref="Quantity"/> が0のときの理由。0でないなら <see cref="NoPurchaseReason.None"/>。</summary>
    public NoPurchaseReason Reason { get; init; }
}
