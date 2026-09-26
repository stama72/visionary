using Visionary.Sim.Randomness;

namespace Visionary.Sim.Systems;

/// <summary>
/// 世帯(TDD01 §3.3 順3)。破産中フラグの更新(GDD02b §3.3)と、④の職業付け替え
/// (GDD02b §4.1・§4.2)を1世帯ずつ進める。
/// </summary>
/// <remarks>
/// <para>
/// <b>乱数を一切引かない。</b><see cref="Stream"/> が <see cref="RandomStream.Household"/> を持つのは
/// <see cref="SimScheduler"/> の登録に系統ごとの一意な識別子が要るからであって、実際に
/// <see cref="SimContext.OpenRandom(int)"/> を呼ぶためではない(<see cref="ProductionSystem"/> と
/// 同じ、TDD01 §3.1)。
/// </para>
/// <para>
/// <b>順3 で金は動かない。</b><see cref="HouseholdState.LiquidFunds"/> を読み書きしない
/// (GDD02b §4.1)。流動資金は補填しない。
/// </para>
/// <para>
/// <b>Step の本体は2つの独立したループである</b>(GDD02b §4.1「手順ごとに全世帯をId昇順で回す。
/// 世帯ごとに1→2を回すのではない」)。分割は<b>この規則が要求している</b>ため保つ。
/// </para>
/// <para>
/// <b>ただしこれは機械で守られていない(訂正、#39タスク仕様1巡目レビュー)。</b>手順2が他世帯から
/// 読むのは <see cref="HouseholdState.Occupation"/> と <see cref="HouseholdState.DistrictId"/> だけで、
/// <see cref="HouseholdState.IsBankrupt"/> は自世帯のぶんしか読まない。手順1が書くのは
/// <see cref="HouseholdState.IsBankrupt"/> だけである。<b>したがって畳んだ実装と畳まない実装は、
/// どの世界でも同じ結果を返す</b> ── 変異M-fold(手順1・2を1つのループに畳む。2026-09-23、
/// <c>8bdd6ee</c>)で497件全合格・1本も落ちないことを<c>mutator</c>が実測した。
/// <b>効き始めるのは、手順2が他世帯の<see cref="HouseholdState.IsBankrupt"/>を読むようになった
/// 日である</b>(<a href="https://github.com/stama72/visionary/issues/196">#196</a>が④のゲートを
/// 別の状態へ移すなら、そこで初めて分割が結果を変える)。そのときテストは1本も無い。
/// </para>
/// </remarks>
public sealed class HouseholdSystem : ISimSystem
{
    private readonly WorldDefinition _definition;

    public HouseholdSystem(WorldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _definition = definition;
    }

    public RandomStream Stream => RandomStream.Household;

    public Cadence Cadence => Cadence.Daily(hour: 0);

    public void Step(World world, SimContext context)
    {
        ArgumentNullException.ThrowIfNull(world);

        // 手順1. 破産中フラグの更新(GDD02b §3.3)。Households は添字=Id なので、先頭から走査する
        // だけでADR-0002の処理順規約(Id昇順)を満たす。
        //
        // 代入であって「立てるだけ」ではない。if (count > 0) IsBankrupt = 1; と書くと降りる枝が
        // 消え、一度立ったフラグが永久に残る(GDD02b §3.3「降りる: 前日、そのような購入が
        // 1つも無かった」)。UnaffordableNecessityCountを読むだけで書かない ── 0に戻すのは
        // 順5段5bの役目であり、ここが書くと「前日の値」という意味が壊れる(GDD02b §3.2末尾)。
        foreach (var household in world.Households)
        {
            household.IsBankrupt = household.UnaffordableNecessityCount > 0 ? 1 : 0;
        }

        // 手順2. ④の職業付け替え(GDD02b §4.1のゲート → §4.2の付け替え先)。手順1と同じループへ
        // 畳まない(docコメント参照)。
        foreach (var household in world.Households)
        {
            if (!OccupationReassignment.IsGateOpen(_definition, household))
            {
                continue;
            }

            if (!OccupationReassignment.TrySelectTarget(_definition, world, household, out var target))
            {
                continue;
            }

            household.Occupation = target;

            // 進捗‰だけを0に戻す(GDD02b §4.2)。当日のProductionCapacityRuns/ProductionRunsは
            // 旧職業のレシピで順1が書いた値のまま触らない ── 翌日の順1が新職業のレシピで上書きする
            // (#237。フェーズ1で決めたこと「残る穴」)。
            household.ProductionProgressPermille = 0;
        }
    }
}
