# UNINET — Contribution V2 Phase 2 result

## Latest completion — authorized automated E2E ratings / Ready V2 Draft (2026-11-20)

See [automated E2E completion](UNINET_AUTOMATED_PEER_EVALUATION_CONTRIBUTION_V2_20261120.md). User explicitly authorized A→B4/B→A5 test ratings. CohortClosed, PeerSnapshot `c6fe092b-ea1f-4e19-b04e-384a7663424b`, V2 Draft Run#4 `ad2cf570-9592-465a-bdb0-c80c5112cdef` Ready, A78.36%/B21.64%/External0=100.00%. Git snapshot unchanged; no sync/capture/migration/review/finalize. OldPendingPeer sections below are historical; **READY FOR PHASE3**, with publication policy/alternate-roster guard retained. These are controlled E2E evaluations, not production feedback.

## Latest final execution — Git Ready, real Peer still pending (2026-11-20)

See [final Phase1+2 execution](UNINET_GIT_CONTRIBUTION_PHASE1_PHASE2_FINAL.md). The subsequently approved single V5 job completed and one new policy2 snapshot `d56f2e2c-5b40-4508-8746-943ee15ae256` froze693 observations/102 versions/470 receipts. Git now Ready: A raw74/share92.50%, B raw6/share7.50%, Review verified NotApplicable. Existing cohort remains Open0/2; no Peer Snapshot or new Run/final allocation. Final PID20740 worker-off; history86/seven pending. Old inputs/runs unchanged. Original diagnostic values and earlier deployment PIDs below describe prior read points, not current Git readiness. Both one-sync/one-capture authorizations are consumed; wait for genuine ratings, not another Git sync.

## Deployment follow-up — 2026-11-20

The separately approved coverage migration is now **APPLIED / VERIFIED** on local E2E: **86 applied / 7 old pending**, empty coverage table, source data/all four old policy1 snapshots/three Runs/cohort unchanged, same worker-off PID32812/health200. See [deployment receipt](UNINET_SCOPED_COVERAGE_E2E_MIGRATION_DEPLOYMENT_20261120.md). The original implementation report below describes the earlier 85-migration baseline. No subsequent Git sync, policy2 capture, Peer submission/closure or Run was authorized/executed by this deployment.

## Status

**PARTIAL — deployed, one real cohort Open; waiting for real member submissions and approved Git data/schema actions.** No ratings were sent by the agent. No close, Peer Snapshot, Run V2, finalization or publication occurred. Do not turn missing components into zero or publish a 100% allocation.

## A. Git

Read-only current-source diagnostic still uses snapshot **`467894ae-e5ed-41c9-af7f-9095e581e5a4`**, cutoff **2026-11-19 15:54:46.221595 UTC**, 646 observations / 100 versions. No capture or new sync in this phase.

| Member | Raw Commit | Raw PR | Raw Review* | Raw Issue* | Provisional raw GitScore | Ready GitScore / GitShare |
|---|---:|---:|---:|---:|---:|---|
| Student A | 100 | 100 | 0 | 75 | **76.25** | null |
| Student B | 0 | 0 | 0 | 25 | **3.75** | null |

*Review's arithmetic zero is not verified NotApplicable. Issue reflects the eligible subset only. Git remains **Incomplete**: Commit/PR eligible subset counts=3/2; Review count=0 with `EmptyCategoryCoverageNotFrozen`; Issue count=4 with `IncompleteSourceEvidence`. The nine scored activities and [Phase 1 observation trace](UNINET_GIT_SCORING_V2_PHASE1_OBSERVATIONS.csv) are unchanged. B's pure merge has no standalone CommitScore. PR-shaped issues and their children remain excluded from Issue; two missing EventKind values were not inferred. No new eligible External contributor was established; unknown actors and the GitHub automation committer are not reclassified as External.

Snapshot membership hash remains **`ab8ce4342fa945f59c34ba7016c4a78818fc0f0c4bce3a8b8b307c7d25f56833`**. Enhanced diagnostic input hash is **`5f965bcba4878b0b2241f50b202223556bf3c0448114cabf4fd0e7d9269b6fbe`**: its context now explicitly contains frozen-coverage metadata (empty for this legacy snapshot). This is a new diagnostic artifact, not an update to a snapshot or Run hash. The official reader still validates historical frozen facts and current project/repository access; no mutable provider payload was scored.

### Implemented future-readiness enhancements

**Frozen retrieval coverage, capture policy 2**

- New `ProjectGitEvidenceSnapshotCoverage` stores exact association/job/checkpoint, source category/scope, item count, scope/job completion times and receipt-policy version. It is retrieval metadata, not a synthetic GitHub activity and not a fabricated zero-review evidence object.
- The existing capture service reads completed source jobs/checkpoints at the **same Repeatable Read MVCC point as membership**, then inserts header, membership and receipts and finalizes atomically. Late/uncommitted state cannot retroactively enter the finalized snapshot. Source completion times additionally respect observation cutoff; timestamps alone do not freeze membership.
- Four restrictive composite FKs enforce header/project, association/project, job/association and checkpoint/job identities. New alternate keys are `(GitSyncJobs.Id, ProjectGitRepositoryId)` and `(GitSyncCheckpoints.Id, SyncJobId)`. PK `(SnapshotId, GitSyncCheckpointId)` and a unique snapshot/job/category/scope index prevent duplicate receipts.
- Source-validation trigger requires a matching completed job/checkpoint, exact category/scope/count/times, matching association repository, policy-2 open snapshot and cutoff. Fabricated metadata is rejected with **23514**. The existing snapshot entry guard makes receipts immutable: update/delete and insert after finalization are rejected with **55000**. Down refuses populated receipt history; no destructive Down rehearsal was needed.
- Reader uses **persisted receipts**, never live checkpoints to reconstruct an old snapshot's coverage. Coverage associations pass the same local authorization/locking as facts; hidden coverage also makes the read restricted. Unsupported capture policies fail explicitly.
- Verified empty Review requires a frozen successful all-PR enumeration; exact PR evidence/count and same association/job; a zero-item completed review receipt for **each** PR scope; supported receipt version, cutoff and unrestricted access. Missing/nonempty/mismatched scopes, conflicting latest identities or newer partial-job observations cannot establish empty coverage. Only this proof permits Review NotApplicable; empty membership alone does not.
- Existing policy-1 snapshots remain policy 1 and have **no** coverage proof. If the new table is absent, legacy capture remains explicitly policy 1 and cannot claim the new proof. This preserves legacy APIs without bypassing readiness. No old snapshot was backfilled, relabeled or edited. Complete absence of all Git work remains conservatively unproven unless its full component-level evidence is established.

**Issue comment frozen contract V5**

- Future fetched `issue-comments` use schema 5: actual numeric provider actor ID, valid CreatedAt and positive issue scope; supplied timestamps must be coherent, and future creation remains Incomplete. No body, login, secret or arbitrary payload is added to frozen facts. Parent PR/Issue type is still independently resolved from frozen project evidence before scoring.
- Processor invokes this extractor in the **existing atomic writer/page transaction**. Version reuse/canonical SHA-256, observations, scope, lease, authorization-generation, rollback and checkpoint mechanisms remain in place. No endpoint/retrieval expansion or alternative writer.
- Schema 5 is limited to this typed comment contract; canonicalization stays version 1. Schemas 1–4 retain their existing bytes/semantics. Reader/validator support V5, but **the old V4 Incomplete comment is not promoted**.
- B's real-issue comment `6656546011` has actor/time/scope in historical facts but retains `CategoryCoverage` / Incomplete. That flag cannot be erased by a scorer. A fresh authorized fetch must produce a new supported version/observation. Missing EventKind remains unsupported; no event name or new points were invented.

**Current E2E has no V5 facts or policy-2 snapshot.** These features are deployed/tested code, not a claim that the current frozen input has become Ready. Partial-patch allocation and historical force-push limitations remain; weights, attribution, External rules and scoring policy were not changed to make results Ready.

## B. Peer — actual E2E cohort

| Field | Actual value |
|---|---|
| CohortId | **`5f798512-6bce-4c66-b58d-c3d5b859fae7`** |
| ProjectId | `68f98a9f-d73d-4dcf-be5c-7099b29329c0` |
| OpenedById | Creator A profile `fef4ef4d-0049-4f7a-b629-fe9760bbbff9` |
| CreatedAt | 2026-11-19 16:56:21.211904 UTC |
| Frozen roster | A + B (`8791ff0a-38a4-44e2-97f3-3ef8fd0d2202`), two participants |
| RosterHash | `52ac8c0ff32c5c1bf389d5e4993f41c9834df62af9fa8d5665df7aff67bbf3eb` |
| Status | **Open** |
| Submitted / Required | **0 / 2** |
| Revisions | **0** |
| PeerSnapshotId / PeerScore | **null / pending** |
| Missing | A→B and B→A, both genuinely member-submitted |

Exactly **one authenticated POST**, HTTP **200**, to the existing cohort API after deployment/preflight. ID comes from the actual response and was joined back to the persisted project/opener/roster. A durable attempt marker prevents accidental retry; OpenAsync has no request idempotency key, so a lost response must be reconciled from actual data before any repeat. No direct SQL insert or duplicate cohort.

Auth A/B and authenticated project/repository reads passed. Student B used the newer protected credential directory; no password reset. Existing Draft Run #3 remained readable by A (200) and denied to B (403). Cohort GET passed for A/B; anonymous GET returned 401. B's JWT was used only for authorized read checks, **never a rating**. Passwords/JWTs were not printed/persisted.

Existing peer service already enforces Creator inclusion, active Student project membership, score 1–5, evaluator from JWT, no self-review, unique pair, expected revisions/history and managing-Creator-only close. The existing calculator rejects incomplete all-to-all input; close locks the cohort and freezes the Peer Snapshot transactionally. Members see only outgoing ratings; managing Creator can inspect ratings/history under current policy. With two people, inference anonymity cannot be guaranteed. These policies were retained, not broadened.

**[Member submission instructions and exact A→B / B→A endpoints](UNINET_PHASE2_PEER_SUBMISSION_GUIDE.md).** Each member chooses and submits their own rating in their own authenticated session. No agent-suggested/default rating. The cohort is not auto-closed when complete.

## C. Contribution

No RunId or PeerSnapshotId exists for this phase. **GitShare, PeerShare, FinalContributionPercent, External allocation and percentage total remain null/unavailable.** Do not report 0% or 100% from missing data.

Unchanged `GitPeerSeparate60_40V1`: normalize eligible Git member+External mass separately from closed-snapshot member Peer mass, combine 60/40, retain External's Git-only portion, use deterministic largest remainder. Transfer weight only for a proven NotApplicable top-level component. Incomplete and applicable zero-mass inputs cannot donate weight. Real minimum ratings may produce zero peer mass/InsufficientData; do not fabricate positive evaluations to avoid that outcome. Regression tests exercise complete member-only/member+External totals of 100.00% and blocked incomplete/zero-mass cases; no actual E2E percentage allocation was created.

The operational gate for this phase is closed: Peer is Open/missing two real submissions and the selected Git snapshot remains Incomplete. No Run creation request was sent simply to inspect an incomplete draft. Closed Peer alone will not repair the frozen Git snapshot.

## D. Build, tests, schema and deployment

### Preflight / runtime

- Verified only `127.0.0.1:55433/uninet_github_app_e2e`, user `uninet_test`, container `uninet-step2-concurrency` / ID `2fbe020661be4f7ae4a7fd6eac82524b6f3f33dad8ad74da7981e1ce3eb9f767`. All 102 application-table fingerprints matched Phase 1 before mutation. Project Completed, A Creator/B member, active linked/unpaused App association, ownership/grant generations unchanged, no active job.
- Correct old PID **24848** and binary/hash/start time/listener were checked and it exited gracefully. New verified PID **32812** runs current source from isolated `uninet-phase2-build/bin/UniNet.API/debug/`.
- API SHA-256 `06F5814834505F0337949076243850CB118E1D353ECA1E93CB51B8A6B3CBFA5C`; Application SHA-256 `831DB1377BF026ADED53BEC3805DD02820599FADEDE9B560B67360B8D00669AB`.
- Global=false, ManualEnqueue=false, Scoped=false, JobId absent from environment/explicitly empty CLI; unrelated workers off; generic env loading skipped. Original E2E env/launcher/watchdog unchanged. Unique API/listener on 5239. Health **200**, broad GitHub readiness **503**, existing meaning/guards retained.
- After startup **all 102 tables unchanged**. After authorized A/B logins and the cohort POST, only Accounts, RefreshTokens and ExperimentalPeerCohorts changed; other **99 tables unchanged**. No background Git activity, scheduled enqueue, webhook consumption or reconciliation.

### Additive migration — NOT applied on E2E

- New [20261119164008_AddProjectGitSnapshotCoverageReceipts](../../UniNet.Infrastructure/Data/Migrations/20261119164008_AddProjectGitSnapshotCoverageReceipts.cs), its generated Designer and model snapshot. One new coverage table, two supporting alternate keys, restrictive FKs/indexes/check and source/immutability triggers. No existing facts/observations/run/cycle update/delete or fabricated/backfilled coverage.
- [Scoped idempotent review SQL](../migrations/ProjectGitSnapshotCoverageReceipts.review.sql) contains only this new migration, generated from the previous V2 readiness migration. It is a review artifact, **not permission to execute on E2E**. Idempotent reapply passed on disposable.
- E2E history stays **85 applied**. Seven older unrelated migrations remain pending; **one additional scoped coverage migration is now pending in source**, total eight. No blanket database update, history stamping, old migration edit or E2E DDL. Model-vs-migration consistency is clean; that does not mean this partially migrated E2E database is fully current.
- New API works against the old schema for peer operations and policy-1 snapshot reads (verified). Capture without the new table stays policy 1 and cannot prove empty coverage. Apply the reviewed receipt schema before requesting policy-2 capture; readers must support V5 before enabling writers that produce it. Do not mix a pre-Phase-1/V5-unaware reader with new writers. No auto-migration was added.

### Tests

**247 unique PASS / 0 FAIL / 0 SKIP**, including **89 real PostgreSQL cases**. The full regression ended 246 PASS/1 FAIL because an existing hosted FK-fault test encountered a genuine **40001** before its intended FK insertion; standalone rerun confirmed **23503**, precise FK rejection and full rollback. Latest outcome aggregation counts that test once. No worker error policy was changed to mask this conflict. Feature run and earlier focused reruns are not added to the total.

Coverage includes: peer authorization/Creator roster/self-review/revisions/privacy, concurrent edit+close/immutable peer snapshot, normalization/External/largest remainder, V1 lifecycle/idempotency, V5 allowlist/missing/future facts, real writer→snapshot V5 ingestion, policy-1 old-schema compatibility, policy-2 source receipt validation and immutability, upgrade preserving an existing V1 run/snapshot/facts, attribution/unknown/conflicts, PR/Issue boundary and existing writer/lease/ABA/replay/recovery regression.

PostgreSQL 17 disposable: `uninet-scoring-phase1-disposable`, ID `724f4624bbab39993877d0a9a05c22d5791da918da297d9af7a29df81d71c0ed`. Restart changed its actual port to **63405**; this was reverified before all test writes. User postgres and explicit disposable database names; **no test fixture writes on 55433**. Fresh chain=93; existing-data upgrade from92=PASS, no automatic coverage backfill; legacy capture on92=policy1/PASS; snapshot-upgrade baseline88=PASS. Fixture database resets were confined to this verified disposable container. Container stopped after testing.

API/Application/Infrastructure build **PASS** using isolated artifacts; EF `migrations has-pending-model-changes`: **no changes**; `git diff --check`: **PASS**. Existing nullability/API-unused-parameter/test analyzer and LF/CRLF notices remain warnings, not test failures.

### Source files changed in this phase

- [Frozen facts](../../UniNet.Domain/Entities/GitFrozenFacts.cs), [validator](../../UniNet.Domain/Entities/GitSnapshotEvidenceValidator.cs), [snapshot entity/coverage](../../UniNet.Domain/Entities/ProjectGitEvidenceSnapshot.cs).
- [Extractor](../../UniNet.Application/Services/GitEvidenceFactsExtractor.cs), [existing processor dispatch](../../UniNet.Application/Services/GitHubSyncJobProcessor.cs), [capture](../../UniNet.Application/Services/ProjectGitEvidenceSnapshotService.cs), [reader](../../UniNet.Application/Services/ProjectGitEvidenceSnapshotReader.cs).
- [Coverage proof](../../UniNet.Application/Services/GitSnapshotCoverageValidation.cs), [V2 readiness](../../UniNet.Application/Services/ExperimentalGitScoringV2.cs), [run inputs](../../UniNet.Application/Services/GitExperimentalContributionService.cs), [diagnostic](../../UniNet.Application/Services/GitExperimentalContributionDiagnostics.cs).
- [Snapshot configuration](../../UniNet.Infrastructure/Data/ProjectGitEvidenceSnapshotConfiguration.cs), new migration/Designer/model snapshot and review SQL.
- [Coverage/V5 unit tests](../../UniNet.Tests/GitSnapshotCoverageValidationTests.cs), [validator test](../../UniNet.Tests/GitSnapshotEvidenceValidatorTests.cs), [snapshot PG](../../UniNet.Tests/ProjectGitEvidenceSnapshotPostgresTests.cs), [writer PG](../../UniNet.Tests/GitHubAppActivityWebhookPostgresTests.cs), [Contribution PG](../../UniNet.Tests/GitExperimentalContributionPostgresHttpTests.cs).
- This report, submission guide and latest handoff. Existing uncommitted work retained; no commit/push. Peer service/controllers and normalization/calculator policy were reused, not rewritten.

## E. Remaining work / Phase 3

1. **Student A/B:** independently send the two real reciprocal ratings using the existing cohort. Currently 0/2; no close until genuine complete input. Do not share credentials/tokens with the agent or choose scores to force allocation.
2. **Creator/authorized flow:** verify roster, all-to-all input and revision history, then close the cohort through the official leader API. This remains pending, not simulated. Peer can be completed independently of Git readiness.
3. **Approval required:** review/apply **only the new coverage migration** on E2E with scoped SQL and preservation preflight; do not run the seven unrelated migrations. This task has not applied it.
4. **Separate approval required:** one new scoped manual sync through the official pinned pipeline to fetch new V5 comment facts. No new endpoint is needed. The current old Incomplete version cannot be upgraded by inference. Global worker must remain off.
5. **New capture approval required:** after a successful approved sync and deployed receipt schema, capture a fresh policy-2 snapshot to freeze both new facts and consistent scope receipts. Do not mutate the four old snapshots. Then rerun Git diagnostics and keep any genuine remaining limitations.
6. **Conditional V2 run:** only once Git and real closed Peer inputs satisfy the phase's readiness conditions, use the existing API to create an idempotent V2 draft and verify 100.00% allocation. No finalize/publication. Known general partial-patch/force-push policy limits are not resolved by an empty-review receipt.
7. **Phase 3 publication policy:** legacy cycle is A-only; alternate A+B runs remain barred from review/finalize by the unchanged official-roster guard. An explicit approved publication path is still needed without editing that legacy cycle or old runs.

**Phase 3 is NOT ready.** Runtime/cohort preparation and future-data enhancements are complete; real evaluations and approved Git schema/sync/capture actions remain. No additional acceptance phase is proposed.

Local nonsecret receipts: `C:/Users/ADMIN/AppData/Local/UniNetE2EBackups/contribution-v2-phase2-20261119-235021/` (preflight/fingerprints, actual cohort request/response/DB row, HTTP statuses, diagnostics, runtime hashes, TRX/latest test outcomes and final status). Passwords, JWTs, installation tokens/private keys and member-private ratings/comments were not logged in handoff artifacts.
