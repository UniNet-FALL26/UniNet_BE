# UNINET — PROJECT A MULTI-REPOSITORY SCOPED SYNC E2E — 2026-11-23

## Result

**Both approved Project A Manual jobs completed through the official hosted worker.** No Contribution calculation, snapshot capture or finalization was performed. Backend is restored to worker-off. This supersedes the prior BLOCKED audit following the user's explicit re-link and approval to add scoped activation.

## Target and baseline

- FE clone only: `127.0.0.1:55433/uninet_github_fe_e2e_20261121`, role `uninet_test`; container `uninet-step2-concurrency`, ID `2fbe020661be4f7ae4a7fd6eac82524b6f3f33dad8ad74da7981e1ce3eb9f767`.
- Loaded only `.env.github-app.e2e`, overridden with verified CLR connection-string setter to FE clone. Root `.env`, original E2E and Neon were not connected.
- Project A `44b9e967-a830-4061-b33b-5d96ca41df07` has two currently linked associations. The old mln131 association `889d8b27-888f-4057-849a-069ec9b50365` remains unlinked and unchanged.
- Initial state of both current associations: PendingAuthorization, SyncPaused=true, no jobs. Worker-off prevented normal `RetryPendingAsync` reconciliation/activation. Normal worker re-enablement would scan pending links globally and queue Initial jobs, so it was not used.
- Installation/grants Active and not revoked, authorization generations 1 before/after. Existing jobs: 24 Completed, all Project B. No competing claim worker observed.

## Production change

Changed only these production files in this task:

- [GitHubAppRepositoryActivationService](../../UniNet.Application/Services/GitHubAppRepositoryActivationService.cs): added `ActivateScopedManualAsync`. It requires exact approved scope, manual enqueue enabled, global disabled, no pinned JobId during creation, manager authorization and pending/paused/unlinkedAt-null association. It obtains repository-scoped installation token and verifies provider repository access outside the transaction. Inside Serializable it rechecks manager authorization, locks association FOR UPDATE and repository/grant/installation FOR SHARE, compares association Version and grant/installation identities/generations, rejects existing active jobs, and atomically activates plus creates a Manual job. No Initial job conversion or raw status SQL is used. Normal `ActivateAsync` remains unchanged.
- [GitSyncJobService](../../UniNet.Application/Services/GitSyncJobService.cs): existing authenticated Manual endpoint invokes this activation only for pending scoped App links. Existing active-link/pinned replay and normal production behavior remain.
- No schema/model/migration change; EF pending-model-changes=false.

[Scoped PostgreSQL tests](../../UniNet.Tests/GitScopedManualPostgresTests.cs) now cover two repositories in one project, another project sharing a repository, idempotent enqueue, grant/installation revoke-restore during provider verification, unlink rejection, and preservation of normal Initial activation.

## Backup and disposable gate

Current protected custom-format pg_dump created before live writes:
`%LOCALAPPDATA%/UniNetE2EBackups/project-a-multi-20261123-095421/baseline.dump`.
SHA-256: `511B86FA5A95A3767C50225E86341115DC8DC0FB6DADB74854D1D975911F9BBA`.
ACL restricted to current Windows principal and SYSTEM. Archive listed successfully; full restore with --exit-on-error succeeded into dedicated PostgreSQL 17 container `uninet-scoped-activation-release`, localhost:55448, database `uninet_step5b_scoped_manual_release`, role postgres. Identity and 104-row history verified before fixture writes. No restore over live FE was performed.

The initial brand-new schema fixture lacked migration-history/immutability guard setup required by existing worker tests; those runs were not used as a live safety gate. Final restored-schema gate passed. Grant-generation tests use real revoke/restore transitions because PostgreSQL triggers intentionally override direct generation assignments.

## Actual execution

| Repository | Current association | Manual JobId | Durable result |
|---|---|---|---|
| trangtanduoc/mln131-group4 | 5313bc39-8ffc-45a6-8e18-272cdc33a4a9 | 4b1e18f3-c27e-4b25-b70a-c16499c950bf | Completed, retry 0 |
| trangtanduoc/uninet-github-app-e2e | 38135f80-f18e-48c9-a9f4-df73fe268d92 | cfbb5570-6eb0-4b91-b2eb-a8fe1c2049a3 | Completed, retry 0 |

One authenticated POST per association, Student A session checked with auth/me and API-issued session hash independently found in FE clone. No password/JWT printed or saved. Each request atomically activated and queued a Manual job; response `wasAlreadyQueued=true` reflects the existing inner enqueue finding the job just created by activation in that same request. IDs came from actual responses, not fabricated or selected by recency.

First response verification helper initially expected the wrong numeric SyncType (1); it stopped before consumer launch. Source enum and database established Manual=3, the helper was corrected, and the existing response/job was verified. **No second POST** for that association. A later combined shell command was rejected before execution by automatic review; API/verification/start steps were performed separately without execution-policy bypass.

- First job: Running `02:58:21.928374Z`, Completed `02:58:30.551985Z`, 5/5 checkpoints Completed.
- Second job: Running `02:59:38.213931Z`, Completed `03:00:04.710222Z`, 23/23 checkpoints Completed.
- Consumers ran sequentially, exact ProjectId/AssociationId/JobId pinned, global=false, manual enqueue=true, scoped=true, deadline=900 seconds. BackgroundWorkers=true only with the pinned consumer; E2E DisableUnrelatedWorkers=true excluded unrelated persistent services. Scoped branch bypassed scheduled polling/webhook/reconciliation. Existing lease, heartbeat, authorization-generation checks, page transactions and checkpoint lifecycle were retained.
- Both consumer loops recorded stop reason Completed and durable Completed. No permission/token/rate-limit failure or retry was recorded. This is not a claim of unlimited provider quota.

## Evidence obtained (new observations, not scoring activities)

| Category | mln131-group4 | uninet-github-app-e2e |
|---|---:|---:|
| branches | 1 | 3 |
| commits | 1 | 21 (10 distinct versions) |
| pull-requests | 0 | 2 |
| pull-request-commits | 0 | 5 |
| pull-request-files | 0 | 3 |
| pull-request-reviews | 0 | 0 |
| pull-request-review-comments | 0 | 0 |
| issues | 0 | 4 |
| issue-events | 0 | 6 |
| issue-comments | 0 | 2 |
| releases | 0 | 1 |
| **Total** | **2** | **47** |

38 distinct referenced frozen versions validated using the production GitSnapshotEvidenceValidator: **38 Valid / 0 rejected**, canonical hashes/source identity recomputed. Technical integrity Valid does not promote completeness.

- Commits schema 4 Complete; PR and PR-commit relations schema 4 Complete; PR files schema 3 Complete; issue comments schema 5 Complete. Branches/releases remain schema 2 Incomplete; two issue events remain Incomplete.
- Total 49 observations: 42 Complete, 7 Incomplete. Issue rows include PR-shaped items; category counts are not automatically scoring-eligible Issue counts.
- No review observations were returned; completed per-PR checkpoints provide current retrieval coverage, not proof of every deleted/historical review.
- Only **2 new immutable versions** were needed (mln131 branch/commit); 36 existing versions were reused for the shared repository. No EP-06 duplicate logical keys. Repeated commit branch-scope observations remain auditable and do not imply duplicate activity credit.
- Zero observation/job/association/repository-version relationship mismatches. No extra sync was run merely to test repeat idempotency; replay/atomicity/lease are covered on disposable.

## Preservation

Before/after full-table fingerprints: only 14 tables changed, all expected: Accounts/RefreshTokens/ChatDeviceSessions from authorized auth sessions; GitActivities/Actors/CommitBranchReferences, shared ProviderObjects, Versions, Jobs, Checkpoints, Observations, project grants/visibility and the two A associations. Other tables match.

Project B full-row fingerprints match the restored backup for its association, all 24 jobs/checkpoints, observations, all 102 pre-existing EvidenceVersions, snapshot headers/runs, activity grants and visibility. No B job enqueued/claimed, no ownership/identity/membership transfer, no scoring. Shared repository-level provider-object state may refresh through official ingestion; Project B's immutable provenance did not change.

Totals: jobs 24 -> 26, versions 102 -> 104, observations 693 -> 742. Five snapshots, four GitContributionRuns, legacy Contribution tables and Peer data retain their prior fingerprints. EF history stays 104 applied. No cohort/rating/snapshot/run creation, migration, finalization or policy change.

## Runtime / API readback

Restored using existing safe FE launcher; current API PID **19808**, `/health` **200**. Effective launcher configuration: BackgroundWorkers=false, global=false, manual enqueue=false, scoped=false, JobId empty, unrelated workers disabled. No active GitSyncJob remains. Port 5239 has one listener owner.

Authenticated `GET /projects/{A}/git/sync-status`: both associations **SyncReady**, paused=false, latestJob.status=Completed. Repository-detail DTO still says **PendingWorker** for Active/unpaused App associations because its mapping does not inspect jobs; use sync-status/latestJob for actual execution state. This contract difference should not be presented as failed linking or overwrite a known Completed job in FE.

## Contribution aggregation audit / readiness

Existing snapshot capture selects all observations through requested project's associations and matching jobs/repository versions, freezes exact membership; B snapshots/runs remain independent. No capture was invoked here.

Scoring service reads one project-scoped snapshot; project roster/identity history and Peer cohort are bound to that project. Commit SHA dedup spans repositories within a run; PR/Review/Issue use repository/object identity, exact PR overlap is handled, and partial patch/content overlap remains an explicit limitation. Timeline/ownership and source completeness checks precede points. Approved normalization and 35/30/20/15 category weights are unchanged.

**Project A has zero ProjectGitMemberIdentities and zero ownership-history records at audit time.** Do not reuse B's verified identities for A. Thus ingested technically Complete facts exist for both repositories, but verified member attribution/Contribution eligibility is not established for A. A project-scoped snapshot/input, verified per-project identity mapping/history, eligible roster/timeline and appropriate Peer inputs must be established through supported flows before an official result. No scoring was executed and no official readiness was fabricated.

## Build / focused verification

- First targeted new regression reproduced PendingActivation enqueue failure before the fix.
- Final focused run: **59 PASS / 0 FAIL / 0 SKIP**, including 5 new cases, existing scoped worker PostgreSQL cases, runtime worker registration, App grant/user-client and Git Sync infrastructure filters. Repeated runs are not added to the count. Pre-live gate was 31/0/0; normal-production activation preservation and related focused tests were included in the final run.
- Backend isolated build PASS; restored worker-off launcher build PASS, 0 errors (existing warnings remain).
- EF pending model changes=false; no migration required/applied.
- `git diff --check` PASS; existing line-ending warnings only.
- No full-suite/E2E rerun, no production/Neon, no commit/push. Existing uncommitted work preserved.

**Sync scope completed. Contribution scoring readiness remains conditional on Project A attribution/inputs and existing scoring limitations. Stop before snapshot, Peer or Contribution actions.**
