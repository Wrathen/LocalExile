# One run against the official client (see Agents/GettingStarted.md):
#   checks the one-time setup, stops anything on the dev ports and the patched client, builds Auth, Patch and Gateway,
#   starts all three (logs in logs/live-*.log) and launches Client/Game.patched.exe through the local patching server.
#   The previous run's logs move to logs/archive/<time>/ (the newest 10 full runs are kept), and the Gateway writes each
#   connection's last packets to logs/packets/ when it closes (Game_PACKET_DUMPS; scripts/dev/bugreport.ps1 bundles both).
#   -NoClient   keep a running client (it drops to the login screen; log in again)
#   -NoBuild    skip the builds
#   -NoExtract  skip scripts/dev/extract-data.ps1, which otherwise first extracts any missing or outdated game data into
#               data/ (a quick check when nothing is missing)
#   -Walkmaps   also run scripts/walkmaps/walkmaps.py (needs Python + `pip install frida`): captures the walk map of every
#               area the client generates into data/walkability/, and lets the Gateway announce each load to it and run
#               capture tours (Game_WALKMAPS=1). Without it nothing is recorded.
#   -Only Gateway[,Auth,Patch]   rebuild and restart only these servers; the others and the client keep running (implies
#               -NoClient). Restarting the Gateway drops the client to the login screen, but Auth keeps the login session.
#               With -Walkmaps the restarted Gateway announces loads again, and a recorder already running is kept.
#   The Gateway runs under `dotnet watch` (.NET hot reload): a saved edit to its code applies to the running server
#               within seconds and the client stays in game. An edit hot reload cannot apply (a changed signature, a new
#               static initializer) rebuilds and restarts the Gateway, which drops the client to the login screen.
#   -NoHotReload  start the built Gateway without `dotnet watch`. Agents/GettingStarted.md step 10.
#   -ClientOnly start only the client, against servers that already run (refused without them; a running client stays);
#               with -Walkmaps also the walk-map recorder, unless one runs. Nothing is built, extracted or restarted.
#   -Stop       stop the patched client, the walk-map recorder and our servers, then exit; with -Only only those servers,
#               with -NoClient everything but the client
param([switch]$NoClient, [switch]$NoBuild, [switch]$NoExtract, [switch]$Walkmaps, [switch]$ClientOnly, [switch]$NoHotReload, [switch]$Stop, [ValidateSet('Auth', 'Patch', 'Gateway')][string[]]$Only = @())

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$out = Join-Path $repo 'logs'
New-Item -ItemType Directory -Force $out | Out-Null

function Need($ok, $message) { if (-not $ok) { throw "setup incomplete: $message (see Agents/GettingStarted.md)" } }
function Get-Recorders {
    Get-CimInstance Win32_Process -Filter "Name like 'python%'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like '*walkmaps.py*' }
}
$patchWebPort = if ($env:Game_PATCH_URL -match ':(\d+)') { [int]$Matches[1] } else { 8080 }
$ports = @{ Auth = @(20493); Patch = @(12995, $patchWebPort); Gateway = @(6112) }
# Stops our own processes of these servers and returns the names of those that were running.
function Stop-Servers([string[]]$names) {
    $stopped = @()
    # Our own processes of each server go first, found by their command line: one still loading its game data holds no
    # port yet, and its cmd wrapper keeps the log open (the next run's log then goes nowhere). That is
    # `dotnet <server>.dll`, a watched Gateway's `dotnet watch` (which would otherwise start it again on the next saved
    # edit, beside the new one), its `dotnet run` and executable, and the cmd wrappers. Builds and test runs of the same
    # projects are left alone.
    $ours = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe' OR Name = 'cmd.exe' OR Name LIKE 'GameServer.%'" -ErrorAction SilentlyContinue
    foreach ($s in $names) {
        $found = @($ours | Where-Object {
            $_.Name -eq "GameServer.$s.exe" -or
            ($_.CommandLine -like "*GameServer.$s*" -and $_.CommandLine -notmatch "GameServer\.$s\.Tests|\s(build|test)\s" -and
                ($_.CommandLine -like "*GameServer.$s.dll*" -or $_.CommandLine -match '\bwatch\b|DOTNET_WATCH'))
        })
        $found | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        if ($found.Count) { $stopped += $s }
    }
    # Then whatever of ours still holds a dev port (dotnet, or the Gateway's own executable when `dotnet watch` started
    # it); anything else there is reported, not killed.
    foreach ($s in $names) {
        foreach ($port in $ports[$s]) {
            foreach ($c in Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) {
                $proc = Get-Process -Id $c.OwningProcess -ErrorAction SilentlyContinue
                if ($proc -and $proc.ProcessName -in 'dotnet', 'GameServer.Gateway') { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue; $stopped += $s }
                elseif ($proc) { throw "port $port is used by $($proc.ProcessName) (pid $($proc.Id)); close it or change the port (Agents/GettingStarted.md)" }
            }
        }
    }
    $stopped | Select-Object -Unique
}

if ($Stop) {
    if ($ClientOnly -or $Walkmaps) { throw '-Stop cannot be combined with -ClientOnly or -Walkmaps' }
    $servers = if ($Only) { $Only } else { @('Auth', 'Patch', 'Gateway') }
    $stopped = @()
    if (-not $Only -and -not $NoClient) {
        $clients = @(Get-Process -Name 'Game.patched' -ErrorAction SilentlyContinue)
        $clients | Stop-Process -Force
        if ($clients.Count) { $stopped += 'client' }
    }
    if (-not $Only) {
        $recorders = @(Get-Recorders)
        $recorders | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        if ($recorders.Count) { $stopped += 'walk-map recorder' }
    }
    $stopped += @(Stop-Servers $servers)
    # Until the ports are free, so that a start right after this one can bind them.
    $deadline = (Get-Date).AddSeconds(10)
    while (@($servers | ForEach-Object { $ports[$_] } | Where-Object { Get-NetTCPConnection -LocalPort $_ -State Listen -ErrorAction SilentlyContinue }).Count) {
        if ((Get-Date) -gt $deadline) { throw 'servers still listening after 10 s' }
        Start-Sleep -Milliseconds 200
    }
    Write-Output $(if ($stopped.Count) { "stopped: $($stopped -join ', ')" } else { 'nothing was running' })
    return
}
function Select-LocalGateway {
    # The client can use a cached gateway list even when the patch welcome needs no download.
    & dotnet (Join-Path $repo 'src\GameServer.Patch\bin\Debug\net10.0\GameServer.Patch.dll') gateway (Join-Path $repo 'Client\Content.ggpk') --login '127.0.0.1:20493' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'could not select local login server; close all clients and retry' }
}
# Long-running processes start through `cmd /c ... > log` rather than Start-Process -RedirectStandardOutput: the
# redirecting form creates them with handle inheritance on, so they would hold this shell's stdout open, and whoever runs
# this script through a pipe (an AI agent's shell tool, `| Select-Object`) would wait until they exit. Started this way
# (ShellExecute) they inherit the environment (the Game_* settings) but no handles.
function Start-Detached([string]$exe, [string[]]$arguments, [string]$log, [string]$err) {
    $line = (@($exe) + $arguments | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } }) -join ' '
    Start-Process cmd.exe -ArgumentList "/c $line 1>`"$log`" 2>`"$err`"" -WorkingDirectory $repo -WindowStyle Hidden
}
Need (Get-Command dotnet -ErrorAction SilentlyContinue) '.NET SDK not found (dotnet)'
Need (Test-Path (Join-Path $repo 'data\login-keys.json')) 'data\login-keys.json missing: run GameServer.Auth keygen'
Need (Test-Path (Join-Path $repo 'Client\Game.exe')) 'Client\Game.exe missing: copy the client and its PathOfExile.exe'
Need (Test-Path (Join-Path $repo 'Client\Game.patched.exe')) 'Client\Game.patched.exe missing: run GameServer.Auth patch'
Need (Test-Path (Join-Path $repo 'Client\Content.ggpk')) 'Client\Content.ggpk missing: this needs the standalone client layout'

$python = $null
if ($Walkmaps) {
    # A virtual environment in the repository (.venv) wins over the python on PATH.
    $venv = Join-Path $repo '.venv\Scripts\python.exe'
    $python = if (Test-Path $venv) { $venv } else { (Get-Command python -ErrorAction SilentlyContinue).Source }
    if (-not $python) { $python = (Get-Command py -ErrorAction SilentlyContinue).Source }
    Need $python 'Python not found (needed for -Walkmaps)'
    & $python -c 'import frida' 2>$null
    Need ($LASTEXITCODE -eq 0) 'the frida Python package is missing: python -m pip install frida'
}

# The walk-map recorder attaches to the client and ends with it, so each new client needs one. A recorder already running
# is kept (only one ever attaches); the previous one's logs move to logs\archive\<time>-walkmaps\.
function Start-Recorder {
    if (@(Get-Recorders).Count) { Write-Output 'walk-map recorder already running'; return }
    $archive = Join-Path (Join-Path $out 'archive') ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-walkmaps')
    foreach ($name in 'walkmaps.log', 'walkmaps.err') {
        $path = Join-Path $out $name
        if ((Test-Path $path) -and (Get-Item $path).Length -gt 0) {
            New-Item -ItemType Directory -Force $archive | Out-Null
            Move-Item $path $archive -Force -ErrorAction SilentlyContinue
        }
    }
    Start-Detached $python @((Join-Path $repo 'scripts\walkmaps\walkmaps.py')) (Join-Path $out 'walkmaps.log') (Join-Path $out 'walkmaps.err')
    Write-Output 'walk-map capture on (logs\walkmaps.log)'
}

# The control bridge and the launcher open a closed client without restarting the servers or touching an existing game.
# With -Walkmaps the recorder starts beside it; the Gateway announces area loads to it only when it was started with
# -Walkmaps too.
if ($ClientOnly) {
    if ($NoClient -or $Only) { throw '-ClientOnly cannot be combined with -NoClient or -Only' }
    $existing = @(Get-Process -Name 'Game.patched' -ErrorAction SilentlyContinue)
    if ($existing.Count) {
        Need ($existing.Count -eq 1 -and $existing[0].Path -eq (Join-Path $repo 'Client\Game.patched.exe')) 'another patched client is running'
        if ($Walkmaps) { Start-Recorder }
        Write-Output "client already running: $($existing[0].Id)"
        return
    }
    foreach ($port in @(12995, 20493, 6112)) {
        Need (@(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue).Count -gt 0) "local servers must be running before -ClientOnly (missing port $port)"
    }
    Select-LocalGateway
    if ($Walkmaps) { Start-Recorder }
    $p = Start-Process (Join-Path $repo 'Client\Game.patched.exe') -ArgumentList '--patching-server', '127.0.0.1:12995' -WorkingDirectory (Join-Path $repo 'Client') -PassThru
    Write-Output "client pid $($p.Id)"
    return
}

# Game data from the GGPK (data/tables, object-types.json, ...): before the servers start, since they read it once.
if (-not $NoExtract) { & (Join-Path $PSScriptRoot 'extract-data.ps1') }

# Without this the Gateway stops after creating the player and the client never enters the area.
if (-not $env:Game_GATEWAY_ENTER) { $env:Game_GATEWAY_ENTER = '1' }

$servers = if ($Only) { $Only } else { @('Auth', 'Patch', 'Gateway') }
if ($Only) { $NoClient = $true }
# Hot reload (the Gateway under `dotnet watch`) unless -NoHotReload.
$Watch = -not $NoHotReload -and $servers -contains 'Gateway'

if (-not $NoClient) { Get-Process -Name 'Game.patched' -ErrorAction SilentlyContinue | Stop-Process -Force }
if (-not $Only) { Get-Recorders | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue } }
Stop-Servers $servers | Out-Null
Start-Sleep -Milliseconds 500
# The previous run's logs move to logs\archive\<time>\ instead of being deleted: a crash noticed only after the
# restart still has its log. Empty files go. A -Only restart's folder is <time>-<servers>; the newest 10 full runs and
# the newest 20 restarts are kept, so restarting the Gateway while debugging does not push the full runs out.
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if ($Only) { $stamp += '-' + (($Only | ForEach-Object { $_.ToLower() }) -join '-') }
$archive = Join-Path (Join-Path $out 'archive') $stamp
$previous = @($servers | ForEach-Object { "live-$($_.ToLower()).log", "live-$($_.ToLower()).err" })
if (-not $Only) { $previous += 'walkmaps.log', 'walkmaps.err' }
foreach ($name in $previous) {
    $path = Join-Path $out $name
    if (-not (Test-Path $path)) { continue }
    if ((Get-Item $path).Length -gt 0) {
        New-Item -ItemType Directory -Force $archive | Out-Null
        Move-Item $path $archive -Force -ErrorAction SilentlyContinue
    }
    Remove-Item $path -ErrorAction SilentlyContinue
}
$runs = @(Get-ChildItem (Join-Path $out 'archive') -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending)
@($runs | Where-Object { $_.Name -match '^\d{8}-\d{6}$' } | Select-Object -Skip 10) +
    @($runs | Where-Object { $_.Name -notmatch '^\d{8}-\d{6}$' } | Select-Object -Skip 20) |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

if (-not $NoBuild) {
    # A watched Gateway builds itself.
    foreach ($s in ($servers | Where-Object { -not ($Watch -and $_ -eq 'Gateway') })) {
        dotnet build (Join-Path $repo "src\GameServer.$s") -v q -nologo | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "build failed: GameServer.$s (run: dotnet build src\GameServer.$s)" }
    }
    # The modules in Modules\ and the mods (Mods\Source\ into Mods\Binaries\), which the Gateway loads; it compiles Leagues\ itself
    # (Agents/docs/dev/modules.md, modding.md). One that does
    # not build is left out; the Gateway runs without it.
    if ($servers -contains 'Gateway') {
        foreach ($mod in Get-ChildItem (Join-Path $repo 'Modules'), (Join-Path $repo 'Mods\Source') -Filter *.csproj -Recurse -ErrorAction SilentlyContinue) {
            dotnet build $mod.FullName -v q -nologo | Out-Null
            if ($LASTEXITCODE -ne 0) { Write-Warning "mod $($mod.BaseName) did not build (run: dotnet build $($mod.FullName)); the Gateway starts without it" }
        }
    }
}

$bin = 'bin\Debug\net10.0'
$serverArgs = @{ Auth = @('serve'); Patch = @(); Gateway = @() }
# The servers inherit it: the Gateway records area loads for the recorder only beside it (AreaLayouts.Recording).
$env:Game_WALKMAPS = if ($Walkmaps) { '1' } else { $null }
# The cached gateway list can point elsewhere even when the patch welcome says no download is needed.
# Select the local Auth explicitly before the patch server or client opens the GGPK.
if (-not $NoClient) {
    Select-LocalGateway
}

# Each connection's last packets go to a file when it closes (PacketHistory): the lead for a client crash. Relative to
# the servers' working directory (this checkout), and only for them: the shell keeps its own value (0 turns it off).
$dumpsBefore = $env:Game_PACKET_DUMPS
if (-not $env:Game_PACKET_DUMPS) { $env:Game_PACKET_DUMPS = 'logs\packets' }
# A watched Gateway reads `dotnet watch`'s own output (its stderr, below) to tell players about compile errors (HotReload.cs).
$hotReloadLogBefore = $env:Game_HOT_RELOAD_LOG
$env:Game_HOT_RELOAD_LOG = if ($Watch) { Join-Path $out 'live-gateway.err' } else { $null }
try {
    foreach ($s in 'Patch', 'Gateway', 'Auth') {
        if ($servers -notcontains $s) { continue }
        # `dotnet watch` runs the Gateway with this checkout as its working directory, like the plain start, so data\
        # and logs\ resolve the same. --non-interactive restarts it on an edit hot reload cannot apply instead of asking.
        # @(...): a single value would come out as a string, and `+` would glue 'serve' onto the dll's name.
        $command = @(if ($Watch -and $s -eq 'Gateway') { @('watch', 'run', '--project', "src\GameServer.$s", '--non-interactive', '--') }
            else { "src\GameServer.$s\$bin\GameServer.$s.dll" })
        Start-Detached 'dotnet' ($command + $serverArgs[$s]) `
            (Join-Path $out "live-$($s.ToLower()).log") (Join-Path $out "live-$($s.ToLower()).err")
    }
}
finally { $env:Game_PACKET_DUMPS = $dumpsBefore; $env:Game_HOT_RELOAD_LOG = $hotReloadLogBefore }

# A watched Gateway first builds: wait until it listens, so the client does not reach a closed port.
if ($Watch) {
    $deadline = (Get-Date).AddMinutes(3)
    $gatewayLog = Join-Path $out 'live-gateway.log'
    $gatewayLogs = @($gatewayLog, (Join-Path $out 'live-gateway.err'))
    while (-not ((Test-Path $gatewayLog) -and (Select-String -Path $gatewayLog -Pattern '\[gateway\] listening on' -Quiet))) {
        if ((Get-Date) -gt $deadline) { throw 'the watched Gateway did not start (logs\live-gateway.log, logs\live-gateway.err)' }
        $built = @($gatewayLogs | Where-Object { Test-Path $_ })
        if ($built -and (Select-String -Path $built -Pattern 'Build failed|error CS' -Quiet)) {
            throw 'the watched Gateway did not build (logs\live-gateway.log, logs\live-gateway.err)'
        }
        Start-Sleep -Milliseconds 500
    }
}

# The patch server must read the GGPK root hash before the client locks the file.
if ($servers -contains 'Patch') {
    $deadline = (Get-Date).AddSeconds(30)
    # The log appears a moment after the server starts: until then there is nothing to read (with $ErrorActionPreference
    # Stop, Select-String on a missing file would end the script even with -ErrorAction SilentlyContinue).
    $patchLog = Join-Path $out 'live-patch.log'
    while (-not ((Test-Path $patchLog) -and (Select-String -Path $patchLog -Pattern 'patching protocol on' -Quiet))) {
        if ((Get-Date) -gt $deadline) {
            $err = Get-Content (Join-Path $out 'live-patch.err') -Raw -ErrorAction SilentlyContinue
            throw "patch server did not start (logs\live-patch.err):`n$err"
        }
        Start-Sleep -Milliseconds 300
    }
}
Start-Sleep -Seconds 1
foreach ($name in ($servers | Where-Object { $_ -ne 'Patch' } | ForEach-Object { $_.ToLower() })) {
    # `dotnet watch` writes its own status lines (hot reload applied, file updated) to stderr.
    $err = (Get-Content (Join-Path $out "live-$name.err") -ErrorAction SilentlyContinue |
        Where-Object { -not ($Watch -and $name -eq 'gateway' -and $_ -match '^dotnet watch ') }) -join "`n"
    if ($err) { throw "$name failed to start (logs\live-$name.err):`n$err" }
}

if ($Walkmaps) {
    # -Only keeps the recorder that is already running (it stopped none above), so only one ever attaches.
    if (-not ($Only -and @(Get-Recorders).Count)) {
        Start-Detached $python @((Join-Path $repo 'scripts\walkmaps\walkmaps.py')) (Join-Path $out 'walkmaps.log') (Join-Path $out 'walkmaps.err')
    }
    Write-Output 'walk-map capture on (logs\walkmaps.log)'
}

if (-not $NoClient) {
    $client = Join-Path $repo 'Client'
    $p = Start-Process (Join-Path $client 'Game.patched.exe') -ArgumentList '--patching-server', '127.0.0.1:12995' -WorkingDirectory $client -PassThru
    Write-Output "client pid $($p.Id)"
}
Write-Output $(if ($Only) { "restarted: $($Only -join ', ')" } else { 'servers up' })
