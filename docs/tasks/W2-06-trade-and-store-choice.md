# W2-06: Trade システムと店の選択

| 項目     | 内容                                                                 |
| -------- | -------------------------------------------------------------------- |
| issue    | [#37](https://github.com/stama72/visionary/issues/37)                |
| 根拠     | [GDD06 §2〜§4](../03-gdd/06-trade-and-negotiation.md) / [GDD02 §6.2・§6.2.1・§8.1.1・§8.2.3](../03-gdd/02-economy.md) / [GDD08 §7.2・§9](../03-gdd/08-household-and-decision.md) / [TDD01 §3.2・§3.3](../04-tdd/01-sim-core-and-m0.md) |
| ブランチ | `feat/37-trade-and-store-choice`                                     |
| worktree | `visionary/`(本体)                                                  |

> **この文書は使い捨ての作業指示である。** 実装完了時点で凍結し、以後の正はコードと GDD/TDD。

## スコープ

**買い手が店を選び、実際に金と物が動く段である。** [#35](https://github.com/stama72/visionary/issues/35) が提示価格を、[#36](https://github.com/stama72/visionary/issues/36) が予算と観測を作った。本タスクで **`Market` → 店の選択 → 約定 → 帳簿 → 流動資金** の輪が初めて閉じる。

**#36 が作った `BuyerDemand` / `BuyerBudget` は、本タスクで初めて呼ばれる。** [#37 に引き継がれた配線10件(A〜J)](https://github.com/stama72/visionary/issues/37)は、**いまテストが一度も踏んでいない**。本タスクのパイプライン統合テストがそれを構造的に踏む(下記「統合」節)。**ただし A(耐久の基礎値の母数)だけはパイプラインでは踏めない** — 耐久の約定が W2 では構造的に成立しないためで、A は単体で見る(下記 #29 と「耐久の約定は本タスクでは検証しない」)。**W2-05 側で配線が守られていることを前提にしてはならない。**

**含まない:**

- **都市外市場の候補合成と輸出**([#38](https://github.com/stama72/visionary/issues/38))。したがって **1次産品(穀物・木材・鉄鉱石・木炭)は誰も売っておらず、買えない**。生産の入力のうち買えるのは小麦粉と薪だけである
- **Need の生成**([#40](https://github.com/stama72/visionary/issues/40))。[GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md)「それでも0件のとき」の `在庫不足 / 遠方在庫` は #40。**本タスクは候補0件のとき何もしない**
- **破産中フラグの更新・②の投げ売り・④の職業付け替え**([#39](https://github.com/stama72/visionary/issues/39))。**本タスクが書くのはフラグの入力になる件数だけ**である(下記 3.)
- **信用による割引(効果1)と店の信用**(W4)。M0-W2 の **実効価格 = 提示価格**([GDD06 §6](../03-gdd/06-trade-and-negotiation.md) は M0 に入れるとしているが、W2 では `TrustLedger` が空で誰の信用も動かない)
- **ドメインイベント(`TradeExecuted` など)の発行。** 約定の真実は `LedgerEntry` であり([TDD01 §3.2](../04-tdd/01-sim-core-and-m0.md))、`DomainEvent` は種別コード + 汎用ペイロード1本の仮の形なので、品目・数量・単価の3つを載せられない。**M0 に購読者が居ない**(#41 のメトリクスは帳簿から数えられる)ので、正式な形は購読者が現れるタスクが決める
- **`OpportunityCost` をシステムとして順0 に登録すること。**[TDD01 §3.3](../04-tdd/01-sim-core-and-m0.md)「M0 の `OpportunityCost` はシステムを持たない」
- **`vsim` / Runner の追随。**[`SyntheticLoadSystem`](../../src/Visionary.Sim.Runner/Determinism/SyntheticLoadSystem.cs) のままにする(W2-03〜W2-05 と同じ理由 — #41 まで合成負荷が `StateHasher` の全区分を CI の2プロセス比較に踏ませている)

## 設計の前提(フェーズ1で決めたこと)

**実装の前に GDD/TDD を直してある(本ブランチの先行コミット)。実装はこの7点を仕様として読むこと。**

| どこ | 何を決めたか |
| ---- | ------------ |
| [GDD02 §8.1.1](../03-gdd/02-economy.md)「仕入れ移動平均単価の更新」 | **再帰形(指数移動平均)。丸めは最後に1回。更新するのは「生産の入力として買った」約定だけ**(暖房用に買った薪が焼成の原価に乗らないようにする)。窓付きを採らない理由も同節 |
| [GDD02 §6.2.1](../03-gdd/02-economy.md) | **買った品の行き先は用途で決まる**(必需・嗜好 → 世帯在庫、生産の入力・耐久 → 工房在庫)。**「資金不足で買えなかった」件数は世帯の状態として持つ**(帳簿には残らないため) |
| [GDD06 §2](../03-gdd/06-trade-and-negotiation.md) | **耐久の目標在庫は耐久値なので、移動費で割る前に個数へ直す。** 実効価格が1個あたりである以上、耐久値のまま割ると移動費が「1個あたりの耐久値」ぶん小さく出て、**空間の摩擦が耐久財についてだけ消える** |
| [GDD06 §3](../03-gdd/06-trade-and-negotiation.md) | **在庫の無い店は候補に入らない**(先に来た買い手が買い尽くす)。**選ぶ店は1つで、足りなくても次善の店へは回らない**(移動費が1回の来店を前提に割り戻されているため) |
| [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) | **「知っている店」は世帯主(親方)の観測で決める**(予算と同じ理由) |
| [TDD01 §3.3](../04-tdd/01-sim-core-and-m0.md)「順5 `Trade` の内部の段」 | **6段の順序。段4(需要の一括作成)を段5(買い物)の中へ畳まない** — 畳むと予算そのものが世帯 Id の走査順の関数になる |
| [TDD01 §3.2](../04-tdd/01-sim-core-and-m0.md) / [GDD01 §4.4](../03-gdd/01-trust-and-conversation.md) | **`LedgerEntry` は売買の向きを持つ**(1つの約定を双方が1行ずつ記帳する)。**数量の符号では表さない** |

## 作るもの

名前空間は `Visionary.Sim`(`Definition/` `World/`)と `Visionary.Sim.Systems`(`Systems/`)。

### 1. `WorldDefinition` の追加欄(`Definition/WorldDefinition.cs`)

**3つの欄をコンストラクタ引数として足す。既存引数の後ろに並べること。**

```csharp
/// <summary>
/// 機会費用の職業別の基準値。添字 = (int)Occupation。単位: 貨幣/1時間(GDD08 §9)。
/// </summary>
public int[] OpportunityCostBaseByOccupation { get; }

/// <summary>1区画あたりの移動時間。単位: 時間(GDD02 §4.3)。</summary>
public int TravelHoursPerDistrict { get; }

/// <summary>仕入れ移動平均単価の平滑化係数‰(GDD02 §8.1.1)。単位: ‰。</summary>
public int AcquisitionCostSmoothingPermille { get; }
```

**コンストラクタの前提検査**(既存の検査と同じ書き方で `ArgumentException` / `ArgumentOutOfRangeException` を投げる):

| 欄 | 検査 | 検査する理由 |
| -- | ---- | ------------ |
| `OpportunityCostBaseByOccupation` | 長さ = `Recipes.Length`、**全要素 1 以上** | [GDD02 §8.1.1](../03-gdd/02-economy.md) が「機会費用は…**必ず正**なので 0 にならず」に立って原価下限の消失を塞いでいる。0 を許すと移動費が全区画で 0 になり、[GDD08 §7.4](../03-gdd/08-household-and-decision.md)「信用インフレを止める唯一の絞り」が恒偽になる |
| `TravelHoursPerDistrict` | **1 以上** | 0 は R = 0 と同じ構造の破壊である([GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) の囲み)。実質コストの第2項が全区画で消える |
| `AcquisitionCostSmoothingPermille` | **1〜1000** | 0 は移動平均が初期値に凍る(仕入れても原価が動かない)。1000 超は `旧移動平均 × (1000 − β)` が負になり、**高値で仕入れるほど原価が下がる** |

**M0 の初期値**(`BuildM0`。単位のコメントを必ず付ける):

```csharp
// 添字 = (int)Occupation(Miller, Baker, Brewer, Woodworker, Smith)。単位: 貨幣/1時間。
// 目安は GDD08 §5.1 の「親方の1日粗利 ÷ 可処分時間12時間」だが、初期の提示価格が原価下限に
// 張り付く前提(GDD02 §8.2.7)で粗利を見積もると5職業とも 1 未満になり、階層係数200‰ を
// 掛けた徒弟も切り上げで 1 になって階層の差が消える。職業ごとに差が出る最小の水準まで
// 持ち上げてある(#28 の検算対象)。
var opportunityCostBaseByOccupation = new[] { 5, 6, 6, 4, 8 };

const int TravelHoursPerDistrictForM0 = 1;             // 時間/区画(GDD02 §4.3)
const int AcquisitionCostSmoothingPermilleForM0 = 250; // ‰。実効的な窓は7件程度(2/β − 1)
```

**5職業に別々の値を置くのは、添字の取り違えを落とすためである。** 全職業同値だと `Recipes[(int)Occupation]` と `[0]` の取り違え(#36 引き継ぎ表の J と同型)がどのテストにも現れない。

### 2. `LedgerEntry` に売買の向きを足す(`World/LedgerEntry.cs`)

```csharp
/// <summary>約定を記帳した側から見た向き(GDD01 §4.4 / TDD01 §3.2)。</summary>
public enum LedgerDirection
{
    /// <summary>買った(流動資金が減る)。</summary>
    Purchase = 0,

    /// <summary>売った(流動資金が増える)。</summary>
    Sale = 1,
}
```

`LedgerEntry` に `public LedgerDirection Direction { get; init; }` を足す。

- **`StateHasher` の `Ledgers` 節に `(int)entry.Direction` を足す。位置は `CreditDueAt` の後ろ**([TDD01 §3.8](../04-tdd/01-sim-core-and-m0.md)「既存の値を動かさず末尾へ足す」)
- **`StateHasherCoverageTests.ExpectedSectionElementMembers` の `LedgerEntry` の一覧を更新する。** 更新しないと赤になる — **それがこのテストの役目である**

### 3. `HouseholdState` に資金不足の件数を足す(`World/HouseholdState.cs`)

```csharp
/// <summary>
/// 当日、必需品を「資金不足で買えなかった」購入の件数(GDD02 §6.2.1 / §6.2.2)。0以上。
/// </summary>
/// <remarks>
/// <b>#39 の破産中フラグの入力である。</b>順3 <c>Household</c> が読む時点ではまだ前日の値で
/// あり(順5 <c>Trade</c> が上書きするのはその後)、GDD02 §6.2.2「前日の購入結果を評価する」が
/// 順序の帰結として成立する。<b>フラグそのものは本タスクでは立てない。</b>
/// </remarks>
public int UnaffordableNecessityCount { get; set; }
```

- **負を setter で拒む**(`ToolWearCount` と同じ書き方)
- **`StateHasher` の `Households` 節の末尾(`UnmetConsumption` の後ろ)に足す**
- **`StateHasherCoverageTests.ExpectedSectionElementMembers` の `HouseholdState` の一覧を更新する**

### 4. `OpportunityCost`(`Systems/OpportunityCost.cs` を新規作成)

```csharp
/// <summary>
/// 機会費用(GDD08 §5・§9)。<b>M0 は職業 × 階層の固定値なので、システムではなく純関数である</b>
/// (TDD01 §3.3「M0 の OpportunityCost はシステムを持たない」)。
/// </summary>
public static class OpportunityCost
{
    /// <summary>1人あたりの機会費用。単位: 貨幣/1時間(GDD08 §9)。</summary>
    public static int ForNpc(WorldDefinition definition, Occupation occupation, NpcRank rank);

    /// <summary>
    /// 買いに行く者の機会費用(GDD08 §7.2・§9)。世帯構成員のうち<b>最小</b>を採る。
    /// </summary>
    public static int ForErrand(WorldDefinition definition, World world, HouseholdState household);
}
```

- `ForNpc` = `IntegerMath.ApplyPermille(definition.OpportunityCostBaseByOccupation[(int)occupation], definition.RankCoefficientPermille[(int)rank])`
- `ForErrand` は `household.MemberNpcIds`(昇順が構築時に保証されている)を走査して **`<` で更新する**。**同値なら NpcId 最小が残る**([GDD08 §9](../03-gdd/08-household-and-decision.md) の M0 の単純化「手が空いているを判定しない」)
- **職業は世帯の現在の値を読む**(#39 の付け替えで変わる)。`ForErrand` の中で `household.Occupation` を引くこと
- **返すのは値だけで、誰が行くかは返さない。** W4 で店の信用(応対者)が要るようになったら NpcId も返す。**M0 で NpcId を返しても読む処理が無く、[TDD01 §3.2](../04-tdd/01-sim-core-and-m0.md) の「使わない状態を器に残さない」と同じ理由で足さない**

### 5. `StoreChoice`(`Systems/StoreChoice.cs` を新規作成)

```csharp
/// <summary>選ばれうる店1件(GDD06 §2・§3)。</summary>
public readonly record struct StoreCandidate
{
    /// <summary>売り手の世帯 Id。</summary>
    public int SellerId { get; init; }

    /// <summary>売り手の区画 Id(移動費と、訪れた区画の記録に使う)。</summary>
    public int DistrictId { get; init; }

    /// <summary>
    /// 実効価格。単位: 貨幣/1単位。<b>M0-W2 では提示価格そのものである</b> —
    /// 信用による割引(GDD01 §2.2 効果1)は W4。<b>支払いに使うのはこちらであって
    /// <see cref="UnitRealCost"/> ではない</b>(GDD02 §6.2.1)。
    /// </summary>
    public int UnitEffectivePrice { get; init; }

    /// <summary>実質コスト = 実効価格 + 1単位あたりの移動費(GDD06 §2)。単位: 貨幣/1単位。</summary>
    public int UnitRealCost { get; init; }
}

/// <summary>
/// 店の候補集合と実質コストと選択(GDD06 §2・§3・§3.1)。<b>World を読むだけで書かない。</b>
/// </summary>
public sealed class StoreChoice
{
    public StoreChoice(WorldDefinition definition);

    /// <summary>移動時間。単位: 時間。距離 × 1区画あたりの移動時間 × 2(<b>往復</b>、GDD02 §4.3)。</summary>
    public static int TravelHours(int fromDistrictId, int toDistrictId, int hoursPerDistrict);

    /// <summary>
    /// 1単位あたりの移動費(GDD06 §2)。
    /// <c>CeilDiv(移動時間 × 機会費用, max(目標在庫, 1))</c>。
    /// </summary>
    /// <param name="targetStockInUnits">
    /// <b>個数に直した目標在庫</b>。耐久の行は耐久値で数えているので、呼び出し側が
    /// <c>CeilDiv(TargetStock, 1個あたりの耐久値)</c> してから渡すこと(GDD06 §2)。
    /// </param>
    public static int TravelCostPerUnit(int travelHours, int opportunityCost, int targetStockInUnits);

    /// <summary>
    /// 知っている店のうち実質コストが最小のものを選ぶ(GDD06 §3)。候補0件なら false。
    /// </summary>
    public bool TrySelect(
        World world,
        HouseholdState buyer,
        int itemId,
        int targetStockInUnits,
        int errandOpportunityCost,
        out StoreCandidate selected);
}
```

**`TrySelect` の候補の条件(すべて満たすものだけ)**:

| # | 条件 | 落とすと何が起きるか |
| - | ---- | -------------------- |
| 1 | `world.Market` に `(itemId, sellerId)` のエントリがある | 売っていない店から買う |
| 2 | `sellerId != buyer.Id` | **自分から自分へ売買し、資金が動かないまま在庫と帳簿だけが増える** |
| 3 | `world.Households[sellerId].WorkshopInventory[itemId] > 0` | 先に来た買い手が買い尽くした店を選び、その日は何も買えずに終わる([GDD06 §3](../03-gdd/06-trade-and-negotiation.md)) |
| 4a | `District.Distance(buyer.DistrictId, 売り手の区画) <= District.VisionRadius`(**今日の知覚**) | — |
| 4b | または `world.Knowledge[buyer.HeadNpcId]` に `ItemId` と `SellerId` が一致し、**`world.Now.DayIndex − ObservedAt.DayIndex >= 1`** の観測が1件以上ある(**有効な記憶**) | 4a だけにすると知識が永久に自宅の R 以内に閉じる |

- **走査は `world.Market` の列挙順そのまま**(`SortedDictionary`、`MarketKey` は ItemId → SellerId の昇順)。**`<` で更新する** — 同値なら売り手 Id 最小が残る([ADR-0002](../adr/0002-time-model-and-determinism.md) / [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md))
- **4b で保持期間を見ない。** 段3 の `Observations.Expire` が保持期間を過ぎた観測を先に消しているので、ここに残っているものはすべて期間内である。**二重に書くと、片方だけ直したときに境界がずれる**([`Observations.Expire`](../../src/Visionary.Sim/Systems/Observations.cs) が `TryMarketReference` と境界を揃えている理由と同じ)
- **4b の「差 ≥ 1」は書く。** 段6(観測の生成)が段5 の後ろにある以上、当日の観測は実際には存在しない。**それでも明示するのは、段の順序という別の事実に規範を依存させないためである** — 段を入れ替えた瞬間に [GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) が切った日内の相互参照が静かに戻る。**テスト #12 が当日の観測を直接注入してここを踏む**
- **実効価格は `Market` の当日の提示価格である**([GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md)「∪ の重複は現在の売り注文を採る」)。**記憶に入っている過去の価格ではない**

### 6. `TradeSettlement`(`Systems/TradeSettlement.cs` を新規作成)

```csharp
/// <summary>
/// 約定の適用(GDD02 §6.2・§6.2.1 / TDD01 §3.2)。<b>在庫・流動資金・帳簿を動かす唯一の場所。</b>
/// </summary>
public static class TradeSettlement
{
    /// <summary>
    /// 資金上限(GDD02 §6.2.1)。<c>FloorDiv(流動資金, 実効価格)</c>。<b>切り下げる</b> —
    /// 切り上げると1単位多く買えて流動資金が負に落ちる。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="unitEffectivePrice"/> が0以下。</exception>
    public static int FundsCap(int liquidFunds, int unitEffectivePrice);

    /// <summary>1件の約定を適用する。</summary>
    public static void Execute(
        World world,
        HouseholdState buyer,
        HouseholdState seller,
        DemandPurpose purpose,
        int itemId,
        int quantity,
        int unitEffectivePrice,
        int acquisitionCostSmoothingPermille);
}
```

**`Execute` の後条件**(この順に書く):

1. `支払額 = (long)quantity × unitEffectivePrice`。**`buyer.LiquidFunds -= 支払額` / `seller.LiquidFunds += 支払額`**
2. **`seller.WorkshopInventory[itemId] -= quantity`**(販売在庫は工房在庫である。[GDD02 §8.1.1](../03-gdd/02-economy.md))
3. **買い手の在庫は用途で行き先が変わる**([GDD02 §6.2.1](../03-gdd/02-economy.md)):`Necessity` / `Preference` → `HouseholdInventory[itemId]`、`ProductionInput` / `Durable` → `WorkshopInventory[itemId]`
4. **帳簿は双方に1行ずつ。** 買い手に `Direction = Purchase, CounterpartyId = seller.Id`、売り手に `Direction = Sale, CounterpartyId = buyer.Id`。`ItemId` / `Quantity` / `UnitPrice`(= `unitEffectivePrice`)/ `OccurredAt = world.Now` / `Terms = Cash` は同じ。**`CreditDueAt` は書かない**(`Cash` のとき未使用、[LedgerEntry](../../src/Visionary.Sim/World/LedgerEntry.cs))
5. **`purpose == DemandPurpose.ProductionInput` のときだけ** `buyer.PurchaseUnitCostAverage[itemId] = OfferPrice.UpdatedAcquisitionCost(…)`([GDD02 §8.1.1](../03-gdd/02-economy.md))

- **`quantity` が0以下、または `unitEffectivePrice` が0以下なら `ArgumentOutOfRangeException`。** 0個の約定を記帳すると帳簿に意味の無い行が増え、`StateHasher` にも乗る
- **支払額は `long` で積んでから `checked((int)…)` で引く。** `quantity × unitEffectivePrice` は int を容易に超える

### 7. `OfferPrice.UpdatedAcquisitionCost`(`Systems/OfferPrice.cs` に追加)

```csharp
/// <summary>
/// 仕入れ移動平均単価の更新(GDD02 §8.1.1「仕入れ移動平均単価の更新」)。
/// <c>CeilDiv(旧移動平均 × (1000 − β‰) + 約定単価 × β‰, 1000)</c>。
/// </summary>
/// <remarks>
/// <b>丸めは最後に1回だけ掛ける。</b>2項をそれぞれ <see cref="IntegerMath.ApplyPermille"/> で
/// 丸めてから足すと両方が切り上がり、<b>価格が動いていない日でも移動平均が1ずつ上がり続ける</b>。
/// </remarks>
public static int UpdatedAcquisitionCost(int previousAverage, int unitPrice, int smoothingPermille);
```

**`OfferPrice` に置くのは、これが §8.1.1 の原価の入力だからである**(`UnitCost` が読む欄を作る側)。

### 8. `TradeSystem.Step` の段構成(`Systems/TradeSystem.cs`)

**[TDD01 §3.3](../04-tdd/01-sim-core-and-m0.md)「順5 `Trade` の内部の段」の6段に組み替える。** 現在の実装は段1・2・3・6 を持っているので、**段4・5 を段3 と段6 のあいだに挿し、段1 で前日の提示価格を控える。**

```
段1  全売り手の新しい提示価格を求める(Market は読むだけ)
     ★ 追加: 世帯ごとの「前日の自分の出力品目の提示価格」を控える
         → 既に hasOwn / ownPreviousPrice として引いている値そのもの。配列に積むだけ
         → World の状態にしない(1日限りの中間値。#36 引き継ぎ)
         → 販売在庫0で売り注文を出さない世帯についても控えること
            (その世帯も買い手としては需要を持つ)
段2  Market.Clear() → 一括書き込み(現状のまま)
段3  Observations.Expire(現状のまま)
段4  全世帯ぶんの HouseholdDemand を作る
         demands[householdId] = _buyerDemand.Build(world, household, hasOwn[i], ownPreviousPrice[i])
         ★ 段5 の中へ畳まない(TDD01 §3.3)
段5  世帯 Id 昇順に買い物
段6  Observations.CollectAndShare(world, household, 段5 で控えた訪問区画)
```

**段5 の1世帯ぶんの手順**:

```
household.UnaffordableNecessityCount = 0      ← 毎日上書きする
    (ConsumptionSystem が UnmetConsumption を毎日上書きするのと同じ)
errand = OpportunityCost.ForErrand(definition, world, household)

demands[householdId].Lines を並び順そのままに走査する
    ← 並びが GDD02 §6.2.1 の走査順(必需 → 生産の入力 → 耐久 → 嗜好、同一用途は品目 Id 昇順)
      そのものである(#36 引き継ぎ「並べ直さないこと」)

各 line について:
  1. 目標在庫を個数へ直す(移動費の分母。GDD06 §2)
         Durable  : targetInUnits = CeilDiv(line.TargetStock, definition.ProductionRunsPerToolWear)
         それ以外 : targetInUnits = line.TargetStock
  2. _storeChoice.TrySelect(world, household, line.ItemId, targetInUnits, errand, out store)
         false(知っている店が0件 / 在庫のある店が0件)なら、この line は終わり
         ← Need は立てない(#40)
  3. store.UnitRealCost > line.Budget なら買わない(GDD06 §3 の条件②)
  4. 購入量(用途の単位)= BuyerBudget.PurchaseQuantity(
         line.BaseValue, store.UnitRealCost, line.TargetStock, line.ExpectedStock)
         ← 実質コストを渡す。実効価格ではない(GDD02 §8.2.3)
  5. 個数へ直す
         Durable  : CeilDiv(購入量, definition.ProductionRunsPerToolWear)(GDD02 §8.2.1)
         それ以外 : そのまま
  6. 0 以下なら、この line は終わり(店は選んだが買う量が0。訪問にも数えない)
  7. 訪れた区画に store.DistrictId を控える(重複は積まない)
         ← 6 を通った時点で控える。8 の切り詰めで0個になっても「行った」ことは変わらない
  8. fundsCap = TradeSettlement.FundsCap(household.LiquidFunds, store.UnitEffectivePrice)
     買える数量 = min(個数, fundsCap, 売り手の販売在庫)
  9. line.Purpose == Necessity かつ fundsCap == 0 なら household.UnaffordableNecessityCount++
         ← 「資金不足で買えなかった」は資金の項だけを見る(GDD02 §6.2.1)。
           売り手の在庫が尽きて0個になったのは資金不足ではない
 10. 買える数量 >= 1 なら TradeSettlement.Execute(…)
```

- **手順1 と手順5 の換算は、名前付きの静的ヘルパー2つへ切り出す**(レビュー1巡目 I-b の訂正)。**用途による分岐もヘルパーの中へ入れる。** 段5 の本文に埋めると、耐久の約定が W2 では起きない以上どこからも踏めず、テスト #6 が判別力を持てない(上記「落ちるべき条件」の囲み)。名前は `TargetStockInUnits(purpose, targetStock, runsPerToolWear)` / `PurchaseQuantityInUnits(purpose, quantity, runsPerToolWear)` のように、**耐久値と個数のどちらを返すかが名前で分かる**こと
- **`Market` を書くのは段2 だけである。** 約定は在庫・資金・帳簿を動かすが `Market` には触らない(現状の doc コメントの約束を守る)
- **販売在庫は約定のたびに減るので、`TrySelect` は毎回 `world` を読み直す。** 候補を1日1回作って使い回さない — 使い回すと売り切れた店を選ぶ
- **`BuyerDemand` / `StoreChoice` は `Step` のたびに作らず、コンストラクタで1つ持つ**(`_definition` と同じ)
- **段5 と段6 は別のループである。** 段6 を段5 の世帯ループに畳むと、**先に買った世帯の観測が、後の世帯の買い物より前に生まれる**([GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) の「記憶は前日まで」は当日生成の観測が誰にも読まれないことに立っている)

## 落ちるべき条件(テスト)

**この節が完了条件そのもの。** 目標は「通ること」ではなく「**壊したときに落ちること**」である。

### 単体(式)

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心 |
| - | ------ | -------- | -------------------- | ---- |
| 1 | `OpportunityCostAppliesTheRankCoefficient` | 基準値5・親方(1000‰)→ **5**、徒弟(200‰)→ **1**。基準値4・徒弟 → **1**(切り上げ) | 階層係数を掛けない(全階層が基準値になり、`ForErrand` が常に同値になる)。`ApplyPermille` を `FloorDiv` にする(基準値4の徒弟が **0** になり、移動費が消える) | |
| 2 | `ErrandOpportunityCostTakesTheSmallestMember` | 親方(1000‰)と徒弟(200‰)の2人世帯・基準値5 → **1**。全員同値なら NpcId 最小のもの | `Max` を取る / 世帯主で固定する(**徒弟に行かせても実質コストが下がらず、GDD06 §3「機会費用の低い者を使うと選択肢が広がる」が恒偽になる**) | **核心** |
| 3 | `ErrandOpportunityCostReadsTheHouseholdOccupation` | 同じ構成員で `Occupation` だけ Miller(基準値5)→ Smith(基準値8)に変えると **1 → 2** | 職業を定数で引く(#36 引き継ぎ表 J と同型。**#39 の職業付け替え後に機会費用が追随しない**) | |
| 4 | `TravelHoursCountsTheRoundTrip` | 区画0→区画8(距離4)・1時間/区画 → **8**。同一区画 → **0** | `× 2` を落とす(片道だけ数え、**空間の摩擦が半分になる**) | |
| 5 | `TravelCostIsDividedByTheTargetStock` | 移動時間8・機会費用3・目標在庫5 → **CeilDiv(24,5) = 5**。目標在庫**0** → **24**(零除算しない) | `max(目標在庫, 1)` を落とす(`DivideByZeroException`。**入力切れで停止した工房が入力を買い直せず永久に止まる**、GDD06 §2)。割らずに1回あたりの値を返す | **核心** |
| 6 | `DurableTravelCostConvertsTheTargetStockToUnits` | **段5 の換算そのものを呼ぶ**(下記の囲み)。用途 `Durable`・目標在庫 **15(耐久値)**・N=30 → 個数 **1**、用途 `Necessity`・目標在庫15 → **15**(換算しない)。購入量の換算(手順5)も同じテストで踏む: `Durable`・購入量15 → **1**、`Necessity`・購入量15 → **15** | 個数へ直さずに `line.TargetStock` を渡す(**移動費が 1/N になり、空間の摩擦が耐久財についてだけ消える**。GDD06 §2)。用途を見ずに常に換算する(**必需の移動費が 1/N になる**) | **核心** |
| 7 | `FundsCapRoundsDown` | 流動資金100・実効価格30 → **3**。流動資金29・実効価格30 → **0** | `CeilDiv` にする(4個買えることになり**流動資金が負に落ちる**)。実質コストで割る(**移動費ぶん買える数が減る**。移動費は貨幣として支払わない、GDD02 §6.2.1) | **核心** |
| 8 | `FundsCapRejectsNonPositivePrice` | 実効価格 0 / −1 で `ArgumentOutOfRangeException` | 0 を通す(`FloorDiv` が `DivideByZeroException`) | |
| 9 | `UpdatedAcquisitionCostRoundsOnlyOnce` | 旧20・単価20・β250‰ → **20**(動かない)。旧20・単価40・β250‰ → **25** | 2項を別々に `ApplyPermille` して足す(前者が **21** になり、**価格が動いていない日でも原価が上がり続ける**)。β を逆向きに掛ける(新しい単価に `1000 − β` を掛ける) | **核心** |

> **#6 は `TravelCostPerUnit` を直接呼んではならない(レビュー1巡目 I-b の訂正)。** 換算をテスト側で手で書き、`TravelCostPerUnit(8, 3, CeilDiv(15, 30))` と `TravelCostPerUnit(8, 3, 15)` を比べる形は、**`CeilDiv(15, 30) == 1` という恒真命題を確かめているだけ**で、段5 が実際に何を渡しているかを見ていない。狙った変異(段5 が `line.TargetStock` をそのまま渡す)を当てても、テストが呼ぶ2つの値は動かない。**耐久の約定は W2 では構造的に起きない**(下記「耐久の約定は本タスクでは検証しない」)ので、パイプラインからも踏めない。
>
> **したがって段5 の手順1・手順5 の換算を `TradeSystem` の名前付き静的ヘルパー2つへ切り出し、#6 はそれを直接呼ぶこと**(下記「8.」の該当項)。**用途による分岐をヘルパーの中に入れる** — 分岐が段5 の本文に残ると、ヘルパーを呼んでも分岐は踏めない。

### 単体(店の選択)

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心 |
| -- | ------ | -------- | -------------------- | ---- |
| 10 | `SelectsTheCheapestRealCostNotTheCheapestPrice` | 近い店(距離0・提示価格 **30**)と遠い店(距離2・提示価格 **26**、移動費が5)→ **近い店** | 実質コストではなく提示価格で `argmin` する(**GDD06 §3 の帰結表の「遠い店は不利」が丸ごと消える**) | **核心** |
| 11 | `SelectsTheSmallestSellerIdOnTies` | 実質コストが同値の売り手2件 → **Id の小さい方** | `<=` で更新する(Id 最大が残る。[ADR-0002](../adr/0002-time-model-and-determinism.md) 違反。**#38 の都市外市場が同値のとき都市内より優先される**) | |
| 12 | `SeesDistantStoresOnlyThroughYesterdaysObservation` | 距離2の売り手のみ → **候補0件**。**前日**の観測を1件置くと候補に入る。**当日(差0)の観測では入らない** | 4b の「差 ≥ 1」を落とす(**日内の相互参照が戻る**、GDD06 §3.1)。4b ごと落とす(**知識が永久に自宅の R 以内に閉じる**) | **核心** |
| 13 | `IgnoresSellersWithoutSellableStock` | `Market` にエントリはあるが `WorkshopInventory[itemId] == 0` の売り手は候補に入らない。距離0のその店と距離2(観測あり)の在庫ありの店なら**後者**が選ばれる | 在庫を見ない(**売り切れた店を選んで0個買い、その日は何も買えない**。GDD06 §3) | |
| 14 | `IgnoresOwnOffer` | 自分が売っている品目について、自分は候補に入らない | `sellerId != buyer.Id` を落とす(**自分から自分へ売買し、帳簿と在庫だけが増える**) | |
| 15 | `UsesTheHeadObservationsForMemory` | 観測を**世帯主以外の構成員にだけ**置く → 候補に入らない。世帯主に置くと入る。**世帯 Id ≠ 世帯主 NpcId の世帯を使うこと**(M0 と同じく `HeadNpcId = 世帯Id × 2` など。レビュー1巡目 I-b の訂正 — `Id == HeadNpcId` の世帯では2つの添字が同じ配列を指し、**変異を素通りさせる**) | `Knowledge[household.Id]` で引く(#36 引き継ぎ表 B と同型。**型が止めない取り違え**、TDD01 §3.2) | |

### 単体(約定)

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心 |
| -- | ------ | -------- | -------------------- | ---- |
| 16 | `ExecuteMovesFundsInventoryAndBothLedgers` | 数量2・単価30 → 買い手 **−60** / 売り手 **+60**、売り手の工房在庫 **−2**、帳簿が**双方に1行ずつ**(`Purchase` と `Sale`、`CounterpartyId` が相手) | 片側だけ記帳する(**売り手の帳簿と流動資金が突合しない**)。`Direction` を両方 `Purchase` にする(**帳簿から資金を復元すると符号が逆になる**) | **核心** |
| 17 | `ExecuteRoutesTheGoodsByPurpose` | `Necessity` で買った薪 → **世帯在庫**、`ProductionInput` で買った薪 → **工房在庫** | 品目で振り分ける / 常に片方へ入れる(**暖房用の薪が工房在庫に入り、生産用の在庫圧力が下がって仕入が止まる**。GDD02 §6.2.1) | **核心** |
| 18 | `ExecuteUpdatesTheAcquisitionCostOnlyForProductionInput` | `ProductionInput` の約定 → `PurchaseUnitCostAverage[薪]` が動く。**`Necessity` の約定では動かない** | 用途を見ずに更新する(**暖房用に高値で買った薪が焼成の原価に乗り、提示価格が跳ねる**。GDD02 §8.1.1) | **核心** |
| 19 | `ExecuteRejectsEmptyTrades` | 数量 0 / 負、単価 0 / 負で `ArgumentOutOfRangeException` | 通す(**0個の行が帳簿に積まれ、`StateHasher` にも乗る**) | |

### 統合(パイプライン。`Systems/TradePipelineTests.cs` を新規作成)

**世界は `WorldGenerator.Generate(WorldDefinition.M0, …)` で作り、`ProductionSystem` / `ConsumptionSystem` / `TradeSystem` を登録して複数日回す**([`ProductionAndConsumptionPipelineTests`](../../tests/Visionary.Sim.Tests/Systems/ProductionAndConsumptionPipelineTests.cs) と同じ形)。

**手組みの縮退した世界を使ってはならない。** #36 から引き継いだ配線10件は、テスト世界の縮退3つ(世帯 Id = 世帯主 NpcId = 職業添字 / 必要運転資金0 / 季節係数が一様)に守られて通り抜けた。**M0 の世界はその3つをすべて壊している** — 世帯主 NpcId は世帯 Id × 2、職業は区画制約で振られる、必要運転資金は正、薪の季節係数は 800/400/1000/2000。

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心 |
| -- | ------ | -------- | -------------------- | ---- |
| 20 | `LedgersReconcileWithLiquidFundsForEveryHousehold` | 10日回した後、**全世帯**について `初期資金 + Σ(Sale の額) − Σ(Purchase の額) == LiquidFunds` | 支払いと記帳がずれるあらゆる経路(片側記帳・数量の取り違え・実質コストで支払う)。**issue #37 の閉じる条件そのもの** | **核心** |
| 21 | `TradeNeitherCreatesNorDestroysGoods` | 約定1日ぶんの前後で、各品目の **(全世帯の世帯在庫 + 工房在庫)の合計が変わらない**(生産・消費を登録せず `TradeSystem` だけで回す) | 売り手の在庫を減らし忘れる(**無から財が湧く**)。買い手の在庫を2本とも増やす | |
| 22 | `PreferenceIsActuallyBought` | 30日回した後、**ビール(嗜好)の約定が1件以上**ある | #36 引き継ぎ表 **C**(嗜好の目標在庫が0 → ビールが一度も買われない)、**D**(嗜好の予想在庫が工房在庫) | **核心** |
| 23 | `KnowledgeSpreadsBeyondTheHomeVisionRadius` | 30日回した後、**自区画から距離2以上の売り手の観測を持つ世帯が1つ以上ある** | 段6 に訪問区画を渡さない(**知識が永久に自宅の R 以内に閉じる**。#36 引き継ぎ表 **H** と GDD06 §3.1) | **核心** |
| 24 | `ObservationsDoNotGrowWithoutBound` | 60日回した後、`Knowledge` の総件数が **売り注文数 × (保持期間 + 1) × 構成員数** 以下 | 段3 の `Expire` に `int.MaxValue` を渡す(#36 引き継ぎ表 **G**。**`StateHasher` の対象が単調増加する**) | |
| 25 | `NecessityIsSettledBeforePreference` | 必需だけが買える資金しか持たない世帯を作り(初期資金を絞った定義で1日回す)、**必需の約定が成立し、嗜好の約定が0件**である。**あわせて「資金さえあれば嗜好は買える」ことを対照で確かめる** — 同じ世帯の初期資金だけを増やした世界で**嗜好の約定が成立する**こと(レビュー1巡目 I-b の訂正。これが無いと嗜好が手順3(予算 < 実質コスト)で**順序と無関係に**落ち、並べ替え変異を素通りさせる) | `Lines` を並べ替える / 用途でソートし直す(**「資金不足で必需品が買えなかった」が買い物の順序の関数になり、困窮の指標として読めなくなる**。GDD02 §6.2.1)。**ただし W2 ではこの変異は落ちない**(下記) | **核心** |
| 26 | `UnaffordableNecessityCountsOnlyTheFundsShortfall` | 流動資金0の世帯 → 必需の行で **`UnaffordableNecessityCount >= 1`**。**売り手の在庫が0で買えなかっただけの世帯 → 0 のまま**。**嗜好が買えなくても 0 のまま**。翌日に買えたら **0 に戻る** | 在庫切れも数える(**#39 のフラグが品切れの検出器になる**。GDD02 §6.2.2)。用途を見ずに数える。毎日0にし忘れる(**一度立つと二度と降りない**) | **核心** |
| 27 | `TradeIsDeterministicAcrossRuns` | 同じシードで2つの世界を30日回し、`StateHasher` の値が一致する | `Dictionary` の列挙順に依存する / 世帯の走査順を崩す | |
| 28 | `SpatialFrictionSurvives` | 30日回した後、**同一品目の約定単価の分布が区画によって一致しない**(区画間の価格差が消えていない) | 店の選択から区画の別が一切効かなくなる経路(実質コストの argmin をやめる / 候補を全買い手で共通にする)。**値の調整(#28)で揺れうるので閾値を置かず、「一致しない」ことだけを見る**。**「移動費を実質コストに乗せ忘れる」は本テストでは落ちない**(下記) | |

> **#25 は「`Lines` を逆順に走査する」変異を落とさない(レビュー1巡目の実測)。判別力を持つ資金帯が W2 には存在しない。** 必需の基礎値は**流動資金**に比率を掛けるが([GDD02 §8.2.7](../03-gdd/02-economy.md) のフォールバック)、嗜好の基礎値は **`SurplusFunds = max(0, 流動資金 − 必要運転資金)`** に掛ける([GDD02 §8.2.1](../03-gdd/02-economy.md))。世帯の必要運転資金が大きいため、**嗜好が手順3 を通過できる資金水準(実測 約580)に達した時点で、必需の1日あたりの必要額(冬季でも約33)は無視できるほど小さい。** 逆に必需が資金不足に落ちるまで絞ると(50〜200)、嗜好は手順3 で必ず落ちて手順8 に到達せず、資金を奪い合わない。**両者の帯が重ならないので、走査順を入れ替えても観測可能な結果が動かない。**
>
> 探索範囲(seed 1・世帯 Id 0): 資金 50〜1200 を粗く、300〜620 を5刻み、50〜300 を10刻み × 秋/冬 × 1・2・5日目 × 正順/逆順。**`UnaffordableNecessityCount`・嗜好の約定の有無・購入後の流動資金がいずれも1桁まで一致した。** 薪の冬の季節係数 2000‰ は必需側の必要額を約16 → 約33 に動かすだけで、約17倍の差を埋めない。
>
> **走査順そのものは実装が守っている**(段5 は `Lines` を並べ替えていない)。**守られていないのは「テストで守られている」という保証のほうである。** 検出器の置き場所(`TradeSystem` の段5 を狭く踏む単体テストを起こすか、#39 が困窮の指標を実際に読むときに併せて立てるか)は本タスクのスコープ外なので **issue へ落とす**。

> **#28 は「移動費を実質コストに乗せ忘れる」変異を落とさない(レビュー1巡目の実測)。** W2 の区画間の価格差は移動費だけから生じるのではなく、**「どの区画の買い手がどの店を知っているか」(記憶の局所性)からも生じる**。そのため実質コストから移動費を落としても、不一致が同じ値で残る(seed 1・30日で実測)。**この変異の検出器は単体 #10 である。** #28 が主張できるのは「統合系で区画間の価格差が実際に立っている」という**存在命題まで**であり、その原因の分解ではない。**#38(都市外市場)で候補集合の構造が変わればこの交絡も変わるので、そのとき #28 の判別力を測り直すこと。**

**テスト #22・#23・#28 は「何日回すか」に依存する。** 日数は仕様ではなく、実装が緑にできる最小の日数でよい。**ただし30日回しても1件も買われないなら、それは実装ミスではなく値の問題(#28)である可能性がある** — その場合は直さずに**止まって報告する**(仕様の穴として数える)。

#### 耐久(工具)の約定は本タスクでは検証しない(フェーズ1 の裁定)

**旧 #22 は「工具の約定が1件以上」も求めていた。これは W2 では満たせない。値の問題ではない。**

**工具の摩耗は生産回数でしか進まない**(`ToolWearCount += runs`。1生産 = 1摩耗、[GDD02 §5.3](../03-gdd/02-economy.md))。ところが **W2 では1次産品(穀物・木材・鉄鉱石・木炭)に売り手が居ない**(都市外市場は #38。本仕様「スコープ」)ので、**生産は初期入力5日分の5回で止まり、摩耗もそこで止まる**。耐久の需要が立つ(在庫圧力が 1000‰ になる)には `予想在庫 ≤ 目標在庫`、すなわち**摩耗15回**が要る。**W2 が供給できる摩耗は5回である。**

計測(`TradeSystem` に一時プローブを入れ、シード1で実測。いずれも世帯9。1行目は最終日 day29、2行目は day149 の値):

| 耐久の予算比率‰ | 日数 | 予想在庫 | 目標在庫 | 在庫圧力 | 基礎値 | 予算 | 実質コスト | 工具の約定 |
| ---------------- | ---- | -------- | -------- | -------- | ------ | ---- | ---------- | ---------- |
| 10‰(M0 の初期値) | 30 | 25 | 15 | 334‰ | 3 | 2 | 48 | **0件** |
| 1000‰(実験) | 150 | **25(day5 から凍結)** | 15 | 334‰ | 48 | 17 | 48 | **0件** |

**予算比率を流動資金の100%まで上げ、日数を5倍にしても約定は0件である。** したがって「値の問題(#28)」という読みは棄却される。**同時に「配線の問題(#36 引き継ぎ表 A)」も棄却される** — プローブで `基礎値 == ApplyPermille(流動資金, 比率‰)` が毎日一致しており、母数は [GDD02 §8.2.1](../03-gdd/02-economy.md) どおり流動資金である。

**帰結として、A はパイプラインでは原理的に検出できない。** 母数を `surplus` に取り違えても約定は0件のままで、観測される挙動が変わらない。**A の検出器は単体へ移す**(下記 #29)。**「工具が実際に買われる」ことの検証は #38 の Exit Criteria へ移す**(1次産品が買えるようになれば生産が続き、摩耗が進む。[GDD02 §5.3 の注](../03-gdd/02-economy.md)「都市外市場が鉄鉱石と木炭を常に売る」がその前提を持つ)。

### 単体(需要の配線。`Systems/BuyerDemandTests.cs` に追加)

| # | テスト | 検証内容 | この実装ミスで落ちる | 核心 |
| -- | ------ | -------- | -------------------- | ---- |
| 29 | `DurableBaseValueIsMeasuredOnLiquidFundsNotSurplus` | **必要運転資金 > 0 の世帯**で耐久の行の `BaseValue == BuyerBudget.DurableBaseValue(…, household.LiquidFunds, 耐久の予算比率‰)`。**同じテストの中で `demand.SurplusFunds < household.LiquidFunds` を先に確かめる** | #36 引き継ぎ表 **A**(`household.LiquidFunds` → `surplus`。運転資金で余剰が0に潰れた世帯の耐久の基礎値が恒久0 → **工具を買えず設備係数0‰ → 恒久停止**) | **核心** |

**`demand.SurplusFunds < household.LiquidFunds` の確認を省かないこと。この1行が判別力そのものである。** 既存の `DurableAndPreferenceReadTheirOwnBudgetRatio` は必要運転資金0の世界で同じ欄を assert しており、`surplus == LiquidFunds` になるので **A の変異を素通りさせる**(#36 引き継ぎ「根の構造」の縮退2)。**縮退していない世帯を用意すること** — [`TradePipelineTests`](../../tests/Visionary.Sim.Tests/Systems/TradePipelineTests.cs) と同じく `WorldGenerator.Generate(WorldDefinition.M0, …)` で作れば必要運転資金は正になる。

## 先のタスクへ

### #38(都市外市場)へ

- **候補の合成は `StoreChoice.TrySelect` の中に足す。** `world.Market` の走査の後ろに、中心区画(`District.ExternalMarketDistrictId`)の窓口を1件足す形になる。**売り手 Id は `HouseholdState.ExternalMarketSellerId`(`int.MaxValue`)なので、`world.Households[sellerId]` を引く経路を通らないこと** — 条件3(販売在庫 > 0)と `DistrictId` の取得がそれに当たる。**無限在庫なので在庫の条件も適用しない**
- **`TradeSettlement.Execute` も同じ経路を通る。** 相手世帯が存在しないので、**資金・在庫・帳簿を動かすのは買い手側だけ**になる。`Execute` を分岐させるか別メソッドにするかは #38 の判断
- **1次産品が買えるようになるまで、派生需要はフォールバックに張り付く**(#36 引き継ぎ)。本タスクの統合テストが見ている挙動は #38 の後で大きく変わる
- **「工具(耐久)の約定が1件以上ある」を #38 の Exit Criteria へ引き取る**(本仕様「耐久の約定は本タスクでは検証しない」の裁定)。**W2 では1次産品が買えず生産が5日で止まるので摩耗が進まず、耐久の需要が構造的に立たない。** 1次産品が買えるようになれば生産が続き、摩耗が目標在庫を割って初めて `min` の耐久の予算([GDD02 §8.2.1](../03-gdd/02-economy.md))が実地で回る。**そのとき初めて、耐久の予算比率‰ が実際に効く値かどうかも測れる**(#28)

### #39(Household)へ

- **`UnaffordableNecessityCount` は本タスクが毎日上書きする。** 順3 が読む時点ではまだ前日の値である。**フラグを立てるときに 0 に戻さないこと** — 戻すと同じ日に順5 がまた書くので二重になる
- **②の投げ売りは #35 の値付けの分岐(段1)である。** 段5 ではない

### #40(NeedGeneration)へ

- **「知っている店が0件」は `StoreChoice.TrySelect` が false を返した状態である。** ただし**順4 `NeedGeneration` は順5 `Trade` より前にある**ので、当日の判定結果をその日の Need にはできない。[GDD06 §3.1](../03-gdd/06-trade-and-negotiation.md) の `在庫不足 / 遠方在庫` をどう立てるかは #40 が決める(**前日の結果を読むか、順4 で候補集合だけを先に評価するか**)

### #41(Metrics)へ

- **約定の真実は `Ledgers` にしか無い**(ドメインイベントを発行しないため)。メトリクスは帳簿を数える。**`Direction` で二重計上を避けること** — 1つの約定が買い手と売り手の2行になっている

### #28(値の検算と調整)へ

- **ここが初出の初期値は2つである** — `OpportunityCostBaseByOccupation = {5,6,6,4,8}` と `AcquisitionCostSmoothingPermille = 250`(`TravelHoursPerDistrict = 1` は [GDD02 §4.3](../03-gdd/02-economy.md) が値を持つ)
- **機会費用の基準値は「粗利 ÷ 可処分時間」から出していない。** 出すと5職業とも1未満になり、階層係数を掛けた徒弟が切り上げで1に潰れて階層の差が消える。**いまの値は「階層の差が移動費に現れる最小の水準」であって、GDD08 §5.1 の式の近似ではない**
- **β = 250‰ は実効的な窓7件に相当する**(`2/β − 1`)。[GDD02 §13.2](../03-gdd/02-economy.md) の「原価の移動平均の平滑化係数‰」がこの軸である
- **耐久の予算比率‰(初期値10‰)は M0 の貨幣規模に対して桁が合っていない(実測)。** [GDD02 §8.2.1](../03-gdd/02-economy.md) の基礎値は**単価の上限**として効く(`実質コスト > 基礎値 → 購入量0`)ので、10‰ は「**工具を1個買うには流動資金が工具価格の100倍要る**」という意味になる。M0 の初期流動資金200・工具の実効価格48 では最低でも240‰ が要る。**`min` の相場基準側には必需の `許容乖離‰`(1200‰)に当たる余裕が無く、実質コスト = 提示価格 + 移動費 は相場基準を構造的に上回る**ので、比率を上げても `予算 = 基礎値 × 在庫圧力‰` が実質コストに届くのは在庫圧力が 1000‰ に近いとき(= 摩耗が目標在庫を割ったとき)だけである。**値の当否を測れるのは #38 の後である**(上記「#38 へ」)

## 編集してよい文書

worktree をまたいだ所有権の宣言。**ここに挙がっていない文書は触らない。**

- なし(コードとテストのみ)

**[GDD02](../03-gdd/02-economy.md)・[GDD06](../03-gdd/06-trade-and-negotiation.md)・[GDD08](../03-gdd/08-household-and-decision.md)・[TDD01](../04-tdd/01-sim-core-and-m0.md) は、本タスクが必要とする決定をすべて持っている**(不足していた7点はフェーズ1 が本ブランチで先に埋めてある)。実装が仕様と食い違ったら、**直すのはコードであって文書ではない**。文書側を直す必要があると判断したら、それは象限I-b(仕様そのものの欠陥)なので[止まって報告する](../process/02-task-spec.md)。

## このタスクで特に効く規約

[ADR-0002](../adr/0002-time-model-and-determinism.md) の決定論規約のうち、このタスクで踏みやすいものだけを挙げる。**機械で捕まるものは書かない**(浮動小数点と列挙順が保証されないコレクションは `BannedSymbols.txt` と `DeterminismConventionTests` が止める)。

- **「実効価格」と「実質コスト」を取り違えない。** 比較(argmin・予算・購入量)に使うのは**実質コスト**、支払いと資金上限に使うのは**実効価格**である([GDD02 §6.2.1](../03-gdd/02-economy.md) / [§8.2.3](../03-gdd/02-economy.md))。**移動費は貨幣として誰にも支払われない** — 取り違えると帳簿と流動資金が突合しないか、移動費ぶん買える数が減る
- **耐久(工具)だけ単位が2つある。** `DemandLine.TargetStock` / `ExpectedStock` と `PurchaseQuantity` の結果は**耐久値**、在庫・価格・数量は**個数**。`CeilDiv(…, ProductionRunsPerToolWear)` を通す位置は2か所(移動費の分母と、購入量 → 個数)
- **丸めの向きが式ごとに違う。** 移動費は切り上げ(`CeilDiv`)、資金上限は**切り下げ**(`FloorDiv`)、移動平均は切り上げ1回。**裸の `/` を書かない**
- **中間の積は `long`。** `数量 × 実効価格`、`移動時間 × 機会費用`、`旧移動平均 × (1000 − β)`
- **走査順は 世帯 Id 昇順 → `HouseholdDemand.Lines` の並び → `Market` の `MarketKey` 昇順。** `Lines` を並べ替えない(#36 が GDD02 §6.2.1 の順に組んである)
- **`Knowledge` の添字は NpcId、`Households` の添字は世帯 Id。** `Knowledge[household.Id]` と書いてもコンパイルは通る([TDD01 §3.2](../04-tdd/01-sim-core-and-m0.md))
- **係数の定数には単位のコメントを付ける**(`= 250; // ‰`、`= 1; // 時間/区画`)

## 完了条件

- [ ] 「落ちるべき条件」のテストが全て緑
- [ ] **「核心」印のうち #6・#7・#12・#17・#20・#26・#29 に変異を当てて落ちることを確認し、当てた変異と結果を残した**(#29 は `household.LiquidFunds` → `surplus` を当てる。#36 引き継ぎ表 A そのものである)
- [ ] `dotnet build Visionary.sln -c Release` が警告0
- [ ] `dotnet test Visionary.sln -c Release` が緑
- [ ] `dotnet format Visionary.sln --verify-no-changes --severity warn` が通る
- [ ] レビュアーエージェントの指摘が解消済み
