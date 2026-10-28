using Visionary.Sim.Commands;
using Visionary.Sim.Metrics;
using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests;

/// <summary>SimHost(W5-04 タスク仕様のテスト表 #21〜#25、TDD01 §3.5)。</summary>
public sealed class SimHostTests
{
    private static readonly WorldDefinition Definition = WorldDefinition.M0;

    private sealed record UnknownCommand : ICommand;

    private sealed class NullSink : IDailyMetricsSink
    {
        public void Write(in DailySnapshot snapshot)
        {
        }
    }

    /// <summary>#21。先回し。<c>startDay: 15</c> は D15T00 で、<c>Advance(360)</c> と同じ状態になる。</summary>
    [Fact]
    public void StartDayFastForwardsLikeAdvancingByDaysTimesTwentyFour()
    {
        var fastForwarded = new SimHost(Definition, 1, startDay: 15);
        var advanced = new SimHost(Definition, 1);
        advanced.Advance(360);

        Assert.Equal(Tick.FromDays(15), fastForwarded.Now);
        Assert.Equal(advanced.StateHash(), fastForwarded.StateHash());
    }

    /// <summary>#22。負の開始日は例外(黙って 0 に丸めない)。0 は D0T00。</summary>
    [Fact]
    public void NegativeStartDayIsRejectedAndZeroStartsAtTheEpoch()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimHost(Definition, 1, startDay: -1));
        Assert.Equal(Tick.Zero, new SimHost(Definition, 1, startDay: 0).Now);
    }

    /// <summary>
    /// #23。同じシード・同じコマンド列を2つの <c>SimHost</c> に流すと、各日の終わりのハッシュが一致する。
    /// コマンド列は毎日: 窓口で買い、世帯 0 の区画へ行き、買って、中心へ戻り、売る。
    /// </summary>
    [Fact]
    public void SameSeedAndSameCommandsGiveTheSameHashEveryDay()
    {
        var left = new SimHost(Definition, 7);
        var right = new SimHost(Definition, 7);
        bool anyAccepted = false;

        for (int day = 0; day < 8; day++)
        {
            ulong leftHash = RunDay(left, ref anyAccepted);
            ulong rightHash = RunDay(right, ref anyAccepted);

            Assert.Equal(leftHash, rightHash);
        }

        Assert.True(anyAccepted, "8日間のどのコマンドも Accepted にならなかった(値の問題の可能性)。");
    }

    private static ulong RunDay(SimHost host, ref bool anyAccepted)
    {
        host.Advance(6);

        var results = new List<Result>
        {
            host.Execute(new BuyFromWindowCommand(Item.Bread, 2)),
        };

        var target = host.World.Households[0];
        var move = host.Execute(new MoveCommand(target.DistrictId));

        if (move.Status == CommandStatus.Accepted)
        {
            host.Advance(checked((int)(move.ArrivalAt!.Value.Value - host.Now.Value)));
        }

        results.Add(host.Execute(new AskNeedsCommand(target.Id)));
        results.Add(host.Execute(new BuyFromHouseholdCommand(
            target.Id, Definition.Recipes[(int)target.Occupation].Outputs[0].ItemId, 1)));

        foreach (var result in results)
        {
            anyAccepted |= result.Status == CommandStatus.Accepted;
        }

        host.Advance(checked((int)(Tick.FromDays(host.Now.DayIndex + 1).Value - host.Now.Value)));

        return host.StateHash();
    }

    /// <summary>
    /// #24。<c>SimPipeline.Build</c> の系統の並びが TDD01 §3.3 の順(Production, Consumption, Household,
    /// NeedGeneration, TradePlanning, Trade, Metrics)と同じ。<c>Program.BuildPipeline</c> も同じ並びを返す。
    /// </summary>
    [Fact]
    public void PipelineIsBuiltInTheDocumentedOrder()
    {
        var expected = new[]
        {
            RandomStream.Production,
            RandomStream.Consumption,
            RandomStream.Household,
            RandomStream.NeedGeneration,
            RandomStream.TradePlanning,
            RandomStream.Trade,
            RandomStream.Metrics,
        };

        var built = SimPipeline.Build(Definition, new NullSink()).Select(system => system.Stream).ToArray();
        var viaRunner = Visionary.Sim.Runner.Program.BuildPipeline(Definition, new NullSink()).Select(system => system.Stream).ToArray();

        Assert.Equal(expected, built);
        Assert.Equal(expected, viaRunner);
    }

    /// <summary>#25。呼び出し側の誤り: <c>null</c> は <c>ArgumentNullException</c>、未知の型は <c>ArgumentException</c>。</summary>
    [Fact]
    public void ExecuteRejectsNullAndUnknownCommandTypes()
    {
        var host = new SimHost(Definition, 1);
        ulong before = host.StateHash();

        Assert.Throws<ArgumentNullException>(() => host.Execute(null!));
        Assert.Throws<ArgumentException>(() => host.Execute(new UnknownCommand()));
        Assert.Equal(before, host.StateHash());
    }

    /// <summary><c>Advance</c> の0以下は <c>ArgumentOutOfRangeException</c>(SimScheduler にそのまま渡す)。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AdvanceRejectsNonPositiveTicks(int ticks)
    {
        var host = new SimHost(Definition, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => host.Advance(ticks));
    }
}
