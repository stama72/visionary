using Visionary.Sim.Numerics;

namespace Visionary.Sim.Systems;

/// <summary><see cref="PlayerSale.Execute"/> の結果の種別(GDD06 §6.1)。</summary>
public enum PlayerSaleOutcome
{
    /// <summary>成立。</summary>
    Accepted = 1,

    /// <summary>開示される Need が (世帯, 品目) に無い。1日1回に数えない。</summary>
    NoNeed = 2,

    /// <summary>同じ (世帯, 品目) に今日すでに値を出した。</summary>
    AlreadyOfferedToday = 3,

    /// <summary>価格 &gt; 予算。1日1回に数える。</summary>
    OverBudget = 4,

    // 「価格 ≤ 予算だが資金で 0 個」は値を持たない。起きないからである(Execute の手順7。GDD06 §6.1)。
}

/// <summary><see cref="PlayerSale.Execute"/> の結果。</summary>
public readonly record struct PlayerSaleResult
{
    public PlayerSaleOutcome Outcome { get; init; }

    /// <summary>当てた Need の Id。<see cref="PlayerSaleOutcome.NoNeed"/> のときだけ −1。</summary>
    public int NeedId { get; init; }

    /// <summary>当てた用途。<see cref="PlayerSaleOutcome.NoNeed"/> のときは意味を持たない(既定値)。</summary>
    public DemandPurpose Purpose { get; init; }

    /// <summary>約定した個数。単位: 個。Accepted 以外は 0。</summary>
    public int Quantity { get; init; }

    /// <summary>プレイヤーの値(引数そのまま)。単位: 貨幣/個。</summary>
    public int UnitPrice { get; init; }

    /// <summary>Quantity × UnitPrice。単位: 貨幣。Accepted 以外は 0。</summary>
    public int Payment { get; init; }

    /// <summary>Accepted のときだけ値を持つ。</summary>
    public Gratitude? Gratitude { get; init; }
}

/// <summary>
/// プレイヤーが Need を持つ NPC 世帯へ売る(GDD06 §6.1)と、感謝の段階(GDD01 §3.6)。
/// <b>世帯の側だけを動かす。</b>プレイヤーの所持金・所持品・居場所と時間帯の検査は #263 のコマンドが
/// <see cref="Execute"/> の前後で行う ── <see cref="Execute"/> だけを呼ぶと貨幣が消える。
/// </summary>
public sealed class PlayerSale
{
    /// <summary>単位: ‰。150 = 15%。これ以上で「感謝」(GDD01 §3.6。初期値)。</summary>
    public const int GratefulThresholdPermille = 150;

    /// <summary>単位: ‰。400 = 40%。これ以上で「深い感謝」(GDD01 §3.6。初期値)。</summary>
    public const int DeepGratitudeThresholdPermille = 400;

    private readonly WorldDefinition _definition;
    private readonly BuyerDemand _buyerDemand;

    public PlayerSale(WorldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _definition = definition;
        _buyerDemand = new BuyerDemand(definition);
    }

    /// <summary>
    /// 評価額 = min( ApplyPermille(基礎値, 在庫圧力‰) , 利潤上限 )。利潤上限は
    /// <see cref="DemandLine.HasProfitCap"/> が真のときだけ min に入れる。
    /// <b>現金上限は入れない</b> ── 払える額の制約であって、その品への評価ではない(GDD01 §3.6)。
    /// </summary>
    public static int Evaluation(in DemandLine line)
    {
        int evaluation = IntegerMath.ApplyPermille(line.BaseValue, line.StockPressurePermille);

        return line.HasProfitCap ? Math.Min(evaluation, line.ProfitCap) : evaluation;
    }

    /// <summary>
    /// 余剰の比率‰ = CeilDiv( 1000 × (評価額 − 価格) , 評価額 )。積は long で取る。
    /// 成立した取引では 評価額 ≥ 予算 ≥ 価格 ≥ 1(GDD06 §6.1)。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 評価額 ≤ 0、価格 &lt; 1、または価格 &gt; 評価額。
    /// </exception>
    public static int SurplusPermille(int evaluation, int price)
    {
        if (evaluation <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(evaluation), evaluation, "評価額は1以上(GDD01 §3.6)。");
        }

        if (price < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(price), price, "価格は1以上(GDD06 §6.1)。");
        }

        if (price > evaluation)
        {
            throw new ArgumentOutOfRangeException(nameof(price), price, "価格は評価額以下(GDD06 §6.1)。");
        }

        long numerator = (long)IntegerMath.PermilleScale * (evaluation - price);

        return checked((int)IntegerMath.CeilDiv(numerator, evaluation));
    }

    /// <summary>
    /// s‰ &lt; 150 → 渋々 / 150 ≤ s‰ &lt; 400 → 感謝 / 400 ≤ s‰ → 深い感謝(境目は上側を含む)。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="surplusPermille"/> が負。</exception>
    public static Gratitude GratitudeOf(int surplusPermille)
    {
        if (surplusPermille < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(surplusPermille), surplusPermille, "余剰の比率‰は0以上(GDD01 §3.6)。");
        }

        if (surplusPermille < GratefulThresholdPermille)
        {
            return Gratitude.Reluctant;
        }

        return surplusPermille < DeepGratitudeThresholdPermille ? Gratitude.Grateful : Gratitude.DeepGratitude;
    }

    /// <summary>
    /// プレイヤーが (世帯, 品目) に値を出したときの購入規則(GDD06 §6.1)。手順の順序は仕様である。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 世帯 Id・品目 Id が範囲外、<paramref name="unitPrice"/> または <paramref name="offeredQuantity"/> が1未満。
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Need があるのに需要の行が無い、または価格 ≤ 予算なのに払える数が 0(いずれもモデルの欠陥)。
    /// </exception>
    public PlayerSaleResult Execute(World world, int householdId, int itemId, int unitPrice, int offeredQuantity)
    {
        ArgumentNullException.ThrowIfNull(world);

        // 1. 引数の検査。Result にしない ── 呼び出し側(#263)が先に検証する入力である。
        if (householdId < 0 || householdId >= world.Households.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(householdId), householdId, "世帯 Id が範囲外。");
        }

        if (itemId < 0 || itemId >= _definition.ItemCount)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId), itemId, "品目 Id が範囲外。");
        }

        if (unitPrice < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), unitPrice, "プレイヤーの値は1以上(GDD06 §6.1)。");
        }

        if (offeredQuantity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(offeredQuantity), offeredQuantity, "提示数量は1以上。");
        }

        var household = world.Households[householdId];

        // 2. 当てる Need を選ぶ。優先順は 工具切れ → 生産停止 → 遠方在庫(world.Needs の並びに依存しない)。
        // 同じ (世帯, 理由, 品目) は1件しか立たないので、優先順だけで1件に決まる。
        bool found = false;
        Need chosen = default;
        int chosenRank = int.MaxValue;

        foreach (var need in world.Needs)
        {
            if (need.TargetHouseholdId != householdId || need.ItemId != itemId)
            {
                continue;
            }

            int rank = need.ReasonCode switch
            {
                NeedReason.ToolsExhausted => 0,
                NeedReason.ProductionStopped => 1,
                NeedReason.DistantStock => 2,
                _ => int.MaxValue, // 困窮・増産できないは開示されない(GDD01 §6.1)
            };

            if (rank < chosenRank)
            {
                chosenRank = rank;
                chosen = need;
                found = true;
            }
        }

        if (!found)
        {
            return new PlayerSaleResult
            {
                Outcome = PlayerSaleOutcome.NoNeed,
                NeedId = -1,
                UnitPrice = unitPrice,
            };
        }

        // 3. 1日1回。
        int today = checked((int)world.Now.DayIndex);

        if (household.PlayerOfferDayIndex[itemId] == today)
        {
            return new PlayerSaleResult
            {
                Outcome = PlayerSaleOutcome.AlreadyOfferedToday,
                NeedId = chosen.Id,
                UnitPrice = unitPrice,
            };
        }

        // 4. 記録する。ここより後のどの結果でも記録は残る。
        household.PlayerOfferDayIndex[itemId] = today;

        // 5. 予算を作り直し、行を選ぶ(朝の予算は控えない。夕方の段4 と同じ呼び方)。
        int outputItemId = _definition.Recipes[(int)household.Occupation].Outputs[0].ItemId;
        var demand = _buyerDemand.Build(
            world, household,
            household.MemoHasPreviousOffer[outputItemId] == 1,
            household.MemoPreviousOfferPrice[outputItemId]);

        var line = SelectLine(demand, chosen.ReasonCode, itemId);

        // 6. 価格のゲート。ちょうど等しければ通る。
        if (unitPrice > line.Budget)
        {
            return new PlayerSaleResult
            {
                Outcome = PlayerSaleOutcome.OverBudget,
                NeedId = chosen.Id,
                Purpose = line.Purpose,
                UnitPrice = unitPrice,
            };
        }

        // 7. 数量。資金の上限は流動資金ではなく用途に使える資金で切る。
        int quantity = Math.Min(
            Math.Min(offeredQuantity, chosen.Quantity),
            IntegerMath.FloorDiv(line.AvailableFunds, unitPrice));

        // 手順6 を通った後は必ず 1 以上: 価格 ≤ 予算 ≤ 現金上限 = FloorDiv(AvailableFunds, max(1日分, 1))
        // ≤ AvailableFunds。0 になるなら予算の式か AvailableFunds の設定が壊れている(正常系では発火しない番人)。
        if (quantity < 1)
        {
            throw new InvalidOperationException(
                $"価格 {unitPrice} ≤ 予算 {line.Budget} なのに払える数が {quantity}" +
                $"(用途に使える資金 {line.AvailableFunds})。予算の式か AvailableFunds が壊れている。");
        }

        // 10(先に計算). 段階は約定前の行から出す(値を出した時点の評価額。約定後に作り直さない)。
        var gratitude = GratitudeOf(SurplusPermille(Evaluation(line), unitPrice));

        // 8. 約定。
        TradeSettlement.ExecutePlayerSale(
            world, household, line.Purpose, itemId, quantity, unitPrice,
            _definition.AcquisitionCostSmoothingPermille);

        // 9. Need の差し引き。
        NeedDeduction.SubtractPurchase(world, householdId, line.Purpose, itemId, quantity);

        return new PlayerSaleResult
        {
            Outcome = PlayerSaleOutcome.Accepted,
            NeedId = chosen.Id,
            Purpose = line.Purpose,
            Quantity = quantity,
            UnitPrice = unitPrice,
            Payment = checked((int)((long)quantity * unitPrice)),
            Gratitude = gratitude,
        };
    }

    private static DemandLine SelectLine(HouseholdDemand demand, NeedReason reason, int itemId)
    {
        // demand.Lines の並びがそのまま走査順(必需 → 耐久 → 生産の入力 → 嗜好)。
        foreach (var line in demand.Lines)
        {
            bool matches = reason switch
            {
                NeedReason.ToolsExhausted => line.Purpose == DemandPurpose.Durable,
                NeedReason.ProductionStopped =>
                    line.Purpose == DemandPurpose.ProductionInput && line.ItemId == itemId,
                _ => line.ItemId == itemId, // 遠方在庫: 最初の行
            };

            if (matches)
            {
                return line;
            }
        }

        throw new InvalidOperationException(
            $"Need(理由={reason}、品目={itemId})があるのに需要の行が無い(モデルの欠陥)。");
    }
}
