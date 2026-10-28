# W5-03: プレイヤーが Need を持つ NPC に売るときの購入規則と感謝の段階

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#253](https://github.com/stama72/visionary/issues/253)              |
| 根拠     | [GDD06 §6.1](../03-gdd/06-trade-and-negotiation.md)(購入規則・当てる Need・予算の時点・1日1回) / [GDD01 §3.6](../03-gdd/01-trust-and-conversation.md)(感謝の段階)・§6.1(開示する3理由) / [GDD02b §8.1](../03-gdd/02b-consumption-and-household.md)(差し引き) / [GDD02c §2.1](../03-gdd/02c-price-and-budget.md)(予算・母数) / [TDD01 §3.2・§3.3 段4・段8・§3.8](../04-tdd/01-sim-core-and-m0.md) / [#245](https://github.com/stama72/visionary/issues/245) 決定1〜10 / [#261](https://github.com/stama72/visionary/issues/261) 決定1・3 |
| ブランチ | `feat/253-player-sale-gratitude`                                     |
| worktree | 本体(`visionary/`)。パイプラインもここで走る                      |

**仕様は GDD06 §6.1 と GDD01 §3.6 が持つ。** 本書は「どう作るか」だけを書く。やることは5つ。

| # | 何を | 正 |
| - | ---- | -- |
| 1 | 感謝の段階の純関数(評価額・余剰の比率‰・段階) | GDD01 §3.6 |
| 2 | プレイヤーの販売 `PlayerSale.Execute`: 当てる Need の選択 → 1日1回 → その時点の予算 → 数量 → 約定 → Need の差し引き → 段階 | GDD06 §6.1 |
| 3 | 買い手側だけを動かす約定 `TradeSettlement.ExecutePlayerSale`(帳簿の相手はプレイヤーの予約 Id、仕入れ移動平均は用途で更新) | TDD01 §3.2 / GDD06 §6.1 |
| 4 | Need の差し引きの対応表を `NeedDeduction` に切り出し、夕方の段8 とプレイヤーの販売の両方がそれを通る | GDD02b §8.1 / TDD01 §3.3 段8 |
| 5 | `HouseholdState.PlayerOfferDayIndex`(プレイヤーが最後に値を出した日)とハッシュへの追加 | TDD01 §3.2・§3.8 |

## フェーズ1 で決めたこと

決定の中身は GDD06 §6.1 と GDD01 §3.6 に書いた(開発者の裁定。選択肢は引き継ぎメモ)。ここには実装の形だけを置く。

### プレイヤー側の状態は触らない

**`Player` 区分(所持金・所持品・居場所)は [#263](https://github.com/stama72/visionary/issues/263) が `World` に置く。本タスクの時点では存在しない。** `PlayerSale.Execute` は**世帯の側だけ**を動かし、プレイヤー側に要る値(数量・支払額)を結果で返す。所持金への加算・所持品からの減算・「提示数量 ≤ 所持品」の検査・居場所と6〜17時の検査(GDD06 §6.4)は、#263 のコマンドが `Execute` の前後で行う。

- **残る穴**: `Execute` だけを呼ぶと、世帯の流動資金は減るがプレイヤーの所持金は増えない(貨幣が消える)。本タスクでは呼び出し側が無いので走行には現れない。#263 がコマンドの中で両側を動かす

### プレイヤーの予約 Id は `HouseholdState` の定数に置く

```csharp
public const int PlayerId = int.MaxValue - 1;   // TDD01 §3.2。窓口(ExternalMarketSellerId)と同じ形
```

帳簿の相手 Id に要るので本タスクで置く。#263 の `Player` 区分も同じ定数を読む。

### 予算はコマンドの中で `BuyerDemand.Build` から作り直す

夕方の段4(`TradeSystem.Step` の冒頭)と同じ呼び方をする:

```csharp
int outputItemId = definition.Recipes[(int)household.Occupation].Outputs[0].ItemId;
var demand = buyerDemand.Build(
    world, household,
    household.MemoHasPreviousOffer[outputItemId] == 1,
    household.MemoPreviousOfferPrice[outputItemId]);
```

**朝の予算を World に控えない**(GDD06 §6.1)。`BuyerDemand` は `PlayerSale` のコンストラクタで1つ持つ(`TradeSystem` と同じ)。`Build` は乱数を開かないので、`Step` の外から呼んでよい(TDD01 §3.1 の「`Step` の外からの利用 → 例外」に当たらない)。

### 数量の資金の上限は「用途に使える資金」で切る — `DemandLine.AvailableFunds` を足す

GDD06 §6.1 の数量は「**用途に使える資金**で払える数」である。流動資金ではない(耐久・生産の入力は必需の取り置きを除いた残り、嗜好はさらに運転資金を除いた残り。GDD02c §2.1)。`BuyerDemand.BuildLine` が既に計算して `CashCap` に使っている `availableFunds` を、`DemandLine` の新しい欄 `AvailableFunds`(単位: 貨幣)に入れる。**`HouseholdDemand.NecessityReserve` / `WorkingCapital` から計算し直さない** — `Build` は耐久と生産の入力の行で `workingCapital: 0` を渡しており、合計から組み直すと食い違う。

```
資金で払える数 = FloorDiv( line.AvailableFunds , 価格 )
```

### 訂正(フェーズ2 の `IMPL-BLOCKED` を受けて)

**凍結時の仕様は `PlayerSaleOutcome.CannotAfford`(価格 ≤ 予算だが資金で 0 個)を持ち、テスト #6 でそれを要求していた。到達できない。** 予算は現金上限で頭を押さえられ、現金上限は `FloorDiv(AvailableFunds, max(1日分, 1)) ≤ AvailableFunds` なので、予算以下の価格なら1個は必ず払える。GDD06 §6.1 の該当文(フェーズ1 が同じブランチで書いた)も同じ誤りだったので直した。値を外し、手順7 を番人にし、テスト #6 を差し替えた。**実装済みの `CannotAfford` は外すこと。**

### 段階の境目は `PlayerSale` の定数に置く

```csharp
public const int GratefulThresholdPermille = 150;        // 単位: ‰。150 = 15%。これ以上で「感謝」(GDD01 §3.6。初期値)
public const int DeepGratitudeThresholdPermille = 400;   // 単位: ‰。400 = 40%。これ以上で「深い感謝」(同)
```

`WorldDefinition` のコンストラクタ引数にしない。境目は台詞の照合軸(#46)と対で動くもので、比較実験の可変軸ではない。

## スコープ

**含む:**

- `PlayerSale`(新規)、`PlayerSaleOutcome` / `PlayerSaleResult`(新規)
- `NeedDeduction`(新規)と、`TradeSystem.SubtractPurchasesFromNeeds` の対応表をそれに置き換えること(**振る舞いは変えない**)
- `TradeSettlement.ExecutePlayerSale`(新規)
- `DemandLine.AvailableFunds`(新しい欄)と `BuyerDemand.BuildLine` での設定
- `HouseholdState.PlayerId`・`HouseholdState.PlayerOfferDayIndex` と、`StateHasher` への追加・`StateHasherCoverageTests` の期待一覧の更新

**含まない:**

- `Player` 区分・コマンド・`SimHost`・`Query`・居場所と時間帯の検査([#263](https://github.com/stama72/visionary/issues/263))
- `Metrics` の都市内の約定からプレイヤーとの約定を外すこと([#264](https://github.com/stama72/visionary/issues/264)。TDD01 §3.2)。本タスクの後もプレイヤー不在の走行には行が現れないので、既存の出力は変わらない
- 取引の記録([#264](https://github.com/stama72/visionary/issues/264))、台詞の選択([#46](https://github.com/stama72/visionary/issues/46) で済み。本タスクは `Gratitude` を返すだけ)、`DomainEvent`([#263](https://github.com/stama72/visionary/issues/263))

**変えない既存コード**(規則8):

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `BuyerDemand.Build` の予算の式 `min(ApplyPermille(基礎値, 在庫圧力‰), 現金上限, 利潤上限)`(`BuildLine` → `BuyerBudget.Budget`) | GDD02c §2.1 | 一致(`BuildLine` と GDD02c 159行を突き合わせた)。**本タスクは欄を1つ足すだけで、値は変えない** |
| `BuyerBudget.AvailableFunds` の母数の段階(必需 = 流動資金 / 耐久・入力 = − 必需の取り置き / 嗜好 = − 取り置き − 運転資金、負なら0) | GDD02c §2.1 の表・§2.2 | 一致 |
| `BuyerDemand.Build` の行の並び(必需 → 耐久 → 生産の入力 → 嗜好、同一用途は品目 Id 昇順) | GDD02b §3.2 / GDD06 §6.1(遠方在庫だけのときの用途) | 一致(`HouseholdDemand.Lines` の doc と `Build` の4つのループ) |
| `DisclosedNeeds` の3理由(生産停止・工具切れ・遠方在庫) | GDD01 §6.1 | 一致。`PlayerSale` は `DisclosedNeeds.Of` を**呼ばない**(Id 昇順に並べ替えた列は要らない)が、同じ3理由に絞る |
| `TradeSystem` 段8 の差し引き(理由 → 引く用途の対応、0 以下で `RemoveAll`、格納順のまま走査) | GDD02b §8.1 の表 / TDD01 §3.3 段8 | 一致。**本タスクは対応表を `NeedDeduction.Subtracts` へ移すだけで、結果は変えない** |
| `TradeSettlement.Execute` / `ExecuteImport` / `ExecuteExport` | TDD01 §3.2 / GDD02b §3.2 | 変えない。`ExecutePlayerSale` が `ExecuteImport` と同じ形で買い手側だけを動かす |

## 作るもの

### 1. 感謝の段階(GDD01 §3.6)— `PlayerSale` の静的メソッド

```csharp
/// 評価額 = min( ApplyPermille(line.BaseValue, line.StockPressurePermille) , 利潤上限 )。
/// 利潤上限は line.HasProfitCap が真のときだけ min に入れる。現金上限(line.CashCap)は入れない。
public static int Evaluation(in DemandLine line);

/// s‰ = CeilDiv( 1000 × (evaluation − price) , evaluation )。積は long で取る。
/// evaluation ≤ 0 または price < 1 または price > evaluation なら ArgumentOutOfRangeException
/// (成立した取引では 評価額 ≥ 予算 ≥ 価格 ≥ 1。GDD06 §6.1)。
public static int SurplusPermille(int evaluation, int price);

/// s‰ < 150 → Reluctant / 150 ≤ s‰ < 400 → Grateful / 400 ≤ s‰ → DeepGratitude。
/// s‰ < 0 なら ArgumentOutOfRangeException。
public static Gratitude GratitudeOf(int surplusPermille);
```

- **評価額 ≥ 予算である**(予算の min の項が評価額の項の上位集合)。`price ≤ line.Budget` が通った後なら、`SurplusPermille` の前提は崩れない
- 具体例(GDD01 §3.6 の工具の例): `BaseValue = 348`(相場基準 290 × 1200‰)、`StockPressurePermille = 1500`、`CashCap = 300`、利潤上限なし → `Budget = 300`、`Evaluation = 522`。価格 300 → `CeilDiv(1000 × 222, 522) = CeilDiv(222000, 522) = 426`(222000 / 522 = 425.28…)→ `DeepGratitude`

### 2. `PlayerSale.Execute`(GDD06 §6.1)

```csharp
public enum PlayerSaleOutcome
{
    Accepted = 1,             // 成立
    NoNeed = 2,               // 開示される Need が (世帯, 品目) に無い。1日1回に数えない
    AlreadyOfferedToday = 3,  // 同じ (世帯, 品目) に今日すでに値を出した
    OverBudget = 4,           // 価格 > 予算。1日1回に数える
}
// 「価格 ≤ 予算だが資金で 0 個」は値を持たない。起きないからである(手順7。GDD06 §6.1)

public readonly record struct PlayerSaleResult
{
    public PlayerSaleOutcome Outcome { get; init; }
    public int NeedId { get; init; }              // 当てた Need の Id。NoNeed のときだけ -1
    public DemandPurpose Purpose { get; init; }   // 当てた用途。NoNeed のときは意味を持たない(既定値)
    public int Quantity { get; init; }            // 約定した個数。Accepted 以外は 0
    public int UnitPrice { get; init; }           // プレイヤーの値(引数そのまま)
    public int Payment { get; init; }             // Quantity × UnitPrice(checked)。Accepted 以外は 0
    public Gratitude? Gratitude { get; init; }    // Accepted のときだけ値を持つ
}

public sealed class PlayerSale
{
    public PlayerSale(WorldDefinition definition);

    public PlayerSaleResult Execute(World world, int householdId, int itemId, int unitPrice, int offeredQuantity);
}
```

**`Execute` の手順(順序は仕様である):**

1. **引数の検査。** `householdId` が `world.Households` の範囲外、`itemId` が `definition.ItemCount` の範囲外、`unitPrice < 1`、`offeredQuantity < 1` のいずれかなら `ArgumentOutOfRangeException`。**Result にしない** — 呼び出し側(#263)が先に検証する入力である(GDD06 §6.1「プレイヤーの値は 1 以上」)
2. **当てる Need を選ぶ。** `world.Needs` を走査し、`TargetHouseholdId == householdId && ItemId == itemId` の Need のうち、理由が工具切れ・生産停止・遠方在庫のものを集める。**優先順は 工具切れ → 生産停止 → 遠方在庫**(GDD06 §6.1 の表)。同じ (世帯, 理由, 品目) は1件しか立たない(GDD02b §8.1 の同一性)ので、優先順だけで1件に決まる。1件も無ければ `NoNeed` を返す(**状態を何も変えない**)
3. **1日1回。** `household.PlayerOfferDayIndex[itemId] == world.Now.DayIndex`(int へ checked で直して比べる)なら `AlreadyOfferedToday` を返す(状態を変えない)
4. **記録する。** `household.PlayerOfferDayIndex[itemId] = (int)world.Now.DayIndex`(checked)。**ここより後のどの結果でも記録は残る**
5. **予算を作り直し、行を選ぶ。** 上の「予算はコマンドの中で…」の形で `demand` を作る。行は: 工具切れ → `Purpose == Durable` の行 / 生産停止 → `Purpose == ProductionInput && ItemId == itemId` の行 / 遠方在庫 → `ItemId == itemId` の**最初の**行(`demand.Lines` の並びがそのまま走査順)。見つからなければ `InvalidOperationException`(Need があるのに需要の行が無いのはモデルの欠陥である)
6. **価格のゲート。** `unitPrice > line.Budget` なら `OverBudget`。**ちょうど等しければ通る**(GDD06 §6.1「価格 ≤ 予算」)
7. **数量。** `quantity = min(offeredQuantity, need.Quantity, FloorDiv(line.AvailableFunds, unitPrice))`。**手順6 を通った後は必ず 1 以上である**(価格 ≤ 予算 ≤ 現金上限 = `FloorDiv(AvailableFunds, max(1日分, 1))` ≤ `AvailableFunds`。GDD06 §6.1)。0 なら `InvalidOperationException`(予算の式か `AvailableFunds` の設定が壊れている。正常系では発火しない番人)
8. **約定。** `TradeSettlement.ExecutePlayerSale(world, household, line.Purpose, itemId, quantity, unitPrice, definition.AcquisitionCostSmoothingPermille)`
9. **Need の差し引き。** `NeedDeduction.SubtractPurchase(world, householdId, line.Purpose, itemId, quantity)`
10. **段階。** `GratitudeOf(SurplusPermille(Evaluation(line), unitPrice))`。**`line` は手順5 で作った、約定前の行である**(値を出した時点の評価額。GDD01 §3.6)。約定後に作り直さない

- **手順2 を手順3・4 より前に置くのは、Need が無い値を1日1回に数えないためである**(GDD06 §6.1)。逆にすると、Need が無い品目に値を出しただけで `PlayerOfferDayIndex` が記録される。予算について何も漏らしていないのに状態が変わる
- **境界の具体例(1日1回)**: 0日目の 10時(`Now.Value = 10`)に `OverBudget` → 同じ (世帯, 品目) に 0日目の 17時(`Now.Value = 17`)で `AlreadyOfferedToday` → 1日目の 6時(`Now.Value = 30`)では手順4 まで進む。別の品目なら 0日目のうちに通る
- **プレイヤーの値の時刻は検査しない。** 6〜17時の検査は #263 のコマンド(GDD06 §6.4)

### 3. `TradeSettlement.ExecutePlayerSale`(TDD01 §3.2)

`ExecuteImport` と同じ形・同じ検査で、**相手 Id だけが `HouseholdState.PlayerId`** である。

- 流動資金 −= `checked((int)((long)quantity × unitPrice))`
- 必需・嗜好 → 世帯在庫、生産の入力・耐久 → 工房在庫(`Execute` の手順3 と同じ分岐)
- 帳簿は**買い手側の1行だけ**(`Purchase`・相手 = `PlayerId`・`Cash`・`OccurredAt = world.Now`・単価 = プレイヤーの値)。プレイヤーは帳簿を持たない(TDD01 §3.2)
- 仕入れ移動平均単価は**生産の入力と耐久のときだけ**更新する(`Execute` の手順5 と同じ。#261 決定3)
- `ExecuteImport` と本体を共有する private の補助関数に切り出してよい。**`ExecuteImport` の外から見える振る舞い(相手 Id が窓口であること)は変えない**

### 4. `NeedDeduction`(GDD02b §8.1)

```csharp
public static class NeedDeduction
{
    /// その理由の Need から、その用途の購入を引くか(GDD02b §8.1 の表)。
    /// 生産停止 ← 生産の入力 / 工具切れ ← 耐久 / 困窮 ← 必需 / 遠方在庫 ← 4用途すべて / 増産できない ← どれも引かない。
    public static bool Subtracts(NeedReason reason, DemandPurpose purpose);

    /// 1件の購入を、その世帯の Need から引く。world.Needs を格納順のまま走査し、
    /// TargetHouseholdId と ItemId が一致し Subtracts(reason, purpose) が真の要素の Quantity から quantity を引く。
    /// 0 以下になった要素を RemoveAll で消す。NextNeedId には触らない。
    public static void SubtractPurchase(World world, int householdId, DemandPurpose purpose, int itemId, int quantity);
}
```

- **`TradeSystem.SubtractPurchasesFromNeeds` の `switch` を `Subtracts` の和に置き換える**: `subtract = Σ_{p ∈ 4用途, Subtracts(need.ReasonCode, p)} bought[p][itemId]`。走査・`RemoveAll` の位置は変えない。段8 の既存テストが緑のままであること
- `Subtracts` は未知の理由・用途で `ArgumentOutOfRangeException`
- **同じ購入を複数の Need から引く**(段8 と同じ。GDD02b §8.1 の表は購入を Need の間で配分しない)。生産停止に当てた薪は、同じ品目の遠方在庫からも引かれる(GDD06 §6.1)

### 5. `HouseholdState.PlayerOfferDayIndex`(TDD01 §3.2・§3.8)

- `public int[] PlayerOfferDayIndex { get; }`。長さ = 品目数。**コンストラクタで全要素 −1**(未提示。日の通し番号は 0 から始まるので、0 で初期化すると 0日目の最初の値が `AlreadyOfferedToday` になる)
- 単位: 日の通し番号(`Tick.DayIndex`)。毎日戻すシステムは作らない(TDD01 §3.8)
- `StateHasher` の Households 区分に、既存の欄の**後ろ**に足す(区分タグは動かさない)。`StateHasherCoverageTests` の `HouseholdState` の期待一覧に足す

## 呼び出し側を持たないコード(規則7)

本タスクの `PlayerSale.Execute` は**パイプラインのどのシステムからも呼ばれない。** 呼ぶのは #263 のコマンドである。

| 作るもの | 呼ぶ側 | 呼ぶ側が持つもの |
| -------- | ------ | ---------------- |
| `PlayerSale.Execute` | #263 の売るコマンド | 居場所と6〜17時の検査(GDD06 §6.4)、`offeredQuantity ≤ 所持品` の検査、`unitPrice ≥ 1` の検査、結果に従って所持金 += `Payment`・所持品 −= `Quantity` |
| `PlayerSaleResult.Gratitude` | #263 の `Query` / `DomainEvent` → 台詞(#46 の `DialogueSelector`)・日誌(#47) | 段階を台詞の照合軸へ渡す |
| `HouseholdState.PlayerId` | #263 の `Player` 区分 / #264 の `Metrics` の除外 | — |
| `NeedDeduction.SubtractPurchase` | 本タスクの `PlayerSale.Execute`(段8 は `Subtracts` を使う) | — |

**統合テストの保護を受けない経路**: コマンド → `Execute` → プレイヤーの所持金の配線は #263 のテストが持つ。本タスクのテストは `Execute` を直接呼ぶ。

## 落ちるべき条件(テスト)

テストは `tests/Visionary.Sim.Tests/Systems/PlayerSaleTests.cs` と `NeedDeductionTests.cs`(新規)に置く。世界は `EconomySystemTestFixtures` から作り、Need は `world.Needs` へ直接置く。**`Execute` の前に `TradePlanningSystem` を1回走らせて控えの欄を埋める**(控えが 0 のままだと利潤上限が立たず、生産の入力の行がテストの意図と違う予算になる)。

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心(当てる変異 / 期待) |
| - | ------ | -------- | -------------------- | ------------------------- |
| 1 | `GratitudeOf_SplitsAtThresholdsInclusiveOnTheUpperSide` | 149 → 渋々 / 150 → 感謝 / 399 → 感謝 / 400 → 深い感謝 / 0 → 渋々 | 境目の不等号の向き | **核心**: `GratitudeOf` の `< GratefulThresholdPermille` を `<=` に / 赤 |
| 2 | `SurplusPermille_RoundsUp` | (評価額 1003, 価格 853) → 150(149.55… の切り上げ)。(522, 300) → 426。(10, 10) → 0 | 切り捨て・四捨五入 | **核心**: `CeilDiv` を `FloorDiv` に / 赤 |
| 3 | `Evaluation_ExcludesCashCapButKeepsProfitCap` | (a) §1 の工具の例で `Evaluation = 522`(`Budget = 300`)。(b) `HasProfitCap = true, ProfitCap = 200`、第1項 522 → 200。(c) `HasProfitCap = false, ProfitCap = 0` → 522 | 評価額に現金上限を入れる / 利潤上限を外す / `HasProfitCap` を見ずに `ProfitCap` を min に入れる | **核心**: `Evaluation` が `line.Budget` を返す / 赤((a)) |
| 4 | `Execute_AcceptsAtExactlyTheBudgetAndRejectsOneAbove` | 同じ状態の世界を2つ作り、片方に `unitPrice = line.Budget` → `Accepted`、もう片方に `Budget + 1` → `OverBudget`(`Quantity = 0`、在庫・資金・帳簿・Need が不変) | 価格のゲートの不等号 | **核心**: 手順6 の `>` を `>=` に / 赤 |
| 5 | `Execute_QuantityIsBoundByOfferNeedAndPurposeFunds` | 3つの場合でそれぞれ別の項が最小になる: 提示数量 / Need の数量 / `FloorDiv(AvailableFunds, 価格)`。**3つ目は、必需の取り置きで `AvailableFunds < LiquidFunds` になる生産の入力の行で作る**(流動資金で切ると1個以上多く売れる値にする) | 数量の上限を流動資金で切る / Need の数量を見ない | **核心**: 手順7 の `line.AvailableFunds` を `household.LiquidFunds` に / 赤 |
| 6 | `Execute_PriceAtBudgetAlwaysAffordsAtLeastOne` | 工具(1日分の数量 1 なので `CashCap = AvailableFunds`)で、現金上限が予算を決めている状態(`Budget == CashCap == AvailableFunds`)を作り、`unitPrice = Budget`・提示数量 2・Need の数量 2 → `Accepted`・`Quantity = 1`・買い手の流動資金が `LiquidFunds − Budget` になる | 予算ちょうどで用途の資金を使い切る取引を拒む(番人の条件を `quantity <= 1` などで書き違える・資金の上限を `AvailableFunds − 1` で取る)。**切り上げと切り捨ての取り違えは捕まえない**(`AvailableFunds / 価格` が割り切れる。そちらは #5 の3つ目が持つ) | — |
| 7 | `Execute_SecondOfferOnTheSameDayIsRefusedRegardlessOfTheFirstOutcome` | 0日目に `OverBudget` → 同日に予算以下の値で `AlreadyOfferedToday`(状態不変)。翌日(`Now` を 24 進める)には通る。同日でも別品目は通る | 1日1回を NPC 単位・品目なしで数える / 成立したときだけ記録する / 日をまたいで残る | **核心**: 手順4 を手順7 の後(成立したときだけ記録)へ動かす / 赤 |
| 8 | `Execute_NoNeedDoesNotConsumeTheDailyOffer` | (a) Need が無い品目 → `NoNeed`、`PlayerOfferDayIndex[item] == -1`。(b) 困窮だけ・増産できないだけの (世帯, 品目) → `NoNeed` | 開示しない理由を相手にする / Need の検査の前に記録する | **核心**: 手順3・4 を手順2 の前へ動かす / 赤((a)) |
| 9 | `Execute_PrefersTheReasonThatNamesThePurpose` | 薪(必需と生産の入力の両方に行がある品目)で: (a) 生産停止 + 遠方在庫 → `Purpose = ProductionInput`、工房在庫に入り、**2件とも**数量が減る。(b) 遠方在庫だけ → `Purpose = Necessity`(走査順で最初の行)、世帯在庫に入り、遠方在庫だけ減る。(c) 工具で工具切れ + 遠方在庫 → `Purpose = Durable` | 優先順の取り違え / 遠方在庫で最後の行を取る / 当てた Need しか引かない | **核心**: 手順2 の優先順で遠方在庫を生産停止より先に / 赤((a)) |
| 10 | `Execute_SettlesOnlyTheBuyerSideWithThePlayerAsCounterparty` | 成立後: 買い手の流動資金 −= `Payment`、帳簿に `Purchase`・相手 `PlayerId`・`OccurredAt = world.Now` の1行だけ増える。**他の世帯の帳簿・流動資金・工房在庫は不変**。`world.Market` 不変 | 相手 Id に窓口を書く / 売り手を探して動かす | — |
| 11 | `Execute_UpdatesAcquisitionCostOnlyForInputAndDurable` | 生産の入力・耐久で成立 → `PurchaseUnitCostAverage[item]` が `OfferPrice.UpdatedAcquisitionCost(旧, 価格, 平滑化‰)` に一致。必需で成立 → 不変 | 用途の分岐を落とす(暖房用の薪が原価に乗る) | **核心**: `ExecutePlayerSale` の移動平均の `if` を外す / 赤(必需の場合) |
| 12 | `Execute_GratitudeUsesTheLineBeforeSettlement` | 生産の入力の行で、約定により在庫圧力が変わる数量を売る。返る `Gratitude` が、約定前の `Evaluation(line)` から計算した段階と一致し、約定後に `Build` し直した行から計算した段階とは異なる(そうなる数値を選ぶ) | 約定後に予算を作り直して段階を出す | — |
| 13 | `Execute_RejectsInvalidArguments` | `unitPrice = 0`、`offeredQuantity = 0`、範囲外の世帯 Id・品目 Id → `ArgumentOutOfRangeException`、状態不変 | 0 円・0 個を Result や約定で通す | — |
| 14 | `NeedDeduction_SubtractsMatchesTheTable` | 5理由 × 4用途の20組すべてを GDD02b §8.1 の表と照合 | 対応表の写し間違い | **核心**: 遠方在庫の行から `Preference` を外す / 赤 |
| 15 | `StateHasher_ChangesWhenPlayerOfferDayIndexChanges` | 同じ世界の2つの複製で `PlayerOfferDayIndex[0]` だけ違う → ハッシュが異なる | ハッシュへ入れ忘れる(期待一覧は足したが `StateHasher` に書かない) | — |

- **段8 の既存テスト(`TradeScheduleTests` / `TradeSystemTests` の差し引き)が緑のままであること**が、#4 の置き換えの保護である。新しいテストは足さない
- **#9 の「薪」は、必需と生産の入力の両方に行を持つ職業の世帯で作る**(行が1つしか無い品目では (b) が `Purpose` の取り違えを捕まえない)

## 編集してよい文書

- なし(コードとテストのみ)。GDD06 §6.1・GDD01 §3.6・TDD01 §3.2・§3.8 はフェーズ1 で更新済み

## このタスクで特に効く規約

- **`world.Needs` の格納順に依存してよいのは差し引きの走査だけ。** 当てる Need は優先順で決めるので、`world.Needs` の並びで結果が変わってはならない(テスト #9 で、Need を逆順に格納しても同じ結果になることを1ケース足す)
- **積は long で取ってから checked で int へ戻す**(`1000 × (評価額 − 価格)`・`数量 × 価格`)
- 浮動小数点を使わない。比率は ‰ の整数、除算は `IntegerMath` を通す

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **「核心」印の変異を `mutator` が実測し(レビューの巡が閉じた後)、結果を doc コメントへ転記した**
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
