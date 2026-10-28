namespace Visionary.Sim.Commands;

/// <summary>プレイヤーのコマンド(TDD01 §3.4)。マーカー。<see cref="SimHost"/> が型で振り分ける。</summary>
public interface ICommand
{
}

/// <summary>別の区画へ移動する。時間は進めない(進めるのは <see cref="SimHost.Advance"/> だけ)。</summary>
public sealed record MoveCommand(int DestinationDistrictId) : ICommand;

/// <summary>世帯に困りごと(Need)を聞く。World を書かない。</summary>
public sealed record AskNeedsCommand(int HouseholdId) : ICommand;

/// <summary>世帯へ売る(GDD06 §6.1)。<paramref name="UnitPrice"/> 単位: 貨幣/個。</summary>
public sealed record SellToHouseholdCommand(int HouseholdId, int ItemId, int UnitPrice, int Quantity) : ICommand;

/// <summary>世帯から買う(GDD06 §6.2)。価格は世帯の提示価格。</summary>
public sealed record BuyFromHouseholdCommand(int HouseholdId, int ItemId, int Quantity) : ICommand;

/// <summary>窓口(都市外市場)から買う(GDD06 §6.3)。</summary>
public sealed record BuyFromWindowCommand(int ItemId, int Quantity) : ICommand;

/// <summary>窓口(都市外市場)へ売る(GDD06 §6.3)。</summary>
public sealed record SellToWindowCommand(int ItemId, int Quantity) : ICommand;
