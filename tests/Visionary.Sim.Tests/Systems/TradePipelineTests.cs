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
    /// 構造的に成立しない。母数の取り違え(#36引き継ぎ表A)の検出は #29
    /// (<see cref="BuyerDemandTests.DurableBaseValueIsMeasuredOnLiquidFundsNotSurplus"/>)が単体で担う。
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
    /// 売り注文数 × (保持期間 + 1) × 構成員数 以下。
    /// </summary>
    [Fact]
    public void ObservationsDoNotGrowWithoutBound()
    {
        var definition = WorldDefinition.M0;
        var world = WorldGenerator.Generate(definition, new RandomSource(1));

        var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));
        scheduler.Advance(world, ticks: 60 * 24);

        long totalKnowledge = world.Knowledge.Sum(observations => (long)observations.Count);

        int membersPerHousehold = definition.NpcCount / definition.HouseholdCount; // M0: 2(親方+徒弟)
        long upperBound = (long)definition.HouseholdCount
            * (definition.ObservationRetentionDays + 1)
            * membersPerHousehold;

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
    /// <b>落ちる場所は日数で変わる(レビュー2巡目の実測。1巡目に書いた機構の記述は誤っていた
    /// ので訂正した)。</b>
    ///
    /// | 世界 | 嗜好(ビール)が落ちる場所 | 資金を積むと |
    /// | ---- | -------------------------- | ------------ |
    /// | 1日だけ回す | 手順2(<c>TrySelect</c>が false)。初日は他の醸造家の売り注文の観測が
    /// まだ無く、知っている店が0件 | 変わらない(資金を1000倍にしても買えない) |
    /// | 2日以上回す | 手順3(<c>store.UnitRealCost &gt; line.Budget</c>) | 約580で通過する |
    ///
    /// <b>資金の帯が重ならないのは2日目以降の話である。</b>絞った世界(下記
    /// <c>ScarceLiquidFunds</c>)は<b>2日</b>回すことで手順3まで到達させ、assertに資金への感度を
    /// 持たせている ── 1日しか回さない世界では、必需(薪)の相場基準はその日のうちに得られても
    /// (中心区画から木工2戸がVisionRadius内)、嗜好(ビール)は他の醸造家の売り注文の観測(記憶)が
    /// まだ無く手順2で落ちるため、資金を100→100000(1000倍)にしても
    /// <c>Assert.False(boughtBeer, ...)</c>は空振りのまま緑になる。<b>2日回すよう直した後、
    /// 実際に100000へ変えて赤になることを確認した(赤を確認、2026-09-17)。確認後、値を100へ
    /// 戻した。</b>
    /// </remarks>
    /// <remarks>
    /// <b>それでも <c>Lines</c> 逆順変異は落ちない(レビュー1・2巡目の実測。全317件が緑のまま)。</b>
    /// 判別力を持つ資金帯がW2には存在しない ── 必需の基礎値は流動資金に比率を掛け、嗜好の基礎値は
    /// <c>SurplusFunds = max(0, 流動資金 − 必要運転資金)</c>に比率を掛けるため(GDD02 §8.2.1・
    /// §8.2.7)、必要運転資金が大きいW2では嗜好が手順3を通過できる水準(約580)で必需の1日あたりの
    /// 必要額(冬季でも約33)が無視できるほど小さく、両者が資金を奪い合う帯が見つからない
    /// (探索範囲: 資金50〜1200・秋冬・1〜5日目・正順逆順、issue #37検出器はissueへ落とす)。
    /// <b>走査順そのものは実装が守っている</b>(段5は<c>Lines</c>を並べ替えていない)。守られて
    /// いないのは「テストで守られている」という保証のほうである。
    /// </remarks>
    /// <remarks>
    /// <b>世帯Id0(Brewer、区画4=中心)を使う。</b>中心区画は初日から <see cref="District.VisionRadius"/>
    /// で木工(薪の売り手)2戸を見通せるので、初日のうちに必需(薪)の約定が成立しうる世帯である。
    /// </remarks>
    [Fact]
    public void NecessityIsSettledBeforePreference()
    {
        const int TargetHouseholdId = 0;
        const int ScarceLiquidFunds = 100; // 必需は買えるが嗜好へは届かない額(値の検算対象、#28)
        const int AmpleLiquidFunds = 1000; // 嗜好も届く額(対照。値の検算対象、#28)

        var definition = WorldDefinition.M0;

        // 絞った世界: 必需だけ成立し、嗜好は0件。
        {
            var world = WorldGenerator.Generate(definition, new RandomSource(1));
            world.Households[TargetHouseholdId].LiquidFunds = ScarceLiquidFunds;

            // 2日回す ── 1日だけでは嗜好(ビール)が手順2(知っている店が0件)で落ち、資金と
            // 無関係に0件になる(上記remark)。2日目以降で初めて手順3(予算判定)まで到達し、
            // assertが資金の関数になる。
            var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));
            scheduler.Advance(world, ticks: 2 * 24);

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
            scheduler.Advance(world, ticks: 2 * 24);

            bool boughtBeer = world.Ledgers[TargetHouseholdId].Any(
                entry => entry.Direction == LedgerDirection.Purchase && entry.ItemId == Item.Beer);
            Assert.True(
                boughtBeer,
                "資金を増やしても嗜好(ビール)の約定が成立しなかった"
                    + "(手順3の予算判定以外で嗜好が塞がれている可能性)。");
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
    /// 厳密な期待値(2)へ変えた。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void UnaffordableNecessityCountsOnlyTheFundsShortfall()
    {
        var definition = WorldDefinition.M0;

        // 資金不足のケース。世帯Id0(区画4=中心)は1日目のうちに薪の相場基準を得られる
        // (木工2戸がVisionRadius=1に入るため)。2日目の直前に流動資金を0へ落とすと、相場基準に
        // 基づく必需の予算(流動資金に依存しない、GDD02 §8.2.7)は薪の実質コストへ届くが、
        // 実際の支払いは流動資金0で不可能になる ── UnaffordableNecessityCountが検出すべき
        // ずれそのものである(GDD02 §6.2.2)。
        {
            var world = WorldGenerator.Generate(definition, new RandomSource(1));
            var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(1));

            scheduler.Advance(world, ticks: 24); // 1日目。相場基準を作る。
            Assert.Equal(0, world.Households[0].UnaffordableNecessityCount);

            world.Households[0].LiquidFunds = 0;
            scheduler.Advance(world, ticks: 24); // 2日目。資金不足で必需が買えない。

            // 厳密な期待値(実測、シード1)。世帯Id0(Brewer)の必需の行は薪とパンの2件であり、
            // 2日目までに両方とも相場基準(知っている店)を得ているので、流動資金0の日は
            // 両方とも資金不足で落ちる ── 期待値2。閾値を`>= 1`にすると、用途を見ずに数える
            // 変異(嗜好・生産の入力の行も無条件で加算する)が「候補ありかつ資金0」の行を
            // 余分に足しても`>= 1`のままなので判別できない(レビュー1巡目 I-b の訂正)。
            const int ExpectedUnaffordableNecessityCount = 2;
            Assert.Equal(
                ExpectedUnaffordableNecessityCount, world.Households[0].UnaffordableNecessityCount);

            // 翌日、流動資金を戻すと0に戻る(毎日上書きする。GDD02 §6.2.2)。
            world.Households[0].LiquidFunds = 300;
            scheduler.Advance(world, ticks: 24); // 3日目。
            Assert.Equal(0, world.Households[0].UnaffordableNecessityCount);
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

        // 嗜好が買えなくても0のまま。素のM0世界の1日目、世帯Id1はビールを一度も買わないが
        // (実測、シード1)、UnaffordableNecessityCountは用途がNecessityの行しか数えないので
        // 0のままである(GDD02 §6.2.1)。
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
    /// <b>この構成でも判別力は回復していない(レビュー2巡目の実測。seed 1・30日)。</b>
    /// <see cref="StoreChoice.TrySelect"/> への変異でいずれも本テストは緑のままだった:
    ///
    /// | 当てた変異 | このテスト | 赤になったテスト |
    /// | ---------- | ---------- | ---------------- |
    /// | 実質コストから移動費を落とす | 緑 | ─ |
    /// | <c>realCost = price</c>(argmin を提示価格にする) | 緑 | #10のみ |
    /// | <c>isKnown</c> を常に真 + 距離を固定(候補集合と移動費を全買い手で共通化) | 緑 | #12・#15・#24 |
    /// | 上の2つを同時に(買い手の区画が店の選択に一切効かない) | 緑 | ─ |
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
