using Visionary.Sim.Randomness;

namespace Visionary.Sim.Systems;

/// <summary>
/// 交易(TDD01 §3.3 順5)。値付け・約定・観測の3系統を1つのシステムに収める ──
/// 順5 に2つのシステムを置けない(<see cref="SimScheduler"/> が <see cref="Stream"/> の
/// 重複登録を拒む。共通乱数法が壊れる)。
/// </summary>
/// <remarks>
/// <para>
/// <b>内部は7段(うち段5は5a・5bの2手順)</b>(TDD01 §3.3「順5 Trade の内部の段」/ #38 タスク仕様。
/// 段6(輸出)を新設して段を1つ増やした)。
/// 1. 全売り手の新しい提示価格を求める(<c>Market</c> は読むだけ。前日の自分の出力品目の
/// 提示価格と、輸出(段6)が読む相場基準(速い側)を控える)。2. <c>Market.Clear()</c> → 一括書き込み。
/// 3. <see cref="Observations.Expire"/>。
/// 4. 全世帯ぶんの <see cref="HouseholdDemand"/> を作る(段5 の中へ畳まない)。
/// 5. 世帯 Id 昇順に、5a(<see cref="ErrandPlanner"/> で外出を計画し
/// <see cref="HouseholdState.ErrandLaborLossPermille"/> を書く)→5b(<see cref="StoreChoice"/> で
/// 5aが決めた区画の中から店を選び、都市内は <see cref="TradeSettlement.Execute"/>、窓口は
/// <see cref="TradeSettlement.ExecuteImport"/> で約定する)。
/// 6. <b>輸出(新設)。</b><see cref="WorldDefinition.IsExportEnabled"/> が偽なら段ごと飛ばす。
/// 世帯 Id 昇順に、閾在庫を超えた販売在庫を <see cref="TradeSettlement.ExecuteExport"/> で窓口へ
/// 売る(GDD02d §2.3)。<b>段5 の世帯ループへ畳まない</b> ── 畳むと先に輸出した世帯の在庫減が、
/// 後の世帯の買い物に効く(世帯間の取引が「確定したあと」でなくなる)。
/// 7. <see cref="Observations.CollectAndShare"/> の直後に <see cref="Observations.CollectWindow"/>
/// (段5 と別ループ)。
/// </para>
/// <para>
/// <b><see cref="World.Market"/> を書くのは段2 だけである</b>(GDD02b §4.1「書き込みは1か所の
/// ままである」)。約定は在庫・資金・帳簿を動かすが <c>Market</c> には触らない。
/// <c>Market</c> のエントリがあることが「前日に売り注文を出していた」と同値になるのは、
/// 毎日クリアして書き直すからである(GDD02c §1)。売り手ごとに読んでは書くと、先に
/// <c>Clear()</c> してしまった売り手が自分の前日価格を失うので、段1 と段2 は分かれている。
/// </para>
/// <para>
/// <b>段7(観測の生成)は段6(輸出)の後であり、かつ別ループである。</b>段5 の世帯ループへ畳むと、
/// 先に買った世帯の観測が後の世帯の買い物より前に生まれる(GDD06 §3.1「記憶は前日まで」は
/// 当日生成の観測が誰にも読まれないことに立っている)。生まれた観測は
/// <see cref="MarketReference"/> / <see cref="ErrandPlanner"/> の鮮度判定
/// (差1日以上)により翌日から有効な記憶になる。
/// </para>
/// <para>
/// <b>輸出は段5 と段7(観測)の間の新しい段である。</b>観測より前に置くのは、持ち込んだ世帯が
/// その日中心に居たことになる(GDD02d §2.3 追記)ためである ── 段6 が訪問区画に中心を足した後で
/// なければ、段7 がその観測を生めない。
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

        // 順10(Metrics)が読む当日ぶんの計数の初期化(W2-20 タスク仕様「呼び出し順」)。
        // 段1 より前、順5 の先頭で呼ぶ ── 順5 が唯一の書き手である。
        world.Metrics.BeginDay();

        int householdCount = world.Households.Length;

        // 段4がBuyerDemand.Buildへ渡す「前日の自分の出力品目の提示価格」。Worldの状態にしない
        // (1日限りの中間値、#36引き継ぎ)。販売在庫0で売り注文を出さない世帯についても控える
        // ── その世帯も買い手としては需要を持つ。
        var hasOwnPreviousOffer = new bool[householdCount];
        var ownPreviousOfferPrice = new int[householdCount];

        // 段6(輸出)が読む、売り手の相場基準(速い側)。段1が求める(#38タスク仕様)。
        var hasSellerReference = new bool[householdCount];
        var sellerReference = new int[householdCount];

        // 段1. 新しい提示価格を求める(この間 world.Market は読むだけ)。世帯Id昇順に
        // (Householdsは添字=Idなので先頭から走査するだけでADR-0002の処理順規約を満たす)。
        var newOffers = new List<(MarketKey Key, int Price)>();

        foreach (var household in world.Households)
        {
            var recipe = _definition.Recipes[(int)household.Occupation];
            int outputItemId = recipe.Outputs[0].ItemId; // 出力1件はコンストラクタが保証

            // 販売在庫は工房在庫の出力品目から留保を引いた量(世帯在庫は見ない。GDD02c §1.3
            // 「販売在庫は工房在庫の部分集合であり、生産の入力・設備として抱えている分は
            // 売りに出ていない」)。5経路すべてを SellableStock に通す(W2-14。#148 追随表)。
            int sellableStock = SellableStock.Of(_definition, household, outputItemId);

            // hasOwnPreviousOffer/ownPreviousOfferPriceは段4(利潤上限)が読む「前日の出力提示価格」
            // である。販売在庫0の世帯についても控える ── その世帯も買い手として利潤上限を持つ。
            bool hasOwnOffer = world.Market.TryGetValue(
                new MarketKey(outputItemId, household.Id), out int ownOfferPrice);

            hasOwnPreviousOffer[household.Id] = hasOwnOffer;
            ownPreviousOfferPrice[household.Id] = ownOfferPrice;

            // 売り手の自分の錨は前日の提示価格ではなく前日の約定単価(帳簿、輸出を含む)。
            // hasSettledは錨(§1.2)と頭打ち(§1.1)の両方に使う。母数は同じ(帳簿のSale、
            // 品目で絞る、輸出を含む)。
            bool hasSettled = MarketReference.TryPreviousDaySettledPrice(
                world.Ledgers[household.Id], outputItemId, world.Now, out int settledPrice);

            // 誰の観測かは世帯主(親方)である(GDD02c §1.2)。買い物に行くのが徒弟でも
            // 値付けに使うのは世帯主の観測。Knowledgeの添字はNpcIdなのでHeadNpcIdで引く。
            // #38: この控えは販売在庫0の世帯についても行う(sellableStock<=0のcontinueより上)。
            // 移さないと、段5で工具を買った鍛冶(耐久は工房在庫へ入る=販売在庫が増える唯一の経路)の
            // 閾在庫が「相場基準が無い日」の枝に落ちる(#38タスク仕様「決めたこと」)。
            bool hasReference = MarketReference.TrySeller(
                world.Knowledge[household.HeadNpcId],
                outputItemId,
                household.Id,
                world.Now,
                _definition.ObservationRetentionDays,
                hasSettled,
                settledPrice,
                out int marketReference);

            hasSellerReference[household.Id] = hasReference;
            sellerReference[household.Id] = marketReference;

            // 職業は世帯の現在の値を読む(#39の付け替えで変わる)。出荷目標在庫も現在の職業から導く。
            // sellableStock<=0のcontinueより前に出す ── 順10(Metrics)の2欄が
            // 販売在庫0の世帯についても読むため(W2-20 タスク仕様「呼び出し側を持たないコード」表)。
            int shipmentTargetStock =
                _definition.ShipmentTargetStock(household.Occupation, outputItemId);

            world.Metrics.SellerHasNoReference[household.Id] = hasReference ? 0 : 1;
            world.Metrics.SellerCoefficientCapped[household.Id] = OfferPrice.WasUnsoldCapApplied(
                hasReference, sellableStock, shipmentTargetStock, household.IsBankrupt, hasSettled)
                ? 1
                : 0;

            if (sellableStock <= 0)
            {
                // 販売在庫0の日は売り注文を出さない。出品すると店選択に在庫のない店が
                // 候補として載る(GDD02c §1.3)。
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

        // 段2. Market.Clear() してから、段1 で積んだものを一括で書く。先にClear()すると
        // 全売り手が自分の前日価格を失う(TDD01 §3.2が要求した一括書き込み)。
        world.Market.Clear();

        foreach (var offer in newOffers)
        {
            world.Market[offer.Key] = offer.Price;
        }

        // 段3. 観測の失効(GDD06 §3.1)。値付けの後・買い物の前に置く ── 段5a の
        // ErrandPlanner(見積もり価格5.3)が「有効な記憶」を保持期間で二重に判定しないための前提
        // (ErrandPlannerのdocコメント参照)。
        Observations.Expire(world, _definition.ObservationRetentionDays);

        // 段4. 全世帯ぶんのHouseholdDemandを作る。段5 の中へ畳まない ── 畳むと予算そのものが
        // 世帯Idの走査順の関数になる(TDD01 §3.3)。
        var demands = new HouseholdDemand[householdCount];

        foreach (var household in world.Households)
        {
            var demand = _buyerDemand.Build(
                world, household, hasOwnPreviousOffer[household.Id], ownPreviousOfferPrice[household.Id]);

            demands[household.Id] = demand;

            // 順10(Metrics)の需要の3欄(W2-20 タスク仕様「呼び出し側を持たないコード」表)。
            // BuyerDemand.Build の直後、demand.Lines を1回走査してまとめて求める。
            int demandLines = 0;
            int demandLinesWithoutKnownPrice = 0;
            bool inputBlockedByCashCap = false;

            foreach (var line in demand.Lines)
            {
                if (line.ExpectedStock < line.TargetStock)
                {
                    demandLines++;

                    if (!line.HasMarketTerm)
                    {
                        demandLinesWithoutKnownPrice++;
                    }
                }

                if (line.Purpose == DemandPurpose.ProductionInput && line.CashCap == 0)
                {
                    inputBlockedByCashCap = true;
                }
            }

            world.Metrics.DemandLines[household.Id] = demandLines;
            world.Metrics.DemandLinesWithoutKnownPrice[household.Id] = demandLinesWithoutKnownPrice;

            if (inputBlockedByCashCap)
            {
                world.Metrics.InputBlockedByFunds[household.Id] = 1;
            }
        }

        // 段5. 世帯Id昇順に、5a(外出の計画)→5b(購入)。段6・段7 でまとめて使うため、
        // 行くと決めた区画(List<int>のまま。段6が中心を足す)・委託先・往復時間の合計を
        // 世帯ごとに控える(TDD01 §3.3 / #38タスク仕様)。
        var visitedDistrictIdsByHousehold = new List<int>[householdCount];
        var errandDelegateByHousehold = new ErrandDelegate[householdCount];
        var totalTravelHoursByHousehold = new int[householdCount];

        foreach (var household in world.Households)
        {
            // 5a. 外出の計画。ErrandLaborLossPermilleは0の日も必ず書く ── UnaffordableNecessityCount
            // と同じ理由で、書かない日があると前日の損失が翌日以降も効き続ける。
            var errand = OpportunityCost.SelectErrandDelegate(_definition, world, household);
            var plan = _errandPlanner.Plan(world, household, demands[household.Id], errand);

            household.ErrandLaborLossPermille = plan.LaborLossPermille;

            // VisitedDistrictIdsの実体は必ずList<int>(ErrandPlanner.Planの構築どおり)。
            // 段6が同じ実体へ中心を足す(#38タスク仕様「型をList<int>[]にする」)。
            visitedDistrictIdsByHousehold[household.Id] = (List<int>)plan.VisitedDistrictIds;
            errandDelegateByHousehold[household.Id] = errand;
            totalTravelHoursByHousehold[household.Id] = plan.TotalTravelHours;

            // 5b. 購入。訪問は5aが決めており、買えたかどうかで変わらない。
            RunOneHouseholdsShopping(world, household, demands[household.Id], visitedDistrictIdsByHousehold[household.Id]);
        }

        // 段6(新設)。輸出。IsExportEnabledが偽なら段ごと飛ばす(GDD02d §2.1「状態ではない」)。
        // 段5 の世帯ループへ畳まない ── 世帯間の取引が確定したあとに走る(GDD02d §2.3)。
        if (_definition.IsExportEnabled)
        {
            foreach (var household in world.Households)
            {
                RunOneHouseholdsExport(
                    world, household, hasSellerReference[household.Id], sellerReference[household.Id],
                    errandDelegateByHousehold[household.Id], totalTravelHoursByHousehold[household.Id],
                    visitedDistrictIdsByHousehold[household.Id]);
            }
        }

        // 段7. 段5・段6 と別のループ(先に買った/輸出した世帯の観測が、後の世帯の買い物より前に
        // 生まれないようにする。docコメント参照)。世帯Id昇順に。
        foreach (var household in world.Households)
        {
            Observations.CollectAndShare(world, household, visitedDistrictIdsByHousehold[household.Id]);
            Observations.CollectWindow(_definition, world, household, visitedDistrictIdsByHousehold[household.Id]);
        }
    }

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

        // UnfilledPurchase(#40)も冒頭で全品目0にする。書かない日があると、前日の不足が
        // その後もずっとNeedを立て続ける(タスク仕様)。
        Array.Clear(household.UnfilledPurchase);

        // 品目ごとに「その日1個でも買えたか」を記録する(#40訂正。フェーズ2レビュー1巡目
        // 象限I-b)。判定は行単位ではなく品目単位である ── GDD02b §8.1 の遠方在庫の条件は
        // 「前日、その品目を1個も買えず」。薪・穀物のように必需と生産の入力の2行に現れる品目は、
        // GDD02b §3.2 の走査順で資金を食い潰すので「先の行は買えて後の行は買えない」が定常的に
        // 起きる。行単位のままだと、買えている品目にも遠方在庫が立ってしまう。
        var boughtItem = new bool[_definition.ItemCount];

        // demand.Linesの並び順そのままに走査する ── GDD02b §3.2の走査順(必需→耐久→生産の入力→
        // 嗜好、同一用途は品目Id昇順)そのものである。資金は世帯内の共有資源なので、並べ替えると
        // 決済順が変わり結果が変わる(#36引き継ぎ「並べ直さないこと」)。
        foreach (var line in demand.Lines)
        {
            bool purchased = TryPurchaseLine(world, household, line, visitedDistrictIds);

            if (purchased)
            {
                boughtItem[line.ItemId] = true;
                continue;
            }

            // #40: その行についてTradeSettlement.Execute/ExecuteImportを一度も呼ばなかった
            // (=1個も買えなかった)かつ予想在庫が目標在庫を下回っていたなら、不足量を足し込む。
            // += である(薪が必需と生産の入力の2行に現れるため。代入だと後の行が前の行を消す)。
            // 数量の合計(行をまたいだ+=)は品目単位の0クリアの対象ではない ── ExpectedStock/
            // TargetStockは段4が作った買い物より前の値であり、worldから読み直さない
            // (「その日に買った量」が混ざる)。
            if (line.ExpectedStock < line.TargetStock)
            {
                household.UnfilledPurchase[line.ItemId] += BuyerBudget.QuantityInUnits(
                    line.Purpose, line.TargetStock - line.ExpectedStock, _definition.ToolDurabilityPerUnit);
            }
        }

        // 走査を終えたあと、その日1個でも買えた品目はUnfilledPurchaseを0へ戻す(#40訂正)。
        // 部分的にでも買えた品目は数えない(GDD06 §3.1「その日に買えず」であって
        // 「目標在庫まで買えず」ではない) ── 行単位の合計を積んだ後に、品目単位で上書きする。
        for (int itemId = 0; itemId < _definition.ItemCount; itemId++)
        {
            if (boughtItem[itemId])
            {
                household.UnfilledPurchase[itemId] = 0;
            }
        }
    }

    /// <summary>
    /// 1行ぶんの購入を試みる。<see cref="TradeSettlement.Execute"/> /
    /// <see cref="TradeSettlement.ExecuteImport"/> を呼んで1個以上買えたら <c>true</c>。
    /// </summary>
    private bool TryPurchaseLine(
        World world, HouseholdState household, DemandLine line, IReadOnlyList<int> visitedDistrictIds)
    {
        // 2. 自区画と行った区画の店のうち実効価格が最小のものを選ぶ。0件ならこのlineは終わり
        // (Needは立てない、#40)。販売在庫は約定のたびに減るのでworldを毎回読み直す ──
        // 候補を1日1回作って使い回すと売り切れた店を選んでしまう。
        if (!_storeChoice.TrySelect(world, household, line.ItemId, visitedDistrictIds, out var store))
        {
            return false;
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

            // 順10(Metrics)のInputBlockedByFunds、経路(1)(W2-20 タスク仕様「呼び出し側を
            // 持たないコード」表)。1を代入する(加算しない。世帯日の0/1)。
            if (line.Purpose == DemandPurpose.ProductionInput
                && decision.Reason == NoPurchaseReason.CashCap)
            {
                world.Metrics.InputBlockedByFunds[household.Id] = 1;
            }

            return false;
        }

        int purchaseQuantity = decision.Quantity;

        // 5. 個数へ直す。ErrandPlannerの余剰(5.5)と同じ関数を通す(GDD06 §4「見積もりに
        // 使ったqと、着いてから解く購入量は、価格が見積もりどおりなら一致する」の実体)。
        int purchaseQuantityInUnits = BuyerBudget.QuantityInUnits(
            line.Purpose, purchaseQuantity, _definition.ToolDurabilityPerUnit);

        // 6. 0以下なら、このlineは終わり(店は選んだが買う量が0)。
        if (purchaseQuantityInUnits <= 0)
        {
            return false;
        }

        // 8. 資金上限で買える数量を切り詰める。窓口(isWindow)は無限在庫なので売り手の在庫
        // による切り詰めはしない ── int.MaxValue(HouseholdState.ExternalMarketSellerId)を
        // world.Householdsの添字に通さない(#38タスク仕様)。
        bool isWindow = store.SellerId == HouseholdState.ExternalMarketSellerId;
        var seller = isWindow ? null : world.Households[store.SellerId];

        int fundsCap = TradeSettlement.FundsCap(household.LiquidFunds, store.UnitEffectivePrice);
        int affordableQuantity = Math.Min(purchaseQuantityInUnits, fundsCap);
        int actualQuantity = isWindow
            ? affordableQuantity
            : Math.Min(affordableQuantity, SellableStock.Of(_definition, seller!, line.ItemId));

        // 9. 経路(2): 資金上限の切り詰めで0(GDD02b §3.2)。売り手の在庫が尽きて
        // 0個になったのは資金不足ではない。経路(1)は上で既にreturnしているので、
        // 同じlineが両方の経路で二重に数えられることは無い。数え方は変えない(#38タスク仕様)。
        if (line.Purpose == DemandPurpose.Necessity && fundsCap == 0)
        {
            household.UnaffordableNecessityCount++;
        }

        // 順10(Metrics)のInputBlockedByFunds、経路(2)(W2-20 タスク仕様「呼び出し側を
        // 持たないコード」表)。上の経路(1)は既にreturnしているので二重に立つことは無いが、
        // 1を代入する形は変えない(0/1の代入。加算しない)。
        if (line.Purpose == DemandPurpose.ProductionInput && fundsCap == 0)
        {
            world.Metrics.InputBlockedByFunds[household.Id] = 1;
        }

        // 10. 約定を適用する。窓口は買い手側だけを動かすExecuteImportを使う。
        if (actualQuantity < 1)
        {
            return false;
        }

        if (isWindow)
        {
            TradeSettlement.ExecuteImport(
                world, household, line.Purpose, line.ItemId, actualQuantity,
                store.UnitEffectivePrice, _definition.AcquisitionCostSmoothingPermille);
        }
        else
        {
            // 番人へ渡す留保量は SellableStock.ReserveQuantity(呼び出し側の切り詰め漏れを
            // 落とす最終防衛線。definition そのものは TradeSettlement へ渡さない、W2-14)。
            TradeSettlement.Execute(
                world, household, seller!, line.Purpose, line.ItemId, actualQuantity,
                store.UnitEffectivePrice, _definition.AcquisitionCostSmoothingPermille,
                SellableStock.ReserveQuantity(_definition, seller!, line.ItemId));
        }

        // 順10(Metrics)のCurrentCounterpartyId(W2-20 タスク仕様「呼び出し側を持たないコード」表)。
        // 窓口(HouseholdState.ExternalMarketSellerId = int.MaxValue)も相手として記録する。
        // 同じ日に同じ品目を複数の相手から買った世帯は、最後に買った相手を採る(上書き)。
        world.Metrics.CurrentCounterpartyId[world.Metrics.IndexOf(household.Id, line.ItemId)] = store.SellerId;

        return true;
    }

    /// <summary>
    /// 段6 の1世帯ぶんの輸出(GDD02d §2.3)。段5 の後(世帯間の取引が確定したあと)に呼ばれる。
    /// </summary>
    /// <remarks>
    /// <b>段5a が求めた <see cref="ErrandDelegate"/> と往復時間の合計を渡され、呼び直さない。</b>
    /// いまは同じ値を返すが、呼び直す形は「段5a と段6 で委託先が違う日がありうる」という契約を
    /// 静かに作る(#38タスク仕様)。<paramref name="visitedDistrictIds"/> は段5a の実体そのもので、
    /// 中心へ持ち込んだ日はここで中心を足す(段7 の観測がこれを読む)。
    /// </remarks>
    private void RunOneHouseholdsExport(
        World world,
        HouseholdState seller,
        bool hasSellerReference,
        int sellerReference,
        in ErrandDelegate errand,
        int totalTravelHours,
        List<int> visitedDistrictIds)
    {
        var recipe = _definition.Recipes[(int)seller.Occupation];
        int outputItemId = recipe.Outputs[0].ItemId; // 出力1件はコンストラクタが保証

        // 販売在庫は段5 の後の値(工房在庫の出力品目から留保を引いた量。段1 と同じ
        // SellableStock を通す。W2-14)。
        int sellableStock = SellableStock.Of(_definition, seller, outputItemId);

        if (sellableStock <= 0)
        {
            return;
        }

        int externalBuyPrice = _definition.ExternalBuyPrice(outputItemId);
        int shipmentTargetStock = _definition.ShipmentTargetStock(seller.Occupation, outputItemId);

        int thresholdStock = ExternalMarket.ExportThresholdStock(
            externalBuyPrice, hasSellerReference, sellerReference, shipmentTargetStock, seller.IsBankrupt);

        int surplus = Math.Max(0, sellableStock - thresholdStock);

        if (surplus <= 0)
        {
            return;
        }

        if (!ExternalMarket.IsWithinReach(seller.DistrictId, visitedDistrictIds))
        {
            int travelHours = Errand.TravelHours(
                seller.DistrictId, District.ExternalMarketDistrictId, _definition.TravelHoursPerDistrict);

            if (totalTravelHours + travelHours > _definition.DisposableHours)
            {
                // 持ち込まない(GDD02d §2.3「その日の往復時間の合計がTを超える日は持ち込まない」)。
                return;
            }

            // 労働損失は加算である(段5a が書いた当日の合計に足す。GDD06 §3「外出ごとに切り上げて
            // から合計する」)。
            seller.ErrandLaborLossPermille = checked(seller.ErrandLaborLossPermille + Errand.LaborLossPermille(
                errand.LaborPermille, travelHours, _definition.DisposableHours));

            // 訪問区画のリストは段5a のList<int>を持ち回っているので、ここで中心を足せば
            // 段7 の観測がこれを読む。
            visitedDistrictIds.Add(District.ExternalMarketDistrictId);
        }

        TradeSettlement.ExecuteExport(
            world, seller, outputItemId, surplus, externalBuyPrice,
            SellableStock.ReserveQuantity(_definition, seller, outputItemId));
    }
}
