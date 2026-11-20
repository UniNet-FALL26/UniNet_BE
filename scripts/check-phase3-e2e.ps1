$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'phase3-e2e-common.ps1')

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
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
    Write-Output '[FAIL] E2E environment is not ready. No database connection or GitHub request was made.'
    exit 1
}

Write-Output '[PASS] E2E configuration checks passed. This checker made no database or GitHub requests.'
exit 0
