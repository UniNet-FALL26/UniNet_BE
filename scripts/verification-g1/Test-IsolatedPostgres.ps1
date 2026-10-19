[CmdletBinding()]
param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [int]$Port = 55449,
    [string]$Filter = 'FullyQualifiedName~Postgres',
    [string]$ArchiveDirectory
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$run = Join-Path $repo ('artifacts/verification-g1/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$password = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$pwfile = Join-Path $run 'init-password.txt'
$password | Set-Content -LiteralPath $pwfile -Encoding ascii
$saved = @{}
foreach ($name in @('PGHOST','PGPORT','PGDATABASE','PGUSER','PGPASSWORD','PGSSLMODE','PGCHANNELBINDING','PGCONNECT_TIMEOUT','PGOPTIONS','UNINET_TEST_POSTGRES','ConnectionStrings__UniNet')) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
$started = $false
try {
    & (Join-Path $PostgresBin 'initdb.exe') -D (Join-Path $run 'data') -U g1_test --auth=scram-sha-256 "--pwfile=$pwfile" --encoding=UTF8 --locale=C > (Join-Path $run 'initdb.log') 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Isolated cluster initialization failed.' }
    Remove-Item -LiteralPath $pwfile
    $ctlArgs = @('-D', ('"' + (Join-Path $run 'data') + '"'), '-l', ('"' + (Join-Path $run 'server.log') + '"'), '-o', ('"-h 127.0.0.1 -p ' + $Port + '"'), '-w', 'start')
    $process = Start-Process -FilePath (Join-Path $PostgresBin 'pg_ctl.exe') -ArgumentList $ctlArgs -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw 'Isolated PostgreSQL failed to start.' }
    $started = $true
    $env:PGHOST='127.0.0.1'; $env:PGPORT=[string]$Port; $env:PGUSER='g1_test'; $env:PGPASSWORD=$password; $env:PGDATABASE='postgres'; $env:PGSSLMODE='disable'; $env:PGCHANNELBINDING='prefer'; $env:PGOPTIONS=''
    $env:PGCONNECT_TIMEOUT='15'
    & (Join-Path $PostgresBin 'createdb.exe') -w g1_test
    if ($LASTEXITCODE -ne 0) { throw 'Test database creation failed.' }
    $env:UNINET_TEST_POSTGRES="Host=127.0.0.1;Port=$Port;Database=g1_test;Username=g1_test;Password=$password;SSL Mode=Disable"
    & dotnet test (Join-Path $repo 'UniNet.Tests/UniNet.Tests.csproj') --no-build --no-restore --filter $Filter --logger "trx;LogFileName=g1-postgres.trx" --results-directory $run
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL tests failed; evidence: $run" }
    if ($ArchiveDirectory) {
        # A clone only. Never read source .env or apply migrations to the source database.
        $archive = (Resolve-Path -LiteralPath $ArchiveDirectory).Path
        $manifest = Get-Content (Join-Path $archive 'sha256.json') -Raw | ConvertFrom-Json
        $entry = $manifest | Where-Object File -eq database.dump
        if (!$entry -or (Get-FileHash (Join-Path $archive 'database.dump')).Hash -ne $entry.SHA256) { throw 'Archive checksum mismatch.' }
        & (Join-Path $PostgresBin 'createdb.exe') -w g1_clone
        if ($LASTEXITCODE -ne 0) { throw 'Clone database creation failed.' }
        & (Join-Path $PostgresBin 'pg_restore.exe') -w --exit-on-error --no-owner --no-privileges --dbname=g1_clone (Join-Path $archive 'database.dump') > (Join-Path $run 'clone-restore.log') 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'Clone restore failed.' }
        $env:ConnectionStrings__UniNet="Host=127.0.0.1;Port=$Port;Database=g1_clone;Username=g1_test;Password=$password;SSL Mode=Disable"
        & dotnet ef database update --no-build --project (Join-Path $repo 'UniNet.Infrastructure') --startup-project (Join-Path $repo 'UniNet.Infrastructure') > (Join-Path $run 'clone-migration.log') 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'G1 migration on restored clone failed.' }
        & (Join-Path $repo 'scripts/verification-g0/Invoke-Inventory.ps1') -EnvironmentName g1-clone -OutputRoot $run -PostgresBin $PostgresBin | Out-Null
        $baseline = @(Get-Content (Join-Path $archive 'inventory.jsonl') | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
        $columns = ($baseline | Where-Object section -eq columns).data
        $env:PGOPTIONS='-c timezone=UTC'
        $comparisons = @()
        foreach ($table in $baseline | Where-Object section -like 'rows:*') {
            $parts = $table.section.Substring(5).Split('.',2)
            if ($parts[1] -eq 'ProjectEvidenceModerations') {
                if ($table.count -ne 0) { throw 'Clone comparison requires a dedicated mapped comparison for nonempty evidence; use migration fixtures.' }
                continue
            }
            $projection = ($columns | Where-Object { $_.table_schema -ceq $parts[0] -and $_.table_name -ceq $parts[1] } | Sort-Object ordinal_position | ForEach-Object { '"' + $_.column_name.Replace('"','""') + '"' }) -join ','
            $qualified = '"' + $parts[0].Replace('"','""') + '"."' + $parts[1].Replace('"','""') + '"'
            $sql = "SELECT jsonb_build_object('count',count(*),'keys',coalesce(jsonb_agg(jsonb_build_object('id',to_jsonb(t)->'Id','row_md5',md5(to_jsonb(t)::text)) ORDER BY to_jsonb(t)->>'Id'),'[]'::jsonb)) FROM (SELECT $projection FROM $qualified) t;"
            $env:PGDATABASE='g1_clone'
            $json = & (Join-Path $PostgresBin 'psql.exe') -X -w -q -A -t --set=ON_ERROR_STOP=1 -c $sql
            if ($LASTEXITCODE -ne 0) { throw 'Clone projection comparison query failed.' }
            $actual = $json | ConvertFrom-Json
            $expectedKeys = $table.keys | ConvertTo-Json -Depth 8 -Compress
            $actualKeys = $actual.keys | ConvertTo-Json -Depth 8 -Compress
            $equal = $actual.count -eq $table.count -and $expectedKeys -ceq $actualKeys
            $comparisons += [pscustomobject]@{Table=$table.section.Substring(5);Rows=$actual.count;OriginalColumnsAndRowHashesMatch=$equal}
            if (!$equal) { throw "Clone data comparison failed: $($table.section)" }
        }
        $cloneInventory = Get-ChildItem -LiteralPath $run -Directory -Filter 'g1-clone-*' | Select-Object -First 1
        $cloneSummary = Get-Content (Join-Path $cloneInventory.FullName 'summary.json') -Raw | ConvertFrom-Json
        if ($cloneSummary.OrphanChecksFailed -ne 0) { throw 'Clone has FK orphans.' }
        $comparisons | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $run 'clone-comparison.json') -Encoding utf8
        Write-Output "G1 migration on restored G0 archive: PASS. Evidence: $run"
    }
    Write-Output "Isolated PostgreSQL verification: PASS. Evidence: $run"
} finally {
    if ($started) {
        & (Join-Path $PostgresBin 'pg_ctl.exe') -D (Join-Path $run 'data') -m fast -w stop > (Join-Path $run 'stop.log') 2>&1
        if ($LASTEXITCODE -ne 0) { Write-Warning 'Check isolated server stop.log.' }
    }
    if (Test-Path -LiteralPath $pwfile) { Remove-Item -LiteralPath $pwfile }
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name,$saved[$name]) }
}
