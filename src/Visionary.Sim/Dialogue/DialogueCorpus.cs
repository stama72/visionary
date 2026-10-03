namespace Visionary.Sim.Dialogue;

/// <summary>
/// 台詞の資料(テンプレート・品目の表示名と助数詞・季節名)。コンストラクタが資料の誤りを
/// 起動時に落とす(会話の途中で壊れさせない)。
/// </summary>
public sealed class DialogueCorpus
{
    internal const string ItemPlaceholder = "item";
    internal const string QuantityPlaceholder = "qty";
    internal const string SeasonPlaceholder = "season";

    private const int SeasonCount = 4;

    private readonly DialogueTemplate[] _templates;
    private readonly ItemWording[] _items;
    private readonly string[] _seasonNames;

    /// <param name="templates">資料の並び順が候補の並び順になる。</param>
    /// <param name="items">添字 = <see cref="Item"/> の Id。長さ <see cref="Item.Count"/>。</param>
    /// <param name="seasonNames">添字 = <c>(int)Season</c>。長さ 4。</param>
    /// <exception cref="ArgumentException">資料が規則(タスク仕様 W5-01 §2)に反するとき。</exception>
    public DialogueCorpus(
        IReadOnlyList<DialogueTemplate> templates,
        IReadOnlyList<ItemWording> items,
        IReadOnlyList<string> seasonNames)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(seasonNames);

        if (items.Count != Item.Count)
        {
            throw new ArgumentException($"品目の表示名は {Item.Count} 件必要(実際 {items.Count})。", nameof(items));
        }

        if (seasonNames.Count != SeasonCount)
        {
            throw new ArgumentException($"季節名は {SeasonCount} 件必要(実際 {seasonNames.Count})。", nameof(seasonNames));
        }

        var seenIds = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var template in templates)
        {
            Validate(template, seenIds);
        }

        _templates = templates.ToArray();
        _items = items.ToArray();
        _seasonNames = seasonNames.ToArray();
    }

    /// <summary>M0 の資料(<c>DialogueCorpusM0.cs</c>)。</summary>
    public static DialogueCorpus M0 { get; } = new(
        DialogueCorpusM0.Templates, DialogueCorpusM0.Items, DialogueCorpusM0.SeasonNames);

    public IReadOnlyList<DialogueTemplate> Templates => _templates;

    internal ItemWording ItemWordingOf(int itemId) => _items[itemId];

    internal string SeasonNameOf(int seasonIndex) => _seasonNames[seasonIndex];

    private static void Validate(DialogueTemplate template, SortedSet<string> seenIds)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (string.IsNullOrEmpty(template.Id))
        {
            throw new ArgumentException("テンプレートの Id が空。");
        }

        if (!seenIds.Add(template.Id))
        {
            throw new ArgumentException($"テンプレートの Id が重複している: {template.Id}");
        }

        if (!Enum.IsDefined(template.Kind))
        {
            throw new ArgumentException($"未知の種類({template.Id})。");
        }

        if (template.Season is not null && template.Kind != LineKind.SmallTalk)
        {
            throw new ArgumentException($"季節を持てるのは世間話だけ: {template.Id}");
        }

        // 取引の成立は段階を必ず指定する。指定なしだと渋々にも深い感謝にも当たってしまう。
        if (template.Kind == LineKind.TradeAccepted && template.Gratitude is null)
        {
            throw new ArgumentException($"取引の成立は段階が必須: {template.Id}");
        }

        if (template.Kind != LineKind.TradeAccepted && template.Gratitude is not null)
        {
            throw new ArgumentException($"段階を持てるのは取引の成立だけ: {template.Id}");
        }

        ValidatePlaceholders(template);
    }

    private static void ValidatePlaceholders(DialogueTemplate template)
    {
        if (template.Text is null)
        {
            throw new ArgumentException($"本文が null: {template.Id}");
        }

        var found = new List<string>();
        string text = template.Text;
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '}')
            {
                throw new ArgumentException($"対応の取れない '}}': {template.Id}");
            }

            if (c == '{')
            {
                int close = text.IndexOf('}', i + 1);

                if (close < 0)
                {
                    throw new ArgumentException($"対応の取れない '{{': {template.Id}");
                }

                string name = text.Substring(i + 1, close - i - 1);

                if (name.Contains('{', StringComparison.Ordinal))
                {
                    throw new ArgumentException($"対応の取れない '{{': {template.Id}");
                }

                found.Add(name);
                i = close + 1;
                continue;
            }

            i++;
        }

        string[] allowed = template.Kind switch
        {
            LineKind.SmallTalk => new[] { SeasonPlaceholder },
            LineKind.NeedDisclosure => new[] { ItemPlaceholder, QuantityPlaceholder },
            LineKind.TradeAccepted => new[] { ItemPlaceholder, QuantityPlaceholder },
            LineKind.TradeRefused => new[] { ItemPlaceholder },
            _ => Array.Empty<string>(),
        };

        foreach (string name in found)
        {
            if (Array.IndexOf(allowed, name) < 0)
            {
                throw new ArgumentException($"この種類で使えない差し込み {{{name}}}: {template.Id}");
            }
        }

        // 開示は品目と数量が見えることが GDD01 §6.1 の要件。
        if (template.Kind == LineKind.NeedDisclosure
            && (!found.Contains(ItemPlaceholder) || !found.Contains(QuantityPlaceholder)))
        {
            throw new ArgumentException($"開示は {{item}} と {{qty}} の両方が必須: {template.Id}");
        }
    }
}
