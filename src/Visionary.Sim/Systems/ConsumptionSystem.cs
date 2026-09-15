using Visionary.Sim.Numerics;
using Visionary.Sim.Randomness;
using Visionary.Sim.Time;

namespace Visionary.Sim.Systems;

/// <summary>
/// 消費(TDD01 §3.3 順2)。GDD02 §6.1・§9 の1人1日あたりの消費量を世帯在庫から引き、
/// 不足量を <see cref="HouseholdState.UnmetConsumption"/> に記録する。
/// </summary>
/// <remarks>
/// <para>
/// <b>乱数を一切引かない。</b><see cref="ProductionSystem"/> と同じ理由(TDD01 §3.1)。
/// </para>
/// <para>
/// <b>触るのは世帯在庫だけである。</b>工房在庫の薪(itemId 5、生産入力でもある)には触れない
/// (TDD01 §3.2)。
/// </para>
/// <para>
/// <b>#40(NeedGeneration)への申し送り。</b>ここが書く <see cref="HouseholdState.UnmetConsumption"/>
/// が「足りなかったぶん」の唯一の記録である ── GDD02 §6.1 の「在庫は0で下げ止まり、
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
        for (int itemId = 0; itemId < _definition.ItemCount; itemId++)
        {
            int requiredQuantity = RequiredQuantityFor(world, household, itemId, season);

            int consumedQuantity = Math.Min(requiredQuantity, household.HouseholdInventory[itemId]);
            household.HouseholdInventory[itemId] -= consumedQuantity;

            // 不足が無い日も0で上書きする。不足した日だけ書くと、前日の不足が
            // #40 の Need として残り続ける(タスク仕様)。
            household.UnmetConsumption[itemId] = requiredQuantity - consumedQuantity;
        }
    }

    private int RequiredQuantityFor(World world, HouseholdState household, int itemId, Season season)
    {
        int requiredQuantity = 0;

        // 構成員は先頭から(MemberNpcIdsの昇順は構築時に検証済み)。
        foreach (int npcId in household.MemberNpcIds)
        {
            int baseQuantity = _definition.DailyConsumptionPerNpcByRank[(int)world.Npcs[npcId].Rank][itemId];

            // 季節係数を掛けるのは薪だけ(GDD02 §9)。構成員ごとに切り上げてから合計する ──
            // 世帯合計に先に掛けると、基礎量が奇数の場合に結果がずれる(GDD02 §6.1)。
            requiredQuantity += itemId == Item.Firewood
                ? IntegerMath.ApplyPermille(
                    baseQuantity, _definition.FirewoodConsumptionSeasonPermille[(int)season])
                : baseQuantity;
        }

        return requiredQuantity;
    }
}
