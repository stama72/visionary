using System.Globalization;
using System.Reflection;
using Visionary.Sim.Commands;
using Visionary.Sim.Randomness;
using Visionary.Sim.Runner;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Runner;

/// <summary>
/// Runner の台本の口(<see cref="ScriptedRun"/> / <see cref="IPlayerSession"/>。W5-05 タスク仕様のテスト表
/// #16〜#20、TDD01 §4.1)。1テストごとに一時ディレクトリを持ち、終了時に削除する。
/// </summary>
public sealed class ScriptedRunTests : IDisposable
{
    private static readonly string[] CsvFiles =
    {
        "economy.csv", "prices.csv", "districts.csv", "households.csv", "trades.csv",
    };

    private readonly string _workDirectory;

    public ScriptedRunTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "vsim-script-tests-" + Guid.NewGuid().ToString("N"));
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

    private sealed class RecordingScript : IPlayerScript
    {
        public List<Tick> Times { get; } = new();

        public void OnHour(IPlayerSession session) => Times.Add(session.Now);
    }

    private sealed class NoOpScript : IPlayerScript
    {
        public void OnHour(IPlayerSession session)
        {
        }
    }

    private sealed class BuyOnceScript : IPlayerScript
    {
        public List<DomainEvent> Received { get; } = new();

        public Result? Bought { get; private set; }

        public void OnHour(IPlayerSession session)
        {
            if (session.Now == new Tick(6))
            {
                Bought = session.Execute(new BuyFromWindowCommand(Item.Grain, 1));
            }

            if (session.Now == new Tick(7))
            {
                Received.AddRange(session.DrainEvents());
            }
        }
    }

    private string RunCsv(string name, long seed, int ticks, IPlayerScript? script)
    {
        string directory = Path.Combine(_workDirectory, name);
        SimHost host;

        using (var csv = new CsvMetricsSink(directory))
        {
            host = new SimHost(WorldDefinition.M0, seed, startDay: 0, metricsSink: csv);
            ScriptedRun.Run(host, script, ticks);
        }

        LastHash = host.StateHash();
        return directory;
    }

    private ulong LastHash { get; set; }

    /// <summary>
    /// #16。<c>ticks = 48</c> で、<c>OnHour</c> は48回、時刻は D0T00〜D1T23 の連番、終了時 D2T00。
    /// </summary>
    [Fact]
    public void ScriptIsCalledBeforeEachTickIsProcessed()
    {
        var host = new SimHost(WorldDefinition.M0, 1);
        var script = new RecordingScript();

        ScriptedRun.Run(host, script, 48);

        Assert.Equal(48, script.Times.Count);
        Assert.Equal(Enumerable.Range(0, 48).Select(i => new Tick(i)).ToArray(), script.Times.ToArray());
        Assert.Equal(Tick.FromDays(2), host.Now);
    }

    [Fact]
    public void NonPositiveTicksAreRejected()
    {
        var host = new SimHost(WorldDefinition.M0, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => ScriptedRun.Run(host, null, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScriptedRun.Run(host, new NoOpScript(), -1));
    }

    /// <summary>#17。<c>script: null</c> と何もしない台本で、3日ぶんの CSV 5ファイルが1バイト一致し、<c>StateHash()</c> も一致。</summary>
    [Fact]
    public void NullScriptAndNoOpScriptProduceIdenticalOutput()
    {
        string withoutScript = RunCsv("null", 3, 3 * 24, script: null);
        ulong hashWithout = LastHash;
        string withScript = RunCsv("noop", 3, 3 * 24, new NoOpScript());
        ulong hashWith = LastHash;

        foreach (string file in CsvFiles)
        {
            byte[] left = File.ReadAllBytes(Path.Combine(withoutScript, file));
            byte[] right = File.ReadAllBytes(Path.Combine(withScript, file));

            Assert.NotEmpty(left);
            Assert.Equal(left, right);
        }

        Assert.Equal(hashWithout, hashWith);
    }

    /// <summary>
    /// #18。移設前の組み方(<c>WorldGenerator.Generate</c> + <c>new SimScheduler(Program.BuildPipeline(...))</c>)で
    /// 同じシード・3日を <c>CsvMetricsSink</c> に書いたものと、<c>vsim run</c> の出力が5ファイルとも1バイト一致。
    /// </summary>
    [Fact]
    public void VsimRunMatchesTheOldDirectAssembly()
    {
        const long seed = 5;
        var definition = WorldDefinition.M0;

        string oldDirectory = Path.Combine(_workDirectory, "old");

        using (var csv = new CsvMetricsSink(oldDirectory))
        {
            var world = WorldGenerator.Generate(definition, new RandomSource(seed));
            var scheduler = new SimScheduler(Program.BuildPipeline(definition, csv), new RandomSource(seed));
            scheduler.Advance(world, 3 * 24);
        }

        string configPath = Path.Combine(_workDirectory, "config.json");
        File.WriteAllText(
            configPath,
            $$"""
            { "masterSeeds": [{{seed.ToString(CultureInfo.InvariantCulture)}}], "durationDays": 3, "world": { "preset": "m0-small" } }
            """);
        string outDirectory = Path.Combine(_workDirectory, "out");

        int exitCode = Program.Execute(new[] { "run", "--config", configPath, "--out", outDirectory });

        Assert.Equal(0, exitCode);

        foreach (string file in CsvFiles)
        {
            byte[] expected = File.ReadAllBytes(Path.Combine(oldDirectory, file));
            byte[] actual = File.ReadAllBytes(Path.Combine(outDirectory, seed.ToString(CultureInfo.InvariantCulture), file));

            Assert.True(expected.Length > 100, $"{file} が空に近い。");
            Assert.Equal(expected, actual);
        }
    }

    /// <summary>
    /// #19。D0T06 に窓口から1個買う台本で1日回す。台本は D0T07 の <c>OnHour</c> で <c>DrainEvents()</c> から
    /// 1件受け取り、economy.csv の day 0 の <c>player_funds</c> が減っている。
    /// </summary>
    [Fact]
    public void ScriptTradesReachTheSimHostEventsAndTheCsv()
    {
        var script = new BuyOnceScript();
        string directory = RunCsv("buy", 1, 24, script);

        Assert.NotNull(script.Bought);
        Assert.Equal(CommandStatus.Accepted, script.Bought.Status);
        var received = Assert.Single(script.Received);
        Assert.IsType<PlayerTradeExecuted>(received);

        string[] rows = File.ReadAllLines(Path.Combine(directory, "economy.csv"));
        string[] header = rows[0].Split(',');
        string[] day0 = rows[1].Split(',');
        int funds = int.Parse(day0[Array.IndexOf(header, "player_funds")], CultureInfo.InvariantCulture);

        Assert.Equal("0", day0[0]);
        Assert.Equal(WorldDefinition.PlayerInitialFunds - script.Bought.Payment, funds);
        Assert.True(funds < WorldDefinition.PlayerInitialFunds);
    }

    /// <summary>#20。<c>IPlayerSession</c> の公開メンバーは <c>Now</c>・<c>Execute</c>・<c>Query</c>・<c>DrainEvents</c> の4つちょうど。</summary>
    [Fact]
    public void SessionExposesExactlyFourMembers()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var type = typeof(IPlayerSession);

        var names = type.GetMembers(all)
            .Where(member => member is not MethodInfo { IsSpecialName: true })
            .Select(member => member.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "DrainEvents", "Execute", "Now", "Query" }, names);
        Assert.Empty(type.GetInterfaces());
    }
}
