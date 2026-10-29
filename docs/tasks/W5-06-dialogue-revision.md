# W5-06: 台詞の資料を §6.2 #3 の判定結果で直す(続きの型・書き方の規則・本数)

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#273](https://github.com/stama72/visionary/issues/273)(決定 R1〜R9 は issue のコメント「#259 台詞レビューの決定ログ」) |
| 根拠     | [GDD01 §3.3.1(本タスクの凍結コミットで R1 と書き方の規則を書いた)・§3.6・§6.1・§6.2 #3](../03-gdd/01-trust-and-conversation.md) / [GDD06 §6.1](../03-gdd/06-trade-and-negotiation.md) / [TDD01 §4.1](../04-tdd/01-sim-core-and-m0.md) / W5-01(`docs/tasks/W5-01-dialogue-templates.md`)の型と手順 |
| ブランチ | `feat/273-dialogue-revise`                                           |
| worktree | 本体(パイプラインが使う)                                           |

**W5-01 が作った台詞の照合に「開示の1件目か続きか」の軸を足し、M0 の資料を #273 の決定に沿って書き直す。** 規則は GDD01 §3.3.1 が持つ。本書は「どう作るか」だけを書く。W5-01 の型・手順・テストのうち、本書が触れないものはそのまま生きる。

## スコープ

**含む:** 開示の順の軸(型・検査・照合・直前の Id の持ち方)、`vsim dialogue-sample` がその軸を渡すこと、`DialogueCorpusM0.cs` の本文の書き直しと本数の追加、資料のテスト(本数の下限と書き方の規則)の更新。

**含まない:** 開示で理由・用途を語らせること(#271、M1)。同じ Need を毎日初めて聞く言い方で開示すること(#272、M1)。Godot 側の会話 UI(#47。本タスクが決める型の形を読むだけ)。`dialogue-sample` の出力書式の変更。

**変えない既存コード**(規則8):

| 変えないコード(規則の単位) | 従う節(現行版) | 確認 |
| --------------------------- | --------------- | ---- |
| `DialogueSelector.Say` の手順2〜8(和集合・使用回数最小のプール・1行1回引く・展開)。**変えるのは順の軸の照合と、手順5 の「直前の Id」の鍵だけ** | GDD01 §3.3.1「照合」「選び方」 | 一致(本書を書く際に突き合わせた) |
| `KanjiNumerals.Format` と、`{item}` `{qty}` `{season}` の展開 | GDD01 §3.3.1「差し込みの展開」 | 一致 |
| 品目の表示名・助数詞の表と季節名(`DialogueCorpusM0.Items` / `SeasonNames`) | GDD01 §3.3.1 / W5-01 §5 の表 | 一致 |
| `DisclosedNeeds.Of`(3理由・Need Id 昇順) | GDD01 §6.1 | 一致 |
| `DialogueSampleCommand` の時刻・巡り・出力書式・集計(`[開示]` のラベルは1件目も続きも同じ) | TDD01 §4.1 / W5-01 §7 | 一致 |
| `OpenConversation` の鍵と系統 `Dialogue` | TDD01 §3.1 | 一致 |

## 作るもの

置き場所は W5-01 と同じ `src/Visionary.Sim/Dialogue/`。

### 1. 開示の順 — `DisclosureOrder`(GDD01 §3.3.1「開示の2件目以降は『続き』の型から選ぶ」)

```csharp
namespace Visionary.Sim.Dialogue;
/// 開示の何件目か(GDD01 §3.3.1)。0 を使わない。
public enum DisclosureOrder { First = 1, Continued = 2 }   // 1件目 / 2件目以降
```

`DialogueTemplate` と `DialogueContext` の **`Gratitude` の直後に `DisclosureOrder? Order` を足す**(既定値を付けない。呼ぶ側に書き忘れさせない):

```csharp
public sealed record DialogueTemplate(
    string Id, LineKind Kind,
    Occupation? Occupation, Season? Season, Gratitude? Gratitude, DisclosureOrder? Order, Personality? Personality,
    string Text);

public readonly record struct DialogueContext(
    Occupation Occupation, Season Season, Personality? Personality,
    Gratitude? Gratitude, DisclosureOrder? Order, int? ItemId, int? Quantity);
```

**#47 が読む型の形は本書のとおりにする。変える必要に出会ったら止まって報告する**(02-task-spec「止まる」)。

### 2. 検査(`DialogueCorpus` のコンストラクタと `Say` の手順1)

段階(`Gratitude`)と同じ形にする — 「指定なしの開示」は1件目にも続きにも当たってしまうからである。

- **資料**: `Order` は **`NeedDisclosure` なら必須、それ以外は持てない**。違反は `ArgumentException`
- **`Say` の引数**: `context.Order` は `kind == NeedDisclosure` なら必須、それ以外なら `null`。違反は `ArgumentException`。検査の位置は `Gratitude` の検査の直後

### 3. 照合と選択(`Say` の手順2・5)

- **手順2**: 4軸に `Order` を足して5軸にする。当たり方は他の軸と同じ(テンプレート側が `null` なら何にでも当たる。値があれば `context.Order` が等しい値のときだけ)。資料の検査により開示のテンプレートは必ず値を持つので、**1件目の開示には1件目の型だけ、続きには続きの型だけが当たる**
- **手順5**: 「この `npcId` がこの `kind` で直前に使った Id」を **`(npcId, kind, Order)` ごとの直前の Id** にする(`Order` は開示以外では `null`)。`DialogueMemory` の内部の鍵を広げる。**使用回数(手順4)はテンプレート Id ごとのまま変えない**
  - **理由**: 鍵が `(npcId, kind)` のままだと、Need が2件以上ある会話では直前の Id が必ず続きの型になり、翌日の1件目の候補から何も除かれない。一巡した直後に1件目の同じ台詞が続くのを防げなくなる(GDD01 §3.3.1 選び方の2)
  - **例(規則6)**: 資料 {F1, F2 = 1件目, C1 = 続き}。会話ごとに 1件目 → 続き と話す。会話1: プール {F1, F2} から F1、続き C1。会話2: 使用回数 F1=1・F2=0 でプール {F2}。会話3: F1=1・F2=1 でプール {F1, F2}、**1件目の直前 F2 を除いて {F1}**。鍵が `(npcId, kind)` だと直前は C1 で何も除かれず、F2 が2回続きうる

**保証と残る穴(規則4)**: 1件目・続きのそれぞれで、候補が変わらない間、同じ NPC で N 本を使い切るまで重複しない。**ただし**同じ会話の中で続きの型の候補が尽きれば(候補 6 本に対して続きが 7 件以上)、使用回数最小のプールが一巡して同じ会話の中で同じ続きが出うる。M0 の1世帯の開示は seed 1 の30日で最大5件(#273 の材料)なので、下の下限 6 で同じ会話の中の重複は起きない。

### 4. `vsim dialogue-sample`(W5-01 §7 の手順4)

各 Need の開示行に、**`DisclosedNeeds.Of` の並びで先頭なら `Order = First`、2件目以降なら `Continued`** を渡す。出力の書式・ラベル(`[開示]`)・集計(開示は1件目と続きをまとめて数える)は変えない。取引の行(手順5)の文脈には `Order = null` を渡す。

### 5. M0 の資料 — `DialogueCorpusM0.cs` の書き直し

**データだけのファイルであることは変えない**(W5-01 決定3)。開示の書き口 `D` に順の引数を足すか、1件目用と続き用の2つの書き口に分ける(どちらでもよい。判断を含まないこと)。クラスの `<remarks>` の書き方の規則は、GDD01 §3.3.1 の「書き方の規則」を指す形に書き換える(W5-01 §5 の「深い感謝は救われたと言う」を消す)。

**本数の下限**(テスト #16 が数える。和集合・職業指定の意味は W5-01 §5 と同じ。太字が本タスクで変えたところ):

| 種類 | 文脈の単位 | 和集合の下限 | 職業指定の下限 |
| ---- | ---------- | ------------ | -------------- |
| Greeting | 職業ごと | **10**(R4) | 2 |
| SmallTalk | 職業 × 季節ごと | **10**(R4) | 2 |
| NeedDisclosure / **First** | 職業ごと | 6 | 2 |
| **NeedDisclosure / Continued** | 職業ごと | **6** | **0** |
| NoNeed | 職業ごと | **10**(R5) | 2 |
| TradeAccepted | 職業 × 段階ごと | 4 | 1 |
| TradeRefused | 職業ごと | 4 | 2 |

続きの下限 6 は、1回の会話の続きの最大件数(4)を上回り、翌日の続きがまるごと前日と同じにならない本数として置いた初期値である。続きは短いつなぎなので職業指定を要求しない。

**書き直す本文**(#273 の決定ごと。Id は `<種類>.<職業 | any>.<季節 | 段階 | first | cont | ->.<2桁>` を目安にする。開示は `need.any.first.01`・`need.any.cont.01` の形):

| 決定 | 対象 | やること |
| ---- | ---- | -------- |
| R1 | 開示 | 既存の開示 14 本を1件目の型にする。続きの型を 6 本以上足す。続きは前の行に繋ぐ短い言い方(「それと、{item}を{qty}。」「あとは{item}を{qty}だな。」)で、**展開前の `Text` で25字以内** |
| R2 | 開示(1件目) | 職業名の名乗り(「粉屋の頼みだ」「醸造屋だが」「パン屋に届けて」「木工の家に」「鍛冶場まで」)を消す。品目と数量だけを頼み、**職業の差は口調で出す**(職業指定の 2 本は残す)。`{qty}ほど` も消す(R8) |
| R3 | 世間話 | `small.any.-.02`〜`.05`(1回きりの出来事)を習慣・格言・季節の様子の型に書き換える |
| R4 | 挨拶・世間話 | 和集合を 8 → 10 にする本文を足す(職業指定・指定なしのどちらで足してもよい) |
| R5 | 困りごとは無い | 職業名を名乗る5本(`none.miller.-.01` `none.baker.-.01` `none.brewer.-.01` `none.woodworker.-.01` `none.woodworker.-.02`)を書き直す。仕事場の順調さを理由にする型(「水車は回っているし」)は残してよい。和集合を 6 → 10 にする本文を足す |
| R6 | 取引の成立 | 3段階を値段の妥当さで語る。**渋々 = 高い**(「高いが、仕方ない」)/ **感謝 = 妥当な値だ**(「まっとうな値だ、有難う」)/ **深い感謝 = この値で譲ってくれるのか**(気前がいい)。「救われた」「命の恩人」「恩人」を使わない |
| R7 | 取引の成立 | 職業名の名乗り(「粉屋の足元を見る」「醸造屋だからと」「鍛冶場へ運んで」)を消す。職業の差は口調で出す |
| R8 | 全種類 | `{qty}` に量を強調・ぼかす語を付けない(`{qty}も` `{qty}ほど` `{qty}ばかり` `{item}の{qty}`) |
| R9 | 取引の不成立 | 「その値では{item}は買えん」までにする。理由(手持ち・蓄え・懐・立ち行かん・樽ひとつ空けられん)と再提示の誘い(「もう少し何とかならんか」「安くならんか」)を消す。「また今度」は書いてよい。職業指定の 2 本は口調で差を出す |

W5-01 §5 の他の規則(1〜2文・展開前60字以内・外来語・時間帯の挨拶語と月の禁止・開示は理由を言わない・不調を語らない)はそのまま守る。

## 配線(規則7)

| 項目 | 約束 |
| ---- | ---- |
| 入力の作り方 | `context.Order` は呼ぶ側(Runner・#47)が `DisclosedNeeds.Of` の並びから決める。**先頭の Need が `First`、残りが `Continued`。** 1件目を Need Id で選び直さない — 並びは `DisclosedNeeds.Of` が Id 昇順で固定している |
| 呼び出し順 | 開示の行は `DisclosedNeeds.Of` の並び順に `Say` する(W5-01 と同じ)。1行1回引くことは変わらない |
| 状態の置き場所 | `DialogueMemory` の鍵が広がるだけで、`World` の外・状態ハッシュの外であることは変わらない。**`vsim hash` と `vsim run` の結果は変わらない** |
| 取り違え | `Order` を逆に渡しても例外は出ない(どちらも候補がある)。テスト #5 が値で見る |

## 落ちるべき条件(テスト)

#1〜#4 は W5-01 と同じく合成した小さな資料で、`DialogueCorpus.M0` に依存させない。**既存の W5-01 のテストは、型の引数が増えた分の書き換え以外で弱めない**(候補・プール・巡りの検査を消さない)。

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心(当てる変異 / 期待) |
| - | ------ | -------- | -------------------- | ------------------------- |
| 1 | `DisclosureOrderSelectsOnlyTemplatesOfThatOrder` | 資料 {F1, F2 = 1件目, C1, C2 = 続き}。同じ NPC と `First` で4回 → 出た Id はすべて F。`Continued` で4回 → すべて C | 順の軸を照合しない(1件目に続きの型が混ざる) | **核心**: 手順2 の `Order` の照合を常に真にする → 赤 |
| 2 | `LastIdIsTrackedPerDisclosureOrder` | 資料 {F1, F2 = 1件目, C1 = 続き}。1回の会話で `First` → `Continued` と話す会話を、会話ごとに tick を変えて10回 → 連続する2会話の1件目の Id が一度も等しくない。シード 1〜20 で全部成り立つ | 直前の Id を `(npcId, kind)` で持つ(続きの型が直前を上書きし、1件目の一巡直後の重複を防げない) | **核心**: `DialogueMemory` の直前の Id の鍵から `Order` を外す → 赤 |
| 3 | `CorpusRejectsMalformedTemplates`(W5-01 #14 に足す) | 既存の8件に加えて: 開示に `Order` が無い / Greeting に `Order` がある / TradeAccepted に `Order` がある → それぞれ `ArgumentException` | 順の検査の漏れ(指定なしの開示が1件目にも続きにも出る) | |
| 4 | `SayRejectsInconsistentArguments`(W5-01 #15 に足す) | 既存の検査に加えて: 開示に `Order` が無い / Greeting に `Order` がある / TradeAccepted に `Order` がある → `ArgumentException` | 呼ぶ側(#47)の書き忘れが黙って別の台詞になる | |
| 5 | `DialogueSampleUsesContinuedTemplatesFromTheSecondDisclosure` | テスト内で同じ世界を独立に回し、seed 1 の day 0〜29 で `DisclosedNeeds.Of` が2件以上の最初の (day, npc) を探す(無ければテストを失敗させる)。その npc・day で `--repeat 1` → `[開示]` 行の Id を `DialogueCorpus.M0.Templates` で引くと、1行目が `First`、2行目以降がすべて `Continued` | Runner が全件に `First` を渡す / 全件に `Continued` を渡す / 添字の取り違え | **核心**: Runner で全件に `First` を渡す → 赤 |
| 6 | `M0CorpusMeetsTheCoverageFloor`(W5-01 #16 を置き換え) | `DialogueCorpus.M0` を、5. の本数の表の全文脈(職業5 × 季節4 × 段階3 × 開示の順2 の組み合わせを種類ごとに)で数え、下限を満たす | 本数が足りない / 順の付け間違い(続きを1件目にした等) | |
| 7 | `M0CorpusFollowsTheWritingRules`(W5-01 #17 に足す) | 既存の検査(60字・禁止語7語・性格なし・品目表・季節名)に加えて、下の「機械で見る書き方の規則」をすべて満たす | 書き直し漏れ(名乗り・救済・量のぼかし・不成立の理由が残る)/ 続きが長い | **核心**: 資料の深い感謝の1本に「救われた」を足す → 赤 |
| 8 | 既存の W5-01 #19〜#23(`dialogue-sample`) | 変えずに緑 | 開示の順を渡したことで巡り・集計・決定論が崩れる | |

**機械で見る書き方の規則**(テスト #7。展開前の `Text` に対して、序数比較の部分文字列で見る):

| 対象 | 含まないこと | 決定 |
| ---- | ------------ | ---- |
| 全種類 | `{qty}も` `{qty}ほど` `{qty}ばかり` `の{qty}` | R8 |
| 開示・困りごとは無い・取引の成立・取引の不成立 | 職業名 `粉屋` `パン屋` `醸造屋` `木工` `鍛冶屋` | R2・R5・R7 |
| 取引の成立 | `救` `恩人` | R6 |
| 取引の不成立 | `手持ち` `蓄え` `懐` `何とか` `安く` `ならんか` `ならないか` | R9 |
| 開示 / Continued | 展開前の `Text` が25字を超える | R1 |

**機械で見ない規則(残る穴。規則4)**: R3(1回きりの出来事か)、R6 の肯定側(段階が値段の妥当さで語られているか)、R2・R7 の「職業の差を口調で出す」、`鍛冶場` のような仕事場の名での名乗り(R5 が仕事場の様子を許すので語では切れない)は**テストで見ない。** 開発者が下の判定材料を読んで §6.2 #3 を判定するときに見る(#273 の閉じる条件)。

## 編集してよい文書

- なし(コードとテストのみ)。**GDD01 §3.3.1 は本タスクの凍結コミットで書いた。フェーズ2 は触らない。** 資料 `DialogueCorpusM0.cs` はコードである

## このタスクで特に効く規約

- **候補の順序は資料の並び順だけで決める。** `DialogueMemory` の鍵を広げても、その列挙結果で候補やプールを並べない(CLAUDE.md 決定論)
- 乱数は `Dialogue` 系統だけ。1行1回引くことを変えない
- 文字列の比較は序数(`StringComparer.Ordinal` / `StringComparison.Ordinal`)

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **「核心」印の変異を `mutator` が実測し(レビューの巡が閉じた後)、結果を doc コメントへ転記した**
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
- [ ] **判定材料を PR 説明へ貼る**(コミットしない。それぞれ `<details>` で畳む): `vsim dialogue-sample --npc 2 --repeat 30`、`--npc 8 --repeat 30`、`--npc 2 --start-day 30 --repeat 10`・`--start-day 60 --repeat 10`・`--start-day 90 --repeat 10`(すべて seed 1)。**PR は #273 を閉じない**(`Refs #273`)— 閉じる条件は、開発者がこの材料で §6.2 #3 を合格と判定することである
