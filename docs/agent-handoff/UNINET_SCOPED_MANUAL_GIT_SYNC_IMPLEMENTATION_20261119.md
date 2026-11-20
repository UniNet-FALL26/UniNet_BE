# UNINET — Safe Scoped Manual Git Sync implementation

## Status

**IMPLEMENTATION PASS — ready for a separately approved E2E manual sync. No real sync was run.** The existing E2E Backend/configuration was not restarted, upgraded or enabled by this task. No live manual POST, snapshot, Run, cohort, rating, identity change, migration application, production/Neon access or commit/push.

## A. Architecture

Reuses the existing GitSyncWorker, GitSyncJobService, fenced GitHubSyncJobProcessor, provider, page transactions, evidence writer and checkpoint implementation. No second engine or public endpoint.

[Options](../../UniNet.Application/Services/GitSyncOptions.cs):

- Existing Enabled retains normal/global behavior.
- New ManualEnqueueEnabled defaults false and allows the existing authenticated manual API independently of normal background execution.
- New ScopedManual.Enabled defaults false. Scoped configuration requires nonempty ProjectId/AssociationId, an optional JobId, and ExecutionTimeoutSeconds between 1 and 86400 (default 900).
- Scoped mode and global Enabled=true are rejected together. Invalid/missing scope fails closed; there is no global fallback.
- No JobId means enqueue-only: the scoped consumer stops with AwaitingJobId without creating worker scopes or querying/claiming jobs. The API host remains available.

The [worker](../../UniNet.API/Services/GitSyncWorker.cs) branches into scoped execution **before** creating normal worker/polling/reconciliation/webhook/heartbeat loops. Normal and scoped workers share ProcessClaimedJobAsync, including the existing processor, lease heartbeat, completion, safe error classification and bounded retry policy. Cancellation is explicitly checked before CompleteAsync.

## B. Files changed by this feature

Production:

- [GitSyncOptions](../../UniNet.Application/Services/GitSyncOptions.cs): explicit scoped/enqueue configuration and validation.
- [IGitSyncJobService](../../UniNet.Application/Interfaces/IGitSyncJobService.cs): ClaimScopedManualAsync contract; other implementations default to rejecting unsupported scoped claims, never falling back to global claim.
- [GitSyncJobService](../../UniNet.Application/Services/GitSyncJobService.cs): target filters inside the existing atomic claim SQL, scoped manual enqueue/replay checks.
- [GitSyncWorker](../../UniNet.API/Services/GitSyncWorker.cs): bounded single-job consumer; shared existing execution lifecycle.

Tests:

- [GitScopedManualSyncTests](../../UniNet.Tests/GitScopedManualSyncTests.cs): no-job behavior and invalid configuration.
- [GitScopedManualPostgresTests](../../UniNet.Tests/GitScopedManualPostgresTests.cs): real PostgreSQL scoped behavior, races, rollback/recovery and history preservation.
- [Existing PostgreSQL fixture](../../UniNet.Tests/GitHubAppActivityWebhookPostgresTests.cs): made the class partial to reuse its established fake provider/data setup; added an opt-in transient failure counter with unchanged default behavior.

Also added the two configuration examples below, this report, and a latest handoff note. Existing unrelated tracked/untracked changes were retained. GitHubSyncJobProcessor, manual controller, broad readiness check, scoring, EF mappings and migrations were not changed by this feature.

## C. How the correct Manual JobId is established

### Stage 1 — enqueue only, not performed on E2E

Merge [enqueue-only overrides](config/git-scoped-manual-enqueue.env.example) into a child environment loaded from the verified E2E configuration. Keep global Enabled=false; enable ManualEnqueueEnabled and ScopedManual for the exact approved project/association; unset any inherited JobId. The consumer completes without a claim or background loops.

After explicit real-sync approval, Creator A authenticates normally and calls the existing endpoint once:

`POST /api/projects/68f98a9f-d73d-4dcf-be5c-7099b29329c0/git/repositories/25fe740d-dfbf-4859-8ce1-257247b4b3e7/sync`

The service keeps the existing manager authorization and repository/App eligibility checks. Wrong configured project/association is rejected. An active Manual request is idempotently reused. An active Scheduled/Initial/Webhook job is **not** silently reused in scoped mode: it returns GITHUB_SCOPED_MANUAL_JOB_CONFLICT. Wait/resolve through an authorized lifecycle; never change its SyncType directly.

Record the response's `job.id`; verify association and SyncType=Manual before binding it. No consumer can claim globally during this gap.

### Stage 2 — consume exact job, not performed on E2E

Use [consumer overrides](config/git-scoped-manual-consume.env.example), replacing the invalid placeholder with the actual approved Manual JobId. IOptions is fixed for a running instance, so apply this through a controlled restart/new correctly configured consumer instance; do not assume hot reload or auto-discovery of a job ID.

With a JobId pinned, manual API replay returns that same job, including terminal status; it does not create a replacement job. Wrong project/association/type/id cannot substitute another job. Global claiming through a service configured in scoped mode explicitly throws GITHUB_SCOPED_MANUAL_GLOBAL_CLAIM_FORBIDDEN.

## D. Claim and auxiliary-work exclusion

ClaimScopedManualAsync uses the **same** UPDATE ... candidate SELECT ... FOR UPDATE SKIP LOCKED transaction as normal claims, with these extra predicates **inside candidate selection before mutation**:

- association.ProjectId = approved ProjectId;
- candidate.ProjectGitRepositoryId = approved AssociationId;
- candidate.Id = approved JobId;
- candidate.SyncType = Manual.

All existing repository/credential/grant/installation/status/lease predicates remain. A Scheduled job on the same association is not claimed. The consumer does not call ClaimNextAsync globally or filter an already-mutated global claim.

Scoped execution never starts scheduled enqueue, webhook consumer, installation reconciliation, pending activation or durable global heartbeat maintenance. It does not enqueue any job on startup. The already-enqueued Manual job is the only executable target. Normal mode still consumes webhooks and completes jobs as before, verified by the existing real hosted-worker PostgreSQL regression.

## E. Authorization, locking, lease and recovery

- Keeps App grant/installation validation and advisory lock during claim, and all processor row/advisory locks, per-page authorization-generation and OAuth Version fencing.
- Does not call an unfenced processor. Every attempt comes from the scoped atomic claim and carries the real LeaseId.
- Uses the original lease heartbeat and FailAsync/CompleteAsync lifecycle. RetryScheduled is claimed only when NextAttemptAt is due; retries remain bounded by MaxRetryAttempts and the scoped deadline.
- Uses a fresh service scope/DbContext on every attempt. Observation failure was injected after page writes began: provider objects/activities/grants/visibility/evidence/observations/checkpoints rolled back, and recovery used a different DbContext and a new transaction.
- Authorization revoked after fake fetch rejects the page and persists terminal failure according to existing policy. Lease loss rejects the stale page; a fresh scoped consumer can reclaim the expired lease and complete safely.
- Completed/Failed/Cancelled/Paused durable state stops the consumer. Missing/wrong target stops with TargetMismatch without changing that target's job state.
- Deadline/shutdown cancels processing. It does not force Completed or rewrite checkpoints/status. A cancelled in-flight job can remain Running until its lease expires; a future retry can remain RetryScheduled. Recovery is explicit using the **same JobId** and real eligibility/lease rules.
- ScopedStopReason is a local execution diagnostic, not a new durable GitSyncJobStatus. ScopedJobStatus/final logs perform a bounded fresh read of persisted status; null means unavailable, not success. Existing job-status GET remains authoritative.

Cancellation is cooperative: the consumer waits for the existing processor to unwind/roll back before reporting stop. A provider that ignores cancellation can delay shutdown; the production HTTP timeout still applies. No thread kill or force-completion mechanism was added. Locally unknown external revocation remains detectable only through GitHub responses or later local authorization updates; no instantaneous detection claim.

## F. Tests — current-source final result

**98 PASS / 0 FAIL / 0 SKIP**, counted once from the final guarded run. The initial no-JobId test reproduced the missing behavior before implementation, then passed. Intermediate reruns are not counted as extra tests.

| Suite | Cases |
|---|---:|
| GitHubAppActivityWebhookPostgresTests | 30 |
| GitScopedManualSyncTests | 6 |
| GitSyncInfrastructureTests | 4 |
| GitEvidenceFactsExtractorTests | 13 |
| GitFrozenFactsTests | 3 |
| GitPeerNormalizationTests | 5 |
| ContributionCalculatorTests | 11 |
| ContributionServiceTests | 10 |
| GitExperimentalContributionServiceTests | 16 |

There are **23 new scoped cases**: 17 PostgreSQL + 6 unit. The 30 PostgreSQL cases include the 17 scoped cases and 13 existing worker/rollback/concurrency/authorization regressions. Coverage:

| Requirement | Direct verification |
|---|---|
| Normal behavior preserved | Existing normal hosted worker consumes webhook and completes both project jobs; infrastructure defaults pass. |
| No scheduled/webhook/reconciliation in scoped mode | Pinned job completes with another project job untouched; overdue association adds no job; auxiliary service resolution counters stay zero. |
| Scheduled/wrong project/association/JobId | SQL/method rejection with original Pending state and null lease/start fields. |
| Authorized enqueue/idempotency | Unknown actor and wrong scope rejected; same Manual job reused; Scheduled conflict; completed pinned job replay does not create a new job. |
| Concurrent claiming | Two PostgreSQL claims yield one valid lease; two hosted consumers retain one lease/EP-06 membership and observe completion. |
| Authorization/lease changes | Scoped revoke/lease-after-fetch tests; stale completion/heartbeat rejected; lease reclaim and fresh processing succeed. Existing OAuth replacement, App ABA, routine verification and six persisted-state tests also pass. |
| Retry | Transient provider failure retries the whole page; persistent provider failure reaches MaxRetryAttempts; future RetryScheduled remains unclaimed until due/deadline. |
| Shutdown/deadline | Deterministic provider gates, real cancellation; durable state remains Running or RetryScheduled, never fake Completed. |
| Atomicity/checkpoints | Observation write fault, complete rollback and fresh-context retry; existing SQL FK and shared-version conflict worker tests. |
| History | Previously finalized capture retains its exact empty membership after new observations; a pre-existing run's input hash remains unchanged. No production histories were used as write fixtures. |

### Disposable target and safety

- Container `uninet-scoped-manual-20261119`, ID `33d6d4f368484eca18b933494ce3e5c3db912cbe821b53397746349847d26e67`.
- Loopback `127.0.0.1:52522`, database `uninet_step5b_scoped_manual_20261119`, role postgres.
- Restored **schema plus its actual copied 85-row migration history only** from the previously validated three-migration disposable. No E2E business/account/credential rows were copied. Initial Projects count was zero. No EF migration Up/database update was executed.
- New scoped write tests require explicit opt-in, the scoped disposable name prefix, and an explicitly non-E2E port; they do not inherit the older fixture's default 55433 port. Host/port/database were verified before fixture writes. Fake providers/tokens were used; no real GitHub requests.
- Final test receipt: `%TEMP%/uninet-scoped-manual-results/scoped-release-guarded.trx`. New tests/fixture changes are in source for replay with the explicit disposable variables.

## G. Build / migration / deployment status

- Explicit current-source API build in isolated artifacts: **PASS**, zero errors, one existing CS9113 warning. Application and Infrastructure references built successfully.
- EF pending-model-changes on the explicit disposable target: **PASS**. **No schema change or migration is needed.** No new migration source, history stamping or E2E migration command.
- `git diff --check`: PASS; pre-existing compiler/analyzer/line-ending warnings remain separate from test results.
- [Broad readiness guard](../../UniNet.API/Services/GitHubDeploymentReadiness.cs) is unchanged. Scoped configuration keeps global Enabled=false; broad readiness can remain 503, and the seven Pending migrations are not hidden. Scoped validation describes only this job's execution boundary.
- Current build output: `%TEMP%/uninet-scoped-manual-build`. Existing E2E process was not stopped, restarted, replaced or enabled. This new feature is **not loaded into that running process yet**.

## H. Plan for one real E2E manual sync — approval required

1. Obtain explicit approval for controlled E2E process/configuration changes and the one live Manual sync. No real POST or worker activation occurred here.
2. Reverify target/container/history, association/grant/generation, live App access, no in-flight job and no other global workers/processes. Do not use blanket migrations or the unmodified global-worker launcher defaults.
3. Start the new isolated binary in enqueue-only configuration, with unrelated jobs still disabled. Authenticate Creator A with existing credential; keep JWT in memory; send the approved manual POST and record its returned job identity.
4. Configure that exact JobId and activate only the scoped consumer. Observe existing GET sync-job/status APIs and safe worker diagnostics until real terminal state or deadline. No scheduled/webhook/reconciliation work is consumed.
5. On Completed, inspect V4 versions/observations/checkpoints and completeness without changing prior snapshots/runs. On Failed/Paused/timeout, report exact durable state/error/NextAttemptAt and propose same-job recovery; do not fabricate completion or start another job silently.
6. Return to worker-off configuration. No automatic new snapshot, peer cohort/rating, Contribution Run or finalization. Those remain separate instructions.

## I. Remaining limitations

- This confines the configured consumer, not every process sharing the database. Operators must keep other normal/global workers off; a separate legacy global worker can otherwise claim jobs under its own configuration.
- A job can outlive a scoped execution window in a recoverable durable state; logs and status GET must be checked, not merely process exit.
- This implementation does not promise V4 completeness, contribution eligibility or a Final ContributionPercent. Evidence may remain Incomplete; no peer snapshot exists on E2E.
- Actual E2E restart/configuration activation, real manual POST, GitHub fetch and persistence are **NOT RUN** in this task and require the next approval. No readiness guard is relaxed to obtain that approval.

**READY FOR A SEPARATELY APPROVED SCOPED E2E MANUAL SYNC. Stop here.**
