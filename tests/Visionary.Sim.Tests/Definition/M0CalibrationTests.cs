using Visionary.Sim.Numerics;
using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Definition;

/// <summary>
/// <see cref="WorldDefinition.M0"/> の校正(GDD02d §4.3 の条件(a)〜(e)、#96 タスク仕様 §8)を
/// 整数演算で検査する。
/// </summary>
/// <remarks>
/// <para>
/// <b>このテストの仕事は、校正表の値を動かしたPRで落ちることである</b>(タスク仕様)。
/// 逆に言えば、値が正しく校正されている限りは常に緑であるべきで、<c>WorldDefinition.M0</c> の
/// 値そのものを直接 assert するテストではない ── 定義から定常状態の需給・収支・許容原価を
/// 計算し直し、GDD02d §4.3 の不等式を満たすかどうかだけを見る。
/// </para>
/// <para>
/// <b>浮動小数点を使わない。</b>テストアセンブリは <c>BannedSymbols</c> の対象外だが、規約は
/// 同じ(タスク仕様)。合計は <c>long</c>。
/// </para>
/// </remarks>
public sealed class M0CalibrationTests
{
    private static readonly WorldDefinition Definition = WorldDefinition.M0;

    // 全季節を巡る固定の並び。合計・最大値の走査順に仕様上の意味は無いが、
    // ADR-0002の列挙順規約に合わせて配列で持つ(Dictionary等は使わない)。
    private static readonly Season[] AllSeasons =
    {
        Season.Spring, Season.Summer, Season.Autumn, Season.Winter,
    };

    /// <summary>
    /// 代表世帯(全世帯が親方1・徒弟1なので、どの世帯でも消費量は同じ。タスク仕様)。
    /// <c>DailyConsumption</c> の式を再実装しない ── 世帯の消費量は
    /// <see cref="Systems.DailyConsumption.Quantity"/> をそのまま呼ぶ。
    /// </summary>
    private static (World World, HouseholdState Household) RepresentativeHousehold()
    {
        var world = WorldGenerator.Generate(Definition, new RandomSource(masterSeed: 1));
        return (world, world.Households[0]);
    }

    private static int DailyConsumptionQuantity(
        World world, HouseholdState household, int itemId, Season season) =>
        DailyConsumption.Quantity(Definition, world, household, itemId, season);

    /// <summary>世帯の年間消費(タスク仕様 §8)= Σ_季節 DaysPerSeason × 世帯の1日消費(i, 季節)。</summary>
    private static long AnnualConsumption(World world, HouseholdState household, int itemId)
    {
        long total = 0;
        foreach (var season in AllSeasons)
        {
            total += (long)Calendar.DaysPerSeason * DailyConsumptionQuantity(world, household, itemId, season);
        }

        return total;
    }

    /// <summary>
    /// 年間価格(タスク仕様 §8)。1次産品: Σ_季節 DaysPerSeason × ExternalSellPrice(j, 季節)。
    /// 都市生産品: DaysPerYear × ExternalBuyPrice(j)。
    /// </summary>
    private static long AnnualPrice(int itemId)
    {
        if (Definition.IsPrimaryItem(itemId))
        {
            long total = 0;
            foreach (var season in AllSeasons)
            {
                total += (long)Calendar.DaysPerSeason * Definition.ExternalSellPrice(itemId, season);
            }

            return total;
        }

        return (long)Calendar.DaysPerYear * Definition.ExternalBuyPrice(itemId);
    }

    /// <summary>
    /// 床(タスク仕様 §8)。1次産品: ExternalSellPriceBase(= ExternalSellPriceの年平均 =
    /// <see cref="AnnualPrice"/> ÷ DaysPerYear)。都市生産品: ExternalBuyPrice(i)。
    /// </summary>
    private static long Floor(int itemId) =>
        Definition.IsPrimaryItem(itemId)
            ? AnnualPrice(itemId) / Calendar.DaysPerYear
            : Definition.ExternalBuyPrice(itemId);

    /// <summary>
    /// 季節最高値(タスク仕様 §8)。1次産品: max_季節 ExternalSellPrice(j, 季節)。
    /// 都市生産品: ExternalBuyPrice(j)(季節係数は掛からない。GDD02d §5)。
    /// </summary>
    private static int SeasonMax(int itemId)
    {
        if (!Definition.IsPrimaryItem(itemId))
        {
            return Definition.ExternalBuyPrice(itemId);
        }

        int max = 0;
        foreach (var season in AllSeasons)
        {
            max = Math.Max(max, Definition.ExternalSellPrice(itemId, season));
        }

        return max;
    }

    /// <summary>
    /// テスト表 #22。(a) 都市生産品ごとに年平均の供給 ≥ 需要(生産能力 ≥ 需要)。
    /// </summary>
    /// <remarks>
    /// 値の側の変異: 木材加工の所要労働‰を108→120にすると(10回/日)、薪の年間供給
    /// 7,200(=2世帯×10×3×120日)が需要8,280を下回り落ちる(タスク仕様)。
    /// </remarks>
    [Fact]
    public void M0SatisfiesSurplusIsNonNegative_A()
    {
        var (world, household) = RepresentativeHousehold();

        for (int itemId = 0; itemId < Definition.ItemCount; itemId++)
        {
            if (Definition.IsPrimaryItem(itemId))
            {
                continue; // (a)は都市生産品ごとの条件(タスク仕様)
            }

            long supply = 0;
            long demand = (long)Definition.HouseholdCount * AnnualConsumption(world, household, itemId);
            long toolLaborInput = 0;

            for (int occupationId = 0; occupationId < Definition.OccupationCount; occupationId++)
            {
                var occupation = (Occupation)occupationId;
                var recipe = Definition.Recipes[occupationId];
                int cap = Definition.ProductionCapacity(occupation);

                foreach (var output in recipe.Outputs)
                {
                    if (output.ItemId == itemId)
                    {
                        supply += (long)Definition.HouseholdsPerOccupation * cap
                            * output.Quantity * Calendar.DaysPerYear;
                    }
                }

                foreach (var input in recipe.Inputs)
                {
                    if (input.ItemId == itemId)
                    {
                        demand += (long)Definition.HouseholdsPerOccupation * cap
                            * input.Quantity * Calendar.DaysPerYear;
                    }
                }

                if (itemId == Item.Tools)
                {
                    toolLaborInput += (long)Definition.HouseholdsPerOccupation * Calendar.DaysPerYear
                        * recipe.LaborPermille * cap;
                }
            }

            if (itemId == Item.Tools)
            {
                demand += IntegerMath.CeilDiv(toolLaborInput, Definition.ToolDurabilityPerUnit);
            }

            Assert.True(
                supply >= demand,
                $"(a) 品目{itemId}: 年間供給{supply} < 年間需要{demand}。");
        }
    }

    /// <summary>
    /// テスト表 #23。(b) 都市生産品ごとに輸入含有原価 &lt; 外部買値。
    /// </summary>
    /// <remarks>
    /// 値の側の変異: 穀物の外部売値を10→40にすると、小麦粉の輸入含有原価80が
    /// 外部買値56以上になり落ちる(タスク仕様)。
    /// </remarks>
    [Fact]
    public void M0SatisfiesImportContentBelowBuyPrice_B()
    {
        var importContent = new long?[Definition.ItemCount];
        for (int itemId = 0; itemId < Definition.ItemCount; itemId++)
        {
            if (Definition.IsPrimaryItem(itemId))
            {
                importContent[itemId] = Floor(itemId);
            }
        }

        var recipes = Definition.Recipes;
        var resolved = new bool[recipes.Length];

        // 入力がすべて定義済みのレシピから順に埋める。レシピ数回まわして残れば循環として fail
        // (タスク仕様)。
        for (int pass = 0; pass < recipes.Length; pass++)
        {
            for (int r = 0; r < recipes.Length; r++)
            {
                if (resolved[r])
                {
                    continue;
                }

                var recipe = recipes[r];
                bool allInputsKnown = true;
                foreach (var input in recipe.Inputs)
                {
                    if (importContent[input.ItemId] is null)
                    {
                        allInputsKnown = false;
                        break;
                    }
                }

                if (!allInputsKnown)
                {
                    continue;
                }

                long total = 0;
                foreach (var input in recipe.Inputs)
                {
                    total += (long)input.Quantity * importContent[input.ItemId]!.Value;
                }

                importContent[recipe.Outputs[0].ItemId] =
                    IntegerMath.CeilDiv(total, recipe.Outputs[0].Quantity);
                resolved[r] = true;
            }
        }

        Assert.True(
            Array.TrueForAll(resolved, r => r),
            "(b) レシピの入力が循環していて輸入含有原価を解けない。");

        for (int itemId = 0; itemId < Definition.ItemCount; itemId++)
        {
            if (Definition.IsPrimaryItem(itemId))
            {
                continue;
            }

            Assert.True(
                importContent[itemId]!.Value < Definition.ExternalBuyPrice(itemId),
                $"(b) 品目{itemId}: 輸入含有原価{importContent[itemId]} ≥ "
                    + $"外部買値{Definition.ExternalBuyPrice(itemId)}。");
        }
    }

    /// <summary>
    /// 【核心】テスト表 #24。(c) 床価格で評価した各職業の世帯収支が 0 ± 生活費の2%。
    /// </summary>
    /// <remarks>
    /// <para>
    /// フェーズ1の検算(GDD02d §4.4): 水車小屋番 −227 / パン屋 −230 / 醸造 −230 /
    /// 木材加工 −230 / 鍛冶 +323(単位: 貨幣/年)。生活費 27,000/年。
    /// </para>
    /// <para>
    /// 値の側の変異: 薪の外部買値を10→11にすると木材加工が+4,050/年(+15%)で落ちる。
    /// </para>
    /// <para>
    /// <b>式の側の変異(このテスト自身の判別力の確認)。</b>摩耗を「年間の投入労働 ×
    /// 工具の床 ÷ 耐久値」(1回で切り上げる)ではなく「1回あたりの摩耗費(切り上げ)× 回数」
    /// (02c §2.3 の利潤上限のための保守的な丸め。現金の流出ではない)に書き換える変異を
    /// このテストのコードへ当てたところ(<c>wear</c> の計算を
    /// <c>IntegerMath.CeilDiv((long)Definition.ExternalBuyPrice(Item.Tools) * recipe.LaborPermille,
    /// Definition.ToolDurabilityPerUnit) * Calendar.DaysPerYear * cap</c> に変更)、
    /// 水車小屋番の収支が−960/年(−3.6%)になり <c>Assert.True(...)</c> が失敗した(赤を確認、
    /// 2026-09-19)。変異を戻して緑に復帰させた。
    /// </para>
    /// </remarks>
    [Fact]
    public void M0SatisfiesFloorPriceBalanceWithinTwoPercent_C()
    {
        var (world, household) = RepresentativeHousehold();

        long livingCost = 0;
        for (int itemId = 0; itemId < Definition.ItemCount; itemId++)
        {
            livingCost += AnnualConsumption(world, household, itemId) * Floor(itemId);
        }

        for (int occupationId = 0; occupationId < Definition.OccupationCount; occupationId++)
        {
            var occupation = (Occupation)occupationId;
            var recipe = Definition.Recipes[occupationId];
            int cap = Definition.ProductionCapacity(occupation);
            int outputItemId = recipe.Outputs[0].ItemId;
            int outputQuantity = recipe.Outputs[0].Quantity;

            long revenue = (long)Calendar.DaysPerYear * cap * outputQuantity
                * Definition.ExternalBuyPrice(outputItemId);

            long purchases = 0;
            foreach (var input in recipe.Inputs)
            {
                purchases += (long)cap * input.Quantity * AnnualPrice(input.ItemId);
            }

            // 摩耗は現金の流出で数える ── 年間の投入労働(‰人日)× 工具の床 ÷ 耐久値、
            // 切り上げは合計に対して1回だけ(GDD02d §4.4)。
            long wear = IntegerMath.CeilDiv(
                (long)Calendar.DaysPerYear * cap * recipe.LaborPermille
                    * Definition.ExternalBuyPrice(Item.Tools),
                Definition.ToolDurabilityPerUnit);

            long balance = revenue - purchases - wear - livingCost;

            Assert.True(
                Math.Abs(balance) * 100 <= 2 * livingCost,
                $"(c) 職業{occupation}: 収支{balance}が生活費{livingCost}の2%を超える"
                    + $"(売上{revenue}、仕入{purchases}、摩耗{wear})。");
        }
    }

    /// <summary>
    /// テスト表 #25。(d) 初期資金 ≥ 必需の取り置き + 運転資金 + 嗜好の1日分。
    /// </summary>
    /// <remarks>
    /// <b>取り置き・運転資金・嗜好はいずれも冬の消費で評価する(GDD02d §4.4)。</b>
    /// 春の消費で数えると取り置き884が604になり緩みが黙って入るが、このテストは
    /// (M0の値では)それでも初期資金2,400以内に収まるため落ちない ──
    /// 判別できないので、季節を冬に固定する意図をここに doc コメントで残す(タスク仕様)。
    /// </remarks>
    [Fact]
    public void M0SatisfiesInitialFundsCoverReservesAndWorkingCapital_D()
    {
        var (world, household) = RepresentativeHousehold();

        long reserve = 0;
        for (int itemId = 0; itemId < Definition.ItemCount; itemId++)
        {
            if (Definition.NecessityTargetStockDays[itemId] <= 0)
            {
                continue;
            }

            int winterDaily = DailyConsumptionQuantity(world, household, itemId, Season.Winter);
            reserve += (long)Definition.NecessityTargetStockDays[itemId] * winterDaily * Floor(itemId);
        }

        long preference = 0;
        for (int itemId = 0; itemId < Definition.ItemCount; itemId++)
        {
            if (Definition.PreferenceTargetStockDays[itemId] <= 0)
            {
                continue;
            }

            int winterDaily = DailyConsumptionQuantity(world, household, itemId, Season.Winter);
            preference += (long)Definition.PreferenceTargetStockDays[itemId] * winterDaily * Floor(itemId);
        }

        for (int occupationId = 0; occupationId < Definition.OccupationCount; occupationId++)
        {
            var occupation = (Occupation)occupationId;
            var recipe = Definition.Recipes[occupationId];
            int cap = Definition.ProductionCapacity(occupation);

            long workingCapital = 0;
            foreach (var input in recipe.Inputs)
            {
                workingCapital += (long)cap * input.Quantity * Definition.InputBufferDays
                    * SeasonMax(input.ItemId);
            }

            Assert.True(
                Definition.InitialLiquidFunds >= reserve + workingCapital + preference,
                $"(d) 職業{occupation}: 初期資金{Definition.InitialLiquidFunds} < "
                    + $"取り置き{reserve} + 運転資金{workingCapital} + 嗜好{preference}。");
        }
    }

    /// <summary>
    /// テスト表 #26。(e) レシピごとに季節最高値で評価した相場での原価 ≤ 許容原価合計。
    /// </summary>
    /// <remarks>
    /// 値の側の変異: 穀物の季節係数を{500,2100,700,700}(合計4000のまま)にすると夏の穀物が
    /// 21になり、水車小屋番(許容41 &lt; 2×21)で落ちる(タスク仕様)。
    /// </remarks>
    [Fact]
    public void M0SatisfiesWorstSeasonInputCostWithinAllowedCost_E()
    {
        for (int occupationId = 0; occupationId < Definition.OccupationCount; occupationId++)
        {
            var occupation = (Occupation)occupationId;
            var recipe = Definition.Recipes[occupationId];
            int outputItemId = recipe.Outputs[0].ItemId;
            int outputQuantity = recipe.Outputs[0].Quantity;

            long allowedCostTotal = IntegerMath.FloorDiv(
                (long)Definition.ExternalBuyPrice(outputItemId) * outputQuantity * 1000,
                1000 + Definition.MinimumMarginPermille);

            long wearCostPerRun = IntegerMath.CeilDiv(
                (long)Definition.ExternalBuyPrice(Item.Tools) * recipe.LaborPermille,
                Definition.ToolDurabilityPerUnit);

            allowedCostTotal -= wearCostPerRun;

            long worstCaseCost = 0;
            foreach (var input in recipe.Inputs)
            {
                worstCaseCost += (long)input.Quantity * SeasonMax(input.ItemId);
            }

            Assert.True(
                allowedCostTotal >= worstCaseCost,
                $"(e) 職業{occupation}: 許容原価合計{allowedCostTotal} < "
                    + $"季節最高値での相場での原価{worstCaseCost}。");
        }
    }

    /// <summary>
    /// 【核心】テスト表 #27。<c>WorldDefinition.M0</c> で世界を生成し1日進めると、各職業の
    /// <c>ProductionRuns</c> が 7 / 6 / 6 / 12 / 1、出力が 7 / 12 / 6 / 36 / 1 増える
    /// (初期在庫は3日ぶんあるので入力で制約されない)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-19)。</b><c>WorldGenerator.Generate</c> の工房在庫の初期化を
    /// 旧式(<c>InitialWorkshopInputDays × input.Quantity</c>、1実行/日の前提)に戻す変異を
    /// 当てたところ、初期在庫が穀物6個(3日×2個)しかなく水車小屋番の生産能力7に対し
    /// 1日目から入力切れ(<c>floor(6/2)=3</c>)を起こし、<c>Assert.Equal(7,
    /// millerHousehold.ProductionRuns)</c> が実際値3で失敗した(赤を確認)。変異を戻して
    /// 緑に復帰させた。
    /// </remarks>
    [Fact]
    public void M0ProductionMatchesCalibrationTable()
    {
        var world = WorldGenerator.Generate(Definition, new RandomSource(masterSeed: 1));

        // 出力品目の初期工房在庫を控える(鍛冶は Item.Tools が出力かつ初期工具在庫1個を
        // 持つので、増分ではなく絶対値で比べると値がずれる)。
        var outputBefore = new int[world.Households.Length];
        foreach (var household in world.Households)
        {
            var recipe = Definition.Recipes[(int)household.Occupation];
            outputBefore[household.Id] = household.WorkshopInventory[recipe.Outputs[0].ItemId];
        }

        var scheduler = new SimScheduler(
            new ISimSystem[] { new ProductionSystem(Definition) }, new RandomSource(1));
        scheduler.Advance(world, ticks: 24);

        int[] expectedRuns = { 7, 6, 6, 12, 1 };
        int[] expectedOutputIncrease = { 7, 12, 6, 36, 1 };

        foreach (var household in world.Households)
        {
            int occupationId = (int)household.Occupation;
            var recipe = Definition.Recipes[occupationId];
            int outputItemId = recipe.Outputs[0].ItemId;
            int actualIncrease = household.WorkshopInventory[outputItemId] - outputBefore[household.Id];

            Assert.Equal(expectedRuns[occupationId], household.ProductionRuns);
            Assert.Equal(expectedOutputIncrease[occupationId], actualIncrease);
        }
    }
}
