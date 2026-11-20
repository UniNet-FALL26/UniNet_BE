# UNINET — PHASE 3 OFFICIAL CONTRIBUTION V2 BACKEND

Date: 2026-11-20. **BACKEND IMPLEMENTED — DEPLOYMENT PENDING APPROVAL.**

All three requested Backend components are implemented: persisted default Official V2 for new projects, official review/finalization/publication, and legacy/experimental compatibility. Source/contract/migration and relevant tests are verified. **No new schema or binary was deployed on E2E. No E2E Run was reviewed, finalized, published, recalculated or replaced.** No Frontend files were opened or changed.

## 1. Architecture and workflow selection

The official service reuses the existing Git snapshot capture/authorized reader, historical validator/attribution, frozen Peer service, Git Scoring V2 and `GitPeerSeparate60_40V1` normalization. No second scoring engine, Git retrieval engine or alternative evidence writer was created.

`ProjectContributionWorkflows` makes selection durable:

- New Project INSERT after schema enablement gets **OfficialV2 atomically via PostgreSQL trigger**, independent of API process/date/E2E identifiers.
- Existing projects with finalized legacy Cycle (`Status=Finalized`) are seeded **LegacyV1**.
- Other existing projects are seeded **RequiresOptIn**; they retain their legacy history/workflows and are not silently promoted. There is no public adoption/opt-in endpoint; historical V2 enablement requires an explicit approved policy/action.
- Project completion on an official project no longer inserts a legacy Cycle. Existing historical completion semantics remain. A legitimate pending-project deletion can cascade an otherwise unused workflow selector; it cannot delete official calculation/audit history. This compatibility case was tested.

Official calculation creation has no dependency on legacy Cycle participants. The shared engine has an internal official creation entry point; its legacy route still requires the legacy Cycle and retains the original roster guard. Frozen V2 roster is validated against the correct closed Peer Snapshot/full effective Creator-inclusive membership, not an old A-only Cycle.

The registered calculation remains in `GitContributionRuns`, with original scoring/normalization versions and frozen inputs/results. `OfficialContributionRuns` owns the new lifecycle. DB guards freeze the referenced calculation **from official registration onward**, so old experimental mutation endpoints cannot change it. Existing experimental calculations cannot be adopted implicitly.

## 2. Official API and FE contract

Complete route/DTO/error/authorization documentation: [Official Contribution V2 API](../api/UNINET_OFFICIAL_CONTRIBUTION_V2_API.md).

Base route: **`/api/projects/{projectId}/contribution/v2`**.

Implemented groups:

- Workflow mode, setup blockers and resumable latest cohort/snapshot/run IDs/progress.
- Policy2 capture, authorized snapshot metadata and paginated membership references; no raw frozen payload endpoint.
- Git-only readiness/category summary, provisional versus Ready GitScore/GitShare and explicit limitations.
- Creator-inclusive frozen peer cohort, own-session submissions/revisions, progress, close and validated Peer Snapshot summary, reusing existing service/DTO behavior.
- Idempotent V2 Run create, paginated run list/filter, Creator detail, actually scored activity references, exclusion reasons and immutable audit pages.
- Submit for review, approve, reject with required feedback, finalize, publish.
- Published **own-member result**; no Draft/rejected result leak, individual peer comments or arbitrary private EvidenceVersion lookup.

Official DTOs use `contractVersion="1"`; existing peer DTOs retain numeric enum semantics. Official category projection reports Complete instead of the engine's old `EligibleForExperimentalMvp` label. Calculation algorithm identifiers remain unchanged for compatibility. Outer official state is authoritative; the shared calculation's legacy status stays Draft because the calculation content itself is frozen, not driven through the old lifecycle.

Latest workflow references support FE reload/recovery without accidentally reopening cohorts or recapturing snapshots. Member-visible metadata is filtered; Git snapshot/run references are Creator-only. All list/audit/membership/activity/exclusion endpoints have bounded paging and documented filters/order.

## 3. Review, approval, finalization and publication

Implemented graph:

**Draft → Review → Approved/Rejected → Finalized → Published**.

Rejected may be submitted for another review of unchanged frozen content. New evidence requires new immutable inputs/calculation; no endpoint edits ContributionPercent.

- Management follows the **existing Contribution leader policy: Project Creator**, with active Student account and effective project access. A textual Leader role or Git Manager grant is not silently promoted into an official approval permission.
- Explicit `expectedRevision` prevents stale transitions. Rejection requires nonblank feedback ≤2000 chars; all reviews retain actor/time/action/feedback. Invalid state skips are rejected.
- Finalization verifies policy2/current authorized evidence, selected completed retrieval jobs within cutoff, exact snapshot membership/coverage, canonical input/result hashes, frozen roster/history, supported Peer contract/all-to-all ratings, deterministic shared scoring/normalization, positive applicable mass,100.00% allocation and absence of conflicting legacy/official final results.
- Project row lock + Serializable transaction serializes competing writes. Creator account and frozen participants' account/membership rows and the reader's repository/grant/installation/connection locks remain held through commit. Locally committed revocation cannot silently authorize a stale finalization; serialization/unique/deadlock conflicts return a safe409 and require a fresh unit of work.
- Calculated data/result hash is immutable upon official registration; finalized official header cannot be changed/deleted. Audit rows are append-only.
- Publication is a **separate append-only row** with PublishedAt/PublishedById. It only references a finalized official Run of the same project, without unlocking frozen results. One finalized/publication result per project is enforced by unique constraints.
- Double finalize/publish is idempotent for the same result and appends no duplicate audit event. A competing different official result cannot replace the existing one.
- Published member reads contain only that frozen participant's own aggregate shares/percent/hashes, with current membership authorization. Retained audit does not grant private-facts access after unlink/AccessLost. External GitHub revocation is detected when local state/probes observe it, not instantaneously by this workflow.

## 4. Legacy and policy compatibility

- Contribution V1 entities, formula, finalized Cycles/Participants/PeerEvaluations/Results are unchanged. Old endpoints retain old DTOs/semantics for legacy projects. V1 routing rejects new official projects with `CONTRIBUTION_USE_OFFICIAL_V2`; V1 reads retain compatibility when the additive workflow table is absent.
- Experimental endpoints remain readable. Their old review/finalize roster validation is **not removed or weakened**. Registered official calculations get `OFFICIAL_LIFECYCLE_REQUIRED`; V2 has its own validation path.
- All existing snapshots, Peer Snapshot and Runs #1–#4 remain historical records. No official wrapper/publication is seeded for them.
- Weights remain35/30/20/15 and separate Git60/Peer40, eligible External denominator retained, verified NotApplicable transfer only, deterministic largest remainder. Applicable zero mass/Incomplete does not become fake zero-ready Contribution.
- Existing hybrid partial-patch allocation, overlap tie/dispute and historical/post-merge force-push limits remain controlled. Actual unsupported partial overlap/ties prevent Ready; no similarity threshold/allocation policy was invented. The algorithm's historical `ExperimentalGitScoringV2` identifier is reused as a versioned engine contract, not replaced by an untested new formula.

## 5. Database and migration

One **new additive** migration:

**`20261119190820_AddOfficialContributionV2Workflow`**.

New tables:

1. `ProjectContributionWorkflows`: ProjectId PK/FK, Mode, SelectedAt.
2. `OfficialContributionRuns`: same ID/project as its calculation, RequestId, State/Revision, ResultHash, creator/time, finalizer/time.
3. `OfficialContributionAudits`: append-only action/actor/time/feedback and unique Run/revision.
4. `OfficialContributionPublications`: one Project/run, publisher/time, composite run/project FK.

Adds supporting `(GitContributionRuns.Id,ProjectId)` alternate key, new scoped/composite keys, restrictive audit FKs, lifecycle/finalization/hash-length CHECKs, idempotency/finalization/publication uniqueness and six PostgreSQL functions/seven triggers. Only workflow-selection rows are seeded; no old business table is updated/deleted, no scoring/evidence/ratings backfill.

The workflow-selector Project FK is Cascade **only to preserve existing legitimate project deletion when no retained audit references prevent it**. A guard rejects direct selector edits/deletion; actual calculation/run/snapshot/audit references remain restrictive/immutable. This is not a cascade that deletes finalized Contribution data.

Migration source/Designer and EF model snapshot agree; `HasPendingModelChanges=false`.

Reviewed scoped SQL: [OfficialContributionV2.review.sql](../migrations/OfficialContributionV2.review.sql).

SHA-256: **`3184034999DABFADE3AD7EACF8E389F52658C629DDD6BB125F19A7FA6D14EF05`**.

This review script contains only this migration and its own history insertion in one transaction. **It is not permission to apply it on E2E. No blanket EF database update.**

### Disposable migration verification

- Fresh full chain: **94 migrations**, new Project default/lifecycle tested.
- Existing-data upgrade from pre-official schema: old Git V1 Run/input hash/snapshot/facts preserved, finalized legacy selector=LegacyV1, historical unfinalized selector=RequiresOptIn.
- Empty/unused official-schema Down → Up: PASS, original history preserved. Once official projects/results exist, Down fails closed with **55000**, retaining audit/history.
- Actual restore of the protected E2E baseline (86 applied, seven old missing) into separate disposable: exact103 application-table baseline fingerprints match.
- Apply **only new scoped SQL as non-superuser `uninet_test`**: **86→87**; exact one added history ID, seven old migrations not executed, all103 original tables/old Run#4 unchanged; one RequiresOptIn selector, zero official Runs. Reapply: PASS.
- Test-only late-DDL fault (missing trigger function) produced **42883**: full transaction rollback,86 history rows, no new workflow/run tables, all original data unchanged. Source/review SQL was not weakened to pass this test.

## 6. Tests and build

**265 unique latest PASS / 0 FAIL / 0 SKIP**, including **96 PostgreSQL cases**.

- One full relevant regression:265/0/0. Subsequent official contract/resume/paging assertions18/0/0 and scored-summary HTTP assertion1/0/0 replace the corresponding earlier outcomes; they are **not added** to265.
- New coverage is18 cases:11 transition/feedback unit cases +7 PostgreSQL feature/lifecycle cases. Existing247 cases remain passing.
- New PostgreSQL coverage includes default OfficialV2, actual official project completion without legacy Cycle, pending deletion compatibility, three members/two repositories, same provider repository across isolated projects, closed full roster, missing repository/identity, incomplete/empty evidence, zero Peer mass, missing/foreign snapshots, corruption/hash mismatch, unauthorized/Creator/member privacy, approval/rejection/feedback/revision checks, competing approvals/publications, double finalize, publication ordering, revoked local access, frozen registered/finalized results, legacy guard, schema-required behavior and migration upgrade/rollback.
- HTTP lifecycle uses real Kestrel/JWT/controller/DI/service/PostgreSQL path on an ephemeral port; no production/E2E records are used as a write fixture. No mock-only transaction claim.
- Shared regression includes evidence writer/canonicalization/EP06/snapshot coverage/authorization-generation/lease/recovery, Git attribution/scoring/External/zero-mass/rounding and Contribution v1 compatibility.
- Isolated API/Application/Infrastructure build: **PASS**. EF model consistency: **PASS**. `git diff --check`: **PASS**. Existing compiler/analyzer/line-ending warnings remain non-failing.

Fixture corrections encountered during implementation were ordinary compile/import/seed-query/audit-count setup issues. The HTTP run-list state filter exposed an EF projection translation defect in the new code; it was corrected and retested. Scored reference pagination now asks the shared calculator for actually scored identities, avoiding incorrect cross-category filtering by an unrelated exclusion. No production hash/authorization/readiness assertion or immutable guard was disabled.

Verified dedicated disposable PostgreSQL17:
`uninet-official-v2-20261120-020736`, ID `2152a422dca278ab91c638a693c4488c569555977b17e52a2aabd5d7280ca973`, host127.0.0.1, actual port **55081**, postgres for fixtures; `uninet_test` for restored-baseline scoped DDL. Explicit disposable database names. Identity/port/user/database were checked before writes/resets. It is separate from the E2E container and was stopped after verification; database evidence was retained.

## 7. E2E preservation — read-only PASS

Target: `127.0.0.1:55433/uninet_github_app_e2e`, container `uninet-step2-concurrency` with its previously verified full ID. No login, rating, capture, new Run, GitHub request/sync, migration or lifecycle write occurred.

Before/after **all103 table fingerprints and schema catalog match**. Exact86 applied migration IDs retained. The source now has94 migrations, so live E2E has **seven old pending plus this one new pending**; no history stamping.

- Run#4 **`ad2cf570-9592-465a-bdb0-c80c5112cdef`** remains **Draft / Ready**, InputHash `291d97e4d33438f05fb651bcb09dda8d571feb93b77d3ff8d9db15b5e0ae465b`, A78.36/B21.64. Not reviewed/finalized/published/adopted.
- All previous Runs, five Git snapshots, existing closed Peer Snapshot, ratings/revisions, legacy Cycle, evidence/observations, identity history and memberships unchanged.
- Jobs24, active0; checkpoints470; versions102; observations693; Git snapshots5; coverage470; Runs4. Same one Closed cohort with two authorized E2E test submissions and one Peer Snapshot.
- Same **PID20740**, verified original binary/listener, Global/ManualEnqueue/Scoped=false, JobId=null. Health **200**, broad readiness **503**, unchanged. No restart or worker activation.
- No Frontend, watchdog/launcher, generic env or E2E config modification; no Neon/production; no commit/push. Existing dirty worktree retained.

## 8. Files in this implementation

New:

- [Official entities](../../UniNet.Domain/Entities/OfficialContribution.cs).
- [Official EF configuration](../../UniNet.Infrastructure/Data/OfficialContributionConfiguration.cs).
- [Official DTOs](../../UniNet.Application/DTOs/OfficialContributionContracts.cs).
- [Official orchestration/lifecycle](../../UniNet.Application/Services/OfficialContributionService.cs).
- [Shared-engine official entry/validation](../../UniNet.Application/Services/GitOfficialCalculation.cs).
- [Official controller](../../UniNet.API/Controllers/OfficialContributionController.cs).
- New migration/Designer, scoped SQL, [unit tests](../../UniNet.Tests/OfficialContributionTests.cs), [PostgreSQL tests](../../UniNet.Tests/OfficialContributionPostgresTests.cs), API document and this report.

Focused modifications:

- DbContext applies four configurations; model snapshot includes additive schema.
- Shared calculation creation supports an externally owned transaction and explicit official path; original experimental cycle/roster semantics stay intact. Existing review/finalize reject officially registered calculations.
- Snapshot reader can join an existing transaction so authorization locks stay held through official lifecycle commit; stand-alone reads still own/commit their transaction.
- DI registration; Project completion branching; V1 route guard; existing PG fixture class made partial to reuse its seed/auth helpers. No second test scoring fixture engine.
- Latest-agent handoff updated. Earlier unrelated edits were not removed or refactored.

## 9. Deployment plan and remaining boundaries

**Separate user approval required before E2E schema/code deployment.** New endpoints are not yet present on the current E2E process.

Recommended reviewed rollout:

1. Reverify actual target/history/schema/data, all workers off, no competing Contribution/project writes, current protected backup and restore evidence. Preserve any genuine changes made since this task.
2. Quiesce old/new **project and Contribution writers** across API instances; old binaries do not understand the new default selection/frozen official guards. Do not run mixed old/new writers during enablement.
3. Apply only the reviewed new scoped SQL after approval, with ON_ERROR_STOP and bounded lock/statement timeouts. On E2E's current partial baseline the expected history becomes87 applied/seven old pending. No seven-old-migration execution or automatic startup migration.
4. Verify selectors/keys/guards, unchanged old data and no new official result. Deploy the tested Backend coherently, then verify official schema-required/status contracts and restore intended worker-off runtime. Do not use Run#4 for write verification.
5. Enable official writes only after verification. Use a dedicated approved non-historical project/fixture for a deployed write demonstration. Existing E2E project remains RequiresOptIn; historical publication/adoption needs its own explicit policy and is not silently enabled by migration.

Rollback before any official data: tested transactional rollback/empty-schema Down is available only after impact review. Once new official projects/results exist, Down deliberately refuses; retain data and use an approved forward correction. Never restore an old backup over later user work automatically.

General partial-patch allocation/post-merge force-push and historical opt-in remain explicit policy boundaries, not guessed implementation. Current approved rules are enforced, including blocking actual unsupported overlap. Future delegated-leader permissions would need an explicit policy change; this task follows existing Creator authorization.

## 10. Final readiness

**BACKEND IMPLEMENTED — DEPLOYMENT PENDING APPROVAL.**

Backend contracts and source behavior are ready for FE planning against the documented API, and disposable write lifecycle/migrations/regression pass. Do **not** label the live E2E Backend ready for FE official-workflow integration until the separately approved schema/code rollout and corresponding deployment checks complete.

Stop here. No E2E review/finalization/publication, Frontend implementation or automatic additional acceptance phase.

Local receipts: `C:/Users/ADMIN/AppData/Local/UniNetE2EBackups/official-v2-phase3-20261120-020735/` (TRX/latest unique outcomes, fixture targets, generated SQL/model proof, scoped restore/rollback/idempotency logs, old-data fingerprints and live read-only preservation).
