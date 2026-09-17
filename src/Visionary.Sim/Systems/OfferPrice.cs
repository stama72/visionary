using Visionary.Sim.Numerics;
using Visionary.Sim.Time;

namespace Visionary.Sim.Systems;

/// <summary>
/// 提示価格の式(GDD02 §8.1・§8.1.1・§6.3・§8.2.7)。<b>純関数のみ。</b>
/// <see cref="World"/> も <see cref="WorldDefinition"/> も受け取らない ── 式を単体で
/// 試験できる形に切り出すのが目的で、<see cref="TradeSystem"/> が世帯を走査しながらこれを呼ぶ。
/// </summary>
public static class OfferPrice
{
    private const int MaxCoefficientPermille = 1500; // ‰。在庫0のときの上限(GDD02 §8.1.1)
    private const int MinCoefficientPermille = 500;  // ‰。溢れているときの下限(同)

    // BankruptFloorPermille と MinCoefficientPermille は同じ 500 だが、別の定数として置く。
    // GDD02 §6.3 が「床の500‰は新しい係数ではない。§8.1.1のclampの下限と同じ値を使う」と
    // 書いたのは値の由来の説明であって、一方を動かしたら他方も動くという関係ではない。
    // 1つの定数にすると#28が投げ売りの深さだけを調整できない。
    private const int BankruptFloorPermille = 500; // ‰。②の投げ売りの床(GDD02 §6.3②)

    /// <summary>在庫比‰ = CeilDiv(1000 × 販売在庫, 出荷目標在庫)。</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="shipmentTargetStock"/> が0以下。</exception>
    public static int StockRatioPermille(int sellableStock, int shipmentTargetStock)
    {
        // WorldDefinitionのコンストラクタが既に0以下を拒んでいるが、純関数の側でも拒む ──
        // ゼロ除算がDivideByZeroExceptionとして出ると、どの表の欄が空欄なのか分からない。
        if (shipmentTargetStock <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(shipmentTargetStock), shipmentTargetStock, "出荷目標在庫は1以上(GDD02 §8.1.1)。");
        }

        // 1000 × 販売在庫はlongで持つ。販売在庫が214万を超えるとintで桁あふれする。
        long scaledStock = (long)IntegerMath.PermilleScale * sellableStock;

        return checked((int)IntegerMath.CeilDiv(scaledStock, shipmentTargetStock));
    }

    /// <summary>価格係数‰ = clamp(1500 − CeilDiv(在庫比‰, 2), 500, 1500)。</summary>
    /// <remarks>
    /// <b>CeilDiv(在庫比‰, 2) の切り上げは意図的であり、式全体としては切り下げ方向になる。</b>
    /// GDD02 §8.1.1 の註が「品薄側へ偏るほうが §4.1・§8.2.4 の設計意図に沿う」として、切り上げ規約を
    /// 在庫比‰ の算出にだけ適用し係数全体には及ぼさないと明記している。ここを FloorDiv に揃える
    /// 変異は善意から起こりうる(タスク仕様テスト #3)。
    /// </remarks>
    public static int PriceCoefficientPermille(int stockRatioPermille)
    {
        int lowered = MaxCoefficientPermille - IntegerMath.CeilDiv(stockRatioPermille, 2);

        return Math.Clamp(lowered, MinCoefficientPermille, MaxCoefficientPermille);
    }

    /// <summary>原価下限。破産中(isBankrupt == 1)は 500‰ へ下げる(GDD02 §6.3②)。</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="isBankrupt"/> が0/1以外。</exception>
    public static int CostFloor(int unitCost, int minimumMarginPermille, int isBankrupt)
    {
        // "!=0" ではなく値域そのものを閉じる。HouseholdState.IsBankrupt のsetterと同じ理由 ──
        // 2 が破産扱いになる実装と、== 1 で書いた実装が食い違うのを入口で防ぐ。
        if (isBankrupt is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(isBankrupt), isBankrupt, "破産中フラグは 0 / 1(GDD02 §6.3②)。");
        }

        // 「外す」のではなく「下げる」。原価下限が消えると相場基準×500‰の半減ループに底が無くなる
        // (GDD02 §6.3の導出: 不動点はQ/3、同時に立てば毎日半減して1まで落ちる)。
        int marginPermille = isBankrupt == 1
            ? BankruptFloorPermille
            : IntegerMath.PermilleScale + minimumMarginPermille;

        return IntegerMath.ApplyPermille(unitCost, marginPermille);
    }

    /// <summary>出力1単位あたりの原価(GDD02 §8.1.1)。</summary>
    /// <exception cref="NotSupportedException">
    /// 入力0件(機会費用ベースの分岐、未実装)、または出力2件以上(配分規則がGDD02に無い)のとき。
    /// </exception>
    public static int UnitCost(Recipe recipe, int[] purchaseUnitCostAverage)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(purchaseUnitCostAverage);

        if (recipe.Inputs.Length == 0)
        {
            throw new NotSupportedException(
                "入力0件のレシピは機会費用ベースの原価が必要(GDD02 §8.1.1)。"
                    + "機会費用(OpportunityCost、GDD08 §9)は#51まで未実装。");
        }

        if (recipe.Outputs.Length != 1)
        {
            throw new NotSupportedException(
                "複数出力への原価配分規則はGDD02 §8.1.1に無い。");
        }

        // Σは long で積む。単価×数量の合計は int を超えうる。
        long total = 0;
        foreach (var input in recipe.Inputs)
        {
            total += (long)purchaseUnitCostAverage[input.ItemId] * input.Quantity;
        }

        // この除算は切り上げる(GDD02 §8.1.1)。GDD02 §5.2の切り下げる例外とは向きが逆。
        return checked((int)IntegerMath.CeilDiv(total, recipe.Outputs[0].Quantity));
    }

    /// <summary>提示価格 = max(原価下限, ApplyPermille(相場基準, 価格係数‰))。</summary>
    /// <remarks>
    /// 相場基準が立たないときは呼ばない ── 呼び出し側が原価下限をそのまま提示価格にする
    /// (GDD02 §8.2.7「maxの第2項が消えるだけで式は分岐しない」)。
    /// </remarks>
    public static int Calculate(int costFloor, int marketReference, int sellableStock, int shipmentTargetStock)
    {
        int coefficientPermille = PriceCoefficientPermille(StockRatioPermille(sellableStock, shipmentTargetStock));
        int referencePrice = IntegerMath.ApplyPermille(marketReference, coefficientPermille);

        return Math.Max(costFloor, referencePrice);
    }

    /// <summary>
    /// 仕入れ移動平均単価の更新(GDD02 §8.1.1「仕入れ移動平均単価の更新」)。
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

    /// <summary>相場基準(GDD02 §8.1.1「相場基準」)。0件なら false を返す。</summary>
    /// <remarks>
    /// <para>
    /// <b>有効な観測の判定は <see cref="Tick.DayIndex"/> の差で行う。</b>時刻(<see cref="Tick.HourOfDay"/>)
    /// は見ない。順5 は <c>Daily(hour: 0)</c> なので当日の観測は通常存在しないが、コマンド
    /// (GDD06 §3.4)は任意の tick に割り込むため tick 差で書くと「同日の午後の観測」が
    /// 前日扱いになりうる(タスク仕様)。
    /// </para>
    /// <para>
    /// <b>売り手ごとに最新の1件だけを平均に入れる。</b>レコード単位で平均すると、よく見る売り手ほど
    /// 重みが増す(GDD02 §8.1.1)。同着(同一tickに同一売り手の観測が2件)は後に追加されたほう
    /// (添字が大きいほう)を採る ── 走査で <c>&gt;=</c> を使うことで、先勝ちにすると
    /// <c>List</c> の並びが結果を変える経路を避ける。
    /// </para>
    /// </remarks>
    public static bool TryMarketReference(
        IReadOnlyList<PriceObservation> headObservations,
        int itemId,
        int selfHouseholdId,
        Tick now,
        int retentionDays,
        bool hasOwnPreviousPrice,
        int ownPreviousPrice,
        out int marketReference)
    {
        ArgumentNullException.ThrowIfNull(headObservations);

        // 売り手ごとの畳み込みにSortedDictionaryを使う。Dictionaryは列挙順が保証されず
        // ADR-0002の規約に触れる(合計そのものは順序に依らないが、規約を型で守る)。
        var latestBySeller = new SortedDictionary<int, PriceObservation>();

        for (int i = 0; i < headObservations.Count; i++)
        {
            var observation = headObservations[i];

            if (observation.ItemId != itemId)
            {
                continue;
            }

            // 自分の売り注文は自分の観測に入れない(GDD06 §3.1)。観測の生成側が守る規約だが、
            // ここで守らないと「他の売り手の観測が1件以上」を自分で満たしてしまう
            // (GDD06 §3.1が「純粋な自己ループ」と呼んだ状態)。
            if (observation.SellerId == selfHouseholdId)
            {
                continue;
            }

            long dayDifference = now.DayIndex - observation.ObservedAt.DayIndex;

            // 記憶は前日まで(GDD06 §3.1)。当日(差0)を使うと日内の相互参照が復活する。
            // 保持期間ちょうど(差 == retentionDays)は入る。
            if (dayDifference < 1 || dayDifference > retentionDays)
            {
                continue;
            }

            if (!latestBySeller.TryGetValue(observation.SellerId, out var existing)
                || observation.ObservedAt >= existing.ObservedAt)
            {
                latestBySeller[observation.SellerId] = observation;
            }
        }

        if (latestBySeller.Count == 0)
        {
            // 他の売り手の観測が0件のときは自分の前日価格も加えない(無い日は集合に加えないだけ、
            // 0を足さない。GDD02 §8.1.1が名指しした誤り)。
            marketReference = 0;
            return false;
        }

        long total = 0;
        int count = 0;

        foreach (var kvp in latestBySeller)
        {
            total += kvp.Value.Price;
            count++;
        }

        // 自分の前日の提示価格は、他の売り手の観測が1件以上あるときだけ加える
        // (GDD02 §8.1.1 / GDD06 §3.1)。上のearly returnにより、ここへ来る時点で1件以上ある。
        if (hasOwnPreviousPrice)
        {
            total += ownPreviousPrice;
            count++;
        }

        // 平均はCeilDiv。合計はlongで積む。
        marketReference = checked((int)IntegerMath.CeilDiv(total, count));
        return true;
    }
}
