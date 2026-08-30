# W1-02: 系統別乱数ストリーム

| 項目     | 内容                                                     |
| -------- | -------------------------------------------------------- |
| 根拠     | [ADR-0002](../adr/0002-time-model-and-determinism.md) 論点2 / [TDD01 §3.1](../04-tdd/01-sim-core-and-m0.md) |
| ブランチ | `feat/random-streams`                                    |
| worktree | `visionary/`(本体)                                      |
| 状態     | レビュー対応済み                                         |

> **この文書は使い捨ての作業指示である。**実装完了時点で凍結し、以後の正はコードと TDD。

## 背景 — なぜ「系統別」だけでは足りないか

ADR-0002 は「機能フラグを切り替えても、**無効化された系統以外の乱数消費列が変わらない**」ことを約束している(共通乱数法)。これが崩れると、信用あり/なしの比較が「別世界同士の比較」になり M0 の実験が無意味になる。

ところが**系統ごとに1本の長い列を持つ実装ではこの約束を守れない**。例えば Trade 系統の相手選好が乱数を引くとき、信用ありモデルと信用なしモデルで引く回数が変われば、**それ以降の Trade の乱数がすべてずれる**。フラグを切った系統の内部が丸ごと別世界になる。

したがって鍵を **(マスターシード, 系統, tick, エンティティ)** から導出し、その組ごとに独立した短い列を開く。NPC #7 の 100日目の Trade で引く回数が変わっても、影響はその組の中だけに閉じる。他のNPC・他の日・他の系統はビット単位で同一に保たれる。

## 作るもの

名前空間 `Visionary.Sim.Randomness`。`System.Random` は使えない(禁止銘柄)ので自前で実装する。**これは制約ではなく利点でもある** — .NET の `Random` は実装がバージョン間で変わったことがあり、ランタイムに依存しない再現性を自前実装が保証する。

### `RandomStream`(enum)

```csharp
public enum RandomStream
{
    WorldGen = 1, Production = 2, Consumption = 3, Household = 4,
    NeedGeneration = 5, Trade = 6, Promise = 7, Trust = 8,
    UnfairPrice = 9, Rumor = 10, Dialogue = 11,
}
```

値は鍵の導出に直接使うため**仕様**。振り直さない。0 は使わない(既定値と衝突させない)。

### `SplitMix64`(internal static)

```csharp
internal static class SplitMix64
{
    internal const ulong Golden = 0x9E3779B97F4A7C15;
    internal static ulong Mix(ulong z);   // 標準の SplitMix64 finalizer
}
```

### `RandomSource`(readonly struct)

```csharp
public readonly struct RandomSource
{
    public const int NoEntity = -1;      // エンティティに紐づかない用途
    public RandomSource(long masterSeed);
    public RandomSequence Open(RandomStream stream, Tick tick, int entityId);
    public RandomSequence Open(RandomStream stream, Tick tick);   // entityId = NoEntity
}
```

鍵の導出(この順序と定数が仕様):

```
k = (ulong)masterSeed
k = Mix(k ^ (ulong)(long)stream)
k = Mix(k ^ (ulong)tick.Value)
k = Mix(k ^ (ulong)(long)entityId)
```

### `RandomSequence`(ref struct)

```csharp
public ref struct RandomSequence
{
    public ulong NextUInt64();
    public int NextInt(int minInclusive, int maxExclusive);
    public bool NextBool(int trueProbabilityPermille);
}
```

- **`ref struct` にする。** フィールドに保持したりラムダに捕捉したりできなくなり、「1つの (系統, tick, エンティティ) に属する使い捨ての列」という設計意図が型で守られる。コピーによるカウンタ複製という決定論バグの経路を狭める
- `NextInt` は**剰余バイアスを除去**する。`reject = (0UL - range) % range`(= 2^64 mod range)未満の値を捨てて引き直す
- `NextBool` は ‰(千分率)で受ける。範囲外は例外
- 浮動小数点を一切使わない

### Id の制約

`NoEntity = -1` を番兵に使うため、**シムの Id は非負の int** とする。TDD01 §3.2 に追記すること。

## 落ちるべき条件(テスト)

**この節が完了条件そのもの。**

> 当初は「緑にすべきテスト」という節名だった。通ることを目標に据えたために検出力の無いテストを生んだため、[書き方の規則](README.md#タスク仕様の書き方)とともに改めた。

| #  | テスト                                          | 検証内容                                                             |
| -- | ----------------------------------------------- | -------------------------------------------------------------------- |
| 1  | `SameScopeProducesSameSequence`                 | 同一の(seed, 系統, tick, entity)は同一列                             |
| 2  | `DifferentStreamsProduceDifferentSequences`     | 系統が違えば別の列                                                   |
| 3  | `DifferentEntitiesProduceDifferentSequences`    | エンティティが違えば別の列                                           |
| 4  | `DifferentTicksProduceDifferentSequences`       | tick が違えば別の列                                                  |
| 5  | `DifferentMasterSeedsProduceDifferentSequences` | シードが違えば別の列                                                 |
| 6  | **`ConsumptionInOneScopeDoesNotAffectAnother`** | **本タスクの核心。**ある組で余分に引いても他の組の値が1ビットも変わらない |
| 7  | `NextIntStaysWithinRange`                       | 多数回引いて範囲外が出ない                                           |
| 8  | `NextIntCoversWholeRange`                       | 十分な回数で全ての値が出る(縮退していない)                          |
| 9  | `NextIntWithSingleValueRangeReturnsThatValue`   | `NextInt(5, 6)` は常に5                                              |
| 10 | `NextIntThrowsWhenRangeIsEmpty`                 | `max <= min` は例外                                                  |
| 11 | `NextIntHandlesFullIntRange`                    | `int.MinValue`〜`int.MaxValue` で桁あふれしない                      |
| 12 | `NextBoolIsDeterministicAtBounds`               | 0‰ は常に false、1000‰ は常に true                                  |
| 13 | `NextBoolThrowsOnOutOfRangePermille`            | 範囲外の ‰ は例外                                                    |
| 14 | `ReferenceVectorsAreStable`                     | 既知の入力に対する出力を固定し、アルゴリズムの黙った変更を検出する   |

> テスト14は**参照ベクトルの固定**であり、§3.8 が禁じたゴールデン値とは性質が違う。状態ハッシュの値は係数調整で正当に変わるが、**乱数アルゴリズムは仕様であり変わってはならない**。ここは固定してよい場所である。

## 編集してよい文書

- `docs/04-tdd/01-sim-core-and-m0.md` §3.1(鍵の導出方針)と §3.2(Id は非負の int)
- `docs/tasks/W1-02-random-streams.md`(この文書の状態欄)

## このタスクで特に効く規約

- **系統をまたいで乱数を借用しない。** 本タスクはその借用を型で不可能にするのが目的であり、API 自体がそれを許してはならない
- 浮動小数点を使わない。確率は ‰ の int
- 機械で捕まる規約(`System.Random` 禁止など)はここに書かない

## 完了条件

- [X] 「落ちるべき条件」のテストが全て緑
- [X] `dotnet build Visionary.sln -c Release` が警告0
- [X] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [X] レビュアーエージェントの指摘が解消済み
