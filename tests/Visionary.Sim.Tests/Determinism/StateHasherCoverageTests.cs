using System.Reflection;
using System.Runtime.CompilerServices;

namespace Visionary.Sim.Tests.Determinism;

/// <summary>
/// <see cref="World"/> の区画の一覧を凍結し、<c>StateHasher</c> が追随すべき変更を
/// リフレクションで検知する(TDD01 §3.8)。
/// </summary>
/// <remarks>
/// <para>
/// W2 で <see cref="World"/> に区画が増えても、<c>StateHasher.Compute</c> の更新を忘れれば
/// ビルドもテストもコンパイルは通り、2プロセス検証(CI の3回実行)も緑のまま進む —
/// 新しい区画がハッシュに入らないだけで「一致する」ことに変わりはないため。
/// この静かな緩みを検出するのがこのテストの役目。
/// </para>
/// <para>
/// <see cref="Architecture.DeterminismConventionTests"/> と同じ束縛(<c>Public | NonPublic |
/// Instance | Static</c>)でリフレクションし、宣言側を機械的に押さえる方式を踏襲する。
/// <c>StateHasher</c> は <see cref="World"/> と同じ <c>Visionary.Sim</c> アセンブリ内にあるので
/// <c>internal</c> な区画もハッシュでき、W2 の本物のシステム群も TDD01 §3.3 により同じ
/// アセンブリに置かれる。「アセンブリ内でしか使わない区画を <c>internal</c> で足す」は
/// 現実的な書き方なので、<c>public</c> だけに絞ると見逃しが生まれる。
/// </para>
/// </remarks>
public sealed class StateHasherCoverageTests
{
    private const BindingFlags AllMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
        | BindingFlags.DeclaredOnly;

    /// <summary>
    /// <see cref="World"/> の区画の期待一覧(TDD01 §3.8 の含める/含めない表と一致)。
    /// 増減したら <c>StateHasher</c> 側(§3.8 の含める/含めない表を含む)を見直し、
    /// 意図した変更ならここも更新すること。
    /// </summary>
    private static readonly string[] ExpectedWorldSections =
    {
        "Now",
        "Npcs",
        "Market",
        "TrustLedger",
        "Needs",
        "Promises",
        "Knowledge",
        "Ledgers",
        "EventLog",
    };

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
            "World の区画が変わった。StateHasher を更新したか、意図的な除外なら"
                + "TDD01 §3.8 の除外表とこの一覧(ExpectedWorldSections)を更新せよ。"
                + Environment.NewLine
                + $"  期待: {string.Join(", ", expected)}"
                + Environment.NewLine
                + $"  実際: {string.Join(", ", actual)}");
    }

    /// <summary>
    /// プロパティとフィールドの両方を見る。<c>public</c> プロパティだけに絞ると、
    /// アセンブリ内(<c>Visionary.Sim</c>)にしか公開しない <c>internal</c> な区画を見逃す。
    /// </summary>
    /// <remarks>
    /// フィールドはコンパイラ生成の自動プロパティのバッキングフィールド
    /// (<c>&lt;Now&gt;k__BackingField</c> など)を含むため、<see cref="CompilerGeneratedAttribute"/>
    /// が付いたものを除く — でなければ同じ区画がプロパティとフィールドの二重に数えられる。
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
