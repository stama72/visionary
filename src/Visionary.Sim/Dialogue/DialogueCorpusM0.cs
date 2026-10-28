using Visionary.Sim.Time;

namespace Visionary.Sim.Dialogue;

/// <summary>
/// M0 の台詞の資料。<b>データだけのファイル</b> — 開発者がロジックに触れずに台詞を書き直すために、
/// 検査・照合・展開のロジックを置かない(W5-01 決定3)。下の <c>G</c> <c>S</c> などは
/// <see cref="DialogueTemplate"/> の引数を短く書くための薄い書き口で、判断を含まない。
/// </summary>
/// <remarks>
/// 書き方の規則(W5-01 §5)。1〜2文・展開前60字以内。時間帯の挨拶語と、月を使う期限を書かない。
/// 開示は品目と数量が要ることだけを言い、理由・背景・期限・予算を言わない。世間話・挨拶・
/// 困りごとは無い、で自分の仕事の不調を語らない。性格の軸はすべて指定なし(決定2)。
/// 取引の成立は 渋々(不満を残す) / 感謝(礼を言う) / 深い感謝(救われたと言う) の差を台詞だけで出す。
/// 並び順は候補の並び順になる(同じ文脈なら、上にあるほど先に候補へ入る)。
/// </remarks>
internal static class DialogueCorpusM0
{
    /// <summary>添字 = <see cref="Item"/> の Id(穀物 木材 鉄鉱石 木炭 小麦粉 薪 パン ビール 工具)。</summary>
    public static readonly ItemWording[] Items =
    {
        new("穀物", "袋"),
        new("木材", "本"),
        new("鉄鉱石", "籠"),
        new("木炭", "俵"),
        new("小麦粉", "袋"),
        new("薪", "束"),
        new("パン", "個"),
        new("ビール", "杯"),
        new("工具", "本"),
    };

    /// <summary>添字 = <c>(int)Season</c>(春 夏 秋 冬)。</summary>
    public static readonly string[] SeasonNames = { "春", "夏", "秋", "冬" };

    public static readonly DialogueTemplate[] Templates =
    {
        // ---- 挨拶(職業) ----
        G("greet.any.-.01", null, "よう、来たか。"),
        G("greet.any.-.02", null, "やあ、顔を見せてくれたな。"),
        G("greet.any.-.03", null, "いらっしゃい。ゆっくりしていきなさい。"),
        G("greet.any.-.04", null, "おや、あんたか。まあ、寄っていきな。"),
        G("greet.any.-.05", null, "ようこそ。通りは賑わっているかい。"),
        G("greet.any.-.06", null, "来たな。立ち話で悪いが、付き合ってくれ。"),
        G("greet.miller.-.01", Occupation.Miller, "粉の匂いのする所で悪いな。まあ入ってくれ。"),
        G("greet.miller.-.02", Occupation.Miller, "水車の音がうるさいが、聞こえるかい。"),
        G("greet.baker.-.01", Occupation.Baker, "焼き立ての匂いにつられて来たかい。"),
        G("greet.baker.-.02", Occupation.Baker, "竈の前で失礼するよ。よく来たね。"),
        G("greet.brewer.-.01", Occupation.Brewer, "樽の間を通ってきたかい。よく来た。"),
        G("greet.brewer.-.02", Occupation.Brewer, "麦の香りがするだろう。まあ座んな。"),
        G("greet.woodworker.-.01", Occupation.Woodworker, "木屑だらけで済まないね。よく来た。"),
        G("greet.woodworker.-.02", Occupation.Woodworker, "鋸の手を止めて迎えるよ。いらっしゃい。"),
        G("greet.smith.-.01", Occupation.Smith, "槌の音の中で失礼する。よく来たな。"),
        G("greet.smith.-.02", Occupation.Smith, "火の側は暑いぞ。それでもよければ入れ。"),

        // ---- 世間話(職業・季節。{season} を差し込める) ----
        S("small.any.-.01", null, null, "市の立つ日は人が多くて、道を歩くのも一苦労だな。"),
        S("small.any.-.02", null, null, "鐘楼の鐘が、今日は妙に遠くまで響いていたよ。"),
        S("small.any.-.03", null, null, "向かいの子供らが、朝から石蹴りに夢中でね。"),
        S("small.any.-.04", null, null, "隣の通りに、新しい井戸が掘られるそうだ。"),
        S("small.any.-.05", null, null, "城壁の外から来た旅の者が、広場で歌っていたよ。"),
        S("small.any.spring.01", null, Season.Spring, "{season}になって、道端の草がずいぶん伸びたな。"),
        S("small.any.summer.01", null, Season.Summer, "{season}は日が長い。夕方になっても、まだ空が明るいよ。"),
        S("small.any.autumn.01", null, Season.Autumn, "{season}は市に実りの品が並ぶ。見て歩くだけでも楽しいぞ。"),
        S("small.any.winter.01", null, Season.Winter, "{season}の朝は水が凍る。手がかじかんで難儀するよ。"),
        S("small.miller.-.01", Occupation.Miller, null, "粉屋の子は、小さい頃から水車の音で眠るものさ。"),
        S("small.miller.-.02", Occupation.Miller, null, "川の流れを見ていると、一日が早く過ぎるよ。"),
        S("small.baker.-.01", Occupation.Baker, null, "パンの値で、その年の景気が分かると言うだろう。"),
        S("small.baker.-.02", Occupation.Baker, null, "朝いちばんの竈の火は、いつ見ても綺麗なものだ。"),
        S("small.brewer.-.01", Occupation.Brewer, null, "ビールは寝かせたぶんだけ、味が丸くなるものさ。"),
        S("small.brewer.-.02", Occupation.Brewer, null, "祭りの日には、樽がいくつ空くか賭けをするんだ。"),
        S("small.woodworker.-.01", Occupation.Woodworker, null, "いい木は切る前から、叩けば音で分かるんだよ。"),
        S("small.woodworker.-.02", Occupation.Woodworker, null, "森の木の香りは、町に来ても忘れられんな。"),
        S("small.smith.-.01", Occupation.Smith, null, "鉄は叩くほど締まる。人もそうだと言うがな。"),
        S("small.smith.-.02", Occupation.Smith, null, "火の色を見れば、鉄の機嫌が分かるものだ。"),

        // ---- 開示(職業。{item} と {qty} の両方を含む。理由・期限・予算は言わない) ----
        D("need.any.-.01", null, "{item}が{qty}、要るんだ。回してもらえないか。"),
        D("need.any.-.02", null, "実は{item}を{qty}ほど探している。心当たりはないか。"),
        D("need.any.-.03", null, "{item}が{qty}あれば助かるんだが、頼めるかね。"),
        D("need.any.-.04", null, "{item}を{qty}、都合してくれる人を探しているんだ。"),
        D("need.miller.-.01", Occupation.Miller, "粉屋の頼みだ。{item}を{qty}、何とかならんか。"),
        D("need.miller.-.02", Occupation.Miller, "{item}が{qty}いる。粉屋に回してくれると有難い。"),
        D("need.baker.-.01", Occupation.Baker, "パン屋の頼みだ。{item}を{qty}、分けてもらえないか。"),
        D("need.baker.-.02", Occupation.Baker, "{item}が{qty}いる。パン屋に届けてくれる人はいないか。"),
        D("need.brewer.-.01", Occupation.Brewer, "醸造屋だが、{item}を{qty}、融通してくれないか。"),
        D("need.brewer.-.02", Occupation.Brewer, "{item}が{qty}いるんだ。醸造屋まで頼めるかい。"),
        D("need.woodworker.-.01", Occupation.Woodworker, "木工の者だが、{item}を{qty}、頼めないだろうか。"),
        D("need.woodworker.-.02", Occupation.Woodworker, "{item}が{qty}いる。木工の家に回してもらえんか。"),
        D("need.smith.-.01", Occupation.Smith, "鍛冶屋だが、{item}を{qty}、都合してもらえんか。"),
        D("need.smith.-.02", Occupation.Smith, "{item}が{qty}いる。鍛冶場まで運んでくれると助かる。"),

        // ---- 困りごとは無い(職業) ----
        N("none.any.-.01", null, "今は特に困っていることはないよ。"),
        N("none.any.-.02", null, "おかげさまで、頼みごとは何もない。"),
        N("none.any.-.03", null, "困りごとかい。今日のところは何も思いつかんな。"),
        N("none.any.-.04", null, "いまは足りているよ。気にかけてくれて有難う。"),
        N("none.miller.-.01", Occupation.Miller, "粉屋は今のところ平穏だよ。頼みは何もない。"),
        N("none.miller.-.02", Occupation.Miller, "水車は回っているし、頼むことは無いな。"),
        N("none.baker.-.01", Occupation.Baker, "パン屋は足りているよ。頼みごとは無いな。"),
        N("none.baker.-.02", Occupation.Baker, "竈の火も絶やさずにいる。頼みは何も無いよ。"),
        N("none.brewer.-.01", Occupation.Brewer, "醸造屋は今のところ何も困っていないよ。"),
        N("none.brewer.-.02", Occupation.Brewer, "樽は揃っているし、頼みごとは無いな。"),
        N("none.woodworker.-.01", Occupation.Woodworker, "木工の家は足りているよ。頼むことは無いな。"),
        N("none.woodworker.-.02", Occupation.Woodworker, "今日のところ、木工に頼みごとは無いね。"),
        N("none.smith.-.01", Occupation.Smith, "鍛冶場は今のところ何も要らんよ。"),
        N("none.smith.-.02", Occupation.Smith, "火は絶やしていないし、頼みは無いな。"),

        // ---- 取引の成立(職業・段階。段階は必ず指定する) ----
        A("thanks.any.reluctant.01", null, Gratitude.Reluctant, "まあ、いいだろう。その値で{item}を{qty}受け取ろう。"),
        A("thanks.any.reluctant.02", null, Gratitude.Reluctant, "高い気もするが、背に腹は代えられん。{item}を{qty}もらう。"),
        A("thanks.any.reluctant.03", null, Gratitude.Reluctant, "……仕方ない。{item}を{qty}、その値で引き取ろう。"),
        A("thanks.miller.reluctant.01", Occupation.Miller, Gratitude.Reluctant, "粉屋の足元を見るとはな。{item}を{qty}、受け取ってやる。"),
        A("thanks.baker.reluctant.01", Occupation.Baker, Gratitude.Reluctant, "パン屋も楽ではないんだ。{item}を{qty}、仕方なく受け取る。"),
        A("thanks.brewer.reluctant.01", Occupation.Brewer, Gratitude.Reluctant, "醸造屋だからと吹っかけたな。{item}を{qty}、受け取ろう。"),
        A("thanks.woodworker.reluctant.01", Occupation.Woodworker, Gratitude.Reluctant, "木工の家に高い値をつけたな。{item}を{qty}、受け取ろう。"),
        A("thanks.smith.reluctant.01", Occupation.Smith, Gratitude.Reluctant, "鍛冶屋だからと足元を見たな。{item}を{qty}、受け取る。"),
        A("thanks.any.grateful.01", null, Gratitude.Grateful, "{item}を{qty}、確かに受け取った。有難う。"),
        A("thanks.any.grateful.02", null, Gratitude.Grateful, "助かったよ。{item}を{qty}、礼を言う。"),
        A("thanks.any.grateful.03", null, Gratitude.Grateful, "ちょうど欲しかったんだ。{item}の{qty}、有難う。"),
        A("thanks.miller.grateful.01", Occupation.Miller, Gratitude.Grateful, "粉屋として礼を言う。{item}を{qty}、確かに受け取った。"),
        A("thanks.baker.grateful.01", Occupation.Baker, Gratitude.Grateful, "パン屋に届けてくれて有難う。{item}を{qty}、確かに。"),
        A("thanks.brewer.grateful.01", Occupation.Brewer, Gratitude.Grateful, "醸造屋として礼を言う。{item}を{qty}、有難う。"),
        A("thanks.woodworker.grateful.01", Occupation.Woodworker, Gratitude.Grateful, "木工の家に回してくれて有難う。{item}を{qty}、確かに。"),
        A("thanks.smith.grateful.01", Occupation.Smith, Gratitude.Grateful, "鍛冶場へ運んでくれて有難う。{item}を{qty}、確かに。"),
        A("thanks.any.deep.01", null, Gratitude.DeepGratitude, "{item}を{qty}も届けてくれるとは。あんたには救われた。"),
        A("thanks.any.deep.02", null, Gratitude.DeepGratitude, "本当に助かった。この恩は忘れんよ。{item}を{qty}、有難う。"),
        A("thanks.any.deep.03", null, Gratitude.DeepGratitude, "{item}が{qty}。あんたは命の恩人だ。何と礼を言えばいいか。"),
        A("thanks.miller.deep.01", Occupation.Miller, Gratitude.DeepGratitude, "粉屋の恩人だ。{item}を{qty}、本当に救われた。"),
        A("thanks.baker.deep.01", Occupation.Baker, Gratitude.DeepGratitude, "パン屋を救ってくれた。{item}を{qty}、恩に着る。"),
        A("thanks.brewer.deep.01", Occupation.Brewer, Gratitude.DeepGratitude, "醸造屋の恩人だ。{item}を{qty}、これで救われた。"),
        A("thanks.woodworker.deep.01", Occupation.Woodworker, Gratitude.DeepGratitude, "木工の家は救われた。{item}を{qty}、この恩は忘れん。"),
        A("thanks.smith.deep.01", Occupation.Smith, Gratitude.DeepGratitude, "鍛冶場が救われた。{item}を{qty}、この恩は忘れんぞ。"),

        // ---- 取引の不成立(職業。{item} のみ) ----
        R("refuse.any.-.01", null, "その値では{item}は買えん。もう少し何とかならんか。"),
        R("refuse.any.-.02", null, "{item}は欲しいが、その値は手持ちを超えている。"),
        R("refuse.miller.-.01", Occupation.Miller, "粉屋の懐では{item}にそんな値は出せん。"),
        R("refuse.miller.-.02", Occupation.Miller, "{item}は要るが、その値では粉屋が立ち行かん。"),
        R("refuse.baker.-.01", Occupation.Baker, "パン屋にその値は無理だ。{item}は諦めるよ。"),
        R("refuse.baker.-.02", Occupation.Baker, "{item}にその値は出せんな。また今度にしてくれ。"),
        R("refuse.brewer.-.01", Occupation.Brewer, "醸造屋にその値は出せんよ。{item}は見送る。"),
        R("refuse.brewer.-.02", Occupation.Brewer, "{item}にその値では、樽ひとつ空けられん。"),
        R("refuse.woodworker.-.01", Occupation.Woodworker, "木工の家にその値は高すぎる。{item}は見送ろう。"),
        R("refuse.woodworker.-.02", Occupation.Woodworker, "{item}にその値は出せん。もう少し安くならんか。"),
        R("refuse.smith.-.01", Occupation.Smith, "鍛冶屋にその値は払えん。{item}は諦める。"),
        R("refuse.smith.-.02", Occupation.Smith, "{item}にその値は高い。鍛冶場の蓄えを超えている。"),
    };

    private static DialogueTemplate G(string id, Occupation? occupation, string text) =>
        new(id, LineKind.Greeting, occupation, null, null, null, text);

    private static DialogueTemplate S(string id, Occupation? occupation, Season? season, string text) =>
        new(id, LineKind.SmallTalk, occupation, season, null, null, text);

    private static DialogueTemplate D(string id, Occupation? occupation, string text) =>
        new(id, LineKind.NeedDisclosure, occupation, null, null, null, text);

    private static DialogueTemplate N(string id, Occupation? occupation, string text) =>
        new(id, LineKind.NoNeed, occupation, null, null, null, text);

    private static DialogueTemplate A(string id, Occupation? occupation, Gratitude gratitude, string text) =>
        new(id, LineKind.TradeAccepted, occupation, null, gratitude, null, text);

    private static DialogueTemplate R(string id, Occupation? occupation, string text) =>
        new(id, LineKind.TradeRefused, occupation, null, null, null, text);
}
