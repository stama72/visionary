namespace Visionary.Sim;

/// <summary>
/// <see cref="World.Market"/> の疎キー。品目と<b>売り手世帯</b>の組(TDD01 §3.2)。
/// </summary>
/// <remarks>
/// フィールドは int のみ。<see cref="SortedDictionary{TKey,TValue}"/> のキーとして
/// 列挙順を確定させるため <see cref="IComparable{T}"/> を実装する(ADR-0002)。
/// </remarks>
/// <param name="ItemId">品目 Id(GDD02 §2.2)。</param>
/// <param name="SellerId">
/// 売り手の<b>世帯</b> Id。都市外市場の売り注文はここに実体化しない(GDD02 §10.2)ので、
/// <see cref="HouseholdState.ExternalMarketSellerId"/> がこのキーに現れることはない。
/// </param>
public readonly record struct MarketKey(int ItemId, int SellerId) : IComparable<MarketKey>
{
    public int CompareTo(MarketKey other)
    {
        int byItem = ItemId.CompareTo(other.ItemId);

        return byItem != 0 ? byItem : SellerId.CompareTo(other.SellerId);
    }
}
