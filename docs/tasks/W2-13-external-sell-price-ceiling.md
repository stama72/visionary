# W2-13: 窓口が都市生産品も売る(価格の天井)と、帯の検出器を複数シードで引き直す

| 項目     | 内容                                                       |
| -------- | ---------------------------------------------------------- |
| issue    | [#149](https://github.com/stama72/visionary/issues/149)     |
| 根拠     | [GDD02d §2.1・§2.2・§3・§3.2・§4.3 (f)・§4.4・§5](../03-gdd/02d-external-market-and-money.md) / [#120](https://github.com/stama72/visionary/issues/120) の決定 6〜8 |
| ブランチ | `feat/149-external-sell-price-ceiling`                      |
| worktree | 本体(`visionary/`)                                        |

**天井は `OfferPrice.Calculate` に入れない。** 買い手の店選択が窓口を選ぶことで働く([GDD02d §3](../03-gdd/02d-external-market-and-money.md) の囲み「天井は clamp ではなく、店選択から創発する」)。**値付けの式は1行も変えない。**

## スコープ

**含まない:**

- **`OfferPrice.Calculate` への上限の項**([GDD02 §8](../03-gdd/02-economy.md)-4 / [#29](https://github.com/stama72/visionary/issues/29))。式の中で clamp すると区画間の価格差が消える
- **[#131](https://github.com/stama72/visionary/issues/131) で入れた GDD02c §1.1 の頭打ちの除去。** 塞いでいる枝が違う(あちらは約定の無い売り手どうしの2周期ループ、天井は完売の枝)
- **売り注文の件数の下限の検出器** — [#148](https://github.com/stama72/visionary/issues/148)(鍛冶)が引き取っている
- **都市生産品の外部売値への季節係数**([GDD02d §5](../03-gdd/02d-external-market-and-money.md))。床が季節で動かない以上、天井も動かない
- **輸出(`TradeSettlement.ExecuteExport`)の規則。** 本タスクは買う側(窓口が売る側)だけを開く

**変えない既存コード**(規則8)。単位はファイルではなく規則:

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `OfferPrice.Calculate` の式 `max(床, 相場基準 × 価格係数)` と §1.1 の頭打ち(`Math.Min(係数, UnsoldCapPermille)`) | [GDD02c §1・§1.1](../03-gdd/02c-price-and-budget.md) / [02d §3](../03-gdd/02d-external-market-and-money.md)「式に上限の項は無い」 | **一致**(2026-09-21 に現行版と突き合わせ) |
| `ExternalMarket.ExportThresholdStock` の逆関数と clamp 上限 2000 | [GDD02d §2.3](../03-gdd/02d-external-market-and-money.md) | **一致**(同上) |
| `TradeSettlement.ExecuteImport` の記帳(相手 = 予約Id・`Purchase`・単価 = 実効価格) | [GDD02d §2.2](../03-gdd/02d-external-market-and-money.md)「合成された候補は中心に居る1人の売り手とまったく同じに扱う」 | **一致**(品目種別で分岐していないことを確認) |
| `ExternalMarket.IsWithinReach`(中心に届くか) | [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) | **一致** |
| `StoreChoice` の「窓口は走査の最後・`<` で比べる(同値なら都市内が勝つ)」 | [GDD02d §2.1](../03-gdd/02d-external-market-and-money.md) | **一致**。**この順序と比較演算子は本タスクで動かさない** |
| `WorldDefinition` の「1次産品は外部買値を持たない(0)」の検査 | [GDD02d §2.1](../03-gdd/02d-external-market-and-money.md) | **一致**。反転するのは売値側だけである |
| `MarketReference.TryPreviousDaySettledPrice` が窓口の約定を数える規則 | [GDD02c §1.1・§1.2](../03-gdd/02c-price-and-budget.md) | **未確認**(本タスクは触らない。ただし**窓口からの購入が増えると売り手の錨の材料が変わる** — 実測は [#41](https://github.com/stama72/visionary/issues/41) が拾う) |

## 作るもの

### 1. `WorldDefinition` — 交易マージン‰ と、都市生産品の外部売値の導出

`src/Visionary.Sim/Definition/WorldDefinition.cs`

- コンストラクタに `int tradeMarginPermille` を足す(既存の `trustDiscountPermille` の隣)。**単位のコメントを必須とする**(`// ‰。1000 = 帯の幅 2.0 倍(GDD02d §3)`)
- 公開プロパティ `public int TradeMarginPermille { get; }`
- **検査: `tradeMarginPermille < 1` を `ArgumentOutOfRangeException` で拒む。** [GDD02d §3.2](../03-gdd/02d-external-market-and-money.md) の成立条件 (f) は「導出の形で守る」ので、守るべきは値の組ではなく**マージンが正であること**である
- `ExternalSellPrice(int itemId, Season season)` の都市生産品での `ArgumentException` を外し、次を返す:

```
1次産品    : ApplyPermille( _externalSellPriceBase[itemId], _externalSellPriceSeasonPermille[itemId][(int)season] )   ← 現行のまま
都市生産品 : ApplyPermille( _externalBuyPrice[itemId], PermilleScale + TradeMarginPermille )                          ← 季節に依らない(GDD02d §5)
```

- **`externalSellPriceBase[都市生産品] == 0` の検査は残す(決定1)。ただし例外メッセージを現行の規則へ直す** — 「都市生産品は外部売値を持たない」は [GDD02d §2.1](../03-gdd/02d-external-market-and-money.md) が反転させた文であり、いま正しいのは「**都市生産品の外部売値は外部買値から導出するので、基準値の表には置かない**」である。検査そのものを外すと、表に置いた値が黙って無視される — すぐ下の季節係数行が「都市生産品の行はすべて 1000(使わないが、黙って効く値を置かせない)」で塞いでいるのと同じ穴である
- `BuildM0`: `const int TradeMarginPermilleForM0 = 1000; // ‰。帯 = [床, 床×2](GDD02d §3・§4.4)`。**`externalSellPriceBase` / `externalBuyPrice` の配列は1つも動かさない**

導出後の値([GDD02d §4.4](../03-gdd/02d-external-market-and-money.md) の校正表と一致すること):

| 品目 | 床(外部買値) | 天井(外部売値) |
| ---- | -------------- | ---------------- |
| 小麦粉(4) | 56 | **112** |
| 薪(5) | 10 | **20** |
| パン(6) | 54 | **108** |
| ビール(7) | 72 | **144** |
| 工具(8) | 290 | **580** |

### 2. `ExternalMarket.TryOfferPrice` — 全品目を並べる

`src/Visionary.Sim/Systems/ExternalMarket.cs:32-44`

`IsPrimaryItem` の早期 return を外し、常に `definition.ExternalSellPrice(itemId, 季節)` を返して `true` を返す。

- **シグネチャは変えない(決定2)。** `StoreChoice.cs:112` と `ErrandPlanner.cs:276` を触らずに済み、[GDD11](../03-gdd/11-external-trade.md) が窓口を行商人へ置き換えるとき「その日は並ばない」が戻る
- **`Try` が M0 では常に `true` であることを doc コメントに書く。** 書かないと、レビューと次の読者が「false になる日がある」と読む

### 3. `ErrandPlanner` — 見積もりの品目ゲートと、未知価格の床

`src/Visionary.Sim/Systems/ErrandPlanner.cs`

- **`:242` の `&& _definition.IsPrimaryItem(itemId)` を外す。** ここが issue #149 の「分岐が品目で切れているか」の現物である。外さない限り、窓口は都市生産品の見積もりの候補に入らない
- **`:311` の3段目(未知価格)を品目で分ける**([GDD02d §2.2](../03-gdd/02d-external-market-and-money.md)):

```
1次産品    : ExternalMarket.UnknownPriceFloor (= 1)      ← 外部買値を持たないので床が無い
都市生産品 : definition.ExternalBuyPrice(itemId)          ← 床を持つので通常どおり床で見積もる
```

- `:263-266` の `<remarks>`(「1次産品で通してはならない」)を現行の規則へ直す

### 4. `Observations.CollectWindow` — 窓口の観測を全品目に広げる

`src/Visionary.Sim/Systems/Observations.cs:124`

**issue #149 の表に無い5件目である(決定4)。** `IsPrimaryItem` の `continue` を外し、品目Id昇順に全品目の窓口価格を観測する。

- 根拠は [GDD02d §2.2](../03-gdd/02d-external-market-and-money.md)「観測(`PriceObservation`)も通常どおり生まれる。視界半径の例外は置かない」。窓口が都市生産品を並べる以上、その値も観測になる
- **外さないと何が起きるか**: 中心を離れた買い手は窓口の都市生産品価格を記憶できず、`EstimateWindowPrice` の2段目が永久に埋まらない。3段目(床)で見積もり続けるので、**実際には床の2倍の窓口を「床で買える店」と見て外出する**。天井そのものは中心に着いた日の `StoreChoice` が効かせるので帯は閉じるが、**外出の判断だけが恒久に歪む**
- `TradePipelineTests.ObservationsDoNotGrowWithoutBound` の `totalKnowledge` の実測値(210)は動く。**上界の式(NPC数 ×(世帯数 − 1)×(保持期間 + 1))は構造だけで決まるので緑のままのはずだが、doc コメントの実測値は測り直して書き換える**

## 落ちるべき条件(テスト)

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心(当てる変異 / 期待) |
| - | ------ | -------- | -------------------- | ------------------------- |
| 1 | `TradePipelineTests.SettledPricesStayWithinTheBandOverSixtyDays`(既存 `OfferPricesStayWithinTheBandOverSixtyDays` を**置き換える**)。`[Theory]` シード **1/2/3/7/42**・60日。**判定対象は `world.Ledgers` の約定価格**(`UnitPrice`)で、都市生産品(`!IsPrimaryItem`)の行すべてが `ExternalBuyPrice(itemId) × 2` 以下 | 天井が実際に約定を縛っていること | 窓口が都市生産品を売らない / 店選択が窓口を候補に入れない / 導出のマージンが効いていない | **【核心】** M-1: `ExternalMarket.TryOfferPrice` の先頭に `if (!definition.IsPrimaryItem(itemId)) { offerPrice = 0; return false; }` を戻す → **赤。シード2 が赤になることを明示的に確かめる**([#120](https://github.com/stama72/visionary/issues/120) の実測でシード2 は 28日目に床の20倍を超える) |
| 2 | 同上の**空振り防止**。各シードで都市生産品の約定(`!IsPrimaryItem` の `LedgerEntry`)が **1件以上**ある | 経済が止まって0件で緑になることを防ぐ | 取引が成立しない値・パイプラインの配線漏れ | — |
| 3 | 同上の**帯の上限をリテラル `2` で持つ**(`const int BandMultiplier = 2;`)。`ExternalSellPrice` を読まない | 検出器が交易マージン‰ から独立であること | 検出器が `ExternalSellPrice` を読むと、マージンを 5000 にしても緑のままになる | — |
| 4 | `M0CalibrationTests.TradeMarginIsPositive`(条件 (f)。[GDD02d §4.3](../03-gdd/02d-external-market-and-money.md)) | `TradeMarginPermille > 0` かつ、都市生産品5品目すべてで `ExternalSellPrice(i, 季節) > ExternalBuyPrice(i)`(全4季節) | 導出が `ApplyPermille(買値, 1000)`(マージンを足し忘れ)になっている | **【核心】** M-2: `ExternalSellPrice` の都市生産品の枝を `ApplyPermille(買値, PermilleScale)` にする(マージン0相当)→ **赤** |
| 5 | `WorldDefinitionTests.ExternalSellPriceOfCityGoodsIsDerivedFromBuyPrice` | M0 で 小麦粉 112 / 薪 20 / パン 108 / ビール 144 / 工具 580。**4季節すべてで同じ値**([GDD02d §5](../03-gdd/02d-external-market-and-money.md)) | 導出に季節係数を掛けた / 基準値(0)から導いた | — |
| 6 | `WorldDefinitionTests`(既存 `:620` の `Assert.Throws` を反転) | `ExternalSellPrice(Item.Bread, Season.Spring)` が例外を投げない | 反転の漏れ | — |
| 7 | `WorldDefinitionTests.WorldDefinitionRejectsNonPositiveTradeMargin` | `tradeMarginPermille: 0` と `-1` を `ArgumentOutOfRangeException` で拒む | (f) の守りが入口に無い | — |
| 8 | `WorldDefinitionTests.WorldDefinitionRejectsExternalSellPriceBaseOnCityGoods`(既存の検査が残ることの確認) | 都市生産品の `externalSellPriceBase` に 1 を渡すと `ArgumentOutOfRangeException` | 検査を「外す」側に倒した実装(決定1 の反対) | — |
| 9 | `ExternalMarketTests`(既存 `:119` の `foundForCityGood` を反転) | 都市生産品で `TryOfferPrice` が `true` を返し、値が導出した外部売値と一致する | §2 の実装漏れ | — |
| 10 | `StoreChoiceTests.BuyerAtTheCentreChoosesTheWindowWhenLocalOffersExceedTheCeiling` | 中心区画に、都市生産品を**外部売値より高く**並べた売り手を1人置く。買い手(中心に居る)が選ぶのは `HouseholdState.ExternalMarketSellerId` で、実効価格は外部売値 | 窓口が候補に入らない / 走査順が逆 | — |
| 11 | `StoreChoiceTests`(同値の対照) | 都市内の提示価格が**外部売値と同値**なら、選ばれるのは都市内の売り手である | `<` を `<=` にした / 窓口を走査の前に置いた | — |
| 12 | `ErrandPlannerTests.WindowIsACandidateForCityGoods` | 中心が対象区画のとき、都市生産品でも窓口の見積もりが最安候補に入る | `:242` の品目ゲートが残っている | — |
| 13 | `ErrandPlannerTests.UnknownWindowPriceOfCityGoodsUsesTheFloor` | 窓口の記憶も知覚も無い買い手の、都市生産品の窓口見積もりが `ExternalBuyPrice(itemId)`(**1 ではない**) | 3段目を品目で分けていない | **【核心】** M-3: 3段目を都市生産品でも `ExternalMarket.UnknownPriceFloor` にする → **赤** |
| 14 | `ObservationsTests.WindowObservationsCoverCityGoods` | 中心が視界内の世帯に、都市生産品5品目の窓口観測(`SellerId` = 予約Id・`Price` = 導出した外部売値)が生まれる | `CollectWindow` の品目フィルタが残っている | **【核心】** M-4: `Observations.CollectWindow` の `if (!definition.IsPrimaryItem(itemId)) continue;` を戻す → **赤** |
| 15 | `ObservationsTests`(既存 `:237`) | 窓口観測の価格が `ExternalSellPrice(itemId, 季節)` と一致する。**都市生産品を含めて**成り立つ | 導出と観測が別経路で計算されている | — |

**帯の検出器を「毎日」から「走行後に1回」へ変えてよい理由を doc コメントに書く。** 旧検出器が毎日見ていたのは `world.Market`(その日の提示価格。翌日に上書きされる)を見ていたからである。`world.Ledgers` は**追記のみで剪定されない**ので、60日走行後に1回走査すれば全日が対象になる。**この理由を書かずに1回走査へ変えると、次の読者は「最終日しか見ていない」と読む。**

**提示価格は天井を上回ってよい**([GDD02 §8](../03-gdd/02-economy.md)-5)。旧検出器が `world.Market`(提示価格)を見ていたのをそのまま `BandMultiplier = 2` にすると、仕様が許している値で赤になる。**置き換えるのは閾値だけではなく、判定対象そのものである。**

## 編集してよい文書

- `docs/tasks/W2-13-external-sell-price-ceiling.handoff.md`(引き継ぎメモ)

**`docs/03-gdd/` `docs/04-tdd/` `docs/adr/` は触らない。** 本タスクが実装する規則は [#120](https://github.com/stama72/visionary/issues/120) が既に GDD02d へ書き終えている(master の `5da7e51`)。触ったらパイプラインが `SPEC-OUTSIDE` で止まる。

## このタスクで特に効く規約

- **導出は `IntegerMath.ApplyPermille` を通す。** `買値 * 2` と書くと、マージンを 1000 以外へ動かした瞬間に意味が変わる。切り上げの向きも `ApplyPermille` が持つ
- **`ExternalSellPrice` の分岐は `IsPrimaryItem` で切る。** 品目Idの範囲(0〜3)で切らない — `WorldDefinition` は品目の意味を知らない(`WorldDefinitionTests` の `PrimaryGoodCostsMatchTheExternalSellPrices` の囲みが述べている規約)
- **検出器のシードは `[Theory]` の `InlineData` で持ち、`WorldGenerator.Generate` と `SimScheduler` の両方に同じ値を渡す**(既存のテストと同じ組み立て)

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **【核心】M-1〜M-4 を `mutator` が実測し(レビューの巡が閉じた後)、結果を doc コメントへ転記した。M-1 は「シード2 で赤になること」まで報告に含める**
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
