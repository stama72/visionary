namespace Visionary.Sim.Metrics;

/// <summary>
/// 当日ぶんの計数(TDD01 §4.2)。<b>ハッシュに含めない</b>(§3.8) — 順1〜順5 が当日ぶんの計数を
/// 書き、順10(<see cref="Systems.MetricsSystem"/>)だけが読む。<c>World</c> の欄ではなく区分
/// 1つに置く判断は W2-20 タスク仕様「決めて報告2」(TDD01 §4.2 が挙げた2択のうち後者)。
/// </summary>
/// <remarks>
/// <b>意思決定には一切関与しない。</b>ここへの書き込みを配線し忘れても既存のテストは緑のまま
/// 通る(呼び出し側を持たないコード、W2-20 タスク仕様 規則7)。機械が守るのは「この区分を
/// 登録した走行と登録しない走行のハッシュが一致すること」だけである。
/// </remarks>
public sealed class MetricsScratch
{
    private readonly int _itemCount;

    /// <summary>
    /// <paramref name="householdCount"/> 戸・<paramref name="itemCount"/> 品目ぶんの器を確保する。
    /// <see cref="World"/> のコンストラクタが世帯数・品目数と揃えて呼ぶ。
    /// </summary>
    public MetricsScratch(int householdCount, int itemCount)
    {
        if (householdCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(householdCount), householdCount, "世帯数は非負。");
        }

        if (itemCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemCount), itemCount, "品目数は非負。");
        }

        _itemCount = itemCount;

        SellerHasNoReference = new int[householdCount];
        SellerCoefficientCapped = new int[householdCount];
        DemandLines = new int[householdCount];
        DemandLinesWithoutKnownPrice = new int[householdCount];
        InputBlockedByFunds = new int[householdCount];

        int pairCount = householdCount * itemCount;
        PreviousCounterpartyId = new int[pairCount];
        CurrentCounterpartyId = new int[pairCount];

        Array.Fill(PreviousCounterpartyId, -1);
        Array.Fill(CurrentCounterpartyId, -1);
    }

    /// <summary>段1 が世帯 Id 昇順に毎日書く。添字 = 世帯Id。相場基準が立たなかった: 1 / 立った: 0。</summary>
    public int[] SellerHasNoReference { get; }

    /// <summary>段1 が世帯 Id 昇順に毎日書く。添字 = 世帯Id。§1.1 の頭打ちが実際に効いた: 1。</summary>
    public int[] SellerCoefficientCapped { get; }

    /// <summary>段4 が書く。添字 = 世帯Id。<c>ExpectedStock &lt; TargetStock</c> の行の数。</summary>
    public int[] DemandLines { get; }

    /// <summary>段4 が書く。添字 = 世帯Id。うち <c>HasMarketTerm == false</c> の行の数。</summary>
    public int[] DemandLinesWithoutKnownPrice { get; }

    /// <summary>
    /// 段4 と段5b が書く(どちらも1を代入する。加算しない)。添字 = 世帯Id。
    /// GDD02 §8-2 (b) の世帯日の 0/1。
    /// </summary>
    public int[] InputBlockedByFunds { get; }

    /// <summary>
    /// 段5b が書く。添字 = 世帯Id * itemCount + itemId。前日の取引相手(-1 = 買っていない)。
    /// <b>当日ぶんの <see cref="BeginDay"/> では消さない唯一の欄</b>(スイッチ率が前日と比べるため)。
    /// </summary>
    public int[] PreviousCounterpartyId { get; }

    /// <summary>段5b が書く。添字 = 世帯Id * itemCount + itemId。当日の取引相手(-1 = 買っていない)。</summary>
    public int[] CurrentCounterpartyId { get; }

    /// <summary>
    /// 当日ぶんを初期化する。<b>段1 の先頭で呼ぶ</b>(順5 が唯一の書き手)。
    /// </summary>
    /// <remarks>
    /// <c>CurrentCounterpartyId</c> を <c>PreviousCounterpartyId</c> へ移してから -1 で埋める。
    /// 他の欄は 0 で埋める。
    /// </remarks>
    public void BeginDay()
    {
        Array.Copy(CurrentCounterpartyId, PreviousCounterpartyId, CurrentCounterpartyId.Length);
        Array.Fill(CurrentCounterpartyId, -1);

        Array.Clear(SellerHasNoReference);
        Array.Clear(SellerCoefficientCapped);
        Array.Clear(DemandLines);
        Array.Clear(DemandLinesWithoutKnownPrice);
        Array.Clear(InputBlockedByFunds);
    }

    /// <summary>(世帯Id, itemId) を <see cref="PreviousCounterpartyId"/> / <see cref="CurrentCounterpartyId"/> の添字へ写す。</summary>
    public int IndexOf(int householdId, int itemId) => (householdId * _itemCount) + itemId;
}
