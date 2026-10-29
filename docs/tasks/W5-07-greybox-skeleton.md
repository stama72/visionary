# W5-07: グレーボックスの骨格(Godot プロジェクト・時間のポンプ・地図と歩行・NPC の時間割の描画・Godot の CI)

| 項目     | 内容                           |
| -------- | ------------------------------ |
| issue    | #281(#47 を3本に分けた1本目。会話メニューと売買の UI は #282、感謝の演出と日誌は #283) |
| 根拠     | ADR-0001 / ADR-0002 / TDD01 §2・§2.1・§3.5・§3.9・§3.10 / GDD05 §1.1・§1.4・§1.5 / GDD02 §4.3 |
| ブランチ | `feat/281-greybox-skeleton`     |
| worktree | 本体(パイプラインが使う)     |

## スコープ

**含む:** `Visionary.Presentation`(時間のポンプ・1フレームぶんの進め方・居場所の補間・起動引数・表示名)とそのテスト、`Visionary.Game`(Godot 4.7.2 の C# プロジェクト。地図・NPC とプレイヤーの描画・区画のクリックで移動・日時と所持金の表示)、`Visionary.sln` への登録、CI の `godot` ジョブ。

**含まない:** 会話メニュー・売買の UI・時間を止める操作(#282。`TickPump.Paused` は作るが、本タスクでは誰も立てない)、感謝の演出と日誌(#283)、gdUnit4(入れない。TDD01 §3.10)、アート、倍速(GDD05 §6 の未決)、`Visionary.Sim` の変更(**一切触らない**)。

**変えない既存コード**(規則8):

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `SimHost` の `Advance` / `Execute` / `Query` / `DrainEvents` / `StateHash` の契約(時間を進めるのは `Advance` だけ、`Query` は写しを返し World を書かない) | TDD01 §3.5 | 一致 |
| `ReadModels.Player()`(移動中は `Location` = 出発区画・`DestinationDistrictId`・`ArrivalAt`。到着済みは移動していない形) | TDD01 §3.5 | 一致 |
| `PlayerCommandExecutor` の移動(到着 tick = 現在 + 距離 × 1区画あたりの移動時間。移動中・同じ区画は拒否) | TDD01 §3.4 / GDD02 §2.4 | 一致 |
| `Timetable.Of`(居場所と活動。0時ちょうどは前日の計画で答える穴がある) | TDD01 §3.9 / GDD05 §1.5 | 未確認(穴は #268) |
| `District.RowOf` / `ColumnOf` / `Distance`(行優先の 3×3、マンハッタン距離) | GDD02 §4.3 | 一致 |
| `Program.RunHash`(`vsim hash`。`X16` の1行) | TDD01 §4.1 | 一致(本タスクで触らない。CI が突き合わせに使う) |

## 作るもの

### 1. `src/Visionary.Presentation/`(TDD01 §2・§3.10)

`Visionary.Presentation.csproj`: `Microsoft.NET.Sdk`。`Visionary.Sim` を `ProjectReference`。**Godot のパッケージを参照しない**(TDD01 §3.10)。名前空間 `Visionary.Presentation`。他の設定は `Directory.Build.props` に従う。

#### 1a. `TickPump`(TDD01 §3.10「時間のポンプ」、GDD05 §1.1・§1.4)

```csharp
public sealed class TickPump
{
    public const long DefaultMicrosecondsPerTick = 25_000_000;   // 実時間 µs / tick(GDD05 §1.1。1 tick = 25秒、1日 = 10分)

    public TickPump(long microsecondsPerTick = DefaultMicrosecondsPerTick);   // 1 未満は ArgumentOutOfRangeException

    /// 立てている間は Elapse が 0 を返し、端数も増えない(GDD05 §1.4)
    public bool Paused { get; set; }

    /// tick の中の進み。単位: ‰(0〜999)= 端数 × 1000 ÷ microsecondsPerTick(切り捨て。積は long)
    public int ProgressPermille { get; }

    /// 実時間を足し、進める tick の数を返す。呼び出し側がその数だけ SimHost.Advance(1) を呼ぶ
    public int Elapse(long elapsedMicroseconds);             // 負は ArgumentOutOfRangeException
}
```

- 端数(`long`。常に `0 ≤ 端数 < microsecondsPerTick`)だけを状態に持つ。`Paused` なら何もせず 0 を返す。そうでなければ `端数 += elapsed`、`ticks = 端数 / µs`、`端数 %= µs`、`ticks` を返す(`int` へ `checked`)。**上限で丸めない** — 60,000,000 µs 来たら 2 を返す
- **単位がマイクロ秒なのは、フレームごとの丸めでゲームの速さがフレームレートに依存しないためである**(TDD01 §3.10。ミリ秒では 60 Hz で +2% ずれる)
- **`SimHost` を持たない。** tick の数を返すだけで、今が何 tick かは知らない(TDD01 §3.10「端数はゲーム時間ではない」)
- 例(25,000,000 µs): `Elapse(24_999_999)` → 0、進み 999 / 続けて `Elapse(1)` → 1、進み 0 / 続けて `Elapse(60_000_000)` → 2、進み 400 / `Paused = true` で `Elapse(30_000_000)` → 0、進み 400 のまま / `Paused = false` で `Elapse(0)` → 0、進み 400
- **フレームの速さに依らないこと**の例: `Elapse(16_667)` を 1,500 回(60 Hz の 25 秒ぶん、計 25,000,500 µs)→ 合計 1 tick。`Elapse(4_167)` を 6,000 回(240 Hz、計 25,002,000 µs)→ 合計 1 tick。ミリ秒に丸めた旧形(17 / 4)ではそれぞれ 25,500 ms・24,000 ms になり、後者は 1 tick に届かない

#### 1b. `MapPoint` と `MapLayout`(TDD01 §3.10「居場所の補間」、§3.9、GDD02 §4.3)

```csharp
/// 地図座標。単位: 地図単位(区画1つ = 1,000)
public readonly record struct MapPoint(int X, int Y);

public static class MapLayout
{
    public const int DistrictSize = 1000;   // 地図単位 / 区画
    public const int SlotOffset = 250;      // 地図単位。止まっている NPC を区画の中心からずらす量

    public static MapPoint CenterOf(int districtId);
    public static int DistrictAt(MapPoint point);                               // 地図の外は −1
    public static MapPoint Along(int fromDistrictId, int toDistrictId, int traveledUnits);
    public static MapPoint Traveling(int fromDistrictId, int toDistrictId, long elapsedHours,
                                     int progressPermille, int travelHoursPerDistrict);
    public static MapPoint NpcPosition(TimetableEntry entry, int npcId, int progressPermille,
                                       int travelHoursPerDistrict);
    public static MapPoint PlayerPosition(PlayerView player, Tick now, int progressPermille,
                                          int travelHoursPerDistrict);
}
```

- **`CenterOf`** = `(列 × 1000 + 500, 行 × 1000 + 500)`。行・列は `District.RowOf` / `ColumnOf`(範囲外の Id はそちらの例外をそのまま通す)。例: 0 → (500, 500)、5 → (2500, 1500)、8 → (2500, 2500)
- **`DistrictAt`** = `0 ≤ X < 3000` かつ `0 ≤ Y < 3000` なら `(Y / 1000) × 3 + X / 1000`、それ以外は −1。例: (0, 0) → 0、(1000, 999) → 1、(2999, 2999) → 8、(3000, 0) → −1、(−1, 0) → −1
- **`Along`** は L 字(**先に列 = X、次に行 = Y**)。`traveledUnits` を `[0, 距離 × 1000]` に丸める。横の区間の長さを `|Δ列| × 1000` として、丸めた値がそれ以下なら `(始点.X + 符号(Δ列) × traveled, 始点.Y)`、超えたら `(終点.X, 始点.Y + 符号(Δ行) × (traveled − 横の長さ))`。例: 0 → 8 で 1500 → (2000, 500)、2500 → (2500, 1000)、5000 → (2500, 2500)、−10 → (500, 500)。8 → 0 で 1500 → (1000, 2500)
- **`Traveling`** = `Along(from, to, (int)((elapsedHours × 1000 + progressPermille) / travelHoursPerDistrict))`(切り捨て。積は `long`、丸めは `Along` の側)。`travelHoursPerDistrict < 1` は `ArgumentOutOfRangeException`。例: 0 → 2、経過 1・進み 500・1区画 1 時間 → 1500 → (2000, 500)。同じで 1区画 2 時間 → 750 → (1250, 500)
- **`NpcPosition`**: `entry.Activity == Traveling` なら `Traveling(entry.DistrictId, entry.DestinationDistrictId, entry.ElapsedHours, …)`(**ずらさない**)。それ以外(休む・家業・外出先)は `CenterOf(entry.DistrictId)` を `npcId mod 4` でずらす — 0: (−250, −250)、1: (+250, −250)、2: (−250, +250)、3: (+250, +250)。例: 家業・区画 4・npc 6 → (1250, 1750)。外出先・区画 2・npc 1 → (2750, 250)
- **`PlayerPosition`**: `player.Location.InTransit` でなければ `CenterOf(player.Location.DistrictId)`(ずらさない)。移動中なら `距離 = District.Distance(Location.DistrictId, DestinationDistrictId)`、`出発 = ArrivalAt.Value − 距離 × travelHoursPerDistrict`、`経過 = now − 出発`(時間)で `Traveling(Location.DistrictId, DestinationDistrictId, 経過, …)`
- **割り算は切り捨てでよい**(TDD01 §3.10。画面の座標であってシムの計算ではない)。`IntegerMath` を使う必要は無い

#### 1c. `LaunchOptions`(TDD01 §3.10「起動引数と煙試験」、§3.5 の開始日)

```csharp
public sealed record LaunchOptions(long Seed, int StartDay, int? SmokeTicks)
{
    public const long DefaultSeed = 1;
    public static readonly LaunchOptions Default = new(DefaultSeed, 0, null);

    public static bool TryParse(IReadOnlyList<string> args, out LaunchOptions options, out string error);
}
```

- 引数はすべて `--名前=値` の形。名前は `seed`(`long`)・`start-day`(`int`、0 以上)・`smoke-ticks`(`int`、1 以上)の3つ。数値は `NumberStyles.Integer` と `CultureInfo.InvariantCulture` で読む
- 空なら `Default`。**次はすべて `false` と空でない `error`**(`options` は `Default`): 知らない名前(`--foo=1`)・`=` が無い(`--seed`)・数値でない(`--seed=abc`)・範囲外(`--start-day=-1`、`--smoke-ticks=0`)・同じ名前を2回(`--seed=1 --seed=2`)・`--` で始まらない(`seed=1`)

#### 1d. `GameLoop`(TDD01 §3.10「時間のポンプ」「起動引数と煙試験」)

```csharp
/// 1フレームぶんの進め方。Main._Process はこれを呼ぶだけ(通常の起動も煙試験も同じ経路)
public sealed class GameLoop
{
    public GameLoop(SimHost host, TickPump pump);   // null は ArgumentNullException
    public SimHost Host { get; }
    public TickPump Pump { get; }

    /// pump.Elapse(elapsedMicroseconds) の数だけ Host.Advance(1) を呼び、その数を返す
    public int Frame(long elapsedMicroseconds);
}
```

- **`Advance` を呼ぶのはここだけである。** `Game` のノードは `Advance` を直接呼ばない
- **旧 1d の `SmokeRun` は作らない**(#281 の1巡目 I-b-1。煙試験が描画のノードを通らず、TDD01 §3.10 の保証より狭かった)。煙試験は `Main` が通常と同じノードで回す(3)。**`SmokeRun.cs` は消す**

#### 1e. `Labels`(表示名)

```csharp
public static class Labels
{
    public static string Of(Occupation occupation);   // 水車小屋番 / パン屋 / 醸造 / 木材加工 / 鍛冶(Occupation の doc コメントの名)
    public static string Of(Season season);           // 春 / 夏 / 秋 / 冬
    public static string Of(TimetableActivity activity); // 休む / 家業 / 移動中 / 外出先
    public static string Of(CommandStatus status);    // 各値に短い日本語。Accepted は「受け付けた」
}
```

- 知らない値は `ArgumentOutOfRangeException`(`switch` 式の既定の枝)。**`default` で空文字や `ToString()` に落とさない**

### 2. `tests/Visionary.Presentation.Tests/`

`Visionary.Sim.Tests.csproj` と同じパッケージと版(xunit 2.5.3 ほか)。`Visionary.Presentation` を `ProjectReference`。テストは下の表。

### 3. `src/Visionary.Game/`(TDD01 §2・§3.10、ADR-0001)

**Godot 4.7.2(.NET 版)に固定する**(TDD01 §2)。API は Godot 4 系の C# の公式クラスリファレンスに従う(CLAUDE.md)。**Godot 3 系の名前(`Spatial`・`KinematicBody2D`・`yield`・`OS.GetTicksMsec` 前提の時間管理など)を使わない。**

- **`Visionary.Game.csproj`**: `<Project Sdk="Godot.NET.Sdk/4.7.2">`。`RootNamespace` = `Visionary.Game`、`EnableDynamicLoading` = `true`。`Visionary.Presentation` を `ProjectReference`。`TargetFramework` は `Directory.Build.props` の `net8.0` を継ぐ(書かない)。**`dotnet build Visionary.sln -c Release` が警告 0 で通ること**
- **`project.godot`**: `config_version=5`。`[application]` に `config/name="Visionary"`・`run/main_scene="res://Main.tscn"`・`config/features=PackedStringArray("4.7", "C#")`。`[dotnet]` に `project/assembly_name="Visionary.Game"`。`[display]` は 1280 × 720。`[rendering]` は `renderer/rendering_method="gl_compatibility"`
- **`Main.tscn`**: ルートは `Node2D` 1つで、`Main.cs` を付ける。**子ノードはコードで作る**(手書きの `.tscn` を増やさない)
- **`Main.cs`(`public partial class Main : Node2D`)**:
  - `_Ready`: `LaunchOptions.TryParse(OS.GetCmdlineUserArgs(), …)`。失敗なら `GD.PrintErr(error)` → `GetTree().Quit(2)` → return
  - **煙試験でも通常でも同じく**、`SimHost`(`WorldDefinition.M0`, `Seed`, `StartDay`)・`TickPump`・`GameLoop` を作り、地図(`MapView`)と表示(`CanvasLayer` の下の `Label`)を子に足す。煙試験なら目標 tick = `host.Now + SmokeTicks` を覚える
  - `_Process(double delta)`: 経過 = 煙試験なら `TickPump.DefaultMicrosecondsPerTick`(1フレーム = 1 tick)、通常なら `(long)Math.Round(delta * 1_000_000)`。`loop.Frame(経過)` → 表示を更新 → `MapView.QueueRedraw()`。**`delta` を積んでゲーム時間を作らない**(TDD01 §3.10)。**経過を選ぶ1行のほかに、煙試験と通常で枝を分けない**
  - **煙試験の終わり方**(TDD01 §3.10。#281 の2巡目): `Main` は煙試験の開始 tick(`_Ready` で作った直後の `host.Now`)と、回したフレームの数を持つ。`MapView` は `_Draw` が呼ばれた回数を持つ(`public int DrawCount`)
    - `_Process` の**冒頭**で、回したフレームの数が `SmokeTicks` に達していれば: `MapView.DrawCount < SmokeTicks` なら `GD.PrintErr` → `GetTree().Quit(1)`。そうでなければ `GD.Print("VISIONARY_SMOKE_HASH=" + host.StateHash().ToString("X16", CultureInfo.InvariantCulture))` → `GetTree().Quit(0)`。どちらも return し、以後のフレームでは何もしない
    - そうでなければ `loop.Frame(定数)` の後でフレームの数を1増やし、**`host.Now != 開始 tick + フレームの数` なら** `GD.PrintErr`(期待値と実際の値)→ `GetTree().Quit(1)` → return
    - **tick(`host.Now ≥ 目標`)で終えない。** 1フレームに余計な `Advance` が紛れても割り切れる回数で止まって緑になる
    - 煙試験中の `_Process` と `MapView._Draw` の例外は捕まえて `GD.PrintErr` → `GetTree().Quit(1)`
  - `_Ready` の例外(煙試験時)も `GetTree().Quit(1)`
  - `_UnhandledInput(InputEvent @event)`: 左ボタンの押下(`InputEventMouseButton`、`Pressed`、`ButtonIndex == MouseButton.Left`)なら、画面座標を地図単位へ戻し(下の倍率の逆)、`MapLayout.DistrictAt` が 0 以上なら `host.Execute(new MoveCommand(id))`。結果の `Status` を `Labels.Of` で表示に残す(`Accepted` なら到着の時刻も)
- **`MapView.cs`(`public partial class MapView : Node2D`)**: `_Draw` で描く。**地図単位 → 画素は `画素 = 原点 + 地図単位 × 1/5`**(区画 = 200 px、原点 (40, 80))。定数に単位のコメントを付ける
  - 9 区画: 枠付きの矩形(区画どうしに 4 px の隙間)。区画の中に `区画 {Id}` と、そこに住む世帯の職業名(`Query.Households()` の `DistrictId` と `Labels.Of`)。区画 4 には「窓口」も書く。文字は `ThemeDB.FallbackFont`
  - NPC: `Query.Households()` の全世帯の全 `MemberNpcIds` について `MapLayout.NpcPosition(Query.TimetableOf(npcId), npcId, pump.ProgressPermille, TravelHoursPerDistrict)` の位置に半径 8 px の円。色は活動で4色
  - プレイヤー: `MapLayout.PlayerPosition(Query.Player(), host.Now, pump.ProgressPermille, TravelHoursPerDistrict)` に半径 12 px の円(NPC と違う色)。移動中は行き先の区画の枠を強調する
- **表示(`Label`)**: `GameDate.FromTick(host.Now)` から `{Year}年 {Labels.Of(Season)} {DayOfSeason}日 {HourOfDay}時`、`所持金 {Funds}`、居場所(区画 Id。移動中なら `→ 行き先 Id(到着 {時}時)`)、最後の移動コマンドの結果

### 4. `Visionary.sln`

`Visionary.Presentation`(src)・`Visionary.Presentation.Tests`(tests)・`Visionary.Game`(src)を登録する。`Visionary.Game` の Release の構成は、Godot.NET.Sdk が受ける構成名に合わせてよい(合わせ方は決めて報告)。**`dotnet test Visionary.sln -c Release` が全プロジェクトを組んで通ること。**

### 5. CI の `godot` ジョブ(TDD01 §2.1・§3.10)

`.github/workflows/ci.yml` に足す。冒頭コメントの「Godot側は…別ジョブで追加する」を、このジョブを指す文に直す。

```yaml
  godot:
    name: Godot (headless smoke)
    runs-on: ubuntu-latest
    timeout-minutes: 15
    env:
      GODOT_VERSION: 4.7.2   # TDD01 §2 の固定。上げるときは csproj の Sdk と同時に
    steps:
      - checkout / setup-dotnet(global-json-file)
      - Godot の取得: https://github.com/godotengine/godot/releases/download/${GODOT_VERSION}-stable/Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64.zip
        を actions/cache(キーは版)に置いて展開する
      - dotnet build src/Visionary.Game/Visionary.Game.csproj -c Debug
      - godot --headless --path src/Visionary.Game --import
      - 煙試験: godot --headless --path src/Visionary.Game -- --seed=20260904 --smoke-ticks=720
        の出力から VISIONARY_SMOKE_HASH= の行を取り、終了コードが 0 であること
      - 参照: dotnet run --project src/Visionary.Sim.Runner -c Release -- hash --seed 20260904 --ticks 720
      - 2つが一致しなければ ::error:: を出して落とす。煙試験の行が無いときも落とす
```

既存の `sim` ジョブの決定論チェックと同じシード・tick 数を使う。

## 落ちるべき条件(テスト)

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心(当てる変異 / 期待) |
| - | ------ | -------- | -------------------- | ------------------------- |
| 1 | `TickPump` の閾値 | 1a の例の前半: 24,999,999 → 0・999 / 1 → 1・0 | `>` と `>=` の取り違え、進みの式の誤り | |
| 2 | `TickPump` の複数 tick | 60,000,000 → 2・400 | 1 回に 1 tick までに丸める、端数を捨てる | |
| 2b | `TickPump` がフレームの速さに依らない | 1a の 60 Hz・240 Hz の例がどちらも合計 1 tick | ミリ秒未満を捨てる(単位をミリ秒に戻すのと同じ) | **核心**: `Elapse` の先頭で `elapsed = elapsed / 1000 * 1000` とする / 赤(両方の例) |
| 3 | `TickPump` の停止 | `Paused` 中の 30,000,000 → 0・進み不変、解除後の `Elapse(0)` → 0 | 止めている間も端数を積む(再開で一気に進む) | **核心**: `Elapse` の `Paused` の早期 return を消す / 赤 |
| 4 | `TickPump` の入力検査 | 負の経過・1 未満の ms で例外 | 検査漏れ | |
| 5 | `CenterOf` | 0・5・8 の例 | 行と列の取り違え(5 が (1500, 2500) になる) | |
| 6 | `DistrictAt` | 1b の5例 | 境界(3000・−1)の取り違え、行と列の取り違え | |
| 7 | `Along` | 0 → 8 の4例と 8 → 0 の1例 | 縦を先に進む、逆向きの符号、丸め漏れ | **核心**: 横と縦の順を入れ替える(先に行 = Y)/ 赤。**核心**: `符号(Δ列)` を常に +1 にする / 赤 |
| 8 | `Traveling` | 1区画 1 時間と 2 時間の例 | `travelHoursPerDistrict` で割らない | **核心**: 割り算を消す / 赤(2 時間の例) |
| 9 | `NpcPosition` | 家業・外出先のずらし、移動中はずらさず `Traveling` と一致 | 移動中にもずらす、外出先を自区画に置く | |
| 10 | `PlayerPosition`(SimHost を通す) | seed 1 の `SimHost` を 8 tick 進め、プレイヤーの区画から最も遠い区画(同点は小さい Id)へ `MoveCommand`、`Advance(1)` 後の進み 0 で `Along(出発, 行き先, 1000)` と一致。さらに距離 − 1 tick 進めて `CenterOf(行き先)` と一致 | 出発 tick を到着 tick と取り違える、移動中の判定を `DestinationDistrictId` で行う | |
| 11 | `LaunchOptions` | 空・正常3つ・1c の誤り7種 | 範囲検査の漏れ、重複を後勝ちで通す、知らない名前を無視する | |
| 12 | `GameLoop` が tick だけ進める | seed 20260904 の `GameLoop` に `Frame(DefaultMicrosecondsPerTick)` を 720 回、毎回の後に全 NPC とプレイヤーの居場所を計算(描画と同じ読み出し)し、`StateHash()` が別に作った `SimHost` を `Advance(720)` したものと一致。`Frame(60_000_000)` は 2 を返し `Now` が 2 進む | 1フレームの `Advance` の数がずれる、読み出しが World を書く | **核心**: `Frame` の中で `Host.Advance(1)` をもう1回呼ぶ / 赤 |
| 13 | Godot を参照しない | `Visionary.Presentation` のアセンブリの参照に名前が `Godot` で始まるものが無い | `GodotSharp` を参照に足す | |
| 14 | `Labels` の網羅 | 4 つの列挙の全値で空でない名を返し、同じ列挙の中で重複しない | 列挙に値を足して名を足し忘れる | |

**表に無い保護**: `Visionary.Game` のノードは xUnit で踏まない。**コンパイル(警告 0)と CI の `godot` ジョブの煙試験だけが守る。** 煙試験は1フレームの `Advance` の数(毎フレーム `Now` を確かめる)と `_Draw` の回数を自分で確かめる。**この検査は `mutator` では測らない**(パイプラインは Godot を叩けない)。フェーズ1 が DONE の後に、`Main` の `_Process` で `loop.Frame` を2回呼ぶ変異を noz72 の Godot で当てて終了コード 1 を確かめる。 煙試験は通常と同じ `_Ready` → `_Process` → `MapView._Draw` を通る(TDD01 §3.10)。**踏まないのは `delta` をマイクロ秒に直す1行と、クリックの経路(`_UnhandledInput` → `Execute`)である。**

## 配線(規則7)

`Presentation` の部品は `Game` からしか呼ばれない。xUnit が踏むのは #10・#12 だけで、煙試験は `delta` の変換を踏まないので、次をレビューで見る。

- **入力の作り方**: `GameLoop.Frame` に渡すのは `_Process` の `delta`(秒)× 1,000,000 の四捨五入(µs。煙試験では定数)。`travelHoursPerDistrict` は `WorldDefinition.M0.TravelHoursPerDistrict`(`SimHost` に渡した定義と同じもの)。NPC の Id は `Query.Households()` の `MemberNpcIds`
- **単位の約束**: 地図単位(区画 = 1000)と画素(区画 = 200 px)を混ぜない。`ProgressPermille` は ‰
- **呼び出し順**: 1フレームの中で `GameLoop.Frame`(`Elapse` → `Advance(1)` × n)→ 表示の更新 → `QueueRedraw` → 次の `_Draw` で位置の計算(進みは `Elapse` の後の値)。煙試験も同じ順
- **終了コード**: 引数の誤り 2・煙試験の例外 1・煙試験の成功 0

## 別表: 開発者のプレイからの変更(2026-10-29、PR #284 の上)

**止まっている NPC の置き方を「`npcId mod 4` の4隅」から「区画の下詰め」に変える**(TDD01 §3.10「居場所の補間」)。上の 1b・テスト #9 の例はこの表で置き換える。

- `MapLayout.SlotOffset` と `NpcPosition` の `npcId` 引数を消し、次にする:

```csharp
public const int SlotSpacing = 250;   // 地図単位。下詰めの並びの間隔
public const int SlotsPerRow = 4;     // 1段に並べる人数

/// 区画 districtId に止まっている者の順位 slotIndex(0 始まり)の位置。下詰め・左詰め、4人で1段
public static MapPoint StationaryPosition(int districtId, int slotIndex);   // 負は ArgumentOutOfRangeException
/// Traveling ならずらさない。それ以外は StationaryPosition(entry.DistrictId, slotIndex)
public static MapPoint NpcPosition(TimetableEntry entry, int slotIndex, int progressPermille, int travelHoursPerDistrict);
/// entries[k] は npcIds[k] の時間割。止まっている者に、同じ区画の中で NpcId 昇順の順位を返す。Traveling は −1
public static int[] StationarySlots(IReadOnlyList<int> npcIds, IReadOnlyList<TimetableEntry> entries);   // 長さ違いは ArgumentException
```

- `StationaryPosition` = `(列 × 1000 + 125 + (i mod 4) × 250, 行 × 1000 + 875 − (i ÷ 4) × 250)`。例: 区画 4・順位 0 → (1125, 1875)、順位 3 → (1875, 1875)、順位 4 → (1125, 1625)。区画 0・順位 1 → (375, 875)
- `StationarySlots` の例: npcIds = [5, 2, 9, 7]、区画 = [3, 3, 3, 1]、活動 = [家業, 休む, 移動中, 外出先] → [1, 0, −1, 0](npc 9 は移動中なので数えず、区画 3 の 2 → 0・5 → 1)。**入力の並びではなく NpcId で数える**
- `MapView._Draw` と `GameLoopTests` の読み出しは、全 NPC の時間割を先に集めて `StationarySlots` で順位を出し、`NpcPosition(entry, slot, …)` を呼ぶ

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心 |
| - | ------ | -------- | -------------------- | ---- |
| 9' | `StationaryPosition` | 上の4例 | 上詰めにする、段の折り返しの誤り、行と列の取り違え | |
| 9'' | `StationarySlots` | 上の例。入力の順を入れ替えても同じ順位 | 入力の並びで数える、移動中を数える、区画をまたいで数える | **核心**: 区画の一致の条件を消す / 赤 |
| 9''' | `NpcPosition` | 家業・外出先は `StationaryPosition` と一致、移動中は `Traveling` と一致(順位に依らない) | 移動中にずらす | |

## 編集してよい文書

- なし(コードとテスト、`Visionary.sln`、`.github/workflows/ci.yml` のみ)

## このタスクで特に効く規約

- **描画側でゲーム時間を作らない**(ADR-0002・TDD01 §3.10)。今の tick は `host.Now` から読む。`delta` を積むのは `TickPump` の端数だけ
- **`Visionary.Sim` を変更しない。** 足りない読み出し口に気付いたら止まって報告する(タスクの外から参照される API に触れる判断)

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **「核心」印の変異を `mutator` が実測し(レビューの巡が閉じた後)、結果を doc コメントへ転記した**
- [ ] `dotnet build Visionary.sln -c Release` が警告0(`Visionary.Game` を含む)
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
- [ ] (フェーズ1 が DONE の後に確かめる)CI の `godot` ジョブが緑
