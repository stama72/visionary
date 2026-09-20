# W2-11 引き継ぎメモ

**この文書は PR 説明へ転記して削除する。**

## 却下した設計案(フェーズ1)

- **帯の定数を品目ごとに置く(工具 2 倍・パン 12 倍 のように)** — 採らない。頭打ちを外す変異に対する判別力は 1 定数(床 × 20)で足りる(素の master はビールが 20 日目に 21.6 倍)。
  **代償: 帯の定数はパンの完売枝のラチェット(9.26 倍)で決まっており、他 4 品目には緩い。** 工具(1.17〜1.53 倍)が 10 倍になっても緑のまま通る。品目別の帯は #38 の後の実測([#120](https://github.com/stama72/visionary/issues/120) の残り)で置くかを決める。
- **`NecessityIsSettledBeforePreference` の定数(資金 100 / 100,000・3 日)を新しい実測(1,000 でも 1 日目にビールが約定する)に合わせて狭める** — 採らない。[#81](https://github.com/stama72/visionary/issues/81) の検出器の再校正は判別力の再実測を伴う契約変更で、W2-09 が「止まるべきだった」と数え直した型そのもの。
  **代償: remarks の実測値と定数の余裕が乖離する**(100,000 は必要以上に潤沢)。判別力そのものは変異 M-4 で測り直す — **M-4 が緑のままなら、据え置いた前提が崩れている。**
- **`ObservationBecomesUsableOnTheNextDayNotToday` の修正で、帳簿の行を直接置く代わりに実際の買い手を置く** — 採らない。買い手を置くと在庫が減って在庫比が動き、テストの主張(観測が翌日使える)と無関係な量が期待値に混ざる。
  **代償: フィクスチャが `LedgerEntry` の形(`Sale` / `ItemId` / `OccurredAt`)を直接知る。** `TradeSettlement` が帳簿の書き方を変えても、このテストの前提は動かず、`TryPreviousDaySettledPrice` が読む欄とずれても緑のまま通る。押さえているのは `PreviousDaySettledPriceIsQuantityWeighted` の 1 件だけ。
- **頭打ちの判定を `OfferPrice` の中で帳簿から求める(`Calculate` に `IReadOnlyList<LedgerEntry>` を渡す)** — 採らない。`OfferPrice` は純関数で `World` を受け取らない(W2-08 の切り出しの理由)。段1 が既に `hasSettled` を持っている。
  **代償: 「錨」と「頭打ち」が同じ母数であることは、段1 が同じ 1 つの bool を 2 か所に渡していることだけで保たれる。** テスト #5 が配線を踏むが、2 つ目の呼び出し側が現れたときに片方だけ別の判定を書いても機械は止めない。

## パイプラインの再開位置(フェーズ1 が 1 回目の停止後に書いた)

**1 回目のフェーズ2 は `NO-SENTINEL` で止まった**(2026-09-20 21:18 頃。生ログ `.pipeline/131-impl-20260920-204506.jsonl`)。原因はレビュー2巡目のレビュアーが **600 秒無進捗で watchdog に落とされた**こと(直前に 5 時間枠の利用率 82% の `rate_limit_event`)。実装と 1 巡目は済んでいる。**2 回目のフェーズ2 は implementer を最初から起動せず、下の状態から続けること。**

| 項目 | 状態 |
| ---- | ---- |
| 実装 | `f3e7243`(済) |
| レビュー1巡目の修正 | `3206b0a`(済。3 件すべて「直す」) |
| build / test / format | `3206b0a` 時点で 警告 0 / 365 件緑 / 差分なし(implementer 報告。フェーズ2 が再確認すること) |
| レビュー2巡目 | **未完**。網羅パス(doc コメント / remarks の実測・保証の主張を列挙)として起動したが途中で落ちた。**2巡目からやり直す** |
| `mutator`(M-1〜M-4) | **未実行** |
| 作業ツリー | 1 回目のレビュアーが `dotnet new console -o .probe` で本体に残した `.probe/` はフェーズ1 が削除済み。clean |

## implementer の件数(フェーズ2)

- 止まって報告した件数: **0 件**
- 決めて報告した件数: **3 件**(いずれもテストの配置場所。契約に触るものは無い — フェーズ1 の事後の仕分け)
  - `OfferPriceTests` の新規 4 件を `OfferPriceFloorIsExternalBuyPrice` と `BankruptSellerFixesCoefficientAtFivehundred` の間に置く
  - `UnsoldSellerIsCappedAtTheReferenceInThePipeline` を `BankruptSellerPostsTheHalvedFloorInThePipeline` の直後に置く
  - `OfferPricesStayWithinTheBandOverSixtyDays` を `KnowledgeSpreadsBeyondTheHomeVisionRadius` と `ObservationsDoNotGrowWithoutBound` の間に置く
- 仕様の実測値との食い違い 1 件(仕様の欠陥。止まる/決めるには数えない): 仕様「設計の前提」のシード 3 の最大は「工具 444(1.53 倍)」ではなく **パン 195(3.61 倍、11 日目)**。工具 444 は 15 日目以降パンが市場から消えた後の定常値で、フェーズ1 が最終日近傍の値を最大と取り違えた。帯の定数 20 は動かない。implementer が remarks に正しい値を書いた

## 直さないと決めた指摘(フェーズ2)

| 巡 | 象限 | 指摘 | 直さない理由 |
| -- | ---- | ---- | ------------ |

## 巡ごとの件数(フェーズ2)

| 巡 | 守備範囲 | 象限I | 象限II | 疑い |
| -- | -------- | ----- | ------ | ---- |
| 1 | 探索(全体) | **I-b 3**(帯の定数の根拠に赤側 21.6 倍が無い / 帯の検出器の summary と失敗メッセージが「§1.1 が帯を保つ」と断言 / `NecessityIsSettledBeforePreference` の「判別力は維持されている」が頭打ち前の実測であることが読めない)。I-a 0。すべて「直す」→ `3206b0a` | 0 | 0 |
| 2 | 網羅パス(触った doc コメント / remarks の実測・保証の主張)として起動 | **未完(watchdog で停止。2 回目のフェーズ2 がやり直す)** | — | — |

## `mutator` の件数(フェーズ2)

- 当てた変異: N 件 / 期待と食い違った数: M 件
