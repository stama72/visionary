using Visionary.Sim.Time;

namespace Visionary.Sim.Systems;

/// <summary>
/// 自家消費(GDD02b §1.1)。自分の生産物を工房在庫から世帯在庫へ移す量。
/// </summary>
public static class SelfConsumption
{
    /// <summary>
    /// 目標日数 = 必需の日数 + 嗜好の日数(GDD02b §2)。単位: 日。
    /// </summary>
    /// <remarks>
    /// <b>必需と嗜好を足すのは、世帯在庫が1本の在庫だからである。</b>M0 には両方が正の品目が
    /// 無い(パン・薪は必需のみ、ビールは嗜好のみ)ので、この加算が発火するのは合成の
    /// <see cref="WorldDefinition"/> を使うテストだけである。規則としては置く ──
    /// <see cref="SellableStock.ReserveQuantity"/> の入力の枝と同じ扱い(GDD02b §1.1)。
    /// </remarks>
    public static int TargetStockDays(WorldDefinition definition, int itemId)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return definition.NecessityTargetStockDays[itemId] + definition.PreferenceTargetStockDays[itemId];
    }

    /// <summary>
    /// 移動量 = min( 販売在庫 , max( 0 , 目標在庫 + 今日の消費量 − 世帯在庫 ) )。単位: 個。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>今日の消費量に <see cref="DailyConsumption.Lookahead"/> ではなく
    /// <see cref="DailyConsumption.Quantity"/> を使う。</b>呼び出し側(<see cref="ConsumptionSystem"/>)が
    /// 1日1回だけ解いた <paramref name="season"/> を渡すことで、移す量と同じ日に実際に引かれる量が
    /// 同一の値であることを引数で保証する。<c>Lookahead</c> は <c>world.Now</c> から季節を解き直すので、
    /// 値は同じでも保証が消える(GDD02b §1.1)。
    /// </para>
    /// <para>
    /// <b>目標日数が0の品目を早期 return で弾かない。</b>弾かなくても式が0を返す
    /// (目標在庫0 + 今日の消費量0 − 世帯在庫 ≥ 0 の <c>max(0, …)</c>)。小麦粉と工具が移らないのは
    /// 式の帰結である。
    /// </para>
    /// <para>
    /// <b><see cref="SellableStock.Of"/> を通す。</b><c>household.WorkshopInventory[itemId]</c> を
    /// 直読みしない(GDD02c §1.3「自家消費も販売在庫から取る」)。M0 の対象3品目は留保量が0なので
    /// 値は変わらないが、留保が値を持つ品目が現れた日に黙って外れるのを防ぐために通す。
    /// <b>この保証には穴がある</b>: 呼び出し側が工房在庫を直読みする経路は型では防げない
    /// (<see cref="SellableStock"/> の doc コメントが既に書いている穴と同じもの)。
    /// </para>
    /// </remarks>
    public static int TransferQuantity(
        WorldDefinition definition, World world, HouseholdState household, int itemId, Season season)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(household);

        int targetStock = DailyConsumption.Lookahead(
            definition, world, household, itemId, world.Now, TargetStockDays(definition, itemId));
        int todaysConsumption = DailyConsumption.Quantity(definition, world, household, itemId, season);
        int householdStock = household.HouseholdInventory[itemId];
        int sellableStock = SellableStock.Of(definition, household, itemId);

        int required = Math.Max(0, targetStock + todaysConsumption - householdStock);

        return Math.Min(sellableStock, required);
    }
}
