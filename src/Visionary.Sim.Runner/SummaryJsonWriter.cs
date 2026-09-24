using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Visionary.Sim.Verification;

namespace Visionary.Sim.Runner;

/// <summary>
/// <c>summary.json</c> の書き出し(W2-21 タスク仕様「6.」)。
/// </summary>
/// <remarks>
/// <para>
/// <b><c>Verdict</c> は文字列 <c>"green"</c> / <c>"red"</c> / <c>"indeterminate"</c> で出す。</b>
/// 既定の enum 変換(<see cref="JsonStringEnumConverter"/>)は <c>"Green"</c> のように大文字始まりを
/// 返すので、明示的な変換関数(<see cref="VerdictJsonConverter"/>)を書く(タスク仕様)。
/// </para>
/// <para>
/// <b>書き出す直前に <c>\r\n</c> を <c>\n</c> へ正規化し、末尾に改行を1つ置く。</b>
/// <see cref="Utf8JsonWriter"/> の字下げがどの改行を使うかに依存しない ── CI が Ubuntu、
/// 開発機が Windows で、バイト一致の検査が壊れる(タスク仕様。W2-20 別表 #21' と同じ罠)。
/// </para>
/// <para>
/// <b>数値はすべて整数である。</b><see cref="RunSummary"/> の型がすべて int/long/enum/文字列/
/// コレクションで構成されているため、シリアライズの経路に <c>double</c> が現れない
/// (<c>Visionary.Sim.Runner</c> は <see cref="Architecture.DeterminismConventionTests"/> の
/// 守備範囲外なので、ここは機械ではなくコードの構成で守る)。
/// </para>
/// </remarks>
internal static class SummaryJsonWriter
{
    // BOM無しUTF-8(CsvMetricsSinkと同じ)。
    private static readonly UTF8Encoding NoBomUtf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new VerdictJsonConverter() },
    };

    public static void Write(string path, RunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(summary);

        string? directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            // シードが0件でも書けるように(タスク仕様「呼び出し側を持たないコード」)。
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(summary, SerializerOptions);

        // \r\n を \n へ正規化し、末尾に改行を1つ置く。
        json = json.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";

        File.WriteAllBytes(path, NoBomUtf8.GetBytes(json));
    }

    private sealed class VerdictJsonConverter : JsonConverter<Verdict>
    {
        public override Verdict Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("summary.json の読み込みは対象外(書き出し専用)。");

        public override void Write(Utf8JsonWriter writer, Verdict value, JsonSerializerOptions options)
        {
            string text = value switch
            {
                Verdict.Green => "green",
                Verdict.Red => "red",
                Verdict.Indeterminate => "indeterminate",
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, "未知のVerdict。"),
            };

            writer.WriteStringValue(text);
        }
    }
}
