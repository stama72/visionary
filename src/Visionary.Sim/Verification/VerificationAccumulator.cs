using Visionary.Sim.Metrics;
using Visionary.Sim.Numerics;
using Visionary.Sim.Time;

namespace Visionary.Sim.Verification;

/// <summary>
/// 日次メトリクス(TDD01 §4.2)を1日ずつ受け取り、GDD02 §8 の7項目を判定する
/// (TDD01 §5.2)。<b>走行全体を溜めない</b> ── 保持するのは窓1つぶんの作業領域と、
/// 窓をまたぐ小さな状態だけである(W2-21 タスク仕様「3.」)。
/// </summary>
/// <remarks>
/// <para>
/// <b>判定は <see cref="World"/> を読まない。</b>入力は <see cref="DailySnapshot"/> と
/// <see cref="WorldDefinition"/> だけである(<see cref="WorldDefinition"/> は不変)。
/// </para>
/// <para>
/// <b>窓の畳み込みは2段である。</b>(1) 窓の中で複数の品目・職業を持つ項目
/// (8-1a・8-1b・8-3・8-4・8-5b)は、品目・職業ごとの判定を「赤が1つでもあれば赤、
/// それ以外に判定不能が1つでもあれば判定不能、それ以外は緑」で1つの窓判定へ畳む
/// (<see cref="Pool"/>)。(2) 窓をまたぐ畳み込みは TDD01 §5.2 の明示規則
/// (「1つでも赤い窓があれば赤、すべて判定不能なら判定不能、それ以外は緑」)をそのまま使う
/// (<see cref="Merge"/> / <see cref="Resolve"/>)。
/// <b>段(1)は仕様に明示が無い(決めて報告)。</b> §5.2 が明示するのは窓をまたぐ畳み込みだけで、
/// 窓の中で品目間をどう畳むかは書かれていない。「赤 &gt; 判定不能 &gt; 緑」の優先順位
/// (03-corrections 規則1「狭い側に倒す」)を段(1)にも適用する ── そうしないと、1品目だけが
/// 判定不能でも他の健全な品目に埋もれて窓全体が緑になり、判定不能が存在する目的
/// (「母数が消えた状態を緑と読ませない」)がまさにその場面で壊れる(テスト #3 が実測する形)。
/// </para>
/// </remarks>
public sealed class VerificationAccumulator : IDailyMetricsSink
{
    private readonly WorldDefinition _definition;
    private readonly int _itemCount;
    private readonly int _occupationCount;
    private readonly long _initialTotalMoney;

    /// <summary>輸入含有原価(GDD02d §3.1)。添字 = itemId。都市生産品だけが意味を持つ。</summary>
    private readonly long[] _importContentCost;

    /// <summary>都市生産品のいずれかで `輸入含有原価 &lt; 外部買値` が破れているか(静的・不変)。</summary>
    private readonly bool _importContentCostViolated;

    // 窓をまたぐ小さな状態 ── 保持するのは (i) 前の窓の偏差‰・(ii) 連続日数・(iii) 連続窓数・
    // (iv) FirstRedDay 相当の値だけである(タスク仕様「保証と、残る穴」)。
    private readonly DispersionState[] _dispersionState; // 8-1a。添字 = itemId
    private readonly long[] _streakDays; // 8-5b。添字 = itemId。走行を通した1本のカウンタ

    // 8-6: 窓末の貨幣総量の連続下降。
    private long _previousWindowEndMoney;
    private bool _hasPreviousWindowEndMoney;
    private int _moneyDeclineStreak;

    // 12項目のうち、窓を使う10項目の畳み込み状態(TDD01 §5.2 の窓の畳み込み規則)。
    // 8-5a と 8-6 は「全日」を見る特別扱いなので、この一般化には乗らない(下記専用フィールド)。
    private readonly IdFoldState _divergenceState = new();       // 8-1a
    private readonly IdFoldState _rigidityState = new();         // 8-1b
    private readonly IdFoldState _importCostState = new();       // 8-1c
    private readonly IdFoldState _bankruptState = new();         // 8-2a
    private readonly IdFoldState _inputBlockedState = new();     // 8-2b
    private readonly IdFoldState _productionStoppedState = new(); // 8-3
    private readonly IdFoldState _districtSpreadState = new();   // 8-4
    private readonly IdFoldState _bandExceededState = new();     // 8-5b
    private readonly IdFoldState _windowPurchaseState = new();   // 8-5c
    private readonly IdFoldState _unknownPriceState = new();     // 8-7

    // 8-5a(全日・不変条件)。
    private long _floorBreachFirstRedDay = -1;
    private IReadOnlyList<Evidence> _floorBreachEvidence = Array.Empty<Evidence>();

    // 8-6(全日の帯 OR 窓末の連続下降。どちらが先に赤くなったかで FirstRedDay が決まる)。
    private long _moneyDayFirstRedDay = -1;
    private IReadOnlyList<Evidence> _moneyDayEvidence = Array.Empty<Evidence>();
    private long _moneyWindowFirstRedDay = -1;
    private IReadOnlyList<Evidence> _moneyWindowEvidence = Array.Empty<Evidence>();

    // day の昇順・欠けなくの検査。
    private long _lastDay = -1;
    private bool _hasWrittenAnyDay;
    private long _daysWritten;
    private bool _built;

    private int _windowCount;

    // 窓1つぶんの作業領域(窓を閉じたら ResetWindowScratch で捨てる)。添字 = itemId。
    private readonly List<int>[] _windowMedians1a;
    private readonly List<int>[] _windowMedians1b;
    private readonly long[] _windowSellerDaysAtFloorWithExport1b;
    private readonly long[] _windowSellerDays1b;
    private int _windowSwitchMinPermille;
    private int _windowSwitchValidDays;

    private long _windowExportQuantitySum1c;

    private long _windowBankruptSum2;
    private long _windowInputBlockedSum2;
    private int _windowDayCount2;

    private readonly long[] _windowOccupationDays3;
    private readonly long[] _windowOccupationStoppedDays3;

    private readonly int[] _windowValidDayCount4;
    private readonly long[] _windowSpreadSum4;
    private readonly int[] _windowMinSellerHouseholds4;
    private readonly long[] _windowSettledCountSum4;
    private readonly long[] _windowSettledWindowCountSum4;
    private long _windowSellerDaysTotal4;
    private long _windowSellerDaysWithoutReferenceTotal4;

    private readonly bool[] _windowStreakTriggered5b;
    private readonly int[] _windowMaxStreak5b;

    private int _windowTestedDays5c;

    private long _windowMoneyMin6;
    private long _windowMoneyMax6;
    private long _windowLastMoneyTotal6;
    private bool _windowLeftBand6;
    private bool _windowCurrentlyOutside6;
    private bool _windowExportedDuringExcursion6;
    private bool _windowReturnedAfterExcursion6;
    private long _windowExportValueSum6;
    private long _windowImportValueSum6;

    private long _windowDemandLinesSum7;
    private long _windowDemandLinesWithoutKnownPriceSum7;

    // 日1つぶんの再利用スクラッチ(GCを避けるための場所。窓をまたいで使い回す)。
    private readonly int[] _dayDistrictCount;
    private readonly int[] _dayDistrictMax;
    private readonly int[] _dayDistrictMin;
    private readonly int[] _daySellerHouseholds;

    public VerificationAccumulator(WorldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _definition = definition;
        _itemCount = definition.ItemCount;
        _occupationCount = definition.OccupationCount;
        _initialTotalMoney = checked((long)definition.InitialLiquidFunds * definition.HouseholdCount);

        _importContentCost = new long[_itemCount];
        _importContentCostViolated = ComputeImportContentCost(definition, _importContentCost);

        _dispersionState = new DispersionState[_itemCount];
        _streakDays = new long[_itemCount];

        _windowMedians1a = new List<int>[_itemCount];
        _windowMedians1b = new List<int>[_itemCount];

        for (int i = 0; i < _itemCount; i++)
        {
            _windowMedians1a[i] = new List<int>();
            _windowMedians1b[i] = new List<int>();
        }

        _windowSellerDaysAtFloorWithExport1b = new long[_itemCount];
        _windowSellerDays1b = new long[_itemCount];
        _windowSettledCountSum4 = new long[_itemCount];
        _windowSettledWindowCountSum4 = new long[_itemCount];
        _windowValidDayCount4 = new int[_itemCount];
        _windowSpreadSum4 = new long[_itemCount];
        _windowMinSellerHouseholds4 = new int[_itemCount];
        _windowStreakTriggered5b = new bool[_itemCount];
        _windowMaxStreak5b = new int[_itemCount];

        _windowOccupationDays3 = new long[_occupationCount];
        _windowOccupationStoppedDays3 = new long[_occupationCount];

        _dayDistrictCount = new int[_itemCount];
        _dayDistrictMax = new int[_itemCount];
        _dayDistrictMin = new int[_itemCount];
        _daySellerHouseholds = new int[_itemCount];

        ResetWindowScratch();
    }

    public void Write(in DailySnapshot snapshot)
    {
        if (_built)
        {
            throw new InvalidOperationException("Build() の後に Write() を呼ばない(タスク仕様)。");
        }

        long day = snapshot.Economy.Day;

        // 順序が飛んだら例外(窓の切り方が日の並びに依存するので、黙って進むと窓が静かにずれる)。
        if (_hasWrittenAnyDay && day != _lastDay + 1)
        {
            throw new ArgumentException(
                $"day が昇順・欠けなく渡されなかった(前回={_lastDay}、今回={day})。"
                    + "VerificationAccumulator.Write の契約違反。",
                nameof(snapshot));
        }

        _lastDay = day;
        _hasWrittenAnyDay = true;
        _daysWritten++;

        // 8-5a・8-6(全日、過渡期を含む。TDD01 §5.2)。
        ProcessDayLevelChecks(snapshot);

        if (day >= VerificationThresholds.TransientDays)
        {
            AccumulateWindowData(snapshot);

            long windowEndDay = ComputeWindowEndDay(day);

            if (day == windowEndDay)
            {
                CloseWindow(windowEndDay);
            }
        }
    }

    /// <summary>走行の最後に1度だけ呼ぶ。<b>呼んだ後に <see cref="Write"/> を呼ばない。</b></summary>
    public SeedVerification Build(long seed)
    {
        if (_built)
        {
            throw new InvalidOperationException("Build() は1シードにつき1回だけ呼べる(タスク仕様)。");
        }

        _built = true;

        var items = new List<VerificationItemResult>(VerificationItemIds.All.Length)
        {
            Resolve(VerificationItemIds.Divergence, _divergenceState),
            Resolve(VerificationItemIds.Rigidity, _rigidityState),
            Resolve(VerificationItemIds.ImportContentCost, _importCostState),
            Resolve(VerificationItemIds.BankruptHouseholds, _bankruptState),
            Resolve(VerificationItemIds.InputBlockedHouseholds, _inputBlockedState),
            Resolve(VerificationItemIds.ProductionStopped, _productionStoppedState),
            Resolve(VerificationItemIds.DistrictSpread, _districtSpreadState),
            ResolveFloorBreach(),
            Resolve(VerificationItemIds.BandExceeded, _bandExceededState),
            Resolve(VerificationItemIds.WindowPurchase, _windowPurchaseState),
            ResolveMoneyBounded(),
            Resolve(VerificationItemIds.UnknownPriceRatio, _unknownPriceState),
        };

        return new SeedVerification(seed, checked((int)_daysWritten), _windowCount, items);
    }

    private static long ComputeWindowEndDay(long day)
    {
        long windowIndex = (day - VerificationThresholds.TransientDays) / VerificationThresholds.WindowDays;
        return VerificationThresholds.TransientDays
            + ((windowIndex + 1) * VerificationThresholds.WindowDays) - 1;
    }

    // ------------------------------------------------------------------
    // 8-5a・8-6(全日、過渡期を含む)
    // ------------------------------------------------------------------

    private void ProcessDayLevelChecks(in DailySnapshot snapshot)
    {
        long day = snapshot.Economy.Day;

        if (_floorBreachFirstRedDay == -1)
        {
            for (int item = 0; item < _itemCount; item++)
            {
                if (_definition.IsPrimaryItem(item))
                {
                    continue;
                }

                var price = snapshot.Prices[item];

                // 床は WorldDefinition.ExternalBuyPrice から取る。PriceRow.ExternalBuyPrice を
                // 読まない(タスク仕様「4.」)。
                int floor = _definition.ExternalBuyPrice(item);

                if (price.OfferCount > 0 && price.OfferMin < floor)
                {
                    _floorBreachFirstRedDay = day;
                    _floorBreachEvidence = new[]
                    {
                        new Evidence($"offerMin:{item}", price.OfferMin, floor, -1),
                    };
                    break;
                }
            }
        }

        if (_moneyDayFirstRedDay == -1)
        {
            long money = snapshot.Economy.MoneyTotal;
            bool violatesLower = money * 1000 < _initialTotalMoney * VerificationThresholds.MoneyLowerBoundPermille;
            bool violatesUpper = money * 1000 > _initialTotalMoney * VerificationThresholds.MoneyUpperBoundPermille;

            if (violatesLower || violatesUpper)
            {
                _moneyDayFirstRedDay = day;

                long thresholdPermille = violatesLower
                    ? VerificationThresholds.MoneyLowerBoundPermille
                    : VerificationThresholds.MoneyUpperBoundPermille;
                long thresholdValue = IntegerMath.CeilDiv(_initialTotalMoney * thresholdPermille, 1000);

                _moneyDayEvidence = new[]
                {
                    new Evidence("moneyTotal", money, thresholdValue, _initialTotalMoney),
                };
            }
        }
    }

    // ------------------------------------------------------------------
    // 窓の中の蓄積(day >= TransientDays)
    // ------------------------------------------------------------------

    private void AccumulateWindowData(in DailySnapshot snapshot)
    {
        var economy = snapshot.Economy;
        var season = (Season)economy.Season;

        _windowDayCount2++;
        _windowBankruptSum2 += economy.BankruptHouseholds;
        _windowInputBlockedSum2 += economy.InputBlockedHouseholds;
        _windowDemandLinesSum7 += economy.DemandLines;
        _windowDemandLinesWithoutKnownPriceSum7 += economy.DemandLinesWithoutKnownPrice;
        _windowSellerDaysTotal4 += economy.SellerDays;
        _windowSellerDaysWithoutReferenceTotal4 += economy.SellerDaysWithoutReference;
        _windowExportQuantitySum1c += economy.ExportQuantity;

        long money = economy.MoneyTotal;

        if (money < _windowMoneyMin6)
        {
            _windowMoneyMin6 = money;
        }

        if (money > _windowMoneyMax6)
        {
            _windowMoneyMax6 = money;
        }

        _windowLastMoneyTotal6 = money;
        _windowExportValueSum6 += economy.ExportValue;
        _windowImportValueSum6 += economy.ImportValue;

        bool insideBand =
            money * 1000 >= _initialTotalMoney * (1000L - VerificationThresholds.MoneyCalibrationBandPermille)
            && money * 1000 <= _initialTotalMoney * (1000L + VerificationThresholds.MoneyCalibrationBandPermille);

        if (!insideBand)
        {
            _windowLeftBand6 = true;
            _windowCurrentlyOutside6 = true;

            if (economy.ExportQuantity > 0)
            {
                _windowExportedDuringExcursion6 = true;
            }
        }
        else if (_windowCurrentlyOutside6)
        {
            _windowCurrentlyOutside6 = false;
            _windowReturnedAfterExcursion6 = true;
        }

        // trades.csv: partner_switch_permille の窓最小値(未定義日 = -1 は母数に入れない)。
        var trades = snapshot.Trades;

        if (trades.PartnerSwitchPermille != -1)
        {
            _windowSwitchValidDays++;

            if (trades.PartnerSwitchPermille < _windowSwitchMinPermille)
            {
                _windowSwitchMinPermille = trades.PartnerSwitchPermille;
            }
        }

        // households.csv: 職業別の世帯日と、品目別の担い手世帯数(1回の走査で両方を数える)。
        Array.Clear(_daySellerHouseholds, 0, _daySellerHouseholds.Length);

        foreach (var household in snapshot.Households)
        {
            _windowOccupationDays3[household.Occupation]++;

            if (household.ProductionRuns == 0)
            {
                _windowOccupationStoppedDays3[household.Occupation]++;
            }

            _daySellerHouseholds[household.OutputItemId]++;
        }

        for (int item = 0; item < _itemCount; item++)
        {
            if (_definition.IsPrimaryItem(item) || item == Item.Tools)
            {
                continue;
            }

            if (_daySellerHouseholds[item] < _windowMinSellerHouseholds4[item])
            {
                _windowMinSellerHouseholds4[item] = _daySellerHouseholds[item];
            }
        }

        // districts.csv: 品目ごとに、その日の区画別 settled_median の max-min(2区画以上の日だけ)。
        Array.Clear(_dayDistrictCount, 0, _dayDistrictCount.Length);
        Array.Fill(_dayDistrictMax, int.MinValue);
        Array.Fill(_dayDistrictMin, int.MaxValue);

        foreach (var district in snapshot.Districts)
        {
            if (district.ItemId == Item.Tools)
            {
                continue; // 8-4は工具を除く(GDD02 §8-4)。
            }

            _dayDistrictCount[district.ItemId]++;

            if (district.SettledMedian > _dayDistrictMax[district.ItemId])
            {
                _dayDistrictMax[district.ItemId] = district.SettledMedian;
            }

            if (district.SettledMedian < _dayDistrictMin[district.ItemId])
            {
                _dayDistrictMin[district.ItemId] = district.SettledMedian;
            }
        }

        for (int item = 0; item < _itemCount; item++)
        {
            if (_definition.IsPrimaryItem(item) || item == Item.Tools)
            {
                continue;
            }

            if (_dayDistrictCount[item] >= VerificationThresholds.MinDistrictsForSpread)
            {
                _windowValidDayCount4[item]++;
                _windowSpreadSum4[item] += _dayDistrictMax[item] - _dayDistrictMin[item];
            }
        }

        // prices.csv: 品目ごとの当日の値を窓のスクラッチへ足す。
        bool testedToday = false;

        for (int item = 0; item < _itemCount; item++)
        {
            if (_definition.IsPrimaryItem(item))
            {
                continue;
            }

            var price = snapshot.Prices[item];

            _windowSettledCountSum4[item] += price.SettledCount;
            _windowSettledWindowCountSum4[item] += price.WindowSettledCount;
            _windowSellerDaysAtFloorWithExport1b[item] += price.OfferAtFloorWithExportCount;
            _windowSellerDays1b[item] += price.OfferCount;

            // 天井は WorldDefinition.ExternalSellPrice(itemId, season) を呼ぶ。式を写さない
            // (タスク仕様「4.」)。季節は EconomyRow.Season から作る。
            int ceiling = _definition.ExternalSellPrice(item, season);

            if (price.OfferMax != -1 && price.OfferMax > ceiling)
            {
                testedToday = true;
            }

            if (price.SettledCount > 0)
            {
                _windowMedians1a[item].Add(price.SettledMedian);

                // 8-1bの除外: その日に出品した売り手が全員「床かつ輸出」の日だけを外す
                // (GDD02 §8-1。一部だけ該当する日は除かない)。
                bool allSellersAtFloorWithExport =
                    price.OfferCount > 0 && price.OfferAtFloorWithExportCount == price.OfferCount;

                if (!allSellersAtFloorWithExport)
                {
                    _windowMedians1b[item].Add(price.SettledMedian);
                }

                if (price.SettledMedian > ceiling)
                {
                    _streakDays[item]++;
                }
                else
                {
                    _streakDays[item] = 0;
                }
            }
            else
            {
                // 約定が無い日は「帯を上回ったまま」とは言えない(8-5b。タスク仕様「4.」)。
                _streakDays[item] = 0;
            }

            if (_streakDays[item] >= VerificationThresholds.BandExceededConsecutiveDays)
            {
                _windowStreakTriggered5b[item] = true;
            }

            if (_streakDays[item] > _windowMaxStreak5b[item])
            {
                _windowMaxStreak5b[item] = checked((int)_streakDays[item]);
            }
        }

        if (testedToday)
        {
            _windowTestedDays5c++;
        }
    }

    // ------------------------------------------------------------------
    // 窓を閉じる
    // ------------------------------------------------------------------

    private void CloseWindow(long windowEndDay)
    {
        _windowCount++;

        var (verdict1a, evidence1a) = Evaluate8_1a();
        Merge(_divergenceState, verdict1a, windowEndDay, evidence1a);

        var (verdict1b, evidence1b) = Evaluate8_1b();
        Merge(_rigidityState, verdict1b, windowEndDay, evidence1b);

        var (verdict1c, evidence1c) = Evaluate8_1c();
        Merge(_importCostState, verdict1c, windowEndDay, evidence1c);

        var (verdict2a, evidence2a) = EvaluateHouseholdRatio(
            _windowBankruptSum2, VerificationThresholds.BankruptHouseholdRatioPermille, "bankruptHouseholdRatio");
        Merge(_bankruptState, verdict2a, windowEndDay, evidence2a);

        var (verdict2b, evidence2b) = EvaluateHouseholdRatio(
            _windowInputBlockedSum2, VerificationThresholds.InputBlockedHouseholdRatioPermille,
            "inputBlockedHouseholdRatio");
        Merge(_inputBlockedState, verdict2b, windowEndDay, evidence2b);

        var (verdict3, evidence3) = Evaluate8_3();
        Merge(_productionStoppedState, verdict3, windowEndDay, evidence3);

        var (verdict4, evidence4) = Evaluate8_4();
        Merge(_districtSpreadState, verdict4, windowEndDay, evidence4);

        var (verdict5b, evidence5b) = Evaluate8_5b();
        Merge(_bandExceededState, verdict5b, windowEndDay, evidence5b);

        var (verdict5c, evidence5c) = Evaluate8_5c();
        Merge(_windowPurchaseState, verdict5c, windowEndDay, evidence5c);

        EvaluateWindow8_6(windowEndDay);

        var (verdict7, evidence7) = Evaluate8_7();
        Merge(_unknownPriceState, verdict7, windowEndDay, evidence7);

        ResetWindowScratch();
    }

    private (Verdict Verdict, List<Evidence> Evidence) Evaluate8_1a()
    {
        var parts = new List<Verdict>();
        var evidence = new List<Evidence>();

        for (int item = 0; item < _itemCount; item++)
        {
            if (_definition.IsPrimaryItem(item))
            {
                continue;
            }

            var medians = _windowMedians1a[item];
            int validDays = medians.Count;

            if (validDays < VerificationThresholds.MinValidDaysPerWindow)
            {
                parts.Add(Verdict.Indeterminate);
                evidence.Add(new Evidence($"validDays:{item}", validDays, VerificationThresholds.MinValidDaysPerWindow, -1));
                continue;
            }

            int floor = _definition.ExternalBuyPrice(item);
            int max = int.MinValue;
            int min = int.MaxValue;
            long sum = 0;

            foreach (int median in medians)
            {
                if (median > max)
                {
                    max = median;
                }

                if (median < min)
                {
                    min = median;
                }

                sum += median;
            }

            bool bandBreach = (long)max > (long)floor * VerificationThresholds.DivergenceUpperMultiplier
                || (long)min * VerificationThresholds.DivergenceLowerDivisor < floor;

            long average = IntegerMath.CeilDiv(sum, validDays);
            long dispersionPermille = -1;
            Verdict dispersionVerdict;

            if (average > 0)
            {
                long sumAbsoluteDeviation = 0;

                foreach (int median in medians)
                {
                    sumAbsoluteDeviation += Math.Abs(median - average);
                }

                dispersionPermille = IntegerMath.CeilDiv(1000L * sumAbsoluteDeviation, validDays * average);

                ref var dispersion = ref _dispersionState[item];

                if (dispersion.Count == 0)
                {
                    dispersion.First = dispersionPermille;
                    dispersion.Previous = dispersionPermille;
                    dispersion.MonotonicSoFar = true;
                    dispersion.Count = 1;
                }
                else
                {
                    if (dispersionPermille < dispersion.Previous)
                    {
                        dispersion.MonotonicSoFar = false;
                    }

                    dispersion.Previous = dispersionPermille;
                    dispersion.Count++;
                }

                // First == 0 は「最初の窓から偏差ゼロ」であり、0 × 2 = 0 という自明な不等式が
                // 常に成り立ってしまう(平坦なゼロを発散と誤読する)。First > 0 を必ず要求する。
                dispersionVerdict = dispersion.Count >= VerificationThresholds.MinWindowsForDispersion
                    && dispersion.MonotonicSoFar
                    && dispersion.First > 0
                    && dispersion.Previous >= dispersion.First * VerificationThresholds.DispersionGrowthMultiplier
                        ? Verdict.Red
                        : Verdict.Green;
            }
            else
            {
                // 窓の平均が0(ゼロ除算)。偏差の枝だけ判定不能にする(タスク仕様「4.」)。
                dispersionVerdict = Verdict.Indeterminate;
            }

            Verdict itemVerdict;

            if (bandBreach)
            {
                itemVerdict = Verdict.Red;
            }
            else if (dispersionVerdict == Verdict.Red)
            {
                itemVerdict = Verdict.Red;
            }
            else if (dispersionVerdict == Verdict.Indeterminate)
            {
                itemVerdict = Verdict.Indeterminate;
            }
            else
            {
                itemVerdict = Verdict.Green;
            }

            parts.Add(itemVerdict);
            evidence.Add(new Evidence(
                $"settledMedianMax:{item}", max, (long)floor * VerificationThresholds.DivergenceUpperMultiplier, validDays));
            evidence.Add(new Evidence($"settledMedianMin:{item}", min, floor, validDays));
            evidence.Add(new Evidence($"dispersionPermille:{item}", dispersionPermille, -1, validDays));
        }

        return (Pool(parts), evidence);
    }

    private (Verdict Verdict, List<Evidence> Evidence) Evaluate8_1b()
    {
        var parts = new List<Verdict>();
        var evidence = new List<Evidence>();

        for (int item = 0; item < _itemCount; item++)
        {
            if (_definition.IsPrimaryItem(item))
            {
                continue;
            }

            var medians = _windowMedians1b[item];
            int validDays = medians.Count;
            int floor = _definition.ExternalBuyPrice(item);
            Verdict rangeVerdict;

            if (validDays < VerificationThresholds.MinValidDaysPerWindow)
            {
                rangeVerdict = Verdict.Indeterminate;
                evidence.Add(new Evidence($"settledMedianRange:{item}", -1, VerificationThresholds.RigidityRangePermille, validDays));
            }
            else
            {
                int max = int.MinValue;
                int min = int.MaxValue;

                foreach (int median in medians)
                {
                    if (median > max)
                    {
                        max = median;
                    }

                    if (median < min)
                    {
                        min = median;
                    }
                }

                int range = max - min;
                bool narrow = (long)range * 1000 < (long)floor * VerificationThresholds.RigidityRangePermille;
                rangeVerdict = narrow ? Verdict.Red : Verdict.Green;
                evidence.Add(new Evidence($"settledMedianRange:{item}", range, VerificationThresholds.RigidityRangePermille, validDays));
            }

            parts.Add(rangeVerdict);

            long sellerDaysAtFloorWithExport = _windowSellerDaysAtFloorWithExport1b[item];
            long sellerDays = _windowSellerDays1b[item];
            long floorExportRatio = sellerDays > 0
                ? IntegerMath.CeilDiv(1000L * sellerDaysAtFloorWithExport, sellerDays)
                : -1;
            evidence.Add(new Evidence($"floorExportSellerDaysRatio:{item}", floorExportRatio, -1, sellerDays));
        }

        Verdict switchVerdict;

        if (_windowSwitchValidDays == 0)
        {
            switchVerdict = Verdict.Indeterminate;
        }
        else
        {
            switchVerdict = _windowSwitchMinPermille < VerificationThresholds.PartnerSwitchFloorPermille
                ? Verdict.Red
                : Verdict.Green;
        }

        parts.Add(switchVerdict);
        evidence.Add(new Evidence(
            "partnerSwitchMinPermille",
            _windowSwitchValidDays == 0 ? -1 : _windowSwitchMinPermille,
            VerificationThresholds.PartnerSwitchFloorPermille,
            _windowSwitchValidDays));

        return (Pool(parts), evidence);
    }

    private (Verdict Verdict, List<Evidence> Evidence) Evaluate8_1c()
    {
        var evidence = new List<Evidence>();

        for (int item = 0; item < _itemCount; item++)
        {
            if (_definition.IsPrimaryItem(item))
            {
                continue;
            }

            evidence.Add(new Evidence($"importContentCost:{item}", _importContentCost[item], -1, -1));
            evidence.Add(new Evidence($"externalBuyPrice:{item}", _definition.ExternalBuyPrice(item), -1, -1));
        }

        // 品目別のexport_quantityは日次メトリクスの列に無い(収集側には触れない。タスク仕様
        // 「変えない既存コード」)ため、都市全体の集計値(EconomyRow.ExportQuantity)を使う
        // (決めて報告)。
        evidence.Add(new Evidence("windowExportQuantity", _windowExportQuantitySum1c, 0, -1));

        Verdict verdict = _importContentCostViolated || _windowExportQuantitySum1c == 0
            ? Verdict.Red
            : Verdict.Green;

        return (verdict, evidence);
    }

    private (Verdict Verdict, List<Evidence> Evidence) EvaluateHouseholdRatio(
        long sum, int thresholdPermille, string evidenceName)
    {
        long denominator = (long)_windowDayCount2 * _definition.HouseholdCount;
        long ratio = denominator > 0 ? IntegerMath.CeilDiv(1000L * sum, denominator) : 0;
        Verdict verdict = ratio >= thresholdPermille ? Verdict.Red : Verdict.Green;
        var evidence = new List<Evidence> { new(evidenceName, ratio, thresholdPermille, denominator) };

        return (verdict, evidence);
    }

    private (Verdict Verdict, List<Evidence> Evidence) Evaluate8_3()
    {
        var parts = new List<Verdict>();
        var evidence = new List<Evidence>();
        bool anyOccupationCounted = false;

        for (int occupation = 0; occupation < _occupationCount; occupation++)
        {
            long occupationDays = _windowOccupationDays3[occupation];
            long stoppedDays = _windowOccupationStoppedDays3[occupation];

            if (occupationDays > 0)
            {
                anyOccupationCounted = true;

                long ratio = IntegerMath.CeilDiv(1000L * stoppedDays, occupationDays);
                Verdict verdict = ratio >= VerificationThresholds.ProductionStoppedRatioPermille
                    ? Verdict.Red
                    : Verdict.Green;

                parts.Add(verdict);
                evidence.Add(new Evidence(
                    $"productionStoppedRatio:{occupation}", ratio,
                    VerificationThresholds.ProductionStoppedRatioPermille, occupationDays));
            }
            else if (occupation == (int)Occupation.Brewer)
            {
                // 醸造は閾値を超えていなくても、居なくても必ず根拠に出す(GDD02 §8-3)。
                evidence.Add(new Evidence(
                    $"productionStoppedRatio:{occupation}", -1,
                    VerificationThresholds.ProductionStoppedRatioPermille, 0));
            }
        }

        if (!anyOccupationCounted)
        {
            return (Verdict.Indeterminate, evidence);
        }

        return (Pool(parts), evidence);
    }

    private (Verdict Verdict, List<Evidence> Evidence) Evaluate8_4()
    {
        var parts = new List<Verdict>();
        var evidence = new List<Evidence>();

        for (int item = 0; item < _itemCount; item++)
        {
            if (_definition.IsPrimaryItem(item) || item == Item.Tools)
            {
                continue;
            }

            int minSellerHouseholds = _windowMinSellerHouseholds4[item];
            int validDays = _windowValidDayCount4[item];
            int floor = _definition.ExternalBuyPrice(item);
            Verdict itemVerdict;

            if (minSellerHouseholds < VerificationThresholds.MinSellerHouseholdsForSpread)
            {
                itemVerdict = Verdict.Indeterminate;
            }
            else if (validDays < VerificationThresholds.MinValidDaysPerWindow)
            {
                itemVerdict = Verdict.Indeterminate;
            }
            else
            {
                long windowAverage = IntegerMath.CeilDiv(_windowSpreadSum4[item], validDays);
                bool converged = windowAverage * 1000 < (long)floor * VerificationThresholds.DistrictSpreadPermille;
                itemVerdict = converged ? Verdict.Red : Verdict.Green;
                evidence.Add(new Evidence($"districtSpreadAveragePermille:{item}", windowAverage, floor, validDays));
            }

            parts.Add(itemVerdict);
            evidence.Add(new Evidence(
                $"sellerHouseholds:{item}", minSellerHouseholds, VerificationThresholds.MinSellerHouseholdsForSpread, -1));

            long settledCountSum = _windowSettledCountSum4[item];
            long windowSettledCountSum = _windowSettledWindowCountSum4[item];
            long windowPurchaseRatio = settledCountSum > 0
                ? IntegerMath.CeilDiv(1000L * windowSettledCountSum, settledCountSum)
                : -1;
            evidence.Add(new Evidence($"windowPurchaseRatio:{item}", windowPurchaseRatio, -1, settledCountSum));
        }

        long sellerDaysWithoutReferenceRatio = _windowSellerDaysTotal4 > 0
            ? IntegerMath.CeilDiv(1000L * _windowSellerDaysWithoutReferenceTotal4, _windowSellerDaysTotal4)
            : -1;
        evidence.Add(new Evidence(
            "sellerDaysWithoutReferenceRatio", sellerDaysWithoutReferenceRatio, -1, _windowSellerDaysTotal4));

        return (Pool(parts), evidence);
    }

    private (Verdict Verdict, List<Evidence> Evidence) Evaluate8_5b()
    {
        var parts = new List<Verdict>();
        var evidence = new List<Evidence>();

        for (int item = 0; item < _itemCount; item++)
        {
            if (_definition.IsPrimaryItem(item))
            {
                continue;
            }

            int validDays = _windowMedians1a[item].Count; // 8-1aと同じ有効日基準(settled_count>0)。
            Verdict verdict;

            if (validDays < VerificationThresholds.MinValidDaysPerWindow)
            {
                verdict = Verdict.Indeterminate;
            }
            else
            {
                verdict = _windowStreakTriggered5b[item] ? Verdict.Red : Verdict.Green;
            }

            parts.Add(verdict);
            evidence.Add(new Evidence(
                $"longestStreakDays:{item}", _windowMaxStreak5b[item],
                VerificationThresholds.BandExceededConsecutiveDays, validDays));
        }

        return (Pool(parts), evidence);
    }

    private (Verdict Verdict, List<Evidence> Evidence) Evaluate8_5c()
    {
        long windowSettledCountSum = 0;

        for (int item = 0; item < _itemCount; item++)
        {
            if (_definition.IsPrimaryItem(item))
            {
                continue;
            }

            windowSettledCountSum += _windowSettledWindowCountSum4[item];
        }

        Verdict verdict = _windowTestedDays5c == 0
            ? Verdict.Indeterminate
            : windowSettledCountSum == 0 ? Verdict.Red : Verdict.Green;

        var evidence = new List<Evidence>
        {
            new("testedDays", _windowTestedDays5c, -1, VerificationThresholds.WindowDays),
            new("windowSettledCountSum", windowSettledCountSum, 0, -1),
        };

        return (verdict, evidence);
    }

    /// <summary>
    /// 8-6 の窓ぶん(窓末の連続下降 + calibration/mechanism/unknown の分類)。
    /// 全日の帯チェック(<see cref="ProcessDayLevelChecks"/>)とは別枠で、<see cref="ResolveMoneyBounded"/>
    /// が組み合わせる。
    /// </summary>
    private void EvaluateWindow8_6(long windowEndDay)
    {
        long windowEndMoney = _windowLastMoneyTotal6;

        if (_hasPreviousWindowEndMoney && windowEndMoney < _previousWindowEndMoney)
        {
            _moneyDeclineStreak++;
        }
        else
        {
            _moneyDeclineStreak = 0;
        }

        bool declineRed = _moneyDeclineStreak >= VerificationThresholds.MoneyDecliningWindows;

        _previousWindowEndMoney = windowEndMoney;
        _hasPreviousWindowEndMoney = true;

        // calibration = 0 / mechanism = 1 / unknown = 2(GDD02 §8-6)。判定には使わない、記録のみ。
        int boundedBy;

        if (!_windowLeftBand6)
        {
            boundedBy = 0;
        }
        else if (_windowReturnedAfterExcursion6 && _windowExportedDuringExcursion6)
        {
            boundedBy = 1;
        }
        else
        {
            boundedBy = 2;
        }

        var evidence = new List<Evidence>
        {
            new("moneyTotalMin", _windowMoneyMin6, IntegerMath.CeilDiv(_initialTotalMoney * VerificationThresholds.MoneyLowerBoundPermille, 1000), _initialTotalMoney),
            new("moneyTotalMax", _windowMoneyMax6, IntegerMath.CeilDiv(_initialTotalMoney * VerificationThresholds.MoneyUpperBoundPermille, 1000), _initialTotalMoney),
            new("moneyTotalWindowEnd", windowEndMoney, -1, _initialTotalMoney),
            new("exportValueSum", _windowExportValueSum6, -1, -1),
            new("importValueSum", _windowImportValueSum6, -1, -1),
            new("boundedBy", boundedBy, -1, -1),
        };

        if (declineRed)
        {
            if (_moneyWindowFirstRedDay == -1)
            {
                _moneyWindowFirstRedDay = windowEndDay;
                _moneyWindowEvidence = evidence;
            }
        }
        else if (_moneyWindowFirstRedDay == -1)
        {
            // まだ赤くなっていない ── 緑の間は「最後の窓」の根拠を保つ(規則「根拠は判定を決めた
            // 窓のものを出す」)。
            _moneyWindowEvidence = evidence;
        }
    }

    private (Verdict Verdict, List<Evidence> Evidence) Evaluate8_7()
    {
        long denominator = _windowDemandLinesSum7;
        Verdict verdict;
        long ratio;

        if (denominator == 0)
        {
            verdict = Verdict.Indeterminate;
            ratio = -1;
        }
        else
        {
            ratio = IntegerMath.CeilDiv(1000L * _windowDemandLinesWithoutKnownPriceSum7, denominator);
            verdict = ratio >= VerificationThresholds.UnknownPriceLineRatioPermille ? Verdict.Red : Verdict.Green;
        }

        var evidence = new List<Evidence>
        {
            new("unknownPriceLineRatio", ratio, VerificationThresholds.UnknownPriceLineRatioPermille, denominator),
        };

        return (verdict, evidence);
    }

    // ------------------------------------------------------------------
    // 窓の畳み込み(段2。TDD01 §5.2 の明示規則)
    // ------------------------------------------------------------------

    /// <summary>窓の中の複数の判定を1つへ畳む(段1。決めて報告 — 上の doc コメントを参照)。</summary>
    private static Verdict Pool(IReadOnlyList<Verdict> parts)
    {
        bool anyIndeterminate = false;

        foreach (var part in parts)
        {
            if (part == Verdict.Red)
            {
                return Verdict.Red;
            }

            if (part == Verdict.Indeterminate)
            {
                anyIndeterminate = true;
            }
        }

        return anyIndeterminate ? Verdict.Indeterminate : Verdict.Green;
    }

    /// <summary>
    /// 窓をまたぐ畳み込み(段2。TDD01 §5.2「1つでも赤い窓があれば赤、すべて判定不能なら判定不能、
    /// それ以外は緑」)。赤くなった最初の窓で <see cref="IdFoldState.FirstRedDay"/> と
    /// <see cref="IdFoldState.Evidence"/> を凍結し、以後は上書きしない(テスト #28 の核心)。
    /// </summary>
    private static void Merge(IdFoldState state, Verdict windowVerdict, long windowEndDay, IReadOnlyList<Evidence> evidence)
    {
        state.WindowsSeen++;

        if (windowVerdict == Verdict.Red)
        {
            if (!state.RedLocked)
            {
                state.RedLocked = true;
                state.FirstRedDay = windowEndDay;
                state.Evidence = evidence;
            }

            return;
        }

        if (state.RedLocked)
        {
            return;
        }

        if (windowVerdict == Verdict.Indeterminate)
        {
            state.IndeterminateWindows++;
        }

        state.Evidence = evidence;
    }

    private static VerificationItemResult Resolve(string id, IdFoldState state)
    {
        Verdict verdict;

        if (state.RedLocked)
        {
            verdict = Verdict.Red;
        }
        else if (state.WindowsSeen == 0 || state.IndeterminateWindows == state.WindowsSeen)
        {
            verdict = Verdict.Indeterminate;
        }
        else
        {
            verdict = Verdict.Green;
        }

        long firstRedDay = verdict == Verdict.Red ? state.FirstRedDay : -1;

        return new VerificationItemResult(id, verdict, firstRedDay, state.Evidence);
    }

    private VerificationItemResult ResolveFloorBreach()
    {
        Verdict verdict = _floorBreachFirstRedDay == -1 ? Verdict.Green : Verdict.Red;

        return new VerificationItemResult(
            VerificationItemIds.FloorBreach, verdict, _floorBreachFirstRedDay, _floorBreachEvidence);
    }

    private VerificationItemResult ResolveMoneyBounded()
    {
        long firstRedDay;

        if (_moneyDayFirstRedDay != -1 && _moneyWindowFirstRedDay != -1)
        {
            firstRedDay = Math.Min(_moneyDayFirstRedDay, _moneyWindowFirstRedDay);
        }
        else if (_moneyDayFirstRedDay != -1)
        {
            firstRedDay = _moneyDayFirstRedDay;
        }
        else if (_moneyWindowFirstRedDay != -1)
        {
            firstRedDay = _moneyWindowFirstRedDay;
        }
        else
        {
            firstRedDay = -1;
        }

        Verdict verdict = firstRedDay == -1 ? Verdict.Green : Verdict.Red;

        IReadOnlyList<Evidence> evidence = firstRedDay == -1
            ? _moneyWindowEvidence
            : firstRedDay == _moneyDayFirstRedDay
                ? _moneyDayEvidence
                : _moneyWindowEvidence;

        return new VerificationItemResult(VerificationItemIds.MoneyBounded, verdict, firstRedDay, evidence);
    }

    private void ResetWindowScratch()
    {
        for (int item = 0; item < _itemCount; item++)
        {
            _windowMedians1a[item].Clear();
            _windowMedians1b[item].Clear();
            _windowSellerDaysAtFloorWithExport1b[item] = 0;
            _windowSellerDays1b[item] = 0;
            _windowSettledCountSum4[item] = 0;
            _windowSettledWindowCountSum4[item] = 0;
            _windowValidDayCount4[item] = 0;
            _windowSpreadSum4[item] = 0;
            _windowMinSellerHouseholds4[item] = int.MaxValue;
            _windowStreakTriggered5b[item] = false;
            _windowMaxStreak5b[item] = 0;
        }

        Array.Clear(_windowOccupationDays3, 0, _windowOccupationDays3.Length);
        Array.Clear(_windowOccupationStoppedDays3, 0, _windowOccupationStoppedDays3.Length);

        _windowSwitchMinPermille = int.MaxValue;
        _windowSwitchValidDays = 0;

        _windowExportQuantitySum1c = 0;

        _windowBankruptSum2 = 0;
        _windowInputBlockedSum2 = 0;
        _windowDayCount2 = 0;

        _windowTestedDays5c = 0;

        _windowMoneyMin6 = long.MaxValue;
        _windowMoneyMax6 = long.MinValue;
        _windowLeftBand6 = false;
        _windowCurrentlyOutside6 = false;
        _windowExportedDuringExcursion6 = false;
        _windowReturnedAfterExcursion6 = false;
        _windowExportValueSum6 = 0;
        _windowImportValueSum6 = 0;

        _windowDemandLinesSum7 = 0;
        _windowDemandLinesWithoutKnownPriceSum7 = 0;

        _windowSellerDaysTotal4 = 0;
        _windowSellerDaysWithoutReferenceTotal4 = 0;
    }

    // ------------------------------------------------------------------
    // 8-1c: 輸入含有原価(GDD02d §3.1)。静的・不変(WorldDefinitionから決まる)。
    // ------------------------------------------------------------------

    private static bool ComputeImportContentCost(WorldDefinition definition, long[] result)
    {
        var computed = new bool[definition.ItemCount];
        var visiting = new bool[definition.ItemCount];

        for (int item = 0; item < definition.ItemCount; item++)
        {
            ResolveImportContentCost(definition, item, result, computed, visiting);
        }

        bool anyViolation = false;

        for (int item = 0; item < definition.ItemCount; item++)
        {
            if (!definition.IsPrimaryItem(item) && result[item] >= definition.ExternalBuyPrice(item))
            {
                anyViolation = true;
            }
        }

        return anyViolation;
    }

    private static long ResolveImportContentCost(
        WorldDefinition definition, int itemId, long[] result, bool[] computed, bool[] visiting)
    {
        if (computed[itemId])
        {
            return result[itemId];
        }

        if (visiting[itemId])
        {
            throw new InvalidOperationException("輸入含有原価の計算でレシピの循環を検出した(想定外)。");
        }

        visiting[itemId] = true;

        long cost;

        if (definition.IsPrimaryItem(itemId))
        {
            // 1次産品の輸入含有原価は外部売値。季節を持つので年間の最大値(4季のmax)を使う
            // (GDD02d §4.3 (e)。一番きつい季節で条件が破れるなら破れている)。
            long max = long.MinValue;

            for (int seasonValue = 0; seasonValue < 4; seasonValue++)
            {
                int sellPrice = definition.ExternalSellPrice(itemId, (Season)seasonValue);

                if (sellPrice > max)
                {
                    max = sellPrice;
                }
            }

            cost = max;
        }
        else
        {
            var recipe = FindRecipeForOutput(definition, itemId, out int outputQuantity);
            long sum = 0;

            foreach (var input in recipe.Inputs)
            {
                sum += (long)input.Quantity * ResolveImportContentCost(definition, input.ItemId, result, computed, visiting);
            }

            cost = IntegerMath.CeilDiv(sum, outputQuantity);
        }

        result[itemId] = cost;
        computed[itemId] = true;
        visiting[itemId] = false;

        return cost;
    }

    private static Recipe FindRecipeForOutput(WorldDefinition definition, int itemId, out int outputQuantity)
    {
        foreach (var recipe in definition.Recipes)
        {
            foreach (var output in recipe.Outputs)
            {
                if (output.ItemId == itemId)
                {
                    outputQuantity = output.Quantity;
                    return recipe;
                }
            }
        }

        throw new InvalidOperationException($"品目Id={itemId}を出力するレシピが無い(1次産品ではないはず)。");
    }

    /// <summary>8-1aの偏差の枝が窓をまたいで持ち越す小さな状態。</summary>
    private struct DispersionState
    {
        public int Count;
        public long First;
        public long Previous;
        public bool MonotonicSoFar;
    }

    /// <summary>窓を使う10項目の畳み込み状態(TDD01 §5.2)。</summary>
    private sealed class IdFoldState
    {
        public bool RedLocked;
        public long FirstRedDay = -1;
        public IReadOnlyList<Evidence> Evidence = Array.Empty<Evidence>();
        public int WindowsSeen;
        public int IndeterminateWindows;
    }
}
