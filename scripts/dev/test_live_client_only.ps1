# Exercise live.ps1's client launch, port and process-ownership logic with mocks: no client, server, or GGPK is touched.
$ErrorActionPreference = 'Stop'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'live.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
foreach ($name in @('Need', 'Get-Stack', 'Get-SessionName', 'Test-Here', 'Get-OwnClients', 'Get-OwnServers', 'Select-LocalGateway',
        'Get-OwnLaunchers', 'Get-RenamedClientIds', 'Get-GlobalSlotHolders', 'Assert-GlobalSlot', 'Start-SessionLauncher')) {
    $definition = $ast.Find({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $false)
    if (-not $definition) { throw "missing function $name" }
    . ([scriptblock]::Create($definition.Extent.Text))
}
$branch = $ast.Find({ param($node)
    $node -is [System.Management.Automation.Language.IfStatementAst] -and
    $node.Clauses[0].Item1.Extent.Text -eq '$ClientOnly' -and $node.Extent.Text -like '*Start-Client*'
}, $false)
if (-not $branch) { throw 'missing ClientOnly branch' }
$launch = [scriptblock]::Create($branch.Extent.Text)
$stopBranch = $ast.Find({ param($node)
    $node -is [System.Management.Automation.Language.IfStatementAst] -and $node.Clauses[0].Item1.Extent.Text -eq '$Stop'
}, $false)
if (-not $stopBranch) { throw 'missing Stop branch' }
$stopRun = [scriptblock]::Create($stopBranch.Extent.Text)

$repo = Join-Path ([IO.Path]::GetTempPath()) "localexile-live-test-$PID"
$clients = Join-Path $repo 'data\clients'
$out = Join-Path $repo 'logs'
New-Item -ItemType Directory -Force $clients, $out | Out-Null
$ownExe = Join-Path $repo 'Client\Game.patched.exe'
$ClientOnly = $true
$NoClient = $false
$Only = @()
$Walkmaps = $false
$script:events = [System.Collections.Generic.List[string]]::new()
$script:processes = @()
$script:missingPort = 0
$script:listed = @('Europe;Local (EU);127.0.0.1:1;127.0.0.1;false')
$script:gatewayExit = 0
$script:gatewayArgs = @()
$script:checkedPorts = @()
$script:started = [datetime]'2026-10-30T21:00:00'
function Get-CimInstance { param($ClassName, $Filter, $ErrorAction)
    if ($Filter -match "Name = '(.+)'") { $n = $Matches[1]; return @($script:processes | Where-Object { $_.Name -eq $n }) }
    $script:processes
}
function Get-NetTCPConnection {
    param($LocalPort, $State, $ErrorAction)
    $script:checkedPorts += $LocalPort
    if ($LocalPort -ne $script:missingPort) { [pscustomobject]@{ LocalPort = $LocalPort } }
}
function dotnet {
    $script:gatewayArgs = @($args)
    if ($args -contains '--login') { $script:events.Add('gateway-write'); $global:LASTEXITCODE = $script:gatewayExit; return }
    $script:events.Add('gateway-read')
    $global:LASTEXITCODE = 0
    '[patch] Bundles2/gateway_list.txt:'
    $script:listed
}
function Start-Client($session, $stack) { $script:events.Add("launch:$session") }
function Assert($condition, $message) { if (-not $condition) { throw $message } }
function Client($id, $path, $started = $script:started) {
    [pscustomobject]@{ Name = 'Game.patched.exe'; ProcessId = $id; ExecutablePath = $path; CreationDate = $started }
}
function Record($session, $id, $started = $script:started) {
    @{ pid = $id; started = $started.ToFileTimeUtc() } | ConvertTo-Json | Set-Content (Join-Path $clients "$session.json")
}
function Run { $script:events.Clear(); $script:checkedPorts = @(); & $launch | Out-Null }
function Expect-Failure($pattern) {
    $message = $null
    $script:events.Clear()
    try { & $launch | Out-Null } catch { $message = $_.Exception.Message }
    Assert ($message -like "*$pattern*") "expected '$pattern', got '$message'"
    Assert (-not @($script:events | Where-Object { $_ -like 'launch*' }).Count) 'failed preflight launched a client'
}

try {
    # The default stack and client, as before: the gateway list is pointed at the local Auth, then the client starts.
    $env:Game_PORT_BASE = $null
    $stack = Get-Stack -1
    $session = Get-SessionName ''
    Assert ($stack.Base -eq 0 -and $stack.Ports.login -eq 20493 -and $stack.Ports.gateway -eq 6112 -and $stack.Ports.patch -eq 12995) 'default ports changed'
    Assert (-not (Test-Path (Join-Path $repo 'data\stack.json'))) 'the default stack must not write data\stack.json'
    Run
    Assert (($script:events -join ',') -eq 'gateway-read,gateway-write,launch:default') "gateway must be selected before a closed client launches: $($script:events -join ',')"
    Assert ($script:gatewayArgs[0] -eq (Join-Path $repo 'src\GameServer.Patch\bin\Debug\net10.0\GameServer.Patch.dll')) 'wrong patch executable'
    Assert (($script:gatewayArgs[1..4] -join '|') -eq ('gateway|' + (Join-Path $repo 'Client\Content.ggpk') + '|--login|127.0.0.1:20493')) 'gateway must target this checkout and local Auth'
    Assert (($script:checkedPorts -join ',') -eq '12995,20493,6112') "default ports not checked: $($script:checkedPorts -join ',')"
    # A list already pointing at the local Auth is not written again (the GGPK is shared and may be open).
    $script:listed = @('Europe;Local (EU);127.0.0.1:20493;127.0.0.1;false', 'America;Local (US);127.0.0.1:20493;127.0.0.1;false')
    Run
    Assert (($script:events -join ',') -eq 'gateway-read,launch:default') 'an already local gateway list must not be written'

    # This checkout's client runs: nothing is written or launched. Another checkout's client does not stop a launch.
    $script:processes = @(Client 42 $ownExe)
    Run
    Assert ($script:events.Count -eq 0) 'existing client must return without writes or launch'
    $script:processes = @(Client 43 'C:\Other\Client\Game.patched.exe')
    Run
    Assert (($script:events -join ',') -eq 'gateway-read,launch:default') "another checkout's client must not block or be touched"
    # Two other clients hold both machine-wide slots: a default client is refused before anything is written.
    $script:processes = @(Client 43 'C:\Other\Client\Game.patched.exe'), (Client 44 "$repo-b\Client\Game.patched.exe")
    Expect-Failure 'two run already'
    Assert ($script:events.Count -eq 0) 'a refused default client must not touch the gateway list'

    # Named sessions: each is known by its record; an unrecorded client of this checkout is the default session's.
    $script:processes = @((Client 42 $ownExe), (Client 45 $ownExe ([datetime]'2026-10-30T21:05:00')))
    Record 'a1' 45 ([datetime]'2026-10-30T21:05:00')
    $session = Get-SessionName 'a1'
    Run
    Assert ($script:events.Count -eq 0) 'a recorded session client must count as running'
    $session = Get-SessionName 'a2'
    Run
    Assert (($script:events -join ',') -eq 'gateway-read,launch:a2') 'a session without a client must launch beside the others'
    $session = Get-SessionName 'default'
    Run
    Assert ($script:events.Count -eq 0) 'the unrecorded client is the default session'
    Assert (@(Get-OwnClients 'default').ProcessId -eq 42) 'default session must not take the recorded a1 client'
    Assert (@(Get-OwnClients).Count -eq 2) 'all sessions of this checkout'
    # A record whose PID now belongs to a later process does not count.
    Record 'a1' 45 ([datetime]'2026-10-30T20:00:00')
    Assert (@(Get-OwnClients 'a1').Count -eq 0) 'a reused PID is not the session client'
    # A launch under way (pid 0 until the PID is known): a client started after it is not the default session's.
    Remove-Item (Join-Path $clients '*.json')
    $script:processes = @((Client 42 $ownExe), (Client 46 $ownExe (Get-Date)))
    @{ pid = 0; started = (Get-Date).AddSeconds(-5).ToFileTimeUtc() } | ConvertTo-Json | Set-Content (Join-Path $clients 'a3.json')
    Assert ((@(Get-OwnClients 'default').ProcessId -join ',') -eq '42') 'a client of a pending launch must not be the default session'
    Assert (@(Get-OwnClients 'a3').Count -eq 0) 'a pending session has no client yet'
    Remove-Item (Join-Path $clients '*.json')
    foreach ($bad in '-x', 'a b', '..\x', ('a' * 25)) {
        $failed = $false; try { Get-SessionName $bad | Out-Null } catch { $failed = $true }
        Assert $failed "session name '$bad' must be refused"
    }

    # Its own ports: remembered, checked, and the shared GGPK is never written.
    $script:processes = @()
    $session = Get-SessionName ''
    $stack = Get-Stack 21000
    Assert (($stack.Ports.Values -join ',') -eq '21000,21001,21002,21003,21004') "port base order: $($stack.Ports.Values -join ',')"
    Assert ((Get-Content (Join-Path $repo 'data\stack.json') -Raw | ConvertFrom-Json).ports.login -eq 21002) 'stack.json must remember the ports'
    Assert ((Get-Stack -1).Base -eq 21000) 'a later run must reuse the remembered base'
    # With a base the shell's own port variables (left from a default-port run) do not count; on the default ports they do.
    $env:Game_LOGIN_PORT = '30000'; $env:Game_PATCH_URL = 'http://127.0.0.1:8080'
    Assert ((Get-Stack -1).Ports.login -eq 21002 -and (Get-Stack -1).Ports.patchWeb -eq 21001) "the base must win over the shell's port variables"
    Assert ((Get-Stack 0).Ports.login -eq 30000) "on the default ports a server's own variable wins"
    Assert ((Get-Stack 21000).Base -eq 21000) 'base restored'
    $env:Game_LOGIN_PORT = $null; $env:Game_PATCH_URL = $null
    $stack = Get-Stack -1
    Run
    Assert (($script:events -join ',') -eq 'launch:default') "a stack on its own ports must not touch the shared GGPK: $($script:events -join ',')"
    Assert (($script:checkedPorts -join ',') -eq '21000,21002,21004') "the stack's own ports must be checked: $($script:checkedPorts -join ',')"
    $script:missingPort = 21004
    Expect-Failure 'missing port 21004'
    $script:missingPort = 0
    $failed = $false; try { Get-Stack 80 | Out-Null } catch { $failed = $true }
    Assert $failed 'a port base below 1024 must be refused'
    Assert ((Get-Stack 0).Base -eq 0 -and -not (Test-Path (Join-Path $repo 'data\stack.json'))) '-PortBase 0 must forget the base'

    # Failures before any write or launch.
    $stack = Get-Stack -1
    $script:listed = @('Europe;Local (EU);127.0.0.1:1;127.0.0.1;false')
    $script:missingPort = 20493
    Expect-Failure 'missing port 20493'
    Assert ($script:events.Count -eq 0) 'missing server must return before gateway write'
    $script:missingPort = 0
    $script:gatewayExit = 1
    Expect-Failure 'could not select local login server'
    Assert (($script:events -join ',') -eq 'gateway-read,gateway-write') 'failed gateway reset must prevent launch'
    # A full run selects the list after it stopped this checkout's servers: there it warns and goes on.
    $script:events.Clear()
    $warned = @(Select-LocalGateway 20493 3>&1)
    Assert ($warned.Count -eq 1 -and ($script:events -join ',') -eq 'gateway-read,gateway-write') 'a full run must not stop on the gateway list'
    $script:gatewayExit = 0
    $NoClient = $true
    Expect-Failure '-ClientOnly cannot be combined'
    Assert ($script:events.Count -eq 0) 'invalid options must return before gateway write'
    $NoClient = $false

    # Server ownership: this checkout's servers and what they started, never another checkout's (even one whose folder
    # name starts with this one's), a build, or a test run.
    $t = [datetime]'2026-10-30T20:00:00'
    function P($id, $parent, $name, $line, $exe = $null) {
        [pscustomobject]@{ ProcessId = $id; ParentProcessId = $parent; Name = $name; CommandLine = $line; ExecutablePath = $exe; CreationDate = $t.AddSeconds($id) }
    }
    $script:processes = @(
        # this checkout, started by an older live.ps1: relative paths, the cmd wrapper names its log here
        (P 10 1 'cmd.exe' "cmd.exe /c dotnet src\GameServer.Auth\bin\Debug\net10.0\GameServer.Auth.dll serve 1>`"$repo\logs\live-auth.log`"")
        (P 11 10 'dotnet.exe' 'dotnet  src\GameServer.Auth\bin\Debug\net10.0\GameServer.Auth.dll serve')
        # this checkout, the watched Gateway with absolute paths, and its tree
        (P 20 1 'cmd.exe' "cmd.exe /c dotnet watch run --project $repo\src\GameServer.Gateway --non-interactive -- 1>`"$repo\logs\live-gateway.log`"")
        (P 21 20 'dotnet.exe' "dotnet  watch run --project $repo\src\GameServer.Gateway --non-interactive --")
        (P 22 21 'dotnet.exe' "dotnet.exe dotnet-watch.dll run --project $repo\src\GameServer.Gateway")
        (P 23 22 'dotnet.exe' 'dotnet.exe run --no-build -e DOTNET_WATCH=1')
        (P 24 23 'GameServer.Gateway.exe' "`"$repo\src\GameServer.Gateway\bin\Debug\net10.0\GameServer.Gateway.exe`"" "$repo\src\GameServer.Gateway\bin\Debug\net10.0\GameServer.Gateway.exe")
        # another checkout whose folder name starts with this one's
        (P 30 1 'cmd.exe' "cmd.exe /c dotnet src\GameServer.Auth\bin\Debug\net10.0\GameServer.Auth.dll serve 1>`"$repo-b\logs\live-auth.log`"")
        (P 31 30 'dotnet.exe' 'dotnet  src\GameServer.Auth\bin\Debug\net10.0\GameServer.Auth.dll serve')
        (P 32 1 'GameServer.Gateway.exe' "`"$repo-b\src\GameServer.Gateway\bin\Debug\net10.0\GameServer.Gateway.exe`"" "$repo-b\src\GameServer.Gateway\bin\Debug\net10.0\GameServer.Gateway.exe")
        # a build and a test run here
        (P 40 1 'dotnet.exe' "dotnet build $repo\src\GameServer.Auth -v q")
        (P 41 1 'dotnet.exe' "dotnet test $repo\tests\GameServer.Gateway.Tests")
        # an older process whose reused PID is a later root's "parent"
        (P 5 24 'dotnet.exe' 'dotnet something-else.dll')
        # shared build processes a watched Gateway's build started: they serve every build on the PC
        (P 50 21 'dotnet.exe' '"C:\Program Files\dotnet\dotnet.exe" "C:\Program Files\dotnet\sdk\10.0.302\MSBuild.dll" /noautoresponse /nologo /nodemode:1 /nodeReuse:true')
        (P 51 50 'conhost.exe' 'conhost.exe 0x4')
        (P 52 21 'VBCSCompiler.exe' '"C:\Program Files\dotnet\sdk\10.0.302\Roslyn\bincore\VBCSCompiler.exe" -pipename:x')
        (P 53 21 'dotnet.exe' 'dotnet build-server shutdown')
        # a worktree nested in this checkout is another checkout
        (P 60 1 'cmd.exe' "cmd.exe /c dotnet src\GameServer.Auth\bin\Debug\net10.0\GameServer.Auth.dll serve 1>`"$repo\.claude\worktrees\x\logs\live-auth.log`"")
        (P 61 60 'dotnet.exe' 'dotnet  src\GameServer.Auth\bin\Debug\net10.0\GameServer.Auth.dll serve')
    )
    $own = @(Get-OwnServers 'Auth', 'Gateway' | ForEach-Object { $_.ProcessId } | Sort-Object)
    Assert (($own -join ',') -eq '10,11,20,21,22,23,24') "own servers: $($own -join ',')"
    $own = @(Get-OwnServers 'Auth' | ForEach-Object { $_.ProcessId } | Sort-Object)
    Assert (($own -join ',') -eq '10,11') "own Auth only: $($own -join ',')"

    # Session clients start through nullifier.py; its record names the launcher by PID, start time and command line.
    Remove-Item (Join-Path $clients '*.json') -ErrorAction SilentlyContinue
    $t0 = [datetime]'2026-10-31T10:00:00'
    $launcherLine = "`"$repo\.venv\Scripts\python.exe`" $repo\scripts\dev\nullifier.py --cwd $repo\data\clients\s1"
    $script:processes = @(
        [pscustomobject]@{ Name = 'python.exe'; ProcessId = 70; ParentProcessId = 1; CommandLine = $launcherLine; ExecutablePath = $null; CreationDate = $t0 }
        [pscustomobject]@{ Name = 'python.exe'; ProcessId = 71; ParentProcessId = 1; CommandLine = 'python other.py'; ExecutablePath = $null; CreationDate = $t0 }
        [pscustomobject]@{ Name = 'python.exe'; ProcessId = 72; ParentProcessId = 1; CommandLine = $launcherLine.Replace($repo, "$repo-b"); ExecutablePath = $null; CreationDate = $t0 }
        (Client 80 $ownExe $t0)
        (Client 81 $ownExe $t0)
        (Client 82 'C:\Other\Client\Game.patched.exe' $t0))
    function Launched($session, $client, $launcher, $launcherStarted = $t0) {
        [ordered]@{ pid = $client; started = $t0.ToFileTimeUtc(); session = $session; launcher = $launcher; launcherStarted = $launcherStarted.ToFileTimeUtc() } |
            ConvertTo-Json | Set-Content (Join-Path $clients "$session.json")
    }
    Launched 's1' 80 70
    Assert ((@(Get-OwnLaunchers 's1').ProcessId -join ',') -eq '70') 'the recorded launcher must be found'
    Assert ((@(Get-OwnLaunchers).ProcessId -join ',') -eq '70') 'all launchers of this checkout'
    Launched 's2' 81 71
    Assert (@(Get-OwnLaunchers 's2').Count -eq 0) 'a recorded PID running something else is not a launcher'
    Launched 's2' 81 72
    Assert (@(Get-OwnLaunchers 's2').Count -eq 0) "another checkout's launcher is not this one's"
    Launched 's2' 81 70 ([datetime]'2026-10-31T09:00:00')
    Assert (@(Get-OwnLaunchers 's2').Count -eq 0) 'a reused launcher PID (other start time) is not the launcher'
    # Session clients hold no machine-wide slot, from any checkout: this checkout's by their records, another's by
    # their launcher parent or the --nologo only nullifier.py passes. The default client counts only the others.
    Assert ((@(Get-RenamedClientIds) | Sort-Object) -join ',' -eq '80,81') 'session clients with a launcher are renamed'
    Assert ((@(Get-GlobalSlotHolders).ProcessId -join ',') -eq '82') "only clients outside this checkout's sessions hold slots"
    Remove-Item (Join-Path $clients '*.json')
    Assert ((@(Get-GlobalSlotHolders).ProcessId | Sort-Object) -join ',' -eq '80,81,82') 'without records or markers every client counts'
    $otherLauncher = "`"C:\Python\python.exe`" D:\other\scripts\dev\nullifier.py --cwd D:\other\data\clients\x"
    $script:processes = @(
        [pscustomobject]@{ Name = 'python.exe'; ProcessId = 73; ParentProcessId = 1; CommandLine = $otherLauncher; CreationDate = $t0 }
        [pscustomobject]@{ Name = 'python.exe'; ProcessId = 74; ParentProcessId = 1; CommandLine = $otherLauncher; CreationDate = $t0.AddMinutes(5) }
        [pscustomobject]@{ Name = 'Game.patched.exe'; ProcessId = 83; ParentProcessId = 73; CommandLine = '"D:\other\Client\Game.patched.exe" --patching-server 127.0.0.1:21000'; CreationDate = $t0.AddSeconds(1) }
        [pscustomobject]@{ Name = 'Game.patched.exe'; ProcessId = 84; ParentProcessId = 1; CommandLine = 'D:\other\Client\Game.patched.exe --patching-server 127.0.0.1:21000 --nologo --nosound'; CreationDate = $t0 }
        [pscustomobject]@{ Name = 'Game.patched.exe'; ProcessId = 85; ParentProcessId = 74; CommandLine = '"D:\user\Client\Game.patched.exe" --patching-server 127.0.0.1:12995'; CreationDate = $t0.AddSeconds(2) }
        [pscustomobject]@{ Name = 'Game.patched.exe'; ProcessId = 86; ParentProcessId = 1; CommandLine = '"D:\user\Client\Game.patched.exe" --patching-server 127.0.0.1:12995'; CreationDate = $t0 })
    Assert ((@(Get-GlobalSlotHolders).ProcessId | Sort-Object) -join ',' -eq '85,86') "another checkout's session clients hold no slot; a reused launcher PID (a later python) does not count: $((@(Get-GlobalSlotHolders).ProcessId) -join ',')"
    Assert ((@(Get-GlobalSlotHolders @(86)).ProcessId) -join ',' -eq '85') 'excluded clients (replaced by this run) do not count'
    $failed = $null; try { Assert-GlobalSlot } catch { $failed = $_.Exception.Message }
    Assert ($failed -like '*two run already (pids 85, 86)*') "two Global holders must refuse a default client: $failed"
    Assert-GlobalSlot @(86)
    # The checks come before anything is stopped or started: in the -ClientOnly branch before the gateway list and the
    # launch, and for a full run at the top, before the extraction, the stops and the servers.
    Assert ($branch.Extent.Text.IndexOf('Assert-GlobalSlot') -lt $branch.Extent.Text.IndexOf('Select-LocalGateway')) '-ClientOnly must check the slots before writing the gateway list'
    $script = $ast.Extent.Text
    $top = $script.IndexOf("Assert-GlobalSlot @(Get-OwnClients")
    Assert ($top -gt 0 -and $top -lt $script.IndexOf("& (Join-Path `$PSScriptRoot 'extract-data.ps1')") -and
        $top -lt $script.IndexOf('Stop-Processes (Get-OwnServers $servers)')) 'a full run must check the slots before stopping or starting anything'

    # The launch: the launcher's arguments, the wait for its record, and the cleanup when it fails.
    $script:LauncherTimeout = 2
    function Get-Python { 'python.exe' }
    $script:wrapperExited = $false
    $script:stopped = @()
    $script:launch = $null
    function Start-Detached($exe, $arguments, $log, $err, [switch]$PassThru) {
        $script:launch = @($exe) + $arguments
        if ($script:writeRecord) {
            $r = Get-Content $script:writeRecord -Raw | ConvertFrom-Json
            $r | Add-Member -Force pid 90; $r | Add-Member -Force launcher 91
            $r | ConvertTo-Json | Set-Content $script:writeRecord
        }
        [pscustomobject]@{ Id = 95; HasExited = $script:wrapperExited }
    }
    function Stop-Wrapper($wrapper) { $script:stopped += $wrapper.Id }
    $stack = Get-Stack 21000
    $record = Join-Path $clients 's3.json'
    @{ pid = 0; started = 1; session = 's3' } | ConvertTo-Json | Set-Content $record
    $script:writeRecord = $record
    $said = Start-SessionLauncher 's3' (Join-Path $clients 's3') $record $stack $true
    $line = $script:launch -join ' '
    Assert ($line -like "*scripts\dev\nullifier.py --cwd $clients\s3 --record $record --patch-port 21000 --login-port 21002 --renderer null --trim-seconds 10") "launcher arguments: $line"
    Assert ("$said" -like 'client pid 90 (session s3*launcher 91; Null renderer)') "launch report: $said"
    Assert ($script:stopped.Count -eq 0) 'a started launcher must keep running'
    $null = Start-SessionLauncher 's3' (Join-Path $clients 's3') $record $stack $false
    Assert (($script:launch -join ' ') -like '*--renderer configured') 'without -Nullifier the settings decide the renderer'
    # The launcher ends before recording, or records nothing in time: it is stopped, the record goes, and it throws.
    foreach ($exited in $true, $false) {
        @{ pid = 0; started = 1; session = 's3' } | ConvertTo-Json | Set-Content $record
        $script:writeRecord = $null; $script:wrapperExited = $exited; $script:stopped = @()
        $message = $null
        try { Start-SessionLauncher 's3' (Join-Path $clients 's3') $record $stack $true | Out-Null } catch { $message = $_.Exception.Message }
        Assert ($message -like 'the session client did not start*') "a failed launch must throw: $message"
        Assert (($script:stopped -join ',') -eq '95') 'a failed launch must stop its launcher'
        Assert (-not (Test-Path $record)) 'a failed launch must remove its pending record'
    }
    # -Stop: this checkout's processes only, in upstream's variants (-Only: those servers; -NoClient: all but the
    # clients; -ClientOnly: the session's client and launcher).
    $Stop = $true
    function Get-OwnLaunchers($session) { if ($session) { ,@([pscustomobject]@{ ProcessId = 301 }) } else { @([pscustomobject]@{ ProcessId = 301 }, [pscustomobject]@{ ProcessId = 302 }) } }
    function Get-OwnClients($session) { if ($session) { ,@([pscustomobject]@{ ProcessId = 311 }) } else { @([pscustomobject]@{ ProcessId = 311 }, [pscustomobject]@{ ProcessId = 312 }) } }
    function Get-OwnWalkmaps { ,@([pscustomobject]@{ ProcessId = 321 }) }
    function Get-OwnServers($names) { @($names | ForEach-Object { [pscustomobject]@{ ProcessId = @{ Auth = 331; Patch = 332; Gateway = 333 }[$_] } }) }
    function Stop-Processes($processes) { $script:killed = @($processes | ForEach-Object { $_.ProcessId } | Sort-Object) }
    function Get-NetTCPConnection { param($LocalPort, $State, $ErrorAction) }
    $stack = Get-Stack 0
    $ports = @{ Auth = @(20493, 20495); Patch = @(12995, 8080); Gateway = @(6112) }
    function StopWith([bool]$clientOnly, [string[]]$only, [bool]$noClient) {
        $script:killed = @(); $ClientOnly = $clientOnly; $Only = $only; $NoClient = $noClient; $Walkmaps = $false
        Set-Content (Join-Path $clients 'default.json') '{}'; Set-Content (Join-Path $clients 's1.json') '{}'
        $said = & $stopRun
        [pscustomobject]@{ Killed = ($script:killed -join ','); Said = "$said"; Default = (Test-Path (Join-Path $clients 'default.json')); S1 = (Test-Path (Join-Path $clients 's1.json')) }
    }
    $session = 'default'
    $r = StopWith $false @() $false
    Assert ($r.Killed -eq '301,302,311,312,321,331,332,333') "full -Stop: $($r.Killed)"
    Assert ($r.Said -like 'stopped: client, walk-map recorder, Auth, Patch, Gateway (8 process(es)*') "full -Stop report: $($r.Said)"
    Assert (-not $r.Default -and -not $r.S1) 'a full -Stop removes the client records'
    $r = StopWith $false @('Gateway') $false
    Assert ($r.Killed -eq '333') "-Stop -Only Gateway must stop only that server: $($r.Killed)"
    Assert ($r.Default -and $r.S1) '-Stop -Only keeps the client records'
    $r = StopWith $false @() $true
    Assert ($r.Killed -eq '321,331,332,333') "-Stop -NoClient must keep the clients: $($r.Killed)"
    Assert ($r.Default -and $r.S1) '-Stop -NoClient keeps the client records'
    $session = 's1'
    $r = StopWith $true @() $false
    Assert ($r.Killed -eq '301,311') "-Stop -ClientOnly must stop only the session client and its launcher: $($r.Killed)"
    Assert ($r.Default -and -not $r.S1) '-Stop -ClientOnly removes only that record'
    $failed = $null; try { StopWith $true @('Gateway') $false | Out-Null } catch { $failed = $_.Exception.Message }
    Assert ($failed -like '*cannot be combined*') "-Stop -ClientOnly -Only must be refused: $failed"
    $Walkmaps = $true; $failed = $null; try { & $stopRun | Out-Null } catch { $failed = $_.Exception.Message }
    Assert ($failed -like '*-Walkmaps*') "-Stop -Walkmaps must be refused: $failed"
    $Walkmaps = $false; $Stop = $false; $ClientOnly = $true; $Only = @(); $NoClient = $false; $session = 'default'
    Remove-Item function:Get-OwnLaunchers, function:Get-OwnClients, function:Get-OwnWalkmaps, function:Get-OwnServers,
        function:Stop-Processes, function:Get-NetTCPConnection
    foreach ($name in 'Get-OwnLaunchers', 'Get-OwnClients', 'Get-OwnWalkmaps', 'Get-OwnServers') {
        $definition = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $false)
        if ($definition) { . ([scriptblock]::Create($definition.Extent.Text)) }
    }
    Remove-Item (Join-Path $clients '*.json') -ErrorAction SilentlyContinue

    $launch = [scriptblock]::Create($branch.Extent.Text)  # (the launcher test above reused the name)
    # -ClientOnly -Walkmaps: the recorder beside the default client on the default ports (also for a client already
    # running), never for a session or a stack on its own ports.
    function Get-NetTCPConnection { param($LocalPort, $State, $ErrorAction) [pscustomobject]@{ LocalPort = $LocalPort } }
    function Get-Python { 'python.exe' }
    function Start-Recorder($python) { $script:events.Add("recorder:$python") }
    function Assert-GlobalSlot { }
    $script:listed = @('Europe;Local (EU);127.0.0.1:20493;127.0.0.1;false')
    $script:processes = @()
    $Walkmaps = $true
    $stack = Get-Stack 0; $session = 'default'
    Run
    Assert (($script:events -join ',') -eq 'gateway-read,recorder:python.exe,launch:default') "-ClientOnly -Walkmaps must start the recorder before the client: $($script:events -join ',')"
    $script:processes = @(Client 42 $ownExe)
    Run
    Assert (($script:events -join ',') -eq 'recorder:python.exe') "a running default client still gets its recorder: $($script:events -join ',')"
    $script:processes = @()
    $session = 's1'
    $script:events.Clear(); & $launch 3>$null | Out-Null
    Assert (($script:events -join ',') -eq 'gateway-read,launch:s1') "a session gets no recorder: $($script:events -join ',')"
    $session = 'default'; $stack = Get-Stack 21000
    $script:events.Clear(); & $launch 3>$null | Out-Null
    Assert (($script:events -join ',') -eq 'launch:default') "a stack on its own ports gets no recorder: $($script:events -join ',')"
    $null = Get-Stack 0; $Walkmaps = $false
    Remove-Item function:Get-Python, function:Start-Recorder, function:Get-NetTCPConnection, function:Assert-GlobalSlot

    # A start that throws after the pending record was written removes it (the real Start-Client with the default
    # client: no session folder or settings file is made).
    $real = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Start-Client' }, $false)
    . ([scriptblock]::Create($real.Extent.Text))
    function Start-Process { throw 'cannot start' }
    $message = $null
    try { Start-Client 'default' (Get-Stack 0) '' $false | Out-Null } catch { $message = $_.Exception.Message }
    Remove-Item function:Start-Process
    Assert ($message -eq 'cannot start') "the start error must surface: $message"
    Assert (-not (Test-Path (Join-Path $clients 'default.json'))) 'a failed start must remove its pending record'
    $null = Get-Stack 0
}
finally {
    $env:Game_PORT_BASE = $null
    Remove-Item $repo -Recurse -Force -ErrorAction SilentlyContinue
}

# Not mocked, and only on request (Game_TEST_SHARED_GGPK=1): the real gateway-list read (GameServer.Patch gateway,
# without --login: it never writes) on this checkout's Client\Content.ggpk, which is shared with every checkout and
# client. Select-LocalGateway is not run against it: it may write. The mocked checks above cover its logic.
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$patchDll = Join-Path $repo 'src\GameServer.Patch\bin\Debug\net10.0\GameServer.Patch.dll'
$ggpk = Join-Path $repo 'Client\Content.ggpk'
if ($env:Game_TEST_SHARED_GGPK -eq '1' -and (Test-Path $patchDll) -and (Test-Path $ggpk)) {
    Remove-Item function:dotnet
    $listed = @(& dotnet $patchDll gateway $ggpk 2>$null | Where-Object { "$_" -match '^[^;]*;[^;]*;' })
    Assert ($LASTEXITCODE -eq 0 -and $listed.Count -gt 0) "the gateway list must be readable while clients hold the GGPK (exit $LASTEXITCODE)"
}
else { Write-Output 'skipped the real gateway-list read (set Game_TEST_SHARED_GGPK=1; needs a built GameServer.Patch and Client\Content.ggpk)' }
Write-Output 'live.ps1 client, port and ownership checks passed (mocked; no client or server started, nothing written).'
