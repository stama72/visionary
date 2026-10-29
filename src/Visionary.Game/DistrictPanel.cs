using Godot;
using Visionary.Presentation;
using Visionary.Sim.Commands;

namespace Visionary.Game;

/// <summary>
/// 居る区画の入口の一覧(GDD05 §2.2)。並べ方と文字は <see cref="PlayerSession.Entries"/> から読むだけで、
/// ノードは描画と入力だけを持つ。パネルが開いている間は隠す。
/// </summary>
public partial class DistrictPanel : VBoxContainer
{
    private PlayerSession _session = null!;
    private Action<string> _report = null!;
    private IReadOnlyList<DistrictEntry>? _shown;

    public void Setup(PlayerSession session, Action<string> report)
    {
        _session = session;
        _report = report;
    }

    public void Refresh()
    {
        Visible = !_session.IsPanelOpen;

        var entries = _session.Entries();

        // 毎フレーム作り直すと、押下が届く前にボタンが消える。違うときだけ作り直す。
        if (_shown != null && _shown.SequenceEqual(entries))
        {
            return;
        }

        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        foreach (var entry in entries)
        {
            var button = new Button { Text = entry.Label };
            var captured = entry;

            button.Pressed += () => OnPressed(captured);
            AddChild(button);
        }

        _shown = entries;
    }

    private void OnPressed(DistrictEntry entry)
    {
        if (entry.Kind == DistrictEntryKind.Window)
        {
            _session.OpenWindow();
            _report("窓口を開いた");
            return;
        }

        var result = _session.Talk(entry.HouseholdId);

        if (result.Status != CommandStatus.Accepted)
        {
            _report(Labels.Of(result.Status));
        }
    }
}
