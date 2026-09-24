using Visionary.Sim.Metrics;
using Visionary.Sim.Time;
using Visionary.Sim.Verification;

namespace Visionary.Sim.Tests.Verification;

/// <summary>
/// <see cref="VerificationAccumulator"/> の検査(W2-21 タスク仕様「落ちるべき条件」#1〜#29・#36・#37)。
/// </summary>
public sealed class VerificationAccumulatorTests
{
    private const int TransientDays = VerificationThresholds.TransientDays; // 30
    private const int WindowDays = VerificationThresholds.WindowDays;      // 120

    private static SeedVerification Run(IReadOnlyList<DailySnapshot> days, long seed = 1)
    {
        var accumulator = new VerificationAccumulator(DailySnapshotTestBuilder.Definition);

        foreach (var day in days)
        {
            accumulator.Write(in day);
        }

        return accumulator.Build(seed);
    }

    private static VerificationItemResult GetItem(SeedVerification seed, string id) =>
        seed.Items.Single(i => i.Id == id);

    private static Evidence FindEvidence(VerificationItemResult item, string name) =>
        item.Evidence.Single(e => e.Name == name);

    /// <summary>#1(前半)。過渡期の帯超えは8-1aに影響しない(緑)。窓内の帯超えは赤になる。</summary>
    [Fact]
    public void WindowsStartAfterTheTransient_TransientViolationIsIgnored()
    {
        var days = DailySnapshotTestBuilder.Sequence(150).ToList();

        // day5(過渡期)にBreadの帯を大きく超える値を置く。
        var badPrice = days[5].Prices[Item.Bread] with { SettledMedian = 10000, SettledCount = 4 };
        days[5] = DailySnapshotTestBuilder.WithPrice(days[5], Item.Bread, badPrice);

        var result = Run(days);

        Assert.Equal(Verdict.Green, GetItem(result, "8-1a").Verdict);
    }

    /// <summary>#1(後半)。窓の中(day30以降)に置くと赤になる。</summary>
    [Fact]
    public void WindowsStartAfterTheTransient_WindowViolationIsRed()
    {
        var days = DailySnapshotTestBuilder.Sequence(150).ToList();

        var badPrice = days[100].Prices[Item.Bread] with { SettledMedian = 10000, SettledCount = 4 };
        days[100] = DailySnapshotTestBuilder.WithPrice(days[100], Item.Bread, badPrice);

        var result = Run(days);

        Assert.Equal(Verdict.Red, GetItem(result, "8-1a").Verdict);
    }

    /// <summary>#1(8-6)。8-6は過渡期(day0)の違反でも赤になる。</summary>
    [Fact]
    public void WindowsStartAfterTheTransient_MoneyBoundedChecksDayZero()
    {
        var days = DailySnapshotTestBuilder.Sequence(10).ToList();
        int initial = DailySnapshotTestBuilder.InitialTotalMoney;

        days[0] = DailySnapshotTestBuilder.WithEconomy(
            days[0], days[0].Economy with { MoneyTotal = initial / 10 });

        var result = Run(days);
        var item = GetItem(result, "8-6");

        Assert.Equal(Verdict.Red, item.Verdict);
        Assert.Equal(0, item.FirstRedDay);
    }

    /// <summary>#1(8-5a)。8-5aは過渡期(day0)の違反でも赤になる。</summary>
    [Fact]
    public void WindowsStartAfterTheTransient_FloorBreachChecksDayZero()
    {
        var days = DailySnapshotTestBuilder.Sequence(10).ToList();
        int floor = DailySnapshotTestBuilder.Floor(Item.Bread);

        var badPrice = days[0].Prices[Item.Bread] with { OfferMin = floor - 1, OfferCount = 1 };
        days[0] = DailySnapshotTestBuilder.WithPrice(days[0], Item.Bread, badPrice);

        var result = Run(days);
        var item = GetItem(result, "8-5a");

        Assert.Equal(Verdict.Red, item.Verdict);
        Assert.Equal(0, item.FirstRedDay);
    }

    /// <summary>#2。端数の窓(149日)は判定不能、150日なら判定が出る。</summary>
    [Fact]
    public void PartialWindowIsNotJudged()
    {
        var partial = Run(DailySnapshotTestBuilder.Sequence(TransientDays + WindowDays - 1));
        var full = Run(DailySnapshotTestBuilder.Sequence(TransientDays + WindowDays));

        Assert.Equal(0, partial.WindowCount);
        Assert.Equal(Verdict.Indeterminate, GetItem(partial, "8-2a").Verdict);

        Assert.Equal(1, full.WindowCount);
        Assert.Equal(Verdict.Green, GetItem(full, "8-2a").Verdict);
    }

    /// <summary>#3。有効日0の品目は判定不能になり、緑にならない(8-1a/8-1b/8-5b)。</summary>
    [Fact]
    public void EmptyDenominatorIsIndeterminateNotGreen()
    {
        var days = DailySnapshotTestBuilder.Sequence(150).ToList();

        for (int day = 0; day < days.Count; day++)
        {
            var deadPrice = days[day].Prices[Item.Bread] with
            {
                SettledCount = 0,
                SettledMedian = -1,
                SettledMin = -1,
                SettledMax = -1,
            };
            days[day] = DailySnapshotTestBuilder.WithPrice(days[day], Item.Bread, deadPrice);
        }

        var result = Run(days);

        Assert.Equal(Verdict.Indeterminate, GetItem(result, "8-1a").Verdict);
        Assert.Equal(Verdict.Indeterminate, GetItem(result, "8-1b").Verdict);
        Assert.Equal(Verdict.Indeterminate, GetItem(result, "8-5b").Verdict);
    }

    /// <summary>#4。有効日が29日の窓は判定不能、30日なら判定が出る。</summary>
    [Fact]
    public void ValidDayFloorIsEnforced()
    {
        var days29 = DailySnapshotTestBuilder.Sequence(150).ToList();
        ZeroOutSettledCount(days29, Item.Beer, TransientDays, TransientDays + 90); // 91日を殺す→29日だけ有効

        var days30 = DailySnapshotTestBuilder.Sequence(150).ToList();
        ZeroOutSettledCount(days30, Item.Beer, TransientDays, TransientDays + 89); // 90日を殺す→30日だけ有効

        var result29 = Run(days29);
        var result30 = Run(days30);

        Assert.Equal(Verdict.Indeterminate, GetItem(result29, "8-1a").Verdict);
        Assert.Equal(Verdict.Green, GetItem(result30, "8-1a").Verdict);
    }

    private static void ZeroOutSettledCount(List<DailySnapshot> days, int itemId, int fromDay, int toDayInclusive)
    {
        for (int day = fromDay; day <= toDayInclusive; day++)
        {
            var dead = days[day].Prices[itemId] with
            {
                SettledCount = 0,
                SettledMedian = -1,
                SettledMin = -1,
                SettledMax = -1,
            };
            days[day] = DailySnapshotTestBuilder.WithPrice(days[day], itemId, dead);
        }
    }

    /// <summary>
    /// #5。核心。基準は <see cref="WorldDefinition.ExternalBuyPrice"/> であり、<see cref="PriceRow.ExternalBuyPrice"/>
    /// を読むと同じ定数どうしの比較になる ── テストは <see cref="PriceRow"/> 側に誤った床を入れる。
    /// </summary>
    [Fact]
    public void DivergenceUsesTheFloorAsTheBase()
    {
        int floor = DailySnapshotTestBuilder.Floor(Item.Bread); // 54。帯の上限は540。

        var daysRed = DailySnapshotTestBuilder.Sequence(150).ToList();
        daysRed[100] = DailySnapshotTestBuilder.WithPrice(
            daysRed[100], Item.Bread,
            daysRed[100].Prices[Item.Bread] with { SettledMedian = 541, SettledCount = 4, ExternalBuyPrice = 999_999 });

        var daysGreen = DailySnapshotTestBuilder.Sequence(150).ToList();
        daysGreen[100] = DailySnapshotTestBuilder.WithPrice(
            daysGreen[100], Item.Bread,
            daysGreen[100].Prices[Item.Bread] with { SettledMedian = 540, SettledCount = 4, ExternalBuyPrice = 999_999 });

        Assert.Equal(Verdict.Red, GetItem(Run(daysRed), "8-1a").Verdict);
        Assert.Equal(Verdict.Green, GetItem(Run(daysGreen), "8-1a").Verdict);
        Assert.Equal(54, floor);
    }

    /// <summary>#6。核心。2窓では偏差の枝が赤にならず、3窓で単調増加×2倍になったときだけ赤。</summary>
    [Fact]
    public void DispersionGrowthNeedsThreeWindows()
    {
        var twoWindows = DailySnapshotTestBuilder.Sequence(TransientDays + (WindowDays * 2)).ToList();
        ApplyAlternatingMedian(twoWindows, Item.Bread, TransientDays, TransientDays + (WindowDays * 2) - 1, 80, 120);

        var threeWindows = DailySnapshotTestBuilder.Sequence(TransientDays + (WindowDays * 3)).ToList();
        ApplyAlternatingMedian(threeWindows, Item.Bread, TransientDays, TransientDays + (WindowDays * 2) - 1, 80, 120);
        ApplyAlternatingMedian(
            threeWindows, Item.Bread, TransientDays + (WindowDays * 2), TransientDays + (WindowDays * 3) - 1, 60, 140);

        Assert.Equal(Verdict.Green, GetItem(Run(twoWindows), "8-1a").Verdict);
        Assert.Equal(Verdict.Red, GetItem(Run(threeWindows), "8-1a").Verdict);
    }

    /// <summary>#7。核心。相対平均絶対偏差‰は分母に窓の平均を含む(絶対平均偏差にしない)。</summary>
    [Fact]
    public void DispersionIsRelativeMeanAbsoluteDeviation()
    {
        var flat = DailySnapshotTestBuilder.Sequence(150).ToList();
        ApplyConstantMedian(flat, Item.Bread, TransientDays, TransientDays + WindowDays - 1, 100);

        var alternating = DailySnapshotTestBuilder.Sequence(150).ToList();
        ApplyAlternatingMedian(alternating, Item.Bread, TransientDays, TransientDays + WindowDays - 1, 80, 120);

        var flatEvidence = FindEvidence(GetItem(Run(flat), "8-1a"), "dispersionPermille:" + Item.Bread);
        var alternatingEvidence = FindEvidence(GetItem(Run(alternating), "8-1a"), "dispersionPermille:" + Item.Bread);

        Assert.Equal(0, flatEvidence.Value);
        Assert.Equal(200, alternatingEvidence.Value);
    }

    private static void ApplyConstantMedian(List<DailySnapshot> days, int itemId, int fromDay, int toDayInclusive, int median)
    {
        for (int day = fromDay; day <= toDayInclusive; day++)
        {
            var row = days[day].Prices[itemId] with { SettledMedian = median, SettledCount = 4 };
            days[day] = DailySnapshotTestBuilder.WithPrice(days[day], itemId, row);
        }
    }

    private static void ApplyAlternatingMedian(
        List<DailySnapshot> days, int itemId, int fromDay, int toDayInclusive, int low, int high)
    {
        for (int day = fromDay; day <= toDayInclusive; day++)
        {
            int median = (day % 2 == 0) ? low : high;
            var row = days[day].Prices[itemId] with { SettledMedian = median, SettledCount = 4 };
            days[day] = DailySnapshotTestBuilder.WithPrice(days[day], itemId, row);
        }
    }

    /// <summary>
    /// #8。核心。除外は「その日に出品した売り手が全員床かつ輸出」の日だけ。一部だけ該当する日は
    /// 除外されない(除外を <c>offer_at_floor_count == offer_count</c> で書くと、一部だけ該当する日も
    /// 除外され、有効日数が誤って減る)。
    /// </summary>
    [Fact]
    public void RigidityExcludesOnlyAllFloorExportDays()
    {
        int floor = DailySnapshotTestBuilder.Floor(Item.Beer);

        var allExcluded = DailySnapshotTestBuilder.Sequence(150).ToList();
        allExcluded[100] = DailySnapshotTestBuilder.WithPrice(
            allExcluded[100], Item.Beer,
            allExcluded[100].Prices[Item.Beer] with
            {
                SettledMedian = floor,
                SettledCount = 2,
                OfferCount = 2,
                OfferAtFloorCount = 2,
                OfferAtFloorWithExportCount = 2,
            });

        var partiallyExcluded = DailySnapshotTestBuilder.Sequence(150).ToList();
        partiallyExcluded[100] = DailySnapshotTestBuilder.WithPrice(
            partiallyExcluded[100], Item.Beer,
            partiallyExcluded[100].Prices[Item.Beer] with
            {
                SettledMedian = floor,
                SettledCount = 2,
                OfferCount = 2,
                OfferAtFloorCount = 2,
                OfferAtFloorWithExportCount = 1,
            });

        var allExcludedRange = FindEvidence(GetItem(Run(allExcluded), "8-1b"), "settledMedianRange:" + Item.Beer);
        var partiallyExcludedRange = FindEvidence(GetItem(Run(partiallyExcluded), "8-1b"), "settledMedianRange:" + Item.Beer);

        Assert.Equal(WindowDays - 1, allExcludedRange.Denominator);
        Assert.Equal(WindowDays, partiallyExcludedRange.Denominator);
    }

    /// <summary>#9。硬直はレンジとスイッチ率を「または」で結ぶ(独立に赤くなれる)。</summary>
    [Fact]
    public void RigidityReadsSwitchRateAndPriceRangeIndependently()
    {
        // レンジは広い(既定の変動)が、スイッチ率が全日49‰。
        var lowSwitch = DailySnapshotTestBuilder.Sequence(150).ToList();
        for (int day = TransientDays; day < lowSwitch.Count; day++)
        {
            lowSwitch[day] = DailySnapshotTestBuilder.WithTrades(
                lowSwitch[day], lowSwitch[day].Trades with { PartnerSwitchPermille = 49 });
        }

        Assert.Equal(Verdict.Red, GetItem(Run(lowSwitch), "8-1b").Verdict);

        // レンジが狭い(全品目・全日で床×2に固定)。スイッチ率は健全。
        var narrowRange = DailySnapshotTestBuilder.Sequence(150).ToList();
        for (int day = 0; day < narrowRange.Count; day++)
        {
            var prices = narrowRange[day].Prices.ToList();

            for (int itemId = 0; itemId < prices.Count; itemId++)
            {
                if (DailySnapshotTestBuilder.Definition.IsPrimaryItem(itemId))
                {
                    continue;
                }

                int floor = DailySnapshotTestBuilder.Floor(itemId);
                prices[itemId] = prices[itemId] with { SettledMedian = floor * 2, SettledCount = 4 };
            }

            narrowRange[day] = new DailySnapshot(
                narrowRange[day].Economy, prices, narrowRange[day].Districts, narrowRange[day].Households, narrowRange[day].Trades);
        }

        Assert.Equal(Verdict.Red, GetItem(Run(narrowRange), "8-1b").Verdict);
    }

    /// <summary>
    /// #10。核心。<c>partner_switch_permille == -1</c> の日は母数に入らず、-1しか無い窓は判定不能。
    /// </summary>
    [Fact]
    public void SwitchRateIgnoresUndefinedDays()
    {
        var days = DailySnapshotTestBuilder.Sequence(150).ToList();

        for (int day = 0; day < days.Count; day++)
        {
            days[day] = DailySnapshotTestBuilder.WithTrades(days[day], days[day].Trades with { PartnerSwitchPermille = -1 });
        }

        Assert.Equal(Verdict.Indeterminate, GetItem(Run(days), "8-1b").Verdict);
    }

    /// <summary>
    /// #40(レビューで追加)。TDD01 §5.2 の8-1bのスイッチ率は「partner_switch_permille &gt;= 0 の
    /// <b>全日</b>で &lt; 50‰」が赤の条件である。有効日のうち1日だけ49‰(閾値未満)で残りが
    /// 既定の500‰(健全)の窓は、全日条件を満たさないのでスイッチ率の枝では赤にならない
    /// ── 窓最小値(49‰)だけを見ると誤って赤になる。
    /// </summary>
    [Fact]
    public void SwitchRateBranchIsNotRedWhenOnlyOneDayIsBelowTheFloor()
    {
        var days = DailySnapshotTestBuilder.Sequence(150).ToList();

        days[100] = DailySnapshotTestBuilder.WithTrades(
            days[100], days[100].Trades with { PartnerSwitchPermille = 49 });

        var item = GetItem(Run(days), "8-1b");

        Assert.Equal(Verdict.Green, item.Verdict);

        var minEvidence = FindEvidence(item, "partnerSwitchMinPermille");
        var maxEvidence = FindEvidence(item, "partnerSwitchMaxPermille");

        Assert.Equal(49, minEvidence.Value);
        Assert.Equal(500, maxEvidence.Value); // 判定を決めたのは最大値のほう。
    }

    /// <summary>#11。輸入含有原価は年間の最悪季節(4季のmax)で評価する。</summary>
    [Fact]
    public void ImportContentCostUsesTheWorstSeason()
    {
        // 素材(item0、1次)の外部売値の季節係数を極端に偏らせ(冬だけ3700‰)、
        // 年平均や他季節では成立するが冬(最悪季節)だけ破れる組を作る。
        var definition = BuildImportContentCostDefinition(
            oreExternalSellBase: 10,
            oreSeasonPermille: new[] { 100, 100, 100, 3700 },
            toolExternalBuyPrice: 30);

        var days = HealthyMinimalSequence(definition, 150, exportQuantityPerDay: 1);
        var result = Run(days, definition);

        Assert.Equal(Verdict.Red, GetItem(result, "8-1c").Verdict);
    }

    /// <summary>#12。静的条件が成立していても、窓の <c>export_quantity</c> 合計が0なら8-1cは赤。</summary>
    [Fact]
    public void NoExportMakesTheConditionRed()
    {
        var definition = BuildImportContentCostDefinition(
            oreExternalSellBase: 10,
            oreSeasonPermille: new[] { 1000, 1000, 1000, 1000 },
            toolExternalBuyPrice: 50); // 37 相当まで上がらないので静的条件は成立する

        var withExport = HealthyMinimalSequence(definition, 150, exportQuantityPerDay: 1);
        var withoutExport = HealthyMinimalSequence(definition, 150, exportQuantityPerDay: 0);

        Assert.Equal(Verdict.Green, GetItem(Run(withExport, definition), "8-1c").Verdict);
        Assert.Equal(Verdict.Red, GetItem(Run(withoutExport, definition), "8-1c").Verdict);
    }

    /// <summary>#13。分母は「窓の日数 × definition.HouseholdCount」であり、households.csv の行数ではない。</summary>
    [Fact]
    public void DistressRatiosUseTheDefinitionHouseholdCount()
    {
        var days = DailySnapshotTestBuilder.Sequence(150).ToList();

        for (int day = 0; day < days.Count; day++)
        {
            days[day] = DailySnapshotTestBuilder.WithHouseholds(days[day], days[day].Households.Take(5).ToList());
        }

        var evidence = FindEvidence(GetItem(Run(days), "8-2a"), "bankruptHouseholdRatio");

        Assert.Equal((long)WindowDays * DailySnapshotTestBuilder.Definition.HouseholdCount, evidence.Denominator);
    }

    /// <summary>#14。核心。8-2aと8-2bは別々の閾値(100‰ / 250‰)を持つ。</summary>
    [Fact]
    public void BankruptAndInputBlockedHaveSeparateThresholds()
    {
        var days = DailySnapshotTestBuilder.Sequence(150).ToList();

        for (int day = TransientDays; day < TransientDays + 60; day++)
        {
            days[day] = DailySnapshotTestBuilder.WithEconomy(
                days[day], days[day].Economy with { BankruptHouseholds = 1, InputBlockedHouseholds = 1 });
        }

        for (int day = TransientDays + 60; day < TransientDays + WindowDays; day++)
        {
            days[day] = DailySnapshotTestBuilder.WithEconomy(
                days[day], days[day].Economy with { BankruptHouseholds = 2, InputBlockedHouseholds = 2 });
        }

        var result = Run(days);

        Assert.Equal(Verdict.Red, GetItem(result, "8-2a").Verdict);
        Assert.Equal(Verdict.Green, GetItem(result, "8-2b").Verdict);
        Assert.Equal(150L, FindEvidence(GetItem(result, "8-2a"), "bankruptHouseholdRatio").Value);
    }

    /// <summary>#15。核心。醸造2戸だけが全日停止でも8-3が赤になる(職業別に判定する)。</summary>
    [Fact]
    public void ProductionStopIsJudgedPerOccupation()
    {
        var days = DailySnapshotTestBuilder.Sequence(150).ToList();

        for (int day = 0; day < days.Count; day++)
        {
            var households = days[day].Households
                .Select(h => (int)h.Occupation == (int)Occupation.Brewer ? h with { ProductionRuns = 0 } : h)
                .ToList();
            days[day] = DailySnapshotTestBuilder.WithHouseholds(days[day], households);
        }

        Assert.Equal(Verdict.Red, GetItem(Run(days), "8-3").Verdict);
    }

    /// <summary>#16。醸造が閾値を超えていない緑の窓でも、根拠に醸造の停止割合が出る。</summary>
    [Fact]
    public void BrewerEvidenceIsAlwaysPresent()
    {
        var result = Run(DailySnapshotTestBuilder.Sequence(150));
        var item = GetItem(result, "8-3");

        Assert.Equal(Verdict.Green, item.Verdict);
        Assert.Contains(item.Evidence, e => e.Name == "productionStoppedRatio:" + (int)Occupation.Brewer);
    }

    /// <summary>#17。1区画しか無い日は有効日から外れる(差0として数えない)。</summary>
    [Fact]
    public void DistrictSpreadNeedsTwoDistricts()
    {
        var days = DailySnapshotTestBuilder.Sequence(150).ToList();

        // 窓の120日のうち40日を1区画だけの日にする(残り80日は本来の2区画。80 >= 30なので
        // 判定不能の下限には掛からない ── ここで見たいのは除外の正しさだけである)。
        for (int day = TransientDays; day < TransientDays + 40; day++)
        {
            var districts = days[day].Districts.Where(d => !(d.ItemId == Item.Bread && d.DistrictId == 1)).ToList();
            days[day] = DailySnapshotTestBuilder.WithDistricts(days[day], districts);
        }

        var evidence = FindEvidence(GetItem(Run(days), "8-4"), "districtSpreadAveragePermille:" + Item.Bread);

        Assert.Equal(80, evidence.Denominator);
    }

    /// <summary>#18。核心。担い手世帯が1戸になった窓は8-4が判定不能になる(差0でも赤にしない)。</summary>
    [Fact]
    public void DistrictSpreadIsIndeterminateWithOneSeller()
    {
        var days = DailySnapshotTestBuilder.Sequence(150).ToList();

        for (int day = 0; day < days.Count; day++)
        {
            var households = days[day].Households.ToList();

            // Bread(パン屋)の2戸のうち1戸だけ出力品目を変えて担い手を1戸に減らす。
            var bakerHouseholds = households.Where(h => (int)h.Occupation == (int)Occupation.Baker).ToList();
            var target = bakerHouseholds[1];
            int index = households.IndexOf(target);
            households[index] = target with { OutputItemId = Item.Flour };

            days[day] = DailySnapshotTestBuilder.WithHouseholds(days[day], households);
        }

        Assert.Equal(Verdict.Indeterminate, GetItem(Run(days), "8-4").Verdict);
    }

    /// <summary>#19。工具(Item.Tools)の区画差は8-4から除かれる(差0でも緑)。</summary>
    [Fact]
    public void DistrictSpreadExcludesTools()
    {
        var days = DailySnapshotTestBuilder.Sequence(150).ToList();
        int toolsFloor = DailySnapshotTestBuilder.Floor(Item.Tools);

        for (int day = 0; day < days.Count; day++)
        {
            var districts = days[day].Districts
                .Select(d => d.ItemId == Item.Tools ? d with { SettledMedian = toolsFloor * 2 } : d)
                .ToList();
            days[day] = DailySnapshotTestBuilder.WithDistricts(days[day], districts);
        }

        Assert.Equal(Verdict.Green, GetItem(Run(days), "8-4").Verdict);
    }

    /// <summary>
    /// #41(レビューで追加)。8-1bのレンジの根拠は床に対する‰であり、同じ根拠の閾値(40‰)と
    /// 「値 &lt; 閾値 → 赤」の向きで素直に読める(貨幣単位の絶対値と40という閾値を並べると
    /// 逆向きに読めてしまっていた)。
    /// </summary>
    [Fact]
    public void RigidityRangeEvidenceIsExpressedInPermille()
    {
        int floor = DailySnapshotTestBuilder.Floor(Item.Bread);
        var days = DailySnapshotTestBuilder.Sequence(150).ToList();

        // 全日同一値に固定してレンジ0にする(硬直の赤条件)。
        for (int day = 0; day < days.Count; day++)
        {
            days[day] = DailySnapshotTestBuilder.WithPrice(
                days[day], Item.Bread, days[day].Prices[Item.Bread] with { SettledMedian = floor * 2, SettledCount = 4 });
        }

        var evidence = FindEvidence(GetItem(Run(days), "8-1b"), "settledMedianRange:" + Item.Bread);

        Assert.Equal(VerificationThresholds.RigidityRangePermille, evidence.Threshold);
        Assert.True(evidence.Value < evidence.Threshold); // ‰で読むと「値<閾値→赤」の向きになる。
        Assert.Equal(0, evidence.Value);
    }

    /// <summary>
    /// #42(レビューで追加)。8-4の区画差の窓平均の根拠は床に対する‰であり、同じ根拠の閾値
    /// (20‰)と「値 &lt; 閾値 → 赤」の向きで素直に読める(名前は Permille なのに Value が
    /// 貨幣単位の絶対値だった)。
    /// </summary>
    [Fact]
    public void DistrictSpreadEvidenceIsExpressedInPermille()
    {
        int floor = DailySnapshotTestBuilder.Floor(Item.Bread);
        var days = DailySnapshotTestBuilder.Sequence(150).ToList();

        // 全区画・全日同一値に固定して区画差0にする(8-4の赤条件)。
        for (int day = 0; day < days.Count; day++)
        {
            var districts = days[day].Districts
                .Select(d => d.ItemId == Item.Bread ? d with { SettledMedian = floor * 2 } : d)
                .ToList();
            days[day] = DailySnapshotTestBuilder.WithDistricts(days[day], districts);
        }

        var evidence = FindEvidence(GetItem(Run(days), "8-4"), "districtSpreadAveragePermille:" + Item.Bread);

        Assert.Equal(VerificationThresholds.DistrictSpreadPermille, evidence.Threshold);
        Assert.True(evidence.Value < evidence.Threshold); // ‰で読むと「値<閾値→赤」の向きになる。
        Assert.Equal(0, evidence.Value);
    }

    /// <summary>#20。day0の床割れも即赤。FirstRedDayは0。</summary>
    [Fact]
    public void FloorBreachIsRedOnTheFirstDay()
    {
        var days = DailySnapshotTestBuilder.Sequence(10).ToList();
        int floor = DailySnapshotTestBuilder.Floor(Item.Flour);

        days[0] = DailySnapshotTestBuilder.WithPrice(
            days[0], Item.Flour, days[0].Prices[Item.Flour] with { OfferMin = floor - 1, OfferCount = 1 });

        var item = GetItem(Run(days), "8-5a");

        Assert.Equal(Verdict.Red, item.Verdict);
        Assert.Equal(0, item.FirstRedDay);
    }

    /// <summary>#21。核心。連続日数は窓をまたいで数える(境界をまたぐ30日連続で赤、29日では緑)。</summary>
    [Fact]
    public void BandExceededCountsAcrossWindowBoundaries()
    {
        Assert.Equal(Verdict.Red, RunBandExceededScenario(streakLength: 30).Verdict);
        Assert.Equal(Verdict.Green, RunBandExceededScenario(streakLength: 29).Verdict);
    }

    private static VerificationItemResult RunBandExceededScenario(int streakLength)
    {
        // 窓境界(day149/150)をまたぐ位置に連続日数を置く。
        int startDay = 149 - (streakLength / 2);
        int totalDays = TransientDays + (WindowDays * 2);
        var days = DailySnapshotTestBuilder.Sequence(totalDays).ToList();
        int ceiling = DailySnapshotTestBuilder.Ceiling(Item.Flour);

        // 既定値の周期変動が天井をまたいで独自の連続日数を作らないよう、まず天井未満の
        // 安全な値で品目全体を均す(既定の健全パターンは床×2 = 天井を基準にしているため)。
        NormalizeBelowCeiling(days, Item.Flour, ceiling);

        for (int day = startDay; day < startDay + streakLength; day++)
        {
            days[day] = DailySnapshotTestBuilder.WithPrice(
                days[day], Item.Flour, days[day].Prices[Item.Flour] with { SettledMedian = ceiling + 1, SettledCount = 4 });
        }

        return GetItem(Run(days), "8-5b");
    }

    private static void NormalizeBelowCeiling(List<DailySnapshot> days, int itemId, int ceiling)
    {
        for (int day = 0; day < days.Count; day++)
        {
            days[day] = DailySnapshotTestBuilder.WithPrice(
                days[day], itemId, days[day].Prices[itemId] with { SettledMedian = ceiling - 1, SettledCount = 4 });
        }
    }

    /// <summary>#22。約定0の日を挟むと連続が切れる。</summary>
    [Fact]
    public void BandExceededIsBrokenByDaysWithoutSettlement()
    {
        var days = DailySnapshotTestBuilder.Sequence(TransientDays + WindowDays).ToList();
        int ceiling = DailySnapshotTestBuilder.Ceiling(Item.Flour);

        NormalizeBelowCeiling(days, Item.Flour, ceiling);

        for (int day = TransientDays; day < TransientDays + 29; day++)
        {
            days[day] = DailySnapshotTestBuilder.WithPrice(
                days[day], Item.Flour, days[day].Prices[Item.Flour] with { SettledMedian = ceiling + 1, SettledCount = 4 });
        }

        // 29日目の直後に約定0の日を挟む。
        days[TransientDays + 29] = DailySnapshotTestBuilder.WithPrice(
            days[TransientDays + 29], Item.Flour,
            days[TransientDays + 29].Prices[Item.Flour] with { SettledCount = 0, SettledMedian = -1 });

        // break の後も少しだけ帯超えを続けるが、30日には遠く届かない(このテストが見たいのは
        // 「29日 + break」で切れることだけである。ここを WindowDays まで伸ばすと、break 後の
        // 90日だけで別の30日連続ができてしまい、テストの意図と無関係に赤くなる)。
        for (int day = TransientDays + 30; day < TransientDays + 35; day++)
        {
            days[day] = DailySnapshotTestBuilder.WithPrice(
                days[day], Item.Flour, days[day].Prices[Item.Flour] with { SettledMedian = ceiling + 1, SettledCount = 4 });
        }

        Assert.Equal(Verdict.Green, GetItem(Run(days), "8-5b").Verdict);
    }

    /// <summary>
    /// #23。核心。天井が1日も試されていない窓は判定不能。試された日が1日でもあり窓口購入0なら赤。
    /// </summary>
    [Fact]
    public void WindowPurchaseIsIndeterminateWhenTheCeilingIsNeverTested()
    {
        var neverTested = DailySnapshotTestBuilder.Sequence(150).ToList();

        for (int day = 0; day < neverTested.Count; day++)
        {
            var prices = neverTested[day].Prices.ToList();

            for (int itemId = 0; itemId < prices.Count; itemId++)
            {
                if (DailySnapshotTestBuilder.Definition.IsPrimaryItem(itemId))
                {
                    continue;
                }

                int ceiling = DailySnapshotTestBuilder.Ceiling(itemId);
                prices[itemId] = prices[itemId] with { OfferMax = ceiling, WindowSettledCount = 0 };
            }

            neverTested[day] = new DailySnapshot(
                neverTested[day].Economy, prices, neverTested[day].Districts, neverTested[day].Households, neverTested[day].Trades);
        }

        Assert.Equal(Verdict.Indeterminate, GetItem(Run(neverTested), "8-5c").Verdict);

        var testedOnce = DailySnapshotTestBuilder.Sequence(150).ToList();

        for (int day = 0; day < testedOnce.Count; day++)
        {
            var prices = testedOnce[day].Prices.ToList();

            for (int itemId = 0; itemId < prices.Count; itemId++)
            {
                if (DailySnapshotTestBuilder.Definition.IsPrimaryItem(itemId))
                {
                    continue;
                }

                int ceiling = DailySnapshotTestBuilder.Ceiling(itemId);
                prices[itemId] = prices[itemId] with { OfferMax = ceiling, WindowSettledCount = 0 };
            }

            testedOnce[day] = new DailySnapshot(
                testedOnce[day].Economy, prices, testedOnce[day].Districts, testedOnce[day].Households, testedOnce[day].Trades);
        }

        int flourCeiling = DailySnapshotTestBuilder.Ceiling(Item.Flour);
        var flourRow = testedOnce[100].Prices[Item.Flour] with { OfferMax = flourCeiling + 1, WindowSettledCount = 0 };
        testedOnce[100] = DailySnapshotTestBuilder.WithPrice(testedOnce[100], Item.Flour, flourRow);

        Assert.Equal(Verdict.Red, GetItem(Run(testedOnce), "8-5c").Verdict);
    }

    /// <summary>#24。核心。初期貨幣総量は definition から取る(day0の実測値ではない)。</summary>
    [Fact]
    public void MoneyBoundUsesTheDefinitionInitialTotal()
    {
        var days = DailySnapshotTestBuilder.Sequence(10).ToList();
        int initial = DailySnapshotTestBuilder.InitialTotalMoney;

        days[0] = DailySnapshotTestBuilder.WithEconomy(
            days[0], days[0].Economy with { MoneyTotal = (initial * 40) / 100 });

        var item = GetItem(Run(days), "8-6");

        Assert.Equal(Verdict.Red, item.Verdict);
        Assert.Equal(0, item.FirstRedDay);
    }

    /// <summary>#25。窓末が2窓連続で下がるだけなら緑、3窓連続なら赤。</summary>
    [Fact]
    public void MoneyDecliningNeedsThreeConsecutiveWindows()
    {
        Assert.Equal(Verdict.Green, RunMoneyDeclineScenario(new[] { 20000, 19000, 18000 }).Verdict);
        Assert.Equal(Verdict.Red, RunMoneyDeclineScenario(new[] { 20000, 19000, 18000, 17000 }).Verdict);
    }

    private static VerificationItemResult RunMoneyDeclineScenario(int[] windowEndMoney)
    {
        int totalDays = TransientDays + (WindowDays * windowEndMoney.Length);
        var days = DailySnapshotTestBuilder.Sequence(totalDays).ToList();

        for (int w = 0; w < windowEndMoney.Length; w++)
        {
            int windowStart = TransientDays + (w * WindowDays);
            int windowEnd = windowStart + WindowDays - 1;

            for (int day = windowStart; day <= windowEnd; day++)
            {
                days[day] = DailySnapshotTestBuilder.WithEconomy(
                    days[day], days[day].Economy with { MoneyTotal = windowEndMoney[w] });
            }
        }

        return GetItem(Run(days), "8-6");
    }

    /// <summary>#26。帯を出ない窓はcalibration、出て戻り輸出が発火した窓はmechanism、それ以外はunknown。</summary>
    [Fact]
    public void BoundedByDistinguishesCalibrationFromMechanism()
    {
        int initial = DailySnapshotTestBuilder.InitialTotalMoney;

        // (a) calibration: 全日 initial のまま。
        var calibration = DailySnapshotTestBuilder.Sequence(150);
        var calibrationEvidence = FindEvidence(GetItem(Run(calibration), "8-6"), "boundedBy");
        Assert.Equal(0, calibrationEvidence.Value);

        // (b) mechanism: 1日だけ外へ出て(輸出は既定で発生している)、翌日以降戻る。
        var mechanism = DailySnapshotTestBuilder.Sequence(150).ToList();
        mechanism[101] = DailySnapshotTestBuilder.WithEconomy(
            mechanism[101], mechanism[101].Economy with { MoneyTotal = initial * 2 });
        var mechanismEvidence = FindEvidence(GetItem(Run(mechanism), "8-6"), "boundedBy");
        Assert.Equal(1, mechanismEvidence.Value);

        // (c) unknown: 外へ出たまま窓の最後まで戻らない。
        var unknown = DailySnapshotTestBuilder.Sequence(150).ToList();
        for (int day = 101; day < unknown.Count; day++)
        {
            unknown[day] = DailySnapshotTestBuilder.WithEconomy(
                unknown[day], unknown[day].Economy with { MoneyTotal = initial * 2 });
        }
        var unknownEvidence = FindEvidence(GetItem(Run(unknown), "8-6"), "boundedBy");
        Assert.Equal(2, unknownEvidence.Value);
    }

    /// <summary>
    /// #43(レビューで追加)。日次の帯(day0、過渡期)が窓より先に8-6を赤くしても、窓ぶんの
    /// 根拠(<c>boundedBy</c> を含む6件)が根拠から落ちない。着手時点の経済ではほぼ全シードが
    /// この経路を通るため(day0〜1で貨幣が枯れる)、落ちると <c>boundedBy</c> が summary.json
    /// に一度も現れなくなる。
    /// </summary>
    [Fact]
    public void MoneyBoundedKeepsWindowEvidenceWhenDayLevelDecides()
    {
        var days = DailySnapshotTestBuilder.Sequence(150).ToList();
        int initial = DailySnapshotTestBuilder.InitialTotalMoney;

        // day0(過渡期)で貨幣総量が帯を大きく外れる。窓(day30〜149)は健全なまま1つ閉じる。
        days[0] = DailySnapshotTestBuilder.WithEconomy(
            days[0], days[0].Economy with { MoneyTotal = initial / 10 });

        var item = GetItem(Run(days), "8-6");

        Assert.Equal(Verdict.Red, item.Verdict);
        Assert.Equal(0, item.FirstRedDay);
        Assert.Contains(item.Evidence, e => e.Name == "boundedBy");
        Assert.Contains(item.Evidence, e => e.Name == "moneyTotalMin");
        Assert.Contains(item.Evidence, e => e.Name == "moneyTotalMax");
        Assert.Contains(item.Evidence, e => e.Name == "moneyTotalWindowEnd");
        Assert.Contains(item.Evidence, e => e.Name == "exportValueSum");
        Assert.Contains(item.Evidence, e => e.Name == "importValueSum");
    }

    /// <summary>#27。核心。割合は窓合計を先に足してから割る(日ごとに割って平均しない)。</summary>
    [Fact]
    public void UnknownPriceRatioSumsBeforeDividing()
    {
        var days = DailySnapshotTestBuilder.Sequence(150).ToList();

        for (int day = TransientDays; day < TransientDays + WindowDays; day++)
        {
            days[day] = DailySnapshotTestBuilder.WithEconomy(
                days[day], days[day].Economy with { DemandLines = 0, DemandLinesWithoutKnownPrice = 0 });
        }

        days[100] = DailySnapshotTestBuilder.WithEconomy(
            days[100], days[100].Economy with { DemandLines = 1, DemandLinesWithoutKnownPrice = 1 });
        days[101] = DailySnapshotTestBuilder.WithEconomy(
            days[101], days[101].Economy with { DemandLines = 99, DemandLinesWithoutKnownPrice = 0 });

        var evidence = FindEvidence(GetItem(Run(days), "8-7"), "unknownPriceLineRatio");

        Assert.Equal(10, evidence.Value);
    }

    /// <summary>
    /// #28。核心。日単位の条件(8-5a)はその日、窓単位の条件(8-2a)は窓の末日がFirstRedDayになる。
    /// 2つの赤い窓があっても最初の窓の値のまま上書きされない。
    /// </summary>
    [Fact]
    public void FirstRedDayIsTheDayTheJudgementLands()
    {
        var days = DailySnapshotTestBuilder.Sequence(TransientDays + (WindowDays * 2)).ToList();

        for (int day = TransientDays; day < days.Count; day++)
        {
            days[day] = DailySnapshotTestBuilder.WithEconomy(
                days[day], days[day].Economy with { BankruptHouseholds = 2 }); // 両窓とも赤(ratio 200)
        }

        var item = GetItem(Run(days), "8-2a");

        Assert.Equal(Verdict.Red, item.Verdict);
        Assert.Equal(149, item.FirstRedDay); // 最初の窓の末日。2つ目の窓の末日(269)に上書きされない
    }

    /// <summary>#29。窓1で赤、窓2で判定不能になる走行で、根拠の値が窓1のものになる。</summary>
    [Fact]
    public void EvidenceComesFromTheDecidingWindow()
    {
        var days = DailySnapshotTestBuilder.Sequence(TransientDays + (WindowDays * 2)).ToList();

        for (int day = TransientDays; day < TransientDays + WindowDays; day++)
        {
            days[day] = DailySnapshotTestBuilder.WithEconomy(days[day], days[day].Economy with { BankruptHouseholds = 2 });
        }

        // 2つ目の窓は健全(赤ではロックされているのでこの値は評価に影響しないはず)。
        for (int day = TransientDays + WindowDays; day < days.Count; day++)
        {
            days[day] = DailySnapshotTestBuilder.WithEconomy(days[day], days[day].Economy with { BankruptHouseholds = 0 });
        }

        var evidence = FindEvidence(GetItem(Run(days), "8-2a"), "bankruptHouseholdRatio");

        Assert.Equal(200, evidence.Value);
    }

    /// <summary>#36。day が飛んだ/戻った <see cref="DailySnapshot"/> を流すと <see cref="ArgumentException"/>。</summary>
    [Fact]
    public void AccumulatorRejectsOutOfOrderDays()
    {
        var accumulator = new VerificationAccumulator(DailySnapshotTestBuilder.Definition);
        var day0 = DailySnapshotTestBuilder.Healthy(0);
        var day2 = DailySnapshotTestBuilder.Healthy(2);

        accumulator.Write(in day0);

        Assert.Throws<ArgumentException>(() => accumulator.Write(in day2));
    }

    /// <summary>#37。<see cref="VerificationAccumulator.Build"/> を2回呼ぶと <see cref="InvalidOperationException"/>。</summary>
    [Fact]
    public void AccumulatorRejectsASecondBuild()
    {
        var accumulator = new VerificationAccumulator(DailySnapshotTestBuilder.Definition);
        var day0 = DailySnapshotTestBuilder.Healthy(0);

        accumulator.Write(in day0);
        accumulator.Build(1);

        Assert.Throws<InvalidOperationException>(() => accumulator.Build(1));
    }

    // ------------------------------------------------------------------
    // #11・#12 専用: 輸入含有原価の季節評価を試すための最小 WorldDefinition
    // ------------------------------------------------------------------

    private static WorldDefinition BuildImportContentCostDefinition(
        int oreExternalSellBase, int[] oreSeasonPermille, int toolExternalBuyPrice)
    {
        var recipes = new[]
        {
            new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = 1, Quantity = 1 } },
                inputs: new[] { new ItemQuantity { ItemId = 0, Quantity = 1 } },
                laborPermille: 1000),
        };

        return new WorldDefinition(
            itemCount: 2,
            householdsPerOccupation: 9,
            recipes: recipes,
            initialLiquidFunds: 1000,
            initialAcquisitionCost: new[] { 1, 1 },
            initialHouseholdInventory: new[] { 0, 0 },
            initialWorkshopInputDays: 1,
            initialToolStock: 1,
            initialSkillPermilleByRank: new[] { 500, 500, 500 },
            laborPermilleByRank: new[] { 1000, 800, 300 },
            dailyConsumptionPerNpcByRank: new[] { new[] { 0, 0 }, new[] { 0, 0 }, new[] { 0, 0 } },
            firewoodConsumptionSeasonPermille: new[] { 1000, 1000, 1000, 1000 },
            minimumMarginPermille: 0,
            observationRetentionDays: 1,
            necessityTargetStockDays: new[] { 0, 0 },
            preferenceTargetStockDays: new[] { 0, 0 },
            toolTargetStockPermille: 1,
            rankCoefficientPermille: new[] { 1000, 600, 200 },
            tolerancePermille: 1000,
            opportunityCostBaseByOccupation: new[] { 20 },
            travelHoursPerDistrict: 1,
            acquisitionCostSmoothingPermille: 250,
            externalSellPriceBase: new[] { oreExternalSellBase, 0 },
            externalSellPriceSeasonPermille: new[] { oreSeasonPermille, new[] { 1000, 1000, 1000, 1000 } },
            externalBuyPrice: new[] { 0, toolExternalBuyPrice },
            inputBufferDays: 1,
            shipmentDays: 1,
            toolLifeLaborDays: 1,
            equipmentPermilleWithoutTools: 0,
            disposableHours: 1,
            trustDiscountPermille: 0,
            tradeMarginPermille: 1000);
    }

    private static SeedVerification Run(IReadOnlyList<DailySnapshot> days, WorldDefinition definition, long seed = 1)
    {
        var accumulator = new VerificationAccumulator(definition);

        foreach (var day in days)
        {
            accumulator.Write(in day);
        }

        return accumulator.Build(seed);
    }

    private static IReadOnlyList<DailySnapshot> HealthyMinimalSequence(
        WorldDefinition definition, int totalDays, int exportQuantityPerDay)
    {
        var list = new List<DailySnapshot>(totalDays);

        for (long day = 0; day < totalDays; day++)
        {
            var economy = new EconomyRow(
                Day: day, Season: 0, MoneyTotal: definition.InitialLiquidFunds * definition.HouseholdCount,
                BankruptHouseholds: 0, NecessityBlockedHouseholds: 0, NecessityBlockedCount: 0,
                InputBlockedHouseholds: 0, ExportValue: 0, ExportQuantity: exportQuantityPerDay,
                ImportValue: 0, ImportQuantity: 0, SellerDays: 0, SellerDaysWithoutReference: 0,
                SellerDaysCoefficientCapped: 0, SellerDaysAtFloor: 0, SellerDaysAtFloorWithExport: 0,
                ProductionStoppedHouseholds: 0, ProductionRunsTotal: 0, DemandLines: 0,
                DemandLinesWithoutKnownPrice: 0, HouseholdsWithoutKnownPrice: 0, ProfitTotal: 0);

            var prices = new List<PriceRow>
            {
                new(day, 0, -1, -1, -1, 0, 0, 0, 0, 0, 0, -1, -1, -1, 0, 0, 0, -1, definition.ExternalSellPrice(0, Season.Spring)),
                new(day, 1, -1, -1, -1, 0, 0, 0, 0, 0, 0, -1, -1, -1, 0, 0, 0, definition.ExternalBuyPrice(1), definition.ExternalSellPrice(1, Season.Spring)),
            };

            var households = new List<HouseholdRow>();

            for (int i = 0; i < definition.HouseholdCount; i++)
            {
                households.Add(new HouseholdRow(
                    day, i, 0, 0, 1, definition.InitialLiquidFunds, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
            }

            var trades = new TradesRow(day, 0, 0, 0, 0, -1, -1, 0, 0);

            list.Add(new DailySnapshot(economy, prices, Array.Empty<DistrictRow>(), households, trades));
        }

        return list;
    }
}
