# GitHub FE E2E Backend — API READY; provider callback settings UNVERIFIED

> **2026-11-22 safety correction — authoritative current state:** the previous launcher receipt mislabeled the API database. A PowerShell DbConnectionStringBuilder adapter bug left the API on `uninet_github_app_e2e`. That runtime (PID 2120) was stopped. The launcher now uses CLR setters/getters and validates the serialized child target. Current API **13820** (parent 2104) is on **uninet_github_fe_e2e_20261121**, with independent verification that new API-issued sessions persist in that exact database; workers remain off. The older API-read target claims below are historical and must not be used as target proof. Initial seed attempts changed A/B's missing profile fields, registered C and issued sessions in the original database before detection; no automatic cleanup/restore was performed. See [the current manual-test handoff](./UNINET_GITHUB_FE_MANUAL_TEST_READY.md) for full disclosure, current fixtures and recovery boundaries. No Neon/root environment access occurred.

> Latest repository-access build: installation discovery now includes optional validated `configurationUrl` from GitHub; no endpoint/schema change. API PID **13820**, launcher **2104**, E2E/worker-off, health 200, real API-issued session persistence verified in the FE clone. Live discovery: one installation, two eligible repositories, provider Configure URL returned. See [manual-test handoff](./UNINET_GITHUB_FE_MANUAL_TEST_READY.md) and [FE implementation report](../../../UniNet_FE/docs/specs/project-github-integration.md). No live GitHub access edit/link/sync was executed. Source configuration and old E2E database were not modified by this improvement.

Updated: 2026-11-22. Replaces the previous missing-PEM blocker report. Source baseline `a4f56dc05dddf77e29e661bb7144f9178d07e394`; existing uncommitted worker-isolation changes were preserved.

## Runtime — verified running and left running

- API PID **2120**, parent launcher PID **3776**. Already running when this task resumed; reused rather than restarting or creating a second instance.
- Listener: `http://localhost:5239`; API base `http://localhost:5239/api`.
- Verified parent command: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./scripts/start-github-frontend-e2e.ps1`.
- Verified child command: `dotnet.exe C:/Users/ADMIN/AppData/Local/UniNetGitHubFrontendE2E-runtime/build/bin/UniNet.API/debug/UniNet.API.dll`.
- Runtime receipt: `%LOCALAPPDATA%/UniNetGitHubFrontendE2E-runtime/runtime.json`, PID and binary match the live listener. Receipt start time `2026-11-21T16:59:40.9001687Z`.
- Required environment source: `.env.github-app.e2e`; child-only database override to the prepared FE clone. Root environment file was not read, applied or modified. No Neon connection.
- Runtime source build predates no changed production source in this continuation. Current source also compiled successfully through an isolated API-referencing verification helper, 0 errors (existing warnings). No production code or launcher changes were necessary in this continuation.

Launcher flags:

```text
ASPNETCORE_ENVIRONMENT=E2E
DOTNET_ENVIRONMENT=E2E
UNINET_SKIP_LOCAL_ENV_FILE=true
ASPNETCORE_URLS=http://localhost:5239
BackgroundWorkers__Enabled=false
GitHubE2E__DisableUnrelatedWorkers=true
GitHubSync__Enabled=false
GitHubSync__ManualEnqueueEnabled=false
GitHubSync__ScopedManual__Enabled=false
GitHubSync__ScopedManual__JobId=<empty>
AssessorOnboarding__Enabled=false
VerificationWorker__Enabled=false
```

The launcher clears inherited child environment, loads the E2E file, injects its PEM and explicit overrides, and runs the DLL without a launch profile. No Development user secrets are loaded in E2E. Existing registration gates suppress Git, chat delivery, reconciliation, notifications and other persistent workers. No sync was requested or executed.

## Database safety

- Docker `uninet-step2-concurrency`: ID `2fbe020661be4f7ae4a7fd6eac82524b6f3f33dad8ad74da7981e1ce3eb9f767`, running; exact port mapping `127.0.0.1:55433 -> 5432`.
- API target: **uninet_github_fe_e2e_20261121**, role **uninet_test**, local PostgreSQL. Existing clone was reused; no database created/restored/migrated/reset/dropped in this continuation.
- Reverified actual migration IDs against EF current source: **104 applied / 0 pending**, no pending model changes. Real EF Project read succeeds.
- Counts: 102 GitEvidenceVersions, 693 observations, 5 snapshots, 4 Git Contribution Runs. All 24 GitSyncJobs Completed; no Pending/Running/RetryScheduled job.
- Original valuable database `uninet_github_app_e2e` was not connected to or modified during this continuation. Its name appears only in the source file safety check, before the launcher substitutes the FE clone name.
- Login verification creates legitimate session/authentication writes in the FE clone only. No OAuth consent, repository linking, Git identity mutation, Peer/Contribution write, snapshot or GitHub activity creation was performed.

### Previous preparation evidence (2026-11-21)

Original database had 87 migrations versus 104 source migrations. A custom-format backup was restored into the owned FE clone and upgraded there only; original 111-table fingerprints remained unchanged. After clone upgrade, 46 Git/Contribution/Peer/OAuth table fingerprints matched the original, including the evidence/snapshot/run counts above. Project/member schema changes followed current source migrations.

Protected backup receipts: directory recorded in `%LOCALAPPDATA%/UniNetGitHubFrontendE2E-current.txt`. Backup SHA-256 `0570A9F6DFD91E08D4FB81E0A6FF675E6CDA41E186F96AC4D2B85AAEF6E7BB56`. Do not commit receipts or restore over original E2E.

## PEM and GitHub settings

- Restored configured key exists at `D:/uninet/uninet-integration-e2e.2026-11-12.private-key.pem`.
- Imported as RSA private key, minimum 2048 bits; successfully signed a short-lived App JWT in memory.
- GitHub `GET /app`: **200**, returned **App ID 5736376**, matching E2E configuration. PEM/JWT/secrets were not printed or persisted by verification.
- GitHub `GET /app/hook/config`: **200**. Actual provider webhook URL is `https://outwit-salami-hug.ngrok-free.dev/api/webhooks/github/app`.
- **Actual OAuth callback URLs in GitHub App/OAuth App Settings remain UNVERIFIED.** App JWT REST verification did not expose those settings. User reports the App retains an older callback. Do not infer that its saved callback is correct from a working webhook or `/app` response.
- No App ID, key path, provider settings, callback, webhook or environment file was changed.

## Ngrok, callbacks and FE returns

Existing tunnel on ngrok API 4040 is active:
`https://outwit-salami-hug.ngrok-free.dev -> http://localhost:5239`.
Public `/health` returns **200**. No tunnel reconfiguration/restart was needed.

| Purpose | Backend-configured URL / verified state |
| --- | --- |
| OAuth GitHub -> Backend | `https://outwit-salami-hug.ngrok-free.dev/api/github/oauth/callback` |
| App GitHub -> Backend | `https://outwit-salami-hug.ngrok-free.dev/api/github/app/authorization/callback` |
| OAuth webhook route | `https://outwit-salami-hug.ngrok-free.dev/api/webhooks/github` (provider setting not inspected) |
| App webhook | `https://outwit-salami-hug.ngrok-free.dev/api/webhooks/github/app` (provider setting verified) |
| Existing return target `e2e`, both flows | `https://outwit-salami-hug.ngrok-free.dev/swagger/index.html` |
| OAuth child target `expoWeb` | `http://localhost:8081/github/callback` |
| OAuth child target `expoNative` | `uninet://github/callback` |

Both public Backend callback paths respond **400** to a request without state: they are reachable and reject invalid callbacks. This is not proof of a successful GitHub OAuth round trip or correctness of the saved GitHub callback.

Ask the App administrator to inspect the existing callback setting. The required App callback for this runtime is the App URL above; the OAuth App needs the OAuth URL above. If the old value differs, its exact old URL and failure cannot be determined from the available API response; do not claim it was tested. No provider setting should be changed without the user's decision.

Backend -> FE return is separate. FE must implement `/github/callback`. `uninet://` is the actual FE native scheme and requires an appropriate native/dev build; generic Expo Go needs its own exact allowlisted return address. App return-target deployment validation requires HTTPS; `e2e` currently returns to Swagger, not a FE completion page. An explicit HTTPS FE App return target is still needed for a complete automatic App-to-FE handoff; do not bypass validation or add a callback relay.

## Live verification (this continuation)

| Check | Result |
| --- | --- |
| Local `/health` | 200 |
| Public tunnel `/health` | 200 |
| `/health/github/instance` | 200, body Degraded (worker-off) |
| `/health/github/readiness` | 503, expected with GitSync disabled |
| `/health/github/queue` | 503, reported as actual result; not forced healthy |
| Expo Web CORS OPTIONS | 204, exact allow-origin `http://localhost:8081`, authorization header allowed |
| Student A Auth login + `/api/auth/me` | 200 + 200, correct account |
| Student B Auth login + `/api/auth/me` | 200 + 200, correct account; new StudentB DPAPI store used |
| Both users: connections, project repository list, project identity list | 200 for all six requests |
| Anonymous auth/me, connections, project repositories/identities | 401 |
| Anonymous OAuth start/complete and App authorization start | 401 |
| Public callbacks without state | 400 for both |
| App PEM and actual App ID | Verified via live GitHub API |

Authentication used protected E2E credentials and the official login API. No passwords, access/refresh tokens or authorization codes were printed. No OAuth start/link/sync mutation was sent with an authenticated session.

Previous focused regression: **89 PASS / 0 FAIL / 0 SKIP** (2026-11-21). Not rerun or double-counted this continuation: no implementation changed. Live HTTP/GitHub/real PostgreSQL checks above are direct verification, not a newly claimed automated test-suite count. `git diff --check` passes with line-ending notices.

## Frontend configuration

The real FE key remains `EXPO_PUBLIC_API_URL` in `D:/uninet/UniNet_FE/src/constants/config.ts`. Current FE environment still points at `https://uninet-be.onrender.com/api`; **FE was not edited**.

Set for Expo Web local development:

```text
EXPO_PUBLIC_API_URL=http://localhost:5239/api
```

Restart Expo after changing it. Keep `/api` suffix: the FE API service appends paths such as `/auth/refresh`.
Android emulator can use `http://10.0.2.2:5239/api`; physical devices need the allowed HTTPS tunnel, not their own localhost. CORS includes localhost:8081 and 127.0.0.1:8081 without adding a wildcard.

## Readiness summary

- **API READY**, local auth/CORS/project Git read APIs verified.
- **GitHub App key/configuration identity READY**; callback/consent configuration is a separate check.
- **OAuth callback round-trip BLOCKED / UNVERIFIED** until saved provider setting and user consent are checked.
- **App callback round-trip BLOCKED / UNVERIFIED**, despite reachable Backend route, until the older provider callback is checked and FE return target is prepared.
- **FE coding READY** for repository linking and verified identity UI/API integration. Existing signed-in E2E users can read linked resources now. Fresh end-to-end provider authorization/linking is conditional on the callback checks, real user consent and project authorization; no complete linking mutation was claimed as tested.

## Run, inspect and stop

Current API remains running in its existing launcher terminal (parent PID 3776). Do not start a second copy.

```powershell
# Inspect current receipt, then verify live listener/process identity.
Get-Content "$env:LOCALAPPDATA/UniNetGitHubFrontendE2E-runtime/runtime.json"
Get-NetTCPConnection -LocalPort 5239 -State Listen
Get-CimInstance Win32_Process -Filter 'ProcessId=2120' | Select-Object ProcessId,ParentProcessId,CommandLine
Invoke-WebRequest http://localhost:5239/health
```

To stop: Ctrl+C in its terminal, or **only after rechecking PID 2120 still matches the recorded API DLL and listener**, `Stop-Process -Id 2120`. A stale PID must not be killed blindly.

After stopping, start the existing launcher:

```powershell
cd D:/uninet/UniNet_BE
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/start-github-frontend-e2e.ps1
```

Current console output belongs to the launcher terminal. The previous `%LOCALAPPDATA%/UniNetGitHubFrontendE2E-runtime/stdout.log` and `stderr.log` are historical preparation logs, not proof of current live output. For a future foreground launch with a log:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/start-github-frontend-e2e.ps1 *>&1 |
  Tee-Object "$env:LOCALAPPDATA/UniNetGitHubFrontendE2E-runtime/frontend-console.log"
# In another terminal:
Get-Content "$env:LOCALAPPDATA/UniNetGitHubFrontendE2E-runtime/frontend-console.log" -Tail 50 -Wait
```

No migrations, DB clone recreation, source refactor, FE edit, provider settings change, commit or push in this continuation.

## FE API contracts

All authenticated calls use the caller's UniNet bearer session; never send GitHub tokens/PEM to FE. Authenticate through the existing Auth API. Official local E2E login was verified; no user OAuth consent was performed.

### OAuth identity verification

1. `POST /api/github/oauth/start`, body `{"returnTarget":"expoWeb"}` -> `{flowId, authorizationUrl, expiresAt}`.
2. Open returned authorization URL; GitHub calls the Backend callback. Backend validates protected state and redirects to the allowlisted target with `githubFlowId` and `githubStatus`.
3. Same signed-in account sends `POST /api/github/oauth/complete`, body `{"flowId":"<received-flow-id>"}`.
4. Response contains connection `id`, verified `providerAccountId`, login/display/avatar, grantedScopes, status and timestamps; no provider secrets. `GET /api/github/connections` lists caller-owned connections.

Do not submit GitHub callback `code` directly as a connection or infer identity from login.

### Project repository linking

Project Git routes require Student role and existing project authorization; repository management uses Creator/delegated Git Manager policy.

- OAuth discovery: `GET /api/projects/{projectId}/git/repositories/discover?connectionId={connectionId}`.
- OAuth link: `POST /api/projects/{projectId}/git/repositories`, `{"providerRepositoryId":123,"oauthConnectionId":"<connection-id>"}`.
- App authorization: `POST /api/projects/{projectId}/git/app/authorization/start`, `{"oauthConnectionId":"<connection-id>","returnTarget":"e2e"}` -> `{flowId,authorizationUrl,expiresAt}`.
- App callback redirects with `githubAppFlowId` / `githubAppStatus`. **There is no App completion POST**; callback persists the verified credential.
- App discovery: `GET /api/projects/{projectId}/git/app/credentials`; `GET /api/projects/{projectId}/git/app/installations?credentialId={id}`; `GET /api/projects/{projectId}/git/app/installations/{installationId}/repositories?credentialId={id}`.
- App link: `POST /api/projects/{projectId}/git/app/repositories/link`, `{"credentialId":"<credential-id>","installationId":123,"repositoryId":456}`.
- Read: `GET /api/projects/{projectId}/git/repositories` and `/repositories/{linkId}`. Response includes association id, repository, credentialSource, accessStatus, syncReadiness, linked/unlinked timestamps and syncPaused.
- Unlink: `DELETE /api/projects/{projectId}/git/repositories/{linkId}`; do not delete historical evidence in FE.

With workers disabled, repository linking may persist a job/readiness state that is not processed. This task does not authorize executing those jobs. UI must present actual PendingAuthorization/AccessLost/SyncPaused states rather than inventing completion.

### Project Git identity

- `POST /api/projects/{projectId}/git/identities`, `{"connectionId":"<own-verified-oauth-connection-id>"}`.
- `GET /api/projects/{projectId}/git/identities` and `/identities/{identityId}`.
- `DELETE /api/projects/{projectId}/git/identities/{identityId}`.
- `POST /api/projects/{projectId}/git/identities/{identityId}/reactivate`, same connectionId body.
- `GET /api/projects/{projectId}/git/identities/{identityId}/history`.

Identity response includes id, projectId, userProfileId, provider, numeric externalAccountId, externalUsername, status and lifecycle timestamps. Ownership is checked server-side; FE cannot choose another owner using a request field.

Relevant errors from source: `UNAUTHORIZED`, `GITHUB_NOT_CONFIGURED`, `GITHUB_REAUTH_REQUIRED`, `GITHUB_APP_NOT_CONFIGURED`, `GITHUB_APP_IDENTITY_REQUIRED`, `GITHUB_APP_USER_MISMATCH`, `GITHUB_REPOSITORY_ALREADY_LINKED`, `GITHUB_LINK_CONFLICT`, `GITHUB_REPOSITORY_LINK_NOT_FOUND`, `GIT_PROJECT_MEMBERSHIP_REQUIRED`, `GIT_IDENTITY_NOT_OWNER`, `GIT_IDENTITY_OWNERSHIP_CONFLICT`, `GIT_IDENTITY_CONCURRENCY_CONFLICT`. Treat status/code together; never log authorization URLs, callback codes or tokens.



## Project A scoped sync audit — 2026-11-23
See [scoped sync audit](./UNINET_PROJECT_A_SCOPED_SYNC_AUDIT_20261123.md). Requested mln131-group4 association is already unlinked. Current activation requires global Enabled and creates Initial, while scoped consumer accepts only Manual. No activation/enqueue/sync/restart performed; worker-off remains. Provider read-only repository access verified. BLOCKED before writes.


## Project A multi-repository sync — 2026-11-23
Both current A associations were activated through scoped Manual API and completed their pinned jobs. See [execution report](./UNINET_PROJECT_A_MULTI_REPOSITORY_SYNC_E2E_20261123.md). Final API PID 19808, health 200, all workers/manual enqueue off. Project B/history preserved. Project A has no project Git identity history yet; do not claim Contribution attribution ready. No snapshot/scoring/Peer action performed.


## Project A identity/readiness audit — 2026-11-23
A/B now have verified Active identities in A. Read-only policy preview retains A: 4 Commit, 2 PR, 3 Issue; B: 2 Issue; no unattributed candidates. These are not frozen/official results. Project A remains Active, OfficialV2 selected, no A Git snapshot/Peer cohort; official status blocks ProjectNotCompleted. C need not link GitHub for audit, but official roster includes A+B+C. See [identity audit](./UNINET_PROJECT_A_IDENTITY_ATTRIBUTION_READINESS_20261123.md). All table fingerprints unchanged; no live writes.


## Publication version redesign — source only (2026-11-23)
See [atomic approval/versioning report](./UNINET_OFFICIAL_CONTRIBUTION_PUBLICATION_VERSIONS_20261123.md). New migration and APIs verified on disposable only; NOT applied/restarted live. Current A run was already Finalized/published when audited; not modified. New FE capabilities remain gated on deployment. No scoring/Peer/identity/sync live mutations by this task.


## Official versioning deployed — 2026-11-23
[Deployment report](./UNINET_OFFICIAL_VERSIONING_E2E_DEPLOYMENT_20261123.md): only migration 20261123061945 applied to FE clone;105applied/0pending. Backup restore/rehearsal verified. API latest source PID15640, health200, workers/manual/scoped off. Existing A published result imported version1, original hashes and all business rows preserved; authenticated A/B GETsPASS. Adjustment/new Peer-round capabilities true. No new cohort, rating, Run or publish executed.

