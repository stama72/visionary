namespace Visionary.Sim.Systems;

/// <summary>
/// 職業付け替え(GDD02b §4.1・§4.2 ④)の純関数。<see cref="SellableStock"/> と同じく静的クラスに置く
/// ── <see cref="World"/> は受け取るが状態を持たない。
/// </summary>
/// <remarks>
/// <b>価格や原価から決めない</b>(GDD02b §4.2)。担い手世帯数だけを見る。除算は現れない ──
/// 担い手世帯数は整数の数え上げであり、比率も平均も取らない。
/// </remarks>
public static class OccupationReassignment
{
    /// <summary>その職業を担っている世帯数。<b>自世帯を含めて数える</b>(GDD02b §4.2)。</summary>
    /// <remarks>
    /// <b>その場の <paramref name="world"/> を数える。</b>呼び出し側がループの前にスナップショットして
    /// はならない ── 同職業の2戸が同じ日に3条件を満たしたとき、スナップショットだと両方が離脱して
    /// 担い手0になる(GDD02b §4.2 が構造的に防ぐと宣言した状態そのもの)。その場で数えれば、
    /// Id の小さい方が離脱した後で Id の大きい方は <c>CarrierCount == 1</c> に当たり、維持される。
    /// </remarks>
    public static int CarrierCount(World world, Occupation occupation)
    {
        ArgumentNullException.ThrowIfNull(world);

        int count = 0;

        // 世帯Id昇順(添字順)。列挙順規約(ADR-0002)を、Dictionary/HashSetを使わない線形走査で満たす。
        foreach (var household in world.Households)
        {
            if (household.Occupation == occupation)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>④へ進むゲート(GDD02b §4.1 の3条件すべて)。</summary>
    /// <remarks>
    /// <b><c>&amp;&amp;</c> である。</b>1つ欠けたら付け替えない ── b だけでは「その日完売した
    /// 健全な世帯」が1日の資金不足で廃業する(GDD02b §4.1 ※1)。
    /// <para>
    /// <b>b は販売在庫であって工房在庫ではない</b>(※2)。<see cref="SellableStock.Of"/> を通す ──
    /// 生の <c>WorkshopInventory[出力品目] == 0</c> だと工具の留保1個(GDD02c §1.3)を引かないので、
    /// 工具を1個持つ日にbが立たない。
    /// </para>
    /// <para>
    /// <b>c は当日の値である。</b>順1 <see cref="ProductionSystem"/> が同じtickで書いた値を順3が読む
    /// (前日値ではない)。<b>c は入力切れだけを見る</b>(GDD02b §4.1 の※。生産量0だけにすると、
    /// 鍛冶が労働が1日足りないだけで廃業する ── 工具切れはcに入らない: 全職業が工具無しでも
    /// 半分の能力で続くため生産量が0になるとは限らないが、それを理由にゲートを閉じない
    /// (#237、進捗‰の持ち越し)。
    /// </para>
    /// </remarks>
    public static bool IsGateOpen(WorldDefinition definition, HouseholdState household)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(household);

        // a. 破産中。
        if (household.IsBankrupt != 1)
        {
            return false;
        }

        // b. 出力品目1件(TradeSystemのコンストラクタが出力2件以上を拒んでいるので、
        // Outputs[0]を読むだけでよい。GDD02c §2.3の配分規則が無いことの重複検査はしない)。
        var recipe = definition.Recipes[(int)household.Occupation];
        int outputItemId = recipe.Outputs[0].ItemId;

        if (SellableStock.Of(definition, household, outputItemId) != 0)
        {
            return false;
        }

        // c. 当日の生産量0 かつ 入力から作れる回数0(入力切れ、GDD02b §4.1 の※)。
        // 生産量0だけで判定すると、能力はあるのに入力が届いていないだけの日(旧版)に加えて、
        // 入力は足りているのに労働力が所要労働‰未満だった日(#237、鍛冶が1日だけ労働不足に
        // 陥った日)まで開いてしまう。入力から作れる回数は順1の後・順3までに入力が動かないため
        // (GDD02b §4.1 の※)、順1が書いた工房在庫からその場で求め直せる。
        if (household.ProductionRuns != 0)
        {
            return false;
        }

        if (recipe.RunsFromInputs(household.WorkshopInventory) != 0)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 付け替え先(GDD02b §4.2)。<b>維持するときは false を返し、<paramref name="target"/> には
    /// 現在の職業を置く。</b>
    /// </summary>
    /// <remarks>
    /// <b>段Aで区画フィルタつき、空なら段Bで区画フィルタを外す</b>(自職業の除外は外さない)。
    /// 段Bは M0 では立たない(1区画最大2戸なので、自職業を除いた4職業から同区画の1職業を除いても
    /// 3件は残る)が、GDD02b §4.2 が規則として持っているため実装する。
    /// <para>
    /// <b>同値は職業Id昇順。</b>昇順に走査して <c>count &lt; bestCount</c> で更新するので、
    /// 先に来た小さいIdが残る(<c>&lt;=</c> にすると大きいIdが勝ってしまう。ADR-0002の列挙順規約)。
    /// </para>
    /// </remarks>
    public static bool TrySelectTarget(
        WorldDefinition definition, World world, HouseholdState household, out Occupation target)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(household);

        target = household.Occupation;

        // 1. 唯一の担い手なら維持する(GDD02b §4.2が構造的に防ぐ「担い手0」)。
        if (CarrierCount(world, household.Occupation) <= 1)
        {
            return false;
        }

        // 2. 段A: 区画フィルタつき。
        if (TryFindLeastCarriedOccupation(definition, world, household, applyDistrictFilter: true, out var districtSafeTarget))
        {
            target = districtSafeTarget;
            return true;
        }

        // 段B: 段Aが1件も拾えなかったときだけ、区画フィルタを外す(自職業の除外は外さない)。
        if (TryFindLeastCarriedOccupation(definition, world, household, applyDistrictFilter: false, out var anyTarget))
        {
            target = anyTarget;
            return true;
        }

        // 4. 候補が1件も無い(=職業が1つしかない定義)。
        return false;
    }

    /// <summary>
    /// 自職業を除く職業のうち、<paramref name="applyDistrictFilter"/> が真なら「破産世帯と同じ区画に
    /// その職業の他世帯が居ない」ものだけに絞り、担い手世帯数が最小のものを1件返す。
    /// </summary>
    private static bool TryFindLeastCarriedOccupation(
        WorldDefinition definition,
        World world,
        HouseholdState household,
        bool applyDistrictFilter,
        out Occupation best)
    {
        best = default;
        bool found = false;
        int bestCount = 0;

        // 職業Id昇順(0…OccupationCount-1)。definitionが持つレシピの数で走査する
        // (Occupation enumの要素数ではない)。
        for (int occupationId = 0; occupationId < definition.OccupationCount; occupationId++)
        {
            var candidate = (Occupation)occupationId;

            if (candidate == household.Occupation)
            {
                continue;
            }

            if (applyDistrictFilter && IsOccupationPresentInDistrict(world, household.DistrictId, candidate))
            {
                continue;
            }

            int count = CarrierCount(world, candidate);

            // 昇順走査 + < 更新(同値は先に来た小さいIdが残る。ADR-0002)。
            if (!found || count < bestCount)
            {
                found = true;
                bestCount = count;
                best = candidate;
            }
        }

        return found;
    }

    /// <summary>区画 <paramref name="districtId"/> に職業 <paramref name="occupation"/> の世帯が居るか。</summary>
    private static bool IsOccupationPresentInDistrict(World world, int districtId, Occupation occupation)
    {
        foreach (var household in world.Households)
        {
            if (household.DistrictId == districtId && household.Occupation == occupation)
            {
                return true;
            }
        }

        return false;
    }
}
