using Visionary.Sim.Dialogue;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Sim;

/// <summary>
/// 描画・台本が読むための読み出しモデル(TDD01 §3.5 の <c>Query</c>)。
/// <b>どのメソッドも <see cref="World"/> を書かず、呼ぶたびに新しい写しを作る</b> ── World の配列・
/// リストをそのまま包んで返すと、後の <c>Execute</c> で呼び出し側から見える値が変わる。
/// </summary>
public sealed class ReadModels
{
    private readonly WorldDefinition _definition;
    private readonly World _world;

    internal ReadModels(WorldDefinition definition, World world)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(world);

        _definition = definition;
        _world = world;
    }

    public Tick Now => _world.Now;

    /// <summary>売り注文。<see cref="MarketKey"/> 順(品目 Id 昇順 → 世帯 Id 昇順)。</summary>
    public IReadOnlyList<OfferView> Offers()
    {
        var result = new List<OfferView>(_world.Market.Count);

        foreach (var (key, price) in _world.Market)
        {
            var household = _world.Households[key.SellerId];

            result.Add(new OfferView(
                key.ItemId, key.SellerId, household.DistrictId, price,
                SellableStock.Of(_definition, household, key.ItemId)));
        }

        return result;
    }

    /// <summary>世帯 Id 昇順。</summary>
    public IReadOnlyList<HouseholdView> Households()
    {
        var result = new List<HouseholdView>(_world.Households.Length);

        foreach (var household in _world.Households)
        {
            // MetricsSystem と同じ取り方(出力1件は TradeSystem のコンストラクタが保証)。
            int outputItemId = _definition.Recipes[(int)household.Occupation].Outputs[0].ItemId;

            result.Add(new HouseholdView(
                household.Id, household.DistrictId, household.Occupation, outputItemId,
                (int[])household.MemberNpcIds.Clone()));
        }

        return result;
    }

    /// <summary>
    /// 取引で解ける Need。世帯 Id 昇順に <see cref="DisclosedNeeds.Of"/> を連結する(世帯内は Need Id 昇順)。
    /// 居合わせを見ない。
    /// </summary>
    public IReadOnlyList<Need> TradableNeeds()
    {
        var result = new List<Need>();

        for (int householdId = 0; householdId < _world.Households.Length; householdId++)
        {
            result.AddRange(DisclosedNeeds.Of(_world, householdId));
        }

        return result;
    }

    public TimetableEntry TimetableOf(int npcId) =>
        Timetable.Of(_definition, _world, npcId, _world.Now);

    public PlayerView Player()
    {
        var player = _world.Player;
        var location = player.LocationAt(_world.Now);

        // 移動中かは LocationAt で判定する。MoveDestinationDistrictId は到着後も残るので、
        // −1 との比較では到着済みを移動中と誤る。
        return new PlayerView(
            player.Funds,
            (int[])player.Inventory.Clone(),
            location,
            location.InTransit ? player.MoveDestinationDistrictId : -1,
            location.InTransit ? new Tick(player.MoveArrivalTick) : null);
    }
}

/// <param name="ItemId">品目 Id。</param>
/// <param name="HouseholdId">売り手の世帯 Id。</param>
/// <param name="DistrictId">売り手の区画。</param>
/// <param name="UnitPrice">提示価格。単位: 貨幣/個。</param>
/// <param name="SellableStock">販売在庫。単位: 個(工房在庫 − 留保量)。</param>
public readonly record struct OfferView(
    int ItemId, int HouseholdId, int DistrictId, int UnitPrice, int SellableStock);

public sealed record HouseholdView(
    int Id, int DistrictId, Occupation Occupation, int OutputItemId, IReadOnlyList<int> MemberNpcIds);

/// <param name="Funds">所持金。単位: 貨幣。</param>
/// <param name="Inventory">所持品。単位: 個。添字 = 品目 Id。</param>
/// <param name="Location">居場所。移動中なら出発した区画。</param>
/// <param name="DestinationDistrictId">移動中の行き先。移動中でなければ −1。</param>
/// <param name="ArrivalAt">到着 tick。移動中でなければ null。</param>
public sealed record PlayerView(
    int Funds, IReadOnlyList<int> Inventory, PlayerLocation Location, int DestinationDistrictId, Tick? ArrivalAt);
