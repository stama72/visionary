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
    4. `.pipeline/` への書き込みは素通し — **ロックの回収路**だから(Git 管理外)
    5. 本体ツリーの書き込みだけを止める(exit 2)

    **`Edit` / `Write` は対象ファイルを見るが、`Bash` / `PowerShell` は cwd と「コマンドに
    現れる本体の絶対パス」しか見ていない。** 相対パスで本体へ届く形(`git -C ../../..`、
    `dotnet --project ../..`)は止まらない。抜け道が無限にあるので、機械を厚くするより
    **境界を正直に書く**側に倒してある(05-phase-sessions「何が止まらないか」)。

    **例外はすべて素通しに倒す。** フックが落ちて開発が止まるほうが、守り損ねるより高くつく。
    ただし**黙って素通しはしない** — `.pipeline/guard-failures.log` に1行残す。`exit 0` の
    フックの標準出力は transcript(ctrl+o)にしか出ないので、画面だけに頼ると
    **守る側が静かに壊れる**(この差分が消しにきたものと同じ形になる)。
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

    # パスの綴り替え(下の 2 関数)を効かせてよいホストか。`$IsWindows` は Windows PowerShell 5.1
    # で未定義 = $false と評価され、**守りが黙って消える**ので、区切り文字そのもので見る。
    $script:IsWindowsHost = ([IO.Path]::DirectorySeparatorChar -eq '\')

    $worktreeMark = [IO.Path]::Combine('.claude', 'worktrees')
    $pipelineDir = Join-Path $mainRoot '.pipeline'

    function ConvertTo-GitBashPath {
        <#
            `C:\Users\<you>\visionary` -> `/c/Users/<you>/visionary`。
            **この環境の `Bash` ツールは Git Bash である**ため、本体を名指しする綴りが
            Windows 形だけではない。**照合しているのは、この環境で実際に出た 3 通りである**
            (`\` 区切り / `/` 区切り / この形)。**閉じた列挙ではない** — ホーム相対の
            `~/...`(Git Bash が `$HOME` へ展開して本体に届く。素通しを実測済み)と
            `/cygdrive/c/...` は当たらない([#152](https://github.com/stama72/visionary/issues/152))。
            綴りを 1 つずつ足すしかないが、形はここ 1 か所から機械的に作る。

            ドライブ文字を持たないパス(UNC など)はそのまま返す — 綴り替える先が無い。
            **綴り替えが意味を持つのは Windows だけである**ため、区切りが `/` の OS では何もしない
            (クラウドセッションの Linux では本体が `/home/user/visionary` で、この形は存在しない)。
        #>
        param([string]$Path)
        if (-not $script:IsWindowsHost) { return $Path }
        if ($Path -match '^([A-Za-z]):(.*)$') {
            $rest = $Matches[2].Replace('\', '/').TrimStart('/')
            return ('/{0}/{1}' -f $Matches[1].ToLowerInvariant(), $rest)
        }
        return $Path
    }

    function ConvertFrom-GitBashPath {
        <#
            `/c/Users/...` -> `C:\Users\...`。逆向きの綴り替え。**Windows の API はこの形を
            絶対パスとして解さない** — `[IO.Path]::GetFullPath('/c/Users/x')` はカレントドライブ
            直下の `C:\c\Users\x` になり、本体ツリーへの前方一致から**静かに**外れる。例外にすら
            ならないので、素通ししていることが表に出ない。

            `Edit` / `Write` はこの綴りを受けて実際の場所に書く(2026-09-21 実測)ので、
            ここは理屈の上の穴ではない。
        #>
        param([string]$Path)
        # **Linux ではこの綴り替えが守りを消す。** `/c/...` は Windows の綴りとしてしか意味が
        # 無いのに、本体が `/c/` 以下にある Linux ホストで当てると、本物のパスを `C:\...` へ
        # 変えてしまい、本体ツリーへの前方一致から外れる。Windows でだけ効かせる。
        if (-not $script:IsWindowsHost) { return $Path }
        if ($Path -match '^/([A-Za-z])(/.*)?$') {
            $rest = if ($Matches[2]) { $Matches[2].Replace('/', '\') } else { '\' }
            return ('{0}:{1}' -f $Matches[1].ToUpperInvariant(), $rest)
        }
        return $Path
    }

    function Test-InMainTree {
        param([string]$Path)
        if (-not $Path) { return $false }
        $Path = ConvertFrom-GitBashPath $Path
        $full = try { [IO.Path]::GetFullPath($Path) } catch { return $false }
        if (-not $full.StartsWith($mainRoot, [StringComparison]::OrdinalIgnoreCase)) { return $false }
        # worktree は本体のパスの下に居るが、本体ツリーではない。
        if ($full -like ("*{0}*" -f $worktreeMark)) { return $false }
        # `.pipeline/` は作業ツリーではなく、**ロックの回収路**である(Git 管理外)。
        # ここを止めると、誤検知で居座ったロックを Claude のセッションから外せなくなる。
        if ($full.StartsWith($pipelineDir, [StringComparison]::OrdinalIgnoreCase)) { return $false }
        return $true
    }

    function Test-TouchesMainTreeByAbsolutePath {
        <#
            **`Bash` / `PowerShell` は cwd でしか判定できない。** worktree に居るセッションが
            `git -C <本体> checkout` や本体の絶対パスへの `sed -i` を打つと、cwd は worktree
            なので素通しする — #109 の実害そのものの形である。せめて**本体の絶対パスが
            コマンドに現れる場合**だけは拾う。

            worktree のパスは本体のパスを前方一致で含むので、一致箇所の直後が
            `.claude/worktrees` なら本体ではない。**これは網羅ではない**(相対パスでの
            `git -C ../..` や `dotnet --project` は拾えない)。境界は 05-phase-sessions に書く。
        #>
        param([string]$Command)
        if (-not $Command) { return $false }

        # **Git Bash 形式(`/c/Users/...`)を落とすと、Bash ツールの経路がまるごと素通しする。**
        # この環境の `Bash` は Git Bash なので、本体を名指しする綴りとしては `C:\Users\...` と
        # 同じくらい素直に出る。実際 2026-09-21 に走行中の本体へ `cat > /c/Users/.../_probe.md`
        # が通り、untracked を 1 本残した([#138](https://github.com/stama72/visionary/issues/138))。
        # 綴りは `$mainRoot` から機械的に作る。照合は OrdinalIgnoreCase なのでドライブの大小は問わない。
        $needles = @($mainRoot, $mainRoot.Replace('\', '/'), (ConvertTo-GitBashPath $mainRoot))
        foreach ($needle in ($needles | Sort-Object -Unique)) {
            $from = 0
            while ($true) {
                $at = $Command.IndexOf($needle, $from, [StringComparison]::OrdinalIgnoreCase)
                if ($at -lt 0) { break }
                $rest = $Command.Substring($at + $needle.Length)
                $isWorktree = $rest -match '^[\\/]\.claude[\\/]worktrees'
                $isPipeline = $rest -match '^[\\/]\.pipeline'
                if (-not $isWorktree -and -not $isPipeline) { return $true }
                $from = $at + $needle.Length
            }
        }
        return $false
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
                # `git -C <path> checkout` のように、動詞の前に大域オプションが挟まる形も拾う。
                # **これを落としていたのが最初の実装で、worktree から本体を触る経路がまるごと素通しした。**
                'git\s+((-C|-c|--git-dir|--work-tree)(\s+|=)\S+\s+)*(checkout|switch|restore|reset|commit|add|rm|mv|stash|merge|rebase|clean|apply|cherry-pick|push)'
                '\brm\s'; '\bmv\s'; '\bcp\s'; 'sed\s+-i'; '\btee\b'
                'dotnet\s+format(?!.*--verify-no-changes)'
                'Set-Content|Out-File|Remove-Item|New-Item|Move-Item|Copy-Item|Add-Content'
                'gh\s+pr\s+(create|merge|edit)'
                # **リダイレクトは「ファイルへ書き出す形」だけを止める。** 素朴に `>` の後ろに
                # 非空白が続く形を止めると、`grep ... 2>&1` のような**読み取りだけのコマンドが
                # 止まる。** 走行中の様子を worktree から覗く経路で毎回踏むうえ、
                # 「読み取りは止めない」という上の方針と矛盾する。除くのは2種類:
                #   `>&1` / `>&2`  ハンドルの合流であって、ファイルには書かない
                #   `/dev/null` / `$null`  捨て先であって、作業ツリーには残らない
                # `>> file` は追記(書き込み)なので**止まる側に残す**。
                '>\s*(?!&|/dev/null|\$null)\S'
            ) -join '|'
            if ($cmd -notmatch $writeish) { exit 0 }
            # ロックそのものを触るコマンドは素通し(回収路)。
            if ($cmd -match '\.pipeline' -and $cmd -match '\.lock') { exit 0 }
            # cwd が本体でなくても、本体の絶対パスを名指ししているなら本体への操作である。
            if (Test-TouchesMainTreeByAbsolutePath -Command $cmd) { $target = $mainRoot }
            else { $target = $cwd }
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
    # フック自身の失敗で開発を止めない。**ただし黙って素通しはしない。**
    #
    # `Write-Host` は exit 0 のフックでは transcript(ctrl+o)にしか出ないので、
    # 画面だけに頼ると「守る側が静かに壊れる」— この差分が消しにきたものと同じ形になる。
    # 追記先は `.pipeline/`(Git 管理外・作業ツリーではない)。
    try {
        $dir = if ($mainRoot) { Join-Path $mainRoot '.pipeline' } else { Join-Path (Split-Path -Parent $PSScriptRoot) '.pipeline' }
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        $line = "{0}`t{1}`t{2}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $ev.tool_name, ($_ -replace "`r?`n", ' ')
        Add-Content -LiteralPath (Join-Path $dir 'guard-failures.log') -Value $line -Encoding UTF8
    } catch { }
    Write-Host "pipeline-guard: 判定できませんでした($_)。素通しします(.pipeline/guard-failures.log に記録)。"
    exit 0
}
