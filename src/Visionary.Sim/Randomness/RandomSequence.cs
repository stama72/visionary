namespace Visionary.Sim.Randomness;

/// <summary>
/// 1つの (系統, tick, エンティティ) に属する乱数列。使い捨て。
/// </summary>
/// <remarks>
/// <para>
/// <c>ref struct</c> にしているのは設計意図を型で守るため。フィールドに保持したり
/// ラムダに捕捉したりできなくなるので、「列がスコープを越えて生き延び、
/// 別の文脈から引かれる」という経路が**狭まる**。
/// </para>
/// <para>
/// <b>ただし値コピーは防げない。</b><c>ref struct</c> が禁じるのはヒープへの脱出だけで、
/// <c>var b = a;</c> も値渡しの <c>Helper(sequence)</c> も合法であり、コピーは元と独立に
/// 進む。つまり <c>Helper(seq); Helper(seq);</c> は同じ値を2回返す。C# にコピーを
/// 禁じる手段は無いため、これは残存する危険であり文書で補うしかない。
/// <b>このため、引数に取るときは必ず <c>ref</c> を付ける。</b>
/// </para>
/// <para>
/// 同様に、同じ (系統, tick, エンティティ) を2度開くと同じ値列が返る。
/// 「1つの組につき1本だけ開く」も型では守れない規約である(TDD01 §3.1)。
/// </para>
/// </remarks>
public ref struct RandomSequence
{
    private ulong _state;

    internal RandomSequence(ulong key) => _state = key;

    /// <summary>次の64bit値。</summary>
    public ulong NextUInt64()
    {
        unchecked
        {
            _state += SplitMix64.Golden;
        }

        return SplitMix64.Mix(_state);
    }

    /// <summary>
    /// <paramref name="minInclusive"/> 以上 <paramref name="maxExclusive"/> 未満の一様乱数。
    /// </summary>
    /// <remarks>
    /// 単純な剰余は範囲が2の冪でないときに小さい値へ偏る。2^64 を範囲で割った余り未満の値を
    /// 捨てて引き直すことで偏りを除く。棄却は決定論的(列の続きを引くだけ)なので再現性は保たれる。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">範囲が空のとき。</exception>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxExclusive), maxExclusive, "乱数の範囲が空。max は min より大きい必要がある。");
        }

        // int の全域(min = int.MinValue, max = int.MaxValue)でも桁あふれしないよう long で引く
        ulong range = (ulong)((long)maxExclusive - minInclusive);

        // (0 - range) % range は 2^64 % range に等しい。これ未満の値が偏りの原因になる
        ulong reject = unchecked(0UL - range) % range;

        ulong value;
        do
        {
            value = NextUInt64();
        }
        while (value < reject);

        return (int)(minInclusive + (long)(value % range));
    }

    /// <summary>千分率(‰)で指定した確率で true を返す。</summary>
    /// <exception cref="ArgumentOutOfRangeException">‰ が 0〜1000 の外のとき。</exception>
    public bool NextBool(int trueProbabilityPermille)
    {
        if (trueProbabilityPermille is < 0 or > Numerics.IntegerMath.PermilleScale)
        {
            throw new ArgumentOutOfRangeException(
                nameof(trueProbabilityPermille),
                trueProbabilityPermille,
                $"確率は0〜{Numerics.IntegerMath.PermilleScale}‰。");
        }

        // 0‰ なら常に false、1000‰ なら常に true になる比較にする
        return NextInt(0, Numerics.IntegerMath.PermilleScale) < trueProbabilityPermille;
    }
}
