using System.Buffers.Binary;
using System.IO.Hashing;

namespace Visionary.Sim.Determinism;

/// <summary>
/// <see cref="World"/> の状態ハッシュ(TDD01 §3.8)。同一シード2プロセス実行の一致検証に使う。
/// </summary>
/// <remarks>
/// <para>
/// <b>バイト列化の規約。</b>TDD01 §3.8 は「選んだ方法を1か所にコメントで固定する」としている。
/// その1か所がここである:
/// </para>
/// <list type="bullet">
/// <item><description><c>int</c> は4バイト、<c>long</c> は8バイト、いずれも
/// <b>リトルエンディアン固定</b>。<see cref="BinaryPrimitives"/> の Write*LittleEndian を使う。
/// <see cref="BitConverter"/> は実行環境のエンディアンに従うため使わない。</description></item>
/// <item><description><c>enum</c> は基になる <c>int</c> として書く。</description></item>
/// <item><description><c>Tick</c> は <c>Tick.Value</c>(<c>long</c>)として書く。</description></item>
/// <item><description>各区分の先頭に区分タグ(<see cref="Section"/>、int)と要素数(int)を書く。
/// <see cref="Section.Clock"/> のようにスカラー1個しか持たない区分も要素数=1として同じ形式に
/// 揃える。特例を作らないことで、区分ごとに違う読み方を覚えずに済む。</description></item>
/// <item><description><b>所有者別の区分(<see cref="World.Knowledge"/> /
/// <see cref="World.Ledgers"/>)の要素数は「所有者数」であって「総件数」ではない。</b>
/// 所有者ごとに所有者Id(int)と件数(int)を前置してから中身を書く。所有者Id は配列の添字と
/// 一致するが、<see cref="World.Npcs"/> が <c>npc.Id</c> を書いているのと同じ理由で明示する。</description></item>
/// <item><description>可変長の配列(在庫・構成員)は長さ(int)を前置してから各要素を書く。</description></item>
/// <item><description><b>順序非依存の畳み込み(XOR・加算)は使わない。</b>単一の
/// <see cref="XxHash64"/> インスタンスに前から順に <c>Append</c> する(§3.8)。</description></item>
/// </list>
/// </remarks>
public static class StateHasher
{
    // 値は仕様である。振り直してはならない(RandomStream と同じ理由)。
    // 0 を使わないのは、既定値の Section が有効な区分に見えるのを避けるため。
    // 区分を追加するときは、既存の値を動かさずに末尾へ足す(TDD01 §3.8)。
    // 「区分」は World の内訳。都市の空間的な「区画」(GDD02 §4.3)とは別概念(TDD01 §3.2)。
    private enum Section
    {
        Clock = 1,
        Npcs = 2,
        Market = 3,
        TrustLedger = 4,
        Needs = 5,
        Promises = 6,
        Knowledge = 7,
        Ledgers = 8,
        Households = 9,
    }

    /// <summary>World の状態ハッシュ(TDD01 §3.8)。</summary>
    public static ulong Compute(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        var hasher = new XxHash64();
        Span<byte> buffer = stackalloc byte[8];

        // Now を最初に書く。含めないと「同じ状態に違う時刻で到達した」を検出できない(§3.8)。
        WriteSectionHeader(hasher, buffer, Section.Clock, elementCount: 1);
        WriteInt64(hasher, buffer, world.Now.Value);

        // 配列の添字順 = Id 昇順(ADR-0002)。個人に残るのは相場知識・信用・熟練度・階層だけで、
        // 流動資金と在庫は Households(区分9)へ移った(TDD01 §3.2)。
        WriteSectionHeader(hasher, buffer, Section.Npcs, world.Npcs.Length);
        foreach (var npc in world.Npcs)
        {
            WriteInt32(hasher, buffer, npc.Id);
            WriteInt32(hasher, buffer, npc.HouseholdId);
            WriteInt32(hasher, buffer, (int)npc.Rank);
            WriteInt32(hasher, buffer, npc.SkillPermille);
        }

        // SortedDictionary の列挙順(MarketKey.CompareTo = ItemId → SellerId)はキー順で決定的。
        WriteSectionHeader(hasher, buffer, Section.Market, world.Market.Count);
        foreach (var (key, price) in world.Market)
        {
            WriteInt32(hasher, buffer, key.ItemId);
            WriteInt32(hasher, buffer, key.SellerId);
            WriteInt32(hasher, buffer, price);
        }

        // 同上(TrustKey.CompareTo = From → To)。
        WriteSectionHeader(hasher, buffer, Section.TrustLedger, world.TrustLedger.Count);
        foreach (var (key, score) in world.TrustLedger)
        {
            WriteInt32(hasher, buffer, key.From);
            WriteInt32(hasher, buffer, key.To);
            WriteInt32(hasher, buffer, score.Value);
            WriteInt64(hasher, buffer, score.LastMet.Value);
        }

        // List の格納順そのまま。ソートも正規化もしない — 列挙順の破れ自体が検出したいバグ(§3.8)。
        WriteSectionHeader(hasher, buffer, Section.Needs, world.Needs.Count);
        foreach (var need in world.Needs)
        {
            WriteInt32(hasher, buffer, need.TypeCode);
            WriteInt32(hasher, buffer, need.TargetHouseholdId);
            WriteInt32(hasher, buffer, need.ItemId);
            WriteInt32(hasher, buffer, need.Quantity);
            WriteInt64(hasher, buffer, need.Deadline.Value);
            WriteInt32(hasher, buffer, need.Urgency);
            WriteInt32(hasher, buffer, need.ReasonCode);
        }

        WriteSectionHeader(hasher, buffer, Section.Promises, world.Promises.Count);
        foreach (var promise in world.Promises)
        {
            WriteInt32(hasher, buffer, promise.NeedIndex);
            WriteInt64(hasher, buffer, promise.T0.Value);
            WriteInt64(hasher, buffer, promise.T1.Value);
            WriteInt32(hasher, buffer, promise.B);
            WriteInt32(hasher, buffer, (int)promise.State);
        }

        // Knowledge 全部を含める(§3.8)。Rumor(§3.3-9)の伝播順の破れが最も起きやすい系統。
        // 要素数は所有者数(= NPC 数)。所有者ごとに NpcId と件数を前置する。
        WriteSectionHeader(hasher, buffer, Section.Knowledge, world.Knowledge.Length);
        for (int npcId = 0; npcId < world.Knowledge.Length; npcId++)
        {
            var observations = world.Knowledge[npcId];

            WriteInt32(hasher, buffer, npcId);
            WriteInt32(hasher, buffer, observations.Count);

            // List の格納順そのまま。所有者の中でもソートも正規化もしない(§3.8)。
            foreach (var observation in observations)
            {
                WriteInt32(hasher, buffer, observation.ItemId);
                WriteInt32(hasher, buffer, observation.LocationId);
                WriteInt32(hasher, buffer, observation.Price);
                WriteInt32(hasher, buffer, observation.SellerId);
                WriteInt64(hasher, buffer, observation.ObservedAt.Value);
                WriteInt32(hasher, buffer, (int)observation.Source);
            }
        }

        // Knowledge と添字の意味が違う — あちらは NpcId、こちらは世帯Id(TDD01 §3.2)。
        WriteSectionHeader(hasher, buffer, Section.Ledgers, world.Ledgers.Length);
        for (int householdId = 0; householdId < world.Ledgers.Length; householdId++)
        {
            var entries = world.Ledgers[householdId];

            WriteInt32(hasher, buffer, householdId);
            WriteInt32(hasher, buffer, entries.Count);

            foreach (var entry in entries)
            {
                WriteInt32(hasher, buffer, entry.CounterpartyId);
                WriteInt32(hasher, buffer, entry.ItemId);
                WriteInt32(hasher, buffer, entry.Quantity);
                WriteInt32(hasher, buffer, entry.UnitPrice);
                WriteInt64(hasher, buffer, entry.OccurredAt.Value);
                WriteInt32(hasher, buffer, (int)entry.Terms);
                WriteInt64(hasher, buffer, entry.CreditDueAt.Value);
            }
        }

        // 配列の添字順 = 世帯Id 昇順(ADR-0002)。区画Id を含めるのは、不変だが初期配置の一部で
        // あり、シードから決まる世界の同一性に属するため(§3.8)。破産中フラグを含めるのは、
        // GDD02 §6.2.2 の②(値付けで原価下限を 500‰ へ下げる)と④のゲートを駆動するため。
        WriteSectionHeader(hasher, buffer, Section.Households, world.Households.Length);
        foreach (var household in world.Households)
        {
            WriteInt32(hasher, buffer, household.Id);
            WriteInt32(hasher, buffer, household.DistrictId);
            WriteInt32(hasher, buffer, (int)household.Occupation);
            WriteInt32(hasher, buffer, household.HeadNpcId);

            WriteInt32(hasher, buffer, household.MemberNpcIds.Length);

            foreach (int memberNpcId in household.MemberNpcIds)
            {
                WriteInt32(hasher, buffer, memberNpcId);
            }

            WriteInt32(hasher, buffer, household.LiquidFunds);

            // 世帯在庫と工房在庫は別勘定である(TDD01 §3.2)。薪のように両方に現れる品目が
            // あるため、2本を畳むと GDD02 §8.2.1 の目標在庫が一意に決まらない。
            WriteInt32Array(hasher, buffer, household.HouseholdInventory);
            WriteInt32Array(hasher, buffer, household.WorkshopInventory);

            WriteInt32(hasher, buffer, household.IsBankrupt);

            // 要素の末尾に足す(区分タグを末尾に足すのと同じ規律。TDD01 §3.8)。
            WriteInt32Array(hasher, buffer, household.PurchaseUnitCostAverage);
        }

        // EventLog は含めない(§3.8 の除外表)。意思決定に関与せず、追記専用で巨大。

        return hasher.GetCurrentHashAsUInt64();
    }

    /// <summary>
    /// 長さを前置してから各要素を書く。在庫(添字 = itemId、GDD02 §2.2)と
    /// 仕入れ移動平均単価の両方に使う共通の書式。
    /// </summary>
    private static void WriteInt32Array(XxHash64 hasher, Span<byte> buffer, int[] values)
    {
        WriteInt32(hasher, buffer, values.Length);

        foreach (int value in values)
        {
            WriteInt32(hasher, buffer, value);
        }
    }

    private static void WriteSectionHeader(
        XxHash64 hasher, Span<byte> buffer, Section section, int elementCount)
    {
        WriteInt32(hasher, buffer, (int)section);
        WriteInt32(hasher, buffer, elementCount);
    }

    private static void WriteInt32(XxHash64 hasher, Span<byte> buffer, int value)
    {
        var slice = buffer[..4];
        BinaryPrimitives.WriteInt32LittleEndian(slice, value);
        hasher.Append(slice);
    }

    private static void WriteInt64(XxHash64 hasher, Span<byte> buffer, long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        hasher.Append(buffer);
    }
}
