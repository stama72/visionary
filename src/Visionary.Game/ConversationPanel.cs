using Godot;
using Visionary.Presentation;

namespace Visionary.Game;

/// <summary>
/// 会話のパネル(GDD05 §2.2・§3.1)。中身の並べ方・既定値・行の組み立ては <see cref="Conversation"/> から読む。
/// 数量の上限で画面の側から切らない(在庫・所持金・Need で切るのはシム)。
/// </summary>
public partial class ConversationPanel : PanelContainer
{
    // 単価(単位: 貨幣/個)と買う数量(単位: 個)の SpinBox の上限。画面の側で切らず、切るのはシム。
    private const double MaxSpin = 1_000_000;
    private const int PanelWidth = 560;       // px
    private const int LogHeight = 160;        // px

    private PlayerSession _session = null!;
    private Conversation? _shown;
    private VBoxContainer? _content;

    public void Setup(PlayerSession session)
    {
        _session = session;
        Position = new Vector2(680, 220);
        CustomMinimumSize = new Vector2(PanelWidth, 0);
        Visible = false;
    }

    public void Refresh()
    {
        var conversation = _session.Conversation;

        Visible = conversation != null;

        if (conversation == null)
        {
            _shown = null;
            return;
        }

        if (!ReferenceEquals(conversation, _shown))
        {
            _shown = conversation;
            Rebuild(conversation);
        }
    }

    private void Rebuild(Conversation conversation)
    {
        if (_content != null)
        {
            RemoveChild(_content);
            _content.QueueFree();
        }

        _content = new VBoxContainer();
        AddChild(_content);

        _content.AddChild(new Label { Text = conversation.Title });

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(PanelWidth - 20, LogHeight) };
        var log = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };

        foreach (var line in conversation.Lines)
        {
            log.AddChild(new Label
            {
                Text = line.Kind != null ? $"「{line.Text}」" : $"― {line.Text}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(PanelWidth - 40, 0),
            });
        }

        scroll.AddChild(log);
        _content.AddChild(scroll);
        Callable.From(() => scroll.ScrollVertical = 100000).CallDeferred();

        _content.AddChild(new Label { Text = "売る" });

        foreach (var option in conversation.SellOptions())
        {
            var row = new HBoxContainer();
            var price = NewSpin(1, MaxSpin, option.DefaultUnitPrice);
            var quantity = NewSpin(1, option.Held, option.Held);
            var button = new Button { Text = "売る" };
            int itemId = option.ItemId;

            button.Pressed += () =>
            {
                conversation.Sell(itemId, (int)price.Value, (int)quantity.Value);
                Rebuild(conversation);
            };

            row.AddChild(new Label { Text = $"{Labels.ItemName(itemId)}(所持 {option.Held})" });
            row.AddChild(price);
            row.AddChild(quantity);
            row.AddChild(button);
            _content.AddChild(row);
        }

        _content.AddChild(new Label { Text = "買う" });

        foreach (var offer in conversation.BuyOptions())
        {
            var row = new HBoxContainer();
            var quantity = NewSpin(1, MaxSpin, 1);
            var button = new Button { Text = "買う" };
            int itemId = offer.ItemId;

            button.Pressed += () =>
            {
                conversation.Buy(itemId, (int)quantity.Value);
                Rebuild(conversation);
            };

            row.AddChild(new Label { Text = $"{Labels.ItemName(itemId)} 単価 {offer.UnitPrice}(在庫 {offer.SellableStock})" });
            row.AddChild(quantity);
            row.AddChild(button);
            _content.AddChild(row);
        }

        var leave = new Button { Text = "立ち去る" };

        leave.Pressed += () => _session.Close();
        _content.AddChild(leave);
    }

    /// <summary>整数だけの SpinBox。値は呼び出し側が <c>(int)Value</c> で読む。</summary>
    private static SpinBox NewSpin(double min, double max, double value) =>
        new() { MinValue = min, MaxValue = max, Step = 1, Rounded = true, Value = value };
}
