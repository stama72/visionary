<#
.SYNOPSIS
    `pipeline-guard.ps1` が「止めるべきものを止め、通すべきものを通す」ことを実測する。

.DESCRIPTION
    ガードは **PreToolUse フックであり、普通のテストから呼べない。** 使い捨ての git リポジトリに
    生きたロックを置き、フックへ直接イベント(JSON)を流して終了コードを見る、という形でしか
    測れない。**この手順を 3 回作り直した**(#132 で 7 ケース、#138 で 15+2 ケース、#152)ので
    残すことにした([#152](https://github.com/stama72/visionary/issues/152) 決定 2)。

    **CI(Ubuntu)が守るのはホスト非依存の範囲だけである。** ガードの綴り替え
    (`C:\` / `C:/` / `/c/` / `/cygdrive/`)は Windows でだけ効く設計なので、Linux では
    skip する。**skip は黙って通さない** — 件数と理由を最後に出す。緑を「全部守られている」と
    読ませないためで、これはガードの docstring を閉じた列挙に読ませないのと同じ理屈である。
    全件を走らせるには Windows の開発機で打つ。

    fixture を **`$HOME` の下に作る**のは `~/...` 形のケースのためである(#152)。

.PARAMETER KeepFixture
    使い捨てリポジトリを消さずに残す。落ちたケースを手で追うとき用。
#>

[CmdletBinding()]
param([switch]$KeepFixture)

# **外部コマンドの非ゼロ終了を terminating error にしない。** ここは終了コードそのものを
# 測る道具であり、PowerShell 7.4 の既定($PSNativeCommandUseErrorActionPreference = $true)の
# ままだと、ガードが 2 を返した瞬間にハーネスが落ちる。
$PSNativeCommandUseErrorActionPreference = $false
$ErrorActionPreference = 'Continue'

$IsWindowsHost = ([IO.Path]::DirectorySeparatorChar -eq '\')
$GuardPath = (Resolve-Path (Join-Path $PSScriptRoot 'pipeline-guard.ps1')).Path
$HomeDir = ([string]$HOME).TrimEnd('\', '/')

# ---- fixture -----------------------------------------------------------------

$fixture = Join-Path $HomeDir (".visionary-guard-check-{0}" -f $PID)
$outside = Join-Path $HomeDir (".visionary-guard-outside-{0}" -f $PID)
$lockPath = $null

function New-Fixture {
    New-Item -ItemType Directory -Path $fixture -Force | Out-Null
    New-Item -ItemType Directory -Path $outside -Force | Out-Null
    git init -q $fixture
    git -C $fixture config user.email 'guard-check@example.invalid'
    git -C $fixture config user.name 'guard-check'
    Set-Content -LiteralPath (Join-Path $fixture 'README.md') -Value 'fixture' -Encoding UTF8
    New-Item -ItemType Directory -Path (Join-Path $fixture 'docs') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $fixture 'docs/x.md') -Value 'x' -Encoding UTF8
    git -C $fixture add -A | Out-Null
    git -C $fixture commit -q -m 'init' | Out-Null
    git -C $fixture worktree add -q (Join-Path $fixture '.claude/worktrees/wt') -b wt | Out-Null
}

function Remove-Fixture {
    foreach ($d in @($fixture, $outside)) {
        if (Test-Path -LiteralPath $d) {
            Remove-Item -LiteralPath $d -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# ---- 綴り --------------------------------------------------------------------
# **ガードの関数は呼ばない。** 実装から綴りを借りると、実装が綴りを間違えたときテストも
# 同じように間違え、両方そろって緑になる。ここは独立に綴る。

function ConvertTo-BashSpelling {
    param([string]$Path)
    if ($Path -match '^([A-Za-z]):(.*)$') {
        return ('/{0}{1}' -f $Matches[1].ToLowerInvariant(), $Matches[2].Replace('\', '/'))
    }
    return $Path
}

$relFromHome = $fixture.Substring($HomeDir.Length).TrimStart('\', '/')

$MainWin = $fixture                                        # C:\Users\you\...  /  /home/you/...
$MainFwd = $fixture.Replace('\', '/')                      # C:/Users/you/...
$MainBash = ConvertTo-BashSpelling $fixture                # /c/Users/you/...
$MainCyg = '/cygdrive' + $MainBash                         # /cygdrive/c/Users/you/...
$MainTilde = '~/' + $relFromHome.Replace('\', '/')         # ~/...
$MainTildeBk = '~\' + $relFromHome.Replace('/', '\')       # ~\...
$Worktree = Join-Path $fixture '.claude/worktrees/wt'

# ---- ケース ------------------------------------------------------------------
# expect: 2 = 止まる / 0 = 通る。win = Windows でだけ意味を持つ綴り(Linux では skip)。

$cases = @(
    # --- #152: ホーム相対の絶対パス。ここが本 issue の本体である -----------------
    @{ g = '~'; n = 'Bash: cat > ~/<本体>/docs/x.md'; tool = 'Bash'; cwd = $Worktree
       cmd = "cat > $MainTilde/docs/x.md"; expect = 2 }
    @{ g = '~'; n = 'Bash: sed -i ~/<本体>/docs/x.md'; tool = 'Bash'; cwd = $Worktree
       cmd = "sed -i s/a/b/ $MainTilde/docs/x.md"; expect = 2 }
    @{ g = '~'; n = 'Edit: file_path = ~/<本体>/docs/x.md'; tool = 'Edit'; cwd = $Worktree
       path = "$MainTilde/docs/x.md"; expect = 2 }
    @{ g = '~'; n = 'Write: file_path = ~/<本体>/new.md'; tool = 'Write'; cwd = $Worktree
       path = "$MainTilde/new.md"; expect = 2 }
    @{ g = '~'; n = 'PowerShell: Set-Content ~\<本体>\x.md(円記号区切り)'; tool = 'PowerShell'; cwd = $Worktree
       cmd = "Set-Content $MainTildeBk\x.md -Value y"; expect = 2; win = $true }
    @{ g = '~'; n = 'Edit: file_path = ~\<本体>\x.md(円記号区切り)'; tool = 'Edit'; cwd = $Worktree
       path = "$MainTildeBk\x.md"; expect = 2; win = $true }
    # carve-out: 新しい綴りでも保たれること
    @{ g = '~'; n = 'carve-out: Edit ~/<本体>/.claude/worktrees/wt/a.md'; tool = 'Edit'; cwd = $Worktree
       path = "$MainTilde/.claude/worktrees/wt/a.md"; expect = 0 }
    @{ g = '~'; n = 'carve-out: Bash cat > ~/<本体>/.pipeline/x.json'; tool = 'Bash'; cwd = $Worktree
       cmd = "cat > $MainTilde/.pipeline/x.json"; expect = 0 }
    @{ g = '~'; n = 'carve-out: 読み取りだけの grep ~/<本体>'; tool = 'Bash'; cwd = $Worktree
       cmd = "grep -r foo $MainTilde/docs"; expect = 0 }
    @{ g = '~'; n = '本体の外: cat > ~/<別ディレクトリ>/x.md'; tool = 'Bash'; cwd = $Worktree
       cmd = "cat > ~/.visionary-guard-outside-$PID/x.md"; expect = 0 }
    @{ g = '~'; n = '~otheruser は展開しない(追わないと決めた境界)'; tool = 'Edit'; cwd = $Worktree
       path = "~someone/$relFromHome/docs/x.md"; expect = 0 }

    # --- #152: /cygdrive/c/... 形 ------------------------------------------------
    # **Bash / PowerShell 側は `/c/...` の綴りが部分一致で当てている。** `/cygdrive/c/X` は
    # `/c/X` を含み、照合は IndexOf だからである。`Get-MainTreeNeedle` に `/cygdrive` を足す
    # 行は消しても緑のままだったので置いていない(#152 決定3)。**下の 3 件が測っているのは
    # 「この綴りでも本体に届かない」ことであって、綴りの登録が効いていることではない。**
    # `Edit` 側だけは `ConvertFrom-GitBashPath` の剥がしが要る(消すと落ちる。実測済み)。
    @{ g = '/cygdrive'; n = 'Bash: cat > /cygdrive/c/<本体>/docs/x.md'; tool = 'Bash'; cwd = $Worktree
       cmd = "cat > $MainCyg/docs/x.md"; expect = 2; win = $true }
    @{ g = '/cygdrive'; n = 'Edit: file_path = /cygdrive/c/<本体>/docs/x.md'; tool = 'Edit'; cwd = $Worktree
       path = "$MainCyg/docs/x.md"; expect = 2; win = $true }
    @{ g = '/cygdrive'; n = 'carve-out: /cygdrive/c/<本体>/.claude/worktrees/wt/a.md'; tool = 'Edit'; cwd = $Worktree
       path = "$MainCyg/.claude/worktrees/wt/a.md"; expect = 0; win = $true }

    # --- #134: dotnet new ---------------------------------------------------------
    @{ g = 'dotnet'; n = 'dotnet new console -o .probe(本体で)'; tool = 'Bash'; cwd = $MainWin
       cmd = 'dotnet new console -o .probe'; expect = 2 }
    @{ g = 'dotnet'; n = 'dotnet new console --output .probe(本体で)'; tool = 'Bash'; cwd = $MainWin
       cmd = 'dotnet new console --output .probe'; expect = 2 }
    @{ g = 'dotnet'; n = 'dotnet new console(出力先なしでも cwd に作る)'; tool = 'Bash'; cwd = $MainWin
       cmd = 'dotnet new console'; expect = 2 }
    @{ g = 'dotnet'; n = 'dotnet new console -o .probe(worktree でなら通る)'; tool = 'Bash'; cwd = $Worktree
       cmd = 'dotnet new console -o .probe'; expect = 0 }
    @{ g = 'dotnet'; n = 'dotnet build は止めない'; tool = 'Bash'; cwd = $MainWin
       cmd = 'dotnet build Visionary.sln -c Release'; expect = 0 }
    @{ g = 'dotnet'; n = 'dotnet format は止まる(既存)'; tool = 'Bash'; cwd = $MainWin
       cmd = 'dotnet format Visionary.sln'; expect = 2 }
    @{ g = 'dotnet'; n = 'dotnet format --verify-no-changes は通る(既存)'; tool = 'Bash'; cwd = $MainWin
       cmd = 'dotnet format Visionary.sln --verify-no-changes'; expect = 0 }
    # **意図した素通し。** 足さないと決めた(#152 決定 1)ので、素通しを記録として残す。
    # ここが落ちたら決定が覆ったということで、05「何が止まらないか」も直す必要がある。
    @{ g = 'dotnet'; n = 'dotnet fsi は止めない(足さないと決めた境界)'; tool = 'Bash'; cwd = $MainWin
       cmd = 'dotnet fsi probe.fsx'; expect = 0 }

    # --- 既存の綴り(#138)の回帰 ------------------------------------------------
    @{ g = '綴り'; n = 'Edit: C:\<本体>\docs\x.md'; tool = 'Edit'; cwd = $Worktree
       path = (Join-Path $fixture 'docs\x.md'); expect = 2; win = $true }
    @{ g = '綴り'; n = 'Bash: cat > C:/<本体>/docs/x.md'; tool = 'Bash'; cwd = $Worktree
       cmd = "cat > $MainFwd/docs/x.md"; expect = 2; win = $true }
    @{ g = '綴り'; n = 'Bash: cat > /c/<本体>/docs/x.md'; tool = 'Bash'; cwd = $Worktree
       cmd = "cat > $MainBash/docs/x.md"; expect = 2; win = $true }
    @{ g = '綴り'; n = 'Edit: /c/<本体>/docs/x.md'; tool = 'Edit'; cwd = $Worktree
       path = "$MainBash/docs/x.md"; expect = 2; win = $true }
    @{ g = '綴り'; n = 'Bash: git -C /c/<本体> checkout .'; tool = 'Bash'; cwd = $Worktree
       cmd = "git -C $MainBash checkout ."; expect = 2; win = $true }

    # --- cwd 判定と書き込み判定(#109 / #132)の回帰 ------------------------------
    @{ g = '基本'; n = '本体で git checkout .'; tool = 'Bash'; cwd = $MainWin
       cmd = 'git checkout .'; expect = 2 }
    @{ g = '基本'; n = 'worktree から git -C <本体> checkout .'; tool = 'Bash'; cwd = $Worktree
       cmd = "git -C $MainWin checkout ."; expect = 2 }
    @{ g = '基本'; n = 'worktree 内の git commit は通る'; tool = 'Bash'; cwd = $Worktree
       cmd = 'git commit -m x'; expect = 0 }
    @{ g = '基本'; n = '本体で読み取りだけの cat'; tool = 'Bash'; cwd = $MainWin
       cmd = 'cat docs/x.md'; expect = 0 }
    @{ g = '基本'; n = '本体で grep ... 2>&1(ハンドルの合流は書き込みではない)'; tool = 'Bash'; cwd = $MainWin
       cmd = 'grep -r foo . 2>&1'; expect = 0 }
    @{ g = '基本'; n = '本体で cmd 2>/dev/null(捨て先は書き込みではない)'; tool = 'Bash'; cwd = $MainWin
       cmd = 'git status 2>/dev/null'; expect = 0 }
    @{ g = '基本'; n = '本体で echo >> file(追記は止まる)'; tool = 'Bash'; cwd = $MainWin
       cmd = 'echo x >> docs/x.md'; expect = 2 }
    @{ g = '基本'; n = '本体で Remove-Item'; tool = 'PowerShell'; cwd = $MainWin
       cmd = 'Remove-Item docs/x.md -Force'; expect = 2 }
    @{ g = '基本'; n = '本体の .pipeline/*.lock を触るのは通る(回収路)'; tool = 'Bash'; cwd = $MainWin
       cmd = 'rm .pipeline/999.lock'; expect = 0 }
    @{ g = '基本'; n = '意図した境界: 相対パスで本体へ届く git -C ../../..'; tool = 'Bash'; cwd = $Worktree
       cmd = 'git -C ../../.. checkout .'; expect = 0 }
    @{ g = '基本'; n = 'パイプライン自身のセッション(VISIONARY_PIPELINE_ISSUE)は通る'; tool = 'Bash'; cwd = $MainWin
       cmd = 'cat > docs/x.md'; expect = 0; pipelineIssue = '999' }

    # --- 引用符の中は書き込み判定に当てない(#166)---------------------------------
    # **ホスト非依存**(綴り替えを使わない)ので CI でも走る。
    @{ g = '引用符'; n = '実測された偽陽性: grep -n "Remove-Item|…" は通る'; tool = 'Bash'; cwd = $MainWin
       cmd = 'grep -n "Remove-Item|prune|retention" scripts/pipeline.ps1'; expect = 0 }
    @{ g = '引用符'; n = 'Select-String -Pattern "Set-Content" は通る'; tool = 'PowerShell'; cwd = $MainWin
       cmd = 'Select-String -Pattern "Set-Content" scripts/pipeline.ps1'; expect = 0 }
    @{ g = '引用符'; n = "単引用符の中の二重引用符も剥がれる(状態は 1 つ)"; tool = 'Bash'; cwd = $MainWin
       cmd = 'grep -n ''Remove-Item "x"'' scripts/pipeline.ps1'; expect = 0 }
    @{ g = '引用符'; n = '引用符の外に動詞があれば止まる'; tool = 'PowerShell'; cwd = $MainWin
       cmd = 'Remove-Item "docs/x.md" -Force'; expect = 2 }
    @{ g = '引用符'; n = 'リダイレクト先が引用符でも止まる'; tool = 'Bash'; cwd = $MainWin
       cmd = 'echo x > "docs/x.md"'; expect = 2 }
    # **潰す先が空白だと落ちるケース。** `(-C\s+\S+\s+)*` が満たせなくなり、
    # 偽陽性を消すつもりで書き込み判定ごと外れる(プレースホルダを 1 文字にした理由)。
    @{ g = '引用符'; n = '引用符でくるんだ git -C <本体> checkout は止まる'; tool = 'Bash'; cwd = $Worktree
       cmd = "git -C `"$MainWin`" checkout ."; expect = 2 }
    # 剥がした文字列の行き先は `$writeish` だけ。絶対パスの照合は元の文字列に当てている。
    @{ g = '引用符'; n = '引用符でくるんだ本体の絶対パスへの rm は止まる'; tool = 'Bash'; cwd = $Worktree
       cmd = "rm `"$MainWin/docs/x.md`""; expect = 2 }
    # 対応が取れなければ現状動作に落とす(素通しではない)。
    @{ g = '引用符'; n = '閉じていない引用符は現状動作に落ちる(止まる)'; tool = 'Bash'; cwd = $MainWin
       cmd = 'grep -n "Remove-Item scripts/pipeline.ps1'; expect = 2 }
    # `.pipeline` の回収路判定も剥がす前に当てている。**引用符つきでなければ差が出ない。**
    @{ g = '引用符'; n = '引用符でくるんだ .pipeline/*.lock の回収路は通る'; tool = 'Bash'; cwd = $MainWin
       cmd = 'rm ".pipeline/999.lock"'; expect = 0 }
    # --- #166 が開けた偽陰性を機械に固定する(expect = 0)---------------------------
    # **緑であること自体が「止まらない」の実測である。** ここが落ちたら順序が変わった
    # ということで、05「何が止まらないか」も直す必要がある(`dotnet fsi` の行と同じ形)。
    @{ g = '引用符'; n = '意図した境界: 動詞が引用符の中にしかない(bash -c "rm …")'; tool = 'Bash'; cwd = $MainWin
       cmd = 'bash -c "rm docs/x.md"'; expect = 0 }
    @{ g = '引用符'; n = '意図した境界: 動詞ごと引用符の中だと絶対パスの照合に届かない'; tool = 'Bash'; cwd = $Worktree
       cmd = "bash -c `"rm $MainWin/docs/x.md`""; expect = 0 }
)

# ---- 実行 --------------------------------------------------------------------

function Invoke-Guard {
    param([hashtable]$Case)

    $toolInput = @{}
    if ($Case.cmd) { $toolInput.command = $Case.cmd }
    if ($Case.path) { $toolInput.file_path = $Case.path }
    $json = (@{ tool_name = $Case.tool; cwd = $Case.cwd; tool_input = $toolInput } |
        ConvertTo-Json -Depth 6 -Compress)

    # **子に渡す環境を明示する。** パイプラインのフェーズからこのハーネスを走らせると、
    # 継承した VISIONARY_PIPELINE_ISSUE で全件が素通しし、**何も測らずに緑になる。**
    $prev = $env:VISIONARY_PIPELINE_ISSUE
    $env:VISIONARY_PIPELINE_ISSUE = $Case.pipelineIssue
    try {
        $json | & 'pwsh' -NoProfile -NonInteractive -File $GuardPath 2>$null | Out-Null
        return $LASTEXITCODE
    } finally {
        $env:VISIONARY_PIPELINE_ISSUE = $prev
    }
}

$pass = 0; $fail = 0; $skip = 0
$failures = @()

try {
    New-Fixture
    . (Join-Path $PSScriptRoot 'pipeline-lock.ps1')
    $lockPath = New-PipelineLock -LockDir (Join-Path $fixture '.pipeline') -Issue 999 -Phase 'guard-check'
    if (-not $lockPath) { throw "fixture にロックを置けませんでした" }

    Write-Host ("ガードの実測: {0} ケース(fixture: {1})" -f $cases.Count, $fixture)
    Write-Host ''

    foreach ($c in $cases) {
        $label = "[{0}] {1}" -f $c.g, $c.n
        if ($c.win -and -not $IsWindowsHost) {
            Write-Host ("SKIP  {0}" -f $label) -ForegroundColor DarkGray
            $skip++
            continue
        }
        $code = Invoke-Guard -Case $c
        if ($code -eq $c.expect) {
            Write-Host ("PASS  {0}  (exit {1})" -f $label, $code) -ForegroundColor Green
            $pass++
        } else {
            Write-Host ("FAIL  {0}  (exit {1}, 期待 {2})" -f $label, $code, $c.expect) -ForegroundColor Red
            $fail++
            $failures += $label
        }
    }

    # ロックが無ければ何も止めない、を最後に測る(fixture のロックを外して 1 件)。
    Remove-PipelineLock -Path $lockPath
    $lockPath = $null
    $noLock = Invoke-Guard -Case @{ tool = 'Bash'; cwd = $MainWin; cmd = 'cat > docs/x.md' }
    if ($noLock -eq 0) {
        Write-Host ("PASS  [基本] 生きたロックが無ければ本体でも通る  (exit {0})" -f $noLock) -ForegroundColor Green
        $pass++
    } else {
        Write-Host ("FAIL  [基本] 生きたロックが無ければ本体でも通る  (exit {0}, 期待 0)" -f $noLock) -ForegroundColor Red
        $fail++
        $failures += '[基本] 生きたロックが無ければ本体でも通る'
    }
} finally {
    if ($lockPath) { Remove-PipelineLock -Path $lockPath }
    if (-not $KeepFixture) { Remove-Fixture }
}

# ---- 集計 --------------------------------------------------------------------

Write-Host ''
Write-Host ("{0} 件: PASS {1} / FAIL {2} / SKIP {3}" -f ($pass + $fail + $skip), $pass, $fail, $skip)

if ($skip -gt 0) {
    Write-Host ''
    Write-Host ("SKIP {0} 件はすべて **Windows でだけ意味を持つ綴り替え**(C:\ / C:/ / /c/ / /cygdrive/)である。" -f $skip) -ForegroundColor Yellow
    Write-Host 'ガードは $script:IsWindowsHost が false のとき綴り替えを止める(Linux で当てると逆に守りが消えるため)。' -ForegroundColor Yellow
    Write-Host '**このホストの緑は「綴りの回帰も見た」を意味しない。** 全件を走らせるには Windows の開発機で打つ。' -ForegroundColor Yellow
}

if ($fail -gt 0) {
    Write-Host ''
    Write-Host '落ちたケース:' -ForegroundColor Red
    $failures | ForEach-Object { Write-Host ("  - {0}" -f $_) -ForegroundColor Red }
    Write-Host 'fixture を残して追うには -KeepFixture を付ける。' -ForegroundColor Red
    exit 1
}

exit 0
