using Visionary.Sim;
using Visionary.Sim.Commands;
using Visionary.Sim.Dialogue;
using Visionary.Sim.Randomness;
using Visionary.Sim.Time;

namespace Visionary.Presentation;

/// <summary>台詞なら Kind と TemplateId を持つ。地の文(売買の結果)なら両方 null。</summary>
public readonly record struct ConversationLine(LineKind? Kind, string? TemplateId, string Text);

/// <summary>世帯へ売る候補。<paramref name="DefaultUnitPrice"/> 単位: 貨幣/個。</summary>
public readonly record struct SellOption(int ItemId, int Held, int DefaultUnitPrice);

/// <summary>
/// 開いている会話(TDD01 §3.10、GDD01 §3.3.1、GDD05 §2.2・§3.1)。<see cref="PlayerSession.Talk"/> だけが作る。
/// </summary>
public sealed class Conversation
{
    private readonly PlayerSession _session;
    private readonly Occupation _occupation;
    private readonly Season _season;
    private readonly Tick _openedAt;
    private readonly List<ConversationLine> _lines = [];
    private Gratitude? _pendingChime;

    internal Conversation(
        PlayerSession session, int householdId, int speakerNpcId, Occupation occupation, IReadOnlyList<Need> needs)
    {
        _session = session;
        HouseholdId = householdId;
        SpeakerNpcId = speakerNpcId;
        _occupation = occupation;
        DisclosedNeeds = needs;
        _openedAt = session.Host.Now;
        _season = GameDate.FromTick(_openedAt).Season;
        Title = $"{Labels.Of(occupation)}(世帯 {householdId})";

        // 会話の頭は1本の列で続けて引く(GDD01 §3.3.1)。
        var random = DialogueSelector.OpenConversation(session.MasterSeed, _openedAt, speakerNpcId);

        AddSpeech(ref random, LineKind.Greeting, null, null, null, null);
        AddSpeech(ref random, LineKind.SmallTalk, null, null, null, null);

        if (needs.Count == 0)
        {
            AddSpeech(ref random, LineKind.NoNeed, null, null, null, null);
        }
        else
        {
            for (int i = 0; i < needs.Count; i++)
            {
                AddSpeech(
                    ref random, LineKind.NeedDisclosure, null,
                    i == 0 ? DisclosureOrder.First : DisclosureOrder.Continued,
                    needs[i].ItemId, needs[i].Quantity);
            }
        }
    }

    public int HouseholdId { get; }

    /// <summary>世帯の構成員の最小 NpcId。</summary>
    public int SpeakerNpcId { get; }

    public string Title { get; }

    public IReadOnlyList<Need> DisclosedNeeds { get; }

    /// <summary>追記のみ。</summary>
    public IReadOnlyList<ConversationLine> Lines => _lines;

    /// <summary>話し手の表情の一語。売りの結果(成立・予算超過)だけが変える。</summary>
    public string Expression { get; private set; } = "ふつう";

    /// <summary>鳴らす効果音の段階を1回だけ返す。保留が無ければ null。</summary>
    public Gratitude? TakeChime()
    {
        var pending = _pendingChime;

        _pendingChime = null;
        return pending;
    }

    public IReadOnlyList<SellOption> SellOptions()
    {
        var inventory = _session.Host.Query.Player().Inventory;
        var result = new List<SellOption>();

        for (int itemId = 0; itemId < inventory.Count; itemId++)
        {
            if (inventory[itemId] >= 1)
            {
                int last = _session.LastPurchaseUnitPrice(itemId);
                result.Add(new SellOption(itemId, inventory[itemId], last == 0 ? 1 : last));
            }
        }

        return result;
    }

    public IReadOnlyList<OfferView> BuyOptions()
    {
        var result = new List<OfferView>();

        foreach (var offer in _session.Host.Query.Offers())
        {
            if (offer.HouseholdId == HouseholdId)
            {
                result.Add(offer);
            }
        }

        return result;
    }

    public Result Sell(int itemId, int unitPrice, int quantity)
    {
        _session.EnsureOpen(this);

        var result = _session.Host.Execute(new SellToHouseholdCommand(HouseholdId, itemId, unitPrice, quantity));

        // DrainEvents を呼ぶのはここだけ。結果を問わず Execute の直後に1回。
        AppendJournal(_session.Host.DrainEvents());

        if (result.Status == CommandStatus.Accepted)
        {
            // 同じ鍵で列を開き直し、これまでに言った台詞の行数だけ引き捨てる(RandomSequence は ref struct で持てない)。
            var random = ReopenAfterSpokenLines();
            var gratitude = result.Gratitude!.Value;
            AddSpeech(ref random, LineKind.TradeAccepted, gratitude, null, itemId, result.Quantity);

            Expression = gratitude switch
            {
                Gratitude.Reluctant => "渋い顔",
                Gratitude.Grateful => "ほっとした顔",
                Gratitude.DeepGratitude => "晴れやかな顔",
                _ => throw new ArgumentOutOfRangeException(nameof(result), gratitude, "知らない段階。"),
            };
            _pendingChime = gratitude;
        }
        else if (result.Status == CommandStatus.OverBudget)
        {
            var random = ReopenAfterSpokenLines();
            AddSpeech(ref random, LineKind.TradeRefused, null, null, itemId, null);
            Expression = "首を振る";
        }

        _lines.Add(new ConversationLine(null, null, Notes.Trade(PlayerTradeKind.SellToHousehold, itemId, result)));
        return result;
    }

    public Result Buy(int itemId, int quantity)
    {
        _session.EnsureOpen(this);

        var result = _session.Host.Execute(new BuyFromHouseholdCommand(HouseholdId, itemId, quantity));

        if (result.Status == CommandStatus.Accepted)
        {
            _session.RecordPurchase(itemId, result.UnitPrice);
        }

        _lines.Add(new ConversationLine(null, null, Notes.Trade(PlayerTradeKind.BuyFromHousehold, itemId, result)));
        return result;
    }

    private void AppendJournal(IReadOnlyList<DomainEvent> events)
    {
        foreach (var e in events)
        {
            if (e is not PlayerTradeExecuted { Kind: PlayerTradeKind.SellToHousehold } trade)
            {
                continue;
            }

            NeedReason? reason = null;

            foreach (var need in DisclosedNeeds)
            {
                if (need.Id == trade.NeedId)
                {
                    reason = need.ReasonCode;
                    break;
                }
            }

            if (reason == null)
            {
                throw new InvalidOperationException($"約定の Need #{trade.NeedId} がこの会話で開示した Need に無い。");
            }

            var gratitude = trade.Gratitude!.Value;
            var occupation = _session.Host.Query.Households()[trade.CounterpartyId].Occupation;
            string text = Notes.Journal(
                GameDate.FromTick(trade.At), occupation, trade.CounterpartyId, reason.Value, trade.ItemId, gratitude);

            _session.AppendJournal(new JournalEntry(trade.At, trade.CounterpartyId, trade.NeedId, gratitude, text));
        }
    }

    private RandomSequence ReopenAfterSpokenLines()
    {
        var random = DialogueSelector.OpenConversation(_session.MasterSeed, _openedAt, SpeakerNpcId);
        int spoken = _lines.Count(l => l.Kind != null);

        for (int i = 0; i < spoken; i++)
        {
            random.NextUInt64();
        }

        return random;
    }

    private void AddSpeech(
        ref RandomSequence random, LineKind kind, Gratitude? gratitude,
        DisclosureOrder? order, int? itemId, int? quantity)
    {
        var line = DialogueSelector.Say(
            _session.Corpus, _session.Memory, ref random, SpeakerNpcId, kind,
            new DialogueContext(_occupation, _season, null, gratitude, order, itemId, quantity));

        _lines.Add(new ConversationLine(line.Kind, line.TemplateId, line.Text));
    }
}
