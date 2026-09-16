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

    /// <summary>工房在庫[入力j] = これ × 必要数量j として初期化する日数。単位: 日。</summary>
    public int InitialWorkshopInputDays { get; }

    /// <summary>工房在庫[Item.Tools] の初期値。単位: 個。1以上(下記コンストラクタ参照)。</summary>
    public int InitialToolStock { get; }

    /// <summary>添字 = (int)NpcRank の初期熟練度。長さ3。単位: ‰。</summary>
    public int[] InitialSkillPermilleByRank { get; }

    /// <summary>添字 = (int)NpcRank の労働力係数‰。長さ3(GDD02 §5.2)。</summary>
    public int[] LaborPermilleByRank { get; }

    /// <summary>工具1個を消費するまでのレシピ実行回数 N。1以上(GDD02 §5.3)。</summary>
    public int ProductionRunsPerToolWear { get; }

    /// <summary>1人1日あたりの消費量。添字 = [(int)NpcRank][itemId]。長さ3 × itemCount(GDD02 §6.1)。</summary>
    public int[][] DailyConsumptionPerNpcByRank { get; }

    /// <summary>添字 = (int)Season の薪の消費の季節係数‰。長さ4(GDD02 §9 / GDD03 §2.1)。</summary>
    public int[] FirewoodConsumptionSeasonPermille { get; }

    /// <summary>最低利幅‰。原価下限 = ApplyPermille(原価, 1000 + これ)(GDD02 §8.1)。</summary>
    public int MinimumMarginPermille { get; }

    /// <summary>
    /// 出荷目標在庫。添字 = [(int)Occupation][itemId]。単位: 個(GDD02 §8.1.1)。
    /// </summary>
    public int[][] ShipmentTargetStockByOccupation { get; }

    /// <summary>相場観測の保持期間。単位: 日(GDD06 §3.1)。</summary>
    public int ObservationRetentionDays { get; }

    /// <summary>
    /// 生産の入力の目標在庫。添字 = [(int)Occupation][itemId]。単位: 個(GDD02 §8.2.1)。
    /// </summary>
    public int[][] InputTargetStockByOccupation { get; }

    /// <summary>必需の目標在庫の日数。添字 = itemId。単位: 日(GDD02 §8.2.1)。0 = 必需ではない。</summary>
    public int[] NecessityTargetStockDays { get; }

    /// <summary>嗜好・奢侈の目標在庫の日数。添字 = itemId。単位: 日(同)。0 = 嗜好ではない。</summary>
    public int[] PreferenceTargetStockDays { get; }

    /// <summary>工具の目標在庫‰。1個あたりの耐久値に対する比率(GDD02 §8.2.1)。単位: ‰。</summary>
    public int ToolTargetStockPermille { get; }

    /// <summary>階層係数‰。添字 = (int)NpcRank。長さ3(GDD08 §9)。単位: ‰。</summary>
    public int[] RankCoefficientPermille { get; }

    /// <summary>必需の許容乖離‰。基礎値 = ApplyPermille(相場基準, これ)(GDD02 §8.2.1)。単位: ‰。</summary>
    public int NecessityTolerancePermille { get; }

    /// <summary>
    /// 用途別の予算比率‰。添字 = (int)DemandPurpose。長さ4(GDD02 §8.2.1・§8.2.7)。単位: ‰。
    /// </summary>
    public int[] BudgetRatioPermilleByPurpose { get; }

    /// <summary>職業数。<see cref="Recipes"/> の長さから導く。</summary>
    public int OccupationCount => Recipes.Length;

    /// <summary>世帯数。職業数 × 1職業あたりの世帯数(GDD02 §2.4)。</summary>
    public int HouseholdCount => OccupationCount * HouseholdsPerOccupation;

    /// <summary>NPC数。世帯あたり親方1・徒弟1(GDD02 §2.4)。</summary>
    public int NpcCount => HouseholdCount * 2;

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
        int productionRunsPerToolWear,
        int[][] dailyConsumptionPerNpcByRank,
        int[] firewoodConsumptionSeasonPermille,
        int minimumMarginPermille,
        int[][] shipmentTargetStockByOccupation,
        int observationRetentionDays,
        int[][] inputTargetStockByOccupation,
        int[] necessityTargetStockDays,
        int[] preferenceTargetStockDays,
        int toolTargetStockPermille,
        int[] rankCoefficientPermille,
        int necessityTolerancePermille,
        int[] budgetRatioPermilleByPurpose)
    {
        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(initialAcquisitionCost);
        ArgumentNullException.ThrowIfNull(initialHouseholdInventory);
        ArgumentNullException.ThrowIfNull(initialSkillPermilleByRank);
        ArgumentNullException.ThrowIfNull(laborPermilleByRank);
        ArgumentNullException.ThrowIfNull(dailyConsumptionPerNpcByRank);
        ArgumentNullException.ThrowIfNull(firewoodConsumptionSeasonPermille);
        ArgumentNullException.ThrowIfNull(shipmentTargetStockByOccupation);
        ArgumentNullException.ThrowIfNull(inputTargetStockByOccupation);
        ArgumentNullException.ThrowIfNull(necessityTargetStockDays);
        ArgumentNullException.ThrowIfNull(preferenceTargetStockDays);
        ArgumentNullException.ThrowIfNull(rankCoefficientPermille);
        ArgumentNullException.ThrowIfNull(budgetRatioPermilleByPurpose);

        if (recipes.Length == 0)
        {
            throw new ArgumentException("レシピは1件以上(GDD02 §2.4)。", nameof(recipes));
        }

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

        // N=0を拒むのは、FloorDiv(摩耗, N)がゼロ除算になるからである(GDD02 §5.3)。
        if (productionRunsPerToolWear < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(productionRunsPerToolWear), productionRunsPerToolWear,
                "工具1個あたりの実行回数Nは1以上(GDD02 §5.3)。");
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

        // 負だけを拒む。0(利幅なし = 原価がそのまま下限)は構造として成立する(GDD02 §12-5)。
        if (minimumMarginPermille < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumMarginPermille), minimumMarginPermille, "最低利幅‰は非負(GDD02 §8.1)。");
        }

        // 行数 = 職業数(Recipes.Length)。長さの一致は行ごとに itemCount と照合する
        // (dailyConsumptionPerNpcByRank と同じ形の検査)。
        if (shipmentTargetStockByOccupation.Length != recipes.Length)
        {
            throw new ArgumentException(
                $"出荷目標在庫の行数は職業数({recipes.Length})と一致する必要がある。",
                nameof(shipmentTargetStockByOccupation));
        }

        for (int occupationId = 0; occupationId < shipmentTargetStockByOccupation.Length; occupationId++)
        {
            var row = shipmentTargetStockByOccupation[occupationId];

            ArgumentNullException.ThrowIfNull(row, nameof(shipmentTargetStockByOccupation));

            if (row.Length != itemCount)
            {
                throw new ArgumentException(
                    $"出荷目標在庫の各行の長さは品目数({itemCount})と一致する必要がある。",
                    nameof(shipmentTargetStockByOccupation));
            }

            var outputs = recipes[occupationId].Outputs;

            for (int itemId = 0; itemId < itemCount; itemId++)
            {
                bool isOutputItem = false;
                foreach (var output in outputs)
                {
                    if (output.ItemId == itemId)
                    {
                        isOutputItem = true;
                        break;
                    }
                }

                int target = row[itemId];

                if (isOutputItem)
                {
                    // 在庫比‰ = CeilDiv(1000 × 販売在庫, 出荷目標在庫) の分母。0だとゼロ除算になる
                    // (GDD02 §8.1.1「定数にすれば停止中の工房も正しく判定される」の前提)。
                    if (target <= 0)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(shipmentTargetStockByOccupation), target,
                            $"出力品目(itemId={itemId})の出荷目標在庫は1以上(GDD02 §8.1.1)。");
                    }
                }
                else if (target != 0)
                {
                    // 出力品目以外を0に強制するのは、表から「どの欄が効くか」を読めるようにするため
                    // (品目×職業45欄のうち意味を持つのは出力品目ぶんだけ)。
                    throw new ArgumentOutOfRangeException(
                        nameof(shipmentTargetStockByOccupation), target,
                        $"出力品目でない欄(itemId={itemId})の出荷目標在庫は0(GDD02 §8.1.1)。");
                }
            }
        }

        // 0だと有効な観測が永久に0件になる(下記の境界規則が「差1日以上」を要求するため)。
        if (observationRetentionDays < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observationRetentionDays), observationRetentionDays,
                "相場観測の保持期間は1日以上(GDD06 §3.1)。");
        }

        // 行数 = 職業数(Recipes.Length)。長さの一致は行ごとに itemCount と照合する
        // (shipmentTargetStockByOccupationと同じ形の検査)。
        if (inputTargetStockByOccupation.Length != recipes.Length)
        {
            throw new ArgumentException(
                $"生産の入力の目標在庫の行数は職業数({recipes.Length})と一致する必要がある。",
                nameof(inputTargetStockByOccupation));
        }

        for (int occupationId = 0; occupationId < inputTargetStockByOccupation.Length; occupationId++)
        {
            var row = inputTargetStockByOccupation[occupationId];

            ArgumentNullException.ThrowIfNull(row, nameof(inputTargetStockByOccupation));

            if (row.Length != itemCount)
            {
                throw new ArgumentException(
                    $"生産の入力の目標在庫の各行の長さは品目数({itemCount})と一致する必要がある。",
                    nameof(inputTargetStockByOccupation));
            }

            var inputs = recipes[occupationId].Inputs;

            for (int itemId = 0; itemId < itemCount; itemId++)
            {
                bool isInputItem = false;
                foreach (var input in inputs)
                {
                    if (input.ItemId == itemId)
                    {
                        isInputItem = true;
                        break;
                    }
                }

                int target = row[itemId];

                if (isInputItem)
                {
                    // 0を拒むのは、0が「この入力は要らない」と読めてしまうからである。GDD02 §8.2.1が
                    // 名指しした「入力切れの世帯がもう要らないと判定されて恒久停止する」状態が、
                    // 定義の側から入る。
                    if (target <= 0)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(inputTargetStockByOccupation), target,
                            $"入力品目(itemId={itemId})の目標在庫は1以上(GDD02 §8.2.1)。");
                    }
                }
                else if (target != 0)
                {
                    // 入力品目以外を0に強制するのは shipmentTargetStockByOccupation と同じ理由
                    // (45欄のうちどれが効くかを表から読めるようにする)。
                    throw new ArgumentOutOfRangeException(
                        nameof(inputTargetStockByOccupation), target,
                        $"入力品目でない欄(itemId={itemId})の目標在庫は0(GDD02 §8.2.1)。");
                }
            }
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

        // 詰みは作らない(GDD02 §4.2)。許容乖離0だと必需の基礎値が常に0になり、パンも薪も
        // 永久に買えなくなる。
        if (necessityTolerancePermille < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(necessityTolerancePermille), necessityTolerancePermille,
                "必需の許容乖離‰は1以上(GDD02 §8.2.1)。");
        }

        // 長さ4はDemandPurposeの用途数(GDD02 §8.2.1・§8.2.7)。
        if (budgetRatioPermilleByPurpose.Length != 4)
        {
            throw new ArgumentException(
                "用途別の予算比率‰は DemandPurpose の4用途ぶん(長さ4)必要(GDD02 §8.2.1)。",
                nameof(budgetRatioPermilleByPurpose));
        }

        // 生産の入力は母数を持たない(派生需要。GDD02 §8.2.1)。観測ゼロ時のフォールバックは
        // Necessityの比率‰を流用すると§8.2.1が明記しているので、専用の欄を作ると
        // 「使われない調整軸」が表に住み着く。
        if (budgetRatioPermilleByPurpose[(int)DemandPurpose.ProductionInput] != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(budgetRatioPermilleByPurpose),
                budgetRatioPermilleByPurpose[(int)DemandPurpose.ProductionInput],
                "生産の入力(ProductionInput)は母数を持たない。予算比率‰は0(GDD02 §8.2.1)。");
        }

        // 詰みは作らない(GDD02 §4.2)。必需が0だとパンも薪も永久に買えず、耐久が0だと
        // 工具が買えず設備係数が全世帯0‰に落ちて恒久停止する。
        if (budgetRatioPermilleByPurpose[(int)DemandPurpose.Necessity] < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(budgetRatioPermilleByPurpose),
                budgetRatioPermilleByPurpose[(int)DemandPurpose.Necessity],
                "必需の予算比率‰は1以上(GDD02 §4.2 / §8.2.7)。");
        }

        if (budgetRatioPermilleByPurpose[(int)DemandPurpose.Durable] < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(budgetRatioPermilleByPurpose),
                budgetRatioPermilleByPurpose[(int)DemandPurpose.Durable],
                "耐久の予算比率‰は1以上(GDD02 §4.2 / §8.2.1)。");
        }

        // 嗜好の0は許す ── 嗜好を切っても詰まない(GDD02 §4.2)。
        if (budgetRatioPermilleByPurpose[(int)DemandPurpose.Preference] < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(budgetRatioPermilleByPurpose),
                budgetRatioPermilleByPurpose[(int)DemandPurpose.Preference],
                "嗜好の予算比率‰は非負(GDD02 §8.2.1)。");
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
        ProductionRunsPerToolWear = productionRunsPerToolWear;

        // jagged配列は行ごとに複製する。外側だけToArray()すると、呼び出し側が検証後に
        // 行の中身を書き換えられてしまう(既存欄の防御的コピーと同じ理由)。
        DailyConsumptionPerNpcByRank = new int[dailyConsumptionPerNpcByRank.Length][];
        for (int rank = 0; rank < dailyConsumptionPerNpcByRank.Length; rank++)
        {
            DailyConsumptionPerNpcByRank[rank] = dailyConsumptionPerNpcByRank[rank].ToArray();
        }

        FirewoodConsumptionSeasonPermille = firewoodConsumptionSeasonPermille.ToArray();

        MinimumMarginPermille = minimumMarginPermille;

        // jagged配列は行ごとに複製する(DailyConsumptionPerNpcByRankと同じ理由)。
        ShipmentTargetStockByOccupation = new int[shipmentTargetStockByOccupation.Length][];
        for (int occupationId = 0; occupationId < shipmentTargetStockByOccupation.Length; occupationId++)
        {
            ShipmentTargetStockByOccupation[occupationId] = shipmentTargetStockByOccupation[occupationId].ToArray();
        }

        ObservationRetentionDays = observationRetentionDays;

        // jagged配列は行ごとに複製する(DailyConsumptionPerNpcByRankと同じ理由)。
        InputTargetStockByOccupation = new int[inputTargetStockByOccupation.Length][];
        for (int occupationId = 0; occupationId < inputTargetStockByOccupation.Length; occupationId++)
        {
            InputTargetStockByOccupation[occupationId] = inputTargetStockByOccupation[occupationId].ToArray();
        }

        NecessityTargetStockDays = necessityTargetStockDays.ToArray();
        PreferenceTargetStockDays = preferenceTargetStockDays.ToArray();
        ToolTargetStockPermille = toolTargetStockPermille;
        RankCoefficientPermille = rankCoefficientPermille.ToArray();
        NecessityTolerancePermille = necessityTolerancePermille;
        BudgetRatioPermilleByPurpose = budgetRatioPermilleByPurpose.ToArray();
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
    /// M0 の初期値(GDD02 §2.2・§2.4・§8.1)。すべて初期値であり調整対象
    /// (CLAUDE.md)。検算と調整は #28。出典を持つのは<b>レシピの品目の組</b>(GDD02 §2.4)と
    /// <b>1次産品(品目0〜3)の取得原価</b>(GDD02 §10.2。外部売値に固定される仕入単価の天井)
    /// であり、数量・所要労働‰・都市生産品(品目4〜8)の金額はここが初出である(GDD02 §13.2)。
    /// </summary>
    private static WorldDefinition BuildM0()
    {
        // 所要労働‰を5職業とも1000にするのは、意図的に無風の初期値を置くためである
        // (GDD02 §5.2の労働力合計は親方1000‰+徒弟300‰=1300‰。#28が調整するときは
        // この上限に触れること)。
        const int LaborPermilleForM0 = 1000; // ‰。1000‰ = 親方1人日相当

        var recipes = new[]
        {
            new Recipe(
                Occupation.Miller,
                outputs: new[] { new ItemQuantity { ItemId = Item.Flour, Quantity = 1 } },
                inputs: new[] { new ItemQuantity { ItemId = Item.Grain, Quantity = 2 } },
                laborPermille: LaborPermilleForM0),
            new Recipe(
                Occupation.Baker,
                outputs: new[] { new ItemQuantity { ItemId = Item.Bread, Quantity = 2 } },
                inputs: new[]
                {
                    new ItemQuantity { ItemId = Item.Flour, Quantity = 1 },
                    new ItemQuantity { ItemId = Item.Firewood, Quantity = 1 },
                },
                laborPermille: LaborPermilleForM0),
            new Recipe(
                Occupation.Brewer,
                outputs: new[] { new ItemQuantity { ItemId = Item.Beer, Quantity = 1 } },
                inputs: new[]
                {
                    new ItemQuantity { ItemId = Item.Grain, Quantity = 2 },
                    new ItemQuantity { ItemId = Item.Firewood, Quantity = 1 },
                },
                laborPermille: LaborPermilleForM0),
            new Recipe(
                Occupation.Woodworker,
                outputs: new[] { new ItemQuantity { ItemId = Item.Firewood, Quantity = 3 } },
                inputs: new[] { new ItemQuantity { ItemId = Item.Timber, Quantity = 1 } },
                laborPermille: LaborPermilleForM0),
            new Recipe(
                Occupation.Smith,
                outputs: new[] { new ItemQuantity { ItemId = Item.Tools, Quantity = 1 } },
                inputs: new[]
                {
                    new ItemQuantity { ItemId = Item.IronOre, Quantity = 2 },
                    new ItemQuantity { ItemId = Item.Charcoal, Quantity = 1 },
                },
                laborPermille: LaborPermilleForM0),
        };

        // 添字 = itemId(Grain, Timber, IronOre, Charcoal, Flour, Firewood, Bread, Beer, Tools)。
        // 1次産品(0〜3)は初出ではない — GDD02 §10.2 の外部売値に固定される(仕入単価の天井)。
        // 都市生産品(4〜8)だけがここの初出であり、#28 の検算・調整対象。
        var initialAcquisitionCost = new[] { 10, 8, 14, 12, 22, 6, 16, 30, 60 }; // 単位: 貨幣/1単位
        var initialHouseholdInventory = new[] { 0, 0, 0, 0, 0, 4, 4, 2, 0 }; // 単位: 個

        // 熟練度‰を階層で違う値にするのは、ハッシュの回帰を鈍らせないためである
        // (全員が同じ値だと、ハッシュからSkillPermilleを落としても値が変わらない)。
        // 添字 = (int)NpcRank(Master, Journeyman, Apprentice)。Journeyman はM0に存在しないが
        // TDD01 §3.2は熟練度を「器として先に持つ」としているので欄だけ埋める。
        var initialSkillPermilleByRank = new[] { 700, 400, 100 }; // 単位: ‰

        // 添字 = (int)NpcRank(Master, Journeyman, Apprentice)。GDD02 §5.2。
        // Journeyman はM0に存在しない(GDD10)が、階層の欄は先に埋める。
        var laborPermilleByRank = new[] { 1000, 800, 300 }; // 単位: ‰

        // 1人1日あたりの消費量。添字 = [(int)NpcRank][itemId]。GDD02 §6.1。
        // itemIdの並びはGrain, Timber, IronOre, Charcoal, Flour, Firewood, Bread, Beer, Tools。
        // 徒弟のビールが0なのは、徒弟が給金を持たないため(GDD02 §6.1 / GDD10 §1)。
        var dailyConsumptionPerNpcByRank = new[]
        {
            new[] { 0, 0, 0, 0, 0, 2, 1, 1, 0 }, // 親方。単位: 個
            new[] { 0, 0, 0, 0, 0, 2, 1, 1, 0 }, // 職人。単位: 個
            new[] { 0, 0, 0, 0, 0, 2, 1, 0, 0 }, // 徒弟。単位: 個
        };

        // 添字 = (int)Season(Spring, Summer, Autumn, Winter)。GDD03 §2.1。基準は秋の1000‰。
        var firewoodConsumptionSeasonPermille = new[] { 800, 400, 1000, 2000 }; // 単位: ‰

        const int MinimumMarginPermilleForM0 = 200; // ‰。20%

        // 出荷目標在庫。添字 = [(int)Occupation][itemId]。単位: 個(GDD02 §8.1.1)。
        // 初期値は「3日分の出力」— 生産能力が 1実行/日 に張り付いている(#28 への申し送り、
        // W2-03)ので、出力数量 × 3 を置く。出力品目以外は 0(コンストラクタが強制する)。
        var shipmentTargetStockByOccupation = new[]
        {
            NewShipmentRow(Item.Flour, 3),     // Miller:     小麦粉1/日 × 3
            NewShipmentRow(Item.Bread, 6),     // Baker:      パン2/日 × 3
            NewShipmentRow(Item.Beer, 3),      // Brewer:     ビール1/日 × 3
            NewShipmentRow(Item.Firewood, 9),  // Woodworker: 薪3/日 × 3
            NewShipmentRow(Item.Tools, 3),     // Smith:      工具1/日 × 3
        };

        const int ObservationRetentionDaysForM0 = 7; // 日(GDD06 §3.1)

        // 生産の入力の目標在庫。添字 = [(int)Occupation][itemId]。単位: 個(GDD02 §8.2.1)。
        // 初期値は「必要数量 × 仕入れ間隔5日」— 生産能力が 1実行/日 に張り付いている(#28 への
        // 申し送り、W2-03)ので 1日分の使用量 = 必要数量。入力品目以外は0(コンストラクタが強制する)。
        const int InputTargetStockDaysForM0 = 5; // 日。仕入れ間隔(GDD02 §8.2.1)

        var inputTargetStockByOccupation = new[]
        {
            // Miller: 穀物2/日 × 5
            NewInputRow((Item.Grain, 2 * InputTargetStockDaysForM0)),
            // Baker: 小麦粉1/日 × 5、薪1/日 × 5
            NewInputRow(
                (Item.Flour, 1 * InputTargetStockDaysForM0),
                (Item.Firewood, 1 * InputTargetStockDaysForM0)),
            // Brewer: 穀物2/日 × 5、薪1/日 × 5
            NewInputRow(
                (Item.Grain, 2 * InputTargetStockDaysForM0),
                (Item.Firewood, 1 * InputTargetStockDaysForM0)),
            // Woodworker: 木材1/日 × 5
            NewInputRow((Item.Timber, 1 * InputTargetStockDaysForM0)),
            // Smith: 鉄鉱石2/日 × 5、木炭1/日 × 5
            NewInputRow(
                (Item.IronOre, 2 * InputTargetStockDaysForM0),
                (Item.Charcoal, 1 * InputTargetStockDaysForM0)),
        };

        // 必需・嗜好の目標在庫の日数。添字 = itemId。単位: 日(GDD02 §8.2.1 の表そのもの)。
        var necessityTargetStockDays = new int[Item.Count];
        necessityTargetStockDays[Item.Firewood] = 7; // 薪: 1週間分(1日消費量は季節変動、GDD02 §9)
        necessityTargetStockDays[Item.Bread] = 3;    // パン: 3日分

        var preferenceTargetStockDays = new int[Item.Count];
        preferenceTargetStockDays[Item.Beer] = 1;    // ビール: 1日分

        const int ToolTargetStockPermilleForM0 = 500; // ‰。1個あたりの耐久値(= N)に対する比率

        // 階層係数‰。添字 = (int)NpcRank(Master, Journeyman, Apprentice)。GDD08 §9 の表。
        // Journeyman は M0 に存在しない(GDD10)が、階層の欄は先に埋める。
        var rankCoefficientPermille = new[] { 1000, 600, 200 }; // 単位: ‰

        const int NecessityTolerancePermilleForM0 = 1200; // ‰。相場の1.2倍までは追随する

        // 用途別の予算比率‰。添字 = (int)DemandPurpose。GDD02 §8.2.1・§8.2.7。
        // ProductionInput が0なのは母数を持たないからである(派生需要。コンストラクタが強制する)。
        var budgetRatioPermilleByPurpose = new[]
        {
            0,   // ProductionInput: 母数なし
            50,  // Necessity:  流動資金の 5%(GDD02 §8.2.7)
            200, // Preference: 余剰資金の 20%
            10,  // Durable:    流動資金の 1%(GDD02 §8.2.1)
        };

        return new WorldDefinition(
            itemCount: Item.Count,
            householdsPerOccupation: 2,
            recipes: recipes,
            initialLiquidFunds: 200, // 単位: 貨幣
            initialAcquisitionCost: initialAcquisitionCost,
            initialHouseholdInventory: initialHouseholdInventory,
            initialWorkshopInputDays: 5, // 単位: 日
            initialToolStock: 1, // 単位: 個
            initialSkillPermilleByRank: initialSkillPermilleByRank,
            laborPermilleByRank: laborPermilleByRank,
            productionRunsPerToolWear: 30, // 単位: 回(GDD02 §5.3)
            dailyConsumptionPerNpcByRank: dailyConsumptionPerNpcByRank,
            firewoodConsumptionSeasonPermille: firewoodConsumptionSeasonPermille,
            minimumMarginPermille: MinimumMarginPermilleForM0,
            shipmentTargetStockByOccupation: shipmentTargetStockByOccupation,
            observationRetentionDays: ObservationRetentionDaysForM0,
            inputTargetStockByOccupation: inputTargetStockByOccupation,
            necessityTargetStockDays: necessityTargetStockDays,
            preferenceTargetStockDays: preferenceTargetStockDays,
            toolTargetStockPermille: ToolTargetStockPermilleForM0,
            rankCoefficientPermille: rankCoefficientPermille,
            necessityTolerancePermille: NecessityTolerancePermilleForM0,
            budgetRatioPermilleByPurpose: budgetRatioPermilleByPurpose);
    }

    /// <summary>
    /// 出荷目標在庫の1行(長さ <see cref="Item.Count"/>)を作る。<paramref name="itemId"/> の欄だけ
    /// <paramref name="target"/> を入れ、残りは0(コンストラクタが出力品目以外の非0を拒む)。
    /// </summary>
    private static int[] NewShipmentRow(int itemId, int target)
    {
        var row = new int[Item.Count];
        row[itemId] = target;

        return row;
    }

    /// <summary>
    /// 生産の入力の目標在庫の1行(長さ <see cref="Item.Count"/>)を作る。渡された欄だけ埋め、
    /// 残りは0(コンストラクタが入力品目以外の非0を拒む)。行を手書きで9個並べない
    /// (<see cref="NewShipmentRow"/> と同じ)。
    /// </summary>
    private static int[] NewInputRow(params (int ItemId, int Target)[] inputs)
    {
        var row = new int[Item.Count];

        foreach (var (itemId, target) in inputs)
        {
            row[itemId] = target;
        }

        return row;
    }
}
