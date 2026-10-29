using System.Globalization;
using Visionary.Sim;

namespace Visionary.Presentation;

/// <summary>
/// ヘッドレスの煙試験(TDD01 §3.10)。描画が通る読み出しの経路を毎 tick 踏み、最後の状態ハッシュを返す。
/// </summary>
/// <remarks>
/// <b><c>Execute</c> を呼ばない。</b>操作の経路(クリック → Execute)は踏まない。
/// </remarks>
public static class SmokeRun
{
    public static string Run(SimHost host, int travelHoursPerDistrict, int ticks)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(ticks, 1);

        var pump = new TickPump();

        for (int i = 0; i < ticks; i++)
        {
            int advance = pump.Elapse(TickPump.DefaultMillisecondsPerTick);

            for (int t = 0; t < advance; t++)
            {
                host.Advance(1);
            }

            // 結果は捨てる。描画と同じ読み出しの経路が World を書かないことを踏むため。
            foreach (var household in host.Query.Households())
            {
                foreach (int npcId in household.MemberNpcIds)
                {
                    MapLayout.NpcPosition(
                        host.Query.TimetableOf(npcId), npcId, pump.ProgressPermille, travelHoursPerDistrict);
                }
            }

            MapLayout.PlayerPosition(host.Query.Player(), host.Now, pump.ProgressPermille, travelHoursPerDistrict);
        }

        return host.StateHash().ToString("X16", CultureInfo.InvariantCulture);
    }
}
