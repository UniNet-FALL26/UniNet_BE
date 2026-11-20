# UNINET — 8 missing EF migrations / Git V2 deployment path

## Executive decision

**The two reviewed Git V2 migrations can be applied independently to this restored database without changing historical rows. The complete current backend is NOT compatible with that database after only those two migrations.** A real EF Project query fails with SQLSTATE `42P01`, because Project.Specialization is AutoIncluded and its table is missing.

Recommend **option B with an explicitly expanded, separately reviewed additive prerequisite**: obtain approval to include `AddProjectSpecializations` in the E2E deployment scope, rehearse the expanded scope on disposable before any E2E write, then deploy the required scripts by exact migration ranges, recording only migrations actually executed. Do not apply or falsely stamp the other seven missing migrations. Two V2 migrations alone are a valid schema change but do not deliver a usable current Git V2 API.

**No E2E changes were made.** No backend was started, no live GitHub calls/sync, no ratings, no new Run, no finalization, no commit/push. Old migration source and model snapshot were not modified.

## 1. Audit scope and reproducible evidence

Read/inventoried all **187 migration-directory C# files**: 92 migrations, 92 designers, two archive partials and the current model snapshot. Loaded all 92 migrations through EF, inspected their Up/Down operations and target models, and extracted target relational models around the eight missing migrations. Compared all 82 E2E history IDs, rather than just the latest row. [Inventory](../migrations/GitMigrationInventory.audit.csv) records every migration's Up/Down operation counts and source/designer hashes.

The reusable [read-only audit SQL](../migrations/GitV2MigrationReconciliation.audit.sql) inventories actual column types/nullability/defaults, FK/key/check definitions, validated status, unique/index column definitions and predicates, trigger definitions and source-data counts. It does not apply or stamp migrations. Source/target-model inventories and full before/after catalogs are retained locally under:

`C:/Users/ADMIN/AppData/Local/UniNetE2EBackups/pre-v2-20261118-231652/`

In particular: `reconciliation-before/`, `reconciliation-after/`, `reconciliation-e2e-catalog.txt`, `reconciliation-disposable-v2-catalog.txt`, and `reconciliation-v2-objects.txt`. These contain metadata/fingerprints rather than private evidence payload dumps.

## 2. Verified environments

| Use | Container | Host/port | Database/user |
|---|---|---|---|
| E2E, read-only throughout | uninet-step2-concurrency | 127.0.0.1:55433 | uninet_github_app_e2e / uninet_test |
| Restored backup, V2-only writes | uninet-v2-restore-20261118 | 127.0.0.1:59026 | uninet_v2_restore_disposable / uninet_test |

Container IDs match the previous [approval preflight](UNINET_GIT_V2_E2E_MIGRATION_PREFLIGHT_BLOCKED_20261118.md). Actual PostgreSQL database/user and Docker port bindings were verified. Read-only E2E SQL used `default_transaction_read_only=on`; EF probes/tooling used explicit disposable connection strings, not the default environment. No credentials were printed.

Before writes, the restored database still matched backup fingerprints and current E2E for all **96 existing application tables**. Both had the same 82 history rows. The dump SHA-256 remains `75f414050019371b02b1f514546e1835574e3da0b890c13f297f548a4d7668a9`.

## 3. Exact eight missing migrations and classification

Every row below is Pending because its ID is absent from E2E history. Inspection also finds that its intended new schema is absent and/or its old schema remains. **No row qualifies as A (fully matching already-applied schema) or C (demonstrated partial application).** For destructive migrations, D takes precedence; structurally their intended Up effect is also absent.

| ID / source | Class | Observed PostgreSQL evidence | Role / risk |
|---|---|---|---|
| [20261106081602_ConsolidateVerificationPayments](../../UniNet.Infrastructure/Data/Migrations/20261106081602_ConsolidateVerificationPayments.cs) | D | Transactions.VerificationId and VerificationReservationId absent; old PaymentUrl and VerificationTransactions remain. New FK/index absent. | Transfers payment relationships, drops source table and URL column. Not an audit-history-only omission. |
| [20261106141923_AddAssessorOnboarding](../../UniNet.Infrastructure/Data/Migrations/20261106141923_AddAssessorOnboarding.cs) | B | AssessorOnboardings and AssessorOnboardingEmails absent, including their keys/FKs/indexes. | Additive assessor module, unrelated to Git scoring. |
| [20261107051900_AddAssessorContractSigning](../../UniNet.Infrastructure/Data/Migrations/20261107051900_AddAssessorContractSigning.cs) | B | Both prerequisite tables absent; its twelve additional columns and replacement indexes therefore absent. | Depends on AddAssessorOnboarding, not independently deployable as-is. |
| [20261108023501_AddAssessorPasswordTokenIndex](../../UniNet.Infrastructure/Data/Migrations/20261108023501_AddAssessorPasswordTokenIndex.cs) | B | AssessorOnboardings and its unique SetPasswordTokenHash index absent. | Depends on assessor schema. |
| [20261108142140_ArchiveAndRemoveLevel1](../../UniNet.Infrastructure/Data/Migrations/20261108142140_ArchiveAndRemoveLevel1.cs) | D | EvidenceModerationAttempts, old moderation columns/keys/indexes, composite ProjectVerification link and Purpose IN(0,1) check remain. l1_archive/manifest/triggers absent. | Would archive/detach/delete active L1 data and remove schema. One existing Purpose=1 row would leave the active moderation table. |
| [20261109013212_ArchiveAndRemoveVerificationEvidence](../../UniNet.Infrastructure/Data/Migrations/20261109013212_ArchiveAndRemoveVerificationEvidence.cs) | D | Public ProjectVerificationEvidence, its FKs/unique version index/check remain; verification_evidence_archive absent. | Archives then drops active evidence table. |
| [20261109090826_AddProjectSpecializations](../../UniNet.Infrastructure/Data/Migrations/20261109090826_AddProjectSpecializations.cs) | B | Projects.SpecializationId, Specializations, AssessorSpecializations, associated keys/FKs/indexes and three seeds absent. | **Concrete current Project query dependency.** Additive; needs separate approval/rehearsal. |
| [20261114014337_ArchiveAndRemoveExternalProjectFiles](../../UniNet.Infrastructure/Data/Migrations/20261114014337_ArchiveAndRemoveExternalProjectFiles.cs) | D | Old ExternalProjectDraftFiles / ProjectEvidenceDocuments and four draft lease/retry columns remain. external_project_file_archive absent. | Archives file links, updates draft lifecycle and removes tables/retry metadata. |

The existing `verification_archive` schema belongs to a different previously applied verification-history migration. Its existence is **not** evidence that `ArchiveAndRemoveVerificationEvidence` ran. Its history manifest matches between live E2E and the restored copy.

No migration-execution provenance was found proving any of these eight was applied by another mechanism. Current schema contradicts their completed Up postconditions. This does not reconstruct the original operator's intent or rule out a past apply-and-rollback; no such chronology is claimed. Do not insert history rows based on similar names or old archive schemas.

### Up/Down and data impact

- **Payments:** Up adds two nullable UUID columns, transfers references with UPDATE, adds a restrictive verification FK/index, drops VerificationTransactions and PaymentUrl. It refuses pending URL-only checkouts without QR. Down recreates the old relation and inserts relationships back, but does not reconstruct historical URL values. Both source tables currently have zero rows; that does not authorize destructive schema work.
- **Onboarding:** Up creates two tables (UUID PKs, required/nullable source-defined columns), restrictive Account FKs, email/token uniqueness and filtered open-status indexes. Down drops both tables and their data. No rename or data migration.
- **Contract signing:** Up adds 12 typed columns with source-defined defaults/nullability, removes one index and creates four indexes; Down removes these fields/indexes and restores the old index. No explicit UPDATE/INSERT/DELETE data SQL; adding required columns with defaults still affects existing rows if present.
- **Password token:** Up creates one unique index; Down drops it. No data SQL.
- **L1 removal:** Up's archive partial locks source tables, verifies dependency/trigger conditions, creates private immutable archive copies/manifests/checksums and frozen verification links; then DELETEs moderation attempts, NULLs live verification links, DELETEs Purpose=1 moderation rows, and removes one table, sixteen columns and old constraints/indexes. Down verifies archive checksums/schema/parents/ID collisions before recreating/restoring and removing archive objects. It must not be treated as a harmless additive migration or an unconditional rollback.
- **Verification evidence removal:** Up's archive partial creates verified immutable copies plus parent/submitter manifests, then drops the live evidence table. Down validates the archive, recreates its typed/FK/check/index structure, restores records and removes archive objects. Not equivalent to the older verification_archive feature.
- **Specializations:** Up adds one nullable UUID Project FK, creates two tables, restrictive specialization FKs, an assessor-user cascade FK, composite/unique keys and three indexes; INSERTs three static specialization catalog rows. It does not assign a specialization to existing projects. Down drops those structures/data. Its Up does not need the assessor onboarding tables.
- **External files:** Up copies link rows into a private archive schema, UPDATEs draft status/error/version/time, drops two live tables, their index and four lease/retry columns. Down recreates structures/restores link rows, then drops archives; it does not reconstruct all removed retry/lease values or the previous draft lifecycle. Archive presence alone is insufficient proof of recoverability.

No rename operation occurs in these eight. Explicit DML is in payments, L1 archive/removal, specialization seeding and external-file lifecycle handling; Down also contains restoration INSERT/UPDATE SQL. No Task/TaskReport deletion was executed or proposed.

### Existing dependencies counted, without reading private contents

Transactions=0; VerificationTransactions=0; EvidenceModerationAttempts=0; ProjectVerificationEvidence=0; ProjectVerifications=0; ExternalProjectDrafts=0; ExternalProjectDraftFiles=0; ProjectEvidenceDocuments=0. ProjectModerations=2: **one Purpose=0 and one Purpose=1**. Old validated FK/check/index relationships were inventoried, including the composite moderation-purpose link. Zero row counts in some tables do not justify stamping history or running unrelated migrations.

## 4. Disposable V2-only application: PASS

Applied exactly the existing [review SQL](../migrations/GitPolicyV2PeerCohorts.review.sql), matched against freshly generated current-source SQL. No old migration, manual history INSERT/DELETE or legacy data repair was performed. History rows for the two V2 migrations were inserted by the actual approved migration SQL, after its operations.

- `20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2`: applied.
- `20261118152617_GuardGitV2FinalNormalizationReadiness`: applied.
- First application: exit 0, no SQL/schema conflicts.
- Idempotent second application: exit 0, history remains 84, no duplicate objects/history rows.
- Four peer tables created; five nullable V2 run columns have correct types/nullability. Peer composite/scoped FKs and restrictive delete behavior, replay/revision/cohort uniqueness, V1 filtered identity and V2 context identity match the current model.
- Programmatic catalog comparison verified **49 keys/FKs/indexes/checks** across the four peer tables and GitContributionRuns: names, column composition, FK principal/columns/delete actions, uniqueness/validity/readiness; check constraints present and validated. Full SQL definitions/predicates are retained in the audit catalog, including the V1/V2 filtered indexes and final-readiness check.
- All eight new triggers are enabled: peer_cohort_guard, peer_submission_guard, peer_history_immutable, peer_history_insert_guard, peer_snapshot_immutable, peer_snapshot_insert_guard, deferred peer_snapshot_must_close, git_v2_inputs_immutable. Existing finalized-run/evidence/snapshot guards remain. This is catalog verification, not a new end-to-end PostgreSQL rating/immutability test claim.
- All 96 old application tables have identical ordered row-content fingerprints before/after, comparing pre-existing fields (excluding the five new nullable run columns). No non-null V2 fields were backfilled into old runs.
- New peer tables have **zero rows**; V2 run count=0. Old runs remain Draft with original hashes; old snapshots, evidence, identities, memberships and legacy cycle unchanged.

## 5. EF/runtime compatibility: partial, concrete blocker remains

| Check | Before V2 | After V2 |
|---|---|---|
| EF history rows | 82 | 84 |
| EF pending migration IDs | eight old + two V2 | exactly eight old |
| Current-model GitContributionRuns query | SQLSTATE 42703, missing CalculationContextHash | PASS |
| Current-model Project query for actual E2E project | SQLSTATE 42P01, Specializations missing | **Same failure** |
| EF pending-model-changes | model/snapshot match | PASS; does not prove DB parity |

[SpecializationConfiguration](../../UniNet.Infrastructure/Data/SpecializationConfiguration.cs) AutoIncludes Project.Specialization, causing the join to the nonexistent table. Projects.SpecializationId is also missing. This is a demonstrated read failure, not speculation based on pending history. The Git/peer services query Projects, so merely adding V2 tables cannot make their real flow usable with this binary.

Remaining model-column inventory after V2: four missing tables (AssessorOnboardings, AssessorOnboardingEmails, AssessorSpecializations, Specializations), Projects.SpecializationId, Transactions.VerificationId and Transactions.VerificationReservationId. No store-type/nullability mismatches were found among the current model's columns that actually exist. Missing onboarding/payment structures remain limitations for their respective features; absence of failures in Git unit tests does not prove full-application DB compatibility. Old extra columns/constraints also remain and were not removed.

EF migration tooling correctly leaves the eight unexecuted migrations Pending. **Future `database update` would still attempt them**, even though the newest two history IDs exist. No history repair or migration filtering was introduced. Schema source/snapshot consistency and database migration completeness are separate checks.

## 6. Focused verification

Current-source isolated build via test project: PASS (API/Application/Infrastructure references built), one existing CS9113 warning in this run. **28 PASS / 0 FAIL / 0 SKIP**:

- GitPeerNormalizationTests: 5.
- ExperimentalGitScoringV2Tests: 2.
- GitExperimentalContributionServiceTests: 16.
- ContributionPercentAllocatorTests: 5.

These run in unit/SQLite fixtures, not by inserting ratings into the restored E2E records. No fake evaluation was added to either PostgreSQL target. Actual PostgreSQL validation above covers exact migration application/replay, metadata, immutable input preservation and real EF reads. Expected Project read failure remains a deployment blocker, not a passing API test. No HTTP backend/API was launched; the SQL/EF reproduction already proves the incompatibility.

EF pending-model-changes and `git diff --check` passed. Existing line-ending warnings are separate from test results. No production implementation changes were needed or made in this audit.

## 7. Deployment alternatives

| Option | Assessment |
|---|---|
| A — reconcile all eight first | Broadest consistency, but not minimal: retires live L1 schema/data and changes unrelated payments/onboarding/external files. Requires separate business/rollback review. Do not run automatically. |
| B — exact V2 SQL only | **Proven safe for these V2 schema operations/data**, truthful history for two actual migrations. Leaves eight pending and demonstrated Project dependency unresolved. Suitable as a scoped schema step, insufficient to claim Backend ready. |
| C — new reconciliation migration | Only if a distinct compatibility delta is necessary. A new migration alone does not prevent EF from running older pending ones. Duplicating an existing specialization migration would create future collisions; prefer approving that existing additive migration over inventing its duplicate. Never use a reconciliation migration to falsely stamp destructive migrations. |
| D — new E2E database | Last resort. Existing data is intact and backup restoration works; no evidence demands abandoning this database. A fresh schema needs a separately approved migration/data-transfer plan to preserve OAuth identity keys, project/roster and immutable evidence/history. |

## 8. Exact next approval requested

1. **Do not apply anything to E2E based solely on this report.** Existing conditional approval was not exercised there.
2. The E2E approval needed is to expand the deployment scope by **`20261109090826_AddProjectSpecializations`**, the smallest demonstrated Project dependency, followed by the two V2 migrations. It does not require applying the assessor/payment/archive migrations. The expanded three-migration deployment is not yet tested here because applying any of the eight old migrations was explicitly forbidden in this task. No separate permission is requested for ordinary disposable verification; after scope approval, rehearse it there before writing to E2E.
3. That expanded E2E change would add the nullable Project specialization FK/catalog/join table/three static catalog seeds, plus the four peer tables and five V2 columns/guards already reviewed. Execution must remain conditional on the combined disposable rehearsal and current-source Git/Peer Project reads passing. Each script records only its genuinely executed migration ID. No existing Project specialization assignment, ownership transfer, cohort, rating, Run or snapshot rewrite.
4. Keep the remaining seven old migrations explicitly pending. Do not use blanket EF database update/startup migration. Keep unrelated features/workers requiring their missing schema disabled or out of this scoped E2E test; review broader reconciliation separately. This is a documented limited deployment, not full migration-history convergence.
5. Refresh and restore-verify a backup if E2E has changed since the approved baseline; recheck all history IDs and old-data fingerprints immediately before execution. Use bounded lock/statement timeouts and quiet Git Contribution writers. If deployment fails, retain actual committed history and assess forward recovery; do not run destructive Down or restore over E2E automatically.

This recommends a concrete path; it does not implement a bypass or authorize broader migrations. If approval must remain **exactly two migrations and no additive prerequisite**, the current backend's Project dependency must be addressed separately before claiming a working V2 E2E flow.

## Final status

**Audit/disposable V2 validation COMPLETE. E2E deployment pending approval of the demonstrated additive dependency and its disposable rehearsal.** E2E remains 82 history rows and unchanged data; disposable is 84 rows with only the two V2 migrations added. No automatic history stamping, old-migration execution or production access occurred.
