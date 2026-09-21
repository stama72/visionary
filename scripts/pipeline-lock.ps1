<#
.SYNOPSIS
    走行中のパイプラインを表すロック(`.pipeline/<issue>.lock`)の読み書き。

.DESCRIPTION
    **停止の唯一の証拠はプロセスが消えることである。** 生ログ `.pipeline/*.jsonl` は走行中も
    伸びるので末尾は終端ではなく、オーケストレータの「止まりました」も停止を意味しない
    ([#109](https://github.com/stama72/visionary/issues/109) の実測)。そこで「走行中か」を
    **PID の生存**で答えられる形にして、判定を目視から外す。

    `pipeline.ps1`(ロックを取る側)と `pipeline-guard.ps1`(フックで読む側)の両方から
    dot-source する。**生存判定を2か所に書くと必ずずれる**ので、ここ1か所に置く。

    ロックは JSON 1件:

        { "pid": 1234, "issue": 97, "phase": "impl", "started": "2026-09-20T01:05:34.000Z",
          "processStartUnixMs": 1789002333912, "log": "...jsonl" }

    `processStartUnixMs` を持つのは **PID の使い回し**があるため。PID が生きていても起動時刻が
    食い違うなら、それは別のプロセスであり、ロックは死んでいる。

    **起動時刻を整数(Unix ミリ秒)で持つのは、ISO-8601 文字列が JSON の往復で壊れるため。**
    `ConvertFrom-Json` は `"...Z"` を勝手に `[datetime]` へ変換し、秒未満と Kind を落とす。
    そこへ `ToUniversalTime()` を重ねると時差ぶんずれ、**生きているロックが死んだと判定される**
    (実測: +9時間ずれて `-Status` が「走っていません」と答えた)。整数は往復しても壊れない。
#>

# **StrictMode は置かない。** この関数群は PreToolUse フック(pipeline-guard.ps1)から
# dot-source される。フック側は「存在しないプロパティ」を日常的に触る(ツールごとに
# tool_input の形が違う)ので、strict にすると例外 -> フックの fail-open 経路に落ち、
# **止めるべきときに黙って素通しする。**

function Get-PipelineLockPath {
    param(
        [Parameter(Mandatory = $true)][string]$LockDir,
        [Parameter(Mandatory = $true)][int]$Issue
    )
    Join-Path $LockDir ("{0}.lock" -f $Issue)
}

function Test-PipelineLockAlive {
    <#
        ロック1件が「生きている」か。**生きている = その PID のプロセスが今も居て、
        起動時刻がロックに書かれたものと一致する。** どちらかが欠ければ死んだロックとして扱う。
    #>
    param([Parameter(Mandatory = $true)]$Lock)

    if (-not $Lock -or -not $Lock.pid) { return $false }

    $proc = Get-Process -Id ([int]$Lock.pid) -ErrorAction SilentlyContinue
    if (-not $proc) { return $false }

    # 起動時刻が読めない場合(権限・OS差・古い形式のロック)は PID の生存だけで判断する。
    # **狭い側に倒すと守りが消える**ので、ここは「生きている」に倒す。
    if ($Lock.PSObject.Properties.Name -notcontains 'processStartUnixMs') { return $true }
    $recordedMs = $Lock.processStartUnixMs -as [long]
    if ($null -eq $recordedMs) { return $true }

    $actualMs = try { [DateTimeOffset]::new($proc.StartTime).ToUnixTimeMilliseconds() } catch { $null }
    if ($null -eq $actualMs) { return $true }

    # 同じプロセスなら一致する。幅を持たせるのは時計のまるめぶんだけ。
    return ([math]::Abs($actualMs - $recordedMs) -lt 2000)
}

function Get-LivePipelineLock {
    <#
        `.pipeline/*.lock` のうち生きているものを返す。死んだロックは黙って無視する
        (消しはしない — 消すのは新しく取る側の仕事)。
    #>
    param([Parameter(Mandatory = $true)][string]$LockDir)

    if (-not (Test-Path -LiteralPath $LockDir)) { return @() }

    Get-ChildItem -LiteralPath $LockDir -Filter '*.lock' -ErrorAction SilentlyContinue |
        ForEach-Object {
            $lock = try {
                Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
            } catch { $null }
            if ($lock -and (Test-PipelineLockAlive -Lock $lock)) { $lock }
        }
}

function New-PipelineLock {
    <#
        ロックを取る。**既に生きているロックがあれば取らずに $null を返す** —
        呼び出し側が「二重起動」として止まるため。
    #>
    param(
        [Parameter(Mandatory = $true)][string]$LockDir,
        [Parameter(Mandatory = $true)][int]$Issue,
        [string]$Phase = ''
    )

    New-Item -ItemType Directory -Path $LockDir -Force | Out-Null

    $live = @(Get-LivePipelineLock -LockDir $LockDir)
    if ($live.Count -gt 0) { return $null }

    $self = Get-Process -Id $PID
    $lock = [ordered]@{
        pid                = $PID
        issue              = $Issue
        phase              = $Phase
        started            = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        processStartUnixMs = [DateTimeOffset]::new($self.StartTime).ToUnixTimeMilliseconds()
    }
    $path = Get-PipelineLockPath -LockDir $LockDir -Issue $Issue
    ($lock | ConvertTo-Json) | Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

function Set-PipelineLockPhase {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Phase,
        [string]$Log = ''
    )
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $lock = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    $lock | Add-Member -NotePropertyName phase -NotePropertyValue $Phase -Force
    $lock | Add-Member -NotePropertyName log -NotePropertyValue $Log -Force
    ($lock | ConvertTo-Json) | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Remove-PipelineLock {
    param([Parameter(Mandatory = $true)][string]$Path)
    if ($Path -and (Test-Path -LiteralPath $Path)) {
        Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    }
}
