# W2-03: Production / Consumption システム

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#34](https://github.com/stama72/visionary/issues/34)                |
| 根拠     | [GDD02 §5.1・§5.2・§5.3・§6.1・§9](../03-gdd/02-economy.md) / [GDD03 §2.1](../03-gdd/03-seasons-and-city.md) / [GDD08 §9](../03-gdd/08-household-and-decision.md) / [TDD01 §3.1・§3.3・§3.8](../04-tdd/01-sim-core-and-m0.md) |
| ブランチ | `feat/34-production-and-consumption`                                 |
| worktree | `visionary/`(本体)                                                  |

> **この文書は使い捨ての作業指示である。** 実装完了時点で凍結し、以後の正はコードと GDD/TDD。

## スコープ

**世界を動かす最初の2システムを作るタスクである。** [TDD01 §3.3](../04-tdd/01-sim-core-and-m0.md) の日次パイプラインのうち順1(Production)と順2(Consumption)にあたる。[#33](https://github.com/stama72/visionary/issues/33) が作った `WorldDefinition` と `World` を、初めて tick で変化させる。

**含まない:**

- **価格・予算・購入・取引**(#35 / #36 / #37 / #38)。本タスクは在庫を増減させるだけで、貨幣に一切触れない。`LiquidFunds` を読みも書きもしない
- **`Need` の生成と `TypeCode` / `ReasonCode` の enum 化**(#40)。本タスクが用意するのは Need の**入力**だけである(下記)
- **熟練度・時間配分**([GDD08 §4.2](../03-gdd/08-household-and-decision.md) の式は M0 では使わない。[GDD08 §9](../03-gdd/08-household-and-decision.md) の決定)。`NpcState.SkillPermille` は読まない
- **設備係数の連続化**(GDD02 §5.3 が v1.0 の拡張とした)
- **機会費用**(順0。#36 以降)
- **`vsim` / Runner の追随。** [`SyntheticLoadSystem`](../../src/Visionary.Sim.Runner/Determinism/SyntheticLoadSystem.cs) / `SyntheticDecaySystem` のままにする。理由は下記
- **値の作り込み。** 本タスクが置くのは**初期値**であって、需給が釣り合うかの検算と調整は [#28](https://github.com/stama72/visionary/issues/28) が持つ(下記の申し送り)。**ただし置き場所と型と丸めの向きは本タスクで決める**

### Runner を差し替えない理由

`SyntheticLoadSystem` の doc は「W2 で TDD01 §3.3 の本物のシステム群に差し替え、このファイルは削除する」と書いているが、**11システム中2つだけを差し替えると決定論回帰テストの検出範囲が狭まる。** 合成負荷は `Knowledge` / `Ledgers` / `Promises` / `TrustLedger` / `Market` を含む**全区分**に書き込むことで、`StateHasher` の全経路を CI の2プロセス比較に踏ませている。Production と Consumption が触るのは在庫と工具だけなので、いま差し替えると残り6区分がハッシュ回帰で一度も動かなくなる。**差し替えはパイプラインが揃う時点(#41 の Metrics まで)の作業である。**

### #40(NeedGeneration)への申し送り — 何が復元でき、何が復元できないか

**消費の不足量だけを状態として記録する。** [GDD02 §6.1](../03-gdd/02-economy.md) の「在庫は 0 で下げ止まり、負にならない。足りなかったぶんは繰り越さない」という規約のせいで、順2 が終わった後の在庫は「ちょうど足りた」と「足りずに 0 になった」を区別できない。順4 の #40 からは**不足の大きさ**(`Need.Quantity` に入る値)が復元できないので、順2 が書き残す。

**生産側は記録しない。** 入力切れ・工具切れ・労働力不足は、順4 の時点の工房在庫・レシピ・`LaborPermilleByRank` から**そのまま再計算できる**。記録すると同じ事実が2か所に載る。

| 事実 | #40 はどこから採るか |
| ---- | -------------------- |
| 消費の不足(在庫不足) | **`HouseholdState.UnmetConsumption`**(本タスクが書く) |
| 生産の入力切れ(生産停止) | 工房在庫 < 必要数量 × 生産能力 を再計算 |
| 工具切れ | 工房在庫`[Item.Tools]` == 0 |
| 労働力不足 | 労働力合計‰ < `Recipe.LaborPermille` を再計算 |

### #28 への申し送り — 需給が5〜14倍ずれている

**本タスクの初期値を入れると、都市の生産が消費に一桁足りない。** 値の調整は #28 の担当なので**本タスクでは直さない**が、数字を残す。

`所要労働‰` は5職業とも 1000 で、労働力合計‰ は 1300(親方1000 + 徒弟300)である。したがって `生産能力 = floor(1300 ÷ 1000) = 1 実行/日` に張り付く。

| 品目 | 日次供給(2世帯合計) | 日次需要 | 比 |
| ---- | -------------------- | -------- | -- |
| パン | 4 | 20(20人 × 1) | **1/5** |
| 薪 | 6 | 44(消費40 + 生産入力4)、冬は 84 | **1/7〜1/14** |
| ビール | 2 | 10(親方10人 × 1) | **1/5** |
| 小麦粉 | 2 | 2(パン屋2世帯 × 1) | 1/1 |
| 工具 | 2 | 1/3(10世帯 ÷ 30日) | 6/1 |

**動かす軸は `所要労働‰` である。** パンを釣り合わせるには約130‰、薪は約90〜160‰(冬か秋か)が要る。**100‰ 前後の桁**であって、1000‰ ではない。[#33](https://github.com/stama72/visionary/issues/33) が 1000 を「意図的に無風の初期値」として置いた時点から変わっていない。

## 作るもの

名前空間は `Visionary.Sim`(`Definition/` `World/`)と `Visionary.Sim.Systems`(`Systems/`)、`Visionary.Sim.Numerics`。

### 1. `IntegerMath.FloorDiv`(`Numerics/IntegerMath.cs` に追加)

```csharp
public static long FloorDiv(long dividend, long divisor);
public static int  FloorDiv(int dividend, int divisor);
```

**数学的な切り下げ除算。** `7 / 2 = 3`、`-7 / 2 = -4`、`-6 / 2 = -3`。C# の `/` は0方向への切り捨てなので、**被除数と除数が異符号で割り切れないときだけ** `-1` の補正が要る。既存の `CeilDiv` と対称に書くこと。

- 除数0は `DivideByZeroException`、`long.MinValue / -1` は `OverflowException`(`CeilDiv` と同じ)
- `int` 版は `CeilDiv(int, int)` と同じく `checked((int)FloorDiv((long)…))`

**なぜ要るか**: [GDD02 §5.2](../03-gdd/02-economy.md) が生産能力と `入力から作れる回数` の除算を「**切り上げ規約の意図的な例外**」と明記している。裸の `/` で書くと、切り上げ規約の例外なのか書き手の不注意なのかがレビューでしか判別できない([#28](https://github.com/stama72/visionary/issues/28) が挙げた論点)。**本タスクの被除数はいずれも非負なので `/` でも同値だが、意図を型で表すためにヘルパーを通す。**

> **残る穴**: `FloorDiv` を用意しても、裸の `/` を書くことは防げない。`BannedSymbols.txt` は演算子を禁止できず、`DeterminismConventionTests` も演算子は見ていない。**機械では捕まらない。レビュー観点として残る。**

### 2. `WorldDefinition` の追加欄(`Definition/WorldDefinition.cs`)

**4つの欄をコンストラクタ引数として足す。** 既存引数の後ろに並べること(呼び出し側は `BuildM0` とテストだけ)。

```csharp
/// <summary>添字 = (int)NpcRank の労働力係数‰。長さ3(GDD02 §5.2)。</summary>
public int[] LaborPermilleByRank { get; }

/// <summary>工具1個を消費するまでのレシピ実行回数 N。1以上(GDD02 §5.3)。</summary>
public int ProductionRunsPerToolWear { get; }

/// <summary>1人1日あたりの消費量。添字 = [(int)NpcRank][itemId]。長さ3 × itemCount(GDD02 §6.1)。</summary>
public int[][] DailyConsumptionPerNpcByRank { get; }

/// <summary>添字 = (int)Season の薪の消費の季節係数‰。長さ4(GDD02 §9 / GDD03 §2.1)。</summary>
public int[] FirewoodConsumptionSeasonPermille { get; }
```

**コンストラクタの検証**(既存欄の検証と同じ書き方で):

| 欄 | 拒むもの | 例外 |
| -- | -------- | ---- |
| `LaborPermilleByRank` | `null` / 長さ ≠ 3 / 負の要素 | `ArgumentNullException` / `ArgumentException` / `ArgumentOutOfRangeException` |
| `ProductionRunsPerToolWear` | 0以下 | `ArgumentOutOfRangeException` |
| `DailyConsumptionPerNpcByRank` | `null` / 長さ ≠ 3 / 行が `null` / 行の長さ ≠ `itemCount` / 負の要素 | 同上 |
| `FirewoodConsumptionSeasonPermille` | `null` / 長さ ≠ 4 / 負の要素 | 同上 |

- **長さ3は `NpcRank` の階層数**(既存の `InitialSkillPermilleByRank` と同じ根拠)。**長さ4は `Season` の季節数**
- **`N` が0以下を拒むのは、`FloorDiv(摩耗, N)` がゼロ除算になるからである。** `N = 1`(毎回1個消費)は異常だが構造的には成立するので許す
- **jagged 配列は行ごとに複製して持つ。** 外側だけ `ToArray()` すると、呼び出し側が検証後に行の中身を書き換えられる(既存欄の防御的コピーと同じ理由)

**`BuildM0` の初期値**(すべて調整対象。[GDD02 §13.2](../03-gdd/02-economy.md)):

```csharp
// 添字 = (int)NpcRank(Master, Journeyman, Apprentice)。GDD02 §5.2。
// Journeyman は M0 に存在しない(GDD10)が、階層の欄は先に埋める。
var laborPermilleByRank = new[] { 1000, 800, 300 }; // 単位: ‰

// 1人1日あたりの消費量。添字 = [(int)NpcRank][itemId]。GDD02 §6.1。
// itemId の並びは Grain, Timber, IronOre, Charcoal, Flour, Firewood, Bread, Beer, Tools。
// 徒弟のビールが0なのは、徒弟が給金を持たないため(GDD02 §6.1 / GDD10 §1)。
var dailyConsumptionPerNpcByRank = new[]
{
    new[] { 0, 0, 0, 0, 0, 2, 1, 1, 0 }, // 親方
    new[] { 0, 0, 0, 0, 0, 2, 1, 1, 0 }, // 職人
    new[] { 0, 0, 0, 0, 0, 2, 1, 0, 0 }, // 徒弟
};

// 添字 = (int)Season(Spring, Summer, Autumn, Winter)。GDD03 §2.1。基準は秋の1000‰。
var firewoodConsumptionSeasonPermille = new[] { 800, 400, 1000, 2000 }; // 単位: ‰
```

`ProductionRunsPerToolWear: 30`(単位: 回。GDD02 §5.3)。

> **`WorldGenerator` は変えない。** 追加した欄はどれも初期配置に効かない。工具の初期在庫(`InitialToolStock`)は #33 が既に全世帯へ1個置いている。

### 3. `HouseholdState` の追加欄(`World/HouseholdState.cs`)

```csharp
/// <summary>累積した工具の摩耗(レシピ実行回数)。0以上(GDD02 §5.3)。</summary>
public int ToolWearCount { get; set; }

/// <summary>当日の消費不足量。添字 = itemId。単位: 個(GDD02 §6.1 / §8.4)。</summary>
public int[] UnmetConsumption { get; }
```

- **`ToolWearCount` は setter で負を拒む**(`IsBankrupt` / `SkillPermille` と同じ、手書きのバッキングフィールド + 検証)。負になれば `FloorDiv` が負の商を返し、工具在庫が**増える**
- **`UnmetConsumption` はコンストラクタで長さ `itemCount` を 0 で確保する**(他の在庫配列と同じ)
- **`ToolWearCount` の上限は型では守れない。** 「工具在庫がある間は `0 ≤ ToolWearCount < N`」は `ProductionSystem` の後条件であって、`HouseholdState` は N を知らない。**テストで押さえる**(テスト表 #8)

### 4. `StateHasher` の追随(`Determinism/StateHasher.cs`)

**`Section.Households` の書き込みの末尾に2つ足す**([TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md)「既存の値を動かさず末尾へ足す」。`PurchaseUnitCostAverage` を #33 が末尾に足したのと同じ規律):

```csharp
WriteInt32(hasher, buffer, household.ToolWearCount);
WriteInt32Array(hasher, buffer, household.UnmetConsumption);
```

**`Section` の enum には触らない。** 増えるのは区分ではなく区分の要素の欄である。

**[`StateHasherCoverageTests.ExpectedSectionElementMembers`](../../tests/Visionary.Sim.Tests/Determinism/StateHasherCoverageTests.cs) の `HouseholdState` の行に、`ToolWearCount` と `UnmetConsumption` を足す。** この凍結検査は欄を足した時点で**必ず落ちる**。落ちるのが仕事なので、落ちてから直すこと。

> **残る穴は、この凍結検査自身が doc で認めているものと同じである** — 期待一覧だけを更新して `Compute` を触らなければ緑になる。**ハッシュが実際に2欄を見ていることは、テスト表 #17・#18 が押さえる。**

### 5. `ProductionSystem`(`Systems/ProductionSystem.cs`)

```csharp
public sealed class ProductionSystem : ISimSystem
{
    public ProductionSystem(WorldDefinition definition);

    public RandomStream Stream => RandomStream.Production;
    public Cadence Cadence => Cadence.Daily(hour: 0);

    public void Step(World world, SimContext context);
}
```

- コンストラクタは `null` を `ArgumentNullException` で拒む
- **`context` は使わない。乱数を一切引かない** — [GDD02 §5.3](../03-gdd/02-economy.md) が「確率ではなく決定的に」と決めている。系統 `Production` は `SimScheduler` の登録に一意の識別子が要るから持つ([TDD01 §3.1](../04-tdd/01-sim-core-and-m0.md))
- **`Cadence.Daily(hour: 0)`。** 日次フェーズは同一tickに登録順で走る(TDD01 §3.3)。**時刻を分散させるのは移動を実装する #37 の判断であって、本タスクでは決めない**

**手順(この順序が仕様。世帯Id 昇順に1世帯ずつ)**:

```
recipe       = definition.Recipes[(int)household.Occupation]
設備係数‰    = household.WorkshopInventory[Item.Tools] >= 1 ? 1000 : 0
労働力合計‰  = Σ( definition.LaborPermilleByRank[(int)world.Npcs[npcId].Rank] )
                 npcId は household.MemberNpcIds を先頭から(昇順が構築時に保証されている)
生産能力      = FloorDiv( ApplyPermille(労働力合計‰, 設備係数‰), recipe.LaborPermille )

実行回数      = 生産能力
foreach input in recipe.Inputs:                      ← 配列順。入力0件なら制約しない
    実行回数 = min( 実行回数, FloorDiv(工房在庫[input.ItemId], input.Quantity) )

if 実行回数 <= 0:  この世帯は何もしない(在庫も摩耗も動かさない)。次の世帯へ

foreach input  in recipe.Inputs:  工房在庫[input.ItemId]  -= input.Quantity  × 実行回数
foreach output in recipe.Outputs: 工房在庫[output.ItemId] += output.Quantity × 実行回数

── 摩耗(GDD02 §5.3)。出力を加算した後に行う ──
household.ToolWearCount += 実行回数
worn = FloorDiv( household.ToolWearCount, definition.ProductionRunsPerToolWear )
if worn > 0:
    consumed = min( worn, 工房在庫[Item.Tools] )
    工房在庫[Item.Tools]    -= consumed
    household.ToolWearCount -= consumed × definition.ProductionRunsPerToolWear
    if 工房在庫[Item.Tools] == 0:  household.ToolWearCount = 0
```

**順序・境界を具体例で固定する**([process/02](../process/02-task-spec.md) 規則6):

- **摩耗は出力の加算より後である。** 鍛冶(`鉄鉱石2 + 木炭1 → 工具1`)が工具在庫1・`N = 1` で1回実行すると、**出力先**なら在庫 `1 → 2 → 1`、摩耗カウンタ 0。**摩耗先**なら在庫が一度 0 に落ちて `ToolWearCount = 0` のリセットを踏み、そのあと出力で 1 に戻る。**日末の在庫は同じ1個だが、カウンタの端数が捨てられるかどうかが違う。** 出力先を採るのは、端数を理由なく捨てないためである
- **`実行回数 <= 0` のとき摩耗も進まない。** 「工具切れで停止した工房の工具がさらに摩耗する」ことは無い
- **入力0件のレシピ**(GDD02 §2.3 が許す。M0 に該当なし)は `実行回数 = 生産能力` になる。**`min` の初期値を 0 にしないこと** — 入力の無いレシピが永久に停止する
- **`設備係数‰ = 0` なら `ApplyPermille(労働力合計‰, 0) = 0` で `生産能力 = 0`。** 入力が足りていても停止する
- **`Advance(1)` を `Tick.Zero` から呼ぶと、エポック(1年 春1日 0時)で1回だけ実行される。** `Cadence.Daily(hour: 0)` と `SimScheduler` の「現在tickを処理してから進める」順序による

> **`ApplyPermille` が切り上げヘルパーであることは承知のうえである。** [GDD02 §5.2](../03-gdd/02-economy.md) の註が「設備係数が二値(1000/0)の M0 では乗じても値が変わらないので実害は無い。摩耗を連続化する v1.0 ではこの内側の丸めも切り下げへ直す必要がある」と既に書いている。**本タスクでは直さない。**

### 6. `ConsumptionSystem`(`Systems/ConsumptionSystem.cs`)

```csharp
public sealed class ConsumptionSystem : ISimSystem
{
    public ConsumptionSystem(WorldDefinition definition);

    public RandomStream Stream => RandomStream.Consumption;
    public Cadence Cadence => Cadence.Daily(hour: 0);

    public void Step(World world, SimContext context);
}
```

**手順(この順序が仕様)**:

```
season = GameDate.FromTick(world.Now).Season          ← 1日1回。世帯ごとに引き直さない

世帯Id 昇順に、itemId 0 … ItemCount-1 の昇順に:
    必要量 = 0
    foreach npcId in household.MemberNpcIds:          ← 昇順(構築時に保証されている)
        基礎量 = definition.DailyConsumptionPerNpcByRank[(int)world.Npcs[npcId].Rank][itemId]
        必要量 += (itemId == Item.Firewood)
                    ? ApplyPermille(基礎量, definition.FirewoodConsumptionSeasonPermille[(int)season])
                    : 基礎量

    消費量 = min( 必要量, household.HouseholdInventory[itemId] )
    household.HouseholdInventory[itemId] -= 消費量
    household.UnmetConsumption[itemId]    = 必要量 - 消費量      ← 毎日、全品目を上書きする
```

**順序・境界を具体例で固定する**:

- **季節係数は構成員ごとに切り上げてから合計する**([GDD02 §6.1](../03-gdd/02-economy.md))。親方+徒弟の世帯が夏(400‰)に消費する薪は `ApplyPermille(2, 400) = 1` の2人ぶんで **2**。**M0 の初期値では世帯合計に先に掛けても一致するが、基礎量が奇数なら一致しない**(基礎量1・係数500‰・2人なら、構成員ごと = **2**、世帯合計先 = 1)。**基礎量は #28 の調整対象なので、一致していることに頼らない**
- **`UnmetConsumption` は不足が無い日も 0 で上書きする。** 不足した日だけ書くと、前日の不足が翌日の `Need` として残る
- **減らすのは世帯在庫だけである。** 工房在庫の薪には触れない(2本の在庫の区別は [TDD01 §3.2](../04-tdd/01-sim-core-and-m0.md))
- **季節係数を掛けるのは `Item.Firewood` だけである。** [GDD02 §9](../03-gdd/02-economy.md)「消費の季節性: 薪のみ季節係数を持つ」。`ApplyPermille(0, 係数)` は 0 なので他品目に掛けても現在の値では結果が変わらないが、**基礎量が 0 でない品目が増えた瞬間に黙って季節変動する**
- **`context` は使わない。乱数を一切引かない**(Production と同じ)
- **冬(2000‰)の親方+徒弟の世帯**: 薪 `ApplyPermille(2, 2000) = 4` × 2人 = **8/日**。夏は **2/日**

## 落ちるべき条件(テスト)

**この節が完了条件そのもの。** 目標は「通ること」ではなく「**壊したときに落ちること**」である。

**システムは `SimScheduler.Advance` 経由で走らせる** — `SimContext` のコンストラクタが `internal` で、テストアセンブリからは構築できない。`RandomSource` は任意のシードでよい(両システムとも引かない)。

`WorldDefinition` を直接組み立てて、`所要労働‰` や `N` を小さくしたテスト専用の定義を使ってよい。**`BuildM0` の値に依存したテストを書かないこと** — #28 が値を動かす。

| #  | テスト | 検証内容 | この実装ミスで落ちる | 核心 |
| -- | ------ | -------- | -------------------- | ---- |
| 1  | `FloorDivRoundsTowardNegativeInfinity` | `7/2=3`、`-7/2=-4`、`-6/2=-3`、`6/2=3`、`0/5=0` | 裸の `/`(0方向への切り捨て)をそのまま返す。`(a - b + 1) / b` 型の正数専用の式を書く |  |
| 2  | `FloorDivRejectsZeroDivisorAndOverflow` | 除数0で `DivideByZeroException`、`int.MinValue / -1` で `OverflowException` | `checked` を外す。除数0の検査を落とす |  |
| 3  | `ProductionCapacitySumsAllMembersLaborPermille` | 所要労働 400‰ の定義で、親方+徒弟(1300‰)の世帯が **3回**実行する | 世帯主だけを数える(1000‰ → 2回)。構成員の階層を見ず一律 1000‰ を足す(2000‰ → 5回) |  |
| 4  | `ProductionCapacityRoundsDown` | 所要労働 400‰・労働力 1300‰ で **3回**(切り上げなら4回) | `FloorDiv` を `CeilDiv` にする。`ApplyPermille(労働力, 1000) ÷ 所要労働` を切り上げヘルパーで書く | **核心** |
| 5  | `ProductionIsLimitedByTheScarcestInput` | 生産能力3、入力Aが2回ぶん・入力Bが5回ぶん → **2回**。入力Aは0に、Bは3回ぶん残る | `min` ではなく最初の入力だけを見る。入力ごとの `必要数量` で割らずに在庫の個数で比べる |  |
| 6  | `ProductionStopsWhenAnInputIsExhausted` | 入力在庫0 → 実行回数0。**工房在庫が1つも動かない**(出力も増えない)。負にならない | 入力の充足を確かめずに減算する(在庫が負になる)。実行回数0でも出力を加算する | **核心** |
| 7  | `ProductionStopsWhenToolStockIsZero` | 入力が十分あっても工具在庫0なら実行回数0、在庫が動かない | 設備係数を常に 1000‰ にする。`>= 1` を `>= 0` と書く | **核心** |
| 8  | `ToolWearCarriesOverAcrossDaysUntilNIsReached` | `N = 3`・1回/日で、2日目までは工具在庫が減らず、**3日目に1個減って `ToolWearCount` が 0 に戻る** | 摩耗カウンタを日ごとに捨てる(永久に減らない)。`>=` を `>` と書く(4日目にずれる) |  |
| 9  | `ToolWearLeavesTheRemainderForTheNextTool` | `N = 3`・工具2個・1日に5回実行できる定義 → 工具1個減り `ToolWearCount = 2` が残る | `ToolWearCount = 0` と代入する(端数を捨てる)。`consumed × N` を引かずに `N` だけ引く |  |
| 10 | `ToolWearIsDroppedWhenToolStockRunsOut` | 工具1個・`N = 3`・1日に7回実行できる定義 → 工具0個、**`ToolWearCount = 0`**。翌日は設備係数0で停止する | 在庫の残量で `min` を取らず在庫を負にする。在庫0でもカウンタを残す(次に買った工具が初日に壊れる) |  |
| 11 | `ProductionAppliesOutputQuantity` | 木材加工型(`入力1 → 出力3`)を1回実行 → 出力が **+3**、入力が **−1** | 出力数量を無視して +1 する。入力と出力の数量を取り違える |  |
| 12 | `ProductionRunsWithoutInputsUpToCapacity` | 入力0件のレシピで、生産能力ぶん実行される | `実行回数` の初期値を 0 にする。入力配列が空のとき `min` が 0 に潰れる |  |
| 13 | `ProductionDrawsNoRandomNumbers` | マスターシードだけを変えた2つの `RandomSource` で同じ世界を1日進め、**状態ハッシュが一致する** | `context.OpenRandom` を使って摩耗や生産量を揺らす |  |
| 14 | `ConsumptionScalesWithHouseholdSize` | 1人あたりパン1の定義で、2人世帯の世帯在庫が **1日に2**減る | 世帯あたり1回しか引かない。構成員を二重に数える |  |
| 15 | `ConsumptionDiffersByRank` | 親方1・徒弟0 のビールの定義で、親方+徒弟の世帯が **1**だけ減る | 全員に世帯主の階層を使う。階層の添字を取り違える |  |
| 16 | `FirewoodConsumptionFollowsTheSeason` | 基礎2・係数 `{800,400,1000,2000}` の定義で、夏の世帯消費が **2**、冬が **8**(親方+徒弟) | 季節を見ずに基礎量を使う。`(int)Season` ではなく `DayIndex` などで添字を作る | **核心** |
| 17 | `SeasonCoefficientIsRoundedPerMember` | **基礎量1・係数500‰・2人世帯** → 消費 **2**(世帯合計に先に掛けると1) | 世帯の合計に季節係数を掛けてから切り上げる | **核心** |
| 18 | `ConsumptionStopsAtZeroAndDoesNotGoNegative` | 在庫1・必要2 → 在庫 **0**。翌日も 0 のまま負にならない | 必要量をそのまま引く。`min` を取らない | **核心** |
| 19 | `UnmetConsumptionRecordsTheShortfall` | 在庫1・必要2 → `UnmetConsumption[itemId] == 1` | 不足を記録しない。消費量のほうを書く |  |
| 20 | `UnmetConsumptionIsClearedWhenDemandIsMet` | 前日に不足した世帯へ在庫を補充して1日進めると **0 に戻る** | 不足した日だけ書く(前日の不足が残る)。`+=` で累積する |  |
| 21 | `ConsumptionTouchesOnlyTheHouseholdInventory` | 薪を世帯在庫と工房在庫の両方に置いて1日進め、**工房在庫が動かない** | 在庫を1本と混同する。工房在庫からも引く |  |
| 22 | `SeasonalItemIsOnlyFirewood` | パンの基礎量を1にしたまま冬(2000‰)に進め、**パンの消費が変わらない** | 全品目に季節係数を掛ける |  |
| 23 | `ConsumptionDrawsNoRandomNumbers` | #13 と同じ形を Consumption に当てる | `context.OpenRandom` を使う |  |
| 24 | `HashChangesWhenToolWearCountChanges` | `ToolWearCount` だけを変えると状態ハッシュが変わる | `StateHasher` に書き忘れる(凍結検査だけ更新した状態) |  |
| 25 | `HashChangesWhenUnmetConsumptionChanges` | `UnmetConsumption` の1要素だけを変えると状態ハッシュが変わる | 同上 |  |
| 26 | `ToolWearCountRejectsNegativeValues` | `household.ToolWearCount = -1` が `ArgumentOutOfRangeException` | 検証なしの自動プロパティにする(負のカウンタで工具在庫が増える) |  |
| 27 | `WorldDefinitionRejectsMalformedConsumptionTables` | 階層の行数 ≠ 3 / 行の長さ ≠ `itemCount` / 季節の長さ ≠ 4 / `N` が0 / 負の係数 を、それぞれ拒む | 長さを検査しない(実行時に `IndexOutOfRangeException`)。`N = 0` を通す(ゼロ除算) |  |
| 28 | `WorldDefinitionCopiesEachConsumptionRow` | 渡した jagged 配列の**行の中身**を後から書き換えても、定義が変わらない | 外側だけ `ToArray()` する |  |
| 29 | `ProductionAndConsumptionRunInPipelineOrder` | 両システムを `SimScheduler` に順1・順2で登録して1日進めると、**その日に生産した出力が同じ日の消費の対象になっていない**(生産は工房在庫、消費は世帯在庫) | 生産の出力を世帯在庫に入れる |  |

**「核心」印(#4・#6・#7・#16・#17・#18)には実際に変異を当てて落ちることを確認し、当てた変異と結果を doc コメントかコミット本文に残す**([process/02](../process/02-task-spec.md))。

## 2巡目レビューで足したテスト(別表)

**上の表は implementer に渡した時点の指示であり、最終形ではない。** 2巡目のレビューが象限I-a(緑のまま壊れる)として2件を挙げたので、ここに足す。**上の表を書き換えずに別表にするのは、「何を最初に指示し、何を後から足したか」を残すためである。**

**足す理由は共通して「上の表が『このミスで落ちる』と宣言した変異が、実際には落ちない」ことである。** 上の表の #5 と #9 は変異を名指ししているが、**その変異を判別できる値をテストが通っていなかった**。

- **#9 が名指しした「`consumed × N` を引かずに `N` だけ引く」** — `ProductionSystem` を走らせるテストは4本とも `consumed == 1` に収束しており、`consumed × N` と `N` が同値になる。**掛け忘れても全テストが緑になる**
- **#5 が名指しした「入力ごとの `必要数量` で割らずに在庫の個数で比べる」** — 入力の `Quantity` がどのテストでも `1` であり、「在庫の個数」と「数量で割った回数」が一致する。**割り忘れても掛け忘れても全テストが緑になる**

**どちらも現在の `BuildM0` では差が出ない。** 生産能力が 1 実行/日 に張り付いている(上記「#28 への申し送り」)ため `consumed` は 1 を超えず、在庫も負に落ちない。**差が出るのは #28 が `所要労働‰` を 100‰ 台へ下げて1日複数回の実行が成立した瞬間**であり、そのとき変わるのは `World` の内部状態の数値だけで、テストは全部緑のままである。**「値の調整不足」と読めてしまい、配線ミスを疑う契機が構造的に生まれない。**

| #  | テスト | 検証内容 | この実装ミスで落ちる | 核心 |
| -- | ------ | -------- | -------------------- | ---- |
| 30 | `ToolWearConsumesMultipleToolsInOneDay` | **`N = 3`・工具在庫3個・1日に7回実行できる定義** → 工具が **2個**減って **1個**残り、`ToolWearCount == 1`(= 7 − 2 × 3) | **`consumed × N` を引かずに `N` だけ引く**(`ToolWearCount` が 4 になり、「工具在庫がある間は `0 ≤ ToolWearCount < N`」の後条件が破れる)。`consumed` を在庫で clamp した値ではなく `worn` のまま在庫から引く | **核心** |
| 31 | `ProductionDividesAndMultipliesByInputQuantity` | **入力A(数量2・在庫5)と入力B(数量3・在庫15)を持つレシピ**で生産能力3 → 実行 **2回**。入力Aは **1**、入力Bは **9** が残る | **`FloorDiv(在庫, 必要数量)` を書かず在庫の個数で比べる**(実行3回になり入力Aが **−1** に落ちる)。**`-= 必要数量 × 実行回数` の掛け算を落とす**(入力Aに 3、入力Bに 13 が残る)。**`FloorDiv` を `CeilDiv` にする**(`CeilDiv(5,2) = 3` で実行3回になり入力Aが **−1** に落ちる) | **核心** |

- **2件とも核心印を付ける。** 上の表が名指しした変異が落ちないことが指摘の中身なので、**実際に変異を当てて赤になることの確認までが対応である**。当てた変異と結果は doc コメントかコミット本文に残す
- **#30 は上の表の #9・#10 を置き換えない。** #9 は「端数を次の工具へ持ち越す」(`consumed == 1` で端数が残る)、#10 は「在庫が尽きたらカウンタを捨てる」(在庫で clamp する)を押さえており、**#30 が押さえるのは `consumed >= 2` の経路だけ**である。3本は別の境界を見ている
- **#31 は上の表の #5 を置き換えない。** #5 は「最も逼迫した入力で決まる」(`min` を取ること)を押さえており、**#31 が押さえるのは数量による除算と乗算**である。#5 が入力2本の `min` を見るのに対し、#31 は `Quantity > 1` の経路を通す
- **`BuildM0` の値に依存しないこと**(上の表と同じ)。#31 の「数量2の入力」は `Miller`(穀物2 → 小麦粉1)と `Smith`(鉄鉱石2 + 木炭1 → 工具1)が実際に持つ形だが、**テストは専用の定義を組み立てる**。#28 が値を動かす

> **#31 の入力Aの在庫を 4 から 5 に直した。**(3巡目レビュー I-a の訂正)
>
> **当初 `4 ÷ 2` と `15 ÷ 3` を置いていたが、どちらも割り切れる。** 2巡目の指摘は「`Quantity > 1` の経路を一度も通っていない」であり、割り切れる値でも「割るか割らないか」は判別できるので指摘そのものには応えていた。**しかし割り切れる値では、この除算が丸める場面が一度も作られない。** `FloorDiv` を `CeilDiv` に変異させても**全テストが緑のまま通ることが実測された**(3巡目)。
>
> **この行を守る手段はテストの判別力しかない。** 本書 §1 の「残る穴」が「`FloorDiv` を用意しても裸の `/` を書くことは防げない。`BannedSymbols.txt` は演算子を禁止できず、`DeterminismConventionTests` も演算子は見ていない。**機械では捕まらない**」と宣言したとおりで、向きの取り違えを止めるものが他に無い。しかもこの除算は [GDD02 §5.2](../03-gdd/02-economy.md) が「**切り上げ規約の意図的な例外**」と明記した数少ない例外であり、**プロジェクトの既定(切り上げ)に引き戻す変異は、書き手の善意からでも起こりうる。**
>
> **在庫を 5 にすると `FloorDiv(5,2) = 2` / `CeilDiv(5,2) = 3` で向きが分かれる。** 判別できる変異が2種から3種に増え、テストは1本のままである。
>
> **`0 < 在庫 < 必要数量`(切り上げると在庫が負に落ちる境界)を別のテストとして足さないのは、それを壊す変異が `CeilDiv` と同一だからである。** 上の在庫5で同じ変異が赤になるので、行を増やしても判別できる変異は増えない。**上の表 #6 が押さえるのは在庫ちょうど0の場合である**ことは変わらない。

## 編集してよい文書

worktree をまたいだ所有権の宣言。**ここに挙がっていない文書は触らない。**

- なし(コードとテストのみ)

**GDD02 §5.2・§5.3・§6.1・§9 と GDD03 §2 は、本タスクの設計(フェーズ1)で既に改訂済みである。** 実装が仕様と食い違ったら、**直すのはコードであって文書ではない**。文書側を直す必要があると判断したら、それは象限I-b(仕様そのものの欠陥)なので[止まって報告する](../process/02-task-spec.md)。

## このタスクで特に効く規約

[ADR-0002](../adr/0002-time-model-and-determinism.md) の決定論規約のうち、このタスクで踏みやすいものだけを挙げる。**機械で捕まるものは書かない**(浮動小数点と列挙順が保証されないコレクションは `BannedSymbols.txt` と `DeterminismConventionTests` がビルドとテストで止める)。

- **切り下げの除算は `FloorDiv` を通す。裸の `/` を書かない。** 本タスクの被除数は非負なので結果は同値であり、**機械でも捕まらない**。意図の記録としてヘルパーを通す
- **係数の定数には単位のコメントを付ける**(`= 800; // ‰`、`= 30; // 回`)
- **走査順は世帯Id 昇順 → 構成員 NpcId 昇順 → itemId 昇順。** `MemberNpcIds` の昇順は `HouseholdState` の構築時に検証済みなので、**並べ替え直さずに先頭から使う**
- **`Season` / `NpcRank` / `RandomStream` の値は仕様である。** 配列の添字に `(int)` で使うので、振り直すと表の行がずれる

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **「核心」印のテストに変異を当てて落ちることを確認し、当てた変異と結果を残した**
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
