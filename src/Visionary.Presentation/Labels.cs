using Visionary.Sim;
using Visionary.Sim.Commands;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Presentation;

/// <summary>
/// 表示名。知らない値は例外にする ── 空文字や <c>ToString()</c> に落とすと、
/// 列挙に値を足したときに画面だけが黙って壊れる。
/// </summary>
public static class Labels
{
    public static string Of(Occupation occupation) => occupation switch
    {
        Occupation.Miller => "水車小屋番",
        Occupation.Baker => "パン屋",
        Occupation.Brewer => "醸造",
        Occupation.Woodworker => "木材加工",
        Occupation.Smith => "鍛冶",
        _ => throw new ArgumentOutOfRangeException(nameof(occupation), occupation, "名前のない職業。"),
    };

    public static string Of(Season season) => season switch
    {
        Season.Spring => "春",
        Season.Summer => "夏",
        Season.Autumn => "秋",
        Season.Winter => "冬",
        _ => throw new ArgumentOutOfRangeException(nameof(season), season, "名前のない季節。"),
    };

    public static string Of(TimetableActivity activity) => activity switch
    {
        TimetableActivity.Resting => "休む",
        TimetableActivity.Working => "家業",
        TimetableActivity.Traveling => "移動中",
        TimetableActivity.AtDestination => "外出先",
        _ => throw new ArgumentOutOfRangeException(nameof(activity), activity, "名前のない活動。"),
    };

    public static string Of(CommandStatus status) => status switch
    {
        CommandStatus.Accepted => "受け付けた",
        CommandStatus.InvalidArgument => "引数が不正",
        CommandStatus.InTransit => "移動中",
        CommandStatus.OutsideTradingHours => "取引時間外",
        CommandStatus.NotPresent => "その場にいない",
        CommandStatus.AlreadyThere => "もうここにいる",
        CommandStatus.NoOffer => "売り注文がない",
        CommandStatus.SellerOutOfStock => "売り手の在庫切れ",
        CommandStatus.InsufficientFunds => "所持金が足りない",
        CommandStatus.NotEnoughGoods => "所持品が足りない",
        CommandStatus.WindowDoesNotBuy => "窓口は買わない",
        CommandStatus.NoNeed => "困りごとがない",
        CommandStatus.AlreadyOfferedToday => "今日はもう持ちかけた",
        CommandStatus.OverBudget => "予算を超える",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "名前のない結果。"),
    };
}
