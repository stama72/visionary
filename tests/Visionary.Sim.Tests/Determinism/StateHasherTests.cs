using Visionary.Sim.Determinism;
using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Determinism;

/// <summary>
/// <see cref="StateHasher"/> の回帰テスト(TDD01 §3.8)。合成システム(<c>SyntheticLoadSystem</c> /
/// <c>SyntheticDecaySystem</c>)は <c>Visionary.Sim.Runner</c> 側にあるため、
/// ここでは触らず <see cref="World"/> を直接組み立てる(docs/tasks/W1-04-determinism-hash.md)。
/// </summary>
public sealed class StateHasherTests
{
    [Fact]
    public void HashChangesWhenClockAdvances()
    {
        var world = new World(npcCount: 0);
        ulong before = StateHasher.Compute(world);

        // World.Now は internal set。公開APIで時刻を進めるため SimScheduler を素通しで使う。
        var scheduler = new SimScheduler(Array.Empty<ISimSystem>(), new RandomSource(1));
        scheduler.Advance(world, ticks: 1);

        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    /// <summary>
    /// 【核心】Npcs の走査が順序依存であること。順序非依存の畳み込み(XOR・加算)に変えると、
    /// 2つのNPCの値を入れ替えても合計・XORは変わらないため、このテストが緑のまま壊れを見逃す。
    /// </summary>
    [Fact]
    public void HashChangesWhenTwoNpcsSwapTheirFunds()
    {
        var world = new World(npcCount: 6);
        world.Npcs[3].LiquidFunds = 100;
        world.Npcs[5].LiquidFunds = 200;
        ulong before = StateHasher.Compute(world);

        world.Npcs[3].LiquidFunds = 200;
        world.Npcs[5].LiquidFunds = 100;
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
        var world = new World(npcCount: 0);
        world.Knowledge.Add(new PriceObservation
        {
            ItemId = 1,
            LocationId = 0,
            Price = 10,
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        });
        world.Knowledge.Add(new PriceObservation
        {
            ItemId = 2,
            LocationId = 0,
            Price = 20,
            ObservedAt = Tick.Zero,
            Source = ObservationSource.Direct,
        });
        ulong before = StateHasher.Compute(world);

        (world.Knowledge[0], world.Knowledge[1]) = (world.Knowledge[1], world.Knowledge[0]);
        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void HashIgnoresEventLog()
    {
        var world = new World(npcCount: 0);
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
        var world = new World(npcCount: 1);
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
        var world = new World(npcCount: 2);
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
        var world = new World(npcCount: 0);
        ulong before = StateHasher.Compute(world);

        world.Needs.Add(new Need
        {
            TypeCode = 1,
            TargetNpcId = 0,
            ItemId = 0,
            Quantity = 1,
            Deadline = Tick.Zero,
            Urgency = 50,
            ReasonCode = 0,
        });
        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    /// <summary>Promise.State を書き忘れると Active と Completed が同じハッシュになる。</summary>
    [Fact]
    public void HashChangesWhenPromiseStateChanges()
    {
        var world = new World(npcCount: 0);
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
        var world = new World(npcCount: 0);
        ulong before = StateHasher.Compute(world);

        world.Ledgers.Add(new LedgerEntry
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
        var world = new World(npcCount: 2);
        var key = new TrustKey(From: 0, To: 1);
        world.TrustLedger[key] = new TrustScore { Value = 10, LastMet = Tick.Zero };
        ulong before = StateHasher.Compute(world);

        world.TrustLedger[key] = new TrustScore { Value = 10, LastMet = new Tick(5) };
        ulong after = StateHasher.Compute(world);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void HashIsStableWhenComputedTwiceOnTheSameWorld()
    {
        var world = new World(npcCount: 3);
        world.Npcs[1].LiquidFunds = 42;

        ulong first = StateHasher.Compute(world);
        ulong second = StateHasher.Compute(world);

        Assert.Equal(first, second);
    }

    /// <summary>【核心】Append を素通りして定数を返すと、テスト11(安定性)が常に緑になり空虚化する。</summary>
    [Fact]
    public void HashIsNotZeroForAPopulatedWorld()
    {
        var world = new World(npcCount: 3);
        world.Npcs[0].LiquidFunds = 100;
        world.Market[new MarketKey(0, 0)] = 5;

        ulong hash = StateHasher.Compute(world);

        Assert.NotEqual(0UL, hash);
    }
}
