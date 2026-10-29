using Godot;
using Visionary.Presentation;

namespace Visionary.Game;

/// <summary>窓口のパネル(GDD05 §3.1、GDD06 §6.3)。中身は <see cref="WindowCounter"/> から読む。</summary>
public partial class WindowPanel : PanelContainer
{
    // 買う数量(単位: 個)の SpinBox の上限。画面の側で切らず、切るのはシム。
    private const double MaxSpin = 1_000_000;
    private const int PanelWidth = 560;       // px
    private const int LogHeight = 100;        // px

    private PlayerSession _session = null!;
    private WindowCounter? _shown;
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
        var window = _session.Window;

        Visible = window != null;

        if (window == null)
        {
            _shown = null;
            return;
        }

        if (!ReferenceEquals(window, _shown))
        {
            _shown = window;
            Rebuild(window);
        }
    }

    private void Rebuild(WindowCounter window)
    {
        if (_content != null)
        {
            RemoveChild(_content);
            _content.QueueFree();
        }

        _content = new VBoxContainer();
        AddChild(_content);

        _content.AddChild(new Label { Text = "窓口" });

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(PanelWidth - 20, LogHeight) };
        var log = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };

        foreach (var line in window.Lines)
        {
            log.AddChild(new Label
            {
                Text = $"― {line.Text}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(PanelWidth - 40, 0),
            });
        }

        scroll.AddChild(log);
        _content.AddChild(scroll);
        Callable.From(() => scroll.ScrollVertical = 100000).CallDeferred();

        _content.AddChild(new Label { Text = "買う" });

        foreach (var option in window.BuyOptions())
        {
            var row = new HBoxContainer();
            var quantity = NewSpin(1, MaxSpin, 1);
            var button = new Button { Text = "買う" };
            int itemId = option.ItemId;

            button.Pressed += () =>
            {
                window.Buy(itemId, (int)quantity.Value);
                Rebuild(window);
            };

            row.AddChild(new Label { Text = $"{Labels.ItemName(itemId)} 単価 {option.UnitPrice}(所持 {option.Held})" });
            row.AddChild(quantity);
            row.AddChild(button);
            _content.AddChild(row);
        }

        _content.AddChild(new Label { Text = "売る" });

        foreach (var option in window.SellOptions())
        {
            var row = new HBoxContainer();
            var quantity = NewSpin(1, option.Held, option.Held);
            var button = new Button { Text = "売る" };
            int itemId = option.ItemId;

            button.Pressed += () =>
            {
                window.Sell(itemId, (int)quantity.Value);
                Rebuild(window);
            };

            row.AddChild(new Label { Text = $"{Labels.ItemName(itemId)} 単価 {option.UnitPrice}(所持 {option.Held})" });
            row.AddChild(quantity);
            row.AddChild(button);
            _content.AddChild(row);
        }

        var close = new Button { Text = "閉じる" };

        close.Pressed += () => _session.Close();
        _content.AddChild(close);
    }

    /// <summary>整数だけの SpinBox。値は呼び出し側が <c>(int)Value</c> で読む。</summary>
    private static SpinBox NewSpin(double min, double max, double value) =>
        new() { MinValue = min, MaxValue = max, Step = 1, Rounded = true, Value = value };
}
