namespace Visionary.Sim.Verification;

/// <summary>
/// シードを横断する畳み込み(W2-21 タスク仕様「5.」)。<see cref="VerificationAccumulator"/> が
/// 出した1シードぶんの判定を、項目ごとに1つの <see cref="Verdict"/> へ畳む。
/// </summary>
public static class RunSummaryBuilder
{
    /// <summary>
    /// <paramref name="seeds"/> は<b>シードを回した順</b>(= <c>masterSeeds</c> の並び)のまま
    /// <see cref="RunSummary.Seeds"/> に写す。並べ替えない — <c>masterSeeds</c> が昇順でない設定を
    /// 書ける以上、並べ替えると設定と出力の対応が崩れる(タスク仕様)。
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="seeds"/> が空のとき。</exception>
    public static RunSummary Build(int durationDays, IReadOnlyList<SeedVerification> seeds)
    {
        ArgumentNullException.ThrowIfNull(seeds);

        if (seeds.Count == 0)
        {
            throw new ArgumentException(
                "seeds は1件以上必要(vsim run は masterSeeds を非空に強制するので通常は到達しない)。",
                nameof(seeds));
        }

        var overall = new List<OverallItemResult>(VerificationItemIds.All.Length);

        foreach (string id in VerificationItemIds.All)
        {
            var redSeeds = new List<long>();
            var indeterminateSeeds = new List<long>();

            foreach (var seed in seeds)
            {
                var item = FindItem(seed, id);

                switch (item.Verdict)
                {
                    case Verdict.Red:
                        redSeeds.Add(seed.Seed);
                        break;
                    case Verdict.Indeterminate:
                        indeterminateSeeds.Add(seed.Seed);
                        break;
                    case Verdict.Green:
                    default:
                        break;
                }
            }

            // シード昇順(タスク仕様「3.」)。masterSeedsの並びは保持するのはseeds配列そのもの
            // だけであり、赤・判定不能の一覧は横断集計なので独立に昇順で持つ。
            redSeeds.Sort();
            indeterminateSeeds.Sort();

            // 判定不能を緑へ倒さない(狭い側に倒す規則。03-corrections 規則1)。
            // 1つでも赤いシードがあれば赤、赤が無く判定不能が1つでもあれば判定不能、それ以外が緑。
            Verdict overallVerdict = redSeeds.Count > 0
                ? Verdict.Red
                : indeterminateSeeds.Count > 0
                    ? Verdict.Indeterminate
                    : Verdict.Green;

            overall.Add(new OverallItemResult(id, overallVerdict, redSeeds, indeterminateSeeds));
        }

        return new RunSummary(
            SchemaVersion: 1,
            WindowDays: VerificationThresholds.WindowDays,
            TransientDays: VerificationThresholds.TransientDays,
            DurationDays: durationDays,
            Seeds: seeds,
            Overall: overall);
    }

    private static VerificationItemResult FindItem(SeedVerification seed, string id)
    {
        foreach (var item in seed.Items)
        {
            if (item.Id == id)
            {
                return item;
            }
        }

        throw new ArgumentException(
            $"シード{seed.Seed}の判定に項目Id={id}が無い(VerificationAccumulator.Buildの契約違反)。");
    }
}
