using Visionary.Sim.Metrics;
using Visionary.Sim.Numerics;
using Visionary.Sim.Time;
using Visionary.Sim.Verification;

namespace Visionary.Sim.Tests.Verification;

/// <summary>
/// <see cref="VerificationAccumulator"/> のテスト用の <see cref="DailySnapshot"/> ビルダ
/// (W2-21 タスク仕様「落ちるべき条件」冒頭)。
/// </summary>
/// <remarks>
/// <para>
/// <b>既定値は「すべて緑になる健全な1日」。</b>各テストは変えたい列だけを上書きする ──
/// シムを回して作らない(判定の欠陥と経済の欠陥が同じテストの中で混ざるため)。
/// </para>
/// <para>
/// <b>日ごとの変動を持たせる理由。</b>硬直(8-1b)の赤条件は「レンジが狭い」ことなので、
/// 同じ値を毎日繰り返す既定値は逆に赤を招く。品目の床(<see cref="WorldDefinition.ExternalBuyPrice"/>)
/// に比例した周期的な変動を持たせ、レンジ・区画差・分散のどれも安全側(緑)に収まる幅を確保する。
/// </para>
/// </remarks>
internal static class DailySnapshotTestBuilder
{
    public static WorldDefinition Definition { get; } = WorldDefinition.M0;

    /// <summary>「すべて緑になる健全な1日」の <see cref="DailySnapshot"/>。</summary>
    public static DailySnapshot Healthy(long day)
    {
        var economy = HealthyEconomy(day);
        var prices = HealthyPrices(day);
        var districts = HealthyDistricts(day);
        var households = HealthyHouseholds(day);
        var trades = HealthyTrades(day);

        return new DailySnapshot(economy, prices, districts, households, trades);
    }

    /// <summary><paramref name="totalDays"/> 日ぶんの健全な日を生成する(day 0 始まり)。</summary>
    public static IReadOnlyList<DailySnapshot> Sequence(int totalDays)
    {
        var list = new List<DailySnapshot>(totalDays);

        for (long day = 0; day < totalDays; day++)
        {
            list.Add(Healthy(day));
        }

        return list;
    }

    /// <summary>初期貨幣総量(= 初期資金 × 世帯数)。8-6のテストが基準として使う。</summary>
    public static int InitialTotalMoney => Definition.InitialLiquidFunds * Definition.HouseholdCount;

    /// <summary>床(外部買値)。都市生産品だけ。</summary>
    public static int Floor(int itemId) => Definition.ExternalBuyPrice(itemId);

    /// <summary>天井(外部売値)。季節に依らない(都市生産品)。</summary>
    public static int Ceiling(int itemId) => Definition.ExternalSellPrice(itemId, Season.Spring);

    /// <summary><paramref name="snapshot"/> の <see cref="DailySnapshot.Economy"/> だけを差し替える。</summary>
    public static DailySnapshot WithEconomy(DailySnapshot snapshot, EconomyRow economy) =>
        new(economy, snapshot.Prices, snapshot.Districts, snapshot.Households, snapshot.Trades);

    /// <summary><paramref name="snapshot"/> の <see cref="DailySnapshot.Trades"/> だけを差し替える。</summary>
    public static DailySnapshot WithTrades(DailySnapshot snapshot, TradesRow trades) =>
        new(snapshot.Economy, snapshot.Prices, snapshot.Districts, snapshot.Households, trades);

    /// <summary><paramref name="snapshot"/> の <see cref="DailySnapshot.Districts"/> だけを差し替える。</summary>
    public static DailySnapshot WithDistricts(DailySnapshot snapshot, IReadOnlyList<DistrictRow> districts) =>
        new(snapshot.Economy, snapshot.Prices, districts, snapshot.Households, snapshot.Trades);

    /// <summary><paramref name="snapshot"/> の <see cref="DailySnapshot.Households"/> だけを差し替える。</summary>
    public static DailySnapshot WithHouseholds(DailySnapshot snapshot, IReadOnlyList<HouseholdRow> households) =>
        new(snapshot.Economy, snapshot.Prices, snapshot.Districts, households, snapshot.Trades);

    /// <summary>
    /// <paramref name="snapshot"/> の <see cref="DailySnapshot.Prices"/> のうち、<paramref name="itemId"/>
    /// の1行だけを差し替える(<see cref="PriceRow"/> の添字は itemId と一致する。<see cref="HealthyPrices"/>
    /// が itemId 昇順で埋めているため)。
    /// </summary>
    public static DailySnapshot WithPrice(DailySnapshot snapshot, int itemId, PriceRow row)
    {
        var prices = snapshot.Prices.ToList();
        prices[itemId] = row;

        return new DailySnapshot(snapshot.Economy, prices, snapshot.Districts, snapshot.Households, snapshot.Trades);
    }

    private static EconomyRow HealthyEconomy(long day) => new(
        Day: day,
        Season: (int)Season.Spring,
        MoneyTotal: InitialTotalMoney,
        BankruptHouseholds: 0,
        NecessityBlockedHouseholds: 0,
        NecessityBlockedCount: 0,
        InputBlockedHouseholds: 0,
        ExportValue: 10,
        ExportQuantity: 1,
        ImportValue: 0,
        ImportQuantity: 0,
        SellerDays: 10,
        SellerDaysWithoutReference: 0,
        SellerDaysCoefficientCapped: 0,
        SellerDaysAtFloor: 0,
        SellerDaysAtFloorWithExport: 0,
        ProductionStoppedHouseholds: 0,
        ProductionRunsTotal: 50,
        DemandLines: 100,
        DemandLinesWithoutKnownPrice: 0,
        HouseholdsWithoutKnownPrice: 0,
        ProfitTotal: 0);

    private static IReadOnlyList<PriceRow> HealthyPrices(long day)
    {
        var list = new List<PriceRow>(Definition.ItemCount);

        for (int item = 0; item < Definition.ItemCount; item++)
        {
            if (Definition.IsPrimaryItem(item))
            {
                list.Add(new PriceRow(
                    Day: day, ItemId: item, SettledMedian: -1, SettledMin: -1, SettledMax: -1,
                    SettledCount: 0, SettledQuantity: 0, SettledValue: 0,
                    WindowSettledCount: 0, WindowSettledQuantity: 0, WindowSettledValue: 0,
                    OfferMin: -1, OfferMedian: -1, OfferMax: -1, OfferCount: 0,
                    OfferAtFloorCount: 0, OfferAtFloorWithExportCount: 0,
                    ExternalBuyPrice: -1, ExternalSellPrice: Definition.ExternalSellPrice(item, Season.Spring)));
                continue;
            }

            int median = MedianFor(item, day);
            int floor = Floor(item);
            int ceiling = Ceiling(item);

            list.Add(new PriceRow(
                Day: day,
                ItemId: item,
                SettledMedian: median,
                SettledMin: median,
                SettledMax: median,
                SettledCount: 4,
                SettledQuantity: 4,
                SettledValue: 4 * median,
                WindowSettledCount: 1,
                WindowSettledQuantity: 1,
                WindowSettledValue: median,
                OfferMin: floor + 1,
                OfferMedian: median,
                OfferMax: ceiling + 1,
                OfferCount: 2,
                OfferAtFloorCount: 0,
                OfferAtFloorWithExportCount: 0,
                ExternalBuyPrice: floor,
                ExternalSellPrice: ceiling));
        }

        return list;
    }

    private static IReadOnlyList<DistrictRow> HealthyDistricts(long day)
    {
        var list = new List<DistrictRow>();

        for (int item = 0; item < Definition.ItemCount; item++)
        {
            if (Definition.IsPrimaryItem(item) || item == Item.Tools)
            {
                continue;
            }

            int floor = Floor(item);
            int spread = checked((int)RequiredDistrictSpread(item)) + 5;

            list.Add(new DistrictRow(
                Day: day, ItemId: item, DistrictId: 0, SettledMedian: floor * 2,
                SettledCount: 2, SettledQuantity: 2, SettledValue: 2 * floor * 2, WindowSettledCount: 0));
            list.Add(new DistrictRow(
                Day: day, ItemId: item, DistrictId: 1, SettledMedian: (floor * 2) + spread,
                SettledCount: 2, SettledQuantity: 2, SettledValue: 2 * ((floor * 2) + spread), WindowSettledCount: 0));
        }

        return list;
    }

    private static IReadOnlyList<HouseholdRow> HealthyHouseholds(long day)
    {
        var list = new List<HouseholdRow>();
        int householdId = 0;

        for (int occupation = 0; occupation < Definition.OccupationCount; occupation++)
        {
            var recipe = Definition.Recipes[occupation];
            int outputItemId = recipe.Outputs[0].ItemId;

            for (int i = 0; i < Definition.HouseholdsPerOccupation; i++)
            {
                list.Add(new HouseholdRow(
                    Day: day,
                    HouseholdId: householdId,
                    DistrictId: householdId % District.Count,
                    Occupation: occupation,
                    OutputItemId: outputItemId,
                    LiquidFunds: Definition.InitialLiquidFunds,
                    ProductionRuns: 5,
                    SalesValue: 100,
                    RunCost: 50,
                    Profit: 50,
                    IsBankrupt: 0,
                    NecessityBlockedCount: 0,
                    InputBlocked: 0,
                    DemandLines: 5,
                    DemandLinesWithoutKnownPrice: 0,
                    SellableStockOutput: 10,
                    OfferPrice: MedianFor(outputItemId, day)));

                householdId++;
            }
        }

        return list;
    }

    private static TradesRow HealthyTrades(long day) => new(
        Day: day,
        SettlementCount: 10,
        InternalSettlementCount: 9,
        WindowSettlementCount: 1,
        InternalSettlementValue: 500,
        PartnerSwitchPermille: 500,
        HhiPermilleSquared: 200,
        ActiveSellerCount: 5,
        ActiveBuyerCount: 10);

    /// <summary>
    /// 品目の周期的な変動幅(硬直の閾値より広く取り、既定値が誤って赤にならないようにする)。
    /// </summary>
    private static int PeriodLengthFor(int itemId)
    {
        int floor = Floor(itemId);
        long requiredRange = IntegerMath.CeilDiv((long)floor * VerificationThresholds.RigidityRangePermille, 1000);

        return checked((int)requiredRange) + 6;
    }

    private static long RequiredDistrictSpread(int itemId)
    {
        int floor = Floor(itemId);

        return IntegerMath.CeilDiv((long)floor * VerificationThresholds.DistrictSpreadPermille, 1000);
    }

    private static int MedianFor(int itemId, long day)
    {
        if (Definition.IsPrimaryItem(itemId))
        {
            return -1;
        }

        int floor = Floor(itemId);
        int period = PeriodLengthFor(itemId);

        return (floor * 2) + checked((int)(day % period));
    }
}
