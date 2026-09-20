using Visionary.Sim.Numerics;

namespace Visionary.Sim.Systems;

/// <summary>
/// 約定の適用(GDD02b §3・§3.2 / TDD01 §3.2)。<b>在庫・流動資金・帳簿を動かす唯一の場所。</b>
/// </summary>
public static class TradeSettlement
{
    /// <summary>
    /// 資金上限(GDD02b §3.2)。<c>FloorDiv(流動資金, 実効価格)</c>。<b>切り下げる</b> —
    /// 切り上げると1単位多く買えて流動資金が負に落ちる。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="unitEffectivePrice"/> が0以下。</exception>
    public static int FundsCap(int liquidFunds, int unitEffectivePrice)
    {
        if (unitEffectivePrice <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitEffectivePrice), unitEffectivePrice, "実効価格は1以上(GDD02b §3.2)。");
        }

        return IntegerMath.FloorDiv(liquidFunds, unitEffectivePrice);
    }

    /// <summary>1件の約定を適用する。</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="quantity"/> または <paramref name="unitEffectivePrice"/> が0以下。
    /// 0個の約定を記帳すると帳簿に意味の無い行が増え、<c>StateHasher</c> にも乗る。
    /// </exception>
    public static void Execute(
        World world,
        HouseholdState buyer,
        HouseholdState seller,
        DemandPurpose purpose,
        int itemId,
        int quantity,
        int unitEffectivePrice,
        int acquisitionCostSmoothingPermille)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(buyer);
        ArgumentNullException.ThrowIfNull(seller);

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "数量は1以上(GDD02b §3)。");
        }

        if (unitEffectivePrice <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitEffectivePrice), unitEffectivePrice, "実効価格は1以上(GDD02b §3)。");
        }

        // 1. 支払額はlongで積んでからcheckedでintへ戻す(quantity×unitEffectivePriceはintを
        // 容易に超える)。
        int payment = checked((int)((long)quantity * unitEffectivePrice));

        buyer.LiquidFunds -= payment;
        seller.LiquidFunds += payment;

        // 2. 販売在庫は工房在庫である(GDD02c §1.3)。
        seller.WorkshopInventory[itemId] -= quantity;

        // 3. 買い手の在庫は用途で行き先が変わる(GDD02b §3.2)。
        //    必需・嗜好 → 世帯在庫、生産の入力・耐久 → 工房在庫。
        if (purpose is DemandPurpose.Necessity or DemandPurpose.Preference)
        {
            buyer.HouseholdInventory[itemId] += quantity;
        }
        else
        {
            buyer.WorkshopInventory[itemId] += quantity;
        }

        // 4. 帳簿は双方に1行ずつ。向きで表す(数量の符号では表さない、GDD01 §4.4)。
        world.Ledgers[buyer.Id].Add(new LedgerEntry
        {
            CounterpartyId = seller.Id,
            ItemId = itemId,
            Quantity = quantity,
            UnitPrice = unitEffectivePrice,
            OccurredAt = world.Now,
            Terms = LedgerTerms.Cash,
            Direction = LedgerDirection.Purchase,
        });

        world.Ledgers[seller.Id].Add(new LedgerEntry
        {
            CounterpartyId = buyer.Id,
            ItemId = itemId,
            Quantity = quantity,
            UnitPrice = unitEffectivePrice,
            OccurredAt = world.Now,
            Terms = LedgerTerms.Cash,
            Direction = LedgerDirection.Sale,
        });

        // 5. 仕入れ移動平均単価は「生産の入力として買った」約定と「耐久として買った工具」だけが
        // 更新する(GDD02a §5.1)。暖房用に買った薪・嗜好で買った分が原価に乗らないようにする。
        // 品目ではなく用途で分岐させる ── 耐久財が工具以外に増えても黙って外れない(W2-08 §10)。
        if (purpose is DemandPurpose.ProductionInput or DemandPurpose.Durable)
        {
            buyer.PurchaseUnitCostAverage[itemId] = OfferPrice.UpdatedAcquisitionCost(
                buyer.PurchaseUnitCostAverage[itemId], unitEffectivePrice, acquisitionCostSmoothingPermille);
        }
    }

    /// <summary>
    /// 窓口からの輸入(GDD02d §2.1・§2.2)。<b>買い手側だけ動かす。</b>窓口は在庫・帳簿・流動資金を
    /// 持たないので、売り手側の状態は一切動かさない ── <see cref="Execute"/> に <c>null</c> を
    /// 通す形にしない(タスク仕様。売り手側の不変条件が読めなくなる)。
    /// </summary>
    /// <remarks>
    /// <b>帳簿は買い手側の1行だけ</b>(<see cref="LedgerDirection.Purchase"/>・相手 =
    /// <see cref="HouseholdState.ExternalMarketSellerId"/>・<see cref="LedgerTerms.Cash"/>)。
    /// これが <see cref="MarketReference.TryPreviousDaySettledPrice"/> と
    /// <see cref="MarketReference.TrySeller"/> が輸出を約定として数える契約の裏返しである
    /// (輸入は売り手側の錨に混ざらない ── 買った側の観測ではない)。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="quantity"/> または <paramref name="unitEffectivePrice"/> が0以下。
    /// </exception>
    public static void ExecuteImport(
        World world,
        HouseholdState buyer,
        DemandPurpose purpose,
        int itemId,
        int quantity,
        int unitEffectivePrice,
        int acquisitionCostSmoothingPermille)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(buyer);

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "数量は1以上(GDD02b §3)。");
        }

        if (unitEffectivePrice <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitEffectivePrice), unitEffectivePrice, "実効価格は1以上(GDD02b §3)。");
        }

        int payment = checked((int)((long)quantity * unitEffectivePrice));

        buyer.LiquidFunds -= payment;

        if (purpose is DemandPurpose.Necessity or DemandPurpose.Preference)
        {
            buyer.HouseholdInventory[itemId] += quantity;
        }
        else
        {
            buyer.WorkshopInventory[itemId] += quantity;
        }

        world.Ledgers[buyer.Id].Add(new LedgerEntry
        {
            CounterpartyId = HouseholdState.ExternalMarketSellerId,
            ItemId = itemId,
            Quantity = quantity,
            UnitPrice = unitEffectivePrice,
            OccurredAt = world.Now,
            Terms = LedgerTerms.Cash,
            Direction = LedgerDirection.Purchase,
        });

        if (purpose is DemandPurpose.ProductionInput or DemandPurpose.Durable)
        {
            buyer.PurchaseUnitCostAverage[itemId] = OfferPrice.UpdatedAcquisitionCost(
                buyer.PurchaseUnitCostAverage[itemId], unitEffectivePrice, acquisitionCostSmoothingPermille);
        }
    }

    /// <summary>
    /// 窓口への輸出(GDD02d §2.3)。<b>売り手側だけ動かす。</b>仕入れ移動平均は更新しない
    /// (売りであり、原価の観測ではない)。
    /// </summary>
    /// <remarks>
    /// <b>帳簿は売り手側の1行だけ</b>(<see cref="LedgerDirection.Sale"/>・相手 =
    /// <see cref="HouseholdState.ExternalMarketSellerId"/>・単価 = 外部買値)。<c>Sale</c> で書くこと・
    /// 相手を予約 Id にすることが契約である ── <see cref="MarketReference.TryPreviousDaySettledPrice"/>
    /// が既にこれを約定として数えており、書き方を変えると売り手の錨と GDD02c §1.1 の頭打ちが
    /// 黙って輸出を落とす。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="quantity"/> または <paramref name="unitPrice"/> が0以下。
    /// </exception>
    public static void ExecuteExport(World world, HouseholdState seller, int itemId, int quantity, int unitPrice)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(seller);

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "数量は1以上(GDD02d §2.3)。");
        }

        if (unitPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), unitPrice, "外部買値は1以上(GDD02d §2.3)。");
        }

        int payment = checked((int)((long)quantity * unitPrice));

        seller.LiquidFunds += payment;
        seller.WorkshopInventory[itemId] -= quantity;

        world.Ledgers[seller.Id].Add(new LedgerEntry
        {
            CounterpartyId = HouseholdState.ExternalMarketSellerId,
            ItemId = itemId,
            Quantity = quantity,
            UnitPrice = unitPrice,
            OccurredAt = world.Now,
            Terms = LedgerTerms.Cash,
            Direction = LedgerDirection.Sale,
        });
    }
}
