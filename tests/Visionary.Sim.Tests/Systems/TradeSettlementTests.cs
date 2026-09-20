using Visionary.Sim.Systems;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="TradeSettlement"/>(GDD02b §3・§3.2 / TDD01 §3.2、#37 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class TradeSettlementTests
{
    private const int BuyerId = 0;
    private const int SellerId = 1;
    private const int ItemA = 0;

    /// <summary>買い手(0)・売り手(1)の2戸の世界。それぞれ単一構成員。</summary>
    private static World BuildWorld()
    {
        var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);

        world.Npcs[0].Rank = NpcRank.Master;
        world.Households[0] = new HouseholdState(
            id: BuyerId, districtId: 0, headNpcId: 0, memberNpcIds: new[] { 0 }, itemCount: Item.Count);
        world.Households[0].Occupation = Occupation.Miller;
        world.Households[0].LiquidFunds = 1000;

        world.Npcs[1].Rank = NpcRank.Master;
        world.Households[1] = new HouseholdState(
            id: SellerId, districtId: 0, headNpcId: 1, memberNpcIds: new[] { 1 }, itemCount: Item.Count);
        world.Households[1].Occupation = Occupation.Miller;
        world.Households[1].WorkshopInventory[ItemA] = 100;

        return world;
    }

    /// <summary>【核心】テスト表 #7。流動資金100・実効価格30 → 3。流動資金29・実効価格30 → 0。</summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-17)。</b><c>TradeSettlement.FundsCap</c> を <c>IntegerMath.CeilDiv</c>
    /// に変える変異を当てたところ、<c>Assert.Equal(3, FundsCap(100, 30))</c> が実際値4
    /// (4個買えることになり流動資金が負に落ちる経路)で失敗した(赤を確認)。変異を戻して
    /// 緑に復帰させた。
    /// </remarks>
    [Fact]
    public void FundsCapRoundsDown()
    {
        Assert.Equal(3, TradeSettlement.FundsCap(liquidFunds: 100, unitEffectivePrice: 30));
        Assert.Equal(0, TradeSettlement.FundsCap(liquidFunds: 29, unitEffectivePrice: 30));
    }

    /// <summary>テスト表 #8。実効価格0 / -1 で ArgumentOutOfRangeException。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FundsCapRejectsNonPositivePrice(int unitEffectivePrice)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TradeSettlement.FundsCap(liquidFunds: 100, unitEffectivePrice));
    }

    /// <summary>
    /// 【核心】テスト表 #16。数量2・単価30 → 買い手 -60 / 売り手 +60、売り手の工房在庫 -2、
    /// 帳簿が双方に1行ずつ(Purchase と Sale、CounterpartyId が相手)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-17)。</b><c>TradeSettlement.Execute</c> の売り手側の帳簿追加
    /// (<c>world.Ledgers[seller.Id].Add(...)</c>)を削る変異を当てたところ、
    /// <c>Assert.Single(world.Ledgers[SellerId])</c> が実際値0件で失敗した(赤を確認、
    /// 売り手の帳簿と流動資金が突合しない経路)。さらに売り手側の <c>Direction</c> を
    /// <c>LedgerDirection.Purchase</c>(両方 Purchase にする)に変える変異を当てたところ、
    /// <c>Assert.Equal(LedgerDirection.Sale, sellerEntry.Direction)</c> が実際値Purchaseで
    /// 失敗した(赤を確認、帳簿から資金を復元すると符号が逆になる経路)。
    /// いずれも変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ExecuteMovesFundsInventoryAndBothLedgers()
    {
        var world = BuildWorld();
        var buyer = world.Households[BuyerId];
        var seller = world.Households[SellerId];

        int buyerFundsBefore = buyer.LiquidFunds;
        int sellerFundsBefore = seller.LiquidFunds;

        TradeSettlement.Execute(
            world, buyer, seller, DemandPurpose.Necessity, ItemA,
            quantity: 2, unitEffectivePrice: 30, acquisitionCostSmoothingPermille: 250);

        Assert.Equal(buyerFundsBefore - 60, buyer.LiquidFunds);
        Assert.Equal(sellerFundsBefore + 60, seller.LiquidFunds);
        Assert.Equal(98, seller.WorkshopInventory[ItemA]); // 100 - 2

        var buyerEntry = Assert.Single(world.Ledgers[BuyerId]);
        Assert.Equal(LedgerDirection.Purchase, buyerEntry.Direction);
        Assert.Equal(SellerId, buyerEntry.CounterpartyId);
        Assert.Equal(2, buyerEntry.Quantity);
        Assert.Equal(30, buyerEntry.UnitPrice);

        var sellerEntry = Assert.Single(world.Ledgers[SellerId]);
        Assert.Equal(LedgerDirection.Sale, sellerEntry.Direction);
        Assert.Equal(BuyerId, sellerEntry.CounterpartyId);
        Assert.Equal(2, sellerEntry.Quantity);
        Assert.Equal(30, sellerEntry.UnitPrice);
    }

    /// <summary>
    /// 【核心】テスト表 #17。Necessity で買った薪 → 世帯在庫、ProductionInput で買った薪 → 工房在庫。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-17)。</b><c>TradeSettlement.Execute</c> の在庫振り分けを常に
    /// <c>buyer.WorkshopInventory[itemId] += quantity;</c>(用途を見ない)に変える変異を
    /// 当てたところ、<c>Assert.Equal(3, buyer.HouseholdInventory[Item.Firewood])</c>
    /// (Necessityのケース)が実際値0で失敗した(赤を確認、暖房用の薪が工房在庫に入り
    /// 生産用の在庫圧力が下がって仕入が止まる経路)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ExecuteRoutesTheGoodsByPurpose()
    {
        var world = BuildWorld();
        world.Households[SellerId].WorkshopInventory[Item.Firewood] = 100;

        TradeSettlement.Execute(
            world, world.Households[BuyerId], world.Households[SellerId], DemandPurpose.Necessity,
            Item.Firewood, quantity: 3, unitEffectivePrice: 5, acquisitionCostSmoothingPermille: 250);

        Assert.Equal(3, world.Households[BuyerId].HouseholdInventory[Item.Firewood]);
        Assert.Equal(0, world.Households[BuyerId].WorkshopInventory[Item.Firewood]);

        TradeSettlement.Execute(
            world, world.Households[BuyerId], world.Households[SellerId], DemandPurpose.ProductionInput,
            Item.Firewood, quantity: 4, unitEffectivePrice: 5, acquisitionCostSmoothingPermille: 250);

        Assert.Equal(3, world.Households[BuyerId].HouseholdInventory[Item.Firewood]); // 変わらない
        Assert.Equal(4, world.Households[BuyerId].WorkshopInventory[Item.Firewood]);
    }

    /// <summary>
    /// 【核心】テスト表 #18。ProductionInput の約定 → PurchaseUnitCostAverage[薪] が動く。
    /// Necessity の約定では動かない。<b>本テストが見るのは ProductionInput / Necessity の対だけ</b>
    /// ── 「更新するのは ProductionInput だけ」という主張はしない(§10 で Durable も加わった)。
    /// Durable 側の検証は <see cref="DurablePurchaseUpdatesToolAcquisitionCost"/>(別表D D-1)が持つ。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-17、条件がまだ ProductionInput だけだった時点)。</b>
    /// <c>TradeSettlement.Execute</c> の末尾の <c>if (purpose == DemandPurpose.ProductionInput)</c> を
    /// 外し、常に <c>PurchaseUnitCostAverage</c> を更新する変異を当てたところ、Necessity のケースの
    /// <c>Assert.Equal(previousAverage, buyer.PurchaseUnitCostAverage[Item.Firewood])</c>
    /// (更新されないはず)が実際値(更新後の値)で失敗した(赤を確認、暖房用に高値で買った薪が
    /// 焼成の原価に乗り提示価格が跳ねる経路)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ExecuteUpdatesTheAcquisitionCostForProductionInputButNotNecessity()
    {
        var world = BuildWorld();
        world.Households[SellerId].WorkshopInventory[Item.Firewood] = 100;
        world.Households[BuyerId].PurchaseUnitCostAverage[Item.Firewood] = 20;

        TradeSettlement.Execute(
            world, world.Households[BuyerId], world.Households[SellerId], DemandPurpose.Necessity,
            Item.Firewood, quantity: 1, unitEffectivePrice: 999, acquisitionCostSmoothingPermille: 250);

        Assert.Equal(20, world.Households[BuyerId].PurchaseUnitCostAverage[Item.Firewood]);

        TradeSettlement.Execute(
            world, world.Households[BuyerId], world.Households[SellerId], DemandPurpose.ProductionInput,
            Item.Firewood, quantity: 1, unitEffectivePrice: 100, acquisitionCostSmoothingPermille: 250);

        int expected = OfferPrice.UpdatedAcquisitionCost(previousAverage: 20, unitPrice: 100, smoothingPermille: 250);
        Assert.Equal(expected, world.Households[BuyerId].PurchaseUnitCostAverage[Item.Firewood]);
        Assert.NotEqual(20, world.Households[BuyerId].PurchaseUnitCostAverage[Item.Firewood]);
    }

    /// <summary>
    /// 別表D D-1。Durableで工具を買った約定 → PurchaseUnitCostAverage[Item.Tools]が
    /// UpdatedAcquisitionCostの式で動く。Necessity/Preferenceの約定では動かない
    /// (別表Aの既存ケース<see cref="ExecuteUpdatesTheAcquisitionCostForProductionInputButNotNecessity"/>が
    /// Necessity/ProductionInputの非更新・更新を持つので、本テストはPreference/Durableを補う)。
    /// </summary>
    /// <remarks>
    /// GDD02a §5.1「更新するのは『生産の入力として買った』約定と、耐久として買った工具だけである」。
    /// 現行の<c>ProductionInput</c>だけという条件は#85途中(13e274a)から現行(50cf648)へ反転した
    /// 一世代前の規則の正確な実装であり、履歴を引かないと見えない(タスク仕様§10)。
    /// </remarks>
    [Fact]
    public void DurablePurchaseUpdatesToolAcquisitionCost()
    {
        var world = BuildWorld();
        world.Households[SellerId].WorkshopInventory[Item.Tools] = 100;
        world.Households[BuyerId].PurchaseUnitCostAverage[Item.Tools] = 20;

        // Preferenceでは動かない(用途で分岐する。品目がItem.Toolsであることだけで動いてはならない)。
        TradeSettlement.Execute(
            world, world.Households[BuyerId], world.Households[SellerId], DemandPurpose.Preference,
            Item.Tools, quantity: 1, unitEffectivePrice: 999, acquisitionCostSmoothingPermille: 250);
        Assert.Equal(20, world.Households[BuyerId].PurchaseUnitCostAverage[Item.Tools]);

        // Durableでは動く。
        TradeSettlement.Execute(
            world, world.Households[BuyerId], world.Households[SellerId], DemandPurpose.Durable,
            Item.Tools, quantity: 1, unitEffectivePrice: 100, acquisitionCostSmoothingPermille: 250);

        int expected = OfferPrice.UpdatedAcquisitionCost(previousAverage: 20, unitPrice: 100, smoothingPermille: 250);
        Assert.Equal(expected, world.Households[BuyerId].PurchaseUnitCostAverage[Item.Tools]);
        Assert.NotEqual(20, world.Households[BuyerId].PurchaseUnitCostAverage[Item.Tools]);
    }

    /// <summary>テスト表 #19。数量0 / 負、単価0 / 負で ArgumentOutOfRangeException。</summary>
    [Theory]
    [InlineData(0, 30)]
    [InlineData(-1, 30)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public void ExecuteRejectsEmptyTrades(int quantity, int unitEffectivePrice)
    {
        var world = BuildWorld();

        Assert.Throws<ArgumentOutOfRangeException>(() => TradeSettlement.Execute(
            world, world.Households[BuyerId], world.Households[SellerId], DemandPurpose.Necessity,
            ItemA, quantity, unitEffectivePrice, acquisitionCostSmoothingPermille: 250));
    }
}
