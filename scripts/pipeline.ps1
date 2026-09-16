<#
.SYNOPSIS
    ADR-0010 のフェーズ連鎖。フェーズ2 -> フェーズ3 を別プロセスで繋ぎ、停止則に当たったら
    デスクトップ通知を出して止まる。

.DESCRIPTION
    各フェーズは `claude -p` の別プロセスで走る。プロセスが別なのでコンテキスト窓も別で、
    ADR-0009 が開発者の `/clear` に負わせていた隔離と意味論が一致する。

    **Git Bash からは呼べない。** MSYS が先頭の `/` を `C:/Program Files/Git/...` に変換し、
    `/impl 34` がスラッシュコマンドとして解決されない。PowerShell から回すこと。

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
$AllowedTools = @(
    'Read', 'Glob', 'Grep', 'Edit', 'Write', 'TodoWrite',
    'Task', 'Agent',
    'Bash(dotnet:*)', 'Bash(git:*)', 'Bash(gh:*)'
)

# フェーズごとのモデル。CLI の --model は コマンド側の `model:` frontmatter より強い(実測)。
$PhaseModel = @{ impl = 'opus'; wrap = 'sonnet' }

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

function Invoke-Phase {
    param([string]$Command)

    $model = $PhaseModel[$Command]
    $prompt = "/$Command $Issue"
    $log = Join-Path $LogDir ("{0}-{1}-{2}.log" -f $Issue, $Command, (Get-Date -Format 'yyyyMMdd-HHmmss'))

    Write-Host ""
    Write-Host "=== /$Command $Issue  (model: $model) ===" -ForegroundColor Cyan
    Write-Host "    log: $log"

    if ($DryRun) {
        Write-Host "    [DryRun] claude -p `"$prompt`" --model $model --allowedTools ..."
        return 'DONE'
    }

    # プロンプトは可変長フラグ(--allowedTools)より前に置く。後ろだと引数として吸われる(実測)。
    & claude -p $prompt --model $model --allowedTools $AllowedTools 2>&1 |
        Tee-Object -FilePath $log

    $out = Get-Content -Path $log -Raw -Encoding UTF8

    # **fail-closed。** HALT が無くても DONE が無ければ進めない。
    # 停止則を踏んだかどうか判らないまま次のフェーズへ渡すほうが危ない。
    if ($out -match 'PIPELINE:\s*HALT\s*(\S+)\s*(.*)') {
        return @{ Status = 'HALT'; Reason = $Matches[1]; Detail = $Matches[2].Trim(); Log = $log }
    }
    if ($out -match 'PIPELINE:\s*DONE') {
        return @{ Status = 'DONE'; Log = $log }
    }
    return @{ Status = 'HALT'; Reason = 'NO-SENTINEL'; Detail = 'フェーズが DONE も HALT も出さずに終了した'; Log = $log }
}

New-Item -ItemType Directory -Path $LogDir -Force | Out-Null

$phases = if ($From -eq 'wrap') { @('wrap') } else { @('impl', 'wrap') }

foreach ($phase in $phases) {
    $r = Invoke-Phase -Command $phase
    if ($r -is [string]) { continue }   # DryRun

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
