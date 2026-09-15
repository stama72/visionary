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
        int[] initialSkillPermilleByRank)
    {
        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(initialAcquisitionCost);
        ArgumentNullException.ThrowIfNull(initialHouseholdInventory);
        ArgumentNullException.ThrowIfNull(initialSkillPermilleByRank);

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
    /// (CLAUDE.md)。検算と調整は #28。出典を持つのはレシピの品目の組(GDD02 §2.4)だけで、
    /// 数量・所要労働‰・金額はここが初出である(GDD02 §13.2)。
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
        var initialAcquisitionCost = new[] { 10, 8, 20, 12, 22, 6, 16, 30, 60 }; // 単位: 貨幣/1単位
        var initialHouseholdInventory = new[] { 0, 0, 0, 0, 0, 4, 4, 2, 0 }; // 単位: 個

        // 熟練度‰を階層で違う値にするのは、ハッシュの回帰を鈍らせないためである
        // (全員が同じ値だと、ハッシュからSkillPermilleを落としても値が変わらない)。
        // 添字 = (int)NpcRank(Master, Journeyman, Apprentice)。Journeyman はM0に存在しないが
        // TDD01 §3.2は熟練度を「器として先に持つ」としているので欄だけ埋める。
        var initialSkillPermilleByRank = new[] { 700, 400, 100 }; // 単位: ‰

        return new WorldDefinition(
            itemCount: Item.Count,
            householdsPerOccupation: 2,
            recipes: recipes,
            initialLiquidFunds: 200, // 単位: 貨幣
            initialAcquisitionCost: initialAcquisitionCost,
            initialHouseholdInventory: initialHouseholdInventory,
            initialWorkshopInputDays: 5, // 単位: 日
            initialToolStock: 1, // 単位: 個
            initialSkillPermilleByRank: initialSkillPermilleByRank);
    }
}
