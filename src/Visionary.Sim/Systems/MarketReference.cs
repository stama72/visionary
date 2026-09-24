using Visionary.Sim.Numerics;
using Visionary.Sim.Time;

namespace Visionary.Sim.Systems;

/// <summary>
/// 相場基準(GDD02c §1.2)。<b>純関数のみ。</b>売り手(速い)と買い手(遅い)は有効な観測の
/// 判定が共通で、畳み方だけが違う。
/// </summary>
public static class MarketReference
{
    /// <summary>
    /// 売り手の相場基準(速い側)。他の売り手ごとに最新1件 + 自分の前日の約定単価
    /// (GDD02c §1.2)。<b>他の売り手の観測が0件なら false を返し、自分の約定単価も使わない</b>。
    /// </summary>
    /// <remarks>
    /// <b>畳み込みは <see cref="SortedDictionary{TKey, TValue}"/> を使う。</b><c>Dictionary</c> は
    /// 列挙順が保証されず ADR-0002 に触れる(合計そのものは順序に依らないが、規約を型で守る)。
    /// 同一tickに同一売り手の観測が2件あるときは後に追加されたほう(走査で <c>&gt;=</c>)を採る。
    /// </remarks>
    /// <remarks>
    /// <b>窓口(<see cref="HouseholdState.ExternalMarketSellerId"/>)の観測もこの畳み込みに入る
    /// (決定5、#149タスク仕様)。</b>窓口は売り手 Id を予約した売り手であり
    /// (<a href="../../../docs/03-gdd/02d-external-market-and-money.md">GDD02d §2.1</a>)、
    /// その観測も通常どおり生まれる(同 §2.2「視界半径の例外は置かない」)。GDD02c §1.2 の表が
    /// 定める「他の売り手ごとに最新の1件」は売り手 Id を持つものすべてを対象にしており、
    /// 窓口を除外する読みは無い。<b>したがって窓口を取り込むのが現行仕様であり、ここは変えない</b>
    /// ── ただし#149で窓口が都市生産品も並べるまで、この経路は構造的に不活性だった(1次産品の
    /// 観測しか無かった)ことは事実である。
    /// </remarks>
    public static bool TrySeller(
        IReadOnlyList<PriceObservation> headObservations,
        int itemId,
        int selfHouseholdId,
        Tick now,
        int retentionDays,
        bool hasOwnSettledPrice,
        int ownSettledPrice,
        out int marketReference)
    {
        ArgumentNullException.ThrowIfNull(headObservations);

        var latestBySeller = new SortedDictionary<int, PriceObservation>();

        for (int i = 0; i < headObservations.Count; i++)
        {
            var observation = headObservations[i];

            if (!IsValid(observation, itemId, selfHouseholdId, now, retentionDays))
            {
                continue;
            }

            if (!latestBySeller.TryGetValue(observation.SellerId, out var existing)
                || observation.ObservedAt >= existing.ObservedAt)
            {
                latestBySeller[observation.SellerId] = observation;
            }
        }

        if (latestBySeller.Count == 0)
        {
            // 他の売り手の観測が0件のときは自分の約定単価も加えない(GDD06 §3.1が
            // 「純粋な自己ループ」と呼んだ状態)。
            marketReference = 0;
            return false;
        }

        long total = 0;
        int count = 0;

        foreach (var kvp in latestBySeller)
        {
            total += kvp.Value.Price;
            count++;
        }

        if (hasOwnSettledPrice)
        {
            total += ownSettledPrice;
            count++;
        }

        marketReference = checked((int)IntegerMath.CeilDiv(total, count));
        return true;
    }

    /// <summary>
    /// 買い手の相場基準(遅い側)。<b>有効な観測すべての平均</b>(売り手ごとに畳まない)。
    /// 0件なら false。自分の錨は無い(GDD02c §1.2)。
    /// </summary>
    /// <remarks>畳まずに全件を足す ── 合計と件数だけを見るので順序に依らない。</remarks>
    /// <remarks>
    /// <b>窓口の観測もこの平均に入る(決定5、#149タスク仕様)。</b>結論は
    /// <see cref="TrySeller"/> の doc コメントと同一である ── GDD02c §1.2 の「有効な観測すべて
    /// の平均」に窓口を除外する読みは無いので、ここも変えない。
    /// </remarks>
    public static bool TryBuyer(
        IReadOnlyList<PriceObservation> headObservations,
        int itemId,
        int selfHouseholdId,
        Tick now,
        int retentionDays,
        out int marketReference)
    {
        ArgumentNullException.ThrowIfNull(headObservations);

        long total = 0;
        int count = 0;

        for (int i = 0; i < headObservations.Count; i++)
        {
            var observation = headObservations[i];

            if (!IsValid(observation, itemId, selfHouseholdId, now, retentionDays))
            {
                continue;
            }

            total += observation.Price;
            count++;
        }

        if (count == 0)
        {
            marketReference = 0;
            return false;
        }

        marketReference = checked((int)IntegerMath.CeilDiv(total, count));
        return true;
    }

    /// <summary>
    /// 前日の約定単価 = CeilDiv( Σ(単価 × 数量), Σ(数量) )(GDD02c §1.2)。
    /// <b><see cref="LedgerDirection.Sale"/> の行だけを見る。</b>都市外市場への輸出
    /// (<see cref="HouseholdState.ExternalMarketSellerId"/> が相手)も含める ── 帳簿は
    /// 資金の増減と一致する規約(GDD02b §3)なので輸出も約定である。前日の売りが1件も
    /// 無ければ false(0を返さない)。
    /// </summary>
    /// <remarks>
    /// <b>末尾から走査し、前日より前に達したら打ち切る</b>(W2-20 タスク仕様「性能 ── 帳簿の
    /// 全走査を消す」)。帳簿は追記専用で <see cref="LedgerEntry.OccurredAt"/> が非減少なので、
    /// この打ち切りは結果を変えない。段1 が毎日・全世帯について呼ぶため、先頭からの全走査だと
    /// 帳簿の行数に比例した仕事が毎日発生し、36,000日 では日数の2乗になる。
    /// </remarks>
    public static bool TryPreviousDaySettledPrice(
        IReadOnlyList<LedgerEntry> ledger, int itemId, Tick now, out int settledPrice)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        long previousDayIndex = now.DayIndex - 1;
        long totalPayment = 0;
        long totalQuantity = 0;

        for (int i = ledger.Count - 1; i >= 0; i--)
        {
            var entry = ledger[i];
            long entryDayIndex = entry.OccurredAt.DayIndex;

            if (entryDayIndex < previousDayIndex)
            {
                // 帳簿は非減少なので、これより前の行はすべて前日より古い(打ち切ってよい)。
                break;
            }

            if (entryDayIndex != previousDayIndex)
            {
                // 当日ぶん(呼び出し時点でまだ無いはずだが、将来コマンドが割り込む経路に
                // 備えて安全側に倒す)。打ち切らずスキップする。
                continue;
            }

            if (entry.Direction != LedgerDirection.Sale || entry.ItemId != itemId)
            {
                continue;
            }

            totalPayment += (long)entry.UnitPrice * entry.Quantity;
            totalQuantity += entry.Quantity;
        }

        if (totalQuantity <= 0)
        {
            settledPrice = 0;
            return false;
        }

        settledPrice = checked((int)IntegerMath.CeilDiv(totalPayment, totalQuantity));
        return true;
    }

    /// <summary>
    /// 有効な観測の判定(売り手・買い手で共通、GDD02c §1.2)。
    /// <b>1か所に集約する</b> ── 2つの関数へ写すと、保持期間・当日の除外・自分の売り注文の
    /// 除外のどれかが片側だけずれても緑のまま通る(売り手と買い手の速さの区別(§1.2)が壊れる。
    /// 買い手を遅くするのは公比の上限(1.173/日)を作るためであり、水準の歯止めは §1.1 が持つ)。
    /// </summary>
    /// <remarks>
    /// <b>判定は <see cref="Tick.DayIndex"/> の差で行う。</b>時刻(<see cref="Tick.HourOfDay"/>)は
    /// 見ない ── 順5 は <c>Daily(hour: 0)</c> なので当日の観測は通常存在しないが、コマンドは
    /// 任意の tick に割り込むため tick 差で書くと「同日の午後の観測」が前日扱いになりうる。
    /// </remarks>
    private static bool IsValid(
        PriceObservation observation, int itemId, int selfHouseholdId, Tick now, int retentionDays)
    {
        if (observation.ItemId != itemId)
        {
            return false;
        }

        // 自分の売り注文は自分の観測に入れない(GDD06 §3.1)。
        if (observation.SellerId == selfHouseholdId)
        {
            return false;
        }

        long dayDifference = now.DayIndex - observation.ObservedAt.DayIndex;

        // 記憶は前日まで。当日(差0)を使うと日内の相互参照が復活する。
        // 保持期間ちょうど(差 == retentionDays)は入る。
        return dayDifference >= 1 && dayDifference <= retentionDays;
    }
}
