# W1-04: 決定論ハッシュと2プロセス検証

| 項目     | 内容                                                                      |
| -------- | ------------------------------------------------------------------------- |
| 根拠     | [TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md) / [TDD01 §5.4 W1](../04-tdd/01-sim-core-and-m0.md) / [ADR-0002](../adr/0002-time-model-and-determinism.md) 設計目標1 |
| ブランチ | `feat/determinism-hash`                                                   |
| worktree | `visionary/`(本体)                                                       |
| 状態     | 進行中                                                                    |

W1 の最後。完了条件は TDD01 §5.4 の「**同一シード2プロセス実行の状態ハッシュ一致がCIで回る**」。

契約([TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md))は確定済みである。アルゴリズム(`XxHash64`)・入力規約(int/long のみ、`string` を入れない)・含める/含めないの表・**ゴールデン値を固定しない**方針は、このタスクで再検討しない。**§3.8 と食い違う点を見つけたら、実装せずに設計セッションへ戻すこと。**

## このタスクの設計判断(設計セッションで決定済み)

TDD01 §3.8 に書かれていなかった2点を、着手前に決めた。

| 判断 | 決定 | 理由 |
| ---- | ---- | ---- |
| 回帰テストが「何を走らせたハッシュ」を比べるか | **`World` の全区画に書き込む合成システムを W1 限りで作り、それを N tick 回した後のハッシュ**を比べる | W1 には経済が無く、Clock と初期 `Npcs` だけのハッシュは乱数・列挙順の破れを何ひとつ検出しない。またハッシャの「全区画を Id 昇順で走る」経路が W1 中に一度も実行されないと、W2 で経済が区画を埋めた瞬間に未実行のコードへ依存することになる |
| 2プロセス検証の置き場所 | **`vsim hash` サブコマンド + CI ステップ**。xUnit からの子プロセス起動はしない | 実行形態が CI 上で明示的に読め、ローカルでも手で叩ける。テストがビルド成果物のパスを解決する必要が無い |

**合成システムは `Visionary.Sim.Runner` に置く。`Visionary.Sim` には入れない。** 捨てる前提のコードを本体に入れないことに加え、外部アセンブリから `ISimSystem` を実装する経路が実地で検証される(`SimContext` の doc コメントが主張している「アセンブリの外」に実際に立つ)。

## 作るもの

### 1. `System.IO.Hashing` パッケージ参照(`src/Visionary.Sim/Visionary.Sim.csproj`)

- `Visionary.Sim` に初めて入る実行時の外部依存。csproj 冒頭コメントの「現在の実行時依存: なし。予定: System.IO.Hashing」を**実際の状態に書き換える**(TDD01 §3.8 が「その方針を csproj のコメントに書く」と要求している)
- **版は `8.0.0` に固定する。** `PackageReference Include="System.IO.Hashing" Version="8.0.0"`。TFM(net8.0)と `global.json` の SDK 8.0.424 に帯を揃える。9.x / 10.x も net8.0 で動くが、band をまたぐ理由が無い
- 使う API は `XxHash64.Append(ReadOnlySpan<byte>)` と `GetCurrentHashAsUInt64()`。**8.0.0 / net8.0 で実在することを設計セッションで実測済み**(2026-09-04)。`BannedApiAnalyzers` は `PrivateAssets=all` なのでこの参照には影響しない

### 2. `src/Visionary.Sim/Determinism/StateHasher.cs`

```csharp
namespace Visionary.Sim.Determinism;

public static class StateHasher
{
    /// <summary>World の状態ハッシュ(TDD01 §3.8)。</summary>
    public static ulong Compute(World world);
}
```

**バイト列化の規約。** TDD01 §3.8 は「実装判断でよいが、選んだ方法を1か所にコメントで固定する」としている。**その1か所をこのファイルの冒頭とする**:

- `int` は4バイト、`long` は8バイト、いずれも**リトルエンディアン固定**。`BinaryPrimitives.Write*LittleEndian` を使い、`BitConverter` は使わない(`BitConverter` は実行環境のエンディアンに従う)
- `enum` は基になる `int` として書く
- `Tick` は `Tick.Value`(`long`)として書く
- **各区画の先頭に「区画タグ(int)」と「要素数(int)」を書く。** これが無いと、隣接する同型の可変長区画の境界が曖昧になり、片方が空でもう片方に要素がある2つの状態が同じバイト列になりうる。区画タグは `StateHasher` に入れ子の `private enum` として置き、**固定値を明示する**:

  ```csharp
  // 値は仕様である。振り直してはならない(RandomStream と同じ理由)。
  // 0 を使わないのは、既定値の Section が有効な区画に見えるのを避けるため。
  // W2 で区画を追加するときは、既存の値を動かさずに末尾へ足す。
  private enum Section
  {
      Clock       = 1,
      Npcs        = 2,
      Market      = 3,
      TrustLedger = 4,
      Needs       = 5,
      Promises    = 6,
      Knowledge   = 7,
      Ledgers     = 8,
  }
  ```

  連番をその場で振る書き方(`tag++`)は採らない。W2 で区画を途中に挿入すると以降のタグが全部ずれ、「タグは安定している」という読み手の期待を裏切る。ゴールデン値を固定しない方針(§3.8)なのでずれても壊れはしないが、壊れないことと誤解を招かないことは別である
  - **残る穴**: W1 の `World` には同じ要素型の可変長区画が隣接していないため、**この規約を落とせるテストは書けない**(下の「書けないテスト」を参照)。W2 で Actor 別の `Knowledge`(TDD01 §3.6 の仮決め表)が入り、区画が入れ子の可変長になった時点でテストを追加すること
- **順序非依存の畳み込み(XOR・加算)を使ってはならない**(§3.8)。単一の `XxHash64` に前から順に `Append` する

**走査順**:

| 区画 | 走査順 | 根拠 |
| ---- | ------ | ---- |
| `Now` | — | 最初に書く。含めないと「同じ状態に違う時刻で到達した」を検出できない(§3.8) |
| `Npcs` | 配列の添字順(= Id 昇順) | ADR-0002 の列挙順規約。`NpcState` は `Id` / `LiquidFunds` / `Inventory`(要素数 + 各要素)を書く |
| `Market` | `SortedDictionary` の列挙順(`MarketKey.CompareTo` = ItemId → SellerId) | キー順が決定的なのでそのまま列挙してよい |
| `TrustLedger` | 同上(`TrustKey.CompareTo` = From → To) | 同上 |
| `Needs` / `Promises` / `Knowledge` / `Ledgers` | **`List` の格納順そのまま。ソートも正規化もしない** | 列挙順の破れ自体が検出したいバグである(§3.8)。ここで正規化すると、W2 の Rumor(§3.3-9)の順序破れが検出範囲の外に出る |
| `EventLog` | **含めない** | §3.8 の除外表。意思決定に関与せず、追記専用で巨大 |

### 3. `src/Visionary.Sim.Runner/Determinism/SyntheticLoadSystem.cs`

**W1 限りの合成負荷。W2 で TDD01 §3.3 の本物のシステム群に差し替え、このファイルは削除する。** その旨をファイル冒頭のコメントに書く。

```csharp
internal sealed class SyntheticLoadSystem : ISimSystem
{
    public RandomStream Stream => RandomStream.WorldGen;
    public Cadence Cadence => Cadence.EveryTick();
    public void Step(World world, SimContext context);
}
```

`Step` は `world.Npcs` を添字昇順に走り、各 NPC につき `context.OpenRandom(npc.Id)` を**1回だけ**開いて、以下を順に行う。数値はすべて合成負荷の都合で選んだ値であり、経済的な意味は無い:

1. `npc.LiquidFunds += rng.NextInt(-50, 51);` — 単位: 貨幣(int)
2. `world.Market[new MarketKey(itemId: rng.NextInt(0, 5), sellerId: npc.Id)] = rng.NextInt(1, 101);` — 品目5種は TDD01 §3.6
3. `world.TrustLedger[new TrustKey(npc.Id, rng.NextInt(0, world.Npcs.Length))] = new TrustScore { Value = rng.NextInt(0, 101), LastMet = world.Now };`
4. `rng.NextBool(100)`(100‰ = 10%)が真なら、`world.Needs` と `world.Promises` に1件ずつ追加する。**全フィールドを以下で埋める**:

   ```csharp
   world.Needs.Add(new Need
   {
       TypeCode     = rng.NextInt(0, 6),                 // W2 で enum 化(TDD01 §3.6 仮決め表)
       TargetNpcId  = rng.NextInt(0, world.Npcs.Length),
       ItemId       = rng.NextInt(0, 5),                 // 品目5種(TDD01 §3.6)
       Quantity     = rng.NextInt(1, 11),                // 単位: 個
       Deadline     = world.Now.AddDays(rng.NextInt(1, 8)),
       Urgency      = rng.NextInt(0, 101),               // 単位: 0〜100 の素の整数(‰ ではない)
       ReasonCode   = rng.NextInt(0, 4),
   });

   world.Promises.Add(new Promise
   {
       NeedIndex = world.Needs.Count - 1,                // W2 で Id 参照へ(TDD01 §3.6 仮決め表)
       T0        = world.Now,
       T1        = world.Now.AddDays(rng.NextInt(1, 8)),
       B         = rng.NextInt(1, 1001),                 // 単位: 貨幣(GDD01 §2.8 の B)
       State     = (PromiseState)rng.NextInt(0, 4),
   });
   ```

5. `rng.NextBool(200)`(200‰ = 20%)が真なら、`world.Knowledge` と `world.Ledgers` に1件ずつ追加する:

   ```csharp
   world.Knowledge.Add(new PriceObservation
   {
       ItemId     = rng.NextInt(0, 5),
       LocationId = rng.NextInt(0, 9),                   // 9区画(TDD01 §3.2)
       Price      = rng.NextInt(1, 101),                 // 単位: 貨幣
       ObservedAt = world.Now,
       Source     = (ObservationSource)rng.NextInt(0, 2),
   });

   world.Ledgers.Add(new LedgerEntry
   {
       CounterpartyId = rng.NextInt(0, world.Npcs.Length),
       ItemId         = rng.NextInt(0, 5),
       Quantity       = rng.NextInt(1, 11),              // 単位: 個
       UnitPrice      = rng.NextInt(1, 101),             // 単位: 貨幣
       OccurredAt     = world.Now,
       Terms          = (LedgerTerms)rng.NextInt(0, 2),
       CreditDueAt    = world.Now.AddDays(rng.NextInt(1, 31)),
   });
   ```

6. `world.EventLog` に `DomainEvent` を1件追加する — **ハッシュに入らない区画を実行時にも踏むため**に必ず追加する:

   ```csharp
   world.EventLog.Add(new DomainEvent
   {
       KindCode  = rng.NextInt(0, 6),                    // W2 で設計(TDD01 §3.6 仮決め表)
       At        = world.Now,
       SubjectId = npc.Id,
       RelatedId = rng.NextInt(0, world.Npcs.Length),
       Payload   = rng.NextInt(0, 1000),
   });
   ```

**`rng` の消費回数は分岐によって変わる**(手順4・5 が確率で発火するため)。これは正しい。1つの `RandomSequence` から順に引いている限り決定的であり、「分岐の有無で消費回数を揃える」ような細工はしない。

### 4. `src/Visionary.Sim.Runner/Determinism/SyntheticDecaySystem.cs`

同じく W1 限り。**`Cadence.Daily(hour: 0)` かつ別系統にすることが目的**である。これにより、1tick に2システムが走り、**両システムが同じ `entityId` で別系統の乱数を開く**経路(W1-03 で二重オープン検出のキーを誤ったときに壊れた、まさにその経路)が回帰テストの射程に入る。

```csharp
internal sealed class SyntheticDecaySystem : ISimSystem
{
    public RandomStream Stream => RandomStream.Trust;
    public Cadence Cadence => Cadence.Daily(hour: 0);
    public void Step(World world, SimContext context);
}
```

`Step` は `world.Npcs` を添字昇順に走り、各 NPC につき `context.OpenRandom(npc.Id)` を1回開いて:

1. `TrustLedger` のうち `From == npc.Id` のエントリの `Value` を `rng.NextInt(1, 4)` だけ減らす(下限0)。**`SortedDictionary` を列挙しながら変更しないこと** — キーを先に配列へ取り出してから書き戻す
2. `world.Knowledge.Count > 500` なら先頭から `Count - 500` 件を `RemoveRange` で捨てる。**保持本数の上限**(GDD01 §4.1 の保持ポリシーの合成版)であり、状態が単調増加でなくなることで `List` の順序変化がハッシュに効く

### 5. `vsim hash` サブコマンド(`src/Visionary.Sim.Runner/Program.cs`)

```
vsim hash --seed <long> --ticks <int> [--npcs <int>]
```

- `--seed` / `--ticks` は必須。`--npcs` の既定は **40**(TDD01 §3.6「NPC 30〜50体」の中央)
- `--ticks` は 1 以上(`SimScheduler.Advance` が 0 以下を拒否する)。`--npcs` は **2 以上**(`SyntheticLoadSystem` が `rng.NextInt(0, Npcs.Length)` で相手 NPC を選ぶため、1体だと自分しか選べず `TrustLedger` が退化する)
- 数値の解釈は `long.Parse(s, CultureInfo.InvariantCulture)`。`InvariantGlobalization` が有効なので実質不変だが明示する
- 不正な引数・未知のオプションは `PrintUsage()` して終了コード **64**(既存の `ExitUsage`)
- 実行手順: `new World(npcs)` → `new RandomSource(seed)` → `new SimScheduler([load, decay], random)` → `Advance(world, ticks)` → `StateHasher.Compute(world)`
- **stdout には16桁の大文字hex(`X16`)を1行だけ書く。** 診断情報を出す場合は stderr へ。CI がシェルで比較するため、stdout に他の文字を混ぜない
- 使い方表示の「未実装」一覧から `hash` を外し、「実装済み」へ移す

### 6. CI ステップ(`.github/workflows/ci.yml`、`sim` ジョブの Test の後)

```yaml
- name: Determinism (cross-process state hash)
  run: |
    ARGS="hash --seed 20260904 --ticks 720 --npcs 40"
    RUN="dotnet run --project src/Visionary.Sim.Runner -c Release --no-build --"
    A=$($RUN $ARGS)
    B=$($RUN $ARGS)
    C=$($RUN hash --seed 20260905 --ticks 720 --npcs 40)
    echo "same-seed run1=$A run2=$B / other-seed=$C"
    if [ "$A" != "$B" ]; then
      echo "::error::同一シードの状態ハッシュが別プロセス間で一致しない (TDD01 §3.8)"; exit 1
    fi
    if [ "$A" = "$C" ]; then
      echo "::error::シードを変えても状態ハッシュが変わらない。ハッシュが状態を見ていない (TDD01 §3.8)"; exit 1
    fi
```

**3回目の実行(別シード)が要点である。** 一致検証だけでは、`Compute` が定数を返す実装で常に緑になり、回帰テストとして空虚になる。720 tick = 30日(TDD01 §5.4 の36,000日実行はここではやらない。CI 時間を使わずに全区画へ値が入れば足りる)。

## 落ちるべき条件(テスト)

`tests/Visionary.Sim.Tests/Determinism/StateHasherTests.cs`。合成システムは Runner にあるためテストからは触らない。各テストは `World` を直接組み立てる。

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心 |
| - | ------ | -------- | -------------------- | ---- |
| 1 | `HashChangesWhenClockAdvances` | `Now` が入力に含まれる | `Now` を書き忘れる。「同じ状態に違う時刻で到達した」が検出できなくなる(§3.8 が `Clock` を含める理由そのもの) | |
| 2 | `HashChangesWhenTwoNpcsSwapTheirFunds` | `Npcs` の走査が順序依存 | 順序非依存の畳み込み(XOR・加算)にする。NPC#3 と NPC#5 の `LiquidFunds` を入れ替えても同じ値になり、Id 昇順規約の破れが検出できなくなる | **核心** |
| 3 | `HashChangesWhenKnowledgeListIsPermuted` | `List` 区画の格納順が状態の一部 | 走査前に `OrderBy` などで正規化する。W2 の Rumor(§3.3-9)の列挙順の破れが検出範囲の外に出る | **核心** |
| 4 | `HashIgnoresEventLog` | `EventLog` が入力から除外されている | `EventLog` を含める。§3.8 の除外表に反し、追記専用で巨大な列を毎回走ることになる | |
| 5 | `HashChangesWhenMarketPriceChanges` | `Market` 区画が実際に入力に入っている | `Market` の書き込みを丸ごと落とす。W2 の価格形成の破れを検出できなくなる | |
| 6 | `HashChangesWhenTrustScoreChanges` | `TrustLedger` 区画が入力に入っている | 同上(`TrustLedger`) | |
| 7 | `HashChangesWhenNeedIsAdded` | `Needs` 区画が入力に入っている | 同上(`Needs`) | |
| 8 | `HashChangesWhenPromiseStateChanges` | `Promises` 区画が入力に入っている。かつ `enum` が基の int として書かれている | `Promise.State` を書き忘れる。`Active` と `Completed` が同じハッシュになる | |
| 9 | `HashChangesWhenLedgerEntryIsAdded` | `Ledgers` 区画が入力に入っている | 同上(`Ledgers`) | |
| 10 | `HashChangesWhenTrustScoreLastMetChanges` | `Tick` フィールドが `long` として書かれている | `TrustScore.LastMet` を書き忘れる。`Tick` を持つ他の型でも同じ落とし方をしていることの代表 | |
| 11 | `HashIsStableWhenComputedTwiceOnTheSameWorld` | `Compute` が副作用を持たない | バッファや `XxHash64` インスタンスを静的に使い回して状態を残す。2回目以降が違う値になる | |
| 12 | `HashIsNotZeroForAPopulatedWorld` | `Compute` が定数や既定値を返していない | `Append` を一切呼ばずに `GetCurrentHashAsUInt64()` を返す。テスト11 が常に緑になり空虚化する | **核心** |

**書けないテストとその理由**([docs/process/02-task-spec.md](../process/02-task-spec.md) 規則4「保証には残る穴も書く」):

- **区画タグ・要素数の前置**を落とせるテストは W1 では書けない。前置が無くても曖昧になるのは「同じ要素型の可変長区画が隣接する」ときだけで、現在の `World` にその形が無いため、規約を破っても衝突する `World` の組を構成できない。W2 で Actor 別 `Knowledge` が入った時点でテストを追加すること
- **プロセス間の不一致**はテストでは書かない(§3.8「同一プロセス内の2回では回帰テストとして無意味」)。CI ステップが唯一の検証手段である

### 変異テスト

「核心」印のテスト2・3・12 について、実際に変異を当てて落ちることを確認し、**当てた変異と結果をコミットメッセージに残す**([docs/process/02-task-spec.md](../process/02-task-spec.md))。当てる変異の例:

- テスト2 → `Npcs` の走査を単純加算の畳み込みに変える
- テスト3 → `Knowledge` の走査前に `OrderBy(o => o.ItemId)` を挟む
- テスト12 → `Compute` の本体を `return 0;` にする

## 編集してよい文書

- `src/Visionary.Sim/BannedSymbols.txt` の末尾コメント — 「機械では守れていない」一覧の「状態ハッシュの回帰テスト(**未実装**)」を実態に合わせる。この一覧は ADR-0004 の「委譲できる範囲は機械的ガードの範囲に依存する」判断の入力なので、実態より狭くも広くも書かない
- `src/Visionary.Sim/Visionary.Sim.csproj` のコメント — 「現在の実行時依存: なし。予定: System.IO.Hashing」を実態に合わせる
- `.github/workflows/ci.yml`
- **`docs/04-tdd/01-sim-core-and-m0.md` §4.1 の CLI ブロックに `hash` を1行足す。この1行だけ。** 足す内容は以下で確定しており、文面を変えない:

  ```
  vsim hash --seed <n> --ticks <n> [--npcs <n>]   # 状態ハッシュを標準出力に1行(§3.8 の2プロセス検証用)
  ```

  §4.1 は M0 のCLI表面を持つ育てる文書である。ここに載せずにコマンドを足すと、仕様がコードにしか存在しない状態になる(CLAUDE.md「実装コメントやADRに仕様を溜めない」)

**TDD01 の §4.1 以外は触らない。とくに §3.8 は触らない。** 契約は確定済みであり、乖離を見つけたら実装せず設計セッションへ戻す。

## このタスクで特に効く規約

`BannedSymbols.txt` と `DeterminismConventionTests` が捕まえないもののうち、このタスクで踏みやすいものだけ:

- **`BitConverter` は禁止対象に入っていないが使ってはならない。** 実行環境のエンディアンに従うため、`Visionary.Sim` を別アーキテクチャで走らせた瞬間に値が変わる。`BinaryPrimitives.Write*LittleEndian` を使う
- **`SortedDictionary` を列挙しながら変更しない**(`SyntheticDecaySystem`)。キーを先に配列へ取り出す
- 合成システムの数値定数(確率の‰、金額の範囲)には**単位のコメントを付ける**(ADR-0002)

## 完了条件

- [ ] 「落ちるべき条件」のテスト12件が全て緑
- [ ] **テスト2・3・12 に変異を当てて落ちることを確認し、当てた変異と結果をコミットメッセージに残した**
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] `vsim hash --seed 1 --ticks 720` を**手元で2回**実行して一致することを確認した(CI と同じ検証をローカルで踏む)
- [ ] CI の `Determinism (cross-process state hash)` ステップが緑
- [ ] `BannedSymbols.txt` と `Visionary.Sim.csproj` のコメントが実態に一致している
- [ ] レビュアーエージェントの指摘が解消済み
