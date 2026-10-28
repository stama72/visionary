namespace Visionary.Sim.Systems;

/// <summary>
/// 購入を Need から引く規則(GDD02b §8.1 の表)。夕方の段8(<see cref="TradeSystem"/>)と
/// プレイヤーの販売(<see cref="PlayerSale"/>)の両方がここを通る ── 対応表が2か所にあると、
/// 片方だけ直って食い違う。
/// </summary>
public static class NeedDeduction
{
    /// <summary>
    /// その理由の Need から、その用途の購入を引くか(GDD02b §8.1 の表)。
    /// 生産停止 ← 生産の入力 / 工具切れ ← 耐久 / 困窮 ← 必需 / 遠方在庫 ← 4用途すべて /
    /// 増産できない ← どれも引かない(‰人日で、買い物では埋まらない)。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">未知の理由または用途。</exception>
    public static bool Subtracts(NeedReason reason, DemandPurpose purpose)
    {
        if (purpose is not (DemandPurpose.ProductionInput or DemandPurpose.Necessity
            or DemandPurpose.Preference or DemandPurpose.Durable))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "未知の用途。");
        }

        return reason switch
        {
            NeedReason.ProductionStopped => purpose == DemandPurpose.ProductionInput,
            NeedReason.ToolsExhausted => purpose == DemandPurpose.Durable,
            NeedReason.Distress => purpose == DemandPurpose.Necessity,
            NeedReason.DistantStock => true,
            NeedReason.CannotExpandProduction => false,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "未知の理由コード(GDD02b §8)。"),
        };
    }

    /// <summary>
    /// 1件の購入を、その世帯の Need から引く。<see cref="World.Needs"/> を<b>格納順のまま</b>走査し、
    /// 世帯と品目が一致し <see cref="Subtracts"/> が真の要素の Quantity から <paramref name="quantity"/>
    /// を引く。0 以下になった要素は <c>RemoveAll</c> で消す(格納順は保たれる)。
    /// <c>NextNeedId</c> には触らない。同じ購入を複数の Need から引く(購入を Need の間で配分しない)。
    /// </summary>
    public static void SubtractPurchase(
        World world, int householdId, DemandPurpose purpose, int itemId, int quantity)
    {
        ArgumentNullException.ThrowIfNull(world);

        for (int i = 0; i < world.Needs.Count; i++)
        {
            var need = world.Needs[i];

            if (need.TargetHouseholdId != householdId
                || need.ItemId != itemId
                || !Subtracts(need.ReasonCode, purpose))
            {
                continue;
            }

            world.Needs[i] = need with { Quantity = need.Quantity - quantity };
        }

        world.Needs.RemoveAll(static need => need.Quantity <= 0);
    }
}
