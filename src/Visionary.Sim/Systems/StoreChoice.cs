using Visionary.Sim.Numerics;
using Visionary.Sim.Time;

namespace Visionary.Sim.Systems;

/// <summary>選ばれうる店1件(GDD06 §2・§3)。</summary>
public readonly record struct StoreCandidate
{
    /// <summary>売り手の世帯 Id。</summary>
    public int SellerId { get; init; }

    /// <summary>売り手の区画 Id(移動費と、訪れた区画の記録に使う)。</summary>
    public int DistrictId { get; init; }

    /// <summary>
    /// 実効価格。単位: 貨幣/1単位。<b>M0-W2 では提示価格そのものである</b> —
    /// 信用による割引(GDD01 §2.2 効果1)は W4。<b>支払いに使うのはこちらであって
    /// <see cref="UnitRealCost"/> ではない</b>(GDD02 §6.2.1)。
    /// </summary>
    public int UnitEffectivePrice { get; init; }

    /// <summary>実質コスト = 実効価格 + 1単位あたりの移動費(GDD06 §2)。単位: 貨幣/1単位。</summary>
    public int UnitRealCost { get; init; }
}

/// <summary>
/// 店の候補集合と実質コストと選択(GDD06 §2・§3・§3.1)。<b>World を読むだけで書かない。</b>
/// </summary>
public sealed class StoreChoice
{
    private readonly WorldDefinition _definition;

    public StoreChoice(WorldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _definition = definition;
    }

    /// <summary>移動時間。単位: 時間。距離 × 1区画あたりの移動時間 × 2(<b>往復</b>、GDD02 §4.3)。</summary>
    public static int TravelHours(int fromDistrictId, int toDistrictId, int hoursPerDistrict)
    {
        int distance = District.Distance(fromDistrictId, toDistrictId);

        return checked(distance * hoursPerDistrict * 2);
    }

    /// <summary>
    /// 1単位あたりの移動費(GDD06 §2)。
    /// <c>CeilDiv(移動時間 × 機会費用, max(目標在庫, 1))</c>。
    /// </summary>
    /// <param name="targetStockInUnits">
    /// <b>個数に直した目標在庫</b>。耐久の行は耐久値で数えているので、呼び出し側が
    /// <c>CeilDiv(TargetStock, 1個あたりの耐久値)</c> してから渡すこと(GDD06 §2)。
    /// </param>
    public static int TravelCostPerUnit(int travelHours, int opportunityCost, int targetStockInUnits)
    {
        // 中間の積はlong(移動時間×機会費用はintを超えうる)。
        long numerator = (long)travelHours * opportunityCost;

        // max(目標在庫,1)で零除算を避ける ── 目標在庫0(入力切れの工房)でも移動費は
        // 定義できる必要がある(GDD06 §2)。
        int denominator = Math.Max(targetStockInUnits, 1);

        return checked((int)IntegerMath.CeilDiv(numerator, denominator));
    }

    /// <summary>
    /// 知っている店のうち実質コストが最小のものを選ぶ(GDD06 §3)。候補0件なら false。
    /// </summary>
    /// <remarks>
    /// <b>走査は <see cref="World.Market"/> の列挙順そのまま。</b><see cref="MarketKey"/> は
    /// ItemId → SellerId の昇順(<c>SortedDictionary</c>)なので、<c>&lt;</c> で更新すれば
    /// 同値のとき売り手 Id 最小が自然に残る(ADR-0002 / GDD06 §3.1)。
    /// </remarks>
    public bool TrySelect(
        World world,
        HouseholdState buyer,
        int itemId,
        int targetStockInUnits,
        int errandOpportunityCost,
        out StoreCandidate selected)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(buyer);

        bool found = false;
        var best = default(StoreCandidate);
        int bestRealCost = 0;

        var headObservations = world.Knowledge[buyer.HeadNpcId];

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

            // 先に来た買い手が買い尽くした店は候補に入らない(GDD06 §3)。
            if (seller.WorkshopInventory[itemId] <= 0)
            {
                continue;
            }

            bool isKnown =
                District.Distance(buyer.DistrictId, seller.DistrictId) <= District.VisionRadius // 今日の知覚
                || HasValidMemory(headObservations, itemId, sellerId, world.Now); // 有効な記憶

            if (!isKnown)
            {
                continue;
            }

            int travelHours = TravelHours(
                buyer.DistrictId, seller.DistrictId, _definition.TravelHoursPerDistrict);
            int travelCost = TravelCostPerUnit(travelHours, errandOpportunityCost, targetStockInUnits);
            int realCost = checked(price + travelCost);

            if (!found || realCost < bestRealCost)
            {
                found = true;
                bestRealCost = realCost;
                best = new StoreCandidate
                {
                    SellerId = sellerId,
                    DistrictId = seller.DistrictId,
                    UnitEffectivePrice = price,
                    UnitRealCost = realCost,
                };
            }
        }

        selected = best;
        return found;
    }

    /// <summary>
    /// 有効な記憶(GDD06 §3.1)。<b>4b で保持期間を見ない</b> ── 段3 <see cref="Observations.Expire"/>
    /// が保持期間を過ぎた観測を先に消しているので、ここに残っているものはすべて期間内である
    /// (境界は <see cref="OfferPrice.TryMarketReference"/> と同じ理由で二重に書かない)。
    /// </summary>
    /// <remarks>
    /// <b>「差 ≥ 1」は明示する。</b>段6(観測の生成)が段5(買い物)より後ろにある以上、当日の観測は
    /// 実際には存在しない。それでも書くのは、段の順序という別の事実に規範を依存させないため
    /// である ── 段を入れ替えた瞬間に GDD06 §3.1 が切った日内の相互参照が静かに戻る。
    /// </remarks>
    private static bool HasValidMemory(
        IReadOnlyList<PriceObservation> headObservations, int itemId, int sellerId, Tick now)
    {
        foreach (var observation in headObservations)
        {
            if (observation.ItemId == itemId
                && observation.SellerId == sellerId
                && now.DayIndex - observation.ObservedAt.DayIndex >= 1)
            {
                return true;
            }
        }

        return false;
    }
}
