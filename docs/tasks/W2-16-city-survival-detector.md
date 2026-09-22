# W2-16: 30日で都市が死なないことを見る検出器を置く

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#173](https://github.com/stama72/visionary/issues/173)              |
| 根拠     | [GDD02 §8](../03-gdd/02-economy.md)-2・-3 / [GDD02a §1](../03-gdd/02a-production.md) / [GDD02b §1](../03-gdd/02b-consumption-and-household.md) / [GDD02d §2.1](../03-gdd/02d-external-market-and-money.md) / [GDD01 §4.4](../03-gdd/01-trust-and-conversation.md) |
| ブランチ | `feat/173-city-survival-detector`                                    |
| worktree | 本体ツリー(パイプライン)                                           |

**`src/` を一文字も変えないタスクである。** 作るのは `tests/` の検出器だけで、都市が死ぬ原因は直さない(issue #173「この issue は検出器だけを持つ」)。

## 前提 — 「書いた時点で赤」を、緑を要求する機械の中へどう置くか

**issue #173 は「この検出器は、書いた時点で赤である」と書いている。そのままでは置けない。** `dotnet test` が赤だと CI が落ち、パイプラインは `RED` の停止則で止まる([05-phase-sessions](../process/05-phase-sessions.md))。

**開発者の決定(2026-09-22): 反転して置く。** 測定コードは最終形のまま書き、assert だけを「**病理がまだある**」向きにする。都市が30日生き延びた日にその assert が自動で赤くなり、直ったことが機械で分かる。

| 却下した案 | 却下理由 |
| ---------- | -------- |
| `[Theory(Skip = ...)]` | CI は緑になるが、**直っても誰も気付かない。**「Skip を外す」を人の記憶に預けることになる |
| `[Trait("Category","Survival")]` + CI の別ジョブ(`continue-on-error`) | 向きは正しいが、`dotnet test Visionary.sln` に `--filter` を足す必要があり、CLAUDE.md・`ci.yml`・`impl` / `wrap` の契約へ波及する。**本タスク(impl)の射程ではなく設計 issue の仕事である** |

### 反転の代償と、それを受ける仕掛け

**反転すると空振りの向きが逆になる。** 正の向きの検出器は測定が壊れていれば赤くなるが、**反転した検出器は測定が壊れると永久に緑である**(「違反がある」を主張しているので、壊れて全日違反に見えても通る)。

したがって**条件ごとに「測定が値を見ていること」を守る空振り防止を置く**(下の「3. 検出器」)。これは正の向きへ反転した後もそのまま残る(正の向きでは論理的に含意されるので、緑のままである)。

### 終わり方

**向きが反転しきったとき、この検出器は正の向きの `[Theory]` だけになる。** 反転側のメソッドは `[InlineData]` が空になった時点で**メソッドごと削除する**(xUnit は `[InlineData]` の無い `[Theory]` をエラーにする)。そのとき issue #173 が閉じる。

## スコープ

**含まない:**

- **`src/` の変更すべて。** 都市が死ぬ原因([#30](https://github.com/stama72/visionary/issues/30) 懸念2 の必需の取り置き / [GDD02d §2.4](../03-gdd/02d-external-market-and-money.md) の天井による流出 / [#174](https://github.com/stama72/visionary/issues/174) の自家消費)は本タスクでは直さない
- **60日の検出器**([#149](https://github.com/stama72/visionary/issues/149) / [#154](https://github.com/stama72/visionary/issues/154) / W2-14 の2本)。**条件ごとに地平線が違ってよい**(issue #173)ので、30日へ揃えない
- **`vsim` への配線。** 本タスクの成果物は `tests/` にある(issue #173 の閉じる条件)
- **条件を [GDD02 §8](../03-gdd/02-economy.md)-3 の強さへ上げること。** §8-3 は「**各レシピの**生産停止が常態化しない」だが、issue #173 の条件1 は「**都市の生産回数の合計** > 0」であり、1職業が止まっても他が動いていれば成立する。**弱いほうを採る** — 本タスクは #173 の閉じる条件を見る検出器であって、§8-3 の検証項目ではない。この差は doc コメントに書く(下の「3. 検出器」)

### 変えない既存コード(規則8。[02-task-spec](../process/02-task-spec.md))

**本タスクは `src/` を変えないので、表が持つのは「検出器が読む側の規則」である。** 読み方が現行の節とずれていれば、検出器は動いているのに別のものを測る。

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `ProductionSystem.RunOneHousehold` が `household.ProductionRuns` を**毎日書く(0の日も書く)** | [GDD02a §1](../03-gdd/02a-production.md) | **一致。** §1 は日次で `生産量 = min(入力から作れる回数, 生産能力)` を解き、「生産量が 0 の日は停止」と書く。0 の日も書かれるので、`Advance(24)` の直後に読めば当日の値である |
| `ProductionSystem` が書くのは**その世帯の当日ぶんだけ**(累積ではない) | [GDD02a §1](../03-gdd/02a-production.md) | **一致。** §1 の式は当日の実行回数であり、`HouseholdState.ProductionRuns` の doc も「当日の生産量(実行回数)」 |
| `TradeSettlement.Execute` が**1約定につき買い手に `Purchase`・売り手に `Sale` を1行ずつ**記帳する | [GDD01 §4.4](../03-gdd/01-trust-and-conversation.md) | **一致。** §4.4 は「`direction` を持つのは、1つの約定を買い手と売り手が1行ずつ記帳するためである」。**`Purchase` だけ数えれば二重計上なしで約定件数になる** |
| `TradeSettlement.ExecuteImport` / `ExecuteExport` が `CounterpartyId` に `HouseholdState.ExternalMarketSellerId` を入れる | [GDD02d §2.1](../03-gdd/02d-external-market-and-money.md) | **一致。** §2.1 は窓口に「既存の世帯 Id と衝突しない非負 int を1つ」予約する。**`CounterpartyId != ExternalMarketSellerId` が「都市内の約定」の判定そのものになる** |
| 都市生産品 = 小麦粉・薪・パン・ビール・工具(itemId 4〜8) | [GDD02d §2.1](../03-gdd/02d-external-market-and-money.md) の表 | **一致。** `WorldDefinition.BuildM0` の `externalBuyPrice` のコメントも「都市生産品(4〜8)だけが外部買値を持つ」 |
| `ConsumptionSystem` が減らすのは**世帯在庫だけ**(工房在庫ではない)、在庫は0で下げ止まる | [GDD02b §1](../03-gdd/02b-consumption-and-household.md) | **一致。** §1 は「減らすのは世帯在庫であって工房在庫ではない」「在庫は 0 で下げ止まり、負にならない」。**条件3 が世帯在庫を見るのはこのためである** — 工房在庫を見ると、パン屋が売れ残りのパンを抱えて飢えている状態([#174](https://github.com/stama72/visionary/issues/174))が「空でない」に見える |
| `TradeSettlement.Execute` の**買い手の在庫の行き先**(必需・嗜好 → 世帯在庫、生産の入力・耐久 → 工房在庫) | [GDD02b §3.2](../03-gdd/02b-consumption-and-household.md) | **一致。** §3.2 は「買った品の行き先は用途で決まる。必需・嗜好の消費は**世帯在庫**へ」と書く。**条件3 が検出器として生きているのはこの行のためである** — 購入で世帯在庫が戻る経路が無ければ、世帯在庫は消費で単調に減るだけになり、「day 30 に全戸空」は経済の状態と無関係に必ず成立してしまう |
| `WorldDefinition.M0` の校正値(初期世帯在庫 薪28・パン6・ビール1 を含む) | [GDD02d §4.4](../03-gdd/02d-external-market-and-money.md) / [GDD02b §2](../03-gdd/02b-consumption-and-household.md) | **一致。触らない。** 初期在庫が3品目とも正なので、**day 1 に「全戸空」は起こりえない** — 条件3 の空振り防止(下記)が成立する根拠である |
| `SimScheduler.Advance` / `Tick.DayIndex` の対応 | [ADR-0002](../adr/0002-time-model-and-determinism.md) / [TDD01 §3.3](../04-tdd/01-sim-core-and-m0.md) | **一致。** 1 tick = 1時間、24 tick = 1日。境界の具体例は下の「順序・境界」 |

## 作るもの

### 0. 置き場所

**`tests/Visionary.Sim.Tests/Systems/TradePipelineTests.cs` に足す。** 新しいファイルを立てない — 系統の組み立て(`FullPipeline`)を複製すると、系統が増えた日に片方だけ古くなって黙って別の世界を測る。

### 1. 走査 — `ScanThirtyDays`

3条件を1回の走行でまとめて採る private static ヘルパーと、その結果の器を置く。

```csharp
/// <summary>#173 の3条件を day 1〜30 について評価した結果。</summary>
private sealed class CitySurvivalScan
{
    /// <summary>条件1(都市の生産回数の合計 > 0)が破れた日。1始まり。</summary>
    public List<int> ProductionStoppedDays { get; } = new();

    /// <summary>条件2(都市生産品の都市内約定件数 > 0)が破れた日。1始まり。</summary>
    public List<int> NoInternalSettlementDays { get; } = new();

    /// <summary>条件3(世帯在庫が空の世帯が全世帯にならない)が破れた日。1始まり。</summary>
    public List<int> AllHouseholdsEmptyDays { get; } = new();

    /// <summary>30日の延べ生産回数(全世帯・全日の合計)。空振り防止に使う。</summary>
    public long TotalProductionRuns { get; set; }

    /// <summary>30日の延べ都市内約定件数(都市生産品)。空振り防止に使う。</summary>
    public long TotalInternalSettlements { get; set; }

    /// <summary>世帯数。空振り防止に使う。</summary>
    public int HouseholdCount { get; set; }

    /// <summary>走行後の world.Now.DayIndex。空振り防止に使う。</summary>
    public long FinalDayIndex { get; set; }

    /// <summary>ITestOutputHelper へ流す1行(下の 4)。</summary>
    public string Format(long seed) { /* 下の 4 の書式 */ }
}

private static CitySurvivalScan ScanThirtyDays(long seed)
```

手順:

1. `var definition = WorldDefinition.M0;` / `var world = WorldGenerator.Generate(definition, new RandomSource(seed));` / `var scheduler = new SimScheduler(FullPipeline(definition), new RandomSource(seed));`(既存の検出器と同じ組み立て)
2. `for (int day = 1; day <= 30; day++) { scheduler.Advance(world, ticks: 24); /* 3条件を評価して記録 */ }`
3. **最初の違反で止めない。** 30日を走り切り、違反日をすべて積む(既存の60日検出器と同じ規律)。最初の違反で止めると、何日目まで生きたのかも、どの条件がいくつ破れたのかも読めない
4. 走行後に `FinalDayIndex` / `HouseholdCount` を詰める

**条件2 の帳簿の走査は、その日の中で全世帯ぶん走ってよい**(`world.Ledgers` は追記のみで剪定されない)。30日 × 10世帯 × 高々数百行であり、カーソルを持って最適化しない — 添字の持ち回しは取り違えの種にしかならない。

### 2. 3条件の定義

**day k(k = 1〜30)の評価は `scheduler.Advance(world, ticks: 24)` の直後に行う**(= その日の終わり)。**day k が指すのは `Tick.DayIndex == k − 1` の1日である**(下の「順序・境界」)。

| # | 条件(成立する形) | 破れた日として記録する形 | 根拠 |
| - | ------------------ | ------------------------ | ---- |
| 1 | 全世帯の `household.ProductionRuns` の合計 > 0 | 合計が **0** | [GDD02a §1](../03-gdd/02a-production.md)(生産量0の日は停止)/ [GDD02 §8](../03-gdd/02-economy.md)-3 |
| 2 | 当日の**都市内約定件数** > 0 | 件数が **0** | [GDD01 §4.4](../03-gdd/01-trust-and-conversation.md) / [GDD02d §2.1](../03-gdd/02d-external-market-and-money.md) |
| 3 | 「3品目とも0の世帯」が**全世帯ではない** | 全世帯が3品目とも0 | [GDD02b §1](../03-gdd/02b-consumption-and-household.md) |

**条件2 の件数の数え方**(1行が1約定になるように数える):

```
world.Ledgers の全世帯ぶんの行のうち、次をすべて満たす行の本数

    entry.OccurredAt.DayIndex == day - 1
    entry.Direction == LedgerDirection.Purchase
    entry.CounterpartyId != HouseholdState.ExternalMarketSellerId
    Item.Flour <= entry.ItemId && entry.ItemId <= Item.Tools
```

- **`Purchase` だけ数えるのは二重計上を避けるためだけである**([GDD01 §4.4](../03-gdd/01-trust-and-conversation.md)。1約定は買い手と売り手が1行ずつ記帳する)。`Sale` を足すと件数が2倍になる。**窓口への輸出を落としているのは向きではなく相手の条件である**(輸出の `Sale` は `CounterpartyId == ExternalMarketSellerId`)— 向きの条件が輸出を守っていると読まないこと(レビュー2巡目)
- **`CounterpartyId != ExternalMarketSellerId` が「都市内」である**(開発者の決定、2026-09-22)。窓口からの輸入で緑になるのを防ぐ。**都市の中で財が回っているかを見る検出器だからである**
- **品目は 4〜8(都市生産品)。** `Item.Flour`(4)〜`Item.Tools`(8)の範囲で書き、`ExternalBuyPrice` が正かどうかでは判定しない — 校正値([GDD02d §4.4](../03-gdd/02d-external-market-and-money.md))は調整対象であり、検出器の母数を値に依存させない

**条件3 の「空の世帯」の定義**:

```
household.HouseholdInventory[Item.Bread] == 0
    && household.HouseholdInventory[Item.Firewood] == 0
    && household.HouseholdInventory[Item.Beer] == 0
```

**3品目の論理積である**(issue #173 の「世帯在庫(パン・薪・ビール)が 0 の世帯」の字義どおり)。品目ごとに「全世帯で0」を見る形は採らない — ビール(嗜好、[GDD02b §1](../03-gdd/02b-consumption-and-household.md))が都市から消えることは飢餓とは別の事象であり、issue #173 の閉じる条件より強い主張になる。

**条件3 は [GDD02 §8](../03-gdd/02-economy.md)-2 の検証項目ではない**(レビュー2巡目の訂正)。§8-2 の判定基準は「**困窮状態の世帯が全体の一定割合を超えない。困窮は破産中フラグが立っている状態を指す**」であり、世帯在庫とも「全世帯」という閾値とも無関係である。条件3 が緑でも §8-2 については何も言えない。**条件1 が §8-3 より弱いのと同じ釘である**(上の「スコープ」)。

### 3. 検出器 — 向きは実測が決める

**条件ごとに2つの向きを用意し、シードをどちらへ入れるかは実測で決める。**

| 条件 | 反転(病理がまだある側) | 正(直った側) |
| ---- | ------------------------ | -------------- |
| 1 | `ProductionStopsForAWholeDayWithinThirtyDays` | `ProductionNeverStopsForAWholeDayOverThirtyDays` |
| 2 | `InternalSettlementsOfCityGoodsDisappearWithinThirtyDays` | `InternalSettlementsOfCityGoodsNeverDisappearOverThirtyDays` |
| 3 | `AllHouseholdsRunEmptyWithinThirtyDays` | `SomeHouseholdAlwaysHoldsNecessitiesOverThirtyDays` |

**割り振りの規則(実装者が判断しない形で決める)**:

- 30日のうち**違反日が1日以上あるシード** → **反転側**の `[InlineData]`
- 違反日が**0日のシード** → **正側**の `[InlineData]`
- **どちらかが空になったら、そのメソッドは書かない**(xUnit は `[InlineData]` の無い `[Theory]` をエラーにする)
- シードは 1 / 2 / 3 / 7 / 42 の5つ(issue #173)

**assert の順序は3段。どの向きでも同じ形にする。**

1. **核心と独立な空振り防止**(先に置く): `Assert.Equal(30, scan.FinalDayIndex)` / `Assert.Equal(10, scan.HouseholdCount)`
2. **条件別の空振り防止**(測定が値を見ていることを守る。**反転では核心がこれを含意しないので、核心より前に置く**):

   | 条件 | 置く assert | これが守るもの |
   | ---- | ----------- | -------------- |
   | 1 | `scan.TotalProductionRuns > 0` | 生産回数を読む先を間違えて常に0を見ている |
   | 2 | `scan.TotalInternalSettlements > 0` | 絞り込みが全行を落としている(向き・相手・品目のどれかの取り違え) |
   | 3 | `scan.AllHouseholdsEmptyDays.Count < 30` | 在庫の添字を間違えて常に空に見えている(初期在庫は薪28・パン6・ビール1 なので day 1 は必ず空でない) |

3. **核心**: 反転側は `Assert.True(days.Count > 0, ...)`、正側は `Assert.True(days.Count == 0, ...)`。**`Assert.NotEmpty` / `Assert.Empty` は使わない** — 失敗時に伝えることがある(下記)

**失敗メッセージ**:

- 反転側: `seed={seed}: 条件N が30日すべてで成立した。この条件はこのシードについて直っている。W2-16 の手順に従い、このシードを {正側のメソッド名} の [InlineData] へ移し、doc コメントの基準値を更新すること(#173)。` — **「テストが壊れた」ではなく「経済が直った」と読ませる。反転した検出器の失敗は朗報である**
- 正側: 違反日の一覧と条件ごとの内訳(`seed={seed}: 条件N が day {…} で破れた(違反{n}日 / 延べ生産回数={…} / 延べ都市内約定={…})`)

**doc コメントに書くこと**(各メソッド):

- `【核心】W2-16 タスク仕様テスト表 #N(検出器)。M0・シード1/2/3/7/42・30日。`
- **この検出器は「病理がまだある」ことを断定している。向きが反転するのは経済が直った日である**(反転側のみ)
- **30日である理由**: プレイテストで使うのが1季 = 30日だから。60日にすると季節の切り替わりと [#172](https://github.com/stama72/visionary/issues/172) の段差が混ざる。**帯の検出器([#149](https://github.com/stama72/visionary/issues/149))は60日のまま**(issue #173)
- **条件1 は [GDD02 §8](../03-gdd/02-economy.md)-3 より弱い**(都市全体の合計であって、レシピごとではない)。§8-3 を満たしたと読まないこと
- **基準値の実測**(下の 4)と日付
- **変異の実測**(`mutator` の報告を転記。下の 5)

### 4. 基準値を読む口 — `ITestOutputHelper`

**反転した検出器は緑で通るので、失敗メッセージからは基準値が読めない。** かといって assert を一時的にひっくり返して読むのは変異であり、[ADR-0013](../adr/0013-mutation-measurement-separated.md) が implementer に禁じている。

したがって `TradePipelineTests` に xUnit の出力口を足す(**本プロジェクト初出**)。

```csharp
private readonly ITestOutputHelper _output;

public TradePipelineTests(ITestOutputHelper output) => _output = output;
```

- 各検出器は assert の**前**に `_output.WriteLine(scan.Format(seed));` を呼ぶ
- `Format` の書式: `seed={seed} 条件1: 違反{n}日 (day {カンマ区切り}) / 条件2: 違反{n}日 (day …) / 条件3: 違反{n}日 (day …) / 延べ生産回数={…} / 延べ都市内約定={…}`
- 読む口: `dotnet test Visionary.sln -c Release --filter "FullyQualifiedName~TradePipelineTests" --logger "console;verbosity=detailed"`
- **この値を doc コメントへ転記する**(日付を添える)。**転記した数字の出所はこの出力であって、推測や紙の計算ではない**

> コンストラクタは同じクラスの既存テストにも付くが、`_output` を使わないテストは何も変わらない。

### 5. 変異(核心印。フェーズ2 が `mutator` に渡す)

**変異を選ぶのは本仕様である。** implementer もレビュアーも当てない([ADR-0013](../adr/0013-mutation-measurement-separated.md))。

| # | 場所 | 内容 | 期待 |
| - | ---- | ---- | ---- |
| **M-1** | `ProductionSystem.RunOneHousehold` の `household.ProductionRuns = runs;` | `household.ProductionRuns = Math.Max(1, runs);` | **条件1 の反転側が赤**(全世帯が毎日1以上を報告するので違反日が消える)。正側に居るシードがあれば緑のまま。**検出器が生産回数そのものを見ていることの実測** |
| **M-2** | `TradeSettlement.Execute` の `world.Ledgers[buyer.Id].Add(...)`(買い手側の記帳) | 行ごと削る | **条件2 の空振り防止(`TotalInternalSettlements > 0`)が赤。** 反転側の核心は緑のまま — **反転した検出器が空振りで緑になる経路を、この1本が塞いでいることの実測である** |
| **M-3** | `TradeSettlement.ExecuteImport` の記帳の `CounterpartyId = HouseholdState.ExternalMarketSellerId` | `CounterpartyId = 0` | **条件2 の反転側が赤。****ただし赤になるのは、違反日の全部で窓口から都市生産品を1件以上買っているときだけである** — 窓口の輸入も無い違反日が1日でも残れば違反日リストは空にならず、**緑のまま通る**。[#120](https://github.com/stama72/visionary/issues/120) の実測(シード1・day 3: 都市内約定0・窓口購入20)から赤を期待するが、**緑でも検出器の壊れではない。** その場合は実測を doc コメントに書いて [#41](https://github.com/stama72/visionary/issues/41) へ落とす — **止まらなくてよい**(裁定済み)。**母数を「都市内だけ」にした決定を突く唯一の変異である** |
| **M-4** | `ConsumptionSystem` の `household.HouseholdInventory[itemId] -= consumedQuantity;` | 行ごと削る | **条件3 の反転側が赤**(世帯在庫が減らないので全戸空の日が来ない) |

### 6. 別表 — レビューで足したもの(フェーズ2、2026-09-22)

**上の 1〜5 は implementer に渡した時点の指示であり、最終形ではない。** レビュー2巡目(網羅パス)の結果として次を足す。

#### 6.1 反転した検出器が原理的に見ないもの(doc コメントに書く)

**核心が `違反日 > 0`(存在)なので、母数を減らす方向の取り違えは違反日を増やすだけで、核心は緑のまま通る。** 捕まるのは「数え過ぎ(違反日が消える)」と「全滅(母数が0)」だけである。

| 条件 | 数え過ぎ | 数え落とし | 全滅 |
| ---- | -------- | ---------- | ---- |
| 1 | 反転側が見る | **見る assert が無い** | 空振り防止が見る |
| 2 | 反転側が見る | **見る assert が無い** | 空振り防止が見る |
| 3 | 反転側(seed 7)が見る | **正側4シードが見る** | 空振り防止が見る |

**条件3 だけが両方向を見ている。** 正側が居るからである。向きが反転しきって条件1・2 にも正側が立てば、この非対称は自然に消える。

#### 6.2 足す検出(日付の対応を機械で見る)

**条件2 の帳簿の絞り込みだけが日付を使うのに、その日付を検算する assert が無い。** `dayIndex` が ±1 ずれても `Total > 0` も違反日の非空も保たれ、**4本とも緑のまま**通る。壊れるのは値ではなく意味で、doc コメントへ転記した違反日と issue #173 の「day 9」「day 30」が静かに1日ずれる。

| 足すもの | 形 |
| -------- | -- |
| `CitySurvivalScan.TotalCityGoodInternalRowsIgnoringDate` | **日付で絞らずに**、向き・相手・品目だけで数えた30日ぶんの都市内約定件数 |
| 条件2 の検出器の空振り防止(核心より前、`TotalInternalSettlements > 0` の直後) | `Assert.Equal(scan.TotalCityGoodInternalRowsIgnoringDate, scan.TotalInternalSettlements)` |

**日別に数えた合計が、日付を無視した全件と一致することを見る。** 30日の窓の外に行は存在しない(走行が30日で終わる)ので、素の実装では一致する。

**訂正(レビュー3巡目)。この等値 assert は ±1 のずれを構造では守らない。** 当初ここには「どちらの向きのずれでも赤になる」と書いたが、**誤りである**。−1 方向で落ちる行があるのは `DayIndex == 29` に都市内約定が残っているシードだけで、**実測では条件2 の違反日に day 30 を含むシードが 1 / 2 / 3 / 7 の4つある**(= その日の都市内約定は0件)。つまり −1 方向で赤になるのは**シード42 の1本だけ**であり、しかもその1本は「たまたま最終日まで都市内で取引が残っている」というデータに乗っている。**この検出器が測っている病理が進めば、保護は無言で消える。** 等値 assert が構造で守るのは、日別の写像が飛んだり重なったりする形だけである(`dayIndex` が ±1 ずれても起きるのは取りこぼしだけで、**二重計上は起きない**)。

#### 6.4 日付とラベルを構造で留める(レビュー3巡目)

**6.2 では足りない2つを、データに依存しない形で留める。**

- **違反日の番号(`Add(day)` の `day`)は、どの assert にも触れられていない。** `for (int day = 0; day < 30; day++)` への書き換え1つでラベルが 0 始まりに滑り、4本とも緑のまま `_output` の行と doc コメントの基準値が全部1日ずれる。仕様「順序・境界」が「違反日は k(1始まり)で記録する。issue #173 の表の『day 9』『day 30』はこの k である」と書いた対応が、黙って壊れる
- **帳簿の絞り込みに使う `dayIndex` の窓**も同様に、データに依存せず留める必要がある(上の訂正)

| 足すもの | assert(全検出器共通。`FinalDayIndex` / `HouseholdCount` と同じ段に置く) |
| -------- | ---- |
| `FirstDayLabel` / `LastDayLabel` — 走査が違反日として記録しうる最初と最後のラベル | `Assert.Equal(1, scan.FirstDayLabel)` / `Assert.Equal(30, scan.LastDayLabel)` |
| `FirstScannedLedgerDayIndex` / `LastScannedLedgerDayIndex` — 帳簿の絞り込みに実際に使った `DayIndex` の最初と最後 | `Assert.Equal(0, scan.FirstScannedLedgerDayIndex)` / `Assert.Equal(29, scan.LastScannedLedgerDayIndex)` |

**この4本はデータを1行も参照しない** — 帳簿が空でも、経済が直っても、値は 1 / 30 / 0 / 29 である。ラベルと窓のずれはこれで構造的に落ちる。

#### 6.3 doc コメントに足す3行

| # | 書くこと | 出所 |
| - | -------- | ---- |
| 1 | **シードが効いている経路は `WorldGenerator` だけである。** `FullPipeline` の3系統はいずれも `SimContext.OpenRandom` を呼ばないので、`SimScheduler` に渡すシードは結果に影響しない。「5シードで見た」は**世界生成の5通り**を見たという意味である | レビュー2巡目 指摘2 |
| 2 | **issue #173 本文の実測表は条件3 について再現しない。** #173 は「シード1・day 30 に全10戸が0」を記録したが、本実測(2026-09-22)ではシード1 は条件3 の違反0日であり、違反を持つのはシード7 の day 30 だけである。**原因は特定していない** | レビュー1巡目 II-1 |
| 3 | **条件3 の反転側は30日の地平線の端に1日だけぶら下がっている**(シード7 の違反日は day 30 のみ)。地平線を縮めれば「経済が直った」と同じ失敗メッセージが出る。**反転側の失敗メッセージには `scan.Format(seed)` の1行を添える** — 地平線の話か経済の話かを、受け取った側が区別できるようにするため | レビュー1巡目 II-2 / 2巡目 疑い2 |

#### 6.5 「落ちるべき条件」表の訂正 — 実測(`mutator`、2026-09-22、14件)

**上の「落ちるべき条件」表が「この実装ミスで落ちる」と書いた形のうち、4つは落ちない。** `mutator` が当てて確かめた(出所は `mutator` の報告であり、推測ではない)。

| 表の行 | 表の主張 | 実測 | 当てた変異 |
| ------ | -------- | ---- | ---------- |
| #1 | 「1世帯ぶんしか合計していない」で落ちる | **落ちない(緑)** | M-10。部分和が0の日は全体和が0の日を含むので、違反日が増えるだけで核心は緑 |
| #2 | 「読む先を取り違えて常に0」で落ちる | **落ちる(赤)** | M-11(`ProductionRuns` の書き込みを削る)。条件1 の空振り防止が全5シードで赤 |
| #3・#4 | 「向き(`Sale` と `Purchase`)・相手(`==` と `!=`)・品目範囲」の取り違えで落ちる | **3つとも落ちない(緑)** | M-6(向き)/ M-5(相手)/ M-7(品目範囲)。**この3行は撤回する** |
| #4 | 空振り防止が絞り込みの取り違えを捕まえる | **捕まえるのは「全滅型」だけ** | M-2(買い手側の記帳を削る)で赤。母数が0になる形しか見ていない |
| #5 | 「**工房在庫**を見ている」で落ちる | **落ちる(赤)** | M-8。条件3 の反転側(seed 7)の核心が赤 |
| #5 | 「論理積が論理和になっている(M-4)」で落ちる | **落ちるが、M-4 ではない** | M-9(`&&` → `\|\|`)で**正側4シードが赤**。**M-4 はこの取り違えを突かない** — 突いているのは正側の存在である |
| #6 | 品目添字の取り違えで落ちる | **落ちる(赤)** | M-12(3品目を1次産品へ)。条件3 の空振り防止が5件で赤 |

**なぜこうなるかは 6.1 が説明している。** 核心が「違反日が存在する」なので、**母数を減らす方向の取り違えは違反日を増やすだけで緑のまま通る。** #1・#3・#4 が挙げていたのはすべて数え落とし方向の取り違えであり、原理的に見えない。

##### 守られていないと確定したもの

**条件2 の母数を「都市内の約定だけ」にした決定(開発者の決定、2026-09-22)を守る assert は、無い。**

- **M-5**(`tests` 側で `CounterpartyId != ExternalMarketSellerId` を `==` へ = 母数を窓口からの輸入へ丸ごと入れ替える)→ **緑。4本とも赤にならない**
- **M-3**(`src` 側で `ExecuteImport` の `CounterpartyId` を 0 にする = 窓口からの輸入を都市内に見せる)→ **緑。** 仕様が先に裁定したとおり「緑でも検出器の壊れではない」(窓口から都市生産品を買っていない違反日が残るため)が、**結果として、この決定を突く変異は2つとも緑だった**

**フェーズ2 は直さないと決めた。** 理由は引き継ぎメモが持つ。**この事実は doc コメントに書く** — 「守られている」と読ませないために。

##### 6.4 の留め具は効いている

| 変異 | 実測 |
| ---- | ---- |
| M-13(`dayIndex = day - 1` を `day - 2` へ) | **4本すべて・全15インスタンスが赤。** 落ちたのは `Assert.Equal(0, FirstScannedLedgerDayIndex)`(`Last` より先に評価されるため) |
| M-14(走査ループを0始まりへ) | **4本すべて・全15インスタンスが赤。** 落ちたのは `Assert.Equal(30, LastDayLabel)`(`FirstDayLabel` は day 1 の回が来るので 1 のまま) |

**このずれは 6.4 を足す前は4本とも緑で通っていた**(レビュー3巡目の指摘)。**データを1行も参照せずに落ちている。**

## 順序・境界の具体例(規則6)

**`Advance(24)` の1回が1日である。** `Tick.DayIndex` は走った日数を数えるので、**k 回目の直後に `world.Now.DayIndex == k` であり、そのとき終わったばかりの日は `DayIndex == k − 1` である。**

| 回 | `Advance` 後の `world.Now.DayIndex` | 本仕様の呼び方 | 帳簿の照合 |
| -- | ----------------------------------- | -------------- | ---------- |
| 1回目 | 1 | **day 1** | `entry.OccurredAt.DayIndex == 0` |
| 9回目 | 9 | **day 9**(issue #173 が「生産0」と書いた日) | `== 8` |
| 30回目 | 30 | **day 30**(issue #173 が「全10戸が0」と書いた日) | `== 29` |

- **違反日は k(1始まり)で記録する。** issue #173 の表の「day 9」「day 30」はこの k である
- **`ProductionRuns` と `HouseholdInventory` は「今の値」を読む**(その日の終わりの値そのもの)。日付で絞る必要があるのは帳簿だけである
- **`world.Market` は見ない。** 3条件はいずれも市場の売り注文を必要としない

## 落ちるべき条件(テスト)

**この節が完了条件そのものである。** 目標は通すことではなく、**壊したときに落ちること**。

> **この表の「この実装ミスで落ちる」欄には誤りがあった。実測による訂正を下の「6.5」が持つ**(フェーズ2、2026-09-22)。**表を単独で読まないこと。**

| # | テスト | 何を見るか | この実装ミスで落ちる |
| - | ------ | ---------- | -------------------- |
| **1** | `ProductionStopsForAWholeDayWithinThirtyDays`(**核心**。反転側) | 条件1 の違反日が1日以上ある | 生産が全世帯・全日で止まらなくなった(**= 直った。朗報**)。`ProductionRuns` 以外を合計している。1世帯ぶんしか合計していない(M-1) |
| **2** | 同上の空振り防止 `TotalProductionRuns > 0` | 測定が値を見ている | 読む先を取り違えて常に0(`WorkshopInventory` を見る / 走査前に読む) |
| **3** | `InternalSettlementsOfCityGoodsDisappearWithinThirtyDays`(**核心**。反転側) | 条件2 の違反日が1日以上ある | 都市内の約定が毎日立つようになった(**= 直った**)。窓口からの輸入を都市内に数えている(M-3)。品目の範囲が 0〜8 になっている |
| **4** | 同上の空振り防止 `TotalInternalSettlements > 0` | 絞り込みが全行を落としていない | 向き(`Sale` と `Purchase`)・相手(`==` と `!=`)・品目範囲のいずれかの取り違え(M-2) |
| **5** | `AllHouseholdsRunEmptyWithinThirtyDays`(**核心**。反転側) | 条件3 の違反日が1日以上ある | 世帯が空にならなくなった(**= 直った**)。**工房在庫**を見ている(パン屋が売れ残りを抱えている状態が「空でない」に化ける、[#174](https://github.com/stama72/visionary/issues/174))。論理積が論理和になっている(M-4) |
| **6** | 同上の空振り防止 `AllHouseholdsEmptyDays.Count < 30` | 在庫の添字が正しい | 品目添字の取り違え(初期在庫が正の3品目を見ていれば day 1 は必ず空でない) |
| **7** | 全検出器共通 `Assert.Equal(30, scan.FinalDayIndex)` | 30日ぶん進んだ | `ticks: 24` を `ticks: 1` と書いた / ループが30回未満 |
| **8** | 全検出器共通 `Assert.Equal(10, scan.HouseholdCount)` | 母集団が M0 の都市である | `WorldDefinition.M0` 以外を組んだ / 世帯数が変わったのに検出器が気付かない |
| **9** | 正側(`…NeverStops…` / `…NeverDisappear…` / `SomeHouseholdAlways…`)。**実測で該当シードがあるときだけ書く** | その条件が30日すべてで成立する | 条件が破れる日が現れた(= **退行**。こちらは正の向きなので赤が凶報である) |

**既存テストへの影響**: `ITestOutputHelper` のコンストラクタを足す以外、`TradePipelineTests` の既存メソッドには触らない。**実装前の件数を控えてから足し、既存が全件緑のままであることを確認する。**

## 実装の手順

**向きを実測で決めるので、順番が固定である。**

1. `ScanThirtyDays` と `CitySurvivalScan` を書く(まだ検出器は書かない)
2. **5シードぶん走らせて `_output` の行を読む。** 一時的に `[Theory]` 1本(assert を置かず `_output.WriteLine` だけ)を置いて出力を採り、読み終えたら消す。**`src/` には触らない** — これは変異ではない
3. 読んだ結果に従って、条件ごとにシードを反転側 / 正側へ割り振り、検出器を書く
4. `_output` の行を doc コメントへ転記する(日付を添える)
5. `dotnet build -c Release`(警告0)/ `dotnet test -c Release` / `dotnet format --verify-no-changes --severity warn`

**2 の実測が「全シードで違反0」だったら、そこで止まって報告すること。** 経済が既に直っているか、測定が壊れているかのどちらかであり、**どちらであるかの判断は設計の仕事である。**
