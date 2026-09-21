#!/usr/bin/env bash
#
# check-budget-threshold.sh — 起動時の枠ガードの閾値(#132 決定1)について、
# `scripts/pipeline.ps1` の `$DefaultMinRemaining` と、文書側の写し3箇所が
# 一致しているかを検査する(#139)。
#
# **正は `$DefaultMinRemaining` である。** #132 決定1(論点6 案A)が、閾値はスクリプトの
# 定数が持ち、出所はコメントと 04 の相互参照が持つ形にした。この検査は文書を正にしない —
# スクリプトから読んだ値に文書が追随しているかだけを見る。
#
# **二重管理そのものは消えていない。** 消せないから機械に見させている。[04「枠」](../docs/process/04-issue-driven.md)
# の表は #123 の確認で動く予定があり、動かしたときに写しが取り残されても人は気付けない。
#
# 使い方: 引数なしでリポジトリのどこからでも実行できる。CI(`.github/workflows/ci.yml`
# の `docs` ジョブ)からも同じ形で回る。
#   $ bash scripts/check-budget-threshold.sh
#
# 標準出力: 照合した組を1行ずつ("OK" / "NG" と、正・写し・出所)。
# 終了コード: すべて一致なら 0、1件でも食い違うか値が取れなければ 1。
#
# **値が取れなければ落とす(fail-closed)。** `pipeline.ps1` の枠ガード自身が
# 「読めなかったときも拒否する」で揃えてあるのと同じ理由である — 取れないまま 0 を返す
# 検査は、無い検査より悪い(「機械が見ている」と読ませるので、踏んでも気付けない)。
# したがって**文面を書き換えて錨が外れると、数値が正しくてもここは落ちる。**
# そのときは数値ではなく下の「錨」の表を直す。
#
# --- この検査が見つけられないもの(残る穴) -----------------------------------
#
# 1. **ここに列挙していない写し。** 走査するのは下の3箇所だけで、リポジトリ全体から
#    「40%」を探しはしない。新しい文書が閾値を書き写しても、この表に足さない限り
#    検査は 0 を返し続ける。
# 2. **根拠のほうの陳腐化。** 一致するのは数値だけである。`pipeline.ps1` の
#    コメントが持つ導出(「中央値 14 + 対話 4〜8」など)や 04 の実測表が古くなっても
#    数値さえ揃っていれば通る。数値の校正は #123 が持つ。
# 3. **`-MinRemaining` を手で渡した実行。** 検査しているのは既定値だけである。

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
cd "$repo_root"

fail=0

die() {
    printf 'NG  %s\n' "$1"
    fail=1
}

# 比を1つだけ取り出す。マッチが 0 件でも 2 件以上でも空文字を返す(呼び出し側が落とす)。
# **2 件以上を通さない**のは、同じ節に別の閾値が増えたときにどちらを見ているか分からない
# ままにしないためである。
extract_one() {
    local text="$1" pattern="$2"
    local hits
    hits="$(printf '%s\n' "$text" | grep -oE "$pattern" || true)"
    [ "$(printf '%s' "$hits" | grep -c . || true)" -eq 1 ] || return 0
    printf '%s' "$hits" | sed -E 's/^.*[^0-9.]([0-9.]+).*$/\1/'
}

# 2つの比を「枠の残り比(0〜1)」として突き合わせる。文書は % 表記なので呼び出し側で /100 する。
same_ratio() {
    awk -v a="$1" -v b="$2" 'BEGIN { d = a - b; if (d < 0) d = -d; exit (d < 1e-9) ? 0 : 1 }'
}

compare() {
    local label="$1" expected="$2" actual="$3" origin="$4"
    if [ -z "$actual" ]; then
        die "$label: 値が取れなかった($origin)。錨が文面の書き換えで外れている"
        return
    fi
    if same_ratio "$expected" "$actual"; then
        printf 'OK  %-34s %s == %s (%s)\n' "$label" "$expected" "$actual" "$origin"
    else
        die "$(printf '%-34s 正 %s / 写し %s (%s)' "$label" "$expected" "$actual" "$origin")"
    fi
}

# === 正 — scripts/pipeline.ps1 の $DefaultMinRemaining ========================
src_file='scripts/pipeline.ps1'
src_line="$(grep -E '^\$DefaultMinRemaining[[:space:]]*=' "$src_file" || true)"
if [ "$(printf '%s' "$src_line" | grep -c . || true)" -ne 1 ]; then
    printf 'NG  %s: $DefaultMinRemaining の代入行が1行に決まらない\n' "$src_file"
    exit 1
fi

impl="$(extract_one "$src_line" 'impl[[:space:]]*=[[:space:]]*[0-9.]+')"
wrap="$(extract_one "$src_line" 'wrap[[:space:]]*=[[:space:]]*[0-9.]+')"
if [ -z "$impl" ] || [ -z "$wrap" ]; then
    printf 'NG  %s: $DefaultMinRemaining から impl / wrap が読めない: %s\n' "$src_file" "$src_line"
    exit 1
fi
printf '正  %s $DefaultMinRemaining: impl=%s wrap=%s\n' "$src_file" "$impl" "$wrap"

# === 錨 — 写しの在り処と、その行から数値を取り出す形 ==========================
#
# **錨は「番号のすぐ隣にあって、言い換えにくい語」を選ぶ。** どの写しも
# 「<数値>% 未満」という同じ言い回しで閾値を書いているので、それを共通の錨にした。
# `-From wrap` の側はコード上のトークンそのものなので、言い換えでは動かない。

# --- 写し1: docs/process/04-issue-driven.md 「## 枠」規則2 --------------------
doc04='docs/process/04-issue-driven.md'
rule2="$(awk '/^## 枠/ { inside = 1; next } /^## / { inside = 0 } inside && /^2\. /' "$doc04")"
if [ "$(printf '%s' "$rule2" | grep -c . || true)" -ne 1 ]; then
    die "$doc04: 「## 枠」の規則2 の行が1行に決まらない"
else
    compare '04 規則2 impl' "$impl" \
        "$(awk -v p="$(extract_one "$rule2" '[0-9]+%[[:space:]]*未満')" 'BEGIN { if (p != "") printf "%.10g", p / 100 }')" \
        "$doc04 「<N>% 未満」"

    # `-From wrap` より後ろの最初の "<N>%" を wrap の写しとする。同じ行の後方には
    # 「枠の 0.2%」「0.64 %/分」も出るが、いずれも 10% より後ろにある。
    wrap_tail="${rule2#*-From wrap}"
    compare '04 規則2 wrap' "$wrap" \
        "$(awk -v p="$(printf '%s' "$wrap_tail" | grep -oE '[0-9]+%' | head -1 | tr -d '%')" 'BEGIN { if (p != "") printf "%.10g", p / 100 }')" \
        "$doc04 「\`-From wrap\` …… <N>%」"
fi

# --- 写し2: CLAUDE.md 「タスクの進め方」 --------------------------------------
claude_md='CLAUDE.md'
claude_line="$(grep -E '[0-9]+%[[:space:]]*未満' "$claude_md" || true)"
if [ "$(printf '%s' "$claude_line" | grep -c . || true)" -ne 1 ]; then
    die "$claude_md: 「<N>% 未満」の行が1行に決まらない"
else
    compare 'CLAUDE.md impl' "$impl" \
        "$(awk -v p="$(extract_one "$claude_line" '[0-9]+%[[:space:]]*未満')" 'BEGIN { if (p != "") printf "%.10g", p / 100 }')" \
        "$claude_md 「<N>% 未満」"
fi

# --- 写し3: docs/process/05-phase-sessions.md の haiku 1 行 -------------------
#
# **ここだけ写しが2つある。** 「`utilization` が 0.60 を超えていれば残り 40% 未満」は、
# 残り比 40% と、その裏返しの使用率 0.60(= 1 - impl)を同じ行に書いている。
# 40% だけを見ると 0.60 の取り残しが素通りするので、両方を突き合わせる。
doc05='docs/process/05-phase-sessions.md'
line05="$(grep -E '[0-9]+%[[:space:]]*未満' "$doc05" || true)"
if [ "$(printf '%s' "$line05" | grep -c . || true)" -ne 1 ]; then
    die "$doc05: 「<N>% 未満」の行が1行に決まらない"
else
    compare '05 haiku 1行 impl' "$impl" \
        "$(awk -v p="$(extract_one "$line05" '[0-9]+%[[:space:]]*未満')" 'BEGIN { if (p != "") printf "%.10g", p / 100 }')" \
        "$doc05 「<N>% 未満」"

    # 使用率の側は 1 - impl と突き合わせる。
    compare '05 haiku 1行 utilization' \
        "$(awk -v i="$impl" 'BEGIN { printf "%.10g", 1 - i }')" \
        "$(extract_one "$line05" 'utilization`[^0-9]*[0-9.]+[[:space:]]*を超え')" \
        "$doc05 「\`utilization\` が <X> を超え」(= 1 - impl)"
fi

exit "$fail"
