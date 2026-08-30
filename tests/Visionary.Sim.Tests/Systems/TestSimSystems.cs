using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Systems;

/// <summary>スケジューラの検証専用ダミーシステム群。経済ロジックは持たない(タスク仕様のスコープ外)。</summary>
internal static class TestSimSystems
{
    /// <summary>呼ばれるたびに <c>world.Now</c> / <c>context.Now</c> を記録するだけ。</summary>
    internal sealed class RecordingSystem : ISimSystem
    {
        public RandomStream Stream { get; }

        public Cadence Cadence { get; }

        public List<Tick> RunAtWorldNow { get; } = new();

        public List<Tick> RunAtContextNow { get; } = new();

        public RecordingSystem(RandomStream stream, Cadence cadence)
        {
            Stream = stream;
            Cadence = cadence;
        }

        public void Step(World world, SimContext context)
        {
            RunAtWorldNow.Add(world.Now);
            RunAtContextNow.Add(context.Now);
        }
    }

    /// <summary>登録順の検証用。呼ばれるたびに <see cref="Name"/> を共有ログへ追記する。</summary>
    internal sealed class OrderRecordingSystem : ISimSystem
    {
        private readonly List<string> _log;

        public string Name { get; }

        public RandomStream Stream { get; }

        public Cadence Cadence { get; }

        public OrderRecordingSystem(string name, RandomStream stream, Cadence cadence, List<string> log)
        {
            Name = name;
            Stream = stream;
            Cadence = cadence;
            _log = log;
        }

        public void Step(World world, SimContext context) => _log.Add(Name);
    }

    /// <summary>
    /// <see cref="SimContext.OpenRandom(int)"/> を1回呼び、値を <see cref="Values"/> に積む。
    /// 「自分の系統の値しか返らないこと」(落ちるべき条件 #10)と
    /// 「tickが進めば同じエンティティで再び開けること」(#10c)の検証に使う。
    /// </summary>
    internal sealed class CapturingSystem : ISimSystem
    {
        private readonly int _entityId;

        public RandomStream Stream { get; }

        public Cadence Cadence { get; }

        public List<ulong> Values { get; } = new();

        public CapturingSystem(RandomStream stream, Cadence cadence, int entityId)
        {
            Stream = stream;
            Cadence = cadence;
            _entityId = entityId;
        }

        public void Step(World world, SimContext context)
        {
            var sequence = context.OpenRandom(_entityId);
            Values.Add(sequence.NextUInt64());
        }
    }

    /// <summary>同一tick内で同じエンティティを2度開く。二重オープン検出(#10b)の検証用。</summary>
    internal sealed class DoubleOpeningSystem : ISimSystem
    {
        private readonly int _entityId;

        public RandomStream Stream { get; }

        public Cadence Cadence { get; }

        public DoubleOpeningSystem(RandomStream stream, Cadence cadence, int entityId)
        {
            Stream = stream;
            Cadence = cadence;
            _entityId = entityId;
        }

        public void Step(World world, SimContext context)
        {
            context.OpenRandom(_entityId);
            context.OpenRandom(_entityId);
        }
    }

    /// <summary>
    /// 全NPCの流動資金を乱数で増減させる。器(World/Scheduler/SimContext)だけの段階でも
    /// 決定論が成立していることを示すための、経済ロジックを持たない最小の状態変更(#11)。
    /// </summary>
    internal sealed class MutatingSystem : ISimSystem
    {
        public RandomStream Stream { get; }

        public Cadence Cadence { get; }

        public MutatingSystem(RandomStream stream, Cadence cadence)
        {
            Stream = stream;
            Cadence = cadence;
        }

        public void Step(World world, SimContext context)
        {
            // NPCの処理順はId昇順で固定(ADR-0002)。Npcsは添字=Idの配列なので、
            // 配列を先頭から走査するだけで満たされる。
            foreach (var npc in world.Npcs)
            {
                var sequence = context.OpenRandom(npc.Id);
                npc.LiquidFunds += sequence.NextInt(-5, 6);
            }
        }
    }

    /// <summary>
    /// 渡された <see cref="SimContext"/> を退避し、<see cref="ISimSystem.Step"/> の外から
    /// 使えてしまわないかを確かめるためのシステム。
    /// </summary>
    internal sealed class ContextStashingSystem : ISimSystem
    {
        internal SimContext? Stashed { get; private set; }

        public RandomStream Stream => RandomStream.Trade;

        public Cadence Cadence => Cadence.EveryTick();

        public void Step(World world, SimContext context) => Stashed = context;
    }

    /// <summary><see cref="Cadence"/> を初期化し忘れたシステム。</summary>
    internal sealed class UnsetCadenceSystem : ISimSystem
    {
        public RandomStream Stream => RandomStream.Rumor;

        public Cadence Cadence { get; }

        public void Step(World world, SimContext context)
        {
        }
    }
}
