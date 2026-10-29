using System.Collections.Generic;
using Godot;
using Visionary.Presentation;
using Visionary.Sim;
using Visionary.Sim.Systems;

namespace Visionary.Game;

/// <summary>
/// 地図の描画。ノードは描画だけを持ち、位置の計算は <see cref="MapLayout"/>(Presentation)に任せる。
/// </summary>
public partial class MapView : Node2D
{
    // 地図単位 → 画素: 画素 = 原点 + 地図単位 × 1/5(区画 = 1,000 地図単位 = 200 px)。
    /// <summary>地図単位 / 画素。単位: 地図単位 per px。</summary>
    public const int UnitsPerPixel = 5;

    /// <summary>地図の原点。単位: px。</summary>
    public static readonly Vector2 Origin = new(40, 80);

    private const int DistrictPixels = MapLayout.DistrictSize / UnitsPerPixel;   // px / 区画
    private const int DistrictGapPixels = 4;                                     // px(区画どうしの隙間)
    private const float NpcRadiusPixels = 8;                                     // px
    private const float PlayerRadiusPixels = 12;                                 // px
    private const int FontSize = 14;                                             // pt

    private static readonly Color DistrictFill = new(0.16f, 0.17f, 0.2f);
    private static readonly Color DistrictBorder = new(0.45f, 0.47f, 0.52f);
    private static readonly Color DestinationBorder = new(1f, 0.85f, 0.2f);
    private static readonly Color TextColor = new(0.9f, 0.9f, 0.9f);
    private static readonly Color PlayerColor = new(1f, 1f, 1f);

    private SimHost _host = null!;
    private TickPump _pump = null!;
    private int _travelHoursPerDistrict;

    public void Setup(SimHost host, TickPump pump, int travelHoursPerDistrict)
    {
        _host = host;
        _pump = pump;
        _travelHoursPerDistrict = travelHoursPerDistrict;
    }

    /// <summary>地図単位 → 画面の画素。</summary>
    public static Vector2 ToPixels(MapPoint point) =>
        Origin + new Vector2(point.X / (float)UnitsPerPixel, point.Y / (float)UnitsPerPixel);

    /// <summary>画面の画素 → 地図単位(<see cref="ToPixels"/> の逆)。</summary>
    public static MapPoint FromPixels(Vector2 pixels) => new(
        (int)Math.Floor((pixels.X - Origin.X) * UnitsPerPixel),
        (int)Math.Floor((pixels.Y - Origin.Y) * UnitsPerPixel));

    private static Color ActivityColor(TimetableActivity activity) => activity switch
    {
        TimetableActivity.Resting => new Color(0.45f, 0.55f, 0.95f),
        TimetableActivity.Working => new Color(0.95f, 0.6f, 0.2f),
        TimetableActivity.Traveling => new Color(0.4f, 0.9f, 0.5f),
        TimetableActivity.AtDestination => new Color(0.9f, 0.4f, 0.8f),
        _ => throw new ArgumentOutOfRangeException(nameof(activity), activity, "色のない活動。"),
    };

    /// <summary>
    /// 設定すると、<c>_Draw</c> の例外を握ってここへ渡す(煙試験が終了コード 1 にするため)。
    /// 未設定なら例外はそのまま Godot に渡る。
    /// </summary>
    public Action<Exception>? OnDrawError { get; set; }

    /// <summary>
    /// <c>_Draw</c> が呼ばれた回数。煙試験が、ヘッドレスでも描画が実際に走ったことを確かめるのに使う。
    /// </summary>
    public int DrawCount { get; private set; }

    public override void _Draw()
    {
        DrawCount++;

        if (OnDrawError == null)
        {
            DrawMap();
            return;
        }

        try
        {
            DrawMap();
        }
        catch (Exception e)
        {
            OnDrawError(e);
        }
    }

    private void DrawMap()
    {
        if (_host == null)
        {
            return;
        }

        var households = _host.Query.Households();
        var player = _host.Query.Player();
        var font = ThemeDB.FallbackFont;

        var namesByDistrict = new Dictionary<int, List<string>>();
        foreach (var household in households)
        {
            if (!namesByDistrict.TryGetValue(household.DistrictId, out var list))
            {
                list = new List<string>();
                namesByDistrict[household.DistrictId] = list;
            }

            list.Add(Labels.Of(household.Occupation));
        }

        for (int id = 0; id < District.Count; id++)
        {
            var topLeft = ToPixels(new MapPoint(
                District.ColumnOf(id) * MapLayout.DistrictSize, District.RowOf(id) * MapLayout.DistrictSize));
            var rect = new Rect2(
                topLeft + new Vector2(DistrictGapPixels / 2f, DistrictGapPixels / 2f),
                new Vector2(DistrictPixels - DistrictGapPixels, DistrictPixels - DistrictGapPixels));
            bool isDestination = player.Location.InTransit && player.DestinationDistrictId == id;

            DrawRect(rect, DistrictFill, true);
            DrawRect(rect, isDestination ? DestinationBorder : DistrictBorder, false, isDestination ? 4 : 2);

            var textPos = rect.Position + new Vector2(8, 18);
            DrawString(font, textPos, $"区画 {id}", HorizontalAlignment.Left, -1, FontSize, TextColor);

            if (id == District.ExternalMarketDistrictId)
            {
                textPos += new Vector2(0, 18);
                DrawString(font, textPos, "窓口", HorizontalAlignment.Left, -1, FontSize, TextColor);
            }

            if (namesByDistrict.TryGetValue(id, out var names))
            {
                foreach (string name in names)
                {
                    textPos += new Vector2(0, 18);
                    DrawString(font, textPos, name, HorizontalAlignment.Left, -1, FontSize, TextColor);
                }
            }
        }

        var npcIds = new List<int>();
        var entries = new List<TimetableEntry>();
        foreach (var household in households)
        {
            foreach (int npcId in household.MemberNpcIds)
            {
                npcIds.Add(npcId);
                entries.Add(_host.Query.TimetableOf(npcId));
            }
        }

        int[] slots = MapLayout.StationarySlots(npcIds, entries);
        for (int k = 0; k < npcIds.Count; k++)
        {
            var point = MapLayout.NpcPosition(entries[k], slots[k], _pump.ProgressPermille, _travelHoursPerDistrict);

            DrawCircle(ToPixels(point), NpcRadiusPixels, ActivityColor(entries[k].Activity));
        }

        var playerPoint = MapLayout.PlayerPosition(
            player, _host.Now, _pump.ProgressPermille, _travelHoursPerDistrict);

        DrawCircle(ToPixels(playerPoint), PlayerRadiusPixels, PlayerColor);
    }
}
