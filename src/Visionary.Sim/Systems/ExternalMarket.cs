using Visionary.Sim.Numerics;
using Visionary.Sim.Time;

namespace Visionary.Sim.Systems;

/// <summary>
/// 都市外市場の窓口の純関数(GDD02d §2〜§2.3)。<see cref="World"/> を受け取らない ──
/// <see cref="OfferPrice"/> / <see cref="BuyerBudget"/> / <see cref="Errand"/> と同じ切り出し方。
/// <b>窓口そのものは在庫・帳簿・流動資金を持たない</b>(GDD02d §2.1)ので、ここには置かない。
/// </summary>
public static class ExternalMarket
{
    /// <summary>
    /// 窓口の未知価格の見積もりの下限。単位: 貨幣/1単位。1次産品は外部買値(床)を持たないため、
    /// 「知らない店を楽観的に見積もらせて探索を起こす」役目の代わりに置く最小の正の価格
    /// (GDD02d §2.2)。0 にしないのは <see cref="BuyerBudget.Decide"/> /
    /// <see cref="TradeSettlement.FundsCap"/> が「実効価格は1以上」の前提に立っているため。
    /// </summary>
    public const int UnknownPriceFloor = 1;

    /// <summary>逆関数の基準点(価格係数‰の中点。<see cref="OfferPrice.PriceCoefficientPermille"/>と対称)。</summary>
    private const int CoefficientMidpointPermille = 1500; // ‰

    /// <summary>在庫比*‰の上限(GDD02d §2.3。相場が床の2倍以上で頭打ち)。</summary>
    private const int StockRatioUpperBoundPermille = 2000; // ‰

    /// <summary>
    /// その日の窓口の提示価格(GDD02d §2.1・§2.2)。全品目を並べる ── 1次産品は床から、
    /// 都市生産品は外部買値(床)から導出した天井から(<see cref="WorldDefinition.ExternalSellPrice"/>)。
    /// </summary>
    /// <remarks>
    /// <b>戻り値は M0 では常に <c>true</c> である。</b>シグネチャを <c>bool Try…</c> のまま残すのは、
    /// <see cref="StoreChoice"/> / <see cref="ErrandPlanner"/> を触らずに済ませるためであり
    /// (決定2、タスク仕様)、GDD11 が窓口を行商人へ置き換えるとき「その日は並ばない」が
    /// 戻る余地を残す。<b>「false になる日がある」と読んではならない</b>(M0 の間は無い)。
    /// </remarks>
    public static bool TryOfferPrice(WorldDefinition definition, Tick now, int itemId, out int offerPrice)
    {
        ArgumentNullException.ThrowIfNull(definition);

        offerPrice = definition.ExternalSellPrice(itemId, GameDate.FromTick(now).Season);
        return true;
    }

    /// <summary>
    /// 買い手の区画が中心(窓口)に届くか(GDD06 §3.1「居た区画 = 自区画 ∪ その日に行った区画」)。
    /// 線形探索(高々8件。<c>Dictionary</c>/<c>HashSet</c>を持ち込まない、GDD06 §3)。
    /// </summary>
    public static bool IsWithinReach(int buyerDistrictId, IReadOnlyList<int> visitedDistrictIds)
    {
        ArgumentNullException.ThrowIfNull(visitedDistrictIds);

        if (buyerDistrictId == District.ExternalMarketDistrictId)
        {
            return true;
        }

        for (int i = 0; i < visitedDistrictIds.Count; i++)
        {
            if (visitedDistrictIds[i] == District.ExternalMarketDistrictId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 輸出の閾在庫(GDD02d §2.3)。提示価格式の逆関数 ──
    /// <c>係数*‰ = CeilDiv(外部買値×1000, 相場基準)</c>、
    /// <c>在庫比*‰ = clamp(2×(1500−係数*‰), 0, 2000)</c>、
    /// <c>閾在庫 = CeilDiv(出荷目標在庫×在庫比*‰, 1000)</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>破産中の判定を先に置く。</b>相場基準の枝の後ろに置いても M0 の値では同じ結果になる
    /// 場合が多く、踏んでも気付けない(タスク仕様)。
    /// </para>
    /// <para>
    /// <b>相場基準が使うのは売り手側(速い側、<see cref="MarketReference.TrySeller"/>)である。</b>
    /// 買い手側(遅い側)と取り違えても例外は出ない(GDD02c §1.2 末尾)。
    /// </para>
    /// <para><b><c>clamp</c> の上限は2000である。</b>1000にすると閾在庫が出荷目標在庫を超えられず、
    /// 「都市の相場が高い日は輸出が減る」という応答(GDD02d §2.3)が消える。</para>
    /// </remarks>
    public static int ExportThresholdStock(
        int externalBuyPrice, bool hasMarketReference, int marketReference,
        int shipmentTargetStock, int isBankrupt)
    {
        if (isBankrupt is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(isBankrupt), isBankrupt, "破産中フラグは0/1(GDD02b §3.3)。");
        }

        if (isBankrupt == 1)
        {
            return 0;
        }

        if (!hasMarketReference)
        {
            return shipmentTargetStock;
        }

        // 中間の積はlong(外部買値×1000、出荷目標在庫×在庫比*‰はintを容易に超える)。
        long coefficientPermille = IntegerMath.CeilDiv(
            (long)externalBuyPrice * IntegerMath.PermilleScale, marketReference);
        long stockRatioPermille = Math.Clamp(
            2L * (CoefficientMidpointPermille - coefficientPermille), 0L, (long)StockRatioUpperBoundPermille);

        return checked((int)IntegerMath.CeilDiv((long)shipmentTargetStock * stockRatioPermille, IntegerMath.PermilleScale));
    }
}
