using Godot;
using Visionary.Presentation;

namespace Visionary.Game;

/// <summary>日誌のパネル(GDD05 §2.1)。行の組み立ては <see cref="PlayerSession.Journal"/> から読む。パネルが開いていても見せる。</summary>
public partial class JournalPanel : PanelContainer
{
    private const int PanelWidth = 580;       // px
    private const int PanelHeight = 140;      // px
    private const int LogHeight = 100;        // px

    private PlayerSession _session = null!;
    private int _shownCount = -1;
    private VBoxContainer? _content;

    public void Setup(PlayerSession session)
    {
        _session = session;
        Position = new Vector2(680, 560);
        CustomMinimumSize = new Vector2(PanelWidth, PanelHeight);
    }

    /// <summary><c>Journal.Count</c> が前回と違うときだけ作り直す。</summary>
    public void Refresh()
    {
        var journal = _session.Journal;

        if (journal.Count == _shownCount)
        {
            return;
        }

        _shownCount = journal.Count;

        if (_content != null)
        {
            RemoveChild(_content);
            _content.QueueFree();
        }

        _content = new VBoxContainer();
        AddChild(_content);
        _content.AddChild(new Label { Text = "日誌" });

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(PanelWidth - 20, LogHeight) };
        var log = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };

        foreach (var entry in journal)
        {
            log.AddChild(new Label
            {
                Text = entry.Text,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(PanelWidth - 40, 0),
            });
        }

        scroll.AddChild(log);
        _content.AddChild(scroll);
        Callable.From(() => scroll.ScrollVertical = 100000).CallDeferred();
    }
}
