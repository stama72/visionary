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
    /// <b>W2-09 追随(2026-09-20)。</b>外出が「1日1区画まで」になった(GDD06 §3 5.6、
    /// <see cref="TradePipelineTests.UnaffordableNecessityCountsOnlyTheFundsShortfall"/>の
    /// remarks参照)ことで、必需(薪)と嗜好(ビール)が別々の区画にしか売り手を持たない世帯は、
    /// その日どちらか一方しか外出できない。<b>資金の絞り方・観察に要する日数を実測し直した</b>
    /// (シード1・世帯Id0=Brewer・区画4)。
    /// <list type="bullet">
    /// <item>絞った資金(100): 1日目のうちに薪が約定する(初期28→34)。3日目までビールは
    /// 一度も約定しない。</item>
    /// <item>潤沢な資金(100000): 薪は2日目までに約定する。ビールは3日目に初めて約定する
    /// (1000では3日目までに一度も約定しない ── 外出のたび薪の方が価値が高く、ビールの番が
    /// 回ってこない。潤沢さの基準そのものが変わった)。</item>
    /// </list>
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
            // 世帯在庫が増えていることが必需の約定の証拠になる(GDD02 §6.2.1)。初期の世帯在庫は
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
    /// 【核心】テスト表 #26。流動資金0の世帯 → 必需の行で <c>UnaffordableNecessityCount</c> が
    /// 厳密な期待値になる。売り手の在庫が0で買えなかっただけの世帯 → 0のまま。嗜好が買えなくても
    /// 0のまま。翌日に買えたら0に戻る。
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
    /// <b>変異の実測・追補(2026-09-17、レビュー1巡目 I-b の訂正)。</b>手順9 の
    /// <c>line.Purpose == DemandPurpose.Necessity &amp;&amp;</c> を外す変異(用途を見ずに数える)を
    /// 当てたところ、資金不足のケースの <c>UnaffordableNecessityCount</c> が実測2 → 3 になった
    /// (赤を確認: 世帯Id0(Brewer)は2日目までに嗜好・生産の入力の行も「知っている店」を得ており、
    /// 流動資金0の日はそれらの行も <c>fundsCap == 0</c> を通るため、用途を見ない変異は必需以外の
    /// 行も加算する)。旧い <c>&gt;= 1</c> の期待値ではこの差(2 → 3)を判別できないため、
    /// 厳密な期待値へ変えた。変異を戻して緑に復帰させた。
    /// </remarks>
    /// <remarks>
    /// <b>W2-09 追随(2026-09-20)。資金不足のケースを、流動資金を人為的に0へ落とす形から
    /// 自然発生(シード1・操作なし)の日へ差し替えた。</b>外出が「1日1区画まで」の構造的な
    /// 性質(GDD06 §3 5.6。1周目が全候補中の最良を選ぶ以上、2周目のどの候補の価値も
    /// <c>−Errand.Cost(1周目の往復,…) ≤ 0</c> を超えられない)により、必需2品目(薪・パン)を
    /// 別々の区画で買う世帯は「その日どちらか一方しか外出しない」。<c>候補0件(知っている店が
    /// 無い)</c> と <c>候補は見つかるが現金上限(CashCap)で落ちる</c> を人為的な資金操作なしに
    /// 判別するには、CashCap が流動資金そのものではなく「用途に使える資金 ÷ 1日分の数量」
    /// (GDD02c §2.1)で決まることを利用し、1日分の数量が大きい日にたまたま資金不足になる
    /// 自然な日を探した(実測: 世帯Id2、14日目)。
    /// </remarks>
    [Fact]
    public void UnaffordableNecessityCountsOnlyTheFundsShortfall()
    {
        var definition = WorldDefinition.M0;

        // 資金不足のケース(シード1・操作なし。世帯Id2、14日目に自然発生する。上のremarks参照)。
        {
            var world = WorldGenerator.Generate(definition, new RandomSource(1));
            var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));

            scheduler.Advance(world, ticks: 13 * 24); // 13日目まで。
            Assert.Equal(0, world.Households[2].UnaffordableNecessityCount);

            scheduler.Advance(world, ticks: 24); // 14日目。資金不足が1件自然発生する。
            Assert.Equal(1, world.Households[2].UnaffordableNecessityCount);

            scheduler.Advance(world, ticks: 24); // 15日目。毎日上書きする(GDD02 §6.2.2)ので0に戻る。
            Assert.Equal(0, world.Households[2].UnaffordableNecessityCount);
        }

        // 在庫切れのケース。木工2戸の薪(工房在庫)と入力の木材(工房在庫)を0にして生産による
        // 補充も止め、資金は潤沢にする ── 知っている店が0件になり(StoreChoiceの条件3)、
        // 資金の項(fundsCap)へは到達しない(GDD02 §6.2.1「売り手の在庫が尽きたのは資金不足では
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

        // 嗜好が買えなくても0のまま。素のM0世界の1日目、世帯Id3はビールを一度も買わないが
        // (実測、シード1。W2-07で世帯Id1の前提が崩れたので世帯Id3へ差し替えた ──
        // 校正の変更で1日目の供給・価格が変わり、世帯Id1は1日目のうちにビールを買うようになった)、
        // UnaffordableNecessityCountは用途がNecessityの行しか数えないので0のままである
        // (GDD02 §6.2.1)。
        {
            var world = WorldGenerator.Generate(definition, new RandomSource(1));
            var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));
            scheduler.Advance(world, ticks: 24);

            bool boughtBeer = world.Ledgers[3].Any(
                entry => entry.Direction == LedgerDirection.Purchase && entry.ItemId == Item.Beer);
            Assert.False(boughtBeer);
            Assert.Equal(0, world.Households[3].UnaffordableNecessityCount);
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
