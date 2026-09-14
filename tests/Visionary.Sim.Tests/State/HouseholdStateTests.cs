using System.Reflection;

namespace Visionary.Sim.Tests.State;

/// <summary>
/// 世帯単位への区分変更(TDD01 §3.2「経済主体は世帯である」)の検査。
/// </summary>
/// <remarks>
/// 名前空間を <c>Tests.World</c> にしないのは、<see cref="Visionary.Sim.World"/> 型が
/// 名前空間に隠されるため。
/// </remarks>
public sealed class HouseholdStateTests
{
    private const BindingFlags AllInstanceMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>
    /// <see cref="NpcState"/> が世帯持ちの状態を持たないこと(issue #32 の閉じる条件)。
    /// </summary>
    /// <remarks>
    /// <b>コンパイラも同じ誤りを捕まえる</b> — 廃止したメンバを参照すれば CS1061 になる。
    /// このテストが足すのは「<c>NpcState</c> に同名のメンバを戻してしまう」側の検出である。
    /// 資金と在庫を個人と世帯の両方に持つと帳簿と突合できなくなる(GDD08 §2.2)。
    /// </remarks>
    [Theory]
    [InlineData("LiquidFunds")]
    [InlineData("Inventory")]
    public void NpcStateNoLongerCarriesHouseholdOwnedState(string abolishedMember)
    {
        var names = typeof(NpcState).GetMembers(AllInstanceMembers).Select(member => member.Name);

        Assert.DoesNotContain(abolishedMember, names, StringComparer.Ordinal);
    }

    /// <summary>
    /// 構成員は NpcId 昇順・重複なし・世帯主を含む(GDD08 §2.1 / ADR-0002 の列挙順規約)。
    /// </summary>
    /// <remarks>
    /// 検証を省くと、世帯内の処理順(GDD02 §6.2.1 の購入の決済順)が入力の並び次第になり、
    /// 同じ世界から違う結果が出る。<b>ただし防げるのは構築時だけである</b> —
    /// <c>MemberNpcIds</c> は配列なので、受け取った側が並べ替えれば昇順は崩れる。
    /// </remarks>
    [Theory]
    [InlineData(new[] { 5, 3 }, 3, "昇順でない")]
    [InlineData(new[] { 3, 3, 5 }, 3, "重複がある")]
    [InlineData(new[] { 3, 5 }, 7, "世帯主を含まない")]
    [InlineData(new[] { -1, 3 }, 3, "負の NpcId を含む")]
    public void HouseholdRejectsUnsortedMembers(int[] memberNpcIds, int headNpcId, string why)
    {
        var thrown = Record.Exception(() => new HouseholdState(
            id: 0, districtId: 0, headNpcId: headNpcId, memberNpcIds: memberNpcIds, itemCount: 9));

        Assert.True(
            thrown is ArgumentException,
            $"構成員の検証が効いていない({why})。実際: {thrown?.GetType().Name ?? "例外なし"}");
    }

    /// <summary>
    /// 構成員は<b>複製して</b>持つこと。渡した配列を後から並べ替えても世帯は影響を受けない。
    /// </summary>
    /// <remarks>
    /// 検証を通した後に呼び出し側が元の配列を並べ替える経路を塞ぐのが、複製する理由である。
    /// <c>.ToArray()</c> を落として参照をそのまま持つ実装に戻すとここで落ちる。
    /// <b>この property を受け取った側が並べ替える経路は塞げていない</b>(
    /// <see cref="HouseholdState.MemberNpcIds"/> の doc 参照)。塞げていないものは、
    /// テストにも書かない。
    /// </remarks>
    [Fact]
    public void HouseholdCopiesTheMembersItWasGiven()
    {
        var givenToConstructor = new[] { 3, 5, 8 };

        var household = new HouseholdState(
            id: 0, districtId: 0, headNpcId: 3, memberNpcIds: givenToConstructor, itemCount: 0);

        Array.Reverse(givenToConstructor);

        Assert.Equal(new[] { 3, 5, 8 }, household.MemberNpcIds);
    }

    [Fact]
    public void HouseholdAcceptsSortedMembersIncludingTheHead()
    {
        var household = new HouseholdState(
            id: 0, districtId: 0, headNpcId: 3, memberNpcIds: new[] { 3, 5, 8 }, itemCount: 9);

        Assert.Equal(new[] { 3, 5, 8 }, household.MemberNpcIds);
    }

    /// <summary>
    /// 在庫は2本とも品目数ぶん確保される(TDD01 §3.2)。
    /// </summary>
    /// <remarks>
    /// 片方だけ確保する / <c>itemCount</c> を無視して空配列にすると落ちる。薪のように
    /// 消費財でも生産入力でもある品目(GDD02 §2.2)で、用途別の目標在庫が置けなくなる。
    /// </remarks>
    [Fact]
    public void WorldAllocatesBothInventoriesAtItemCount()
    {
        var world = new World(npcCount: 2, householdCount: 3, itemCount: 9);

        Assert.All(world.Households, household =>
        {
            Assert.Equal(9, household.HouseholdInventory.Length);
            Assert.Equal(9, household.WorkshopInventory.Length);
        });
    }

    /// <summary>所有者別の入れ物が所有者数ぶん用意され、null が無いこと。</summary>
    [Fact]
    public void WorldAllocatesOwnerIndexedContainers()
    {
        var world = new World(npcCount: 4, householdCount: 2, itemCount: 1);

        // 相場知識の所有者は個人、帳簿の所有者は世帯(TDD01 §3.2)。長さが違うのが正しい。
        Assert.Equal(4, world.Knowledge.Length);
        Assert.Equal(2, world.Ledgers.Length);
        Assert.All(world.Knowledge, Assert.NotNull);
        Assert.All(world.Ledgers, Assert.NotNull);
    }

    /// <summary>
    /// 都市外市場の窓口 Id が、ありうるどの世帯 Id よりも大きいこと(TDD01 §3.2)。
    /// </summary>
    /// <remarks>
    /// 売り手 Id 昇順の走査で合成した候補が最後に来ることが、GDD02 §10.2 の
    /// 「実質コストが同値なら都市内の売り手が選ばれる」を支えている。世帯数から導く値に
    /// 変えると、世帯数を増やした実験で既存の世帯 Id と衝突する。
    /// </remarks>
    [Fact]
    public void ExternalMarketSellerIdIsAboveEveryHouseholdId()
    {
        Assert.Equal(int.MaxValue, HouseholdState.ExternalMarketSellerId);
    }
}
