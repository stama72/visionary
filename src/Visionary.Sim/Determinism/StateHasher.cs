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
/// <item><description>各区画の先頭に区画タグ(<see cref="Section"/>、int)と要素数(int)を書く。
/// <see cref="Section.Clock"/> のようにスカラー1個しか持たない区画も要素数=1として同じ形式に
/// 揃える。特例を作らないことで、区画ごとに違う読み方を覚えずに済む。</description></item>
/// <item><description><b>順序非依存の畳み込み(XOR・加算)は使わない。</b>単一の
/// <see cref="XxHash64"/> インスタンスに前から順に <c>Append</c> する(§3.8)。</description></item>
/// </list>
/// </remarks>
public static class StateHasher
{
    // 値は仕様である。振り直してはならない(RandomStream と同じ理由)。
    // 0 を使わないのは、既定値の Section が有効な区画に見えるのを避けるため。
    // W2 で区画を追加するときは、既存の値を動かさずに末尾へ足す。
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

        // 配列の添字順 = Id 昇順(ADR-0002)。
        WriteSectionHeader(hasher, buffer, Section.Npcs, world.Npcs.Length);
        foreach (var npc in world.Npcs)
        {
            WriteInt32(hasher, buffer, npc.Id);
            WriteInt32(hasher, buffer, npc.LiquidFunds);
            WriteInt32(hasher, buffer, npc.Inventory.Length);

            foreach (var quantity in npc.Inventory)
            {
                WriteInt32(hasher, buffer, quantity);
            }
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
            WriteInt32(hasher, buffer, need.TargetNpcId);
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
        WriteSectionHeader(hasher, buffer, Section.Knowledge, world.Knowledge.Count);
        foreach (var observation in world.Knowledge)
        {
            WriteInt32(hasher, buffer, observation.ItemId);
            WriteInt32(hasher, buffer, observation.LocationId);
            WriteInt32(hasher, buffer, observation.Price);
            WriteInt64(hasher, buffer, observation.ObservedAt.Value);
            WriteInt32(hasher, buffer, (int)observation.Source);
        }

        WriteSectionHeader(hasher, buffer, Section.Ledgers, world.Ledgers.Count);
        foreach (var entry in world.Ledgers)
        {
            WriteInt32(hasher, buffer, entry.CounterpartyId);
            WriteInt32(hasher, buffer, entry.ItemId);
            WriteInt32(hasher, buffer, entry.Quantity);
            WriteInt32(hasher, buffer, entry.UnitPrice);
            WriteInt64(hasher, buffer, entry.OccurredAt.Value);
            WriteInt32(hasher, buffer, (int)entry.Terms);
            WriteInt64(hasher, buffer, entry.CreditDueAt.Value);
        }

        // EventLog は含めない(§3.8 の除外表)。意思決定に関与せず、追記専用で巨大。

        return hasher.GetCurrentHashAsUInt64();
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
