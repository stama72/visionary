# W2-17: 自家消費 — 自分の生産物を世帯在庫へ移す

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#174](https://github.com/stama72/visionary/issues/174)              |
| 根拠     | [GDD02b §1.1](../03-gdd/02b-consumption-and-household.md)(本タスクのために書いた節)/ [GDD02b §1・§2・§5.1・§5.2](../03-gdd/02b-consumption-and-household.md) / [GDD02c §1.3](../03-gdd/02c-price-and-budget.md) / [TDD01 §3.3](../04-tdd/01-sim-core-and-m0.md) |
| ブランチ | `feat/174-self-consumption-transfer`                                 |
| worktree | 本体ツリー(パイプライン)                                           |

**`WorkshopInventory` → `HouseholdInventory` の移動は `src/` のどこにも無い。** 世帯が消費できるのは買ったものだけで、**パン屋は工房在庫にパンを16個抱えたまま、世帯在庫[パン]が0の日を過ごす**([#120](https://github.com/stama72/visionary/issues/120) の実測)。本タスクはその1本の経路を足す。

## 前提 — この変更は W2-16 の検出器の向きを動かす

**[W2-16](W2-16-city-survival-detector.md) の30日検出器は「病理がまだある」向きで置かれている**(反転側)。自家消費が入ると条件が満たされるようになり、**反転側の `[Theory]` が赤くなる。これは朗報であって、直すべき故障ではない。**

**フェーズ2 は W2-16 が書いた手順(「3. 検出器 — 向きは実測が決める」)をそのまま実行する。** 手順は下の「作るもの 4」に、本タスクの基準値とともに書いてある。**赤を見て `RED` で止まらないこと。**

| 条件 | 現在の割り振り(2026-09-22 実測) |
| ---- | ---------------------------------- |
| 1 生産停止 | 反転側 `ProductionStopsForAWholeDayWithinThirtyDays` に 1/2/3/7/42。**正側メソッドは存在しない** |
| 2 都市内約定の消滅 | 反転側 `InternalSettlementsOfCityGoodsDisappearWithinThirtyDays` に 1/2/3/7/42。**正側メソッドは存在しない** |
| 3 全戸空 | 反転側 `AllHouseholdsRunEmptyWithinThirtyDays` に 7。正側 `SomeHouseholdAlwaysHoldsNecessitiesOverThirtyDays` に 1/2/3/42 |

## スコープ

**含まない:**

- **必需の取り置き([GDD02b §3.1](../03-gdd/02b-consumption-and-household.md))を変えること。** 自家供給できる品目のぶんも取り置きに加算し続ける。**正しくはないが、#174 の閉じる条件には不要であり、混ぜると「自家消費を入れて30日回す」の実測が2つの変更の合成になる**([#185](https://github.com/stama72/visionary/issues/185) が引き取る)
- **自家消費を帳簿に記帳すること。** 貨幣が動かないので [GDD02b §3](../03-gdd/02b-consumption-and-household.md)「帳簿は資金の増減と一致する」を破る(issue #174「やらないこと」)
- **2本の在庫の分離をやめること。** 足すのは移動だけである([GDD02b §1.1](../03-gdd/02b-consumption-and-household.md))
- **都市が死ぬ他の主因を直すこと。** 天井による貨幣流出([GDD02d §2.4](../03-gdd/02d-external-market-and-money.md))も取り置き([#30](https://github.com/stama72/visionary/issues/30) 懸念2)も本タスクでは触らない。**自家消費だけを入れて30日回し、#173 が赤のままなら主因はそちらだと分かる** — それが issue #174「なぜ実装を先に置くか」である
- **`docs/` の変更すべて。** GDD02b §1.1 / GDD02c §1.3 / TDD01 §3.3 はフェーズ1 が済ませた。**フェーズ2 が GDD / TDD / ADR を触ると `SPEC-OUTSIDE` で止まる**

### 変えない既存コード(規則8。[02-task-spec](../process/02-task-spec.md))

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `ConsumptionSystem` が**減らす**のは世帯在庫だけ(消費そのものは工房在庫に触れない)。在庫は0で下げ止まる | [GDD02b §1](../03-gdd/02b-consumption-and-household.md) | **一致。** §1 は「減らすのは世帯在庫であって工房在庫ではない」。**§1.1 が足すのは移動であって消費ではない** — 消費のループは従来どおり世帯在庫だけを見る |
| `ConsumptionSystem` が `UnmetConsumption[itemId]` を**毎日全品目上書きする**(不足0の日も書く) | [GDD02b §1・§8](../03-gdd/02b-consumption-and-household.md) | **一致。** 自家消費で足りた日は不足が0になるだけで、書く規律は変わらない |
| `DailyConsumption.Quantity` / `Lookahead` が消費量と目標在庫の唯一の置き場所(書き分けない) | [GDD02b §1・§2](../03-gdd/02b-consumption-and-household.md) | **一致。** 本タスクは**この2つを呼ぶ**。移動量のために消費量の式を書き直さない |
| `SellableStock.Of` / `ReserveQuantity` の留保量の定義(設備1個 / 入力1回分 / 0) | [GDD02c §1.3](../03-gdd/02c-price-and-budget.md) | **一致。触らない。** 本タスクは**呼ぶ側が1本増える**だけである(§1.3 に自家消費の箇条書きを足した) |
| `ProductionSystem` が順1 で工房在庫へ出力を加算し、その後に摩耗を引く | [GDD02a §1・§3.1](../03-gdd/02a-production.md) | **一致。触らない。** 自家消費は順2 なので、移す対象は**当日の生産を足した後の**工房在庫である |
| `BuyerDemand` の必需・嗜好の行が `TargetStock` に `DailyConsumption.Lookahead(…, 用途の日数)`、`ExpectedStock` に `household.HouseholdInventory[itemId]` を入れる | [GDD02b §2・§5.1](../03-gdd/02b-consumption-and-household.md) | **一致。触らない。** 本タスクの移動量の水準は、**この2つが順2 の後に読まれることを前提に決まっている**([GDD02b §1.1](../03-gdd/02b-consumption-and-household.md))。ここを変えると水準の根拠が消える |
| `SimScheduler` が順1 Production → 順2 Consumption → 順5 Trade の順で回す | [TDD01 §3.3](../04-tdd/01-sim-core-and-m0.md) | **一致。触らない。** 新しいシステムも新しい `RandomStream` も足さない |
| `WorldDefinition.M0` の校正値(初期世帯在庫 薪28・パン6・ビール1、目標日数 薪7・パン3・ビール1) | [GDD02d §4.4](../03-gdd/02d-external-market-and-money.md) / [GDD02b §2](../03-gdd/02b-consumption-and-household.md) | **一致。触らない。** 初期世帯在庫は**春の目標在庫ちょうど**なので、春の初日の移動量は「1日消費量ぶん」になる(下の「順序・境界」) |

## 作るもの

### 0. 置き場所

| 何を | どこへ |
| ---- | ------ |
| 移動量の式 | **新規** `src/Visionary.Sim/Systems/SelfConsumption.cs` |
| 移動の適用 | 既存 `src/Visionary.Sim/Systems/ConsumptionSystem.cs` |
| 式の単体テスト | **新規** `tests/Visionary.Sim.Tests/Systems/SelfConsumptionTests.cs` |
| 30日の検出器 | 既存 `tests/Visionary.Sim.Tests/Systems/TradePipelineTests.cs`(`ScanThirtyDays` を拡張。**新しい走行を足さない**) |

**式を `SellableStock` と同じ形の静的クラスに切り出すのは、パイプラインを走らせずに水準を突けるようにするためである。** `ConsumptionSystem` の私有メソッドに畳むと、移動量の境界(販売在庫での頭打ち・季節境界)を突くのに毎回1日ぶんの走行が要る。

### 1. `SelfConsumption` — 移動量の式

```csharp
namespace Visionary.Sim.Systems;

/// <summary>
/// 自家消費(GDD02b §1.1)。自分の生産物を工房在庫から世帯在庫へ移す量。
/// </summary>
public static class SelfConsumption
{
    /// <summary>
    /// 目標日数 = 必需の日数 + 嗜好の日数(GDD02b §2)。単位: 日。
    /// </summary>
    public static int TargetStockDays(WorldDefinition definition, int itemId);

    /// <summary>
    /// 移動量 = min( 販売在庫 , max( 0 , 目標在庫 + 今日の消費量 − 世帯在庫 ) )。単位: 個。
    /// </summary>
    public static int TransferQuantity(
        WorldDefinition definition, World world, HouseholdState household, int itemId, Season season);
}
```

- **`TargetStockDays` が必需と嗜好を足すのは、[GDD02b §1.1](../03-gdd/02b-consumption-and-household.md)「用途で分岐しない」を分岐なしに書くためである。** 用途は排他なので([GDD02b §2](../03-gdd/02b-consumption-and-household.md) の表)、**和は常に「該当する側の日数」と一致し、両項が正になる入力は存在しない。** `WorldDefinition` のコンストラクタが「品目を必需と嗜好の両方には置けない」を `ArgumentException` で弾くので、**合成の `WorldDefinition` でも作れない**(フェーズ2 実測、2026-09-22)。**したがって両項が正の場合を突くテストは書けない。** 書かないのが正しい — 排他が緩んだ日にだけ意味を持つ枝であり、いま守れる振る舞いを持たない

  > **【フェーズ2 による仕様の訂正・2026-09-22】** 凍結時の本箇条書きは「`BuyerDemand` は同じ品目が両方に該当すれば行を2本作り、どちらも同じ世帯在庫を予想在庫として読む」「この加算が発火するのは合成の `WorldDefinition` を使うテストだけである」と書いていた。**どちらも偽である。** 2行が立つ状態は `WorldDefinition` が名指しで禁じている病理であり(guard のコメント: 「先に走査したほうが買った量を後の行が見ないので、**目標在庫の2倍まで買う**」)、合成の定義でも構成できない。先例として引いた [`SellableStock.ReserveQuantity`](../../src/Visionary.Sim/Systems/SellableStock.cs) の入力の枝は**到達可能**(下のテスト #4 の合成レシピが実際に突く)なので、先例として成立していない。**GDD02b §1.1 の式そのものは健全であり、訂正はタスク仕様の根拠文に閉じる**(GDD は触らない)
- **`TransferQuantity` の中身は次の4つを呼ぶだけである。新しい式を書かない:**

  | 項 | 呼ぶもの |
  | -- | -------- |
  | 目標在庫 | `DailyConsumption.Lookahead(definition, world, household, itemId, world.Now, TargetStockDays(definition, itemId))` |
  | 今日の消費量 | `DailyConsumption.Quantity(definition, world, household, itemId, season)` |
  | 世帯在庫 | `household.HouseholdInventory[itemId]` |
  | 販売在庫 | `SellableStock.Of(definition, household, itemId)` |

- **今日の消費量に `Lookahead(…, days: 1)` ではなく `Quantity(…, season)` を使う。** 呼び出し側(`ConsumptionSystem`)が1日1回だけ解いた `season` を渡すことで、**移す量と同じ日に実際に引かれる量が同一の値であることを引数で保証する。** `Lookahead` は `world.Now` から季節を解き直すので、値は同じでも**保証が消える**
- **目標日数が 0 の品目を早期 return で弾かない。** 弾かなくても式が 0 を返す(目標在庫 0 + 今日の消費量 0 − 世帯在庫 ≥ 0 の `max(0, …)`)。**ゲートを置くと、置いたことを守るテストが書けない**(ゲートを外しても全シードで緑のまま)。**小麦粉と工具が移らないのは式の帰結である**ことを doc コメントに書く
- **`SellableStock.Of` を通す。** `household.WorkshopInventory[itemId]` を直読みしない([GDD02c §1.3](../03-gdd/02c-price-and-budget.md)「自家消費も販売在庫から取る」)。**M0 の対象3品目は留保量が 0 なので値は変わらない** — それでも通すのは、留保が値を持つ品目が現れた日に黙って外れるのを防ぐためである。**この保証には穴がある**: 呼び出し側が工房在庫を直読みする経路は型では防げない(`SellableStock` の doc コメントが既に書いている穴と同じもの)

### 2. `ConsumptionSystem` への接続

`RunOneHousehold` の**先頭**で移動を済ませてから、既存の消費ループへ入る。

```csharp
private void RunOneHousehold(World world, HouseholdState household, Season season)
{
    TakeOwnOutputHome(world, household, season);   // GDD02b §1.1。消費の前

    for (int itemId = 0; itemId < _definition.ItemCount; itemId++)
    {
        // 既存のまま
    }
}

/// <summary>自家消費(GDD02b §1.1)。移すのは自分のレシピの出力品目だけである。</summary>
private void TakeOwnOutputHome(World world, HouseholdState household, Season season)
{
    var recipe = _definition.Recipes[(int)household.Occupation];

    foreach (var output in recipe.Outputs)
    {
        int quantity = SelfConsumption.TransferQuantity(
            _definition, world, household, output.ItemId, season);

        household.WorkshopInventory[output.ItemId] -= quantity;
        household.HouseholdInventory[output.ItemId] += quantity;
    }
}
```

- **走査するのは `recipe.Outputs` であって全品目ではない。** 全品目を走ると、**パン屋が生産の入力として抱えている工房在庫の薪を食べてしまう** — 薪は必需の消費財でもあり([GDD02 §2.2](../03-gdd/02-economy.md))、`SellableStock` の留保(入力1回分 = 1個)は1個しか守らない。**自家消費は「自分が作ったものを食べる」であって「工房にあるものを食べる」ではない**([GDD02b §1.1](../03-gdd/02b-consumption-and-household.md)「対象品目 = 自分のレシピの出力品目」)
- **`recipe.Outputs` は配列であり、列挙順は定義順で確定している**([ADR-0002](../adr/0002-time-model-and-determinism.md))。並べ替えない。**同じ品目が2回現れる定義なら2回目は更新後の在庫を見る** — M0 に該当は無く、順序が結果を変えるだけで非決定にはならない
- **世帯の走査順(Id昇順)は既存のループがそのまま持つ。** 自家消費は世帯内で閉じている(共有資源に触れない)ので、順序が結果を変えない
- **クラスの doc コメントを直す。** 現在の「**触るのは世帯在庫だけである。**工房在庫の薪(itemId 5、生産入力でもある)には触れない(TDD01 §3.2)」は**偽になる**。新しい文言は「**減らすのは世帯在庫だけである**(消費)。**工房在庫を減らすのは自家消費の移動だけで、対象は自分のレシピの出力品目に限る**(GDD02b §1.1)。**生産の入力として抱えている工房在庫には触れない**」

### 3. 順序・境界(具体例で固定する)

M0・パン屋(親方+徒弟)・**春**。1日消費量[パン] = 1 + 1 = **2**、目標日数 = 必需3 + 嗜好0 = **3**、目標在庫 = 2 + 2 + 2 = **6**。

| 時点 | 世帯在庫[パン] | 工房在庫[パン] |
| ---- | -------------- | -------------- |
| 初期(エポック) | 6 | 0 |
| 順1 生産の後(6実行 × 出力2) | 6 | 12 |
| **順2 自家消費の後**(移動量 = min(12, max(0, 6 + 2 − 6)) = **2**) | **8** | **10** |
| 順2 消費の後(2個) | **6 = 目標在庫** | 10 |
| 段4 需要 | 予想在庫 6 = 目標在庫 6 → **在庫圧力 1000‰ → 購入量 0** | — |

**季節の境をまたぐ日**(秋の最終日、木材加工)。1日消費量[薪] は秋 `ApplyPermille(2,1000) × 2人 = 4`、冬 `ApplyPermille(2,2000) × 2人 = 8`。目標日数7 の先読みは当日(秋)1日ぶん + 冬6日ぶん:

```
目標在庫 = 4 + 8×6 = 52
移動量   = min( 販売在庫 , max(0, 52 + 4 − 世帯在庫[薪]) )
消費後の世帯在庫 = 52
```

**工房在庫が足りない日**(パン屋、入力切れで順1 が0実行、工房在庫[パン] = 1、世帯在庫[パン] = 0):

```
移動量 = min( 1 , max(0, 6 + 2 − 0) ) = 1        ← 販売在庫で頭打ち
消費   = min( 2 , 1 ) = 1 → 世帯在庫 0、UnmetConsumption[パン] = 1、工房在庫[パン] = 0
```

**この日も #174 の閉じる条件は満たされる** — 世帯在庫[パン]が0の日は、工房在庫[パン]も0になっている。**閉じる条件は実測ではなく式の帰結として成立する**(下の テスト #7 の doc コメントに書くこと)。

### 4. W2-16 の検出器 — 向きの更新手順

**フェーズ2 は、実装を緑にした後で次の手順を機械的に実行する。判断は要らない。**

1. `dotnet test` を走らせ、W2-16 の3条件の検出器の結果を見る
2. **反転側が落ちたシードは「直った」である。** そのシードを `[InlineData]` から正側のメソッドへ移す
3. **正側のメソッドが存在しなければ作る**(条件1・条件2 にはまだ無い)。名前は W2-16 が決めてある:
   - 条件1 正側 `ProductionNeverStopsForAWholeDayOverThirtyDays`
   - 条件2 正側 `InternalSettlementsOfCityGoodsNeverDisappearOverThirtyDays`
   - **assert の3段の形と空振り防止は、既存の正側 `SomeHouseholdAlwaysHoldsNecessitiesOverThirtyDays` をそのまま写す。** 核心だけ `Assert.True(days.Count == 0, …)` にし、失敗メッセージに違反日の一覧と条件ごとの内訳を入れる(W2-16「3. 検出器」)
4. **`[InlineData]` が空になった反転側のメソッドはメソッドごと削除する**(xUnit は `[InlineData]` の無い `[Theory]` をエラーにする)
5. **doc コメントの「基準値の実測」を、本タスクの実測日(実行日)と新しい割り振りで書き直す。** 「自家消費(#174)を入れた後の実測である」と明記する
6. **3条件すべてが正側だけになったら、issue #173 が閉じる。** その旨を引き継ぎメモの2番目(implementer の件数)ではなく、**フェーズ3 の PR 説明に書くこと**(#173 を閉じるのは開発者の判断)

**#173 が赤のまま(反転側が緑のまま)でもそれは想定内であり、停止条件ではない。** issue #174「なぜ実装を先に置くか」が「入れても #173 が赤のままなら、主因は取り置きか天井だと分かる」と書いている。**その場合は結果をそのまま引き継ぎメモへ書き、`PIPELINE: DONE` で進む。**

## 落ちるべき条件(テスト)

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心(当てる変異 / 期待) |
| - | ------ | -------- | -------------------- | ------------------------- |
| 1 | `TargetStockDaysAddsNecessityAndPreference` | **【フェーズ2 が縮小・2026-09-22】** M0 で パン=3、ビール=1、小麦粉=0、工具=0。**凍結時に書いていた「必需3日・嗜好2日の品目の目標日数が 5」は構成できないので落とす**(上の §1 の訂正。`WorldDefinition` が両方正を `ArgumentException` で弾く) | 片方しか読まない — **必需しか読まない実装はビール=1 で落ち、嗜好しか読まない実装はパン=3 で落ちる。** 縮小後も凍結時に名指しした故障は捕まる | — |
| 2 | `TransferFillsTheTargetStockMeasuredAfterTodaysConsumption` | **【核心】** M0・春・パン屋。`ConsumptionSystem` を1日走らせた後の世帯在庫[パン] が **目標在庫 6 ちょうど**、工房在庫が移動量ぶん減っている | 水準が「消費前に目標在庫」になっている(`+ 今日の消費量` が無い)。移動を消費の**後**に置いた | **M-1**: `TransferQuantity` から `+ 今日の消費量` の項を落とす / **赤**。**M-4**: `RunOneHousehold` で `TakeOwnOutputHome` の呼び出しを消費ループの後へ移す / **赤** |
| 3 | `TransferDoesNotAccumulateAcrossDays` | 同じ世帯を**5日**走らせ、各日の消費後の世帯在庫[パン]が毎日 6 で、単調増加しない | 世帯在庫を引く項が無く、毎日 `目標在庫 + 消費量` ぶん積み増す | **M-3**: `max(0, 目標在庫 + 今日の消費量 − 世帯在庫)` から `− 世帯在庫` を落とす / **赤** |
| 4 | `TransferNeverExceedsTheSellableStock` | **【核心】** 合成レシピ(出力品目が自分の入力でもある)で、留保量1回分が工房在庫に残る。あわせて M0 で工房在庫が目標に届かない日は**あるだけ**移る | 工房在庫を直読みして留保を素通りする。`min` を取り違えて要求量をそのまま移す | **M-2**: `SellableStock.Of(definition, household, itemId)` を `household.WorkshopInventory[itemId]` へ置換 / **赤** |
| 5 | `SelfSuppliedHouseholdDoesNotDemandItsOwnOutput` | **【核心】** M0・春・パン屋。1日走らせた後に `BuyerDemand.Build` を呼び、パンの必需の行が `ExpectedStock == TargetStock` かつ `StockPressurePermille == 1000` | 水準が違う(#2 と同じ取り違え)。**水準の決定([GDD02b §1.1](../03-gdd/02b-consumption-and-household.md))が守っているものはここにしか現れない** | **M-1**(再掲)/ **赤**。**M-5**: `TransferQuantity` の `世帯在庫` を `工房在庫` に取り違える / **赤** |
| 6 | `TransferMovesOnlyTheHouseholdsOwnOutputs` | M0・パン屋。1日走らせた後、**工房在庫[薪](生産の入力)が自家消費で減っていない**。醸造の工房在庫[穀物]・[薪] も同様 | 走査が `recipe.Outputs` ではなく全品目になっている | **M-6**: `foreach (var output in recipe.Outputs)` を全品目のループへ変える / **赤** |
| 7 | `OwnOutputIsNeverHoardedWhileTheHouseholdGoesWithout` | **【核心】** シード 1/2/3/7/42・30日。**どの世帯についても、自分の出力品目の世帯在庫が 0 の日に工房在庫が正である日が1日も無い**(#174 の閉じる条件)。空振り防止として、自家供給できる世帯が **6戸**(パン屋2・木材加工2・醸造2)であることと `FinalDayIndex == 30` を先に assert する | 移動そのものが無い。対象品目の絞り込みが出力品目から外れている | **M-7**: `TakeOwnOutputHome` の呼び出しを削除する / **赤**(全5シード) |
| 8 | `SelfConsumptionMovesNoMoneyAndWritesNoLedgerEntry` | M0・1日走行の前後で、全世帯の `LiquidFunds` の合計と `world.Ledgers` の行数が自家消費では変わらない(順2 の直前直後で比較する) | 移動を約定として記帳した([GDD02b §3](../03-gdd/02b-consumption-and-household.md) 違反) | — |
| 9 | `NonConsumedOutputsAreNeverMoved` | M0・製粉(小麦粉)と鍛冶(工具)。30日走っても世帯在庫[小麦粉] と 世帯在庫[工具] が **0 のまま**、鍛冶の工房在庫[工具]が自家消費で減らない | 目標日数0 の品目まで移す式になっている(`max(0, …)` の取り違え、負の扱い) | — |
| 10 | 既存 `StateHasherTests` / 決定論の回帰 | 同一シードの2回の走行でハッシュが一致する | 列挙順の破れ。**新しい状態は足さないのでハッシュの区分は増えない** | — |

**#7 の doc コメントに書くこと**: **この条件は実測ではなく式の帰結として成立する** — 移動量が `min(販売在庫, …)` なので、世帯在庫が0まで下がった日は販売在庫を使い切っている。**したがって #7 が落ちたときに疑うのは経済ではなく実装である**(上の「順序・境界」の3例目)。

**#5 の doc コメントに書くこと**: **本テストが守っているのは「水準の決定」であって「自家消費があること」ではない。** 自家消費が無くても水準の項が全部揃っていれば #2 は緑にできるが、#5 は「段4 の予想在庫が目標在庫に一致する」という**この水準を選んだ理由そのもの**([GDD02b §1.1](../03-gdd/02b-consumption-and-household.md))を見ている。

## 編集してよい文書

- **なし(コードとテストのみ)。** GDD02b §1.1・GDD02c §1.3・TDD01 §3.3 はフェーズ1 が済ませた。**フェーズ2 が `docs/` を触ると `SPEC-OUTSIDE` で止まる**
- 例外は `docs/tasks/W2-17-self-consumption-transfer.handoff.md`(引き継ぎメモ)だけである

## このタスクで特に効く規約

- **除算が1つも現れない。** 移動量は `min` / `max` / 加減算だけである。[GDD01 §2.3](../03-gdd/01-trust-and-conversation.md) の切り上げが要る場面が無いので、`IntegerMath` の丸めヘルパーを呼ぶ必要も無い。**呼びたくなったら式を取り違えている**
- **乱数を1つも引かない。** `ConsumptionSystem` は `RandomStream.Consumption` を名乗るが `SimContext.OpenRandom` を呼ばない([TDD01 §3.1](../04-tdd/01-sim-core-and-m0.md))。自家消費でも呼ばない
- **新しい状態を足さない。** 移動は既存の2本の配列の間でやりとりするだけなので、[TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md) のハッシュの区分は増えない。**増やしたくなったら仕様から外れている**
- **`recipe.Outputs` の列挙順に依存する処理を書かない**(同一品目が2回現れる定義が無いので、現状は依存していない)

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **W2-16 の検出器の向きを「作るもの 4」の手順で更新し、doc コメントの基準値を実測日とともに書き直した**
- [ ] **「核心」印の変異(M-1〜M-7)を `mutator` が実測し(レビューの巡が閉じた後)、結果を doc コメントへ転記した**
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
