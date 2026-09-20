using Visionary.Sim.Numerics;

namespace Visionary.Sim.Systems;

/// <summary>実効価格(GDD06 §2 / GDD01 §2.2 効果1)。<b>純関数。</b></summary>
public static class EffectivePrice
{
    /// <summary>
    /// 実効価格 = ApplyPermille( 提示価格 , 1000 − CeilDiv( α‰ × 信用 , 100 ) )(GDD06 §2)。
    /// 単位: 貨幣/1単位。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>内側を <see cref="IntegerMath.CeilDiv(int, int)"/> にするのは
    /// <see cref="BuyerBudget.StockPressurePermille"/>(<c>1500 − CeilDiv(在庫比‰, 2)</c>)と
    /// 同じ形である。</b>係数全体としては切り下げ方向に寄る(割引を甘く見積もらない)。
    /// </para>
    /// <para>
    /// <b>W2 では信用が常に0なので、実効価格は提示価格に等しい。</b>それでも式を関数として
    /// 置くのは、[#44](https://github.com/stama72/visionary/issues/44) が信用を配線するときに
    /// 呼び出し側を探し回らずに済ませるためである。
    /// </para>
    /// <para>
    /// <b>戻り値は、M0 の値域(<paramref name="trustDiscountPermille"/> ≤ 1000)では通常1以上になる。</b>
    /// <see cref="IntegerMath.ApplyPermille(int, int)"/> が <c>CeilDiv</c> なので、正の提示価格に
    /// 正の係数を掛けて0にはならない ── これが <see cref="BuyerBudget.Decide"/> /
    /// <see cref="TradeSettlement.FundsCap"/>(どちらも0以下で投げる)の前提を満たしている。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="trust"/> が0〜100の外、または <paramref name="offerPrice"/> が0以下。
    /// </exception>
    public static int Calculate(int offerPrice, int trust, int trustDiscountPermille)
    {
        if (offerPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offerPrice), offerPrice, "提示価格は1以上(GDD06 §2)。");
        }

        if (trust is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(trust), trust, "信用は0〜100(GDD01 §2.1)。");
        }

        // 中間の積はlong(α‰×信用はintを超えうる)。
        long discountNumerator = (long)trustDiscountPermille * trust;
        int discountPermille = checked((int)IntegerMath.CeilDiv(discountNumerator, 100));
        int coefficientPermille = checked(IntegerMath.PermilleScale - discountPermille);

        return IntegerMath.ApplyPermille(offerPrice, coefficientPermille);
    }
}
