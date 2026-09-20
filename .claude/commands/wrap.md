---
description: フェーズ3(文書更新とPR)として枠づけする。コードは触らない
argument-hint: <issue番号>
model: sonnet
---

このセッションは **フェーズ3 — 文書更新と PR** です([ADR-0009](../../docs/adr/0009-phase-scoped-sessions.md) / 運用は [docs/process/05-phase-sessions.md](../../docs/process/05-phase-sessions.md))。

対象: issue #$ARGUMENTS

## 最初に読むもの

1. **引き継ぎメモ**(`docs/tasks/W*.handoff.md`)
2. `git log master..HEAD`(**本文まで**。レビュー指摘の内容と対応理由はここにあります)
3. `git diff master...HEAD --stat`
4. タスク仕様(`docs/tasks/W*.md`)と issue #$ARGUMENTS

**コードの中身は読まなくて構いません。** 必要になったら、それは PR 説明に書くべきことが実装にしか無いということなので、その旨を報告してください。

## してはいけないこと

- **コードとテストを触らない**
- **新しい設計判断をしない。** 文書を直すのに判断が要ると分かったら、**そこで止めて報告してください**(停止コード `SPEC-OUTSIDE`)。フェーズ1 に戻す案件です。**止まったこと自体を PR 説明の検証節に1行残します**(下記)
- **ADR を書き換えない**(状態欄の更新を除く)

## すること

1. **GDD / TDD の実態合わせ。** 実装が文書と食い違っている箇所を、**文書側を実態に合わせる**方向でのみ直す。どちらが正しいか判断が要るなら止まる
2. **PR 説明を組む。** 構成は [PR #64](https://github.com/stama72/visionary/pull/64) を範とする:

   | 節 | 材料 |
   | -- | ---- |
   | 変更 | `git diff --stat` と型ごとの要約 |
   | 設計判断 | コミット本文と doc コメント。**却下した案は引き継ぎメモ** |
   | レビュー N巡 | 巡ごとの件数は引き継ぎメモ、各指摘の内容と対応は**コミット本文** |
   | 切り出したもの | この間に立てた issue |
   | 検証 | build / test / format を**再実行**し、変異の結果はコミットか doc コメントから |
   | 仕様品質の先行指標 | 引き継ぎメモの件数。**採れなかったなら採れなかったと書く** |

   **検証の節に、必ずもう1行入れてください** — 「**引き継ぎメモの不足で止まった・やり直した回数: N 件**」(0 件なら「0 件」と書く)。[ADR-0009](../../docs/adr/0009-phase-scoped-sessions.md) の検証条件です。**書かなければ、0 件だったのか誰も記録しなかったのかが区別できません。**

3. **引き継ぎメモを削除する。** 転記が終わった時点で正は PR 説明です。同じブランチの最後のコミットで消すので、PR の差分には現れません
4. **`gh pr create`。** 本文の末尾に `🤖 Generated with [Claude Code](https://claude.com/claude-code)` を付ける
5. **issue の後始末。** `Closes #$ARGUMENTS` を本文に入れ、切り出した issue にラベルを付ける

### ラベルは `gh` CLI で付け、付いたことを確かめる

**手段を1つに固定します。** 毎回選び直すと、通らない経路を引いて**ラベルの無い issue が残ります**([#102](https://github.com/stama72/visionary/issues/102))。

```
gh issue edit <番号> --add-label "type:impl" --add-label "P2"
gh issue view <番号> --json labels
```

- **`mcp__github__issue_write` を使わない。** 無人実行の許可一覧に無いので必ず弾かれます(同じ理由で、PR を読むのも `mcp__github__pull_request_read` ではなく `gh pr view` です — W2-07 の走行でここに 4 回払っています)
- **ラベルは1つずつ `--add-label` で渡す。** コロンを含む名前(`type:process`)はそのまま引用符で通ります
- **付けたら `gh issue view --json labels` で確かめる。** 確かめないと、静かに付かなかった場合に誰も気付きません

型(`type:*`)と優先度(`P*`)の意味は [04-issue-driven](../../docs/process/04-issue-driven.md)。**自動付与はしません** — 静かに壊れる機械を増やさないという決定です。

## 「気付いたら直す」

リンク切れ・表記揺れ・古い件数は、**見つけたらこのブランチで直します**。issue も別 PR も立てません(5分を超えるときだけ issue へ落とす)。[01-review](../../docs/process/01-review.md)

## 終わり方 — 合図を出す

**最後の1行を、次のどちらかにしてください。**[ADR-0010](../../docs/adr/0010-phase-pipeline-and-halt-conditions.md) のパイプラインはこの行だけを見ています。

| 出す行 | いつ |
| ------ | ---- |
| `PIPELINE: DONE` | PR を作り終えた |
| `PIPELINE: HALT <コード> <1行の理由>` | 下の停止則に当たった |

| コード | 条件 |
| ------ | ---- |
| `SPEC-OUTSIDE` | 文書を直すのに新しい設計判断が要る(フェーズ1 の仕事) |
| `RED` | build / test / format の再実行が赤だった |

**どちらも出さずに終わると、パイプラインは `NO-SENTINEL` として停止扱いにします。** 判らないまま進めるより止めるほうが安いためです。
