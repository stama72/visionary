using System.Globalization;
using Godot;
using Visionary.Presentation;
using Visionary.Sim;
using Visionary.Sim.Commands;
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
    private long? _smokeTargetTick;   // 煙試験のときだけ。目標の tick(host.Now + SmokeTicks)
    private bool _finished;
    private MapView _mapView = null!;
    private Label _label = null!;
    private string _lastMove = "(なし)";

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

            if (options.SmokeTicks is int smokeTicks)
            {
                _smokeTargetTick = _host.Now.Value + smokeTicks;
            }

            _mapView = new MapView();
            _mapView.Setup(_host, _pump, _definition.TravelHoursPerDistrict);

            if (_smokeTargetTick != null)
            {
                _mapView.OnDrawError = FailSmoke;
            }

            AddChild(_mapView);

            var layer = new CanvasLayer();
            _label = new Label { Position = new Vector2(680, 80) };
            layer.AddChild(_label);
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

        // 冒頭で見るのは、目標に達したフレームで QueueRedraw した _Draw を踏んでから出すため。
        if (_smokeTargetTick is long target && _host.Now.Value >= target)
        {
            _finished = true;
            GD.Print("VISIONARY_SMOKE_HASH=" + _host.StateHash().ToString("X16", CultureInfo.InvariantCulture));
            GetTree().Quit(0);
            return;
        }

        try
        {
            // 経過を選ぶ1行。煙試験は 1フレーム = 1 tick。delta を積んでゲーム時間を作らない。
            long elapsedMicroseconds = _smokeTargetTick != null
                ? TickPump.DefaultMicrosecondsPerTick
                : (long)Math.Round(delta * 1_000_000);

            _loop.Frame(elapsedMicroseconds);

            RefreshLabel();
            _mapView.QueueRedraw();
        }
        catch (Exception e) when (_smokeTargetTick != null)
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

        var result = _host.Execute(new MoveCommand(districtId));

        _lastMove = Labels.Of(result.Status);

        if (result.Status == CommandStatus.Accepted && result.ArrivalAt is Tick arrival)
        {
            _lastMove += $"(到着 {GameDate.FromTick(arrival).HourOfDay}時)";
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
            + $"移動: {_lastMove}";
    }
}
