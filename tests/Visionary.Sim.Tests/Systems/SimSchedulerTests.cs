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

        // Advanceは世界のNowを各tickの処理の前に更新するので、Stepから見えるworld.Nowは
        // 「これから処理するtick」自身であり、1つ前のtickではない。
        Assert.Equal(new[] { new Tick(1), new Tick(2), new Tick(3) }, recorder.RunAtWorldNow);
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
    /// 緑にすべきテストの核心その1。あるシステムの OpenRandom が、そのシステム自身の
    /// Stream で開いた列(= RandomSourceを直接呼んだ場合と同じ値)を返すことを示す。
    /// </summary>
    [Fact]
    public void SystemReceivesOnlyItsOwnRandomStream()
    {
        const long seed = 999;
        var tradeSystem = new CapturingSystem(RandomStream.Trade, Cadence.EveryTick(), entityId: 1);
        var trustSystem = new CapturingSystem(RandomStream.Trust, Cadence.EveryTick(), entityId: 2);
        var scheduler = new SimScheduler(
            new ISimSystem[] { tradeSystem, trustSystem }, new RandomSource(seed));
        var world = new World(npcCount: 0);

        scheduler.Advance(world, ticks: 1);

        var expectedTick = new Tick(1);
        ulong expectedTrade = new RandomSource(seed).Open(RandomStream.Trade, expectedTick, 1).NextUInt64();
        ulong expectedTrust = new RandomSource(seed).Open(RandomStream.Trust, expectedTick, 2).NextUInt64();

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
    /// 緑にすべきテストの核心その2。器(World/SimScheduler/SimContext)だけの段階でも、
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
}
