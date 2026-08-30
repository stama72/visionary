namespace Visionary.Sim.Randomness;

/// <summary>
/// SplitMix64。カウンタを進めて撹拌するだけの生成器で、状態が 64bit ひとつに収まる。
/// </summary>
/// <remarks>
/// 自前で持つ理由は2つ。<c>System.Random</c> が禁止銘柄であること(ADR-0002)と、
/// .NET の乱数実装はバージョン間で変わった前例があり、ランタイムに依存しない再現性を
/// 保証できないこと。アルゴリズムを固定して初めて「同一シード → 同一結果」が
/// 将来のランタイム更新をまたいで成立する。
/// </remarks>
internal static class SplitMix64
{
    /// <summary>黄金比由来の増分。カウンタをこの幅で進める。</summary>
    internal const ulong Golden = 0x9E3779B97F4A7C15UL;

    /// <summary>SplitMix64 の finalizer。桁あふれは意図した挙動なので unchecked。</summary>
    internal static ulong Mix(ulong z)
    {
        unchecked
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
