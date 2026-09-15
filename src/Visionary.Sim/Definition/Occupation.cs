namespace Visionary.Sim;

/// <summary>
/// 職業(GDD02 §2.4)。
/// </summary>
/// <remarks>
/// <b>値は GDD02 §2.4 の採番そのもの</b>(§6.3 と GDD08 §9 の「同値なら職業 Id 昇順」が
/// この値に依存する)。<see cref="Randomness.RandomStream"/> /
/// <see cref="Determinism.StateHasher"/> の区分タグと同じく<b>振り直してはならない</b>。
/// <see cref="HouseholdState"/> の欄として比較・代入される値であって配列の添字ではないため
/// <c>enum</c> にする(<see cref="Item"/> の doc の「分ける基準」を参照)。
/// </remarks>
public enum Occupation
{
    /// <summary>水車小屋番。穀物 → 小麦粉。</summary>
    Miller = 0,

    /// <summary>パン屋。小麦粉 + 薪 → パン。</summary>
    Baker = 1,

    /// <summary>醸造。穀物 + 薪 → ビール。</summary>
    Brewer = 2,

    /// <summary>木材加工。木材 → 薪。</summary>
    Woodworker = 3,

    /// <summary>鍛冶。鉄鉱石 + 木炭 → 工具。</summary>
    Smith = 4,
}
