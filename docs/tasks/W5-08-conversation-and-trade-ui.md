# W5-08: グレーボックスの会話メニューと売買の UI(時間の停止を含む)

| 項目     | 内容                           |
| -------- | ------------------------------ |
| issue    | #282(#47 を3本に分けた2本目。1本目 #281 は PR #284 でマージ済み。感謝の演出と日誌は #283) |
| 根拠     | TDD01 §3.4・§3.5・§3.10「会話と売買」/ GDD05 §1.4・§2.1・§2.2・§3.1 / GDD01 §3.3.1 / GDD06 §6.1〜§6.4 |
| ブランチ | `feat/282-conversation-and-trade-ui` |
| worktree | 本体(パイプラインが使う)     |

## スコープ

**含む:** `Visionary.Presentation` に会話と売買のセッション(`PlayerSession`・`Conversation`・`WindowCounter`・`DistrictEntry`・`Notes`)と `Labels.ItemName`、そのテスト。`Visionary.Game` に区画の入口の一覧・会話のパネル・窓口のパネル、移動をセッション経由にする変更、表示に所持品を足す変更。

**含まない:** 感謝の演出(表情・効果音)と日誌(#283。`DrainEvents` は本タスクでは呼ばない)、操作の経路の煙試験(足さない。TDD01 §3.10「起動引数と煙試験」)、CI の変更、`Visionary.Sim` の変更(**一切触らない**)、`Visionary.Sim.Runner` の変更、倍速(GDD05 §6)、セーブ。

**変えない既存コード**(規則8):

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `PlayerCommandExecutor` の検査順(引数 → 移動中 → 時間帯 → 居合わせ → コマンドごと)と、拒否は World を書かないこと | TDD01 §3.4 / GDD06 §6.4 | 一致 |
| `AskNeeds` は開示される Need を返すだけで World を書かない | TDD01 §3.4 | 一致 |
| 世帯へ売る: 予算以下で成立、数量 = min(提示, Need の数量, 資金)、同じ (世帯, 品目) は1日1回、成立時だけ `Gratitude` を返す | GDD06 §6.1 / GDD01 §3.6 | 一致 |
| 世帯から買う・窓口との売買の数量の切り方(在庫・所持金・所持品) | GDD06 §6.2・§6.3 | 一致 |
| `DialogueSelector.Say`(1行につき `NextInt` を1回。候補の和集合・使用回数最小・直前除外)と `OpenConversation` の鍵 (seed, `Dialogue`, tick, npcId) | GDD01 §3.3.1 / TDD01 §3.1 | 一致 |
| `ExternalMarket.TryOfferPrice`・`WorldDefinition.ExternalBuyPrice`・`IsPrimaryItem`(窓口の値) | GDD06 §6.3 / GDD02d §2.1・§5 | 一致 |
| `TickPump`・`GameLoop`・`MapLayout`・`LaunchOptions`(#281 のまま) | TDD01 §3.10 | 一致 |
| `Main` の煙試験の流れ(フレーム数で終える・毎フレーム `Now` を確かめる・`_Draw` の回数) | TDD01 §3.10「起動引数と煙試験」 | 一致(本タスクで流れを変えない) |

## 作るもの

### 1. `Labels.ItemName` と `Notes`(GDD05 §3.1、TDD01 §3.10「会話と売買」)

```csharp
public static class Labels
{
    // 既存の Of(...) 4つはそのまま
    public static string ItemName(int itemId);   // 0〜8: 穀物 木材 鉄鉱石 木炭 小麦粉 薪 パン ビール 工具。範囲外は ArgumentOutOfRangeException
}

public static class Notes
{
    /// 所持品のうち 1 個以上の品目を Id 昇順に「{名} {数}」で「、」区切り。1つも無ければ「なし」
    public static string Inventory(IReadOnlyList<int> inventory);

    /// 売買の結果の地の文
    public static string Trade(PlayerTradeKind kind, int itemId, Result result);
}
```

- `ItemName` の名は台詞の資料(`DialogueCorpusM0.Items`)と同じ字面にする。**資料は internal なので写しを持つ**(写しのずれは機械が見ない。下の「表に無い保護」)
- `Inventory` の例: `[0, 0, 0, 0, 3, 5, 0, 0, 0]` → `小麦粉 3、薪 5`。全部 0 → `なし`
- `Trade`: `result.Status == Accepted` なら `{品目} × {result.Quantity} を単価 {result.UnitPrice} で{動詞}(計 {result.Payment})`。動詞は `SellToHousehold`・`SellToWindow` が「売った」、`BuyFromHousehold`・`BuyFromWindow` が「買った」。それ以外の結果は `Labels.Of(result.Status)` だけ(品目を書かない — `InvalidArgument` では品目 Id が範囲外でありうる)。例: 薪・19・単価 1・計 19 を世帯へ売った → `薪 × 19 を単価 1 で売った(計 19)`
- **`Trade` は `result.Gratitude` を読まない。** 段階の名を地の文に出さない(GDD05 §2.1・§3.1)。知らない `PlayerTradeKind` は `ArgumentOutOfRangeException`

### 2. `DistrictEntry`(GDD05 §2.2)

```csharp
public enum DistrictEntryKind { Household = 1, Window = 2 }   // 0 を使わない

/// 居る区画の入口。Window なら HouseholdId = −1
public readonly record struct DistrictEntry(DistrictEntryKind Kind, int HouseholdId, string Label);
```

### 3. `PlayerSession`(TDD01 §3.10「会話と売買」、GDD05 §1.4・§2.2)

```csharp
public sealed class PlayerSession
{
    /// definition は host に渡したものと同じ。masterSeed も host に渡したものと同じ(台詞の乱数の鍵)。null は ArgumentNullException
    public PlayerSession(SimHost host, WorldDefinition definition, TickPump pump, long masterSeed, DialogueCorpus corpus);

    public SimHost Host { get; }
    public Conversation? Conversation { get; }   // 開いている会話。無ければ null
    public WindowCounter? Window { get; }        // 開いている窓口。無ければ null
    public bool IsPanelOpen { get; }             // Conversation か Window が開いている

    public IReadOnlyList<DistrictEntry> Entries();
    public Result? Move(int districtId);
    public Result Talk(int householdId);
    public WindowCounter OpenWindow();
    public void Close();
}
```

- **コンストラクタは `pump.Paused` を変えない。** `DialogueMemory` を1つ作って持つ。品目ごとの「この起動で最後に買った単価」(`int[]`、長さ `definition.ItemCount`、初期値 0 = 買っていない)を持つ
- **`Entries`**: `Host.Query.Player()` が移動中なら空。そうでなければ、居場所の区画に住む世帯を Id 昇順に `(Household, Id, "{Labels.Of(職業)}(世帯 {Id})")`、続けて居場所が `District.ExternalMarketDistrictId` なら `(Window, −1, "窓口")`。**パネルが開いていても同じものを返す**(隠すのは画面の側)
- **`Move`**: パネルが開いていれば **何もせず `null`**(`Execute` を呼ばない)。そうでなければ `Host.Execute(new MoveCommand(districtId))` の結果
- **`Talk`**: パネルが開いていれば `InvalidOperationException`。`Host.Execute(new AskNeedsCommand(householdId))` を呼び、**`Accepted` でなければ会話を開かずにその結果を返す**(`Paused` は変えない)。`Accepted` なら会話を開き(下の 4)、`pump.Paused = true`、結果を返す。**会話を開く条件をここで判定し直さない**(TDD01 §3.10)
- **`OpenWindow`**: パネルが開いていれば `InvalidOperationException`。条件を見ずに窓口を開き、`pump.Paused = true`、開いた `WindowCounter` を返す(TDD01 §3.10「窓口のパネルは条件なしに開く」)
- **`Close`**: 開いているパネルを閉じ(`Conversation`・`Window` を null に)、`pump.Paused = false`。何も開いていなければ何もしない(`Paused` も変えない)
- **`pump.Paused` を書くのは `Talk`・`OpenWindow`・`Close` だけである。** 他の部品と `Game` は書かない

### 4. `Conversation`(TDD01 §3.10「会話と売買」、GDD01 §3.3.1、GDD05 §2.2・§3.1)

```csharp
/// 台詞なら Kind と TemplateId を持つ。地の文(売買の結果)なら両方 null
public readonly record struct ConversationLine(LineKind? Kind, string? TemplateId, string Text);

/// 世帯へ売る候補。DefaultUnitPrice 単位: 貨幣/個
public readonly record struct SellOption(int ItemId, int Held, int DefaultUnitPrice);

public sealed class Conversation
{
    public int HouseholdId { get; }
    public int SpeakerNpcId { get; }                       // 世帯の構成員の最小 NpcId
    public string Title { get; }                           // "{Labels.Of(職業)}(世帯 {Id})"
    public IReadOnlyList<Need> DisclosedNeeds { get; }     // Talk の AskNeeds の結果そのまま
    public IReadOnlyList<ConversationLine> Lines { get; }  // 追記のみ

    public IReadOnlyList<SellOption> SellOptions();
    public IReadOnlyList<OfferView> BuyOptions();
    public Result Sell(int itemId, int unitPrice, int quantity);
    public Result Buy(int itemId, int quantity);
}
```

コンストラクタは `internal`(`PlayerSession.Talk` だけが作る)。

- **開いたとき**(`Talk` の中): 職業は `Host.Query.Households()[householdId].Occupation`、季節は `GameDate.FromTick(Host.Now).Season`、性格は null。会話を通してこの2つを使う(時間が止まっているので変わらない)。`DialogueSelector.OpenConversation(masterSeed, Host.Now, SpeakerNpcId)` を**1回**開き、**その1本の列で**次を順に `Say` して `Lines` に足す(GDD01 §3.3.1):
  1. 挨拶(`Greeting`)
  2. 世間話(`SmallTalk`)
  3. `DisclosedNeeds` が空なら困りごとは無い(`NoNeed`)。そうでなければ各 Need を並び順に開示(`NeedDisclosure`。1件目は `Order = First`、2件目以降は `Continued`。`ItemId` = Need の品目、`Quantity` = Need の数量)
- **`SellOptions`**: `Host.Query.Player().Inventory` のうち 1 個以上の品目を Id 昇順に。`Held` = 所持数、`DefaultUnitPrice` = セッションの「最後に買った単価」(0 なら 1)
- **`BuyOptions`**: `Host.Query.Offers()` のうち `HouseholdId` がこの世帯のもの(並びは `Offers()` のまま = 品目 Id 昇順)。**他の世帯の注文を出さない**(GDD05 §3.1)
- **`Sell`**: `Host.Execute(new SellToHouseholdCommand(HouseholdId, itemId, unitPrice, quantity))`。結果が
  - `Accepted` なら取引の成立(`TradeAccepted`。`Gratitude` = `result.Gratitude.Value`、`ItemId` = `itemId`、`Quantity` = `result.Quantity`)
  - `OverBudget` なら取引の不成立(`TradeRefused`。`ItemId` = `itemId`)
  - それ以外は台詞なし

  を足し、続けて地の文 `Notes.Trade(SellToHousehold, itemId, result)` を足して、結果を返す。**台詞を言うときは、同じ鍵で列を開き直し、その会話の `Lines` のうち台詞(`Kind != null`)の行数だけ `NextUInt64()` を引き捨ててから `Say` する**(TDD01 §3.10。`RandomSequence` は `ref struct` でフィールドに持てない)
- **`Buy`**: `Host.Execute(new BuyFromHouseholdCommand(HouseholdId, itemId, quantity))`。`Accepted` ならセッションの「最後に買った単価」を `result.UnitPrice` にする。地の文 `Notes.Trade(BuyFromHousehold, itemId, result)` を足して、結果を返す。台詞は無い(GDD01 §3.3.1 に行が無い)
- **閉じた後**(`PlayerSession.Close` の後)の `Sell`・`Buy` は `InvalidOperationException`(`Execute` を呼ばない)。古いボタンが残っていても状態を書かない

### 5. `WindowCounter`(GDD05 §3.1、GDD06 §6.3)

```csharp
/// UnitPrice 単位: 貨幣/個。Held 単位: 個
public readonly record struct WindowPrice(int ItemId, int UnitPrice, int Held);

public sealed class WindowCounter
{
    public IReadOnlyList<ConversationLine> Lines { get; }   // 地の文だけ。追記のみ
    public IReadOnlyList<WindowPrice> BuyOptions();         // 窓口から買う
    public IReadOnlyList<WindowPrice> SellOptions();        // 窓口へ売る
    public Result Buy(int itemId, int quantity);
    public Result Sell(int itemId, int quantity);
}
```

コンストラクタは `internal`(`PlayerSession.OpenWindow` だけが作る)。

- **`BuyOptions`**: 品目 0〜`ItemCount − 1` のうち `ExternalMarket.TryOfferPrice(definition, Host.Now, itemId, out price)` が true のものを Id 昇順に。`Held` = 所持数
- **`SellOptions`**: `IsPrimaryItem` でなく所持数 1 以上の品目を Id 昇順に。`UnitPrice` = `ExternalBuyPrice(itemId)`
- **`Buy`** / **`Sell`**: `BuyFromWindowCommand` / `SellToWindowCommand` を `Execute`。`Buy` が `Accepted` ならセッションの「最後に買った単価」を更新。地の文 `Notes.Trade(BuyFromWindow / SellToWindow, itemId, result)` を足して、結果を返す
- 閉じた後の `Buy`・`Sell` は `InvalidOperationException`

### 6. `src/Visionary.Game/`(TDD01 §3.10、GDD05 §2.2・§3.1)

Godot 4.7.2 の C# API に従う(CLAUDE.md)。**ノードは描画と入力だけを持つ。** 並べ方・既定値・行の組み立ては `Presentation` から読む。**`Host.Execute` と `pump.Paused` に触らない**(すべて `PlayerSession` 経由)。

- **`Main`**: `_Ready` で(煙試験でも通常でも同じく)`PlayerSession(host, _definition, pump, options.Seed, DialogueCorpus.M0)` を作る。下の3つのパネルを既存の `CanvasLayer` の下に足す
  - `_UnhandledInput`: `_session.Move(districtId)` に置き換える。`null`(パネルが開いている)なら何もしない。それ以外は従来どおり結果を表示に残す
  - 表示(`Label`)に `所持品 {Notes.Inventory(player.Inventory)}` の行を足す。「移動:」の行は「最後の操作:」にし、移動・話しかけの拒否・窓口を開いたことを出す
  - `_Process` は従来の流れのまま、表示の更新の後にパネルの更新(下)を呼ぶ。**煙試験の終わり方・フレーム数の検査は変えない**
- **区画の入口(`DistrictPanel`。`VBoxContainer`、位置 (680, 220))**: 毎フレーム `_session.Entries()` を読み、前回と `SequenceEqual` で違うときだけボタンを作り直す(毎フレーム作り直すと押下が届かない)。ボタンの文字は `Label`。`Household` を押すと `_session.Talk(HouseholdId)`(拒否なら結果を「最後の操作」へ)、`Window` を押すと `_session.OpenWindow()`。**パネルが開いている間は隠す**
- **会話のパネル(`ConversationPanel`。`PanelContainer`、位置 (680, 220)、幅 560 程度)**: `_session.Conversation` が null でない間だけ見せる。中身:
  - `Title`
  - `Lines` を上から。台詞は `「{Text}」`、地の文は `― {Text}`(`ScrollContainer` に入れ、高さを決めて溢れさせない)
  - 「売る」: `SellOptions` の各行に `{ItemName}(所持 {Held})`・単価の `SpinBox`(最小 1、既定 `DefaultUnitPrice`)・数量の `SpinBox`(1〜`Held`、既定 `Held`)・ボタン「売る」→ `Conversation.Sell`
  - 「買う」: `BuyOptions` の各行に `{ItemName} 単価 {UnitPrice}(在庫 {SellableStock})`・数量の `SpinBox`(最小 1、既定 1)・ボタン「買う」→ `Conversation.Buy`
  - ボタン「立ち去る」→ `_session.Close()`
  - 売買のボタンを押したら中身を作り直す(古い子は `RemoveChild` してから `QueueFree`)
- **窓口のパネル(`WindowPanel`。会話のパネルと同じ位置)**: `_session.Window` が null でない間だけ見せる。`Lines`(地の文)、「買う」(`BuyOptions`。`{ItemName} 単価 {UnitPrice}(所持 {Held})`・数量 `SpinBox` 最小 1 既定 1・ボタン)、「売る」(`SellOptions`。数量 `SpinBox` 1〜`Held` 既定 `Held`・ボタン)、「閉じる」→ `_session.Close()`
- **`SpinBox` の値は `Rounded = true`・`Step = 1` とし、コマンドへは `(int)spinBox.Value` で渡す。** 単価の `SpinBox` の上限は 1,000,000(単位: 貨幣/個)とする。数量の上限で画面の側から数量を切らない — 在庫・所持金・Need で切るのはシム(GDD05 §3.1)
- パネルのノードも `Main.tscn` に足さず、コードで作る(#281 と同じ)

## 落ちるべき条件(テスト)

**筋書き S**(#6〜#9 で使う。seed 1 で実測、2026-10-29): `host = new SimHost(WorldDefinition.M0, 1)`、`pump = new TickPump()`、`session = new PlayerSession(host, WorldDefinition.M0, pump, 1, DialogueCorpus.M0)`。`host.Advance(54)`(2日目6時)。`need = host.Query.TradableNeeds()[0]`(= Need #3、世帯 1・パン屋・区画 0、薪(5)× 19)。プレイヤーは区画 4 に居る。`session.OpenWindow().Buy(5, 19)` → `Accepted`・19 個・単価 20 → `session.Close()` → `session.Move(0)` → `Accepted`・到着 56 → `host.Advance(2)` → `session.Talk(1)` → `Accepted`(8時)。

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心(当てる変異 / 期待) |
| - | ------ | -------- | -------------------- | ------------------------- |
| 1 | `Labels.ItemName` | 0〜8 が上の名で空でなく重複しない。−1・9 で例外 | 名の取り違え、範囲検査の漏れ | |
| 2 | `Notes.Inventory` | 1 の2例 | 0 個の品目を出す、区切りの誤り、空のとき空文字 | |
| 3 | `Notes.Trade` | 1 の例、4つの向きの動詞、拒否(`OverBudget` と `InvalidArgument`)は `Labels.Of` だけ、`Gratitude` が `DeepGratitude` でも文字列が `Gratitude` null のときと同じ | 売る・買うの取り違え、拒否で範囲外の品目名を引いて例外、段階を出す | |
| 4 | `Entries` | seed 1 の生成直後(0時、区画 4): `[(Household, 0, "{Labels.Of(世帯 0 の職業)}(世帯 0)"), (Window, −1, "窓口")]`(区画 4 の世帯は 0 だけ。実測)。`Move(0)` の直後(移動中)は空。`Advance(2)` で到着後は `[(Household, 1, "パン屋(世帯 1)")]`(窓口なし) | 窓口を中心以外に出す、移動中に出発区画の世帯を出す、並びを Id 昇順にしない | |
| 5 | 時間の停止 | `Talk` が拒否(seed 1 の生成直後は0時なので区画 4 の世帯へ `OutsideTradingHours`)なら `Conversation` は null・`Paused` は false。`OpenWindow` 後は `Paused` true で、`GameLoop.Frame(DefaultMicrosecondsPerTick)` が 0 を返し `Now` が変わらない。`Close` 後は `Paused` false で同じ `Frame` が 1 を返す。何も開いていない `Close` は `Paused` を変えない | 開いても止めない、閉じても再開しない、拒否でも止める | **核心**: `OpenWindow` の `pump.Paused = true` を消す / 赤。**核心**: `Close` の `pump.Paused = false` を消す / 赤 |
| 6 | 開いている間は移動しない | S の `Talk` 後に `session.Move(4)` が null で、`Query.Player()` の居場所が区画 0 のまま(移動中にならない)。`Talk`・`OpenWindow` の二重開きは例外 | パネルを見ずに `Execute` する | **核心**: `Move` のパネルの検査を消す / 赤 |
| 7 | 会話の頭の台詞 | S の `Lines` が4行で、`Kind` = [挨拶, 世間話, 開示, 開示]、`TemplateId` = [`greet.baker.-.01`, `small.baker.-.02`, `need.any.first.04`, `need.any.cont.07`]。`SpeakerNpcId` = 2。`DisclosedNeeds` の Id = [3, 19]。**同じ筋書きを別のセッションで2回回して `Lines` が一致する** | 開示の順(1件目・続き)の取り違え、話し手を最小 NpcId にしない、列を行ごとに開き直す(2行目以降が1行目と同じ値で選ばれる)、シードを host と違うものにする | |
| 8 | 売って成立 | S の後 `Sell(5, 1, 19)` → `Accepted`・19 個・`DeepGratitude`。`Lines` の5行目が `TradeAccepted`・`thanks.baker.deep.01`、6行目が地の文 `薪 × 19 を単価 1 で売った(計 19)`。所持金が `2020 + 19` | 段階・数量を結果から取らない、地の文を足さない、**列の引き捨てをしない**(5行目が `thanks.any.deep.01` になる。実測 2026-10-29) | **核心**: `Sell` の引き捨ての `NextUInt64()` を消す / 赤 |
| 9 | 売って予算超過 | S の後 `Sell(5, 1_000_000, 19)` → `OverBudget`。5行目が `TradeRefused`・`refuse.baker.-.02`、6行目が地の文 `予算を超える`。所持品の薪が 19 のまま | 不成立の台詞を出さない、拒否なのに成立の台詞を出す | |
| 10 | その他の拒否に台詞を出さない | S の後に所持していない品目(工具 8)を `Sell(8, 1, 1)` → `NotEnoughGoods`。足されるのは地の文1行だけ | 拒否のたびに不成立の台詞を出す | |
| 11 | 売る候補と既定の単価 | S の後(薪 19 を単価 20 で買った)の `SellOptions` が `[(5, 19, 20)]`。**セッションを通さずに** `host.Execute(new BuyFromWindowCommand(5, 19))` で持たせた後に作ったセッションでは、同じ所で `[(5, 19, 1)]` | 既定値を相場・窓口の値から取る、買値を記録しない | |
| 12 | 世帯から買う | S の会話で `BuyOptions` が `[(品目 6, 世帯 1, 区画 0, 単価 94, 在庫 10)]`(= `Query.Offers()` の世帯 1 の分。実測 2026-10-29)。`Buy(6, 1)` → `Accepted`・1 個・単価 94 で、所持品のパンが 1、地の文 `パン × 1 を単価 94 で買った(計 94)`、`SellOptions` のパンの既定単価が 94 | 他の世帯の注文を出す、買値を記録しない | |
| 13 | 窓口 | 生成直後(0時)に `OpenWindow` して `BuyOptions` が 9 品目(`TryOfferPrice` の値と一致)、`SellOptions` が空。`Buy(5, 1)` は `OutsideTradingHours` で地の文 `取引時間外`。S の窓口(6時)の `Buy` 後、`SellOptions` が薪だけで単価 = `ExternalBuyPrice(5)` | 1次産品を売る候補に出す、所持 0 を出す、時刻を見ずに価格を引く | |
| 14 | 閉じた後は書かない | S の `Talk` → `Close` の後、手元の `Conversation` の `Sell` が例外で、所持品・所持金・`StateHash()` が変わらない。窓口も同じ | 閉じた会話から `Execute` する | |
| 15 | 話しても World は変わらない | S の `Talk` → `Close` の前後で `StateHash()` が等しい(`AskNeeds` と台詞は World を書かない) | 会話の中で `Execute` を余計に呼ぶ | |

**表に無い保護**: `Labels.ItemName` と台詞の資料の名の一致は機械が見ない(資料が internal)。`Game` のノード(ボタンからセッションを呼ぶ配線、パネルの見せ隠し、`SpinBox` の値の読み方)は xUnit で踏まない。**煙試験は従来どおり通常と同じノードを通すので、パネルを足して `_Ready`・`_Process` が壊れれば落ちる**が、パネルの中身は踏まない(TDD01 §3.10「起動引数と煙試験」の穴)。フェーズ1 が DONE の後に、noz72 の Godot で煙試験と、話しかけ・売買・窓口・時間の停止を手で確かめる。

## 配線(規則7)

`PlayerSession` 以下は `Game` からしか呼ばれない。xUnit が踏むのはセッションまでなので、次をレビューで見る。

- **入力の作り方**: `PlayerSession` に渡す `definition` は `SimHost` に渡した `_definition`、`masterSeed` は `options.Seed`(`SimHost` に渡したのと同じ)、`pump` は `GameLoop` に渡したのと同じもの。`DialogueCorpus.M0`
- **`Execute` と `Paused` の唯一の経路**: `Game` の中に `Execute(` と `Paused` の代入が無いこと(移動も `_session.Move`)
- **呼び出し順**: 1フレームの中で `GameLoop.Frame` → 表示の更新 → 入口の一覧とパネルの更新 → `QueueRedraw`。ボタンの押下はセッションを呼んでパネルを作り直すだけで、`Frame` を呼ばない
- **単位の約束**: 単価は貨幣/個、数量は個。`SpinBox` の `double` は `(int)` で整数に直してからコマンドへ渡し、シムへ浮動小数点を渡さない

## 編集してよい文書

- なし(コードとテストのみ)

## このタスクで特に効く規約

- **`Visionary.Sim` を変更しない。** 足りない読み出し口に気付いたら止まって報告する
- **乱数は系統 `Dialogue` だけを、`OpenConversation` 経由で開く。** `RandomSource.Open` を直接呼ばない(TDD01 §3.1)
- **描画側でゲーム時間を作らない**(#281 と同じ)。止めるのは `Paused` だけ

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **「核心」印の変異を `mutator` が実測し(レビューの巡が閉じた後)、結果を doc コメントへ転記した**
- [ ] `dotnet build Visionary.sln -c Release` が警告0(`Visionary.Game` を含む)
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
- [ ] (フェーズ1 が DONE の後に確かめる)Godot の煙試験と手での操作
