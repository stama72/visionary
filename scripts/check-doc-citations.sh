#!/usr/bin/env bash
#
# check-doc-citations.sh — 現行の GDD / TDD (docs/03-gdd/*.md, docs/04-tdd/*.md) に
# 見出しとして存在しない節番号を引用している src/ tests/ の doc コメント・行コメントを
# 列挙する(#112 / W2-09)。#101 の追随表を作る設計セッションが手で叩くためのもので、
# CI からは回さない(判断: #112 のフェーズ1。GDD 側だけを直す PR が通らなくなる代償を、
# いまは取らない)。
#
# 使い方: 引数なしでリポジトリのどこからでも実行できる。
#   $ bash scripts/check-doc-citations.sh
#
# 標準出力: STALE な引用を1件1行、"path:line<TAB>DocId §番号" の形式で path:line 昇順に出す。
# 標準エラー: UNRESOLVED な引用を1件1行 "UNRESOLVED<TAB>path:line<TAB>§番号" で同じ順に出し、
#             最後に集計 "STALE <N> / UNRESOLVED <M>" を1行出す。一覧の行数は <M> に一致する。
#             集計は**標準エラーの**最後の1行である。`2>&1` で標準出力と合流させた場合、
#             STALE が1件以上出ている局面では `tail -1` が STALE 行を返しうる(#233 / #117)。
# 終了コード: STALE が1件以上なら1、0件なら0。UNRESOLVED は終了コードに影響しない。
#
# --- この検出器が見つけられないもの(残る穴) ---------------------------------
#
# 1. 番号が再利用された引用。旧節が分割で移動し、同じ番号が現行では別の節に
#    振り直された場合(例: 旧 GDD02 §9「季節との接続」→現行は GDD02d §5 だが、
#    番号9はGDD02の現行「9. 本書が持たないもの」として存在する)、この検出器は
#    見出しの存在だけを見るので 0 件のまま通る。節番号を振り直す変更をしたら、
#    この検出器が 0 でも安心してはならない。
#
# 2. 行をまたぐ継続引用。`GDD02 §6.1・§9` のように2つ目以降に文書名が無い形は
#    「同じ行で直前に現れた文書Id」だけを引き継ぐ。前の行に文書Idがあり当該行が
#    裸の `§X` から始まる場合は引き継ぎ先が無く、UNRESOLVED として件数だけ数える
#    (STALE にはしない)。行をまたぐ引き継ぎを実装しないのは、誤帰属の危険が
#    引き継がないことの取りこぼしより大きいと判断したため。
#
#    **UNRESOLVED は定義上 STALE にならない。** 照合する組が決まらないので、前の行で
#    名指しされた文書の節を振り直しても、この形の引用は 0 件のまま通る。裸の § で
#    ファイル冒頭や数行前に文書を名指ししている形がここに落ちる(2026-09-25 実測で
#    UNRESOLVED は 66 件)。**そのため、節番号を振り直したら標準エラーの一覧を人が
#    読むこと**(#117 / #233)。一覧には「タスク仕様 / ADR- / #<数字>」で引き継ぎを
#    打ち切ったものも入る — GDD/TDD の節ではないので、読み飛ばす判断は人が行う。
#
# 3. 節が在り主題も同じだが、コードが実際に従っているのは一世代前の規則、という
#    「本文の中身が古い」食い違い。引用先が現行の見出しを指してさえいれば
#    この検出器は関知しない。これは機械では見えない — 見えないからこそ #101 の
#    追随表と規則8(docs/process/02-task-spec.md)が人の側にある。
#
# 4. "<文書Id> §<番号>" 以外の形で文書を名指しした引用。パスで名指しする形
#    (`<c>docs/03-gdd/03-seasons-and-city.md</c> §1.2`)や GDD/TDD 接頭辞の無い形
#    (`(02c §2.3 の利潤上限のための…)`)は、行内に文書が書いてあっても UNRESOLVED
#    に落ちる。`GDD03 §1.2` を振り直すと `GDD03 §1.2` 形式の引用は STALE に出るのに、
#    この形の引用は 0 件のまま通る。**実在していた3件(GameDate.cs / Season.cs /
#    M0CalibrationTests.cs)は #233 で `<文書Id> §<番号>` の形へ書き換えたので、
#    2026-09-25 時点で 0 件である。** 検出器の正規表現は広げていない(#116 決定1-1)
#    ので、この形で新しく書けばまた落ちる。落ちた先は 2. の一覧に file:line で出る。
#
# 5. 枝番 "-N" は照合されない。「節番号は番号のあとに続く -4 や . を番号の
#    一部にしない」ので、`GDD02 §8-4` は `GDD02 §8` の存在だけを見る。-4 は
#    読まれてすらいない(これは設計どおりの挙動でありバグではない)。実在する
#    枝番引用は 19 件(2026-09-25 実測)。**このうち GDD02 §8 を指す 17 件は、
#    節番号と行番号までは GDD02 §8 冒頭の凍結規則が縛る(docs/03-gdd/02-economy.md。
#    機械は見ていない)。** **そのうち "(a)(b)" 付きの 4 件**(VerificationThresholds.cs
#    の :28 :29 :36 と MetricsScratch.cs:63)**は枝の側が凍結の対象外である**
#    (#118 決定3-2。行の中の (a)(b)(c) の並びは凍らせていない)。**TDD01 §3.3-9 の
#    2 件**(StateHasher.cs:123 / StateHasherTests.cs:105)**は穴のままである** —
#    §3.3 は実行順の表で、システムを挿入すれば振り直しが自然に起きるため凍らせない。
#    1.(番号の再利用)とは別物 — 1. は「番号が存在してしまう」case、こちらは
#    「そもそも読んでいない」case。
#
# 6. 見出しの収集側が過剰に採る場合。1.〜5. はすべて引用側の限界だが、これは
#    照合相手の集合そのものの限界。**列0 で始まるコードフェンス(``` / ~~~)の中は
#    見出しとして採らない**(#233 / #118 穴6)。**過剰収集の経路はこれで尽きておらず、
#    少なくとも次の2つが残る。この列挙も閉じていない。**
#    (i) **列0 以外で開いたフェンス。** その中に列0 の "## 3. ..." が入ると幻の節が
#    でき、存在しない節への引用が 0 件のまま通る。字下げされたフェンス記号をフェンスと
#    見ない側に倒したのは、本物の見出しを飲み込むと STALE の偽陽性になるためで、
#    飲み込まない側が狭い(2026-09-25 実測: GDD/TDD のフェンスは 13 ファイル・78
#    ブロックすべて列0、`~~~` は 0 件、フェンス内の見出し行は 0 件)。
#    (ii) **見出し本文が数字で始まる見出し。** 収集は "^#{2,6} <数字>" を採るだけで、
#    その数字が節番号かどうかは見ない。"#### 7項目の判定" は TDD01 の見出し集合へ
#    "7" を入れる(2026-09-25 実測で 2 件: docs/04-tdd/01-sim-core-and-m0.md の
#    "7項目の判定" と docs/03-gdd/02b-consumption-and-household.md の "1日消費量"。
#    どちらも §7 / §1 が実在するので、いまは幻の節になっていない)。**節番号として
#    存在しない数字で始まる見出しを1本足すと、その番号への引用が 0 件のまま通る。**
#    なお 0 件は「引用先が実在する」ではなく「引用先が列0 の ^#{2,6} <番号> で
#    始まる行として存在する」しか意味しない。
#
# --- "0 件" が保証する範囲(2026-09-25 実測で確定) --------------------------
#
# 保証するのは、同一行に "<GDD|TDD>dd[a-z]* §<番号>" の形で書かれた 1020 件の
# (文書Id, 節番号) の組が、現行の GDD / TDD の見出しとして存在することだけ。
# 件数の数え方: `grep -rhoE --exclude-dir=bin --exclude-dir=obj '§[0-9]+(\.[0-9]+)*' src tests | wc -l`
# で数えた全 § 引用(2026-09-25 実測 1086 件)から、集計行の UNRESOLVED(66 件)を引く。
# 保証しないもの: 節が引用の主題と一致すること(3.) / 番号が再利用されていない
# こと(1.) / 枝番の指す行(5.) / UNRESOLVED の 66 件(2.・4.) / 見出し集合の正しさ
# (6.) / docs/ 配下の相互引用(走査対象は src/ と tests/ のみで、docs/ は対象外)。
#
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
cd "$repo_root"

headings_file="$(mktemp)"
trap 'rm -f "$headings_file"' EXIT

# 1. 見出しの正 — docs/03-gdd/*.md と docs/04-tdd/*.md(README.md は除く)。
#    ファイル名の先頭のハイフンまでを文書Idとする("02c-price-and-budget.md" → "GDD02c")。
#    見出しは "^#{2,6} <番号>" から番号だけを採る("## 8. ..." → "8"、"### 5.1 ..." → "5.1")。
#    列0 から始まるコードフェンス(``` 3つ以上 または ~~~ 3つ以上)の中は見出しとして
#    採らない(#233 / #118 穴6)。開始行で文字種(` か ~)と個数を覚え、終了は同じ文字種・
#    同じ個数以上・後ろが空白以外を含まない行とする(情報文字列付きの行は終了ではない)。
#    列0 以外で始まるフェンス記号はフェンスと見ない — 字下げされたフェンスの誤検出は
#    本物の見出しを飲み込み STALE の偽陽性を作るので、見逃す側に倒す(スクリプト冒頭 6.)。
#    状態(in_fence)は awk をファイルごとに起動しているのでファイルをまたいで残らない。
for doc_dir_prefix in "docs/03-gdd GDD" "docs/04-tdd TDD"; do
    set -- $doc_dir_prefix
    doc_dir="$1"
    doc_prefix="$2"

    for f in "$repo_root"/$doc_dir/*.md; do
        [ -e "$f" ] || continue
        base="$(basename "$f")"
        if [ "$base" = "README.md" ]; then
            continue
        fi

        doc_num="${base%%-*}"
        doc_id="${doc_prefix}${doc_num}"

        awk -v docid="$doc_id" '
            BEGIN {
                in_fence = 0
                fence_char = ""
                fence_count = 0
            }
            in_fence {
                # 終了判定: 開始と同じ文字種・個数が開始以上・後ろが空白以外を含まない。
                close_re = (fence_char == "`") ? "^```+[ \t]*$" : "^~~~+[ \t]*$"
                if (match($0, close_re)) {
                    marker = substr($0, RSTART, RLENGTH)
                    gsub(/[ \t]+$/, "", marker)
                    if (length(marker) >= fence_count) {
                        in_fence = 0
                        fence_char = ""
                        fence_count = 0
                    }
                }
                next
            }
            match($0, /^(```+|~~~+)/) {
                marker = substr($0, RSTART, RLENGTH)
                in_fence = 1
                fence_char = substr(marker, 1, 1)
                fence_count = length(marker)
                next
            }
            match($0, /^#{2,6}[ \t]+[0-9]+(\.[0-9]+)*/) {
                heading = substr($0, RSTART, RLENGTH)
                sub(/^#+[ \t]+/, "", heading)
                print docid " " heading
            }
        ' "$f"
    done
done > "$headings_file"

# 2. src/ tests/ を対象に、行内の "§<番号>" を左から順に走査する。
#    - "(GDD|TDD)dd<分冊接尾辞>? §<番号>" の形は明示引用として doc/number を確定する。
#      分冊接尾辞は見出し収集側(1.)と同じく a〜d に限定しない(小文字を任意個)。
#    - 文書Idの無い裸の "§<番号>" は、同じ行で直前に確定した doc を引き継ぐ。
#      ただし直前の引用と当該 § のあいだのテキストに「タスク仕様」「ADR-」「#<数字>」の
#      いずれかが現れたら引き継がない(#96 タスク仕様の節をGDDの節と誤認しないため)。
#    - 引き継ぎ元が無い/引き継ぎが打ち切られた裸の § は UNRESOLVED として件数だけ数える。
#
#    対象は拡張子で絞らない。bin/obj/.git はビルド生成物・VCS内部なので除外する
#    (決めて報告: この3つの除外はタスク仕様に明記が無い実装細部)。
mapfile -t target_files < <(find src tests -type f -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/.git/*' | sort)

if [ "${#target_files[@]}" -eq 0 ]; then
    # 対象ファイルが1件も無い場合、引数無しの awk は標準入力待ちで停止してしまう。
    echo "STALE 0 / UNRESOLVED 0" >&2
    exit 0
fi

# 決めて報告: LC_ALL を固定する代わりに「§ の長さぶんだけ読み飛ばす」形にした。
# gawk の length()/substr() は、単バイトロケール(C など)では "§"(UTF-8で2バイト)を
# 2文字として、UTF-8ロケールでは1文字として数える。旧実装は「§の次の1文字から番号」と
# 決め打ちしており、単バイトロケールでは §のバイトの片割れを番号に巻き込んで誤報していた
# (実測: 2026-09-20、詳細はタスク仕様「訂正」節)。length("§") をその場で測って読み飛ばす
# 長さに使えば、length()/substr() が同じロケールの下で常に整合するので、
# LC_ALL の値やロケールの存在有無に依存しない。
awk -v headings_file="$headings_file" '
    BEGIN {
        while ((getline h < headings_file) > 0) {
            valid[h] = 1
        }
        close(headings_file)
        stale_count = 0
        unresolved_count = 0
        section_mark_len = length("§")
    }
    index($0, "§") == 0 { next }
    {
        line = $0
        last_doc = ""
        while ((idx = index(line, "§")) > 0) {
            prefix = substr(line, 1, idx - 1)
            tail = substr(line, idx)

            if (!match(tail, /^§[0-9]+(\.[0-9]+)*/)) {
                # "§" の直後が数字でない(節番号ではない用法)。"§" 自体の長さぶん進めて続行する
                # (idx + 1 だと単バイトロケールで § の後半バイトが残り、次周回の index() が
                # ずれる)。
                line = substr(line, idx + section_mark_len)
                continue
            }

            numtoken = substr(tail, section_mark_len + 1, RLENGTH - section_mark_len)
            rest = substr(tail, RLENGTH + 1)

            if (match(prefix, /(GDD|TDD)[0-9][0-9][a-z]*[ \t]*$/)) {
                docid = substr(prefix, RSTART, RLENGTH)
                gsub(/[ \t]+$/, "", docid)
                last_doc = docid

                key = docid " " numtoken
                if (!(key in valid)) {
                    print FILENAME ":" FNR "\t" docid " §" numtoken
                    stale_count++
                }
            } else if (last_doc == "") {
                print "UNRESOLVED\t" FILENAME ":" FNR "\t§" numtoken > "/dev/stderr"
                unresolved_count++
            } else if (prefix ~ /タスク仕様|ADR-|#[0-9]/) {
                print "UNRESOLVED\t" FILENAME ":" FNR "\t§" numtoken > "/dev/stderr"
                unresolved_count++
            } else {
                key = last_doc " " numtoken
                if (!(key in valid)) {
                    print FILENAME ":" FNR "\t" last_doc " §" numtoken
                    stale_count++
                }
            }

            line = rest
        }
    }
    END {
        print "STALE " stale_count " / UNRESOLVED " unresolved_count > "/dev/stderr"
        exit (stale_count > 0) ? 1 : 0
    }
' "${target_files[@]}"
