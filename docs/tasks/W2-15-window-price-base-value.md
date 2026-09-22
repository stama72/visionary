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
| **M-5**(訂正3 で追加) | `TradeSystem` の買い物が `Decide(line, store.UnitEffectivePrice)` ではなく `Decide(line, store.UnitEffectivePrice + Errand.Cost(travelHours, errand.CostPerHour))` を呼ぶ(**[#85](https://github.com/stama72/visionary/issues/85) が消した二重計上の復活**) | **赤: `TradeSystemTests.BudgetGateUsesTheEffectivePriceOnly`。** 本タスクは既存テストではないが、**訂正3 の赤A が「この変異で赤になるか」を配置の合否そのものにしている** — 緑のままなら赤A の直しは効いていない |
| **M-6**(訂正4 で追加) | `TradeSystem` の需要行の走査を `demand.Lines.Reverse()` に反転する(**走査順 必需 → 耐久 → 入力 → 嗜好 の破壊**) | **赤: `TradeSystemTests` の3件**(`NecessityIsSettledBeforePreference` / `NecessityShortfallIsCountedOnBothPaths` / `NonNecessityFundsShortfallIsNotCounted`)。**`TradePipelineTests.NecessityIsSettledBeforePreference` は緑のまま** — これが訂正4 の赤C の根拠そのものである。**裁定のために2巡目の途中で一度測っており(2026-09-22)、そのとき 3赤 / 緑 だった。テストが動いたので測り直す** |
| **M-7**(訂正4 で追加) | `BuyerBudget.BaseValue` の**戻り値**ガード(`<= 0` で例外)を削除する | **赤: テスト4 の `(hasMarketTerm: true, marketTerm: 0, windowPrice: 50)` の行だけ。** 訂正3 赤B の直しが効いていることの実測 |
| **M-8**(訂正4 で追加) | `BuyerBudget.BaseValue` の**引数**ガード(`windowPrice <= 0`)を削除する(戻り値ガードは残す) | **赤: テスト4 の `(true, 100, 0)` と `(true, 100, -1)` の2行だけ**(`hasMarketTerm: false` の行は戻り値ガードが拾う)。**2つのガードが別々の面を守っていることの実測** |
| **M-9**(訂正4 で追加) | `TradeSystem` の輸出の時間検査を `>` → `>=` にする | **赤: `ExportErrandIsSkippedWhenTheDayIsFull` の caseB**(往復 12 = T ちょうどで持ち込まなくなる)。**訂正2 で足場を入れ替えた後も、往復時間という判別軸が生きていることの実測** |
| **M-10**(訂正4 で追加) | `TradeSystem` 段5a の `household.ErrandLaborLossPermille = plan.LaborLossPermille;` を削除する | **赤: `NextDaysProductionDropsByTheErrandLaborLoss`。** 穀物の床を 1 → 30 へ置き直した後も [#98](https://github.com/stama72/visionary/issues/98) の閉じる条件を守っていることの実測 |

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
| `TradeSystemTests.ExportErrandIsSkippedWhenTheDayIsFull` | 前提の断定「買い物が実際に成立している(`HouseholdInventory[Item.Grain] > 0`)」が崩れた。同じ根(買い物の外出が起きない) | **梃子が違う。下の「訂正2」を読むこと**(初版のこの行は誤り) |
| `TradeSystemTests.NecessityIsSettledBeforePreference` | 「必需1日分は払えるが、必需+嗜好1日分は払えない帯」に流動資金 15 を置いている。基礎値が変わって数量と残資金がずれた(実測: 嗜好の約定が成立した) | **まず期待値の更新で原意(必需が先に決済される)が保たれるかを見る。** 保たれないなら帯の置き直しなので報告する |
| `TradePipelineTests.NecessityIsSettledBeforePreference` | 同上 | 同上 |
| `TradePipelineTests.UnaffordableNecessityCountsOnlyTheFundsShortfall` | **M0 の 60日走行で「資金不足が自然発生する (世帯, 日)」が動いた。** 本テストの doc コメントは #38・#149 でも同じ理由で2度更新されており、**手順がそこに書いてある** | **doc コメントの手順どおり**、60日を走査して `0 → 1 → 0` のきれいな遷移を持つ最初の (世帯, 日) を選び直し、**実測値と測定日を doc コメントへ書く**。報告は不要(確立した手順である) |

**7件目(初版から挙げていた `ErrandPlannerAndSettlementAgreeOnQuantity`)はフェーズ2 の1回目の走行で緑になっている。**

### 訂正2(2026-09-22、フェーズ1)— 族は 8 件。うち 2 件は上の裁定では直らない

**フェーズ2 の2回目の走行が `IMPL-BLOCKED` で止まった。** 停止時の機械の状態は **build 警告0 / format 通過 / 442緑・2赤・444件 / 作業ツリー clean**(`e021288`・`1ef5ed2` の2コミット)。**族の外の赤は1件も出ていない** ⇒ 決定10・11 の実装そのものに欠陥は見つかっていない。

上の裁定表は族を7件と書いたが、**8件である**(下の赤2 が未列挙)。**あわせて赤1 の裁定行で名指しした梃子は誤りだった。**

#### 赤1 `TradeSystemTests.ExportErrandIsSkippedWhenTheDayIsFull` — 床を上げる梃子は効かない

**なぜ効かないか(フェーズ2 の診断。フェーズ1 が裏付けた):** 窓口の見積もり(`ErrandPlanner.EstimateWindowPrice` の3段目)も都市内の売り手の見積もり(`TryEstimateOfferPrice` の3段目)も、**記憶が無い日は同じ床を読む。** 両者の見積もり価格が恒に同額である以上、**床をどれだけ上げても勝敗は距離だけで決まる** — 窓口(中心 = 区画4。買い手の区画0 から距離2)が、caseB の売り手(区画8、距離4)に必ず勝つ(実測: `VisitedDistrictIds = [4]`)。**床は差を作らない軸なので梃子にならない。** 初版の裁定行はここを見落としていた。

**採る梃子: 買い手に窓口の高値の価格記憶を仕込み、窓口を候補から実質的に外す。**

- **このファイルが既に2回使っている足場である** — `BudgetGateUsesTheEffectivePriceOnly`(パン 999)と `ErrandPlannerAndSettlementAgreeOnQuantity`(工具 999999)。**新しい手口を持ち込まない**
- 効く理由は床と違う: 買い手の区画0 から中心までの距離 2 は **視界半径 R = 1 の外**なので、`EstimateWindowPrice` は今日の知覚ではなく**有効な記憶**(差 1 日以上)を読む。**価格の側に差を作れる**
- **caseA・caseB の両方に同じ記憶を入れる。** 片方だけに入れると、`BudgetGateUsesTheEffectivePriceOnly` で起きたのと同じ非対称が生まれる
- **副作用を承知で採る**(規則4): この観測は `MarketReference.TryBuyer` にも拾われるので、**穀物の相場項が立ち、基礎値が高くなって到達在庫が上側 clamp(2T)へ戻る。** 本テストの判別軸は往復時間(「日が埋まっているとき輸出の外出が飛ばされる」)であって数量ではないので、軸は保たれる。**ただし本テストは以後「相場項が無い日」の経路を通らない** — 通っていないことを doc コメントに1行書く

**確かめて報告すること**: (i) caseA・caseB とも `VisitedDistrictIds` に**売り手の区画が入り、中心(4)が入らない**こと、(ii) caseA は輸出が起きず caseB は起きること(往復時間の帯は `travelHoursPerDistrict` だけで決まり、8+8=16 > 12 と 8+4=12 = T のまま)、(iii) 記憶の価格。**(i) が満たせないなら、値を動かさずに報告する。**

#### 赤2 `TradePipelineTests.UnaffordableNecessityCountsOnlyTheFundsShortfall` の第3ブロック(ビール)— 族の8件目

**(世帯, 日)の選び直し(世帯Id6・8日目 → 世帯Id4・7日目)は確立手順どおり済んで緑である。** 赤なのは同じテストメソッドの**第3ブロック**(素の M0・day1・世帯Id1 がビールを買わないことの断定)で、これは上の裁定表に挙げていなかった。

**機構**(フェーズ2 が旧コード `7377df3` と突き合わせて実測): 旧コードでは世帯Id1 の必需・耐久・入力の基礎値が現金上限(600/1200/2400/400/400)になり、**day1 に 2,388 を使い切る買い占め**が起きて嗜好の番に 12 しか残らず、価格72 のビールが買えなかった。新コードでは基礎値が窓口の当日価格(10/54/290/56/10)に落ち、**流動資金 2400 が嗜好まで温存されてビールが買える。**

**`Assert.False` を `Assert.True` に反転してはならない**(フェーズ2 の診断のとおり)。何も失敗していない世帯で「資金不足カウントが0」を示すだけになり、**このブロックが証明したかったこと(嗜好の購入失敗は資金不足に数えない)を何一つ証明しなくなる。**

**裁定 — 第一案と、その不成立時の落とし所まで決める:**

1. **第一案**: 素の M0 の60日走行を走査し、**(a) その日に嗜好(ビール)を買っていない / (b) その日に必需を1件以上約定している / (c) `UnaffordableNecessityCount == 0`** を満たす最初の (世帯, 日) を選び直す。**(b) を足すのは、ビールを買わなかった理由が「店を1つも知らない」側に落ちていないことを示すためである**(旧版は素の世界の初日なので暗黙に満たしていた)。選んだ (世帯, 日) と**ビールが買われなかった機構**(資金上限で切られたのか、在庫圧力 0 か)を doc コメントへ実測として書く
2. **第一案が60日で1件も見つからなければ、このブロックを削除する。** 同じ主張は **`TradeSystemTests.NecessityIsSettledBeforePreference` が統制された世界で持っている**(嗜好が `FundsCap` で 0 個・`UnaffordableNecessityCount == 0`)。本ブロックは **#38・#149・本タスクと3度続けて、経済の形が変わるたびに再調整されている創発頼みの写し**であり、維持の費用のほうが大きい([ADR-0008](../adr/0008-review-scope-narrowed-to-unnoticeable-defects.md))。削除するときは、**理由と引き取り先を doc コメントへ書く**

**どちらの枝を採ったかを報告する。**

#### この族を今後の走行で取りこぼさないために

**`dotnet test` が緑になるまで、赤の1件ずつについて「根は基礎値の族か、別の欠陥か」を明示的に分ける。** 族なら上の裁定に従い、**族でない赤が1件でも出たら止めて報告する** — 決定10・11 は `BuyerBudget` / `BuyerDemand` の外に触っていないので、族の外の赤は実装の誤りである。

### 訂正3(2026-09-22、フェーズ2)— 対称化の梃子がテストの判別力を消した

**出所はレビュー1巡目の象限I。** 訂正1 の裁定表が `BudgetGateUsesTheEffectivePriceOnly` に名指しした梃子(「偽の観測を home にも同じく入れるのが最小の直し方」)は、**テストを緑にすると同時にその判別力を消していた。** 裁定表自身が警告していた型([#154](https://github.com/stama72/visionary/issues/154) / W2-14 のテスト11)が、警告している当の指示で再現した。**仕様の欠陥であって実装の誤りではない** — implementer は指示どおりに手を動かし、求められた確認(足場の役割が保たれているか)にも正しく答えている。**求めた確認の中身が足りなかった。**

#### 赤A `TradeSystemTests.BudgetGateUsesTheEffectivePriceOnly` — 両辺が上側 clamp に飽和した

**機構**(レビュアーの算術。実測は implementer が取る):

- 偽の観測(パン 999)を home にも入れたことで、**両世界とも基礎値が相場項由来になる** — 相場基準 999・許容乖離 1200‰ → 相場項 `ApplyPermille(999, 1200)` = **1199**
- `necessityTargetStockDays[Bread] = 1`・消費1/日・NPC1人 → **T = 1**、予想在庫 0
- 実効価格 10 → 到達在庫 = `clamp(3 − CeilDiv(2 × 10, 1199), 0, 2)` = **2 = 上側 clamp**。home も distant も同じ

**何が消えたか:** 核心の断定 `Assert.Equal(home の購入量, distant の購入量)` が **「2 == 2」** になった。上側 clamp を割るには実効価格が 基礎値の半分(≈ 600)を超える必要があるので、**このテストは「外出の費用が単価に混ざる」摂動を一切検出しない。** doc コメントが記録している変異(`Decide(line, store.UnitEffectivePrice + Errand.Cost(...))`)は単価を 10 → 14 にするだけで、`CeilDiv(2 × 14, 1199) = 1` → 到達在庫は両辺とも 2 のまま。決済額も両辺 20 で一致する。**[#85](https://github.com/stama72/visionary/issues/85) が消した二重計上が戻っても、赤になるテストは1つも無い。**

**直前の赤(実測「期待1・実際2」)は、home が基礎値 = 床10 で到達在庫 T = 1 という感度のある帯にいたことの裏返しである。** 訂正1 の梃子は**感度のある側を飽和側へ引き上げて**対称にした。逆向き(distant を床側へ落とす)なら両辺が感度帯に残る。

**採る梃子: 偽の観測を両世界から外し、`necessityTargetStockDays[Bread]` を 1 → 2 にする。**

- 外すだけでは外出が立たない(T = 1 では 支払い意思額 `ApplyPermille(10, 1500)` = 15、余剰 `FloorDiv(1 × (15 − 10), 2)` = **2** < 外出の費用 4)。**T = 2 が両方を同時に満たす** — 到達在庫 = `clamp(6 − CeilDiv(4 × 10, 10), 0, 4)` = **2 = T(非飽和)**、余剰 = `FloorDiv(2 × (15 − 10), 2)` = **5 > 4**
- **変異(単価 10 → 14)では** 到達在庫 = `clamp(6 − CeilDiv(4 × 14, 10), 0, 4)` = **0** に落ちる。判別力が戻る

**確かめて報告すること**(**(i) が満たせないなら、値を動かさずに報告する**):

- **(i) 足場を外した後も、home・distant とも窓口(中心 = 区画4)が店の候補に入っていないこと。** 偽の観測はもともと窓口を候補から外すために置かれている。外すと**赤1 と同じ機構**(記憶が無い日は窓口も売り手も同じ床を読み、勝敗が距離だけで決まる)で窓口が勝ちうる。**`VisitedDistrictIds` を実測して報告する**
- (ii) home・distant の購入量が**一致し、かつ上側 clamp(2T = 4)ではないこと**
- (iii) doc コメントの変異(単価に外出の費用を混ぜる)を当てたときに**赤になること**。これは `mutator` の仕事である([ADR-0013](../adr/0013-mutation-measurement-separated.md))— implementer は当てず、**この変異を後段の `mutator` へ回す M-5 として記録するだけでよい**

**不成立時の落とし所**(= (i) が満たせない / 両方を同時に満たす配置が無い): **配置を訂正1 のまま戻し、「両辺が上側 clamp に飽和しており、このテストは現状 `Decide` への単価の混入を検出しない」ことを doc コメントに明記したうえで issue へ落とす。** 黙って緑にしない。判別力が無いことが**読めば分かる**状態にすることが最低条件である([ADR-0008](../adr/0008-review-scope-narrowed-to-unnoticeable-defects.md) — 直さない選択は許されるが、気付けないまま残すことは許されない)。

#### 赤B `BuyerBudget.BaseValue` の「常に1以上」は、ガードより広い

**「作るもの 2」が `windowPrice <= 0` だけを検査すると決めたのに対し、doc は「常に1以上」「基礎値0の枝は例外で弾くので到達不能」と書いている。** `BaseValue(hasMarketTerm: true, marketTerm: 0, windowPrice: 50)` は**例外を投げずに 0 を返す。** 新テスト `BaseValueRejectsANonPositiveWindowPrice` は `marketTerm: 100` で呼ぶのでこの穴を通り抜ける。

**同じ変更で削除された旧文「基礎値が0の日はゲートが必ず閉じる(実効価格 ≥ 1 > 0 = 予算)ので `PurchaseQuantity` の除算に到達しない」が、いま実際に効いている唯一の保証である。** 広い誤りは「見なくてよい」と読ませるので、`PurchaseQuantity` の除算のゼロ保護が何によって守られているかを確かめようとした人がそこで確認をやめる([docs/process/03-corrections.md](../process/03-corrections.md))。**M0 では相場項が 2 以上なので今日は踏まない。害は読者の側に出る。**

**採る直し: ガードを戻り値に掛けて、doc の主張を機械が守る形にする。**

- `BaseValue` の検査を **`hasMarketTerm` の枝を通った後の戻り値 ≤ 0** に対して行う(`windowPrice <= 0` の無条件検査という「作るもの 2」の趣旨は、これに含まれる形で満たされる)
- テスト4 に **`hasMarketTerm: true, marketTerm: 0`** の行を足す
- **前提の確認**: `hasMarketTerm = true` かつ `marketTerm = 0` で `BaseValue` を呼ぶ既存の呼び出し・既存テストが**無いこと**を確かめてから入れる。あれば投げてしまう
- **あった場合の落とし所**: ガードは「作るもの 2」のまま(`windowPrice` のみ)にし、**doc を狭い側へ直す** — 「常に1以上」は `hasMarketTerm = false` の枝についての主張であること、`hasMarketTerm = true` の枝を守っているのは**実効価格 ≥ 1 という旧来の不変条件**であることを書き戻す。**どちらを採ったかを報告する**

### 訂正4(2026-09-22、フェーズ2)— 網羅パスが見つけた4件

**出所はレビュー2巡目(網羅パス)。** 本タスクが配置・期待値・足場を変えたテストを全件(**既存10件 + 新規4件 = 14件**。仕様の「族は8件」は*赤になった*件数であって、触った件数ではない)列挙し、核心の断定が飽和・自明化していないかを1件ずつ算術で判定させた。**4件が出た** — うち1件は判別力の消失(赤A と同型)、3件は doc の記述が事実と食い違う。

#### 赤C `TradePipelineTests.NecessityIsSettledBeforePreference` — 帯の置き直しでは復元できない

**帯 `ScarceLiquidFunds` を 100 → 50 にした置き直し(訂正1 の裁定で implementer が実測して選んだ)が、走査順の判別力を消していた。**

**実測(`mutator`・M-6・2026-09-22)**: `TradeSystem` の需要行の走査を `demand.Lines.Reverse()` に反転すると、**赤になるのは `TradeSystemTests` 側の3件**(`NecessityIsSettledBeforePreference` / `NecessityShortfallIsCountedOnBothPaths` / `NonNecessityFundsShortfallIsNotCounted`)で、**`TradePipelineTests.NecessityIsSettledBeforePreference` は緑のままである。**

**帯の置き直しでは復元できない。** ビールの最小実効価格は床 `ExternalBuyPrice[Beer] = 72`。前回の走行の実測は「**1〜70 は薪の約定が成立しビールは0件、72 以降はビールも成立**」であり、**「必需は払えるが嗜好は走査順のせいで買えない」帯はこの世界に存在しない** — 70 以下では嗜好が絶対額で塞がれ、72 以上では買える。復元するには M0 の価格か世界の構成を変えることになり、**それは本タスクの外である。**

**あわせて必需側の断定が空振りしている。** `Assert.True(HouseholdInventory[Item.Firewood] > 0)` は購入が1件も無くても真である — 初期在庫 薪28(`WorldDefinition.cs`)に対し、世帯2人・春の1日消費 `ApplyPermille(2 + 2, 800‰)` = 4 なので、3日走っても 16 残る。**doc の「初期の世帯在庫はその日のうちに `ConsumptionSystem` が使い切るので、値が残っていれば買い直した証拠になる」は薪については偽である。** パン(初期6・2/日)なら3日でちょうど 0 になり、断定が意味を持つ。

**裁定 — 消さずに、持っていない保証を名指しで書く:**

1. **空振りしている断定を直す。** 薪 → **パン**。「買い直した証拠になる」が実際に成り立つ品目へ移す
2. **走査順の保証を持っていないことを doc コメントの冒頭1行で宣言する。** 名前(`NecessityIsSettledBeforePreference`)は**変えない** — 本タスクは `NoPurchaseReason.MarketTerm` で同じ裁きをしており(引き継ぎメモの却下1)、**「名前は残し、doc の1行が読み手を止める」が本タスクの一貫した形である**。書く内容は、**帯 50 がビールの床 72 を下回るため嗜好は走査順ではなく絶対額で塞がれていること**と、**M-6 の実測(反転しても緑)**、**引き取り先が `TradeSystemTests.NecessityIsSettledBeforePreference`(同じ変異で赤)であること**の3つ
3. **パイプライン級の走査順の検出器を建て直すかは issue へ落とす。** M0 の価格を動かすか世界の構成を変えることになり、本タスクの外である。**走査順の規則そのものは `TradeSystemTests` の3件が機械で守っている**(M-6 で実測済み)ので、[M0 の Exit Criteria を脅かさない](../adr/0008-review-scope-narrowed-to-unnoticeable-defects.md)

#### 赤D `BuyerBudget.Decide` の doc が、ゼロ除算を守っている当の不変条件を「もう要らない」と書いている

**訂正3 の赤B が `BaseValue` だけを見て、`Decide` 側に同じ主張が残ったまま広くなった。** `Decide` は `BaseValue(...)` を**呼ばない** — 読むのは `in DemandLine line` の `line.BaseValue` である。手組みの `DemandLine` は実在する(`BuyerBudgetTests.BuildLine` / `ErrandPlannerTests.cs:89` / `TradeSystemTests.cs:1334`)ので、**`BaseValue = 0` の行が `Decide` に渡ることは型でも呼び出し規約でも防がれていない。**

**いまも `PurchaseQuantity` の除算を守っているのは、旧版が書いていた不変条件そのものである** — `実効価格 ≥ 1 > 0 = ApplyPermille(0, 圧力)` で分岐1 が必ず立つ。

**採る直し(狭い側へ)**: 「生産経路では `BuyerDemand.BuildLine` が `BaseValue` を通すので 0 は入らない。`Decide` 自身が除算を守っているのは、依然として『実効価格 ≥ 1』と分岐1 が厳密な `>` であることである」。**「その保証はもう要らない」を消す。**

#### 赤E 窓口が候補から外れる理由が、同じ変更の中で2通りに説明されている

**`BudgetGateUsesTheEffectivePriceOnly` の (i) の doc(訂正3 で implementer が書いた)が「窓口側の天井のぶん構造的に発火しない」としているが、これは誤りである。** 実測の結論(窓口の行が1件も無い)は正しく、機構の説明だけが違う。

- **計画段(`ErrandPlanner`)に天井は現れない。** `EstimateWindowPrice` は距離 > R かつ記憶が無い日、都市生産品について `ExternalBuyPrice`(= **床10**)を返す。売り手側の `TryEstimateOfferPrice` の3段目も同じ床10。距離も往復時間も同じなので、**両者は完全に同点で、訂正2 の赤1 と同じタイの機構がここでも発火している**
- 窓口を外しているのは `ErrandPlanner.Plan` の**区画Id昇順の走査と厳密な `>` 更新**である(区画2 が区画4 より先に評価される)。天井が効くのは訪問後の `StoreChoice` の段で、distant 世界では窓口はそもそも訪問区画に入っていない

**「構造的に発火しない」は「もう見なくてよい」と読ませる記述である。** 実際には売り手を区画8 へ置くだけでタイが逆転する(= 訂正2 の赤1 の caseB とまったく同じ配置)。**同じ変更の中で `NextDaysProductionDropsByTheErrandLaborLoss` と `ExportErrandIsSkippedWhenTheDayIsFull` は同じ機構を「区画Id昇順・厳密 `>`」と正しく書いており、3か所が2通りの説明を持っている。** 訂正2 が「床を上げる梃子」を誤ったのは、まさにこの段の取り違えであった — **3度目を踏ませない。**

**採る直し**: (i) の doc を「区画Id昇順・厳密な `>` 更新で区画2 が先に勝つ。窓口と売り手の見積もりは床で同点であり、天井が効くのは訪問後の `StoreChoice` の段である」へ直す。

#### 赤F `TradeSystemTests.NecessityIsSettledBeforePreference` の期待値 3→4 の機構が誤り(木材ではなく工具)

**この世界で木材は1個も買われない(旧コードでも買われていなかった)。** 買い手は Woodworker で `laborPermille = 1` → `ProductionCapacity = 1300` → `DailyInputQuantity(Woodworker, Timber) = 1300`。生産の入力の現金上限は `CashCap(15, 1300) = FloorDiv(15, 1300) = 0` なので、`Decide` は新旧とも `NoPurchaseReason.CashCap` を返す。

**実際に動いたのは耐久(工具)の行である。**

- 旧: 基礎値 = 現金上限 15 → 到達在庫 `clamp(90000 − CeilDiv(60000 × 1, 15), 0, 60000)` = 60000 → `CeilDiv(60000, 30000)` = **2個** → 代金2 → `15 − 10 − 2` = **3**
- 新: 基礎値 = `ExternalSellPrice(Tools, 春)` = 1 → 到達在庫 `clamp(90000 − 60000, 0, 60000)` = 30000 → **1個** → 代金1 → `15 − 10 − 1` = **4**

**期待値 4 も原意(必需が先に決済される)も正しい。壊れるのは次に触る人である** — この doc を読んだ人は `inputBufferDays` や木材の価格を動かせばこのテストが動くと予測し、工具の目標在庫・耐久値・`ToolTargetStockPermille` を動かしても動かないと予測する。**どちらも逆である。** 同じ誤りが [#38](https://github.com/stama72/visionary/issues/38) の remarks(「生産の入力(木材)が新たに約定するようになった」)から継承されている。

**採る直し**: 当該 doc の機構を**工具の行**へ直す(上の算術を添える)。**引き継ぎメモの該当行も直す** — PR 説明へ転記されるためである。

### レビューで足した断定(別表。上の表は実装に渡した時点の指示であって最終形ではない)

| # | どこ | 足す断定 | なぜ |
| - | ---- | -------- | ---- |
| R-1 | `TradePipelineTests.UnaffordableNecessityCountsOnlyTheFundsShortfall` 第3ブロック | 選んだ (世帯, 日) が**その日に必需を1件以上約定していること**(例: `world.Ledgers` に day0 の `Purchase` が1件以上) | 訂正2 の裁定1 は (b) を**選び直しの条件**として置き、理由まで書いたが、実装は doc コメントの実測として書いただけで断定していない。**世帯Id8 が「店を1つも知らない」状態へ落ちても `Assert.False(boughtBeer)` と `Assert.Equal(0, ...)` は通る** — 訂正2 が `Assert.True` への反転を却下した理由が、別経路で成立してしまう |

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
