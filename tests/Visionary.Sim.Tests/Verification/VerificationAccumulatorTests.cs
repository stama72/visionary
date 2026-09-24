using Visionary.Sim.Metrics;
using Visionary.Sim.Time;
using Visionary.Sim.Verification;

namespace Visionary.Sim.Tests.Verification;

/// <summary>
/// <see cref="VerificationAccumulator"/> の検査(W2-21 タスク仕様「落ちるべき条件」#1〜#29・#36・#37・
/// 別表(フェーズ1 の訂正)#44〜#48・6'・1'・4'・5'・別表(レビューで追加)#40〜#43・
/// 別表(レビュー2巡目で追加)#49・#50・別表(レビュー3巡目で追加)#51〜#54・#56)。
/// </summary>
public sealed class VerificationAccumulatorTests
{
    private const int TransientDays = VerificationThresholds.TransientDays; // 30
    private const int WindowDays = VerificationThresholds.WindowDays;      // 120

    /// <summary>
    /// 過渡期30日 + 窓3つ(TDD01 §5.2「390日に満たない走行では8-1aが恒久的に判定不能」)。
    /// 8-1aを読む #1・#4・#5 はこの長さ以上でないと、偏差の枝が「基準窓から3窓未満」で
    /// 判定不能になり、項目の判定不能が帯の枝の結果を隠してしまう(別表(フェーズ1の訂正)#1'・4'・5')。
    /// </summary>
    private const int ThreeWindowDays = TransientDays + (WindowDays * 3); // 390

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

    /// <summary>
    /// #1(前半。別表(フェーズ1の訂正)#1' により390日以上へ訂正。検証内容は変えない)。
    /// 過渡期の帯超えは8-1aに影響しない(緑)。窓内の帯超えは赤になる。
    /// </summary>
    [Fact]
    public void WindowsStartAfterTheTransient_TransientViolationIsIgnored()
    {
        var days = DailySnapshotTestBuilder.Sequence(ThreeWindowDays).ToList();

        // day5(過渡期)にBreadの帯を大きく超える値を置く。
        var badPrice = days[5].Prices[Item.Bread] with { SettledMedian = 10000, SettledCount = 4 };
        days[5] = DailySnapshotTestBuilder.WithPrice(days[5], Item.Bread, badPrice);

        var result = Run(days);

        Assert.Equal(Verdict.Green, GetItem(result, "8-1a").Verdict);
    }

    /// <summary>
    /// #1(後半。別表(フェーズ1の訂正)#1' により390日以上へ訂正。検証内容は変えない)。
    /// 窓の中(day30以降)に置くと赤になる。
    /// </summary>
    [Fact]
    public void WindowsStartAfterTheTransient_WindowViolationIsRed()
    {
        var days = DailySnapshotTestBuilder.Sequence(ThreeWindowDays).ToList();

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

    /// <summary>
    /// 51(別表(レビュー3巡目で追加)#51)。149日の走行(窓が1つも取れない)では、
    /// 日次の帯(8-6)が一度も破れていなくても「連続3窓」の枝を一度も評価できていないので
    /// 8-6は判定不能になる(緑にならない。TDD01 §5.2「窓が1つも取れない走行では、窓を使う
    /// 項目はすべて判定不能」。#2 が8-2aで確かめている規則を8-6にも適用する)。day0に帯を
    /// 割る走行なら149日でも赤になる(赤 &gt; 判定不能。全日の帯チェックは窓を使わない)。
    /// <i>この実装ミスで落ちる</i>: <see cref="VerificationAccumulator.ResolveMoneyBounded"/> が
    /// <c>firstRedDay == -1</c> だけを見て緑を返していた(窓が0でも緑になっていた)。
    /// </summary>
    [Fact]
    public void MoneyBoundedIsIndeterminateWithoutWindows()
    {
        var healthy = DailySnapshotTestBuilder.Sequence(TransientDays + WindowDays - 1); // 149日
        var healthyResult = Run(healthy);

        Assert.Equal(0, healthyResult.WindowCount);
        Assert.Equal(Verdict.Indeterminate, GetItem(healthyResult, "8-6").Verdict);

        var days = DailySnapshotTestBuilder.Sequence(TransientDays + WindowDays - 1).ToList();
        int initial = DailySnapshotTestBuilder.InitialTotalMoney;
        days[0] = DailySnapshotTestBuilder.WithEconomy(days[0], days[0].Economy with { MoneyTotal = initial / 10 });

        var redItem = GetItem(Run(days), "8-6");

        Assert.Equal(Verdict.Red, redItem.Verdict);
        Assert.Equal(0, redItem.FirstRedDay);
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

    /// <summary>
    /// #4(別表(フェーズ1の訂正)#4' により390日以上へ訂正。検証内容は変えない)。
    /// 有効日が29日の窓は判定不能、30日なら判定が出る。
    /// </summary>
    [Fact]
    public void ValidDayFloorIsEnforced()
    {
        var days29 = DailySnapshotTestBuilder.Sequence(ThreeWindowDays).ToList();
        ZeroOutSettledCount(days29, Item.Beer, TransientDays, TransientDays + 90); // 91日を殺す→29日だけ有効

        var days30 = DailySnapshotTestBuilder.Sequence(ThreeWindowDays).ToList();
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
    /// #5(別表(フェーズ1の訂正)#5' により390日以上へ訂正。検証内容は変えない)。核心。基準は
    /// <see cref="WorldDefinition.ExternalBuyPrice"/> であり、<see cref="PriceRow.ExternalBuyPrice"/>
    /// を読むと同じ定数どうしの比較になる ── テストは <see cref="PriceRow"/> 側に誤った床を入れる。
    /// </summary>
    [Fact]
    public void DivergenceUsesTheFloorAsTheBase()
    {
        int floor = DailySnapshotTestBuilder.Floor(Item.Bread); // 54。帯の上限は540。

        var daysRed = DailySnapshotTestBuilder.Sequence(ThreeWindowDays).ToList();
        daysRed[100] = DailySnapshotTestBuilder.WithPrice(
            daysRed[100], Item.Bread,
            daysRed[100].Prices[Item.Bread] with { SettledMedian = 541, SettledCount = 4, ExternalBuyPrice = 999_999 });

        var daysGreen = DailySnapshotTestBuilder.Sequence(ThreeWindowDays).ToList();
        daysGreen[100] = DailySnapshotTestBuilder.WithPrice(
            daysGreen[100], Item.Bread,
            daysGreen[100].Prices[Item.Bread] with { SettledMedian = 540, SettledCount = 4, ExternalBuyPrice = 999_999 });

        Assert.Equal(Verdict.Red, GetItem(Run(daysRed), "8-1a").Verdict);
        Assert.Equal(Verdict.Green, GetItem(Run(daysGreen), "8-1a").Verdict);
        Assert.Equal(54, floor);
    }

    /// <summary>
    /// 6'(別表(フェーズ1の訂正)。上の #6 を訂正)。「3窓」は走行の窓数ではなく基準窓(偏差‰ が
    /// 0でない最初の窓)から最後の窓までの窓数である(TDD01 §5.2)。
    /// </summary>
    /// <remarks>
    /// <b>タスク仕様 別表(フェーズ1の訂正)#6' の具体的な数値列(4窓の例 <c>0, 0, 300, 700</c>)は
    /// TDD01 §5.2 の一般規則と矛盾する ── 報告事項(止まって報告)。</b>
    /// <c>0, 0, 300, 700</c> は最初の非ゼロが3番目の窓(値300)なので、基準窓から最後の窓(4番目)
    /// までは2窓しかない(TDD01 §5.2「基準窓から最後の窓まで」は基準窓を含む区間の窓数)。
    /// これは「基準窓以降2窓」の5窓ケース(<c>0, 0, 0, 300, 700</c>)と全く同じ形であり、
    /// タスク仕様が主張する「基準窓以降3窓」にはならない(判定不能になるはずで、「判定でき」とは
    /// 矛盾する)。§5.2 自身にはこの具体例が無く、一般規則(基準窓から最後の窓までの窓数 ≥ 3)は
    /// 一意で計算可能なので、このテストは一般規則に忠実な数値列(基準窓の直後に2窓を足して
    /// ちょうど3窓にする)で書く。判定不能側(5窓・基準窓以降2窓)はタスク仕様の数値列のまま
    /// (こちらは一般規則と矛盾しない)。
    /// </remarks>
    [Fact]
    public void DispersionGrowthNeedsThreeWindows()
    {
        // 決定できる側: 窓の偏差‰ が [0, 300, 300, 600] (4窓・基準窓=2番目・基準窓から最後まで3窓)。
        // 基準窓(300)から最後の窓(600)まで単調非減少、600 >= 100(下限)、600 >= 300×2 → 赤。
        var determinable = DailySnapshotTestBuilder.Sequence(TransientDays + (WindowDays * 4)).ToList();
        ApplyDispersionWindow(determinable, Item.Bread, windowIndex: 0, targetPermille: 0);
        ApplyDispersionWindow(determinable, Item.Bread, windowIndex: 1, targetPermille: 300);
        ApplyDispersionWindow(determinable, Item.Bread, windowIndex: 2, targetPermille: 300);
        ApplyDispersionWindow(determinable, Item.Bread, windowIndex: 3, targetPermille: 600);

        // 判定不能側: 窓の偏差‰ が [0, 0, 0, 300, 700] (5窓・基準窓=4番目・基準窓から最後まで2窓 < 3)。
        var indeterminate = DailySnapshotTestBuilder.Sequence(TransientDays + (WindowDays * 5)).ToList();
        ApplyDispersionWindow(indeterminate, Item.Bread, windowIndex: 0, targetPermille: 0);
        ApplyDispersionWindow(indeterminate, Item.Bread, windowIndex: 1, targetPermille: 0);
        ApplyDispersionWindow(indeterminate, Item.Bread, windowIndex: 2, targetPermille: 0);
        ApplyDispersionWindow(indeterminate, Item.Bread, windowIndex: 3, targetPermille: 300);
        ApplyDispersionWindow(indeterminate, Item.Bread, windowIndex: 4, targetPermille: 700);

        Assert.Equal(Verdict.Red, GetItem(Run(determinable), "8-1a").Verdict);
        Assert.Equal(Verdict.Indeterminate, GetItem(Run(indeterminate), "8-1a").Verdict);
    }

    /// <summary>
    /// 44(別表(フェーズ1の訂正)。レビュー2巡目指摘2により、全品目・全窓で偏差‰ = 0(全有効日の
    /// 中央値が一定)になる入力へ訂正)。核心。全窓で偏差‰ = 0 の390日以上の走行は8-1aが緑
    /// (基準窓が一度も立たない。TDD01 §5.2「基準窓が存在しない → 緑」)。<b>既定の
    /// <see cref="DailySnapshotTestBuilder.Sequence"/> は中央値を一定にしない</b>
    /// (<c>MedianFor</c> が周期的に変動する)ため、全品目の全日を <see cref="ApplyConstantMedian"/>
    /// で床×2に固定する ── これをしないと基準窓が窓1で必ず立ち、緑になるのは条件(2)
    /// (最後の窓の偏差‰ &gt;= 100)が効いているだけになり、「基準窓が存在しない → 緑」の枝に
    /// カバレッジが無くなる。
    /// </summary>
    [Fact]
    public void DispersionIsGreenWhenDispersionNeverAppears()
    {
        var days = DailySnapshotTestBuilder.Sequence(ThreeWindowDays).ToList();

        for (int itemId = 0; itemId < DailySnapshotTestBuilder.Definition.ItemCount; itemId++)
        {
            if (DailySnapshotTestBuilder.Definition.IsPrimaryItem(itemId))
            {
                continue;
            }

            ApplyConstantMedian(days, itemId, 0, ThreeWindowDays - 1, DailySnapshotTestBuilder.Floor(itemId) * 2);
        }

        Assert.Equal(Verdict.Green, GetItem(Run(days), "8-1a").Verdict);
    }

    /// <summary>
    /// 45(別表(フェーズ1の訂正))。核心。偏差‰ が窓ごとに 0, 0, 300, 700, 1500 と並ぶ走行(5窓)で
    /// 8-1aが赤、FirstRedDayが最後の窓の末日になる(床への張り付きから離脱して発散する経路を
    /// 検出できる ── 「最初の窓が0なら以後この枝を諦める」実装だと黙って見逃す)。
    /// </summary>
    [Fact]
    public void DispersionFiresAfterLeavingTheFloor()
    {
        int totalDays = TransientDays + (WindowDays * 5);
        var days = DailySnapshotTestBuilder.Sequence(totalDays).ToList();
        ApplyDispersionWindow(days, Item.Bread, windowIndex: 0, targetPermille: 0);
        ApplyDispersionWindow(days, Item.Bread, windowIndex: 1, targetPermille: 0);
        ApplyDispersionWindow(days, Item.Bread, windowIndex: 2, targetPermille: 300);
        ApplyDispersionWindow(days, Item.Bread, windowIndex: 3, targetPermille: 700);
        ApplyDispersionWindow(days, Item.Bread, windowIndex: 4, targetPermille: 900);

        var item = GetItem(Run(days), "8-1a");
        int lastWindowEndDay = totalDays - 1;

        Assert.Equal(Verdict.Red, item.Verdict);
        Assert.Equal(lastWindowEndDay, item.FirstRedDay);
    }

    /// <summary>
    /// 46(別表(フェーズ1の訂正))。偏差‰ が 1, 2, 99 の走行(3窓)は緑(単調・3窓・最後≥基準×2だが
    /// 100‰未満)、1, 2, 100 は赤(散らばりの下限 <see cref="VerificationThresholds.DispersionFloorPermille"/>
    /// = 100)。
    /// </summary>
    [Fact]
    public void DispersionNeedsAbsoluteDispersionToo()
    {
        var green = DailySnapshotTestBuilder.Sequence(TransientDays + (WindowDays * 3)).ToList();
        ApplyDispersionWindow(green, Item.Bread, windowIndex: 0, targetPermille: 10);
        ApplyDispersionWindow(green, Item.Bread, windowIndex: 1, targetPermille: 20);
        ApplyDispersionWindow(green, Item.Bread, windowIndex: 2, targetPermille: 90);

        var red = DailySnapshotTestBuilder.Sequence(TransientDays + (WindowDays * 3)).ToList();
        ApplyDispersionWindow(red, Item.Bread, windowIndex: 0, targetPermille: 10);
        ApplyDispersionWindow(red, Item.Bread, windowIndex: 1, targetPermille: 20);
        ApplyDispersionWindow(red, Item.Bread, windowIndex: 2, targetPermille: 100);

        Assert.Equal(Verdict.Green, GetItem(Run(green), "8-1a").Verdict);
        Assert.Equal(Verdict.Red, GetItem(Run(red), "8-1a").Verdict);
    }

    /// <summary>
    /// 47(別表(フェーズ1の訂正))。核心。帯の枝が緑・偏差の枝が判定不能(基準窓以降が2窓)の
    /// 走行で、8-1aが判定不能になる(判定不能を緑へ倒さない。TDD01 §5.2「畳み方はどの階層でも
    /// 赤 &gt; 判定不能 &gt; 緑」)。
    /// </summary>
    [Fact]
    public void IndeterminateBranchDoesNotFoldToGreen()
    {
        var days = DailySnapshotTestBuilder.Sequence(TransientDays + (WindowDays * 5)).ToList();
        ApplyDispersionWindow(days, Item.Bread, windowIndex: 0, targetPermille: 0);
        ApplyDispersionWindow(days, Item.Bread, windowIndex: 1, targetPermille: 0);
        ApplyDispersionWindow(days, Item.Bread, windowIndex: 2, targetPermille: 0);
        ApplyDispersionWindow(days, Item.Bread, windowIndex: 3, targetPermille: 300);
        ApplyDispersionWindow(days, Item.Bread, windowIndex: 4, targetPermille: 700);

        Assert.Equal(Verdict.Indeterminate, GetItem(Run(days), "8-1a").Verdict);
    }

    /// <summary>
    /// 48(別表(フェーズ1の訂正))。5品目のうち1品目だけが赤の窓で8-1aが赤。1品目だけが
    /// 判定不能で残りが緑なら判定不能(品目間の畳み込み。TDD01 §5.2「畳む階層は3つ」)。
    /// </summary>
    [Fact]
    public void ItemsFoldAcrossGoodsWithinAWindow()
    {
        var withOneRedItem = DailySnapshotTestBuilder.Sequence(150).ToList();
        var badBreadPrice = withOneRedItem[100].Prices[Item.Bread] with { SettledMedian = 10000, SettledCount = 4 };
        withOneRedItem[100] = DailySnapshotTestBuilder.WithPrice(withOneRedItem[100], Item.Bread, badBreadPrice);

        Assert.Equal(Verdict.Red, GetItem(Run(withOneRedItem), "8-1a").Verdict);

        // 他の品目(・Beerの2窓目以降)は健全な3窓ぶんを持たせ、確実に緑になるようにする
        // (偏差の枝は基準窓から3窓無いと判定不能になるため。1窓だけの走行では健全な品目すら
        // 判定不能になり、「残りが緑」を検査できない)。
        var withOneIndeterminateItem = DailySnapshotTestBuilder.Sequence(ThreeWindowDays).ToList();
        ZeroOutSettledCount(withOneIndeterminateItem, Item.Beer, TransientDays, TransientDays + 90); // 窓1のみ29日有効

        Assert.Equal(Verdict.Indeterminate, GetItem(Run(withOneIndeterminateItem), "8-1a").Verdict);
    }

    /// <summary>
    /// 52(別表(レビュー3巡目で追加)#52)。帯の枝の窓の階層だけを分離して観察できることを確かめる。
    /// 510日(過渡期30 + 窓4つ)で、Breadの窓1だけ有効日29(帯の枝が判定不能)、窓2〜4は
    /// 同じ偏差‰(200)を持つ健全な窓にする。偏差の枝は基準窓(窓2)から3窓そろい、単調非減少
    /// だが成長条件(基準窓×2)を満たさないので緑になる ── 8-1aの判定不能が「偏差の枝の判定不能
    /// との合成」からではなく、帯の枝そのものの窓の畳み込みから出ていることを分離して確かめる
    /// (どのテストも2つの枝が両方とも判定不能な入力しか作っていなかった。レビュー3巡目指摘A)。
    /// </summary>
    [Fact]
    public void BandBranchWindowFoldIsObservable()
    {
        int totalDays = TransientDays + (WindowDays * 4); // 510日
        var days = DailySnapshotTestBuilder.Sequence(totalDays).ToList();

        // 窓1(day30〜149)のBreadだけ有効日を29日にする(帯の枝が判定不能)。
        ZeroOutSettledCount(days, Item.Bread, TransientDays, TransientDays + 90);

        // 窓2〜4(day150〜509)のBreadは同じ偏差‰(200)を持つ ── 基準窓(窓2)以降3窓そろって
        // 単調非減少・同値なので、成長条件(最後 >= 基準×2)が偽になり偏差の枝は緑になる。
        ApplyDispersionWindow(days, Item.Bread, windowIndex: 1, targetPermille: 200);
        ApplyDispersionWindow(days, Item.Bread, windowIndex: 2, targetPermille: 200);
        ApplyDispersionWindow(days, Item.Bread, windowIndex: 3, targetPermille: 200);

        Assert.Equal(Verdict.Indeterminate, GetItem(Run(days), "8-1a").Verdict);
    }

    /// <summary>
    /// 49(レビュー2巡目指摘1)。窓の階層を持つ4項目(8-1b/8-4/8-5b/8-5c)は、緑の窓と判定不能の
    /// 窓が混在する走行で判定不能になる(緑にならない。TDD01 §5.2「赤 &gt; 判定不能 &gt; 緑」は
    /// 窓の階層でも同じ)。<i>この実装ミスで落ちる</i>: 窓の階層だけ「すべて判定不能なら判定不能」
    /// で畳んだ(<see cref="VerificationAccumulator.ResolveFold"/> の旧実装)。
    /// </summary>
    [Fact]
    public void WindowFoldKeepsIndeterminateOverGreen()
    {
        int window1End = TransientDays + WindowDays - 1;
        int window2Start = window1End + 1;
        int window2End = window2Start + WindowDays - 1;

        var days = DailySnapshotTestBuilder.Sequence(window2End + 1).ToList();

        // 窓1は既定の健全な日のまま(4項目とも緑)。窓2だけを判定不能にする ──
        // 8-1b/8-5b はBreadの約定を殺して有効日数を0にし、8-4はBreadの区画を1つに減らして
        // 有効日数を0にし、8-5cは全品目のOfferMaxを天井以下に落として「試された日」を0にする。
        ZeroOutSettledCount(days, Item.Bread, window2Start, window2End);
        RemoveSecondDistrict(days, Item.Bread, window2Start, window2End);
        SetOfferMaxNeverTested(days, window2Start, window2End);

        var result = Run(days);

        Assert.Equal(Verdict.Indeterminate, GetItem(result, "8-1b").Verdict);
        Assert.Equal(Verdict.Indeterminate, GetItem(result, "8-4").Verdict);
        Assert.Equal(Verdict.Indeterminate, GetItem(result, "8-5b").Verdict);
        Assert.Equal(Verdict.Indeterminate, GetItem(result, "8-5c").Verdict);
    }

    /// <summary>
    /// 50(レビュー2巡目指摘1)。窓の階層を持つ4項目(8-1b/8-4/8-5b/8-5c)は、赤・判定不能・緑の
    /// 窓が混在する走行で赤になり、<see cref="VerificationItemResult.FirstRedDay"/> は赤くなった窓の
    /// 末日になる。<b>判定不能の窓を先に(窓1)置いてから赤の窓(窓2)を置く</b> ── 赤くロックした
    /// 後に判定不能を数えない実装(<see cref="VerificationAccumulator.Merge"/>)の下で、
    /// <c>IndeterminateWindows &gt; 0</c> と <c>RedLocked</c> が同時に真になる状態を作るためである。
    /// <i>この実装ミスで落ちる</i>: <see cref="VerificationAccumulator.ResolveFold"/> が判定不能を
    /// 赤より優先する順序で判定した(順位を取り違えた)。
    /// </summary>
    [Fact]
    public void WindowFoldKeepsRedOverIndeterminate()
    {
        int window1Start = TransientDays;
        int window1End = TransientDays + WindowDays - 1;
        int window2Start = window1End + 1;
        int window2End = window2Start + WindowDays - 1;
        int window3Start = window2End + 1;
        int window3End = window3Start + WindowDays - 1;

        // 8-1b(Bread): 窓1で約定を殺して判定不能、窓2でレンジを狭めて赤(硬直)、窓3は既定の緑。
        var rigidityDays = DailySnapshotTestBuilder.Sequence(window3End + 1).ToList();
        ZeroOutSettledCount(rigidityDays, Item.Bread, window1Start, window1End);
        ApplyConstantMedian(rigidityDays, Item.Bread, window2Start, window2End, DailySnapshotTestBuilder.Floor(Item.Bread) * 2);
        var rigidityItem = GetItem(Run(rigidityDays), "8-1b");
        Assert.Equal(Verdict.Red, rigidityItem.Verdict);
        Assert.Equal(window2End, rigidityItem.FirstRedDay);

        // 8-5b(Beer): 窓1で約定を殺して判定不能、窓2で天井超えを30日以上続けて赤、窓3は既定の緑。
        int beerCeiling = DailySnapshotTestBuilder.Ceiling(Item.Beer);
        var bandDays = DailySnapshotTestBuilder.Sequence(window3End + 1).ToList();
        ZeroOutSettledCount(bandDays, Item.Beer, window1Start, window1End);
        for (int day = window2Start; day <= window2End; day++)
        {
            bandDays[day] = DailySnapshotTestBuilder.WithPrice(
                bandDays[day], Item.Beer, bandDays[day].Prices[Item.Beer] with { SettledMedian = beerCeiling + 1, SettledCount = 4 });
        }
        var bandItem = GetItem(Run(bandDays), "8-5b");
        Assert.Equal(Verdict.Red, bandItem.Verdict);
        Assert.Equal(window2End, bandItem.FirstRedDay);

        // 8-4(Flour): 窓1で区画を1つに減らして判定不能、窓2で2区画を同値にそろえて赤(収束)、窓3は既定の緑。
        var spreadDays = DailySnapshotTestBuilder.Sequence(window3End + 1).ToList();
        RemoveSecondDistrict(spreadDays, Item.Flour, window1Start, window1End);
        ConvergeDistricts(spreadDays, Item.Flour, window2Start, window2End, DailySnapshotTestBuilder.Floor(Item.Flour) * 2);
        var spreadItem = GetItem(Run(spreadDays), "8-4");
        Assert.Equal(Verdict.Red, spreadItem.Verdict);
        Assert.Equal(window2End, spreadItem.FirstRedDay);

        // 8-5c: 窓1は全品目のOfferMaxを天井以下にして「試された日」を0にし判定不能、窓2は
        // 天井を試しつつ窓内の約定を全品目0にして赤、窓3は既定の緑。
        var purchaseDays = DailySnapshotTestBuilder.Sequence(window3End + 1).ToList();
        SetOfferMaxNeverTested(purchaseDays, window1Start, window1End);
        SetOfferMaxTestedWithNoWindowPurchase(purchaseDays, window2Start, window2End);
        var purchaseItem = GetItem(Run(purchaseDays), "8-5c");
        Assert.Equal(Verdict.Red, purchaseItem.Verdict);
        Assert.Equal(window2End, purchaseItem.FirstRedDay);
    }

    /// <summary>
    /// 53(別表(レビュー3巡目で追加)#53)。同じ窓の中で、ある品目/職業が赤・別の品目/職業が
    /// 判定不能になる入力で、8-1b / 8-3 / 8-4 / 8-5b が赤になる(<see cref="VerificationAccumulator.Pool"/> の階層。
    /// 「赤 &gt; 判定不能」)。#50 は判定不能を窓1・赤を窓2に置いて<b>窓の階層</b>だけを試して
    /// いた ── このテストは<b>品目・職業の階層</b>を試す(レビュー3巡目指摘B)。
    /// </summary>
    /// <remarks>
    /// <b>8-3の「判定不能」は、他の3項目と形が異なる</b>(決めて報告)。TDD01 §5.2「8-3」の列は
    /// 「その職業の世帯が0戸の窓はその職業を外す。全職業が0戸なら判定不能」と定めており、
    /// 0戸の職業は<see cref="Verdict.Indeterminate"/>として<c>parts</c>へ積まれるのではなく、
    /// そもそも<c>parts</c>から除外される(<see cref="VerificationAccumulator.Evaluate8_3"/>)。
    /// 1つの職業だけが0戸でも、他の職業が生きていれば項目レベルの判定不能にはならない。
    /// したがって8-3で検査できるのは「0戸で除外された職業がPoolの結果を薄めない」ことであり、
    /// 他の3項目の「partsにIndeterminateが混じっても赤が勝つ」とは別の経路である
    /// (どちらも「Poolが赤を取りこぼさない」という同じ性質の別の現れ方)。
    /// <see cref="VerificationAccumulator.Pool"/> はIdの若い順(<c>for (int item = 0; ...)</c>)に
    /// 走査し、赤を見つけた時点で <c>return Verdict.Red;</c> する。8-1b / 8-4 / 8-5b の下の3つの
    /// サブケースは、赤の品目のIdが判定不能の品目のIdより若い(赤を先に踏む)。<b>この向きだけでは、
    /// 「判定不能を先に見ても、最後まで走査して赤を拾う」という Pool の振る舞いを検査できない</b>
    /// ── 赤が常に先に踏まれる入力では、判定不能を見つけた時点で早期returnする変異でも同じ結果に
    /// なる。そのため各項目に「判定不能の品目のIdが赤の品目のIdより若い」向きのサブケースを
    /// 追加する(下の3つの「Idの向きを逆にする」ブロック)。
    /// </remarks>
    [Fact]
    public void PoolKeepsRedOverIndeterminateWithinAWindow()
    {
        // 8-1b: Breadは全日同一値(レンジ0、硬直で赤)。Beerは窓の約定を全日殺す(判定不能)。
        var rigidityDays = DailySnapshotTestBuilder.Sequence(150).ToList();
        int breadFloor = DailySnapshotTestBuilder.Floor(Item.Bread);

        for (int day = 0; day < rigidityDays.Count; day++)
        {
            rigidityDays[day] = DailySnapshotTestBuilder.WithPrice(
                rigidityDays[day], Item.Bread, rigidityDays[day].Prices[Item.Bread] with { SettledMedian = breadFloor * 2, SettledCount = 4 });
        }

        ZeroOutSettledCount(rigidityDays, Item.Beer, TransientDays, 149);

        Assert.Equal(Verdict.Red, GetItem(Run(rigidityDays), "8-1b").Verdict);

        // 8-1b(Idの向きを逆にする): Flour(id=4)を判定不能(約定なし)にし、Bread(id=6)を硬直で
        // 赤にする。上のBread(id=6)/Beer(id=7)は赤が先に来る向きなので、判定不能のIdが赤のId
        // より若い向きをここで足す。
        var rigidityReversedDays = DailySnapshotTestBuilder.Sequence(150).ToList();

        for (int day = 0; day < rigidityReversedDays.Count; day++)
        {
            rigidityReversedDays[day] = DailySnapshotTestBuilder.WithPrice(
                rigidityReversedDays[day], Item.Bread,
                rigidityReversedDays[day].Prices[Item.Bread] with { SettledMedian = breadFloor * 2, SettledCount = 4 });
        }

        ZeroOutSettledCount(rigidityReversedDays, Item.Flour, TransientDays, 149);

        Assert.Equal(Verdict.Red, GetItem(Run(rigidityReversedDays), "8-1b").Verdict);

        // 8-3: 醸造を窓の全日停止(赤)。木材加工の世帯を全日居なくす(その職業を除外。0戸)。
        var productionDays = DailySnapshotTestBuilder.Sequence(150).ToList();

        for (int day = 0; day < productionDays.Count; day++)
        {
            var households = productionDays[day].Households
                .Where(h => (int)h.Occupation != (int)Occupation.Woodworker)
                .Select(h => (int)h.Occupation == (int)Occupation.Brewer ? h with { ProductionRuns = 0 } : h)
                .ToList();
            productionDays[day] = DailySnapshotTestBuilder.WithHouseholds(productionDays[day], households);
        }

        Assert.Equal(Verdict.Red, GetItem(Run(productionDays), "8-3").Verdict);

        // 8-4: Flourは区画差0(収束、赤)。醸造(Beer)の担い手2戸のうち1戸をFlourへ付け替えて
        // Beerの担い手を1戸に減らす(判定不能。#18と同じ形)。
        var spreadDays = DailySnapshotTestBuilder.Sequence(150).ToList();
        ConvergeDistricts(spreadDays, Item.Flour, TransientDays, 149, DailySnapshotTestBuilder.Floor(Item.Flour) * 2);

        for (int day = 0; day < spreadDays.Count; day++)
        {
            var households = spreadDays[day].Households.ToList();
            var brewerHouseholds = households.Where(h => (int)h.Occupation == (int)Occupation.Brewer).ToList();
            var reassigned = brewerHouseholds[1];
            int index = households.IndexOf(reassigned);
            households[index] = reassigned with { OutputItemId = Item.Flour };
            spreadDays[day] = DailySnapshotTestBuilder.WithHouseholds(spreadDays[day], households);
        }

        Assert.Equal(Verdict.Red, GetItem(Run(spreadDays), "8-4").Verdict);

        // 8-4(Idの向きを逆にする): Flour(id=4)は窓内で区画を1つに減らして判定不能にし、
        // Bread(id=6)は区画差0(収束)で赤にする。上のFlour(id=4)/Beer(id=7)は赤が先に来る
        // 向きなので、判定不能のIdが赤のIdより若い向きをここで足す。
        var spreadReversedDays = DailySnapshotTestBuilder.Sequence(150).ToList();
        RemoveSecondDistrict(spreadReversedDays, Item.Flour, TransientDays, 149);
        ConvergeDistricts(spreadReversedDays, Item.Bread, TransientDays, 149, DailySnapshotTestBuilder.Floor(Item.Bread) * 2);

        Assert.Equal(Verdict.Red, GetItem(Run(spreadReversedDays), "8-4").Verdict);

        // 8-5b: Flourは窓内で30日連続天井超え(赤)。Beerは窓の約定を全日殺す(判定不能)。
        var bandDays = DailySnapshotTestBuilder.Sequence(150).ToList();
        int flourCeiling = DailySnapshotTestBuilder.Ceiling(Item.Flour);
        NormalizeBelowCeiling(bandDays, Item.Flour, flourCeiling);

        for (int day = TransientDays; day < TransientDays + 30; day++)
        {
            bandDays[day] = DailySnapshotTestBuilder.WithPrice(
                bandDays[day], Item.Flour, bandDays[day].Prices[Item.Flour] with { SettledMedian = flourCeiling + 1, SettledCount = 4 });
        }

        ZeroOutSettledCount(bandDays, Item.Beer, TransientDays, 149);

        Assert.Equal(Verdict.Red, GetItem(Run(bandDays), "8-5b").Verdict);

        // 8-5b(Idの向きを逆にする): Flour(id=4)は窓の約定を全日殺して判定不能にし、
        // Bread(id=6)は窓内で30日連続天井超えで赤にする。上のFlour(id=4)/Beer(id=7)は赤が
        // 先に来る向きなので、判定不能のIdが赤のIdより若い向きをここで足す。
        var bandReversedDays = DailySnapshotTestBuilder.Sequence(150).ToList();
        int breadCeiling = DailySnapshotTestBuilder.Ceiling(Item.Bread);
        NormalizeBelowCeiling(bandReversedDays, Item.Bread, breadCeiling);

        for (int day = TransientDays; day < TransientDays + 30; day++)
        {
            bandReversedDays[day] = DailySnapshotTestBuilder.WithPrice(
                bandReversedDays[day], Item.Bread, bandReversedDays[day].Prices[Item.Bread] with { SettledMedian = breadCeiling + 1, SettledCount = 4 });
        }

        ZeroOutSettledCount(bandReversedDays, Item.Flour, TransientDays, 149);

        Assert.Equal(Verdict.Red, GetItem(Run(bandReversedDays), "8-5b").Verdict);
    }

    /// <summary>
    /// 54(別表(レビュー3巡目で追加)#54)。窓の全日で <see cref="DailySnapshot.Households"/> が
    /// 空の走行は、8-3が判定不能になる(TDD01 §5.2「全職業が0戸なら判定不能」。#13は世帯行を
    /// 5件に減らすだけで、職業が1つも残らない走行を試していなかった)。
    /// </summary>
    [Fact]
    public void ProductionStopIsIndeterminateWithoutAnyHousehold()
    {
        var days = DailySnapshotTestBuilder.Sequence(150).ToList();

        for (int day = 0; day < days.Count; day++)
        {
            days[day] = DailySnapshotTestBuilder.WithHouseholds(days[day], Array.Empty<HouseholdRow>());
        }

        Assert.Equal(Verdict.Indeterminate, GetItem(Run(days), "8-3").Verdict);
    }

    /// <summary>
    /// 56(別表(レビュー3巡目で追加)#56)。窓の全日で需要行(<c>demand_lines</c>)が0の走行は、
    /// 8-7が判定不能になる(TDD01 §5.2 L466 の唯一の判定不能条件。#27は day 100/101 に需要行を
    /// 残しており、分母が0になる走行を試していなかった)。
    /// </summary>
    [Fact]
    public void UnknownPriceRatioIsIndeterminateWithoutDemandLines()
    {
        var days = DailySnapshotTestBuilder.Sequence(150).ToList();

        for (int day = TransientDays; day < 150; day++)
        {
            days[day] = DailySnapshotTestBuilder.WithEconomy(
                days[day], days[day].Economy with { DemandLines = 0, DemandLinesWithoutKnownPrice = 0 });
        }

        Assert.Equal(Verdict.Indeterminate, GetItem(Run(days), "8-7").Verdict);
    }

    /// <summary><paramref name="itemId"/> の <c>DistrictId == 1</c> の行を除いて1区画だけにする(8-4の有効日数を0にする)。</summary>
    private static void RemoveSecondDistrict(List<DailySnapshot> days, int itemId, int fromDay, int toDayInclusive)
    {
        for (int day = fromDay; day <= toDayInclusive; day++)
        {
            var districts = days[day].Districts.Where(d => !(d.ItemId == itemId && d.DistrictId == 1)).ToList();
            days[day] = DailySnapshotTestBuilder.WithDistricts(days[day], districts);
        }
    }

    /// <summary><paramref name="itemId"/> の全区画の <c>SettledMedian</c> を同値にそろえる(区画差0。8-4の赤条件)。</summary>
    private static void ConvergeDistricts(List<DailySnapshot> days, int itemId, int fromDay, int toDayInclusive, int median)
    {
        for (int day = fromDay; day <= toDayInclusive; day++)
        {
            var districts = days[day].Districts
                .Select(d => d.ItemId == itemId ? d with { SettledMedian = median } : d)
                .ToList();
            days[day] = DailySnapshotTestBuilder.WithDistricts(days[day], districts);
        }
    }

    /// <summary>全品目の <c>OfferMax</c> を天井以下に落とし、8-5cの「天井が試された日」を0にする。</summary>
    private static void SetOfferMaxNeverTested(List<DailySnapshot> days, int fromDay, int toDayInclusive)
    {
        for (int day = fromDay; day <= toDayInclusive; day++)
        {
            var prices = days[day].Prices.ToList();

            for (int itemId = 0; itemId < prices.Count; itemId++)
            {
                if (DailySnapshotTestBuilder.Definition.IsPrimaryItem(itemId))
                {
                    continue;
                }

                int ceiling = DailySnapshotTestBuilder.Ceiling(itemId);
                prices[itemId] = prices[itemId] with { OfferMax = ceiling };
            }

            days[day] = new DailySnapshot(
                days[day].Economy, prices, days[day].Districts, days[day].Households, days[day].Trades);
        }
    }

    /// <summary>
    /// 全品目の <c>OfferMax</c> を天井超えにして「試された日」にしつつ、<c>WindowSettledCount</c> を
    /// 0にして窓口購入合計を0にする(8-5cの赤条件 ── 試された日があり窓口購入が0)。
    /// </summary>
    private static void SetOfferMaxTestedWithNoWindowPurchase(List<DailySnapshot> days, int fromDay, int toDayInclusive)
    {
        for (int day = fromDay; day <= toDayInclusive; day++)
        {
            var prices = days[day].Prices.ToList();

            for (int itemId = 0; itemId < prices.Count; itemId++)
            {
                if (DailySnapshotTestBuilder.Definition.IsPrimaryItem(itemId))
                {
                    continue;
                }

                int ceiling = DailySnapshotTestBuilder.Ceiling(itemId);
                prices[itemId] = prices[itemId] with { OfferMax = ceiling + 1, WindowSettledCount = 0 };
            }

            days[day] = new DailySnapshot(
                days[day].Economy, prices, days[day].Districts, days[day].Households, days[day].Trades);
        }
    }

    /// <summary>
    /// <paramref name="targetPermille"/> の相対平均絶対偏差‰ を持つ窓を作る(avg=100 を基準にした
    /// 対称パターン。avg=100・低=100-d・高=100+dの60/60交互で dispersion‰ = 10d = targetPermille に
    /// 厳密に一致する。<paramref name="targetPermille"/> は10の倍数、かつ 0〜900 の範囲で使うこと
    /// ── 低が0を割らず、帯 [床÷10, 床×10] の内側に収まる範囲)。<paramref name="targetPermille"/> = 0
    /// のときは <see cref="ApplyConstantMedian"/>(基準窓が立たない)と同義。
    /// </summary>
    private static void ApplyDispersionWindow(List<DailySnapshot> days, int itemId, int windowIndex, int targetPermille)
    {
        int windowStart = TransientDays + (windowIndex * WindowDays);
        int windowEnd = windowStart + WindowDays - 1;

        if (targetPermille == 0)
        {
            ApplyConstantMedian(days, itemId, windowStart, windowEnd, 100);
            return;
        }

        int half = targetPermille / 10;
        ApplyAlternatingMedian(days, itemId, windowStart, windowEnd, 100 - half, 100 + half);
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
