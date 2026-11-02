using Visionary.Sim;
using Visionary.Sim.Commands;
using Visionary.Sim.Systems;

namespace Visionary.Presentation;

/// <summary>窓口の価格の1行。<paramref name="UnitPrice"/> 単位: 貨幣/個。<paramref name="Held"/> 単位: 個。</summary>
public readonly record struct WindowPrice(int ItemId, int UnitPrice, int Held);

/// <summary>開いている窓口(GDD05 §3.1、GDD06 §6.3)。<see cref="PlayerSession.OpenWindow"/> だけが作る。</summary>
public sealed class WindowCounter
{
    private readonly PlayerSession _session;
    private readonly WorldDefinition _definition;
    private readonly List<ConversationLine> _lines = [];

    internal WindowCounter(PlayerSession session, WorldDefinition definition)
    {
        _session = session;
        _definition = definition;
    }

    /// <summary>地の文だけ。追記のみ。</summary>
    public IReadOnlyList<ConversationLine> Lines => _lines;

    public IReadOnlyList<WindowPrice> BuyOptions()
    {
        var inventory = _session.Host.Query.Player().Inventory;
        var result = new List<WindowPrice>();

        for (int itemId = 0; itemId < _definition.ItemCount; itemId++)
        {
            if (ExternalMarket.TryOfferPrice(_definition, _session.Host.Now, itemId, out int price))
            {
                result.Add(new WindowPrice(itemId, price, inventory[itemId]));
            }
        }

        return result;
    }

    public IReadOnlyList<WindowPrice> SellOptions()
    {
        var inventory = _session.Host.Query.Player().Inventory;
        var result = new List<WindowPrice>();

        for (int itemId = 0; itemId < _definition.ItemCount; itemId++)
        {
            if (!_definition.IsPrimaryItem(itemId) && inventory[itemId] >= 1)
            {
                result.Add(new WindowPrice(itemId, _definition.ExternalBuyPrice(itemId), inventory[itemId]));
            }
        }

        return result;
    }

    public Result Buy(int itemId, int quantity)
    {
        _session.EnsureOpen(this);

        var result = _session.Host.Execute(new BuyFromWindowCommand(itemId, quantity));

        if (result.Status == CommandStatus.Accepted)
        {
            _session.RecordPurchase(itemId, result.UnitPrice);
        }

        _lines.Add(new ConversationLine(null, null, Notes.Trade(PlayerTradeKind.BuyFromWindow, itemId, result)));
        return result;
    }

    public Result Sell(int itemId, int quantity)
    {
        _session.EnsureOpen(this);

        var result = _session.Host.Execute(new SellToWindowCommand(itemId, quantity));

        _lines.Add(new ConversationLine(null, null, Notes.Trade(PlayerTradeKind.SellToWindow, itemId, result)));
        return result;
    }
}
