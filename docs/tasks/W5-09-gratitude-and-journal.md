# W5-09: グレーボックスの感謝の演出と日誌

| 項目     | 内容                           |
| -------- | ------------------------------ |
| issue    | #283(#47 を3本に分けた3本目。1本目 #281・2本目 #282 はマージ済み) |
| 根拠     | GDD05 §2.1 / TDD01 §3.10「日誌と感謝の演出」「会話と売買」/ GDD01 §3.3.1・§3.6 / TDD01 §3.4・§3.5(`DrainEvents`) |
| ブランチ | `feat/283-gratitude-and-journal` |
| worktree | 本体(パイプラインが使う)     |

## スコープ

**含む:** `Visionary.Presentation` に日誌(`JournalEntry`・`PlayerSession.Journal`)、`Labels` の2つ(困りごとの理由・段階の言い方)、`Notes.Journal`、会話の表情と効果音の口(`Conversation.Expression`・`Conversation.TakeChime`)、効果音の波形(`Chime`)、そのテスト。`Visionary.Game` に日誌のパネル、会話のパネルの表情の表示と効果音の再生。

**含まない:** `Visionary.Sim` の変更(**一切触らない**)、`Visionary.Sim.Runner` の変更、CI の変更、煙試験への操作の追加(TDD01 §3.10「起動引数と煙試験」)、絵(表情の画像)と作り込んだ音、セーブ、#287(同じ値で残りを足す)。

**変えない既存コード**(規則8):

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `SimHost.DrainEvents`(前回以降を追記順に返す。読み出し位置は `SimHost` に1つ) | TDD01 §3.5 | 一致 |
| `PlayerTradeExecuted` の中身(`SellToHousehold` のときだけ `Gratitude` と `NeedId`) | TDD01 §3.4 | 一致 |
| `Conversation.Sell` の台詞の足し方(成立 → 成立の台詞、予算超過 → 不成立の台詞、他は台詞なし。引き捨ての数は台詞の行数)と、地の文に段階を書かないこと(`Notes.Trade`) | TDD01 §3.10「会話と売買」/ GDD05 §3.1 | 一致(本タスクは `Sell` の末尾に日誌と表情を足すだけ) |
| `PlayerSession` の `Paused` の書き手(`Talk`・`OpenWindow`・`Close` だけ)と `Move`・`Talk`・`OpenWindow`・`Close` の振る舞い | TDD01 §3.10「会話と売買」 | 一致 |
| `Main` の煙試験の流れ(フレーム数で終える・毎フレーム `Now` を確かめる・`_Draw` の回数) | TDD01 §3.10「起動引数と煙試験」 | 一致 |

## 作るもの

### 1. `Labels` の2つ(GDD05 §2.1)

```csharp
public static string Of(NeedReason reason);   // 開示される3つだけ。それ以外は ArgumentOutOfRangeException
public static string Of(Gratitude gratitude); // 日誌の段階の言い方。知らない値は ArgumentOutOfRangeException
```

| 値 | 字面 |
| -- | ---- |
| `NeedReason.ProductionStopped` | `材料切れ` |
| `NeedReason.ToolsExhausted` | `工具切れ` |
| `NeedReason.DistantStock` | `品切れ` |
| `NeedReason.Distress`・`CannotExpandProduction` | 例外(開示しない。GDD01 §6.1) |
| `Gratitude.Reluctant` | `渋々だった` |
| `Gratitude.Grateful` | `感謝された` |
| `Gratitude.DeepGratitude` | `深く感謝された` |

### 2. `Notes.Journal`(GDD05 §2.1)

```csharp
/// 日誌の1行。単価・数量を受け取らない(書かない)
public static string Journal(GameDate date, Occupation occupation, int householdId, NeedReason reason, int itemId, Gratitude gratitude);
```

書式: `{date.Year}年 {Labels.Of(date.Season)} {date.DayOfSeason}日 {Labels.Of(occupation)}(世帯 {householdId})の{Labels.Of(reason)}に{Labels.ItemName(itemId)}を売った — {Labels.Of(gratitude)}`(数値は `InvariantCulture`)。例: `1年 春 3日 パン屋(世帯 1)の品切れに薪を売った — 深く感謝された`。**「救った」と書かない**(GDD05 §2.1)。

### 3. 日誌(`JournalEntry`・`PlayerSession.Journal`。TDD01 §3.10「日誌と感謝の演出」)

```csharp
/// 日誌の1行。At は約定時刻
public readonly record struct JournalEntry(Tick At, int HouseholdId, int NeedId, Gratitude Gratitude, string Text);

public sealed class PlayerSession
{
    // 既存はそのまま
    public IReadOnlyList<JournalEntry> Journal { get; }   // 追記のみ。古い順
}
```

- **`Host.DrainEvents()` を呼ぶのは `Conversation.Sell` の中だけで、`Host.Execute` の直後に結果を問わず1回呼ぶ。** `Game` と他の部品は呼ばない
- 読み出したイベントのうち、`PlayerTradeExecuted` で `Kind == SellToHousehold` のものだけを、追記順に日誌へ足す。他の向き(窓口・世帯から買う)と他の型は読み捨てる。**段階で絞らない**(渋々も載せる。GDD05 §2.1)
- 1行の中身: `At` = イベントの `At`、`HouseholdId` = `CounterpartyId`、`NeedId` = イベントの `NeedId`、`Gratitude` = イベントの `Gratitude.Value`、`Text` = `Notes.Journal(GameDate.FromTick(At), その世帯の職業, CounterpartyId, 理由, ItemId, Gratitude.Value)`。職業は `Host.Query.Households()[CounterpartyId].Occupation`
- **理由は、その会話の `DisclosedNeeds` のうち `Id == NeedId` のものの `ReasonCode`。** 見つからなければ `InvalidOperationException`(黙って落とさない。TDD01 §3.10)
- 日誌は会話を閉じても、別の会話を開いても消えない(セッションが持つ)

### 4. 表情と効果音の口(`Conversation`。GDD05 §2.1)

```csharp
public sealed class Conversation
{
    // 既存はそのまま
    public string Expression { get; }   // 話し手の表情の一語
    public Gratitude? TakeChime();      // 鳴らす効果音の段階を1回だけ返す
}
```

- `Expression` は開いたとき `ふつう`。`Sell` の結果が `Accepted` なら段階で `渋い顔`(Reluctant)/ `ほっとした顔`(Grateful)/ `晴れやかな顔`(DeepGratitude)、`OverBudget` なら `首を振る`。**それ以外の結果では変えない**(台詞を出さない結果と揃える)
- `TakeChime`: `Sell` が `Accepted` だったら、その段階を保留する(後の `Accepted` が上書きする)。`TakeChime` は保留を返して空にする。保留が無ければ null。`OverBudget` と他の拒否は保留を作らない(既存の保留は消さない)。開いたときは保留なし
- **`Game` は売るボタンの後に `TakeChime` を呼び、null でなければその段階の音を鳴らすだけ**にする

### 5. 効果音の波形(`Chime`)

```csharp
public static class Chime
{
    public const int MixRate = 22050;   // 単位: Hz(標本/秒)
    /// モノラル・16 bit 符号付き・リトルエンディアンの PCM。段階ごとに音の並びが違う
    public static byte[] Pcm16(Gratitude gratitude);   // 知らない値は ArgumentOutOfRangeException
}
```

| 段階 | 音の並び(周波数 Hz / 長さ ms) | 標本数 | バイト数 |
| ---- | ------------------------------- | ------ | -------- |
| Reluctant | 220/200 | 4410 | 8820 |
| Grateful | 440/120 → 554/120 | 2646 + 2646 = 5292 | 10584 |
| DeepGratitude | 523/100 → 659/100 → 784/160 | 2205 + 2205 + 3528 = 7938 | 15876 |

- 1音の標本数 = `MixRate × ms ÷ 1000`(上の値はすべて割り切れる)
- **整数だけで作る**(TDD01 §3.10)。矩形波: 音の中の標本 i の符号は `(i × 周波数 × 2 ÷ MixRate) mod 2 == 0` なら正、そうでなければ負。振幅は 6000 から、その音の終わりへ向けて線形に 0 へ下げる(`6000 × (n − i) ÷ n`、n = その音の標本数、切り捨て)。音のつなぎ目に無音を挟まない
- 定数(周波数・長さ・振幅)には単位のコメントを付ける(CLAUDE.md)

### 6. `src/Visionary.Game/`(GDD05 §2.1、TDD01 §3.10)

Godot 4.7.2 の C# API に従う(CLAUDE.md。**`AudioStreamWav`・`AudioStreamPlayer` のプロパティ名は公式クラスリファレンスで確かめる**)。**ノードは描画と入力だけを持つ。** 行の組み立て・表情・波形は `Presentation` から読む。**`DrainEvents`・`Host.Execute`・`pump.Paused` に触らない。**

- **日誌のパネル(`JournalPanel`。`PanelContainer`、位置 (680, 560)、大きさ 580 × 140 程度)**: 見出し `日誌` と、`_session.Journal` の `Text` を古い順に1行ずつ(`ScrollContainer` に入れ、末尾へ送る)。**`Journal.Count` が前回と違うときだけ作り直す。** パネルが開いていても見せる。`Main` で入口の一覧・会話・窓口のパネルより**先に**足す(会話のパネルが上に描かれる)。`Main._Process` のパネルの更新で `Refresh` を呼ぶ
- **会話のパネル**: 見出しの行を `{Title} — {Expression}` にする。売るボタンの後に `conversation.TakeChime()` を呼び、null でなければ `Chime.Pcm16(段階)` の `AudioStreamWav`(16 bit・`Chime.MixRate`・モノラル)を `AudioStreamPlayer` で鳴らす。波形は段階ごとに1回だけ作って持ち回してよい
- パネルのノードも `Main.tscn` に足さず、コードで作る(#281 と同じ)

## 落ちるべき条件(テスト)

**筋書き S** は W5-08 と同じ(`PlayerSessionTests.ScenarioS`。seed 1。tick 54(1年 春 3日 6時)に窓口で薪 19 を単価 20 で買い、区画 0 へ移り、tick 56(同じ日の8時)に世帯 1(パン屋)へ話しかけた直後)。`DisclosedNeeds` は Need #3(薪 × 19、`DistantStock`)と #19(ビール × 1、`DistantStock`)。**実測(2026-11-03)**: 薪 19 を単価 1〜11 で深い感謝、12〜16 で感謝、17〜19 で渋々、20 以上で予算超過。`alsoBuyBeer` 付きでビール 1 を単価 140 で感謝。そのときの日付は `GameDate.FromTick(56)` = 1年 春 3日 8時。

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心(当てる変異 / 期待) |
| - | ------ | -------- | -------------------- | ------------------------- |
| 1 | `Labels` の2つ | 1 の表の字面。`Distress`・`CannotExpandProduction`・範囲外の値で例外。段階の3つが互いに異なる | 理由の取り違え、開示しない理由に字面を与える | |
| 2 | `Notes.Journal` | 2 の例の字面と完全一致 | 書式の誤り、段階・理由の取り違え | |
| 3 | 売ると日誌に1行 | S の後 `Sell(5, 1, 19)` → `Journal` が1行で、`Text` = `1年 春 3日 パン屋(世帯 1)の品切れに薪を売った — 深く感謝された`、`At` = 56、`HouseholdId` = 1、`NeedId` = 3、`Gratitude` = `DeepGratitude` | 読み出さない、職業・理由の引き違い | |
| 4 | 渋々も残す | S の後 `Sell(5, 17, 19)` → `Reluctant` で、`Journal` が1行、`Text` の末尾が `— 渋々だった`。別の S で `Sell(5, 12, 19)` → `Grateful`・末尾 `— 感謝された` | 段階で絞る | **核心**: 日誌に足す条件に `Gratitude != Reluctant` を足す / 赤 |
| 5 | 売り以外は載せない | S の直後(窓口で2回買った後。`alsoBuyBeer` 付き)の `Journal` が空。会話で `Buy(6, 1)`(パン)→ `Accepted` の後も空。`Sell(5, 1_000_000, 19)` → `OverBudget` の後も空。続けて `Sell(7, 140, 1)`(ビール)→ `Accepted` で **ちょうど1行**(フェーズ2 訂正: 予算超過も (世帯, 品目) の今日の値出しに数えるので、薪を出し直すと `AlreadyOfferedToday` になる) | 窓口・世帯から買った約定を載せる | **核心**: `Kind == SellToHousehold` の絞りを消す / 赤 |
| 6 | 1つの会話で2行 | `alsoBuyBeer` 付きの S で `Sell(5, 1, 19)`(深い感謝)→ `Sell(7, 140, 1)`(感謝)。`Journal` が2行で順に `NeedId` 3・19、2行目の `Text` = `1年 春 3日 パン屋(世帯 1)の品切れにビールを売った — 感謝された` | 古い順にしない、2回目の読み出しで1回目を重ねて載せる | |
| 7 | 閉じても残り、重ならない | #3 の後 `Close` → 同じ世帯へ `Talk(1)` → `Accepted`。`Journal` が1行のまま。その会話で `Sell(7, …)` が拒否されても1行のまま(フェーズ2 訂正: 拒否は予算超過のビールで作る) | 会話ごとに日誌を作る、閉じると消える | |
| 8 | 表情 | S の直後 `Expression` = `ふつう`。`Sell(5, 1, 19)` で `晴れやかな顔`。別の S で `Sell(5, 12, 19)` → `ほっとした顔`、`Sell(5, 17, 19)` → `渋い顔`、`Sell(5, 1_000_000, 19)` → `首を振る`。`OverBudget` の後に `Sell(8, 1, 1)` → `NotEnoughGoods` で `首を振る` のまま | 段階の取り違え、台詞を出さない拒否で変える | |
| 9 | 効果音の保留 | S の直後 `TakeChime()` が null。`Sell(5, 1, 19)` の後、1回目が `DeepGratitude`・2回目が null。別の S で `Sell(5, 1_000_000, 19)` の後 null | 保留を空にしない(鳴り続ける)、拒否で鳴らす | |
| 10 | 波形 | 3段階の `Pcm16` の長さが表のバイト数。3つが互いに異なる。同じ段階を2回呼んで同じバイト列。各標本(2バイトずつ `short` で読む)の絶対値が 6000 以下。Reluctant の先頭標本が +6000 | 長さの誤り、段階の取り違え、振幅の誤り | |
| 11 | 日誌は World を変えない | S で `Conversation.Sell(5, 1, 19)` を通した後の `StateHash()` が、別の S で `Host.Execute(new SellToHouseholdCommand(1, 5, 1, 19))` を直接打った後の値と等しい | 日誌・表情の組み立ての中で、**成立する**書き込みコマンドを余計に `Execute` する(フェーズ2 訂正: 拒否される `Execute` は World を書かないので落ちない。そちらは配線の約束が受け持つ) | |

**レビューで足したテスト**(上の表は implementer に渡した時点の指示):

| # | テスト | 検証内容 | この実装ミスで落ちる | 出所 |
| - | ------ | -------- | -------------------- | ---- |
| R1 | 理由を Need の Id で引く | 開示される `DisclosedNeeds` に `ReasonCode` が2種類以上あり、売る相手の Need が**先頭でない**筋書きで売り、日誌の `Text` がその Need の理由の字面になる。`NeedId` が開示に無いときの `InvalidOperationException` も、公開の口から踏めるなら踏む | 先頭の Need の理由を使う、品目で引く、理由を固定する | レビュー1巡目 I-a。**筋書きの候補**(1回目のフェーズ2 が探した。未確定): seed 1・tick 534(1年 春 23日 6時)に窓口で鉄鉱石(2)を1個買い、世帯 3 の区画へ移って `Talk(3)`。開示は #63(ビール、品切れ)・#70(鉄鉱石、材料切れ)・#71(木炭、品切れ)で、鉄鉱石を売ると #70 に当たる。「品目で引く」は落とせない |

**表に無い保護**: `Game` のノード(日誌のパネルの作り直し、表情の見出し、音の再生)は xUnit で踏まない。煙試験は操作をしないので、日誌のパネルの `_Ready`・`Refresh` までしか踏まない(空の日誌)。音が実際に鳴るか・聞き分けられるかは、フェーズ1 が DONE の後に noz72 の Godot で手で確かめる。

## 配線(規則7)

- **`DrainEvents` の唯一の呼び出し**: `Presentation` と `Game` の中で `DrainEvents(` が `Conversation.Sell` の1か所だけであること
- **`Game` は `Execute(`・`Paused` の代入・`DrainEvents(` を持たない**(W5-08 の約束に1つ足す)
- **呼び出し順**: 1フレームの中で `GameLoop.Frame` → 表示の更新 → 入口の一覧とパネル(日誌を含む)の更新 → `QueueRedraw`。売るボタンは `conversation.Sell` → `TakeChime` → (鳴らす)→ パネルの作り直し
- **単位の約束**: `Chime.MixRate` は Hz で、`AudioStreamWav.MixRate` へそのまま渡す。波形は 16 bit・モノラル

## 編集してよい文書

- なし(コードとテストのみ)

## このタスクで特に効く規約

- **`Visionary.Sim` を変更しない。** 足りない読み出し口に気付いたら止まって報告する
- 定数には単位のコメントを付ける
- **描画側でゲーム時間を作らない**(#281 と同じ)

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **「核心」印の変異を `mutator` が実測し(レビューの巡が閉じた後)、結果を doc コメントへ転記した**
- [ ] `dotnet build Visionary.sln -c Release` が警告0(`Visionary.Game` を含む)
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
- [ ] (フェーズ1 が DONE の後に確かめる)Godot の煙試験と手での操作(音を含む)
