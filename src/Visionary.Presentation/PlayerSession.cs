using Visionary.Sim;
using Visionary.Sim.Commands;
using Visionary.Sim.Dialogue;

namespace Visionary.Presentation;

/// <summary>
/// プレイヤーの操作の窓口(TDD01 §3.10「会話と売買」、GDD05 §1.4・§2.2)。<c>Game</c> は
/// <c>Host.Execute</c> も <c>pump.Paused</c> も直接触らず、すべてここを通す。
/// <b><c>pump.Paused</c> を書くのは <see cref="Talk"/>・<see cref="OpenWindow"/>・<see cref="Close"/> だけ。</b>
/// </summary>
public sealed class PlayerSession
{
    private readonly WorldDefinition _definition;
    private readonly TickPump _pump;

    // 品目ごとの「この起動で最後に買った単価」。単位: 貨幣/個。0 = 買っていない。添字 = 品目 Id。
    private readonly int[] _lastPurchaseUnitPrice;

    public PlayerSession(SimHost host, WorldDefinition definition, TickPump pump, long masterSeed, DialogueCorpus corpus)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(pump);
        ArgumentNullException.ThrowIfNull(corpus);

        Host = host;
        _definition = definition;
        _pump = pump;
        MasterSeed = masterSeed;
        Corpus = corpus;
        Memory = new DialogueMemory();
        _lastPurchaseUnitPrice = new int[definition.ItemCount];
    }

    public SimHost Host { get; }

    public Conversation? Conversation { get; private set; }

    public WindowCounter? Window { get; private set; }

    public bool IsPanelOpen => Conversation != null || Window != null;

    internal long MasterSeed { get; }

    internal DialogueCorpus Corpus { get; }

    internal DialogueMemory Memory { get; }

    public IReadOnlyList<DistrictEntry> Entries()
    {
        var player = Host.Query.Player();
        var result = new List<DistrictEntry>();

        if (player.Location.InTransit)
        {
            return result;
        }

        // Households() は世帯 Id 昇順。
        foreach (var household in Host.Query.Households())
        {
            if (household.DistrictId == player.Location.DistrictId)
            {
                result.Add(new DistrictEntry(
                    DistrictEntryKind.Household, household.Id, $"{Labels.Of(household.Occupation)}(世帯 {household.Id})"));
            }
        }

        if (player.Location.DistrictId == District.ExternalMarketDistrictId)
        {
            result.Add(new DistrictEntry(DistrictEntryKind.Window, -1, "窓口"));
        }

        return result;
    }

    /// <summary>パネルが開いていれば何もせず null(<c>Execute</c> を呼ばない)。</summary>
    public Result? Move(int districtId)
    {
        if (IsPanelOpen)
        {
            return null;
        }

        return Host.Execute(new MoveCommand(districtId));
    }

    /// <summary>
    /// <c>AskNeeds</c> が Accepted のときだけ会話を開いて時間を止める。開く条件をここで判定し直さない
    /// (拒否の検査はシムが持つ)。
    /// </summary>
    public Result Talk(int householdId)
    {
        EnsureNothingOpen();

        var result = Host.Execute(new AskNeedsCommand(householdId));

        if (result.Status != CommandStatus.Accepted)
        {
            return result;
        }

        var household = Host.Query.Households()[householdId];
        int speaker = household.MemberNpcIds.Min();

        Conversation = new Conversation(this, householdId, speaker, household.Occupation, result.DisclosedNeeds);
        _pump.Paused = true;
        return result;
    }

    /// <summary>条件を見ずに窓口を開く(拒否は売買の結果として返る)。</summary>
    public WindowCounter OpenWindow()
    {
        EnsureNothingOpen();

        Window = new WindowCounter(this, _definition);
        _pump.Paused = true;
        return Window;
    }

    public void Close()
    {
        if (!IsPanelOpen)
        {
            return;
        }

        Conversation = null;
        Window = null;
        _pump.Paused = false;
    }

    internal int LastPurchaseUnitPrice(int itemId) => _lastPurchaseUnitPrice[itemId];

    /// <summary>範囲外の品目 Id は記録しない(Accepted の買い物の品目だけが来る)。</summary>
    internal void RecordPurchase(int itemId, int unitPrice) => _lastPurchaseUnitPrice[itemId] = unitPrice;

    /// <summary>閉じた後の古いパネルから売買させない。</summary>
    internal void EnsureOpen(object panel)
    {
        if (!ReferenceEquals(panel, Conversation) && !ReferenceEquals(panel, Window))
        {
            throw new InvalidOperationException("閉じたパネルでは売買できない。");
        }
    }

    private void EnsureNothingOpen()
    {
        if (IsPanelOpen)
        {
            throw new InvalidOperationException("パネルが開いている間は開けない。");
        }
    }
}
