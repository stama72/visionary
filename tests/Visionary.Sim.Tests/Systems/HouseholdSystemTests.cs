using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="HouseholdSystem"/> / <see cref="OccupationReassignment"/>(GDD02b §3.3・§4.1・§4.2、
/// W2-18 タスク仕様のテスト表)の検査。
/// </summary>
/// <remarks>
/// <b>世界は手組みでよい。</b>順3は価格も観測も読まないので <see cref="WorldGenerator"/> を通す
/// 必要が無い。定義は <see cref="WorldDefinition.M0"/> をそのまま使う ── 本テストは
/// <c>household.ProductionRuns</c> / <c>household.WorkshopInventory</c> をすべて手で置くので、
/// M0 の労働力・価格の調整値(#28)には依存しない。依存するのは職業ごとの入出力品目(GDD02 §2.4)
/// という構造だけである。
/// </remarks>
public sealed class HouseholdSystemTests
{
    /// <summary>世帯を1戸、指定の区画・職業で作る(単独NPC世帯。<c>TradeSystemTests.AddHousehold</c>相当)。</summary>
    private static void AddHousehold(World world, int id, int districtId, Occupation occupation)
    {
        world.Npcs[id].Rank = NpcRank.Master;
        world.Households[id] = new HouseholdState(
            id: id, districtId: districtId, headNpcId: id, memberNpcIds: new[] { id }, itemCount: Item.Count);
        world.Households[id].Occupation = occupation;
    }

    /// <summary>職業 <paramref name="occupation"/> の出力品目Id(M0のレシピはすべて出力1件)。</summary>
    private static int OutputItemId(Occupation occupation) =>
        WorldDefinition.M0.Recipes[(int)occupation].Outputs[0].ItemId;

    /// <summary>
    /// テスト表 #1。前日の資金不足(件数&gt;0)を1日回すとフラグが立つ。
    /// </summary>
    /// <remarks>
    /// <b>被験者を世帯 Id 1 に置く(2巡目 #18)。</b>手順1 の走査を先頭1戸(<c>world.Households[0]</c>)
    /// だけに絞る変異は、被験者が Id 0 のときは先頭1戸=被験者なので緑のまま通っていた。世帯 Id 0 に
    /// 無関係な健全世帯(decoy)を置き、被験者を Id 1 に動かすことでこの変異を赤にする。
    /// </remarks>
    [Fact]
    public void BankruptFlagRisesFromYesterdaysShortfall()
    {
        var definition = WorldDefinition.M0;
        var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);
        AddHousehold(world, id: 0, districtId: 9, Occupation.Baker); // decoy(#18)。健全なまま無関係。
        AddHousehold(world, id: 1, districtId: 0, Occupation.Miller); // 被験者。
        world.Households[1].UnaffordableNecessityCount = 1;

        EconomySystemTestFixtures.RunDays(world, new HouseholdSystem(definition), days: 1);

        Assert.Equal(1, world.Households[1].IsBankrupt);
    }

    /// <summary>
    /// テスト表 #2。前日に資金不足が無かった日は、既に立っているフラグが降りる。
    /// </summary>
    /// <remarks>
    /// <c>if (count &gt; 0) IsBankrupt = 1;</c> と書いて降りる枝を落とす実装ミスでは、本テストが
    /// 赤になる(一度立ったフラグが永久に残り、②が恒久化する)。
    /// <para>
    /// <b>被験者を世帯 Id 1 に置く(2巡目 #18)。</b>#1 と同じ理由 ── 手順1 の走査を先頭1戸へ絞る
    /// 変異が、被験者 Id 0 のときは緑のまま通っていた。
    /// </para>
    /// </remarks>
    [Fact]
    public void BankruptFlagFallsWhenYesterdayHadNoShortfall()
    {
        var definition = WorldDefinition.M0;
        var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);
        AddHousehold(world, id: 0, districtId: 9, Occupation.Baker); // decoy(#18)。健全なまま無関係。
        AddHousehold(world, id: 1, districtId: 0, Occupation.Miller); // 被験者。
        world.Households[1].IsBankrupt = 1;
        world.Households[1].UnaffordableNecessityCount = 0;

        EconomySystemTestFixtures.RunDays(world, new HouseholdSystem(definition), days: 1);

        Assert.Equal(0, world.Households[1].IsBankrupt);
    }

    /// <summary>
    /// テスト表 #3。順3 は金を動かさず(<see cref="HouseholdState.LiquidFunds"/> 不変)、
    /// <see cref="HouseholdState.UnaffordableNecessityCount"/> も読むだけで書かない(0に戻すのは
    /// 順5 段5bの役目)。
    /// </summary>
    [Fact]
    public void HouseholdSystemMovesNoMoneyAndDoesNotResetTheCount()
    {
        var definition = WorldDefinition.M0;
        var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);
        AddHousehold(world, id: 0, districtId: 0, Occupation.Miller);
        world.Households[0].LiquidFunds = 777;
        world.Households[0].UnaffordableNecessityCount = 3;

        EconomySystemTestFixtures.RunDays(world, new HouseholdSystem(definition), days: 1);

        Assert.Equal(777, world.Households[0].LiquidFunds);
        Assert.Equal(3, world.Households[0].UnaffordableNecessityCount);
    }

    /// <summary>
    /// 【核心 C-1】テスト表 #4。4通り(3条件すべて／a欠け／b欠け／c欠け)で、揃った1通りだけ
    /// <see cref="HouseholdState.Occupation"/> が変わる(GDD02b §4.1)。
    /// </summary>
    /// <remarks>
    /// <b>b欠けケースの被験者を Miller 以外にする(2巡目 #17)。</b><c>IsGateOpen</c> がレシピを
    /// <c>definition.Recipes[(int)household.Occupation]</c> の代わりに <c>Recipes[0]</c> で引く変異は、
    /// Miller の添字が 0 なので、被験者が Miller のときはどの行でも結果が変わらず緑のまま通っていた。
    /// b欠け行だけ被験者を Woodworker(添字3)にする ── 自職業(薪)の販売在庫を正にしても、
    /// 変異は <c>Recipes[0]</c>(Miller・小麦粉)の在庫(既定 0)を読むので b が誤って開き、
    /// 「維持される」はずの本行が付け替わってしまう。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(<c>mutator</c>、2026-09-23、<c>8bdd6ee</c>)。</b>
    /// <see cref="OccupationReassignment.IsGateOpen"/> のゲート条件c(<c>household.ProductionRuns == 0</c>
    /// の早期return)を丸ごと落とす変異。期待は赤、実測は赤(1件) ── 落ちたのは本テストの
    /// <c>OccupationChangesOnlyWhenAllThreeConditionsHold(isBankrupt: True, sellableStockIsZero: True,
    /// productionRunsIsZero: False, expectReassignment: False, occupation: Miller)</c>(c欠けのケース)
    /// のみ。受け入れ条件を満たした。
    /// </remarks>
    [Theory]
    [InlineData(true, true, true, true, Occupation.Miller)]       // 3条件すべて → 付け替わる
    [InlineData(false, true, true, false, Occupation.Miller)]     // a欠け(健全) → 維持
    [InlineData(true, false, true, false, Occupation.Woodworker)] // b欠け(販売在庫が正) → 維持。#17。
    [InlineData(true, true, false, false, Occupation.Miller)]     // c欠け(当日生産あり) → 維持
    public void OccupationChangesOnlyWhenAllThreeConditionsHold(
        bool isBankrupt,
        bool sellableStockIsZero,
        bool productionRunsIsZero,
        bool expectReassignment,
        Occupation occupation)
    {
        var definition = WorldDefinition.M0;
        var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);

        // 世帯Id0が被験者。世帯Id1は同職業の相方(担い手が0で1にならないようにするだけ)。
        AddHousehold(world, id: 0, districtId: 0, occupation);
        AddHousehold(world, id: 1, districtId: 1, occupation);

        var subject = world.Households[0];
        subject.UnaffordableNecessityCount = isBankrupt ? 1 : 0;
        subject.WorkshopInventory[OutputItemId(occupation)] = sellableStockIsZero ? 0 : 5;
        subject.ProductionRuns = productionRunsIsZero ? 0 : 1;

        // 前提(W2-24): 新しいc(生産量0 かつ 入力から作れる回数0)は、AddHouseholdが入力在庫を
        // 0で作ることに依存する。ここが崩れると「3条件すべて」「a欠け」「c欠け」の各行の
        // ゲートcの開閉が変わってしまう。
        int inputItemId = definition.Recipes[(int)occupation].Inputs[0].ItemId;
        Assert.Equal(0, subject.WorkshopInventory[inputItemId]);

        EconomySystemTestFixtures.RunDays(world, new HouseholdSystem(definition), days: 1);

        if (expectReassignment)
        {
            Assert.NotEqual(occupation, world.Households[0].Occupation);
        }
        else
        {
            Assert.Equal(occupation, world.Households[0].Occupation);
        }
    }

    /// <summary>
    /// テスト表 #5。ゲートの条件bは <see cref="SellableStock.Of"/>(販売在庫)を読み、
    /// 生の <see cref="HouseholdState.WorkshopInventory"/> の直読みや入力品目の在庫では判定しない
    /// (GDD02b §4.1 ※2)。
    /// </summary>
    [Fact]
    public void GateReadsSellableStockNotWorkshopInventory()
    {
        var definition = WorldDefinition.M0;

        // (i) 出力品目(工具)の販売在庫0だが入力品目(鉄鉱石)の工房在庫が正の鍛冶 → 付け替わる。
        {
            var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);
            AddHousehold(world, id: 0, districtId: 0, Occupation.Smith);
            AddHousehold(world, id: 1, districtId: 1, Occupation.Smith);

            var subject = world.Households[0];
            subject.UnaffordableNecessityCount = 1;
            subject.WorkshopInventory[Item.Tools] = 0;
            subject.WorkshopInventory[Item.IronOre] = 100; // 入力。ゲートには関係しない。
            subject.ProductionRuns = 0;

            EconomySystemTestFixtures.RunDays(world, new HouseholdSystem(definition), days: 1);

            Assert.NotEqual(Occupation.Smith, world.Households[0].Occupation);
        }

        // (ii) 工具を1個だけ持つ鍛冶(留保1で販売在庫0)→ 付け替わる。
        {
            var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);
            AddHousehold(world, id: 0, districtId: 0, Occupation.Smith);
            AddHousehold(world, id: 1, districtId: 1, Occupation.Smith);

            var subject = world.Households[0];
            subject.UnaffordableNecessityCount = 1;
            subject.WorkshopInventory[Item.Tools] = 1; // 留保1(EquipmentThresholdStock)で販売在庫0。
            subject.ProductionRuns = 0;

            EconomySystemTestFixtures.RunDays(world, new HouseholdSystem(definition), days: 1);

            Assert.NotEqual(Occupation.Smith, world.Households[0].Occupation);
        }
    }

    /// <summary>
    /// 【核心】テスト表 #8(#237)。破産中・販売在庫0・生産量0だが入力は1回分ある世帯(Miller、
    /// 工房在庫[穀物]=2)→ 付け替えない(GDD02b §4.1 の※。cは入力切れだけを見る)。
    /// </summary>
    /// <remarks>
    /// <b>M4の実測</b>(<c>mutator</c>、2026-09-26、HEAD <c>c900127</c>)。
    /// <c>RunsFromInputs(...) != 0</c> の早期returnを消す(c を生産量0だけで判定する旧版)変異を
    /// 当てると、本テストは赤くなった(入力が残っていても付け替わり、Bakerになった)。期待との
    /// 食い違いは無い。
    /// </remarks>
    [Fact]
    public void GateStaysClosedWhenInputsRemainOnAZeroProductionDay()
    {
        var definition = WorldDefinition.M0;
        var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);

        AddHousehold(world, id: 0, districtId: 0, Occupation.Miller); // 被験者。
        AddHousehold(world, id: 1, districtId: 1, Occupation.Miller); // 相方(担い手2)。

        var subject = world.Households[0];
        subject.UnaffordableNecessityCount = 1;
        subject.WorkshopInventory[OutputItemId(Occupation.Miller)] = 0;
        subject.ProductionRuns = 0;
        subject.WorkshopInventory[Item.Grain] = 2; // 入力(穀物)から1回作れる → RunsFromInputs != 0。

        EconomySystemTestFixtures.RunDays(world, new HouseholdSystem(definition), days: 1);

        Assert.Equal(Occupation.Miller, world.Households[0].Occupation);
    }

    /// <summary>
    /// 【核心】テスト表 #9(#237)。入力0件のレシピの世帯(破産中・販売在庫0・生産量0)で
    /// <see cref="OccupationReassignment.IsGateOpen"/> が false(空のminを0にしない)。
    /// </summary>
    /// <remarks>
    /// <b>M5の実測</b>(<c>mutator</c>、2026-09-26、HEAD <c>c900127</c>)。
    /// <see cref="Recipe.RunsFromInputs"/> が入力0件で0を返す(空のminを0にする)変異を当てると、
    /// 本テストは赤くなった(<c>IsGateOpen</c>: 期待false → 実測true)。既存の
    /// <c>ProductionSystemTests.ProductionRunsWithoutInputsUpToCapacity</c> も同時に赤くなった
    /// (出力: 期待4 → 実測0。入力0件のレシピの生産が永久に止まる)。期待との食い違いは無い。
    /// </remarks>
    [Fact]
    public void GateStaysClosedForARecipeWithoutInputs()
    {
        var recipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = 1 } },
            inputs: Array.Empty<ItemQuantity>(),
            laborPermille: 1000);

        var definition = EconomySystemTestFixtures.BuildDefinition(recipe);
        var world = EconomySystemTestFixtures.BuildWorldWithOneHousehold(new[] { NpcRank.Master });

        var household = world.Households[0];
        household.IsBankrupt = 1;
        household.WorkshopInventory[Item.Bread] = 0;
        household.ProductionRuns = 0;

        Assert.False(OccupationReassignment.IsGateOpen(definition, household));
    }

    /// <summary>
    /// 【核心 C-2】テスト表 #6。その職業の担い手が自世帯だけなら、3条件が成立していても
    /// <see cref="HouseholdState.Occupation"/> は不変(GDD02b §4.2 が構造的に防ぐ「担い手0」)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(<c>mutator</c>、2026-09-23、<c>8bdd6ee</c>)。</b>
    /// <see cref="OccupationReassignment.TrySelectTarget"/> 冒頭の <c>CarrierCount(...) &lt;= 1</c>
    /// 早期returnを丸ごと落とす変異。期待は赤、実測は赤(5件) ── 落ちたのは本テスト(受け入れ対象)、
    /// <see cref="TrySelectTargetLeavesTheCurrentOccupationInTargetWhenItReturnsFalse"/>、
    /// <see cref="CarrierCountIsCountedLiveSoTheSecondCarrierStays"/>、
    /// <c>TradePipelineTests.EveryOccupationKeepsAtLeastOneCarrierOverSixtyDays(seed: 7)</c>、
    /// 同 <c>(seed: 42)</c>。受け入れ条件は引き続き本テストが落ちることであり、60日走行の赤は
    /// 測定値であって受け入れの根拠ではない(詳細は
    /// <see cref="TradePipelineTests.EveryOccupationKeepsAtLeastOneCarrierOverSixtyDays"/> のremarks)。
    /// </remarks>
    [Fact]
    public void LastCarrierOfAnOccupationIsNeverReassigned()
    {
        var definition = WorldDefinition.M0;
        var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);
        AddHousehold(world, id: 0, districtId: 0, Occupation.Smith);

        var subject = world.Households[0];
        subject.UnaffordableNecessityCount = 1;
        subject.WorkshopInventory[Item.Tools] = 0;
        subject.ProductionRuns = 0;

        EconomySystemTestFixtures.RunDays(world, new HouseholdSystem(definition), days: 1);

        Assert.Equal(Occupation.Smith, world.Households[0].Occupation);
    }

    /// <summary>
    /// テスト表 #7。担い手最少の職業(Baker、担い手1)が破産世帯と同じ区画に既に居るとき、それを
    /// 選ばず次に薄い職業(Brewer、担い手1・別区画)を選ぶ(GDD02b §4.2 段A)。
    /// </summary>
    /// <remarks>
    /// <b>被験者の区画を 0 以外にする(2巡目 #16)。</b>区画フィルタが見る区画を
    /// <c>household.DistrictId</c> の代わりに定数 <c>0</c> にする変異は、旧世界の被験者が全員
    /// 区画0だったため緑のまま通っていた(<c>ObservationsTests.WindowObservationRecordsTheCentreNotTheObserversDistrict</c>
    /// が塞いだのと同型の取り違え)。被験者・相方・候補すべてを区画0を含まない世界へ移す。
    /// </remarks>
    [Fact]
    public void TargetAvoidsAnOccupationAlreadyPresentInTheSameDistrict()
    {
        var definition = WorldDefinition.M0;
        var world = new World(npcCount: 8, householdCount: 8, itemCount: Item.Count);

        AddHousehold(world, id: 0, districtId: 30, Occupation.Miller); // 被験者。
        AddHousehold(world, id: 1, districtId: 31, Occupation.Miller); // Miller の相方(担い手2)。
        AddHousehold(world, id: 2, districtId: 30, Occupation.Baker);  // 被験者と同じ区画(除外対象)。
        AddHousehold(world, id: 3, districtId: 35, Occupation.Brewer); // 別区画(次点)。
        AddHousehold(world, id: 4, districtId: 36, Occupation.Woodworker);
        AddHousehold(world, id: 5, districtId: 37, Occupation.Woodworker); // Woodworker 担い手2。
        AddHousehold(world, id: 6, districtId: 38, Occupation.Smith);
        AddHousehold(world, id: 7, districtId: 32, Occupation.Smith); // Smith 担い手2。

        var subject = world.Households[0];
        subject.UnaffordableNecessityCount = 1;
        subject.WorkshopInventory[OutputItemId(Occupation.Miller)] = 0;
        subject.ProductionRuns = 0;

        EconomySystemTestFixtures.RunDays(world, new HouseholdSystem(definition), days: 1);

        Assert.Equal(Occupation.Brewer, world.Households[0].Occupation);
    }

    /// <summary>
    /// テスト表 #8。自職業以外の全職業が破産世帯と同じ区画に居る合成世界で、それでも
    /// 付け替わる(段B。GDD02b §4.2)。
    /// </summary>
    [Fact]
    public void DistrictOverlapIsAllowedWhenNoCandidateRemains()
    {
        var definition = WorldDefinition.M0;
        var world = new World(npcCount: 6, householdCount: 6, itemCount: Item.Count);

        AddHousehold(world, id: 0, districtId: 0, Occupation.Miller); // 被験者。
        AddHousehold(world, id: 1, districtId: 8, Occupation.Miller); // Miller の相方(担い手2)。
        AddHousehold(world, id: 2, districtId: 0, Occupation.Baker);
        AddHousehold(world, id: 3, districtId: 0, Occupation.Brewer);
        AddHousehold(world, id: 4, districtId: 0, Occupation.Woodworker);
        AddHousehold(world, id: 5, districtId: 0, Occupation.Smith);

        var subject = world.Households[0];
        subject.UnaffordableNecessityCount = 1;
        subject.WorkshopInventory[OutputItemId(Occupation.Miller)] = 0;
        subject.ProductionRuns = 0;

        EconomySystemTestFixtures.RunDays(world, new HouseholdSystem(definition), days: 1);

        // 段Aは4職業すべてが被験者と同じ区画に居るため空。段Bで区画フィルタを外し、
        // 担い手数が同値(各1)のうち職業Id最小のBakerを選ぶ。
        Assert.Equal(Occupation.Baker, world.Households[0].Occupation);
    }

    /// <summary>
    /// テスト表 #9。担い手数が同じ職業が複数あるとき、職業Idの小さい方を選ぶ(ADR-0002の列挙順規約)。
    /// </summary>
    [Fact]
    public void TieOnCarrierCountIsBrokenByOccupationId()
    {
        var definition = WorldDefinition.M0;
        var world = new World(npcCount: 8, householdCount: 8, itemCount: Item.Count);

        AddHousehold(world, id: 0, districtId: 0, Occupation.Miller); // 被験者。
        AddHousehold(world, id: 1, districtId: 7, Occupation.Miller); // Miller の相方(担い手2)。
        AddHousehold(world, id: 2, districtId: 7, Occupation.Baker);      // Baker 担い手1(タイ)。
        AddHousehold(world, id: 3, districtId: 7, Occupation.Woodworker); // Woodworker 担い手1(タイ)。
        AddHousehold(world, id: 4, districtId: 7, Occupation.Brewer);
        AddHousehold(world, id: 5, districtId: 7, Occupation.Brewer);     // Brewer 担い手2(タイより多い)。
        AddHousehold(world, id: 6, districtId: 7, Occupation.Smith);
        AddHousehold(world, id: 7, districtId: 7, Occupation.Smith);      // Smith 担い手2(タイより多い)。

        var subject = world.Households[0];
        subject.UnaffordableNecessityCount = 1;
        subject.WorkshopInventory[OutputItemId(Occupation.Miller)] = 0;
        subject.ProductionRuns = 0;

        EconomySystemTestFixtures.RunDays(world, new HouseholdSystem(definition), days: 1);

        // Baker(Id1)とWoodworker(Id3)が担い手1でタイ。職業Id昇順+`<`更新でBakerが残る。
        Assert.Equal(Occupation.Baker, world.Households[0].Occupation);
    }

    /// <summary>
    /// 【核心 C-3】テスト表 #10。同職業の2戸がともに3条件を満たす日、Id の小さい方だけが
    /// 付け替わり、大きい方は維持される(担い手数をその場で数えることの実体、GDD02b §4.2)。
    /// </summary>
    /// <remarks>
    /// <b>受け入れ条件は「赤」ではなく「本テストが落ちること」である(タスク仕様)。</b>他のテストが
    /// 道連れで落ちても、担い手世帯数の数え方を守った証拠にはならない。
    /// </remarks>
    /// <remarks>
    /// <b>変異の実測(<c>mutator</c>、2026-09-23、<c>8bdd6ee</c>)。</b>
    /// <see cref="HouseholdSystem.Step"/> の手順2 で担い手世帯数をループ前にスナップショットし、
    /// ループ中は配列を使い回す変異。期待は赤、実測は赤(4件) ── 落ちたのは本テスト(受け入れ対象)、
    /// <c>TradePipelineTests.EveryOccupationKeepsAtLeastOneCarrierOverSixtyDays(seed: 3)</c>、
    /// 同 <c>(seed: 42)</c>、<c>TradePipelineTests.SmithNeverRunsOutOfToolsOverSixtyDays(seed: 7)</c>。
    /// <b>当て方の注記(<c>mutator</c> の報告)</b>: <see cref="OccupationReassignment.TrySelectTarget"/>
    /// のシグネチャに必須引数を足すと既存の直接呼び出しテスト(本ファイル #19)がビルドエラーになるため、
    /// オプション引数でスナップショットを渡す形にし、未指定時はその場で等価なスナップショットを1回
    /// 組み立てるフォールバックを置いて当てた。変異の効果(ループ中は配列を使い回す = 世帯Idの小さい方
    /// の付け替えが、同じループ内の大きい方の判定に反映されない)はそのまま再現されている。
    /// </remarks>
    [Fact]
    public void CarrierCountIsCountedLiveSoTheSecondCarrierStays()
    {
        var definition = WorldDefinition.M0;
        var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);

        // Woodworker 2戸だけの世界(Miller/Baker/Brewer/Smithの世帯は1つも無い。他の職業の
        // 担い手数はすべて0なので、職業Id昇順のMillerがタイ無しで選ばれる)。
        AddHousehold(world, id: 0, districtId: 0, Occupation.Woodworker);
        AddHousehold(world, id: 1, districtId: 1, Occupation.Woodworker);

        foreach (var household in world.Households)
        {
            household.UnaffordableNecessityCount = 1;
            household.WorkshopInventory[OutputItemId(Occupation.Woodworker)] = 0;
            household.ProductionRuns = 0;
        }

        EconomySystemTestFixtures.RunDays(world, new HouseholdSystem(definition), days: 1);

        // Id0を処理する時点でWoodworkerの担い手数は2(自分を含む)なので付け替わる。
        Assert.Equal(Occupation.Miller, world.Households[0].Occupation);
        // Id1を処理する時点ではId0が既に離脱しているのでWoodworkerの担い手数は1(自分だけ)、維持される。
        Assert.Equal(Occupation.Woodworker, world.Households[1].Occupation);
    }

    /// <summary>
    /// テスト表 #11(W2-24で書き換え)。付け替えの前後で <see cref="HouseholdState.Occupation"/> と
    /// <see cref="HouseholdState.ProductionProgressPermille"/>(0に戻る)以外が不変
    /// (GDD02b §4.2「変えないもの」の表)。木材(生産の入力)は0にしてゲートcを閉じさせない
    /// ── 42のままだと入力から21回作れてしまい(新しいc)、付け替わらなくなる(タスク仕様
    /// 「9. 既存テストの追随」)。「ゲートに関係しない工房在庫」の役は穀物(Millerの入力・
    /// Woodworkerには無関係)に譲る。
    /// </summary>
    /// <remarks>
    /// <b>M6の実測</b>(<c>mutator</c>、2026-09-26、HEAD <c>c900127</c>)。付け替えの
    /// <c>ProductionProgressPermille = 0</c> の代入を消す変異を当てると、本テストは赤くなった
    /// (進捗‰: 期待0 → 実測90)。期待との食い違いは無い。
    /// </remarks>
    /// <remarks>
    /// <b>M10の実測</b>(<c>mutator</c>、2026-09-26、HEAD <c>c900127</c>。レビュー1巡目 象限I-aで
    /// 追加)。付け替え時に <c>ProductionCapacityRuns = 0</c> も書く変異を当てると、本テストは
    /// 赤くなった(能力: 期待3 → 実測0)。期待との食い違いは無い。
    /// </remarks>
    [Fact]
    public void ReassignmentTouchesNothingButTheOccupation()
    {
        var definition = WorldDefinition.M0;
        var world = new World(npcCount: 2, householdCount: 2, itemCount: Item.Count);

        AddHousehold(world, id: 0, districtId: 3, Occupation.Woodworker); // 被験者。
        AddHousehold(world, id: 1, districtId: 5, Occupation.Woodworker); // 相方(担い手2)。

        var subject = world.Households[0];
        subject.UnaffordableNecessityCount = 1;
        subject.LiquidFunds = 500;
        subject.WorkshopInventory[OutputItemId(Occupation.Woodworker)] = 0; // ゲートb。
        subject.WorkshopInventory[Item.Timber] = 0; // 生産の入力。ゲートcを閉じさせない。
        subject.WorkshopInventory[Item.Grain] = 42; // ゲートに関係しない工房在庫(Woodworkerの入力ではない)。
        subject.HouseholdInventory[Item.Firewood] = 7;
        subject.ProductionRuns = 0;
        subject.ProductionProgressPermille = 90; // 付け替えで0に戻ることを断定するため正の値にする。

        // 既定値0のままだと「付け替えで能力が変わらない」の断定が0と0の比較になり、能力を0に
        // 消す実装ミスを見逃す(レビュー1巡目 象限I-a)。入力(木材)が0なので生産量0のまま
        // ゲートcと両立する正の値を置く(タスク仕様5c・表#10)。
        subject.ProductionCapacityRuns = 3;
        int capacityBefore = subject.ProductionCapacityRuns;

        var memberNpcIdsBefore = (int[])subject.MemberNpcIds.Clone();

        EconomySystemTestFixtures.RunDays(world, new HouseholdSystem(definition), days: 1);

        Assert.NotEqual(Occupation.Woodworker, subject.Occupation); // 前提: 実際に付け替わった。
        Assert.Equal(1, subject.IsBankrupt); // 付け替えでフラグを降ろさない。
        Assert.Equal(500, subject.LiquidFunds);
        Assert.Equal(0, subject.WorkshopInventory[OutputItemId(Occupation.Woodworker)]);
        Assert.Equal(0, subject.WorkshopInventory[Item.Timber]);
        Assert.Equal(42, subject.WorkshopInventory[Item.Grain]);
        Assert.Equal(7, subject.HouseholdInventory[Item.Firewood]);
        Assert.Equal(3, subject.DistrictId);
        Assert.Equal(0, subject.HeadNpcId);
        Assert.Equal(memberNpcIdsBefore, subject.MemberNpcIds);
        Assert.Equal(0, subject.ProductionProgressPermille); // GDD02b §4.2が戻すのは進捗‰だけ。
        Assert.Equal(capacityBefore, subject.ProductionCapacityRuns); // 当日の値のまま(順3は触らない)。
    }

    /// <summary>
    /// テスト表 #12。<c>new ISimSystem[] { new HouseholdSystem(d), new TradeSystem(d) }</c> の順で
    /// 登録し、1日目に必需の資金不足を起こす。2日目の順3でフラグが立ち、同じ2日目の順5で
    /// その売り手の提示価格が半値(床の2倍を超える相場基準のとき)へ、販売在庫が全量窓口へ出る
    /// (閾在庫0)。<b>順3からフラグが届く経路そのものを押さえる</b> ── ②の分岐自体は
    /// <c>TradeSystemTests</c> の既存テストが押さえている。
    /// </summary>
    [Fact]
    public void BankruptFlagReachesTheOfferPriceAndTheExportThreshold()
    {
        var definition = BuildBankruptcyPipelineDefinition();
        var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);

        // 中心区画(窓口)に置く。IsWithinReachが自区画=中心を無条件に真とするので、買い物・輸出の
        // どちらも往復時間の計算に立ち入らずに済む(タスク仕様「数値は仕様ではない」の趣旨に沿い、
        // 構成を単純に保つ)。
        AddHousehold(world, id: 0, districtId: District.ExternalMarketDistrictId, Occupation.Miller);
        var seller = world.Households[0];
        seller.LiquidFunds = 0; // 必需(薪)の現金上限を0にし、経路(1)の資金不足を起こす。
        seller.WorkshopInventory[Item.Bread] = 1; // 破産中でも販売在庫は正(④のゲートbが閉じる)。

        var systems = new ISimSystem[] { new HouseholdSystem(definition), new TradeSystem(definition) };
        var scheduler = new SimScheduler(systems, new RandomSource(1));

        scheduler.Advance(world, ticks: 24); // 1日目。

        // 前提: 1日目に必需(薪)の資金不足が経路(1)でちょうど1件起きる。フラグはまだ立たない
        // (順3がこのtickで読むのは前日=存在しない日の値である)。
        Assert.Equal(1, seller.UnaffordableNecessityCount);
        Assert.Equal(0, seller.IsBankrupt);

        // 他の売り手の観測を仕込む(前日=1日目の日付として2日目に有効になる)。世帯は中心区画に
        // 居るため、1日目のうちに窓口自身のパン価格(ApplyPermille(床10, 2000) = 20)も
        // 自動的に観測済みである(#149。Observations.CollectWindow)。相場基準は
        // 「他の売り手ごとに最新1件」の平均(GDD02c §1.2)なので、窓口(20)とこの観測(380)の
        // 平均 = 200。床10の2倍(20)を上回る ── 破産中でなければ提示価格は床のままで
        // 分岐が観測できない(GDD02c §1.4)。
        world.Knowledge[seller.HeadNpcId].Add(new PriceObservation
        {
            ItemId = Item.Bread,
            LocationId = 0,
            Price = 380,
            SellerId = 999,
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        });

        scheduler.Advance(world, ticks: 24); // 2日目。順3がフラグを立て、同じ日の順5が反映する。

        Assert.Equal(1, seller.IsBankrupt);
        Assert.Equal(Occupation.Miller, seller.Occupation); // ④のゲートbが閉じる(販売在庫が正)ので職業は動かない。

        var key = new MarketKey(Item.Bread, seller.Id);
        Assert.True(world.Market.ContainsKey(key));
        // 相場基準 = CeilDiv(20 + 380, 2) = 200。破産中は係数‰を500に固定する:
        // ApplyPermille(200, 500) = 100(床10より大きいので採用)。
        Assert.Equal(100, world.Market[key]);

        // 輸出: 破産中は閾在庫0(GDD02d §2.3)。販売在庫1が全量窓口へ出る。
        Assert.Equal(0, seller.WorkshopInventory[Item.Bread]);
        Assert.Contains(
            world.Ledgers[seller.Id],
            entry => entry.Direction == LedgerDirection.Sale
                && entry.CounterpartyId == HouseholdState.ExternalMarketSellerId
                && entry.ItemId == Item.Bread);
    }

    /// <summary>
    /// テスト表 #14(レビュー1巡目)。自職業の担い手が全候補職業より真に少なくても、
    /// <c>argmin</c> は自職業を返さない(GDD02b §4.2「候補は自職業を除く」)。
    /// </summary>
    /// <remarks>
    /// <b>1巡目のレビュー指摘の実体。</b><see cref="OccupationReassignment.TrySelectTarget"/> の
    /// 「自職業を除く」行(<c>if (candidate == household.Occupation) { continue; }</c>)を丸ごと
    /// 消しても、既存の #1〜#13 は1つも落ちない ── 既存テストの世界はどれも「自職業の担い手数が
    /// 候補の最小値を上回る」ように組まれており、<c>argmin</c> が自職業を返す状況を作れていなかった
    /// (2026-09-23)。消したときの帰結: <c>argmin</c> が自職業を返し、
    /// <c>household.Occupation = target</c> が自己代入の no-op になる。ゲートは翌日も開いたままで、
    /// その世帯の④が<b>永久に発火しない</b>。
    /// <para>
    /// <b>段A(区画フィルタが効く経路)は、自職業を除く行を消しても単体では赤にならない。</b>
    /// Stage A の区画フィルタ(<see cref="OccupationReassignment"/> 内 <c>IsOccupationPresentInDistrict</c>)
    /// は「被験者と同じ区画に、その職業の他世帯が居るか」を <b>被験者自身を含めて</b> 数える。
    /// 自職業を候補として評価すると、被験者自身が「被験者の区画に居る自職業の世帯」として必ず
    /// マッチし、区画フィルタ単独で自職業を除外してしまう ── 自職業を除く行の有無に関わらず結果が
    /// 変わらない。したがって以下の段Aブロックは、自職業を除く行を消しても常に緑のまま通る。
    /// これは仕様として構わない ── 本テストの断定は<c>[Fact]</c>1本の中の複数ブロックであり、
    /// どこか1つでも落ちれば本テスト全体が赤になる。<b>実際に赤にするのは段Bブロックである。</b>
    /// 段Aブロックを残すのは、段Aでも「自職業が最少でも選ばれない」という契約(GDD02b §4.2)自体は
    /// 保たれていることを確認するためである。
    /// </para>
    /// <para>
    /// <b>段B(候補が空で区画フィルタを外す経路)はこの変異で確実に赤になる。</b>段Bは区画フィルタを
    /// 掛けないので、自職業の除外を保証するのは「自職業を除く」行だけになる。この行を消すと、
    /// 自職業(担い手2)が他職業(担い手3)より少ないので <c>argmin</c> が自職業を選び、
    /// <c>Occupation</c> が変わらない ── 下の <c>Assert.NotEqual</c> / <c>Assert.Equal</c> が落ちる。
    /// 手元でこの行を一時的に消して確認済み(2026-09-23、実装者手元。正式な実測は
    /// <a href="../../.claude/agents/mutator.md">mutator</a> が別途行う)。
    /// </para>
    /// </remarks>
    [Fact]
    public void SelfOccupationIsExcludedEvenWhenItHasTheFewestCarriers()
    {
        var definition = WorldDefinition.M0;

        // 段A: 自職業(Woodworker)の担い手2、他4職業はいずれも担い手3。他職業はすべて被験者の
        // 区画(0)の外に置く ── 段Aで少なくとも1件は候補が残る世界(段Bへ落ちない)。
        {
            var world = new World(npcCount: 14, householdCount: 14, itemCount: Item.Count);

            AddHousehold(world, id: 0, districtId: 0, Occupation.Woodworker); // 被験者。
            AddHousehold(world, id: 1, districtId: 9, Occupation.Woodworker); // 相方(自職業の担い手2)。

            AddHousehold(world, id: 2, districtId: 1, Occupation.Miller);
            AddHousehold(world, id: 3, districtId: 1, Occupation.Miller);
            AddHousehold(world, id: 4, districtId: 1, Occupation.Miller);

            AddHousehold(world, id: 5, districtId: 2, Occupation.Baker);
            AddHousehold(world, id: 6, districtId: 2, Occupation.Baker);
            AddHousehold(world, id: 7, districtId: 2, Occupation.Baker);

            AddHousehold(world, id: 8, districtId: 3, Occupation.Brewer);
            AddHousehold(world, id: 9, districtId: 3, Occupation.Brewer);
            AddHousehold(world, id: 10, districtId: 3, Occupation.Brewer);

            AddHousehold(world, id: 11, districtId: 4, Occupation.Smith);
            AddHousehold(world, id: 12, districtId: 4, Occupation.Smith);
            AddHousehold(world, id: 13, districtId: 4, Occupation.Smith);

            var subject = world.Households[0];
            subject.UnaffordableNecessityCount = 1;
            subject.WorkshopInventory[OutputItemId(Occupation.Woodworker)] = 0;
            subject.ProductionRuns = 0;

            EconomySystemTestFixtures.RunDays(world, new HouseholdSystem(definition), days: 1);

            // 自職業(担い手2)は全候補中最少だが選ばれない。担い手3で並ぶ他4職業のうち
            // 職業Id最小のMillerが選ばれる。
            Assert.Equal(Occupation.Miller, world.Households[0].Occupation);
        }

        // 段B: 他4職業をすべて被験者の区画(0)に置き、段Aを空にする(区画フィルタを外す経路へ)。
        {
            var world = new World(npcCount: 14, householdCount: 14, itemCount: Item.Count);

            AddHousehold(world, id: 0, districtId: 0, Occupation.Woodworker); // 被験者。
            AddHousehold(world, id: 1, districtId: 9, Occupation.Woodworker); // 相方(自職業の担い手2)。

            AddHousehold(world, id: 2, districtId: 0, Occupation.Miller);
            AddHousehold(world, id: 3, districtId: 0, Occupation.Miller);
            AddHousehold(world, id: 4, districtId: 0, Occupation.Miller);

            AddHousehold(world, id: 5, districtId: 0, Occupation.Baker);
            AddHousehold(world, id: 6, districtId: 0, Occupation.Baker);
            AddHousehold(world, id: 7, districtId: 0, Occupation.Baker);

            AddHousehold(world, id: 8, districtId: 0, Occupation.Brewer);
            AddHousehold(world, id: 9, districtId: 0, Occupation.Brewer);
            AddHousehold(world, id: 10, districtId: 0, Occupation.Brewer);

            AddHousehold(world, id: 11, districtId: 0, Occupation.Smith);
            AddHousehold(world, id: 12, districtId: 0, Occupation.Smith);
            AddHousehold(world, id: 13, districtId: 0, Occupation.Smith);

            var subject = world.Households[0];
            subject.UnaffordableNecessityCount = 1;
            subject.WorkshopInventory[OutputItemId(Occupation.Woodworker)] = 0;
            subject.ProductionRuns = 0;

            EconomySystemTestFixtures.RunDays(world, new HouseholdSystem(definition), days: 1);

            // 段Aは他4職業すべてが被験者の区画に居るため空 → 段Bへ。区画フィルタを外しても
            // 自職業(担い手2、他職業より真に少ない)は除外されたままなので、担い手3で並ぶ
            // 他4職業のうち職業Id最小のMillerが選ばれる。「自職業を除く」行を消すと、担い手最少の
            // 自職業がargminに残り、Occupation == Woodworkerのまま(自己代入のno-op)になって
            // 以下の2つの断定が落ちる。
            Assert.NotEqual(Occupation.Woodworker, world.Households[0].Occupation);
            Assert.Equal(Occupation.Miller, world.Households[0].Occupation);
        }
    }

    /// <summary>
    /// テスト表 #15(2巡目)。付け替え先は「候補中の担い手最少」であって「候補中の職業 Id 最小」
    /// ではない(GDD02b §4.2 段3)。
    /// </summary>
    /// <remarks>
    /// <b>2つの変異がこのテストまで緑のまま通っていた。</b>
    /// <list type="bullet">
    /// <item><c>argmin</c> の更新条件を <c>if (!found || count &lt; bestCount)</c> から
    /// <c>if (!found)</c> へ退化させる(= 担い手数を一切見ず、最初に見つかった候補=職業Id最小を
    /// 採る)変異。</item>
    /// <item>走査上限を <c>definition.OccupationCount - 1</c> にする(= 職業Id最大が候補から
    /// 消える)変異。</item>
    /// </list>
    /// 既存 #7/#8/#9/#10/#14 はいずれも「期待する付け替え先が同時に候補中の職業Id最小」の世界
    /// だったので、この2つの変異のどちらでも緑のまま通っていた ── GDD02b §4.2 の中核(担い手数で
    /// 決める)を1本も押さえていなかった(2巡目の網羅パスの指摘)。
    /// <para>
    /// 本テストは、候補中の担い手最少が Smith(職業Id 4、最大)になるよう組む。Miller・Baker・Brewer
    /// をそれぞれ担い手3、Smith を担い手1(decoy世帯1戸のみ)にする。「最初に見つかった候補を採る」
    /// 変異では職業Id最小の Miller が選ばれ、「走査上限を1手前で止める」変異では Smith が候補から
    /// 消えて Miller(担い手3のタイの中で最小Id)が選ばれる。どちらも期待する Smith と一致しない。
    /// </para>
    /// <para>
    /// <b>被験者を世帯 Id 1 に置く(#18)。</b>世帯 Id 0 には無関係な decoy(Smith、健全)を置く ──
    /// この decoy がそのまま Smith の唯一の担い手(担い手1)を兼ねる。
    /// </para>
    /// </remarks>
    [Fact]
    public void TargetIsTheOccupationWithFewestCarriersNotTheLowestId()
    {
        var definition = WorldDefinition.M0;
        var world = new World(npcCount: 12, householdCount: 12, itemCount: Item.Count);

        AddHousehold(world, id: 0, districtId: 50, Occupation.Smith); // decoy(#18)。健全、Smithの唯一の担い手。

        AddHousehold(world, id: 1, districtId: 0, Occupation.Woodworker); // 被験者。
        AddHousehold(world, id: 2, districtId: 9, Occupation.Woodworker); // Woodworker の相方(担い手2)。

        AddHousehold(world, id: 3, districtId: 1, Occupation.Miller);
        AddHousehold(world, id: 4, districtId: 1, Occupation.Miller);
        AddHousehold(world, id: 5, districtId: 1, Occupation.Miller); // Miller 担い手3。

        AddHousehold(world, id: 6, districtId: 2, Occupation.Baker);
        AddHousehold(world, id: 7, districtId: 2, Occupation.Baker);
        AddHousehold(world, id: 8, districtId: 2, Occupation.Baker); // Baker 担い手3。

        AddHousehold(world, id: 9, districtId: 3, Occupation.Brewer);
        AddHousehold(world, id: 10, districtId: 3, Occupation.Brewer);
        AddHousehold(world, id: 11, districtId: 3, Occupation.Brewer); // Brewer 担い手3。

        var subject = world.Households[1];
        subject.UnaffordableNecessityCount = 1;
        subject.WorkshopInventory[OutputItemId(Occupation.Woodworker)] = 0;
        subject.ProductionRuns = 0;

        EconomySystemTestFixtures.RunDays(world, new HouseholdSystem(definition), days: 1);

        // Smith(担い手1)が候補中最少。Miller/Baker/Brewer(いずれも担い手3)より少ない。
        Assert.Equal(Occupation.Smith, world.Households[1].Occupation);
    }

    /// <summary>
    /// テスト表 #19(2巡目)。<see cref="OccupationReassignment.TrySelectTarget"/> が false を
    /// 返すとき、<c>target</c> には現在の職業が置かれる(GDD02b §4.2 のdocコメントの契約)。
    /// </summary>
    /// <remarks>
    /// <b><see cref="OccupationReassignment.TrySelectTarget"/> を直接呼ぶ。</b><c>target = household.Occupation;</c>
    /// を <c>target = default;</c> にする変異は、<see cref="HouseholdSystem.Step"/> 経由の呼び出しでは
    /// 呼び出し側が false のとき <c>continue</c> して <c>target</c> を読まないため、隠れて観測でき
    /// ない(<see cref="HouseholdSystem"/> 側の2つ目の <c>continue</c> を消す変異も同じ理由で隠れる
    /// ── 2つの契約が互いの検出器を打ち消し合っている)。直接呼んで <c>target</c> を断定することでしか
    /// 機械で守れない(2巡目の網羅パスの指摘)。
    /// </remarks>
    [Fact]
    public void TrySelectTargetLeavesTheCurrentOccupationInTargetWhenItReturnsFalse()
    {
        var definition = WorldDefinition.M0;
        var world = new World(npcCount: 1, householdCount: 1, itemCount: Item.Count);
        AddHousehold(world, id: 0, districtId: 0, Occupation.Smith); // 唯一の担い手 → false になる。

        bool result = OccupationReassignment.TrySelectTarget(
            definition, world, world.Households[0], out var target);

        Assert.False(result);
        Assert.Equal(Occupation.Smith, target);
    }

    /// <summary>
    /// テスト表 #20(2巡目)。<see cref="HouseholdSystem.Stream"/> が
    /// <see cref="RandomStream.Household"/> であり、<see cref="HouseholdSystem.Cadence"/> が
    /// 「1日1回・0時」であることを直接断定する。
    /// </summary>
    /// <remarks>
    /// <b>塞がないと決めた3つのうちの1つ(タスク仕様)。</b>順3は乱数を引かないので、
    /// <see cref="HouseholdSystem.Stream"/> を <see cref="RandomStream.NeedGeneration"/> に変える
    /// 変異は既存の #1〜#14 のどれも落とさない(乱数を引かないため挙動が変わらない)。本テストが
    /// <c>Stream</c> と <c>Cadence</c> の値そのものを直接断定して塞ぐ。<c>Cadence</c> は等価比較を
    /// 公開していないので、<see cref="Cadence.ShouldRunAt"/> で「0時のみ真」を確かめる。
    /// </remarks>
    [Fact]
    public void StreamIsHouseholdAndCadenceIsDailyAtHourZero()
    {
        var system = new HouseholdSystem(WorldDefinition.M0);

        Assert.Equal(RandomStream.Household, system.Stream);

        for (int hour = 0; hour < Tick.HoursPerDay; hour++)
        {
            bool expectedToRun = hour == 0;
            Assert.Equal(expectedToRun, system.Cadence.ShouldRunAt(new Tick(hour)));
        }
    }

    /// <summary>Millerがパン(必需候補)を木材から作る、テスト#12専用の定義。</summary>
    private static WorldDefinition BuildBankruptcyPipelineDefinition()
    {
        var millerBreadRecipe = new Recipe(
            Occupation.Miller,
            outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = 1 } },
            inputs: new[] { new ItemQuantity { ItemId = Item.Timber, Quantity = 1 } },
            laborPermille: 1000);

        var necessityTargetStockDays = new int[Item.Count];
        necessityTargetStockDays[Item.Firewood] = 1; // 世帯自身が必要とする必需(自分の出力=パンとは別品目)。

        var dailyConsumptionPerNpcByRank = new[]
        {
            BuildRow(Item.Firewood, quantity: 1), // 親方。
            new int[Item.Count],                  // 職人(この世界には居ない)。
            new int[Item.Count],                  // 徒弟(同上)。
        };

        // 都市生産品の外部買値。パン(自分の出力)は床10で②の観測に使う。穀物(UnusedRecipeの出力)は
        // 既定の1のまま保つ(WorldDefinitionの検査が都市生産品の外部買値≥1を要求するため)。
        var externalBuyPrice = new int[Item.Count];
        externalBuyPrice[Item.Grain] = 1;
        externalBuyPrice[Item.Bread] = 10;

        return EconomySystemTestFixtures.BuildDefinition(
            millerBreadRecipe,
            dailyConsumptionPerNpcByRank: dailyConsumptionPerNpcByRank,
            necessityTargetStockDays: necessityTargetStockDays,
            tolerancePermille: 1200,
            opportunityCostBaseByOccupation: new[] { 1, 1, 1, 1, 1 },
            rankCoefficientPermille: new[] { 1000, 1000, 1000 },
            travelHoursPerDistrict: 1,
            shipmentDays: 1,
            inputBufferDays: 1,
            externalBuyPriceOverride: externalBuyPrice,
            isExportEnabled: true);
    }

    private static int[] BuildRow(int itemId, int quantity)
    {
        var row = new int[Item.Count];
        row[itemId] = quantity;

        return row;
    }
}
