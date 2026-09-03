using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;

namespace Visionary.Sim.Runner.Determinism;

/// <summary>
/// W1 限りの合成負荷(docs/tasks/W1-04-determinism-hash.md)。
/// W2 で TDD01 §3.3 の本物のシステム群に差し替え、このファイルは削除する。
/// </summary>
/// <remarks>
/// <see cref="SyntheticLoadSystem"/> とは別系統・別 Cadence(<see cref="Cadence.Daily"/>)で走ることが
/// 目的。これにより、両システムが同じ <c>entityId</c> で別系統の乱数を開く経路
/// (W1-03 で二重オープン検出のキーを誤ったときに壊れた、まさにその経路)が回帰テストの射程に入る。
/// </remarks>
internal sealed class SyntheticDecaySystem : ISimSystem
{
    // 信用の日次減衰量(下限0)。合成負荷の都合で選んだ値であり、経済的な意味は無い(仕様)。
    private const int MinDecay = 1;
    private const int MaxDecayExclusive = 4;

    // Knowledge の保持本数上限(GDD01 §4.1 の保持ポリシーの合成版)。
    private const int KnowledgeRetentionLimit = 500;

    public RandomStream Stream => RandomStream.Trust;

    public Cadence Cadence => Cadence.Daily(hour: 0);

    public void Step(World world, SimContext context)
    {
        // NPCの処理順はId昇順で固定(ADR-0002)。
        foreach (var npc in world.Npcs)
        {
            var rng = context.OpenRandom(npc.Id);

            // SortedDictionary を列挙しながら変更しない。キーを先に配列へ取り出してから書き戻す。
            var keysFromThisNpc = world.TrustLedger.Keys
                .Where(key => key.From == npc.Id)
                .ToArray();

            foreach (var key in keysFromThisNpc)
            {
                int decay = rng.NextInt(MinDecay, MaxDecayExclusive);
                var score = world.TrustLedger[key];
                world.TrustLedger[key] = score with { Value = Math.Max(0, score.Value - decay) };
            }
        }

        // 保持本数の上限。状態が単調増加でなくなることで List の順序変化がハッシュに効く。
        if (world.Knowledge.Count > KnowledgeRetentionLimit)
        {
            int excess = world.Knowledge.Count - KnowledgeRetentionLimit;
            world.Knowledge.RemoveRange(0, excess);
        }
    }
}
