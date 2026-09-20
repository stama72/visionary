using Visionary.Sim.Systems;

namespace Visionary.Sim.Tests.Systems;

/// <summary>
/// <see cref="EffectivePrice"/>(GDD06 §2 / GDD01 §2.2 効果1、#98 タスク仕様のテスト表)の検査。
/// </summary>
public sealed class EffectivePriceTests
{
    private const int AlphaPermille = 200; // ‰(GDD01 §2.2 効果1の校正値。信用100で2割引)

    /// <summary>
    /// 【核心】テスト表 #5。α=200・信用0 → 提示価格そのまま(100→100)。信用100 →
    /// ApplyPermille(100,800)=80。信用50 → ApplyPermille(100,900)=90。提示価格1・信用100 → 1(0にならない)。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-20)。</b><c>EffectivePrice.Calculate</c> の
    /// <c>CeilDiv(trustDiscountPermille × trust, 100)</c> の <c>÷ 100</c> を落とす変異
    /// (α‰×信用をそのまま割引‰として使う)を当てたところ、信用100のケース
    /// <c>Assert.Equal(80, EffectivePrice.Calculate(100, 100, 200))</c> が例外
    /// (係数 <c>1000 − 20000 = −19000</c> で <c>ApplyPermille</c> の <c>checked</c> が
    /// <c>OverflowException</c>)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void EffectivePriceDiscountsByTrust()
    {
        Assert.Equal(100, EffectivePrice.Calculate(offerPrice: 100, trust: 0, AlphaPermille));
        Assert.Equal(80, EffectivePrice.Calculate(offerPrice: 100, trust: 100, AlphaPermille));
        Assert.Equal(90, EffectivePrice.Calculate(offerPrice: 100, trust: 50, AlphaPermille));

        // 提示価格1・信用100でも0にならない(ApplyPermilleがCeilDivのため)。
        Assert.Equal(1, EffectivePrice.Calculate(offerPrice: 1, trust: 100, AlphaPermille));
    }

    /// <summary>
    /// 【核心】テスト表 #6。信用101と−1で投げる。提示価格0でも投げる。
    /// </summary>
    /// <remarks>
    /// <b>変異の実測(2026-09-20)。</b><c>EffectivePrice.Calculate</c> の
    /// <c>trust is &lt; 0 or &gt; 100</c> の値域検査を丸ごと削る変異を当てたところ、
    /// <c>Assert.Throws&lt;ArgumentOutOfRangeException&gt;(() =&gt; EffectivePrice.Calculate(100, 101, 200))</c>
    /// が例外なし(実際値 <c>ApplyPermille(100, 798)=80</c>相当の値が返る。信用を‰と取り違えても
    /// 黙って負の実効価格になりうる経路)で失敗した(赤を確認)。変異を戻して緑に復帰させた。
    /// </remarks>
    [Fact]
    public void EffectivePriceRejectsTrustOutsideZeroToHundred()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EffectivePrice.Calculate(offerPrice: 100, trust: 101, AlphaPermille));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EffectivePrice.Calculate(offerPrice: 100, trust: -1, AlphaPermille));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EffectivePrice.Calculate(offerPrice: 0, trust: 0, AlphaPermille));
    }
}
