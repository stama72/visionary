using System.Globalization;
using System.Text;

namespace Visionary.Sim.Dialogue;

/// <summary>台詞の数量に使う漢数字(タスク仕様 W5-01 §4)。</summary>
internal static class KanjiNumerals
{
    private const string Digits = "〇一二三四五六七八九";
    private const int ArabicFrom = 10000;   // これ以上は算用数字

    /// <summary>
    /// 1〜9999 は千・百・十の位を「数字 + 位」で並べる。位の数字が 1 なら数字を省き、0 なら位ごと省く。
    /// 一の位は 0 なら省く。10000 以上は算用数字。
    /// </summary>
    public static string Format(int value)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "数量は1以上。");
        }

        if (value >= ArabicFrom)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        var sb = new StringBuilder();
        AppendPlace(sb, value / 1000 % 10, '千');
        AppendPlace(sb, value / 100 % 10, '百');
        AppendPlace(sb, value / 10 % 10, '十');

        int ones = value % 10;

        if (ones != 0)
        {
            sb.Append(Digits[ones]);
        }

        return sb.ToString();
    }

    private static void AppendPlace(StringBuilder sb, int digit, char place)
    {
        if (digit == 0)
        {
            return;
        }

        if (digit != 1)
        {
            sb.Append(Digits[digit]);
        }

        sb.Append(place);
    }
}
