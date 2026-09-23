namespace Visionary.Sim.Metrics;

/// <summary>
/// <c>economy.csv</c> の1行(W2-20 タスク仕様「4. 出力の列」)。すべて整数。割合は千分率(‰)。
/// <b>値が定義できない欄は -1</b>(0 と区別する)。
/// </summary>
public readonly record struct EconomyRow(
    long Day,
    int Season,
    int MoneyTotal,
    int BankruptHouseholds,
    int NecessityBlockedHouseholds,
    int NecessityBlockedCount,
    int InputBlockedHouseholds,
    int ExportValue,
    int ExportQuantity,
    int ImportValue,
    int ImportQuantity,
    int SellerDays,
    int SellerDaysWithoutReference,
    int SellerDaysCoefficientCapped,
    int SellerDaysAtFloor,
    int SellerDaysAtFloorWithExport,
    int ProductionStoppedHouseholds,
    int ProductionRunsTotal,
    int DemandLines,
    int DemandLinesWithoutKnownPrice,
    int HouseholdsWithoutKnownPrice,
    int ProfitTotal);

/// <summary><c>prices.csv</c> の1行(品目1件ぶん)。全品目・全日を出す。</summary>
public readonly record struct PriceRow(
    long Day,
    int ItemId,
    int SettledMedian,
    int SettledMin,
    int SettledMax,
    int SettledCount,
    int SettledQuantity,
    int SettledValue,
    int WindowSettledCount,
    int WindowSettledQuantity,
    int WindowSettledValue,
    int OfferMin,
    int OfferMedian,
    int OfferMax,
    int OfferCount,
    int OfferAtFloorCount,
    int OfferAtFloorWithExportCount,
    int ExternalBuyPrice,
    int ExternalSellPrice);

/// <summary>
/// <c>districts.csv</c> の1行(品目 × 区画。都市生産品だけ・約定が1件以上ある行だけ)。
/// 区画は<b>買い手</b>の区画(GDD02 §8-4)。
/// </summary>
public readonly record struct DistrictRow(
    long Day,
    int ItemId,
    int DistrictId,
    int SettledMedian,
    int SettledCount,
    int SettledQuantity,
    int SettledValue,
    int WindowSettledCount);

/// <summary><c>households.csv</c> の1行(世帯1戸ぶん)。全日・全世帯を出す。</summary>
public readonly record struct HouseholdRow(
    long Day,
    int HouseholdId,
    int DistrictId,
    int Occupation,
    int OutputItemId,
    int LiquidFunds,
    int ProductionRuns,
    int SalesValue,
    int RunCost,
    int Profit,
    int IsBankrupt,
    int NecessityBlockedCount,
    int InputBlocked,
    int DemandLines,
    int DemandLinesWithoutKnownPrice,
    int SellableStockOutput,
    int OfferPrice);

/// <summary><c>trades.csv</c> の1行(日次1行)。</summary>
public readonly record struct TradesRow(
    long Day,
    int SettlementCount,
    int InternalSettlementCount,
    int WindowSettlementCount,
    int InternalSettlementValue,
    int PartnerSwitchPermille,
    int HhiPermilleSquared,
    int ActiveSellerCount,
    int ActiveBuyerCount);

/// <summary>
/// 1日ぶんの日次メトリクス(W2-20 タスク仕様「3. 順10 MetricsSystem」)。
/// <b>書き終えたら捨てる</b> — 走行全体を溜めない(36,000日 × 5ファイルぶんを持つと数百MBになる)。
/// </summary>
public readonly struct DailySnapshot
{
    public DailySnapshot(
        EconomyRow economy,
        IReadOnlyList<PriceRow> prices,
        IReadOnlyList<DistrictRow> districts,
        IReadOnlyList<HouseholdRow> households,
        TradesRow trades)
    {
        ArgumentNullException.ThrowIfNull(prices);
        ArgumentNullException.ThrowIfNull(districts);
        ArgumentNullException.ThrowIfNull(households);

        Economy = economy;
        Prices = prices;
        Districts = districts;
        Households = households;
        Trades = trades;
    }

    public EconomyRow Economy { get; }

    /// <summary>品目Id昇順・全品目ぶん。</summary>
    public IReadOnlyList<PriceRow> Prices { get; }

    /// <summary>約定が1件以上ある (品目, 区画) だけ。順序は <see cref="Systems.MetricsSystem"/> が決める。</summary>
    public IReadOnlyList<DistrictRow> Districts { get; }

    /// <summary>世帯Id昇順・全世帯ぶん。</summary>
    public IReadOnlyList<HouseholdRow> Households { get; }

    public TradesRow Trades { get; }
}

/// <summary>
/// 1日ぶんの <see cref="DailySnapshot"/> の書き出し先。<see cref="Systems.MetricsSystem"/> が
/// 呼ぶ。実装(CSV書き出し)は <c>Visionary.Sim.Runner</c> が持つ — <c>Visionary.Sim</c> は
/// ファイルI/Oを持たない。
/// </summary>
public interface IDailyMetricsSink
{
    void Write(in DailySnapshot snapshot);
}
