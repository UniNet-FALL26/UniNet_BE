$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'check-github-app-e2e.ps1')
if ($LASTEXITCODE -ne 0) { Write-Output '[FAIL] Preflight failed; backend not started.'; exit 1 }
. (Join-Path $PSScriptRoot 'phase3-e2e-common.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$values = Read-Phase3E2EEnv (Join-Path $root '.env.github-app.e2e')
try { $pem = [IO.File]::ReadAllText($values['GitHubAppE2E__PrivateKeyPath']) } catch { Write-Output '[FAIL] Could not read the local PEM.'; exit 1 }
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { Write-Output '[FAIL] dotnet not found.'; exit 1 }
$info = New-Object Diagnostics.ProcessStartInfo
$info.FileName = $dotnet.Source
$info.Arguments = 'run --no-restore --project "' + (Join-Path $root 'UniNet.API/UniNet.API.csproj') + '" --no-launch-profile'
$info.WorkingDirectory = $root; $info.UseShellExecute = $false
$info.EnvironmentVariables.Clear()
foreach ($name in @('PATH','SystemRoot','WINDIR','TEMP','TMP','USERPROFILE','HOMEDRIVE','HOMEPATH','APPDATA','LOCALAPPDATA','COMSPEC','PATHEXT','DOTNET_ROOT','DOTNET_ROOT_X64','DOTNET_CLI_HOME','NUGET_PACKAGES','PROCESSOR_ARCHITECTURE','NUMBER_OF_PROCESSORS')) {
    $value = [Environment]::GetEnvironmentVariable($name, 'Process'); if ($null -ne $value) { $info.EnvironmentVariables[$name] = $value }
}
foreach ($name in $values.Keys) { if ($name -notlike 'GitHubAppE2E__*') { $info.EnvironmentVariables[$name] = $values[$name] } }
$info.EnvironmentVariables['GitHubApp__PrivateKeyPem'] = $pem
$info.EnvironmentVariables['ASPNETCORE_CONTENTROOT'] = Join-Path $root 'UniNet.API'
$info.EnvironmentVariables['DOTNET_NOLOGO'] = 'true'
$mutex = New-Object System.Threading.Mutex($false, 'Local\UniNetGitHubAppE2E-Launch-5239')
$locked = $false
$process = $null
try {
    try { $locked = $mutex.WaitOne([TimeSpan]::FromSeconds(100)) }
    catch [System.Threading.AbandonedMutexException] { $locked = $true }
    if (-not $locked) { throw 'launcher lock unavailable' }
    if (Test-Phase3E2ETcpPort '127.0.0.1' 5239) { throw 'port occupied' }
    Write-Output '[PASS] Starting isolated E2E backend; runtime configuration is supplied via child environment.'
    $process = [Diagnostics.Process]::Start($info)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Phase3E2ETcpPort '127.0.0.1' 5239)) {
        if ($process.HasExited -or $watch.Elapsed.TotalSeconds -ge 90) { throw 'backend startup did not complete' }
        Start-Sleep -Milliseconds 300
    }
    $listenerIds = @(Get-NetTCPConnection -LocalPort 5239 -State Listen -ErrorAction Stop | Select-Object -ExpandProperty OwningProcess -Unique)
    if ($listenerIds.Count -ne 1) { throw 'listener ownership ambiguous' }
    $listener = Get-CimInstance Win32_Process -Filter "ProcessId=$($listenerIds[0])"
    if (-not $listener -or $listener.ParentProcessId -ne $process.Id -or
        $listener.ExecutablePath -ine (Join-Path $root 'UniNet.API\bin\Debug\net8.0\UniNet.API.exe')) { throw 'listener ownership mismatch' }
    $mutex.ReleaseMutex(); $locked = $false
    $process.WaitForExit(); $result = $process.ExitCode
}
catch { Write-Output '[FAIL] Backend launch did not complete; check port and startup configuration.'; exit 1 }
finally { if ($locked) { $mutex.ReleaseMutex() }; $mutex.Dispose(); if ($process) { $process.Dispose() } }
exit $result
