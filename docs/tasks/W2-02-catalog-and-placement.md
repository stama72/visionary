# W2-02: 品目・レシピ・職業・初期配置を定義する

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#33](https://github.com/stama72/visionary/issues/33)                |
| 根拠     | [GDD02 §2.2・§2.3・§2.4・§4.2・§4.3・§5.3・§8.1](../03-gdd/02-economy.md) / [TDD01 §3.2・§3.8](../04-tdd/01-sim-core-and-m0.md) / [GDD08 §2.1](../03-gdd/08-household-and-decision.md) |
| ブランチ | `feat/33-catalog-and-placement`                                      |
| worktree | `visionary/`(本体)                                                  |

> **この文書は使い捨ての作業指示である。** 実装完了時点で凍結し、以後の正はコードと GDD/TDD。

## スコープ

**世界の「定義」と、そこから作る初期状態を1つ作るタスクである。** 経済を動かすシステムは作らない。

**[TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md) が境界を決めている** — 「外部価格は設定と暦から決まる定数であり、**レシピ表・労働力係数・区画間距離と同じく状態ではない**(§3.2)。**ハッシュが対象とするのは `World` の状態であって、世界の定義ではない**」。したがって**品目表・レシピ表・職業表・区画の距離は `World` に載せず、ハッシュにも乗せない**。載るのは**生成された配置の結果**(区画Id・職業・在庫・資金・仕入れ移動平均単価)だけである。

**含まない:**

- 生産・消費・価格・取引のロジック(#34 以降)
- **仕入れ移動平均単価の更新規則**(#35)。本タスクは**器と初期値だけ**を持つ。窓付きにするか再帰形にするかは原価の式を書く #35 が決める。**#35 が窓付きを選ぶと標本バッファぶん状態が増え、ハッシュと凍結検査がもう一度動く** — それは承知のうえの分割である
- **`出荷目標在庫`**([GDD02 §8.1.1](../03-gdd/02-economy.md))。読むのは #35 であり、`WorldDefinition` は状態ではないので後から欄を足してもハッシュは動かない
- 1日消費量・季節係数(#34 / #31)
- `Need` / 理由コードの enum 化(#40)
- **値の作り込み。** 本タスクが置くのは**初期値**であって、`原価 < 外部買値 < 原価下限`([GDD02 §10.2](../03-gdd/02-economy.md))の検算と調整は #28 が持つ。**ただし置き場所と型と丸めの向きは本タスクで決める**
- **`vsim hash` の配線**。合成システム(`SyntheticLoadSystem` / `SyntheticDecaySystem`)のままにする。理由は下記「Runner の追随」

> **#35 へ申し送る論点が1件ある(本タスクでは直さない)。** [GDD02 §8.1.1](../03-gdd/02-economy.md) は「販売在庫 = 生産職では自分のレシピの**出力在庫の全量**」としているが、**鍛冶の出力は工具であり、工具は全世帯の設備でもある**([GDD02 §5.3](../03-gdd/02-economy.md))。素直に読むと鍛冶は自分の設備を売りに出す。本タスクは初期在庫として工具1個を置くだけで、この重なりには触れない。

## 作るもの

名前空間はすべて `Visionary.Sim`(既存の `World/` と同じ。フォルダは `Definition/`)。

### `Item`(static class、`Definition/Item.cs`)

**品目 Id の名前**([GDD02 §2.2](../03-gdd/02-economy.md) の表の順に 0〜8):

```csharp
public static class Item
{
    public const int Grain = 0;     // 穀物。1次・都市外市場
    public const int Timber = 1;    // 木材。1次・都市外市場
    public const int IronOre = 2;   // 鉄鉱石。1次・都市外市場
    public const int Charcoal = 3;  // 木炭。1次・都市外市場(金属加工専用)
    public const int Flour = 4;     // 小麦粉。中間
    public const int Firewood = 5;  // 薪。中間。必需 + 生産入力
    public const int Bread = 6;     // パン。最終・必需
    public const int Beer = 7;      // ビール。最終・嗜好
    public const int Tools = 8;     // 工具。最終・耐久(GDD02 §5.3)

    public const int Count = 9;
}
```

- **品目を `enum` にしないのは、品目 Id が配列の添字だからである。** 在庫は `int[]`(添字 = itemId)なので、`enum` にすると在庫に触れるたび `(int)` のキャストが入る。**職業は逆に `enum` にする**(下記)— あちらは世帯の欄として比較・代入される値であって添字ではない。**分ける基準は「添字か、値か」である**

### `Occupation`(enum、`Definition/Occupation.cs`)

```csharp
public enum Occupation
{
    Miller = 0,      // 水車小屋番。穀物 → 小麦粉
    Baker = 1,       // パン屋。小麦粉 + 薪 → パン
    Brewer = 2,      // 醸造。穀物 + 薪 → ビール
    Woodworker = 3,  // 木材加工。木材 → 薪
    Smith = 4,       // 鍛冶。鉄鉱石 + 木炭 → 工具
}
```

- **値は [GDD02 §2.4](../03-gdd/02-economy.md) の採番そのもの**(§6.3 と GDD08 §9 の「同値なら職業 Id 昇順」がこの値に依存する)。`RandomStream` / `StateHasher.Section` と同じく**振り直してはならない**
- **`enum` 化は [W2-01 の仕様](W2-01-household-state.md) が本タスクへ送った作業である**(「職業 Id は GDD02 §2.4 が 0〜4 と採番済みで、enum 化は #33 の作業」)

### `ItemQuantity` / `Recipe`(`Definition/Recipe.cs`)

```csharp
public readonly record struct ItemQuantity
{
    public int ItemId { get; init; }
    public int Quantity { get; init; }   // 1以上
}

public sealed class Recipe
{
    public Occupation Occupation { get; }
    public ItemQuantity[] Outputs { get; }   // 1件以上。複数出力を許す(GDD02 §2.3)
    public ItemQuantity[] Inputs { get; }    // 0件を許す(GDD02 §2.3。M0 に該当する職業は無い)
    public int LaborPermille { get; }        // 所要労働‰。1000‰ = 親方1人日相当(GDD02 §5.2)

    public Recipe(Occupation occupation, ItemQuantity[] outputs, ItemQuantity[] inputs, int laborPermille);
}
```

- **入力0件を許すのは [GDD02 §2.3](../03-gdd/02-economy.md) の決定である。** M0 の5職業はすべて財の投入を持つが、[GDD11](../03-gdd/11-external-trade.md) の貿易商と [GDD12](../03-gdd/12-countryside-and-fairs.md) の農村職業が戻ったときに発火する分岐([GDD02 §8.1.1](../03-gdd/02-economy.md) の原価の2分岐)がこれに対応する。**構造としては残すが、M0 で通る経路ではない**
- 検証(`ArgumentException` / `ArgumentOutOfRangeException`):`outputs` が空、`Quantity` < 1、`laborPermille` < 1、`Outputs` / `Inputs` の中で **同じ `ItemId` が2度現れる**、を拒否する。**渡された配列は複製して持つ**
- **`ItemId` の値域(0〜8)は `Recipe` では検査しない。** 品目数を知っているのは `WorldDefinition` なので、そちらで検査する。**`Recipe` 単体では「非負」すら検査しない** — 中途半端な検査は「検査済み」と読ませるため置かない

### `District`(static class、`Definition/District.cs`)

**区画と距離**([GDD02 §4.3](../03-gdd/02-economy.md))。**状態ではない**([TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md))。

```csharp
public static class District
{
    public const int GridSide = 3;                 // 3×3(GDD02 §4.3)
    public const int Count = GridSide * GridSide;  // 9
    public const int ExternalMarketDistrictId = 4; // 都市外市場は中心に固定(GDD02 §4.3・§10)

    public static int RowOf(int districtId);       // districtId / GridSide(行優先)
    public static int ColumnOf(int districtId);    // districtId % GridSide
    public static int Distance(int fromDistrictId, int toDistrictId);  // マンハッタン距離 0〜4
}
```

- **区画数を `WorldDefinition` の可変値にしない。** 距離の式が 3×3 を前提にしているので、可変にすると**式が黙って無視する設定値**が生まれる。[GDD02 §4.3](../03-gdd/02-economy.md) は 3×3 を決定として書いている
- **`RowOf` / `ColumnOf` / `Distance` はいずれも 0〜8 の外を `ArgumentOutOfRangeException` で拒む。** 検査しないと `RowOf(9) == 3` という**存在しない行**が返り、距離が黙って 0〜4 の外に出る
- **距離表を持たない**([GDD02 §4.3](../03-gdd/02-economy.md))。Id から行・列が定まる

### `WorldDefinition`(sealed class、`Definition/WorldDefinition.cs`)

**世界の定義。`World` の外に置く**([TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md))。

```csharp
public sealed class WorldDefinition
{
    public int ItemCount { get; }                      // 9(GDD02 §2.2)
    public int HouseholdsPerOccupation { get; }        // 2(GDD02 §2.4)
    public Recipe[] Recipes { get; }                   // 添字 = (int)Occupation。長さ 5

    public int InitialLiquidFunds { get; }             // 単位: 貨幣
    public int[] InitialAcquisitionCost { get; }       // 添字 = itemId。単位: 貨幣/1単位(GDD02 §8.1)
    public int[] InitialHouseholdInventory { get; }    // 添字 = itemId。単位: 個
    public int InitialWorkshopInputDays { get; }       // 単位: 日。工房在庫[入力j] = これ × 必要数量j
    public int InitialToolStock { get; }               // 単位: 個。工房在庫[Item.Tools]。1以上(下記)
    public int[] InitialSkillPermilleByRank { get; }   // 添字 = (int)NpcRank。長さ 3。単位: ‰

    public static WorldDefinition M0 { get; }          // 下の初期値表

    public int OccupationCount => Recipes.Length;
    public int HouseholdCount => OccupationCount * HouseholdsPerOccupation;
    public int NpcCount => HouseholdCount * 2;         // 親方1・徒弟1(GDD02 §2.4)
}
```

- **`static` な可変テーブルにしない。** #28 が値を差し替え、[TDD01 §4.1](../04-tdd/01-sim-core-and-m0.md) は JSON 設定を予定している。加えて、**`static` な可変配列は `StateHasherCoverageTests` の凍結検査にも `StateHasher` にも2プロセス比較にも一切現れない** — 同テストの doc コメントが警告しているとおりである。`M0` は毎回新しいインスタンスを組み立てて返すこと
- **世帯の人数を可変値にしない。** 親方1・徒弟1 は [GDD02 §2.4](../03-gdd/02-economy.md) の決定であり、人数だけ可変にすると「3人目の階層が未定義」という穴になる。**人数は `NpcCount` として導出する**
- **`InitialToolStock` は 1以上でなければならない。** 0 を許すと [GDD02 §5.3](../03-gdd/02-economy.md) の設備係数が全世帯 0‰ になり、初日から全生産が停止する。**鍛冶自身も工具が無いと工具を作れないので回復経路が無く、[GDD02 §4.2](../03-gdd/02-economy.md)「詰みは作らない」に正面から反する。** コンストラクタで拒否する
- **`InitialAcquisitionCost` の各要素は 1以上。** [GDD02 §8.1](../03-gdd/02-economy.md) が「与えないと初日の原価が 0 になり、§8.1.1 が『0 が恒久に固定される』と警告している経路を初日に踏む」と書いている。**0 を許さないことで、`M0` の表を書き換えたときにその経路へ落ちない**
- コンストラクタの検証:`Recipes` の長さが 1以上、**添字と `Recipe.Occupation` の値が一致**、全レシピの全 `ItemId` が 0〜`ItemCount-1`、`InitialAcquisitionCost` / `InitialHouseholdInventory` の長さが `ItemCount`、`InitialSkillPermilleByRank` の長さが 3 で各要素 0〜1000、`HouseholdsPerOccupation` が 1以上、`HouseholdCount` が `District.Count` 以上 `District.Count * 2` 以下(下記の配置の前提)
- **渡された配列はすべて複製して持つ。** `MemberNpcIds` と同じく、**受け取った側が書き換える経路は塞がらない**(`int[]` をそのまま公開するため)。読むだけにすること

#### `WorldDefinition.M0` の初期値

**すべて初期値であり調整対象**(CLAUDE.md「数値(閾値・係数)は初期値であり調整対象。固定すべきは構造と依存関係」)。**検算と調整は [#28](https://github.com/stama72/visionary/issues/28)。** 出典を持つのは**レシピの品目の組**([GDD02 §2.4](../03-gdd/02-economy.md))だけで、**数量・所要労働‰・金額はここが初出である**([GDD02 §13.2](../03-gdd/02-economy.md) が「値として残っているもの」に挙げている項目そのもの)。

| 職業 | 入力 | 出力 | 所要労働‰ |
| ---- | ---- | ---- | --------- |
| 0 Miller | 穀物 2 | 小麦粉 1 | 1000 |
| 1 Baker | 小麦粉 1 + 薪 1 | パン 2 | 1000 |
| 2 Brewer | 穀物 2 + 薪 1 | ビール 1 | 1000 |
| 3 Woodworker | 木材 1 | 薪 3 | 1000 |
| 4 Smith | 鉄鉱石 2 + 木炭 1 | 工具 1 | 1000 |

> **所要労働‰ を5職業とも 1000 にするのは、意図的に無風の初期値を置いているためである。** [GDD02 §5.2](../03-gdd/02-economy.md) の労働力合計は親方1000‰ + 徒弟300‰ = 1300‰ なので、**1300‰ を超える所要労働を置くとその職業の生産能力が 0 になり、初日から止まる。** #28 が調整するときはこの上限に触れること。

| 品目 | 0 穀物 | 1 木材 | 2 鉄鉱石 | 3 木炭 | 4 小麦粉 | 5 薪 | 6 パン | 7 ビール | 8 工具 |
| ---- | -- | -- | -- | -- | -- | -- | -- | -- | -- |
| `InitialAcquisitionCost` | 10 | 8 | 20 | 12 | 22 | 6 | 16 | 30 | 60 |
| `InitialHouseholdInventory` | 0 | 0 | 0 | 0 | 0 | 4 | 4 | 2 | 0 |

- `InitialLiquidFunds` = 200
- `InitialWorkshopInputDays` = 5
- `InitialToolStock` = 1
- `InitialSkillPermilleByRank` = `[700, 400, 100]`(添字 = `NpcRank` の Master / Journeyman / Apprentice)

> **熟練度‰ を階層で違う値にするのは、ハッシュの回帰を鈍らせないためである。** [W2-01 の仕様](W2-01-household-state.md)が合成初期配置について同じ罠を記録している — **全員が同じ値だと、ハッシュから `SkillPermille` を落としても値が変わらない。** `Journeyman` は M0 に存在しないが、[TDD01 §3.2](../04-tdd/01-sim-core-and-m0.md) は熟練度を「器として先に持つ」としているので欄だけ埋める。

### `WorldGenerator`(static class、`Definition/WorldGenerator.cs`)

```csharp
public static class WorldGenerator
{
    /// <summary>GDD02 §2.2・§2.4・§4.3・§8.1 の初期世界を1つ生成する。</summary>
    public static World Generate(WorldDefinition definition, RandomSource random);
}
```

**乱数は `RandomStream.WorldGen`(既存の系統1)を `Tick.Zero`・`RandomSource.NoEntity` で1本だけ開く。** `ISimSystem` ではないので `SimContext` を通らず、`RandomSource.Open` を直接呼ぶ。**他の系統を開いてはならない**(ADR-0002)。

**生成順序は仕様である。変えると同じシードから別の世界が出る**([docs/process/02](../process/02-task-spec.md) 規則6):

```
1. world = new World(definition.NpcCount, definition.HouseholdCount, definition.ItemCount)
2. rng = random.Open(RandomStream.WorldGen, Tick.Zero)
3. districts   = AssignDistricts(rng, definition.HouseholdCount)
4. occupations = AssignOccupations(rng, districts, definition)
5. 世帯Id 昇順に 0 .. HouseholdCount-1:
       master     = householdId * 2
       apprentice = householdId * 2 + 1
       Npcs[master]     .HouseholdId = householdId, .Rank = Master,     .SkillPermille = 定義の値
       Npcs[apprentice] .HouseholdId = householdId, .Rank = Apprentice, .SkillPermille = 定義の値
       Households[householdId] = new HouseholdState(
           id: householdId, districtId: districts[householdId],
           headNpcId: master, memberNpcIds: new[] { master, apprentice },
           itemCount: definition.ItemCount)
       .Occupation                 = occupations[householdId]
       .LiquidFunds                = definition.InitialLiquidFunds
       .HouseholdInventory[i]      = definition.InitialHouseholdInventory[i]   (全 i)
       .PurchaseUnitCostAverage[i] = definition.InitialAcquisitionCost[i]      (全 i)
       .WorkshopInventory[入力j.ItemId] = InitialWorkshopInputDays × 入力j.Quantity (自職業のレシピの全 j)
       .WorkshopInventory[Item.Tools]  += definition.InitialToolStock
```

- **`Generate` の入口では `definition` の妥当性を検査しない。** `WorldDefinition` のコンストラクタが既に通したものしか存在しないためである。**検査するのは `definition` が `null` でないことだけ**
- **NpcId の割り当てに乱数を使わない。** 世帯 h の親方が `2h`、徒弟が `2h + 1` と決まっていれば、構成員配列は常に `[2h, 2h+1]` で昇順が自明になる
- **工具を `+=` で足すのは、#28 がレシピを変えて工具を入力に持つ職業が現れたときに、設備ぶんが黙って消えないためである。** M0 の5レシピはどれも工具を入力に持たないので、現時点では `=` でも結果は同じである

**`AssignDistricts(rng, householdCount)` — 戻り値は `districts[householdId] = 区画Id`:**

```
slots = [0, 1, ..., District.Count-1]            ← 全区画を1つずつ。空区画を作らない
extra = householdCount - District.Count          ← M0 は 10 - 9 = 1
pool  = [0, 1, ..., District.Count-1] を Fisher-Yates で先頭 extra 個だけ部分シャッフル
slots に pool の先頭 extra 個を足す                ← 相異なる区画なので 1区画あたり最大2世帯
slots を Fisher-Yates で全体シャッフル              ← 世帯Id と区画の対応を無作為化
return slots
```

**`AssignOccupations(rng, districts, definition)` — 戻り値は `occupations[householdId]`。棄却法:**

```
labels = 職業 k を HouseholdsPerOccupation 個ずつ並べた配列(M0 は [0,0,1,1,2,2,3,3,4,4])
for attempt in 1 .. MaxPlacementAttempts(= 1000):
    labels を Fisher-Yates で全体シャッフル
    区画ごとに職業の重複が無ければ return labels
throw new InvalidOperationException(...)   ← 定義がこの制約を満たせない
```

- **棄却法を採る。** M0 で衝突する確率は 1/9(二重に入る区画の2世帯目が1世帯目と同職業になる確率)なので、期待試行は約 1.125 回である。**構成的に「衝突したら決定的に入れ替える」案を採らないのは、入れ替え規則が配置に偏りを入れ、かつ二重区画が複数ある一般の世帯数で終端性の証明が要るからである。** 棄却法は上限で明示的に失敗する
- **上限 1000 を置くのは、制約を満たせない定義で無限に回らないためである。** `WorldDefinition` の前提検査(世帯数が `District.Count`〜`District.Count * 2`)を通っても、例えば `HouseholdsPerOccupation` が `District.Count` を超える定義は構造的に満たせない
- **Fisher-Yates の向きも仕様である:** `for i = n-1 downto 1: j = rng.NextInt(0, i + 1); swap(a[i], a[j])`。**向きを変えると同じシードから別の配置が出る**
- **重複検査に `Dictionary` / `HashSet` を使わない**(ADR-0002)。区画は 0〜8 なので `int[District.Count]` に職業を書き込んで突き合わせる

> **これは「配置が振られる」ことそのものが仕様である。** 同一マスターシードなら同一配置、違うシードなら違う配置になる。[TDD01 §4.1](../04-tdd/01-sim-core-and-m0.md) の「条件あたり10反復」が**実際に10通りの地理**を見ることになり、[GDD02 §12-4](../03-gdd/02-economy.md)(区画間の価格差が消えない)が特定の配置の産物でなくなる。ADR-0002 の共通乱数法により、**A/B の2条件は同一シードなら同一配置で揃う。**

### `HouseholdState` の変更(`Visionary.Sim`)

```csharp
public Occupation Occupation { get; set; }       // int OccupationId から改名・型変更
public int[] PurchaseUnitCostAverage { get; }    // 追加。添字 = itemId。単位: 貨幣/1単位
```

- **`OccupationId`(int)を `Occupation`(enum)へ改名する。** `NpcRank` と同じ扱いになる。`set` を残すのは [GDD02 §6.3](../03-gdd/02-economy.md) ④の職業付け替えのため
- **`PurchaseUnitCostAverage` は「仕入れ移動平均単価」である**([GDD02 §8.1.1](../03-gdd/02-economy.md) の語)。doc コメントに単位を書くこと(CLAUDE.md)
- **更新規則は本タスクに無い。** コンストラクタは長さ `itemCount` の配列を 0 で確保するだけで、値を入れるのは `WorldGenerator` である。**「移動平均の初期値を決める」のが [GDD02 §8.1](../03-gdd/02-economy.md) の要求であり、初期値を書く先がここになる**

### `StateHasher` の変更(`Visionary.Sim.Determinism`)

**区分は増えない。`Households` 区分の1要素に書くものが増えるだけである。**

| 区分 | 変更 |
| ---- | ---- |
| `Households` | `Occupation` を `(int)` として書く(バイト幅は変わらない)。**要素の末尾に**「仕入れ移動平均単価の長さ、各値」を足す |

- **要素の末尾に足すのは、区分タグを末尾に足すのと同じ規律である**([TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md))。ゴールデン値は無いので互換の要請は無いが、差分が読みやすい
- **既存の在庫2本と同じく、長さを前置してから各値を書く**
- **ハッシュ値は変わる。固定しているゴールデン値は無いので更新するファイルは無い**([TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md))

### Runner の追随(`Visionary.Sim.Runner`)

- `SyntheticLoadSystem` の `household.OccupationId = rng.NextInt(0, OccupationCount)` を `Occupation` へ追随させる。**`OccupationCount` のローカル定数は `Definition` 側を使わず、合成負荷の値のままでよい** — 合成システムは GDD02 の職業ではない(既存の注記どおり)
- **`vsim hash` を `WorldGenerator` に差し替えない。** 合成システムは #34〜#39 が揃うまでのハッシュ検証手段であり、CLI の `--npcs` / `--households` / `--items` に依存している。**本物の配置と合成システムを混ぜると、回帰が出たときにどちらの側の回帰か切り分けられない。** `Program.cs` の既存の注記(「W2 で §3.3 の本物のシステム群が揃ったら差し替える」)をそのまま残す
- **したがって `WorldGenerator` の呼び出し元は本タスクではテストだけになる。** 死んだコードではなく、#34 以降が入口として使う

## 落ちるべき条件(テスト)

**この節が完了条件そのもの。** 目標は「通ること」ではなく「**壊したときに落ちること**」である。

構造制約の検査(#1・#2・#4・#7)は **`masterSeed` を 1〜200 まで回して全シードで成り立つこと**を見る。1シードだけだと、たまたま通った配置と、構造で保証された配置が区別できない。

| #  | テスト | 検証内容 | この実装ミスで落ちる | 核心 |
| -- | ------ | -------- | -------------------- | ---- |
| 1  | `EveryDistrictHoldsOneOrTwoHouseholds` | 200シードすべてで、区画 0〜8 の世帯数がいずれも 1以上2以下 | 空区画を許す(`slots` の土台に全区画を1つずつ置くのをやめる)/ 3世帯以上を許す(追加ぶんを相異なる区画から採らない)。[GDD02 §4.3](../03-gdd/02-economy.md) の密度が崩れ、**§2.4 が密度から導いている「中心に隣接する4区画(1/3/5/7)は必ず埋まる」= §4.2 の観測の広がりの下限**が消える | ★ |
| 2  | `SameOccupationHouseholdsNeverShareADistrict` | 200シードすべてで、同じ職業の2世帯の区画Id が異なる | 棄却検査を省く / 検査の向きを逆にする。当該品目の売り手が1区画に集まり、**[GDD02 §12-4](../03-gdd/02-economy.md)(区画間の価格差が消えない)が「消える」のではなく最初から存在しなくなる**。§2.4 が「値ではなく構造の制約である」と明記している | ★ |
| 3  | `PlacementIsDeterministicForTheSameSeed` | 同一シードから2回生成した `World` の `StateHasher.Compute` が一致する(3シード) | 系統外から乱数を引く / 列挙順が不定のコレクションに配置を載せる |  |
| 4  | `PlacementVariesAcrossSeeds` | 200シードの `StateHasher.Compute` に2種類以上の値が現れる | **乱数を引いておきながら結果に使わない**(固定配置に落ちる)。**#3 は固定配置でも緑になるので、これが無いと乱数の存在が検証されない** |  |
| 5  | `EachHouseholdHasOneMasterAndOneApprentice` | 全世帯の構成員がちょうど2人で、`NpcRank` が `Master` と `Apprentice` が1人ずつ、かつ `HeadNpcId` の NPC が `Master` | 徒弟を置き忘れる / 世帯主を徒弟にする。[GDD02 §2.4](../03-gdd/02-economy.md)「世帯構成は親方1・徒弟1」が崩れ、**§5.2 の「階層で係数が違うこと」を M0 で見たいという目的そのものが空になる** |  |
| 6  | `HouseholdMembershipAgreesInBothDirections` | 全 NpcId について `Households[npc.HouseholdId].MemberNpcIds` が `npc.Id` を含み、逆に全世帯の全構成員の `HouseholdId` がその世帯Id と一致する | 片方向だけ書く。世帯→NPC と NPC→世帯 が食い違い、[GDD02 §6.2.1](../03-gdd/02-economy.md) の世帯内の決済が取りこぼす |  |
| 7  | `EveryOccupationGetsExactlyTheDefinedNumberOfHouseholds` | 200シードすべてで、各職業がちょうど `HouseholdsPerOccupation` 世帯 | シャッフルで職業を落とす / 重複させる。**1品目の売り手が1世帯になると [GDD02 §8.1.1](../03-gdd/02-economy.md) の相場基準が「他の売り手の観測0件」に落ち、§8.1.1 が名指しで警告する純粋な自己ループが成立する** |  |
| 8  | `EveryWorkshopStartsWithAtLeastOneTool` | 全世帯の `WorkshopInventory[Item.Tools]` が 1以上 | 工具を与えない。[GDD02 §5.3](../03-gdd/02-economy.md) の設備係数が全世帯 0‰ になり**初日から全生産が停止する。鍛冶自身も工具が無いと工具を作れないので回復経路が無く、[GDD02 §4.2](../03-gdd/02-economy.md)「詰みは作らない」に反する** |  |
| 9  | `WorldDefinitionRejectsZeroToolStock` | `InitialToolStock = 0` の定義がコンストラクタで例外 | 値の検証を省く。**#8 は `M0` しか見ないので、定義を差し替えた #28 のときに同じ詰みへ落ちる経路を塞げない** |  |
| 10 | `InitialCostAverageIsSeededFromTheCatalogAndNeverZero` | 全世帯・全品目で `PurchaseUnitCostAverage[i] == definition.InitialAcquisitionCost[i]` かつ 1以上 | 取得原価を与え忘れる / 0 で埋める。[GDD02 §8.1](../03-gdd/02-economy.md) が「与えないと初日の原価が 0 になる」、§8.1.1 が「**0 が恒久に固定され**、下流の原価が移動平均で 0 へ向かい、原価下限が消えて半減ループに歯止めがなくなる」と警告している経路を初日に踏む |  |
| 11 | `WorldDefinitionRejectsZeroAcquisitionCost` | `InitialAcquisitionCost` に 0 を含む定義がコンストラクタで例外 | 同上。**#10 は `M0` しか見ない** |  |
| 12 | `WorkshopStocksTheInputsOfItsOwnRecipe` | 各世帯の `WorkshopInventory[入力j] == InitialWorkshopInputDays × 数量j`、かつ**自職業のレシピに現れない品目**(工具を除く)が 0 | 全世帯に全品目を配る / 職業を取り違えて別のレシピの入力を積む。**#34 の生産が初日に入力切れを起こさない前提が崩れる** | ★ |
| 13 | `DistrictIdIsRowMajor` | `RowOf(5)==1` かつ `ColumnOf(5)==2`、`RowOf(3)==1` かつ `ColumnOf(3)==0`、`RowOf(2)==0` かつ `ColumnOf(2)==2` | **行優先を列優先で書く**(`RowOf` と `ColumnOf` の `/` と `%` が入れ替わる)。[GDD02 §4.3](../03-gdd/02-economy.md)「区画 Id は行優先で 0〜8」。**下の注記のとおり、この誤りは `Distance` のテストでは原理的に捕まらない** |  |
| 14 | `DistanceIsManhattanNotChebyshev` | `Distance(0,8)==4`、`Distance(2,6)==4`、`Distance(0,2)==2`、`Distance(4,4)==0`、`Distance(1,7)==2`、および 81組すべてで `Distance(a,b)==Distance(b,a)` かつ 0〜4 | 行と列の差を**加算せず `max` を採る**(チェビシェフ距離。`Distance(0,8)` が 2 になる)/ 絶対値を落とす(対称性が破れ、負の距離が出る)。**距離は [GDD06 §2](../03-gdd/06-trade-and-negotiation.md) の実質コストに乗る唯一の空間の摩擦の数値表現**([GDD02 §4.3](../03-gdd/02-economy.md))なので、縮んだ距離は摩擦をそのまま薄くする |  |
| 15 | `DistrictHelpersRejectIdsOutsideTheGrid` | `RowOf` / `ColumnOf` / `Distance` が負・9以上で例外(`Distance` は両引数それぞれ) | 添字を検証しない。`RowOf(9) == 3` という**存在しない行**が返り、距離が黙って 0〜4 の外へ出る |  |
| 16 | `M0RecipesMatchTheOccupationTable` | `M0` の5レシピが職業 0〜4 と1対1に対応し、入出力の品目が [GDD02 §2.4](../03-gdd/02-economy.md) の表と一致する(穀物→小麦粉 / 小麦粉+薪→パン / 穀物+薪→ビール / 木材→薪 / 鉄鉱石+木炭→工具) | レシピ表を写し間違える。**#34 の生産が違う品目を作り、誰も気付かない** — 数量は調整対象だが**品目の組は GDD02 の決定である** |  |
| 17 | `EveryItemIsEitherImportedOrProducedByExactlyOneRecipe` | 品目 0〜3 はどのレシピの出力にも現れず、品目 4〜8 はちょうど1つのレシピの出力である | 供給の欠けた品目・供給の重なる品目を作る。[GDD02 §2.2](../03-gdd/02-economy.md) の供給列と食い違い、**その品目が永久に0在庫になる**か、[GDD02 §2.4](../03-gdd/02-economy.md) の「1品目につき売り手2世帯」が崩れる |  |
| 18 | `RecipeRejectsDuplicateItemIdsAndEmptyOutputs` | 同じ `ItemId` を2度持つ入力(または出力)、空の `Outputs`、`Quantity < 1`、`LaborPermille < 1` で例外 | 検証を省く。**`穀物2 + 穀物3 → …` が通ると、[GDD02 §8.1.1](../03-gdd/02-economy.md) の原価 `Σ_j(単価 × 数量_j)` が同じ品目を2度数える** |  |
| 19 | `WorldDefinitionRejectsHouseholdCountsThatBreakTheDensity` | 世帯数が `District.Count` 未満、または `District.Count * 2` 超の定義で例外 | 前提を検査せず棄却ループに入り、1000回試して `InvalidOperationException` になる(**原因が「密度を満たせない定義」だと分からないメッセージで落ちる**) |  |
| 20 | `HashChangesWhenOccupationOrCostAverageChanges` | `Households[0].Occupation` を変えると、また `PurchaseUnitCostAverage[0]` を変えるとハッシュが変わる(2ケース) | `Occupation` の enum 化で `(int)` の書き出しを落とす / 仕入れ移動平均単価を `Compute` に書き足し忘れる。**後者は2プロセス比較では原理的に検出できない**(同一ビルド同士なので、見ていない状態があっても一致は成立する) |  |
| 21 | `SectionElementMembersAreFrozenSoNewOnesMustBeHashed`(既存) | `HouseholdState` の期待欄が `Occupation` と `PurchaseUnitCostAverage` を含む | 欄を足して `Compute` を更新し忘れる。**このテストは実装前に赤になる。赤を確認してから期待一覧を更新すること** |  |

### 核心印について

**印を付けたのは #1・#2・#12 の3件である**([docs/process/02](../process/02-task-spec.md) の「1タスクあたり2〜3件」)。

**基準は「テストが自分で集合を組み立てるかどうか」である。** この3件はいずれも、区画ごとの世帯数・職業ごとの区画・世帯ごとの入力品目という**中間の集合をテスト側が構築してから表明する**。その構築を誤ると、**表明が空の集合に対して実行され、何も検査せずに緑になる**。変異はこの空虚を暴くためのものであって、実装の誤りを見つけるためではない。

**#8・#10・#13・#14 に印を付けないのは、いずれも単一の値の表明であり、空虚になりようがないからである。** 「工具を 0 にする」「行と列を入れ替える」といった変異はテストの表明を直接反転させるだけで、テストが空虚でないことの証拠を足さない。**代わりに、値が変わったときに検査の側が追随するよう #9・#11 で「定義の側が 0 を拒む」ことを押さえてある。**

### 距離のテストで行優先は守れない

**マンハッタン距離は転置に対して不変である。** 行優先は `(行, 列) = (id / 3, id % 3)`、列優先は `(id % 3, id / 3)` であり、両者は行と列を入れ替えただけなので `|Δ行| + |Δ列|` は完全に同じ値になる。**したがって `Distance` をいくら検証しても、行優先か列優先かは判別できない。** [GDD02 §4.3](../03-gdd/02-economy.md)「区画 Id は行優先で 0〜8」を守れるのは `RowOf` / `ColumnOf` を直接見る #13 だけであり、#14 が押さえるのは**距離の式そのもの**(加算か `max` か、絶対値を採るか)である。**2つを1つのテストに畳まないこと** — 畳むと、片方が捕まえないものをもう片方が捕まえているように読める。

### 乱数の系統について — テストで押さえないもの

**「`WorldGen` 以外の系統から引いていないこと」はテストで押さえない。** `RandomSource` は `readonly struct` で消費の状態を持たないため、**どの系統を何回開いたかは外から観測できない。** `WorldGenerator` は `ISimSystem` ではないので `SimContext` の保護(現在のシステムの系統しか開けない)も効かない。**ここは機械で守れておらず、レビュー観点として残る。** 保証を書くときは残る穴も書く([docs/process/02](../process/02-task-spec.md) 規則4)。

## 編集してよい文書

worktree をまたいだ所有権の宣言。**ここに挙がっていない文書は触らない。**

- [`docs/04-tdd/01-sim-core-and-m0.md`](../04-tdd/01-sim-core-and-m0.md)
  - **§3.2 の Households 行**と、**§3.8 の含める表の Households 行**に「**仕入れ移動平均単価**」を足す。理由は本書「`HouseholdState` の変更」に書いたもの([GDD02 §8.1](../03-gdd/02-economy.md) の移動平均の初期値の置き場所)
  - **§3.6 の「W1 で置いた仮決め」表のうち、W2-01(#32)で既に閉じた2行を現況に直す** — `Need.TargetNpcId` は `TargetHouseholdId` に、`PriceObservation` の売り手は `SellerId` として実在する。**表が「W2 着手前に確定させる」と題したまま確定済みの行を載せている。** CLAUDE.md「気付いたら直す。古い件数は、見つけたそのとき、触っているブランチで直す」に従う。**閉じた行を消すのではなく「W2-01 で確定済み」と分かる形にすること** — 何をどう決めたかが失われる
  - **それ以外の節は触らない**
- [`docs/03-gdd/02-economy.md`](../03-gdd/02-economy.md)
  - **§2.4 に「初期配置はマスターシードから振る」旨を1段落足す。** 同一シードなら同一配置、違うシードなら違う配置になること、そして [TDD01 §4.1](../04-tdd/01-sim-core-and-m0.md) の10反復が10通りの地理を見ることで §12-4 が特定の配置の産物でなくなること。**§2.4 が既に持っている構造制約(同職業は別区画 / 密度)の直後に置く** — 制約と、その制約の下で何が振られるかは対で読む
  - **数量・金額の初期値は GDD へ書かない。** 「数値は初期値であり調整対象」(GDD README 執筆ルール)であり、[GDD02 §13.2](../03-gdd/02-economy.md) が既に「値として残っているもの」として項目を挙げている。**本タスクが置くのは値ではなく、値の置き場所である**
  - **それ以外の節は触らない**

## このタスクで特に効く規約

- **`Dictionary` / `HashSet` など列挙順が保証されないコレクションの列挙結果をロジックに使わない**(ADR-0002)。区画ごとの職業の重複検査は `int[District.Count]` で書く。**「列挙しないから安全」と考えない** — `SimContext` が同じ理由で `SortedSet` を選んでいる
- **乱数は `RandomStream.WorldGen` からだけ引く**(ADR-0002)。上記のとおり**機械では守れない**
- **除算は切り上げヘルパー経由**(CLAUDE.md / [GDD01 §2.3](../03-gdd/01-trust-and-conversation.md))。本タスクで裸の `/` を書いてよいのは `District.RowOf`(グリッドの行の算出であって金額でも比率でもない)だけであり、**その1か所に「切り上げ規約の対象ではない」と doc コメントを置くこと**
- **係数の定数定義には単位のコメントを必須**(CLAUDE.md)。`LaborPermille`(‰)/ `InitialSkillPermilleByRank`(‰)/ `InitialAcquisitionCost`(貨幣/1単位)/ `InitialWorkshopInputDays`(日)/ `InitialToolStock`(個)

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **「核心」印(#1・#2・#12)のテストに変異を当てて落ちることを確認し、当てた変異と結果を残した**
- [ ] **#21 が実装前に赤になることを確認してから期待一覧を更新した**
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] `vsim hash` の同一シード2プロセス実行が一致する
- [ ] レビュアーエージェントの指摘が解消済み
