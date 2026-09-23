# W2-19: NeedGeneration と Need / Reason の enum 化

| 項目     | 内容                                        |
| -------- | ------------------------------------------- |
| issue    | [#40](https://github.com/stama72/visionary/issues/40) |
| 根拠     | [GDD02b §8・§8.1](../03-gdd/02b-consumption-and-household.md) / [GDD02a §1〜§3](../03-gdd/02a-production.md) / [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) / [GDD01 §3.2](../03-gdd/01-trust-and-conversation.md) / [TDD01 §3.3・§3.6・§3.8](../04-tdd/01-sim-core-and-m0.md) |
| ブランチ | `feat/40-need-generation`                   |
| worktree | 本体(`visionary/`)                        |

**[GDD02b §8.1](../03-gdd/02b-consumption-and-household.md) が発生条件・品目・数量・失効の正である。** 本書はそれをコードの形へ落とすだけで、規則を新しく決めない。**§8.1 と食い違ったら §8.1 が勝つ。**

## スコープ

**含まない:**

- **Need の開示・会話**(信用 Tier による開示の深さ。[GDD01 §3.2](../03-gdd/01-trust-and-conversation.md))。W3 以降
- **Need を履行する主体**(行商人・運送労働者。[GDD11](../03-gdd/11-external-trade.md))。M0 に置かない
- **`Promise` を作る処理**(順6)。本タスクがするのは `Promise.NeedIndex` → `Promise.NeedId` の改名までである
- **`Need.Deadline` / `Need.Urgency` に値を入れる規則。** M0 は `Tick.Zero` / `0` で固定する([GDD02b §8.1](../03-gdd/02b-consumption-and-household.md))
- **`Metrics`(順10)への Need の集計。** [#41](https://github.com/stama72/visionary/issues/41) が判定の閾値と併せて置く

**変えない既存コード**(規則8。[02-task-spec](../process/02-task-spec.md)「変えないと宣言する既存コード」)。単位はファイルではなく規則:

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `ConsumptionSystem` が `UnmetConsumption` を毎日上書きする(不足が無い日も 0 を書く) | [GDD02b §1](../03-gdd/02b-consumption-and-household.md) | **一致**(`RunOneHousehold` の代入と §1「在庫は 0 で下げ止まり、足りなかったぶんは繰り越さない」を突き合わせた) |
| `HouseholdSystem` 手順1 の破産中フラグの更新(前日の件数から立て、0 件なら降ろす) | [GDD02b §3.3](../03-gdd/02b-consumption-and-household.md) | **一致**(代入であって「立てるだけ」ではない。降りる枝がある) |
| `Recipe.CapacityRuns` の2段の切り下げ | [GDD02a §1・§2](../03-gdd/02a-production.md) | **一致**(内側は `FloorDiv`。`ApplyPermille`(切り上げ)ではない) |
| `TradeSystem` 段5b の `UnaffordableNecessityCount` の数え方(現金上限のゲートと資金上限の切り詰めの2経路。二重に数えない) | [GDD02b §3.2](../03-gdd/02b-consumption-and-household.md) | **一致。本タスクは同じループに欄を1つ足すだけで、数え方には触れない** |
| `BuyerDemand` の目標在庫・予想在庫の式(必需 / 耐久 / 生産の入力 / 嗜好) | [GDD02b §2・§5.1](../03-gdd/02b-consumption-and-household.md) / [GDD02a §4](../03-gdd/02a-production.md) / [GDD02c §1.3](../03-gdd/02c-price-and-budget.md) | **一致**(本タスクは `DemandLine.TargetStock` / `ExpectedStock` を読むだけで、式に触れない) |
| `SellableStock` の工具の留保(1個) | [GDD02c §1.3](../03-gdd/02c-price-and-budget.md) | **未確認**(本タスクは読まない。ただし `ProductionSystem.EquipmentThresholdStock` を参照している側なので、下の「作るもの」4 で定数を動かさないこと) |
| `ProductionSystem` の摩耗(`WearTools`)と在庫の増減 | [GDD02a §3.1](../03-gdd/02a-production.md) | **一致**(本タスクが触るのは設備係数と労働力合計‰の**求め方の置き場所**だけで、値も順序も変えない) |

## 作るもの

### 1. `Need` の enum 化と Id(`src/Visionary.Sim/World/Need.cs`)

```csharp
/// <summary>Need の種別(GDD02b §8 の表の左2列)。</summary>
public enum NeedType
{
    StockShortage = 1,   // 在庫不足
    MoneyShortage = 2,   // 金銭不足
    LaborShortage = 3,   // 労働力不足
}

/// <summary>Need の理由コード(GDD02b §8 の表の右列)。</summary>
public enum NeedReason
{
    ProductionStopped = 1,        // 生産停止
    Distress = 2,                 // 困窮
    CannotExpandProduction = 3,   // 増産できない
    ToolsExhausted = 4,           // 工具切れ
    DistantStock = 5,             // 遠方在庫
}

public readonly record struct Need
{
    public int Id { get; init; }                    // 非負。World.NextNeedId が払い出す
    public NeedType TypeCode { get; init; }
    public int TargetHouseholdId { get; init; }
    public int ItemId { get; init; }
    public int Quantity { get; init; }              // 単位は ReasonCode による(GDD02b §8.1)
    public Tick Deadline { get; init; }             // M0 は Tick.Zero
    public int Urgency { get; init; }               // M0 は 0
    public NeedReason ReasonCode { get; init; }

    /// <summary>理由コードから種別を引く(GDD02b §8 の表)。</summary>
    public static NeedType TypeOf(NeedReason reason);
}
```

- **どちらの enum も 0 を使わない。** 既定値が有効な種別に見えると `default(Need)` が本物の Need として通る(`RandomStream` / `StateHasher.Section` と同じ規律)。**`PromiseState` が 0 始まりなのは、あちらの 0 が「有効」という実在の初期状態だからである**
- **`TypeOf` を置くのは、種別と理由の組を実装の各所で手で綴らせないためである。** 対応は1対3を含む(在庫不足に3理由)ので、手で綴ると「金銭不足 / 工具切れ」のような [GDD02b §8](../03-gdd/02b-consumption-and-household.md) の表に無い組が静かに作れる。**`NeedReason` の全要素について網羅的に分岐し、未知の値は `ArgumentOutOfRangeException` を投げる** — 理由を1つ足したときに、ここが落ちて気付く
- `Quantity` の単位は理由コードによって違う([GDD02b §8.1](../03-gdd/02b-consumption-and-household.md))。**4つは個数、`CannotExpandProduction` だけが ‰人日である。** doc コメントに書く
- **W1 の doc コメント(「W2 で変わる。仮の形は TDD01 §3.6 が持つ」)を消す。** 仮決めは確定した

### 2. Id の払い出し(`src/Visionary.Sim/World/World.cs`)

```csharp
/// <summary>次に払い出す Need の Id。非負。単調増加で、失効した Id を再利用しない。</summary>
public int NextNeedId { get; internal set; }
```

- コンストラクタで `0` に初期化する
- **失効した Id を再利用しない。** 再利用すると、失効前の Need を指していた `Promise.NeedId` が、同じ Id で立った**別の** Need を指す

### 3. `Promise.NeedIndex` → `Promise.NeedId`(`src/Visionary.Sim/World/Promise.cs`)

- 型と位置は変えない(`int`、先頭)。**意味が「`World.Needs` の添字」から「`Need.Id`」へ変わる**([TDD01 §3.6](../04-tdd/01-sim-core-and-m0.md))
- doc コメントの「W2 で変わる」を消し、Id 参照であることを書く

### 4. 労働力と設備係数の求め方を1か所へ(`src/Visionary.Sim/Systems/LaborCapacity.cs`、新規)

```csharp
public static class LaborCapacity
{
    /// <summary>設備係数‰(GDD02a §3)。工具在庫が閾値以上なら 1000、無ければ definition の値。</summary>
    public static int EquipmentPermille(WorldDefinition definition, HouseholdState household);

    /// <summary>労働力合計‰ = max(0, Σ構成員の労働力係数‰ − 前日の外出の労働損失‰)(GDD02a §2)。</summary>
    public static int LaborPermille(WorldDefinition definition, World world, HouseholdState household);

    /// <summary>floor(労働力合計‰ × 設備係数‰ ÷ 1000)(GDD02a §1 の内側の切り下げ)。</summary>
    public static int EffectiveLaborPermille(int laborPermille, int equipmentPermille);
}
```

- **`ProductionSystem.RunOneHousehold` を書き換えて、設備係数と労働力合計‰をこの2つから取る。** 値も順序も変えない(構成員は `MemberNpcIds` の昇順のまま、`max(0, …)` も残す)
- **`ProductionSystem.EquipmentThresholdStock` は動かさない**(`SellableStock` が参照しており、[GDD02c §1.3](../03-gdd/02c-price-and-budget.md) が留保量の定義として採っている)。`LaborCapacity.EquipmentPermille` はこの定数を読む
- **切り出す理由は、順4 が同じ2つの値を必要とするからである。** 順4 で綴り直すと [02-task-spec](../process/02-task-spec.md) 規則5 の「式を2か所に置く」に当たる — **片方の丸めを直したとき他方が黙ってずれる**(`Recipe.CapacityRuns` の doc コメントが既に同じ理由で1か所に寄せている)
- **`EffectiveLaborPermille` の中間の積は `long`。** `Recipe.CapacityRuns` と同じ理由(労働力合計‰ × 設備係数‰ は `int` を超えうる)
- **負を拒む。** `Recipe.CapacityRuns` が両引数について `ArgumentOutOfRangeException` を投げるのと揃える

### 5. 「買えず、目標在庫に届かなかった」不足量(`src/Visionary.Sim/World/HouseholdState.cs`)

```csharp
/// <summary>当日、1個も買えず、かつ予想在庫が目標在庫を下回っていた量。添字 = itemId。単位: 個。</summary>
public int[] UnfilledPurchase { get; }
```

- **書き手は順5 段5b だけである**(下記6)。読み手は**翌日の**順4
- 単位は**個**である。耐久(工具)は耐久値ではなく個数で入る(下記6)

### 6. 段5b が毎日書く(`src/Visionary.Sim/Systems/TradeSystem.cs` の `RunOneHouseholdsShopping`)

- **冒頭で全品目を 0 にする**(`UnaffordableNecessityCount = 0` と同じ位置・同じ理由)。書かない日があると、前日の不足がその後もずっと Need を立て続ける
- `demand.Lines` の走査の中で、その `line` について **`TradeSettlement.Execute` / `ExecuteImport` を一度も呼ばなかった**(= 1個も買えなかった)なら、次を足し込む:

```
if (line.ExpectedStock < line.TargetStock)
    household.UnfilledPurchase[line.ItemId]
        += BuyerBudget.QuantityInUnits(line.Purpose, line.TargetStock - line.ExpectedStock,
                                       _definition.ToolDurabilityPerUnit);
```

- **走査を終えたあと、その日その品目を1個でも買えていたなら `UnfilledPurchase[itemId]` を 0 に戻す。**(**フェーズ2 の訂正。1巡目の象限 I-b。** 元の指示は行単位の判定だけで足し込みを確定させていたが、[GDD02b §8.1](../03-gdd/02b-consumption-and-household.md) の遠方在庫の条件は「前日、**その品目を**1個も買えず」と**品目単位**である。薪・穀物は必需と生産の入力の2行に現れ、[GDD02b §3.2](../03-gdd/02b-consumption-and-household.md) の順に資金を食い潰すので「先の行は買えて後の行は買えない」は定常的に起きる。行単位のままだと、買えている品目にも遠方在庫が立ち、[#41](https://github.com/stama72/visionary/issues/41) が読む件数が系統的に過大になる。本書 :10 の「§8.1 と食い違ったら §8.1 が勝つ」を適用した)
- **`=` ではなく `+=` である。** 薪は必需と生産の入力の2行に現れる([GDD02b §2](../03-gdd/02b-consumption-and-household.md))ので、代入だと後の行が前の行を消す。**数量の側は行をまたいで合計してよい** — [GDD02b §2](../03-gdd/02b-consumption-and-household.md) の目標在庫は (用途, 品目) ごとに立つので、§8.1 の `目標在庫 − 予想在庫` を品目へ持ち上げると用途の和になる。**0 に戻す規則が掛かるのは「1個でも買えた品目」だけで、数量の合計には掛からない**
- **`QuantityInUnits` を通すのは耐久のためである。** `DemandLine.TargetStock` / `ExpectedStock` は**耐久だけ耐久値**(N × 1000 倍)で入っている。通さないと工具の不足量が数千倍になる(W2-10 欠陥3 と同じ型の誤り)
- **部分的にでも買えた行は数えない。** [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) の条件は「その日に買えず」であって「目標在庫まで買えず」ではない
- **`ExpectedStock` / `TargetStock` は段4 が作った値であり、買い物より前の在庫である**([GDD02b §5.1](../03-gdd/02b-consumption-and-household.md))。**`world` から在庫を読み直さない** — 読み直すと「その日に買った量」が混ざる

### 7. `NeedGenerationSystem`(`src/Visionary.Sim/Systems/NeedGenerationSystem.cs`、新規)

```csharp
public sealed class NeedGenerationSystem : ISimSystem
{
    public NeedGenerationSystem(WorldDefinition definition);
    public RandomStream Stream => RandomStream.NeedGeneration;   // 既存の値 5。振り直さない
    public Cadence Cadence => Cadence.Daily(hour: 0);
    public void Step(World world, SimContext context);
}
```

**乱数を一切引かない**(`ProductionSystem` / `ConsumptionSystem` / `HouseholdSystem` と同じ。系統を持つのは `SimScheduler` の登録に一意な識別子が要るからである)。

`Step` の手順は**この順そのものが仕様**である:

1. 組み直した一覧 `rebuilt` を空で作る
2. **世帯 Id 昇順**(`world.Households` の添字順)に、各世帯で**理由コードの値の昇順**(1→5)に、各理由で**品目 Id 昇順**に、条件を満たす組を `rebuilt` へ積む。`TypeCode = Need.TypeOf(reason)`、`Deadline = Tick.Zero`、`Urgency = 0`
3. 積んだ各組について、**`world.Needs` を先頭から線形に走査**し、`TargetHouseholdId`・`ReasonCode`・`ItemId` の3つがすべて一致する要素があればその `Id` を採る。無ければ `world.NextNeedId` を採り、`world.NextNeedId` を1つ進める
4. `world.Needs.Clear()` してから `rebuilt` を `AddRange` する

- **`Dictionary` / `HashSet` を使わない**(ADR-0002)。M0 の上限は 10世帯 × 9品目 × 5理由 = 450 件で、線形走査で足りる
- **一覧を毎日まるごと組み直すのが失効である**([GDD02b §8.1](../03-gdd/02b-consumption-and-household.md))。条件が消えた組は積まれないので落ちる。**「失効した Need を探して消す」処理を別に書かない** — 2つの規則ができ、片方だけ直る
- **並びが履歴に依存しない。** 一覧は常に (世帯 Id, 理由コード, 品目 Id) の昇順であり、**いつ立ったかで並びが変わらない。** `StateHasher` は `List` の格納順そのままを読む([TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md))ので、並びが履歴に依存すると「同じ状態に別の経路で到達した」が別ハッシュになる
- **3 の走査は `world.Needs`(前日の一覧)に対して行う。** `rebuilt` に対してではない — 同じ日に同じ3つ組を2度積むことは無い(2 の走査が (理由, 品目) について一意だから)ので、`rebuilt` 側の重複検査は要らない

各理由の条件と数量(**正は [GDD02b §8.1](../03-gdd/02b-consumption-and-household.md) の表**)。`r = definition.Recipes[(int)household.Occupation]`:

| 理由 | 条件 | 品目 | 数量 |
| ---- | ---- | ---- | ---- |
| 1 `ProductionStopped` | `household.ProductionRuns == 0` かつ、品目 j が `r` の入力で `WorkshopInventory[j] < 必要数量_j` | j(足りない入力ごとに1件) | `必要数量_j − WorkshopInventory[j]` |
| 2 `Distress` | `household.IsBankrupt == 1` かつ `definition.NecessityTargetStockDays[i] > 0` かつ `household.UnmetConsumption[i] > 0` | i | `household.UnmetConsumption[i]` |
| 3 `CannotExpandProduction` | `r.CapacityRuns(labor, equip) == 0`(`labor` / `equip` は `LaborCapacity`) | `r.Outputs[0].ItemId` | `r.LaborPermille − LaborCapacity.EffectiveLaborPermille(labor, equip)` |
| 4 `ToolsExhausted` | `household.WorkshopInventory[Item.Tools] == 0` | `Item.Tools` | `ProductionSystem.EquipmentThresholdStock`(= 1) |
| 5 `DistantStock` | `household.UnfilledPurchase[i] > 0` | i | `household.UnfilledPurchase[i]` |

- **1 は `r.Inputs` の並びで回さない。** `Recipe` は入力が品目 Id 昇順であることを保証していない(`BuyerDemand` が同じ理由で品目 Id の外側ループを回している)。**品目 Id で外側を回し、その品目が入力かどうかを内側で調べる**
- **1 の条件に「生産能力が 0 か」は入らない。** 入力が足りていて生産量 0 なら、それは労働力の側であり 3 が拾う。**逆に両方が同時に成り立つ日は Need が2件立つ**([GDD02b §8.1](../03-gdd/02b-consumption-and-household.md)「理由の違う Need が同時に立つことを許す」)
- **3 の条件は `CapacityRuns(...) == 0` と書く。** [GDD02a §2](../03-gdd/02a-production.md) の「労働力合計‰ × 設備係数‰ ÷ 1000 < 所要労働‰」と同値であり、**式を綴り直さずに同値を使うほうが狭い**(`CapacityRuns` の2段の切り下げを写し損ねる余地が無い)。**数量の側だけは引き算が要るので `EffectiveLaborPermille` を呼ぶ** — これも `CapacityRuns` の内側と同じ関数である
- **3 の数量は必ず正である。** 条件が `所要労働‰ > 実効労働‰` なので差は 1 以上になる
- **3 の `r.Outputs[0]` を読んでよいのは、`TradeSystem` のコンストラクタが全レシピの出力2件以上を拒んでいるからである**(`OccupationReassignment.IsGateOpen` が同じ根拠で同じ読み方をしている)
- **4 は「生産が止まったか」を見ない。** 工具が無くても半分の能力で続く職業が4つある([GDD02a §3](../03-gdd/02a-production.md))。**それでも Need を立てるのは、[GDD02b §8](../03-gdd/02b-consumption-and-household.md) の発生源が「設備の摩耗」だからである**
- **2 の `NecessityTargetStockDays[i] > 0` を落とさない。** `UnmetConsumption` はビール(嗜好)にも立つ([GDD02b §1](../03-gdd/02b-consumption-and-household.md) の1日消費量)

### 8. ハッシュ(`src/Visionary.Sim/Determinism/StateHasher.cs`)

- `Needs` 区分: 各要素の**末尾**に `need.Id` を足す(既存の値は動かさない)。`TypeCode` / `ReasonCode` は `(int)` へキャストして同じ位置に書く
- `Needs` 区分: **ループの後に `world.NextNeedId` を1つ書く**(区分の末尾に足す)
- `Promises` 区分: `promise.NeedIndex` → `promise.NeedId`(位置も型も同じ)
- `Households` 区分: 各要素の**末尾**に `WriteInt32Array(..., household.UnfilledPurchase)` を足す
- **`StateHasherCoverageTests` の凍結表を更新する。** `ExpectedWorldSections` に `NextNeedId`、`Need` に `Id`、`HouseholdState` に `UnfilledPurchase`、`Promise` の `NeedIndex` → `NeedId`。**先に表を直してから `StateHasher` を直すと、赤が一度も出ずに通り抜ける** — 逆の順で進めること

## 配線の列挙(規則7)

**`NeedGenerationSystem` は本タスクの時点で、テスト以外に呼び出し側を持たない。** `Visionary.Sim.Runner` は合成システムしか組んでおらず、実パイプラインを組んでいるのは `TradePipelineTests.FullPipeline` だけである。したがって次を別立てで固定する。

| 項目 | 約束 |
| ---- | ---- |
| **呼び出し順** | `TradePipelineTests.FullPipeline` の `HouseholdSystem`(順3)と `TradeSystem`(順5)の**間**に `NeedGenerationSystem` を入れる。**順5 の後に置いてはならない** — 置くと `UnfilledPurchase` を当日中に読み、[GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) の1日遅延が消える |
| **入力の作り方** | `ProductionRuns` は順1 が、`UnmetConsumption` は順2 が、`IsBankrupt` は順3 が、`UnfilledPurchase` は**前日の**順5 が書く。**順4 はどれも書き換えない**(読むだけ) |
| **添字の約束** | `UnfilledPurchase` / `UnmetConsumption` の添字は **itemId**。`world.Households` の添字は**世帯 Id**。`world.Knowledge` の添字は **NpcId** であって世帯 Id ではない([TDD01 §3.2](../04-tdd/01-sim-core-and-m0.md)。順4 は `Knowledge` を読まない) |
| **単位の約束** | `UnfilledPurchase` は**個**、`UnmetConsumption` は**個**、`Need.Quantity` は理由コードによって**個または ‰人日**、`ErrandLaborLossPermille` と `LaborPermille` は**‰** |
| **書き込みの範囲** | 順4 が書くのは `world.Needs` と `world.NextNeedId` の2つだけである。**世帯の状態を1つも書き換えない** |

## 落ちるべき条件(テスト)

**この節が完了条件そのもの。** 目標は「通ること」ではなく「**壊したときに落ちること**」である。

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心(当てる変異 / 期待) |
| - | ------ | -------- | -------------------- | ------------------------- |
| 1 | `ProductionStoppedNeedRisesForEachShortInput` | 生産量 0・入力2件のうち1件が不足 → その入力にだけ 在庫不足/生産停止 が1件、数量 = `必要数量 − 在庫` | 足りている入力にも立てる / 数量を目標在庫との差にする | |
| 2 | `ProductionStoppedNeedDoesNotRiseWhenInputsSuffice` | 生産量 0 だが入力は足りている(能力 0)→ 生産停止は立たない | 条件を `ProductionRuns == 0` だけにする | |
| 3 | `ProductionStoppedNeedsAreOrderedByItemId` | 不足する入力が2件で、`r.Inputs` の並びが品目 Id 降順のとき、`Needs` の並びは品目 Id 昇順 | `r.Inputs` の並びで回す(`Recipe` は昇順を保証しない) | |
| 4 | `DistressNeedUsesUnmetConsumptionAsQuantity` | フラグ 1・必需の消費不足 3 → 金銭不足/困窮 が1件、数量 3 | 数量に目標在庫との差を使う / 種別を在庫不足にする | |
| 5 | `DistressNeedDoesNotRiseWithoutUnmetConsumption` | **フラグ 1・消費不足 0**(在庫は満ちている)→ 困窮は立たない | フラグだけで立てる。[GDD02b §3.3](../03-gdd/02b-consumption-and-household.md)「フラグが見ているのは現金であって在庫ではない」を落とす | **核心**。変異: 条件から `&& household.UnmetConsumption[itemId] > 0` を削る / 期待 **赤** |
| 6 | `DistressNeedDoesNotRiseWhenSolvent` | フラグ 0・消費不足 3 → 困窮は立たない(遠方在庫の側が拾う) | フラグを読まない | |
| 7 | `DistressNeedIgnoresPreferenceItems` | フラグ 1・**嗜好品**の消費不足 3 → 困窮は立たない | `NecessityTargetStockDays[i] > 0` を落とす | |
| 8 | `LaborShortageNeedRisesWhenCapacityIsZero` | 工具 0 で所要労働 1000‰・労働力 1300‰・設備 500‰ → 労働力不足/増産できない が1件、品目 = 出力品目、数量 = `1000 − 650` = 350 | 数量を 1 や 0 にする / 品目に入力を使う | |
| 9 | `LaborShortageNeedDoesNotRiseAtTheBoundary` | 実効労働‰ が**所要労働‰ ちょうど**(能力 1)→ 立たない | `<` を `<=` にする / 綴り直した式の丸めを切り上げにする | |
| 10 | `ToolsExhaustedNeedRisesOnlyWhenToolStockIsZero` | 工具在庫 0 → 在庫不足/工具切れ が1件・数量 1。在庫 1 → 立たない | 摩耗量で判定する / `<= 1` にする | |
| 11 | `DistantStockNeedMirrorsUnfilledPurchase` | `UnfilledPurchase[i] = 4` → 在庫不足/遠方在庫 が1件、数量 4。0 の品目には立たない | 0 の品目にも立てる / 数量を目標在庫との差で計算し直す | |
| 12 | `NeedsExpireWhenTheConditionIsGone` | 1日目に立った Need が、条件を消した2日目に `world.Needs` から消える | 失効を書かない(積むだけ) | **核心**。変異: `world.Needs.Clear()` を消して `AddRange` だけにする / 期待 **赤** |
| 13 | `NeedIdIsStableWhileTheConditionHolds` | 2日連続で同じ (世帯, 理由, 品目) の条件が立つと `Id` が同じ。**手前の Need が1件失効して並びが詰まっても変わらない** | Id を `world.Needs` の添字から振る | |
| 14 | `ExpiredNeedIdIsNotReused` | 立つ → 失効 → また立つ、で**3回目の Id が1回目と違う**。`world.NextNeedId` が単調増加 | 失効時に `NextNeedId` を巻き戻す / 添字で振る | |
| 15 | `NeedsAreOrderedByHouseholdThenReasonThenItem` | 3世帯・複数理由が同時に立つ世界で、並びが (世帯 Id, 理由コード, 品目 Id) の昇順 | 理由をコードの記述順で積む(`NeedReason` の値と食い違う) / 世帯ごとに畳む | |
| 16 | `NeedTypeMatchesReasonForEveryReason` | `NeedReason` の全要素について `Need.TypeOf` が [GDD02b §8](../03-gdd/02b-consumption-and-household.md) の表と一致し、未知の値で `ArgumentOutOfRangeException` | 在庫不足の3理由のどれかを取り違える / `default` 節で黙って種別を返す | |
| 17 | `NeedEnumsDoNotUseZero` | `NeedType` / `NeedReason` のどの要素も 0 でない(`default(Need)` が有効に見えない) | 0 始まりで宣言する | |
| 18 | `NeedGenerationWritesNoHouseholdState` | 順4 の前後で全世帯の全欄(在庫・資金・`ProductionRuns`・`UnmetConsumption`・`IsBankrupt`・`UnfilledPurchase`・`ToolWear`・`ErrandLaborLossPermille`)が不変 | 順4 が `UnfilledPurchase` を 0 に戻す(順5 の仕事を奪う) | |
| 19 | `NeedGenerationDrawsNoRandomNumbers` | 順4 だけを登録したスケジューラを回しても乱数の状態が動かない(既存の同型テストに倣う) | `OpenRandom` を呼ぶ | |
| 20 | `UnfilledPurchaseIsClearedEveryDay` | 1日目に買えず値が入り、2日目に買えたら **0 に戻る** | 冒頭の 0 クリアを書かない | **核心**。変異: `RunOneHouseholdsShopping` 冒頭の `UnfilledPurchase` のクリアを消す / 期待 **赤** |
| 21 | `UnfilledPurchaseIsZeroWhenStockIsAtTarget` | 買えなかったが**予想在庫 ≥ 目標在庫** → 0 | 条件 `ExpectedStock < TargetStock` を落とす | |
| 22 | `UnfilledPurchaseSumsBothPurposesForTheSameItem` | 薪が必需と生産の入力の2行に立ち、両方買えない日 → 2行の合計が入る | `+=` を `=` にする | |
| 23 | `UnfilledPurchaseForToolsIsInUnits` | 工具が買えなかった日、`UnfilledPurchase[Item.Tools]` が**個数**(1 前後)であって耐久値(数千)ではない | `QuantityInUnits` を通さない | **核心**。変異: `QuantityInUnits(...)` を外して `TargetStock − ExpectedStock` をそのまま足す / 期待 **赤** |
| 24 | `PartiallyFilledLineIsNotCountedAsUnfilled` | 目標に届かないが1個は買えた行 → 0 | 「目標まで買えなかった」で数える | |
| 25 | `ProductionSystemStillProducesTheSameAfterExtraction` | `LaborCapacity` へ切り出した後も既存の `ProductionSystemTests` が全件緑([GDD02a §2](../03-gdd/02a-production.md) の能力表 1300 / 1200 / 1000 / 650 の行を含む) | 切り出しで `max(0, …)` や構成員の走査順を落とす | |
| 26 | `PipelineRaisesDistantStockNeedOnTheNextDay` | **統合。** `FullPipeline` に順4 を入れて `WorldGenerator.Generate` の世界を2日回す。初日の順4 では遠方在庫が1件も立たず、2日目の順4 で初日に買えなかった品目に立つ | 順4 を順5 の後に置く(同日に立つ)/ 順4 が `UnfilledPurchase` を読まない | |
| 27 | `PipelineIsDeterministicWithNeedGeneration` | **統合。** 同一シード・同一設定で5日 × 2回、最終 `StateHasher.Compute` が一致 | `Dictionary` / `HashSet` の列挙順を使う | |
| 28 | `HashChangesWhenNeedIdChanges` / `HashChangesWhenNextNeedIdChanges` / `HashChangesWhenUnfilledPurchaseChanges` | 新しい3欄がハッシュに入っている | `StateHasher` への追記漏れ | |

**26・27 は `WorldGenerator.Generate` の世界で回す**(`TradePipelineTests` の既存の規律。手組みの縮退した世界を使わない)。**1〜25 は `EconomySystemTestFixtures` の小さな世界でよい**(`WorldDefinition.M0` の値に依存させない。[#28](https://github.com/stama72/visionary/issues/28) が値を動かす)。**15 は世帯が3戸要るので、フィクスチャに「世帯を N 戸作る」口を足してよい** — 既存の1戸の口(`BuildWorldWithOneHousehold`)の挙動は変えないこと。

### 別表: レビューで足したテスト(フェーズ2)

**上の表は実装へ渡した時点の指示であって最終形ではない。** ここはレビューの巡で足したぶんである。

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心(当てる変異 / 期待) |
| - | ------ | -------- | -------------------- | ------------------------- |
| 29 | `UnfilledPurchaseIsZeroWhenTheItemWasBoughtOnAnotherLine` | 薪が必需と生産の入力の2行に立ち、**必需の行では買えたが生産の入力の行では資金が尽きて買えなかった**日 → `UnfilledPurchase[薪]` は 0(遠方在庫は立たない) | 判定を行単位のままにする([GDD02b §8.1](../03-gdd/02b-consumption-and-household.md)「**その品目を**1個も買えず」を落とす) | **核心**。変異: 走査後の「その日1個でも買えた品目を 0 に戻す」を消す / 期待 **赤** |
| 30 | `UnfilledPurchaseDoesNotPersistWhenNoLineAddsToIt` | 1日目に買えず値が入り、2日目はその品目について**買えもしないが足し込みも起きない**(`ExpectedStock >= TargetStock`、または需要行そのものが立たない)→ **0 に戻る** | 冒頭の `Array.Clear` を書かない | **核心**。変異: `RunOneHouseholdsShopping` 冒頭の `Array.Clear(household.UnfilledPurchase)` を消す / 期待 **赤** |

**#20 の「核心」印は #30 へ移る。** 1巡目の修正で入った「走査後に、その日1個でも買えた品目を 0 に戻す」経路が、**#20 の2日目(その品目を買えた日)を丸ごと引き受けてしまう** — 冒頭の `Array.Clear` を消しても最後に 0 が書かれるので、#20 は変異で赤にならない(2巡目の象限 I-a)。`Array.Clear` が今も必要なのは「買えず、かつ足し込みも起きない品目」の経路であり、それを踏むのが #30 である。**#20 は残す**(品目単位の 0 戻しを守る側のテストとして緑であり続ける)。

| 31 | `UnfilledPurchaseSubtractsExpectedStock` | 買えなかった行で **`ExpectedStock` が 0 でも `TargetStock` 以上でもない**(`0 < ExpectedStock < TargetStock`)世界 → `UnfilledPurchase[i]` が `TargetStock − ExpectedStock` であって `TargetStock` ではない | `− line.ExpectedStock` を落とす / 符号を逆にする | **核心**。変異: `TradeSystem` の足し込みの `line.TargetStock - line.ExpectedStock` を `line.TargetStock` にする / 期待 **赤** |
| 32 | `DistantStockNeedQuantityForToolsIsInUnits` | 工具の `UnfilledPurchase` が正の世界で、遠方在庫 Need の `Quantity` が**個数**のまま(耐久値へ換算し直されない) | 順4 が読むときに `ToolDurabilityPerUnit` を掛け直す(順5 が個で書いた値を耐久値へ戻す) | |

| 33 | `UnfilledPurchaseIsZeroForItemZeroWhenItWasBoughtOnAnotherLine` | #29 と同じ主張を**品目 Id 0(穀物)**で見る。`UnfilledPurchase` 配列の全要素が 0 | 走査後の 0 戻しループの初期値を `itemId = 1` にする(**品目 Id 0 だけが 0 戻しを免れる**) | |

**3巡目(網羅パス)が見つけた「守るテストが無い経路」への手当てである。** #31 は、遠方在庫の数量を決める唯一の式を守るテストが1件も無かったため(既存の #22・#23 は**どちらも `ExpectedStock = 0` の世界**で書かれており式に差が出ない。#21 はガードに弾かれて式へ到達しない)。#32 は、順5 が個で書き順4 が個で読む、という単位の一貫性を端から端まで見るテストが無かったため(#11 は必需品で書かれており耐久を踏まない)。

**あわせて #29・#30 のアサートを配列全体へ広げる。** どちらも単一品目の1点しか見ていないので、冒頭の `Array.Clear` と走査後の 0 戻しの**範囲を1要素ずらす**変異(`Item.Tools` だけ持ち越す / `Item.Grain` だけ 0 戻しを免れる)がどちらも緑になる。

**#24 の変異の期待は実態と食い違ったままにしてある。** `if (purchased) { …; continue; }` の行単位のガードは、走査後の品目単位の 0 戻しに完全に包含されて観測不能になった(ガードを外しても結果が変わらない)。**ガードは残し、#24 の変異は当てない** — 理由は引き継ぎメモの「直さないと決めた指摘」に置く。

## 編集してよい文書

worktree をまたいだ所有権の宣言。**ここに挙がっていない文書は触らない。**

- **なし(コードとテストのみ)。** [GDD02b §8.1](../03-gdd/02b-consumption-and-household.md) / [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) / [TDD01 §3.2・§3.6・§3.8](../04-tdd/01-sim-core-and-m0.md) は**フェーズ1 が本ブランチで既に直してある。** 実装中に食い違いを見つけたら、直さずに象限I-b として報告すること(仕様の訂正は設計判断であり、[05-phase-sessions](../process/05-phase-sessions.md) の `SPEC-OUTSIDE` に当たる)

## このタスクで特に効く規約

- **列挙順**(ADR-0002)。**`Dictionary` / `HashSet` を新しく持ち込まない。** 一覧の組み直しは線形走査で足りる。**`BannedSymbols.txt` が止めるのは浮動小数点であって、コレクションの選択ではない**
- **`Need.Quantity` の単位は理由コードで変わる。** 型は `int` のままなので、取り違えても例外は出ない(`CannotExpandProduction` だけ ‰人日)
- **`DemandLine.TargetStock` / `ExpectedStock` は耐久だけ耐久値である。** 同じ理由で取り違えても例外が出ない

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **「核心」印の4件の変異を `mutator` が実測し(レビューの巡が閉じた後)、結果を doc コメントへ転記した**
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
