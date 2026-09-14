using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Visionary.Sim.Determinism;

namespace Visionary.Sim.Tests.Determinism;

/// <summary>
/// <see cref="World"/> の区分の一覧を凍結し、<c>StateHasher</c> が追随すべき変更を
/// リフレクションで検知する(TDD01 §3.8)。
/// </summary>
/// <remarks>
/// <para>
/// W2 で <see cref="World"/> に区分が増えても、<c>StateHasher.Compute</c> の更新を忘れれば
/// ビルドもテストもコンパイルは通り、2プロセス検証(CI の3回実行)も緑のまま進む —
/// 新しい区分がハッシュに入らないだけで「一致する」ことに変わりはないため。
/// その緩みに気づく機会を作るのがこのテストの役目である。
/// </para>
/// <para>
/// <b>ただし検出しているのは「区分の一覧が変わったこと」だけで、<c>StateHasher</c> が
/// 追随したことは見ていない。</b>下の期待一覧だけを更新して <c>Compute</c> を触らなければ
/// このテストは緑になる。ハッシャ本体の追随は人が確認すること。
/// </para>
/// <para>
/// <see cref="Architecture.DeterminismConventionTests"/> と同じ束縛(<c>Public | NonPublic |
/// Instance | Static</c>)でリフレクションし、宣言側を機械的に押さえる方式を踏襲する。
/// <c>StateHasher</c> は <see cref="World"/> と同じ <c>Visionary.Sim</c> アセンブリ内にあるので
/// <c>internal</c> な区分もハッシュでき、W2 の本物のシステム群も TDD01 §3.3 により同じ
/// アセンブリに置かれる。「アセンブリ内でしか使わない区分を <c>internal</c> で足す」は
/// 現実的な書き方なので、<c>public</c> だけに絞ると見逃しが生まれる。
/// </para>
/// </remarks>
public sealed class StateHasherCoverageTests
{
    private const BindingFlags AllMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
        | BindingFlags.DeclaredOnly;

    /// <summary>
    /// <see cref="World"/> の区分の期待一覧(TDD01 §3.8 の含める/含めない表と一致)。
    /// 増減したら <c>StateHasher</c> 側(§3.8 の含める/含めない表を含む)を見直し、
    /// 意図した変更ならここも更新すること。
    /// </summary>
    private static readonly string[] ExpectedWorldSections =
    {
        "Now",
        "Npcs",
        "Households",
        "Market",
        "TrustLedger",
        "Needs",
        "Promises",
        "Knowledge",
        "Ledgers",
        "EventLog",
    };

    /// <summary>
    /// 区分タグの期待値(TDD01 §3.8「既存の値を動かさず末尾へ足す」)。
    /// </summary>
    /// <remarks>
    /// <b>途中に挿入して既存の値をずらす変更を捕まえるためにある。</b>区分タグは
    /// ハッシュへ書き込まれる仕様値なので、値がずれると「同一シード・同一設定の2回実行」の
    /// 比較そのものは緑のまま、過去の実行と比較できない状態になる。
    /// <c>Section</c> は <c>StateHasher</c> の private な入れ子 enum なのでリフレクションで読む。
    /// </remarks>
    private static readonly (string Name, int Value)[] ExpectedSectionTags =
    {
        ("Clock", 1),
        ("Npcs", 2),
        ("Market", 3),
        ("TrustLedger", 4),
        ("Needs", 5),
        ("Promises", 6),
        ("Knowledge", 7),
        ("Ledgers", 8),
        ("Households", 9),
    };

    [Fact]
    public void SectionTagsAreFrozenAndHouseholdsIsNine()
    {
        var sectionType = typeof(StateHasher)
            .GetNestedType("Section", BindingFlags.NonPublic);

        Assert.NotNull(sectionType);

        var actual = Enum.GetValues(sectionType!)
            .Cast<object>()
            // ボックス化された enum は (int) で直接アンボックスできない。
            .Select(value => (
                Name: value.ToString()!,
                Value: Convert.ToInt32(value, CultureInfo.InvariantCulture)))
            .OrderBy(tag => tag.Value)
            .ToArray();

        Assert.Equal(ExpectedSectionTags, actual);
    }

    [Fact]
    public void WorldSectionsAreFrozenSoNewOnesMustBeHashed()
    {
        var actual = WorldMemberNames()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var expected = ExpectedWorldSections
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            actual.SequenceEqual(expected),
            "World の区分が変わった。StateHasher を更新したか、意図的な除外なら"
                + "TDD01 §3.8 の除外表とこの一覧(ExpectedWorldSections)を更新せよ。"
                + Environment.NewLine
                + $"  期待: {string.Join(", ", expected)}"
                + Environment.NewLine
                + $"  実際: {string.Join(", ", actual)}");
    }

    /// <summary>
    /// 区分の<b>要素型</b>の欄の期待一覧。増減したら <c>StateHasher</c> 側を見直し、
    /// 意図した変更ならここも更新すること。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="WorldSectionsAreFrozenSoNewOnesMustBeHashed"/> は区分の一覧しか凍結しない。</b>
    /// <c>World</c> の直下のメンバ名しか見ないので、<c>HouseholdState</c> に欄を足して
    /// <c>StateHasher.Compute</c> に書き忘れる経路は素通りする — ビルドもテストも緑、
    /// CI の2プロセス比較も緑(同一ビルド同士なので、ハッシュが状態の一部を見ていなくても
    /// 「一致」は成立する)。区分レベルで採っている方式を、要素型にも当てるのがこの表である。
    /// </para>
    /// <para>
    /// <b>凍結しているのは欄の一覧だけで、<c>StateHasher</c> が追随したことは見ていない。</b>
    /// 下の一覧だけを更新して <c>Compute</c> を触らなければ緑になる。区分の一覧と同じ限界であり、
    /// ハッシャ本体の追随は人が確認すること。
    /// </para>
    /// </remarks>
    private static readonly (Type Type, string[] Members)[] ExpectedSectionElementMembers =
    {
        (typeof(NpcState), new[] { "Id", "HouseholdId", "Rank", "SkillPermille" }),
        (typeof(HouseholdState), new[]
        {
            "Id", "DistrictId", "OccupationId", "HeadNpcId", "MemberNpcIds",
            "LiquidFunds", "HouseholdInventory", "WorkshopInventory", "IsBankrupt",
        }),
        (typeof(MarketKey), new[] { "ItemId", "SellerId" }),
        (typeof(TrustKey), new[] { "From", "To" }),
        (typeof(TrustScore), new[] { "Value", "LastMet" }),
        (typeof(Need), new[]
        {
            "TypeCode", "TargetHouseholdId", "ItemId", "Quantity", "Deadline", "Urgency", "ReasonCode",
        }),
        (typeof(Promise), new[] { "NeedIndex", "T0", "T1", "B", "State" }),
        (typeof(PriceObservation), new[]
        {
            "ItemId", "LocationId", "Price", "SellerId", "ObservedAt", "Source",
        }),
        (typeof(LedgerEntry), new[]
        {
            "CounterpartyId", "ItemId", "Quantity", "UnitPrice", "OccurredAt", "Terms", "CreditDueAt",
        }),
    };

    [Fact]
    public void SectionElementMembersAreFrozenSoNewOnesMustBeHashed()
    {
        foreach (var (type, expectedMembers) in ExpectedSectionElementMembers)
        {
            var actual = StateMemberNames(type)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            var expected = expectedMembers
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                actual.SequenceEqual(expected),
                $"{type.Name} の欄が変わった。StateHasher.Compute を更新したか、"
                    + "意図的な除外なら TDD01 §3.8 の除外表とこの一覧"
                    + "(ExpectedSectionElementMembers)を更新せよ。"
                    + Environment.NewLine
                    + $"  期待: {string.Join(", ", expected)}"
                    + Environment.NewLine
                    + $"  実際: {string.Join(", ", actual)}");
        }
    }

    /// <summary>
    /// 型が持つ<b>インスタンスの状態</b>の名前。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>static</c> を見ないのは、定数(<see cref="HouseholdState.ExternalMarketSellerId"/>)が
    /// 状態ではないため。<see cref="World"/> 側の走査が <c>Static</c> を含むのと違う点である。
    /// </para>
    /// <para>
    /// <b>手書きのバッキングフィールドをプロパティと二重に数えない。</b>
    /// <c>HouseholdState.isBankrupt</c> や <c>NpcState.skillPermille</c> は、検証付きの setter を
    /// 書くために手で置いたフィールドであり <see cref="CompilerGeneratedAttribute"/> が付かない。
    /// 属性による除外だけでは落ちないので、<b>同名(大文字小文字を無視)のプロパティがある
    /// フィールドを除く</b>。
    /// </para>
    /// <para>
    /// <b>この規則の穴</b>: プロパティと無関係な private フィールドを、たまたま既存プロパティと
    /// 同名(大小違い)で足すと見逃す。実際には起こりにくいので許容する。
    /// </para>
    /// </remarks>
    private static IEnumerable<string> StateMemberNames(Type type)
    {
        const BindingFlags InstanceMembers =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;

        var propertyNames = type.GetProperties(InstanceMembers).Select(property => property.Name).ToArray();

        var fieldNames = type.GetFields(InstanceMembers)
            .Where(field => !field.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .Where(field => !propertyNames.Any(
                name => string.Equals(name, field.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(field => field.Name);

        return propertyNames.Concat(fieldNames);
    }

    /// <summary>
    /// プロパティとフィールドの両方を見る。<c>public</c> プロパティだけに絞ると、
    /// アセンブリ内(<c>Visionary.Sim</c>)にしか公開しない <c>internal</c> な区分を見逃す。
    /// </summary>
    /// <remarks>
    /// フィールドはコンパイラ生成の自動プロパティのバッキングフィールド
    /// (<c>&lt;Now&gt;k__BackingField</c> など)を含むため、<see cref="CompilerGeneratedAttribute"/>
    /// が付いたものを除く — でなければ同じ区分がプロパティとフィールドの二重に数えられる。
    /// </remarks>
    private static IEnumerable<string> WorldMemberNames()
    {
        var type = typeof(World);

        var propertyNames = type.GetProperties(AllMembers).Select(property => property.Name);

        var fieldNames = type.GetFields(AllMembers)
            .Where(field => !field.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .Select(field => field.Name);

        return propertyNames.Concat(fieldNames);
    }
}
