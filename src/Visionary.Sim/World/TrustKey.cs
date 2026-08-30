namespace Visionary.Sim;

/// <summary>
/// <see cref="World.TrustLedger"/> の疎キー。信用を寄せる側と寄せられる側の組(GDD01 §2.1)。
/// </summary>
/// <remarks>
/// フィールドは int のみ。<see cref="SortedDictionary{TKey,TValue}"/> のキーとして
/// 列挙順を確定させるため <see cref="IComparable{T}"/> を実装する(ADR-0002)。
/// </remarks>
public readonly record struct TrustKey(int From, int To) : IComparable<TrustKey>
{
    public int CompareTo(TrustKey other)
    {
        int byFrom = From.CompareTo(other.From);

        return byFrom != 0 ? byFrom : To.CompareTo(other.To);
    }
}
