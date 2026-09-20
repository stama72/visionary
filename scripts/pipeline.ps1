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
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][int]$Issue,
    [ValidateSet('impl', 'wrap')][string]$From = 'impl',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$LogDir = Join-Path $RepoRoot '.pipeline'

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
        Detail = 'フェーズが DONE も HALT も出さずに終了した(出し忘れ / 異常終了 / 中断)'
        Log    = $log
    }
}

New-Item -ItemType Directory -Path $LogDir -Force | Out-Null

$phases = if ($From -eq 'wrap') { @('wrap') } else { @('impl', 'wrap') }

foreach ($phase in $phases) {
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
