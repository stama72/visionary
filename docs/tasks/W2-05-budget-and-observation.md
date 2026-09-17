# W2-05: 予算・購入量と観測(Knowledge)

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#36](https://github.com/stama72/visionary/issues/36)                |
| 根拠     | [GDD02 §8.2〜§8.2.7・§6.1・§5.3](../03-gdd/02-economy.md) / [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) / [GDD08 §8.1・§9](../03-gdd/08-household-and-decision.md) / [TDD01 §3.2・§3.3](../04-tdd/01-sim-core-and-m0.md) |
| ブランチ | `feat/36-budget-and-observation`                                     |
| worktree | `visionary/`(本体)                                                  |

> **この文書は使い捨ての作業指示である。** 実装完了時点で凍結し、以後の正はコードと GDD/TDD。

## スコープ

**買い手が「いくらまでなら払うか・何個要るか」を決める式と、その入力になる観測の生まれ方である。** [#35](https://github.com/stama72/visionary/issues/35) が売り手の値付けを作ったので、同じ `相場基準` の上に買い手側が乗る。

**2つの成果物があり、依存の向きが逆である**ことに注意する:

| 成果物 | 誰が呼ぶか |
| ------ | ---------- |
| 予算・購入量(`BuyerBudget` / `BuyerDemand`) | **本タスクでは誰も呼ばない。** 店の選択と約定([#37](https://github.com/stama72/visionary/issues/37))が呼ぶ |
| 観測の生成・世帯共有・失効(`Observations`) | **本タスクが `TradeSystem` に繋ぐ。** 値付けの段の後ろに足す |

**予算の式をテストからしか呼ばないのは意図どおりである。** 買うには店の選択(#37)が要り、それを本タスクへ引き込むと1タスクで順5 を丸ごと作ることになる。issue の閉じる条件「4つの基礎値がすべて実行され」は、**「落ちるべき条件」の表が4用途すべてを通すこと**で満たす。

**含まない:**

- **店の選択・実質コスト・約定・帳簿記帳・仕入れ移動平均の更新**(#37)。本タスクは `LiquidFunds` も `Ledgers` も `PurchaseUnitCostAverage` も**読むだけ**で書かない
- **都市外市場の候補合成**([#38](https://github.com/stama72/visionary/issues/38))。`Market` に窓口のエントリが無い M0 では、観測の生成は都市の売り手しか見ない
- **Need の生成**([#40](https://github.com/stama72/visionary/issues/40))。[GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md)「それでも0件のとき」の `在庫不足 / 遠方在庫` は #40
- **信用による割引**(効果1、W4)と**移動の機会費用**(順0 `OpportunityCost` は未実装)
- **値の作り込み**([#28](https://github.com/stama72/visionary/issues/28))。置き場所と型と丸めの向きは本タスクで決める
- **`vsim` / Runner の追随。** [`SyntheticLoadSystem`](../../src/Visionary.Sim.Runner/Determinism/SyntheticLoadSystem.cs) のままにする(W2-03・W2-04 と同じ理由 — #41 まで合成負荷が `StateHasher` の全区分を CI の2プロセス比較に踏ませている)
- **`World` の区分追加。** `Knowledge` は #32 で既にあり `StateHasher` も持っている([TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md))ので、**ハッシュ側の変更は無い**

## 設計の前提(フェーズ1で決めたこと)

### GDD02 に3点を確定させた(本ブランチの先行コミット)

実装の前に [GDD02](../03-gdd/02-economy.md) を直してある。**実装はこの3点を仕様として読む**こと。

| どこ | 何を決めたか |
| ---- | ------------ |
| §8.2 | **買い手の `相場基準` は世帯主(親方)の観測から作る。** 買いに行く者が誰かで予算が変わると、[GDD06 §3](../03-gdd/06-trade-and-negotiation.md) の委託先の選択が予算を通して価格へ跳ね返る。**ただし買い手側は自分の前日の提示価格を混ぜない** |
| §8.2.1「M0 での目標在庫の持ち方」 | **生産の入力は職業 × 品目の定数表**(実行回数から毎日導くと入力切れの日に目標在庫が 0 になり恒久停止する)。**耐久は耐久値で数え、購入量も耐久値で解いてから `CeilDiv(…, N)` で個数に直す**。**階層係数は世帯主のものを採る** |
| §8.2.2 | **M0 では `予想在庫 = 現在庫` になる。** 順5 は順1 生産・順2 消費の**後**なので、買い物の時点で「これから予定されている消費・生産投入」が残っていない。引き算の位置は式に残すが、M0 で引く量は 0 |

### 消費量の計算は1か所にしか置かない

**[GDD02 §8.2.1](../03-gdd/02-economy.md) の目標在庫(必需・嗜好)は「今後 N 日に消費する予定の量の合計」であり、その1日ぶんは順2 が実際に引く量と同じ式である。** 書き分けると、**目標在庫と実消費が静かにずれる** — 構成員ごとの切り上げを片方だけ忘れても、どちらのテストも単独では緑のままになる。

**したがって `ConsumptionSystem` の私有メソッド `RequiredQuantityFor` を `DailyConsumption` へ切り出し、`ConsumptionSystem` はそれを呼ぶ形に直す。** 複製ではなく移動である。

### 視界半径 R は `WorldDefinition` に置かない

**`District` の `const` にする。** [GDD02 §13.2](../03-gdd/02-economy.md) が「**視界半径 R は値ではなく構造であり、この表には載せない**」と明示し、[GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) が「W2 の実データで調整する値として扱ってはならない」と書いている。`WorldDefinition` に入れると #28 の調整対象の見た目になる。

### 派生需要が読む「前日の提示価格」は呼び出し側が渡す

**`TradeSystem.Step` の値付けの段は `Market.Clear()` してから当日の価格を書く。** したがって**同じ `Step` の後段で `Market` を読むと「前日」ではなく「当日」が返る。** [GDD02 §8.2.1](../03-gdd/02-economy.md) の派生需要は `提示価格_出力[前日]` を要求しているので、**`BuyerDemand.Build` は前日の値を引数で受け取る**形にする。#37 が値付けの段で `Clear()` の前に控える。

**本タスクは控える側を書かない**(買い物の段が無いため)。**代わりに引数の doc コメントで契約を固定する。**

## 作るもの

名前空間は `Visionary.Sim`(`Definition/`)と `Visionary.Sim.Systems`(`Systems/`)。

### 1. `DemandPurpose`(`Definition/DemandPurpose.cs` を新規作成)

```csharp
/// <summary>需要の用途(GDD02 §8.2.1)。値は §8.2.1 の表の順。</summary>
public enum DemandPurpose
{
    /// <summary>生産の入力。基礎値は派生需要(GDD02 §8.2.1)。</summary>
    ProductionInput = 0,

    /// <summary>必需の消費。基礎値は 相場基準 × 許容乖離‰。</summary>
    Necessity = 1,

    /// <summary>嗜好・奢侈の消費。基礎値は 余剰資金 × 予算比率‰。</summary>
    Preference = 2,

    /// <summary>耐久の消費。基礎値は min(相場基準, 流動資金 × 予算比率‰)。</summary>
    Durable = 3,
}
```

- **`enum` にするのは `Occupation` と同じ理由である**(`Item` の doc の「添字か、値か」)。予算比率表の添字にも使うが、主には `DemandLine` の欄として比較される値である
- **値の順は [GDD02 §8.2.1](../03-gdd/02-economy.md) の表の順であって、[GDD02 §6.2.1](../03-gdd/02-economy.md) の走査順ではない。** 走査順(必需 → 生産の入力 → 耐久 → 嗜好)は `BuyerDemand.Build` が**並び**として持つ。**`enum` の値に走査順を兼ねさせない** — 兼ねさせると、後から用途を1つ足したときに走査順が黙って変わる

### 2. `WorldDefinition` の追加欄(`Definition/WorldDefinition.cs`)

**7つの欄をコンストラクタ引数として足す。既存引数の後ろに並べること。**

```csharp
/// <summary>
/// 生産の入力の目標在庫。添字 = [(int)Occupation][itemId]。単位: 個(GDD02 §8.2.1)。
/// </summary>
public int[][] InputTargetStockByOccupation { get; }

/// <summary>必需の目標在庫の日数。添字 = itemId。単位: 日(GDD02 §8.2.1)。0 = 必需ではない。</summary>
public int[] NecessityTargetStockDays { get; }

/// <summary>嗜好・奢侈の目標在庫の日数。添字 = itemId。単位: 日(同)。0 = 嗜好ではない。</summary>
public int[] PreferenceTargetStockDays { get; }

/// <summary>工具の目標在庫‰。1個あたりの耐久値に対する比率(GDD02 §8.2.1)。単位: ‰。</summary>
public int ToolTargetStockPermille { get; }

/// <summary>階層係数‰。添字 = (int)NpcRank。長さ3(GDD08 §9)。単位: ‰。</summary>
public int[] RankCoefficientPermille { get; }

/// <summary>必需の許容乖離‰。基礎値 = ApplyPermille(相場基準, これ)(GDD02 §8.2.1)。単位: ‰。</summary>
public int NecessityTolerancePermille { get; }

/// <summary>
/// 用途別の予算比率‰。添字 = (int)DemandPurpose。長さ4(GDD02 §8.2.1・§8.2.7)。単位: ‰。
/// </summary>
public int[] BudgetRatioPermilleByPurpose { get; }
```

**コンストラクタの検証**(既存欄と同じ書き方で):

| 欄 | 拒むもの | 例外 |
| -- | -------- | ---- |
| `InputTargetStockByOccupation` | `null` / 行数 ≠ 職業数 / 行が `null` / 行の長さ ≠ `itemCount` / **レシピの入力品目の欄が0以下** / **入力品目以外の欄が0でない** | `ArgumentNullException` / `ArgumentException` / `ArgumentOutOfRangeException` |
| `NecessityTargetStockDays` / `PreferenceTargetStockDays` | `null` / 長さ ≠ `itemCount` / 負 / **同じ品目が両方で正** / **`Item.Tools` の欄が正** | 同上 |
| `ToolTargetStockPermille` | 1未満 | `ArgumentOutOfRangeException` |
| `RankCoefficientPermille` | `null` / 長さ ≠ 3 / 1未満 | 同上 |
| `NecessityTolerancePermille` | 1未満 | 同上 |
| `BudgetRatioPermilleByPurpose` | `null` / 長さ ≠ 4 / **`ProductionInput` の欄が0でない** / `Necessity` が1未満 / `Durable` が1未満 / `Preference` が負 | 同上 |

- **入力品目の欄の0を拒むのは、0 が「この入力は要らない」を意味してしまうからである。** [GDD02 §8.2.1](../03-gdd/02-economy.md) が「入力切れの世帯が『もう要らない』と判定されて恒久停止する」と名指しした状態が、定義の側から入る
- **入力品目以外を0に強制するのは `ShipmentTargetStockByOccupation` と同じ理由である**(45欄のうちどれが効くかを表から読めるようにする)
- **必需と嗜好で同じ品目が正であることを拒むのは、[GDD02 §8.2.1](../03-gdd/02-economy.md) の表が用途を排他に定めているからである。** 両方に置くと同じ世帯在庫に対して2本の行が立ち、**先に走査したほうが買った量を後の行が見ない**(`予想在庫` は #37 が約定するまで動かない)ので、目標在庫の2倍まで買う
- **`Item.Tools` を必需・嗜好に置けないのは、耐久だけ単位が違うからである**(耐久値 vs 個)。同じ品目に単位の違う2本の行が立つ
- **`ProductionInput` の予算比率を0に強制するのは、生産の入力が母数を持たないからである**([GDD02 §8.2.1](../03-gdd/02-economy.md) 派生需要)。**観測ゼロ時のフォールバックは `Necessity` の比率‰ を流用する**と §8.2.1 が明記しているので、専用の欄を作ると「使われない調整軸」が表に住み着く
- **`Necessity` と `Durable` の比率が1以上なのは、詰みを作らないためである**([GDD02 §4.2](../03-gdd/02-economy.md))。必需が0だとパンも薪も永久に買えず、耐久が0だと工具が買えず設備係数が0‰ に落ちて恒久停止する。**`Preference` の0は許す** — 嗜好を切っても詰まない
- **`RankCoefficientPermille` が1以上なのは同じ理由である**(0 だと、その階層が世帯主の世帯の工具の目標在庫が0になる)
- **jagged 配列は行ごとに複製して持つ**(既存欄と同じ)

**`BuildM0` の初期値**(すべて調整対象。[GDD02 §13.2](../03-gdd/02-economy.md)):

```csharp
// 生産の入力の目標在庫。添字 = [(int)Occupation][itemId]。単位: 個(GDD02 §8.2.1)。
// 初期値は「必要数量 × 仕入れ間隔5日」— 生産能力が 1実行/日 に張り付いている(#28 への
// 申し送り、W2-03)ので 1日分の使用量 = 必要数量。入力品目以外は0(コンストラクタが強制する)。
const int InputTargetStockDaysForM0 = 5; // 日。仕入れ間隔(GDD02 §8.2.1)

var inputTargetStockByOccupation = new[]
{
    // Miller: 穀物2/日 × 5
    NewInputRow((Item.Grain, 2 * InputTargetStockDaysForM0)),
    // Baker: 小麦粉1/日 × 5、薪1/日 × 5
    NewInputRow((Item.Flour, 1 * InputTargetStockDaysForM0),
                (Item.Firewood, 1 * InputTargetStockDaysForM0)),
    // Brewer: 穀物2/日 × 5、薪1/日 × 5
    NewInputRow((Item.Grain, 2 * InputTargetStockDaysForM0),
                (Item.Firewood, 1 * InputTargetStockDaysForM0)),
    // Woodworker: 木材1/日 × 5
    NewInputRow((Item.Timber, 1 * InputTargetStockDaysForM0)),
    // Smith: 鉄鉱石2/日 × 5、木炭1/日 × 5
    NewInputRow((Item.IronOre, 2 * InputTargetStockDaysForM0),
                (Item.Charcoal, 1 * InputTargetStockDaysForM0)),
};

// 必需・嗜好の目標在庫の日数。添字 = itemId。単位: 日(GDD02 §8.2.1 の表そのもの)。
var necessityTargetStockDays = new int[Item.Count];
necessityTargetStockDays[Item.Firewood] = 7; // 薪: 1週間分(1日消費量は季節変動、GDD02 §9)
necessityTargetStockDays[Item.Bread] = 3;    // パン: 3日分

var preferenceTargetStockDays = new int[Item.Count];
preferenceTargetStockDays[Item.Beer] = 1;    // ビール: 1日分

const int ToolTargetStockPermilleForM0 = 500; // ‰。1個あたりの耐久値(= N)に対する比率

// 階層係数‰。添字 = (int)NpcRank(Master, Journeyman, Apprentice)。GDD08 §9 の表。
// Journeyman は M0 に存在しない(GDD10)が、階層の欄は先に埋める。
var rankCoefficientPermille = new[] { 1000, 600, 200 }; // 単位: ‰

const int NecessityTolerancePermilleForM0 = 1200; // ‰。相場の1.2倍までは追随する

// 用途別の予算比率‰。添字 = (int)DemandPurpose。GDD02 §8.2.1・§8.2.7。
// ProductionInput が0なのは母数を持たないからである(派生需要。コンストラクタが強制する)。
var budgetRatioPermilleByPurpose = new[]
{
    0,   // ProductionInput: 母数なし
    50,  // Necessity:  流動資金の 5%(GDD02 §8.2.7)
    200, // Preference: 余剰資金の 20%
    10,  // Durable:    流動資金の 1%(GDD02 §8.2.1)
};
```

`NewInputRow` は `BuildM0` の中の `private static` ヘルパー(長さ `Item.Count` の配列を作り、渡された欄だけ埋める)。**行を手書きで9個並べない**(`NewShipmentRow` と同じ)。

### 3. `District` の追加(`Definition/District.cs`)

```csharp
/// <summary>視界半径 R(GDD06 §3.1)。単位: 区画。</summary>
public const int VisionRadius = 1;
```

**`WorldDefinition` に置かない。** [GDD02 §13.2](../03-gdd/02-economy.md) が「視界半径 R は**値ではなく構造**であり、この表には載せない」と明記している。doc コメントにその理由(R=0 なら誰も他区画の店を知れず、R=2 なら中心の世帯が最初から全区画を見通す)を1行で書く。

### 4. `DailyConsumption`(`Systems/DailyConsumption.cs` を新規作成)

**`static` クラス。`ConsumptionSystem` の私有メソッドを切り出したものである(複製ではない)。**

```csharp
/// <summary>世帯の1日消費量(GDD02 §6.1・§9)。</summary>
public static int Quantity(
    WorldDefinition definition, World world, HouseholdState household, int itemId, Season season);

/// <summary>
/// 今日から <paramref name="days"/> 日ぶんの消費量の合計(GDD02 §8.2.1「N日分は先読みで数える」)。
/// </summary>
public static int Lookahead(
    WorldDefinition definition, World world, HouseholdState household, int itemId, Tick now, int days);
```

```
Quantity  = Σ_構成員 ( 薪なら ApplyPermille(基礎量[階層][itemId], 季節係数‰[season]) , それ以外は 基礎量 )
            構成員ごとに切り上げてから合計する(GDD02 §6.1。世帯合計に先に掛けると奇数でずれる)

Lookahead = Σ_{k=0..days-1} Quantity(..., GameDate.FromTick(Tick.FromDays(now.DayIndex + k)).Season)
            days <= 0 なら 0
```

- **`ConsumptionSystem.RequiredQuantityFor` を消し、`DailyConsumption.Quantity` を呼ぶ形に直す。** 既存の `ConsumptionSystem` のテストは1行も書き換えずに緑のままであること
- **日ごとに季節係数を適用して切り上げてから足す。** 合計してから切り上げない([GDD02 §8.2.1](../03-gdd/02-economy.md))
- **`Tick.FromDays(now.DayIndex + k)` で数える。** `now` の時刻(`HourOfDay`)は捨てる — 「今日」は日単位の概念である

### 5. `BuyerBudget`(`Systems/BuyerBudget.cs` を新規作成)

**`static` クラス。全メソッドが純関数で、`World` も `WorldDefinition` も受け取らない**(`OfferPrice` と同じ切り出し方)。

```csharp
/// <summary>買い手の在庫圧力‰(GDD02 §8.2.2)。上限倍率は2倍固定。</summary>
public static int StockPressurePermille(int expectedStock, int targetStock);

/// <summary>予算 = ApplyPermille(基礎値, 在庫圧力‰)(GDD02 §8.2)。</summary>
public static int Budget(int baseValue, int stockPressurePermille);

/// <summary>購入量の線形解(GDD02 §8.2.3)。</summary>
public static int PurchaseQuantity(int baseValue, int unitRealCost, int targetStock, int expectedStock);

/// <summary>余剰資金 = max(0, 流動資金 − 必要運転資金)(GDD02 §8.2.1)。</summary>
public static int SurplusFunds(int liquidFunds, long workingCapital);

/// <summary>必需の基礎値(GDD02 §8.2.1 / §8.2.7)。</summary>
public static int NecessityBaseValue(
    bool hasReference, int marketReference, int liquidFunds,
    int tolerancePermille, int fallbackRatioPermille);

/// <summary>嗜好・奢侈の基礎値(GDD02 §8.2.1)。母数は余剰資金である。</summary>
public static int PreferenceBaseValue(int surplusFunds, int ratioPermille);

/// <summary>耐久の基礎値(GDD02 §8.2.1 / §8.2.7)。母数は流動資金である。</summary>
public static int DurableBaseValue(
    bool hasReference, int marketReference, int liquidFunds, int ratioPermille);

/// <summary>派生需要(GDD02 §8.2.1「派生需要の算出」)。結果を baseValues へ書く。</summary>
public static void DerivedDemand(
    Recipe recipe,
    bool hasPreviousOutputOfferPrice,
    int previousOutputOfferPrice,
    int minimumMarginPermille,
    int liquidFunds,
    int necessityFallbackRatioPermille,
    bool[] hasInputReference,
    int[] inputMarketReference,
    int[] baseValues);
```

#### `StockPressurePermille`

```
予想在庫 <= 目標在庫              → 1000
目標在庫 < 予想在庫 <= 目標在庫×2 → CeilDiv( 1000 × (2 × 目標在庫 − 予想在庫) , 目標在庫 )
予想在庫 > 目標在庫×2             → 0
```

- **1000 は `IntegerMath.PermilleScale` を使う。** 裸の `1000` を書かない
- **`1000 × (…)` と `目標在庫 × 2` は `long` で持つ**(`CeilDiv(long, long)` を通す)
- **目標在庫0でゼロ除算しない形に書く。** `予想在庫 <= 0` は最初の枝、`予想在庫 > 0` は最後の枝に落ち、**中間の枝は目標在庫 ≥ 1 のときしか評価されない**。枝の順を入れ替えると壊れる
- **目標在庫まで 1000‰ で据え置くこと、上限が 1000‰ であることが要点である**([GDD02 §8.2.2](../03-gdd/02-economy.md))。1000‰ を超えると**赤字の原価で仕入れが発生する**

#### `PurchaseQuantity`

```
実質コスト <= 0             → ArgumentOutOfRangeException
実質コスト > 基礎値         → 0
それ以外:
    到達在庫 = 2 × 目標在庫 − CeilDiv( 目標在庫 × 実質コスト , 基礎値 )
    購入量   = max( 0 , 到達在庫 − 予想在庫 )
```

- **`実質コスト > 基礎値` を先に判定する。** 基礎値0のときここで必ず返るので、除算に到達しない。**順を入れ替えるとゼロ除算になる**
- **`実質コスト <= 0` は例外にする。** 0 を通すと基礎値0のときに上の保証が崩れる。**実質コスト = 提示価格 + 移動費であり、提示価格は原価下限以上なので1以上である**(#37)
- **`CeilDiv` は引かれる側に掛かるので、式全体としては切り下げになる。** [GDD02 §8.2.3](../03-gdd/02-economy.md) の検算「基礎値 × 1/2 → 目標在庫 × 1.5」は、目標3・基礎値100・実質コスト50 のとき **4**(4.5 の切り下げ)である。`FloorDiv` にすると 5 になる。`PriceCoefficientPermille` と同じ向きの丸めである
- **中間の積は `long`**
- **耐久の行では 目標在庫・予想在庫・購入量の単位が耐久値である。** 個数へ直すのは呼び出し側(#37)で、`CeilDiv(購入量, 1個あたりの耐久値)`([GDD02 §8.2.1](../03-gdd/02-economy.md))

#### `SurplusFunds` と基礎値の3つ

```
余剰資金       = (int) max( 0 , 流動資金 − 必要運転資金 )        必要運転資金は long
必需の基礎値   = 観測あり → ApplyPermille(相場基準, 許容乖離‰)
                 観測なし → ApplyPermille(流動資金, 必需の予算比率‰)
嗜好の基礎値   = ApplyPermille(余剰資金, 嗜好の予算比率‰)         観測の有無に依らない
耐久の基礎値   = 観測あり → min( 相場基準 , ApplyPermille(流動資金, 耐久の予算比率‰) )
                 観測なし → ApplyPermille(流動資金, 耐久の予算比率‰)
```

- **嗜好の母数は余剰資金、必需と耐久の母数は流動資金である**([GDD02 §8.2.1](../03-gdd/02-economy.md))。**取り違えると、嗜好側では黒字倒産が戻り、必需・耐久側では困窮した世帯が食料と工具を買えなくなる**
- **耐久は `min` を取ることが要点である。** `max` にすると「資金がなくても買おうとする」と「相場より高く買う」が同時に起きる

#### `DerivedDemand`

```
recipe.Outputs.Length != 1 → NotSupportedException(配分規則が GDD02 に無い。OfferPrice.UnitCost と同じ)
recipe.Inputs.Length == 0  → 何もしない(baseValues は空)

フォールバック基礎値 = ApplyPermille(流動資金, 必需の予算比率‰)   ← 全入力で同じ値

if (!hasPreviousOutputOfferPrice):
    全入力の baseValues[j] = フォールバック基礎値 → 終了(按分の式を評価しない)

見込み収益   = (long)提示価格_出力[前日] × recipe.Outputs[0].Quantity
許容原価合計 = FloorDiv( 見込み収益 × 1000 , 1000 + 最低利幅‰ )

残余 = 許容原価合計
W    = 0
for each 入力 j:
    if (hasInputReference[j])  W += (long)inputMarketReference[j] × 必要数量_j
    else { baseValues[j] = フォールバック基礎値;  残余 -= (long)フォールバック基礎値 × 必要数量_j }
残余 = max(0, 残余)

if (W == 0):
    観測のある入力が無い。全入力の baseValues[j] = フォールバック基礎値 → 終了

for each 入力 j with hasInputReference[j]:
    w             = (long)inputMarketReference[j] × 必要数量_j
    配分予算      = FloorDiv( 残余 × w , W )
    baseValues[j] = (int)FloorDiv( 配分予算 , 必要数量_j )
```

- **すべての除算を `FloorDiv` にする。** [GDD02 §8.2.1](../03-gdd/02-economy.md) が「`floor(A × w_j ÷ W)` を J 内で合計すると必ず `A` 以下になる」ことに立って `最低利幅‰` の保証を成立させている
- **途中で ‰ 表現の按分比を経由しない。** `ApplyPermille` は切り上げなので、**入力ごとに端数が切り上がって合計が `許容原価合計` を超える** — 実現利幅が最低利幅を下回る日が、観測が部分的に欠けた日にだけ生まれる。**善意で「‰ は `ApplyPermille` を通す」規約に揃えられる形なので、テスト #15 でここを見る**
- **フォールバック入力のぶんを先に差し引く。** 差し引かないと、観測できた入力だけが「本来2入力で分けるはずだった上限」を丸ごと受け取る
- **`残余` を `max(0, …)` で止める。** フォールバック仕入見込みが許容原価合計を超えうる(流動資金が大きい世帯)
- **`W == 0` はゼロ除算そのものである。** [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) の R=1 では「パン屋から見て水車小屋番も木材加工も距離2以上」が普通に起きるので、**初日に限らずいつでも通る経路**である
- **中間はすべて `long`**

### 6. `BuyerDemand`(`Systems/BuyerDemand.cs` を新規作成)

**`World` と `WorldDefinition` を読んで `BuyerBudget` を呼び、世帯の (用途, 品目) の組を全部作る。書き込みは一切しない。**

```csharp
/// <summary>1つの (用途, 品目) の組についての需要(GDD02 §8.2)。</summary>
public readonly record struct DemandLine
{
    public DemandPurpose Purpose { get; init; }

    public int ItemId { get; init; }

    /// <summary>基礎値。単位: 貨幣/1単位(耐久は1個あたり)。</summary>
    public int BaseValue { get; init; }

    /// <summary>目標在庫。単位: 個。<b>耐久だけ耐久値</b>(GDD02 §8.2.1)。</summary>
    public int TargetStock { get; init; }

    /// <summary>予想在庫。単位は <see cref="TargetStock"/> と揃う(GDD02 §8.2.2)。</summary>
    public int ExpectedStock { get; init; }

    public int StockPressurePermille { get; init; }

    /// <summary>予算 = ApplyPermille(基礎値, 在庫圧力‰)(GDD02 §8.2)。</summary>
    public int Budget { get; init; }
}

/// <summary>世帯1戸ぶんの需要(GDD02 §8.2)。</summary>
public readonly record struct HouseholdDemand
{
    /// <summary>GDD02 §6.2.1 の走査順に並んだ組。</summary>
    public IReadOnlyList<DemandLine> Lines { get; init; }

    /// <summary>必要運転資金(GDD02 §8.2.1)。単位: 貨幣。</summary>
    public long WorkingCapital { get; init; }

    /// <summary>余剰資金。単位: 貨幣。</summary>
    public int SurplusFunds { get; init; }
}

public sealed class BuyerDemand
{
    public BuyerDemand(WorldDefinition definition);

    /// <summary>世帯の (用途, 品目) の組をすべて作る。</summary>
    /// <param name="hasPreviousOutputOfferPrice">
    /// <b>前日の</b>自世帯の出力品目の提示価格があるか。<b>呼び出し側が
    /// <see cref="World.Market"/> を <c>Clear()</c> する前に控えた値を渡すこと</b>
    /// (GDD02 §8.2.1。当日の値を渡すと同一tick内で循環する)。
    /// </param>
    public HouseholdDemand Build(
        World world,
        HouseholdState household,
        bool hasPreviousOutputOfferPrice,
        int previousOutputOfferPrice);
}
```

**`Build` の手順:**

```
recipe = definition.Recipes[(int)household.Occupation]
head   = world.Knowledge[household.HeadNpcId]

0. 相場基準を品目ごとに1回だけ引く(薪は必需と生産の入力の両方に現れる)
   引くのは { 生産の入力の品目 } ∪ { 必需の品目 } ∪ { Item.Tools } だけでよい
       (嗜好の基礎値は相場基準を使わない。必要運転資金も嗜好・耐久を走査しない)
   hasReference[itemId] / reference[itemId] を itemCount ぶん用意し、対象の品目について:
       OfferPrice.TryMarketReference(
           head, itemId, household.Id, world.Now,
           definition.ObservationRetentionDays,
           hasOwnPreviousPrice: false, ownPreviousPrice: 0, out reference[itemId])

1. 目標在庫
   必需 i (NecessityTargetStockDays[i] > 0):
       target = DailyConsumption.Lookahead(definition, world, household, i, world.Now,
                                           definition.NecessityTargetStockDays[i])
   嗜好 i (PreferenceTargetStockDays[i] > 0):
       target = DailyConsumption.Lookahead(..., definition.PreferenceTargetStockDays[i])
   生産の入力 j (recipe.Inputs):
       target = definition.InputTargetStockByOccupation[(int)household.Occupation][j.ItemId]
   耐久 (Item.Tools、常に1行):
       target = ApplyPermille(
                    ApplyPermille(definition.ProductionRunsPerToolWear,
                                  definition.ToolTargetStockPermille),
                    definition.RankCoefficientPermille[(int)world.Npcs[household.HeadNpcId].Rank])

2. 必要運転資金(GDD02 §8.2.1)。走査するのは (生産の入力, 品目) と (必需, 品目) だけ
   workingCapital = 0L
   それらの組について hasReference なら workingCapital += (long)target × reference[itemId]
   surplus = BuyerBudget.SurplusFunds(household.LiquidFunds, workingCapital)

3. 基礎値
   生産の入力: BuyerBudget.DerivedDemand(recipe, hasPreviousOutputOfferPrice,
                   previousOutputOfferPrice, definition.MinimumMarginPermille,
                   household.LiquidFunds,
                   definition.BudgetRatioPermilleByPurpose[(int)DemandPurpose.Necessity],
                   hasInputReference, inputMarketReference, baseValues)
   必需:      BuyerBudget.NecessityBaseValue(hasReference[i], reference[i], household.LiquidFunds,
                   definition.NecessityTolerancePermille,
                   definition.BudgetRatioPermilleByPurpose[(int)DemandPurpose.Necessity])
   嗜好:      BuyerBudget.PreferenceBaseValue(surplus,
                   definition.BudgetRatioPermilleByPurpose[(int)DemandPurpose.Preference])
   耐久:      BuyerBudget.DurableBaseValue(hasReference[Item.Tools], reference[Item.Tools],
                   household.LiquidFunds,
                   definition.BudgetRatioPermilleByPurpose[(int)DemandPurpose.Durable])

4. 予想在庫(M0 は現在庫。GDD02 §8.2.2)
   必需・嗜好: household.HouseholdInventory[itemId]
   生産の入力: household.WorkshopInventory[itemId]
   耐久:      household.WorkshopInventory[Item.Tools] × definition.ProductionRunsPerToolWear
                  − household.ToolWearCount

5. 在庫圧力‰ と 予算 を各行で求める

6. 並べる(GDD02 §6.2.1 の走査順)
   必需(品目 Id 昇順) → 生産の入力(品目 Id 昇順) → 耐久 → 嗜好(品目 Id 昇順)
```

- **`相場基準` は世帯主の観測から作り、`hasOwnPreviousPrice` には `false` を渡す**([GDD02 §8.2](../03-gdd/02-economy.md)・[GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md))。**`OfferPrice.TryMarketReference` をそのまま使う** — 相場基準の作り方は売り手と買い手で同じ規約である([GDD02 §8.1.1](../03-gdd/02-economy.md))
- **`selfHouseholdId` には `household.Id` を渡す。** `HeadNpcId` ではない([TDD01 §3.2](../04-tdd/01-sim-core-and-m0.md) が「取り違えを型で防げない」と書いた経路。W2-04 のテスト R1 が同じ間違いを見ている)
- **生産の入力の行は品目 Id 昇順に並べる。** `recipe.Inputs` の並びに依存しない — `Recipe` は入力が昇順であることを保証していない
- **薪は2行になる。** (必需, 世帯在庫, 先読み7日ぶん) と (生産の入力, 工房在庫, 定数表)。**品目単位に畳まない**([GDD02 §8.2.1](../03-gdd/02-economy.md)「生産用の薪が運転資金から丸ごと落ちて、黒字倒産が生産世帯でだけ戻る」)
- **耐久の行は全世帯に立つ**(鍛冶を含む)。鍛冶は工具の売り手でもあるが、`Item.Tools` は自分の入力ではないので生産の入力の行にはならない
- **木材加工は自分が作る薪を必需として買う行を持つ。** 工房在庫と世帯在庫は別勘定であり、[GDD02 §8.1.1](../03-gdd/02-economy.md) は「出力を手元に残す規則を M0 では置かない」と決めている。**塞がない**
- **`Build` は `World` を一切書き換えない。** `Knowledge` も `Market` も読むだけである

### 7. `Observations`(`Systems/Observations.cs` を新規作成)

```csharp
/// <summary>保持期間を過ぎた観測を全 NPC の Knowledge から取り除く(GDD06 §3.1)。</summary>
public static void Expire(World world, int retentionDays);

/// <summary>
/// その日に居た区画から距離 R 以内の売り注文を観測し、世帯全員で共有する
/// (GDD06 §3.1 / GDD08 §8.1)。
/// </summary>
public static void CollectAndShare(
    World world, HouseholdState household, IReadOnlyList<int> visitedDistrictIds);
```

```
Expire:
    npcId 昇順に world.Knowledge[npcId] から
        world.Now.DayIndex − o.ObservedAt.DayIndex > retentionDays を取り除く
    (List<T>.RemoveAll。生き残りの相対順は保つ)

CollectAndShare:
    居た区画 = { household.DistrictId } ∪ visitedDistrictIds        (GDD06 §3.1)

    world.Market を MarketKey 昇順に1回だけ走査し、各エントリについて:
        key.SellerId == household.Id                → 飛ばす(自分の売り注文)
        sellerDistrict = world.Households[key.SellerId].DistrictId
        居た区画のいずれからも District.Distance(…) > District.VisionRadius → 飛ばす

        観測 = new PriceObservation {
                   ItemId     = key.ItemId,
                   LocationId = sellerDistrict,
                   Price      = 提示価格,
                   SellerId   = key.SellerId,
                   ObservedAt = world.Now,
                   Source     = ObservationSource.Direct }

        household.MemberNpcIds の昇順に world.Knowledge[npcId].Add(観測)
```

- **`Market` を1回だけ走査することが、重複を構造で防いでいる。** 居た区画を外側のループにすると、自区画と訪問区画の**両方から見える売り手が2件記録される** — 平均は売り手ごとに畳むので相場基準は変わらないが、`Knowledge` はハッシュ対象なので状態が変わる
- **`LocationId` は売り手の区画である**(観測者の区画ではない)。`PriceObservation.LocationId` の doc が「`location` は**区画**であって売り手ではない」と書いているのは**欄の意味**の話であり、入る値は**店の場所**である。#37 の移動費がここを読む
- **`Source` は `Direct` だけを作る。** 帰宅時共有は M0 で唯一の共有経路であり、`Heard` を区別して読む処理が M0 に無い(W2-04 が既に「M0 が生成するのは `Direct` だけ」と決めている)。区別を入れるのは Rumor([#42](https://github.com/stama72/visionary/issues/42))
- **世帯全員に同じレコードを配る**([GDD08 §8.1](../03-gdd/08-household-and-decision.md)「見ただけの売り注文も含む」)。**世帯主だけに入れると [GDD08 §9](../03-gdd/08-household-and-decision.md) 検証項目4(世帯内共有が効くか)が実行できない**
- **失効の境界は `OfferPrice.TryMarketReference` と揃える。** あちらは `差 > retentionDays` を無効としているので、こちらも `差 > retentionDays` を取り除く。**`>=` にすると、保持期間ちょうどの観測が読む前に消える**
- **`visitedDistrictIds` は本タスクでは常に空である。** 買い物の段(#37)が訪れた区画を渡す。**引数を先に開けておく**

### 8. `TradeSystem` の追加(`Systems/TradeSystem.cs`)

`Step` の末尾に**観測の段**を足す。値付けの段には触らない。

```
1. 新しい提示価格を求める(既存)
2. world.Market.Clear() してから一括で書く(既存)
3. 観測(GDD06 §3.1 / GDD08 §8.1)
       Observations.Expire(world, definition.ObservationRetentionDays)
       世帯 Id 昇順に Observations.CollectAndShare(world, household, 訪れた区画)
```

- **失効を先に呼ぶ。** 当日生まれた観測は差0 なのでどちらの順でも消えないが、**「生まれた当日は失効しない」が順序に依存しない形になる**
- **観測の段は値付けの後である。** [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md)「今日の知覚 = 自区画から距離 R 以内に**出ている**売り注文」なので、観測するのは**当日の提示価格**である。生まれた観測は翌日から有効な記憶になる(`TryMarketReference` が差0 を弾く)
- **訪れた区画は `Array.Empty<int>()` を渡し、「#37 が渡す」とコメントする**
- **乱数は引かない**(既存の規約のまま)

## 落ちるべき条件(テスト)

**この節が完了条件そのもの。** 目標は「通ること」ではなく「**壊したときに落ちること**」である。

- **`BuyerBudget` / `DailyConsumption` は純関数なので直接呼ぶ。** `TradeSystem` を走らせるテストは `SimScheduler.Advance` 経由にする(`SimContext` のコンストラクタが `internal`)
- **`BuildM0` の値に依存したテストを書かない**(#28 が値を動かす)。テスト専用の `WorldDefinition` を組み立てること
- **`EconomySystemTestFixtures` に新しい欄の既定値を足す**(既存の書き方に倣い、省略可能引数で差し替えられるようにする)

| #  | テスト | 検証内容 | この実装ミスで落ちる | 核心 |
| -- | ------ | -------- | -------------------- | ---- |
| 1  | `StockPressureStaysAtOneThousandUpToTheTarget` | 目標3・予想 0/1/3 → すべて **1000** | 在庫に比例させて 0→1000 に上げる(基礎値の意味が壊れ、赤字の原価で仕入れが発生する) |  |
| 2  | `StockPressureFallsLinearlyToZeroAtTwiceTheTarget` | 目標3: 予想4 → **667**、予想5 → **334**、予想6 → **0** | `CeilDiv` を `FloorDiv` にする(666 / 333)。分母を `2 × 目標` にする | **核心** |
| 3  | `StockPressureIsZeroBeyondTwiceTheTarget` | 目標3・予想 7 / 100 → **0** | clamp を外して負を返す(予算が負になる) |  |
| 4  | `StockPressureHandlesAZeroTarget` | 目標0: 予想0 → **1000**、予想1 → **0**。例外を投げない | 枝の順を入れ替えて `DivideByZeroException` |  |
| 5  | `BudgetScalesTheBaseValueByStockPressure` | 基礎値100・圧力1000 → **100**、圧力500 → **50**、圧力0 → **0**、基礎値**500**・圧力**1** → **1**(0.5 の切り上げ) | 切り下げる(0)。`× permille / 1000` を手で書く(先に割って0)。`ApplyPermille` を通さない |  |
| 6  | `PurchaseQuantityReachesTheTargetAtTheBaseValue` | 基礎値100・実質コスト100・目標3・予想0 → **3** | 2×目標まで買う(6)。予想在庫を引かない | **核心** |
| 7  | `PurchaseQuantityBuysMoreBelowTheBaseValue` | 基礎値100・実質コスト50・目標3・予想0 → **4**(4.5 の切り下げ) | `CeilDiv` を `FloorDiv` にする(5)。GDD02 §8.2.3 の検算表を「×1.5 ちょうど」と読んで切り上げる | **核心** |
| 8  | `PurchaseQuantityIsZeroAboveTheBaseValue` | 基礎値100・実質コスト101 → **0**。基礎値0・実質コスト1 → **0**(例外を投げない) | 除算を先に書く(基礎値0で `DivideByZeroException`) | **核心** |
| 9  | `PurchaseQuantityNeverGoesNegative` | 基礎値100・実質コスト100・目標3・**予想5** → **0** | `max(0, …)` を外す(在庫過多の世帯が負の数量を返し、#37 が「売る」側へ回る) |  |
| 10 | `PurchaseQuantityRejectsNonPositiveCost` | 実質コスト 0 / −1 で `ArgumentOutOfRangeException` | 0 を通す(基礎値0との組で上の保証が崩れる) |  |
| 11 | `NecessityBaseValueAppliesToleranceOrFallsBackToTheRatio` | 相場基準100・許容乖離1200‰ → **120**。観測なし・流動資金200・50‰ → **10** | 許容乖離を掛けない(100)。観測があるのにフォールバックを使う | **核心** |
| 12 | `PreferenceBaseValueUsesSurplusFundsNotLiquidFunds` | 余剰資金0 → **0**。余剰資金1000・200‰ → **200** | 母数に流動資金を使う(GDD02 §8.2.1 が黒字倒産と名指しした経路。**余剰資金0 の世帯が嗜好を買う**) | **核心** |
| 13 | `DurableBaseValueTakesTheSmallerOfReferenceAndRatio` | 相場基準100・流動資金2000・10‰(=20) → **20**。相場基準10・同 → **10**。観測なし → **20** | `max` を取る。相場基準だけを返す(資金がなくても買う)。予算比率だけを返す(相場より高く買う) | **核心** |
| 14 | `SurplusFundsClampsAtZero` | 流動資金100・運転資金250 → **0**。100・40 → **60** | `max(0, …)` を外す(嗜好の基礎値が負になる) |  |
| 15 | `DerivedDemandSharesTheAllowedCostWithoutExceedingIt` | 出力数量1・前日価格**12**・利幅200‰(許容原価合計 **10**)、入力A(相場3・数量1)B(相場4・数量1) → **A=4 / B=5**、`Σ(基礎値 × 数量) = 9 ≤ 10` | **‰ の按分比を中間に挟む**(`ApplyPermille` で 5 と 6 になり合計11 > 10。**最低利幅の保証が、観測の揃った日にだけ静かに破れる**)。`CeilDiv` を使う | **核心** |
| 16 | `DerivedDemandDividesTheAllocationByTheRequiredQuantity` | 前日価格60・出力数量1・利幅200‰(許容原価合計 **50**)、入力A(相場10・**数量2**)単独 → 配分予算50・基礎値 **25** | 数量で割らない(50 → 実際の支払いは2倍の100になり、許容原価合計を超える) |  |
| 17 | `DerivedDemandSubtractsFallbackInputsBeforeSharing` | A(相場20・数量1)/ B は観測なし、流動資金200・必需50‰ → B=**10**、残余 `50−10=40` → A=**40** | 差し引かない(A=50 で合計60 > 50)。残余を負のまま使う | **核心** |
| 18 | `DerivedDemandFallsBackForEveryInputWhenNoReferenceSurvives` | 全入力に観測なし → 全員 **ApplyPermille(流動資金, 50‰)**。例外を投げない | `Σ_{k∈J}` で割ってゼロ除算(R=1 では**初日に限らずいつでも通る経路**) | **核心** |
| 19 | `DerivedDemandFallsBackWhenThereIsNoPreviousOutputPrice` | `hasPreviousOutputOfferPrice = false` → 観測があっても全入力フォールバック | 前日価格を0として按分する(許容原価合計0 → 基礎値が全部0 → **初日に原材料を一切買わない**) | **核心** |
| 20 | `DerivedDemandFloorsEveryDivision` | 前日価格9・出力数量1・利幅200‰(許容原価合計 **7**)、入力A(相場1・数量3) → 基礎値 **2**(`floor(7/3)`) | `CeilDiv`(3 → 3×3=9 > 7 で利幅が破れる) |  |
| 21 | `LookaheadAddsEachDaySeparatelyWithItsOwnSeason` | 薪・親方+徒弟(各基礎量2)・係数{春800,夏400,秋1000,冬2000}、`now` = 秋30日(`DayIndex 89`)、days=3 → **20**(秋4 + 冬8 + 冬8) | `3 × 当日の1日消費量`(12)。合計してから季節係数を掛ける。**冬支度が帰結として現れなくなる** | **核心** |
| 22 | `LookaheadMatchesWhatConsumptionActuallyEatsForOneDay` | 同じ世界で `ConsumptionSystem` を1日走らせた実消費量と `Lookahead(days: 1)` が**一致** | 消費量の計算を2か所へ書き分ける(構成員ごとの切り上げを片方だけにする等)。**この乖離はどちらの単独テストでも捕まらない** | **核心** |
| 23 | `LookaheadIsZeroForNonPositiveDays` | days 0 / −1 → **0** | 常に1日ぶん数える(目標在庫を持たない品目に在庫圧力が立つ) |  |
| 24 | `EveryPurposeProducesALineInTheSpendingScanOrder` | パン屋の世帯 → 行が **必需(薪→パン)→ 生産の入力(小麦粉→薪)→ 耐久(工具)→ 嗜好(ビール)** の順に並ぶ | 用途を1つ落とす(GDD02 §2.2「どれか1つでも落とすと、その式が W2 で一度も実行されない」)。`enum` の値の順で並べる | **核心** |
| 25 | `FirewoodAppearsAsTwoLinesWithDifferentStocksAndTargets` | 薪の2行が **(必需, 世帯在庫, 先読み7日)** と **(生産の入力, 工房在庫, 定数表)** で別の値を持つ | 品目単位に畳む(GDD02 §8.2.1「生産用の薪が運転資金から丸ごと落ちて、黒字倒産が生産世帯でだけ戻る」) | **核心** |
| 26 | `WorkingCapitalScansOnlyProductionInputAndNecessity` | 嗜好・耐久の目標在庫と相場基準をいくら大きくしても `WorkingCapital` が変わらない | 全用途を足す(余剰資金が過小に出て嗜好が恒久に0。§12-3 の醸造が不通過になる) | **核心** |
| 27 | `WorkingCapitalIgnoresCurrentStockAndUnobservedItems` | 在庫を目標まで満たしても `WorkingCapital` が変わらない。観測の無い品目は**加算されない** | 現在庫を差し引く(周期的な黒字倒産)。§8.2.7 のフォールバックを足す(`余剰資金 = 流動資金 − f(流動資金)` の自己参照) | **核心** |
| 28 | `BuyerReferenceComesFromTheHeadNpcAndIgnoresOwnOfferPrice` | 徒弟にだけ観測を置くと必需がフォールバックへ落ちる。`Market` に自世帯の当日価格があっても必需の基礎値が変わらない | 構成員全員から集める。`hasOwnPreviousPrice: true` を渡す(売り手の式を買い手へ流用)。`selfHouseholdId` に `HeadNpcId` を渡す | **核心** |
| 29 | `DurableLineIsMeasuredInDurabilityNotUnits` | 工具1個・N=30・摩耗5・500‰・階層1000‰ → 予想在庫 **25**、目標在庫 **15** | 個数で数える(在庫1・目標15 → 圧力1000‰ で毎日工具を買い続ける)。摩耗を引かない | **核心** |
| 30 | `DurableTargetUsesTheHeadRankCoefficient` | 世帯主の階層を `Apprentice`(200‰)にすると目標在庫が **3** に縮む | 階層係数を掛けない。構成員の最小/最大を採る |  |
| 31 | `ObservationsAreBornOnlyWithinTheVisionRadius` | 自区画4・売り手を区画1(距離1)と区画**0**(距離2)に置く → 区画1の売り注文**だけ**が観測になる。`visitedDistrictIds` に 0 を渡すと区画0 も入る | 距離を見ない(全部見える → 情報の摩擦が消え §12-7 が無意味になる)。起点を自宅だけに固定する(知識が永久に自宅の R 以内に閉じる) | **核心** |
| 32 | `OwnOffersAreNeverObserved` | 自世帯の売り注文が `Knowledge` に入らない | 除外を忘れる(GDD06 §3.1 の「純粋な自己ループ」が観測側から成立する) |  |
| 33 | `ObservationRecordsThePriceSellerAndSellerDistrict` | `Price` = `Market` の値、`SellerId` = 売り手世帯、`LocationId` = **売り手の区画**、`ObservedAt` = 当日、`Source` = `Direct` | `LocationId` に観測者の区画を入れる(#37 の移動費が常に0になり、空間の摩擦が消える) |  |
| 34 | `TodaysObservationIsSharedWithEveryHouseholdMember` | 親方と徒弟の `Knowledge` に**同じ件数・同じ内容**が入る | 世帯主にだけ入れる(GDD08 §8.1 の帰宅時共有が無くなり、§9 検証項目4 が実行不能になる) | **核心** |
| 35 | `ObservationsExpireStrictlyAfterTheRetentionPeriod` | retention=7・`now`=D10: **D3 は残り D2 は消える**。当日(D10)に生まれたものは消えない | `>=` で書く(保持期間ちょうどが読む前に消え、`TryMarketReference` の境界と1日ずれる)。失効を呼ばない | **核心** |
| 36 | `ObservationsAreAppendedInMarketKeyOrder` | 1世帯の `Knowledge` の並びが **(品目Id, 売り手Id) 昇順**。同じ売り注文が2件入らない | 居た区画を外側のループにする(自区画と訪問区画の両方から見える売り手が重複する) |  |
| 37 | `ObservationBecomesUsableOnTheNextDayNotToday` | パイプラインを1日進めて観測が生まれても**当日の相場基準は立たない**(原価下限のまま)。2日目に進めると立つ | 当日の観測を使う(GDD06 §3.1 の「日内の相互参照を切る」が崩れる)。観測の段を値付けの**前**に置く | **核心** |
| 38 | `WorldDefinitionRejectsMalformedBudgetTables` | 入力目標在庫の**入力品目欄0** / **入力品目以外の非0** / 行数違い / 必需と嗜好で**同じ品目が正** / **`Item.Tools` を必需に置く** / 予算比率の長さ違い / **`ProductionInput` の比率が非0** / `Necessity`・`Durable` の比率0 / 階層係数0 / 許容乖離0 を、それぞれ拒む | 表の形を検査しない(**使われない調整軸と、単位の違う2本の行が定義から入る**) |  |
| 39 | `WorldDefinitionCopiesEachInputTargetRow` | 渡した jagged 配列の**行の中身**を後から書き換えても定義が変わらない | 外側だけ `ToArray()` する |  |
| 40 | `ConsumptionStillEatsTheSameAmountAfterTheExtraction` | 既存の `ConsumptionSystemTests` が**1行も書き換えずに**緑 | `DailyConsumption` を複製として作り、`ConsumptionSystem` を元のままにする(**切り出しの目的そのものが消える**) |  |

**「核心」印のうち、少なくとも #7・#15・#21・#22・#27・#29・#37 には実際に変異を当てて落ちることを確認し、当てた変異と結果を doc コメントかコミット本文に残す**([process/02](../process/02-task-spec.md))。

> **核心が多いのは W2-04 と同じ理由である** — 予算の式も「間違っても、それらしい整数が出る」。丸めの向き・母数の取り違え・用途の畳み込みは、いずれも例外を出さず在庫も負にしない。[ADR-0008](../adr/0008-review-scope-narrowed-to-unnoticeable-defects.md) の象限I-a(緑のまま壊れる)そのものであり、判別力はテストの値の選び方にしかない。

### 別表: レビュー1巡目で足したテスト(フェーズ2)

**上の表は implementer に渡した時点の指示であり、最終形ではない。** レビュー1巡目が、上の表のとおりに書くと判別力ゼロになる行を2つ見つけたので、**上の表は書き換えずにここへ足す**(表が最初からこう書けていたのか、レビューで育ったのかを後から見分けられるようにするため)。

**#12 と #28 は、名指しした誤りが住めない場所へ変異を割り当てていた**(象限I-b):

- **#12** の対象 `BuyerBudget.PreferenceBaseValue(int surplusFunds, int ratioPermille)` は**母数を引数で1つしか受け取らない**ので、「母数に流動資金を使う」という誤りを関数の中に書けない。**純関数に切り出した時点で、その取り違えは呼び出し側(`BuyerDemand.Build`)へ移っている。** 誤りが住める唯一の場所を、上の表はどの行も見ていなかった
- **#28** は3つの変異を1行に束ねているが、3番目「`selfHouseholdId` に `HeadNpcId` を渡す」は、1・2番目が要求するシナリオでは**原理的に落ちない** — 観測の `SellerId` を自世帯 Id または世帯主 NpcId に一致させる別のケースが要る。**W2-04 が同じ理由で専用テストを分けている**([`TradeSystemTests`](../../tests/Visionary.Sim.Tests/Systems/TradeSystemTests.cs) の R1)

**根の原因は同じである** — `BuyerBudget` の純関数テストは**式**を守るが、`BuyerDemand` が「どの母数・どの比率‰・どの引数順で」呼ぶかは**別の情報**であり、式のテストは1件もそこに当たらない。`BuyerDemand` は本タスクでは誰も呼ばない(スコープの節)ので、実行時にも誰も踏まない。

| #    | テスト | 検証内容 | この実装ミスで落ちる | 核心 |
| ---- | ------ | -------- | -------------------- | ---- |
| R1-1 | `BuyerReferenceExcludesOwnHouseholdNotTheHeadNpcId` | 自世帯 Id を `SellerId` とする観測と、**世帯主 NpcId を `SellerId` とする観測**を両方置く → 前者だけが相場基準から外れ、後者は**含まれる** | `selfHouseholdId` に `HeadNpcId` を渡す。[`WorldGenerator`](../../src/Visionary.Sim/Definition/WorldGenerator.cs) は `headNpcId = householdId × 2` なので、世帯主の NpcId は**実在する別世帯の Id** である。取り違えた実装は別世帯の売り注文を毎日無言で捨て、相場基準が別の平均になる。**#28 は `SellerId` を999固定にしたので、0 とも 2 とも一致せず偶然除外が効く** | **核心** |
| R1-2 | `PreferenceLineUsesSurplusFundsNotLiquidFunds` | **`BuyerDemand.Build` が返す嗜好の行**の `BaseValue`。運転資金で余剰資金が0に潰れた世帯 → **0**。余剰のある世帯 → `ApplyPermille(余剰資金, 比率‰)` | `BuyerDemand` が `PreferenceBaseValue` へ `household.LiquidFunds` を渡す。**#12 が名指しした黒字倒産が、#12 が見ていない場所でそのまま成立する** | **核心** |
| R1-3 | `EveryLineCarriesItsOwnStockPressureAndBudget` | 目標14・予想3 の必需の行で `StockPressurePermille` = **1000**、`Budget` = `ApplyPermille(BaseValue, 1000)` | `BuildLine` が `StockPressurePermille(expectedStock, targetStock)` を**引数逆順**で呼ぶ(0‰ に落ち、必需を永久に買わない)。`Budget` 欄を埋め忘れる。**`DemandLine` のこの2欄は上の表のどの行も assert していない** | **核心** |
| R1-4 | `DurableAndPreferenceReadTheirOwnBudgetRatio` | 耐久と嗜好で**違う比率‰** を与えた定義で、両方の行の `BaseValue` がそれぞれの比率で出る | `BudgetRatioPermilleByPurpose` の添字を入れ替える(M0 の 10‰ と 200‰ が入れ替わり、耐久の基礎値が20倍になる) |  |
| R1-5 | `ProductionInputFallbackUsesTheNecessityRatio` | 観測ゼロかつ `hasPreviousOutputOfferPrice = false` の世帯 → 生産の入力の `BaseValue` = `ApplyPermille(流動資金, 必需の比率‰)` で **0 ではない** | `DerivedDemand` へ `DemandPurpose.ProductionInput` の比率(**定義が0を強制している**)を渡す。**#38 まで `W == 0` の経路が常態である**(申し送り)ので、M0 の既定が「原材料を一切買わない」になる | **核心** |
| R1-6 | `WorkingCapitalIgnoresPreferenceEvenWhenConsumedAndObserved` | #26 の**嗜好側を実際に効かせる**: 嗜好の品目に**正の1日消費量**と**相場基準の立つ観測**を与えたうえで目標在庫を膨らませても `WorkingCapital` が変わらない | 消費量0・観測なしのまま日数だけ膨らませる。`Lookahead` も相場基準も0なので、嗜好を運転資金へ足す変異が `0 × 0` を足して**通過する**(#26 が主張する判別力の半分が無い) | **核心** |
| R1-7 | `DurableTargetIgnoresNonHeadMemberRanks` | 世帯主 `Master`(1000‰)+ 徒弟 `Apprentice`(200‰)の**混成世帯**で、目標在庫が**世帯主**の係数で出る | 構成員の最小/最大を採る。**#29・#30 はどちらも構成員1名なので、世帯主・最小・最大が同じ値になり落ちない。M0 の世帯は親方+徒弟の混成であり、この誤りは M0 で実際に踏まれる** |  |

**あわせて #27 の変異記録を実態に合わせる。** 記録は「`Assert.Equal(95, zeroStockWorkingCapital)` が実際値35で失敗した」となっているが、記載の変異(`target − expected`)で実際に落ちるのは次行の在庫あり/なしの比較である。**テストの判別力は仕様どおりあるので、直すのは記録のほうである**([process/03](../process/03-corrections.md))。

## 申し送り

### #37(Trade システムと店の選択)へ

- **`BuyerDemand.Build` に渡す `previousOutputOfferPrice` は、値付けの段が `Market.Clear()` する前に控えた値である。** 同じ `Step` の後段で `Market` を読むと当日の値が返る。**控える配列は `World` の状態にしない**(1日限りの中間値であり、ハッシュ対象を増やす理由が無い)
- **`Observations.CollectAndShare` の `visitedDistrictIds` に、その日に買い物で訪れた区画を渡す**([GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md)「距離の起点は『その日に居た区画』であって自宅だけではない」)。**渡さないと、知識は永久に自宅の R 以内に閉じ、§12-7(知識が広がるか)が構造的に不通過になる**
- **`DemandLine` の耐久の行だけ単位が耐久値である。** 購入量を個数に直すのは `CeilDiv(購入量, definition.ProductionRunsPerToolWear)`([GDD02 §8.2.1](../03-gdd/02-economy.md))
- **走査順は `HouseholdDemand.Lines` の並びがそのまま持っている**([GDD02 §6.2.1](../03-gdd/02-economy.md))。並べ直さないこと
- **`予想在庫` は約定で動かない。** `Build` は1日1回・買い物の前に呼ぶ想定である。複数単位の買い増しは `PurchaseQuantity` の線形解が一度で解く([GDD02 §8.2.3](../03-gdd/02-economy.md))
- **木材加工が自分の作る薪を必需として買いうる。** 工房在庫と世帯在庫は別勘定であり、[GDD02 §8.1.1](../03-gdd/02-economy.md) が「出力を手元に残す規則を M0 では置かない」と決めている。**塞がない**

### #38(都市外市場)へ

- **窓口の売り注文は `Market` に無いので、`Observations.CollectAndShare` は見ない。** 窓口を観測に載せるのは #38 の仕事である([GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md)「移動費も観測も通常どおり生じる」)。載せるときは `world.Households[SellerId]` で区画を引く経路を通らないこと(`ExternalMarketSellerId` は `int.MaxValue`)
- **1次産品の `相場基準` は、窓口の観測が入るまで立たない。** それまで生産の入力の基礎値は §8.2.7 のフォールバックに落ちる — **`W == 0` の経路が M0 の初期に常態化する**ので、#38 の前後で派生需要の挙動が大きく変わる

### #40(NeedGeneration)へ

- **「知っている店が0件」の判定はここに無い。** [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md)「それでも0件のとき」の `在庫不足 / 遠方在庫` は #37 の店選択と #40 が持つ
- **`DemandLine.TargetStock` と `ExpectedStock` が、その判定の入力になる**(`予想在庫 < 目標在庫`)

### #28(値の検算と調整)へ

- **ここが初出の初期値は3つである** — `NecessityTolerancePermille = 1200`、`BudgetRatioPermilleByPurpose[Preference] = 200`、`InputTargetStockDaysForM0 = 5`。[GDD02 §13.2](../03-gdd/02-economy.md) の調整対象表に既に行がある(§8.2.1「許容乖離‰」「嗜好の予算比率‰」「目標在庫」)
- **必需の予算比率 50‰・耐久の予算比率 10‰・耐久の目標在庫 500‰ は GDD が値まで書いている**([GDD02 §8.2.7](../03-gdd/02-economy.md) / [§8.2.1](../03-gdd/02-economy.md))。動かすときは GDD 側も直す
- **`ToolTargetStockPermille = 500` と `RankCoefficientPermille[Master] = 1000` の組は、目標在庫を `N/2` にする。** 工具を1個持つ世帯は `N = 2 × 目標` なので在庫圧力が **0‰ になり、摩耗が進むまで工具を買わない**。[GDD02 §8.2.1](../03-gdd/02-economy.md) の「最下層の NPC は製品寿命の半分で目標在庫に達する」は仕様どおりだが、**M0 の世帯主は全員親方なので、実際には『半分まで摩耗して初めて買い始める』挙動になる。** §12-3(生産チェーンが止まらないか)と併せて見ること
- **`必要運転資金` は現在庫を差し引かないので、健全な世帯からも恒久的に同額が控除される**([GDD02 §8.2.1](../03-gdd/02-economy.md) が自ら「嗜好の需要を構造的に細らせる」と書いている)。**醸造が成立するかは §12-3 で実測する** — 調整手段は嗜好の予算比率‰ と目標在庫だけである

## 編集してよい文書

worktree をまたいだ所有権の宣言。**ここに挙がっていない文書は触らない。**

- なし(コードとテストのみ)

**[GDD02 §8.2〜§8.2.7](../03-gdd/02-economy.md)、[GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md)、[GDD08 §8.1・§9](../03-gdd/08-household-and-decision.md) は、本タスクが必要とする決定をすべて持っている**(不足していた3点はフェーズ1 が本ブランチで先に埋めてある)。実装が仕様と食い違ったら、**直すのはコードであって文書ではない**。文書側を直す必要があると判断したら、それは象限I-b(仕様そのものの欠陥)なので[止まって報告する](../process/02-task-spec.md)。

## このタスクで特に効く規約

[ADR-0002](../adr/0002-time-model-and-determinism.md) の決定論規約のうち、このタスクで踏みやすいものだけを挙げる。**機械で捕まるものは書かない**(浮動小数点と列挙順が保証されないコレクションは `BannedSymbols.txt` と `DeterminismConventionTests` が止める)。

- **丸めの向きが式ごとに違う。** 派生需要の按分は**すべて切り下げ**(`FloorDiv`)、在庫圧力・予算・基礎値・耐久値→個数の変換は**切り上げ**(`CeilDiv` / `ApplyPermille`)、購入量の線形解は**引かれる側を切り上げるので全体としては切り下げ**。**裸の `/` を書かない**
- **派生需要だけは `ApplyPermille` を使ってはならない。** ‰ を中間に挟むと端数が入力ごとに切り上がり、`最低利幅‰` の保証が破れる([GDD02 §8.2.1](../03-gdd/02-economy.md))。**規約に揃えたくなる形なので注意する**
- **中間の積と合計は `long`。** `目標在庫 × 相場基準` の総和(必要運転資金)、`1000 × (2×目標 − 予想)`、`許容原価合計 × w`
- **係数の定数には単位のコメントを付ける**(`= 1200; // ‰`、`= 5; // 日`)
- **走査順は 世帯 Id 昇順 → `Market` は `MarketKey` 昇順 → 構成員は `MemberNpcIds` 昇順 → 同一用途の行は品目 Id 昇順。** `Knowledge` はハッシュ対象なので、追加の順が変わるとハッシュが変わる
- **`Knowledge` の添字は NpcId、`Households` の添字は世帯 Id。** `TryMarketReference` の `selfHouseholdId` に `HeadNpcId` を渡す間違いを**型は止めない**([TDD01 §3.2](../04-tdd/01-sim-core-and-m0.md))

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **「核心」印のうち指定した7件に変異を当てて落ちることを確認し、当てた変異と結果を残した**
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
