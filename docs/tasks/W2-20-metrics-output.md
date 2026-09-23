# W2-20: Metrics の収集と日次メトリクスの出力

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#41](https://github.com/stama72/visionary/issues/41)                |
| 根拠     | [TDD01 §3.3](../04-tdd/01-sim-core-and-m0.md)(順10)/ [§3.8](../04-tdd/01-sim-core-and-m0.md)(ハッシュの契約)/ [§4.1](../04-tdd/01-sim-core-and-m0.md)(CLI)/ [§4.2](../04-tdd/01-sim-core-and-m0.md)(メトリクス出力)/ [GDD02 §8](../03-gdd/02-economy.md) |
| ブランチ | `feat/41-metrics-and-verification`                                   |
| worktree | `.claude/worktrees/41-metrics-and-verification/`                     |

**本タスクは計器を作る。校正はしない。** 着手時点の master(`0a6961f`)では、貨幣総量が 20〜30 日で初期 24,000 の 1〜7% へ落ち、生産が止まり、都市内の約定が消える([#41 の決定ログ](https://github.com/stama72/visionary/issues/41#issuecomment-5796332682)の実測表)。**出力が「経済が死んでいる」という数字になるのは正しい動作である。** 直すのは後続の issue であり、本タスクの完了条件に入れない。

## スコープ

**含まない:**

- **[GDD02 §8](../03-gdd/02-economy.md) の7項目の判定関数・閾値・`summary.json`・[TDD01 §5.2](../04-tdd/01-sim-core-and-m0.md) の初期価格比** — [#209](https://github.com/stama72/visionary/issues/209)。本タスクが出すのは**列**であって判定ではない
- `trust.csv` / `events.jsonl`(W3・W4)
- `vsim promise-table` / `vsim dialogue-sample`(W3・W5)
- 設定ファイルの `features` / `coefficients`(W4。下記「`vsim run`」のとおり、空でなければエラーにする)
- **経済の校正**(外部価格・初期資金・レシピ数量のどれも動かさない)

**変えない既存コード**(規則8。[02-task-spec](../process/02-task-spec.md)「変えないと宣言する既存コード」)。単位はファイルではなく規則:

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `TradeSettlement.ExecuteImport` が買い手に `CounterpartyId = ExternalMarketSellerId` の `Purchase` 行を立てる | [GDD02d §2.2](../03-gdd/02d-external-market-and-money.md) | **一致**(読んで突き合わせた。輸入額の母数はこの行である) |
| `TradeSettlement.ExecuteExport` が売り手に `CounterpartyId = ExternalMarketSellerId` の `Sale` 行を立てる | [GDD02d §2.3](../03-gdd/02d-external-market-and-money.md) | **一致**(輸出額の母数はこの行である) |
| `HouseholdState.UnaffordableNecessityCount` を段5b が2経路で加算する(現金上限のゲート / 資金上限の切り詰め) | [GDD02b §3.2](../03-gdd/02b-consumption-and-household.md) | **一致**(`TradeSystem.TryPurchaseLine` の2箇所。(b) はこれを `DemandPurpose.ProductionInput` へ写す形で足す) |
| `OfferPrice.Calculate` の「売れなかった日は値上げしない」(`UnsoldCapPermille = 1000`、破産中は 500‰ 固定が先) | [GDD02c §1.1](../03-gdd/02c-price-and-budget.md) | **一致**(頭打ちの計数はこの定数を**再定義せず**、下記の述語を同じクラスに足して読む) |
| `MarketReference.TrySeller` の「他の売り手の観測が0件なら false、自分の約定単価も使わない」 | [GDD02c §1.2](../03-gdd/02c-price-and-budget.md) | **一致**(「相場基準を持たない売り手日」はこの `false` そのものである) |
| `BuyerBudget.WearCostPerRun(工具の移動平均単価, 所要労働‰, N)` | [GDD02a §5](../03-gdd/02a-production.md) | **一致**(式が節と一字一致。利潤の原価はこれを**呼ぶ**。式を写さない) |
| `HouseholdSystem` が破産中フラグを**前日の**購入結果から立てる(順3) | [GDD02b §3.3](../03-gdd/02b-consumption-and-household.md) | **未確認**(本タスクは値を読むだけで書かない。列の意味が1日遅れることは下記「(a) と (b) の時点のずれ」に明記する) |
| `SellableStock.Of` の留保(設備・入力) | [GDD02c §1.3](../03-gdd/02c-price-and-budget.md) | **未確認**(`households.csv` の `sellable_stock_output` はこの関数を**呼ぶ**。自前で工房在庫を引き算しない) |
| `BuyerDemand.Build` が需要の行を**在庫の過不足に関係なく**立てる(`ExpectedStock ≥ TargetStock` の行も作る) | [GDD02b §5.2](../03-gdd/02b-consumption-and-household.md) | **一致**(読んで突き合わせた。§8-7 の分母はこのため `ExpectedStock < TargetStock` で絞る。下記) |

## 作るもの

### 1. `World` の区分 `Metrics`(ハッシュ対象外)

**順5 の段1・段4・段5b の瞬間の値は、順10 のスナップショットからは復元できない。** 置き場所は `HouseholdState` の欄ではなく **`World` の区分1つ**とする([TDD01 §4.2](../04-tdd/01-sim-core-and-m0.md) が挙げた2択のうち後者。[決定ログ](https://github.com/stama72/visionary/issues/41#issuecomment-5796332682)決めて報告2)。

```csharp
namespace Visionary.Sim.Metrics;

/// <summary>当日ぶんの計数(TDD01 §4.2)。ハッシュに含めない(§3.8)。</summary>
public sealed class MetricsScratch
{
    public MetricsScratch(int householdCount, int itemCount);

    // 段1 が世帯 Id 昇順に毎日書く。添字 = 世帯 Id。
    public int[] SellerHasNoReference { get; }        // 相場基準が立たなかった: 1 / 立った: 0
    public int[] SellerCoefficientCapped { get; }     // §1.1 の頭打ちが実際に効いた: 1

    // 段4 が書く。添字 = 世帯 Id。
    public int[] DemandLines { get; }                 // ExpectedStock < TargetStock の行の数
    public int[] DemandLinesWithoutKnownPrice { get; }// うち HasMarketTerm == false の行の数

    // 段4 と段5b が書く(どちらも 1 を代入する。加算しない)。添字 = 世帯 Id。
    public int[] InputBlockedByFunds { get; }         // GDD02 §8-2 (b)。世帯日の 0/1

    // 段5b が書く。添字 = 世帯 Id * itemCount + itemId。前日の取引相手(-1 = 買っていない)。
    // 当日ぶんの Reset では消さない唯一の欄である(スイッチ率が前日と比べるため)。
    public int[] PreviousCounterpartyId { get; }
    public int[] CurrentCounterpartyId { get; }

    /// <summary>当日ぶんを初期化する。<b>段1 の先頭で呼ぶ</b>(順5 が唯一の書き手)。</summary>
    /// <remarks><c>CurrentCounterpartyId</c> を <c>PreviousCounterpartyId</c> へ移してから
    /// -1 で埋める。他の欄は 0 で埋める。</remarks>
    public void BeginDay();
}
```

- **`World` に `public MetricsScratch Metrics { get; }` を足す。** `World` のコンストラクタで `householdCount` / `itemCount` から作る
- **`StateHasher.Compute` はこの区分を読まない。** [TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md) の「含めない」列がこれを名指している。`StateHasherCoverageTests.ExpectedWorldSections` に `"Metrics"` を足す(**区分タグは増やさない** — ハッシュしないので `Section` の enum には現れない)
- **`-1` は「値なし」である。** 取引相手 Id は非負(窓口は `int.MaxValue`)なので、`-1` と衝突しない

> **残る穴**: `StateHasherCoverageTests` が凍結しているのは **`World` の区分の一覧**と、**ハッシュ対象の要素型の欄**である。`MetricsScratch` はどちらでもないので、**この区分に欄を足しても機械は何も言わない。** 強制が働くのは区分を足す**この1回だけ**である。`HouseholdState` の欄にすれば欄ごとに強制が掛かるが、下記の理由で採らない。

> **なぜ `HouseholdState` の欄にしないか。** 欄なら6本増え、[TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md) の除外の理由を**欄ごとに**書くことになる。区分1つなら「この区分は誰も読まない」1本で済み、`StateHasherCoverageTests` の凍結一覧にも1行しか現れない。**代償は、`HouseholdState` を見ただけでは当日ぶんの計数が全部そこにあると読めなくなることである** — 既存の `UnaffordableNecessityCount` / `ProductionRuns` / `UnfilledPurchase` は欄のままなので、当日ぶんの計数が2か所に分かれる。分けているのは「誰かが読むか」であり、`MetricsScratch` は順10 だけが読む。

### 2. `OfferPrice` に頭打ちの述語を足す

```csharp
/// <summary>その日、§1.1 の「売れなかった日は値上げしない」が実際に効いたか。</summary>
public static bool WasUnsoldCapApplied(
    bool hasReference, int sellableStock, int shipmentTargetStock, int isBankrupt, bool hasSettledYesterday);
```

- **`UnsoldCapPermille` を再定義しない。** 同じクラスに置くのは、`1000` を呼び出し側へ写すと片方だけ動かせてしまうためである([GDD02c §1.1](../03-gdd/02c-price-and-budget.md) の値は調整対象)
- **相場基準が立たない日(`hasReference == false`)は `false` を返す**(レビュー1巡目 I-b の訂正)。その日、段1 は `OfferPrice.Calculate` を**呼ばず**に床をそのまま提示価格にするので([GDD02c §1.2](../03-gdd/02c-price-and-budget.md)、`Calculate` の doc「相場基準が立たない日は呼ばない」)、**価格係数‰ はそもそも算出されておらず、§1.1 の頭打ちは評価すらされていない。** 在庫比から係数を再計算して 1 を立てると、「盲目(相場基準なし)」の売り手日が「ラチェットの停止(頭打ち)」に混入し、[TDD01 §4.2](../04-tdd/01-sim-core-and-m0.md) がこの2列に求めた切り分け([GDD02 §8-4](../03-gdd/02-economy.md) が 0 へ収束したときの原因の切り分け)が成立しない
- **破産中(`isBankrupt != 0`)は `false` を返す。** 500‰ の固定が先に効くので頭打ちは何もしない(`Calculate` の既存の分岐と同じ順序)
- **`hasSettledYesterday` が真なら `false`**
- **在庫比から出した価格係数‰ が 1000 以下なら `false`**(`min` が実際には切っていない)

### 3. 順10 `MetricsSystem`

```csharp
namespace Visionary.Sim.Systems;

public sealed class MetricsSystem : ISimSystem
{
    public MetricsSystem(WorldDefinition definition, IDailyMetricsSink sink);

    public RandomStream Stream => RandomStream.Metrics;   // 13
    public Cadence Cadence => Cadence.Daily(hour: 0);     // 他のシステムと同じ
    public void Step(World world, SimContext context);    // ISimSystem の現行のシグネチャ
}
```

- **`RandomStream` に `Metrics = 13` を足す。** **12 は `OpportunityCost` の予約であり、飛ばす**([TDD01 §3.3](../04-tdd/01-sim-core-and-m0.md) の囲み「値は末尾に足す。順序に合わせて振り直してはならない」)。乱数は引かないが、登録に一意の系統が要る
- **`Step` は `World` を一切書き換えない。** 読むのは `Households` / `Market` / `Ledgers` / `Knowledge` / `Metrics` / `Now` だけである
- **帳簿は末尾から読み、当日の行を過ぎたら打ち切る。** 先頭から走査すると 36,000日 で日数の2乗になる(下記「5. 性能」と同じ理由)

```csharp
namespace Visionary.Sim.Metrics;

public interface IDailyMetricsSink
{
    void Write(in DailySnapshot snapshot);
}
```

**`DailySnapshot` は1日ぶんを持ち、書き終えたら捨てる。** 走行全体を溜めない(36,000日 × 5 ファイルぶんを持つと数百 MB になる)。

### 4. 出力の列

**すべて整数。割合は千分率(‰)の整数で、計算は `Visionary.Sim` 側で行う。** `Visionary.Sim.Runner` は整形だけをする — **比率を `double` で出すと [ADR-0002](../adr/0002-time-model-and-determinism.md) の規約が `Runner` 側で静かに破れる**(`DeterminismConventionTests` は `Visionary.Sim` しか見ていない)。

**値が定義できない欄は `-1` を書く**(0 と区別する。中央値・割合の分母が 0 の日)。

#### `economy.csv`(日次1行)

| 列 | 意味 | 出所 |
| -- | ---- | ---- |
| `day` | 日次(`GameDate.FromTick(world.Now).DayIndex`) | |
| `season` | 季節(enum の整数) | [ADR-0003](../adr/0003-calendar-structure.md) |
| `money_total` | **貨幣総量** = Σ 世帯の流動資金 | [GDD02 §8](../03-gdd/02-economy.md)-6 |
| `bankrupt_households` | Σ `IsBankrupt` | §8-2 **(a)** |
| `necessity_blocked_households` | 当日 `UnaffordableNecessityCount > 0` の世帯数 | 下記「(a) と (b) の時点のずれ」 |
| `necessity_blocked_count` | Σ `UnaffordableNecessityCount`(件数) | [GDD02b §3.2](../03-gdd/02b-consumption-and-household.md) |
| `input_blocked_households` | Σ `Metrics.InputBlockedByFunds` | §8-2 **(b)** |
| `export_value` / `export_quantity` | 当日の `Sale` かつ相手 = 予約 Id の Σ(単価 × 数量)/ Σ 数量 | [GDD02d §4.1](../03-gdd/02d-external-market-and-money.md) |
| `import_value` / `import_quantity` | 当日の `Purchase` かつ相手 = 予約 Id の同上 | 同上 |
| `seller_days` | 当日 `Market` に売り注文が載っている (売り手 × 品目) の数 | §8-1 |
| `seller_days_without_reference` | うち `Metrics.SellerHasNoReference` が 1 のもの | [GDD02c §1.2](../03-gdd/02c-price-and-budget.md) |
| `seller_days_coefficient_capped` | うち `Metrics.SellerCoefficientCapped` が 1 のもの | [GDD02c §1.1](../03-gdd/02c-price-and-budget.md) |
| `seller_days_at_floor` | うち提示価格が床(当日の外部買値)に一致するもの | §8-1 |
| `seller_days_at_floor_with_export` | うち**その日に輸出が発火した**もの | §8-1(硬直の除外はこれだけ) |
| `production_stopped_households` | `ProductionRuns == 0` の世帯数 | §8-3(**(b) と混ぜない**) |
| `production_runs_total` | Σ `ProductionRuns` | |
| `demand_lines` / `demand_lines_without_known_price` | Σ `Metrics.DemandLines` / Σ `Metrics.DemandLinesWithoutKnownPrice` | §8-7 |
| `households_without_known_price` | `DemandLinesWithoutKnownPrice > 0` の世帯数 | §8-7 |
| `profit_total` | Σ 世帯の日次利潤(`households.csv` の合計) | [GDD02a §5](../03-gdd/02a-production.md) |

**`seller_days` 系の分母は「その日に売り注文を出した (売り手 × 品目)」である。** 販売在庫 0 で出品しなかった世帯は分母に入らない — `Metrics.SellerHasNoReference` は段1 が**全世帯について**書く(輸出の閾在庫が読むため、販売在庫 0 でも相場基準は求まる)ので、**分母を `Market` の側から取らないと、出品していない売り手が割合を押し上げる。**

##### (a) と (b) の時点のずれ(**保証と、残る穴**)

- **`bankrupt_households` は前日の購入結果である。** 破産中フラグを立てるのは順3 で、購入が起きるのは順5 だからである([GDD02b §3.3](../03-gdd/02b-consumption-and-household.md))。**`input_blocked_households`(b) は当日である。** 同じ行に並ぶ2つの列の時点が1日ずれる
- **これを揃えない。** 揃えると、フラグの定義([GDD02b §3.3](../03-gdd/02b-consumption-and-household.md)「評価するのは前日の購入結果である」)から離れる
- **代わりに `necessity_blocked_households` を同じ行に置く。** これは当日の `UnaffordableNecessityCount` から数えるので (b) と時点が揃い、**(a) と (b) を同じ日で比べたい読み手はこちらを使う。** §8-2 (a) の判定([#209](https://github.com/stama72/visionary/issues/209))が読むのは `bankrupt_households` のほうである
- **残る穴**: `necessity_blocked_households` と `bankrupt_households` は、日をまたいで完全には一致しない。順3 と順5 の間に順4 しか無いので通常は1日ずれた同じ値だが、**④の職業付け替え([GDD02b §4.2](../03-gdd/02b-consumption-and-household.md))が走った世帯では一致しない。** 揃うことを前提にした判定を書かない

#### `prices.csv`(日次 × 品目。**全9品目・全日**)

`day`, `item_id`,
`settled_median`, `settled_min`, `settled_max`, `settled_count`, `settled_quantity`, `settled_value`,
`window_settled_count`, `window_settled_quantity`, `window_settled_value`,
`offer_min`, `offer_median`, `offer_max`, `offer_count`, `offer_at_floor_count`, `offer_at_floor_with_export_count`,
`external_buy_price`, `external_sell_price`

- **約定の母数は `Purchase` の行だけである**(都市内の約定は買い手と売り手が1行ずつ記帳するので、両方数えると2倍になる)。**窓口からの輸入もここに入る** — `window_settled_*` はその内数である。**この2つの列で「窓口からの購入が約定に占める割合」が品目別に出る**([GDD02 §8](../03-gdd/02-economy.md)-6 の切り分け)
- **中央値は約定の行単位で取る。数量で重み付けしない。** 件数が偶数なら中央2つの `CeilDiv(a + b, 2)`([GDD01 §2.3](../03-gdd/01-trust-and-conversation.md) の切り上げ)。**0 件の日は `-1`**
- **`offer_*` は当日の `world.Market` から取る。** 順5 の段2 が書いた当日の値がそのまま残っている(次の日の段2 まで `Clear()` されない)
- **`offer_at_floor_count` の床は「その品目の外部買値」**(**季節係数は掛からない**。[GDD02d §5](../03-gdd/02d-external-market-and-money.md)。仕様の初稿が「季節係数込み」と書いていたのは誤りで、レビュー1巡目 II-1 で訂正した。床が季節で動かない以上、天井も動かない)。**1次産品は外部買値を持たない**ので `external_buy_price = -1`、`offer_*` も売り手が居ないので 0 件になる
- **`offer_at_floor_with_export_count` は「床に一致し、かつその日に輸出の `Sale` 行がある (売り手 × 品目)」である。** [GDD02 §8-1](../03-gdd/02-economy.md) が硬直の判定から除くのは**この集合だけ**であり、`offer_at_floor_count` との差(床に居るが輸出していない売り手日)は除かない

#### `districts.csv`(日次 × 品目 × 区画。**都市生産品だけ・約定が1件以上ある行だけ**)

`day`, `item_id`, `district_id`, `settled_median`, `settled_count`, `settled_quantity`, `settled_value`, `window_settled_count`

- **区画は買い手の区画である**(`Purchase` 行を立てた世帯の `DistrictId`)。売り手の区画ではない。[GDD02 §8-4](../03-gdd/02-economy.md) が見ているのは「どの区画の買い手がいくらで買ったか」である
- **1次産品を出さないのは、中心区画の窓口でしか買えないからである**([GDD02d §2.1](../03-gdd/02d-external-market-and-money.md))。区画差が定義できない
- **約定 0 の行を書かない。** 36,000日 × 5品目 × 9区画 = 162万行の器に対し、実際に約定がある組は一部である

#### `households.csv`(日次 × 世帯。**全日・全世帯**)

`day`, `household_id`, `district_id`, `occupation`, `output_item_id`, `liquid_funds`,
`production_runs`, `sales_value`, `run_cost`, `profit`,
`is_bankrupt`, `necessity_blocked_count`, `input_blocked`, `demand_lines`, `demand_lines_without_known_price`,
`sellable_stock_output`, `offer_price`

**利潤は [GDD02a §5](../03-gdd/02a-production.md) の式そのものである:**

```
run_cost  = Σ_j( PurchaseUnitCostAverage[j] × 必要数量_j ) + BuyerBudget.WearCostPerRun(...)
sales_value = Σ( 当日の Sale 行の 単価 × 数量 )                     ← 輸出を含む
profit    = sales_value − run_cost × production_runs
```

- **`production_runs` は実行回数である**([GDD02a §1](../03-gdd/02a-production.md)「生産量(実行回数)」)。出力の個数ではない。**`run_cost` は「レシピ1回」の原価なので、掛ける相手は実行回数でなければ次元が合わない** — パン屋(出力2個/回)で個数を掛けると原価が2倍になる
- **`run_cost` の移動平均はその日の終わりの値を使う。** 順10 で読むので、当日の仕入で更新された後の値である。**当日の仕入が原価に即日効く**ことになるが、日次の利潤を日をまたがずに閉じるにはこれしかない([GDD02a §5.1](../03-gdd/02a-production.md) の移動平均は「約定のたびに」更新される)
- **摩耗費は `BuyerBudget.WearCostPerRun` を呼ぶ。式を写さない**(規則8 の表)
- **`profit` は負になりうる。** 売上が無く生産だけした日は `−run_cost × runs` である。**それが正常である**([GDD02 §5](../03-gdd/02-economy.md) 帰結2「既に持っている在庫は、売値が原価を下回っても売る」)
- `offer_price` は当日その世帯が出力品目に付けた提示価格。出品しなかった日は `-1`
- `sellable_stock_output` は `SellableStock.Of` を**呼んで**得る(自前で工房在庫から引かない)

#### `trades.csv`(日次1行)

`day`, `settlement_count`, `internal_settlement_count`, `window_settlement_count`,
`internal_settlement_value`, `partner_switch_permille`, `hhi_permille_squared`,
`active_seller_count`, `active_buyer_count`

```
partner_switch_permille = CeilDiv( 1000 × 相手が変わった (世帯, 品目) の数 ,
                                   前日も当日も買った (世帯, 品目) の数 )
                          分母が 0 の日は -1

hhi_permille_squared    = Σ_売り手( CeilDiv(1000 × その売り手の都市内約定金額, 都市内約定金額の合計) )²
                          都市内約定金額の合計が 0 の日は -1
```

- **`partner_switch_permille` の母数は「前日も当日も同じ品目を買った (世帯, 品目)」である。** `MetricsScratch.PreviousCounterpartyId` / `CurrentCounterpartyId` が持つ。**同じ日に同じ品目を複数の相手から買った世帯は、最後に買った相手を採る**(段5b は店を選び直しながら買うので、行が複数立ちうる)
- **`hhi_permille_squared` は都市内の約定だけを母数にする**(相手 = 予約 Id の行を除く)。窓口を入れると窓口が支配して読めなくなる。値域は 0〜1,000,000
- **`active_seller_count` は当日 `Sale` 行を立てた世帯数、`active_buyer_count` は `Purchase` 行を立てた世帯数**(どちらも窓口を相手にした行を含む)

### 5. `vsim run` と `vsim hash`

```
vsim run  --config <path> --out <dir>
vsim hash --seed <n> --ticks <n>
```

- **`vsim hash` の世界を `WorldDefinition.M0` + `WorldGenerator.Generate(seed)` に、システムを [TDD01 §3.3](../04-tdd/01-sim-core-and-m0.md) の登録順に差し替える。** `--npcs` / `--households` / `--items` を落とす(規模は定義が持つ)。`.github/workflows/ci.yml` の2箇所(`ARGS` と 3回目の実行)を同じコミットで直す
- **`SyntheticLoadSystem` / `SyntheticDecaySystem` / `Program.PlaceSyntheticPopulation` を削除する。** 参照は `Program.cs` と `StateHasherTests` の doc コメント1箇所だけである(`grep -rl Synthetic src tests` で確認済み)
- **設定ファイルは `masterSeeds`(非空の整数配列)・`durationDays`(1以上)・`world.preset`(`"m0-small"` のみ)を読む。** `features` / `coefficients` は**キーがあってもよいが空でなければエラー**にする(W4 で実装する旨をメッセージに書く)。**未知のキーはエラーにする**(`JsonSerializerOptions.UnmappedMemberHandling = Disallow`)— 黙って無視すると、綴り違いの設定が「効いたつもり」で走る
- **出力は `<out>/<シード>/{prices,economy,trades,households,districts}.csv`。** ディレクトリが無ければ作る。**`summary.json` は [#209](https://github.com/stama72/visionary/issues/209) が足す**
- **CSV は1行目にヘッダを書き、数値は `CultureInfo.InvariantCulture` で整形する。** 区切りは `,`、改行は `\n`(プラットフォーム既定にしない — CI が Ubuntu、開発機が Windows で、バイト一致の検査が壊れる)
- `run` は標準出力にシードごとに1行(シード・日数・経過ミリ秒・出力先)を書き、`0` を返す

### 6. 性能 — 帳簿の全走査を消す

**`MarketReference.TryPreviousDaySettledPrice` は帳簿を先頭から全走査している**(`for (int i = 0; i < ledger.Count; i++)`)。段1 が**毎日・全世帯**について呼ぶので、帳簿の行数に比例した仕事が毎日発生し、**36,000日 では日数の2乗になる。**

- **末尾から走査し、`entry.OccurredAt.DayIndex < previousDayIndex` に達したら打ち切る。** 帳簿は追記専用で `OccurredAt` が非減少なので、この打ち切りは**結果を変えない**
- **`MetricsSystem` が当日の行を読むときも同じ形にする**(末尾から、`DayIndex < 当日` で打ち切り)
- **保証と、残る穴**: これで1日あたりの仕事は「直近2日ぶんの行数」に比例する。**ただし帳簿そのものは伸び続ける** — 経済が生きた状態の 36,000日 では 100 万行規模になり、メモリと `StateHasher` の1回ぶんの走査は線形に増える。**本タスクは帳簿を刈らない**(刈ると [TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md) の `Ledgers` の契約が動く)

## 呼び出し側を持たないコード(規則7)

**`MetricsScratch` への書き込みは、順5 の3箇所からしか起きない。** どれも「書いても誰も困らない」ので、**配線を落としても既存のテストは緑のままである。**

| 何を | どこで | 入力の作り方・約束 |
| ---- | ------ | ------------------ |
| `BeginDay()` | `TradeSystem.Step` の**先頭**(段1 より前) | 順5 が唯一の書き手。**順10 で呼ばない** — 読む前に消える。**順1 で呼ばない** — 順5 が登録されていないテストで前日の値が残るのは許容する(メトリクスは順5 の値だからである) |
| `SellerHasNoReference` / `SellerCoefficientCapped` | 段1 の世帯ループ、`sellableStock <= 0` の `continue` **より前** | `hasReference` は `MarketReference.TrySeller` の戻り値をそのまま反転したもの。頭打ちは `OfferPrice.WasUnsoldCapApplied` を呼ぶ(段1 が既に持っている `hasReference` / `sellableStock` / `shipmentTargetStock` / `household.IsBankrupt` / `hasSettled` を渡す)。**`hasReference` を渡す**(レビュー1巡目 I-b の訂正。上記「2.」)。**`continue` の後に置くと、販売在庫 0 の世帯の相場基準が落ちる** |
| `DemandLines` / `DemandLinesWithoutKnownPrice` | 段4 の世帯ループ、`BuyerDemand.Build` の**直後** | `HouseholdDemand.Lines` を走査し、`line.ExpectedStock < line.TargetStock` の行だけを数える。そのうち `line.HasMarketTerm == false` を分子にする |
| `InputBlockedByFunds`(段4 側) | 同上 | `line.Purpose == DemandPurpose.ProductionInput && line.CashCap == 0` の行が1つでもあれば 1 を代入 |
| `InputBlockedByFunds`(段5b 側) | `TradeSystem.TryPurchaseLine` の既存の2箇所(`UnaffordableNecessityCount` を加算している行のとなり) | 経路(1) `decision.Reason == NoPurchaseReason.CashCap`、経路(2) `fundsCap == 0`。**どちらも `line.Purpose == DemandPurpose.ProductionInput` のとき 1 を代入する**(加算しない。世帯日の 0/1 である) |
| `CurrentCounterpartyId` | `TradeSystem.TryPurchaseLine` の約定が成立した直後 | `[household.Id * itemCount + line.ItemId] = store.SellerId`。**窓口(`int.MaxValue`)も相手として記録する** |

**呼び出し順**: 段1 の `BeginDay()` → 段1 の売り手の2欄 → 段4 の需要の3欄 → 段5b の2欄 → 順10 が全部読む。

**代償**: 段4 の `InputBlockedByFunds` と段5b の `InputBlockedByFunds` は**同じ世帯日を両方立てうる**。0/1 の代入にしているのはそのためで、**件数として足せない。** [GDD02 §8-2](../03-gdd/02-economy.md) が (a) を件数、(b) を世帯日で測る非対称はここに由来する(TDD01 §4.2 に明記した)。

## 落ちるべき条件(テスト)

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心(当てる変異 / 期待) |
| - | ------ | -------- | -------------------- | ------------------------- |
| 1 | `MetricsDoesNotChangeTheStateHash` | 同じシードで、`MetricsSystem` を登録した走行と登録しない走行を30日回し、`StateHasher.Compute` が一致する | 順10 が `World` を書き換える(集計の途中で配列へ代入する・在庫や資金に触れる) | **核心**。`MetricsSystem.Step` の中で `household.UnmetConsumption[0] = 0;` を1行足す(集計のつもりの代入)/ **期待: 赤** |
| 2 | `MetricsScratchIsNotHashed` | `world.Metrics` の全欄を走行後に任意の値で埋めても `StateHasher.Compute` が変わらない | 区分をハッシュに含めてしまった(`StateHasher` に追随を書いてしまった) | |
| 3 | `MoneyTotalMovesOnlyByExportsAndImports` | 30日回して、`economy.csv` の `money_total` の日次差が毎日 `export_value − import_value` に一致する | 輸出入の絞り込みの取り違え(相手 Id の `==` / `!=`、`Sale` / `Purchase` の向き) | **核心**。`MetricsSystem` の輸出入の絞り込み `CounterpartyId == ExternalMarketSellerId` を `!=` にする / **期待: 赤** |
| 4 | `InputBlockedCountsTheFirstDay` | 初日(相場基準が無く、必需と耐久が現金を持っていく日)に `input_blocked_households` が 1 以上になる | 段5b の**経路(2)**(資金上限の切り詰め)の計数を落とした。#30 の実測では「入力が最も押し出された日に (b) が ≒0 を返していた」 | **核心**。段5b の経路(2)(`fundsCap == 0`)側の `InputBlockedByFunds` の代入を削る / **期待: 赤** |
| 5 | `InputBlockedCountsTheDayWithNoStore` | 流動資金を必需の取り置き未満まで削った世帯で、生産の入力の店が1件も選ばれない日に `input_blocked_households` が 1 になる | 段4 の計数(`CashCap == 0`)を落とした。**店が作られない日は段5b を通らない**ので、段4 だけがこの世帯日を拾える | **核心**。段4 側の `InputBlockedByFunds` の代入を削る / **期待: 赤** |
| 6 | `SellerDaysCountOnlyPostedOffers` | 販売在庫 0 の世帯が1つ以上ある日について、`seller_days` がその世帯を含まない | 分母を `world.Households` の数から取った(出品していない売り手が割合を押し下げる/押し上げる) | |
| 7 | `SellerWithoutReferenceIsCountedAtTheFloor` | 他の売り手の観測が1件も無い売り手が出品した日、`seller_days_without_reference` が 1 以上になり、その売り手の `offer_price` が床に一致する | 段1 の計数を `sellableStock <= 0` の `continue` より後に置いた / `MarketReference.TrySeller` の戻り値ではなく別の判定を書いた | |
| 8 | `UnsoldCapIsCountedOnlyWhenItBites` | 在庫比から出した価格係数‰ が 1000 を超え、かつ前日の約定が無い日だけ `seller_days_coefficient_capped` が立つ。破産中の日と、係数が 1000 以下の日は立たない | `hasSettled` の向きを反転した / 破産中の分岐を落とした / `1000` を呼び出し側へ写して片方だけ動かした | |
| 9 | `FloorWithExportIsASubsetOfFloor` | 全日・全品目で `offer_at_floor_with_export_count <= offer_at_floor_count` | 輸出の有無を見ずに床の売り手日をそのまま除外対象にした(**[GDD02 §8-1](../03-gdd/02-economy.md) が「床に居るが輸出していない売り手日は除かない」と名指した穴**) | |
| 10 | `SettledMedianIsRoundedUpOnEvenCounts` | 単価 10 と 13 の約定が1件ずつの日、`settled_median` が 12 になる(切り下げなら 11) | 中央値を切り下げた / 下位中央値を採った / 数量で重み付けした | |
| 11 | `SettledStatisticsCountBuyerRowsOnly` | 都市内の約定が1件ある日、`settled_count` が 1 になる(2 ではない) | `Sale` と `Purchase` の両方を数えた(都市内の約定が2倍になる) | |
| 12 | `WindowPurchasesAreASubsetOfSettlements` | 全日・全品目で `window_settled_count <= settled_count`、かつ窓口からの輸入しか無い日は両者が一致する | 窓口の絞り込みの取り違え | |
| 13 | `DistrictRowsUseTheBuyerDistrict` | 区画 A の買い手が区画 B の売り手から買った日、`districts.csv` の行の `district_id` が A になる | 売り手の区画で集計した | **核心**。`districts.csv` の集計キーを買い手の `DistrictId` から売り手の `DistrictId` へ変える / **期待: 赤** |
| 14 | `ProfitUsesRunsNotOutputUnits` | パン屋(出力2個/回)が6回実行した日、`run_cost × 6` が引かれる(`× 12` ではない) | `production_runs` に出力個数を掛けた([GDD02a §5](../03-gdd/02a-production.md) の「生産量」を出力数量と読んだ) | **核心**。`profit` の式の `production_runs` を `production_runs × 出力数量` に変える / **期待: 赤** |
| 15 | `ProfitIncludesWearCost` | 工具の移動平均単価を上げると `run_cost` が上がる | 摩耗費を原価に含めなかった([GDD02 §5](../03-gdd/02-economy.md) 帰結3「工具代を回収できない値付けが『利益が出ている』と判定される」) | |
| 16 | `DemandLinesCountOnlyShortfallLines` | 予想在庫が目標在庫以上の行が `demand_lines` に入らない | `BuyerDemand.Build` が過不足に関係なく行を立てることを見落とし、全行を分母にした(§8-7 の割合が構造的に薄まる) | |
| 17 | `PartnerSwitchIsUndefinedWithoutABase` | 前日も当日も同じ品目を買った (世帯, 品目) が 0 件の日、`partner_switch_permille` が `-1` になる | 分母 0 を 0 として出した(**硬直の判定([TDD01 §5.2](../04-tdd/01-sim-core-and-m0.md)「スイッチ率5%未満に固着」)が、経済が死んだ日を「固着」と読む**) | |
| 18 | `HhiIsUndefinedWithoutInternalSettlements` | 都市内の約定が 0 件の日、`hhi_permille_squared` が `-1` になる | 同上 | |
| 19 | `PreviousDaySettledPriceStopsAtTheSecondDay` | 10万行の古い帳簿 + 直近2日の3行で `MarketReference.TryPreviousDaySettledPrice` を1000回呼び、結果が全走査版と一致し、かつ経過時間が閾値内 | 先頭からの全走査に戻した(36,000日 で日数の2乗) | **核心**。末尾からの走査を先頭からに戻す(`for (int i = 0; i < ledger.Count; i++)` + 打ち切り無し)/ **期待: 赤** |
| 20 | `LongRunFinishesWithinTheBudget` | `WorldDefinition.M0` を 36,000日 回し、CSV の書き出しまで含めて 60 秒以内 | 1日あたりの仕事が日数に比例する実装 | |
| 21 | `RunIsByteIdenticalAcrossRuns` | 同じ設定で `vsim run` を2回走らせ、5つの CSV がバイト一致する | 列挙順の破れ / 文化依存の整形 / 改行のプラットフォーム依存 | |
| 22 | `RowCountsMatchTheGrid` | 30日の走行で `economy.csv` / `trades.csv` が 30 行、`prices.csv` が 30 × 9 行、`households.csv` が 30 × 10 行(いずれもヘッダを除く) | 日をまたぐ書き漏らし・二重書き | |
| 23 | `ConfigRejectsUnknownKeys` | 未知のキーを持つ設定、空でない `features`、`preset` が `"m0-small"` 以外、`durationDays <= 0`、空の `masterSeeds` がいずれもエラー(終了コード 64)になる | 黙って無視した(綴り違いの設定が「効いたつもり」で走る) | |

> **「核心」印は7件で、[02-task-spec](../process/02-task-spec.md) の想定(2〜3件)より多い。** 本タスクは [#41](https://github.com/stama72/visionary/issues/41) を2本へ割った前半で、収集の配線が6箇所(順10 と順5 の5箇所)に分かれるためである。**7件の内訳は、ハッシュの契約1・貨幣の恒等式1・(b) の2経路2・集計キー1・次元1・性能1** で、いずれも**外しても既存のテストが緑のまま通る**形である(#13 の集計キーの取り違えは、同じ形の変異が緑のまま通った実測が `TradePipelineTests.InternalSettlementsOfCityGoodsDisappearWithinThirtyDays` の doc コメントに残っている)。

> **#20 は現時点では空振りに近い。** 着手時点の master では経済が 20〜30 日で止まるので、36,000日 の大半は1日あたりの仕事がほぼ 0 である(実測: 1,600日 が 85 ミリ秒)。**「60 秒以内で通った」ことは、経済が生きた状態で通ることを意味しない。** 性能を実際に守っているのは #19 のほうであり、#20 は完了条件の写しとして置く。**この注記をテストの doc コメントにも書くこと** — 書かないと、後から読んだ人が「性能は測ってある」と読む。

### 落ちるべき条件 — 別表(レビューで足した / 訂正したもの)

**上の表は実装に渡した時点の指示であって最終形ではない。** レビューで見つかった欠陥に対する追加・訂正はここに持つ。

| # | テスト | 検証内容 | この実装ミスで落ちる | 出所 |
| - | ------ | -------- | -------------------- | ---- |
| 8' | `UnsoldCapIsCountedOnlyWhenItBites`(**上の #8 を訂正**) | 上の #8 に加えて、**相場基準が立たない日は `seller_days_coefficient_capped` が立たない**。初日(全売り手が相場基準を持たない日)は 0 である | `WasUnsoldCapApplied` に `hasReference` を渡さず、在庫比だけから係数を再計算した(盲目の売り手日がラチェット停止に混入する) | 1巡目 I-b |
| 7' | `SellerWithoutReferenceIsCountedAtTheFloor`(**上の #7 の「落ちる条件」を訂正**) | 検証内容は変えない。**落ちる条件から「段1 の計数を `sellableStock <= 0` の `continue` より後に置いた」を落とす** — `seller_days` 系の分母は `world.Market` から取っており、`Market` に載るのは `sellableStock > 0` を通った売り手だけなので、**代入を `continue` の後ろへ移しても出力のどの値も変わらない。** 段1 が全世帯について書くことを守る機械は無い(規約としては残すが、「機械が守っている」とは書かない) | `MarketReference.TrySeller` の戻り値ではなく別の判定を書いた | 1巡目 I-b |
| 24 | `InputBlockedCountsWhenFundsCapTruncates` | **段5b の経路(2)(`fundsCap == 0`)だけ**が `input_blocked_households` を立てる世帯日を作る。段4 の `CashCap == 0` が立たない(= 需要を組んだ時点では資金があった)世帯について、段5b の切り詰めで 1 になる | 段5b 経路(2) の代入を削った。**上の #4 はこの経路を核心に指定していたが、流動資金 0 の世帯が段4 経路で先に 1 を立てるため、経路(2) を丸ごと削っても緑のまま通っていた**(1巡目 I-a) | 1巡目 I-a |
| 21' | `RunIsByteIdenticalAcrossRuns`(**上の #21 を補強**) | 上の #21 に加えて、**(i)** 負号・小数点の表記が異なるカルチャを現在のカルチャに設定した実行の出力が、不変カルチャの実行とバイト一致する、**(ii)** 出力バイトに `\r\n` が現れない | `CultureInfo.InvariantCulture` を落とした / `NewLine = "\n"` を落とした。**同一プロセス内で2回走らせて比べるだけでは、カルチャも改行も列挙順も同じなので3モードとも素通りする**(1巡目 I-a)。`Runner` は `DeterminismConventionTests` の守備範囲外なので、ここが `Runner` の整形規約を守る唯一の機械である | 1巡目 I-a |

## 編集してよい文書

worktree をまたいだ所有権の宣言。**ここに挙がっていない文書は触らない。**

- `.github/workflows/ci.yml` — `vsim hash` の選択肢を落とす追随
- `docs/tasks/W2-20-metrics-output.md` — レビューで足したテストの**別表**だけ(表そのものは書き換えない)

**`docs/04-tdd/01-sim-core-and-m0.md` は触らない。** §3.8 / §4.1 / §4.2 はフェーズ1 が既に更新済みである(この仕様と同じコミット)。**フェーズ2 が触れば `SPEC-OUTSIDE` で止まる**(機械が差分で見ている)。実装が TDD と食い違うなら、それは象限I-b であり、フェーズ1 へ戻す案件である。
- `docs/tasks/W2-20-metrics-output.handoff.md` — 引き継ぎメモ

**GDD は触らない。** 触る必要が出たら `SPEC-OUTSIDE` である。

## このタスクで特に効く規約

- **割合は千分率(‰)の整数で、`Visionary.Sim` 側で計算する。** `DeterminismConventionTests` が見ているのは `Visionary.Sim` だけなので、**`Runner` 側で `double` を使うと機械に止められない**
- **列挙順。** 世帯は Id 昇順、品目は Id 昇順、区画は Id 昇順。`Dictionary` の列挙結果を行の順序に使わない(`world.Market` は `SortedDictionary` なのでそのまま使える)
- **`Metrics` はシムの意思決定に一切関与しない。** 機械が見ているのは「順10 を登録した走行としない走行のハッシュが一致すること」(テスト #1)までで、**`MetricsScratch` の値を他の系統が読む形は機械に止められない**([TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md) に明記した)

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **「核心」印の7件の変異を `mutator` が実測し(レビューの巡が閉じた後)、結果を doc コメントへ転記した**
- [ ] `vsim run --config <36,000日の設定> --out <dir>` が 60 秒以内に終わり、5つの CSV が出る
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
