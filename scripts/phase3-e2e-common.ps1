Set-StrictMode -Version Latest

function Read-Phase3E2EEnv {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw '.env.e2e was not found.'
    }

    $values = New-Object System.Collections.Hashtable ([System.StringComparer]::OrdinalIgnoreCase)
    $lineNumber = 0
    foreach ($rawLine in [System.IO.File]::ReadAllLines($Path)) {
        $lineNumber++
        $commentCheck = $rawLine.TrimStart()
        if ([string]::IsNullOrWhiteSpace($rawLine) -or $commentCheck.StartsWith('#')) { continue }

        $separator = $rawLine.IndexOf('=')
        if ($separator -lt 1) { throw ".env.e2e has a malformed line at line $lineNumber." }

        $name = $rawLine.Substring(0, $separator).Trim()
        if ($name -notmatch '^[A-Za-z_][A-Za-z0-9_-]*$') {
            throw ".env.e2e has an invalid variable name at line $lineNumber."
        }
        if ($values.ContainsKey($name)) { throw ".env.e2e contains a duplicate variable: $name." }

        # Split once only: connection strings and passwords may contain additional '=' characters.
        $value = $rawLine.Substring($separator + 1)
        if ($value.Length -ge 2 -and
            (($value[0] -eq '"' -and $value[$value.Length - 1] -eq '"') -or
             ($value[0] -eq "'" -and $value[$value.Length - 1] -eq "'"))) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        $values[$name] = $value
    }

    return $values
}

function Test-Phase3E2EParser {
    $samplePath = [System.IO.Path]::GetTempFileName()
    try {
        [System.IO.File]::WriteAllLines($samplePath, @(
            '# comment',
            '',
            'PLAIN=left=right; keep spaces',
            'DOUBLE="  outer spaces;inner=value  "',
            "SINGLE='single=value;retained'",
            'HASH=keep#inside'
        ))
        $parsed = Read-Phase3E2EEnv -Path $samplePath
        if ($parsed['PLAIN'] -cne 'left=right; keep spaces' -or
            $parsed['DOUBLE'] -cne '  outer spaces;inner=value  ' -or
            $parsed['SINGLE'] -cne 'single=value;retained' -or
            $parsed['HASH'] -cne 'keep#inside') { return $false }

        $connection = ConvertFrom-Phase3E2EConnectionString -ConnectionString 'Host=127.0.0.1;Port=55432;Database=uninet_e2e;Username=uninet_test;Password="left=right;with spaces";SSL Mode=Disable;'
        if ($connection['Password'] -cne 'left=right;with spaces' -or
            -not (Test-Phase3E2ELocalDatabaseTarget $connection)) { return $false }
        $shared = ConvertFrom-Phase3E2EConnectionString -ConnectionString 'Host=example.neon.tech;Port=5432;Database=uninet_e2e;Username=uninet_test;Password=not-a-real-secret;'
        if (Test-Phase3E2ELocalDatabaseTarget $shared) { return $false }

        [System.IO.File]::WriteAllLines($samplePath, @('DUP=first', 'dup=second'))
        $duplicateRejected = $false
        try { $null = Read-Phase3E2EEnv -Path $samplePath } catch { $duplicateRejected = $true }
        if (-not $duplicateRejected) { return $false }

        [System.IO.File]::WriteAllLines($samplePath, @('malformed-line'))
        $malformedRejected = $false
        try { $null = Read-Phase3E2EEnv -Path $samplePath } catch { $malformedRejected = $true }
        return $malformedRejected
    }
    finally {
        Remove-Item -LiteralPath $samplePath -ErrorAction SilentlyContinue
    }
}

function ConvertFrom-Phase3E2EConnectionString {
    param([Parameter(Mandatory)][string]$ConnectionString)

    $parts = New-Object System.Collections.Hashtable ([System.StringComparer]::OrdinalIgnoreCase)
    $index = 0
    while ($index -lt $ConnectionString.Length) {
        while ($index -lt $ConnectionString.Length -and
            ([char]::IsWhiteSpace($ConnectionString[$index]) -or $ConnectionString[$index] -eq ';')) { $index++ }
        if ($index -ge $ConnectionString.Length) { break }

        $keyStart = $index
        while ($index -lt $ConnectionString.Length -and $ConnectionString[$index] -ne '=' -and $ConnectionString[$index] -ne ';') { $index++ }
        if ($index -ge $ConnectionString.Length -or $ConnectionString[$index] -ne '=') {
            throw 'Malformed local PostgreSQL connection string.'
        }
        $key = $ConnectionString.Substring($keyStart, $index - $keyStart).Trim()
        if ([string]::IsNullOrWhiteSpace($key)) { throw 'Malformed local PostgreSQL connection string.' }
        if ($parts.ContainsKey($key)) { throw 'Duplicate PostgreSQL connection-string key.' }
        $index++
        while ($index -lt $ConnectionString.Length -and [char]::IsWhiteSpace($ConnectionString[$index])) { $index++ }

        $value = ''
        if ($index -lt $ConnectionString.Length -and
            ($ConnectionString[$index] -eq '"' -or $ConnectionString[$index] -eq "'")) {
            $quote = $ConnectionString[$index]
            $index++
            $builder = New-Object System.Text.StringBuilder
            $closed = $false
            while ($index -lt $ConnectionString.Length) {
                $character = $ConnectionString[$index]
                if ($character -eq $quote) {
                    if ($index + 1 -lt $ConnectionString.Length -and $ConnectionString[$index + 1] -eq $quote) {
                        [void]$builder.Append($quote)
                        $index += 2
                        continue
                    }
                    $index++
                    $closed = $true
                    break
                }
                [void]$builder.Append($character)
                $index++
            }
            if (-not $closed) { throw 'Unclosed quoted value in PostgreSQL connection string.' }
            $value = $builder.ToString()
            while ($index -lt $ConnectionString.Length -and [char]::IsWhiteSpace($ConnectionString[$index])) { $index++ }
            if ($index -lt $ConnectionString.Length -and $ConnectionString[$index] -ne ';') {
                throw 'Malformed quoted value in PostgreSQL connection string.'
            }
        }
        else {
            $valueStart = $index
            while ($index -lt $ConnectionString.Length -and $ConnectionString[$index] -ne ';') { $index++ }
            $value = $ConnectionString.Substring($valueStart, $index - $valueStart).Trim()
        }

        $parts[$key] = $value
        if ($index -lt $ConnectionString.Length -and $ConnectionString[$index] -eq ';') { $index++ }
    }
    return $parts
}

function Test-Phase3E2ELocalDatabaseTarget {
    param([Parameter(Mandatory)][hashtable]$ConnectionParts)
    return ([string]$ConnectionParts['Host'] -ceq '127.0.0.1' -and
        [string]$ConnectionParts['Port'] -ceq '55432' -and
        [string]$ConnectionParts['Database'] -ceq 'uninet_e2e' -and
        [string]$ConnectionParts['Username'] -ceq 'uninet_test')
}

function Add-Phase3E2ECheck {
    param([System.Collections.Generic.List[object]]$Checks, [string]$Status, [string]$Message)
    [void]$Checks.Add([pscustomobject]@{ Status = $Status; Message = $Message })
}

function Test-Phase3E2ETcpPort {
    param([string]$HostName, [int]$Port)
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $asyncResult = $client.BeginConnect($HostName, $Port, $null, $null)
        if (-not $asyncResult.AsyncWaitHandle.WaitOne(1500)) { return $false }
        $client.EndConnect($asyncResult)
        return $true
    }
    catch { return $false }
    finally { $client.Close() }
}

function Test-Phase3E2EIgnoredPath {
    param([string]$RepoRoot, [string]$RelativePath, [switch]$MustBeIgnored)
    $git = Get-Command git -ErrorAction SilentlyContinue
    if (-not $git) { return $false }
    & $git.Source -C $RepoRoot check-ignore --no-index --quiet -- $RelativePath 2>$null
    $ignored = ($LASTEXITCODE -eq 0)
    if ($MustBeIgnored) { return $ignored }
    return (-not $ignored)
}

function Test-Phase3E2EUrls {
    param([string]$Callback, [string]$ReturnTarget, [string]$Webhook)

    $callbackUri = $null; $returnUri = $null; $webhookUri = $null
    foreach ($entry in @(@($Callback, [ref]$callbackUri), @($ReturnTarget, [ref]$returnUri), @($Webhook, [ref]$webhookUri))) {
        if (-not [Uri]::TryCreate($entry[0], [UriKind]::Absolute, $entry[1])) { return $false }
    }
    foreach ($uri in @($callbackUri, $returnUri, $webhookUri)) {
        if ($uri.UserInfo -or $uri.Query -or $uri.Fragment) { return $false }
    }
    if ($webhookUri.Scheme -cne 'https' -or -not $webhookUri.IsDefaultPort -or
        $webhookUri.IdnHost -notmatch '^(?i)[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.ngrok(?:-free)?\.(?:app|io|dev)$' -or
        $webhookUri.AbsolutePath -cne '/api/webhooks/github') { return $false }
    if ($callbackUri.AbsolutePath -cne '/api/github/oauth/callback' -or
        $returnUri.AbsolutePath -cne '/swagger/index.html') { return $false }
    $publicOrigin = $webhookUri.GetLeftPart([UriPartial]::Authority)
    $localOrigin = 'http://localhost:5239'
    $callbackOrigin = $callbackUri.GetLeftPart([UriPartial]::Authority)
    $returnOrigin = $returnUri.GetLeftPart([UriPartial]::Authority)
    return (($callbackOrigin -ceq $localOrigin -and $returnOrigin -ceq $localOrigin) -or
        ($callbackOrigin -ceq $publicOrigin -and $returnOrigin -ceq $publicOrigin))
}

function Test-Phase3E2EConfiguration {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepoRoot, [Parameter(Mandatory)][hashtable]$Values)

    $checks = New-Object 'System.Collections.Generic.List[object]'
    $required = @(
        'ASPNETCORE_ENVIRONMENT', 'ASPNETCORE_URLS', 'UNINET_SKIP_LOCAL_ENV_FILE',
        'ConnectionStrings__UniNet', 'Jwt__Key', 'Jwt__Issuer', 'Jwt__Audience',
        'GitHub__ClientId', 'GitHub__ClientSecret', 'GitHub__CallbackUrl', 'GitHub__KeyRingPath',
        'GitHub__ReturnTargets__phase3-e2e', 'GitHubWebhook__Secret',
        'GitHubE2E__WebhookPublicUrl', 'GitHubE2E__DisableUnrelatedWorkers', 'VerificationWorker__Enabled',
        'GitHubSync__Enabled', 'GitHubSync__DefaultIntervalHours', 'GitHubSync__WorkerConcurrency',
        'GitHubSync__BatchSize', 'GitHubSync__RetryBaseDelaySeconds', 'GitHubSync__MaxRetryAttempts',
        'GitHubSync__JobLeaseDurationSeconds', 'GitHubSync__PollSeconds', 'Cors__Origins__0'
    )
    $missing = @($required | Where-Object { -not $Values.ContainsKey($_) })
    if ($missing.Count -gt 0) {
        Add-Phase3E2ECheck $checks 'FAIL' ("Missing required keys: " + ($missing -join ', '))
    }
    else { Add-Phase3E2ECheck $checks 'PASS' 'Required E2E keys are present.' }

    if ($missing.Count -gt 0) { return $checks.ToArray() }

    $placeholders = @($required | Where-Object {
        $Values[$_] -match '(?i)__FILL_ME|__EXACT|__REPLACE|<[^>]+>'
    })
    if ($placeholders.Count -gt 0) {
        Add-Phase3E2ECheck $checks 'FAIL' ("Unfilled placeholders: " + ($placeholders -join ', '))
    }
    else { Add-Phase3E2ECheck $checks 'PASS' 'Required values contain no fill-in placeholders.' }

    if (-not (Test-Phase3E2EParser)) {
        Add-Phase3E2ECheck $checks 'FAIL' 'The .env parser self-check failed.'
    }
    else { Add-Phase3E2ECheck $checks 'PASS' 'The .env parser preserves equals, semicolons, spaces, and quote pairs; it rejects duplicate and malformed keys.' }

    if ($Values['ASPNETCORE_ENVIRONMENT'] -cne 'E2E') {
        Add-Phase3E2ECheck $checks 'FAIL' 'ASPNETCORE_ENVIRONMENT must be E2E to avoid loading Development User Secrets.'
    }
    else { Add-Phase3E2ECheck $checks 'PASS' 'The E2E environment is isolated from Development User Secrets.' }

    if ($Values['ASPNETCORE_URLS'] -cne 'http://localhost:5239') {
        Add-Phase3E2ECheck $checks 'FAIL' 'ASPNETCORE_URLS must be http://localhost:5239.'
    }
    else { Add-Phase3E2ECheck $checks 'PASS' 'Backend URL matches the E2E localhost port.' }
    if (Test-Phase3E2ETcpPort '127.0.0.1' 5239) {
        Add-Phase3E2ECheck $checks 'FAIL' 'Backend port 5239 is already occupied; stop the existing local backend before E2E startup.'
    }
    else { Add-Phase3E2ECheck $checks 'PASS' 'Backend port 5239 is available.' }

    if ($Values['UNINET_SKIP_LOCAL_ENV_FILE'] -notmatch '^(?i:true)$') {
        Add-Phase3E2ECheck $checks 'FAIL' 'UNINET_SKIP_LOCAL_ENV_FILE must be true to prevent loading repository .env values.'
    }
    else { Add-Phase3E2ECheck $checks 'PASS' 'Repository .env loading is disabled for this E2E process.' }

    $connectionStringValid = $true
    try { $connectionBuilder = ConvertFrom-Phase3E2EConnectionString -ConnectionString $Values['ConnectionStrings__UniNet'] }
    catch { $connectionStringValid = $false }
    if (-not $connectionStringValid) {
        Add-Phase3E2ECheck $checks 'FAIL' 'ConnectionStrings__UniNet is malformed.'
    }
    else {
        $requiredDbParts = @('Host', 'Port', 'Database', 'Username', 'Password')
        $hasParts = @($requiredDbParts | Where-Object { -not $connectionBuilder.ContainsKey($_) }).Count -eq 0
        if (-not $hasParts) {
            Add-Phase3E2ECheck $checks 'FAIL' 'ConnectionStrings__UniNet must include Host, Port, Database, Username, and Password.'
        }
        elseif (-not (Test-Phase3E2ELocalDatabaseTarget $connectionBuilder)) {
            Add-Phase3E2ECheck $checks 'FAIL' 'Database target must be exactly 127.0.0.1:55432/uninet_e2e as uninet_test; shared/Neon hosts are refused.'
        }
        elseif ([string]::IsNullOrWhiteSpace([string]$connectionBuilder['Password'])) {
            Add-Phase3E2ECheck $checks 'FAIL' 'The local PostgreSQL password is empty.'
        }
        else { Add-Phase3E2ECheck $checks 'PASS' 'Connection string targets only the allowlisted local E2E database; credentials were not displayed.' }
    }

    $jwtKeyBytes = [System.Text.Encoding]::UTF8.GetByteCount($Values['Jwt__Key'])
    if ($jwtKeyBytes -lt 32) { Add-Phase3E2ECheck $checks 'FAIL' 'Jwt__Key must be at least 32 UTF-8 bytes.' }
    else { Add-Phase3E2ECheck $checks 'PASS' 'JWT signing key length meets the backend minimum.' }

    if (-not (Test-Phase3E2EUrls $Values['GitHub__CallbackUrl'] $Values['GitHub__ReturnTargets__phase3-e2e'] $Values['GitHubE2E__WebhookPublicUrl'])) {
        Add-Phase3E2ECheck $checks 'FAIL' 'OAuth callback and return target must use localhost:5239 together or the exact configured HTTPS ngrok webhook origin, with correct paths and no credentials, query or fragment.'
    }
    else { Add-Phase3E2ECheck $checks 'PASS' 'OAuth callback and return target use an allowed origin and the webhook uses the configured HTTPS ngrok origin.' }

    if ($Values['GitHub__KeyRingPath'] -cne '.phase3-e2e-data-protection') {
        Add-Phase3E2ECheck $checks 'FAIL' 'GitHub__KeyRingPath must use the ignored local E2E Data Protection directory.'
    }
    else {
        $apiRoot = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot 'UniNet.API'))
        $keyRingPath = [System.IO.Path]::GetFullPath((Join-Path $apiRoot $Values['GitHub__KeyRingPath']))
        if (-not $keyRingPath.StartsWith($apiRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase)) {
            Add-Phase3E2ECheck $checks 'FAIL' 'Data Protection key path must stay under UniNet.API.'
        }
        elseif (-not (Test-Phase3E2EIgnoredPath $RepoRoot 'UniNet.API/.phase3-e2e-data-protection/.probe' -MustBeIgnored)) {
            Add-Phase3E2ECheck $checks 'FAIL' 'The E2E Data Protection directory is not Git-ignored.'
        }
        else { Add-Phase3E2ECheck $checks 'PASS' 'Persistent Data Protection path is local and Git-ignored; the launcher creates it.' }
    }

    if ($Values['GitHubSync__Enabled'] -notmatch '^(?i:true)$') {
        Add-Phase3E2ECheck $checks 'FAIL' 'GitHubSync__Enabled must be true for Phase 3 E2E after PostgreSQL/OAuth prechecks.'
    }
    else { Add-Phase3E2ECheck $checks 'PASS' 'Git Sync is explicitly enabled for the E2E process only.' }

    if ($Values['GitHubE2E__DisableUnrelatedWorkers'] -notmatch '^(?i:true)$' -or
        $Values['VerificationWorker__Enabled'] -notmatch '^(?i:false)$') {
        Add-Phase3E2ECheck $checks 'FAIL' 'Unrelated workers must be disabled and VerificationWorker__Enabled must be false.'
    }
    else { Add-Phase3E2ECheck $checks 'PASS' 'Payment, notification, token and portrait cleanup workers are disabled for E2E.' }

    $bounds = @(
        @{ Key = 'GitHubSync__DefaultIntervalHours'; Minimum = 1; Maximum = 720 },
        @{ Key = 'GitHubSync__WorkerConcurrency'; Minimum = 1; Maximum = 16 },
        @{ Key = 'GitHubSync__BatchSize'; Minimum = 1; Maximum = 100 },
        @{ Key = 'GitHubSync__RetryBaseDelaySeconds'; Minimum = 1; Maximum = 1800 },
        @{ Key = 'GitHubSync__MaxRetryAttempts'; Minimum = 1; Maximum = 100 },
        @{ Key = 'GitHubSync__JobLeaseDurationSeconds'; Minimum = 30; Maximum = 3600 },
        @{ Key = 'GitHubSync__PollSeconds'; Minimum = 1; Maximum = 300 }
    )
    $invalidOptions = @()
    foreach ($bound in $bounds) {
        $number = 0
        if (-not [int]::TryParse($Values[$bound.Key], [ref]$number) -or
            $number -lt $bound.Minimum -or $number -gt $bound.Maximum) { $invalidOptions += $bound.Key }
    }
    if ($invalidOptions.Count -gt 0) { Add-Phase3E2ECheck $checks 'FAIL' ("Invalid Git Sync option values: " + ($invalidOptions -join ', ')) }
    else { Add-Phase3E2ECheck $checks 'PASS' 'Git Sync option values are inside supported bounds.' }

    if ($Values['Cors__Origins__0'] -cne 'http://localhost:8081') {
        Add-Phase3E2ECheck $checks 'FAIL' 'Cors__Origins__0 must be http://localhost:8081 for the local test client.'
    }
    else { Add-Phase3E2ECheck $checks 'PASS' 'CORS is restricted to the local E2E client origin.' }

    if (-not (Test-Phase3E2EIgnoredPath $RepoRoot '.env.e2e' -MustBeIgnored) -or
        -not (Test-Phase3E2EIgnoredPath $RepoRoot '.env.e2e.local' -MustBeIgnored)) {
        Add-Phase3E2ECheck $checks 'FAIL' 'Secret .env.e2e files are not Git-ignored.'
    }
    else { Add-Phase3E2ECheck $checks 'PASS' '.env.e2e and .env.e2e.local are Git-ignored.' }

    if (Test-Phase3E2EIgnoredPath $RepoRoot '.env.e2e.example' -MustBeIgnored) {
        Add-Phase3E2ECheck $checks 'FAIL' '.env.e2e.example must remain visible to Git.'
    }
    else { Add-Phase3E2ECheck $checks 'PASS' '.env.e2e.example is not ignored.' }

    if (-not (Test-Phase3E2ETcpPort '127.0.0.1' 55432)) {
        Add-Phase3E2ECheck $checks 'FAIL' 'Local PostgreSQL TCP port 55432 is not reachable.'
    }
    else { Add-Phase3E2ECheck $checks 'PASS' 'Local PostgreSQL TCP port 55432 is reachable.' }

    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if (-not $docker) {
        Add-Phase3E2ECheck $checks 'WARNING' 'Docker CLI is unavailable; rely on the direct local PostgreSQL TCP check.'
    }
    else {
        $dockerContainers = @(& $docker.Source ps --filter 'publish=55432' --format '{{.Names}}' 2>$null)
        if ($LASTEXITCODE -ne 0) {
            Add-Phase3E2ECheck $checks 'WARNING' 'Docker daemon is unavailable; rely on the direct local PostgreSQL TCP check.'
        }
        elseif ($dockerContainers.Count -gt 0) {
            Add-Phase3E2ECheck $checks 'PASS' 'A running Docker container publishes local PostgreSQL port 55432.'
        }
        else { Add-Phase3E2ECheck $checks 'WARNING' 'No Docker container publishes port 55432; the target may be a local PostgreSQL service.' }
    }

    return $checks.ToArray()
}
