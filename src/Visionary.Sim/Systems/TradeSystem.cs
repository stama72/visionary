using Visionary.Sim.Randomness;

namespace Visionary.Sim.Systems;

/// <summary>
/// 交易の夕方の部(TDD01 §3.3 順5 夕)。約定・輸出・観測の生成・Need の差し引きを行う。
/// 値付け・市場の書き込み・外出の計画は朝0時の <see cref="TradePlanningSystem"/> が済ませている。
/// </summary>
/// <remarks>
/// <para>
/// <b>順5 を2つのシステムに割る理由。</b>朝と夕方で実行時刻が違うので <see cref="Cadence"/> が別になる。
/// 1つの系統に2つのシステムを置けない(<see cref="SimScheduler"/> が <see cref="Stream"/> の重複登録を
/// 拒む。共通乱数法が壊れる)ので、朝は <see cref="RandomStream.TradePlanning"/>、夕方は
/// <see cref="RandomStream.Trade"/> を持つ。
/// </para>
/// <para>
/// <b>内部の段(TDD01 §3.3 の段番号)。</b>段1〜3・5a は <see cref="TradePlanningSystem"/>。ここは
/// 4. 全世帯ぶんの <see cref="HouseholdDemand"/> を控えの欄から作り直す(朝と同じ式。プレイヤーの売買が
/// 0時〜18時の間に入った状態を映す。Metrics の需要の3欄はここだけが書く)。
/// 5b. 世帯 Id 昇順に、朝の計画の区画の中から <see cref="StoreChoice"/> で店を選び、都市内は
/// <see cref="TradeSettlement.Execute"/>、窓口は <see cref="TradeSettlement.ExecuteImport"/> で約定する。
/// 6. <b>輸出。</b><see cref="WorldDefinition.IsExportEnabled"/> が偽なら段ごと飛ばす。世帯 Id 昇順に、
/// 閾在庫を超えた販売在庫を <see cref="TradeSettlement.ExecuteExport"/> で窓口へ売る(GDD02d §2.3)。
/// <b>段5b の世帯ループへ畳まない</b> ── 畳むと先に輸出した世帯の在庫減が後の世帯の買い物に効く。
/// 7. <see cref="Observations.CollectAndShare"/> の直後に <see cref="Observations.CollectWindow"/>
/// (別ループ)。8. 段5b で約定した購入の個数を、用途を合わせて <see cref="World.Needs"/> から引く
/// (GDD02b §8.1)。
/// </para>
/// <para>
/// <b>朝の計画は World から読む。</b>計画の区画列・委託先・控えは <see cref="HouseholdState"/> の欄から
/// 読み、<see cref="TradePlanningSystem"/> のインスタンスやこのインスタンスのフィールドで持ち越さない
/// (ハッシュが同じで振る舞いが違う世界ができる)。段6 が中心を足す先は World の列ではなく、世帯ごとに
/// 複製した列である(World の列に足すと時間割が中心行きを2回数える)。
/// </para>
/// <para>
/// <b><see cref="World.Market"/> を書くのは朝の段2 だけである</b>(GDD02b §4.1)。約定は在庫・資金・帳簿を
/// 動かすが <c>Market</c> には触らない。
/// </para>
/// <para>
/// <b>段7(観測の生成)は段6(輸出)の後であり、かつ別ループである。</b>段5b の世帯ループへ畳むと、
/// 先に買った世帯の観測が後の世帯の買い物より前に生まれる(GDD06 §3.1「記憶は前日まで」)。
/// 観測を輸出より後に置くのは、持ち込んだ世帯がその日中心に居たことになる(GDD02d §2.3 追記)ためである。
/// </para>
/// <para>
/// <b>乱数を一切引かない。</b><see cref="Stream"/> が <see cref="RandomStream.Trade"/> を持つのは
/// 登録に一意な識別子が要るからであって、<see cref="SimContext.OpenRandom(int)"/> を呼ぶためでは
/// ない(<see cref="ProductionSystem"/> と同じ、TDD01 §3.1)。
/// </para>
/// </remarks>
public sealed class TradeSystem : ISimSystem
{
    /// <summary><see cref="DemandPurpose"/> の値の数(添字 = (int)DemandPurpose、0〜3)。</summary>
    private const int PurposeCount = 4;

    private readonly WorldDefinition _definition;
    private readonly BuyerDemand _buyerDemand;
    private readonly StoreChoice _storeChoice;

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

        // BuyerDemand / StoreChoice はStepのたびに作らず、コンストラクタで1つ持つ(_definitionと同じ)。
        _buyerDemand = new BuyerDemand(definition);
        _storeChoice = new StoreChoice(definition);
    }

    public RandomStream Stream => RandomStream.Trade;

    public Cadence Cadence => Cadence.Daily(WorldDefinition.EveningTradeHour);

    public void Step(World world, SimContext context)
    {
        ArgumentNullException.ThrowIfNull(world);

        int householdCount = world.Households.Length;

        // 段4(夕)。朝の段4 と同じ式・同じ引数の出所(控えの欄)。段5b の中へ畳まない ── 畳むと
        // 予算そのものが世帯Idの走査順の関数になる(TDD01 §3.3)。
        var demands = new HouseholdDemand[householdCount];

        foreach (var household in world.Households)
        {
            int outputItemId = _definition.Recipes[(int)household.Occupation].Outputs[0].ItemId;

            var demand = _buyerDemand.Build(
                world, household,
                household.MemoHasPreviousOffer[outputItemId] == 1,
                household.MemoPreviousOfferPrice[outputItemId]);

            demands[household.Id] = demand;

            // 順10(Metrics)の需要の3欄(W2-20 タスク仕様「呼び出し側を持たないコード」表)。
            // 夕方の段4 だけが書く ── 朝も書くと InputBlockedByFunds の 1 が夕方まで残る。
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

        // 段8 が引く購入の記録。世帯 × 用途((int)DemandPurpose) × 品目の約定個数。単位: 個。
        // World に置かない(1日限りの中間値)。
        var purchased = new int[householdCount][][];

        for (int h = 0; h < householdCount; h++)
        {
            purchased[h] = new int[PurposeCount][];

            for (int p = 0; p < PurposeCount; p++)
            {
                purchased[h][p] = new int[_definition.ItemCount];
            }
        }

        // 段5b. 世帯Id昇順に購入。訪問は朝の段5a が決めており、World の計画の欄から読む。
        foreach (var household in world.Households)
        {
            RunOneHouseholdsShopping(
                world, household, demands[household.Id], household.PlannedErrandDistrictIds,
                purchased[household.Id]);
        }

        // 段6・段7 が読む訪問区画は、世帯ごとに複製した列である。段6 が中心を足すのはこの複製で、
        // World の PlannedErrandDistrictIds は書き換えない(時間割が中心行きを2回数えるため)。
        var visitedDistrictIdsByHousehold = new List<int>[householdCount];

        foreach (var household in world.Households)
        {
            visitedDistrictIdsByHousehold[household.Id] = new List<int>(household.PlannedErrandDistrictIds);
        }

        // 段6. 輸出。IsExportEnabledが偽なら段ごと飛ばす(GDD02d §2.1「状態ではない」)。
        // 段5b の世帯ループへ畳まない ── 世帯間の取引が確定したあとに走る(GDD02d §2.3)。
        if (_definition.IsExportEnabled)
        {
            foreach (var household in world.Households)
            {
                // 委託先と往復時間の合計は、朝に書いた計画から引き直す。SelectErrandDelegate を
                // 夕方に呼び直さない(朝と夕方で委託先が違う日がありうる契約を静かに作る)。
                if (household.ErrandDelegateNpcId < 0)
                {
                    throw new InvalidOperationException(
                        "外出の計画が無い(TradePlanningSystem が先に走っていない)。");
                }

                int delegateLaborPermille =
                    _definition.LaborPermilleByRank[(int)world.Npcs[household.ErrandDelegateNpcId].Rank];

                // ErrandPlanner.Plan の TotalTravelHours と同じ式。World に欄を足さない。
                int totalTravelHours = 0;

                foreach (int districtId in household.PlannedErrandDistrictIds)
                {
                    totalTravelHours += Errand.TravelHours(
                        household.DistrictId, districtId, _definition.TravelHoursPerDistrict);
                }

                int outputItemId = _definition.Recipes[(int)household.Occupation].Outputs[0].ItemId;

                RunOneHouseholdsExport(
                    world, household,
                    household.MemoHasSellerReference[outputItemId] == 1,
                    household.MemoSellerReference[outputItemId],
                    delegateLaborPermille, totalTravelHours,
                    visitedDistrictIdsByHousehold[household.Id]);
            }
        }

        // 段7. 段5b・段6 と別のループ(先に買った/輸出した世帯の観測が、後の世帯の買い物より前に
        // 生まれないようにする)。世帯Id昇順に。
        foreach (var household in world.Households)
        {
            Observations.CollectAndShare(world, household, visitedDistrictIdsByHousehold[household.Id]);
            Observations.CollectWindow(_definition, world, household, visitedDistrictIdsByHousehold[household.Id]);
        }

        // 段8. 購入した個数を Need から引く(GDD02b §8.1)。
        SubtractPurchasesFromNeeds(world, purchased);
    }

    /// <summary>
    /// 段8. <see cref="World.Needs"/> を<b>格納順のまま</b>先頭から走査し、理由に応じた用途の購入個数を
    /// 引く。0 以下になった要素は <c>RemoveAll</c> で消す(格納順は保たれる)。<c>NextNeedId</c> には触らない。
    /// 世帯 × 理由の索引(Dictionary)は作らない ── 列挙順が不定になる。
    /// </summary>
    private static void SubtractPurchasesFromNeeds(World world, int[][][] purchased)
    {
        for (int i = 0; i < world.Needs.Count; i++)
        {
            var need = world.Needs[i];
            var bought = purchased[need.TargetHouseholdId];
            int itemId = need.ItemId;

            // 理由ごとに数量の出所(どの在庫の不足か)が違うので、同じ購入を複数の Need から引いてよい
            // (GDD02b §8.1 の表は購入を Need の間で配分していない)。
            // 対応表は NeedDeduction.Subtracts が持つ(プレイヤーの販売と共有)。
            int subtract = 0;

            for (int p = 0; p < PurposeCount; p++)
            {
                if (NeedDeduction.Subtracts(need.ReasonCode, (DemandPurpose)p))
                {
                    subtract += bought[p][itemId];
                }
            }

            if (subtract > 0)
            {
                world.Needs[i] = need with { Quantity = need.Quantity - subtract };
            }
        }

        world.Needs.RemoveAll(static need => need.Quantity <= 0);
    }

    /// <summary>
    /// 段5b の1世帯ぶんの買い物。<paramref name="visitedDistrictIds"/> は朝の段5a
    /// (<see cref="ErrandPlanner"/>)が既に決めた行き先であり、ここでは読むだけで書かない
    /// (訪問は買えたかどうかで変わらない)。<paramref name="purchased"/> は用途 × 品目の約定個数で、
    /// ここで足し込む。
    /// </summary>
    private void RunOneHouseholdsShopping(
        World world, HouseholdState household, HouseholdDemand demand, IReadOnlyList<int> visitedDistrictIds,
        int[][] purchased)
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
            bool bought = TryPurchaseLine(world, household, line, visitedDistrictIds, purchased);

            if (bought)
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
        World world, HouseholdState household, DemandLine line, IReadOnlyList<int> visitedDistrictIds,
        int[][] purchased)
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

        // 段8 の購入の記録。窓口からの輸入も足す(GDD02b §8.1「売り手が誰かは問わない」)。
        // 輸出は足さない(売りである)。
        purchased[(int)line.Purpose][line.ItemId] = checked(purchased[(int)line.Purpose][line.ItemId] + actualQuantity);

        // 順10(Metrics)のCurrentCounterpartyId(W2-20 タスク仕様「呼び出し側を持たないコード」表)。
        // 窓口(HouseholdState.ExternalMarketSellerId = int.MaxValue)も相手として記録する。
        // 同じ日に同じ品目を複数の相手から買った世帯は、最後に買った相手を採る(上書き)。
        world.Metrics.CurrentCounterpartyId[world.Metrics.IndexOf(household.Id, line.ItemId)] = store.SellerId;

        return true;
    }

    /// <summary>
    /// 段6 の1世帯ぶんの輸出(GDD02d §2.3)。段5b の後(世帯間の取引が確定したあと)に呼ばれる。
    /// </summary>
    /// <remarks>
    /// <b>委託先の労働力係数‰ と往復時間の合計は、朝の計画から引き直した値を渡される。</b>
    /// <see cref="OpportunityCost.SelectErrandDelegate"/> を夕方に呼び直す形は「朝と夕方で委託先が違う日が
    /// ありうる」という契約を静かに作る(#38タスク仕様)。<paramref name="visitedDistrictIds"/> は
    /// 世帯ごとに複製した列で、中心へ持ち込んだ日はここで中心を足す(段7 の観測がこれを読む)。
    /// 別の外出で中心へ持ち込んだ世帯だけ <see cref="HouseholdState.ExportErrandTaken"/> を 1 にする。
    /// </remarks>
    private void RunOneHouseholdsExport(
        World world,
        HouseholdState seller,
        bool hasSellerReference,
        int sellerReference,
        int delegateLaborPermille,
        int totalTravelHours,
        List<int> visitedDistrictIds)
    {
        var recipe = _definition.Recipes[(int)seller.Occupation];
        int outputItemId = recipe.Outputs[0].ItemId; // 出力1件はコンストラクタが保証

        // 販売在庫は段5b の後の値(工房在庫の出力品目から留保を引いた量。朝の段1 と同じ
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

            // 労働損失は加算である(朝の段5a が書いた当日の合計に足す。GDD06 §3「外出ごとに切り上げて
            // から合計する」)。
            seller.ErrandLaborLossPermille = checked(seller.ErrandLaborLossPermille + Errand.LaborLossPermille(
                delegateLaborPermille, travelHours, _definition.DisposableHours));

            // 複製の列へ中心を足す。段7 の観測がこれを読む。時間割のために別の外出だったことも残す。
            visitedDistrictIds.Add(District.ExternalMarketDistrictId);
            seller.ExportErrandTaken = 1;
        }

        TradeSettlement.ExecuteExport(
            world, seller, outputItemId, surplus, externalBuyPrice,
            SellableStock.ReserveQuantity(_definition, seller, outputItemId));
    }
}
