# W2-14: 販売在庫から設備分を留保する

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#154](https://github.com/stama72/visionary/issues/154)              |
| 根拠     | [GDD02c §1.3](../03-gdd/02c-price-and-budget.md) / [GDD02a §3](../03-gdd/02a-production.md) / [GDD02d §2.3](../03-gdd/02d-external-market-and-money.md) |
| ブランチ | `feat/154-sellable-stock-reserve`                                    |
| worktree | 本体ツリー(パイプライン)                                           |

**仕様は [#148](https://github.com/stama72/visionary/issues/148) で凍結済み。** 決定1〜7・追随表・訂正8〜11 は #148 のコメントが持ち、本書はそれを実装の形に落とすだけである。**設計判断が要ると気づいたら実装せずに止まる**(`PIPELINE: HALT SPEC-OUTSIDE`)。

## 前提 — #148 の数値は紙である

**#148 の検算(健全な鍛冶の工房在庫が `留保1 + 閾在庫3 = 4` に張り付く / 破産中は閾在庫 0 で工房在庫 1 を保つ / 留保の有無で定常の価格係数が変わらない)は、文書の規則を紙で回したものであって実装の実測ではない。**

したがって本タスクは**紙の値をテストの期待値に埋め込まない**。定常値は「実測して doc コメントへ転記する」対象であり、断定の閾値ではない。**唯一の例外はテスト6(破産中に工房在庫 1 が残る)で、これは在庫の構造から決まるので期待値にしてよい** — 閾在庫 0 のとき販売在庫の全量が窓口へ出るが、販売在庫の定義に留保が入っている以上、残るのは留保量そのものである。

## スコープ

**含む:**

1. 販売在庫の留保を1か所のヘルパーに集め、**5経路すべてをそこに通す**(#148 追随表の1行目)
2. 売り注文の件数の下限を見る検出器
3. `ProductionSystem` の doc コメント「工具切れでも0にはならず、半分の能力で続く」の訂正(鍛冶については偽)

**含まない:**

- **設備係数‰・所要労働‰・[GDD02d §4.4](../03-gdd/02d-external-market-and-money.md) の校正表を動かすこと。** #148 の決定1 で却下済み
- **外出損失が 300‰ を超えた日に鍛冶が止まること。** #148 の決定5 で「直さない」と決めた仕様である([GDD02a §2](../03-gdd/02a-production.md) に表として入っている)
- **初期累積摩耗による摩耗の位相ずらし。** #148 の訂正8 で撤回した。**`WorldDefinition.M0` の初期値は1つも動かさない**
- **[#158](https://github.com/stama72/visionary/issues/158) の環路**(窓口から天井で買った耐久が、後日 床で窓口へ出ていく)。**留保はこの環路を閉じない** — 留保は販売在庫から定数を引くだけで、輸出の発火点が1個ぶんずれるにすぎない。出自による区別は #158 / [#41](https://github.com/stama72/visionary/issues/41) が持つ
- **破産中フラグの判定条件**([#30](https://github.com/stama72/visionary/issues/30) の懸念2)
- **GDD / TDD / ADR の書き換え。** 本タスクが触るのはコードとテストと本書だけである

### 変えない既存コード(規則8。[02-task-spec](../process/02-task-spec.md))

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `ExternalMarket.ExportThresholdStock`(係数*‰ → 在庫比*‰ → 閾在庫の逆関数。破産中は 0、相場基準が無い日は出荷目標在庫) | [GDD02d §2.3](../03-gdd/02d-external-market-and-money.md) | **一致。** §2.3 の擬似コードは `超過分 = max(0, 販売在庫 − 輸出の閾在庫)` と**販売在庫の語で書かれている**ので、留保を入れても式は変わらない。変わるのは販売在庫の定義だけである |
| `WorldDefinition.ShipmentTargetStock`(出荷目標在庫 = 生産能力 × 出力数量 × 出荷日数) | [GDD02c §1.3](../03-gdd/02c-price-and-budget.md) | **一致。** 生産能力から導くので留保の影響を受けない(§1.3 が「下記の出荷目標在庫も生産能力から導くので影響を受けない」と明記) |
| `OfferPrice.StockRatioPermille` / `PriceCoefficientPermille` / 1000‰ の頭打ち | [GDD02c §1.1](../03-gdd/02c-price-and-budget.md) | **一致。** 式の**入力**(販売在庫)が変わるだけで、式も頭打ちの母数(帳簿の `Sale`・輸出を含む)も変わらない |
| `TradeSettlement.FundsCap` と走査順(必需 → 耐久 → 生産の入力 → 嗜好) | [GDD02b §3.2](../03-gdd/02b-consumption-and-household.md) | **一致。** §3.2 の `買える数量 = min(購入量, FloorDiv(資金, 単価))` は資金の側の規則であり、売り手の在庫による切り詰めは [GDD02c §1.3](../03-gdd/02c-price-and-budget.md)(届くのは販売在庫だけ)の側に立つ。**本タスクが差し替えるのは後者だけである** |
| `TradeSettlement.ExecuteImport`(買った耐久は工房在庫へ入る) | [GDD02b §3.2](../03-gdd/02b-consumption-and-household.md)「買った品の行き先は用途で決まる」 | **一致。** 鍛冶が買った工具が販売在庫に乗ること自体は仕様である(留保を引いた残りが乗る) |
| `ProductionSystem` の設備係数の二値(`工具在庫 ≥ 1 ? 1000 : 500`)と、生産 → 摩耗の順 | [GDD02a §3・§3.1](../03-gdd/02a-production.md) | **一致。** ただし**同ファイルの doc コメントの記述は偽**なので、やること3 で直す(規則そのものは変えない) |
| `ErrandPlanner`(段5a は販売在庫を読まない) | [GDD06 §3](../03-gdd/06-trade-and-negotiation.md) / [TDD01 §3.3](../04-tdd/01-sim-core-and-m0.md) 順5 | **一致。** #148 追随表が「留保が変えるのは購入時点(段5b)の候補と切り詰めだけ」と確認済み。**段5a に留保を持ち込まない** |

## 作るもの

### 1. `Systems/SellableStock.cs`(新規)

```csharp
namespace Visionary.Sim.Systems;

/// <summary>販売在庫(GDD02c §1.3)。工房在庫から、自分の生産の必要財ぶんを除いた量。</summary>
public static class SellableStock
{
    /// <summary>留保量(GDD02c §1.3)。単位: 個。</summary>
    public static int ReserveQuantity(WorldDefinition definition, HouseholdState household, int itemId);

    /// <summary>販売在庫 = max(0, 工房在庫[itemId] − 留保量)。単位: 個。</summary>
    public static int Of(WorldDefinition definition, HouseholdState household, int itemId);
}
```

**`Recipe` ではなく `(definition, household)` を取る。** 呼び出し側が「別の世帯のレシピ」を渡す取り違えを型の手前で消すためである(規則7 の「添字・並び・単位の約束」)。職業は**世帯の現在の値**から引く(`definition.Recipes[(int)household.Occupation]`)—— [#39](https://github.com/stama72/visionary/issues/39) の付け替えで変わる。**ただしこれは呼び出し側が工房在庫を直接読むことは防げない**(規則4)。そちらは下の4の番人が受け持つ。

`ReserveQuantity` の分岐([GDD02c §1.3](../03-gdd/02c-price-and-budget.md) の表そのまま。**上から順に、最初に当たった枝で決める**):

| 枝 | 条件 | 留保量 |
| -- | ---- | ------ |
| 設備 | `itemId == Item.Tools` | `ProductionSystem.EquipmentThresholdStock`(= 1) |
| 入力 | `itemId` が `recipe.Inputs` に含まれる | その `ItemQuantity.Quantity`(レシピ1回分) |
| どちらでもない | 上記以外 | 0 |

- **`itemId` が売り手の出力品目かどうかは見ない。** 見なくても結果は同じ(売り注文は出力品目にしか立たない)うえ、見ると「出力でない品目の工房在庫を売る経路」が将来生えたときに留保が黙って外れる
- **設備と入力の両方に当たる品目は M0 に無いので、合算しない。** 上の表は排他の分岐である。該当する品目が現れたら、合算するか否かは [GDD02c §1.3](../03-gdd/02c-price-and-budget.md) に決めてから実装する(**その形を作らないこと自体は機械で守れない**)
- **入力の枝は M0 では発火しない**(出力を自分の入力に使うレシピが無い)。#148 の決定3 が「規則としては置く」と決めたので実装し、テスト3 が合成レシピで踏む

### 2. `ProductionSystem.EquipmentThresholdStock`(新規の定数)と doc コメントの訂正

```csharp
/// <summary>設備係数‰ が 1000 になる最小の工具在庫(個。GDD02a §3)。</summary>
public const int EquipmentThresholdStock = 1;
```

- `RunOneHousehold` の `household.WorkshopInventory[Item.Tools] >= 1` をこの定数で書く。**留保量と設備の閾値が同じ 1 であることは偶然ではなく、[GDD02c §1.3](../03-gdd/02c-price-and-budget.md) が「設備係数‰ が 1000 になる最小在庫」と定義している**(#148 決定2)。2か所に別々のリテラルで置くと黙って食い違う
- **doc コメントの訂正**(やること3)。`:17-18` と `:53-54` の「工具切れでも0にはならず、半分の能力で続く」は**鍛冶については偽**である([GDD02a §3](../03-gdd/02a-production.md))。**能力2以上の職業に限ることと、鍛冶が工具を失う経路を塞いでいるのは [GDD02c §1.3](../03-gdd/02c-price-and-budget.md) の留保であって設備係数の 500‰ ではないことを書く**

### 3. 5経路を `SellableStock` に通す

| # | 場所 | 現在 | 差し替え後 |
| - | ---- | ---- | ---------- |
| 1 | `TradeSystem.cs:118`(段1 売り注文) | `household.WorkshopInventory[outputItemId]` | `SellableStock.Of(_definition, household, outputItemId)` |
| 2 | `TradeSystem.cs:361`(段6 輸出) | `seller.WorkshopInventory[outputItemId]` | `SellableStock.Of(_definition, seller, outputItemId)` |
| 3 | `TradeSystem.cs:310`(段5b 購入量の切り詰め) | `Math.Min(affordableQuantity, seller!.WorkshopInventory[line.ItemId])` | `Math.Min(affordableQuantity, SellableStock.Of(_definition, seller!, line.ItemId))` |
| 4 | `StoreChoice.cs:80`(店の候補) | `seller.WorkshopInventory[itemId] <= 0` | `SellableStock.Of(_definition, seller, itemId) <= 0` |
| 5 | `TradeSettlement.cs:64・211`(約定での減算) | 無条件に減算 | 下の4(番人) |

**3 と 4 を落とすと留保は素通りする。** 段1 が売り注文を控えても、店の候補と購入量の切り詰めは工房在庫を見ているので、**買い手は「並んでいないはずの1個」を買える**。#148 の追随表が5行を1つの issue にまとめているのはこのためである。

**窓口(`isWindow`)の枝は触らない。** `HouseholdState.ExternalMarketSellerId`(`int.MaxValue`)を `world.Households` の添字に通さない現在の形をそのまま保つ。

### 4. `TradeSettlement` の番人(留保を割らないことの最終的な担保)

`Execute` と `ExecuteExport` に **売り手の留保量**を引数で渡し、**減算する前に**検査する。

```csharp
public static void Execute(
    World world, HouseholdState buyer, HouseholdState seller, DemandPurpose purpose,
    int itemId, int quantity, int unitEffectivePrice, int acquisitionCostSmoothingPermille,
    int sellerReserveQuantity)          // ← 追加。単位: 個
```

- 検査: `seller.WorkshopInventory[itemId] - quantity < sellerReserveQuantity` なら `InvalidOperationException`。メッセージに売り手 Id・品目 Id・数量・工房在庫・留保量を含める
- **境界**: 減算後がちょうど留保量に等しいのは**正常**(`<` であって `<=` ではない)。例: 工房在庫 2・留保 1 のとき `quantity = 1` は通り、`quantity = 2` は例外
- **`ExecuteImport` には置かない。** 買い手側の在庫は増えるだけである
- 呼び出し側は `SellableStock.ReserveQuantity(_definition, seller, itemId)` を渡す。**`definition` そのものを `TradeSettlement` へ渡さない** — 現在の `acquisitionCostSmoothingPermille` と同じく、必要な値だけを int で受け取る形を保つ
- **これは呼び出し側の切り詰め漏れを落とすための番人であり、正常系では発火しない。** 発火したら実装かモデルの欠陥である

## 落ちるべき条件(テスト)

**順序・境界の具体例(規則6)**: 鍛冶(出力 = 工具・留保 1)について —— **工房在庫 0 → 販売在庫 0**(負にしない) / **1 → 0**(売り注文を出さない・店の候補に入らない) / **2 → 1**(1個だけ売れる) / **5 → 4**。

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心(当てる変異 / 期待) |
| - | ------ | -------- | -------------------- | ------------------------- |
| 1 | `SellableStockTests.ReserveOfTheSmithsOutputIsOneTool` | `WorldDefinition.M0` の鍛冶について、工具の留保量が 1 | 設備の枝が無い / 品目を取り違えた / 全職業に一律で置いた | — |
| 2 | `SellableStockTests.ReserveOfAnItemThatIsNeitherEquipmentNorInputIsZero` | パン屋のパン(出力だが設備でも入力でもない)の留保量が 0。**M0 の5レシピすべてについて、出力品目の留保量が鍛冶だけ 1・他は 0** | 全品目に一律1を留保した(校正表が動く) | — |
| 3 | `SellableStockTests.ReserveOfAnOutputThatIsAlsoItsOwnInputIsTheRecipeQuantity` | **合成レシピ**(出力品目を自分の入力にも持つ)で、留保量がその入力の `Quantity` と等しい | 入力の枝が未実装 / 1 で固定した | — |
| 4 | `SellableStockTests.SellableStockNeverGoesNegative` | 鍛冶の工房在庫 0 と 1 のどちらでも販売在庫が 0 | `max(0, …)` が無い → 負の販売在庫が `OfferPrice` の在庫比‰ と輸出の超過分へ流れる | **【核心】** M-6 |
| 5 | `TradeSystemTests.SmithDoesNotOfferItsLastTool` | 工房在庫 1 の鍛冶は工具の売り注文を出さない(`world.Market` にキーが立たない)。2 なら立つ | 段1 が工房在庫を直読みしている | **【核心】** M-2 |
| 6 | `TradeSystemTests.ExportLeavesTheReservedToolEvenWhenBankrupt` | **破産中**(`IsBankrupt` = true → 閾在庫 0)の鍛冶の**工房在庫を 4 に置いて**段6 に通すと、帳簿の輸出行の数量が **3**、残る工房在庫が **1**。**工房在庫 1 の鍛冶は輸出行が立たない**(販売在庫 0 で段6 が早期 return する) | 段6 が工房在庫を直読みしている → 工房在庫 0・翌日から能力 0 が永久化(#148 の実測した詰みそのもの) | **【核心】** M-3 |
| 7 | `TradeSystemTests.BuyerCannotBuyTheSmithsReservedTool` | 工房在庫 2 の鍛冶から買い手が買えるのは 1 個まで。約定後の工房在庫が 1 | 段5b の切り詰めが工房在庫を直読みしている(**留保が素通りする最短経路**) | **【核心】** M-4 |
| 8 | `StoreChoiceTests.SmithWithOnlyTheReservedToolIsNotACandidate` | 工房在庫 1 の鍛冶は工具の店の候補に入らない(他に候補が無ければ `TrySelect` が false) | 候補の判定が工房在庫を直読みしている | **【核心】** M-5 |
| 9 | `TradeSettlementTests.SettlementRejectsAQuantityThatBreaksTheReserve` | `Execute` / `ExecuteExport` のどちらも、留保を割る数量で `InvalidOperationException`。**ちょうど留保量まで減らす数量は通る** | 番人が無い / `<` と `<=` を取り違えた(正常な約定が落ちる) | — |
| 10 | `TradePipelineTests.ToolOffersNeverDisappearOverSixtyDays`(**検出器**。`[Theory]` シード 1/2/3/7/42) | 60日のどの日も、`world.Market` の工具の売り注文が **1件以上** | 留保が効いていない / 鍛冶の在庫が 1 個まで痩せた / 生産が止まった | **【核心】** M-1 |
| 11 | **【訂正版】** `TradePipelineTests.SmithNeverRunsOutOfToolsOverSixtyDays`(同シード。**下の「テスト11 の訂正」を読むこと**) | 60日のどの日も、鍛冶2戸それぞれの `WorkshopInventory[Item.Tools] >= 1`(= 設備係数‰ が 1000 を保つ) | 鍛冶が工具を売り切る(**master では day 1 に 0 になる**)/ 段5b・段6 が留保を素通りする | **【核心】** M-1 |
| 12 | 10・11 の**空振り防止**(同じテスト内の断定) | (i) 鍛冶が2戸存在する、(ii) 60日ぶん進んだ(`world.Now.DayIndex`)、(iii) 工具の売り注文の**延べ件数が 60 以上**。**これは日ごとの下限とは別の、弱い断定である** — 数えている対象そのものが空でないことだけを見る(健全な走行では鍛冶2戸ぶんで 120 前後になる) | 母集団が空・走行が進んでいない・職業の配置が変わって鍛冶が居ない、で緑になること | — |

### 当てる変異(`mutator` が測る。[ADR-0013](../adr/0013-mutation-measurement-separated.md))

**それぞれ単独で当てる**(M-1〜M-6 を混ぜない。**M-3・M-4 が2か所にまたがるのは、下の「複合変異である理由」のとおり1つの変異として扱う**)。期待と食い違ったら転記せずに止まって報告する。

| # | 変異 | 期待 |
| - | ---- | ---- |
| **M-1** | `SellableStock.ReserveQuantity` が常に 0 を返す(= 留保そのものを消す。#148 以前の状態) | **【核心】赤。訂正版の**テスト11 が**全5シードで赤**であること(これが留保の核心の担保である)。**テスト10 は M-1 の担保に数えない** — 理由は下の「M-1 がテスト10 を動かさない理由」。**どのシードが赤になったか**を両テストについて報告に含める |
| **M-2** | `TradeSystem` 段1 の販売在庫を `household.WorkshopInventory[outputItemId]` の直読みに戻す | **【核心】赤(テスト5)** |
| **M-3** | **【訂正版】** `TradeSystem` 段6 の販売在庫を `seller.WorkshopInventory[outputItemId]` の直読みに戻し、**あわせて `TradeSettlement.ExecuteExport` の番人の検査を外す**(下の「M-3・M-4 が複合変異である理由」) | **【核心】赤(テスト6)。** 赤になった**理由**(断定の失敗か `InvalidOperationException` か)を報告に含める。**断定の失敗であること**を期待する。番人も外しているのでテスト9 も赤になるが、これは副作用であって M-3 が見ている事象ではない |
| **M-4** | **【訂正版】** `TradeSystem` 段5b の切り詰めを `Math.Min(affordableQuantity, seller!.WorkshopInventory[line.ItemId])` に戻し、**あわせて `TradeSettlement.Execute` の番人の検査を外す** | **【核心】赤(テスト7)。** 同上 — **断定の失敗であること**を期待する。テスト9 も赤になる |
| **M-5** | `StoreChoice` の候補判定を `seller.WorkshopInventory[itemId] <= 0` に戻す | **【核心】赤(テスト8)** |
| **M-6** | `SellableStock.Of` の `max(0, …)` を外す | **【核心】赤(テスト4)** |

**M-2〜M-5 は「5経路を1か所に通した」ことの実測である。** どれか1つでも緑なら、その経路は留保を素通りしている。

#### M-1 がテスト10 を動かさない理由(2026-09-21。`mutator` の実測を受けたフェーズ2 の訂正)

**実測**(`mutator`。HEAD `0da66a4`): M-1 を当てると **テスト11 は全5シード(1/2/3/7/42)赤**、**テスト10 はシード7 のみ赤**で 1・2・3・42 は緑のままだった。初版の期待「両方が赤」は**5シード中4シードで成立しない。**

**これは検出器の壊れではなく、期待の側の見落としである。**

- **留保がある世界**では、工房在庫 1 → 販売在庫 0 → 段1 が売り注文を立てない。だから「0 件の日 = 鍛冶の在庫が 1 個まで痩せた兆候」が成り立つ(#148 の**訂正9** が下限を 1 件/日 に定めた根拠)
- **M-1 はその世界そのものを壊す。** 留保が無ければ**工房在庫 1 でも売り注文は立つ**ので、0 件になるのは在庫が 0 の日だけである。つまり **M-1 はテスト10 の下限を満たしやすくする方向に働く**
- したがって **M-1 とテスト10 は向きが逆で、テスト10 は M-1 の担保にならない。** シード7 だけが赤になったのは、その走行で鍛冶の在庫が 0 の日まで落ちたからである

**留保の核心はテスト11 が担保する。** 工具在庫 ≥ 1(= 設備係数‰ が 1000 を保つ)は #148 が実測した詰みの機構そのもので、M-1 で全5シードが赤になる。

**残る穴**: **テスト10 の断定は、どの変異でも担保されていない。** 将来この断定を空洞にしても(下限を 0 件/日 に緩めても)M-1〜M-6 は実測どおりのまま・通常走行も緑のままで、誰も気付けない。**テスト10 を担保する変異の設計は #154 のスコープ外**(新しい変異を1件足して `mutator` をもう一巡回すことになる)なので、**issue へ落とす**。テスト10 自体は「鍛冶の生産が完全に止まったこと」の検出器として意味を持ち続けるので、外さない。

#### M-3・M-4 が複合変異である理由(2026-09-21。レビュー1巡目の象限 I-b を受けたフェーズ2 の訂正)

**初版の M-3・M-4 は、経路の差し替えを測れていなかった。** 段6・段5b を直読みに戻すと、テスト6・テスト7 の断定に到達する前に**番人(経路5)が `InvalidOperationException` を投げる**。

- M-3 初版: 工房在庫 4・留保 1・閾在庫 0 → 直読みなら `超過分 = 4` → `ExecuteExport` の `4 − 4 = 0 < 1` で例外。「輸出数量 3・残る工房在庫 1」の断定は一度も評価されない
- M-4 初版: 工房在庫 2・留保 1 → 直読みなら `約定数量 = 2` → `Execute` の `2 − 2 = 0 < 1` で例外。「約定後の工房在庫 1」の断定は評価されない

したがって初版の M-3・M-4 は**赤にはなるが、赤の理由が「断定が捉えた」か「番人が落とした」か区別できない。** テスト6・7 の断定を将来空洞にしても赤のままなので、誰も気付けない。**これは初版のテスト11 と同じ型の欠陥である**(「変異の有無と無関係に赤」→「変異が見たい事象と別の事象で赤」)。

番人の検査を同時に外すことで、断定そのものが変異を捉えるかを測る。**番人が単独で機能していることはテスト9 が直接叩いているので、この複合化で測れなくなるものは無い。**

**M-2(段1)と M-5(`StoreChoice`)は番人を通らない**(売り注文を立てる/店の候補に入れる、のどちらも在庫を動かさない)ので、初版のまま単独で当てる。

### テスト11 の訂正(2026-09-21。フェーズ2 の `IMPL-BLOCKED` を受けたフェーズ1 の裁定)

**初版のテスト11**(「後半30日のうち生産回数が正の日が1日以上」= [#148](https://github.com/stama72/visionary/issues/148) の閉じる条件の1つ目を字面どおり写したもの)**は検出器として空洞だった。**

- フェーズ2 の実測: **留保は仕様どおり効いている**(鍛冶の工具在庫は60日間一度も 0 にならず 4〜7 で安定。master は day 1 に 0)。それでも**初版のテスト11 は5シードすべてで赤**だった。鍛冶は day 7〜8 に鉄鉱石・木炭を切らし、以後 生産0 が続く
- **原材料の枯渇は留保が作った経路ではない** — master でも day 8 に 0 になる
- したがって初版のテスト11 は**変異 M-1 を当てる前から赤**であり、留保の有無を区別できない。**M-1 の期待「両方赤」は成立し得なかった**

**訂正後のテスト11 は、#148 が実測した詰みの機構そのもの**(工具切れ → 設備係数 500‰ → 能力 0 → 復帰しない)**を見る。** 工具在庫 ≥ 1 は設備係数が 1000 になる条件([GDD02a §3](../03-gdd/02a-production.md))であり、master では day 1 に破れる。

**#148 の閉じる条件の1つ目(生産の復帰)は #154 では満たさない。** 資金と入力の枯渇による停止は留保と無関係で、直すには値付け・購入判定・資金の側に触れる —— #148 の決定1 が却下した「[GDD02d §4.4](../03-gdd/02d-external-market-and-money.md) の校正表を動かすこと」に当たる。**この実測は [#30](https://github.com/stama72/visionary/issues/30) の懸念2(「必需品は買えるが仕入には足りない世帯 — 資金が細った鍛冶の典型」)へ渡した。** #30 は W2 の実測を待って open にしてあったものである。

**フェーズ2 の診断のうち1点は誤りだったので、そのまま引き継がない**(フェーズ1 が検算): 「`world.Market` に鉄鉱石・木炭の売り注文が 0/53日 → 供給側の断絶」。**M0 の5レシピはどれも1次産品を出力しない**(出力は 小麦粉・パン・ビール・薪・工具)ので、**この件数は day 1 でも 0 である。** 1次産品は窓口からの輸入でしか手に入らない([GDD02d §2.2](../03-gdd/02d-external-market-and-money.md))。残る事実(帳簿の `Purchase` が day 8 以降0件・資金 8〜68)が指しているのは**買い手側**であり、原因の特定は #30 が持つ。

## 検出器の設計(テスト10)

**判定対象が `world.Market` である以上、走行後に1回走査する形は採れない。** `TradeSystem` 段2 が毎日 `world.Market.Clear()` を呼ぶので、売り注文はその日のうちにしか存在しない。**[#149](https://github.com/stama72/visionary/issues/149) の帯の検出器(`world.Ledgers` を走行後に1回走査)と構造が違う理由をこれとして doc コメントに書く** —— 書かずに日ごとのループを置くと、次の読者は「なぜ帯の検出器と揃えないのか」を読み取れない。

- **観測点**: `scheduler.Advance(world, ticks: 24)` を60回まわし、**各日の終わりに** `world.Market` のうち `ItemId == Item.Tools` のキーを数える(既存の `TradePipelineTests:343` と同じ形)
- **下限は 1 件/日。緩めない**(#148 **訂正9**)。**健全な走行では工具の売り注文は毎日立つ。0 件の日は鍛冶の工房在庫が 1 個まで痩せた兆候であり、それ自体が検出したい事象である。** 訂正9 は「0件の日を許容する」という初版の向きを**反転させた**ものなので、**閾値を緩める方向の変更は仕様の後退である**。この経緯を doc コメントに残す
- **最初の違反で止めない。** 60日を走り切り、**最小件数とその日**を記録して最後に1回 assert する(#149 の帯の検出器が「最初の違反で止まると超過の上限が測れない」で直したのと同じ規律)
- **失敗メッセージ**には シード / 日 / その日の件数 / **鍛冶ごとの工房在庫[工具]**(世帯 Id 昇順)を出す。在庫を出さないと「痩せて 0 件」と「生産が止まって 0 件」を読み分けられない
- **件数を数えるだけなので列挙順に依存しない。** ただし失敗メッセージに世帯を並べるときは **Id 昇順**で固定する(ADR-0002)

## 実測して doc コメントへ転記すること

**「実測した」と書いてよいのは、実際に走らせた結果だけである**(#148 の数値は紙)。

1. **健全な鍛冶の工房在庫[工具]の定常値**(紙では `留保1 + 閾在庫3 = 4`)。5シードで実測し、値と測定日を doc コメントへ。**紙と食い違ったら、食い違ったまま転記して報告する** —— 紙に合わせてテストを動かさない
2. **破産中の鍛冶が60日走行で現れたかどうか。** 現れなければ「現れなかった」と書く。**破産中の挙動はテスト6 が合成で担保しているので、現れないこと自体は欠陥ではない**

## 編集してよい文書

- `docs/tasks/W2-14-sellable-stock-reserve.md`(本書)と `docs/tasks/W2-14-sellable-stock-reserve.handoff.md`
- **GDD / TDD / ADR は触らない。** 触ると `SPEC-OUTSIDE` で止まる

## このタスクで特に効く規約

- **留保量・在庫・件数はすべて個数の int。** 比率も除算も現れない(**‰ も切り上げヘルパーも要らない**)。持ち込んだらそれは設計判断であり、仕様の外である
- **乱数を1つも引かない。** 検出器はシードを固定して走らせるだけで、`SellableStock` は乱数を持たない
- **`world.Market` の列挙は件数を数えるためだけに使う。** 「最初に見つけた売り手」をロジックに使わない(ADR-0002)

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **M-1〜M-6 を `mutator` が実測し(レビューの巡が閉じた後)、結果を doc コメントへ転記した**
- [ ] **上の「実測して doc コメントへ転記すること」2件を転記した**
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
