$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'phase3-e2e-common.ps1')

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$apiRoot = Join-Path $repoRoot 'UniNet.API'
$projectPath = Join-Path $apiRoot 'UniNet.API.csproj'
$envPath = Join-Path $repoRoot '.env.e2e'

try {
    $values = Read-Phase3E2EEnv -Path $envPath
    $checks = Test-Phase3E2EConfiguration -RepoRoot $repoRoot -Values $values
}
catch {
    Write-Output '[FAIL] Could not safely parse E2E configuration. Check .env.e2e syntax and duplicate keys.'
    exit 1
}

foreach ($check in $checks) {
    Write-Output ('[{0}] {1}' -f $check.Status, $check.Message)
}

$failures = @($checks | Where-Object Status -eq 'FAIL')
if ($failures.Count -gt 0) {
    Write-Output '[FAIL] Backend was not started. Fix the failed checks and run check-phase3-e2e.ps1 first.'
    exit 1
}

$keyRingPath = Join-Path $apiRoot $values['GitHub__KeyRingPath']
try {
    if (-not (Test-Path -LiteralPath $keyRingPath -PathType Container)) {
        $null = New-Item -ItemType Directory -Path $keyRingPath -Force
    }
}
catch {
    Write-Output '[FAIL] Could not create the ignored E2E Data Protection key directory.'
    exit 1
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Output '[FAIL] dotnet was not found on PATH.'
    exit 1
}

# Build a clean child-process environment: no repository .env, User Secrets,
# Neon, Cloudinary, payment, or other inherited integration variables.
$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $dotnet.Source
$startInfo.Arguments = 'run --no-restore --project "' + $projectPath + '" --no-launch-profile'
$startInfo.WorkingDirectory = $repoRoot
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $false
$startInfo.EnvironmentVariables.Clear()

$safeRuntimeEnvironment = @(
    'PATH', 'SystemRoot', 'WINDIR', 'TEMP', 'TMP', 'USERPROFILE', 'HOMEDRIVE', 'HOMEPATH',
    'APPDATA', 'LOCALAPPDATA', 'COMSPEC', 'PATHEXT', 'DOTNET_ROOT', 'DOTNET_ROOT_X64',
    'DOTNET_ROOT_X86', 'DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'PROCESSOR_ARCHITECTURE',
    'NUMBER_OF_PROCESSORS'
)
foreach ($name in $safeRuntimeEnvironment) {
    $value = [System.Environment]::GetEnvironmentVariable($name, 'Process')
    if ($null -ne $value) { $startInfo.EnvironmentVariables[$name] = $value }
}

foreach ($name in $values.Keys) {
    $startInfo.EnvironmentVariables[$name] = $values[$name]
}
$startInfo.EnvironmentVariables['ASPNETCORE_CONTENTROOT'] = $apiRoot
$startInfo.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'E2E'
$startInfo.EnvironmentVariables['UNINET_SKIP_LOCAL_ENV_FILE'] = 'true'
$startInfo.EnvironmentVariables['DOTNET_NOLOGO'] = 'true'
$startInfo.EnvironmentVariables['DOTNET_SKIP_FIRST_TIME_EXPERIENCE'] = '1'
$startInfo.EnvironmentVariables['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'

Write-Output '[PASS] Starting UniNet API with the isolated E2E environment. Secrets are supplied through process environment, not command-line arguments.'
$process = $null
try {
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $process.WaitForExit()
    $exitCode = $process.ExitCode
}
catch {
    Write-Output '[FAIL] Backend process failed to start.'
    exit 1
}
finally {
    if ($process) { $process.Dispose() }
}

exit $exitCode
