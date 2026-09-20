using Visionary.Sim.Numerics;
using Visionary.Sim.Time;

namespace Visionary.Sim;

/// <summary>
/// 世界の定義。<see cref="World"/> の外に置く(TDD01 §3.8)。
/// </summary>
/// <remarks>
/// <para>
/// 品目表・レシピ表・労働力係数・区画間距離と同じく<b>状態ではない</b>。外部価格は設定と
/// 暦から決まる定数であり、ハッシュが対象とするのは <see cref="World"/> の状態であって
/// 世界の定義ではない(TDD01 §3.8)。
/// </para>
/// <para>
/// <b>世帯の人数を可変値にしない。</b>親方1・徒弟1 は GDD02 §2.4 の決定であり、
/// 人数だけ可変にすると「3人目の階層が未定義」という穴になる。人数は
/// <see cref="NpcCount"/> として導出する。
/// </para>
/// </remarks>
public sealed class WorldDefinition
{
    /// <summary>品目数(GDD02 §2.2)。</summary>
    public int ItemCount { get; }

    /// <summary>1職業あたりの世帯数(GDD02 §2.4)。</summary>
    public int HouseholdsPerOccupation { get; }

    /// <summary>添字 = (int)Occupation のレシピ表。</summary>
    public Recipe[] Recipes { get; }

    /// <summary>初期の手元資金。単位: 貨幣。</summary>
    public int InitialLiquidFunds { get; }

    /// <summary>初期の仕入れ移動平均単価。添字 = itemId。単位: 貨幣/1単位(GDD02 §8.1)。</summary>
    public int[] InitialAcquisitionCost { get; }

    /// <summary>初期の世帯在庫(消費財)。添字 = itemId。単位: 個。</summary>
    public int[] InitialHouseholdInventory { get; }

    /// <summary>
    /// 工房在庫[入力j] = これ × <see cref="DailyInputQuantity"/>(occupation, j) として
    /// 初期化する日数。単位: 日(GDD02a §4)。
    /// </summary>
    /// <remarks>
    /// <b>旧は「× 必要数量」(1実行/日 の前提)だった。</b>生産能力が1実行/日 に固定されなくなった
    /// (#96)ため、「× 1日の投入量」に変えないと、生産能力が2以上の職業の初日が入力切れで
    /// 生産停止してしまう。
    /// </remarks>
    public int InitialWorkshopInputDays { get; }

    /// <summary>工房在庫[Item.Tools] の初期値。単位: 個。1以上(下記コンストラクタ参照)。</summary>
    public int InitialToolStock { get; }

    /// <summary>添字 = (int)NpcRank の初期熟練度。長さ3。単位: ‰。</summary>
    public int[] InitialSkillPermilleByRank { get; }

    /// <summary>添字 = (int)NpcRank の労働力係数‰。長さ3(GDD02 §5.2)。</summary>
    public int[] LaborPermilleByRank { get; }

    /// <summary>1人1日あたりの消費量。添字 = [(int)NpcRank][itemId]。長さ3 × itemCount(GDD02 §6.1)。</summary>
    public int[][] DailyConsumptionPerNpcByRank { get; }

    /// <summary>添字 = (int)Season の薪の消費の季節係数‰。長さ4(GDD02 §9 / GDD03 §2.1)。</summary>
    public int[] FirewoodConsumptionSeasonPermille { get; }

    /// <summary>最低利幅‰。利潤上限の許容原価合計(GDD02c §2.3)。値付けからは消えた。</summary>
    public int MinimumMarginPermille { get; }

    /// <summary>相場観測の保持期間。単位: 日(GDD06 §3.1)。</summary>
    public int ObservationRetentionDays { get; }

    /// <summary>必需の目標在庫の日数。添字 = itemId。単位: 日(GDD02 §8.2.1)。0 = 必需ではない。</summary>
    public int[] NecessityTargetStockDays { get; }

    /// <summary>嗜好・奢侈の目標在庫の日数。添字 = itemId。単位: 日(同)。0 = 嗜好ではない。</summary>
    public int[] PreferenceTargetStockDays { get; }

    /// <summary>工具の目標在庫‰。1個あたりの耐久値に対する比率(GDD02 §8.2.1)。単位: ‰。</summary>
    public int ToolTargetStockPermille { get; }

    /// <summary>階層係数‰。添字 = (int)NpcRank。長さ3(GDD08 §9)。単位: ‰。</summary>
    public int[] RankCoefficientPermille { get; }

    /// <summary>
    /// 許容乖離‰。相場項 = ApplyPermille(相場基準, これ)(GDD02c §2.1)。単位: ‰。
    /// </summary>
    /// <remarks>
    /// <b>全用途の相場項が使う</b>(GDD02c §2.1 の表)── 必需専用だった旧
    /// <c>NecessityTolerancePermille</c> を改名した。専用だと読める名前のまま全用途に配ると、
    /// 片方の用途だけ別の値にしたくなったときに黙って全部動く(本タスク仕様)。
    /// </remarks>
    public int TolerancePermille { get; }

    /// <summary>
    /// 機会費用の職業別の基準値。添字 = (int)Occupation。単位: 貨幣/1時間(GDD08 §9)。
    /// </summary>
    public int[] OpportunityCostBaseByOccupation { get; }

    /// <summary>1区画あたりの移動時間。単位: 時間(GDD02 §4.3)。</summary>
    public int TravelHoursPerDistrict { get; }

    /// <summary>仕入れ移動平均単価の平滑化係数‰(GDD02 §8.1.1)。単位: ‰。</summary>
    public int AcquisitionCostSmoothingPermille { get; }

    /// <summary>
    /// 外部売値の基準値(年平均)。添字 = itemId。単位: 貨幣/1単位(GDD02d §2.1・§4.4)。
    /// <b>1次産品(どのレシピも出力しない品目)だけが1以上、都市生産品は0。</b>
    /// </summary>
    /// <remarks>読み口は <see cref="ExternalSellPrice"/>。表そのものは公開しない。</remarks>
    private readonly int[] _externalSellPriceBase;

    /// <summary>
    /// 外部売値の季節係数‰。添字 = [itemId][(int)Season]。長さ itemCount × 4(GDD02d §5 / GDD03 §2.2)。
    /// 都市生産品の行はすべて1000(使わないが、黙って効く値を置かせない)。
    /// </summary>
    /// <remarks>読み口は <see cref="ExternalSellPrice"/>。表そのものは公開しない。</remarks>
    private readonly int[][] _externalSellPriceSeasonPermille;

    /// <summary>
    /// 外部買値。添字 = itemId。単位: 貨幣/1単位(GDD02d §2.1・§3)。<b>都市生産品だけが1以上、
    /// 1次産品は0。</b>提示価格の床であり、輸出の値でもある(#97 / #38 が読む)。季節係数は掛からない。
    /// </summary>
    /// <remarks>読み口は <see cref="ExternalBuyPrice"/>。表そのものは公開しない。</remarks>
    private readonly int[] _externalBuyPrice;

    /// <summary>どのレシピも出力しない品目(= 1次産品)かの表。添字 = itemId(GDD02 §2.2)。</summary>
    private readonly bool[] _isPrimaryItemByItemId;

    /// <summary>入力の緩衝日数 D。単位: 日。1以上(GDD02a §4)。</summary>
    public int InputBufferDays { get; }

    /// <summary>出荷日数。単位: 日。1以上(GDD02c §1.3)。</summary>
    public int ShipmentDays { get; }

    /// <summary>工具1個が尽きる労働量 N。単位: 人日。1以上(GDD02a §3.1)。</summary>
    public int ToolLifeLaborDays { get; }

    /// <summary>工具が無いときの設備係数‰。0〜1000(GDD02a §3)。</summary>
    public int EquipmentPermilleWithoutTools { get; }

    /// <summary>可処分時間 T。単位: 時間。1以上(GDD08 §3.1。GDD02a §2 の労働損失の分母)。</summary>
    public int DisposableHours { get; }

    /// <summary>
    /// 信用による実効価格の割引係数 α‰(GDD01 §2.2 効果1)。単位: ‰。信用100で
    /// <c>trustDiscountPermille</c>‰の割引(M0の既定200‰なら2割引)。<b>値域は0〜999</b>
    /// (1000を含めない。1000ちょうどだと信用100で実効価格が0になり下流が投げる)。
    /// </summary>
    public int TrustDiscountPermille { get; }

    /// <summary>職業数。<see cref="Recipes"/> の長さから導く。</summary>
    public int OccupationCount => Recipes.Length;

    /// <summary>世帯数。職業数 × 1職業あたりの世帯数(GDD02 §2.4)。</summary>
    public int HouseholdCount => OccupationCount * HouseholdsPerOccupation;

    /// <summary>NPC数。世帯あたり親方1・徒弟1(GDD02 §2.4)。</summary>
    public int NpcCount => HouseholdCount * 2;

    /// <summary>
    /// 親方1・徒弟1 の労働力係数‰の合計(GDD02 §2.4 / GDD02a §2)。外出の損失を引く前の値。
    /// </summary>
    /// <remarks>
    /// <b>実際の世帯の構成員(<see cref="HouseholdState.MemberNpcIds"/>)から求めるのではない。</b>
    /// 目標在庫の物差しとして使うのは <see cref="Systems.ProductionSystem"/> の日ごとの実行回数
    /// ではなく、世界の設計として想定した「親方+徒弟」の名目値である(GDD02a §4「日ごとに動く
    /// 損失で物差しを揺らさない」)。
    /// </remarks>
    public int NominalLaborPermille { get; }

    /// <summary>工具1個の耐久値 = <see cref="ToolLifeLaborDays"/> × 1000。単位: ‰人日(GDD02a §3.1)。</summary>
    public int ToolDurabilityPerUnit { get; }

    /// <summary>M0 の初期値(下記表)。毎回新しいインスタンスを組み立てて返す。</summary>
    /// <remarks>
    /// <b><c>static</c> な可変テーブルにしない。</b>#28 が値を差し替え、TDD01 §4.1 は
    /// JSON 設定を予定している。加えて、<c>static</c> な可変配列は
    /// <c>StateHasherCoverageTests</c> の凍結検査にも <c>StateHasher</c> にも2プロセス比較にも
    /// 一切現れない。
    /// </remarks>
    public static WorldDefinition M0 => BuildM0();

    public WorldDefinition(
        int itemCount,
        int householdsPerOccupation,
        Recipe[] recipes,
        int initialLiquidFunds,
        int[] initialAcquisitionCost,
        int[] initialHouseholdInventory,
        int initialWorkshopInputDays,
        int initialToolStock,
        int[] initialSkillPermilleByRank,
        int[] laborPermilleByRank,
        int[][] dailyConsumptionPerNpcByRank,
        int[] firewoodConsumptionSeasonPermille,
        int minimumMarginPermille,
        int observationRetentionDays,
        int[] necessityTargetStockDays,
        int[] preferenceTargetStockDays,
        int toolTargetStockPermille,
        int[] rankCoefficientPermille,
        int tolerancePermille,
        int[] opportunityCostBaseByOccupation,
        int travelHoursPerDistrict,
        int acquisitionCostSmoothingPermille,
        int[] externalSellPriceBase,
        int[][] externalSellPriceSeasonPermille,
        int[] externalBuyPrice,
        int inputBufferDays,
        int shipmentDays,
        int toolLifeLaborDays,
        int equipmentPermilleWithoutTools,
        int disposableHours,
        int trustDiscountPermille)
    {
        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(initialAcquisitionCost);
        ArgumentNullException.ThrowIfNull(initialHouseholdInventory);
        ArgumentNullException.ThrowIfNull(initialSkillPermilleByRank);
        ArgumentNullException.ThrowIfNull(laborPermilleByRank);
        ArgumentNullException.ThrowIfNull(dailyConsumptionPerNpcByRank);
        ArgumentNullException.ThrowIfNull(firewoodConsumptionSeasonPermille);
        ArgumentNullException.ThrowIfNull(necessityTargetStockDays);
        ArgumentNullException.ThrowIfNull(preferenceTargetStockDays);
        ArgumentNullException.ThrowIfNull(rankCoefficientPermille);
        ArgumentNullException.ThrowIfNull(opportunityCostBaseByOccupation);
        ArgumentNullException.ThrowIfNull(externalSellPriceBase);
        ArgumentNullException.ThrowIfNull(externalSellPriceSeasonPermille);
        ArgumentNullException.ThrowIfNull(externalBuyPrice);

        if (recipes.Length == 0)
        {
            throw new ArgumentException("レシピは1件以上(GDD02 §2.4)。", nameof(recipes));
        }

        // どのレシピも出力しない品目か(= 1次産品か)。外部価格表の検証がこれを読む
        // (タスク仕様「1次産品はレシピから決まる。価格表の側で判定させない」)。
        var producedByAnyRecipe = new bool[itemCount];

        for (int occupationId = 0; occupationId < recipes.Length; occupationId++)
        {
            var recipe = recipes[occupationId];

            ArgumentNullException.ThrowIfNull(recipe, nameof(recipes));

            if ((int)recipe.Occupation != occupationId)
            {
                throw new ArgumentException(
                    $"Recipes[{occupationId}].Occupation が添字と一致しない"
                        + $"(実際: {recipe.Occupation})。",
                    nameof(recipes));
            }

            ValidateItemIdsAreInRange(recipe.Outputs, itemCount, nameof(recipes));
            ValidateItemIdsAreInRange(recipe.Inputs, itemCount, nameof(recipes));

            foreach (var output in recipe.Outputs)
            {
                producedByAnyRecipe[output.ItemId] = true;
            }
        }

        if (initialAcquisitionCost.Length != itemCount)
        {
            throw new ArgumentException(
                $"取得原価表の長さは品目数({itemCount})と一致する必要がある。",
                nameof(initialAcquisitionCost));
        }

        foreach (int cost in initialAcquisitionCost)
        {
            // 0を許すと初日の原価が0になり、GDD02 §8.1.1 の「0が恒久に固定される」経路を踏む。
            if (cost < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialAcquisitionCost), cost, "取得原価は1以上(GDD02 §8.1)。");
            }
        }

        if (initialHouseholdInventory.Length != itemCount)
        {
            throw new ArgumentException(
                $"初期世帯在庫の長さは品目数({itemCount})と一致する必要がある。",
                nameof(initialHouseholdInventory));
        }

        if (initialSkillPermilleByRank.Length != 3)
        {
            throw new ArgumentException(
                "初期熟練度は NpcRank の3階層ぶん(長さ3)必要。", nameof(initialSkillPermilleByRank));
        }

        foreach (int skillPermille in initialSkillPermilleByRank)
        {
            if (skillPermille is < 0 or > 1000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialSkillPermilleByRank), skillPermille, "熟練度‰は0〜1000。");
            }
        }

        // 長さ3は NpcRank の階層数(InitialSkillPermilleByRank と同じ根拠、GDD02 §5.2)。
        if (laborPermilleByRank.Length != 3)
        {
            throw new ArgumentException(
                "労働力係数‰は NpcRank の3階層ぶん(長さ3)必要。", nameof(laborPermilleByRank));
        }

        foreach (int laborPermille in laborPermilleByRank)
        {
            if (laborPermille < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(laborPermilleByRank), laborPermille, "労働力係数‰は非負。");
            }
        }

        // 長さ3はNpcRankの階層数。行ごとに itemCount 一致を検査する(GDD02 §6.1)。
        if (dailyConsumptionPerNpcByRank.Length != 3)
        {
            throw new ArgumentException(
                "1人1日あたりの消費量は NpcRank の3階層ぶん(長さ3)必要。",
                nameof(dailyConsumptionPerNpcByRank));
        }

        foreach (var row in dailyConsumptionPerNpcByRank)
        {
            ArgumentNullException.ThrowIfNull(row, nameof(dailyConsumptionPerNpcByRank));

            if (row.Length != itemCount)
            {
                throw new ArgumentException(
                    $"1人1日あたりの消費量の各行の長さは品目数({itemCount})と一致する必要がある。",
                    nameof(dailyConsumptionPerNpcByRank));
            }

            foreach (int quantity in row)
            {
                if (quantity < 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(dailyConsumptionPerNpcByRank), quantity, "1人1日あたりの消費量は非負。");
                }
            }
        }

        // 長さ4はSeasonの季節数(GDD03 §2.1)。
        if (firewoodConsumptionSeasonPermille.Length != 4)
        {
            throw new ArgumentException(
                "薪の季節係数‰は Season の4季節ぶん(長さ4)必要。",
                nameof(firewoodConsumptionSeasonPermille));
        }

        foreach (int seasonPermille in firewoodConsumptionSeasonPermille)
        {
            if (seasonPermille < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(firewoodConsumptionSeasonPermille), seasonPermille, "季節係数‰は非負。");
            }
        }

        // 負だけを拒む。0(利幅なし = 許容原価合計が見込み収益そのまま)は構造として成立する。
        if (minimumMarginPermille < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumMarginPermille), minimumMarginPermille, "最低利幅‰は非負(GDD02c §2.3)。");
        }

        // 0だと有効な観測が永久に0件になる(下記の境界規則が「差1日以上」を要求するため)。
        if (observationRetentionDays < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observationRetentionDays), observationRetentionDays,
                "相場観測の保持期間は1日以上(GDD06 §3.1)。");
        }

        if (necessityTargetStockDays.Length != itemCount)
        {
            throw new ArgumentException(
                $"必需の目標在庫日数の長さは品目数({itemCount})と一致する必要がある。",
                nameof(necessityTargetStockDays));
        }

        if (preferenceTargetStockDays.Length != itemCount)
        {
            throw new ArgumentException(
                $"嗜好の目標在庫日数の長さは品目数({itemCount})と一致する必要がある。",
                nameof(preferenceTargetStockDays));
        }

        for (int itemId = 0; itemId < itemCount; itemId++)
        {
            int necessityDays = necessityTargetStockDays[itemId];
            int preferenceDays = preferenceTargetStockDays[itemId];

            if (necessityDays < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(necessityTargetStockDays), necessityDays, "必需の目標在庫日数は非負(GDD02 §8.2.1)。");
            }

            if (preferenceDays < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(preferenceTargetStockDays), preferenceDays, "嗜好の目標在庫日数は非負(GDD02 §8.2.1)。");
            }

            // GDD02 §8.2.1の表が用途を排他に定めている。両方に置くと同じ世帯在庫に対して
            // 2本の行が立ち、先に走査したほうが買った量を後の行が見ない(予想在庫は#37が
            // 約定するまで動かない)ので、目標在庫の2倍まで買う。
            if (necessityDays > 0 && preferenceDays > 0)
            {
                throw new ArgumentException(
                    $"品目(itemId={itemId})を必需と嗜好の両方には置けない"
                        + "(GDD02 §8.2.1、用途は排他)。",
                    nameof(necessityTargetStockDays));
            }

            // Item.Toolsを必需・嗜好に置けないのは、耐久だけ単位が違うからである(耐久値 vs 個)。
            // 同じ品目に単位の違う2本の行が立つ。
            if (itemId == Item.Tools && (necessityDays > 0 || preferenceDays > 0))
            {
                throw new ArgumentException(
                    "工具(Item.Tools)は必需・嗜好に置けない(耐久は単位が異なる。GDD02 §8.2.1)。",
                    nameof(necessityTargetStockDays));
            }
        }

        // 1未満を拒むのは、目標在庫が0だとその品目が「耐久ではない」扱いに見えてしまうからである
        // (工具の行は全世帯に常に立つ。GDD02 §8.2.1)。
        if (toolTargetStockPermille < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toolTargetStockPermille), toolTargetStockPermille, "工具の目標在庫‰は1以上(GDD02 §8.2.1)。");
        }

        // 長さ3はNpcRankの階層数(InitialSkillPermilleByRankと同じ根拠、GDD08 §9)。
        if (rankCoefficientPermille.Length != 3)
        {
            throw new ArgumentException(
                "階層係数‰は NpcRank の3階層ぶん(長さ3)必要(GDD08 §9)。", nameof(rankCoefficientPermille));
        }

        foreach (int coefficient in rankCoefficientPermille)
        {
            // 0だと、その階層が世帯主の世帯の工具の目標在庫が0になる(GDD02 §8.2.1)。
            if (coefficient < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rankCoefficientPermille), coefficient, "階層係数‰は1以上(GDD02 §8.2.1)。");
            }
        }

        // 詰みは作らない(GDD02 §4.2)。許容乖離0だと相場項が常に0になり、パンも薪も
        // 永久に買えなくなる。
        if (tolerancePermille < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tolerancePermille), tolerancePermille,
                "許容乖離‰は1以上(GDD02c §2.1)。");
        }

        if (householdsPerOccupation < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(householdsPerOccupation), householdsPerOccupation, "1職業あたりの世帯数は1以上。");
        }

        // 詰みは作らない(GDD02 §4.2)。0を許すと設備係数が全世帯0‰になり、鍛冶自身も
        // 工具を作れず回復経路が無い。
        if (initialToolStock < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialToolStock), initialToolStock, "初期工具在庫は1以上(GDD02 §5.3)。");
        }

        int householdCount = recipes.Length * householdsPerOccupation;

        // WorldGenerator.AssignDistricts の配置の前提(空区画を作らず、1区画あたり最大2世帯)。
        if (householdCount < District.Count || householdCount > District.Count * 2)
        {
            throw new ArgumentException(
                $"世帯数({householdCount})は区画数({District.Count})以上"
                    + $"区画数の2倍({District.Count * 2})以下(GDD02 §4.3の配置密度)。",
                nameof(householdsPerOccupation));
        }

        // 長さ = 職業数(Recipes.Length)。全要素1以上 ── 0を許すと移動費が全区画で0になり、
        // GDD08 §7.4「信用インフレを止める唯一の絞り」が恒偽になる(GDD02 §8.1.1)。
        if (opportunityCostBaseByOccupation.Length != recipes.Length)
        {
            throw new ArgumentException(
                $"機会費用の基準値の長さは職業数({recipes.Length})と一致する必要がある。",
                nameof(opportunityCostBaseByOccupation));
        }

        foreach (int baseValue in opportunityCostBaseByOccupation)
        {
            if (baseValue < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(opportunityCostBaseByOccupation), baseValue, "機会費用の基準値は1以上(GDD02 §8.1.1)。");
            }
        }

        // 0はGDD06 §3.1の囲みが挙げるR=0と同じ構造の破壊 ── 実質コストの第2項(移動費)が
        // 全区画で消える。
        if (travelHoursPerDistrict < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(travelHoursPerDistrict), travelHoursPerDistrict, "1区画あたりの移動時間は1以上(GDD02 §4.3)。");
        }

        // 0は移動平均が初期値に凍る。1000超は「旧移動平均×(1000−β)」が負になり、
        // 高値で仕入れるほど原価が下がる(GDD02 §8.1.1)。
        if (acquisitionCostSmoothingPermille is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(acquisitionCostSmoothingPermille), acquisitionCostSmoothingPermille,
                "仕入れ移動平均単価の平滑化係数‰は1〜1000(GDD02 §8.1.1)。");
        }

        // 外部売値の基準値・外部買値の長さと符号(GDD02d §2.1・§4.4)。
        if (externalSellPriceBase.Length != itemCount)
        {
            throw new ArgumentException(
                $"外部売値の基準値の長さは品目数({itemCount})と一致する必要がある。",
                nameof(externalSellPriceBase));
        }

        if (externalBuyPrice.Length != itemCount)
        {
            throw new ArgumentException(
                $"外部買値の長さは品目数({itemCount})と一致する必要がある。",
                nameof(externalBuyPrice));
        }

        // 「1次産品」はレシピから決まる(どのレシピの出力にも現れない品目)。価格表の側で
        // それを表現させると、レシピと価格表が食い違ったまま両方が通ってしまう
        // (タスク仕様)。検査は品目ごとに「ちょうど一方だけが1以上」を見る。
        for (int itemId = 0; itemId < itemCount; itemId++)
        {
            int sellBase = externalSellPriceBase[itemId];
            int buyPrice = externalBuyPrice[itemId];
            bool isPrimary = !producedByAnyRecipe[itemId];

            if (sellBase < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(externalSellPriceBase), sellBase, "外部売値の基準値は非負(GDD02d §4.4)。");
            }

            if (buyPrice < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(externalBuyPrice), buyPrice, "外部買値は非負(GDD02d §4.4)。");
            }

            if (isPrimary)
            {
                if (sellBase < 1)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(externalSellPriceBase), sellBase,
                        $"1次産品(itemId={itemId})の外部売値の基準値は1以上(GDD02d §2.1)。");
                }

                if (buyPrice != 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(externalBuyPrice), buyPrice,
                        $"1次産品(itemId={itemId})は外部買値を持たない(0。GDD02d §2.1)。");
                }
            }
            else
            {
                if (buyPrice < 1)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(externalBuyPrice), buyPrice,
                        $"都市生産品(itemId={itemId})の外部買値は1以上(GDD02d §2.1)。");
                }

                if (sellBase != 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(externalSellPriceBase), sellBase,
                        $"都市生産品(itemId={itemId})は外部売値を持たない(0。GDD02d §2.1)。");
                }
            }
        }

        // 外部売値の季節係数‰(GDD02d §5 / GDD03 §2.2)。行数=itemCount、各行長さ4(Season)。
        if (externalSellPriceSeasonPermille.Length != itemCount)
        {
            throw new ArgumentException(
                $"外部売値の季節係数‰の行数は品目数({itemCount})と一致する必要がある。",
                nameof(externalSellPriceSeasonPermille));
        }

        for (int itemId = 0; itemId < itemCount; itemId++)
        {
            var row = externalSellPriceSeasonPermille[itemId];

            ArgumentNullException.ThrowIfNull(row, nameof(externalSellPriceSeasonPermille));

            if (row.Length != 4)
            {
                throw new ArgumentException(
                    "外部売値の季節係数‰の各行の長さは Season の4季節ぶん(長さ4)必要"
                        + $"(itemId={itemId})。",
                    nameof(externalSellPriceSeasonPermille));
            }

            long seasonSum = 0;

            foreach (int seasonPermille in row)
            {
                if (seasonPermille <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(externalSellPriceSeasonPermille), seasonPermille,
                        $"外部売値の季節係数‰は1以上(itemId={itemId}。GDD02d §5)。");
                }

                seasonSum += seasonPermille;
            }

            // 年平均1000‰(合計4000)を機械で守るのは、校正(c)が年平均で成立しているからである
            // (GDD02d §5)。1000‰からずれると貿易収支が構造的に黒字か赤字になり、
            // それは踏んでも気付けない。
            if (seasonSum != 4000)
            {
                throw new ArgumentException(
                    $"外部売値の季節係数‰の行の合計は4000(年平均1000‰)である必要がある"
                        + $"(itemId={itemId}。実際: {seasonSum}。GDD02d §5)。",
                    nameof(externalSellPriceSeasonPermille));
            }

            if (producedByAnyRecipe[itemId])
            {
                // 都市生産品の行はすべて1000(使わないが、黙って効く値を置かせない)。
                foreach (int seasonPermille in row)
                {
                    if (seasonPermille != 1000)
                    {
                        throw new ArgumentException(
                            $"都市生産品(itemId={itemId})の外部売値の季節係数‰の行はすべて1000"
                                + "である必要がある(使わない値を置かせない。GDD02d §5)。",
                            nameof(externalSellPriceSeasonPermille));
                    }
                }
            }
        }

        if (inputBufferDays < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inputBufferDays), inputBufferDays, "入力の緩衝日数Dは1以上(GDD02a §4)。");
        }

        if (shipmentDays < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(shipmentDays), shipmentDays, "出荷日数は1以上(GDD02c §1.3)。");
        }

        // 0を拒むのは、FloorDiv(摩耗, N×1000)がゼロ除算になるからである(GDD02a §3.1)。
        if (toolLifeLaborDays < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toolLifeLaborDays), toolLifeLaborDays, "工具1個が尽きる労働量Nは1以上(GDD02a §3.1)。");
        }

        if (equipmentPermilleWithoutTools is < 0 or > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(equipmentPermilleWithoutTools), equipmentPermilleWithoutTools,
                "工具が無いときの設備係数‰は0〜1000(GDD02a §3)。");
        }

        if (disposableHours < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(disposableHours), disposableHours, "可処分時間Tは1以上(GDD08 §3.1)。");
        }

        // 0〜999の外を拒む(1000を含めない)。1000ちょうどだと信用100で係数が0になり、
        // 実効価格が0になって下流(BuyerBudget.Decide / TradeSettlement.FundsCap)が
        // 投げる(GDD06 §2)。
        if (trustDiscountPermille is < 0 or > 999)
        {
            throw new ArgumentOutOfRangeException(
                nameof(trustDiscountPermille), trustDiscountPermille,
                "信用による実効価格の割引係数‰は0〜999(GDD06 §2)。");
        }

        ItemCount = itemCount;
        HouseholdsPerOccupation = householdsPerOccupation;
        Recipes = recipes.ToArray();
        InitialLiquidFunds = initialLiquidFunds;
        InitialAcquisitionCost = initialAcquisitionCost.ToArray();
        InitialHouseholdInventory = initialHouseholdInventory.ToArray();
        InitialWorkshopInputDays = initialWorkshopInputDays;
        InitialToolStock = initialToolStock;
        InitialSkillPermilleByRank = initialSkillPermilleByRank.ToArray();
        LaborPermilleByRank = laborPermilleByRank.ToArray();

        // jagged配列は行ごとに複製する。外側だけToArray()すると、呼び出し側が検証後に
        // 行の中身を書き換えられてしまう(既存欄の防御的コピーと同じ理由)。
        DailyConsumptionPerNpcByRank = new int[dailyConsumptionPerNpcByRank.Length][];
        for (int rank = 0; rank < dailyConsumptionPerNpcByRank.Length; rank++)
        {
            DailyConsumptionPerNpcByRank[rank] = dailyConsumptionPerNpcByRank[rank].ToArray();
        }

        FirewoodConsumptionSeasonPermille = firewoodConsumptionSeasonPermille.ToArray();

        MinimumMarginPermille = minimumMarginPermille;
        ObservationRetentionDays = observationRetentionDays;

        NecessityTargetStockDays = necessityTargetStockDays.ToArray();
        PreferenceTargetStockDays = preferenceTargetStockDays.ToArray();
        ToolTargetStockPermille = toolTargetStockPermille;
        RankCoefficientPermille = rankCoefficientPermille.ToArray();
        TolerancePermille = tolerancePermille;
        OpportunityCostBaseByOccupation = opportunityCostBaseByOccupation.ToArray();
        TravelHoursPerDistrict = travelHoursPerDistrict;
        AcquisitionCostSmoothingPermille = acquisitionCostSmoothingPermille;

        _externalSellPriceBase = externalSellPriceBase.ToArray();
        _externalBuyPrice = externalBuyPrice.ToArray();

        // jagged配列は行ごとに複製する(DailyConsumptionPerNpcByRankと同じ理由)。
        _externalSellPriceSeasonPermille = new int[externalSellPriceSeasonPermille.Length][];
        for (int itemId = 0; itemId < externalSellPriceSeasonPermille.Length; itemId++)
        {
            _externalSellPriceSeasonPermille[itemId] = externalSellPriceSeasonPermille[itemId].ToArray();
        }

        _isPrimaryItemByItemId = new bool[itemCount];
        for (int itemId = 0; itemId < itemCount; itemId++)
        {
            _isPrimaryItemByItemId[itemId] = !producedByAnyRecipe[itemId];
        }

        InputBufferDays = inputBufferDays;
        ShipmentDays = shipmentDays;
        ToolLifeLaborDays = toolLifeLaborDays;
        EquipmentPermilleWithoutTools = equipmentPermilleWithoutTools;
        DisposableHours = disposableHours;
        TrustDiscountPermille = trustDiscountPermille;

        // 導出値。状態ではなく、すべてここまでの引数から決まる(タスク仕様)。
        NominalLaborPermille = laborPermilleByRank[(int)NpcRank.Master] + laborPermilleByRank[(int)NpcRank.Apprentice];
        ToolDurabilityPerUnit = checked(toolLifeLaborDays * IntegerMath.PermilleScale);
    }

    /// <summary>どのレシピも出力しない品目か(= 都市外市場から来る1次産品か)。GDD02 §2.2。</summary>
    public bool IsPrimaryItem(int itemId)
    {
        ValidateItemIdInRange(itemId);

        return _isPrimaryItemByItemId[itemId];
    }

    /// <summary>
    /// 外部買値。都市生産品だけが持つ(GDD02d §2.1・§3)。季節係数は掛からない。
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="itemId"/> が1次産品のとき。</exception>
    public int ExternalBuyPrice(int itemId)
    {
        ValidateItemIdInRange(itemId);

        if (_isPrimaryItemByItemId[itemId])
        {
            throw new ArgumentException(
                $"品目Id={itemId}は1次産品であり、外部買値を持たない"
                    + "(都市生産品だけが持つ。GDD02d §2.1)。",
                nameof(itemId));
        }

        return _externalBuyPrice[itemId];
    }

    /// <summary>
    /// 当日の外部売値 = ApplyPermille(基準値, 季節係数‰[季節])(GDD02d §2.1)。1次産品だけが持つ。
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="itemId"/> が都市生産品のとき。</exception>
    public int ExternalSellPrice(int itemId, Season season)
    {
        ValidateItemIdInRange(itemId);

        if (!_isPrimaryItemByItemId[itemId])
        {
            throw new ArgumentException(
                $"品目Id={itemId}は都市生産品であり、外部売値を持たない"
                    + "(1次産品だけが持つ。GDD02d §2.1)。",
                nameof(itemId));
        }

        return IntegerMath.ApplyPermille(
            _externalSellPriceBase[itemId], _externalSellPriceSeasonPermille[itemId][(int)season]);
    }

    /// <summary>
    /// 生産能力(実行回数/日)。労働力は <see cref="NominalLaborPermille"/>、設備係数は1000‰で評価する
    /// (GDD02a §4 / GDD02c §1.3)。式は <see cref="Recipe.CapacityRuns"/>。
    /// </summary>
    public int ProductionCapacity(Occupation occupation)
    {
        var recipe = Recipes[(int)occupation];

        return recipe.CapacityRuns(NominalLaborPermille, IntegerMath.PermilleScale);
    }

    /// <summary>1日の投入量 = 生産能力 × 必要数量。入力でない品目は0(GDD02a §4)。</summary>
    public int DailyInputQuantity(Occupation occupation, int itemId)
    {
        var recipe = Recipes[(int)occupation];

        foreach (var input in recipe.Inputs)
        {
            if (input.ItemId == itemId)
            {
                return checked(ProductionCapacity(occupation) * input.Quantity);
            }
        }

        return 0;
    }

    /// <summary>入力の目標在庫 = 1日の投入量 × D。入力でない品目は0(GDD02a §4)。</summary>
    public int InputTargetStock(Occupation occupation, int itemId) =>
        checked(DailyInputQuantity(occupation, itemId) * InputBufferDays);

    /// <summary>出荷目標在庫 = 生産能力 × 出力数量 × 出荷日数。出力でない品目は0(GDD02c §1.3)。</summary>
    public int ShipmentTargetStock(Occupation occupation, int itemId)
    {
        var recipe = Recipes[(int)occupation];

        foreach (var output in recipe.Outputs)
        {
            if (output.ItemId == itemId)
            {
                return checked(ProductionCapacity(occupation) * output.Quantity * ShipmentDays);
            }
        }

        return 0;
    }

    private void ValidateItemIdInRange(int itemId)
    {
        if (itemId < 0 || itemId >= ItemCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(itemId), itemId, $"品目Idは0〜{ItemCount - 1}(GDD02 §2.2)。");
        }
    }

    private static void ValidateItemIdsAreInRange(ItemQuantity[] items, int itemCount, string paramName)
    {
        foreach (var item in items)
        {
            if (item.ItemId < 0 || item.ItemId >= itemCount)
            {
                throw new ArgumentOutOfRangeException(
                    paramName, item.ItemId, $"品目Idは0〜{itemCount - 1}(GDD02 §2.2)。");
            }
        }
    }

    /// <summary>
    /// M0 の初期値(GDD02d §4.4 の校正表そのもの)。すべて初期値であり調整対象(CLAUDE.md)。
    /// 校正 (a)〜(e) の検査は <c>M0CalibrationTests</c>(#96)。
    /// </summary>
    private static WorldDefinition BuildM0()
    {
        // レシピ(GDD02d §4.4)。所要労働‰ で1日の実行回数を作る
        // (1300‰ ÷ 所要労働‰ = 7 / 6 / 6 / 12 / 1)。
        var recipes = new[]
        {
            new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
                inputs: new[] { new ItemQuantity { ItemId = Item.Grain, Quantity = 2 } },
                laborPermille: 185), // ‰。1300‰ ÷ 185‰ = 7実行/日
            new Recipe(
                Occupation.Baker,
                outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = 2 } },
                inputs: new[]
                {
                    new ItemQuantity { ItemId = Item.Flour, Quantity = 1 },
                    new ItemQuantity { ItemId = Item.Firewood, Quantity = 1 },
                },
                laborPermille: 216), // ‰。1300‰ ÷ 216‰ = 6実行/日
            new Recipe(
                Occupation.Brewer,
                outputs: new[] { new ItemQuantity { ItemId = Item.Beer, Quantity = 1 } },
                inputs: new[]
                {
                    new ItemQuantity { ItemId = Item.Grain, Quantity = 2 },
                    new ItemQuantity { ItemId = Item.Firewood, Quantity = 1 },
                },
                laborPermille: 216), // ‰。1300‰ ÷ 216‰ = 6実行/日
            new Recipe(
                Occupation.Woodworker,
                outputs: new[] { new ItemQuantity { ItemId = Item.Firewood, Quantity = 3 } },
                inputs: new[] { new ItemQuantity { ItemId = Item.Timber, Quantity = 1 } },
                laborPermille: 108), // ‰。1300‰ ÷ 108‰ = 12実行/日
            new Recipe(
                Occupation.Smith,
                outputs: new[] { new ItemQuantity { ItemId = Item.Tools, Quantity = 1 } },
                inputs: new[]
                {
                    new ItemQuantity { ItemId = Item.IronOre, Quantity = 2 },
                    new ItemQuantity { ItemId = Item.Charcoal, Quantity = 1 },
                },
                laborPermille: 1000), // ‰。1300‰ ÷ 1000‰ = 1実行/日
        };

        // 添字 = itemId(Grain, Timber, IronOre, Charcoal, Flour, Firewood, Bread, Beer, Tools)。
        // 1次産品(0〜3)だけが外部売値の基準値を持つ(GDD02d §2.1・§4.4)。単位: 貨幣/1単位(年平均)。
        var externalSellPriceBase = new[] { 10, 9, 14, 12, 0, 0, 0, 0, 0 };

        // 都市生産品(4〜8)だけが外部買値を持つ(GDD02d §2.1・§4.4)。単位: 貨幣/1単位。
        var externalBuyPrice = new[] { 0, 0, 0, 0, 56, 10, 54, 72, 290 };

        // 添字 = [itemId][(int)Season](Spring, Summer, Autumn, Winter)。GDD03 §2.2。年平均1000‰。
        // 都市生産品の行はすべて1000(使わないが、黙って効く値を置かせない。コンストラクタが強制する)。
        var externalSellPriceSeasonPermille = new[]
        {
            new[] { 1000, 1300, 700, 1000 }, // Grain。‰(収穫直後が最安、次の収穫前が最高)
            new[] { 1000, 1000, 1000, 1000 }, // Timber。‰(季節性なし)
            new[] { 1000, 1000, 1000, 1000 }, // IronOre。‰(季節性なし)
            new[] { 1000, 1250, 1000, 750 }, // Charcoal。‰(炭焼きは農閑期の仕事)
            new[] { 1000, 1000, 1000, 1000 }, // Flour。‰(都市生産品)
            new[] { 1000, 1000, 1000, 1000 }, // Firewood。‰(都市生産品)
            new[] { 1000, 1000, 1000, 1000 }, // Bread。‰(都市生産品)
            new[] { 1000, 1000, 1000, 1000 }, // Beer。‰(都市生産品)
            new[] { 1000, 1000, 1000, 1000 }, // Tools。‰(都市生産品)
        };

        // 初期在庫の取得原価(仕入れ移動平均の初期値)は床価格に揃える ──
        // 1次産品は外部売値の基準値、都市生産品は外部買値(タスク仕様)。
        var initialAcquisitionCost = new[] { 10, 9, 14, 12, 56, 10, 54, 72, 290 }; // 単位: 貨幣/1単位

        // 世帯在庫は春の目標在庫(薪7日×4/日、パン3日×2/日、ビール1日×1/日。GDD02b §2)。
        var initialHouseholdInventory = new[] { 0, 0, 0, 0, 0, 28, 6, 1, 0 }; // 単位: 個

        // 熟練度‰を階層で違う値にするのは、ハッシュの回帰を鈍らせないためである
        // (全員が同じ値だと、ハッシュからSkillPermilleを落としても値が変わらない)。
        var initialSkillPermilleByRank = new[] { 700, 400, 100 }; // 単位: ‰

        // 添字 = (int)NpcRank(Master, Journeyman, Apprentice)。GDD02a §2。
        var laborPermilleByRank = new[] { 1000, 800, 300 }; // 単位: ‰

        // 1人1日あたりの消費量。添字 = [(int)NpcRank][itemId]。GDD02 §6.1。
        // itemIdの並びはGrain, Timber, IronOre, Charcoal, Flour, Firewood, Bread, Beer, Tools。
        var dailyConsumptionPerNpcByRank = new[]
        {
            new[] { 0, 0, 0, 0, 0, 2, 1, 1, 0 }, // 親方。単位: 個
            new[] { 0, 0, 0, 0, 0, 2, 1, 1, 0 }, // 職人。単位: 個
            new[] { 0, 0, 0, 0, 0, 2, 1, 0, 0 }, // 徒弟。単位: 個
        };

        // 添字 = (int)Season(Spring, Summer, Autumn, Winter)。GDD03 §2.1。基準は秋の1000‰。
        var firewoodConsumptionSeasonPermille = new[] { 800, 400, 1000, 2000 }; // 単位: ‰

        const int MinimumMarginPermilleForM0 = 200; // ‰。20%
        const int ObservationRetentionDaysForM0 = 7; // 日(GDD06 §3.1)

        // 必需・嗜好の目標在庫の日数。添字 = itemId。単位: 日(GDD02 §8.2.1 の表そのもの)。
        var necessityTargetStockDays = new int[Item.Count];
        necessityTargetStockDays[Item.Firewood] = 7; // 薪: 1週間分(1日消費量は季節変動、GDD02 §9)
        necessityTargetStockDays[Item.Bread] = 3;    // パン: 3日分

        var preferenceTargetStockDays = new int[Item.Count];
        preferenceTargetStockDays[Item.Beer] = 1;    // ビール: 1日分

        const int ToolTargetStockPermilleForM0 = 500; // ‰。1個あたりの耐久値に対する比率

        // 階層係数‰。添字 = (int)NpcRank(Master, Journeyman, Apprentice)。GDD08 §9 の表。
        var rankCoefficientPermille = new[] { 1000, 600, 200 }; // 単位: ‰

        const int TolerancePermilleForM0 = 1200; // ‰。相場の1.2倍までは追随する(全用途共通、GDD02c §2.1)

        // 添字 = (int)Occupation(Miller, Baker, Brewer, Woodworker, Smith)。単位: 貨幣/1時間(GDD02c §2.4)。
        var opportunityCostBaseByOccupation = new[] { 20, 20, 20, 20, 20 };

        const int TravelHoursPerDistrictForM0 = 1;             // 時間/区画(GDD02 §4.3)
        const int AcquisitionCostSmoothingPermilleForM0 = 250; // ‰。実効的な窓は7件程度(2/β − 1)

        return new WorldDefinition(
            itemCount: Item.Count,
            householdsPerOccupation: 2,
            recipes: recipes,
            initialLiquidFunds: 2400, // 単位: 貨幣(GDD02d §4.4 (d))
            initialAcquisitionCost: initialAcquisitionCost,
            initialHouseholdInventory: initialHouseholdInventory,
            initialWorkshopInputDays: 3, // 単位: 日(× 1日の投入量。Dと同じ3日ぶん)
            initialToolStock: 1, // 単位: 個
            initialSkillPermilleByRank: initialSkillPermilleByRank,
            laborPermilleByRank: laborPermilleByRank,
            dailyConsumptionPerNpcByRank: dailyConsumptionPerNpcByRank,
            firewoodConsumptionSeasonPermille: firewoodConsumptionSeasonPermille,
            minimumMarginPermille: MinimumMarginPermilleForM0,
            observationRetentionDays: ObservationRetentionDaysForM0,
            necessityTargetStockDays: necessityTargetStockDays,
            preferenceTargetStockDays: preferenceTargetStockDays,
            toolTargetStockPermille: ToolTargetStockPermilleForM0,
            rankCoefficientPermille: rankCoefficientPermille,
            tolerancePermille: TolerancePermilleForM0,
            opportunityCostBaseByOccupation: opportunityCostBaseByOccupation,
            travelHoursPerDistrict: TravelHoursPerDistrictForM0,
            acquisitionCostSmoothingPermille: AcquisitionCostSmoothingPermilleForM0,
            externalSellPriceBase: externalSellPriceBase,
            externalSellPriceSeasonPermille: externalSellPriceSeasonPermille,
            externalBuyPrice: externalBuyPrice,
            inputBufferDays: 3,                  // 単位: 日(GDD02a §4)
            shipmentDays: 3,                      // 単位: 日(GDD02c §1.3)
            toolLifeLaborDays: 13,                // 単位: 人日(GDD02a §3.1)
            equipmentPermilleWithoutTools: 500,   // 単位: ‰(GDD02a §3)
            disposableHours: 12,                  // 単位: 時間(GDD08 §3.1)
            trustDiscountPermille: 200); // ‰(GDD01 §2.2 効果1)。信用100で2割引の校正値
    }
}
