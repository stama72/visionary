# W2-04: 提示価格の更新式と Market

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#35](https://github.com/stama72/visionary/issues/35)                |
| 根拠     | [GDD02 §8.1・§8.1.1・§6.3・§8.2.7](../03-gdd/02-economy.md) / [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) / [TDD01 §3.2・§3.3](../04-tdd/01-sim-core-and-m0.md) |
| ブランチ | `feat/35-offer-price-and-market`                                     |
| worktree | `visionary/`(本体)                                                  |

> **この文書は使い捨ての作業指示である。** 実装完了時点で凍結し、以後の正はコードと GDD/TDD。

## スコープ

**売り手が「いくらで並べるか」を決めて `Market` に書く、パイプライン順5 の前半である。** [#34](https://github.com/stama72/visionary/issues/34) が在庫を動かすところまで作ったので、その在庫から価格が出る。**M0 で初めて貨幣が World の状態に現れる系統**でもある。

**含まない:**

- **店の選択・約定・帳簿記帳**(#37)。本タスクは `Market` に価格を書くだけで、`LiquidFunds` も `Ledgers` も読み書きしない
- **観測(`PriceObservation`)の生成**(#36)。本タスクは**読む側の規約だけ**を決める。生成規則(どこで生まれるか・帰宅時の世帯共有)は #36
- **予算・購入量**(#36)。相場基準の作り方は売り手の値付けと買い手の予算で同じ規約だが([GDD02 §8.1.1](../03-gdd/02-economy.md))、本タスクが作るのは売り手側だけである
- **都市外市場**(#38)。窓口の観測を除外しない規約だけ置く(下記)
- **破産中フラグを立てること**(#39)。本タスクは**読むだけ**。フラグは順3 が前日の購入結果から立てる
- **②の投げ売りを順3 に置くこと。** [GDD02 §6.3](../03-gdd/02-economy.md) が明示的に禁じている。②は本タスクの値付けの分岐である
- **妥結価格・交渉**([GDD06 §6](../03-gdd/06-trade-and-negotiation.md) が M0 は提示価格をそのまま妥結価格とする)
- **`vsim` / Runner の追随。** [`SyntheticLoadSystem`](../../src/Visionary.Sim.Runner/Determinism/SyntheticLoadSystem.cs) のままにする。理由は [W2-03](W2-03-production-and-consumption.md) と同じ — パイプラインが揃う #41 までは、合成負荷が `StateHasher` の全区分を CI の2プロセス比較に踏ませている
- **値の作り込み**([#28](https://github.com/stama72/visionary/issues/28))。置き場所と型と丸めの向きは本タスクで決める

## 設計の前提(フェーズ1で決めたこと)

### `Market` を書くのはこのシステムだけである

[GDD02 §6.3](../03-gdd/02-economy.md) が「書き込みは1か所のままである」と決めている。約定(#37)は在庫・資金・帳簿を動かすが **`Market` には触らない**。②の投げ売りを順3 に置かないのも同じ理由である。

### 毎日クリアして書き直す

**`Market` のエントリがあることが「前日に売り注文を出していた」と同値になるのは、毎日クリアして書き直すからである。** [GDD02 §8.1.1](../03-gdd/02-economy.md) の「自分の前日の提示価格が存在しない日」の判定が、専用フィールドなしで成立する根拠がこれである([TDD01 §3.2](../04-tdd/01-sim-core-and-m0.md)「更新前の `Market` の値がそれである」)。職業付け替え(#39)で出力品目が変わった売り手の古いエントリが残らないのも同じ帰結。

**したがって実装は2段である。前日の値を読み終える前に `Market` を書き換えない。**

1. 全売り手ぶんの新しい提示価格を求める(この間 `Market` は読むだけ)
2. `Market.Clear()` してから、1 の結果を一括で書く

> **「売り手ごとに読んでは書く」が結果を変える経路は、実は1つしかない。** 相場基準の材料は(a)世帯主の観測(前日まで。この間 `Knowledge` は動かない)と(b)`Market[(品目, 自世帯)]` = **自分の**前日価格だけで、他の売り手の当日価格は入らない。**壊れるのは「先に `Market.Clear()` してしまう」形であり、そのとき全売り手が自分の前日価格を失う。** テスト #20 がこれを見る。

### 原価の未定義経路は例外で落とす

[GDD02 §8.1.1](../03-gdd/02-economy.md) の原価は2分岐だが、**M0 の5職業はすべて「財の投入がある職業」である**。`Recipe` は入力0件と複数出力を構造として許す([GDD02 §2.3](../03-gdd/02-economy.md))ので、実装は何かを返さなければならない。**どちらも `NotSupportedException` で落とす**(開発者判断)。

- **入力0件(機会費用ベースの分岐)** — 機会費用は順0 `OpportunityCost` の成果物であり未実装、[GDD08 §9](../03-gdd/08-household-and-decision.md) の値もコードに無い。ここで入れると「M0 で一度も実行されない式」と「調整軸2本(機会費用・可処分時間T)」を同時に抱える。発火するのは [GDD11](../03-gdd/11-external-trade.md) の貿易商と [GDD12](../03-gdd/12-countryside-and-fairs.md) の農村職業が戻る時点([#51](https://github.com/stama72/visionary/issues/51))
- **複数出力** — 入力費を各出力へどう配分するかの規則を **GDD02 が持っていない**。ここで決めると仕様がコードにだけ存在する状態になる(CLAUDE.md「実装コメントやADRに仕様を溜めない」)

**0 を返して通す形は採らない。** [GDD02 §8.1.1](../03-gdd/02-economy.md) が「原価が 0 になると、その品目の売り手が全員そうなら相場基準も 0 にしかならず、0 が恒久に固定される」と警告した経路そのものである。`WorldDefinition` が取得原価0を既に拒んでいるのと同じ歯止めを、式の側にも置く。

**検査は `TradeSystem` のコンストラクタで行う。** 定義の全レシピを一度見て落とす。値付けの最中に落とすと、**販売在庫がたまたま0の日だけ生き延びる**ので、失敗が在庫に依存して再現しない。

### 販売在庫が0の日は売り注文を出さない

[GDD02 §8.1.1](../03-gdd/02-economy.md) の `販売在庫` は**工房在庫のうち出力品目の全量**である。0 のときエントリを作らない:

- **[GDD02 §8.1.1](../03-gdd/02-economy.md) が「前日に売り注文を出していない日」を明示的に想定している。** 在庫0の日がそれである
- 在庫比0 → 価格係数1500‰ で「品薄の高値」が付くが、**売るものが無い**。出品すると #37 の店選択に**在庫のない店**が候補として載る

> **在庫比の表の「販売在庫 0 → 1500‰」の行は、この規則によって `Market` には現れない。** 式の形を示す行であって、出品される価格ではない。

## 作るもの

名前空間は `Visionary.Sim`(`Definition/`)と `Visionary.Sim.Systems`(`Systems/`)。

**`World` の区分は1つも増えない。** `Market` は #33 で既にあり `StateHasher` も持っている([TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md))ので、**ハッシュ側の変更は無い**。

### 1. `WorldDefinition` の追加欄(`Definition/WorldDefinition.cs`)

**3つの欄をコンストラクタ引数として足す。** 既存引数の後ろに並べること。

```csharp
/// <summary>最低利幅‰。原価下限 = ApplyPermille(原価, 1000 + これ)(GDD02 §8.1)。</summary>
public int MinimumMarginPermille { get; }

/// <summary>
/// 出荷目標在庫。添字 = [(int)Occupation][itemId]。単位: 個(GDD02 §8.1.1)。
/// </summary>
public int[][] ShipmentTargetStockByOccupation { get; }

/// <summary>相場観測の保持期間。単位: 日(GDD06 §3.1)。</summary>
public int ObservationRetentionDays { get; }
```

**コンストラクタの検証**(既存欄と同じ書き方で):

| 欄 | 拒むもの | 例外 |
| -- | -------- | ---- |
| `MinimumMarginPermille` | 負 | `ArgumentOutOfRangeException` |
| `ShipmentTargetStockByOccupation` | `null` / 長さ ≠ 職業数 / 行が `null` / 行の長さ ≠ `itemCount` / **レシピの出力品目の欄が0以下** / **出力品目以外の欄が0でない** | `ArgumentNullException` / `ArgumentException` / `ArgumentOutOfRangeException` |
| `ObservationRetentionDays` | 1未満 | `ArgumentOutOfRangeException` |

- **出力品目の欄が0を拒むのは、在庫比‰ の分母だからである**(ゼロ除算)。[GDD02 §8.1.1](../03-gdd/02-economy.md) が「定数にすれば、停止中の工房も『まだ目標に届いていない』と正しく判定される」と決めた形が、0 だと崩れる
- **出力品目以外を0に強制するのは、表から「どの欄が効くか」を読めるようにするためである。** 品目 × 職業の表は 5 × 9 = 45 欄あるが、意味を持つのは5欄しかない。非0を許すと「木材加工の工具の目標在庫」のような**読まれない値**が表に住み着く
- **`MinimumMarginPermille = 0` は許す。** 「利幅なし = 原価がそのまま下限」は構造として成立し、[GDD02 §12-5](../03-gdd/02-economy.md)(原価を下回る提示価格が継続しないか)の測り方も変わらない。負だけを拒む
- **`ObservationRetentionDays` が1以上なのは、0 だと有効な観測が永久に0件になるからである**(下記の境界規則により差が1日以上必要)。[GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) の「その品目が売られる間隔より長い」は**値の選び方**の制約であり、構造としての下限は1
- **jagged 配列は行ごとに複製して持つ**(既存欄と同じ)

**`BuildM0` の初期値**(すべて調整対象。[GDD02 §13.2](../03-gdd/02-economy.md)):

```csharp
const int MinimumMarginPermilleForM0 = 200; // ‰。20%

// 出荷目標在庫。添字 = [(int)Occupation][itemId]。単位: 個(GDD02 §8.1.1)。
// 初期値は「3日分の出力」— 生産能力が 1実行/日 に張り付いている(#28 への申し送り、
// W2-03)ので、出力数量 × 3 を置く。出力品目以外は 0(コンストラクタが強制する)。
var shipmentTargetStockByOccupation = new[]
{
    NewShipmentRow(Item.Flour, 3),     // Miller:     小麦粉1/日 × 3
    NewShipmentRow(Item.Bread, 6),     // Baker:      パン2/日 × 3
    NewShipmentRow(Item.Beer, 3),      // Brewer:     ビール1/日 × 3
    NewShipmentRow(Item.Firewood, 9),  // Woodworker: 薪3/日 × 3
    NewShipmentRow(Item.Tools, 3),     // Smith:      工具1/日 × 3
};

const int ObservationRetentionDaysForM0 = 7; // 日(GDD06 §3.1)
```

`NewShipmentRow` は `BuildM0` の中の `private static` ヘルパー(長さ `Item.Count` の配列を作り、1欄だけ埋める)でよい。**行を手書きで9個並べない** — 出力品目がどれかが読めなくなる。

### 2. `OfferPrice`(`Systems/OfferPrice.cs` を新規作成)

**`static` クラス。全メソッドが純関数で、`World` も `WorldDefinition` も受け取らない。** 式を単体で試験できる形に切り出す。

```csharp
/// <summary>在庫比‰ = CeilDiv(1000 × 販売在庫, 出荷目標在庫)。</summary>
public static int StockRatioPermille(int sellableStock, int shipmentTargetStock);

/// <summary>価格係数‰ = clamp(1500 − CeilDiv(在庫比‰, 2), 500, 1500)。</summary>
public static int PriceCoefficientPermille(int stockRatioPermille);

/// <summary>原価下限。破産中(isBankrupt == 1)は 500‰ へ下げる(GDD02 §6.3②)。</summary>
public static int CostFloor(int unitCost, int minimumMarginPermille, int isBankrupt);

/// <summary>出力1単位あたりの原価(GDD02 §8.1.1)。</summary>
public static int UnitCost(Recipe recipe, int[] purchaseUnitCostAverage);

/// <summary>提示価格 = max(原価下限, ApplyPermille(相場基準, 価格係数‰))。</summary>
public static int Calculate(int costFloor, int marketReference, int sellableStock, int shipmentTargetStock);

/// <summary>相場基準(GDD02 §8.1.1「相場基準」)。0件なら false を返す。</summary>
public static bool TryMarketReference(
    IReadOnlyList<PriceObservation> headObservations,
    int itemId,
    int selfHouseholdId,
    Tick now,
    int retentionDays,
    bool hasOwnPreviousPrice,
    int ownPreviousPrice,
    out int marketReference);
```

**式の定数**(`OfferPrice` の中に `private const` で、単位のコメント付きで置く):

```csharp
private const int MaxCoefficientPermille = 1500; // ‰。在庫0のときの上限(GDD02 §8.1.1)
private const int MinCoefficientPermille = 500;  // ‰。溢れているときの下限(同)
private const int BankruptFloorPermille = 500;   // ‰。②の投げ売りの床(GDD02 §6.3②)
```

> **`BankruptFloorPermille` と `MinCoefficientPermille` は同じ 500 だが、別の定数として置く。** [GDD02 §6.3](../03-gdd/02-economy.md) が「床の 500‰ は新しい係数ではない。§8.1.1 の clamp の下限と同じ値を使う」と書いたのは**値の由来**の説明であって、**一方を動かしたら他方も動く**という関係ではない。1つの定数にすると #28 が投げ売りの深さだけを調整できない。

#### `StockRatioPermille` / `PriceCoefficientPermille`

```
在庫比‰   = CeilDiv(1000 × 販売在庫, 出荷目標在庫)
価格係数‰ = clamp(1500 − CeilDiv(在庫比‰, 2), 500, 1500)
```

- **1000 は `IntegerMath.PermilleScale` を使う。** 裸の `1000` を書かない
- **`1000 × 販売在庫` は `long` で持つ**(`CeilDiv(long, long)` を通す)。販売在庫が 214 万を超えると int で桁あふれする
- **`CeilDiv(在庫比‰, 2)` の切り上げは意図的であり、式全体としては切り下げ方向になる。** [GDD02 §8.1.1](../03-gdd/02-economy.md) の註が「品薄側へ偏るほうが §4.1・§8.2.4 の設計意図に沿う」として、**切り上げ規約を在庫比‰ の算出にだけ適用し係数全体には及ぼさない**と明記している。**`FloorDiv` に揃える変異は善意から起こりうる**(テスト #3)
- `shipmentTargetStock` が0以下なら `ArgumentOutOfRangeException`。`WorldDefinition` が既に拒んでいるが、**純関数の側でも拒む**(ゼロ除算が `DivideByZeroException` として出ると、どの表が空欄なのかが分からない)

#### `CostFloor`

```
破産中フラグ == 0 → ApplyPermille(原価, 1000 + 最低利幅‰)
破産中フラグ == 1 → ApplyPermille(原価, 500)
```

- **「外す」のではなく「下げる」。** 原価下限が消えると `相場基準 × 500‰` の半減ループに底が無くなる([GDD02 §6.3](../03-gdd/02-economy.md) の導出: 不動点が Q/3、同時に立てば毎日半減して 1 まで落ちる)
- `isBankrupt` が 0 / 1 以外なら `ArgumentOutOfRangeException`。**`!= 0` でも `== 1` でも書けてしまうので、値域を入口で閉じる**(`HouseholdState.IsBankrupt` の setter と同じ理由)

#### `UnitCost`

```
原価 = CeilDiv( Σ_j(仕入れ移動平均単価[入力j] × 必要数量_j) , 出力数量 )
```

- **Σ は `long` で積む。** 単価 × 数量の合計は int を超えうる
- **この除算は切り上げる**([GDD02 §8.1.1](../03-gdd/02-economy.md)。切り上げても提示価格が上がる方向にしか効かず、「原価は移動平均」の保守性と整合する。[GDD02 §5.2](../03-gdd/02-economy.md) の**切り下げる例外とは向きが逆**である)
- `recipe.Inputs.Length == 0` → `NotSupportedException`(機会費用ベースの分岐。上記「原価の未定義経路」)
- `recipe.Outputs.Length != 1` → `NotSupportedException`(配分規則が GDD02 に無い)
- **`PurchaseUnitCostAverage` の更新規則は本タスクに無い。** 仕入れ時の移動平均の更新は #37。本タスクは**読むだけ**で、初期値は `WorldGenerator` が `InitialAcquisitionCost` から書いている(#33)

#### `Calculate`

```
提示価格 = max( 原価下限 , ApplyPermille(相場基準, 価格係数‰) )
```

**相場基準が立たないときは呼ばない** — 呼び出し側が原価下限をそのまま提示価格にする([GDD02 §8.2.7](../03-gdd/02-economy.md)「`max` の第2項が消えるだけで式は分岐しない」)。

#### `TryMarketReference`

**有効な観測を売り手ごとに1件へ畳んで平均する。**

```
有効な観測 = headObservations のうち、次をすべて満たすもの
    o.ItemId   == itemId
    o.SellerId != selfHouseholdId
    1 <= (now.DayIndex − o.ObservedAt.DayIndex) <= retentionDays

売り手ごとに最新の1件だけを採る(同じ SellerId の中で ObservedAt が最大のもの)

if (採用件数 == 0) → false(相場基準を立てない)

合計 = Σ(採用した観測の Price)
件数 = 採用件数
if (hasOwnPreviousPrice) { 合計 += ownPreviousPrice; 件数 += 1; }

marketReference = CeilDiv(合計, 件数)  → true
```

**境界を具体例で固定する。** `retentionDays = 7`、`now` が **D10**(`DayIndex == 10`)のとき:

| 観測日 | 差 | 有効か | 根拠 |
| ------ | -- | ------ | ---- |
| D10 | 0 | **無効** | [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md)「記憶は前日まで」。当日の観測を使うと日内の相互参照が復活する |
| D9 | 1 | 有効 | 前日 |
| D3 | 7 | **有効** | 保持期間ちょうどは入る |
| D2 | 8 | 無効 | 保持期間を過ぎた |

**判定は `DayIndex` の差で行う。時刻(`HourOfDay`)を見ない。** 順5 は `Daily(hour: 0)` なので当日の観測はまだ存在しないはずだが、**コマンド(§3.4)は任意の tick に割り込む**ので tick 差で書くと「同日の午後の観測」が前日扱いになりうる。

**規約:**

- **誰の観測かは世帯主(親方)である**([GDD02 §8.1.1](../03-gdd/02-economy.md))。`world.Knowledge[household.HeadNpcId]` だけを読む。**買い物に行くのが徒弟でも値付けに使うのは世帯主の観測**
- **売り手ごとに最新の1件だけ。** レコード単位で平均すると**よく見る売り手ほど重みが増す**。[GDD02 §8.1.1](../03-gdd/02-economy.md) の交互振動の導出も「前日の値だけで決まる1次の差分方程式」であることに立っている
- **同着(同一 tick に同一売り手の観測が2件)は、後に追加されたほう(添字が大きいほう)を採る。** 走査で `>=` を使う。#36 の生成規則では起きないが、**タイブレークを決めておかないと `List` の並びが結果を変える**
- **`SellerId != selfHouseholdId` を値付け側でも確かめる。** [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) は「自分の売り注文は自分の観測に入れない」と生成側で決めているが、**ここで守らないと「他の売り手の観測が1件以上」という条件を自分で満たしてしまう** — [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) が「純粋な自己ループ」と呼んで避けた状態が、観測の生成側の取りこぼし1つで成立する
- **自分の前日の提示価格は、他の売り手の観測が1件以上あるときだけ加える**([GDD02 §8.1.1](../03-gdd/02-economy.md) / [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md))
- **無い日は集合に加えないだけである。0 を足さない**([GDD02 §8.1.1](../03-gdd/02-economy.md) が名指しした誤り)。件数も増やさない
- **平均は `CeilDiv`。** 合計は `long` で積む
- **`ObservationSource`(自分で見たか聞いたか)で区別しない。** [GDD02 §8.1.1](../03-gdd/02-economy.md) は区別していない。M0 が生成するのは `Direct` だけで、`Heard` は Rumor(#42、W4)が入ってから現れる
- **都市外市場の窓口(`HouseholdState.ExternalMarketSellerId`)の観測も除外しない。** [GDD02 §10.2](../03-gdd/02-economy.md) が「店の候補としては1人の売り手と同じに扱う」と決めている。**M0 では発火しない** — 窓口が並べるのは1次産品(#38)で、都市の売り手はいずれも都市生産品しか出力に持たないため、`itemId` が一致しない
- **売り手ごとの畳み込みには `SortedDictionary<int, PriceObservation>`(キー = SellerId)を使う。** `Dictionary` は列挙順が保証されず、ADR-0002 の規約に触れる(合計そのものは順序に依らないが、**規約を型で守る**)

### 3. `TradeSystem`(`Systems/TradeSystem.cs` を新規作成)

```csharp
public sealed class TradeSystem : ISimSystem
{
    public TradeSystem(WorldDefinition definition);
    public RandomStream Stream => RandomStream.Trade;
    public Cadence Cadence => Cadence.Daily(hour: 0);
    public void Step(World world, SimContext context);
}
```

- **順5 に2つのシステムを置けない。** `SimScheduler` は `Stream` の重複登録を拒む(共通乱数法が壊れる)。**店選択・約定(#37)はこのクラスの中に足す。** 本タスクが実装するのは `Step` の先頭、値付けの段だけである
- **乱数を一切引かない。** `Stream` が `RandomStream.Trade` を持つのは登録に一意な識別子が要るからであって、`OpenRandom` を呼ぶためではない(`ProductionSystem` と同じ。[TDD01 §3.1](../04-tdd/01-sim-core-and-m0.md))
- **コンストラクタで全レシピの形を検査する**(入力0件 / 出力2件以上 → `NotSupportedException`)。上記「原価の未定義経路」

**`Step` の手順:**

```
1. 新しい提示価格を求める(この間 world.Market は読むだけ)

   世帯 Id 昇順に(world.Households は添字 = Id なので先頭から):
       recipe        = definition.Recipes[(int)household.Occupation]
       outputItemId  = recipe.Outputs[0].ItemId        // 出力1件はコンストラクタが保証
       sellableStock = household.WorkshopInventory[outputItemId]

       if (sellableStock <= 0) → この売り手は出品しない(次の世帯へ)

       unitCost  = OfferPrice.UnitCost(recipe, household.PurchaseUnitCostAverage)
       costFloor = OfferPrice.CostFloor(
                       unitCost, definition.MinimumMarginPermille, household.IsBankrupt)

       hasOwn = world.Market.TryGetValue(
                    new MarketKey(outputItemId, household.Id), out int ownPreviousPrice)

       hasReference = OfferPrice.TryMarketReference(
                          world.Knowledge[household.HeadNpcId],
                          outputItemId, household.Id, world.Now,
                          definition.ObservationRetentionDays,
                          hasOwn, ownPreviousPrice, out int marketReference)

       target = definition.ShipmentTargetStockByOccupation[(int)household.Occupation][outputItemId]

       price = hasReference
             ? OfferPrice.Calculate(costFloor, marketReference, sellableStock, target)
             : costFloor                                  // GDD02 §8.2.7

       求めた (MarketKey, price) を一時の List に積む

2. world.Market.Clear() してから、1 で積んだものを順に書く
```

- **職業は世帯の**現在の**値を読む**(#39 の付け替えで変わる)。出荷目標在庫も現在の職業の行を引く
- **`sellableStock` は工房在庫の出力品目だけ。** 世帯在庫は見ない([GDD02 §8.1.1](../03-gdd/02-economy.md)「販売在庫は工房在庫の部分集合であり、生産の入力として抱えている分は売りに出ていない」)。パン屋が持つ薪は工房在庫にあるが**出力ではない**ので売り注文にならない

## 落ちるべき条件(テスト)

**この節が完了条件そのもの。** 目標は「通ること」ではなく「**壊したときに落ちること**」である。

- **`OfferPrice` は純関数なので直接呼ぶ。** `TradeSystem` を走らせるテストは `SimScheduler.Advance` 経由にする(`SimContext` のコンストラクタが `internal`)。`RandomSource` は任意のシードでよい
- **`BuildM0` の値に依存したテストを書かない**(#28 が値を動かす)。テスト専用の `WorldDefinition` を組み立てること。**#19 だけは例外的にパイプラインを M0 の定義で回すが、期待値は定義から計算して比較する**(数値をハードコードしない)

| #  | テスト | 検証内容 | この実装ミスで落ちる | 核心 |
| -- | ------ | -------- | -------------------- | ---- |
| 1  | `StockRatioIsCeiledAgainstTheShipmentTarget` | 在庫1・目標3 → **334**(切り下げなら333)。在庫0 → 0。在庫3・目標3 → 1000 | 裸の `/` を書く。`CeilDiv` を `FloorDiv` にする |  |
| 2  | `StockRatioRejectsNonPositiveTarget` | 目標0・負 で `ArgumentOutOfRangeException` | 検査せず `DivideByZeroException` を出す(どの表の欄が空なのか分からなくなる) |  |
| 3  | `PriceCoefficientRoundsTheRatioUpButTheCoefficientDown` | 在庫比 **1001** → **999**(`FloorDiv` なら1000)。在庫比1000 → 1000 | `CeilDiv(在庫比, 2)` を `FloorDiv` にする(GDD02 §8.1.1 の註が意図的と明記した向き。**善意で切り上げ規約に揃えられる**) | **核心** |
| 4  | `PriceCoefficientIsClamped` | 在庫比0 → 1500、在庫比2000 → 500、在庫比 **2001** → 500(clamp が無ければ499)、在庫比10000 → 500 | clamp を外す(在庫比3000 で 0、やがて負になる) |  |
| 5  | `CostFloorAppliesTheMinimumMargin` | 原価100・利幅200‰ → **120**。利幅0‰ → 100 | `1000 + 利幅` を `利幅` だけにする(20)。`ApplyPermille` を通さず `原価 + 利幅` と書く |  |
| 6  | `BankruptSellerFloorsAtHalfTheCost` | 原価100・利幅200‰・フラグ1 → **50** | フラグを見ない(120)。下限を0にして「外す」(GDD02 §6.3 が禁じた形。半減ループに底が無くなる) | **核心** |
| 7  | `CostFloorRejectsFlagsOutsideZeroAndOne` | `isBankrupt = 2` / `-1` で `ArgumentOutOfRangeException` | `!= 0` で書く(2 が破産扱いになり、`== 1` で書いた #39 と食い違う) |  |
| 8  | `UnitCostNormalizesToOneOutputUnit` | 入力(単価8・数量1)→ 出力 **3** のレシピ → 原価 **3**(= CeilDiv(8,3)) | 出力数量で割らない(8)。`CeilDiv` を `FloorDiv` にする(2) | **核心** |
| 9  | `UnitCostSumsEveryInputTimesItsQuantity` | 入力(単価10・数量2)+(単価6・数量1)→ 出力1 → **26** | 数量を掛けない(16)。最初の入力だけ見る(20) |  |
| 10 | `UnitCostRejectsRecipesWithNoInputOrManyOutputs` | 入力0件 / 出力2件 で `NotSupportedException` | 0 を返す(GDD02 §8.1.1 が警告した「0 が恒久に固定される」経路)。出力数量の合計で割る(GDD02 に無い配分規則) |  |
| 11 | `OfferPriceTakesTheHigherOfFloorAndReference` | 下限120・相場基準100・在庫0(係数1500‰)→ **150**。同じ下限で在庫が目標の2倍(係数500‰)→ **120** | `min` を取る。相場基準側だけを返す(原価割れが常態化する)。下限側だけを返す(在庫圧力が価格に効かない) | **核心** |
| 12 | `MarketReferenceTakesTheLatestObservationPerSeller` | 売り手A(D8:100, D9:200)と売り手B(D9:300)、now=D10 → **250**(= CeilDiv(500,2)) | レコード単位で平均する(200)。**よく見る売り手の重みが増す** | **核心** |
| 13 | `MarketReferenceIsCeiled` | 観測 100 と 101 の2件 → **101** | 切り下げる(100) |  |
| 14 | `MarketReferenceExcludesTodaysObservations` | now=D10、観測が D10 の1件だけ → **false**(相場基準が立たない) | 日付で絞らない。当日の観測を使うと GDD06 §3.1 の「日内の相互参照を切る」が崩れる | **核心** |
| 15 | `MarketReferenceHonoursTheRetentionBoundary` | retention=7・now=D10: **D3 は有効・D2 は無効**。D9 は有効 | `<` と `<=` を取り違える。`DayIndex` ではなく tick 差で書く |  |
| 16 | `MarketReferenceExcludesOwnObservations` | 自世帯 Id の観測だけがある → **false** | `SellerId != selfHouseholdId` を省く(純粋な自己ループが成立する) |  |
| 17 | `MarketReferenceExcludesOtherItems` | 別品目の観測だけがある → **false** | `ItemId` で絞らない |  |
| 18 | `OwnPreviousPriceIsAddedOnlyWhenOtherSellersAreObserved` | 他1件(200)+自前日(100) → **150**。他0件+自前日(100) → **false** | 他0件でも自分だけで立てる(GDD06 §3.1 が「旧版の懸念の有効な部分」と呼んだ純粋な自己ループ) | **核心** |
| 19 | `AbsentOwnPreviousPriceIsNotCountedAsZero` | 他1件(200)・自前日なし → **200**(件数1) | 0 を足して件数を2にする(100)。**GDD02 §8.1.1 が名指しした誤り** | **核心** |
| 20 | `MarketReferenceKeepsTheLaterEntryOnATie` | 同一 tick・同一売り手の2件(100 → 200 の順で追加)→ **200** | `>=` を `>` と書く(先勝ちになり `List` の並びが結果を変える) |  |
| 21 | `FirstDayOffersAreExactlyTheCostFloor` | M0 の定義でパイプライン(順1・順2・順5)を1日進める → **全売り手の `Market` の値が `CostFloor(UnitCost(...))` と一致**。期待値は定義から計算する | 初日に相場基準を立てる(`Knowledge` が空なのに 0 を平均に混ぜる)。`InitialAcquisitionCost` を読まない。**issue の閉じる条件そのもの** | **核心** |
| 22 | `OfferReadsYesterdaysOwnPriceNotAClearedMarket` | 1日目で価格が付いた後、世帯主の `Knowledge` に他の売り手の観測を1件仕込んで2日目を進める → 相場基準が **CeilDiv(観測 + 自分の前日価格, 2)** になっている | **先に `Market.Clear()` してから計算する**(全売り手が自分の前日価格を失い、相場基準が観測のみになる)。TDD01 §3.2 が要求した一括書き込みが崩れる形 | **核心** |
| 23 | `SellableStockIsOnlyTheWorkshopOutputInventory` | 同じ品目を**世帯在庫**に積んでも提示価格が変わらない。パン屋の工房在庫にある**薪**(入力)に売り注文が立たない | 世帯在庫を足す。工房在庫の全品目に売り注文を立てる(入力まで売りに出る) | **核心** |
| 24 | `NoOfferIsPostedWhenSellableStockIsZero` | 出力在庫0(入力切れで生産停止)の売り手の `MarketKey` が `Market` に**無い** | 在庫0でも出品する(在庫比0 → 係数1500‰ の「在庫のない店」が #37 の候補に載る) |  |
| 25 | `StaleOfferIsRemovedWhenStockRunsOut` | 前日出品した売り手の在庫を0にして1日進める → エントリが**消える** | 上書きするだけで削除しない(職業付け替え後の古い品目の売り注文も残る) |  |
| 26 | `MarketReferenceComesFromTheHeadNpcOnly` | **徒弟**の `Knowledge` にだけ観測を置く → 相場基準が立たない(原価下限のまま)。同じ観測を**世帯主**に置くと立つ | 構成員全員から集める。`HeadNpcId` ではなく世帯 Id で `Knowledge` を引く(TDD01 §3.2 が「取り違えを型で防げない」と書いた経路) | **核心** |
| 27 | `BankruptSellerPostsTheHalvedFloorInThePipeline` | `IsBankrupt = 1` を直接立てて1日進める → 提示価格の下限が 500‰ になっている | `HouseholdState.IsBankrupt` を読まずに定数で値付けする |  |
| 28 | `TradeSystemRejectsUnsupportedRecipesAtConstruction` | 入力0件 / 出力2件を含む定義で**コンストラクタ**が `NotSupportedException` | 値付けの最中に落とす(販売在庫が0の日だけ生き延び、失敗が在庫に依存して再現しない) |  |
| 29 | `TradeDrawsNoRandomNumbers` | マスターシードだけを変えた2つの `RandomSource` で同じ世界を1日進め、**状態ハッシュが一致する** | `context.OpenRandom` を使って価格を揺らす |  |
| 30 | `WorldDefinitionRejectsMalformedShipmentTargets` | 行数 ≠ 職業数 / 行の長さ ≠ `itemCount` / **出力品目の欄が0** / **出力品目以外の欄が非0** / 保持期間0 / 負の利幅 を、それぞれ拒む | 出力品目の欄の0を通す(在庫比‰ がゼロ除算)。表の形を検査しない |  |
| 31 | `WorldDefinitionCopiesEachShipmentTargetRow` | 渡した jagged 配列の**行の中身**を後から書き換えても定義が変わらない | 外側だけ `ToArray()` する |  |

**「核心」印(#3・#6・#8・#11・#12・#14・#18・#19・#21・#22・#23・#26)には実際に変異を当てて落ちることを確認し、当てた変異と結果を doc コメントかコミット本文に残す**([process/02](../process/02-task-spec.md))。

> **核心が12件と多いのは、本タスクの式が「間違っても数字が出る」からである。** 生産(W2-03)は在庫が負に落ちるなどの目に見える破れがあったが、価格は**どの変異でも「それらしい値」が `Market` に並ぶ**。[ADR-0008](../adr/0008-review-scope-narrowed-to-unnoticeable-defects.md) の象限I-a(緑のまま壊れる)がそのまま当たる領域であり、判別力はテストの値の選び方にしか無い。

## 申し送り

### #28(値の検算と調整)へ

- **`最低利幅‰ = 200` と出荷目標在庫(出力の3日分)はここが初出の初期値である。** [GDD02 §13.2](../03-gdd/02-economy.md) の調整対象表に既に行がある(§8.1「最低利幅‰」/ §8.1.1「出荷目標在庫」)
- **[GDD02 §10.2](../03-gdd/02-economy.md) の成立条件 `原価 < 外部買値 < 原価下限` は、利幅200‰ のとき原価の 1.0〜1.2 倍という 20% の帯になる。** 外部価格を置く #38 と併せて検算が要る。帯が狭すぎれば利幅を上げる
- **`BuildM0` の初期値での原価下限**(検算の出発点。`InitialAcquisitionCost` = `{10, 8, 14, 12, 22, 6, 16, 30, 60}` から):

  | 職業 | 原価 | 原価下限(× 1200‰) |
  | ---- | ---- | ------------------- |
  | Miller(小麦粉) | 20 | 24 |
  | Baker(パン) | 14 | 17 |
  | Brewer(ビール) | 26 | 32 |
  | Woodworker(薪) | 3 | 4 |
  | Smith(工具) | 40 | 48 |

- **`InitialAcquisitionCost` の都市生産品の値(小麦粉22・薪6・パン16・ビール30・工具60)と、上の原価下限が噛み合っていない。** 例えばパンの取得原価16に対して原価下限は17で、**初日から買い手の移動平均が売り手の下限を下回る**。取得原価は「初期在庫を持っている世帯の仕入値」、原価下限は「作った側が並べる値」なので**一致する必要は無い**が、乖離の向きと大きさは §12-1(価格の安定)に効く

### #37(Trade システムと店の選択)へ

- **`Market` を書くのは `TradeSystem` の値付けの段だけである。** 約定は在庫・資金・帳簿を動かすが `Market` に触らない([GDD02 §6.3](../03-gdd/02-economy.md))。**在庫が日中に尽きた売り手のエントリはその日のあいだ残る** — 買える数量の判定は買い手側で行うこと
- **鍛冶の販売在庫には、自分が稼働に使っている工具が含まれる。** [GDD02 §8.1.1](../03-gdd/02-economy.md) が「出力を手元に残す規則を M0 では置かない」と決めている。**鍛冶が工具を売り切ると翌日から設備係数0‰ で自分の生産が止まる** — [GDD02 §5.3](../03-gdd/02-economy.md) が「工具を買えない鍛冶が工具の供給を止める」と書いた自己参照の経路であり、**先回りして塞がない**(§12-3 が観測すべき失敗モード)
- **`PurchaseUnitCostAverage` の更新(仕入れ移動平均)は #37 が書く。** 窓付きにするか再帰形にするかも #37 の判断。[GDD02 §13.2](../03-gdd/02-economy.md) は「原価の移動平均の窓」を調整対象に挙げている

### #36(予算・購入量と観測)へ

- **相場基準の作り方は売り手の値付けと買い手の予算で同じ規約である**が、**買い手側には自分の前日の提示価格を混ぜない**([GDD02 §8.1.1](../03-gdd/02-economy.md))。`OfferPrice.TryMarketReference` の `hasOwnPreviousPrice` に `false` を渡せば同じ関数を使える
- **観測の生成側で「自分の売り注文を自分の観測に入れない」を守ること**([GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md))。値付け側でも `SellerId != selfHouseholdId` で防御しているが、**買い手の予算には自世帯の除外を入れていない**(自分の売り注文を自分で観測しない限り不要)

### #39(Household システム)へ

- **破産中フラグは順3 が前日の購入結果から立てる。** 本タスクは読むだけ。②(投げ売り)は**値付けの分岐として既に入っている**ので、順3 で実装しないこと([GDD02 §6.3](../03-gdd/02-economy.md))
- **④のゲートが見る「販売在庫」は工房在庫の出力品目である**(本タスクの `sellableStock` と同じ切り口)

## 編集してよい文書

worktree をまたいだ所有権の宣言。**ここに挙がっていない文書は触らない。**

- なし(コードとテストのみ)

**GDD02 §8.1・§8.1.1・§6.3・§8.2.7 と GDD06 §3.1 は、本タスクが必要とする決定をすべて持っている。** 実装が仕様と食い違ったら、**直すのはコードであって文書ではない**。文書側を直す必要があると判断したら、それは象限I-b(仕様そのものの欠陥)なので[止まって報告する](../process/02-task-spec.md)。

## このタスクで特に効く規約

[ADR-0002](../adr/0002-time-model-and-determinism.md) の決定論規約のうち、このタスクで踏みやすいものだけを挙げる。**機械で捕まるものは書かない**(浮動小数点と列挙順が保証されないコレクションは `BannedSymbols.txt` と `DeterminismConventionTests` が止める)。

- **丸めの向きが式ごとに違う。** 在庫比‰・原価・相場基準の平均は**切り上げ**(`CeilDiv`)、価格係数は**式全体として切り下げ**([GDD02 §8.1.1](../03-gdd/02-economy.md) の註が意図的と明記)。裸の `/` を書かない
- **‰ を掛けるのは `ApplyPermille` を通す。** `× permille / 1000` を手で書かない(桁あふれと演算順序の取り違えを同時に塞ぐヘルパーである)
- **中間の積と合計は `long` で持つ。** `1000 × 販売在庫`、`Σ(単価 × 数量)`、観測の合計
- **係数の定数には単位のコメントを付ける**(`= 200; // ‰`、`= 7; // 日`)
- **走査順は世帯Id 昇順 → 観測は SellerId 昇順。** `Households` は添字 = Id なので先頭から使う。観測の畳み込みは `SortedDictionary<int, PriceObservation>`
- **`Market` への書き込みは1か所・1回。** 読み終える前に書かない

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **「核心」印のテストに変異を当てて落ちることを確認し、当てた変異と結果を残した**
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
