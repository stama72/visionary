using Visionary.Sim.Randomness;
using Visionary.Sim.Time;

namespace Visionary.Sim;

/// <summary>
/// GDD02 §2.2・§2.4・§4.3・§8.1 の初期世界を組み立てる。
/// </summary>
/// <remarks>
/// <para>
/// <b>これは「配置が振られる」ことそのものが仕様である。</b>同一マスターシードなら同一配置、
/// 違うシードなら違う配置になる。ADR-0002 の共通乱数法により、A/B の2条件は同一シードなら
/// 同一配置で揃う。
/// </para>
/// <para>
/// <b>乱数の系統について — ここでは押さえられないもの。</b>「<see cref="RandomStream.WorldGen"/>
/// 以外の系統から引いていないこと」は型では守れない。<see cref="RandomSource"/> は
/// <c>readonly struct</c> で消費の状態を持たないため、どの系統を何回開いたかは外から観測
/// できない。<see cref="WorldGenerator"/> は <see cref="Systems.ISimSystem"/> ではないので
/// <see cref="Systems.SimContext"/> の保護(現在のシステムの系統しか開けない)も効かない。
/// ここは機械で守れておらず、レビュー観点として残る。
/// </para>
/// </remarks>
public static class WorldGenerator
{
    // 制約(同一職業は別区画)を満たせない定義で無限に回らないための上限(仕様)。
    // WorldDefinition の前提検査(世帯数がDistrict.Count〜District.Count*2)を通っても、
    // 例えばHouseholdsPerOccupationがDistrict.Countを超える定義は構造的に満たせない。
    private const int MaxPlacementAttempts = 1000;

    /// <summary>GDD02 §2.2・§2.4・§4.3・§8.1 の初期世界を1つ生成する。</summary>
    public static World Generate(WorldDefinition definition, RandomSource random)
    {
        // definitionの妥当性はここでは検査しない — WorldDefinitionのコンストラクタが
        // 既に通したものしか存在しないため(仕様)。
        ArgumentNullException.ThrowIfNull(definition);

        var world = new World(definition.NpcCount, definition.HouseholdCount, definition.ItemCount);

        // 乱数はWorldGen系統(既存の系統1)をTick.Zero・NoEntityで1本だけ開く。
        // ISimSystemではないのでSimContextを通らず、RandomSource.Openを直接呼ぶ(仕様)。
        var rng = random.Open(RandomStream.WorldGen, Tick.Zero);

        int[] districts = AssignDistricts(ref rng, definition.HouseholdCount);
        Occupation[] occupations = AssignOccupations(ref rng, districts, definition);

        // 生成順序は仕様である。変えると同じシードから別の世界が出る(docs/process/02 規則6)。
        for (int householdId = 0; householdId < definition.HouseholdCount; householdId++)
        {
            // NpcIdの割り当てに乱数を使わない。世帯hの親方が2h、徒弟が2h+1と決まっていれば、
            // 構成員配列は常に[2h, 2h+1]で昇順が自明になる(仕様)。
            int masterId = householdId * 2;
            int apprenticeId = (householdId * 2) + 1;

            var master = world.Npcs[masterId];
            master.HouseholdId = householdId;
            master.Rank = NpcRank.Master;
            master.SkillPermille = definition.InitialSkillPermilleByRank[(int)NpcRank.Master];

            var apprentice = world.Npcs[apprenticeId];
            apprentice.HouseholdId = householdId;
            apprentice.Rank = NpcRank.Apprentice;
            apprentice.SkillPermille = definition.InitialSkillPermilleByRank[(int)NpcRank.Apprentice];

            var household = new HouseholdState(
                id: householdId,
                districtId: districts[householdId],
                headNpcId: masterId,
                memberNpcIds: new[] { masterId, apprenticeId },
                itemCount: definition.ItemCount);

            household.Occupation = occupations[householdId];
            household.LiquidFunds = definition.InitialLiquidFunds;

            for (int itemId = 0; itemId < definition.ItemCount; itemId++)
            {
                household.HouseholdInventory[itemId] = definition.InitialHouseholdInventory[itemId];
                household.PurchaseUnitCostAverage[itemId] = definition.InitialAcquisitionCost[itemId];
            }

            var recipe = definition.Recipes[(int)occupations[householdId]];

            foreach (var input in recipe.Inputs)
            {
                household.WorkshopInventory[input.ItemId] =
                    definition.InitialWorkshopInputDays * input.Quantity;
            }

            // += で足すのは、#28がレシピを変えて工具を入力に持つ職業が現れたときに、
            // 設備ぶんが黙って消えないためである。M0の5レシピはどれも工具を入力に持たないので、
            // 現時点では = でも結果は同じである。
            household.WorkshopInventory[Item.Tools] += definition.InitialToolStock;

            world.Households[householdId] = household;
        }

        return world;
    }

    /// <summary>戻り値は <c>districts[householdId] = 区画Id</c>。</summary>
    private static int[] AssignDistricts(ref RandomSequence rng, int householdCount)
    {
        var slots = new int[householdCount];

        // 全区画を1つずつ土台に置く。空区画を作らない(GDD02 §4.3)。
        for (int districtId = 0; districtId < District.Count; districtId++)
        {
            slots[districtId] = districtId;
        }

        // M0は10-9=1。相異なる区画から採るので1区画あたり最大2世帯になる。
        int extra = householdCount - District.Count;

        var pool = new int[District.Count];
        for (int districtId = 0; districtId < District.Count; districtId++)
        {
            pool[districtId] = districtId;
        }

        // 先頭extra個だけの部分シャッフル(前方からの部分Fisher-Yates)。
        // 各位置iについて、未確定の範囲[i, District.Count)から一様に選んでswapする。
        //
        // この向きも仕様である(全体シャッフルの向きと同じ扱い)。タスク仕様は
        // 「pool を Fisher-Yates で先頭 extra 個だけ部分シャッフル」とだけ書き、向きまでは
        // 明示していなかったため、ここで選んだ前方からの向きを固定する。向きを変えると
        // 同じシードから別の世界が出る。ゴールデン値は無いので、変えても他のテストは
        // 落ちない(#3 は同一プロセス内の2回比較、#4 は分布の広がりしか見ない) — この doc
        // コメントが向きの記録そのものである。
        for (int i = 0; i < extra; i++)
        {
            int j = rng.NextInt(i, District.Count);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        for (int i = 0; i < extra; i++)
        {
            slots[District.Count + i] = pool[i];
        }

        // 世帯Idと区画の対応を無作為化する。
        FisherYatesShuffle(ref rng, slots);

        return slots;
    }

    /// <summary>戻り値は <c>occupations[householdId]</c>。棄却法。</summary>
    private static Occupation[] AssignOccupations(
        ref RandomSequence rng, int[] districts, WorldDefinition definition)
    {
        var labels = new Occupation[definition.HouseholdCount];

        int index = 0;
        for (int occupationId = 0; occupationId < definition.OccupationCount; occupationId++)
        {
            for (int copy = 0; copy < definition.HouseholdsPerOccupation; copy++)
            {
                labels[index] = (Occupation)occupationId;
                index++;
            }
        }

        for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
        {
            FisherYatesShuffle(ref rng, labels);

            if (!HasSameOccupationInSameDistrict(labels, districts))
            {
                return labels;
            }
        }

        throw new InvalidOperationException(
            $"{MaxPlacementAttempts}回試しても職業配置が見つからなかった。"
                + "定義がGDD02 §4.3の密度制約(同一職業は別区画)を満たせない可能性がある。");
    }

    /// <summary>
    /// 区画ごとに職業の重複が無いか調べる。<b>Dictionary / HashSet を使わない</b>(ADR-0002)。
    /// </summary>
    /// <remarks>
    /// <b>全ペアを比較する。</b>1区画あたり最大2世帯という前提(<see cref="AssignDistricts"/> と
    /// <see cref="WorldDefinition"/> の密度検査が構造で保証している)に依存しない実装を選んだ
    /// — 「区画ごとに最初に見た職業だけを記録する」実装だと、1区画に3世帯以上入る変則的な
    /// 呼び出しで <c>(A, B, B)</c> のような2件目以降の重複を見逃す。世帯数は最大でも
    /// <see cref="District.Count"/> の2倍程度なので O(n^2) でも性能上の問題はない。
    /// </remarks>
    private static bool HasSameOccupationInSameDistrict(Occupation[] labels, int[] districts)
    {
        for (int i = 0; i < labels.Length; i++)
        {
            for (int j = i + 1; j < labels.Length; j++)
            {
                if (districts[i] == districts[j] && labels[i] == labels[j])
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Fisher-Yatesの向きも仕様である。<c>for i = n-1 downto 1: j = rng.NextInt(0, i + 1);
    /// swap(a[i], a[j])</c>。向きを変えると同じシードから別の配置が出る。
    /// </summary>
    private static void FisherYatesShuffle<T>(ref RandomSequence rng, T[] array)
    {
        for (int i = array.Length - 1; i >= 1; i--)
        {
            int j = rng.NextInt(0, i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }
}
