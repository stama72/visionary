# 開発プロセス文書

**開発プロセスにとっての GDD/TDD にあたる層。** エージェント運用・レビュー規律・仕様の書き方の「今どうなっているか」を持ち、実態と乖離したら直す。

文書の四層(CLAUDE.md「ドキュメント運用」)のうち**育てる層**に属する。ゲームの仕様は GDD、技術の仕様は TDD、**プロセスの仕様は本ディレクトリ**が持つ。

| #  | 文書 | 内容 |
| -- | ---- | ---- |
| 01 | [レビューの規律](01-review.md) | 守備範囲(「気付けない × 影響大」)、工程ごとの分担、減衰と打ち切り、網羅パス、「気付いたら直す」 |
| 02 | [タスク仕様の書き方](02-task-spec.md) | 実装エージェントに渡す仕様の書き方。W1-02/W1-03 の失敗8件から作った規則6つ + W2-05 から1つ + W2-08(#101)から1つ |
| 03 | [訂正の作法](03-corrections.md) | 指摘を受けて**直すとき**の規則2つ(広い保証 / 却下理由の空洞化)。W1-04 の8件のうち、踏んでも気付けないものだけを残した |
| 04 | [issue 運用](04-issue-driven.md) | 進行・スコープ・優先順位を issue が持つときの運用。切り分け・階層・ラベル・WIP・未決事項の仕分け・上位文書を書き換える設計 issue の追随表 |
| 05 | [フェーズごとにセッションを切る](05-phase-sessions.md) | 1タスクを3フェーズに切る運用。引き継ぎメモに何を書き、何を書かないか |
| 06 | [設計工程の進め方](06-design-sessions.md) | 設計タスク(`docs/` の規則・仕様・設定を書き換える。層の表)の運用。仕事の5分割と分担、決めて報告 / 止まって報告、決定ログ、束で切る、モデルは仕事で決める |

関連:

- [docs/tasks/](../tasks/) — タスク仕様のファイル名とテンプレート(**使い捨て層**)。**状態一覧は GitHub の issue へ移した**([ADR-0006](../adr/0006-issue-driven-task-management.md) 論点1)
- [`.claude/agents/`](../../.claude/agents/) — 各エージェントの憲章(**実行される仕様**)
- [`.claude/commands/learn.md`](../../.claude/commands/learn.md) — 学習セッションの枠づけ
- [`.claude/agents/adviser.md`](../../.claude/agents/adviser.md) — 全般アドバイザーの憲章(設計・プロセス工程で `reviewer` の代わりに使う)
- [`.claude/agents/adviser-economy.md`](../../.claude/agents/adviser-economy.md) — 経済アドバイザーの憲章(全般より先に呼ぶ)
- [`.claude/agents/propagator.md`](../../.claude/agents/propagator.md) — 機械的な波及の憲章(設計セッションが本文を書き、波及だけを出す)
- [`.claude/commands/advise.md`](../../.claude/commands/advise.md) — アドバイザーを手で呼ぶ入口(設計セッションの外で書いた差分用)
- [`.claude/commands/design.md`](../../.claude/commands/design.md) — 設計セッションの枠づけ(06 の実行される仕様)
- [`.claude/commands/impl.md`](../../.claude/commands/impl.md) / [`.claude/commands/wrap.md`](../../.claude/commands/wrap.md) — フェーズ2・フェーズ3 の枠づけ

## 「実行される仕様」という位置づけ

`.claude/agents/*.md` と `.claude/commands/*.md` は**文書ではなく設定**である。ハーネスが読んで動く。

したがってこれらは**それ自体が正**であり、本ディレクトリの文書はその本文を複製しない。本ディレクトリが持つのは**現行の運用**(憲章の所在、憲章の外にある規律、書き方の規則)である。

**「なぜその決定に至ったか」(検討した選択肢と却下理由)は ADR が持つ。** 本ディレクトリは決定の理由を再説明しない。

コードの場合は仕様(GDD/TDD)と実装(C#)を分けるが、それは表現形式が違うからである。憲章は最初から散文なので、分けるとドリフトを作るだけになる。

## ADR との関係

- **ADR は「選択肢と理由」を記録して凍る。** プロセスに関する ADR は [ADR-0004](../adr/0004-ai-driven-development-workflow.md)(エージェントの役割分担)、[ADR-0005](../adr/0005-reviewer-scope-includes-spec-defects.md)(レビュアーの守備範囲)、[ADR-0006](../adr/0006-issue-driven-task-management.md)(issue 駆動のタスク管理)、[ADR-0008](../adr/0008-review-scope-narrowed-to-unnoticeable-defects.md)(レビューの守備範囲と効率と質の両立)、[ADR-0009](../adr/0009-phase-scoped-sessions.md)(フェーズごとにセッションを捨てる)
- **本ディレクトリは「今の運用」を持って育つ。** 運用のフィードバックは、まずここと `.claude/` に反映する。**プロセスについて新たに ADR を起こすのは、過去の ADR の決定を覆すときに限る**

この分業がなかったため、W1-03 のフィードバックを反映する場所が構造上なく、ADR-0005 を起こすしかなかった。

## 本ディレクトリの未決事項について

**未決事項は [GitHub の issue](https://github.com/stama72/visionary/issues?q=is%3Aissue+is%3Aopen+label%3Atype%3Aprocess) が持つ。** 本書がかつて末尾に持っていた5件(ADR の帰結を2節構成にするか、多論点 ADR の扱い、本ディレクトリの分冊構成、ADR テンプレートの標準化、ADR-0001〜0003 への遡及適用)は issue へ移した([ADR-0006](../adr/0006-issue-driven-task-management.md) 論点2)。

**育てる文書は「今どうなっているか」だけを持つ。** 決まっていないことを同じ文書の末尾に溜めると、優先順位も期日も付けられないまま増える。
