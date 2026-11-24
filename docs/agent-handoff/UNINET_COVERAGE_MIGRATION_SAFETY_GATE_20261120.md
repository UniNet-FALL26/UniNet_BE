# UNINET — PHASE 2 COVERAGE MIGRATION SAFETY GATE

Date: 2026-11-20. **READY FOR SCOPED E2E MIGRATION APPROVAL.**

Preparation only. No E2E migration, GitHub fetch/sync, application snapshot capture, peer submission/closure, Contribution Run or finalization was performed. Current Backend was not restarted. Existing uncommitted work was preserved.

## A. Dependencies and live preflight

- Verified effective E2E configuration: `127.0.0.1:55433/uninet_github_app_e2e`, user `uninet_test`; container `uninet-step2-concurrency`, ID `2fbe020661be4f7ae4a7fd6eac82524b6f3f33dad8ad74da7981e1ce3eb9f767`, actual mapped port 55433, PostgreSQL 17.11. All live database commands were read-only. No Neon/production connection.
- **85 applied / 8 pending**: the seven old migrations below plus `20261119164008_AddProjectGitSnapshotCoverageReceipts`. Compared complete ID sets against 93 EF migrations, using migration attributes rather than counting partial archive source files. Coverage table/function/history row remain absent on E2E.
- Supporting snapshot/association `(Id, ProjectId)` keys and `uninet_snapshot_entry_guard()` exist. Current app role is not a superuser, owns the relevant objects, can create in public and execute the existing guard. Disposable execution used the same non-superuser role.
- New job/checkpoint alternate-key columns are non-null. Duplicate key tuples: **0**; job/association repository mismatches: **0**. Existing PKs already make the proposed `(Id, ...)` tuples unique; the new alternate keys provide the exact FK targets.
- PID **32812**, same deployed binary/hash and unique listener on 5239. Global/ManualEnqueue/Scoped=false, JobId=null; unrelated workers remain disabled. Health **200**, broad readiness **503**, unchanged. No readiness bypass or automatic migration.

Seven unrelated migrations remain pending and must not be executed:

1. `20261106081602_ConsolidateVerificationPayments`
2. `20261106141923_AddAssessorOnboarding`
3. `20261107051900_AddAssessorContractSigning`
4. `20261108023501_AddAssessorPasswordTokenIndex`
5. `20261108142140_ArchiveAndRemoveLevel1`
6. `20261109013212_ArchiveAndRemoveVerificationEvidence`
7. `20261114014337_ArchiveAndRemoveExternalProjectFiles`

## B. Disposable rehearsal — PASS

Separate container `uninet-coverage-gate-20261120-002439`, ID `1d114b3b919749caaa08e0349645d5d1f09ba52496f2f8dafd321c7f6e605e48`; actual host `127.0.0.1:54206`, PostgreSQL 17.11, user `uninet_test`.

Databases: `uninet_coverage_gate_disposable` and `uninet_coverage_gate_failure_disposable`. Container identity, mapped port and database/user were verified before mutation scenarios. Both were restored from the same consistent E2E backup, preserving object ownership. Neither is E2E.

The disposable container was stopped after verification; its databases and local receipts were retained. E2E container and Backend were left running.

| Verification | Result |
|---|---|
| Restore baseline | All **102 application-table row fingerprints** and all **85 history IDs** match the exported backup read point. |
| Schema restoration | Columns, FK/unique constraints, indexes, functions and triggers equivalent. Dropped-column physical numbering was excluded from comparison. Five CHECK expressions had normal dump/restore parse-tree formatting differences; parsing source/restored definitions into disposable temporary tables proved identical normalized predicates. No constraint was weakened. |
| Original policy-1 reader | All **four** old snapshots read successfully through the official authorized reader, before and after migration. |
| Scoped apply | Exact reviewed SQL committed as non-superuser `uninet_test`; **85 → 86**, adding only the coverage migration. Seven old migrations still pending. |
| Initial data preservation | All 102 old tables unchanged; new coverage table **empty**. No backfill or old snapshot policy upgrade. |
| Policy-2 capture | Official capture service and reader succeeded on restored real job/checkpoint data: **447 persisted receipts**, 646 observation memberships. Existing empty-review proof accepted the fixture's recorded completed retrieval scopes. This is a disposable test, not a new E2E snapshot or a claim that Git scoring is now Ready. |
| Forgery/constraints | Incorrect count and scope rejected **23514** by source validation; unsupported receipt policy rejected **23514 / CK_SnapshotCoverage_Receipt**. Draft policy-1 snapshot cannot acquire coverage receipts. |
| Membership duplicate | Actual duplicate rejected **23505 / PK_ProjectGitEvidenceSnapshotCoverage**. |
| Immutability | Receipt UPDATE/DELETE and INSERT after snapshot finalization rejected **55000**. Existing guards remain in place. |
| Failed capture transaction | Forged INSERT rejected; explicit transaction rollback left no draft header or receipt. Source versions/observations unchanged. |
| Migration failure atomicity | A separate test-only copy called a deliberately missing guard function late in Up: actual **42883**. After connection rollback, all earlier table/key/index/function DDL disappeared; catalog, all original data and history matched baseline. No failed migration history row. Reviewed/source SQL were not modified. |
| Reapply | Exact SQL reapplied with receipts empty, then populated: data, schema and history unchanged. |

The source migration's idempotent SQL was regenerated with EF `IMigrator.GenerateScript` from `20261118152617_GuardGitV2FinalNormalizationReadiness` to the coverage migration. It matches the review file after line-ending normalization. Review SQL SHA-256: **`FD9BBCA36DA372D41D939909034AC49DE7382B99008875C88A3BD73DA3CAF9B3`**.

## C. Trigger and constraint integrity

- Coverage has **11 required columns**, PK `(SnapshotId, GitSyncCheckpointId)`, four validated restrictive composite FKs, one CHECK, five explicit indexes plus its PK index. Two supporting alternate keys add two more indexes on jobs/checkpoints.
- FKs bind checkpoint/job, job/association, snapshot/project and association/project. No cascade deletion of audit data.
- `uninet_snapshot_coverage_validate()` and trigger `snapshot_coverage_validate` require an open policy-2 header and exact, actually persisted Completed job/checkpoint state: project/association/repository, category, scope, item count, completion timestamps and cutoff. CHECK additionally requires nonnegative count, receipt policy 1 and scope completion no later than job completion.
- `snapshot_coverage_immutable` calls existing `uninet_snapshot_entry_guard()`, which locks the header and only permits INSERT before finalization. It rejects receipt changes/deletion and later insertion. Existing snapshot-entry behavior/function definition is unchanged.
- Capture persists selected membership and actual source receipts in the same Repeatable Read transaction; reads use persisted membership/receipts. Policy-1 snapshots cannot prove empty Review coverage and are never upgraded by this migration.

## D. Legacy preservation — PASS

Final read-only E2E comparison: **all 102 table fingerprints, full migration history and schema catalog unchanged**.

| E2E object | Before → after |
|---|---|
| Git jobs | 23 → 23, all Completed; active jobs 0 → 0 |
| Checkpoints | 447 → 447 |
| Evidence versions / observations | 100 / 646 → 100 / 646 |
| Immutable snapshots | 4 → 4, all policy 1; exact rows and entry fingerprints unchanged |
| Git Runs #1–#3 | 3 → 3, exact row fingerprints unchanged |
| Peer cohort | `5f798512-6bce-4c66-b58d-c3d5b859fae7`, Open, A+B roster retained; 0 submissions, 0 revisions, no Peer Snapshot |

Legacy Contribution, identity/ownership, membership and provider facts also match. On disposable, the only successful test business inserts were a new snapshot header/memberships and its receipts; all original snapshot rows/entries and every other baseline table remained unchanged.

## E. Exact future E2E SQL scope — NOT executed

Use only [the reviewed scoped SQL](../migrations/ProjectGitSnapshotCoverageReceipts.review.sql), after a separate user approval and renewed preflight. No ordinary `dotnet ef database update`.

1. Session `lock_timeout='5s'`, `statement_timeout='60s'`, psql `ON_ERROR_STOP=1`; one script `START TRANSACTION ... COMMIT`.
2. Add `AK_GitSyncJobs_Id_ProjectGitRepositoryId` on `(Id, ProjectGitRepositoryId)`.
3. Add `AK_GitSyncCheckpoints_Id_SyncJobId` on `(Id, SyncJobId)`.
4. CREATE `ProjectGitEvidenceSnapshotCoverage` with the PK, CHECK and four restrictive composite FKs described above.
5. CREATE the five explicit indexes listed verbatim in the reviewed SQL; the PK/alternate keys create their backing indexes.
6. CREATE only the new validation function and the two new coverage-table triggers. Reference, do not replace, `uninet_snapshot_entry_guard()`.
7. INSERT **only** `20261119164008_AddProjectGitSnapshotCoverageReceipts`, ProductVersion `8.0.11`, into EF history after successful DDL, in the same transaction.

No business UPDATE/DELETE/INSERT, no old seven migrations, no backfilled receipts and no policy-1 header/entry updates. Each operation is guarded by this migration's history ID. Reapply skips an already applied migration; it is not a repair mechanism for manually damaged schema. Execute serially, not concurrently from two deployers.

Expected after future approved execution: **86 applied / 7 pending**, coverage table empty until an explicitly approved capture, source counts/fingerprints unchanged.

## F. Backup, rollback and verification plan

Protected backup/evidence directory:
`C:/Users/ADMIN/AppData/Local/UniNetE2EBackups/coverage-safety-gate-20261120-002150/`.

- `e2e-baseline.dump`: custom format, **776634 bytes**, SHA-256 **`74bbeb62cb620f71f7ee2c50fc82684bae43d087600201bf25d183a5e753bb58`**. TOC valid; 103 TABLE DATA entries including EF history. Dump imported an exported read-only PostgreSQL MVCC snapshot so baseline fingerprints and backup share the same read point. This was a PostgreSQL backup operation, not an application evidence snapshot capture.
- Full restores into both disposable databases succeeded with exact data/history verification. Directory ACL restricts access to the current Windows user, SYSTEM and Administrators. Secrets and private peer content were not printed.
- Recheck backup hash, container/database/user/effective connection, history IDs, schema dependencies, pending IDs, worker-off/no active jobs and protected data fingerprints immediately before future E2E deployment. If real member ratings or other business data change, preserve them and take/verify a fresh backup; never restore this older backup over their work.
- Pre-commit DDL error: stop, retain error/SQLSTATE, let the transaction roll back and verify absence of new objects/history plus original fingerprints. This path was rehearsed.
- Unknown commit outcome: inspect history, catalog and data before retry. No automatic restore, history stamping or Down. After a successful deployment, favor diagnosis/forward correction; source Down intentionally refuses populated receipts. Destructive recovery needs separate review/approval.
- Post-commit verification: exact new history ID, seven old IDs still absent, expected constraints/indexes/triggers valid, zero initial receipts, old facts/snapshots/runs/cohort unchanged; health and worker-off unchanged. Do not capture or sync as a verification shortcut.

Local reproducible evidence includes the preflight/catalog queries, parser equivalence proof, failure-injected SQL copy and logs, restore fingerprints/history, generated SQL, receipt guard SQL assertions, service-verification receipt, helper source reference, TRX and final manifest. It contains protected backup data; do not commit or publish that directory.

## G. Tests, source changes and remaining risks

- Current-source isolated API/Application/Infrastructure build: **PASS**.
- Focused xUnit: `GitSnapshotCoverageValidationTests` + `GitSnapshotEvidenceValidatorTests`: **21 PASS / 0 FAIL / 0 SKIP**. No repeated runs added to counts. Existing warnings remain non-failing. This task did not rerun the full previously accepted 247-case suite.
- Actual PostgreSQL restore/migration/service/guard/rollback/idempotency checks: **PASS**, separately evidenced above; not miscounted as xUnit cases.
- Current EF model vs migration snapshot (`HasPendingModelChanges`): **false**. Review SQL matches regenerated migration. This does not imply the deliberately partially migrated E2E has zero pending migrations.
- `git diff --check`: **PASS**. No production code, migration, model or reviewed SQL change was needed. Repository changes in this task: this report and the latest handoff pointer only.
- DDL takes locks on jobs/checkpoints; keep workers off and use bounded timeouts. Re-preflight at execution because readiness is time-specific. History idempotency assumes the matching schema is intact.
- Backup restore normalizes physical dropped-column holes and expression formatting; normalized constraint semantics and actual original row values were verified, not inferred from object names.
- This receipt migration does not fetch missing V5 facts, establish Peer completeness, solve partial patch overlap/force-push limitations or authorize publication of the alternate roster. Broad readiness stays 503 while global Git sync is off and seven migrations remain pending.

## H. Approval boundary and V5 continuation

**READY FOR SCOPED E2E MIGRATION APPROVAL.** No unresolved dependency or integrity blocker was found for this exact migration on the verified baseline.

Next approval must name only `20261119164008_AddProjectGitSnapshotCoverageReceipts` and the verified E2E target. After that deployment, separate approvals remain necessary for one pinned official Manual sync to fetch real Issue Comment V5 facts, then one new policy-2 snapshot freezing real completed Review coverage receipts, then Git-only diagnostics. Keep actual missing facts/partial-overlap/force-push limitations. None of those actions was executed here. Members independently submit real evaluations to the existing cohort; this task did not act as either member or close it.
