using Visionary.Sim.Numerics;

namespace Visionary.Sim.Systems;

/// <summary>
/// 買い手の予算・購入量の式(GDD02 §8.2〜§8.2.7)。<b>純関数のみ。</b>
/// <see cref="World"/> も <see cref="WorldDefinition"/> も受け取らない ── <see cref="OfferPrice"/>
/// と同じ切り出し方で、式を単体で試験できる形にする。<see cref="BuyerDemand"/> が世帯を
/// 走査しながらこれを呼ぶ。
/// </summary>
public static class BuyerBudget
{
    /// <summary>買い手の在庫圧力‰(GDD02 §8.2.2)。上限倍率は2倍固定。</summary>
    /// <remarks>
    /// <b>目標在庫まで 1000‰ で据え置くこと、上限が 1000‰ であることが要点である。</b>
    /// 1000‰ を超えると赤字の原価で仕入れが発生する。
    /// <para>
    /// <b>目標在庫0でゼロ除算しない形に書く。</b>予想在庫 ≤ 目標在庫 は最初の枝、
    /// 予想在庫 &gt; 目標在庫×2 は最後の枝に落ち、中間の枝は目標在庫 ≥ 1 のときしか
    /// 評価されない(目標在庫 ≤ 0 なら doubledTarget ≤ 0 なので、予想在庫 &gt; 目標在庫 の時点で
    /// 必ず expectedStock &gt; doubledTarget が成り立ち、最後の枝に落ちる)。枝の順を入れ替えると壊れる。
    /// </para>
    /// </remarks>
    public static int StockPressurePermille(int expectedStock, int targetStock)
    {
        if (expectedStock <= targetStock)
        {
            return IntegerMath.PermilleScale;
        }

        // 目標在庫×2はlongで持つ(CeilDivの分子と同じ理由)。
        long doubledTarget = (long)targetStock * 2;

        if (expectedStock <= doubledTarget)
        {
            // ここへ来る時点で expectedStock > targetStock。targetStock <= 0 なら
            // doubledTarget <= 0 < expectedStock となり、この分岐へは来ない
            // (targetStock >= 1 が保証されるのでゼロ除算にならない)。
            long numerator = (long)IntegerMath.PermilleScale * (doubledTarget - expectedStock);

            return checked((int)IntegerMath.CeilDiv(numerator, targetStock));
        }

        return 0;
    }

    /// <summary>予算 = ApplyPermille(基礎値, 在庫圧力‰)(GDD02 §8.2)。</summary>
    public static int Budget(int baseValue, int stockPressurePermille) =>
        IntegerMath.ApplyPermille(baseValue, stockPressurePermille);

    /// <summary>購入量の線形解(GDD02 §8.2.3)。</summary>
    /// <remarks>
    /// <b>実質コスト &gt; 基礎値 を先に判定する。</b>基礎値0のときここで必ず返るので、除算に
    /// 到達しない。順を入れ替えるとゼロ除算になる。
    /// <para>
    /// <b><see cref="IntegerMath.CeilDiv(long, long)"/> は引かれる側に掛かるので、式全体としては
    /// 切り下げになる。</b>GDD02 §8.2.3 の検算「基礎値 × 1/2 → 目標在庫 × 1.5」は、目標3・基礎値100・
    /// 実質コスト50 のとき 4(4.5 の切り下げ)である。<see cref="IntegerMath.FloorDiv(long, long)"/> に
    /// すると 5 になる。<see cref="OfferPrice.PriceCoefficientPermille"/> と同じ向きの丸めである。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="unitRealCost"/> が0以下。0を通すと基礎値0のときの上の保証が崩れる。
    /// 実質コスト = 提示価格 + 移動費であり、提示価格は原価下限以上なので1以上である(#37)。
    /// </exception>
    public static int PurchaseQuantity(int baseValue, int unitRealCost, int targetStock, int expectedStock)
    {
        if (unitRealCost <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitRealCost), unitRealCost, "実質コストは1以上(GDD02 §8.2.3)。");
        }

        if (unitRealCost > baseValue)
        {
            return 0;
        }

        // 中間の積はlong。baseValue >= unitRealCost >= 1がこの時点で保証されるのでゼロ除算しない。
        long reachedStock = ((long)targetStock * 2)
            - IntegerMath.CeilDiv((long)targetStock * unitRealCost, baseValue);

        return checked((int)Math.Max(0L, reachedStock - expectedStock));
    }

    /// <summary>余剰資金 = max(0, 流動資金 − 必要運転資金)(GDD02 §8.2.1)。</summary>
    public static int SurplusFunds(int liquidFunds, long workingCapital) =>
        checked((int)Math.Max(0L, liquidFunds - workingCapital));

    /// <summary>
    /// 必需の基礎値(GDD02 §8.2.1 / §8.2.7)。母数は流動資金である ── 取り違えると、
    /// 困窮した世帯が食料を買えなくなる。
    /// </summary>
    public static int NecessityBaseValue(
        bool hasReference, int marketReference, int liquidFunds,
        int tolerancePermille, int fallbackRatioPermille) =>
        hasReference
            ? IntegerMath.ApplyPermille(marketReference, tolerancePermille)
            : IntegerMath.ApplyPermille(liquidFunds, fallbackRatioPermille);

    /// <summary>
    /// 嗜好・奢侈の基礎値(GDD02 §8.2.1)。母数は余剰資金である(観測の有無に依らない)。
    /// 母数に流動資金を使うと、GDD02 §8.2.1 が名指しした黒字倒産(余剰資金0の世帯が嗜好を買う)
    /// が戻る。
    /// </summary>
    public static int PreferenceBaseValue(int surplusFunds, int ratioPermille) =>
        IntegerMath.ApplyPermille(surplusFunds, ratioPermille);

    /// <summary>
    /// 耐久の基礎値(GDD02 §8.2.1 / §8.2.7)。母数は流動資金である。
    /// </summary>
    /// <remarks>
    /// <b>min を取ることが要点である。</b><see cref="Math.Max(int, int)"/> にすると「資金がなくても
    /// 買おうとする」と「相場より高く買う」が同時に起きる。
    /// </remarks>
    public static int DurableBaseValue(
        bool hasReference, int marketReference, int liquidFunds, int ratioPermille)
    {
        int ratioBasedValue = IntegerMath.ApplyPermille(liquidFunds, ratioPermille);

        return hasReference ? Math.Min(marketReference, ratioBasedValue) : ratioBasedValue;
    }

    /// <summary>派生需要(GDD02 §8.2.1「派生需要の算出」)。結果を <paramref name="baseValues"/> へ書く。</summary>
    /// <remarks>
    /// <para>
    /// <b>すべての除算を <see cref="IntegerMath.FloorDiv(long, long)"/> にする。</b>GDD02 §8.2.1 が
    /// 「floor(A × w_j ÷ W) を J 内で合計すると必ず A 以下になる」ことに立って最低利幅‰ の保証を
    /// 成立させている。<b>途中で ‰ 表現の按分比を経由しない</b> ── <see cref="IntegerMath.ApplyPermille"/>
    /// は切り上げなので、入力ごとに端数が切り上がって合計が許容原価合計を超える。実現利幅が
    /// 最低利幅を下回る日が、観測が部分的に欠けた日にだけ生まれる。善意で「‰ は ApplyPermille を
    /// 通す」規約に揃えられる形なので注意する(タスク仕様テスト #15)。
    /// </para>
    /// <para>
    /// <b>フォールバック入力のぶんを先に差し引く。</b>差し引かないと、観測できた入力だけが
    /// 「本来2入力で分けるはずだった上限」を丸ごと受け取る。<b>残余を max(0, …) で止める</b> ──
    /// フォールバック仕入見込みが許容原価合計を超えうる(流動資金が大きい世帯)。
    /// </para>
    /// <para>
    /// <b>W == 0 はゼロ除算そのものである。</b>GDD06 §3.1 の R=1 では観測できる入力が1つも
    /// 無い日が初日に限らずいつでも通る経路である。
    /// </para>
    /// <para><paramref name="hasInputReference"/> / <paramref name="inputMarketReference"/> /
    /// <paramref name="baseValues"/> は <b>itemId 添字</b>の配列である(<see cref="Item.Count"/>
    /// 以上の長さを持つこと)。<see cref="Recipe.Inputs"/> のうち観測対象の品目だけを読み書きする。</para>
    /// </remarks>
    /// <exception cref="NotSupportedException">
    /// 出力2件以上(配分規則がGDD02に無い。<see cref="OfferPrice.UnitCost"/> と同じ)。
    /// </exception>
    public static void DerivedDemand(
        Recipe recipe,
        bool hasPreviousOutputOfferPrice,
        int previousOutputOfferPrice,
        int minimumMarginPermille,
        int liquidFunds,
        int necessityFallbackRatioPermille,
        bool[] hasInputReference,
        int[] inputMarketReference,
        int[] baseValues)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(hasInputReference);
        ArgumentNullException.ThrowIfNull(inputMarketReference);
        ArgumentNullException.ThrowIfNull(baseValues);

        if (recipe.Outputs.Length != 1)
        {
            throw new NotSupportedException("複数出力への原価配分規則はGDD02に無い(OfferPrice.UnitCostと同じ)。");
        }

        if (recipe.Inputs.Length == 0)
        {
            return;
        }

        int fallbackBaseValue = IntegerMath.ApplyPermille(liquidFunds, necessityFallbackRatioPermille);

        if (!hasPreviousOutputOfferPrice)
        {
            // 前日価格を0として按分すると許容原価合計が0になり、初日に原材料を一切買わなくなる
            // (タスク仕様テスト #19)。按分の式を評価せず、全入力をフォールバックへ落とす。
            foreach (var input in recipe.Inputs)
            {
                baseValues[input.ItemId] = fallbackBaseValue;
            }

            return;
        }

        long expectedRevenue = (long)previousOutputOfferPrice * recipe.Outputs[0].Quantity;
        long allowedCostTotal = IntegerMath.FloorDiv(
            expectedRevenue * IntegerMath.PermilleScale, IntegerMath.PermilleScale + minimumMarginPermille);

        long remainder = allowedCostTotal;
        long totalWeight = 0;

        foreach (var input in recipe.Inputs)
        {
            if (hasInputReference[input.ItemId])
            {
                totalWeight += (long)inputMarketReference[input.ItemId] * input.Quantity;
            }
            else
            {
                baseValues[input.ItemId] = fallbackBaseValue;
                remainder -= (long)fallbackBaseValue * input.Quantity;
            }
        }

        remainder = Math.Max(0L, remainder);

        if (totalWeight == 0)
        {
            // 観測のある入力が無い。Σ_{k∈J} で割るとゼロ除算になる経路(タスク仕様テスト #18)。
            foreach (var input in recipe.Inputs)
            {
                baseValues[input.ItemId] = fallbackBaseValue;
            }

            return;
        }

        foreach (var input in recipe.Inputs)
        {
            if (!hasInputReference[input.ItemId])
            {
                continue;
            }

            long weight = (long)inputMarketReference[input.ItemId] * input.Quantity;
            long allocatedBudget = IntegerMath.FloorDiv(remainder * weight, totalWeight);

            baseValues[input.ItemId] = checked((int)IntegerMath.FloorDiv(allocatedBudget, input.Quantity));
        }
    }
}
