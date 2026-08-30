# W1-03: World と Clock/スケジューラ

| 項目     | 内容                                                             |
| -------- | ---------------------------------------------------------------- |
| 根拠     | [TDD01 §3.1〜3.3](../04-tdd/01-sim-core-and-m0.md) / [ADR-0002](../adr/0002-time-model-and-determinism.md) / [GDD03 §1.3](../03-gdd/03-seasons-and-city.md) |
| ブランチ | `feat/world-and-scheduler`                                       |
| worktree | `visionary/`(本体)                                              |
| 状態     | 未着手                                                           |

> **この文書は使い捨ての作業指示である。**実装完了時点で凍結し、以後の正はコードと TDD。

## スコープ

**器を作るタスクであって、経済を作るタスクではない。** 生産・消費・取引などの中身は W2(TDD01 §3.6 の仮経済)で入れる。ここで作るのは、それらが載る土台:

- `World`(全状態を保持する単一の集約。区画は型として定義するが中身は最小)
- `Clock`(現在tick)
- `Cadence`(実行周期)
- `ISimSystem` と `SimScheduler`
- `SimContext`(システムが乱数と時刻に触る唯一の口)

**経済システム(Production〜Rumor)の実装は含まない。** スケジューラの検証にはテスト用のダミーシステムを使う。

## 作るもの

名前空間は `Visionary.Sim`(World 系)と `Visionary.Sim.Systems`(スケジューラ系)。

### `Cadence`(readonly struct、`Visionary.Sim.Systems`)

```csharp
public readonly struct Cadence
{
    public static Cadence EveryTick();
    public static Cadence Daily(int hour);                    // hour: 0〜23
    public static Cadence Weekly(int dayOfWeekIndex, int hour); // dayOfWeekIndex: 0〜6
    public bool ShouldRunAt(Tick tick);
}
```

- `Daily(h)`: `tick.HourOfDay == h`
- `Weekly(d, h)`: `tick.DayIndex % 7 == d && tick.HourOfDay == h`(TDD01 §3.1。週は暦と独立)
- 範囲外の `hour` / `dayOfWeekIndex` は `ArgumentOutOfRangeException`

### `ISimSystem`(`Visionary.Sim.Systems`)

```csharp
public interface ISimSystem
{
    RandomStream Stream { get; }
    Cadence Cadence { get; }
    void Step(World world, SimContext context);
}
```

**`Stream` を持たせるのが要点。** システムの識別子を系統そのものにすることで、`SimContext` が「今動いているシステムの系統」しか開けなくなり、**系統をまたいだ乱数の借用が API として不可能になる**(ADR-0002 の規約を規律ではなく型で守る)。

### `SimContext`(sealed class、`Visionary.Sim.Systems`)

```csharp
public sealed class SimContext
{
    public Tick Now { get; }
    public RandomSequence OpenRandom(int entityId);
    public RandomSequence OpenRandom();   // entityId = RandomSource.NoEntity
}
```

- `OpenRandom` は**現在実行中のシステムの `Stream`** で `RandomSource.Open` を呼ぶ。呼び出し側が系統を指定する口を作らない
- 現在のシステムはスケジューラが `Step` の直前に設定する(`internal` なセッターでよい)
- `Now` はスケジューラが進める現在tick
- **同一tick内で同じ `entityId` に対して2度 `OpenRandom` を呼んだら `InvalidOperationException`。** 同じ組を2度開くと同じ値列が返るため(TDD01 §3.1「機械で守れていない残りの危険」)。tick が進むたびに記録をクリアする。記録は `SortedSet<int>` など列挙順が定まるコレクションで持つ
- **`RandomSequence` を引数に渡すときは必ず `ref` を付ける。** 値コピーは元と独立に進み、同じ値が2度返る

### `World`(sealed class、`Visionary.Sim`)

TDD01 §3.2 の9区画を型として定義する。**W1 で中身を持つのは `Now` と `Npcs` のみ**、残りは空のコンテナとして用意する。

```csharp
public sealed class World
{
    public Tick Now { get; internal set; }
    public NpcState[] Npcs { get; }        // Id 昇順。添字 = NpcId
    // 以下は W2 以降で中身が入る。型だけ用意する
    public SortedDictionary<MarketKey, int> Market { get; }        // (itemId, sellerId) → 提示価格
    public SortedDictionary<TrustKey, TrustScore> TrustLedger { get; }
    public List<Need> Needs { get; }
    public List<Promise> Promises { get; }
    public List<PriceObservation> Knowledge { get; }
    public List<LedgerEntry> Ledgers { get; }
    public List<DomainEvent> EventLog { get; }
}
```

- **`Dictionary` / `HashSet` を使わない**(列挙順が不定)。疎な対応は `SortedDictionary`、密な対応は Id 添字の配列
- キーの型(`MarketKey` / `TrustKey`)は `readonly record struct` にして `IComparable<T>` を実装する。フィールドは int のみ
- `NpcState` は W1 では最小: `int Id`、`int LiquidFunds`、`int[] Inventory`(添字 = itemId)。職業・性格・固定支出は W2
- `Need` / `Promise` / `PriceObservation` / `LedgerEntry` / `DomainEvent` は **W1 では型の宣言のみ**でよい。フィールドは TDD01 §3.2 / GDD01 の定義に従い int/long/enum のみ
- **すべて int/long/enum。浮動小数点も文字列 Id も持たない**(TDD01 §3.2)

### `SimScheduler`(sealed class、`Visionary.Sim.Systems`)

```csharp
public sealed class SimScheduler
{
    public SimScheduler(IReadOnlyList<ISimSystem> systemsInPipelineOrder, RandomSource random);
    public void Advance(World world, int ticks);
}
```

- `Advance` は1tickずつ進める。各tickで **登録順に** 全システムを見て、`Cadence.ShouldRunAt(now)` が真のものだけ `Step` する
- **登録順が仕様**(TDD01 §3.3)。登録順ではなく明示順で固定する、という記述はこの「呼び出し側が §3.3 の順に並べた配列を渡す」ことを指す
- `world.Now` は各tickの処理の**前**に更新する
- `ticks` が0以下なら `ArgumentOutOfRangeException`
- 同じ `RandomStream` を持つシステムを2つ以上登録したら `ArgumentException`(系統の重複は共通乱数法を壊す)

## 緑にすべきテスト

**この節が完了条件そのもの。**

| #  | テスト                                              | 検証内容                                                              |
| -- | --------------------------------------------------- | --------------------------------------------------------------------- |
| 1  | `EveryTickCadenceRunsOnEveryTick`                   | 24tick進めて24回                                                      |
| 2  | `DailyCadenceRunsOncePerDayAtGivenHour`             | 72tick進めて3回、毎回指定時刻                                         |
| 3  | `WeeklyCadenceRunsEverySevenDays`                   | 21日進めて3回                                                         |
| 4  | `WeeklyCadenceDriftsAcrossSeasonBoundary`           | 30日は7で割り切れないため、季節をまたいで曜日がずれる(GDD03 §1.3)    |
| 5  | `CadenceRejectsOutOfRangeArguments`                 | hour・dayOfWeekIndex の範囲外は例外                                   |
| 6  | `SystemsRunInRegisteredOrderWithinATick`            | 同一tickで登録順に呼ばれる                                            |
| 7  | `AdvanceUpdatesClockBeforeRunningSystems`           | `Step` から見える `world.Now` / `context.Now` がそのtickを指す        |
| 8  | `AdvanceRejectsNonPositiveTicks`                    | 0以下は例外                                                           |
| 9  | `DuplicateRandomStreamIsRejected`                   | 同一系統の二重登録は例外                                              |
| 10 | **`SystemReceivesOnlyItsOwnRandomStream`**          | あるシステムの `OpenRandom` が、そのシステムの `Stream` の列を返す     |
| 10b | `DoubleOpenForSameEntityInSameTickThrows`          | 同一tick・同一エンティティの2度目の `OpenRandom` は例外                |
| 10c | `OpenRandomIsAllowedAgainOnTheNextTick`            | tick が進めば同じエンティティで再び開ける                             |
| 11 | `SameSeedProducesIdenticalWorldAfterAdvance`        | 同一シード2回実行で `Npcs` の内容が完全一致                           |
| 12 | `NpcsAreIndexedByAscendingId`                       | `Npcs[i].Id == i`                                                     |
| 13 | `WorldCollectionsAreDeterministicallyOrdered`       | `SortedDictionary` の列挙がキー昇順(`Dictionary` に差し替えたら落ちる) |

**テスト10と11がこのタスクの核心。** 10は系統の借用を API が防いでいること、11は器の段階で決定論が成立していることを示す。

## 編集してよい文書

- `docs/tasks/W1-03-world-and-scheduler.md`(この文書の状態欄)
- **TDD・GDD・ADR は触らない。** 実装中に文書との食い違いに気づいたら、直さずに報告すること

## このタスクで特に効く規約

- **NPC の処理順は Id 昇順で固定**(ADR-0002)。`Npcs` 配列を添字順に走査すれば満たされるが、後から `where`/`OrderBy` を挟むときは順序が保たれることを確認する
- **系統をまたいで乱数を借用しない。** `SimContext` の API がこれを構造的に防ぐ設計になっているので、その設計を崩さない
- 機械で捕まる規約(浮動小数点・`Dictionary`・`System.Random` など)はここに書かない

## 完了条件

- [ ] 「緑にすべきテスト」が全て緑
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
