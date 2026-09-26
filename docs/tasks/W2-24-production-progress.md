# W2-24: 生産の進捗‰・鍛冶 1300‰・取り置きからの在庫の差し引き・④ の c

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#237](https://github.com/stama72/visionary/issues/237)              |
| 根拠     | [GDD02a §1〜§4](../03-gdd/02a-production.md) / [GDD02b §3.1・§4.1・§4.2・§8](../03-gdd/02b-consumption-and-household.md) / [GDD02c §1.3](../03-gdd/02c-price-and-budget.md) / [GDD02d §4.4](../03-gdd/02d-external-market-and-money.md) / [TDD01 §3.2・§3.8](../04-tdd/01-sim-core-and-m0.md) / [#218](https://github.com/stama72/visionary/issues/218) 決定ログ1〜7・追随表 |
| ブランチ | `feat/237-production-progress`                                       |
| worktree | 本体ツリー(パイプライン)                                           |

**4つの規則を実装する。** どれも [#218](https://github.com/stama72/visionary/issues/218)(醸造が止まって戻らない)の処方で、仕様は GDD が持つ。本書は「どう作るか」だけを書く。

| # | 規則 | 正 |
| - | ---- | -- |
| 1 | 生産の進捗‰ — 労働の端数だけを翌日へ持ち越す。職業の付け替えで 0 | GDD02a §1、02b §4.2 |
| 2 | 鍛冶の所要労働‰ 1000 → 1300。校正 (c) の年間実行回数を `floor(120 × 1300 ÷ 所要労働‰)` に | GDD02d §4.4 |
| 3 | 必需の取り置き・運転資金 = `Σ max(0, 目標在庫 − 予想在庫) × 相場基準` | GDD02b §3.1 |
| 4 | ④ のゲート c = 生産量 0 **かつ** 入力から作れる回数 0 | GDD02b §4.1 |

## フェーズ1 で決めたこと

### 「増産できない」は、順1 が書く「当日の生産能力」を読む

**GDD02b §8 の条件「その日の生産能力が 0(進捗‰ < 所要労働‰)」は、順4 では評価できない。** 順4 の `NeedGenerationSystem` が読める進捗‰ は、順1 が実行ぶんを引いて `所要労働‰ − 1` 以下に丸めた後の値である。文字どおり `進捗‰ < 所要労働‰` と書くと**全世帯で毎日立つ。**

**決定: `HouseholdState` に「当日の生産能力(実行回数)」の欄を足し、順1 が毎日書く。順4 はそれが 0 かを見る。** TDD01 §3.2・§3.8 に欄を足した(本タスクの凍結コミット)。

- **数量は進捗‰ の欄から読める。** 生産能力が 0 の日は生産量も 0 なので、順1 は何も引かない。`min(進捗‰ − 所要労働‰ × 0, 所要労働‰ − 1)` は、進捗‰ < 所要労働‰ なので進捗‰ のまま残る。**したがって順4 が読む進捗‰ は GDD02b §8 の「その日の労働を足した後の進捗」と一致し、数量 = `所要労働‰ − 進捗‰`(1 以上)。**
- **労働力を順4 で計算し直す今の実装は使えない。** 前日から持ち越した端数が見えない。「生産量 0 かつ 入力が足りている」なら能力 0 と逆算できるが、入力も足りない日は一意に決まらない(#218 全般1巡目 指摘3)。

却下した案は引き継ぎメモ(`W2-24-production-progress.handoff.md`)。

### 醸造の完了条件は 90日走行で測る

issue の閉じる条件は「5シード・60日で、各シードに醸造の最終稼働日が day 60 以上の戸が1戸以上」である。**day は 0 始まり**(#218 決定ログ2 は 150日走行で「day 96〜149」、TDD01 §5.2 の判定窓も「day 29〜149」)なので、**60日走行の最後の日は day 59 で、この条件は満たせない。**

**決定: 90日走行(day 0〜89)で、最終稼働日が day 60 以上の醸造の戸が1戸以上あることを見る。** day 90 から冬に入る([GDD03 §1.2](../03-gdd/03-seasons-and-city.md) 春始まり・30日×4季)ので、#218 決定6b の「冬 = #222 をまたがない」を保つ。**「day 60 以上」の物差しは変えていない** — 差し引く前の実測は全シード day 6〜16 に止まっていた(決定ログ2)。

### seed 7 を閉じる条件から外す(再凍結 2026-09-26)

1回目のフェーズ2 で、規則1〜4 を入れた状態の実測は **seed 1/2/3/42 が day 89 まで稼働、seed 7 だけ最終稼働日 day 48** だった(WIP コミット `8e54adf`)。seed 7 の household3 はビールが売れ残り続けて ④ のゲート b が閉じたまま、day 50 以降は穀物 0・資金膠着で止まる。「全5シード」は規則3 を入れた後の実測に基づかない期待だった。

**決定(開発者): 閉じる条件を 4/5 シード(1/2/3/42)に緩め、seed 7 は [#239](https://github.com/stama72/visionary/issues/239) へ切り出す。** 4/5 は「どれか4つ」ではなく**シードを名指しで固定する** — 数で数えると、seed 1 が壊れて seed 7 が直った変更も緑になる。seed 7 は `[InlineData]` から外し、doc コメントに実測(day 48)と #239 を書く。#239 で抜ける経路が入ったら seed 7 を戻す。

## スコープ

**含む:**

- 規則1〜4 の実装と、それに伴う doc コメントの書き換え
- 状態の欄2つ(進捗‰・当日の生産能力)とハッシュへの追加
- 校正テスト (c) の年間実行回数の書き換え
- 醸造の完了条件のテスト(90日走行)
- 規則の変更で数値が変わった既存テストの追随(下の「6. 既存テストの追随」の手順に従う)

**含まない:**

- 8-3 の集計(`VerificationAccumulator`)・生産停止世帯の数(`MetricsSystem`)— 読むのは `ProductionRuns == 0` のままで、**意味だけが変わる**(鍛冶に平均損失 ÷ 1300 の底が乗る。TDD01 §5.2 `8-3` の行が既に書いている)
- 校正テスト (a)(b)(d)(e) の式 — 下の表
- 外出の損失の平均を校正に入れること([#222](https://github.com/stama72/visionary/issues/222))、鍛冶 1000 + (c) の解き直し([#236](https://github.com/stama72/visionary/issues/236))、2戸目の醸造([#224](https://github.com/stama72/visionary/issues/224))

### 変えない既存コード(規則8。[02-task-spec](../process/02-task-spec.md)「変えないと宣言する既存コード」)

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `WorldDefinition.ProductionCapacity` = `CapacityRuns(NominalLaborPermille, 1000)` と、それを読む `DailyInputQuantity` / `InputTargetStock` / `ShipmentTargetStock` | GDD02a §4「進捗‰ は読まない」/ GDD02c §1.3 出荷目標在庫 | **一致**(`floor(1300 ÷ 所要労働‰)`。世帯を引数に取らないので、**進捗‰ を読めないことは型が守る**) |
| `LaborCapacity.LaborPermille`(`max(0, Σ構成員の係数‰ − 前日の外出の労働損失‰)`) | GDD02a §2 | 一致 |
| `LaborCapacity.EquipmentPermille`(工具在庫 ≥ 1 なら 1000、無ければ 500) | GDD02a §3 | 一致 |
| `ProductionSystem.WearTools`(`累積摩耗 += 所要労働‰ × 生産量`、工具0で破棄) | GDD02a §1・§3.1 | 一致 |
| `NeedGenerationSystem.CollectProductionStopped`(生産量 0 かつ 工房在庫 < 必要数量) | GDD02b §8 の表「生産停止」 | 一致 |
| `BuyerBudget.AvailableFunds` の段(必需 = 流動資金 / 耐久・入力 = − 取り置き / 嗜好 = − 取り置き − 運転資金) | GDD02b §3.1 / GDD02c §2.1 | 一致(段の形は変わらない。変わるのは取り置き・運転資金の中身だけ) |
| `SelfConsumption`(自家消費の移動) | GDD02b §1.1 | 一致(取り置きに触れるコードを持たない。§1.1 の「取り置きは自然に縮む」は規則3 の帰結) |
| `M0CalibrationTests` (a) の供給 = `ProductionCapacity × 出力数量 × 120` | GDD02d §4.4 の表「実行/日」の列(`floor(1300 ÷ 所要労働‰)`) | 一致(日ごとの切り下げで供給を**下から**見積もる。平均実行回数は切り下げないので、(a) は十分条件のまま) |
| `M0CalibrationTests` (d) の取り置き・運転資金 = 目標在庫 × 床(在庫を差し引かない) | GDD02d §4.4「(d) の初期資金」(884 / 1,188)、#218 決定ログ5「(d) は直さない」 | 一致(在庫 0 から揃える額 = 初日の予算が切り詰められない十分条件) |
| `M0CalibrationTests` (e) の摩耗費 = `WearCostPerRun(工具の床, 所要労働‰, N)` | GDD02d §4.4 (e)(鍛冶 241 − 29 = 212) | 一致(所要労働‰ を定義から読むので、1300 で 29 に自動で変わる) |
| `VerificationAccumulator` の 8-3、`MetricsSystem` の生産停止世帯の数 | GDD02 §8-3 / TDD01 §5.2 `8-3` | 未確認(`ProductionRuns == 0` を数えることだけ見た。コードは変えない) |

## 作るもの

### 1. 状態の欄(`src/Visionary.Sim/World/HouseholdState.cs`)

TDD01 §3.2「生産の進捗‰(持ち越した端数)」「当日の生産能力(実行回数)」。

```csharp
/// 生産の進捗‰(GDD02a §1)。単位: ‰人日。0以上。順1 が毎日書く。④の付け替え(順3)で 0 に戻す。
public int ProductionProgressPermille { get; set; }   // 負を setter で拒む(ArgumentOutOfRangeException)

/// 当日の生産能力(実行回数)(GDD02a §1・GDD02b §8)。0以上。順1 が毎日書く(0 の日も書く)。
/// 順4 の「増産できない」が読む。
public int ProductionCapacityRuns { get; set; }       // 負を setter で拒む
```

- 既存の `ProductionRuns` と同じ形(private の backing field + 検証付き setter、コンストラクタで 0)
- **上限(進捗‰ < 所要労働‰)は型では守れない。** 所要労働‰ を知っているのはレシピである。「順1 の後は `0 ≤ 進捗‰ ≤ 所要労働‰ − 1`」は `ProductionSystem` の後条件で、テスト #3 が押さえる
- `WorldGenerator` は書かない(コンストラクタの 0 が初期値。GDD02a §1「初期値 0」)

### 2. ハッシュ(`src/Visionary.Sim/Determinism/StateHasher.cs`)

TDD01 §3.8。世帯の要素の**末尾**(`UnfilledPurchase` の後)に2欄を足す。コメントは既存と同じ形(「#237が足した2欄。既存の値は動かさず末尾へ足す(同じ規律)」)。

```csharp
WriteInt32(hasher, buffer, household.ProductionProgressPermille);
WriteInt32(hasher, buffer, household.ProductionCapacityRuns);
```

`StateHasherCoverageTests` の `HouseholdState` の欄一覧に2つを足す。

### 3. レシピ(`src/Visionary.Sim/Definition/Recipe.cs`)

**外側の除算(÷ 所要労働‰)と入力の min を、それぞれ1か所に置く。**

```csharp
/// 生産能力(実行回数)= floor(進捗‰ ÷ 所要労働‰)(GDD02a §1)。
public int CapacityRunsFromProgress(int progressPermille)   // 負は ArgumentOutOfRangeException

/// 入力から作れる回数 = min_j floor(在庫[入力j] ÷ 必要数量_j)(GDD02a §1)。
/// 入力が0件のレシピは int.MaxValue を返す(「この項では制約しない」)。
public int RunsFromInputs(int[] workshopInventory)          // null は ArgumentNullException
```

- **既存の `CapacityRuns(labor, equipment)` は残し、中身を `CapacityRunsFromProgress(IntegerMath.FloorPermille(...))` に置き換える。** 「物差し = 進捗‰ 0 から1日働いたときの能力」であり、外側の除算は `CapacityRunsFromProgress` にしか無くなる。呼び出し側は `WorldDefinition.ProductionCapacity` だけになる(`ProductionSystem` と `NeedGenerationSystem` は呼ばなくなる)。doc コメントの「両方がこのメソッドを呼ぶ」を直す
- **`RunsFromInputs` が入力0件で 0 を返してはならない。** 空の min を 0 と実装すると、`ProductionSystem` の入力0件のレシピが永久に止まり、④ の c が毎日立つ(#218 全般2巡目 指摘2)。テスト #9 と既存の `ProductionRunsWithoutInputsUpToCapacity` が押さえる

### 4. 生産(`src/Visionary.Sim/Systems/ProductionSystem.cs`)

GDD02a §1 の式をこの順で書く。

```
equipment = LaborCapacity.EquipmentPermille(definition, household)          // 摩耗の前(今と同じ)
labor     = LaborCapacity.LaborPermille(definition, world, household)
progress  = household.ProductionProgressPermille
          + LaborCapacity.EffectiveLaborPermille(labor, equipment)          // += floor(労働 × 設備 ÷ 1000)
capacity  = recipe.CapacityRunsFromProgress(progress)
runs      = min(capacity, recipe.RunsFromInputs(household.WorkshopInventory))

household.ProductionCapacityRuns = capacity
household.ProductionRuns         = runs
入力を減らし、出力を足す(今と同じ)
household.ProductionProgressPermille = min(progress − recipe.LaborPermille × runs, recipe.LaborPermille − 1)
WearTools(household, recipe.LaborPermille, runs)                            // 今と同じ
```

- **`min(…, 所要労働‰ − 1)` が「端数だけ持ち越す」の実体である。** 入力が足りずに使えなかった労働は捨てる(GDD02a §1 の2つ目の箇条)。落とすと入力が届いた日に出力が跳ねる(テスト #4)
- **早期 return しない**(今の doc コメントのとおり。摩耗の破棄が道連れで飛ぶ)
- クラスの doc コメントの「鍛冶は工具在庫0で能力が0(完全停止)になる」は偽になる。**「工具が無ければどの職業も生産が半分になる(GDD02a §3)。鍛冶は2日に1回の完成になる。留保(GDD02c §1.3)が工具を手放させないのは、詰みではなく自己参照を切るためである」**の趣旨に書き直す。`RunOneHousehold` 内の同じ趣旨のコメントも直す
- `WearTools` の remarks「108から1000まで」→「108から1300まで」

### 5. Need・④・付け替え・取り置き

**5a. 増産できない**(`NeedGenerationSystem.CollectCannotExpandProduction`、GDD02b §8):

```
if (household.ProductionCapacityRuns != 0) return;
shortage = recipe.LaborPermille − household.ProductionProgressPermille     // 1以上(上の「フェーズ1 で決めたこと」)
Add(..., CannotExpandProduction, recipe.Outputs[0].ItemId, shortage)
```

- `LaborCapacity` を呼ばなくなる。`LaborCapacity` のクラス doc の「`ProductionSystem` と `NeedGenerationSystem` の両方が」を直す
- **残る穴**: 付け替えの日(順3)は、順4 が読む能力は旧職業のレシピで順1 が求めた値、所要労働‰ は新職業のレシピ、進捗‰ は 0 になる。能力 0 の日に付け替わった世帯は、新職業の出力について数量 = 新職業の所要労働‰ の Need が1日だけ立つ。**翌日の順1 で解消する。** ④ の c は入力切れの日にしか開かないので、能力も 0 の日と重なるのは鍛冶が外出で1回に届かなかった日に限られる。直さない
- `CollectToolsExhausted` の doc「工具が無くても半分の能力で続く職業が4つある」→「全職業が半分の能力で続く(GDD02a §3。進捗‰)」
- `CollectProductionStopped` の doc「入力が足りていて生産量0ならそれは理由3(労働力)が拾う」は正しいまま

**5b. ④ のゲート c**(`OccupationReassignment.IsGateOpen`、GDD02b §4.1):

```
// c. 当日の生産量が 0 かつ 入力から作れる回数が 0(入力切れ)。
if (household.ProductionRuns != 0) return false;
if (recipe.RunsFromInputs(household.WorkshopInventory) != 0) return false;
```

- **入力から作れる回数は順3 の時点の工房在庫から求める。** 順1 の後、順3 までに入力の在庫は動かない(GDD02b §4.1 の ※。順2 の自家消費が動かすのは出力だけで、M0 に自分の出力を入力に使うレシピは無い)。生産量 0 の日は順1 も入力を減らしていないので、順1 の前の在庫と同じ値である
- **入力0件のレシピでは c は立たない**(`RunsFromInputs` が `int.MaxValue`)。M0 に該当なし
- remarks の「工具切れはcに入らない ── 工具が無くても半分の能力で続く職業では生産量が0にならない(※3)」を、GDD02b §4.1 の現行の ※(c は入力切れだけを見る。生産量 0 だけにすると鍛冶が労働が1日足りないだけで廃業する)に書き直す

**5c. 付け替えで進捗‰ を 0 に戻す**(`HouseholdSystem` の手順2、GDD02b §4.2):

`household.Occupation = target;` の直後に `household.ProductionProgressPermille = 0;`。**`ProductionCapacityRuns` と `ProductionRuns` は触らない**(当日の値であり、翌日の順1 が上書きする。GDD02b §4.2 が戻すと書くのは進捗‰ だけ)。

**5d. 取り置き・運転資金**(`BuyerDemand.Build`、GDD02b §3.1):

```csharp
// 必需のループ(expected = household.HouseholdInventory[itemId]。§5.1 の予想在庫と同じ値)
necessityReserve += (long)Math.Max(0, target - expected) * reference[itemId];
// 生産の入力のループ(expected = household.WorkshopInventory[itemId])
workingCapital   += (long)Math.Max(0, target - expected) * reference[itemId];
```

- `expected` は各ループが既に行に渡している変数をそのまま使う。**別に計算し直さない**(GDD02b §3.1「予想在庫は §5.1 と同じ値」)
- 相場基準が立った品目だけを足すのは今と同じ
- `HouseholdDemand.NecessityReserve` / `WorkingCapital` の doc の式を `max(0, 目標在庫 − 予想在庫) × 相場基準` に直す。`BuyerBudget.cs` と `DemandPurpose.cs` の doc は取り置きの式を書いていないので変えない

### 6. 鍛冶の所要労働‰(`WorldDefinition.BuildM0`)

`laborPermille: 1000` → `1300`。コメントは `// ‰。1300‰ ÷ 1300‰ = 1実行/日(平均。GDD02a §2・GDD02d §4.4)`。

### 7. 校正テスト (c)(`M0CalibrationTests.M0SatisfiesFloorPriceBalanceWithinTwoPercent_C`)

GDD02d §4.4「下の検算はこれを年に直した実行回数 `floor(120 × 1300 ÷ 所要労働‰)` で解く」。

```
annualRuns = FloorDiv(DaysPerYear × NominalLaborPermille, recipe.LaborPermille)     // 843 / 722 / 722 / 1,444 / 120
revenue    = annualRuns × 出力数量 × ExternalBuyPrice(出力)
purchases  = Σ_j annualRuns × 必要数量_j × Floor(入力j)                               // Floor = 年平均(既存の helper)
wear       = CeilDiv(annualRuns × 所要労働‰ × ExternalBuyPrice(工具), ToolDurabilityPerUnit)
```

期待(GDD02d §4.4 の表。本タスクの凍結コミットで摩耗の切り上げを直した):

| 職業 | 売上 | 仕入 | 摩耗 | 収支 |
| ---- | ---- | ---- | ---- | ---- |
| 水車小屋番 | 47,208 | 16,860 | 3,479 | −131 |
| パン屋 | 77,976 | 47,652 | 3,480 | −156 |
| 醸造 | 51,984 | 21,660 | 3,480 | −156 |
| 木材加工 | 43,320 | 12,996 | 3,480 | −156 |
| 鍛冶 | 34,800 | 4,800 | 3,480 | −480 |

- `purchases` は `annualRuns × 数量 × AnnualPrice ÷ 120` ではなく `Floor(入力)`(年平均)を掛ける。M0 の値では割り切れて同じだが、除算をテストの中に新しく書かない
- remarks の「フェーズ1の検算: −227 / −230 / … / +323」と変異の実測記録は旧版の値である。**変異の記録は消さず「旧版(所要労働 1000‰・日ごとの floor × 120)での実測」と明記して残し**、上の表の値を新しい検算として書く

### 8. 醸造の完了条件(`TradePipelineTests` に新設)

```csharp
[Theory]
[InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(42)]
public void SomeBrewerIsStillProducingAfterDaySixty(long seed)
```

- **seed 7 は入れない**(上の「seed 7 を閉じる条件から外す」)。doc コメントに「seed 7 は最終稼働日 day 48(売れ残りと資金膠着。#239)で、#239 が閉じるまで外す」と、実測の日付・HEAD を書く。**`[InlineData(7)]` を `Skip` 付きで残す形は採らない**(xUnit の `InlineData` 単位の Skip は無く、`[Theory(Skip=…)]` は全シードを止める)
- `WorldDefinition.M0`・`WorldGenerator.Generate`・`FullPipeline` で **90日**(`scheduler.Advance(world, ticks: 24)` を 90 回)
- **日の番号は 0 始まりで数える。** k 回目(1〜90)の `Advance` の直後に見える `ProductionRuns` は day `k − 1` の順1 が書いた値である(`SmithNeverRunsOutOfToolsOverSixtyDays` のループ変数 `day` は 1 始まりなので、流用するときは −1 する)
- 世帯ごとに「その日の職業が醸造 かつ `ProductionRuns > 0`」の最後の day を記録し、**最大値 ≥ 60** を断定する。④ で醸造に付け替わった世帯も数える(「醸造の戸」は日ごとの職業で判定する)
- 失敗メッセージに、各醸造世帯(day 0 の醸造と、途中で醸造になった世帯)の最終稼働日を出す
- 空振り防止: `Assert.Equal(90, world.Now.DayIndex)` と、day 0 に醸造が2戸あること

### 9. 既存テストの追随

**規則の変更で既存テストの期待値が変わるのは想定内である。** 次の手順で扱う。

1. **式から期待値を導き直せるテスト**(`ProductionSystemTests` の複数日にわたる検査など)は、新しい式で導き直した値に書き換え、**導出をコメントに書く**(「実際値に合わせた」は理由にならない)
2. **期待値が変わる既知のテスト**:
   - `NeedGenerationSystemTests.LaborShortageNeedRisesWhenCapacityIsZero`(#8)/ `LaborShortageNeedDoesNotRiseAtTheBoundary`(#9)— `NeedGenerationSystem` は労働力を読まなくなるので、**`ProductionCapacityRuns` と `ProductionProgressPermille` を直接置く形**に書き換える(#8: 能力 0・進捗 650・所要労働 1000 → 数量 350。#9: 能力 1 → 立たない)
   - `HouseholdSystemTests.ReassignmentTouchesNothingButTheOccupation` — 被験者(木材加工)の工房在庫[木材] = 42 は新しい c を閉じる(入力から 42 回作れる)。**「ゲートに関係しない工房在庫」は入力でない品目(例: 穀物)に置き換え**、木材は 0 にする。あわせて付け替え前に進捗‰ を正の値にし、**後で 0 になること**と、`ProductionCapacityRuns` が変わらないことを断定する(題は変えてよい)
   - `HouseholdSystemTests.OccupationChangesOnlyWhenAllThreeConditionsHold` の「3条件すべて」行は、被験者の入力在庫が 0 であることを前提にしている。**`AddHousehold` が入力在庫を 0 で作ることを確かめ、前提として断定を1行足す**
   - `TradePipelineTests.SmithNeverRunsOutOfToolsOverSixtyDays` — remarks の「工具切れ → 設備係数500‰ → 能力0 → 復帰しない」を「工具切れ → 設備係数500‰ → 生産が半減」へ直す。**検査の中身(工具在庫 ≥ 1)は有効なので変えない**
3. **全走行の空振り防止が崩れたとき**(例: `EveryOccupationKeepsAtLeastOneCarrierOverSixtyDays` の「60日のうち職業分布が day 0 と異なる日がある」= ④ が発火した)。規則3 で必需が資金で止まる世帯日は 5.5% → 0.6% に下がる(#218 決定ログ2)ので、**④(破産中が前提)が発火しなくなるシードが出うる。** そのときは:
   - 反転しない。**シードごとに実測で割り振る** — `[Theory]` に `bool expectReassignmentToFire` を足し、発火したシードは `true`(断定を残す)、発火しなかったシードは `false`(断定を飛ばす)
   - 全シードが `false` なら引数ごと消し、doc コメントに「M0 の値では60日で④が発火しない。担い手 ≥ 1 の核心は空振りしており、規則は `HouseholdSystemTests.LastCarrierOfAnOccupationIsNeverReassigned` が守る」と書く
   - どちらの場合も、実測の日付・HEAD・シードごとの結果を doc コメントに残す
4. **1〜3 に当たらずに赤くなった核心の断定**(空振り防止ではない断定)は直さずに止まって報告する(`IMPL-BLOCKED`)

## 順序・境界の具体例(規則6)

**パン屋**(所要労働 216‰、工具あり、入力は十分):

| 日 | 前日の損失‰ | 進捗‰(足した後) | 能力 | 生産量 | 持ち越す進捗‰ |
| -- | ----------- | ----------------- | ---- | ------ | ------------- |
| 0 | 0 | 0 + 1300 = 1300 | 6 | 6 | min(1300 − 1296, 215) = 4 |
| 1 | 100 | 4 + 1200 = 1204 | 5 | 5 | min(1204 − 1080, 215) = 124 |
| 2 | 0 | 124 + 1300 = 1424 | 6 | 6 | 128 |

**パン屋、入力が2回分しか無い日**: 進捗 4 から 1304 → 能力 6、生産量 2 → `min(1304 − 432, 215)` = **215**(872 ではない)。翌日、入力が十分なら 215 + 1300 = 1515 → 7 回。

**鍛冶**(所要労働 1300‰、工具あり、入力は十分):

| 日 | 前日の損失‰ | 進捗‰(足した後) | 能力 | 生産量 | 持ち越す進捗‰ | 増産できない |
| -- | ----------- | ----------------- | ---- | ------ | ------------- | ------------ |
| 0 | 150 | 0 + 1150 = 1150 | 0 | 0 | 1150 | 立つ・数量 150 |
| 1 | 0 | 1150 + 1300 = 2450 | 1 | 1 | 1150 | **立たない**(持ち越した 1150 は 1300 未満だが、能力は 1) |
| 2 | 150 | 1150 + 1150 = 2300 | 1 | 1 | 1000 | 立たない |

旧版(日ごとの floor)なら日2 は 1150 → 0 回である。**持ち越しが損失を吸収するのは日2 である。**

**工具なし**(所要労働 1300‰・**出力が工具でない**テスト用レシピ。M0 の鍛冶は完成した日に自分の工具を得るので、工具なしが続かない): 進捗 0 から 650 → 0 回(持ち越し 650、増産できない・数量 650)→ 1300 → 1 回(0)→ 650 → 0 回 → 1300 → 1 回。**生産量 0, 1, 0, 1。**

**付け替え**: 順3 で職業が変わった世帯は、その場で進捗‰ = 0。当日の `ProductionRuns` / `ProductionCapacityRuns` は順1 が書いた値のまま。

**取り置き**(パンの目標 3・相場基準 20): 世帯在庫 0 → 60、1 → 40、3 → 0、5 → **0**(−40 ではない)。

## 落ちるべき条件(テスト)

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心(当てる変異 / 期待) |
| - | ------ | -------- | -------------------- | ------------------------- |
| 1 | `ProductionSystemTests` `ProgressCarriesTheRemainderAcrossDays` | 上の「パン屋」表の日0〜2(前日の損失 0/100/0 を各日の前に置く)で、各日の生産量 6/5/6 と持ち越し 4/124/128 | 端数を捨てる(生産量は 6/5/6 のまま変わらず、**持ち越しが 0/0/0 になる**) | **核心**。M1: 順1 の最後の代入を `ProductionProgressPermille = 0` にする / 赤 |
| 2 | `ProductionSystemTests` `SmithCompletesOnTheLossDayFromTheCarriedRemainder` | 「鍛冶」表の日0〜2。日0 生産量 0・能力 0・持ち越し 1150、日1 生産量 1・持ち越し 1150、**日2 生産量 1** | 端数を捨てる(日2 が 1150 → 0 回になる)。能力の欄を書かない | M1 で赤(#1 と同じ変異。日2 の生産量で落ちる) |
| 3 | `ProductionSystemTests` `ToolLessProductionCompletesEveryOtherDay` | 「工具なし」の例を4日。生産量 0, 1, 0, 1 | 工具なしで能力 0 に張り付く(進捗を足さない・設備係数を二重に掛ける) | — |
| 4 | `ProductionSystemTests` `UnusedLaborIsNotBankedWhenInputsRunShort` | 「入力が2回分しか無い日」。**持ち越しがちょうど 215**、翌日(入力十分)7 回・持ち越し 3 | `min(…, 所要労働‰ − 1)` を落とす(持ち越し 872、翌日 10 回)。上限を所要労働‰ にする(持ち越し 216) | **核心**。M2: `min` を落として `progress − L × runs` をそのまま代入 / 赤 |
| 5 | `ProductionSystemTests` `ProductionRecordsCapacityEveryDayIncludingZero` | 入力0の日も `ProductionCapacityRuns` が能力(生産量ではない)を持つ。パン屋・入力0・進捗 0 → 能力 6・生産量 0 | 能力の欄に生産量を書く。入力0の日に書かない | — |
| 6 | `NeedGenerationSystemTests` `CannotExpandProductionReadsTheRecordedCapacityNotTheCarriedProgress` | `SimScheduler` に `ProductionSystem` → `NeedGenerationSystem` の順で登録し、「鍛冶」表の日0〜1 を回す(各日の前に損失を置く)。日0 の後に増産できないが1件・数量 150、日1 の後に0件 | 順4 で `進捗‰ < 所要労働‰` を条件にする(日1 も持ち越し 1150 で立つ) | **核心**。M3: 条件を `household.ProductionProgressPermille < recipe.LaborPermille` にする / 赤 |
| 7 | `NeedGenerationSystemTests` #8・#9(書き換え) | 上の「9. 既存テストの追随」 | 能力の欄を読まない | — |
| 8 | `HouseholdSystemTests` `GateStaysClosedWhenInputsRemainOnAZeroProductionDay` | 破産中・販売在庫 0・**生産量 0 だが入力は1回分ある**世帯(Miller、工房在庫[穀物] = 2)→ 付け替えない。同職業の相方を置く(担い手 2) | c を生産量 0 だけで判定する(旧版) | **核心**。M4: `RunsFromInputs(...) != 0` の早期 return を消す / 赤 |
| 9 | `HouseholdSystemTests` `GateStaysClosedForARecipeWithoutInputs` | 入力0件のレシピの世帯(破産中・販売在庫 0・生産量 0)で `IsGateOpen` が false | 空の min を 0 にする | **核心**。M5: `RunsFromInputs` が入力0件で 0 を返す / 赤(既存の `ProductionRunsWithoutInputsUpToCapacity` も赤の見込み) |
| 10 | `HouseholdSystemTests` `ReassignmentTouchesNothingButTheOccupation`(書き換え) | 付け替えで進捗‰ が 0、`ProductionCapacityRuns` は不変、他の欄は不変 | 付け替えで進捗‰ を戻さない。能力・生産量まで消す | **核心**。M6: `ProductionProgressPermille = 0` の行を消す / 赤 |
| 11 | `BuyerDemandTests` `ReservesSubtractTheExpectedStock` | パンの目標 3・世帯在庫 1・相場基準 20 → 取り置き 40。小麦粉の目標 5・工房在庫 2・相場基準 7 → 運転資金 21 | 目標在庫をそのまま掛ける(60 / 35)。在庫の欄を取り違える(必需に工房在庫、入力に世帯在庫) | **核心**。M7: 両ループを `(long)target * reference` に戻す / 赤 |
| 12 | `BuyerDemandTests` `ReservesDoNotGoNegativeWhenStockExceedsTheTarget` | パンの世帯在庫 5(目標 3)・小麦粉の工房在庫 9(目標 5)→ 取り置き 0・運転資金 0、**嗜好の行の `CashCap` が流動資金そのものから求まる** | `max(0, …)` を落とす(取り置きが負になり、嗜好の母数が流動資金を超える) | M8: `Math.Max(0, …)` を外す / 赤 |
| 13 | 既存の `ReservesSkipItemsWithoutReference`(#25)・`CashCapReflectsTheStagedAvailableFundsPerPurpose` | 在庫 0 の世帯なので 60 / 35 のまま緑 | — | — |
| 14 | `M0CalibrationTests` `M0SatisfiesFloorPriceBalanceWithinTwoPercent_C`(書き換え) | 上の「7.」の式で5職とも ±540 以内 | 鍛冶を 1000‰ に戻す(年間 156 回、収支 +8,520)。年間実行回数を日ごとの floor × 120 のまま | **核心**。M9: `BuildM0` の鍛冶を `laborPermille: 1000` に戻す / 赤。**書き換える前のテストには当てても緑である**(#218 決定ログ5 の訂正) |
| 15 | `StateHasherTests` `HashChangesWhenProductionProgressChanges` / `HashChangesWhenProductionCapacityChanges` | 既存の #12・#13 と同じ形 | ハッシュへの書き忘れ | — |
| 16 | `HouseholdStateTests` | 2欄の setter が負を拒む | 検証を落とす | — |
| 17 | `TradePipelineTests` `SomeBrewerIsStillProducingAfterDaySixty` | 上の「8.」 | 規則3 を戻す(差し引く前は全シード day 6〜16 に止まった。#218 決定ログ2) | **核心**。M7 で赤(seed 1/2/3/42 の4シードの見込み)。**M1 での結果は測っていない**(決定ログ2 に「差し引きのみ」の行が無い)ので期待は置かない |

**変異は [`mutator`](../../.claude/agents/mutator.md) が測る。** M1〜M9 はレビューの巡が閉じた後にまとめて渡す。結果は各テストの doc コメントへ転記する。

## 呼び出し側を持たないコード(規則7)

**無い。** 新しい関数(`CapacityRunsFromProgress` / `RunsFromInputs`)は同じタスクで `ProductionSystem` と `OccupationReassignment` から呼ばれ、全走行のテスト(#17 と既存の `TradePipelineTests`)が通る。

## 編集してよい文書

- なし(コードとテストのみ)。GDD02d §4.4 の表と TDD01 §3.2・§3.8 は本タスクの凍結コミットで直してある

## このタスクで特に効く規約

- **進捗‰ は int。** 積は `所要労働‰ × 生産量` ≤ 1300 × 12 程度で溢れないが、既存どおり `checked` を外さない
- **`Dictionary` を使わない。** 醸造の最終稼働日は世帯 Id を添字にした配列で持つ
- **係数に単位コメント**(鍛冶の 1300 は「‰」)

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **「核心」印の変異(M1〜M9)を `mutator` が実測し(レビューの巡が閉じた後)、結果を doc コメントへ転記した**
- [ ] `SomeBrewerIsStillProducingAfterDaySixty` が4シード(1/2/3/42)とも緑(#237 の閉じる条件。seed 7 は #239)
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
