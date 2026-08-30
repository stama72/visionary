namespace Visionary.Sim;

/// <summary>
/// NPC の状態(TDD01 §3.2)。W1 では最小構成。職業・性格・固定支出見込みは W2 で追加する。
/// </summary>
public sealed class NpcState
{
    /// <summary><see cref="World.Npcs"/> の添字と一致する、非負の Id(TDD01 §3.2)。</summary>
    public int Id { get; }

    /// <summary>手元の流動資金。</summary>
    public int LiquidFunds { get; set; }

    /// <summary>在庫。添字 = itemId。品目カタログは W2 以降で確定するため、W1 では空で始める。</summary>
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
