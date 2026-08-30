namespace Visionary.Sim.Numerics;

/// <summary>
/// 決定論を保つための整数演算(ADR-0002)。浮動小数点を経由せずに
/// GDD01 §2.3「全計算式の結果は小数点以下切り上げ」を実現する。
/// </summary>
public static class IntegerMath
{
    /// <summary>‰(千分率)の分母。係数は <c>alphaPermille = 200</c> のように整数で持つ(= 20%)。</summary>
    public const int PermilleScale = 1000;

    /// <summary>
    /// 数学的な切り上げ除算(ceiling)。<c>7 / 2 = 4</c>、<c>-7 / 2 = -3</c>。
    /// </summary>
    /// <remarks>
    /// C# の <c>/</c> は0方向への切り捨てなので、符号によって補正の要否が変わる。
    /// 商が正のときだけ +1 する必要がある。よく使われる <c>(a + b - 1) / b</c> は
    /// 正数専用で、負で割り切れる場合(<c>-6 / 2</c>)に切り上げるべきでないところで
    /// 切り上げてしまう。これがこのヘルパーが存在する理由。
    /// </remarks>
    /// <exception cref="DivideByZeroException">除数が0のとき。</exception>
    /// <exception cref="OverflowException"><c>long.MinValue / -1</c> のとき。</exception>
    public static long CeilDiv(long dividend, long divisor)
    {
        if (divisor == 0)
        {
            throw new DivideByZeroException("切り上げ除算の除数が0。");
        }

        long quotient = checked(dividend / divisor);
        long remainder = dividend % divisor;

        // 割り切れず、かつ商が正(被除数と除数が同符号)のときだけ切り上がる
        bool roundsUp = remainder != 0 && ((dividend ^ divisor) >= 0);

        return roundsUp ? checked(quotient + 1) : quotient;
    }

    /// <inheritdoc cref="CeilDiv(long, long)"/>
    public static int CeilDiv(int dividend, int divisor) =>
        checked((int)CeilDiv((long)dividend, divisor));

    /// <summary>
    /// ‰係数を適用して切り上げる。<c>value × permille / 1000</c> の頻出パターンを1関数に閉じる。
    /// </summary>
    /// <remarks>
    /// 中間の積は <see cref="long"/> で計算する。<c>value × permille</c> は
    /// int の範囲を容易に超えるため(例: 200万 × 200‰)、呼び出し側で書くと桁あふれしやすい。
    /// 演算順序を間違えて先に除算してしまう経路(<c>value / 1000 * permille</c>)も同時に塞ぐ。
    /// </remarks>
    /// <exception cref="OverflowException">結果が int に収まらないとき。</exception>
    public static int ApplyPermille(int value, int permille) =>
        checked((int)CeilDiv((long)value * permille, PermilleScale));
}
