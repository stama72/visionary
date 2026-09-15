namespace Visionary.Sim.Tests.Definition;

/// <summary>
/// <see cref="District"/>(GDD02 §4.3)の検査。
/// </summary>
public sealed class DistrictTests
{
    /// <summary>
    /// マンハッタン距離は転置に対して不変なので、行優先か列優先かは <c>Distance</c> では
    /// 判別できない。<c>RowOf</c> / <c>ColumnOf</c> を直接見るのはこのテストだけの役目
    /// (タスク仕様「距離のテストで行優先は守れない」)。
    /// </summary>
    [Fact]
    public void DistrictIdIsRowMajor()
    {
        Assert.Equal(1, District.RowOf(5));
        Assert.Equal(2, District.ColumnOf(5));

        Assert.Equal(1, District.RowOf(3));
        Assert.Equal(0, District.ColumnOf(3));

        Assert.Equal(0, District.RowOf(2));
        Assert.Equal(2, District.ColumnOf(2));
    }

    /// <summary>
    /// 距離はマンハッタン距離であって、チェビシェフ距離(行と列の差の max)ではない。
    /// </summary>
    [Fact]
    public void DistanceIsManhattanNotChebyshev()
    {
        Assert.Equal(4, District.Distance(0, 8));
        Assert.Equal(4, District.Distance(2, 6));
        Assert.Equal(2, District.Distance(0, 2));
        Assert.Equal(0, District.Distance(4, 4));
        Assert.Equal(2, District.Distance(1, 7));

        for (int a = 0; a < District.Count; a++)
        {
            for (int b = 0; b < District.Count; b++)
            {
                int distance = District.Distance(a, b);

                Assert.Equal(distance, District.Distance(b, a));
                Assert.InRange(distance, 0, 4);
            }
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void DistrictHelpersRejectIdsOutsideTheGrid(int invalidDistrictId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => District.RowOf(invalidDistrictId));
        Assert.Throws<ArgumentOutOfRangeException>(() => District.ColumnOf(invalidDistrictId));
        Assert.Throws<ArgumentOutOfRangeException>(() => District.Distance(invalidDistrictId, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => District.Distance(0, invalidDistrictId));
    }
}
