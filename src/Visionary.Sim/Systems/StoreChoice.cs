namespace Visionary.Sim.Systems;

/// <summary>選ばれうる店1件(GDD06 §3)。</summary>
public readonly record struct StoreCandidate
{
    /// <summary>売り手の世帯 Id。</summary>
    public int SellerId { get; init; }

    /// <summary>売り手の区画 Id。</summary>
    public int DistrictId { get; init; }

    /// <summary>
    /// 実効価格。単位: 貨幣/1単位。<b>移動は乗らない</b>(外出ごとの固定費であり、
    /// <see cref="Errand.Cost"/> が別に数える。GDD06 §2)。
    /// </summary>
    public int UnitEffectivePrice { get; init; }
}

/// <summary>
/// 店の選択(GDD06 §3)。<b>World を読むだけで書かない。</b>
/// </summary>
/// <remarks>
/// <b>R はここに現れない。</b>距離1の店を「知っている」ことは、そこで買えることを意味しない
/// (本タスクの決定)。<b>購入できるかは知識ではなく、その区画に居るか</b>(自区画または
/// <see cref="ErrandPlan.VisitedDistrictIds"/>)で決まる。旧実装は R 以内の店から移動費を
/// 払わずに買えていた ── 空間の摩擦が購入の側から抜けていた経路である。
/// </remarks>
public sealed class StoreChoice
{
    private readonly WorldDefinition _definition;

    public StoreChoice(WorldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _definition = definition;
    }

    /// <summary>
    /// 自区画と行った区画の店のうち、実効価格が最小のものを選ぶ(GDD06 §3)。候補0件ならfalse。
    /// </summary>
    /// <remarks>
    /// <b>走査は <see cref="World.Market"/> の列挙順そのまま。</b><see cref="MarketKey"/> は
    /// ItemId → SellerId の昇順(<c>SortedDictionary</c>)なので、<c>&lt;</c> で更新すれば
    /// 同値のとき売り手 Id 最小が自然に残る(ADR-0002 / GDD06 §3)。
    /// </remarks>
    public bool TrySelect(
        World world,
        HouseholdState buyer,
        int itemId,
        IReadOnlyList<int> visitedDistrictIds,
        out StoreCandidate selected)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(buyer);
        ArgumentNullException.ThrowIfNull(visitedDistrictIds);

        bool found = false;
        var best = default(StoreCandidate);
        int bestPrice = 0;

        foreach (var (key, price) in world.Market)
        {
            if (key.ItemId != itemId)
            {
                continue;
            }

            int sellerId = key.SellerId;

            // 自分から自分へは売買しない(資金が動かないまま在庫と帳簿だけが増える経路を断つ)。
            if (sellerId == buyer.Id)
            {
                continue;
            }

            var seller = world.Households[sellerId];

            // 先に来た買い手が買い尽くした店・留保しか残っていない店は候補に入らない
            // (GDD06 §3 / GDD02c §1.3。5経路すべてを SellableStock に通す、W2-14)。
            if (SellableStock.Of(_definition, seller, itemId) <= 0)
            {
                continue;
            }

            bool isInReach =
                seller.DistrictId == buyer.DistrictId || ContainsDistrict(visitedDistrictIds, seller.DistrictId);

            if (!isInReach)
            {
                continue;
            }

            int effectivePrice = EffectivePrice.Calculate(price, trust: 0, _definition.TrustDiscountPermille);

            if (!found || effectivePrice < bestPrice)
            {
                found = true;
                bestPrice = effectivePrice;
                best = new StoreCandidate
                {
                    SellerId = sellerId,
                    DistrictId = seller.DistrictId,
                    UnitEffectivePrice = effectivePrice,
                };
            }
        }

        // world.Marketの走査が終わったあとに窓口を1件だけ足す(GDD02d §2.1・§2.2)。
        // 走査の後ろに置いたうえで`<`を使うことが「同値なら都市内の売り手が勝つ」の実体である
        // ── `<=`にしない・走査の前に置かない。窓口は無限在庫(在庫の条件を適用しない)・
        // 世帯ではない(world.Households[sellerId]を引く経路も自分から自分への除外も通らない)。
        if (ExternalMarket.TryOfferPrice(_definition, world.Now, itemId, out int windowOfferPrice)
            && ExternalMarket.IsWithinReach(buyer.DistrictId, visitedDistrictIds))
        {
            int windowEffectivePrice = EffectivePrice.Calculate(
                windowOfferPrice, trust: 0, _definition.TrustDiscountPermille);

            if (!found || windowEffectivePrice < bestPrice)
            {
                found = true;
                bestPrice = windowEffectivePrice;
                best = new StoreCandidate
                {
                    SellerId = HouseholdState.ExternalMarketSellerId,
                    DistrictId = District.ExternalMarketDistrictId,
                    UnitEffectivePrice = windowEffectivePrice,
                };
            }
        }

        selected = best;
        return found;
    }

    /// <summary>
    /// 線形探索での包含判定(GDD06 §3「Dictionary/HashSetを新しく持ち込まない」)。
    /// 高々8件(区画数9 − 自区画1)なので、線形探索でよい。
    /// </summary>
    private static bool ContainsDistrict(IReadOnlyList<int> districtIds, int districtId)
    {
        for (int i = 0; i < districtIds.Count; i++)
        {
            if (districtIds[i] == districtId)
            {
                return true;
            }
        }

        return false;
    }
}
