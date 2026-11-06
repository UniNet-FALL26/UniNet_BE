# UniNet GitHub Integration Phase 3 E2E Report

**Date:** 2026-11-05
**Result:** **BLOCKED — local setup is prepared, but real E2E has not started.**
**Phase 3 status:** Not complete. No real E2E pass is claimed.

## Minimal real GitHub linking E2E

- Source inspection: repository discovery/linking requires the project creator or an active Git Manager; it does not require an Active project. The local test project remains Pending and was not modified.
- OAuth: **BLOCKED**, awaiting user authorization of the test GitHub account and completion of the OAuth flow. Repository discovery, linking and persistence: **BLOCKED**, pending the connection and identification of the authorized test repository. No GitHub repository was changed.

## Project setup continuation

- Project `ad3b101e-fc07-4061-9e43-b1b539cd1719` remains **Pending**. Its first moderation attempt is **Error**, classified as missing Gemini API configuration. Manual approval requires the latest moderation to be **ReviewRequired**; an Error cannot be approved through the existing review service. No E2E Admin or Moderator exists. The database contains no active Admin or Moderator accounts. No project status or role was modified.
- Membership is **BLOCKED**: invitation requires an Active project. Student B remains an outsider, not a member; no membership was inserted.
- Real HTTP authorization checks: Student A, as creator, received HTTP 200 from the Git manager listing. Student B, as an outsider, received HTTP 403 when attempting repository linking. This does not establish the member-without-manager case. Identity linking as a member remains unverified.
- Student A has zero GitHub OAuth connections. OAuth authorization, discovery, linking, sync, and live webhook remain **NOT RUN**. The E2E backend and tunnel are available subject to process lifetime. Gemini test configuration and the existing moderation flow are needed before membership; a test OAuth authorization can proceed independently.

## E2E test accounts and project — 2026-11-06

- Created `student-a@uninet-e2e.test` and `student-b@uninet-e2e.test` through the existing student registration API against local `uninet_e2e`. Both are active Students with profiles and `EmailVerified=false`; this matches the existing registration and login behavior and no verification bypass was used.
- Both accounts successfully logged in through `POST /api/auth/login`; their access JWTs contain `Student` role and each successfully called authenticated `GET /api/auth/me`. No JWT or password is recorded here. New random passwords were saved encrypted under the current Windows user using DPAPI outside the repository.
- Created project `ad3b101e-fc07-4061-9e43-b1b539cd1719` via the project API; its creator is Student A profile `cfb1baa1-e7d1-4fea-bc2a-ae3a72831f65`. Student B profile `4a79be24-f8c5-4548-a849-66d3dae5dbb9` is not yet a member. The project is `Pending`; the existing invitation service requires an `Active` project, so no membership row or moderation bypass was inserted.
- GitHub OAuth authorization and subsequent E2E steps remain pending user interaction. No fake GitHub credentials were inserted. `SECRET_ROTATION_DEFERRED` remains open.

## E2E continuation — 2026-11-06

- **SECRET_ROTATION_DEFERRED:** user elected to continue E2E with the current credentials; rotation is not confirmed and the previously identified security risk remains open.
- Checker **PASS**. Docker container `uninet-postgres-e2e` publishes `127.0.0.1:55432`; ngrok local API reports the configured HTTPS origin forwarding to `http://localhost:5239`. E2E backend started; local and ngrok Swagger both returned HTTP 200. No migration was applied.
- First worker startup exposed a real PostgreSQL defect: EF Core attempted to compose `SingleOrDefaultAsync` over a non-composable `UPDATE ... RETURNING` claim statement. Changed materialization to `ToListAsync` before applying `SingleOrDefault` in memory. After restart, PostgreSQL executed the claim SQL successfully with an empty job queue. This verifies the empty-queue path only; actual job claiming, lease, retry and concurrency remain unverified.
- OAuth authorization, repository/identity linking, historical sync, activity, webhook, scheduled/manual sync, recovery and private isolation are **not yet run**; waiting for test-account login and OAuth authorization. No real E2E pass is claimed.
- A focused test build against default output failed because the running E2E API locked its DLL; rerun with isolated output passed **4 GitSyncInfrastructureTests**. The E2E backend was left running for user OAuth interaction.

## Ngrok configuration checkpoint — 2026-11-06

- E2E checker now accepts paired localhost callback/return URLs or paired HTTPS ngrok URLs on the exact origin configured for the webhook. It rejects other hosts, public HTTP, userinfo, query, fragment, incorrect paths and mismatched origins. OAuth business validation and Development/Production configuration were not changed.
- URL validation: seven focused PowerShell cases passed; `Phase3E2EConfigurationTests`: 2 passed. Checker: **FAIL** because PostgreSQL port 55432 was unreachable and Docker daemon unavailable. URL configuration passed; API port 5239 was free and `.env.e2e` was ignored.
- Local ngrok inspection API was unavailable. An HTTPS HEAD request to the user-provided ngrok host returned 404; tunnel ownership and forwarding to the intended backend remain **unverified**. Backend startup was **not attempted**. Rotation of PostgreSQL password, JWT key, OAuth client secret and webhook secret is **SECRET_ROTATION_UNCONFIRMED**; no credential validity was tested.
- OAuth, webhook, Git sync and all real GitHub scenarios remain **BLOCKED / not run**. User action: rotate the four previously exposed secrets, update the test OAuth App/webhook and ignored local configuration, start Docker and ngrok, and confirm rotation. Do not disclose the values.

## Read-only PostgreSQL and environment checks — 2026-11-06

- Docker container `uninet-postgres-e2e` (`postgres:17`) publishes `127.0.0.1:55432`. A local container `psql` connection as `uninet_test` identified database `uninet_e2e` and PostgreSQL **17.11**. The database is the user's stated local test target; its disposability and provenance have not independently been proven. No data was changed and no migration was applied.
- `__EFMigrationsHistory` contains 61 migrations, including all eight GitHub migrations in source order from `20261104081807_AddGitHubRepositoryIntegration` through `20261104163219_AddProjectGitActivityVisibility`, including `20261104155132_RestoreProjectGitMemberIdentities`.
- PostgreSQL catalogs show the partial unique indexes for active OAuth connections, identities, project-repository links, manager grants and sync jobs, plus unique checkpoint, webhook delivery, provider object and visibility indexes. Foreign-key catalog inspection showed restrictive deletes on Git history/sync relationships, and cascade on OAuth authorization flows. These are catalog inspections, not behavioral or concurrency tests; schema completeness was not exhaustively verified.
- `check-phase3-e2e.ps1` confirmed required keys and non-placeholder values are present, the connection target matches its local allowlist, and the PostgreSQL TCP port is reachable. It failed because port 5239 was occupied by API PID 8328, `GitHub__CallbackUrl` did not match the required local callback, and `GitHub__ReturnTargets__phase3-e2e` did not match local Swagger. It did not authenticate to the database or GitHub. The separate `psql` calls above were read-only.
- No GitHub OAuth/API/webhook calls, worker execution, migration application or end-to-end scenarios were performed. Credential validity, database disposability, job concurrency, recovery and project isolation on PostgreSQL remain unverified. Phase 3 remains **PARTIAL / E2E BLOCKED**.

The rest of this report records the **2026-11-05** results and should be read in light of the newer checks above.

> **Current checkpoint (2026-11-05):** A read-only re-entry check before this documentation update reconfirmed branch `dattt`, HEAD `cdf2bf153d602f1c351f9bd365a4705f43e14678`, 6 modified tracked files and 94 untracked files. This update adds one untracked quick-start doc, so the current count is 95 untracked files. `.env.e2e` exists and is ignored; its contents were not inspected. The dedicated Docker PostgreSQL container and TCP endpoint are visible, but no database authentication, version query, schema inspection or migration-history query was performed. API port 5239 remains occupied by PID 8328. No database/GitHub request, migration or process start occurred during this checkpoint. The detailed run results below remain the latest recorded validation, not a live E2E result.

## Test environment

- Repository branch: `dattt`.
- PostgreSQL test endpoint: Docker reports container `uninet-postgres-e2e`, image tag `postgres:17`, mapped to `127.0.0.1:55432`; a TCP probe succeeds. `UNINET_TEST_POSTGRES` is not configured for the test runner, and `.env.e2e` still has a password placeholder.
- PostgreSQL server version: image tag `postgres:17`; actual server version was not queried.
- The repository's configured/shared database was not contacted. The user reports migrations are applied to the configured database, but this session did not authenticate to verify that the configured database is exactly `uninet_e2e` or inspect its migration history.
- GitHub test OAuth credentials, repository identifier, webhook secret, and callback configuration are absent from the test environment and application settings. No GitHub account/repository or public webhook URL was used.
- Backend port 5239 is already occupied by a running `UniNet.API` process. It was left running; the E2E checker refuses to start a second backend on the same port.

## Source and migration checks

- Isolated-output `dotnet build UniNet.sln --no-restore -p:BaseOutputPath=<temp> -p:UseAppHost=false`: **passed**, 0 errors; existing warnings `CS9113` and `CS8619`. An in-place build was blocked because the running API process locks its default output files; that process was left running.
- Focused Phase 1–3 and membership tests: **48 passed, 0 failed, 0 skipped** (SQLite/fakes).
- `dotnet ef migrations has-pending-model-changes`: **no pending model changes**.
- `dotnet ef migrations list --no-connect`: source migration chain includes all Phase 1–3 migrations, including `20261104155132_RestoreProjectGitMemberIdentities`, in source order. Applied status cannot be read without a test database.
- Generated an idempotent migration SQL script outside the repository and checked migration order plus generated definitions for the active-job, identity-owner, checkpoint, webhook-delivery, project-activity-visibility unique indexes and restrictive activity foreign keys. This was static SQL inspection only; no SQL was applied.
- No migrations were applied to any database.
- PowerShell AST parsing passed for the common/check/start scripts. Parser self-check passed for `=`, `;`, spaces, paired quotes, duplicate keys, malformed lines, and Neon-host rejection. The checker refuses the current placeholders and occupied API port; it did not start the backend.
- E2E startup-safety C# tests: **2 passed**. Isolated-output solution build: **passed**, avoiding the running API process's locked default output files.
- `check-phase3-e2e.ps1` sees the local PostgreSQL TCP port and Docker container, but fails safely on unfilled secret placeholders and the API port already in use. `start-phase3-e2e.ps1` repeats those checks and refuses to launch; no process was started.
- Created `.env.e2e.example`, ignored placeholder `.env.e2e`, both PowerShell scripts, and `docs/PHASE3_E2E_LOCAL_SETUP.md`. Existing appsettings and launch settings were not edited.

## E2E scenarios

| Scenario | Result |
|---|---|
| PostgreSQL migration application/schema/index/FK verification | Generated idempotent migration SQL was inspected statically. **No database connection or migration application performed; applied schema remains unverified.** |
| Concurrent job claim, lease expiry/recovery, checkpoint persistence, idempotency, retry | **Not run on PostgreSQL.** Fake/SQLite service coverage passes; it does not prove PostgreSQL locking behavior. |
| UniNet login and GitHub OAuth callback | **Not run — test credentials/configuration unavailable.** |
| Repository linking and Git Identity linking | **Fake-provider/SQLite tests pass; real E2E not run.** |
| Initial sync and real commit/PR/review/issue/comment/release retrieval | **Not run — no test GitHub OAuth App/repository.** |
| Activity API with real PostgreSQL visibility | **Not run.** Project-scoped service/DTO tests pass with SQLite/fakes. |
| GitHub webhook delivery, signature, duplicate delivery | **Service tests pass; real delivery not run — no public test endpoint/secret.** |
| Scheduled sync and enabled hosted worker | **Not run — worker requires PostgreSQL and remains disabled by default.** |
| Manual sync and worker recovery | **Service/job behavior is focused-tested; real worker E2E not run.** |
| OAuth revoke/reauthorization and repository AccessLost/recovery | **Fake-provider service tests pass; real GitHub status transitions not run.** |
| Shared private repository across multiple projects | **SQLite/fake-provider isolation tests pass; PostgreSQL and HTTP-host E2E not run.** |

## Test suite result

- Focused Phase 1–3/membership filter: **48 passed, 0 failed, 0 skipped**.
- Full `UniNet.Tests` attempt: **191 passed, 1 failed, 19 skipped**, then the test host aborted with ONNX Runtime `AccessViolationException`. The failure was `DatabaseDesignTests.SkillsUseExactlyFourCategories`, where the expected check constraint omits the nullable-category case. No unrelated module was changed to mask it.

## Outstanding blockers

1. Fill the local PostgreSQL password in `.env.e2e`; confirm the target database is disposable and inspect its migration history without using Neon/shared configuration.
2. Fill test-only GitHub OAuth client ID/secret and webhook secret, confirm the OAuth callback matches the test app, and provide the identifier for a disposable GitHub test repository.
3. Start ngrok for port 5239 and fill the resulting HTTPS webhook URL. Stop the already-running API process before using the E2E launcher.
4. Run the checker; it will refuse a non-local database target, missing values, invalid callback/URL, or occupied backend port. Then run the E2E scenarios above. No production domain was created or used.

The current blocker state was rechecked on 2026-11-05: the ignored local env file remains present, the API port is occupied, and no DB migration status or GitHub credentials were verified. See [GITHUB_INTEGRATION_NEXT_AGENT.md](./GITHUB_INTEGRATION_NEXT_AGENT.md) for a concise resume sequence.

## Production readiness

**Not production-ready.** Source-level Phase 3 features and fake-provider regressions are present, but real PostgreSQL migration/concurrency, enabled worker behavior, and GitHub OAuth/API/webhook behavior have not been verified. Phase 3 must remain incomplete until those checks pass. Phase 4 remains out of scope and unstarted.
