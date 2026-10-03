using Visionary.Sim.Time;

namespace Visionary.Sim.Dialogue;

/// <summary>
/// 台詞テンプレート(GDD01 §3.3 / §3.3.1)。軸が <c>null</c> なら何にでも当たり、値があればその値にだけ当たる。
/// </summary>
public sealed record DialogueTemplate(
    string Id,
    LineKind Kind,
    Occupation? Occupation,
    Season? Season,
    Gratitude? Gratitude,
    Personality? Personality,
    string Text);

/// <summary>品目の表示名と助数詞(「工具」と「本」)。</summary>
public readonly record struct ItemWording(string Name, string Counter);

/// <summary><see cref="DialogueSelector.Say"/> に渡す、会話の時点の文脈。</summary>
/// <remarks>
/// 呼ぶ側が <see cref="World"/> から組む。<see cref="Quantity"/> の単位は個
/// (開示する3理由に限るので ‰人日 は来ない)。
/// </remarks>
public readonly record struct DialogueContext(
    Occupation Occupation,
    Season Season,
    Personality? Personality,
    Gratitude? Gratitude,
    int? ItemId,
    int? Quantity);

/// <summary>選ばれた台詞。<see cref="Text"/> は差し込み展開済み。</summary>
public readonly record struct DialogueLine(LineKind Kind, string TemplateId, string Text);
