using Visionary.Sim.Dialogue;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Dialogue;

/// <summary>
/// 台詞のテスト(W5-01 #1〜#15)が使う合成資料の部品。<c>DialogueCorpus.M0</c> に依存させない
/// (資料を書き直しても落ちないように)。
/// </summary>
internal static class DialogueTestHelpers
{
    /// <summary>W5-01 §5 の品目表(添字 = Item の Id)。M0 の資料とは別に、テスト側で持つ。</summary>
    public static readonly ItemWording[] StandardItems =
    {
        new("穀物", "袋"), new("木材", "本"), new("鉄鉱石", "籠"), new("木炭", "俵"), new("小麦粉", "袋"),
        new("薪", "束"), new("パン", "個"), new("ビール", "杯"), new("工具", "本"),
    };

    public static readonly string[] StandardSeasons = { "春", "夏", "秋", "冬" };

    public static DialogueTemplate Template(
        string id,
        LineKind kind,
        Occupation? occupation = null,
        Season? season = null,
        Gratitude? gratitude = null,
        Personality? personality = null,
        string? text = null) =>
        new(id, kind, occupation, season, gratitude, personality, text ?? id);

    public static DialogueCorpus Corpus(params DialogueTemplate[] templates) =>
        new(templates, StandardItems, StandardSeasons);

    public static DialogueCorpus Corpus(ItemWording[] items, params DialogueTemplate[] templates) =>
        new(templates, items, StandardSeasons);

    /// <summary>n 本の指定なしテンプレート(Id は <c>prefix1</c>〜<c>prefixN</c>)。</summary>
    public static DialogueTemplate[] Many(LineKind kind, string prefix, int count)
    {
        var result = new DialogueTemplate[count];

        for (int i = 0; i < count; i++)
        {
            result[i] = Template(prefix + (i + 1), kind);
        }

        return result;
    }

    public static DialogueContext Plain(
        Occupation occupation = Occupation.Baker,
        Season season = Season.Spring,
        Personality? personality = null) =>
        new(occupation, season, personality, null, null, null);
}
