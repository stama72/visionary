using Visionary.Sim.Randomness;

namespace Visionary.Sim.Systems;

/// <summary>
/// 交易(TDD01 §3.3 順5)。<b>本タスクが実装するのは値付けの段と観測の段だけである。</b>
/// 店の選択・約定(#37)はこのクラスの中に足す ── 順5 に2つのシステムを置けない
/// (<see cref="SimScheduler"/> が <see cref="Stream"/> の重複登録を拒む。共通乱数法が壊れる)。
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="World.Market"/> を書くのはこのシステムだけである</b>(GDD02 §6.3「書き込みは1か所
/// のままである」)。約定(#37)は在庫・資金・帳簿を動かすが <c>Market</c> には触らない。
/// </para>
/// <para>
/// <b>毎日クリアして書き直す。</b><c>Market</c> のエントリがあることが「前日に売り注文を出して
/// いた」と同値になるのは、毎日クリアして書き直すからである(GDD02 §8.1.1)。<b>したがって実装は
/// 2段である。</b>1. 全売り手ぶんの新しい提示価格を求める(この間 <c>Market</c> は読むだけ)。
/// 2. <c>Market.Clear()</c> してから、1 の結果を一括で書く。売り手ごとに読んでは書くと、
/// 先に <c>Clear()</c> してしまった売り手が自分の前日価格を失う。
/// </para>
/// <para>
/// <b>観測の段は値付けの後である(#36)。</b>GDD06 §3.1「今日の知覚 = 自区画から距離 R 以内に
/// 出ている売り注文」なので、観測するのは当日の提示価格である。生まれた観測は
/// <see cref="OfferPrice.TryMarketReference"/> の鮮度判定(差1日以上)により翌日から有効な
/// 記憶になる ── 同一tick内での相互参照を切る。
/// </para>
/// <para>
/// <b>乱数を一切引かない。</b><see cref="Stream"/> が <see cref="RandomStream.Trade"/> を持つのは
/// 登録に一意な識別子が要るからであって、<see cref="SimContext.OpenRandom(int)"/> を呼ぶためでは
/// ない(<see cref="ProductionSystem"/> と同じ、TDD01 §3.1)。
/// </para>
/// </remarks>
public sealed class TradeSystem : ISimSystem
{
    private readonly WorldDefinition _definition;

    public TradeSystem(WorldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // 原価の未定義経路(入力0件・複数出力)をコンストラクタで検査する。値付けの最中に
        // 落とすと、販売在庫がたまたま0の日だけ生き延びるので、失敗が在庫に依存して再現しない。
        foreach (var recipe in definition.Recipes)
        {
            if (recipe.Inputs.Length == 0)
            {
                throw new NotSupportedException(
                    $"職業 {recipe.Occupation} のレシピが入力0件。機会費用ベースの原価は未実装"
                        + "(GDD02 §8.1.1、#51まで)。");
            }

            if (recipe.Outputs.Length != 1)
            {
                throw new NotSupportedException(
                    $"職業 {recipe.Occupation} のレシピが出力2件以上。原価の配分規則はGDD02に無い。");
            }
        }

        _definition = definition;
    }

    public RandomStream Stream => RandomStream.Trade;

    public Cadence Cadence => Cadence.Daily(hour: 0);

    public void Step(World world, SimContext context)
    {
        ArgumentNullException.ThrowIfNull(world);

        // 1. 新しい提示価格を求める(この間 world.Market は読むだけ)。世帯Id昇順に
        // (Householdsは添字=Idなので先頭から走査するだけでADR-0002の処理順規約を満たす)。
        var newOffers = new List<(MarketKey Key, int Price)>();

        foreach (var household in world.Households)
        {
            var recipe = _definition.Recipes[(int)household.Occupation];
            int outputItemId = recipe.Outputs[0].ItemId; // 出力1件はコンストラクタが保証

            // 販売在庫は工房在庫の出力品目だけ(世帯在庫は見ない。GDD02 §8.1.1「販売在庫は
            // 工房在庫の部分集合であり、生産の入力として抱えている分は売りに出ていない」)。
            int sellableStock = household.WorkshopInventory[outputItemId];

            if (sellableStock <= 0)
            {
                // 販売在庫0の日は売り注文を出さない。出品すると#37の店選択に
                // 在庫のない店が候補として載る(GDD02 §8.1.1)。
                continue;
            }

            int unitCost = OfferPrice.UnitCost(recipe, household.PurchaseUnitCostAverage);
            int costFloor = OfferPrice.CostFloor(
                unitCost, _definition.MinimumMarginPermille, household.IsBankrupt);

            bool hasOwn = world.Market.TryGetValue(
                new MarketKey(outputItemId, household.Id), out int ownPreviousPrice);

            // 誰の観測かは世帯主(親方)である(GDD02 §8.1.1)。買い物に行くのが徒弟でも
            // 値付けに使うのは世帯主の観測。Knowledgeの添字はNpcIdなのでHeadNpcIdで引く。
            bool hasReference = OfferPrice.TryMarketReference(
                world.Knowledge[household.HeadNpcId],
                outputItemId,
                household.Id,
                world.Now,
                _definition.ObservationRetentionDays,
                hasOwn,
                ownPreviousPrice,
                out int marketReference);

            // 職業は世帯の現在の値を読む(#39の付け替えで変わる)。出荷目標在庫も現在の職業の行を引く。
            int shipmentTargetStock =
                _definition.ShipmentTargetStockByOccupation[(int)household.Occupation][outputItemId];

            int price = hasReference
                ? OfferPrice.Calculate(costFloor, marketReference, sellableStock, shipmentTargetStock)
                : costFloor; // 相場基準が立たない(GDD02 §8.2.7)

            newOffers.Add((new MarketKey(outputItemId, household.Id), price));
        }

        // 2. Market.Clear() してから、1 で積んだものを一括で書く。先にClear()すると
        // 全売り手が自分の前日価格を失う(TDD01 §3.2が要求した一括書き込み)。
        world.Market.Clear();

        foreach (var offer in newOffers)
        {
            world.Market[offer.Key] = offer.Price;
        }

        // 3. 観測(GDD06 §3.1 / GDD08 §8.1、#36)。失効を先に呼ぶ ── 当日生まれた観測は差0なので
        // どちらの順でも消えないが、「生まれた当日は失効しない」が順序に依存しない形になる。
        Observations.Expire(world, _definition.ObservationRetentionDays);

        // 世帯Id昇順に(Householdsは添字=Idなので先頭から走査するだけで規約を満たす)。
        // 訪れた区画は本タスクでは常に空(#37が買い物で訪れた区画を渡す)。
        foreach (var household in world.Households)
        {
            Observations.CollectAndShare(world, household, Array.Empty<int>());
        }
    }
}
