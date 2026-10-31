# Measures one game client process for the headless-client comparison (Agents/docs/dev/headless-client.md):
# private commit, working set, CPU (percent of one core and of the whole machine), GPU engine utilisation and
# dedicated/shared GPU memory, sampled every -Interval seconds for -Seconds seconds. Read-only: it opens no handle that
# could change the process, so it is safe to point at any pid.
#   powershell -NoProfile -File scripts/dev/nullifier-measure.ps1 -ProcessId 1234 [-Seconds 60] [-Interval 5] [-Label baseline]
# Prints one JSON object (averages and peaks); -Out appends it as a line to a file.
param([Parameter(Mandatory)][int]$ProcessId, [int]$Seconds = 60, [int]$Interval = 5, [string]$Label = '', [string]$Out = '')
$ErrorActionPreference = 'Stop'
$p = Get-Process -Id $ProcessId
$cores = [Environment]::ProcessorCount
$gpuCounters = @("\GPU Engine(pid_$($ProcessId)_*)\Utilization Percentage",
                 "\GPU Process Memory(pid_$($ProcessId)_*)\Dedicated Usage",
                 "\GPU Process Memory(pid_$($ProcessId)_*)\Shared Usage")
$samples = @()
$cpuBefore = $p.TotalProcessorTime; $clock = [Diagnostics.Stopwatch]::StartNew()
$deadline = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $deadline) {
    # Get-Counter waits one interval; without GPU counters for the pid (no GPU use) it returns at once, so pad the rest.
    $set = Get-Counter -Counter $gpuCounters -SampleInterval $Interval -MaxSamples 1 -ErrorAction SilentlyContinue
    $left = $Interval - $clock.Elapsed.TotalSeconds
    if ($left -gt 0) { Start-Sleep -Milliseconds ([int]($left * 1000)) }
    $p.Refresh()
    if ($p.HasExited) { throw "process $ProcessId exited during the measurement" }
    $cpuNow = $p.TotalProcessorTime; $elapsed = $clock.Elapsed.TotalSeconds
    $gpu = 0.0; $dedicated = 0.0; $shared = 0.0
    foreach ($s in @($set.CounterSamples)) {
        if ($s.Path -match 'utilization percentage$') { $gpu += $s.CookedValue }
        elseif ($s.Path -match 'dedicated usage$') { $dedicated += $s.CookedValue }
        elseif ($s.Path -match 'shared usage$') { $shared += $s.CookedValue }
    }
    $samples += [pscustomobject]@{
        cpuCore = 100 * ($cpuNow - $cpuBefore).TotalSeconds / $elapsed
        privateMB = $p.PrivateMemorySize64 / 1MB; workingSetMB = $p.WorkingSet64 / 1MB
        gpu = $gpu; dedicatedMB = $dedicated / 1MB; sharedMB = $shared / 1MB }
    $cpuBefore = $cpuNow; $clock.Restart()
}
function Stat($name) {
    $m = $samples | Measure-Object -Property $name -Average -Maximum
    [ordered]@{ avg = [math]::Round($m.Average, 1); max = [math]::Round($m.Maximum, 1) }
}
$result = [ordered]@{
    label = $Label; pid = $ProcessId; seconds = $Seconds; samples = $samples.Count; cores = $cores
    privateMB = Stat 'privateMB'; workingSetMB = Stat 'workingSetMB'
    cpuPercentOfOneCore = Stat 'cpuCore'
    cpuPercentOfMachine = [ordered]@{ avg = [math]::Round(($samples | Measure-Object cpuCore -Average).Average / $cores, 2) }
    gpuUtilisationPercent = Stat 'gpu'; gpuDedicatedMB = Stat 'dedicatedMB'; gpuSharedMB = Stat 'sharedMB'
    threads = $p.Threads.Count; handles = $p.HandleCount
}
$json = $result | ConvertTo-Json -Compress -Depth 4
if ($Out) { Add-Content -Path $Out -Value $json -Encoding utf8 }
$json
