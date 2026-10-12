# W5-02: Trade を朝と夕方に分け、外出の計画を World に残し、時間割を置く

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#257](https://github.com/stama72/visionary/issues/257)              |
| 根拠     | [TDD01 §3.1・§3.2・§3.3「順5 の段」・§3.8・§3.9](../04-tdd/01-sim-core-and-m0.md) / [GDD05 §1.5](../03-gdd/05-ui-ux.md) / [GDD02b §8.1](../03-gdd/02b-consumption-and-household.md)(差し引き) / [GDD06 §3](../03-gdd/06-trade-and-negotiation.md) / [GDD02d §2.3](../03-gdd/02d-external-market-and-money.md) / [#249](https://github.com/stama72/visionary/issues/249) 決定1〜7 |
| ブランチ | `feat/257-trade-schedule`                                            |
| worktree | `.claude/worktrees/257-trade-schedule/`(パイプラインもここで走る) |

**仕様は TDD01 §3.3「順5 の段」の表と §3.9 が持つ。** 本書は「どう作るか」だけを書く。やることは4つ。

| # | 何を | 正 |
| - | ---- | -- |
| 1 | `TradeSystem` を朝0時の `TradePlanningSystem`(段1〜4・5a)と夕方18時の `TradeSystem`(段4・5b・6・7・8)に割る。`MetricsSystem` も18時へ | TDD01 §3.3 |
| 2 | 段1 の控え(当日の値付けの控え)と段5a の計画(当日の外出の計画)を `HouseholdState` に置き、ハッシュに含める | TDD01 §3.2・§3.8 |
| 3 | 段8: 段5b で約定した購入の個数を、用途を合わせて Need から引く | GDD02b §8.1 / TDD01 §3.3 段8 |
| 4 | 時間割の純関数 `Timetable.Of` | GDD05 §1.5 / TDD01 §3.9 |

**閉じる条件の「経済の軌跡が分割前と一致する」は、`vsim run` の出力(CSV と `summary.json`)のバイト一致で測る**(下の「6. 分割前との一致の実測」)。状態ハッシュは比べない — 新しい欄が増え、約定と観測の tick が 0時 → 18時に動くので**必ず変わる**(TDD01 §3.8「ゴールデン値を固定しない」)。

## フェーズ1 で決めたこと

### 夕方は控えと計画を World から読み、朝の中間値を持ち越さない

**`TradePlanningSystem` と `TradeSystem` の間で、World 以外の経路で値を渡さない。** 片方のインスタンスにフィールドで持たせる・静的に共有する、はどちらも禁止。TDD01 §3.8 が「当日の外出の計画」と「当日の値付けの控え」をハッシュに含めたのは、**夕方の段がそれを World から読む**からである。インスタンス経由で渡すと、ハッシュが同じで夕方の振る舞いが違う世界が作れる。

- **夕方の段6 の委託先の労働力係数‰ は、計画に書いた委託先 NpcId から引き直す**: `definition.LaborPermilleByRank[(int)world.Npcs[household.ErrandDelegateNpcId].Rank]`。`OpportunityCost.SelectErrandDelegate` を夕方に呼び直さない(現行 `RunOneHouseholdsExport` の doc コメントの理由 — 呼び直す形は「朝と夕方で委託先が違う日がありうる」という契約を静かに作る)
- **夕方の段6 の往復時間の合計は、計画の区画列から計算し直す**: `Σ Errand.TravelHours(household.DistrictId, d, definition.TravelHoursPerDistrict)`(d は `PlannedErrandDistrictIds` の各要素)。`ErrandPlanner.Plan` の `TotalTravelHours` と同じ式である(`ErrandPlanner.cs` 152〜153行)。World に欄を足さない — TDD01 §3.2 の計画の欄にない
- **Metrics の需要の3欄(`DemandLines` / `DemandLinesWithoutKnownPrice` / `InputBlockedByFunds` の段4 経路)は夕方の段4 だけが書く。** 朝の段4 は書かない。`InputBlockedByFunds` は 1 を代入するだけで 0 に戻さないので、朝も書くと朝の需要の 1 が夕方まで残る。プレイヤー不在では朝と夕方の需要は一致するので、どちらで書いても値は同じだが、**プレイヤーが居る日に意味を持つのは買い物に使った夕方の需要である**
- **`world.Metrics.BeginDay()` は `TradePlanningSystem.Step` の先頭で呼ぶ**(順5朝 が当日の Metrics の最初の書き手になる。段1 が `SellerHasNoReference` / `SellerCoefficientCapped` を書く)

### 計画の区画列に中心を足さない(段6)

**段6 は `PlannedErrandDistrictIds` を書き換えない。** 夕方の `Step` の中で世帯ごとに**複製した** `List<int>` を作り、段6 はその複製に中心を足し、段7 は複製を読む。World の列に中心を足すと、時間割が中心行きを2回数える(列の中心行き + `ExportErrandTaken` のぶん)([#249](https://github.com/stama72/visionary/issues/249) 全般アドバイザー)。**別の外出で中心へ持ち込んだ世帯だけ `ExportErrandTaken = 1`** にする(現行の `visitedDistrictIds.Add(District.ExternalMarketDistrictId)` を通る枝と同じ条件)。

### 時刻の定数は `WorldDefinition` の `const` に置く

```csharp
public const int WorkdayStartHour = 6;   // 単位: 時。家業の始まり(GDD05 §1.5)。プレイヤーの取引の時間帯の始まりも読む(GDD06 §6.4)
public const int EveningTradeHour = 18;  // 単位: 時。順5夕〜10 の実行時刻で、委託先が外出から帰り着く時刻(TDD01 §3.3・GDD05 §1.5)
```

**コンストラクタの引数にしない。** `Cadence` はシステムのプロパティで、`MetricsSystem` / `TradeSystem` がこの値を読む。引数にすると、定義ごとに夕方のシステムの実行時刻が変わり、M0 の比較実験の前提(全条件で同じ時刻に約定する)が設定ファイルで壊せるようになる。W5 で触って直すときは定数を書き換える(GDD05 §1.5「初期値であり、W5 で触って直す」)。

**`WorldDefinition` のコンストラクタは `disposableHours > EveningTradeHour − WorkdayStartHour` を拒む**(`ArgumentOutOfRangeException`)。GDD05 §1.5 が「往復時間の合計は T 以下なので、出発は6時より前にならない」と書いている前提を、ここで機械に持たせる。既存のテストの `disposableHours` は 0・1・7・8・12 だけで、拒まれるのは 0(既存の「1以上」の検査で既に落ちている)だけである。

### 時間割は「同じ日」の tick だけを受け付ける

`Timetable.Of` は `tick.DayIndex != world.Now.DayIndex` なら `ArgumentOutOfRangeException` を投げる。World が持つ計画は当日の1日分だけで、過去・未来の日の時間割は World から復元できない。

- **残る穴**: `world.Now` がその日の0時ちょうど(0時をまだ処理していない)とき、同じ日の6〜17時を問うと**前日の計画**で答える。Query(#263)は `world.Now` を渡すので、0〜5時しか問わず、計画を読まない(TDD01 §3.9「0〜5時は計画を読まない」)。未来の時刻を先読みする呼び出し側を作るときに塞ぐ
- **残る穴(上位文書)**: `ExportErrandTaken` は18時の段6 が書く。**当日の6〜17時に時間割を問うた時点では常に 0 なので、輸出の別の外出は日中の時間割に現れない**(18時以降に同じ日の6〜17時を問えば現れる)。GDD05 §1.5「輸出のための中心行きが別の外出になる日は、買い物の外出の後に行う」は、そのままでは描写されない。本タスクは文書どおり実装し、上位文書の側の扱いは [#268](https://github.com/stama72/visionary/issues/268) へ送る

却下した案は引き継ぎメモ(`W5-02-trade-schedule.handoff.md`)。

## スコープ

**含む:**

- `TradePlanningSystem` の新設と `TradeSystem` の書き換え(上の表 #1)、`RandomStream.TradePlanning = 14`
- `HouseholdState` の欄(計画3つ・控え4つ)とハッシュへの追加
- 段8(Need の差し引き)
- `Timetable` / `TimetableEntry` / `TimetableActivity`
- `WorldDefinition` の2定数とコンストラクタの検査
- `Program.BuildPipeline`(Runner)と `SummaryJsonTests` の写しの登録順
- 分割で壊れた既存テストの追随(下の「7. 既存テストの追随」の手順に従う)

**含まない:**

- プレイヤーから買ったときの差し引き([#253](https://github.com/stama72/visionary/issues/253))
- `SimHost` / `Query` への配線([#263](https://github.com/stama72/visionary/issues/263))。Godot 側の描画([#47](https://github.com/stama72/visionary/issues/47))
- 輸出の別の外出を日中の時間割に出すこと([#268](https://github.com/stama72/visionary/issues/268))

### 変えない既存コード(規則8。[02-task-spec](../process/02-task-spec.md)「変えないと宣言する既存コード」)

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `ErrandPlanner.Plan` — 行き先は自区画を含まず重複なし、選んだ順に並ぶ | GDD06 §3 | 一致(`ErrandPlanner.cs` 94〜97行・152行。時間割が「行き先 ≠ 自区画、片道 ≥ 1時間」を前提にできる根拠) |
| `ErrandPlanner.Plan` — 往復時間の合計 ≤ T | GDD06 §3 / GDD05 §1.5 | 一致(同 102〜105行) |
| `NeedGenerationSystem.ResolveId` — 前日の一覧に同じ (世帯, 理由, 品目) があれば Id を引き継ぎ、無ければ新しい Id | GDD02b §8.1「差し引きで消えた組は、失効した組と同じに扱う」「数量が残った組は、翌日も立てば Id を引き継ぐ」 | 一致(`NeedGenerationSystem.cs` 78〜93行。差し引きで `world.Needs` から消せば、翌日は新しい Id になる) |
| `NeedGenerationSystem` の順4 の条件の判定 | GDD02b §8.1「順4 の条件の判定は変えない」 | 未確認(本タスクは触らない) |
| `BuyerDemand.Build` — 需要の作り方 | GDD02b §5 / GDD02c §2.3 | 未確認(本タスクは夕方に同じ関数を同じ引数の形で呼ぶだけ) |
| `StoreChoice.TrySelect` / `TradeSettlement.*` / `Observations.*` / `ExternalMarket.*` | GDD06 §3・§4 / GDD02d §2.3 | 未確認(呼び出す時刻が 0時 → 18時に動くだけ。`OccurredAt` / `ObservedAt` は `world.Now` を書くので 18時の tick になる。鮮度・前日判定はすべて `DayIndex` の差で、時刻を見ない — `grep` で `DayIndex` 以外の Tick 演算が無いことを確認した。例外は `MarketReference.cs` 55行の同一日内の大小比較で、全観測が同じ 18時に生まれるので順序は変わらない) |

## 作るもの

### 1. `RandomStream.TradePlanning = 14`

TDD01 §3.3 の囲み「`TradePlanning` は **14** を取る」。乱数は引かない(`Trade` / `Metrics` と同じく、登録に一意の系統が要るだけ。TDD01 §3.1)。

### 2. `HouseholdState` の欄

**当日の外出の計画**(TDD01 §3.2。朝の段5a が全世帯ぶん毎日書く):

| プロパティ | 型 | 初期値 | 検査 |
| ---------- | -- | ------ | ---- |
| `ErrandDelegateNpcId` | `int` | **−1**(まだ計画していない) | setter は `< −1` を拒む |
| `PlannedErrandDistrictIds` | `List<int>`(get のみ。中身を書き換える) | 空 | なし |
| `ExportErrandTaken` | `int`(0/1) | 0 | setter は 0/1 以外を拒む(`IsBankrupt` と同じ形) |

- 段5a は、世帯ごとに `ErrandDelegateNpcId = errand.NpcId`、`PlannedErrandDistrictIds.Clear()` → `AddRange(plan.VisitedDistrictIds)`、`ExportErrandTaken = 0` を**外出しない日も**書く(`ErrandLaborLossPermille` を 0 の日も書くのと同じ理由。書かない日があると前日の計画が残る)
- −1 は `RandomSource.NoEntity` と同じ番兵である(TDD01 §3.2「Id は非負」。番兵に −1 を使う規約)。時間割は −1 を「誰も外出しない」と読む

**当日の値付けの控え**(TDD01 §3.2・§3.3 段1。朝の段1 が全世帯ぶん毎日書く)。添字はすべて品目 Id、長さは `itemCount`:

| プロパティ | 型 | 中身 |
| ---------- | -- | ---- |
| `MemoHasPreviousOffer` | `int[]`(0/1) | 段1 の `hasOwnOffer`(前日の自分の提示価格が `Market` にあったか) |
| `MemoPreviousOfferPrice` | `int[]` | 段1 の `ownOfferPrice` |
| `MemoHasSellerReference` | `int[]`(0/1) | 段1 の `hasReference`(`MarketReference.TrySeller` が立ったか) |
| `MemoSellerReference` | `int[]` | 段1 の `marketReference` |

- 段1 は世帯ごとに4本とも `Array.Clear` してから、**自レシピの出力品目の要素だけ**を書く(M0 のレシピは出力1件。コンストラクタの検査が保証する)。**販売在庫 0 の世帯も書く** — 現行の `hasOwnPreviousOffer` / `hasSellerReference` の控えが `sellableStock <= 0` の `continue` より上にあるのと同じ位置
- 0/1 の配列は要素の値を検査できない(配列なので setter が無い)。書き手は段1 だけで、`? 1 : 0` で書く

### 3. ハッシュ(`StateHasher`、TDD01 §3.8)

`Households` 区分の要素の末尾(`ProductionCapacityRuns` の後)へ、この順で足す。既存の値は動かさない(同節の規律):

```
WriteInt32(ErrandDelegateNpcId)
WriteInt32(PlannedErrandDistrictIds.Count); foreach d: WriteInt32(d)   // 選んだ順のまま。並べ替えない
WriteInt32(ExportErrandTaken)
WriteInt32Array(MemoHasPreviousOffer)
WriteInt32Array(MemoPreviousOfferPrice)
WriteInt32Array(MemoHasSellerReference)
WriteInt32Array(MemoSellerReference)
```

コメントは既存の形(「#257が足した欄。既存の値は動かさず末尾へ足す(同じ規律)。」)に揃える。

### 4. `TradePlanningSystem`(新設、`Systems/TradePlanningSystem.cs`)

```csharp
public sealed class TradePlanningSystem : ISimSystem
{
    public TradePlanningSystem(WorldDefinition definition);   // 出力2件以上のレシピを NotSupportedException で拒む(TradeSystem と同じ検査)
    public RandomStream Stream => RandomStream.TradePlanning;
    public Cadence Cadence => Cadence.Daily(hour: 0);
    public void Step(World world, SimContext context);
}
```

`Step` の中身(**順序が仕様**。TDD01 §3.3 段1〜5a):

1. `world.Metrics.BeginDay()`
2. **段1**: 現行 `TradeSystem.Step` の段1 のループをそのまま移す。ローカル配列 `hasOwnPreviousOffer` 等へ控えていた4つを、**控えの欄(上の 2.)へ書く**。Metrics の2欄もここで書く(現行どおり)
3. **段2**: `Market.Clear()` → 一括書き込み(現行どおり)
4. **段3**: `Observations.Expire`(現行どおり)
5. **段4(朝)**: 全世帯ぶんの `BuyerDemand.Build(world, household, MemoHasPreviousOffer[出力] == 1, MemoPreviousOfferPrice[出力])` を作る。**Metrics を書かない**(上の「決めたこと」)
6. **段5a**: 世帯 Id 昇順に `OpportunityCost.SelectErrandDelegate` → `ErrandPlanner.Plan` → `ErrandLaborLossPermille = plan.LaborLossPermille` と計画の3欄を書く。**買い物はしない**

### 5. `TradeSystem`(書き換え)

```csharp
public Cadence Cadence => Cadence.Daily(WorldDefinition.EveningTradeHour);
```

`Step` の中身(TDD01 §3.3 段4〜8):

1. **段4(夕)**: 全世帯ぶんの需要を作り直す — 朝の段4 と**同じ式・同じ引数の出所**(控えの欄)。Metrics の需要の3欄はここで書く(現行の段4 ループの後半をそのまま)。`BeginDay()` は呼ばない
2. **段5b**: 世帯 Id 昇順に `RunOneHouseholdsShopping(world, household, demand, household.PlannedErrandDistrictIds)`。購入の記録(下の 8.)を足す以外は現行どおり
3. 世帯ごとに `var visited = new List<int>(household.PlannedErrandDistrictIds)` を作る(上の「計画の区画列に中心を足さない」)
4. **段6**: 現行どおり `IsExportEnabled` が偽なら段ごと飛ばす。`RunOneHouseholdsExport` へは、`hasSellerReference` / `sellerReference` を控えの欄から、委託先の労働力係数‰ と往復時間の合計を上の「決めたこと」の式で渡し、`visited` を渡す。中心を `visited` に足す枝で `seller.ExportErrandTaken = 1` を書く
5. **段7**: `Observations.CollectAndShare(world, household, visited)` → `Observations.CollectWindow(_definition, world, household, visited)`(現行どおり、別ループ)
6. **段8**: Need の差し引き(下の 8.)

`RunOneHouseholdsExport` の引数 `in ErrandDelegate errand` は `int delegateLaborPermille` に置き換えてよい(使っているのは `LaborPermille` だけ)。

**クラスの doc コメントを書き直す。** 現行は「内部は7段」「順5 に2つのシステムを置けない」と書いている。後者は**今も正しい理由**(系統の重複登録の拒否)で2つの系統を持つことの説明に変える。段の一覧は TDD01 §3.3 の段番号に揃え、朝の段は `TradePlanningSystem` を `<see cref>` で指す。

### 6. `MetricsSystem`

`Cadence => Cadence.Daily(WorldDefinition.EveningTradeHour)`。他は触らない(`DayIndex` しか読まない)。

### 7. 登録順(`Program.BuildPipeline` と `SummaryJsonTests` の写し)

```
Production, Consumption, Household, NeedGeneration, TradePlanning, Trade, Metrics
```

`SimScheduler` は同じ tick に走るシステムを登録順に回すので、0時は順1〜5朝、18時は Trade → Metrics の順になる。

### 8. 段8 — Need の差し引き(GDD02b §8.1)

**購入の記録.** `TradeSystem.Step` のローカルに、世帯 × 用途 × 品目の約定個数 `int[householdCount][4][itemCount]`(用途の添字は `(int)DemandPurpose`)を持つ。`TryPurchaseLine` が `TradeSettlement.Execute` / `ExecuteImport` を呼んだ直後に `actualQuantity`(個)を `checked` で足す。**窓口からの輸入も足す**(GDD02b §8.1「売り手が誰かは問わない」)。**輸出は足さない**(売りである)。World に置かない。

**差し引き.** 段7 の後、`world.Needs` を**格納順のまま**先頭から走査し、各 Need について引く量 `b` を決める:

| `ReasonCode` | `b` |
| ------------ | --- |
| `ProductionStopped` | 対象世帯の `ProductionInput` の、その品目の約定個数 |
| `ToolsExhausted` | 対象世帯の `Durable` の、その品目の約定個数 |
| `Distress` | 対象世帯の `Necessity` の、その品目の約定個数 |
| `DistantStock` | 対象世帯の、その品目の**4用途すべて**の約定個数の和 |
| `CannotExpandProduction` | 0(‰人日で、買い物では埋まらない) |

`b > 0` なら `Quantity − b` に置き換える(`with { Quantity = … }`)。その後 `Quantity <= 0` の要素を `RemoveAll` で消す(格納順は保たれる)。`NextNeedId` には触らない。

- **同じ購入を複数の Need から引いてよい。** 薪を必需で3個買った世帯に `Distress(薪)` と `DistantStock(薪)` が立っていれば、両方から3を引く。理由ごとに数量の出所(どの在庫の不足か)が違うからで、GDD02b §8.1 の表は購入を Need の間で配分していない
- **具体例**(パン屋、薪が必需と生産の入力の2行に現れる。GDD02b §8.1 の表の下の段落): 0時に `ProductionStopped(薪, 4)` と `Distress(薪, 2)` と `DistantStock(薪, 5)` が立ち、18時に薪を必需で2個・入力で0個買った → `ProductionStopped(薪, 4)` は残る、`Distress(薪)` は消える、`DistantStock(薪, 3)` が残る

### 9. 時間割(`Systems/Timetable.cs`、TDD01 §3.9・GDD05 §1.5)

```csharp
public enum TimetableActivity
{
    Resting = 1,        // 自区画の家で休む
    Working = 2,        // 自区画の工房で家業
    Traveling = 3,      // 外出の移動中
    AtDestination = 4,  // 外出先(着いた時刻に居て、そのまま帰る)
}

public readonly record struct TimetableEntry
{
    public TimetableActivity Activity { get; init; }
    /// <summary>居場所の区画Id。Traveling のときは出発区画。</summary>
    public int DistrictId { get; init; }
    /// <summary>Traveling のときの行き先の区画Id。それ以外は DistrictId と同じ値。</summary>
    public int DestinationDistrictId { get; init; }
    /// <summary>Traveling のときの、出発からの経過時間。単位: 時間。それ以外は 0。</summary>
    public int ElapsedHours { get; init; }
}

public static class Timetable
{
    public static TimetableEntry Of(WorldDefinition definition, World world, int npcId, Tick tick);
}
```

**書き込みは一切しない。乱数を引かない。** 世帯は `world.Households[world.Npcs[npcId].HouseholdId]`、自区画 H = その `DistrictId`。

判定(時 = `tick.HourOfDay`):

1. `tick.DayIndex != world.Now.DayIndex` → `ArgumentOutOfRangeException`
2. 時 < `WorkdayStartHour` または 時 ≥ `EveningTradeHour` → `Resting` @ H
3. npcId ≠ `ErrandDelegateNpcId` → `Working` @ H
4. 外出の列を作る: `PlannedErrandDistrictIds` の順に各 d、続けて `ExportErrandTaken == 1` なら中心(`District.ExternalMarketDistrictId`)。各外出の片道 `h = Errand.TravelHours(H, d, definition.TravelHoursPerDistrict) / 2`(`TravelHours` は往復。`× 2` を割り戻す。割り切れる)
5. 出発 S₀ = `EveningTradeHour − Σ 2h`、Sᵢ₊₁ = Sᵢ + 2hᵢ。時 < S₀(外出が無い日を含む)→ `Working` @ H
6. Sᵢ ≤ 時 < Sᵢ + 2hᵢ となる i について、e = 時 − Sᵢ:
   - e < hᵢ → `Traveling` { 出発 H, 行き先 dᵢ, 経過 e }
   - e = hᵢ → `AtDestination` @ dᵢ
   - e > hᵢ → `Traveling` { 出発 dᵢ, 行き先 H, 経過 e − hᵢ }

**具体例**(`TravelHoursPerDistrict = 1`。区画は 3×3 の行優先で、中心 = 4。距離はマンハッタン):

| 条件 | 6〜S₀−1 | S₀ 以降(時: 状態) | 18 |
| ---- | -------- | ------------------- | -- |
| H = 0、計画 [1]、輸出 0 | Working @0(6〜15) | 16: Traveling 0→1 経過0 / 17: AtDestination @1 | Resting @0 |
| H = 0、計画 [8, 1]、輸出 0(往復 8 + 2 = 10) | Working @0(6〜7) | 8〜11: Traveling 0→8 経過0〜3 / 12: AtDestination @8 / 13〜15: Traveling 8→0 経過1〜3 / 16: Traveling 0→1 経過0 / 17: AtDestination @1 | Resting @0 |
| H = 0、計画 [1]、輸出 1(往復 2 + 4 = 6) | Working @0(6〜11) | 12: Traveling 0→1 経過0 / 13: AtDestination @1 / 14〜15: Traveling 0→4 経過0〜1 / 16: AtDestination @4 / 17: Traveling 4→0 経過1 | Resting @0 |
| 委託先でない構成員 | Working @H(6〜17) | — | Resting @H |
| `ErrandDelegateNpcId == −1`(計画前) | Working @H(6〜17) | — | Resting @H |

**帰り着いた時刻と次の出発は同じ tick である**(2行目の 16時: 8 から帰り着いた瞬間に 1 へ出る)。そのとき出発区画は H、経過 0 として返す。

## 落ちるべき条件(テスト)

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心(当てる変異 / 期待) |
| - | ------ | -------- | -------------------- | ------------------------- |
| 1 | `TradePlanningRunsAtMidnightAndTradeSettlesAtEvening` | 本物の登録順で1日を `Advance` する。tick 0〜17 を処理した後: `Market` が当日の値・計画の欄が書かれている・帳簿に当日の行が無い。18 を処理した後: 当日の帳簿の行があり、`OccurredAt.HourOfDay == 18` | `TradeSystem` の `Cadence` を 0時のまま残す / 段5b を朝へ置く | — |
| 2 | `EveningDemandIsRebuiltFromTheCurrentState` | 0時を処理した後・18時より前に、テストが世帯の流動資金(または世帯在庫)を書き換える(プレイヤーの売買の代わり)。18時の購入量が、書き換えた状態から作った需要で決まる(書き換えないときと購入量が違うことを確かめる組み合わせにする) | 夕方の段4 を省き、朝の需要をインスタンスのフィールドで持ち越す | — |
| 3 | `EveningDemandReadsYesterdaysOwnOfferFromTheMemo` | 前日の `Market` に自分の出力品目の高い提示価格 P_old を仕込み、当日は相場基準が立たず提示価格が床(低い)になる売り手兼買い手(例: パン屋が小麦を買う)。利潤上限が P_old なら入力を買い、当日の提示価格なら買えない値にする。18時に入力を買ったことを確かめる | 夕方の段4 が控えの欄ではなく `world.Market` から当日の自分の提示価格を引く | **核心**: `TradeSystem` 段4(夕)で `BuyerDemand.Build` に渡す前日の提示価格を `world.Market.TryGetValue(new MarketKey(出力, household.Id), out p)` の結果に差し替える / **赤** |
| 4 | `ExportTripDoesNotAddTheCentreToThePlannedDistricts` | 中心へ別の外出で輸出する日を作る(現行 `ExportTripAddsTheCentreToTheObservedDistricts` の構成を流用してよい)。18時の後: `PlannedErrandDistrictIds` に中心が無い・`ExportErrandTaken == 1`・窓口の観測が生まれている(段7 が複製を読んだ) | 段6 が World の列に中心を足す / フラグを書かない / 段7 が World の列を読む(窓口の観測が消える) | **核心**: 段6 で中心を足す先を `visited` から `household.PlannedErrandDistrictIds` に差し替え、段7 にも World の列を渡す / **赤** |
| 5 | `ExportFlagIsClearedTheNextMorning` | #4 の翌日の0時の後に `ExportErrandTaken == 0` | 段5a がフラグを 0 に戻さない | — |
| 6 | `PlanIsWrittenEvenOnDaysWithoutAnErrand` | 前日に区画列が非空だった世帯が、当日は外出しない(価値 ≤ 0)。0時の後に列が空・委託先 NpcId が当日の選択の値 | 段5a が外出の無い日に計画を書かない | — |
| 7 | `NeedSubtractionMatchesThePurpose`(Theory、上の段8 の具体例を含む) | 4理由それぞれについて、用途の合う購入だけが引かれる。具体例の3つの結果 | 品目だけで引く(用途を見ない) / `DistantStock` を1用途だけで引く / `CannotExpandProduction` を引く | **核心**: 段8 で `ProductionStopped` の `b` を「その品目の4用途すべての和」に差し替える / **赤**(具体例の `ProductionStopped(薪, 4)` が消える) |
| 8 | `NeedSubtractionRemovesFilledNeedsAndKeepsTheRest` | 部分的に引かれた Need は Id と格納順を保ったまま数量が減る。0 以下は消える。消えた組が翌日また立つと新しい Id(`NextNeedId` から)、残った組は同じ Id | `Quantity <= 0` ではなく `< 0` で消す / 消さずに 0 で残す / 並べ替える | — |
| 9 | `NeedSubtractionCountsWindowImports` | 窓口からの輸入だけで買った品目の `DistantStock` が引かれる | 段8 の記録を `TradeSettlement.Execute`(都市内)の後にだけ置く | — |
| 10 | `TimetableFollowsTheWorkedExamples`(Theory、上の具体例の表の全行・全時刻 0〜23) | 表のとおりの `TimetableEntry` が返る | 片道に往復(`TravelHours`)をそのまま使う / 出発を 6時起点で数える / 輸出の外出を先に置く・数えない / 帰着と次の出発の tick を1ずらす | **核心**: 手順4 の `/ 2` を外す / **赤** |
| 11 | `TimetableRejectsATickOfAnotherDay` | `world.Now` と別の日の tick で `ArgumentOutOfRangeException` | 日の検査を省く | — |
| 12 | `TimetableDoesNotReadThePlanOutsideWorkingHours` | 計画の欄に任意の値を入れても 0〜5時・18〜23時は全員 `Resting` @H | 外出の判定を時刻の検査より先に置く | — |
| 13 | `WorldDefinitionRejectsDisposableHoursLongerThanTheWorkday` | `disposableHours: 13` で `ArgumentOutOfRangeException`、12 は通る | 検査を省く / `>=` で 12 を拒む | — |
| 14 | `HashChangesWhen<欄>Changes`(Theory、計画3つ・控え4つの7欄) | 1欄だけ変えるとハッシュが変わる。区画列は**並べ替えただけ**でも変わる | ハッシュに足し忘れる / 区画列をソートしてから書く | — |
| 15 | `TradePlanningStreamIs14` | `new TradePlanningSystem(d).Stream == (RandomStream)14`、全システムの系統が重複しない(本物の登録順で `SimScheduler` が構築できる) | 値を振り直す | — |

**#10 の「全時刻」は 0〜23 の 24 点を行ごとに並べる。** 6〜S₀−1 の Working を1点だけ確かめる形にしない — 出発を 6時起点で数える誤りは、Working の範囲の端でしか出ない。

## 6. 分割前との一致の実測(閉じる条件)

**完了条件の1つである。テストではなく、1回だけ測ってコミット本文に残す。** 比べる相手は本タスクの分岐元 `788b6e4`(凍結時点の `origin/master`)。

1. `git worktree add --detach .pipeline/baseline/257 788b6e4`
2. 設定を `Write` で `.pipeline/baseline/257-split.json` に置く: `{ "masterSeeds": [1, 2, 3, 4, 5], "durationDays": 30, "world": { "preset": "m0-small" } }`
3. `dotnet run --project .pipeline/baseline/257/src/Visionary.Sim.Runner -c Release -- run --config .pipeline/baseline/257-split.json --out .pipeline/baseline/257-out-before`
4. `dotnet run --project src/Visionary.Sim.Runner -c Release -- run --config .pipeline/baseline/257-split.json --out .pipeline/baseline/257-out-after`
5. `git diff --no-index --stat .pipeline/baseline/257-out-before .pipeline/baseline/257-out-after` — **出力が空であること**
6. `git worktree remove --force .pipeline/baseline/257`

**差が出たら止まって報告する(`IMPL-BLOCKED`)。** 差分を消す方向にコードや期待を合わせない。プレイヤー不在では0時と18時の間に World を書くシステムが無いので、差は分割の誤り(段の順序・控えの取り違え・Metrics の書き手)を意味する。段8 は `Needs` しか書かず、`Needs` を読むのは `NeedGenerationSystem` とハッシュだけなので CSV には現れない(`grep` で確認済み)。

**記録**: 一致を確かめたコミットの本文に、手順3〜5 のコマンドと、比べた2つの HEAD、`--stat` が空だったことを書く。

## 7. 既存テストの追随

分割で既存テストが壊れる経路は3つで、**どれも期待値を変えずに直る**:

- `new TradeSystem(definition)` を単独で登録して日単位で回すテスト → `EconomySystemTestFixtures` に `TradeSystems(WorldDefinition)`(`new ISimSystem[] { new TradePlanningSystem(d), new TradeSystem(d) }`)と `RunTradeDays(World, WorldDefinition, int days, long seed = 1)` を足し、そちらへ置き換える。他のシステムと並べて登録しているテストは、`TradeSystem` の直前に `TradePlanningSystem` を挿す
- 24 tick 未満しか進めずに約定・Metrics を見ているテスト → 18時を処理するところまで進める
- 帳簿・観測の tick を 0時の値で比べているテスト → 18時の値にする

**この3つ以外の理由で既存テストの期待値(数量・価格・件数・ハッシュ以外の値)を変えることになったら、止まって報告する。** 上の「分割前との一致」と同じ理由で、それは分割が軌跡を変えた印である。

## 編集してよい文書

- なし(コードとテストのみ)

## このタスクで特に効く規約

- **朝と夕方のシステムの間で World 以外の経路を作らない**(上の「決めたこと」)。インスタンスのフィールド・静的フィールドで中間値を持ち越すと、ハッシュが同じで振る舞いが違う世界ができる。**型では防げない**ので、ここはレビュー観点である
- **段8 は `world.Needs` の格納順で走査し、`RemoveAll` で消す。** 世帯 × 理由で索引を作って `Dictionary` を列挙すると列挙順規約に触れる
- 新しい定数には単位のコメントを付ける(`WorkdayStartHour` / `EveningTradeHour`)

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **「核心」印の変異を `mutator` が実測し(レビューの巡が閉じた後)、結果を doc コメントへ転記した**
- [ ] **「6. 分割前との一致の実測」の差分が空で、その記録がコミット本文にある**
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
