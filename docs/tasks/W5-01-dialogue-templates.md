# W5-01: 台詞テンプレートと dialogue-sample

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#46](https://github.com/stama72/visionary/issues/46)                |
| 根拠     | [GDD01 §3.3・§3.3.1・§3.6・§6.1・§6.2 #3](../03-gdd/01-trust-and-conversation.md) / [GDD06 §6.1](../03-gdd/06-trade-and-negotiation.md) / [TDD01 §3.1・§4.1](../04-tdd/01-sim-core-and-m0.md) / ADR-0002 |
| ブランチ | `feat/46-dialogue-sample`                                            |
| worktree | `.claude/worktrees/46-dialogue-sample/`(背景セッションのフェーズ1 がここから `pipeline.ps1` を起動する。[#204](https://github.com/stama72/visionary/issues/204) の形) |

**台詞テンプレートの照合と選択、M0 の台詞資料、`vsim dialogue-sample` を作る。** 規則は GDD01 §3.3.1(本タスクの凍結コミットで書いた)が持つ。本書は「どう作るか」だけを書く。

## フェーズ1 で決めたこと

開発者と決めた4点(2026-10-03)。GDD01 §3.3.1 に書いた。

| # | 決定 | 理由 |
| - | ---- | ---- |
| 1 | 会話は 挨拶 → 世間話 → 開示 / 困りごとは無い → 取引の台詞(成立は感謝の3段階 / 不成立)。6種類 | 同じ NPC と10回話して最も繰り返されるのは挨拶と世間話で、そこを含めないと §6.2 #3 が判定できない |
| 2 | 性格の軸は型に置くが、M0 の資料はすべて「指定なし」 | シムの NPC に性格が無い。台詞の量を増やさない |
| 3 | 本文は implementer が初稿を書く。資料はロジックと別のファイル | 開発者がロジックに触れずに書き直せるように |
| 4 | `dialogue-sample` は実際のシムを回し、その日に立っている Need を使う | どんな Need がどれだけ立つかを判定材料に反映する |

**`{deadline}` は M0 で使わない。** issue #46 の依存節は「M0 ではニーズの期限としてだけ使う」と書いたが、GDD01 §6.1(#243 決定11・24)は M0 で期限を開示しないと決めており、`Need.Deadline` も M0 では `Tick.Zero` 固定で値が無い。**GDD を正として、展開規則は M1 へ送った**(GDD01 §3.3 の暦の制約)。

## スコープ

**含まない:**

- 感謝の段階の計算と、プレイヤーの販売の購入規則(#253)。本タスクは段階を**引数で受け取る**
- 会話 UI・グレーボックス(#47)。Godot から呼ぶ API の形だけを本タスクが決める
- `{deadline} {budget} {reason} {npcName} {playerTitle}` の展開(GDD01 §3.3.1)
- 台詞の使用履歴を `World` に入れること・状態ハッシュに含めること(下の「配線」)

**変えない既存コード**(規則8):

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `NeedReason` の5値と、M0 で開示する3理由(生産停止・工具切れ・遠方在庫) | GDD01 §6.1 / GDD02b §8.1 | 一致 |
| `Need.Quantity` の単位(3理由では個) | GDD02b §8.1 | 一致(doc コメントと照合) |
| `NeedGenerationSystem` が毎日 hour 0 に `World.Needs` を組み直すこと | TDD01 §3.3 | 未確認(`Cadence.Daily(hour: 0)` だけ見た) |
| `RandomStream.Dialogue = 11` | TDD01 §3.1 | 未確認(値の表は見ていない) |
| `Program.BuildPipeline` の登録順 | TDD01 §3.3 | 未確認(`vsim hash` と同じものを使うだけ) |
| `GameDate.FromTick` の季節 | GDD03 §1.2 | 未確認 |

## 作るもの

置き場所は `src/Visionary.Sim/Dialogue/`(名前空間 `Visionary.Sim.Dialogue`)。**ただし `Gratitude` だけは `src/Visionary.Sim/World/Gratitude.cs`(名前空間 `Visionary.Sim`)** — #253 がシム側で計算して返す値であり、台詞の型ではない。

**#47 と #253 が読む型・API**(`Gratitude` / `LineKind` / `DialogueContext` / `DialogueLine` / `DialogueSelector` / `DialogueMemory` / `DisclosedNeeds`)**の形は本書のとおりにする。変える必要に出会ったら止まって報告する**(02-task-spec「止まる」)。

### 1. 列挙

```csharp
namespace Visionary.Sim;
/// 感謝の段階(GDD01 §3.6)。0 を使わない理由は NeedType と同じ。
public enum Gratitude { Reluctant = 1, Grateful = 2, DeepGratitude = 3 }   // 渋々 / 感謝 / 深い感謝

namespace Visionary.Sim.Dialogue;
/// 台詞の種類(GDD01 §3.3.1 の表の行)。0 を使わない。
public enum LineKind { Greeting = 1, SmallTalk = 2, NeedDisclosure = 3, NoNeed = 4, TradeAccepted = 5, TradeRefused = 6 }
/// 性格(GDD01 §3.3)。M0 の資料はどれも使わない(GDD01 §3.3.1)。0 を使わない。
public enum Personality { Taciturn = 1, Talkative = 2, Servile = 3, Haughty = 4 }   // 寡黙 / 饒舌 / 卑屈 / 尊大
```

### 2. `DialogueTemplate` と `DialogueCorpus`(GDD01 §3.3・§3.3.1)

```csharp
public sealed record DialogueTemplate(
    string Id, LineKind Kind,
    Occupation? Occupation, Season? Season, Gratitude? Gratitude, Personality? Personality,
    string Text);

public readonly record struct ItemWording(string Name, string Counter);   // 表示名と助数詞

public sealed class DialogueCorpus
{
    public DialogueCorpus(IReadOnlyList<DialogueTemplate> templates,
                          IReadOnlyList<ItemWording> items,      // 添字 = Item の Id。長さ Item.Count
                          IReadOnlyList<string> seasonNames);    // 添字 = (int)Season。長さ 4
    public IReadOnlyList<DialogueTemplate> Templates { get; }
    public static DialogueCorpus M0 { get; }                     // 下の 5. の資料から組む
}
```

**コンストラクタが検査し、違反は `ArgumentException`**(資料の誤りを起動時に落とす。テスト #14):

- `Id` が空でなく、資料の中で一意(序数比較)
- `Season` を持てるのは `SmallTalk` だけ。`Gratitude` は **`TradeAccepted` なら必須、それ以外は持てない**(「指定なしの成立」は渋々にも深い感謝にも当たってしまう)
- `Text` のプレースホルダ(`{` と `}` で囲んだ名前)は種類ごとの許可の中だけ。対応の取れない `{` / `}` は違反

  | 種類 | 許可 | 必須 |
  | ---- | ---- | ---- |
  | Greeting / NoNeed | なし | — |
  | SmallTalk | `{season}` | — |
  | NeedDisclosure | `{item}` `{qty}` | **両方**(GDD01 §6.1: 品目と数量が見える) |
  | TradeAccepted | `{item}` `{qty}` | — |
  | TradeRefused | `{item}` | — |

- `items.Count == Item.Count`、`seasonNames.Count == 4`

### 3. 照合と選択 — `DialogueSelector`(GDD01 §3.3.1「照合」「選び方」)

```csharp
public readonly record struct DialogueContext(
    Occupation Occupation, Season Season, Personality? Personality,
    Gratitude? Gratitude, int? ItemId, int? Quantity);

public readonly record struct DialogueLine(LineKind Kind, string TemplateId, string Text);

public sealed class DialogueMemory { public DialogueMemory(); }   // NPC ごとの使用回数と、種類ごとの直前の Id

public static class DialogueSelector
{
    /// 1回の会話につき1度だけ開く。鍵は (masterSeed, RandomStream.Dialogue, tick, npcId)。
    public static RandomSequence OpenConversation(long masterSeed, Tick tick, int npcId);

    public static DialogueLine Say(DialogueCorpus corpus, DialogueMemory memory, ref RandomSequence random,
                                   int npcId, LineKind kind, in DialogueContext context);
}
```

**`Say` の手順**(順序が仕様):

1. **引数の検査**(違反は `ArgumentException`。範囲外の数値は `ArgumentOutOfRangeException`)
   - `context.Gratitude` は `kind == TradeAccepted` なら必須、それ以外なら `null`
   - `ItemId` と `Quantity`: NeedDisclosure / TradeAccepted は両方必須、TradeRefused は `ItemId` だけ必須で `Quantity` は `null`、他の種類は両方 `null`
   - `ItemId` は `0 ≤ ItemId < Item.Count`、`Quantity ≥ 1`
2. **候補** = `corpus.Templates` を**資料の並び順のまま**走査し、`Kind == kind` かつ4軸すべてが当たるもの。軸の当たり方は GDD01 §3.3.1:テンプレートの軸が `null` なら何にでも当たる。値があれば `context` の同じ軸が**等しい値のときだけ**当たる(`context` 側が `null` なら当たらない)。`Season` の軸は `context.Season`、`Gratitude` は `context.Gratitude`、`Personality` は `context.Personality`、`Occupation` は `context.Occupation` と比べる。**和集合である — 職業を指定したテンプレートがあっても、指定なしのものを落とさない**
3. 候補が 0 本なら `InvalidOperationException`(資料の網羅はテスト #16 が守る)
4. **プール** = 候補のうち、`memory` 上でこの `npcId` の使用回数が最小のもの(並び順を保つ)
5. プールが2本以上で、この `npcId` が**この `kind` で直前に使った Id** がプールにあれば、それを除く
6. `random.NextInt(0, プール.Count)` で1本選ぶ。**プールが1本でも必ず1回引く** — 1行あたりの引く回数を一定にし、後の行の選択が前の行のプールの大きさに依存しないようにする
7. `memory` を更新する(使用回数 +1、`(npcId, kind)` の直前の Id)
8. 本文を展開して返す(下の 4.)

**例(順序の固定。規則6)**: 候補が A・B の2本、`memory` が空。1回目はプール {A, B} から乱数で選ぶ(A とする)。2回目は使用回数 A=1・B=0 なのでプール {B}。3回目は A=1・B=1 でプール {A, B}、直前の B を除いて {A}。**以後 A・B が交互に出る。**

**保証と残る穴(規則4)**: 候補が変わらない間、同じ NPC で N 本を使い切るまで重複しない。**ただし**同じ NPC・同じ tick で `OpenConversation` を2度呼ぶと同じ乱数列になる(`memory` が違うので結果は普通は違う)。`SimContext` の二重オープン検出は**ここには掛からない**(`RandomSource` を直接開くため)。M0 は1 tick に同じ NPC と2度話す経路を作らないので許容する。

### 4. 展開(GDD01 §3.3.1「差し込みの展開」)

- `{item}` → `items[ItemId].Name`
- `{qty}` → `漢数字(Quantity) + items[ItemId].Counter`
- `{season}` → `seasonNames[(int)context.Season]`

**漢数字**: 1〜9999 は千・百・十の位を「数字 + 位」で並べ、**位の数字が 1 なら数字を省き(十・百・千)、0 なら位ごと省く**。一の位は 0 なら省く。**10000 以上は算用数字**(`InvariantCulture`)。

| 値 | 1 | 10 | 11 | 20 | 105 | 110 | 1000 | 1001 | 2345 | 9999 | 10000 |
| -- | - | -- | -- | -- | --- | --- | ---- | ---- | ---- | ---- | ----- |
| 出力 | 一 | 十 | 十一 | 二十 | 百五 | 百十 | 千 | 千一 | 二千三百四十五 | 九千九百九十九 | 10000 |

数字の字は 一二三四五六七八九。

### 5. M0 の資料 — `DialogueCorpusM0.cs`(**データだけのファイル**)

`DialogueCorpus.M0` が読む3つの表だけを置く。ロジック(検査・照合・展開)を書かない — 開発者がこのファイルだけを書き直して台詞を調整するためである(決定3)。

**品目の表示名と助数詞**(添字 = `Item` の Id。この表のとおり):

| Id | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
| -- | - | - | - | - | - | - | - | - | - |
| 名前 | 穀物 | 木材 | 鉄鉱石 | 木炭 | 小麦粉 | 薪 | パン | ビール | 工具 |
| 助数詞 | 袋 | 本 | 籠 | 俵 | 袋 | 束 | 個 | 杯 | 本 |

**季節名**: 春・夏・秋・冬(`Season` の値の順)。

**テンプレートの本数**(下限。テスト #16 が数える)。「和集合」は、その文脈で `Say` の候補になる本数、「職業指定」はそのうち `Occupation` を持つ本数。`Personality` はすべて `null`(決定2)。

| 種類 | 文脈の単位 | 和集合の下限 | 職業指定の下限 |
| ---- | ---------- | ------------ | -------------- |
| Greeting | 職業ごと | 8 | 2 |
| SmallTalk | 職業 × 季節ごと | 8 | 2 |
| NeedDisclosure | 職業ごと | 6 | 2 |
| NoNeed | 職業ごと | 6 | 2 |
| TradeAccepted | 職業 × 段階ごと | 4 | 1 |
| TradeRefused | 職業ごと | 4 | 2 |

**本文の書き方**(初稿。開発者が後で書き直す):

- NPC の1回の発話。1〜2文、**展開前の `Text` で60字以内**(テスト #17)
- 中近世の都市の職人・住人の口調。外来語は品目名(パン・ビール)以外に使わない
- **時間帯の挨拶語と、月を使う期限を書かない**: 「おはよう」「こんにちは」「こんばんは」「来月」「ヶ月」「か月」「カ月」(テスト #17)
- **開示は品目と数量が要ることだけを言う。** 理由・背景・期限・予算を言わない(GDD01 §6.1)。「工具が折れて」「仕事が止まって」のように Need の理由が分かる言い方も避ける
- **世間話・挨拶・困りごとは無い、で自分の仕事の不調を語らない**(GDD01 §3.3.1)
- 取引の成立は段階の差が台詞だけで分かるように書く — 渋々は不満を残して受け取る、感謝は礼を言う、深い感謝は救われたと言う
- `Id` は `<種類>.<職業 | any>.<季節 | 段階 | ->.<2桁>` を目安にする(例 `small.baker.winter.01`、`thanks.any.deep.03`)。検査するのは一意性だけ

### 6. 開示する Need — `DisclosedNeeds`(GDD01 §6.1)

```csharp
public static class DisclosedNeeds
{
    /// world.Needs のうち TargetHouseholdId == householdId で、ReasonCode が
    /// ProductionStopped / ToolsExhausted / DistantStock のもの。Need.Id 昇順。
    public static IReadOnlyList<Need> Of(World world, int householdId);
}
```

`World.Needs` の並びに依存しない(必ず `Id` で並べ直す)。

### 7. `vsim dialogue-sample`(TDD01 §4.1)

```
vsim dialogue-sample --npc <int> [--seed <long>] [--start-day <int>] [--repeat <int>] [--out <path>]
```

| 選択肢 | 既定 | 範囲(外れたら終了コード 64 と使い方) |
| ------ | ---- | ------------------------------------ |
| `--npc` | 必須 | `0 ≤ npc < WorldDefinition.M0.NpcCount` |
| `--seed` | 1 | `long` 全域 |
| `--start-day` | 0 | 0 以上 |
| `--repeat` | 10 | 1 以上 |
| `--out` | 標準出力 | 指定時は UTF-8(BOM なし)でそのファイルへ書く。親ディレクトリは作らない |

`--out` を置くのは、Windows のコンソールのコードページで日本語が化けても判定材料を読めるようにするためと、テストが `Console` を差し替えずに出力を読めるようにするためである。`PrintUsage` にも1行足す。

**手順**:

1. 世界と系列は `vsim hash` と同じ:`WorldDefinition.M0` + `WorldGenerator.Generate(definition, new RandomSource(seed))`、`new SimScheduler(BuildPipeline(definition, new NullDailyMetricsSink()), new RandomSource(seed))`。`DialogueMemory` は1つ、`DialogueCorpus.M0`
2. 会話 `i`(0 始まり)の時刻 `T = (startDay + i) × 24 + 12`。`scheduler.Advance(world, (int)(T − world.Now.Value))` で `T` まで進める
   - **例(規則6)**: `--start-day 1 --repeat 3` の会話は tick 36・60・84。最初の `Advance(36)` は tick 0〜35 を処理するので、day 1 の hour 0(tick 24)に組み直した `World.Needs` が見える
3. `h = world.Npcs[npc].HouseholdId`、`occupation = world.Households[h].Occupation`、`season = GameDate.FromTick(T).Season` を**会話ごとに読み直す**(職業は付け替えで変わる)。`random = DialogueSelector.OpenConversation(seed, T, npc)`
4. 行を次の順で `Say` する(`Personality = null`):Greeting → SmallTalk → `needs = DisclosedNeeds.Of(world, h)` が空なら NoNeed、空でなければ各 Need に NeedDisclosure(`ItemId` と `Quantity` は Need のもの)
5. `needs` が空でなければ、**先頭の Need について取引の台詞を1行**。結果は `渋々 → 感謝 → 深い感謝 → 不成立` を巡らせる。**巡る添字は「Need があった会話」だけで進む**(Need の無い会話では進めない)。成立は TradeAccepted(段階・`ItemId`・`Quantity`)、不成立は TradeRefused(`ItemId`)

**出力の書式**(テストが解析するので、この形のとおり):

```
dialogue-sample seed=1 npc=12 start-day=0 repeat=10
== 1/10 day 0 Y0-Spring-00T12 household=6 occupation=Smith
[挨拶] (greet.any.03) <本文>
[世間話] (small.smith.-.01) <本文>
[開示] (need.any.-.02) <本文> <need #34 item=8 qty=2 reason=ToolsExhausted>
[取引の成立/渋々] (thanks.any.reluctant.01) <本文>
== 2/10 day 1 Y0-Spring-01T12 household=6 occupation=Smith
...
== 集計
挨拶: 10行 / 8種 / 最多 greet.any.03 ×2
世間話: 10行 / 10種 / 最多 small.smith.-.01 ×1
開示: 3行 / 3種 / 最多 need.any.-.02 ×1
困りごとは無い: 7行 / 6種 / 最多 none.any.-.01 ×2
取引の成立: 2行 / 2種 / 最多 thanks.any.reluctant.01 ×1
取引の不成立: 0行
```

- 見出しの日付は `GameDate.FromTick(T).ToString()`、`day` は `startDay + i`
- 種類のラベル:挨拶 / 世間話 / 開示 / 困りごとは無い / 取引の成立/渋々・感謝・深い感謝 / 取引の不成立
- 集計は `LineKind` の値の順に6行。取引の成立は段階をまとめて数える。「最多」は使用回数が最大のテンプレート、同数なら**最初に出たもの**。0行の種類は `<ラベル>: 0行` だけ
- 改行は `\n`

## 配線(規則7)

| 項目 | 約束 |
| ---- | ---- |
| 入力の作り方 | `DialogueContext` は呼ぶ側(Runner・#47)が会話の時点の `World` から組む。職業は `Households[npc.HouseholdId].Occupation`、季節は `GameDate.FromTick(Now).Season`。開示する Need は必ず `DisclosedNeeds.Of` を通す — 理由の絞り込みを呼ぶ側で綴らせない(`Need.TypeOf` と同じ理由) |
| 添字・単位 | `ItemWording` の添字は `Item` の Id、`seasonNames` の添字は `(int)Season`。`Quantity` は個(3理由に限るので ‰人日 は来ない)。取り違えても例外は出ない — テスト #12・#17 が値で見る |
| 呼び出し順 | `OpenConversation` を会話の頭で1回、行は会話の中で決まった順に `Say` する。`Say` の引く回数は1行1回なので、順序を変えると以後の選択が変わる |
| 状態の置き場所 | `DialogueMemory` は `World` の外で、呼ぶ側が持つ。**状態ハッシュに入らず、`vsim run` の結果にも影響しない**(台詞は経済に効かない。GDD01 §3.6 の向き)。Godot ではプレイのセッションと同じ寿命で持つ。M0 にセーブは無いので永続化しない |
| tick の中の位置 | 会話はシムの tick の**間**に起きる(`Advance` の外)。`World` を読むだけで書かない |

## 落ちるべき条件(テスト)

#1〜#15 は合成した小さな資料で、`DialogueCorpus.M0` に依存させない(資料を書き直しても落ちないように)。

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心(当てる変異 / 期待) |
| - | ------ | -------- | -------------------- | ------------------------- |
| 1 | `CandidatesAreTheUnionOfGenericAndOccupationSpecific` | 資料 {A: Greeting 職業なし, B: Greeting Baker, C: Greeting Smith}。Baker の NPC と2回話すと {A, B} が1回ずつ出て C は出ない | 職業指定があると指定なしを落とす(最も具体的なものだけ)/ 職業の軸を見ない | **核心**: 手順2 で「職業を指定した候補が1本でもあれば `Occupation == null` の候補を除く」に変える → 赤 |
| 2 | `PersonalityAxisMatchesOnlyAnEqualValue` | Greeting {A: 性格なし, B: Taciturn}。`Personality = null` で8回 → B は一度も出ない。`Personality = Taciturn` で2回 → A・B が出る | 性格の軸を無視する / `context` 側の `null` が値のある軸に当たる | |
| 3 | `SeasonAxisFiltersSmallTalk` | SmallTalk {A: 季節なし, B: Spring}。Summer で8回 → B は出ない。Spring で2回 → 両方 | 季節の軸を見ない | |
| 4 | `GratitudeSelectsOnlyTemplatesOfThatTier` | TradeAccepted を3段階に各2本。各段階で4回ずつ → 出た Id はすべてその段階のもの | 段階の軸を見ない / 段階の値を取り違える | |
| 5 | `UnusedTemplatesComeFirstWithinOneNpc` | Greeting 5本、同じ NPC と5回(会話ごとに tick を変える)→ 5本すべて異なる。シード 1〜20 で全部成り立つ | 手順4 が無い(候補全体から乱数) | **核心**: 手順4 を「プール = 候補全体」に変える → 赤 |
| 6 | `UsageIsCountedPerNpc` | Greeting 5本。NPC 1 と5回話した後、NPC 2 と5回 → NPC 2 でも5本すべて異なる | 使用回数を NPC をまたいで数える | |
| 7 | `NoImmediateRepeatAfterACycle` | Greeting 2本、同じ NPC と20回 → 連続する2回の Id が一度も等しくない(交互)。シード 1〜20 | 手順5 が無い | **核心**: 手順5 を消す → 赤 |
| 8 | `SameKeyGivesSameLines` | 同じ (seed, tick, npc) と新しい `DialogueMemory` で2回会話を組む → 行がすべて一致 | 選択に非決定的な要素が入る | |
| 9 | `NpcsOfOneOccupationDoNotShareOneOrder` | Greeting 8本、同じ職業の NPC 0〜9 それぞれと新しい memory で8回 → 10人の出現順がすべて同じではない | 手順6 で乱数を使わない(常にプールの先頭) | |
| 10 | `EveryLineDrawsExactlyOnce` | 資料 X(Greeting 1本 + SmallTalk 8本)と資料 Y(Greeting 3本 + 同じ SmallTalk 8本)。同じ鍵で Greeting → SmallTalk と話すと、SmallTalk の Id が X と Y で一致 | プールが1本のとき引かない | |
| 11 | `ConversationUsesTheDialogueStream` | `OpenConversation(s, t, n)` の最初の `NextUInt64` が `new RandomSource(s).Open(RandomStream.Dialogue, t, n)` のものと等しく、`RandomStream.Trade` のものと異なる | 別系統から借りる(共通乱数法が壊れる) | |
| 12 | `PlaceholdersExpandToWordsAndKanjiCounts` | NeedDisclosure `"{item}を{qty}"` に工具・2 → `工具を二本`。SmallTalk `"{season}だね"` に Winter → `冬だね`。品目表の工具と薪を入れ替えた資料では `薪を二束` | 添字の取り違え / 助数詞を付けない / 算用数字のまま | |
| 13 | `KanjiNumeralsFollowThePositionRules` | 4. の表の11値と、9・19・99・100・101・999 | 「一十」「一百」と書く / 0 の位を「〇」で出す / 10000 の境界のずれ | |
| 14 | `CorpusRejectsMalformedTemplates` | 次をそれぞれ `ArgumentException`:Id 重複 / `{budget}` / 開示に `{qty}` が無い / Greeting に季節 / TradeAccepted に段階が無い / TradeRefused に段階 / `"{item"` / `items` の長さが 8 | 検査の漏れ(誤った資料が起動を通り、会話の途中で壊れる) | |
| 15 | `SayRejectsInconsistentArguments` | TradeAccepted に段階なし / Greeting に段階あり / 開示に `ItemId` なし / 不成立に `Quantity` あり → `ArgumentException`。`Quantity = 0` → `ArgumentOutOfRangeException`。候補 0 本 → `InvalidOperationException` | 呼ぶ側(#47)の誤りが黙って別の台詞になる | |
| 16 | `M0CorpusMeetsTheCoverageFloor` | `DialogueCorpus.M0` を、5. の本数の表の全文脈(職業5 × 季節4 × 段階3 の組み合わせを種類ごとに)で数え、下限を満たす | 本数が足りない / 軸の付け間違い(例: 季節を付けた挨拶は検査で落ちるが、職業の付け間違いはここでしか出ない) | |
| 17 | `M0CorpusFollowsTheWritingRules` | `DialogueCorpus.M0` が例外なく組める。全 `Text` が60字以内で、禁止語(5. の7語)を含まない。品目表と季節名が 5. の表と一字一句一致 | 品目表のずれ(ビールに「パン」)/ 禁止語 / 長すぎる行 | |
| 18 | `DisclosedNeedsAreTheThreeTradableReasonsSortedById` | 世帯 h に5理由の Need を Id の逆順で積み、別の世帯にも1件 → 結果は h の3理由だけで Id 昇順 | 困窮・増産できないを含める / 並べ直さない / 他の世帯を含める | **核心**: 理由の条件に `CannotExpandProduction` を足す → 赤 |
| 19 | `DialogueSampleWritesTheRequestedConversations` | `Program.Execute(dialogue-sample --npc 0 --seed 1 --start-day 2 --repeat 3 --out <tmp>)` が 0 を返し、見出しが3つで `day 2・3・4`、時刻が `T12`。各会話に挨拶と世間話が1行ずつ。同じ引数で2回 → バイト単位で一致 | 時刻の計算のずれ / 決定論の破れ | |
| 20 | `DialogueSampleDisclosesTheNeedsStandingThatDay` | テスト内で同じ世界を独立に回し、seed 1 の day 0〜29 で `DisclosedNeeds.Of` が空でない最初の (day, npc) を探す(無ければテストを失敗させる)。その npc・day で `--repeat 1` → `[開示]` 行がその Need の数だけあり、本文に品目名と漢数字の数量を含み、末尾の `<need #…>` が Need と一致。取引の行は `[取引の成立/渋々]` | Need を読まない / 世帯ではなく NPC の Id で引く / 巡りの初期値のずれ | |
| 21 | `DialogueSampleRotatesOutcomesOnlyOnNeedDays` | 20 と同じ npc を `--start-day 0 --repeat 30` で → 取引の行のラベルの列が `渋々, 感謝, 深い感謝, 不成立, 渋々, …` の先頭部分に一致。**取引の行が2行以上あることを前提として確かめる**(無ければ失敗) | Need の無い会話でも添字を進める(Need の無い日を挟むと列が飛ぶ) | |
| 22 | `DialogueSampleSummaryMatchesTheLines` | 19 の出力の集計6行が、同じ出力の本文の行から数えた行数・種類数・最多と一致 | 集計の数え間違い / 段階別に分けて数える | |
| 23 | `DialogueSampleRejectsBadOptions` | `--npc` なし / `--npc` = NpcCount / `--npc -1` / `--repeat 0` / `--start-day -1` → 64 | 範囲の検査漏れ(範囲外の NPC で `IndexOutOfRangeException`) | |

**#21 の検出力の穴(規則4)**: 選んだ npc で Need のある日が連続していると、添字の進め方を誤っても列は変わらない。**Need の無い日が Need のある日の間に挟まるときだけ落ちる。** テストは「取引の行が2行以上」までしか前提にしないので、seed 1 でその並びにならなければこのテストは空振りする。implementer は、選ばれた npc の30日の Need の有無の並びを doc コメントに書く(空振りかどうかを読者が判断できるように)。

## 編集してよい文書

- なし(コードとテストのみ)。**資料 `DialogueCorpusM0.cs` はコードである**

## このタスクで特に効く規約

- **候補の順序は資料の並び順だけで決める。** 使用回数を `Dictionary` で持ってよいが、その**列挙結果**で候補やプールを並べない(CLAUDE.md 決定論)
- **乱数は `Dialogue` 系統だけ。** `OpenConversation` の外で `RandomSource` を開かない
- 文字列の比較は序数(`StringComparer.Ordinal`)。カルチャ依存の比較・書式を使わない(数値の書式は `InvariantCulture`)

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **「核心」印の変異を `mutator` が実測し(レビューの巡が閉じた後)、結果を doc コメントへ転記した**
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
- [ ] `vsim dialogue-sample --npc <#20 の npc> --repeat 10 --out <path>` の出力をコミットせずに PR 説明へ貼る(開発者が §6.2 #3 の判定に使う)
