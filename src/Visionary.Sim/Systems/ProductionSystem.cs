using Visionary.Sim.Numerics;
using Visionary.Sim.Randomness;

namespace Visionary.Sim.Systems;

/// <summary>
/// 生産(TDD01 §3.3 順1)。GDD02a §1〜§4 の生産能力・入力充足・工具摩耗を1世帯ずつ進める。
/// </summary>
/// <remarks>
/// <para>
/// <b>乱数を一切引かない。</b>GDD02a §3.1 が「確率ではなく決定的に」と決めている。
/// <see cref="Stream"/> が <see cref="RandomStream.Production"/> を持つのは、
/// <see cref="SimScheduler"/> の登録に系統ごとの一意な識別子が要るからであって、
/// 実際に <see cref="SimContext.OpenRandom(int)"/> を呼ぶためではない(TDD01 §3.1)。
/// </para>
/// <para>
/// <b>設備係数は工具在庫 ≥ 1 かどうかの二値。</b>ありなら1000‰、無ければ
/// <see cref="WorldDefinition.EquipmentPermilleWithoutTools"/>(GDD02a §3)。<b>「工具切れでも
/// 半分の能力で続く」が成り立つのは生産能力2以上の職業に限る(鍛冶については偽、W2-14)。</b>
/// 鍛冶は所要労働‰が高く生産能力が1なので、<see cref="Recipe.CapacityRuns"/>の2段の
/// 切り下げにより工具在庫0(設備係数500‰)で能力が0(完全停止)になる ── 自分の出力(工具)を
/// 作れず回復経路を失って詰む。実際に鍛冶が最後の1個を失わないのは、この500‰の設備係数
/// そのものではなく<see cref="SellableStock"/>(GDD02c §1.3)の留保が売り注文・輸出・購入の
/// 3経路すべてで工房在庫を1未満にさせないからである。連続化・熟練度・機会費用は本タスクの
/// スコープ外(生産関数の分冊、issue #92)。
/// </para>
/// </remarks>
public sealed class ProductionSystem : ISimSystem
{
    /// <summary>設備係数‰ が 1000 になる最小の工具在庫(個。GDD02a §3)。</summary>
    /// <remarks>
    /// <see cref="SellableStock"/> の工具の留保量と同じ値である。偶然ではなく、GDD02c §1.3 が
    /// 「設備係数‰ が 1000 になる最小在庫」を留保量の定義として採っている(#148 決定2)。
    /// 2か所に別々のリテラルで置くと黙って食い違うので、定数はここに1つだけ置く。
    /// </remarks>
    public const int EquipmentThresholdStock = 1;

    private readonly WorldDefinition _definition;

    public ProductionSystem(WorldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _definition = definition;
    }

    public RandomStream Stream => RandomStream.Production;

    public Cadence Cadence => Cadence.Daily(hour: 0);

    public void Step(World world, SimContext context)
    {
        ArgumentNullException.ThrowIfNull(world);

        // 世帯Id昇順に1世帯ずつ(仕様)。Households は添字=Id の配列なので、先頭から
        // 走査するだけで ADR-0002 の処理順規約を満たす。
        foreach (var household in world.Households)
        {
            RunOneHousehold(world, household);
        }
    }

    private void RunOneHousehold(World world, HouseholdState household)
    {
        var recipe = _definition.Recipes[(int)household.Occupation];

        // 設備係数‰は二値(GDD02a §3。連続化はv1.0)。旧仕様の「工具が無ければ停止」は消えた
        // (#96)が、「半分の能力で続く」は生産能力2以上の職業に限る話であり、鍛冶(生産能力1)には
        // 成り立たない(工具在庫0で能力0になり詰む。W2-14。クラスdocコメント参照)。
        int equipmentPermille = household.WorkshopInventory[Item.Tools] >= EquipmentThresholdStock
            ? IntegerMath.PermilleScale
            : _definition.EquipmentPermilleWithoutTools;

        // 構成員は先頭から(MemberNpcIdsの昇順は構築時に検証済み。並べ替え直さない)。
        int totalLaborPermille = 0;
        foreach (int npcId in household.MemberNpcIds)
        {
            totalLaborPermille += _definition.LaborPermilleByRank[(int)world.Npcs[npcId].Rank];
        }

        // 前日の外出の労働損失‰を引く(GDD02a §2)。max(0, …)を落とすと、損失が労働力合計を
        // 超えたときに負のまま CapacityRuns へ渡ってしまう(テスト#16)。
        int laborPermille = Math.Max(0, totalLaborPermille - household.ErrandLaborLossPermille);

        int capacity = recipe.CapacityRuns(laborPermille, equipmentPermille);

        // 初期値0にすると入力0件のレシピが永久に停止する(GDD02 §2.3が入力0件を許す)。
        int runs = capacity;
        foreach (var input in recipe.Inputs)
        {
            int affordableRuns = IntegerMath.FloorDiv(household.WorkshopInventory[input.ItemId], input.Quantity);
            runs = Math.Min(runs, affordableRuns);
        }

        // 0の日も必ず書く(#39の④のゲート・#40のNeedが読む。タスク仕様)。
        household.ProductionRuns = runs;

        // ここで早期returnしない(#96 3巡目 象限Iの訂正)。runs==0なら在庫の増減は0の掛け算に
        // なるだけで実害は無いが、摩耗の破棄(WearTools末尾の無条件の手順)は実行回数0の日も
        // 必ず走らせる必要がある ── 早期returnがあると、それだけが道連れで飛んでしまう。
        foreach (var input in recipe.Inputs)
        {
            household.WorkshopInventory[input.ItemId] -= input.Quantity * runs;
        }

        foreach (var output in recipe.Outputs)
        {
            household.WorkshopInventory[output.ItemId] += output.Quantity * runs;
        }

        WearTools(household, recipe.LaborPermille, runs);
    }

    /// <summary>
    /// 工具の摩耗(GDD02a §3.1)。出力の加算より後に呼ぶこと ── 鍛冶が工具を生産した当日に
    /// 摩耗で在庫を0へ落としてから戻すと、端数(<see cref="HouseholdState.ToolWear"/>)が
    /// 理由なく捨てられる(タスク仕様の具体例)。
    /// </summary>
    /// <remarks>
    /// <b>実行回数ではなく労働量(‰人日)で数える。</b>所要労働‰ が108から1000まで違うので、
    /// 回数で数えると木材加工は鍛冶の12倍の速さで工具を消費する(GDD02a §3.1)。
    /// </remarks>
    private void WearTools(HouseholdState household, int laborPermille, int runs)
    {
        household.ToolWear += laborPermille * runs;

        int worn = IntegerMath.FloorDiv(household.ToolWear, _definition.ToolDurabilityPerUnit);

        if (worn > 0)
        {
            int consumed = Math.Min(worn, household.WorkshopInventory[Item.Tools]);
            household.WorkshopInventory[Item.Tools] -= consumed;
            household.ToolWear -= consumed * _definition.ToolDurabilityPerUnit;
        }

        // worn>0 の外。日次の無条件の手順(GDD02a §3.1)。工具0個のまま摩耗を積み続けると、
        // 次に買った工具がその日のうちに壊れうる(worn==0でも累積を捨てないと再現する)。
        if (household.WorkshopInventory[Item.Tools] == 0)
        {
            household.ToolWear = 0;
        }
    }
}
