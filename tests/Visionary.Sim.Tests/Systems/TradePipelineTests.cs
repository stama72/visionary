using Visionary.Sim.Determinism;
using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

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
    private static ISimSystem[] FullPipeline(WorldDefinition definition) => new ISimSystem[]
    {
        new ProductionSystem(definition),
        new ConsumptionSystem(definition),
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
    /// <b>旧い記録(2026-09-17、#98・<c>13e9251</c>、頭打ちが入る<b>前</b>の経済で測ったもの)は
    /// 参考として残す:</b>同じ変異で同じ <c>Assert.False(boughtBeer, ...)</c> が実際値trueで
    /// 失敗していた(赤を確認)。<b>今回(<c>87d4837</c>)の実測がこれを置き換える。</b>
    /// </remarks>
    [Fact]
    public void NecessityIsSettledBeforePreference()
    {
        const int TargetHouseholdId = 0;
        const int ScarceLiquidFunds = 100; // 必需は買えるが嗜好へは届かない額(値の検算対象、#28)
        const int AmpleLiquidFunds = 100_000; // 嗜好も届く額(対照。値の検算対象、#28。remarks参照)

        var definition = WorldDefinition.M0;

        // 絞った世界: 必需だけ成立し、嗜好は0件。
        {
            var world = WorldGenerator.Generate(definition, new RandomSource(1));
            world.Households[TargetHouseholdId].LiquidFunds = ScarceLiquidFunds;

            var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));
            scheduler.Advance(world, ticks: 3 * 24);

            var household = world.Households[TargetHouseholdId];

            // 必需(薪)の約定が成立した ── TradeSettlement.Executeが用途で行き先を振り分けるので、
            // 世帯在庫が増えていることが必需の約定の証拠になる(GDD02b §3.2)。初期の世帯在庫は
            // その日のうちにConsumptionSystemが使い切るので、値が残っていれば買い直した証拠になる。
            Assert.True(
                household.HouseholdInventory[Item.Firewood] > 0,
                "必需(薪)の約定が成立しなかった(値の問題の可能性)。");

            bool boughtBeer = world.Ledgers[TargetHouseholdId].Any(
                entry => entry.Direction == LedgerDirection.Purchase && entry.ItemId == Item.Beer);
            Assert.False(boughtBeer, "嗜好(ビール)の約定が成立した(資金を絞った意味が無い)。");
        }

        // 対照: 同じ世帯の初期資金だけを増やすと、嗜好(ビール)の約定が成立する。
        {
            var world = WorldGenerator.Generate(definition, new RandomSource(1));
            world.Households[TargetHouseholdId].LiquidFunds = AmpleLiquidFunds;

            var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));
            scheduler.Advance(world, ticks: 3 * 24);

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
    [Fact]
    public void UnaffordableNecessityCountsOnlyTheFundsShortfall()
    {
        var definition = WorldDefinition.M0;

        // 資金不足のケース(シード1・操作なし。世帯Id6、8日目に自然発生する。上のremarks参照)。
        {
            var world = WorldGenerator.Generate(definition, new RandomSource(1));
            var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));

            scheduler.Advance(world, ticks: 7 * 24); // 7日目まで。
            Assert.Equal(0, world.Households[6].UnaffordableNecessityCount);

            scheduler.Advance(world, ticks: 24); // 8日目。資金不足が1件自然発生する。
            Assert.Equal(1, world.Households[6].UnaffordableNecessityCount);

            scheduler.Advance(world, ticks: 24); // 9日目。毎日上書きする(GDD02b §3.3)ので0に戻る。
            Assert.Equal(0, world.Households[6].UnaffordableNecessityCount);
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

        // 嗜好が買えなくても0のまま。素のM0世界の1日目、世帯Id1はビールを一度も買わないが
        // (実測、シード1。W2-09で価値の式(A-1)が変わり、1日目にビールを買わない世帯が
        // 世帯Id3から世帯Id1へ動いた)、UnaffordableNecessityCountは用途がNecessityの行しか
        // 数えないので0のままである(GDD02b §3.2)。
        {
            var world = WorldGenerator.Generate(definition, new RandomSource(1));
            var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));
            scheduler.Advance(world, ticks: 24);

            bool boughtBeer = world.Ledgers[1].Any(
                entry => entry.Direction == LedgerDirection.Purchase && entry.ItemId == Item.Beer);
            Assert.False(boughtBeer);
            Assert.Equal(0, world.Households[1].UnaffordableNecessityCount);
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

    /// <summary>失敗メッセージ用。鍛冶ごとの工房在庫[工具]を世帯Id昇順で並べる(ADR-0002)。</summary>
    private static string ToolInventorySnapshot(World world)
    {
        var snapshot = new System.Text.StringBuilder();

        foreach (var household in world.Households)
        {
            if (household.Occupation == Occupation.Smith)
            {
                snapshot.Append($" household{household.Id}={household.WorkshopInventory[Item.Tools]}");
            }
        }

        return snapshot.ToString();
    }

    /// <summary>
    /// 【核心】W2-14 タスク仕様テスト表 #10(検出器)。M0・シード1/2/3/7/42・60日。各日の終わりに
    /// <c>world.Market</c> のうち工具の売り注文を数える ── どの日も1件以上(#148 訂正9)。
    /// </summary>
    /// <remarks>
    /// <b>判定対象が <c>world.Market</c> である以上、走行後に1回走査する形は採れない。</b>
    /// <see cref="TradeSystem"/> 段2 が毎日 <c>world.Market.Clear()</c> を呼ぶので、売り注文は
    /// その日のうちにしか存在しない(<see cref="MoneyChangesOnlyByTheExternalLedger"/> 等の
    /// <c>world.Ledgers</c>(追記のみで剪定されない)を走行後に1回走査する検出器とは構造が違う
    /// 理由がこれである)。
    /// </remarks>
    /// <remarks>
    /// <b>下限は1件/日。緩めない(#148 訂正9)。</b>健全な走行では工具の売り注文は毎日立つ。
    /// 0件の日は鍛冶の工房在庫が1個まで痩せた兆候であり、それ自体が検出したい事象である。
    /// 訂正9は「0件の日を許容する」という初版の向きを反転させたものなので、閾値を緩める方向の
    /// 変更は仕様の後退である。
    /// </remarks>
    /// <remarks><b>最初の違反で止めない。</b>60日を走り切り、最小件数とその日を記録して最後に
    /// 1回だけ assert する(<see cref="SettledPricesAtTheCentreStayWithinTheBandOverSixtyDays"/>
    /// が「最初の違反で止まると超過の上限が測れない」で採った規律と同じ)。</remarks>
    /// <remarks>
    /// <b>変異M-1の実測</b>(<c>mutator</c>、2026-09-21、HEAD <c>0da66a4</c>)。<c>SellableStock.
    /// ReserveQuantity</c> が常に0を返す(留保を消す)変異を当てると、本テストはシード7のみ赤で、
    /// シード1・2・3・42 は緑のままだった。<b>これは検出器の壊れではなく、期待の向きが逆になる
    /// ためである。</b> 留保がある世界では工房在庫1→販売在庫0で段1が売り注文を立てないので
    /// 「0件の日」は在庫が1個まで痩せた兆候になるが、M-1 で留保そのものを消すと工房在庫1でも
    /// 売り注文は立つため、0件になるのは在庫が0の日だけになる ── 変異は本テストの下限を
    /// 満たしやすくする方向に働く。留保の核心は
    /// <see cref="SmithNeverRunsOutOfToolsOverSixtyDays"/>(全5シード赤)が担保する。**本テストの
    /// 断定は、どの変異(M-1〜M-6)でも担保されていない**(将来この下限を緩めても mutator の
    /// 測定結果は変わらない)。この穴は issue へ落とした(W2-14 タスク仕様「M-1がテスト10を
    /// 動かさない理由」)。それでも本テストは「鍛冶の生産が完全に止まったこと」の検出器として
    /// 意味を持つので外さない。
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(42)]
    public void ToolOffersNeverDisappearOverSixtyDays(long seed)
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(seed));
        var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(seed));

        int smithHouseholdCount = world.Households.Count(household => household.Occupation == Occupation.Smith);

        bool hasMinDay = false;
        int minCount = 0;
        long minDay = 0;
        string minDayToolInventorySnapshot = string.Empty;
        long totalToolOfferCount = 0;

        for (int day = 1; day <= 60; day++)
        {
            scheduler.Advance(world, ticks: 24);

            int toolOfferCount = CountMarketOffers(world, Item.Tools);
            totalToolOfferCount += toolOfferCount;

            if (!hasMinDay || toolOfferCount < minCount)
            {
                hasMinDay = true;
                minCount = toolOfferCount;
                minDay = day;
                // 違反日その日の工房在庫を控える。走行後に1回走査すると最終日の在庫になり、
                // 違反日の在庫と読み違える(レビュー指摘)。
                minDayToolInventorySnapshot = ToolInventorySnapshot(world);
            }
        }

        // 空振り防止(#148 の閉じる条件・タスク仕様テスト表 #12)のうち (i)(ii) は核心と独立
        // なので先に置く。(i) 鍛冶が2戸存在する、(ii) 60日ぶん進んだ。
        Assert.Equal(2, smithHouseholdCount);
        Assert.Equal(60, world.Now.DayIndex);

        Assert.True(
            minCount >= 1,
            $"seed={seed} day={minDay}: 工具の売り注文が{minCount}件(0件の日があった。留保が"
                + "効いていない/鍛冶の在庫が1個まで痩せた/生産が止まった可能性)。"
                + minDayToolInventorySnapshot);

        // 空振り防止 (iii) 延べ件数60以上は、核心(日ごとの下限1以上)より後に置く。核心が真
        // (60日すべて1件以上)なら延べ件数は論理的に必ず60以上になるため、これを核心より前に
        // 置くと核心の失敗をこちらが横取りしてしまう(レビュー指摘)。
        Assert.True(
            totalToolOfferCount >= 60,
            $"seed={seed}: 60日間の工具の売り注文の延べ件数({totalToolOfferCount})が60未満"
                + "(母集団が空振りの可能性)。");
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

        // 空振り防止(タスク仕様テスト表 #12。ToolOffersNeverDisappearOverSixtyDaysと同じ3条件)
        // のうち (i)(ii) は核心と独立なので先に置く。
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

        // 空振り防止 (iii) 延べ件数60以上は、核心(工具在庫の下限)より後に置く。工房在庫が
        // 残ることと工具の売り注文が立つことは別の事象なので、核心が真でも(iii)が偽になり得る
        // (冗長ではない)。ここより前に置くと変異M-1(留保を消す)で先に落ち、核心が評価されない
        // (レビュー指摘)。
        Assert.True(
            totalToolOfferCount >= 60,
            $"seed={seed}: 60日間の工具の売り注文の延べ件数({totalToolOfferCount})が60未満"
                + "(母集団が空振りの可能性)。");
    }
}
