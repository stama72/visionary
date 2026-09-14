using Visionary.Sim.Randomness;
using Visionary.Sim.Systems;

namespace Visionary.Sim.Runner.Determinism;

/// <summary>
/// W1 限りの合成負荷(TDD01 §3.8 の決定論ハッシュ回帰テスト用)。
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

    // Knowledge の保持本数上限。所有者1人あたり(GDD01 §4.1 の保持ポリシーの合成版)。
    private const int KnowledgeRetentionLimit = 50;

    // 熟練度‰ の1日あたりの振れ幅。合成負荷の都合で選んだ値(GDD08 §4.1 の式ではない)。
    private const int SkillDrift = 5;

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

            // 熟練度を動かす。Npcs 区分が時刻とともに変わらないと、ハッシュから熟練度を
            // 落としても「初期値のぶんだけ違う」状態が残り続けて回帰が鈍る。
            npc.SkillPermille = Math.Clamp(
                npc.SkillPermille + rng.NextInt(-SkillDrift, SkillDrift + 1), 0, 1000);

            // 保持本数の上限は所有者ごとに掛ける。相場知識の所有者は個人(TDD01 §3.2)。
            // 状態が単調増加でなくなることで List の順序変化がハッシュに効く。
            var observations = world.Knowledge[npc.Id];

            if (observations.Count > KnowledgeRetentionLimit)
            {
                observations.RemoveRange(0, observations.Count - KnowledgeRetentionLimit);
            }
        }
    }
}
