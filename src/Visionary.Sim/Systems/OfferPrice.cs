using Visionary.Sim.Numerics;

namespace Visionary.Sim.Systems;

/// <summary>
/// 提示価格の式(GDD02c §1)。<b>純関数のみ。</b>
/// <see cref="World"/> も <see cref="WorldDefinition"/> も受け取らない ── 式を単体で
/// 試験できる形に切り出すのが目的で、<see cref="TradeSystem"/> が世帯を走査しながらこれを呼ぶ。
/// </summary>
public static class OfferPrice
{
    private const int MaxCoefficientPermille = 1500; // ‰。在庫0のときの上限(GDD02c §1.1)
    private const int MinCoefficientPermille = 500;  // ‰。溢れているときの下限(同)

    /// <summary>破産中の売り手の価格係数‰(GDD02c §1.4)。在庫を見ずに固定する。</summary>
    /// <remarks>
    /// <c>BankruptCoefficientPermille</c> と <see cref="MinCoefficientPermille"/> は同じ500だが、
    /// 別の定数として置く。値の由来が同じであることは、一方を動かしたら他方も動くという関係を
    /// 意味しない ── 1つの定数にすると#28(投げ売りの深さ)が§1.1のclampと一緒にしか動かせない
    /// (#96 が <c>BankruptFloorPermille</c> を別定数に置いたのと同じ理由)。
    /// </remarks>
    private const int BankruptCoefficientPermille = 500; // ‰

    /// <summary>
    /// 売れなかった日の価格係数‰の上限(GDD02c §1.1「売れなかった日は値上げしない」)。
    /// </summary>
    /// <remarks>
    /// 1000は「相場どおり」の意味の値であり、係数の表の中点(<see cref="MinCoefficientPermille"/>と
    /// <see cref="MaxCoefficientPermille"/>の中間)と同じ値であることは連動を意味しない ──
    /// <see cref="BankruptCoefficientPermille"/> の remarks と同じ理由で別定数として置く。
    /// </remarks>
    private const int UnsoldCapPermille = 1000; // ‰

    /// <summary>在庫比‰ = CeilDiv(1000 × 販売在庫, 出荷目標在庫)。</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="shipmentTargetStock"/> が0以下。</exception>
    public static int StockRatioPermille(int sellableStock, int shipmentTargetStock)
    {
        // WorldDefinitionのコンストラクタが既に0以下を拒んでいるが、純関数の側でも拒む ──
        // ゼロ除算がDivideByZeroExceptionとして出ると、どの表の欄が空欄なのか分からない。
        if (shipmentTargetStock <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(shipmentTargetStock), shipmentTargetStock, "出荷目標在庫は1以上(GDD02c §1.1)。");
        }

        // 1000 × 販売在庫はlongで持つ。販売在庫が214万を超えるとintで桁あふれする。
        long scaledStock = (long)IntegerMath.PermilleScale * sellableStock;

        return checked((int)IntegerMath.CeilDiv(scaledStock, shipmentTargetStock));
    }

    /// <summary>価格係数‰ = clamp(1500 − CeilDiv(在庫比‰, 2), 500, 1500)。</summary>
    /// <remarks>
    /// <b>CeilDiv(在庫比‰, 2) の切り上げは意図的であり、式全体としては切り下げ方向になる。</b>
    /// GDD02c §1.1 の註が「品薄側へ偏るほうが設計意図に沿う」として、切り上げ規約を在庫比‰ の
    /// 算出にだけ適用し係数全体には及ぼさないと明記している。ここを FloorDiv に揃える変異は
    /// 善意から起こりうる。
    /// </remarks>
    public static int PriceCoefficientPermille(int stockRatioPermille)
    {
        int lowered = MaxCoefficientPermille - IntegerMath.CeilDiv(stockRatioPermille, 2);

        return Math.Clamp(lowered, MinCoefficientPermille, MaxCoefficientPermille);
    }

    /// <summary>
    /// 提示価格 = max( 床, ApplyPermille(相場基準, 価格係数‰) )(GDD02c §1)。
    /// <b>床は外部買値である</b> ── 原価は値付けに入らない。
    /// <b>破産中(isBankrupt == 1)は価格係数‰ を 500 に固定し、在庫比を評価しない</b>(§1.4)。
    /// <b>床は破らない</b> ── 半値が床を下回るなら床で並べる。
    /// <b>前日に自分の約定が無い日(hasSettledYesterday == false)は価格係数‰ を 1000 で
    /// 頭打ちにする(§1.1)。</b>判定は呼び出し側が品目ごとに帳簿から求める(このメソッドは
    /// 品目や母数を知らない)。
    /// </summary>
    /// <remarks>
    /// 相場基準が立たない日は呼ばない ── 呼び出し側が床をそのまま提示価格にする(§1)。
    /// <b>この頭打ちで帯が保たれるわけではない。</b>保証するのは「約定が無い日に相場より上へ
    /// 出さない」ことだけで、完売が続く売り手には効かない(§1.2 の囲み)。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="isBankrupt"/> が0/1以外。</exception>
    public static int Calculate(
        int floorPrice,
        int marketReference,
        int sellableStock,
        int shipmentTargetStock,
        int isBankrupt,
        bool hasSettledYesterday)
    {
        // "!=0" ではなく値域そのものを閉じる。HouseholdState.IsBankrupt のsetterと同じ理由 ──
        // 2 が破産扱いになる実装と、== 1 で書いた実装が食い違うのを入口で防ぐ。
        if (isBankrupt is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(isBankrupt), isBankrupt, "破産中フラグは 0 / 1(GDD02c §1.4)。");
        }

        int coefficientPermille;

        if (isBankrupt == 1)
        {
            // 破産中は500‰固定が先に効くので、§1.1の頭打ちは何もしない(min(500,1000)=500と
            // 同じ結果になるが、意味としては「評価するまでもない」)。在庫比も評価しない ──
            // 在庫比を評価してから係数を捨てる書き方だと「出荷目標在庫 <= 0」の検査が
            // 破産中だけ効くという非対称が入る。
            coefficientPermille = BankruptCoefficientPermille;
        }
        else
        {
            coefficientPermille = PriceCoefficientPermille(StockRatioPermille(sellableStock, shipmentTargetStock));

            if (!hasSettledYesterday)
            {
                // minであって代入ではない ── 溢れている売り手の500‰はそのまま働く
                // (§1.1「値下げ側には効かない」)。
                coefficientPermille = Math.Min(coefficientPermille, UnsoldCapPermille);
            }
        }

        int referencePrice = IntegerMath.ApplyPermille(marketReference, coefficientPermille);

        return Math.Max(floorPrice, referencePrice);
    }

    /// <summary>
    /// その日、§1.1 の「売れなかった日は値上げしない」が実際に効いたか(W2-20 タスク仕様)。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="UnsoldCapPermille"/> を再定義しない。</b>同じクラスに置くのは、1000 を
    /// 呼び出し側へ写すと片方だけ動かせてしまうためである(GDD02c §1.1 の値は調整対象)。
    /// </para>
    /// <para>
    /// <b>破産中(<paramref name="isBankrupt"/> != 0)は false。</b>500‰の固定が先に効くので
    /// 頭打ちは何もしない(<see cref="Calculate"/> の既存の分岐と同じ順序)。
    /// <b><paramref name="hasSettledYesterday"/> が真なら false。</b>
    /// <b>在庫比から出した価格係数‰ が 1000 以下なら false</b>(<c>min</c> が実際には切っていない)。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="isBankrupt"/> が0/1以外。</exception>
    public static bool WasUnsoldCapApplied(
        int sellableStock, int shipmentTargetStock, int isBankrupt, bool hasSettledYesterday)
    {
        if (isBankrupt is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(isBankrupt), isBankrupt, "破産中フラグは 0 / 1(GDD02c §1.4)。");
        }

        if (isBankrupt == 1 || hasSettledYesterday)
        {
            return false;
        }

        int coefficientPermille = PriceCoefficientPermille(StockRatioPermille(sellableStock, shipmentTargetStock));

        return coefficientPermille > UnsoldCapPermille;
    }

    /// <summary>
    /// 仕入れ移動平均単価の更新(GDD02a §5.1「仕入れ移動平均単価の更新」)。
    /// <c>CeilDiv(旧移動平均 × (1000 − β‰) + 約定単価 × β‰, 1000)</c>。
    /// </summary>
    /// <remarks>
    /// <b>丸めは最後に1回だけ掛ける。</b>2項をそれぞれ <see cref="IntegerMath.ApplyPermille"/> で
    /// 丸めてから足すと両方が切り上がり、<b>価格が動いていない日でも移動平均が1ずつ上がり続ける</b>。
    /// </remarks>
    public static int UpdatedAcquisitionCost(int previousAverage, int unitPrice, int smoothingPermille)
    {
        // 中間の積はlong(旧移動平均×(1000−β)はintを超えうる)。
        long weightedPrevious = (long)previousAverage * (IntegerMath.PermilleScale - smoothingPermille);
        long weightedNew = (long)unitPrice * smoothingPermille;

        return checked((int)IntegerMath.CeilDiv(weightedPrevious + weightedNew, IntegerMath.PermilleScale));
    }
}
