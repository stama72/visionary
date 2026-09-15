namespace Visionary.Sim;

/// <summary>
/// 世帯の状態(TDD01 §3.2「経済主体は世帯である」)。流動資金・在庫・帳簿・職業の主体。
/// </summary>
/// <remarks>
/// <para>
/// <b>在庫は2本ある。</b>世帯在庫(消費財)と工房在庫(生産の入出力)を分けるのは
/// 構造的な要請である — 薪(itemId 5)は必需の消費財でもパン・ビールの生産入力でもあり
/// (GDD02 §2.2)、1本では GDD02 §8.2.1 の目標在庫が一意に決まらない。
/// </para>
/// <para>
/// <b>区画 Id と世帯主と構成員は不変。</b>区画が不変なのは GDD02 §4.3「世帯は区画を移らない」、
/// 構成員が M0 で不変なのは世代交代(GDD02 §11)が M0 スコープ外だから。
/// <see cref="Occupation"/> だけが可変なのは GDD02 §6.3 ④の職業付け替えがあるため。
/// </para>
/// </remarks>
public sealed class HouseholdState
{
    private int isBankrupt;
    private int toolWearCount;

    /// <summary><see cref="World.Households"/> の添字と一致する、非負の Id(TDD01 §3.2)。</summary>
    public int Id { get; }

    /// <summary>区画 Id 0〜8(GDD02 §4.3)。初期配置で決まり、以後変わらない。</summary>
    public int DistrictId { get; }

    /// <summary>
    /// 職業。値の定義(0〜4)は GDD02 §2.4 が持つ。GDD02 §6.3 ④の職業付け替えで変わる
    /// ため <c>set</c> を残す。
    /// </summary>
    public Occupation Occupation { get; set; }

    /// <summary>世帯主の NpcId(GDD08 §2.1)。</summary>
    public int HeadNpcId { get; }

    /// <summary>構成員の NpcId。<b>昇順</b>。<see cref="HeadNpcId"/> を含む(GDD08 §2.1)。</summary>
    /// <remarks>
    /// <para>
    /// <b>構築時に検証し、渡された配列を複製して持つ</b>(昇順・重複なし・世帯主を含む・非負)。
    /// これで「呼び出し側が検証を通した後に、渡した配列を並べ替える」経路は防げる。
    /// </para>
    /// <para>
    /// <b>ただし、この property を受け取った側が並べ替える経路は防げない。</b>
    /// <c>int[]</c> をそのまま公開しているためである。<c>IReadOnlyList&lt;int&gt;</c> として
    /// 宣言しても同じで、配列は <c>IReadOnlyList&lt;int&gt;</c> を実装しているので
    /// <c>int[]</c> へ戻せてしまう(別インスタンスの <c>ReadOnlyCollection&lt;int&gt;</c> で
    /// 包めば防げるが、そこまでの手当てはしていない)。<b>読むだけにすること。</b>
    /// 昇順が崩れると世帯内の処理順(GDD02 §6.2.1 の購入の決済順)が入力次第になり、
    /// ADR-0002 の列挙順規約が破れる。
    /// </para>
    /// </remarks>
    public int[] MemberNpcIds { get; }

    /// <summary>手元の流動資金。単位は貨幣(GDD02 §6.2)。</summary>
    public int LiquidFunds { get; set; }

    /// <summary>世帯在庫(消費財)。添字 = itemId。</summary>
    public int[] HouseholdInventory { get; }

    /// <summary>工房在庫(生産の入出力)。添字 = itemId。</summary>
    public int[] WorkshopInventory { get; }

    /// <summary>
    /// 仕入れ移動平均単価。添字 = itemId。単位: 貨幣/1単位(GDD02 §8.1.1)。
    /// </summary>
    /// <remarks>
    /// <b>更新規則は本タスク(#33)に無い。</b>コンストラクタは長さ <c>itemCount</c> の配列を
    /// 0 で確保するだけで、初期値を書き込むのは <see cref="WorldGenerator"/> である。
    /// 窓付きにするか再帰形にするかは原価の式を書く #35 が決める。
    /// </remarks>
    public int[] PurchaseUnitCostAverage { get; }

    /// <summary>
    /// 破産中フラグ(GDD02 §6.2.2)。0 / 1。<b>bool を使わない</b> —
    /// 状態はすべて int/long(TDD01 §3.2)、ハッシュ入力も int/long のみ(§3.8)。
    /// </summary>
    /// <remarks>
    /// <b>0 / 1 以外を setter で拒む。</b>bool の代わりに int を使う以上、値域は型では守れない。
    /// 2 や -1 が入ると、GDD02 §6.2.2 の②(値付けで原価下限を 500‰ へ下げる)と④のゲートを
    /// <c>== 1</c> で書いた実装と <c>!= 0</c> で書いた実装が食い違う。
    /// </remarks>
    public int IsBankrupt
    {
        get => isBankrupt;
        set
        {
            if (value is not (0 or 1))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "破産中フラグは 0 / 1(GDD02 §6.2.2)。");
            }

            isBankrupt = value;
        }
    }

    /// <summary>
    /// 都市外市場の窓口を指す、予約済みの売り手 Id(TDD01 §3.2 / GDD02 §10.2)。
    /// </summary>
    /// <remarks>
    /// 都市外市場は <see cref="World"/> の区分を持たず、売り注文を <see cref="World.Market"/> に
    /// 実体化しない(GDD02 §10.2)。この定数は <see cref="PriceObservation.SellerId"/> と
    /// <see cref="LedgerEntry.CounterpartyId"/> の値としてのみ使う。
    /// <para>
    /// <b><see cref="int.MaxValue"/> を選ぶのは、世帯数に依存しないからである。</b>
    /// TDD01 §3.2 は「既存の世帯 Id より大きい非負 int を1つ窓口に予約する」「売り手 Id 昇順の
    /// 走査では合成した候補が最後に来る」と要求している。世帯数から導く値だと、世帯数を変えた
    /// 実験で既存の世帯 Id と衝突しうる。
    /// </para>
    /// </remarks>
    public const int ExternalMarketSellerId = int.MaxValue;

    /// <summary>累積した工具の摩耗(レシピ実行回数)。0以上(GDD02 §5.3)。</summary>
    /// <remarks>
    /// <b>上限(N)は型では守れない</b> — <c>N</c> を知っているのは <c>WorldDefinition</c> であって
    /// この型ではない。「工具在庫がある間は 0 ≤ ToolWearCount &lt; N」は
    /// <see cref="Systems.ProductionSystem"/> の後条件であり、テストで押さえる。
    /// 負を setter で拒むのは、負になると <c>FloorDiv</c> が負の商を返し工具在庫が
    /// 増えてしまうため。
    /// </remarks>
    public int ToolWearCount
    {
        get => toolWearCount;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "工具の摩耗カウンタは0以上(GDD02 §5.3)。");
            }

            toolWearCount = value;
        }
    }

    /// <summary>当日の消費不足量。添字 = itemId。単位: 個(GDD02 §6.1 / §8.4)。</summary>
    /// <remarks>
    /// <see cref="Systems.ConsumptionSystem"/> が毎日、不足の有無にかかわらず全品目を
    /// 上書きする(#40 が <c>Need.Quantity</c> の入力として読む)。
    /// </remarks>
    public int[] UnmetConsumption { get; }

    public HouseholdState(int id, int districtId, int headNpcId, int[] memberNpcIds, int itemCount)
    {
        if (id < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "世帯の Id は非負(TDD01 §3.2)。");
        }

        if (districtId < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(districtId), districtId, "区画 Id は非負(GDD02 §4.3)。");
        }

        if (headNpcId < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(headNpcId), headNpcId, "世帯主の NpcId は非負(TDD01 §3.2)。");
        }

        if (itemCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(itemCount), itemCount, "品目数は非負(GDD02 §2.2)。");
        }

        ArgumentNullException.ThrowIfNull(memberNpcIds);

        bool containsHead = false;

        for (int i = 0; i < memberNpcIds.Length; i++)
        {
            if (memberNpcIds[i] < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(memberNpcIds), memberNpcIds[i], "構成員の NpcId は非負(TDD01 §3.2)。");
            }
            if (i > 0 && memberNpcIds[i - 1] > memberNpcIds[i])
            {
                throw new ArgumentException(
                    "構成員の NpcId は昇順である必要がある(ADR-0002 の列挙順規約)。",
                    nameof(memberNpcIds));
            }
            if (i > 0 && memberNpcIds[i - 1] == memberNpcIds[i])
            {
                throw new ArgumentException(
                    "構成員の NpcId に重複がないこと。",
                    nameof(memberNpcIds));
            }
            containsHead |= memberNpcIds[i] == headNpcId;
        }

        if (!containsHead)
        {
            throw new ArgumentException(
                "構成員には世帯主の NpcId が含まれている必要がある(GDD08 §2.1)。",
                nameof(memberNpcIds));
        }

        Id = id;
        DistrictId = districtId;
        HeadNpcId = headNpcId;
        MemberNpcIds = memberNpcIds.ToArray();

        Occupation = default;
        LiquidFunds = 0;
        HouseholdInventory = new int[itemCount];
        WorkshopInventory = new int[itemCount];
        PurchaseUnitCostAverage = new int[itemCount];
        UnmetConsumption = new int[itemCount];
        IsBankrupt = 0;
        ToolWearCount = 0;
    }
}
