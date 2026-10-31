# usage: scripts/perf/startup.ps1 [-PortBase 23000] [-Runs 3] [-SettleSeconds 10] [-Configuration Debug] [-Env @{ NAME = 'value' }]
# Measures one server stack's startup and resident cost (Agents/docs/dev/performance.md): starts the built Auth, then the
# built Gateway, on PortBase+1 … +3 with a private copy of the save database, times each to its "listening" line, waits
# SettleSeconds, records private bytes / working set / GC heap (dotnet-counters, when artifacts/tools has it), and
# stops exactly the processes it started. Never touches the default ports; the Patch server is not part of it.
# Run from the repository root after `dotnet build`.
param(
    [int] $PortBase = 23000,
    [int] $Runs = 3,
    [int] $SettleSeconds = 10,
    [string] $Configuration = 'Debug',
    [hashtable] $Env = @{}
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repo
$out = Join-Path $repo 'artifacts\perf'
New-Item -ItemType Directory -Force $out | Out-Null
$bin = "bin\$Configuration\net10.0"
$counters = Join-Path $repo 'artifacts\tools\dotnet-counters.exe'
$source = Join-Path $repo 'data\validation-saves.db'
if (-not (Test-Path $source)) { throw "no data\validation-saves.db to copy (the perf run never uses data\saves.db)" }

function Wait-Line($proc, $log, $pattern, $timeoutSeconds) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $timeoutSeconds) {
        if ($proc.HasExited) { throw "process $($proc.Id) exited early; see $log" }
        if ((Test-Path $log) -and (Select-String -Path $log -Pattern $pattern -Quiet)) { return $clock.Elapsed.TotalMilliseconds }
        Start-Sleep -Milliseconds 20
    }
    throw "timed out waiting for '$pattern' in $log"
}

function Measure-Process($proc) {
    $proc.Refresh()
    $gc = $null; $committed = $null
    if (Test-Path $counters) {
        $csv = Join-Path $out "counters-$($proc.Id).csv"
        & $counters collect -p $proc.Id --counters 'System.Runtime[dotnet.gc.last_collection.heap.size,dotnet.gc.last_collection.memory.committed_size]' `
            --duration 00:00:00:03 --refresh-interval 1 -o $csv 2>&1 | Out-Null
        if (Test-Path $csv) {
            $rows = Import-Csv $csv
            function Mean($name) { $r = @($rows | Where-Object { $_.'Counter Name' -like "*$name*" }); if ($r) { ($r | ForEach-Object { [double]$_.'Mean/Increment' } | Measure-Object -Average).Average / 1MB } }
            $gc = Mean 'heap.size'; $committed = Mean 'committed_size'
            Remove-Item $csv -ErrorAction SilentlyContinue
        }
    }
    $proc.Refresh()
    [pscustomobject]@{ PrivateMB = [math]::Round($proc.PrivateMemorySize64 / 1MB); WorkingSetMB = [math]::Round($proc.WorkingSet64 / 1MB);
        GcHeapMB = if ($gc) { [math]::Round($gc) } else { $null }; GcCommittedMB = if ($committed) { [math]::Round($committed) } else { $null }
        Threads = $proc.Threads.Count }
}

# One copy for every run: run 1 is a stack's first start (the Gateway seeds the game-data reference tables into it),
# the later runs are ordinary restarts; the medians use those.
$db = Join-Path $out 'startup-saves.db'
Copy-Item $source $db -Force
$results = @()
for ($run = 1; $run -le $Runs; $run++) {
    $started = @()
    $saved = @{}
    $vars = @{ Game_SAVES_DB = $db; Game_LOGIN_PORT = "$($PortBase + 1)"; Game_AUTH_CONTROL_PORT = "$($PortBase + 2)";
               Game_GATEWAY_PORT = "$($PortBase + 3)"; Game_DEV_PORT = "$($PortBase + 4)" }
    foreach ($k in $Env.Keys) { $vars[$k] = $Env[$k] }   # -Env may also override these
    foreach ($k in $vars.Keys) { $saved[$k] = [Environment]::GetEnvironmentVariable($k); [Environment]::SetEnvironmentVariable($k, $vars[$k]) }
    try {
        $authLog = Join-Path $out "auth-$run.log"; $gwLog = Join-Path $out "gateway-$run.log"
        Remove-Item $authLog, $gwLog -ErrorAction SilentlyContinue
        $auth = Start-Process dotnet -ArgumentList "src\GameServer.Auth\$bin\GameServer.Auth.dll" -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $authLog -RedirectStandardError "$authLog.err"
        $started += $auth
        $authMs = Wait-Line $auth $authLog 'login server on tcp' 60
        $gw = Start-Process dotnet -ArgumentList "src\GameServer.Gateway\$bin\GameServer.Gateway.dll" -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $gwLog -RedirectStandardError "$gwLog.err"
        $started += $gw
        $gwMs = Wait-Line $gw $gwLog 'listening on' 120
        Start-Sleep -Seconds $SettleSeconds
        $a = Measure-Process $auth; $g = Measure-Process $gw
        $results += [pscustomobject]@{ Run = $run; AuthReadyMs = [math]::Round($authMs); GatewayReadyMs = [math]::Round($gwMs);
            AuthPrivateMB = $a.PrivateMB; AuthWorkingSetMB = $a.WorkingSetMB; 
            GatewayPrivateMB = $g.PrivateMB; GatewayWorkingSetMB = $g.WorkingSetMB; GatewayGcHeapMB = $g.GcHeapMB; GatewayGcCommittedMB = $g.GcCommittedMB; GatewayThreads = $g.Threads }
    }
    finally {
        foreach ($p in $started) { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue; $p.WaitForExit(5000) | Out-Null } }
        foreach ($k in $saved.Keys) { [Environment]::SetEnvironmentVariable($k, $saved[$k]) }
    }
}
Remove-Item $db, "$db-wal", "$db-shm" -ErrorAction SilentlyContinue
$results | Format-Table -AutoSize | Out-String -Width 220
$restarts = @($results | Where-Object Run -gt 1)
if ($restarts.Count -eq 0) { $restarts = $results }
function Median($values) { $v = @($values | Sort-Object); $v[[int][math]::Floor(($v.Count - 1) / 2)] }
"median of restarts: Auth ready {0} ms, Gateway ready {1} ms; Auth private {2} MB; Gateway private {3} MB, working set {4} MB, GC heap {5} MB, GC committed {6} MB" -f `
    (Median $restarts.AuthReadyMs), (Median $restarts.GatewayReadyMs), (Median $restarts.AuthPrivateMB),
    (Median $restarts.GatewayPrivateMB), (Median $restarts.GatewayWorkingSetMB), (Median $restarts.GatewayGcHeapMB), (Median $restarts.GatewayGcCommittedMB)
