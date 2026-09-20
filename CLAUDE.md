# Visionary(仮題)

中近世都市を舞台にした商人ライフシム。エージェントベースの経済シミュレーションが中核。
プロジェクト全体は [docs/README.md](docs/README.md) を参照。

現在は **M0プロトタイプ**。**期日と進捗の正は [GitHub Milestone](https://github.com/stama72/visionary/milestones)**、各段階で何を作るかは [TDD01 §5.4](docs/04-tdd/01-sim-core-and-m0.md)、マイルストーンの定義と Exit Criteria は [企画書 §6](docs/02-project-proposal.md) が持つ。

## 構成

| パス                       | 内容                                                              |
| -------------------------- | ----------------------------------------------------------------- |
| `docs/`                    | 企画・GDD・TDD・ADR。**設計判断は必ずここが正**                   |
| `src/Visionary.Sim/`       | 経済シミュレーション本体。純C#(net8.0)、**Godot参照ゼロ**         |
| `src/Visionary.Sim.Runner/` | ヘッドレス実験ハーネス(`vsim`)。M0の比較実験はすべてここから実行 |
| `src/Visionary.Game/`      | Godot 4プロジェクト。**W5で作成予定・現時点では未作成**            |
| `tests/Visionary.Sim.Tests/` | xUnit                                                           |

## 技術規約

- **Godot 4 + C#**([ADR-0001](docs/adr/0001-engine-and-simulation-architecture.md))。`Visionary.Sim` に Godot 依存を持ち込まない。CIをUbuntuで回しているのはこの制約の継続検証を兼ねる
- Godot の作例は GDScript / Godot 3系が多い。**C# かつ Godot 4 系のAPIであることを公式クラスリファレンスで確認してから書く**
- **テストメソッド名は英語**

### 決定論([ADR-0002](docs/adr/0002-time-model-and-determinism.md))

M0の比較実験はすべて決定論に依存する。以下はレビュー観点でもある:

- 1 tick = ゲーム内1時間。時刻の真実は `Visionary.Sim` のみが持ち、Godot側で独自にゲーム時間を進めない
- **浮動小数点をシム状態と計算に使わない。** 金額・信用はint、比率係数は千分率(‰)の整数。除算は切り上げヘルパー経由(GDD01 §2.3「全計算式の結果は小数点以下切り上げ」)
- 係数の定数定義には**単位のコメントを必須**とする(`alphaPermille = 200` は 20% の意)
- 乱数は系統別ストリームから取る。**系統をまたいで乱数を借用しない**(共通乱数法が壊れ、A/B比較が無意味になる)
- `Dictionary` など列挙順が保証されないコレクションの**列挙結果をロジックに使わない**。NPCの処理順はId昇順で固定

## コマンド

```
dotnet build Visionary.sln -c Release     # 警告はエラーとして扱われる
dotnet test  Visionary.sln -c Release
dotnet format Visionary.sln               # CIのフォーマット検証を通す
```

## スコープと優先順位

方針は**効率と質の両立**([ADR-0008](docs/adr/0008-review-scope-narrowed-to-unnoticeable-defects.md))。かつての「速さより質」は理解負債を根拠にしていたが、10万字級の設計文書が揃った時点でその根拠は満たされた。**いま大きいリスクは品質の崩壊ではなく、完遂しないことである。** 以下を規律とする([ADR-0006](docs/adr/0006-issue-driven-task-management.md) / [ADR-0008](docs/adr/0008-review-scope-narrowed-to-unnoticeable-defects.md))。

- **進行・スコープ・優先順位・未決事項・更新履歴を持つのは [GitHub の issue](https://github.com/stama72/visionary/issues) だけ。** 文書は「なぜ」と「今の仕様」だけを持つ。運用の正は [docs/process/04-issue-driven.md](docs/process/04-issue-driven.md)
- **指摘・提案は「今のマイルストーンの Exit Criteria を脅かすか」で仕分ける。** 脅かさないものは**直さずに issue へ落とす**。正しい指摘であることと、今直すべきことは別である
- **マイルストーンの定義と Exit Criteria は [企画書 §6](docs/02-project-proposal.md) が正。** 期日と進捗は GitHub Milestone が持つ。`docs/` にマイルストーン計画やリスク管理表を作らない
- **スコープが膨らんだら、上げるのは並行度ではなくスコープ削減で対応する**(ADR-0004 帰結)
- **開発者の判断を要するタスクの WIP は 1**(設計・ADR・ゲームデザイン)。仕様が凍結済みの実装タスクに限り 2 本まで。**パイプラインの無人フェーズは開発者を消費しないので、この数に入らない** — ただし**走っている間、本体の `visionary/` はパイプラインのものである。** うっかりの書き込みは `PreToolUse` フックが止める(**本体ツリーのみ・承認済みのセッションのみ**。何が止まらないかは [05-phase-sessions](docs/process/05-phase-sessions.md))。並行して進めるタスクは worktree を分ける
- **`docs/` に「未決定事項」を節見出しとして置かない**(番号付きも含む。凍る ADR は対象外)。`- [ ]` は完了条件のチェックリストとしてのみ使う

## タスクの進め方

**実装タスクは3フェーズに切る**([ADR-0009](docs/adr/0009-phase-scoped-sessions.md))。**フェーズ1 が仕様を凍らせたら、フェーズ2・3 はパイプラインが別プロセスで連鎖させる**([ADR-0010](docs/adr/0010-phase-pipeline-and-halt-conditions.md))。運用の正は [docs/process/05-phase-sessions.md](docs/process/05-phase-sessions.md)。

| フェーズ | 起動 | モデル | 成果物 |
| -------- | ---- | ------ | ------ |
| 1 設計 | 既定のセッション | `~/.claude/settings.json` の既定(2026-09-20 時点で fable) | タスク仕様の凍結 |
| 2 実装とレビュー | **フェーズ1 が背景起動した** `pwsh scripts/pipeline.ps1 -Issue <番号>` が `/impl` を開く | Opus(実装は Sonnet の implementer) | 緑のコードとコミット |
| 3 文書更新と PR | 同じパイプラインが続けて `/wrap` を開く | Sonnet | PR と切り出した issue |

**スクリプトを打つのはフェーズ1 のセッションである。** 仕様を凍結したら自分で背景起動して止まる。開発者に打たせると「終わったので次を打つ」待ちが残り、ADR-0010 が消しにきたものがそのまま残る。**待っている間にフェーズ1 が使うターンは 0 である**(W2-04 実測: 36分・0ターン)。

**パイプラインは停止則4つ**(`SPEC-OUTSIDE` / `IMPL-BLOCKED` / `REVIEW-EXHAUSTED` / `RED`)**に当たったときだけ止まり、デスクトップ通知を出す。** `SPEC-OUTSIDE`(フェーズ2 が GDD / TDD / ADR を触った)は機械が差分で見ている。**`DONE` 時に本体の作業ツリーが汚れていれば `RED` で止まる**(コミット漏れか、戻し忘れた変異)。止まったら `.pipeline/*.jsonl`(走行中も伸びる生ログ)を読む。**停止すると制御は起動したフェーズ1 に戻るので、新しいフェーズ1 を立てない。** 手で回すときは `/clear` してから `/impl <issue番号>` `/wrap <issue番号>` を開く。

**走行中かは `pwsh scripts/pipeline.ps1 -Status` で見る。停止の唯一の証拠はプロセスが消えることである** — 生ログの末尾も、オーケストレータの「止まりました」も証拠にならない([#109](https://github.com/stama72/visionary/issues/109))。

- **変異を当てて測るのは `mutator` だけである**([ADR-0013](docs/adr/0013-mutation-measurement-separated.md))。使い捨て worktree の中で当て、本体の作業ツリーには書かない。**変異を選ぶのは依頼側**(タスク仕様の表 / レビュアーの列挙)で、implementer とレビュアーは当てない。**「変異を当てて確認した」と書いてよいのは、`mutator` の報告を出所とする記述だけである**
- **`docs/tasks/` に仕様があるものが実装タスクであり、その実装は必ず implementer(Sonnet)が書く**([ADR-0004](docs/adr/0004-ai-driven-development-workflow.md) 論点1)。**レビュー指摘の修正も含む**(ADR-0009 論点3)。**設計セッションが自分で実装しない** — 書いた本人が実装すると、仕様に穴があっても自分の頭から埋めてしまい表面化しない
- **フェーズ2 のメインはコードを読み書きしない。** ここが崩れると、実装を Sonnet に逃がした節約がそのまま消える
- **各フェーズは成果物を出したら `PIPELINE: DONE` か `PIPELINE: HALT <コード>` を出して止まる。** 自分で次のフェーズを開かない。**どちらも出さずに終わると停止扱いになる**
- 引き継ぎメモ `docs/tasks/W*.handoff.md` は**フェーズをまたいで消える5件だけ**を持つ(却下した設計案 / implementer の件数 / 直さないと決めた指摘 / 巡ごとの件数 / `mutator` の件数)。既にコミット本文や doc コメントに残るものは書かない。フェーズ3 で PR 説明へ転記して削除する
- **設計・プロセス・文書のみの変更はこの3フェーズに乗らない。** `/design <issue番号>` で開く設計セッションが**束**(決めて書いてコミットするまで)ごとに回し、決定は issue のコメントに決めたその場で残す。引き継ぎメモは書かない。運用の正は [docs/process/06-design-sessions.md](docs/process/06-design-sessions.md)
- **モデルはセッションではなく仕事で決める。** `docs/` の設計(コンセプト・企画書・世界観・GDD・TDD・ADR・process)= fable、参照の付け替えなど機械的な波及 = Sonnet のサブエージェント。**実装タスクのフェーズ1 は settings の既定(2026-09-20 時点で fable)で走っており、opus に落とすかは実測してから決める**

## ドキュメント運用

- 個人開発だが、チーム開発の意思決定プロセスを模して文書を運用する
- 大きな技術的決定は **ADR** に「背景・選択肢・決定・理由」を記録する
- GDD/TDD は「育てる文書」。実装が仕様と乖離したら、コードだけでなく**文書側も直す**
- **上位文書の規則を書き換えた設計 issue は、閉じる条件に追随表を含める。** GDD/TDD なら「変えた規則 → 実装している既存コード → 引き取る impl issue」、docs 直下(コンセプト・企画書・世界観)なら「変えた規則 → 追随する GDD/TDD の節 → 引き取る design issue」。 実装タスクの仕様が既存コードを「変えない」と書くときは、従う現行の節を規則ごとに添える。書き換え前のコードは「一世代前の仕様の正確な実装」として残り、現行版だけを読むと正しく見える([docs/process/04-issue-driven.md](docs/process/04-issue-driven.md) / [02-task-spec](docs/process/02-task-spec.md) 規則8)
- 文書は**寿命**で四層。ADR(永続) / GDD・TDD・[process/](docs/process/)(育てる) / **issue(閉じるまで)** / タスク仕様と引き継ぎメモ [docs/tasks/](docs/tasks/)(PRとともに終わる)
- **ADRとGDDの境界**:
  - **ADRは「選択肢と理由」を記録する。** 何を検討し、なぜそれを選んだか。凍る
  - **GDDは「今の仕様」を持つ。** 現時点で何がどうなっているか。育つ
  - 同じ決定が両方に現れてよい。ADR-0003が「1年=120日を選んだ理由」を持ち、GDD03が「1年 = 4季 × 30日」という仕様を持つ、という形
  - **M0の中で決めたことは、仮仕様でよいのでGDDに書く。実装コメントやADRに仕様を溜めない**(暦のエポックと季節順が実装コメントにしか無い状態を招いたため)
- **ADRか他文書かの切り分けは3つの問いで判定する**(却下選択肢の価値 / 決定か予測か / なぜか今か)。判定基準の正は [docs/adr/README.md](docs/adr/README.md)「ADRか他文書かの切り分け」
- **開発プロセスの現行仕様は [docs/process/](docs/process/)** が持つ。運用のフィードバックはまずそこと `.claude/` に反映する。**プロセスについて新たにADRを起こすのは、過去のADRの決定を覆すときに限る**(新しい分野の決定であれば、却下した選択肢に価値があるかで判断する)
- 新しい規約を作るときは「**これは機械で守れるか**」を必ず問う。機械で守れない規約は、サブエージェントに任せられる範囲を狭める([ADR-0004](docs/adr/0004-ai-driven-development-workflow.md))
- タスクの状態・優先順位・依存関係は **issue が正**。`docs/tasks/` はタスク仕様のファイルとテンプレートだけを持つ
- **レビュアーエージェントは実装工程にだけ使う。** 設計・プロセス・文書のみの変更には使わない。そちらは開発者のレビューと [`/advise`](.claude/commands/advise.md)(設計アドバイザー)が担う
- **レビューの守備範囲は「気付けない × 影響大」に限る。** 表記揺れ・リンク切れ・軽微な不整合は**報告しない**。実装レビューは巡ごとに報告してよい象限を狭め、4巡目で打ち切る([docs/process/01-review.md](docs/process/01-review.md))
- **気付いたら直す。** 守備範囲の外を放置できるのはこの規約があるからである。リンク切れ・表記揺れ・古い件数は、見つけたそのとき、触っているブランチで直す。issue も別 PR も立てない
- **タスク仕様のテスト節は「落ちるべき条件」を書く。** 目標は通すことではなく、壊したときに落ちること。書き方の規則は [docs/process/02-task-spec.md](docs/process/02-task-spec.md)
- **訂正で「機械が守っている」と書く前に、実際に壊して落ちることを確かめる。迷ったら狭い側に倒す。** 広い誤りは「見なくてよい」と読ませるので、踏んでも気付けない。あわせて**却下理由の前提が、同じ変更で崩れていないか**を見る。規則は [docs/process/03-corrections.md](docs/process/03-corrections.md)
- 数値(閾値・係数)は初期値であり調整対象。固定すべきは構造と依存関係
