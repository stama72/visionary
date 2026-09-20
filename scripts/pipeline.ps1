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

.EXAMPLE
    pwsh scripts/pipeline.ps1 -Issue 35
    pwsh scripts/pipeline.ps1 -Issue 35 -From wrap   # フェーズ3 だけやり直す
#>
[CmdletBinding(DefaultParameterSetName = 'Run')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Run')][int]$Issue,
    [Parameter(ParameterSetName = 'Run')][ValidateSet('impl', 'wrap')][string]$From = 'impl',
    [Parameter(ParameterSetName = 'Run')][switch]$DryRun,
    [Parameter(Mandatory = $true, ParameterSetName = 'Status')][switch]$Status
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$LogDir = Join-Path $RepoRoot '.pipeline'

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
# 要らなくなる可能性があり、そのときに見直す(#106 に申し送り済み)。
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

function Send-DesktopNotification {
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
    $phases = if ($From -eq 'wrap') { @('wrap') } else { @('impl', 'wrap') }

    foreach ($phase in $phases) {
        Set-PipelineLockPhase -Path $lockPath -Phase $phase
        $r = Invoke-Phase -Command $phase

        if ($r.Status -eq 'HALT') {
            $title = "Visionary #$Issue — フェーズ /$phase が停止"
            $text = "{0}`n{1}`n`nlog: {2}" -f $r.Reason, $r.Detail, $r.Log
            Write-Host ""
            Write-Host "!!! HALT [$($r.Reason)] $($r.Detail)" -ForegroundColor Yellow
            Send-DesktopNotification -Title $title -Text $text -Level 'Warning'
            exit 2
        }
    }

    if ($DryRun) {
        Write-Host "    (DryRun: 完了通知を1回出します — 通知が届くかの確認を兼ねています)"
    }
    Send-DesktopNotification -Title "Visionary #$Issue — PR まで完了" `
        -Text "フェーズ2・3 が停止則に当たらず通りました。PR を確認してください。"
    Write-Host ""
    Write-Host "=== #$Issue 完了 ===" -ForegroundColor Green
} finally {
    # **`exit` を通っても解放する。** ここが漏れると、次の起動が死んだロックに阻まれる
    # …ことは無い(生存判定が PID を見るので)が、`-Status` が嘘をつく。
    Remove-PipelineLock -Path $lockPath
}
