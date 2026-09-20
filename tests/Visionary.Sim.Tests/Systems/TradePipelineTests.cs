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
    /// テスト表 #21。約定1日ぶんの前後で、各品目の(全世帯の世帯在庫 + 工房在庫)の合計が変わらない
    /// (生産・消費を登録せず TradeSystem だけで回す)。
    /// </summary>
    /// <remarks>
    /// <c>WorldGenerator</c> は各世帯の<b>出力品目</b>の工房在庫を初期化しない(生産の入力・工具
    /// だけを初期化する)ので、<c>TradeSystem</c> 単独では誰も売り注文を出せない。実際に約定が
    /// 起きる母数を作るため、各世帯の出力品目へ販売在庫を直接与える ── 世帯Id・区画・職業の
    /// 対応は <see cref="WorldGenerator"/> が生成した実世界のままで、縮退させない。
    /// </remarks>
    [Fact]
    public void TradeNeitherCreatesNorDestroysGoods()
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

        Assert.Equal(before, after);

        // 空振り防止。誰も約定していなければ、このテストは「生産も消費もしなければ壊れない」
        // という自明な主張しか検証していないことになる。
        Assert.True(
            world.Ledgers.Any(entries => entries.Count > 0),
            "1日回しても1件も約定していない(値の問題の可能性。止まって報告する対象)。");
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
    /// 【核心】W2-11 タスク仕様テスト表 #6。M0・シード1・60日。<b>毎日</b>、<c>world.Market</c> の
    /// 全件が <c>床 × BandMultiplier</c> 以下(GDD02c §1.1 の頭打ちが提示価格の帯を保つ)。
    /// </summary>
    /// <remarks>
    /// <b>帯の定数(20倍)の根拠。</b>本実装での実測(2026-09-20、M0・60日・シード1/2/3)で
    /// 頭打ちを入れたときの最大はシード1のパン500(床54の9.26倍、12日目)、シード2はパン435
    /// (床54の8.06倍、14日目)。<b>シード3はタスク仕様「設計の前提」の記載(工具444・1.53倍)と
    /// 食い違い、実際にはシード1・2と同じくパンが最大になる(パン195、床54の3.61倍、11日目。
    /// 工具444・1.53倍は15日目以降パンが市場から消えた後に定常する値であり、60日全体の最大では
    /// ない)。</b>いずれにせよ20倍を超えないので帯の定数そのものは動かさない。20倍は
    /// 頭打ちを外す変異(M-1)に対して判別力を持ち、実測(最大9.26倍)に対して2倍強の余裕がある。
    /// </remarks>
    /// <remarks>
    /// <b>パンの9.26倍は不具合ではない。</b>パン屋は毎日売れている(約定がある)ので§1.1の頭打ちを
    /// 受けず、完売枝のラチェットで上がる。§1.1は「約定が無い日は上げない」であって水準の復元力
    /// ではない(GDD02c §1.2の囲み)。帯の定数がパンで決まっているのはこのためで、他4品目には緩い。
    /// </remarks>
    /// <remarks>
    /// <b>毎日見る形を doc コメントで固定する。</b>15日目以降は売り注文が2件(工具)に落ち、
    /// パン・ビールは市場から消えるので、<b>最終日だけ見ると発散した品目を見ずに緑になりうる</b>
    /// (この変異は最終日だけ見る形では落ちない)。<b>毎日</b>、その時点の <c>world.Market</c> の
    /// 全件を確かめること。
    /// </remarks>
    /// <remarks>
    /// <b>この検出器が緑であることは「帯が保たれる経済」を意味しない。</b>
    /// <see href="https://github.com/stama72/visionary/issues/38">#38</see>(都市外市場の窓口)が
    /// 無い世界では生産0/日・約定0/日・売り注文2件で平らになるので、緑が保証するのは式の代数
    /// だけである。本テストは TDD01 §5.2 の発散・硬直の判定関数ではない(あちらは約定価格の
    /// 中央値・120日窓で <see href="https://github.com/stama72/visionary/issues/41">#41</see> が
    /// 持つ)。<b>GDD02 §8-1 を満たしたことにはならない。</b>
    /// </remarks>
    [Fact]
    public void OfferPricesStayWithinTheBandOverSixtyDays()
    {
        const int BandMultiplier = 20; // 倍。床に対する提示価格の帯の上限(このテストの検出器の閾値)。

        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));

        var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));

        for (int day = 1; day <= 60; day++)
        {
            scheduler.Advance(world, ticks: 24);

            // 1次産品は市場に載らない(TradeSystem段1が出力品目だけをMarketへ書く構造的な
            // 保証。WorldDefinition.ExternalBuyPriceは1次産品に対して例外を投げるので、
            // ここでitemIdの種別を分岐する必要は無い)。
            foreach (var entry in world.Market)
            {
                int floorPrice = definition.ExternalBuyPrice(entry.Key.ItemId);
                int bandUpperBound = floorPrice * BandMultiplier;

                Assert.True(
                    entry.Value <= bandUpperBound,
                    $"day={day} itemId={entry.Key.ItemId} sellerId={entry.Key.SellerId} "
                        + $"price={entry.Value} floor={floorPrice} band<= {bandUpperBound}"
                        + "(GDD02c §1.1の頭打ちが提示価格の帯を保っていない)。");
            }
        }
    }

    /// <summary>
    /// テスト表 #24。60日回した後、<c>Knowledge</c> の総件数が
    /// NPC数 × (世帯数 − 1) × (保持期間 + 1) 以下。
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
    /// <see cref="OfferPricesStayWithinTheBandOverSixtyDays"/>(W2-11)が見るようになった。
    /// <b>売り注文が2件へ枯れることは引き続き本テストは検出しない</b>
    /// (<see href="https://github.com/stama72/visionary/issues/38">#38</see> の窓口が入った後に
    /// #120 が締める)。<c>Errand.Surplus</c> を<see cref="long"/>にしたことで60日走行は緑に戻るが、
    /// それは型が広いあいだ通るだけであり、値付け(GDD02c §1)の発散そのものを止めたわけではない。
    /// </remarks>
    /// <remarks>
    /// <b>実測値(2026-09-20、W2-11の実装での再実測)。</b>M0・シード1・60日で
    /// <c>totalKnowledge</c> = 210。<c>upperBound</c> の式自体は構造(NPC数・世帯数・保持期間)だけで
    /// 決まり価格には依らないが、<c>totalKnowledge</c>(60日走行の実測値)は「売り注文が2件へ
    /// 枯れる」現行の経済に従属している。<see href="https://github.com/stama72/visionary/issues/38">#38</see>
    /// が入って売り注文が枯れなくなれば <c>totalKnowledge</c> は再び動きうる
    /// (<c>upperBound</c> を超えないことは構造上保たれる)。
    /// </remarks>
    [Fact]
    public void ObservationsDoNotGrowWithoutBound()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));

        var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));
        scheduler.Advance(world, ticks: 60 * 24);

        long totalKnowledge = world.Knowledge.Sum(observations => (long)observations.Count);

        // NPC1人が知りうる売り手は自分の世帯を除く(世帯数−1)戸まで。各売り手について
        // 保持期間+1日ぶん(Observations.Expireが「差 > 保持期間」で消すので、差0〜保持期間の
        // (保持期間+1)日ぶんが同時に残りうる)。
        long upperBound = (long)definition.NpcCount
            * (definition.HouseholdCount - 1)
            * (definition.ObservationRetentionDays + 1);

        Assert.True(
            totalKnowledge <= upperBound,
            $"Knowledgeの総件数({totalKnowledge})が上限({upperBound})を超えた"
                + "(保持期間の失効が効いていない可能性)。");
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
    /// <b>変異の再実測(2026-09-20、持ち越し指摘)。</b><c>[#81](https://github.com/stama72/visionary/issues/81)</c>
    /// の検出器としての判別力が、<c>AmpleLiquidFunds</c> を1000 → 100,000、観察日数を2日 → 3日へ
    /// 広げたことで吸収されていないかを確かめるため、<c>TradeSystem.RunOneHouseholdsShopping</c>
    /// の <c>demand.Lines</c> の走査を <c>.Reverse()</c> する変異(製品コードで用途の走査順を
    /// 必需→耐久→入力→嗜好から逆順へ変える変異に相当)を当て直した。絞った世界(資金100)の
    /// <c>Assert.False(boughtBeer, ...)</c> が実際値trueで失敗した(赤を確認: 嗜好が必需より先に
    /// 決済され、流動資金が先に嗜好へ回って必需を圧迫する経路が再現する)。判別力は維持されている。
    /// 変異を戻して緑に復帰させた。
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
    [Fact]
    public void UnaffordableNecessityCountsOnlyTheFundsShortfall()
    {
        var definition = WorldDefinition.M0;

        // 資金不足のケース(シード1・操作なし。世帯Id3、10日目に自然発生する。上のremarks参照)。
        {
            var world = WorldGenerator.Generate(definition, new RandomSource(1));
            var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));

            scheduler.Advance(world, ticks: 9 * 24); // 9日目まで。
            Assert.Equal(0, world.Households[3].UnaffordableNecessityCount);

            scheduler.Advance(world, ticks: 24); // 10日目。資金不足が1件自然発生する。
            Assert.Equal(1, world.Households[3].UnaffordableNecessityCount);

            scheduler.Advance(world, ticks: 24); // 11日目。毎日上書きする(GDD02b §3.3)ので0に戻る。
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
    /// <b>この構成でも判別力は回復していない(3巡目の実測。seed 1・30日・全317件に対して
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
    /// 一致する保証が無い。<b>移動費と実質コストの argmin を守っているのは単体 #10
    /// (<see cref="StoreChoiceTests.SelectsTheCheapestRealCostNotTheCheapestPrice"/>)である。</b>
    /// 本テストを「空間の摩擦の検出器」として読まないこと ── 本テストが主張できるのは
    /// 「統合系で区画ごとの約定単価が一致しない」という存在命題までであり、原因の分解ではない。
    /// #38(都市外市場)で候補集合の構造が変われば交絡も変わるので、そのとき判別力を持たせ
    /// られるかを測り直す(issue へ)。
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
}
