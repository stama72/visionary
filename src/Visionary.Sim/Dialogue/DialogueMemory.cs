namespace Visionary.Sim.Dialogue;

/// <summary>
/// NPC ごとの台詞の使用回数と、種類ごとの直前の Id(GDD01 §3.3.1「選び方」)。
/// <see cref="World"/> の外に置き、状態ハッシュに入らない。
/// </summary>
/// <remarks>
/// 列挙しない(引くだけ)ので順序は選択に影響しないが、ADR-0002 の規約に合わせて
/// <c>SortedDictionary</c> で持つ。文字列キーは序数比較。
/// </remarks>
public sealed class DialogueMemory
{
    private readonly SortedDictionary<int, SortedDictionary<string, int>> _useCounts = new();

    // 直前の Id の鍵は (種類, 開示の順)。順は開示以外では null。
    private readonly SortedDictionary<int, SortedDictionary<(LineKind, DisclosureOrder?), string>> _lastIds = new();

    public DialogueMemory()
    {
    }

    internal int UseCount(int npcId, string templateId) =>
        _useCounts.TryGetValue(npcId, out var perNpc) && perNpc.TryGetValue(templateId, out int count)
            ? count
            : 0;

    internal string? LastId(int npcId, LineKind kind, DisclosureOrder? order) =>
        _lastIds.TryGetValue(npcId, out var perNpc) && perNpc.TryGetValue((kind, order), out string? id)
            ? id
            : null;

    internal void Record(int npcId, LineKind kind, DisclosureOrder? order, string templateId)
    {
        if (!_useCounts.TryGetValue(npcId, out var counts))
        {
            counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            _useCounts[npcId] = counts;
        }

        counts[templateId] = counts.GetValueOrDefault(templateId) + 1;

        if (!_lastIds.TryGetValue(npcId, out var lasts))
        {
            lasts = new SortedDictionary<(LineKind, DisclosureOrder?), string>();
            _lastIds[npcId] = lasts;
        }

        lasts[(kind, order)] = templateId;
    }
}
