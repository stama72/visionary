# W5-04: プレイヤーの区分とコマンド(SimHost の入力口)

| 項目     | 内容                           |
| -------- | ------------------------------ |
| issue    | #263(決定1〜4 はコメント [6469563316](https://github.com/stama72/visionary/issues/263#issuecomment-6469563316)) |
| 根拠     | TDD01 §3.2・§3.4・§3.5・§3.8 / GDD02 §2.4「プレイヤー」/ GDD06 §6.1〜§6.4 / GDD02d §4.1 |
| ブランチ | `feat/263-simhost`             |
| worktree | 本体(パイプラインが使う)     |

## スコープ

**含む:** `World.Player` 区分とハッシュ、`SimHost`(`Advance` / `Execute` / 先回し)、M0 の6つのコマンドと `Result`、パイプラインの組み立ての移設、`MoneyChangesOnlyByTheExternalLedger` の書き直し。

**含まない(#275 へ):** `Query` / `DrainEvents` / `DomainEvent` の作り直し / `Metrics` のプレイヤー除外と `economy.csv` の所持金の列 / Runner の台本の口。**Godot 側は #47。**

**変えない既存コード**(規則8):

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `PlayerSale.Execute` の Need の当て方(工具切れ → 生産停止 → 遠方在庫)と、行の選び方 | GDD06 §6.1 の表と箇条 | 一致 |
| `PlayerSale.Execute` の1日1回(Need が無いときは数えない・予算と比べる前に記録する) | GDD06 §6.1「1日1回に数えるのは…」 | 一致 |
| `PlayerSale.Execute` の数量 `min(提示数量, Need の数量, FloorDiv(用途に使える資金, 価格))` | GDD06 §6.1 表「数量」 | 一致 |
| `TradeSettlement.ExecutePlayerSale` の帳簿(買い手側1行・相手 = `PlayerId`)と仕入れ移動平均の更新条件 | TDD01 §3.2 / #261 決定3 | 一致 |
| `ExternalMarket.TryOfferPrice`(外部売値。1次産品だけ季節係数)と `WorldDefinition.ExternalBuyPrice`(都市生産品だけ) | GDD02d §2.1・§5 / GDD06 §6.3 | 一致 |
| `DisclosedNeeds.Of`(開示は生産停止・工具切れ・遠方在庫の3理由) | GDD01 §6.1 | 一致 |
| `SellableStock.Of`(販売在庫 = 工房在庫 − 留保量) | GDD02c §1.3 | 未確認 |
| `MetricsSystem` の都市内の約定の数え方(相手が窓口でなければ都市内) | TDD01 §3.2「`Metrics` の都市内の約定からは、相手 Id で外す」 | **不一致 → #275**。本タスクの後、`SellToHousehold` / `BuyFromHousehold` の約定が prices.csv・districts.csv・trades.csv に混ざる。Runner はまだプレイヤーを動かさない(#275・#264)ので、M0 の比較実験の CSV は変わらない |

## 作るもの

### 1. `PlayerState` と `World.Player`(TDD01 §3.2 / GDD02 §2.4)

`src/Visionary.Sim/World/PlayerState.cs`。`sealed class`(`HouseholdState` と同じくプレーンデータ。ロジックを持たせない — 例外は下の `LocationAt` だけで、純関数である)。

| メンバー | 型 | 意味 |
| -------- | -- | ---- |
| `Funds` | `int`(`internal set`) | 所持金。単位: 貨幣 |
| `Inventory` | `int[]`(長さ = 品目数) | 所持品。単位: 個。添字 = 品目 Id |
| `DistrictId` | `int`(`internal set`) | 最後に居た区画。移動中なら出発した区画 |
| `MoveDestinationDistrictId` | `int`(`internal set`) | 移動中の行き先。**移動していなければ −1** |
| `MoveArrivalTick` | `long`(`internal set`) | 到着 tick の値(`Tick.Value`)。移動していなければ 0 |
| `LocationAt(Tick now)` | `PlayerLocation` を返す | 下記 |

```csharp
public readonly record struct PlayerLocation(bool InTransit, int DistrictId);
```

`LocationAt(now)`:
- `MoveDestinationDistrictId == -1` → `(false, DistrictId)`
- それ以外で `now.Value >= MoveArrivalTick` → `(false, MoveDestinationDistrictId)`(到着済み)
- それ以外 → `(true, DistrictId)`(移動中。`DistrictId` は出発地)

**状態を到着時に書き換えない**(到着を処理するシステムを作らない — 順1〜9 は `Player` を読まない。TDD01 §3.2)。到着済みの状態は、次の移動コマンドが正規化する(下の 4.1)。

- `World` のコンストラクタで `Player = new PlayerState(itemCount)` を作る。初期値: 所持金 0・所持品すべて 0・`DistrictId = District.ExternalMarketDistrictId`(4)・行き先 −1・到着 0
- `WorldGenerator.Generate` が `world.Player.Funds = WorldDefinition.PlayerInitialFunds` を書く
- `WorldDefinition` に `public const int PlayerInitialFunds = 2400; // 単位: 貨幣(GDD02 §2.4。世帯の初期資金とは別の定数)`。**コンストラクタの引数にしない**(#263 の「決めて報告」。`WorkdayStartHour` と同じ扱い)
- **プレイヤーの Id は既存の `HouseholdState.PlayerId`(`int.MaxValue − 1`)を使う。** 新しく定数を作らない

### 2. ハッシュ(TDD01 §3.8)

`StateHasher` の `Section` に `Player = 10` を足し、`Households` の後に書く。要素数は 1。中身は次の順で固定する: `Funds`(int)・`Inventory.Length`(int)・`Inventory` の各要素(int、品目 Id 昇順)・`DistrictId`(int)・`MoveDestinationDistrictId`(int)・`MoveArrivalTick`(long)。

### 3. パイプラインの組み立ての移設(TDD01 §3.5)

- `src/Visionary.Sim/Systems/SimPipeline.cs`: `public static ISimSystem[] Build(WorldDefinition definition, IDailyMetricsSink sink)`。中身は今の `Program.BuildPipeline` と同じ並び(順1〜5朝・5夕・10)
- `Program.BuildPipeline` は残し、本体を `SimPipeline.Build(definition, sink)` の1行にする(既存のテストが呼んでいるため)
- `src/Visionary.Sim/Metrics/DiscardDailyMetricsSink.cs`: `internal sealed class`。何もしない `IDailyMetricsSink`(Runner の `NullDailyMetricsSink` と同じ。Runner 側は変えない)

### 4. コマンドと `Result`(TDD01 §3.4・§3.5)

名前空間 `Visionary.Sim.Commands`、置き場所 `src/Visionary.Sim/Commands/`。

```csharp
public interface ICommand { }   // マーカー。SimHost が型で振り分ける

public sealed record MoveCommand(int DestinationDistrictId) : ICommand;
public sealed record AskNeedsCommand(int HouseholdId) : ICommand;
public sealed record SellToHouseholdCommand(int HouseholdId, int ItemId, int UnitPrice, int Quantity) : ICommand;
public sealed record BuyFromHouseholdCommand(int HouseholdId, int ItemId, int Quantity) : ICommand;
public sealed record BuyFromWindowCommand(int ItemId, int Quantity) : ICommand;
public sealed record SellToWindowCommand(int ItemId, int Quantity) : ICommand;
```

```csharp
public enum CommandStatus
{
    Accepted = 1,
    InvalidArgument = 2,      // Id が範囲外・数量 < 1・価格 < 1
    InTransit = 3,            // 移動中
    OutsideTradingHours = 4,  // 6〜17時の外(移動には掛けない)
    NotPresent = 5,           // 居場所が相手の世帯の区画(窓口なら中心)でない
    AlreadyThere = 6,         // 移動: 行き先が今の居場所
    NoOffer = 7,              // 世帯から買う: Market にその (品目, 世帯) の提示価格が無い
    SellerOutOfStock = 8,     // 世帯から買う: 販売在庫が 0
    InsufficientFunds = 9,    // 買う: 1個も払えない
    NotEnoughGoods = 10,      // 売る: 所持品がその品目を 0 個しか持たない
    WindowDoesNotBuy = 11,    // 窓口へ売る: 1次産品(外部買値を持たない)
    NoNeed = 12,              // 世帯へ売る: PlayerSaleOutcome.NoNeed
    AlreadyOfferedToday = 13, // 世帯へ売る: PlayerSaleOutcome.AlreadyOfferedToday
    OverBudget = 14,          // 世帯へ売る: PlayerSaleOutcome.OverBudget
}

public sealed record Result
{
    public CommandStatus Status { get; init; }
    public int Quantity { get; init; }          // 約定した個数。Accepted の売買以外は 0
    public int UnitPrice { get; init; }         // 約定単価。Accepted の売買以外は 0
    public int Payment { get; init; }           // Quantity × UnitPrice(long で積んで checked)。同上
    public Gratitude? Gratitude { get; init; }  // 世帯へ売って Accepted のときだけ
    public int NeedId { get; init; } = -1;      // 世帯へ売る: PlayerSaleResult.NeedId をそのまま
    public IReadOnlyList<Need> DisclosedNeeds { get; init; } = Array.Empty<Need>(); // Need を聞く: Accepted のときだけ
    public Tick? ArrivalAt { get; init; }       // 移動: Accepted のときだけ
}
```

**検査の順序は仕様である**(すべてのコマンドに共通。前の段で拒否したら後ろは見ない。拒否は World を一切書かない):

1. 引数の検査 → `InvalidArgument`(区画 Id 0〜8・世帯 Id・品目 Id が範囲外、数量 < 1、価格 < 1)
2. 移動中か(`Player.LocationAt(Now).InTransit`)→ `InTransit`
3. **移動以外**: 時刻が `WorldDefinition.WorkdayStartHour` ≤ `Now.HourOfDay` < `WorldDefinition.EveningTradeHour` でない → `OutsideTradingHours`
4. **移動以外**: 居場所(`LocationAt(Now).DistrictId`)が、世帯相手なら `world.Households[h].DistrictId`、窓口相手なら `District.ExternalMarketDistrictId` でない → `NotPresent`
5. コマンドごとの段(下記)

例(規則6):`Now` が D0T05 → `OutsideTradingHours`。D0T06 と D0T17 → 通る。D0T18 → `OutsideTradingHours`。

#### 4.1 移動(GDD02 §2.4・#263 決定2)

段3・4 は無い。段5: 行き先 = `LocationAt(Now).DistrictId` → `AlreadyThere`。それ以外は Accepted で、

```
DistrictId               = LocationAt(Now).DistrictId   // 到着済みの移動をここで正規化する
MoveDestinationDistrictId = 行き先
MoveArrivalTick          = Now.Value + District.Distance(DistrictId, 行き先) × definition.TravelHoursPerDistrict
ArrivalAt                = new Tick(MoveArrivalTick)
```

例(規則6): `Now` = D0T06、中心(4)→ 区画 0(距離 2、1区画 1時間)。到着 = D0T08。D0T07 に `AskNeeds` → `InTransit`。D0T08 に区画 0 の世帯へ `AskNeeds` → 段2・4 を通る。D0T08 に再び移動 → 段2 を通り、`DistrictId` が 0 に正規化されてから新しい移動が書かれる。

**時間を進めない。** 時間は `SimHost.Advance` だけが進める(TDD01 §3.4)。

#### 4.2 Need を聞く(#263 決定4)

段5: 無い。Accepted で `DisclosedNeeds = DisclosedNeeds.Of(world, householdId)`。**World を書かない。** 台詞は選ばない。

#### 4.3 世帯へ売る(GDD06 §6.1)

段5:
1. `q = min(Quantity, Player.Inventory[item])`。`Player.Inventory[item] == 0` → `NotEnoughGoods`(`PlayerSale.Execute` を呼ばない — 1日1回に数えない)
2. `PlayerSale.Execute(world, householdId, itemId, unitPrice, q)` を呼ぶ。`NoNeed` / `AlreadyOfferedToday` / `OverBudget` は同名の `CommandStatus` に写す(`NeedId` も写す)
3. Accepted なら `Player.Funds += Payment`、`Player.Inventory[item] -= Quantity`(**`PlayerSale` の結果の数量**であって `q` ではない)。`Gratitude` を写す

`PlayerSale` のインスタンスは `SimHost` が1つ持つ(`new PlayerSale(definition)`)。

#### 4.4 世帯から買う(GDD06 §6.2)

段5:
1. `world.Market` に `new MarketKey(itemId, householdId)` が無い → `NoOffer`
2. `stock = SellableStock.Of(definition, household, itemId)`。0 → `SellerOutOfStock`
3. `price` = その提示価格。`affordable = IntegerMath.FloorDiv(Player.Funds, price)`。0 → `InsufficientFunds`
4. `q = min(Quantity, stock, affordable)`
5. `TradeSettlement.ExecutePlayerPurchase(world, household, itemId, q, price, SellableStock.ReserveQuantity(definition, household, itemId))` を**新設して**呼ぶ。中身は `ExecuteExport` と同じ形で、相手 Id だけ `HouseholdState.PlayerId`(売り手側だけ動かす: 流動資金 +、工房在庫 −、帳簿に `Sale` を1行、`OccurredAt = world.Now`、留保の番人も同じ)。仕入れ移動平均は更新しない
6. `Player.Funds -= q × price`、`Player.Inventory[item] += q`

**観測を作らない**(GDD06 §6.2)。`Market` も書かない。

#### 4.5 窓口から買う(GDD06 §6.3)

段5: `price` = `ExternalMarket.TryOfferPrice(definition, Now, itemId, out price)`(外部売値。全品目)。`affordable = FloorDiv(Player.Funds, price)`。0 → `InsufficientFunds`。`q = min(Quantity, affordable)`。`Player.Funds -= q × price`、`Player.Inventory[item] += q`。**帳簿に何も書かない**(GDD06 §6.3)。

#### 4.6 窓口へ売る(GDD06 §6.3)

段5: `definition.IsPrimaryItem(itemId)` → `WindowDoesNotBuy`。`Player.Inventory[item] == 0` → `NotEnoughGoods`。`price = definition.ExternalBuyPrice(itemId)`。`q = min(Quantity, Player.Inventory[item])`。`Player.Funds += q × price`、`Player.Inventory[item] -= q`。**帳簿に何も書かない。**

**全コマンド共通**: 乱数を開かない(TDD01 §3.4)。所持金・支払額の積は long で取り `checked` で int へ戻す。

### 5. `SimHost`(TDD01 §3.5)

`src/Visionary.Sim/SimHost.cs`(名前空間 `Visionary.Sim`)。

```csharp
public sealed class SimHost
{
    public SimHost(WorldDefinition definition, long masterSeed, int startDay = 0, IDailyMetricsSink? metricsSink = null);
    public Tick Now { get; }                 // = World.Now
    public void Advance(int ticks);          // SimScheduler.Advance にそのまま渡す(ticks ≤ 0 は ArgumentOutOfRangeException)
    public Result Execute(ICommand command); // null → ArgumentNullException。未知の型 → ArgumentException
    public ulong StateHash();                // StateHasher.Compute(World)
    internal World World { get; }            // テスト用。読み出しの公開口は #275 の Query
}
```

- 生成: `world = WorldGenerator.Generate(definition, new RandomSource(masterSeed))`、`scheduler = new SimScheduler(SimPipeline.Build(definition, metricsSink ?? new DiscardDailyMetricsSink()), new RandomSource(masterSeed))`(Runner の `RunHash` と同じ組み方)。`startDay < 0` → `ArgumentOutOfRangeException`。`startDay > 0` なら `Advance(checked(startDay × 24))`
- 例(規則6): `new SimHost(M0, 1, startDay: 15).Now == Tick.FromDays(15)`(D15T00)。その `StateHash()` は `new SimHost(M0, 1)` に `Advance(360)` したものと一致する
- **`InternalsVisibleTo("Visionary.Sim.Tests")` を `Visionary.Sim` に足す**(`src/Visionary.Sim/AssemblyInfo.cs` を新設。Runner と同じ書き方)
- 検証エラー(上の段1〜5)は Result で返す。**例外は呼び出し側の誤り(null・未知の型・`Advance` の 0 以下)とモデルの番人(`PlayerSale` / `TradeSettlement` の `InvalidOperationException`)だけ**

### 6. 恒等式テストの書き直し(GDD02d §4.1・#260 追随表・#261 全般アドバイザー指摘1)

`MoneyChangesOnlyByTheExternalLedger` を `SimHost` を通す形に書き換える(テスト名は変えない)。台本(テストの中に書く。シード 1・開始日 0・60日):

各日 d について:
1. `Advance(6)`(D d T06。この時点で居場所は中心)
2. 世帯 Id 昇順に `DisclosedNeeds.Of(world, h)` を見て、最初の Need(Id 昇順の先頭)を (h, 品目, 数量) とする。無ければ 3 へ
   - `BuyFromWindow(品目, min(数量, 3))`(窓口は全品目を売る)
   - 世帯 h の区画が中心でなければ `Move(その区画)` し、`Advance(到着 − Now)`
   - `AskNeeds(h)`(Accepted であることを表明する)
   - `SellToHousehold(h, 品目, 価格 1, 所持品の数)`
3. 今の区画に居る世帯を Id 昇順に見て、`Market` に自分の出力品目の提示価格があり販売在庫が 1 以上の最初の世帯 s から `BuyFromHousehold(s, その品目, 1)`
4. 中心に居なければ `Move(4)` して `Advance(到着 − Now)`。所持品のうち都市生産品をすべて `SellToWindow`
5. 次の日の 0時まで `Advance`
6. **毎日の終わり(D d+1 T00)に表明する**:
   `(Σ世帯の流動資金 − 初日の Σ) + (所持金 − 2,400) == (帳簿の世帯の輸出 − 輸入) + (窓口へ売った Payment の累計 − 窓口から買った Payment の累計)`。世帯の輸出入の数え方は今のテストのまま(相手 = `ExternalMarketSellerId` の行)

走行の後に表明する: 4方向(窓口から買う・窓口へ売る・世帯から買う・世帯へ売る)の **Accepted がそれぞれ 1件以上**。0件のまま緑になると、その方向の項を誰も見ないまま残る(#261 全般アドバイザー指摘1)。**0件になったら、台本を変える前に止まって報告する**(値の問題か規則の問題かを設計側で見る)。

## 落ちるべき条件(テスト)

テストファイル: `tests/Visionary.Sim.Tests/Commands/PlayerCommandTests.cs`(1〜20)、`tests/Visionary.Sim.Tests/SimHostTests.cs`(21〜25)、ハッシュは既存の `StateHasherTests.cs`(26〜27)、恒等式は既存の `TradePipelineTests.cs`(28)。**テストの前提状態は `SimHost` を作ってから internal の `World` を書いて作ってよい**(居場所・所持品・時刻)。時刻は `Advance` で進める(`World.Now` を直接書かない)。

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心(当てる変異 / 期待) |
| - | ------ | -------- | -------------------- | ------------------------- |
| 1 | 移動の到着 tick | D0T06 に 4→0。`ArrivalAt` = D0T08、`MoveArrivalTick` = 8、行き先 0、`DistrictId` 4 のまま | 距離を掛け忘れる / 往復(×2)で数える / 1区画あたりの時間を掛け忘れる | |
| 2 | 移動中の拒否 | 1 の後 D0T07 に区画 0 の世帯へ `AskNeeds` → `InTransit`。D0T08 に → Accepted | 到着の比較を `>` にする(D0T08 に `InTransit`)/ 移動中を見ずに `DistrictId` だけで居合わせを判定する(出発地の世帯と取引できる) | **核心**: `LocationAt` の `now.Value >= MoveArrivalTick` を `now.Value > MoveArrivalTick` に / 赤 |
| 3 | 移動中の出発地 | 1 の後 D0T07 に中心の窓口から `BuyFromWindow` → `InTransit`(Accepted ではない) | 移動中に出発地で取引できる | **核心**: 検査の段2(`InTransit`)を消す / 赤 |
| 4 | 移動中に移動 | 1 の後 D0T07 に `Move(8)` → `InTransit`、状態は変わらない | 移動中の付け替えを許す | |
| 5 | 移動の正規化 | 1 の後 D0T08 に `Move(8)` → Accepted、`DistrictId` = 0、到着 = 8 + 距離(0→8)4 = D0T12 | 正規化を忘れて出発地(4)から距離を測る(4→8 は距離 2 で D0T10 になる)。**行き先は 8 でなければならない** — 1 や 2 だと 0 からも 4 からも距離が同じで、このミスが落ちない | |
| 6 | 同じ区画への移動 | 中心で `Move(4)` → `AlreadyThere`、状態は変わらない | 距離 0 の移動を Accepted にする | |
| 7 | 移動は時間帯を見ない | D0T02 と D0T20 に `Move` → Accepted | 移動にも段3 を掛ける | |
| 8 | 時間帯の境界 | 中心で `BuyFromWindow` を D0T05・D0T06・D0T17・D0T18 に → 拒否・通る・通る・拒否 | `<=` と `<` の取り違え / 定数を使わず 6・18 を直書きして片方をずらす | **核心**: 段3 の `< EveningTradeHour` を `<= EveningTradeHour` に / 赤 |
| 9 | 居合わせ | 中心に居て、区画 ≠ 4 の世帯へ `AskNeeds` → `NotPresent`。その区画へ移動後 → Accepted | 居合わせを見ない / 窓口の区画と世帯の区画を取り違える | |
| 10 | 検査の順序 | 移動中かつ D0T20 に `AskNeeds` → `InTransit`(`OutsideTradingHours` ではない)。範囲外の世帯 Id を D0T20 に → `InvalidArgument` | 段の順序の入れ替え | |
| 11 | Need を聞くは書かない | Accepted の前後で `StateHash()` が一致し、`DisclosedNeeds` が `DisclosedNeeds.Of` と同じ並び | `AskNeeds` が World を書く(例: 1日1回の記録を付ける)/ 自前で絞り込み困窮を混ぜる | |
| 12 | 世帯へ売る: 成立 | 開示 Need のある世帯に、所持品を持って価格 1 で売る → Accepted。所持金が `Payment` 増え、所持品が `Quantity` 減り、`Gratitude` が入る。世帯側の変化は `PlayerSale.Execute` を直接呼んだときと一致 | 所持金・所持品の片方を動かし忘れる / `q` で所持品を減らす(Need の数量で切られたとき食い違う) | |
| 13 | 世帯へ売る: 所持品 0 | 所持品 0 で売る → `NotEnoughGoods`。同じ日に所持品を持たせて売る → `AlreadyOfferedToday` ではなく判定まで進む | 所持品の検査を `PlayerSale` の後に置き、1日1回を消費する | |
| 14 | 世帯へ売る: 写し | Need の無い世帯 → `NoNeed`。同じ日に2回 → 2回目 `AlreadyOfferedToday`。予算を超える値 → `OverBudget`。どれも所持金・所持品は不変 | 結果の写し間違い / 拒否で所持品を減らす | |
| 15 | 世帯から買う: 成立 | 提示価格と販売在庫のある世帯から買う。数量 = min(要求, 販売在庫, 払える数)。売り手の帳簿に `Sale` 1行(相手 `PlayerId`・`OccurredAt == Now`・単価 = 提示価格)、売り手の流動資金 + 、工房在庫 − 、所持金 −、所持品 + | 帳簿の相手を窓口にする / 記帳時刻を当日 18時にする / 販売在庫ではなく工房在庫で切る(留保を割って番人が投げる) | |
| 16 | 世帯から買う: 拒否 | 提示価格の無い品目 → `NoOffer`。販売在庫 0 → `SellerOutOfStock`。所持金 < 価格 → `InsufficientFunds`。どれも World 不変(ハッシュ一致) | 拒否で部分的に書く | |
| 17 | 世帯から買う: 観測を作らない | 買った後、全 NPC の `Knowledge` の件数が変わらない。`Market` も変わらない | 観測を生成する(GDD06 §6.2) | |
| 18 | 窓口から買う | 1次産品を春と夏に買い、単価がそれぞれの季節の `ExternalSellPrice` になる。所持金で切られる。どの世帯の帳簿も増えない | 季節を見ない / 帳簿に書く | |
| 19 | 窓口へ売る | 都市生産品を売る → 単価 = `ExternalBuyPrice`、所持金 +。1次産品 → `WindowDoesNotBuy`。帳簿は増えない | 1次産品を受け付ける(`ExternalBuyPrice` が投げる)/ 帳簿に書く | |
| 20 | 記帳時刻の非減少 | D0T10 に世帯 s から買い(テスト 15 の形)、`Advance` で D1T00 まで回す。s の帳簿の当日の行は `OccurredAt` が非減少で、プレイヤーとの行は D0T10、NPC との行は D0T18(**s は、同じシードで D0T18 に NPC との行が立つ世帯から選ぶ**。立たない世帯だと非減少が空虚に通る) | コマンドが記帳時刻を当日の18時や翌日にする(`MarketReference.TryPreviousDaySettledPrice` の打ち切りが壊れる。TDD01 §3.4) | |
| 21 | 先回し | `SimHost(M0, 1, startDay: 15)` の `Now` = D15T00、ハッシュが `SimHost(M0, 1)` + `Advance(360)` と一致 | 開始日 × 24 を忘れる / 先回しで別の乱数源を使う | |
| 22 | 先回しの引数 | `startDay: -1` → `ArgumentOutOfRangeException`。`startDay: 0` → `Now` = D0T00 | 負の開始日を黙って 0 に丸める | |
| 23 | 決定論 | 同じシード・同じコマンド列を2つの `SimHost` に流すと、各日の終わりのハッシュが一致 | コマンドが列挙順の保証されないコレクションを使う | |
| 24 | パイプラインの移設 | `SimPipeline.Build` の系統の並びが、移設前の `Program.BuildPipeline` と同じ(`Production`, `Consumption`, `Household`, `NeedGeneration`, `TradePlanning`, `Trade`, `Metrics` の系統) | 並びを変える / 抜ける | |
| 25 | `Execute` の呼び出し側の誤り | `null` → `ArgumentNullException`。未知の `ICommand` 実装 → `ArgumentException` | 未知の型を黙って Accepted にする | |
| 26 | ハッシュに Player が乗る | 所持金・所持品の1要素・`DistrictId`・`MoveDestinationDistrictId`・`MoveArrivalTick` を1つずつ変えると、それぞれハッシュが変わる | 区分を書き忘れる / 5つのうちどれかを入れ忘れる | **核心**: `StateHasher` の Player 区分から `MoveArrivalTick` の書き込みを消す / 赤 |
| 27 | Player の初期値 | `WorldGenerator.Generate` 直後: 所持金 2,400・所持品すべて 0・`DistrictId` 4・行き先 −1 | 初期所持金を世帯の初期資金から引く / 中心以外に置く | |
| 28 | 恒等式(書き直し) | 上の 6. の台本で、毎日の終わりに恒等式が成り立ち、4方向の Accepted がそれぞれ 1件以上 | 窓口から買って所持金を減らし忘れる / 窓口へ売って所持金を増やし忘れる / 世帯から買って売り手の流動資金を増やし忘れる | **核心**: 4.5 の `Player.Funds -= q × price` を消す / 赤 |

**核心は5件**(2・3・8・26・28)。`mutator` が測る。

## 編集してよい文書

- なし(コードとテストのみ)。GDD02 §2.4・TDD01 §3.2・§3.4・§3.5・§3.8 はフェーズ1 が本仕様と同じコミットで書いた

## このタスクで特に効く規約

- **コマンドは乱数を開かない。** NPC の取引コード(`StoreChoice` など)を流用しない。`PlayerSale` / `BuyerDemand` は乱数を引かない(TDD01 §3.1・§3.4)
- **列挙順**: 「最初の世帯」「最初の Need」はすべて Id 昇順で決める(`World.Needs` の並びに依存しない。`DisclosedNeeds.Of` は Id 昇順を返す)
- **`Player` を順1〜9 のシステムから読まない**(TDD01 §3.2)。到着の処理をシステムにしない理由でもある
- 係数・定数には単位のコメント(`PlayerInitialFunds`)

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **「核心」印の変異を `mutator` が実測し(レビューの巡が閉じた後)、結果を doc コメントへ転記した**
- [ ] **先回しの所要時間を実測し、PR 説明に書いた**(`SimHost(M0, シード 1〜5, startDay: 15)` の生成にかかったミリ秒。#262 が開始日を決める材料)
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
