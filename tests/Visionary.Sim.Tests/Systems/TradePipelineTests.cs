using Visionary.Sim.Determinism;
using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;
using Xunit.Abstractions;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="TradeSystem"/> を <see cref="ProductionSystem"/> / <see cref="ConsumptionSystem"/> と
/// 一緒に <see cref="WorldDefinition.M0"/> で複数日回したときの検査(#37 タスク仕様のテスト表「統合」節)。
/// </summary>
/// <remarks>
/// <b>世界は必ず <see cref="WorldGenerator.Generate"/> で作る。</b>手組みの縮退した世界(世帯Id =
/// 世帯主NpcId = 職業添字 / 必要運転資金0 / 季節係数が一様)を使わない ── #36 から引き継いだ配線
/// 10件はその3つの縮退に守られて通り抜けたので、ここではそれを崩す(タスク仕様)。
/// </remarks>
public sealed class TradePipelineTests
{
    private readonly ITestOutputHelper _output;

    public TradePipelineTests(ITestOutputHelper output) => _output = output;

    private static ISimSystem[] FullPipeline(WorldDefinition definition) => new ISimSystem[]
    {
        new ProductionSystem(definition),
        new ConsumptionSystem(definition),
        new HouseholdSystem(definition), // 順3(#39)。順5(Trade)より後に置いてはならない
                                          // (②が同一tick内で循環する。GDD02b §3.3 / TDD01 §3.3)。
        new NeedGenerationSystem(definition), // 順4(#40)。順3と順5の間 ── 順5の後に置くと
                                               // UnfilledPurchaseを当日中に読み、GDD06 §3.1の
                                               // 1日遅延が消える(タスク仕様「配線の列挙」)。
        new TradeSystem(definition),
    };

    /// <summary>各世帯の(世帯在庫 + 工房在庫)の合計。添字 = itemId。</summary>
    private static long[] TotalGoodsByItem(World world, int itemCount)
    {
        var totals = new long[itemCount];

        foreach (var household in world.Households)
        {
            for (int itemId = 0; itemId < itemCount; itemId++)
            {
                totals[itemId] += household.HouseholdInventory[itemId];
                totals[itemId] += household.WorkshopInventory[itemId];
            }
        }

        return totals;
    }

    /// <summary>
    /// 【核心】テスト表 #20。10日回した後、全世帯について
    /// 初期資金 + Σ(Saleの額) − Σ(Purchaseの額) == LiquidFunds。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-17)。</b><c>TradeSettlement.Execute</c> の
    /// <c>seller.LiquidFunds += payment;</c> を <c>seller.LiquidFunds += payment - 1;</c>
    /// (支払額と記帳額を1だけずらす)変異を当てたところ、約定が1件以上ある世帯で
    /// <c>Assert.Equal(expectedFunds, household.LiquidFunds)</c> が失敗した(赤を確認、
    /// 帳簿と流動資金の突合が崩れる。issue #37 が閉じる条件そのもの)。変異を戻して
    /// 緑に復帰させた。
    /// </remarks>
    [Fact]
    public void LedgersReconcileWithLiquidFundsForEveryHousehold()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));

        var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));
        scheduler.Advance(world, ticks: 10 * 24);

        bool anyLedgerEntry = false;

        foreach (var household in world.Households)
        {
            long salesTotal = 0;
            long purchasesTotal = 0;

            foreach (var entry in world.Ledgers[household.Id])
            {
                long amount = (long)entry.Quantity * entry.UnitPrice;

                if (entry.Direction == LedgerDirection.Sale)
                {
                    salesTotal += amount;
                }
                else
                {
                    purchasesTotal += amount;
                }

                anyLedgerEntry = true;
            }

            long expectedFunds = definition.InitialLiquidFunds + salesTotal - purchasesTotal;
            Assert.Equal(expectedFunds, household.LiquidFunds);
        }

        // 空振り(誰も一度も約定しない)で緑になるのを防ぐ。
        Assert.True(anyLedgerEntry, "10日回しても1件も約定していない(値の問題の可能性。止まって報告する対象)。");
    }

    /// <summary>
    /// 【核心】W2-15 タスク仕様テスト表 #1(検出器)。M0・シード1/2/3/7/42・60日。観測ゼロの
    /// 初日(<c>DayIndex == 0</c>)に、全世帯の外部 <c>Purchase</c>(<see cref="world.Ledgers"/>、
    /// <c>CounterpartyId == HouseholdState.ExternalMarketSellerId</c>)の
    /// <c>Quantity × UnitPrice</c> を合計した額が、都市の初期総資金の1/10未満であること
    /// (決定10・11。相場項が無い初日は基礎値が窓口の当日価格に落ちるので、旧規則(基礎値=現金上限)
    /// で起きていた一撃買いが縮む)。
    /// </summary>
    /// <remarks>
    /// <b>実測して doc コメントへ転記すること(タスク仕様)。</b>2026-09-22 実測: seed1=816 /
    /// seed2=816 / seed3=816 / seed7=816 / seed42=776(いずれも <c>10 × 合計 &lt; 24,000</c>
    /// を大きく下回る)。#120 の使い捨て実測(816 / 816 / 776、決定11を入れる前、シード3・7は
    /// 未測定)と比べ、シード1・2・42は同値、シード3・7も同じ816である。決定11は初日には効かない
    /// ── 初日は誰も相場基準を持たない(<c>HasMarketTerm = false</c>)ので、分岐1から
    /// 「相場項があり」を外しても判定そのものは変わらない(決定10だけが初日の値を決める)。
    /// 一般化はゲートを緩める方向には動かないので、この実測より上には戻らないはずである。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(<c>mutator</c> が測定、2026-09-22、対象HEAD <c>624f5b1</c>、変異なしで
    /// 445件緑を確認済み)。</b>
    /// <list type="bullet">
    /// <item><b>M-1</b>(<c>BuyerDemand.BuildLine</c> が <c>BaseValue</c> へ窓口価格ではなく
    /// <c>cashCap</c> を渡す変異)は<b>全5シードで赤</b>。<b>ただし赤の理由は閾値の断定ではなく
    /// <c>ArgumentOutOfRangeException</c></b>(<c>cashCap = 0</c> の行を <c>BaseValue</c> の戻り値
    /// ガードが捕まえる)であり、<b>断定に到達しないためday0の輸入額は読めない</b>。仕様は
    /// 「失敗メッセージから輸入額を読む」ことを期待していたが、本タスクで足した <c>BaseValue</c> の
    /// ガードが検出器より手前で落とす ── 2つの機械が別の面を守っていることの現れである。</item>
    /// <item><b>M-2</b>(<c>Decide</c> の分岐1を <c>if (line.HasMarketTerm &amp;&amp;
    /// effectivePrice &gt; adjustedBaseValue)</c> に戻す変異)は<b>本テストでは全5シード緑のまま</b>。
    /// 決定11は購入量を1つも変えない ── 初日の輸入額を決めているのは決定10だけ、という主張の
    /// 実測であり、検出器を2本(本テストと<see
    /// cref="BuyerBudgetTests.DecideClosesOnTheBaseValueWithoutAMarketReference"/>)に割った
    /// 根拠そのものである。</item>
    /// <item><b>M-3</b>(<c>BuyerDemand.WindowPrice</c> の分岐を落とし全品目
    /// <c>ExternalSellPrice(itemId, season)</c> を使う変異)も<b>全5シードで赤</b>。day0の輸入額は
    /// seed1=5752 / seed2=4568 / seed3=4568 / seed7=4744 / seed42=4408(閾値2400を大きく超える)。</item>
    /// <item><b>M-4</b>(<c>WindowPrice</c> が季節を <c>Season.Spring</c> 固定で渡す変異)は
    /// <b>緑のまま</b>。</item>
    /// </list>
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(42)]
    public void WindowImportsOnTheFirstDayStayBelowATenthOfTheCitysMoney(long seed)
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(seed));

        var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(seed));
        scheduler.Advance(world, ticks: 60 * 24);

        long day0Imports = 0;

        foreach (var household in world.Households)
        {
            foreach (var entry in world.Ledgers[household.Id])
            {
                if (entry.CounterpartyId == HouseholdState.ExternalMarketSellerId
                    && entry.Direction == LedgerDirection.Purchase
                    && entry.OccurredAt.DayIndex == 0)
                {
                    day0Imports += (long)entry.Quantity * entry.UnitPrice;
                }
            }
        }

        // 空振り防止(タスク仕様)。
        Assert.True(day0Imports >= 1, $"seed={seed}: 初日に窓口からの輸入が1件も無い(値の問題の可能性)。");
        Assert.Equal(60, world.Now.DayIndex);

        Assert.True(
            10 * day0Imports < (long)definition.InitialLiquidFunds * definition.HouseholdCount,
            $"seed={seed}: 初日の窓口からの輸入額({day0Imports})が都市の初期総資金の1/10以上"
                + $"(初期総資金={(long)definition.InitialLiquidFunds * definition.HouseholdCount})。");
    }

    /// <summary>
    /// 【核心】テスト表 #17(#38)。M0・60日・シード1: Σ(流動資金の変化) ==
    /// Σ(外部 <c>Sale</c> の額) − Σ(外部 <c>Purchase</c> の額)。GDD02d §4.1 の恒等式そのもの。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-21、<c>mutator</c> が使い捨てworktreeで測定、対象コミット
    /// <c>39e367a</c>、M-3)。</b><c>TradeSettlement.ExecuteExport</c> の記帳を落とす
    /// (資金と在庫だけ動かす)変異は期待どおり赤になった。
    /// </remarks>
    [Fact]
    public void MoneyChangesOnlyByTheExternalLedger()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));

        long initialFundsTotal = 0;
        foreach (var household in world.Households)
        {
            initialFundsTotal += household.LiquidFunds;
        }

        var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));
        scheduler.Advance(world, ticks: 60 * 24);

        long finalFundsTotal = 0;
        foreach (var household in world.Households)
        {
            finalFundsTotal += household.LiquidFunds;
        }

        long externalSaleTotal = 0;
        long externalPurchaseTotal = 0;
        bool anyExternalEntry = false;

        foreach (var household in world.Households)
        {
            foreach (var entry in world.Ledgers[household.Id])
            {
                if (entry.CounterpartyId != HouseholdState.ExternalMarketSellerId)
                {
                    continue;
                }

                anyExternalEntry = true;
                long amount = (long)entry.Quantity * entry.UnitPrice;

                if (entry.Direction == LedgerDirection.Sale)
                {
                    externalSaleTotal += amount;
                }
                else
                {
                    externalPurchaseTotal += amount;
                }
            }
        }

        Assert.Equal(finalFundsTotal - initialFundsTotal, externalSaleTotal - externalPurchaseTotal);
        Assert.True(anyExternalEntry, "60日回しても外部の約定が1件も無い(値の問題の可能性)。");
    }

    /// <summary>
    /// テスト表 #18(#38)。60日で外部 <c>Purchase</c> ≥ 1 かつ外部 <c>Sale</c> ≥ 1。
    /// </summary>
    /// <remarks>
    /// <b>窓口の帳簿を世帯として持つ(相手Idが <c>int.MaxValue</c> 以外になる)実装ミス</b>では、
    /// ここで相手Idを予約Idで絞る集計が0件のままになり本テストが落ちる(タスク仕様)。
    /// </remarks>
    [Fact]
    public void ImportsAndExportsAreRecordedAgainstTheReservedId()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));

        var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));
        scheduler.Advance(world, ticks: 60 * 24);

        int externalPurchaseCount = 0;
        int externalSaleCount = 0;

        foreach (var household in world.Households)
        {
            foreach (var entry in world.Ledgers[household.Id])
            {
                if (entry.CounterpartyId != HouseholdState.ExternalMarketSellerId)
                {
                    continue;
                }

                if (entry.Direction == LedgerDirection.Purchase)
                {
                    externalPurchaseCount++;
                }
                else
                {
                    externalSaleCount++;
                }
            }
        }

        Assert.True(externalPurchaseCount >= 1, "60日で外部Purchaseが1件も無い(値の問題の可能性)。");
        Assert.True(externalSaleCount >= 1, "60日で外部Saleが1件も無い(値の問題の可能性)。");
    }

    /// <summary>
    /// テスト表 #19(#38。既存テスト表 #21「TradeNeitherCreatesNorDestroysGoods」の書き直し)。
    /// 約定1日ぶんの前後で、品目ごとに「都市の財の総量の変化 = 外部 <c>Purchase</c> の数量 −
    /// 外部 <c>Sale</c> の数量」が成り立つ(生産・消費を登録せず <see cref="TradeSystem"/> だけで
    /// 回す)。
    /// </summary>
    /// <remarks>
    /// <b>#38 追随(2026-09-20)。</b>輸入が財を増やし輸出が減らすようになったため、
    /// 「= 0」の不変条件は成り立たなくなった(実測: 穀物 156 → 312 など)。<b>世帯間の売買が
    /// 財を作らないことは、この式の右辺に外部の行しか現れないことで守られる</b> ──
    /// <see cref="TradeSettlement.Execute"/>(世帯間)は買い手・売り手の双方を動かし総量を
    /// 変えないので、右辺の外部 <c>Purchase</c>/<c>Sale</c> だけが差分の全量を説明する。
    /// </remarks>
    /// <remarks>
    /// <c>WorldGenerator</c> は各世帯の<b>出力品目</b>の工房在庫を初期化しない(生産の入力・工具
    /// だけを初期化する)ので、<c>TradeSystem</c> 単独では誰も売り注文を出せない。実際に約定が
    /// 起きる母数を作るため、各世帯の出力品目へ販売在庫を直接与える ── 世帯Id・区画・職業の
    /// 対応は <see cref="WorldGenerator"/> が生成した実世界のままで、縮退させない。
    /// </remarks>
    [Fact]
    public void GoodsChangeOnlyByTheExternalLedger()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));

        foreach (var household in world.Households)
        {
            var recipe = definition.Recipes[(int)household.Occupation];
            household.WorkshopInventory[recipe.Outputs[0].ItemId] += 30;
        }

        var before = TotalGoodsByItem(world, definition.ItemCount);

        var scheduler = new SimScheduler(
            new ISimSystem[] { new TradeSystem(definition) }, new RandomSource(1));
        scheduler.Advance(world, ticks: 24);

        var after = TotalGoodsByItem(world, definition.ItemCount);

        var externalPurchaseByItem = new long[definition.ItemCount];
        var externalSaleByItem = new long[definition.ItemCount];
        bool anyLedgerEntry = false;
        long externalPurchaseQuantityTotal = 0;
        long externalSaleQuantityTotal = 0;

        foreach (var household in world.Households)
        {
            foreach (var entry in world.Ledgers[household.Id])
            {
                anyLedgerEntry = true;

                if (entry.CounterpartyId != HouseholdState.ExternalMarketSellerId)
                {
                    // 世帯間の約定は移転であり、双方の行を合わせると財を作らない(下のremark参照)。
                    continue;
                }

                if (entry.Direction == LedgerDirection.Purchase)
                {
                    externalPurchaseByItem[entry.ItemId] += entry.Quantity;
                    externalPurchaseQuantityTotal += entry.Quantity;
                }
                else
                {
                    externalSaleByItem[entry.ItemId] += entry.Quantity;
                    externalSaleQuantityTotal += entry.Quantity;
                }
            }
        }

        for (int itemId = 0; itemId < definition.ItemCount; itemId++)
        {
            long expectedDelta = externalPurchaseByItem[itemId] - externalSaleByItem[itemId];
            Assert.Equal(expectedDelta, after[itemId] - before[itemId]);
        }

        // 空振り防止。誰も約定していなければ、このテストは「生産も消費もしなければ壊れない」
        // という自明な主張しか検証していないことになる。
        Assert.True(
            anyLedgerEntry,
            "1日回しても1件も約定していない(値の問題の可能性。止まって報告する対象)。");

        // 別表(続き)R-7(レビュー2巡目)。上のanyLedgerEntryは世帯間の行でも真になるので、
        // 不変条件を「=0」から「=外部Purchase-外部Sale」へ書き換えたのに旧テストの空振り防止を
        // 持ち越すと、外部の約定が0件の日でも両辺とも0で緑になる(書き換え前の主張しか
        // 検証していない)。外部の約定が実際に1件以上あることを別に確かめる。
        Assert.True(
            externalPurchaseQuantityTotal + externalSaleQuantityTotal >= 1,
            "1日回しても外部の約定(Purchase/Sale)が1件も無い(値の問題の可能性)。");
    }

    /// <summary>
    /// 【核心】テスト表 #22。30日回した後、ビール(嗜好)の約定が1件以上ある。
    /// </summary>
    /// <remarks>
    /// <b>工具(耐久)の約定はここでは検証しない</b>(タスク仕様「耐久(工具)の約定は本タスクでは
    /// 検証しない(フェーズ1 の裁定)」)。W2では1次産品に売り手が居ないため生産が5日で止まり、
    /// 摩耗も5回で止まる。耐久の需要が立つには摩耗15回が要るので、耐久の約定はパイプラインでは
    /// 構造的に成立しない。母数(耐久 = 流動資金 − 必需の取り置き)の取り違えの検出は
    /// <see cref="BuyerBudgetTests.AvailableFundsAreStagedByPurpose"/>(単体)が担う
    /// (#97 で母数の段階が <c>BuyerBudget.AvailableFunds</c> へ統一された)。
    /// 「工具が実際に買われる」ことの検証は #38 の Exit Criteria へ移された。
    /// </remarks>
    [Fact]
    public void PreferenceIsActuallyBought()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));

        var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));
        scheduler.Advance(world, ticks: 30 * 24);

        bool AnyPurchase(int itemId) => world.Ledgers.Any(entries => entries.Any(
            entry => entry.Direction == LedgerDirection.Purchase && entry.ItemId == itemId));

        Assert.True(AnyPurchase(Item.Beer), "30日でビールが一度も買われなかった(値の問題の可能性)。");
    }

    /// <summary>
    /// 【核心】テスト表 #23。30日のあいだに、自区画から距離2以上の売り手の観測を持つ世帯が
    /// 1つ以上現れる。
    /// </summary>
    /// <remarks>
    /// <b>最終日の一点ではなく、毎日判定する。</b>観測は保持期間(GDD06 §3.1)で失効するので、
    /// 遠方の知識は生成と失効を繰り返す。実測(シード1、2026-09-17): day1〜29は複数日で
    /// 距離2・距離3の観測が現れるが、day30だけは失効の谷に当たって0件になる。「30日で最終日に
    /// 持っているか」ではなく「30日のうちに広がるか」を検証したいので、最終スナップショットではなく
    /// 毎日の判定のいずれかが真になることを見る(タスク仕様「日数は仕様ではなく、実装が緑にできる
    /// 最小の日数でよい」)。
    /// </remarks>
    [Fact]
    public void KnowledgeSpreadsBeyondTheHomeVisionRadius()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));

        var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));

        bool FoundDistantKnowledge() => world.Households.Any(household =>
            world.Knowledge[household.HeadNpcId].Any(
                observation => District.Distance(household.DistrictId, observation.LocationId) >= 2));

        bool foundDistantKnowledge = false;

        for (int day = 1; day <= 30 && !foundDistantKnowledge; day++)
        {
            scheduler.Advance(world, ticks: 24);
            foundDistantKnowledge = FoundDistantKnowledge();
        }

        Assert.True(
            foundDistantKnowledge,
            "30日のどの日にも、自区画から距離2以上の観測を持つ世帯が現れなかった"
                + "(段6が訪問区画を渡していない可能性、または値の問題)。");
    }

    /// <summary>
    /// 【核心】W2-13 タスク仕様テスト表 #1-a・#1-b・#2・#3。M0・シード1/2/3/7/42・60日。判定対象は
    /// <c>world.Ledgers</c> の約定価格(<c>UnitPrice</c>)。
    /// </summary>
    /// <remarks>
    /// <b>断定は2段に分ける(2026-09-21の訂正。フェーズ1)。</b>初版は「都市生産品の約定すべてが
    /// ×2以下」という1本の断定だったが、[GDD02c §1](../../../docs/03-gdd/02c-price-and-budget.md)
    /// の囲み・[GDD02d §2.2](../../../docs/03-gdd/02d-external-market-and-money.md) の囲みが
    /// 「周縁の売り手は窓口へ行く手間のぶんだけ外部売値を上回っても売れる」と定めており、
    /// 周縁の約定が天井を上回ることは仕様の範囲内である。
    /// <list type="bullet">
    /// <item><b>1-a(構造)</b>: 買い手の区画が中心(<see cref="District.ExternalMarketDistrictId"/>)
    /// である <c>Purchase</c> 行に限り、都市生産品の約定単価が
    /// <c>ExternalBuyPrice(itemId) × BandMultiplier</c>(= 2)以下。中心の買い手には
    /// <see cref="ExternalMarket.IsWithinReach"/> が常に真を返すので窓口が必ず候補に入り、
    /// <see cref="StoreChoice"/> は <c>&lt;</c> で比べる(同値なら都市内)ため、実効価格は
    /// 外部売値を超えられない(タスク仕様「1-aが構造的に成り立つ根拠」)。</item>
    /// <item><b>1-b(水準)</b>: 全区画の都市生産品の約定が
    /// <c>ExternalBuyPrice(itemId) × PeripheralBandMultiplier</c> 以下。1-aが赤なら実装か
    /// モデルの欠陥、1-bが赤なら水準の問題であり、区別できない1本の断定にしない。</item>
    /// </list>
    /// </remarks>
    /// <remarks>
    /// <b>旧検出器(<c>OfferPricesStayWithinTheBandOverSixtyDays</c>、W2-11)を置き換える。</b>
    /// 旧検出器は <c>world.Market</c>(その日の提示価格)を毎日見て床の20倍を上限にしていた。
    /// <b>提示価格は天井を上回ってよい</b>(GDD02 §8-5)ので、判定対象をそのまま <c>world.Market</c>
    /// に残して閾値だけ変えると、仕様が許している値で赤になる。<b>置き換えるのは閾値だけでは
    /// なく、判定対象そのものである</b>(タスク仕様)。
    /// </remarks>
    /// <remarks>
    /// <b>「毎日」から「走行後に1回」へ変えてよい理由。</b>旧検出器が毎日見ていたのは
    /// <c>world.Market</c>(その日の提示価格。翌日に上書きされる)を見ていたからである。
    /// <c>world.Ledgers</c> は<b>追記のみで剪定されない</b>ので、60日走行後に1回走査すれば
    /// 全日が対象になる。この理由を書かずに1回走査へ変えると、次の読者は「最終日しか見ていない」
    /// と読む(タスク仕様118行目)。
    /// </remarks>
    /// <remarks>
    /// <b>最初の違反で止まらない。</b>60日の全行を走査して最大比(約定単価 ÷ 床)を求め、
    /// 最後に1回だけ assert する(タスク仕様「周縁の緩みの定数」節)。
    /// </remarks>
    /// <remarks>
    /// <b>訂正(2026-09-21、レビュー1巡目)。</b>最大比の追跡(=どの行を assert 対象に選ぶか)を
    /// 浮動小数点の比較で行っていたのは誤りだった ── 「表示用」ではなく<b>合否に効く</b>
    /// (どの行が「最悪」として選ばれるかを決めている)。#120 を閉じる検出器そのものに
    /// 浮動小数点の比較を残す理由が無いため、<c>a/b &gt; c/d ⟺ a*d &gt; c*b</c>
    /// (両辺とも正の<c>int</c>なので不等号の向きは保たれる)による整数の交差乗算へ直した。
    /// 積は<c>long</c>で受ける(下記コード参照。<c>int.MaxValue</c>同士の積でも
    /// <c>long.MaxValue</c>未満に収まるためオーバーフローしない)。<b>失敗メッセージに比を
    /// 出すための整形だけ</b>は<c>double</c>を使う ── 比較・選択が整数で終わった後に
    /// メッセージ組み立てのためだけに換算するので、合否には影響しない。
    /// </remarks>
    /// <remarks>
    /// <b><c>PeripheralBandMultiplier</c> の実測(2026-09-21、フェーズ2)。</b>M0・60日走行での
    /// 全区画の最大比(約定単価 ÷ 床): seed1=2.5, seed2=2.3, seed3=2.425925…, seed7=2.018518…,
    /// seed42=2.5。最大値2.5の約2倍 = 5 を <c>PeripheralBandMultiplier</c> に置いた
    /// (4を超えていないので、置かずに止まる条件には当たらない)。
    /// </remarks>
    /// <remarks>
    /// <b>#149 の M-1 の定義(2026-09-21 の訂正。レビュー2巡目の象限I-b)。</b>本ファイル・
    /// 本テストプロジェクト内には他タスクの「M-1」が別に存在する
    /// (<see cref="UnaffordableNecessityCountsOnlyTheFundsShortfall"/> の remarks が指す
    /// #38 の M-1、<see cref="ExternalMarketTests"/> の M-1)── ここで言う M-1a/M-1b は
    /// **#149 のタスク仕様が定義するものだけ**を指す。
    /// <para>
    /// **初版の M-1(早期 return だけを戻す変異)は検出器として空洞だった。**
    /// <c>ExternalMarket.TryOfferPrice</c> の早期 return は <c>offerPrice = 0</c> を置くが、
    /// <c>ErrandPlanner</c> は戻り値を捨てて <c>EffectivePrice.Calculate(0, …)</c> へ渡すため、
    /// 提示価格1以上の検査で例外が飛び、帯の断定(1-a)には一度も到達しない見込みである
    /// (中心の近くの世帯が必需品の需要行を持つ日に必ず踏むため、5シードとも例外になる見込み)。
    /// 「赤」とだけ転記すると「天井が中心の約定を構造的に縛っていることが実測で確かめられた」
    /// と読まれるが、実際に確かめられるのは「例外を投げた」ことだけである。旧版の根拠
    /// (#120 の「シード2 は28日目に床の20倍」)も、旧検出器が <c>world.Market</c>(提示価格)で
    /// 測った値であり、#1-a が見る <c>world.Ledgers</c> の中心区画の買い手の <c>Purchase</c> 行とは
    /// 別物だったため誤りだった。
    /// </para>
    /// <para>
    /// **したがって M-1 は2形で実測する。** <c>mutator</c> は各形について、赤/緑だけでなく
    /// 「失敗の形」(帯の断定の assert か、例外か。例外なら型と発生箇所)を報告する。
    /// <list type="bullet">
    /// <item><b>M-1a</b>(初版どおり): <c>ExternalMarket.TryOfferPrice</c> の先頭に
    /// <c>if (!definition.IsPrimaryItem(itemId)) { offerPrice = 0; return false; }</c> **だけ**を
    /// 戻す。期待は赤だが、**帯の断定に到達しない見込み**(例外による赤)であり、**核心の
    /// 受け入れには使わない**。到達しないこと自体が実測の対象である。</item>
    /// <item><b>M-1b</b>(核心): #149 が外した3つの品目ゲートを同時に戻す(= #149 以前の
    /// 状態) ── <c>ExternalMarket.TryOfferPrice</c> の早期 return /
    /// <c>ErrandPlanner:242</c> の <c>&amp;&amp; _definition.IsPrimaryItem(itemId)</c> /
    /// <c>Observations.CollectWindow</c> の
    /// <c>if (!definition.IsPrimaryItem(itemId)) continue;</c>。**受け入れ条件は「赤」では
    /// なく「帯の断定(1-a)の assert で赤」であること**、かつどのシードが赤になったかを
    /// 報告に含める。</item>
    /// </list>
    /// M-1b が緑、または例外で赤になった場合は、天井が何にも守られていないことになるので、
    /// 転記せずに止まって報告する(タスク仕様の規定)。
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(2026-09-21、<c>mutator</c> が使い捨てworktreeで測定、対象コミット
    /// <c>afb4ddd</c>)。</b>期待と実測の食い違いは0件だった(裁定は不要)。
    /// <para>
    /// <b>M-1a(核心の受け入れには使わない形)。</b>赤。31件失敗。<b>失敗の形は全件
    /// <see cref="ArgumentOutOfRangeException"/></b>(<c>EffectivePrice.cs:38</c>、
    /// <c>offerPrice &lt;= 0</c>)。本テストは5シードすべて失敗したが、<b>帯の断定
    /// (<c>:557</c> の <c>Assert.True</c>)には一度も到達していない</b>。他に
    /// <c>ToolsAreTradedInThePipeline</c> / <c>SpatialFrictionSurvives</c> /
    /// <c>ObservationsDoNotGrowWithoutBound</c> /
    /// <c>LedgersReconcileWithLiquidFundsForEveryHousehold</c> なども同一例外で落ちた。
    /// <b>2巡目の読み(早期 return だけでは帯の断定に到達しない)が実測で裏付いた。</b>
    /// <b>この形の「赤」は天井について何も語らない</b> ── 落ちたテストのどれも、天井を
    /// 「守っている」ことの証拠にはならない。</para>
    /// <para>
    /// <b>M-1b(核心)。</b>赤。14件失敗。本テストは<b>5シードすべてが<c>:557</c>の帯の断定の
    /// <c>Assert.True</c>で落ちた(例外ではない)</b>。最大比(約定単価÷床):
    /// seed1=5.100 / seed2=4.870 / seed3=6.900 / seed7=11.000 / seed42=5.130。
    /// 失敗メッセージの実例(seed7): <c>seed=7 itemId=5 day=40 districtId=4(中心):
    /// 約定単価(110)が床(10)×2を上回った(最大比=11.000)。</c> 初版が根拠にしていた
    /// 「シード2」も実測で赤である(ただし根拠としてではなく実測としてであり、初版の
    /// 引用の仕方——旧検出器の値を援用した点——が正しかったことにはならない)。他に落ちた
    /// 9件: <c>ExternalMarketTests.WindowOfferPriceAppliesTheSeasonCoefficient</c> /
    /// <c>StoreChoiceTests.BuyerAtTheCentreChoosesTheWindowWhenLocalOffersExceedTheCeiling</c> /
    /// <c>StoreChoiceTests.CitySellerWinsTheTieAgainstTheWindowForCityGoods</c> /
    /// <see cref="UnaffordableNecessityCountsOnlyTheFundsShortfall"/> /
    /// <c>TradeSystemTests.ExportUsesTheSellerSideMarketReference</c> /
    /// <c>TradeSystemTests.SellerReferenceIsTakenBeforeTheSellableStockGate</c> /
    /// <c>ErrandPlannerTests.WindowIsACandidateForCityGoods</c> /
    /// <c>ObservationsTests.WindowObservationsCoverCityGoods</c> /
    /// <c>ObservationsTests.WindowObservationIsBornForEveryItem</c>。
    /// </para>
    /// <para>
    /// M-2〜M-4 の実測結果は、それぞれが守るテスト
    /// (<see cref="M0CalibrationTests.TradeMarginIsPositive"/> ・
    /// <see cref="WorldDefinitionTests.ExternalSellPriceOfCityGoodsIsDerivedFromBuyPrice"/> ・
    /// <c>ErrandPlannerTests.UnknownWindowPriceOfCityGoodsUsesTheFloor</c> ・
    /// <c>ObservationsTests.WindowObservationsCoverCityGoods</c> /
    /// <c>ObservationsTests.WindowObservationIsBornForEveryItem</c>)の doc コメントへ転記した。
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(42)]
    public void SettledPricesAtTheCentreStayWithinTheBandOverSixtyDays(long seed)
    {
        // 倍。GDD02d §3「帯 = [床, 床×2]」。ExternalSellPriceを読まない ── 検出器を交易マージン‰
        // から独立させる(タスク仕様テスト表 #3。マージンを動かしても本テストの閾値は動かない)。
        const int BandMultiplier = 2;

        // 倍。周縁の緩みの検出器の閾値(仕様値ではない。上記docコメントの実測値から
        // 「最大比2.5倍の約2倍」で置いた。GDD02c §1・GDD02d §2.2の「周縁は手間のぶん緩い」を
        // 数値の上界として実測しただけであり、GDD由来の定数ではない)。
        const int PeripheralBandMultiplier = 5;

        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(seed));

        var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(seed));
        scheduler.Advance(world, ticks: 60 * 24);

        bool anyCityGoodSettlement = false;
        bool anyCentreBuyerPurchase = false;

        // 1-b(水準・全区画)の最悪候補。
        bool hasWorstAny = false;
        int worstAnyPrice = 0, worstAnyFloor = 1, worstAnyItemId = 0, worstAnyDistrictId = 0;
        long worstAnyDay = 0;

        // 1-a(構造・中心の買い手)の最悪候補。
        bool hasWorstCentre = false;
        int worstCentrePrice = 0, worstCentreFloor = 1, worstCentreItemId = 0;
        long worstCentreDay = 0;

        foreach (var household in world.Households)
        {
            foreach (var entry in world.Ledgers[household.Id])
            {
                if (entry.Direction != LedgerDirection.Purchase)
                {
                    // Ledgersは記帳した側から見た向きを持つ(売った側の行は別の世帯に立つ)ので、
                    // 買い手の区画を見たい本テストはPurchase行だけを対象にする。
                    continue;
                }

                if (definition.IsPrimaryItem(entry.ItemId))
                {
                    continue; // 判定対象は都市生産品の行だけ(タスク仕様)。
                }

                anyCityGoodSettlement = true;

                int floor = definition.ExternalBuyPrice(entry.ItemId);
                long day = entry.OccurredAt.DayIndex;

                // 整数の交差乗算で比較する(a/b > c/d ⟺ a*d > c*b。floor・worstAnyFloorは
                // ExternalBuyPriceの検査により1以上なので符号は変わらない)。積はintを超えうる
                // ので long で受ける ── 両辺ともint.MaxValueでも積はlong.MaxValue未満に収まる
                // (2^31未満の2乗は2^62未満、long.MaxValueは2^63−1)。
                if (!hasWorstAny || (long)entry.UnitPrice * worstAnyFloor > (long)worstAnyPrice * floor)
                {
                    hasWorstAny = true;
                    worstAnyPrice = entry.UnitPrice;
                    worstAnyFloor = floor;
                    worstAnyItemId = entry.ItemId;
                    worstAnyDistrictId = household.DistrictId;
                    worstAnyDay = day;
                }

                if (household.DistrictId != District.ExternalMarketDistrictId)
                {
                    continue; // 1-a(構造)は中心の買い手だけを見る。
                }

                anyCentreBuyerPurchase = true;

                // 上と同じ整数の交差乗算。
                if (!hasWorstCentre
                    || (long)entry.UnitPrice * worstCentreFloor > (long)worstCentrePrice * floor)
                {
                    hasWorstCentre = true;
                    worstCentrePrice = entry.UnitPrice;
                    worstCentreFloor = floor;
                    worstCentreItemId = entry.ItemId;
                    worstCentreDay = day;
                }
            }
        }

        // 空振り防止(テスト表 #2)。(i)都市生産品の約定が1件以上、(ii)そのうち中心区画の
        // 買い手のPurchaseが1件以上 ── 経済が止まって0件で緑になること、および1-aの母集団が
        // 空で緑になることを防ぐ。
        Assert.True(
            anyCityGoodSettlement,
            $"seed={seed}: 60日回しても都市生産品の約定が1件も無い(値の問題の可能性)。");
        Assert.True(
            anyCentreBuyerPurchase,
            $"seed={seed}: 60日回しても中心区画の買い手のPurchaseが1件も無い"
                + "(配置か窓口の配線の問題の可能性)。");

        // 1-a: 構造。中心の買い手はBandMultiplier(=2)以下でなければならない。
        Assert.True(
            worstCentrePrice <= worstCentreFloor * BandMultiplier,
            $"seed={seed} itemId={worstCentreItemId} day={worstCentreDay} "
                + $"districtId={District.ExternalMarketDistrictId}(中心): "
                + $"約定単価({worstCentrePrice})が床({worstCentreFloor})×{BandMultiplier}を上回った"
                + $"(最大比={(double)worstCentrePrice / worstCentreFloor:F3})。");

        // 1-b: 水準。全区画はPeripheralBandMultiplier以下でなければならない。
        Assert.True(
            worstAnyPrice <= worstAnyFloor * PeripheralBandMultiplier,
            $"seed={seed} itemId={worstAnyItemId} day={worstAnyDay} "
                + $"districtId={worstAnyDistrictId}: 約定単価({worstAnyPrice})が "
                + $"床({worstAnyFloor})×{PeripheralBandMultiplier}を上回った"
                + $"(最大比={(double)worstAnyPrice / worstAnyFloor:F3})。");
    }

    /// <summary>
    /// テスト表 #24。60日回した後、<c>Knowledge</c> の総件数が
    /// NPC数 × (保持期間 + 1) × (世帯数 − 1 + 品目数) 以下(#149 訂正後。窓口の枠を含む。
    /// 下記remarks参照)。
    /// </summary>
    /// <remarks>
    /// <b>W2-09 追随(2026-09-20)。</b>境界を「NPC1人につき、他の世帯(売り手候補)ごとに
    /// 保持期間+1日ぶんまで」へ広げた。外出のたび、訪れた区画の全ての売り注文を観測する
    /// (GDD06 §3.1「観測は『見た』時点で生まれる」)ため、1人が複数の売り手を同時に知る
    /// ことが日常的になり、旧い境界(世帯数 × (保持期間+1) × 構成員数。「観測は買った相手からだけ
    /// 生まれる」という前提に立っていた)は本タスクの再設計で狭すぎる値になった。
    /// </remarks>
    /// <remarks>
    /// <b>本テストは <see cref="World.Knowledge"/> の件数の上界しか見ない。</b>価格の側は
    /// <see cref="SettledPricesAtTheCentreStayWithinTheBandOverSixtyDays"/>(W2-13)が見るようになった。
    /// <b>売り注文が2件へ枯れることは引き続き本テストは検出しない</b>
    /// (<see href="https://github.com/stama72/visionary/issues/38">#38</see> の窓口が入った後に
    /// #120 が締める)。<c>Errand.Surplus</c> を<see cref="long"/>にしたことで60日走行は緑に戻るが、
    /// それは型が広いあいだ通るだけであり、値付け(GDD02c §1)の発散そのものを止めたわけではない。
    /// </remarks>
    /// <remarks>
    /// <b>実測値(2026-09-21、W2-13の実装での再実測)。</b>M0・シード1・60日で
    /// <c>totalKnowledge</c> = 1200(#149 前は210)。<c>upperBound</c> は1440(訂正前の式)に対して
    /// 83%まで来ており、余裕は17%しかない。
    /// </remarks>
    /// <remarks>
    /// <b>訂正(2026-09-21、#149・仕様の訂正。フェーズ2)。</b>旧remarksは「窓口の観測が売り手
    /// 自身の相場基準の材料にもなり、日々の観測の<b>生成頻度そのものが上がっている</b>」と
    /// 書いていたが、これは事実と合っていない ── <see cref="MarketReference"/> は観測を
    /// <b>消費するだけで生成しない</b>。また旧upperBoundの式(NPC数×(世帯数−1)×(保持期間+1))は
    /// 「構造だけで決まる」と書いていたが誤りだった。式の(世帯数−1)に窓口が入っておらず、
    /// 窓口は「1売り手=1品目」ではなく1日に全品目の観測を生む(#149)ため、式は構造上界では
    /// なく経験的な線だった。<b>本テストは上界の式に窓口の枠を足して構造上界に戻す側を
    /// 採った</b>(上記コード参照。経験的な線のまま残す代替案は採らなかった)。
    /// <para>
    /// <b>実際に効いた要因を実測で切り分けた。</b>M0・シード1・60日の総数1200のうち、
    /// 窓口由来(<c>SellerId == HouseholdState.ExternalMarketSellerId</c>)が720、世帯由来
    /// (通常の売り手を見た観測)が480。窓口由来720は品目ごとに均等(1品目あたり80件)で、
    /// 1次産品4品目ぶん320・都市生産品5品目ぶん400に分かれる。
    /// <list type="bullet">
    /// <item><b>要因(a) 観測対象の品目数の増加。</b>都市生産品ぶんの400件は、#149以前は
    /// 窓口がそもそも都市生産品を観測させなかったので<b>まったく存在し得なかった</b>
    /// (品目フィルタの直接の効果)。</item>
    /// <item><b>要因(b) 中心への外出頻度の増加。</b>世帯由来だけで480あり、これは
    /// <b>#149前の総数(210)を単独で上回る</b>。世帯由来の観測は窓口の品目フィルタとは
    /// 無関係(他の世帯を見た観測であって窓口を見た観測ではない)なので、品目数の増加
    /// (2.25倍)ではこの480という値そのものを一切説明できない。窓口が都市生産品の候補になり
    /// 中心への外出そのものが増えたこと(ErrandPlannerの品目ゲートを外した効果)が、世帯由来・
    /// 窓口由来の両方を押し上げている。
    /// </item>
    /// </list>
    /// <b>したがって「2.25倍(品目数の比)」だけでは210→1200(5.7倍)を説明できない</b>という
    /// 指摘は正しく、要因(a)(400件、品目数の増加)と要因(b)(外出頻度の増加。世帯由来480件が
    /// その直接の証拠)の両方が効いている。
    /// </para>
    /// <para>
    /// <b>訂正(2026-09-21、レビュー3巡目)。</b>両要因の厳密な寄与の切り分け(#149前のコードでの
    /// 窓口由来・世帯由来の内訳)について、上の版は「旧コードが残っていないため再現できない」と
    /// 書いたが、これは事実として誤りだった。#149前のコードは <c>master</c>
    /// (<c>5da7e51</c> / マージコミット <c>2828bf8</c>)に残っており、<c>git worktree add</c> して
    /// 同じ計測を当てれば内訳は再現できる。**測っていない**というだけであり、測れないのではない。
    /// 本テストで測り直してはいない ── 要因の切り分けは<c>upperBound</c>の式の正当性(構造上界
    /// であること)には効かず、式は品目数・世帯数・NPC数・保持期間という構造だけで決まるため、
    /// 実測の内訳の値そのものに依存しない。
    /// </para>
    /// </remarks>
    [Fact]
    public void ObservationsDoNotGrowWithoutBound()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));

        var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));
        scheduler.Advance(world, ticks: 60 * 24);

        long totalKnowledge = world.Knowledge.Sum(observations => (long)observations.Count);

        // NPC1人が知りうる「都市内の」売り手は自分の世帯を除く(世帯数−1)戸まで。各売り手は
        // 自分の出力品目1件しか持たない(Recipeが出力1件を保証)ので、1日1件×保持期間+1日ぶん
        // (Observations.Expireが「差 > 保持期間」で消すので、差0〜保持期間の(保持期間+1)日ぶんが
        // 同時に残りうる)。
        //
        // 訂正(2026-09-21、#149・仕様の訂正)。窓口はこの(世帯数−1)に入っていない別枠であり、
        // かつ「1売り手=1品目」ではなく1日に全品目(ItemCount件)の観測を生む(#149で都市生産品
        // まで並べるようになったため)。窓口の枠を落とすと式は構造上界ではなく経験的な線になる
        // ため、窓口の枠(NPC1人につき品目数×(保持期間+1)日ぶん)を足して構造上界に戻した
        // (選択肢は2つ提示されており、こちらを採った。窓口を1つの「売り手」として数える
        // (世帯数−1)戸の枠とは独立に足すのは、窓口が実在の世帯Idを持たず、通常の売り手の
        // 「1日1件」という制約も受けないため)。
        long realSellerCount = definition.HouseholdCount - 1;
        long windowItemCount = definition.ItemCount; // 窓口は1日に全品目を観測させうる(#149)。
        long upperBound = (long)definition.NpcCount
            * (definition.ObservationRetentionDays + 1)
            * (realSellerCount + windowItemCount);

        Assert.True(
            totalKnowledge <= upperBound,
            $"Knowledgeの総件数({totalKnowledge})が上限({upperBound})を超えた"
                + "(保持期間の失効が効いていない可能性、または窓口の観測件数の想定が崩れた可能性)。");
    }

    /// <summary>
    /// テスト表 #25。必需だけが買える資金しか持たない世帯を作り(初期資金を絞った定義で1日回す)、
    /// 必需の約定が成立し、嗜好の約定が0件である。あわせて「資金さえあれば嗜好は買える」ことを
    /// 対照で確かめる ── 同じ世帯の初期資金だけを増やした世界で嗜好の約定が成立すること。
    /// </summary>
    /// <remarks>
    /// <b>W2-15 訂正4赤C(2026-09-22)。</b>本テストは「必需が嗜好より先に決済される(走査順)」を
    /// <b>検出しない。</b>帯<c>ScarceLiquidFunds</c>=50はビールの床72を下回るため、嗜好は走査順
    /// ではなく絶対額(資金不足)で塞がれている ── 70以下は薪の約定が成立しビールが0件、72以降は
    /// ビールも成立する世界であり、「必需は払えるが嗜好は走査順のせいで買えない」帯はこの世界に
    /// 存在しない。<c>mutator</c>による実測(M-6、2026-09-22): <c>TradeSystem</c>の需要行の走査を
    /// <c>demand.Lines.Reverse()</c>に反転しても本テストは<b>緑のまま</b>(赤になるのは
    /// <see cref="TradeSystemTests.NecessityIsSettledBeforePreference"/> /
    /// <see cref="TradeSystemTests.NecessityShortfallIsCountedOnBothPaths"/> /
    /// <see cref="TradeSystemTests.NonNecessityFundsShortfallIsNotCounted"/>の3件)。走査順の規則
    /// そのものは<see cref="TradeSystemTests.NecessityIsSettledBeforePreference"/>が機械で守って
    /// いる(同じ変異で赤)。帯の置き直しでは復元できない(M0の価格か世界の構成を変える必要があり
    /// 本タスクの外、issue化)。名前は変えない(本タスクが<c>NoPurchaseReason.MarketTerm</c>で
    /// 採った「名前は残しdocの1行が読み手を止める」形にあわせる)。
    /// </remarks>
    /// <remarks>
    /// <b>W2-11 追随(2026-09-20)。§1.1 の頭打ちが入って値付けが直ったことで、資金の絞り方・
    /// 観察に要する日数を実測し直した</b>(シード1・世帯Id0=Brewer・区画4)。
    /// <list type="bullet">
    /// <item>絞った資金(100): 1日目のうちに薪が約定する。3日目までビールは一度も約定しない
    /// (変わらず)。</item>
    /// <item>潤沢な資金(100,000): 薪もビールも1日目に約定する(1,000でも同じ)。</item>
    /// </list>
    /// <b><c>ScarceLiquidFunds</c> / <c>AmpleLiquidFunds</c> / 観察日数(3日)は動かさない。</b>
    /// 潤沢な側は1,000でも1日目にビールが約定するようになったが、
    /// <see href="https://github.com/stama72/visionary/issues/81">#81</see> の検出器の再校正は
    /// 判別力の再実測を伴う契約変更であり、本タスクでは実測値だけを書き換える
    /// (判別力は変異M-4で測り直す)。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(2026-09-20、<c>mutator</c> による測定、対象コミット <c>87d4837</c>、
    /// 変異M-4)。</b><see href="https://github.com/stama72/visionary/issues/81">#81</see>
    /// の検出器としての判別力が、<c>AmpleLiquidFunds</c> を1000 → 100,000、観察日数を2日 → 3日へ
    /// 広げたことで吸収されていないかを確かめるため、<c>TradeSystem.RunOneHouseholdsShopping</c>
    /// の <c>demand.Lines</c> の走査を <c>.Reverse()</c> する変異(製品コードで用途の走査順を
    /// 必需→耐久→入力→嗜好から逆順へ変える変異に相当)を当てた。絞った世界(資金100)の
    /// <c>Assert.False(boughtBeer, ...)</c> が失敗した(赤を確認: 嗜好が必需より先に
    /// 決済され、流動資金が先に嗜好へ回って必需を圧迫する経路が再現する)。
    /// <b>W2-11 の頭打ち(GDD02c §1.1)が入った後の経済でも判別力が維持されている。</b>
    /// <b>この測定は帯100(<c>ScarceLiquidFunds</c>=100)の配置についてのものであり、帯を50へ
    /// 置き直した本タスク(W2-15)で無効になった。M-6(2026-09-22)がこれを置き換える。</b>
    /// <b>旧い記録(2026-09-17、#98・<c>13e9251</c>、頭打ちが入る<b>前</b>の経済で測ったもの)は
    /// 参考として残す:</b>同じ変異で同じ <c>Assert.False(boughtBeer, ...)</c> が実際値trueで
    /// 失敗していた(赤を確認)。<b>今回(<c>87d4837</c>)の実測がこれを置き換える。</b>
    /// </remarks>
    /// <remarks>
    /// <b>帯の置き直し(2026-09-22、W2-15)。</b>決定10で必需(薪)の基礎値が現金上限から
    /// 窓口の当日価格(=床)へ落ち、必需の支出が大幅に下がった結果、旧い帯(絞った資金100)でも
    /// 嗜好(ビール)の約定まで成立するようになった(実測: <c>boughtBeer</c> が実際値true)。
    /// <b>帯の原意(必需1日分は払えるが必需+嗜好1日分は払えない流動資金)は変わらないので、
    /// 帯を置き直した。</b>M0・シード1・世帯Id0・3日で流動資金を段階的に振った実測:
    /// 1〜70は薪の約定が成立しビールの約定が0件のまま、72以降はビールの約定も成立する
    /// (境界は70と72の間)。安全側に寄せて <c>ScarceLiquidFunds</c> = 50 を採る。
    /// <c>AmpleLiquidFunds</c>(100,000)・観察日数(3日)は動かしていない(どちらも境界の外なので
    /// そのまま成り立つ)。
    /// </remarks>
    /// <remarks>
    /// <b>世帯の置き直し(2026-09-22、W2-17 追随)。</b>自家消費(#174)が入ったことで、世帯Id0
    /// (Brewer)は自分の出力(ビール)を自家消費でまかなうようになり、資金をいくら増やしても
    /// 外部/都市内の<c>Purchase</c>行を作らなくなった(対照ブロックが実際値falseで失敗した
    /// ── これは退行ではなく、まさに#174が意図した変化そのものである。パン屋・木材加工・醸造の
    /// いずれも「自分の出力品目」を対照に使うと同じことが起きる)。<b>本テストが見たいのは
    /// 「必需 vs 嗜好の資金配分」であって「自家供給の有無」ではないので、ビール(パン屋・醸造では
    /// 自家供給されうる)の自家供給と無関係な世帯へ差し替えた。</b>世帯Id6(Miller、区画8、
    /// 中心から離れる)を候補に、資金・観察日数を段階的に振って実測(シード1): 5日・資金50は
    /// パンの約定が成立しビールが0件のまま、5日・資金72以降はビールの約定も成立する(3日では
    /// 中心から遠いため資金1000でもビールが0件のままだったので観察日数を3日→5日へ広げた)。
    /// <c>ScarceLiquidFunds</c>(50)・<c>AmpleLiquidFunds</c>(100,000)は動かしていない
    /// (どちらも新しい境界の外なのでそのまま成り立つ)。
    /// </remarks>
    /// <remarks>
    /// <b>留め具(2026-09-23、レビュー1巡目)。</b>対象世帯の職業(の出力品目)がパン・ビールの
    /// いずれでもないことをassertで留める ── 帳簿の<c>Purchase</c>行で必需の成立を見る形(b)ではなく
    /// 職業そのものをassertする形(a)を選んだのは、必需(パン)の証拠を<c>HouseholdInventory</c>で
    /// 見る現行の作りを変えずに済み、配置の前提が崩れたことを1行で言い切れるため。
    /// </remarks>
    [Fact]
    public void NecessityIsSettledBeforePreference()
    {
        // W2-17 追随。世帯Id0(Brewer)は自家消費でビールを自給するため対照に使えない
        // (上記remarks参照)。Millerは必需(パン)・嗜好(ビール)のいずれも自分の出力ではない。
        const int TargetHouseholdId = 6;
        const int ObservationDays = 5; // 世帯Id6は中心区画から離れているため3日では短すぎる(remarks参照)。
        const int ScarceLiquidFunds = 50; // 必需は買えるが嗜好へは届かない額(値の検算対象、#28。W2-15で置き直した)
        const int AmpleLiquidFunds = 100_000; // 嗜好も届く額(対照。値の検算対象、#28。remarks参照)

        var definition = WorldDefinition.M0;

        // 絞った世界: 必需だけ成立し、嗜好は0件。
        {
            var world = WorldGenerator.Generate(definition, new RandomSource(1));
            world.Households[TargetHouseholdId].LiquidFunds = ScarceLiquidFunds;

            var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));
            scheduler.Advance(world, ticks: ObservationDays * 24);

            var household = world.Households[TargetHouseholdId];

            // 留め具(レビュー1巡目)。対象世帯がパン・ビールいずれの生産者でもないことをassertする
            // ── 配置の乱数消費が動く変更(世帯数・区画数・WorldGeneratorの順・
            // HouseholdsPerOccupation)で世帯Id6がパン屋またはビール屋になると、下のBread在庫の
            // assertは自家消費(#174)だけで購入0件でも真になり、必需側の証拠が空振りする
            // (上記remarks「世帯の置き直し」参照)。
            var producedItemId = definition.Recipes[(int)household.Occupation].Outputs[0].ItemId;
            Assert.True(
                producedItemId != Item.Bread && producedItemId != Item.Beer,
                $"世帯Id{TargetHouseholdId}の職業({household.Occupation})の出力が"
                    + $"パンまたはビール(itemId={producedItemId})になった"
                    + "(配置が変わり、自家消費だけで下のBread在庫assertが空振りする前提になった)。");

            // 必需(パン)の約定が成立した ── TradeSettlement.Executeが用途で行き先を振り分けるので、
            // 世帯在庫が増えていることが必需の約定の証拠になる(GDD02b §3.2)。初期の世帯在庫
            // (パン6、WorldDefinition.cs)は消費2/日(2人世帯)で3日ちょうど0になるので、値が
            // 残っていれば買い直した証拠になる(W2-15訂正4赤C。薪は初期28・3日で12しか消費しない
            // ため購入0件でも真になり空振りしていた ── 薪からパンへ差し替えた)。
            Assert.True(
                household.HouseholdInventory[Item.Bread] > 0,
                "必需(パン)の約定が成立しなかった(値の問題の可能性)。");

            bool boughtBeer = world.Ledgers[TargetHouseholdId].Any(
                entry => entry.Direction == LedgerDirection.Purchase && entry.ItemId == Item.Beer);
            Assert.False(boughtBeer, "嗜好(ビール)の約定が成立した(資金を絞った意味が無い)。");
        }

        // 対照: 同じ世帯の初期資金だけを増やすと、嗜好(ビール)の約定が成立する。
        {
            var world = WorldGenerator.Generate(definition, new RandomSource(1));
            world.Households[TargetHouseholdId].LiquidFunds = AmpleLiquidFunds;

            var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));
            scheduler.Advance(world, ticks: ObservationDays * 24);

            bool boughtBeer = world.Ledgers[TargetHouseholdId].Any(
                entry => entry.Direction == LedgerDirection.Purchase && entry.ItemId == Item.Beer);
            Assert.True(
                boughtBeer,
                "資金を増やしても嗜好(ビール)の約定が成立しなかった"
                    + "(予算判定以外で嗜好が塞がれている可能性)。");
        }
    }

    /// <summary>
    /// 【核心】テスト表 #26。必需の行で <c>UnaffordableNecessityCount</c> が厳密な期待値になる
    /// (資金不足の日は自然発生。下の<c>W2-09 追随</c>参照)。売り手の在庫が0で買えなかっただけの
    /// 世帯 → 0のまま。嗜好が買えなくても0のまま。翌日に買えたら0に戻る。
    /// <b>本テストは手順9の用途フィルタ(<c>Purpose == Necessity &amp;&amp;</c>)を判別しない</b>
    /// (別表D-1)。それを押さえるのは
    /// <see cref="TradeSystemTests.NonNecessityFundsShortfallIsNotCounted"/> である。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-17)。</b><c>fundsCap == 0</c> への置換(<c>actualQuantity == 0</c> など)
    /// は、<c>purchaseQuantityInUnits &gt;= 1</c>(段6 が既に保証)と <c>sellerStock &gt;= 1</c>
    /// (<c>StoreChoice.TrySelect</c> の条件3 が既に保証)の下では <c>fundsCap == 0</c> と
    /// 論理的に同値になり、外部からは区別できない(<c>actualQuantity == 0 ⟺ fundsCap == 0</c>)。
    /// 実際に効く変異は「候補が0件(知っている店が無い/在庫が無い)のとき、判定へ到達する前に
    /// <c>continue</c> する段2 の分岐そのものに、必需の行なら無条件で
    /// <c>household.UnaffordableNecessityCount++</c> を足す」形である。これを
    /// <c>TradeSystem.RunOneHouseholdsShopping</c> の <c>if (!_storeChoice.TrySelect(...))</c> の
    /// 中へ足したところ、在庫切れケースの
    /// <c>Assert.Equal(0, world.Households[0].UnaffordableNecessityCount)</c> が実際値1で
    /// 失敗した(赤を確認、#39の破産フラグが品切れ・情報不足の検出器になる経路)。変異を戻して
    /// 緑に復帰させた。
    /// </remarks>
    /// <remarks>
    /// <b>W2-09 追随(2026-09-20)。資金不足のケースを、流動資金を人為的に0へ落とす形から
    /// 自然発生(シード1・操作なし)の日へ差し替えた。</b><c>候補0件(知っている店が無い)</c> と
    /// <c>候補は見つかるが現金上限(CashCap)で落ちる</c> を人為的な資金操作なしに判別するには、
    /// CashCap が流動資金そのものではなく「用途に使える資金 ÷ 1日分の数量」(GDD02c §2.1)で
    /// 決まることを利用し、1日分の数量が大きい日にたまたま資金不足になる自然な日を探した
    /// (値そのものは仕様ではなく、実装が緑にできる自然な例でよい)。
    /// </remarks>
    /// <remarks>
    /// <b>W2-11 追随(2026-09-20)。§1.1 の頭打ちが入って値付けが直ったことで、自然発生する日・
    /// 世帯が動いた</b>(実測: 世帯Id3、10日目。60日間の走行で
    /// <c>UnaffordableNecessityCount &gt; 0</c> になる(世帯, 日)はこの1件だけ)。
    /// <b>この実測値は <see href="https://github.com/stama72/visionary/issues/38">#38</see>
    /// (都市外市場の窓口)が入れば動く。</b>売り注文が枯れる経済の中の自然発生日であることは
    /// 変わらないため、窓口が入って経済の形が変われば別の日・別の世帯へ動く。
    /// </remarks>
    /// <remarks>
    /// <b>別表D-1(2026-09-20)。用途フィルタ(<c>Purpose == Necessity &amp;&amp;</c>)の判別力は、
    /// このテストからは求めない。</b>2026-09-17 の追補remarksは、流動資金を人為的に0へ落とす
    /// 旧本体(世帯Id0、Brewer)で同変異が2 → 3で赤になったと記録していたが、この<c>2</c>は
    /// 旧本体の値である。自然発生の日(世帯Id2、17日目)へ差し替えた現本体へ同じ変異を当て直した
    /// ところ、3回のAssert(0/1/0)がいずれも変わらず、赤を確認できなかった ── その日は
    /// <c>Necessity</c> 以外の行が <c>fundsCap == 0</c> を踏まないためである。<b>構造的に踏みにくい</b>
    /// (手順9に到達するには段4のゲート(実効価格 ≤ 現金上限)を通っている必要があり、
    /// 現金上限 ≤ 用途に使える資金 ≤ 流動資金なので本来 <c>fundsCap ≥ 1</c> である。踏むのは
    /// 同じ世帯の先行する行が約定して <c>LiquidFunds</c> を減らした後だけであり、45世帯17日の
    /// 走行の中で偶然その組み合わせが出るのを待つ形は判別力が経済の状態に従属する
    /// ([#81](https://github.com/stama72/visionary/issues/81) と同じ穴)。<b>裁定は、探さずに
    /// 単体テストで構成すること</b> —
    /// <see cref="TradeSystemTests.NonNecessityFundsShortfallIsNotCounted"/> が必需の行で資金を
    /// ほぼ使い切らせてから嗜好の行が古い現金上限のゲートを通る世帯を手で組み、この変異に対する
    /// 判別力を持つ。
    /// </remarks>
    /// <remarks>
    /// <b>#38 追随(2026-09-20)。窓口が入って経済の形が変わり、自然発生する日・世帯が動いた</b>
    /// (実測: 世帯Id2、12日目に自然発生する。10日目・世帯Id3ではもう自然発生しない)。
    /// 60日間の走行で <c>UnaffordableNecessityCount &gt; 0</c> になる(世帯, 日)の組は多数に
    /// 増えた(窓口の輸入・輸出が入り経済が枯れなくなったため) ── そのうち最初に現れるものを
    /// 使う。この実測値も、輸出入の帯([#120](https://github.com/stama72/visionary/issues/120))が
    /// 変われば動く。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(2026-09-21、<c>mutator</c> が使い捨てworktreeで測定、対象コミット
    /// <c>39e367a</c>)。</b>#38 の M-1・M-4・M-5・M-6・M-7 の5つすべてで本テストが赤になった。
    /// 60日走行を毎日見る固定検出器は<b>どんな摂動でも赤になりうる</b> ──
    /// 本テストが赤になったことを、特定の契約が壊れた証拠として読んではならない。
    /// </remarks>
    /// <remarks>
    /// <b>#149 追随(2026-09-21)。窓口が都市生産品も売るようになり経済の形がまた変わったため、
    /// 自然発生する日・世帯が動いた</b>(実測: 世帯Id6、8日目に自然発生する。世帯Id2、12日目では
    /// もう自然発生しない ── 12日目時点で既に世帯Id6の資金不足が7日目から続いていたため、
    /// <c>Assert.Equal(0, …)</c> が11日目時点で崩れていた)。0→1→0のきれいな遷移を持つ最初の
    /// 組を60日走査して選び直した。この実測値も、窓口が絡む変更が入れば再び動きうる。
    /// </remarks>
    /// <remarks>
    /// <b>W2-15 追随(2026-09-22)。</b>相場項が無い日の基礎値が窓口の当日価格になった(決定10・11)
    /// ことで経済の形がまた変わり、自然発生する日・世帯が動いた(doc コメントの確立した手順
    /// どおり60日を走査。実測: 世帯Id4、7日目に0→1→0のきれいな遷移を持つ最初の組が現れる。
    /// 世帯Id6、8日目ではもう自然発生しない)。
    /// </remarks>
    /// <remarks>
    /// <b>W2-17 追随(2026-09-22)。</b>自家消費(#174)が入ったことで経済の形がまた変わり、
    /// 自然発生する日・世帯が動いた(doc コメントの確立した手順どおり60日を走査。実測:
    /// 世帯Id6、8日目に0→1→0のきれいな遷移を持つ最初の組が現れる。世帯Id4、7日目ではもう
    /// 自然発生しない)。
    /// </remarks>
    /// <remarks>
    /// <b>W2-22 追随(2026-09-24)。</b>出荷日数 1(#216)が入って経済の形がまた変わり、資金不足の
    /// 自然発生する(世帯, 日)が動いた(doc コメントの確立した手順どおり60日を走査。実測:
    /// 世帯Id4、14日目に0→1→0のきれいな遷移を持つ最初の組が現れる。世帯Id6、8日目ではもう
    /// 自然発生しない)。<b>在庫切れ・嗜好の2ブロックは手作りの世界(在庫切れ)・実測ずみの
    /// (世帯, 日)(世帯Id8・0日目、嗜好)ともに無改修で緑のままだった</b>(実測、2026-09-24)。
    /// </remarks>
    /// <remarks>
    /// <b>W2-24 追随(#237、2026-09-26)。</b>規則3(必需の取り置き・運転資金 =
    /// <c>max(0, 目標在庫 − 予想在庫) × 相場基準</c>)が入り必需が資金で止まる世帯日が
    /// 5.5%→0.6%に下がった(#218決定ログ2)ことで経済の形がまた変わり、自然発生する(世帯, 日)が
    /// 動いた(doc コメントの確立した手順どおり60日を走査。実測: 世帯Id3、16日目に0→1→0の
    /// きれいな遷移を持つ最初の組が現れる。世帯Id4、14日目ではもう自然発生しない)。在庫切れ・
    /// 嗜好の2ブロックは無改修で緑のままだった。
    /// </remarks>
    [Fact]
    public void UnaffordableNecessityCountsOnlyTheFundsShortfall()
    {
        var definition = WorldDefinition.M0;

        // 資金不足のケース(シード1・操作なし。世帯Id3、16日目に自然発生する。上のremarks参照)。
        {
            var world = WorldGenerator.Generate(definition, new RandomSource(1));
            var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));

            scheduler.Advance(world, ticks: 15 * 24); // 15日目まで。
            Assert.Equal(0, world.Households[3].UnaffordableNecessityCount);

            scheduler.Advance(world, ticks: 24); // 16日目。資金不足が1件自然発生する。
            Assert.Equal(1, world.Households[3].UnaffordableNecessityCount);

            scheduler.Advance(world, ticks: 24); // 17日目。毎日上書きする(GDD02b §3.3)ので0に戻る。
            Assert.Equal(0, world.Households[3].UnaffordableNecessityCount);
        }

        // 在庫切れのケース。木工2戸の薪(工房在庫)と入力の木材(工房在庫)を0にして生産による
        // 補充も止め、資金は潤沢にする ── 知っている店が0件になり(StoreChoiceの条件3)、
        // 資金の項(fundsCap)へは到達しない(GDD02b §3.2「売り手の在庫が尽きたのは資金不足では
        // ない」)。
        {
            var world = WorldGenerator.Generate(definition, new RandomSource(1));

            foreach (var household in world.Households)
            {
                if (household.Occupation == Occupation.Woodworker)
                {
                    household.WorkshopInventory[Item.Firewood] = 0;
                    household.WorkshopInventory[Item.Timber] = 0;
                }
            }

            world.Households[0].LiquidFunds = 10_000;

            var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));
            scheduler.Advance(world, ticks: 24);

            Assert.Equal(0, world.Households[0].UnaffordableNecessityCount);
        }

        // 嗜好が買えなくても0のまま。W2-15追随(2026-09-22、訂正2)。決定10・11で基礎値が
        // 窓口の当日価格へ落ちたことで、素のM0世界1日目・世帯Id1はビールを買うようになった
        // (実測、シード1)。裁定の第一案どおり60日を走査し、(a)その日に嗜好(ビール)を買っていない
        // /(b)その日に必需を1件以上約定している/(c)UnaffordableNecessityCount==0を満たす
        // 最初の(世帯, 日)を選び直した ── 世帯Id8・0日目(実測)。(b)は、ビールを買わなかった
        // 理由が「店を1つも知らない」側に落ちていないことを示す。
        // <b>機構(実測)。</b>世帯Id8は0日目に必需(パン2個・穀物14個)を約定させている
        // (店を知らないのではない)。ビールは知っている店(実効価格144)が見つかったが、
        // BuyerBudget.Decideの理由はCashCap(現金上限)でも在庫圧力0(StockPressurePermille=1500、
        // 非0)でもなく、決定11の基礎値ゲート(MarketTerm。実効価格144 >
        // ApplyPermille(基礎値72, 1500‰)=108)である ── 相場項が無い日の基礎値(窓口の当日
        // 価格=72)に対して実効価格が高すぎたため、資金不足ではなく「高すぎて買わなかった」
        // 経路で0個になった。UnaffordableNecessityCountはNecessityの行しか数えないので0のまま
        // (GDD02b §3.2)。
        {
            var world = WorldGenerator.Generate(definition, new RandomSource(1));
            var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));
            scheduler.Advance(world, ticks: 24);

            bool boughtBeer = world.Ledgers[8].Any(
                entry => entry.Direction == LedgerDirection.Purchase && entry.ItemId == Item.Beer);
            Assert.False(boughtBeer);
            Assert.Equal(0, world.Households[8].UnaffordableNecessityCount);

            // R-1(訂正3の別表)。裁定表の選び直しの条件(b)「その日に必需を1件以上約定している」
            // は、これまでdocコメントの実測として書くだけで断定していなかった ── 世帯Id8が
            // 「店を1つも知らない」状態へ落ちても上の2つのAssertは通ってしまう。断定として足す
            // (実測: 上記remarksのとおりパン2個・穀物14個が0日目に約定している)。
            Assert.Contains(
                world.Ledgers[8],
                entry => entry.Direction == LedgerDirection.Purchase && entry.OccurredAt.DayIndex == 0);
        }
    }

    /// <summary>
    /// テスト表 #27。同じシードで2つの世界を30日回し、<c>StateHasher</c> の値が一致する。
    /// </summary>
    [Fact]
    public void TradeIsDeterministicAcrossRuns()
    {
        var definition = WorldDefinition.M0;

        ulong RunAndHash()
        {
            var world = WorldGenerator.Generate(definition, new RandomSource(1));
            var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));
            scheduler.Advance(world, ticks: 30 * 24);

            return StateHasher.Compute(world);
        }

        Assert.Equal(RunAndHash(), RunAndHash());
    }

    /// <summary>
    /// テスト表 #28。30日回した後、同一品目・同一日について、買い手の区画ごとの約定単価の集合が
    /// 一致しない(存在命題。<b>「空間の摩擦が消えたら落ちる」テストではない</b>。下記remark)。
    /// </summary>
    /// <remarks>
    /// <b>「日」ではなく「区画」の違いを見る</b>(レビュー1巡目 I-b の訂正)。単純に品目ごとの
    /// 全期間・全区画の価格を1つの集合へ <c>SelectMany</c> して <c>Distinct</c> すると、区画の
    /// 区別がその時点で消える ── 提示価格は毎日動くので、全買い手が同じ区画に居ても
    /// 「価格の種類が複数ある」という結果になり、移動費を実質コストに乗せ忘れる変異
    /// (区画によらず最安値の売り手へ全員収束する)を当てても真のままになる。
    /// 同一品目・同一日に絞って区画ごとの価格<b>集合</b>を比べることで、区画そのものが
    /// 価格差を生んでいるかを見る。<b>閾値を置かない</b>(値の調整(#28)で揺れうるため)。
    /// </remarks>
    /// <remarks>
    /// <b>以下は #37 当時(#98 の前)の実測であり、当てた先のコードは現行には無い。</b>
    /// #98 が移動費を実効価格から外し(GDD06 §2「移動は外出ごとに1回払う。品目ごと・単位ごとには
    /// 払わない」)、<c>TravelCostPerUnit</c> と <c>isKnown</c> はどちらも削除された。
    /// <b>現行コードで何が本テストを拘束しているかは測っていない</b> ── 測り直しは
    /// <see href="https://github.com/stama72/visionary/issues/145">#145</see>(#37 申し送り3 の引き取り)。
    /// #38 で候補集合の構造が変わった(中心区画に窓口が常時1件ある)ので、交絡も当時とは違う。
    ///
    /// <b>この構成でも判別力は回復していない(#37 の3巡目の実測。seed 1・30日・全317件に対して
    /// フルスイートで測り直した)。</b>「実質コストから移動費を落とす」と「<c>realCost = price</c>」は
    /// <see cref="StoreChoice.TrySelect"/> の実質コスト算出箇所が1か所しか無いため、
    /// <b>同一のコード変更になる</b>(2巡目の表はこの2行に異なる結果を載せており誤っていた)。
    /// <see cref="StoreChoice.TrySelect"/> への変異でいずれも本テストは緑のままだった:
    ///
    /// | 当てた変異 | このテスト | 赤になったテスト |
    /// | ---------- | ---------- | ---------------- |
    /// | <c>realCost = price</c>(実質コストから移動費を落とす。argmin を提示価格にする ──
    /// 上記の理由でこの2つは同一の変更) | 緑 | #10のみ |
    /// | <c>isKnown</c> を常に真 + 距離を固定(候補集合と移動費を全買い手で共通化) | 緑 | #12・#15
    /// (#24 は緑のまま。2巡目の表は #24 も赤としていたが誤りだった) |
    /// | 上の2つを同時に(買い手の区画が店の選択に一切効かない) | 緑 | #10・#12・#15
    /// (2巡目の表は「─」としていたが誤りだった) |
    ///
    /// <b>W2 で残る価格差の主因は在庫枯渇の順序であって、空間の摩擦ではない。</b>買い手の区画が
    /// 店の選択から完全に消えても、売り切れの起き方が日ごと・区画ごとに違えば約定単価の集合は
    /// 一致する保証が無い。<b>当時 argmin を守っていた単体テストは #98 で置き換わっており、
    /// いまどのテストが守っているかは未確認である</b>(#145 が測る。狭い側に倒して
    /// 名前を書かない ── 実在しないテスト名を「守っている」と書いた記述が #38 まで残っていた)。
    /// 本テストを「空間の摩擦の検出器」として読まないこと ── 本テストが主張できるのは
    /// 「統合系で区画ごとの約定単価が一致しない」という存在命題までであり、原因の分解ではない。
    /// </remarks>
    [Fact]
    public void SpatialFrictionSurvives()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));

        var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));
        scheduler.Advance(world, ticks: 30 * 24);

        // (itemId, dayIndex) → (買い手の区画 → その日その品目で約定した単価の集合)。
        var pricesByItemDayAndDistrict = new Dictionary<(int ItemId, long DayIndex), Dictionary<int, HashSet<int>>>();

        foreach (var household in world.Households)
        {
            foreach (var entry in world.Ledgers[household.Id])
            {
                if (entry.Direction != LedgerDirection.Purchase)
                {
                    continue;
                }

                var key = (entry.ItemId, entry.OccurredAt.DayIndex);

                if (!pricesByItemDayAndDistrict.TryGetValue(key, out var byDistrict))
                {
                    byDistrict = new Dictionary<int, HashSet<int>>();
                    pricesByItemDayAndDistrict[key] = byDistrict;
                }

                if (!byDistrict.TryGetValue(household.DistrictId, out var prices))
                {
                    prices = new HashSet<int>();
                    byDistrict[household.DistrictId] = prices;
                }

                prices.Add(entry.UnitPrice);
            }
        }

        // 同一品目・同一日のなかで、区画ごとの価格集合が互いに一致しない組が1つでもあれば真。
        bool anyDayHasDistrictPriceDisagreement = pricesByItemDayAndDistrict.Values.Any(byDistrict =>
        {
            var priceSetsByDistrict = byDistrict.Values.ToList();

            for (int i = 1; i < priceSetsByDistrict.Count; i++)
            {
                if (!priceSetsByDistrict[i].SetEquals(priceSetsByDistrict[0]))
                {
                    return true;
                }
            }

            return false;
        });

        Assert.True(
            anyDayHasDistrictPriceDisagreement,
            "30日回しても、同一品目・同一日について区画ごとの約定単価の集合が常に一致した"
                + "(存在命題が成立しなかった。値の問題の可能性。上記remark参照 ── この失敗は"
                + "『空間の摩擦が消えた』ことを直接は意味しない)。");
    }

    private static Recipe TimberToFlourRecipe() =>
        new(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = Item.Timber, Quantity = 2 } },
            laborPermille: 1000);

    /// <summary>
    /// テスト表 #20(#38)。<c>IsExportEnabled = false</c> で60日回すと外部 <c>Sale</c> が0件。
    /// 輸入は起き続ける。
    /// </summary>
    /// <remarks>
    /// <b>M-2非対象(measure only)。</b>世帯は中心区画に住み、木材(1次産品)を入力に薪(都市生産品)
    /// を作る ── 輸出も輸入も移動なしで毎日成立する配置にすることで、「輸出を切っても輸入まで
    /// 止まっていない」ことを空間の摩擦抜きで確かめる。実験軸そのものが輸入まで止める(フラグを
    /// 読んでいない)実装ミスで本テストが落ちる(タスク仕様)。<see cref="WorldDefinition.M0"/> は
    /// 使わない ── <c>BuildM0</c> は <c>isExportEnabled</c> を明示的に渡さない(既定 <c>true</c>)
    /// ため、M0を経由してこの軸を反転させる手段が無い(#38タスク仕様「決めたこと」)。
    /// </remarks>
    [Fact]
    public void ExportCanBeTurnedOff()
    {
        var definition = EconomySystemTestFixtures.BuildDefinition(
            TimberToFlourRecipe(),
            opportunityCostBaseByOccupation: new[] { 1, 1, 1, 1, 1 },
            rankCoefficientPermille: new[] { 1000, 1000, 1000 },
            travelHoursPerDistrict: 1,
            inputBufferDays: 1,
            shipmentDays: 1,
            equipmentPermilleWithoutTools: 1000, // 工具の有無を本テストの関心から外す。
            isExportEnabled: false);

        var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);
        world.Npcs[0].Rank = NpcRank.Master;
        world.Households[0] = new HouseholdState(
            id: 0, districtId: District.ExternalMarketDistrictId, headNpcId: 0,
            memberNpcIds: new[] { 0 }, itemCount: Item.Count);
        world.Households[0].Occupation = Occupation.Miller;
        world.Households[0].LiquidFunds = 100_000;

        var scheduler = new SimScheduler(
            new ISimSystem[] { new ProductionSystem(definition), new TradeSystem(definition) },
            new RandomSource(1));

        scheduler.Advance(world, ticks: 60 * 24);

        bool anyExternalSale = false;
        bool anyExternalPurchase = false;

        foreach (var entry in world.Ledgers[0])
        {
            if (entry.CounterpartyId != HouseholdState.ExternalMarketSellerId)
            {
                continue;
            }

            if (entry.Direction == LedgerDirection.Sale)
            {
                anyExternalSale = true;
            }
            else
            {
                anyExternalPurchase = true;
            }
        }

        Assert.False(anyExternalSale, "IsExportEnabled=falseでも輸出(外部Sale)が起きた。");
        Assert.True(anyExternalPurchase, "輸入(外部Purchase)まで止まった(フラグが輸入まで読んでいる可能性)。");
    }

    /// <summary>
    /// テスト表 #21(#38)。60日で工具の <c>Purchase</c> が1件以上
    /// (<a href="https://github.com/stama72/visionary/issues/38">#37</a> 申し送り2)。
    /// </summary>
    /// <remarks>
    /// <b>本テストは #38 の検出器ではない。</b>実測(タスク仕様「設計の前提」)のとおり master
    /// (<c>010afa0</c>)でも120日で工具の約定は4〜6件あり、#38 が無くても成り立つ。
    /// <b>耐久の枝(段5b の <c>BuyerBudget.QuantityInUnits</c>)の回帰ガードである。</b>
    /// </remarks>
    [Fact]
    public void ToolsAreTradedInThePipeline()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));

        var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));
        scheduler.Advance(world, ticks: 60 * 24);

        bool anyToolPurchase = world.Ledgers.Any(entries => entries.Any(
            entry => entry.Direction == LedgerDirection.Purchase && entry.ItemId == Item.Tools));

        Assert.True(anyToolPurchase, "60日で工具のPurchaseが一度も成立しなかった(値の問題の可能性)。");
    }

    /// <summary>その日の <c>world.Market</c> のうち、指定品目のキー件数を数える。</summary>
    private static int CountMarketOffers(World world, int itemId)
    {
        int count = 0;

        foreach (var key in world.Market.Keys)
        {
            if (key.ItemId == itemId)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 失敗メッセージ用。職業Id昇順(ADR-0002)に「職業=担い手数」を並べる
    /// (<see cref="EveryOccupationKeepsAtLeastOneCarrierOverSixtyDays"/> が違反日の職業分布を
    /// 記録するのに使う)。
    /// </summary>
    private static string OccupationDistributionSnapshot(WorldDefinition definition, World world)
    {
        var snapshot = new System.Text.StringBuilder();

        for (int occupationId = 0; occupationId < definition.OccupationCount; occupationId++)
        {
            var occupation = (Occupation)occupationId;

            if (occupationId > 0)
            {
                snapshot.Append(' ');
            }

            snapshot.Append($"{occupation}={OccupationReassignment.CarrierCount(world, occupation)}");
        }

        return snapshot.ToString();
    }

    /// <summary>
    /// 【核心】#39裁定D-C(2026-09-23)。M0・シード1/2/3/7/42・60日。旧
    /// <c>ToolOffersNeverDisappearOverSixtyDays</c>(W2-14 タスク仕様テスト表 #10)の置き直し。
    /// 各日の終わりに、5職業すべてについて <see cref="OccupationReassignment.CarrierCount"/> が
    /// 1以上(= GDD02b §4.2「最後の1世帯は付け替えない」が供給の消滅を構造的に防ぐことを、
    /// 品目ではなく職業を主語にして機械で守る)。
    /// </summary>
    /// <remarks>
    /// <b>旧テストの日次断定(工具の売り注文が毎日1件以上)は、④の下では成り立たず、成り立たせても
    /// ならない。</b>④で鍛冶に入ったばかりの世帯は入力(鉄鉱石・木炭)を持たないので生産できず、
    /// 工具在庫は留保の1個だけで販売在庫0になる ── GDD02b §4.1 が「②が発火した翌日にはほぼ必ず
    /// 販売在庫が尽きる」と書いた状態そのものである。日次下限を維持する手は冷却期間か④の抑制
    /// しかなく、どちらもGDD02b §4.1が却下済みの調整軸である(#39裁定D-C)。
    /// </remarks>
    /// <remarks>
    /// <b>置き直した理由(実測、2026-09-23、<c>a38b5b2</c>)。</b>シード7では day 33 の時点で、
    /// day 0 の鍛冶2戸(household2・household9)がどちらも鍛冶を降りており、唯一の鍛冶は
    /// household8 である(工具在庫1 = <see cref="SellableStock"/> の留保ぶんだけ)。経路は
    /// GDD02b §4.2の規則どおりで、担い手0は一度も起きていない
    /// (household2が④で降りる[2→1] → household8が④で入る[1→2] → household9が④で降りる[2→1]。
    /// 「最後の1世帯は付け替えない」がhousehold8を守る)。<b>「担い手0を防ぐ」は守られており、
    /// 「担い手2を保つ」は初めから誰も保証していない</b>(GDD02b §4.2「ただし防いでいるのは
    /// 担い手0だけであり、2→1への減少は防がない」)。旧テストは「職業が不変だった世界で
    /// 『担い手2が続くこと』」を暗黙の母集団にしていた。
    /// </remarks>
    /// <remarks>
    /// <b>これは緩めた置き直しではない。</b>GDD02b §4.2の「最後の1世帯は付け替えないが供給の消滅を
    /// 構造的に防ぐ」を、テストが初めて機械で守ることになる。品目ではなく職業を主語にするので、
    /// 工具に限らず5品目すべての供給消滅を1本で拾う。
    /// </remarks>
    /// <remarks>
    /// <b>旧remarksのうち残すもの(#39裁定D-Cの指示)。</b>変異M-1(<c>mutator</c>、2026-09-21、
    /// HEAD <c>0da66a4</c>)の実測: <c>SellableStock.ReserveQuantity</c> が常に0を返す(留保を消す)
    /// 変異を当てると、旧テストはシード7のみ赤で、シード1・2・3・42は緑のままだった ── 留保がある
    /// 世界では工房在庫1→販売在庫0で段1が売り注文を立てないので「0件の日」は在庫が1個まで痩せた
    /// 兆候になるが、M-1で留保そのものを消すと工房在庫1でも売り注文は立つため、0件になるのは
    /// 在庫が0の日だけになる(期待の向きが逆になる)。<b>旧テストの断定は、どの変異(M-1〜M-6)でも
    /// 担保されていなかった</b>(留保の核心は<see cref="SmithNeverRunsOutOfToolsOverSixtyDays"/>
    /// [全5シード赤]が持つ)。これが日次下限を落としてよい根拠そのものである。
    /// </remarks>
    /// <remarks>
    /// <b>④が実際に発火することの断定(#39レビュー3巡目 #21、2026-09-23)。</b>裁定D-A・D-B・D-Cが
    /// 固定値と日次下限を落とした根拠は「④が職業分布を動かす」の1点だが、置き直した3本
    /// (D-A・D-B・D-C)はどれも④が1度も発火しない世界でも緑になる(D-Aは6/180/6で下限を満たし、
    /// D-Cは全職業2のままで担い手 ≥ 1 を満たす)。④が到達不能になった日、残るのは理由の消えた
    /// 緩い検出器である。そこで本テストへ「60日のうち少なくとも1日、職業分布がday 0と異なる」
    /// ことを断定として足す。<b>実測(2026-09-23、最初に職業分布がday 0と異なった日)</b>:
    /// seed=1: day10 / seed=2: day17 / seed=3: day9 / seed=7: day11 / seed=42: day10。
    /// 全5シードで60日以内に分布が動いたため、5シードすべてに断定を足す。
    /// </remarks>
    /// <remarks>
    /// <b>核心C-2の追加測定(<c>mutator</c>、2026-09-23、<c>8bdd6ee</c>)。</b>
    /// <see cref="OccupationReassignment.TrySelectTarget"/> 冒頭の <c>CarrierCount(...) &lt;= 1</c>
    /// 早期return(「最後の1世帯は付け替えない」の保護)を丸ごと落とす変異(核心C-2。
    /// <see cref="HouseholdSystemTests.LastCarrierOfAnOccupationIsNeverReassigned"/> が受け入れ対象)
    /// を当てると、本テストは seed=7・seed=42 で赤になった。担い手0を検出した位置:
    /// seed=42: day=23 Miller(以降day 25・27・29・31もMiller)、day=41 Woodworker、day=45 Woodworker。
    /// seed=7: day=36 Smith(以降day 39・42・45・48・51・54・57・60もSmith)。
    /// <b>C-2の受け入れ条件は引き続き
    /// <see cref="HouseholdSystemTests.LastCarrierOfAnOccupationIsNeverReassigned"/> が落ちることである
    /// ── 本テストの60日走行の赤は測定値であって、特定の契約が壊れた証拠として読まない。</b>
    /// </remarks>
    /// <remarks>
    /// <b>「④が実際に発火することの断定」を削除した(W2-24 #237、2026-09-26)。</b>規則3
    /// (必需の取り置き・運転資金 = <c>max(0, 目標在庫 − 予想在庫) × 相場基準</c>)により、必需が
    /// 資金で止まる世帯日が5.5%→0.6%に下がった(#218決定ログ2)。実測(2026-09-26、5シードとも
    /// 60日間、職業分布がday 0から一度も動かなかった): seed=1/2/3/7/42のいずれも
    /// <c>distributionEverDiverged</c>が偽。上の2026-09-23の実測(day10/17/9/11/10で分布が動いた)は
    /// 規則3を適用する前の値であり、規則3の下では再現しない。タスク仕様(W2-24 §9手順3)の指示
    /// どおり、全シードが偽になったため引数を足さずに断定ごと削除した。<b>M0 の値では60日で④が
    /// 発火しない。担い手 ≥ 1 の核心は空振りしており、規則は
    /// <see cref="HouseholdSystemTests.LastCarrierOfAnOccupationIsNeverReassigned"/> が守る。</b>
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(42)]
    public void EveryOccupationKeepsAtLeastOneCarrierOverSixtyDays(long seed)
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(seed));
        var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(seed));

        // 空振り防止。day 0 に5職業すべてが2戸(WorldGeneratorの初期配置の確認。
        // #39裁定D-C「Assert.Equal(2, smithHouseholdCount)(day 0)は『day 0 に5職業すべてが2戸』へ
        // 広げる」)。
        for (int occupationId = 0; occupationId < definition.OccupationCount; occupationId++)
        {
            var occupation = (Occupation)occupationId;
            int carrierCount = OccupationReassignment.CarrierCount(world, occupation);

            Assert.True(
                carrierCount == 2,
                $"seed={seed} day=0 occupation={occupation}: 担い手が{carrierCount}戸"
                    + "(WorldGeneratorの初期配置は5職業とも2戸のはず)。");
        }

        var violationDays = new List<int>();
        var violationOccupations = new List<Occupation>();
        var violationDistributionSnapshots = new List<string>();
        long totalToolOfferCount = 0;
        int zeroToolOfferDayCount = 0;

        for (int day = 1; day <= 60; day++)
        {
            scheduler.Advance(world, ticks: 24);

            // 診断値(断定しない。#39裁定D-C)。60日間の工具の売り注文の延べ件数と0件だった日。
            int toolOfferCount = CountMarketOffers(world, Item.Tools);
            totalToolOfferCount += toolOfferCount;
            if (toolOfferCount == 0)
            {
                zeroToolOfferDayCount++;
            }

            // 核心。5職業すべてについて担い手が1以上(最初の違反で止めない。60日を走り切り、
            // 違反した日・職業・その日の職業分布を記録して最後に1回だけassertする)。
            for (int occupationId = 0; occupationId < definition.OccupationCount; occupationId++)
            {
                var occupation = (Occupation)occupationId;
                int carrierCount = OccupationReassignment.CarrierCount(world, occupation);

                if (carrierCount < 1)
                {
                    violationDays.Add(day);
                    violationOccupations.Add(occupation);
                    violationDistributionSnapshots.Add(OccupationDistributionSnapshot(definition, world));
                }
            }
        }

        Assert.Equal(60, world.Now.DayIndex);

        var violationDetails = new System.Text.StringBuilder();
        for (int i = 0; i < violationDays.Count; i++)
        {
            violationDetails.Append(
                $" day={violationDays[i]} occupation={violationOccupations[i]} "
                    + $"分布=[{violationDistributionSnapshots[i]}];");
        }

        Assert.True(
            violationDays.Count == 0,
            $"seed={seed}: ある職業の担い手が0になった日があった(GDD02b §4.2「最後の1世帯は"
                + $"付け替えない」が破れた可能性)。{violationDetails}"
                + $"(診断: 60日間の工具の売り注文の延べ件数={totalToolOfferCount} / "
                + $"0件だった日={zeroToolOfferDayCount}日。留め具ではなく診断のみ)。");
    }

    /// <summary>
    /// 【核心】W2-14 タスク仕様テスト表 #11(訂正版。2026-09-21)。M0・シード1/2/3/7/42・60日。
    /// 各日の終わりに、鍛冶2戸それぞれの工房在庫[工具]が1以上(= 設備係数‰が1000を保つ、
    /// GDD02a §3)。
    /// </summary>
    /// <remarks>
    /// <b>初版のテスト11(後半30日のうち生産回数が正の日が1日以上)は検出器として空洞だった</b>
    /// (タスク仕様「テスト11 の訂正」)。留保は仕様どおり効いていて工具在庫は60日間一度も0に
    /// ならないのに、鍛冶は day 7〜8 に鉄鉱石・木炭(原材料)を切らして以後生産0が続くため、
    /// 初版は変異M-1(留保を消す)の有無に関わらず5シードすべてで赤だった。原材料の枯渇は
    /// 留保が作った経路ではなく master でも day 8 に0になる。訂正後は#148が実測した詰みの
    /// 機構そのもの(工具切れ → 設備係数500‰ → 能力0 → 復帰しない)を見る。#148の閉じる条件の
    /// 1つ目(生産の復帰)は本テストでは満たさない ── 資金と入力の枯渇による停止は留保と無関係
    /// であり、直すには値付け・購入判定・資金の側に触れる(#148決定1が却下した範囲)。この実測は
    /// [#30](https://github.com/stama72/visionary/issues/30) の懸念2へ渡した。
    /// </remarks>
    /// <remarks>
    /// <b>変異M-1の実測</b>(<c>mutator</c>、2026-09-21、HEAD <c>0da66a4</c>)。<c>SellableStock.
    /// ReserveQuantity</c> が常に0を返す(留保を消す)変異を当てると、本テストは全5シード
    /// (1/2/3/7/42)が赤になった ── 留保の核心はこのテストが担保する。
    /// </remarks>
    /// <remarks>
    /// <b>健全な鍛冶の工房在庫[工具]の定常値の実測</b>(2026-09-21、60日走行、シード
    /// 1/2/3/7/42。day 60 時点)。seed=1: household2=4, household3=4。seed=2: household3=4,
    /// household7=4。seed=3: household1=6, household7=6。seed=7: household2=4, household9=4。
    /// seed=42: household0=4, household2=6。#148 の紙の予測(留保1 + 閾在庫3 = 4)は seed=3 の
    /// 2戸と seed=42 の1戸で6になり食い違う(値・原因の推測はどちらも実測していないので書かない)。
    /// </remarks>
    /// <remarks>
    /// <b>#39裁定D-A(iii)(2026-09-23)。</b>空振り防止 (iii)「60日間の工具の売り注文の延べ件数
    /// ≥ 60」を断定から落とした。核心の母集団は<b>day 0 の鍛冶2戸</b>だが、(iii) が数えるのは
    /// <b>都市全体の工具の売り注文</b>であり、④(<see cref="OccupationReassignment"/>による職業
    /// 付け替え、#39)が入ると売り注文を出しているのは別の世帯になりうる(同じテストの中で
    /// 核心が主張する集合と空振り防止が数える集合がずれる)。核心が空振りでないことは
    /// (i)(鍛冶2戸存在)・(ii)(60日走行)・上の変異M-1の実測(全5シード赤)が既に担保しており、
    /// (iii) はそれらに対する冗長な代理指標だった。(iii) は断定から外すが、失敗メッセージの
    /// 診断値としては残す。<b>実測(2026-09-23、④を通した後)</b>: seed=1: 76 / seed=2: 93 /
    /// seed=3: 72 / seed=7: 51 / seed=42: 74(④が発火したseed=7だけ旧下限60を下回る)。
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(42)]
    public void SmithNeverRunsOutOfToolsOverSixtyDays(long seed)
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(seed));
        var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(seed));

        int[] smithHouseholdIds = world.Households
            .Where(household => household.Occupation == Occupation.Smith)
            .Select(household => household.Id)
            .ToArray();

        var hasMinToolStock = new bool[smithHouseholdIds.Length];
        var minToolStock = new int[smithHouseholdIds.Length];
        var minToolStockDay = new long[smithHouseholdIds.Length];
        long totalToolOfferCount = 0;

        for (int day = 1; day <= 60; day++)
        {
            scheduler.Advance(world, ticks: 24);

            for (int i = 0; i < smithHouseholdIds.Length; i++)
            {
                int toolStock = world.Households[smithHouseholdIds[i]].WorkshopInventory[Item.Tools];

                if (!hasMinToolStock[i] || toolStock < minToolStock[i])
                {
                    hasMinToolStock[i] = true;
                    minToolStock[i] = toolStock;
                    minToolStockDay[i] = day;
                }
            }

            totalToolOfferCount += CountMarketOffers(world, Item.Tools);
        }

        // 空振り防止(タスク仕様テスト表 #12。#39裁定D-Cで
        // EveryOccupationKeepsAtLeastOneCarrierOverSixtyDaysへ置き直された旧
        // ToolOffersNeverDisappearOverSixtyDaysと同じ(i)(ii))は核心と独立なので先に置く。
        Assert.Equal(2, smithHouseholdIds.Length);
        Assert.Equal(60, world.Now.DayIndex);

        for (int i = 0; i < smithHouseholdIds.Length; i++)
        {
            Assert.True(
                minToolStock[i] >= 1,
                $"seed={seed} householdId={smithHouseholdIds[i]} day={minToolStockDay[i]}: "
                    + $"工房在庫[工具]が{minToolStock[i]}まで痩せた(設備係数500‰への転落。"
                    + "留保が段5b・段6を素通りした可能性)。");
        }

        // #39裁定D-A(iii)。空振り防止 (iii)(延べ件数60以上)は断定から外した ──
        // 核心の母集団はday 0の鍛冶2戸だが(iii)が数えるのは都市全体の工具の売り注文であり、
        // ④が入ると売り注文を出すのは別の世帯になりうる(鍛冶2戸が担保していた母集団と
        // ずれる。詳細はメソッドのremarks参照)。診断値としてのみ出力する。
        _output.WriteLine(
            $"seed={seed}: 60日間の工具の売り注文の延べ件数={totalToolOfferCount}(診断のみ。断定しない)。");
    }

    /// <summary>#173 の3条件を day 1〜30 について評価した結果。</summary>
    private sealed class CitySurvivalScan(World world)
    {
        /// <summary>
        /// 走行後の <see cref="World"/>(コンストラクタで受け取ったのと同じ参照。走行は
        /// <see cref="ScanThirtyDays"/> がこの世界の上で進める)。#174 テスト表 #7 の核心が、
        /// 記録した観測(<see cref="SelfSuppliableObservations"/>)を最終日の実際の在庫と
        /// 突き合わせるために持つ(レビュー3巡目の指摘の修正。理由は
        /// <see cref="TradePipelineTests.OwnOutputIsNeverHoardedWhileTheHouseholdGoesWithout"/>
        /// のremarks参照)。
        /// </summary>
        public World World { get; } = world;

        /// <summary>条件1(都市の生産回数の合計 > 0)が破れた日。1始まり。</summary>
        public List<int> ProductionStoppedDays { get; } = new();

        /// <summary>条件2(都市生産品の都市内約定件数 > 0)が破れた日。1始まり。</summary>
        public List<int> NoInternalSettlementDays { get; } = new();

        /// <summary>条件3(世帯在庫が空の世帯が全世帯にならない)が破れた日。1始まり。</summary>
        public List<int> AllHouseholdsEmptyDays { get; } = new();

        /// <summary>
        /// #174 テスト表 #7。自家供給できる世帯(目標日数が正の出力品目を持つ世帯)の
        /// (世帯, 出力品目, 日)の組ごとに、世帯在庫・工房在庫の値を持つ観測。<b>条件の成否に
        /// かかわらず必ず記録する</b>。day 0 は自家供給6戸 × 出力品目1 × 30日で180件になるが、
        /// #39裁定D-Aのとおり④の職業付け替えが職業分布を動かすため、実際の総件数は180に固定
        /// されない(下の<see cref="ScannedSelfSuppliableEntryCount"/>参照)。
        /// </summary>
        /// <remarks>
        /// <b>なぜ記録と絞り込みを分けたか(2巡目レビュー指摘の修正)。</b>旧版は「抱え込みの条件に
        /// 当たったときだけ<c>HoardedWhileEmptyEntries</c>へ<c>Add</c>する」形だった。この形では
        /// 「連言(世帯在庫0 かつ 工房在庫正)が0件」という1本の断定しか持てず、<b>経済が健全だから
        /// 0件なのか、走査そのものが壊れていて0件なのかを区別できない</b>。#7の条件は
        /// <see cref="SelfConsumption.TransferQuantity"/>の式の帰結として成立する(上の
        /// <see cref="OwnOutputIsNeverHoardedWhileTheHouseholdGoesWithout"/>のremarks参照)ので、
        /// 「走査が実際に回ったこと」は連言の断定とは別の留め具で留めるほかない。<b>そこで走査は
        /// 評価した観測を全件記録し、<see cref="HoardedWhileEmptyEntries"/>はこの観測列の上の
        /// 絞り込みとして導出する</b>(走査ループの中で条件付きに<c>Add</c>するのをやめた)。
        /// </remarks>
        /// <remarks>
        /// <b>目標日数0の品目を弾く理由(訂正、レビュー2巡目)。</b>旧版は「含めても恒等的に空である」
        /// と書いていたが偽である ── 製粉は<c>HouseholdInventory[Flour]</c>が常に0(消費表が0・
        /// 移動対象外・買う経路も無い)で<c>WorkshopInventory[Flour]</c>は生産後に正になるため、
        /// この<c>continue</c>を外すと製粉と鍛冶で毎日のように記録される(この絞り込みは検出器の
        /// 範囲を実際に狭めている)。正しい理由は「目標日数0の品目は自家消費の対象外なので、
        /// 『世帯在庫0 × 工房在庫正』は病理(抱え込み)ではなく、ただの生産物の在庫である」こと
        /// (テスト表 #9)。
        /// </remarks>
        public List<(int HouseholdId, int ItemId, int Day, int HouseholdStock, int WorkshopStock)>
            SelfSuppliableObservations
        { get; } = new();

        /// <summary>
        /// <see cref="SelfSuppliableObservations"/>の件数。<b>#39裁定D-A(2026-09-23)で
        /// <c>Assert.Equal(180, ...)</c>の固定値assertは下限断定(日次観測件数 ≥ 3 / 延べ観測件数
        /// ≥ 90)へ置き替えた</b> ── ④の職業付け替えが職業分布を動かすため、6戸が6戸のまま
        /// 続くことを前提にできない(理由は
        /// <see cref="TradePipelineTests.OwnOutputIsNeverHoardedWhileTheHouseholdGoesWithout"/>の
        /// remarks参照)。<b>判別力は180 → 90へ落ちた。</b>自家供給3戸ぶんまで走査が縮む変異
        /// (1日あたりの記録を3件までに切り詰める)は、日次下限3・延べ下限90・day 0の
        /// <see cref="SelfSuppliableHouseholdCount"/>・最終日の実在庫との突き合わせループの
        /// いずれも通り抜ける(実測、2026-09-23。この変異を一時的に当てて全5シード緑のまま
        /// 通過することを確認し、戻した)。この件数が捕まえるのは「走査が丸ごと止まる」
        /// 「観測の記録そのものが消える」といった全滅型の変異に限られる。
        /// </summary>
        public int ScannedSelfSuppliableEntryCount => SelfSuppliableObservations.Count;

        /// <summary>
        /// #174 テスト表 #7(核心)。<see cref="SelfSuppliableObservations"/>のうち
        /// <see cref="TradePipelineTests.IsHoardedWhileEmpty"/>が真の(世帯Id, 品目Id, 日)。
        /// 観測列の上の絞り込みとして導出する(上のremarks参照。条件付き<c>Add</c>はしない)。
        /// </summary>
        public List<(int HouseholdId, int ItemId, int Day)> HoardedWhileEmptyEntries =>
            SelfSuppliableObservations
                .Where(o => TradePipelineTests.IsHoardedWhileEmpty(o.HouseholdStock, o.WorkshopStock))
                .Select(o => (o.HouseholdId, o.ItemId, o.Day))
                .ToList();

        /// <summary>
        /// 自家供給できる世帯数(M0はパン屋2・木材加工2・醸造2の6戸。空振り防止に使う)。
        /// </summary>
        public int SelfSuppliableHouseholdCount { get; set; }

        /// <summary>30日の延べ生産回数(全世帯・全日の合計)。空振り防止に使う。</summary>
        public long TotalProductionRuns { get; set; }

        /// <summary>30日の延べ都市内約定件数(都市生産品)。空振り防止に使う。</summary>
        public long TotalInternalSettlements { get; set; }

        /// <summary>世帯数。空振り防止に使う。</summary>
        public int HouseholdCount { get; set; }

        /// <summary>走行後の world.Now.DayIndex。空振り防止に使う。</summary>
        public long FinalDayIndex { get; set; }

        /// <summary>
        /// 30日ぶんの都市内約定件数(都市生産品)を、日付で絞らずに向き・相手・品目だけで数えた値。
        /// <see cref="TotalInternalSettlements"/>(日別の合計)と一致するはずの日付の検算用
        /// (W2-16 タスク仕様 6.2)。<c>dayIndex</c>がずれると、実際の<c>DayIndex</c>のうちどの回の
        /// 絞り込みにも一致しない行が生まれてこの値と食い違いうる ── 1行は1つの<c>dayIndex</c>にしか
        /// 属さないので、起きるのは取りこぼしだけであり、<b>二重に数えられることはない</b>。
        /// </summary>
        /// <remarks>
        /// <b>訂正(W2-16 タスク仕様 6.2)。</b>この等値assertは±1のずれを<b>構造では</b>守らない。
        /// −1方向(<c>dayIndex</c>が1小さくなる)で落ちる行があるのは<c>DayIndex==29</c>に都市内約定
        /// が残っているシードだけである。
        /// <b>訂正(W2-22、#216)。</b>出荷日数1(<a
        /// href="https://github.com/stama72/visionary/issues/210">#210</a>決定1)を入れた新しい
        /// 基準値では、条件2の違反日が5シードとも0日になった ── 核心
        /// (<see cref="NoInternalSettlementDays"/>が空)が成り立つ限り、帳簿日<c>DayIndex==29</c>
        /// (走査ラベルではday 30)には都市内約定が必ず1件以上ある。<b>ただしコード順では、この
        /// 等値assertは核心より先に評価される</b>(<see
        /// cref="InternalSettlementsOfCityGoodsNeverDisappearOverThirtyDays"/>の
        /// assertは条件別の空振り防止 → 本等値assert → 核心の順)。−1方向のずれ(V-12: 帳簿の
        /// 絞り込みを<c>== dayIndex</c>から<c>== dayIndex - 1</c>へ変える)では、走査が帳簿日
        /// <c>DayIndex==29</c>の行を一度も参照しなくなるため、日別合計
        /// (<see cref="TotalInternalSettlements"/>)がこの等値assertの相手側
        /// (<see cref="TotalCityGoodInternalRowsIgnoringDate"/>、日付を無視した全件数)より小さく
        /// なり、<b>5シードとも、核心へ到達する前にこの等値assertが赤になる</b>(実測はV-12。
        /// 下記「変異の実測」参照)。<b>核心自身がこの取り違えを検出できなくなるという意味では
        /// ない</b> ── 走査ラベルday 1(<c>DayIndex==0</c>の回)の内部約定も0件になる見込みで
        /// 核心の<see cref="NoInternalSettlementDays"/>も空でなくなるはずだが、xUnitは最初に
        /// 落ちたassertで止まるため、<b>実際に赤を報告するのは等値assertのほうである</b>。これは
        /// 本assert(等値検算)が構造で守るようになったという意味ではない ── 守っているのは核心の
        /// ほうであり、核心が緑であることが前提である。構造で留めているのは
        /// <see cref="FirstScannedLedgerDayIndex"/>・
        /// <see cref="LastScannedLedgerDayIndex"/>(下記、W2-16 タスク仕様 6.4)のほうである。
        /// </remarks>
        public long TotalCityGoodInternalRowsIgnoringDate { get; set; }

        /// <summary>
        /// 走査が違反日として記録しうる最初のラベル(= ループが最初に<c>Add(day)</c>へ渡す<c>day</c>)。
        /// データを参照せず、ループが実際に使った値をそのまま記録する(W2-16 タスク仕様 6.4)。
        /// </summary>
        public int FirstDayLabel { get; set; }

        /// <summary>
        /// 走査が違反日として記録しうる最後のラベル(= ループが最後に<c>Add(day)</c>へ渡す<c>day</c>)。
        /// 毎回上書きするので、走行後は最終回の値が残る(W2-16 タスク仕様 6.4)。
        /// </summary>
        public int LastDayLabel { get; set; }

        /// <summary>
        /// 帳簿の絞り込みに実際に使った最初の<c>DayIndex</c>(= ループが最初に使った<c>dayIndex</c>)。
        /// データを参照せず、ループが実際に使った値をそのまま記録する(W2-16 タスク仕様 6.4)。
        /// </summary>
        public long FirstScannedLedgerDayIndex { get; set; }

        /// <summary>
        /// 帳簿の絞り込みに実際に使った最後の<c>DayIndex</c>(= ループが最後に使った<c>dayIndex</c>)。
        /// 毎回上書きするので、走行後は最終回の値が残る(W2-16 タスク仕様 6.4)。
        /// </summary>
        public long LastScannedLedgerDayIndex { get; set; }

        /// <summary>ITestOutputHelper へ流す1行(W2-16 タスク仕様「4. 基準値を読む口」の書式)。</summary>
        public string Format(long seed)
        {
            static string DaysList(List<int> days) => string.Join(", ", days);

            return $"seed={seed} 条件1: 違反{ProductionStoppedDays.Count}日 "
                + $"(day {DaysList(ProductionStoppedDays)}) / "
                + $"条件2: 違反{NoInternalSettlementDays.Count}日 "
                + $"(day {DaysList(NoInternalSettlementDays)}) / "
                + $"条件3: 違反{AllHouseholdsEmptyDays.Count}日 "
                + $"(day {DaysList(AllHouseholdsEmptyDays)}) / "
                + $"延べ生産回数={TotalProductionRuns} / 延べ都市内約定={TotalInternalSettlements} / "
                + $"#174観測件数={ScannedSelfSuppliableEntryCount} / "
                + $"#174抱え込み件数={HoardedWhileEmptyEntries.Count}";
        }
    }

    /// <summary>
    /// #174 テスト表 #7の判定式。ある観測(世帯在庫・工房在庫の値)について、抱え込み
    /// (世帯が自分の出力品目を切らしているのに工房在庫は残っている)かどうかを返す。
    /// <see cref="CitySurvivalScan.HoardedWhileEmptyEntries"/>が観測列を絞り込むのに使う
    /// (走査ループの中に条件式をインラインで書かない。2巡目レビュー指摘の修正)。
    /// </summary>
    private static bool IsHoardedWhileEmpty(int householdStock, int workshopStock) =>
        householdStock == 0 && workshopStock > 0;

    /// <summary>
    /// <see cref="IsHoardedWhileEmpty"/>の単体テスト。合成の観測値を直接与えるので、走行を伴わずに
    /// 判定式の取り違え(<c>== 0</c>→<c>&lt; 0</c>、<c>&gt; 0</c>→<c>&lt; 0</c>、左右の在庫の
    /// 入れ替え)を検出する(#174 レビュー2巡目の裁定)。
    /// </summary>
    [Theory]
    [InlineData(0, 3, true)] // 世帯在庫0・工房在庫正 → 抱え込み。
    [InlineData(0, 0, false)] // 世帯在庫0・工房在庫0 → 抱え込みではない(在庫自体が無い)。
    [InlineData(5, 3, false)] // 世帯在庫正・工房在庫正 → 抱え込みではない(世帯が困っていない)。
    [InlineData(5, 0, false)] // 世帯在庫正・工房在庫0 → 抱え込みではない。
    public void IsHoardedWhileEmptyMatchesTheHoardingDefinition(
        int householdStock, int workshopStock, bool expected)
    {
        Assert.Equal(expected, IsHoardedWhileEmpty(householdStock, workshopStock));
    }

    /// <summary>
    /// #173 の3条件(生産・都市内約定・世帯在庫)を M0・30日について1回の走行でまとめて評価する。
    /// 最初の違反で止めない(W2-16 タスク仕様「1. 走査」手順3。既存の60日検出器と同じ規律)。
    /// </summary>
    /// <remarks>
    /// <b>シードが効く経路は <see cref="WorldGenerator"/> だけである。</b><c>FullPipeline</c>の4系統
    /// (<c>ProductionSystem</c>・<c>ConsumptionSystem</c>・<c>HouseholdSystem</c>・<c>TradeSystem</c>)
    /// はいずれも
    /// <c>SimContext.OpenRandom</c>を呼ばないので、<c>SimScheduler</c>に渡すシードは結果に影響しない。
    /// 「5シードで見た」は<see cref="WorldGenerator.Generate"/>(世界生成)の5通りを見たという意味である
    /// (W2-16 タスク仕様 6.3 #1、レビュー2巡目 指摘2)。
    /// </remarks>
    /// <remarks>
    /// <b>反転した検出器が原理的に見ないもの(W2-16 タスク仕様 6.1)。</b>核心が「違反日が1日以上ある
    /// (= 存在)」なので、母数を減らす方向の取り違えは違反日を<b>増やす</b>だけで、反転側の核心は
    /// 緑のまま通ってしまう。反転側が捕まえられるのは「数え過ぎ(違反日が消える)」と
    /// 「全滅(母数が常に0)」だけであり、「数え落とし」は条件別の空振り防止にも反転側の核心にも
    /// 引っ掛からない。<b>条件3は正側
    /// (<see cref="SomeHouseholdAlwaysHoldsNecessitiesOverThirtyDays"/>)を持つため両方向を見ていた
    /// (W2-16当時)。</b>正側は核心の向きが逆(= 0)なので、数え落とし(違反日が現れる)を正側の核心が
    /// 捕まえる。<b>W2-17(自家消費、#174)で条件3の5シードすべてが直り、反転側
    /// (<c>AllHouseholdsRunEmptyWithinThirtyDays</c>)は空になったため削除した。</b>
    /// <b>訂正(レビュー2巡目)。</b>これは「非対称の解消」ではない ── むしろ向きの違う死角を
    /// 生んだ。正側の核心(<c>AllHouseholdsEmptyDays.Count == 0</c>)と条件別の空振り防止
    /// (<c>Count &lt; 30</c>)は、どちらも違反日が「増える」方向しか見ない。反転側だけが持っていた
    /// 「数え過ぎ」方向の検出力(条件3の判定を<c>HouseholdInventory</c>→<c>WorkshopInventory</c>に
    /// 取り違える変異。W2-16の記録ではM-8で、当時の正側4シードでは緑だった)は反転側の削除と
    /// 一緒に失われた。この変異が通ると、パン屋が売れ残りのパンを抱えて飢えている状態
    /// (#174そのものの病理)が「空でない」と判定され、条件3は「直った」と報告され続ける。
    /// <b>M-8の実測(実測日 2026-09-22、<c>mutator</c> が使い捨てworktreeで測定、対象コミット
    /// <c>13144cf</c>)。</b><c>ScanThirtyDays</c>の条件3の判定が読む在庫を
    /// <c>HouseholdInventory</c>→<c>WorkshopInventory</c>に変える変異は<b>緑のまま</b>だった
    /// (<c>dotnet test</c> 477件全通過)。W2-16時点(当時の正側4シード)でも緑であり、傾向は
    /// 変わっていない。<b>いまの5シード(1/2/3/7/42)の組では、条件3の検出器は「世帯在庫が
    /// 全品目0」と「工房在庫が全品目0」を判別する力を持たない。</b>これはW2-17が反転側を
    /// 削除したことで構造として守るものが無くなった結果であり(正側の核心
    /// <c>Count == 0</c>も空振り防止<c>Count &lt; 30</c>も、どちらも違反日が増える方向しか
    /// 見ない)、<b>退行ではなく既知の限界の再確認である。</b>構造的な留め具は本タスクでは足さない
    /// ── それは#173の持ち物であり、フェーズ3がissueへ切り出す予定である。
    /// <b>訂正(W2-22、#216)。</b>条件1・条件2は本節がここまで前提にしてきた「#174 の後も
    /// 反転側のみのまま」からもう外れている ── W2-22で出荷日数1(#210決定1)が入り、5シードとも
    /// 違反日0になったため正側へ移った(反転側の2メソッドは削除済み)。<b>「向きが反転しきって
    /// 条件1・2にも正側が立てば、それについては解消する」という上の一文は誤りだった。</b>実際に
    /// 正側が立っても、上で述べた「数え落とし」方向の非対称は消えない ── 数え過ぎ方向が見えなく
    /// なり、数え落とし方向が見えるようになるだけであり、<b>これは非対称の解消ではなく、死角の
    /// 入れ替えである</b>(W2-22タスク仕様「向きが変わると、検出器が見るものが変わる」。条件3で
    /// 先に起きたのと同じ入れ替えが、条件1・条件2でも起きた)。条件1・条件2それぞれの向き別の
    /// 結論(M-1/M-10の実測)と、検出器自身の空洞化(V-5/V-6)は各テストのdocコメントを参照。
    /// <para>
    /// <b>変異の実測(<c>mutator</c> が測定、2026-09-22)がこの非対称を裏づける。</b>
    /// 数え落とし方向の取り違え(M-6: 条件2の向き<c>Purchase</c>→<c>Sale</c> / M-7: 条件2の品目範囲を
    /// 0〜8へ広げる / M-10: 条件1の合計を<c>world.Households[0]</c>の1戸だけにする)は
    /// <b>3件とも緑のまま</b>だった。捕まったのは数え過ぎ方向(M-1: 条件1の<c>ProductionRuns</c>を
    /// <c>Math.Max(1, runs)</c>にする / M-8: 条件3の判定を<c>WorkshopInventory</c>にする)と
    /// 全滅型(M-2 / M-11 / M-12)だった。
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <b>6.4 の留め具の変異の実測(<c>mutator</c> が測定、2026-09-22)。</b>
    /// <c>FirstDayLabel</c> / <c>LastDayLabel</c> / <c>FirstScannedLedgerDayIndex</c> /
    /// <c>LastScannedLedgerDayIndex</c> は4検出器メソッド共通の空振り防止であり、この4本の実測は
    /// どのメソッドでも同一である。
    /// <list type="bullet">
    /// <item><b>M-13</b>(<c>dayIndex = day - 1;</c> を <c>day - 2</c> へ変える)は<b>赤</b>。
    /// 4本すべて・全15インスタンスが落ちた。落ちたのは
    /// <c>Assert.Equal(0, scan.FirstScannedLedgerDayIndex)</c>(<c>Last</c>より先に評価されるため)。</item>
    /// <item><b>M-14</b>(走査ループを <c>for (int day = 0; day &lt; 30; day++)</c> へ変える)は
    /// <b>赤</b>。4本すべて・全15インスタンスが落ちた。落ちたのは
    /// <c>Assert.Equal(30, scan.LastDayLabel)</c>(<c>FirstDayLabel</c>はday 1の回が来るので
    /// 1のまま残るため)。</item>
    /// </list>
    /// このずれは6.4を足す前は4本とも緑で通っていた(レビュー3巡目の指摘)。データを1行も
    /// 参照せずに落ちている。
    /// </remarks>
    private static CitySurvivalScan ScanThirtyDays(long seed)
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(seed));
        var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(seed));

        var scan = new CitySurvivalScan(world);

        // #174 テスト表 #7。自家供給できる世帯数(day 0 = WorldGeneratorの初期配置の値。走行前に
        // 1回だけ数える。HouseholdSystem(#39)が順3で職業を書き換えうるので、この値は day 0 の
        // ものであり「30日間ずっとこの数」という意味ではない。#39裁定D-A参照)。
        foreach (var household in world.Households)
        {
            var recipe = definition.Recipes[(int)household.Occupation];
            bool selfSuppliable = false;

            foreach (var output in recipe.Outputs)
            {
                if (SelfConsumption.TargetStockDays(definition, output.ItemId) > 0)
                {
                    selfSuppliable = true;
                    break;
                }
            }

            if (selfSuppliable)
            {
                scan.SelfSuppliableHouseholdCount++;
            }
        }

        for (int day = 1; day <= 30; day++)
        {
            scheduler.Advance(world, ticks: 24);

            // Advance(24)のk回目の直後はDayIndex==k。終わったばかりの日はDayIndex==k-1
            // (W2-16タスク仕様「順序・境界」)。
            long dayIndex = day - 1;

            // ラベルと窓の端を、ループが実際に使った値としてそのまま記録する(定数を代入すると
            // 恒真になって何も守らない。W2-16 タスク仕様 6.4)。Firstは最初の回だけ書き、Lastは
            // 毎回上書きするので走行後は最終回の値が残る。
            if (day == 1)
            {
                scan.FirstDayLabel = day;
                scan.FirstScannedLedgerDayIndex = dayIndex;
            }

            scan.LastDayLabel = day;
            scan.LastScannedLedgerDayIndex = dayIndex;

            // 条件1: 全世帯のProductionRunsの合計。
            long productionRuns = 0;
            foreach (var household in world.Households)
            {
                productionRuns += household.ProductionRuns;
            }

            scan.TotalProductionRuns += productionRuns;
            if (productionRuns == 0)
            {
                scan.ProductionStoppedDays.Add(day);
            }

            // 条件2: 当日の都市内約定件数(Purchase・相手が窓口以外・都市生産品4〜8)。
            long internalSettlements = 0;
            foreach (var household in world.Households)
            {
                foreach (var entry in world.Ledgers[household.Id])
                {
                    if (entry.OccurredAt.DayIndex == dayIndex
                        && entry.Direction == LedgerDirection.Purchase
                        && entry.CounterpartyId != HouseholdState.ExternalMarketSellerId
                        && entry.ItemId >= Item.Flour && entry.ItemId <= Item.Tools)
                    {
                        internalSettlements++;
                    }
                }
            }

            scan.TotalInternalSettlements += internalSettlements;
            if (internalSettlements == 0)
            {
                scan.NoInternalSettlementDays.Add(day);
            }

            // 条件3: 「パン・薪・ビールが3品目とも0」の世帯が全世帯かどうか(論理積)。
            bool allHouseholdsEmpty = true;
            foreach (var household in world.Households)
            {
                bool householdEmpty = household.HouseholdInventory[Item.Bread] == 0
                    && household.HouseholdInventory[Item.Firewood] == 0
                    && household.HouseholdInventory[Item.Beer] == 0;

                if (!householdEmpty)
                {
                    allHouseholdsEmpty = false;
                    break;
                }
            }

            if (allHouseholdsEmpty)
            {
                scan.AllHouseholdsEmptyDays.Add(day);
            }

            // #174 テスト表 #7。自分の出力品目(自家供給できる品目に限る)について、世帯在庫・
            // 工房在庫の値を条件の成否にかかわらず必ず記録する(絞り込みはCitySurvivalScan.
            // HoardedWhileEmptyEntriesが観測列の上で行う。2巡目レビュー指摘の修正)。
            foreach (var household in world.Households)
            {
                var recipe = definition.Recipes[(int)household.Occupation];

                foreach (var output in recipe.Outputs)
                {
                    if (SelfConsumption.TargetStockDays(definition, output.ItemId) <= 0)
                    {
                        continue;
                    }

                    scan.SelfSuppliableObservations.Add((
                        household.Id,
                        output.ItemId,
                        day,
                        household.HouseholdInventory[output.ItemId],
                        household.WorkshopInventory[output.ItemId]));
                }
            }
        }

        // 日付の検算(W2-16 タスク仕様 6.2)。日別に数えた合計(TotalInternalSettlements)と、
        // 日付で絞らずに全帳簿を1回走査した値が一致するはずである。dayIndexがずれると、どの回の
        // 絞り込みにも一致しない行が生まれてこの値と食い違いうる(30日の窓の外に行は存在しないので、
        // 素の実装では一致する)。1行は1つのdayIndexにしか属さないので、起きるのは取りこぼしだけで
        // あり、二重に数えられることはない。
        //
        // 訂正(W2-16 タスク仕様 6.2)。この等値assertは±1のずれを構造では守らない ──
        // −1方向で落ちる行があるのはDayIndex==29に都市内約定が残っているシードだけである。
        //
        // 訂正(W2-22、#216)。出荷日数1(#210決定1)を入れた新しい基準値では、条件2の違反日が
        // 5シードとも0日になった ── 核心(NoInternalSettlementDaysが空)が成り立つ限り帳簿日
        // DayIndex==29(走査ラベルではday 30)には都市内約定が必ず1件以上ある。ただしコード順では、
        // この等値assertは核心より先に評価される(assertは条件別の空振り防止 → 本等値assert →
        // 核心の順)。−1方向のずれ(V-12: 帳簿の絞り込みを== dayIndexから== dayIndex - 1へ変える)
        // では、走査が帳簿日DayIndex==29の行を一度も参照しなくなるため、日別合計
        // (TotalInternalSettlements)がこの等値assertの相手側(TotalCityGoodInternalRowsIgnoringDate、
        // 日付を無視した全件数)より小さくなり、5シードとも、核心へ到達する前にこの等値assertが
        // 赤になる(実測はV-12)。核心自身がこの取り違えを検出できなくなるという意味ではない ──
        // 走査ラベルday 1(DayIndex==0の回)の内部約定も0件になる見込みで核心の
        // NoInternalSettlementDaysも空でなくなるはずだが、xUnitは最初に落ちたassertで止まるため、
        // 実際に赤を報告するのは等値assertのほうである。これは本assert(等値検算)が構造で守るように
        // なったという意味ではない ── 守っているのは核心のほうであり、核心が緑であることが前提で
        // ある。構造で留めているのは下のFirstScannedLedgerDayIndex/LastScannedLedgerDayIndexの
        // ほうである(W2-16 タスク仕様 6.4)。
        foreach (var household in world.Households)
        {
            foreach (var entry in world.Ledgers[household.Id])
            {
                if (entry.Direction == LedgerDirection.Purchase
                    && entry.CounterpartyId != HouseholdState.ExternalMarketSellerId
                    && entry.ItemId >= Item.Flour && entry.ItemId <= Item.Tools)
                {
                    scan.TotalCityGoodInternalRowsIgnoringDate++;
                }
            }
        }

        scan.FinalDayIndex = world.Now.DayIndex;
        scan.HouseholdCount = world.Households.Length;

        return scan;
    }

    /// <summary>
    /// 【核心】W2-17 タスク仕様テスト表 #7(#174の閉じる条件)。M0・シード1/2/3/7/42・30日。
    /// どの世帯についても、自分の出力品目(目標日数が正のものに限る。小麦粉・工具は対象外)の
    /// 世帯在庫が0の日に工房在庫が正である日が1日も無い。
    /// </summary>
    /// <remarks>
    /// <b>この条件は実測ではなく式の帰結として成立する。</b><see cref="SelfConsumption.TransferQuantity"/>
    /// が <c>min(販売在庫, …)</c> なので、世帯在庫が0まで下がった日は販売在庫を使い切っている
    /// (タスク仕様「順序・境界」節の「工房在庫が足りない日」の具体例)。<b>したがって本テストが
    /// 落ちたときに疑うのは経済ではなく実装である。</b>
    /// </remarks>
    /// <remarks>
    /// <b>空振り防止として、自家供給できる世帯が6戸(パン屋2・木材加工2・醸造2)であることを
    /// 先に確かめる。</b>小麦粉(製粉)・工具(鍛冶)は目標日数0のため自家供給から外れる
    /// (テスト表 #9)。
    /// </remarks>
    /// <remarks>
    /// <b>観測件数の下限(#39裁定D-A、2026-09-23)。</b>day 0 時点は自家供給6戸(パン屋2・木材加工2・
    /// 醸造2)× 出力品目1(各職業のレシピは出力1件)だが、④の職業付け替え(#39)が職業分布を
    /// 動かすので、6戸が6戸のまま30日続くことは前提にできない。日ごとの下限3は
    /// <see cref="Visionary.Sim.Systems.OccupationReassignment.TrySelectTarget"/>の
    /// 「担い手が自世帯だけなら維持する」規則(GDD02b §4.2)が自家供給3職業(パン屋・木材加工・
    /// 醸造)のどれも担い手0にしないことの翻訳であり、延べ観測件数の下限90はその30日ぶん
    /// (= 3 × 30)である。<b>実測(2026-09-23、シード1/2/3/7/42、④を通した後の延べ観測件数)</b>:
    /// seed=1: 168 / seed=2: 175 / seed=3: 169 / seed=7: 189 / seed=42: 188。<b>上にも下にも動く</b>
    /// ── 鍛冶や水車小屋番が④で自家供給できる職業へ入れば増える。<b>「母集団が縮んだ」わけではない</b>
    /// (180からの差はseed=7・42では増加である)。<b>実測(2026-09-23)、置き直した後の日次最小観測件数
    /// (下限3との距離)</b>: 全5シードとも最小値は5件(下限3を2件上回る。seed=1: day10 / seed=2:
    /// day20 / seed=3: day9 / seed=7: day26 / seed=42: day16)。
    /// </remarks>
    /// <remarks>
    /// <b>左項(世帯在庫が0だった観測)の到達可能性は assert しない理由と、その実測
    /// (2026-09-22、フェーズ1の裁定)。</b>「seed=42で左項が0件」は走査の欠陥ではない ──
    /// そのシードでは自家供給6戸すべてが30日間、一度も世帯在庫0まで下がらない(検出対象の前提
    /// そのものが成立していない)。実測した30日間の世帯在庫の最小値: household3 item7=1 /
    /// household4 item6=2 / household5 item7=1 / household6 item5=20 / household7 item5=16 /
    /// household8 item6=6。<b>これは経済が健全であることの現れであり、走査を疑う根拠にはならない
    /// ため、左項・右項の到達件数はassert対象にせず、下の核心の失敗メッセージにだけ載せる。</b>
    /// </remarks>
    /// <remarks>
    /// <b>なぜ件数(180)だけでは足りないか(レビュー3巡目の指摘の修正)。</b>件数は
    /// <c>SelfSuppliableObservations</c>への記録が180回起きたことしか留めない ──
    /// 記録する式の右項(<c>household.WorkshopInventory[...]</c>)を左項と同じ
    /// <c>household.HouseholdInventory[...]</c>に取り違えても(逆向きの取り違えも同様)、
    /// 記録は毎回起き件数は180のまま変わらない。このとき<see cref="IsHoardedWhileEmpty"/>が
    /// 評価する式は実質<c>x == 0 &amp;&amp; x &gt; 0</c>になって<b>恒偽</b>となり、
    /// <c>HoardedWhileEmptyEntries</c>はどんな世界でも常に空になる ── 件数の断定も
    /// <c>SelfSuppliableHouseholdCount</c>・<c>FinalDayIndex</c>も<see cref="IsHoardedWhileEmpty"/>の
    /// 単体テストも、下の核心も、すべて緑のまま通る。<b>この取り違えを塞ぐには、記録した値
    /// そのものを走行後の実際の在庫と突き合わせる必要がある。</b>下のループは最終日(day 30)の
    /// 観測について、記録した世帯在庫・工房在庫が<c>scan.World</c>(走行後の世界)の実際の値と
    /// 一致することを見る(世帯・品目は記録された組から引く。ハードコードしない)。
    /// </remarks>
    /// <remarks>
    /// <b>この留め具(<see cref="CitySurvivalScan.SelfSuppliableObservations"/>の記録と、上の
    /// 最終日突き合わせ)自体の変異の実測(実測日 2026-09-22、<c>mutator</c> が使い捨てworktreeで
    /// 1件ずつ当て、毎回 <c>dotnet test Visionary.sln -c Release</c>(477件)を走らせて測定。
    /// 対象コミット <c>13144cf</c>)。期待と食い違った件数は0件。</b>
    /// <b>訂正(#39裁定D-A、2026-09-23。docs/process/03-corrections.md)。</b>以下のR-1〜R-5'は
    /// 測定当時(<c>13144cf</c> / <c>0d46c1d</c> / <c>717709c</c>)のコードに対する記録であり、
    /// 当時の<see cref="ScannedSelfSuppliableEntryCount"/>は<c>Assert.Equal(180, ...)</c>の
    /// 固定値、最終日の観測件数は<c>Assert.Equal(6, finalDayObservations.Count)</c>の固定値
    /// だった(どちらも#39裁定D-Aで下限断定[日次観測 ≥ 3 / 延べ観測 ≥ 90 / 最終日観測 ≥ 3]へ
    /// 置き替え済み)。<b>結論(赤/緑・Actual値)は当時の測定結果そのものなので書き換えない</b>。
    /// 以下では、当時「観測件数180」「最終日の観測件数(6)」と呼んでいた留め具の現行の名前を
    /// 併記する。
    /// <list type="bullet">
    /// <item><b>R-1</b>(観測の右項<c>household.WorkshopInventory[...]</c>を左項と同じ
    /// <c>household.HouseholdInventory[...]</c>に取り違える。両項が同じ配列を読む)は
    /// <b>赤(全5シード)</b>。落ちたのは<b>最終日の工房在庫の突き合わせ</b>(核心ではない)。
    /// 当時の観測件数180の固定値assert(現行の延べ観測件数下限90 assertに相当)は通過した。</item>
    /// <item><b>R-2</b>(観測の左項<c>household.HouseholdInventory[...]</c>を右項と同じ
    /// <c>household.WorkshopInventory[...]</c>に取り違える。同上)は<b>赤(全5シード)</b>。
    /// 落ちたのは<b>最終日の世帯在庫の突き合わせ</b>(核心ではない)。</item>
    /// <item><b>R-3</b>(<c>scan.SelfSuppliableObservations.Add(...)</c>の行を削除)は
    /// <b>赤(全5シード)</b>。落ちたのは<b>当時の観測件数180の固定値assert</b>
    /// (現行の延べ観測件数下限90 assertに相当。Actual 0)。核心には到達しない。
    /// </item>
    /// <item><b>R-4</b>(#7 の走査を先頭1戸だけに絞る)は<b>赤(全5シード)</b>。落ちたのは
    /// <b>当時の観測件数180の固定値assert</b>(現行の延べ観測件数下限90 assertに相当。
    /// Actual 30 または 0、シードにより異なる)。核心には到達しない。</item>
    /// <item><b>R-5</b>(記録時の<c>day</c>を<c>day - 1</c>に変える。Dayのラベルのずれ)。
    /// この突き合わせ自体が空振りしうることへの手当て(開発者レビュー、2026-09-23)。
    /// <c>Where(o =&gt; o.Day == 30)</c>が0件になると下のforeachのassertが1本も走らず、
    /// R-1/R-2を唯一落としている留め具が緑のまま死ぬ。当時の観測件数180の固定値assertは
    /// <c>SelfSuppliableObservations.Count</c>であり<c>Day</c>の値を留めないので通過する。
    /// したがって最終日の観測件数が3件以上であることを先に留める(測定当時は
    /// <c>Assert.Equal(6, finalDayObservations.Count)</c>の固定値だったが、#39裁定D-Aで
    /// <c>finalDayObservations.Count &gt;= 3</c>の下限へ置き替えた)。<b>実測(実測日 2026-09-23、
    /// <c>mutator</c> が使い捨てworktreeで測定。対象コミット<c>0d46c1d</c>)。</b>
    /// <b>赤(全5シード)</b>。落ちたのは当時の<c>Assert.Equal(6, finalDayObservations.Count)</c>
    /// (本変異のために置いた空振り防止そのもの)で、失敗メッセージは全シード共通で
    /// <c>Expected: 6 / Actual: 0</c>。foreachの中の2本の<c>Assert.True</c>には到達していない
    /// (0周のため)。当時の観測件数180の固定値assert(<c>SelfSuppliableObservations.Count</c>)は
    /// 素通りした。総件数5件(Failed 5 / Passed 472 / Total 477)。他テストへの巻き込みなし。
    /// <b>R-5'(同じ変異を、空振り防止を置く前のコミット<c>717709c</c>に当てた反実仮想)。</b>
    /// <b>緑。</b>5インスタンスがすべて通り、477件全体も全緑だった。これが「空振り防止が
    /// 無い版では、Dayのラベルを1つずらす変異を当ててもループが0周のまま何も検証せずに
    /// 通過していた」ことの実測である。期待と食い違った件数は0件。</item>
    /// </list>
    /// <b>R-1〜R-5 はいずれも留め具か空振り防止で落ち、核心には一度も到達していない。これは
    /// 仕様どおりの階層である</b>(記録の正しさを守る留め具が、核心より先に壊れる)。<b>逆に
    /// M-7(<c>ConsumptionSystem.RunOneHousehold</c> の doc コメント参照)は留め具を全部通過して
    /// 核心で落ちた</b> ── 留め具は「走査が壊れたこと」を、核心は「経済が壊れたこと」を、
    /// それぞれ別々に捕まえている。
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(42)]
    public void OwnOutputIsNeverHoardedWhileTheHouseholdGoesWithout(long seed)
    {
        var scan = ScanThirtyDays(seed);
        _output.WriteLine(scan.Format(seed));

        Assert.Equal(30, scan.FinalDayIndex);
        Assert.Equal(6, scan.SelfSuppliableHouseholdCount);

        // 空振り防止 #39裁定D-A (1)。day 1〜30 の各日について、観測件数が3以上であることを
        // 確認する。下限3は「自家供給できる3職業(パン屋・木材加工・醸造)は、どれも
        // OccupationReassignment.TrySelectTargetの「担い手が自世帯だけなら維持する」規則
        // (GDD02b §4.2)により担い手0にならない」の翻訳であり、④の職業付け替えが動いても
        // 構造的に成り立つ(旧版の固定値180は「6戸が6戸のまま続くこと」を前提にしており、
        // ④が職業分布を動かすと決めたGDD02b §4.2の下では一世代前の仕様である)。
        int minDailyObservationCount = int.MaxValue;
        int minDailyObservationDay = 0;

        for (int day = 1; day <= 30; day++)
        {
            int dailyObservationCount = scan.SelfSuppliableObservations.Count(o => o.Day == day);

            if (dailyObservationCount < minDailyObservationCount)
            {
                minDailyObservationCount = dailyObservationCount;
                minDailyObservationDay = day;
            }

            Assert.True(
                dailyObservationCount >= 3,
                $"seed={seed} day={day}: 観測件数が{dailyObservationCount}件"
                    + "(自家供給3職業のいずれかの担い手が0になった可能性)。");
        }

        _output.WriteLine(
            $"seed={seed}: 観測件数の最小値={minDailyObservationCount}件"
                + $"(day={minDailyObservationDay})。");

        // 空振り防止 #39裁定D-A (2)。延べ観測件数が90(= 下限3 × 30日)以上であることを確認する。
        // (1)が全日で成り立てば論理的に必ず満たすが、D-Aの表がこの2本立てを指示している。
        Assert.True(
            scan.ScannedSelfSuppliableEntryCount >= 90,
            $"seed={seed}: 延べ観測件数({scan.ScannedSelfSuppliableEntryCount})が90未満"
                + "(母集団が空振りの可能性)。");

        // 記録した値そのものを走行後の実際の在庫と突き合わせる(上のremarks参照。件数だけでは
        // 「同じ配列を2回読む」取り違えを検出できない)。最終日(day 30)の観測に限り、世帯・
        // 品目は記録された組から引く(ここでハードコードしない)。
        //
        // 最終日(day 30)の観測が3件以上存在することを先に留める(#39裁定D-A。理由は上の下限3と
        // 同じ)。これが無いと、dayのラベルがずれる変異で下のループが0周になり、R-1/R-2 を唯一
        // 落としている突き合わせがassertを1本も走らせないまま緑で通る。
        var finalDayObservations = scan.SelfSuppliableObservations.Where(o => o.Day == 30).ToList();

        Assert.True(
            finalDayObservations.Count >= 3,
            $"seed={seed}: day30の観測件数が{finalDayObservations.Count}件(3未満)。");

        foreach (var observation in finalDayObservations)
        {
            var household = scan.World.Households[observation.HouseholdId];
            int actualHouseholdStock = household.HouseholdInventory[observation.ItemId];
            int actualWorkshopStock = household.WorkshopInventory[observation.ItemId];

            Assert.True(
                observation.HouseholdStock == actualHouseholdStock,
                $"seed={seed} householdId={observation.HouseholdId} itemId={observation.ItemId}: "
                    + $"day30に記録した世帯在庫({observation.HouseholdStock})が走行後の実際の"
                    + $"世帯在庫({actualHouseholdStock})と食い違った。");

            Assert.True(
                observation.WorkshopStock == actualWorkshopStock,
                $"seed={seed} householdId={observation.HouseholdId} itemId={observation.ItemId}: "
                    + $"day30に記録した工房在庫({observation.WorkshopStock})が走行後の実際の"
                    + $"工房在庫({actualWorkshopStock})と食い違った。");
        }

        int householdStockZeroCount = scan.SelfSuppliableObservations.Count(o => o.HouseholdStock == 0);
        int workshopStockPositiveCount =
            scan.SelfSuppliableObservations.Count(o => o.WorkshopStock > 0);

        Assert.True(
            scan.HoardedWhileEmptyEntries.Count == 0,
            $"seed={seed}: 自分の出力品目の世帯在庫が0の日に工房在庫が正だった世帯・品目・日: "
                + string.Join(
                    "; ",
                    scan.HoardedWhileEmptyEntries.Select(
                        e => $"householdId={e.HouseholdId} itemId={e.ItemId} day={e.Day}"))
                + $"(診断: 世帯在庫が0だった観測={householdStockZeroCount}件 / "
                + $"工房在庫が正だった観測={workshopStockPositiveCount}件。留め具ではなく診断のみ)。");
    }

    /// <summary>
    /// 【核心】W2-22 タスク仕様テスト表 #1(検出器)。M0・シード1/2/3/7/42・30日。day 1〜30のうち、
    /// 全世帯の <c>ProductionRuns</c> の合計が0になる日(都市の生産が丸1日止まる日)が1日も無い
    /// (<a href="https://github.com/stama72/visionary/issues/173">issue #173</a> の条件1)。
    /// </summary>
    /// <remarks>
    /// <b>正の向き。</b>失敗(違反日が現れる)は退行であり凶報である。
    /// </remarks>
    /// <remarks>
    /// <b>反転側から移した経緯。</b>W2-16(#173)は「書いた時点で赤」を機械に置くため、本条件を
    /// 反転して置いた(病理がまだあることを断定する側。理由はW2-16タスク仕様「前提」節)。
    /// <a href="https://github.com/stama72/visionary/issues/216">#216</a>(出荷日数 3 → 1、
    /// <a href="https://github.com/stama72/visionary/issues/210">#210</a> 決定1)を入れた実測
    /// (下記)で、5シードとも違反日が0日になった。W2-16タスク仕様「3. 検出器 ── 向きは実測が
    /// 決める」の割り振り規則(違反日0のシードは正側へ、反転側の<c>[InlineData]</c>が空になれば
    /// メソッドごと削除)に従い、反転側だった<c>ProductionStopsForAWholeDayWithinThirtyDays</c>を
    /// 削除し、全5シードを本テストへ移した(W2-22タスク仕様「実装の手順」)。<b>移したのは実測に
    /// 従った結果であり、この仕様が指図した割り振りではない</b>(W2-22タスク仕様「前提」節)。
    /// </remarks>
    /// <remarks>
    /// <b>向きが変わって見えなくなったもの(数え過ぎ方向)。</b>W2-16のM-1
    /// (<c>ProductionSystem.RunOneHousehold</c> の <c>household.ProductionRuns = runs;</c> を
    /// <c>household.ProductionRuns = Math.Max(1, runs);</c> へ変える変異)は、反転側だった当時
    /// 全5シードを赤にしていた(全世帯が毎日1以上を報告するので違反日が消える)。<b>正側では
    /// この取り違えは核心に見えない(V-2の実測、2026-09-25、<c>mutator</c>が使い捨てworktreeで
    /// 対象コミット<c>2c25b7a</c>に対して測定。条件1の正側は5/5緑、9失敗/622合格)。</b>
    /// 全世帯が常に1以上を報告する世界は「違反日0」そのものであり、正側の核心
    /// (<c>ProductionStoppedDays.Count == 0</c>)は素の実装と区別が付かない。<b>検出器は
    /// 見ないが、suiteは見ている。</b>捕まえたのは<c>ProductionSystemTests</c>の
    /// <c>EquipmentWithoutToolsZeroStopsProduction</c> /
    /// <c>ToolWearIsDroppedOnDaysWithZeroProduction</c> /
    /// <c>ProductionSubtractsPreviousDayErrandLaborLoss</c> /
    /// <c>ProductionRecordsRunsEveryDayIncludingZero</c>の4本と、<b>予期外に</b>
    /// <see cref="EveryOccupationKeepsAtLeastOneCarrierOverSixtyDays"/>(seed 1/2/3/7/42の
    /// 全5件)の合計9本である ── 依頼側が予期していたのは<c>ProductionSystemTests</c>だけ
    /// だった。
    /// <b>見えなくなったものはこれだけではない。</b>反転側(<c>Count &gt; 0</c>)が消えたので、
    /// <c>ScanThirtyDays</c>が違反日を記録する行(<c>scan.ProductionStoppedDays.Add(day)</c>)が
    /// 一度も走らなくなる書き換えは、どのassertにも触れない ── 6本の留め具はデータを1行も
    /// 読まず、条件別の空振り防止は別の変数(<c>TotalProductionRuns</c>)を見ており、核心は
    /// 空のリストを見て緑になる。<b>構造的な留め具は足さない</b>(追補Aの理由)。V-5の実測
    /// (2026-09-25、<c>mutator</c>、対象コミット<c>2c25b7a</c>)で確定した:
    /// <b>全件緑(631/631)。捕まえたテストは1本も無い</b> ── 検出器は自分自身の空洞化を見ない。
    /// </remarks>
    /// <remarks>
    /// <b>向きが変わって見えるようになったもの(数え落とし方向)。</b>W2-16のM-10(条件1の合計を
    /// <c>world.Households[0]</c> の1戸だけにする変異)は、反転側だった当時は緑のまま(部分和が
    /// 0の日は全体和が0の日を含むので、違反日が増えるだけで反転側の核心は動じなかった)。
    /// <b>V-3の実測(2026-09-25、<c>mutator</c>が使い捨てworktreeで対象コミット<c>2c25b7a</c>に
    /// 対して測定)は赤だが、5シード中3シード(seed 1・3・42)だけである。</b>seed 2・7は緑の
    /// まま(3失敗/628合格)。<b>これは「数え落とし方向が構造的に見えるようになった」のでは
    /// なく、データに乗った検出である。</b>本タスクの仕様「向きが変わると、検出器が見るものが
    /// 変わる」の表は「数え落とし → 正側は見る」と書いたが、実測はその「見る」が5シード中3
    /// シードにとどまることを示した ── 表の主張は部分的にしか成立しない。
    /// </remarks>
    /// <remarks>
    /// <b>M-4の予測外の巻き込み(W2-16から引き継ぐ)。</b>W2-16実測時、<c>ConsumptionSystem</c>の
    /// 世帯在庫の減算(条件3向けに選んだ変異)を削ると、条件3の核心だけでなく本条件(当時の
    /// 反転側)も全5シードで落ちた。<b>原因は特定していない。</b>正側での再測定(V-11、
    /// 2026-09-25、<c>mutator</c>が使い捨てworktreeで対象コミット<c>2c25b7a</c>に対して測定)
    /// では、本条件を含む#173の3検出器はすべて緑だった(23失敗/608合格)。かわりに他suiteの
    /// 23件が赤になった。詳細は<see cref="SomeHouseholdAlwaysHoldsNecessitiesOverThirtyDays"/>の
    /// docコメント(M-4)参照。
    /// </remarks>
    /// <remarks>
    /// <b>30日である理由。</b>プレイテストで使うのが1季 = 30日だから。60日にすると季節の切り替わりと
    /// <a href="https://github.com/stama72/visionary/issues/172">#172</a>の段差が混ざる。帯の検出器
    /// (<a href="https://github.com/stama72/visionary/issues/149">#149</a>)は60日のまま
    /// (issue #173)。
    /// </remarks>
    /// <remarks>
    /// <b>条件1は<a href="../../../docs/03-gdd/02-economy.md">GDD02 §8</a>-3より弱い。</b>都市全体の
    /// 生産回数の合計であって、レシピごとではない(issue #173の条件1は「都市の生産回数の合計 > 0」)。
    /// §8-3を満たしたと読まないこと(W2-16タスク仕様「含まない」節)。
    /// </remarks>
    /// <remarks>
    /// <b>基準値の実測(2026-09-24、出荷日数1(#216)を入れた後の実測である)。</b>条件1・条件2とも
    /// 5シードすべてで違反日が0日になった(#210決定ログ1の期待どおり)。<c>_output</c>の実測行:
    /// <list type="bullet">
    /// <item>seed=1 条件1: 違反0日 (day ) / 条件2: 違反0日 (day ) / 条件3: 違反0日 (day ) /
    /// 延べ生産回数=1314 / 延べ都市内約定=355 / #174観測件数=174 / #174抱え込み件数=0</item>
    /// <item>seed=2 条件1: 違反0日 (day ) / 条件2: 違反0日 (day ) / 条件3: 違反0日 (day ) /
    /// 延べ生産回数=1293 / 延べ都市内約定=357 / #174観測件数=171 / #174抱え込み件数=0</item>
    /// <item>seed=3 条件1: 違反0日 (day ) / 条件2: 違反0日 (day ) / 条件3: 違反0日 (day ) /
    /// 延べ生産回数=1360 / 延べ都市内約定=321 / #174観測件数=178 / #174抱え込み件数=0</item>
    /// <item>seed=7 条件1: 違反0日 (day ) / 条件2: 違反0日 (day ) / 条件3: 違反0日 (day ) /
    /// 延べ生産回数=1279 / 延べ都市内約定=401 / #174観測件数=180 / #174抱え込み件数=0</item>
    /// <item>seed=42 条件1: 違反0日 (day ) / 条件2: 違反0日 (day ) / 条件3: 違反0日 (day ) /
    /// 延べ生産回数=1355 / 延べ都市内約定=388 / #174観測件数=175 / #174抱え込み件数=0</item>
    /// </list>
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(<c>mutator</c>が使い捨てworktreeで2026-09-25、対象コミット<c>2c25b7a</c>に
    /// 対して1件ずつ当てた。素の<c>2c25b7a</c>は631件全緑)。</b>
    /// <list type="bullet">
    /// <item><b>V-1</b>(<c>shipmentDays: 1</c> を <c>3</c> へ戻す)は<b>赤</b>
    /// (11失敗/620合格)。条件1の正側が全5件、条件2の正側も全5件。条件3は5件とも緑のまま
    /// (= 条件3は出荷日数を見ていない)。加えて<see cref="UnaffordableNecessityCountsOnlyTheFundsShortfall"/>
    /// が巻き添えで赤(本タスクで選び直した(世帯, 日)が出荷日数3では成立しないため)。</item>
    /// <item><b>V-2</b>(<c>household.ProductionRuns = Math.Max(1, runs);</c>)は
    /// 条件1の正側が<b>5/5緑</b>(9失敗/622合格)。捕まえたのは<c>ProductionSystemTests</c>の4本
    /// (上記remarks参照)と、予期外に
    /// <see cref="EveryOccupationKeepsAtLeastOneCarrierOverSixtyDays"/>(全5件)。</item>
    /// <item><b>V-3</b>(条件1の合計を<c>world.Households[0]</c>の1戸だけに)は<b>赤だが3/5シード
    /// のみ</b>(3失敗/628合格。seed 1・3・42。seed 2・7は緑のまま)。詳細は上記remarks参照。</item>
    /// <item><b>V-5</b>(条件1の違反日記録の分岐を偽に)は<b>全件緑</b>(631/631)。捕まえた
    /// テストは1本も無い。</item>
    /// </list>
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(42)]
    public void ProductionNeverStopsForAWholeDayOverThirtyDays(long seed)
    {
        var scan = ScanThirtyDays(seed);
        _output.WriteLine(scan.Format(seed));

        // 核心と独立な空振り防止(6本)。この6本はデータを1行も参照しない ── 帳簿が空でも、経済が
        // 直っても値は1/30/0/29である(W2-16 タスク仕様 6.4)。ラベルが0始まりに滑る書き換えや、
        // 帳簿の絞り込みのdayIndexの窓が±1ずれる書き換えを構造で落とす。
        Assert.Equal(30, scan.FinalDayIndex);
        Assert.Equal(10, scan.HouseholdCount);
        Assert.Equal(1, scan.FirstDayLabel);
        Assert.Equal(30, scan.LastDayLabel);
        Assert.Equal(0, scan.FirstScannedLedgerDayIndex);
        Assert.Equal(29, scan.LastScannedLedgerDayIndex);

        // 条件別の空振り防止。正の向きでは核心が論理的に含意する(違反日0 ⇒ 全日で合計が正)ので、
        // 成立していれば必ず緑である。それでも残す(W2-16タスク仕様「正の向きへ反転した後もそのまま
        // 残る」)。
        Assert.True(
            scan.TotalProductionRuns > 0,
            $"seed={seed}: 30日間の延べ生産回数が0(ProductionRunsを読む先の取り違えの可能性)。");

        // 核心。正側 ── 違反日が現れたら退行。Assert.Emptyは使わない(失敗時に伝えることがある)。
        Assert.True(
            scan.ProductionStoppedDays.Count == 0,
            $"seed={seed}: 条件1 が day {string.Join(", ", scan.ProductionStoppedDays)} で破れた"
                + $"(違反{scan.ProductionStoppedDays.Count}日)。これは退行である(#216)。"
                + $"{scan.Format(seed)}");
    }

    /// <summary>
    /// 【核心】W2-22 タスク仕様テスト表 #2(検出器)。M0・シード1/2/3/7/42・30日。day 1〜30のうち、
    /// 都市生産品(小麦粉・薪・パン・ビール・工具、itemId 4〜8)の都市内約定件数が0になる日が
    /// 1日も無い(issue #173 の条件2)。
    /// </summary>
    /// <remarks>
    /// <b>正の向き。</b>失敗(違反日が現れる)は退行であり凶報である。
    /// </remarks>
    /// <remarks>
    /// <b>反転側から移した経緯。</b><see cref="ProductionNeverStopsForAWholeDayOverThirtyDays"/>と
    /// 同じ(W2-16が反転して置き、<a href="https://github.com/stama72/visionary/issues/216">#216</a>
    /// (出荷日数 3 → 1、<a href="https://github.com/stama72/visionary/issues/210">#210</a> 決定1)
    /// を入れた実測で違反日が5シードとも0日になったため、反転側だった
    /// <c>InternalSettlementsOfCityGoodsDisappearWithinThirtyDays</c>を削除し、全5シードを
    /// 本テストへ移した。W2-22タスク仕様「実装の手順」)。
    /// </remarks>
    /// <remarks>
    /// <b>向きが変わって見えなくなったもの。</b>反転側(<c>Count &gt; 0</c>)が消えたので、
    /// <c>ScanThirtyDays</c>が違反日を記録する行(<c>scan.NoInternalSettlementDays.Add(day)</c>)が
    /// 一度も走らなくなる書き換えは、どのassertにも触れない ── 6本の留め具はデータを1行も読まず、
    /// 条件別の空振り防止は別の変数(<c>TotalInternalSettlements</c>)を見ており、下記の日付の検算
    /// (<c>TotalCityGoodInternalRowsIgnoringDate</c>との等値assert)も<c>NoInternalSettlementDays</c>を
    /// 参照しないので、核心は空のリストを見て緑になる。<b>構造的な留め具は足さない</b>
    /// (追補Aの理由)。V-6の実測(2026-09-25、<c>mutator</c>が使い捨てworktreeで対象コミット
    /// <c>2c25b7a</c>に対して測定)で確定した: <b>全件緑(631/631)。捕まえたテストは1本も無い</b>
    /// ── 検出器は自分自身の空洞化を見ない。
    /// </remarks>
    /// <remarks>
    /// <b>都市内の判定。</b><c>CounterpartyId != HouseholdState.ExternalMarketSellerId</c>が
    /// 「都市内の約定」の判定そのもの(<a href="../../../docs/03-gdd/02d-external-market-and-money.md">
    /// GDD02d §2.1</a>)。窓口からの輸入で緑になるのを防ぐ(開発者の決定、2026-09-22)。
    /// <c>Purchase</c>だけ数えるのは二重計上を避けるため(<a
    /// href="../../../docs/03-gdd/01-trust-and-conversation.md">GDD01 §4.4</a>。1約定は買い手と
    /// 売り手が1行ずつ記帳する)。
    /// </remarks>
    /// <remarks>
    /// <b>日付の±1が核心に見えるようになったこと。</b>核心が「30日すべてで都市内約定がある」を
    /// 主張するので、核心が緑である限り帳簿日<c>DayIndex == 29</c>(走査ラベルではday 30)には
    /// 必ず行がある。<b>走査ラベルと帳簿日を区別して書く</b> ── 窓が−1へずれれば(絞り込みが
    /// 本来の帳簿日より1小さい値を見るようになれば)、走査ラベルday 1(対応する帳簿日
    /// <c>DayIndex == -1</c>は存在しない)の都市内約定が0件になって核心自身が赤になる。+1へ
    /// ずれれば走査ラベルday 30(対応する帳簿日<c>DayIndex == 30</c>は存在しない)が0件になって
    /// 同じく赤になる(W2-22タスク仕様「日付の±1は、正の向きでは核心が見る」)。<b>ただし下記の等値
    /// assert(<c>TotalCityGoodInternalRowsIgnoringDate</c>との突き合わせ、W2-16タスク仕様6.2)が
    /// 「構造で守るようになった」とは書かない</b> ── 守っているのは核心のほうであり、核心が
    /// 緑であることが前提である。<b>この改善は6.4の留め具(<c>FirstDayLabel</c> /
    /// <c>LastDayLabel</c> / <c>FirstScannedLedgerDayIndex</c> /
    /// <c>LastScannedLedgerDayIndex</c>)を置き換えない。</b>4本はデータを1行も参照せずにループの
    /// 形そのものを留めており、向きに依らない。両方残す。<b>V-12の実測(2026-09-25、
    /// <c>mutator</c>が使い捨てworktreeで対象コミット<c>2c25b7a</c>に対して測定。帳簿の
    /// 絞り込みを<c>== dayIndex</c>から<c>== dayIndex - 1</c>へ変える)は赤を確定させた</b>
    /// (5失敗/626合格、条件2の正側が全5件)。<b>ただし落ちたのは核心ではなく下記の6.2の等値
    /// assertである</b>(例: seed=42でExpected 388 / Actual 379)。核心
    /// <c>NoInternalSettlementDays.Count == 0</c>には到達していない。本仕様が「日付の±1は、
    /// 正の向きでは核心が見る」と書いた改善は、検出自体は実在するが、報告するのは核心ではなく
    /// 6.2の等値assertである。
    /// </remarks>
    /// <remarks>
    /// <b>母数を「都市内の約定だけ」にした決定を守るassertの有無(相手軸)。</b>W2-16は「無い」と
    /// 実測で確定させた(M-5: 本テスト側で相手条件<c>!=</c>を<c>==</c>へ変える/M-3: <c>src</c>側で
    /// <c>ExecuteImport</c>の<c>CounterpartyId</c>を0にする。いずれも当時の反転側で緑のまま)。
    /// <b>V-4の実測(2026-09-25、<c>mutator</c>が使い捨てworktreeで対象コミット<c>2c25b7a</c>に
    /// 対して測定。M-5と同じ変異)は赤</b>(5失敗/626合格、条件2の正側が全5件)。<b>ただし落ちた
    /// のは核心ではなく下記の6.2の等値assert</b>
    /// (<c>Assert.Equal(scan.TotalCityGoodInternalRowsIgnoringDate, scan.TotalInternalSettlements)</c>。
    /// 例: seed=42でExpected 388 / Actual 62)。<b>W2-16が「母数を都市内だけにした決定を守る
    /// assertは無い」と確定させた結論は、正側では偽になった</b> ── 相手軸は機械に守られている。
    /// ただし守っているのは核心ではなく6.2の等値assertである。
    /// </remarks>
    /// <remarks>
    /// <b>向き・品目の絞り込みは恒等変換なので、向きに依らず機械に守られない
    /// (W2-16の実測を引き継ぐ)。</b>M-6(絞り込みの<c>Direction == Purchase</c>を<c>Sale</c>へ
    /// 変える)とM-7(品目範囲の下限を<c>Item.Flour</c>から<c>Item.Grain</c>へ広げ0〜8にする)は、
    /// 当時の反転側でどちらも<b>緑のまま</b>だった ── M-6は1約定につき買い手<c>Purchase</c>・
    /// 売り手<c>Sale</c>が1本ずつ立つので件数が完全に一致し、M-7は都市内で売買される品目が必ず
    /// 4〜8なので恒等変換になる。<b>この2つは恒等変換であることの理屈が向きに依らないので、
    /// 正側でも緑のままと現在形で書ける</b>(測定自体は当時の反転側のものである)。
    /// <b>絞り込みの4軸(日付・向き・相手・品目)は軸ごとに扱いが分かれる。</b>
    /// <b>日付</b>: 守られている(V-12で実測。報告するのは核心ではなく下記の6.2の等値
    /// assertであり、核心が緑であることが前提である)。<b>相手</b>: 守られている(V-4で実測。
    /// 報告するのは同じく6.2の等値assertであり、核心ではない)。<b>向き(M-6)・品目を広げる
    /// 方向(M-7)</b>: 恒等変換なので守られない(向きに依らない)。<b>日付軸と相手軸のどちらも、
    /// 赤を報告するのは6.2の等値assertである</b> ──「核心が見る」ではない。6.4の留め具
    /// (M-13/M-14)はループの形そのものを守るだけで、絞り込みの中身(向き・相手・品目)は読まない。
    /// この絞り込みを書き換えるときに機械に頼れるのは、日付・相手軸については6.2の等値assert
    /// (ただし核心が緑であることが前提)、向き・品目については恒等変換であることの理屈だけである。
    /// </remarks>
    /// <remarks>
    /// <b>30日である理由。</b><see cref="ProductionNeverStopsForAWholeDayOverThirtyDays"/>と同じ
    /// (W2-16タスク仕様)。
    /// </remarks>
    /// <remarks>
    /// <b>基準値の実測(2026-09-24、出荷日数1(#216)を入れた後の実測である)。</b>
    /// <see cref="ProductionNeverStopsForAWholeDayOverThirtyDays"/>のdocコメントに転記済みの
    /// 実測行と同一(1回の走行で3条件をまとめて採るため)。5シードとも条件2の違反日が0日になった。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(<c>mutator</c>が使い捨てworktreeで2026-09-25、対象コミット<c>2c25b7a</c>に
    /// 対して1件ずつ当てた。素の<c>2c25b7a</c>は631件全緑)。</b>
    /// <list type="bullet">
    /// <item><b>V-1</b>(<c>shipmentDays: 1</c> を <c>3</c> へ戻す)は<b>赤</b>
    /// (11失敗/620合格)。条件2の正側が全5件(条件1の正側も全5件。詳細は
    /// <see cref="ProductionNeverStopsForAWholeDayOverThirtyDays"/>のdocコメント参照)。条件3は
    /// 5件とも緑のまま(= 条件3は出荷日数を見ていない)。</item>
    /// <item><b>V-4</b>(相手条件<c>!=</c>を<c>==</c>へ)は<b>赤</b>(5失敗/626合格)。条件2の
    /// 正側が全5件。落ちたのは核心ではなく下記の6.2の等値assert(例: seed=42でExpected 388 /
    /// Actual 62)。詳細は上記remarks(相手軸)参照。</item>
    /// <item><b>V-6</b>(条件2の違反日記録の分岐を偽に)は<b>全件緑</b>(631/631)。捕まえた
    /// テストは1本も無い。</item>
    /// <item><b>V-12</b>(帳簿日照合を<c>== dayIndex</c>から<c>== dayIndex - 1</c>へ)は
    /// <b>赤</b>(5失敗/626合格)。条件2の正側が全5件。落ちたのは6.2の等値assert(例: seed=42で
    /// Expected 388 / Actual 379)。核心<c>NoInternalSettlementDays.Count == 0</c>には到達して
    /// いない。</item>
    /// </list>
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(42)]
    public void InternalSettlementsOfCityGoodsNeverDisappearOverThirtyDays(long seed)
    {
        var scan = ScanThirtyDays(seed);
        _output.WriteLine(scan.Format(seed));

        // 核心と独立な空振り防止(6本)。この6本はデータを1行も参照しない ── 帳簿が空でも、経済が
        // 直っても値は1/30/0/29である(W2-16 タスク仕様 6.4)。ラベルが0始まりに滑る書き換えや、
        // 帳簿の絞り込みのdayIndexの窓が±1ずれる書き換えを構造で落とす。
        Assert.Equal(30, scan.FinalDayIndex);
        Assert.Equal(10, scan.HouseholdCount);
        Assert.Equal(1, scan.FirstDayLabel);
        Assert.Equal(30, scan.LastDayLabel);
        Assert.Equal(0, scan.FirstScannedLedgerDayIndex);
        Assert.Equal(29, scan.LastScannedLedgerDayIndex);

        // 条件別の空振り防止(2本)。絞り込みが全行を落としている(向き・相手・品目のいずれかの
        // 取り違え)ケースを塞ぐ。
        Assert.True(
            scan.TotalInternalSettlements > 0,
            $"seed={seed}: 30日間の延べ都市内約定が0(向き・相手・品目のいずれかの取り違えの"
                + "可能性)。");

        // 日付の検算(W2-16 タスク仕様 6.2)。条件2の帳簿の絞り込みだけが日付を使うので、その日付が
        // 正しいかをここで見る。この等値assertは±1のずれを構造では守らない(上のremarks参照) ──
        // 構造で留めているのは上のFirstScannedLedgerDayIndex/LastScannedLedgerDayIndexのほうである
        // (W2-16 タスク仕様 6.4)。
        Assert.Equal(scan.TotalCityGoodInternalRowsIgnoringDate, scan.TotalInternalSettlements);

        // 核心。正側 ── 違反日が現れたら退行。Assert.Emptyは使わない(失敗時に伝えることがある)。
        Assert.True(
            scan.NoInternalSettlementDays.Count == 0,
            $"seed={seed}: 条件2 が day {string.Join(", ", scan.NoInternalSettlementDays)} で破れた"
                + $"(違反{scan.NoInternalSettlementDays.Count}日)。これは退行である(#216)。"
                + $"{scan.Format(seed)}");
    }

    /// <summary>
    /// テスト表 #9(正側)。M0・シード1/2/3/7/42・30日。day 1〜30のいずれの日も、世帯在庫(パン・薪・
    /// ビール)が3品目とも0の世帯は全世帯にならない ── 条件3(issue #173)がこの5シードでは
    /// 30日すべてで成立している(直った側)。
    /// </summary>
    /// <remarks>
    /// <b>正の向き。</b>失敗(条件が破れる日が現れる)は退行であり凶報である。
    /// </remarks>
    /// <remarks>
    /// <b>30日である理由。</b><see cref="ProductionNeverStopsForAWholeDayOverThirtyDays"/>と同じ
    /// (W2-16タスク仕様)。
    /// </remarks>
    /// <remarks>
    /// <b>見るのは世帯在庫(工房在庫ではない)。</b><a
    /// href="../../../docs/03-gdd/02b-consumption-and-household.md">GDD02b §1</a>。工房在庫を見ると、
    /// パン屋が売れ残りのパンを抱えて飢えている状態(<a
    /// href="https://github.com/stama72/visionary/issues/174">#174</a>)が「空でない」に見える。
    /// </remarks>
    /// <remarks>
    /// <b>3品目の論理積である。</b>issue #173の「世帯在庫(パン・薪・ビール)が0の世帯」の字義どおり。
    /// 品目ごとに「全世帯で0」を見る形は採らない ── ビール(嗜好)が都市から消えることは飢餓とは
    /// 別の事象であり、issue #173の閉じる条件より強い主張になる(W2-16タスク仕様)。
    /// </remarks>
    /// <remarks>
    /// <b>基準値の実測(2026-09-22、旧版。#174 を入れる前)。</b>5シードのうちseed7だけが違反日を
    /// 持った(30日、1日)ため、条件3はseed7だけを反転側(<c>AllHouseholdsRunEmptyWithinThirtyDays</c>、
    /// #174 で削除済み)へ、残り4シード(1/2/3/42)は本テスト(正側)へ割り振った。<c>_output</c>の
    /// 実測行はseed=7: 条件3: 違反1日 (day 30)。
    /// </remarks>
    /// <remarks>
    /// <b>基準値の実測(2026-09-22、自家消費(#174)を入れた後の実測である)。</b>5シードとも条件3の
    /// 違反日が0日になった(<see cref="ProductionNeverStopsForAWholeDayOverThirtyDays"/> の
    /// doc コメントに転記済みの実測行 ── seed7も含め全5シードで「条件3: 違反0日」)。W2-16の手順
    /// (「作るもの4」)に従い、seed7を反転側から本テスト(正側)へ移し、反転側の
    /// <c>[InlineData]</c> が空になった<c>AllHouseholdsRunEmptyWithinThirtyDays</c>はメソッドごと
    /// 削除した(xUnitは<c>[InlineData]</c>の無い<c>[Theory]</c>をエラーにするため)。
    /// </remarks>
    /// <remarks>
    /// <b>issue #173 本文の実測表は条件3について再現しない(W2-16 タスク仕様 6.3 #2、
    /// レビュー1巡目 II-1)。</b>#173は「シード1・day30に全10戸が0」と記録したが、
    /// 本実測(2026-09-22)ではシード1は条件3の違反0日であり、旧版で違反を持っていたのはシード7の
    /// day30だけだった(#174でそれも直った)。原因は特定していない。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(<c>mutator</c> が測定、2026-09-22・出荷日数3の経済(コミット<c>13144cf</c>より
    /// 前)、14件、対象は#174より前のコミット。当時は本テストがseed1/2/3/42の4シード、反転側
    /// <c>AllHouseholdsRunEmptyWithinThirtyDays</c>がseed7の1シードという割り振りだった)。</b>
    /// <list type="bullet">
    /// <item><b>M-4</b>(<c>ConsumptionSystem</c> の <c>household.HouseholdInventory[itemId] -=
    /// consumedQuantity;</c> を行ごと削る)は<b>赤</b>。当時の反転側(seed7)の核心(世帯在庫が
    /// 減らないので全戸空の日が来ない)が落ちた。同じ変異は条件1の当時の反転側
    /// (現在は削除され、正側 <see cref="ProductionNeverStopsForAWholeDayOverThirtyDays"/> に
    /// 置き換わっている)も全5シードで落としている(予測外の巻き込み、原因は特定していない)。
    /// <b>正側は実測で緑と確定した</b>(V-11の実測、2026-09-25、<c>mutator</c>が使い捨て
    /// worktreeで対象コミット<c>2c25b7a</c>に対して測定)。#173の3検出器(条件1・条件2・
    /// 条件3)はすべて緑だった(23失敗/608合格)。追補Cの訂正(「反転側を赤にした変異は
    /// 正側では緑」)が実測で裏づけられた。<b>かわりに他suiteの23件が赤になった</b> ──
    /// <c>ConsumptionSystemTests</c>の<c>FirewoodConsumptionFollowsTheSeason</c> /
    /// <c>ConsumptionDiffersByRank</c> / <c>ConsumptionScalesWithHouseholdSize</c> /
    /// <c>ConsumptionStopsAtZeroAndDoesNotGoNegative</c> /
    /// <c>ConsumptionTouchesOnlyTheHouseholdInventory</c> / <c>SeasonalItemIsOnlyFirewood</c> /
    /// <c>SeasonCoefficientIsRoundedPerMember</c>の7本、
    /// <c>DailyConsumptionTests.LookaheadMatchesWhatConsumptionActuallyEatsForOneDay</c>、
    /// <c>ProductionAndConsumptionPipelineTests.ProductionAndConsumptionRunInPipelineOrder</c>、
    /// <c>SelfConsumptionTests</c>の4本、
    /// <see cref="TradePipelineTests.EveryOccupationKeepsAtLeastOneCarrierOverSixtyDays"/>
    /// (5件)、<c>MetricsSystemTests.SellerDaysCountOnlyPostedOffers</c>、
    /// <see cref="TradePipelineTests.UnaffordableNecessityCountsOnlyTheFundsShortfall"/> /
    /// <see cref="TradePipelineTests.PreferenceIsActuallyBought"/> /
    /// <see cref="TradePipelineTests.NecessityIsSettledBeforePreference"/> /
    /// <see cref="TradePipelineTests.PipelineIsDeterministicWithNeedGeneration"/>。
    /// 詳細は<see cref="ProductionNeverStopsForAWholeDayOverThirtyDays"/>のdocコメント(M-4)にも
    /// 転記済み。</item>
    /// <item><b>M-8</b>(判定を <c>HouseholdInventory</c> から <c>WorkshopInventory</c> へ変える)は、
    /// 当時の本テスト(正側4シード)では<b>緑のまま</b>。落ちたのは当時の反転側(seed7)の核心
    /// だった。<b>この記録は出荷日数3の経済でのものである。</b>出荷日数1(#216)での再実測は
    /// V-7(下記「変異の実測(V-7〜V-10)」参照)。</item>
    /// <item><b>M-9</b>(3品目の判定の <c>&amp;&amp;</c> を <c>||</c> へ変える。論理積を論理和にする)は
    /// <b>赤</b>。当時の本テスト(正側)の4シードの核心が落ちた。当時の反転側(seed7)は緑のまま
    /// だった ── この取り違えを捕まえているのは正側の存在そのものである(<see
    /// cref="ScanThirtyDays"/> のdocコメント、6.1の非対称)。<b>この記録は出荷日数3の経済での
    /// ものである。</b>出荷日数1での再実測はV-8。</item>
    /// <item><b>M-12</b>(3品目を <c>Grain</c>/<c>Timber</c>/<c>IronOre</c> へ変える。品目添字の
    /// 取り違え)は<b>赤</b>。条件別の空振り防止(<c>AllHouseholdsEmptyDays.Count &lt; 30</c>)が
    /// 5件(当時の本テスト4シード+当時の反転側1シード)で落ちた。<b>この記録は出荷日数3の経済での
    /// ものである。</b>出荷日数1での再実測はV-9。</item>
    /// <item><b>M-13 / M-14</b>(6.4の留め具の変異)は本テストを含む4本すべて・全15インスタンスで
    /// 赤。詳細は <see cref="ScanThirtyDays"/> のdocコメントを参照。</item>
    /// </list>
    /// <b>seed7を本テストへ移した後の判別力(M-8・M-9・M-12を含む)は、いずれも出荷日数3の経済での
    /// ものであり、#174でも出荷日数1(#216)でも再実測していなかった。</b>本タスクで
    /// V-7〜V-9として再実測し、結果は下記「変異の実測(V-7〜V-10)」へ転記する(変異を当てるのは
    /// <c>mutator</c>のみ。ADR-0013)。
    /// </remarks>
    /// <remarks>
    /// <b>検出器自身の空洞化(追補C-2)。</b>条件1・条件2の同種の記述(それぞれのdocコメント参照)を
    /// 条件3にも置く ── 3本を並べて読むと「条件1・2にはこの穴があり、条件3には無い」と読めて
    /// しまうが、<b>穴は3本とも同じである。</b><c>ScanThirtyDays</c>が違反日を記録する行
    /// (<c>scan.AllHouseholdsEmptyDays.Add(day)</c>)が一度も走らなくなる書き換えは、どのassertにも
    /// 触れない ── 6本の留め具はデータを1行も読まず、条件別の空振り防止(<c>Count &lt; 30</c>)は
    /// 記録が空でも成立し、核心(<c>Count == 0</c>)は空のリストを見て緑になる。他のテストもこの
    /// リストを読んでいない。<b>構造的な留め具は足さない</b>(追補Aの理由と同じ)。V-10の実測
    /// (下記「変異の実測(V-7〜V-10)」参照)で確定した: 全件緑、捕まえたテストは1本も無い。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(V-7〜V-10)</b>(<c>mutator</c>が使い捨てworktreeで2026-09-25、対象コミット
    /// <c>2c25b7a</c>に対して1件ずつ当てた。素の<c>2c25b7a</c>は631件全緑)。
    /// <list type="bullet">
    /// <item><b>V-7</b>(条件3の判定を<c>HouseholdInventory</c>→<c>WorkshopInventory</c>へ。
    /// W2-16のM-8と同じ変異)は<b>全件緑</b>(631/631)。出荷日数1でも緑 ──
    /// 2026-09-22(出荷日数3)の結果と同じ。上のM-8の記録「いまの5シードの組では判別する力を
    /// 持たない」は出荷日数1でも当たる。鮮度切れは解消。</item>
    /// <item><b>V-8</b>(3品目の判定の<c>&amp;&amp;</c>を<c>||</c>へ。W2-16のM-9と同じ変異)は
    /// <b>赤</b>(5失敗/626合格)。条件3が全5件。落ちたのは<b>核心</b>
    /// <c>AllHouseholdsEmptyDays.Count == 0</c>。上のM-9の記録は出荷日数1でも当たる。</item>
    /// <item><b>V-9</b>(3品目を<c>Grain</c>/<c>Timber</c>/<c>IronOre</c>へ。W2-16のM-12と同じ
    /// 変異)は<b>赤</b>(5失敗/626合格)。条件3が全5件。ただし落ちたのは<b>空振り防止</b>
    /// <c>AllHouseholdsEmptyDays.Count &lt; 30</c>(30日すべてが「全戸空」と判定されたため)で
    /// あり、<b>核心ではない</b>。</item>
    /// <item><b>V-10</b>(条件3の違反日記録の分岐を偽に。V-5/V-6の条件3版)は<b>全件緑</b>
    /// (631/631)。捕まえたテストは1本も無い ── 検出器は自分自身の空洞化を見ない。</item>
    /// </list>
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(42)]
    public void SomeHouseholdAlwaysHoldsNecessitiesOverThirtyDays(long seed)
    {
        var scan = ScanThirtyDays(seed);
        _output.WriteLine(scan.Format(seed));

        // 核心と独立な空振り防止(6本)。この6本はデータを1行も参照しない ── 帳簿が空でも、経済が直っても
        // 値は1/30/0/29である(W2-16 タスク仕様 6.4)。ラベルが0始まりに滑る書き換えや、帳簿の
        // 絞り込みのdayIndexの窓が±1ずれる書き換えを構造で落とす。
        Assert.Equal(30, scan.FinalDayIndex);
        Assert.Equal(10, scan.HouseholdCount);
        Assert.Equal(1, scan.FirstDayLabel);
        Assert.Equal(30, scan.LastDayLabel);
        Assert.Equal(0, scan.FirstScannedLedgerDayIndex);
        Assert.Equal(29, scan.LastScannedLedgerDayIndex);

        // 条件別の空振り防止(正の向きでは核心が論理的に含意するので、成立していれば必ず緑)。
        Assert.True(
            scan.AllHouseholdsEmptyDays.Count < 30,
            $"seed={seed}: 30日すべてで全世帯が空になった(在庫の添字の取り違えの可能性。"
                + "初期在庫は薪28・パン6・ビール1のため、day 1は必ず空でないはずである)。");

        // 核心。正側 ── 違反日が現れたら退行。
        Assert.True(
            scan.AllHouseholdsEmptyDays.Count == 0,
            $"seed={seed}: 条件3が day {string.Join(", ", scan.AllHouseholdsEmptyDays)} で破れた"
                + $"(違反{scan.AllHouseholdsEmptyDays.Count}日 / 延べ生産回数="
                + $"{scan.TotalProductionRuns} / 延べ都市内約定={scan.TotalInternalSettlements})。");
    }

    /// <summary>世帯を1戸、指定の区画・職業で作る(単独NPC世帯。<c>TradeSystemTests.AddHousehold</c>相当)。</summary>
    private static void AddHousehold(World world, int id, int districtId, Occupation occupation, int liquidFunds)
    {
        world.Npcs[id].Rank = NpcRank.Master;
        world.Households[id] = new HouseholdState(
            id: id, districtId: districtId, headNpcId: id, memberNpcIds: new[] { id }, itemCount: Item.Count);
        world.Households[id].Occupation = occupation;
        world.Households[id].LiquidFunds = liquidFunds;
    }

    /// <summary>
    /// テスト表 #13(#39)専用の定義。薪(Woodworker)とパン(Baker)の売り手を実在の職業として持たせる
    /// 必要があるため、<see cref="EconomySystemTestFixtures.BuildDefinition"/>(カスタムレシピを
    /// 1件しか差し込めない)ではなく <see cref="WorldDefinition.M0"/> の実レシピ表(5職業とも本物の
    /// 入出力を持つ)を流用し、それ以外の係数だけを本テスト用に単純化した値へ差し替える。
    /// </summary>
    /// <remarks>
    /// <b>本クラスの「世界は必ず<see cref="WorldGenerator.Generate"/>で作る」規約に対する意図的な
    /// 例外である。</b>タスク仕様(W2-18)が「世界は手組みでよい」を#1〜#13すべてに対して明示的に
    /// 許可しており、#13固有の注記は「使うパイプラインをFullPipelineにする」ことだけを追加で
    /// 要求している。順3(<see cref="HouseholdSystem"/>)は価格も観測も読まないため、本クラスの
    /// 既存テストが避けている「縮退した世界」の懸念(#36引き継ぎ)は本テストには当たらない。
    /// </remarks>
    private static WorldDefinition BuildFirewoodCrowdsOutBreadDefinition()
    {
        var recipes = WorldDefinition.M0.Recipes; // Miller/Baker/Brewer/Woodworker/Smithの実レシピ。

        var necessityTargetStockDays = new int[Item.Count];
        necessityTargetStockDays[Item.Firewood] = 3; // T=3日×2/日=6単位。
        necessityTargetStockDays[Item.Bread] = 3;    // T=3日×1/日=3単位。

        var dailyConsumptionPerNpcByRank = new[]
        {
            BuildRow((Item.Firewood, 2), (Item.Bread, 1)), // 親方。
            new int[Item.Count],                            // 職人(この世界には居ない)。
            new int[Item.Count],                            // 徒弟(同上)。
        };

        // 季節係数は一様1000‰にする(薪の消費量計算から季節変動を除き、数値の見通しを保つ)。
        var firewoodConsumptionSeasonPermille = new[] { 1000, 1000, 1000, 1000 };

        // 都市生産品(Flour/Firewood/Bread/Beer/Tools)の外部買値。薪10・パン50が本テストの床。
        // 他2品目(Flour/Beer/Tools)はこのテストで触れないが1以上の検査を満たす必要がある。
        var externalBuyPrice = new int[Item.Count];
        externalBuyPrice[Item.Flour] = 1;
        externalBuyPrice[Item.Firewood] = 10;
        externalBuyPrice[Item.Bread] = 50;
        externalBuyPrice[Item.Beer] = 1;
        externalBuyPrice[Item.Tools] = 1;

        // 1次産品(Grain/Timber/IronOre/Charcoal)の外部売値の基準値(値そのものは仕様ではない)。
        var externalSellPriceBase = new int[Item.Count];
        externalSellPriceBase[Item.Grain] = 10;
        externalSellPriceBase[Item.Timber] = 9;
        externalSellPriceBase[Item.IronOre] = 14;
        externalSellPriceBase[Item.Charcoal] = 12;

        // 季節係数は一様1000‰(合計4000)。1次産品・都市生産品のどちらの検査も満たす。
        var externalSellPriceSeasonPermille = new int[Item.Count][];
        for (int itemId = 0; itemId < Item.Count; itemId++)
        {
            externalSellPriceSeasonPermille[itemId] = new[] { 1000, 1000, 1000, 1000 };
        }

        return new WorldDefinition(
            itemCount: Item.Count,
            householdsPerOccupation: 2, // recipes.Length(5)×2=10戸(区画数9〜18)。
            recipes: recipes,
            initialLiquidFunds: 0, // 未使用(世帯はAddHouseholdで手組みするため)。
            initialAcquisitionCost: Enumerable.Repeat(1, Item.Count).ToArray(),
            initialHouseholdInventory: new int[Item.Count],
            initialWorkshopInputDays: 0,
            initialToolStock: 1,
            initialSkillPermilleByRank: new[] { 0, 0, 0 },
            laborPermilleByRank: new[] { 1000, 800, 300 },
            dailyConsumptionPerNpcByRank: dailyConsumptionPerNpcByRank,
            firewoodConsumptionSeasonPermille: firewoodConsumptionSeasonPermille,
            minimumMarginPermille: 0,
            observationRetentionDays: 7,
            necessityTargetStockDays: necessityTargetStockDays,
            preferenceTargetStockDays: new int[Item.Count],
            toolTargetStockPermille: 1000,
            rankCoefficientPermille: new[] { 1000, 1000, 1000 },
            tolerancePermille: 1200,
            opportunityCostBaseByOccupation: Enumerable.Repeat(1, recipes.Length).ToArray(),
            travelHoursPerDistrict: 1,
            acquisitionCostSmoothingPermille: 250,
            externalSellPriceBase: externalSellPriceBase,
            externalSellPriceSeasonPermille: externalSellPriceSeasonPermille,
            externalBuyPrice: externalBuyPrice,
            inputBufferDays: 1,
            shipmentDays: 1,
            toolLifeLaborDays: 30,
            equipmentPermilleWithoutTools: 0,
            disposableHours: 12,
            trustDiscountPermille: 200,
            tradeMarginPermille: 1000,
            isExportEnabled: false); // 輸出はこのテストの関心事の外(#39タスク仕様「数値は仕様ではない」)。
    }

    private static int[] BuildRow(params (int ItemId, int Quantity)[] entries)
    {
        var row = new int[Item.Count];

        foreach (var (itemId, quantity) in entries)
        {
            row[itemId] = quantity;
        }

        return row;
    }

    /// <summary>
    /// テスト表 #13(#39)。必需2品目(薪 Id 5 → パン Id 6 の走査順)で、薪の世帯在庫が
    /// <c>2 × 目標在庫</c> に達し、同じ日にパンが <c>fundsCap == 0</c> で0個に切られ(経路(2))、
    /// 翌日の順3で <c>IsBankrupt == 1</c> になる。<see cref="TradeSystemTests.NecessityShortfallIsCountedOnBothPaths"/>
    /// の経路(2)の組み立て(必需2品目が同じ流動資金を奪い合う世界)を出発点にし、薪が上側clampまで
    /// 買えるようにする(タスク仕様「#13の組み立て」節)。
    /// </summary>
    /// <remarks>
    /// <b>買い手に高めの相場観測を仕込む手を採る</b>(タスク仕様が挙げる2つの手のうち)。信用割引を
    /// 効かせる手は採らない ── <see cref="StoreChoice.TrySelect"/> は <c>trust: 0</c> を定数で渡す
    /// ため(GDD01「W2では信用が常に0」)、信用を配線する#44より前の現コードでは効かない。
    /// </remarks>
    [Fact]
    public void BankruptFlagRisesAfterFirewoodCrowdsOutBread()
    {
        var definition = BuildFirewoodCrowdsOutBreadDefinition();
        var world = new World(npcCount: 3, householdCount: 3, itemCount: Item.Count);

        AddHousehold(world, id: 0, districtId: 0, Occupation.Smith, liquidFunds: 160); // 買い手。
        AddHousehold(world, id: 1, districtId: 0, Occupation.Woodworker, liquidFunds: 0); // 薪の売り手。
        AddHousehold(world, id: 2, districtId: 0, Occupation.Baker, liquidFunds: 0);      // パンの売り手。

        world.Households[1].WorkshopInventory[Item.Firewood] = 100_000; // 売り切れさせない。
        world.Households[2].WorkshopInventory[Item.Bread] = 100_000;

        var buyer = world.Households[0];
        var systems = FullPipeline(definition);
        var scheduler = new SimScheduler(systems, new RandomSource(1));

        // 観測を「前日」にするため、システムを何も登録せずに1日空回しする(タスク仕様の日数は
        // 仕様ではない。ExportErrandIsSkippedWhenTheDayIsFull 等と同じ手法)。
        EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 24);

        // 買い手に薪の高い相場観測を仕込む(相場基準1000。許容乖離1200‰でB=1200、薪の売り手は
        // 相場基準を持たないため実効価格は床10のまま。B > 2×10 なので上側clampへ届く)。
        world.Knowledge[buyer.HeadNpcId].Add(new PriceObservation
        {
            ItemId = Item.Firewood,
            LocationId = 0,
            Price = 1000,
            SellerId = 999,
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        });

        scheduler.Advance(world, ticks: 24); // 1日目。薪が上側clamp、パンが経路(2)で0個。

        Assert.Equal(12, buyer.HouseholdInventory[Item.Firewood]); // 2×目標在庫(6)。
        Assert.Equal(0, buyer.HouseholdInventory[Item.Bread]);
        Assert.Equal(1, buyer.UnaffordableNecessityCount);
        Assert.Equal(0, buyer.IsBankrupt); // 順3がこのtickで読むのは前日の値(まだ存在しない)。

        scheduler.Advance(world, ticks: 24); // 2日目。順3が前日の資金不足を読みフラグを立てる。

        Assert.Equal(1, buyer.IsBankrupt);
    }

    /// <summary>
    /// テスト表 #26(#40)。順4(<see cref="NeedGenerationSystem"/>)を組み込んだ完全なパイプラインを
    /// <see cref="WorldGenerator.Generate"/> の世界で2日回す。初日の順4では遠方在庫が1件も立たず、
    /// 2日目の順4で初日に買えなかった品目に立つ(GDD06 §3.1の1日遅延)。
    /// </summary>
    [Fact]
    public void PipelineRaisesDistantStockNeedOnTheNextDay()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));

        var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));

        // 1日目。順4は前日(=初期状態、全世帯UnfilledPurchase=0)を読むので、遠方在庫は1件も無い。
        scheduler.Advance(world, ticks: 24);
        Assert.DoesNotContain(world.Needs, n => n.ReasonCode == NeedReason.DistantStock);

        // 1日目の順5が書いたUnfilledPurchaseのうち正のもの(世帯Id・品目Idの組)を覚えておく。
        // Dictionary/HashSetは使わない(ADR-0002)。
        var expectedItems = new List<(int HouseholdId, int ItemId)>();
        foreach (var household in world.Households)
        {
            for (int itemId = 0; itemId < definition.ItemCount; itemId++)
            {
                if (household.UnfilledPurchase[itemId] > 0)
                {
                    expectedItems.Add((household.Id, itemId));
                }
            }
        }

        // 空振り防止(値の問題の可能性)。
        Assert.NotEmpty(expectedItems);

        // 2日目。順4が1日目のUnfilledPurchaseを読み、遠方在庫を立てる。
        scheduler.Advance(world, ticks: 24);

        foreach (var (householdId, itemId) in expectedItems)
        {
            Assert.Contains(
                world.Needs,
                n => n.ReasonCode == NeedReason.DistantStock
                    && n.TargetHouseholdId == householdId
                    && n.ItemId == itemId);
        }
    }

    /// <summary>
    /// テスト表 #27(#40)。同一シード・同一設定で5日×2回、最終 <c>StateHasher.Compute</c> が一致する
    /// (順4を含む完全なパイプラインが <c>Dictionary</c>/<c>HashSet</c> の列挙順に依存しないこと)。
    /// </summary>
    [Fact]
    public void PipelineIsDeterministicWithNeedGeneration()
    {
        var definition = WorldDefinition.M0;

        ulong RunAndHash(out int needCount)
        {
            var world = WorldGenerator.Generate(definition, new RandomSource(1));
            var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));
            scheduler.Advance(world, ticks: 5 * 24);

            needCount = world.Needs.Count;
            return StateHasher.Compute(world);
        }

        ulong first = RunAndHash(out int firstNeedCount);
        ulong second = RunAndHash(out int secondNeedCount);

        // 空振り防止。Needが一度も立たないまま「一致した」と主張しても判別力が無い。
        Assert.True(firstNeedCount >= 1, "5日回してもNeedが1件も立たない(値の問題の可能性)。");
        Assert.Equal(firstNeedCount, secondNeedCount);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// 【核心】W2-24(#237)タスク仕様テスト表 #17。issue #237 の閉じる条件そのもの。
    /// M0・シード1/2/3/7/42・90日走行(day 0〜89)。醸造の戸(その日の職業で判定)のうち、
    /// その日 <c>ProductionRuns &gt; 0</c> だった最後の day(0始まり)が60以上の戸が1戸以上ある。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>90日で測る理由(フェーズ1で決めたこと)。</b>issueの閉じる条件は「60日で、day 60以上」
    /// だが、60日走行の最後の日は day 59(0始まり)であり満たせない。90日走行(day 0〜89)で測る ──
    /// day 90 から冬に入る(GDD03 §1.2)ので、#218決定6b「冬 = #222 をまたがない」は保たれる。
    /// 「day 60以上」の物差しは変えていない(差し引く前は全シードday 6〜16に止まった。
    /// #218決定ログ2)。
    /// </para>
    /// <para>
    /// <b>day の数え方。</b>k回目(1〜90)の <c>Advance</c> の直後に見える <c>ProductionRuns</c> は
    /// day <c>k − 1</c> の順1が書いた値である(0始まり)。
    /// </para>
    /// </remarks>
    /// <remarks>
    /// M7(規則3を戻す。<c>BuyerDemand</c>の両ループを<c>max(0,...)</c>無しへ戻す変異)は
    /// 全5シードで赤になる見込み(差し引く前は全シードday 6〜16に止まった。#218決定ログ2)。
    /// M1(進捗‰の持ち越し単体)での結果は測っていない(決定ログ2に「差し引きのみ」の行が
    /// 無いため)。mutatorの実測はレビューの巡が閉じた後にまとめて渡される(結果はこの
    /// remarks へ転記する)。
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(42)]
    public void SomeBrewerIsStillProducingAfterDaySixty(long seed)
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(seed));
        var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(seed));

        int householdCount = world.Households.Length;
        var hasRunOnADay = new bool[householdCount];
        var lastRunDay = new int[householdCount];

        int day0BrewerCount = 0;
        foreach (var household in world.Households)
        {
            if (household.Occupation == Occupation.Brewer)
            {
                day0BrewerCount++;
            }
        }

        for (int k = 1; k <= 90; k++)
        {
            scheduler.Advance(world, ticks: 24);
            int day = k - 1; // 0始まり(フェーズ1で決めたこと)。

            foreach (var household in world.Households)
            {
                if (household.Occupation == Occupation.Brewer && household.ProductionRuns > 0)
                {
                    hasRunOnADay[household.Id] = true;
                    lastRunDay[household.Id] = day;
                }
            }
        }

        Assert.Equal(90, world.Now.DayIndex);

        // 空振り防止(タスク仕様「作るもの8.」)。day 0 に醸造が2戸。
        Assert.Equal(2, day0BrewerCount);

        int maxLastRunDay = -1;
        var details = new System.Text.StringBuilder();

        for (int id = 0; id < householdCount; id++)
        {
            if (!hasRunOnADay[id])
            {
                continue;
            }

            details.Append($" household{id}=day{lastRunDay[id]};");
            maxLastRunDay = Math.Max(maxLastRunDay, lastRunDay[id]);
        }

        Assert.True(
            maxLastRunDay >= 60,
            $"seed={seed}: 醸造の最終稼働日の最大値={maxLastRunDay}(60未満)。"
                + $"世帯ごとの最終稼働日(day 0の醸造と、途中で醸造になった世帯):{details}");
    }
}
