using System.Globalization;
using System.Text;
using Visionary.Sim.Metrics;

namespace Visionary.Sim.Runner;

/// <summary>
/// <see cref="IDailyMetricsSink"/> の実装(W2-20 タスク仕様「4. 出力の列」)。5つの CSV
/// (<c>economy</c> / <c>prices</c> / <c>districts</c> / <c>households</c> / <c>trades</c>)を
/// 1日ぶんずつ書き足す ── 走行全体をメモリに溜めない(36,000日 × 5ファイルぶんは数百MBになる)。
/// </summary>
/// <remarks>
/// <para>
/// <b>区切りは <c>,</c>、改行は <c>\n</c> 固定。</b>プラットフォーム既定の改行にしない ──
/// CI が Ubuntu、開発機が Windows で、バイト一致の検査(テスト表 #21)が壊れる。
/// </para>
/// <para>
/// <b>数値は <see cref="CultureInfo.InvariantCulture"/> で整形する。</b>
/// <see cref="Visionary.Sim"/> 側で計算済みの整数を並べるだけで、比率の計算はここでは一切しない
/// (<c>Visionary.Sim.Runner</c> 側で <c>double</c> を使うと ADR-0002 の規約が静かに破れる)。
/// </para>
/// </remarks>
internal sealed class CsvMetricsSink : IDailyMetricsSink, IDisposable
{
    // BOM無しUTF-8。BOMがあっても2回の実行間では同一だが、余計なバイトを持たせない。
    private static readonly UTF8Encoding NoBomUtf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly StreamWriter _economy;
    private readonly StreamWriter _prices;
    private readonly StreamWriter _districts;
    private readonly StreamWriter _households;
    private readonly StreamWriter _trades;

    public CsvMetricsSink(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        _economy = CreateWriter(outputDirectory, "economy.csv");
        _prices = CreateWriter(outputDirectory, "prices.csv");
        _districts = CreateWriter(outputDirectory, "districts.csv");
        _households = CreateWriter(outputDirectory, "households.csv");
        _trades = CreateWriter(outputDirectory, "trades.csv");

        _economy.WriteLine(
            "day,season,money_total,bankrupt_households,necessity_blocked_households,"
                + "necessity_blocked_count,input_blocked_households,export_value,export_quantity,"
                + "import_value,import_quantity,seller_days,seller_days_without_reference,"
                + "seller_days_coefficient_capped,seller_days_at_floor,seller_days_at_floor_with_export,"
                + "production_stopped_households,production_runs_total,demand_lines,"
                + "demand_lines_without_known_price,households_without_known_price,profit_total");

        _prices.WriteLine(
            "day,item_id,settled_median,settled_min,settled_max,settled_count,settled_quantity,"
                + "settled_value,window_settled_count,window_settled_quantity,window_settled_value,"
                + "offer_min,offer_median,offer_max,offer_count,offer_at_floor_count,"
                + "offer_at_floor_with_export_count,external_buy_price,external_sell_price");

        _districts.WriteLine(
            "day,item_id,district_id,settled_median,settled_count,settled_quantity,settled_value,"
                + "window_settled_count");

        _households.WriteLine(
            "day,household_id,district_id,occupation,output_item_id,liquid_funds,production_runs,"
                + "sales_value,run_cost,profit,is_bankrupt,necessity_blocked_count,input_blocked,"
                + "demand_lines,demand_lines_without_known_price,sellable_stock_output,offer_price");

        _trades.WriteLine(
            "day,settlement_count,internal_settlement_count,window_settlement_count,"
                + "internal_settlement_value,partner_switch_permille,hhi_permille_squared,"
                + "active_seller_count,active_buyer_count");
    }

    public void Write(in DailySnapshot snapshot)
    {
        var economy = snapshot.Economy;

        _economy.WriteLine(string.Join(',', new[]
        {
            Format(economy.Day), Format(economy.Season), Format(economy.MoneyTotal),
            Format(economy.BankruptHouseholds), Format(economy.NecessityBlockedHouseholds),
            Format(economy.NecessityBlockedCount), Format(economy.InputBlockedHouseholds),
            Format(economy.ExportValue), Format(economy.ExportQuantity), Format(economy.ImportValue),
            Format(economy.ImportQuantity), Format(economy.SellerDays),
            Format(economy.SellerDaysWithoutReference), Format(economy.SellerDaysCoefficientCapped),
            Format(economy.SellerDaysAtFloor), Format(economy.SellerDaysAtFloorWithExport),
            Format(economy.ProductionStoppedHouseholds), Format(economy.ProductionRunsTotal),
            Format(economy.DemandLines), Format(economy.DemandLinesWithoutKnownPrice),
            Format(economy.HouseholdsWithoutKnownPrice), Format(economy.ProfitTotal),
        }));

        foreach (var price in snapshot.Prices)
        {
            _prices.WriteLine(string.Join(',', new[]
            {
                Format(price.Day), Format(price.ItemId), Format(price.SettledMedian),
                Format(price.SettledMin), Format(price.SettledMax), Format(price.SettledCount),
                Format(price.SettledQuantity), Format(price.SettledValue),
                Format(price.WindowSettledCount), Format(price.WindowSettledQuantity),
                Format(price.WindowSettledValue), Format(price.OfferMin), Format(price.OfferMedian),
                Format(price.OfferMax), Format(price.OfferCount), Format(price.OfferAtFloorCount),
                Format(price.OfferAtFloorWithExportCount), Format(price.ExternalBuyPrice),
                Format(price.ExternalSellPrice),
            }));
        }

        foreach (var district in snapshot.Districts)
        {
            _districts.WriteLine(string.Join(',', new[]
            {
                Format(district.Day), Format(district.ItemId), Format(district.DistrictId),
                Format(district.SettledMedian), Format(district.SettledCount),
                Format(district.SettledQuantity), Format(district.SettledValue),
                Format(district.WindowSettledCount),
            }));
        }

        foreach (var household in snapshot.Households)
        {
            _households.WriteLine(string.Join(',', new[]
            {
                Format(household.Day), Format(household.HouseholdId), Format(household.DistrictId),
                Format(household.Occupation), Format(household.OutputItemId),
                Format(household.LiquidFunds), Format(household.ProductionRuns),
                Format(household.SalesValue), Format(household.RunCost), Format(household.Profit),
                Format(household.IsBankrupt), Format(household.NecessityBlockedCount),
                Format(household.InputBlocked), Format(household.DemandLines),
                Format(household.DemandLinesWithoutKnownPrice), Format(household.SellableStockOutput),
                Format(household.OfferPrice),
            }));
        }

        var trades = snapshot.Trades;

        _trades.WriteLine(string.Join(',', new[]
        {
            Format(trades.Day), Format(trades.SettlementCount), Format(trades.InternalSettlementCount),
            Format(trades.WindowSettlementCount), Format(trades.InternalSettlementValue),
            Format(trades.PartnerSwitchPermille), Format(trades.HhiPermilleSquared),
            Format(trades.ActiveSellerCount), Format(trades.ActiveBuyerCount),
        }));
    }

    public void Dispose()
    {
        _economy.Dispose();
        _prices.Dispose();
        _districts.Dispose();
        _households.Dispose();
        _trades.Dispose();
    }

    private static StreamWriter CreateWriter(string directory, string fileName) =>
        new(Path.Combine(directory, fileName), append: false, NoBomUtf8) { NewLine = "\n" };

    private static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
