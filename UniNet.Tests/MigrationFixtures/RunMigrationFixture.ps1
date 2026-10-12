param(
    [Parameter(Mandatory = $true)][string]$Fixture,
    [string]$PsqlPath = 'psql'
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
function Expand-Fixture([string]$Path) {
    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    if (!$resolvedPath.StartsWith($repositoryRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Fixture includes must remain inside the repository.'
    }
    foreach ($line in [System.IO.File]::ReadAllLines($resolvedPath)) {
        if ($line -match '^\s*\\ir\s+(.+?)\s*$') {
            Expand-Fixture (Join-Path ([System.IO.Path]::GetDirectoryName($resolvedPath)) $Matches[1])
        } elseif ($line -notmatch '^\s*(START TRANSACTION|BEGIN|COMMIT);\s*$') {
            $line
        }
    }
}
# Keep the pooler's server connection pinned and roll back all temporary test state.
$expandedSql = @('BEGIN;', 'SET LOCAL search_path = pg_temp, public;') +
    @(Expand-Fixture (Join-Path $PSScriptRoot $Fixture)) + @('ROLLBACK;')
$temporarySql = Join-Path ([System.IO.Path]::GetTempPath()) ('uninet-migration-' + [Guid]::NewGuid().ToString('N') + '.sql')
try {
    [System.IO.File]::WriteAllLines($temporarySql, $expandedSql)
    & $PsqlPath -X -w -v ON_ERROR_STOP=1 -f $temporarySql
    if ($LASTEXITCODE -ne 0) { throw 'Migration fixture failed; its transaction was rolled back.' }
} finally {
    if (Test-Path -LiteralPath $temporarySql) { Remove-Item -LiteralPath $temporarySql }
}
