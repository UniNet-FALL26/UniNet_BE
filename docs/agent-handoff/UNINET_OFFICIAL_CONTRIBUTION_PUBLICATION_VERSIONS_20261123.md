# UNINET — OFFICIAL CONTRIBUTION: ATOMIC APPROVAL & PUBLICATION VERSIONS

2026-11-23 — **IMPLEMENTED IN SOURCE / DISPOSABLE VERIFIED; NOT DEPLOYED TO FE E2E**.

## 1. Audit findings and live boundary

Existing implementation supported multiple calculation contexts, but Official Create rejected any project with a Finalized OfficialRun, a partial unique index allowed one Finalized row/project, and OfficialContributionPublications had ProjectId PK. results/me read that one receipt. Consequently post-publication replacement was not supported even though Peer storage could already retain multiple Closed cohorts. Old review/finalize actions write lifecycle/revision/audit only; publishing writes a separate immutable receipt. Frozen GitContributionRun input/result hashes remain protected by trigger.

Source audited: OfficialContributionService/Controller, GitOfficialCalculation, frozen calculation/context hashing, Official/Peer configuration and migrations, snapshot/reader/authorization, existing HTTP/PostgreSQL tests. Existing uncommitted changes retained.

Read-only FE audit: Project A Run `00902016-dbf8-4fe6-9664-470d1bd2bc0b` was already **Finalized revision3** and had first-publication receipt timestamp `2026-11-23T05:57:16.288952Z` when checked; it was no longer Draft as in the request. ResultHash `78c560d5a535174c06ab5f6a0c34d8d2e7c7604fdd9746423b29914e082ae82b` matched repeated readback. Agent did not transition/publish it. FE history remains104 migrations; new migration was NOT applied live. No Project B write, snapshot/Peer/rating/run creation, API restart, migration, Git sync, consent, Neon/root `.env` use or commit/push in this task. Existing API health200; runtime receipt worker-off. No live E2E success claim is made from fixtures.

## 2. New lifecycle and contracts

Normal UX: **preview -> Duyệt và công bố**. POST `/api/projects/{projectId}/contribution/v2/runs/{runId}/approve-and-publish`:
```json
{"expectedRevision":0,"expectedPublishedRunId":null,"reason":null}
```
Values are examples, not live instructions; use actual server Revision/currentPublishedRunId. For replacement, expectedPublishedRunId must identify the current server publication.

Operation uses Serializable, locks Project, checks Creator/Student/membership/mode, run binding, current publication, expected revision, hashes, current Git authorization, exact frozen roster/Peer/Git inputs, completed retrieval and readiness via existing ValidateOfficialAsync. Internal Draft/Rejected -> Review -> Approved -> Finalized transitions each retain audit and required existing database guards, all inside the same transaction; publication then commits with them. Finalized rows are never edited again. Already-published same-Run replay returns its historical Published result without moving current backwards. Failed validation/insert rolls back all intermediate states/audits and preserves prior current release.

Old submit/approve/reject/finalize/publish APIs remain. Old publish can make the first publication or replay itself, but cannot replace a different current release without the expected-current atomic contract. No FE four-request orchestration. Approve-and-publish does not run automatically on calculation.

New Creator-authorized reads:
- GET `/version-capabilities`: supported/canAdjust/canOpenPeerRound/currentPublishedRunId.
- GET `/versions?page=1&pageSize=20`: publication version, RunId, PublishedAt, IsCurrent, ReplacedByRunId, SupersededAt, Reason.
- Existing GET run/activities/exclusions/audit remains for history under current project/private-evidence authorization.

Members retain **only own current results/me**. Member historical audience has not been approved, so history remains Creator-only; this avoids inventing access to departed-member/private historical results. A future own-history policy/API can be reviewed separately. No raw Peer rating endpoint added.

## 3. Additive model/migration

Migration: `20261123061945_AddOfficialContributionPublicationVersions`.

- Adds `OfficialContributionReleases`: RunId PK; ProjectId; positive Version; PreviousRunId; publisher, publication time and optional reason.
- Unique `(ProjectId,Version)` and one release/Run, project-consistent composite FKs to current/previous OfficialRuns, publisher FK and indexes.
- Removes only the old *unique Finalized/project index*, recreating it as nonunique. No old result, snapshot or audit value is changed.
- Copies existing immutable OfficialContributionPublications receipts as release version1 with exactly original actor/time. Original table/PK/immutability remain; it continues to retain first receipt, never changes to point at v2.
- Insert guard locks Project, requires finalized same-project run, exact next Version and previous RunId. UPDATE/DELETE of release ledger is forbidden (55000). Existing finalized-run/calculation/snapshot/Peer immutability triggers are unchanged.
- Current publication is **highest ordinal release chosen by Backend**. There is exactly one highest ordinal because of the unique project/version key and guarded linear append. No mutable current pointer or FE timestamp inference. Supersession metadata is derived from next ledger entry; old row/result hash is not edited.
- Run identity and calculation RunNumber remain; publication ordinal is allocated only on successful publish. Multiple immutable Draft candidates may exist so disagreement does not require deleting/rewriting old results. All candidates compete against an explicitly expected current publication; only one can replace that current point. FE presents latest preparation context; previous candidates remain in authorized run history. No arbitrary score editing or new scoring weights.

Generated SQL reviewed: [scoped SQL](../migrations/OfficialContributionPublicationVersions.review.sql), SHA256 `EE42FD9808F68E2B43AD0C03664CB83AFE49E4C7249BCDDEB6931610F618F2F6`. It is one transaction and idempotent by EF history. It does not run unrelated pending migrations or delete Task/Report data. Initial EF scaffold surfaced unrelated historical Designer/snapshot differences; those operations were removed from the new migration before any rehearsal. Final model snapshot diff is only release model and Finalized-index change. No accepted migration source was rewritten.

Down refuses destructive rollback once any release exists (including imported legacy publication). Do not restore over live DB or drop history automatically. Unpublished empty-schema rollback still requires preflight, including ability to reinstate the old Finalized unique index.

## 4. Recalculation and Peer rounds

Create now permits new Official calculation contexts after publication. Same Git/Peer Snapshot, policy, roster and frozen identity/history context deduplicates through existing CalculationContextHash; returns the original OfficialRun, `reusedInputs=true`, instead of creating a fake new result. Original RequestId still replay-protected; different RequestId for same inputs does not create another calculation. Experimental calculations are not implicitly promoted.

Reusing a Git snapshot requires policy2, intact canonical/input hashes, current evidence authorization, complete jobs/coverage, eligible effective roster and correct snapshot cutoff/history. Peer snapshot must belong to project/Closed cohort, have exact compatible effective roster and valid hash. A roster member who joined after cutoff cannot be silently inserted into old inputs. Historical identity changes after cutoff are not rewritten into old snapshots. Policy remains ExperimentalGitScoringV2 / GitPeerSeparate60_40V1; this is implementation naming, not a new scoring decision. Partial overlap/force-push limits stay visible.

Official OpenPeer locks project and permits a new cohort only when no unclosed round exists; old closed rounds/snapshots remain untouched. Each new round has its own IDs/roster/revisions and starts with **zero ratings**. Members must submit authentic new ratings. Caller may reuse valid existing Git snapshot or explicitly capture a new one. Cohort/snapshot IDs selected for a new Run are explicit and project-bound. Creator remains included; members without Git identity are not removed.

## 5. Frontend

Existing Project Detail -> Đóng góp and three cards retained.
- Draft/current preparation: preview score table, exclusions, limitations and policy; one **Duyệt và công bố** transaction with explicit confirmation.
- **Chưa đồng ý với kết quả** opens non-destructive adjustment choices.
- Published: **Điều chỉnh Contribution**, **Xem lịch sử điểm**. Adjustment offers same-input recalculation (dedup notice), new Peer round, or explicit new Git capture when supported.
- New round's progress/inputs stay distinct from published result; Member continues own current Published score while replacement Draft is private.
- Server capabilities gate new actions. On the still-old live Backend, new functionality is disabled rather than silently chaining old transitions.
- Pending Run journal binds account/project, RequestId and exact Git/Peer Snapshot IDs, so retries cannot substitute another round's latest inputs. No secret/rating stored locally.
- Version history uses server IsCurrent/supersession, existing authorized historical Run reads. Members never request privileged history/Run detail.

[FE specification](../../../UniNet_FE/docs/specs/project-contribution-v2.md) has complete frontend file inventory and existing error/recovery semantics.

## Files changed in this task

Backend: `UniNet.API/Controllers/OfficialContributionController.cs`; `UniNet.Application/DTOs/OfficialContributionContracts.cs`; `UniNet.Application/Services/OfficialContributionService.cs`; new `OfficialContributionVersions.cs`; `ExperimentalPeerCohortService.cs` (transaction reuse only); `UniNet.Domain/Entities/OfficialContribution.cs`; `UniNet.Infrastructure/Data/OfficialContributionConfiguration.cs`, `UniNetDbContext.cs`, new migration+Designer and model snapshot; `UniNet.Tests/OfficialContributionPostgresTests.cs`, new `OfficialContributionVersionPostgresTests.cs`; review SQL and this handoff. Prior unrelated worktree changes are not part of this versioning feature.

Frontend: Contribution types/service/pending journal/next-action mapping, useContribution, ProjectContributionPanel, unit/browser fixtures and Contribution specification. No repository/identity policy or production auth/session storage change.

## 6. Tests and evidence

Dedicated PostgreSQL17 disposable container `uninet-scoped-activation-release`, ID `e5702f6d5c18c2cabb0dcf98eef8cc607bd3e7e87e9b322912af6d2dd289ab27`, localhost55448 (not55433), userpostgres. Official fixture DB names `uninet_official_v2_disposable` and unique `uninet_official_v2_versions_upgrade_<random>_disposable`. No real provider requests or real ratings in tests.

- Backend isolated build PASS (existing warnings).
- Final focused Backend: **32 PASS / 0 FAIL / 0 SKIP**, including4 new version tests plus existing Official lifecycle/HTTP/security/migration and normalization filters. Repeated executions not double-counted.
- Direct service and real disposable HTTP: Draft approval; member/wrong project/revision rejection; legacy endpoint compatibility; duplicate replay; two competing distinct Runs; stale current version; integrity/readiness/authorization rejection; immutable finalized results/releases; failed insert rollback before/after existing publication; v1 retained during Draft v2; v2 atomically selected; v1 auditability; new real-fixture Peer round no copied ratings; incomplete close rejected; old/new Peer snapshots retained.
- Fresh chain and populated legacy-publication upgrade tested. Upgrade copies receipt and keeps result hash; destructive Down correctly rejected. Idempotent review SQL reapply COMMIT on disposable. EF pending-model-changes PASS.
- Frontend combined focused: **94 PASS / 0 FAIL / 0 SKIP** (27 Contribution +67 prior focused).
- Redesigned browser: **24 PASS**, actual Expo bundle in real Chrome, all requests/mutations intercepted. Includes preview/confirmation, disagreement/adjustment/history, exactly one atomic approval POST, no multi-step chain, own Published visibility and stale-response isolation.
- New feature ESLint PASS; full TypeScript retains13 unrelated baseline errors (opportunities/AssessorProfilePosts). Existing expanded Hub lint baseline unchanged. diff--check PASS.
- Native device and live new version rollout/publishing NOT TESTED. Current Project A's already Published v1 was not changed by tests.

Receipts: `%TEMP%/official-version-results/official-versions.trx`, `official-version-final4.log`, `version-fe-unit-final.log`, `version-browser-last.log`; fixture screenshots `%TEMP%/uninet-contribution-ui/`. No production approval inferred from them.

## 7. Deployment requirements and risks

**Separate approval required before FE E2E migration/restart.** Stop/drain old API writers, review current DB/backup, rehearse current backup with scoped migration, apply only this migration in transaction, verify imported version1/current/result hashes/ProjectB, deploy new API, then enable FE capability. No blanket EF update if other migrations remain pending.

Mixed API versions are unsafe for semantics: old readers use the first receipt, and old writers do not use release concurrency. Do not use a rolling interval with old Contribution traffic enabled. Existing finalized historical values remain valid; imported metadata makes them v1 without recalculation. If live binary were started before migration it must fail closed; do not suppress missing schema checks. Existing safe launcher checks all source migrations and will require schema alignment before new startup.

No automatic rollback over data. Backup and explicit recovery review required after history exists. Additional member-history access and single-pending/withdrawal policy, if desired, need an explicit product choice; current implementation retains multiple candidates and serializes publication, avoiding destructive automatic draft cancellation.

## 8. Manual E2E plan after deployment approval

For a fresh test project/input context: Creator captures/reuses authorized policy2 Git snapshot, opens/finishes real Peer round, creates Draft v1, previews, clicks **Duyệt và công bố**, confirms. Verify own member result/current version1. Current Project A already has legacy Published result, so after migration it should be imported as v1; do not republish it to pretend a new first publication.

Creator -> Điều chỉnh Contribution -> new Peer round (all effective members) -> members submit new authentic ratings -> close -> create Draft v2 with explicit Git/Peer snapshots. Verify members still read v1 while Draft exists. Creator previews and approves with expected v1 ID and actual revision. Verify version2 current, v1 superseded metadata/history readable to Creator, original input/result hashes unchanged. Repeat same inputs must reuse context/no fake new version. Failed approval/concurrent stale request must leave prior current version intact.

No live step in this plan executed by Agent. Implementation stops here, pending separate rollout approval.
