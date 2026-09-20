using System.IO.Hashing;
using System.Reflection;
using Visionary.Sim.Determinism;
using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Determinism;

/// <summary>
/// <see cref="StateHasher"/> の回帰テスト(TDD01 §3.8)。合成システム(<c>SyntheticLoadSystem</c> /
/// <c>SyntheticDecaySystem</c>)は <c>Visionary.Sim.Runner</c> 側にあるため、
/// ここでは触らず <see cref="World"/> を直接組み立てる。
/// </summary>
public sealed class StateHasherTests
{
    // XXH64(seed 0) の空入力のハッシュ(実測値は 0xEF46DB3751D8E999。0 ではない)。
    // この値と一致するなら Compute は1バイトも Append していない(テスト12)。
    //
    // リテラルで書かず XxHash64 から導く。Assert.NotEqual(定数, hash) の形なので、
    // 定数を書き間違えるとテストは常に緑になり、「Append を全て消す」変異が素通りする
    // (真の空入力ハッシュは誤った定数と一致しないため)。
    private static readonly ulong XxHash64OfNoInput = new XxHash64().GetCurrentHashAsUInt64();

    /// <summary>構成員1人の世帯を1戸だけ持つ世界。世帯持ちの区分を触るテストの土台。</summary>
    private static World OneHouseholdWorld(int itemCount = 0) =>
        new(npcCount: 1, householdCount: 1, itemCount: itemCount);

    [Fact]
    public void HashChangesWhenClockAdvances()
    {
        var world = new World(npcCount: 0, householdCount: 0, itemCount: 0);
        ulong before = StateHasher.Compute(world);

        // World.Now は internal set。公開APIで時刻を進めるため SimScheduler を素通しで使う。
        var scheduler = new SimScheduler(Array.Empty<ISimSystem>(), new RandomSource(1));
        scheduler.Advance(world, ticks: 1);

        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    /// <summary>
    /// 【核心】Households の走査が順序依存であること。順序非依存の畳み込み(XOR・加算)に変えると、
    /// 2つの世帯の値を入れ替えても合計・XORは変わらないため、このテストが緑のまま壊れを見逃す。
    /// </summary>
    [Fact]
    public void HashChangesWhenTwoHouseholdsSwapTheirFunds()
    {
        var world = new World(npcCount: 6, householdCount: 6, itemCount: 0);
        world.Households[3].LiquidFunds = 100;
        world.Households[5].LiquidFunds = 200;
        ulong before = StateHasher.Compute(world);

        world.Households[3].LiquidFunds = 200;
        world.Households[5].LiquidFunds = 100;
        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    /// <summary>
    /// 【核心】Knowledge の格納順が状態の一部であること。走査前に <c>OrderBy</c> などで
    /// 正規化すると、同じ2件を逆順にしただけでは(ソート結果が同じため)ハッシュが変わらなくなる。
    /// </summary>
    [Fact]
    public void HashChangesWhenKnowledgeListIsPermuted()
    {
        var world = OneHouseholdWorld();
        world.Knowledge[0].Add(new PriceObservation
        {
            ItemId = 1,
            LocationId = 0,
            Price = 10,
            SellerId = 0,
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        });
        world.Knowledge[0].Add(new PriceObservation
        {
            ItemId = 2,
            LocationId = 0,
            Price = 20,
            SellerId = 0,
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        });
        ulong before = StateHasher.Compute(world);

        (world.Knowledge[0][0], world.Knowledge[0][1]) = (world.Knowledge[0][1], world.Knowledge[0][0]);
        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    /// <summary>
    /// 【核心】相場知識の所有者が状態の一部であること(テスト7)。
    /// </summary>
    /// <remarks>
    /// <b>所有者の取り違えは型では防げない。</b><see cref="World.Knowledge"/> と
    /// <see cref="World.Ledgers"/> はどちらも <c>List&lt;T&gt;[]</c> で、添字の意味だけが違う
    /// (NpcId と世帯Id、TDD01 §3.2)。所有者を畳んで平坦に走査する実装に変えると、
    /// 同じ観測を誰が持っていても同じハッシュになる。W3 の Rumor(TDD01 §3.3-9)は
    /// 観測を家族・仕事仲間・近所へ複製する処理なので、伝播先の取り違えがここで死角に入る。
    /// <para>
    /// <b>変異の実測(2026-09-14)。</b>所有者Id と件数の前置をやめ
    /// <c>SelectMany</c> で平坦に走査する変異を当てて赤になることを確認した。
    /// </para>
    /// </remarks>
    [Fact]
    public void HashDistinguishesKnowledgeOwners()
    {
        var observation = new PriceObservation
        {
            ItemId = 3,
            LocationId = 4,
            Price = 50,
            SellerId = 0,
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        };

        var ownedByNpc0 = new World(npcCount: 2, householdCount: 0, itemCount: 0);
        ownedByNpc0.Knowledge[0].Add(observation);

        var ownedByNpc1 = new World(npcCount: 2, householdCount: 0, itemCount: 0);
        ownedByNpc1.Knowledge[1].Add(observation);

        Assert.NotEqual(StateHasher.Compute(ownedByNpc0), StateHasher.Compute(ownedByNpc1));
    }

    /// <summary>帳簿の所有者が状態の一部であること(テスト8)。所有者は世帯(GDD08 §2.2)。</summary>
    [Fact]
    public void HashDistinguishesLedgerOwners()
    {
        var entry = new LedgerEntry
        {
            CounterpartyId = 7,
            ItemId = 1,
            Quantity = 2,
            UnitPrice = 30,
            OccurredAt = Tick.Zero,
            Terms = LedgerTerms.Cash,
            CreditDueAt = Tick.Zero,
        };

        var ownedByHousehold0 = new World(npcCount: 2, householdCount: 2, itemCount: 0);
        ownedByHousehold0.Ledgers[0].Add(entry);

        var ownedByHousehold1 = new World(npcCount: 2, householdCount: 2, itemCount: 0);
        ownedByHousehold1.Ledgers[1].Add(entry);

        Assert.NotEqual(
            StateHasher.Compute(ownedByHousehold0), StateHasher.Compute(ownedByHousehold1));
    }

    /// <summary>
    /// 【核心】世帯在庫と工房在庫が別勘定であること(テスト6)。
    /// </summary>
    /// <remarks>
    /// 在庫を2本に分けるのは構造的な要請である(TDD01 §3.2)。薪(itemId 5、GDD02 §2.2)は
    /// 必需の消費財でもパン・ビールの生産入力でもあり、1本に畳むと GDD02b §2 の目標在庫が
    /// 用途別に決まらなくなる。
    /// <para>
    /// <b>変異の実測(2026-09-14)。</b>2本を要素ごとの和で1本に畳む変異を当てて赤になることを
    /// 確認した。<b>「片方を書き忘れる」変異は緑のまま通る</b> — 2つの世界で在庫の中身自体が
    /// 違うため、残った1本の側でハッシュが変わるからである。そちらは
    /// <see cref="HashChangesWhenEitherInventoryChanges"/> が捕まえる。
    /// </para>
    /// </remarks>
    [Fact]
    public void HashDistinguishesHouseholdInventoryFromWorkshopInventory()
    {
        const int FirewoodItemId = 5; // 薪。消費財でも生産入力でもある(GDD02 §2.2)

        var inHousehold = OneHouseholdWorld(itemCount: 9);
        inHousehold.Households[0].HouseholdInventory[FirewoodItemId] = 3;

        var inWorkshop = OneHouseholdWorld(itemCount: 9);
        inWorkshop.Households[0].WorkshopInventory[FirewoodItemId] = 3;

        Assert.NotEqual(StateHasher.Compute(inHousehold), StateHasher.Compute(inWorkshop));
    }

    /// <summary>
    /// 在庫の各本が単独でハッシュに乗ること。<b>片方の書き忘れを捕まえるのはこちらである。</b>
    /// </summary>
    /// <remarks>
    /// <see cref="HashDistinguishesHouseholdInventoryFromWorkshopInventory"/> は
    /// 「2本を1本に畳む」変異を捕まえるが、<b>片方を書き忘れる変異は捕まえない</b> —
    /// あちらは2つの世界で在庫の中身そのものが違うため、残った1本の側でハッシュが変わって
    /// しまい、緑のまま通る(2026-09-14 実測)。2種類の壊し方には2種類のテストが要る。
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HashChangesWhenEitherInventoryChanges(bool workshop)
    {
        var world = OneHouseholdWorld(itemCount: 9);
        ulong before = StateHasher.Compute(world);

        var inventory = workshop
            ? world.Households[0].WorkshopInventory
            : world.Households[0].HouseholdInventory;

        inventory[5] = 3;
        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    /// <summary>
    /// 世帯を1戸だけ持ち、その不変欄を指定した世界を作る。
    /// </summary>
    /// <remarks>
    /// 区画Id・世帯主・構成員は不変(GDD02 §4.3 / GDD08 §2.1)なので、後から変えて比べられない。
    /// 違う引数で組み立てた2つの世界を比べる形でしか、これらの欄がハッシュに乗っているかを
    /// 確かめられない。
    /// </remarks>
    private static World WorldWithOneHousehold(int districtId, int headNpcId, int[] memberNpcIds)
    {
        var world = new World(npcCount: 3, householdCount: 1, itemCount: 0);

        world.Households[0] = new HouseholdState(
            id: 0,
            districtId: districtId,
            headNpcId: headNpcId,
            memberNpcIds: memberNpcIds,
            itemCount: 0);

        return world;
    }

    /// <summary>
    /// 世帯の不変欄がハッシュに乗ること。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>2プロセス比較では原理的に検出できない欄である。</b>同一ビルド同士を比べるので、
    /// ハッシュが状態の一部を見ていなくても「一致」は成立する。したがってここで押さえるしかない。
    /// </para>
    /// <para>
    /// <b>区画Id を含めるのは §3.8 の明示的な要求である</b> — 「不変だが初期配置の一部であり、
    /// シードから決まる世界の同一性に属する」。<b>構成員列</b>は世帯内の処理順
    /// (GDD02b §3.2 の購入の決済順)を決めるデータなので、落ちると並びの違う世界が
    /// 同じハッシュになる。
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("district")]
    [InlineData("head")]
    [InlineData("memberIds")]
    [InlineData("memberCount")]
    public void HashDistinguishesHouseholdsByTheirImmutableFields(string field)
    {
        var baseline = WorldWithOneHousehold(districtId: 0, headNpcId: 0, memberNpcIds: new[] { 0, 1 });

        var varied = field switch
        {
            "district" => WorldWithOneHousehold(3, headNpcId: 0, memberNpcIds: new[] { 0, 1 }),
            "head" => WorldWithOneHousehold(0, headNpcId: 1, memberNpcIds: new[] { 0, 1 }),
            "memberIds" => WorldWithOneHousehold(0, headNpcId: 0, memberNpcIds: new[] { 0, 2 }),
            "memberCount" => WorldWithOneHousehold(0, headNpcId: 0, memberNpcIds: new[] { 0 }),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "未知のフィールド。"),
        };

        Assert.NotEqual(StateHasher.Compute(baseline), StateHasher.Compute(varied));
    }

    /// <summary>
    /// 世帯 Id は添字と一致する(TDD01 §3.2)。
    /// </summary>
    /// <remarks>
    /// <b>この不変条件があるために、<c>StateHasher</c> が書く <c>household.Id</c> は冗長である</b> —
    /// 添字順に走査している以上、Id を書かなくても到達できるハッシュの集合は変わらない。
    /// したがって「Id の書き忘れ」を値で検出するテストは書けない(書けば、World が作らない
    /// 不正な状態を組み立てることになる)。<c>Npcs</c> 区分の <c>npc.Id</c> と
    /// 所有者別区分の所有者Id も同じ性質を持つ。<b>書いているのは形式を自己記述的にするため</b>
    /// であって、検出のためではない。ここで押さえるのは不変条件のほうである。
    /// </remarks>
    [Fact]
    public void HouseholdIdMatchesItsIndex()
    {
        var world = new World(npcCount: 2, householdCount: 5, itemCount: 0);

        for (int householdId = 0; householdId < world.Households.Length; householdId++)
        {
            Assert.Equal(householdId, world.Households[householdId].Id);
        }
    }

    /// <summary>世帯の流動資金がハッシュに乗ること(テスト4)。Households 区分の書き忘れで落ちる。</summary>
    [Fact]
    public void HashChangesWhenHouseholdFundsChange()
    {
        var world = OneHouseholdWorld();
        ulong before = StateHasher.Compute(world);

        world.Households[0].LiquidFunds = 500;
        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    /// <summary>
    /// 破産中フラグがハッシュに乗ること(テスト5)。
    /// </summary>
    /// <remarks>
    /// GDD02b §3.3 の②(順5 の値付けで原価下限を 500‰ へ下げる)と ④のゲートを駆動する状態
    /// なので、意思決定に直接関与する(TDD01 §3.8)。書き忘れると、投げ売り中の世帯と
    /// そうでない世帯が同じハッシュになる。
    /// </remarks>
    [Fact]
    public void HashChangesWhenBankruptFlagChanges()
    {
        var world = OneHouseholdWorld();
        ulong before = StateHasher.Compute(world);

        world.Households[0].IsBankrupt = 1;
        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    /// <summary>世帯の職業がハッシュに乗ること。GDD02b §4.2 ④の職業付け替えで変わる状態。</summary>
    [Fact]
    public void HashChangesWhenHouseholdOccupationChanges()
    {
        var world = OneHouseholdWorld();
        ulong before = StateHasher.Compute(world);

        world.Households[0].Occupation = Occupation.Smith;
        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    /// <summary>
    /// テスト表 #14(#96)。ToolWear(旧ToolWearCount)の書き忘れ(凍結検査だけ更新した状態)で落ちる。
    /// 改名後も書いていることを見る ── 欄名を変えるときにコピー元の行ごと消し忘れる経路。
    /// </summary>
    [Fact]
    public void HashChangesWhenToolWearChanges()
    {
        var world = OneHouseholdWorld();
        ulong before = StateHasher.Compute(world);

        world.Households[0].ToolWear = 5;
        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    /// <summary>テスト表 #12(#96)。ErrandLaborLossPermilleの書き忘れで落ちる。</summary>
    [Fact]
    public void HashChangesWhenErrandLaborLossChanges()
    {
        var world = OneHouseholdWorld();
        ulong before = StateHasher.Compute(world);

        world.Households[0].ErrandLaborLossPermille = 5;
        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    /// <summary>テスト表 #13(#96)。ProductionRunsの書き忘れで落ちる。</summary>
    [Fact]
    public void HashChangesWhenProductionRunsChange()
    {
        var world = OneHouseholdWorld();
        ulong before = StateHasher.Compute(world);

        world.Households[0].ProductionRuns = 5;
        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    /// <summary>テスト表 #25(#34)。UnmetConsumptionの1要素だけを変えると状態ハッシュが変わる。</summary>
    [Fact]
    public void HashChangesWhenUnmetConsumptionChanges()
    {
        var world = OneHouseholdWorld(itemCount: 9);
        ulong before = StateHasher.Compute(world);

        world.Households[0].UnmetConsumption[3] = 2;
        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    /// <summary>
    /// NPC の世帯Id・階層・熟練度がそれぞれ単独でハッシュに乗ること(テスト10)。
    /// Npcs 区分を Id だけのまま放置すると3ケースとも落ちる。
    /// </summary>
    [Theory]
    [InlineData("household")]
    [InlineData("rank")]
    [InlineData("skill")]
    public void HashChangesWhenNpcHouseholdOrRankOrSkillChanges(string field)
    {
        var world = new World(npcCount: 2, householdCount: 2, itemCount: 0);
        ulong before = StateHasher.Compute(world);

        switch (field)
        {
            case "household":
                world.Npcs[1].HouseholdId = 1;
                break;

            case "rank":
                world.Npcs[1].Rank = NpcRank.Apprentice;
                break;

            case "skill":
                world.Npcs[1].SkillPermille = 750;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, "未知のフィールド。");
        }

        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    /// <summary>
    /// 観測の売り手がハッシュに乗ること(テスト9)。
    /// </summary>
    /// <remarks>
    /// 欄を足したのに <c>Compute</c> へ書き足すのを忘れると、GDD02c §1.2「売り手ごとに
    /// 最新の1件」が同定できない状態が回帰テストの外に出る。
    /// </remarks>
    [Fact]
    public void HashChangesWhenObservationSellerChanges()
    {
        var world = OneHouseholdWorld();
        world.Knowledge[0].Add(new PriceObservation
        {
            ItemId = 1,
            LocationId = 2,
            Price = 30,
            SellerId = 0,
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        });
        ulong before = StateHasher.Compute(world);

        world.Knowledge[0][0] = world.Knowledge[0][0] with { SellerId = 1 };
        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void HashIgnoresEventLog()
    {
        var world = new World(npcCount: 0, householdCount: 0, itemCount: 0);
        ulong before = StateHasher.Compute(world);

        world.EventLog.Add(new DomainEvent
        {
            KindCode = 1,
            At = Tick.Zero,
            SubjectId = 0,
            RelatedId = 0,
            Payload = 999,
        });
        ulong after = StateHasher.Compute(world);

        Assert.Equal(before, after);
    }

    [Fact]
    public void HashChangesWhenMarketPriceChanges()
    {
        var world = OneHouseholdWorld();
        var key = new MarketKey(ItemId: 0, SellerId: 0);
        world.Market[key] = 10;
        ulong before = StateHasher.Compute(world);

        world.Market[key] = 20;
        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void HashChangesWhenTrustScoreChanges()
    {
        var world = new World(npcCount: 2, householdCount: 0, itemCount: 0);
        var key = new TrustKey(From: 0, To: 1);
        world.TrustLedger[key] = new TrustScore { Value = 10, LastMet = Tick.Zero };
        ulong before = StateHasher.Compute(world);

        world.TrustLedger[key] = new TrustScore { Value = 20, LastMet = Tick.Zero };
        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void HashChangesWhenNeedIsAdded()
    {
        var world = new World(npcCount: 0, householdCount: 0, itemCount: 0);
        ulong before = StateHasher.Compute(world);

        world.Needs.Add(new Need
        {
            TypeCode = 1,
            TargetHouseholdId = 0,
            ItemId = 0,
            Quantity = 1,
            Deadline = Tick.Zero,
            Urgency = 50,
            ReasonCode = 0,
        });
        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    /// <summary>Need の主体が世帯であること。TargetHouseholdId の書き忘れで落ちる。</summary>
    [Fact]
    public void HashChangesWhenNeedTargetHouseholdChanges()
    {
        var world = new World(npcCount: 0, householdCount: 0, itemCount: 0);
        world.Needs.Add(new Need
        {
            TypeCode = 1,
            TargetHouseholdId = 0,
            ItemId = 0,
            Quantity = 1,
            Deadline = Tick.Zero,
            Urgency = 50,
            ReasonCode = 0,
        });
        ulong before = StateHasher.Compute(world);

        world.Needs[0] = world.Needs[0] with { TargetHouseholdId = 1 };
        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    /// <summary>Promise.State を書き忘れると Active と Completed が同じハッシュになる。</summary>
    [Fact]
    public void HashChangesWhenPromiseStateChanges()
    {
        var world = new World(npcCount: 0, householdCount: 0, itemCount: 0);
        world.Promises.Add(new Promise
        {
            NeedIndex = 0,
            T0 = Tick.Zero,
            T1 = Tick.Zero,
            B = 10,
            State = PromiseState.Active,
        });
        ulong before = StateHasher.Compute(world);

        world.Promises[0] = world.Promises[0] with { State = PromiseState.Completed };
        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void HashChangesWhenLedgerEntryIsAdded()
    {
        var world = OneHouseholdWorld();
        ulong before = StateHasher.Compute(world);

        world.Ledgers[0].Add(new LedgerEntry
        {
            CounterpartyId = 0,
            ItemId = 0,
            Quantity = 1,
            UnitPrice = 10,
            OccurredAt = Tick.Zero,
            Terms = LedgerTerms.Cash,
            CreditDueAt = Tick.Zero,
        });
        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    /// <summary>TrustScore.LastMet(Tick)を書き忘れる落とし方の代表。</summary>
    [Fact]
    public void HashChangesWhenTrustScoreLastMetChanges()
    {
        var world = new World(npcCount: 2, householdCount: 0, itemCount: 0);
        var key = new TrustKey(From: 0, To: 1);
        world.TrustLedger[key] = new TrustScore { Value = 10, LastMet = Tick.Zero };
        ulong before = StateHasher.Compute(world);

        world.TrustLedger[key] = new TrustScore { Value = 10, LastMet = new Tick(5) };
        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    /// <summary>
    /// <see cref="StateHasher.Compute"/> が共有可変状態を持たないこと。
    /// </summary>
    /// <remarks>
    /// 現在の実装形状(<c>XxHash64</c> インスタンスもバッファもすべてローカル変数)では
    /// このテストは自明に緑である — 呼び出しごとに新しい状態から始まるので、共有状態を持たない
    /// 実装で壊れようがない。検出力を持つのは、将来誰かが速度目的などで <c>hasher</c> や
    /// <c>buffer</c> を <c>static</c> フィールドへ持ち出す退行が起きたときだけである。
    /// <b>ただし、その退行はこのテスト固有の検出力ではない。</b>
    /// 同じ退行は同一 World に2回 <c>Compute</c> する <see cref="HashIgnoresEventLog"/> も
    /// 同時に壊す(1回目の <c>Append</c> が2回目に持ち越され、EventLog を足していないのに
    /// ハッシュが変わって見える)。このテストは「安価な重複した安全網」であり、
    /// 「これが無いと検出できない壊れ方」を持つわけではない。
    /// </remarks>
    [Fact]
    public void HashIsStableWhenComputedTwiceOnTheSameWorld()
    {
        var world = new World(npcCount: 3, householdCount: 3, itemCount: 0);
        world.Households[1].LiquidFunds = 42;

        ulong first = StateHasher.Compute(world);
        ulong second = StateHasher.Compute(world);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// 【核心】<c>Compute</c> が入力を1バイトも <c>Append</c> せずに返していないこと。
    /// </summary>
    /// <remarks>
    /// <c>XxHash64</c>(seed 0)の空入力のハッシュは <see cref="XxHash64OfNoInput"/> であり
    /// 0 ではない(2026-09-04 実測)。したがって <c>Assert.NotEqual(0UL, hash)</c> では
    /// <c>Append</c> を一度も呼ばない実装を検出できない。空入力の値そのものと比較する。
    /// </remarks>
    [Fact]
    public void HashOfAnEmptyWorldDiffersFromTheHashOfNoInput()
    {
        var world = new World(npcCount: 0, householdCount: 0, itemCount: 0);

        ulong hash = StateHasher.Compute(world);

        Assert.NotEqual(XxHash64OfNoInput, hash);
    }

    // 下のテストの衝突ペア(A/B)は、区分ヘッダを外すと両者のバイト列が完全に一致するように
    // 値まで選んである。釣り合いは MarketKey と PriceObservation のフィールド構成に依存する:
    //   Market  エントリ = ItemId + SellerId + 値      = int×3            = 12バイト
    //   Knowledge の所有者1名分 = 所有者Id + 件数 + 観測×n
    //   観測1件 = ItemId + LocationId + Price + SellerId + ObservedAt + Source
    //           = int×4 + long + int = 28バイト
    //   → Market 7件(84) + 所有者1名の空 Knowledge(8) = 92
    //   → Market 0件( 0) + 所有者1名 × 観測3件(8 + 84) = 92
    // どちらかの型にフィールドが増減すると釣り合いが崩れ、ヘッダを外しても
    // このテストが緑のまま通ってしまう(静かに空虚化する)。それに気づけるよう
    // 2型のフィールド「数」を凍結する。
    //
    // 凍結できているのは数だけである。以下は素通りするため、釣り合いが崩れても落ちない:
    //   - World.Market の値型が int 以外になる(SortedDictionary<MarketKey, int> の int)。
    //     Market エントリが12バイトを超えるが、MarketKey も PriceObservation も無傷
    //   - フィールドの幅が変わる(int -> long、Tick の幅変更)。数は変わらない
    // 幅で凍結するのが本来だが、シリアライズ幅は手書きなので managed な型サイズとは
    // 一致せず、素直な検査にならない。
    private const int ExpectedMarketKeyFieldCount = 2;
    private const int ExpectedPriceObservationFieldCount = 6;

    private static int CountInstanceFields(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Length;

    /// <summary>
    /// 【核心】区分タグ・要素数の前置(ヘッダ)が実際に効いていること。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ヘッダを外すと、総バイト幅が一致する区分は要素型が違っても衝突しうる。上の算術のとおり
    /// <c>Market</c> 7件と <c>Knowledge</c>(所有者1名・観測3件)がちょうど92バイトで並ぶ。
    /// <b>値も語単位で一致するよう選んである</b> — どちらの世界も、ヘッダを外すと
    /// <c>0,3,11,0,4,12,0,5,1,0,6,13,0,7,0,0,8,14,1,0,15,0,0</c> という同じ23語になる
    /// (4バイト・リトルエンディアン。<c>ObservedAt</c> は long なので2語を占める)。
    /// </para>
    /// <para>
    /// <c>ObservedAt = 21474836480</c> は <c>5 × 2^32</c> である。<b>下位語 0・上位語 5 を
    /// 作るためであって、時刻としての意味は無い。</b>
    /// </para>
    /// <para>
    /// <c>Market</c> は <see cref="SortedDictionary{TKey,TValue}"/> なので挿入順ではなく
    /// キー昇順で書かれる。下の7件は既に昇順に並べてある。
    /// </para>
    /// <para>
    /// <b>変異の実測(2026-09-14)。</b><c>WriteSectionHeader</c> の中身を空にする変異を当てて
    /// 赤になることを確認した。<b>赤にならなければこの組は釣り合っておらず、テストは空虚である。</b>
    /// </para>
    /// <para>
    /// <b>危険なのはこのテストを変更するときではなく、<see cref="MarketKey"/> /
    /// <see cref="PriceObservation"/> を変更するときである。</b>
    /// そのときテスト自体は書き換わらないため、上のコメントは読まれない。だから
    /// フィールド数を機械的に凍結し、型が変わったらこのテスト自身が落ちるようにしてある。
    /// </para>
    /// </remarks>
    [Fact]
    public void HashDistinguishesSectionsOfEqualTotalByteWidth()
    {
        int marketKeyFieldCount = CountInstanceFields(typeof(MarketKey));
        int priceObservationFieldCount = CountInstanceFields(typeof(PriceObservation));

        Assert.True(
            marketKeyFieldCount == ExpectedMarketKeyFieldCount
                && priceObservationFieldCount == ExpectedPriceObservationFieldCount,
            "バイト幅の釣り合いが崩れた。この衝突ペアを選び直せ。"
                + $" MarketKey フィールド数={marketKeyFieldCount}(期待{ExpectedMarketKeyFieldCount}),"
                + $" PriceObservation フィールド数={priceObservationFieldCount}"
                + $"(期待{ExpectedPriceObservationFieldCount})");

        var marketOnly = new World(npcCount: 1, householdCount: 0, itemCount: 0);
        marketOnly.Market[new MarketKey(ItemId: 0, SellerId: 3)] = 11;
        marketOnly.Market[new MarketKey(ItemId: 0, SellerId: 4)] = 12;
        marketOnly.Market[new MarketKey(ItemId: 0, SellerId: 5)] = 1;
        marketOnly.Market[new MarketKey(ItemId: 0, SellerId: 6)] = 13;
        marketOnly.Market[new MarketKey(ItemId: 0, SellerId: 7)] = 0;
        marketOnly.Market[new MarketKey(ItemId: 0, SellerId: 8)] = 14;
        marketOnly.Market[new MarketKey(ItemId: 1, SellerId: 0)] = 15;

        var knowledgeOnly = new World(npcCount: 1, householdCount: 0, itemCount: 0);
        knowledgeOnly.Knowledge[0].Add(new PriceObservation
        {
            ItemId = 11,
            LocationId = 0,
            Price = 4,
            SellerId = 12,
            ObservedAt = new Tick(21474836480), // 5 × 2^32。下位語 0・上位語 5 を作るため
            Source = ObservationSource.Heard,
        });
        knowledgeOnly.Knowledge[0].Add(new PriceObservation
        {
            ItemId = 0,
            LocationId = 6,
            Price = 13,
            SellerId = 0,
            ObservedAt = new Tick(7),
            Source = ObservationSource.Direct,
        });
        knowledgeOnly.Knowledge[0].Add(new PriceObservation
        {
            ItemId = 8,
            LocationId = 14,
            Price = 1,
            SellerId = 0,
            ObservedAt = new Tick(15),
            Source = ObservationSource.Direct,
        });

        ulong marketHash = StateHasher.Compute(marketOnly);
        ulong knowledgeHash = StateHasher.Compute(knowledgeOnly);

        Assert.NotEqual(marketHash, knowledgeHash);
    }
}
