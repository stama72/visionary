using Visionary.Sim.Time;

namespace Visionary.Sim.Systems;

/// <summary>
/// 相場観測の生成・世帯内共有・失効(GDD06 §3.1 / GDD08 §8.1)。
/// </summary>
public static class Observations
{
    /// <summary>保持期間を過ぎた観測を全 NPC の Knowledge から取り除く(GDD06 §3.1)。</summary>
    /// <remarks>
    /// <b>失効の境界は <see cref="MarketReference"/> の有効性判定と揃える。</b>あちらは
    /// 差 &gt; retentionDays を無効としているので、こちらも差 &gt; retentionDays を取り除く。
    /// <c>&gt;=</c> にすると、保持期間ちょうどの観測が読む前に消える。
    /// </remarks>
    public static void Expire(World world, int retentionDays)
    {
        ArgumentNullException.ThrowIfNull(world);

        // npcId昇順(Knowledgeは添字=NpcIdの配列なので、先頭から走査するだけで
        // ADR-0002の処理順規約を満たす)。List<T>.RemoveAllは生き残りの相対順を保つ。
        for (int npcId = 0; npcId < world.Knowledge.Length; npcId++)
        {
            world.Knowledge[npcId].RemoveAll(
                observation => world.Now.DayIndex - observation.ObservedAt.DayIndex > retentionDays);
        }
    }

    /// <summary>
    /// その日に居た区画から距離 R 以内の売り注文を観測し、世帯全員で共有する
    /// (GDD06 §3.1 / GDD08 §8.1)。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="World.Market"/> を1回だけ走査することが、重複を構造で防いでいる。</b>
    /// 居た区画を外側のループにすると、自区画と訪問区画の両方から見える売り手が2件記録される。
    /// </para>
    /// <para>
    /// <b><see cref="PriceObservation.LocationId"/> は売り手の区画である</b>(観測者の区画ではない)。
    /// #37 の移動費がここを読む。
    /// </para>
    /// <para>
    /// <b><see cref="ObservationSource.Direct"/> だけを作る。</b>帰宅時共有は M0 で唯一の共有経路
    /// であり、<see cref="ObservationSource.Heard"/> を区別して読む処理が M0 に無い。区別を入れる
    /// のは Rumor(#42)。
    /// </para>
    /// <para><paramref name="visitedDistrictIds"/> は買い物の段(<see cref="TradeSystem"/> 段5)が
    /// 訪れた区画を渡す(#37)。</para>
    /// </remarks>
    public static void CollectAndShare(
        World world, HouseholdState household, IReadOnlyList<int> visitedDistrictIds)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(household);
        ArgumentNullException.ThrowIfNull(visitedDistrictIds);

        // MarketKey昇順に1回だけ走査する(World.MarketはSortedDictionary)。
        foreach (var entry in world.Market)
        {
            var key = entry.Key;

            if (key.SellerId == household.Id)
            {
                // 自分の売り注文は自分の観測に入れない(GDD06 §3.1「純粋な自己ループ」)。
                continue;
            }

            int sellerDistrictId = world.Households[key.SellerId].DistrictId;

            if (!IsWithinVisionRadius(household.DistrictId, visitedDistrictIds, sellerDistrictId))
            {
                continue;
            }

            var observation = new PriceObservation
            {
                ItemId = key.ItemId,
                LocationId = sellerDistrictId,
                Price = entry.Value,
                SellerId = key.SellerId,
                ObservedAt = world.Now,
                Source = ObservationSource.Direct,
            };

            // 世帯全員に同じレコードを配る(GDD08 §8.1「見ただけの売り注文も含む」)。
            // MemberNpcIdsの昇順は構築時に検証済み。
            foreach (int npcId in household.MemberNpcIds)
            {
                world.Knowledge[npcId].Add(observation);
            }
        }
    }

    /// <summary>
    /// 都市外市場の窓口の観測(GDD02d §2.2 / GDD06 §3.1)。<b>視界半径の例外は置かない</b>
    /// ── 中心が視界(R)の外なら何もしない。真なら全品目を品目Id昇順に1件ずつ、
    /// 世帯全員へ配る(既存の <see cref="CollectAndShare"/> と同じ)。窓口が都市生産品も並べる
    /// (#149)以上、その値も観測になる(1次産品だけに絞る理由が無い)。
    /// </summary>
    /// <remarks>
    /// <b><see cref="CollectAndShare"/> に畳まない。</b>あちらは <see cref="World.Market"/> を
    /// 1回だけ走査することが重複を構造で防いでおり、窓口は <see cref="World.Market"/> に載らない
    /// (GDD02d §2.1)。畳むと走査の中に「<see cref="World.Market"/> に無いもの」を混ぜることになる。
    /// <b>自分の売り注文の除外は要らない</b> ── 窓口はどの世帯でもない。
    /// </remarks>
    public static void CollectWindow(
        WorldDefinition definition, World world, HouseholdState household,
        IReadOnlyList<int> visitedDistrictIds)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(household);
        ArgumentNullException.ThrowIfNull(visitedDistrictIds);

        if (!IsWithinVisionRadius(household.DistrictId, visitedDistrictIds, District.ExternalMarketDistrictId))
        {
            return;
        }

        var season = GameDate.FromTick(world.Now).Season;

        // 品目Id昇順に全品目を対象にする(0..ItemCount-1を素直に走査すれば昇順になる)。
        for (int itemId = 0; itemId < definition.ItemCount; itemId++)
        {
            var observation = new PriceObservation
            {
                ItemId = itemId,
                LocationId = District.ExternalMarketDistrictId,
                Price = definition.ExternalSellPrice(itemId, season),
                SellerId = HouseholdState.ExternalMarketSellerId,
                ObservedAt = world.Now,
                Source = ObservationSource.Direct,
            };

            // 世帯全員に同じレコードを配る(MemberNpcIdsの昇順は構築時に検証済み)。
            foreach (int npcId in household.MemberNpcIds)
            {
                world.Knowledge[npcId].Add(observation);
            }
        }
    }

    /// <summary>居た区画(自区画 ∪ 訪問区画)のいずれかから距離 R 以内かどうか(GDD06 §3.1)。</summary>
    private static bool IsWithinVisionRadius(
        int homeDistrictId, IReadOnlyList<int> visitedDistrictIds, int sellerDistrictId)
    {
        if (District.Distance(homeDistrictId, sellerDistrictId) <= District.VisionRadius)
        {
            return true;
        }

        foreach (int visitedDistrictId in visitedDistrictIds)
        {
            if (District.Distance(visitedDistrictId, sellerDistrictId) <= District.VisionRadius)
            {
                return true;
            }
        }

        return false;
    }
}
