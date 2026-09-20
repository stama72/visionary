# W2-09: 外出と店の選択

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
    public static int Surplus(int quantity, int willingness, int price);
}
```

- **`TravelHours` は `StoreChoice` から移す**(既存のテスト `TravelHoursCountsTheRoundTrip` も移す)
- **`Cost` に割る対象は無い。** 引数が 2 つしか無いことが、旧の「目標在庫で割り戻す」が戻らない歯止めである
- **`LaborLossPermille` の分母は `T`(可処分時間、`WorldDefinition.DisposableHours` = 12)であって 24 ではない**
- **`Surplus` の中間の積は `long`。** `quantity × (willingness − price)` は int を超えうる
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
- **0〜1000 の外を拒む。** 1000 を超えると信用 100 で係数が 0 以下になり、実効価格が 0 になって下流が投げる

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

#### 5.5 余剰

```
余剰(line, p) = p が「無い」なら 0
                q = BuyerBudget.Decide(line, p).Quantity              ← **ゲートを通した値**
                w = ApplyPermille(line.BaseValue, line.StockPressurePermille)
                Errand.Surplus(q, w, p)
```

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
        利得 = Σ_line( 余剰(line, 最安見積もり(line.ItemId, {d}))
                       − 余剰(line, 最安見積もり(line.ItemId, {自区画} ∪ 行くと決めた区画)) )
        価値 = 利得 − Errand.Cost(往復, 委託先の機会費用)
        価値 > 最良の価値 なら 最良を更新
    最良の区画が無ければ終わり
    行くと決めた区画 へ足す
    往復時間の合計 += 往復
    労働損失‰      += Errand.LaborLossPermille(委託先の労働力係数‰, 往復, T)
```

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

| 旧手順 | どうする |
| ------ | -------- |
| 1. `TargetStockInUnits` で目標在庫を個数へ | **消す**(移動費の分母が無くなった)。`TradeSystem.TargetStockInUnits` ごと削除 |
| 2. `_storeChoice.TrySelect(…, targetInUnits, errandOpportunityCost, …)` | `_storeChoice.TrySelect(world, household, line.ItemId, visitedDistrictIds, out store)` |
| 3・4. `BuyerBudget.Decide(line, store.UnitEffectivePrice)` | **そのまま**(#97 で既に実効価格を渡している) |
| 5・6. 個数へ直す / 0 以下なら終わり | そのまま |
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
| 6  | `EffectivePriceRejectsTrustOutsideZeroToHundred` | 信用 **101** と **−1** で投げる。提示価格 **0** でも投げる | 値域を開けたまま(GDD01 §2.1 の 0〜100 は型で守れない)。#44 が ‰ を渡したときに黙って負の価格になる |  |
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

**「核心」印は 12 件ある。** [process/02](../process/02-task-spec.md) の「1 タスクあたり 2〜3 件」を超えるので、**実際に変異を当てるのは次の 3 件に絞る**。

| 印を当てる | なぜこの 3 件か |
| ---------- | --------------- |
| **#14** | 貪欲の差の取り方。**間違えても外出は起きるし、回数も「それらしく」増える** — 結果を眺めて気付ける種類の誤りではない |
| **#17** | 計画が在庫を見ないこと。**破っても緑のままで、壊れるのは [#29](https://github.com/stama72/visionary/issues/29) の実測の解釈可能性だけである**(区画差が機構の帰結か走査順の産物かを切り分けられなくなる) |
| **#21** | 翌日の生産が減ること。**issue #98 の閉じる条件そのもの**であり、書き忘れても `ProductionSystem` は 0 を読んで普通に動く |

**残りの「核心」印は「壊れたときの影響が大きい」ことだけを示す** — 変異は当てず、レビュアーが読む優先度として使う。

### #12 の数値の出どころ

**[GDD06 §3](../03-gdd/06-trade-and-negotiation.md) の例をそのまま再現する。** 目標在庫 6・基礎値 65・相場 54 で:

| 在庫 | 在庫比‰ | 在庫圧力‰ | w = `ApplyPermille(65, 圧力)` | 到達在庫 | q | 余剰 | 費用 16 と比べて |
| ---- | ------- | --------- | ----------------------------- | -------- | - | ---- | ---------------- |
| 6 | 1000 | 1000 | 65 | 8 | 2 | `FloorDiv(2 × 11, 2)` = **11** | 行かない |
| 4 | 667 | 1166 | 76 | 8 | 4 | `FloorDiv(4 × 22, 2)` = **44** | 行く |

> **GDD06 §3 は当初この 2 行を 11 / 43 と書いていた(小麦粉の例も 38)。** 実数で `65 × 1.166 = 75.79` のまま三角形を取って最後に切り捨てた値であり、**整数の式(`ApplyPermille` は `CeilDiv`)では 44(小麦粉は 39)になる。** 本ブランチの先行コミットで直してある。**結論(行く / 行かない)は変わらない。**

## 編集してよい文書

worktree をまたいだ所有権の宣言。**ここに挙がっていない文書は触らない。**

- なし(コードとテストのみ)

> **GDD/TDD はフェーズ1 が先行コミットで直し終えている。** フェーズ2 が `docs/03-gdd/` `docs/04-tdd/` `docs/adr/` に差分を出すと `SPEC-OUTSIDE` で止まる(機械が見ている)。**仕様そのものの欠陥(象限 I-b)を見つけたら、直さずに報告すること。**
> **`docs/tasks/W2-09-errand-and-store-choice.handoff.md` は例外で、フェーズ2 が追記する。**

## このタスクで特に効く規約

**機械で捕まるものは書かない**(`BannedSymbols.txt` と `DeterminismConventionTests` がビルドとテストで止める)。

- **`Dictionary` / `HashSet` を新しく持ち込まない。** 訪問区画は `List<int>` で持ち、包含は線形探索でよい(高々 8 件)。**`HashSet<int>` は列挙順が保証されない** — 今回は「含むか」しか問わないので壊れないが、後から「訪問順に回す」を足した瞬間に静かに壊れる
- **除算はすべて `IntegerMath` 経由。** 本タスクで新しく入る除算は `Errand.LaborLossPermille`(`CeilDiv`)・`Errand.Surplus`(`FloorDiv`)・`EffectivePrice`(内側 `CeilDiv` + `ApplyPermille`)の 3 か所で、**3 つとも向きが違う。** それぞれ doc コメントに向きの理由を書くこと
- **係数の定数には単位のコメントを付ける**(`TrustDiscountPermille = 200` は 20% の意)
- **乱数を引かない。** `TradeSystem` は `RandomStream.Trade` を登録に使うだけである

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **#14・#17・#21 に変異を当てて落ちることを確認し、当てた変異と結果を残した**
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
