using Visionary.Sim.Verification;

namespace Visionary.Sim.Tests.Verification;

/// <summary>
/// <see cref="RunSummaryBuilder"/> の検査(W2-21 タスク仕様「落ちるべき条件」#30・#31・
/// 別表(レビュー3巡目で追加)#55)。
/// </summary>
public sealed class RunSummaryBuilderTests
{
    /// <summary>TDD01 §5.2 の表の並び(12項目)。<c>VerificationItemIds</c> は internal なのでここで写す。</summary>
    private static readonly string[] AllIds =
    {
        "8-1a", "8-1b", "8-1c", "8-2a", "8-2b", "8-3", "8-4", "8-5a", "8-5b", "8-5c", "8-6", "8-7",
    };

    private static SeedVerification MakeSeed(long seed, string targetId, Verdict targetVerdict)
    {
        var items = AllIds
            .Select(id => new VerificationItemResult(
                id,
                id == targetId ? targetVerdict : Verdict.Green,
                targetVerdict == Verdict.Red && id == targetId ? 100 : -1,
                Array.Empty<Evidence>()))
            .ToList();

        return new SeedVerification(seed, 150, 1, items);
    }

    /// <summary>
    /// #30。核心。3シードが(赤,緑,緑)なら赤、(緑,判定不能,緑)なら判定不能、(緑,緑,緑)なら緑。
    /// <c>redSeeds</c> / <c>indeterminateSeeds</c> がシード昇順。
    /// <b>mutator実測(2026-09-24)</b>: 変異#30(<c>RunSummaryBuilder</c> の判定不能の枝を
    /// <see cref="Verdict.Green"/> へ)で赤(落ちた)。
    /// </summary>
    [Fact]
    public void OverallFoldsRedOverIndeterminateOverGreen()
    {
        var redCase = new[]
        {
            MakeSeed(3, "8-2a", Verdict.Red),
            MakeSeed(1, "8-2a", Verdict.Green),
            MakeSeed(2, "8-2a", Verdict.Green),
        };
        var redSummary = RunSummaryBuilder.Build(150, redCase);
        var redOverall = redSummary.Overall.Single(o => o.Id == "8-2a");

        Assert.Equal(Verdict.Red, redOverall.Verdict);
        Assert.Equal(new long[] { 3 }, redOverall.RedSeeds);

        var indeterminateCase = new[]
        {
            MakeSeed(1, "8-2a", Verdict.Green),
            MakeSeed(3, "8-2a", Verdict.Indeterminate),
            MakeSeed(2, "8-2a", Verdict.Green),
        };
        var indeterminateSummary = RunSummaryBuilder.Build(150, indeterminateCase);
        var indeterminateOverall = indeterminateSummary.Overall.Single(o => o.Id == "8-2a");

        Assert.Equal(Verdict.Indeterminate, indeterminateOverall.Verdict);
        Assert.Equal(new long[] { 3 }, indeterminateOverall.IndeterminateSeeds);

        var greenCase = new[]
        {
            MakeSeed(1, "8-2a", Verdict.Green),
            MakeSeed(2, "8-2a", Verdict.Green),
            MakeSeed(3, "8-2a", Verdict.Green),
        };
        var greenSummary = RunSummaryBuilder.Build(150, greenCase);
        var greenOverall = greenSummary.Overall.Single(o => o.Id == "8-2a");

        Assert.Equal(Verdict.Green, greenOverall.Verdict);
        Assert.Empty(greenOverall.RedSeeds);
        Assert.Empty(greenOverall.IndeterminateSeeds);
    }

    /// <summary>
    /// 55(別表(レビュー3巡目で追加)#55)。(赤, 判定不能, 緑) のシードの組で <c>overall</c> が
    /// 赤になり、<c>redSeeds</c> / <c>indeterminateSeeds</c> の双方が正しく埋まる。#30 の赤ケースは
    /// (赤, 緑, 緑) で判定不能のシードを含んでおらず、判定不能を含む赤の組を試していなかった。
    /// <b>mutator実測(2026-09-24)</b>: 変異E6(<c>RunSummaryBuilder</c> の三項を入れ替え、判定不能を
    /// 赤より先に判定)で赤(落ちた)。
    /// </summary>
    [Fact]
    public void OverallKeepsRedOverIndeterminateAcrossSeeds()
    {
        var seeds = new[]
        {
            MakeSeed(1, "8-2a", Verdict.Red),
            MakeSeed(2, "8-2a", Verdict.Indeterminate),
            MakeSeed(3, "8-2a", Verdict.Green),
        };

        var summary = RunSummaryBuilder.Build(150, seeds);
        var overall = summary.Overall.Single(o => o.Id == "8-2a");

        Assert.Equal(Verdict.Red, overall.Verdict);
        Assert.Equal(new long[] { 1 }, overall.RedSeeds);
        Assert.Equal(new long[] { 2 }, overall.IndeterminateSeeds);
    }

    /// <summary>#31。<c>masterSeeds</c> が [3, 1, 2] のとき <c>seeds</c> 配列もその順。</summary>
    [Fact]
    public void SeedOrderFollowsTheConfig()
    {
        var seeds = new[]
        {
            MakeSeed(3, "8-2a", Verdict.Green),
            MakeSeed(1, "8-2a", Verdict.Green),
            MakeSeed(2, "8-2a", Verdict.Green),
        };

        var summary = RunSummaryBuilder.Build(150, seeds);

        Assert.Equal(new long[] { 3, 1, 2 }, summary.Seeds.Select(s => s.Seed));
    }

    /// <summary><c>seeds</c> が空なら例外を投げる(Buildの契約)。</summary>
    [Fact]
    public void BuildRejectsEmptySeeds()
    {
        Assert.Throws<ArgumentException>(() => RunSummaryBuilder.Build(150, Array.Empty<SeedVerification>()));
    }
}
