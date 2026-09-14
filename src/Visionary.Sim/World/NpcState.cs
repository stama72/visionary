namespace Visionary.Sim;

/// <summary>
/// NPC の状態(TDD01 §3.2)。W1 では最小構成。
/// </summary>
/// <remarks>
/// <b>W2 で区分が変わる。</b><see cref="LiquidFunds"/> と <see cref="Inventory"/> は廃止し、
/// 職業も <c>HouseholdState</c> が持つ。個人に残るのは相場知識である。
/// 何がどこへ移るかと、その理由は TDD01 §3.2「経済主体は世帯である」の表にある(ここに複製しない)。
/// </remarks>
public sealed class NpcState
{
    /// <summary><see cref="World.Npcs"/> の添字と一致する、非負の Id(TDD01 §3.2)。</summary>
    public int Id { get; }

    /// <summary>手元の流動資金。<b>W2 で廃止し世帯へ移す</b>(TDD01 §3.2)。</summary>
    public int LiquidFunds { get; set; }

    /// <summary>在庫。添字 = itemId。W1 では空で始める。<b>W2 で廃止し、世帯と工房の2本へ移す</b>(TDD01 §3.2)。</summary>
    public int[] Inventory { get; }

    public NpcState(int id)
    {
        if (id < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "NPC の Id は非負(TDD01 §3.2)。");
        }

        Id = id;
        LiquidFunds = 0;
        Inventory = Array.Empty<int>();
    }
}
