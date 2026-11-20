# UNINET — scoped 3-migration Git V2 E2E deployment

## Result

**SCOPED DEPLOYMENT PASS.** Applied exactly the three approved migrations to `127.0.0.1:55433/uninet_github_app_e2e`, container `uninet-step2-concurrency`. No blanket EF update, old-migration rewrite, fake history stamping, GitHub sync, real peer cohort/rating, V2 Run, finalization or commit/push.

## A. Fresh disposable rehearsal

Used a new restore, not the previous disposable that already contained V2:

- Container `uninet-v2-three-20261118`, ID `514da1ebaaad11ec102be0e58ed03fade5b9391d5a7c91a25ce72f40a89a45fa`.
- Host `127.0.0.1`, port `49293`, database `uninet_v2_three_disposable`, user `uninet_test`.
- Restored the verified baseline dump using `pg_restore --exit-on-error --single-transaction`. All 96 existing application-table fingerprints matched the backup; history started at 82 rows.
- Generated [Specializations scoped SQL](../migrations/AddProjectSpecializations.scoped.review.sql) from the exact predecessor-to-target EF range. Reviewed all DDL: nullable Projects.SpecializationId, Specializations and AssessorSpecializations tables, PK/composite PK, restrictive specialization FKs, assessor-user cascade FK, three indexes and three static catalog seeds. No existing Project specialization assignment or lifecycle update.
- Executed Specializations first, then the unchanged [two-migration V2 review SQL](../migrations/GitPolicyV2PeerCohorts.review.sql), with lock_timeout=5s and statement_timeout=60s. Only actual migration SQL recorded its three IDs.
- Ran the same scripts again: idempotency PASS; history remained 85, specialization catalog remained three rows, no unexpected data changes.

### PostgreSQL integration checks: 13 PASS / 0 FAIL / 0 SKIP

These are standalone current-source EF/PostgreSQL checks, separate from xUnit counts:

1. Actual Project EF read succeeds; Completed status and Creator unchanged.
2. Specialization AutoInclude joins successfully; existing Project remains null; three seeds present.
3. GitContributionRuns EF read succeeds; three old runs and Run #3 input hash retained.
4. Peer service A authorization reaches expected PEER_COHORT_NOT_FOUND, not a schema/auth error.
5. Same Peer service check for B.
6. Exactly three approved history additions and seven old migrations Pending.
7. Original snapshot still has 599 persisted entries.
8. Legacy cycle/participant/evaluation counts unchanged.
9. Peer roster mutation rejected by PostgreSQL, SQLSTATE 55000.
10. Peer cohort deletion rejected, SQLSTATE 55000.
11. Snapshot insertion on an unready cohort rejected, SQLSTATE 23514.
12. Existing EvidenceVersion update rejected, SQLSTATE 55000.
13. Test transaction rolled back: no cohort/snapshot/submission or V2 Run committed.

The guard checks used one temporary cohort row inside a rolled-back **disposable-only** transaction; no ratings were inserted, no fake Peer Snapshot committed, and no corresponding fixture was written to E2E. An initial scratch-check compilation error referenced a nonexistent named DbSet; it was corrected to the actual typed Set before checks ran. Production implementation was unchanged.

All new tables/columns, FK/index/check definitions and enabled triggers were inspected in PostgreSQL. No unvalidated constraints were found. Existing historical fingerprints remained equal after checks/replay. Evidence/snapshot/run protections were not disabled.

## B. E2E deployment

Before writes, reverified Docker identity/binding, parsed E2E connection, backup SHA-256 and restore evidence, all 82 history IDs, the full original schema catalog and 96 table fingerprints. No Backend listener, other database clients, or Pending/Running/RetryScheduled Git jobs were present.

The following IDs were applied in order, with bounded lock/statement timeouts and ON_ERROR_STOP:

1. `20261109090826_AddProjectSpecializations`
2. `20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2`
3. `20261118152617_GuardGitV2FinalNormalizationReadiness`

Each migration committed successfully. E2E history is **85 rows**. Never ran `dotnet ef database update` or Down. Never inserted a history row for an unexecuted migration.

### Post-deployment verification

- EF Project and GitContributionRuns reads: PASS.
- Full E2E catalog matches the rehearsal, apart from database identity and an unrelated pre-existing UploadedFiles check whose pg_restore cast formatting differs. That check's E2E definition was verified byte-for-byte unchanged from the pre-deployment catalog; no schema workaround was applied.
- Specialization seeds=3; AssessorSpecializations=0; all new peer tables=0.
- All 96 old application tables have unchanged row fingerprints immediately after migration, excluding only the new nullable Project/run columns. No old Project has been assigned a specialization by backfill.
- Runs #1–#3 remain Draft/V1 with identical inputs/results; Run #3 input hash remains `64d0bc18a6cb388f9fba937101b86a71372254dbd8c4cb584f18606b0365b560`.
- Evidence snapshots=3, Git runs=3, Git jobs=22 Completed, active jobs=0. Old evidence versions/observations, roster, membership, identity history, Task/Report/Verification data and legacy cycle remain unchanged.
- EF pending-model-changes: PASS. This does not claim full database parity: unrelated assessor/payment structures remain missing.

Exactly seven old migrations remain Pending:

1. `20261106081602_ConsolidateVerificationPayments`
2. `20261106141923_AddAssessorOnboarding`
3. `20261107051900_AddAssessorContractSigning`
4. `20261108023501_AddAssessorPasswordTokenIndex`
5. `20261108142140_ArchiveAndRemoveLevel1`
6. `20261109013212_ArchiveAndRemoveVerificationEvidence`
7. `20261114014337_ArchiveAndRemoveExternalProjectFiles`

Do not run blanket migration tooling in a subsequent task. No approval to apply these seven is implied.

## C. Backend / authentication

Started the current isolated build using the actual E2E configuration in a child environment, without editing the launcher, watchdog, migration source or E2E env file:

- URL `http://localhost:5239`; PID `28292` at report time.
- ASPNETCORE_ENVIRONMENT=E2E; UNINET_SKIP_LOCAL_ENV_FILE=true.
- Child overrides: GitHubSync__Enabled=false, GitHubE2E__DisableUnrelatedWorkers=true, VerificationWorker__Enabled=false, AssessorOnboarding__Enabled=false.
- The Assessor hosted shell is registered by current source, but task methods return immediately under Enabled=false and do not query missing assessor tables. Existing OAuth security housekeeping remains registered; pending cleanup count is zero and no OAuth-flow changes were observed. It performs no GitHub sync.
- Runtime record/log location: `C:/Users/ADMIN/AppData/Local/UniNetE2EBackups/git-v2-runtime-20261119-000418/`. No secrets/JWT were written to the report or printed.

### HTTP checks

- `/health`: **200**, status=ok.
- A login and `/api/auth/me`: **200**.
- B login using **new** protected credential `UniNetGitHubAppStep6E2E-StudentB/student-b.dpapi`, and `/api/auth/me`: **200**. No password reset or old B credential used.
- Project detail and repository access-state GET: **200 for A and B**.
- Peer lookup with a nonexistent cohort: **404 for A and B**, expected authorized absence; no cohort created. This is not presented as a successful read of real peer ratings.
- Existing Run #3 GET: A **200**, B **403**, preserving leader-only Draft detail access.
- Unauthenticated Project read: **401**.
- Thirteen login/read/authorization assertions PASS; health is recorded separately.

Only Accounts and RefreshTokens changed after the two authorized logins (normal login timestamps/session issuance). The other 94 pre-existing table fingerprints, including all Contribution/Git/project business records, remained unchanged. There is no claim that authentication is a read-only operation.

**`/health/github/readiness` returns 503**, as expected with GitHubSync disabled. Its current implementation also requires no Pending EF migrations; enabling sync alone would not make that broad readiness check green while these seven remain Pending. This is an operational limitation of the intentionally scoped E2E deployment, not permission to stamp/apply them or weaken readiness guards. `/health` and the tested Git/Peer APIs pass. Logged exceptions correspond to intentional 404/403 probes and this unhealthy readiness check, not unexpected SQL failures.

### Build / focused regression

Current-source isolated build through the test project: PASS. **28 xUnit PASS / 0 FAIL / 0 SKIP** (not double-counted): normalizer 5, V2 policy 2, experimental service 16, allocator 5. These used unit/SQLite fixtures, not E2E ratings. The 13 disposable PostgreSQL checks and 13 HTTP assertions above are separate verification groups. Existing compiler/line-ending warnings were observed; diff check passed.

## D. Next steps / approval boundaries

### Git Evidence V4

- Local association Active, unpaused and linked; App grant not revoked, installation Active, VerificationUnknown=false; grant/installation generations both 1.
- A/B verified numeric identities match their active OAuth connections; each actor has one distinct historical owner in this project. Existing historical-attribution/conflict rules unchanged.
- Repository access-state API succeeds for both accounts. No live GitHub token/repository probe or sync was made, so instantaneous external access is not claimed.
- Current stored evidence is still 65 V2 + 6 V3 versions, no V4. Old frozen snapshots lack parent SHAs/merge classification and some PR/Issue-boundary facts. Source pipeline supports the new contract; readiness requires a new authorized official sync and a new capture, never rewriting old snapshots.
- External classification still requires verified evidence; no actor was reclassified or new score produced here. Missing coverage/overlap can still keep a future Run Incomplete after sync.
- Next user approval needed: enable the scoped Git sync execution and request the official new sync for the existing association, with the broad readiness-health limitation understood. Do not enable normal launcher defaults or workers automatically: the worker-off settings here are child-process overrides, while the existing env file still has its original worker setting.

### Peer Cohort

- A+B is an eligible experimental roster; A remains Creator and is included by effective membership. Source and focused regression preserve Creator-only close, no self-review, 1–5 range, pair/revision uniqueness, member privacy and immutable closed snapshots.
- No real cohort, rating, close, V2 calculation or finalization was performed. Next approval is to open the A+B cohort; the two users must personally submit real evaluations before Creator closes it.
- Legacy cycle remains A-only; alternate roster A+B still cannot pass official review/finalize protection. No policy/cycle mutation is implied by this deployment.

## Files / retained evidence

Added [Specializations review SQL](../migrations/AddProjectSpecializations.scoped.review.sql), this report, and an allowed disposable database name in the existing [read-only audit script](../migrations/GitV2MigrationReconciliation.audit.sql). No production source or old migration was modified. Existing unrelated tracked/untracked work was preserved.

Backup directory additionally contains `three-deployment-receipt.json`, PostgreSQL check source/receipt, rehearsal and E2E catalogs, before/after fingerprints, complete EF history output and final state summary. No credentials are stored in these receipts. The tested disposable is retained for inspection.

**Final: scoped three-migration deployment and requested Backend/Auth preparation completed. Wait for explicit Git sync and real Peer Cohort instructions.**
