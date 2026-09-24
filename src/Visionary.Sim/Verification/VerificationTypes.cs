namespace Visionary.Sim.Verification;

/// <summary>判定の三値(TDD01 §5.2)。</summary>
public enum Verdict
{
    Green = 0,
    Red = 1,

    /// <summary>母数が消えている・条件が一度も試されていない。<b>緑ではない。</b></summary>
    Indeterminate = 2,
}

/// <summary>
/// 根拠の1件。<b>すべて整数</b>(割合は‰)。<see cref="Threshold"/> は「使った閾値」、
/// <see cref="Denominator"/> は「母数」で、issue #209 が summary.json に求めた4つ
/// (項目 / 判定 / 根拠の値 / 使った閾値 / 母数)のうち3つを担う。
/// </summary>
/// <remarks>閾値を持たない根拠(切り分けのために出すだけの値)は <see cref="Threshold"/> = -1。</remarks>
public readonly record struct Evidence(string Name, long Value, long Threshold, long Denominator);

/// <summary>1項目 × 1シードの判定。</summary>
public sealed record VerificationItemResult(
    string Id,                       // "8-1a" など。TDD01 §5.2 の表の Id
    Verdict Verdict,
    long FirstRedDay,                // 赤でなければ -1
    IReadOnlyList<Evidence> Evidence);

/// <summary>1シードぶんの判定。</summary>
public sealed record SeedVerification(
    long Seed,
    int DurationDays,
    int WindowCount,                 // 判定に使えた窓の数(過渡期を除いた後)
    IReadOnlyList<VerificationItemResult> Items);

/// <summary>シードを横断した1項目の判定。</summary>
public sealed record OverallItemResult(
    string Id,
    Verdict Verdict,
    IReadOnlyList<long> RedSeeds,
    IReadOnlyList<long> IndeterminateSeeds);

/// <summary><c>summary.json</c> の中身。整形は Runner が行う。</summary>
public sealed record RunSummary(
    int SchemaVersion,               // 1
    int WindowDays,
    int TransientDays,
    int DurationDays,
    IReadOnlyList<SeedVerification> Seeds,
    IReadOnlyList<OverallItemResult> Overall);

/// <summary>
/// 12項目の Id と並び順(W2-21 タスク仕様「2.」)。<b>この順で <see cref="SeedVerification.Items"/> /
/// <see cref="RunSummary.Overall"/> を並べる。</b> <see cref="VerificationAccumulator"/> と
/// <see cref="RunSummaryBuilder"/> の両方が同じ並びを読むための唯一の置き場所(決めて報告 —
/// 仕様は並び順だけを定め、定数として1か所に持つ場所までは決めていない)。
/// </summary>
internal static class VerificationItemIds
{
    public const string Divergence = "8-1a";
    public const string Rigidity = "8-1b";
    public const string ImportContentCost = "8-1c";
    public const string BankruptHouseholds = "8-2a";
    public const string InputBlockedHouseholds = "8-2b";
    public const string ProductionStopped = "8-3";
    public const string DistrictSpread = "8-4";
    public const string FloorBreach = "8-5a";
    public const string BandExceeded = "8-5b";
    public const string WindowPurchase = "8-5c";
    public const string MoneyBounded = "8-6";
    public const string UnknownPriceRatio = "8-7";

    /// <summary>TDD01 §5.2 の表の並び(W2-21 タスク仕様「2.」)。</summary>
    public static readonly string[] All =
    {
        Divergence, Rigidity, ImportContentCost, BankruptHouseholds, InputBlockedHouseholds,
        ProductionStopped, DistrictSpread, FloorBreach, BandExceeded, WindowPurchase,
        MoneyBounded, UnknownPriceRatio,
    };
}
