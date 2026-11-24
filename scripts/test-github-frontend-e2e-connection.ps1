$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'phase3-e2e-common.ps1')
# No database/network and no real credentials. Verify serialization rather than a receipt label.
$source='Host=127.0.0.1;Port=55433;Database=uninet_github_app_e2e;Username=uninet_test;Password="fixture=with;separator";SSL Mode=Disable'
$builder=New-Object System.Data.Common.DbConnectionStringBuilder
$builder.set_ConnectionString($source)
$builder['Database']='uninet_github_fe_e2e_20261121'
$serialized=$builder.get_ConnectionString()
$target=ConvertFrom-Phase3E2EConnectionString $serialized
if ($target['Database'] -cne 'uninet_github_fe_e2e_20261121' -or $target['Host'] -cne '127.0.0.1' -or
    $target['Port'] -ne '55433' -or $target['Username'] -cne 'uninet_test' -or $target['Password'] -cne 'fixture=with;separator') {
    throw 'Child connection string serialization regression.'
}
Write-Output 'PASS: effective serialized connection targets FE disposable and preserves quoted values.'
