using Visionary.Sim.Systems;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="Errand"/>(GDD06 §2・§3、#98 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class ErrandTests
{
    /// <summary>テスト表 #1(旧 StoreChoiceTests から移設)。距離4→8(往復)。同一区画→0。</summary>
    [Fact]
    public void TravelHoursCountsTheRoundTrip()
    {
        Assert.Equal(8, Errand.TravelHours(fromDistrictId: 0, toDistrictId: 8, hoursPerDistrict: 1));
        Assert.Equal(0, Errand.TravelHours(fromDistrictId: 3, toDistrictId: 3, hoursPerDistrict: 1));
    }

    /// <summary>
    /// テスト表 #2。往復4時間・機会費用4 → 16。往復0 → 0。引数は2つだけ
    /// (目標在庫を渡す余地が無い ── 旧の「目標在庫で割り戻す」が戻らない)。
    /// </summary>
    [Fact]
    public void ErrandCostIsNotAmortized()
    {
        Assert.Equal(16, Errand.Cost(travelHours: 4, errandOpportunityCostPerHour: 4));
        Assert.Equal(0, Errand.Cost(travelHours: 0, errandOpportunityCostPerHour: 4));
    }

    /// <summary>
    /// テスト表 #3。労働力係数300‰・往復2時間・T=12 → 50。往復5時間 →
    /// CeilDiv(1500,12) = 125。
    /// </summary>
    [Fact]
    public void ErrandLaborLossCeilsPerTrip()
    {
        Assert.Equal(50, Errand.LaborLossPermille(delegateLaborPermille: 300, travelHours: 2, disposableHours: 12));
        Assert.Equal(125, Errand.LaborLossPermille(delegateLaborPermille: 300, travelHours: 5, disposableHours: 12));
    }

    /// <summary>
    /// 【核心】テスト表 #4。q=4・w=76・p=54 → 44。w=p → 0。
    /// q=1・w=50・p=51 → 0(FloorDiv(−1,2)が−1にならない)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-20)。</b><c>Errand.Surplus</c> の <c>willingness &lt;= price</c> の
    /// ガードを外し、常に <c>FloorDiv(quantity × (willingness − price), 2)</c> を計算する変異
    /// (<c>max(0, …)</c> を落とす)を当てたところ、<c>Assert.Equal(0, Errand.Surplus(1, 50, 51))</c>
    /// が実際値-1(<c>FloorDiv(1×(50-51),2) = FloorDiv(-1,2) = -1</c>。負の余剰が他の品目の
    /// 余剰を食う経路)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void SurplusNeverGoesNegative()
    {
        Assert.Equal(44, Errand.Surplus(quantity: 4, willingness: 76, price: 54));
        Assert.Equal(0, Errand.Surplus(quantity: 4, willingness: 54, price: 54));
        Assert.Equal(0, Errand.Surplus(quantity: 1, willingness: 50, price: 51));
    }

    /// <summary>
    /// テスト表 #20。T=7・労働力係数300‰で、往復2時間と往復6時間の2回 →
    /// CeilDiv(600,7)+CeilDiv(1800,7)=86+258=344。往復時間を先に足すとCeilDiv(2400,7)=343
    /// (外出ごとに切り上げてから合計する。GDD06 §3)。
    /// </summary>
    /// <remarks>
    /// <b>置き場所について。</b>T(<c>disposableHours</c>)は <see cref="ErrandPlanner"/> の
    /// 外出上限の判定にも同じ値を使う(GDD06 §3)ため、往復2時間+往復6時間=8時間の2回の外出は
    /// T=7の上限そのものと両立しない(<see cref="ErrandPlanner"/> を通すと2回目が上限で
    /// 弾かれてしまい、この数値例を再現できない)。「外出ごとに切り上げてから合計する」という
    /// 性質そのものは <see cref="Errand.LaborLossPermille"/> の呼び出し2回として直接確かめる。
    /// </remarks>
    [Fact]
    public void ErrandLaborLossCeilsEachTripSeparately()
    {
        int perTripSum =
            Errand.LaborLossPermille(delegateLaborPermille: 300, travelHours: 2, disposableHours: 7)
            + Errand.LaborLossPermille(delegateLaborPermille: 300, travelHours: 6, disposableHours: 7);

        Assert.Equal(344, perTripSum);

        // 往復時間を先に合計してから1回だけ切り上げる(誤り)と343になり、区別できる。
        int combinedFirst =
            Errand.LaborLossPermille(delegateLaborPermille: 300, travelHours: 8, disposableHours: 7);
        Assert.Equal(343, combinedFirst);
        Assert.NotEqual(perTripSum, combinedFirst);
    }
}
