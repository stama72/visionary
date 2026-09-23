using Visionary.Sim.Metrics;
using Visionary.Sim.Numerics;
using Visionary.Sim.Randomness;
using Visionary.Sim.Time;

namespace Visionary.Sim.Systems;

/// <summary>
/// 順10 Metrics(TDD01 §3.3 / §4.2)。日次メトリクスのスナップショットを
/// <see cref="IDailyMetricsSink"/> へ渡す。<b>シムの意思決定には一切関与しない。</b>
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Step"/> は <see cref="World"/> を一切書き換えない。</b>読むのは
/// <see cref="World.Households"/> / <see cref="World.Market"/> / <see cref="World.Ledgers"/> /
/// <see cref="World.Metrics"/> / <see cref="World.Now"/> だけである(W2-20 タスク仕様)。
/// </para>
/// <para>
/// <b>帳簿は末尾から読み、当日の行を過ぎたら打ち切る。</b>先頭から走査すると 36,000日 で
/// 日数の2乗になる(<see cref="MarketReference.TryPreviousDaySettledPrice"/> と同じ理由)。
/// </para>
/// </remarks>
public sealed class MetricsSystem : ISimSystem
{
    private readonly WorldDefinition _definition;
    private readonly IDailyMetricsSink _sink;

    public MetricsSystem(WorldDefinition definition, IDailyMetricsSink sink)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(sink);

        _definition = definition;
        _sink = sink;
    }

    /// <summary>13。乱数は引かないが、登録に一意な系統が要る(12は OpportunityCost の予約)。</summary>
    public RandomStream Stream => RandomStream.Metrics;

    public Cadence Cadence => Cadence.Daily(hour: 0);

    public void Step(World world, SimContext context)
    {
        ArgumentNullException.ThrowIfNull(world);

        int householdCount = world.Households.Length;
        int itemCount = _definition.ItemCount;
        long today = world.Now.DayIndex;
        var season = GameDate.FromTick(world.Now).Season;

        // 品目別の約定・提示価格の集計(prices.csv)。List は品目あたりの件数(高々世帯数程度)
        // ぶんだけ遅延確保する。
        var settledCount = new int[itemCount];
        var settledQuantity = new long[itemCount];
        var settledValue = new long[itemCount];
        var settledPrices = new List<int>?[itemCount];
        var windowSettledCount = new int[itemCount];
        var windowSettledQuantity = new long[itemCount];
        var windowSettledValue = new long[itemCount];
        var offerCount = new int[itemCount];
        var offerPrices = new List<int>?[itemCount];
        var offerAtFloorCount = new int[itemCount];
        var offerAtFloorWithExportCount = new int[itemCount];

        // 品目 × 区画の集計(districts.csv)。添字 = itemId * District.Count + districtId。
        int districtCellCount = itemCount * District.Count;
        var districtCount = new int[districtCellCount];
        var districtQuantity = new long[districtCellCount];
        var districtValue = new long[districtCellCount];
        var districtWindowCount = new int[districtCellCount];
        var districtPrices = new List<int>?[districtCellCount];

        // 世帯別の当日集計。
        var salesValueAll = new long[householdCount]; // households.csv sales_value(輸出を含む)
        var sellerInternalSaleValue = new long[householdCount]; // HHI の分子(都市内のSaleだけ)
        var exportedToday = new bool[householdCount]; // seller_days_at_floor_with_export が読む
        var activeSeller = new bool[householdCount];
        var activeBuyer = new bool[householdCount];

        int settlementCountTotal = 0;
        int internalSettlementCountTotal = 0;
        int windowSettlementCountTotal = 0;
        long internalSettlementValueTotal = 0;
        long exportValueTotal = 0;
        long exportQuantityTotal = 0;
        long importValueTotal = 0;
        long importQuantityTotal = 0;

        for (int householdId = 0; householdId < householdCount; householdId++)
        {
            var household = world.Households[householdId];
            var ledger = world.Ledgers[householdId];

            long householdSalesValueAll = 0;
            long householdSalesValueInternal = 0;

            // 末尾から走査し、当日より前に達したら打ち切る(帳簿は追記専用でOccurredAtが
            // 非減少。W2-20 タスク仕様「性能」)。
            for (int i = ledger.Count - 1; i >= 0; i--)
            {
                var entry = ledger[i];
                long entryDay = entry.OccurredAt.DayIndex;

                if (entryDay < today)
                {
                    break;
                }

                if (entryDay != today)
                {
                    // 当日より後(通常は起きない。コマンドの割り込みに備えた安全側)。
                    continue;
                }

                bool isWindowCounterparty = entry.CounterpartyId == HouseholdState.ExternalMarketSellerId;
                long amount = (long)entry.UnitPrice * entry.Quantity;

                if (entry.Direction == LedgerDirection.Purchase)
                {
                    // 約定の母数はPurchaseの行だけ(都市内の約定は買い手と売り手が1行ずつ
                    // 記帳するので、両方数えると2倍になる。窓口からの輸入もここに入る)。
                    settlementCountTotal++;
                    activeBuyer[householdId] = true;

                    settledCount[entry.ItemId]++;
                    settledQuantity[entry.ItemId] += entry.Quantity;
                    settledValue[entry.ItemId] += amount;
                    (settledPrices[entry.ItemId] ??= new List<int>()).Add(entry.UnitPrice);

                    if (isWindowCounterparty)
                    {
                        windowSettlementCountTotal++;
                        windowSettledCount[entry.ItemId]++;
                        windowSettledQuantity[entry.ItemId] += entry.Quantity;
                        windowSettledValue[entry.ItemId] += amount;

                        importValueTotal += amount;
                        importQuantityTotal += entry.Quantity;
                    }
                    else
                    {
                        internalSettlementCountTotal++;
                        internalSettlementValueTotal += amount;
                    }

                    // districts.csv は都市生産品だけ・買い手の区画(household.DistrictId)。
                    if (!_definition.IsPrimaryItem(entry.ItemId))
                    {
                        int cell = (entry.ItemId * District.Count) + household.DistrictId;

                        districtCount[cell]++;
                        districtQuantity[cell] += entry.Quantity;
                        districtValue[cell] += amount;
                        (districtPrices[cell] ??= new List<int>()).Add(entry.UnitPrice);

                        if (isWindowCounterparty)
                        {
                            districtWindowCount[cell]++;
                        }
                    }
                }
                else
                {
                    // Sale。輸出(相手=予約Id)も含む(households.csv sales_value)。
                    householdSalesValueAll += amount;
                    activeSeller[householdId] = true;

                    if (isWindowCounterparty)
                    {
                        exportedToday[householdId] = true;
                        exportValueTotal += amount;
                        exportQuantityTotal += entry.Quantity;
                    }
                    else
                    {
                        householdSalesValueInternal += amount;
                    }
                }
            }

            salesValueAll[householdId] = householdSalesValueAll;
            sellerInternalSaleValue[householdId] = householdSalesValueInternal;
        }

        // seller_days 系(economy.csv)と offer_* 系(prices.csv)は world.Market から読む
        // (段2が書いた当日の値がそのまま残っている。分母は「その日に売り注文を出した
        // (売り手×品目)」であり、Market の側から取る ── 出品していない売り手を含めない)。
        int sellerDaysTotal = 0;
        int sellerDaysWithoutReferenceTotal = 0;
        int sellerDaysCoefficientCappedTotal = 0;
        int sellerDaysAtFloorTotal = 0;
        int sellerDaysAtFloorWithExportTotal = 0;

        foreach (var (key, price) in world.Market)
        {
            int itemId = key.ItemId;
            int sellerId = key.SellerId;

            sellerDaysTotal++;
            offerCount[itemId]++;
            (offerPrices[itemId] ??= new List<int>()).Add(price);

            if (world.Metrics.SellerHasNoReference[sellerId] == 1)
            {
                sellerDaysWithoutReferenceTotal++;
            }

            if (world.Metrics.SellerCoefficientCapped[sellerId] == 1)
            {
                sellerDaysCoefficientCappedTotal++;
            }

            // Market のキーは常に都市生産品(1次産品はレシピの出力に現れないので売り注文を
            // 持たない) ── ExternalBuyPrice が例外を投げることはない。
            int floor = _definition.ExternalBuyPrice(itemId);

            if (price == floor)
            {
                sellerDaysAtFloorTotal++;
                offerAtFloorCount[itemId]++;

                if (exportedToday[sellerId])
                {
                    sellerDaysAtFloorWithExportTotal++;
                    offerAtFloorWithExportCount[itemId]++;
                }
            }
        }

        // households.csv・economy.csv の世帯別集計をまとめて求める。
        var householdRows = new List<HouseholdRow>(householdCount);

        int bankruptHouseholds = 0;
        int necessityBlockedHouseholds = 0;
        int necessityBlockedCount = 0;
        int inputBlockedHouseholds = 0;
        int productionStoppedHouseholds = 0;
        int productionRunsTotal = 0;
        int demandLinesTotal = 0;
        int demandLinesWithoutKnownPriceTotal = 0;
        int householdsWithoutKnownPrice = 0;
        long moneyTotal = 0;
        long profitTotalLong = 0;

        for (int householdId = 0; householdId < householdCount; householdId++)
        {
            var household = world.Households[householdId];
            var recipe = _definition.Recipes[(int)household.Occupation];
            int outputItemId = recipe.Outputs[0].ItemId; // 出力1件はTradeSystemのコンストラクタが保証

            // run_cost = Σ_j(仕入れ移動平均単価[j] × 必要数量_j) + 摩耗費[1回]
            // (GDD02a §5。式そのもの、規則8の表)。摩耗費はBuyerBudget.WearCostPerRunを呼ぶ。
            long runCost = 0;

            foreach (var input in recipe.Inputs)
            {
                runCost += (long)household.PurchaseUnitCostAverage[input.ItemId] * input.Quantity;
            }

            runCost += BuyerBudget.WearCostPerRun(
                household.PurchaseUnitCostAverage[Item.Tools], recipe.LaborPermille, _definition.ToolLifeLaborDays);

            long profit = salesValueAll[householdId] - (runCost * household.ProductionRuns);
            profitTotalLong += profit;

            bool hasOfferPrice = world.Market.TryGetValue(
                new MarketKey(outputItemId, householdId), out int offerPrice);

            householdRows.Add(new HouseholdRow(
                Day: today,
                HouseholdId: householdId,
                DistrictId: household.DistrictId,
                Occupation: (int)household.Occupation,
                OutputItemId: outputItemId,
                LiquidFunds: household.LiquidFunds,
                ProductionRuns: household.ProductionRuns,
                SalesValue: checked((int)salesValueAll[householdId]),
                RunCost: checked((int)runCost),
                Profit: checked((int)profit),
                IsBankrupt: household.IsBankrupt,
                NecessityBlockedCount: household.UnaffordableNecessityCount,
                InputBlocked: world.Metrics.InputBlockedByFunds[householdId],
                DemandLines: world.Metrics.DemandLines[householdId],
                DemandLinesWithoutKnownPrice: world.Metrics.DemandLinesWithoutKnownPrice[householdId],
                SellableStockOutput: SellableStock.Of(_definition, household, outputItemId),
                OfferPrice: hasOfferPrice ? offerPrice : -1));

            moneyTotal += household.LiquidFunds;
            bankruptHouseholds += household.IsBankrupt;

            if (household.UnaffordableNecessityCount > 0)
            {
                necessityBlockedHouseholds++;
            }

            necessityBlockedCount += household.UnaffordableNecessityCount;
            inputBlockedHouseholds += world.Metrics.InputBlockedByFunds[householdId];

            if (household.ProductionRuns == 0)
            {
                productionStoppedHouseholds++;
            }

            productionRunsTotal += household.ProductionRuns;
            demandLinesTotal += world.Metrics.DemandLines[householdId];
            demandLinesWithoutKnownPriceTotal += world.Metrics.DemandLinesWithoutKnownPrice[householdId];

            if (world.Metrics.DemandLinesWithoutKnownPrice[householdId] > 0)
            {
                householdsWithoutKnownPrice++;
            }
        }

        // trades.csv: partner_switch_permille(母数は「前日も当日も同じ品目を買った
        // (世帯,品目)」)。
        long switchNumerator = 0;
        long switchDenominator = 0;

        for (int householdId = 0; householdId < householdCount; householdId++)
        {
            for (int itemId = 0; itemId < itemCount; itemId++)
            {
                int index = world.Metrics.IndexOf(householdId, itemId);
                int previous = world.Metrics.PreviousCounterpartyId[index];
                int current = world.Metrics.CurrentCounterpartyId[index];

                if (previous == -1 || current == -1)
                {
                    continue;
                }

                switchDenominator++;

                if (previous != current)
                {
                    switchNumerator++;
                }
            }
        }

        int partnerSwitchPermille = switchDenominator == 0
            ? -1
            : checked((int)IntegerMath.CeilDiv(1000L * switchNumerator, switchDenominator));

        // trades.csv: hhi_permille_squared(都市内の約定だけを母数にする。窓口を除く)。
        long internalSaleTotal = 0;

        for (int householdId = 0; householdId < householdCount; householdId++)
        {
            internalSaleTotal += sellerInternalSaleValue[householdId];
        }

        int hhiPermilleSquared;

        if (internalSaleTotal <= 0)
        {
            hhiPermilleSquared = -1;
        }
        else
        {
            long sumOfSquares = 0;

            for (int householdId = 0; householdId < householdCount; householdId++)
            {
                if (sellerInternalSaleValue[householdId] <= 0)
                {
                    continue;
                }

                long share = IntegerMath.CeilDiv(1000L * sellerInternalSaleValue[householdId], internalSaleTotal);
                sumOfSquares += share * share;
            }

            hhiPermilleSquared = checked((int)sumOfSquares);
        }

        int activeSellerCount = 0;
        int activeBuyerCount = 0;

        for (int householdId = 0; householdId < householdCount; householdId++)
        {
            if (activeSeller[householdId])
            {
                activeSellerCount++;
            }

            if (activeBuyer[householdId])
            {
                activeBuyerCount++;
            }
        }

        var tradesRow = new TradesRow(
            Day: today,
            SettlementCount: settlementCountTotal,
            InternalSettlementCount: internalSettlementCountTotal,
            WindowSettlementCount: windowSettlementCountTotal,
            InternalSettlementValue: checked((int)internalSettlementValueTotal),
            PartnerSwitchPermille: partnerSwitchPermille,
            HhiPermilleSquared: hhiPermilleSquared,
            ActiveSellerCount: activeSellerCount,
            ActiveBuyerCount: activeBuyerCount);

        var economyRow = new EconomyRow(
            Day: today,
            Season: (int)season,
            MoneyTotal: checked((int)moneyTotal),
            BankruptHouseholds: bankruptHouseholds,
            NecessityBlockedHouseholds: necessityBlockedHouseholds,
            NecessityBlockedCount: necessityBlockedCount,
            InputBlockedHouseholds: inputBlockedHouseholds,
            ExportValue: checked((int)exportValueTotal),
            ExportQuantity: checked((int)exportQuantityTotal),
            ImportValue: checked((int)importValueTotal),
            ImportQuantity: checked((int)importQuantityTotal),
            SellerDays: sellerDaysTotal,
            SellerDaysWithoutReference: sellerDaysWithoutReferenceTotal,
            SellerDaysCoefficientCapped: sellerDaysCoefficientCappedTotal,
            SellerDaysAtFloor: sellerDaysAtFloorTotal,
            SellerDaysAtFloorWithExport: sellerDaysAtFloorWithExportTotal,
            ProductionStoppedHouseholds: productionStoppedHouseholds,
            ProductionRunsTotal: productionRunsTotal,
            DemandLines: demandLinesTotal,
            DemandLinesWithoutKnownPrice: demandLinesWithoutKnownPriceTotal,
            HouseholdsWithoutKnownPrice: householdsWithoutKnownPrice,
            ProfitTotal: checked((int)profitTotalLong));

        var priceRows = new List<PriceRow>(itemCount);

        for (int itemId = 0; itemId < itemCount; itemId++)
        {
            var settled = settledPrices[itemId];
            settled?.Sort();

            var offers = offerPrices[itemId];
            offers?.Sort();

            bool isPrimary = _definition.IsPrimaryItem(itemId);

            priceRows.Add(new PriceRow(
                Day: today,
                ItemId: itemId,
                SettledMedian: Median(settled),
                SettledMin: settled is { Count: > 0 } ? settled[0] : -1,
                SettledMax: settled is { Count: > 0 } ? settled[^1] : -1,
                SettledCount: settledCount[itemId],
                SettledQuantity: checked((int)settledQuantity[itemId]),
                SettledValue: checked((int)settledValue[itemId]),
                WindowSettledCount: windowSettledCount[itemId],
                WindowSettledQuantity: checked((int)windowSettledQuantity[itemId]),
                WindowSettledValue: checked((int)windowSettledValue[itemId]),
                OfferMin: offers is { Count: > 0 } ? offers[0] : -1,
                OfferMedian: Median(offers),
                OfferMax: offers is { Count: > 0 } ? offers[^1] : -1,
                OfferCount: offerCount[itemId],
                OfferAtFloorCount: offerAtFloorCount[itemId],
                OfferAtFloorWithExportCount: offerAtFloorWithExportCount[itemId],
                ExternalBuyPrice: isPrimary ? -1 : _definition.ExternalBuyPrice(itemId),
                ExternalSellPrice: _definition.ExternalSellPrice(itemId, season)));
        }

        var districtRows = new List<DistrictRow>();

        for (int itemId = 0; itemId < itemCount; itemId++)
        {
            if (_definition.IsPrimaryItem(itemId))
            {
                // 1次産品は中心区画の窓口でしか買えない。区画差が定義できない(GDD02d §2.1)。
                continue;
            }

            for (int districtId = 0; districtId < District.Count; districtId++)
            {
                int cell = (itemId * District.Count) + districtId;

                if (districtCount[cell] <= 0)
                {
                    // 約定0の行は書かない(36,000日×5品目×9区画の器に対し、実際に約定が
                    // ある組は一部)。
                    continue;
                }

                var prices = districtPrices[cell];
                prices?.Sort();

                districtRows.Add(new DistrictRow(
                    Day: today,
                    ItemId: itemId,
                    DistrictId: districtId,
                    SettledMedian: Median(prices),
                    SettledCount: districtCount[cell],
                    SettledQuantity: checked((int)districtQuantity[cell]),
                    SettledValue: checked((int)districtValue[cell]),
                    WindowSettledCount: districtWindowCount[cell]));
            }
        }

        var snapshot = new DailySnapshot(economyRow, priceRows, districtRows, householdRows, tradesRow);

        _sink.Write(in snapshot);
    }

    /// <summary>
    /// 約定の行単位の中央値(数量で重み付けしない)。件数が偶数なら中央2つの
    /// <c>CeilDiv(a + b, 2)</c>(GDD01 §2.3 の切り上げ)。0件は -1。
    /// </summary>
    /// <param name="sortedAscending">昇順に <c>Sort()</c> 済みであること。</param>
    private static int Median(List<int>? sortedAscending)
    {
        if (sortedAscending is null || sortedAscending.Count == 0)
        {
            return -1;
        }

        int count = sortedAscending.Count;

        if ((count & 1) == 1)
        {
            return sortedAscending[count / 2];
        }

        int lower = sortedAscending[(count / 2) - 1];
        int upper = sortedAscending[count / 2];

        return IntegerMath.CeilDiv(lower + upper, 2);
    }
}
