# Git V2 / Peer E2E migration approval — execution blocked

## Outcome

**No E2E migration was applied.** Conditional approval requires exactly two pending migrations; the current EF migration assembly reports **ten**. The extra eight are genuine EF migrations, not the `.Archive.cs` helper files. No migration history was fabricated or repaired. Backend startup and HTTP authentication were not attempted because they were conditional on migration success.

This corrects the earlier [preflight report](UNINET_GIT_V2_E2E_PREFLIGHT_20261118.md): checking only the latest applied migration did not detect holes earlier in the migration history. A clean EF pending-model-changes check does not establish that the live database has applied every older migration.

## Verified target

- E2E container `uninet-step2-concurrency`, ID `2fbe020661be4f7ae4a7fd6eac82524b6f3f33dad8ad74da7981e1ce3eb9f767`.
- Docker binding `127.0.0.1:55433`; database `uninet_github_app_e2e`; user `uninet_test`.
- E2E configuration parsed and checked before EF commands; explicit connection passed to EF with read-only PostgreSQL session options. No default, Neon or production connection was used.
- E2E has 82 migration history rows, latest `20261114234836_AddGitExperimentalContributionRuns`. All 22 sync jobs are Completed; no Pending/Running/RetryScheduled jobs were found.

## Backup restore rehearsal: PASS

- Existing custom-format backup SHA-256 was reverified on host and container: `75f414050019371b02b1f514546e1835574e3da0b890c13f297f548a4d7668a9`.
- Restore was performed with `pg_restore --exit-on-error --single-transaction`, exit 0, in a **new separate disposable PostgreSQL 17 container**:
  - name `uninet-v2-restore-20261118`;
  - ID `ae82d4770dce7fb754e515b7c7bc670793b9e689b8d2a2c4cd5d2f19ce4f7eeb`;
  - binding `127.0.0.1:59026`;
  - database `uninet_v2_restore_disposable`, user `uninet_test`.
- All **96 existing application tables** matched E2E row counts and ordered JSON content fingerprints after restore; the restored migration history also matched the 82-row baseline. This includes Runs #1–#3, evidence versions/observations, snapshots/entries, legacy cycle/participants/results, identities, memberships and account records. No sensitive row contents were printed.
- A final read-only fingerprint comparison confirmed that the same 96 E2E tables remained unchanged throughout preflight. No new run, peer cohort, rating or evidence was created.
- Initial attempt to create a rehearsal database using the E2E role was denied (`permission denied to create database`); no database or role change resulted. The separate container resolved that fixture setup issue without changing E2E privileges.
- Backup and rehearsal artifacts remain at `C:/Users/ADMIN/AppData/Local/UniNetE2EBackups/pre-v2-20261118-231652/`, including the dump, manifest, restore fingerprints and comparison query. The disposable restored database is retained for inspection.

## Exact blocking migration history

`dotnet ef migrations list` against the explicitly verified read-only E2E connection reported these additional Pending migrations:

1. `20261106081602_ConsolidateVerificationPayments`
2. `20261106141923_AddAssessorOnboarding`
3. `20261107051900_AddAssessorContractSigning`
4. `20261108023501_AddAssessorPasswordTokenIndex`
5. `20261108142140_ArchiveAndRemoveLevel1`
6. `20261109013212_ArchiveAndRemoveVerificationEvidence`
7. `20261109090826_AddProjectSpecializations`
8. `20261114014337_ArchiveAndRemoveExternalProjectFiles`

The two approved Pending migrations are also present:

- [20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2](../../UniNet.Infrastructure/Data/Migrations/20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2.cs)
- [20261118152617_GuardGitV2FinalNormalizationReadiness](../../UniNet.Infrastructure/Data/Migrations/20261118152617_GuardGitV2FinalNormalizationReadiness.cs)

Calling EF `database update` with the final target would also attempt older pending migrations. That exceeds approval and includes archive/remove operations. **It was not run.** The exact [reviewed SQL range](../migrations/GitPolicyV2PeerCohorts.review.sql) still matches freshly generated current-source SQL; this range excludes the eight older migrations. Executing this scoped SQL instead is a possible limited deployment route, but does not resolve the unrelated schema/history drift or prove that the new full backend is compatible with it. Do not stamp the missing eight as applied.

## Credential correction

- Student A: saved credential matches the current ASP.NET Identity password hash.
- Student B: **the newly specified** `C:/Users/ADMIN/AppData/Local/UniNetGitHubAppStep6E2E-StudentB/student-b.dpapi` **matches** the current hash. Use this file in the next E2E operation; the older file in the original Step6E2E directory is stale.
- Verification decrypted protected credentials only in memory; no password/token was logged, no account was reset, and no login/session mutation occurred.
- HTTP login for both accounts: **NOT RUN**, because migration preflight blocked startup. Hash matching is not presented as a successful HTTP login.

## Build/model/testing

- Current-source isolated API build: PASS, 0 errors, 1 existing CS9113 warning.
- EF pending-model-changes: PASS.
- Reviewed SQL versus freshly generated SQL: MATCH after line-ending normalization.
- New focused test execution: **NOT RUN** after the stop condition; no new test PASS count is claimed. The previous preflight's 28 PASS / 0 FAIL / 0 SKIP remains historical evidence.
- No production source, migration, launcher, watchdog, E2E configuration, legacy Contribution data or immutable snapshot was changed. Only this report and local backup verification artifacts were added.

## Readiness and next authorization

- Git V4 sync: not executed; still requires a new authorized official sync and new snapshot, never backfill of old snapshots.
- Peer Cohort: source/roster readiness unchanged; no real cohort, submission, close or V2 run created.
- Backend health/Auth/App access: not re-tested; backend not started.
- Smallest next step: resolve the migration-history discrepancy with a schema comparison for the eight missing migrations. If they are independent of the approved features, obtain explicit approval for a **scoped execution of only the reviewed two-migration SQL** with that drift documented. Otherwise a separate migration/deployment decision is needed. Do not apply the eight outside scope or silently alter history.

**Final status: BLOCKED at conditional migration preflight.** The E2E database remains at its original migration baseline. No commit/push occurred.
