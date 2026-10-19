[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [int]$Port = 55439
)
$ErrorActionPreference = 'Stop'
$evidence = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
$dump = Join-Path $evidence 'database.dump'
if (!(Test-Path -LiteralPath $dump)) { throw 'No database archive.' }
$manifest = Get-Content (Join-Path $evidence 'sha256.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest) { if ((Get-FileHash (Join-Path $evidence $entry.File) -Algorithm SHA256).Hash -ne $entry.SHA256) { throw "Checksum mismatch: $($entry.File)" } }
$cluster = Join-Path $evidence ('restore-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $cluster | Out-Null
$password = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$pwfile = Join-Path $cluster 'init-password.txt'
$password | Set-Content -LiteralPath $pwfile -Encoding ascii
$saved = @{}
foreach ($name in @('PGHOST','PGPORT','PGDATABASE','PGUSER','PGPASSWORD','PGSSLMODE','PGOPTIONS','ConnectionStrings__UniNet')) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
$started = $false
try {
    & (Join-Path $PostgresBin 'initdb.exe') -D (Join-Path $cluster 'data') -U g0_restore --auth=scram-sha-256 "--pwfile=$pwfile" --encoding=UTF8 --locale=C > (Join-Path $cluster 'initdb.log') 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Isolated cluster initialization failed.' }
    Remove-Item -LiteralPath $pwfile
    $ctlArgs = @('-D', ('"' + (Join-Path $cluster 'data') + '"'), '-l', ('"' + (Join-Path $cluster 'server.log') + '"'), '-o', ('"-h 127.0.0.1 -p ' + $Port + '"'), '-w', 'start')
    $process = Start-Process -FilePath (Join-Path $PostgresBin 'pg_ctl.exe') -ArgumentList $ctlArgs -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw 'Isolated PostgreSQL could not start (port may be occupied).' }
    $started = $true
    $env:PGHOST='127.0.0.1'; $env:PGPORT=[string]$Port; $env:PGUSER='g0_restore'; $env:PGPASSWORD=$password; $env:PGDATABASE='postgres'; $env:PGSSLMODE='disable'; $env:PGOPTIONS=''
    & (Join-Path $PostgresBin 'createdb.exe') -w g0_restore > (Join-Path $cluster 'createdb.log') 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Isolated database creation failed.' }
    $env:PGDATABASE='g0_restore'
    & (Join-Path $PostgresBin 'pg_restore.exe') -w --exit-on-error --no-owner --no-privileges --dbname=g0_restore $dump > (Join-Path $cluster 'restore.log') 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Archive restore failed. Cleanup remains forbidden.' }
    $env:ConnectionStrings__UniNet="Host=127.0.0.1;Port=$Port;Database=g0_restore;Username=g0_restore;Password=$password;SSL Mode=Disable"
    & (Join-Path $PSScriptRoot 'Invoke-Inventory.ps1') -EnvironmentName isolated-restore -OutputRoot $cluster -PostgresBin $PostgresBin | Out-Null
    $restored = Get-ChildItem -LiteralPath $cluster -Directory -Filter 'isolated-restore-*' | Select-Object -First 1
    $before = @(Get-Content (Join-Path $evidence 'inventory.jsonl') | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
    $after = @(Get-Content (Join-Path $restored.FullName 'inventory.jsonl') | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
    $differences = @()
    $normalizations = @('Column database catalog names differ for the isolated database.', 'Column ordinals/dtd identifiers are renumbered densely after pg_dump omits dropped-column slots; relative column order is retained.')
    foreach ($set in @($before,$after)) {
        $tableKey = ''; $ordinal = 0
        foreach ($column in ($set | Where-Object section -eq 'columns').data) {
            $key = $column.table_schema + '.' + $column.table_name
            if ($key -cne $tableKey) { $tableKey = $key; $ordinal = 0 }
            $ordinal++
            $column.ordinal_position = $ordinal
            $column.dtd_identifier = [string]$ordinal
            foreach ($field in @('table_catalog','udt_catalog','domain_catalog')) {
                if ($null -ne $column.$field) { $column.$field = '<isolated-database>' }
            }
        }
    }
    # pg_dump/restore across server versions changes parenthesization of this AND-only numeric check.
    # Allow only the reviewed constraint with identical tokens; preserve exact raw evidence on disk.
    $oldCheck = @(($before | Where-Object section -eq 'constraints').data | Where-Object { $_.schema -eq 'public' -and $_.table -eq 'ContributionResults' -and $_.name -eq 'CK_ContributionResults_Scores' })
    $newCheck = @(($after | Where-Object section -eq 'constraints').data | Where-Object { $_.schema -eq 'public' -and $_.table -eq 'ContributionResults' -and $_.name -eq 'CK_ContributionResults_Scores' })
    if ($oldCheck.Count -eq 1 -and $newCheck.Count -eq 1 -and $oldCheck[0].definition -cne $newCheck[0].definition) {
        $oldTokens = $oldCheck[0].definition -replace '[()\s]', ''
        $newTokens = $newCheck[0].definition -replace '[()\s]', ''
        if ($oldTokens -ceq $newTokens -and $oldCheck[0].definition -notmatch '\b(OR|NOT)\b|[+*/]') {
            $newCheck[0].definition = $oldCheck[0].definition
            $normalizations += 'Reviewed CK_ContributionResults_Scores: identical AND-only comparison tokens; server deparser parentheses differ.'
        }
    }
    foreach ($item in $before | Where-Object section -ne 'database') {
        $match = @($after | Where-Object section -eq $item.section)
        if ($match.Count -ne 1 -or ($item | ConvertTo-Json -Depth 100 -Compress) -cne ($match[0] | ConvertTo-Json -Depth 100 -Compress)) { $differences += $item.section }
    }
    if ($before.Count -ne $after.Count) { $differences += 'section-count' }
    $result = [ordered]@{VerifiedAtUtc=[DateTime]::UtcNow.ToString('o');RestoreSucceeded=$true;ComparedSections=$before.Count-1;Normalizations=$normalizations;Differences=$differences;RestoreVerified=($differences.Count -eq 0);SourceWritersStopped=$false;CleanupAllowed=$false;Note='Source inventory and dump use separate snapshots; equality is checked against the restored archive. Repeat under a writer freeze for cutover. Ownership/grants are retained in archive but not applied in the isolated restore.'}
    $result | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $evidence 'restore-verification.json') -Encoding utf8
    $summaryPath = Join-Path $evidence 'summary.json'
    $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
    $summary.RestoreVerified = $result.RestoreVerified
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding utf8
    Get-ChildItem -LiteralPath $evidence -File | Where-Object Name -ne 'sha256.json' | ForEach-Object { [pscustomobject]@{File=$_.Name;Bytes=$_.Length;SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'sha256.json') -Encoding utf8
    $result | ConvertTo-Json -Depth 5
    if ($differences.Count) { throw 'Archive differs from source inventory; do not proceed with cleanup.' }
} finally {
    if ($started) {
        & (Join-Path $PostgresBin 'pg_ctl.exe') -D (Join-Path $cluster 'data') -m fast -w stop > (Join-Path $cluster 'stop.log') 2>&1
        if ($LASTEXITCODE -ne 0) { Write-Warning 'Isolated cluster did not stop; inspect stop.log.' }
    }
    if (Test-Path -LiteralPath $pwfile) { Remove-Item -LiteralPath $pwfile }
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name,$saved[$name]) }
}
