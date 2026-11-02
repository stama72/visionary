using System.Globalization;
using Godot;
using Visionary.Presentation;
using Visionary.Sim;
using Visionary.Sim.Commands;
using Visionary.Sim.Dialogue;
using Visionary.Sim.Time;

namespace Visionary.Game;

/// <summary>
/// グレーボックスの入口(TDD01 §3.10)。時刻の真実は <see cref="SimHost"/> だけが持つ。
/// ここで <c>delta</c> を積んでゲーム時間を作らない ── 積むのは <see cref="TickPump"/> の端数だけ。
/// </summary>
public partial class Main : Node2D
{
    // 終了コード: 引数の誤り 2・煙試験の例外 1・煙試験の成功 0。
    private const int ExitArgumentError = 2;
    private const int ExitSmokeFailure = 1;

    private readonly WorldDefinition _definition = WorldDefinition.M0;

    private SimHost _host = null!;
    private TickPump _pump = null!;
    private GameLoop? _loop;
    // 煙試験のときだけ使う。終わりは tick ではなくフレームの数で決める(余計な Advance を割り切れる回数で見逃さないため)。
    private int? _smokeTicks;         // 回すフレームの数(= SmokeTicks)
    private long _smokeStartTick;     // _Ready で作った直後の host.Now
    private int _smokeFrames;         // 回したフレームの数
    private bool _finished;
    private MapView _mapView = null!;
    private Label _label = null!;
    private PlayerSession _session = null!;
    private DistrictPanel _districtPanel = null!;
    private ConversationPanel _conversationPanel = null!;
    private WindowPanel _windowPanel = null!;
    private string _lastAction = "(なし)";

    public override void _Ready()
    {
        if (!LaunchOptions.TryParse(OS.GetCmdlineUserArgs(), out var options, out string error))
        {
            GD.PrintErr(error);
            GetTree().Quit(ExitArgumentError);
            return;
        }

        try
        {
            // 煙試験でも通常でも同じ組み立て。違うのは目標 tick を覚えるかどうかだけ。
            _host = new SimHost(_definition, options.Seed, options.StartDay);
            _pump = new TickPump();
            _loop = new GameLoop(_host, _pump);
            _session = new PlayerSession(_host, _definition, _pump, options.Seed, DialogueCorpus.M0);

            if (options.SmokeTicks is int smokeTicks)
            {
                _smokeTicks = smokeTicks;
                _smokeStartTick = _host.Now.Value;
            }

            _mapView = new MapView();
            _mapView.Setup(_host, _pump, _definition.TravelHoursPerDistrict);

            if (_smokeTicks != null)
            {
                _mapView.OnDrawError = FailSmoke;
            }

            AddChild(_mapView);

            var layer = new CanvasLayer();
            _label = new Label { Position = new Vector2(680, 80) };
            layer.AddChild(_label);

            _districtPanel = new DistrictPanel { Position = new Vector2(680, 220) };
            _districtPanel.Setup(_session, text => _lastAction = text);
            layer.AddChild(_districtPanel);

            _conversationPanel = new ConversationPanel();
            _conversationPanel.Setup(_session);
            layer.AddChild(_conversationPanel);

            _windowPanel = new WindowPanel();
            _windowPanel.Setup(_session);
            layer.AddChild(_windowPanel);

            AddChild(layer);

            RefreshLabel();
        }
        catch (Exception e) when (options.SmokeTicks != null)
        {
            FailSmoke(e);
        }
    }

    public override void _Process(double delta)
    {
        if (_loop == null || _finished)
        {
            return;
        }

        // 冒頭で見るのは、最後のフレームで QueueRedraw した _Draw を踏んでから出すため。
        if (_smokeTicks is int smokeTicks && _smokeFrames >= smokeTicks)
        {
            _finished = true;

            if (_mapView.DrawCount < smokeTicks)
            {
                GD.PrintErr($"_Draw の回数が足りない: {_mapView.DrawCount} < {smokeTicks}");
                GetTree().Quit(ExitSmokeFailure);
                return;
            }

            GD.Print("VISIONARY_SMOKE_HASH=" + _host.StateHash().ToString("X16", CultureInfo.InvariantCulture));
            GetTree().Quit(0);
            return;
        }

        try
        {
            // 経過を選ぶ1行。煙試験は 1フレーム = 1 tick。delta を積んでゲーム時間を作らない。
            long elapsedMicroseconds = _smokeTicks != null
                ? TickPump.DefaultMicrosecondsPerTick
                : (long)Math.Round(delta * 1_000_000);

            _loop.Frame(elapsedMicroseconds);

            if (_smokeTicks != null)
            {
                _smokeFrames++;

                long expected = _smokeStartTick + _smokeFrames;

                if (_host.Now.Value != expected)
                {
                    _finished = true;
                    GD.PrintErr($"Now がずれた: 期待 {expected}、実際 {_host.Now.Value}");
                    GetTree().Quit(ExitSmokeFailure);
                    return;
                }
            }

            RefreshLabel();
            _districtPanel.Refresh();
            _conversationPanel.Refresh();
            _windowPanel.Refresh();
            _mapView.QueueRedraw();
        }
        catch (Exception e) when (_smokeTicks != null)
        {
            FailSmoke(e);
        }
    }

    private void FailSmoke(Exception e)
    {
        _finished = true;
        GD.PrintErr(e.ToString());
        GetTree().Quit(ExitSmokeFailure);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_loop == null
            || @event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            return;
        }

        int districtId = MapLayout.DistrictAt(MapView.FromPixels(click.Position));

        if (districtId < 0)
        {
            return;
        }

        var result = _session.Move(districtId);

        // パネルが開いている間は移動しない。
        if (result == null)
        {
            return;
        }

        _lastAction = Labels.Of(result.Status);

        if (result.Status == CommandStatus.Accepted && result.ArrivalAt is Tick arrival)
        {
            _lastAction += $"(到着 {GameDate.FromTick(arrival).HourOfDay}時)";
        }
    }

    private void RefreshLabel()
    {
        var date = GameDate.FromTick(_host.Now);
        var player = _host.Query.Player();

        string place = player.Location.InTransit
            ? $"{player.Location.DistrictId} → {player.DestinationDistrictId}(到着 {GameDate.FromTick(player.ArrivalAt!.Value).HourOfDay}時)"
            : $"{player.Location.DistrictId}";

        _label.Text =
            $"{date.Year}年 {Labels.Of(date.Season)} {date.DayOfSeason}日 {date.HourOfDay}時\n"
            + $"所持金 {player.Funds}\n"
            + $"居場所 {place}\n"
            + $"所持品 {Notes.Inventory(player.Inventory)}\n"
            + $"最後の操作: {_lastAction}";
    }
}
