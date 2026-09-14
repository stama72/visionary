namespace Visionary.Sim;

/// <summary>
/// 商業・サービス職を含む全職業に共通の階層(GDD08 §2.1 / §3.3)。
/// </summary>
/// <remarks>
/// <b>職人が「含まない」側なのが要点である。</b>徒弟は親方の家に住み込むので同じ世帯だが、
/// 職人は自分の家計を持つ別世帯として扱う(GDD08 §2.1)。階層は個人に付き、世帯には付かない。
/// </remarks>
public enum NpcRank
{
    /// <summary>親方(世帯主)。</summary>
    Master = 0,

    /// <summary>職人。自分の家計を持つため、親方とは別世帯になる。</summary>
    Journeyman = 1,

    /// <summary>徒弟。住み込みで、給金なし・食住のみ(GDD10 §1)。</summary>
    Apprentice = 2,
}

/// <summary>
/// NPC の状態(TDD01 §3.2)。<b>個人に残るのは、その人が誰と会い何を見たかに依存するものだけである</b>
/// — 相場知識・信用・熟練度・階層。流動資金・在庫・帳簿・職業は <see cref="HouseholdState"/> が持つ。
/// </summary>
/// <remarks>
/// 何がどこへ移り、なぜそうなのかは TDD01 §3.2「経済主体は世帯である」の表が持つ(ここに複製しない)。
/// </remarks>
public sealed class NpcState
{
    private int householdId;
    private int skillPermille;

    /// <summary><see cref="World.Npcs"/> の添字と一致する、非負の Id(TDD01 §3.2)。</summary>
    public int Id { get; }

    /// <summary>所属する世帯の Id(TDD01 §3.2)。非負。</summary>
    public int HouseholdId
    {
        get => householdId;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "世帯の Id は非負(TDD01 §3.2)。");
            }

            householdId = value;
        }
    }

    /// <summary>階層(GDD08 §2.1 / §3.3)。</summary>
    public NpcRank Rank { get; set; }

    /// <summary>
    /// 熟練度。<b>単位は ‰(0〜1000)</b>。段階ではなく連続量である(GDD08 §4.1)。
    /// </summary>
    public int SkillPermille
    {
        get => skillPermille;
        set
        {
            if (value is < 0 or > 1000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "熟練度は ‰ の整数で 0〜1000(GDD08 §4.1)。");
            }

            skillPermille = value;
        }
    }

    public NpcState(int id)
    {
        if (id < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "NPC の Id は非負(TDD01 §3.2)。");
        }

        Id = id;
        householdId = 0;
        Rank = NpcRank.Master;
        skillPermille = 0;
    }
}
