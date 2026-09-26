namespace Visionary.Sim;

/// <summary>
/// 世帯の状態(TDD01 §3.2「経済主体は世帯である」)。流動資金・在庫・帳簿・職業の主体。
/// </summary>
/// <remarks>
/// <para>
/// <b>在庫は2本ある。</b>世帯在庫(消費財)と工房在庫(生産の入出力)を分けるのは
/// 構造的な要請である — 薪(itemId 5)は必需の消費財でもパン・ビールの生産入力でもあり
/// (GDD02 §2.2)、1本では GDD02b §2 の目標在庫が一意に決まらない。
/// </para>
/// <para>
/// <b>区画 Id と世帯主と構成員は不変。</b>区画が不変なのは GDD02 §4.3「世帯は区画を移らない」、
/// 構成員が M0 で不変なのは世代交代(GDD02 §7)が M0 スコープ外だから。
/// <see cref="Occupation"/> だけが可変なのは GDD02b §4.2 ④の職業付け替えがあるため。
/// </para>
/// </remarks>
public sealed class HouseholdState
{
    private int isBankrupt;
    private int toolWear;
    private int unaffordableNecessityCount;
    private int errandLaborLossPermille;
    private int productionRuns;
    private int productionProgressPermille;
    private int productionCapacityRuns;

    /// <summary><see cref="World.Households"/> の添字と一致する、非負の Id(TDD01 §3.2)。</summary>
    public int Id { get; }

    /// <summary>区画 Id 0〜8(GDD02 §4.3)。初期配置で決まり、以後変わらない。</summary>
    public int DistrictId { get; }

    /// <summary>
    /// 職業。値の定義(0〜4)は GDD02 §2.4 が持つ。GDD02b §4.2 ④の職業付け替えで変わる
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
    /// 昇順が崩れると世帯内の処理順(GDD02b §3.2 の購入の決済順)が入力次第になり、
    /// ADR-0002 の列挙順規約が破れる。
    /// </para>
    /// </remarks>
    public int[] MemberNpcIds { get; }

    /// <summary>手元の流動資金。単位は貨幣(GDD02b §3)。</summary>
    public int LiquidFunds { get; set; }

    /// <summary>世帯在庫(消費財)。添字 = itemId。</summary>
    public int[] HouseholdInventory { get; }

    /// <summary>工房在庫(生産の入出力)。添字 = itemId。</summary>
    public int[] WorkshopInventory { get; }

    /// <summary>
    /// 仕入れ移動平均単価。添字 = itemId。単位: 貨幣/1単位(GDD02a §5.1)。
    /// </summary>
    /// <remarks>
    /// <b>更新規則は本タスク(#33)に無い。</b>コンストラクタは長さ <c>itemCount</c> の配列を
    /// 0 で確保するだけで、初期値を書き込むのは <see cref="WorldGenerator"/> である。
    /// 窓付きにするか再帰形にするかは原価の式を書く #35 が決める。
    /// </remarks>
    public int[] PurchaseUnitCostAverage { get; }

    /// <summary>
    /// 破産中フラグ(GDD02b §3.3)。0 / 1。<b>bool を使わない</b> —
    /// 状態はすべて int/long(TDD01 §3.2)、ハッシュ入力も int/long のみ(§3.8)。
    /// </summary>
    /// <remarks>
    /// <b>0 / 1 以外を setter で拒む。</b>bool の代わりに int を使う以上、値域は型では守れない。
    /// 2 や -1 が入ると、GDD02c §1.4 の②(価格係数‰ を 500 に固定する。床は破らない)と
    /// GDD02b §4.1 の④のゲートを <c>== 1</c> で書いた実装と <c>!= 0</c> で書いた実装が食い違う。
    /// </remarks>
    public int IsBankrupt
    {
        get => isBankrupt;
        set
        {
            if (value is not (0 or 1))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "破産中フラグは 0 / 1(GDD02b §3.3)。");
            }

            isBankrupt = value;
        }
    }

    /// <summary>
    /// 都市外市場の窓口を指す、予約済みの売り手 Id(TDD01 §3.2 / GDD02d §2.1)。
    /// </summary>
    /// <remarks>
    /// 都市外市場は <see cref="World"/> の区分を持たず、売り注文を <see cref="World.Market"/> に
    /// 実体化しない(GDD02d §2.1)。この定数は <see cref="PriceObservation.SellerId"/> と
    /// <see cref="LedgerEntry.CounterpartyId"/> の値としてのみ使う。
    /// <para>
    /// <b><see cref="int.MaxValue"/> を選ぶのは、世帯数に依存しないからである。</b>
    /// TDD01 §3.2 は「既存の世帯 Id より大きい非負 int を1つ窓口に予約する」「売り手 Id 昇順の
    /// 走査では合成した候補が最後に来る」と要求している。世帯数から導く値だと、世帯数を変えた
    /// 実験で既存の世帯 Id と衝突しうる。
    /// </para>
    /// </remarks>
    public const int ExternalMarketSellerId = int.MaxValue;

    /// <summary>
    /// 累積した工具の摩耗。単位: ‰人日。0以上(GDD02a §3.1)。旧 <c>ToolWearCount</c>(回)を改名した
    /// ── 所要労働‰ が108から1300まで違うので、回数で数えると木材加工は鍛冶の12倍の速さで
    /// 工具を消費する(タスク仕様「摩耗は実行回数ではなく労働量で数える」)。
    /// </summary>
    /// <remarks>
    /// <b>上限(N × 1000)は型では守れない</b> — <c>N</c> を知っているのは
    /// <see cref="WorldDefinition.ToolDurabilityPerUnit"/> であってこの型ではない。
    /// 「工具在庫がある間は 0 ≤ ToolWear &lt; N × 1000」は <see cref="Systems.ProductionSystem"/> の
    /// 後条件であり、テストで押さえる。負を setter で拒むのは、負になると <c>FloorDiv</c> が
    /// 負の商を返し工具在庫が増えてしまうため。
    /// </remarks>
    public int ToolWear
    {
        get => toolWear;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "工具の摩耗は0以上(GDD02a §3.1)。");
            }

            toolWear = value;
        }
    }

    /// <summary>
    /// 前日の外出の労働損失‰。0以上(GDD02a §2 / GDD06 §2)。順5(#98)が当日の外出の合計を
    /// 毎日上書きし、翌日の順1(<see cref="Systems.ProductionSystem"/>)が読む。
    /// </summary>
    /// <remarks>
    /// <b>上限(<see cref="WorldDefinition.NominalLaborPermille"/>)は型では守れない。</b>
    /// 超える値が来ても <see cref="Systems.ProductionSystem"/> が <c>max(0, …)</c> で0へ潰す
    /// (GDD02a §2)。
    /// <para>
    /// <b>書き手は2人であり、順序と意味が違う(#38)。</b>
    /// <see cref="Systems.TradeSystem"/> の段5a(<see cref="Systems.ErrandPlanner"/>、買い物の外出)が
    /// 当日の合計を<b>上書き</b>する(<c>=</c>) ── 外出しない日も0を書く(書かない日があると
    /// 前日の損失が翌日以降も効き続ける)。続く段6(輸出。GDD06 §3「外出ごとに切り上げてから
    /// 合計する」)は、段5aが書いたその日の値へ<b>加算</b>する(<c>+=</c>)。
    /// <b>段6を <c>=</c> に直すと段5aの買い物の労働損失が消える</b>(タスク仕様W2-12。
    /// 守っているのはテスト <c>ErrandPlannerTests</c> ではなく
    /// <c>TradeSystemTests.ExportAddsToTheErrandLaborLoss</c> の1件だけである)。
    /// </para>
    /// </remarks>
    public int ErrandLaborLossPermille
    {
        get => errandLaborLossPermille;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "外出の労働損失‰は0以上(GDD02a §2)。");
            }

            errandLaborLossPermille = value;
        }
    }

    /// <summary>
    /// 当日の生産量(実行回数)。0以上(GDD02a §1)。順1(<see cref="Systems.ProductionSystem"/>)が
    /// 毎日書く(0の日も書く)。順3の④のゲート(#39)と順4のNeed(#40)が読む。
    /// </summary>
    public int ProductionRuns
    {
        get => productionRuns;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "当日の生産量(実行回数)は0以上(GDD02a §1)。");
            }

            productionRuns = value;
        }
    }

    /// <summary>
    /// 生産の進捗‰(持ち越した端数、GDD02a §1)。単位: ‰人日。0以上。
    /// 順1(<see cref="Systems.ProductionSystem"/>)が毎日書く。④の付け替え(順3、
    /// <see cref="Systems.HouseholdSystem"/>)で0に戻す。
    /// </summary>
    /// <remarks>
    /// <b>上限(進捗‰ &lt; 所要労働‰)は型では守れない。</b>所要労働‰ を知っているのはレシピ
    /// (<see cref="Recipe"/>)であり、この型ではない。「順1 の後は 0 ≤ 進捗‰ ≤ 所要労働‰ − 1」は
    /// <see cref="Systems.ProductionSystem"/> の後条件であり、テストで押さえる。
    /// </remarks>
    public int ProductionProgressPermille
    {
        get => productionProgressPermille;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "生産の進捗‰は0以上(GDD02a §1)。");
            }

            productionProgressPermille = value;
        }
    }

    /// <summary>
    /// 当日の生産能力(実行回数、GDD02a §1・GDD02b §8)。0以上。順1が毎日書く(0の日も書く)。
    /// 順4(<see cref="Systems.NeedGenerationSystem"/>)の「増産できない」が読む。
    /// </summary>
    /// <remarks>
    /// <b><see cref="ProductionRuns"/>(入力充足も反映した実行回数)とは別の欄である。</b>
    /// 入力が足りない日は <see cref="ProductionRuns"/> より大きくなりうる ── 「増産できない」の
    /// 判定(GDD02b §8)は労働力側の能力だけを見るため、入力切れとは独立に持つ必要がある。
    /// </remarks>
    public int ProductionCapacityRuns
    {
        get => productionCapacityRuns;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "当日の生産能力(実行回数)は0以上(GDD02a §1)。");
            }

            productionCapacityRuns = value;
        }
    }

    /// <summary>当日の消費不足量。添字 = itemId。単位: 個(GDD02b §1 / §8)。</summary>
    /// <remarks>
    /// <see cref="Systems.ConsumptionSystem"/> が毎日、不足の有無にかかわらず全品目を
    /// 上書きする(#40 が <c>Need.Quantity</c> の入力として読む)。
    /// </remarks>
    public int[] UnmetConsumption { get; }

    /// <summary>
    /// 当日、1個も買えず、かつ予想在庫が目標在庫を下回っていた量。添字 = itemId。単位: 個
    /// (耐久(工具)も耐久値ではなく個数で入る。GDD06 §3.1)。
    /// </summary>
    /// <remarks>
    /// 書き手は <see cref="Systems.TradeSystem"/> 段5b だけである(<see cref="UnaffordableNecessityCount"/>
    /// と同じく毎日0クリアしてから書く)。読み手は翌日の <see cref="Systems.NeedGenerationSystem"/>
    /// (理由 <see cref="NeedReason.DistantStock"/>)。
    /// </remarks>
    public int[] UnfilledPurchase { get; }

    /// <summary>
    /// 当日、必需品を「資金不足で買えなかった」購入の件数(GDD02b §3.2 / §3.3)。0以上。
    /// </summary>
    /// <remarks>
    /// <b>#39 の破産中フラグの入力である。</b>順3 Household が読む時点ではまだ前日の値であり
    /// (順5 Trade が上書きするのはその後)、GDD02b §3.3「前日の購入結果を評価する」が
    /// 順序の帰結として成立する。<b>フラグそのものは本タスクでは立てない。</b>
    /// </remarks>
    public int UnaffordableNecessityCount
    {
        get => unaffordableNecessityCount;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "資金不足で買えなかった件数は0以上(GDD02b §3.2)。");
            }

            unaffordableNecessityCount = value;
        }
    }

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
        UnfilledPurchase = new int[itemCount];
        IsBankrupt = 0;
        ToolWear = 0;
        UnaffordableNecessityCount = 0;
        ErrandLaborLossPermille = 0;
        ProductionRuns = 0;
        ProductionProgressPermille = 0;
        ProductionCapacityRuns = 0;
    }
}
