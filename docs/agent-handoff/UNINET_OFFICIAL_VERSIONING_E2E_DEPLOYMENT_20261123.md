# UNINET — SAFE OFFICIAL CONTRIBUTION VERSIONING E2E DEPLOYMENT

2026-11-23 — **SCOPED MIGRATION APPLIED — VERIFIED**. Stops before new Peer cohort, Draft v2 or publication v2.

## A. Preflight / target

- Target only `127.0.0.1:55433/uninet_github_fe_e2e_20261121`, role `uninet_test`.
- Docker `uninet-step2-concurrency`, ID `2fbe020661be4f7ae4a7fd6eac82524b6f3f33dad8ad74da7981e1ce3eb9f767`; mapping exactly127.0.0.1:55433 ->5432.
- E2E configuration read from `.env.github-app.e2e`; safe launcher's CLR connection-string setter explicitly overrides Database and checks host/port/database/user. Root `.env`, Neon, production and original E2E were not connected. Source E2E config is parsed as launcher input, not used to connect to original DB.
- Full applied-ID list compared to all source Designer IDs: **104 applied / exactly1 pending**, the approved migration. No unrelated migration was pending or executed. No blanket EF database update command.
- Existing API PID19808 / binary under protected `UniNetGitHubFrontendE2E-runtime/build` verified by process command line and listener ownership. Workers off, no active sync job; no other active DB client/writer at stop gate. API stopped before backup and retained off throughout migration; no mixed-version Contribution traffic.
- Current A Run `00902016-dbf8-4fe6-9664-470d1bd2bc0b` already Finalized revision3, first publication `2026-11-23T05:57:16.288952Z`. No Run transition performed.

## B. Backup and exact restore rehearsal

Protected receipt/backup directory:
`%LOCALAPPDATA%/UniNetE2EBackups/official-versions-20261123-134848/`.
ACL limited to current Windows principal/SYSTEM. No secret/token/private rating included in this report.

Custom-format `baseline.dump`, SHA256:
`79CA66910EEE6899E86028A5B34DB31D4B42605EDF3E98A5178DCB533C58899B`.
Checksum matched Docker-side dump before restore. Consistent pg_dump after writer stop; no reset/drop/restore over FE.

Full restore with `--exit-on-error --no-owner --no-privileges` into dedicated PostgreSQL17 container `uninet-scoped-activation-release`, ID `e5702f6d5c18c2cabb0dcf98eef8cc607bd3e7e87e9b322912af6d2dd289ab27`, localhost55448, database `uninet_versions_rehearsal_20261123_1348`, rolepostgres. Verified identity before DDL. **Every existing table's count+full-row fingerprint matched live backup baseline**.

Applied exact reviewed scoped migration on restore. COMMIT PASS; all old business tables unchanged, only EF history changed104->105. Import mismatch count0. UPDATE and DELETE of imported A release each rejected with **55000** inside a disposable rolled-back guard test. Idempotent SQL reapply COMMIT PASS. Source EF pending-model-changes PASS. Rehearsal container stopped afterward; backup and receipt artifacts retained.

## C. Live migration

Applied only **20261123061945_AddOfficialContributionPublicationVersions** using reviewed SQL:
[OfficialContributionPublicationVersions.review.sql](../migrations/OfficialContributionPublicationVersions.review.sql).
Source regenerated from migration and matched reviewed SHA256:
`EE42FD9808F68E2B43AD0C03664CB83AFE49E4C7249BCDDEB6931610F618F2F6`.

Execution copy adds only SET LOCAL lock_timeout=5s and statement_timeout=60s immediately after START TRANSACTION; ON_ERROR_STOP=1. Execution-copy SHA256:
`E6940934AD9F53F5F8EA0CC71B593DD2469494F826076C3E0DE6756FDD59494E`.
Before DDL: listener absent, live fingerprints rechecked against backup, DB/user/history104 and release-table absence checked. SQL executed **one transaction / COMMIT success**; no error/rollback/retry, no separate history stamping.

After: **105 applied / 0 pending**, exact new migration is sole history delta.

## D. Schema and import

- `OfficialContributionReleases` exists.
- PK RunId, positive Version CHECK, unique ProjectId+Version, publisher and previous/current Run+Project composite FKs all validated. Index catalog matches rehearsal.
- Original unique Finalized/project index replaced with nonunique index; enables multiple historical Finalized runs. Existing values are not edited; existing finalized/calculation immutability triggers retain protection. Publication concurrency is now guarded by project locking and immutable linear ledger.
- `uninet_official_release_guard` and enabled `official_release_integrity` BEFORE INSERT/UPDATE/DELETE trigger exist. Actual UPDATE/DELETE guard execution tested on disposable only; live verification was catalog/read-only.
- Exactly1 imported release: Project A, Version1, Run `00902016-dbf8-4fe6-9664-470d1bd2bc0b`, PreviousRunId=null, PublishedById `fef4ef4d-0049-4f7a-b629-fe9760bbbff9`, PublishedAt `2026-11-23T05:57:16.288952Z`. Publisher/time match original receipt exactly.
- Original OfficialContributionPublications receipt unchanged. No new business publication or approval action performed; imported ledger metadata represents existing publication.
- Current selection still original Run; no replacement Draft created.

Hashes unchanged, verified full-row fingerprints and API readback:
- ResultHash `78c560d5a535174c06ab5f6a0c34d8d2e7c7604fdd9746423b29914e082ae82b`.
- InputHash `454ef88bb82855e96914a859ca84b44b1817656b110a6594657f03a0e5dd79ae`.

## E. Data preservation

Immediately after migration, **all pre-existing business table fingerprints matched**. Only old EF history fingerprint changed. Therefore Project B associations/jobs/evidence/observations/snapshots/runs/results/audit/Peer data, A's existing records, memberships/identity history and legacy Contribution are unchanged by DDL/import.

After API login/read verification, only Accounts, RefreshTokens and ChatDeviceSessions changed, from the explicitly authorized real A/B Auth API sessions. They are session/account metadata, not new ratings or recalculation. All other old tables still match the post-migration baseline. Authenticated GETs did not write business records. No sync/snapshot/cohort/rating/Run/publication v2/finalize operation, reset/reseed or migration outside scope.

## F. API rollout and read-only verification

Started latest source with existing [safe launcher](../../scripts/start-github-frontend-e2e.ps1), isolated artifacts, E2E environment and source migration allowlist. Build succeeded,0errors; no auto migration in launcher.

- PID **15640**, API `http://localhost:5239/api`, health **200**.
- Effective FE connection target verified by launcher and independently by matching API-issued A/B session hashes in FE database (no credential/JWT printed/stored in receipts).
- Flags: BackgroundWorkers=false; GitHubE2E.DisableUnrelatedWorkers=true; global Git sync=false; ManualEnqueue=false; ScopedManual=false; JobId empty; Assessor/Verification workers disabled; UNINET_SKIP_LOCAL_ENV_FILE=true.
- Only new API runtime handles Contribution; old API stopped before DDL. Compiler/MSBuild helper processes are not API writers.

Official GET results with Creator A/member B:
- `/workflow`: PublishedRunId=originalRun; no replacement latest Run.
- `/version-capabilities`: supported=true, canAdjust=true, canOpenPeerRound=true, currentPublishedRunId=originalRun.
- `/versions`: total1, Version1 current, points at old Run. Member B GET versions correctly403.
- `/runs/{originalRun}`: Published response, unchanged revision/hash/input.
- `/results/me`: correct own UserId/currentRun for independent A/B sessions; values match stored frozen normalized result.

| Member | GitShare | PeerShare (unrounded display here) | Current ContributionPercent |
|---|---:|---:|---:|
| A | 92.50 | 72.7272727272727 | 84.59 |
| B | 7.50 | 27.2727272727273 | 15.41 |

Existing frozen C result is0; C login/result endpoint was not exercised in this rollout. No rating values/comments disclosed.

No approve-and-publish call. Read verification script invokes only business GETs, plus authorized Auth login/me. No test suite counts reused as new deployment evidence. New deployment checks: backup restore PASS; all-row preservation PASS; exact migration rehearsal PASS; immutable guard tests PASS; idempotent reapply PASS; live commit/schema/import PASS; build/model consistency PASS; authenticated read/security checks PASS.

## G. FE and next step

FE source already consumes version-capabilities. With this new API/schema, **Điều chỉnh Contribution** and Creator publication history can be enabled by actual server capability; refresh/reopen Đóng góp to obtain new state. Browser button click was not performed against live data. No FE restart is required for the running Expo development source; production bundle rollout is separate.

Backend is ready to let Creator **begin a new Peer round**: no unclosed cohort and canOpenPeerRound=true. This is capability readiness, not an automatically opened round. Actual roster is revalidated from effective members, including Creator and C even without Git identity. New round must have genuine new ratings; old Peer Snapshot stays immutable. Git snapshot reuse remains subject to integrity/current authorization/roster compatibility and existing scoring limitations. A Draft v2 requires both selected frozen inputs and Ready calculation. No new blocker was found at rollout checks; future authorization or roster changes can correctly reject a subsequent operation.

Next user action, when intended: Project A -> Đóng góp -> Điều chỉnh Contribution -> Mở vòng đánh giá mới; inspect roster, collect each user's own ratings, close, create Draft, preview. **This task stops before all of those mutations.**

Rollback after imported history is intentionally guarded; no automatic Down/destructive restore. Preserve backup and evaluate any future recovery request explicitly. No commit/push; existing uncommitted source changes retained.
