using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;

namespace Visionary.Sim.Runner.Determinism;

/// <summary>
/// W1 限りの合成負荷(TDD01 §3.8 の決定論ハッシュ回帰テスト用)。
/// W2 で TDD01 §3.3 の本物のシステム群に差し替え、このファイルは削除する。
/// </summary>
/// <remarks>
/// <para>
/// 全区分に書き込むことで、<see cref="Determinism.StateHasher"/> の「全区分を Id 昇順で走る」
/// 経路を実行させる。数値定数はすべて合成負荷の都合で選んだ値であり、
/// 経済的な意味は無い(仕様)。品目・区画・職業の値も同様で、M0 の正は
/// GDD02 §2.2 / §4.3 / §2.4 が持つ。
/// </para>
/// <para>
/// <b>世帯持ちの状態は <c>npc.HouseholdId</c> を経由して書く。</b>複数の NPC が同じ世帯を
/// 指すため書き込みの順序が結果を変えるが、NPC の走査は Id 昇順に固定されている(ADR-0002)。
/// 世帯を共有資源として踏むこの経路は GDD02 §6.2.1(世帯内の購入の決済順)の前触れであり、
/// 合成負荷のうちに踏ませておく価値がある。
/// </para>
/// </remarks>
internal sealed class SyntheticLoadSystem : ISimSystem
{
    // 立地9区画(合成負荷の任意の値。M0 の区画は GDD02 §4.3)。
    private const int LocationCount = 9;

    // 合成の職業数(合成負荷の任意の値。M0 の職業は GDD02 §2.4 で 0〜4)。
    private const int OccupationCount = 5;

    // Needs/Promises を追加する確率。
    private const int NeedAndPromiseProbabilityPermille = 100; // 100‰ = 10%

    // Knowledge/Ledgers を追加する確率。
    private const int KnowledgeAndLedgerProbabilityPermille = 200; // 200‰ = 20%

    // 破産中フラグを立てる確率。0/1 の状態がハッシュに効く経路を踏ませる。
    private const int BankruptProbabilityPermille = 50; // 50‰ = 5%

    public RandomStream Stream => RandomStream.WorldGen;

    public Cadence Cadence => Cadence.EveryTick();

    public void Step(World world, SimContext context)
    {
        // NPCの処理順はId昇順で固定(ADR-0002)。Npcsは添字=Idの配列なので先頭から走査するだけでよい。
        foreach (var npc in world.Npcs)
        {
            var rng = context.OpenRandom(npc.Id); // NPCあたり1回だけ開く(仕様)

            var household = world.Households[npc.HouseholdId];

            // 品目数は世界の設定から取る。合成の定数を別に持つと、--items を変えたときに
            // 在庫の添字が範囲外になる。
            int itemCount = household.HouseholdInventory.Length;

            household.LiquidFunds += rng.NextInt(-50, 51); // 単位: 貨幣
            household.OccupationId = rng.NextInt(0, OccupationCount);
            household.IsBankrupt = rng.NextBool(BankruptProbabilityPermille) ? 1 : 0;

            // 世帯在庫と工房在庫の両方に書く。片方だけだと2本の区別(TDD01 §3.2)が
            // ハッシュ回帰で一度も動かない。
            household.HouseholdInventory[rng.NextInt(0, itemCount)] += rng.NextInt(-3, 4);
            household.WorkshopInventory[rng.NextInt(0, itemCount)] += rng.NextInt(-3, 4);

            world.Market[new MarketKey(ItemId: rng.NextInt(0, itemCount), SellerId: npc.HouseholdId)] =
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
                    TypeCode = rng.NextInt(0, 6), // enum化は別タスク(TDD01 §3.6 仮決め表)
                    TargetHouseholdId = rng.NextInt(0, world.Households.Length),
                    ItemId = rng.NextInt(0, itemCount),
                    Quantity = rng.NextInt(1, 11), // 単位: 個
                    Deadline = world.Now.AddDays(rng.NextInt(1, 8)),
                    Urgency = rng.NextInt(0, 101), // 単位: 0〜100 の素の整数(‰ ではない)
                    ReasonCode = rng.NextInt(0, 4),
                });

                world.Promises.Add(new Promise
                {
                    NeedIndex = world.Needs.Count - 1, // Id 参照への置き換えは別タスク
                    T0 = world.Now,
                    T1 = world.Now.AddDays(rng.NextInt(1, 8)),
                    B = rng.NextInt(1, 1001), // 単位: 貨幣(GDD01 §2.8 の B)
                    State = (PromiseState)rng.NextInt(0, 4),
                });
            }

            if (rng.NextBool(KnowledgeAndLedgerProbabilityPermille))
            {
                // 相場知識の所有者は個人。添字は NpcId(TDD01 §3.2)。
                world.Knowledge[npc.Id].Add(new PriceObservation
                {
                    ItemId = rng.NextInt(0, itemCount),
                    LocationId = rng.NextInt(0, LocationCount),
                    Price = rng.NextInt(1, 101), // 単位: 貨幣
                    SellerId = rng.NextInt(0, world.Households.Length),
                    ObservedAt = world.Now,
                    Source = (ObservationSource)rng.NextInt(0, 2),
                });

                // 帳簿の所有者は世帯。添字は世帯Id(TDD01 §3.2)。Knowledge と取り違えない。
                world.Ledgers[npc.HouseholdId].Add(new LedgerEntry
                {
                    CounterpartyId = rng.NextInt(0, world.Households.Length),
                    ItemId = rng.NextInt(0, itemCount),
                    Quantity = rng.NextInt(1, 11), // 単位: 個
                    UnitPrice = rng.NextInt(1, 101), // 単位: 貨幣
                    OccurredAt = world.Now,
                    Terms = (LedgerTerms)rng.NextInt(0, 2),
                    CreditDueAt = world.Now.AddDays(rng.NextInt(1, 31)),
                });
            }

            // ハッシュに入らない区分(EventLog)を実行時にも踏むため必ず追加する(仕様)。
            world.EventLog.Add(new DomainEvent
            {
                KindCode = rng.NextInt(0, 6), // 設計は別タスク(TDD01 §3.6 仮決め表)
                At = world.Now,
                SubjectId = npc.Id,
                RelatedId = rng.NextInt(0, world.Npcs.Length),
                Payload = rng.NextInt(0, 1000),
            });
        }
    }
}
