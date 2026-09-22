<#
.SYNOPSIS
    ADR-0010 のフェーズ連鎖。フェーズ2 -> フェーズ3 を別プロセスで繋ぎ、停止則に当たったら
    デスクトップ通知を出して止まる。

.DESCRIPTION
    各フェーズは `claude -p` の別プロセスで走る。プロセスが別なのでコンテキスト窓も別で、
    ADR-0009 が開発者の `/clear` に負わせていた隔離と意味論が一致する。

    **これを起動するのはフェーズ1 のセッションである**(ADR-0010 論点1 選択肢2)。開発者が
    手で打ってもよいが、既定はフェーズ1 が背景で起動して終わる — 待ち時間を無くすのが
    ADR-0010 の目的で、開発者が打つ形だと「終わったので次を打つ」待ちがそのまま残る。
    実測では、待っている間にフェーズ1 が使うターンは 0 である(W2-04: 36分 / 0ターン)。

    **Git Bash からは呼べない。** MSYS が先頭の `/` を `C:/Program Files/Git/...` に変換し、
    `/impl 34` がスラッシュコマンドとして解決されない。PowerShell から回すこと。

    生ログは `.pipeline/<issue>-<phase>-<時刻>.jsonl`(Git 管理外)。1イベント1行の JSONL で、
    **走っている間ずっと伸びる。** 停止したときに最初に読む場所である。

    **起動時に 5 時間枠の残りを読み、足りなければ起動しない**(終了コード 4。#132)。
    途中で枠が切れたパイプラインはその場で死に、使った分が消える(#36 で $16、#37 で $30)。

    **`-From impl` のときは、本体が origin/master を含むかも見る**(終了コード 5。#146)。
    設計をラップトップ、実装パイプラインをデスクトップで回すため、pull を忘れると
    フェーズ2 が一世代前のタスク仕様を実装する — 出来上がりを読んでも気付けない形になる。

.EXAMPLE
    pwsh scripts/pipeline.ps1 -Issue 35
    pwsh scripts/pipeline.ps1 -Issue 35 -From wrap   # フェーズ3 だけやり直す
    pwsh scripts/pipeline.ps1 -Issue 35 -MinRemaining 0   # 枠の検査をしない(プローブも打たない)
#>
[CmdletBinding(DefaultParameterSetName = 'Run')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Run')][int]$Issue,
    [Parameter(ParameterSetName = 'Run')][ValidateSet('impl', 'wrap')][string]$From = 'impl',
    # 起動に要求する枠の残り(0〜1)。**未指定なら $From から既定を採る。**
    # **0 は「検査しない」を兼ねる** — プローブ自身も打たないので $0.11 も枠の 0.05% も使わない。
    [Parameter(ParameterSetName = 'Run')][ValidateRange(0.0, 1.0)][double]$MinRemaining,
    [Parameter(ParameterSetName = 'Run')][switch]$DryRun,
    # **DryRun で枠ガードを壊して確かめるための口**(process/03-corrections 規則1)。
    # 閾値を跨ぐ実測は作れない(枠の残りは選べない)ので、プローブの出力だけを差し替える。
    [Parameter(ParameterSetName = 'Run')][ValidateSet('ok', 'short', 'near-reset', 'fail')][string]$DryRunProbe = 'ok',
    [Parameter(Mandatory = $true, ParameterSetName = 'Status')][switch]$Status
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$LogDir = Join-Path $RepoRoot '.pipeline'

# === 起動時の枠ガードの定数(#132 決定1)===================================
#
# **単位は「枠の残り比」(0〜1)。** 0.40 = 残り 40%。
#
# 出所は [04「枠」](../docs/process/04-issue-driven.md) 規則2。**40% は「パイプライン中央値 +
# 対話 1 本」から置かれた値である**(#122 決定1)。現在のレートで組み直すと:
#
#   中央値どうし   14 + 4〜8  = 18〜22%
#   上限どうし     28 + 21    = 49%      <- **40% を超える**
#
# **上限どうしの組み合わせは元々守っていない。** 21 は 04 の「実装のフェーズ1(opus)」行
# (3〜21)で、fable 以外の対話でいちばん重い。40% はこの 2 つの間にある値である。
#
# **`wrap` 単独だけ別の値を持つ。** 実測 $1.07〜1.41 / 2〜5 分 = 枠の 0.16〜0.21%
# (P_sonnet の LOO 下限で悲観して 1.0%)に、併走する対話 1 本(8%)を足して丸めた。
# `-From wrap` は停止則の後の**再開路**であり、枠切れで死んだ直後がこの入口になる。
# ここを 40% にすると、0.2% の仕事のために再開がリセットまで一律に塞がる。
#
# **04 の表を動かしたらここも動かす。** 写しは 04 規則2 / CLAUDE.md / 05 の3箇所にあり、
# **一致は `scripts/check-budget-threshold.sh` が CI で見ている**([#139](https://github.com/stama72/visionary/issues/139))。
$DefaultMinRemaining = @{ impl = 0.40; wrap = 0.10 }

# パイプラインの消費レートの実測上限。**単位は 枠の %/分**(0.64 = 1 分あたり枠の 0.64%)。
#
# 4 走行の実測 0.17〜0.64 %/分 の上限(#112 impl が util 0.11 -> 0.34 / 36 分)。生ログの
# `rate_limit_event` の系列から直に測った値で、**口座全体のレートなので併走する対話
# セッションを含む**(含んだままが安全側。併走が増えれば上振れする)。
#
# 使い道は1つ — **リセットが近ければ、枠全体が収まらなくても起動してよい。** 枠切れで死ぬ
# 条件は「消費が残りに達する**前に**リセットが来るか」なので、リセットまで T 分なら
# この枠で使うのは高々 `rate * T` である(#132 決定1 論点7)。
$PipelineBurnPercentPerMinute = 0.64

# プローブが返らないときの上限。haiku 1 呼び出しは実測で 5〜15 秒。
# **ハングしたプローブが通知も出さずに居座るほうが高くつく**ので、切って fail-closed に落とす。
$ProbeTimeoutSec = 180

# ロックの読み書きは pipeline-guard.ps1(PreToolUse フック)と共有する。
. (Join-Path $PSScriptRoot 'pipeline-lock.ps1')

# **サブエージェントの取り込み待ちの上限。** 非対話モード(`claude -p`)は、サブエージェントや
# ワークフローを背景で走らせている間は終了せずに待つが、**待ちがアイドルのまま 10 分続くと
# 走っているものを止め、部分結果を捨てる**(公式ドキュメント「Claude Code をプログラムで
# 実行する — 終了時のバックグラウンドタスク」)。W2-07(#96)の2回目はこれを踏んだ:
# `Background tasks still running after 600s; terminating.` の直後に implementer の
# ツール実行が拒否の形で返り、オーケストレータの最後の発話「implementer が…処理中です。
# 完了通知を待ちます。」がそのまま最終 result になって `NO-SENTINEL` で止まった
# ([#100](https://github.com/stama72/visionary/issues/100))。
#
# 0(無限待ち)にしないのは、ハングしたフェーズが**通知も出さずに永久に居座る**ほうが
# 高くつくため。1時間で切れば、少なくとも NO-SENTINEL として通知が飛ぶ。
# **これは打ち切りを遅らせるだけで、ハングを見分けてはいない。** 実行基盤ごと替えれば
# 要らなくなる可能性があり、そのときに見直す(#146 に申し送り済み)。
$env:CLAUDE_CODE_PRINT_BG_WAIT_CEILING_MS = '3600000'

# 無人実行の権限面。**対話セッションには一切影響しない** — ここで渡した範囲だけが
# 無人のフェーズに効く。settings.json に書くと開発者自身のセッションまで緩むので置かない。
#
# **W2-04(#35)の実測で 8件の拒否が出た範囲を足してある。** いずれも自力で迂回して
# 完走したが、迂回に1件1ターン払う。拒否の内訳は3種類だった:
#
#   1. `Bash(dotnet:*)` などの前方一致は **1コマンドにしか掛からない。**
#      `dotnet format ... ; echo "EXIT: $?"` は後半の `echo` が別途承認を要求する。
#      複合コマンドで使われた読み取り専用のコマンドを個別に足す
#   2. **PowerShell ツールは Bash とは別に許可が要る。** `PowerShell(dotnet:*)` の
#      形で効くことを実測で確認した
#   3. **MCP ツールは名前ごとに要る。** フェーズ3 が issue を読もうとして拒否され、
#      `gh` CLI へ迂回した
#
# **許可一覧に載っていても、コマンドの「形」で弾かれるものがある**(W2-07 / W2-08 の
# 生ログに残っていた理由文): 複数操作を1呼び出しに詰めた `cmd1; cmd2` /
# `cd X && <書き込み>` の複合 / 展開式を含む文字列 `"exit=$LASTEXITCODE"` /
# ヒアドキュメント。**ここは許可一覧では塞げない**ので、書き方のほうを
# `.claude/agents/implementer.md` に書いてある(#100)。
#
# ラベル付与は `Bash(gh:*)` 経由で通る(#107 に無人で `type:impl` / `P2` が付いた実績)。
# **MCP の `issue_write` / `pull_request_read` はここに無いので必ず弾かれる** —
# 手段は `gh` CLI に固定した(#102 / `.claude/commands/wrap.md`)。
#
# サブエージェントの起動は **`Agent` が実際に使われた名前**(W2-04 で3回とも)。
# `Task` は一度も出ていないが、改名に対する保険として残す — 余分に許すだけで、
# 何かを「守っている」という主張ではない。
$AllowedTools = @(
    'Read', 'Glob', 'Grep', 'Edit', 'Write', 'TodoWrite',
    'Task', 'Agent',
    'Bash(dotnet:*)', 'Bash(git:*)', 'Bash(gh:*)',
    'Bash(cd:*)', 'Bash(ls:*)', 'Bash(cat:*)', 'Bash(echo:*)',
    'Bash(grep:*)', 'Bash(sed:*)', 'Bash(find:*)',
    'Bash(head:*)', 'Bash(tail:*)', 'Bash(wc:*)',
    # リポジトリ内の検査スクリプトは1本ずつ名指しで許す。`Bash(bash:*)` にすると
    # 任意のスクリプトが無人で走るので広げない。W2-09(#112)は完了条件がこの
    # スクリプトの実行結果そのものなので、許さないとフェーズ2 が構造的に完走できない。
    'Bash(bash scripts/check-doc-citations.sh:*)',
    'PowerShell(dotnet:*)', 'PowerShell(git:*)', 'PowerShell(gh:*)',
    'mcp__github__issue_read', 'mcp__github-ro__issue_read'
)

# フェーズごとのモデル。CLI の --model は コマンド側の `model:` frontmatter より強い(実測)。
$PhaseModel = @{ impl = 'opus'; wrap = 'sonnet' }

# SPEC-OUTSIDE が見る「仕様の外」。ADR-0010 が停止則4つの中心と呼んだもので、
# 4つのうちこれだけが差分に現れるため機械で判定できる。
$SpecPaths = @('docs/03-gdd', 'docs/04-tdd', 'docs/adr')

function Get-SpecChange {
    <#
        フェーズ起動直前の HEAD から見て、仕様の外に差分があるかを返す。

        **基準が master でないのは、フェーズ1 が仕様を凍らせるときに GDD を直すのが
        正当な仕事だからである** — その変更は既にブランチ上にあるので、master 起点だと
        毎回誤検出する(W2-03 の GDD02 §5.2・GDD03 §2.1 が実例)。

        コミット済みと作業ツリーの両方を見る。**コミットしないまま `PIPELINE: DONE` を
        出す経路があるので、片方だけでは素通りする。**
    #>
    param([Parameter(Mandatory = $true)][string]$Baseline)

    $committed = & git -C $RepoRoot diff --name-only $Baseline HEAD -- $SpecPaths
    # --porcelain の各行は "XY <path>"。追跡外のファイル(?? 行)も拾う。
    $working = & git -C $RepoRoot status --porcelain -- $SpecPaths |
        ForEach-Object { $_.Substring(3).Trim('"') }

    return @($committed) + @($working) |
        Where-Object { $_ } |
        Sort-Object -Unique
}

function Get-DirtyWorkTree {
    <#
        **フェーズ2 が `DONE` を出した時点で、本体の作業ツリーが汚れていないかを返す。**

        フェーズ2 の成果物は「緑のコードと**コミット**」である。汚れて終わるのは2つの
        どちらかで、どちらも黙って進めてはいけない:

          1. コミットし忘れ — 検証した状態と、次のフェーズが PR にする状態が違う
          2. **戻し忘れた変異** — W2-08 では、依頼した3変異のどれでもない変異が
             本体に残ったまま「`git diff --stat src/` は空」と報告された([#110](https://github.com/stama72/visionary/issues/110))

        変異は [ADR-0013](../docs/adr/0013-mutation-measurement-separated.md) で使い捨て
        worktree の中だけに閉じたが、**閉じたことを確かめるのは規律ではなくここである。**
        `.pipeline/` と `.claude/worktrees/` は `.gitignore` 済みなので、`--porcelain` には
        現れない(使い捨て worktree 自身も見えない)。
    #>
    return @(& git -C $RepoRoot status --porcelain) |
        Where-Object { $_ } |
        ForEach-Object { $_.Substring(3).Trim('"') }
}

function Send-Notification {
    <#
        **2経路に出す。デスクトップ通知と、issue へのコメントである。**

        デスクトップ通知だけだと、**誰も見ていないマシンの画面に出る。** 実装タスクは
        デスクトップで走らせる([#146](https://github.com/stama72/visionary/issues/146) 決定5)ので、開発者は出先にいる。そのままだと
        「出先から起動 -> 数分で `IMPL-BLOCKED` -> 気付くのは帰宅後」になり、
        [ADR-0010](../docs/adr/0010-phase-pipeline-and-halt-conditions.md) が消した待ちが形を変えて戻る。

        **issue コメントが出先へ届くのは、GitHub の「自分の更新」メール通知に乗るからである**
        (実測 2026-09-22)。GitHub Mobile のプッシュ通知は直接メンション / アサイン /
        レビュー依頼 / デプロイ承認依頼の4種だけで、**issue コメント自体は対象外**である。
        経路はメールであって、GitHub のプッシュではない。

        **`claude -p` の外側で打つので、Remote Control の可否に依存しない。**

        **どちらの経路も、出せなくてもパイプラインの判定は変えない。** 通知は補助である。
    #>
    param([string]$Title, [string]$Text, [string]$Level = 'Info')
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        Add-Type -AssemblyName System.Drawing -ErrorAction Stop
        $icon = New-Object System.Windows.Forms.NotifyIcon
        $icon.Icon = if ($Level -eq 'Warning') {
            [System.Drawing.SystemIcons]::Warning
        } else {
            [System.Drawing.SystemIcons]::Information
        }
        $icon.BalloonTipTitle = $Title
        $icon.BalloonTipText = $Text
        $icon.Visible = $true
        $icon.ShowBalloonTip(30000)
        Start-Sleep -Milliseconds 1200
        $icon.Dispose()
    } catch {
        # 通知はあくまで補助。出せなくてもパイプラインの判定は変えない。
        Write-Warning "デスクトップ通知を出せませんでした: $_"
    }

    # **出先へ届く経路。** `gh` は本体ツリーの origin から repo を解決するので、
    # cwd を本体に寄せてから打つ。`-Status` では $Issue が束縛されないが、
    # そちらからはこの関数を呼ばない。
    if (-not $Issue) { return }
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("visionary-notify-{0}.md" -f [guid]::NewGuid())
    try {
        [IO.File]::WriteAllText($tmp, ("**{0}**{1}{1}{2}" -f $Title, [Environment]::NewLine, $Text), [Text.UTF8Encoding]::new($false))
        Push-Location $RepoRoot
        try { & gh issue comment $Issue --body-file $tmp 2>&1 | Out-Null } finally { Pop-Location }
        if ($LASTEXITCODE -ne 0) { Write-Warning "issue コメントを出せませんでした(gh 終了コード $LASTEXITCODE)。" }
    } catch {
        Write-Warning "issue コメントを出せませんでした: $_"
    } finally {
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    }
}

function Write-PhaseProgress {
    <#
        生ログの1イベントを、画面用の1行に潰す。**走っている間、開発者に見えるものを作る**
        のが目的で、停止の判定には使わない(判定は Get-PhaseResultText が合図1行で行う)。
    #>
    param([string]$Line)

    $ev = try { $Line | ConvertFrom-Json -ErrorAction Stop } catch { $null }
    if (-not $ev -or $ev.type -ne 'assistant') { return }

    foreach ($block in @($ev.message.content)) {
        switch ($block.type) {
            'tool_use' { Write-Host ("    . {0}" -f $block.name) -ForegroundColor DarkGray }
            'text' {
                $head = ($block.text -split "`n" | Where-Object { $_.Trim() } | Select-Object -First 1)
                if ($head) {
                    if ($head.Length -gt 100) { $head = $head.Substring(0, 100) + '...' }
                    Write-Host ("    > {0}" -f $head) -ForegroundColor DarkGray
                }
            }
        }
    }
}

function Get-PhaseResultText {
    <#
        合図を読む対象は **最後の `result` イベントの `result` 欄**、つまりフェーズの最終メッセージ。

        `result` イベントが無い(プロセスが殺された・落ちた)場合と `is_error` の場合は
        空文字を返す。**呼び出し側の fail-closed(NO-SENTINEL)に落ちる**ので、
        「判らないまま次のフェーズへ渡す」ことにはならない。
    #>
    param([string]$Log)

    if (-not (Test-Path -LiteralPath $Log)) { return '' }

    $last = Get-Content -LiteralPath $Log -Encoding UTF8 |
        Where-Object { $_ -like '*"type":"result"*' } |
        Select-Object -Last 1
    if (-not $last) { return '' }

    $ev = try { $last | ConvertFrom-Json -ErrorAction Stop } catch { $null }
    if (-not $ev -or $ev.is_error) { return '' }
    return [string]$ev.result
}

function Get-NoSentinelDetail {
    <#
        **合図が読めなかったとき、なぜかを生ログから1行にする。**

        `NO-SENTINEL` は原因の違う事故を1つの箱に入れている。W2-07(#96)の1回目は
        ネットワーク断、2回目は背景タスクの打ち切りで、**ログを開くまで区別がつかなかった**
        ([#100](https://github.com/stama72/visionary/issues/100))。デスクトップ通知に
        乗るのはこの1行なので、ここで区別がつけば生ログを開く前に見当がつく。

        判定は「ログにその跡が残っているか」だけで、推測はしない。跡が無ければ素の
        NO-SENTINEL のまま返す。
    #>
    param([string]$Log)

    if (-not (Test-Path -LiteralPath $Log)) {
        return 'フェーズが DONE も HALT も出さずに終了した(生ログが存在しない — 起動に失敗した可能性)'
    }

    $lines = Get-Content -LiteralPath $Log -Encoding UTF8

    # 1. 背景タスクの打ち切り。非対話モード(`claude -p`)が自分で書き出す1行。
    $bg = $lines | Where-Object { $_ -match 'Background tasks still running after .* terminating' } | Select-Object -First 1
    if ($bg) {
        return "サブエージェントを待ち切れずに打ち切られた(CLAUDE_CODE_PRINT_BG_WAIT_CEILING_MS)。合図を出す前に終わっている: $($bg.Trim())"
    }

    # 2. 権限拒否。最後の result の permission_denials に残る。
    $last = $lines | Where-Object { $_ -like '*"type":"result"*' } | Select-Object -Last 1
    $ev = if ($last) { try { $last | ConvertFrom-Json -ErrorAction Stop } catch { $null } } else { $null }
    if ($ev) {
        $denials = @($ev.permission_denials)
        if ($denials.Count -gt 0) {
            $names = ($denials | ForEach-Object { $_.tool_name } | Sort-Object -Unique) -join ', '
            return "権限で拒否されたツール呼び出しが $($denials.Count) 件ある($names)。迂回できずに終わった可能性がある"
        }
        if ($ev.is_error -or $ev.api_error_status) {
            return "フェーズが異常終了した(subtype=$($ev.subtype) / api_error_status=$($ev.api_error_status))"
        }
        return "フェーズが DONE も HALT も出さずにターンを閉じた(最終メッセージ: $((([string]$ev.result) -split "`n" | Where-Object { $_.Trim() } | Select-Object -First 1)))"
    }

    return 'フェーズが DONE も HALT も出さずに終了した(result イベントが無い — 殺された / 落ちた / 中断)'
}

function Get-DryRunStream {
    <#
        DryRun 用の作り物の生ログ。**差し替えるのは `claude` の起動だけ**で、形は
        `--output-format stream-json` の実物に合わせてある(1イベント1行の JSONL)。

        こうしておくと、DryRun が進捗表示・合図の読み取り・SPEC-OUTSIDE の検査を
        **本番と同じ経路で**通る。「機械が守っている」を入口から壊して確かめられる状態を
        用意しておくのが、[process/03](../docs/process/03-corrections.md) 規則1 の要求である。
    #>
    @(
        '{"type":"system","subtype":"init"}'
        '{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Read"}]}}'
        '{"type":"assistant","message":{"content":[{"type":"text","text":"(DryRun) フェーズを終えます。"}]}}'
        '{"type":"result","subtype":"success","is_error":false,"result":"PIPELINE: DONE"}'
    )
}

function Get-DryRunProbeStream {
    <#
        DryRun 用の作り物のプローブ出力。**差し替えるのはプローブの取得だけ**で、読み取り・
        判定・表示・終了コードは本番と同じ関数を通る(`Get-DryRunStream` と同じ作り)。

        **閾値を跨ぐ実測は作れない** — 枠の残りは選べないからである。それでも
        「機械が守っている」と書く前に壊して確かめる必要がある(process/03-corrections 規則1)。
        4つの口はそれぞれ次を踏ませる:

          ok         残り 90%                          -> 通る
          short      残り 5% / リセットまで 5 時間      -> 拒否される
          near-reset 残り 10% / リセットまで 10 分      -> **通る**(論点7。閾値が 6.4% に下がる)
          fail       rate_limit_event が無い            -> fail-closed で拒否される
    #>
    param([string]$Case)

    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $spec = switch ($Case) {
        'short' { @{ util = 0.95; resets = $now + (5 * 3600) } }
        'near-reset' { @{ util = 0.90; resets = $now + (10 * 60) } }
        default { @{ util = 0.10; resets = $now + (5 * 3600) } }
    }

    $lines = @('{"type":"system","subtype":"init"}')
    if ($Case -ne 'fail') {
        # 週次も入れてある — **表示の経路まで DryRun で通す**ため(判定には使わない)。
        $lines += '{{"type":"rate_limit_event","rate_limit_info":{{"status":"allowed","unifiedWindows":{{"five_hour":{{"utilization":{0},"resetsAt":{1}}},"seven_day":{{"utilization":0.58,"resetsAt":{1}}}}}}}}}' -f $spec.util, $spec.resets
    }
    return $lines + @('{"type":"result","subtype":"success","is_error":false,"result":"OK"}')
}

function Read-UsageWindows {
    <#
        プローブの出力(JSONL)から枠の観測点を取り出す。**5 時間枠が読めなければ `$null` を返し、
        呼び出し側の fail-closed に落ちる。**

        `type` は**トップレベルで `rate_limit_event`** である(`system` / `subtype` ではない)。
        `unifiedWindows.five_hour` が無い形(API キー運用など)も `$null` に倒す。

        **週次(`seven_day`)は読むが、判定には使わない。** 論点7 の緩和の前提(リセットで
        utilization が 0 に戻る)は週次には成り立たず、しかも緩和がいちばん効く局面は週次も
        高い局面である。それでも閾値を置かないのは根拠が無いためで、**表示して開発者の目に
        入れるところまでにしてある**([#141](https://github.com/stama72/visionary/issues/141)。
        校正は [#123](https://github.com/stama72/visionary/issues/123))。
    #>
    param([string[]]$Lines)

    $last = @($Lines) | Where-Object { $_ -like '*"type":"rate_limit_event"*' } | Select-Object -Last 1
    if (-not $last) { return $null }

    $ev = try { $last | ConvertFrom-Json -ErrorAction Stop } catch { $null }
    $w = $ev.rate_limit_info.unifiedWindows.five_hour
    if (-not $w -or $null -eq $w.utilization -or $null -eq $w.resetsAt) { return $null }

    return [pscustomobject]@{
        Utilization        = [double]$w.utilization
        ResetsAt           = [long]$w.resetsAt
        # 無ければ $null。**表示専用なので、欠けても判定は変えない。**
        SevenDayUtilization = $ev.rate_limit_info.unifiedWindows.seven_day.utilization
    }
}

function Get-UsageWindows {
    <#
        haiku を 1 回呼んで 5 時間枠の観測点を取る。**`/usage` は CLI の UI なので読めないが、
        同じ値が `rate_limit_event` に乗る**(実測 2026-09-20、$0.11 = 枠の 0.05%)。

        返らないときは `$ProbeTimeoutSec` で切る。**プローブがハングしたまま居座ると、
        パイプラインは起動も通知もしないまま消える** — 停止則が拾えない死に方になる。
    #>
    if ($DryRun) { return Read-UsageWindows -Lines (Get-DryRunProbeStream -Case $DryRunProbe) }

    $job = Start-Job -ScriptBlock {
        & claude -p --model haiku --output-format stream-json --verbose 'OK' 2>&1
    }
    try {
        if (-not (Wait-Job -Job $job -Timeout $ProbeTimeoutSec)) {
            Write-Warning "枠のプローブが $ProbeTimeoutSec 秒で返りませんでした。"
            return $null
        }
        return Read-UsageWindows -Lines @(Receive-Job -Job $job | ForEach-Object { [string]$_ })
    } catch {
        Write-Warning "枠のプローブに失敗しました: $_"
        return $null
    } finally {
        Stop-Job -Job $job -ErrorAction SilentlyContinue
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    }
}

function Get-EffectiveMinRemaining {
    <#
        **リセットが近ければ閾値を下げる**(#132 決定1 論点7)。

        枠切れで死ぬ条件は「消費が残りに達する**前に**リセットが来るか」である。リセットまで
        T 分なら、この枠で使うのは高々 `rate * T` なので、要求すべき残りも `rate * T` でよい。
        リセットをまたいだぶんは**次の枠から**出る(前提: リセットで utilization が 0 に戻る)。

        交点は約 63 分 — 実質「**リセットまで 1 時間を切ったときだけ下がる**」規則である。
        観測が古くて T が負になる場合は下げない(安全側)。
    #>
    param([double]$Base, [long]$ResetsAt)

    $minutes = ($ResetsAt - [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) / 60.0
    if ($minutes -le 0) { return $Base }

    $byReset = ($PipelineBurnPercentPerMinute * $minutes) / 100.0
    return [Math]::Min($Base, $byReset)
}

function Format-ResetsAtJst {
    param([long]$ResetsAt)
    $utc = [DateTimeOffset]::FromUnixTimeSeconds($ResetsAt).UtcDateTime
    try {
        $tz = [TimeZoneInfo]::FindSystemTimeZoneById('Tokyo Standard Time')
        return '{0} JST' -f [TimeZoneInfo]::ConvertTimeFromUtc($utc, $tz).ToString('MM/dd HH:mm')
    } catch {
        return '{0} (ローカル)' -f $utc.ToLocalTime().ToString('MM/dd HH:mm')
    }
}

function Test-BudgetGate {
    <#
        **起動してよいかを枠の残りで判定する。** 通れば `$true`、拒否なら `$false`。

        **読めなければ拒否する(fail-closed)。** `pipeline-guard.ps1` が例外を素通しに倒して
        いるのと逆だが、あちらは**止まると開発が死ぬ**フックであるのに対し、こちらは打ち直せば
        よい。読めないまま起動するのは「守る側が静かに壊れる」形そのものである。
    #>
    param([double]$Required)

    Write-Host ""
    Write-Host "=== 5 時間枠の確認(要求: 残り $([int]($Required * 100))% 以上)===" -ForegroundColor Cyan

    $w = Get-UsageWindows
    if (-not $w) {
        Write-Host "!!! 枠の残りを読めませんでした。起動しません(fail-closed)。" -ForegroundColor Red
        Write-Host "    検査を外して打つなら -MinRemaining 0 です。"
        Send-Notification -Title "Visionary #$Issue — 起動しませんでした" `
            -Text "5 時間枠の残りを読めませんでした(fail-closed)。" -Level 'Warning'
        return $false
    }

    $remaining = 1.0 - $w.Utilization
    $effective = Get-EffectiveMinRemaining -Base $Required -ResetsAt $w.ResetsAt
    $resetText = Format-ResetsAtJst -ResetsAt $w.ResetsAt

    # **週次は表示だけで、判定には使わない**(#141)。単位を 5 時間枠と揃えて「残り」で出す
    # — 片方が残り・片方が使用率だと読み違える。
    $weekly = if ($null -ne $w.SevenDayUtilization) {
        '  /  週次 残り {0}%(判定には使わない)' -f [int]((1.0 - [double]$w.SevenDayUtilization) * 100)
    } else { '' }
    Write-Host ("    残り {0}% / リセット {1}{2}" -f [int]($remaining * 100), $resetText, $weekly)
    if ($effective -lt $Required) {
        Write-Host ("    リセットが近いので要求を {0}% まで下げます(#132 論点7)" -f [Math]::Round($effective * 100, 1)) -ForegroundColor DarkGray
    }

    if ($remaining -lt $effective) {
        $text = "5 時間枠の残りが {0}% で、要求 {1}% に足りません。リセットは {2} です。" -f `
            [int]($remaining * 100), [Math]::Round($effective * 100, 1), $resetText
        Write-Host ""
        Write-Host "!!! 枠が足りないので起動しません。$text" -ForegroundColor Yellow
        Write-Host "    リセットを待つか、-MinRemaining で閾値を変えてください。"
        Send-Notification -Title "Visionary #$Issue — 枠が足りず起動しませんでした" `
            -Text $text -Level 'Warning'
        return $false
    }

    Write-Host "    起動します。" -ForegroundColor Green
    return $true
}

function Test-FreshnessGate {
    <#
        **本体が origin/master を含んでいるかを見る。** 含んでいれば `$true`、遅れて
        いれば `$false`。

        本体ツリーを2台に置く([#146](https://github.com/stama72/visionary/issues/146) 決定1)と、
        **ラップトップの `/design` が入れた GDD / TDD / process が master に積まれ、
        デスクトップがそれを pull していない**状態が起こる。そのままフェーズ2 を回すと、
        **一世代前の上位文書と既存コードを正確に前提にした実装**が出来上がり、
        読んでも正しく見える。束1 の作業中に実際に 18 コミット遅れていた。

        **守るのは本体の世代であって、ブランチ上のタスク仕様の新しさではない。** 仕様は
        フェーズ1 がブランチに積むので(05「差分で見えるものは機械が見ている」)、ここの
        述語には入らない。**タスク仕様を凍らせるフェーズ1 もデスクトップで開く**(決定5)
        ので、仕様がマシンをまたぐ経路は**規律の上では**無い — 機械は見ていない。

        決定1 が開いたもう一方の穴(コミットし忘れた作業が片方のマシンに取り残される)は
        機械では塞げないが、**こちらは塞げるので塞ぐ。**

        **`impl` から始めるときだけ見る。** タスク仕様を読むのはフェーズ2 の入口だけで
        ある。`-From wrap` は停止則の後の再開路で、ここを塞ぐと「走っている間に master が
        動いた」だけで再開が止まる。枠の閾値が `wrap` だけ別の値を持つのと同じ理由である。

        **fetch できなければ拒否する。逃げ口は置かない。** フェーズ本体は `claude -p` で
        あり、**ネットワークが無ければどのみち走らない。** 「オフラインでも回したい」が
        存在しない以上、ここに口を開けても偽陰性が増えるだけである。

        比較対象に `origin/master` ではなく `FETCH_HEAD` を使うのは、リモート追跡ブランチの
        設定に依存しないためである。
    #>

    Write-Host ""
    Write-Host "=== 本体が origin/master を含むかの確認 ===" -ForegroundColor Cyan

    try {
        & git -C $RepoRoot fetch --quiet origin master 2>&1 | Out-Null
        $fetched = ($LASTEXITCODE -eq 0)
    } catch { $fetched = $false }

    if (-not $fetched) {
        Write-Host "!!! origin から fetch できませんでした。起動しません(fail-closed)。" -ForegroundColor Red
        Write-Host "    フェーズ本体は claude -p なので、ネットワークが無ければどのみち走りません。"
        Send-Notification -Title "Visionary #$Issue — 起動しませんでした" `
            -Text "origin から fetch できませんでした(fail-closed)。" -Level 'Warning'
        return $false
    }

    try {
        & git -C $RepoRoot merge-base --is-ancestor FETCH_HEAD HEAD 2>&1 | Out-Null
        $contains = ($LASTEXITCODE -eq 0)
    } catch { $contains = $false }

    if (-not $contains) {
        $behind = (@(& git -C $RepoRoot rev-list --count "HEAD..FETCH_HEAD") | Select-Object -First 1)
        $text = "本体が origin/master より $behind コミット遅れています。git pull してから打ってください。"
        Write-Host ""
        Write-Host "!!! $text" -ForegroundColor Yellow
        Write-Host "    そのまま打つと、フェーズ2 が一世代前のタスク仕様を実装します(#146 決定4)。"
        Send-Notification -Title "Visionary #$Issue — 本体が古いので起動しませんでした" `
            -Text $text -Level 'Warning'
        return $false
    }

    Write-Host "    origin/master を含んでいます。" -ForegroundColor Green
    return $true
}

function Invoke-Phase {
    param([string]$Command)

    $model = $PhaseModel[$Command]
    $prompt = "/$Command $Issue"
    $log = Join-Path $LogDir ("{0}-{1}-{2}.jsonl" -f $Issue, $Command, (Get-Date -Format 'yyyyMMdd-HHmmss'))

    Write-Host ""
    Write-Host "=== /$Command $Issue  (model: $model) ===" -ForegroundColor Cyan
    Write-Host "    log: $log"

    $baseline = (& git -C $RepoRoot rev-parse HEAD).Trim()

    if ($DryRun) {
        # **DryRun が差し替えるのは `claude` の起動だけ。** 生ログの書き出し・進捗表示・
        # 合図の読み取り・SPEC-OUTSIDE の検査は、下の本番経路と同じ関数を通る。
        Write-Host "    [DryRun] claude -p `"$prompt`" --model $model --output-format stream-json --verbose --allowedTools ..."
        Get-DryRunStream |
            Tee-Object -FilePath $log |
            ForEach-Object { Write-PhaseProgress -Line $_ }

        $out = Get-PhaseResultText -Log $log
    } else {
        # プロンプトは可変長フラグ(--allowedTools)より前に置く。後ろだと引数として吸われる(実測)。
        #
        # **--output-format stream-json --verbose にするのは、既定の text が生ログにならないため。**
        # text は最終メッセージ1本しか出さず、しかも終わるまで1バイトも出さない。
        # Tee-Object は最初の1件が流れた時点でファイルを作るので、text のままだと
        # **走行中と中断時にログが存在しない** — ADR-0010 が「停止したとき、開発者が最初に
        # 読むのはここである」と書いた場所が、止まったときにいちばん空になっていた
        # (W2-04 で中断した1回目のログが1件も残っていない)。
        #
        # stream-json は1イベント1行の JSONL で流れるので、ログは数秒後に現れ、
        # 走っている間ずっと伸びる(実測: 15秒のツール実行中に 6行 -> 13行)。
        & claude -p $prompt --model $model --output-format stream-json --verbose --allowedTools $AllowedTools 2>&1 |
            Tee-Object -FilePath $log |
            ForEach-Object { Write-PhaseProgress -Line $_ }

        $out = Get-PhaseResultText -Log $log
    }

    # **fail-closed。** HALT が無くても DONE が無ければ進めない。
    # 停止則を踏んだかどうか判らないまま次のフェーズへ渡すほうが危ない。
    if ($out -match 'PIPELINE:\s*HALT\s*(\S+)\s*(.*)') {
        return @{ Status = 'HALT'; Reason = $Matches[1]; Detail = $Matches[2].Trim(); Log = $log }
    }
    if ($out -match 'PIPELINE:\s*DONE') {
        # **DONE を出していても、仕様の外に差分があれば止める。**
        # --allowedTools は Edit / Write を無条件で渡すので、GDD を書き換えたうえで
        # DONE を出すセッションを合図の読み取りだけでは素通りさせてしまう。
        # **フェーズ3 は文書を触るのが仕事なので検査しない**(05-phase-sessions)。
        if ($Command -eq 'impl') {
            $changed = Get-SpecChange -Baseline $baseline
            if ($changed) {
                return @{
                    Status = 'HALT'
                    Reason = 'SPEC-OUTSIDE'
                    Detail = "仕様の外に差分がある: {0}" -f ($changed -join ', ')
                    Log    = $log
                }
            }

            # **コミットし忘れと、戻し忘れた変異を同じ検査で拾う。**
            $dirty = Get-DirtyWorkTree
            if ($dirty) {
                $head = ($dirty | Select-Object -First 5) -join ', '
                if ($dirty.Count -gt 5) { $head += (" ほか{0}件" -f ($dirty.Count - 5)) }
                return @{
                    Status = 'HALT'
                    Reason = 'RED'
                    Detail = "DONE だが作業ツリーが汚れている(コミット漏れか、戻し忘れた変異): $head"
                    Log    = $log
                }
            }
        }

        return @{ Status = 'DONE'; Log = $log }
    }
    # 合図が読めないのは「フェーズが出し忘れた」場合だけではない。**プロセスが殺された・
    # 落ちた場合も、result イベントごと無い。** どちらも踏んだか判らないので同じ扱いにする。
    return @{
        Status = 'HALT'
        Reason = 'NO-SENTINEL'
        Detail = Get-NoSentinelDetail -Log $log
        Log    = $log
    }
}

New-Item -ItemType Directory -Path $LogDir -Force | Out-Null

# `-Status` — **走行中かどうかを目で判断しないための入口。** 生ログの末尾は終端ではない
# ので、答えるのは PID の生存だけである(#109)。
if ($Status) {
    $live = @(Get-LivePipelineLock -LockDir $LogDir)
    if ($live.Count -eq 0) {
        Write-Host "パイプラインは走っていません(生きているロックなし)。" -ForegroundColor Green
        exit 0
    }
    foreach ($l in $live) {
        Write-Host ("走行中: issue #{0} / フェーズ {1} / PID {2} / 開始 {3}" -f $l.issue, $l.phase, $l.pid, $l.started) -ForegroundColor Yellow
        if ($l.PSObject.Properties.Name -contains 'log' -and $l.log) { Write-Host ("    log: {0}" -f $l.log) }
    }
    Write-Host "**本体の作業ツリーはパイプラインのものです。** 止めるなら Stop-Process -Id <PID> です。"
    exit 1
}

# **二重起動を止める。** 同じ本体ツリーで2本走ると、片方の `git checkout` が
# もう片方の編集を消す(#109 の実害はこれである)。
$lockPath = New-PipelineLock -LockDir $LogDir -Issue $Issue -Phase $From
if (-not $lockPath) {
    $live = @(Get-LivePipelineLock -LockDir $LogDir)[0]
    Write-Host ""
    Write-Host ("!!! 既にパイプラインが走っています: issue #{0} / フェーズ {1} / PID {2}" -f $live.issue, $live.phase, $live.pid) -ForegroundColor Red
    Write-Host "    先に終わらせるか、Stop-Process -Id $($live.pid) で止めてください。"
    exit 3
}

# 子プロセス(= フェーズ本体の claude)に渡す。PreToolUse フック pipeline-guard.ps1 は
# **これがあるセッションだけ**を本体ツリーへの書き手として通す。
$env:VISIONARY_PIPELINE_ISSUE = [string]$Issue

try {
    # **古い本体で打たない**(#146 決定4)。枠のプローブより先に置くのは、fetch がタダで
    # あり、拒否すると分かっているのに $0.11 を払う理由が無いためである。
    if ($From -eq 'impl' -and -not (Test-FreshnessGate)) { exit 5 }

    # **枠が足りなければ起動しない**(#132)。ロックを取った後に置くのは、二重起動の拒否
    # (終了コード 3)が先に出るべきだからである — 走行中と分かっているのにプローブへ
    # $0.11 を払う理由が無い。拒否しても `finally` がロックを外す。
    #
    # **`-MinRemaining 0` は検査しない。** プローブも打たないので $0.11 も枠の 0.05% も使わない。
    if (-not $PSBoundParameters.ContainsKey('MinRemaining')) { $MinRemaining = $DefaultMinRemaining[$From] }
    if ($MinRemaining -gt 0 -and -not (Test-BudgetGate -Required $MinRemaining)) { exit 4 }

    $phases = if ($From -eq 'wrap') { @('wrap') } else { @('impl', 'wrap') }

    foreach ($phase in $phases) {
        Set-PipelineLockPhase -Path $lockPath -Phase $phase
        $r = Invoke-Phase -Command $phase

        if ($r.Status -eq 'HALT') {
            $title = "Visionary #$Issue — フェーズ /$phase が停止"
            $text = "{0}`n{1}`n`nlog: {2}" -f $r.Reason, $r.Detail, $r.Log
            Write-Host ""
            Write-Host "!!! HALT [$($r.Reason)] $($r.Detail)" -ForegroundColor Yellow
            Send-Notification -Title $title -Text $text -Level 'Warning'
            exit 2
        }
    }

    if ($DryRun) {
        Write-Host "    (DryRun: 完了通知を1回出します — 通知が届くかの確認を兼ねています)"
    }
    Send-Notification -Title "Visionary #$Issue — PR まで完了" `
        -Text "フェーズ2・3 が停止則に当たらず通りました。PR を確認してください。"
    Write-Host ""
    Write-Host "=== #$Issue 完了 ===" -ForegroundColor Green
} finally {
    # **`exit` を通っても解放する。** ここが漏れると、次の起動が死んだロックに阻まれる
    # …ことは無い(生存判定が PID を見るので)が、`-Status` が嘘をつく。
    Remove-PipelineLock -Path $lockPath
}
