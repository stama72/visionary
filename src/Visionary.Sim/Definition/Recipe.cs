namespace Visionary.Sim;

/// <summary>品目と数量の組(GDD02 §2.3)。レシピの入力・出力に使う。</summary>
public readonly record struct ItemQuantity
{
    /// <summary>
    /// 品目 Id。<b>値域(0〜8)は <see cref="Recipe"/> では検査しない。</b>品目数を知っているのは
    /// <see cref="WorldDefinition"/> なので、範囲検査はそちらが持つ。
    /// </summary>
    public int ItemId { get; init; }

    /// <summary>数量(個)。1以上。</summary>
    public int Quantity { get; init; }
}

/// <summary>
/// 職業ごとのレシピ(GDD02 §2.3・§2.4)。
/// </summary>
/// <remarks>
/// <b>入力0件を許すのは GDD02 §2.3 の決定である。</b>M0 の5職業はすべて財の投入を持つが、
/// GDD11 の貿易商と GDD12 の農村職業が戻ったときに発火する分岐(GDD02 §8.1.1 の原価の
/// 2分岐)がこれに対応する。構造としては残すが、M0 で通る経路ではない。
/// </remarks>
public sealed class Recipe
{
    /// <summary>このレシピを持つ職業。</summary>
    public Occupation Occupation { get; }

    /// <summary>出力。1件以上、複数出力を許す(GDD02 §2.3)。</summary>
    public ItemQuantity[] Outputs { get; }

    /// <summary>入力。0件を許す(GDD02 §2.3。M0 に該当する職業は無い)。</summary>
    public ItemQuantity[] Inputs { get; }

    /// <summary>所要労働‰。1000‰ = 親方1人日相当(GDD02 §5.2)。</summary>
    public int LaborPermille { get; }

    public Recipe(Occupation occupation, ItemQuantity[] outputs, ItemQuantity[] inputs, int laborPermille)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(inputs);

        if (outputs.Length == 0)
        {
            throw new ArgumentException("出力は1件以上(GDD02 §2.3)。", nameof(outputs));
        }

        ValidateQuantities(outputs, nameof(outputs));
        ValidateQuantities(inputs, nameof(inputs));
        ValidateNoDuplicateItemIds(outputs, nameof(outputs));
        ValidateNoDuplicateItemIds(inputs, nameof(inputs));

        if (laborPermille < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(laborPermille), laborPermille, "所要労働‰は1以上(GDD02 §5.2)。");
        }

        Occupation = occupation;

        // 渡された配列は複製して持つ(仕様)。呼び出し側が検証後に元配列を書き換える経路を塞ぐ。
        Outputs = outputs.ToArray();
        Inputs = inputs.ToArray();
        LaborPermille = laborPermille;
    }

    private static void ValidateQuantities(ItemQuantity[] items, string paramName)
    {
        foreach (var item in items)
        {
            if (item.Quantity < 1)
            {
                throw new ArgumentOutOfRangeException(paramName, item.Quantity, "数量は1以上。");
            }
        }
    }

    /// <summary>
    /// 同じ <see cref="ItemQuantity.ItemId"/> が2度現れることを拒む。
    /// </summary>
    /// <remarks>
    /// 「穀物2 + 穀物3 → …」が通ると、GDD02 §8.1.1 の原価 Σ_j(単価 × 数量_j) が
    /// 同じ品目を2度数えることになる。
    /// </remarks>
    private static void ValidateNoDuplicateItemIds(ItemQuantity[] items, string paramName)
    {
        for (int i = 0; i < items.Length; i++)
        {
            for (int j = i + 1; j < items.Length; j++)
            {
                if (items[i].ItemId == items[j].ItemId)
                {
                    throw new ArgumentException(
                        $"同じ品目Id({items[i].ItemId})が2度現れている。", paramName);
                }
            }
        }
    }
}
