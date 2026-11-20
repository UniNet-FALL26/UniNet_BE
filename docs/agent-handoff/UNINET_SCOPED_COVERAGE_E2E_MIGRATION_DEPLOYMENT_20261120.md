# UNINET — SCOPED COVERAGE E2E MIGRATION DEPLOYMENT

**SCOPED COVERAGE MIGRATION APPLIED — VERIFIED**

Date: 2026-11-20, Asia/Saigon. User approval covered exactly one migration. No GitHub sync, snapshot capture, Peer submission/closure, new Contribution Run or finalization was performed.

## A. Execution

| Item | Verified value |
|---|---|
| Target | `127.0.0.1:55433/uninet_github_app_e2e` |
| Role | `uninet_test`, non-superuser |
| Container | `uninet-step2-concurrency` |
| Container ID | `2fbe020661be4f7ae4a7fd6eac82524b6f3f33dad8ad74da7981e1ce3eb9f767` |
| PostgreSQL | 17.11 |
| Applied migration | `20261119164008_AddProjectGitSnapshotCoverageReceipts` |
| Execution time | 2026-11-20 **00:43:19 +07:00** |
| Execution result | psql exit **0**, explicit **COMMIT**; durable history/catalog verified afterward |
| Timeouts | `lock_timeout='5s'`, `statement_timeout='60s'`, `ON_ERROR_STOP=1` |

Executed only [the reviewed SQL](../migrations/ProjectGitSnapshotCoverageReceipts.review.sql), with read-only precondition assertions and session timeouts. SHA-256: **`FD9BBCA36DA372D41D939909034AC49DE7382B99008875C88A3BD73DA3CAF9B3`**. The checksum matches the safety gate; current-source EF `GenerateScript` reproduced identical scoped SQL after line-ending normalization. Current model vs migration snapshot is clean. Generation required no database connection.

Before DDL: actual container mapping, configured and live process connection target, complete 85 history IDs, exact eight pending IDs, absent coverage objects/new alternate keys, required existing guard/keys, no active Git job, worker-off runtime, schema catalog and all 102 application-table fingerprints were checked. Everything matched the [verified safety gate](UNINET_COVERAGE_MIGRATION_SAFETY_GATE_20261120.md). No unexplained drift or new Peer data existed. Fingerprints were rechecked immediately before execution.

The reviewed script created its DDL and inserted its own history row in **one transaction**. No separate history stamping, blanket EF update, old migration execution, Down or restore occurred. No error/unknown commit outcome occurred; no retry was needed.

### Backup verification

Existing consistent backup still matches the entire current pre-DDL business data, schema and history baseline, so a new backup was not required:

- Protected directory: `C:/Users/ADMIN/AppData/Local/UniNetE2EBackups/coverage-safety-gate-20261120-002150/`.
- File `e2e-baseline.dump`, **776634 bytes**, SHA-256 **`74bbeb62cb620f71f7ee2c50fc82684bae43d087600201bf25d183a5e753bb58`**.
- SHA-256 and restrictive ACL reverified. `pg_restore --list` rerun successfully without a database connection. Prior actual full restore evidence matches all 102 table fingerprints and all 85 migration IDs.
- Backup uses the same exported read-only PostgreSQL MVCC read point as its baseline. No current data was restored, overwritten or deleted. No other database was accessed in this deployment task.
- This is the **pre-migration** backup. Preserve it for controlled recovery; never automatically restore it over subsequent real member submissions or Git work.

## B. Schema — PASS

Post-commit catalog exactly matches the previously rehearsed additive changes; no old catalog definition disappeared or changed:

- `ProjectGitEvidenceSnapshotCoverage`: **11 NOT NULL columns**, initially **0 rows**.
- PK `(SnapshotId, GitSyncCheckpointId)`.
- CHECK `CK_SnapshotCoverage_Receipt`: nonnegative item count, receipt policy 1, scope completion no later than job completion.
- Four **validated restrictive composite FKs**: checkpoint/job, job/association, snapshot/project, association/project.
- Alternate keys `AK_GitSyncJobs_Id_ProjectGitRepositoryId` and `AK_GitSyncCheckpoints_Id_SyncJobId`.
- Five explicit coverage indexes, including the unique snapshot/job/category/scope index; plus PK index and two alternate-key backing indexes (**eight new indexes total**).
- New function `uninet_snapshot_coverage_validate()`.
- Enabled triggers `snapshot_coverage_validate` and `snapshot_coverage_immutable`.
- Existing `uninet_snapshot_entry_guard()` and all old functions/triggers remain unchanged.

EF history is now **86 applied / 7 pending**, with exactly one added history ID. The pending set is unchanged except for removal of the approved coverage migration:

1. `20261106081602_ConsolidateVerificationPayments`
2. `20261106141923_AddAssessorOnboarding`
3. `20261107051900_AddAssessorContractSigning`
4. `20261108023501_AddAssessorPasswordTokenIndex`
5. `20261108142140_ArchiveAndRemoveLevel1`
6. `20261109013212_ArchiveAndRemoveVerificationEvidence`
7. `20261114014337_ArchiveAndRemoveExternalProjectFiles`

Do not use a blanket migration command to clear these. No coverage backfill, policy-1 upgrade or synthetic receipt was created.

## C. Data preservation — PASS

**All 102 pre-existing application-table row fingerprints match before/after**, including exact serialized row values, not just counts. The only new table is empty; the only existing table whose data changed is EF migration history, by the approved one row.

| Data | Before → after |
|---|---|
| GitSyncJobs | 23 → 23, all Completed; active 0 → 0 |
| GitSyncCheckpoints | 447 → 447 |
| GitEvidenceVersions | 100 → 100 |
| ProjectEvidenceObservations | 646 → 646 |
| Evidence snapshots | 4 → 4, all finalized policy 1; headers and membership unchanged |
| Git Contribution Runs #1–#3 | 3 → 3, immutable inputs/results unchanged |
| Coverage receipts | Absent table → empty table |
| Peer cohort | Existing `5f798512-6bce-4c66-b58d-c3d5b859fae7`, Open, frozen A+B roster unchanged |
| Peer data | 0 submissions, 0 revisions, no Peer Snapshot; unchanged |

Identity ownership/history, memberships, OAuth/App authorization, repository linkage, provider objects, legacy Contribution cycles/results and all other business tables are unchanged. No authentication login/session write was needed for this deployment.

## D. Runtime and verification

- **PID 32812** and original start time unchanged; no Backend restart. Same API/Application binary hashes, one listener on 5239.
- Process environment was inspected with an allowlist before DDL: actual E2E connection, Global=false, ManualEnqueue=false, Scoped=false, JobId absent/empty, unrelated workers disabled, generic local-env loading skipped. CLI was checked for conflicting worker overrides. Same process remained after commit.
- `/health`: **200**. `/health/github/readiness`: **503**, expected with global sync disabled and the seven old migrations still pending. Its guard/meaning was not changed.
- Current-source Infrastructure/Domain compile and EF model consistency: **PASS**. Reviewed SQL regeneration, live read-only schema assertions and baseline/history checks: **PASS**.
- No new xUnit suite or write test was run on E2E. The safety gate's **21 PASS / 0 FAIL / 0 SKIP** and real disposable guard/rollback/idempotency tests remain the rehearsal evidence, not new test executions.
- No production code, migration source, reviewed SQL, runtime configuration, launcher or watchdog was changed. Documentation only: this deployment report, latest-agent pointer and Phase 2 follow-up note. Existing dirty worktree retained; no commit/push.

Protected deployment receipts:
`C:/Users/ADMIN/AppData/Local/UniNetE2EBackups/coverage-e2e-deployment-20261120-004101/`.

Includes exact executed SQL/checksum, execution log/UTC timing, backup verification, runtime allowlist, complete history/pending IDs, source-generated SQL, before/after fingerprints/catalog, schema assertions and postflight metadata. No passwords, JWTs, tokens or private ratings/comments were printed or included in this report.

## E. Next readiness and approval boundary

- **Coverage schema is ready for future policy-2 capture.** Existing policy-1 snapshots remain unchanged and cannot acquire coverage proof retroactively.
- **No new deployment blocker found.** Schema readiness does not mean current Git facts/Peer inputs are complete or that Contribution is publishable.
- A future **Scoped Manual Git V5 Sync can be prepared** using the existing official enqueue/pinned-consumer pipeline, with new authorization/job-isolation preflight. The current task did not make a live GitHub access request or enqueue/claim a job.
- A separate user approval is required before Manual Sync POST and consumer activation. After a successful authorized sync, a separate approved policy-2 capture would freeze actual source receipts and new V5 facts, followed by diagnostics. Partial overlap/force-push and other genuine readiness limits remain.
- Existing cohort is left for members' real evaluations. No rating, closure, Peer Snapshot, Run V2, scoring-policy change, finalization or publication was performed.

**Stop here: SCOPED COVERAGE MIGRATION APPLIED — VERIFIED.**
