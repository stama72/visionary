using Visionary.Sim.Randomness;
using Visionary.Sim.Time;

namespace Visionary.Sim.Systems;

/// <summary>
/// 消費(TDD01 §3.3 順2)。GDD02b §1 / GDD02d §5 の1人1日あたりの消費量を世帯在庫から引き、
/// 不足量を <see cref="HouseholdState.UnmetConsumption"/> に記録する。
/// </summary>
/// <remarks>
/// <para>
/// <b>乱数を一切引かない。</b><see cref="ProductionSystem"/> と同じ理由(TDD01 §3.1)。
/// </para>
/// <para>
/// <b>減らすのは世帯在庫だけである</b>(消費)。<b>工房在庫を減らすのは自家消費の移動だけで、
/// 対象は自分のレシピの出力品目に限る</b>(GDD02b §1.1)。<b>生産の入力として抱えている
/// 工房在庫には触れない</b>。
/// </para>
/// <para>
/// <b>#40(NeedGeneration)への申し送り。</b>ここが書く <see cref="HouseholdState.UnmetConsumption"/>
/// が「足りなかったぶん」の唯一の記録である ── GDD02b §1 の「在庫は0で下げ止まり、
/// 足りなかったぶんは繰り越さない」という規約のせいで、消費後の在庫からは
/// 「ちょうど足りた」と「足りずに0になった」を区別できない(#34 タスク仕様)。
/// </para>
/// </remarks>
public sealed class ConsumptionSystem : ISimSystem
{
    private readonly WorldDefinition _definition;

    public ConsumptionSystem(WorldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _definition = definition;
    }

    public RandomStream Stream => RandomStream.Consumption;

    public Cadence Cadence => Cadence.Daily(hour: 0);

    public void Step(World world, SimContext context)
    {
        ArgumentNullException.ThrowIfNull(world);

        // 1日1回だけ引く。世帯ごとに引き直すと、Consumptionが暦の計算をtick単位で
        // 何度も繰り返すことになり、GDD03 §2.1 の季節境界と実行時刻がずれても気づけない。
        var season = GameDate.FromTick(world.Now).Season;

        // 世帯Id昇順に1世帯ずつ(仕様)。
        foreach (var household in world.Households)
        {
            RunOneHousehold(world, household, season);
        }
    }

    private void RunOneHousehold(World world, HouseholdState household, Season season)
    {
        TakeOwnOutputHome(world, household, season); // GDD02b §1.1。消費の前

        for (int itemId = 0; itemId < _definition.ItemCount; itemId++)
        {
            // 消費量の計算は1か所にしか置かない(#36 タスク仕様)。目標在庫(#36)の先読みも
            // 同じ式を使う ── 書き分けると目標在庫と実消費が静かにずれる。
            int requiredQuantity = DailyConsumption.Quantity(_definition, world, household, itemId, season);

            int consumedQuantity = Math.Min(requiredQuantity, household.HouseholdInventory[itemId]);
            household.HouseholdInventory[itemId] -= consumedQuantity;

            // 不足が無い日も0で上書きする。不足した日だけ書くと、前日の不足が
            // #40 の Need として残り続ける(タスク仕様)。
            household.UnmetConsumption[itemId] = requiredQuantity - consumedQuantity;
        }
    }

    /// <summary>自家消費(GDD02b §1.1)。移すのは自分のレシピの出力品目だけである。</summary>
    /// <remarks>
    /// <para>
    /// <b>走査するのは <c>recipe.Outputs</c> であって全品目ではない。</b>全品目を走ると、パン屋が
    /// 生産の入力として抱えている工房在庫の薪を食べてしまう ── 薪は必需の消費財でもあり
    /// (GDD02 §2.2)、<see cref="SellableStock"/> の留保(入力1回分 = 1個)は1個しか守らない。
    /// 自家消費は「自分が作ったものを食べる」であって「工房にあるものを食べる」ではない
    /// (GDD02b §1.1「対象品目 = 自分のレシピの出力品目」)。
    /// </para>
    /// <para>
    /// <b><c>recipe.Outputs</c> は配列であり、列挙順は定義順で確定している</b>(ADR-0002)。
    /// 並べ替えない。同じ品目が2回現れる定義なら2回目は更新後の在庫を見るが、M0 に該当は無く、
    /// 順序が結果を変えるだけで非決定にはならない。
    /// </para>
    /// <para>
    /// <b>世帯の走査順(Id昇順)は既存のループがそのまま持つ。</b>自家消費は世帯内で閉じている
    /// (共有資源に触れない)ので、順序が結果を変えない。
    /// </para>
    /// </remarks>
    private void TakeOwnOutputHome(World world, HouseholdState household, Season season)
    {
        var recipe = _definition.Recipes[(int)household.Occupation];

        foreach (var output in recipe.Outputs)
        {
            int quantity = SelfConsumption.TransferQuantity(
                _definition, world, household, output.ItemId, season);

            household.WorkshopInventory[output.ItemId] -= quantity;
            household.HouseholdInventory[output.ItemId] += quantity;
        }
    }
}
