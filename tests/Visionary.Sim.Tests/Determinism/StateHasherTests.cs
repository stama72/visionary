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
        var world = new World(npcCount: 3);
        world.Npcs[1].LiquidFunds = 42;

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
        var world = new World(npcCount: 0);

        ulong hash = StateHasher.Compute(world);

        Assert.NotEqual(XxHash64OfNoInput, hash);
    }

    // MarketKey(int×2) + 値(int) = 12バイト × 2件 と PriceObservation(int×3 + Tick + enum) =
    // 24バイト × 1件 が釣り合うことで、下のテストの衝突ペア(A/B)が成立している。
    // どちらかの型にフィールドが増減すると釣り合いが崩れ、ヘッダを外しても
    // このテストが緑のまま通ってしまう(静かに空虚化する)。型を変更したときに気づけるよう、
    // 釣り合いの前提そのものをここで凍結する。
    private const int ExpectedMarketKeyFieldCount = 2;
    private const int ExpectedPriceObservationFieldCount = 5;

    private static int CountInstanceFields(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Length;

    /// <summary>
    /// 【核心】区画タグ・要素数の前置(ヘッダ)が実際に効いていること。
    /// </summary>
    /// <remarks>
    /// ヘッダを外すと、総バイト幅が一致する区画は要素型が違っても衝突しうる。
    /// <c>Market</c> エントリは int×3 = 12バイト、<c>Knowledge</c> エントリは
    /// int×3 + long + int = 24バイトなので、<c>Market</c> 2件と <c>Knowledge</c> 1件が
    /// ちょうど32バイトで一致する。この2つの値の組は恣意的ではなく、ヘッダ無しでバイト列が
    /// 一致するよう選んである(2026-09-04 実測)。
    /// <b>危険なのはこのテストを変更するときではなく、<see cref="MarketKey"/> /
    /// <see cref="PriceObservation"/> を変更するときである。</b>
    /// そのときテスト自体は書き換わらないため、上のコメントは読まれない。だから
    /// フィールド数を機械的に凍結し、型が変わったらこのテスト自身が落ちるようにしてある。
    /// </remarks>
    [Fact]
    public void HashDistinguishesSectionsOfEqualTotalByteWidth()
    {
        int marketKeyFieldCount = CountInstanceFields(typeof(MarketKey));
        int priceObservationFieldCount = CountInstanceFields(typeof(PriceObservation));

        Assert.True(
            marketKeyFieldCount == ExpectedMarketKeyFieldCount
                && priceObservationFieldCount == ExpectedPriceObservationFieldCount,
            "バイト幅の釣り合いが崩れた。テスト13 の World の組を選び直せ。"
                + $" MarketKey フィールド数={marketKeyFieldCount}(期待{ExpectedMarketKeyFieldCount}),"
                + $" PriceObservation フィールド数={priceObservationFieldCount}"
                + $"(期待{ExpectedPriceObservationFieldCount})");

        var marketOnly = new World(npcCount: 0);
        marketOnly.Market[new MarketKey(ItemId: 1, SellerId: 2)] = 3;
        marketOnly.Market[new MarketKey(ItemId: 5, SellerId: 0)] = 0;

        var knowledgeOnly = new World(npcCount: 0);
        knowledgeOnly.Knowledge.Add(new PriceObservation
        {
            ItemId = 1,
            LocationId = 2,
            Price = 3,
            ObservedAt = new Tick(5),
            Source = ObservationSource.Direct,
        });

        ulong marketHash = StateHasher.Compute(marketOnly);
        ulong knowledgeHash = StateHasher.Compute(knowledgeOnly);

        Assert.NotEqual(marketHash, knowledgeHash);
    }
}
