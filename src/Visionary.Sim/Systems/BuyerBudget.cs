using Visionary.Sim.Numerics;

namespace Visionary.Sim.Systems;

/// <summary>
/// 買い手の予算・購入量の式(GDD02c §2 / GDD02b §5)。<b>純関数のみ。</b>
/// <see cref="World"/> も <see cref="WorldDefinition"/> も受け取らない ── <see cref="OfferPrice"/>
/// と同じ切り出し方で、式を単体で試験できる形にする。<see cref="BuyerDemand"/> が世帯を
/// 走査しながらこれを呼ぶ。
/// </summary>
public static class BuyerBudget
{
    private const int MaxPressurePermille = 1500; // ‰。在庫0のときの上限(GDD02b §5.1)
    private const int MinPressurePermille = 500;  // ‰。目標の2倍手前での下限(同)

    /// <summary>
    /// 買い手の在庫圧力‰(GDD02b §5.1)。
    /// <c>在庫比‰ = CeilDiv(1000 × 予想在庫, 目標在庫)</c>、
    /// <c>在庫圧力‰ = 在庫比‰ &gt; 2000 ? 0 : clamp(1500 − CeilDiv(在庫比‰, 2), 500, 1500)</c>。
    /// </summary>
    /// <remarks>
    /// <b>目標在庫 ≤ 0 は 0 を返す(ゼロ除算しない)。</b>「1単位も持ちたくない」であり、
    /// 予想在庫が0でも0である。旧実装はこの場合に1000‰を返していた ── 目標0の品目に
    /// 相場どおりの予算が立っていた。
    /// <para>
    /// <b>丸めの向きは売り手側(<see cref="OfferPrice.PriceCoefficientPermille"/>)と同じ。</b>
    /// 在庫比‰ を切り上げてから引くので、式全体としては切り下げ方向になる。
    /// </para>
    /// </remarks>
    public static int StockPressurePermille(int expectedStock, int targetStock)
    {
        if (targetStock <= 0)
        {
            return 0;
        }

        // 1000 × 予想在庫はlongで持つ(OfferPrice.StockRatioPermilleと同じ理由)。
        long numerator = (long)IntegerMath.PermilleScale * expectedStock;
        long stockRatioPermille = IntegerMath.CeilDiv(numerator, targetStock);

        if (stockRatioPermille > 2000)
        {
            return 0;
        }

        long lowered = MaxPressurePermille - IntegerMath.CeilDiv(stockRatioPermille, 2);

        return checked((int)Math.Clamp(lowered, MinPressurePermille, MaxPressurePermille));
    }

    /// <summary>現金上限 = FloorDiv( 用途に使える資金, max(1日分の数量, 1) )(GDD02c §2.1)。</summary>
    /// <remarks><b><paramref name="dailyQuantity"/> が0でもゼロ除算しない</b>のは max(…, 1) による。
    /// 耐久は1個で呼ぶ(1日分が1個未満なので、GDD02c §2.1 の表が1と定めている)。</remarks>
    public static int CashCap(int availableFunds, int dailyQuantity) =>
        IntegerMath.FloorDiv(availableFunds, Math.Max(dailyQuantity, 1));

    /// <summary>
    /// 用途に使える資金(母数。GDD02c §2.1 / GDD02b §3.1)。<b>段階である</b> ──
    /// 必需 = 流動資金 / 耐久・生産の入力 = 流動資金 − 必需の取り置き /
    /// 嗜好 = 流動資金 − 必需の取り置き − 運転資金。<b>負なら0</b>。
    /// </summary>
    public static int AvailableFunds(
        DemandPurpose purpose, int liquidFunds, long necessityReserve, long workingCapital)
    {
        long available = purpose switch
        {
            DemandPurpose.Necessity => liquidFunds,
            DemandPurpose.Durable or DemandPurpose.ProductionInput => liquidFunds - necessityReserve,
            DemandPurpose.Preference => liquidFunds - necessityReserve - workingCapital,
            _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "未知の用途。"),
        };

        return checked((int)Math.Max(0L, available));
    }

    /// <summary>
    /// 摩耗費[1回] = CeilDiv( 仕入れ移動平均単価[工具] × 所要労働‰, N × 1000 )(GDD02a §5)。
    /// </summary>
    public static int WearCostPerRun(int toolUnitCostAverage, int laborPermille, int toolLifeLaborDays)
    {
        // 中間の積はlong(単価×所要労働‰はintを超えうる)。
        long numerator = (long)toolUnitCostAverage * laborPermille;
        long denominator = (long)toolLifeLaborDays * IntegerMath.PermilleScale;

        return checked((int)IntegerMath.CeilDiv(numerator, denominator));
    }

    /// <summary>
    /// 利潤上限(GDD02c §2.3)。結果を <paramref name="profitCap"/> / <paramref name="hasProfitCap"/>
    /// の<b>itemId 添字</b>の配列へ書く。<b>レシピの入力の品目だけを書き、他の添字に触れない。</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>見込み収益 = 提示価格_出力[前日] × 出力数量</c>、
    /// <c>許容原価合計 = FloorDiv(見込み収益 × 1000, 1000 + 最低利幅‰) − 摩耗費[1回]</c>、
    /// <c>相場での原価 = Σ_k(相場基準_k × 必要数量_k)</c>、
    /// <c>利潤上限_j = FloorDiv(許容原価合計 × 相場基準_j, 相場での原価)</c>。
    /// </para>
    /// <para>
    /// <b>次のいずれかなら全入力の <paramref name="hasProfitCap"/> を false にする</b>(一部だけ
    /// 按分しない。GDD02c §2.3):前日の出力提示価格が無い / <b>いずれかの</b>入力の相場基準が無い /
    /// 許容原価合計 ≤ 0 / 相場での原価 ≤ 0。
    /// </para>
    /// <para>
    /// <b>すべての除算を <see cref="IntegerMath.FloorDiv(long, long)"/> にする。</b>
    /// <see cref="IntegerMath.ApplyPermille"/> を経由すると入力ごとに切り上がって合計が許容原価合計を
    /// 超え、最低利幅‰ の保証が崩れる。<b>‰ 表現の按分比を挟まず、相場基準に直接比例させる</b>
    /// (GDD02c §2.3)。善意で「‰ は ApplyPermille を通す」規約に揃えられる形なので注意する。
    /// </para>
    /// </remarks>
    public static void ProfitCaps(
        Recipe recipe,
        bool hasPreviousOutputOfferPrice, int previousOutputOfferPrice,
        int minimumMarginPermille, int wearCostPerRun,
        bool[] hasInputReference, int[] inputMarketReference,
        bool[] hasProfitCap, int[] profitCap)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(hasInputReference);
        ArgumentNullException.ThrowIfNull(inputMarketReference);
        ArgumentNullException.ThrowIfNull(hasProfitCap);
        ArgumentNullException.ThrowIfNull(profitCap);

        if (!hasPreviousOutputOfferPrice)
        {
            ClearProfitCaps(recipe, hasProfitCap);
            return;
        }

        long expectedRevenue = (long)previousOutputOfferPrice * recipe.Outputs[0].Quantity;
        long allowedCostTotal = IntegerMath.FloorDiv(
            expectedRevenue * IntegerMath.PermilleScale, IntegerMath.PermilleScale + minimumMarginPermille)
            - wearCostPerRun;

        if (allowedCostTotal <= 0)
        {
            ClearProfitCaps(recipe, hasProfitCap);
            return;
        }

        long marketCostTotal = 0;

        foreach (var input in recipe.Inputs)
        {
            if (!hasInputReference[input.ItemId])
            {
                // 一部だけ按分しない ── 観測できた入力だけで按分すると、1入力が
                // 「本来複数入力で分けるはずだった上限」を丸ごと受け取る。
                ClearProfitCaps(recipe, hasProfitCap);
                return;
            }

            marketCostTotal += (long)inputMarketReference[input.ItemId] * input.Quantity;
        }

        if (marketCostTotal <= 0)
        {
            ClearProfitCaps(recipe, hasProfitCap);
            return;
        }

        foreach (var input in recipe.Inputs)
        {
            hasProfitCap[input.ItemId] = true;
            profitCap[input.ItemId] = checked((int)IntegerMath.FloorDiv(
                allowedCostTotal * inputMarketReference[input.ItemId], marketCostTotal));
        }
    }

    private static void ClearProfitCaps(Recipe recipe, bool[] hasProfitCap)
    {
        foreach (var input in recipe.Inputs)
        {
            hasProfitCap[input.ItemId] = false;
        }
    }

    /// <summary>予算 = min( ApplyPermille(基礎値, 在庫圧力‰) , 現金上限 , 利潤上限 )(GDD02c §2.1)。</summary>
    /// <remarks><b>在庫圧力‰ を掛けるのは第1項(基礎値)だけである(現金上限・利潤上限には掛からない)。</b>
    /// 現金上限・利潤上限に掛けると、「払えない金を緊急性で払う」ことになる(GDD02c §2.1)。
    /// <b>第1項は常にある</b>(決定11。GDD02b §5.2) ── 相場項が無い日は基礎値が窓口の当日価格に
    /// 落ちるだけで、項そのものが消えることはない。無い項(利潤上限)だけを min から落とす。</remarks>
    public static int Budget(int baseValue, int stockPressurePermille, int cashCap, bool hasProfitCap, int profitCap)
    {
        int result = Math.Min(IntegerMath.ApplyPermille(baseValue, stockPressurePermille), cashCap);

        return hasProfitCap ? Math.Min(result, profitCap) : result;
    }

    /// <summary>線形解の基礎値 = 相場項。無ければ窓口の当日価格(GDD02b §5.2)。<b>常に1以上</b>。</summary>
    /// <remarks>
    /// <b>在庫圧力を掛けない。</b>在庫圧力は線形解の形そのものに入っており、基礎値にも掛けると
    /// 二重に効く(GDD02c §2.1)。
    /// <para>
    /// <b>窓口の当日価格(<paramref name="windowPrice"/>)を解くのは呼び出し側(<see cref="BuyerDemand"/>)
    /// である。</b>本メソッドは純関数として受け取るだけ(決定10)。
    /// </para>
    /// <para>
    /// <b>残る穴(規則4)。</b>ここで防げるのは「窓口価格が0のまま渡る」ことだけで、
    /// 「間違った窓口価格(天井や基準値)が渡る」ことは防げない(<see cref="BuyerDemand"/> 側の
    /// 呼び出し実装が正しい値を渡すことに依存する)。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="windowPrice"/> が0以下(<paramref name="hasMarketTerm"/> の真偽によらず無条件)。
    /// <paramref name="hasMarketTerm"/> が真で <paramref name="marketTerm"/> が0以下のときも、
    /// 戻り値0が<see cref="PurchaseQuantity"/>の除算まで届く前にここで弾く
    /// (W2-15訂正3赤B。「常に1以上」はdocの主張であって<paramref name="windowPrice"/>だけの
    /// 検査では守れない ── 実運用では<see cref="BuyerDemand"/>が渡す<c>marketTerm</c>は
    /// <c>ApplyPermille(相場基準, 許容乖離‰)</c>で、許容乖離‰は<see cref="WorldDefinition"/>が
    /// 0を弾くため0にならないが、この関数自身は<see cref="WorldDefinition"/>を知らない純関数
    /// なので、契約は戻り値そのものに掛けて守る)。
    /// </exception>
    public static int BaseValue(bool hasMarketTerm, int marketTerm, int windowPrice)
    {
        if (windowPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(windowPrice), windowPrice, "窓口の当日価格は1以上(GDD02d §2.1・§3・§5)。");
        }

        int result = hasMarketTerm ? marketTerm : windowPrice;

        if (result <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(marketTerm), marketTerm, "基礎値は常に1以上(GDD02b §5.2)。");
        }

        return result;
    }

    /// <summary>
    /// 購入量の線形解(GDD02b §5.2)。
    /// <c>到達在庫 = clamp( 3 × 目標在庫 − CeilDiv(2 × 目標在庫 × 実効価格, 基礎値) , 0 , 2 × 目標在庫 )</c>、
    /// <c>購入量 = max(0, 到達在庫 − 予想在庫)</c>。
    /// </summary>
    /// <remarks>
    /// <b>旧実装は上側の clamp を持たず、下側だけを <c>max(0, …)</c> で押さえていた。</b>
    /// 上側が無いと、実効価格が基礎値の1/2を下回る日に目標在庫の2倍を超えて買い溜める
    /// (GDD02b §5.1 の「溜め込みの縮退」)。
    /// <para><b>基礎値0のときの除算を避けるため、呼ぶ前にゲートを通す。</b>
    /// <see cref="Decide"/> が唯一の想定呼び出し側である。</para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="baseValue"/> が0以下。</exception>
    public static int PurchaseQuantity(int baseValue, int effectivePrice, int targetStock, int expectedStock)
    {
        if (baseValue <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baseValue), baseValue, "基礎値は1以上(GDD02b §5.2)。");
        }

        // 中間の積はlong。
        long doubledTarget = (long)targetStock * 2;
        long triple = (long)targetStock * 3;
        long subtrahend = IntegerMath.CeilDiv((long)targetStock * 2 * effectivePrice, baseValue);
        long reachedStock = Math.Clamp(triple - subtrahend, 0L, doubledTarget);

        return checked((int)Math.Max(0L, reachedStock - expectedStock));
    }

    /// <summary>
    /// 用途の単位で解いた数量を個数へ直す(GDD02b §5.2・§2)。耐久だけ耐久値で解くので
    /// 変換が要る。<b>購入(段5b)と外出の計画(<see cref="ErrandPlanner"/>)の両方がこれを
    /// 呼ぶ</b> ── 変換が1か所にあることが、耐久の行だけ単位を取り違える(3回目の差し戻しの
    /// 原因。別表C)を防ぐ。
    /// </summary>
    public static int QuantityInUnits(DemandPurpose purpose, int quantity, int durabilityPerTool) =>
        purpose == DemandPurpose.Durable
            ? IntegerMath.CeilDiv(quantity, durabilityPerTool)
            : quantity;

    /// <summary>
    /// ゲートと線形解(GDD02b §5.2)。<b>購入量が0になった理由を返す。</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>理由は <c>相場 → 利潤上限 → 現金上限</c> の固定順で、最初に当たったものを採る。</b>
    /// min の argmin ではない ── 相場項と現金上限がともに実効価格を下回る日に argmin を採ると、
    /// 「金が無限にあっても相場項で買わなかった」世帯が資金不足に数えられ、破産中フラグが
    /// 価格ショックの検出器に化ける(GDD02b §5.2・§3.2)。<b>同点は相場が勝つ</b>(狭い側に倒す)。
    /// </para>
    /// <para>
    /// ゲートを通っても購入量が0になることはある(到達在庫が予想在庫に届いた)。そのときの
    /// 理由は <see cref="NoPurchaseReason.None"/> であり、資金不足ではない。
    /// </para>
    /// <para>
    /// <b>分岐1に「相場項があり」の条件は無い(決定11)。</b>ゲートが読むのは <see cref="DemandLine.BaseValue"/>
    /// であって <see cref="DemandLine.MarketTerm"/> / <see cref="DemandLine.HasMarketTerm"/> ではない ──
    /// 相場項が無い日も基礎値(窓口の当日価格)を使ってゲートが立つ。<b>本メソッドは
    /// <see cref="BaseValue"/> を呼ばない</b> ── 読むのは手組みでも渡せる <c>line.BaseValue</c> であり、
    /// 生産経路(<see cref="BuyerDemand.BuildLine"/>)が <see cref="BaseValue"/> を通すので0は入らないが、
    /// それはこのメソッド自身が持つ不変条件ではない。<b>いまも
    /// <see cref="PurchaseQuantity"/> の除算を守っているのは旧版が書いていた不変条件そのものである</b>
    /// ── 実効価格 ≥ 1(上の検査)と、分岐1が厳密な <c>&gt;</c> であることにより、<c>BaseValue = 0</c>
    /// の行が来ても分岐1が必ず立ち、除算まで届かない。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="effectivePrice"/> が0以下。</exception>
    public static PurchaseDecision Decide(in DemandLine line, int effectivePrice)
    {
        if (effectivePrice <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectivePrice), effectivePrice, "実効価格は1以上(GDD02b §5.2)。");
        }

        int adjustedBaseValue = IntegerMath.ApplyPermille(line.BaseValue, line.StockPressurePermille);

        if (effectivePrice > adjustedBaseValue)
        {
            return new PurchaseDecision { Quantity = 0, Reason = NoPurchaseReason.MarketTerm };
        }

        if (line.HasProfitCap && effectivePrice > line.ProfitCap)
        {
            return new PurchaseDecision { Quantity = 0, Reason = NoPurchaseReason.ProfitCap };
        }

        if (effectivePrice > line.CashCap)
        {
            return new PurchaseDecision { Quantity = 0, Reason = NoPurchaseReason.CashCap };
        }

        int quantity = PurchaseQuantity(line.BaseValue, effectivePrice, line.TargetStock, line.ExpectedStock);

        return new PurchaseDecision { Quantity = quantity, Reason = NoPurchaseReason.None };
    }
}
