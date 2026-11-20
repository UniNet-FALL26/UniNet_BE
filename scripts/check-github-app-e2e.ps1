$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'phase3-e2e-common.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$config = Join-Path $root '.env.github-app.e2e'
function Stop-Check([string]$message) { Write-Output "[FAIL] $message"; exit 1 }
try { $v = Read-Phase3E2EEnv $config } catch { Stop-Check 'Local GitHub App E2E configuration missing or malformed.' }
$required = @('ASPNETCORE_ENVIRONMENT','ASPNETCORE_URLS','UNINET_SKIP_LOCAL_ENV_FILE','ConnectionStrings__UniNet','Jwt__Key','Jwt__Issuer','Jwt__Audience','GitHub__ClientId','GitHub__ClientSecret','GitHub__CallbackUrl','GitHub__ReturnTargets__e2e','GitHub__KeyRingPath','GitHub__KeyRingCertificatePath','GitHubWebhook__Secret','GitHubApp__Enabled','GitHubApp__AppId','GitHubApp__ClientId','GitHubApp__ClientSecret','GitHubAppE2E__PrivateKeyPath','GitHubApp__WebhookSecret','GitHubApp__CallbackUrl','GitHubApp__ReturnTargets__e2e','GitHubAppE2E__WebhookPublicUrl','GitHubSync__Enabled','GitHubE2E__DisableUnrelatedWorkers','VerificationWorker__Enabled')
foreach ($k in $required) { if (-not $v.ContainsKey($k) -or [string]::IsNullOrWhiteSpace($v[$k]) -or $v[$k] -match '__FILL_ME|<[^>]+>') { Stop-Check "Missing or placeholder setting: $k" } }
if ($v['ASPNETCORE_ENVIRONMENT'] -cne 'E2E' -or $v['ASPNETCORE_URLS'] -cne 'http://localhost:5239' -or $v['UNINET_SKIP_LOCAL_ENV_FILE'] -ine 'true' -or $v['GitHubApp__Enabled'] -ine 'true' -or $v['GitHubSync__Enabled'] -ine 'true' -or $v['GitHubE2E__DisableUnrelatedWorkers'] -ine 'true' -or $v['VerificationWorker__Enabled'] -ine 'false') { Stop-Check 'Unsafe environment or worker settings.' }
if ([Text.Encoding]::UTF8.GetByteCount($v['Jwt__Key']) -lt 32 -or $v['GitHubApp__AppId'] -notmatch '^[1-9][0-9]*$') { Stop-Check 'Invalid JWT key length or App ID.' }
if ($v['GitHubApp__WebhookSecret'] -ceq $v['GitHubWebhook__Secret']) { Stop-Check 'App and legacy webhook secrets must differ.' }
try { $c = ConvertFrom-Phase3E2EConnectionString $v['ConnectionStrings__UniNet'] } catch { Stop-Check 'Malformed database configuration.' }
$allowed = @('Host','Port','Database','Username','Password','SSL Mode')
foreach ($k in $c.Keys) { if ($k -notin $allowed) { Stop-Check 'Database connection includes an unsupported parameter.' } }
if ($c['Host'] -cne '127.0.0.1' -or
    $c['Port'] -cne '55433' -or
    $c['Database'] -cne 'uninet_github_app_e2e' -or
    [string]::IsNullOrWhiteSpace($c['Username']) -or
    [string]::IsNullOrWhiteSpace($c['Password']) -or
    $c['Username'] -match '__FILL_ME|<[^>]+>' -or
    $c['Password'] -match '__FILL_ME|<[^>]+>' -or
    $c['SSL Mode'] -ine 'Disable') {
    Stop-Check 'Database target must be the dedicated local GitHub App E2E database.'
}
function Test-Https([string]$value, [string]$path) {
    $uri = $null
    if (-not [Uri]::TryCreate($value, [UriKind]::Absolute, [ref]$uri)) { return $false }
    return $uri.Scheme -ceq 'https' -and -not $uri.UserInfo -and -not $uri.Query -and -not $uri.Fragment -and ($path -eq '' -or $uri.AbsolutePath -ceq $path)
}
if (-not (Test-Https $v['GitHub__CallbackUrl'] '/api/github/oauth/callback') -or -not (Test-Https $v['GitHubApp__CallbackUrl'] '/api/github/app/authorization/callback') -or -not (Test-Https $v['GitHubAppE2E__WebhookPublicUrl'] '/api/webhooks/github/app') -or -not (Test-Https $v['GitHub__ReturnTargets__e2e'] '') -or -not (Test-Https $v['GitHubApp__ReturnTargets__e2e'] '')) { Stop-Check 'Invalid HTTPS callback, webhook or return target.' }
$origin = ([Uri]$v['GitHubAppE2E__WebhookPublicUrl']).GetLeftPart([UriPartial]::Authority)
if (([Uri]$v['GitHubApp__CallbackUrl']).GetLeftPart([UriPartial]::Authority) -cne $origin -or ([Uri]$v['GitHub__CallbackUrl']).GetLeftPart([UriPartial]::Authority) -cne $origin) { Stop-Check 'Callbacks and shared webhook must use the same configured tunnel origin.' }
foreach ($k in @('GitHub__KeyRingPath','GitHub__KeyRingCertificatePath','GitHubAppE2E__PrivateKeyPath')) {
    if (-not [IO.Path]::IsPathRooted($v[$k])) { Stop-Check "Path must be absolute: $k" }
    $p = [IO.Path]::GetFullPath($v[$k]); if ($p.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { Stop-Check "Private path must be outside the repository: $k" }
    if (-not (Test-Path -LiteralPath $p)) { Stop-Check "Required local path not found: $k" }
}
if (-not (Test-Path -LiteralPath $v['GitHub__KeyRingPath'] -PathType Container) -or -not (Test-Path -LiteralPath $v['GitHub__KeyRingCertificatePath'] -PathType Leaf) -or -not (Test-Path -LiteralPath $v['GitHubAppE2E__PrivateKeyPath'] -PathType Leaf)) { Stop-Check 'Key ring, PFX or PEM path has the wrong type.' }
if (-not (Test-Phase3E2EIgnoredPath $root '.env.github-app.e2e' -MustBeIgnored)) { Stop-Check 'Local secret configuration is not Git ignored.' }
$mapping = & docker port uninet-step2-concurrency 5432/tcp 2>$null
if ($LASTEXITCODE -ne 0 -or @($mapping).Count -ne 1 -or $mapping -cne '127.0.0.1:55433') { Stop-Check 'Expected dedicated local PostgreSQL container mapping is unavailable.' }
$db = & docker exec uninet-step2-concurrency psql -U postgres -d uninet_github_app_e2e -At -v ON_ERROR_STOP=1 -c 'SELECT current_database()' 2>$null
if ($LASTEXITCODE -ne 0 -or $db -cne 'uninet_github_app_e2e') { Stop-Check 'Dedicated database is absent or could not be verified; no database was created.' }
$historySql = "SELECT EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public' AND c.relname = '__EFMigrationsHistory' AND c.relkind IN ('r','p'))"
$history = $historySql | & docker exec -i uninet-step2-concurrency psql -U postgres -d uninet_github_app_e2e -At -v ON_ERROR_STOP=1 2>$null
if ($LASTEXITCODE -ne 0 -or $history -cne 't') { Stop-Check 'Migration history is absent; review and explicitly authorize migration separately.' }
$applied = @('SELECT "MigrationId" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId"' | & docker exec -i uninet-step2-concurrency psql -U postgres -d uninet_github_app_e2e -At -v ON_ERROR_STOP=1 2>$null)
if ($LASTEXITCODE -ne 0) { Stop-Check 'Could not read migration history.' }
$expected = @(Get-ChildItem -LiteralPath (Join-Path $root 'UniNet.Infrastructure/Data/Migrations') -Filter '*.Designer.cs' | ForEach-Object { $_.Name -replace '\.Designer\.cs$', '' })
if (@(Compare-Object ($expected | Sort-Object) ($applied | Sort-Object)).Count -ne 0) { Stop-Check 'Migration history differs from source; review drift and approve migration separately.' }
Write-Output '[PASS] Local target, source migration history, required paths and HTTPS URL shapes validated.'
Write-Output '[NOTE] GitHub registration, tunnel reachability, database login, PEM/PFX cryptography and filesystem ACLs require separate verification. No GitHub call or migration was made.'
