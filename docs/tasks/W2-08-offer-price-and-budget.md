# W2-08: 提示価格と予算の統一形

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#97](https://github.com/stama72/visionary/issues/97)                |
| 根拠     | [GDD02c §1〜§2.3](../03-gdd/02c-price-and-budget.md) / [GDD02b §3.1・§3.2・§5](../03-gdd/02b-consumption-and-household.md) / [GDD02a §4・§5](../03-gdd/02a-production.md) / [TDD01 §3.2・§3.3](../04-tdd/01-sim-core-and-m0.md) |
| ブランチ | `feat/97-offer-price-and-budget`                                     |
| worktree | `visionary/`(本体)                                                  |

> **この文書は使い捨ての作業指示である。** 実装完了時点で凍結し、以後の正はコードと GDD/TDD。

## スコープ

**[#85](https://github.com/stama72/visionary/issues/85) で凍結した経済の分冊に実装を追随させる 5 本の 2 本目である**([#96](https://github.com/stama72/visionary/issues/96) が 1 本目)。本タスクは**値付け(順5 段1)と予算(順5 段4)**を新しい式に置き換える。

**#96 が「作る側」の値を揃えたので、本タスクが読む材料はすべて揃っている** — 外部買値(床)・生産能力から導いた目標在庫・工具の摩耗の単位(‰人日)。

**含まない:**

- **店の選択と外出の改訂**([#98](https://github.com/stama72/visionary/issues/98))。`StoreChoice` の「知っている店のうち実質コストが最小」という選び方は**旧規則のまま**である。本タスクが変えるのは、選んだ店の単価をどう判定するか(§5 の段5 手順3・4)だけ
- **輸出入**([#38](https://github.com/stama72/visionary/issues/38))。前日の約定単価の式は輸出を含むが、**輸出を発生させるのは #38 である**。本タスクの時点では帳簿に輸出の行が現れない
- **破産中フラグの更新と④の付け替え**([#39](https://github.com/stama72/visionary/issues/39))。本タスクは**フラグを読む側**(値付けの §1.4)と、フラグの材料である `UnaffordableNecessityCount` を**書く側**だけを作る
- **Need の生成**([#40](https://github.com/stama72/visionary/issues/40))、**`vsim` / Runner の追随**([#41](https://github.com/stama72/visionary/issues/41))、**`DomainEvent` の発行**
- **原価(`原価[レシピ1回]`)の算出。** 下記「`UnitCost` を残さない」を見ること

## 設計の前提(フェーズ1で決めたこと)

**実装の前に GDD/TDD を直してある(本ブランチの先行コミット)。実装はこの 6 点を仕様として読むこと。**

| どこ | 何を決めたか |
| ---- | ------------ |
| [GDD02c §2.1](../03-gdd/02c-price-and-budget.md) | **予算の第 1 項に在庫圧力‰ が掛かる**。`予算 = min( ApplyPermille(相場項, 在庫圧力‰) , 現金上限 , 利潤上限 )`。**在庫圧力が掛かるのは相場項だけ**で、現金上限・利潤上限には掛からない |
| [GDD02b §5.2](../03-gdd/02b-consumption-and-household.md) | 同じ形をゲートにも書いた。**線形解の基礎値は在庫圧力を掛ける前の相場項である**(掛けると二重に効く)。**相場項が無い日は基礎値 = 現金上限で、このとき予算に在庫圧力は掛からない** |
| [GDD02b §5.2](../03-gdd/02b-consumption-and-household.md)「購入量 0 の理由」 | **理由は `相場 → 利潤上限 → 現金上限` の固定順で、最初に当たったものを採る。argmin ではない。同点は相場が勝つ。** argmin にすると価格が跳ねた日に破産中フラグが一斉に立ち、②の投げ売りの検出器が価格ショックの検出器に化ける |
| [TDD01 §3.2・§3.3](../04-tdd/01-sim-core-and-m0.md) | **売り手の「自分の錨」は前日の提示価格ではなく前日の約定単価であり、出所は `Ledgers` である。** `Market` の前日の値は残るが、読み手は**利潤上限の `提示価格_出力[前日]` だけ**になる |
| [TDD01 §3.6](../04-tdd/01-sim-core-and-m0.md) | **初日の提示価格は床(外部買値)に張り付く。** 床が定数になったので、**初期価格のために初期在庫の取得原価を要求しない** |
| 本書 §5 | **段5 が予算のゲートと線形解に渡すのは `UnitEffectivePrice`(実効価格)であり、`UnitRealCost`(実質コスト)ではない**。下記「実効価格を渡す」 |

### 実効価格を渡す — 実質コストではない

**旧の段5 は `store.UnitRealCost`(提示価格に移動費を単価へ割り戻した値)を予算のゲートと線形解へ渡していた。本タスクで `store.UnitEffectivePrice` に変える。**

[GDD02b §7](../03-gdd/02b-consumption-and-household.md) が「便益と費用の分離」として名指ししたとおり、**新しい予算は移動費を一切含まない**。そこへ移動費を割り戻した単価をぶつけると、[GDD06 §2](../03-gdd/06-trade-and-negotiation.md) の外出の費用と合わせて**空間の摩擦が二重計上になる**。これは #98 を待って直せる種類の食い違いではない — 予算の式が変わる本タスクで一緒に変えないと、中間状態の数字が「機構の帰結」として読めなくなる。

**`StoreChoice.TrySelect` が `UnitRealCost` の argmin で店を選ぶことは変えない。** 変えるのは選んだ後の判定だけであり、選び方の改訂は #98 が持つ。`StoreCandidate` は両方の欄を持ったままにする。

### `UnitCost` を残さない

**`OfferPrice.UnitCost`(原価)と `OfferPrice.CostFloor`(原価下限)を削除する。** 床が外部買値になったことで `CostFloor` の呼び出し側が消え、`CostFloor` が唯一の呼び出し側だった `UnitCost` も道連れに死ぬ。

**「使わないので残す」を選ばない理由は、残る `UnitCost` が [GDD02a §5](../03-gdd/02a-production.md) の現行式と**違う**からである** — 現行の `原価[レシピ1回]` は摩耗費を含むが、`UnitCost` は含まない。読み手が居ないまま古い式を残すと、原価を最初に読む [#41](https://github.com/stama72/visionary/issues/41)(メトリクスの日次の利潤)が**それを正だと思って使う**。#41 が読み手と一緒に 02a §5 の式で書くこと。

**`OfferPrice.UpdatedAcquisitionCost`(仕入れ移動平均単価、02a §5.1)は残す。** こちらは呼び出し側(`TradeSettlement`)が生きており、本タスクの利潤上限が摩耗費の材料として読む。

### 相場基準は「速さだけが違う 2 つ」である

[GDD02c §1.2](../03-gdd/02c-price-and-budget.md) の表のとおり、売り手と買い手は**有効な観測の判定は同じで、畳み方だけが違う**。

| | 畳み方 | 自分の錨 |
| -- | ------ | -------- |
| 売り手(速い) | 他の売り手ごとに**最新の 1 件** | **前日の約定単価**(帳簿。輸出を含む) |
| 買い手(遅い) | 有効な観測**すべて**の平均 | 無い |

**したがって有効性の判定を 1 か所に置く。** 2 つの関数へ写すと、保持期間・当日の除外・自分の売り注文の除外のどれかが片側だけずれても緑のまま通る。**そのずれは [GDD02c §1.2](../03-gdd/02c-price-and-budget.md) の囲みが「複利発散の歯止め」と呼んだ性質そのものを壊す**(買い手が速くなった瞬間に天井が消える)。

### `NoPurchaseReason` を `World` の状態にしない

**購入量 0 の理由は段5 の局所値であり、`World` に持たせない。** 読み手は同じ手順の中にある `UnaffordableNecessityCount` の加算だけで、日をまたいで読む者が居ない。状態にすると [TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md) のハッシュに読み手のない欄が増える(§3.2 の「使わない状態を器に残さない」)。

## 作るもの

名前空間は `Visionary.Sim`(`Definition/` `World/`)と `Visionary.Sim.Systems`(`Systems/`)。

### 1. `MarketReference`(新規 `Systems/MarketReference.cs`)

**`OfferPrice.TryMarketReference` をここへ移し、売り手用と買い手用に割る。** 有効性の判定は `private static bool IsValid(...)` 1 つに集約する。

```csharp
/// <summary>相場基準(GDD02c §1.2)。<b>純関数のみ。</b>売り手(速い)と買い手(遅い)の
/// 畳み方だけが違い、有効な観測の判定は共通である。</summary>
public static class MarketReference
{
    /// <summary>
    /// 売り手の相場基準(速い側)。他の売り手ごとに最新 1 件 + 自分の前日の約定単価。
    /// <b>他の売り手の観測が 0 件なら false を返し、自分の約定単価も使わない</b>(GDD02c §1.2)。
    /// </summary>
    public static bool TrySeller(
        IReadOnlyList<PriceObservation> headObservations,
        int itemId, int selfHouseholdId, Tick now, int retentionDays,
        bool hasOwnSettledPrice, int ownSettledPrice,
        out int marketReference);

    /// <summary>
    /// 買い手の相場基準(遅い側)。<b>有効な観測すべての平均</b>(売り手ごとに畳まない)。
    /// 0 件なら false。自分の錨は無い(GDD02c §1.2)。
    /// </summary>
    public static bool TryBuyer(
        IReadOnlyList<PriceObservation> headObservations,
        int itemId, int selfHouseholdId, Tick now, int retentionDays,
        out int marketReference);

    /// <summary>
    /// 前日の約定単価 = CeilDiv( Σ(単価 × 数量) , Σ(数量) )(GDD02c §1.2)。
    /// <b><see cref="LedgerDirection.Sale"/> の行だけを見る。</b>都市外市場への輸出
    /// (<see cref="HouseholdState.ExternalMarketSellerId"/> が相手)も含める ── 帳簿は
    /// 資金の増減と一致する規約(GDD02b §3)なので輸出も約定である。
    /// 前日の売りが 1 件も無ければ false(0 を返さない)。
    /// </summary>
    public static bool TryPreviousDaySettledPrice(
        IReadOnlyList<LedgerEntry> ledger, int itemId, Tick now, out int settledPrice);
}
```

**有効な観測の判定(両方に共通)**:

- `observation.ItemId == itemId`
- `observation.SellerId != selfHouseholdId`(自分の売り注文は自分の観測に入れない)
- `1 <= now.DayIndex − observation.ObservedAt.DayIndex <= retentionDays`(**`Tick` の差ではなく `DayIndex` の差**。当日は入らず、保持期間ちょうどは入る)

**`TrySeller` の畳み込みは `SortedDictionary<int, PriceObservation>`**(`Dictionary` は列挙順が保証されず ADR-0002 に触れる)。同一 tick に同一売り手の観測が 2 件あるときは**後に追加されたほう**(走査で `>=`)。

**`TryBuyer` は畳まずに全件を足す** — 合計と件数だけなので順序に依らない。

**`TryPreviousDaySettledPrice` の前日の判定は `OccurredAt.DayIndex == now.DayIndex − 1`**。数量の合計が 0 以下なら false(ゼロ除算の経路)。

### 2. `OfferPrice` の改訂(`Systems/OfferPrice.cs`)

**削除**: `UnitCost` / `CostFloor` / `TryMarketReference`(§1 へ移動)。

**残す**: `StockRatioPermille` / `PriceCoefficientPermille(int)` / `UpdatedAcquisitionCost`。

**差し替え**:

```csharp
/// <summary>破産中の売り手の価格係数‰(GDD02c §1.4)。在庫を見ずに固定する。</summary>
private const int BankruptCoefficientPermille = 500; // ‰

/// <summary>
/// 提示価格 = max( 床 , ApplyPermille(相場基準, 価格係数‰) )(GDD02c §1)。
/// <b>床は外部買値である</b> ── 原価は値付けに入らない(GDD02c §1)。
/// <b>破産中(isBankrupt == 1)は価格係数‰ を 500 に固定し、在庫比を評価しない</b>(§1.4)。
/// <b>床は破らない</b> ── 半値が床を下回るなら床で並べる。
/// </summary>
/// <remarks>相場基準が立たない日は呼ばない ── 呼び出し側が床をそのまま提示価格にする(§1)。</remarks>
/// <exception cref="ArgumentOutOfRangeException"><paramref name="isBankrupt"/> が 0/1 以外。</exception>
public static int Calculate(
    int floorPrice, int marketReference, int sellableStock, int shipmentTargetStock, int isBankrupt);
```

- **破産中の枝では `StockRatioPermille` を呼ばない。** 在庫比を評価してから係数を捨てる書き方にすると、`出荷目標在庫 <= 0` の検査が破産中だけ効くという非対称が入る
- **`PriceCoefficientPermille` に `isBankrupt` を足さない。** 在庫比 → 係数の写像は §1.1 の表そのものであり、破産中の固定は §1.4 の別の規則である。1 つにすると #28(投げ売りの深さ)が §1.1 の clamp と一緒にしか動かせなくなる(#96 が `BankruptFloorPermille` を別定数に置いたのと同じ理由)

### 3. `PurchaseDecision` / `NoPurchaseReason`(新規 `Systems/PurchaseDecision.cs`)

```csharp
/// <summary>購入量が 0 になった理由(GDD02b §5.2「購入量 0 の理由」)。</summary>
/// <remarks>
/// <b>店を 1 つも知らない・売り手の在庫が尽きた はここに入れない。</b>どちらもゲートに
/// 到達しておらず、資金不足でもない(GDD02b §3.3)。Need の側が拾う(#40)。
/// </remarks>
public enum NoPurchaseReason
{
    /// <summary>ゲートは開いている(購入量が 0 でも、それは到達在庫が予想在庫に届いただけ)。</summary>
    None = 0,

    /// <summary>実効価格が ApplyPermille(相場項, 在庫圧力‰) を超えた。「高すぎて買わなかった」。</summary>
    MarketTerm = 1,

    /// <summary>実効価格が利潤上限を超えた(生産の入力にしか起きない)。</summary>
    ProfitCap = 2,

    /// <summary>実効価格が現金上限を超えた。<b>必需ではこれだけが「資金不足」に数えられる</b>(GDD02b §3.2 経路(1))。</summary>
    CashCap = 3,
}

public readonly record struct PurchaseDecision
{
    /// <summary>購入量。用途の単位(耐久は耐久値)。</summary>
    public int Quantity { get; init; }

    /// <summary><see cref="Quantity"/> が 0 のときの理由。0 でないなら <see cref="NoPurchaseReason.None"/>。</summary>
    public NoPurchaseReason Reason { get; init; }
}
```

### 4. `DemandLine` / `HouseholdDemand` の改訂(`Systems/BuyerDemand.cs`)

**`DemandLine` は min の 3 項を別々に持つ** — 段5 が「どの項が閉じたか」を判定するために要る。

```csharp
public readonly record struct DemandLine
{
    public DemandPurpose Purpose { get; init; }
    public int ItemId { get; init; }

    /// <summary>相場項 = ApplyPermille(相場基準, 許容乖離‰)。<b>在庫圧力を掛ける前</b>(GDD02c §2.1)。</summary>
    public int MarketTerm { get; init; }

    /// <summary>相場基準が立ったか。false なら <see cref="MarketTerm"/> は 0 で、min から落ちる。</summary>
    public bool HasMarketTerm { get; init; }

    /// <summary>現金上限 = FloorDiv(用途に使える資金, max(1日分の数量, 1))。<b>常にある</b>(GDD02c §2.1)。</summary>
    public int CashCap { get; init; }

    /// <summary>利潤上限(GDD02c §2.3)。生産の入力にだけある。</summary>
    public int ProfitCap { get; init; }

    public bool HasProfitCap { get; init; }

    /// <summary>目標在庫。単位: 個。<b>耐久だけ耐久値</b>(GDD02b §2)。</summary>
    public int TargetStock { get; init; }

    /// <summary>予想在庫。単位は <see cref="TargetStock"/> と揃う(GDD02b §5.1)。</summary>
    public int ExpectedStock { get; init; }

    /// <summary>在庫圧力‰(GDD02b §5.1)。500〜1500、目標在庫の 2 倍超で 0。</summary>
    public int StockPressurePermille { get; init; }

    /// <summary>線形解の基礎値。相場項、無ければ現金上限(GDD02b §5.2)。<b>在庫圧力を掛けない</b>。</summary>
    public int BaseValue { get; init; }

    /// <summary>予算 = min( ApplyPermille(相場項, 在庫圧力‰) , 現金上限 , 利潤上限 )(GDD02c §2.1)。</summary>
    public int Budget { get; init; }
}

public readonly record struct HouseholdDemand
{
    /// <summary>GDD02b §3.2 の走査順(必需 → 耐久 → 生産の入力 → 嗜好、同一用途は品目 Id 昇順)に並んだ組。</summary>
    public IReadOnlyList<DemandLine> Lines { get; init; }

    /// <summary>必需の取り置き = Σ_(必需, 品目)(目標在庫 × 相場基準)(GDD02b §3.1)。単位: 貨幣。</summary>
    public long NecessityReserve { get; init; }

    /// <summary>運転資金 = Σ_(生産の入力, 品目)(目標在庫 × 相場基準)(同上)。単位: 貨幣。</summary>
    public long WorkingCapital { get; init; }
}
```

**`SurplusFunds` の欄は消す。** 母数は用途ごとに段階になったので、1 つの「余剰資金」では表せない(§5「母数の段階」)。

### 5. `BuyerBudget` の改訂(`Systems/BuyerBudget.cs`)

**削除**: `NecessityBaseValue` / `PreferenceBaseValue` / `DurableBaseValue` / `DerivedDemand` / `SurplusFunds` / 旧 `Budget` / 旧 `PurchaseQuantity` の実質コスト前提。

```csharp
/// <summary>
/// 買い手の在庫圧力‰(GDD02b §5.1)。
/// <c>在庫比‰ = CeilDiv(1000 × 予想在庫, 目標在庫)</c>、
/// <c>在庫圧力‰ = 在庫比‰ &gt; 2000 ? 0 : clamp(1500 − CeilDiv(在庫比‰, 2), 500, 1500)</c>。
/// </summary>
/// <remarks>
/// <b>目標在庫 ≤ 0 は 0 を返す(ゼロ除算しない)。</b>「1 単位も持ちたくない」であり、
/// 予想在庫が 0 でも 0 である。<b>旧実装はこの場合に 1000‰ を返していた</b> ── 目標 0 の品目に
/// 相場どおりの予算が立っていた。
/// <para>
/// <b>丸めの向きは売り手側(<see cref="OfferPrice.PriceCoefficientPermille"/>)と同じ。</b>
/// 在庫比‰ を切り上げてから引くので、式全体としては切り下げ方向になる。
/// </para>
/// </remarks>
public static int StockPressurePermille(int expectedStock, int targetStock);

/// <summary>現金上限 = FloorDiv( 用途に使える資金 , max(1日分の数量, 1) )(GDD02c §2.1)。</summary>
/// <remarks><b><paramref name="dailyQuantity"/> が 0 でもゼロ除算しない</b>のは max(…, 1) による。
/// 耐久は 1 個で呼ぶ(1日分が 1 個未満なので、GDD02c §2.1 の表が 1 と定めている)。</remarks>
public static int CashCap(int availableFunds, int dailyQuantity);

/// <summary>
/// 用途に使える資金(母数。GDD02c §2.1 / GDD02b §3.1)。<b>段階である</b> ──
/// 必需 = 流動資金 / 耐久・生産の入力 = 流動資金 − 必需の取り置き /
/// 嗜好 = 流動資金 − 必需の取り置き − 運転資金。<b>負なら 0</b>。
/// </summary>
public static int AvailableFunds(
    DemandPurpose purpose, int liquidFunds, long necessityReserve, long workingCapital);

/// <summary>
/// 摩耗費[1回] = CeilDiv( 仕入れ移動平均単価[工具] × 所要労働‰ , N × 1000 )(GDD02a §5)。
/// </summary>
public static int WearCostPerRun(int toolUnitCostAverage, int laborPermille, int toolLifeLaborDays);

/// <summary>
/// 利潤上限(GDD02c §2.3)。結果を <paramref name="profitCap"/> / <paramref name="hasProfitCap"/>
/// の<b>itemId 添字</b>の配列へ書く。<b>レシピの入力の品目だけを書き、他の添字に触れない。</b>
/// </summary>
/// <remarks>
/// <para>
/// <c>見込み収益 = 提示価格_出力[前日] × 出力数量</c>、
/// <c>許容原価合計 = FloorDiv(見込み収益 × 1000, 1000 + 最低利幅‰) − 摩耗費[1回]</c>、
/// <c>相場での原価 = Σ_k(相場基準_k × 必要数量_k)</c>、
/// <c>利潤上限_j = FloorDiv(許容原価合計 × 相場基準_j, 相場での原価)</c>。
/// </para>
/// <para>
/// <b>次のいずれかなら全入力の <paramref name="hasProfitCap"/> を false にする</b>(一部だけ
/// 按分しない。GDD02c §2.3):前日の出力提示価格が無い / <b>いずれかの</b>入力の相場基準が無い /
/// 許容原価合計 ≤ 0 / 相場での原価 ≤ 0。
/// </para>
/// <para>
/// <b>すべての除算を <see cref="IntegerMath.FloorDiv(long, long)"/> にする。</b>
/// <see cref="IntegerMath.ApplyPermille"/> を経由すると入力ごとに切り上がって合計が許容原価合計を
/// 超え、最低利幅‰ の保証が崩れる。<b>‰ 表現の按分比を挟まず、相場基準に直接比例させる</b>
/// (GDD02c §2.3)。善意で「‰ は ApplyPermille を通す」規約に揃えられる形なので注意する。
/// </para>
/// </remarks>
public static void ProfitCaps(
    Recipe recipe,
    bool hasPreviousOutputOfferPrice, int previousOutputOfferPrice,
    int minimumMarginPermille, int wearCostPerRun,
    bool[] hasInputReference, int[] inputMarketReference,
    bool[] hasProfitCap, int[] profitCap);

/// <summary>予算 = min( ApplyPermille(相場項, 在庫圧力‰) , 現金上限 , 利潤上限 )(GDD02c §2.1)。</summary>
/// <remarks><b>在庫圧力‰ を掛けるのは相場項だけである。</b>現金上限・利潤上限に掛けると、
/// 「払えない金を緊急性で払う」ことになる(GDD02c §2.1)。無い項は min から落とす。</remarks>
public static int Budget(
    bool hasMarketTerm, int marketTerm, int stockPressurePermille,
    int cashCap, bool hasProfitCap, int profitCap);

/// <summary>線形解の基礎値 = 相場項。無ければ現金上限(GDD02b §5.2)。</summary>
/// <remarks><b>在庫圧力を掛けない。</b>在庫圧力は線形解の形そのものに入っており、
/// 基礎値にも掛けると二重に効く(GDD02c §2.1)。</remarks>
public static int BaseValue(bool hasMarketTerm, int marketTerm, int cashCap);

/// <summary>
/// 購入量の線形解(GDD02b §5.2)。
/// <c>到達在庫 = clamp( 3 × 目標在庫 − CeilDiv(2 × 目標在庫 × 実効価格, 基礎値) , 0 , 2 × 目標在庫 )</c>、
/// <c>購入量 = max(0, 到達在庫 − 予想在庫)</c>。
/// </summary>
/// <remarks>
/// <b>旧実装は上側の clamp を持たず、下側だけを <c>max(0, …)</c> で押さえていた。</b>
/// 上側が無いと、実効価格が基礎値の 1/2 を下回る日に目標在庫の 2 倍を超えて買い溜める
/// (GDD02b §5.1 の「溜め込みの縮退」)。
/// <para><b>基礎値 0 のときの除算を避けるため、呼ぶ前にゲートを通す。</b>
/// <see cref="Decide"/> が唯一の想定呼び出し側である。</para>
/// </remarks>
/// <exception cref="ArgumentOutOfRangeException"><paramref name="baseValue"/> が 0 以下。</exception>
public static int PurchaseQuantity(int baseValue, int effectivePrice, int targetStock, int expectedStock);

/// <summary>
/// ゲートと線形解(GDD02b §5.2)。<b>購入量が 0 になった理由を返す。</b>
/// </summary>
/// <remarks>
/// <para>
/// <b>理由は <c>相場 → 利潤上限 → 現金上限</c> の固定順で、最初に当たったものを採る。</b>
/// min の argmin ではない ── 相場項と現金上限がともに実効価格を下回る日に argmin を採ると、
/// 「金が無限にあっても相場項で買わなかった」世帯が資金不足に数えられ、破産中フラグが
/// 価格ショックの検出器に化ける(GDD02b §5.2・§3.2)。<b>同点は相場が勝つ</b>(狭い側に倒す)。
/// </para>
/// <para>
/// ゲートを通っても購入量が 0 になることはある(到達在庫が予想在庫に届いた)。そのときの
/// 理由は <see cref="NoPurchaseReason.None"/> であり、資金不足ではない。
/// </para>
/// <para><b>基礎値が 0 の日はゲートが必ず閉じる</b>(実効価格 ≥ 1 &gt; 0 = 予算)ので、
/// <see cref="PurchaseQuantity"/> の除算に到達しない。<b>この保証は「実効価格が 1 以上」に
/// 立っている</b> ── 提示価格は床(外部買値、1 以上)以上なので成り立つ。</para>
/// </remarks>
/// <exception cref="ArgumentOutOfRangeException"><paramref name="effectivePrice"/> が 0 以下。</exception>
public static PurchaseDecision Decide(in DemandLine line, int effectivePrice);
```

### 6. `BuyerDemand` の改訂(`Systems/BuyerDemand.cs`)

**走査順を `必需 → 耐久 → 生産の入力 → 嗜好` に変える**([GDD02b §3.2](../03-gdd/02b-consumption-and-household.md))。同一用途の中は品目 Id 昇順。

**この順は 1 パスで組める** — 必需のループで取り置きが確定し、耐久はそれだけを要り、入力のループで運転資金が確定し、嗜好は最後にその両方を読む。

| 順 | 用途 | 目標在庫 | 予想在庫 | 1日分の数量 | 母数 |
| -- | ---- | -------- | -------- | ----------- | ---- |
| 1 | 必需 | `DailyConsumption.Lookahead(..., NecessityTargetStockDays[itemId])` | `HouseholdInventory[itemId]` | `DailyConsumption.Lookahead(..., days: 1)` | 流動資金 |
| 2 | 耐久(`Item.Tools` 1 行) | `ApplyPermille(ApplyPermille(ToolDurabilityPerUnit, ToolTargetStockPermille), RankCoefficientPermille[世帯主の階層])` | `工具在庫 × ToolDurabilityPerUnit − ToolWear` | **1** | 流動資金 − 取り置き |
| 3 | 生産の入力 | `definition.InputTargetStock(職業, itemId)` | `WorkshopInventory[itemId]` | `definition.DailyInputQuantity(職業, itemId)` | 流動資金 − 取り置き |
| 4 | 嗜好 | `DailyConsumption.Lookahead(..., PreferenceTargetStockDays[itemId])` | `HouseholdInventory[itemId]` | `DailyConsumption.Lookahead(..., days: 1)` | 流動資金 − 取り置き − 運転資金 |

- **相場基準は `MarketReference.TryBuyer`(遅い側)で引く。** 品目ごとに 1 回だけ引き、必需・入力・耐久の行と、取り置き・運転資金の両方が同じ値を読む。**`TrySeller` を呼んではならない** — 買い手が速くなると [GDD02c §1.2](../03-gdd/02c-price-and-budget.md) の天井が消える
- **誰の観測かは世帯主である。** `world.Knowledge[household.HeadNpcId]` を読み、`selfHouseholdId` には **`household.Id`** を渡す(`HeadNpcId` ではない。取り違えても例外が出ない)
- **取り置きと運転資金は、相場基準が立った品目だけを足す**([GDD02b §3.1](../03-gdd/02b-consumption-and-household.md))。立たない品目を現金上限で代用すると、母数が自己参照する
- **薪は必需の行と生産の入力の行に別々に立つ。** 目標在庫も予想在庫も別(世帯在庫 / 工房在庫)であり、取り置きは必需の薪、運転資金は入力の薪を数える
- **利潤上限は入力の行を組む前に `BuyerBudget.ProfitCaps` でまとめて求める。** 材料は前日の出力提示価格(呼び出し側が `Market.Clear()` の前に控えた値)・最低利幅‰・摩耗費・全入力の買い手側の相場基準
- **摩耗費は `BuyerBudget.WearCostPerRun(household.PurchaseUnitCostAverage[Item.Tools], recipe.LaborPermille, definition.ToolLifeLaborDays)`**

### 7. `DemandPurpose` の doc コメント(`Definition/DemandPurpose.cs`)

**enum の値は変えない。** 走査順を兼ねさせないことも変えない。**doc コメントだけを直す** — 現行は旧の走査順(必需 → 生産の入力 → 耐久 → 嗜好)と旧の基礎値(予算比率‰・派生需要)を書いており、**両方とも本タスクで消える**。

### 8. `WorldDefinition` の改訂(`Definition/WorldDefinition.cs`)

| 欄 | どうする |
| -- | -------- |
| `BudgetRatioPermilleByPurpose` | **消す**(コンストラクタ引数・検証・`M0` の値ごと)。用途別の予算比率‰ は現金上限に置き換わった([GDD02c §2.1](../03-gdd/02c-price-and-budget.md)) |
| `NecessityTolerancePermille` | **`TolerancePermille` に改名**。値は 1200 のまま。**全用途の相場項が使う** — 必需だけの値ではなくなった。**改名するのは、必需専用だと読める名前のまま全用途に配ると、片方の用途だけ別の値にしたくなったときに黙って全部動くからである** |
| `MinimumMarginPermille` | 欄は残す。**doc コメントを「原価下限 = ApplyPermille(原価, 1000 + これ)」から「利潤上限の許容原価合計([GDD02c §2.3](../03-gdd/02c-price-and-budget.md))」に直す**。値付けからは消えた |

**`InitialAcquisitionCost` は残す。** [GDD02a §5.1](../03-gdd/02a-production.md) の移動平均の初期値であり、摩耗費(= 利潤上限の材料)が工具の移動平均を読む。**初期価格のために要る、という理由だけが消えた**。

### 9. `TradeSystem` の改訂(`Systems/TradeSystem.cs`)

**段の構成(6 段)は変えない。** 変わるのは段1・段4・段5 の中身である。

**コンストラクタの検査**: 「入力 0 件のレシピ」を拒む検査は**原価のためだったので消す**。「出力 2 件以上」を拒む検査は**残す** — 利潤上限の `見込み収益 = 提示価格_出力[前日] × 出力数量` が出力 1 件を前提にしており、販売在庫がたまたま 0 の日だけ生き延びる失敗を避けるためにコンストラクタで拒む理由も変わらない。

**段1(値付け)**:

```
出力品目 = recipe.Outputs[0].ItemId
hasOwnPreviousOffer / ownPreviousOfferPrice を Market から控える(段4 の利潤上限が読む)
販売在庫 = WorkshopInventory[出力品目]        ← 0 なら売り注文を出さない(現行のまま)
床 = definition.ExternalBuyPrice(出力品目)
(hasSettled, settled) = MarketReference.TryPreviousDaySettledPrice(world.Ledgers[household.Id], 出力品目, world.Now)
hasReference = MarketReference.TrySeller(
    world.Knowledge[household.HeadNpcId], 出力品目, household.Id, world.Now,
    definition.ObservationRetentionDays, hasSettled, settled, out 相場基準)
提示価格 = hasReference
    ? OfferPrice.Calculate(床, 相場基準, 販売在庫, definition.ShipmentTargetStock(職業, 出力品目), household.IsBankrupt)
    : 床
```

- **`hasOwnPreviousOffer` は販売在庫 0 の世帯についても控える**(現行のまま)。その世帯も買い手として利潤上限を持つ
- **床は `ExternalBuyPrice`。** M0 の 5 職業はすべて都市生産品を出力するので例外は出ない。**1 次産品を出力するレシピを足したら例外で落ちる** — [GDD02d §2.1](../03-gdd/02d-external-market-and-money.md) がそれを許していないので、落ちるのが正しい

**段5(買い物)** — 手順 3・4・9 が変わる。

```
3. var decision = BuyerBudget.Decide(line, store.UnitEffectivePrice);   ← UnitRealCost ではない
   if (decision.Quantity <= 0)
   {
       // 経路(1): 現金上限のゲートで 0(GDD02b §3.2)
       if (line.Purpose == Necessity && decision.Reason == NoPurchaseReason.CashCap)
           household.UnaffordableNecessityCount++;
       continue;                                   ← 訪問には数えない
   }
4.(線形解は 3 に畳んだ)
5. 個数へ直す(PurchaseQuantityInUnits)
6. 0 以下ならこの line は終わり
7. 訪れた区画を控える
8. fundsCap = TradeSettlement.FundsCap(LiquidFunds, store.UnitEffectivePrice)
   actualQuantity = min(個数, fundsCap, 売り手の在庫)
9. // 経路(2): 資金上限の切り詰めで 0(GDD02b §3.2)
   if (line.Purpose == Necessity && fundsCap == 0) household.UnaffordableNecessityCount++;
10. actualQuantity >= 1 なら TradeSettlement.Execute
```

- **経路(1)は `continue` するので、同じ line が両方の経路で二重に数えられることは無い**
- **売り手の在庫が尽きて 0 個になったのは、どちらの経路でもない**(現行のまま。[GDD02b §3.3](../03-gdd/02b-consumption-and-household.md) の囲み)
- **店を 1 つも知らない(手順 2 で `continue`)もどちらでもない**

### 呼び出し側の配線(規則7)

本タスクが作るもののうち、**書き手または読み手が本タスクの外にあるもの**。契約を書く。

> **訂正(フェーズ2・2巡目の象限 I-b)。** 初版はここに「**テストが踏めないので**契約を書く」と書いていた。**これは広すぎた。** 下表のうち「踏める」と記した 3 行は、**両端がこの `TradeSystem.Step` の中にあるか、書き手が既存コードとして同じ Step の中で動く**ので、2 日走らせるパイプラインテストで踏める。広い保証は「見なくてよい」と読ませるので、**契約だけを書いた結果、その配線に検出器が 1 件も掛からなかった**([process/03-corrections](../process/03-corrections.md))。**「踏める」行は契約に加えてテストを書く**(別表 B-1・B-2)。

| 何 | 書く | 読む | 踏めるか | 約束 |
| -- | ---- | ---- | -------- | ---- |
| `UnaffordableNecessityCount` | 本タスク(順5 段5。**毎日 0 で上書きしてから加算**) | [#39](https://github.com/stama72/visionary/issues/39)(順3 の破産中フラグ) | 読み手が外 | 単位 件。**必需の (用途, 品目) の組ごとに最大 1** — 2 経路が同じ line で二重に立たない。**順3 は順5 より前にあるので、#39 が読む時点でその欄は前日の値である**([GDD02b §3.2](../03-gdd/02b-consumption-and-household.md)) |
| `IsBankrupt` | #39(順3) | 本タスク(順5 段1 の §1.4) | 書き手が外 | 0 / 1。**本タスクの時点では誰も 1 にしない** — 値付けの破産中の枝はテストが直接 1 を置いて踏む |
| 前日の約定単価 | `TradeSettlement`(既存。`Sale` の行)/ #38(輸出の行) | 本タスク(順5 段1) | **踏める**(書き手は既存だが同じ Step の段5 で動く。**別表 B-1**) | **`Ledgers` の `Sale` だけ。輸出を含む。** #38 が輸出の行を足すと、余剰を捌いた売り手の錨が床寄りに動く — それは [GDD02c §1.2](../03-gdd/02c-price-and-budget.md) が意図した挙動である |
| `store.UnitEffectivePrice` | `StoreChoice`(既存) | 本タスク(段5 手順 3・8) | **踏める**(表 #30 が踏んでいる) | **移動費を含まない単価。** #98 が `UnitRealCost` と店の選び方を改訂するとき、**予算の側は既に実効価格なので触らなくてよい** |
| `ProfitCaps` の `提示価格_出力[前日]` | 段1(`Market.Clear()` の**前**に控える) | 段4 | **踏める**(**両端とも本タスクの中**。**別表 B-2**) | **当日の値を渡すと同一 tick 内で循環する**([GDD08 §6.3](../03-gdd/08-household-and-decision.md)) |

## 落ちるべき条件(テスト)

**この節が完了条件そのもの。** 目標は「通ること」ではなく「**壊したときに落ちること**」である。

`WorldDefinition` を直接組み立ててよい(`EconomySystemTestFixtures.BuildDefinition` から `budgetRatioPermilleByPurpose` を落として使う)。**`BuildM0` の値に依存しないこと。**

| #  | テスト | 検証内容 | この実装ミスで落ちる | 核心 |
| -- | ------ | -------- | -------------------- | ---- |
| 1  | `BuyerReferenceAveragesEveryValidObservation` | 同一売り手の 3 件(10/20/60)+ 別売り手 1 件(10) → 買い手の相場基準 = `CeilDiv(100, 4)` = **25**。同じ入力で売り手側は `CeilDiv(60 + 10, 2)` = **35** | 買い手側を売り手ごとに畳む(35 になる)。売り手側を全件平均にする(25 になる)。**どちらの向きに写しても、天井(GDD02c §1.2)が消える** | **核心** |
| 2  | `ReferenceValidityIsSharedByBothSides` | 当日(差 0)・保持期間 + 1 日・品目違い・自分の売り注文 をそれぞれ 1 件ずつ混ぜ、保持期間ちょうど(差 = `retentionDays`)の 1 件だけが残ることを**売り手側と買い手側の両方**で確かめる | 判定を 2 か所に写して片側だけ `>` / `>=` を間違える。`Tick` の差で書く(同日の午後の観測が前日扱いになる) | **核心** |
| 3  | `SellerReferenceNeedsAnotherSeller` | 他の売り手の観測 0 件 + 自分の前日の約定単価あり → **false**(約定単価も使わない)。他の売り手 1 件を足すと true になり、約定単価が平均に入る | 0 件のときに約定単価だけで立てる(純粋な自己ループ)。0 を平均に足す |  |
| 4  | `PreviousDaySettledPriceIsQuantityWeighted` | 前日の `Sale` が (単価 10 × 2 個) と (単価 21 × 1 個) → `CeilDiv(41, 3)` = **14**。`Purchase` の行・前々日の行・品目違いを混ぜても 14。**輸出(相手 = `ExternalMarketSellerId`)の行は含める** | 件数平均にする(`CeilDiv(31, 2)` = 16)。`Purchase` を数える。輸出を除外する(GDD02c §1.2 が名指しした誤り) | **核心** |
| 5  | `PreviousDaySettledPriceIsAbsentWithoutYesterdaySale` | 前日の売りが 0 件 → **false**、`settledPrice` は 0。当日の売りだけがある場合も false | 当日を前日に数える(同一 tick 内の循環)。0 を返して true にする |  |
| 6  | `OfferPriceFloorIsExternalBuyPrice` | 相場基準 100・在庫比 2000‰(係数 500‰)→ `ApplyPermille(100, 500)` = 50。床 = **80** なら提示価格 **80**、床 = 30 なら **50** | `Math.Min` で書く。床を原価から作る(#96 で消した経路が戻る) | **核心** |
| 7  | `OfferPriceIsFloorWithoutReference` | 相場基準が立たない日の提示価格が**床ちょうど**。販売在庫・出荷目標在庫をどう動かしても床のまま | 相場基準 0 で `Calculate` を呼ぶ(0 × 係数 = 0 になり、床が `max` で拾っても在庫の影響が消えたことに気付けない) |  |
| 8  | `BankruptSellerFixesCoefficientAtFivehundred` | 破産中 = 1・相場基準 100・**販売在庫 0**(健全なら係数 1500‰ = 150)→ **50**。床 30 のとき 50、床 **80** のとき **80**(床は破らない) | 在庫比を見てから係数を捨てる(150 が出る)。床を破って 50 を返す。`isBankrupt` に 2 を渡して破産扱いにする(値域を閉じる) | **核心** |
| 9  | `StockPressureIsLinearFromFivehundredToFifteenhundred` | 目標 10 で 予想 0 → **1500**、5 → 1250、10 → **1000**、20 → **500**、21 → **0**。目標 **0** は予想 0 でも **0** | 目標以下を 1000‰ で据え置く(旧実装。予想 0 が 1000 になり §5.1 の 1500 の行が死ぬ)。2000‰ 超を 500 で止める(半値なら無限に買う)。目標 0 で 1000 を返す | **核心** |
| 10 | `CashCapDividesAvailableFundsByDailyQuantity` | 資金 100・1日分 3 → `FloorDiv` で **33**。1日分 **0** → **100**(max(…,1))。資金 2・1日分 3 → **0** | `CeilDiv` で書く(34 になり 1 日分が買えないのに買えると判定する)。max(…,1) を落としてゼロ除算 |  |
| 11 | `AvailableFundsAreStagedByPurpose` | 流動資金 1000・取り置き 300・運転資金 400 → 必需 **1000** / 耐久 **700** / 入力 **700** / 嗜好 **300**。取り置き 1200 なら耐久・入力・嗜好とも **0**(負を 0 で止める) | 全用途に流動資金を配る(黒字倒産が戻る)。嗜好から運転資金を引き忘れる。`max(0, …)` を落として負の母数が `FloorDiv` で負の現金上限を作る | **核心** |
| 12 | `WearCostPerRunUsesLaborNotRuns` | 工具の移動平均 260・所要労働 108‰・N = 13 → `CeilDiv(28080, 13000)` = **3** | `N` に 1000 を掛けない(28080 ÷ 13 = 2161)。`FloorDiv` にする(2) |  |
| 13 | `ProfitCapsAllocateProportionallyToReference` | 前日の出力提示価格 120・出力数量 2・最低利幅 200‰・摩耗費 4 → 許容原価合計 = `FloorDiv(240000, 1200) − 4` = **196**。入力が (A: 相場 10 × 必要 2) と (B: 相場 30 × 必要 1) で相場での原価 50 → 利潤上限 A = `FloorDiv(196 × 10, 50)` = **39**、B = `FloorDiv(196 × 30, 50)` = **117**。**`39 × 2 + 117 × 1 = 195 ≤ 196`** | 按分を `ApplyPermille` で経由する(切り上がって合計が 196 を超え、最低利幅の保証が崩れる)。摩耗費を引かない(利潤上限が 200 / 120 になる) | **核心** |
| 14 | `ProfitCapIsAbsentWhenAnyInputLacksReference` | 2 入力のうち **1 つ**の相場基準が無い → **両方とも `hasProfitCap` が false**。前日の出力提示価格が無い日・許容原価合計 ≤ 0(摩耗費が許容原価を食い切る)の日も同じ | 観測できた入力だけで按分する(2 入力ぶんの上限を 1 入力が丸ごと受け取る)。フォールバック値を入れる(#85 で廃止した規則が戻る) | **核心** |
| 15 | `ProfitCapOfSingleInputRecipeEqualsAllowedCostPerQuantity` | 1 入力(必要数量 3)のレシピで 利潤上限 = `FloorDiv(許容原価合計, 3)` に一致する | 相場基準で按分してから必要数量で割る順を取り違える |  |
| 16 | `BudgetAppliesStockPressureToMarketTermOnly` | 相場項 100・在庫圧力 1500‰・現金上限 **200**・利潤上限なし → **150**。現金上限 **120** なら **120**(相場項に圧力が乗った 150 より小さい側) | 現金上限にも圧力を掛ける(`min(150, ApplyPermille(120, 1500) = 180)` = **150** になり 120 にならない。払えない金を緊急性で払う)。相場項に圧力を掛け忘れる(100 に頭打ちして §5.1 の 1500 の行が死ぬ) | **核心** |
| 17 | `BudgetDropsAbsentTerms` | 相場項なし・現金上限 50 → **50**。利潤上限あり(30)なら **30**。**3 項とも無いことは無い**(現金上限は常にある) | 無い項を 0 として min に入れる(予算が常に 0)。無い項を `int.MaxValue` で書いて `HasProfitCap` を読み忘れる |  |
| 18 | `BaseValueFallsBackToCashCapWithoutMarketTerm` | 相場項 100 あり → **100**(在庫圧力 1500‰ でも 100 のまま)。相場項なし・現金上限 50 → **50** | 基礎値に在庫圧力を掛ける(150。線形解で二重に効く) | **核心** |
| 19 | `PurchaseQuantitySolvesTheLinearDemand` | 目標 10・予想 0・基礎値 100 で 実効価格 **150** → **0**、**100** → **10**、**50** → **20**(上限)、**40** → **20**(2 倍で頭打ち) | 上側の clamp を落とす(実効価格 40 で **22** になり溜め込みが縮退する)。`CeilDiv` を `FloorDiv` にする | **核心** |
| 20 | `DecideReportsMarketTermBeforeCashCap` | 相場項 100・圧力 1000‰・現金上限 **50**・実効価格 **120** → 数量 0、理由 **`MarketTerm`**(現金上限のほうが小さくても)。実効価格 **80** なら理由 **`CashCap`**。**相場項と現金上限がともに 80 で実効価格 100 のときも `MarketTerm`**(同点は相場) | argmin を採る(120 のとき `CashCap` になり、破産中フラグが価格ショックの検出器に化ける)。同点を現金上限に倒す | **核心** |
| 21 | `DecideReportsProfitCapBetweenMarketAndCash` | 相場項 100・圧力 1000‰・利潤上限 **60**・現金上限 **40**・実効価格 **80** → 理由 **`ProfitCap`**。実効価格 **50** なら **`CashCap`** | 順を入れ替える。利潤上限を見ない(生産の入力で「相場では儲からないのに買う」) |  |
| 22 | `DecideReportsNoneWhenGateOpens` | ゲートを通って数量が正 → `None`。**目標 10・予想 20・基礎値 100・実効価格 50**(圧力 500‰ で予算 50、ゲートは等号で開く。到達在庫 20 = 予想在庫)→ 数量 0 で **`None`**(資金不足ではない) | 数量 0 をすべて資金不足に数える(在庫が満ちた世帯で必需のフラグが立つ) | **核心** |
| 23 | `DemandLinesFollowNecessityDurableInputPreference` | 薪(必需)・パン(必需)・工具(耐久)・穀物(入力)・薪(入力)・ビール(嗜好)を持つ世帯で、`Lines` の `(Purpose, ItemId)` の並びが **必需の品目 Id 昇順 → 耐久 → 入力の品目 Id 昇順 → 嗜好** ちょうどであること | 旧の順(必需 → 入力 → 耐久 → 嗜好)のまま。`recipe.Inputs` の並びをそのまま使う(`Recipe` は昇順を保証しない) | **核心** |
| 24 | `FirewoodGetsSeparateNecessityAndInputLines` | 薪が必需と入力の **2 行**に立ち、目標在庫と予想在庫がそれぞれ世帯在庫 / 工房在庫から来ること(片方だけを変えるともう片方の行が動かない) | 品目で 1 行に畳む(暖房用の薪が生産用の在庫圧力を下げる) | **核心** |
| 25 | `ReservesSkipItemsWithoutReference` | 必需 2 品目のうち 1 つだけ相場基準がある世帯で、`NecessityReserve` が**その 1 品目ぶんだけ**。入力側も同じく `WorkingCapital` が立った品目ぶんだけ | 現金上限で代用する(母数が自己参照する)。0 として足す(結果は同じだが、相場が立った日に黙って変わる ── **この変異は #25 では落ちないので、doc コメントで規約を固定する**) |  |
| 26 | `DemandUsesBuyerReferenceNotSeller` | 同一売り手の観測が 3 件ある世帯で、`DemandLine.MarketTerm` が**全件平均**から作られていること(#1 の買い手側の値を使う) | `MarketReference.TrySeller` を呼ぶ(天井が消える)。`hasOwnPreviousPrice: true` で呼ぶ | **核心** |
| 27 | `NecessityShortfallIsCountedOnBothPaths` | 必需 1 品目で (1) 現金上限が実効価格を下回ってゲートで 0 → `UnaffordableNecessityCount == 1`、(2) ゲートは開くが `FundsCap` が 0 に切り詰める → **同じく 1**。**同じ line で 2 にならない** | 経路(1)を数えない(旧実装。冬の薪で資金が尽きた世帯が困窮と判定されない)。経路(1)の後に `continue` せず経路(2)でも数えて 2 になる | **核心** |
| 28 | `TooExpensiveIsNotCountedAsShortfall` | 必需で相場項が実効価格を下回ってゲートが閉じた日 → **`UnaffordableNecessityCount == 0`**。売り手の在庫が 0 で約定できなかった日・店を 1 つも知らない日も **0** | 理由を見ずに「買えなかった」で数える(価格が跳ねた日に全世帯のフラグが立つ) | **核心** |
| 29 | `NecessityIsSettledBeforePreference`([#81](https://github.com/stama72/visionary/issues/81)) | **流動資金が「必需 1 日分」と「嗜好 1 日分」の片方しか払えない帯**に置いた世帯で、必需が約定し `UnaffordableNecessityCount == 0`、嗜好の約定が 0 個であること。**`Lines` を逆順(嗜好 → 必需)に並べ替えると、嗜好が約定して必需が `FundsCap` で 0 に切られ、`UnaffordableNecessityCount == 1` になる** | `Lines` の並べ替え。段5 が `Lines` の順ではなく品目 Id 順に走査する | **核心** |
| 30 | `BudgetGateUsesEffectivePriceNotRealCost` | 移動費が乗る区画の店で、`UnitRealCost > 予算 ≥ UnitEffectivePrice` になる配置 → **約定する**(数量は実効価格で解いた値) | `store.UnitRealCost` を渡したまま(空間の摩擦が二重計上になる) | **核心** |
| 31 | `WorldDefinitionHasNoBudgetRatios` | `TolerancePermille` が 1200 で、**全用途の相場項がこれを使う**(必需と嗜好で同じ値になる)。`budgetRatioPermilleByPurpose` の引数が無いこと(コンパイルで担保) | 必需だけに許容乖離を掛け、他の用途を素の相場基準にする |  |
| 32 | `TradePipelineStillRunsDeterministically` | (既存を維持)同じシードで 2 回走らせると状態ハッシュが一致する。`TradeSystem` が乱数を引かない | `SortedDictionary` を `Dictionary` に戻す。帳簿の走査で列挙順に依存する |  |

**「核心」印(#1・#2・#4・#6・#8・#9・#11・#13・#14・#16・#18・#19・#20・#22・#23・#24・#26・#27・#28・#29・#30)は多い。** [process/02](../process/02-task-spec.md) の「1 タスクあたり 2〜3 件」を大きく超えるので、**実際に変異を当てるのは次の 6 件に絞る**:

| 印を当てる | なぜこの 6 件か |
| ---------- | --------------- |
| #1 | 売り手と買い手の速さの取り違え。**天井が消えても A/B 比較の差分に現れない**(GDD02c §1.2) |
| #9 | 在庫圧力の下側の傾き。旧実装がここを据え置いていたので、**善意の「元に戻す」が起きうる** |
| #13 | 按分の丸め。`ApplyPermille` に揃える変異は規約に沿って見えるので通りやすい |
| #16 | 在庫圧力をどの項に掛けるか。**本タスクの主題そのもの** |
| #20 | 理由の判定順。argmin は自然な書き方であり、壊れても緑のまま通る |
| #29 | 走査順の検出器([#81](https://github.com/stama72/visionary/issues/81))。**判別力が無いことが実測されて立った issue なので、判別力を実測で示さないと閉じられない** |

**残りの「核心」印は「壊れたときの影響が大きい」ことだけを示す** — 変異は当てず、レビュアーが読む優先度として使う。

### #29 の帯の作り方([#81](https://github.com/stama72/visionary/issues/81) が引き継いだ事実)

**#81 が「判別力のある帯が見つからない」と報告した原因は、旧の基礎値が用途ごとに別の母数(流動資金 / 余剰資金)から来ていたことである。** 本タスクで母数が段階(流動資金 → 取り置きを引く → 運転資金も引く)になり、**相場項の形が全用途で同じ**になったので、帯が作れるようになった。

**帯を作る条件**:

- **取り置きと運転資金を 0 に近づける。** 相場基準が立った品目しか足されない([GDD02b §3.1](../03-gdd/02b-consumption-and-household.md))ので、**観測を必需 1 品目と嗜好 1 品目にだけ配れば、取り置きはその必需 1 品目ぶんだけ、運転資金は 0 になる**
- **流動資金を「必需 1 日分は買えるが、必需 + 嗜好 1 日分は買えない」額に置く**
- **耐久と入力の行は店が見つからないので手順 2 で `continue` する**(観測を配らないため)。資金を消費しない
- **1 日目で成立する。** #81 が「2 日目以降でないと」と書いたのは旧の基礎値の話であり、本タスクでは観測を直接置けば 1 日目の段5 で判定できる

**逆順の変異は `Lines` を `Reverse()` して段5 へ渡すことで当てる。** 実装本体を書き換えずに済むよう、テスト側が `HouseholdDemand` を組み立てて段5 相当を呼べる形にしておくこと(段5 を `private` のまま残すなら、`Lines` の並びを変えた `BuyerDemand` の派生を使うのではなく、**`TradeSystem.Step` を通して `BuyerDemand` の出力順だけが違う状態を作る**)。

## 別表: レビューで足した「落ちるべき条件」(フェーズ2)

**上の表は実装に渡した時点の指示であって最終形ではない。** レビューで足りないと分かったぶんをここに足す。**上の表は書き換えない** — どこまでが凍結時点の仕様で、どこからがレビューの産物かが読めなくなるため。

### A. 判別力の補強(1巡目)

いずれも**上の表が自分で名指しした変異が、実際には落ちなかった**もの。式は正しく、テストが変異を通していた。

| #  | テスト | 落ちなかった変異 | なぜ通っていたか |
| -- | ------ | ---------------- | ---------------- |
| A-1 | `PurchaseQuantitySolvesTheLinearDemand` に**割り切れない**組を追加(表 #19) | `CeilDiv` → `FloorDiv` | 4 ケースの被除数が**すべて基礎値で割り切れて**いた。あわせて「確認した」と誤って書いていた doc コメントを訂正した |
| A-2 | `DurableLineIsMeasuredInDurabilityNotUnits` / `DurableTargetUsesTheHeadRankNotAMemberRank` / `DurableTargetActuallyAppliesTheRankCoefficient`(表 §6 順2) | 階層係数‰ を落とす / 世帯主でなく構成員の階層を採る / `− ToolWear` を落とす / 個数のまま予想在庫にする | 耐久行の値を見るテストが 1 件も無く、**並び位置しか見ていなかった**。3 件目が要るのは、世帯主が Master(係数 1000‰)だと `ApplyPermille(x, 1000) == x` で**恒等元になり係数の掛け忘れが通る**ため |
| A-3 | `DemandExcludesTheHouseholdIdNotTheHeadNpcId`(表 §6) | `TryBuyer` の `selfHouseholdId` に `HeadNpcId` を渡す | 買い手側のテストが**すべて `headNpcId == id == 0` のフィクスチャ**で、2 つの Id が常に一致していた。本番世界では `masterId = householdId * 2` で一致しない |
| A-4 | `BakerLikeRecipe` の入力を**降順**に変更(表 #23) | 品目 Id 昇順ループをやめて `recipe.Inputs` を直接回す | レシピの入力が**既に昇順**で、両者が同じ並びを出していた |
| A-5 | `SellerFoldKeepsTheLaterEntryOnATie`(表 §1) | 畳み込みのタイブレーク `>=` → `>` | 観測が**すべて別 tick** で、同一 tick の重複が 1 件も無かった |
| A-6 | `CashCapReflectsTheStagedAvailableFundsPerPurpose`(表 #11) | 嗜好の母数から運転資金を引かない | `AvailableFunds` の**単体**しか試しておらず、`DemandLine.CashCap` を見るアサートが 1 件も無かった |

### B. 配線の検出器(2巡目・上の §「呼び出し側の配線(規則7)」の I-b 訂正に対応)

**どちらも「テストが弱い」ではなく「テストが 1 件も無い」**。上の表が一度も触れていないので、表を読む限り見えない。**2 日走らせるパイプラインテスト**で踏む。

| #  | テスト | 落ちるべき変異 | 核心 |
| -- | ------ | -------------- | ---- |
| B-1 | **売り手の錨(前日の約定単価)がパイプラインで効いていること。** 1 日目に**実際に約定する**売り手を作り、2 日目の提示価格が「他の売り手の値だけ」から作った値と**異なる**こと | 段1 が `MarketReference.TrySeller` へ渡す `hasSettled, settledPrice` を `false, 0` に置換 | **核心。** 自分を錨に含めるのは [GDD02c §1.2](../03-gdd/02c-price-and-budget.md) の囲みが言う**売り手2世帯の交互振動モードを消す**仕掛けである。落ちると交互振動が出るが、それは「機構の帰結」として読めてしまい、[GDD02 §8](../03-gdd/02-economy.md)-1 の実測が**別のものを測ったまま結論を出す** |
| B-2 | **利潤上限が需要行に届いていること。** 生産の入力の `DemandLine.ProfitCap` / `HasProfitCap` が立ち、**利潤上限がゲートを閉じる帯**で入力の約定が 0 になること | (a) `BuyerDemand` が `hasProfitCap` / `profitCap` を渡さない (b) 摩耗費の材料 `PurchaseUnitCostAverage[Item.Tools]` を 0 にする (c) 段1 が段4 へ `提示価格_出力[前日]` を渡さない | **核心。** 届かないと生産者は「相場では儲からない値」でも入力を買い続け、[GDD02c §2.3](../03-gdd/02c-price-and-budget.md) の仕入上限が事実上無効になる。最低利幅‰ の保証は `ProfitCaps` の中だけで成立し**世界に出ない** |

**`TradeSystemTests.SellerAnchorsOnSettledPriceNotOnItsOwnPreviousOffer` の doc コメントも直す。** 「パイプラインで確かめる」と書いているが、実際に確かめているのは**約定が無い日は錨を使わない**という半分だけである。広い保証は次に読む者に確認をやめさせる([process/03-corrections](../process/03-corrections.md))。

## 編集してよい文書

worktree をまたいだ所有権の宣言。**ここに挙がっていない文書は触らない。**

- `docs/tasks/W2-08-offer-price-and-budget.md`(本書。**フェーズ2 は「別表」の追記だけ**)
- `docs/tasks/W2-08-offer-price-and-budget.handoff.md`(引き継ぎメモ)

**GDD / TDD / ADR は触らない。** 本タスクが要る改訂はフェーズ1 が先行コミットで入れてある。**触ると `SPEC-OUTSIDE` で止まる**([ADR-0010](../adr/0010-phase-pipeline-and-halt-conditions.md))。仕様に穴を見つけたら**止まって報告する**。

## このタスクで特に効く規約

- **除算の向きを式ごとに確かめる。** 本タスクは `CeilDiv` と `FloorDiv` が近接して現れる — 相場基準の平均・約定単価・在庫比・摩耗費は**切り上げ**、現金上限・利潤上限の按分・線形解の `到達在庫` は**切り下げ**である。[GDD01 §2.3](../03-gdd/01-trust-and-conversation.md) の「全計算式切り上げ」を機械的に当てると、**利潤上限の合計が許容原価合計を超え**(#13)、**現金上限が 1 日分を買えないのに買えると判定する**(#10)
- **`long` で積む。** 相場基準の合計・約定単価の合計・按分の中間積・在庫比の分子は `int` を超えうる
- **列挙順。** 売り手ごとの畳み込みは `SortedDictionary`。帳簿と観測は `List` の並びをそのまま走るが、**結果が合計と件数にしか依らない**ことを確かめてから使う

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **上の 6 件に変異を当てて落ちることを確認し、当てた変異と結果を doc コメントかコミット本文に残した**
- [ ] **旧の規則が消えている**: `OfferPrice.UnitCost` / `OfferPrice.CostFloor` / `BuyerBudget.DerivedDemand` / `NecessityBaseValue` / `PreferenceBaseValue` / `DurableBaseValue` / `SurplusFunds` / `WorldDefinition.BudgetRatioPermilleByPurpose`
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
