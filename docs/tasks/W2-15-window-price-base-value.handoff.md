# W2-15 引き継ぎメモ

**この文書は PR 説明へ転記して削除する。**

## 却下した設計案(フェーズ1)

- **`NoPurchaseReason.MarketTerm` を改名する** — 採らない。決定11 で分岐1 の材料は基礎値になったので、enum の名前は相場項が無い日の分岐も指すようになった。それでも改名しないのは、(1) [#120](https://github.com/stama72/visionary/issues/120) の追随表が「doc を直す」と決めていること、(2) [GDD02b §5.2](../03-gdd/02b-consumption-and-household.md) の3分岐の名前も「相場」のままであること、(3) `TradeSystem` の破産中フラグの判定と既存テストに波及し、[ADR-0008](../adr/0008-review-scope-narrowed-to-unnoticeable-defects.md) の「Exit Criteria を脅かすか」で仕分けると脅かさないこと、の3つによる。**失う保護**: enum の名前だけを読む人は「相場基準が無い日には立たない理由」と読む。**`PurchaseDecision.cs:13` の doc 1行がそれを止める唯一の手段になる** — だから仕様の追随表でその1行を明示的に要求している
- **窓口価格を `WorldDefinition.WindowPrice(itemId, season)` として公開する** — 採らない。呼び出し側が `BuyerDemand` 1つしかなく、[#172](https://github.com/stama72/visionary/issues/172) が移動平均へ差し替える場所そのものである。公開すると `ExternalSellPrice`(窓口が**売る**値。都市生産品では天井)と名前の近い読み口が `WorldDefinition` に3つ並び、取り違えの面が増える。**失う保護**: 単体テストで直接叩けない。テスト3 が `BuyerDemand.Build` 経由で**配線ごと**見ることで代える(規則7 と同じ向き)
- **検出器を1本にまとめる** — 採らない。#120 の全般アドバイザー指摘【3】のとおり、決定11 は購入量を1つも変えないので輸入額の走行テストでは M-2 が落ちない。**変異 M-2 の期待に「テスト1 は緑のまま」を書き込んで、この主張自体を実測させている**

## フェーズ1 が凍結前に直したもの

- PR **#175** をマージして GDD の決定10・11・12 を master へ入れた(`7377df3`)。**未マージのままだと implementer が読む GDD と仕様が食い違う**([#154](https://github.com/stama72/visionary/issues/154) と同じ形)
- GDD の誤り2種を直した(`efd8a3a`)。決定11 の取り残し4か所(うち GDD02b §3.2 は破産中フラグの経路)と、「零点の一致は恒等式」という過大な断定。**後者は #170 の本文にも同じ言い方がある** — 正確な形と M0 で到達しないことの実測は仕様の「前提」節にある

## フェーズ2 の再開時の状態(1回目の走行が `NO-SENTINEL` で止まった)

**1回目のパイプライン(`.pipeline/170-impl-20260922-173014.jsonl`)は `NO-SENTINEL` で停止した。** 枠の問題ではない(停止時点の 5時間枠の利用率 10%)。

| 事実 | 出所 |
| ---- | ---- |
| implementer(Sonnet)は起動し、**39k トークン出力・$2.81 ぶん働いた**。その途中で `[Request interrupted by user for tool use]` を受けて落ちた(`subagent_stats.failed: 1`。**開発者の中断ではない**) | 生ログの `result` 行 |
| **フェーズ2 のメインは「implementer は起動前に中断された。何も実装されていない。作業ツリーは clean」と報告した。これは偽である** — `git status` は 7 ファイルの変更を出す。メインは状態を確認せずに書いた | 生ログ最終メッセージ / `git status` |
| フェーズ2 は `PIPELINE: DONE` も `HALT` も出さず、**開発者へ質問して**ターンを閉じた | 同 |

**2回目の走行は、1回目の未コミットの実装の上から再開する**(本体ツリーは clean ではない)。フェーズ1 が機械で測った状態:

- `dotnet build Visionary.sln -c Release` — **成功・警告0**
- `dotnet test Visionary.sln -c Release` — **6件赤 / 432件緑 / 438件**。6件はすべてタスク仕様「既存テストの追随」の**訂正節**が裁定した族である
- 未コミット: `BuyerBudget.cs` / `BuyerDemand.cs` / `ErrandPlanner.cs` / `PurchaseDecision.cs` / `BuyerBudgetTests.cs` / `BuyerDemandTests.cs` / `TradeSystemTests.cs`

**捨てずに引き継ぐ。** 作り直しても同じ6件に当たる(根は仕様の穴であって実装の誤りではない)。

## implementer の件数(フェーズ2)

- 止まって報告した件数: N 件
- 決めて報告した件数: M 件

## 直さないと決めた指摘(フェーズ2)

| 巡 | 象限 | 指摘 | 直さない理由 |
| -- | ---- | ---- | ------------ |

## 巡ごとの件数(フェーズ2)

| 巡 | 守備範囲 | 象限I | 象限II | 疑い |
| -- | -------- | ----- | ------ | ---- |

## `mutator` の件数(フェーズ2)

- 当てた変異: N 件 / 期待と食い違った数: M 件
