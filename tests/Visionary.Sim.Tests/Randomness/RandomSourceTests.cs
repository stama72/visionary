using Visionary.Sim.Randomness;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Randomness;

public sealed class RandomSourceTests
{
    private const long Seed = 12345;
    private static readonly Tick Day10 = Tick.FromDays(10);

    /// <summary>指定した組から先頭 n 個を取り出す。</summary>
    /// <remarks>
    /// <c>RandomSequence</c> は値コピーすると元と独立に進むため、ヘルパの中で開いて
    /// 中で使い切る。<c>ref struct</c> は戻り値にも引数にもできる(<c>Open</c> が返している)
    /// ので、「メソッドを跨げない」わけではない — 跨ぐときに <c>ref</c> を忘れるのが危険。
    /// </remarks>
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

    private const int NpcCount = 12;
    private const int ChangedNpcId = 7;

    /// <summary>スコープごとに列を開く(本実装の使い方)。NPC を Id 昇順に処理する1tickの模擬。</summary>
    private static ulong[][] RunScoped(int extraDrawsForChangedNpc)
    {
        var source = new RandomSource(Seed);
        var perNpc = new ulong[NpcCount][];

        for (int npcId = 0; npcId < NpcCount; npcId++)
        {
            int draws = 3 + (npcId == ChangedNpcId ? extraDrawsForChangedNpc : 0);
            var sequence = source.Open(RandomStream.Trade, Day10, npcId);
            perNpc[npcId] = new ulong[draws];

            for (int i = 0; i < draws; i++)
            {
                perNpc[npcId][i] = sequence.NextUInt64();
            }
        }

        return perNpc;
    }

    /// <summary>
    /// 1本の列を全 NPC で共有する(= 系統ごとに1本の長い列)。負の対照。
    /// </summary>
    /// <remarks>
    /// 比較するのは「何番目に引いた値か」ではなく「どの NPC にどの値が渡ったか」である。
    /// 共有列は位置で見れば当然同じ値を返すので、ずれるのは NPC への割り当てのほう。
    /// A/B比較で問題になるのもそちら(NPC #8 の判定が #7 の事情で変わる)。
    /// </remarks>
    private static ulong[][] RunShared(int extraDrawsForChangedNpc)
    {
        var sequence = new RandomSource(Seed).Open(RandomStream.Trade, Day10);
        var perNpc = new ulong[NpcCount][];

        for (int npcId = 0; npcId < NpcCount; npcId++)
        {
            int draws = 3 + (npcId == ChangedNpcId ? extraDrawsForChangedNpc : 0);
            perNpc[npcId] = new ulong[draws];

            for (int i = 0; i < draws; i++)
            {
                perNpc[npcId][i] = sequence.NextUInt64();
            }
        }

        return perNpc;
    }

    /// <summary>
    /// 本タスクの核心。ADR-0002 が約束した共通乱数法は、あるNPCが引く回数が変わっても
    /// 他のNPCが1ビットも動かないことに依存している。
    /// </summary>
    /// <remarks>
    /// 負の対照(共有列)を同じテストに置いているのは、この主張が自明に成立してしまう
    /// 書き方を避けるため。共有列では NPC #7 の消費が変わると後続がずれることを先に示し、
    /// そのうえでスコープ別なら崩れないことを主張する。
    /// なお、どちらの使い方を選ぶかは呼び出し側(システム)の責任であり、
    /// RandomSource だけでは強制できない。構造的な保証は W1-03 の SimContext で入れる。
    /// </remarks>
    [Fact]
    public void PerScopeSequencesIsolateConsumptionUnlikeASharedSequence()
    {
        // 負の対照: 共有列なら #7 の消費回数の変化が「#7 より後ろの NPC」に漏れる
        ulong[][] sharedBefore = RunShared(0);
        ulong[][] sharedAfter = RunShared(1);

        Assert.Equal(sharedBefore[0], sharedAfter[0]);                   // #7 より前は無傷
        Assert.NotEqual(sharedBefore[ChangedNpcId + 1], sharedAfter[ChangedNpcId + 1]);

        ulong[][] before = RunScoped(0);
        ulong[][] after = RunScoped(1);

        for (int npcId = 0; npcId < NpcCount; npcId++)
        {
            if (npcId == ChangedNpcId)
            {
                continue;
            }

            Assert.Equal(before[npcId], after[npcId]);
        }

        // #7 自身は先頭3個が一致したうえで、余分に引いた分だけ長い
        Assert.Equal(before[ChangedNpcId], after[ChangedNpcId].AsSpan(0, 3).ToArray());
        Assert.Equal(before[ChangedNpcId].Length + 1, after[ChangedNpcId].Length);
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

    /// <summary>
    /// 範囲幅が int に収まらない場合。幅を int 減算で計算すると桁あふれし、
    /// 範囲外の値が出る。単に「int の範囲に入っている」ことを主張しても
    /// 戻り値の型が int である以上つねに真なので、境界の片側を切った範囲で検証する。
    /// </summary>
    [Theory]
    [InlineData(int.MinValue, 0)]        // 幅 2^31。結果は必ず負
    [InlineData(0, int.MaxValue)]        // 結果は必ず非負
    [InlineData(-1, int.MaxValue)]       // 幅が int.MaxValue を超える
    public void NextIntHandlesRangesWiderThanInt(int min, int max)
    {
        var sequence = new RandomSource(Seed).Open(RandomStream.Production, Day10, 1);

        for (int i = 0; i < 5_000; i++)
        {
            int value = sequence.NextInt(min, max);

            Assert.InRange(value, min, max - 1);
        }
    }

    /// <summary>int 全域。両側の半分に値が出ることまで見て、分布が潰れていないことを確認する。</summary>
    [Fact]
    public void NextIntHandlesFullIntRange()
    {
        var sequence = new RandomSource(Seed).Open(RandomStream.Production, Day10, 1);
        bool sawNegative = false;
        bool sawNonNegative = false;

        for (int i = 0; i < 5_000; i++)
        {
            int value = sequence.NextInt(int.MinValue, int.MaxValue);

            Assert.NotEqual(int.MaxValue, value);

            if (value < 0)
            {
                sawNegative = true;
            }
            else
            {
                sawNonNegative = true;
            }
        }

        Assert.True(sawNegative && sawNonNegative, "int 全域の範囲で片側にしか値が出ていない");
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
    /// </summary>
    /// <remarks>
    /// <para>
    /// §3.8 が禁じた「状態ハッシュのゴールデン値」とは性質が違う — 状態ハッシュの値は
    /// 係数調整で正当に変わるが、乱数アルゴリズムは仕様であり変わってはならない。
    /// ここが落ちたら、実装ではなく「同一シードで同じ世界が再現できる」という前提が壊れている。
    /// </para>
    /// <para>
    /// <b>入力は縮退させない。</b>seed と stream の値が等しいと1段目の XOR が 0 に潰れ、
    /// tick が 0 だと2段目が恒等写像になる。その組み合わせで固定すると、
    /// 鍵導出の該当段が XOR から加算に変わっても参照ベクトルが通ってしまう
    /// (実測で確認済み)。seed ≠ stream値、tick ≠ 0、entityId ≥ 0 を満たす値を使う。
    /// </para>
    /// </remarks>
    [Fact]
    public void ReferenceVectorsAreStable()
    {
        Assert.Equal(
            new ulong[] { 0x51373479A07AFE54UL, 0x019BC503F75A2CCAUL, 0xA7EB3A93F4145868UL },
            Take(0x5EED_1234, RandomStream.Trade, Tick.FromDays(37).AddHours(9), 42, 3));
    }

    /// <summary>
    /// エンティティに紐づかない列(2引数版)を固定する。番兵が <see cref="RandomSource.NoEntity"/>
    /// から 0 に書き換わると、この列が NPC #0 の列と一致してしまう(相関バグ)。
    /// </summary>
    [Fact]
    public void EntitylessOverloadIsStableAndDistinctFromEntityZero()
    {
        Tick tick = Tick.FromDays(37).AddHours(9);
        var source = new RandomSource(0x5EED_1234);

        var entityless = source.Open(RandomStream.WorldGen, tick);
        var explicitNoEntity = source.Open(RandomStream.WorldGen, tick, RandomSource.NoEntity);
        var entityZero = source.Open(RandomStream.WorldGen, tick, 0);

        ulong first = entityless.NextUInt64();

        Assert.Equal(first, explicitNoEntity.NextUInt64());
        Assert.NotEqual(first, entityZero.NextUInt64());
        Assert.Equal(0xC5AD0200C596E36AUL, first);
    }

    /// <summary>マスターシードは long 全域を受ける。負のシードも有効。</summary>
    [Fact]
    public void NegativeMasterSeedIsSupported()
    {
        Assert.Equal(
            Take(-1, RandomStream.Trade, Day10, 3, 4),
            Take(-1, RandomStream.Trade, Day10, 3, 4));
        Assert.NotEqual(
            Take(-1, RandomStream.Trade, Day10, 3, 4),
            Take(1, RandomStream.Trade, Day10, 3, 4));
    }
}
