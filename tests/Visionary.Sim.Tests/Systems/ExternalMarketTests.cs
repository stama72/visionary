using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="ExternalMarket"/>(GDD02d §2〜§2.3、#38 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class ExternalMarketTests
{
    /// <summary>
    /// 【核心】テスト表 #1。§1 の境界表そのもの(木材加工の薪。外部買値10・出荷目標在庫108)。
    /// 相場基準 6 / 7 / 10 / 14 / 20 / 30 に対し 0 / 16 / 108 / 170 / 216 / 216。
    /// </summary>
    /// <remarks>
    /// <b>M-1(実測されたら書く)。</b><c>clamp</c> の上限を 2000 → 1000 にする変異が本テストを
    /// 赤にすることを期待する(タスク仕様)。
    /// </remarks>
    [Theory]
    [InlineData(6, 0)]
    [InlineData(7, 16)]
    [InlineData(10, 108)]
    [InlineData(14, 170)]
    [InlineData(20, 216)]
    [InlineData(30, 216)]
    public void ExportThresholdStockInvertsThePriceCoefficient(int marketReference, int expectedThreshold)
    {
        int actual = ExternalMarket.ExportThresholdStock(
            externalBuyPrice: 10,
            hasMarketReference: true,
            marketReference: marketReference,
            shipmentTargetStock: 108,
            isBankrupt: 0);

        Assert.Equal(expectedThreshold, actual);
    }

    /// <summary>テスト表 #2。相場基準が無い日 = 出荷目標在庫。</summary>
    [Fact]
    public void ExportThresholdStockFallsBackToTheShipmentTarget()
    {
        int actual = ExternalMarket.ExportThresholdStock(
            externalBuyPrice: 10, hasMarketReference: false, marketReference: 0,
            shipmentTargetStock: 108, isBankrupt: 0);

        Assert.Equal(108, actual);
    }

    /// <summary>テスト表 #3。破産中は相場基準の有無によらず0。</summary>
    [Theory]
    [InlineData(true, 10)]
    [InlineData(false, 0)]
    public void BankruptSellerExportsEverything(bool hasMarketReference, int marketReference)
    {
        int actual = ExternalMarket.ExportThresholdStock(
            externalBuyPrice: 10, hasMarketReference, marketReference,
            shipmentTargetStock: 108, isBankrupt: 1);

        Assert.Equal(0, actual);
    }

    /// <summary>
    /// テスト表 #4。木炭が夏(1250‰)と冬(750‰)で違う。都市生産品では false。
    /// </summary>
    [Fact]
    public void WindowOfferPriceAppliesTheSeasonCoefficient()
    {
        // 基準値(externalSellPriceBase)・外部買値は EconomySystemTestFixtures.BuildDefinition の
        // 既定(1次産品=1、都市生産品=1)に任せる ── 季節係数の行だけをCharcoalについて差し替える。
        var externalSellPriceSeasonPermille = new int[Item.Count][];
        for (int itemId = 0; itemId < Item.Count; itemId++)
        {
            externalSellPriceSeasonPermille[itemId] = new[] { 1000, 1000, 1000, 1000 };
        }

        // 木炭(Charcoal)。GDD02d §4.4: 夏1250‰・冬750‰(構造だけ借りる。基準値は1)。
        externalSellPriceSeasonPermille[Item.Charcoal] = new[] { 1000, 1250, 1000, 750 };

        var definition = EconomySystemTestFixtures.BuildDefinition(
            new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
                inputs: new[] { new ItemQuantity { ItemId = Item.Charcoal, Quantity = 1 } },
                laborPermille: 1000),
            externalSellPriceSeasonPermilleOverride: externalSellPriceSeasonPermille);

        // 夏(Summer)。
        var summerTick = new GameDate(year: 1, season: Season.Summer, dayOfSeason: 1, hourOfDay: 0).ToTick();
        bool foundSummer = ExternalMarket.TryOfferPrice(definition, summerTick, Item.Charcoal, out int summerPrice);
        Assert.True(foundSummer);
        Assert.Equal(2, summerPrice); // CeilDiv(基準値1×1250,1000) = 2。

        // 冬(Winter)。
        var winterTick = new GameDate(year: 1, season: Season.Winter, dayOfSeason: 1, hourOfDay: 0).ToTick();
        bool foundWinter = ExternalMarket.TryOfferPrice(definition, winterTick, Item.Charcoal, out int winterPrice);
        Assert.True(foundWinter);
        Assert.Equal(1, winterPrice); // CeilDiv(基準値1×750,1000) = 1。

        Assert.NotEqual(summerPrice, winterPrice);

        // 都市生産品(Flour、Millerの出力)ではfalse(0ではない)。
        bool foundForCityGood = ExternalMarket.TryOfferPrice(definition, summerTick, Item.Flour, out int cityGoodPrice);
        Assert.False(foundForCityGood);
        Assert.Equal(0, cityGoodPrice);
    }
}
