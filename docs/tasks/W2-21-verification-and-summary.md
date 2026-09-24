# W2-21: GDD02 §8 の7項目の判定関数と `summary.json`

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#209](https://github.com/stama72/visionary/issues/209)              |
| 根拠     | [GDD02 §8](../03-gdd/02-economy.md)(7項目)/ [§9.2](../03-gdd/02-economy.md)(値の所在)/ [TDD01 §4.1](../04-tdd/01-sim-core-and-m0.md)(CLI)/ [§4.2](../04-tdd/01-sim-core-and-m0.md)(メトリクス)/ [§5.2](../04-tdd/01-sim-core-and-m0.md)(**判定の定量化。本タスクの正**) |
| ブランチ | `feat/209-verification-and-summary`                                  |
| worktree | `.claude/worktrees/209-verification-and-summary/`                    |

**本タスクは計器の目盛を作る。校正はしない。** [#41 の実測](https://github.com/stama72/visionary/issues/41)のとおり、着手時点の master では貨幣総量が 20〜30 日で初期 24,000 の 1〜7% へ落ち、生産が止まり、都市内の約定が消える。**したがって `summary.json` の大半が「赤」を返すのが正しい動作である。** 緑にするのは後続の校正 issue であり、本タスクの完了条件に入れない。

**閾値・窓・三値・独立性の判定は [TDD01 §5.2](../04-tdd/01-sim-core-and-m0.md) が正である。** 本書はそれを**どう実装するか**だけを持つ。**§5.2 と食い違う実装を書かない。食い違いを見つけたら象限I-b であり、フェーズ1 へ戻す案件である**(フェーズ2 が §5.2 を直すと `SPEC-OUTSIDE` で止まる)。

## スコープ

**含まない:**

- **日次メトリクスの収集と CSV 出力**([#41](https://github.com/stama72/visionary/issues/41) / [W2-20](W2-20-metrics-output.md))。本タスクは**既にある列を読むだけ**で、収集側(`MetricsSystem` / `MetricsScratch` / 順5 の配線)には一切触れない
- **経済の校正**(外部価格・初期資金・レシピ数量・目標在庫のどれも動かさない)
- 信用あり/なしの比較実験(W4、[#44](https://github.com/stama72/visionary/issues/44))
- **誤爆率**(GDD01 §6-5。W4、[#45](https://github.com/stama72/visionary/issues/45))。`summary.json` に欄も作らない
- `trust.csv` / `events.jsonl` / `vsim promise-table` / `vsim dialogue-sample`(W3・W5)
- 設定ファイルの `features` / `coefficients`(W4。現行どおり空でなければエラー)
- **判定を緑にすること**(上記)

**変えない既存コード**(規則8。[02-task-spec](../process/02-task-spec.md)「変えないと宣言する既存コード」)。単位はファイルではなく規則:

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `MetricsSystem` が `PriceRow.SettledMedian` を**行単位・数量で重み付けしない中央値**とし、0 件の日を `-1` にする | [TDD01 §4.2](../04-tdd/01-sim-core-and-m0.md) | **一致**(読んで突き合わせた。判定の「有効日 = `settled_count > 0`」はこの `-1` を踏まないための言い換えである) |
| `MetricsSystem` が `PriceRow.OfferAtFloorWithExportCount` を「床に一致し、かつその日に輸出の `Sale` 行がある (売り手 × 品目)」とする | [GDD02 §8](../03-gdd/02-economy.md)-1 | **一致**(硬直の除外はこの列だけを読む。`OfferAtFloorCount` との差は除かない) |
| `MetricsSystem` が `TradesRow.PartnerSwitchPermille` の分母 0 を `-1` にする | [TDD01 §4.2](../04-tdd/01-sim-core-and-m0.md) / [W2-20 テスト #17](W2-20-metrics-output.md) | **一致**(経済が死んだ日を「スイッチ率が固着」と読ませないための `-1` である。判定側もこの日を有効日から外す) |
| `MetricsSystem` が `DistrictRow` を**買い手の区画**で立て、約定が 1 件以上ある行だけを書く | [GDD02 §8](../03-gdd/02-economy.md)-4 | **一致**(区画差はこの行から取る。行が無い (品目, 区画) は「その日その区画で買われなかった」であって価格 0 ではない) |
| `MetricsSystem` が `EconomyRow.BankruptHouseholds` を**前日**の購入結果から立て、`InputBlockedHouseholds` を**当日**とする | [W2-20「(a) と (b) の時点のずれ」](W2-20-metrics-output.md) / [GDD02b §3.3](../03-gdd/02b-consumption-and-household.md) | **一致**(`8-2a` が読むのは `BankruptHouseholds` のほうである。[TDD01 §5.2](../04-tdd/01-sim-core-and-m0.md) の「(a) ⊆ (b)」も1日ずれたまま成り立つ主張として書いてある) |
| `WorldDefinition.ExternalBuyPrice` が**1次産品で例外を投げる** | [GDD02d §2.1](../03-gdd/02d-external-market-and-money.md) | **一致**(判定は `IsPrimaryItem` で先に弾く。下記「3.」) |
| `WorldDefinition.ExternalSellPrice(itemId, season)` が都市生産品では季節に依らず `ApplyPermille(外部買値, 1000 + 交易マージン‰)` を返す | [GDD02d §3](../03-gdd/02d-external-market-and-money.md) / [§5](../03-gdd/02d-external-market-and-money.md) | **一致**(`8-5b` / `8-5c` の天井はこれを呼ぶ。式を写さない) |
| `CsvMetricsSink` の5つの CSV(列・順序・改行 `\n`・`InvariantCulture`) | [TDD01 §4.2](../04-tdd/01-sim-core-and-m0.md) | **一致**(本タスクは列を1本も足さない。`summary.json` は**別のファイル**である) |
| `RunConfig` の検査(`masterSeeds` / `durationDays` / `world.preset` / 未知キー拒否) | [TDD01 §4.1](../04-tdd/01-sim-core-and-m0.md) | **一致**(`durationDays` に下限を足さない。窓が取れない走行は「判定不能」で表現する) |

## 作るもの

### 1. `Visionary.Sim.Verification` — 閾値

```csharp
namespace Visionary.Sim.Verification;

/// <summary>
/// GDD02 §8 の7項目の判定に使う閾値(TDD01 §5.2)。<b>すべて初期値であり調整対象</b>
/// (GDD02 §9.2)。単位をコメントに書く(ADR-0002)。
/// </summary>
public static class VerificationThresholds
{
    public const int WindowDays = 120;                        // 単位: 日。ADR-0003 で1年 = 120日
    public const int TransientDays = 30;                      // 単位: 日。窓は day 30 から切る
    public const int MinValidDaysPerWindow = 30;              // 単位: 日。下回る窓は判定不能
    public const int MinWindowsForDispersion = 3;             // 単位: 窓。下回れば偏差の枝は判定不能

    public const int DivergenceLowerDivisor = 10;             // 床 ÷ 10 を下回れば発散(×0.1)
    public const int DivergenceUpperMultiplier = 10;          // 床 × 10 を上回れば発散(×10)
    public const int DispersionGrowthMultiplier = 2;          // 最後の窓 ≥ 基準窓 × 2 で発散
    public const int DispersionFloorPermille = 100;           // 単位: ‰(窓平均に対する比)。これを下回る偏差は発散と呼ばない

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
```

**この 21 個以外の定数を判定の中に書かない。** 書いた瞬間、[TDD01 §5.2](../04-tdd/01-sim-core-and-m0.md) の表に無い閾値が生まれ、調整対象の所在([GDD02 §9.2](../03-gdd/02-economy.md))から外れる。

### 2. `Visionary.Sim.Verification` — 判定結果の型

```csharp
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
/// 根拠の1件。<b>すべて整数</b>(割合は‰)。<paramref name="Threshold"/> は「使った閾値」、
/// <paramref name="Denominator"/> は「母数」で、issue #209 が summary.json に求めた4つ
/// (項目 / 判定 / 根拠の値 / 使った閾値 / 母数)のうち3つを担う。
/// </summary>
/// <remarks>閾値を持たない根拠(切り分けのために出すだけの値)は <c>Threshold = -1</c>。</remarks>
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
```

- **項目の Id は 12 本である**(7項目を判定の単位まで割ったもの): `8-1a` `8-1b` `8-1c` `8-2a` `8-2b` `8-3` `8-4` `8-5a` `8-5b` `8-5c` `8-6` `8-7`。**この順で並べる**(`Items` / `Overall` とも)
- **`Verdict` の数値を振り直さない。** JSON には文字列 `"green"` / `"red"` / `"indeterminate"` で出す(下記「6.」)
- **`FirstRedDay` は、日単位の条件ならその日、窓単位の条件なら窓の末日である**([TDD01 §5.2](../04-tdd/01-sim-core-and-m0.md))

### 3. `VerificationAccumulator` — 日次メトリクスを畳む

```csharp
namespace Visionary.Sim.Verification;

/// <summary>
/// 日次メトリクス(TDD01 §4.2)を1日ずつ受け取り、GDD02 §8 の7項目を判定する
/// (TDD01 §5.2)。<b>走行全体を溜めない</b> ── 保持するのは窓1つぶんの作業領域と、
/// 窓をまたぐ小さな状態だけである(下記「保証と、残る穴」)。
/// </summary>
public sealed class VerificationAccumulator : IDailyMetricsSink
{
    public VerificationAccumulator(WorldDefinition definition);

    public void Write(in DailySnapshot snapshot);

    /// <summary>走行の最後に1度だけ呼ぶ。<b>呼んだ後に <see cref="Write"/> を呼ばない。</b></summary>
    public SeedVerification Build(long seed);
}
```

**入力の作り方・約束(規則7 の一部。残りは下記の専用節)**:

- **`Write` は `MetricsSystem` が `IDailyMetricsSink` へ渡すのと同じ `DailySnapshot` を、day 昇順に、欠けなく受け取る前提で書く。** 順序が飛んだら `ArgumentException` を投げる(**窓の切り方が日の並びに依存するので、黙って進むと窓が静かにずれる**)
- **床は `WorldDefinition.ExternalBuyPrice(itemId)` から取る。`PriceRow.ExternalBuyPrice` を読まない。** 読むと、`8-5a`(提示価格が床を下回らない)が**同じ定数どうしの比較**になり、`MetricsSystem` が床を取り違えたときに判定が素通りする。**1次産品は `IsPrimaryItem` で先に弾く**(`ExternalBuyPrice` は1次産品で例外を投げる)
- **天井は `WorldDefinition.ExternalSellPrice(itemId, season)` を呼ぶ。式を写さない。** 季節は `EconomyRow.Season` から作る
- **初期貨幣総量は `definition.InitialLiquidFunds × definition.HouseholdCount` である。** day 0 の `money_total` を使わない — それは**順10 で読む値なので、初日の輸出入がもう反映されている**。[issue #120](https://github.com/stama72/visionary/issues/120) の「初日の1日で 84.3% が流出」は、この2つを取り違えると丸ごと見えなくなる
- **`8-4` の担い手数は `HouseholdRow.OutputItemId` を数える。** 職業から逆引きしない(④の職業付け替えで当日の職業が動くため、その日の実態は世帯行のほうにある)

**保証と、残る穴(メモリ)**:

- **保持するのは (i) 都市生産5品目 × 窓の日数ぶんの約定価格中央値(最大 600 int)、(ii) 品目・項目ごとの窓をまたぐ小さな状態(前の窓の偏差‰・連続日数・連続窓数・`FirstRedDay`)だけである。** 窓を閉じたら (i) を捨てる
- **(i) を持つのは、相対平均絶対偏差が平均を先に要求するためである**(2 周する)。レンジ(`max − min`)だけならオンラインで済むが、偏差は済まない
- **残る穴**: `DurationDays` を増やすと `Overall` / `Seeds` は増えないが、**シード数に比例して `SeedVerification` が積まれる。** 10 シードでは無視できる(1シードあたり 12 項目 × 数件の根拠)。**シードを 1,000 本にする実験を想定していない**

### 4. 12 項目の判定の実装

**赤の条件・閾値・母数・判定不能の条件は [TDD01 §5.2](../04-tdd/01-sim-core-and-m0.md) の表が正である。** ここでは表から読み取れない実装上の約束だけを書く。

| Id | 実装上の約束 |
| -- | ------------ |
| `8-1a` | 帯の比較は**除算を使わず**に書く: `settled_median > 床 × 10` / `settled_median × 10 < 床`。相対平均絶対偏差‰ は §5.2 の2式をそのまま使う(`CeilDiv`)。**窓の平均が 0 の窓は偏差の枝を判定不能にする**(ゼロ除算)。**基準窓・散らばりの下限・4つの分岐は §5.2「浮動小数点を呼び込む語を置き換える」のとおり**(下記「別表(フェーズ1 の訂正)」) |
| `8-1b` | レンジは有効日の `max − min`。比較は `(max − min) × 1000 < 床 × 40`(除算を使わない)。**スイッチ率は品目を持たない**ので、都市合計として1件の根拠に出す。**品目のレンジとスイッチ率は `or` で結ぶ**(§5.2 の「または」) |
| `8-1c` | 輸入含有原価は [GDD02d §3.1](../03-gdd/02d-external-market-and-money.md) の再帰式を `WorldDefinition` から解く。**1次産品の輸入含有原価は外部売値**で、季節を持つので**年間の最大値**(4季の `max`)を使う — 一番きつい季節で条件が破れるなら破れている。`CeilDiv` の向きは §3.1 のとおり |
| `8-2a` / `8-2b` | 分母は `窓の日数 × definition.HouseholdCount`。**`世帯数` を `households.csv` の行数から数えない**(同じ値だが、判定が出力の形に依存する) |
| `8-3` | `HouseholdRow.Occupation` で束ね、`ProductionRuns == 0` の世帯日を数える。**醸造(`Occupation.Brewer`)の値は、閾値を超えていなくても必ず根拠に出す**([GDD02 §8](../03-gdd/02-economy.md)-3 が名指ししている) |
| `8-4` | その日・その品目の `DistrictRow` を集め、2 行以上あれば `max(SettledMedian) − min(SettledMedian)` を足す。**窓平均は `CeilDiv(Σ 差, 有効日数)`**。比較は `窓平均 × 1000 < 床 × 20`。**工具(`Item.Tools`)を外す** |
| `8-5a` | `OfferCount > 0 && OfferMin < 床` を**全日**について見る。過渡期も窓も関係なく、1日でも成立したら赤 |
| `8-5b` | 連続日数は**窓をまたいで数える**(走行を通した1本のカウンタ)。30 日目に達した日を含む窓が赤になる。`settled_count == 0` の日は**連続を切る**(約定が無い日は「帯を上回った」とは言えない) |
| `8-5c` | 「天井が試された日」= `OfferMax > 天井` の日。**都市生産5品目のどれかで試されればよい**(窓に対して1つの判定) |
| `8-6` | 帯の比較は `money_total × 1000 < 初期 × 500` / `money_total × 1000 > 初期 × 2000`。**連続3窓の判定は窓末の値を持ち越す。** `calibration` / `mechanism` / `unknown` は §5.2 の表のとおり窓ごとに決め、**窓ごとの値を根拠に出す**(項目の判定には使わない) |
| `8-7` | 分子・分母とも `EconomyRow` の日次の値を窓で合計してから割る。**日ごとに割って平均しない**(日次の分母が小さい日が過大な重みを持つ) |

**根拠(`Evidence`)には、少なくとも次を出す。**

| Id | 出す根拠 |
| -- | -------- |
| `8-1a` | 品目ごとに、窓内の `settled_median` の `max` / `min`(閾値は床 × 10 / 床 ÷ 10、母数は有効日数)、窓の相対平均絶対偏差‰ |
| `8-1b` | 品目ごとにレンジ‰(閾値 40、母数 有効日数)、`seller_days_at_floor_with_export ÷ seller_days` ‰(閾値 `-1`。**上の「残る穴」を人が読むための値**)、スイッチ率の窓最小値‰(閾値 50) |
| `8-1c` | 品目ごとに 輸入含有原価 と 外部買値、窓の `export_quantity` 合計 |
| `8-2a` / `8-2b` | 割合‰(閾値 100 / 250、母数 `窓の日数 × 世帯数`) |
| `8-3` | 職業ごとの停止割合‰(閾値 300、母数 その職業の世帯日)。**醸造は必ず含める** |
| `8-4` | 品目ごとの窓平均の差‰(閾値 20、母数 有効日数)、担い手の戸数、`seller_days_without_reference ÷ seller_days` ‰、`window_settled_count ÷ settled_count` ‰(後ろ2つは閾値 `-1`。[GDD02 §8](../03-gdd/02-economy.md)-4 が「併せて読む」と書いている切り分け) |
| `8-5a` | 最初に床を割った (日, 品目) と、そのときの `offer_min` と床 |
| `8-5b` | 品目ごとの最長連続日数(閾値 30) |
| `8-5c` | 天井が試された日数、`window_settled_count` の窓合計 |
| `8-6` | 窓内の `money_total` の `min` / `max` / 窓末(閾値は初期 × 500‰ / 2000‰、母数 初期貨幣総量)、`export_value` / `import_value` の窓合計、`boundedBy`(`calibration` = 0 / `mechanism` = 1 / `unknown` = 2 を `Value` に置き、閾値 `-1`) |
| `8-7` | 割合‰(閾値 200、母数 `Σ demand_lines`) |

**根拠は「最後の窓のもの」ではなく「判定を決めた窓のもの」を出す。** 赤なら最初に赤くなった窓、緑なら最後の窓、判定不能なら最後の窓である。**最後の窓を無条件に出すと、経済が死んで判定不能になった後の窓の値が、赤の根拠として並ぶ。**

### 5. シードを横断する畳み込み

```csharp
namespace Visionary.Sim.Verification;

public static class RunSummaryBuilder
{
    public static RunSummary Build(int durationDays, IReadOnlyList<SeedVerification> seeds);
}
```

- **項目ごとに: 1つでも赤いシードがあれば赤。赤が無く、判定不能のシードが1つでもあれば判定不能。それ以外が緑。**
- **判定不能を緑へ倒さないのは、狭い側に倒す規則である**([03-corrections](../process/03-corrections.md) 規則1)。10 シードのうち1本で判定できなかったなら、その項目は 10 シードについて確かめられていない
- `RedSeeds` / `IndeterminateSeeds` は**シード昇順**
- **`seeds` が空なら例外を投げる**(`vsim run` は `masterSeeds` を非空に強制しているので到達しないが、`Build` の契約としては定める)

### 6. `Visionary.Sim.Runner` — 配線と `summary.json`

```csharp
namespace Visionary.Sim.Runner;

/// <summary>複数の <see cref="IDailyMetricsSink"/> へ同じスナップショットを流す。</summary>
internal sealed class CompositeDailyMetricsSink : IDailyMetricsSink, IDisposable
{
    public CompositeDailyMetricsSink(params IDailyMetricsSink[] sinks);
    public void Write(in DailySnapshot snapshot);   // 配列の順に呼ぶ
    public void Dispose();                          // IDisposable なものだけを、同じ順で捨てる
}

internal static class SummaryJsonWriter
{
    public static void Write(string path, RunSummary summary);
}
```

**`RunRun` の変更はこれだけである:**

1. シードごとに `new CompositeDailyMetricsSink(csvSink, accumulator)` を `BuildPipeline` へ渡す。**`accumulator` は `using` の外で作り、`Advance` の後に `Build(seed)` を呼ぶ**(`csvSink` の `Dispose` と順序を絡めない)
2. 全シードを回し終えた後、`RunSummaryBuilder.Build(...)` の結果を `<out>/summary.json` へ書く。**`<out>/<シード>/` の下ではない**([TDD01 §4.1](../04-tdd/01-sim-core-and-m0.md))
3. 標準出力のシードごとの1行は**変えない**

**`summary.json` の整形(**W2-20 別表 #21' と同じ罠を踏まないための約束**):**

- **`JsonSerializerOptions`**: `WriteIndented = true` / `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`(日本語を `\uXXXX` にしない)/ `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`
- **書き出す直前に `\r\n` を `\n` へ正規化し、末尾に改行を1つ置く。** `Utf8JsonWriter` の字下げがどの改行を使うかに依存しない — **CI が Ubuntu、開発機が Windows で、バイト一致の検査が壊れる**
- **BOM 無し UTF-8。** `CsvMetricsSink` と同じ
- **`Verdict` は文字列 `"green"` / `"red"` / `"indeterminate"` で出す**(`JsonStringEnumConverter` に小文字化を任せず、**明示的な変換関数を書く** — 既定の変換は `"Green"` を返す)
- **数値はすべて整数である。** `double` は1つも現れない([ADR-0002](../adr/0002-time-model-and-determinism.md)。`DeterminismConventionTests` は `Runner` を見ていないので、ここは機械が守らない)

```jsonc
{
  "schemaVersion": 1,
  "windowDays": 120,
  "transientDays": 30,
  "durationDays": 36000,
  "seeds": [
    { "seed": 1, "durationDays": 36000, "windowCount": 299,
      "items": [
        { "id": "8-1a", "verdict": "red", "firstRedDay": 149,
          "evidence": [ { "name": "settledMedianMax:6", "value": 1720, "threshold": 540, "denominator": 41 } ] }
      ] }
  ],
  "overall": [
    { "id": "8-1a", "verdict": "red", "redSeeds": [1, 3], "indeterminateSeeds": [] }
  ]
}
```

### 7. `configs/m0-w2-baseline.json`

```jsonc
{
  "masterSeeds": [1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
  "durationDays": 36000,
  "world": { "preset": "m0-small" }
}
```

**Git 管理する**([TDD01 §4.1](../04-tdd/01-sim-core-and-m0.md)「実験の再現条件を履歴に残す」)。**`features` / `coefficients` のキーは書かない** — W4 で実装するまで空しか受け付けないので、空のオブジェクトを置くと「効いたつもり」の器だけが残る。

## 呼び出し側を持たないコード(規則7)

**`VerificationAccumulator` と `RunSummaryBuilder` は `vsim run` からしか呼ばれない。** シムの本体は一切呼ばないので、**配線を落としても既存のテストは 1 件も落ちない**(W2-05 の `BuyerDemand` と同じ形。[02-task-spec](../process/02-task-spec.md) 規則7)。

| 何を | どこで | 入力の作り方・約束 |
| ---- | ------ | ------------------ |
| `new VerificationAccumulator(definition)` | `RunRun` のシードごとのループの中、`WorldGenerator.Generate` の後 | `definition` は `WorldDefinition.M0`。**シードごとに作り直す** — 持ち回すと前のシードの窓の状態が混ざる |
| `CompositeDailyMetricsSink(csvSink, accumulator)` | 同上、`BuildPipeline` へ渡す直前 | **順序は CSV が先。** 逆でも値は同じだが、CSV の書き出しが accumulator の例外で失われないほうがよい |
| `accumulator.Build(seed)` | `scheduler.Advance` の後、`csvSink` の `using` を抜けた後 | **1シードにつき1回だけ。** 2回呼んだときの挙動は定めない(`InvalidOperationException` を投げる) |
| `RunSummaryBuilder.Build(config.DurationDays, results)` | 全シードのループを抜けた後 | `results` は**シードを回した順**(= `masterSeeds` の並び)。`Build` の中で昇順に並べ直さない — **`masterSeeds` が昇順でない設定を書ける**ので、並べ替えを入れると `seeds` 配列の順が設定と食い違う |
| `SummaryJsonWriter.Write(Path.Combine(outDirectory, "summary.json"), summary)` | 同上 | `outDirectory` は既にシードごとのディレクトリ作成で存在するが、**シードが0件でも書けるように `Directory.CreateDirectory` を呼ぶ** |

**呼び出し順**: 日次は `MetricsSystem` → `CsvMetricsSink.Write` → `VerificationAccumulator.Write`。走行後は `Advance` → `Dispose(csv)` → `Build` → 全シード後に `RunSummaryBuilder.Build` → `SummaryJsonWriter.Write`。

**代償**: 判定は**日次メトリクスの列を正しいものとして読む。** 列が取り違わっていれば判定も静かに取り違える。W2-20 別表 #29 / #30 がその層の機械であり、**本タスクは重ねない**(重ねると同じ取り違えを2度書くことになり、2つとも同じ向きに間違える)。

## 落ちるべき条件(テスト)

**入力は合成した `DailySnapshot` の列である。** シムを回して作らない — 回して作ると、**判定の欠陥と経済の欠陥が同じテストの中で混ざる**(着手時点の経済は死んでいるので、ほとんどの判定が赤になり、赤の理由が判定側か経済側か区別できない)。**テスト用のビルダを1つ置き、既定値は「すべて緑になる健全な1日」とする** — 各テストは変えたい列だけを上書きする。

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心(当てる変異 / 期待) |
| - | ------ | -------- | -------------------- | ------------------------- |
| 1 | `WindowsStartAfterTheTransient` | 過渡期(day 0〜29)に帯を出る `settled_median` を置いても `8-1a` が緑、day 30 以降に置くと赤。**`8-6` と `8-5a` だけは day 0 の違反でも赤** | 過渡期を全項目に適用した / どこにも適用しなかった。**前者は [issue #120](https://github.com/stama72/visionary/issues/120) の「初日の1日で 84.3% が流出」を判定から消す** | **核心**。`8-6` の日次の帯の検査を過渡期の後から始めるように変える / **期待: 赤** |
| 2 | `PartialWindowIsNotJudged` | 149 日の走行(過渡期 30 + 窓 120 − 1)で、窓を使う全項目が判定不能になる。150 日なら判定が出る | 端数の窓を判定した(**日数が足りないだけの走行が「緑」を返す**) | |
| 3 | `EmptyDenominatorIsIndeterminateNotGreen` | 全日 `settled_count == 0` の品目について `8-1a` / `8-1b` / `8-5b` が判定不能になり、**緑にならない** | 有効日 0 を「帯を出ていない = 緑」と読んだ。**経済が死んだ走行が「価格は安定している」と報告される** | **核心**。`8-1a` の有効日 0 の枝を `Verdict.Green` に変える / **期待: 赤** |
| 4 | `ValidDayFloorIsEnforced` | 有効日が 29 日の窓は判定不能、30 日なら判定が出る | 有効日数の下限を落とした / 境界を `>` と `>=` で取り違えた | |
| 5 | `DivergenceUsesTheFloorAsTheBase` | 床 54(パン)に対し `settled_median = 541` で赤、`540` で緑。**`WorldDefinition` の床を変えると判定も動く** | 基準を day 0 の実測値にした / `PriceRow.ExternalBuyPrice` を読んだ(**同じ定数どうしの比較になる**) | **核心**。基準を `WorldDefinition.ExternalBuyPrice` から `PriceRow.ExternalBuyPrice` へ変える(テストは `PriceRow` 側に誤った床を入れる)/ **期待: 赤** |
| 6 | `DispersionGrowthNeedsThreeWindows` | 2 窓しか取れない走行では偏差の枝が判定不能で、3 窓なら単調増加 × 2 倍で赤になる | 窓 2 つで「最後 ≥ 最初 × 2」を判定した(**1回の跳ねが発散と読まれる**) | |
| 7 | `DispersionIsRelativeMeanAbsoluteDeviation` | 中央値が `100, 100, 100, 100` の窓で偏差‰ が 0、`80, 120, 80, 120` の窓で 200 になる | 平方根・分散・標準偏差を書いた(`double` が入る)/ 平均で割り忘れた(絶対量になり、水準が上がるだけで「発散」と読まれる) | **核心**。相対平均絶対偏差の分母から `窓の平均` を落とす(絶対平均偏差にする)/ **期待: 赤** |
| 8 | `RigidityExcludesOnlyAllFloorExportDays` | 全売り手が「床かつ輸出」の日(`offer_at_floor_with_export_count == offer_count > 0`)は有効日から外れる。**一部だけ該当する日は外れない** | 除外を `offer_at_floor_count` で書いた(**[GDD02 §8](../03-gdd/02-economy.md)-1 が「床に居るが輸出していない売り手日は除かない」と名指した穴**)/ 一部該当の日も外した(床に居ない売り手の中央値ごと落ちる) | **核心**。除外の条件を `OfferAtFloorCount == OfferCount` に変える / **期待: 赤** |
| 9 | `RigidityReadsSwitchRateAndPriceRangeIndependently` | レンジが広くてもスイッチ率が全日 `49‰` なら `8-1b` が赤。レンジが狭ければスイッチ率が高くても赤 | 2 つを `and` で結んだ(§5.2 は「または」) | |
| 10 | `SwitchRateIgnoresUndefinedDays` | `partner_switch_permille == -1` の日は母数に入らず、`-1` しか無い窓は判定不能になる | `-1` を「0‰ = 固着」と読んだ(**約定が1件も無い日が「市場機能が死んだ」ではなく「関係が固着」と報告される**) | **核心**。`partner_switch_permille >= 0` の絞り込みを落とす / **期待: 赤** |
| 11 | `ImportContentCostUsesTheWorstSeason` | 穀物の季節係数が最大の季節で `輸入含有原価 ≥ 外部買値` になる品目があれば `8-1c` が赤。年平均では成立していても赤 | 季節の1点だけを見た / 年平均で解いた。**[GDD02d §4.3](../03-gdd/02d-external-market-and-money.md) (e) が季節の最高値で評価すると書いている** | |
| 12 | `NoExportMakesTheConditionRed` | 静的条件が成立していても、窓の `export_quantity` 合計が 0 なら `8-1c` が赤 | 実行時の側を落として静的検査だけにした([GDD02 §8](../03-gdd/02-economy.md)-1 (b) は「実行時に保たれているか」を求めている) | |
| 13 | `DistressRatiosUseTheDefinitionHouseholdCount` | 世帯行を 5 件しか含まない日でも、分母が `窓の日数 × definition.HouseholdCount` になる | 分母を `households.csv` の行数から数えた(**書き漏らした日ほど割合が高く出る**) | |
| 14 | `BankruptAndInputBlockedHaveSeparateThresholds` | 割合 `150‰` のとき `8-2a` が赤・`8-2b` が緑になる(閾値 100 / 250) | 2 本を同じ閾値で判定した([GDD02 §8](../03-gdd/02-economy.md)-2 / [#30](https://github.com/stama72/visionary/issues/30) 決定4 が「**別々の**閾値」と書いている) | **核心**。`8-2b` の閾値を `BankruptHouseholdRatioPermille` に差し替える / **期待: 赤** |
| 15 | `ProductionStopIsJudgedPerOccupation` | 醸造 2 戸だけが全日停止(都市全体では 200‰)でも `8-3` が赤になる | 職業で束ねず都市合計で判定した(**[GDD02 §8](../03-gdd/02-economy.md)-3 の「醸造が成立するか」が 5 職業の平均に薄められる**) | **核心**。`8-3` の集計を職業別から都市合計へ変える / **期待: 赤** |
| 16 | `BrewerEvidenceIsAlwaysPresent` | 醸造が閾値を超えていない緑の窓でも、根拠に醸造の停止割合が出る | 「超えた職業だけ根拠に出す」実装にした | |
| 17 | `DistrictSpreadNeedsTwoDistricts` | その日の `DistrictRow` が 1 行しか無い日は有効日に入らない。2 行あれば入る | 1 区画の日を「差 0」として足した(**差が構造的に 0 の日が分子を薄め、摩擦が消えていないのに `8-4` が赤になる**) | |
| 18 | `DistrictSpreadIsIndeterminateWithOneSeller` | その品目の担い手世帯が 1 戸になった窓で `8-4` が判定不能になる(差が 0 でも赤にならない) | 担い手数を見なかった。**アドバイザーが名指した「担い手 2→1 の偽陰性」がそのまま残り、`8-3` が原因の停止が「摩擦が消えた」と報告される** | **核心**。担い手数の判定不能の枝を削る / **期待: 赤** |
| 19 | `DistrictSpreadExcludesTools` | 工具(`Item.Tools`)の区画差が 0 でも `8-4` が緑 | 工具を含めた([GDD02 §8](../03-gdd/02-economy.md)-4 が「鍛冶 2 世帯 × 1 個/日 に対して需要 1 個/日で床に張り付くと先に分かっている」ので外している) | |
| 20 | `FloorBreachIsRedOnTheFirstDay` | day 0 に `offer_min < 床` の日が1日あれば `8-5a` が赤で、`FirstRedDay == 0` | 継続日数を要求した / 過渡期を除いた(**不変条件は1日でも破れてはならない**) | |
| 21 | `BandExceededCountsAcrossWindowBoundaries` | 窓の境界をまたぐ 30 日連続で `8-5b` が赤になる。29 日では緑 | 連続日数を窓ごとにリセットした(**境界をまたぐ継続が永久に見えない**) | **核心**。連続日数のカウンタを窓の先頭でリセットする / **期待: 赤** |
| 22 | `BandExceededIsBrokenByDaysWithoutSettlement` | 29 日連続の後に `settled_count == 0` の日を挟むと、連続が切れて緑になる | 約定 0 の日を「帯を上回ったまま」と数えた | |
| 23 | `WindowPurchaseIsIndeterminateWhenTheCeilingIsNeverTested` | `offer_max ≤ 天井` が全日で、`window_settled_count` の合計が 0 の窓は**判定不能**。1日でも `offer_max > 天井` があれば赤 | 「窓口が発火していない = 赤」と書いた。**[GDD02d §4.5](../03-gdd/02d-external-market-and-money.md) が予測する健全な定常状態(全売り手が床)がそのまま赤になる** | **核心**。`8-5c` の「試された日が 0 なら判定不能」の枝を `Verdict.Red` に変える / **期待: 赤** |
| 24 | `MoneyBoundUsesTheDefinitionInitialTotal` | day 0 に既に初期の 40% まで落ちている走行で `8-6` が赤になり、`FirstRedDay == 0` | 初期貨幣総量を day 0 の `money_total` から取った。**[issue #120](https://github.com/stama72/visionary/issues/120) の「初日の1日で 84.3% が流出」が「初期値がもともとそれ」と読まれて消える** | **核心**。初期貨幣総量を `snapshot[0].Economy.MoneyTotal` から取るように変える / **期待: 赤** |
| 25 | `MoneyDecliningNeedsThreeConsecutiveWindows` | 窓末が 2 窓連続で下がるだけなら緑、3 窓なら赤 | 連続窓数を取り違えた / 1 窓で判定した(**季節の振れが「減り続けている」と読まれる**) | |
| 26 | `BoundedByDistinguishesCalibrationFromMechanism` | 帯(± 100‰)を一度も出ない窓が `calibration`、出て戻り輸出が発火した窓が `mechanism`、出たまま戻らない窓が `unknown` | 3 値を書き分けなかった。**[GDD02 §8](../03-gdd/02-economy.md)-6 が「校正で通したのか機構で通したのかを記録する」と決定として書いている** | |
| 27 | `UnknownPriceRatioSumsBeforeDividing` | 需要行が `(1 行中 1 行不明)` と `(99 行中 0 行不明)` の 2 日から成る窓で、割合が `10‰` になる(日ごとに割って平均すると `500‰`) | 日ごとに割って平均した(**需要行が少ない日が過大な重みを持ち、`8-7` が偽陽性になる**) | **核心**。`8-7` を日ごとの割合の平均に変える / **期待: 赤** |
| 28 | `FirstRedDayIsTheDayTheJudgementLands` | 日単位の条件(`8-5a`)はその日、窓単位の条件(`8-2a`)は窓の末日が `FirstRedDay` になる。緑・判定不能なら `-1` | `FirstRedDay` を窓の先頭日にした / 最後に赤くなった日で上書きした。**[TDD01 §5.2](../04-tdd/01-sim-core-and-m0.md) が「同時に赤くなったときの切り分けは赤くなった日の順序で行う」と書いており、上書きすると順序が消える** | **核心**。`FirstRedDay` を赤くなるたびに上書きする / **期待: 赤** |
| 29 | `EvidenceComesFromTheDecidingWindow` | 窓 1 で赤、窓 2 で判定不能になる走行で、根拠の値が窓 1 のものになる | 最後の窓の根拠を無条件に出した(**経済が死んだ後の窓の値が、赤の根拠として並ぶ**) | |
| 30 | `OverallFoldsRedOverIndeterminateOverGreen` | 3 シードが (赤, 緑, 緑) なら赤、(緑, 判定不能, 緑) なら判定不能、(緑, 緑, 緑) なら緑。`redSeeds` / `indeterminateSeeds` がシード昇順 | 判定不能を緑へ倒した(**1本で判定できなかったことが消える**)/ 畳む優先順位を取り違えた | **核心**。`RunSummaryBuilder` の判定不能の枝を `Verdict.Green` に変える / **期待: 赤** |
| 31 | `SeedOrderFollowsTheConfig` | `masterSeeds` が `[3, 1, 2]` のとき `seeds` 配列もその順 | `Build` の中で昇順に並べ直した(**設定と出力の対応が崩れる**) | |
| 32 | `SummaryIsByteIdenticalAcrossRuns` | 同じ設定で `vsim run` を 2 回走らせ、`summary.json` がバイト一致する。**(i)** 負号・小数点の表記が異なるカルチャを現在のカルチャに設定した実行とも一致し、**(ii)** 出力バイトに `\r\n` が現れない | `InvariantCulture` を落とした / 改行の正規化を落とした。**W2-20 別表 #21' と同じ罠で、`Runner` は `DeterminismConventionTests` の守備範囲外である。(ii) は Ubuntu では原理的に赤にならない**(既定の改行が `\n`)ので、守っているのは開発機(Windows)で走らせたときだけである | |
| 33 | `SummaryKeepsJapaneseAsUtf8` | `summary.json` のバイト列に `\u` で始まるエスケープが現れない | `UnsafeRelaxedJsonEscaping` を落とした(読めない JSON になる。**判定を人が読むための出力である**) | |
| 34 | `VerdictIsSerializedInLowerCase` | `summary.json` の `verdict` が `"green"` / `"red"` / `"indeterminate"` になる | 既定の enum 変換に任せた(`"Green"`)。**下流(人の目と、後続の集計スクリプト)が名前で引く** | |
| 35 | `SummaryLivesAtTheRunRootNotUnderTheSeed` | `vsim run --out <dir>` の後、`<dir>/summary.json` が存在し、`<dir>/<シード>/summary.json` が存在しない | 置き場所を取り違えた([TDD01 §4.1](../04-tdd/01-sim-core-and-m0.md)) | |
| 36 | `AccumulatorRejectsOutOfOrderDays` | day が飛んだ / 戻った `DailySnapshot` を流すと `ArgumentException` | 黙って進んだ(**窓が静かにずれ、判定は出るが物差しが違う**) | |
| 37 | `AccumulatorRejectsASecondBuild` | `Build` を 2 回呼ぶと `InvalidOperationException` | 2 回目に別の値を返した / 同じ値を返した(どちらも契約が定まっていない状態を許す) | |
| 38 | `CsvOutputIsUnchangedByTheAccumulator` | `CompositeDailyMetricsSink` を通した走行の 5 つの CSV が、`CsvMetricsSink` 単体の走行とバイト一致する | 合成の途中で `DailySnapshot` を書き換えた / 順序を入れ替えた。**本タスクは収集側に触らないという宣言の、唯一の機械である** | **核心**。`CompositeDailyMetricsSink` の呼び出し順を逆にしたうえで `VerificationAccumulator` に `snapshot.Prices` の要素を書き換えさせる / **期待: 赤** |
| 39 | `LongRunStillFinishesWithinTheBudget` | `configs/m0-w2-baseline.json`(10 シード × 36,000日)が 600 秒以内に終わり、`summary.json` が出る | 判定が日数に比例しない仕事をしている(全日の行を溜めた)。**W2-20 のテスト #20 と同じく、着手時点の経済は 20〜30 日で止まるので空振りに近い** — この注記をテストの doc コメントにも書くこと | |

> **「核心」印は 16 件で、[02-task-spec](../process/02-task-spec.md) の想定(2〜3件)より多い。** 判定は 12 項目 × 三値 = 36 の枝を持ち、**そのほとんどが「外しても他のテストが緑のまま通る」独立な分岐**だからである。16 件の内訳は、**三値の縮退 5**(#3 判定不能→緑 / #23 判定不能→赤 / #30 畳み込み / #14 閾値の共有 / #18 担い手数の枝)、**物差しの取り違え 3**(#5 床 / #24 初期貨幣総量 / #7 相対化)、**集計の単位 3**(#8 除外 / #15 職業別 / #27 先に足す)、**順序と境界 2**(#21 窓またぎ / #28 `FirstRedDay`)、**母数の取り違え 1**(#10 未定義日)、**適用範囲 1**(#1 過渡期)、**収集側への非干渉 1**(#38)である。**どれも「赤が出る」ので一見動いて見える** — 判定関数の誤りは、値が出ないのではなく**違う値が出る**形で現れる。
>
> **初出はこの段落を「13 件」と書き、内訳から #1・#10・#18 を落としていた(フェーズ2 が訂正)。** 表の印が実体である。

## 別表(レビューで追加)

レビュー1巡目の指摘(issue #209)を修正するにあたって足した4本。列は上の表と同じ。

| # | テスト | 検証内容 | この実装ミスで落ちる |
| - | ------ | -------- | -------------------- |
| 40 | `SwitchRateBranchIsNotRedWhenOnlyOneDayIsBelowTheFloor` | `8-1b` のスイッチ率は「`partner_switch_permille ≥ 0` の全日で `< 50‰`」が赤の条件(TDD01 §5.2)。有効日のうち1日だけ 49‰(閾値未満)で残りが 500‰(健全)の窓は、スイッチ率の枝では赤にならない | 判定を窓最小値で書いた(1日でも閾値未満なら赤になり、全日条件にならない) |
| 41 | `RigidityRangeEvidenceIsExpressedInPermille` | `8-1b` のレンジの根拠 `Value` が床に対する‰であり、同じ根拠の `Threshold`(40)と「値 < 閾値 → 赤」の向きで読める | `Value` に貨幣単位の絶対値を置いた(‰への変換を忘れた) |
| 42 | `DistrictSpreadEvidenceIsExpressedInPermille` | `8-4` の区画差の窓平均の根拠 `Value` が床に対する‰であり、同じ根拠の `Threshold`(20)と「値 < 閾値 → 赤」の向きで読める | `Value` に貨幣単位の絶対値を置いた(‰への変換を忘れた) |
| 43 | `MoneyBoundedKeepsWindowEvidenceWhenDayLevelDecides` | 日次の帯(`8-6`)が窓より先に赤くなる走行でも、根拠に窓ぶんの6件(`boundedBy` を含む)が出る | 日次で赤になった場合の根拠を `moneyTotal` 1件だけにした(窓ぶんの根拠が丸ごと落ちる) |

## 別表(フェーズ1 の訂正 — `SPEC-OUTSIDE` の差し戻し)

**1巡目の指摘 #2 が [TDD01 §5.2](../04-tdd/01-sim-core-and-m0.md) の欠陥を当てた。** `8-1a` の偏差の枝が「最後の窓 ≥ 最初の窓 × 2」としか書いておらず、**最初の窓の偏差‰ が 0 のとき `0 ≥ 0 × 2` が自明に真になる** — [GDD02d §4.5](../03-gdd/02d-external-market-and-money.md) が予測する健全な定常状態(全売り手が床に張り付き、中央値が一定)がそのまま「発散」と報告される。§5.2 を訂正し、**基準を「偏差‰ が 0 でない最初の窓」に置いた。** あわせて §5.2 に無かった2つを足した(implementer が「決めて報告」で埋めていた穴である)— **畳み方はどの階層でも「赤 > 判定不能 > 緑」**、**畳む階層は 枝・品目/職業・窓 の3つ**。

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心(当てる変異 / 期待) |
| - | ------ | -------- | -------------------- | ------------------------- |
| 44 | `DispersionIsGreenWhenDispersionNeverAppears` | 全窓で偏差‰ = 0(全有効日の中央値が一定)の 390 日以上の走行で `8-1a` が**緑**。赤でも判定不能でもない | 条文どおり `最後 ≥ 最初 × 2` を当てた。**`SPEC-OUTSIDE` で差し戻した欠陥そのもの**で、予測どおりの健全な定常状態が発散と報告される | **核心**。基準窓の判定を「偏差‰ > 0 の最初の窓」から「最初の窓」へ戻す / **期待: 赤** |
| 45 | `DispersionFiresAfterLeavingTheFloor` | 偏差‰ が窓ごとに `0, 0, 300, 700, 1500` と並ぶ走行で `8-1a` が**赤**、`FirstRedDay` が最後の窓の末日 | 「最初の窓が 0 ならこの枝を諦める」実装にした。**床への張り付きから離脱して発散する経路がまさにこの形であり、走行の残り全部で検出器が黙る** | **核心**。基準窓を先頭窓に固定し、0 なら緑で打ち切る / **期待: 赤** |
| 46 | `DispersionNeedsAbsoluteDispersionToo` | 偏差‰ が `1, 2, 99` の走行は緑(単調・3窓・最後 ≥ 基準 × 2 だが 100‰ 未満)、`1, 2, 100` は赤 | 散らばりの下限(`DispersionFloorPermille`)を落とした。**中央値が窓平均から平均して 0.2% ずれているだけの窓が「発散」になる** | |
| 6' | `DispersionGrowthNeedsThreeWindows`(**上の #6 を訂正**) | 「3 窓」は走行の窓数ではなく**基準窓から最後の窓まで**(基準窓を含む)の窓数である。偏差‰ が `0, 300, 300, 600` の走行(4窓・基準窓以降3窓)は判定でき、`0, 0, 0, 300, 700`(5窓・基準窓以降2窓)は偏差の枝が判定不能になる | 窓数を走行の先頭から数えた | |
| 47 | `IndeterminateBranchDoesNotFoldToGreen` | 帯の枝が緑・偏差の枝が判定不能(基準窓以降が2窓)の走行で、`8-1a` が**判定不能**になる | 枝の畳み込みで判定不能を緑へ倒した。**1巡目 I-a の指摘がこれで、当時は凍結仕様の側(テスト #1・#4・#5)が誤っていた** | **核心**。枝の畳み込みで判定不能を緑として扱う / **期待: 赤** |
| 48 | `ItemsFoldAcrossGoodsWithinAWindow` | 5品目のうち1品目だけが赤の窓で `8-1a` が赤。1品目だけが判定不能で残りが緑なら判定不能 | 品目間の畳み込みを書かなかった / 最後の品目で上書きした。**§5.2 が階層を3つと定めるまで、implementer の「決めて報告」で埋まっていた** | |
| 1' 4' 5' | 上の **#1・#4・#5 を訂正** | 検証内容は変えない。**走行を 390 日以上(過渡期 30 + 窓 3 つ)にする。** 1窓の走行では偏差の枝が判定不能になり、上の畳み方によって `8-1a` の項目判定が恒久的に判定不能になるので、**項目の `Verdict` で緑/赤を読めない** | — | |

> **6' の数値例はフェーズ2 が訂正した(象限I-b)。** 初出は「判定できる」側を `0, 0, 300, 700` としていたが、基準窓は3窓目(値 300)なので基準窓以降は **2窓**であり、同じ行が「判定不能」と書く `0, 0, 0, 300, 700`(同じく2窓)と構造が同一だった。[TDD01 §5.2](../04-tdd/01-sim-core-and-m0.md)「基準窓から最後の窓までが 3 窓未満」は基準窓を含む区間なので一意に読める。**訂正先は別表の数値例だけで、§5.2 の条文には及ばない**(だから `SPEC-OUTSIDE` ではない)。

> **「核心」印は 16 件から 19 件になった。** 足した3件はいずれも**偏差の枝の三値の縮退**である(#44 健全な定常状態が赤 / #45 検出器の恒久停止 / #47 判定不能が緑へ倒れる)。**3件とも「赤が出る / 出ない」が静かに入れ替わる形で、走らせても止まらない。**

## 別表(レビュー2巡目で追加)

**2巡目の指摘は、1巡目と同じ「三値の縮退」が別の階層で出たものである。** 1巡目が当てたのは**枝**の階層、2巡目が当てたのは**窓**の階層で、`FoldRedOverIndeterminateOverGreen`(枝)と `Pool`(品目・職業)は [TDD01 §5.2](../04-tdd/01-sim-core-and-m0.md) の「赤 > 判定不能 > 緑」に従っていたのに、`ResolveFold`(窓)だけが旧条文(「**すべて**判定不能なら判定不能」)のまま残っていた。**旧条文は `39cc393` がフェーズ1 として §5.2 から削除したものである** — 実装の doc コメントがそれを §5.2 からの引用として引き続けていた。

| # | テスト | 検証内容 | この実装ミスで落ちる |
| - | ------ | -------- | -------------------- |
| 49 | `WindowFoldKeepsIndeterminateOverGreen` | 緑の窓と判定不能の窓が混在する走行で、窓の階層を持つ項目(`8-1b` / `8-4` / `8-5b` / `8-5c`)が**判定不能**になる。緑にならない | 窓の階層だけ「すべて判定不能なら判定不能」で畳んだ。**経済が day 150 で死ぬ 36,000 日の走行が、窓1が緑だったというだけで「価格は硬直していない」「帯超えは無い」「区画差は消えていない」と報告される** |
| 50 | `WindowFoldKeepsRedOverIndeterminate` | 赤の窓・判定不能の窓・緑の窓が混在する走行で、同じ4項目が**赤**になり、`FirstRedDay` が赤くなった窓の末日になる | 判定不能を赤より優先した(順位を取り違えた) |
| 44' | `DispersionIsGreenWhenDispersionNeverAppears`(**上の #44 を訂正**) | 検証内容は変えない。**入力を実際に「全品目・全窓で偏差‰ = 0」にする。** 初出は既定のビルダ(`MedianFor` が `(床 × 2) + (day % period)` を返す)をそのまま使っており、どの品目のどの窓でも偏差‰ ≥ 1 になって**基準窓が窓1で必ず立っていた** — 緑だったのは「基準窓が存在しない」からではなく、条件 (2)(最後の窓の偏差‰ ≥ 100)が効いていたからである | — |

> **44' が塞いだのは、テストが緑でありながら枝を1本も通っていない状態である。** 訂正前は `ResolveDispersion` の「基準窓が存在しない → 緑」を `Verdict.Indeterminate` へ書き換えても 38 本が全て緑のまま通った。**[GDD02d §4.5](../03-gdd/02d-external-market-and-money.md) が予測する「全売り手が床に張り付く定常状態」が「判定不能」になる**形で、`SPEC-OUTSIDE` の差し戻しが塞いだ偽陽性が符号を変えて戻る経路である。

## 別表(レビュー3巡目 — 網羅パスで追加)

**3巡目は探索をやめ、[01-review](../process/01-review.md) の網羅パスに充てた。** 1巡目(枝の階層)と2巡目(窓の階層)の象限I がどちらも「三値の縮退」で、**出所に列挙可能な境界**(12 項目 × 畳み込みの階層)が見えたためである。12 項目 × 4 階層(枝 / 品目・職業 / 窓 / シード横断)のうち**実在するセルは 30、分離できるカバレッジが無かったのは 6 セル**だった。**欠落は1点に集まっていた — 「赤 > 判定不能」の順序が、窓の階層と `8-1a` の枝・品目の階層でしか測られていない。**

`51` だけが実装の欠陥で、残りはテストの穴である。

| # | テスト | 検証内容 | この実装ミスで落ちる |
| - | ------ | -------- | -------------------- |
| 51 | `MoneyBoundedIsIndeterminateWithoutWindows` | 149 日の走行で `money_total` が全日帯の中に居るとき `8-6` が**判定不能**になる(緑にならない)。day 0 に帯を割る走行なら 149 日でも赤(赤 > 判定不能) | **実装の欠陥。** 窓が 0 の走行で「連続3窓」の枝が一度も評価されていないのに緑を返した。同じ「判定不能: 無し」と書かれた `8-1c` / `8-2a` / `8-2b` は `WindowsSeen == 0` を通って判定不能になるのに、**`8-6` だけが緑へ倒れていた。** 短い試走で `summary.json` が「貨幣総量は有界」と読める |
| 52 | `BandBranchWindowFoldIsObservable` | 510 日(過渡期 30 + 窓 4 つ)で、ある品目の窓1 だけ有効日 29・窓2〜4 は健全。偏差の枝は基準窓 = 窓2 で3窓そろって緑になり、**帯の枝だけが判定不能**で `8-1a` が判定不能になる | **「核心」印 #3 の変異が、この入力を足すまで落ちなかった。** `8-1a` を読むどのテストでも判定不能が偏差の枝からも出ており、**帯の枝の窓の階層に観測手段が無かった** |
| 53 | `PoolKeepsRedOverIndeterminateWithinAWindow` | **同じ窓の中で**ある品目/職業が赤・別が判定不能のとき、`8-1b` / `8-3` / `8-4` / `8-5b` が**赤**になる | `Pool`(品目・職業の階層)で判定不能を赤より先に返した。#50 は判定不能を窓1・赤を窓2 に置いているので**窓の階層しか試していない。** 赤が消える向きなので、**発火していた検出器が「まだ確かめられていない」と読まれて黙る** |
| 54 | `ProductionStopIsIndeterminateWithoutAnyHousehold` | 窓の全日で `snapshot.Households` が空の走行で `8-3` が判定不能 | [TDD01 §5.2](../04-tdd/01-sim-core-and-m0.md) の「全職業が 0 戸なら判定不能」に入力が無かった(#13 は世帯行を 5 件に減らすだけ) |
| 55 | `OverallKeepsRedOverIndeterminateAcrossSeeds` | `(赤, 判定不能, 緑)` のシードの組で `overall` が**赤**になり、`redSeeds` / `indeterminateSeeds` の双方が埋まる | シード横断の「赤 > 判定不能」に入力が無かった。#30 の赤ケースは `(赤, 緑, 緑)` で、測っていたのは「判定不能 > 緑」だけ。**10 シードのうち1本が赤・1本が判定不能は実走行でいちばん出やすい組み合わせである** |
| 56 | `UnknownPriceRatioIsIndeterminateWithoutDemandLines` | 窓の全日で需要行が 0 の走行で `8-7` が判定不能 | §5.2 が `8-7` に定めた唯一の判定不能条件に入力が無かった(#27 は day 100/101 に需要行を残しているので分母が 100) |

> **網羅パスが挙げた7セル目(`ResolveDispersion` の「窓平均が 0 → 判定不能」)は、テストを足さないと決めた。** implementer が**構成不可能**であることを示した: `min ≤ 平均` は恒等式で、`WorldDefinition` は都市生産品の床を 1 以上に強制する。したがって窓平均が 0 以下になる入力では同じ窓の `min` も 0 以下になり、**帯の枝の下限(`min × 10 < 床`)が同時に真になって赤が確定する** — 赤 > 判定不能なので、この枝を緑へ書き換えても項目の判定は動かない。**分岐は防御的に残っているが観測できない**ので、これは issue へ落とす(フェーズ3)。

## 編集してよい文書

worktree をまたいだ所有権の宣言。**ここに挙がっていない文書は触らない。**

- `docs/tasks/W2-21-verification-and-summary.md` — レビューで足したテストの**別表**だけ(上の表そのものは書き換えない)
- `docs/tasks/W2-21-verification-and-summary.handoff.md` — 引き継ぎメモ

**`docs/04-tdd/01-sim-core-and-m0.md` と `docs/03-gdd/02-economy.md` は触らない。** §5.2 / §4.2 / §9.2 はフェーズ1 が既に更新済みである(この仕様と同じコミット)。**フェーズ2 が触れば `SPEC-OUTSIDE` で止まる**(機械が差分で見ている)。実装が §5.2 と食い違うなら、それは象限I-b であり、フェーズ1 へ戻す案件である。

**GDD の他の分冊も触らない。** 触る必要が出たら `SPEC-OUTSIDE` である。

## このタスクで特に効く規約

- **割合は千分率(‰)の整数。除算は `IntegerMath` の切り上げヘルパー経由。** 比較は可能なかぎり**両辺に分母を掛けて除算そのものを消す**(上記「4.」)— 除算を挟むほど丸めの向きが判定の境界に現れる
- **`Runner` 側で `double` を使わない。** `DeterminismConventionTests` が見ているのは `Visionary.Sim` だけなので、**`summary.json` の整形で比率を作ると機械に止められない**
- **列挙順。** 項目は上記 12 本の順、シードは設定の順、`RedSeeds` はシード昇順、根拠は品目 Id 昇順・職業 Id 昇順。`Dictionary` の列挙結果を並びに使わない
- **判定は `World` を読まない。** 入力は `DailySnapshot` と `WorldDefinition` だけである(`WorldDefinition` は不変)

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **「核心」印の 19 件の変異を `mutator` が実測し(レビューの巡が閉じた後)、結果を doc コメントへ転記した**(16 件 + 別表(フェーズ1 の訂正)の3件)
- [ ] `vsim run --config configs/m0-w2-baseline.json --out <dir>` が終わり、`<dir>/summary.json` に 12 項目の判定・根拠の値・使った閾値・母数が出る(**赤が並ぶのが正しい**)
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
