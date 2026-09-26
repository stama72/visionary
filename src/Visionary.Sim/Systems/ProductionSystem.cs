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
/// <see cref="WorldDefinition.EquipmentPermilleWithoutTools"/>(GDD02a §3)。<b>工具が無ければ
/// どの職業も生産が半分になる(GDD02a §3)。</b>鍛冶は所要労働‰が高い(1300‰)ため、進捗‰の
/// 持ち越し(#237)により工具在庫0(設備係数500‰)でも2日に1回の完成になる ── 旧仕様
/// (日ごとに進捗‰を捨てる)では能力0(完全停止)に詰んでいたが、#237がその前提を崩した。
/// <see cref="SellableStock"/>(GDD02c §1.3)の留保が売り注文・輸出・購入の3経路すべてで
/// 工房在庫を1未満にさせないのは、詰みを防ぐためではなく自己参照(自分の出力である工具を
/// 手放し切って設備係数が下がる経路)を切るためである。連続化・熟練度・機会費用は本タスクの
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

        // 設備係数‰は二値(GDD02a §3。連続化はv1.0)。「工具が無ければ半分の能力で続く」は
        // #237の進捗‰の持ち越しにより全職業で成り立つ(クラスdocコメント参照。旧仕様は生産能力1の
        // 鍛冶だけ工具在庫0で詰んでいたが、持ち越しがその前提を崩した)。求め方は
        // LaborCapacity(#40)へ寄せた ── NeedGenerationSystem が同じ2つの値を必要とするため
        // (値も順序も変えない)。
        int equipmentPermille = LaborCapacity.EquipmentPermille(_definition, household);

        // 前日の外出の労働損失‰を引く(GDD02a §2)。max(0, …)を落とすと、損失が労働力合計を
        // 超えたときに負のまま EffectiveLaborPermille へ渡ってしまう(テスト#16)。構成員は
        // MemberNpcIdsの昇順のまま(LaborCapacity.LaborPermilleが並べ替えない)。
        int laborPermille = LaborCapacity.LaborPermille(_definition, world, household);

        // 進捗‰ = 前日から持ち越した端数 + 当日の実効労働‰(GDD02a §1)。端数を捨てずに足すのが
        // #237の実体 ── 日ごとに進捗‰を0から数え直すと、労働力が所要労働‰未満の日が続いた
        // ときに端数が積み上がらず、鍛冶(所要労働1300‰)のような生産能力1の職業が
        // 些細な労働損失で完成できなくなる(タスク仕様「順序・境界の具体例」)。
        int progress = household.ProductionProgressPermille
            + LaborCapacity.EffectiveLaborPermille(laborPermille, equipmentPermille);

        int capacity = recipe.CapacityRunsFromProgress(progress);

        // 入力から作れる回数とのminを取る(初期値intmaxなら入力0件のレシピは永久に停止しない。
        // GDD02 §2.3が入力0件を許す)。
        int runs = Math.Min(capacity, recipe.RunsFromInputs(household.WorkshopInventory));

        // 0の日も必ず書く(#39の④のゲート・#40のNeedが読む。#237で能力の欄も同じく毎日書く)。
        household.ProductionCapacityRuns = capacity;
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

        // 持ち越す進捗‰ = min(進捗‰ − 所要労働‰×runs, 所要労働‰ − 1)(GDD02a §1)。
        // 入力切れで capacity 回ぶん使い切れなかった労働は捨てる ── minを落とすと、入力が
        // 足りない日に消化しなかった労働力が無限に積み上がり、入力が届いた日に出力が跳ねる
        // (タスク仕様「入力が2回分しか無い日」の具体例、テスト#4)。
        household.ProductionProgressPermille = Math.Min(
            progress - (recipe.LaborPermille * runs), recipe.LaborPermille - 1);

        WearTools(household, recipe.LaborPermille, runs);
    }

    /// <summary>
    /// 工具の摩耗(GDD02a §3.1)。出力の加算より後に呼ぶこと ── 鍛冶が工具を生産した当日に
    /// 摩耗で在庫を0へ落としてから戻すと、端数(<see cref="HouseholdState.ToolWear"/>)が
    /// 理由なく捨てられる(タスク仕様の具体例)。
    /// </summary>
    /// <remarks>
    /// <b>実行回数ではなく労働量(‰人日)で数える。</b>所要労働‰ が108から1300まで違うので、
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
