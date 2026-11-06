# UniNet GitHub Integration — Development Handoff

**Handoff date:** 2026-11-05
**Repository:** `UniNet-FALL26/UniNet_BE`
**Current branch:** `dattt`
**Purpose:** Give a new Agent enough verified context to continue Phase 3 without re-reading the conversation.
**Scope of the last task:** Documentation only. No application code, migrations, database, or Git history were changed to create this handoff.

> This handoff is a point-in-time account of the source and recorded tool results. Re-check `git status`, migrations, tests, and source before acting. “Implemented” means relevant code exists; it does not imply production readiness or end-to-end verification.

## Current Continuation Snapshot — 2026-11-05

**Later 2026-11-06 continuation:** User chose **SECRET_ROTATION_DEFERRED**; do not block isolated E2E on rotation, and keep the risk recorded. Checker passed, local PostgreSQL and ngrok are running, E2E API started, and localhost/ngrok Swagger returned 200. A real worker defect was fixed in `GitSyncJobService.ClaimNextAsync`: materialize the non-composable `UPDATE ... RETURNING` query before applying `SingleOrDefault`. Restarted worker executed the claim query with no eligible jobs; actual claim/concurrency remains unverified. Await test user OAuth authorization before remaining E2E scenarios. See [E2E report](./GITHUB_INTEGRATION_PHASE3_E2E_REPORT.md).

**2026-11-06 update:** The E2E checker now supports a paired HTTPS ngrok OAuth callback and return target on the exact configured webhook origin, as well as paired localhost URLs. Seven URL cases and two configuration tests passed. The checker failed because Docker was unavailable and PostgreSQL port 55432 unreachable; port 5239 was free. Ngrok inspection API was unavailable. Secret rotation remains **SECRET_ROTATION_UNCONFIRMED**. Do not start the backend or use the credentials until the user confirms rotation; start Docker/ngrok and rerun the checker afterward. Real E2E and Phase 3 remain incomplete. See [the E2E report](./GITHUB_INTEGRATION_PHASE3_E2E_REPORT.md).

- The immediate task is to continue **Phase 3 E2E setup**, not to add features. Source-level Phase 1–3 work and fake/SQLite regressions exist; real PostgreSQL and GitHub E2E have **not** run. Keep Phase 3 **PARTIAL** and production readiness **NOT READY**.
- Latest read-only Git check before this documentation update: branch `dattt`, HEAD `cdf2bf153d602f1c351f9bd365a4705f43e14678`; worktree had **6 modified tracked files and 94 untracked files**. This update adds one untracked quick-start doc, so the current count is **95 untracked files**. The worktree includes extensive Phase 1–3 source, migrations, tests, E2E setup and docs. Do not stage broadly, overwrite, remove, stash, reset, clean, commit or push these changes.
- `.env.e2e` exists and is ignored; its contents were not read. Do not print or copy secret values. The E2E checker has previously rejected placeholder values. The local Docker container `uninet-postgres-e2e` (`postgres:17`) publishes `127.0.0.1:55432`, and TCP is reachable; database authentication, identity, schema and applied migration history remain unverified.
- Port 5239 is occupied by `UniNet.API` PID 8328. Leave it running unless the user explicitly coordinates stopping it; the E2E launcher will refuse to start a second API on that port.
- No database or GitHub requests were made during this handoff update. No migration was applied. Latest recorded isolated-output solution build passed; latest recorded focused Phase 1–3/membership tests were 48/48, but neither result is a real E2E pass.
- Exact next action: coordinate local E2E-only values and ownership of the process on port 5239; then run the safe checker. Before any DB connection or migration, independently verify the target is the disposable local `uninet_e2e` database and inspect its migration history. Do not use Neon/shared/prod. GitHub E2E additionally needs a test OAuth App, a disposable test repository, and a public HTTPS webhook tunnel.
- Read [GITHUB_INTEGRATION_NEXT_AGENT.md](./GITHUB_INTEGRATION_NEXT_AGENT.md) for a short resumption checklist. The detailed environment instructions remain in [PHASE3_E2E_LOCAL_SETUP.md](./PHASE3_E2E_LOCAL_SETUP.md).

## A. Project Overview

UniNet is a monolithic project collaboration platform. Its backend exposes project, membership, workspace, task/report, and contribution capabilities. This repository is a layered ASP.NET Core solution:

| Project | Responsibility |
|---|---|
| `UniNet.API` | ASP.NET Core REST controllers, authentication, DI, hosted workers, provider clients |
| `UniNet.Application` | Business services, DTOs, interfaces, authorization and workflow logic |
| `UniNet.Domain` | Domain entities and enums |
| `UniNet.Infrastructure` | EF Core `UniNetDbContext`, PostgreSQL mappings and migrations |
| `UniNet.Tests` | xUnit tests; many Git integration tests use SQLite and fakes |

**Confirmed stack:** C# / .NET 8, ASP.NET Core, EF Core 8.0.11, Npgsql 8.0.11, PostgreSQL, xUnit 2.9.2, and SQLite-backed tests. The solution entry point is `UniNet.sln`; API registration is in `UniNet.API/Program.cs`.

The GitHub Integration associates a UniNet project with one or more GitHub repositories, verifies project members’ GitHub identities, and collects repository activity. The intended later role is to supply auditable Git activity to Contribution calculations. Existing Contribution entities/services remain in the solution, but Git-based attribution/scoring is **not implemented** in the current GitHub Integration work. Do not treat Git activity as task/evidence completion or modify finalized Contribution Runs.

The backend instructions are in `.github/instructions/backend.instructions.md`. They describe the existing monolithic layered architecture and require reuse of current services, DTOs, entities, and validation patterns.

## B. Approved Business Rules

These are approved product/business constraints, not new technical recommendations:

- GitHub OAuth Connections belong to a UniNet **Account**, not a Project.
- One UniNet Account may connect multiple GitHub Accounts. The same GitHub Account may independently connect to multiple UniNet Accounts.
- A Project Git Identity is project-scoped and distinct from an OAuth Connection.
- In a given Project, a GitHub Account may have only one Active owner. A Project Member may link multiple GitHub Accounts; the same GitHub Account may be linked in other Projects.
- Identity ownership changes follow **Self-service Option A**: the current owner unlinks first; another eligible participant can then claim after verifying the GitHub account. There is no admin ownership-transfer API. The new owner may receive all retrievable history in a *new* contribution calculation; finalized runs remain immutable.
- Project Creator may manage Repository Integration even without a `ProjectMember` row. Only Creator can grant/revoke Git Manager permissions. An active Git Manager can manage repositories but cannot grant/revoke manager permissions. Ordinary members cannot manage repositories.
- Repository metadata and raw/normalized Git data are shared/deduplicated by stable GitHub Repository ID, but Project attribution/visibility is separate. A Project must not gain access to data merely because another Project linked the same repository.
- A repository can be linked to multiple UniNet Projects. Unlink does not erase historical data or finalized contribution records.
- When a member leaves/is removed, their project Git identities become inactive and Git Manager permission is revoked. This does **not** revoke their personal OAuth Connection or unlink repositories. A valid repository credential remains usable after its connector leaves.
- OAuth invalidation and repository-specific AccessLost are different conditions. OAuth invalidation requires reauthorization; a repository-specific permission denial affects that association. Neither should be inferred solely from the connector leaving a project.
- Git activity is derived from retrievable GitHub repository history, not matched to UniNet task/evidence records. The goal includes all branches and supported activity categories, subject to what GitHub APIs expose to the credential.
- Shared private-repository data requires project-level visibility protection.
- Finalized Contribution Runs must not be edited. Git scoring/formula decisions are pending; Phase 3 does not implement scoring.

**Technical recommendations / not independently approved by this handoff:** use an isolated disposable PostgreSQL instance to validate migrations and locking; run provider tests against a separately configured non-production GitHub App/repository; preserve raw payload retention and access controls; keep all new Git behavior outside global JWT authorization. These are safe next-step recommendations, not business-rule changes.

## C. Phase Completion Matrix

| Phase | Actual status | Implemented / tested evidence | Remaining work / important files |
|---|---|---|---|
| Phase 0 — Analysis | **Completed as analysis** | Earlier source analysis and approved business rules are captured in the conversation history and Phase 1 documentation. | Revalidate source if decisions or models change. No source behavior is delivered by an analysis phase. |
| Phase 1 — OAuth + Repository Integration | **Implemented; focused tests passed; not live-tested** | OAuth flow, encrypted credential persistence, manager grants, repository discovery/link/unlink/credential replacement; focused tests are in `UniNet.Tests/GitHubIntegrationServiceTests.cs`. | No live OAuth/GitHub test, no production key-ring verification, no PostgreSQL migration test. Review `GitHubOAuthService`, `ProjectGitRepositoryService`, `GitHubCredentialService`, and `docs/github-integration-phase-1.md`. |
| Phase 2 — Project Git Identity | **Implemented; focused tests passed; PostgreSQL behavior not verified** | Self-service link/unlink/reactivate, account ownership verification, one active owner per project/GitHub account, immutable history, departure/kick integration. Tests: `ProjectGitIdentityServiceTests.cs`, `ProjectGitIdentityModelTests.cs`, and integration tests. | No PostgreSQL concurrency/index test. Phase 2 migration is uncommitted and must not be removed/recreated. |
| Phase 3.1 — Sync Infrastructure | **Implemented in source; PostgreSQL E2E blocked** | Durable jobs/checkpoints, retry/leases, PostgreSQL `SKIP LOCKED`, active-job exclusion, configurable batch size/default interval, worker registration and disabled-by-default behavior. | Verify migration/index behavior, concurrent claim/reclaim/lease expiry and enabled worker lifecycle on isolated PostgreSQL. |
| Phase 3.2 — Historical Sync | **Partially implemented; fake-provider focused tests passed; not live-tested** | Paged retrieval for branches, commits per branch, PRs/reviews/review comments, issues/comments, and releases; checkpoint per category/scope; deduplicated provider objects, normalized activity/actors, commit-branch references, project grants. | Cannot claim complete GitHub history. Verify real pagination, API limits/shape, error conditions, and branch/history visibility in a non-production environment. |
| Phase 3.3 — Webhook | **Implemented in source; manual registration; E2E blocked** | HMAC-SHA256, payload bounds, delivery deduplication, push/PR/review/issue/comment/release event allowlist, durable jobs and coalescing across all active job states (`Pending`, `Running`, `RetryScheduled`, `Paused`). Manual GitHub registration instructions are in the Phase 3 doc. | No public test endpoint, real GitHub delivery or PostgreSQL duplicate-delivery race run. |
| Phase 3.4 — Scheduled Sync | **Implemented in source; enabled worker E2E blocked** | Due-job enqueue, configured 6-hour default/per-link intervals, pause/resume, missed-webhook fallback, worker registration. `GitHubSync:Enabled` defaults false. | Verify enabled worker startup/recovery/scheduling/retries against isolated PostgreSQL; worker remains off in this environment. |
| Phase 3.5 — Access Reconciliation | **Implemented in source; service/fake-provider tests pass; E2E blocked** | OAuth 401 invalidation is connection-scoped; repository 403 affects one association; pending/access-lost links are periodically probed by Scheduled Sync only when enabled; success records `AccessRestored`; rate-limit/transient failures preserve authorization; 404 does not infer deletion. Credential replacement verifies owner/access and queues or refreshes sync work. | PostgreSQL/worker and live provider classification remain unverified. Connector departure preserves repository/OAuth state and continues identity/grant cleanup. |
| Phase 3.6 — Activity API + Testing | **Implemented in source; fake-provider tests pass; E2E blocked** | Project-scoped activity/status/job APIs, paging/filtering, actors, link/access cutoffs, private-repository isolation and DTOs without raw payload/token fields. Latest focused Phase 1–3/membership run: 48 passed. | Real HTTP-host, PostgreSQL visibility queries and full-suite environment failures remain unresolved (see G). |
| Phase 4 — Attribution | **Planned / not started** | No Phase 4 attribution implementation is included in current scope. | Design project-scoped attribution against the approved visibility and identity lifecycle rules, without modifying finalized runs. |
| Phase 5 — Contribution | **Planned / pending decision** | Existing Contribution domain is present, but Git scoring is not implemented. | Resolve the previously identified formula conflict with the current master prompt. Do not choose a formula or change calculation/finalized runs without approval. |

## D. Current Implementation Details

The status below is based on inspected source. “Tested” means focused automated tests exist and passed in the recorded run, not that the live provider or PostgreSQL was used.

| Area | Current implementation and data | Entry point / dependency | Status and limitations |
|---|---|---|---|
| OAuth flow | `GitHubOAuthService` / `IGitHubOAuthService`; `GitHubOAuthAuthorizationFlow`, `GitHubOAuthConnection`; state is random, stored hashed, expiring, one-time, and bound to `AccountId`. Pending and stored tokens use `IGitHubTokenProtector` backed by ASP.NET Data Protection. | `/api/github/oauth/start`, anonymous `/callback`, authenticated `/complete`; provider is `GitHubProviderClient`. | Implemented; fake-provider tests cover binding, single-use, no token DTOs, encryption/config failures. Live OAuth, actual Expo redirects, persistent multi-instance key-ring operation are not tested. `repo` scope can grant GitHub OAuth App write capability although backend requests are read-only. |
| Credential refresh/disconnect | `GitHubCredentialService` refreshes near-expiry tokens only when a refresh token is available; otherwise requires reauthorization. Invalid OAuth clears local encrypted credentials and sets linked active associations PendingAuthorization. | Used by repository, identity and sync services. | Implemented; depends on GitHub returning refresh support. A newly completed OAuth connection does not automatically repair links assigned to another connection. |
| Git Manager authorization | `ProjectGitAuthorization` resolves active Student Account to `UserProfile`; Creator is checked using `Project.CreatorId`, not a member row; manager requires active grant and active membership. `ProjectGitManagerService` owns grant operations. | `/api/projects/{projectId}/git/managers` and manager-only repository/sync actions. | Implemented; focused tests include Creator-without-membership, grant rules and departure revocation. |
| Repository linking | `ProjectGitRepositoryService` verifies provider read access, stores globally deduplicated `GitHubRepository`, creates project association using `GitSync:DefaultIntervalHours` (fallback 6), initial Pending job and append-only history. Unlink is soft and preserves history. | `/api/projects/{projectId}/git/repositories` and related routes in `ProjectGitController`. | Implemented; SQLite/fake-provider tests cover project scoping, private-read check, shared repo, credential ownership/replacement, configured default interval and unlink history. PostgreSQL race handling/live provider not tested. |
| Git Identity | `ProjectGitIdentityService` verifies the actor-owned OAuth connection against GitHub `/user`; identities/history are project-scoped. `ProjectMemberService` deactivates member identities and revokes Git Manager grants in the membership transaction. | `/api/projects/{projectId}/git/identities` and nested unlink/reactivate/history routes. | Implemented; focused lifecycle and ownership tests passed. No PostgreSQL partial-unique-index/concurrent claim test. OAuth disconnect does not itself deactivate identity. |
| Sync job lifecycle | `GitSyncJobService` creates manual/due jobs, claims with PostgreSQL `FOR UPDATE SKIP LOCKED`, tracks leases, completion and bounded retry outcomes. Active-job unique index is per repository association/status. | Manual `POST .../repositories/{linkId}/sync`, settings/status/job history APIs; worker. | Implemented at source level; worker claim requires Npgsql. Unit tests do not establish PostgreSQL locking behavior. |
| Checkpoints | `GitSyncCheckpoint` keyed uniquely by sync job, category and scope; cursor is page number, status and processed-item count are persisted with each page transaction. | `GitHubSyncJobProcessor`. | Implemented and multi-page fake-provider test passed. Restart/recovery with actual provider and PostgreSQL transactions not verified. |
| GitHub data fetching | `GitHubProviderClient` uses REST API v3 endpoints, configured `GitSync:BatchSize` clamped to 1–100, GitHub Link header for next page, records rate-limit headers and maps Retry-After/rate-limit responses. | OAuth, repository and sync services via `IGitHubProviderClient`. | Implemented; request construction, configured batch size and fake HTTP pagination tested. No live GitHub test. API has retention, permission, and historical availability constraints. |
| Raw Git data | `GitHubProviderObject` stores repository/category/provider object ID, raw JSON payload, occurrence/observation times and retrieval metadata; unique repository/category/object key. | Ingested by processor. | Implemented. Raw private-repository payloads are persisted; access, retention, backup and DB operator controls remain security considerations. |
| Normalized activities | `GitActivity` is related to a provider object and can have multiple actor-role rows (`GitActivityActor`). Commits can have multiple branch references. | Ingested and returned by activity service. | Implemented for the enumerated event mappings. The mapping is not a complete GitHub event/audit model; verify category semantics before scoring. |
| Project visibility | `ProjectGitActivityGrant` grants source-object visibility; `ProjectGitActivityVisibility` records per-project-association activity observation. Activity queries require project viewer authorization and honor unlink/access cutoffs. | `ProjectGitActivityService`, `IProjectGitActivityService`; `/api/projects/{projectId}/git/activities`. | Implemented; SQLite/fake-provider tests verify project separation, unlink/access-loss cutoffs, and response privacy. PostgreSQL behavior and comprehensive API-boundary penetration testing remain unverified. |
| Webhook | `GitHubWebhookService` validates delivery UUID, event and HMAC signature; stores dedup row; enqueues supported events by stable repository ID. Deliveries coalesce with pending/running/retry-scheduled/paused jobs. | Anonymous `POST /api/webhooks/github`; `GitHubWebhookController`. | Service tests cover signature, dedup and active-job coalescing. No public endpoint, live delivery or PostgreSQL concurrency run. |
| Scheduled worker | `GitSyncWorker` is registered as hosted service; disabled config makes it stop. Enabled worker requires PostgreSQL, claims, heartbeats, processes, retries and schedules due jobs. | `UniNet.API/Program.cs`, `GitSyncOptions`. | Worker remains disabled in this environment. Direct coverage proves disabled exit; enabled worker lifecycle and PostgreSQL claiming are unverified. |
| Access reconciliation | Processor maps credential-invalid response to `ReauthorizationRequired` and association-specific access denial to `AccessLost`; Scheduled Sync periodically probes recoverable links using stable repository IDs. Successful checks restore access and update repository metadata. | Sync processor / credential service / repository credential replacement / scheduled job service. | Fake-provider tests cover outcomes and OAuth reuse. Live status classification and enabled worker/recovery are unverified. 404 remains unavailable and is retried without an unsupported deletion inference. |

## E. Database & Migration History

Actual GitHub migration names and timestamps in `UniNet.Infrastructure/Data/Migrations`, in order:

1. `20261104081807_AddGitHubRepositoryIntegration`
2. `20261104082437_AddProjectGitRepositoryConcurrency`
3. `20261104155132_RestoreProjectGitMemberIdentities`
4. `20261104161008_AddGitSyncInfrastructure`
5. `20261104162306_AddGitActivityVisibility`
6. `20261104162504_AddGitCommitBranchReferences`
7. `20261104162721_AddGitHubWebhookDeliveries`
8. `20261104163219_AddProjectGitActivityVisibility`

Each listed migration currently has a `.cs` and `.Designer.cs` file. The Phase 2 migration is named **`20261104155132_RestoreProjectGitMemberIdentities`**.

**Recorded recovery incident:** a Phase 2 migration that had not been committed was accidentally removed using `dotnet ef migrations remove --no-build` with a stale build. The Phase 2 migration was reconstructed as `RestoreProjectGitMemberIdentities`; source/API build and focused tests were reported passing at that time. The migration file is present now, but this history is not proof of PostgreSQL schema equivalence or application.

**Current source state:** `UniNetDbContext` and `UniNetDbContextModelSnapshot.cs` include the GitHub integration, identity, sync, raw/normalized activity and project visibility entities. `dotnet ef migrations has-pending-model-changes` completed successfully with “No changes have been made to the model since the last migration.”

**Database state:** no migration was applied by this agent. The user reports that Phase 1–3 migrations were applied to the database configured at the time; this agent did not authenticate to confirm that target. A local Docker container `uninet-postgres-e2e` (`postgres:17`) is visible on port 55432, but the `.env.e2e` password remains a placeholder and its migration history was not queried. The status of Neon or any shared database remains **unknown**. Treat migration application as unverified until the exact target is checked.

Key schema patterns include stable provider repository/object deduplication, filtered unique active ownership/association indexes, restrictive foreign keys for history-bearing entities, per-association active sync exclusion, unique checkpoint scope, and append-only history guards in `UniNetDbContext`. The latest migration adds `ProjectGitActivityVisibilities` with a unique `(ProjectGitRepositoryId, GitActivityId)` index. EF model/snapshot consistency is verified, but generated migration SQL, PostgreSQL partial-index predicates, migration order in a real disposable database, and concurrent lock semantics are **not verified**.

## F. Known Bugs, Risks and Limitations

1. **PostgreSQL schema and concurrency are unverified.** EF snapshot agreement and generated idempotent SQL do not prove a real migration application. The durable claim SQL explicitly requires PostgreSQL; SQLite tests cannot verify `SKIP LOCKED`, partial indexes or transaction isolation. The local E2E container is visible and its TCP port responds, but no connection string/password is configured and `UNINET_TEST_POSTGRES` is absent.
2. **No live GitHub OAuth/API test.** Provider calls are tested with fakes. OAuth App configuration, permissions, refresh-token behavior, API responses and callback behavior in real Expo Web/Mobile clients are not established.
3. **Worker E2E is unverified.** Worker is registered but `GitHubSync:Enabled` defaults to `false`; direct worker tests prove disabled startup stops without work. Enabled lifecycle, lease recovery and PostgreSQL claiming were not run.
4. **Access reconciliation has source/fake-provider coverage but no live/provider E2E.** Scheduled Sync probes `PendingAuthorization`/`AccessLost` associations only when enabled and the credential is active. 404 remains inconclusive by design; rename/transfer behavior is inferred only when stable-ID lookup succeeds.
5. **Private shared-repository security needs broader testing.** Raw provider JSON is stored once globally. API activity uses project association visibility/cutoffs and omits raw payload, but comprehensive cross-project access tests, query-path audit and retention/backup controls remain important.
6. **Webhook integration is not live-tested.** The endpoint does not register webhooks with GitHub; manual setup instructions exist in `docs/github-integration-phase-3.md`. Delivery dedup and active-job coalescing are service-tested, not tested under PostgreSQL concurrency or through a real GitHub delivery.
7. **GitHub history is inherently bounded by provider access/API behavior.** Deleted branches, inaccessible private content, API retention/limits, permission changes, pagination changes, and secondary rate limits can prevent complete historical retrieval. “All history” must be understood as all retrievable data, not a guarantee of events GitHub no longer exposes.
8. **OAuth `repo` scope is broad.** The application uses provider requests read-only, but the OAuth App scope can grant write-capable authorization. Do not add write operations without separate approval and security review.
9. **Full test suite is not green and baseline is unknown.** The 2026-11-05 run had `DatabaseDesignTests.SkillsUseExactlyFourCategories` fail because the expected constraint omits the nullable-category case, then the test host aborted with ONNX Runtime `AccessViolationException`. 191 passed and 19 skipped before abort; no clean baseline comparison was done. Do not change unrelated modules to mask this result.
10. **No contribution formula decision.** A prior formula discussion conflicted with the current master prompt’s Pending Decision state. Do not implement Git scoring or mutate finalized Contribution Runs until Phase 5 approval.
11. **No commits or migration applications.** A large Phase 1/2/3 worktree remains uncommitted. Avoid staging broad paths or overwriting files.

## G. Test Results

Last recorded verification after the recent source fixes:

| Check | Command / outcome |
|---|---|
| Focused Phase 1–3 and membership regression set | `dotnet test UniNet.Tests\UniNet.Tests.csproj --no-restore --filter "FullyQualifiedName~GitHubIntegrationServiceTests|FullyQualifiedName~GitSyncInfrastructureTests|FullyQualifiedName~ProjectGitIdentityServiceTests|FullyQualifiedName~ProjectGitIdentityModelTests|FullyQualifiedName~ProjectMember"` — **42 passed, 0 failed, 0 skipped**. This filter excluded the rest of the test suite. |
| P0-02 security regression set | `dotnet test UniNet.Tests\UniNet.Tests.csproj --no-restore --filter "FullyQualifiedName~GitHubIntegrationServiceTests|FullyQualifiedName~GitSyncInfrastructureTests|FullyQualifiedName~ProjectGitIdentityServiceTests|FullyQualifiedName~ProjectGitIdentityModelTests|FullyQualifiedName~ProjectMember"` — **43 passed, 0 failed, 0 skipped** on 2026-11-05. Covers shared private repository project isolation, one association losing access while another syncs new data, AccessLost/unlink cutoffs, unlink independence, replacement credential visibility scope, project/link ID tampering, and payload/token-free DTOs. Uses SQLite/fakes; not PostgreSQL or HTTP-host E2E. |
| Final focused Phase 1–3 and membership set | Same focused filter — **48 passed, 0 failed, 0 skipped** on 2026-11-05 after webhook active-job coalescing and option wiring. SQLite/fake provider only. |
| Full test suite | `dotnet test UniNet.Tests\UniNet.Tests.csproj --no-build --no-restore` — **191 passed, 1 failed, 19 skipped**, then test host aborted due ONNX Runtime `AccessViolationException`. The failure was `DatabaseDesignTests.SkillsUseExactlyFourCategories` (nullable `Category` check expectation). No module outside GitHub Integration was changed. |
| E2E configuration safety tests | `Phase3E2EConfigurationTests` — **2 passed**; verifies default `.env` behavior is preserved and E2E can explicitly bypass it. |
| Final source build | Isolated-output `dotnet build UniNet.sln --no-restore -p:BaseOutputPath=<temp> -p:UseAppHost=false` — **succeeded**, 0 errors. In-place output is locked by a running `UniNet.API` process; it was not stopped. Existing `CS9113`/`CS8619` warnings remain. |
| EF snapshot check | `dotnet ef migrations has-pending-model-changes --project UniNet.Infrastructure\UniNet.Infrastructure.csproj --startup-project UniNet.API\UniNet.API.csproj --context UniNetDbContext` — **no pending model changes**. |
| Migration source/SQL inspection | `dotnet ef migrations list --no-connect` — source chain inspected; idempotent migration SQL generated and Phase 3 indexes/restrictive FKs checked statically. No SQL was applied. |
| Setup scripts | PowerShell AST checks passed; environment/connection parser self-tests passed. The current placeholder `.env.e2e` correctly fails validation; start script refused to launch while placeholders remain and port 5239 is occupied. No database/GitHub request was made. |
| Whitespace check | `git diff --check` passed for tracked changes; added untracked integration/docs/scripts were separately inspected. |

`dotnet ef migrations has-pending-model-changes` reports no model changes. `dotnet ef migrations list --no-connect` confirmed source order; an idempotent SQL script was generated and its Phase 3 indexes/FKs checked statically. No migration was applied by this agent. The local PostgreSQL test container is visible on port 55432 and TCP responds; there was no DB authentication/schema check because `.env.e2e` is still placeholder-only. The user reports migrations were applied to the configured database, but the exact target/history was not independently confirmed. `UNINET_TEST_POSTGRES` is absent. No live GitHub OAuth/API test or enabled-worker end-to-end test was performed.

## H. Worktree and Backup State

- **Branch:** `dattt`.
- **Last observed HEAD:** `cdf2bf1` (`Merge branch 'main' ...`). The GitHub Integration changes described here are not committed.
- **Modified tracked files (4):**
  - `UniNet.API/Program.cs`
  - `UniNet.Application/Services/ProjectMemberService.cs`
  - `UniNet.Infrastructure/Data/Migrations/UniNetDbContextModelSnapshot.cs`
  - `UniNet.Infrastructure/Data/UniNetDbContext.cs`
- **Untracked files before this handoff (86):**
  - `UniNet.API/Controllers/`: `GitHubOAuthController.cs`, `GitHubWebhookController.cs`, `ProjectGitController.cs`
  - `UniNet.API/Services/`: `GitHubOAuthFlowCleanupWorker.cs`, `GitHubProviderClient.cs`, `GitHubTokenProtector.cs`, `GitSyncWorker.cs`
  - `UniNet.Application/Common/`: `GitHubIntegrationException.cs`
  - `UniNet.Application/DTOs/`: `GitHubIntegrationContracts.cs`, `ProjectGitIdentityContracts.cs`
  - `UniNet.Application/Interfaces/`: `IGitHubOAuthService.cs`, `IGitHubProviderClient.cs`, `IGitHubTokenProtector.cs`, `IGitHubWebhookService.cs`, `IGitSyncJobProcessor.cs`, `IGitSyncJobService.cs`, `IProjectGitActivityService.cs`, `IProjectGitIdentityService.cs`, `IProjectGitManagerService.cs`, `IProjectGitRepositoryService.cs`
  - `UniNet.Application/Services/`: `GitHubCredentialService.cs`, `GitHubIntegrationOptions.cs`, `GitHubOAuthFlowCleanupService.cs`, `GitHubOAuthService.cs`, `GitHubSyncJobProcessor.cs`, `GitHubWebhookOptions.cs`, `GitHubWebhookService.cs`, `GitSyncJobService.cs`, `GitSyncOptions.cs`, `ProjectGitActivityService.cs`, `ProjectGitAuthorization.cs`, `ProjectGitIdentityService.cs`, `ProjectGitManagerService.cs`, `ProjectGitRepositoryService.cs`
  - `UniNet.Domain/Entities/`: `GitActivity.cs`, `GitActivityActor.cs`, `GitCommitBranchReference.cs`, `GitHubOAuthAuthorizationFlow.cs`, `GitHubOAuthConnection.cs`, `GitHubProviderObject.cs`, `GitHubRepository.cs`, `GitHubWebhookDelivery.cs`, `GitSyncCheckpoint.cs`, `GitSyncJob.cs`, `ProjectGitActivityGrant.cs`, `ProjectGitActivityVisibility.cs`, `ProjectGitManagerGrant.cs`, `ProjectGitMemberIdentity.cs`, `ProjectGitMemberIdentityHistory.cs`, `ProjectGitRepository.cs`, `ProjectGitRepositoryHistory.cs`
  - `UniNet.Domain/Enums/`: `GitHubOAuthConnectionStatus.cs`, `GitHubOAuthFlowStatus.cs`, `GitSyncCheckpointStatus.cs`, `GitSyncJobStatus.cs`, `GitSyncType.cs`, `ProjectGitMemberIdentityHistoryAction.cs`, `ProjectGitMemberIdentityStatus.cs`, `ProjectGitRepositoryAccessStatus.cs`, `ProjectGitRepositoryHistoryAction.cs`
  - `UniNet.Infrastructure/Data/`: `GitActivityConfiguration.cs`, `GitHubIntegrationConfiguration.cs`, `GitSyncConfiguration.cs`, `ProjectGitMemberIdentityConfiguration.cs`
  - `UniNet.Infrastructure/Data/Migrations/`: `.cs` and `.Designer.cs` for each of the eight migrations listed in E.
  - `UniNet.Tests/`: `GitHubIntegrationServiceTests.cs`, `GitSyncInfrastructureTests.cs`, `ProjectGitIdentityModelTests.cs`, `ProjectGitIdentityServiceTests.cs`
  - `docs/`: `github-integration-phase-1.md`, `github-integration-phase-3.md`
- This handoff adds `docs/GITHUB_INTEGRATION_HANDOFF.md`; it will also be untracked until explicitly handled by the user.
- **Do not overwrite or remove** the listed GitHub Integration files, the restored Phase 2 migration, later Phase 3 migrations, or current model snapshot. Do not use `git add .`, reset, clean, migration removal, or automatic commit.
- **Recovery snapshot exists:** `C:\Users\ADMIN\.copilot\session-state\3d95d61d-8375-466b-91c2-16cfdfc78cc1\files\phase3-recovery-20261104-234045`, verified at the prior check with 90 copied source files. It is in Copilot session state, not the repository; another account/machine may not have access. It is not a portable recovery point unless copied securely by the user.

## I. NEXT TASKS — PRIORITIZED

### P0 — Must Fix Before Production / Phase 4

#### P0-01 — Validate GitHub migrations and database constraints on disposable PostgreSQL
- **Why:** EF snapshot checks do not prove migration SQL, partial indexes, restrictions, or PG concurrency work.
- **Read:** `UniNet.Infrastructure/Data/Migrations/*Git*`, `UniNet.Infrastructure/Data/UniNetDbContextModelSnapshot.cs`, `UniNet.Infrastructure/Data/GitHubIntegrationConfiguration.cs`, `GitSyncConfiguration.cs`, `GitActivityConfiguration.cs`.
- **Expected edits:** Add/extend PostgreSQL integration tests or a disposable local test harness only after verifying the existing test setup is safe. Do not target Neon/shared/prod.
- **Dependencies:** A disposable PostgreSQL instance and explicit confirmation its lifecycle is isolated.
- **Acceptance:** Apply the migration chain only to disposable PG; verify schema/index predicates/FKs and migration order; exercise concurrent active identity/link/job claims; capture exact results. No real/shared DB touched.
- **Tests:** Existing PostgreSQL integration tests plus Git identity, repository concurrency, sync claim/lease and webhook dedup tests.
- **Risks:** Accidentally pointing at a shared DB; stale connection settings. Abort if isolation is uncertain.
- **Status:** Not tested.

#### P0-02 — Complete private/shared repository access review
- **Why:** Raw private-repository payloads are globally deduplicated while visibility is project-scoped; cross-project leakage is a high-impact risk.
- **Read:** `GitHubSyncJobProcessor.cs`, `ProjectGitActivityService.cs`, `GitActivityConfiguration.cs`, `GitHubIntegrationConfiguration.cs`, `ProjectGitController.cs`.
- **Expected edits:** Add security regression tests only for proven gaps; do not change business visibility rules.
- **Dependencies:** P0-01 for PG query/index confidence; project-scoped test fixtures.
- **Acceptance:** Project B cannot read Project A-only objects/activities before its own authorized sync; unlink/access loss cutoff is enforced; API DTOs never expose raw JSON/tokens; no cross-project ID manipulation leaks data.
- **Tests:** Expand `GitHubIntegrationServiceTests` and test authorization at service/API boundary.
- **Risks:** Existing in-memory/SQLite coverage may not reflect PostgreSQL translation or private repository metadata exposure.
- **Status:** **PARTIAL — SERVICE REGRESSION PASSED.** SQLite/fake-provider coverage includes private shared-repository isolation; project B remains able to sync/read new data after project A loses access; project A cannot see data collected after its `AccessLost` cutoff; unlink independence; credential replacement does not create cross-project activity visibility; project/link ID substitution is denied; response DTOs omit raw payloads/tokens. The focused GitHub/membership set passed 43 tests on 2026-11-05 before P1-01 additions. `UNINET_TEST_POSTGRES` is unset; PostgreSQL query translation, database constraints/concurrency, and real HTTP-host authorization/serialization remain unverified.

#### P0-03 — Confirm production OAuth and secret/key-ring operations
- **Why:** OAuth App `repo` scope is broad and token encryption depends on stable persistent Data Protection keys.
- **Read:** `docs/github-integration-phase-1.md`, `GitHubIntegrationOptions.cs`, `GitHubOAuthService.cs`, `GitHubTokenProtector.cs`, `Program.cs`.
- **Expected edits:** Documentation/config validation only unless a concrete defect is demonstrated; no secrets in source or logs.
- **Dependencies:** Deployment owner confirms callback URLs, key-ring persistence/permissions, secret store, allowed Expo return targets, and accepted OAuth scope.
- **Acceptance:** Non-production OAuth connection can complete; key ring survives restart and is shared by intended instances; refresh/no-refresh/revocation paths behave as designed; no credentials appear in URLs/DTOs/logs.
- **Tests:** Config failure/unit tests, provider sandbox test and deployment smoke test without exposing credentials.
- **Risks:** Scope provides more privilege than current read-only integration uses; key loss makes stored tokens undecryptable.
- **Status:** Automated tests partial; production configuration/live OAuth not tested.

### P1 — Required to Complete Phase 3

#### P1-01 — Finish Phase 3.5 access reconciliation semantics
- **Why:** 401/403 transitions exist but a periodic reconciliation and complete recovery path have not been established.
- **Read:** `GitHubSyncJobProcessor.cs`, `GitHubCredentialService.cs`, `ProjectGitRepositoryService.cs`, `GitSyncJobService.cs`, `ProjectMemberService.cs`, `ProjectGitRepositoryAccessStatus.cs`.
- **Expected edits:** Narrow reconciliation/recovery logic and tests if required. Preserve approved behavior: member departure alone never invalidates a still-valid repository credential.
- **Dependencies:** P0-01 safe database tests; confirm expected behavior for 404, rename/transfer, credential replacement and reauthorization.
- **Acceptance:** OAuth invalidity affects relevant OAuth-owned links as PendingAuthorization; repository-specific denial changes only that association to AccessLost; rate limiting/transient errors do not change authorization; verified replacement restores usable status and has a deliberate sync/reconciliation action; unlink history remains.
- **Tests:** Unit and PostgreSQL tests for 401, 403, rate-limit, 404, connector departure, replace credential, and repeated reconciliation.
- **Risks:** Marking all shared-project associations AccessLost on one repository-scoped denial, or treating a transient response as permission loss.
- **Status:** **PARTIAL — SERVICE/FAKE-PROVIDER REGRESSION PASSED; POSTGRES/WORKER E2E UNVERIFIED.** OAuth reauthorization reuses the matching inactive connection row; pending links become due for a scheduled probe after that connection is renewed. Scheduled sync periodically probes `PendingAuthorization`/`AccessLost` associations only when enabled and their credential is active. Stable repository-ID lookup can restore renamed/transferred repositories; success records `AccessRestored` and refreshes metadata. 401/403 scope, rate-limit/transient handling, 404 no-inference, credential replacement and connector departure are covered by focused fake-provider tests. The webhook retry-scheduled coalescing gap and unused default interval/batch size settings were fixed and focused-tested. The final GitHub/membership set passed 48 tests on 2026-11-05. `UNINET_TEST_POSTGRES` and test GitHub credentials are unavailable; PostgreSQL locking/index behavior, enabled worker lifecycle and live provider classification remain unverified.

#### P1-02 — Verify enabled worker lifecycle and scheduled recovery end-to-end
- **Why:** Worker is registered but currently tested only while disabled.
- **Read:** `GitSyncWorker.cs`, `GitSyncJobService.cs`, `GitSyncOptions.cs`, `GitHubSyncJobProcessor.cs`, `Program.cs`.
- **Expected edits:** Worker-level integration tests/harness; production changes only for reproduced faults.
- **Dependencies:** P0-01 disposable PostgreSQL.
- **Acceptance:** Enabled worker claims, heartbeats, processes, completes, retries, reclaims expired lease, enqueues due schedules, respects pause/access/connection status, and shuts down predictably.
- **Tests:** Worker integration tests with fake provider and isolated PG; failure/restart and lease-expiry scenarios.
- **Risks:** Tests that use SQLite cannot validate the worker claim query.
- **Status:** Not tested end-to-end.

#### P1-03 — Validate historical category mappings against GitHub in a non-production environment
- **Why:** Fake pagination tests prove request mechanics but not real GitHub response fields, full history, API limits, or mapping semantics.
- **Read:** `GitHubProviderClient.cs`, `GitHubSyncJobProcessor.cs`, `GitActivity.cs`, `GitActivityActor.cs`, `GitCommitBranchReference.cs`.
- **Expected edits:** Provider mapping fixes and tests based on captured sanitized fixtures; never store real credentials in fixtures.
- **Dependencies:** Non-production GitHub OAuth App and repositories with representative branches/PRs/issues/reviews/comments/releases.
- **Acceptance:** Every supported category paginates/checkpoints/restarts without duplicates; missing/restricted data produces safe errors; actor/time/object keys are stable; reported limitations are explicit.
- **Tests:** Fixture tests, pagination/rate-limit tests, controlled provider smoke test.
- **Risks:** GitHub data may be unavailable due account permissions, deleted branches or provider retention/limits.
- **Status:** Partial fake-provider tests; live API not tested.

#### P1-04 — Triage full-suite failures and ONNX test-host abort
- **Why:** The broad test result is incomplete and baseline status is not known.
- **Read:** `DatabaseDesignTests.cs`, `AuthTests.cs`, ONNX portrait processor/tests and the full test output from the prior run.
- **Expected edits:** Only fix issues proven relevant/authorized; do not weaken tests or change unrelated Auth/DatabaseDesign/ONNX code to make GitHub tests green.
- **Dependencies:** Reproducible test environment and baseline branch/CI comparison.
- **Acceptance:** Identify whether each failure is baseline, environment-specific or regression; record evidence; rerun stable broad suite after remediation/triage.
- **Tests:** Individual failing tests, test process isolation for ONNX, then full test suite.
- **Risks:** ONNX native runtime crash can abort remaining tests and obscure results.
- **Status:** One failure and one environment crash reproduced in the single full-suite run: `DatabaseDesignTests.SkillsUseExactlyFourCategories` expected a non-null-only constraint, then ONNX Runtime aborted the host. No baseline comparison was available; no unrelated code was changed. PostgreSQL tests were skipped because no test connection was configured.

### P2 — Improvements / Follow-up

#### P2-01 — Establish retention, audit and observability policy for raw Git data/webhook deliveries
- **Why:** Raw JSON and delivery metadata can contain private repository information and grow over time.
- **Read:** `GitHubProviderObject`, `GitHubWebhookDelivery`, configurations, logging/error paths and deployment policy.
- **Expected edits:** Policy/docs/metrics and later scoped data lifecycle changes if approved.
- **Dependencies:** Product/legal/security retention requirements.
- **Acceptance:** Document retention, backup access, deletion/legal hold, operational metrics and safe error logging.
- **Tests:** Retention boundaries and redaction tests if code is added.
- **Risks:** Premature deletion could violate history and finalized-contribution audit requirements.
- **Status:** Planned.

#### P2-02 — Prepare Phase 4 Attribution design only after Phase 3 acceptance
- **Why:** Attribution must be project scoped and auditable without cross-project data exposure or finalized-run mutation.
- **Read:** Existing Git identity/activity/visibility models and Contribution entities/services.
- **Expected edits:** Design/spec first; no Phase 4 code until Phase 3 is accepted.
- **Dependencies:** P0 security review and Phase 3.5/3.6 acceptance.
- **Acceptance:** Approved attribution rules, immutable snapshot boundary, backfill/identity transfer behavior, and test plan.
- **Tests:** Design acceptance criteria before implementation.
- **Risks:** Re-attributing historical data into finalized runs violates approved rules.
- **Status:** Planned; implementation not started.

#### P2-03 — Resolve Phase 5 Git scoring formula conflict
- **Why:** Prior Git contribution formula discussion conflicts with the current Master Prompt’s Pending Decision.
- **Read:** Current Master Prompt and existing `ContributionCalculator`/`ContributionService` only after Phase 5 is explicitly authorized.
- **Expected edits:** None until decision is approved.
- **Dependencies:** User/product decision.
- **Acceptance:** Explicitly approved formula, inputs, normalization, member evaluation interaction, recalculation/rejection loop, and immutable run behavior.
- **Tests:** Formula tests after approval.
- **Risks:** Unauthorized scoring changes can alter finalized contribution outcomes.
- **Status:** Pending decision; scoring not implemented.

**Next task:** Read `docs/PHASE3_E2E_LOCAL_SETUP.md`; fill ignored `.env.e2e` locally, confirm the exact local database target/migration history, stop the process currently occupying port 5239, then run `scripts/check-phase3-e2e.ps1` and `scripts/start-phase3-e2e.ps1`. Test-only GitHub OAuth and ngrok webhook values are still required. Never substitute Neon or another shared database. Until PostgreSQL, enabled-worker and GitHub E2E pass, P0-02/P1-01 and Phase 3 remain partial. Do not begin Phase 4.

## J. NEXT AGENT INSTRUCTIONS

1. Read `docs/GITHUB_INTEGRATION_HANDOFF.md` and the relevant Phase 1/3 docs.
2. Read `.github/instructions/backend.instructions.md` before any backend code edit.
3. Run `git status --short` and inspect `git diff --stat`; this worktree is intentionally dirty and includes untracked Phase 1/2/3 code.
4. Reconcile this handoff with the current source, migration list, tests, and `UniNetDbContextModelSnapshot.cs`; do not assume the status remained unchanged.
5. Confirm the next task and database test isolation. If no safe disposable PostgreSQL is available, do not apply migrations anywhere.
6. Continue from the existing Phase 3 implementation; do not recreate SyncJob, Checkpoint, processor, provider pagination, webhook or activity service components.
7. Preserve all approved business rules. In particular, member departure does not itself revoke OAuth or invalidate a valid repository; OAuth invalidity differs from repository-specific AccessLost.
8. Do not overwrite or delete Phase 1/2/3 source, restored migration `20261104155132_RestoreProjectGitMemberIdentities`, any other GitHub migration, or the current snapshot.
9. Never run `dotnet ef migrations remove`, especially with `--no-build`. Do not apply migrations to Neon or a real/shared database.
10. Do not start Phase 4 until Phase 3 is explicitly accepted. Do not change Contribution scoring/finalized runs.
11. Make incremental, narrowly scoped changes with tests; build and run the relevant focused tests after each change. Record what was actually tested versus not tested.
12. Update this HANDOFF after each material phase/task completion. Do not commit unless the user explicitly authorizes it.

## PROMPT FOR NEXT COPILOT AGENT

Read `docs/GITHUB_INTEGRATION_HANDOFF.md` and `.github/instructions/backend.instructions.md`, then inspect the current source, `git status`, migration list and EF snapshot. Confirm the first prioritized task and whether an isolated disposable PostgreSQL environment is safe. Continue from the existing Phase 3 implementation rather than rebuilding it. Preserve all approved business rules and Phase 1/2/3 code; do not overwrite or remove migrations, run `dotnet ef migrations remove`, apply any migration to Neon/shared/production databases, commit, or start Phase 4. Implement only the agreed next task, add focused tests, build and test after each increment, and update the HANDOFF with verified results when a significant task is complete.
