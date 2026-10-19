[CmdletBinding()]
param(
    [string]$EnvironmentName = 'configured-local',
    [string]$OutputRoot,
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [switch]$Archive
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!$OutputRoot) { $OutputRoot = Join-Path $repo 'artifacts/verification-g0' }
if ($EnvironmentName -notmatch '^[a-zA-Z0-9_-]+$') { throw 'EnvironmentName must be a simple label.' }
$connection = [Environment]::GetEnvironmentVariable('ConnectionStrings__UniNet')
if (!$connection) {
    foreach ($raw in Get-Content -LiteralPath (Join-Path $repo '.env')) {
        $line = $raw.Trim() -replace '^export\s+', ''
        if ($line -match '^ConnectionStrings__UniNet\s*=(.*)$') {
            $connection = $Matches[1].Trim()
            if ($connection.Length -ge 2 -and (($connection.StartsWith('"') -and $connection.EndsWith('"')) -or ($connection.StartsWith("'") -and $connection.EndsWith("'")))) { $connection = $connection.Substring(1,$connection.Length-2) }
        }
    }
}
if (!$connection) { throw 'No configured connection. No default credentials will be used.' }
$builder = [System.Data.Common.DbConnectionStringBuilder]::new()
$builder.set_ConnectionString($connection)
$run = Join-Path $OutputRoot ($EnvironmentName + '-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$names = @('PGHOST','PGPORT','PGDATABASE','PGUSER','PGPASSWORD','PGCONNECT_TIMEOUT','PGSSLMODE','PGSSLROOTCERT','PGCHANNELBINDING','PGAPPNAME','PGOPTIONS')
$saved = @{}
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    $map = @{ Host='PGHOST'; Port='PGPORT'; Database='PGDATABASE'; Username='PGUSER'; 'User ID'='PGUSER'; Password='PGPASSWORD' }
    foreach ($name in @('PGHOST','PGPORT','PGDATABASE','PGUSER','PGPASSWORD','PGSSLMODE')) { [Environment]::SetEnvironmentVariable($name,$null) }
    foreach ($key in $map.Keys) { if ($builder.ContainsKey($key)) { [Environment]::SetEnvironmentVariable($map[$key],[string]$builder[$key]) } }
    if ($builder.ContainsKey('SSL Mode')) { $env:PGSSLMODE = ([string]$builder['SSL Mode']).ToLowerInvariant().Replace('verifyfull','verify-full').Replace('verifyca','verify-ca') }
    if (!$env:PGSSLMODE) { $env:PGSSLMODE = 'prefer' }
    if ($env:PGSSLMODE -in @('verify-full','verify-ca')) {
        # libpq/OpenSSL on Windows does not automatically use the Windows trust store.
        $roots = @(Get-ChildItem Cert:\LocalMachine\Root; Get-ChildItem Cert:\CurrentUser\Root)
        if (!$roots.Count) { throw 'No system trust roots available; supply a trusted CA bundle.' }
        $pem = foreach ($cert in $roots) { "-----BEGIN CERTIFICATE-----`n" + [Convert]::ToBase64String($cert.RawData,[Base64FormattingOptions]::InsertLineBreaks) + "`n-----END CERTIFICATE-----" }
        $env:PGSSLROOTCERT = Join-Path $run 'trusted-roots.pem'
        $pem | Set-Content -LiteralPath $env:PGSSLROOTCERT -Encoding ascii
    }
    if ($builder.ContainsKey('Channel Binding')) { $env:PGCHANNELBINDING = ([string]$builder['Channel Binding']).ToLowerInvariant() }
    if (!$env:PGPORT) { $env:PGPORT = '5432' }
    $env:PGCONNECT_TIMEOUT = '15'
    $env:PGAPPNAME = 'UniNet-G0-readonly-inventory'
    $env:PGOPTIONS = '-c default_transaction_read_only=on'
    $inventory = Join-Path $run 'inventory.jsonl'
    $sqlPath = Join-Path $PSScriptRoot 'inventory.sql'
    & (Join-Path $PostgresBin 'psql.exe') -X -w -q -A -t --set=ON_ERROR_STOP=1 "--file=$sqlPath" "--output=$inventory" 2> (Join-Path $run 'inventory.stderr.log')
    if ($LASTEXITCODE -ne 0) { throw "Inventory failed; private diagnostics: $run" }
    $items = @(Get-Content -LiteralPath $inventory | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
    $tables = @($items | Where-Object section -like 'rows:*' | ForEach-Object { [pscustomobject]@{ Table=$_.section.Substring(5); Rows=$_.count } })
    $migrations = @($items | Where-Object section -eq 'migrations' | ForEach-Object { $_.data } | ForEach-Object { $_.MigrationId })
    $summary = [ordered]@{ Environment=$EnvironmentName; CapturedAtUtc=[DateTime]::UtcNow.ToString('o'); AppliedMigrations=$migrations; Tables=$tables; OrphanChecksFailed=@($items | Where-Object { $_.section -like 'fk:*' -and $_.orphans -gt 0 }).Count; ArchiveCreated=$false; RestoreVerified=$false; CleanupAllowed=$false }
    if ($Archive) {
        # Full database, including schema, revisions, actions, referenced users and FK dependencies.
        $dumpPath = Join-Path $run 'database.dump'
        & (Join-Path $PostgresBin 'pg_dump.exe') -w --format=custom "--file=$dumpPath" 2> (Join-Path $run 'dump.stderr.log')
        if ($LASTEXITCODE -ne 0) { throw "Archive failed; private diagnostics: $run" }
        & (Join-Path $PostgresBin 'pg_restore.exe') --list (Join-Path $run 'database.dump') > (Join-Path $run 'archive-toc.txt')
        if ($LASTEXITCODE -ne 0) { throw 'Archive cannot be listed.' }
        $summary.ArchiveCreated = $true
        # Listing is NOT proof of restoration; a separate isolated restore and comparison is mandatory.
    }
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $run 'summary.json') -Encoding utf8
    Get-ChildItem -LiteralPath $run -File | Where-Object Name -ne 'sha256.json' | ForEach-Object { [pscustomobject]@{File=$_.Name;Bytes=$_.Length;SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'sha256.json') -Encoding utf8
    Write-Output "Private evidence directory: $run"
    $summary | ConvertTo-Json -Depth 8
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name,$saved[$name]) }
}
