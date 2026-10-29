using Visionary.Sim.Time;

namespace Visionary.Sim.Dialogue;

/// <summary>
/// M0 の台詞の資料。<b>データだけのファイル</b> — 開発者がロジックに触れずに台詞を書き直すために、
/// 検査・照合・展開のロジックを置かない(W5-01 決定3)。下の <c>G</c> <c>S</c> などは
/// <see cref="DialogueTemplate"/> の引数を短く書くための薄い書き口で、判断を含まない。
/// </summary>
/// <remarks>
/// 書き方の規則は GDD01 §3.3.1「書き方の規則」が持つ。ここでは書き換えるときに踏みやすい点だけを挙げる。
/// 開示は 1件目(<c>D</c>)と続き(<c>C</c>)の2つの型を持つ。続きは前の行に繋ぐ短いつなぎで、
/// 展開前25字以内。職業の差は職業名の名乗りではなく口調で出す。{qty} に量を強調・ぼかす語を付けない。
/// 取引の成立は値段の妥当さで語り(渋々 = 高い / 感謝 = 妥当 / 深い感謝 = この値で譲ってくれるのか)、
/// 救済の語を使わない。取引の不成立は理由・再提示の誘いを言わない。性格の軸はすべて指定なし(決定2)。
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
        G("greet.any.-.07", null, "おお、あんたか。変わりはないかい。"),
        G("greet.any.-.08", null, "よく来てくれた。ちょうど一息ついたところだよ。"),
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

        // ---- 世間話(職業・季節。{season} を差し込める。習慣・格言・季節の様子を言い、1回きりの出来事を言わない) ----
        S("small.any.-.01", null, null, "市の立つ日は人が多くて、道を歩くのも一苦労だな。"),
        S("small.any.-.02", null, null, "朝の鐘が鳴ると、通りの店が一斉に戸を開けるのさ。"),
        S("small.any.-.03", null, null, "子供らは日が暮れるまで、通りで石蹴りをして遊ぶものだ。"),
        S("small.any.-.04", null, null, "井戸端に行けば、町の噂は一通り耳に入るものさ。"),
        S("small.any.-.05", null, null, "旅の者は広場で歌い、日が傾くと宿へ戻っていくものだ。"),
        S("small.any.-.06", null, null, "雨上がりの石畳はよく滑る。足元に気を付けなよ。"),
        S("small.any.-.07", null, null, "年寄りの言うことは、だいたい当たるものさ。"),
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

        // ---- 開示・1件目(職業。{item} と {qty} の両方を含む。理由・期限・予算は言わない。職業の差は口調で出す) ----
        D("need.any.first.01", null, "{item}が{qty}、要るんだ。回してもらえないか。"),
        D("need.any.first.02", null, "実は{item}を{qty}、探している。心当たりはないか。"),
        D("need.any.first.03", null, "{item}が{qty}あれば助かるんだが、頼めるかね。"),
        D("need.any.first.04", null, "{item}を{qty}、都合してくれる人を探しているんだ。"),
        D("need.miller.first.01", Occupation.Miller, "{item}を{qty}、回してくれんか。頼むよ。"),
        D("need.miller.first.02", Occupation.Miller, "{item}が{qty}いるんだ。聞いてやってくれ。"),
        D("need.baker.first.01", Occupation.Baker, "{item}を{qty}、分けてもらえると嬉しいんだけどね。"),
        D("need.baker.first.02", Occupation.Baker, "{item}が{qty}いる。朝のうちに回してもらえると助かる。"),
        D("need.brewer.first.01", Occupation.Brewer, "{item}を{qty}、融通してくれないか。頼むぜ。"),
        D("need.brewer.first.02", Occupation.Brewer, "{item}が{qty}いるんだ。誰か回してくれよ。"),
        D("need.woodworker.first.01", Occupation.Woodworker, "{item}を{qty}。頼めるか。"),
        D("need.woodworker.first.02", Occupation.Woodworker, "{item}が{qty}いる。回してもらえんか。"),
        D("need.smith.first.01", Occupation.Smith, "{item}を{qty}だ。都合してもらえんか。"),
        D("need.smith.first.02", Occupation.Smith, "{item}が{qty}いる。運んでくれると助かる。"),

        // ---- 開示・続き(職業指定なし。前の行に繋ぐ短いつなぎ。展開前25字以内) ----
        C("need.any.cont.01", "それと、{item}を{qty}。"),
        C("need.any.cont.02", "あとは{item}を{qty}だな。"),
        C("need.any.cont.03", "ついでに{item}を{qty}、頼めるか。"),
        C("need.any.cont.04", "それから、{item}を{qty}。"),
        C("need.any.cont.05", "もう一つ、{item}を{qty}だ。"),
        C("need.any.cont.06", "それに{item}を{qty}、要るんだ。"),
        C("need.any.cont.07", "あと、{item}を{qty}頼む。"),

        // ---- 困りごとは無い(職業) ----
        N("none.any.-.01", null, "今は特に困っていることはないよ。"),
        N("none.any.-.02", null, "おかげさまで、頼みごとは何もない。"),
        N("none.any.-.03", null, "困りごとかい。今日のところは何も思いつかんな。"),
        N("none.any.-.04", null, "いまは足りているよ。気にかけてくれて有難う。"),
        N("none.any.-.05", null, "特に何も無いな。気にしないでくれ。"),
        N("none.any.-.06", null, "頼みごとか。いまのところ思い当たらんよ。"),
        N("none.any.-.07", null, "ありがたいことに、今日は何も要らないんだ。"),
        N("none.any.-.08", null, "困りごとは無いよ。また今度聞いておくれ。"),
        N("none.miller.-.01", Occupation.Miller, "ここは落ち着いたものさ。頼みは何も無い。"),
        N("none.miller.-.02", Occupation.Miller, "水車は回っているし、頼むことは無いな。"),
        N("none.baker.-.01", Occupation.Baker, "うちは足りているよ。頼みごとは無いね。"),
        N("none.baker.-.02", Occupation.Baker, "竈の火も絶やさずにいる。頼みは何も無いよ。"),
        N("none.brewer.-.01", Occupation.Brewer, "今のところ何も困っていないぜ。"),
        N("none.brewer.-.02", Occupation.Brewer, "樽は揃っているし、頼みごとは無いな。"),
        N("none.woodworker.-.01", Occupation.Woodworker, "足りているよ。頼むことは無いな。"),
        N("none.woodworker.-.02", Occupation.Woodworker, "今日のところ、頼みごとは無いね。"),
        N("none.smith.-.01", Occupation.Smith, "今のところ何も要らんよ。"),
        N("none.smith.-.02", Occupation.Smith, "火は絶やしていないし、頼みは無いな。"),

        // ---- 取引の成立(職業・段階。段階は必ず指定する。値段の妥当さで語る) ----
        A("thanks.any.reluctant.01", null, Gratitude.Reluctant, "高いが、仕方ない。{item}を{qty}受け取ろう。"),
        A("thanks.any.reluctant.02", null, Gratitude.Reluctant, "高い気もするが、背に腹は代えられん。{item}を{qty}、もらうよ。"),
        A("thanks.any.reluctant.03", null, Gratitude.Reluctant, "……この値か。仕方ない。{item}を{qty}、引き取ろう。"),
        A("thanks.miller.reluctant.01", Occupation.Miller, Gratitude.Reluctant, "ずいぶん強気な値だな。{item}を{qty}、受け取っておく。"),
        A("thanks.baker.reluctant.01", Occupation.Baker, Gratitude.Reluctant, "ちょっと高いね。{item}を{qty}、仕方なく受け取るよ。"),
        A("thanks.brewer.reluctant.01", Occupation.Brewer, Gratitude.Reluctant, "吹っかけたな、まったく。{item}を{qty}、持っていくぜ。"),
        A("thanks.woodworker.reluctant.01", Occupation.Woodworker, Gratitude.Reluctant, "高いな。{item}を{qty}、受け取ろう。"),
        A("thanks.smith.reluctant.01", Occupation.Smith, Gratitude.Reluctant, "足元を見たな。{item}を{qty}、受け取る。"),
        A("thanks.any.grateful.01", null, Gratitude.Grateful, "{item}を{qty}、確かに受け取った。まっとうな値だ、有難う。"),
        A("thanks.any.grateful.02", null, Gratitude.Grateful, "妥当な値だな。{item}を{qty}、礼を言う。"),
        A("thanks.any.grateful.03", null, Gratitude.Grateful, "ちょうど欲しかったんだ。まっとうな値で{item}を{qty}、有難う。"),
        A("thanks.miller.grateful.01", Occupation.Miller, Gratitude.Grateful, "この値なら納得だ。{item}を{qty}、確かに受け取った。"),
        A("thanks.baker.grateful.01", Occupation.Baker, Gratitude.Grateful, "いい値をつけてくれて有難う。{item}を{qty}、確かに。"),
        A("thanks.brewer.grateful.01", Occupation.Brewer, Gratitude.Grateful, "まっとうな値だ、気に入った。{item}を{qty}、有難う。"),
        A("thanks.woodworker.grateful.01", Occupation.Woodworker, Gratitude.Grateful, "相応の値だ。{item}を{qty}、確かに。"),
        A("thanks.smith.grateful.01", Occupation.Smith, Gratitude.Grateful, "ちゃんとした値だ。{item}を{qty}、確かに受け取った。"),
        A("thanks.any.deep.01", null, Gratitude.DeepGratitude, "この値で{item}を{qty}、譲ってくれるのか。気前がいいな。"),
        A("thanks.any.deep.02", null, Gratitude.DeepGratitude, "本当にいいのか、この値で。{item}を{qty}、有難う。"),
        A("thanks.any.deep.03", null, Gratitude.DeepGratitude, "{item}を{qty}、この値でか。あんたは気前がいい。礼の言いようもない。"),
        A("thanks.miller.deep.01", Occupation.Miller, Gratitude.DeepGratitude, "この値で譲ってくれるのか。{item}を{qty}、礼を言うぞ。"),
        A("thanks.baker.deep.01", Occupation.Baker, Gratitude.DeepGratitude, "この値で譲ってくれるのか。{item}を{qty}、これで朝が楽になる。"),
        A("thanks.brewer.deep.01", Occupation.Brewer, Gratitude.DeepGratitude, "この値でか、気前がいいぜ。{item}を{qty}、有難う。"),
        A("thanks.woodworker.deep.01", Occupation.Woodworker, Gratitude.DeepGratitude, "この値で譲ってくれるとは。{item}を{qty}、有難い。"),
        A("thanks.smith.deep.01", Occupation.Smith, Gratitude.DeepGratitude, "この値でいいのか。{item}を{qty}、気前がいいな。"),

        // ---- 取引の不成立(職業。{item} のみ。「その値では買えん」までにする) ----
        R("refuse.any.-.01", null, "その値では{item}は買えん。"),
        R("refuse.any.-.02", null, "{item}は欲しいが、その値では買えんな。"),
        R("refuse.miller.-.01", Occupation.Miller, "その値では{item}は買えんな。また今度にしよう。"),
        R("refuse.miller.-.02", Occupation.Miller, "{item}は要るが、その値では買えん。"),
        R("refuse.baker.-.01", Occupation.Baker, "その値は無理だね。{item}は諦めるよ。"),
        R("refuse.baker.-.02", Occupation.Baker, "{item}にその値は出せんな。また今度にしてくれ。"),
        R("refuse.brewer.-.01", Occupation.Brewer, "その値は出せんよ。{item}は見送る。"),
        R("refuse.brewer.-.02", Occupation.Brewer, "{item}にその値では、買えんなあ。"),
        R("refuse.woodworker.-.01", Occupation.Woodworker, "その値は高すぎる。{item}は見送ろう。"),
        R("refuse.woodworker.-.02", Occupation.Woodworker, "{item}にその値は出せん。また今度だ。"),
        R("refuse.smith.-.01", Occupation.Smith, "その値は払えん。{item}は諦める。"),
        R("refuse.smith.-.02", Occupation.Smith, "{item}にその値は高い。買えん。"),
    };

    private static DialogueTemplate G(string id, Occupation? occupation, string text) =>
        new(id, LineKind.Greeting, occupation, null, null, null, null, text);

    private static DialogueTemplate S(string id, Occupation? occupation, Season? season, string text) =>
        new(id, LineKind.SmallTalk, occupation, season, null, null, null, text);

    // 開示・1件目
    private static DialogueTemplate D(string id, Occupation? occupation, string text) =>
        new(id, LineKind.NeedDisclosure, occupation, null, null, DisclosureOrder.First, null, text);

    // 開示・続き(職業指定は持たない)
    private static DialogueTemplate C(string id, string text) =>
        new(id, LineKind.NeedDisclosure, null, null, null, DisclosureOrder.Continued, null, text);

    private static DialogueTemplate N(string id, Occupation? occupation, string text) =>
        new(id, LineKind.NoNeed, occupation, null, null, null, null, text);

    private static DialogueTemplate A(string id, Occupation? occupation, Gratitude gratitude, string text) =>
        new(id, LineKind.TradeAccepted, occupation, null, gratitude, null, null, text);

    private static DialogueTemplate R(string id, Occupation? occupation, string text) =>
        new(id, LineKind.TradeRefused, occupation, null, null, null, null, text);
}
