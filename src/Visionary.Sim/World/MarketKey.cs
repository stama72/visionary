namespace Visionary.Sim;

/// <summary>
/// <see cref="World.Market"/> の疎キー。品目と売り手の組(TDD01 §3.2)。
/// </summary>
/// <remarks>
/// フィールドは int のみ。<see cref="SortedDictionary{TKey,TValue}"/> のキーとして
/// 列挙順を確定させるため <see cref="IComparable{T}"/> を実装する(ADR-0002)。
/// </remarks>
public readonly record struct MarketKey(int ItemId, int SellerId) : IComparable<MarketKey>
{
    public int CompareTo(MarketKey other)
    {
        int byItem = ItemId.CompareTo(other.ItemId);

        return byItem != 0 ? byItem : SellerId.CompareTo(other.SellerId);
    }
}
