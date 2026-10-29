# W5-05: SimHost の読み出し口(Query・DrainEvents)と Runner の台本の口

| 項目     | 内容                           |
| -------- | ------------------------------ |
| issue    | #275(#263 を分割した後半。決定はコメント [6469563316](https://github.com/stama72/visionary/issues/263#issuecomment-6469563316) の決定1・3) |
| 根拠     | TDD01 §3.2・§3.4・§3.5・§3.6・§3.9・§4.1・§4.2 / GDD01 §6.1 / GDD02c §1.3 / #103 決定1 |
| ブランチ | `feat/275-query-events`        |
| worktree | 本体(パイプラインが使う)     |

## スコープ

**含む:** `DomainEvent` の作り直しと `PlayerTradeExecuted` の発行、`SimHost.DrainEvents`、`SimHost.Query`(読み出しモデル)、`MetricsSystem` のプレイヤー除外と `economy.csv` の `player_funds` 列、Runner の台本の口(`vsim run` を `SimHost` で組む)。

**含まない:** 台本プレイヤーの中身・設定ファイルで台本を選ぶキー・計測(#264)。Godot 側(#47)。`vsim hash` / `vsim dialogue-sample` の組み立て(変えない)。

**変えない既存コード**(規則8):

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `PlayerCommandExecutor` の検査の順序と、6コマンドの売買の規則(数量・単価・帳簿・所持金) | TDD01 §3.4 / GDD06 §6.1〜§6.4(W5-04 で突き合わせ済み) | 一致。**本タスクは Accepted の売買の末尾にイベントの追記を足すだけである** |
| `DisclosedNeeds.Of`(開示は生産停止・工具切れ・遠方在庫の3理由、Need Id 昇順) | GDD01 §6.1 | 一致 |
| `SellableStock.Of`(販売在庫 = 工房在庫 − 留保量) | GDD02c §1.3 | 未確認 |
| `Timetable.Of`(同じ日の tick だけを受ける。0時ちょうどは前日の計画で答える穴がある) | TDD01 §3.9 | 未確認(穴は #268) |
| `MetricsSystem` の、プレイヤー以外の行の数え方(窓口の行の扱い・HHI・区画・中央値) | TDD01 §4.2 | 未確認。本タスクは相手 Id が `PlayerId` の行の扱いだけを変える |
| `Program.RunHash`(`vsim hash`)と `DialogueSampleCommand` の世界の組み立て | TDD01 §4.1 | 一致(本タスクで触らない) |

## 作るもの

### 1. `DomainEvent` の作り直しと発行(TDD01 §3.4・§3.6 の仮決め表)

`src/Visionary.Sim/World/DomainEvent.cs` を置き換える。**W1 の `readonly record struct DomainEvent`(`KindCode` / `SubjectId` / `RelatedId` / `Payload`)は消す。**

```csharp
namespace Visionary.Sim;

/// 基底。種別ごとに派生型を作る(TDD01 §3.4)。
public abstract record DomainEvent(Tick At);

public enum PlayerTradeKind
{
    SellToHousehold = 1,
    BuyFromHousehold = 2,
    BuyFromWindow = 3,
    SellToWindow = 4,
}   // 0 を使わない(既定値が有効な種別に見えるのを避ける。CommandStatus と同じ)

public sealed record PlayerTradeExecuted(
    Tick At,
    PlayerTradeKind Kind,
    int CounterpartyId,   // 世帯 Id。窓口なら HouseholdState.ExternalMarketSellerId
    int ItemId,
    int Quantity,         // 単位: 個。Result.Quantity と同じ値
    int UnitPrice,        // 単位: 貨幣/個。Result.UnitPrice と同じ値
    Gratitude? Gratitude, // SellToHousehold のときだけ。それ以外は null
    int NeedId)           // SellToHousehold のときだけ Result.NeedId。それ以外は −1
    : DomainEvent(At);
```

- `World.EventLog` の型は `List<DomainEvent>` のまま(要素型が変わるだけ)
- **発行するのは `PlayerCommandExecutor` の4つの売買の Accepted の経路だけ。** 各メソッドの、所持金・所持品を書いた後、`return` の直前で `world.EventLog.Add(...)` する。値は返す `Result` と同じもの(`Quantity` は約定した個数であってコマンドの要求数量ではない。`At = world.Now`)
- **拒否・`Move`・`AskNeeds` は発行しない。** NPC のシステム(順1〜10)は発行しない
- 乱数を開かない。ハッシュに乗せない(`StateHasher` は `EventLog` を読まない。今のまま)

### 2. `DrainEvents`(TDD01 §3.5)

`SimHost` に足す:

```csharp
private int _drainedEventCount;   // 読み出し位置。World の状態ではない(ハッシュに乗せない)

public IReadOnlyList<DomainEvent> DrainEvents();
```

- `World.EventLog` の `[_drainedEventCount, Count)` を**新しい `List` に写して**返し、`_drainedEventCount = Count` にする。何も無ければ空のリスト
- **`EventLog` を消さない**(追記専用。TDD01 §3.4)
- 例(規則6): 窓口から買う(1件)→ `DrainEvents()` は1件 → もう一度呼ぶと0件 → 窓口から買い、窓口へ売る(2件)→ `DrainEvents()` は2件で、買い・売りの順

### 3. `Query`(TDD01 §3.5)

`src/Visionary.Sim/ReadModels.cs`(名前空間 `Visionary.Sim`)。`SimHost` に `public ReadModels Query { get; }` を足し、コンストラクタで `new ReadModels(definition, World)` を1つ作る。

```csharp
public sealed class ReadModels
{
    internal ReadModels(WorldDefinition definition, World world);

    public Tick Now { get; }                                   // = world.Now
    public IReadOnlyList<OfferView> Offers();
    public IReadOnlyList<HouseholdView> Households();
    public IReadOnlyList<Need> TradableNeeds();
    public TimetableEntry TimetableOf(int npcId);
    public PlayerView Player();
}

public readonly record struct OfferView(
    int ItemId, int HouseholdId, int DistrictId, int UnitPrice, int SellableStock);

public sealed record HouseholdView(
    int Id, int DistrictId, Occupation Occupation, int OutputItemId, IReadOnlyList<int> MemberNpcIds);

public sealed record PlayerView(
    int Funds, IReadOnlyList<int> Inventory, PlayerLocation Location, int DestinationDistrictId, Tick? ArrivalAt);
```

**どのメソッドも `World` を書かず、呼ぶたびに新しい写しを作る**(`World` の配列・リストをそのまま包んで返さない。`Array.AsReadOnly(player.Inventory)` は不可 — 描画側から見える値が後の `Execute` で変わる)。

| メソッド | 中身 |
| -------- | ---- |
| `Offers()` | `world.Market` を列挙順(`MarketKey` の比較 = 品目 Id 昇順 → 世帯 Id 昇順)に1件ずつ。`DistrictId = world.Households[世帯].DistrictId`、`UnitPrice` = Market の値、`SellableStock = SellableStock.Of(definition, household, itemId)` |
| `Households()` | 世帯 Id 昇順。`OutputItemId = definition.Recipes[(int)household.Occupation].Outputs[0].ItemId`(`MetricsSystem` と同じ取り方)。`MemberNpcIds` は配列の写し |
| `TradableNeeds()` | 世帯 Id 0, 1, … の順に `DisclosedNeeds.Of(world, h)` を連結した新しいリスト。**居合わせを見ない。開示されない理由の Need を入れない**(TDD01 §3.5) |
| `TimetableOf(npcId)` | `Timetable.Of(definition, world, npcId, world.Now)` をそのまま返す。npcId の範囲外は `Timetable.Of` の例外に任せる |
| `Player()` | 下記 |

`Player()`:

```
loc = world.Player.LocationAt(world.Now)
Funds = Player.Funds
Inventory = Player.Inventory の写し
Location = loc
DestinationDistrictId = loc.InTransit ? Player.MoveDestinationDistrictId : -1
ArrivalAt             = loc.InTransit ? new Tick(Player.MoveArrivalTick) : null
```

例(規則6。W5-04 のテスト1と同じ形): D0T06 に 4→0 へ移動(到着 D0T08)。D0T07 の `Player()` は `Location = (true, 4)`・行き先 0・到着 D0T08。D0T08(次の移動コマンドで正規化される前)は `Location = (false, 0)`・行き先 −1・到着 null。**`MoveDestinationDistrictId` は D0T08 でも 0 のまま残っているので、`!= -1` で移動中を判定すると到着済みを移動中と返す。**

### 4. `Metrics` のプレイヤー除外と `player_funds`(TDD01 §3.2・§4.2)

`MetricsSystem.Step` の帳簿の走査で、`entry.CounterpartyId == HouseholdState.PlayerId` の行を次のとおり扱う:

| 行 | 扱い |
| -- | ---- |
| `Purchase`(世帯がプレイヤーから買った) | **何にも数えない。** `settlementCountTotal`・`settled*`・`window*`・`internal*`・`district*`・`activeBuyer` のどれにも入れない |
| `Sale`(世帯がプレイヤーへ売った) | **`householdSalesValueAll` にだけ足す**(households.csv の `sales_value` と `profit`、economy.csv の `profit_total` に入る)。`activeSeller`・`householdSalesValueInternal`(HHI の分子)・`exportedToday` には入れない |

**判定は相手 Id だけで行う**(TDD01 §3.2「相手 Id(プレイヤーの予約 Id)で外す」)。

`EconomyRow` の末尾に `int PlayerFunds` を足し、`MetricsSystem` が `world.Player.Funds` を写す。**`MoneyTotal` には足さない**(TDD01 §4.2)。`CsvMetricsSink` の economy.csv のヘッダの末尾に `player_funds` を足し、値を末尾に書く。`EconomyRow` を作っている既存のテスト・ビルダー(`DailySnapshotTestBuilder` など)は値を足して追随させる。

### 5. Runner の台本の口(TDD01 §4.1・#103 決定1)

`src/Visionary.Sim.Runner/PlayerScript.cs`(すべて `internal`):

```csharp
internal interface IPlayerSession
{
    Tick Now { get; }
    Result Execute(ICommand command);
    ReadModels Query { get; }
    IReadOnlyList<DomainEvent> DrainEvents();
}

internal interface IPlayerScript
{
    void OnHour(IPlayerSession session);
}

internal sealed class SimHostPlayerSession : IPlayerSession   // SimHost の4メンバーへ委譲するだけ
{
    public SimHostPlayerSession(SimHost host);
}

internal static class ScriptedRun
{
    public static void Run(SimHost host, IPlayerScript? script, int ticks);
}
```

- **`IPlayerSession` は `Advance` も `SimHost` も `World` も晒さない**(TDD01 §4.1「台本には `Advance` を渡さない」)
- `ScriptedRun.Run`: `ticks < 1` → `ArgumentOutOfRangeException`。`script` が null なら `host.Advance(ticks)` を1回。そうでなければ `session = new SimHostPlayerSession(host)` を1つ作り、`ticks` 回 `script.OnHour(session); host.Advance(1);` を繰り返す
- 例(規則6): D0T00 から `ticks = 48` で回すと、`OnHour` は48回呼ばれ、そのときの `Now` は D0T00, D0T01, …, D1T23。終了時 `host.Now` = D2T00。**`OnHour` が見る時刻 T の tick はまだ処理されていない**(W5-04 のコマンドの時刻と同じ意味)
- `Program.RunRun` のシードごとの組み立てを置き換える: `var host = new SimHost(definition, seed, startDay: 0, metricsSink: composite); ScriptedRun.Run(host, script: null, ticks);`。`WorldGenerator.Generate` と `new SimScheduler(...)` の2行は消す。**台本を選ぶ設定キーは足さない**(#264)。`RunConfig` は変えない

### 6. 既存テストの追随

- `StateHasherTests.HashIgnoresEventLog`: 新しい型で `PlayerTradeExecuted` を1件足して、ハッシュが変わらないことを見る形に書き換える(テスト名は変えない)
- `StateHasherCoverageTests` の除外一覧の `"EventLog"` はそのまま
- `CsvMetricsSinkTests` の economy.csv のヘッダと値の表明に `player_funds` を足す

## 呼び出し側を持たないコード(規則7)

`Query` と `DrainEvents` は本タスクの時点で本番の呼び出し側を持たない(Godot は #47、台本は #264)。`ScriptedRun` も `vsim run` からは `script: null` でしか呼ばれない。配線の約束をここに固定する:

- **入力の作り方**: `ReadModels` は `SimHost` のコンストラクタが1つだけ作り、`SimHost.World` と同じ `World` を持つ。先回し(`startDay > 0`)の後に作っても前に作っても同じ `World` を指すこと
- **添字・並び・単位**: `Offers()` は `MarketKey` 順、`Households()` と `TradableNeeds()` は世帯 Id 昇順(後者は世帯内 Need Id 昇順)、`DrainEvents()` は追記順。`PlayerView.ArrivalAt` は `Tick`(tick の値)、`UnitPrice` は貨幣/個
- **呼び出し順**: `Query` と `DrainEvents` はいつ呼んでもよい(`Advance` の途中には呼べない — シングルスレッド)。台本は毎時 `Advance(1)` の前に呼ばれる。**18時の約定(順5夕)の後の `Market` と帳簿を見たいなら、台本は 19時の `OnHour` で見る**(18時の `OnHour` はその tick の処理前)

## 落ちるべき条件(テスト)

テストファイル: `tests/Visionary.Sim.Tests/SimHostEventTests.cs`(1〜6)、`tests/Visionary.Sim.Tests/SimHostQueryTests.cs`(7〜13)、既存の `Systems/MetricsSystemTests.cs`(14〜15)、`Runner/ScriptedRunTests.cs`(16〜20)、既存の `Determinism/StateHasherTests.cs`(21)。**前提状態は W5-04 と同じく `SimHost` を作ってから internal の `World` を書いて作ってよい**(居場所・所持品・時刻。時刻は `Advance` で進める)。

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心(当てる変異 / 期待) |
| - | ------ | -------- | -------------------- | ------------------------- |
| 1 | 世帯へ売るのイベント | 開示 Need のある世帯へ売って Accepted。`DrainEvents()` が1件で、`At == Now`・`Kind == SellToHousehold`・相手 = 世帯 Id・品目・`Quantity`/`UnitPrice`/`Gratitude`/`NeedId` が返した `Result` と一致。**要求数量 > 約定数量になる前提で表明する**(Need の数量で切られる形) | コマンドの要求数量を書く / 感謝や NeedId を写し忘れる | |
| 2 | 4方向の相手 Id | 窓口から買う・窓口へ売る・世帯から買う・世帯へ売るを1回ずつ Accepted にし、4件が実行順に並ぶ。窓口の2件の相手は `ExternalMarketSellerId`、世帯から買うの相手は売り手の世帯 Id。世帯へ売る以外は `Gratitude == null`・`NeedId == -1` | 窓口の売買を発行しない / 相手を `PlayerId` にする / 窓口を相手 −1 にする | **核心**: `BuyFromWindow` の `EventLog.Add` を消す / 赤 |
| 3 | 拒否は発行しない | `InTransit`・`OutsideTradingHours`・`NoNeed`・`OverBudget`・`InsufficientFunds`・`NotEnoughGoods` の拒否と、Accepted の `Move`・`AskNeeds` の後で `EventLog.Count` が 0 のまま | 検査の前に追記する / `PlayerSale` の結果を見ずに追記する / 移動を売買として発行する | |
| 4 | 読み出し位置 | 「作るもの」2. の例どおり: 1件 → `DrainEvents()` 1件 → 0件 → 2件発行 → 2件(買い・売りの順) | 毎回全件を返す / 位置を進め忘れて重複 / 逆順 | **核心**: `_drainedEventCount = Count` を消す / 赤 |
| 5 | 消さない・写す | `DrainEvents()` の後も `World.EventLog.Count` が変わらない。受け取ったリストの件数が、後の発行で変わらない | `EventLog.Clear()` で読み出す(追記専用の違反)/ `EventLog` を包んだ生のビューを返す | |
| 6 | NPC は発行しない | コマンドを打たずに `SimHost(M0, 1)` を60日 `Advance` → `EventLog` は0件 | NPC のシステムに発行を入れる | |
| 7 | 売り注文 | D1T06 に `Offers()` が `World.Market` と件数・各キーの価格で一致し、`MarketKey` 昇順。各行の `DistrictId` = 世帯の区画、`SellableStock` = `SellableStock.Of`。**販売在庫 ≠ 工房在庫の行が1件以上あることを表明する**(留保が0の行ばかりだと工房在庫との取り違えが通る。M0 で留保が立つのは工具の売り手の行(設備の留保 1。`SellableStock.ReserveQuantity`)だけなので、その行の工房在庫が 1 以上の日を選ぶ。シード 1 の60日内で見つからなければ、日を変える前に止まって報告) | 工房在庫を返す / 区画をプレイヤーの居場所や世帯 Id で埋める / 並びを辞書の列挙に任せて崩す | |
| 8 | 世帯 | `Households()` が世帯 Id 昇順で、区画・職業・出力品目・構成員が `World` と一致 | 出力品目をレシピの入力から取る / 構成員を世帯主だけにする | |
| 9 | 取引で解ける Need | ある日の `TradableNeeds()` が `DisclosedNeeds.Of(world, 0)`, `Of(world, 1)`, … の連結と並びまで一致。**その日の `World.Needs` に開示されない理由の Need が1件以上あり、開示される Need も1件以上あることを表明する**(シード 1 の60日内で探す。無ければ止まって報告)。プレイヤーが中心に居るまま(居合わせていない世帯の Need も入る) | `World.Needs` を全件返す / 居合わせた世帯だけにする / 世帯をまたいで Need Id 順に並べ替える | **核心**: 世帯ごとの `DisclosedNeeds.Of` をやめ、`World.Needs` 全件を Need Id 昇順で返す / 赤 |
| 10 | 時間割 | D1T09 と D1T20 に、全 NPC について `TimetableOf(npc)` が `Timetable.Of(definition, world, npc, Now)` と一致。**D1T09 に `Traveling` か `AtDestination` の NPC が1人以上いることを表明する**(シード 1 で居なければ、日を変える前に止まって報告)(全員が家なら時刻の取り違えが通る) | 時刻を 0時や固定値で引く / NpcId と世帯 Id を取り違える | |
| 11 | プレイヤー | 初期: 所持金 2,400・所持品すべて 0・`Location (false, 4)`・行き先 −1・到着 null。「作るもの」3. の例どおり D0T07 と D0T08 の形 | 到着済みの移動を移動中と返す / 到着後も出発地を返す / 行き先を移動中でないときも返す | **核心**: `Player()` の `loc.InTransit ?` を `Player.MoveDestinationDistrictId != -1 ?` に変える(行き先・到着の2か所とも)/ 赤 |
| 12 | 写し | `Player()` を取った後に `BuyFromWindow` で所持金と所持品を変えても、先に取った `PlayerView` の `Funds` と `Inventory` が変わらない。`Households()` の `MemberNpcIds` と `Offers()` も、`Advance` の後に先に取った値が変わらない | `Inventory` を `Array.AsReadOnly` で包んで返す / 世帯の配列をそのまま返す | |
| 13 | Query は書かない | 5つのメソッドすべてを呼んだ前後で `StateHash()` と `EventLog.Count` が一致 | 読み出しの途中で `World` を正規化する(例: 到着済みの移動を書き換える) | |
| 14 | プレイヤーとの行を外す | `RunMetricsOnly` の組み立てで、当日の帳簿に NPC 同士の約定(Purchase / Sale)を置いた World を2つ作り、片方にだけ相手 `PlayerId` の `Purchase` 1行(世帯 A)と `Sale` 1行(世帯 B、単価 × 数量 = v)を足す。`PriceRow` 全件・`DistrictRow` 全件・`TradesRow` が一致し、`HouseholdRow` は B の `SalesValue` と `Profit` だけが v 大きく、`EconomyRow` は `ProfitTotal` だけが v 大きい。**A の Purchase は都市生産品で、A の区画に NPC 同士の約定がある品目にする**(districts.csv の中央値で差が出るように) | Purchase の行を外し忘れる(予算すれすれの売値が約定価格に混ざる)/ Sale の行を HHI・`active_seller_count` に入れる / households.csv からも外す | **核心**: (a) Purchase 分岐の除外を消す / 赤。(b) Sale 分岐で `activeSeller` を立てないようにした条件を消す / 赤 |
| 15 | `player_funds` | `SimHost(M0, 1, metricsSink: 捕捉)` で D0T06 に窓口から1個買い、D1T00 まで `Advance` → day 0 の `EconomyRow.PlayerFunds == 2400 − Payment`、`MoneyTotal == Σ世帯の流動資金`(所持金を足していない)。コマンド無しの走行では毎日 2,400。CSV: economy.csv のヘッダ末尾が `player_funds` で、値が末尾に出る | 列を書き忘れる / 所持金を貨幣総量に足す / 初期値を 0 で書く | |
| 16 | 台本の呼ばれ方 | 「作るもの」5. の例どおり: 時刻を記録する台本で `ticks = 48`、呼ばれた回数 48、時刻が D0T00〜D1T23 の連番、終了時 D2T00 | `Advance` の後に呼ぶ(D0T01 から始まる)/ 最後の1回を落とす / `ticks` 回 + 1 呼ぶ | |
| 17 | 台本なし ≡ 何もしない台本 | 同じシードで `script: null` と何もしない台本を3日回し、`CsvMetricsSink` の5ファイルが1バイト一致し、`StateHash()` も一致 | 台本ありのときだけ別の組み立て・別のシードで `SimHost` を作る / `Advance(1)` の繰り返しで日次の処理を二重に回す | |
| 18 | `vsim run` の組み替え | 移設前の組み方(`WorldGenerator.Generate` + `new SimScheduler(Program.BuildPipeline(...), new RandomSource(seed))`)で同じシード・3日を `CsvMetricsSink` に書いたものと、`vsim run`(`Program.Execute`)の出力が5ファイルとも1バイト一致 | `SimHost` に別の開始日・別のシードを渡す / 計測の sink を渡し忘れる(CSV が空になる) | |
| 19 | 台本の売買が届く | D0T06 に窓口から1個買う台本で1日回す → 台本が D0T07 の `OnHour` で `DrainEvents()` から1件受け取り、economy.csv の day 0 の `player_funds` が減っている | セッションが別の `SimHost` を指す / `DrainEvents` を委譲せず空を返す | |
| 20 | セッションの面 | リフレクションで `IPlayerSession` の公開メンバーが `Now`・`Execute`・`Query`・`DrainEvents` の4つちょうど | `Advance` や `SimHost` を台本に晒す | |
| 21 | ハッシュはイベントを見ない(書き直し) | `PlayerTradeExecuted` を1件 `EventLog` に足してもハッシュが変わらない | `StateHasher` が `EventLog` を読む | |

**核心は5件**(2・4・9・11・14)。14 は変異を2つ当てる。`mutator` が測る。

### レビューで足した条件(フェーズ2。上の表は実装に渡した時点の指示)

| # | 足した条件 | 落ちるべき実装ミス | 出所 |
| - | ---------- | ------------------ | ---- |
| 9a | テスト9 の選んだ日に、**正しい列(世帯ごとの `DisclosedNeeds.Of` の連結)が、(a) 開示される Need 全体を Need Id 昇順に並べた列とも、(b) `World.Needs` を開示される理由で絞っただけの列とも異なる**ことを表明する。シード 1 の60日内で探し、無ければ `SimHost` を作った後に internal の `World.Needs` の並びを入れ替えて前提を作ってよい(`DisclosedNeeds.Of` の結果が変わらない範囲で) | 世帯をまたいで Need Id 順に並べ替える / `World.Needs` の並びをそのまま使う(day 1 は世帯 Id 順・Need Id 順・`World.Needs` 順が一致しやすく、前提が無いと緑で通る) | レビュー1巡 疑い1 |

## 編集してよい文書

- なし(コードとテストのみ)。TDD01 §3.4・§3.5・§3.6・§4.1 はフェーズ1 が本仕様と同じコミットで書いた

## このタスクで特に効く規約

- **列挙順**: `Offers()` は `SortedDictionary` の列挙(`MarketKey` 順)なので順序が保証されている。**`TradableNeeds()` を `World.Needs` の並びから作らない**(`DisclosedNeeds.Of` を世帯ごとに呼ぶ)
- **`Player` を順1〜9 のシステムから読まない。** 例外は順10 の `MetricsSystem` が所持金を写すことだけ(TDD01 §3.2)
- **コマンドは乱数を開かない**(TDD01 §3.4)。イベントの追記も同じ
- `DrainEvents` の読み出し位置は `SimHost` のフィールドであって `World` の状態ではない。`World` に足すとハッシュの凍結一覧(`StateHasherCoverageTests`)が動く

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **「核心」印の変異を `mutator` が実測し(レビューの巡が閉じた後)、結果を doc コメントへ転記した**
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
