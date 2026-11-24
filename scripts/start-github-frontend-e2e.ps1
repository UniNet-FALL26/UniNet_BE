param(
    [string]$DatabaseName = 'uninet_github_fe_e2e_20261121',
    [int]$Port = 5239,
    [switch]$AdjustmentExecution
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'phase3-e2e-common.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
# This launcher never creates databases or applies migrations. Prepare the clone separately.
if ($DatabaseName -notmatch '^uninet_github_fe_e2e_[0-9]{8}$' -or $Port -notin @(5239,5240)) {
    throw 'Only a prepared local FE disposable database and E2E port are accepted.'
}
$values = Read-Phase3E2EEnv (Join-Path $root '.env.github-app.e2e')
if (-not $values.ContainsKey('GitHubAppE2E__PrivateKeyPath') -or
    -not (Test-Path -LiteralPath $values['GitHubAppE2E__PrivateKeyPath'] -PathType Leaf)) {
    throw 'E2E_GITHUB_APP_PRIVATE_KEY_MISSING: restore the configured E2E App PEM; no alternate App key is accepted.'
}
$connection = ConvertFrom-Phase3E2EConnectionString $values['ConnectionStrings__UniNet']
if ($connection['Host'] -cne '127.0.0.1' -or $connection['Port'] -ne '55433' -or
    $connection['Database'] -cne 'uninet_github_app_e2e' -or $connection['Username'] -cne 'uninet_test') {
    throw 'Source E2E target does not match the reviewed local database.'
}
$mapping = & docker port uninet-step2-concurrency 5432/tcp
if ($LASTEXITCODE -ne 0 -or @($mapping).Count -ne 1 -or $mapping -cne '127.0.0.1:55433') { throw 'Container mapping mismatch.' }
$identity = @('SELECT current_database(),current_user' | & docker exec -i uninet-step2-concurrency psql -U uninet_test -d $DatabaseName -At -v ON_ERROR_STOP=1)
if ($LASTEXITCODE -ne 0 -or $identity -cne "$DatabaseName|uninet_test") { throw 'Disposable database identity mismatch.' }
$applied = @('SELECT "MigrationId" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId"' | & docker exec -i uninet-step2-concurrency psql -U uninet_test -d $DatabaseName -At -v ON_ERROR_STOP=1)
if ($LASTEXITCODE -ne 0) { throw 'Could not verify migration history.' }
$expected = @(Get-ChildItem (Join-Path $root 'UniNet.Infrastructure/Data/Migrations') -Filter '*.Designer.cs' | ForEach-Object { $_.Name -replace '\.Designer\.cs$', '' })
if (@(Compare-Object ($expected | Sort-Object) ($applied | Sort-Object)).Count -ne 0) { throw 'Disposable schema differs from source; no automatic migration is permitted.' }
if (Test-Phase3E2ETcpPort '127.0.0.1' $Port) { throw 'Port occupied; identify the listener before starting.' }
$receipt = Join-Path $env:LOCALAPPDATA 'UniNetGitHubFrontendE2E-runtime'
$null = New-Item -ItemType Directory -Path $receipt -Force
$principal = [Security.Principal.WindowsIdentity]::GetCurrent().Name
& icacls $receipt /inheritance:r /grant:r "${principal}:(OI)(CI)F" 'SYSTEM:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not protect runtime directory.' }
$artifacts = Join-Path $receipt 'build'
& dotnet build (Join-Path $root 'UniNet.API/UniNet.API.csproj') --artifacts-path $artifacts --nologo
if ($LASTEXITCODE -ne 0) { throw 'Current source build failed.' }
$binary = Join-Path $artifacts 'bin/UniNet.API/debug/UniNet.API.dll'
if (-not (Test-Path -LiteralPath $binary)) { throw 'Expected current build not found.' }
$builder = New-Object System.Data.Common.DbConnectionStringBuilder
# DbConnectionStringBuilder is an IDictionary. PowerShell dot assignment can add a
# key named ConnectionString instead of invoking the CLR setter, hiding overrides.
$builder.set_ConnectionString($values['ConnectionStrings__UniNet'])
$builder['Database'] = $DatabaseName
$values['ConnectionStrings__UniNet'] = $builder.get_ConnectionString()
$effectiveTarget = ConvertFrom-Phase3E2EConnectionString $values['ConnectionStrings__UniNet']
if ($effectiveTarget['Host'] -cne '127.0.0.1' -or $effectiveTarget['Port'] -ne '55433' -or
    $effectiveTarget['Database'] -cne $DatabaseName -or $effectiveTarget['Username'] -cne 'uninet_test') {
    throw 'Effective child database override failed; API will not start.'
}
$info = New-Object Diagnostics.ProcessStartInfo
$info.FileName = (Get-Command dotnet).Source
$info.Arguments = '"' + $binary + '"'
$info.WorkingDirectory = Join-Path $root 'UniNet.API'
$info.UseShellExecute = $false
$info.EnvironmentVariables.Clear()
foreach ($name in @('PATH','SystemRoot','WINDIR','TEMP','TMP','USERPROFILE','HOMEDRIVE','HOMEPATH','APPDATA','LOCALAPPDATA','COMSPEC','PATHEXT','DOTNET_ROOT','DOTNET_ROOT_X64','PROCESSOR_ARCHITECTURE','NUMBER_OF_PROCESSORS')) {
    $value = [Environment]::GetEnvironmentVariable($name, 'Process')
    if ($null -ne $value) { $info.EnvironmentVariables[$name] = $value }
}
foreach ($name in $values.Keys) {
    if ($name -notlike 'GitHubAppE2E__*') { $info.EnvironmentVariables[$name] = $values[$name] }
}
$overrides = @{
    ASPNETCORE_ENVIRONMENT='E2E'; DOTNET_ENVIRONMENT='E2E'; UNINET_SKIP_LOCAL_ENV_FILE='true'
    ASPNETCORE_URLS="http://localhost:$Port"; ASPNETCORE_CONTENTROOT=(Join-Path $root 'UniNet.API')
    BackgroundWorkers__Enabled=($AdjustmentExecution.IsPresent.ToString().ToLowerInvariant()); GitHubE2E__DisableUnrelatedWorkers='true'
    GitHubSync__AdjustmentExecutionEnabled=($AdjustmentExecution.IsPresent.ToString().ToLowerInvariant())
    GitHubSync__Enabled='false'; GitHubSync__ManualEnqueueEnabled='false'
    GitHubSync__ScopedManual__Enabled='false'; GitHubSync__ScopedManual__JobId=''
    AssessorOnboarding__Enabled='false'; VerificationWorker__Enabled='false'
    Cors__Origins__0='http://localhost:8081'; Cors__Origins__1='http://127.0.0.1:8081'
    GitHub__ReturnTargets__expoWeb='http://localhost:8081/github/callback'
    GitHub__ReturnTargets__expoNative='uninet://github/callback'
    GitHub__MobileRedirectSchemes__0='exp'; GitHub__MobileRedirectSchemes__1='uninet'
}
foreach ($name in $overrides.Keys) { $info.EnvironmentVariables[$name] = $overrides[$name] }
$info.EnvironmentVariables['GitHubApp__PrivateKeyPem'] = [IO.File]::ReadAllText($values['GitHubAppE2E__PrivateKeyPath'])
$process = [Diagnostics.Process]::Start($info)
@{Pid=$process.Id; Binary=$binary; Database=$effectiveTarget['Database']; Host=$effectiveTarget['Host']; Port=55433; ApiPort=$Port; Environment='E2E'; Workers=$(if($AdjustmentExecution){'adjustment-only'}else{'off'}); GlobalSync=$false; ManualEnqueue=$false; ScopedManual=$false; AdjustmentExecution=$AdjustmentExecution.IsPresent; ConnectionTargetVerified=$true; StartedAt=[DateTime]::UtcNow.ToString('o')} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $receipt 'runtime.json')
Write-Output "E2E API PID=$($process.Id); http://localhost:$Port; disposable=$DatabaseName; adjustment-only=$($AdjustmentExecution.IsPresent); global sync off."
$process.WaitForExit()
exit $process.ExitCode
