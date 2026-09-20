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
}
