# W2-15: 相場項が無い日の基礎値を窓口の当日価格にする

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#170](https://github.com/stama72/visionary/issues/170)              |
| 根拠     | [GDD02b §5.2](../03-gdd/02b-consumption-and-household.md) / [GDD02c §2.1](../03-gdd/02c-price-and-budget.md) / [GDD02d §2.1・§3・§5](../03-gdd/02d-external-market-and-money.md) |
| ブランチ | `feat/170-window-price-base-value`                                   |
| worktree | 本体ツリー(パイプライン)                                           |

**仕様は [#120](https://github.com/stama72/visionary/issues/120) の決定10・11・12 で凍結済み。** 決定の本文・却下した案・追随表は #120 のコメントが持ち、本書はそれを実装の形に落とすだけである。**設計判断が要ると気づいたら実装せずに止まる**(`PIPELINE: HALT SPEC-OUTSIDE`)。

## 前提 — 引用先は master にある

**#120 の GDD(決定10・11・12)は PR #175 で master に入った**(`7377df3`)。**フェーズ1 が着手時に確認している** — 未マージの設計ブランチに残っていると、implementer が読む GDD と本書が正面から食い違う([#154](https://github.com/stama72/visionary/issues/154) で実際に踏んだ形)。

### フェーズ1 が凍結前に直した GDD(`efd8a3a`)

| 何が偽だったか | どこ |
| -------------- | ---- |
| 決定11 の取り残し(「予算 = 相場項・現金上限・利潤上限の min」)。**破産中フラグの経路そのもので、本タスクの implementer が必ず読む** | GDD02b §3.2 / §5.2 の3分岐の説明 / §7 の役割分担の表 |
| 「`ApplyPermille(基礎値, 在庫圧力‰)` は線形解の零点に**恒等的に**等しい」。整数の丸めの後では厳密でない | GDD02b §5.2 / GDD02c §2.1 |

**2つ目は #170 本文も同じ言い方をしている。** 正確には次である。**結論(検出器を2本に割る)は変わらない。**

- 在庫圧力‰ は切り下げ方向に丸まるので、**ゲートは線形解の零点より 1 だけ手前で閉じうる**
- 発火には `基礎値 1000 以上 かつ 目標在庫 500 以上` が同時に要る(反例: 目標 667・予想 55・基礎値 50,000 → 在庫圧力 1458‰・第1項 72,900 に対し、実効価格 72,901 の線形解は 1 を返す)
- **M0 では到達しない。** 相場項が無い日の基礎値の最大は 290(工具の外部買値)で、目標在庫が 500 を超えるのは耐久の行(耐久値で 6,500)だけだからである。M0 に立ちうる組を全数当てて反例 0 を確認した(2026-09-22)
- **相場項がある日は決定11 の前から同じ丸めである。** 本タスクは帯を広げない

## スコープ

**含む:**

1. **相場項が無い日の基礎値を窓口の当日価格にする**(決定10)。都市生産品は外部買値、1次産品は当日の外部売値
2. **予算の第1項の材料を「相場項」から「基礎値」へ一般化する**(決定11)。第1項は常にある
3. **購入量0の理由の分岐1 から「相場項があり」を外す**(決定11)
4. 上の3つが変えた doc コメントの追随(下の表)と、旧規則を固定している既存テストの書き換え

**含まない:**

- **現金上限を min から外すこと。** 第1項と並んで残る — [02d §4.2](../03-gdd/02d-external-market-and-money.md) の貨幣から需要への自動調節はこの項が担う
- **外出の見積もりを変えること。** 1次産品の見積もりの下限 1([02d §2.2](../03-gdd/02d-external-market-and-money.md))はそのまま。**変えるのは「いくつ買うか」だけで、「どこへ行くか」の見積もりは知識に依存したままである**([GDD02b §5.2](../03-gdd/02b-consumption-and-household.md))
- **都市が死ぬことを直すこと。** 決定10・11 を入れても 30 日で都市は死ぬ。**本タスクが直すのは初日の買い込みだけである。** 生存の検出器は [#173](https://github.com/stama72/visionary/issues/173)、原因は [#30](https://github.com/stama72/visionary/issues/30) / [GDD02d §2.4](../03-gdd/02d-external-market-and-money.md) / [#174](https://github.com/stama72/visionary/issues/174) が引き取る
- **季節の移動平均**([#172](https://github.com/stama72/visionary/issues/172))。M0 は当日値である(決定12)
- **`NoPurchaseReason.MarketTerm` の改名**(引き継ぎメモの却下1)
- **GDD / TDD / ADR の書き換え。** フェーズ1 が `efd8a3a` で終えている

### 変えない既存コード(規則8。[02-task-spec](../process/02-task-spec.md))

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `BuyerBudget.PurchaseQuantity`(到達在庫の clamp と線形解) | [GDD02b §5.2](../03-gdd/02b-consumption-and-household.md) | **一致。** 式は基礎値を引数で受けるだけで、基礎値の作り方には依存しない |
| `BuyerBudget.StockPressurePermille`(500〜1500、2000‰ 超で 0、目標0 で 0) | [GDD02b §5.1](../03-gdd/02b-consumption-and-household.md) | **一致。** 決定10・11 は在庫圧力に触れない |
| `BuyerBudget.CashCap` / `AvailableFunds`(母数の段階) | [GDD02c §2.1](../03-gdd/02c-price-and-budget.md) | **一致。** 現金上限は min に残る(「含まない」1件目) |
| `BuyerBudget.ProfitCaps`(利潤上限と、一部だけ按分しない枝) | [GDD02c §2.3](../03-gdd/02c-price-and-budget.md) | **一致。** 第3項は触らない |
| `MarketReference.TryBuyer`(買い手側の遅い相場基準・有効性は前日まで) | [GDD02c §1.2](../03-gdd/02c-price-and-budget.md) | **一致。** 初日に相場基準が立たないことが本タスクの前提そのものである |
| `ErrandPlanner.SurplusFor` の支払い意思額 `ApplyPermille(line.BaseValue, line.StockPressurePermille)` | [GDD06 §3](../03-gdd/06-trade-and-negotiation.md) | **一致。式は変えない。** 相場項が無い日に値が小さくなるのは基礎値が変わった帰結であり、GDD06 §3.1 が「探索の動機は残るが旧版が見込んだほど大きくない」と書いた状態そのものである。**doc に1文だけ足す**(下の表) |
| `ErrandPlanner.EstimateWindowPrice`(今日の知覚 → 有効な記憶 → 床。1次産品は `UnknownPriceFloor` = 1) | [GDD02d §2.2](../03-gdd/02d-external-market-and-money.md) | **一致。** §2.2 は「1次産品の見積もりは 1」「都市生産品はこの例外に入らず床で見積もる」と書いており、本タスクの窓口価格(1次産品は**外部売値**)とは**別の値である**。**取り違えないこと** — 見積もりは知識に依存し、基礎値は依存しない |
| `ExternalMarket.TryOfferPrice`(窓口の提示価格 = 当日の外部売値。都市生産品では天井) | [GDD02d §2.1・§3](../03-gdd/02d-external-market-and-money.md) | **一致。** 窓口が**売る**値であり、都市生産品の基礎値(外部買値 = 床)とは別である |
| `TradeSystem` の破産中フラグ(`NoPurchaseReason.CashCap` だけを資金不足に数える) | [GDD02b §3.2](../03-gdd/02b-consumption-and-household.md) | **一致。** 分岐1 の一般化は分岐3 に落ちる母数を**狭める**方向に働くが、規則そのものは変わらない(相場で買わなかった世帯を資金不足に数えないため) |
| `WorldDefinition.ExternalBuyPrice` / `ExternalSellPrice` / `IsPrimaryItem` | [GDD02d §2.1・§3・§5](../03-gdd/02d-external-market-and-money.md) | **一致。読み口を足さない** — 本タスクは既存の2つを `IsPrimaryItem` で振り分けて呼ぶだけである |

## 作るもの

### 1. `BuyerDemand` — 窓口の当日価格を解いて渡す

`BuyerBudget` は `WorldDefinition` を受け取らない純関数なので、**窓口価格を解くのは `BuyerDemand` だけである。**

```csharp
/// <summary>
/// 窓口の当日価格(GDD02b §5.2 / GDD02d §2.1・§3・§5)。相場項が無い日の線形解の基礎値。
/// 都市生産品は外部買値(= 床。季節に依らない)、1次産品は当日の外部売値。
/// </summary>
private int WindowPrice(int itemId, Season season) =>
    _definition.IsPrimaryItem(itemId)
        ? _definition.ExternalSellPrice(itemId, season)
        : _definition.ExternalBuyPrice(itemId);
```

- **都市生産品に `ExternalSellPrice` を使ってはならない。** 都市生産品のそれは**天井**(`ApplyPermille(外部買値, 1000 + 交易マージン‰)` = M0 では床の2倍)であって床ではない。M0 ではパンの基礎値が 54 → 108 になり、**窓口から都市生産品を買う帯が開いて #120 の初日の一撃が部分的に戻る**
- **1次産品に `ExternalBuyPrice` を使ってはならない。** `WorldDefinition` が `ArgumentException` を投げる(こちらは気付ける)
- **基準値ではなく当日値である**(決定12)。`ExternalSellPrice` が季節係数を掛けた後を返すので、**季節を渡すことがその実体である**
- 季節は `GameDate.FromTick(world.Now).Season`
- 品目ごとに1回だけ解き、`BuildLine` へ渡す。**添字は itemId**(`hasReference` / `reference` と同じ形に揃える)

`BuildLine` の署名に `int windowPrice` を足し、`BuyerBudget.BaseValue` へ `cashCap` の代わりに渡す。**`cashCap` は現金上限の項としてそのまま残る。**

### 2. `BuyerBudget.BaseValue` — 第3引数を窓口価格にする

```csharp
/// <summary>線形解の基礎値 = 相場項。無ければ窓口の当日価格(GDD02b §5.2)。<b>常に1以上</b>。</summary>
public static int BaseValue(bool hasMarketTerm, int marketTerm, int windowPrice)
```

- **引数名を `cashCap` のまま残さない。** 名前が旧規則を指し続ける
- **`windowPrice <= 0` なら `ArgumentOutOfRangeException`**(`hasMarketTerm` の真偽によらず、無条件に検査する)。基礎値 0 の枝が到達不能になったことを、doc ではなく例外で主張する
- **残る穴**(規則4): 防げるのは「窓口価格が 0 のまま渡る」ことだけで、**「間違った窓口価格(天井や基準値)が渡る」ことは防げない。** そちらはテスト3 が見る
- 在庫圧力を掛けないことは現行のまま

### 3. `BuyerBudget.Budget` — 第1項を常に立てる

```csharp
/// <summary>予算 = min( ApplyPermille(基礎値, 在庫圧力‰) , 現金上限 , 利潤上限 )(GDD02c §2.1)。</summary>
public static int Budget(int baseValue, int stockPressurePermille, int cashCap, bool hasProfitCap, int profitCap)
```

- **`hasMarketTerm` / `marketTerm` の引数は消す。** 残すと「第1項は常にある」が引数の形と食い違う
- **在庫圧力‰ が 0 の日は第1項が 0 になり、予算も 0 になる。これは仕様どおりである**(目標在庫の2倍超は買わない。[GDD02b §5.1](../03-gdd/02b-consumption-and-household.md))。相場項が無い日にも同じになるのが決定11 の帰結で、`ErrandPlanner` の需要リストの `line.Budget > 0` のフィルタがその行をこれまでより早い段階で落とす。**外出の判断は変わらない** — 旧形でも支払い意思額が `ApplyPermille(基礎値, 0) = 0` で余剰は恒に 0 だった(フェーズ1 が確認)

### 4. `BuyerBudget.Decide` — 分岐1 から `HasMarketTerm` を外す

```csharp
int adjustedBaseValue = IntegerMath.ApplyPermille(line.BaseValue, line.StockPressurePermille);

if (effectivePrice > adjustedBaseValue)
{
    return new PurchaseDecision { Quantity = 0, Reason = NoPurchaseReason.MarketTerm };
}
```

- 判定の順(相場 → 利潤上限 → 現金上限)と、同点が 1 に倒れることは**変わらない**
- **`NoPurchaseReason.MarketTerm` の名前は変えない。** doc を直す(引き継ぎメモの却下1)

### 5. `DemandLine` の doc

- `BaseValue` — 「相場項。無ければ**窓口の当日価格**(GDD02b §5.2)。**在庫圧力を掛けない**」
- `MarketTerm` / `HasMarketTerm` — **`BuyerBudget.Decide` はもう読まない。ゲートが読むのは `BaseValue` である。** 相場基準が立ったかの記録として残す(`WorldDefinitionTests` が観測している)
- `Budget` — 第1項が常にあること

### 6. 追随して直す doc

| 場所 | 今どう書いてあるか | 直す向き |
| ---- | ------------------ | -------- |
| `BuyerBudget.cs:178-180`(`Budget`) | 「予算 = min( ApplyPermille(**相場項**, …」「在庫圧力‰ を掛けるのは相場項だけ」 | 基礎値。「掛けるのは第1項だけ(現金上限・利潤上限には掛からない)」は理由ごと残す |
| `BuyerBudget.cs:192-196`(`BaseValue`) | 「無ければ**現金上限**」 | 窓口の当日価格。**都市生産品は外部買値・1次産品は当日の外部売値**まで書く |
| `BuyerBudget.cs:244-255`(`Decide`) | 「理由は 相場 → …」「**基礎値が0の日はゲートが必ず閉じる**ので `PurchaseQuantity` の除算に到達しない」 | 分岐1 に条件が無いこと。**基礎値 0 の枝は到達不能になった**(`BaseValue` が弾く)ので、「実効価格が1以上」に立っていた旧い保証の説明ごと差し替える |
| `BuyerDemand.cs:35`(`DemandLine.BaseValue`) | 「相場項、無ければ現金上限」 | 上の 5 |
| `PurchaseDecision.cs:13`(`NoPurchaseReason.MarketTerm`) | 「実効価格が `ApplyPermille(**相場項**, 在庫圧力‰)` を超えた」 | 基礎値。**あわせて「名前は相場のままだが、指しているのは基礎値の分岐である(相場項が無い日も立つ)」を1行で書く** — enum の名前だけを読む人を止める唯一の手段である |
| `ErrandPlanner.SurplusFor` | 偽の記述は無い(フェーズ1 が確認) | **1文だけ足す。** 相場項が無い日の支払い意思額が窓口価格の 1.5 倍までになり、**探索の動機が旧版の見込みより小さいこと**([GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md))。式は変えない |
| `TradeSystemTests.cs:1177-1178` | 「参照が無いと基礎値=現金上限=流動資金となり」 | 「参照が無いと基礎値が工具の外部買値になり」。**耐久側の組は市場参照(2000)を仕込んであるので挙動は変わらない** |

## 順序・境界の具体例(規則6)

### M0・エポック(1年 春1日、`DayIndex = 0`)・観測ゼロ

全世帯・全品目で相場項が無い。**基礎値は窓口の当日価格である。**

| 用途 / 品目 | 基礎値 | 在庫圧力‰(予想在庫0) | 第1項 | 窓口の提示価格 | 結果 |
| ----------- | ------ | ---------------------- | ----- | -------------- | ---- |
| 必需 パン(都市生産品) | **54**(外部買値) | 1500 | **81** | 108(= 床 × 2) | 108 > 81 → **窓口からは買わない。** 都市内のパン屋は相場基準が無いので床 54 を提示し、54 ≤ 81 でゲートは開く |
| 必需 薪(都市生産品) | **10** | 1500 | 15 | 20 | 同上 |
| 耐久 工具(都市生産品) | **290** | 1500 | **435** | 580 | 580 > 435 → **買わない。** 旧規則では基礎値 = 現金上限 = `FloorDiv(流動資金 2400, 1)` = 2400 で買っていた |
| 生産の入力 穀物(1次産品) | **10**(= `ApplyPermille(基準値 10, 春 1000‰)`) | 1500 | 15 | 10 | 10 ≤ 15 → **買う。** 到達在庫 = `clamp(3T − CeilDiv(2T × 10, 10), 0, 2T)` = **目標在庫ちょうど** |

**夏(`DayIndex = 30` 以降)**: 穀物の基礎値は **13**(= `ApplyPermille(10, 1300‰)`)。**基準値の 10 ではない**(決定12)。木炭は 12 → **15**。

### `Decide` の境界

目標在庫 10・予想在庫 5(→ 在庫圧力 **1250‰**)・基礎値 100・現金上限 1000・利潤上限なし・**`HasMarketTerm = false`**:

| 実効価格 | 第1項 | 購入量 | 理由 |
| -------- | ----- | ------ | ---- |
| 120 | 125 | **1**(到達在庫 6) | `None` |
| **125**(同点) | 125 | 0(到達在庫 5 = 予想在庫) | **`None`。ゲートは開く** — 判定は `>` であって `>=` ではない |
| **126** | 125 | 0(到達在庫 4) | **`MarketTerm`** |

**126 の行が決定11 そのものである。** 決定11 の前はここでゲートが開き(現金上限 1000 だけを見る)、線形解が 0 を返して理由が `None` になっていた。**購入量は 0 のままで、変わるのは理由だけである。**

## 落ちるべき条件(テスト)

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心(当てる変異 / 期待) |
| - | ------ | -------- | -------------------- | ------------------------- |
| 1 | `TradePipelineTests.WindowImportsOnTheFirstDayStayBelowATenthOfTheCitysMoney`(**検出器**。`[Theory]` シード 1/2/3/7/42) | `WorldDefinition.M0` を 60日回し、全世帯の `world.Ledgers` から `CounterpartyId == HouseholdState.ExternalMarketSellerId && Direction == Purchase && OccurredAt.DayIndex == 0` の `Quantity × UnitPrice` を合計する。**`10 × 合計 < definition.InitialLiquidFunds × definition.HouseholdCount`**(= 24,000)。**空振り防止**として同じテストで「合計 ≥ 1」と「`world.Now.DayIndex == 60`」も断定する | 基礎値が現金上限のまま(**master は 20,240 = 84.3%**)/ 都市生産品に天井を使った / 窓口価格の分岐ごと落とした | **【核心】** M-1 |
| 2 | `BuyerBudgetTests.DecideClosesOnTheBaseValueWithoutAMarketReference` | 上の「`Decide` の境界」の3行。**`HasMarketTerm = false` / `MarketTerm = 0` で**。在庫圧力は `BuyerBudget.StockPressurePermille(expectedStock: 5, targetStock: 10)` から取り、**1250 であることも断定する**(手で置いた値が式とずれると、境界の意味が消える) | 分岐1 に `HasMarketTerm` の条件が残っている(126 の理由が `None` になる)/ 判定を `>=` にした(125 の行が `MarketTerm` になる) | **【核心】** M-2 |
| 3 | `BuyerDemandTests.BaseValueFallsBackToTheWindowPriceOfTheDay` | `WorldDefinition.M0` + `WorldGenerator.Generate(definition, new RandomSource(1))`。観測ゼロの初日に全世帯で `Build` を呼び、**(a)** 都市生産品の行(必需パン・必需薪・耐久工具)の `BaseValue` が `definition.ExternalBuyPrice(itemId)`、**(b)** `IsPrimaryItem` の行の `BaseValue` が `definition.ExternalSellPrice(itemId, Season.Spring)`。**(c)** システムを1つも登録しない `SimScheduler` で 30日ぶん時計だけ進め(観測は生まれない)、1次産品の行が `ExternalSellPrice(itemId, Season.Summer)`(穀物 **13**)であって基準値 10 ではないこと。**空振り防止**: (a)(b)(c) それぞれで見た行が 1 件以上あること | 都市生産品に `ExternalSellPrice`(天井)を使った / 1次産品に基準値を使った / 季節を固定した / 分岐を落とした | **【核心】** M-3・M-4 |
| 4 | `BuyerBudgetTests.BaseValueRejectsANonPositiveWindowPrice` | `windowPrice` が 0 と −1 で `ArgumentOutOfRangeException`。**`hasMarketTerm` が true でも false でも投げる** | ガードが無い / 片側にしか無い(基礎値 0 が `PurchaseQuantity` の除算まで届く) | — |
| 5 | `BuyerBudgetTests.BudgetKeepsTheFirstTermWithoutAMarketReference`(既存 `BudgetDropsAbsentTerms` の置き換え) | `Budget(baseValue: 100, stockPressurePermille: 1000, cashCap: 50, …) == 50`(現金上限が勝つ)/ **`Budget(baseValue: 40, stockPressurePermille: 1000, cashCap: 50, …) == 40`(第1項が勝つ。旧形ではこの日も 50 だった)** / 利潤上限 30 を足すと 30 | `Budget` が第1項を落としている(相場項の有無で分岐が残っている) | — |

## 当てる変異(`mutator` が測る。[ADR-0013](../adr/0013-mutation-measurement-separated.md))

**それぞれ単独で当てる。** 期待と食い違ったら転記せずに止まって報告する。

| # | 変異 | 期待 |
| - | ---- | ---- |
| **M-1** | `BuyerDemand.BuildLine` が `BuyerBudget.BaseValue` へ窓口価格ではなく `cashCap` を渡す(**決定10 の反転**) | **【核心】赤: テスト1 が全5シード。** どのシードが赤になったかと、そのときの day0 の輸入額を報告に含める。**テスト2・4・5 は緑のまま**(純関数の単体なので届かない) |
| **M-2** | `BuyerBudget.Decide` の分岐1 を `if (line.HasMarketTerm && effectivePrice > adjustedBaseValue)` に戻す(**決定11 の反転**) | **【核心】赤: テスト2 の 126 の行だけ**(`Reason` が `None`。購入量は 0 のまま)。**テスト1 が緑のままであることを確かめて報告する** — これが「決定11 は購入量を1つも変えない」の実測であり、**検出器を2本に割った根拠そのものである**。**テスト1 が赤になったら、直さずに報告して止まる**(仕様側の主張が誤っている) |
| **M-3** | `BuyerDemand.WindowPrice` の分岐を落とし、全品目で `ExternalSellPrice(itemId, season)` を使う | **【核心】赤: テスト3 の (a)**(パンの基礎値が 54 でなく 108)。**テスト1 も赤になりうる** — なったかどうかと輸入額を報告に含める |
| **M-4** | `BuyerDemand.WindowPrice` が `season` ではなく `Season.Spring` を渡す(**決定12 の反転**。M0 の1次産品は春の係数が 1000‰ なので基準値と同値) | **【核心】赤: テスト3 の (c) だけ。(a)(b) は緑のまま**(春では区別が付かない)。**この非対称を報告に含める** — (c) が無ければ決定12 はどの変異でも守られていない |

## 呼び出し側の配線(規則7)

**`BuyerBudget` は純関数で、窓口価格を自分では引けない。窓口価格を解く場所は `BuyerDemand` 1か所だけである。**

- **入力の作り方** — 季節は `GameDate.FromTick(world.Now).Season`。`Build` が呼ばれるのは [TDD01 §3.3](../04-tdd/01-sim-core-and-m0.md) の段4 で、**`world.Now` はその日の値である**(`TradeSystem` は時計を動かさない)
- **添字・単位の約束** — `windowPrice` は **itemId 添字の配列**(`hasReference` / `reference` と同じ形)。**単位は 貨幣/1個**で、基礎値・相場項・実効価格と同じ土俵にある(耐久の行だけ目標在庫が耐久値だが、価格の側は個あたりのままである)。`isReferenceRelevant` が真の品目だけ埋めれば足りる(行が立つ品目と一致する)が、**全品目を埋めても害は無い** — 1次産品にも都市生産品にも値がある。**どちらにするかは implementer が決めてよい**(タスク内部に閉じる)
- **呼び出し順** — `Build` の中で相場基準を引いた後、行を組み立てる前
- **`Decide` が読むのは `line.BaseValue` だけである。** `MarketTerm` / `HasMarketTerm` を読む呼び出し側を新たに作らない

## 既存テストの追随

| 場所 | 何が起きるか | どうする |
| ---- | ------------ | -------- |
| `BuyerBudgetTests.cs:321` `BaseValueFallsBackToCashCapWithoutMarketTerm` | **赤。** 名前も assert も旧規則を固定している | テスト4 と、窓口価格版の等式(`BaseValue(true, 100, windowPrice: 50) == 100` / `BaseValue(false, 0, windowPrice: 50) == 50`)へ置き換え、名前も改める。**2026-09-19 の変異の実測の doc コメント(`BaseValue` に在庫圧力を掛ける変異)は残す** — 変異も期待も変わらない |
| `BuyerBudgetTests.cs:301-306` `BudgetDropsAbsentTerms` | **赤**(`Budget` のシグネチャが変わる) | テスト5 |
| `BuyerBudgetTests.cs:278-292` `BudgetAppliesStockPressureToMarketTermOnly` | シグネチャ変更でビルドが通らない | `baseValue: 100` へ書き換える。**期待値 150 / 120 は変わらない。** 名前を「第1項にだけ掛かる」向きへ改める。変異の実測の doc コメントもそのまま生きる |
| `TradeSystemTests.cs:1246-1254`(`ErrandPlannerAndSettlementAgreeOnQuantity` の**必需の組**) | **前提が反転する。** `StockPressurePermille = 0, // HasMarketTerm=falseのゲートでは読まれない。` は決定11 で偽になり、手組みの `DemandLine` が実パイプラインの行と食い違う | 手組みの行を実パイプラインと同じ材料で組み直す — `BaseValue = 1`(`breadFloor: 1` = パンの外部買値)、`StockPressurePermille = BuyerBudget.StockPressurePermille(expectedStock: 0, targetStock: 6)` = **1500**。**独立予測は 12 → 6 になる**(到達在庫 = `clamp(18 − CeilDiv(2 × 6 × 1, 1), 0, 12)` = 6)。コメントを「第1項が読む」へ直す |
| 同テストが外出を選ぶこと | **余剰が縮む。** 支払い意思額 = `ApplyPermille(1, 1500)` = **2**、見積もり価格 1、余剰 = `FloorDiv(6 × (2 − 1), 2)` = **3**、外出の費用 = 往復2時間 × 機会費用1 = **2** → 価値 **1 > 0** で行く(フェーズ1 が紙で確かめた) | **余りが 1 しかない。** `ErrandLaborLossPermille > 0` が落ちたら、**数値を動かす前に報告する** — 余剰の式か見積もりの側が仕様と食い違っている可能性がある |
| `ErrandPlannerTests.cs:578` / `:1352` / `:1444` | 相場参照がある、または手組みの `BaseValue` を直接置いている | **変わらない**(フェーズ1 が確認) |
| `WorldDefinitionTests.cs:763-766` | `HasMarketTerm` / `MarketTerm` を観測している | **変わらない。両フィールドは残す** |

### 訂正(2026-09-22、フェーズ1)— 上の表は1件しか挙げていなかった。族は7件である

**初版の表は「2T への張り付きに依存したテスト」を `ErrandPlannerAndSettlementAgreeOnQuantity` の1件しか挙げていなかった。** フェーズ2 の1回目の走行(実測: build 警告0 / **6件赤・432件緑 / 438件**)で、同じ根から**さらに6件**が赤になった。**仕様の穴であってコードの欠陥ではない。**

**根は1つである。** 旧規則では相場項が無い日の基礎値が**現金上限(= 流動資金級)**だったため、`実効価格 ÷ 基礎値` が 0 に近づき、**線形解は事実上つねに上側 clamp(目標在庫の 2 倍)に張り付いていた**。同時に支払い意思額 `ApplyPermille(基礎値, 在庫圧力‰)` も流動資金級だったので、**外出の余剰は費用を常に大きく超えていた**。決定10 で基礎値が床級(M0 の手組み世界では 1〜10)に落ちると、

- **到達在庫が 2T → T(以下)に減る** — 数量の期待値がずれる
- **支払い意思額が `1.5 × 床` に縮む** — **外出そのものが起きなくなり、「前提」の断定から落ちる**

**これは決定10 が狙った変化そのものである**([GDD02b §5.2](../03-gdd/02b-consumption-and-household.md)「需要が価格について 0 次同次であることが戻る」)。**テストの側が旧い(誤っていた)挙動を固定していた。**

#### 裁定

**期待値の更新で原意が保たれるものは直す。配置を変えないと原意が保たれないものは、変える前に報告する**(`PIPELINE: HALT IMPL-BLOCKED` ではなく、implementer が止まってオーケストレータへ報告する)。**配置を変えると、そのテストの判別力が黙って消えることがある**(W2-14 のテスト11・[#154](https://github.com/stama72/visionary/issues/154) で実際に起きた型)。

| 赤になったテスト | 診断 | どうする |
| ---------------- | ---- | -------- |
| `TradeSystemTests.BudgetGateUsesTheEffectivePriceOnly` | **期待値の更新では直らない。** 窓口を候補から外すために distant 側だけに入れた偽の高値観測(パン 999)が、**相場基準としても拾われる**。旧規則では home も基礎値が現金上限 1000 で、**両者とも上側 clamp(2T)に張り付いていたので一致していた**。いま home は基礎値 = 床 10 で T、distant は相場項(999 由来)で 2T になり、**home/distant の対称性が壊れた**(実測: 期待 1・実際 2) | **対称性を戻す。** 偽の観測を home にも同じく入れるのが最小の直し方だが、**入れる前に「それで本テストの原意(外出の費用が数量にも予算にも混ざらないこと)が保たれるか」を1行で述べて報告する** |
| `TradeSystemTests.NextDaysProductionDropsByTheErrandLaborLoss` | **traveler が外出しなくなった**(`ErrandLaborLossPermille > 0` が false)。穀物の床 1 に対し支払い意思額が最大 2 まで縮んだ | **配置の変更が要る。** 望ましい梃子は**窓口価格(= 基礎値)を上げる**ことだが、**品目によっては窓口の提示価格も同時に動く**(1次産品は外部売値が基礎値そのもの)。上げた結果 **窓口が店の候補に入り直していないか**を確かめてから報告する |
| `TradeSystemTests.ExportErrandIsSkippedWhenTheDayIsFull` | 前提の断定「買い物が実際に成立している(`HouseholdInventory[Item.Grain] > 0`)」が崩れた。同じ根(買い物の外出が起きない) | 同上 |
| `TradeSystemTests.NecessityIsSettledBeforePreference` | 「必需1日分は払えるが、必需+嗜好1日分は払えない帯」に流動資金 15 を置いている。基礎値が変わって数量と残資金がずれた(実測: 嗜好の約定が成立した) | **まず期待値の更新で原意(必需が先に決済される)が保たれるかを見る。** 保たれないなら帯の置き直しなので報告する |
| `TradePipelineTests.NecessityIsSettledBeforePreference` | 同上 | 同上 |
| `TradePipelineTests.UnaffordableNecessityCountsOnlyTheFundsShortfall` | **M0 の 60日走行で「資金不足が自然発生する (世帯, 日)」が動いた。** 本テストの doc コメントは #38・#149 でも同じ理由で2度更新されており、**手順がそこに書いてある** | **doc コメントの手順どおり**、60日を走査して `0 → 1 → 0` のきれいな遷移を持つ最初の (世帯, 日) を選び直し、**実測値と測定日を doc コメントへ書く**。報告は不要(確立した手順である) |

**7件目(初版から挙げていた `ErrandPlannerAndSettlementAgreeOnQuantity`)はフェーズ2 の1回目の走行で緑になっている。**

#### この族を今後の走行で取りこぼさないために

**`dotnet test` が緑になるまで、赤の1件ずつについて「根は基礎値の族か、別の欠陥か」を明示的に分ける。** 族なら上の裁定に従い、**族でない赤が1件でも出たら止めて報告する** — 決定10・11 は `BuyerBudget` / `BuyerDemand` の外に触っていないので、族の外の赤は実装の誤りである。

## 実測して doc コメントへ転記すること

1. **テスト1 の5シードそれぞれの day0 の輸入額。** #120 の使い捨て実測は **816 / 816 / 776**(シード 1/2/42、**決定11 を入れる前**)で、**シード 3 と 7 は測っていない。** 一般化はゲートが厳しくなる方向なので、これ以上には戻らないはずである。**食い違ったら、閾値を動かす前にそのまま転記して報告する**
2. **M-2 でテスト1 が緑のままだったこと**(決定11 が購入量を変えないことの実測)

## 編集してよい文書

- `docs/tasks/W2-15-window-price-base-value.md`(本書)と `docs/tasks/W2-15-window-price-base-value.handoff.md`
- **GDD / TDD / ADR は触らない。** フェーズ1 が `efd8a3a` で直し終えている。触ると `SPEC-OUTSIDE` で止まる

## このタスクで特に効く規約

- **`ExternalBuyPrice` は1次産品で例外を投げるが、`ExternalSellPrice` は都市生産品でも静かに値を返す。** 分岐の落とし方に非対称がある — **気付けないのは「都市生産品に `ExternalSellPrice`」の側**(天井が返る)である
- **窓口価格・基礎値・相場項・現金上限はすべて int の貨幣。** ‰ が現れるのは在庫圧力と許容乖離だけである
- **乱数を1つも引かない。** 検出器はシードを固定して走らせるだけである
- **`world.Ledgers` の走査は合計を取るためだけに使う。** 世帯は Id 昇順(添字 = Id)で回す(ADR-0002)

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **M-1〜M-4 を `mutator` が実測し(レビューの巡が閉じた後)、結果を doc コメントへ転記した**
- [ ] **上の「実測して doc コメントへ転記すること」2件を転記した**
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
