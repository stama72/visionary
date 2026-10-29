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

        if (options.SmokeTicks is int smokeTicks)
        {
            try
            {
                var smokeHost = new SimHost(_definition, options.Seed, options.StartDay);
                string hash = SmokeRun.Run(smokeHost, _definition.TravelHoursPerDistrict, smokeTicks);

                GD.Print("VISIONARY_SMOKE_HASH=" + hash);
                GetTree().Quit(0);
            }
            catch (Exception e)
            {
                GD.PrintErr(e.ToString());
                GetTree().Quit(ExitSmokeFailure);
            }

            return;
        }

        _host = new SimHost(_definition, options.Seed, options.StartDay);
        _pump = new TickPump();

        _mapView = new MapView();
        _mapView.Setup(_host, _pump, _definition.TravelHoursPerDistrict);
        AddChild(_mapView);

        var layer = new CanvasLayer();
        _label = new Label { Position = new Vector2(680, 80) };
        layer.AddChild(_label);
        AddChild(layer);

        RefreshLabel();
    }

    public override void _Process(double delta)
    {
        if (_host == null)
        {
            return;
        }

        int ticks = _pump.Elapse((long)Math.Round(delta * 1000));

        for (int i = 0; i < ticks; i++)
        {
            _host.Advance(1);
        }

        RefreshLabel();
        _mapView.QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_host == null
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
