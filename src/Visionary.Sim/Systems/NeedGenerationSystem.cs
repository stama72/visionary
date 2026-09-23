using Visionary.Sim.Randomness;
using Visionary.Sim.Time;

namespace Visionary.Sim.Systems;

/// <summary>
/// ニーズの生成(TDD01 §3.3 順4。#40)。GDD02b §8.1 の5理由を毎日、一覧を丸ごと組み直す。
/// </summary>
/// <remarks>
/// <para>
/// <b>乱数を一切引かない。</b><see cref="Stream"/> が <see cref="RandomStream.NeedGeneration"/>
/// を持つのは <see cref="SimScheduler"/> の登録に系統ごとの一意な識別子が要るからであって、
/// 実際に <see cref="SimContext.OpenRandom(int)"/> を呼ぶためではない
/// (<see cref="ProductionSystem"/> と同じ、TDD01 §3.1)。
/// </para>
/// <para>
/// <b>一覧を毎日まるごと組み直すのが失効である</b>(GDD02b §8.1)。条件が消えた組は積まれないので
/// 落ちる。「失効した Need を探して消す」処理を別に書かない ── 2つの規則ができ、片方だけ直る。
/// </para>
/// <para>
/// <b>並びが履歴に依存しない。</b>一覧は常に (世帯Id, 理由コード, 品目Id) の昇順であり、
/// <see cref="Determinism.StateHasher"/> が <c>List</c> の格納順そのままを読む(TDD01 §3.8)ため、
/// 履歴に依存すると「同じ状態に別の経路で到達した」が別ハッシュになる。
/// </para>
/// <para>
/// <b>世帯の状態は1つも書き換えない。</b>書くのは <see cref="World.Needs"/> と
/// <see cref="World.NextNeedId"/> の2つだけである。
/// </para>
/// </remarks>
public sealed class NeedGenerationSystem : ISimSystem
{
    private readonly WorldDefinition _definition;

    public NeedGenerationSystem(WorldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _definition = definition;
    }

    public RandomStream Stream => RandomStream.NeedGeneration;

    public Cadence Cadence => Cadence.Daily(hour: 0);

    public void Step(World world, SimContext context)
    {
        ArgumentNullException.ThrowIfNull(world);

        // 1. 組み直した一覧を空で作る。
        var rebuilt = new List<Need>();

        // 2. 世帯Id昇順(Households は添字=Id)に、各世帯で理由コードの値の昇順(1→5)に、
        // 各理由で品目Id昇順に、条件を満たす組を積む(手順そのものが仕様)。
        foreach (var household in world.Households)
        {
            CollectProductionStopped(world, household, rebuilt);
            CollectDistress(household, rebuilt);
            CollectCannotExpandProduction(world, household, rebuilt);
            CollectToolsExhausted(household, rebuilt);
            CollectDistantStock(household, rebuilt);
        }

        // 3. 積んだ各組について、world.Needs(前日の一覧)を先頭から線形に走査し、
        // (TargetHouseholdId, ReasonCode, ItemId) が一致する要素があればその Id を採る。
        // 無ければ world.NextNeedId を採り、1つ進める。rebuilt 側の重複検査は要らない ──
        // 2 の走査が (理由, 品目) について一意なので、同じ日に同じ3つ組を2度積むことは無い。
        for (int i = 0; i < rebuilt.Count; i++)
        {
            int id = ResolveId(world, rebuilt[i]);
            rebuilt[i] = rebuilt[i] with { Id = id };
        }

        // 4. 丸ごと入れ替える。これが失効の実体である(条件が消えた組は2で積まれないので落ちる)。
        world.Needs.Clear();
        world.Needs.AddRange(rebuilt);
    }

    private static int ResolveId(World world, in Need candidate)
    {
        foreach (var existing in world.Needs)
        {
            if (existing.TargetHouseholdId == candidate.TargetHouseholdId
                && existing.ReasonCode == candidate.ReasonCode
                && existing.ItemId == candidate.ItemId)
            {
                return existing.Id;
            }
        }

        int id = world.NextNeedId;
        world.NextNeedId++;
        return id;
    }

    /// <summary>
    /// 理由1: 生産停止(GDD02b §8.1)。品目Idで外側を回し、その品目が入力かどうかを内側で調べる
    /// ── <see cref="Recipe.Inputs"/> は品目Id昇順であることを保証しない(<see cref="BuyerDemand"/>
    /// が同じ理由で品目Idの外側ループを回している)。「生産能力が0か」は条件に入らない ──
    /// 入力が足りていて生産量0ならそれは理由3(労働力)が拾う。
    /// </summary>
    private void CollectProductionStopped(World world, HouseholdState household, List<Need> rebuilt)
    {
        if (household.ProductionRuns != 0)
        {
            return;
        }

        var recipe = _definition.Recipes[(int)household.Occupation];

        for (int itemId = 0; itemId < _definition.ItemCount; itemId++)
        {
            int requiredQuantity = 0;
            bool isInput = false;

            foreach (var input in recipe.Inputs)
            {
                if (input.ItemId == itemId)
                {
                    isInput = true;
                    requiredQuantity = input.Quantity;
                    break;
                }
            }

            if (!isInput)
            {
                continue;
            }

            int shortage = requiredQuantity - household.WorkshopInventory[itemId];

            if (shortage <= 0)
            {
                continue;
            }

            Add(rebuilt, household.Id, NeedReason.ProductionStopped, itemId, shortage);
        }
    }

    /// <summary>
    /// 理由2: 困窮(GDD02b §8.1)。<see cref="HouseholdState.IsBankrupt"/> が見ているのは現金で
    /// あって在庫ではない(GDD02b §3.3)ため、<see cref="HouseholdState.UnmetConsumption"/> が
    /// 正であることを併せて見る。<see cref="WorldDefinition.NecessityTargetStockDays"/> が
    /// 正の品目に絞る ── 落とすと嗜好品(ビール)にも立つ(GDD02b §1)。
    /// </summary>
    private void CollectDistress(HouseholdState household, List<Need> rebuilt)
    {
        if (household.IsBankrupt != 1)
        {
            return;
        }

        for (int itemId = 0; itemId < _definition.ItemCount; itemId++)
        {
            if (_definition.NecessityTargetStockDays[itemId] <= 0)
            {
                continue;
            }

            int unmet = household.UnmetConsumption[itemId];

            if (unmet <= 0)
            {
                continue;
            }

            Add(rebuilt, household.Id, NeedReason.Distress, itemId, unmet);
        }
    }

    /// <summary>
    /// 理由3: 増産できない(GDD02a §2)。<c>CapacityRuns(...) == 0</c> は「労働力合計‰ ×
    /// 設備係数‰ ÷ 1000 &lt; 所要労働‰」と同値であり、式を綴り直さず同値を使う
    /// (<see cref="Recipe.CapacityRuns"/> の2段の切り下げを写し損ねる余地が無い)。数量の側だけは
    /// 引き算が要るので <see cref="LaborCapacity.EffectiveLaborPermille"/> を呼ぶ(<c>CapacityRuns</c>
    /// の内側と同じ関数)。条件が「所要労働‰ &gt; 実効労働‰」と同値なので差は必ず1以上。
    /// </summary>
    private void CollectCannotExpandProduction(World world, HouseholdState household, List<Need> rebuilt)
    {
        var recipe = _definition.Recipes[(int)household.Occupation];

        int labor = LaborCapacity.LaborPermille(_definition, world, household);
        int equipment = LaborCapacity.EquipmentPermille(_definition, household);

        if (recipe.CapacityRuns(labor, equipment) != 0)
        {
            return;
        }

        int effectiveLabor = LaborCapacity.EffectiveLaborPermille(labor, equipment);
        int shortage = recipe.LaborPermille - effectiveLabor;

        // Outputs[0]を読んでよいのは、TradeSystemのコンストラクタが全レシピの出力2件以上を
        // 拒んでいるからである(OccupationReassignment.IsGateOpenが同じ根拠で同じ読み方をしている)。
        Add(rebuilt, household.Id, NeedReason.CannotExpandProduction, recipe.Outputs[0].ItemId, shortage);
    }

    /// <summary>
    /// 理由4: 工具切れ(GDD02a §3.1)。「生産が止まったか」は見ない ── 工具が無くても
    /// 半分の能力で続く職業が4つある(GDD02a §3)。それでも立てるのは、GDD02b §8 の発生源が
    /// 「設備の摩耗」だからである。
    /// </summary>
    private void CollectToolsExhausted(HouseholdState household, List<Need> rebuilt)
    {
        if (household.WorkshopInventory[Item.Tools] != 0)
        {
            return;
        }

        Add(
            rebuilt, household.Id, NeedReason.ToolsExhausted, Item.Tools,
            ProductionSystem.EquipmentThresholdStock);
    }

    /// <summary>
    /// 理由5: 遠方在庫(GDD06 §3.1)。前日の順5段5bが書いた
    /// <see cref="HouseholdState.UnfilledPurchase"/> をそのまま映す。
    /// </summary>
    private void CollectDistantStock(HouseholdState household, List<Need> rebuilt)
    {
        for (int itemId = 0; itemId < _definition.ItemCount; itemId++)
        {
            int unfilled = household.UnfilledPurchase[itemId];

            if (unfilled <= 0)
            {
                continue;
            }

            Add(rebuilt, household.Id, NeedReason.DistantStock, itemId, unfilled);
        }
    }

    private static void Add(List<Need> rebuilt, int householdId, NeedReason reason, int itemId, int quantity)
    {
        rebuilt.Add(new Need
        {
            Id = 0, // 段3(ResolveId)が上書きするまでの仮値。
            TypeCode = Need.TypeOf(reason),
            TargetHouseholdId = householdId,
            ItemId = itemId,
            Quantity = quantity,
            Deadline = Tick.Zero,
            Urgency = 0,
            ReasonCode = reason,
        });
    }
}
