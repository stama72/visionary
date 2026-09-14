using Visionary.Sim.Time;

namespace Visionary.Sim;

/// <summary>現金取引か、返済期日付きの掛け売りか(GDD01 §4.4)。</summary>
public enum LedgerTerms
{
    Cash = 0,
    Credit = 1,
}

/// <summary>
/// 帳簿(取引履歴、GDD01 §4.4 / TDD01 §3.2)の1行。約定価格の真実はここが持つ
/// (TDD01 §3.2)。<b>所有者は世帯である</b> — 資金の増減と突合するため、個人別に持つと
/// 一致しない(GDD08 §2.2)。<see cref="World.Ledgers"/> は世帯 Id 別に持つ。
/// </summary>
/// <remarks>
/// GDD01 §4.4 の <c>terms: Cash | Credit(返済期日)</c> は Credit のときだけ
/// 返済期日を伴う。フィールドは int/long/enum のみという制約の下、共用体ではなく
/// <see cref="Terms"/>(判別子)と <see cref="CreditDueAt"/>(Credit のときのみ意味を持つ)
/// に分けて表す。Cash のとき <see cref="CreditDueAt"/> は未使用。
/// </remarks>
public readonly record struct LedgerEntry
{
    /// <summary>
    /// 取引相手の<b>世帯</b> Id。都市外市場が相手の取引では
    /// <see cref="HouseholdState.ExternalMarketSellerId"/>(GDD02 §10)。
    /// </summary>
    public int CounterpartyId { get; init; }

    public int ItemId { get; init; }

    public int Quantity { get; init; }

    public int UnitPrice { get; init; }

    /// <summary>取引が成立した時刻。</summary>
    public Tick OccurredAt { get; init; }

    public LedgerTerms Terms { get; init; }

    /// <summary><see cref="Terms"/> が <see cref="LedgerTerms.Credit"/> のときのみ意味を持つ返済期日。</summary>
    public Tick CreditDueAt { get; init; }
}
