using System.Globalization;
using Visionary.Sim.Metrics;
using Visionary.Sim.Runner;

namespace Visionary.Sim.Tests.Runner;

/// <summary>
/// <see cref="CsvMetricsSink"/>(W2-20 タスク仕様の別表 #30)の検査。<c>InternalsVisibleTo</c>
/// (<c>src/Visionary.Sim.Runner/AssemblyInfo.cs</c>)経由で内部クラスへ直接アクセスする。
/// </summary>
/// <remarks>
/// <b>ヘッダは手書きの文字列、値は手書きの配列で、両者の対応を見る機械が無かった(3巡目 I-a)。</b>
/// <c>Format(...)</c> の並びを2つ入れ替える / 1つ落とす変異が、テストもビルドも
/// <c>dotnet format</c> も緑で通っていた。ここでは1日ぶんの既知の <see cref="DailySnapshot"/> を
/// 書き出し、<b>ヘッダ行を <c>,</c> で割った列名 → 値の対応を名前で引いて</b>検査する。
/// <b>各列に相異なる値を入れる</b>(同じ値だと入れ替えを検出できない)。
/// </remarks>
public sealed class CsvMetricsSinkTests : IDisposable
{
    private readonly string _outputDirectory;

    public CsvMetricsSinkTests()
    {
        _outputDirectory = Path.Combine(Path.GetTempPath(), "vsim-csv-tests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
        catch (IOException)
        {
            // 後始末の失敗はテスト結果に影響させない(OSの一時ファイル掃除に任せる)。
        }
    }

    [Fact]
    public void CsvHeadersMatchTheValues()
    {
        // economy.csv(22列)。necessity_blocked_households/necessity_blocked_count、
        // export_value/export_quantity、demand_lines/demand_lines_without_known_price は
        // 「同じ次元で入れ替わっても不自然に見えない組」(タスク仕様)として必ず相異なる値にする。
        var economy = new EconomyRow(
            Day: 1000, Season: 1001, MoneyTotal: 1002, BankruptHouseholds: 1003,
            NecessityBlockedHouseholds: 1004, NecessityBlockedCount: 1005, InputBlockedHouseholds: 1006,
            ExportValue: 1007, ExportQuantity: 1008, ImportValue: 1009, ImportQuantity: 1010,
            SellerDays: 1011, SellerDaysWithoutReference: 1012, SellerDaysCoefficientCapped: 1013,
            SellerDaysAtFloor: 1014, SellerDaysAtFloorWithExport: 1015, ProductionStoppedHouseholds: 1016,
            ProductionRunsTotal: 1017, DemandLines: 1018, DemandLinesWithoutKnownPrice: 1019,
            HouseholdsWithoutKnownPrice: 1020, ProfitTotal: 1021);

        // prices.csv(19列)。settled_min/settled_max(最小最大の入れ替え)を必ず相異なる値にする。
        var price = new PriceRow(
            Day: 1000, ItemId: 2000, SettledMedian: 2001, SettledMin: 2002, SettledMax: 2003,
            SettledCount: 2004, SettledQuantity: 2005, SettledValue: 2006, WindowSettledCount: 2007,
            WindowSettledQuantity: 2008, WindowSettledValue: 2009, OfferMin: 2010, OfferMedian: 2011,
            OfferMax: 2012, OfferCount: 2013, OfferAtFloorCount: 2014, OfferAtFloorWithExportCount: 2015,
            ExternalBuyPrice: 2016, ExternalSellPrice: 2017);

        // districts.csv(8列)。
        var district = new DistrictRow(
            Day: 1000, ItemId: 3000, DistrictId: 3001, SettledMedian: 3002, SettledCount: 3003,
            SettledQuantity: 3004, SettledValue: 3005, WindowSettledCount: 3006);

        // households.csv(17列)。
        var household = new HouseholdRow(
            Day: 1000, HouseholdId: 4000, DistrictId: 4001, Occupation: 4002, OutputItemId: 4003,
            LiquidFunds: 4004, ProductionRuns: 4005, SalesValue: 4006, RunCost: 4007, Profit: 4008,
            IsBankrupt: 4009, NecessityBlockedCount: 4010, InputBlocked: 4011, DemandLines: 4012,
            DemandLinesWithoutKnownPrice: 4013, SellableStockOutput: 4014, OfferPrice: 4015);

        // trades.csv(9列)。internal_settlement_count/window_settlement_count(内数と外数)、
        // active_seller_count/active_buyer_count(向き)を必ず相異なる値にする。
        var trades = new TradesRow(
            Day: 1000, SettlementCount: 5000, InternalSettlementCount: 5001, WindowSettlementCount: 5002,
            InternalSettlementValue: 5003, PartnerSwitchPermille: 5004, HhiPermilleSquared: 5005,
            ActiveSellerCount: 5006, ActiveBuyerCount: 5007);

        var snapshot = new DailySnapshot(
            economy, new[] { price }, new[] { district }, new[] { household }, trades);

        using (var sink = new CsvMetricsSink(_outputDirectory))
        {
            sink.Write(in snapshot);
        }

        AssertRowMatchesHeaderByName(
            "economy.csv",
            new Dictionary<string, long>
            {
                ["day"] = 1000,
                ["season"] = 1001,
                ["money_total"] = 1002,
                ["bankrupt_households"] = 1003,
                ["necessity_blocked_households"] = 1004,
                ["necessity_blocked_count"] = 1005,
                ["input_blocked_households"] = 1006,
                ["export_value"] = 1007,
                ["export_quantity"] = 1008,
                ["import_value"] = 1009,
                ["import_quantity"] = 1010,
                ["seller_days"] = 1011,
                ["seller_days_without_reference"] = 1012,
                ["seller_days_coefficient_capped"] = 1013,
                ["seller_days_at_floor"] = 1014,
                ["seller_days_at_floor_with_export"] = 1015,
                ["production_stopped_households"] = 1016,
                ["production_runs_total"] = 1017,
                ["demand_lines"] = 1018,
                ["demand_lines_without_known_price"] = 1019,
                ["households_without_known_price"] = 1020,
                ["profit_total"] = 1021,
            });

        AssertRowMatchesHeaderByName(
            "prices.csv",
            new Dictionary<string, long>
            {
                ["day"] = 1000,
                ["item_id"] = 2000,
                ["settled_median"] = 2001,
                ["settled_min"] = 2002,
                ["settled_max"] = 2003,
                ["settled_count"] = 2004,
                ["settled_quantity"] = 2005,
                ["settled_value"] = 2006,
                ["window_settled_count"] = 2007,
                ["window_settled_quantity"] = 2008,
                ["window_settled_value"] = 2009,
                ["offer_min"] = 2010,
                ["offer_median"] = 2011,
                ["offer_max"] = 2012,
                ["offer_count"] = 2013,
                ["offer_at_floor_count"] = 2014,
                ["offer_at_floor_with_export_count"] = 2015,
                ["external_buy_price"] = 2016,
                ["external_sell_price"] = 2017,
            });

        AssertRowMatchesHeaderByName(
            "districts.csv",
            new Dictionary<string, long>
            {
                ["day"] = 1000,
                ["item_id"] = 3000,
                ["district_id"] = 3001,
                ["settled_median"] = 3002,
                ["settled_count"] = 3003,
                ["settled_quantity"] = 3004,
                ["settled_value"] = 3005,
                ["window_settled_count"] = 3006,
            });

        AssertRowMatchesHeaderByName(
            "households.csv",
            new Dictionary<string, long>
            {
                ["day"] = 1000,
                ["household_id"] = 4000,
                ["district_id"] = 4001,
                ["occupation"] = 4002,
                ["output_item_id"] = 4003,
                ["liquid_funds"] = 4004,
                ["production_runs"] = 4005,
                ["sales_value"] = 4006,
                ["run_cost"] = 4007,
                ["profit"] = 4008,
                ["is_bankrupt"] = 4009,
                ["necessity_blocked_count"] = 4010,
                ["input_blocked"] = 4011,
                ["demand_lines"] = 4012,
                ["demand_lines_without_known_price"] = 4013,
                ["sellable_stock_output"] = 4014,
                ["offer_price"] = 4015,
            });

        AssertRowMatchesHeaderByName(
            "trades.csv",
            new Dictionary<string, long>
            {
                ["day"] = 1000,
                ["settlement_count"] = 5000,
                ["internal_settlement_count"] = 5001,
                ["window_settlement_count"] = 5002,
                ["internal_settlement_value"] = 5003,
                ["partner_switch_permille"] = 5004,
                ["hhi_permille_squared"] = 5005,
                ["active_seller_count"] = 5006,
                ["active_buyer_count"] = 5007,
            });
    }

    /// <summary>
    /// ヘッダ行を <c>,</c> で割った列名でデータ行の値を引き、<paramref name="expected"/> の
    /// 全列と一致することを見る。あわせて、ヘッダのフィールド数とデータ行のフィールド数が
    /// 一致することを見る(列を1つ落とす変異を捕まえる)。
    /// </summary>
    private void AssertRowMatchesHeaderByName(string fileName, IReadOnlyDictionary<string, long> expected)
    {
        string[] lines = File.ReadAllLines(Path.Combine(_outputDirectory, fileName));
        string[] headers = lines[0].Split(',');
        string[] values = lines[1].Split(',');

        Assert.True(
            headers.Length == values.Length,
            $"{fileName}: ヘッダのフィールド数({headers.Length})とデータ行のフィールド数"
                + $"({values.Length})が一致しない。");

        var actual = new Dictionary<string, long>(headers.Length);

        for (int i = 0; i < headers.Length; i++)
        {
            actual[headers[i]] = long.Parse(values[i], CultureInfo.InvariantCulture);
        }

        Assert.Equal(expected.Count, actual.Count);

        foreach (var (columnName, expectedValue) in expected)
        {
            Assert.True(actual.TryGetValue(columnName, out long actualValue), $"{fileName} にヘッダ列 '{columnName}' が無い。");
            Assert.Equal(expectedValue, actualValue);
        }
    }
}
