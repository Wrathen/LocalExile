# One run against the official client (see Agents/GettingStarted.md):
#   checks the one-time setup, stops this checkout's servers and patched client, builds Auth, Patch and Gateway,
#   starts all three (logs in logs/live-*.log) and launches Client/Game.patched.exe through the local patching server.
#   The previous run's logs move to logs/archive/<time>/ (the newest 10 full runs are kept), and the Gateway writes each
#   connection's last packets to logs/packets/ when it closes (Game_PACKET_DUMPS; scripts/dev/bugreport.ps1 bundles both).
#   -NoClient   keep a running client (it drops to the login screen; log in again)
#   -NoBuild    skip the builds
#   -NoExtract  skip scripts/dev/extract-data.ps1, which otherwise first extracts any missing or outdated game data into
#               data/ (a quick check when nothing is missing)
#   -Walkmaps   also run scripts/walkmaps/walkmaps.py (needs Python + `pip install frida`): captures the walk map of every
#               area the client generates into data/walkability/, and lets the Gateway announce each load to it and run
#               capture tours (Game_WALKMAPS=1). Without it nothing is recorded. Default ports and client session only
#               (otherwise the run goes on without the recorder).
#   -Only Gateway[,Auth,Patch]   rebuild and restart only these servers; the others and the client keep running (implies
#               -NoClient). Restarting the Gateway drops the client to the login screen, but Auth keeps the login session.
#               With -Walkmaps the restarted Gateway announces loads again, and this checkout's recorder already running
#               is kept.
#   The Gateway runs under `dotnet watch` (.NET hot reload): a saved edit to its code applies to the running server
#               within seconds and the client stays in game. An edit hot reload cannot apply (a changed signature, a new
#               static initializer) rebuilds and restarts the Gateway, which drops the client to the login screen.
#   -NoHotReload  start the built Gateway without `dotnet watch`. Agents/GettingStarted.md step 10.
#   -ClientOnly open the client (of -Session) when it is closed; the servers must be running (refused without them; a
#               running client stays). With -Walkmaps (default ports and client) also this checkout's walk-map recorder,
#               unless it runs. Nothing is built, extracted or restarted.
#   -PortBase N run this checkout's stack on ports N to N+4 (Patch, Patch web, Auth login, Auth control, Gateway) instead
#               of 12995, 8080, 20493, 20495 and 6112, beside another checkout's stack. Game_PORT_BASE does the same. The
#               base is remembered in data\stack.json for later runs and scripts/dev/client_control.py; 0 = default ports.
#               With a base, the shell's Game_*_PORT, Game_PATCH_URL and Game_PATCH_WEB_ROOT are not used.
#   -Session S  a named client (also Game_CLIENT_SESSION) beside the others of this stack: its own working folder
#               data\clients\S\ (its logs\Client.txt), settings file and saved login. Its new settings file starts from
#               config\nullifier_Config.ini when the checkout has that cheap preset (DirectX 11, 800x600, low textures,
#               10 fps, no sound), else from the default client's. It starts through scripts/dev/nullifier.py: in a job
#               that ends it with its launcher, at below-normal priority with EcoQoS, its window never activated, and
#               with session-local names for the client's two-copies mutexes, so it never takes one of the two machine-wide
#               slots the user's client needs (Agents/docs/dev/headless-client.md). Without it: the one client as before.
#   -Nullifier  (also Game_CLIENT_NULLIFIER=1) the -Session client without a GPU: the engine's Null renderer and a
#               trimmed working set (about 1.9 GB commit, no GPU memory, ~15 % of a core). Without it the session's
#               settings decide the renderer (the preset: DirectX 11).
#   -SeedConfig F  (also Game_CLIENT_SEED_CONFIG) start the -Session client's settings file over from F (without [LOGIN]).
#   -Stop       stop this checkout's clients (and their launchers), walk-map recorder and servers, then exit; with -Only
#               only those servers, with -NoClient everything but the clients, with -ClientOnly only the -Session client.
#   Several stacks and clients on one PC: Agents/docs/dev/live-testing.md ("Several stacks and clients"). Whatever this
#   script stops is this checkout's own (its paths, or the client PIDs it recorded in data\clients\), never another's.
param([switch]$NoClient, [switch]$NoBuild, [switch]$NoExtract, [switch]$Walkmaps, [switch]$ClientOnly, [switch]$NoHotReload,
    [ValidateSet('Auth', 'Patch', 'Gateway')][string[]]$Only = @(), [int]$PortBase = -1, [string]$Session = $env:Game_CLIENT_SESSION,
    [string]$SeedConfig = $env:Game_CLIENT_SEED_CONFIG, [switch]$Stop, [switch]$Nullifier)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$out = Join-Path $repo 'logs'
$clients = Join-Path $repo 'data\clients'
New-Item -ItemType Directory -Force $out | Out-Null

function Need($ok, $message) { if (-not $ok) { throw "setup incomplete: $message (see Agents/GettingStarted.md)" } }

# This checkout's ports. Without a base, today's, where a server's own variable (Game_LOGIN_PORT, Game_PATCH_URL, ...)
# still wins; with one, five in a row from it, whatever such variables the shell has. A base given here or in
# Game_PORT_BASE is written to data\stack.json with its ports (0 removes it): later runs and the control bridge read it.
function Get-Stack([int]$base) {
    $file = Join-Path $repo 'data\stack.json'
    $given = $base -ge 0
    if (-not $given -and "$env:Game_PORT_BASE" -ne '') { $base = [int]$env:Game_PORT_BASE; $given = $true }
    if (-not $given) { $base = if (Test-Path $file) { [int](Get-Content $file -Raw | ConvertFrom-Json).portBase } else { 0 } }
    if ($base -ne 0 -and ($base -lt 1024 -or $base -gt 65530)) { throw "port base $base is outside 1024-65530 (0 = the default ports)" }
    function Pick([string]$name, [int]$offset, [int]$default) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($base) { $base + $offset } elseif ($value) { [int]$value } else { $default }
    }
    $web = if ($base) { $base + 1 } elseif ($env:Game_PATCH_URL -match ':(\d+)') { [int]$Matches[1] } else { 8080 }
    $ports = [ordered]@{ patch = (Pick 'Game_PATCH_TCP_PORT' 0 12995); patchWeb = $web; login = (Pick 'Game_LOGIN_PORT' 2 20493)
        control = (Pick 'Game_AUTH_CONTROL_PORT' 3 20495); gateway = (Pick 'Game_GATEWAY_PORT' 4 6112) }
    if ($given -and $base) { [ordered]@{ portBase = $base; ports = $ports } | ConvertTo-Json | Set-Content $file -Encoding ascii }
    elseif ($given -and (Test-Path $file)) { Remove-Item $file }
    [pscustomobject]@{ Base = $base; Ports = $ports }
}

# '' and 'default' are the client as before (started in Client\ with the shared settings); other names are sessions.
function Get-SessionName([string]$name) {
    if (-not $name -or $name -eq 'default') { return 'default' }
    if ($name -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]{0,23}$') { throw "session name '$name': up to 24 letters, digits, - and _" }
    $name
}

# Whether a process (Win32_Process) names a path inside this checkout on its command line or as its executable. A git
# worktree nested in it (.claude\worktrees\<name>) is another checkout.
function Test-Here($process) {
    $null -ne $process -and [regex]::IsMatch("$($process.CommandLine)|$($process.ExecutablePath)",
        [regex]::Escape("$repo\") + '(?!\.claude\\worktrees\\)', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
}

# This checkout's patched clients. Each checkout reaches the shared client through its own Client\ path, which tells
# checkouts apart; the PID records Start-Client writes (data\clients\<session>.json) tell its sessions apart. A client
# no record names (one an older live.ps1 started) is the default session's, unless it started while a launch was
# pending (a record with pid 0, written just before the start): that is another session's. Without a session: all.
function Get-OwnClients([string]$session) {
    $exe = Join-Path $repo 'Client\Game.patched.exe'
    $mine = @(Get-CimInstance Win32_Process -Filter "Name = 'Game.patched.exe'" -ErrorAction SilentlyContinue |
        Where-Object { [string]::Equals("$($_.ExecutablePath)", $exe, [StringComparison]::OrdinalIgnoreCase) })
    if (-not $session) { return $mine }
    $records = @{}
    foreach ($file in Get-ChildItem $clients -Filter '*.json' -File -ErrorAction SilentlyContinue) {
        try { $records[$file.BaseName] = Get-Content $file.FullName -Raw | ConvertFrom-Json } catch { }
    }
    # The PID and its start time: a later process may reuse the PID.
    $recorded = { param($p, $r) $p.ProcessId -eq $r.pid -and [math]::Abs($p.CreationDate.ToFileTimeUtc() - [long]$r.started) -lt 20000000 }
    if ($records.ContainsKey($session) -and $records[$session].pid) { $r = $records[$session]; return @($mine | Where-Object { & $recorded $_ $r }) }
    if ($session -ne 'default') { return @() }
    $now = (Get-Date).ToFileTimeUtc()
    $pending = @($records.Values | Where-Object { -not $_.pid -and $now - [long]$_.started -lt 600000000 } | ForEach-Object { [long]$_.started })
    @($mine | Where-Object { $p = $_; -not @($records.Values | Where-Object { & $recorded $p $_ }).Count -and
        -not @($pending | Where-Object { $p.CreationDate.ToFileTimeUtc() -ge $_ }).Count })
}

# This checkout's processes of the given servers: `dotnet <server>.dll`, a watched Gateway's `dotnet watch`, and their cmd
# wrappers, found by their command line (one still loading its game data holds no port yet, and its cmd wrapper keeps the
# log open), with everything they started (`dotnet watch` runs `dotnet run`, which runs GameServer.Gateway.exe). They
# name a path inside this checkout themselves or through their parent (an older live.ps1 started them with relative
# paths, but its cmd wrapper names its log files here). Builds, test runs and other checkouts' servers are left alone.
function Get-OwnServers([string[]]$names) {
    $all = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue)
    $byId = @{}
    foreach ($p in $all) { $byId[[int]$p.ProcessId] = $p }
    $queue = [System.Collections.Generic.Queue[object]]::new()
    foreach ($p in $all) {
        $line = "$($p.CommandLine)"
        foreach ($s in $names) {
            $server = $p.Name -eq "GameServer.$s.exe" -or ($p.Name -in 'dotnet.exe', 'cmd.exe' -and $line -like "*GameServer.$s*" -and
                $line -notmatch "GameServer\.$s\.Tests|\s(build|test)\s" -and ($line -like "*GameServer.$s.dll*" -or $line -match '\bwatch\b|DOTNET_WATCH'))
            if ($server -and ((Test-Here $p) -or (Test-Here $byId[[int]$p.ParentProcessId]))) { $queue.Enqueue($p); break }
        }
    }
    $found = @{}
    while ($queue.Count) {
        $p = $queue.Dequeue()
        if ($found.ContainsKey([int]$p.ProcessId)) { continue }
        $found[[int]$p.ProcessId] = $p
        # Children started after their parent (an older process may hold a reused parent PID). Build nodes and the
        # compiler server a watched Gateway's builds start are shared by every build on the PC: they stay.
        foreach ($c in $all) {
            if ($c.ParentProcessId -ne $p.ProcessId -or $c.CreationDate -lt $p.CreationDate) { continue }
            if ($c.Name -eq 'VBCSCompiler.exe' -or "$($c.CommandLine)" -match 'MSBuild\.dll.*/nodemode|VBCSCompiler|\bbuild-server\b') { continue }
            $queue.Enqueue($c)
        }
    }
    @($found.Values)
}

# Running clients that hold (or wait for) a machine-wide two-copies slot: all but this checkout's session clients.
# Running clients that hold (or wait for) a machine-wide two-copies slot. A client nullifier.py launched, from any
# checkout, renames its mutexes and holds none: it is known by its parent (a python running nullifier.py, started before
# it), by the --nologo only nullifier.py passes, or by this checkout's records. $exclude: PIDs not to count (this
# checkout's clients that a full run is about to replace).
function Get-GlobalSlotHolders([int[]]$exclude = @()) {
    $renamed = @(Get-RenamedClientIds)
    $all = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue)
    $byId = @{}
    foreach ($p in $all) { $byId[[int]$p.ProcessId] = $p }
    @($all | Where-Object {
        if ($_.Name -ne 'Game.patched.exe' -or $_.ProcessId -in $renamed -or $_.ProcessId -in $exclude) { return $false }
        if ("$($_.CommandLine)" -match '(^|\s)--nologo(\s|$)') { return $false }
        $parent = $byId[[int]$_.ParentProcessId]
        -not ($parent -and "$($parent.CommandLine)" -like '*nullifier.py*' -and $parent.CreationDate -le $_.CreationDate)
    })
}

# The default client needs one of the two machine-wide slots. Checked before anything is stopped or started: a full
# run would otherwise leave servers running for a client that cannot start.
function Assert-GlobalSlot([int[]]$exclude = @()) {
    $running = @(Get-GlobalSlotHolders $exclude)
    if ($running.Count -ge 2) {
        throw ("the client runs at most two copies per PC, and two run already (pids $(($running | ForEach-Object { $_.ProcessId }) -join ', ')); " +
            'close one, or start this one as a -Session client, which needs no slot')
    }
}

# Starts a session client through scripts/dev/nullifier.py, detached (it lives as long as its client), and waits until it
# has merged the client's PID and start time (and its own) into the pending record. On failure (the launcher ended, or
# no client within 60 s) the launcher and what it started are stopped, the record goes, and this throws.
function Start-SessionLauncher([string]$session, [string]$folder, [string]$record, $stack, [bool]$nullifier) {
    $python = Get-Python 'session clients (scripts/dev/nullifier.py)'
    $arguments = @((Join-Path $repo 'scripts\dev\nullifier.py'), '--cwd', $folder, '--record', $record,
        '--patch-port', "$($stack.Ports.patch)", '--login-port', "$($stack.Ports.login)")
    $arguments += if ($nullifier) { @('--renderer', 'null', '--trim-seconds', '10') } else { @('--renderer', 'configured') }
    $log = Join-Path $out "nullifier-$session.log"; $err = Join-Path $out "nullifier-$session.err"
    $wrapper = Start-Detached $python $arguments $log $err -PassThru
    $deadline = (Get-Date).AddSeconds($script:LauncherTimeout)
    while ($true) {
        $r = try { Get-Content $record -Raw | ConvertFrom-Json } catch { $null }
        if ($r -and $r.pid) { break }
        $ended = $wrapper.HasExited
        if ($ended -or (Get-Date) -gt $deadline) {
            Stop-Wrapper $wrapper
            Remove-Item $record -ErrorAction SilentlyContinue
            $why = (Get-Content $err -Raw -ErrorAction SilentlyContinue)
            throw "the session client did not start ($(if ($ended) { 'its launcher ended' } else { "no client within $($script:LauncherTimeout) s" })): $why(logs\nullifier-$session.log, .err)"
        }
        Start-Sleep -Milliseconds 250
    }
    Write-Output "client pid $($r.pid) (session $session, $folder; launcher $($r.launcher)$(if ($nullifier) { '; Null renderer' }))"
}
$script:LauncherTimeout = 60

# Session clients start through scripts/dev/nullifier.py (Start-Client), which records its own PID and start time in the
# session's record (launcher, launcherStarted). A launcher counts only when its PID, start time and command line agree:
# a later process may reuse the PID.
function Get-OwnLaunchers([string]$session) {
    $files = if ($session) { @(Get-Item (Join-Path $clients "$session.json") -ErrorAction SilentlyContinue) }
        else { @(Get-ChildItem $clients -Filter '*.json' -File -ErrorAction SilentlyContinue) }
    foreach ($file in $files) {
        $r = try { Get-Content $file.FullName -Raw | ConvertFrom-Json } catch { $null }
        if (-not $r -or -not $r.launcher -or -not $r.launcherStarted) { continue }
        $id = [int]$r.launcher
        $p = @(Get-CimInstance Win32_Process -Filter "ProcessId = $id" -ErrorAction SilentlyContinue | Where-Object { $_.ProcessId -eq $id })
        foreach ($q in $p) {
            if ([math]::Abs($q.CreationDate.ToFileTimeUtc() - [long]$r.launcherStarted) -lt 20000000 -and
                "$($q.CommandLine)" -like '*nullifier.py*' -and (Test-Here $q)) { $q }
        }
    }
}

# The PIDs of this checkout's session clients, which hold no machine-wide two-copies slot (their mutexes are renamed).
function Get-RenamedClientIds {
    foreach ($file in Get-ChildItem $clients -Filter '*.json' -File -ErrorAction SilentlyContinue) {
        $r = try { Get-Content $file.FullName -Raw | ConvertFrom-Json } catch { $null }
        if ($r -and $r.launcher -and $r.pid) { [int]$r.pid }
    }
}

function Get-OwnWalkmaps {
    @(Get-CimInstance Win32_Process -Filter "Name like 'python%'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like '*walkmaps.py*' -and (Test-Here $_) })
}

# The walk-map recorder attaches to the default client (by the PID and start time recorded in data\clients\default.json)
# and ends with it, so each new client needs one. This checkout's recorder already running is kept (only one ever
# attaches); the previous one's logs move to logs\archive\<time>-walkmaps\.
function Start-Recorder([string]$python) {
    if (@(Get-OwnWalkmaps).Count) { Write-Output 'walk-map recorder already running'; return }
    $archive = Join-Path (Join-Path $out 'archive') ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-walkmaps')
    foreach ($name in 'walkmaps.log', 'walkmaps.err') {
        $path = Join-Path $out $name
        if ((Test-Path $path) -and (Get-Item $path).Length -gt 0) {
            New-Item -ItemType Directory -Force $archive | Out-Null
            Move-Item $path $archive -Force -ErrorAction SilentlyContinue
        }
    }
    Start-Detached $python @((Join-Path $repo 'scripts\walkmaps\walkmaps.py'), '--record', (Join-Path $clients 'default.json')) (Join-Path $out 'walkmaps.log') (Join-Path $out 'walkmaps.err')
    Write-Output 'walk-map capture on (logs\walkmaps.log)'
}

function Stop-Processes($processes) {
    foreach ($p in $processes) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
}

# A virtual environment in the repository (.venv) wins over the python on PATH; it needs Frida (walkmaps.py,
# nullifier.py).
function Get-Python([string]$purpose) {
    $venv = Join-Path $repo '.venv\Scripts\python.exe'
    $python = if (Test-Path $venv) { $venv } else { (Get-Command python -ErrorAction SilentlyContinue).Source }
    if (-not $python) { $python = (Get-Command py -ErrorAction SilentlyContinue).Source }
    Need $python "Python not found (needed for $purpose)"
    & $python -c 'import frida' 2>$null
    Need ($LASTEXITCODE -eq 0) 'the frida Python package is missing: python -m pip install frida'
    $python
}

# Long-running processes start through `cmd /c ... > log` rather than Start-Process -RedirectStandardOutput: the
# redirecting form creates them with handle inheritance on, so they would hold this shell's stdout open, and whoever runs
# this script through a pipe (an AI agent's shell tool, `| Select-Object`) would wait until they exit. Started this way
# (ShellExecute) they inherit the environment (the Game_* settings) but no handles. -PassThru returns the cmd wrapper.
function Start-Detached([string]$exe, [string[]]$arguments, [string]$log, [string]$err, [switch]$PassThru) {
    $line = (@($exe) + $arguments | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } }) -join ' '
    # The outer quotes: cmd /c strips the first and the last quote of a line that starts with one (a quoted executable).
    $p = Start-Process cmd.exe -ArgumentList "/c `"$line 1>`"$log`" 2>`"$err`"`"" -WorkingDirectory $repo -WindowStyle Hidden -PassThru
    if ($PassThru) { $p }
}

# A cmd wrapper Start-Detached returned and what it started after it (a launcher's client goes with its job).
function Stop-Wrapper($wrapper) {
    $started = try { $wrapper.StartTime } catch { $null }
    $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $($wrapper.Id)" -ErrorAction SilentlyContinue |
        Where-Object { $_.ParentProcessId -eq $wrapper.Id -and $started -and $_.CreationDate -ge $started.AddSeconds(-1) })
    Stop-Processes $children
    Stop-Process -Id $wrapper.Id -Force -ErrorAction SilentlyContinue
}

# The client's own Log In goes to the login server of the GGPK's cached gateway list; the control bridge
# (client_control.py) connects to this stack's Auth by port and needs no list. The GGPK is shared by every checkout and
# cannot be written while any client runs, so only the default stack points it at its Auth, and only when the list
# (read first; reading works while clients run) does not already. When it cannot be written: with -Strict (before
# anything was stopped) the script stops; otherwise a warning, and the client's own Log In may go elsewhere.
function Select-LocalGateway([int]$loginPort, [switch]$Strict) {
    $patch = Join-Path $repo 'src\GameServer.Patch\bin\Debug\net10.0\GameServer.Patch.dll'
    $ggpk = Join-Path $repo 'Client\Content.ggpk'
    $list = @(& dotnet $patch gateway $ggpk 2>$null | Where-Object { "$_" -match '^[^;]*;[^;]*;' })
    if ($LASTEXITCODE -eq 0 -and $list.Count -and -not @($list | Where-Object { "$_" -notmatch "^[^;]*;[^;]*;127\.0\.0\.1:$loginPort;" }).Count) { return }
    & dotnet $patch gateway $ggpk --login "127.0.0.1:$loginPort" 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { return }
    $message = 'could not select local login server (a client holds the GGPK); close all clients and retry'
    if ($Strict) { throw $message }
    Write-Warning "$message. The client's own Log In may go to another login server; client_control.py logs in by port."
}

# Starts the session's client through this stack's patching server and records its PID. A named session runs in its own
# folder, data\clients\<session>\: the client opens Content.ggpk, config.json, its asset folders (FMOD sound banks,
# Streamline, D3D12), CachedHLSLShaders and logs\Client.txt in its working folder (live 2026-10-30). So the folder gets a
# hard link to the shared Content.ggpk (the same file: nothing is copied or written), junctions to the shared asset
# folders, its own shader cache and log, and a config.json naming the session's own settings file and saved login
# (GGG_CONFIG_FILE_NAME, GGG_CREDENTIAL_FILE_NAME, in Documents\My Games\Path of Exile). A new settings file (or any,
# with -SeedConfig) starts as a copy of the seed without its [LOGIN] section: no saved login to send to whichever login
# server the shared gateway list names. The seed: -SeedConfig, else the cheap preset config\nullifier_Config.ini (DX11,
# 800x600, low textures, 10 fps, no sound) when this checkout has it, else the default client's settings.
# A session client starts through scripts/dev/nullifier.py (detached; it lives as long as its client): in a job that ends
# the client with it, at below-normal priority with EcoQoS (agents' clients yield to the person playing), its window
# never activated, and with session-local names for the two-copies mutexes. $nullifier adds the Null renderer and
# working-set trimming. The launcher merges the client's PID and start time (and its own) into the pending record; this
# waits for them. The default client starts plainly, as before.
function Start-Client([string]$session, $stack, [string]$seedConfig, [bool]$nullifier = $false) {
    $client = Join-Path $repo 'Client'
    $folder = $client
    if ($session -ne 'default') {
        $folder = Join-Path $clients $session
        New-Item -ItemType Directory -Force $folder | Out-Null
        $shared = Join-Path $client 'Content.ggpk'
        $link = Join-Path $folder 'Content.ggpk'
        if (Test-Path $link) {
            # A link to a GGPK that has been replaced since: only this folder's link goes.
            $a = Get-Item $link; $b = Get-Item $shared
            if ($a.Length -ne $b.Length -or $a.LastWriteTimeUtc -ne $b.LastWriteTimeUtc) { Remove-Item $link -Force }
        }
        if (-not (Test-Path $link)) { New-Item -ItemType HardLink -Path $link -Target $shared | Out-Null }
        foreach ($assets in Get-ChildItem $client -Directory | Where-Object { $_.Name -notin 'logs', 'CachedHLSLShaders' }) {
            $junction = Join-Path $folder $assets.Name
            if (-not (Test-Path $junction)) { New-Item -ItemType Junction -Path $junction -Target $assets.FullName | Out-Null }
        }
        $prefix = "localexile-$(Split-Path $repo -Leaf)-$session"
        [ordered]@{ GGG_CONFIG_FILE_NAME = "${prefix}_Config.ini"; GGG_CREDENTIAL_FILE_NAME = "${prefix}_login.credentials" } |
            ConvertTo-Json | Set-Content (Join-Path $folder 'config.json') -Encoding ascii
        $settings = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games\Path of Exile'
        $ini = Join-Path $settings "${prefix}_Config.ini"
        $preset = Join-Path $repo 'config\nullifier_Config.ini'
        $seed = if ($seedConfig) { (Resolve-Path $seedConfig).Path } elseif (Test-Path $preset) { $preset } else { Join-Path $settings 'poeoffline_Config.ini' }
        if (($seedConfig -or -not (Test-Path $ini)) -and (Test-Path $seed)) {
            $section = $null
            $lines = foreach ($line in [IO.File]::ReadAllLines($seed)) {
                if ($line -match '^\[(.+)\]\s*$') { $section = $Matches[1] }
                if ($section -ne 'LOGIN') { $line }
            }
            [IO.File]::WriteAllLines($ini, [string[]]$lines, [Text.UTF8Encoding]::new($true))
        }
    }
    # The client runs at most two copies per PC: it takes one of two machine-wide mutexes (Global\PoERunMutexA, B;
    # 0x140101d85) and otherwise waits on "You may only run up to two copies of Path of Exile at a time." Session clients
    # use session-local names instead, so only the default client needs a free slot: the callers check it first
    # (Assert-GlobalSlot), before anything was stopped or started.
    # The record goes first as a pending launch (pid 0, the time before the start): until it has the PID, nobody takes
    # the new client for the default session's (Get-OwnClients, client_control.py).
    New-Item -ItemType Directory -Force $clients | Out-Null
    $record = Join-Path $clients "$session.json"
    [ordered]@{ pid = 0; started = (Get-Date).AddSeconds(-1).ToFileTimeUtc(); session = $session; folder = $folder; ports = $stack.Ports } |
        ConvertTo-Json | Set-Content $record -Encoding ascii
    # Whatever fails from here on leaves no pending record behind.
    try {
        if ($session -ne 'default') {
            Start-SessionLauncher $session $folder $record $stack $nullifier
            return
        }
        $p = Start-Process (Join-Path $client 'Game.patched.exe') -ArgumentList '--patching-server', "127.0.0.1:$($stack.Ports.patch)" -WorkingDirectory $folder -PassThru
        $started = try { $p.StartTime.ToFileTimeUtc() } catch { $null }
        if ($null -eq $started -or $p.HasExited) {
            Remove-Item $record -ErrorAction SilentlyContinue
            throw "the client exited right after starting (exit code $($p.ExitCode)); see $(Join-Path $folder 'logs\Client.txt')"
        }
        [ordered]@{ pid = $p.Id; started = $started; session = $session; folder = $folder; ports = $stack.Ports } |
            ConvertTo-Json | Set-Content $record -Encoding ascii
        Write-Output "client pid $($p.Id)"
    }
    catch {
        $current = try { Get-Content $record -Raw | ConvertFrom-Json } catch { $null }
        if (-not $current -or -not $current.pid) { Remove-Item $record -ErrorAction SilentlyContinue }
        throw
    }
}

$stack = Get-Stack $PortBase
$session = Get-SessionName $Session
$nullifierMode = $Nullifier -or "$env:Game_CLIENT_NULLIFIER" -in '1', 'true'
if ($nullifierMode -and $session -eq 'default' -and -not $Stop) { throw '-Nullifier needs -Session: the default client starts as before' }
$ports = @{ Auth = @($stack.Ports.login, $stack.Ports.control); Patch = @($stack.Ports.patch, $stack.Ports.patchWeb); Gateway = @($stack.Ports.gateway) }

# -Stop: this checkout's processes only, as everywhere in this script. The -Session client (and its launcher) with
# -ClientOnly; the given servers with -Only; everything but the clients with -NoClient; else the clients (launchers
# first: their job objects end their clients with them), the walk-map recorder and the servers.
if ($Stop) {
    if ($Walkmaps) { throw '-Stop cannot be combined with -Walkmaps' }
    if ($ClientOnly -and ($Only -or $NoClient)) { throw '-Stop -ClientOnly cannot be combined with -Only or -NoClient' }
    $parts = [ordered]@{}
    if ($ClientOnly) { $parts['client'] = @(Get-OwnLaunchers $session) + @(Get-OwnClients $session) }
    else {
        if (-not $Only -and -not $NoClient) { $parts['client'] = @(Get-OwnLaunchers) + @(Get-OwnClients) }
        if (-not $Only) { $parts['walk-map recorder'] = @(Get-OwnWalkmaps) }
        $names = if ($Only) { $Only } else { @('Auth', 'Patch', 'Gateway') }
        foreach ($s in $names) { $parts[$s] = @(Get-OwnServers $s) }
    }
    $stopping = @($parts.Values | ForEach-Object { $_ })
    Stop-Processes $stopping
    if ($ClientOnly) { Remove-Item (Join-Path $clients "$session.json") -ErrorAction SilentlyContinue }
    elseif ($parts.Contains('client')) { Remove-Item (Join-Path $clients '*.json') -ErrorAction SilentlyContinue }
    # Until this stack's ports of the stopped servers are free of this checkout's processes, so that a start right
    # after this one can bind them (another program there is not waited for: a start reports it).
    $stoppedServers = @($parts.Keys | Where-Object { $_ -in 'Auth', 'Patch', 'Gateway' -and $parts[$_].Count })
    $serverPorts = @($stoppedServers | ForEach-Object { $ports[$_] })
    $stoppedIds = @($stopping | ForEach-Object { [int]$_.ProcessId })
    $deadline = (Get-Date).AddSeconds(10)
    while (@($serverPorts | Where-Object { @(Get-NetTCPConnection -LocalPort $_ -State Listen -ErrorAction SilentlyContinue |
            Where-Object { [int]$_.OwningProcess -in $stoppedIds }).Count }).Count) {
        if ((Get-Date) -gt $deadline) { throw 'servers still listening after 10 s' }
        Start-Sleep -Milliseconds 200
    }
    $what = @($parts.Keys | Where-Object { $parts[$_].Count })
    Write-Output $(if ($what.Count) { "stopped: $($what -join ', ') ($($stopping.Count) process(es) of $repo)" } else { 'nothing was running' })
    return
}

Need (Get-Command dotnet -ErrorAction SilentlyContinue) '.NET SDK not found (dotnet)'
Need (Test-Path (Join-Path $repo 'data\login-keys.json')) 'data\login-keys.json missing: run GameServer.Auth keygen'
Need (Test-Path (Join-Path $repo 'Client\Game.exe')) 'Client\Game.exe missing: copy the client and its PathOfExile.exe'
Need (Test-Path (Join-Path $repo 'Client\Game.patched.exe')) 'Client\Game.patched.exe missing: run GameServer.Auth patch'
Need (Test-Path (Join-Path $repo 'Client\Content.ggpk')) 'Client\Content.ggpk missing: this needs the standalone client layout'

# The control bridge can open a closed client without restarting the servers or touching any other client.
# With -Walkmaps the recorder starts beside the default client of the default ports (and the Gateway announces area
# loads to it only when it was started with -Walkmaps too).
if ($ClientOnly) {
    if ($NoClient -or $Only) { throw '-ClientOnly cannot be combined with -NoClient or -Only' }
    if ($Walkmaps -and ($stack.Base -or $session -ne 'default')) {
        Write-Warning '-Walkmaps records only on the default ports and for the default client session; no walk-map recorder'
        $Walkmaps = $false
    }
    $python = if ($Walkmaps) { Get-Python '-Walkmaps' } else { $null }
    $existing = @(Get-OwnClients $session)
    if ($existing.Count) {
        if ($Walkmaps) { Start-Recorder $python }
        Write-Output "client already running: $($existing[0].ProcessId)"
        return
    }
    foreach ($port in @($stack.Ports.patch, $stack.Ports.login, $stack.Ports.gateway)) {
        Need (@(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue).Count -gt 0) "local servers must be running before -ClientOnly (missing port $port)"
    }
    if ($session -eq 'default') { Assert-GlobalSlot }
    if (-not $stack.Base) { Select-LocalGateway $stack.Ports.login -Strict }
    if ($Walkmaps) { Start-Recorder $python }
    Start-Client $session $stack $SeedConfig $nullifierMode
    return
}

# A default client needs a free machine-wide slot: checked before this run stops or starts anything (this checkout's own
# clients do not count: the run replaces them).
if (-not $NoClient -and -not $Only -and $session -eq 'default') { Assert-GlobalSlot @(Get-OwnClients | ForEach-Object { [int]$_.ProcessId }) }

$python = $null
# The recorder meets the Gateway in data\walkability\current.json, which every checkout's stack would share: it records
# only on the default ports and for the default client session (which it attaches to by its recorded PID). Otherwise
# the run goes on without it.
if ($Walkmaps -and ($stack.Base -or $session -ne 'default')) {
    Write-Warning '-Walkmaps records only on the default ports and for the default client session; starting without the walk-map recorder'
    $Walkmaps = $false
}
if ($Walkmaps) { $python = Get-Python '-Walkmaps' }

# Game data from the GGPK (data/tables, object-types.json, ...): before the servers start, since they read it once.
if (-not $NoExtract) { & (Join-Path $PSScriptRoot 'extract-data.ps1') }

# Without this the Gateway stops after creating the player and the client never enters the area.
if (-not $env:Game_GATEWAY_ENTER) { $env:Game_GATEWAY_ENTER = '1' }

$servers = if ($Only) { $Only } else { @('Auth', 'Patch', 'Gateway') }
if ($Only) { $NoClient = $true }
# Hot reload (the Gateway under `dotnet watch`) unless -NoHotReload.
$Watch = -not $NoHotReload -and $servers -contains 'Gateway'

# This checkout's clients (every session: they lose their servers) and recorder.
if (-not $NoClient) {
    Stop-Processes (@(Get-OwnLaunchers) + @(Get-OwnClients))
    Remove-Item (Join-Path $clients '*.json') -ErrorAction SilentlyContinue
}
if (-not $Only) { Stop-Processes (Get-OwnWalkmaps) }
# This checkout's servers go first, then whatever of this checkout still listens on the stack's ports. Anything else
# there (another checkout's stack on the same ports, another program) is reported, not stopped.
Stop-Processes (Get-OwnServers $servers)
foreach ($port in ($servers | ForEach-Object { $ports[$_] })) {
    foreach ($c in Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) {
        $proc = Get-CimInstance Win32_Process -Filter "ProcessId = $($c.OwningProcess)" -ErrorAction SilentlyContinue
        if (-not $proc) { continue }
        $parent = Get-CimInstance Win32_Process -Filter "ProcessId = $($proc.ParentProcessId)" -ErrorAction SilentlyContinue
        if ($proc.Name -in 'dotnet.exe', 'GameServer.Gateway.exe' -and ((Test-Here $proc) -or (Test-Here $parent))) {
            Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
        }
        else {
            throw ("port $port is used by $($proc.Name) (pid $($proc.ProcessId)) $($proc.ExecutablePath), not this checkout's server: " +
                'close it, or give this checkout its own ports with -PortBase (Agents/docs/dev/live-testing.md)')
        }
    }
}
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
    # The first-party mods (Mods\Source\) into Mods\Binaries\, which the Gateway loads (Agents/docs/dev/modding.md). One that does
    # not build is left out; the Gateway runs without it.
    if ($servers -contains 'Gateway') {
        foreach ($mod in Get-ChildItem (Join-Path $repo 'Mods\Source') -Filter *.csproj -Recurse -ErrorAction SilentlyContinue) {
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
# Select the local Auth explicitly before the patch server or client opens the GGPK (the default stack only).
if (-not $NoClient -and -not $stack.Base) {
    Select-LocalGateway $stack.Ports.login
}

# Only for the servers (the shell keeps its own values): this stack's ports; each connection's last packets go to a file
# when it closes (PacketHistory, the lead for a client crash; relative to the servers' working directory, this checkout;
# 0 turns it off); a watched Gateway reads `dotnet watch`'s own output (its stderr, below) to tell players about compile
# errors (HotReload.cs).
$serverEnv = @{
    Game_PATCH_TCP_PORT = "$($stack.Ports.patch)"; Game_LOGIN_PORT = "$($stack.Ports.login)"
    Game_AUTH_CONTROL_PORT = "$($stack.Ports.control)"; Game_GATEWAY_PORT = "$($stack.Ports.gateway)"
    Game_PATCH_URL = if ($env:Game_PATCH_URL -and -not $stack.Base) { $env:Game_PATCH_URL } else { "http://127.0.0.1:$($stack.Ports.patchWeb)" }
    Game_PATCH_WEB_ROOT = if ($stack.Base) { $null } else { $env:Game_PATCH_WEB_ROOT }
    Game_PACKET_DUMPS = if ($env:Game_PACKET_DUMPS) { $env:Game_PACKET_DUMPS } else { 'logs\packets' }
    Game_HOT_RELOAD_LOG = if ($Watch) { Join-Path $out 'live-gateway.err' } else { $null }
}
$envBefore = @{}
foreach ($name in $serverEnv.Keys) {
    $envBefore[$name] = [Environment]::GetEnvironmentVariable($name)
    [Environment]::SetEnvironmentVariable($name, $serverEnv[$name])
}
try {
    foreach ($s in 'Patch', 'Gateway', 'Auth') {
        if ($servers -notcontains $s) { continue }
        # `dotnet watch` runs the Gateway with this checkout as its working directory, like the plain start, so data\
        # and logs\ resolve the same. --non-interactive restarts it on an edit hot reload cannot apply instead of asking.
        # Absolute paths: the command line tells this checkout's servers from another checkout's (Get-OwnServers).
        # @(...): a single value would come out as a string, and `+` would glue 'serve' onto the dll's name.
        $command = @(if ($Watch -and $s -eq 'Gateway') { @('watch', 'run', '--project', (Join-Path $repo "src\GameServer.$s"), '--non-interactive', '--') }
            else { Join-Path $repo "src\GameServer.$s\$bin\GameServer.$s.dll" })
        Start-Detached 'dotnet' ($command + $serverArgs[$s]) `
            (Join-Path $out "live-$($s.ToLower()).log") (Join-Path $out "live-$($s.ToLower()).err")
    }
}
finally { foreach ($name in $envBefore.Keys) { [Environment]::SetEnvironmentVariable($name, $envBefore[$name]) } }

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

# -Only keeps this checkout's recorder that already runs (it stopped none above), so only one ever attaches.
if ($Walkmaps) {
    if ($Only -and @(Get-OwnWalkmaps).Count) { Write-Output 'walk-map capture on (logs\walkmaps.log; the running recorder is kept)' }
    else { Start-Recorder $python }
}

if (-not $NoClient) { Start-Client $session $stack $SeedConfig $nullifierMode }
$portsText = if ($stack.Base) { " on ports $(($stack.Ports.GetEnumerator() | ForEach-Object { "$($_.Key) $($_.Value)" }) -join ', ')" } else { '' }
Write-Output $(if ($Only) { "restarted: $($Only -join ', ')$portsText" } else { "servers up$portsText" })
