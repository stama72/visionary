using Visionary.Sim.Time;

namespace Visionary.Sim.Systems;

/// <summary>
/// 自家消費(GDD02b §1.1)。自分の生産物を工房在庫から世帯在庫へ移す量。
/// </summary>
public static class SelfConsumption
{
    /// <summary>
    /// 目標日数 = 必需の日数 + 嗜好の日数(GDD02b §2)。単位: 日。
    /// </summary>
    /// <remarks>
    /// <b>必需と嗜好を足すのは、世帯在庫が1本の在庫だからである。</b><see cref="WorldDefinition"/>
    /// のコンストラクタが必需・嗜好の同時指定を <see cref="ArgumentException"/> で弾くため、
    /// 合成の <see cref="WorldDefinition"/> を使っても両項が正の入力は作れない ──
    /// <b>この加算は一度も発火しない</b>(2026-09-22 訂正。旧版は「M0には両方が正の品目が無いので
    /// 合成のテストだけで発火する」と書いていたが、合成でも作れないので誤りだった)。
    /// <see cref="SellableStock.ReserveQuantity"/> の入力の枝は<b>到達可能</b>
    /// (合成レシピが実際に踏む)なので、到達しない加算の先例として成立していない。それでも
    /// 和の形を採るのは、GDD02b §1.1「用途で分岐しない」を<b>分岐なしに書くため</b>である ──
    /// 用途は排他(GDD02b §2 の表)なので、和は常に「該当する側の日数」と一致する。
    /// </remarks>
    public static int TargetStockDays(WorldDefinition definition, int itemId)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return definition.NecessityTargetStockDays[itemId] + definition.PreferenceTargetStockDays[itemId];
    }

    /// <summary>
    /// 移動量 = min( 販売在庫 , max( 0 , 目標在庫 + 今日の消費量 − 世帯在庫 ) )。単位: 個。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>今日の消費量に <see cref="DailyConsumption.Lookahead"/> ではなく
    /// <see cref="DailyConsumption.Quantity"/> を使う。</b>呼び出し側(<see cref="ConsumptionSystem"/>)が
    /// 1日1回だけ解いた <paramref name="season"/> を渡すことで、移す量と同じ日に実際に引かれる量が
    /// 同一の値であることを引数で保証する。<c>Lookahead</c> は <c>world.Now</c> から季節を解き直すので、
    /// 値は同じでも保証が消える(GDD02b §1.1)。
    /// </para>
    /// <para>
    /// <b>目標日数が0の品目を早期 return で弾かない。</b>目標日数0でも今日の消費量が正なら、その分
    /// だけ移る(目標在庫0 + 今日の消費量1 − 世帯在庫0 = 1)。<b>M0 で小麦粉と工具が移らないのは、
    /// 消費表の行も0だからである</b>(式が一般に0を返すからではない)。
    /// </para>
    /// <para>
    /// <b><see cref="SellableStock.Of"/> を通す。</b><c>household.WorkshopInventory[itemId]</c> を
    /// 直読みしない(GDD02c §1.3「自家消費も販売在庫から取る」)。M0 の対象3品目は留保量が0なので
    /// 値は変わらないが、留保が値を持つ品目が現れた日に黙って外れるのを防ぐために通す。
    /// <b>この保証には穴がある</b>: 呼び出し側が工房在庫を直読みする経路は型では防げない
    /// (<see cref="SellableStock"/> の doc コメントが既に書いている穴と同じもの)。
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <b>核心の変異の実測(実測日 2026-09-22、<c>mutator</c> が使い捨てworktreeで1件ずつ当て、
    /// 毎回 <c>dotnet test Visionary.sln -c Release</c>(477件)を走らせて測定。対象コミット
    /// <c>13144cf</c>)。期待と食い違った件数は0件。</b>
    /// <list type="bullet">
    /// <item><b>M-1</b>(<c>TransferQuantity</c> から <c>+ 今日の消費量</c> の項を落とす)は
    /// <b>赤</b>。<c>SelfConsumptionTests</c> 3件・
    /// <c>TradePipelineTests.UnaffordableNecessityCountsOnlyTheFundsShortfall</c>・
    /// <c>ProductionAndConsumptionPipelineTests.ProductionAndConsumptionRunInPipelineOrder</c>に
    /// 加え、<c>TradePipelineTests.OwnOutputIsNeverHoardedWhileTheHouseholdGoesWithout</c>の
    /// 全5シードが【核心】(<c>HoardedWhileEmptyEntries.Count == 0</c>)で落ちた(計10件)。</item>
    /// <item><b>M-2</b>(上限を <c>SellableStock.Of</c> から <c>household.WorkshopInventory</c> へ
    /// 置換)は<b>赤</b>。<c>TransferNeverExceedsTheSellableStock</c>(Expected 5, Actual 0)。</item>
    /// <item><b>M-3</b>(<c>max(0, …)</c> から <c>− 世帯在庫</c> の項を落とす)は<b>赤</b>。
    /// <c>SelfConsumptionTests</c> 3件・
    /// <c>TradePipelineTests.UnaffordableNecessityCountsOnlyTheFundsShortfall</c>(計4件)。</item>
    /// <item><b>M-5</b>(式の世帯在庫を工房在庫に取り違える)は<b>赤</b>。
    /// <c>TradePipelineTests.OwnOutputIsNeverHoardedWhileTheHouseholdGoesWithout</c>の全5シードが
    /// 【核心】で落ちたことに加え、
    /// <c>SomeHouseholdAlwaysHoldsNecessitiesOverThirtyDays(seed:7)</c>他5件(計11件)。</item>
    /// </list>
    /// </remarks>
    public static int TransferQuantity(
        WorldDefinition definition, World world, HouseholdState household, int itemId, Season season)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(household);

        int targetStock = DailyConsumption.Lookahead(
            definition, world, household, itemId, world.Now, TargetStockDays(definition, itemId));
        int todaysConsumption = DailyConsumption.Quantity(definition, world, household, itemId, season);
        int householdStock = household.HouseholdInventory[itemId];
        int sellableStock = SellableStock.Of(definition, household, itemId);

        int required = Math.Max(0, targetStock + todaysConsumption - householdStock);

        return Math.Min(sellableStock, required);
    }
}
