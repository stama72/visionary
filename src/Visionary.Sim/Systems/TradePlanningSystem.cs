using Visionary.Sim.Randomness;

namespace Visionary.Sim.Systems;

/// <summary>
/// 交易の朝の部(TDD01 §3.3 順5 朝)。値付け・市場の書き込み・観測の失効・外出の計画を行う。
/// 約定と観測の生成は夕方の <see cref="TradeSystem"/> が行う。
/// </summary>
/// <remarks>
/// <para>
/// <b>段1〜4・5a を朝0時に行う。</b>1. 全売り手の新しい提示価格を求める(<c>Market</c> は読むだけ)。
/// 2. <c>Market.Clear()</c> → 一括書き込み。3. <see cref="Observations.Expire"/>。
/// 4. 全世帯ぶんの <see cref="HouseholdDemand"/> を作る(5a が読む。Metrics は書かない)。
/// 5a. 世帯 Id 昇順に委託先を選び <see cref="ErrandPlanner"/> で外出を計画する。買い物はしない。
/// </para>
/// <para>
/// <b>夕方の <see cref="TradeSystem"/> へは World 以外の経路で値を渡さない。</b>このインスタンスの
/// フィールド・静的フィールドで持ち越すと、ハッシュが同じで夕方の振る舞いが違う世界ができる。
/// 渡すものは <see cref="HouseholdState"/> の控えの欄(<c>Memo*</c>)と計画の欄(<c>ErrandDelegateNpcId</c> /
/// <c>PlannedErrandDistrictIds</c> / <c>ExportErrandTaken</c>)に書く(TDD01 §3.2・§3.8)。
/// </para>
/// <para>
/// <b>乱数を一切引かない。</b><see cref="Stream"/> が <see cref="RandomStream.TradePlanning"/> を持つのは
/// 登録に一意な識別子が要るからである(<see cref="TradeSystem"/> と同じ、TDD01 §3.1)。
/// </para>
/// </remarks>
public sealed class TradePlanningSystem : ISimSystem
{
    private readonly WorldDefinition _definition;
    private readonly BuyerDemand _buyerDemand;
    private readonly ErrandPlanner _errandPlanner;

    public TradePlanningSystem(WorldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // TradeSystem と同じ検査。利潤上限の「見込み収益 = 提示価格_出力[前日] × 出力数量」が
        // 出力1件を前提にしている。
        foreach (var recipe in definition.Recipes)
        {
            if (recipe.Outputs.Length != 1)
            {
                throw new NotSupportedException(
                    $"職業 {recipe.Occupation} のレシピが出力2件以上。利潤上限の配分規則がGDD02c §2.3に無い。");
            }
        }

        _definition = definition;
        _buyerDemand = new BuyerDemand(definition);
        _errandPlanner = new ErrandPlanner(definition);
    }

    public RandomStream Stream => RandomStream.TradePlanning;

    public Cadence Cadence => Cadence.Daily(hour: 0);

    public void Step(World world, SimContext context)
    {
        ArgumentNullException.ThrowIfNull(world);

        // 順10(Metrics)が読む当日ぶんの計数の初期化。順5朝が当日の Metrics の最初の書き手である
        // (段1 が SellerHasNoReference / SellerCoefficientCapped を書く)。
        world.Metrics.BeginDay();

        int householdCount = world.Households.Length;

        // 段1. 新しい提示価格を求める(この間 world.Market は読むだけ)。世帯Id昇順に。
        var newOffers = new List<(MarketKey Key, int Price)>();

        foreach (var household in world.Households)
        {
            var recipe = _definition.Recipes[(int)household.Occupation];
            int outputItemId = recipe.Outputs[0].ItemId; // 出力1件はコンストラクタが保証

            // 販売在庫は工房在庫の出力品目から留保を引いた量(世帯在庫は見ない。GDD02c §1.3)。
            int sellableStock = SellableStock.Of(_definition, household, outputItemId);

            bool hasOwnOffer = world.Market.TryGetValue(
                new MarketKey(outputItemId, household.Id), out int ownOfferPrice);

            // 売り手の自分の錨は前日の提示価格ではなく前日の約定単価(帳簿、輸出を含む)。
            bool hasSettled = MarketReference.TryPreviousDaySettledPrice(
                world.Ledgers[household.Id], outputItemId, world.Now, out int settledPrice);

            // 誰の観測かは世帯主(親方)である(GDD02c §1.2)。
            bool hasReference = MarketReference.TrySeller(
                world.Knowledge[household.HeadNpcId],
                outputItemId,
                household.Id,
                world.Now,
                _definition.ObservationRetentionDays,
                hasSettled,
                settledPrice,
                out int marketReference);

            // 控えは世帯ごとに4本とも消してから、自レシピの出力品目の要素だけを書く。販売在庫0の
            // 世帯についても書く ── その世帯も買い手として利潤上限を持ち、輸出(夕方の段6)の閾在庫は
            // 相場基準を読む。sellableStock <= 0 の continue より上に置くのはそのため。
            Array.Clear(household.MemoHasPreviousOffer);
            Array.Clear(household.MemoPreviousOfferPrice);
            Array.Clear(household.MemoHasSellerReference);
            Array.Clear(household.MemoSellerReference);

            household.MemoHasPreviousOffer[outputItemId] = hasOwnOffer ? 1 : 0;
            household.MemoPreviousOfferPrice[outputItemId] = ownOfferPrice;
            household.MemoHasSellerReference[outputItemId] = hasReference ? 1 : 0;
            household.MemoSellerReference[outputItemId] = marketReference;

            // 職業は世帯の現在の値を読む(#39の付け替えで変わる)。
            int shipmentTargetStock =
                _definition.ShipmentTargetStock(household.Occupation, outputItemId);

            world.Metrics.SellerHasNoReference[household.Id] = hasReference ? 0 : 1;
            world.Metrics.SellerCoefficientCapped[household.Id] = OfferPrice.WasUnsoldCapApplied(
                hasReference, sellableStock, shipmentTargetStock, household.IsBankrupt, hasSettled)
                ? 1
                : 0;

            if (sellableStock <= 0)
            {
                // 販売在庫0の日は売り注文を出さない(GDD02c §1.3)。
                continue;
            }

            int floorPrice = _definition.ExternalBuyPrice(outputItemId);

            int price = hasReference
                ? OfferPrice.Calculate(
                    floorPrice, marketReference, sellableStock, shipmentTargetStock, household.IsBankrupt,
                    hasSettled)
                : floorPrice; // 相場基準が立たない(GDD02c §1)

            newOffers.Add((new MarketKey(outputItemId, household.Id), price));
        }

        // 段2. Market.Clear() してから一括で書く。先に Clear() すると全売り手が自分の前日価格を失う。
        world.Market.Clear();

        foreach (var offer in newOffers)
        {
            world.Market[offer.Key] = offer.Price;
        }

        // 段3. 観測の失効(GDD06 §3.1)。値付けの後・計画の前に置く(ErrandPlanner が保持期間を
        // 二重に判定しないための前提)。
        Observations.Expire(world, _definition.ObservationRetentionDays);

        // 段4(朝)。全世帯ぶんの需要を作る。5a の中へ畳まない ── 畳むと予算が世帯Idの走査順の
        // 関数になる(TDD01 §3.3)。Metrics は書かない(需要の3欄は買い物に使う夕方の段4 が書く)。
        var demands = new HouseholdDemand[householdCount];

        foreach (var household in world.Households)
        {
            int outputItemId = _definition.Recipes[(int)household.Occupation].Outputs[0].ItemId;

            demands[household.Id] = _buyerDemand.Build(
                world, household,
                household.MemoHasPreviousOffer[outputItemId] == 1,
                household.MemoPreviousOfferPrice[outputItemId]);
        }

        // 段5a. 外出の計画。外出しない日も全欄を書く(書かない日があると前日の計画が残る)。
        foreach (var household in world.Households)
        {
            var errand = OpportunityCost.SelectErrandDelegate(_definition, world, household);
            var plan = _errandPlanner.Plan(world, household, demands[household.Id], errand);

            household.ErrandLaborLossPermille = plan.LaborLossPermille;
            household.ErrandDelegateNpcId = errand.NpcId;
            household.PlannedErrandDistrictIds.Clear();
            household.PlannedErrandDistrictIds.AddRange(plan.VisitedDistrictIds);
            household.ExportErrandTaken = 0;
        }
    }
}
