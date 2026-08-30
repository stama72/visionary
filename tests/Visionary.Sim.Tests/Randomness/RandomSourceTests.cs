using Visionary.Sim.Randomness;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Randomness;

public sealed class RandomSourceTests
{
    private const long Seed = 12345;
    private static readonly Tick Day10 = Tick.FromDays(10);

    /// <summary>指定した組から先頭 n 個を取り出す(ref struct はメソッドを跨げないため都度開く)。</summary>
    private static ulong[] Take(
        long seed, RandomStream stream, Tick tick, int entityId, int count)
    {
        var sequence = new RandomSource(seed).Open(stream, tick, entityId);
        var values = new ulong[count];

        for (int i = 0; i < count; i++)
        {
            values[i] = sequence.NextUInt64();
        }

        return values;
    }

    [Fact]
    public void SameScopeProducesSameSequence()
    {
        Assert.Equal(
            Take(Seed, RandomStream.Trade, Day10, 7, 16),
            Take(Seed, RandomStream.Trade, Day10, 7, 16));
    }

    [Fact]
    public void DifferentStreamsProduceDifferentSequences()
    {
        Assert.NotEqual(
            Take(Seed, RandomStream.Trade, Day10, 7, 16),
            Take(Seed, RandomStream.Trust, Day10, 7, 16));
    }

    [Fact]
    public void DifferentEntitiesProduceDifferentSequences()
    {
        Assert.NotEqual(
            Take(Seed, RandomStream.Trade, Day10, 7, 16),
            Take(Seed, RandomStream.Trade, Day10, 8, 16));
    }

    [Fact]
    public void DifferentTicksProduceDifferentSequences()
    {
        Assert.NotEqual(
            Take(Seed, RandomStream.Trade, Day10, 7, 16),
            Take(Seed, RandomStream.Trade, Tick.FromDays(11), 7, 16));
    }

    [Fact]
    public void DifferentMasterSeedsProduceDifferentSequences()
    {
        Assert.NotEqual(
            Take(Seed, RandomStream.Trade, Day10, 7, 16),
            Take(Seed + 1, RandomStream.Trade, Day10, 7, 16));
    }

    /// <summary>
    /// 本タスクの核心。ADR-0002 が約束した共通乱数法は、ある組で引く回数が変わっても
    /// 他の組が1ビットも動かないことに依存している。系統ごとに1本の長い列を持つ実装では
    /// この性質が成立せず、信用あり/なしの比較が「別世界同士の比較」になる。
    /// </summary>
    [Fact]
    public void ConsumptionInOneScopeDoesNotAffectAnother()
    {
        ulong[] baseline = Take(Seed, RandomStream.Trust, Day10, 7, 8);

        // 別の組(別NPC・別系統・別tick)から余分に引いてから、同じ組を開き直す
        var noisyEntity = new RandomSource(Seed).Open(RandomStream.Trust, Day10, 8);
        var noisyStream = new RandomSource(Seed).Open(RandomStream.Trade, Day10, 7);
        var noisyTick = new RandomSource(Seed).Open(RandomStream.Trust, Tick.FromDays(11), 7);

        for (int i = 0; i < 100; i++)
        {
            noisyEntity.NextUInt64();
            noisyStream.NextUInt64();
            noisyTick.NextUInt64();
        }

        Assert.Equal(baseline, Take(Seed, RandomStream.Trust, Day10, 7, 8));
    }

    [Fact]
    public void NextIntStaysWithinRange()
    {
        var sequence = new RandomSource(Seed).Open(RandomStream.Production, Day10, 1);

        for (int i = 0; i < 10_000; i++)
        {
            int value = sequence.NextInt(-5, 7);

            Assert.InRange(value, -5, 6);
        }
    }

    [Fact]
    public void NextIntCoversWholeRange()
    {
        var sequence = new RandomSource(Seed).Open(RandomStream.Production, Day10, 1);
        var seen = new bool[6];

        for (int i = 0; i < 10_000; i++)
        {
            seen[sequence.NextInt(0, 6)] = true;
        }

        Assert.DoesNotContain(false, seen);
    }

    [Fact]
    public void NextIntWithSingleValueRangeReturnsThatValue()
    {
        var sequence = new RandomSource(Seed).Open(RandomStream.Production, Day10, 1);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(5, sequence.NextInt(5, 6));
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(5, 4)]
    public void NextIntThrowsWhenRangeIsEmpty(int min, int max)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            var sequence = new RandomSource(Seed).Open(RandomStream.Production, Day10, 1);
            return sequence.NextInt(min, max);
        });
    }

    /// <summary>範囲幅が int に収まらない場合。long で計算していないと桁あふれする。</summary>
    [Fact]
    public void NextIntHandlesFullIntRange()
    {
        var sequence = new RandomSource(Seed).Open(RandomStream.Production, Day10, 1);

        for (int i = 0; i < 1_000; i++)
        {
            int value = sequence.NextInt(int.MinValue, int.MaxValue);

            Assert.InRange(value, int.MinValue, int.MaxValue - 1);
        }
    }

    [Fact]
    public void NextBoolIsDeterministicAtBounds()
    {
        var sequence = new RandomSource(Seed).Open(RandomStream.Dialogue, Day10, 1);

        for (int i = 0; i < 500; i++)
        {
            Assert.False(sequence.NextBool(0));
            Assert.True(sequence.NextBool(1000));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1001)]
    public void NextBoolThrowsOnOutOfRangePermille(int permille)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            var sequence = new RandomSource(Seed).Open(RandomStream.Dialogue, Day10, 1);
            return sequence.NextBool(permille);
        });
    }

    /// <summary>
    /// アルゴリズムの黙った変更を検出する参照ベクトル。
    /// §3.8 が禁じた「状態ハッシュのゴールデン値」とは性質が違う — 状態ハッシュの値は
    /// 係数調整で正当に変わるが、乱数アルゴリズムは仕様であり変わってはならない。
    /// ここが落ちたら、実装ではなく「同一シードで同じ世界が再現できる」という前提が壊れている。
    /// </summary>
    [Fact]
    public void ReferenceVectorsAreStable()
    {
        ulong[] actual = Take(1, RandomStream.WorldGen, Tick.Zero, RandomSource.NoEntity, 4);

        Assert.Equal(
            new ulong[]
            {
                0xA577782BC52A9F5AUL,
                0xB485244380E590BEUL,
                0x5176985D86CFF511UL,
                0x07508E53B617B6C7UL,
            },
            actual);
    }
}
