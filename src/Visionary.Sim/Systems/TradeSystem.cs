using Visionary.Sim.Numerics;
using Visionary.Sim.Randomness;

namespace Visionary.Sim.Systems;

/// <summary>
/// 交易(TDD01 §3.3 順5)。値付け・約定・観測の3系統を1つのシステムに収める ──
/// 順5 に2つのシステムを置けない(<see cref="SimScheduler"/> が <see cref="Stream"/> の
/// 重複登録を拒む。共通乱数法が壊れる)。
/// </summary>
/// <remarks>
/// <para>
/// <b>内部は6段(うち段5は5a・5bの2手順)</b>(TDD01 §3.3「順5 Trade の内部の段」)。
/// 1. 全売り手の新しい提示価格を求める(<c>Market</c> は読むだけ。前日の自分の出力品目の
/// 提示価格を控える)。2. <c>Market.Clear()</c> → 一括書き込み。3. <see cref="Observations.Expire"/>。
/// 4. 全世帯ぶんの <see cref="HouseholdDemand"/> を作る(段5 の中へ畳まない)。
/// 5. 世帯 Id 昇順に、5a(<see cref="ErrandPlanner"/> で外出を計画し
/// <see cref="HouseholdState.ErrandLaborLossPermille"/> を書く)→5b(<see cref="StoreChoice"/> で
/// 5aが決めた区画の中から店を選び <see cref="TradeSettlement"/> で約定する)。
/// 6. <see cref="Observations.CollectAndShare"/>(段5 と別ループ)。
/// </para>
/// <para>
/// <b><see cref="World.Market"/> を書くのは段2 だけである</b>(GDD02b §4.1「書き込みは1か所の
/// ままである」)。約定は在庫・資金・帳簿を動かすが <c>Market</c> には触らない。
/// <c>Market</c> のエントリがあることが「前日に売り注文を出していた」と同値になるのは、
/// 毎日クリアして書き直すからである(GDD02c §1)。売り手ごとに読んでは書くと、先に
/// <c>Clear()</c> してしまった売り手が自分の前日価格を失うので、段1 と段2 は分かれている。
/// </para>
/// <para>
/// <b>段6(観測の生成)は段5(買い物)の後であり、かつ別ループである。</b>段5 の世帯ループへ畳むと、
/// 先に買った世帯の観測が後の世帯の買い物より前に生まれる(GDD06 §3.1「記憶は前日まで」は
/// 当日生成の観測が誰にも読まれないことに立っている)。生まれた観測は
/// <see cref="MarketReference"/> / <see cref="StoreChoice"/> の鮮度判定
/// (差1日以上)により翌日から有効な記憶になる。
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
    private readonly BuyerDemand _buyerDemand;
    private readonly StoreChoice _storeChoice;
    private readonly ErrandPlanner _errandPlanner;

    public TradeSystem(WorldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // 出力2件以上を拒む検査は残す ── 利潤上限の「見込み収益 = 提示価格_出力[前日] ×
        // 出力数量」が出力1件を前提にしており、販売在庫がたまたま0の日だけ生き延びる失敗を
        // 避けるためコンストラクタで拒む(本タスク仕様)。「入力0件のレシピ」を拒む検査は
        // 原価のためだったので消えた ── 床が外部買値になり、原価は値付けに入らない(GDD02c §1)。
        foreach (var recipe in definition.Recipes)
        {
            if (recipe.Outputs.Length != 1)
            {
                throw new NotSupportedException(
                    $"職業 {recipe.Occupation} のレシピが出力2件以上。利潤上限の配分規則がGDD02c §2.3に無い。");
            }
        }

        _definition = definition;

        // BuyerDemand / StoreChoice / ErrandPlanner はStepのたびに作らず、コンストラクタで1つ持つ
        // (_definitionと同じ。タスク仕様)。
        _buyerDemand = new BuyerDemand(definition);
        _storeChoice = new StoreChoice(definition);
        _errandPlanner = new ErrandPlanner(definition);
    }

    public RandomStream Stream => RandomStream.Trade;

    public Cadence Cadence => Cadence.Daily(hour: 0);

    public void Step(World world, SimContext context)
    {
        ArgumentNullException.ThrowIfNull(world);

        int householdCount = world.Households.Length;

        // 段4がBuyerDemand.Buildへ渡す「前日の自分の出力品目の提示価格」。Worldの状態にしない
        // (1日限りの中間値、#36引き継ぎ)。販売在庫0で売り注文を出さない世帯についても控える
        // ── その世帯も買い手としては需要を持つ。
        var hasOwnPreviousOffer = new bool[householdCount];
        var ownPreviousOfferPrice = new int[householdCount];

        // 段1. 新しい提示価格を求める(この間 world.Market は読むだけ)。世帯Id昇順に
        // (Householdsは添字=Idなので先頭から走査するだけでADR-0002の処理順規約を満たす)。
        var newOffers = new List<(MarketKey Key, int Price)>();

        foreach (var household in world.Households)
        {
            var recipe = _definition.Recipes[(int)household.Occupation];
            int outputItemId = recipe.Outputs[0].ItemId; // 出力1件はコンストラクタが保証

            // 販売在庫は工房在庫の出力品目だけ(世帯在庫は見ない。GDD02c §1.3「販売在庫は
            // 工房在庫の部分集合であり、生産の入力として抱えている分は売りに出ていない」)。
            int sellableStock = household.WorkshopInventory[outputItemId];

            // hasOwnPreviousOffer/ownPreviousOfferPriceは段4(利潤上限)が読む「前日の出力提示価格」
            // である。販売在庫0の世帯についても控える ── その世帯も買い手として利潤上限を持つ。
            bool hasOwnOffer = world.Market.TryGetValue(
                new MarketKey(outputItemId, household.Id), out int ownOfferPrice);

            hasOwnPreviousOffer[household.Id] = hasOwnOffer;
            ownPreviousOfferPrice[household.Id] = ownOfferPrice;

            if (sellableStock <= 0)
            {
                // 販売在庫0の日は売り注文を出さない。出品すると店選択に在庫のない店が
                // 候補として載る(GDD02c §1.3)。
                continue;
            }

            int floorPrice = _definition.ExternalBuyPrice(outputItemId);

            // 売り手の自分の錨は前日の提示価格ではなく前日の約定単価(帳簿、輸出を含む)。
            bool hasSettled = MarketReference.TryPreviousDaySettledPrice(
                world.Ledgers[household.Id], outputItemId, world.Now, out int settledPrice);

            // 誰の観測かは世帯主(親方)である(GDD02c §1.2)。買い物に行くのが徒弟でも
            // 値付けに使うのは世帯主の観測。Knowledgeの添字はNpcIdなのでHeadNpcIdで引く。
            bool hasReference = MarketReference.TrySeller(
                world.Knowledge[household.HeadNpcId],
                outputItemId,
                household.Id,
                world.Now,
                _definition.ObservationRetentionDays,
                hasSettled,
                settledPrice,
                out int marketReference);

            // 職業は世帯の現在の値を読む(#39の付け替えで変わる)。出荷目標在庫も現在の職業から導く。
            int shipmentTargetStock =
                _definition.ShipmentTargetStock(household.Occupation, outputItemId);

            int price = hasReference
                ? OfferPrice.Calculate(
                    floorPrice, marketReference, sellableStock, shipmentTargetStock, household.IsBankrupt)
                : floorPrice; // 相場基準が立たない(GDD02c §1)

            newOffers.Add((new MarketKey(outputItemId, household.Id), price));
        }

        // 段2. Market.Clear() してから、段1 で積んだものを一括で書く。先にClear()すると
        // 全売り手が自分の前日価格を失う(TDD01 §3.2が要求した一括書き込み)。
        world.Market.Clear();

        foreach (var offer in newOffers)
        {
            world.Market[offer.Key] = offer.Price;
        }

        // 段3. 観測の失効(GDD06 §3.1)。値付けの後・買い物の前に置く ── 段5 の
        // StoreChoice.TrySelect が「有効な記憶」を保持期間で二重に判定しないための前提
        // (StoreChoice のdocコメント参照)。
        Observations.Expire(world, _definition.ObservationRetentionDays);

        // 段4. 全世帯ぶんのHouseholdDemandを作る。段5 の中へ畳まない ── 畳むと予算そのものが
        // 世帯Idの走査順の関数になる(TDD01 §3.3)。
        var demands = new HouseholdDemand[householdCount];

        foreach (var household in world.Households)
        {
            demands[household.Id] = _buyerDemand.Build(
                world, household, hasOwnPreviousOffer[household.Id], ownPreviousOfferPrice[household.Id]);
        }

        // 段5. 世帯Id昇順に、5a(外出の計画)→5b(購入)。段6 でまとめて使うため、
        // 行くと決めた区画を世帯ごとに控える(TDD01 §3.3)。
        var visitedDistrictIdsByHousehold = new IReadOnlyList<int>[householdCount];

        foreach (var household in world.Households)
        {
            // 5a. 外出の計画。ErrandLaborLossPermilleは0の日も必ず書く ── UnaffordableNecessityCount
            // と同じ理由で、書かない日があると前日の損失が翌日以降も効き続ける。
            var errand = OpportunityCost.SelectErrandDelegate(_definition, world, household);
            var plan = _errandPlanner.Plan(world, household, demands[household.Id], errand);

            household.ErrandLaborLossPermille = plan.LaborLossPermille;
            visitedDistrictIdsByHousehold[household.Id] = plan.VisitedDistrictIds;

            // 5b. 購入。訪問は5aが決めており、買えたかどうかで変わらない。
            RunOneHouseholdsShopping(world, household, demands[household.Id], plan.VisitedDistrictIds);
        }

        // 段6. 段5 と別のループ(先に買った世帯の観測が、後の世帯の買い物より前に生まれない
        // ようにする。docコメント参照)。世帯Id昇順に。
        foreach (var household in world.Households)
        {
            Observations.CollectAndShare(world, household, visitedDistrictIdsByHousehold[household.Id]);
        }
    }

    /// <summary>
    /// 購入量(用途の単位)を個数へ直す(段5b 手順5。GDD02b §5.2)。耐久だけ耐久値で持つので
    /// 変換が要る。<b>用途による分岐をここに持つ</b> ── 段5b の本文に残すと、耐久の約定が
    /// W2 では構造的に起きない以上どこからもテストが踏めない(レビュー1巡目 I-b の訂正)。
    /// </summary>
    public static int PurchaseQuantityInUnits(DemandPurpose purpose, int quantity, int durabilityPerTool) =>
        purpose == DemandPurpose.Durable
            ? IntegerMath.CeilDiv(quantity, durabilityPerTool)
            : quantity;

    /// <summary>
    /// 段5b の1世帯ぶんの買い物。<paramref name="visitedDistrictIds"/> は段5a
    /// (<see cref="ErrandPlanner"/>)が既に決めた行き先であり、ここでは読むだけで書かない
    /// (訪問は買えたかどうかで変わらない)。
    /// </summary>
    private void RunOneHouseholdsShopping(
        World world, HouseholdState household, HouseholdDemand demand, IReadOnlyList<int> visitedDistrictIds)
    {
        // 毎日上書きする(ConsumptionSystemがUnmetConsumptionを毎日上書きするのと同じ)。
        household.UnaffordableNecessityCount = 0;

        // demand.Linesの並び順そのままに走査する ── GDD02b §3.2の走査順(必需→耐久→生産の入力→
        // 嗜好、同一用途は品目Id昇順)そのものである。資金は世帯内の共有資源なので、並べ替えると
        // 決済順が変わり結果が変わる(#36引き継ぎ「並べ直さないこと」)。
        foreach (var line in demand.Lines)
        {
            // 2. 自区画と行った区画の店のうち実効価格が最小のものを選ぶ。0件ならこのlineは終わり
            // (Needは立てない、#40)。販売在庫は約定のたびに減るのでworldを毎回読み直す ──
            // 候補を1日1回作って使い回すと売り切れた店を選んでしまう。
            if (!_storeChoice.TrySelect(world, household, line.ItemId, visitedDistrictIds, out var store))
            {
                continue;
            }

            // 3・4. ゲートと線形解(GDD02b §5.2)。渡すのは実効価格であって外出の費用を含む値では
            // ない(GDD02b §7「便益と費用の分離」── 予算は外出の費用を一切含まない)。
            var decision = BuyerBudget.Decide(line, store.UnitEffectivePrice);

            if (decision.Quantity <= 0)
            {
                // 経路(1): 現金上限のゲートで0(GDD02b §3.2)。「高すぎて買わなかった」
                // (MarketTerm / ProfitCap)は資金不足に数えない。
                if (line.Purpose == DemandPurpose.Necessity
                    && decision.Reason == NoPurchaseReason.CashCap)
                {
                    household.UnaffordableNecessityCount++;
                }

                continue;
            }

            int purchaseQuantity = decision.Quantity;

            // 5. 個数へ直す。
            int purchaseQuantityInUnits = PurchaseQuantityInUnits(
                line.Purpose, purchaseQuantity, _definition.ToolDurabilityPerUnit);

            // 6. 0以下なら、このlineは終わり(店は選んだが買う量が0)。
            if (purchaseQuantityInUnits <= 0)
            {
                continue;
            }

            // 8. 資金上限と売り手の在庫で買える数量を切り詰める。
            int fundsCap = TradeSettlement.FundsCap(household.LiquidFunds, store.UnitEffectivePrice);
            var seller = world.Households[store.SellerId];
            int affordableQuantity = Math.Min(purchaseQuantityInUnits, fundsCap);
            int actualQuantity = Math.Min(affordableQuantity, seller.WorkshopInventory[line.ItemId]);

            // 9. 経路(2): 資金上限の切り詰めで0(GDD02b §3.2)。売り手の在庫が尽きて
            // 0個になったのは資金不足ではない。経路(1)は上で既にcontinueしているので、
            // 同じlineが両方の経路で二重に数えられることは無い。
            if (line.Purpose == DemandPurpose.Necessity && fundsCap == 0)
            {
                household.UnaffordableNecessityCount++;
            }

            // 10. 約定を適用する。
            if (actualQuantity >= 1)
            {
                TradeSettlement.Execute(
                    world, household, seller, line.Purpose, line.ItemId, actualQuantity,
                    store.UnitEffectivePrice, _definition.AcquisitionCostSmoothingPermille);
            }
        }
    }
}
