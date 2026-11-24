# UNINET — PROJECT A SCOPED SYNC AUDIT — BLOCKED (2026-11-23)

## Scope and execution

Requested project: `44b9e967-a830-4061-b33b-5d96ca41df07`; repository: `trangtanduoc/mln131-group4` (provider repository ID `1137559788`). Audit only; no live database mutation, activation, enqueue, consent, sync, scoring, migration or runtime restart performed. Existing uncommitted changes preserved.

Verified database identity by read-only SQL: `127.0.0.1:55433/uninet_github_fe_e2e_20261121`, role `uninet_test`; Docker `uninet-step2-concurrency`, ID `2fbe020661be4f7ae4a7fd6eac82524b6f3f33dad8ad74da7981e1ce3eb9f767`. 104 applied migrations. Existing runtime receipt selects this FE clone, ConnectionTargetVerified=true, workers off; listener PID 13820, `/health` 200. No root `.env`, original E2E or Neon connection used.

## Actual association state — first blocker

Association `889d8b27-888f-4057-849a-069ec9b50365` belongs to Project A and the requested repository, but **UnlinkedAt = 2026-11-23T02:21:09.679174Z**. AccessStatus=PendingAuthorization (0), SyncPaused=true. There is no currently linked association for this repository/project. Its historical row must not be silently reactivated or have UnlinkedAt cleared.

Project A's other association `38135f80-f18e-48c9-a9f4-df73fe268d92` points to `trangtanduoc/uninet-github-app-e2e`; this is not the approved sync target. Project B's association `25fe740d-dfbf-4859-8ce1-257247b4b3e7` also points to that other repository and is outside scope.

No jobs exist for Project A or the target association. All 24 existing jobs belong to Project B and are Completed; no active jobs were found. No new JobId exists for this task. New Commit/PR/Review/Issue observations from this task: **0 each**. Sync was not executed, so ingestion idempotency/completion/readiness cannot be claimed tested.

## GitHub authorization (live read only)

Stored App user credential is not expired. Using the existing protected key ring with automatic key generation disabled, the audit decrypted credentials only in memory. GET `/user` returned 200 and matched the stored numeric actor ID. GET `/user/installations/181414567/repositories` returned 200; the target repository was found with pull=true/admin=true. Local installation is Active, VerificationUnknown=false; target grant `79743bff-2d72-44a1-813c-2c0dabbc1c69` is not revoked, grant/installation generation=1.

This establishes current user-to-installation repository visibility and local grant validity. A fresh repository-scoped installation token/provider read was not exercised by activation because the association is unlinked; do not describe this as a completed sync or full activation verification. No token/private key/connection string was printed or persisted in the report.

## Activation/scoped execution — second blocker

- [Activation](../../UniNet.Application/Services/GitHubAppRepositoryActivationService.cs) returns false if GitHubSync.Enabled=false; its candidate query excludes UnlinkedAt!=null.
- Successful current activation atomically changes PendingAuthorization/paused to Active/unpaused and creates an **Initial** job, not Manual.
- [Manual enqueue](../../UniNet.Application/Services/GitSyncJobService.cs) requires an unlinkedAt-null, Active, unpaused association, valid grant and Active verified installation. Sync settings cannot promote a PendingAuthorization App link to Active.
- [Scoped options](../../UniNet.Application/Services/GitSyncOptions.cs) require global Enabled=false; setting both global and ScopedManual true is rejected.
- [Scoped worker](../../UniNet.API/Services/GitSyncWorker.cs) accepts only the exact ProjectId/AssociationId/JobId with SyncType=Manual. It cannot consume the Initial job produced by activation. The claim query also filters before mutation; it does not claim global work and then discard it.
- [Runtime registration](../../UniNet.API/Services/RuntimeWorkerRegistration.cs) requires BackgroundWorkers.Enabled=true to register GitSyncWorker. E2E DisableUnrelatedWorkers=true prevents registration of unrelated persistent workers. Scoped execution branches before scheduled enqueue, webhook and App reconciliation loops. JobId=null makes the consumer idle. A valid pinned consumer has a bounded deadline, existing lease/heartbeat, retry/page transaction/authorization fencing and durable stop-state reporting.

Therefore toggling flags cannot safely activate and manually consume this pending App association using the current scoped path. Global sync was not enabled, and direct SQL status changes or direct unfenced processor calls were not used.

## Minimum continuation

1. Confirm the intent to re-link this previously unlinked repository through the existing authorized application link flow; retain the historical unlinked row.
2. Add the smallest scoped-manual activation path to the existing activation/manual-enqueue services: exact approved project/association, manager authorization, provider verification outside the transaction, authorization revalidation and atomic Active/unpaused transition plus a **Manual** job. Preserve normal activation's Initial behavior. Do not manufacture an Initial-to-Manual status/type edit.
3. Prove rejection of wrong project/association and unrelated/non-Manual jobs in isolated tests. Do not enable global worker as a workaround.
4. Before live writes: create a current protected FE backup and verify it, establish fingerprints, verify no competing worker and exact effective target. No new backup was needed for this stopped read-only audit; backup precondition for any future write remains unmet.
5. With supported scoped activation available and target linked: enqueue once, verify real JobId, run pinned consumer with BackgroundWorkers=true / DisableUnrelatedWorkers=true / global=false, bounded 900-second execution; restore worker-off afterward. No live operation in this plan was executed.

## Contribution V2 source audit (no calculation executed)

- [Capture service](../../UniNet.Application/Services/ProjectGitEvidenceSnapshotService.cs) joins observations through association.ProjectId, jobs and matching repository versions, and freezes exact entries. It supports multiple associations for one project, including retained historical audit. Audit inclusion does not grant eligibility/private-read access.
- [Contribution service](../../UniNet.Application/Services/GitExperimentalContributionService.cs) reads a project-scoped frozen snapshot and verifies snapshot.ProjectId. It aggregates its activities into a project run, not separate official per-repository scores.
- [Historical attribution](../../UniNet.Application/Services/GitHistoricalAttributionService.cs) filters by project and numeric provider actor ID, verified connection/history cutoff, roster and ownership conflicts. It does not infer ownership from login; multiple historical owners fail closed. Verified historical attribution intentionally allows qualifying pre-link activity, without backdating OAuth/LinkedAt.
- [Scoring](../../UniNet.Application/Services/ExperimentalGitScoringV1.cs), reused by V2 filtering, deduplicates commits by SHA across repositories; other objects include repository/category/object identity. Exact PR related-commit sets are deduplicated; PR-covered commits are excluded from duplicate credit. Partial overlap is explicitly unsupported/excluded, not proved safe by a successful sync. Content-equivalent commits with different SHAs are not guaranteed deduplicated by this implementation.
- [V2 filtering](../../UniNet.Application/Services/ExperimentalGitScoringV2.cs) handles merge/parent facts, PR-vs-Issue separation and commit timeline before scoring. Raw policy/eligibility remains experimental and limitation-bearing.
- Category weights are Commit 35%, PR 30%, Review 20%, Issue 15%. [Final normalization](../../UniNet.Application/Services/GitPeerNormalization.cs) is GitShare 60% + separately normalized PeerShare 40% when both complete/applicable, including external Git mass. Incomplete/zero-mass guards prevent fake final percentages; NotApplicable redistribution and deterministic allocator follow the approved policy. This is source verification, not a new official score or completed Run.

## Preservation / test evidence

Read-only counts: 102 EvidenceVersions, 693 Observations, 5 Git snapshots, 4 GitContributionRuns, 24 completed jobs. Source snapshots/runs/history, identities/memberships and Project B were not mutated. Existing fingerprints for evidence, observations, snapshot headers, runs and jobs match previous read-only audit receipts. No new auth login/session was performed. Authenticated repository API request was not made: the definitive unlinked database row blocks execution before login/writes; only unauthenticated health and direct provider GETs were exercised.

Operational checks: database/container identity PASS; API health PASS; provider credential/repository GETs PASS. No production code modified. No test suite or Backend build was necessary for a source-only stopped audit; temporary read-only audit helper compiled/executed using isolated artifacts (initial invocation used a missing default assets location, rerun with its existing isolated artifacts succeeded). Live sync/ingestion/idempotency: **NOT RUN — BLOCKED**.

**Final status: BLOCKED before live writes.** Resolve explicit re-link intent and scoped pending-App activation support before Contribution Frontend treats this repository as synced. No commit/push.
