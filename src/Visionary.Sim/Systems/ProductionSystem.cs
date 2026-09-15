using Visionary.Sim.Numerics;
using Visionary.Sim.Randomness;

namespace Visionary.Sim.Systems;

/// <summary>
/// 生産(TDD01 §3.3 順1)。GDD02 §5.2・§5.3 の生産能力・入力充足・工具摩耗を1世帯ずつ進める。
/// </summary>
/// <remarks>
/// <para>
/// <b>乱数を一切引かない。</b>GDD02 §5.3 が「確率ではなく決定的に」と決めている。
/// <see cref="Stream"/> が <see cref="RandomStream.Production"/> を持つのは、
/// <see cref="SimScheduler"/> の登録に系統ごとの一意な識別子が要るからであって、
/// 実際に <see cref="SimContext.OpenRandom(int)"/> を呼ぶためではない(TDD01 §3.1)。
/// </para>
/// <para>
/// <b>設備係数の連続化・熟練度・機会費用は本タスクのスコープ外</b>(#34 の申し送り、
/// GDD02 §5.3 / GDD08 §9)。設備係数は工具在庫 ≥ 1 かどうかの二値(1000‰ / 0‰)のみ。
/// </para>
/// </remarks>
public sealed class ProductionSystem : ISimSystem
{
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

        // 設備係数‰は二値(GDD02 §5.3。連続化はv1.0)。工具切れなら0で生産能力が0に張り付く。
        int equipmentPermille = household.WorkshopInventory[Item.Tools] >= 1
            ? IntegerMath.PermilleScale
            : 0;

        // 構成員は先頭から(MemberNpcIdsの昇順は構築時に検証済み。並べ替え直さない)。
        int totalLaborPermille = 0;
        foreach (int npcId in household.MemberNpcIds)
        {
            totalLaborPermille += _definition.LaborPermilleByRank[(int)world.Npcs[npcId].Rank];
        }

        // GDD02 §5.2 の「切り上げ規約の意図的な例外」。ApplyPermille自体は切り上げのままでよい
        // (設備係数が二値のM0では実害が無いとGDD02 §5.2の註が明記している)。
        int capacity = IntegerMath.FloorDiv(
            IntegerMath.ApplyPermille(totalLaborPermille, equipmentPermille), recipe.LaborPermille);

        // 初期値0にすると入力0件のレシピが永久に停止する(GDD02 §2.3が入力0件を許す)。
        int runs = capacity;
        foreach (var input in recipe.Inputs)
        {
            int affordableRuns = IntegerMath.FloorDiv(household.WorkshopInventory[input.ItemId], input.Quantity);
            runs = Math.Min(runs, affordableRuns);
        }

        if (runs <= 0)
        {
            // 工具切れ・入力切れ・労働力不足のいずれでも、在庫も摩耗も一切動かさない。
            return;
        }

        foreach (var input in recipe.Inputs)
        {
            household.WorkshopInventory[input.ItemId] -= input.Quantity * runs;
        }

        foreach (var output in recipe.Outputs)
        {
            household.WorkshopInventory[output.ItemId] += output.Quantity * runs;
        }

        WearTools(household, runs);
    }

    /// <summary>
    /// 工具の摩耗(GDD02 §5.3)。出力の加算より後に呼ぶこと ── 鍛冶が工具を生産した当日に
    /// 摩耗で在庫を0へ落としてから戻すと、端数(<see cref="HouseholdState.ToolWearCount"/>)が
    /// 理由なく捨てられる(タスク仕様の具体例)。
    /// </summary>
    private void WearTools(HouseholdState household, int runs)
    {
        household.ToolWearCount += runs;

        int worn = IntegerMath.FloorDiv(household.ToolWearCount, _definition.ProductionRunsPerToolWear);

        if (worn <= 0)
        {
            return;
        }

        int consumed = Math.Min(worn, household.WorkshopInventory[Item.Tools]);
        household.WorkshopInventory[Item.Tools] -= consumed;
        household.ToolWearCount -= consumed * _definition.ProductionRunsPerToolWear;

        if (household.WorkshopInventory[Item.Tools] == 0)
        {
            // 工具が尽きたので端数を持ち越さない ── 次に手に入る工具は摩耗していない新品として
            // 扱う(タスク仕様の具体例。テスト #10)。
            household.ToolWearCount = 0;
        }
    }
}
