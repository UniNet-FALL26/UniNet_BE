# Disposable mutex/port rehearsal. Does not import E2E configuration or invoke the real launcher.
$ErrorActionPreference = 'Stop'
$tempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
$dir = Join-Path $tempRoot ('uninet-watchdog-isolated-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $dir
$resolved = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $dir).Path)
if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe simulation directory.' }
$children = @()
try {
    $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $probe.Start(); $port = ([Net.IPEndPoint]$probe.LocalEndpoint).Port; $probe.Stop()
    if ($port -eq 5239) { throw 'Unsafe simulation port.' }
    $mutex = 'Local\UniNetWatchdogTest-' + [Guid]::NewGuid().ToString('N')
    $script = Join-Path $dir 'contender.ps1'
    $code = @'
param([int]$port,[string]$mutexName,[string]$gate,[string]$result,[string]$mode)
$ErrorActionPreference='Stop'; $mx=New-Object Threading.Mutex($false,$mutexName); $held=$false; $listener=$null
try {
    while (-not (Test-Path -LiteralPath $gate)) { Start-Sleep -Milliseconds 20 }
    try { $held=$mx.WaitOne([TimeSpan]::FromSeconds(10)) } catch [Threading.AbandonedMutexException] { $held=$true }
    if (-not $held) { throw 'mutex timeout' }
    if ($mode -eq 'fail') { throw 'simulated startup failure' }
    $client=New-Object Net.Sockets.TcpClient
    try { $pending=$client.BeginConnect('127.0.0.1',$port,$null,$null); $busy=$pending.AsyncWaitHandle.WaitOne(300) -and $client.Connected } finally { $client.Dispose() }
    if ($busy) { [IO.File]::WriteAllText($result,'blocked'); exit 0 }
    $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$port); $listener.Start()
    [IO.File]::WriteAllText($result,'started'); $mx.ReleaseMutex(); $held=$false
    $clock=[Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt 6) {
        if (-not $listener.Pending()) { Start-Sleep -Milliseconds 30; continue }
        $connection=$listener.AcceptTcpClient()
        try { $stream=$connection.GetStream(); $buffer=New-Object byte[] 4096; $null=$stream.Read($buffer,0,$buffer.Length); $response=[Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nContent-Type: text/plain`r`nContent-Length: 7`r`nConnection: close`r`n`r`nHealthy"); $stream.Write($response,0,$response.Length) } finally { $connection.Dispose() }
    }
} catch { [IO.File]::WriteAllText($result,'failed'); exit 1 }
finally { if ($listener) { $listener.Stop() }; if ($held) { $mx.ReleaseMutex() }; $mx.Dispose() }
'@
    [IO.File]::WriteAllText($script, $code)
    $gate = Join-Path $dir 'gate'; $one = Join-Path $dir 'one'; $two = Join-Path $dir 'two'
    foreach ($result in @($one, $two)) {
        $children += Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList @('-NoProfile','-File',('"'+$script+'"'),$port,('"'+$mutex+'"'),('"'+$gate+'"'),('"'+$result+'"'),'normal')
    }
    [IO.File]::WriteAllText($gate, 'go')
    $wait=[Diagnostics.Stopwatch]::StartNew()
    while ((-not (Test-Path -LiteralPath $one) -or -not (Test-Path -LiteralPath $two)) -and $wait.Elapsed.TotalSeconds -lt 10) { Start-Sleep -Milliseconds 50 }
    foreach ($path in @('/health/github/readiness','/health/github/instance','/health/github/queue')) {
        $response=Invoke-WebRequest ('http://127.0.0.1:' + $port + $path) -UseBasicParsing -TimeoutSec 2
        if ($response.StatusCode -ne 200 -or $response.Content -cne 'Healthy') { throw 'Simulated health check failed.' }
    }
    Write-Output 'DISPOSABLE_HEALTH_ENDPOINTS=PASS'
    foreach ($child in $children) { if (-not $child.WaitForExit(15000)) { throw 'Simulation timed out.' } }
    $results = @([IO.File]::ReadAllText($one), [IO.File]::ReadAllText($two))
    if (@($results | Where-Object { $_ -eq 'started' }).Count -ne 1 -or @($results | Where-Object { $_ -eq 'blocked' }).Count -ne 1) { throw 'Unexpected concurrency result.' }
    Write-Output ('DISPOSABLE_PORT=' + $port); Write-Output 'CONCURRENT_START_ONE_WINNER=PASS'
    $failure = Join-Path $dir 'failure'
    $child = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList @('-NoProfile','-File',('"'+$script+'"'),$port,('"'+$mutex+'"'),('"'+$gate+'"'),('"'+$failure+'"'),'fail')
    $children += $child
    if (-not $child.WaitForExit(10000) -or [IO.File]::ReadAllText($failure) -cne 'failed') { throw 'Failure simulation did not complete.' }
    $testMutex = New-Object Threading.Mutex($false, $mutex); $held=$false
    try { try { $held=$testMutex.WaitOne(2000) } catch [Threading.AbandonedMutexException] { $held=$true }; if (-not $held) { throw 'Mutex leaked.' }; Write-Output 'MUTEX_RELEASE_AFTER_FAILURE=PASS' }
    finally { if ($held) { $testMutex.ReleaseMutex() }; $testMutex.Dispose() }
} finally {
    foreach ($child in $children) { if (-not $child.HasExited) { $child.Kill(); $child.WaitForExit() }; $child.Dispose() }
    $actual = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $dir).Path)
    if ($actual -ceq $resolved -and $actual.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $actual -Recurse -Force }
}
Write-Output 'OWN_PROCESSES_AND_FILES_CLEANED=PASS'
