using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="MarketReference"/>(GDD02c §1.2、W2-08 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class MarketReferenceTests
{
    private const int ItemA = 0;
    private const int ItemB = 1;
    private const int SelfHouseholdId = 0;
    private const int SellerA = 1;
    private const int SellerB = 2;

    private static PriceObservation Observation(int itemId, int sellerId, int price, Tick observedAt) =>
        new()
        {
            ItemId = itemId,
            LocationId = 0,
            Price = price,
            SellerId = sellerId,
            ObservedAt = observedAt,
            Source = ObservationSource.Direct,
        };

    /// <summary>
    /// 【核心】テスト表 #1。同一売り手の3件(10/20/60)+別売り手1件(10) → 買い手の相場基準 =
    /// CeilDiv(100, 4) = 25。同じ入力で売り手側は CeilDiv(60 + 10, 2) = 35。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-19)。</b><c>MarketReference.TryBuyer</c> を売り手ごとに畳む形
    /// (<c>SortedDictionary</c> で最新1件だけを残す)に変える変異を当てたところ、
    /// <c>Assert.Equal(25, buyerReference)</c> が実際値35(35 = CeilDiv(60+10,2)、売り手側の値と
    /// 一致してしまう)で失敗した(赤を確認)。逆に <c>TrySeller</c> を全件平均に変える変異では
    /// <c>Assert.Equal(35, sellerReference)</c> が実際値25で失敗した。どちらも
    /// GDD02c §1.2 の「複利発散の歯止め」が壊れる経路であり、変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void BuyerReferenceAveragesEveryValidObservation()
    {
        var now = Tick.FromDays(10);
        var observations = new List<PriceObservation>
        {
            Observation(ItemA, SellerA, 10, Tick.FromDays(5)),
            Observation(ItemA, SellerA, 20, Tick.FromDays(7)),
            Observation(ItemA, SellerA, 60, Tick.FromDays(9)), // SellerAの最新
            Observation(ItemA, SellerB, 10, Tick.FromDays(9)),
        };

        bool foundBuyer = MarketReference.TryBuyer(
            observations, ItemA, SelfHouseholdId, now, retentionDays: 7, out int buyerReference);
        bool foundSeller = MarketReference.TrySeller(
            observations, ItemA, SelfHouseholdId, now, retentionDays: 7,
            hasOwnSettledPrice: false, ownSettledPrice: 0, out int sellerReference);

        Assert.True(foundBuyer);
        Assert.Equal(25, buyerReference); // CeilDiv(10+20+60+10, 4) = 25

        Assert.True(foundSeller);
        Assert.Equal(35, sellerReference); // CeilDiv(60+10, 2) = 35(SellerAは最新の60だけ)
    }

    /// <summary>
    /// 【核心】テスト表 #2。当日(差0)・保持期間+1日・品目違い・自分の売り注文 をそれぞれ1件ずつ
    /// 混ぜ、保持期間ちょうど(差 = retentionDays)の1件だけが残ることを売り手側と買い手側の
    /// 両方で確かめる。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-19)。</b>共通の有効性判定 <c>IsValid</c> の
    /// <c>dayDifference &lt;= retentionDays</c> を <c>dayDifference &lt; retentionDays</c>
    /// (保持期間ちょうどを除外する変異)に変えたところ、<c>TryBuyer</c> / <c>TrySeller</c> 双方の
    /// <c>Assert.True(found)</c> が実際値falseで失敗した(赤を確認)。境界の判定を1か所に
    /// 集約しているので、片方だけがずれる経路そのものは変異で作れない(1関数を直せば両方直る)。
    /// 変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ReferenceValidityIsSharedByBothSides()
    {
        var now = Tick.FromDays(10);
        const int RetentionDays = 7;

        var observations = new List<PriceObservation>
        {
            Observation(ItemA, SellerA, 999, now), // 当日(差0) ── 無効
            Observation(ItemA, SellerA, 999, Tick.FromDays(10 - RetentionDays - 1)), // 差8 ── 無効
            Observation(ItemB, SellerA, 999, Tick.FromDays(10 - RetentionDays)), // 品目違い ── 無効
            Observation(ItemA, SelfHouseholdId, 999, Tick.FromDays(10 - RetentionDays)), // 自分 ── 無効
            Observation(ItemA, SellerA, 42, Tick.FromDays(10 - RetentionDays)), // 差ちょうど7 ── 有効
        };

        bool foundBuyer = MarketReference.TryBuyer(
            observations, ItemA, SelfHouseholdId, now, RetentionDays, out int buyerReference);
        bool foundSeller = MarketReference.TrySeller(
            observations, ItemA, SelfHouseholdId, now, RetentionDays,
            hasOwnSettledPrice: false, ownSettledPrice: 0, out int sellerReference);

        Assert.True(foundBuyer);
        Assert.Equal(42, buyerReference);

        Assert.True(foundSeller);
        Assert.Equal(42, sellerReference);
    }

    /// <summary>
    /// 【核心】レビュー指摘R5。売り手側の畳み込みのタイブレーク(同一tick・同一売り手は後勝ち)。
    /// 同一売り手の観測を同一tickに2件(100→200の順で追加)置くと200になる。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-21)。</b><c>MarketReference.TrySeller</c> の
    /// <c>observation.ObservedAt &gt;= existing.ObservedAt</c> を <c>&gt;</c> に変える変異を
    /// 当てたところ、<c>Assert.Equal(200, reference)</c> が実際値100(同一tickでは更新されず、
    /// 先に追加されたほうが残る)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void SellerFoldKeepsTheLaterEntryOnATie()
    {
        var now = Tick.FromDays(10);
        var sameTick = Tick.FromDays(9);
        var observations = new List<PriceObservation>
        {
            Observation(ItemA, SellerA, 100, sameTick),
            Observation(ItemA, SellerA, 200, sameTick),
        };

        bool found = MarketReference.TrySeller(
            observations, ItemA, SelfHouseholdId, now, retentionDays: 7,
            hasOwnSettledPrice: false, ownSettledPrice: 0, out int reference);

        Assert.True(found);
        Assert.Equal(200, reference);
    }

    /// <summary>
    /// テスト表 #3。他の売り手の観測0件 + 自分の前日の約定単価あり → false(約定単価も使わない)。
    /// 他の売り手1件を足すと true になり、約定単価が平均に入る。
    /// </summary>
    [Fact]
    public void SellerReferenceNeedsAnotherSeller()
    {
        var now = Tick.FromDays(10);

        bool foundWithoutOthers = MarketReference.TrySeller(
            new List<PriceObservation>(), ItemA, SelfHouseholdId, now, retentionDays: 7,
            hasOwnSettledPrice: true, ownSettledPrice: 100, out _);

        Assert.False(foundWithoutOthers);

        var withOtherSeller = new List<PriceObservation>
        {
            Observation(ItemA, SellerA, 200, Tick.FromDays(9)),
        };

        bool foundWithOthers = MarketReference.TrySeller(
            withOtherSeller, ItemA, SelfHouseholdId, now, retentionDays: 7,
            hasOwnSettledPrice: true, ownSettledPrice: 100, out int reference);

        Assert.True(foundWithOthers);
        Assert.Equal(150, reference); // CeilDiv(200+100, 2)
    }

    private static LedgerEntry SaleEntry(int itemId, int counterpartyId, int unitPrice, int quantity, Tick occurredAt) =>
        new()
        {
            CounterpartyId = counterpartyId,
            ItemId = itemId,
            Quantity = quantity,
            UnitPrice = unitPrice,
            OccurredAt = occurredAt,
            Terms = LedgerTerms.Cash,
            Direction = LedgerDirection.Sale,
        };

    /// <summary>
    /// 【核心】テスト表 #4。前日の Sale が (単価10×2個) と (単価21×1個) → CeilDiv(41,3) = 14。
    /// Purchase の行・前々日の行・品目違いを混ぜても14。輸出(相手 = ExternalMarketSellerId)の
    /// 行は含める。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-19)。</b>数量による重み付けをやめ件数平均にする変異
    /// (<c>total += entry.UnitPrice; count++;</c> に変える)を当てたところ、
    /// <c>Assert.Equal(14, settledPrice)</c> が実際値16(CeilDiv(31,2))で失敗した(赤を確認)。
    /// また <c>entry.Direction != LedgerDirection.Sale</c> の判定を外す変異では、Purchaseの行が
    /// 混ざって合計が変わり同じアサートが失敗した。輸出の行(CounterpartyId =
    /// ExternalMarketSellerId)を除外する変異(<c>entry.CounterpartyId !=
    /// HouseholdState.ExternalMarketSellerId</c> を条件に足す)でも、輸出だけの日のテストで
    /// falseになるはずがtrueのまま残り判別できたため、変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void PreviousDaySettledPriceIsQuantityWeighted()
    {
        var now = Tick.FromDays(10);
        var yesterday = Tick.FromDays(9);
        var twoDaysAgo = Tick.FromDays(8);

        var ledger = new List<LedgerEntry>
        {
            SaleEntry(ItemA, counterpartyId: 5, unitPrice: 10, quantity: 2, occurredAt: yesterday),
            SaleEntry(ItemA, counterpartyId: 6, unitPrice: 21, quantity: 1, occurredAt: yesterday),
            new LedgerEntry
            {
                CounterpartyId = 7,
                ItemId = ItemA,
                Quantity = 100,
                UnitPrice = 999,
                OccurredAt = yesterday,
                Terms = LedgerTerms.Cash,
                Direction = LedgerDirection.Purchase, // Purchaseは数えない
            },
            SaleEntry(ItemA, counterpartyId: 8, unitPrice: 999, quantity: 100, occurredAt: twoDaysAgo), // 前々日
            SaleEntry(ItemB, counterpartyId: 9, unitPrice: 999, quantity: 100, occurredAt: yesterday), // 品目違い
        };

        bool found = MarketReference.TryPreviousDaySettledPrice(ledger, ItemA, now, out int settledPrice);

        Assert.True(found);
        Assert.Equal(14, settledPrice); // CeilDiv(10*2 + 21*1, 3) = CeilDiv(41,3) = 14

        // 輸出(窓口が相手)の行も含める。
        var withExport = new List<LedgerEntry>
        {
            SaleEntry(ItemA, counterpartyId: HouseholdState.ExternalMarketSellerId, unitPrice: 50, quantity: 1, occurredAt: yesterday),
        };

        bool foundExport = MarketReference.TryPreviousDaySettledPrice(withExport, ItemA, now, out int exportPrice);

        Assert.True(foundExport);
        Assert.Equal(50, exportPrice);
    }

    /// <summary>
    /// テスト表 #5。前日の売りが0件 → false、settledPriceは0。当日の売りだけがある場合もfalse。
    /// </summary>
    [Fact]
    public void PreviousDaySettledPriceIsAbsentWithoutYesterdaySale()
    {
        var now = Tick.FromDays(10);

        bool foundEmpty = MarketReference.TryPreviousDaySettledPrice(
            new List<LedgerEntry>(), ItemA, now, out int settledPriceEmpty);

        Assert.False(foundEmpty);
        Assert.Equal(0, settledPriceEmpty);

        var todayOnly = new List<LedgerEntry>
        {
            SaleEntry(ItemA, counterpartyId: 5, unitPrice: 10, quantity: 1, occurredAt: now),
        };

        bool foundToday = MarketReference.TryPreviousDaySettledPrice(todayOnly, ItemA, now, out int settledPriceToday);

        Assert.False(foundToday);
        Assert.Equal(0, settledPriceToday);
    }
}
