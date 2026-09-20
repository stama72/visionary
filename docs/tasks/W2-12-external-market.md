# W2-12: 都市外市場(輸入の候補合成・輸出の閾在庫・窓口の観測)と、段の順序の検出器

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#38](https://github.com/stama72/visionary/issues/38) / [#107](https://github.com/stama72/visionary/issues/107)(相乗り) |
| 根拠     | [GDD02d §2〜§5](../03-gdd/02d-external-market-and-money.md) / [GDD02c §1.2・§2](../03-gdd/02c-price-and-budget.md) / [GDD06 §3・§3.1](../03-gdd/06-trade-and-negotiation.md) / [TDD01 §3.2・§3.3・§3.8](../04-tdd/01-sim-core-and-m0.md) |
| ブランチ | `feat/38-external-market`                                            |
| worktree | `visionary/`(本体)                                                  |

> **この文書は使い捨ての作業指示である。** 実装完了時点で凍結し、以後の正はコードと GDD/TDD。

## スコープ

**GDD02d §2 の都市外市場の窓口を配線する。** 機構そのものは GDD02d が既に決定として持っており、本タスクが作るのは**呼び出し側**である — 予約 Id(`HouseholdState.ExternalMarketSellerId`)・外部価格の読み口(`WorldDefinition.ExternalSellPrice` / `ExternalBuyPrice`)・中心区画の定数(`District.ExternalMarketDistrictId`)はすべて既にあり、**どこからも呼ばれていない。**

**[#107](https://github.com/stama72/visionary/issues/107) を相乗りさせる。** #107 は「段4 を段5 へ畳む変異に検出器が無い」(#97 のレビュー3巡目・持ち越し)で、**本番コードの変更を伴わないテスト1件**である。相乗りさせる理由は2つ:

- **本タスクが同じ型の穴を1件増やす。** 新設する段6(輸出)にも「段5 の世帯ループへ畳まない」という契約があり(畳むと世帯 0 が先に輸出して、世帯 1 が買えるはずの在庫が消える)、やはり検出器が無い。**フィクスチャの形は #107 と同一である**(世帯 A の段5 の行動が、世帯 B の観測可能な値を動かすか)。片方だけ守ると、次に段を足す人が「段の順序は守られている」と読む
- **本タスクが段の構成そのものを変える**(段を7段にする)。#107 を後続にすると、同じ doc コメントと同じ段の説明を2回触ることになる

**相乗りは下のテスト表 #22・#23 の2件に限る。** #107 の issue 本文が挙げた「検出器の作り方の候補」をそのまま採り、それ以上広げない。**どちらも本番コードに触れないので、行き詰まったら 2 件とも落として #38 だけを閉じてよい**(そのときは #107 を開いたままにし、落とした理由を引き継ぎメモに書く)。

**含まない:**

- **メトリクスの出力と §12 検証項目の判定関数([#41](https://github.com/stama72/visionary/issues/41))。** 本タスクが負うのは「**帳簿に残ること**」までである。貨幣の出入りは `LedgerEntry.CounterpartyId == HouseholdState.ExternalMarketSellerId` の行だけから数えられる状態にし、数える側は #41 が置く
- **[#29](https://github.com/stama72/visionary/issues/29) の実測そのもの。** 本タスクが置くのは**実験軸**(輸出の on/off)であって、両条件の比較と §12-4 の判定ではない
- **価格の発散([#120](https://github.com/stama72/visionary/issues/120))。** 下の「実測」のとおり **#38 は発散を悪化させる**が、歯止めは #120 が決める。本タスクで帯の定数やシードを動かさない
- **困窮の段階([#30](https://github.com/stama72/visionary/issues/30))。** 破産中の投げ売りが輸出に吸収されることが実測で出た(下記)。どう扱うかは #30 が決める
- **港・行商人・貿易商**([GDD11](../03-gdd/11-external-trade.md)。M1 以降)
- **GDD / TDD / ADR の変更。** GDD02d §2.2・§2.3 の追記はフェーズ1 が済ませ、本ブランチに既にコミットしてある。**実装が GDD と食い違うと思ったら止まる**(`SPEC-OUTSIDE`)

### 変えないと宣言する既存コード([process/02](../process/02-task-spec.md) 規則8)

単位はファイルではなく規則。「一致」と書けるのは現行版の節を読んで突き合わせたときだけ。

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `OfferPrice.Calculate`(床 = 外部買値・在庫比・価格係数の 3 点・売れなかった日の頭打ち・破産中の 500‰ 固定) | [GDD02c §1・§1.1・§1.4](../03-gdd/02c-price-and-budget.md) | **一致**(#131 の追随表が確認済み)。**本タスクは提示価格の式に一切触らない** |
| `MarketReference.TryPreviousDaySettledPrice`(`Sale` の行だけ・品目で絞る・前日・**輸出を含む**・0 件なら false) | [GDD02c §1.1・§1.2](../03-gdd/02c-price-and-budget.md) | **一致**。ただし**輸出の行がここに実際に載るのは本タスクが初めてである** — #97 のテスト #4 は手で作った行で確かめており、パイプラインでは一度も踏まれていない |
| `MarketReference.TrySeller` / `TryBuyer`(速い/遅い、自分の売り注文の除外、当日の除外、保持期間) | [GDD02c §1.2](../03-gdd/02c-price-and-budget.md) | **一致**。窓口の観測は `SellerId = int.MaxValue` なので `selfHouseholdId` と衝突せず、除外の枝を通らない |
| `BuyerBudget.Decide` のゲート順(相場 → 利潤上限 → 現金上限)と「実効価格は 1 以上」の前提 | [GDD02b §5.2](../03-gdd/02b-consumption-and-household.md) / [GDD02c §2.1](../03-gdd/02c-price-and-budget.md) | **一致**。**窓口の未知価格の見積もりを 0 でなく 1 にしたのは、この前提を守るためである**([GDD02d §2.2](../03-gdd/02d-external-market-and-money.md)) |
| `TradeSettlement.Execute`(世帯間の約定。双方に1行・用途で在庫の行き先が変わる・用途で移動平均を更新) | [GDD02b §3・§3.2](../03-gdd/02b-consumption-and-household.md) / [GDD02a §5.1](../03-gdd/02a-production.md) | **一致**。窓口は**別のメソッド**にする(下記) |
| `Observations.Expire` / `CollectAndShare`(`Market` を1回だけ走査・自分の売り注文を除く・世帯全員へ配る・失効の境界) | [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) / [GDD08 §8.1](../03-gdd/08-household-and-decision.md) | **一致**。**窓口は `Market` に載らないので、この走査には乗らない** — 別の関数で足す |
| `ErrandPlanner` の貪欲・増分の取り方・T の上限・労働損失を外出ごとに切り上げること | [GDD06 §3](../03-gdd/06-trade-and-negotiation.md) | **一致**(#98 で確認済み) |
| `StoreChoice` の `Market` 走査順と `<` による同値の解決(売り手 Id 最小が残る) | [GDD06 §3](../03-gdd/06-trade-and-negotiation.md) / [ADR-0002](../adr/0002-time-model-and-determinism.md) | **一致**。窓口を**走査の後ろに**足すことが「同値なら都市内が勝つ」([GDD02d §2.1](../03-gdd/02d-external-market-and-money.md))の実体になる |
| `TradePipelineTests.OfferPricesStayWithinTheBandOverSixtyDays` の `BandMultiplier = 20` とシード 1 | (W2-11 が置いた検出器。GDD の節ではない) | **不一致(→ [#120](https://github.com/stama72/visionary/issues/120) へ)**。下の実測のとおりシード 2 は 67.20 倍で帯を破る。**シードを足さない・定数を動かさない・テストを消さない。** doc コメントに実測を 1 行足すだけにする |

## 設計の前提(フェーズ1 で決めたこと)

### 実測(2026-09-20、master `010afa0` の使い捨て worktree に代理実装。worktree は破棄済み・コードは本ブランチに無い)

`WorldDefinition.M0` + `ProductionSystem` + `ConsumptionSystem` + `TradeSystem`、120 日、シード 1/2/3/7/42。

| | master(`010afa0`) | 本タスクの代理実装 |
| -- | ------------------ | ------------------ |
| 生産回数/日(d1 / d10 / d30 / d60) | 64 / 0〜3 / 0 / 0 | 64 / **39〜48** / 0〜19 / 4〜19 |
| 売り注文/日(d1 / d60) | 10 / 0〜2 | 10 / **2〜3** |
| 内部の `Sale` 行(120 日) | 92〜122 | **404〜580** |
| 貨幣総量の変化(120 日) | 0(閉じた系) | −7,053〜+5,947 |
| 60 日の最大 提示価格/床 | 3.61〜**9.26** 倍 | 7.63〜**67.20** 倍 |
| 工具の `Purchase` 行(120 日) | **4〜6** | 4〜15 |

**この表から本仕様が引く結論は4つある。**

1. **#38 は崩壊を消さない。遅らせるだけである。** 生産は 10 日目に回復する(0 → 39〜48 回/日)が、30 日目には 0 になるシードが残る。**「生産が続いていること」「売り注文が 10 件あること」を完了条件やテストに置いてはならない** — 赤になる。本タスクの閉じる条件は issue のとおり「買える・捌ける・帳簿に残る」までである
2. **価格の発散は #38 で悪化する。** 輸出は毎日 `Sale` の行を作るので、[GDD02c §1.1](../03-gdd/02c-price-and-budget.md) の「売れなかった日は値上げしない」が効かない売り手が増える。**帯のテストが緑のままなのはシード 1(7.63 倍)を使っているからであって、機構が帯を保っているからではない**
3. **貨幣の恒等式([GDD02d §4.1](../03-gdd/02d-external-market-and-money.md))は全シードで厳密に成立した**(例: シード 3 で +5,947 = 21,180 − 15,233)。**これを「核心」のテストに据える**
4. **「W2 では工具の約定が構造的に起きない」は現行の master では成り立たない。** [#37 の申し送り](https://github.com/stama72/visionary/issues/38#issuecomment-5745986550)は当時の実測だが、`010afa0` では #38 が無くても 120 日で 4〜6 件ある(#97・#98・#131 で経路が変わった)。**したがって「工具の約定が1件以上」のテストは #38 の検出器ではない** — 耐久の枝(段5b の `BuyerBudget.QuantityInUnits`)の回帰ガードである。テストは置くが、doc コメントにこの区別を書く

### 決めたこと

| どこ | 何を決めたか |
| ---- | ------------ |
| **窓口の未知価格の見積もり** | **1**(最小の正の価格)。[GDD02d §2.2](../03-gdd/02d-external-market-and-money.md) に追記済み。0 にしないのは `BuyerBudget.Decide` / `TradeSettlement.FundsCap` が「実効価格は 1 以上」で投げるためで、**この前提を崩す形の実装(`Decide` の検査を緩める等)を採ってはならない** |
| **輸出は段5 と段6(観測)の間の新しい段である** | 段を1つ足して、観測を段7 へずらす。**段5 の世帯ループへ畳まない** — 畳むと先に輸出した世帯の在庫減が、後の世帯の買い物に効く。**観測(いまの段6)より前に置く**のは、持ち込んだ世帯がその日中心に居たことになる([GDD02d §2.3](../03-gdd/02d-external-market-and-money.md) 追記)ためである |
| **輸出の閾在庫が使う相場基準は段1 が求めたものである** | [GDD02c §1.2](../03-gdd/02c-price-and-budget.md) 末尾「売り手側(速い側)」。段1 の `MarketReference.TrySeller` の結果を世帯ごとに控えて段6 へ渡す。**段6 で `TryBuyer` を呼び直してはならない** — 遅い基準で「まだ相場は高い」と読んで持ち込みが遅れる。**取り違えても例外は出ない** |
| **段1 は販売在庫 0 の世帯についても相場基準を控える** | いまの段1 は `sellableStock <= 0` で `continue` しており、その手前に相場基準の算出が無い。**算出を `continue` より上へ移す。** 移さないと、段5 で工具を買った鍛冶(耐久は工房在庫へ入る = 販売在庫が増える唯一の経路)の閾在庫が「相場基準が無い日」の枝に落ちる。**移しても提示価格は変わらない**(販売在庫 0 の日は売り注文を出さないまま) |
| **窓口の約定は `TradeSettlement` の別メソッドである** | `Execute` は `HouseholdState seller` を要求する。窓口に世帯は無い。**`Execute` に `null` を通す形にしない** — 売り手側の在庫・資金・帳簿を動かす枝が `if (seller != null)` だらけになり、世帯間の約定の不変条件が読めなくなる |
| **`IsExportEnabled` は `WorldDefinition` の末尾の省略可能引数(既定 `true`)** | 既存の呼び出しはすべて名前付き引数なので、末尾に足せば追随は不要。**外部価格と同じく「設定と暦から決まる定数」であって状態ではない**ので `World` の区分もハッシュ行も増えない([GDD02d §2.1](../03-gdd/02d-external-market-and-money.md) / [TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md)) |
| **落ちる既存テストは 5 件** | 実測。`ErrandPlannerTests.PrimaryItemLinesCreateNoErrand` / `TradePipelineTests.TradeNeitherCreatesNorDestroysGoods` / `TradeSystemTests.BankruptSellerPostsTheHalvedFloorInThePipeline` / `TradeSystemTests.NecessityIsSettledBeforePreference` / `TradePipelineTests.UnaffordableNecessityCountsOnlyTheFundsShortfall`。**扱いは §「作るもの」8 に書いた。5 件を超えたら止まる**(`IMPL-BLOCKED`)— 本仕様の実測と食い違ったという報せである |

### 却下した設計案

- **窓口を `Market` に実体化する** — [GDD02d §2.1](../03-gdd/02d-external-market-and-money.md) が決定として却下済み。`Market` のキーは売り手**世帯**であり、実体化すると決定論ハッシュに「`Market` は含める / 窓口は含めない」の二重定義が生まれる。**しかもどちらに実装しても同一設定の2回実行は一致するので CI が検出しない**
- **窓口の当日価格を全世帯に見せる**(所在が公開なら価格も公開)— [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) が「所在は公開、価格は距離 R の外からは見えない」と明記している。見せると隅の世帯が季節変動を先読みでき、情報の摩擦が窓口の側から抜ける
- **輸出を段5a の外出の計画に畳む** — 余剰はその日の世帯間取引が確定するまで決まらない([GDD02d §2.3](../03-gdd/02d-external-market-and-money.md)「確定したあと」)。計画に畳むと、持ち込む量を予測する別の規則が要る

## 作るもの

### 1. `ExternalMarket`(新規、`src/Visionary.Sim/Systems/ExternalMarket.cs`)

窓口の純関数。`World` を受け取らない(`OfferPrice` / `BuyerBudget` / `Errand` と同じ切り出し方)。

```csharp
public static class ExternalMarket
{
    public const int UnknownPriceFloor = 1;

    public static bool TryOfferPrice(WorldDefinition definition, Tick now, int itemId, out int offerPrice);
    public static bool IsWithinReach(int buyerDistrictId, IReadOnlyList<int> visitedDistrictIds);
    public static int ExportThresholdStock(
        int externalBuyPrice, bool hasMarketReference, int marketReference,
        int shipmentTargetStock, int isBankrupt);
}
```

- `UnknownPriceFloor = 1`(**単位のコメント必須**: 貨幣。1次産品の見積もりの下限。[GDD02d §2.2](../03-gdd/02d-external-market-and-money.md))
- `TryOfferPrice`: `definition.IsPrimaryItem(itemId)` が偽なら **false**(0 を返すのではない)。真なら `definition.ExternalSellPrice(itemId, GameDate.FromTick(now).Season)`。**`IsPrimaryItem` の判定を省くと `ExternalSellPrice` が `ArgumentException` を投げる** — 都市生産品でも呼ばれる位置に置くので、判定はこの関数の中に閉じる
- `IsWithinReach`: `buyerDistrictId == District.ExternalMarketDistrictId` または `visitedDistrictIds` が中心を含む。線形探索(高々8件。`Dictionary`/`HashSet` を持ち込まない、GDD06 §3)
- `ExportThresholdStock`([GDD02d §2.3](../03-gdd/02d-external-market-and-money.md)):

  ```
  破産中(isBankrupt == 1):      0                                   ← 先に判定する
  相場基準が無い:                 出荷目標在庫
  それ以外:
      係数*‰   = CeilDiv( 外部買値 × 1000 , 相場基準 )
      在庫比*‰ = clamp( 2 × (1500 − 係数*‰) , 0 , 2000 )
      閾在庫   = CeilDiv( 出荷目標在庫 × 在庫比*‰ , 1000 )
  ```

  - **破産中の判定を先に置く。** 相場基準の枝の後ろに置いても M0 の値では同じ結果になる場合が多く、**踏んでも気付けない**
  - 中間の積は `long`(`外部買値 × 1000` と `出荷目標在庫 × 在庫比*‰`)
  - **`clamp` の上限は 2000 である。** 1000 にすると閾在庫が出荷目標在庫を超えられず、「都市の相場が高い日は輸出が減る」という応答([GDD02d §2.3](../03-gdd/02d-external-market-and-money.md))が消える
  - **境界の具体例**(木材加工の薪。外部買値 10・出荷目標在庫 108):

    | 相場基準 | 係数*‰ | 在庫比*‰ | 閾在庫 | 意味 |
    | -------- | ------ | -------- | ------ | ---- |
    | 無し | — | — | **108** | 出荷目標在庫で代用 |
    | 6(床の 0.6 倍) | 1667 | **0** | **0** | 2/3 を下回る → 全量を持ち込む |
    | 7 | 1429 | 142 | **16** | |
    | 10(床) | 1000 | 1000 | **108** | 出荷目標在庫ちょうど |
    | 14 | 715 | 1570 | **170** | |
    | 20(床の 2 倍) | 500 | **2000** | **216** | 上限に張り付く |
    | 30(床の 3 倍) | 334 | 2000(clamp) | **216** | 2 倍より上では動かない |
    | 任意(破産中) | — | — | **0** | |

### 2. `StoreChoice.TrySelect` に窓口の候補を足す(`Systems/StoreChoice.cs`)

[GDD02d §2.1・§2.2](../03-gdd/02d-external-market-and-money.md):

- **`world.Market` の走査が終わったあと**に1件足す。条件は `ExternalMarket.TryOfferPrice(...)` が真 **かつ** `ExternalMarket.IsWithinReach(buyer.DistrictId, visitedDistrictIds)`
- 更新は既存と同じ `!found || effectivePrice < bestPrice`。**走査の後ろに置いたうえで `<` を使うことが「同値なら都市内の売り手が勝つ」の実体である**([GDD02d §2.1](../03-gdd/02d-external-market-and-money.md))。`<=` にしない・走査の前に置かない
- `StoreCandidate` は `SellerId = HouseholdState.ExternalMarketSellerId`、`DistrictId = District.ExternalMarketDistrictId`、実効価格は `EffectivePrice.Calculate(窓口の提示価格, trust: 0, _definition.TrustDiscountPermille)`(都市内の店とまったく同じ経路)
- **在庫の条件を適用しない**(無限在庫)。**`world.Households[sellerId]` を引く経路を通らない**(`int.MaxValue`)
- **自分から自分へ売買しない検査も通らない** — 窓口はどの世帯でもない
- 売り手の在庫圧力は適用しない([GDD02d §2.2](../03-gdd/02d-external-market-and-money.md))。提示価格はその日の外部売値そのもの

### 3. `ErrandPlanner` に窓口の見積もりを足す(`Systems/ErrandPlanner.cs`)

**これが無いと中心区画の 2 世帯しか輸入できない。** `StoreChoice` は「その区画に居ること」を要求するので、行く理由が立たなければ窓口は使われない。

- `TryCheapestEstimate` の**世帯の走査が終わったあと**に、`isTargetDistrict(District.ExternalMarketDistrictId)` かつ 1次産品なら窓口の見積もりを比べる
- 窓口の見積もり価格(5.3 の3段を窓口に当てたもの。[GDD02d §2.2](../03-gdd/02d-external-market-and-money.md)):

  1. **距離(買い手の区画, 中心) ≤ R** → その日の外部売値。**都市内の店と違って「売り注文が無いので候補から外す」枝は無い** — 窓口は毎日すべての1次産品を並べる
  2. **有効な記憶** → 世帯主の観測のうち `SellerId == HouseholdState.ExternalMarketSellerId` かつ品目一致で最新のもの(`now.DayIndex − 観測日 ≥ 1`)
  3. **`ExternalMarket.UnknownPriceFloor`(= 1)**

- **既存の `TryEstimateOfferPrice` の3段目(`_definition.ExternalBuyPrice(itemId)`)を1次産品で通してはならない** — 1次産品は外部買値を持たず `ArgumentException` を投げる。**いまは到達しない**(どのレシピも1次産品を出力しないので `OutputsItem` が偽になる)が、窓口の経路は別に書く
- `EffectivePrice.Calculate(..., trust: 0, ...)` を通してから比べる(都市内の店と同じ)
- **`ErrandPlan` に `TotalTravelHours` を足す**(単位: 時間)。段6 が T の残りを読む。`Plan` が既に持っている `totalTravelHours` をそのまま入れる

### 4. `Observations.CollectWindow`(`Systems/Observations.cs`)

[GDD02d §2.2](../03-gdd/02d-external-market-and-money.md) / [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md):

```csharp
public static void CollectWindow(
    WorldDefinition definition, World world, HouseholdState household,
    IReadOnlyList<int> visitedDistrictIds);
```

- 既存の `IsWithinVisionRadius(household.DistrictId, visitedDistrictIds, District.ExternalMarketDistrictId)` が偽なら何もしない。**視界半径の例外は置かない**([GDD02d §2.2](../03-gdd/02d-external-market-and-money.md))
- 真なら **1次産品を品目 Id 昇順に1件ずつ**。`LocationId = District.ExternalMarketDistrictId`、`SellerId = HouseholdState.ExternalMarketSellerId`、`Price` = その日の外部売値、`Source = Direct`、`ObservedAt = world.Now`
- 世帯全員(`MemberNpcIds` 昇順)へ同じレコードを配る(既存と同じ)
- **`CollectAndShare` に畳まない。** あちらは `world.Market` を1回だけ走査することが重複を構造で防いでおり、窓口は `Market` に載らない。畳むと走査の中に「`Market` に無いもの」を混ぜることになる
- **自分の売り注文の除外は要らない** — 窓口はどの世帯でもない

### 5. `TradeSettlement` に窓口の約定を2つ足す(`Systems/TradeSettlement.cs`)

```csharp
public static void ExecuteImport(
    World world, HouseholdState buyer, DemandPurpose purpose, int itemId,
    int quantity, int unitEffectivePrice, int acquisitionCostSmoothingPermille);

public static void ExecuteExport(
    World world, HouseholdState seller, int itemId, int quantity, int unitPrice);
```

- **`ExecuteImport`**: 買い手側だけ動かす。支払額は `long` で積んで `checked` で `int` へ。用途で在庫の行き先が変わる(必需・嗜好 → 世帯在庫、それ以外 → 工房在庫)。**帳簿は買い手側の1行だけ**(`Purchase`・相手 = 予約 Id・`Cash`)。仕入れ移動平均は `ProductionInput` / `Durable` のときだけ更新する(既存 `Execute` と同じ規則。[GDD02a §5.1](../03-gdd/02a-production.md))
- **`ExecuteExport`**: 売り手側だけ動かす。`LiquidFunds += 外部買値 × 数量`、`WorkshopInventory[itemId] -= 数量`、**帳簿は売り手側の1行だけ**(`Sale`・相手 = 予約 Id・`Cash`・単価 = 外部買値)。**仕入れ移動平均は更新しない**(売りである)
- **`Sale` で書くこと・相手を予約 Id にすることが契約である。** `MarketReference.TryPreviousDaySettledPrice` が既にこれを約定として数えており([GDD02c §1.2](../03-gdd/02c-price-and-budget.md))、書き方を変えると売り手の錨と [§1.1](../03-gdd/02c-price-and-budget.md) の頭打ちが黙って輸出を落とす
- どちらも `quantity <= 0` / `unitPrice <= 0` を投げて拒む(既存 `Execute` と同じ理由 — 0 個の行が `StateHasher` に乗る)
- **窓口の側には帳簿を持たせない**([GDD02d §2.1](../03-gdd/02d-external-market-and-money.md)「在庫・帳簿・流動資金・家計を持たない」)

### 6. `TradeSystem` の配線(`Systems/TradeSystem.cs`)

**段を1つ足して7段にする。** doc コメントの段の説明も直す。

**段1(値付け)**

- `hasSellerReference[]` / `sellerReference[]` を世帯数ぶん確保し、**`sellableStock <= 0` の `continue` より上で** `MarketReference.TrySeller` の結果を書く(上の「決めたこと」参照)
- それ以外は変えない

**段5b(買い物)**

- `bool isWindow = store.SellerId == HouseholdState.ExternalMarketSellerId`
- **`isWindow` なら `world.Households[store.SellerId]` を引かない**(`int.MaxValue`)。売り手の在庫による切り詰めもしない(無限在庫)ので `actualQuantity = min(購入量, 資金上限)`
- 約定は `TradeSettlement.ExecuteImport`。都市内は従来どおり `Execute`
- **`UnaffordableNecessityCount` の数え方は変えない**(経路(1)は現金上限のゲート、経路(2)は `fundsCap == 0`)

**段6(輸出。新設)**

`_definition.IsExportEnabled` が偽なら段ごと飛ばす。真なら**世帯 Id 昇順**に:

```
出力品目   = レシピの出力[0](出力1件はコンストラクタが保証)
販売在庫   = seller.WorkshopInventory[出力品目]        ← 段5 の後の値
販売在庫 <= 0 なら何もしない
閾在庫     = ExternalMarket.ExportThresholdStock(
                 外部買値(出力品目), hasSellerReference[id], sellerReference[id],
                 出荷目標在庫(現在の職業から導く), seller.IsBankrupt)
超過分     = max( 0 , 販売在庫 − 閾在庫 )
超過分 <= 0 なら何もしない

中心が「居る区画」(ExternalMarket.IsWithinReach)でなければ:
    往復時間 = Errand.TravelHours(自区画, 中心, TravelHoursPerDistrict)
    段5a の往復時間の合計 + 往復時間 > DisposableHours なら **持ち込まない**(何もしない)
    そうでなければ:
        ErrandLaborLossPermille += Errand.LaborLossPermille(段5a の委託先の労働力係数‰, 往復時間, T)
        訪問区画に中心を足す          ← 段7 の観測がこれを読む

TradeSettlement.ExecuteExport(world, seller, 出力品目, 超過分, 外部買値)
```

- **段5a が求めた `ErrandDelegate` と `plan.TotalTravelHours` を世帯ごとに控えて渡す。** 段6 で `OpportunityCost.SelectErrandDelegate` を呼び直さない — いまは同じ値を返すが、**呼び直す形は「段5a と段6 で委託先が違う日がありうる」という契約を静かに作る**
- **労働損失は加算である**(`=` ではない)。段5a が書いた当日の合計に足す。[GDD06 §3](../03-gdd/06-trade-and-negotiation.md)「外出ごとに切り上げてから合計する」
- **訪問区画のリストは段5a の `List<int>` を持ち回り、段6 が中心を足す。** `visitedDistrictIdsByHousehold` の型を `List<int>[]` にする。段5b へ渡すのも同じ実体でよい(段5b は読むだけ)
- **具体例(T の境界)**: 区画 0 の世帯が段5a で往復 10 時間ぶんの外出を計画し(4 時間 + 6 時間)、中心へは行っていない。輸出の往復は距離 2 × 1 時間/区画 × 2 = **4 時間**。`10 + 4 = 14 > 12` なので**その日は持ち込まない**。段5a が 8 時間だった日は `8 + 4 = 12` で `> 12` ではないので**持ち込む**

**段7(観測)**

- 既存の `Observations.CollectAndShare` の直後に `Observations.CollectWindow` を呼ぶ(同じ世帯ループ・世帯 Id 昇順)

### 7. `WorldDefinition.IsExportEnabled`(`Definition/WorldDefinition.cs`)

- `public bool IsExportEnabled { get; }`。コンストラクタの**末尾に** `bool isExportEnabled = true` を足す
- doc コメント: 「輸出を行うか。[#29](https://github.com/stama72/visionary/issues/29) の実験軸。**状態ではない**ので `World` の区分も決定論ハッシュの行も増えない([TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md))」
- **`BuildM0` は明示的に渡さない**(既定 `true`)。既存の呼び出しはすべて名前付き引数なので追随は不要

### 8. 落ちる既存テスト 5 件の扱い

| テスト | なぜ落ちるか | どうするか |
| ------ | ------------ | ---------- |
| `ErrandPlannerTests.PrimaryItemLinesCreateNoErrand` | 1次産品の需要行で中心へ行くようになった(実測: `VisitedDistrictIds` が `[4]`)。**このテストは「窓口がまだ無い」ことを固定していた** | **`PrimaryItemLinesCreateAnErrandToTheCentre` に置き換える**(下のテスト表 #9)。doc コメントに「#38 で規則が反転した」と1行 |
| `TradePipelineTests.TradeNeitherCreatesNorDestroysGoods` | 輸入が財を増やし輸出が減らす(実測: 穀物 156 → 312 など) | **不変条件を書き直す** — 品目ごとに「都市の財の総量の変化 = 外部 `Purchase` の数量 − 外部 `Sale` の数量」。`= 0` ではなくなる。**世帯間の売買が財を作らないことは、この式の右辺に外部の行しか現れないことで守られる** |
| `TradeSystemTests.BankruptSellerPostsTheHalvedFloorInThePipeline` | 破産中は閾在庫 0 なので**前日の夕方に販売在庫を全量持ち込み**、翌朝は売り注文が立たない(実測: `KeyNotFound`) | **`IsExportEnabled = false` の `WorldDefinition` で構成し直す。** 半値の枝そのものは [GDD02c §1.4](../03-gdd/02c-price-and-budget.md) の規則であって輸出とは別である。doc コメントに「輸出を切って測っている。輸出が入ると投げ売りは床へ吸収される([GDD02c §1.4](../03-gdd/02c-price-and-budget.md) が [02b](../03-gdd/02b-consumption-and-household.md) へ送った論点。[#30](https://github.com/stama72/visionary/issues/30))」 |
| `TradeSystemTests.NecessityIsSettledBeforePreference` | 自然発生の値が動いた(実測: 期待 5 に対し 3) | **[#81](https://github.com/stama72/visionary/issues/81) の定数(`ScarceLiquidFunds` / `AmpleLiquidFunds` / 日数)は動かさない。** 期待値だけを実測に合わせ、remarks に新しい実測値と日付を書く。**判別力は変異 M-5 で測り直す** |
| `TradePipelineTests.UnaffordableNecessityCountsOnlyTheFundsShortfall` | 10 日目の自然発生の資金不足が起きなくなった(実測: 期待 1 に対し 0) | **自然発生する日と世帯を走行で探し直して差し替える。** 見つからなければ止まる(`IMPL-BLOCKED`)— 構成を人工的に作り替えるのは検出器の契約変更であり、本タスクの範囲外 |

**5 件を超えて落ちたら止まる(`IMPL-BLOCKED`)。** 本仕様の実測と食い違ったという報せであり、黙って直してよいものではない。

### 9. 気付いたら直す(CLAUDE.md)

- `tests/Visionary.Sim.Tests/State/HouseholdStateTests.cs` の `ExternalMarketSellerIdIsAboveEveryHouseholdId` の remarks が旧語「実質コスト」を使っている([#112](https://github.com/stama72/visionary/issues/112) 表C)。現行は**「実効価格」**([GDD06 §2](../03-gdd/06-trade-and-negotiation.md) / [GDD02c §2](../03-gdd/02c-price-and-budget.md))
- `TradePipelineTests.OfferPricesStayWithinTheBandOverSixtyDays` の doc コメントに1行足す: 「**`BandMultiplier = 20` が成り立つのはシード 1 だけである。** #38 の実測(2026-09-20)でシード 2 は 60 日で 67.20 倍(37 日目・薪・床 10・価格 672)。シードを足すと赤になる。歯止めは [#120](https://github.com/stama72/visionary/issues/120)」。**定数とシードは動かさない**

## 落ちるべき条件(テスト)

**この節が完了条件そのもの。** 目標は「通ること」ではなく「**壊したときに落ちること**」である。

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心(当てる変異 / 期待) |
| - | ------ | -------- | -------------------- | ------------------------- |
| 1 | `ExportThresholdStockInvertsThePriceCoefficient` | §1 の境界表そのもの(相場基準 6 / 7 / 10 / 14 / 20 / 30 に対し 0 / 16 / 108 / 170 / 216 / 216) | `CeilDiv` を `FloorDiv` にする。`clamp` の上限を 1000 にする。`2 ×` を落とす。`1500 −` の向きを逆にする | **核心** / M-1: `clamp` の上限を 2000 → 1000。期待 **赤** |
| 2 | `ExportThresholdStockFallsBackToTheShipmentTarget` | 相場基準が無い日 = 出荷目標在庫 | 0 を返す(= 相場基準が無い日に全量を持ち込み、都市の買い手の前から在庫が消える。[GDD02d §2.3](../03-gdd/02d-external-market-and-money.md)) | |
| 3 | `BankruptSellerExportsEverything` | 破産中は相場基準の有無によらず 0 | 破産中の枝が無い。相場基準の枝より後ろに置く(相場基準が無い日の破産中が出荷目標在庫になる) | |
| 4 | `WindowOfferPriceAppliesTheSeasonCoefficient` | 木炭が夏(1250‰)と冬(750‰)で違う。都市生産品では false | 季節を無視して基準値を返す。`IsPrimaryItem` の判定を省く(都市生産品で `ArgumentException`) | |
| 5 | `WindowIsACandidateOnlyWhenTheCentreIsReachable` | 自区画が中心 / 訪問に中心を含む / どちらでもない、の3枝。3つ目は候補0件 | 到達判定を落とす(**全世帯が移動せずに輸入でき、空間の摩擦が輸入の側から抜ける**) | **核心** / M-2: `StoreChoice` の `IsWithinReach` の呼び出しを外して常に候補にする。期待 **赤** |
| 6 | `CitySellerWinsTheTieAgainstTheWindow` | 都市内の売り手と窓口の実効価格が同値のとき都市内が選ばれる | 窓口を走査の前に足す。更新を `<=` にする | |
| 7 | `WindowIsNotACandidateForCityGoods` | 都市生産品では窓口が候補に出ない | `IsPrimaryItem` の判定を省く | |
| 8 | `ImportDoesNotIndexTheHouseholdArray` | 中心区画の世帯が1次産品を窓口から買う(段5b をパイプラインで踏む) | `world.Households[store.SellerId]` を無条件に引く(`IndexOutOfRange`)。在庫で切り詰める(窓口は無限在庫なのに 0 個になる) | |
| 9 | `PrimaryItemLinesCreateAnErrandToTheCentre` | 1次産品の需要行を持つ世帯の `VisitedDistrictIds` が中心を含む | **`ErrandPlanner` に窓口を足さない**(= 中心区画の 2 世帯しか輸入できず、他の 8 世帯は入力切れのまま) | **核心** / M-4: `TryCheapestEstimate` から窓口の枝を外す。期待 **赤** |
| 10 | `WindowEstimateFallsBackToOneWithoutAMemory` | 距離 R の外・記憶なしの世帯の見積もりが 1 | `ExternalBuyPrice` を呼ぶ(1次産品で `ArgumentException`)。当日の外部売値を使う(**距離 R の外から窓口の値段が見える** = [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) 違反)。0 を返す(`BuyerBudget.Decide` が投げる) | |
| 11 | `WindowEstimateUsesTheMemoryBeforeTheFloor` | 前日の観測があればその価格、当日の観測は使わない | 記憶の段を飛ばす。当日(差 0)の観測を使う(日内の相互参照が復活する) | |
| 12 | `WindowObservationIsBornForEveryPrimaryItem` | 中心が視界内の世帯の `Knowledge` に、1次産品 4 件が予約 Id・区画 4・当日価格で入る。世帯全員に同じレコード | 都市生産品も作る。`LocationId` を観測者の区画にする。世帯主だけに配る | |
| 13 | `WindowObservationIsNotBornOutOfSight` | 中心から距離 2 以上で、その日中心へ行っていない世帯には生まれない | 視界の判定を落とす(**視界半径の例外**。[GDD02d §2.2](../03-gdd/02d-external-market-and-money.md)) | |
| 14 | `ExportErrandIsSkippedWhenTheDayIsFull` | §6 の具体例(段5a が 10 時間 → 持ち込まない / 8 時間 → 持ち込む) | T の検査が無い(1日 12 時間以上歩く)。比較を `>=` にする(12 時間ちょうどで持ち込まなくなる) | |
| 15 | `ExportTripAddsTheCentreToTheObservedDistricts` | 中心へ行かずに輸出した世帯が、その日に窓口の観測を得る | 訪問区画に中心を足さない([GDD02d §2.3](../03-gdd/02d-external-market-and-money.md) 追記) | |
| 16 | `ExportAddsToTheErrandLaborLoss` | 既に外出している世帯の労働損失に**足される**(上書きされない) | `=` で代入する(段5a の買い物の損失が消える) | |
| 17 | `MoneyChangesOnlyByTheExternalLedger` | M0・60 日・シード 1: `Σ(流動資金の変化) == Σ(外部 Sale の額) − Σ(外部 Purchase の額)`。実測で厳密に成立する | 輸入か輸出のどちらかが記帳を落とす。相手 Id を都市の世帯にする。輸出で双方に記帳する。単価に実効価格でない値を書く | **核心** / M-3: `ExecuteExport` の記帳を落とす(資金と在庫だけ動かす)。期待 **赤** |
| 18 | `ImportsAndExportsAreRecordedAgainstTheReservedId` | 60 日で外部 `Purchase` ≥ 1 かつ 外部 `Sale` ≥ 1。外部の行の相手 Id は予約値のみ | 窓口の帳簿を世帯として持つ(相手 Id が `int.MaxValue` 以外になる) | |
| 19 | `GoodsChangeOnlyByTheExternalLedger` | 既存 #8 の書き直し。品目ごとに「都市の財の総量の変化 = 外部 `Purchase` の数量 − 外部 `Sale` の数量」 | 輸入が在庫を増やさない。輸出が在庫を減らさない。用途による行き先(世帯在庫/工房在庫)を間違えても**総量では落ちない**ので、これは #12 の守備範囲外である | |
| 20 | `ExportCanBeTurnedOff` | `IsExportEnabled = false` で 60 日回すと外部 `Sale` が 0 件。**輸入は起き続ける** | 実験軸が輸入まで止める。フラグを読んでいない | |
| 21 | `ToolsAreTradedInThePipeline` | 60 日で工具の `Purchase` が 1 件以上([#37](https://github.com/stama72/visionary/issues/38) 申し送り2) | **(訂正。下記)** | |

| 22 | `DemandIsBuiltBeforeAnyHouseholdShops` | **[#107](https://github.com/stama72/visionary/issues/107)。** 世帯 A(Id 小)が世帯 B(Id 大)から買い、その売上で B の `DemandLine.CashCap` が閾をまたぐ帯に置く。B の予算が**その日の朝の資金**から決まる | 段4 のループを段5 のループへ畳む(B の予算が A の購入の後で立ち、**予算そのものが世帯 Id の走査順の関数になる**。[TDD01 §3.3](../04-tdd/01-sim-core-and-m0.md))。#97 以降は**利潤上限まで**走査順の関数になる | |
| 23 | `ExportRunsAfterEveryHouseholdHasShopped` | 世帯 A(Id 小)に閾在庫を超える販売在庫を持たせ、世帯 B(Id 大)がその品目を買う。**B は A の在庫を買えている** | 段6 を段5 の世帯ループへ畳む(A が自分の順番で輸出してしまい、B が着いたときには在庫が閾在庫まで減っている)。[GDD02d §2.3](../03-gdd/02d-external-market-and-money.md)「世帯間の取引が**確定したあと**」 | |

> **#22・#23 は同じ形の契約を別の段について守る。** どちらも「世帯 A の段5 の行動が、世帯 B の値を動かすか」を見る。**2 件を1つのテストへ畳まない** — 片方の失敗がもう片方を隠す。

> **#21 は #38 の検出器ではない。** 上の実測のとおり `010afa0` でも 4〜6 件ある。**耐久の枝の回帰ガードである**ことを doc コメントに書く(書かないと、次に読む人が「#38 が耐久の需要を立てた証拠」と読む)。

> **60 日走行のテストは既存の `FullPipeline` ヘルパーとシード 1 を使う。** シードを増やさない — 上の実測のとおり価格はシードによって 67 倍まで伸びる([#120](https://github.com/stama72/visionary/issues/120))ので、シードを増やすと**このタスクと関係のない理由で**赤になる。

### 変異(`mutator` が測る。レビューの巡が閉じた後に一度)

**選ぶのは本仕様である**([ADR-0013](../adr/0013-mutation-measurement-separated.md))。implementer は当てない。

| # | 場所 | 変異 | 期待 |
| - | ---- | ---- | ---- |
| M-1 | `ExternalMarket.ExportThresholdStock` | `clamp` の上限 2000 → 1000 | テスト #1 が**赤** |
| M-2 | `StoreChoice.TrySelect` | 窓口の `IsWithinReach` の条件を外し、1次産品なら常に候補にする | テスト #5 が**赤** |
| M-3 | `TradeSettlement.ExecuteExport` | 帳簿への追加を消す(資金と在庫だけ動かす) | テスト #17 が**赤** |
| M-4 | `ErrandPlanner.TryCheapestEstimate` | 窓口の枝を消す | テスト #9 が**赤** |
| M-5 | `TradeSystem` 段5b | `BuyerBudget.QuantityInUnits` の呼び出しを外し、`decision.Quantity` をそのまま使う | **測るだけ**([#37](https://github.com/stama72/visionary/issues/38) 申し送り1・3 の引き取り)。赤になるテストの名前と件数を報告する。**緑のままなら、それは耐久の換算が依然としてどこからも守られていないという実測である** — 直さずに #37 の後継として issue へ落とす |
| M-6 | `TradeSystem` 段4 | 段4 のループを消し、段5 のループの先頭で `_buyerDemand.Build` を呼ぶ | テスト #22 が**赤**([#107](https://github.com/stama72/visionary/issues/107) の閉じる条件そのもの) |
| M-7 | `TradeSystem` 段6 | 段6 のループを消し、段5 のループの末尾で `RunOneHouseholdsExport` を呼ぶ | テスト #23 が**赤** |

### 別表: レビューで足したテストと変異(フェーズ2)

**上のテスト表と変異表は implementer に渡した時点の指示であり、最終形ではない。** レビュー1巡目が「仕様が数えた実装ミスのうち、実際には検出器が当たっていないもの」を2件挙げた。**どちらも訂正先はこの文書の中で閉じる**(GDD / TDD には及ばない)。

| # | テスト | 検証内容 | 何が検出できていなかったか |
| - | ------ | -------- | -------------------------- |
| R-1 | `WindowObservationRecordsTheCentreNotTheObserversDistrict`(#12 の補強。**別のテストとして足す**) | **中心区画以外に居て、かつ中心が視界内**の世帯が得た窓口の観測の `LocationId` が `District.ExternalMarketDistrictId` であること | 既存 #12 は観測者を中心区画に置いているので `LocationId = household.DistrictId` と同値になり、仕様が数えた「`LocationId` を観測者の区画にする」変異が**緑のまま通る**。`LocationId` はいま `StateHasher` しか読まないので**同一設定の2回実行は一致し、CI が素通りする** |
| R-2 | `WindowEstimateUsesTodaysPriceWithinTheVisionRadius` | 買い手が**中心から距離 R 以内**に居るとき、窓口の見積もりが**その日の外部売値**であること(記憶でも床 1 でもない) | 仕様 §3 の見積もり3段のうち**段1 に検出器が無い**。#10 は段3、#11 は段2 で、どちらも買い手を区画 0(距離 2)に置いており段1 を通らない。落ちても季節係数が変わる日だけ値がずれ、**赤くなるテストが1件も無い** |
| R-3 | `ExportRunsAfterEveryHouseholdHasShopped`(#23 の補強。**表明を足す**) | 世帯 A が**実際に輸出した**こと(外部 `Sale` の行 ≥ 1)を、既存の表明に**加えて**見る | 現状の唯一の表明は世帯 B の在庫。閾在庫の式・出荷目標在庫・T の検査のいずれかが将来動いて輸出が起きなくなると、**緑のまま [#107](https://github.com/stama72/visionary/issues/107) の契約を守らなくなる** |

**変異の追加**(`mutator` が上の 7 件と一緒に測る):

| # | 場所 | 変異 | 期待 |
| - | ---- | ---- | ---- |
| M-8 | `Observations.CollectWindow` | `LocationId` を `District.ExternalMarketDistrictId` → `household.DistrictId` | テスト **R-1** が赤 |
| M-9 | `ErrandPlanner` の窓口見積もり | 段1(`District.Distance(...) <= District.VisionRadius` の枝)を消し、常に記憶/床へ落とす | テスト **R-2** が赤 |

### 別表(続き): レビュー2巡目(網羅パス)が見つけた穴

**2巡目は表 #1〜#23・R-1〜R-3 の「この実装ミスで落ちる」列を全数突き合わせた。** 挙がっていた実装ミス 44 件のうち **3 件は、そのテストを実際には落とさなかった**。下はその訂正と、仕様が名指ししながら検出器を割り当てていなかった契約の引き取りである。

#### 仕様の訂正(象限 I-b)

**テスト表 #21 の「この実装ミスで落ちる」列は誤りだったので取り消す。** 旧記述は「段5b が `BuyerBudget.QuantityInUnits` を通らない(耐久の数量が耐久値のまま約定し、**`FundsCap` で 0 個へ落ちる**)」だったが、**`FundsCap` は `FloorDiv(流動資金, 実効価格)` であって数量に依存しない。** 耐久値のままの巨大な数量でも `actualQuantity` は 1 以上に残り、工具の `Purchase` 行は生まれる。つまり **#21 は耐久の換算の検出器ではない**(#38 の検出器でないことは元から断ってあったが、耐久の換算の検出器としては数えたままだった)。

**耐久の換算(`BuyerBudget.QuantityInUnits`)には現時点で検出器が無い。** これは M-5 の測定対象そのものであり、仕様が既に「**緑のままなら、それは耐久の換算が依然としてどこからも守られていないという実測である** — 直さずに #37 の後継として issue へ落とす」と決めている。**M-5 の期待を「緑」と机上で予測したうえで、`mutator` の実測がこれを確かめる。**

#### 足す検出器

| # | テスト | 検証内容 | 何が検出できていなかったか |
| - | ------ | -------- | -------------------------- |
| R-4 | `ExportThresholdStockRoundsTheCoefficientUp` | 既存 #1 の `[InlineData]` に **`(9, 84)`** を1行足す(外部買値 10・出荷目標在庫 108) | #1 の6点は**係数‰ の `CeilDiv` を `FloorDiv` にしても全点で一致する**(`mr=7`: 1429/1428 → 142/144 → ともに 16 など)。`mr=9` は `CeilDiv` で 1112 → 776 → **84**、`FloorDiv` で 1111 → 778 → **85** と分かれる。[GDD02d §2.3](../03-gdd/02d-external-market-and-money.md)「丸めの向き」の唯一の守り手 |
| R-5 | `ExportUsesTheSellerSideMarketReference` | 段6 の閾在庫が**売り手側(速い側)**の相場基準で決まること。**2日目以降**に踏み、`TrySeller` と `TryBuyer` が**異なる値**を返す構成を作り、販売在庫を2つの閾在庫の**あいだ**に置いて輸出の有無が入れ替わるようにする | 仕様は「**取り違えても例外は出ない**」と2か所で名指ししながら、検出器を1件も割り当てていない。段6 を踏むテスト(#14・#15・#16・#23)は**すべて初日**で、`MarketReference` の材料が0件なので両者とも false を返し**差が出ない**。遅い基準を読むと相場上昇局面で輸出が遅れ、[#29](https://github.com/stama72/visionary/issues/29) の比較の片側だけが静かに別物になる |
| R-6 | `SellerReferenceIsTakenBeforeTheSellableStockGate` | 段1 の相場基準の算出が `sellableStock <= 0` の `continue` **より上**にあること。段1 時点で販売在庫 0・有効な相場基準あり・段5b で自分の出力品目を工房在庫へ買い入れる世帯(M0 では工具を買った鍛冶)を作り、段6 の閾在庫が**相場基準ベース**で決まることを見る | 仕様「決めたこと」が明示的に決めた位置なのに、テスト表にも変異表にも固定する項目が無い。戻しても提示価格は変わらず(`Market` への書き込みは `continue` の下)、60 日走行の恒等式・存在命題はどちらでも成立し、**同一構成同士のハッシュも一致する** |
| R-7 | #19 と R-2 の空振り防止 | **#19** に「外部 `Purchase` と外部 `Sale` の数量合計が 1 以上」を足す。**R-2** に「その日の外部売値を `w` より低くした同じ構成では中心へ行く」肯定形のサブケースを足す | **#19** は不変条件を「= 0」から「= 外部 `Purchase` − 外部 `Sale`」へ書き換えたのに、空振り防止は旧テストの `anyLedgerEntry`(**世帯間の行でも真**)のままである。外部の約定が 0 件なら両辺とも 0 で緑になり、**書き換え前の主張しか検証していない**。**R-2** の表明は `Assert.Empty` だけなので、窓口の枝そのものが消えても緑になる。1巡目の R-3 と**同じ型** |

**変異の追加**:

| # | 場所 | 変異 | 期待 |
| - | ---- | ---- | ---- |
| M-10 | `ExternalMarket.ExportThresholdStock` | 係数‰ の `CeilDiv`(`外部買値 × 1000 ÷ 相場基準`)を `FloorDiv` へ。**閾在庫側の `CeilDiv` は動かさない** | テスト **R-4** が赤 |
| M-11 | `TradeSystem` 段6 | 段1 が控えた `hasSellerReference` / `sellerReference` を捨て、`MarketReference.TryBuyer` を呼び直した結果を使う | テスト **R-5** が赤 |
| M-12 | `TradeSystem` 段1 | `MarketReference.TrySeller` の呼び出しと2つの配列への代入を、`if (sellableStock <= 0) { continue; }` の**下**へ戻す | テスト **R-6** が赤 |

## 編集してよい文書

- `docs/tasks/W2-12-external-market.handoff.md`(引き継ぎメモ)

**GDD / TDD / ADR は触らない。** GDD02d §2.2・§2.3 の追記はフェーズ1 が済ませ、本ブランチに既にコミットしてある。**食い違いを見つけたら直さずに止まる**(`SPEC-OUTSIDE`)。

## このタスクで特に効く規約

- **予約 Id が配列の添字に入らないこと。** `HouseholdState.ExternalMarketSellerId` は `int.MaxValue` である。`world.Households[...]` / `world.Knowledge[...]` / `world.Ledgers[...]` の添字に渡す経路を作らない。**これは落ちるので気付ける**が、`ErrandPlanner` の床(`ExternalBuyPrice`)のように**いまは到達しないだけの経路**は落ちない
- **輸出の閾在庫が読むのは売り手側(速い側)の相場基準である。** 買い手側(`TryBuyer`)と取り違えても例外は出ない([GDD02c §1.2](../03-gdd/02c-price-and-budget.md) 末尾)
- **段の順序。** 輸出は世帯間取引の確定後・観測の前。段5 の世帯ループへ畳まない

## 完了条件

- [ ] 「落ちるべき条件」のテスト 23 件が全て緑(うち #22・#23 は [#107](https://github.com/stama72/visionary/issues/107) の相乗り)
- [ ] 落ちる既存テストが**ちょうど 5 件**で、§8 のとおりに直っている
- [ ] **「核心」印の変異 4 件(M-1〜M-4)と測定 3 件(M-5〜M-7)を `mutator` が実測し**(レビューの巡が閉じた後)、結果を doc コメントへ転記した
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
