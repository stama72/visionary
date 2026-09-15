namespace Visionary.Sim;

/// <summary>
/// 都市の区画と距離(GDD02 §4.3)。
/// </summary>
/// <remarks>
/// <b>状態ではない</b>(TDD01 §3.8)。品目表・レシピ表・労働力係数と同じく、
/// <see cref="World"/> には載らずハッシュにも乗らない。
/// <para>
/// <b>区画数を <see cref="WorldDefinition"/> の可変値にしない。</b>距離の式が 3×3 を
/// 前提にしているので、可変にすると式が黙って無視する設定値が生まれる。GDD02 §4.3 は
/// 3×3 を決定として書いている。
/// </para>
/// <para><b>距離表を持たない。</b>Id から行・列が定まる(GDD02 §4.3)。</para>
/// </remarks>
public static class District
{
    /// <summary>グリッドの一辺(GDD02 §4.3 の 3×3)。</summary>
    public const int GridSide = 3;

    /// <summary>区画数。</summary>
    public const int Count = GridSide * GridSide;

    /// <summary>都市外市場は中心の区画に固定(GDD02 §4.3・§10)。</summary>
    public const int ExternalMarketDistrictId = 4;

    /// <summary>行(0〜<see cref="GridSide"/>-1)。区画Idは行優先(GDD02 §4.3)。</summary>
    public static int RowOf(int districtId)
    {
        ValidateDistrictId(districtId, nameof(districtId));

        // 切り上げ規約(CLAUDE.md)の対象外 — グリッドの行の算出であって金額でも比率でもない。
        return districtId / GridSide;
    }

    /// <summary>列(0〜<see cref="GridSide"/>-1)。区画Idは行優先(GDD02 §4.3)。</summary>
    public static int ColumnOf(int districtId)
    {
        ValidateDistrictId(districtId, nameof(districtId));

        return districtId % GridSide;
    }

    /// <summary>
    /// 2区画間のマンハッタン距離(0〜4)。GDD06 §2 の実質コストに乗る唯一の空間の摩擦の
    /// 数値表現(GDD02 §4.3)。
    /// </summary>
    public static int Distance(int fromDistrictId, int toDistrictId)
    {
        ValidateDistrictId(fromDistrictId, nameof(fromDistrictId));
        ValidateDistrictId(toDistrictId, nameof(toDistrictId));

        int rowDelta = Math.Abs(RowOf(fromDistrictId) - RowOf(toDistrictId));
        int columnDelta = Math.Abs(ColumnOf(fromDistrictId) - ColumnOf(toDistrictId));

        return rowDelta + columnDelta;
    }

    /// <summary>
    /// 区画Idの値域(0〜<see cref="Count"/>-1)を検査する。検査しないと
    /// <c>RowOf(9) == 3</c> という存在しない行が返り、距離が黙って 0〜4 の外に出る。
    /// </summary>
    private static void ValidateDistrictId(int districtId, string paramName)
    {
        if (districtId < 0 || districtId >= Count)
        {
            throw new ArgumentOutOfRangeException(
                paramName, districtId, $"区画Idは0〜{Count - 1}(GDD02 §4.3)。");
        }
    }
}
