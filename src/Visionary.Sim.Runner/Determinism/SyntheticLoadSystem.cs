using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;

namespace Visionary.Sim.Runner.Determinism;

/// <summary>
/// W1 限りの合成負荷(docs/tasks/W1-04-determinism-hash.md)。
/// W2 で TDD01 §3.3 の本物のシステム群に差し替え、このファイルは削除する。
/// </summary>
/// <remarks>
/// 全区画に書き込むことで、<see cref="Determinism.StateHasher"/> の「全区画を Id 昇順で走る」
/// 経路を W1 中に実行させる。数値定数はすべて合成負荷の都合で選んだ値であり、
/// 経済的な意味は無い(仕様)。
/// </remarks>
internal sealed class SyntheticLoadSystem : ISimSystem
{
    // 品目5種(TDD01 §3.6)。
    private const int ItemCount = 5;

    // 立地9区画(TDD01 §3.2)。
    private const int LocationCount = 9;

    // Needs/Promises を追加する確率。
    private const int NeedAndPromiseProbabilityPermille = 100; // 100‰ = 10%

    // Knowledge/Ledgers を追加する確率。
    private const int KnowledgeAndLedgerProbabilityPermille = 200; // 200‰ = 20%

    public RandomStream Stream => RandomStream.WorldGen;

    public Cadence Cadence => Cadence.EveryTick();

    public void Step(World world, SimContext context)
    {
        // NPCの処理順はId昇順で固定(ADR-0002)。Npcsは添字=Idの配列なので先頭から走査するだけでよい。
        foreach (var npc in world.Npcs)
        {
            var rng = context.OpenRandom(npc.Id); // NPCあたり1回だけ開く(仕様)

            npc.LiquidFunds += rng.NextInt(-50, 51); // 単位: 貨幣

            world.Market[new MarketKey(ItemId: rng.NextInt(0, ItemCount), SellerId: npc.Id)] =
                rng.NextInt(1, 101); // 単位: 貨幣

            world.TrustLedger[new TrustKey(npc.Id, rng.NextInt(0, world.Npcs.Length))] = new TrustScore
            {
                Value = rng.NextInt(0, 101),
                LastMet = world.Now,
            };

            if (rng.NextBool(NeedAndPromiseProbabilityPermille))
            {
                world.Needs.Add(new Need
                {
                    TypeCode = rng.NextInt(0, 6), // W2 で enum化(TDD01 §3.6 仮決め表)
                    TargetNpcId = rng.NextInt(0, world.Npcs.Length),
                    ItemId = rng.NextInt(0, ItemCount),
                    Quantity = rng.NextInt(1, 11), // 単位: 個
                    Deadline = world.Now.AddDays(rng.NextInt(1, 8)),
                    Urgency = rng.NextInt(0, 101), // 単位: 0〜100 の素の整数(‰ ではない)
                    ReasonCode = rng.NextInt(0, 4),
                });

                world.Promises.Add(new Promise
                {
                    NeedIndex = world.Needs.Count - 1, // W2 で Id 参照へ(TDD01 §3.6 仮決め表)
                    T0 = world.Now,
                    T1 = world.Now.AddDays(rng.NextInt(1, 8)),
                    B = rng.NextInt(1, 1001), // 単位: 貨幣(GDD01 §2.8 の B)
                    State = (PromiseState)rng.NextInt(0, 4),
                });
            }

            if (rng.NextBool(KnowledgeAndLedgerProbabilityPermille))
            {
                world.Knowledge.Add(new PriceObservation
                {
                    ItemId = rng.NextInt(0, ItemCount),
                    LocationId = rng.NextInt(0, LocationCount),
                    Price = rng.NextInt(1, 101), // 単位: 貨幣
                    ObservedAt = world.Now,
                    Source = (ObservationSource)rng.NextInt(0, 2),
                });

                world.Ledgers.Add(new LedgerEntry
                {
                    CounterpartyId = rng.NextInt(0, world.Npcs.Length),
                    ItemId = rng.NextInt(0, ItemCount),
                    Quantity = rng.NextInt(1, 11), // 単位: 個
                    UnitPrice = rng.NextInt(1, 101), // 単位: 貨幣
                    OccurredAt = world.Now,
                    Terms = (LedgerTerms)rng.NextInt(0, 2),
                    CreditDueAt = world.Now.AddDays(rng.NextInt(1, 31)),
                });
            }

            // ハッシュに入らない区画(EventLog)を実行時にも踏むため必ず追加する(仕様)。
            world.EventLog.Add(new DomainEvent
            {
                KindCode = rng.NextInt(0, 6), // W2 で設計(TDD01 §3.6 仮決め表)
                At = world.Now,
                SubjectId = npc.Id,
                RelatedId = rng.NextInt(0, world.Npcs.Length),
                Payload = rng.NextInt(0, 1000),
            });
        }
    }
}
