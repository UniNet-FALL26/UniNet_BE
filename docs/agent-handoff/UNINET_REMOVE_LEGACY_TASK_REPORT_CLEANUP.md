# UNINET — REMOVE LEGACY TASK / EVIDENCE / REPORT WORKFLOW

Date: 2026-11-20. Backend source cleanup completed; **PARTIAL — HISTORICAL/COMPATIBILITY DEPENDENCIES REMAIN** under the requested status definition.

The remaining components are intentional historical-read/shared-Peer/HTTP410 compatibility boundaries, not active Task persistence or a new scoring implementation. No technical failure remains in the executed tests. Removing these remaining contracts/history was not justified or authorized. No additional acceptance phase is proposed.

## 1. Starting state and scope

- Branch `dattt`, HEAD `625718dd8ea3527fa3540e7660c558641c9f0aa2`; MERGE_HEAD `c4f069ae25678f818f74840d625ad66f1f36a87b` still present. No unmerged index entries.
- Existing staged incoming-main changes and prior unstaged fixes were preserved. Before-cleanup status and copies of changed production files were saved outside Git.
- Main had already removed Task entities/EF mappings and introduced the11-table archive migration. This task did not recreate them or add another DropTable migration.
- No Frontend, live database, runtime, secret store, Git history, frozen snapshot, identity ownership or scoring-policy changes.

## 2. Dependency matrix

| Object | Current purpose / references | Git/Contribution dependency | Historical data | Decision |
|---|---|---|---|---|
| ProjectTask / ProjectTaskAssignment | Former CRUD, assignment, status/deadline; main already removed their entities/DbSets/mappings | No V2 dependency | Original rows archived by existing migration | REMOVE active model, already done by main; KEEP migration/archive |
| TaskReport / TaskEvidence / TaskReportReview | Former report group/version, attachments and approval; main already removed entities/mappings | No Git Evidence or Verification dependency | Original rows/columns/IDs retained in archive | REMOVE active model, already done by main; KEEP archive |
| ProjectTaskService / IProjectTaskService | Only410 stubs, referenced by old controller, DI and tests | None | No storage owned | REMOVE in this task |
| Task/report response DTOs and TaskReportStatus | Only represented successful responses from retired operations | None after controller cleanup | Stored numeric historical values are not changed | REMOVE unused response DTOs/enum |
| ProjectTasksController + six request DTOs | Existing17 client routes, request binding and Student JWT boundary | None; returns410 without a service/database | Does not expose archived rows | RETAIN AS LEGACY |
| ContributionService Task calculation branch/private capture/hash/report records | Unreachable after the retired-input exception | Not used by Git calculation | Existing InputSnapshot/InputHash are stored independently | REMOVE dead Run/result creation path and private types |
| ContributionCycles/Participants/PeerEvaluations/CalculationRuns/Results/Reviews | Authorized historical V1 reads, historical Peer/review compatibility | Explicitly separate from Official lifecycle | Immutable/finalized history must survive | KEEP / RETAIN AS LEGACY |
| ContributionCalculator + task/deliverable input records + ProjectTaskStatus | Pure legacy calculation contract; **Peer conversion is actively reused** by experimental/official services | Peer Cohort close, frozen Peer validation, Git official validation | Legacy mathematical reproducibility | RETAIN AS LEGACY / KEEP shared Peer conversion; no Task persistence caller |
| Legacy review/revision handling | Old feedback/audit path; confirmation needing retired inputs returns410 | Not the Official review/finalize/publish path | Existing review records retained | RETAIN AS LEGACY |
| Project/ProjectMember/recruitment/requests/invitations/details/portfolio | Active Project Hub and Creator-inclusive membership | Required by V2 authorization/roster | Membership/repository history retained | KEEP |
| GitEvidenceVersion / ProjectEvidenceObservation / snapshots/coverage | Atomic immutable Git provenance and frozen capture/reader | Required by Git scoring/attribution and Official V2 | Retain all | KEEP — not Task Evidence |
| VerificationReport / ReportAuditLog / ReportService / ReportController | Assignment/case-scoped Assessor report and frozen assessment | Independent Verification business | Submitted report/audit immutability | KEEP — not Task Report |
| Verification evidence/interview/review and archive migrations | Independent Verification lifecycle/retention | No legacy Task dependency | Preserve its own archives/history | KEEP |
| Workspace channel/message compatibility | Independently retired by main, existing410 boundary | Not part of Task cleanup | Existing archive retained | RETAIN AS LEGACY; no revival or extra removal |
| Meeting/LiveKit/session infrastructure | Active assignment meeting and connections | Independent of Task storage | Case/access/audit retained | KEEP |
| Generic upload/Cloudinary/UploadedFiles/Project documents | Shared storage and file metadata | Used by other modules | Files may be shared; archive retains old URL metadata | KEEP; no physical blob deletion |
| Notifications, including old Task target tombstone | Active inbox; retired target does not reopen Task storage | Independent | Historical notifications remain readable | KEEP / RETAIN AS LEGACY |
| Physical legacy tables/files on unknown live schema | Deployment state not inspected in this task | Cannot infer migration state from source | Unknown target retention/dependencies | INVESTIGATE — untouched, no live deletion |

Search/audit covered API controllers, Application DTOs/interfaces/services, Domain entities/enums, DbContext/configuration/DI, tests, documentation and migration source. No active service still references removed Task response/entity/interface types. Generic .NET Task, background `*Tasks`, Verification report/evidence and Git evidence were not removed by name matching.

## 3. Removed implementation

### Task compatibility architecture

- Deleted `UniNet.Application/Services/ProjectTaskService.cs` and `UniNet.Application/Interfaces/IProjectTaskService.cs`.
- Removed their production and test DI registrations.
- Replaced old controller delegation/try-catch/success paths with a small [410 compatibility controller](../../UniNet.API/Controllers/ProjectTasksController.cs). All17 verbs/routes/action names remain. Existing Student JWT and valid subject checks remain; no DbContext, service, storage or upload dependency is required.
- Removed seven unused successful-response DTOs: assignment info, Task detail/list, evidence response, report review/detail/list. Kept the six [request contracts](../../UniNet.Application/DTOs/Projects/ProjectTaskContracts.cs) solely for matching legacy JSON/model-binding behavior.
- Removed unused `TaskReportStatus`. `ProjectTaskStatus` remains part of the legacy pure-calculation and request contracts; `TaskReportReviewDecision` remains a request-binding contract.
- HTTP410 response code/message is unchanged: `PROJECT_FEATURE_RETIRED`. Invalid/missing JSON may still produce normal400; unauthenticated/forbidden requests remain401/403. No successful CRUD/upload/review is implied by retaining a route.

### V1 dead calculation path

[ContributionService](../../UniNet.Application/Services/ContributionService.cs) no longer contains the unreachable Task-based Run/result creation, Task/report capture DTOs, capture hashing or unused calculated-score response mapper.

`CalculateAsync` retains account, workflow, Creator and open-cycle checks, then returns410 `CONTRIBUTION_INPUTS_RETIRED`, **without a cycle UPDATE or write transaction**. It cannot create a Run/result or substitute empty Task inputs. Legacy confirmation still fails with the same retired-input policy; the impossible Confirm/Finalize assignments after that rejection were removed. Historical revision-feedback behavior and read mapping are retained.

The V1 pure calculator is not an obsolete database writer: its Peer conversion is called by Peer Cohort close and Official/experimental validation with empty Task/deliverable inputs. It was preserved together with V1 mathematical tests; no formula, Peer conversion, Git weight or normalization changed.

## 4. Database/migration audit

Existing **`20261116030240_ArchiveAndRemoveLegacyProjectTables`** handles exactly:

```text
ProjectRoleRequirements
ProjectSkills
ProjectInvitations
WorkspaceChannels
WorkspaceMessages
ProjectTasks
ProjectTaskAssignments
TaskReports
TaskEvidence
TaskReportReviews
ExternalProjectDrafts
```

Observed source behavior, verified again on disposable:

- Locks all source tables ACCESS EXCLUSIVE through its transaction to prevent late writers.
- Creates `project_legacy_archive_<md5(current_schema())>.Snapshots`. Each of11 source tables gets its table/schema identity, row count, all original JSONB columns/rows and MD5 checksum. Table owner is the executing migration role, not a new public owner.
- Revokes PUBLIC access to the archive schema. The ordinary INSERT/UPDATE/DELETE/TRUNCATE guard prevents archive writes; this is not a claim that the database owner/superuser cannot alter schema/guards.
- Copies replacement technology/recruitment/invitation data with identity-collision validation. Outbound Task→Project/user/report relationships remain auditable in archived row IDs; they are not turned into new Git observations.
- Drops child tables before parents; no DROP CASCADE is used to silently remove unknown inbound dependencies. Existing FK dependencies can reject the transaction.
- Down explicitly refuses automatic reconstruction/loss of replacement-schema writes. No live Down was attempted.
- Idempotency is through EF migration history: rerunning an already-applied migration target makes no second archive. Raw SQL without that history guard is not independently idempotent and must not be stamped/replayed blindly.

Separate Verification/Level1/external-file archive migrations were reviewed as independent history; they were not rewritten or applied live.

**No new migration or EF model change is required. All103 existing migration source SHA256 values match the pre-task inventory.** Fresh disposable schema has0 active Task-related tables/mappings. This does not prove that an unmigrated Neon/E2E database has dropped those tables; no live target was queried or changed.

## 5. Verification

### Compatibility and V1

- Focused initial run:25 PASS/0 FAIL/0 SKIP, not added to the final total.
- Real Kestrel/JWT HTTP test calls **all17 Task/report operations** with valid old request shapes: every operation returns410 with the existing machine code. An unauthenticated request returns401. No Task mappings or new Contribution Run exists afterward.
- Completion/retired Task request test still creates no evidence-moderation review.
- Legacy calculation preserves Creator authorization and cycle state, rejects new Run creation, and does not fabricate zero-input scores.
- Authorized finalized V1 result reads still return the existing stored scores without Task tables.

### Archive preservation

The Project-refactor PostgreSQL test seeds a nonempty historical Task→assignment→report→evidence→review graph, plus Workspace/draft/invitation/skill/recruitment source data. It compares each original table's JSONB rows against all11 archive payloads after migration.

Strengthened assertions directly verify:

- Exactly5 rows across the five Task-specific source tables survive in the archive.
- All11 archive entries satisfy original row count and checksum.
- Invitation identity collision aborts before table removal; all11 source tables remain after rejection.
- Archive INSERT, UPDATE, DELETE **and TRUNCATE** are rejected; history is not modified.
- Reapplying the migration target is safe; destructive automatic Down is refused.

The earlier handoff's89-row external-development archive count is historical, not a new live measurement in this task.

### Full Official V2 lifecycle — PostgreSQL, no Task tables

The actual existing Official service/HTTP fixtures run on the freshly migrated database. A direct PostgreSQL catalog assertion before Run creation confirms0 `ProjectTasks`, `ProjectTaskAssignments`, `TaskEvidence`, `TaskReports` and `TaskReportReviews` tables.

Fixture setup creates eligible Project/repository/verified identities and frozen Git facts through test infrastructure. Official services capture policy2 membership, open the full Creator-inclusive Peer roster, accept test all-to-all evaluations, close/freeze Peer input, create an Official Run, review/approve, finalize/publish and read the authorized member result. No direct SQL fakes those successful lifecycle transitions.

Representative successful disposable publication:

- Run `d55f3481-aa5b-4935-9eef-56ec65a2e8a2`.
- Project `9b545e9f-a238-448f-8994-07e4a87aaa6b`.
- Durable header Finalized/revision3, publication present; API state Published.
- InputHash `fa61db6e622180f2e1827081861d7f53e8c154342ce5c38ccd79c5373688e89a`.
- ResultHash `595053b6b14567626271ca298ba560a62b086cb8652eafe7dc155b640a1dd668`.

Real Kestrel/JWT/controller tests additionally exercise official lifecycle/Creator/member privacy and published own-result reads. Existing authorization/ABA/lease/rollback/EP06/snapshot/scoring/External/rounding/immutability tests remain passing. Git35/30/20/15 and separate Git60/Peer40 policy remain unchanged.

### Latest test/build totals

**1,130 PASS /0 FAIL /65 SKIP**, total1,195 test identities, **157 PostgreSQL cases PASS**. One full Backend regression was completed after implementation; focused runs are not added to these totals. An intermediate test-edit compile error from a duplicate fixture variable was corrected before execution; no assertion/guard was removed to obtain PASS.

The65 skips are existing opt-in App/E2E/UserRoleJson fixtures, including older cases hardcoded to live55433; they were not enabled against E2E or newly skipped by this cleanup. Full details match the [merge verification's skip inventory](UNINET_MAIN_MERGE_NEON_MIGRATION_VERIFICATION.md). Portable Git/Official/worker/evidence tests and independent historical migration tests did execute on the dedicated disposable target. Do not report skipped fixtures as verified.

Solution build **PASS**,0 errors; final build retains one existing unread-controller-parameter warning. Existing nullability/test analyzer/line-ending warnings are non-failing. EF `HasPendingModelChanges=false`, retired Task mappings0, source migrations103. `git diff --check` PASS.

## 6. Disposable target and safety

- Container `uninet-main-merge-cleanup-20261120`.
- ID `34d03be8a4e21e13bdc9f583dd08dc593c7c5dd55e96f1aa9e9ce8032b5a75cb`.
- Host127.0.0.1, actual mapped port **54974**, userpostgres; exact explicit disposable databases verified before migrations/writes.
- Fresh chain103; older evidence/auth/snapshot baselines separately prepared for real upgrade tests. No live database was used as a destructive fixture.
- Container stopped after verification; database evidence retained. Protected receipts under `%LOCALAPPDATA%/UniNetLegacyCleanup/20261120-062219/` contain baseline copies/status, target, migration setup/model/build logs, TRX/testId manifest, schema/no-Task assertion and disposable publication receipts. No secrets or private real-user ratings were recorded.

## 7. Files changed in this task

Production:

- [ProjectTasksController](../../UniNet.API/Controllers/ProjectTasksController.cs): direct authenticated410 boundary.
- [Program](../../UniNet.API/Program.cs): remove Task service DI only.
- [ProjectTaskContracts](../../UniNet.Application/DTOs/Projects/ProjectTaskContracts.cs): retain request-only compatibility shapes.
- [ContributionService](../../UniNet.Application/Services/ContributionService.cs): remove dead Task-based persistence/capture code, retain historical authorization/read/revision behavior.
- Deleted Task service/interface and unused TaskReportStatus enum listed above.

Tests:

- [Contribution HTTP](../../UniNet.Tests/ContributionEndToEndHttpTests.cs), [retired features](../../UniNet.Tests/RetiredProjectFeaturesTests.cs), [Verification eligibility](../../UniNet.Tests/VerificationEligibilityTests.cs): remove old service dependency, exercise preserved behavior.
- [Official PostgreSQL](../../UniNet.Tests/OfficialContributionPostgresTests.cs): assert lifecycle runs without all five Task tables.
- [Project refactor migration](../../UniNet.Tests/ProjectRefactorMigrationPostgresTests.cs): explicit nonempty archive/checksum/four-operation immutability assertions.

Docs: [Task retirement API](../api/UNINET_LEGACY_TASK_RETIREMENT.md), superseding notice in [historical Workspace contract](../frontend-integration/workspace-contribution-api-contract.md), this report and [next-agent handoff](UNINET_GITHUB_CONTRIBUTION_NEXT_AGENT.md).

No migration, model snapshot, Git writer/scoring/authorization service, Peer/Official service, Project lifecycle, Verification report/evidence service, generic storage, Meeting or workspace implementation was changed. All pre-existing staged/unstaged work remains reviewable; no commit/push/reset/clean or extra main merge.

## 8. Deployment boundary / remaining dependencies

Local obsolete Task business implementation is removed. Intentional retained dependencies are the17 HTTP410 routes/request shapes, legacy result/Peer/review history/read APIs and shared pure-calculation/Peer contracts. Deleting those would remove explicitly required compatibility or active functionality; no unsupported deletion was performed.

Accordingly the requested conservative final classification is **PARTIAL — HISTORICAL/COMPATIBILITY DEPENDENCIES REMAIN**, with no failing executed test or new migration blocker. This is not a request to rebuild Task functionality or open another acceptance phase.

Neon remains a separate deployment safety gate. No connection, migration, drop, history stamp or backup overwrite occurred there. Existing local E2E runtime/data, including Run#4, were not accessed or modified. No GitHub sync, Git activity, snapshot, Peer submission, real Contribution Run or finalization was created. Source cleanup is not authorization to apply pending migrations on any live database.
