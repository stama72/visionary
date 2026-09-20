# W2-11: 売れなかった日は値上げしない(価格係数の頭打ち)と提示価格の帯の検出器

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#131](https://github.com/stama72/visionary/issues/131)              |
| 根拠     | [GDD02c §1・§1.1・§1.2・§1.4](../03-gdd/02c-price-and-budget.md) / [#120 の決定ログ](https://github.com/stama72/visionary/issues/120#issuecomment-5749222551) / [TDD01 §3.3](../04-tdd/01-sim-core-and-m0.md) |
| ブランチ | `feat/131-unsold-price-cap-and-band`(**`design/120-offer-price-anchor` から切った。** #120 の GDD02c 改稿 2 コミットは master に未マージで PR も無いので、本ブランチの PR にそのまま含まれる) |
| worktree | `visionary/`(本体)                                                  |

> **この文書は使い捨ての作業指示である。** 実装完了時点で凍結し、以後の正はコードと GDD/TDD。

## スコープ

**[#120](https://github.com/stama72/visionary/issues/120) が GDD02c §1.1 に置いた規則「その品目について前日に自分の約定が1件も無かった売り手は値上げしない」を実装し、提示価格の帯を機械が見る検出器を `tests/` に置く。** 規則そのものは #120 の設計セッションが決めて GDD に書いてあり、本タスクは追随表([#120 のコメント](https://github.com/stama72/visionary/issues/120#issuecomment-5749222551))の 3 行を引き取る。

**含まない:**

- **都市外市場の窓口([#38](https://github.com/stama72/visionary/issues/38))。** 窓口が無いあいだは生産が 6 日目前後で止まり、売り注文は 15 日目までに 2 件へ落ちる(本タスクの実測でも同じ)。**売り注文の下限の検出器は #38 の後に #120 が締める** — 本タスクでは置かない
- **[GDD02 §8](../03-gdd/02-economy.md)-1 の発散・硬直の判定関数と「価格係数が 1000‰ で頭打ちになった売り手日の割合」のメトリクス([#41](https://github.com/stama72/visionary/issues/41))。** 本タスクの検出器は提示価格・60 日の走行テストであり、TDD01 §5.2 の判定関数ではない
- **GDD / TDD / ADR の変更。** 規則は既に GDD02c にある。実装が GDD と食い違うと思ったら止まる(`SPEC-OUTSIDE`)

### 変えないと宣言する既存コード([process/02](../process/02-task-spec.md) 規則8)

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `OfferPrice.StockRatioPermille` / `PriceCoefficientPermille`(係数の 3 点: 在庫0 → 1500‰ / 目標 → 1000‰ / 2倍 → 500‰。`CeilDiv` の向き) | [GDD02c §1.1](../03-gdd/02c-price-and-budget.md) の表と註 | **一致**(#120 の追随表が「変えない」と確認済み) |
| `OfferPrice.Calculate` の破産中の枝(500‰ 固定、在庫比を評価しない、床は破らない) | [GDD02c §1.4](../03-gdd/02c-price-and-budget.md) | **一致** |
| `MarketReference.TryPreviousDaySettledPrice`(`Sale` の行だけ、品目で絞る、前日の日付、輸出を含む、0 件なら false) | [GDD02c §1.1](../03-gdd/02c-price-and-budget.md)「約定は帳簿(`Sale` の行)で数え、輸出も含める」/ §1.2 | **一致**。§1.1 の「判定は (売り手 × 品目) ごと」は、この関数が `itemId` で絞っていることで満たされる — **本タスクは判定関数を新設しない** |
| `MarketReference.TrySeller`(他の売り手ごとに最新 1 件 + 自分の前日の約定単価。他の売り手が 0 件なら自分の錨も使わない) | [GDD02c §1.2](../03-gdd/02c-price-and-budget.md) | **一致**(#120 は却下理由の書き方だけを直し、挙動は変えていない) |
| `TradeSystem` 段1 の「販売在庫 0 の日は売り注文を出さない」「相場基準が立たない日は床」 | [GDD02c §1・§1.3](../03-gdd/02c-price-and-budget.md) | **一致** |

## 設計の前提(フェーズ1 で決めたこと)

**フェーズ1 が使い捨ての worktree で頭打ちを当てて実測した**(M0・`ProductionSystem` + `ConsumptionSystem` + `TradeSystem`・60 日・シード 1/2/3。worktree は破棄済みで、コードは本ブランチに無い)。実装はこの実測値を「実装が緑にできる自然な例」として使ってよいが、**必ず自分の走行で確かめてから書くこと。**

| どこ | 何を決めたか |
| ---- | ------------ |
| **帯の定数** | **`床 × 20`**(床 = `WorldDefinition.ExternalBuyPrice(品目)`。M0 の初期値は都市生産 5 品目とも 1 日目の提示価格 = 床である)。実測の最大は **シード 1 のパン 500(床 54 の 9.26 倍、12 日目)**、シード 2 はパン 435(8.06 倍)、シード 3 は工具 444(1.53 倍)。**素の master(頭打ち無し)はビールが 20 日目に 1,556(床 72 の 21.6 倍)、60 日目に 725,771 に達する**ので、20 倍は頭打ちを外す変異に対して判別力を持ち、実測に対して 2 倍強の余裕がある |
| **帯は毎日見る** | 60 日を 1 日ずつ進め、**各日の `World.Market` の全件**を帯と比べる。**最終日だけ見てはならない** — 15 日目以降は売り注文が 2 件(工具)に落ち、パン・ビールは市場から消えているので、最終日だけ見ると発散した品目を見ずに緑になりうる |
| **パンの 9.26 倍は不具合ではない** | パン屋は毎日売れている(約定がある)ので §1.1 の頭打ちを受けず、完売枝のラチェットで上がる。**§1.1 は「約定が無い日は上げない」であって水準の復元力ではない**([GDD02c §1.2](../03-gdd/02c-price-and-budget.md) の囲み)。帯の定数がパンで決まっているのはこのためで、他 4 品目には緩い |
| **既存テストが 2 件落ちる** | 頭打ちを当てると `ObservationsTests.ObservationBecomesUsableOnTheNextDayNotToday` と `TradePipelineTests.UnaffordableNecessityCountsOnlyTheFundsShortfall` が落ちる(実測)。前者は**構成の前提が崩れた**(売れていない売り手の係数 1400‰ を「相場基準が立った証拠」に使っていた)ので構成を直す。後者は**自然発生の日が動いた**ので日と世帯を差し替える。どちらも §「作るもの」4 に書いた |
| **`NecessityIsSettledBeforePreference` の定数は動かさない** | `ScarceLiquidFunds = 100` / `AmpleLiquidFunds = 100_000` / 3 日 は据え置く。実測では潤沢な側は 1,000 でも 1 日目にビールが約定するようになったが、**[#81](https://github.com/stama72/visionary/issues/81) の検出器の再校正は判別力の再実測を伴う契約変更**であり(W2-09 で「止まるべきだった」と数え直した型)、本タスクでは remarks の実測値だけを書き換える。判別力は変異 M-4 で測り直す |
| **`hasSettledYesterday` は `bool`** | `isBankrupt` が `int` 0/1 なのは `HouseholdState.IsBankrupt` の型を写しているためで、こちらは `TradeSystem` 段1 が既に `bool hasSettled` を持っている。**値域を閉じる検査は要らない** |

## 作るもの

### 1. `OfferPrice.Calculate` の改訂(`Systems/OfferPrice.cs`)

[GDD02c §1・§1.1](../03-gdd/02c-price-and-budget.md):

```
前日に自分の約定が無い日: 価格係数‰ = min( 価格係数‰ , 1000 )
```

- シグネチャに **第 6 引数 `bool hasSettledYesterday`** を足す: `Calculate(int floorPrice, int marketReference, int sellableStock, int shipmentTargetStock, int isBankrupt, bool hasSettledYesterday)`
- 定数 `UnsoldCapPermille = 1000`(**単位のコメント必須**: ‰。売れなかった日の価格係数の上限、GDD02c §1.1)。`MaxCoefficientPermille` / `MinCoefficientPermille` / `BankruptCoefficientPermille` とは**別の定数**として置く(1000 は「相場どおり」の意味であって、係数の表の中点と同じ値であることは連動を意味しない — 既存の `BankruptCoefficientPermille` の remarks と同じ理由)
- **健全な枝(`isBankrupt == 0`)で** 在庫比から係数を求めた後、`hasSettledYesterday == false` なら `Math.Min(係数, UnsoldCapPermille)`。**`min` であって代入ではない** — 溢れている売り手の 500‰ はそのまま働く(§1.1「値下げ側には効かない」)
- **破産中の枝は触らない。** §1.1「破産中は 500‰ の固定が先に効くので、この規則は何もしない」。数値上は `min(500, 1000) = 500` なので枝の順序で結果は変わらず、**この順序に「この実装ミスで落ちる」行は立てられない**(規則1)。doc コメントに順序と理由を書くにとどめる
- 床の `Math.Max` はそのまま最後に掛ける。頭打ち後の値が床を下回れば床
- **doc コメントを直す。** summary に「前日に自分の約定が無い日は価格係数‰ を 1000 で頭打ちにする(§1.1)。判定は呼び出し側が品目ごとに帳簿から求める」を足す。**「これで帯が保たれる」とは書かない** — 保証するのは「約定が無い日に相場より上へ出さない」ことだけで、完売が続く売り手には効かない(§1.2 の囲み)

### 2. `TradeSystem` 段1 の配線(`Systems/TradeSystem.cs`)

- 段1 が既に求めている `bool hasSettled`(`MarketReference.TryPreviousDaySettledPrice` の戻り値。`TrySeller` へ渡している同じ値)を、**そのまま** `OfferPrice.Calculate` の第 6 引数に渡す
- **帳簿をもう一度走査しない。** 別の関数で判定し直すと、同じ日の `Sale` の母数(品目の絞り・前日の判定・輸出の扱い)が 2 通りに割れ、片方だけ直したときに緑のまま食い違う
- 段1 のコメントに「`hasSettled` は錨(§1.2)と頭打ち(§1.1)の両方に使う。母数は同じ(帳簿の `Sale`、品目で絞る、輸出を含む)」を 1 行足す

### 3. 帯の検出器(`tests/.../Systems/TradePipelineTests.cs`)

`OfferPricesStayWithinTheBandOverSixtyDays`:

- `WorldDefinition.M0`・シード 1・`FullPipeline`(既存のヘルパー)。**1 日(24 tick)ずつ 60 回進め、各日の `world.Market` の全エントリ**について `price <= BandMultiplier * definition.ExternalBuyPrice(key.ItemId)` を確かめる
- 定数 `BandMultiplier = 20`(**単位のコメント必須**: 倍。床に対する提示価格の帯の上限。実測の根拠は本仕様の「設計の前提」を写す — シード 1 の最大はパン 500 = 床の 9.26 倍・12 日目、素の master はビールが 20 日目に 21.6 倍)
- 失敗メッセージに **日・品目 Id・売り手 Id・提示価格・床** を入れる(帯を破った日と品目が読めないと、次に測る人が同じ走行を最初からやり直す)
- **市場に 1 次産品が載ることは無い**(職業の出力だけが売り注文になる。1 次産品の床は 0)。載った場合は `0 × 20 = 0` を超えて自然に落ちるので、別の分岐は要らない
- **doc コメントに issue の 2 点を写す**: (a) この検出器が緑であることは「帯が保たれる経済」を意味しない — #38 が無い世界では生産 0/日・約定 0/日・売り注文 2 件で平らになるので、**緑が保証するのは式の代数だけ**である。(b) [TDD01 §5.2](../04-tdd/01-sim-core-and-m0.md) の発散・硬直の判定関数ではない(あちらは約定価格の中央値・120 日窓で #41 が持つ)。**[GDD02 §8](../03-gdd/02-economy.md)-1 を満たしたことにはならない**
- **`ObservationsDoNotGrowWithoutBound` と別のテストにする**(同じ 60 日走行だが、あちらは `Knowledge` の件数、こちらは価格。1 つに畳むと片方の失敗がもう片方を隠す)

### 4. 旧語・旧実測値の追随と、落ちる既存テスト 2 件の修正

**doc コメントの旧語(4 件)。** [GDD02c §1.2](../03-gdd/02c-price-and-budget.md) の囲みは「複利発散の歯止め」「天井」から「**公比の上限(1.173/日)**」へ訂正済みで、**価格の水準そのものの天井は §1.2 に無い。** 水準の歯止めは §1.1 が持つ。

| 場所 | 旧語 | 直し方 |
| ---- | ---- | ------ |
| `MarketReference.cs` の `IsValid` の summary(現 163 行付近) | 「複利発散の歯止め」が壊れる | 「売り手と買い手の速さの区別(§1.2)が壊れる。買い手を遅くするのは公比の上限(1.173/日)を作るためであり、水準の歯止めは §1.1 が持つ」 |
| `MarketReferenceTests.cs` の `BuyerReferenceAveragesEveryValidObservation` の remarks(現 38 行付近) | 同上 | 同上。**変異の実測の記述(2026-09-19)はそのまま残す** — 変わったのは語だけで、実測は生きている |
| `BuyerDemand.cs` の `TryBuyer` を呼ぶ箇所のコメント(現 125 行付近) | GDD02c §1.2 の天井が消える | 「公比の上限(GDD02c §1.2、1.173/日)が消える」 |
| `BuyerDemandTests.cs` の `DemandUsesBuyerReferenceNotSeller` の remarks(現 310 行付近) | 同上 | 同上。**実測の記述(2026-09-20)は残す** |

**`TradePipelineTests.cs` の「#120 が直れば動く」(4 件)。** いずれも remarks の書き換えであり、**実測値は自分の走行で確かめてから書く**(下の値はフェーズ1 の実測)。

| 場所 | 今の記述 | 直し方 |
| ---- | -------- | ------ |
| `ObservationsDoNotGrowWithoutBound` の 2 つ目の remarks(現 210 行付近) | 「同じ 60 日走行で提示価格が 6 桁へ発散し売り注文が 2 件へ枯れることを、本テストは検出しない」 | 価格の側は `OfferPricesStayWithinTheBandOverSixtyDays` が見るようになった、と書き換える。**売り注文が 2 件へ枯れることは引き続き検出しない**(#38 の後に #120 が締める)は残す |
| 同 3 つ目の remarks(現 217 行付近) | 「実測値は #120 が直れば動く」 | 頭打ちを入れた走行の実測値(**シード 1・60 日で `totalKnowledge` = 210**)を日付つきで書く。**#38 が入ると再び動く**(売り注文が枯れなくなるので観測が増える)ことを残す |
| `NecessityIsSettledBeforePreference` の remarks(現 253〜266 行) | 「潤沢な資金(100000): 薪は 2 日目までに約定する。ビールは 3 日目に初めて約定する(1000 では 3 日目までに一度も約定しない)」「これらの実測値は #120 が直れば動く」 | 実測し直した値に置き換える: **絞った資金(100): 薪は 1 日目に約定、ビールは 3 日目まで 0 件(変わらず)。潤沢な資金(100,000): 薪もビールも 1 日目に約定する(1,000 でも同じ)。** 「#120 が直れば動く」は消し、**定数を据え置いた理由**(#81 の検出器の再校正は変異の再実測を伴う契約変更。本仕様「設計の前提」)を 1 行書く |
| `UnaffordableNecessityCountsOnlyTheFundsShortfall` の remarks(現 348 行付近)と本体(現 380 行付近) | 「世帯Id2、17 日目」「#120 が直れば動く」 | **世帯 Id 3、10 日目**へ差し替える(実測: シード 1・60 日で `UnaffordableNecessityCount > 0` になる (世帯, 日) は **(3, 10 日目) の 1 件だけ**)。本体は 9 日進めて 0、+1 日で 1、+1 日で 0。remarks の「#120 が直れば動く」は「**#38 が入れば動く**」に書き換える(値付けは直ったが、売り注文が枯れる経済の中の自然発生日であることは変わらない)。**同テスト 3 つ目のブロック「1 日目に世帯 Id 1 はビールを買わない」は実測でも変わらない**(1 日目にビールを買わないのは世帯 1・6・7・8) |

**落ちる既存テスト 2 件の修正。**

- **`ObservationsTests.ObservationBecomesUsableOnTheNextDayNotToday`。** 世帯 0 は買い手が居ないので約定が無く、頭打ちで係数が 1000‰ になる。相場基準は世帯 1 の売り注文(= 床)なので、2 日目の提示価格 = 床 になり `Assert.NotEqual(floorPrice, ...)` が落ちる。**直し方: 1 日目の後(2 日目を回す前)に、世帯 0 の帳簿 `world.Ledgers[0]` へ `Sale` の行を 1 件直接置く**(`ItemId = Item.Flour`、`Quantity = 1`、`UnitPrice = floorPrice`、`OccurredAt = Tick.Zero`、`Direction = LedgerDirection.Sale`、`CounterpartyId = 1`、`Terms = Cash`)。これで 2 日目は `hasSettled = true`、相場基準 = `CeilDiv(床 + 床, 2)` = 床、係数 1400‰(在庫 1 / 目標 5)→ 提示価格 = `ApplyPermille(床, 1400)` > 床。**テストの主張(1 日目に生まれた観測が 2 日目に使える)は変わらず、2026-09-16 の変異(観測の段を値付けの前へ移す)に対する判別力も残る** — 観測が 2 日目の日付で記録されれば相場基準が立たず床に落ちる。remarks に「`Sale` の行を置く理由(§1.1 の頭打ちを外すため。**約定が無い売り手は相場基準より上へ出ない** — この構成では相場基準 = `CeilDiv(床 + 床, 2)` = 床なので、提示価格が床のままになる)」を足す。**「床より上へ出ない」と書いてはならない**(フェーズ2 の訂正 → 下の「フェーズ2 が訂正した箇所」)
- **`TradePipelineTests.UnaffordableNecessityCountsOnlyTheFundsShortfall`。** 上の表のとおり (世帯 3, 10 日目) へ差し替える

### 呼び出し側の配線([process/02](../process/02-task-spec.md) 規則7)

| 何 | 書く | 読む | 踏めるか | 約束 |
| -- | ---- | ---- | -------- | ---- |
| `hasSettledYesterday` | 段1(`TryPreviousDaySettledPrice` の戻り値 `hasSettled`) | `OfferPrice.Calculate`(本タスク)と `MarketReference.TrySeller`(既存)の**両方** | **踏める**(両端が `TradeSystem.Step` の中。テスト #5) | **同じ 1 つの bool を 2 か所に渡す。** 母数は帳簿の `Sale`・品目で絞る・前日・輸出を含む。**#38 が輸出の帳簿行を足すと、輸出した売り手は値上げできるようになる** — それは §1.1 が意図した挙動(「窓口へ輸出した日も約定である」)だが、**輸出の行の `Direction` や `ItemId` の書き方を変えると値付けが黙って変わる** |
| 帯の定数 `BandMultiplier` | テスト(本タスク) | テストのみ | — | **仕様の値ではなく検出器の閾値である。** #38 が入って帯の実態が変わったら、#38 のフェーズ2 が実測して動かしてよい(その根拠を doc コメントに残すこと) |

## 落ちるべき条件(テスト)

「この実装ミスで落ちる」列が埋まらないテストは書かない(規則1)。

| #  | テスト | 検証内容 | この実装ミスで落ちる | 核心 |
| -- | ------ | -------- | -------------------- | ---- |
| 1  | `UnsoldSellerDoesNotRaiseAboveTheReference`(`OfferPriceTests`) | 床 30・相場基準 100・在庫 0・目標 10(係数 1500‰)。`hasSettledYesterday: false` → **100**(1000‰ で頭打ち)。`true` → **150** | 頭打ちを入れない(false でも 150)。引数を読まずに常に頭打ちにする(true でも 100) | **核心** |
| 2  | `UnsoldCapDoesNotLiftTheDiscount`(同) | 床 30・相場基準 100・在庫 20・目標 10(係数 500‰)。`false` → **50** | `Math.Min` ではなく `= 1000` で書く(50 が 100 になる。§1.1「値下げ側には効かない」が消える) | **核心** |
| 3  | `UnsoldCapKeepsTheFloor`(同) | 床 120・相場基準 100・在庫 0・目標 10。`false` → **120** | 頭打ちを係数ではなく最終価格に掛け、床の `Max` より後に置く(100 になる) |  |
| 4  | `BankruptSellerFixesCoefficientAtFivehundred`(既存を拡張) | 破産中 = 1・相場基準 100・在庫 0・目標 0。`hasSettledYesterday` が **true でも false でも 50**(床 30) | 破産中の枝で `hasSettledYesterday` を読んで 500‰ 固定を外す(true で 150 になる) |  |
| 5  | `UnsoldSellerIsCappedAtTheReferenceInThePipeline`(`TradeSystemTests`) | `BuildShoppingDefinition` に `shipmentDays` 引数(既定 1)を足して **2** を渡し、パン屋(世帯 0)に在庫 **1**(目標 2 → 在庫比 500‰ → 係数 1250‰)を持たせ、買い手を置かない。1 日目 → 床 10。他の売り手(Id 999)の観測 200(`ObservedAt = Tick.Zero`)を世帯主の `Knowledge` に置いて 2 日目 → **200**(1250‰ なら 250)。**対照**: 同じ構成で 2 日目を回す前に `world.Ledgers[0]` へ `Sale`(パン・1 個・単価 10・`Tick.Zero`)を 1 件置く → 相場基準 `CeilDiv(200 + 10, 2)` = 105、係数 1250‰ → **132** | 段1 が `Calculate` へ `true` 定数を渡す(200 が 250 になる)。段1 が `hasSettled` を渡さず別の関数で判定し直し、その関数が輸出や品目の絞りで食い違う | **核心** |
| 6  | `OfferPricesStayWithinTheBandOverSixtyDays`(`TradePipelineTests`) | M0・シード 1・60 日。**毎日**、`world.Market` の全件が `床 × 20` 以下 | 頭打ちを外す(ビールが 20 日目前後で 20 倍を超える)。最終日だけ見る(この変異は落ちないので、**毎日見る形を doc コメントで固定する**) | **核心** |
| 7  | `ObservationBecomesUsableOnTheNextDayNotToday`(`ObservationsTests`、既存の修正) | §4 の直し方で、2 日目の提示価格が床と異なる(**`ApplyPermille(床, 1400)`** を期待値として `Equal` で書いてよい) | 観測の段を値付けの前へ移す(2026-09-16 の変異。床のまま)。**§1.1 を入れたのに `Sale` の行を置かない**(床のまま — 構成の前提が崩れたことに気付かない) |  |
| 8  | `UnaffordableNecessityCountsOnlyTheFundsShortfall`(`TradePipelineTests`、既存の修正) | 世帯 3 が 9 日目まで 0、10 日目に 1、11 日目に 0 | (既存の判別力のまま。別表 D-1 の remarks を参照) |  |
| 9  | `NecessityIsSettledBeforePreference`(既存。コードは変えない) | 緑のまま。remarks だけ差し替える | — (判別力は変異 M-4 で測る) |  |
| 10 | `PreviousDaySettledPriceIsQuantityWeighted`(`MarketReferenceTests`、既存。変えない) | 品目違いの行を混ぜても数えない — **§1.1「判定は (売り手 × 品目) ごと」はこのテストが押さえている** | (既存) |  |
| 11 | `TradePipelineStillRunsDeterministically` / `TradeIsDeterministicAcrossRuns`(既存。変えない) | 同じシードで 2 回走らせて状態ハッシュが一致 | 頭打ちの判定に列挙順や乱数を使う |  |

### 変異(`mutator` に渡すもの)

**「核心」印 #1・#2・#5・#6 に対して 4 件。** 測るのはレビューの巡が閉じた後のコミット済み `HEAD`。結果は implementer がテストの doc コメントへ転記する(日付つき。[ADR-0013](../adr/0013-mutation-measurement-separated.md))。

| #   | 場所 | 変異 | 期待 |
| --- | ---- | ---- | ---- |
| M-1 | `OfferPrice.Calculate` | 頭打ちの `Math.Min(...)` の行を削る(`hasSettledYesterday` を読まない) | **赤**: #1(false で 150)・#5(250)・#6(ビールが帯を破る)。#2 は緑のまま(頭打ちが無くても 500‰ は 500‰) |
| M-2 | `TradeSystem.Step` 段1 | `OfferPrice.Calculate` へ渡す第 6 引数を `true` 定数にする | **赤**: #5・#6。**#1 は緑のまま**(単体は正しい — 配線だけが切れている経路) |
| M-3 | `OfferPrice.Calculate` | `Math.Min(係数, UnsoldCapPermille)` を `UnsoldCapPermille` の代入にする | **赤**: #2(50 が 100)。#1 は緑のまま(1500 → 1000 は同じ) |
| M-4 | `TradeSystem.RunOneHouseholdsShopping` | `demand.Lines` の走査を `.Reverse()` する(W2-08 テスト表 #29 / [#81](https://github.com/stama72/visionary/issues/81) の検出器の**再実測**。値付けが変わったので、絞った世界(資金 100)で嗜好が必需より先に決済される経路が今も再現するかを測る) | **赤**: `NecessityIsSettledBeforePreference` の `Assert.False(boughtBeer)`。**緑のままなら象限 I-a** — #81 の検出器が新しい経済で判別力を失っており、定数を据え置いた前提が崩れている。そのときは直さずに引き継ぎメモへ残し、issue へ落とす(定数の再校正はフェーズ1 の判断) |

### 別表 — フェーズ2 が訂正した箇所(象限 I-b)

**上の本文は implementer に渡した時点の指示であり、最終形ではない。** レビューで仕様そのものの欠陥が出た箇所だけを、フェーズ2 がここに記録する。

| 巡 | どこ | 何が誤りだったか | 訂正 |
| -- | ---- | ---------------- | ---- |
| 2(網羅パス) | §4「落ちる既存テスト 2 件の修正」の `ObservationBecomesUsableOnTheNextDayNotToday` の remarks 指示 | 「**約定が無い売り手は相場基準が立っても床より上へ出ない**」は偽。頭打ち後の提示価格は `max(床, ApplyPermille(相場基準, min(係数, 1000)))` であり、`ApplyPermille(x, 1000) = x` なので **相場基準 > 床 の売り手は相場基準そのもので並ぶ**。反例は本仕様のテスト表 #5(床 10・相場基準 200 の売れていない売り手が **200** を提示する)。この文を規則として読むと「§1.1 は売れない売り手を床へ引き戻す」= 水準の復元力があると理解され、[GDD02c §1.2](../03-gdd/02c-price-and-budget.md) の囲み(「ラチェットの停止であって復元力ではない。止まる水準は経路依存」)と正反対になる | 「**相場基準より上へ出ない**」に改め、この構成で床に落ちる理由(相場基準 = 床)を添える。**GDD / TDD は動かない** — §1.2 の囲みは元から正しい |

### 別表 — レビューの網羅パスが追加した変異

**変異を選ぶのは依頼側である**([ADR-0013](../adr/0013-mutation-measurement-separated.md))。レビュー2巡目の網羅パスが列挙した中から、フェーズ2 が 1 件を `mutator` へ渡す分として立てた。

| #   | 場所 | 変異 | 期待 | なぜ足したか |
| --- | ---- | ---- | ---- | ------------ |
| M-5 | `TradeSystem.Step`(観測の段を値付けの段より**前**へ移す。2026-09-16 に一度当てた変異と同じもの) | 当日の提示価格が当日の観測に入るようにする | **赤**: `ObservationsTests.ObservationBecomesUsableOnTheNextDayNotToday` | この変異の赤の実測(2026-09-16)は、**本タスクが差し替える前の `Assert.NotEqual` と、頭打ちが入る前の経済**に対するものだった。本タスクは同テストの構成(帳簿へ `Sale` 行を差し込む)と assert(`Assert.Equal(ApplyPermille(床, 1400), …)`)の両方を変えており、**現本体で判別力が保たれている証拠が無い**。**緑のままなら象限 I-a** — そのときは直さずに引き継ぎメモへ残し、issue へ落とす |

## 編集してよい文書

- **無し。** GDD / TDD / ADR は触らない。`docs/tasks/W2-11-unsold-price-cap-and-band.handoff.md` への追記はフェーズ2 の仕事

## 完了条件

- `dotnet build Visionary.sln -c Release` 警告 0
- `dotnet test Visionary.sln -c Release` 全件緑(**既存 2 件の修正を含む**)
- `dotnet format Visionary.sln --verify-no-changes --severity warn`
- テスト表 #1〜#8 が存在し、「この実装ミスで落ちる」列の変異で落ちる形になっている
- 変異 M-1〜M-4 を `mutator` が実測し、結果が doc コメントに転記されている
- `MarketReference.cs` / `MarketReferenceTests.cs` / `BuyerDemand.cs` / `BuyerDemandTests.cs` に「複利発散の歯止め」「§1.2 の天井」の語が残っていない(`grep -rn "複利発散の歯止め\|§1.2 の天井" src tests` が 0 件)
- `TradePipelineTests.cs` に「#120 が直れば動く」の語が残っていない
