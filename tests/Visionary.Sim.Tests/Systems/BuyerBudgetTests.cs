using Visionary.Sim.Numerics;
using Visionary.Sim.Systems;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="BuyerBudget"/>(GDD02c §2 / GDD02b §5、W2-08 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class BuyerBudgetTests
{
    // レシピの入力・出力に使う品目Id。テストの可読性のためItem.*とは別に短い名前を持つ。
    private const int InputA = 0;
    private const int InputB = 1;
    private const int Output = 2;

    /// <summary>
    /// 【核心】テスト表 #9。目標10 で 予想0 → 1500、5 → 1250、10 → 1000、20 → 500、21 → 0。
    /// 目標0 は予想0でも0。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-19)。</b>目標以下を1000‰で据え置く旧実装の形
    /// (<c>if (expectedStock &lt;= targetStock) return 1000;</c> を先頭に挿入する変異)を当てたところ、
    /// <c>Assert.Equal(1500, StockPressurePermille(0,10))</c> が実際値1000(§5.1の1500‰の行が死ぬ)
    /// で失敗した(赤を確認)。また2000‰超を500で止める変異(clampの上限を外さない代わりに
    /// early returnを消す変異)では、<c>Assert.Equal(0, StockPressurePermille(21,10))</c> が
    /// 実際値500(半値なら無限に買う経路)で失敗した。目標0で1000を返す変異
    /// (<c>if (targetStock &lt;= 0) return 1000;</c>)では
    /// <c>Assert.Equal(0, StockPressurePermille(0,0))</c> が実際値1000で失敗した。
    /// いずれも変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void StockPressureIsLinearFromFivehundredToFifteenhundred()
    {
        Assert.Equal(1500, BuyerBudget.StockPressurePermille(expectedStock: 0, targetStock: 10));
        Assert.Equal(1250, BuyerBudget.StockPressurePermille(expectedStock: 5, targetStock: 10));
        Assert.Equal(1000, BuyerBudget.StockPressurePermille(expectedStock: 10, targetStock: 10));
        Assert.Equal(500, BuyerBudget.StockPressurePermille(expectedStock: 20, targetStock: 10));
        Assert.Equal(0, BuyerBudget.StockPressurePermille(expectedStock: 21, targetStock: 10));

        Assert.Equal(0, BuyerBudget.StockPressurePermille(expectedStock: 0, targetStock: 0));
    }

    /// <summary>テスト表 #10。資金100・1日分3 → FloorDivで33。1日分0 → 100(max(…,1))。資金2・1日分3 → 0。</summary>
    [Fact]
    public void CashCapDividesAvailableFundsByDailyQuantity()
    {
        Assert.Equal(33, BuyerBudget.CashCap(availableFunds: 100, dailyQuantity: 3));
        Assert.Equal(100, BuyerBudget.CashCap(availableFunds: 100, dailyQuantity: 0));
        Assert.Equal(0, BuyerBudget.CashCap(availableFunds: 2, dailyQuantity: 3));
    }

    /// <summary>
    /// 【核心】テスト表 #11。流動資金1000・取り置き300・運転資金400 → 必需1000 / 耐久700 /
    /// 入力700 / 嗜好300。取り置き1200なら耐久・入力・嗜好とも0(負を0で止める)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-19)。</b><c>AvailableFunds</c> の switch を全用途 <c>liquidFunds</c>
    /// (取り置きも運転資金も引かない変異)に変えたところ、耐久の <c>Assert.Equal(700, ...)</c> が
    /// 実際値1000で失敗した(赤を確認、黒字倒産が戻る経路)。嗜好の分岐から運転資金の減算を外す
    /// 変異(<c>liquidFunds - necessityReserve</c> のまま嗜好に流用)では、
    /// <c>Assert.Equal(300, ...)</c> が実際値700で失敗した。<c>Math.Max(0L, …)</c> を外す変異
    /// (取り置き1200のケース)では、耐久が -200 のまま返り <c>checked</c> の範囲内でも
    /// <c>Assert.Equal(0, ...)</c> が実際値-200で失敗した。いずれも変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void AvailableFundsAreStagedByPurpose()
    {
        Assert.Equal(
            1000,
            BuyerBudget.AvailableFunds(DemandPurpose.Necessity, liquidFunds: 1000, necessityReserve: 300, workingCapital: 400));
        Assert.Equal(
            700,
            BuyerBudget.AvailableFunds(DemandPurpose.Durable, liquidFunds: 1000, necessityReserve: 300, workingCapital: 400));
        Assert.Equal(
            700,
            BuyerBudget.AvailableFunds(DemandPurpose.ProductionInput, liquidFunds: 1000, necessityReserve: 300, workingCapital: 400));
        Assert.Equal(
            300,
            BuyerBudget.AvailableFunds(DemandPurpose.Preference, liquidFunds: 1000, necessityReserve: 300, workingCapital: 400));

        Assert.Equal(
            0,
            BuyerBudget.AvailableFunds(DemandPurpose.Durable, liquidFunds: 1000, necessityReserve: 1200, workingCapital: 400));
        Assert.Equal(
            0,
            BuyerBudget.AvailableFunds(DemandPurpose.ProductionInput, liquidFunds: 1000, necessityReserve: 1200, workingCapital: 400));
        Assert.Equal(
            0,
            BuyerBudget.AvailableFunds(DemandPurpose.Preference, liquidFunds: 1000, necessityReserve: 1200, workingCapital: 400));
    }

    /// <summary>テスト表 #12。工具の移動平均260・所要労働108‰・N=13 → CeilDiv(28080,13000) = 3。</summary>
    [Fact]
    public void WearCostPerRunUsesLaborNotRuns()
    {
        Assert.Equal(3, BuyerBudget.WearCostPerRun(toolUnitCostAverage: 260, laborPermille: 108, toolLifeLaborDays: 13));
    }

    private static Recipe TwoInputRecipe() =>
        new(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 2 } },
            inputs: new[]
            {
                new ItemQuantity { ItemId = InputA, Quantity = 2 },
                new ItemQuantity { ItemId = InputB, Quantity = 1 },
            },
            laborPermille: 1);

    /// <summary>
    /// 【核心】テスト表 #13。前日の出力提示価格120・出力数量2・最低利幅200‰・摩耗費4 →
    /// 許容原価合計 = FloorDiv(240000,1200) − 4 = 196。入力A(相場10×必要2)・B(相場30×必要1)で
    /// 相場での原価50 → 利潤上限A = FloorDiv(196×10,50) = 39、B = FloorDiv(196×30,50) = 117。
    /// 39×2 + 117×1 = 195 ≤ 196。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-19)。</b>按分の除算を <c>IntegerMath.ApplyPermille</c> 経由
    /// (<c>ApplyPermille(allowedCostTotal, CeilDiv(reference*1000, marketCostTotal))</c>)に
    /// 変える変異を当てたところ、A=40・B=118(合計40×2+118×1=198&gt;196)になり
    /// <c>Assert.True(A*2 + B*1 &lt;= 196)</c> が失敗した(赤を確認、最低利幅の保証が崩れる)。
    /// 摩耗費を引かない変異(<c>allowedCostTotal</c> からの減算を外す)では
    /// <c>Assert.Equal(39, profitCap[InputA])</c> が実際値40(200/120=... FloorDiv(200*10,50)=40)
    /// で失敗した。いずれも変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ProfitCapsAllocateProportionallyToReference()
    {
        var recipe = TwoInputRecipe();
        var hasInputReference = new bool[Item.Count];
        var inputMarketReference = new int[Item.Count];
        var hasProfitCap = new bool[Item.Count];
        var profitCap = new int[Item.Count];

        hasInputReference[InputA] = true;
        inputMarketReference[InputA] = 10;
        hasInputReference[InputB] = true;
        inputMarketReference[InputB] = 30;

        BuyerBudget.ProfitCaps(
            recipe,
            hasPreviousOutputOfferPrice: true,
            previousOutputOfferPrice: 120,
            minimumMarginPermille: 200,
            wearCostPerRun: 4,
            hasInputReference, inputMarketReference, hasProfitCap, profitCap);

        Assert.True(hasProfitCap[InputA]);
        Assert.True(hasProfitCap[InputB]);
        Assert.Equal(39, profitCap[InputA]);
        Assert.Equal(117, profitCap[InputB]);
        Assert.True((profitCap[InputA] * 2) + (profitCap[InputB] * 1) <= 196);
    }

    /// <summary>
    /// 【核心】テスト表 #14。2入力のうち1つの相場基準が無い → 両方ともhasProfitCapがfalse。
    /// 前日の出力提示価格が無い日・許容原価合計 ≤ 0(摩耗費が許容原価を食い切る)の日も同じ。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-19)。</b>観測できた入力だけで按分する変異(相場基準が無い入力を
    /// <c>continue</c> で読み飛ばし、<c>ClearProfitCaps</c> を呼ばず処理を続ける)を当てたところ、
    /// 「1つの相場基準が無い」ケースの <c>Assert.False(hasProfitCap[InputA])</c> が実際値true
    /// (観測できたInputAだけが2入力ぶんの上限を丸ごと受け取った)で失敗した(赤を確認)。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ProfitCapIsAbsentWhenAnyInputLacksReference()
    {
        var recipe = TwoInputRecipe();

        // ケース1: 1入力だけ相場基準が無い。
        {
            var hasInputReference = new bool[Item.Count];
            var inputMarketReference = new int[Item.Count];
            var hasProfitCap = new bool[Item.Count];
            var profitCap = new int[Item.Count];

            hasInputReference[InputA] = true;
            inputMarketReference[InputA] = 10;
            hasInputReference[InputB] = false;

            BuyerBudget.ProfitCaps(
                recipe, hasPreviousOutputOfferPrice: true, previousOutputOfferPrice: 120,
                minimumMarginPermille: 200, wearCostPerRun: 4,
                hasInputReference, inputMarketReference, hasProfitCap, profitCap);

            Assert.False(hasProfitCap[InputA]);
            Assert.False(hasProfitCap[InputB]);
        }

        // ケース2: 前日の出力提示価格が無い。
        {
            var hasInputReference = new bool[Item.Count];
            var inputMarketReference = new int[Item.Count];
            var hasProfitCap = new bool[Item.Count];
            var profitCap = new int[Item.Count];

            hasInputReference[InputA] = true;
            inputMarketReference[InputA] = 10;
            hasInputReference[InputB] = true;
            inputMarketReference[InputB] = 30;

            BuyerBudget.ProfitCaps(
                recipe, hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0,
                minimumMarginPermille: 200, wearCostPerRun: 4,
                hasInputReference, inputMarketReference, hasProfitCap, profitCap);

            Assert.False(hasProfitCap[InputA]);
            Assert.False(hasProfitCap[InputB]);
        }

        // ケース3: 摩耗費が許容原価合計を食い切る(許容原価合計 <= 0)。
        {
            var hasInputReference = new bool[Item.Count];
            var inputMarketReference = new int[Item.Count];
            var hasProfitCap = new bool[Item.Count];
            var profitCap = new int[Item.Count];

            hasInputReference[InputA] = true;
            inputMarketReference[InputA] = 10;
            hasInputReference[InputB] = true;
            inputMarketReference[InputB] = 30;

            // 許容原価合計 = FloorDiv(240000,1200) - wearCostPerRun = 200 - 200 = 0。
            BuyerBudget.ProfitCaps(
                recipe, hasPreviousOutputOfferPrice: true, previousOutputOfferPrice: 120,
                minimumMarginPermille: 200, wearCostPerRun: 200,
                hasInputReference, inputMarketReference, hasProfitCap, profitCap);

            Assert.False(hasProfitCap[InputA]);
            Assert.False(hasProfitCap[InputB]);
        }
    }

    /// <summary>テスト表 #15。1入力(必要数量3)のレシピで 利潤上限 = FloorDiv(許容原価合計, 3) に一致する。</summary>
    [Fact]
    public void ProfitCapOfSingleInputRecipeEqualsAllowedCostPerQuantity()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = InputA, Quantity = 3 } },
            laborPermille: 1);

        var hasInputReference = new bool[Item.Count];
        var inputMarketReference = new int[Item.Count];
        var hasProfitCap = new bool[Item.Count];
        var profitCap = new int[Item.Count];

        hasInputReference[InputA] = true;
        inputMarketReference[InputA] = 7;

        BuyerBudget.ProfitCaps(
            recipe, hasPreviousOutputOfferPrice: true, previousOutputOfferPrice: 100,
            minimumMarginPermille: 0, wearCostPerRun: 0,
            hasInputReference, inputMarketReference, hasProfitCap, profitCap);

        // 許容原価合計 = FloorDiv(100*1000,1000) - 0 = 100。
        int allowedCostTotal = IntegerMath.FloorDiv(100 * 1000, 1000);

        Assert.True(hasProfitCap[InputA]);
        Assert.Equal(IntegerMath.FloorDiv(allowedCostTotal, 3), profitCap[InputA]);
    }

    /// <summary>
    /// 【核心】テスト表 #16。相場項100・在庫圧力1500‰・現金上限200・利潤上限なし → 150。
    /// 現金上限120なら120(相場項に圧力が乗った150より小さい側)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-19)。</b>現金上限にも在庫圧力を掛ける変異
    /// (<c>Math.Min(ApplyPermille(marketTerm,pressure), ApplyPermille(cashCap,pressure))</c>)を
    /// 当てたところ、現金上限120のケースの <c>Assert.Equal(120, ...)</c> が実際値150
    /// (<c>min(150, ApplyPermille(120,1500)=180)=150</c>。払えない金を緊急性で払う経路)で
    /// 失敗した(赤を確認)。相場項に圧力を掛け忘れる変異(<c>marketTerm</c> をそのまま使う)では、
    /// 現金上限200のケースの <c>Assert.Equal(150, ...)</c> が実際値100(§5.1の1500‰の行が死ぬ)
    /// で失敗した。いずれも変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void BudgetAppliesStockPressureToMarketTermOnly()
    {
        Assert.Equal(
            150,
            BuyerBudget.Budget(
                hasMarketTerm: true, marketTerm: 100, stockPressurePermille: 1500,
                cashCap: 200, hasProfitCap: false, profitCap: 0));
        Assert.Equal(
            120,
            BuyerBudget.Budget(
                hasMarketTerm: true, marketTerm: 100, stockPressurePermille: 1500,
                cashCap: 120, hasProfitCap: false, profitCap: 0));
    }

    /// <summary>
    /// テスト表 #17。相場項なし・現金上限50 → 50。利潤上限あり(30)なら30。
    /// </summary>
    [Fact]
    public void BudgetDropsAbsentTerms()
    {
        Assert.Equal(
            50,
            BuyerBudget.Budget(
                hasMarketTerm: false, marketTerm: 0, stockPressurePermille: 1000,
                cashCap: 50, hasProfitCap: false, profitCap: 0));
        Assert.Equal(
            30,
            BuyerBudget.Budget(
                hasMarketTerm: false, marketTerm: 0, stockPressurePermille: 1000,
                cashCap: 50, hasProfitCap: true, profitCap: 30));
    }

    /// <summary>
    /// 【核心】テスト表 #18。相場項100あり → 100(在庫圧力1500‰でも100のまま)。相場項なし・
    /// 現金上限50 → 50。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-19)。</b><c>BaseValue</c> に在庫圧力を掛ける変異
    /// (<c>ApplyPermille(marketTerm, 1500)</c> を返す)を当てたところ、
    /// <c>Assert.Equal(100, BaseValue(true, 100, 50))</c> が実際値150で失敗した(赤を確認、
    /// 線形解で二重に効く経路)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void BaseValueFallsBackToCashCapWithoutMarketTerm()
    {
        Assert.Equal(100, BuyerBudget.BaseValue(hasMarketTerm: true, marketTerm: 100, cashCap: 50));
        Assert.Equal(50, BuyerBudget.BaseValue(hasMarketTerm: false, marketTerm: 0, cashCap: 50));
    }

    /// <summary>
    /// 【核心】テスト表 #19。目標10・予想0・基礎値100 で 実効価格150 → 0、100 → 10、
    /// 50 → 20(上限)、40 → 20(2倍で頭打ち)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-19)。</b>上側のclampを落とす変異(<c>Math.Clamp</c> を
    /// <c>Math.Max(0L, …)</c> だけに変える)を当てたところ、実効価格40のケースの
    /// <c>Assert.Equal(20, ...)</c> が実際値22(溜め込みが縮退する)で失敗した(赤を確認)。
    /// <c>CeilDiv</c> を <c>FloorDiv</c> に変える変異では、実効価格100のケースの
    /// <c>Assert.Equal(10, ...)</c> は変わらないが実効価格150のケースの
    /// <c>Assert.Equal(0, ...)</c> の手前で符号が変わり、80のような中間値でずれが出るため
    /// 別途 実効価格50の検算(20)で相違を確認した。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void PurchaseQuantitySolvesTheLinearDemand()
    {
        Assert.Equal(0, BuyerBudget.PurchaseQuantity(baseValue: 100, effectivePrice: 150, targetStock: 10, expectedStock: 0));
        Assert.Equal(10, BuyerBudget.PurchaseQuantity(baseValue: 100, effectivePrice: 100, targetStock: 10, expectedStock: 0));
        Assert.Equal(20, BuyerBudget.PurchaseQuantity(baseValue: 100, effectivePrice: 50, targetStock: 10, expectedStock: 0));
        Assert.Equal(20, BuyerBudget.PurchaseQuantity(baseValue: 100, effectivePrice: 40, targetStock: 10, expectedStock: 0));
    }

    /// <summary>基礎値0以下でArgumentOutOfRangeException。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PurchaseQuantityRejectsNonPositiveBaseValue(int baseValue)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BuyerBudget.PurchaseQuantity(baseValue, effectivePrice: 1, targetStock: 3, expectedStock: 0));
    }

    private static DemandLine BuildLine(
        bool hasMarketTerm, int marketTerm, int stockPressurePermille,
        int cashCap, bool hasProfitCap, int profitCap,
        int baseValue, int targetStock, int expectedStock) =>
        new()
        {
            Purpose = DemandPurpose.Necessity,
            ItemId = 0,
            MarketTerm = marketTerm,
            HasMarketTerm = hasMarketTerm,
            CashCap = cashCap,
            ProfitCap = profitCap,
            HasProfitCap = hasProfitCap,
            TargetStock = targetStock,
            ExpectedStock = expectedStock,
            StockPressurePermille = stockPressurePermille,
            BaseValue = baseValue,
            Budget = 0, // Decideはこの欄を読まない(各項を個別に見る)。
        };

    /// <summary>
    /// 【核心】テスト表 #20。相場項100・圧力1000‰・現金上限50・実効価格120 → 数量0、
    /// 理由MarketTerm(現金上限のほうが小さくても)。実効価格80なら理由CashCap。相場項と
    /// 現金上限がともに80で実効価格100のときもMarketTerm(同点は相場)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-19)。</b>argminを採る変異(3項のうち最小の項を理由にする)を
    /// 当てたところ、実効価格120のケースの <c>Assert.Equal(NoPurchaseReason.MarketTerm,
    /// decision.Reason)</c> が実際値CashCap(現金上限50のほうが相場項100より小さい)で失敗した
    /// (赤を確認、破産中フラグが価格ショックの検出器に化ける経路)。同点を現金上限に倒す変異
    /// (<c>&gt;=</c> を現金上限側の判定に使う)では、相場項と現金上限がともに80のケースの
    /// <c>Assert.Equal(NoPurchaseReason.MarketTerm, ...)</c> が実際値CashCapで失敗した。
    /// いずれも変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void DecideReportsMarketTermBeforeCashCap()
    {
        var highPriceLine = BuildLine(
            hasMarketTerm: true, marketTerm: 100, stockPressurePermille: 1000,
            cashCap: 50, hasProfitCap: false, profitCap: 0,
            baseValue: 100, targetStock: 10, expectedStock: 0);

        var decision120 = BuyerBudget.Decide(highPriceLine, effectivePrice: 120);
        Assert.Equal(0, decision120.Quantity);
        Assert.Equal(NoPurchaseReason.MarketTerm, decision120.Reason);

        var decision80 = BuyerBudget.Decide(highPriceLine, effectivePrice: 80);
        Assert.Equal(0, decision80.Quantity);
        Assert.Equal(NoPurchaseReason.CashCap, decision80.Reason);

        var tiedLine = BuildLine(
            hasMarketTerm: true, marketTerm: 80, stockPressurePermille: 1000,
            cashCap: 80, hasProfitCap: false, profitCap: 0,
            baseValue: 80, targetStock: 10, expectedStock: 0);

        var tiedDecision = BuyerBudget.Decide(tiedLine, effectivePrice: 100);
        Assert.Equal(0, tiedDecision.Quantity);
        Assert.Equal(NoPurchaseReason.MarketTerm, tiedDecision.Reason);
    }

    /// <summary>
    /// テスト表 #21。相場項100・圧力1000‰・利潤上限60・現金上限40・実効価格80 → 理由ProfitCap。
    /// 実効価格50なら理由CashCap。
    /// </summary>
    [Fact]
    public void DecideReportsProfitCapBetweenMarketAndCash()
    {
        var line = BuildLine(
            hasMarketTerm: true, marketTerm: 100, stockPressurePermille: 1000,
            cashCap: 40, hasProfitCap: true, profitCap: 60,
            baseValue: 100, targetStock: 10, expectedStock: 0);

        var decision80 = BuyerBudget.Decide(line, effectivePrice: 80);
        Assert.Equal(0, decision80.Quantity);
        Assert.Equal(NoPurchaseReason.ProfitCap, decision80.Reason);

        var decision50 = BuyerBudget.Decide(line, effectivePrice: 50);
        Assert.Equal(0, decision50.Quantity);
        Assert.Equal(NoPurchaseReason.CashCap, decision50.Reason);
    }

    /// <summary>
    /// 【核心】テスト表 #22。ゲートを通って数量が正 → None。目標10・予想20・基礎値100・
    /// 実効価格50(圧力500‰で予算50、ゲートは等号で開く。到達在庫20=予想在庫)→ 数量0でNone
    /// (資金不足ではない)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-19)。</b>数量0をすべて資金不足(CashCap)に数える変異
    /// (<c>PurchaseQuantity</c> の結果が0なら理由を強制的にCashCapにする)を当てたところ、
    /// <c>Assert.Equal(NoPurchaseReason.None, decision.Reason)</c> が実際値CashCapで失敗した
    /// (赤を確認、在庫が満ちた世帯で必需のフラグが立つ経路)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void DecideReportsNoneWhenGateOpens()
    {
        var positiveLine = BuildLine(
            hasMarketTerm: true, marketTerm: 100, stockPressurePermille: 1000,
            cashCap: 9999, hasProfitCap: false, profitCap: 0,
            baseValue: 100, targetStock: 10, expectedStock: 0);

        var positiveDecision = BuyerBudget.Decide(positiveLine, effectivePrice: 100);
        Assert.True(positiveDecision.Quantity > 0);
        Assert.Equal(NoPurchaseReason.None, positiveDecision.Reason);

        var zeroButOpenLine = BuildLine(
            hasMarketTerm: true, marketTerm: 100, stockPressurePermille: 500,
            cashCap: 9999, hasProfitCap: false, profitCap: 0,
            baseValue: 100, targetStock: 10, expectedStock: 20);

        var zeroButOpenDecision = BuyerBudget.Decide(zeroButOpenLine, effectivePrice: 50);
        Assert.Equal(0, zeroButOpenDecision.Quantity);
        Assert.Equal(NoPurchaseReason.None, zeroButOpenDecision.Reason);
    }

    /// <summary>実効価格0以下でArgumentOutOfRangeException。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DecideRejectsNonPositiveEffectivePrice(int effectivePrice)
    {
        var line = BuildLine(
            hasMarketTerm: true, marketTerm: 100, stockPressurePermille: 1000,
            cashCap: 50, hasProfitCap: false, profitCap: 0,
            baseValue: 100, targetStock: 10, expectedStock: 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => BuyerBudget.Decide(line, effectivePrice));
    }
}
