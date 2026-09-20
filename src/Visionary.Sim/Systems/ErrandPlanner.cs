using Visionary.Sim.Numerics;

namespace Visionary.Sim.Systems;

/// <summary>1世帯・1日ぶんの外出の計画(GDD06 §3)。</summary>
public readonly record struct ErrandPlan
{
    /// <summary>行くと決めた区画。貪欲に選んだ順。自区画を含まない。重複なし。</summary>
    public IReadOnlyList<int> VisitedDistrictIds { get; init; }

    /// <summary>当日の外出の労働損失‰の合計(GDD02a §2)。外出しない日は0。</summary>
    public int LaborLossPermille { get; init; }
}

/// <summary>
/// 外出の計画(GDD06 §3)。<see cref="World"/> と <see cref="WorldDefinition"/> を読んで貪欲に
/// 行き先を選ぶ。<b>書き込みは一切しない</b>(<see cref="BuyerDemand"/> と同じ)。
/// </summary>
/// <remarks>
/// <b>見積もり価格と相場基準(<see cref="MarketReference"/>)は別物である。混ぜない。</b>
/// 見積もり価格は「品目 × 売り手」の添字を持ち、当日の <see cref="World.Market"/> を
/// 距離 R 以内なら使う(GDD06 §3)。<see cref="MarketReference"/> は品目に対して畳んだ平均で
/// 当日の値を使わない ── 店ごとの差(外出の動機そのもの)が消えるので流用してはならない。
/// </remarks>
public sealed class ErrandPlanner
{
    private readonly WorldDefinition _definition;

    public ErrandPlanner(WorldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _definition = definition;
    }

    public ErrandPlan Plan(
        World world, HouseholdState buyer, in HouseholdDemand demand, in ErrandDelegate errand)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(buyer);

        // 5.1 需要リスト。demand.Linesの並びそのまま、Budget>0 かつ TargetStock>0 の行だけを採る。
        // TargetStock<=0の行はPurchaseQuantityの到達在庫が常に0にclampされ、余剰も恒に0で
        // 行き先を変えない(GDD02b §5.1)ので、テスト表に対応する行は立てない(規則1)。
        var lines = new List<DemandLine>();

        foreach (var line in demand.Lines)
        {
            if (line.Budget > 0 && line.TargetStock > 0)
            {
                lines.Add(line);
            }
        }

        var visitedDistrictIds = new List<int>();
        int totalTravelHours = 0;
        int laborLossPermille = 0;

        // 5.6 貪欲の本体。各周で必ず1区画増えるので最大District.Count周で終わる(Tの検査を
        // 落としても無限に回らない、GDD06 §3)。
        while (true)
        {
            // このラウンドの基準(自区画 ∪ ここまでに行くと決めた区画)における各行の余剰を
            // 先に固定する(候補区画ごとに毎回同じ値なので、ラウンドの頭で1回だけ求める)。
            var baselineSurplusByLine = new int[lines.Count];

            for (int i = 0; i < lines.Count; i++)
            {
                bool hasPrice = TryCheapestEstimate(
                    world, buyer, lines[i].ItemId,
                    districtId => districtId == buyer.DistrictId || visitedDistrictIds.Contains(districtId),
                    out int price);

                baselineSurplusByLine[i] = SurplusFor(lines[i], hasPrice, price);
            }

            int bestDistrictId = -1;
            long bestValue = 0; // 価値 ≤ 0 なら行かない(GDD06 §3)。
            int bestTravelHours = 0;

            for (int districtId = 0; districtId < District.Count; districtId++)
            {
                if (districtId == buyer.DistrictId || visitedDistrictIds.Contains(districtId))
                {
                    continue;
                }

                int travelHours = Errand.TravelHours(
                    buyer.DistrictId, districtId, _definition.TravelHoursPerDistrict);

                if (totalTravelHours + travelHours > _definition.DisposableHours)
                {
                    continue;
                }

                long gain = 0;

                for (int i = 0; i < lines.Count; i++)
                {
                    int candidateDistrictId = districtId;
                    bool hasPrice = TryCheapestEstimate(
                        world, buyer, lines[i].ItemId,
                        d => d == candidateDistrictId,
                        out int price);

                    int candidateSurplus = SurplusFor(lines[i], hasPrice, price);
                    gain += candidateSurplus - baselineSurplusByLine[i];
                }

                long value = gain - Errand.Cost(travelHours, errand.CostPerHour);

                // 初期値0・更新は`>`。「価値 ≤ 0 なら行かない」と「同値は区画Id昇順」の両方を
                // 同時に満たす(区画Idを昇順に走査しているので、`>=`にすると後勝ちで
                // ADR-0002の列挙順規約が破れる)。
                if (value > bestValue)
                {
                    bestValue = value;
                    bestDistrictId = districtId;
                    bestTravelHours = travelHours;
                }
            }

            if (bestDistrictId < 0)
            {
                break;
            }

            visitedDistrictIds.Add(bestDistrictId);
            totalTravelHours += bestTravelHours;

            // 外出ごとに切り上げてから合計する(Σ_d CeilDiv(…)。GDD06 §3)。
            laborLossPermille = checked(laborLossPermille + Errand.LaborLossPermille(
                errand.LaborPermille, bestTravelHours, _definition.DisposableHours));
        }

        return new ErrandPlan
        {
            VisitedDistrictIds = visitedDistrictIds,
            LaborLossPermille = laborLossPermille,
        };
    }

    /// <summary>余剰(5.5)。見積もり価格が「無い」なら0。</summary>
    private static int SurplusFor(in DemandLine line, bool hasPrice, int price)
    {
        if (!hasPrice)
        {
            return 0;
        }

        // BuyerBudget.Decideを通すことが「見積もりに使ったqと、着いてから解く購入量は、
        // 価格が見積もりどおりなら一致する」の実体である(GDD06 §4)。PurchaseQuantityを
        // 直接呼ぶと、現金上限や利潤上限で買えない品目のために外出が立ってしまう。
        var decision = BuyerBudget.Decide(line, price);
        int willingness = IntegerMath.ApplyPermille(line.BaseValue, line.StockPressurePermille);

        return Errand.Surplus(decision.Quantity, willingness, price);
    }

    /// <summary>
    /// 区画の最安店(5.4)。店(i)のうち<paramref name="isTargetDistrict"/>が真を返す区画に
    /// 属するものの中で見積もり価格が最小のもの。候補0件ならfalse。
    /// </summary>
    /// <remarks>選ばれた店が誰かは使わない ── 使うのは価格だけである(GDD06 §3)。</remarks>
    private bool TryCheapestEstimate(
        World world, HouseholdState buyer, int itemId, Func<int, bool> isTargetDistrict, out int cheapestPrice)
    {
        bool found = false;
        int best = 0;

        // 5.2 所在から引く店の候補。world.HouseholdsをId昇順に走査する(添字=Idなので
        // 先頭から回すだけでよい)。
        foreach (var seller in world.Households)
        {
            if (seller.Id == buyer.Id)
            {
                continue;
            }

            if (!isTargetDistrict(seller.DistrictId))
            {
                continue;
            }

            // 職業は世帯の現在の値を読む(#39の付け替えで変わる)。WorldDefinitionから表を
            // 前計算して持ち回してはならない。
            var recipe = _definition.Recipes[(int)seller.Occupation];

            if (!OutputsItem(recipe, itemId))
            {
                continue;
            }

            if (!TryEstimatePrice(world, buyer, itemId, seller.Id, seller.DistrictId, out int price))
            {
                continue;
            }

            if (!found || price < best)
            {
                found = true;
                best = price;
            }
        }

        cheapestPrice = best;
        return found;
    }

    private static bool OutputsItem(Recipe recipe, int itemId)
    {
        foreach (var output in recipe.Outputs)
        {
            if (output.ItemId == itemId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 見積もり価格(5.3、上から順に、最初に当たったものを採る)。EffectivePrice.Calculate
    /// (trust: 0)へ通した値。距離R以内で当日の売り注文が無ければ、候補から外す(falseを返す)。
    /// </summary>
    private bool TryEstimatePrice(
        World world, HouseholdState buyer, int itemId, int sellerId, int sellerDistrictId, out int estimatedPrice)
    {
        if (!TryEstimateOfferPrice(world, buyer, itemId, sellerId, sellerDistrictId, out int offerPrice))
        {
            estimatedPrice = 0;
            return false;
        }

        estimatedPrice = EffectivePrice.Calculate(offerPrice, trust: 0, _definition.TrustDiscountPermille);
        return true;
    }

    private bool TryEstimateOfferPrice(
        World world, HouseholdState buyer, int itemId, int sellerId, int sellerDistrictId, out int offerPrice)
    {
        // 1. 距離(買い手の区画, 売り手の区画) ≤ R: Marketに(i,s)の売り注文があればその提示価格。
        // 無ければ候補から外す(今日この店は売っていないと見れば分かる。GDD06 §3.1)。
        if (District.Distance(buyer.DistrictId, sellerDistrictId) <= District.VisionRadius)
        {
            return world.Market.TryGetValue(new MarketKey(itemId, sellerId), out offerPrice);
        }

        // 2. 有効な記憶: 世帯主の観測のうち(i,s)で最新のもの(now.DayIndex − 観測日 ≥ 1)の価格。
        // 保持期間は見ない ── 段3のObservations.Expireが先に消しているので、残っているものは
        // すべて期間内である(StoreChoiceの旧HasValidMemoryと同じ理由)。
        var headObservations = world.Knowledge[buyer.HeadNpcId];
        bool hasMemory = false;
        long latestDayIndex = 0;
        int latestPrice = 0;

        foreach (var observation in headObservations)
        {
            if (observation.ItemId != itemId || observation.SellerId != sellerId)
            {
                continue;
            }

            long dayDifference = world.Now.DayIndex - observation.ObservedAt.DayIndex;

            if (dayDifference < 1)
            {
                continue;
            }

            if (!hasMemory || observation.ObservedAt.DayIndex > latestDayIndex)
            {
                hasMemory = true;
                latestDayIndex = observation.ObservedAt.DayIndex;
                latestPrice = observation.Price;
            }
        }

        if (hasMemory)
        {
            offerPrice = latestPrice;
            return true;
        }

        // 3. 床。
        offerPrice = _definition.ExternalBuyPrice(itemId);
        return true;
    }
}
