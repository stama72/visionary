# W2-18: Household システム(破産中フラグ・職業付け替え)

| 項目     | 内容 |
| -------- | ---- |
| issue    | [#39](https://github.com/stama72/visionary/issues/39) |
| 根拠     | [GDD02b §3.2・§3.3・§4・§4.1・§4.2](../03-gdd/02b-consumption-and-household.md) / [GDD02c §1.4](../03-gdd/02c-price-and-budget.md) / [GDD02d §2.3](../03-gdd/02d-external-market-and-money.md) / [TDD01 §3.2・§3.3](../04-tdd/01-sim-core-and-m0.md) |
| ブランチ | `feat/39-household-flag-and-occupation` |
| worktree | `.claude/worktrees/39-household-system/` |

**このタスクは「順3 `Household` を作ってパイプラインに繋ぐ」ことだけである。** フラグが駆動する分岐(②)は既に実装済みで、繋いでいなかったのは**フラグを立てる側**である。

| 既にあるもの | どこ |
| ------------ | ---- |
| `HouseholdState.IsBankrupt`(0/1 の値域検査つき)・`UnaffordableNecessityCount`・`ProductionRuns`・`Occupation` の `set` | `World/HouseholdState.cs` |
| フラグの入力(2経路で数える) | `TradeSystem.RunOneHouseholdsShopping` 手順3(経路(1))と手順9(経路(2)) |
| ②の値付け側(価格係数‰ を 500 に固定、床は破らない) | `OfferPrice.Calculate` の `isBankrupt == 1` の枝 |
| ②の輸出側(閾在庫 0) | `ExternalMarket.ExportThresholdStock` の `isBankrupt == 1` の枝 |
| `IsBankrupt` / `Occupation` が状態ハッシュに乗ること | `StateHasher` |

## スコープ

**含まない:**

- **②(投げ売り)をここで実装しない。** 値付け側(GDD02c §1.4)と輸出側(GDD02d §2.3)がフラグを読む分岐であり、順3 で `Market` に書くと順5 の一括書き込みと二重になる(GDD02b §4.1)
- **①(嗜好を諦める)** — GDD02c §2.1 の余剰資金の帰結であり、規則として実装しない(GDD02b §4 の囲み)
- **③(掛け売り)・雇用(GDD10)・財政からの貸付・流動資金の補填**
- **`UnaffordableNecessityCount` の数え方を変えない**([#30](https://github.com/stama72/visionary/issues/30) 決定1・2・4 で「どちらも変更しない」で決着済み)。②に留まる世帯は [#38](https://github.com/stama72/visionary/issues/38) の輸出で解消し、仕入だけが切り詰められる世帯は破産中フラグを広げずに [GDD02 §8](../03-gdd/02-economy.md)-2 (b) が別に測る
- **④の付け替えの連日発火を抑える冷却期間を置かない**(GDD02b §4.1「N日連続で困窮したら廃業とすると閾値が1つ増える」)。**付け替えが毎日・往復に起きうる**(A → B → A)ことは承知の上である — 観測として [GDD02 §8](../03-gdd/02-economy.md)-3・-4 と職業分布で読む。④のゲートそのものを見直すかは [#196](https://github.com/stama72/visionary/issues/196) が持つ
- **`Visionary.Sim.Runner` の `hash` コマンドの配線**(W1 の合成システムのまま。順3 を足しても §3.3 の登録順にはならないので、差し替えは W2 の登録順が揃ってから)

**変えない既存コード**(規則8)。単位はファイルではなく規則:

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `TradeSystem` 手順3 の経路(1) — `Purpose == Necessity && Reason == CashCap` のときだけ数える | GDD02b §3.2「ただし必需の現金上限で購入量が 0 になった場合も」/ §5.2「購入量 0 の理由」の固定順 | **一致**(節を読んで突き合わせた) |
| `TradeSystem` 手順9 の経路(2) — `Purpose == Necessity && fundsCap == 0` のときだけ数える | GDD02b §3.2「(2) 資金上限の切り詰めで 0 になった」 | **一致** |
| `TradeSystem` 段5b が毎日 `UnaffordableNecessityCount = 0` で上書きする | GDD02b §3.2 末尾「その日のうちに世帯の状態として記録する」/ §3.3「読む時点でその欄はまだ前日の値である」 | **一致**(順3 がこの欄を書かないことが、前日の値という意味の全部である) |
| `OfferPrice.Calculate` の破産中の枝(価格係数‰ 500 固定・在庫比を評価しない・床を破らない) | GDD02c §1.4 | **一致** |
| `ExternalMarket.ExportThresholdStock` の破産中の枝(閾在庫 0 を相場基準の枝より先に判定) | GDD02d §2.3 最後の箇条書き | **一致** |
| `SellableStock.Of`(工房在庫 − 留保。世帯在庫を見ない) | GDD02c §1.3 | **一致**(④の条件 b がこの関数を通る。下記) |
| `ProductionSystem` が `ProductionRuns` を毎日書く(0 の日も書く) | GDD02a §1 / TDD01 §3.2 | **一致**(④の条件 c が当日値を読めるのはこのため) |
| `HouseholdState.IsBankrupt` の 0/1 以外を拒む setter | GDD02b §3.3 / TDD01 §3.2「状態はすべて int/long」 | **一致** |
| `StateHasher` が `IsBankrupt` と `Occupation` を書く位置 | TDD01 §3.8 | **一致**(状態を1つも足さないので位置は動かない) |
| `WorldGenerator.AssignOccupations` の初期配置(同一区画に同職業を作らない) | GDD02 §2.4 / §4.3 | **一致**(④の区画フィルタは同じ制約を実行時に保つ側であり、生成側は触らない) |
| `OpportunityCost` が世帯の**現在の** `Occupation` を読む | GDD08 §9 | **一致**(付け替えの翌日から新しい職業の機会費用になる。キャッシュは無い) |

## 作るもの

### 1. `src/Visionary.Sim/Systems/HouseholdSystem.cs`(新規)

```csharp
public sealed class HouseholdSystem : ISimSystem
{
    public HouseholdSystem(WorldDefinition definition);   // null を拒む(既存システムと同じ)

    public RandomStream Stream => RandomStream.Household; // = 4。既存の enum の値。振り直さない
    public Cadence Cadence => Cadence.Daily(hour: 0);

    public void Step(World world, SimContext context);
}
```

**乱数を一切引かない。** `Stream` を持つのは `SimScheduler` の登録に一意な識別子が要るからであって `SimContext.OpenRandom` を呼ぶためではない(`ProductionSystem` / `TradeSystem` と同じ。TDD01 §3.1)。

`Step` の本体は**2つの独立したループ**である(GDD02b §4.1「**手順ごとに全世帯を Id 昇順で回す。世帯ごとに 1→2 を回すのではない**」)。`world.Households` は添字 = 世帯 Id なので、先頭から走査すれば Id 昇順である(既存システムと同じ)。

```csharp
// 手順1. 破産中フラグの更新(GDD02b §3.3)。
foreach (var household in world.Households)
{
    household.IsBankrupt = household.UnaffordableNecessityCount > 0 ? 1 : 0;
}

// 手順2. ④の職業付け替え(GDD02b §4.1 のゲート → §4.2 の付け替え先)。
foreach (var household in world.Households)
{
    if (!OccupationReassignment.IsGateOpen(_definition, household)) { continue; }
    if (!OccupationReassignment.TrySelectTarget(_definition, world, household, out var target)) { continue; }

    household.Occupation = target;
}
```

- **代入であって、立てるだけではない。** `if (count > 0) IsBankrupt = 1;` と書くと**降りる枝が消え**、一度立ったフラグが永久に残る(GDD02b §3.3「降りる: 前日、そのような購入が1つも無かった」)
- **`UnaffordableNecessityCount` を読むだけで書かない。** 0 に戻すのは順5 段5b の役目であり、順3 が書くと「前日の値」という意味が壊れる(GDD02b §3.2 末尾)
- **順3 で金は動かない。** `LiquidFunds` を読み書きしない(GDD02b §4.1)
- **手順1 と手順2 を1つのループに畳まない。** 畳むと世帯 Id 0 の付け替えが、まだフラグを更新していない世帯 Id 1 の**担い手世帯数**に効く(下記の走査順の約束が「フラグ更新後の world」を前提にしている)

### 2. `src/Visionary.Sim/Systems/OccupationReassignment.cs`(新規)

**純関数の静的クラス。** `SellableStock` / `SelfConsumption` と同じ置き方にする。

```csharp
public static class OccupationReassignment
{
    /// <summary>その職業を担っている世帯数。<b>自世帯を含めて数える</b>(GDD02b §4.2)。</summary>
    public static int CarrierCount(World world, Occupation occupation);

    /// <summary>④へ進むゲート(GDD02b §4.1 の3条件すべて)。</summary>
    public static bool IsGateOpen(WorldDefinition definition, HouseholdState household);

    /// <summary>
    /// 付け替え先(GDD02b §4.2)。<b>維持するときは false を返し、<paramref name="target"/> には
    /// 現在の職業を置く。</b>
    /// </summary>
    public static bool TrySelectTarget(
        WorldDefinition definition, World world, HouseholdState household, out Occupation target);
}
```

#### `IsGateOpen` — 3条件すべて(GDD02b §4.1)

```
a. household.IsBankrupt == 1
b. SellableStock.Of(definition, household, 出力品目) == 0      ← ②で換金できるものが無い
c. household.ProductionRuns == 0                                ← 立ち直れない
出力品目 = definition.Recipes[(int)household.Occupation].Outputs[0].ItemId
```

- **`&&` である。** 1つ欠けたら付け替えない。b だけでは「その日完売した健全な世帯」が1日の資金不足で廃業する(GDD02b §4.1 ※1)
- **b は販売在庫であって工房在庫ではない**(GDD02b §4.1 ※2)。`WorkshopInventory` を直に読むと「鉄鉱石を抱えたまま木炭を買えない鍛冶」が永久に④へ到達しない。**かつ `SellableStock.Of` を通す** — 生の `WorkshopInventory[出力品目] == 0` と書くと鍛冶の工具の留保 1 個(GDD02c §1.3)を引かないので、工具を 1 個持つ日に b が立たない
- **b が見るのは現在の職業の出力品目1件だけである。** 出力2件以上のレシピは `TradeSystem` のコンストラクタが拒んでいる(GDD02c §2.3 の配分規則が無い)。ここでは `Outputs[0]` を読むだけで、拒む検査を重複させない
- **c は当日の値である。** 順1 `ProductionSystem` が同じ tick で書いた値を順3 が読む(前日値ではない)。**工具切れは c に入らない** — 工具が無くても半分の能力で続く職業では生産量が 0 にならず、鍛冶も GDD02c §1.3 の留保で工具を手放さない(GDD02b §4.1 ※3)

#### `TrySelectTarget` — 付け替え先(GDD02b §4.2)

```
target = household.Occupation

1. CarrierCount(world, household.Occupation) <= 1 なら → false(職業を維持する)
2. 候補を2段で作る。先に区画フィルタつき、空なら外す:
     段A: 職業 Id 昇順に 0 … definition.OccupationCount-1 を走査し、
          自職業を除き、かつ「破産世帯と同じ区画に、その職業の他世帯が居ない」ものだけを候補にする
     段B: 段A が1件も拾えなかったときだけ、区画フィルタを外して同じ走査をする
          (自職業の除外は外さない。区画の重複だけを許容する)
3. 候補のうち CarrierCount が最小のものを採る。同値は職業 Id 昇順
4. 候補が1件も無ければ(= 職業が1つしかない定義)→ false(維持する)
target = 選んだ職業; return true
```

- **`CarrierCount` はその場の `world` を数える。ループの前にスナップショットしてはならない。** 同職業の2戸が同じ日に3条件を満たしたとき、スナップショットだと**両方が離脱して担い手 0 になる** — GDD02b §4.2 が「付け替えを無条件にすると両方の木材加工世帯が離脱して薪の売り手が消え、パンとビールが同時に止まる」として構造的に防ぐと宣言した状態そのものである。その場で数えれば、Id の小さい方が離脱した後で Id の大きい方は `CarrierCount == 1` に当たり、手順1 で維持される
- **したがって付け替えの結果は世帯 Id の走査順に依存する。** これは受け入れる — 資金の切り詰めが走査順に依存すること(GDD02b §3.2)と同種で、ADR-0002 が要求するのは「順を Id 昇順で固定すること」である
- **同値は職業 Id 昇順。** 昇順に走査して `count < bestCount` で更新すれば、先に来た小さい Id が残る。**`<=` にすると大きい Id が勝つ**(ADR-0002 の列挙順規約)
- **`Dictionary` / `HashSet` を使わない。** 候補は `OccupationCount`(M0 で 5)ぶんの走査で足り、職業ごとの担い手数も `world.Households` の線形走査で数えられる(世帯数は最大でも区画数の2倍程度)
- **区画フィルタは GDD02 §2.4 の構造制約(同職業2世帯は別区画)を実行時に保つためである**(GDD02b §4.2)。破らせると [GDD02 §8](../03-gdd/02-economy.md)-4(区画間の価格差)がその品目について偽陰性を返す
- **段B は M0 では立たない。** 1区画の世帯は最大2戸(10戸 / 9区画 + `WorldDefinition` の密度検査)なので、自職業を除いた4職業から同区画の1職業を除いても3件は残る。**それでも実装するのは GDD02b §4.2 が規則として持っているためで、テストは合成世界で当てる**(下記テスト #8)
- **価格や原価から決めない**(GDD02b §4.2)。担い手世帯数だけを見る

#### 付け替えで変えないもの(GDD02b §4.2)

| 変えないもの | 理由 |
| ------------ | ---- |
| `LiquidFunds` | **流動資金は補填しない。** 貨幣量を外から増やすと GDD02d の自動調節と [GDD02 §8](../03-gdd/02-economy.md)-6 の判定が濁る |
| `WorkshopInventory` / `HouseholdInventory` | **工房在庫は残る。** 旧職業の入力は M0 では売りに出す経路が無く滞留する |
| `IsBankrupt` | 手順1 が翌日また前日の購入結果から決める。付け替えで降ろさない |
| `MemberNpcIds` / `HeadNpcId` / `DistrictId` | **世帯は解体しない**(GDD02b §3.3 末尾)。世帯主の職業が変わるだけである |
| `ToolWear` / `PurchaseUnitCostAverage` / `ProductionRuns` / `UnaffordableNecessityCount` | 順3 の責務の外 |

### 3. パイプラインへの配線(規則7)

**順3 は `world` の欄しか読み書きせず、引数で値を受け取らない。** したがって配線の誤りは「登録し忘れ」と「登録位置」の2つに絞られる。

| 配線 | 約束 |
| ---- | ---- |
| **入力の作り方** | `UnaffordableNecessityCount` は順5 段5b が**前日**に書いた値(毎日上書きされる)。`ProductionRuns` は順1 が**当日**に書いた値(0 の日も書く)。`IsBankrupt` は手順1 がこの tick で書いた値を手順2 が読む |
| **呼び出し順** | 順1 `Production` → 順2 `Consumption` → **順3 `Household`** → 順5 `Trade`。**順5 より後に置いてはならない** — ②が同一 tick 内で循環する(GDD02b §3.3 / TDD01 §3.3)。順1 より前に置いてもならない — 条件 c が前日の生産量を読むことになる |
| **登録の場所** | `tests/Visionary.Sim.Tests/Systems/TradePipelineTests.FullPipeline` に `new HouseholdSystem(definition)` を `ConsumptionSystem` と `TradeSystem` の間へ挿す。**M0 の登録順を持つ配線は現時点でここだけである**(`Visionary.Sim.Runner` の `hash` は W1 の合成システムのまま) |
| **添字・単位の約束** | `CarrierCount` は**自世帯を含む**。`IsGateOpen` の出力品目は `Outputs[0].ItemId`。`OccupationCount` は `definition.Recipes.Length` であり `Occupation` enum の要素数ではない(定義が持つレシピの数で走査する) |

**`FullPipeline` に順3 を挿すと経済の形が変わる。** フラグが立った売り手の提示価格が半値へ、輸出の閾在庫が 0 へ分岐し、④が発火すれば職業分布も動く。既存の固定検出器が赤になったときの扱いを**先に決めておく**:

| 赤の種類 | 扱い |
| -------- | ---- |
| **不変量**(帳簿と流動資金の突合 / 貨幣と財の保存 / 決定論のハッシュ一致) | **止まって報告する。** 仕様の欠陥か実装の誤りであり、値の追随で消してはならない |
| **実測値・自然発生する(世帯, 日)・件数の閾値** | **値の追随でよい。** `UnaffordableNecessityCountsOnlyTheFundsShortfall` の doc コメントが確立した手順(60日を走査して 0→1→0 のきれいな遷移を持つ最初の組を選び直す)に従い、remarks に「#39 追随」として理由と新しい実測値を書く |
| **検出器の前提が崩れた**(例: ④の付け替えで鍛冶が 1 戸になり `Assert.Equal(2, smithHouseholdIds.Length)` の母集団が変わる / 工具の売り注文の延べ件数が母集団ごと減る) | **止まって報告する。** 前提の置き直しは設計判断であり、implementer もフェーズ2 のメインも決めない |

> **この区分は1度発火した**(2026-09-23、`IMPL-BLOCKED`)。**3本の検出器の置き直しはフェーズ1 が裁定して下記「5. 既存検出器の置き直し」に書いた。** 表はそのまま生きている — **そこに挙がっていない検出器が第3区分で赤になったら、また止まって報告する。**

### 4. 旧規則の doc コメントを直す([#112](https://github.com/stama72/visionary/issues/112) 表C の申し送り)

②の**旧規則**(「値付けで原価下限を 500‰ へ下げる」)を書いた散文が3件残っている。**現行の規則は GDD02c §1.4「価格係数‰ を 500 に固定する(床は破らない)」である** — 床が原価から外部買値に変わったので、下げる対象そのものが無くなった。

| 箇所 | 直す文 |
| ---- | ------ |
| `src/Visionary.Sim/Determinism/StateHasher.cs`(世帯の区分のコメント) | 「②(値付けで原価下限を 500‰ へ下げる)」→ 現行の規則(GDD02c §1.4)へ |
| `src/Visionary.Sim/World/HouseholdState.cs`(`IsBankrupt` の remarks) | 同上 |
| `tests/Visionary.Sim.Tests/Determinism/StateHasherTests.cs`(`HashChangesWhenBankruptFlagChanges` の remarks) | 同上。「投げ売り中の世帯」という言い方は残してよい(②の名前であって旧規則ではない) |

**引用先の節番号は #112 が既に現行へ向けてある。** 直すのは散文だけである。

### 5. 既存検出器の置き直し(フェーズ1 の裁定。2026-09-23)

**1回目のフェーズ2 は、`FullPipeline` に順3 を挿したことで既存の固定検出器3本(8インスタンス)が赤になり `IMPL-BLOCKED` で止まった。** 仕様どおりの停止である(上の第3区分)。**以下はフェーズ1 の裁定であり、implementer はこのとおりに置き直す。**

#### 何が起きていたか(実測。2026-09-23、`HEAD` = `a38b5b2`)

**④は発火し、職業が入れ替わっている。** シード7 では day 33 の時点で、**day 0 の鍛冶2戸(household2・household9)がどちらも鍛冶を降りており、唯一の鍛冶は household8 である**(工具在庫 1 = `SellableStock` の留保ぶんだけ)。経路は GDD02b §4.2 の規則どおりで、担い手 0 は一度も起きていない:

```
鍛冶2戸 → household2 が④で降りる(担い手 2 → 1)
         → 別の破産世帯 household8 が「担い手最少」の鍛冶へ④で入る(1 → 2)
         → household9 が④で降りる(2 → 1)     ← 「最後の1世帯は付け替えない」が household8 を守る
```

**したがって「担い手 0 を防ぐ」は守られており、「担い手 2 を保つ」は初めから誰も保証していない**(GDD02b §4.2「**ただし防いでいるのは担い手 0 だけであり、2 → 1 への減少は防がない**」)。赤になった3本は、いずれも**職業が不変だった世界で「担い手 2 が続くこと」を暗黙の母集団にしていた。**

#### 裁定 D-A: `OwnOutputIsNeverHoardedWhileTheHouseholdGoesWithout`

**核心(抱え込み 0 件)はそのまま。壊れたのは空振り防止の固定値である。**

| 現在 | 置き直す先 |
| ---- | ---------- |
| `Assert.Equal(180, scan.ScannedSelfSuppliableEntryCount)`(= 自家供給6戸 × 30日) | **(1)** day 1〜30 の各日について、その日の観測件数(`SelfSuppliableObservations.Count(o => o.Day == day)`)が **3 以上**。**(2)** 延べ観測件数が **90 以上**(= 3 × 30) |
| `Assert.Equal(6, finalDayObservations.Count)` | `Assert.True(finalDayObservations.Count >= 3, …)`。この断定の役目は「day のラベルがずれる変異で下のループが 0 周にならないこと」(既存コメント)であり、3 以上で果たせる |
| `Assert.Equal(6, scan.SelfSuppliableHouseholdCount)` | **そのまま**。これは **day 0** の値(`ScanThirtyDays` が日ループの前に数えている)であり、`WorldGenerator` の初期配置の確認である |

**下限 3 は定数ではなく、GDD02b §4.2 の構造的保証の翻訳である。** 自家供給できる職業は「出力品目が世帯の消費財である職業」= パン屋(パン)・木材加工(薪)・醸造(ビール)の**3職業**で、そのどれも「最後の1世帯は付け替えない」により担い手 0 にならない。**したがって毎日3戸以上が構造的に保証される。** 180 のほうは「6戸が6戸のまま続くこと」を固定しており、④が職業分布を動かすと決めた GDD02b §4.2 の下では**一世代前の仕様を凍結している。**

- **実測を remarks へ転記すること**(2026-09-23、`a38b5b2`): 延べ観測件数は seed1=168 / seed2=175 / seed3=169 / seed7=**189** / seed42=**188**。**上にも下にも動く** — 鍛冶や水車小屋番が④で自家供給できる職業へ入れば増える。**「母集団が縮んだ」と書かないこと**(1回目のフェーズ2 の引き継ぎメモは「180 → 175〜」と書いたが、2シードは増えている)
- 置き直した後の各日の最小件数も測って remarks に書く(下限 3 との距離が分かるようにする)

#### 裁定 D-B: `SmithNeverRunsOutOfToolsOverSixtyDays`

**核心(day 0 の鍛冶2戸の工房在庫[工具] ≥ 1)はそのまま。全5シードで緑であり、W2-14 の変異 M-1(留保を消す)で全5シード赤になる実測も生きている。**

**空振り防止 (iii)「60日間の工具の売り注文の延べ件数 ≥ 60」を落とす。** 理由:

- **(iii) は核心と母集団が違う。** 核心の母集団は **day 0 の鍛冶2戸**だが、(iii) が数えるのは**都市全体の工具の売り注文**である。④が入ると売り注文を出しているのは別の世帯になりうる(シード7 day 33 の household8)。**同じテストの中で、核心が主張する集合と空振り防止が数える集合がずれた**
- **核心が空振りでないことは、既に別の機械が担保している。** (i) 鍛冶が2戸存在する(day 0)/ (ii) 60日ぶん進んだ / **M-1 の変異実測(全5シード赤)**。(iii) はそれらに対する冗長な代理指標であり、**延べ件数 60 は「鍛冶2戸が毎日売り注文を出せる経済」の産物**である
- **落とすのは狭い側に倒す方向である**([03-corrections](../process/03-corrections.md) 規則1)。主張を増やしていない

**代わりに、都市側の構造は下記 D-C が持つ。** (iii) の延べ件数は**断定から外すが、失敗メッセージの診断値としては残す**(実測 seed7=51)。

#### 裁定 D-C: `ToolOffersNeverDisappearOverSixtyDays` → `EveryOccupationKeepsAtLeastOneCarrierOverSixtyDays`

**日次の断定「工具の売り注文が毎日1件以上」は、④の下では成り立たず、成り立たせてもならない。** ④で鍛冶に入ったばかりの世帯は入力(鉄鉱石・木炭)を持たないので生産できず、工具在庫は留保の1個だけで販売在庫 0 になる — GDD02b §4.1 が「②が発火した翌日にはほぼ必ず販売在庫が尽きる」と書いた状態そのものである。**日次下限を維持する手は冷却期間か④の抑制しかなく、どちらも GDD02b §4.1 が却下済みの調整軸である。**

**このテスト自身の remarks が、落としてよい根拠を既に持っている**:「**本テストの断定は、どの変異(M-1〜M-6)でも担保されていない**」。守っていたのは「鍛冶の生産が完全に止まったこと」の検出であって、留保の核心は D-B のテストが持つ。

**置き直す先**(メソッド名も変える。日次の主語が変わるので、名前を残すと嘘になる):

| 項目 | 内容 |
| ---- | ---- |
| 断定(新) | day 1〜60 の各日の終わりに、**5職業すべてについて `OccupationReassignment.CarrierCount(world, occupation) >= 1`**。破れた日・職業・その日の職業分布を失敗メッセージに出す |
| 空振り防止 | `Assert.Equal(60, world.Now.DayIndex)` はそのまま。`Assert.Equal(2, smithHouseholdCount)`(day 0)は「**day 0 に5職業すべてが2戸**」へ広げる(`WorldGenerator` の初期配置の確認) |
| 診断値 | 60日間の工具の売り注文の延べ件数と、0件だった日を失敗メッセージに残す(断定はしない) |
| シード | 1/2/3/7/42 と 60日をそのまま使う |

**これは緩めた置き直しではない。** GDD02b §4.2 の「**最後の1世帯は付け替えない**が供給の消滅を構造的に防ぐ」を、**テストが初めて機械で守る**ことになる。品目ではなく職業を主語にするので、工具に限らず5品目すべての供給消滅を1本で拾う。

- **旧 remarks のうち、W2-14 の変異 M-1 の実測(シード7のみ赤・期待の向きが逆になる理屈)と「どの変異でも担保されていない」の段落は残す** — 日次下限を落としてよい根拠そのものである。**「置き直した」ことと、その理由(④の発火。実測は上記)を追記する**
- 旧名は `tests/.../TradePipelineTests.cs` に2箇所(宣言と `SmithNeverRunsOutOfToolsOverSixtyDays` の remarks の `<see cref>`)ある。**両方直す。** `docs/tasks/W2-14-sellable-stock-reserve.md` の参照は**直さない**(凍結済みのタスク仕様。[docs/tasks/README.md](README.md))

#### この裁定で `mutator` に足すもの

**核心 C-2(最後の1世帯の保護を落とす)の報告に、`EveryOccupationKeepsAtLeastOneCarrierOverSixtyDays` の結果も含める。** ④が実際に担い手を 2 → 1 へ動かしている経済なので、保護を落とせばどこかの職業が 0 に落ちる見込みである。**ただし C-2 の受け入れ条件は引き続き「テスト #6 が落ちること」である** — 60日走行の結果は測定値として転記し、受け入れの根拠にはしない(どんな摂動でも赤になりうる検出器の赤を、特定の契約が壊れた証拠として読まない)。

## 落ちるべき条件(テスト)

**この節が完了条件そのもの。** 目標は「通ること」ではなく「**壊したときに落ちること**」である。

新規ファイル: `tests/Visionary.Sim.Tests/Systems/HouseholdSystemTests.cs`(#1〜#12)。#13 は `TradePipelineTests` へ足す。

**世界は手組みでよい** — `EconomySystemTestFixtures.BuildDefinition` と `TradeSystemTests` の `AddHousehold` 相当の組み立てを使う。順3 は価格も観測も読まないので `WorldGenerator` を通す必要が無く、④のゲートの3条件と担い手世帯数は手で置くほうが判別力が高い(#10 のように同職業2戸を同じ日に揃える形は、生成された世界を待っていては測れない)。**#13 だけは `FullPipeline(definition)` を通す**(配線の断定がそこにしか無い)。

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心(当てる変異 / 期待) |
| - | ------ | -------- | -------------------- | ------------------------- |
| 1 | `BankruptFlagRisesFromYesterdaysShortfall` | `UnaffordableNecessityCount = 1` の世帯を1日回すと `IsBankrupt == 1` | フラグを立てない / 判定を `> 1` にする / `Step` をパイプラインに繋がない | |
| 2 | `BankruptFlagFallsWhenYesterdayHadNoShortfall` | `IsBankrupt = 1` かつ `UnaffordableNecessityCount = 0` の世帯を1日回すと `IsBankrupt == 0` | `if (count > 0) IsBankrupt = 1;` と書いて**降りる枝を落とす**(一度立ったフラグが永久に残り、②が恒久化する) | |
| 3 | `HouseholdSystemMovesNoMoneyAndDoesNotResetTheCount` | 1日回しても `LiquidFunds` と `UnaffordableNecessityCount` が入力のまま不変 | 順3 が仕入を払う(GDD02b §4.1 の旧文言のまま実装する)/ 順3 が件数を 0 に戻す(順5 の役目を奪い、「前日の値」が壊れる) | |
| 4 | `OccupationChangesOnlyWhenAllThreeConditionsHold` | 4通り(3条件すべて / a 欠け / b 欠け / c 欠け)で、揃った1通りだけ `Occupation` が変わる | ゲートを `\|\|` で書く / 条件を2つしか見ない(完売した健全な世帯が1日の資金不足で廃業する) | **【核心 C-1】** `OccupationReassignment.IsGateOpen` から条件 c(`household.ProductionRuns == 0`)を落とす。**期待 赤** |
| 5 | `GateReadsSellableStockNotWorkshopInventory` | (i) 出力品目の販売在庫 0 だが入力品目の工房在庫が正の鍛冶 → 付け替わる。(ii) 工具を 1 個だけ持つ鍛冶(留保 1 で販売在庫 0)→ 付け替わる | b を `WorkshopInventory` の総和や入力在庫で見る(鉄鉱石を抱えた鍛冶が永久に④へ到達しない)/ `SellableStock.Of` を通さず生の `WorkshopInventory[出力品目] == 0` と書く(留保を引かないので (ii) で b が立たない) | |
| 6 | `LastCarrierOfAnOccupationIsNeverReassigned` | その職業の担い手が自世帯だけ、かつ3条件成立 → `Occupation` 不変 | 無条件に付け替える(GDD02b §4.2 が構造的に防ぐと宣言した「担い手 0」が起きる) | **【核心 C-2】** `TrySelectTarget` の `CarrierCount(...) <= 1` の早期 return を落とす。**期待 赤** |
| 7 | `TargetAvoidsAnOccupationAlreadyPresentInTheSameDistrict` | 担い手最少の職業 Y が破産世帯と同じ区画に既に居るとき、Y を選ばず次に薄い職業を選ぶ | 区画フィルタを掛けない(GDD02 §2.4 が成立しないと言った配置を実行時に作る) | |
| 8 | `DistrictOverlapIsAllowedWhenNoCandidateRemains` | 自職業以外の全職業が破産世帯と同じ区画に居る合成世界で、それでも付け替わる(段B) | 段B を実装しない(候補 0 で維持になり、品目の供給停止より区画の重複を優先してしまう)/ 段B で自職業の除外まで外す(自職業が担い手最少の日に `argmin` が自職業を返し、付け替えが起きない) | |
| 9 | `TieOnCarrierCountIsBrokenByOccupationId` | 担い手数が同じ職業が2つ以上あるとき、職業 Id の小さい方を選ぶ | 更新条件を `<=` にする / LINQ の `OrderBy` などで列挙順に委ねる(ADR-0002) | |
| 10 | `CarrierCountIsCountedLiveSoTheSecondCarrierStays` | 同職業の2戸がともに3条件を満たす日、Id の小さい方だけが付け替わり、大きい方は維持される(その職業の担い手は 1 で残る) | **担い手世帯数をループの前にスナップショットする**(両方が離脱して薪の売り手が消える)/ 手順1 と手順2 を1つのループに畳む | **【核心 C-3】** `HouseholdSystem.Step` の手順2 で、`OccupationReassignment.CarrierCount` を呼ぶ代わりにループ前に全職業の担い手数を配列へ数えて使い回す。**期待 赤**(#10 が落ちる。#6 は落ちない — 単独の世帯では両者が同値なので、この変異を捕まえるのは #10 だけである) |
| 11 | `ReassignmentTouchesNothingButTheOccupation` | 付け替えの前後で `LiquidFunds` / `WorkshopInventory` / `HouseholdInventory` / `IsBankrupt` / `MemberNpcIds` / `DistrictId` が不変 | 流動資金を補填する(GDD02b §4.2)/ 工房在庫を捨てる / 付け替えでフラグを降ろす | |
| 12 | `BankruptFlagReachesTheOfferPriceAndTheExportThreshold`(`HouseholdSystemTests`) | 手組みの世界に **`new ISimSystem[] { new HouseholdSystem(d), new TradeSystem(d) }` の順**で登録する。1日目に必需の資金不足を起こし、2日目の順3 でフラグが立ち、**同じ2日目の順5** でその売り手の提示価格が `ApplyPermille(相場基準, 500)` に落ち、販売在庫が全量窓口へ出る(閾在庫 0)。**売り手に `相場基準 > 2 × 床` を仕込むこと** — 相場が床の2倍を下回ると破産中でも提示価格は床のままで、この分岐は何も変えない(GDD02c §1.4)。売り手の販売在庫は正にしておく(④のゲートの b が閉じるので職業は動かない) | 順5 の**後**に登録する(段1 が前日のフラグを読み、提示価格が半値にならない)/ 順3 を1日1回でない `Cadence` で登録する。**フラグを直に `IsBankrupt = 1` と置く既存テストでは落ちない** — ②の分岐そのものは既存テストが押さえており、ここが押さえるのは**順3 からフラグが届く経路**だけである | |
| 13 | `BankruptFlagRisesAfterFirewoodCrowdsOutBread`(`TradePipelineTests`。**`FullPipeline` を使う**) | 必需2品目(薪 Id 5 → パン Id 6 の走査順)で、**薪の世帯在庫が `2 × 目標在庫` に達し**、同じ日にパンが `fundsCap == 0` で 0 個に切られ(経路(2))、**翌日の順3 で `IsBankrupt == 1`** になる | `HouseholdSystem` を `FullPipeline` に挿し忘れる(フラグが永久に 0 のまま)/ フラグの入力を経路(1)だけにする / 「在庫が尽きた世帯だけが破産中である」と読んだ実装(この世帯の薪は目標の2倍ある) | |

### #13 の組み立て(数値は仕様ではない。満たすべき条件だけを置く)

[#85](https://github.com/stama72/visionary/issues/85) の申し送り([#101](https://github.com/stama72/visionary/issues/101) の棚卸し経由)。GDD02b §3.2 の経路(2)は「**冬の薪を目標の2倍まで買った後にパンが資金上限で 0 に切られる**」を名指ししている。**#97 は件数を2経路で数えるところまでを作り、フラグは立てていない。**

`TradeSystemTests.NecessityShortfallIsCountedOnBothPaths` の経路(2)の組み立て(必需2品目が同じ流動資金を奪い合う世界)を出発点にし、**薪が上側 clamp まで買えるようにする**。満たすべき条件:

```
到達在庫 = clamp( 3T − CeilDiv(2T × P, B) , 0 , 2T )  が 2T になるのは  P ≤ B ÷ 2
    T = 薪の目標在庫、P = 薪の実効価格、B = 薪の基礎値(GDD02b §5.2)
ゲートが開いていること:   P ≤ min( ApplyPermille(B, 在庫圧力‰) , 現金上限 )      (GDD02c §2.1)
    在庫が 0 なら在庫圧力‰ = 1500 なので第 1 項は 1.5B、P ≤ B÷2 の下では自動的に開く
    必需の現金上限 = FloorDiv( 流動資金 , max(1日消費量(世帯合計), 1) )
薪を買い切った後のパン: fundsCap = FloorDiv(残りの流動資金, パンの実効価格) == 0
    段4 の現金上限は買い物の**前**に一括で作られる(TDD01 §3.3 段4)ので、パンの行は
    ゲートを通ってから手順9 で切られる = 経路(2)
```

`P ≤ B ÷ 2` を作る手は2つある。**どちらでもよい**(この選択は結果に効かない):

- **買い手に高めの相場観測を仕込む** — 基礎値が相場項 `ApplyPermille(相場基準, 許容乖離‰)` になるので、売り手の提示価格(床に張り付く)の 2 倍以上の相場基準を置けば `P ≤ B ÷ 2` が立つ
- **信用割引を効かせる** — `trustDiscountPermille` を 500 にし、`TrustLedger` に高い信用を置くと実効価格が提示価格の半値になる

**断定に `薪の世帯在庫 == 2 × 目標在庫` を含めること。** これが無いと、「在庫が尽きた世帯だけが数えられる」実装でもテストが緑になる — GDD02b §3.3 の囲みが名指しした「**フラグが見ているのは在庫ではなく現金である**」を押さえているのはこの1行である。

### 変異の実測について

**「核心」印の3件(C-1・C-2・C-3)は [`mutator`](../../.claude/agents/mutator.md) が使い捨て worktree で当てて測る。** 選ぶのは本仕様、測るのは `mutator`、転記するのは implementer である(ADR-0013)。**測るのはレビューの巡が閉じた後、コミット済みの `HEAD` に対して一度でよい。**

**C-3 の受け入れ条件は「赤」ではなく「#10 が落ちること」である。** 他のテストが道連れで落ちても、それは担い手世帯数の数え方を守った証拠にならない。

## 編集してよい文書

- **なし(コードとテストのみ)。** GDD02b §4.1 と TDD01 §3.3 の訂正はフェーズ1 が先行コミット `e95da21` で済ませた。**フェーズ2 が `docs/03-gdd/` `docs/04-tdd/` `docs/adr/` に差分を作ると `SPEC-OUTSIDE` で止まる**
- 上記「旧規則の doc コメントを直す」の3件は `src/` と `tests/` の中の散文であり、この制限に当たらない

## このタスクで特に効く規約

ADR-0002 のうち、このタスクで踏みやすいものだけを挙げる(`BannedSymbols.txt` と `DeterminismConventionTests` が止めるものは書かない)。

- **列挙順**: 世帯は `world.Households` を先頭から(添字 = Id)、職業は `for (int occupationId = 0; occupationId < definition.OccupationCount; occupationId++)` で走査する。LINQ の `Where` / `OrderBy` / `GroupBy` の列挙結果をロジックに使わない
- **同値の決着**: `argmin` の同値は職業 Id 昇順。昇順走査 + `<` で更新する(`<=` は逆になる)
- **除算は現れない。** 担い手世帯数は整数の数え上げであり、比率も平均も取らない。**取りたくなったらそれは「価格や原価から決めない」(GDD02b §4.2)に反している**
- **`bool` を状態にしない。** `IsBankrupt` は int 0/1 で、値域は setter が守る。比較は `== 1` で書く(`!= 0` と書くと、値域検査を通らない経路が生まれた日に実装間で食い違う)

## 完了条件

- [ ] 「落ちるべき条件」のテスト #1〜#13 が全て緑
- [ ] **「核心」印の変異 C-1・C-2・C-3 を `mutator` が実測し(レビューの巡が閉じた後)、結果を doc コメントへ転記した**
- [ ] `FullPipeline` に順3 が入り、既存の固定検出器の赤を上の表の3分類で仕分けた(追随したものは remarks に理由と新しい実測値を書いた)
- [ ] **裁定 D-A・D-B・D-C のとおりに3本の検出器を置き直し、実測値を remarks へ転記した**
- [ ] 旧規則の doc コメント3件を直した
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
