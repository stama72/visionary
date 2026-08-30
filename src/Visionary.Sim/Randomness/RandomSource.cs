using Visionary.Sim.Time;

namespace Visionary.Sim.Randomness;

/// <summary>
/// マスターシードを保持し、(系統, tick, エンティティ) ごとに独立した乱数列を開く。
/// </summary>
/// <remarks>
/// <para>
/// <b>なぜ系統だけで分けないか。</b>ADR-0002 は「機能フラグを切り替えても、無効化された
/// 系統以外の乱数消費列が変わらない」ことを約束している(共通乱数法)。系統ごとに1本の
/// 長い列を持つと、その約束はフラグを含む系統の内部で必ず破れる — 引く回数が変われば
/// それ以降がすべてずれるためである。
/// </para>
/// <para>
/// 鍵を (マスターシード, 系統, tick, エンティティ) から導出すると、影響はその組の中に閉じる。
/// NPC #7 の100日目の Trade で引く回数が変わっても、他のNPC・他の日・他の系統は
/// ビット単位で同一に保たれる。A/B比較の感度はこの性質に依存している。
/// </para>
/// </remarks>
public readonly struct RandomSource
{
    /// <summary>エンティティに紐づかない用途の番兵。シムの Id は非負(TDD01 §3.2)。</summary>
    public const int NoEntity = -1;

    private readonly long _masterSeed;

    public RandomSource(long masterSeed) => _masterSeed = masterSeed;

    /// <summary>
    /// 指定した (系統, tick, エンティティ) の乱数列を開く。同じ組は常に同じ列を返す。
    /// </summary>
    public RandomSequence Open(RandomStream stream, Tick tick, int entityId)
    {
        // この導出の順序と段数が仕様。変えると同一シードでも別の世界が生成される。
        ulong key = (ulong)_masterSeed;
        key = SplitMix64.Mix(key ^ (ulong)(long)stream);
        key = SplitMix64.Mix(key ^ (ulong)tick.Value);
        key = SplitMix64.Mix(key ^ (ulong)(long)entityId);

        return new RandomSequence(key);
    }

    /// <inheritdoc cref="Open(RandomStream, Tick, int)"/>
    public RandomSequence Open(RandomStream stream, Tick tick) => Open(stream, tick, NoEntity);
}
