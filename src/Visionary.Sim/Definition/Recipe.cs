using Visionary.Sim.Numerics;

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
/// GDD11 の貿易商と GDD12 の農村職業が戻ったときに発火する分岐(GDD02a §5 の原価の
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

    /// <summary>所要労働‰。1000‰ = 親方1人日相当(GDD02a §2)。</summary>
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
                nameof(laborPermille), laborPermille, "所要労働‰は1以上(GDD02a §2)。");
        }

        Occupation = occupation;

        // 渡された配列は複製して持つ(仕様)。呼び出し側が検証後に元配列を書き換える経路を塞ぐ。
        Outputs = outputs.ToArray();
        Inputs = inputs.ToArray();
        LaborPermille = laborPermille;
    }

    /// <summary>
    /// 生産能力(実行回数)= floor( floor(労働力合計‰ × 設備係数‰ ÷ 1000) ÷ 所要労働‰ )(GDD02a §1)。
    /// </summary>
    /// <remarks>
    /// <b>両方の除算を切り下げる。</b>端数の労働力ではレシピを1回完成できない —
    /// <see cref="IntegerMath.ApplyPermille"/>(切り上げ)を内側に使うと存在しない労働力で
    /// 生産したことになる(GDD02a §1 の「切り上げ規約の意図的な例外」)。
    /// <see cref="WorldDefinition"/>(目標在庫の物差し。進捗‰ を持たない世界基準の生産能力)
    /// だけがこのメソッドを呼ぶ ── #237 で日次の実行回数は進捗‰(<see cref="CapacityRunsFromProgress"/>)
    /// 側に移った。
    /// <para>
    /// <b>内側の切り下げ(floor(労働力合計‰ × 設備係数‰ ÷ 1000))は
    /// <see cref="IntegerMath.FloorPermille"/> が唯一の置き場所である</b>(W2-19訂正。レビュー1巡目
    /// 象限I-a)。<see cref="Systems.LaborCapacity.EffectiveLaborPermille(int, int)"/> も同じ
    /// <see cref="IntegerMath.FloorPermille"/> を呼ぶ ── 以前は2か所に同じ式が手で綴られており、
    /// この remarks の主張(「式は1か所にしか置かない」)自体が偽だった。<b>外側の除算
    /// (÷ 所要労働‰)は <see cref="CapacityRunsFromProgress"/> にしか無い</b>(#237。中身をそちらへ
    /// 委譲する)。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="laborPermille"/> または <paramref name="equipmentPermille"/> が負のとき。
    /// </exception>
    public int CapacityRuns(int laborPermille, int equipmentPermille)
    {
        if (laborPermille < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(laborPermille), laborPermille, "労働力合計‰は非負(GDD02a §2)。");
        }

        if (equipmentPermille < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(equipmentPermille), equipmentPermille, "設備係数‰は非負(GDD02a §3)。");
        }

        // 内側の切り下げはIntegerMath.FloorPermilleへ寄せてある(戻り値long。中間の積は
        // labor×equipがintを超えうるため)。LaborCapacity.EffectiveLaborPermilleも同じ関数を呼ぶ
        // ── ここに書き直さない(W2-19訂正)。外側の除算はCapacityRunsFromProgressへ委譲する
        // (#237。物差し=「進捗‰ 0 から1日働いたときの能力」)。
        long effectiveLaborPermille = IntegerMath.FloorPermille(laborPermille, equipmentPermille);

        return CapacityRunsFromProgress(checked((int)effectiveLaborPermille));
    }

    /// <summary>生産能力(実行回数)= floor(進捗‰ ÷ 所要労働‰)(GDD02a §1)。</summary>
    /// <remarks>
    /// <b>外側の除算はここにしか無い</b>(#237)。<see cref="Systems.ProductionSystem"/> と
    /// <see cref="CapacityRuns"/> の両方がこれを呼ぶ。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="progressPermille"/> が負のとき。</exception>
    public int CapacityRunsFromProgress(int progressPermille)
    {
        if (progressPermille < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(progressPermille), progressPermille, "生産の進捗‰は非負(GDD02a §1)。");
        }

        return checked((int)IntegerMath.FloorDiv(progressPermille, LaborPermille));
    }

    /// <summary>
    /// 入力から作れる回数 = min_j floor(工房在庫[入力j] ÷ 必要数量_j)(GDD02a §1)。
    /// </summary>
    /// <remarks>
    /// <b>入力が0件のレシピは <see cref="int.MaxValue"/> を返す。</b>「この項では制約しない」の
    /// 実体である ── 空の min を 0 にすると、入力0件のレシピの生産が永久に止まる(#218 全般2巡目
    /// 指摘2)。<see cref="Systems.ProductionSystem"/> と
    /// <see cref="Systems.OccupationReassignment.IsGateOpen"/> の両方がこれを呼ぶ(#237)。
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="workshopInventory"/> が null のとき。</exception>
    public int RunsFromInputs(int[] workshopInventory)
    {
        ArgumentNullException.ThrowIfNull(workshopInventory);

        int runs = int.MaxValue;

        foreach (var input in Inputs)
        {
            int affordableRuns = IntegerMath.FloorDiv(workshopInventory[input.ItemId], input.Quantity);
            runs = Math.Min(runs, affordableRuns);
        }

        return runs;
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
    /// 「穀物2 + 穀物3 → …」が通ると、GDD02a §5 の原価 Σ_j(単価 × 数量_j) が
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
