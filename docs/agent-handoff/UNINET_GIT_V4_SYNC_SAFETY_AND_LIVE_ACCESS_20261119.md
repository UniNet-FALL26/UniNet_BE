# UNINET — Git V4 live access / scoped sync safety

## Outcome

**Live installation/repository access PASS. Strict scoped manual execution BLOCKED by the current worker control surface. No sync started.** No source guard, migration, identity mapping, historical evidence, snapshot, Run or peer data was modified. Backend remains running at localhost:5239, PID 28292, GitHubSync disabled.

## A. Live GitHub access verification

Verified the E2E target and its association/project/repository/grant relationships using an explicitly read-only PostgreSQL connection. Creator A is the authorized project actor. Installation/grant were locally Active, not revoked, VerificationUnknown=false, generations=1. Verified the actual configured App-enabled state before probing.

Used the existing production provider implementations in a standalone trusted-backend diagnostic; no replacement credential logic or sync writer was created:

1. [ProbeInstallationAsync](../../UniNet.Application/Services/GitHubAppInstallationReconciliationService.cs) performed the official GitHub installation GET. Result Active; remote AppId and OwnerId matched configuration/persisted authorization.
2. [GitHubAppInstallationTokenProvider](../../UniNet.Application/Services/GitHubAppInstallationTokenProvider.cs) successfully issued a short-lived token restricted to repository ID `1446877283`. Token issuance is a GitHub credential operation, not a commit/PR/issue/review activity.
3. A read-only `/installation/repositories` request using that token confirmed **exactly one permitted repository**, matching the requested numeric ID.
4. [GetInstallationRepositoryAsync](../../UniNet.API/Services/GitHubProviderClient.cs) confirmed `trangtanduoc/uninet-github-app-e2e`, CanRead=true, Private=false, Archived=false. No activity pages were fetched or ingested.

Four live assertions PASS. Tokens/JWT/PEM remained in process memory; no secrets were printed or persisted. No reconciliation ApplyResult, activation, grant refresh, job creation or DB update was called. Read-only fingerprints confirm all 96 application tables unchanged.

### Expired App user credential is a separate lifecycle

The stored App user discovery credential expired at `2026-11-13T15:17:13.175363Z`. [Grant discovery](../../UniNet.Application/Services/GitHubAppGrantService.cs) checks that expiry before its user-authorized installation listing, so that path needs reauthorization. This is not proof that the GitHub App installation itself has lost access.

Current App sync eligibility uses the existing valid installation/grant and newly issued repository-scoped installation token, not that expired discovery token. Installation-account authorization is still retained and not revoked. No user reauthorization, OAuth identity remapping or credential replacement was attempted or required merely to perform this successful installation probe.

## B. Scoped sync safety and readiness

### Current persisted/runtime state

- Database `127.0.0.1:55433/uninet_github_app_e2e`, container `uninet-step2-concurrency`, user `uninet_test`.
- History remains 85 rows; the three deployment migrations remain applied; seven unrelated migrations still Pending. No migrations were run.
- Exactly one linked association, installation and grant exist. Association is `25fe740d-dfbf-4859-8ce1-257247b4b3e7`, in project `68f98a9f-d73d-4dcf-be5c-7099b29329c0`, Active/unpaused/linked.
- 22 sync jobs, all Completed; no Pending/Running/RetryScheduled jobs. Seven App webhook deliveries are Processed.
- Latest successful sync finished `2026-11-18T13:50:30.478116Z`. Its six-hour interval elapsed at `2026-11-18T19:50:30.478116Z`; the repository is now due for automatic polling.
- `/health`=200. `/health/github/readiness`=503. The active child environment has GitHubSync=false, unrelated workers disabled and assessor tasks disabled. The env file's original Enabled=true is overridden only in this child; do not launch its defaults blindly.

### Broad deployment readiness versus job safety

[GitHubDeploymentHealthCheck](../../UniNet.API/Services/GitHubDeploymentReadiness.cs) returns Unhealthy immediately when sync is disabled; it also requires zero Pending migrations before checking token-protector round-trip. Enabling sync alone would still leave that broad check unhealthy because seven unrelated migrations remain Pending.

The real [processor schema guard](../../UniNet.Application/Services/GitHubSyncJobProcessor.cs) explicitly requires the EvidenceVersioning, CrossRepositoryIntegrity and AuthorizationGeneration migrations. All three are present. App availability, association/repository identity, authorization generation, row/advisory locks, lease fencing, atomic page persistence and checkpoint commit guards remain in place. The seven unrelated migrations are not referenced by that processor schema guard. The successful current Project/Git reads from the deployment report remain relevant; broad deployment convergence is a separate condition.

This distinction does **not** authorize ignoring a guard or claiming broad readiness PASS. No endpoint/processor guard was changed, suppressed or worked around to execute a job.

### Concrete execution blocker

[RequestManualSyncAsync](../../UniNet.Application/Services/GitSyncJobService.cs) refuses enqueue while the same global Enabled flag is false. But turning it true starts [GitSyncWorker](../../UniNet.API/Services/GitSyncWorker.cs), whose existing behavior is:

- global ClaimNextAsync, without a requested association/job filter;
- EnqueueDueScheduledJobsAsync whenever no job is claimed;
- App reconciliation, pending activation, webhook consumption and heartbeat loops while App integration is enabled.

[GitSyncOptions](../../UniNet.Application/Services/GitSyncOptions.cs) has no manual-only mode, separate worker/enqueue switch or association filter. Concurrency=1 does not turn these loops off. Starting the stock worker before the manual POST can immediately enqueue an overdue **Scheduled** job; the manual request can then coalesce into that job. Worker startup alone therefore has write/fetch effects beyond the requested explicitly started Manual job.

There is only one association today, so this is **not a claim of an existing cross-project data leak**. It is a demonstrated inability to enforce the requested strict execution boundary with the current control surface. New webhook/polling work could also be consumed while the worker remains enabled. I did not enable the worker to reproduce those side effects against E2E; source and the persisted due time already establish them.

### Smallest bounded solution proposed — not implemented

Add an optional restrictive execution mode to the **existing** job service/worker:

1. Separate permission to enqueue through the existing authenticated manual API from starting unrestricted background loops.
2. Pin claim/execution to the approved association and resulting Manual JobId; reject mismatched project, repository, association or SyncType. Keep ordinary mode unchanged.
3. Suppress scheduled enqueue, webhook consumption, reconciliation/activation loops in that scoped execution mode. Do not turn off processor authorization revalidation; a detected authorization loss must still fail closed.
4. Reuse existing lease heartbeat/fencing, fresh DbContext/transaction recovery, bounded retry and worker error-state handling; do not directly force job status or call an unfenced processor.
5. Stop the scoped consumer on terminal job state or a bounded execution deadline. A still-Pending/RetryScheduled/Running job is not reported as Completed.
6. Keep broad readiness and required-schema guards unchanged. Do not insert migration history or apply seven unrelated migrations to make health green.

Approval is needed for this limited worker-control change before implementation. After focused disposable verification, the exact subsequent sync action would be: start the scoped E2E mode, call the existing authenticated `POST /api/projects/68f98a9f-d73d-4dcf-be5c-7099b29329c0/git/repositories/25fe740d-dfbf-4859-8ce1-257247b4b3e7/sync` once, record its JobId/idempotent response, process only that job with existing guards, await a determined result, then return to worker-off state. **That POST has not been sent.** No new public endpoint is proposed.

## C. Sync execution status

**NOT STARTED.** No enable/restart/enqueue, no scheduled job, no redelivery and no checkpoint advancement. All existing 22 jobs remain Completed. No task is left Pending/Running by this audit.

## D. Evidence V4 coverage / limitations

Current database still has **0 V4 versions**, 65 V2 and 6 V3 versions, 599 observations. Old snapshot `7b8f784b-e69f-423f-9487-0cf48c4b03d9` has not changed.

- Parent/tree facts, pure-merge classification and V4 PR/Issue boundaries cannot be audited as newly ingested evidence until a successful fresh fetch/persist.
- Existing PR-commit/patch evidence and older incomplete relationship facts are retained; their existence is not retroactively upgraded to V4 completeness.
- Review absence in old evidence is not proof of complete live review coverage. No live Review/Issue/Commit activity retrieval was performed in this pre-approval access probe.
- A/B numeric GitHub identities still match active OAuth connections; ownership history records one distinct owner for each actor in this project. No new historical attribution was calculated from live metadata.
- External eligibility remains unassessed for fresh evidence; unknown/conflicting identities must not be turned into External merely to obtain a score.
- Existing missing-fact, overlap, unsupported-event and coverage limitations remain. No artificial timestamps, relationships or completeness upgrades.

## E. Snapshot

**No new snapshot.** Snapshot count remains three. New capture is gated on successful authorized sync completion, followed by frozen hash/version/association/cutoff validation. Old snapshots are immutable and were not rewritten or backfilled.

## F. Experimental V2 readiness

No new Run; count remains three. Git V2 schema/normalization exists, but fresh V4 evidence has not been ingested and there is no closed Peer Snapshot. No Final ContributionPercent can be published from this step. Old Runs #1–#3 and the legacy cycle remain unchanged.

## G. Peer Cohort A+B

No cohort, rating, peer snapshot or close operation. Next peer action is a separately approved A+B cohort opening, including Creator A; each authenticated member must submit their own real evaluation. Existing leader-only close, no self-review, revision/uniqueness/privacy protections apply. This does not authorize official finalization of the alternate roster against the old A-only cycle.

## Verification / files

- Four live installation/token-scope/repository assertions PASS; isolated diagnostic build PASS with existing project warnings and one scratch nullability warning.
- Fingerprints: all 96 application tables identical to the post-auth deployment baseline; no login/session mutation was needed for these diagnostic calls.
- Health 200 and broad readiness 503 observed. Source/state audit found the scoped-control blocker; no new unit suite was run because production behavior was not changed.
- Live probe receipt retained beside backup as `live-installation-probe-20261119.txt`; no secrets in it. No production source/config/launcher/watchdog was edited. This report and handoff note are the only repository changes from this task.

**Final: LIVE ACCESS PASS / SCOPED MANUAL EXECUTION BLOCKED. Stop before the worker-control change or any sync until explicitly authorized.**
