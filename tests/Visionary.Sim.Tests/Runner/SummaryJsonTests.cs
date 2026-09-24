using System.Globalization;
using System.Text;
using Visionary.Sim.Determinism;
using Visionary.Sim.Metrics;
using Visionary.Sim.Randomness;
using Visionary.Sim.Runner;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;
using Visionary.Sim.Verification;

namespace Visionary.Sim.Tests.Runner;

/// <summary>
/// <c>summary.json</c> の書き出し(<see cref="SummaryJsonWriter"/> / <c>vsim run</c>)の検査
/// (W2-21 タスク仕様のテスト表 #32〜#35・#38)。
/// </summary>
public sealed class SummaryJsonTests : IDisposable
{
    private readonly string _workDirectory;

    public SummaryJsonTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "vsim-summary-tests-" + Guid.NewGuid().ToString("N"));
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
            // 後始末の失敗はテスト結果に影響させない(ProgramRunTestsと同じ規約)。
        }
    }

    private string WriteConfig(string json)
    {
        string path = Path.Combine(_workDirectory, "config.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static RunSummary SampleSummary() => new(
        SchemaVersion: 1,
        WindowDays: 120,
        TransientDays: 30,
        DurationDays: 150,
        Seeds: new[]
        {
            new SeedVerification(1, 150, 1, new[]
            {
                new VerificationItemResult(
                    "8-1a", Verdict.Red, 149,
                    new[] { new Evidence("日本語のテスト:6", 1720, 540, 41) }),
                new VerificationItemResult("8-2a", Verdict.Green, -1, Array.Empty<Evidence>()),
                new VerificationItemResult("8-4", Verdict.Indeterminate, -1, Array.Empty<Evidence>()),
            }),
        },
        Overall: new[]
        {
            new OverallItemResult("8-1a", Verdict.Red, new long[] { 1 }, Array.Empty<long>()),
            new OverallItemResult("8-2a", Verdict.Green, Array.Empty<long>(), Array.Empty<long>()),
            new OverallItemResult("8-4", Verdict.Indeterminate, Array.Empty<long>(), new long[] { 1 }),
        });

    /// <summary>#33。日本語の根拠名が <c>\u</c> エスケープにならない(UnsafeRelaxedJsonEscaping)。</summary>
    [Fact]
    public void SummaryKeepsJapaneseAsUtf8()
    {
        string path = Path.Combine(_workDirectory, "summary.json");
        SummaryJsonWriter.Write(path, SampleSummary());

        string text = Encoding.UTF8.GetString(File.ReadAllBytes(path));

        Assert.Contains("日本語のテスト", text);
        Assert.DoesNotContain("\\u", text);
    }

    /// <summary>#34。<c>verdict</c> は小文字の <c>"green"</c> / <c>"red"</c> / <c>"indeterminate"</c>。</summary>
    [Fact]
    public void VerdictIsSerializedInLowerCase()
    {
        string path = Path.Combine(_workDirectory, "summary.json");
        SummaryJsonWriter.Write(path, SampleSummary());

        string text = Encoding.UTF8.GetString(File.ReadAllBytes(path));

        Assert.Contains("\"verdict\": \"red\"", text);
        Assert.Contains("\"verdict\": \"green\"", text);
        Assert.Contains("\"verdict\": \"indeterminate\"", text);
        Assert.DoesNotContain("\"Red\"", text);
        Assert.DoesNotContain("\"Green\"", text);
        Assert.DoesNotContain("\"Indeterminate\"", text);
    }

    /// <summary>
    /// #32。同じ設定で <c>vsim run</c> を2回走らせ、<c>summary.json</c> がバイト一致する。
    /// (i) カルチャが異なっても一致する、(ii) 出力バイトに <c>\r\n</c> が現れない。
    /// </summary>
    [Fact]
    public void SummaryIsByteIdenticalAcrossRuns()
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

        byte[] bytesInvariant = File.ReadAllBytes(Path.Combine(outInvariant, "summary.json"));
        byte[] bytesCulture = File.ReadAllBytes(Path.Combine(outCulture, "summary.json"));

        Assert.Equal(bytesInvariant, bytesCulture);

        string text = Encoding.UTF8.GetString(bytesInvariant);
        Assert.DoesNotContain("\r\n", text);
    }

    /// <summary>
    /// #35。<c>vsim run --out &lt;dir&gt;</c> の後、<c>&lt;dir&gt;/summary.json</c> が存在し、
    /// <c>&lt;dir&gt;/&lt;シード&gt;/summary.json</c> は存在しない。
    /// </summary>
    [Fact]
    public void SummaryLivesAtTheRunRootNotUnderTheSeed()
    {
        string configPath = WriteConfig(
            """
            { "masterSeeds": [1, 2], "durationDays": 5, "world": { "preset": "m0-small" } }
            """);
        string outDirectory = Path.Combine(_workDirectory, "out");

        Assert.Equal(0, Program.Execute(new[] { "run", "--config", configPath, "--out", outDirectory }));

        Assert.True(File.Exists(Path.Combine(outDirectory, "summary.json")));
        Assert.False(File.Exists(Path.Combine(outDirectory, "1", "summary.json")));
        Assert.False(File.Exists(Path.Combine(outDirectory, "2", "summary.json")));
    }

    /// <summary>
    /// #38。核心。<see cref="CompositeDailyMetricsSink"/> を通した走行の5つのCSVが、
    /// <see cref="CsvMetricsSink"/> 単体の走行とバイト一致する(収集側に触れないという宣言の機械)。
    /// <b>mutator実測(2026-09-24)</b>: 変異#38(<c>CompositeDailyMetricsSink</c> の呼び出し順を逆に
    /// したうえで <c>VerificationAccumulator</c> に <c>snapshot.Prices</c> の要素を書き換えさせる)で
    /// 赤(落ちた)。
    /// </summary>
    [Fact]
    public void CsvOutputIsUnchangedByTheAccumulator()
    {
        const long Seed = 1;
        const int DurationDays = 40;

        string csvOnlyDirectory = Path.Combine(_workDirectory, "csvOnly");
        string withAccumulatorDirectory = Path.Combine(_workDirectory, "withAccumulator");

        RunWithCsvOnly(csvOnlyDirectory, Seed, DurationDays);

        Assert.Equal(
            0,
            Program.Execute(new[]
            {
                "run", "--config",
                WriteConfig($"{{ \"masterSeeds\": [{Seed}], \"durationDays\": {DurationDays}, \"world\": {{ \"preset\": \"m0-small\" }} }}"),
                "--out", withAccumulatorDirectory,
            }));

        foreach (string fileName in
            new[] { "economy.csv", "prices.csv", "districts.csv", "households.csv", "trades.csv" })
        {
            byte[] csvOnlyBytes = File.ReadAllBytes(Path.Combine(csvOnlyDirectory, fileName));
            byte[] withAccumulatorBytes = File.ReadAllBytes(
                Path.Combine(withAccumulatorDirectory, Seed.ToString(CultureInfo.InvariantCulture), fileName));

            Assert.Equal(csvOnlyBytes, withAccumulatorBytes);
        }
    }

    private static void RunWithCsvOnly(string outputDirectory, long seed, int durationDays)
    {
        var definition = WorldDefinition.M0;
        int ticks = checked(durationDays * Tick.HoursPerDay);
        var world = WorldGenerator.Generate(definition, new RandomSource(seed));

        using var sink = new CsvMetricsSink(outputDirectory);
        var systems = new ISimSystem[]
        {
            new ProductionSystem(definition),
            new ConsumptionSystem(definition),
            new HouseholdSystem(definition),
            new NeedGenerationSystem(definition),
            new TradeSystem(definition),
            new MetricsSystem(definition, sink),
        };
        var scheduler = new SimScheduler(systems, new RandomSource(seed));
        scheduler.Advance(world, ticks);
    }
}
