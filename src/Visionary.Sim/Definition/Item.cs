namespace Visionary.Sim;

/// <summary>
/// 品目 Id(GDD02 §2.2 の表の順に 0〜8)。
/// </summary>
/// <remarks>
/// <b>品目を <c>enum</c> にしないのは、品目 Id が配列の添字だからである。</b>在庫は
/// <c>int[]</c>(添字 = itemId)なので、<c>enum</c> にすると在庫に触れるたび <c>(int)</c> の
/// キャストが入る。<b>職業は逆に <c>enum</c> にする</b>(<see cref="Occupation"/>)—
/// あちらは世帯の欄として比較・代入される値であって添字ではない。
/// <b>分ける基準は「添字か、値か」である。</b>
/// </remarks>
public static class Item
{
    public const int Grain = 0;     // 穀物。1次・都市外市場
    public const int Timber = 1;    // 木材。1次・都市外市場
    public const int IronOre = 2;   // 鉄鉱石。1次・都市外市場
    public const int Charcoal = 3;  // 木炭。1次・都市外市場(金属加工専用)
    public const int Flour = 4;     // 小麦粉。中間
    public const int Firewood = 5;  // 薪。中間。必需 + 生産入力
    public const int Bread = 6;     // パン。最終・必需
    public const int Beer = 7;      // ビール。最終・嗜好
    public const int Tools = 8;     // 工具。最終・耐久(GDD02 §5.3)

    /// <summary>品目数。</summary>
    public const int Count = 9;
}
