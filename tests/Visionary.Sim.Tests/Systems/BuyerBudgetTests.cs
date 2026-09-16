using Visionary.Sim.Numerics;
using Visionary.Sim.Systems;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="BuyerBudget"/>(GDD02 §8.2〜§8.2.7、#36 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class BuyerBudgetTests
{
    // レシピの入力・出力に使う品目Id。テストの可読性のためItem.*とは別に短い名前を持つ。
    private const int InputA = 0;
    private const int InputB = 1;
    private const int Output = 2;

    /// <summary>テスト表 #1。目標3・予想 0/1/3 → すべて 1000。</summary>
    [Fact]
    public void StockPressureStaysAtOneThousandUpToTheTarget()
    {
        Assert.Equal(1000, BuyerBudget.StockPressurePermille(expectedStock: 0, targetStock: 3));
        Assert.Equal(1000, BuyerBudget.StockPressurePermille(expectedStock: 1, targetStock: 3));
        Assert.Equal(1000, BuyerBudget.StockPressurePermille(expectedStock: 3, targetStock: 3));
    }

    /// <summary>
    /// 【核心】テスト表 #2。目標3: 予想4 → 667、予想5 → 334、予想6 → 0。
    /// </summary>
    [Fact]
    public void StockPressureFallsLinearlyToZeroAtTwiceTheTarget()
    {
        Assert.Equal(667, BuyerBudget.StockPressurePermille(expectedStock: 4, targetStock: 3));
        Assert.Equal(334, BuyerBudget.StockPressurePermille(expectedStock: 5, targetStock: 3));
        Assert.Equal(0, BuyerBudget.StockPressurePermille(expectedStock: 6, targetStock: 3));
    }

    /// <summary>テスト表 #3。目標3・予想 7 / 100 → 0。</summary>
    [Fact]
    public void StockPressureIsZeroBeyondTwiceTheTarget()
    {
        Assert.Equal(0, BuyerBudget.StockPressurePermille(expectedStock: 7, targetStock: 3));
        Assert.Equal(0, BuyerBudget.StockPressurePermille(expectedStock: 100, targetStock: 3));
    }

    /// <summary>テスト表 #4。目標0: 予想0 → 1000、予想1 → 0。例外を投げない。</summary>
    [Fact]
    public void StockPressureHandlesAZeroTarget()
    {
        Assert.Equal(1000, BuyerBudget.StockPressurePermille(expectedStock: 0, targetStock: 0));
        Assert.Equal(0, BuyerBudget.StockPressurePermille(expectedStock: 1, targetStock: 0));
    }

    /// <summary>
    /// テスト表 #5。基礎値100・圧力1000 → 100、圧力500 → 50、圧力0 → 0、
    /// 基礎値500・圧力1 → 1(0.5 の切り上げ)。
    /// </summary>
    [Fact]
    public void BudgetScalesTheBaseValueByStockPressure()
    {
        Assert.Equal(100, BuyerBudget.Budget(baseValue: 100, stockPressurePermille: 1000));
        Assert.Equal(50, BuyerBudget.Budget(baseValue: 100, stockPressurePermille: 500));
        Assert.Equal(0, BuyerBudget.Budget(baseValue: 100, stockPressurePermille: 0));
        Assert.Equal(1, BuyerBudget.Budget(baseValue: 500, stockPressurePermille: 1));
    }

    /// <summary>テスト表 #6。基礎値100・実質コスト100・目標3・予想0 → 3。</summary>
    /// <remarks>
    /// <b>【核心】変異の実測(2026-09-16)。</b><c>reachedStock</c> の計算から
    /// <c>CeilDiv(targetStock * unitRealCost, baseValue)</c> の減算を外し、常に
    /// <c>2 * targetStock</c>(2倍の目標まで買う変異)を返すようにしたところ、
    /// <c>Assert.Equal(3, ...)</c> が実際値6で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void PurchaseQuantityReachesTheTargetAtTheBaseValue()
    {
        Assert.Equal(
            3,
            BuyerBudget.PurchaseQuantity(baseValue: 100, unitRealCost: 100, targetStock: 3, expectedStock: 0));
    }

    /// <summary>
    /// 【核心】テスト表 #7。基礎値100・実質コスト50・目標3・予想0 → 4(4.5 の切り下げ)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>IntegerMath.CeilDiv(targetStock * unitRealCost, baseValue)</c>
    /// を <c>IntegerMath.FloorDiv</c> に変える変異を当てたところ、<c>Assert.Equal(4, ...)</c> が
    /// 実際値5(FloorDiv(150,100)=1、reachedStock=6-1=5)で失敗した(赤を確認)。GDD02 §8.2.3 の
    /// 検算表「基礎値×1/2→目標在庫×1.5」を「×1.5 ちょうど」と読んで切り上げる誤りに対応する。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void PurchaseQuantityBuysMoreBelowTheBaseValue()
    {
        Assert.Equal(
            4,
            BuyerBudget.PurchaseQuantity(baseValue: 100, unitRealCost: 50, targetStock: 3, expectedStock: 0));
    }

    /// <summary>
    /// 【核心】テスト表 #8。基礎値100・実質コスト101 → 0。基礎値0・実質コスト1 → 0(例外を投げない)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>unitRealCost &gt; baseValue</c> の早期returnを、除算を
    /// 先に評価する形(<c>reachedStock</c> の計算をガードの前に置く変異)に変えたところ、
    /// 基礎値0・実質コスト1のケースで <c>DivideByZeroException</c> が飛んだ(赤を確認、
    /// <c>Assert.Equal(0, ...)</c> は例外未処理で失敗扱い)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void PurchaseQuantityIsZeroAboveTheBaseValue()
    {
        Assert.Equal(
            0,
            BuyerBudget.PurchaseQuantity(baseValue: 100, unitRealCost: 101, targetStock: 3, expectedStock: 0));
        Assert.Equal(
            0,
            BuyerBudget.PurchaseQuantity(baseValue: 0, unitRealCost: 1, targetStock: 3, expectedStock: 0));
    }

    /// <summary>テスト表 #9。基礎値100・実質コスト100・目標3・予想5 → 0。</summary>
    [Fact]
    public void PurchaseQuantityNeverGoesNegative()
    {
        Assert.Equal(
            0,
            BuyerBudget.PurchaseQuantity(baseValue: 100, unitRealCost: 100, targetStock: 3, expectedStock: 5));
    }

    /// <summary>テスト表 #10。実質コスト 0 / −1 で ArgumentOutOfRangeException。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PurchaseQuantityRejectsNonPositiveCost(int unitRealCost)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BuyerBudget.PurchaseQuantity(baseValue: 100, unitRealCost, targetStock: 3, expectedStock: 0));
    }

    /// <summary>
    /// 【核心】テスト表 #11。相場基準100・許容乖離1200‰ → 120。観測なし・流動資金200・50‰ → 10。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>hasReference</c> を無視して常にフォールバック
    /// (<c>ApplyPermille(liquidFunds, fallbackRatioPermille)</c>)を返す変異を当てたところ、
    /// 観測ありのケース(相場基準100・許容乖離1200‰)の <c>Assert.Equal(120, ...)</c> が
    /// 実際値0(このテストの流動資金は0)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void NecessityBaseValueAppliesToleranceOrFallsBackToTheRatio()
    {
        Assert.Equal(
            120,
            BuyerBudget.NecessityBaseValue(
                hasReference: true, marketReference: 100, liquidFunds: 0,
                tolerancePermille: 1200, fallbackRatioPermille: 50));

        Assert.Equal(
            10,
            BuyerBudget.NecessityBaseValue(
                hasReference: false, marketReference: 0, liquidFunds: 200,
                tolerancePermille: 1200, fallbackRatioPermille: 50));
    }

    /// <summary>
    /// 【核心】テスト表 #12。余剰資金0 → 0。余剰資金1000・200‰ → 200。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>PreferenceBaseValue</c> の引数を無視して、常に
    /// 固定の流動資金値(1000)を母数にする変異(呼び出し元で余剰資金の代わりに流動資金を渡す
    /// 誤りを模した)を <c>PreferenceBaseValue(surplusFunds: 0, ratioPermille: 200)</c> の
    /// 呼び出しに対して当てたところ、<c>Assert.Equal(0, ...)</c> が実際値200
    /// (GDD02 §8.2.1が名指しした黒字倒産 ── 余剰資金0の世帯が嗜好を買う)で失敗した(赤を確認)。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void PreferenceBaseValueUsesSurplusFundsNotLiquidFunds()
    {
        Assert.Equal(0, BuyerBudget.PreferenceBaseValue(surplusFunds: 0, ratioPermille: 200));
        Assert.Equal(200, BuyerBudget.PreferenceBaseValue(surplusFunds: 1000, ratioPermille: 200));
    }

    /// <summary>
    /// 【核心】テスト表 #13。相場基準100・流動資金2000・10‰(=20) → 20。相場基準10・同 → 10。
    /// 観測なし → 20。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>Math.Min</c> を <c>Math.Max</c> に変える変異を当てたところ、
    /// 相場基準10のケースの <c>Assert.Equal(10, ...)</c> が実際値20(資金の上限を無視して相場より
    /// 高く買う)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void DurableBaseValueTakesTheSmallerOfReferenceAndRatio()
    {
        Assert.Equal(
            20,
            BuyerBudget.DurableBaseValue(
                hasReference: true, marketReference: 100, liquidFunds: 2000, ratioPermille: 10));
        Assert.Equal(
            10,
            BuyerBudget.DurableBaseValue(
                hasReference: true, marketReference: 10, liquidFunds: 2000, ratioPermille: 10));
        Assert.Equal(
            20,
            BuyerBudget.DurableBaseValue(
                hasReference: false, marketReference: 0, liquidFunds: 2000, ratioPermille: 10));
    }

    /// <summary>テスト表 #14。流動資金100・運転資金250 → 0。100・40 → 60。</summary>
    [Fact]
    public void SurplusFundsClampsAtZero()
    {
        Assert.Equal(0, BuyerBudget.SurplusFunds(liquidFunds: 100, workingCapital: 250));
        Assert.Equal(60, BuyerBudget.SurplusFunds(liquidFunds: 100, workingCapital: 40));
    }

    private static Recipe TwoInputRecipe() =>
        new(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: new[]
            {
                new ItemQuantity { ItemId = InputA, Quantity = 1 },
                new ItemQuantity { ItemId = InputB, Quantity = 1 },
            },
            laborPermille: 1);

    /// <summary>
    /// 【核心】テスト表 #15。出力数量1・前日価格12・利幅200‰(許容原価合計10)、
    /// 入力A(相場3・数量1)B(相場4・数量1) → A=4 / B=5、Σ(基礎値×数量)=9 ≤ 10。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b>配分の除算(<c>IntegerMath.FloorDiv(remainder * weight,
    /// totalWeight)</c>)を <c>IntegerMath.ApplyPermille</c> 経由(‰の按分比を中間に挟む形。
    /// <c>ApplyPermille(remainder, CeilDiv(weight*1000, totalWeight))</c>)に変える変異を当てたところ、
    /// A=5・B=6(合計11 &gt; 許容原価合計10)になり、<c>Assert.True(total &lt;= 10)</c> が
    /// 失敗した(赤を確認)。最低利幅の保証が観測の揃った日にだけ静かに破れる経路に対応する。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void DerivedDemandSharesTheAllowedCostWithoutExceedingIt()
    {
        var recipe = TwoInputRecipe();
        var hasInputReference = new bool[Item.Count];
        var inputMarketReference = new int[Item.Count];
        var baseValues = new int[Item.Count];

        hasInputReference[InputA] = true;
        inputMarketReference[InputA] = 3;
        hasInputReference[InputB] = true;
        inputMarketReference[InputB] = 4;

        BuyerBudget.DerivedDemand(
            recipe,
            hasPreviousOutputOfferPrice: true,
            previousOutputOfferPrice: 12,
            minimumMarginPermille: 200,
            liquidFunds: 0,
            necessityFallbackRatioPermille: 50,
            hasInputReference, inputMarketReference, baseValues);

        Assert.Equal(4, baseValues[InputA]);
        Assert.Equal(5, baseValues[InputB]);
        Assert.True((baseValues[InputA] * 1) + (baseValues[InputB] * 1) <= 10);
    }

    /// <summary>
    /// テスト表 #16。前日価格60・出力数量1・利幅200‰(許容原価合計50)、
    /// 入力A(相場10・数量2)単独 → 配分予算50・基礎値25。
    /// </summary>
    [Fact]
    public void DerivedDemandDividesTheAllocationByTheRequiredQuantity()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = InputA, Quantity = 2 } },
            laborPermille: 1);

        var hasInputReference = new bool[Item.Count];
        var inputMarketReference = new int[Item.Count];
        var baseValues = new int[Item.Count];

        hasInputReference[InputA] = true;
        inputMarketReference[InputA] = 10;

        BuyerBudget.DerivedDemand(
            recipe,
            hasPreviousOutputOfferPrice: true,
            previousOutputOfferPrice: 60,
            minimumMarginPermille: 200,
            liquidFunds: 0,
            necessityFallbackRatioPermille: 50,
            hasInputReference, inputMarketReference, baseValues);

        Assert.Equal(25, baseValues[InputA]);
    }

    /// <summary>
    /// 【核心】テスト表 #17。A(相場20・数量1)観測あり / B観測なし、流動資金200・必需50‰ →
    /// B=10、残余50−10=40 → A=40。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b>フォールバック入力の差し引き(<c>remainder -=
    /// (long)fallbackBaseValue * input.Quantity</c>)を削る変異を当てたところ、
    /// <c>Assert.Equal(40, baseValues[InputA])</c> が実際値50(観測できた入力Aが「本来2入力で
    /// 分けるはずだった上限」を丸ごと受け取った)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void DerivedDemandSubtractsFallbackInputsBeforeSharing()
    {
        var recipe = TwoInputRecipe();
        var hasInputReference = new bool[Item.Count];
        var inputMarketReference = new int[Item.Count];
        var baseValues = new int[Item.Count];

        hasInputReference[InputA] = true;
        inputMarketReference[InputA] = 20;
        hasInputReference[InputB] = false;

        // 許容原価合計50になるよう前日価格・利幅を選ぶ(FloorDiv(50*1000,1000)=50、利幅0‰)。
        BuyerBudget.DerivedDemand(
            recipe,
            hasPreviousOutputOfferPrice: true,
            previousOutputOfferPrice: 50,
            minimumMarginPermille: 0,
            liquidFunds: 200,
            necessityFallbackRatioPermille: 50, // フォールバック = ApplyPermille(200,50) = 10
            hasInputReference, inputMarketReference, baseValues);

        Assert.Equal(10, baseValues[InputB]);
        Assert.Equal(40, baseValues[InputA]);
    }

    /// <summary>
    /// 【核心】テスト表 #18。全入力に観測なし → 全員 ApplyPermille(流動資金, 50‰)。例外を投げない。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>totalWeight == 0</c> の早期returnを削り、常に
    /// <c>FloorDiv(remainder * weight, totalWeight)</c>(totalWeight=0のまま除算する変異)を
    /// 当てたところ、<c>DivideByZeroException</c> が飛んだ(赤を確認、GDD06 §3.1のR=1では
    /// 初日に限らずいつでも通る経路)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void DerivedDemandFallsBackForEveryInputWhenNoReferenceSurvives()
    {
        var recipe = TwoInputRecipe();
        var hasInputReference = new bool[Item.Count];
        var inputMarketReference = new int[Item.Count];
        var baseValues = new int[Item.Count];

        BuyerBudget.DerivedDemand(
            recipe,
            hasPreviousOutputOfferPrice: true,
            previousOutputOfferPrice: 100,
            minimumMarginPermille: 0,
            liquidFunds: 200,
            necessityFallbackRatioPermille: 50,
            hasInputReference, inputMarketReference, baseValues);

        int expectedFallback = IntegerMath.ApplyPermille(200, 50);
        Assert.Equal(expectedFallback, baseValues[InputA]);
        Assert.Equal(expectedFallback, baseValues[InputB]);
    }

    /// <summary>
    /// 【核心】テスト表 #19。hasPreviousOutputOfferPrice = false → 観測があっても全入力フォールバック。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>hasPreviousOutputOfferPrice</c> の早期returnを削り、
    /// <c>previousOutputOfferPrice</c>(渡された値。ここでは0)をそのまま按分に使う変異を
    /// 当てたところ、許容原価合計が0になり <c>Assert.Equal(expectedFallback,
    /// baseValues[InputA])</c> が実際値0(初日に原材料を一切買わない)で失敗した(赤を確認)。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void DerivedDemandFallsBackWhenThereIsNoPreviousOutputPrice()
    {
        var recipe = TwoInputRecipe();
        var hasInputReference = new bool[Item.Count];
        var inputMarketReference = new int[Item.Count];
        var baseValues = new int[Item.Count];

        hasInputReference[InputA] = true;
        inputMarketReference[InputA] = 20;
        hasInputReference[InputB] = true;
        inputMarketReference[InputB] = 30;

        BuyerBudget.DerivedDemand(
            recipe,
            hasPreviousOutputOfferPrice: false,
            previousOutputOfferPrice: 0,
            minimumMarginPermille: 0,
            liquidFunds: 200,
            necessityFallbackRatioPermille: 50,
            hasInputReference, inputMarketReference, baseValues);

        int expectedFallback = IntegerMath.ApplyPermille(200, 50);
        Assert.Equal(expectedFallback, baseValues[InputA]);
        Assert.Equal(expectedFallback, baseValues[InputB]);
    }

    /// <summary>テスト表 #20。前日価格9・出力数量1・利幅200‰(許容原価合計7)、入力A(相場1・数量3) → 基礎値2(floor(7/3))。</summary>
    [Fact]
    public void DerivedDemandFloorsEveryDivision()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = InputA, Quantity = 3 } },
            laborPermille: 1);

        var hasInputReference = new bool[Item.Count];
        var inputMarketReference = new int[Item.Count];
        var baseValues = new int[Item.Count];

        hasInputReference[InputA] = true;
        inputMarketReference[InputA] = 1;

        BuyerBudget.DerivedDemand(
            recipe,
            hasPreviousOutputOfferPrice: true,
            previousOutputOfferPrice: 9,
            minimumMarginPermille: 200,
            liquidFunds: 0,
            necessityFallbackRatioPermille: 50,
            hasInputReference, inputMarketReference, baseValues);

        Assert.Equal(2, baseValues[InputA]);
    }
}
