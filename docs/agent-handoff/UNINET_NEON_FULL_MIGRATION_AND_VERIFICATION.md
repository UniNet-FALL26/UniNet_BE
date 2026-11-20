# UNINET — NEON FULL MIGRATION AND VERIFICATION

Date: 2026-11-20. Source: `dattt`, HEAD `d425af23a4d427b4d8abc87ec752545c771fe8cf` plus the existing uncommitted cleanup changes. No commit, reset, migration-source rewrite or application-source change was made during this Neon investigation.

## Outcome

**LIVE MIGRATION HELD — WRITER ISOLATION / DEPLOYMENT IDENTITY NOT VERIFIED.**

The Neon target, backup, actual-data restore, pending-only migration rehearsal, rollback and restored application-model reads passed. This is not a live deployment receipt. Neon still has **77 applied / 26 pending** migrations; no live DDL, history inserts, application data writes, GitHub sync, contribution finalization or login was executed by this task.

The remaining live safety gate is identifying the existing deployment(s) that use this Neon branch and quiescing conflicting writers. A momentarily empty `pg_stat_activity` does not prove that a Render service or worker cannot reconnect. The branch's development/staging/production classification and an authorized Neon smoke-test account also remain unconfirmed. Questions were sent to the user; no response had been received when preparing this report.

## 1. Effective connection and read-only identity

The application's key is `ConnectionStrings:UniNet`, consumed by [Program.cs](../../UniNet.API/Program.cs). [LocalEnvConfiguration.cs](../../UniNet.API/Services/LocalEnvConfiguration.cs) loads the API-local environment file, otherwise the repository environment file, then reapplies environment variables and CLI arguments. In this environment the API-local file and relevant shell overrides were absent, so the repository `.env` supplied the effective value. It was read directly and not edited. No alternate database key was selected.

Verified identity, excluding all secret connection parameters:

| Component | Verified value |
|---|---|
| Host | `ep-cool-shape-az1vmy1u-pooler.c-3.ap-southeast-1.aws.neon.tech` |
| Port | 5432 |
| Database / role | `neondb` / `neondb_owner` |
| TLS | `VerifyFull`, hostname validation retained |
| Neon project | `wispy-sea-88460496` |
| Neon branch | `br-proud-fire-azt68awx` |
| Neon endpoint | `ep-cool-shape-az1vmy1u` |
| Server | PostgreSQL 18.7 |

Neon project/branch/endpoint were checked using server-side `neon.*` settings. A proxy-internal server address is not evidence of a localhost target. Connections used explicit Repeatable Read transactions followed by `SET TRANSACTION READ ONLY`, UTC, bounded lock/statement timeouts, and rollback. No password, token or full connection string is included in receipts or this report.

## 2. Source/history reconciliation

- Source contains 103 migrations. All 103 migration source SHA-256 values match [the source inventory](../migrations/MergedSourceMigrationInventory.20261120.csv).
- Neon contains 77 migration IDs. Every one exists in current source; there are no unknown history IDs.
- Pending IDs were computed by full ID-set difference, not by the last history row.
- The seven migrations missing from the earlier local E2E database are already applied on this Neon database. They must not be replayed here.
- Existing archive schemas are present. Their presence was checked against data and checksums, not merely object names.

### Exact pending execution order and effects

| Migration | Main Up effect / data consideration |
|---|---|
| `20261104155132_RestoreProjectGitMemberIdentities` | Identity/ownership-history tables and indexes. |
| `20261104161008_AddGitSyncInfrastructure` | Jobs/checkpoints and association sync fields/constraints. |
| `20261104162306_AddGitActivityVisibility` | Provider objects, activities, grants and visibility structures. |
| `20261104162504_AddGitCommitBranchReferences` | Branch references and index replacement. |
| `20261104162721_AddGitHubWebhookDeliveries` | OAuth webhook delivery storage. |
| `20261104163219_AddProjectGitActivityVisibility` | Association-scoped visibility. |
| `20261106062750_AddGitHubAppInstallationPersistence` | Installation, grant, authorization-flow structures. |
| `20261106064420_AddGitHubAppUserAuthorization` | Quarantines pre-OAuth flows, removes them from active lookup, adds OAuth binding/user credentials. The flow table does not exist in the baseline and is created empty by the preceding pending migration; quarantine count after rehearsal is zero. No existing business records are deleted. |
| `20261106071727_AddGitRepositoryCredentialSource` | Association credential source and FK/check changes. Existing association count is zero. |
| `20261106084237_AddGitSyncJobCredentialSource` | Job credential source and FK/check changes. Job table is new in this plan. |
| `20261106085834_AllowGitHubAppInitialActivation` | Lifecycle CHECK replacement. |
| `20261108030751_AllowGitHubAppSyncLifecycle` | Lifecycle CHECK replacement. |
| `20261108233255_AddGitHubAppReconciliationState` | Reconciliation metadata/check/index. |
| `20261109140808_AddGitHubAppWebhookDeliveries` | App webhook delivery storage. |
| `20261109142804_AddGitHubAppWebhookConsumer` | Consumer fields and CHECK replacement. |
| `20261109235311_AddGitHubAppWorkerHeartbeats` | Heartbeats. |
| `20261110162206_AddGitHubAppWebhookRequeueAudit` | Requeue audit. |
| `20261113165539_AddGitEvidenceVersioning` | Immutable versions and EP-06 observations. |
| `20261113232726_HardenGitEvidenceCrossRepositoryIntegrity` | Repository identity backfill, composite keys/FKs; temporarily disables and re-enables the version trigger within the transaction. Evidence/observations are newly created empty tables here. |
| `20261114070301_AddAppAuthorizationGeneration` | Grant/installation generations, CHECKs and atomic generation triggers. |
| `20261114151603_AddProjectGitEvidenceSnapshots` | Persisted immutable snapshot membership and guards. |
| `20261114234836_AddGitExperimentalContributionRuns` | Experimental run storage and immutability. |
| `20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2` | Peer cohort/submission/history/snapshot and V2 input guards. |
| `20261118152617_GuardGitV2FinalNormalizationReadiness` | Final-normalization readiness CHECK. |
| `20261119164008_AddProjectGitSnapshotCoverageReceipts` | Frozen coverage receipts, composite keys and guards. |
| `20261119190820_AddOfficialContributionV2Workflow` | Official workflow/run/audit/publication tables and guards. Seeds 13 existing projects as `RequiresOptIn`; it does not opt them into Official V2. New-project insertion trigger is a rollout compatibility dependency. |

No pending Up drops an existing business table. Constraint/index replacement is not equivalent to deleting business rows. Down methods were not executed as a recovery mechanism.

## 3. Protected backup and actual-data rehearsal

Backup/receipts are outside Git under `%LOCALAPPDATA%\UniNetNeonAudit\20261120-064939`, with access restricted to the current Windows user and SYSTEM. The protected pointer is `%LOCALAPPDATA%\UniNetNeonAudit-current.txt`.

| Check | Result |
|---|---|
| Consistent exported read point | PASS; read-only exported MVCC snapshot shared with `pg_dump` |
| Backup format / size | PostgreSQL custom / 795,984 bytes |
| Backup SHA-256 | `8F52864195FBC53D59D3F32C0D10AA43A111883998769BB54180546BC1D1CDCB` |
| Backup timestamp | 2026-11-20 01:29:07 UTC |
| TOC / copied-file checksum | PASS |
| Actual restore | PASS; 78/78 table count+content fingerprints and 77/77 history IDs match |
| Existing legacy archive manifest | PASS; 11/11 archived table documents, 89 archived rows, valid counts/checksums |

The Linux dump client initially lacked the CA trust needed for `VerifyFull`; this was corrected with public trusted root certificates from the Windows trust store, excluding disallowed certificates. TLS/hostname checks were not weakened. Pooler-unsupported startup timeout options were removed; read-only startup enforcement, bounded process duration and dump lock timeout remained. No direct endpoint or alternate Neon database was substituted.

PostgreSQL documents use of an exported snapshot for a consistent dump in [pg_dump](https://www.postgresql.org/docs/18/app-pgdump.html). The backup export transaction remained open while the dump imported its snapshot, then was rolled back/released.

### Rehearsal environment

- Container `uninet-neon-rehearsal-20261120`, ID `da52fa22c064cc5003fdf884d9e3714162ee74ae9d3b7303232541a676458cca`.
- Actual published endpoint `127.0.0.1:52244`; PostgreSQL 18.7, builtin `C.UTF-8` locale matching the source provider.
- Database `uninet_neon_restore_disposable`, application-object owner `neondb_owner`.
- Neon ACL role names needed for restoring the dump were created **only in this disposable container**. A temporary role membership needed to restore default ACLs was revoked afterward. No live roles or ACLs were altered.
- This is an actual-data PostgreSQL copy rehearsal, not a Neon control-plane branch. No Neon branch-management credential/tool was available.

### Pending-only script

`pending-copy-rehearsal.sql`, protected receipt directory, SHA-256:

`6C9640821AE4645438BF28F07AEE4A0C3798DBB7F62CBABC4D3FE77ABA3EF21A`

It was generated from the 26 actual pending `UpOperations`, with explicit migration-command terminators. It contains one outer transaction, `ON_ERROR_STOP`, `lock_timeout=5s`, `statement_timeout=60s` and each migration's history insert in that transaction. No transaction-suppressed operations were present. It is marked **copy rehearsal only**; a live execution wrapper still needs final identity/history/drift and writer-isolation gates. No blanket `dotnet ef database update` was run against Neon.

## 4. Rehearsal results

| Scenario | Result / evidence |
|---|---|
| All 26 pending migrations on actual restored data | PASS, one committed transaction; 103 applied, zero pending |
| Existing table preservation | PASS; only `__EFMigrationsHistory` changes among the original 78 tables |
| New tables | 34 added, 112 total application/archive tables |
| Existing Project/Member/Verification/Contribution data | Original fingerprints unchanged |
| Archive integrity after upgrade | 11/11 documents and 89 rows still valid |
| All EF mapped table/column reads | PASS, 96 tables; all 1,003 column names, types and nullability match PostgreSQL |
| Real EF materialization | Projects 13, cycles 3, verification reports 2, join requests 14, recruitments 8; Git runs 0 |
| Model consistency | PASS, no pending model changes |
| Constraints/triggers | Zero unvalidated public constraints, zero disabled user triggers |
| Official workflow seed | Exactly 13 `RequiresOptIn`; zero official runs |
| Deliberate failure after all DDL, before COMMIT | PASS rollback; independent baseline copy returns to all 78 original fingerprints and 77 history IDs |
| Accidental second execution of non-idempotent script | Refused on existing relation; transaction rolls back. This is not an idempotent deployment script; normal tooling correctly reports zero pending on the successfully migrated copy. |

The failure copy uses a no-ACL restore solely for transaction rollback testing. The successful primary restore included ACL restoration. No fabricated ratings or finalization were executed using actual Neon user/project data.

## 5. Build and regression

Current-source API build: **PASS, zero errors, six warnings**. Artifacts are isolated under the temporary directory; the running E2E binary was not replaced.

Fresh current-source regression: **1,130 PASS / 0 FAIL / 65 SKIP, 1,195 distinct test IDs**, including **157 PASS in PostgreSQL test classes**. Repeated executions are not added to totals.

The initial full execution was 1,129 PASS / 1 FAIL / 65 SKIP. The one failure was `LocalEnvConfigurationTests.RuntimeFindsRepoEnvAndCommandLineOverridesIt`: the regression harness's `UNINET_SKIP_LOCAL_ENV_FILE=true` intentionally prevented environment-file loading, including the test's own temporary fixture file. The two-test configuration class was rerun without that flag and passed 2/2. No production/test source change was needed. The final unique-ID tally replaces those same two test IDs with their corrected-harness results; it is not a claim that the first execution had zero failures. Both TRX files and the aggregation explanation are retained.

The 65 existing opt-in fixtures remain SKIP; they were not enabled against the live E2E database. Their exact IDs/names are recorded in protected `test-results/skipped-tests.csv`. No authenticated live Neon API coverage is inferred from these disposable tests.

Full regression uses a separate PostgreSQL 18.7 container `uninet-main-merge-neon-regression-20261120`, published at `127.0.0.1:63525`, explicitly named disposable fixture databases and an environment override preventing accidental `.env`/Neon use. No test fixture targets 55433.

Both containers created for this task were stopped after verification, with their data retained: actual-data rehearsal at 52244 and independent regression fixtures at 63525. Neither stop targeted the existing E2E container or API. The protected migration-copy schema catalog contains 1,112 public constraints, 386 public indexes, 24 user triggers and 21 public functions; zero constraints were unvalidated and zero user triggers disabled.

`git diff --check`: PASS (line-ending warnings only).

## 6. Live preservation and runtime

Final read-only Neon verification at 2026-11-20 01:46:01 UTC:

- 78 table fingerprints still match the consistent backup.
- History remains 77 IDs, identical to backup; all 26 remain pending.
- No live schema/application-data mutation by this task.
- No credentials or tokens were printed.

Existing local E2E API on port 5239 remains PID **40756**, health **200**. It was not stopped, restarted, migrated or repointed to Neon. This health response is not proof of a Neon deployment. The current built API was not launched against the unmigrated live Neon schema.

Authorized Neon login, authenticated project/verification/official-workflow API smoke tests, and Render replacement are **NOT EXECUTED**. An E2E credential must not be assumed to authorize an unrelated Neon account; no JWT was forged and no account/password was reset.

## 7. Remaining rollout gate and recovery plan

Before live execution:

1. Identify the branch's environment classification and every backend/worker using it; obtain the actual service URL/ID and a way to quiesce writers.
2. Identify an authorized Neon smoke-test account and its existing protected credential location. Do not send passwords/JWTs in chat.
3. Freeze conflicting old writers, especially Project creation and Git/App workers. Old binaries do not understand authorization generation or the Official V2 project-workflow trigger. Do not assume mixed workers are safe.
4. Re-read identity, full history/catalog and fingerprints. Any new legitimate user data must be preserved; refresh backup/rehearsal as needed rather than restoring over it.
5. Use the exact pending-only plan, one bounded transaction, and deploy the compatible binary while writers remain controlled.
6. Verify durable history, catalog, business fingerprints and archive checksums before enabling functionality. Require a fresh authorized fetch for in-flight Git work spanning the rollout.
7. Run read-only authenticated API smoke checks with the approved Neon account, then enable only verified workers.

If the migration transaction fails, verify durable history/catalog and rollback before considering a retry. If commit outcome is unknown, inspect first. Do not execute destructive Down methods or restore the backup over newer live user data. After successful commit, use a reviewed forward fix or compatible application rollout; restore into a separate recovery target for investigation.

## 8. Files changed by this investigation

- This report.
- The latest Neon handoff section in [NEXT_AGENT](UNINET_GITHUB_CONTRIBUTION_NEXT_AGENT.md).
- Protected backup, read-only receipts, generated copy-only SQL and temporary helper projects outside the repository.

All pre-existing uncommitted application/test/documentation changes were preserved. No migrations, `.env`, scoring policies, identity ownership, E2E configuration, watchdog or launcher were edited.

**Final deployment status: BLOCKED — LIVE WRITER ISOLATION AND DEPLOYMENT IDENTITY UNVERIFIED.** Backup and actual-data rehearsal are successful; Neon is not claimed to be migrated or deployed.
