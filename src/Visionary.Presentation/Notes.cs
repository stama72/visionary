using System.Globalization;
using Visionary.Sim.Commands;

namespace Visionary.Presentation;

/// <summary>画面に出す地の文(GDD05 §3.1)。段階(感謝)の名は出さない(GDD05 §2.1)。</summary>
public static class Notes
{
    /// <summary>所持品のうち 1 個以上の品目を Id 昇順に「{名} {数}」で「、」区切り。1つも無ければ「なし」。</summary>
    public static string Inventory(IReadOnlyList<int> inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        var parts = new List<string>();

        for (int itemId = 0; itemId < inventory.Count; itemId++)
        {
            if (inventory[itemId] >= 1)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{Labels.ItemName(itemId)} {inventory[itemId]}"));
            }
        }

        return parts.Count == 0 ? "なし" : string.Join("、", parts);
    }

    /// <summary>
    /// 売買の結果の地の文。<c>result.Gratitude</c> は読まない。拒否は品目を書かない
    /// (<c>InvalidArgument</c> では品目 Id が範囲外でありうる)。
    /// </summary>
    public static string Trade(PlayerTradeKind kind, int itemId, Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        string verb = kind switch
        {
            PlayerTradeKind.SellToHousehold or PlayerTradeKind.SellToWindow => "売った",
            PlayerTradeKind.BuyFromHousehold or PlayerTradeKind.BuyFromWindow => "買った",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "知らない売買の向き。"),
        };

        if (result.Status != CommandStatus.Accepted)
        {
            return Labels.Of(result.Status);
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Labels.ItemName(itemId)} × {result.Quantity} を単価 {result.UnitPrice} で{verb}(計 {result.Payment})");
    }
}

/// <summary>売買の向き。0 を使わない。</summary>
public enum PlayerTradeKind
{
    SellToHousehold = 1,
    BuyFromHousehold = 2,
    SellToWindow = 3,
    BuyFromWindow = 4,
}
