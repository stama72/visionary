using Visionary.Sim.Numerics;

namespace Visionary.Sim.Tests.Numerics;

public sealed class IntegerMathTests
{
    [Theory]
    [InlineData(6, 2, 3)]     // 割り切れる
    [InlineData(7, 2, 4)]     // 正: 切り上がる
    [InlineData(1, 1000, 1)]  // 正の端数は必ず1以上になる(0デルタを作らない)
    [InlineData(0, 5, 0)]
    public void CeilDivRoundsUpForPositiveOperands(int dividend, int divisor, int expected)
    {
        Assert.Equal(expected, IntegerMath.CeilDiv(dividend, divisor));
    }

    /// <summary>
    /// C# の整数除算は0方向への切り捨てなので、数学的 ceiling との差が符号で変わる。
    /// −7/2 は既に −3 で正しく、補正してはならない。
    /// </summary>
    [Theory]
    [InlineData(-7, 2, -3)]
    [InlineData(7, -2, -3)]
    [InlineData(-7, -2, 4)]
    [InlineData(-6, 2, -3)]
    public void CeilDivMatchesMathematicalCeilingForNegatives(
        int dividend, int divisor, int expected)
    {
        Assert.Equal(expected, IntegerMath.CeilDiv(dividend, divisor));
    }

    /// <summary>
    /// 素朴な <c>(a + b - 1) / b</c> が誤る条件を記録に残す。このヘルパーが存在する理由そのもの。
    /// 誤るのは「負で割り切れる」場合で、素朴な式は切り上げるべきでないところで切り上げる。
    /// (−7/2 のような端数のある負数では偶然一致するため、そちらでは検出できない)
    /// </summary>
    [Fact]
    public void NaiveAddDivisorMinusOneIdiomIsWrongForExactNegativeDivision()
    {
        const int dividend = -6;
        const int divisor = 2;

        Assert.Equal(-2, (dividend + divisor - 1) / divisor); // 素朴な式: 誤り
        Assert.Equal(-3, IntegerMath.CeilDiv(dividend, divisor)); // 正: ceiling(-3.0) = -3
    }

    [Fact]
    public void CeilDivThrowsOnZeroDivisor()
    {
        Assert.Throws<DivideByZeroException>(() => IntegerMath.CeilDiv(1, 0));
    }

    [Fact]
    public void CeilDivThrowsOnOverflow()
    {
        Assert.Throws<OverflowException>(() => IntegerMath.CeilDiv(long.MinValue, -1));
    }

    [Theory]
    [InlineData(100, 200, 20)]   // 20%
    [InlineData(101, 200, 21)]   // 20.2 → 切り上げ
    [InlineData(1, 1, 1)]        // 0.001 → 切り上げで1。0にしない
    [InlineData(100, 1000, 100)] // 100%
    [InlineData(-100, 200, -20)]
    public void ApplyPermilleRoundsUp(int value, int permille, int expected)
    {
        Assert.Equal(expected, IntegerMath.ApplyPermille(value, permille));
    }

    /// <summary>
    /// 中間の積を long で計算していること。int で書くと桁あふれして符号が反転する。
    /// </summary>
    [Fact]
    public void ApplyPermilleUsesLongIntermediateProduct()
    {
        const int value = 2_000_000;
        const int permille = 2_000; // 200%

        Assert.Equal(4_000_000, IntegerMath.ApplyPermille(value, permille));
        Assert.True(unchecked(value * permille) < 0, "int の積は桁あふれする前提のテスト");
    }

    [Fact]
    public void ApplyPermilleThrowsWhenResultExceedsInt()
    {
        Assert.Throws<OverflowException>(() => IntegerMath.ApplyPermille(int.MaxValue, 2_000));
    }
}
