using Visionary.Sim.Systems;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="OfferPrice"/>(GDD02c §1・§1.1・§1.4、W2-08 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class OfferPriceTests
{
    /// <summary>在庫1・目標3 → 334(切り下げなら333)。在庫0 → 0。在庫3・目標3 → 1000。</summary>
    [Fact]
    public void StockRatioIsCeiledAgainstTheShipmentTarget()
    {
        Assert.Equal(334, OfferPrice.StockRatioPermille(sellableStock: 1, shipmentTargetStock: 3));
        Assert.Equal(0, OfferPrice.StockRatioPermille(sellableStock: 0, shipmentTargetStock: 3));
        Assert.Equal(1000, OfferPrice.StockRatioPermille(sellableStock: 3, shipmentTargetStock: 3));
    }

    /// <summary>目標0・負 で ArgumentOutOfRangeException。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void StockRatioRejectsNonPositiveTarget(int shipmentTargetStock)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OfferPrice.StockRatioPermille(sellableStock: 1, shipmentTargetStock));
    }

    /// <summary>
    /// 在庫比1001 → 999(FloorDivなら1000)。在庫比1000 → 1000。
    /// </summary>
    [Fact]
    public void PriceCoefficientRoundsTheRatioUpButTheCoefficientDown()
    {
        Assert.Equal(999, OfferPrice.PriceCoefficientPermille(stockRatioPermille: 1001));
        Assert.Equal(1000, OfferPrice.PriceCoefficientPermille(stockRatioPermille: 1000));
    }

    /// <summary>
    /// 在庫比0 → 1500、在庫比2000 → 500、在庫比2001 → 500(clampが無ければ499)、
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

    /// <summary>
    /// 【核心】タスク仕様テスト表 #6。相場基準100・在庫比2000‰(係数500‰)→ ApplyPermille(100,500)=50。
    /// 床 = 80 なら提示価格 80、床 = 30 なら 50。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-19)。</b><c>OfferPrice.Calculate</c> の <c>Math.Max</c> を
    /// <c>Math.Min</c> に変える変異を当てたところ、床80のケースの <c>Assert.Equal(80, ...)</c> が
    /// 実際値50(相場側が床より安いのにそちらを採ってしまう)で失敗した(赤を確認)。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void OfferPriceFloorIsExternalBuyPrice()
    {
        // 在庫比2000(在庫20/目標10)→係数500‰→ApplyPermille(100,500)=50。
        // hasSettledYesterday: true(頭打ちが効かない配置で床の分岐だけを見る)。
        Assert.Equal(
            80,
            OfferPrice.Calculate(
                floorPrice: 80, marketReference: 100, sellableStock: 20, shipmentTargetStock: 10, isBankrupt: 0,
                hasSettledYesterday: true));
        Assert.Equal(
            50,
            OfferPrice.Calculate(
                floorPrice: 30, marketReference: 100, sellableStock: 20, shipmentTargetStock: 10, isBankrupt: 0,
                hasSettledYesterday: true));
    }

    /// <summary>
    /// 【核心】タスク仕様テスト表 #1。床30・相場基準100・在庫0・目標10(係数1500‰)。
    /// hasSettledYesterday: false → 100(1000‰で頭打ち)。true → 150。
    /// </summary>
    [Fact]
    public void UnsoldSellerDoesNotRaiseAboveTheReference()
    {
        Assert.Equal(
            100,
            OfferPrice.Calculate(
                floorPrice: 30, marketReference: 100, sellableStock: 0, shipmentTargetStock: 10, isBankrupt: 0,
                hasSettledYesterday: false));
        Assert.Equal(
            150,
            OfferPrice.Calculate(
                floorPrice: 30, marketReference: 100, sellableStock: 0, shipmentTargetStock: 10, isBankrupt: 0,
                hasSettledYesterday: true));
    }

    /// <summary>
    /// 【核心】タスク仕様テスト表 #2。床30・相場基準100・在庫20・目標10(係数500‰)。
    /// hasSettledYesterday: false → 50(値下げ側には効かない。GDD02c §1.1)。
    /// </summary>
    [Fact]
    public void UnsoldCapDoesNotLiftTheDiscount()
    {
        Assert.Equal(
            50,
            OfferPrice.Calculate(
                floorPrice: 30, marketReference: 100, sellableStock: 20, shipmentTargetStock: 10, isBankrupt: 0,
                hasSettledYesterday: false));
    }

    /// <summary>
    /// タスク仕様テスト表 #3。床120・相場基準100・在庫0・目標10。hasSettledYesterday: false でも
    /// 床(120)を下回らない ── 頭打ちは係数に掛かり、床の <c>Math.Max</c> はそれより後に効く。
    /// </summary>
    [Fact]
    public void UnsoldCapKeepsTheFloor()
    {
        Assert.Equal(
            120,
            OfferPrice.Calculate(
                floorPrice: 120, marketReference: 100, sellableStock: 0, shipmentTargetStock: 10, isBankrupt: 0,
                hasSettledYesterday: false));
    }

    /// <summary>
    /// 【核心】タスク仕様テスト表 #4。破産中=1・相場基準100・販売在庫0(健全なら係数1500‰=150)
    /// → 50。床30のとき50、床80のとき80(床は破らない)。hasSettledYesterday が true でも
    /// false でも 50(破産中は500‰固定が§1.1の頭打ちより先に効くので、この規則は何もしない。
    /// GDD02c §1.1)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-19)。</b>破産中の枝で先に <c>StockRatioPermille</c> /
    /// <c>PriceCoefficientPermille</c> を評価してから捨てる書き方(在庫比を評価してしまう変異)に
    /// 変え、あわせて <c>shipmentTargetStock</c> に 0 を渡したところ、
    /// <c>ArgumentOutOfRangeException</c>(出荷目標在庫は1以上)が飛んだ(赤を確認 ──
    /// 破産中は在庫比を評価しないはずなのに評価してしまっている)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void BankruptSellerFixesCoefficientAtFivehundred()
    {
        // shipmentTargetStock=0は「在庫比を評価しない」ことを確かめるための罠 ──
        // 評価すればArgumentOutOfRangeExceptionが飛ぶ。
        foreach (bool hasSettledYesterday in new[] { false, true })
        {
            Assert.Equal(
                50,
                OfferPrice.Calculate(
                    floorPrice: 30, marketReference: 100, sellableStock: 0, shipmentTargetStock: 0, isBankrupt: 1,
                    hasSettledYesterday));
            Assert.Equal(
                80,
                OfferPrice.Calculate(
                    floorPrice: 80, marketReference: 100, sellableStock: 0, shipmentTargetStock: 0, isBankrupt: 1,
                    hasSettledYesterday));
        }
    }

    /// <summary>isBankrupt = 2 / -1 で ArgumentOutOfRangeException。</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public void CalculateRejectsFlagsOutsideZeroAndOne(int isBankrupt)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OfferPrice.Calculate(
            floorPrice: 100, marketReference: 100, sellableStock: 5, shipmentTargetStock: 5, isBankrupt,
            hasSettledYesterday: true));
    }

    /// <summary>
    /// タスク仕様テスト表 #9。旧20・単価20・β250‰ → 20(動かない)。旧20・単価40・β250‰ → 25。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-17、#96 実装当時の記録)。</b><c>OfferPrice.UpdatedAcquisitionCost</c> を、
    /// 2項をそれぞれ <c>IntegerMath.ApplyPermille</c> で丸めてから足す形に変える変異を当てたところ、
    /// 旧21・単価21・β250‰ の組で期待値21に対し実際値22(価格が動いていない日でも移動平均が
    /// 1ずつ上がり続ける)になった(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void UpdatedAcquisitionCostRoundsOnlyOnce()
    {
        Assert.Equal(20, OfferPrice.UpdatedAcquisitionCost(previousAverage: 20, unitPrice: 20, smoothingPermille: 250));
        Assert.Equal(25, OfferPrice.UpdatedAcquisitionCost(previousAverage: 20, unitPrice: 40, smoothingPermille: 250));

        // 「動かない」ケースが丸め2回でも偶然一致しうるため、割り切れない組でも同じ性質
        // (β適用後の合計をceilDivするのが正)を確かめる。
        Assert.Equal(21, OfferPrice.UpdatedAcquisitionCost(previousAverage: 21, unitPrice: 21, smoothingPermille: 250));
    }
}
