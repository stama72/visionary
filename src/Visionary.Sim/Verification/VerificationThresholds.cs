namespace Visionary.Sim.Verification;

/// <summary>
/// GDD02 §8 の7項目の判定に使う閾値(TDD01 §5.2)。<b>すべて初期値であり調整対象</b>
/// (GDD02 §9.2)。単位をコメントに書く(ADR-0002)。
/// </summary>
/// <remarks>
/// <b>この20個以外の定数を判定の中に書かない。</b>書いた瞬間、TDD01 §5.2 の表に無い閾値が
/// 生まれ、調整対象の所在(GDD02 §9.2)から外れる(W2-21 タスク仕様)。
/// </remarks>
public static class VerificationThresholds
{
    public const int WindowDays = 120;                        // 単位: 日。ADR-0003 で1年 = 120日
    public const int TransientDays = 30;                      // 単位: 日。窓は day 30 から切る
    public const int MinValidDaysPerWindow = 30;              // 単位: 日。下回る窓は判定不能
    public const int MinWindowsForDispersion = 3;             // 単位: 窓。下回れば偏差の枝は判定不能

    public const int DivergenceLowerDivisor = 10;             // 床 ÷ 10 を下回れば発散(×0.1)
    public const int DivergenceUpperMultiplier = 10;          // 床 × 10 を上回れば発散(×10)
    public const int DispersionGrowthMultiplier = 2;          // 最後の窓 ≥ 最初の窓 × 2 で発散

    public const int RigidityRangePermille = 40;              // 単位: ‰(床に対する比)。±2% の幅
    public const int PartnerSwitchFloorPermille = 50;         // 単位: ‰。下回り続ければ硬直

    public const int BankruptHouseholdRatioPermille = 100;    // 単位: ‰。§8-2 (a)
    public const int InputBlockedHouseholdRatioPermille = 250;// 単位: ‰。§8-2 (b)
    public const int ProductionStoppedRatioPermille = 300;    // 単位: ‰。§8-3

    public const int DistrictSpreadPermille = 20;             // 単位: ‰(床に対する比)。下回れば §8-4 が赤
    public const int MinDistrictsForSpread = 2;               // 単位: 区画。有効日の条件
    public const int MinSellerHouseholdsForSpread = 2;        // 単位: 戸。下回る窓は §8-4 が判定不能

    public const int BandExceededConsecutiveDays = 30;        // 単位: 日。§8-5 (b)

    public const int MoneyLowerBoundPermille = 500;           // 単位: ‰(初期貨幣総量に対する比)
    public const int MoneyUpperBoundPermille = 2000;          // 単位: ‰(同上)
    public const int MoneyDecliningWindows = 3;               // 単位: 窓。連続して窓末が下がれば赤
    public const int MoneyCalibrationBandPermille = 100;      // 単位: ‰。校正 / 機構の切り分けの帯

    public const int UnknownPriceLineRatioPermille = 200;     // 単位: ‰。§8-7
}
