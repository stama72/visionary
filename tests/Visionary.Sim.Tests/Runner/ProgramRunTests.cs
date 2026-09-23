using System.Globalization;
using Visionary.Sim.Runner;

namespace Visionary.Sim.Tests.Runner;

/// <summary>
/// <c>vsim run</c>(<see cref="Program.Execute"/>、W2-20 タスク仕様のテスト表 #20〜#23)の検査。
/// </summary>
/// <remarks>
/// <see cref="Program.Execute"/> は <c>Program.Main</c> の実体であり、テストは
/// <c>InternalsVisibleTo</c>(<c>src/Visionary.Sim.Runner/AssemblyInfo.cs</c>)経由でプロセスを
/// 起動せずに直接呼ぶ。1テストごとに一時ディレクトリを1つ持ち、終了時に削除する。
/// </remarks>
public sealed class ProgramRunTests : IDisposable
{
    private readonly string _workDirectory;

    public ProgramRunTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "vsim-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (IOException)
        {
            // 後始末の失敗はテスト結果に影響させない(OSの一時ファイル掃除に任せる)。
        }
    }

    private string WriteConfig(string json)
    {
        string path = Path.Combine(_workDirectory, "config.json");
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>
    /// #20。<c>WorldDefinition.M0</c> を36,000日回し、CSVの書き出しまで含めて60秒以内。
    /// </summary>
    /// <remarks>
    /// <b>着手時点では空振りに近い(タスク仕様の注記)。</b>着手時点の master では経済が
    /// 20〜30日で止まるので、36,000日 の大半は1日あたりの仕事がほぼ0になる(実測:
    /// 1,600日 が85ミリ秒)。「60秒以内で通った」ことは、経済が生きた状態で通ることを
    /// 意味しない。性能を実際に守っているのは #19
    /// (<see cref="Visionary.Sim.Tests.Systems.MarketReferenceTests.PreviousDaySettledPriceStopsAtTheSecondDay"/>)
    /// のほうであり、本テストは完了条件の写しとして置く。
    /// </remarks>
    [Fact]
    public void LongRunFinishesWithinTheBudget()
    {
        string configPath = WriteConfig(
            """
            { "masterSeeds": [1], "durationDays": 36000, "world": { "preset": "m0-small" } }
            """);
        string outDirectory = Path.Combine(_workDirectory, "out");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        int exitCode = Program.Execute(new[] { "run", "--config", configPath, "--out", outDirectory });
        stopwatch.Stop();

        Assert.Equal(0, exitCode);
        Assert.True(
            stopwatch.Elapsed.TotalSeconds < 60,
            $"36,000日の走行(CSV書き出しを含む)に{stopwatch.Elapsed.TotalSeconds:F1}秒かかった。");
        Assert.True(File.Exists(Path.Combine(outDirectory, "1", "economy.csv")));
    }

    /// <summary>#21。同じ設定で <c>vsim run</c> を2回走らせ、5つのCSVがバイト一致する。</summary>
    [Fact]
    public void RunIsByteIdenticalAcrossRuns()
    {
        string configPath = WriteConfig(
            """
            { "masterSeeds": [1, 2], "durationDays": 5, "world": { "preset": "m0-small" } }
            """);

        string outA = Path.Combine(_workDirectory, "outA");
        string outB = Path.Combine(_workDirectory, "outB");

        Assert.Equal(0, Program.Execute(new[] { "run", "--config", configPath, "--out", outA }));
        Assert.Equal(0, Program.Execute(new[] { "run", "--config", configPath, "--out", outB }));

        foreach (long seed in new long[] { 1, 2 })
        {
            foreach (string fileName in
                new[] { "economy.csv", "prices.csv", "districts.csv", "households.csv", "trades.csv" })
            {
                byte[] bytesA = File.ReadAllBytes(
                    Path.Combine(outA, seed.ToString(System.Globalization.CultureInfo.InvariantCulture), fileName));
                byte[] bytesB = File.ReadAllBytes(
                    Path.Combine(outB, seed.ToString(System.Globalization.CultureInfo.InvariantCulture), fileName));

                Assert.Equal(bytesA, bytesB);
            }
        }
    }

    /// <summary>
    /// #21'(上の#21を補強。レビュー1巡目 I-a の訂正)。カルチャの表記差(負号・小数点)が
    /// 出力に混ざらないこと、出力バイトに <c>\r\n</c> が現れないことを見る。
    /// </summary>
    /// <remarks>
    /// <b>上の #21 は同一プロセス内で2回走らせて比べるだけなので、列挙順の破れ・カルチャ依存の
    /// 整形・改行のプラットフォーム依存のどれも検出しない。</b>2回とも同じカルチャ・同じ改行規約で
    /// 走るので、3モードとも素通りする(1巡目 I-a)。<see cref="Visionary.Sim.Tests.Architecture
    /// .DeterminismConventionTests"/> が見るのは <c>Visionary.Sim</c> だけで <c>Runner</c> の
    /// 整形規約は守備範囲外なので、ここが唯一の機械になる。
    /// <para>
    /// <b><c>sv-SE</c> のような実在のカルチャ名は使えない。</b><c>Directory.Build.props</c> が
    /// <c>InvariantGlobalization</c> を有効にしており(ADR-0002 の決定論規約をランタイムでも
    /// 締める設定)、名前付きカルチャの解決は <c>CultureNotFoundException</c> になる。代わりに
    /// <see cref="CultureInfo.InvariantCulture"/> をクローンし、負号・小数点・桁区切りの表記だけを
    /// 差し替える(タスク仕様が挙げた2案のうち、<c>NumberFormatInfo.NegativeSign</c> を
    /// U+2212 に差し替える側)。
    /// </para>
    /// <para>
    /// <b>カルチャは必ず元へ戻す</b>(<c>try</c>/<c>finally</c>)。他のテストが
    /// <see cref="CultureInfo.CurrentCulture"/> を見た場合に道連れにしないため。
    /// </para>
    /// </remarks>
    [Fact]
    public void RunIsInvariantToCultureAndUsesUnixNewlines()
    {
        string configPath = WriteConfig(
            """
            { "masterSeeds": [1], "durationDays": 5, "world": { "preset": "m0-small" } }
            """);

        string outInvariant = Path.Combine(_workDirectory, "outInvariant");
        string outCulture = Path.Combine(_workDirectory, "outCulture");

        var originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal(0, Program.Execute(new[] { "run", "--config", configPath, "--out", outInvariant }));

            // 負号をU+2212(全角マイナス)、小数点をカンマ、桁区切りを空白へ差し替えたクローン
            // (profitが負になりうる、GDD02 §5帰結2。桁区切りが混ざる読みも試す)。
            var alteredCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            alteredCulture.NumberFormat.NegativeSign = "−";
            alteredCulture.NumberFormat.NumberDecimalSeparator = ",";
            alteredCulture.NumberFormat.NumberGroupSeparator = " ";

            CultureInfo.CurrentCulture = alteredCulture;
            Assert.Equal(0, Program.Execute(new[] { "run", "--config", configPath, "--out", outCulture }));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }

        foreach (string fileName in
            new[] { "economy.csv", "prices.csv", "districts.csv", "households.csv", "trades.csv" })
        {
            byte[] bytesInvariant = File.ReadAllBytes(Path.Combine(outInvariant, "1", fileName));
            byte[] bytesCulture = File.ReadAllBytes(Path.Combine(outCulture, "1", fileName));

            // (i) カルチャが違っても出力はバイト一致する(CultureInfo.InvariantCultureを
            // 落とす変異で赤になる)。
            Assert.Equal(bytesInvariant, bytesCulture);

            // (ii) 改行に\r\nが現れない(NewLine = "\n"を落とす変異で赤になる)。
            string text = System.Text.Encoding.UTF8.GetString(bytesInvariant);
            Assert.DoesNotContain("\r\n", text);
        }
    }

    /// <summary>
    /// #22。30日の走行で <c>economy.csv</c> / <c>trades.csv</c> が30行、<c>prices.csv</c> が
    /// 30×9行、<c>households.csv</c> が30×10行(いずれもヘッダを除く)。
    /// </summary>
    [Fact]
    public void RowCountsMatchTheGrid()
    {
        string configPath = WriteConfig(
            """
            { "masterSeeds": [1], "durationDays": 30, "world": { "preset": "m0-small" } }
            """);
        string outDirectory = Path.Combine(_workDirectory, "out");

        Assert.Equal(0, Program.Execute(new[] { "run", "--config", configPath, "--out", outDirectory }));

        string seedDirectory = Path.Combine(outDirectory, "1");

        Assert.Equal(1 + 30, File.ReadAllLines(Path.Combine(seedDirectory, "economy.csv")).Length);
        Assert.Equal(1 + 30, File.ReadAllLines(Path.Combine(seedDirectory, "trades.csv")).Length);
        Assert.Equal(1 + (30 * 9), File.ReadAllLines(Path.Combine(seedDirectory, "prices.csv")).Length);
        Assert.Equal(1 + (30 * 10), File.ReadAllLines(Path.Combine(seedDirectory, "households.csv")).Length);
    }

    /// <summary>
    /// #23。未知のキー・空でない <c>features</c> / <c>coefficients</c> ・<c>preset</c> が
    /// <c>"m0-small"</c> 以外・<c>durationDays &lt;= 0</c> ・空の <c>masterSeeds</c> は
    /// いずれもエラー(終了コード64)。
    /// </summary>
    [Theory]
    [InlineData("""{ "masterSeeds": [1], "durationDays": 1, "world": { "preset": "m0-small" }, "unknownKey": 1 }""")]
    [InlineData("""{ "masterSeeds": [1], "durationDays": 1, "world": { "preset": "m0-small" }, "features": { "x": 1 } }""")]
    [InlineData(
        """{ "masterSeeds": [1], "durationDays": 1, "world": { "preset": "m0-small" }, "coefficients": { "x": 1 } }""")]
    [InlineData("""{ "masterSeeds": [1], "durationDays": 1, "world": { "preset": "not-m0-small" } }""")]
    [InlineData("""{ "masterSeeds": [1], "durationDays": 0, "world": { "preset": "m0-small" } }""")]
    [InlineData("""{ "masterSeeds": [], "durationDays": 1, "world": { "preset": "m0-small" } }""")]
    public void ConfigRejectsUnknownKeys(string json)
    {
        string configPath = WriteConfig(json);
        string outDirectory = Path.Combine(_workDirectory, "out");

        int exitCode = Program.Execute(new[] { "run", "--config", configPath, "--out", outDirectory });

        Assert.Equal(64, exitCode);
    }
}
