param(
    [ValidateSet('Arm','Run','Status','Cancel','Inspect','SelfTest','GateCheck')][string]$Action = 'Status',
    [string]$RunId = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'phase3-e2e-common.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stateDir = Join-Path $env:LOCALAPPDATA 'UniNetGitHubAppStep6E2E\watchdog'
$statePath = Join-Path $stateDir 'status.json'
$launcher = Join-Path $PSScriptRoot 'start-github-app-e2e.ps1'
$checker = Join-Path $PSScriptRoot 'check-github-app-e2e.ps1'

function Get-ListenerState {
    $ids = @(Get-NetTCPConnection -LocalPort 5239 -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique)
    if ($ids.Count -eq 0) { return 'Absent' }
    if ($ids.Count -ne 1) { return 'Conflict' }
    $p = Get-CimInstance Win32_Process -Filter "ProcessId=$($ids[0])"
    if (-not $p -or $p.ExecutablePath -ine (Join-Path $root 'UniNet.API\bin\Debug\net8.0\UniNet.API.exe')) { return 'Conflict' }
    $parent = Get-CimInstance Win32_Process -Filter "ProcessId=$($p.ParentProcessId)"
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $parent -or -not $dotnet -or $parent.Name -ine 'dotnet.exe' -or $parent.ExecutablePath -ine $dotnet.Source) { return 'Conflict' }
    $grand = Get-CimInstance Win32_Process -Filter "ProcessId=$($parent.ParentProcessId)"
    if (-not $grand -or $grand.Name -ine 'powershell.exe' -or $grand.CommandLine -notmatch [regex]::Escape($launcher)) { return 'Conflict' }
    foreach ($path in @('/health/github/readiness','/health/github/instance','/health/github/queue')) {
        try { $r = Invoke-WebRequest ('http://localhost:5239' + $path) -UseBasicParsing -TimeoutSec 3; if ($r.StatusCode -ne 200 -or $r.Content.Trim() -ne 'Healthy') { return 'Unhealthy' } }
        catch { return 'Unhealthy' }
    }
    return 'Healthy'
}
function Write-State([string]$id, [string]$status, [int]$pidValue, [int]$remaining = -1) {
    $entry = @{ runId = $id; status = $status; processId = $pidValue; timestampUtc = [DateTimeOffset]::UtcNow.ToString('o'); remainingSeconds = $remaining } | ConvertTo-Json -Compress
    $tmp = Join-Path $stateDir ('.state-' + [Guid]::NewGuid().ToString('N'))
    [IO.File]::WriteAllText($tmp, $entry, [Text.UTF8Encoding]::new($false))
    if ([IO.File]::Exists($statePath)) { [IO.File]::Replace($tmp, $statePath, (Join-Path $stateDir 'status.previous.json')) } else { [IO.File]::Move($tmp, $statePath) }
}
function Read-State {
    if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) { return $null }
    try { return Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } catch { return $null }
}
function Is-WatchdogAlive($state) {
    if ($null -eq $state -or $state.status -notin @('Armed','Recovering')) { return $false }
    try { $age = [DateTimeOffset]::UtcNow - [DateTimeOffset]::Parse([string]$state.timestampUtc); if ($age.TotalSeconds -lt 0 -or $age.TotalSeconds -gt 12) { return $false } } catch { return $false }
    $p = Get-CimInstance Win32_Process -Filter "ProcessId=$([int]$state.processId)"
    return $null -ne $p -and $p.Name -ieq 'powershell.exe' -and $p.CommandLine -match [regex]::Escape([string]$state.runId) -and $p.CommandLine -match 'github-app-e2e-watchdog\.ps1'
}
function Test-Deadline([TimeSpan]$elapsed, [TimeSpan]$limit, [bool]$cancelled) { return -not $cancelled -and $elapsed -ge $limit }
function Test-CancelAllowed([bool]$requested, [string]$listener) { return $requested -and $listener -ceq 'Healthy' }

if ($Action -eq 'SelfTest') {
    if ((Test-Deadline ([TimeSpan]::FromSeconds(179)) ([TimeSpan]::FromSeconds(180)) $false) -or
        -not (Test-Deadline ([TimeSpan]::FromSeconds(180)) ([TimeSpan]::FromSeconds(180)) $false) -or
        (Test-Deadline ([TimeSpan]::FromSeconds(181)) ([TimeSpan]::FromSeconds(180)) $true)) { throw 'deadline self-test failed' }
    if ((Test-CancelAllowed $true 'Absent') -or (Test-CancelAllowed $true 'Conflict') -or -not (Test-CancelAllowed $true 'Healthy')) { throw 'cancel safety self-test failed' }
    $deadState = [pscustomobject]@{ status='Armed'; processId=2147483647; runId=('0' * 32); timestampUtc=[DateTimeOffset]::UtcNow.ToString('o') }
    if (Is-WatchdogAlive $deadState) { throw 'dead process must not be considered alive' }
    Write-Output 'DEADLINE_CANCEL_DEAD_PROCESS_SELFTEST=PASS'
    # A separate process exercises a short simulated deadline and cancellation without a real listener or launcher.
    $simulation = Join-Path $env:TEMP ('uninet-watchdog-simulation-' + [Guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $simulation
    $marker = Join-Path $simulation 'cancel'; $result = Join-Path $simulation 'result'
    $childCode = @'
param($cancelPath, $resultPath)
$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt 2) { if (Test-Path -LiteralPath $cancelPath) { [IO.File]::WriteAllText($resultPath, 'cancelled'); exit 0 }; Start-Sleep -Milliseconds 100 }
[IO.File]::WriteAllText($resultPath, 'deadline')
'@
    $childScript = Join-Path $simulation 'simulation.ps1'; [IO.File]::WriteAllText($childScript, $childCode)
    $child = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile','-File',('"' + $childScript + '"'),('"' + $marker + '"'),('"' + $result + '"')) -WindowStyle Hidden -PassThru
    [IO.File]::WriteAllText($marker, 'cancel')
    if (-not $child.WaitForExit(10000) -or -not (Test-Path -LiteralPath $result) -or [IO.File]::ReadAllText($result) -cne 'cancelled') { throw 'independent cancellation simulation failed' }
    $result2 = Join-Path $simulation 'deadline-result'
    $child2 = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile','-File',('"' + $childScript + '"'),('"' + (Join-Path $simulation 'absent') + '"'),('"' + $result2 + '"')) -WindowStyle Hidden -PassThru
    if (-not $child2.WaitForExit(10000) -or -not (Test-Path -LiteralPath $result2) -or [IO.File]::ReadAllText($result2) -cne 'deadline') { throw 'independent deadline simulation failed' }
    Write-Output 'INDEPENDENT_PROCESS_SIMULATION=PASS'; exit 0
}
if ($Action -eq 'Inspect') { 'BACKEND_STATE=' + (Get-ListenerState); exit 0 }
if ($Action -eq 'Status' -or $Action -eq 'GateCheck') {
    $state = Read-State; if ($null -eq $state) { 'WATCHDOG_STATE=NOT_ARMED'; if ($Action -eq 'GateCheck') { exit 1 }; exit 0 }
    $alive = Is-WatchdogAlive $state
    $reportedStatus = if ($state.status -in @('Armed','Recovering') -and -not $alive) { 'Stale' } else { $state.status }
    'WATCHDOG_STATE=' + $reportedStatus; 'WATCHDOG_ALIVE=' + $alive; 'WATCHDOG_RUN_ID=' + $state.runId; 'WATCHDOG_STATUS_TIME_UTC=' + $state.timestampUtc; 'WATCHDOG_REMAINING_SECONDS=' + $state.remainingSeconds
    if ($Action -eq 'GateCheck') {
        if (-not $alive -or $state.status -cne 'Armed' -or (Get-ListenerState) -cne 'Healthy') { 'GATE_CHECK=BLOCKED'; exit 1 }
        $latest = Read-State
        if ($null -eq $latest -or $latest.runId -cne $state.runId -or $latest.status -cne 'Armed' -or -not (Is-WatchdogAlive $latest)) { 'GATE_CHECK=BLOCKED'; exit 1 }
        $age = ([DateTimeOffset]::UtcNow - [DateTimeOffset]::Parse([string]$latest.timestampUtc)).TotalSeconds
        if (([int]$latest.remainingSeconds - [Math]::Ceiling($age)) -lt 90) { 'GATE_CHECK=BLOCKED'; exit 1 }
        'GATE_CHECK=PASS'
    }
    exit 0
}
if ($Action -eq 'Cancel') {
    $state = Read-State
    if (-not (Is-WatchdogAlive $state)) { Write-Output '[FAIL] No live watchdog to cancel.'; exit 1 }
    if ((Get-ListenerState) -ne 'Healthy') { Write-Output '[FAIL] Backend is not verified Healthy; watchdog remains armed.'; exit 1 }
    [IO.File]::WriteAllText((Join-Path $stateDir ('cancel-' + $state.runId)), 'cancel')
    Write-Output '[PASS] Cancellation requested; check Status.'; exit 0
}
if ($Action -eq 'Arm') {
    & powershell.exe -NoProfile -File $checker | Out-Null
    if ($LASTEXITCODE -ne 0 -or (Get-ListenerState) -ne 'Healthy') { Write-Output '[FAIL] Preflight or Backend identity/health check failed.'; exit 1 }
    $null = New-Item -ItemType Directory -Path $stateDir -Force
    $lock = New-Object Threading.Mutex($false, 'Local\UniNetGitHubAppE2E-Watchdog-Arm-5239'); $held = $false
    try {
        try { $held = $lock.WaitOne([TimeSpan]::FromSeconds(10)) } catch [Threading.AbandonedMutexException] { $held = $true }
        if (-not $held -or (Is-WatchdogAlive (Read-State))) { throw 'already armed or lock unavailable' }
        $id = [Guid]::NewGuid().ToString('N')
        $child = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile','-File',('"' + $PSCommandPath + '"'),'-Action','Run','-RunId',$id) -WindowStyle Hidden -PassThru
        $ready = $false
        for ($i=0; $i -lt 50; $i++) { Start-Sleep -Milliseconds 100; $state=Read-State; if ($null -ne $state -and $state.runId -eq $id -and (Is-WatchdogAlive $state)) { $ready=$true; break }; if ($child.HasExited) { break } }
        if (-not $ready) { throw 'watchdog did not become ready' }
        Write-Output ('WATCHDOG_ARMED=' + $id + '; RECOVERY_TRIGGER_SECONDS=120; TARGET_LIMIT_SECONDS=180; PID=' + $child.Id)
    } catch { Write-Output '[FAIL] Watchdog did not arm safely.'; exit 1 }
    finally { if ($held) { $lock.ReleaseMutex() }; $lock.Dispose() }
    exit 0
}
if ($Action -ne 'Run' -or $RunId -notmatch '^[0-9a-f]{32}$') { exit 1 }
$null = New-Item -ItemType Directory -Path $stateDir -Force
$active = New-Object Threading.Mutex($false, 'Local\UniNetGitHubAppE2E-Watchdog-Active-5239'); $owns = $false
try {
    try { $owns = $active.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owns = $true }
    if (-not $owns) { exit 1 }
    $clock = [Diagnostics.Stopwatch]::StartNew(); $cancel = Join-Path $stateDir ('cancel-' + $RunId)
    # Begin recovery at two minutes to leave a one-minute budget before the three-minute target.
    Write-State $RunId 'Armed' $PID 120; $lastHeartbeat = -5.0
    while (-not (Test-Deadline $clock.Elapsed ([TimeSpan]::FromMinutes(2)) $false)) {
        if ($clock.Elapsed.TotalSeconds - $lastHeartbeat -ge 3) { Write-State $RunId 'Armed' $PID ([Math]::Max(0, [int][Math]::Floor(120 - $clock.Elapsed.TotalSeconds))); $lastHeartbeat = $clock.Elapsed.TotalSeconds }
        if ((Test-Path -LiteralPath $cancel) -and (Test-CancelAllowed $true (Get-ListenerState))) { Write-State $RunId 'Cancelled' $PID; exit 0 }
        Start-Sleep -Milliseconds 500
    }
    if ((Test-Path -LiteralPath $cancel) -and (Test-CancelAllowed $true (Get-ListenerState))) { Write-State $RunId 'Cancelled' $PID; exit 0 }
    $listener = Get-ListenerState
    if ($listener -eq 'Healthy') { Write-State $RunId 'AlreadyHealthy' $PID; exit 0 }
    if ($listener -eq 'Conflict') { Write-State $RunId 'BlockedPortConflict' $PID; exit 1 }
    if ($listener -eq 'Unhealthy') { Write-State $RunId 'BlockedBackendUnhealthy' $PID; exit 1 }
    if ($listener -ne 'Absent') { Write-State $RunId 'BlockedUnknownListenerState' $PID; exit 1 }
    & powershell.exe -NoProfile -File $checker | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-State $RunId 'PreflightFailed' $PID; exit 1 }
    if ((Test-Path -LiteralPath $cancel) -and (Test-CancelAllowed $true (Get-ListenerState))) { Write-State $RunId 'Cancelled' $PID; exit 0 }
    Write-State $RunId 'Recovering' $PID
    $child = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile','-File',('"' + $launcher + '"')) -WorkingDirectory $root -WindowStyle Hidden -PassThru
    $wait = [Diagnostics.Stopwatch]::StartNew()
    while ($wait.Elapsed.TotalSeconds -lt 60) {
        Start-Sleep -Seconds 2
        Write-State $RunId 'Recovering' $PID 0
        $listener = Get-ListenerState
        if ($listener -eq 'Healthy') { Write-State $RunId 'Recovered' $PID; exit 0 }
        if ($listener -eq 'Conflict') { Write-State $RunId 'RecoveryPortConflict' $PID; exit 1 }
        if ($child.HasExited) { Write-State $RunId 'RecoveryLauncherExited' $PID; exit 1 }
    }
    Write-State $RunId 'RecoveryHealthTimeout' $PID; exit 1
} catch { Write-State $RunId 'WatchdogFailed' $PID; exit 1 }
finally { if ($owns) { $active.ReleaseMutex() }; $active.Dispose() }
