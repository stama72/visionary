using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="OfferPrice"/>(GDD02 §8.1・§8.1.1・§6.3・§8.2.7、#35 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class OfferPriceTests
{
    // レシピの入力・出力に使う品目Id。テストの可読性のためItem.*とは別に短い名前を持つ。
    private const int ItemA = 0;
    private const int SelfHouseholdId = 0;
    private const int OtherSellerId = 1;
    private const int AnotherSellerId = 2;

    private static PriceObservation Observation(
        int itemId, int sellerId, int price, Tick observedAt) =>
        new()
        {
            ItemId = itemId,
            LocationId = 0,
            Price = price,
            SellerId = sellerId,
            ObservedAt = observedAt,
            Source = ObservationSource.Direct,
        };

    /// <summary>テスト表 #1。在庫1・目標3 → 334(切り下げなら333)。在庫0 → 0。在庫3・目標3 → 1000。</summary>
    [Fact]
    public void StockRatioIsCeiledAgainstTheShipmentTarget()
    {
        Assert.Equal(334, OfferPrice.StockRatioPermille(sellableStock: 1, shipmentTargetStock: 3));
        Assert.Equal(0, OfferPrice.StockRatioPermille(sellableStock: 0, shipmentTargetStock: 3));
        Assert.Equal(1000, OfferPrice.StockRatioPermille(sellableStock: 3, shipmentTargetStock: 3));
    }

    /// <summary>テスト表 #2。目標0・負 で ArgumentOutOfRangeException。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void StockRatioRejectsNonPositiveTarget(int shipmentTargetStock)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OfferPrice.StockRatioPermille(sellableStock: 1, shipmentTargetStock));
    }

    /// <summary>
    /// 【核心】テスト表 #3。在庫比1001 → 999(FloorDivなら1000)。在庫比1000 → 1000。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>OfferPrice.PriceCoefficientPermille</c> の
    /// <c>IntegerMath.CeilDiv(stockRatioPermille, 2)</c> を <c>IntegerMath.FloorDiv</c> に
    /// 変える変異を当てたところ、<c>Assert.Equal(999, ...(1001))</c> が実際値1000
    /// (FloorDiv(1001,2)=500、1500-500=1000)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void PriceCoefficientRoundsTheRatioUpButTheCoefficientDown()
    {
        Assert.Equal(999, OfferPrice.PriceCoefficientPermille(stockRatioPermille: 1001));
        Assert.Equal(1000, OfferPrice.PriceCoefficientPermille(stockRatioPermille: 1000));
    }

    /// <summary>
    /// テスト表 #4。在庫比0 → 1500、在庫比2000 → 500、在庫比2001 → 500(clampが無ければ499)、
    /// 在庫比10000 → 500。
    /// </summary>
    [Fact]
    public void PriceCoefficientIsClamped()
    {
        Assert.Equal(1500, OfferPrice.PriceCoefficientPermille(stockRatioPermille: 0));
        Assert.Equal(500, OfferPrice.PriceCoefficientPermille(stockRatioPermille: 2000));
        Assert.Equal(500, OfferPrice.PriceCoefficientPermille(stockRatioPermille: 2001));
        Assert.Equal(500, OfferPrice.PriceCoefficientPermille(stockRatioPermille: 10000));
    }

    /// <summary>テスト表 #5。原価100・利幅200‰ → 120。利幅0‰ → 100。</summary>
    [Fact]
    public void CostFloorAppliesTheMinimumMargin()
    {
        Assert.Equal(120, OfferPrice.CostFloor(unitCost: 100, minimumMarginPermille: 200, isBankrupt: 0));
        Assert.Equal(100, OfferPrice.CostFloor(unitCost: 100, minimumMarginPermille: 0, isBankrupt: 0));
    }

    /// <summary>
    /// 【核心】テスト表 #6。原価100・利幅200‰・フラグ1 → 50。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>OfferPrice.CostFloor</c> の
    /// <c>isBankrupt == 1 ? BankruptFloorPermille : ...</c> の分岐を削り、常に
    /// <c>IntegerMath.PermilleScale + minimumMarginPermille</c>(破産中フラグを見ない)に
    /// 変える変異を当てたところ、<c>Assert.Equal(50, ...)</c> が実際値120で失敗した(赤を確認)。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void BankruptSellerFloorsAtHalfTheCost()
    {
        Assert.Equal(50, OfferPrice.CostFloor(unitCost: 100, minimumMarginPermille: 200, isBankrupt: 1));
    }

    /// <summary>テスト表 #7。isBankrupt = 2 / -1 で ArgumentOutOfRangeException。</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public void CostFloorRejectsFlagsOutsideZeroAndOne(int isBankrupt)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OfferPrice.CostFloor(unitCost: 100, minimumMarginPermille: 200, isBankrupt));
    }

    /// <summary>
    /// 【核心】テスト表 #8。入力(単価8・数量1)→出力3のレシピ → 原価3(= CeilDiv(8,3))。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>OfferPrice.UnitCost</c> の戻り値の除算を
    /// <c>recipe.Outputs[0].Quantity</c> で割らずに合計をそのまま返す変異
    /// (出力数量で割らない)を当てたところ、<c>Assert.Equal(3, ...)</c> が実際値8で失敗した
    /// (赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void UnitCostNormalizesToOneOutputUnit()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = ItemA, Quantity = 3 } },
            inputs: new[] { new ItemQuantity { ItemId = ItemA + 1, Quantity = 1 } },
            laborPermille: 1);

        var purchaseUnitCostAverage = new int[Item.Count];
        purchaseUnitCostAverage[ItemA + 1] = 8;

        Assert.Equal(3, OfferPrice.UnitCost(recipe, purchaseUnitCostAverage));
    }

    /// <summary>テスト表 #9。入力(単価10・数量2)+(単価6・数量1)→出力1 → 26。</summary>
    [Fact]
    public void UnitCostSumsEveryInputTimesItsQuantity()
    {
        const int InputA = 0;
        const int InputB = 1;
        const int Output = 2;

        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Output, Quantity = 1 } },
            inputs: new[]
            {
                new ItemQuantity { ItemId = InputA, Quantity = 2 },
                new ItemQuantity { ItemId = InputB, Quantity = 1 },
            },
            laborPermille: 1);

        var purchaseUnitCostAverage = new int[Item.Count];
        purchaseUnitCostAverage[InputA] = 10;
        purchaseUnitCostAverage[InputB] = 6;

        Assert.Equal(26, OfferPrice.UnitCost(recipe, purchaseUnitCostAverage));
    }

    /// <summary>テスト表 #10。入力0件 / 出力2件 で NotSupportedException。</summary>
    [Fact]
    public void UnitCostRejectsRecipesWithNoInputOrManyOutputs()
    {
        var noInputRecipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = 0, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 1);

        Assert.Throws<NotSupportedException>(
            () => OfferPrice.UnitCost(noInputRecipe, new int[Item.Count]));

        var manyOutputsRecipe = new Recipe(
            Occupation.Miller,
            outputs: new[]
            {
                new ItemQuantity { ItemId = 0, Quantity = 1 },
                new ItemQuantity { ItemId = 1, Quantity = 1 },
            },
            inputs: new[] { new ItemQuantity { ItemId = 2, Quantity = 1 } },
            laborPermille: 1);

        Assert.Throws<NotSupportedException>(
            () => OfferPrice.UnitCost(manyOutputsRecipe, new int[Item.Count]));
    }

    /// <summary>
    /// 【核心】テスト表 #11。下限120・相場基準100・在庫0(係数1500‰)→ 150。
    /// 同じ下限で在庫が目標の2倍(係数500‰)→ 120。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>OfferPrice.Calculate</c> の <c>Math.Max</c> を
    /// <c>Math.Min</c> に変える変異を当てたところ、在庫0のケースで
    /// <c>Assert.Equal(150, ...)</c> が実際値120で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void OfferPriceTakesTheHigherOfFloorAndReference()
    {
        // 在庫比0(在庫0/目標10)→係数1500‰→ApplyPermille(100,1500)=150>120。
        Assert.Equal(
            150,
            OfferPrice.Calculate(
                costFloor: 120, marketReference: 100, sellableStock: 0, shipmentTargetStock: 10));

        // 在庫比2000(在庫20/目標10)→係数500‰→ApplyPermille(100,500)=50<120。
        Assert.Equal(
            120,
            OfferPrice.Calculate(
                costFloor: 120, marketReference: 100, sellableStock: 20, shipmentTargetStock: 10));
    }

    /// <summary>
    /// 【核心】テスト表 #12。売り手A(D8:100, D9:200)と売り手B(D9:300)、now=D10 → 250(= CeilDiv(500,2))。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>OfferPrice.TryMarketReference</c> の
    /// 売り手ごとの畳み込み(<c>SortedDictionary</c> への代入)を削り、レコード単位で全件を
    /// 合計する変異(<c>total</c> / <c>count</c> をループ内で直接加算)を当てたところ、
    /// <c>Assert.Equal(250, ...)</c> が実際値200(600/3。よく見る売り手Aの重みが増す)で
    /// 失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void MarketReferenceTakesTheLatestObservationPerSeller()
    {
        var now = Tick.FromDays(10);
        var observations = new List<PriceObservation>
        {
            Observation(ItemA, OtherSellerId, 100, Tick.FromDays(8)),
            Observation(ItemA, OtherSellerId, 200, Tick.FromDays(9)),
            Observation(ItemA, AnotherSellerId, 300, Tick.FromDays(9)),
        };

        bool found = OfferPrice.TryMarketReference(
            observations, ItemA, SelfHouseholdId, now,
            retentionDays: 7, hasOwnPreviousPrice: false, ownPreviousPrice: 0,
            out int marketReference);

        Assert.True(found);
        Assert.Equal(250, marketReference);
    }

    /// <summary>テスト表 #13。観測100と101の2件 → 101。</summary>
    [Fact]
    public void MarketReferenceIsCeiled()
    {
        var now = Tick.FromDays(10);
        var observations = new List<PriceObservation>
        {
            Observation(ItemA, OtherSellerId, 100, Tick.FromDays(9)),
            Observation(ItemA, AnotherSellerId, 101, Tick.FromDays(9)),
        };

        bool found = OfferPrice.TryMarketReference(
            observations, ItemA, SelfHouseholdId, now,
            retentionDays: 7, hasOwnPreviousPrice: false, ownPreviousPrice: 0,
            out int marketReference);

        Assert.True(found);
        Assert.Equal(101, marketReference);
    }

    /// <summary>
    /// 【核心】テスト表 #14。now=D10、観測がD10の1件だけ → false(相場基準が立たない)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b>鮮度の判定 <c>dayDifference &lt; 1</c> を
    /// <c>dayDifference &lt; 0</c> に変える変異(当日の観測を許してしまう)を当てたところ、
    /// <c>Assert.False(found)</c> が実際値trueで失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void MarketReferenceExcludesTodaysObservations()
    {
        var now = Tick.FromDays(10);
        var observations = new List<PriceObservation>
        {
            Observation(ItemA, OtherSellerId, 100, now),
        };

        bool found = OfferPrice.TryMarketReference(
            observations, ItemA, SelfHouseholdId, now,
            retentionDays: 7, hasOwnPreviousPrice: false, ownPreviousPrice: 0,
            out int marketReference);

        Assert.False(found);
    }

    /// <summary>テスト表 #15。retention=7・now=D10: D3は有効・D2は無効。D9は有効。</summary>
    [Theory]
    [InlineData(3, true)]
    [InlineData(2, false)]
    [InlineData(9, true)]
    public void MarketReferenceHonoursTheRetentionBoundary(int observedDay, bool expectedValid)
    {
        var now = Tick.FromDays(10);
        var observations = new List<PriceObservation>
        {
            Observation(ItemA, OtherSellerId, 100, Tick.FromDays(observedDay)),
        };

        bool found = OfferPrice.TryMarketReference(
            observations, ItemA, SelfHouseholdId, now,
            retentionDays: 7, hasOwnPreviousPrice: false, ownPreviousPrice: 0,
            out _);

        Assert.Equal(expectedValid, found);
    }

    /// <summary>テスト表 #16。自世帯Idの観測だけがある → false。</summary>
    [Fact]
    public void MarketReferenceExcludesOwnObservations()
    {
        var now = Tick.FromDays(10);
        var observations = new List<PriceObservation>
        {
            Observation(ItemA, SelfHouseholdId, 100, Tick.FromDays(9)),
        };

        bool found = OfferPrice.TryMarketReference(
            observations, ItemA, SelfHouseholdId, now,
            retentionDays: 7, hasOwnPreviousPrice: false, ownPreviousPrice: 0,
            out _);

        Assert.False(found);
    }

    /// <summary>テスト表 #17。別品目の観測だけがある → false。</summary>
    [Fact]
    public void MarketReferenceExcludesOtherItems()
    {
        var now = Tick.FromDays(10);
        var observations = new List<PriceObservation>
        {
            Observation(ItemA + 1, OtherSellerId, 100, Tick.FromDays(9)),
        };

        bool found = OfferPrice.TryMarketReference(
            observations, ItemA, SelfHouseholdId, now,
            retentionDays: 7, hasOwnPreviousPrice: false, ownPreviousPrice: 0,
            out _);

        Assert.False(found);
    }

    /// <summary>
    /// 【核心】テスト表 #18。他1件(200)+自前日(100) → 150。他0件+自前日(100) → false。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>latestBySeller.Count == 0</c> の早期returnを外し、
    /// 他の売り手の観測が0件でも自分の前日価格だけで相場基準を立てる変異
    /// (GDD06 §3.1が「純粋な自己ループ」と呼んだ状態)を当てたところ、
    /// <c>Assert.False(found)</c>(他0件のケース)が実際値true・marketReference=100で
    /// 失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void OwnPreviousPriceIsAddedOnlyWhenOtherSellersAreObserved()
    {
        var now = Tick.FromDays(10);

        var withOtherSeller = new List<PriceObservation>
        {
            Observation(ItemA, OtherSellerId, 200, Tick.FromDays(9)),
        };

        bool found = OfferPrice.TryMarketReference(
            withOtherSeller, ItemA, SelfHouseholdId, now,
            retentionDays: 7, hasOwnPreviousPrice: true, ownPreviousPrice: 100,
            out int marketReference);

        Assert.True(found);
        Assert.Equal(150, marketReference);

        bool foundWithoutOthers = OfferPrice.TryMarketReference(
            new List<PriceObservation>(), ItemA, SelfHouseholdId, now,
            retentionDays: 7, hasOwnPreviousPrice: true, ownPreviousPrice: 100,
            out _);

        Assert.False(foundWithoutOthers);
    }

    /// <summary>
    /// 【核心】テスト表 #19。他1件(200)・自前日なし → 200(件数1)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>hasOwnPreviousPrice</c> を無視して常に
    /// <c>total += ownPreviousPrice; count++;</c> を実行する変異(0を足して件数を2にする)を
    /// 当てたところ、<c>Assert.Equal(200, ...)</c> が実際値100(200+0を2で割る)で失敗した
    /// (赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void AbsentOwnPreviousPriceIsNotCountedAsZero()
    {
        var now = Tick.FromDays(10);
        var observations = new List<PriceObservation>
        {
            Observation(ItemA, OtherSellerId, 200, Tick.FromDays(9)),
        };

        bool found = OfferPrice.TryMarketReference(
            observations, ItemA, SelfHouseholdId, now,
            retentionDays: 7, hasOwnPreviousPrice: false, ownPreviousPrice: 0,
            out int marketReference);

        Assert.True(found);
        Assert.Equal(200, marketReference);
    }

    /// <summary>テスト表 #20。同一tick・同一売り手の2件(100→200の順で追加)→ 200。</summary>
    [Fact]
    public void MarketReferenceKeepsTheLaterEntryOnATie()
    {
        var now = Tick.FromDays(10);
        var sameTick = Tick.FromDays(9);
        var observations = new List<PriceObservation>
        {
            Observation(ItemA, OtherSellerId, 100, sameTick),
            Observation(ItemA, OtherSellerId, 200, sameTick),
        };

        bool found = OfferPrice.TryMarketReference(
            observations, ItemA, SelfHouseholdId, now,
            retentionDays: 7, hasOwnPreviousPrice: false, ownPreviousPrice: 0,
            out int marketReference);

        Assert.True(found);
        Assert.Equal(200, marketReference);
    }

    /// <summary>
    /// 【核心】タスク仕様テスト表 #9。旧20・単価20・β250‰ → 20(動かない)。旧20・単価40・β250‰ → 25。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-17)。</b><c>OfferPrice.UpdatedAcquisitionCost</c> を、2項をそれぞれ
    /// <c>IntegerMath.ApplyPermille</c> で丸めてから足す形(<c>ApplyPermille(previousAverage, 1000 -
    /// smoothingPermille) + ApplyPermille(unitPrice, smoothingPermille)</c>)に変える変異を
    /// 当てたところ、<c>Assert.Equal(20, ...(20, 20, 250))</c> が実際値21
    /// (<c>CeilDiv(20×750,1000)=15</c> と <c>CeilDiv(20×250,1000)=5</c> がそれぞれ切り上がり
    /// 15+5=20…とはならず、750‰側が <c>CeilDiv(15000,1000)=15</c> ちょうどで割り切れるため
    /// 実際は変異を当てても20のままでは崩れない。<b>そこで旧21・単価21・β250‰</b> の組で再試行
    /// したところ、<c>CeilDiv(21×750,1000)=CeilDiv(15750,1000)=16</c>、
    /// <c>CeilDiv(21×250,1000)=CeilDiv(5250,1000)=6</c>、16+6=22 が実際値になり、
    /// 「価格が動いていない日でも移動平均が1ずつ上がり続ける」ことを確認した(期待値21、実際値22。
    /// 赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void UpdatedAcquisitionCostRoundsOnlyOnce()
    {
        Assert.Equal(20, OfferPrice.UpdatedAcquisitionCost(previousAverage: 20, unitPrice: 20, smoothingPermille: 250));
        Assert.Equal(25, OfferPrice.UpdatedAcquisitionCost(previousAverage: 20, unitPrice: 40, smoothingPermille: 250));

        // 「動かない」ケースが丸め2回でも偶然一致しうるため(上の変異の実測メモ参照)、
        // 割り切れない組でも同じ性質(β適用後の合計をceilDivするのが正)を確かめる。
        Assert.Equal(21, OfferPrice.UpdatedAcquisitionCost(previousAverage: 21, unitPrice: 21, smoothingPermille: 250));
    }
}
