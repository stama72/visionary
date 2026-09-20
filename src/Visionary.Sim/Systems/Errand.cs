using Visionary.Sim.Numerics;

namespace Visionary.Sim.Systems;

/// <summary>
/// 外出の純関数(GDD06 §2・§3)。<see cref="World"/> も <see cref="WorldDefinition"/> も
/// 受け取らない ── <see cref="BuyerBudget"/> / <see cref="OfferPrice"/> と同じ切り出し方で、
/// 式を単体で試験できる形にする。
/// </summary>
public static class Errand
{
    /// <summary>往復移動時間。単位: 時間。距離 × 1区画あたりの移動時間 × 2(GDD02 §4.3 / GDD06 §2)。</summary>
    public static int TravelHours(int fromDistrictId, int toDistrictId, int hoursPerDistrict)
    {
        int distance = District.Distance(fromDistrictId, toDistrictId);

        return checked(distance * hoursPerDistrict * 2);
    }

    /// <summary>
    /// 外出の費用 = 往復移動時間 × 委託先の機会費用。単位: 貨幣(GDD06 §2)。
    /// <b>外出1回につき1度だけ課す固定費。</b>旧の「目標在庫で割り戻して単価へ足す」は
    /// 戻らない ── 引数が2つしか無いことがその歯止めである。
    /// </summary>
    public static int Cost(int travelHours, int errandOpportunityCostPerHour) =>
        checked(travelHours * errandOpportunityCostPerHour);

    /// <summary>
    /// 外出の労働損失‰ = CeilDiv( 委託先の労働力係数‰ × 往復移動時間 , T )(GDD02a §2 / GDD06 §2)。
    /// <b>分母は可処分時間 T(<see cref="WorldDefinition.DisposableHours"/>)であって24ではない。</b>
    /// </summary>
    /// <remarks>
    /// <b>外出ごとに切り上げてから合計する</b>のが呼び出し側の責務である
    /// (<c>Σ_d CeilDiv(…)</c>。<c>CeilDiv(…, Σ_d …)</c> ではない。GDD06 §3)。
    /// 内側を <see cref="IntegerMath.CeilDiv(int, int)"/> にするのは、損失を切り下げると
    /// 存在しない労働力が生産に残ってしまうため(<see cref="BuyerBudget.StockPressurePermille"/>と
    /// 同じ「実在しない余剰を切り捨てない」考え方)。
    /// </remarks>
    public static int LaborLossPermille(int delegateLaborPermille, int travelHours, int disposableHours)
    {
        // 中間の積はlong(労働力係数‰×往復移動時間はintを超えうる)。
        long numerator = (long)delegateLaborPermille * travelHours;

        return checked((int)IntegerMath.CeilDiv(numerator, disposableHours));
    }

    /// <summary>
    /// 余剰 = w ≤ p ? 0 : FloorDiv( q × (w − p) , 2 )(GDD06 §3)。
    /// </summary>
    /// <remarks>
    /// <b>負を返さない。</b><paramref name="willingness"/> が <paramref name="price"/> 以下なら
    /// 先に0を返し、<see cref="IntegerMath.FloorDiv(int, int)"/> に負の被除数を渡さない ──
    /// 負の <c>FloorDiv</c> は−∞方向へ丸まるため、渡すと丸め誤差が「損をする外出」として
    /// 他の品目の余剰から差し引かれてしまう(GDD06 §3)。
    /// <para>中間の積は <see cref="long"/>。<c>quantity × (willingness − price)</c> は
    /// int を超えうる。</para>
    /// </remarks>
    public static int Surplus(int quantity, int willingness, int price)
    {
        if (willingness <= price)
        {
            return 0;
        }

        long product = (long)quantity * (willingness - price);

        return checked((int)IntegerMath.FloorDiv(product, 2));
    }
}
