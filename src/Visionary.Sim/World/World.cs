using Visionary.Sim.Metrics;
using Visionary.Sim.Time;

namespace Visionary.Sim;

/// <summary>
/// 全状態を保持する単一の集約(TDD01 §3.2)。
/// </summary>
/// <remarks>
/// <para>
/// <b>本書でいう「区分」は World の内訳である。</b>GDD02 以降の「区画」は都市の空間的な
/// 3×3 の9区画(GDD02 §4.3)を指し、別概念である(TDD01 §3.2)。
/// </para>
/// <para>
/// <b>経済主体は世帯である。</b>流動資金・在庫・帳簿・職業は <see cref="Households"/> が持ち、
/// 個人に残るのは相場知識・信用・熟練度・階層だけである(TDD01 §3.2 / GDD08 §2.2)。
/// </para>
/// </remarks>
public sealed class World
{
    /// <summary>
    /// NPC を <paramref name="npcCount"/> 体、世帯を <paramref name="householdCount"/> 戸、
    /// それぞれ Id 昇順(0始まり)で用意する。在庫は <paramref name="itemCount"/> 品目ぶん確保する。
    /// </summary>
    /// <remarks>
    /// <b>中身は入れない。</b>職業・区画・構成員・初期在庫・初期資金の値は初期配置の担当
    /// (GDD02 §2.4)であり、ここでは器だけを確保する。
    /// </remarks>
    public World(int npcCount, int householdCount, int itemCount)
    {
        if (npcCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(npcCount), npcCount, "NPC数は非負。");
        }

        if (householdCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(householdCount), householdCount, "世帯数は非負。");
        }

        if (itemCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(itemCount), itemCount, "品目数は非負(GDD02 §2.2)。");
        }

        Npcs = new NpcState[npcCount];
        Knowledge = new List<PriceObservation>[npcCount];

        for (int npcId = 0; npcId < npcCount; npcId++)
        {
            Npcs[npcId] = new NpcState(npcId);
            Knowledge[npcId] = new List<PriceObservation>();
        }

        Households = new HouseholdState[householdCount];
        Ledgers = new List<LedgerEntry>[householdCount];

        for (int householdId = 0; householdId < householdCount; householdId++)
        {
            Households[householdId] = new HouseholdState(
                id: householdId,
                districtId: 0,
                headNpcId: 0,
                memberNpcIds: new[] { 0 },
                itemCount: itemCount);

            Ledgers[householdId] = new List<LedgerEntry>();
        }

        Market = new SortedDictionary<MarketKey, int>();
        TrustLedger = new SortedDictionary<TrustKey, TrustScore>();
        Needs = new List<Need>();
        NextNeedId = 0;
        Promises = new List<Promise>();
        EventLog = new List<DomainEvent>();

        // 順5(Trade)の当日ぶんの計数(TDD01 §4.2)。ハッシュ対象外(§3.8) ──
        // StateHasher.Compute はこの区分を読まない(W2-20 タスク仕様)。
        Metrics = new MetricsScratch(householdCount, itemCount);
    }

    /// <summary>現在tick。暦(年・季節)は <see cref="GameDate"/> による読み替えで、状態としては持たない(ADR-0003)。</summary>
    public Tick Now { get; internal set; }

    /// <summary>Id 昇順。添字 = NpcId(TDD01 §3.2)。</summary>
    public NpcState[] Npcs { get; }

    /// <summary>Id 昇順。添字 = 世帯Id(TDD01 §3.2)。</summary>
    public HouseholdState[] Households { get; }

    /// <summary>品目 × 売り手世帯 → 提示価格。Id昇順の疎構造(TDD01 §3.2 / GDD02c §1)。</summary>
    public SortedDictionary<MarketKey, int> Market { get; }

    /// <summary>信用の疎マップ(GDD01 §2.1)。</summary>
    public SortedDictionary<TrustKey, TrustScore> TrustLedger { get; }

    /// <summary>不足(GDD01 §3.2)。主体は世帯(<see cref="Need.TargetHouseholdId"/>)。</summary>
    public List<Need> Needs { get; }

    /// <summary>
    /// 次に払い出す Need の Id。非負。単調増加で、失効した Id を再利用しない ──
    /// 再利用すると、失効前の Need を指していた <see cref="Promise.NeedId"/> が、
    /// 同じ Id で立った別の Need を指してしまう(<see cref="Systems.NeedGenerationSystem"/>)。
    /// </summary>
    public int NextNeedId { get; internal set; }

    /// <summary>約束(GDD01 §2.8)。</summary>
    public List<Promise> Promises { get; }

    /// <summary>
    /// 相場知識。<b>添字 = NpcId</b>(GDD01 §4.1 / TDD01 §3.2)。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>所有者は個人である。</b>「その人が何を見たか」に依存する(GDD08 §2.2)。
    /// 添字が Id 昇順そのものなので、走査が ADR-0002 の列挙順規約を構造で満たす。
    /// </para>
    /// <para>
    /// <b><see cref="Ledgers"/> と型を共通化してはならない</b>(TDD01 §3.2)。どちらも
    /// <c>List&lt;T&gt;[]</c> だが添字の意味が違う — こちらは NpcId、あちらは世帯Id である。
    /// <b>取り違えを型で防ぐことはできない。</b><c>Knowledge[世帯Id]</c> と書いてもコンパイルは
    /// 通る。検出は「所有者を取り違えるとハッシュが変わる」回帰テストに頼る。
    /// </para>
    /// </remarks>
    public List<PriceObservation>[] Knowledge { get; }

    /// <summary>
    /// 帳簿(取引履歴)。<b>添字 = 世帯Id</b>(GDD01 §4.4 / TDD01 §3.2)。
    /// 資金の増減と突合するため所有者は世帯である(GDD08 §2.2)。
    /// </summary>
    public List<LedgerEntry>[] Ledgers { get; }

    /// <summary>ドメインイベントの追記専用列。ハッシュ対象外(TDD01 §3.4 / §3.8)。</summary>
    public List<DomainEvent> EventLog { get; }

    /// <summary>
    /// 順5(Trade)の当日ぶんの計数(TDD01 §4.2)。<b>ハッシュ対象外</b>(§3.8。
    /// <see cref="Determinism.StateHasher.Compute"/> はこの区分を読まない)。
    /// シムの意思決定には一切関与しない(<see cref="Systems.MetricsSystem"/> だけが読む)。
    /// </summary>
    public MetricsScratch Metrics { get; }
}
