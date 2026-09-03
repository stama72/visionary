using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;
using static Visionary.Sim.Tests.Systems.TestSimSystems;

namespace Visionary.Sim.Tests.Systems;

public sealed class SimSchedulerTests
{
    [Fact]
    public void SystemsRunInRegisteredOrderWithinATick()
    {
        var log = new List<string>();
        var systems = new ISimSystem[]
        {
            new OrderRecordingSystem("A", RandomStream.Production, Cadence.EveryTick(), log),
            new OrderRecordingSystem("B", RandomStream.Consumption, Cadence.EveryTick(), log),
            new OrderRecordingSystem("C", RandomStream.Trade, Cadence.EveryTick(), log),
        };
        var scheduler = new SimScheduler(systems, new RandomSource(1));
        var world = new World(npcCount: 0);

        scheduler.Advance(world, ticks: 1);

        Assert.Equal(new[] { "A", "B", "C" }, log);
    }

    [Fact]
    public void AdvanceUpdatesClockBeforeRunningSystems()
    {
        var recorder = new RecordingSystem(RandomStream.Production, Cadence.EveryTick());
        var scheduler = new SimScheduler(new ISimSystem[] { recorder }, new RandomSource(1));
        var world = new World(npcCount: 0);

        scheduler.Advance(world, ticks: 3);

        // 現在tickを処理してから次へ進めるので、エポック(Tick.Zero = 1年 春1日 0時)から
        // 処理が始まる。先に進めてから処理する実装では春1日が永久に飛ばされ、
        // Daily(hour: 0) のシステムが春2日から始まってしまう。
        Assert.Equal(new[] { Tick.Zero, new Tick(1), new Tick(2) }, recorder.RunAtWorldNow);
        Assert.Equal(recorder.RunAtWorldNow, recorder.RunAtContextNow);
        Assert.Equal(new Tick(3), world.Now);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AdvanceRejectsNonPositiveTicks(int ticks)
    {
        var scheduler = new SimScheduler(Array.Empty<ISimSystem>(), new RandomSource(1));
        var world = new World(npcCount: 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Advance(world, ticks));
    }

    [Fact]
    public void DuplicateRandomStreamIsRejected()
    {
        var systems = new ISimSystem[]
        {
            new RecordingSystem(RandomStream.Trade, Cadence.EveryTick()),
            new RecordingSystem(RandomStream.Trade, Cadence.Daily(hour: 3)),
        };

        Assert.Throws<ArgumentException>(() => new SimScheduler(systems, new RandomSource(1)));
    }

    /// <summary>
    /// 落ちるべき条件の核心その1。あるシステムの OpenRandom が、そのシステム自身の
    /// Stream で開いた列(= RandomSourceを直接呼んだ場合と同じ値)を返すことを示す。
    /// </summary>
    [Fact]
    public void SystemReceivesOnlyItsOwnRandomStream()
    {
        const long seed = 999;
        var tradeSystem = new CapturingSystem(RandomStream.Trade, Cadence.EveryTick(), entityId: 1);
        // 同じ entityId を与える。系統が違えば別の組なので、これは正当な使い方であり、
        // かつ「系統が違えば別の値が返る」ことを最も鋭く示す形になる。
        // entityId をずらすと、二重オープン検出のキーの誤りを見逃す。
        var trustSystem = new CapturingSystem(RandomStream.Trust, Cadence.EveryTick(), entityId: 1);
        var scheduler = new SimScheduler(
            new ISimSystem[] { tradeSystem, trustSystem }, new RandomSource(seed));
        var world = new World(npcCount: 0);

        scheduler.Advance(world, ticks: 1);

        var expectedTick = Tick.Zero;
        ulong expectedTrade = new RandomSource(seed).Open(RandomStream.Trade, expectedTick, 1).NextUInt64();
        ulong expectedTrust = new RandomSource(seed).Open(RandomStream.Trust, expectedTick, 1).NextUInt64();

        Assert.NotEqual(expectedTrade, expectedTrust);

        Assert.Equal(expectedTrade, Assert.Single(tradeSystem.Values));
        Assert.Equal(expectedTrust, Assert.Single(trustSystem.Values));
    }

    [Fact]
    public void DoubleOpenForSameEntityInSameTickThrows()
    {
        var system = new DoubleOpeningSystem(RandomStream.Trade, Cadence.EveryTick(), entityId: 5);
        var scheduler = new SimScheduler(new ISimSystem[] { system }, new RandomSource(1));
        var world = new World(npcCount: 0);

        Assert.Throws<InvalidOperationException>(() => scheduler.Advance(world, ticks: 1));
    }

    [Fact]
    public void OpenRandomIsAllowedAgainOnTheNextTick()
    {
        var system = new CapturingSystem(RandomStream.Trade, Cadence.EveryTick(), entityId: 5);
        var scheduler = new SimScheduler(new ISimSystem[] { system }, new RandomSource(1));
        var world = new World(npcCount: 0);

        scheduler.Advance(world, ticks: 2);

        Assert.Equal(2, system.Values.Count);
        Assert.NotEqual(system.Values[0], system.Values[1]);
    }

    /// <summary>
    /// 落ちるべき条件の核心その2。器(World/SimScheduler/SimContext)だけの段階でも、
    /// 同一シードなら同一の世界になる。
    /// </summary>
    [Fact]
    public void SameSeedProducesIdenticalWorldAfterAdvance()
    {
        const long seed = 20260830;
        const int npcCount = 12;
        const int ticks = 50;

        (int Id, int LiquidFunds)[] RunOnce()
        {
            var world = new World(npcCount);
            var system = new MutatingSystem(RandomStream.Trade, Cadence.EveryTick());
            var scheduler = new SimScheduler(new ISimSystem[] { system }, new RandomSource(seed));

            scheduler.Advance(world, ticks);

            return world.Npcs.Select(npc => (npc.Id, npc.LiquidFunds)).ToArray();
        }

        Assert.Equal(RunOnce(), RunOnce());
    }

    [Fact]
    public void NpcsAreIndexedByAscendingId()
    {
        var world = new World(npcCount: 10);

        for (int i = 0; i < world.Npcs.Length; i++)
        {
            Assert.Equal(i, world.Npcs[i].Id);
        }
    }

    [Fact]
    public void WorldCollectionsAreDeterministicallyOrdered()
    {
        var world = new World(npcCount: 0);

        // 挿入順をキーの昇順とは逆にする。SortedDictionary なら列挙は常に昇順になる
        // (Dictionary に差し替えると、この主張は成立しなくなる)。
        world.Market[new MarketKey(ItemId: 2, SellerId: 5)] = 10;
        world.Market[new MarketKey(ItemId: 1, SellerId: 9)] = 20;
        world.Market[new MarketKey(ItemId: 1, SellerId: 3)] = 30;

        Assert.Equal(
            new[] { new MarketKey(1, 3), new MarketKey(1, 9), new MarketKey(2, 5) },
            world.Market.Keys.ToArray());

        world.TrustLedger[new TrustKey(From: 2, To: 1)] =
            new TrustScore { Value = 10, LastMet = Tick.Zero };
        world.TrustLedger[new TrustKey(From: 1, To: 9)] =
            new TrustScore { Value = 20, LastMet = Tick.Zero };
        world.TrustLedger[new TrustKey(From: 1, To: 3)] =
            new TrustScore { Value = 30, LastMet = Tick.Zero };

        Assert.Equal(
            new[] { new TrustKey(1, 3), new TrustKey(1, 9), new TrustKey(2, 1) },
            world.TrustLedger.Keys.ToArray());
    }

    /// <summary>
    /// <see cref="SimContext"/> を退避して <see cref="ISimSystem.Step"/> の外から使うと例外になる。
    /// </summary>
    /// <remarks>
    /// 【核心】この守りが無いと、直前に走ったシステムの系統が <c>CurrentStream</c> に
    /// 残ったままなので、退避した context から**別系統の列を静かに開けてしまう**。
    /// TDD01 §3.1 が「A/B比較の差分に現れず発見の手がかりが無い」と書いている壊れ方そのもの。
    ///
    /// 落ちるべき条件: <c>SimScheduler</c> の <c>finally</c> の <c>ClearCurrentStream()</c> を消す /
    /// <c>SimContext.OpenRandom</c> の <c>Enum.IsDefined</c> ガードを消す。
    /// (どちらの変異でもこのテストが落ちることを実測で確認済み)
    /// </remarks>
    [Fact]
    public void OpenRandomOutsideStepThrows()
    {
        var stashing = new TestSimSystems.ContextStashingSystem();
        var scheduler = new SimScheduler(new ISimSystem[] { stashing }, new RandomSource(1));

        scheduler.Advance(new World(npcCount: 0), ticks: 1);

        Assert.NotNull(stashing.Stashed);
        SimContext context = stashing.Stashed!;

        Assert.Throws<InvalidOperationException>(() =>
        {
            var unused = context.OpenRandom(0);
            return unused.NextUInt64();
        });
    }

    /// <summary>
    /// <see cref="Cadence"/> を初期化し忘れたシステムは構築時に弾かれる。
    /// </summary>
    /// <remarks>
    /// 既定値をいずれかの周期に割り当てると黙ってその周期で走る。実行周期は乱数の鍵に
    /// 入る(TDD01 §3.1)ので、周期の取り違えは A/B 比較の前提そのものを壊す。
    /// 構築時に弾くのは、同一tickの他システムが既に走った後で落ちるのを避けるため。
    ///
    /// 落ちるべき条件: <c>SimScheduler</c> のコンストラクタから <c>IsSet</c> 検査を消す。
    /// </remarks>
    [Fact]
    public void UnsetCadenceIsRejectedAtConstruction()
    {
        Assert.Throws<ArgumentException>(() =>
            new SimScheduler(
                new ISimSystem[] { new TestSimSystems.UnsetCadenceSystem() }, new RandomSource(1)));
    }
}
