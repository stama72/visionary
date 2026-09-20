<#
.SYNOPSIS
    PreToolUse フック。**パイプラインが走っている間、本体の作業ツリーへの書き込みを止める。**

.DESCRIPTION
    CLAUDE.md と [05-phase-sessions](../docs/process/05-phase-sessions.md) は「走っている間、
    本体の `visionary/` はパイプラインのものである」と定めていたが、**これは規約だけで、
    機械はまったく見ていなかった。** 実際に破れている([#109](https://github.com/stama72/visionary/issues/109)):
    W2-08 で 2 つ目のセッションが走行中のツリーへ 3 回書き込み、`git checkout` で
    implementer が当てたばかりの変異を消した。ログの中では原因不明の環境問題に見えていた。

    CLAUDE.md 自身が新しい規約に「これは機械で守れるか」を問えと定めている。守れるので守る。

    判定はこの順:

    1. `$env:VISIONARY_PIPELINE_ISSUE` があれば**素通し** — そのセッションはパイプラインの
       フェーズ本体である(`pipeline.ps1` が子プロセスに渡す)
    2. 生きているロックが無ければ素通し(通常はここで終わる)
    3. worktree の中なら素通し — **worktree を分ける**のが 05-phase-sessions の逃がし方である
    4. 本体ツリーの書き込みだけを止める(exit 2)

    **例外はすべて素通しに倒す。** フックが落ちて開発が止まるほうが、守り損ねるより高くつく。
    止められなかったことは画面に出る。
#>

$ErrorActionPreference = 'Stop'

# **入出力を UTF-8 に固定する。** 既定(cp932)のままだと、フックの標準エラーに載せた
# 日本語が化けて相手のセッションに届く。ここは「なぜ止まったか」を伝える唯一の経路である。
try {
    [Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
} catch { }

try {
    $raw = [Console]::In.ReadToEnd()
    $ev = $raw | ConvertFrom-Json

    # 1. パイプライン自身のセッションは素通し。
    if ($env:VISIONARY_PIPELINE_ISSUE) { exit 0 }

    $toolName = [string]$ev.tool_name
    $cwd = [string]$ev.cwd
    if (-not $cwd) { exit 0 }

    # 本体ツリー = 共通 .git の親。worktree から呼ばれても本体を指す。
    $common = & git -C $cwd rev-parse --path-format=absolute --git-common-dir 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $common) { exit 0 }
    $mainRoot = (Resolve-Path (Split-Path -Parent ([string]$common).Trim())).Path

    . (Join-Path $PSScriptRoot 'pipeline-lock.ps1')
    $live = @(Get-LivePipelineLock -LockDir (Join-Path $mainRoot '.pipeline'))
    if ($live.Count -eq 0) { exit 0 }

    $worktreeMark = [IO.Path]::Combine('.claude', 'worktrees')
    function Test-InMainTree {
        param([string]$Path)
        if (-not $Path) { return $false }
        $full = try { [IO.Path]::GetFullPath($Path) } catch { return $false }
        if (-not $full.StartsWith($mainRoot, [StringComparison]::OrdinalIgnoreCase)) { return $false }
        # worktree は本体のパスの下に居るが、本体ツリーではない。
        return -not ($full -like ("*{0}*" -f $worktreeMark))
    }

    $target = $null
    $what = $null

    switch -Regex ($toolName) {
        '^(Edit|Write|NotebookEdit)$' {
            $target = [string]$ev.tool_input.file_path
            if (-not $target) { $target = [string]$ev.tool_input.notebook_path }
            if (-not $target) { $target = $cwd }
            $what = "$toolName $target"
        }
        '^(Bash|PowerShell)$' {
            $cmd = [string]$ev.tool_input.command
            # **書き込む形のコマンドだけを止める。** `git status` や `cat` まで止めると、
            # 走行中の様子を見にいくこと自体ができなくなる。
            $writeish = @(
                'git\s+(checkout|switch|restore|reset|commit|add|rm|mv|stash|merge|rebase|clean|apply|cherry-pick|push)'
                '\brm\s'; '\bmv\s'; '\bcp\s'; 'sed\s+-i'; '\btee\b'
                'dotnet\s+format(?!.*--verify-no-changes)'
                'Set-Content|Out-File|Remove-Item|New-Item|Move-Item|Copy-Item|Add-Content'
                'gh\s+pr\s+(create|merge|edit)'
                '>\s*\S'
            ) -join '|'
            if ($cmd -notmatch $writeish) { exit 0 }
            $target = $cwd
            $what = "$toolName : $cmd"
        }
        default { exit 0 }
    }

    if (-not (Test-InMainTree -Path $target)) { exit 0 }

    $l = $live[0]
    $msg = @"
パイプライン(issue #$($l.issue) / フェーズ $($l.phase) / PID $($l.pid))が走行中です。**走っている間、本体の作業ツリーはパイプラインのものです**(CLAUDE.md / docs/process/05-phase-sessions.md)。
止めた操作: $what
別のタスクを進めるなら worktree を分けてください(.claude/worktrees/)。パイプラインが止まった証拠は PID $($l.pid) が消えることです — 生ログ .pipeline/*.jsonl の末尾は終端ではありません。
"@
    [Console]::Error.WriteLine($msg)
    exit 2
} catch {
    # フック自身の失敗で開発を止めない。守れなかったことだけ見えるようにする。
    Write-Host "pipeline-guard: 判定できませんでした($_)。素通しします。"
    exit 0
}
