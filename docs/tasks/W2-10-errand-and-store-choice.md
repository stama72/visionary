# W2-10: 外出と店の選択

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#98](https://github.com/stama72/visionary/issues/98)                |
| 根拠     | [GDD06 §1〜§4](../03-gdd/06-trade-and-negotiation.md) / [GDD02b §5・§7・§8](../03-gdd/02b-consumption-and-household.md) / [GDD02a §2](../03-gdd/02a-production.md) / [GDD08 §7.2・§7.3・§9](../03-gdd/08-household-and-decision.md) / [TDD01 §3.3](../04-tdd/01-sim-core-and-m0.md) |
| ブランチ | `feat/98-errand-and-store-choice`                                    |
| worktree | `.claude/worktrees/feat+98-travel-and-store-choice/`(**本体は #112 が使っている**。ディレクトリ名だけ旧スラッグのまま — Windows のロックで `git worktree move` が通らなかった) |

> **この文書は使い捨ての作業指示である。** 実装完了時点で凍結し、以後の正はコードと GDD/TDD。

## スコープ

**[#85](https://github.com/stama72/visionary/issues/85) で凍結した経済の分冊に実装を追随させる 5 本の 3 本目である**([#96](https://github.com/stama72/visionary/issues/96) が 1 本目、[#97](https://github.com/stama72/visionary/issues/97) が 2 本目)。本タスクは**順5 段5 を「外出の計画(段5a)+ 購入(段5b)」に割り**、移動を単価への割り戻しから**外出1回の固定費**へ置き換える。

**#97 が予算の側を統一形にしたので、余剰の材料(基礎値・在庫圧力‰・線形解・ゲート)はすべて揃っている。** 本タスクが足すのは、その材料を使った**比較**だけである。

**含まない:**

- **都市外市場の窓口**([#38](https://github.com/stama72/visionary/issues/38))。**本タスクの時点で 1 次産品は買えない** — 窓口は `Market` に実体を持たず、どの職業も 1 次産品を出力しないので、所在から引く店の候補が 0 件になる。輸出の外出([GDD02d §2.3](../03-gdd/02d-external-market-and-money.md))も #38
- **信用の配線**([#44](https://github.com/stama72/visionary/issues/44))。実効価格の式は本タスクが置くが、**渡す信用は定数 0 である**。[GDD06 §5](../03-gdd/06-trade-and-negotiation.md) の「店の信用」(店主と応対店員の平均)は W4
- **Need の生成**([#40](https://github.com/stama72/visionary/issues/40))。[GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md)「それでも買えないとき」の遠方在庫 Need は立てない
- **破産中フラグの更新と④の付け替え**([#39](https://github.com/stama72/visionary/issues/39))、**`vsim` / Runner の追随**([#41](https://github.com/stama72/visionary/issues/41))、**`DomainEvent` の発行**
- **余剰を本来の便益関数から導く形**([#94](https://github.com/stama72/visionary/issues/94))。疑似的な三角形のままでよい
- **コード内の旧 GDD 節番号の書き換え**([#112](https://github.com/stama72/visionary/issues/112))。**本タスクが新しく書く / 書き換える doc コメントは現行の分冊と節番号で引くこと。** 触らない行の旧引用はそのままでよい

### 変えない既存コード(規則8)

[02-task-spec](../process/02-task-spec.md)「変えないと宣言する既存コード」。単位はファイルではなく規則。**「一致」は現行版の節を読んでコードと突き合わせた結果である。**

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `BuyerBudget.Decide` のゲートと理由の判定順(相場 → 利潤上限 → 現金上限) | [GDD02b §5.2](../03-gdd/02b-consumption-and-household.md)「購入量 0 の理由」 | 一致 |
| `BuyerBudget.PurchaseQuantity` の線形解と上下の clamp | [GDD02b §5.2](../03-gdd/02b-consumption-and-household.md) | 一致 |
| `BuyerBudget.StockPressurePermille`(目標在庫 ≤ 0 で 0) | [GDD02b §5.1](../03-gdd/02b-consumption-and-household.md) | 一致(**本タスクで §5.1 に目標在庫 0 の行を足した**。式は動かしていない) |
| `BuyerDemand.Build` の走査順と母数の段階 | [GDD02b §3.1・§3.2](../03-gdd/02b-consumption-and-household.md) / [GDD02c §2.1](../03-gdd/02c-price-and-budget.md) | 一致 |
| `OfferPrice.Calculate`(値付け。床は外部買値) | [GDD02c §1](../03-gdd/02c-price-and-budget.md) | 一致 |
| `TradeSettlement.Execute` の仕入れ移動平均の更新条件(**用途が生産の入力か耐久**) | [GDD02a §5.1](../03-gdd/02a-production.md) | 一致(#97 が現行版へ合わせた。[#101](https://github.com/stama72/visionary/issues/101) の当該ケース) |
| `Observations.Expire` の失効境界(差 > 保持期間) | [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) | 一致 |
| `Observations.CollectAndShare` の観測範囲(居た区画から R 以内)と世帯内共有 | [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) / [GDD08 §8.1](../03-gdd/08-household-and-decision.md) | 一致。**ただし入力の「訪問区画」の作り手が本タスクで変わる**(約定の副産物 → 外出の計画) |
| `MarketReference.TryBuyer` / `TrySeller` の速さの違い | [GDD02c §1.2](../03-gdd/02c-price-and-budget.md) | 一致。**本タスクの見積もり価格はこれを使わない**(下記「見積もり価格は相場基準ではない」) |
| `District.VisionRadius = 1` と `District.Distance` | [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) / [GDD02 §4.3](../03-gdd/02-economy.md) | 一致。**本タスクで R の用途が「購入できる店」から外れ、知識だけになる** |
| `ProductionSystem` の `max(0, 労働力合計‰ − 外出の労働損失‰)` | [GDD02a §2](../03-gdd/02a-production.md) | 一致(#96)。**本タスクは書き手を足すだけで、読み手は動かさない** |

## 設計の前提(フェーズ1で決めたこと)

**実装の前に GDD/TDD を直してある(本ブランチの先行コミット)。実装はこの 8 点を仕様として読むこと。**

| どこ | 何を決めたか |
| ---- | ------------ |
| [GDD06 §3](../03-gdd/06-trade-and-negotiation.md) | **2回目以降の外出も「自区画 ∪ ここまでに行くと決めた区画」の最安店との差で数える。** これを採ると「行った区画で買った品目を需要リストから外す」という別の規則が要らない |
| [GDD06 §3](../03-gdd/06-trade-and-negotiation.md) | **余剰は 0 で下限を切る。** `w(s) ≤ p` なら 0。線形解の丸めで q が 1 残ると `FloorDiv` が負を返し、行かない理由が他の品目の余剰を食う |
| [GDD06 §3](../03-gdd/06-trade-and-negotiation.md) | **外出の計画は販売在庫を見ない。** 見ると計画が「その日に自分より先に買った世帯」に依存する |
| [GDD06 §3](../03-gdd/06-trade-and-negotiation.md) | **労働損失は外出ごとに切り上げてから合計する**(`Σ_d CeilDiv(…)`。`CeilDiv(…, Σ_d …)` ではない) |
| [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) | **距離 R 以内の店に売り注文が出ていないことも今日の知覚である。** 見積もりの候補から外し、記憶にも床にも落とさない |
| [GDD02b §5.1](../03-gdd/02b-consumption-and-household.md) | **目標在庫 0 の (用途, 品目) は需要リストに入れない。** 在庫圧力も余剰も恒に 0 |
| [GDD08 §7.1・§7.3](../03-gdd/08-household-and-decision.md) | 耐久の予算を統一形へ(旧 `min(相場基準, 流動資金 × 比率‰)` を削除)。§7.3 の「実質コスト」を外出の費用と労働損失へ |
| [TDD01 §3.2・§3.3](../04-tdd/01-sim-core-and-m0.md) | 段5 を 5a / 5b に割る。旧語(実質コスト・仕入れ間隔・`argmin(実質コスト)`)を現行の規則へ |

### 見積もり価格は相場基準ではない

**外出の計画が使う「見積もり価格」と、予算が使う「相場基準」は別物である。混ぜてはならない。**

| | 相場基準([GDD02c §1.2](../03-gdd/02c-price-and-budget.md)) | 見積もり価格([GDD06 §3](../03-gdd/06-trade-and-negotiation.md)) |
| -- | -- | -- |
| 何の値か | **品目**の相場(全売り手の観測の平均) | **その店**で今日いくら払うことになりそうか |
| 添字 | 品目 | 品目 × 売り手 |
| 誰が読むか | `BuyerDemand`(予算・基礎値) | `ErrandPlanner`(余剰) |
| 当日の値 | **使わない**(前日まで) | **自区画から R 以内なら使う**(店先は目の前にある) |

**`MarketReference` を流用してはならない。** あちらは品目について畳んだ平均であり、店ごとの差(外出の動機そのもの)が消える。

### 実効価格を 1 段で置く

```
実効価格 = ApplyPermille( 提示価格 , 1000 − CeilDiv( α‰ × 信用 , 100 ) )        α‰ = 200(GDD01 §2.2 効果1)、信用 = 0〜100
```

- **内側を `CeilDiv` にするのは `BuyerBudget.StockPressurePermille`(`1500 − CeilDiv(在庫比‰, 2)`)と同じ形である。** 係数全体としては切り下げ方向に寄る
- **W2 では信用が常に 0 なので、実効価格は提示価格に等しい。** それでも式を関数として置くのは、[#44](https://github.com/stama72/visionary/issues/44) が信用を配線するときに**呼び出し側を探し回らずに済ませる**ためである
- **信用を ‰ と取り違えても例外は出ない。** 0〜100 の外を渡したら投げること(値域は型で守れない。`HouseholdState.IsBankrupt` と同じ扱い)

## 作るもの

### 1. `Errand`(新規 `Systems/Errand.cs`)

**外出の純関数。`World` も `WorldDefinition` も受け取らない**(`BuyerBudget` / `OfferPrice` と同じ切り出し方)。

```csharp
public static class Errand
{
    /// 往復移動時間。単位: 時間。距離 × 1区画あたりの移動時間 × 2(GDD02 §4.3 / GDD06 §2)
    public static int TravelHours(int fromDistrictId, int toDistrictId, int hoursPerDistrict);

    /// 外出の費用 = 往復移動時間 × 委託先の機会費用。単位: 貨幣。**外出1回につき1度**(GDD06 §2)
    public static int Cost(int travelHours, int errandOpportunityCostPerHour);

    /// 外出の労働損失‰ = CeilDiv( 委託先の労働力係数‰ × 往復移動時間 , T )(GDD02a §2 / GDD06 §2)
    public static int LaborLossPermille(int delegateLaborPermille, int travelHours, int disposableHours);

    /// 余剰 = w ≤ p ? 0 : FloorDiv( q × (w − p) , 2 )(GDD06 §3)
    public static long Surplus(int quantity, int willingness, int price);
}
```

- **`TravelHours` は `StoreChoice` から移す**(既存のテスト `TravelHoursCountsTheRoundTrip` も移す)
- **`Cost` に割る対象は無い。** 引数が 2 つしか無いことが、旧の「目標在庫で割り戻す」が戻らない歯止めである
- **`LaborLossPermille` の分母は `T`(可処分時間、`WorldDefinition.DisposableHours` = 12)であって 24 ではない**
- **`Surplus` は `long` を返す。中間の積も `long`。** `quantity × (willingness − price)` は int を容易に超える。**初版は `int` を返すと書きながら、同じ節で「中間の積は `long`」、5.6 で「利得と価値は `long` で積む」と書いていた** — 積む先だけが広く、1 項が狭い形で内部矛盾していた(別表 B)。**`checked` で int へ戻さないこと** — `checked` の `OverflowException` は「経済が発散している」という別の事実の報せとしては使えない(別表 B)
- **`Surplus` は負を返さない。** `willingness ≤ price` を先に見て 0 を返す。`FloorDiv` に負を通さない(負の `FloorDiv` は −∞ 方向へ丸まるので、丸め誤差がそのまま「損をする外出」として他の品目の余剰から差し引かれる)

### 2. `ErrandDelegate` / `OpportunityCost.SelectErrandDelegate`(`Systems/OpportunityCost.cs`)

**`ForErrand` を置き換える。** 労働損失が**同じ委託先の労働力係数‰**を要るので、機会費用だけでは足りない。

```csharp
/// 買いに行く者(GDD08 §7.2・§9)。機会費用が最小の構成員。同値は NpcId 昇順
public readonly record struct ErrandDelegate
{
    public int NpcId { get; init; }
    /// 機会費用。単位: 貨幣/1時間(GDD08 §9)
    public int CostPerHour { get; init; }
    /// 労働力係数‰(GDD02a §2)。**階層係数‰(GDD08 §9)ではない**
    public int LaborPermille { get; init; }
}

public static ErrandDelegate SelectErrandDelegate(
    WorldDefinition definition, World world, HouseholdState household);
```

- **走査は `MemberNpcIds` の並びそのまま、更新は `<`。** 昇順は構築時に保証されているので、同値なら NpcId 最小が残る(既存 `ForErrand` と同じ)
- **2 つの係数を取り違えても例外は出ない。** M0 の徒弟は階層係数 200‰ / 労働力係数 300‰ で、どちらも「‰ の整数」である。**取り違えると労働損失が 2/3 になり、翌日の生産が過大になる** — 値としては自然に見えるので気付けない
- **`ForErrand` は消す。** 既存の `OpportunityCostTests` は `SelectErrandDelegate(...).CostPerHour` へ読み替える(変異の実測メモも残す)

### 3. `EffectivePrice`(新規 `Systems/EffectivePrice.cs`)

```csharp
public static class EffectivePrice
{
    /// 実効価格(GDD06 §2 / GDD01 §2.2 効果1)。単位: 貨幣/1単位
    /// <exception cref="ArgumentOutOfRangeException">trust が 0〜100 の外、または offerPrice が 0 以下</exception>
    public static int Calculate(int offerPrice, int trust, int trustDiscountPermille);
}
```

- **戻り値は必ず 1 以上である。** `ApplyPermille` が `CeilDiv` なので、正の提示価格に 0 でない係数を掛けて 0 にはならない。**これが `BuyerBudget.Decide` / `TradeSettlement.FundsCap`(どちらも 0 以下で投げる)の前提を満たしている**
- **係数が負になる経路を塞ぐ。** `信用 ÷ 100` を落とすと α‰ 200 × 信用 100 = 20000 で係数が −19000 になる。`trust` の値域検査がこれを止める

### 4. `WorldDefinition.TrustDiscountPermille`(`Definition/WorldDefinition.cs`)

```csharp
/// 信用による実効価格の割引係数 α‰(GDD01 §2.2 効果1)。単位: ‰。信用 100 で 200‰ = 2 割引
public int TrustDiscountPermille { get; }     // M0: 200
```

- コンストラクタの引数を 1 つ足し、`BuildM0` と `WorldDefinitionTests` の組み立てヘルパーに通す
- **値域は 0〜999 である。1000 を含めない。** 1000 ちょうどだと信用 100 で係数が **0** になり、`ApplyPermille(p, 0) = 0` で実効価格が 0 になる。**`BuyerBudget.Decide` と `TradeSettlement.FundsCap` はどちらも 0 以下で投げる**ので、下流が壊れる。**W2 は信用が常に 0 なので踏めない** — 踏むのは [#44](https://github.com/stama72/visionary/issues/44) が信用を配線したときであり、そのとき原因は `WorldDefinition` の値域にあって #44 の差分には見えない

### 5. `ErrandPlan` / `ErrandPlanner`(新規 `Systems/ErrandPlanner.cs`)

**`World` と `WorldDefinition` を読んで外出を計画する。書き込みは一切しない**(`BuyerDemand` と同じ)。

```csharp
/// 1世帯・1日ぶんの外出の計画(GDD06 §3)
public readonly record struct ErrandPlan
{
    /// 行くと決めた区画。**貪欲に選んだ順。自区画を含まない。重複なし**
    public IReadOnlyList<int> VisitedDistrictIds { get; init; }
    /// 当日の外出の労働損失‰ の合計(GDD02a §2)。外出しない日は 0
    public int LaborLossPermille { get; init; }
}

public sealed class ErrandPlanner
{
    public ErrandPlanner(WorldDefinition definition);

    public ErrandPlan Plan(
        World world, HouseholdState buyer, in HouseholdDemand demand, in ErrandDelegate errand);
}
```

#### 5.1 需要リスト

`demand.Lines` の**並びそのまま**、`Budget > 0` かつ `TargetStock > 0` の行だけを採る。

> **`TargetStock > 0` の条件は外から観測できない。** 目標在庫 0 の行は `PurchaseQuantity` の到達在庫が 0 に clamp されるので q が恒に 0 になり、余剰も恒に 0 で、入れても行き先は変わらない。**したがってテスト表に行を立てない**([02-task-spec](../process/02-task-spec.md) 規則1)。**代わりに doc コメントで [GDD02b §5.1](../03-gdd/02b-consumption-and-household.md) を引き、「余剰が恒に 0 なので落としてよい」と理由を残すこと** — 理由を書かないと、次に読んだ者が「予算だけで足りるのでは」と外し、同時に別の変更が入ったときに誰も気付かない。

#### 5.2 所在から引く店の候補(公開情報)

```
店(i) = { 世帯 s | s ≠ 買い手 かつ s の現在の職業のレシピが品目 i を出力する }
```

- **`world.Households` を Id 昇順に走査する**(添字 = Id なので先頭から回すだけでよい)
- **職業は世帯の現在の値を読む**([#39](https://github.com/stama72/visionary/issues/39) の付け替えで変わる)。`WorldDefinition` から表を前計算して持ち回してはならない
- **`Market` を見ない。** 所在は公開であり、今日売り注文が出ているかとは別である([GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md))
- **1 次産品はこれで 0 件になる。** `WorldDefinition.IsPrimaryItem` の定義そのものが「どのレシピも出力しない品目」なので、1 次産品の行は候補区画を 1 つも生まない。**これが `ExternalBuyPrice`(1 次産品で投げる)を呼ばない保証である。** ただし**「1 次産品を買い手が欲しがらない」ことは保証していない** — 水車小屋番の穀物の行は需要リストに残り、行き先が無いだけである(#38 が窓口を足すまで)

#### 5.3 見積もり価格(上から順に、最初に当たったものを採る)

```
1. 距離( 買い手の区画 , 売り手の区画 ) ≤ R:
      Market に (i, s) の売り注文があれば その提示価格
      無ければ **候補から外す**(今日この店は売っていないと見れば分かる。GDD06 §3.1)
2. 有効な記憶: 世帯主の観測のうち (i, s) で **最新**のもの(now.DayIndex − 観測日 ≥ 1)の価格
3. 床: WorldDefinition.ExternalBuyPrice(i)

得た提示価格を EffectivePrice.Calculate( …, trust: 0, definition.TrustDiscountPermille ) に通す
```

- **観測は世帯主(`HeadNpcId`)のものを読む**([GDD02c §1.2](../03-gdd/02c-price-and-budget.md) が相場基準について固定したのと同じ理由。買いに行く者が誰かで候補集合が変わってはならない)
- **「最新」は `ObservedAt.DayIndex` が最大のもの。** 走査は `List` の並びそのまま、更新は `>`(同日の重複は `CollectAndShare` が 1 日 1 件しか作らないので構造上起きないが、起きても並び順の先頭が残る)
- **保持期間は見ない。** 段3 の `Observations.Expire` が先に消しているので、残っているものはすべて期間内である(`StoreChoice` の旧 `HasValidMemory` と同じ理由)
- **`Tick` の差ではなく `DayIndex` の差で見る。** 差 ≥ 1 は「前日まで」であり、これが [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) の日内の相互参照を切っている

#### 5.4 区画の最安店

```
最安見積もり(i, 区画の集合 D) = min over { 店(i) のうち区画が D に属するもの } の 見積もり価格
                                候補が 0 件なら「無い」
```

同値は売り手 Id 昇順(走査が Id 昇順、更新が `<`)。**選ばれた店が誰かは使わない** — 使うのは価格だけである。

**集合の和は価格の `min` で取れる。**

```
最安見積もり(i, B ∪ {d}) = min( 最安見積もり(i, B) , 最安見積もり(i, {d}) )
                           片方が「無い」なら他方。両方「無い」なら「無い」
```

したがって 1 周ごとに `最安見積もり(i, B)` を品目ごとに 1 回求めておけば、区画 d の評価は `最安見積もり(i, {d})` だけで済む。**`B ∪ {d}` の集合を毎回作り直さないこと**(作ってもよいが、`min` で書くほうが下の「増分は 0 以上」が式から読める)。

#### 5.5 余剰

```
余剰(line, p) = p が「無い」なら 0
                q     = BuyerBudget.Decide(line, p).Quantity          ← **ゲートを通した値。用途の単位**
                q_個  = BuyerBudget.QuantityInUnits(line.Purpose, q, definition.ToolDurabilityPerUnit)
                w     = ApplyPermille(line.BaseValue, line.StockPressurePermille)
                Errand.Surplus(q_個, w, p)
```

- **`q` を個数へ直さずに `Surplus` へ渡してはならない。** `p` も `w` も**貨幣/個**である(`p` は店の提示価格、`w` の材料である相場項も現金上限も 1 個あたり)。一方 **`BuyerBudget.Decide` が返す `q` は「用途の単位」で、耐久だけ耐久値である**([GDD02b §5.2](../03-gdd/02b-consumption-and-household.md)「耐久は耐久値で解き、`CeilDiv(購入量, N × 1000)` で個数に直す」)。直さないと工具の行が **13,000 倍**に膨らむ
- **変換は購入の側とまったく同じ関数を通す。** [GDD06 §4](../03-gdd/06-trade-and-negotiation.md) が「見積もりに使った q と、着いてから解く購入量は、価格が見積もりどおりなら一致する」と約束しているので、**2 か所に同じ式を書いてはならない**

- **`BuyerBudget.Decide` を通すことが、[GDD06 §4](../03-gdd/06-trade-and-negotiation.md)「見積もりに使った q と、着いてから解く購入量は、価格が見積もりどおりなら一致する」の実体である。** `PurchaseQuantity` を直接呼ぶと、現金上限や利潤上限で買えない品目のために外出が立つ
- **`line.BaseValue` は需要リストの行では必ず 1 以上である**(`Budget > 0` は `CashCap > 0` を含意し、`BaseValue` は相場項か現金上限のいずれか)。したがって `PurchaseQuantity` のゼロ除算に到達しない。**この保証は「需要リストの絞り込みを先に通していること」に立っている** — 絞り込みの外で `余剰` を呼ばないこと

#### 5.6 貪欲の本体

```
行くと決めた区画 = []
往復時間の合計   = 0
労働損失‰        = 0

繰り返し:
    最良の区画 = 無し
    最良の価値 = 0                        ← 価値 ≤ 0 なら行かない(GDD06 §3)
    for 区画 d = 0 .. 8:                  ← 区画 Id 昇順。同値は最小が残る
        d が 自区画 または 行くと決めた区画 のいずれかなら飛ばす
        往復 = Errand.TravelHours(自区画, d, 1区画あたりの移動時間)
        往復時間の合計 + 往復 > T なら飛ばす                        ← T = DisposableHours
        利得 = Σ_line( 余剰(line, min( 既に居る区画の最安値(line) , 最安見積もり(line.ItemId, {d}) ))
                       − 余剰(line, 既に居る区画の最安値(line)) )    ← **両側とも「既に居る区画」を含む**
        価値 = 利得 − Errand.Cost(往復, 委託先の機会費用)
        価値 > 最良の価値 なら 最良を更新
    最良の区画が無ければ終わり
    行くと決めた区画 へ足す
    往復時間の合計 += 往復
    労働損失‰      += Errand.LaborLossPermille(委託先の労働力係数‰, 往復, T)
```

- **`既に居る区画の最安値(line)` は 1 周につき 1 回だけ求める。** 品目ごとに `{自区画} ∪ 行くと決めた区画` の最安見積もりを先に配列へ置き、区画のループの中では `min` を取るだけにする
- **`利得` は必ず 0 以上になる。** 比べる集合が広がるほど最安値は下がり、余剰は上がる。**負になったら式を取り違えている**(下の #29 がこれを直接押さえる)
- **`利得` と `価値` は `long` で積む。** 品目数 × 余剰の上限が int を超えうる
- **`最良の価値` の初期値が 0 で、更新が `>` である**ことが「価値 ≤ 0 なら行かない」と「同値は区画 Id 昇順」の両方を同時に満たしている。`>=` にすると後勝ちになり [ADR-0002](../adr/0002-time-model-and-determinism.md) の列挙順規約が破れる
- **終わる保証は 2 つある。** 各周で必ず 1 区画増えるので最大 8 周。加えて T が往復時間の合計を頭打ちにする。**T の検査を落としても無限には回らない** — だから T の検査には別に検出器が要る(テスト #15)
- **`world.Households[*].WorkshopInventory` を読まないこと。** [GDD06 §3](../03-gdd/06-trade-and-negotiation.md) の決定であり、破ると計画が世帯の走査順に依存する

### 6. `StoreCandidate` / `StoreChoice` の改訂(`Systems/StoreChoice.cs`)

```csharp
/// 選ばれうる店1件(GDD06 §3)
public readonly record struct StoreCandidate
{
    public int SellerId { get; init; }
    public int DistrictId { get; init; }
    /// 実効価格。単位: 貨幣/1単位。**移動は乗らない**(外出ごとの固定費。GDD06 §2)
    public int UnitEffectivePrice { get; init; }
}

public sealed class StoreChoice
{
    public StoreChoice(WorldDefinition definition);

    /// 自区画と行った区画の店のうち、実効価格が最小のものを選ぶ(GDD06 §3)。候補0件なら false
    public bool TrySelect(
        World world, HouseholdState buyer, int itemId,
        IReadOnlyList<int> visitedDistrictIds, out StoreCandidate selected);
}
```

**消すもの:**

- `StoreCandidate.UnitRealCost`(実質コスト)
- `StoreChoice.TravelCostPerUnit`(移動費の割り戻し)
- `StoreChoice.TravelHours`(`Errand` へ移す)
- `StoreChoice.HasValidMemory` と、`TrySelect` の中の「知っている店」の判定。**購入できるかは知識ではなく、その区画に居るかで決まる**([GDD06 §3](../03-gdd/06-trade-and-negotiation.md))
- `TrySelect` の `targetStockInUnits` / `errandOpportunityCost` 引数

**`TrySelect` の候補の条件**(`world.Market` を `MarketKey` 昇順に 1 回走査する。順は ItemId → SellerId):

1. `key.ItemId == itemId`
2. `key.SellerId != buyer.Id`(自分から自分へは売買しない)
3. 売り手の `WorkshopInventory[itemId] > 0`(先に来た買い手が買い切った店は候補外。[GDD06 §3](../03-gdd/06-trade-and-negotiation.md))
4. 売り手の区画が `buyer.DistrictId` または `visitedDistrictIds` に含まれる

`UnitEffectivePrice = EffectivePrice.Calculate(提示価格, trust: 0, definition.TrustDiscountPermille)`。更新は `<` なので、同値は売り手 Id 最小が残る。

> **`R` はここに現れない。** 距離 1 の店を「知っている」ことは、そこで買えることを意味しなくなった。**旧実装は R 以内の店から移動費を払わずに買えていた** — 空間の摩擦が購入の側から抜けていた経路である。

### 7. `TradeSystem` の改訂(`Systems/TradeSystem.cs`)

**段5 を 5a / 5b に割る**([TDD01 §3.3](../04-tdd/01-sim-core-and-m0.md))。

```
段5. 世帯 Id 昇順に:
    5a. errand = OpportunityCost.SelectErrandDelegate(definition, world, household)
        plan    = _errandPlanner.Plan(world, household, demands[household.Id], errand)
        household.ErrandLaborLossPermille = plan.LaborLossPermille     ← **0 の日も書く**
    5b. RunOneHouseholdsShopping(world, household, demands[household.Id], plan.VisitedDistrictIds)
    plan.VisitedDistrictIds を段6 のために控える
```

**`RunOneHouseholdsShopping` の変更:**

**`PurchaseQuantityInUnits` を `BuyerBudget` へ移す。**

```csharp
// Systems/BuyerBudget.cs
/// 用途の単位で解いた数量を個数へ直す(GDD02b §5.2・§2)。耐久だけ耐久値で解くので変換が要る
public static int QuantityInUnits(DemandPurpose purpose, int quantity, int durabilityPerTool);
```

- **`TradeSystem.PurchaseQuantityInUnits` を消し、段5b と `ErrandPlanner` の両方がこれを呼ぶ。** 置き場所を `TradeSystem` にしたままだと、計画(5a)が交易システムに依存する向きになるうえ、**同じ変換が 2 か所に書かれる**
- **これが 3 回目の差し戻しの根治である。** 変換が購入の側にしか無かったことが、単位の取り違えを許した(別表 C)

| 旧手順 | どうする |
| ------ | -------- |
| 1. `TargetStockInUnits` で目標在庫を個数へ | **消す**(移動費の分母が無くなった)。`TradeSystem.TargetStockInUnits` ごと削除 |
| 2. `_storeChoice.TrySelect(…, targetInUnits, errandOpportunityCost, …)` | `_storeChoice.TrySelect(world, household, line.ItemId, visitedDistrictIds, out store)` |
| 3・4. `BuyerBudget.Decide(line, store.UnitEffectivePrice)` | **そのまま**(#97 で既に実効価格を渡している) |
| 5・6. 個数へ直す / 0 以下なら終わり | **`BuyerBudget.QuantityInUnits` を呼ぶ形に変える**(式は同じ)。`TradeSystem.PurchaseQuantityInUnits` は消す |
| 7. 訪れた区画を控える | **消す。** 訪問は段5a が決めており、買えたかどうかで変わらない |
| 8〜10. 資金上限と在庫で切り詰め、約定 | そのまま |

- **`UnaffordableNecessityCount` の 2 経路は変えない**(#97)。**店が 0 件で `continue` した行は数えない** — [GDD02b §5.2](../03-gdd/02b-consumption-and-household.md)「店を1つも知らない・売り手の在庫が尽きた はこの理由に入れない」
- **`ErrandLaborLossPermille` は毎日上書きする。** `UnaffordableNecessityCount` を毎日 0 で上書きするのと同じ理由で、**書かない日があると前日の損失が翌日以降も効き続ける**
- `_errandPlanner` はコンストラクタで 1 つ持つ(`_buyerDemand` / `_storeChoice` と同じ)

### 呼び出し側の配線(規則7)

**書き手または読み手が本タスクの外にあるもの、および取り違えても例外が出ないもの。**

| 何 | 書く | 読む | 踏めるか | 約束 |
| -- | ---- | ---- | -------- | ---- |
| `ErrandLaborLossPermille` | 本タスク(段5a。**毎日上書き、0 の日も書く**) | `ProductionSystem`(順1、**翌日**) | **踏める**(#21) | 単位 ‰。0 以上。順1 は順5 より前なので、読む時点で前日の値である([GDD02a §2](../03-gdd/02a-production.md))。**上限は型でも規則でも守っていない** — `ProductionSystem` の `max(0, …)` が 0 で止める |
| `ErrandPlan.VisitedDistrictIds` | 段5a | 段5b(購入の候補)と段6(`Observations.CollectAndShare`) | **踏める**(#22・#27) | **自区画を含まない。重複なし。貪欲に選んだ順。** `CollectAndShare` は自区画を別に見ているので、含めると二重にはならないが順が濁る |
| `ErrandDelegate.LaborPermille` | `SelectErrandDelegate` | `Errand.LaborLossPermille` | 踏める(#19・#21) | **機会費用で選んだのと同じ NPC の労働力係数‰。** 階層係数‰(徒弟 200)と取り違えても例外は出ず、損失が 2/3 になるだけ |
| `trust: 0` | 段5a・段5b の呼び出し側(定数) | `EffectivePrice.Calculate` | **値が定数なので踏めない** | [GDD06 §5](../03-gdd/06-trade-and-negotiation.md) の店の信用は [#44](https://github.com/stama72/visionary/issues/44)。**W2 で 0 以外を渡さない。** 式そのものは #5 が踏む |
| `WorldDefinition.TrustDiscountPermille` | `BuildM0`(200) | `EffectivePrice` | 踏める(#5) | ‰。0〜1000 |
| 需要リストの絞り込み → `余剰` の前提 | 5.1 | 5.5(`BuyerBudget.PurchaseQuantity` のゼロ除算) | 踏める(#12) | **`BaseValue ≥ 1` は「絞り込みを先に通したこと」だけが保証している。** 絞り込みの外で `余剰` を呼ぶと投げる |

## 落ちるべき条件(テスト)

**この節が完了条件そのもの。** 目標は「通ること」ではなく「**壊したときに落ちること**」である。

`WorldDefinition` を直接組み立ててよい(`EconomySystemTestFixtures`)。**`BuildM0` の値に依存しないこと** — ただし #12 だけは [GDD02d §4.4](../03-gdd/02d-external-market-and-money.md) の校正値で書いた [GDD06 §3](../03-gdd/06-trade-and-negotiation.md) の例をなぞるので、必要な値を fixture に明示的に置いて再現する。

| #  | テスト | 検証内容 | この実装ミスで落ちる | 核心 |
| -- | ------ | -------- | -------------------- | ---- |
| 1  | `TravelHoursCountsTheRoundTrip` | (既存を `Errand` へ移設)距離 4 → **8**、同区画 → **0** | 片道で数える。距離表を持ち込んで 3×3 の式から外れる |  |
| 2  | `ErrandCostIsNotAmortized` | 往復 4 時間・機会費用 4 → **16**。往復 0 → **0**。引数は 2 つだけ(目標在庫を渡す余地が無い) | 旧の「目標在庫で割り戻す」が戻る(引数が増えるのでコンパイルでも気付くが、値も固定する) |  |
| 3  | `ErrandLaborLossCeilsPerTrip` | 労働力係数 300‰・往復 2 時間・T = 12 → **50**。往復 5 時間 → `CeilDiv(1500, 12)` = **125** | `FloorDiv` にする(124)。T ではなく 24 で割る。往復時間ではなく片道を渡す |  |
| 4  | `SurplusNeverGoesNegative` | q = 4・w = 76・p = 54 → **44**。w = p → **0**。**q = 1・w = 50・p = 51 → 0**(`FloorDiv(−1, 2)` = −1 にならない) | `max(0, …)` を落とす(負の余剰が他の品目の余剰を食い、行くべき外出が立たなくなる)。`CeilDiv` にする(45) | **核心** |
| 5  | `EffectivePriceDiscountsByTrust` | α‰ 200・信用 **0** → 提示価格そのまま(100 → 100)。信用 **100** → `ApplyPermille(100, 800)` = **80**。信用 **50** → `ApplyPermille(100, 900)` = **90**。提示価格 1・信用 100 → **1**(0 にならない) | `÷ 100` を落とす(係数 −19000 で実効価格が負)。`1000 − α‰ × 信用 ÷ 100` の除算を外側に掛ける。`ApplyPermille` を `FloorDiv` に置き換えて提示価格 1 が 0 になる | **核心** |
| 6  | `EffectivePriceRejectsTrustOutsideZeroToHundred` | 信用 **101** と **−1** で投げる。提示価格 **0** でも投げる。`WorldDefinition` が α‰ **1000** を拒む(上端は 999) | 値域を開けたまま(GDD01 §2.1 の 0〜100 は型で守れない)。#44 が ‰ を渡したときに黙って負の価格になる。α‰ の上端を 1000 にする(信用 100 で実効価格が 0 になり下流が投げる) |  |
| 7  | `EstimateUsesTodaysOfferWithinVisionRadius` | R 以内の店に当日の売り注文(価格 **30**)。同じ店の**前日の観測を 90**、床を **10** に置いても見積もりは **30** | 記憶を先に見る。床を先に見る。当日の `Market` を読まず「価格を知らない店」として床で見積もる |  |
| 8  | `EstimateDropsNearbyStoreWithoutTodaysOffer` | R 以内の店が当日の売り注文を出していない → **前日の観測があっても、床が安くても、その店は候補に入らない**。その品目に他の候補が無ければ**どこへも行かない** | 床へ落とす(GDD06 §3.1)。**床の見積もりは余剰が最大になるので、見れば空と分かる隣区画へ毎日出かける世帯ができる** | **核心** |
| 9  | `EstimateUsesTheLatestValidMemoryOutsideVisionRadius` | R の外の店について、3 日前(**90**)と 1 日前(**50**)の観測 → **50**。**当日の観測(差 0)は使わない**(当日だけを置くと床へ落ちる) | 平均する(`MarketReference` を流用して店ごとの差を潰す)。最古を採る。差 0 を使う(日内の相互参照が戻る) | **核心** |
| 10 | `EstimateFallsBackToTheFloor` | R の外・記憶なし → `ExternalBuyPrice`。**その店の当日の提示価格が床より高くても床で見積もる**(距離の外は見えない) | 距離を見ずに `Market` を覗く(視界が無限になる)。0 を返して `BuyerBudget.Decide` が投げる |  |
| 11 | `PrimaryItemLinesCreateNoErrand` | 1 次産品だけを生産の入力に持つ世帯(予算あり・在庫 0)は、**どこへも行かず、例外も出ない** | 所在の候補を `IsPrimaryItem` で作らず「全世帯」から作る → `ExternalBuyPrice` が `ArgumentException`(#38 の前借り) |  |
| 12 | `ErrandIsTakenOnlyWhenTheGainExceedsTheCost` | [GDD06 §3](../03-gdd/06-trade-and-negotiation.md) の例。徒弟の機会費用 4・距離 2(往復 4 時間)→ 費用 **16**。パン(相場 54・基礎値 65・目標 6):在庫 **6** → 余剰 11 で**行かない**、在庫 **4** → 余剰 **44** で**行く** | 費用を引かない(在庫 6 でも行く)。費用に片道(8)を使う(在庫 6 でも行く)。`>` を `>=` にして価値 0 で行く | **核心** |
| 13 | `ErrandValueIsMeasuredAgainstTheHomeDistrict` | 自区画と遠方に**同じ品目を同じ提示価格**で売る店 → 差が 0 なので**行かない**。自区画の店の価格だけを上げると**行く** | 差を取らず余剰の絶対値で数える(自区画で足りる品目のために外出する) | **核心** |
| 14 | `SecondErrandIsMeasuredAgainstTheDistrictsAlreadyChosen` | 2 つの遠方区画が**同じ安値**で同じ品目を売る → **1 区画だけ行く**(2 つ目は差 0 − 費用 < 0)。2 つ目だけ**さらに安く**すると、**その差が費用を超えるときだけ** 2 つ目にも行く | 差の相手を自区画に固定する(同じ余剰を 2 度取って 2 回行く)。1 周目で買う品目を需要リストから外す(3 つ目がもっと安くても差を数えられない) | **核心** |
| 15 | `ErrandStopsAtTheDisposableHoursCap` | T = 12。距離 4(往復 8)と距離 3(往復 6)がともに価値 > 0 → **距離 4 だけ**(合計 8。6 を足すと 14 で超える)。距離 4 と距離 2(往復 4)なら**両方**(合計 12、等号は入る) | 上限を見ない(14 時間歩く)。片道で数える。`<` にして合計ちょうど T を弾く | **核心** |
| 16 | `ErrandPicksTheLowestDistrictIdOnTies` | 価値が等しい 2 区画 → **区画 Id が小さい方**。買い手の区画からの距離も等しくして、費用で差が付かないようにする | 更新を `>=` にして後勝ちになる([ADR-0002](../adr/0002-time-model-and-determinism.md) の列挙順規約)。区画を Id 昇順に走査しない |  |
| 17 | `ErrandPlanIgnoresTheSellersStock` | 行き先の売り手の `WorkshopInventory` を **0** にしても、`VisitedDistrictIds` と `LaborLossPermille` が**変わらない**(買えないだけ) | 計画で在庫を見る。**先に買った世帯が売り切ると、後の世帯の行き先が変わる** — 区画間の価格差(#29)が機構の帰結か走査順の産物かを切り分けられなくなる | **核心** |
| 18 | `ErrandLaborLossIsWrittenEveryDayIncludingZero` | 外出しない日に、**前日の値(100)が 0 で上書きされる** | 0 のとき書かない(前日の損失が翌日以降も効き続け、生産が永久に減ったままになる) | **核心** |
| 19 | `ErrandLaborLossUsesTheDelegatesLaborCoefficient` | 親方(階層係数 1000‰ / 労働力係数 1000‰)と徒弟(**200‰ / 300‰**)の世帯 → 委託先は徒弟で、損失は **300‰ 基準**。階層係数で数えると 2/3 になる | `RankCoefficientPermille` を `LaborPermilleByRank` の代わりに渡す(どちらも ‰ の整数なので例外は出ない) | **核心** |
| 20 | `ErrandLaborLossCeilsEachTripSeparately` | T = **7**・労働力係数 300‰ で、往復 2 時間と往復 6 時間の 2 回 → `CeilDiv(600,7) + CeilDiv(1800,7)` = 86 + 258 = **344**。往復時間を先に足すと `CeilDiv(2400,7)` = **343** | 往復時間を合計してから 1 回だけ切り上げる(GDD02a §2 が外出 1 回ごとの式として定義している) |  |
| 21 | `NextDaysProductionDropsByTheErrandLaborLoss` | パイプラインを 2 日走らせ、1 日目に外出した世帯の **2 日目の `ProductionRuns`** が、同条件で外出しなかった世帯より**少ない**。**1 日目の `ProductionRuns` は同じ**(損失は翌日に効く) | `ErrandLaborLossPermille` を書かない(`ProductionSystem` は読むが常に 0)。当日の生産に効かせる(順1 より前に書く)。**これが issue #98 の閉じる条件である** | **核心** |
| 22 | `PurchaseIsLimitedToTheHomeAndVisitedDistricts` | 距離 1(R 以内)で**当日の提示価格が見えている**店でも、`visitedDistrictIds` に無ければ**約定しない**。同じ店の区画を渡すと約定する | 旧の「知っている店」(R・記憶)で候補を作る(**移動を払わずに隣区画で買えてしまい、空間の摩擦が購入の側から抜ける**) | **核心** |
| 23 | `PurchasePicksTheCheapestEffectivePriceThenTheLowestSellerId` | 訪問区画の 2 店が提示価格 **30** と **20** → 20 の店。**同値なら売り手 Id 最小** | 更新を `<=` にして後勝ち。距離で並べ替える(移動が単価に戻る) |  |
| 24 | `PurchaseSkipsEmptyStoresAndOwnOffer` | (既存を維持)販売在庫 0 の店は候補外。自分の売り注文も候補外 | 在庫を見ない(負の在庫が出る)。自分から自分へ売買する(資金が動かないまま帳簿だけ増える) |  |
| 25 | `BudgetGateUsesTheEffectivePriceOnly` | **旧テスト #30 の置き換え。** 距離 2 の訪問区画の店で約定し、**数量が距離 0 の店で同じ提示価格のときと一致する** | 外出の費用を単価へ足して `BuyerBudget.Decide` に渡す(#85 で消した二重計上が戻る)。数量の解に外出の費用を混ぜる | **核心** |
| 26 | `ObservationsCoverEveryVisitedDistrictEvenWithoutAPurchase` | 行ったが**1 つも買えなかった**区画(全店の販売在庫 0)でも、翌日その区画の店が「有効な記憶」になる | 訪問記録を約定時だけ積む(旧実装)。**観測が買い物の副産物になると、[GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md)「観測は『見た』時点で生まれる」が壊れ、情報の摩擦が実際より厚くなる** | **核心** |
| 27 | `ErrandPlanIsDeterministicAcrossHouseholdOrder` | 世帯 Id 昇順に段5 を回した結果と、**段5a を全世帯ぶん先に回してから段5b を回した**結果で、`VisitedDistrictIds` と `LaborLossPermille` が全世帯一致する | 計画が `Market` 以外の可変状態(在庫・資金)を読む。`Dictionary` の列挙順に依存する |  |
| 28 | `TradePipelineStillRunsDeterministically` | (既存を維持)同じシードで 2 回走らせると状態ハッシュが一致する。`TradeSystem` が乱数を引かない | `SortedDictionary` を `Dictionary` に戻す。帳簿の走査で列挙順に依存する |  |
| 29 | `ErrandGainIsNeverNegative` | **自区画にすべての品目の最安店がある**世帯を置き、どの遠方区画についても**利得が 0**(価値 = −費用)であること。遠方の 1 品目だけを安くすると、**利得がその品目の増分ぶんだけ正**になる。**負にはならない** | **候補側の集合から「既に居る区画」を落とす**(`Σ 余剰({d}) − Σ 余剰(B)`)。この変異は 1 区画目の結果を変えないので、#12・#13 では落ちない。**落ちるのはここと #14・#15 後半・#20 だけである** | **核心** |

| 30 | `DurableSurplusIsMeasuredInUnitsNotDurability` | **耐久(工具)の行だけを需要に持つ世帯**で、遠方の鍛冶へ行くかどうかが**個数で数えた余剰**で決まる。`N × 1000` = 13,000・目標 6,500・予想 0・基礎値 100・見積もり 50 のとき、線形解は耐久値で 13,000 を返すが **`q_個` = `CeilDiv(13000, 13000)` = 1**。余剰は `FloorDiv(1 × (w − 50), 2)` であって `FloorDiv(13000 × (w − 50), 2)` ではない。**外出の費用を両者の間の値(たとえば 300)に置くと、直っていれば行かず、取り違えていれば行く** | `q` を個数へ直さずに `Errand.Surplus` へ渡す。**この変異は他のどのテストでも落ちない** — `ErrandPlannerTests` は全件が `Necessity`(単位が個)の手組み行で、耐久の行を 1 本も通していなかった(3 回目のレビュー I-1) | **核心** |
| 31 | `ErrandPlannerAndSettlementAgreeOnQuantity` | 見積もり価格と当日の提示価格が一致する配置で、**計画が使った `q_個` と、段5b が `BuyerBudget.QuantityInUnits` で得る購入量が一致する**。必需と耐久の両方で確かめる | 変換を 2 か所に書いて片方だけ直す([GDD06 §4](../03-gdd/06-trade-and-negotiation.md) の約束が破れる)。`CeilDiv` を `FloorDiv` にする(耐久値 13,000 未満の需要が 0 個になり、工具が永久に買われない) | **核心** |

**「核心」印は 15 件ある。** [process/02](../process/02-task-spec.md) の「1 タスクあたり 2〜3 件」を超えるので、**実際に変異を当てるのは次の 3 件に絞る**。

| 印を当てる | なぜこの 3 件か |
| ---------- | --------------- |
| **#14** | 貪欲の差の取り方。**間違えても外出は起きるし、回数も「それらしく」増える** — 結果を眺めて気付ける種類の誤りではない |
| **#17** | 計画が在庫を見ないこと。**破っても緑のままで、壊れるのは [#29](https://github.com/stama72/visionary/issues/29) の実測の解釈可能性だけである**(区画差が機構の帰結か走査順の産物かを切り分けられなくなる) |
| **#21** | 翌日の生産が減ること。**issue #98 の閉じる条件そのもの**であり、書き忘れても `ProductionSystem` は 0 を読んで普通に動く |
| **#29** | 候補側が「既に居る区画」を含むこと。**差し戻しの原因そのもの**(下記「別表 A」)。1 区画目の結果を変えないので、他のどのテストでも落ちない |

| **#30** | 耐久の余剰の単位。**3 回目の差し戻しの原因そのもの**(別表 C)。353 件が緑のまま通り抜けた経路であり、他のどのテストでも落ちない |

**変異は 5 件に増えた**(当初 3 件 + #29 + #30)。

**残りの「核心」印は「壊れたときの影響が大きい」ことだけを示す** — 変異は当てず、レビュアーが読む優先度として使う。

### #12 の数値の出どころ

**[GDD06 §3](../03-gdd/06-trade-and-negotiation.md) の例をそのまま再現する。** 目標在庫 6・基礎値 65・相場 54 で:

| 在庫 | 在庫比‰ | 在庫圧力‰ | w = `ApplyPermille(65, 圧力)` | 到達在庫 | q | 余剰 | 費用 16 と比べて |
| ---- | ------- | --------- | ----------------------------- | -------- | - | ---- | ---------------- |
| 6 | 1000 | 1000 | 65 | 8 | 2 | `FloorDiv(2 × 11, 2)` = **11** | 行かない |
| 4 | 667 | 1166 | 76 | 8 | 4 | `FloorDiv(4 × 22, 2)` = **44** | 行く |

> **GDD06 §3 は当初この 2 行を 11 / 43 と書いていた(小麦粉の例も 38)。** 実数で `65 × 1.166 = 75.79` のまま三角形を取って最後に切り捨てた値であり、**整数の式(`ApplyPermille` は `CeilDiv`)では 44(小麦粉は 39)になる。** 本ブランチの先行コミットで直してある。**結論(行く / 行かない)は変わらない。**

## 別表 A: 差し戻し後の訂正(フェーズ1、2026-09-20)

**1 回目のフェーズ2 が `SPEC-OUTSIDE` で止まった。報告は正しく、欠陥は仕様の側にあった。** 実装(`e71b880`)は「誤った式の正確な実装」であり、コード側に欠陥は無い。

### A-1. 価値の式が 2 回目の外出を構造的に排除していた

**初版は候補側を `{d}` 単独で取っていた。**

```
誤: 外出 d の価値 = Σ_i( 余剰_i({d} の最安店) − 余剰_i(既に居る区画の最安店) ) − 外出の費用(d)
正: 外出 d の価値 = Σ_i( 余剰_i((既に居る区画 ∪ {d}) の最安店) − 余剰_i(既に居る区画の最安店) ) − 外出の費用(d)
```

1 周目が argmax として X を選ぶと、任意の Y について `Σ余剰({Y}) − Σ余剰({X}) ≤ 費用(Y) − 費用(X)`。2 周目の比較相手は X を含むので余剰が大きく、`価値₂(Y) ≤ −費用(X) < 0`。**品目の構成にも距離にも価格にも依存しない。**

**1 回目の外出では観測できない。** 増分が負でも「価値 ≤ 0 なら行かない」に吸収されるので、1 日ぶんの検算では一致する。表面に出るのは「2 回行く日が一度も観測されない」という形だけである。

### A-2. `TrustDiscountPermille` の上端と「戻り値は必ず 1 以上」が両立していなかった

値域を **0〜999** にした(初版は 0〜1000)。1000 ちょうどで信用 100 のとき係数が 0 になり、実効価格が 0 を返す。

### 1 回目の実装から引き継ぐもの・捨てるもの

**`e71b880` を土台にしてよい。** 訂正は `ErrandPlanner` の価値の式と `WorldDefinition` の値域検査に閉じている。

| 何 | どうする |
| -- | -------- |
| `Errand` / `EffectivePrice` / `ErrandDelegate` / `StoreChoice` / `TradeSystem` 段5 の分割 | **そのまま使える**(訂正の影響を受けない) |
| `ErrandPlanner` の価値の式 | **A-1 のとおり直す。** 品目ごとの「既に居る区画の最安値」を 1 周につき 1 回求め、区画の評価では `min` を取る |
| `WorldDefinition.TrustDiscountPermille` の値域 | **A-2 のとおり 0〜999 へ** |
| テスト #14 後半(2 つ目だけさらに安いと 2 つ目にも行く)・**#15 後半**(距離 4 と距離 2 なら両方) | **1 回目は 2 区画目が立たないため書けていない。訂正後は書けるので、仕様どおり書く。** とくに **#15 は「核心」印であり、書けていないあいだ T の上限検査は実質デッドコードである** |

> **訂正(2 回目のフェーズ2 の報告による)。初版の本表は #20 も「書けていない」に数えていたが、誤りである。** #20 は `Errand.LaborLossPermille` の 2 回直接呼び出しで書かれており、2 区画目の成立を要求しない。`e71b880` の時点で `ErrandTests.cs` に実装済みだった。**書けていなかったのは #14 後半・#15 後半の 2 件である**([process/03](../process/03-corrections.md) 規則1 — 狭い側に倒す)。
| **#29**(利得が負にならない) | **新設。** A-1 の変異を直接押さえる唯一のテスト |
| `TradePipelineTests` の 3 件 | **1 回目は「外出は 1 日 1 区画まで」を前提に実測値を取り直していた。その前提は欠陥であって仕様ではない。** `NecessityIsSettledBeforePreference`(**[#81](https://github.com/stama72/visionary/issues/81) の検出器であり、W2-08 の「核心」#29**)は `AmpleLiquidFunds` を 1000 → 100,000、2 日 → 3 日へ動かしていた。**訂正後の挙動で取り直すこと。** doc コメントに「1 日 1 区画」と書いてはならない |
| `ObservationsDoNotGrowWithoutBound` の上界の緩和 | **維持してよい。** 訂正とは独立で、理由(訪れた区画の全売り注文を観測する)は現行の [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) のままである |

> **実測値を assert に置くときは、doc コメントに「何を壊すとこの値が動くか」を書くこと。** 値そのものは仕様ではない。1 回目は動かした値の根拠を「1 日 1 区画」という**欠陥の記述**に置いていた — これは [process/03](../process/03-corrections.md) 規則1 の「広い保証」と同型で、**読み返しても実測ログに嘘が無いぶん穴が見えない。**

## 別表 B: 2 回目の差し戻しの裁定(フェーズ1、2026-09-20)

**2 回目のフェーズ2 が `IMPL-BLOCKED` で止まった。報告は正しい。** 60 日走行で `Errand.Surplus` が `OverflowException` を投げ、`BaseValue`(相場項)が 743 万になっていた。裁定は 2 つに分かれる。

### B-1. `Errand.Surplus` の戻り値を `long` にする(本タスクの中)

**仕様の内部矛盾だった。** 「作るもの」#1 が `int` と書きながら、同じ節が「中間の積は `long`」、5.6 が「利得と価値は `long` で積む」と書いていた。**積む先だけが広く、1 項が狭い。** 直した(上の #1)。

**`checked` で int へ戻さないこと。** `OverflowException` は「経済が発散している」という別の事実を運んでいたが、**例外は検出器ではない** — 発散が半分の速さなら 60 日では投げず、同じ欠陥が緑で通る。検出は B-2 が持つ。

### B-2. 相場基準の指数的発散は #98 の外である([#120](https://github.com/stama72/visionary/issues/120))

**フェーズ2 は「A-1 訂正前 / 後」の 2 変種しか測っておらず、`master` を測っていなかった。フェーズ1 が `master`(`43afada`)で測り直した。**

| 日 | 最大提示価格(master・シード1) | 売り注文の件数 |
| -- | ------------------------------ | -------------- |
| 1  | 290 | **10** |
| 10 | 333 | 3 |
| 20 | 1,556 | 2 |
| 40 | 33,634 | 2 |
| 60 | **725,771** | 2 |

**#98 が存在しない木で、1 日あたり約 1.166 倍の複利で発散し、売り注文が 10 件から 2 件へ枯れる。** #98 は値付けに一切触れていないので、原因ではない。A-1 の訂正がしたのは、暴走した `BaseValue` と baseline の安い見積もりの組み合わせを早く評価するようになり、**int の限界に届くのが早まった**ことだけである。

**したがって #98 は止めない。** [CLAUDE.md](../../CLAUDE.md)「Exit Criteria を脅かすかで仕分ける」でいえば #120 は**脅かす側**だが、**#98 のスコープの中では直せない**(値付けの設計判断であり、[GDD02c §1](../03-gdd/02c-price-and-budget.md) を書き換える設計タスクになる)。WIP の規律からも、#98 の実装と並行して回す対象ではない。

### B-3. 本タスクが「経済が発散していないこと」を保証しないと明記する

**`Errand.Surplus` を `long` にすると 60 日走行は緑に戻るが、それは型が広いあいだ通るだけである。** [process/03](../process/03-corrections.md) 規則1 の「広い保証」そのものなので、**緑が何を意味しないかを書き残す。**

- **`TradePipelineTests.ObservationsDoNotGrowWithoutBound` の doc コメントに 1 段落足すこと。** 「本テストは `Knowledge` の件数の上界しか見ない。**同じ 60 日走行で提示価格が 6 桁へ発散し売り注文が 2 件へ枯れることを、本テストは検出しない**([#120](https://github.com/stama72/visionary/issues/120))」
- **帯を見る検出器は本タスクで足さない。** 足せば赤になる(実際に発散しているため)。検出器は #120 が処方と一緒に持つ
- **PR 説明にも書くこと。** 「テストは緑だが、M0 の経済は 60 日で発散する。#120」

> **これを書かないと、次に 60 日走行を読んだ者が「上界のテストが通っているから経済は健全」と読む。** 発散を見つけたのは `OverflowException` という偶然であり、それを `long` で消した以上、**偶然の報せも無くなる。**

## 別表 C: 3 回目の差し戻しの裁定(フェーズ1、2026-09-20)

**3 回目のフェーズ2 のレビュー1 巡目が `SPEC-OUTSIDE`(I-1)を出した。指摘は正しい。**

### C-1. 耐久の余剰が単位を取り違えていた

**`BuyerBudget.Decide` が返す `q` は「用途の単位」で、耐久だけ耐久値である。`w` と `p` は貨幣/個である。** §5.5 はこれを無変換で `Errand.Surplus` へ渡していた。M0 の `ToolDurabilityPerUnit` = 13,000 なので、**工具の行だけ余剰が 13,000 倍**になる。耐久の行は `BuyerDemand.Build` が全世帯に無条件で 1 行足すので、**工具が摩耗した日から外出の行き先が鍛冶の居る区画に張り付く。**

訂正は上の §5.5 と「作るもの」#7、および [GDD06 §3](../03-gdd/06-trade-and-negotiation.md)(三角形の底辺の単位)。**根治は `PurchaseQuantityInUnits` を `BuyerBudget.QuantityInUnits` へ移して 1 か所にすること** — 変換が購入の側にしか無かったことが取り違えを許した。

### C-2. 別表 B-2 の overflow の帰属を狭める(**フェーズ1 の訂正**)

**別表 B は 2 回目の `OverflowException` を「相場基準の発散」1 つに帰していたが、原因は 2 つあった。**

溢れた積は `3885 × (5,930,407 − 290)`。

| 因子 | 正体 | 誰のものか |
| ---- | ---- | ---------- |
| `3885` | **耐久値で数えた `q`。** 個数へ直せば **1** | **#98(C-1)** |
| `5,930,407` | 発散した `BaseValue` | [#120](https://github.com/stama72/visionary/issues/120) |

**個数へ直していれば、同じ日の同じ発散でも `int` に収まっていた。** 別表 B-1 が `long` へ広げたこと自体は正しい(積の型として)が、**「overflow が出たのは発散のせいである」という帰属は広すぎた**([process/03](../process/03-corrections.md) 規則1)。

**#120 そのものは揺るがない。** 根拠は overflow ではなく、**フェーズ1 が `master` で直接測った提示価格の系列**(別表 B-2 の表)である。あれは `World.Market` の値を読んだだけで、余剰の計算を一切通していない。**したがって「#98 は止めない」という裁定も維持する** — ただし理由は「overflow は発散のせい」ではなく「`master` に #98 抜きで発散が在る」である。

> **別表 B-3 が「`long` で消した以上、偶然の報せも無くなる」と書いた、その報せがもう 1 件あった。** 書いた本人が、同じ段落で 1 件しか数えていなかった。

### C-3. `ErrandPlannerTests` が用途を 1 種類しか通していなかった

**353 件緑・変異 4 件実測を通り抜けた理由がこれである。** `ErrandPlannerTests` は全件が `DemandPurpose.Necessity` の手組み行で、**耐久の行を 1 本も通していない。** テスト #30・#31 を足し、完了条件に「4 種とも通す」を入れた。

### 持ち越した指摘(3 回目のレビュー1 巡目、I-1 以外)

**フェーズ2 の報告のとおり、I-1 の訂正で実測値が動くものを後ろに回してある。次巡でまとめて直すこと。** とくに次の 1 件は**訂正と独立に必ず再実測する**:

- `TradePipelineTests.NecessityIsSettledBeforePreference` の **`Lines` 逆順変異で赤になるかの再実測**。[#81](https://github.com/stama72/visionary/issues/81) の検出器の判別力が、`AmpleLiquidFunds` を 1000 → 100,000 に広げたことで吸収されている可能性がある

## 別表 D: 4 回目の差し戻しの裁定(フェーズ1、2026-09-20)

**4 回目は `IMPL-BLOCKED`。ただし性質が前 3 回と違う — コードは緑(358 件)で、止まったのはテスト設計の選択である。** 別表 C は入り切り、変異 5 件も赤を実測済み。

**晴れた疑いが 2 件ある(先に記録する)。**

- `NecessityIsSettledBeforePreference` の判別力は**維持されていた**。`Lines` 逆順変異で赤を実測。別表 A が疑った「`AmpleLiquidFunds` を 40 倍に広げたことで走査順の変異を吸収している」は**否定された**
- 3 回目の「疑い」(`ErrandPlanner` が `DemandPurpose` を見ない)は C-1 の一本化で**根治**した

### D-1. 用途フィルタは単体テストで押さえ、パイプラインテストには求めない

**報告された事実**: `UnaffordableNecessityCountsOnlyTheFundsShortfall` の手順9 から `Purpose == Necessity &&` を外す変異が、現本体の自然発生シナリオ(世帯 Id 2・17 日目)で**赤にならない**。その日は `Necessity` 以外の行が `fundsCap == 0` を踏まない。

**なぜ踏みにくいかは構造で説明が付く。** 手順9 に到達するには段4 のゲート(`実効価格 ≤ 現金上限`)を通っている必要があり、`現金上限 ≤ 用途に使える資金 ≤ 流動資金` なので、**本来なら `fundsCap ≥ 1` である。** 踏むのは、**同じ世帯の先行する行が約定して `LiquidFunds` を減らした後だけ** — 段4 の `CashCap` が古くなることが唯一の経路である。

したがって**非必需の行が手順9 で `fundsCap == 0` を踏むには、必需の行(走査が先)が先に資金を使い切っている必要がある。** 自然発生を待つ形では、その組み合わせが出る日を探すことになる。

**裁定: 探さない。単体テストで構成する。**

| | どうする |
| -- | -------- |
| **新設** | `TradeSystemTests` に **`NonNecessityFundsShortfallIsNotCounted`** を足す。必需の行で資金をほぼ使い切らせ、**嗜好の行が古い現金上限のゲートを通ってから `fundsCap == 0` に当たる**世帯を手で組む。**必需の側が `fundsCap == 0` を踏まない**ように置き、期待値は **0**。`Purpose == Necessity &&` を外す変異で **1** になる。**変異を当てて赤を実測すること**(変異 6 件目) |
| **置き場所** | [W2-08](W2-08-offer-price-and-budget.md) のテスト #27(`NecessityShortfallIsCountedOnBothPaths`)・#29 の隣。**#27 は必需の 2 経路、#28 は理由コードを押さえており、「非必需が経路(2)で数えられないこと」はどちらも押さえていない** — ここが本当の穴である |
| **パイプラインテスト** | **本体は変えない。doc コメントだけ直す。** 「本テストは用途フィルタを判別しない(押さえるのは `TradeSystemTests.NonNecessityFundsShortfallIsNotCounted`)」と明記し、**旧本体のまま残っている `<summary>`(「流動資金 0 の世帯」)と変異の実測 remarks(「2 → 3 で赤」)を現本体に合わせて書き直す**(3 回目のレビュー I-a) |

**日/世帯を選び直さない理由がもう 1 つある。** 17 日目は [#120](https://github.com/stama72/visionary/issues/120) の発散する経済の中の 1 日である。**いま別の日を選び直しても、#120 が直れば同じ作業をもう一度することになる。** 自然発生のシナリオを固定値で持つ限り、この再導出は #120 のたびに起きる。

> **パイプラインテストから判別力を取り上げるのは、格下げではなく置き場所の訂正である。** 用途フィルタは `RunOneHouseholdsShopping` の 1 行が持つ**単体の契約**であり、45 世帯 17 日の走行の中で偶然その組み合わせが出るのを待つ形は、**判別力が経済の状態に従属する。** [#81](https://github.com/stama72/visionary/issues/81) が「判別力が無い」と実測して立った issue であることを思い出すこと — 同じ形をもう一度作らない。

### D-2. パイプラインテストの実測値は #120 で動く

本タスクで触る 3 件(`UnaffordableNecessityCountsOnlyTheFundsShortfall` / `NecessityIsSettledBeforePreference` / `ObservationsDoNotGrowWithoutBound`)は、いずれも**発散する経済の中の実測値**を持っている。**doc コメントに「#120 が直れば動く」と 1 行ずつ書くこと。** 書かないと、#120 の PR がこの 3 件を壊したときに「#120 の実装バグ」と読まれる。

## 別表 E: 3 巡目(網羅パス)の裁定(フェーズ2 の I-b 訂正、2026-09-20)

**3 巡目を[網羅パス](../process/01-review.md)に充てた。** 渡した境界は「**上のテスト表の各行が『この実装ミスで落ちる』列で宣言している変異を 1 件残らず列挙し、対応するテストが実際に赤になるか判定せよ**」。宣言は **58 件**あり、**4 件が赤にならなかった**(いずれも実測で確認)。

**うち 3 件は仕様の側の欠陥(象限 I-b)である。** 表の宣言どおりに書いた実装が、その宣言を検出できない。**3 件とも訂正は本タスク仕様の中で閉じる** — 規則そのものは [GDD06 §3](../03-gdd/06-trade-and-negotiation.md)(`FloorDiv`・差の取り方)と [GDD02a §2](../03-gdd/02a-production.md)(外出 1 回ごとの切り上げ)に正しく書かれており、**上位文書は直さない。**

### E-1. #4 の宣言値 45 は算術的に生じない(I-b)

`q = 4・w = 76・p = 54` の積は `4 × 22 = 88` で**偶数**である。`FloorDiv(88, 2) = CeilDiv(88, 2) = 44` なので、**宣言した変異「`CeilDiv` にする(45)」は当てても値が動かない。** 他の 2 ケースは `willingness ≤ price` のガードで除算に到達しない。

**テスト表の他の行も全滅している。** #12(2×11・4×22)・#29(4×22)・#30(1×100)・#31 はすべて積が偶数で、奇数の積になる #7・#9・#10 は 1 の差が行く / 行かないを反転させない帯にある。**`Errand.Surplus` の丸めの向きだけが、検出器を 1 つも持たない。**

| | 訂正 |
| -- | -- |
| **足すケース** | `SurplusNeverGoesNegative` に **`q = 3・w = 57・p = 50` → `FloorDiv(3 × 7, 2)` = `FloorDiv(21, 2)` = **10**」を足す。`CeilDiv` にすると **11** になる |
| **既存ケース** | 44 / 0 / 0 の 3 ケースは**そのまま残す**(`max(0, …)` の検出器としては効いている) |

> **これは「このタスクで特に効く規約」が名指しした 3 つの除算の 1 つである。** 「新しく入る除算は 3 か所で**3 つとも向きが違う**」と書きながら、**向きが違うほうの 1 つ(`FloorDiv`)に検出器が無かった。** [GDD01 §2.3](../03-gdd/01-core-loop-and-progression.md) の切り上げ規約の例外そのものであり、次に読んだ者が「ここも切り上げでは」と直しても誰も気付かない。

### E-2. #14 が、却下した設計案の復活を検出できない(I-b)

**#14 後半は 2 つ目の区画に別品目(`Item.Grain`)を置いている。** 1 周目で選ばれない区画にだけ置く構成で、閾値の手計算は楽になるが、**宣言した変異「1 周目で買う品目を需要リストから外す」を当てても、その品目は 1 周目で買われていないので落ちない。**

**これは[引き継ぎメモ](W2-10-errand-and-store-choice.handoff.md)の「却下した設計案」1 件目そのものである。** 却下理由は「3 つ目の区画がもっと安くてもその差を数えられない」であり、**却下した規則が黙って戻っても、テストも [#29](https://github.com/stama72/visionary/issues/29) の実測も区別しない。**

| | 訂正 |
| -- | -- |
| **足すケース** | #14 に「**両方の遠方区画が同じ品目 A を売り、2 つ目のほうが A を厳密に安く売る**」サブケースを足す。1 周目に区画1、2 周目に区画2 へ行く配置にする |
| **1 周目を動かすのは別品目である** | **区画1 に「1 周目を勝たせる品目 B」を置く。** 区画1 は A も(高値で)売り、区画2 は A だけを安く売る。1 周目は B の余剰で区画1 が勝ち、2 周目は **A の増分**(区画2 の安値 − 区画1 の高値)が `c₂` を超えて区画2 が立つ |
| **当てる変異** | 1 周目で見積もりが得られた行を需要リストから除去する処理を挿入する。**A は 1 周目に区画1 で値が付いているので落ち、2 周目が立たない。**「2 区画」が「1 区画」になって赤 |

> **訂正(implementer の報告による)。本表は当初「単一品目で `c₂ > S(p₂) − S(p₁) > c₂ − c₁` を満たせばよい」と書いていたが、この不等式に解は無い。** 1 周目に近い(高い)ほうが勝つ条件は `S(p₂) − S(p₁) < c₂ − c₁`、2 周目に遠い(安い)ほうが立つ条件は `S(p₂) − S(p₁) > c₂` であり、両立には `c₁ < 0` が要る。**単一品目では、1 周目が高いほうを選んだ時点で、安いほうの増分は 2 周目の費用を超えられない** — A-1 の証明と同じ形の帰結である。**駆動する別品目が要る**([process/03](../process/03-corrections.md) 規則2)。

### E-3. #20 が `ErrandPlanner` の集計を一度も通らない(I-b)

**集計の実体は `ErrandPlanner` の周回にある**(`Σ_d CeilDiv(…)`)。しかし #20 は `Errand.LaborLossPermille` を**テスト本文で 2 回呼んで足しているだけ**で、`perTripSum` をテスト自身が組み立てている。**製品コードの集計を `CeilDiv(Σ_d …)` に変えても動かない。**

**この形を承認したのは別表 A の訂正である。** あのとき「#20 は 2 区画目の成立を要求しない」と書いたのは事実として正しかったが、**その裁定が立っていた前提は「2 区画目が立たない」(A-1 の欠陥)であり、A-1 の訂正が同じ差し戻しでその前提を壊している**([process/03](../process/03-corrections.md) 規則2 — 却下理由の前提が同じ変更で崩れていないか)。**いまは 2 区画へ行く計画が書けるので、集計を通す形で書ける。**

| | 訂正 |
| -- | -- |
| **書き換える** | #20 を **`ErrandPlanner.Plan` を通す形**にし、`plan.LaborLossPermille` を assert する。`Errand.LaborLossPermille` の算術だけを見る現行のケースは**別テストとして残してよい**(`CeilDiv` の向きの検出器にはなっている) |
| **満たすべき条件** | 2 区画へ行き、**往復時間の合計が T 以下**で、かつ **`Σ_d CeilDiv(労働力係数‰ × 往復_d, T)` と `CeilDiv(労働力係数‰ × Σ_d 往復_d, T)` が一致しない**こと |
| **成り立つ組の例** | **T = 12・労働力係数 100‰・往復 2 時間(距離1)と 4 時間(距離2)。** 外出ごと: `CeilDiv(200,12) + CeilDiv(400,12)` = 17 + 34 = **51**。先に合計: `CeilDiv(600,12)` = **50**。合計往復 6 ≤ T |
| **なぜ既存の 2 区画テストでは足りないか** | #14 後半・#15 後半は `LaborLossPermille` を assert しておらず、往復の組(4+4・8+4)が **T = 12 で割り切れる**ので、どちらの数え方でも一致する |

> **労働力係数 300‰ と T = 12 の組では永久に差が出ない**(`300a ÷ 12 = 25a` が恒に整数)。**係数か T を動かすこと。**

### E-4. #9 の「平均する」変異が、パイプラインテスト 1 件にしか拾われていない(I-a — 仕様は直さない)

**これだけは仕様の欠陥ではない。** #9 が固定しているのは観測値(3 日前 90 / 1 日前 50)と期待(50)だけで、行 / 行かないを分ける配置は実装が決める。

`ErrandPlannerTests` のサブケース A は doc コメントに「w = 70」と書いているが、`targetStock: 1・expectedStock: 0` の在庫圧力は 1500‰ なので **実際の `w` は `ApplyPermille(70, 1500)` = 105** である。平均(70)でも `105 > 70` で余剰が正になり、**行ってしまうので赤にならない。** 実測で赤になったのは `TradePipelineTests.UnaffordableNecessityCountsOnlyTheFundsShortfall` の 1 件だけで、**その値は別表 D-2 が「[#120](https://github.com/stama72/visionary/issues/120) が直れば動く」と宣言している。**

**別表 D-1 が「判別力が経済の状態に従属する形をもう一度作らない」と名指しした形に、核心の 1 つが落ちている。** 「**`MarketReference` を流用してはならない**」は本タスクの前提の 1 つである(「見積もり価格は相場基準ではない」節)。

**満たすべき性質**: **最新(50)なら行き、平均(70)なら行かない。** すなわち `余剰(70) ≤ 費用 < 余剰(50)`。**doc コメントの `w` の値も実際の値へ直すこと。**

### E-5. 「宣言の誤り」— 穴ではないが、字面を信じてはならない 7 件

**対応するテストでは落ちないが、別のテストが落とす**(または変異点が存在しない)。**表の本文は書き換えない。** 記録だけ残す — 次に読んだ者が「この行が守っている」と読むのを止めるためである。

| 宣言 | その行のテストでは | 実際に落とすもの |
| ---- | ------------------ | ---------------- |
| #3「`FloorDiv` にする(124)」 | 緑 | **宣言値 124 が誤り**(`1500 ÷ 12 = 125` で割り切れる)。落とすのは #20 |
| #3「往復ではなく片道を渡す」 | 緑(純関数のテストで呼び出し側を含まない) | #19 |
| #11「候補を『全世帯』から作る」 | 緑(**その世界に売り手が 1 戸も居ない**ので候補が増えない) | 他 15 件(#14・#15・#21・#25・#26・#31 とパイプライン各種) |
| #12「`>` を `>=` にして価値 0 で行く」 | 緑(価値 0 の区画が無い) | #14 後半・#16・#29 |
| #21「当日の生産に効かせる(順1 より前に書く)」 | — | **製品コードに変異点が無い。** パイプラインの順は `SimScheduler` に渡す配列が決めており、本ブランチに正規の構築箇所が無い |
| #28「`SortedDictionary` を `Dictionary` に戻す」 | 緑(2 回の走行で挿入順が同じ) | `DeterminismConventionTests`(**機械**)。**#28 は検出器ではない** |
| #27「`Dictionary` の列挙順に依存する」/ #28「帳簿の走査で列挙順に依存する」 | — | **変異点が存在しない**(計画側に `Dictionary` は無く、`Ledgers` は `List`) |

### 完了条件への追加(別表 E)

- [ ] **E-1・E-2・E-3 の訂正後のテストに、それぞれ宣言された変異を当てて赤を実測した**(変異 7〜9 件目)
- [ ] **E-4 の訂正後の #9 に「平均する」変異を当てて赤を実測した**(変異 10 件目)

## 編集してよい文書

worktree をまたいだ所有権の宣言。**ここに挙がっていない文書は触らない。**

- なし(コードとテストのみ)

> **GDD/TDD はフェーズ1 が先行コミットで直し終えている。** フェーズ2 が `docs/03-gdd/` `docs/04-tdd/` `docs/adr/` に差分を出すと `SPEC-OUTSIDE` で止まる(機械が見ている)。**仕様そのものの欠陥(象限 I-b)を見つけたら、直さずに報告すること。**
> **`docs/tasks/W2-10-errand-and-store-choice.handoff.md` は例外で、フェーズ2 が追記する。**

## このタスクで特に効く規約

**機械で捕まるものは書かない**(`BannedSymbols.txt` と `DeterminismConventionTests` がビルドとテストで止める)。

- **`Dictionary` / `HashSet` を新しく持ち込まない。** 訪問区画は `List<int>` で持ち、包含は線形探索でよい(高々 8 件)。**`HashSet<int>` は列挙順が保証されない** — 今回は「含むか」しか問わないので壊れないが、後から「訪問順に回す」を足した瞬間に静かに壊れる
- **除算はすべて `IntegerMath` 経由。** 本タスクで新しく入る除算は `Errand.LaborLossPermille`(`CeilDiv`)・`Errand.Surplus`(`FloorDiv`)・`EffectivePrice`(内側 `CeilDiv` + `ApplyPermille`)の 3 か所で、**3 つとも向きが違う。** それぞれ doc コメントに向きの理由を書くこと
- **係数の定数には単位のコメントを付ける**(`TrustDiscountPermille = 200` は 20% の意)
- **乱数を引かない。** `TradeSystem` は `RandomStream.Trade` を登録に使うだけである

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **#14・#17・#21・#29・#30 と `NonNecessityFundsShortfallIsNotCounted`(別表 D-1)に変異を当てて落ちることを確認し、当てた変異と結果を残した**
- [ ] **パイプラインテスト3 件の doc コメントに「実測値は [#120](https://github.com/stama72/visionary/issues/120) が直れば動く」を書いた**(別表 D-2)
- [ ] **`ErrandPlannerTests` が `DemandPurpose` を 4 種とも通している**(3 回目のレビュー I-1 は「耐久の行が 1 本も通っていない」ことに守られていた)
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
- [ ] **`ObservationsDoNotGrowWithoutBound` の doc コメントに「本テストは価格の発散を検出しない([#120](https://github.com/stama72/visionary/issues/120))」を足した**(別表 B-3)
