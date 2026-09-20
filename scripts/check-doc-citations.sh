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
# 標準エラー: 集計 "STALE <N> / UNRESOLVED <M>" の1行だけ。
# 終了コード: STALE が1件以上なら1、0件なら0。
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
# 3. 節が在り主題も同じだが、コードが実際に従っているのは一世代前の規則、という
#    「本文の中身が古い」食い違い。引用先が現行の見出しを指してさえいれば
#    この検出器は関知しない。これは機械では見えない — 見えないからこそ #101 の
#    追随表と規則8(docs/process/02-task-spec.md)が人の側にある。
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
            match($0, /^#{2,6}[ \t]+[0-9]+(\.[0-9]+)*/) {
                heading = substr($0, RSTART, RLENGTH)
                sub(/^#+[ \t]+/, "", heading)
                print docid " " heading
            }
        ' "$f"
    done
done > "$headings_file"

# 2. src/ tests/ を対象に、行内の "§<番号>" を左から順に走査する。
#    - "(GDD|TDD)dd[a-d]? §<番号>" の形は明示引用として doc/number を確定する。
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

            if (match(prefix, /(GDD|TDD)[0-9][0-9][a-d]?[ \t]*$/)) {
                docid = substr(prefix, RSTART, RLENGTH)
                gsub(/[ \t]+$/, "", docid)
                last_doc = docid

                key = docid " " numtoken
                if (!(key in valid)) {
                    print FILENAME ":" FNR "\t" docid " §" numtoken
                    stale_count++
                }
            } else if (last_doc == "") {
                unresolved_count++
            } else if (prefix ~ /タスク仕様|ADR-|#[0-9]/) {
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
