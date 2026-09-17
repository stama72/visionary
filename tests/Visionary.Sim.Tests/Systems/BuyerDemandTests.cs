using Visionary.Sim.Numerics;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="BuyerDemand"/>(GDD02 §8.2〜§8.2.7 / §6.2.1、#36 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class BuyerDemandTests
{
    /// <summary>
    /// パン屋を模したレシピ。小麦粉+薪 → パン。<c>Occupation.Miller</c>(添字0)として登録される
    /// (<see cref="EconomySystemTestFixtures.BuildDefinition"/> の仕様)。
    /// </summary>
    private static Recipe BakerLikeRecipe() =>
        new(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = 2 } },
            inputs: new[]
            {
                new ItemQuantity { ItemId = Item.Flour, Quantity = 1 },
                new ItemQuantity { ItemId = Item.Firewood, Quantity = 1 },
            },
            laborPermille: 1000);

    /// <summary>
    /// 生産の入力の目標在庫。Miller(添字0)の小麦粉・薪だけ指定値、他4職業
    /// (<see cref="EconomySystemTestFixtures.UnusedRecipe"/>)は入力itemId 1 に1を置く。
    /// </summary>
    private static int[][] InputTargets(int flourTarget, int firewoodTarget)
    {
        var millerRow = new int[Item.Count];
        millerRow[Item.Flour] = flourTarget;
        millerRow[Item.Firewood] = firewoodTarget;

        var rows = new int[5][];
        rows[0] = millerRow;

        for (int occupationId = 1; occupationId < 5; occupationId++)
        {
            var row = new int[Item.Count];
            row[1] = 1; // UnusedRecipeの入力itemId(EconomySystemTestFixtures参照)
            rows[occupationId] = row;
        }

        return rows;
    }

    private static int[] NecessityDays(int firewoodDays = 7, int breadDays = 3)
    {
        var row = new int[Item.Count];
        row[Item.Firewood] = firewoodDays;
        row[Item.Bread] = breadDays;

        return row;
    }

    private static int[] PreferenceDays(int beerDays = 1)
    {
        var row = new int[Item.Count];
        row[Item.Beer] = beerDays;

        return row;
    }

    private static int[][] ConsumptionTable(int firewoodQty, int breadQty, int beerQty = 0)
    {
        var row = new int[Item.Count];
        row[Item.Firewood] = firewoodQty;
        row[Item.Bread] = breadQty;
        row[Item.Beer] = beerQty;

        return new[] { (int[])row.Clone(), (int[])row.Clone(), (int[])row.Clone() };
    }

    private static WorldDefinition BuildDefinition(
        int flourInputTarget = 5,
        int firewoodInputTarget = 5,
        int firewoodConsumptionQty = 2,
        int breadConsumptionQty = 1,
        int beerConsumptionQty = 0,
        int toolTargetStockPermille = 500,
        int[]? rankCoefficientPermille = null,
        int necessityTolerancePermille = 1200,
        int[]? budgetRatioPermilleByPurpose = null,
        int[]? necessityTargetStockDays = null,
        int[]? preferenceTargetStockDays = null,
        int productionRunsPerToolWear = 30) =>
        EconomySystemTestFixtures.BuildDefinition(
            BakerLikeRecipe(),
            productionRunsPerToolWear: productionRunsPerToolWear,
            dailyConsumptionPerNpcByRank:
                ConsumptionTable(firewoodConsumptionQty, breadConsumptionQty, beerConsumptionQty),
            inputTargetStockByOccupation: InputTargets(flourInputTarget, firewoodInputTarget),
            necessityTargetStockDays: necessityTargetStockDays ?? NecessityDays(),
            preferenceTargetStockDays: preferenceTargetStockDays ?? PreferenceDays(),
            toolTargetStockPermille: toolTargetStockPermille,
            rankCoefficientPermille: rankCoefficientPermille ?? new[] { 1000, 600, 200 },
            necessityTolerancePermille: necessityTolerancePermille,
            budgetRatioPermilleByPurpose: budgetRatioPermilleByPurpose ?? new[] { 0, 50, 200, 10 });

    private static DemandLine FindLine(HouseholdDemand demand, DemandPurpose purpose, int itemId) =>
        demand.Lines.Single(line => line.Purpose == purpose && line.ItemId == itemId);

    /// <summary>
    /// 【核心】テスト表 #24。パン屋の世帯 → 行が必需(薪→パン)→ 生産の入力(小麦粉→薪)→
    /// 耐久(工具)→ 嗜好(ビール)の順に並ぶ。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>BuyerDemand.Build</c> の耐久の行の追加
    /// (<c>lines.Add(BuildLine(DemandPurpose.Durable, ...))</c>)を丸ごと削る変異
    /// (用途を1つ落とす)を当てたところ、<c>Assert.Equal(expected, actualPairs)</c> が
    /// 期待6件に対し実際5件(耐久が無い)で失敗した(赤を確認)。GDD02 §2.2「どれか1つでも
    /// 落とすと、その式がW2で一度も実行されない」に対応する。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void EveryPurposeProducesALineInTheSpendingScanOrder()
    {
        var definition = BuildDefinition();
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });

        var demand = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);

        var actualPairs = demand.Lines.Select(line => (line.Purpose, line.ItemId)).ToArray();
        var expectedPairs = new[]
        {
            (DemandPurpose.Necessity, Item.Firewood),
            (DemandPurpose.Necessity, Item.Bread),
            (DemandPurpose.ProductionInput, Item.Flour),
            (DemandPurpose.ProductionInput, Item.Firewood),
            (DemandPurpose.Durable, Item.Tools),
            (DemandPurpose.Preference, Item.Beer),
        };

        Assert.Equal(expectedPairs, actualPairs);
    }

    /// <summary>
    /// 【核心】テスト表 #25。薪の2行が (必需, 世帯在庫, 先読み7日) と (生産の入力, 工房在庫, 定数表)
    /// で別の値を持つ。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b>薪の必需の行と生産の入力の行を1本に畳む変異
    /// (生産の入力の行を作らず、必需の行の在庫だけを世帯在庫+工房在庫の合計にする)を当てたところ、
    /// <c>Assert.Equal(5, inputLine.TargetStock)</c>(定数表の値)に対応する行が存在しなくなり
    /// <c>InvalidOperationException</c>(Single が要素0件で失敗)で落ちた(赤を確認、
    /// GDD02 §8.2.1「生産用の薪が運転資金から丸ごと落ちる」に対応)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void FirewoodAppearsAsTwoLinesWithDifferentStocksAndTargets()
    {
        var definition = BuildDefinition(firewoodInputTarget: 5, firewoodConsumptionQty: 2);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].HouseholdInventory[Item.Firewood] = 3;
        world.Households[0].WorkshopInventory[Item.Firewood] = 8;

        var demand = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);

        var necessityLine = FindLine(demand, DemandPurpose.Necessity, Item.Firewood);
        var inputLine = FindLine(demand, DemandPurpose.ProductionInput, Item.Firewood);

        // 必需: 先読み7日 × 基礎量2(季節係数1000‰の据え置き) = 14。
        Assert.Equal(14, necessityLine.TargetStock);
        Assert.Equal(3, necessityLine.ExpectedStock);

        // 生産の入力: 定数表(InputTargets)の値そのもの。
        Assert.Equal(5, inputLine.TargetStock);
        Assert.Equal(8, inputLine.ExpectedStock);
    }

    /// <summary>
    /// 【核心】テスト表 #26。嗜好・耐久の目標在庫と相場基準をいくら大きくしても
    /// <c>WorkingCapital</c> が変わらない。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>BuyerDemand.Build</c> の耐久の行の直前に
    /// <c>workingCapital += (long)durableTarget * reference[Item.Tools];</c>
    /// (全用途を足す変異)を挿入したところ、耐久の目標在庫・相場基準を大きくした側の
    /// <c>Assert.Equal(smallWorkingCapital, largeWorkingCapital)</c> が失敗した(赤を確認、
    /// GDD02 §8.2.1「余剰資金が過小に出て嗜好が恒久に0になる」に対応)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void WorkingCapitalScansOnlyProductionInputAndNecessity()
    {
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].LiquidFunds = 1000;
        SetReference(world, world.Households[0].HeadNpcId, Item.Firewood, price: 10);
        SetReference(world, world.Households[0].HeadNpcId, Item.Flour, price: 7);
        SetReference(world, world.Households[0].HeadNpcId, Item.Tools, price: 99999);

        var smallDefinition = BuildDefinition(
            toolTargetStockPermille: 500,
            preferenceTargetStockDays: PreferenceDays(beerDays: 1));
        var largeDefinition = BuildDefinition(
            toolTargetStockPermille: 100_000,
            preferenceTargetStockDays: PreferenceDays(beerDays: 1000));

        long smallWorkingCapital = new BuyerDemand(smallDefinition)
            .Build(world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0)
            .WorkingCapital;
        long largeWorkingCapital = new BuyerDemand(largeDefinition)
            .Build(world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0)
            .WorkingCapital;

        Assert.Equal(smallWorkingCapital, largeWorkingCapital);
    }

    /// <summary>
    /// 【核心】テスト表 #27。在庫を目標まで満たしても <c>WorkingCapital</c> が変わらない。
    /// 観測の無い品目(薪)は加算されない。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16、レビュー1巡目指摘6で記録を訂正)。</b>必需の運転資金の加算
    /// (必需のループのみ、生産の入力側は変えない)を
    /// <c>workingCapital += (long)(target - expected) * reference[itemId];</c>
    /// (現在庫を差し引く変異)に変えたところ、<b>1行目の <c>Assert.Equal(95, zeroStockWorkingCapital)</c>
    /// は通る</b>(この時点では在庫がまだ0で <c>target - 0 == target</c> のため変異の影響を受けない)。
    /// <b>実際に落ちるのは次行の <c>Assert.Equal(zeroStockWorkingCapital, fullStockWorkingCapital)</c>
    /// である</b> ── パンの在庫を目標まで満たした2回目の呼び出しで <c>target - expected == 0</c> と
    /// なりパンの寄与60が消え、実際値35(小麦粉の寄与のみ)で失敗した(赤を確認、GDD02 §8.2.1
    /// 「周期的な黒字倒産」に対応)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void WorkingCapitalIgnoresCurrentStockAndUnobservedItems()
    {
        var definition = BuildDefinition();
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].LiquidFunds = 1000;

        // パン(必需)・小麦粉(生産の入力)には観測を置く。薪は必需・生産の入力の両方に
        // 目標在庫を持つが、観測を一切置かない(「観測の無い品目は加算されない」を見る)。
        SetReference(world, world.Households[0].HeadNpcId, Item.Bread, price: 20);
        SetReference(world, world.Households[0].HeadNpcId, Item.Flour, price: 7);

        long zeroStockWorkingCapital = new BuyerDemand(definition)
            .Build(world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0)
            .WorkingCapital;

        // パン(必需:目標3)・小麦粉(生産の入力:目標5)を目標まで満たす。
        world.Households[0].HouseholdInventory[Item.Bread] = 3;
        world.Households[0].WorkshopInventory[Item.Flour] = 5;

        long fullStockWorkingCapital = new BuyerDemand(definition)
            .Build(world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0)
            .WorkingCapital;

        // 期待値 = パンの目標3×観測20 + 小麦粉の目標5×観測7 = 60+35 = 95。
        // 薪(必需目標14・生産の入力目標5、いずれも観測なし)は加算されない。
        Assert.Equal(95, zeroStockWorkingCapital);
        Assert.Equal(zeroStockWorkingCapital, fullStockWorkingCapital);
    }

    /// <summary>
    /// <see cref="BuyerReferenceComesFromTheHeadNpcAndIgnoresOwnOfferPrice"/> 専用の世帯1戸の世界。
    /// 世帯主のNpcId(2)を世帯Id(0)とわざと違える(TradeSystemTestsと同じ理由)。
    /// </summary>
    private static World BuildWorldWithHeadNpcIdDifferentFromHouseholdId()
    {
        const int HeadNpcId = 2;
        const int ApprenticeNpcId = 3;

        var world = new World(npcCount: 4, householdCount: 1, itemCount: Item.Count);
        world.Npcs[HeadNpcId].Rank = NpcRank.Master;
        world.Npcs[ApprenticeNpcId].Rank = NpcRank.Apprentice;

        world.Households[0] = new HouseholdState(
            id: 0, districtId: 0, headNpcId: HeadNpcId,
            memberNpcIds: new[] { HeadNpcId, ApprenticeNpcId }, itemCount: Item.Count);
        world.Households[0].Occupation = Occupation.Miller;

        return world;
    }

    /// <summary>
    /// 【核心】テスト表 #28。徒弟にだけ観測を置くと必需がフォールバックへ落ちる。<c>Market</c> に
    /// 自世帯の当日価格があっても必需の基礎値が変わらない。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b><c>BuyerDemand.Build</c> の
    /// <c>world.Knowledge[household.HeadNpcId]</c> を <c>world.Knowledge[household.Id]</c>
    /// (世帯Idで誤って引く)に変える変異を当てたところ、世帯主(NpcId=2)に観測を置いたケースの
    /// <c>Assert.Equal(120, ...)</c> が実際値10(フォールバックのまま。誤って
    /// <c>Knowledge[0]</c>=空を読んだ)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void BuyerReferenceComesFromTheHeadNpcAndIgnoresOwnOfferPrice()
    {
        const int HeadNpcId = 2;
        const int ApprenticeNpcId = 3;
        const int OtherSellerId = 999;

        var definition = BuildDefinition();
        var world = BuildWorldWithHeadNpcIdDifferentFromHouseholdId();
        world.Households[0].LiquidFunds = 200; // フォールバック = ApplyPermille(200,50) = 10

        // now を進めて、過去日の観測を作れるようにする。
        EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 10 * 24);

        var observation = new PriceObservation
        {
            ItemId = Item.Firewood,
            LocationId = 0,
            Price = 100,
            SellerId = OtherSellerId,
            ObservedAt = Tick.FromDays(9),
            Source = ObservationSource.Direct,
        };

        // 徒弟のKnowledgeにだけ観測 → 相場基準が立たず、フォールバックのまま。
        world.Knowledge[ApprenticeNpcId].Add(observation);

        var withApprenticeOnly = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        int fallback = IntegerMath.ApplyPermille(200, 50);
        Assert.Equal(fallback, FindLine(withApprenticeOnly, DemandPurpose.Necessity, Item.Firewood).BaseValue);

        // 同じ観測を世帯主のKnowledgeに置くと相場基準が立つ(ApplyPermille(100,1200)=120)。
        world.Knowledge[HeadNpcId].Add(observation);

        var withHead = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        Assert.Equal(120, FindLine(withHead, DemandPurpose.Necessity, Item.Firewood).BaseValue);

        // Marketに自世帯の当日価格を置いても基礎値は変わらない(BuyerDemandはMarketを読まない)。
        world.Market[new MarketKey(Item.Firewood, world.Households[0].Id)] = 9999;

        var withOwnMarketEntry = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        Assert.Equal(120, FindLine(withOwnMarketEntry, DemandPurpose.Necessity, Item.Firewood).BaseValue);
    }

    /// <summary>
    /// 【核心】テスト表 #29。工具1個・N=30・摩耗5・500‰・階層1000‰ → 予想在庫25、目標在庫15。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-16)。</b>耐久の予想在庫の計算を
    /// <c>household.WorkshopInventory[Item.Tools]</c>(個数のまま、耐久値へ変換しない変異)に
    /// 変えたところ、<c>Assert.Equal(25, durableLine.ExpectedStock)</c> が実際値1
    /// (在庫1・目標15で圧力1000‰のまま摩耗が進むまで工具を買い続ける経路)で失敗した
    /// (赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void DurableLineIsMeasuredInDurabilityNotUnits()
    {
        var definition = BuildDefinition(
            toolTargetStockPermille: 500,
            rankCoefficientPermille: new[] { 1000, 600, 200 },
            productionRunsPerToolWear: 30);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].WorkshopInventory[Item.Tools] = 1;
        world.Households[0].ToolWearCount = 5;

        var demand = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        var durableLine = FindLine(demand, DemandPurpose.Durable, Item.Tools);

        Assert.Equal(25, durableLine.ExpectedStock);
        Assert.Equal(15, durableLine.TargetStock);
    }

    /// <summary>テスト表 #30。世帯主の階層を Apprentice(200‰)にすると目標在庫が3に縮む。</summary>
    [Fact]
    public void DurableTargetUsesTheHeadRankCoefficient()
    {
        var definition = BuildDefinition(
            toolTargetStockPermille: 500,
            rankCoefficientPermille: new[] { 1000, 600, 200 },
            productionRunsPerToolWear: 30);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Apprentice });

        var demand = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        var durableLine = FindLine(demand, DemandPurpose.Durable, Item.Tools);

        Assert.Equal(3, durableLine.TargetStock);
    }

    /// <summary>
    /// 【核心】別表 R1-1。自世帯 Id を <c>SellerId</c> とする観測と、世帯主 NpcId を
    /// <c>SellerId</c> とする観測を両方置く → 前者だけが相場基準から外れ、後者は含まれる。
    /// </summary>
    /// <remarks>
    /// <b>レビュー1巡目指摘(#28の縮退)。</b><see cref="WorldGenerator"/> は
    /// <c>headNpcId = householdId × 2</c> なので、世帯主の NpcId(2)は世帯 Id(0)とは無関係な
    /// <b>実在する別世帯(Id=1)の Id</b> である。<c>#28</c>
    /// (<see cref="BuyerReferenceComesFromTheHeadNpcAndIgnoresOwnOfferPrice"/>)は観測の
    /// <c>SellerId</c> を999固定にしたため、<c>selfHouseholdId</c> に <c>HeadNpcId</c> を渡す
    /// 取り違えがあっても <c>999 != 0</c> かつ <c>999 != 2</c> で偶然除外が効いてしまい判別できない。
    /// <para>
    /// <b>変異の実測(2026-09-17)。</b><c>BuyerDemand.Build</c> の
    /// <c>OfferPrice.TryMarketReference</c> 呼び出しの第3引数(<c>selfHouseholdId</c>)を
    /// <c>household.Id</c> から <c>household.HeadNpcId</c> に変える変異を当てたところ、
    /// <c>Assert.Equal(120, line.BaseValue)</c> が実際値1199(<c>SellerId=2</c> の観測が
    /// 誤って自己除外され、代わりに<c>SellerId=0</c>(本来除外すべき自世帯)の観測999が
    /// 生き残って <c>ApplyPermille(999,1200)=1199</c> になった)で失敗した(赤を確認)。
    /// 変異を戻して緑に復帰させた。
    /// </para>
    /// </remarks>
    [Fact]
    public void BuyerReferenceExcludesOwnHouseholdNotTheHeadNpcId()
    {
        const int HeadNpcId = 2;

        var definition = BuildDefinition();
        var world = BuildWorldWithHeadNpcIdDifferentFromHouseholdId();
        world.Households[0].LiquidFunds = 200;

        EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 10 * 24);

        // 自世帯(Id=0)のSellerIdを持つ観測 → 除外されるべき。
        world.Knowledge[HeadNpcId].Add(new PriceObservation
        {
            ItemId = Item.Firewood,
            LocationId = 0,
            Price = 999,
            SellerId = 0,
            ObservedAt = world.Now.AddDays(-1),
            Source = ObservationSource.Direct,
        });

        // 世帯主のNpcId(2)をSellerIdとする観測。WorldGeneratorの採番規則
        // (headNpcId = householdId × 2)ではNpcId=2は household Id=1 の世帯主であり、
        // この世帯(Id=0)自身ではない → 含まれるべき。
        world.Knowledge[HeadNpcId].Add(new PriceObservation
        {
            ItemId = Item.Firewood,
            LocationId = 0,
            Price = 100,
            SellerId = HeadNpcId,
            ObservedAt = world.Now.AddDays(-1),
            Source = ObservationSource.Direct,
        });

        var demand = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        var line = FindLine(demand, DemandPurpose.Necessity, Item.Firewood);

        // 相場基準 = ApplyPermille(100, 1200) = 120。SellerId=0の観測(999)は含まれない。
        Assert.Equal(120, line.BaseValue);
    }

    /// <summary>
    /// 【核心】別表 R1-2。<c>BuyerDemand.Build</c> が返す嗜好の行の <c>BaseValue</c>。
    /// 運転資金で余剰資金が0に潰れた世帯 → 0。余剰のある世帯 → <c>ApplyPermille(余剰資金, 比率‰)</c>。
    /// </summary>
    /// <remarks>
    /// <b>レビュー1巡目指摘。</b><c>BuyerBudgetTests</c> の #12 は
    /// <c>BuyerBudget.PreferenceBaseValue(int surplusFunds, int ratioPermille)</c> を直接呼ぶため、
    /// 母数を引数で1つしか受け取らないこの関数の中には「母数に流動資金を使う」誤りを書けない。
    /// その取り違えが実際に住むのは呼び出し側(ここ)である。
    /// <para>
    /// <b>変異の実測(2026-09-17)。</b><c>BuyerDemand.Build</c> の嗜好の基礎値の計算
    /// (<c>BuyerBudget.PreferenceBaseValue(surplus, ...)</c>)を
    /// <c>BuyerBudget.PreferenceBaseValue(household.LiquidFunds, ...)</c>(母数に流動資金を使う
    /// 変異)に変えたところ、余剰資金0のケースの <c>Assert.Equal(0, ...)</c> が実際値19
    /// (<c>ApplyPermille(95,200)</c>。GDD02 §8.2.1が名指しした黒字倒産)で失敗した(赤を確認)。
    /// 変異を戻して緑に復帰させた。
    /// </para>
    /// </remarks>
    [Fact]
    public void PreferenceLineUsesSurplusFundsNotLiquidFunds()
    {
        var definition = BuildDefinition();
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });

        // パン(必需)・小麦粉(生産の入力)に観測を置く。workingCapital = 3×20 + 5×7 = 95
        // (#27と同じ計算。薪は観測なしなので寄与しない)。
        SetReference(world, world.Households[0].HeadNpcId, Item.Bread, price: 20);
        SetReference(world, world.Households[0].HeadNpcId, Item.Flour, price: 7);

        // Case A: 流動資金95ちょうど → 余剰資金0。
        world.Households[0].LiquidFunds = 95;
        var demandZeroSurplus = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        Assert.Equal(0, demandZeroSurplus.SurplusFunds);
        Assert.Equal(0, FindLine(demandZeroSurplus, DemandPurpose.Preference, Item.Beer).BaseValue);

        // Case B: 流動資金145 → 余剰資金50 → ApplyPermille(50,200)=10。
        world.Households[0].LiquidFunds = 145;
        var demandWithSurplus = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        Assert.Equal(50, demandWithSurplus.SurplusFunds);
        Assert.Equal(10, FindLine(demandWithSurplus, DemandPurpose.Preference, Item.Beer).BaseValue);
    }

    /// <summary>
    /// 【核心】別表 R1-3。目標14・予想3 の必需の行で <c>StockPressurePermille</c> = 1000、
    /// <c>Budget</c> = <c>ApplyPermille(BaseValue, 1000)</c>。
    /// </summary>
    /// <remarks>
    /// <b>レビュー1巡目指摘。</b><c>DemandLine</c> の <c>StockPressurePermille</c> / <c>Budget</c>
    /// の2欄は、上の表(#1〜#30)のどの行も assert していなかった。
    /// <para>
    /// <b>変異の実測(2026-09-17)。</b><c>BuyerDemand.BuildLine</c> の
    /// <c>BuyerBudget.StockPressurePermille(expectedStock, targetStock)</c> の呼び出しを
    /// 引数逆順(<c>StockPressurePermille(targetStock, expectedStock)</c>)に変える変異を
    /// 当てたところ、<c>Assert.Equal(1000, line.StockPressurePermille)</c> が実際値0
    /// (14を「予想在庫」、3を「目標在庫」として読み、予想14が目標3の2倍(6)を超えるため0‰に
    /// 落ちた)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryLineCarriesItsOwnStockPressureAndBudget()
    {
        var definition = BuildDefinition();
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].LiquidFunds = 200; // フォールバック基礎値を0にしないため(観測なし)
        world.Households[0].HouseholdInventory[Item.Firewood] = 3;

        var demand = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        var line = FindLine(demand, DemandPurpose.Necessity, Item.Firewood);

        Assert.Equal(14, line.TargetStock);
        Assert.Equal(3, line.ExpectedStock);
        Assert.Equal(1000, line.StockPressurePermille);
        Assert.NotEqual(0, line.BaseValue); // Budget=0 が「欄の埋め忘れ」による偶然の一致でないことを見る
        Assert.Equal(line.BaseValue, line.Budget); // ApplyPermille(BaseValue,1000)==BaseValue
    }

    /// <summary>
    /// 別表 R1-4。耐久と嗜好で違う比率‰を与えた定義で、両方の行の <c>BaseValue</c> が
    /// それぞれの比率で出る。
    /// </summary>
    [Fact]
    public void DurableAndPreferenceReadTheirOwnBudgetRatio()
    {
        // Necessity=50(据え置き)、Preference=777、Durable=333。M0の10‰/200‰とは別の値にして
        // 添字の入れ替えが起きても偶然一致しないようにする。
        var definition = BuildDefinition(budgetRatioPermilleByPurpose: new[] { 0, 50, 777, 333 });
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].LiquidFunds = 1000;

        var demand = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);

        var durableLine = FindLine(demand, DemandPurpose.Durable, Item.Tools);
        var preferenceLine = FindLine(demand, DemandPurpose.Preference, Item.Beer);

        // 耐久: 観測なしなので ApplyPermille(流動資金, 333‰)。
        Assert.Equal(BuyerBudget.DurableBaseValue(false, 0, 1000, 333), durableLine.BaseValue);
        // 嗜好: ApplyPermille(余剰資金, 777‰)。
        Assert.Equal(BuyerBudget.PreferenceBaseValue(demand.SurplusFunds, 777), preferenceLine.BaseValue);
        Assert.NotEqual(durableLine.BaseValue, preferenceLine.BaseValue);
    }

    /// <summary>
    /// 【核心】別表 R1-5。観測ゼロかつ <c>hasPreviousOutputOfferPrice = false</c> の世帯 →
    /// 生産の入力の <c>BaseValue</c> = <c>ApplyPermille(流動資金, 必需の比率‰)</c> で0ではない。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-17)。</b><c>BuyerDemand.Build</c> が
    /// <c>BuyerBudget.DerivedDemand</c> の <c>necessityFallbackRatioPermille</c> 引数へ渡す値を
    /// <c>definition.BudgetRatioPermilleByPurpose[(int)DemandPurpose.Necessity]</c> から
    /// <c>definition.BudgetRatioPermilleByPurpose[(int)DemandPurpose.ProductionInput]</c>
    /// (コンストラクタが0を強制している欄)に変える変異を当てたところ、
    /// <c>Assert.Equal(expectedFallback, flourLine.BaseValue)</c> が実際値0
    /// (<c>ApplyPermille(1000,0)=0</c>。M0 の既定が「原材料を一切買わない」になる経路)で
    /// 失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void ProductionInputFallbackUsesTheNecessityRatio()
    {
        var definition = BuildDefinition(budgetRatioPermilleByPurpose: new[] { 0, 77, 200, 10 });
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].LiquidFunds = 1000;

        var demand = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        var flourLine = FindLine(demand, DemandPurpose.ProductionInput, Item.Flour);

        int expectedFallback = IntegerMath.ApplyPermille(1000, 77);
        Assert.NotEqual(0, expectedFallback);
        Assert.Equal(expectedFallback, flourLine.BaseValue);
    }

    /// <summary>
    /// 【核心】別表 R1-6。#26 の嗜好側を実際に効かせる: 嗜好の品目に正の1日消費量と観測を
    /// 与えたうえで目標在庫を膨らませても <c>WorkingCapital</c> が変わらない。
    /// </summary>
    /// <remarks>
    /// <b>レビュー1巡目指摘。</b>既存の #26 はビールの1日消費量が0で観測も無いため、
    /// <c>Lookahead</c> も相場基準も0になり、「全用途を足す」変異が <c>0 × 0</c> を足して
    /// 通過してしまう(判別力が耐久側にしか無い)。ここではビールに正の消費量を与え、
    /// (相場基準を使わない設計であることを崩さないよう)観測も併せて置く。
    /// <para>
    /// <b>変異の実測(2026-09-17)。</b>嗜好の行を組み立てる直前に
    /// <c>if (hasReference[itemId]) { workingCapital += (long)target * reference[itemId]; }</c>
    /// を追加し、あわせて <c>isReferenceRelevant</c> の判定に
    /// <c>definition.PreferenceTargetStockDays[itemId] &gt; 0</c> を足す変異(嗜好の相場基準も
    /// 運転資金へ計上する、GDD02 §8.2.1「全用途を足す」)を当てたところ、
    /// <c>Assert.Equal(smallWorkingCapital, largeWorkingCapital)</c> が「Expected: 155,
    /// Actual: 60095」で失敗した(赤を確認。155 = 薪抜き既存分95 + ビール目標2×観測30、
    /// 60095 = 95 + ビール目標2000×観測30。目標在庫を1000日ぶんに膨らませた側にだけ
    /// 嗜好の寄与が加わった)。変異を戻して緑に復帰させた。
    /// </para>
    /// </remarks>
    [Fact]
    public void WorkingCapitalIgnoresPreferenceEvenWhenConsumedAndObserved()
    {
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });
        world.Households[0].LiquidFunds = 1000;

        SetReference(world, world.Households[0].HeadNpcId, Item.Bread, price: 20);
        SetReference(world, world.Households[0].HeadNpcId, Item.Flour, price: 7);
        // 嗜好(ビール)にも正の消費量と観測を与える(レビュー1巡目指摘)。
        SetReference(world, world.Households[0].HeadNpcId, Item.Beer, price: 30);

        var smallDefinition = BuildDefinition(
            beerConsumptionQty: 2, preferenceTargetStockDays: PreferenceDays(beerDays: 1));
        var largeDefinition = BuildDefinition(
            beerConsumptionQty: 2, preferenceTargetStockDays: PreferenceDays(beerDays: 1000));

        long smallWorkingCapital = new BuyerDemand(smallDefinition)
            .Build(world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0)
            .WorkingCapital;
        long largeWorkingCapital = new BuyerDemand(largeDefinition)
            .Build(world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0)
            .WorkingCapital;

        Assert.Equal(smallWorkingCapital, largeWorkingCapital);
    }

    /// <summary>
    /// 別表 R1-7。世帯主 Master(1000‰)+ 徒弟 Apprentice(200‰)の混成世帯で、目標在庫が
    /// 世帯主の係数で出る。
    /// </summary>
    /// <remarks>
    /// <b>レビュー1巡目指摘。</b>#29・#30 はどちらも構成員1名の世帯なので、世帯主・最小・最大の
    /// 係数が同じ値になり「構成員の最小/最大を採る」誤りが落ちない。M0 の世帯は親方+徒弟の
    /// 混成であり、この誤りは M0 で実際に踏まれる。
    /// </remarks>
    [Fact]
    public void DurableTargetIgnoresNonHeadMemberRanks()
    {
        var definition = BuildDefinition(
            toolTargetStockPermille: 500,
            rankCoefficientPermille: new[] { 1000, 600, 200 },
            productionRunsPerToolWear: 30);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(
            new[] { NpcRank.Master, NpcRank.Apprentice }); // 先頭(世帯主)がMaster

        var demand = new BuyerDemand(definition).Build(
            world, world.Households[0], hasPreviousOutputOfferPrice: false, previousOutputOfferPrice: 0);
        var durableLine = FindLine(demand, DemandPurpose.Durable, Item.Tools);

        // 世帯主(Master,1000‰)の係数で15。徒弟(200‰)の最小を採ると3になる。
        Assert.Equal(15, durableLine.TargetStock);
    }

    /// <summary>世帯主の <c>Knowledge</c> に、過去日(他の売り手999)の観測を1件仕込む。</summary>
    private static void SetReference(World world, int headNpcId, int itemId, int price)
    {
        if (world.Now.DayIndex < 1)
        {
            EconomySystemTestFixtures.AdvanceClockOnly(world, ticks: 10 * 24);
        }

        const int OtherSellerId = 999;

        world.Knowledge[headNpcId].Add(new PriceObservation
        {
            ItemId = itemId,
            LocationId = 0,
            Price = price,
            SellerId = OtherSellerId,
            ObservedAt = world.Now.AddDays(-1),
            Source = ObservationSource.Direct,
        });
    }
}
