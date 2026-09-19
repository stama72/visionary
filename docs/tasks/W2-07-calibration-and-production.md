# W2-07: 世界定義の校正値と生産の改訂

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#96](https://github.com/stama72/visionary/issues/96)                |
| 根拠     | [GDD02a §1〜§5](../03-gdd/02a-production.md) / [GDD02d §3〜§5](../03-gdd/02d-external-market-and-money.md) / [GDD03 §2.2](../03-gdd/03-seasons-and-city.md) / [GDD02c §1.3](../03-gdd/02c-price-and-budget.md) / [TDD01 §3.2・§3.8](../04-tdd/01-sim-core-and-m0.md) |
| ブランチ | `feat/96-calibration-and-production`                                 |
| worktree | `visionary/`(本体)                                                  |

> **この文書は使い捨ての作業指示である。** 実装完了時点で凍結し、以後の正はコードと GDD/TDD。

## スコープ

**[#85](https://github.com/stama72/visionary/issues/85) で凍結した経済の分冊に、実装を追随させる 5 本の最初である。** 本タスクは「作る側」— 世界定義の値、生産、工具の摩耗、校正の検査 — だけを直す。値付け・予算([#97](https://github.com/stama72/visionary/issues/97))、外出と店の選択([#98](https://github.com/stama72/visionary/issues/98))、都市外市場([#38](https://github.com/stama72/visionary/issues/38))、Household([#39](https://github.com/stama72/visionary/issues/39))は後続であり、**本タスクではそれらの旧実装を壊さずに動かしたまま**にする。

**含まない:**

- **提示価格・予算・需要・店の選択・約定の規則の変更。** `OfferPrice` / `BuyerBudget` / `BuyerDemand` / `StoreChoice` / `TradeSystem` / `TradeSettlement` は、本タスクで消える欄(下記 §1)の参照先を付け替える**だけ**で、式は変えない。旧の式が新しい値(外部買値・導出した目標在庫)で動く中間状態は承知のうえである
- **`ErrandLaborLossPermille` に書き込む処理。** 外出が労働損失を書くのは #98。本タスクでは欄と読み手(`ProductionSystem`)だけを作り、値は初期値 0 のままである
- **`ProductionRuns` を読む処理。** ④のゲート(#39)と Need(#40)が読む。本タスクは書き手だけ
- **Need の生成**(#40)。生産量 0 の日に Need を立てるのは #40 の仕事
- **`vsim` / Runner の追随**(#41 まで `SyntheticLoadSystem` のまま)
- **`DomainEvent` の発行**

## 設計の前提(フェーズ1で決めたこと)

**実装の前に GDD/TDD を直してある(本ブランチの先行コミット)。実装はこの 6 点を仕様として読むこと。**

| どこ | 何を決めたか |
| ---- | ------------ |
| [GDD03 §2.2](../03-gdd/03-seasons-and-city.md) | **穀物と木炭の外部売値の季節係数の値**(穀物 1000/1300/700/1000‰、木炭 1000/1250/1000/750‰)。年平均 1000‰ で、整数価格が季節ごとに割り切れる値 |
| [GDD02d §4.4](../03-gdd/02d-external-market-and-money.md) | 校正表の検算を**実装と同じ整数演算**(構成員ごとの切り上げ、120 日の年)で解き直した。生活費は 225/日(薪の年平均が 2.25 個/人日)。**(c) の摩耗は現金の流出(投入労働 × 工具の床 ÷ 工具の寿命)で数える**。1 回あたりの摩耗費を切り上げて足す形は採らない(水車小屋番が −8/日 に見えて (c) を破る) |
| [GDD02a §4](../03-gdd/02a-production.md) / [GDD02c §1.3](../03-gdd/02c-price-and-budget.md) | 目標在庫の導出に使う「生産能力」は、**外出の労働損失を引く前の、構成員の労働力係数‰の合計**(親方 1000 + 徒弟 300 = 1300‰)と設備係数 1000‰ で評価する。日ごとに動く損失で物差しを揺らさない |
| [TDD01 §3.2・§3.8](../04-tdd/01-sim-core-and-m0.md) | 世帯の状態に**当日の実行回数**と**前日の外出の労働損失‰**を足す。**工具の累積摩耗の単位は ‰人日**。3 つともハッシュに含める(在庫から復元できない) |
| [GDD02a §1](../03-gdd/02a-production.md) | 生産能力の除算は**両方とも切り下げ**(`floor(floor(労働力合計‰ × 設備係数‰ ÷ 1000) ÷ 所要労働‰)`)。W2-03 の `ApplyPermille`(切り上げ)は設備係数が二値だったから実害が無かっただけであり、500‰ と労働損失で端数が生まれる本タスクで直す |
| [GDD02d §4.3](../03-gdd/02d-external-market-and-money.md) | 校正 (a) の非負・(b)・(c) の許容幅・(d)・(e) は**機械で守る** — `WorldDefinition.M0` から検査するテストを置く。守れないのは (a) の「1.2 倍前後」という狙いと §4.5 の床への張り付き |

### 出荷目標在庫と入力の目標在庫は表をやめ、生産能力から導く

[GDD02c §1.3](../03-gdd/02c-price-and-budget.md) が理由を持つ — 定数表のままだと、校正表の所要労働‰ を動かしたときに在庫比の物差しだけが古いまま残り、**それは踏んでも気付けない**。`WorldDefinition` の 2 つの jagged 配列(`ShipmentTargetStockByOccupation` / `InputTargetStockByOccupation`)を消し、生産能力から導く**メソッド**に置き換える。

### 生産能力の式は 1 か所にしか置かない

`ProductionSystem`(日次の実行回数)と `WorldDefinition`(目標在庫の物差し)が同じ式を使う。**書き分けると、片方の丸めを直したとき他方が黙ってずれる。** 式は `Recipe.CapacityRuns` に置く(レシピが所要労働‰ を知っている)。

### 摩耗は実行回数ではなく労働量で数える

[GDD02a §3.1](../03-gdd/02a-production.md)。所要労働‰ が 108 から 1000 まで違うので、回数で数えると木材加工は鍛冶の 12 倍の速さで工具を消費する。**`HouseholdState.ToolWearCount`(回)を `ToolWear`(‰人日)に改名する** — 単位が変わる欄を同じ名前で残すと、旧単位で書かれた呼び出しがコンパイルを通る。

## 作るもの

名前空間は `Visionary.Sim`(`Definition/` `World/` `Determinism/`)と `Visionary.Sim.Systems`(`Systems/`)。

### 1. `WorldDefinition` の改訂(`Definition/WorldDefinition.cs`)

**消す欄(コンストラクタ引数ごと)**:

| 欄 | 代わり |
| -- | ------ |
| `ProductionRunsPerToolWear` | `ToolLifeLaborDays`(N、人日)と導出値 `ToolDurabilityPerUnit` |
| `ShipmentTargetStockByOccupation` | メソッド `ShipmentTargetStock(occupation, itemId)` |
| `InputTargetStockByOccupation` | メソッド `InputTargetStock(occupation, itemId)` |

**足す欄(コンストラクタ引数。既存引数の後ろにこの順で並べる)**:

```csharp
/// <summary>
/// 外部売値の基準値(年平均)。添字 = itemId。単位: 貨幣/1単位(GDD02d §2.1・§4.4)。
/// <b>1次産品(どのレシピも出力しない品目)だけが 1 以上、都市生産品は 0</b>。
/// </summary>
public int[] ExternalSellPriceBase { get; }   // private でよい。読み口は ExternalSellPrice(itemId, season)

/// <summary>
/// 外部売値の季節係数‰。添字 = [itemId][(int)Season]。長さ itemCount × 4(GDD02d §5 / GDD03 §2.2)。
/// 都市生産品の行はすべて 1000(使わないが、黙って効く値を置かせない)。
/// </summary>
public int[][] ExternalSellPriceSeasonPermille { get; }   // 同上

/// <summary>
/// 外部買値。添字 = itemId。単位: 貨幣/1単位(GDD02d §2.1・§3)。<b>都市生産品だけが 1 以上、
/// 1次産品は 0</b>。提示価格の床であり、輸出の値でもある(#97 / #38 が読む)。季節係数は掛からない。
/// </summary>
public int ExternalBuyPrice(int itemId);   // 1次産品に対して呼ぶと ArgumentException

/// <summary>入力の緩衝日数 D。単位: 日。1以上(GDD02a §4)。</summary>
public int InputBufferDays { get; }

/// <summary>出荷日数。単位: 日。1以上(GDD02c §1.3)。</summary>
public int ShipmentDays { get; }

/// <summary>工具1個が尽きる労働量 N。単位: 人日。1以上(GDD02a §3.1)。</summary>
public int ToolLifeLaborDays { get; }

/// <summary>工具が無いときの設備係数‰。0〜1000(GDD02a §3)。</summary>
public int EquipmentPermilleWithoutTools { get; }

/// <summary>可処分時間 T。単位: 時間。1以上(GDD08 §3.1。GDD02a §2 の労働損失の分母)。</summary>
public int DisposableHours { get; }
```

**導出値(状態ではない。すべて定義から決まる)**:

```csharp
/// <summary>親方1・徒弟1 の労働力係数‰の合計(GDD02 §2.4 / GDD02a §2)。外出の損失を引く前の値。</summary>
public int NominalLaborPermille { get; }    // LaborPermilleByRank[Master] + LaborPermilleByRank[Apprentice]

/// <summary>どのレシピも出力しない品目か(= 都市外市場から来る1次産品か)。GDD02 §2.2。</summary>
public bool IsPrimaryItem(int itemId);

/// <summary>工具1個の耐久値 = N × 1000。単位: ‰人日(GDD02a §3.1)。</summary>
public int ToolDurabilityPerUnit { get; }

/// <summary>
/// 生産能力(実行回数/日)。労働力は <see cref="NominalLaborPermille"/>、設備係数は 1000‰ で評価する
/// (GDD02a §4 / GDD02c §1.3)。式は <see cref="Recipe.CapacityRuns"/>。
/// </summary>
public int ProductionCapacity(Occupation occupation);

/// <summary>1日の投入量 = 生産能力 × 必要数量。入力でない品目は 0(GDD02a §4)。</summary>
public int DailyInputQuantity(Occupation occupation, int itemId);

/// <summary>入力の目標在庫 = 1日の投入量 × D。入力でない品目は 0(GDD02a §4)。</summary>
public int InputTargetStock(Occupation occupation, int itemId);

/// <summary>出荷目標在庫 = 生産能力 × 出力数量 × 出荷日数。出力でない品目は 0(GDD02c §1.3)。</summary>
public int ShipmentTargetStock(Occupation occupation, int itemId);

/// <summary>当日の外部売値 = ApplyPermille(基準値, 季節係数‰[季節])(GDD02d §2.1)。</summary>
public int ExternalSellPrice(int itemId, Season season);   // 都市生産品に対して呼ぶと ArgumentException
```

**コンストラクタの検証**(既存欄と同じ書き方で。すべて `ArgumentNullException` / `ArgumentException` / `ArgumentOutOfRangeException`):

| 欄 | 拒むもの |
| -- | -------- |
| `externalSellPriceBase` | `null` / 長さ ≠ `itemCount` / 負 / **1次産品の欄が 0** / **都市生産品の欄が 1 以上** |
| `externalBuyPrice` | `null` / 長さ ≠ `itemCount` / 負 / **都市生産品の欄が 0** / **1次産品の欄が 1 以上** |
| `externalSellPriceSeasonPermille` | `null` / 行数 ≠ `itemCount` / 行が `null` / 行の長さ ≠ 4 / 要素が 0 以下 / **行の合計 ≠ 4000**(年平均 1000‰。GDD02d §5)/ **都市生産品の行に 1000 以外がある** |
| `inputBufferDays` / `shipmentDays` / `toolLifeLaborDays` / `disposableHours` | 0 以下 |
| `equipmentPermilleWithoutTools` | 負 / 1000 超 |

- **「1次産品」は `recipes` から決まる**(どのレシピの出力にも現れない品目)。価格表の側でそれを表現させると、レシピと価格表が食い違ったまま両方が通る。**検査は品目ごとに「ちょうど一方だけが 1 以上」を見る**
- **行の合計 4000 を機械で守るのは、校正 (c) が年平均で成立しているからである**([GDD02d §5](../03-gdd/02d-external-market-and-money.md))。1000‰ からずれると貿易収支が構造的に黒字か赤字になり、それは踏んでも気付けない
- **jagged 配列は行ごとに複製して持つ**(既存欄と同じ)

**`ExternalSellPrice(itemId, season)` は `ApplyPermille(基準値, 係数‰)` で切り上げる。** GDD03 §2.2 は「整数価格が季節ごとに割り切れる値」を選んであるが、**割り切れるかどうかに実装は依存しない**。

**`InitialWorkshopInputDays` の意味を変える。** 旧は「× 必要数量」(1 実行/日 の前提)だったが、**「× 1日の投入量」**にする。`WorldGenerator` の初期化式が `InitialWorkshopInputDays × DailyInputQuantity(occupation, input.ItemId)` になる(§6)。

**`BuildM0` の初期値**(すべて調整対象。[GDD02d §4.4](../03-gdd/02d-external-market-and-money.md) の校正表そのもの):

```csharp
// レシピ(GDD02d §4.4)。所要労働‰ で 1日の実行回数を作る(1300‰ ÷ 所要労働‰ = 7 / 6 / 6 / 12 / 1)。
Miller:     穀物 2 → 小麦粉 1,            laborPermille: 185
Baker:      小麦粉 1 + 薪 1 → パン 2,     laborPermille: 216
Brewer:     穀物 2 + 薪 1 → ビール 1,     laborPermille: 216
Woodworker: 木材 1 → 薪 3,               laborPermille: 108
Smith:      鉄鉱石 2 + 木炭 1 → 工具 1,   laborPermille: 1000

// 添字 = itemId(Grain, Timber, IronOre, Charcoal, Flour, Firewood, Bread, Beer, Tools)。
var externalSellPriceBase = new[] { 10, 9, 14, 12, 0, 0, 0, 0, 0 };   // 単位: 貨幣/1単位(年平均)
var externalBuyPrice      = new[] { 0, 0, 0, 0, 56, 10, 54, 72, 290 }; // 単位: 貨幣/1単位

// 添字 = [itemId][(int)Season](Spring, Summer, Autumn, Winter)。GDD03 §2.2。年平均 1000‰。
穀物: { 1000, 1300, 700, 1000 }   木炭: { 1000, 1250, 1000, 750 }   他の 7 品目: { 1000, 1000, 1000, 1000 }

// 初期在庫の取得原価(仕入れ移動平均の初期値)は床価格に揃える —
// 1次産品は外部売値の基準値、都市生産品は外部買値。
var initialAcquisitionCost = new[] { 10, 9, 14, 12, 56, 10, 54, 72, 290 }; // 単位: 貨幣/1単位

// 世帯在庫は春の目標在庫(薪 7日 × 4/日、パン 3日 × 2/日、ビール 1日 × 1/日。GDD02b §2)。
var initialHouseholdInventory = new[] { 0, 0, 0, 0, 0, 28, 6, 1, 0 }; // 単位: 個

initialLiquidFunds: 2400,            // 単位: 貨幣(GDD02d §4.4 (d))
initialWorkshopInputDays: 3,         // 単位: 日(× 1日の投入量。D と同じ 3 日ぶん)
initialToolStock: 1,                 // 単位: 個
opportunityCostBaseByOccupation: { 20, 20, 20, 20, 20 },   // 単位: 貨幣/1時間(GDD02c §2.4)
inputBufferDays: 3,                  // 単位: 日(GDD02a §4)
shipmentDays: 3,                     // 単位: 日(GDD02c §1.3)
toolLifeLaborDays: 13,               // 単位: 人日(GDD02a §3.1)
equipmentPermilleWithoutTools: 500,  // 単位: ‰(GDD02a §3)
disposableHours: 12,                 // 単位: 時間(GDD08 §3.1)
```

他の既存欄(最低利幅‰ 200、保持期間 7 日、目標在庫の日数、階層係数‰、許容乖離‰ 1200、予算比率‰、移動時間 1、平滑化係数‰ 250)は**変えない** — 予算側の欄は #97 が消す。

### 2. `Recipe.CapacityRuns`(`Definition/Recipe.cs` に追加)

```csharp
/// <summary>
/// 生産能力(実行回数)= floor( floor(労働力合計‰ × 設備係数‰ ÷ 1000) ÷ 所要労働‰ )(GDD02a §1)。
/// <b>両方の除算を切り下げる</b> — 端数の労働力ではレシピを1回完成できない。
/// </summary>
public int CapacityRuns(int laborPermille, int equipmentPermille);
```

- 負の引数は `ArgumentOutOfRangeException`。中間の積は `long`
- **内側は `FloorDiv(labor × equip, 1000)` であって `ApplyPermille`(切り上げ)ではない。** 例: 労働力 1275‰(徒弟が往復 1 時間出た翌日)・設備 500‰ → `floor(637.5) = 637`、所要労働 319‰ → **1 回**(内側を切り上げると 638 ÷ 319 = 2 回)

### 3. `HouseholdState` の改訂(`World/HouseholdState.cs`)

```csharp
/// <summary>累積した工具の摩耗。単位: ‰人日。0以上(GDD02a §3.1)。旧 ToolWearCount(回)を改名。</summary>
public int ToolWear { get; set; }

/// <summary>
/// 前日の外出の労働損失‰。0以上(GDD02a §2 / GDD06 §2)。順5 が当日の合計を書き(#98)、翌日の順1 が読む。
/// 本タスクでは書き手が無く、初期値 0 のままである。
/// </summary>
public int ErrandLaborLossPermille { get; set; }

/// <summary>当日の生産量(実行回数)。0以上(GDD02a §1)。順1 が毎日書く(0 の日も書く)。順3 の④のゲートと順4 の Need が読む。</summary>
public int ProductionRuns { get; set; }
```

- 3 つとも setter で負を拒む(`ToolWearCount` と同じ手書きのバッキングフィールド + 検証)
- **`ErrandLaborLossPermille` の上限は型では守らない。** `NominalLaborPermille` を超える値は `ProductionSystem` が `max(0, …)` で 0 に潰す(GDD02a §2)。T の上限で構造的に超えないのは #98 の側の保証である
- コンストラクタの初期値はいずれも 0

### 4. `StateHasher` の追随(`Determinism/StateHasher.cs`)

- `WriteInt32(hasher, buffer, household.ToolWearCount)` → `household.ToolWear`(**位置は変えない**)
- **`Section.Households` の書き込みの末尾に 2 つ足す**(TDD01 §3.8「既存の値を動かさず末尾へ足す」):

```csharp
WriteInt32(hasher, buffer, household.ErrandLaborLossPermille);
WriteInt32(hasher, buffer, household.ProductionRuns);
```

**[`StateHasherCoverageTests.ExpectedSectionElementMembers`](../../tests/Visionary.Sim.Tests/Determinism/StateHasherCoverageTests.cs) の `HouseholdState` の行を、`ToolWearCount` → `ToolWear`、`ErrandLaborLossPermille` と `ProductionRuns` の追加で更新する。** 凍結検査は欄を足した時点で必ず落ちる。落ちてから直すこと。

> **残る穴は凍結検査自身が doc で認めているものと同じである** — 期待一覧だけを更新して `Compute` を触らなければ緑になる。**ハッシュが実際に 2 欄を見ていることはテスト表 #12・#13 が押さえる。**

### 5. `ProductionSystem`(`Systems/ProductionSystem.cs`)

シグネチャは変えない。**手順(この順序が仕様。世帯Id 昇順に1世帯ずつ)**:

```
recipe       = definition.Recipes[(int)household.Occupation]
設備係数‰    = household.WorkshopInventory[Item.Tools] >= 1 ? 1000 : definition.EquipmentPermilleWithoutTools
労働力合計‰  = max( 0 , Σ( definition.LaborPermilleByRank[(int)world.Npcs[npcId].Rank] ) − household.ErrandLaborLossPermille )
                 npcId は household.MemberNpcIds を先頭から
生産能力      = recipe.CapacityRuns( 労働力合計‰, 設備係数‰ )

実行回数      = 生産能力
foreach input in recipe.Inputs:  実行回数 = min( 実行回数, FloorDiv(工房在庫[input.ItemId], input.Quantity) )

household.ProductionRuns = 実行回数        ← 0 の日も書く。ここまでは実行回数 0 でも必ず通る

if 実行回数 <= 0:  在庫も摩耗も動かさない。次の世帯へ

foreach input  in recipe.Inputs:  工房在庫[input.ItemId]  -= input.Quantity  × 実行回数
foreach output in recipe.Outputs: 工房在庫[output.ItemId] += output.Quantity × 実行回数

── 摩耗(GDD02a §3.1)。出力を加算した後に行う(W2-03 と同じ理由) ──
household.ToolWear += recipe.LaborPermille × 実行回数              ← ‰人日
worn = FloorDiv( household.ToolWear, definition.ToolDurabilityPerUnit )
if worn > 0:
    consumed = min( worn, 工房在庫[Item.Tools] )
    工房在庫[Item.Tools] -= consumed
    household.ToolWear   -= consumed × definition.ToolDurabilityPerUnit
    if 工房在庫[Item.Tools] == 0:  household.ToolWear = 0
```

**順序・境界を具体例で固定する**([process/02](../process/02-task-spec.md) 規則6):

- **`ErrandLaborLossPermille` は読むだけで、0 に戻さない。** 書き手は順5(#98)だけにする — 2 か所が書くと「どちらが最後に書いたか」が仕様になる。本タスクの範囲では値は常に 0 であり、テストが値を直接置いて読み手を確かめる(テスト表 #16・#17)
- **労働損失 100‰(徒弟が往復 4 時間)のパン屋(216‰)**: `(1300 − 100) ÷ 216 = 5.55 → 5 回`(損失なしなら 6 回)。**損失 1300‰ 以上なら 0 回**で、在庫も摩耗も動かず `ProductionRuns = 0`
- **工具が無い水車小屋番(185‰)**: `floor(1300 × 500 ÷ 1000) = 650`、`650 ÷ 185 = 3.51 → 3 回`(工具ありなら 7 回)。**0 回ではない** — 旧仕様の「工具が無ければ停止」は消えた
- **工具が無い鍛冶(1000‰)**: `650 ÷ 1000 → 0 回`。これが M0 で唯一「労働力不足」(GDD02b §8)が立つ組み合わせだが、Need を立てるのは #40
- **摩耗**: 木材加工(108‰ × 12 回 = 1296‰人日/日)は `N = 13`(13,000)で **11 日目に 1 個減る**(10 日目まで 12,960 < 13,000)。鍛冶(1000‰ × 1 回)は 13 日目。**旧の「回数 ÷ N」なら木材加工は毎日 1 個近く減っていた**
- **端数は次の工具へ持ち越し、尽きたら捨てる**(W2-03 と同じ。テスト表 #7〜#9)
- `Advance(1)` を `Tick.Zero` から呼ぶとエポックで 1 回だけ実行される(変わらない)

### 6. `WorldGenerator` の追随(`Definition/WorldGenerator.cs`)

```csharp
foreach (var input in recipe.Inputs)
{
    household.WorkshopInventory[input.ItemId] =
        definition.InitialWorkshopInputDays * definition.DailyInputQuantity(occupations[householdId], input.ItemId);
}
```

他は変えない。配置(区画・職業)の乱数の引き方も変えない — **同じシードから同じ配置が出ること**(テスト表 #21)。

### 7. 旧実装の参照先の付け替え(式は変えない)

| ファイル | 旧 | 新 |
| -------- | -- | -- |
| `Systems/BuyerDemand.cs` | `definition.InputTargetStockByOccupation[occupationId][itemId]` | `definition.InputTargetStock(household.Occupation, itemId)` |
| 同 | 耐久の目標 `ApplyPermille(ApplyPermille(ProductionRunsPerToolWear, ToolTargetStockPermille), 階層係数)` | `ApplyPermille(ApplyPermille(definition.ToolDurabilityPerUnit, ToolTargetStockPermille), 階層係数)`(単位が ‰人日になる) |
| 同 | 耐久の予想在庫 `工具在庫 × ProductionRunsPerToolWear − ToolWearCount` | `工具在庫 × definition.ToolDurabilityPerUnit − household.ToolWear` |
| `Systems/TradeSystem.cs` | `definition.ShipmentTargetStockByOccupation[occ][outputItemId]` | `definition.ShipmentTargetStock(household.Occupation, outputItemId)` |
| 同 | `TargetStockInUnits(purpose, targetStock, runsPerToolWear)` / `PurchaseQuantityInUnits(...)` | 第 3 引数を `durabilityPerTool`(= `definition.ToolDurabilityPerUnit`)に改名。式は `CeilDiv(値, durabilityPerTool)` のまま |
| `Systems/OfferPrice.cs` 他 | `ProductionRunsPerToolWear` を読む箇所があれば | `ToolDurabilityPerUnit` |
| `tests/.../EconomySystemTestFixtures.cs` | `BuildDefinition(...)` の引数 | 消えた 3 引数を外し、足した 8 引数に既定値を置く(1次産品/都市生産品の整合はレシピから決まるので、**既定の価格表はレシピを見て組み立てる**) |

**旧実装のテスト(`BuyerDemandTests` / `TradeSystemTests` / `TradePipelineTests` / `StoreChoiceTests` / `OfferPriceTests` / `WorldDefinitionTests`)は、この付け替えを反映して全部緑に保つ。** 期待値が「目標在庫の表の値」に依存していたテストは、導出値(生産能力 × 数量 × 日数)で同じ値になるように定義側の所要労働‰ を選び直す。**式の変更は本タスクに無い**ので、期待値が変わるのは単位が ‰人日になる耐久の行だけである。

### 8. 校正テスト(`tests/Visionary.Sim.Tests/Definition/M0CalibrationTests.cs` を新規作成)

**`WorldDefinition.M0` を入力に、[GDD02d §4.3](../03-gdd/02d-external-market-and-money.md) の (a)〜(e) を整数演算で検査する。** 浮動小数点を使わない(テストアセンブリは `BannedSymbols` の対象外だが、規約は同じ)。合計は `long`。**このテストの仕事は、校正表の値を動かした PR で落ちることである。**

共通の材料(`definition = WorldDefinition.M0`、`Calendar.DaysPerSeason` / `DaysPerYear`、`Season` の 4 値):

```
世帯 = WorldGenerator.Generate(definition, new RandomSource(seed: 1)).Households[0]
      (全世帯が親方1・徒弟1 なので、どの世帯でも同じ。DailyConsumption の式を再実装しない)
世帯の1日消費(i, 季節) = DailyConsumption.Quantity(definition, world, 世帯, i, 季節)
世帯の年間消費(i)      = Σ_季節 DaysPerSeason × 世帯の1日消費(i, 季節)
年間価格(j)            = 1次産品: Σ_季節 DaysPerSeason × ExternalSellPrice(j, 季節) / 都市生産品: DaysPerYear × ExternalBuyPrice(j)
床(i)                  = 1次産品: ExternalSellPriceBase(= ExternalSellPrice の年平均) / 都市生産品: ExternalBuyPrice(i)
季節最高値(j)          = 1次産品: max_季節 ExternalSellPrice(j, 季節) / 都市生産品: ExternalBuyPrice(j)
cap(o)                 = definition.ProductionCapacity(o)、H = definition.HouseholdsPerOccupation
```

| 条件 | 検査(都市生産品 i ごと / 職業 o ごと) |
| ---- | -------------------------------------- |
| (a) | `供給(i) = Σ_o H × cap(o) × 出力数量(o,i) × DaysPerYear` ≥ `需要(i) = HouseholdCount × 世帯の年間消費(i) + Σ_o H × cap(o) × 必要数量(o,i) × DaysPerYear + (i == Tools ? CeilDiv(Σ_o H × DaysPerYear × 所要労働‰(o) × cap(o), ToolDurabilityPerUnit) : 0)` |
| (b) | `輸入含有原価(1次産品) = 床`、`輸入含有原価(都市生産品) = CeilDiv(Σ_j 必要数量_j × 輸入含有原価_j, 出力数量)`(入力がすべて定義済みのレシピから順に埋める。レシピ数回まわして残ればレシピの循環として fail)。`輸入含有原価(i) < ExternalBuyPrice(i)` |
| (c) | `売上 = DaysPerYear × cap × 出力数量 × ExternalBuyPrice(出力)`、`仕入 = Σ_j cap × 必要数量_j × 年間価格(j)`、`摩耗 = CeilDiv(DaysPerYear × cap × 所要労働‰ × ExternalBuyPrice(Tools), ToolDurabilityPerUnit)`、`生活費 = Σ_i 世帯の年間消費(i) × 床(i)`(M0 で消費する品目はすべて都市生産品なので `年間消費 × ExternalBuyPrice`)、`収支 = 売上 − 仕入 − 摩耗 − 生活費`。**`|収支| × 100 ≤ 2 × 生活費`** |
| (d) | `取り置き = Σ_(必需 i) NecessityTargetStockDays[i] × 世帯の1日消費(i, Winter) × 床(i)`、`運転資金 = Σ_j cap × 必要数量_j × InputBufferDays × 季節最高値(j)`、`嗜好 = Σ_(嗜好 i) PreferenceTargetStockDays[i] × 世帯の1日消費(i, Winter) × 床(i)`。**`InitialLiquidFunds ≥ 取り置き + 運転資金 + 嗜好`** |
| (e) | `許容原価合計 = FloorDiv(ExternalBuyPrice(出力) × 出力数量 × 1000, 1000 + MinimumMarginPermille) − CeilDiv(ExternalBuyPrice(Tools) × 所要労働‰, ToolDurabilityPerUnit)`、`最高値での原価 = Σ_j 必要数量_j × 季節最高値(j)`。**`許容原価合計 ≥ 最高値での原価`** |

- **(c) の摩耗は年間の投入労働 × 工具の床 ÷ 寿命で、1 回あたりの摩耗費(切り上げ)× 回数ではない。** 後者は 02c §2.3 の利潤上限のための保守的な丸めであり、現金の流出ではない(フェーズ1 で解いた結果: 前者なら 4 職業 −227〜−230/年・鍛冶 +323/年で全部 ±540 に入り、後者なら水車小屋番が −960/年 で落ちる)
- **(d) の運転資金は季節最高値で見る**(冬に穀物が高いときでも初日の仕入が切り詰められない)。取り置きと嗜好は冬の消費(薪が最大)で見る
- テストは 5 本に分ける(条件ごとに 1 本)。**落ちたときにどの条件が破れたか**が名前で分かるようにする
- `RandomSource` のシードは何でもよい(消費量は配置に依らない)

## 落ちるべき条件(テスト)

**この節が完了条件そのもの。** 目標は「通ること」ではなく「**壊したときに落ちること**」である。

`WorldDefinition` を直接組み立ててよい(既存の `EconomySystemTestFixtures.BuildDefinition` を §7 のとおり直して使う)。**校正テスト(#22〜#27)以外は `BuildM0` の値に依存しないこと。**

| #  | テスト | 検証内容 | この実装ミスで落ちる | 核心 |
| -- | ------ | -------- | -------------------- | ---- |
| 1  | `CapacityRunsFloorsBothDivisions` | `CapacityRuns(1275, 500)` で所要労働 319‰ → **1**。`CapacityRuns(1300, 1000)` で所要労働 217‰ → **5** | 内側を `ApplyPermille`(切り上げ)で書く(638 ÷ 319 = 2)。外側を `CeilDiv` で書く(1300 ÷ 217 → 6) | **核心** |
| 2  | `ProductionCapacityUsesNominalLaborAndFullEquipment` | 労働力 1000/300、所要労働 185‰、工具なし係数 500‰ の定義で `ProductionCapacity` = **7** | 工具なし係数を掛ける(3)。親方だけを数える(5) |  |
| 3  | `TargetStocksAreDerivedFromCapacity` | 上の定義(穀物 2 → 小麦粉 1)で D = 3・出荷日数 3 → `DailyInputQuantity(穀物)` = **14**、`InputTargetStock(穀物)` = **42**、`ShipmentTargetStock(小麦粉)` = **21**、入力でも出力でもない品目は **0** | 必要数量 × 日数だけを返す(6 / 3)。生産能力を掛けるのを出荷側だけ忘れる | **核心** |
| 4  | `WorldDefinitionRejectsPricesInconsistentWithRecipes` | 1次産品の外部買値が 1 以上 / 1次産品の外部売値が 0 / 都市生産品の外部売値が 1 以上 / 都市生産品の外部買値が 0 を、それぞれ `ArgumentException` で拒む | 価格表の側で 1次産品を判定する(レシピと食い違っても通る)。片側だけ検査する |  |
| 5  | `WorldDefinitionRejectsSeasonRowsNotAveragingToBase` | 合計 3999 / 4001 の行、長さ 3 の行、要素 0、都市生産品の行に 1200 を、それぞれ拒む。合計 4000 で通る | 合計を検査しない(年平均が黙ってずれる)。`> 0` を `>= 0` と書く |  |
| 6  | `ExternalSellPriceAppliesSeasonCoefficient` | 基準 10・係数 `{1000,1300,700,1000}` → 春 10・夏 **13**・秋 **7**・冬 10。基準 12・係数 1250 → **15**。都市生産品に対して呼ぶと `ArgumentException` | 係数を掛けない。`(int)Season` ではない添字で引く |  |
| 7  | `ToolWearAccumulatesLaborNotRuns` | 所要労働 108‰・12 回/日・`N = 13` → **10 日目まで工具が減らず、11 日目に 1 個減って `ToolWear = 14256 − 13000 = 1256`** | 回数を足す(2 日目に減る)。`N` に 1000 を掛けない(1 日目に減る)。`>=` を `>` と書く | **核心** |
| 8  | `ToolWearCarriesOverAndConsumesMultipleTools` | `N = 1`(1000)・工具 3 個・所要労働 350‰・労働力係数を親方 2000‰ / 徒弟 500‰ にして 7 回/日 = 2450/日 → 1 日で **2 個減り、`ToolWear = 450`** | `consumed × 耐久値` を引かずに耐久値だけ引く(1450 が残る)。`ToolWear = 0` と代入する | **核心** |
| 9  | `ToolWearIsDroppedWhenToolsRunOut` | `N = 1`・工具 1 個・所要労働 400‰ × 3 回/日 = 1200/日 → 工具 0 個・**`ToolWear = 0`**。翌日は設備係数が工具なしの値で続く | 在庫 0 でもカウンタを残す。在庫を負にする |  |
| 10 | `ProductionContinuesAtReducedCapacityWithoutTools` | 労働力 1300‰・所要労働 185‰・工具 0 個・工具なし係数 500‰ → **3 回**実行し、在庫が動く | 工具なしを 0 回にする(旧仕様)。工具なし係数を無視して 7 回にする | **核心** |
| 11 | `EquipmentWithoutToolsZeroStopsProduction` | 同上で工具なし係数 0‰ → 0 回、在庫も摩耗も動かない | `max(…, 1)` などで底上げする |  |
| 12 | `HashChangesWhenErrandLaborLossChanges` | `ErrandLaborLossPermille` だけを変えると状態ハッシュが変わる | `StateHasher` に書き忘れる |  |
| 13 | `HashChangesWhenProductionRunsChange` | `ProductionRuns` だけを変えると状態ハッシュが変わる | 同上 |  |
| 14 | `HashChangesWhenToolWearChanges` | `ToolWear` だけを変えると状態ハッシュが変わる(改名後も書いている) | 改名時に書き込みを落とす |  |
| 15 | `NewHouseholdFieldsRejectNegativeValues` | `ToolWear` / `ErrandLaborLossPermille` / `ProductionRuns` に −1 を入れると `ArgumentOutOfRangeException` | 検証なしの自動プロパティにする |  |
| 16 | `ProductionSubtractsPreviousDayErrandLaborLoss` | 所要労働 216‰・損失 100‰ → **5 回**(損失なしなら 6)。損失 1300‰ → **0 回**で在庫が動かず負にもならない | 損失を引かない。`max(0, …)` を落として負の労働力で `FloorDiv` が負を返す | **核心** |
| 17 | `ProductionDoesNotResetErrandLaborLoss` | 損失 100‰ を置いて 1 日進めても **`ErrandLaborLossPermille == 100` のまま** | 読んだ後に 0 へ戻す(書き手が 2 か所になる) |  |
| 18 | `ProductionRecordsRunsEveryDayIncludingZero` | 入力 6 回ぶん・生産能力 6 → 1 日目 `ProductionRuns == 6`、2 日目(入力 0)**`== 0`** | 0 の日を書かない(前日の 6 が残る)。`+=` で累積する | **核心** |
| 19 | `ProductionStopsWhenAnInputIsExhausted` | (W2-03 #6 を維持)入力在庫 0 → 0 回、在庫が動かない |  |  |
| 20 | `ProductionDrawsNoRandomNumbers` | (W2-03 #13 を維持) |  |  |
| 21 | `WorldGeneratorSeedsWorkshopInputsFromDailyInputQuantity` | `InitialWorkshopInputDays = 3`・穀物 2 → 小麦粉 1・生産能力 7 の定義 → 水車小屋番の工房在庫[穀物] = **42**。同じシードで 2 回生成すると配置が一致する | `× 必要数量` のまま(6)。生成順を変えて配置がずれる |  |
| 22 | `M0SatisfiesSurplusIsNonNegative_A` | §8 (a) が 5 品目で成立 | 値の側: 木材加工の所要労働‰ を 108 → 120 にすると(10 回/日)薪の供給 7,200 < 需要 8,280 で落ちる |  |
| 23 | `M0SatisfiesImportContentBelowBuyPrice_B` | §8 (b) が 5 品目で成立 | 値の側: 穀物の外部売値を 10 → 40 にすると小麦粉の含有 80 ≥ 56 で落ちる |  |
| 24 | `M0SatisfiesFloorPriceBalanceWithinTwoPercent_C` | §8 (c) が 5 職業で成立(フェーズ1 の検算: 水車小屋番 −227 / パン屋 −230 / 醸造 −230 / 木材加工 −230 / 鍛冶 +323。単位: 貨幣/年。生活費 27,000/年) | 値の側: 薪の外部買値を 10 → 11 にすると木材加工が +4,050/年(+15%)で落ちる。**式の側: 摩耗を 1 回あたり `CeilDiv` × 回数で数えると水車小屋番が −960/年(−3.6%)で落ちる** | **核心** |
| 25 | `M0SatisfiesInitialFundsCoverReservesAndWorkingCapital_D` | §8 (d) が 5 職業で成立(最大はパン屋 2,144 ≤ 2,400) | 値の側: 初期資金を 2,000 にすると落ちる。冬ではなく春の消費で数えると取り置き 884 が 604 になり、緩みが黙って入る(このテストは落ちない — 判別できないので、式の季節を doc コメントで固定する) |  |
| 26 | `M0SatisfiesWorstSeasonInputCostWithinAllowedCost_E` | §8 (e) が 5 レシピで成立 | 値の側: 穀物の係数を `{500, 2100, 700, 700}`(合計 4000 のまま)にすると夏の穀物が 21 になり、水車小屋番(許容 41 < 2 × 21)で落ちる |  |
| 27 | `M0ProductionMatchesCalibrationTable` | `WorldDefinition.M0` で世界を生成し 1 日進めると、各職業の **`ProductionRuns` が 7 / 6 / 6 / 12 / 1**、出力が 7 / 12 / 6 / 36 / 1 増える(初期在庫は 3 日ぶんあるので入力で制約されない) | 校正表の所要労働‰ と `ProductionSystem` の式が食い違う。`InitialWorkshopInputDays` の意味が旧のまま(2 日目以降でなく 1 日目から入力で制約される) | **核心** |
| 28 | `WorldDefinitionRejectsNonPositiveDaysLifeAndHours` | D / 出荷日数 / N / T の 0、工具なし係数の −1 と 1001 を、それぞれ拒む | 検査しない(`FloorDiv(…, 0)` のゼロ除算が実行時に出る) |  |

**「核心」印(#1・#3・#7・#8・#10・#16・#18・#24・#27)には実際に変異を当てて落ちることを確認し、当てた変異と結果を doc コメントかコミット本文に残す**([process/02](../process/02-task-spec.md))。#24 の式の側の変異は「テストの中の摩耗の式を 1 回あたりの `CeilDiv` × 回数に書き換える」であり、**テスト自身の判別力を確かめる**変異である。

**W2-03 のテストのうち #7(工具 0 で停止)は本タスクの #10 で置き換える。** 他(#3〜#6・#8〜#12・#30・#31)は `ToolWear` の改名と単位(回 → ‰人日: `N` を `N × 1000` で読む)を反映して**維持する**。

### 呼び出し側の配線(規則7)

本タスクが作る欄のうち、**書き手または読み手が本タスクの外にあるもの**。テストが踏めないので契約を書く。

| 欄 / 値 | 書く | 読む | 約束 |
| ------- | ---- | ---- | ---- |
| `ErrandLaborLossPermille` | #98(順5。当日の外出の合計を**毎日上書き**。外出が無い日は 0) | 順1(翌日) | 単位 ‰。`CeilDiv(委託先の係数‰ × 往復時間, T)` の合計。**上書きを忘れると損失が翌日以降も残る**(#98 のテスト表に置く) |
| `ProductionRuns` | 順1(毎日) | #39(順3 の④ゲート「生産量 0」)、#40(生産停止の Need) | 単位 回。**0 の日も書かれている**ことに読み手が依存する |
| `InputTargetStock` / `DailyInputQuantity` / `ShipmentTargetStock` | 定義 | #97(現金上限の「1日分の数量」、在庫比の分母)、#38(輸出の閾在庫) | 生産能力は名目の労働力 × 設備 1000‰。**当日の実行回数ではない** |
| `ExternalBuyPrice` / `ExternalSellPrice` | 定義 | #97(床)、#98(知らない店の見積もり)、#38(窓口の価格) | 1次産品/都市生産品で呼べる側が違う(呼べない側は例外) |

## 編集してよい文書

worktree をまたいだ所有権の宣言。**ここに挙がっていない文書は触らない。**

- なし(コードとテストのみ)

**GDD02a §1・§4、GDD02c §1.3、GDD02d §4.4、GDD03 §2.2、TDD01 §3.2・§3.8 は本タスクの設計(フェーズ1)で改訂済みである。** 実装が仕様と食い違ったら、**直すのはコードであって文書ではない**。文書側を直す必要があると判断したら、それは象限I-b(仕様そのものの欠陥)なので[止まって報告する](../process/02-task-spec.md)。

## このタスクで特に効く規約

[ADR-0002](../adr/0002-time-model-and-determinism.md) の決定論規約のうち、このタスクで踏みやすいものだけを挙げる。**機械で捕まるものは書かない。**

- **生産能力の 2 つの除算は `FloorDiv`。`ApplyPermille` は切り上げなので使わない。** 機械では捕まらない(テスト #1 が唯一の守り)
- **単位のコメント**: `‰人日`(摩耗・耐久値)、`‰`(係数・損失)、`人日`(N)、`日`(D・出荷日数)、`時間`(T)。**同じ int でも単位が違う欄を隣に並べる**タスクなので、欄ごとに必ず書く
- **走査順は世帯Id 昇順 → 構成員 NpcId 昇順 → レシピの配列順。** 変わらない
- **`Season` の値は仕様である。** 季節係数の行の添字に `(int)Season` を使う

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **「核心」印のテストに変異を当てて落ちることを確認し、当てた変異と結果を残した**
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
