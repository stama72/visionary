using Visionary.Sim.Time;

namespace Visionary.Sim;

/// <summary><see cref="PlayerState.LocationAt"/> の結果。</summary>
/// <param name="InTransit">移動中か。</param>
/// <param name="DistrictId">居る区画。移動中なら出発した区画。</param>
public readonly record struct PlayerLocation(bool InTransit, int DistrictId);

/// <summary>
/// プレイヤーの状態(TDD01 §3.2 / GDD02 §2.4)。<see cref="World.Player"/> の中身。
/// </summary>
/// <remarks>
/// <b>状態を到着時に書き換えない。</b>到着を処理するシステムを作らない(順1〜9 は
/// <see cref="World.Player"/> を読まない。TDD01 §3.2)。到着済みの状態は、次の移動コマンドが
/// 正規化する(<see cref="DistrictId"/> を行き先で上書きしてから新しい移動を書く)。
/// </remarks>
public sealed class PlayerState
{
    /// <summary>所持金。単位: 貨幣。</summary>
    public int Funds { get; internal set; }

    /// <summary>所持品。単位: 個。添字 = 品目 Id。</summary>
    public int[] Inventory { get; }

    /// <summary>最後に居た区画。移動中なら出発した区画。</summary>
    public int DistrictId { get; internal set; }

    /// <summary>移動中の行き先の区画。<b>移動していなければ −1</b>。</summary>
    public int MoveDestinationDistrictId { get; internal set; }

    /// <summary>到着 tick の値(<see cref="Tick.Value"/>)。移動していなければ 0。</summary>
    public long MoveArrivalTick { get; internal set; }

    public PlayerState(int itemCount)
    {
        if (itemCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(itemCount), itemCount, "品目数は非負(GDD02 §2.2)。");
        }

        Funds = 0;
        Inventory = new int[itemCount];
        DistrictId = District.ExternalMarketDistrictId;
        MoveDestinationDistrictId = -1;
        MoveArrivalTick = 0;
    }

    /// <summary>
    /// <paramref name="now"/> でのプレイヤーの居場所。純関数で、状態を書かない。
    /// 到着 tick ちょうどで到着済みになる(<c>&gt;=</c>)。
    /// </summary>
    public PlayerLocation LocationAt(Tick now)
    {
        if (MoveDestinationDistrictId == -1)
        {
            return new PlayerLocation(false, DistrictId);
        }

        if (now.Value >= MoveArrivalTick)
        {
            return new PlayerLocation(false, MoveDestinationDistrictId);
        }

        return new PlayerLocation(true, DistrictId);
    }
}
