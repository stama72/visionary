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
| `MarketReference.TrySeller` の畳み込み(他の売り手ごとに最新1件)。**窓口の観測もここに入る** | [GDD02c §1.2](../03-gdd/02c-price-and-budget.md) の表「他の売り手ごとに最新の1件」× [GDD02d §2.1・§2.2](../03-gdd/02d-external-market-and-money.md)(窓口は売り手 Id を持つ売り手である) | **一致**(2026-09-21 に現行版と突き合わせ。**ロジックは変えず doc コメントだけ足す** — 下記 決定5) |

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
| 1-a | **【訂正版】** `TradePipelineTests.SettledPricesAtTheCentreStayWithinTheBandOverSixtyDays`。`[Theory]` シード **1/2/3/7/42**・60日。**判定対象は `world.Ledgers` の約定価格**(`UnitPrice`)のうち、**買い手の区画が中心(`District.ExternalMarketDistrictId`)である `Purchase` の行**で、都市生産品(`!IsPrimaryItem`)が `ExternalBuyPrice(itemId) × 2` 以下 | **天井が構造的に縛る範囲**。中心に居る買い手は `IsWithinReach` が常に真なので窓口が必ず候補に入り、`StoreChoice` は `<` で比べる(同値なら都市内)ので、実効価格は外部売値を超えられない | 窓口が都市生産品を売らない / 店選択が窓口を候補に入れない / 導出のマージンが効いていない / 走査順や比較演算子を変えた | **【核心】** M-1: `ExternalMarket.TryOfferPrice` の先頭に `if (!definition.IsPrimaryItem(itemId)) { offerPrice = 0; return false; }` を戻す → **赤。シード2 が赤になることを明示的に確かめる**([#120](https://github.com/stama72/visionary/issues/120) の実測でシード2 は 28日目に床の20倍を超える) |
| 1-b | **【訂正版】** 同テストの第2の断定。**全区画の**都市生産品の約定が `ExternalBuyPrice(itemId) × PeripheralBandMultiplier` 以下。**この定数はフェーズ2 が実測して置く**(下記「周縁の緩みの定数」) | 周縁を含めた水準が発散していないこと([#120](https://github.com/stama72/visionary/issues/120) の閉じる条件「**継続して**上回らない」) | 完売枝のラチェットが周縁で再発する / 買い手が中心へ出向く経路が壊れる | — |
| 2 | 同上の**空振り防止**。各シードで、(i) 都市生産品の約定(`!IsPrimaryItem` の `LedgerEntry`)が1件以上、(ii) **そのうち中心区画の買い手の `Purchase` が1件以上**ある | 経済が止まって0件で緑になること、および 1-a の母集団が空で緑になることを防ぐ | 取引が成立しない値・パイプラインの配線漏れ・中心区画に買い手が置かれない配置 | — |
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

### 天井は区画ごとに柔らかい(2026-09-21 の訂正。フェーズ1)

**初版のテスト #1(「都市生産品の約定すべてが ×2 以下」)は誤りだった。** GDD が決定として置いているのは次であり、**周縁の約定が天井を上回ることは仕様の範囲内**である:

- [GDD02c §1](../03-gdd/02c-price-and-budget.md) の囲み — 「**周縁の売り手は、窓口へ行く手間のぶんだけ外部売値を上回っても売れる。** 式の中で clamp すると、この区画差が消える([#29](https://github.com/stama72/visionary/issues/29))」
- [GDD02d §2.2](../03-gdd/02d-external-market-and-money.md) の囲み — 「**したがって天井は区画ごとに柔らかい。** …これは欠陥ではなく、**プレイヤーが商人として『中心で仕入れて周縁で売る』を成立させる余地そのもの**である」
- [issue #149](https://github.com/stama72/visionary/issues/149) / [#120](https://github.com/stama72/visionary/issues/120) の閉じる条件も「帯を**継続して**上回らない」であって「一度も上回らない」ではない

**したがって断定は2段に分ける。** 1-a は**構造**(中心の買い手には窓口という選択肢が必ずある)、1-b は**水準**(周縁を含めて発散していない)である。**1-a が赤なら実装かモデルの欠陥、1-b が赤なら水準の問題**であり、区別できない1本の断定にしてはならない。

**1-a が構造的に成り立つ根拠**(フェーズ1 がコードを読んで確かめた。2026-09-21): `StoreChoice` は `IsWithinReach(buyer.DistrictId, visited)` が真なら窓口を候補に足し、買い手の区画が中心なら `ExternalMarket.IsWithinReach` は訪問区画に依らず真を返す。窓口の実効価格は `EffectivePrice.Calculate(外部売値, trust: 0, …)` = 外部売値であり、都市内の候補は `<` で比べるので、**都市内の実効価格が外部売値を上回る日は必ず窓口が選ばれる**。信用割引は値を下げる方向にしか働かない。**この読みが外れていれば 1-a が赤になる。**

### 周縁の緩みの定数(`PeripheralBandMultiplier`)— フェーズ2 が実測して置く

**この定数は仕様値ではなく、検出器の閾値である**(旧 `BandMultiplier = 20` が実測 9.26 倍から置かれていたのと同じ性格)。GDD は周縁の緩みを「外出の手間のぶん」と定めているが、**手間は時間(翌日の労働力)であって貨幣ではないので、緩みの上界を仕様から導けない。** したがって実測で置く。

1. **検出器は最初の違反で止まらない。** 5シード × 60日の全行を走査して**最大比(約定価格 ÷ 床)を求め、最後に1回だけ assert する**。失敗メッセージには最大比・シード・品目・日・買い手の区画を出す(初版の実装は最初の違反で止まっており、**超過の上限が測れていなかった**)
2. フェーズ2 は5シードの最大比を実測し、**その約2倍の整数**を `PeripheralBandMultiplier` に置く。**実測値と測定日を doc コメントに書く**
3. **実測の最大比が 4 を超えたら(= 定数が 8 を超えるなら)止まって報告する。** [#120](https://github.com/stama72/visionary/issues/120) の 20 倍・67 倍と近づきすぎ、検出器として意味を失う。そこは水準の問題であり、フェーズ1 の裁定が要る

**定数の値を実測で置き直すことだけは、フェーズ2 の裁量として明示的に許可する**(タスク仕様の他の箇所を書き換えてよいという意味ではない)。

### 窓口の観測が売り手の相場基準に入ることは、現行仕様どおりである(決定5)

フェーズ2 の報告2(`MarketReference.TrySeller` が窓口の観測を「他の売り手」として畳む)について:

- [GDD02c §1.2](../03-gdd/02c-price-and-budget.md) の表は売り手の材料を「**他の売り手ごとに最新の1件** + 自分の前日の約定単価」と定めている。**窓口は売り手である** — [GDD02d §2.1](../03-gdd/02d-external-market-and-money.md) が売り手 Id を予約し、§2.2 が「観測も通常どおり生まれる。視界半径の例外は置かない」と決めている
- **したがって取り込むのが現行仕様であり、`MarketReference` は変えない。** 窓口を除外する実装こそが GDD からの逸脱になる
- **ただし「本タスクまで構造的に不活性だった経路が活性化した」ことは事実である。** `MarketReference.TrySeller` の doc コメントに、窓口の観測が畳み込みに入ること(と、その出所が [GDD02c §1.2](../03-gdd/02c-price-and-budget.md) × [GDD02d §2.2](../03-gdd/02d-external-market-and-money.md) であること)を1段落で足す。**`TrySeller` のロジックは1行も変えない**

### 窓口が都市生産品を並べたことで期待値が変わる既存テスト

実測(2026-09-21、フェーズ1)で赤になっている10件 — `TradeSystemTests` 6件(`ExportUsesTheSellerSideMarketReference` / `SellerReferenceIsTakenBeforeTheSellableStockGate` / `BudgetGateUsesTheEffectivePriceOnly` / `ErrandPlannerAndSettlementAgreeOnQuantity` / `ExportAddsToTheErrandLaborLoss` / `ObservationsCoverEveryVisitedDistrictEvenWithoutAPurchase`)・`ErrandPlannerTests` 2件・`ObservationsTests` 1件・`TradePipelineTests.UnaffordableNecessityCountsOnlyTheFundsShortfall` — の扱い:

- **そのテストが何を判別するテストかが保たれるなら、期待値を実測で置き直してよい。** 置き直した行には、**変わった理由**(窓口が都市生産品を並べた / 相場基準に窓口の観測が入った)を doc コメントで1行残す
- **判別力そのものが吸収されるなら(例: 窓口が常に最安になって、そのテストが見ていた分岐に二度と入らない)、置き直さずに止まって報告する。** 期待値を合わせるだけで緑になるが、**検出器としては死んでいる**
- **`EconomySystemTestFixtures` の配置・初期値を動かして「窓口が効かない世界」を作るのは可**(中心から遠い区画に置く・既に記憶を持たせる)。ただし**帯の検出器(1-a / 1-b)の世界は `WorldDefinition.M0` のままである**

## 編集してよい文書

- `docs/tasks/W2-13-external-sell-price-ceiling.handoff.md`(引き継ぎメモ)

**`docs/03-gdd/` `docs/04-tdd/` `docs/adr/` は触らない。** 本タスクが実装する規則は [#120](https://github.com/stama72/visionary/issues/120) が既に GDD02d へ書き終えている(master の `5da7e51`)。触ったらパイプラインが `SPEC-OUTSIDE` で止まる。

## このタスクで特に効く規約

- **導出は `IntegerMath.ApplyPermille` を通す。** `買値 * 2` と書くと、マージンを 1000 以外へ動かした瞬間に意味が変わる。切り上げの向きも `ApplyPermille` が持つ
- **`ExternalSellPrice` の分岐は `IsPrimaryItem` で切る。** 品目Idの範囲(0〜3)で切らない — `WorldDefinition` は品目の意味を知らない(`WorldDefinitionTests` の `PrimaryGoodCostsMatchTheExternalSellPrices` の囲みが述べている規約)
- **検出器のシードは `[Theory]` の `InlineData` で持ち、`WorldGenerator.Generate` と `SimScheduler` の両方に同じ値を渡す**(既存のテストと同じ組み立て)

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **`PeripheralBandMultiplier` を5シードの最大比の実測から置き、実測値と測定日を doc コメントに書いた**(実測の最大比が 4 を超えたなら、置かずに止まって報告する)
- [ ] **【核心】M-1〜M-4 を `mutator` が実測し(レビューの巡が閉じた後)、結果を doc コメントへ転記した。M-1 は「シード2 で赤になること」まで報告に含める**
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
