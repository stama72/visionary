using Visionary.Sim.Time;

namespace Visionary.Sim;

/// <summary>
/// 全状態を保持する単一の集約(TDD01 §3.2)。W1 では中身を持つのは
/// <see cref="Now"/> と <see cref="Npcs"/> のみ。残りは空のコンテナとして用意し、
/// 中身は W2 以降の経済システムが埋める。
/// </summary>
public sealed class World
{
    /// <summary>
    /// NPC を <paramref name="npcCount"/> 体、Id 昇順(0始まり)で用意する。
    /// </summary>
    public World(int npcCount)
    {
        if (npcCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(npcCount), npcCount, "NPC数は非負。");
        }

        Npcs = new NpcState[npcCount];

        for (int id = 0; id < npcCount; id++)
        {
            Npcs[id] = new NpcState(id);
        }

        Market = new SortedDictionary<MarketKey, int>();
        TrustLedger = new SortedDictionary<TrustKey, TrustScore>();
        Needs = new List<Need>();
        Promises = new List<Promise>();
        Knowledge = new List<PriceObservation>();
        Ledgers = new List<LedgerEntry>();
        EventLog = new List<DomainEvent>();
    }

    /// <summary>現在tick。暦(年・季節)は <see cref="GameDate"/> による読み替えで、状態としては持たない(ADR-0003)。</summary>
    public Tick Now { get; internal set; }

    /// <summary>Id 昇順。添字 = NpcId(TDD01 §3.2)。</summary>
    public NpcState[] Npcs { get; }

    /// <summary>品目 × 売り手 → 提示価格。W2 以降で中身が入る(TDD01 §3.2)。</summary>
    public SortedDictionary<MarketKey, int> Market { get; }

    /// <summary>信用の疎マップ。W2 以降で中身が入る(GDD01 §2.1)。</summary>
    public SortedDictionary<TrustKey, TrustScore> TrustLedger { get; }

    /// <summary>W2 以降で中身が入る(GDD01 §3.2)。</summary>
    public List<Need> Needs { get; }

    /// <summary>W2 以降で中身が入る(GDD01 §2.8)。</summary>
    public List<Promise> Promises { get; }

    /// <summary>W2 以降で中身が入る(GDD01 §4.1)。</summary>
    public List<PriceObservation> Knowledge { get; }

    /// <summary>W2 以降で中身が入る(GDD01 §4.4)。</summary>
    public List<LedgerEntry> Ledgers { get; }

    /// <summary>ドメインイベントの追記専用列。W2 以降で中身が入る(TDD01 §3.4)。</summary>
    public List<DomainEvent> EventLog { get; }
}
