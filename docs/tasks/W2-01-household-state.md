# W2-01: World の区分を世帯単位へ移す

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#32](https://github.com/stama72/visionary/issues/32)                |
| 根拠     | [TDD01 §3.2・§3.8](../04-tdd/01-sim-core-and-m0.md) / [GDD02 §2.2・§2.4・§4.3・§6.2・§6.2.2](../03-gdd/02-economy.md) / [GDD08 §2.1・§2.2・§4.1](../03-gdd/08-household-and-decision.md) |
| ブランチ | `feat/32-household-state`                                            |
| worktree | `visionary/`(本体)                                                  |

> **この文書は使い捨ての作業指示である。** 実装完了時点で凍結し、以後の正はコードと TDD。

## スコープ

**器を作るタスクであって、経済を作るタスクではない。** [TDD01 §3.2「経済主体は世帯である」](../04-tdd/01-sim-core-and-m0.md)の区分変更を実行し、決定論ハッシュ(§3.8)を追随させる。

**含まない:**

- 生産・消費・価格・取引のロジック(#34 以降)
- 品目表・レシピ・職業・初期配置の**値**(#33)。本タスクは値を入れる**枠**だけを作る
- `Need` / 理由コードの enum 化(#40)。`int` のプレースホルダのまま動かさない
- ゴールデンハッシュ値の固定([TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md) の判断どおり、比較は同一シード2プロセス実行)

## 作るもの

名前空間は `Visionary.Sim`(`World` 系)。

### `HouseholdState`(sealed class、`Visionary.Sim`)

```csharp
public sealed class HouseholdState
{
    public int Id { get; }
    public int DistrictId { get; }            // 区画 Id 0〜8(GDD02 §4.3)。不変
    public int OccupationId { get; set; }     // 職業 Id。値の定義は #33
    public int HeadNpcId { get; }             // 世帯主(GDD08 §2.1)
    public int[] MemberNpcIds { get; }        // 構成員。NpcId 昇順。HeadNpcId を含む
    public int LiquidFunds { get; set; }      // 単位: 貨幣(GDD02 §6.2)
    public int[] HouseholdInventory { get; }  // 添字 = itemId。消費財
    public int[] WorkshopInventory { get; }   // 添字 = itemId。生産の入出力
    public int IsBankrupt { get; set; }       // 0 / 1。破産中フラグ(GDD02 §6.2.2)

    public HouseholdState(int id, int districtId, int headNpcId, int[] memberNpcIds, int itemCount);
}
```

- **`DistrictId` と `HeadNpcId` と `MemberNpcIds` は不変**。区画が不変なのは [GDD02 §4.3「世帯は区画を移らない」](../03-gdd/02-economy.md)、構成員が M0 で不変なのは世代交代([GDD02 §11](../03-gdd/02-economy.md))が M0 スコープ外だから。`OccupationId` が `set` を持つのは [GDD02 §6.3](../03-gdd/02-economy.md) ④の職業付け替えがあるため
- **在庫を2本に分けるのは構造的な要請である**([TDD01 §3.2](../04-tdd/01-sim-core-and-m0.md))。薪(itemId 5)は必需の消費財でもパン・ビールの生産入力でもあり([GDD02 §2.2](../03-gdd/02-economy.md))、1本では目標在庫が一意に決まらない
- **`IsBankrupt` を `bool` にしない。** 状態はすべて int/long([TDD01 §3.2](../04-tdd/01-sim-core-and-m0.md))、ハッシュ入力は int/long のみ(§3.8)。`bool` の例外を作らない
- **`OccupationId` を `int` のプレースホルダにするのは決定であって保留ではない。** 職業 Id は [GDD02 §2.4](../03-gdd/02-economy.md) が 0〜4 と採番済みで、enum 化は #33 の作業。`Need.TypeCode` と同じ扱いにする

**コンストラクタの検証(すべて `ArgumentOutOfRangeException` / `ArgumentException`):**

- `id` < 0、`districtId` < 0、`headNpcId` < 0、`itemCount` < 0 を拒否
- `memberNpcIds` が **NpcId 昇順でない**、または **`headNpcId` を含まない** を拒否
- `memberNpcIds` に重複があるものを拒否

> **昇順を検証するのは、構成員の列挙が [ADR-0002](../adr/0002-time-model-and-determinism.md) の列挙順規約の対象だからである。** 世帯内の処理順(例: [GDD02 §6.2.1](../03-gdd/02-economy.md) の購入の決済順)が構成員の並びに触れる以上、並びが入力次第で変わると結果が変わる。
>
> **渡された配列は複製して持つ。** これで「呼び出し側が検証を通した後に、渡した配列を並べ替える」経路は塞がる。
>
> **ただし、`MemberNpcIds` を受け取った側が並べ替える経路は塞がらない。** `int[]` をそのまま公開しているためである。`IReadOnlyList<int>` として宣言しても同じで、配列がその interface を実装している以上 `int[]` へ戻せる。別インスタンスの `ReadOnlyCollection<int>` で包めば防げるが、**そこまでの手当てはせず穴として記録するにとどめる**。

### `NpcState` の変更(`Visionary.Sim`)

```csharp
public sealed class NpcState
{
    public int Id { get; }
    public int HouseholdId { get; set; }   // 所属世帯(TDD01 §3.2)
    public NpcRank Rank { get; set; }      // 階層(GDD08 §2.1・§3.3)
    public int SkillPermille { get; set; } // 熟練度‰ 0〜1000(GDD08 §4.1)

    public NpcState(int id);
}

public enum NpcRank
{
    Master = 0,      // 親方(世帯主)
    Journeyman = 1,  // 職人。自分の家計を持つ = 別世帯(GDD08 §2.1)
    Apprentice = 2,  // 徒弟。住み込み
}
```

- **`LiquidFunds` と `Inventory` を廃止する。** これが issue #32 の閉じる条件そのもの([TDD01 §3.2](../04-tdd/01-sim-core-and-m0.md) の移動表)
- **`SkillPermille` の名前に単位を入れる。** 千分率であることをフィールド名で持たせる(CLAUDE.md「係数の定数定義には単位のコメントを必須とする」の趣旨)。範囲 0〜1000 は setter で検証する
- **「性格」と「役割」は持たせない**(下記「編集してよい文書」の TDD01 §3.2 の訂正とセット)

> **「性格」を持たせないのは、[TDD01 §3.2](../04-tdd/01-sim-core-and-m0.md) の Npcs 行と §3.8 のハッシュ対象表が食い違っているからである。** §3.2 は性格を挙げ、§3.8 は「世帯Id・階層・役割・熟練度」で性格を挙げていない。§3.8 の判断基準は「シムの意思決定に関与するか」であり、**M0 に性格を読む処理は無い**。使わない状態を器に残すと「ハッシュに入れ忘れた穴」を先に作ることになるので、**§3.2 側を直して落とす。**
>
> **「役割」を持たせないのは、M0 では階層から一意に決まるからである。** [GDD08 §3.3](../03-gdd/08-household-and-decision.md) が 徒弟→家事・店番 / 職人→仕入れ交渉 / 親方→関係づくり と階層ごとに担当を定めている。独立した状態として持つと、階層と食い違った値が持てるようになり、ハッシュには乗るが誰も読まない状態が増える。**M0 の人数は親方1・徒弟1**([GDD08 §2.1](../03-gdd/08-household-and-decision.md))なので、写像で足りる。

### `World` の変更(`Visionary.Sim`)

```csharp
public World(int npcCount, int householdCount, int itemCount);

public NpcState[] Npcs { get; }                    // 添字 = NpcId(既存)
public HouseholdState[] Households { get; }        // 添字 = 世帯Id
public List<PriceObservation>[] Knowledge { get; } // 添字 = NpcId(所有者は個人)
public List<LedgerEntry>[] Ledgers { get; }        // 添字 = 世帯Id(所有者は世帯)
```

- `Households[i]` は `new HouseholdState(id: i, districtId: 0, headNpcId: 0, memberNpcIds: new[] { 0 }, itemCount)` で初期化する。**値は #33 が入れる**
- `Knowledge` / `Ledgers` の各要素は空の `List<>` で初期化する。`null` を入れない
- `npcCount` / `householdCount` / `itemCount` はいずれも負を拒否

> **`Knowledge` と `Ledgers` を所有者別の配列にするのは、所有者の取り違えを構造で防ぐためである。** [TDD01 §3.2](../04-tdd/01-sim-core-and-m0.md) は「**知識は個人・帳簿は世帯**である。同じ『所有者Id を持たせる』でも指すものが違うので、型を共通化してはならない」と書いている。添字の意味が違う2本の配列にすれば、片方をもう片方の添字で引く誤りが**型では防げないまま**でも、走査の形(`for (int npcId = ...)` / `for (int householdId = ...)`)が読んで分かるようになる。
>
> **防げないもの:** `Knowledge[householdId]` と書く誤りは、どちらも `List<>[]` なのでコンパイルが通る。**型で防ぐことはできない。** 検出は「所有者を取り違えるとハッシュが変わる」テスト(下表 #7・#8)に頼る。
>
> **平坦な `List<>` + 所有者Id 欄を採らなかった理由:** 走査側が毎回「所有者Id 昇順 → 格納順」を自前で守る必要があり、破れても緑になる。配列なら添字順が Id 昇順そのものなので、[ADR-0002](../adr/0002-time-model-and-determinism.md) の列挙順規約を構造で満たす。

### `PriceObservation` の変更(`Visionary.Sim`)

```csharp
public readonly record struct PriceObservation
{
    public int ItemId { get; init; }
    public int LocationId { get; init; }
    public int Price { get; init; }
    public int SellerId { get; init; }   // 追加。売り手の「世帯」Id
    public Tick ObservedAt { get; init; }
    public ObservationSource Source { get; init; }
}
```

- **`SellerId` は世帯 Id である**([TDD01 §3.2](../04-tdd/01-sim-core-and-m0.md)「売り手は世帯である」)。NpcId ではない。doc コメントに明記する
- **`MarketKey.SellerId` の doc コメントも「売り手世帯」に直す。** 型は変えない(既に int)

**都市外市場の窓口 Id:**

```csharp
// HouseholdState
/// <summary>
/// 都市外市場の窓口を指す予約済みの売り手 Id(TDD01 §3.2 / GDD02 §10.2)。
/// </summary>
public const int ExternalMarketSellerId = int.MaxValue;
```

- **`int.MaxValue` を選ぶのは、世帯数に依存しないからである。** [TDD01 §3.2](../04-tdd/01-sim-core-and-m0.md) は「既存の世帯 Id より大きい非負 int を1つ窓口に予約する」「売り手 Id 昇順の走査では合成した候補が最後に来る」と要求している。`householdCount` のような世帯数依存の値だと、世帯数を変えた実験で既存の世帯 Id と衝突しうる
- **都市外市場は `World` の区分を持たず、`Market` にも載らない**([TDD01 §3.2](../04-tdd/01-sim-core-and-m0.md))。この定数は `PriceObservation.SellerId` と `LedgerEntry.CounterpartyId` の値としてのみ使う。**本タスクでは定数を置くだけで、使う側は #38**

### `Need` の変更(`Visionary.Sim`)

- `TargetNpcId` を **`TargetHouseholdId`** に改名する。名前ごと変える([TDD01 §3.2](../04-tdd/01-sim-core-and-m0.md)、不足の主体は世帯)
- `TypeCode` / `ReasonCode` は `int` のまま動かさない(#40)

### `StateHasher` の変更(`Visionary.Sim.Determinism`)

**区分タグ。既存の値を1つも動かさず、末尾に足す**([TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md)):

```csharp
Households = 9,
```

**書き込む順序と内容**(既存の節順は変えない。`Households` は `Ledgers` の**後ろ**に足す):

| 区分 | 要素数 | 1要素あたりに書くもの(この順) |
| ---- | ------ | ------------------------------ |
| `Npcs` | `Npcs.Length` | `Id`, `HouseholdId`, `(int)Rank`, `SkillPermille` |
| `Knowledge` | `Knowledge.Length`(**所有者数**) | `ownerNpcId`, 件数, 各観測(`ItemId`, `LocationId`, `Price`, **`SellerId`**, `ObservedAt`, `(int)Source`) |
| `Ledgers` | `Ledgers.Length`(**所有者数**) | `ownerHouseholdId`, 件数, 各行(既存の7項目をこの順のまま) |
| `Households` | `Households.Length` | `Id`, `DistrictId`, `OccupationId`, `HeadNpcId`, 構成員数, 各 `MemberNpcId`, `LiquidFunds`, 世帯在庫の長さ, 各数量, 工房在庫の長さ, 各数量, `IsBankrupt` |

- **所有者別区分の要素数は「所有者数」であって「総件数」ではない。** 所有者ごとに `ownerId` と件数を前置する。この形式を `StateHasher` の doc コメント(バイト列化の規約を固定している1か所)に追記する
- **`ownerId` は添字と一致するが、それでも書く。** `Npcs` が `npc.Id` を書いている既存の書き方に揃える
- **在庫は2本とも長さを前置してから中身を書く。** 既存の `npc.Inventory` の書き方(長さ → 各要素)を踏襲する
- `EventLog` は引き続き含めない

### `Visionary.Sim.Runner` の追随

- `World` のコンストラクタ変更に合わせ、`--households`(既定 10)と `--items`(既定 9)を足す。**既定値の出典は [GDD02 §2.4](../03-gdd/02-economy.md)(5職業 × 2世帯 = 10世帯)と [GDD02 §2.2](../03-gdd/02-economy.md)(M0 は9品目)**
- `SyntheticLoadSystem` / `SyntheticDecaySystem` を追随させる:
  - `npc.LiquidFunds` → `world.Households[npc.HouseholdId].LiquidFunds`。**走査は NPC Id 昇順のまま**とし、世帯へは `npc.HouseholdId` を経由して書く。複数の NPC が同じ世帯を指すので書き込みの順序が結果を変えるが、NPC の走査順が固定されていれば決定的である。**これは [GDD02 §6.2.1](../03-gdd/02-economy.md)(世帯内の購入の決済順)の前触れなので、合成負荷のうちに踏ませる**
  - `Need.TargetNpcId` → `TargetHouseholdId`
  - `world.Knowledge.Add(...)` → `world.Knowledge[npcId].Add(...)`、`world.Ledgers.Add(...)` → `world.Ledgers[householdId].Add(...)`
  - 世帯在庫・工房在庫にも合成負荷を掛ける(**片方だけだと2本の区別がハッシュ回帰で一度も動かない**)
- **合成の初期配置を `Program.cs` に置く**(`PlaceSyntheticPopulation`)。NPC を世帯へ割り当て、階層・熟練度‰・区画Id を散らす。**全員が既定値のままだと、ハッシュから `HouseholdId` や `Rank` を落としても値が変わらず回帰が素通りする。** 乱数は使わない — シードに依存しない配置にして、「同一シード2プロセス実行の一致」が配置の再現性に左右されないようにする
- **`SyntheticDecaySystem` で熟練度‰ を動かす。** `Npcs` 区分が時刻とともに変わらないと、ハッシュから熟練度を落としても「初期値のぶんだけ違う」状態が残り続けて回帰が鈍る
- **合成システムは W1 限りのものであり、[GDD02 §2.2](../03-gdd/02-economy.md) の品目でも [GDD02 §2.4](../03-gdd/02-economy.md) の職業でもない。** `Program.cs` の既存の注記(「W2 で §3.3 の本物のシステム群が揃ったら差し替える」)はそのまま残す

## 落ちるべき条件(テスト)

**この節が完了条件そのもの。** 目標は「通ること」ではなく「**壊したときに落ちること**」である。

| #  | テスト | 検証内容 | この実装ミスで落ちる | 核心 |
| -- | ------ | -------- | -------------------- | ---- |
| 1  | `NpcStateNoLongerCarriesHouseholdOwnedState` | `NpcState` に `LiquidFunds` / `Inventory` という名のメンバが無い(リフレクション) | 廃止し忘れ、世帯と NPC に資金・在庫を二重に持たせる |  |
| 2  | `HouseholdRejectsUnsortedMembers` | 構成員が NpcId 昇順でない / 重複がある / 世帯主を含まない配列を渡すと例外 | 検証を省く。世帯内の処理順が入力の並び次第になり ADR-0002 の列挙順規約が破れる |  |
| 3  | `WorldAllocatesBothInventoriesAtItemCount` | `new World(npcCount: 2, householdCount: 3, itemCount: 9)` で、全世帯の世帯在庫・工房在庫がともに長さ 9 | 片方だけ確保する / `itemCount` を無視して空配列にする |  |
| 4  | `HashChangesWhenHouseholdFundsChange` | `Households[0].LiquidFunds` を変えるとハッシュが変わる | `Households` 区分を `Compute` に書き忘れる(§3.8 の含める表に反する) |  |
| 5  | `HashChangesWhenBankruptFlagChanges` | `Households[0].IsBankrupt` を 0→1 にするとハッシュが変わる | フラグを `Compute` に書き忘れる。GDD02 §6.2.2 の②④を駆動する状態が回帰テストの外に出る |  |
| 6  | `HashDistinguishesHouseholdInventoryFromWorkshopInventory` | 世帯在庫の itemId 5 に 3 を置いた世界と、**工房在庫**の itemId 5 に 3 を置いた世界のハッシュが異なる | 2本を1本に畳む / 片方だけ書く / 2本を同じ形式で続けて書き分けを失う。**薪(GDD02 §2.2)で目標在庫が一意に決まらなくなる構造要請が守られていない** | ★ |
| 7  | `HashDistinguishesKnowledgeOwners` | 同一内容の `PriceObservation` を `Knowledge[0]` に置いた世界と `Knowledge[1]` に置いた世界のハッシュが異なる | 所有者を畳んで平坦に走査する。W3 の Rumor(§3.3-9)の伝播先の取り違えが検出できなくなる | ★ |
| 8  | `HashDistinguishesLedgerOwners` | 同一内容の `LedgerEntry` を `Ledgers[0]` / `Ledgers[1]` に置いた世界のハッシュが異なる | 同上(所有者は世帯) |  |
| 9  | `HashChangesWhenObservationSellerChanges` | `SellerId` だけが違う観測でハッシュが変わる | 欄を足したのに `Compute` へ書き足すのを忘れる。GDD02 §8.1.1「売り手ごとに最新の1件」が同定できない |  |
| 10 | `HashChangesWhenNpcHouseholdOrRankOrSkillChanges` | `HouseholdId` / `Rank` / `SkillPermille` をそれぞれ単独で変えるとハッシュが変わる(3ケース) | `Npcs` 区分を `Id` だけのまま放置する |  |
| 11 | `SectionTagsAreFrozenAndHouseholdsIsNine` | 区分タグの値が `Clock=1 … Ledgers=8, Households=9`(リフレクション、または既存タグの値を動かすと落ちる形) | 途中に挿入して既存の値をずらす(§3.8「既存の値を動かさず末尾へ足す」に反する) |  |
| 12 | `HashDistinguishesSectionsOfEqualTotalByteWidth`(**既存テスト13の作り直し**) | 下記「衝突ペアの選び直し」の組でハッシュが異なる | 区分ヘッダ(タグ+要素数)の前置をやめる | ★ |
| 13 | `StateHasherCoverageTests`(既存) | `ExpectedWorldSections` に `Households` が入っている | 区分を足して期待一覧を更新しない(**このテストは実装前に赤になる。赤を確認してから更新すること**) |  |

**以下はレビューで足りないと分かって追加したものである。** 上の表は実装に渡した時点の指示であり、これが最終形ではない。

| #  | テスト | 検証内容 | この実装ミスで落ちる | 核心 |
| -- | ------ | -------- | -------------------- | ---- |
| 14 | `HashChangesWhenEitherInventoryChanges` | 世帯在庫・工房在庫が**それぞれ単独で**ハッシュに乗る | 片方を書き忘れる。**#6 はこれを捕まえない**(2つの世界で在庫の中身自体が違うため、残った1本の側でハッシュが変わる) |  |
| 15 | `HashDistinguishesHouseholdsByTheirImmutableFields` | 区画Id・世帯主・構成員Id・構成員数がハッシュに乗る | 世帯の不変欄を書き忘れる。**2プロセス比較では原理的に検出できない**(同一ビルド同士なので、見ていない状態があっても一致は成立する) | ★ |
| 16 | `HouseholdIdMatchesItsIndex` | 世帯 Id が添字と一致する | この不変条件が崩れる。**`household.Id` の書き忘れは値では検出できない**(冗長なため)ので、不変条件のほうを押さえる |  |
| 17 | `BankruptFlagRejectsValuesOtherThanZeroAndOne` | 破産中フラグが 0 / 1 以外を拒む | 素の `{ get; set; }` にする。doc が「0 / 1」と断定しているので検証済みと読まれる |  |
| 18 | `HouseholdCopiesTheMembersItWasGiven` | 構成員を複製して持つ | `.ToArray()` を落として参照を持つ |  |
| 19 | `SectionElementMembersAreFrozenSoNewOnesMustBeHashed` | 区分の**要素型**の欄の一覧が凍結されている | `HouseholdState` などに欄を足して `Compute` を更新し忘れる。**#13 は `World` 直下のメンバ名しか見ないのでこの経路を捕まえない** | ★ |

### 衝突ペアの選び直し(テスト12)

**既存の組は必ず壊れる。** `PriceObservation` が 24 → 28 バイトになり、`Market` 2件(24バイト)と釣り合わなくなる。既存テストのフィールド数凍結(`ExpectedPriceObservationFieldCount = 5`)がこれを検出して赤になる — **その赤を確認してから 6 に直すこと。**

**新しい組**(上で固定した書き込み形式が前提。両世界とも `new World(npcCount: 1, householdCount: 0, itemCount: 0)`):

- `marketOnly`: `Market` に7件。`Knowledge[0]` は空
- `knowledgeOnly`: `Market` は空。`Knowledge[0]` に3件

**バイト数の釣り合い:** `Market` 7件 × 12バイト + `Knowledge` の所有者1件分(`ownerId` 4 + 件数 4)= 92バイト。`Market` 0件 + `Knowledge` の所有者1件分(4 + 4 + 28 × 3)= 92バイト。

**値もバイト単位で一致させる**(リトルエンディアン・4バイト語で並べたとき、ヘッダを外した両者の語列が同一になる):

```
Market(キー昇順で列挙される):
    (ItemId 0, SellerId 3) = 11
    (ItemId 0, SellerId 4) = 12
    (ItemId 0, SellerId 5) = 1
    (ItemId 0, SellerId 6) = 13
    (ItemId 0, SellerId 7) = 0
    (ItemId 0, SellerId 8) = 14
    (ItemId 1, SellerId 0) = 15

Knowledge[0](この順で Add する):
    { ItemId=11, LocationId= 0, Price= 4, SellerId=12, ObservedAt=Tick(21474836480), Source=Heard  }
    { ItemId= 0, LocationId= 6, Price=13, SellerId= 0, ObservedAt=Tick(7),            Source=Direct }
    { ItemId= 8, LocationId=14, Price= 1, SellerId= 0, ObservedAt=Tick(15),           Source=Direct }
```

- `ObservedAt=Tick(21474836480)` は `5 × 2^32`。**下位語 0・上位語 5 を作るためであって、時刻としての意味は無い。** この意図をテストのコメントに書くこと
- **`Market` は `SortedDictionary` なので挿入順ではなくキー昇順で書かれる。** 上の7件は既に昇順に並べてある
- **実装後、`WriteSectionHeader` の中身を一時的に空にしてこのテストが赤になることを実測し、実測した日付をテストのコメントに残すこと**(既存テストが「2026-09-04 実測」と書いている作法を踏襲する)。**赤にならなければ、この組は釣り合っておらずテストは空虚である**

### 2プロセス一致(既存 CI)

既存の `vsim hash` による同一シード2プロセス実行の一致は緑のまま通ること。**区分が変わったのでハッシュ値は変わるが、固定しているゴールデン値は無いので更新するファイルは無い**([TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md))。

## 編集してよい文書

worktree をまたいだ所有権の宣言。**ここに挙がっていない文書は触らない。**

- [`docs/04-tdd/01-sim-core-and-m0.md`](../04-tdd/01-sim-core-and-m0.md) — **§3.2 の Npcs 行から「性格」と「役割」を落とす訂正のみ。** 理由は上の「`NpcState` の変更」に書いた2点(M0 に読む処理が無い / 役割は階層から一意に決まる)。**それ以外の節は触らない**

## このタスクで特に効く規約

- **`Dictionary` など列挙順が保証されないコレクションの列挙結果をロジックに使わない**([ADR-0002](../adr/0002-time-model-and-determinism.md))。本タスクで新設する走査はすべて配列の添字順(= Id 昇順)にすること。`Households` を `foreach` で回すのはよいが、**順序を前提にした処理を `SortedDictionary` 以外の辞書に載せない**

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **「核心」印(#6・#7・#12)のテストに変異を当てて落ちることを確認し、当てた変異と結果を残した**
- [ ] **テスト12 について、`WriteSectionHeader` を空にして赤になることを実測し、日付をコメントに残した**
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] `vsim hash` の同一シード2プロセス実行が一致する
- [ ] レビュアーエージェントの指摘が解消済み
