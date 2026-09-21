namespace Visionary.Sim.Systems;

/// <summary>
/// 販売在庫(GDD02c §1.3)。工房在庫から、自分の生産の必要財ぶんを除いた量。
/// </summary>
/// <remarks>
/// <para>
/// <b>5経路すべてがここを通る</b>(#148 追随表。W2-14 タスク仕様)── 売り注文(段1)・輸出(段6)・
/// 購入量の切り詰め(段5b)・店の候補(<see cref="StoreChoice"/>)・約定の番人
/// (<see cref="TradeSettlement"/>)。どれか1つでも工房在庫を直読みすると、留保は素通りする。
/// </para>
/// <para>
/// <b><c>Recipe</c> ではなく <c>(definition, household)</c> を取る。</b>呼び出し側が
/// 「別の世帯のレシピ」を渡す取り違えを型の手前で消すためである(規則7「添字・並び・単位の
/// 約束」)。職業は世帯の<b>現在の</b>値から引く(<c>definition.Recipes[(int)household.Occupation]</c>)
/// ── #39 の職業付け替えで変わる。<b>ただしこれは呼び出し側が工房在庫を直接読むことを
/// 防げない</b>(規則4)。そちらは <see cref="TradeSettlement"/> の番人が受け持つ。
/// </para>
/// </remarks>
public static class SellableStock
{
    /// <summary>
    /// 留保量(GDD02c §1.3)。単位: 個。上から順に、最初に当たった枝で決める。
    /// <list type="bullet">
    /// <item>設備: <paramref name="itemId"/> が <see cref="Item.Tools"/> →
    /// <see cref="ProductionSystem.EquipmentThresholdStock"/>(= 1)。</item>
    /// <item>入力: <paramref name="itemId"/> が現在のレシピの入力に含まれる → その数量(1回分)。</item>
    /// <item>どちらでもない → 0。</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><paramref name="itemId"/> が売り手の出力品目かどうかは見ない。</b>見なくても結果は
    /// 同じ(売り注文は出力品目にしか立たない)うえ、見ると「出力でない品目の工房在庫を売る
    /// 経路」が将来生えたときに留保が黙って外れる。
    /// </para>
    /// <para>
    /// <b>設備と入力の両方に当たる品目は M0 に無いので、合算しない。</b>上の分岐は排他である。
    /// 該当する品目が現れたら、合算するか否かは GDD02c §1.3 に決めてから実装する。
    /// </para>
    /// <para>
    /// <b>入力の枝は M0 では発火しない</b>(出力を自分の入力に使うレシピが無い)。#148 の決定3が
    /// 「規則としては置く」と決めたので実装している(合成レシピを使うテストが踏む)。
    /// </para>
    /// </remarks>
    public static int ReserveQuantity(WorldDefinition definition, HouseholdState household, int itemId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(household);

        if (itemId == Item.Tools)
        {
            return ProductionSystem.EquipmentThresholdStock;
        }

        var recipe = definition.Recipes[(int)household.Occupation];

        foreach (var input in recipe.Inputs)
        {
            if (input.ItemId == itemId)
            {
                return input.Quantity;
            }
        }

        return 0;
    }

    /// <summary>販売在庫 = max(0, 工房在庫[<paramref name="itemId"/>] − 留保量)。単位: 個。</summary>
    public static int Of(WorldDefinition definition, HouseholdState household, int itemId)
    {
        ArgumentNullException.ThrowIfNull(household);

        int reserve = ReserveQuantity(definition, household, itemId);

        return Math.Max(0, household.WorkshopInventory[itemId] - reserve);
    }
}
