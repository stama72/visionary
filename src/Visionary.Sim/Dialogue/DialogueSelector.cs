using Visionary.Sim.Randomness;
using Visionary.Sim.Time;

namespace Visionary.Sim.Dialogue;

/// <summary>
/// 台詞テンプレートの照合と選択(GDD01 §3.3.1)。<see cref="World"/> を読まず書かない。
/// </summary>
public static class DialogueSelector
{
    /// <summary>
    /// 1回の会話につき1度だけ開く。鍵は (masterSeed, <see cref="RandomStream.Dialogue"/>, tick, npcId)。
    /// 同じ NPC・同じ tick で2度開くと同じ乱数列になる。<see cref="Systems.SimContext"/> の
    /// 二重オープン検出はここには掛からない(<see cref="RandomSource"/> を直接開くため)。
    /// </summary>
    public static RandomSequence OpenConversation(long masterSeed, Tick tick, int npcId) =>
        new RandomSource(masterSeed).Open(RandomStream.Dialogue, tick, npcId);

    /// <summary>
    /// 1行選んで展開する。<b>1行につき乱数をちょうど1回引く</b>(プールが1本でも)ので、
    /// 呼び出し順を変えると以後の選択が変わる。
    /// </summary>
    /// <exception cref="ArgumentException">引数の組み合わせが <paramref name="kind"/> に合わないとき。</exception>
    /// <exception cref="ArgumentOutOfRangeException">ItemId・Quantity が範囲外のとき。</exception>
    /// <exception cref="InvalidOperationException">候補が 0 本のとき(資料の網羅の不足)。</exception>
    public static DialogueLine Say(
        DialogueCorpus corpus,
        DialogueMemory memory,
        ref RandomSequence random,
        int npcId,
        LineKind kind,
        in DialogueContext context)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(memory);

        ValidateArguments(kind, context);

        // 候補: 資料の並び順のまま、種類と4軸がすべて当たるもの(和集合)。
        var candidates = new List<DialogueTemplate>();

        foreach (var template in corpus.Templates)
        {
            if (template.Kind == kind
                && AxisMatches(template.Occupation, context.Occupation)
                && AxisMatches(template.Season, context.Season)
                && AxisMatches(template.Gratitude, context.Gratitude)
                && AxisMatches(template.Personality, context.Personality))
            {
                candidates.Add(template);
            }
        }

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                $"候補が 0 本(種類 {kind}, 職業 {context.Occupation}, 季節 {context.Season}, "
                + $"段階 {context.Gratitude}, 性格 {context.Personality})。資料の網羅が足りない。");
        }

        // プール: この NPC での使用回数が最小のもの。
        int minUse = int.MaxValue;

        foreach (var candidate in candidates)
        {
            minUse = Math.Min(minUse, memory.UseCount(npcId, candidate.Id));
        }

        var pool = new List<DialogueTemplate>();

        foreach (var candidate in candidates)
        {
            if (memory.UseCount(npcId, candidate.Id) == minUse)
            {
                pool.Add(candidate);
            }
        }

        // 一巡した直後に同じ台詞が続くのを防ぐ。
        if (pool.Count >= 2)
        {
            string? last = memory.LastId(npcId, kind);

            if (last is not null)
            {
                int lastIndex = pool.FindIndex(t => string.Equals(t.Id, last, StringComparison.Ordinal));

                if (lastIndex >= 0)
                {
                    pool.RemoveAt(lastIndex);
                }
            }
        }

        // プールが1本でも必ず1回引く。1行あたりの引く回数を一定にし、後の行の選択が
        // 前の行のプールの大きさに依存しないようにする。
        var chosen = pool[random.NextInt(0, pool.Count)];

        memory.Record(npcId, kind, chosen.Id);

        return new DialogueLine(kind, chosen.Id, Expand(corpus, chosen.Text, context));
    }

    private static bool AxisMatches<T>(T? templateValue, T contextValue)
        where T : struct
        => templateValue is null || EqualityComparer<T>.Default.Equals(templateValue.Value, contextValue);

    private static bool AxisMatches<T>(T? templateValue, T? contextValue)
        where T : struct
        => templateValue is null
            || (contextValue is not null && EqualityComparer<T>.Default.Equals(templateValue.Value, contextValue.Value));

    private static void ValidateArguments(LineKind kind, in DialogueContext context)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException($"未知の種類: {kind}", nameof(kind));
        }

        if (kind == LineKind.TradeAccepted)
        {
            if (context.Gratitude is null)
            {
                throw new ArgumentException("取引の成立は Gratitude が必須。", nameof(context));
            }
        }
        else if (context.Gratitude is not null)
        {
            throw new ArgumentException($"{kind} は Gratitude を持たない。", nameof(context));
        }

        bool needsItem = kind is LineKind.NeedDisclosure or LineKind.TradeAccepted or LineKind.TradeRefused;
        bool needsQuantity = kind is LineKind.NeedDisclosure or LineKind.TradeAccepted;

        if (needsItem != context.ItemId.HasValue)
        {
            throw new ArgumentException($"{kind} の ItemId は{(needsItem ? "必須" : "null")}。", nameof(context));
        }

        if (needsQuantity != context.Quantity.HasValue)
        {
            throw new ArgumentException($"{kind} の Quantity は{(needsQuantity ? "必須" : "null")}。", nameof(context));
        }

        if (context.ItemId is int itemId && (itemId < 0 || itemId >= Item.Count))
        {
            throw new ArgumentOutOfRangeException(nameof(context), itemId, $"ItemId は 0〜{Item.Count - 1}。");
        }

        if (context.Quantity is int quantity && quantity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(context), quantity, "Quantity は 1 以上。");
        }
    }

    private static string Expand(DialogueCorpus corpus, string text, in DialogueContext context)
    {
        // string.Replace(string, string) は序数比較。
        text = text.Replace("{" + DialogueCorpus.SeasonPlaceholder + "}", corpus.SeasonNameOf((int)context.Season));

        if (context.ItemId is int itemId)
        {
            var wording = corpus.ItemWordingOf(itemId);
            text = text.Replace("{" + DialogueCorpus.ItemPlaceholder + "}", wording.Name);

            if (context.Quantity is int quantity)
            {
                text = text.Replace(
                    "{" + DialogueCorpus.QuantityPlaceholder + "}",
                    KanjiNumerals.Format(quantity) + wording.Counter);
            }
        }

        return text;
    }
}
