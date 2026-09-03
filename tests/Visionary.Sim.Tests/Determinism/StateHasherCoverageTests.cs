using System.Reflection;

namespace Visionary.Sim.Tests.Determinism;

/// <summary>
/// <see cref="World"/> の公開区画の一覧を凍結し、<c>StateHasher</c> が追随すべき変更を
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
/// <see cref="Architecture.DeterminismConventionTests"/> と同じくリフレクションで
/// 宣言側を機械的に押さえる方式を踏襲する。
/// </para>
/// </remarks>
public sealed class StateHasherCoverageTests
{
    /// <summary>
    /// <see cref="World"/> の公開プロパティの期待一覧(TDD01 §3.8 の含める/含めない表と一致)。
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
        var actual = typeof(World)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var expected = ExpectedWorldSections
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            actual.SequenceEqual(expected),
            "World の公開区画が変わった。StateHasher を更新したか、意図的な除外なら"
                + "TDD01 §3.8 の除外表とこの一覧(ExpectedWorldSections)を更新せよ。"
                + Environment.NewLine
                + $"  期待: {string.Join(", ", expected)}"
                + Environment.NewLine
                + $"  実際: {string.Join(", ", actual)}");
    }
}
