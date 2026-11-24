# Windows-only local fixture preparation. All business mutations use the authenticated application API.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'phase3-e2e-common.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$database = 'uninet_github_fe_e2e_20261121'
$container = 'uninet-step2-concurrency'
$api = 'http://localhost:5239/api'
$marker = 'UNINET_GITHUB_FE_SEED_V1'
$title = 'GITHUB_LINKING_TEST'
$description = "[$marker] Disposable FE fixture: real OAuth and GitHub App linking by the user. No synthetic Git evidence."
$connectedProjectId = '68f98a9f-d73d-4dcf-be5c-7099b29329c0'
$store = Join-Path $env:LOCALAPPDATA 'UniNetGitHubFrontendSeed'
$run = Join-Path $store ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
$lock = New-Object Threading.Mutex($false, 'Local\UniNetGitHubFrontendSeedV1')
$locked = $false
$manifest = $null
function Protect-Directory([string]$path) {
    $null = New-Item -ItemType Directory -Path $path -Force
    $owner = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    & icacls $path /inheritance:r /grant:r "${owner}:(OI)(CI)F" 'SYSTEM:(OI)(CI)F' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not protect local fixture receipts.' }
}
function Sql([string]$query) {
    # No caller-supplied database or role; no credentials are placed on command lines.
    $rows = @($query | & docker exec -i $container psql -U uninet_test -d $database -At -v ON_ERROR_STOP=1)
    if ($LASTEXITCODE -ne 0) { throw 'Disposable PostgreSQL verification failed.' }
    return $rows
}
function Json-Sql([string]$query) {
    foreach ($line in @(Sql $query)) { if (-not [string]::IsNullOrWhiteSpace($line)) { $line | ConvertFrom-Json } }
}
function Api([string]$method, [string]$path, $session, $body = $null, [switch]$ExpectForbidden) {
    $params = @{ Uri="$api$path"; Method=$method; ContentType='application/json'; TimeoutSec=30 }
    if ($session) { $params.Headers = @{ Authorization="Bearer $($session.accessToken)" } }
    if ($null -ne $body) { $params.Body = $body | ConvertTo-Json -Depth 20 -Compress }
    try {
        $value = Invoke-RestMethod @params
        if ($ExpectForbidden) { throw 'Expected authorization rejection was not enforced.' }
        return $value
    } catch {
        $status = 0
        if ($_.Exception.PSObject.Properties['Response'] -and $_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        if ($ExpectForbidden -and $status -eq 403) { return $true }
        # Never print a request/response body: auth responses contain session credentials.
        throw "E2E API failed: $method $path (HTTP $status). Stop and inspect durable fixture state before retry."
    }
}
function Login([string]$email, [string]$file) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Protected credential missing for $email." }
    $secure = (Get-Content -Raw -LiteralPath $file).Trim() | ConvertTo-SecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer); $session = Api POST '/auth/login' $null @{email=$email;password=$password} }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer); $password=$null }
    $me = Api GET '/auth/me' $session
    if ($me.email -cne $email -or [int]$me.role -ne 0) { throw 'E2E account identity mismatch.' }
    # Independent runtime proof, not just a launcher receipt: the actual API-issued
    # session must exist in this exact FE database before any fixture business write.
    $sha=[Security.Cryptography.SHA256]::Create()
    try { $hash=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($session.refreshToken)))).Replace('-','') }
    finally { $sha.Dispose() }
    $accountId=([Guid]$me.id).ToString('D')
    $proof=@(Sql ('SELECT count(*) FROM "RefreshTokens" WHERE "AccountId"='''+$accountId+''' AND "TokenHash"='''+$hash+''''))
    $hash=$null
    if ($proof[0] -ne '1') { throw 'API_DATABASE_TARGET_MISMATCH: issued session is not in the permitted FE database. No fixture mutation permitted.' }
    return $session
}
function Complete-FixtureProfile($session) {
    $p = (Api GET '/auth/me' $session).profile
    if (-not $p) { throw 'Student profile is missing.' }
    # Preserve every existing field; fill only fields needed for the FE's profile-complete guard.
    if ([string]::IsNullOrWhiteSpace($p.bio) -or [string]::IsNullOrWhiteSpace($p.universityName)) {
        $body = @{ fullName=$p.fullName; nickname=$p.nickname; avatarUrl=$p.avatarUrl; coverUrl=$p.coverUrl; bio=$p.bio; universityName=$p.universityName; major=$p.major; studentCode=$p.studentCode }
        if ([string]::IsNullOrWhiteSpace($p.bio)) { $body.bio="[$marker] Local E2E student for manual GitHub integration testing." }
        if ([string]::IsNullOrWhiteSpace($p.universityName)) { $body.universityName='UniNet E2E Test University' }
        $null = Api PUT '/profile/student' $session $body
    }
    $me = Api GET '/auth/me' $session
    if (-not $me.profileComplete) { throw 'FE profile completion requirement is not satisfied.' }
    return $me
}
function Fingerprints {
    # Every existing Git/Contribution/Peer table is protected, not just row counts.
    $tables = @(Sql 'SELECT tablename FROM pg_tables WHERE schemaname=''public'' AND (tablename LIKE ''%Git%'' OR tablename LIKE ''%Contribution%'' OR tablename LIKE ''%Peer%'') ORDER BY tablename')
    foreach ($table in $tables) {
        if ($table -notmatch '^[A-Za-z0-9_]+$') { throw 'Unexpected catalog identifier.' }
        $query = 'SET TIME ZONE ''UTC''; SELECT '''+$table+'|'' || count(*) || ''|'' || md5(coalesce(string_agg(to_jsonb(t)::text,E''\n'' ORDER BY to_jsonb(t)::text COLLATE "C"),'''')) FROM "'+$table+'" t'
        @(Sql $query) | Where-Object { $_ -ne 'SET' }
    }
}
function Table-Fingerprint([string]$table, [string]$filter='') {
    $query='SET TIME ZONE ''UTC''; SELECT '''+$table+'|'' || count(*) || ''|'' || md5(coalesce(string_agg(to_jsonb(t)::text,E''\n'' ORDER BY to_jsonb(t)::text COLLATE "C"),'''')) FROM "'+$table+'" t'+$filter
    return @(Sql $query | Where-Object {$_ -ne 'SET'})[0]
}
try {
    try { $locked=$lock.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked=$true }
    if (-not $locked) { throw 'Another FE seed is running.' }
    $source = Read-Phase3E2EEnv (Join-Path $root '.env.github-app.e2e')
    $target = ConvertFrom-Phase3E2EConnectionString $source['ConnectionStrings__UniNet']
    if ($target['Host'] -cne '127.0.0.1' -or $target['Port'] -ne '55433' -or $target['Username'] -cne 'uninet_test' -or $target['Database'] -cne 'uninet_github_app_e2e') { throw 'Source E2E configuration identity mismatch.' }
    $info = (& docker inspect $container | ConvertFrom-Json)[0]
    if ($info.Id -cne '2fbe020661be4f7ae4a7fd6eac82524b6f3f33dad8ad74da7981e1ce3eb9f767' -or -not $info.State.Running) { throw 'Container identity mismatch.' }
    $mapping = & docker port $container 5432/tcp
    if ($LASTEXITCODE -ne 0 -or $mapping -cne '127.0.0.1:55433') { throw 'PostgreSQL mapping mismatch.' }
    $identity = @(Sql 'SELECT current_database() || ''|'' || current_user')
    if ($identity -cne "$database|uninet_test") { throw 'FE database identity mismatch.' }
    $runtime = Get-Content -Raw "$env:LOCALAPPDATA/UniNetGitHubFrontendE2E-runtime/runtime.json" | ConvertFrom-Json
    $listener = @(Get-NetTCPConnection -LocalPort 5239 -State Listen | Select-Object -ExpandProperty OwningProcess -Unique)
    $process = Get-CimInstance Win32_Process -Filter "ProcessId=$($runtime.Pid)"
    $parent = Get-CimInstance Win32_Process -Filter "ProcessId=$($process.ParentProcessId)"
    if (-not $runtime.PSObject.Properties['ConnectionTargetVerified'] -or -not $runtime.ConnectionTargetVerified -or
        $runtime.Database -cne $database -or $runtime.Workers -cne 'off' -or $runtime.Environment -cne 'E2E' -or $listener.Count -ne 1 -or $listener[0] -ne $runtime.Pid -or
        -not $process.CommandLine.Contains($runtime.Binary) -or $parent.CommandLine -notlike '*start-github-frontend-e2e.ps1*') { throw 'Running Backend is not the verified worker-off FE launcher.' }
    if ((Invoke-WebRequest 'http://localhost:5239/health' -UseBasicParsing).StatusCode -ne 200) { throw 'Backend unhealthy.' }
    $history = @(Sql 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId"')
    $expected = @(Get-ChildItem (Join-Path $root 'UniNet.Infrastructure/Data/Migrations') -Filter '*.Designer.cs' | ForEach-Object { $_.Name -replace '\.Designer\.cs$', '' })
    if (@(Compare-Object ($history | Sort-Object) ($expected | Sort-Object)).Count -ne 0) { throw 'Schema is not current; no migration permitted.' }
    if (@(Sql 'SELECT count(*) FROM "GitSyncJobs" WHERE "Status" IN (0,1,4)')[0] -ne '0') { throw 'Active Git sync job exists.' }
    Protect-Directory $store; Protect-Directory $run
    $history | Set-Content (Join-Path $run 'history-before.txt')
    $before = @(Fingerprints); $before | Set-Content (Join-Path $run 'git-fingerprints-before.txt')
    $audit = @{}
    $audit.Accounts = @(Json-Sql 'SELECT json_build_object(''id'',a."Id",''email'',a."Email",''role'',a."Role",''status'',a."Status",''profileId'',u."Id",''name'',u."FullName") FROM "Accounts" a LEFT JOIN "Users" u ON u."AccountId"=a."Id" WHERE a."Email" LIKE ''%@uninet-e2e.test'' ORDER BY a."Email"')
    $audit.Projects = @(Json-Sql 'SELECT json_build_object(''id'',"Id",''title'',"Title",''creatorId'',"CreatorId",''status'',"Status") FROM "Projects" ORDER BY "Id"')
    $audit.Members = @(Json-Sql 'SELECT json_build_object(''id'',"Id",''projectId'',"ProjectId",''profileId'',"UserId",''role'',"Role",''status'',"Status") FROM "ProjectMembers" ORDER BY "Id"')
    $audit | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $run 'audit-before.json')
    $dump = '/tmp/uninet-fe-seed-'+[Guid]::NewGuid().ToString('N')+'.dump'
    & docker exec $container pg_dump -U uninet_test -d $database --format=custom --file=$dump
    if ($LASTEXITCODE -ne 0) { throw 'FE backup failed; no seed performed.' }
    & docker exec $container pg_restore --list $dump | Set-Content (Join-Path $run 'backup.toc')
    if ($LASTEXITCODE -ne 0) { throw 'Backup archive invalid.' }
    & docker cp "${container}:$dump" (Join-Path $run 'baseline.dump') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Backup export failed.' }
    (Get-FileHash (Join-Path $run 'baseline.dump') -Algorithm SHA256).Hash | Set-Content (Join-Path $run 'backup.sha256')
    $credentials = @{
        A=@{Email='student-a@uninet-e2e.test';File="$env:LOCALAPPDATA/UniNetGitHubAppStep6E2E/student-a.dpapi"}
        B=@{Email='student-b@uninet-e2e.test';File="$env:LOCALAPPDATA/UniNetGitHubAppStep6E2E-StudentB/student-b.dpapi"}
        C=@{Email='student-c@uninet-e2e.test';File=(Join-Path $store 'student-c.dpapi')}
    }
    $manifest = @{Marker=$marker;Database=$database;Host='127.0.0.1';Port=55433;Role='uninet_test';ReceiptDirectory=$run;Created=@();Accounts=@{};ProjectA=$null;ProjectB=$connectedProjectId;Status='Preparing';ApiChecks=@{}}
    function Save-Manifest {
        $json=$manifest | ConvertTo-Json -Depth 12
        $json | Set-Content (Join-Path $store 'manifest.json')
        $json | Set-Content (Join-Path $run 'manifest.json')
    }
    Save-Manifest
    $sessions=@{}; $people=@{}
    foreach ($persona in @('A','B','C')) {
        $entry=$credentials[$persona]
        $exists=@($audit.Accounts | Where-Object { $_.email -ceq $entry.Email })
        if ($persona -eq 'C' -and $exists.Count -eq 0) {
            if (-not (Test-Path -LiteralPath $entry.File)) {
                $bytes=New-Object byte[] 24; $rng=[Security.Cryptography.RandomNumberGenerator]::Create();$rng.GetBytes($bytes);$rng.Dispose()
                $password='UniNet!7'+[Convert]::ToBase64String($bytes)
                ConvertTo-SecureString $password -AsPlainText -Force | ConvertFrom-SecureString | Set-Content -LiteralPath $entry.File
                $password=$null
            }
            $secure=(Get-Content -Raw -LiteralPath $entry.File).Trim() | ConvertTo-SecureString
            $pointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
            try {
                $password=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
                $null=Api POST '/auth/register/student' $null @{fullName='E2E Student C';nickname='E2E Student C';email=$entry.Email;password=$password;confirmPassword=$password;universityName='UniNet E2E Test University';major='E2E GitHub Integration'}
            } finally {[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer);$password=$null}
            $manifest.Created += 'StudentC'; Save-Manifest
        } elseif ($exists.Count -ne 1) { throw "Expected one existing account for persona $persona." }
        $sessions[$persona]=Login $entry.Email $entry.File
        $people[$persona]=Complete-FixtureProfile $sessions[$persona]
        $manifest.Accounts[$persona]=@{Email=$entry.Email;AccountId=$people[$persona].id;ProfileId=$people[$persona].profile.id;CredentialFile=$entry.File;ProfileComplete=$people[$persona].profileComplete}
        Save-Manifest
    }
    # Recover by marker and creator if a previous response/manifest write was lost. Never blindly POST twice.
    $candidate=@(Json-Sql ('SELECT json_build_object(''id'',"Id",''creatorId'',"CreatorId",''description'',"Description") FROM "Projects" WHERE "Title"=''GITHUB_LINKING_TEST'''))
    if ($candidate.Count -gt 1 -or ($candidate.Count -eq 1 -and ($candidate[0].creatorId -ne $people.A.profile.id -or $candidate[0].description -cne $description))) { throw 'Ambiguous project seed marker; no project altered.' }
    if ($candidate.Count -eq 0) {
        $project=Api POST '/project-hub' $sessions.A @{title=$title;description=$description;status=1;visibility=1;technologies=@('GitHub');submitterRole='Creator'}
        $manifest.Created += 'ProjectA'
    } else { $project=Api GET "/project-hub/$($candidate[0].id)" $sessions.A }
    $projectId=$project.id; $manifest.ProjectA=$projectId; Save-Manifest
    if ($project.creator.id -ne $people.A.profile.id) { throw 'Project Creator mismatch.' }
    $positions=@(
        @{id='e2e-member';name='Member';quantity=1;technologies=@();skills=@();jobDescription='Local E2E regular member';requirements=@();benefits=@()},
        @{id='e2e-leader';name='Leader';quantity=1;technologies=@();skills=@();jobDescription='Local E2E delegated Git Manager';requirements=@();benefits=@()}
    )
    foreach ($persona in @('B','C')) {
        $project=Api GET "/project-hub/$projectId" $sessions.A
        $member=@($project.members | Where-Object { $_.userId -eq $people[$persona].profile.id -and $_.status -eq 0 })
        if ($member.Count -eq 0) {
            if ($project.recruitment -and $project.recruitment.description -notlike "*$marker*") { throw 'Unexpected fixture recruitment drift.' }
            $null=Api PUT "/project-hub/$projectId/recruitment" $sessions.A @{description="[$marker] Seed-only recruitment";positions=$positions;status=1;deadline=$null}
            $requests=Api GET "/project-hub/$projectId/requests" $sessions.A
            $pending=@($requests.items | Where-Object {$_.userId -eq $people[$persona].profile.id -and $_.requestType -eq 0 -and $_.status -eq 0})
            if ($pending.Count -gt 1) { throw 'Ambiguous pending membership application.' }
            if ($pending.Count -eq 0) {
                $position=if($persona -eq 'B'){'e2e-member'}else{'e2e-leader'}
                $request=Api POST "/project-hub/$projectId/requests" $sessions[$persona] @{requestType=0;positionId=$position;message="[$marker] Authorized E2E membership preparation"}
            } else { $request=$pending[0] }
            $null=Api POST "/project-hub/requests/$($request.id)/decision" $sessions.A @{decision='accept'}
            $manifest.Created += "Membership$persona"; Save-Manifest
        }
    }
    $project=Api GET "/project-hub/$projectId" $sessions.A
    if ($project.recruitment -and $project.recruitment.status -eq 1) {
        $null=Api PUT "/project-hub/$projectId/recruitment" $sessions.A @{description="[$marker] Seed-only recruitment";positions=$positions;status=0;deadline=$null}
    }
    $grants=@(Api GET "/projects/$projectId/git/managers" $sessions.A)
    if (@($grants | Where-Object {$_.userProfileId -eq $people.B.profile.id -and $_.isActive}).Count -ne 0) { throw 'Regular member B has unexpected Git Manager rights; not changed automatically.' }
    if (@($grants | Where-Object {$_.userProfileId -eq $people.C.profile.id -and $_.isActive}).Count -eq 0) {
        $grant=Api POST "/projects/$projectId/git/managers" $sessions.A @{userProfileId=$people.C.profile.id}
        $manifest.Created += 'GitManagerGrantC'; $manifest.ManagerGrantId=$grant.id; Save-Manifest
    } else { $manifest.ManagerGrantId=($grants | Where-Object {$_.userProfileId -eq $people.C.profile.id -and $_.isActive} | Select-Object -First 1).id; Save-Manifest }
    foreach ($persona in @('A','B','C')) {
        $session=$sessions[$persona];$checks=@{}
        $detail=Api GET "/project-hub/$projectId" $session
        if (-not ($detail.isOwner -or $detail.isMember)) { throw 'Fixture project access failed.' }
        $checks.ProjectAccess='200'; $checks.Creator=$detail.isOwner
        $checks.ConnectionCount=@(Api GET '/github/connections' $session).Count
        $checks.IdentityCount=@((Api GET "/projects/$projectId/git/identities" $session).items).Count
        $checks.RepositoryCount=@((Api GET "/projects/$projectId/git/repositories" $session).items).Count
        if ($persona -eq 'B') { $null=Api GET "/projects/$projectId/git/app/credentials" $session -ExpectForbidden; $checks.AppCredentials='403' }
        else { $checks.AppCredentialCount=@(Api GET "/projects/$projectId/git/app/credentials" $session).Count; $checks.AppCredentials='200' }
        # No official result may be manufactured for this newly active project.
        try { $result=Api GET "/projects/$projectId/contribution/v2/results/me" $session; $checks.OfficialResult='Published response available' }
        catch { if ($_.Exception.Message -notmatch '\(HTTP (404|409)\)') { throw }; $checks.OfficialResult='Not published / workflow not official (404 or 409)' }
        $manifest.ApiChecks[$persona]=$checks
    }
    $project=Api GET "/project-hub/$projectId" $sessions.A
    $manifest.Members=$project.members | Select-Object id,userId,role,status
    $manifest.Status='ReadyForManualLinking'; $manifest.CompletedAt=[DateTime]::UtcNow.ToString('o')
    $after=@(Fingerprints); $after | Set-Content (Join-Path $run 'git-fingerprints-after.txt')
    $protectedBefore=@($before | Where-Object {$_ -notlike 'ProjectGitManagerGrants|*' -and $_ -notlike 'ProjectContributionWorkflows|*'})
    $protectedAfter=@($after | Where-Object {$_ -notlike 'ProjectGitManagerGrants|*' -and $_ -notlike 'ProjectContributionWorkflows|*'})
    if (@(Compare-Object $protectedBefore $protectedAfter).Count -ne 0) { throw 'Git/Contribution history changed unexpectedly; stop and inspect receipts. No automatic restore.' }
    # Creating a project legitimately fires official_project_workflow. Permit only
    # that new project's default workflow row; preserve every pre-existing row.
    $workflowFilter=if($manifest.Created -contains 'ProjectA'){' WHERE "ProjectId" <> '''+([Guid]$projectId).ToString('D')+''''}else{''}
    if ((Table-Fingerprint 'ProjectContributionWorkflows' $workflowFilter) -cne @($before|Where-Object {$_ -like 'ProjectContributionWorkflows|*'})[0]) { throw 'Existing contribution workflow changed.' }
    $grantFilter=if($manifest.Created -contains 'GitManagerGrantC'){' WHERE "Id" <> '''+([Guid]$manifest.ManagerGrantId).ToString('D')+''''}else{''}
    if ((Table-Fingerprint 'ProjectGitManagerGrants' $grantFilter) -cne @($before|Where-Object {$_ -like 'ProjectGitManagerGrants|*'})[0]) { throw 'Existing manager grant changed.' }
    if (@(Compare-Object $history @(Sql 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId"')).Count -ne 0) { throw 'Migration history changed unexpectedly.' }
    $manifest.HistoricalGitPreservation='PASS'; $manifest.ApiDatabaseTargetProof='API-issued session token hashes verified in FE database (not logged)'; Save-Manifest
    Write-Output "READY: 3 authenticated personas; project A=$projectId; project B=$connectedProjectId; new fixture steps=$($manifest.Created.Count)."
    Write-Output "Manifest: $(Join-Path $store 'manifest.json'). Passwords remain DPAPI-protected; no GitHub authorization fabricated."
} catch {
    # API units retain their own transactional rollback. Successful earlier units are journaled and reusable.
    # Do not delete accounts/projects, roll back user edits, restore over E2E or automatically retry an ambiguous POST.
    if ($manifest) { $manifest.Status='StoppedForInspection'; Save-Manifest }
    throw
} finally { if ($locked) { $lock.ReleaseMutex() }; $lock.Dispose() }
