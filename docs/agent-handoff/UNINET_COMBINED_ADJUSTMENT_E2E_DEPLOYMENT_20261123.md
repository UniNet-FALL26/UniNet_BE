# UNINET — COMBINED GIT + PEER ADJUSTMENT E2E DEPLOYMENT

2026-11-23. **REAL FE WORKFLOW VERIFIED THROUGH READY DRAFT v2 — STOPPED BEFORE PUBLICATION.**

Latest live continuation below supersedes the earlier expired-session blocker and no-live-operation status, which are retained as deployment history.

## A. Exact scope / preflight
User explicitly authorized deployment of only `20261123071955_AddGitContributionAdjustments`, then real Project A FE sync/capture/new Peer round after gates. Publication remains forbidden without a further explicit confirmation. No member ratings may be chosen/submitted by the agent.

Target: `127.0.0.1:55433/uninet_github_fe_e2e_20261121`, role `uninet_test`, container `uninet-step2-concurrency`, ID `2fbe020661be4f7ae4a7fd6eac82524b6f3f33dad8ad74da7981e1ce3eb9f767`. Source `.env.github-app.e2e` parsed using existing safe launcher; CLR connection-string setter overrides Database to FE clone, then checks Host/Port/Database/User. No connection to original E2E, root .env, Neon or production.

All105 applied migration IDs compared to source Designer IDs: exactly one pending, approved adjustment migration. No active Pending/Running/Paused/RetryScheduled job. Both current Project A associations Active, unpaused, unlinkedAt-null, unrevoked generation1 grants and active verified installation:
- `38135f80-f18e-48c9-a9f4-df73fe268d92` — trangtanduoc/uninet-github-app-e2e.
- `5313bc39-8ffc-45a6-8e18-272cdc33a4a9` — trangtanduoc/mln131-group4.
Old unlinked association `889d8b27-888f-4057-849a-069ec9b50365` retained; not used as live sync target. Both Projects A/B remain Completed.

Verified old API PID15640 and exact protected binary/process/listener identity, no other API process or DB client writer. Stopped only that verified API before consistent backup/DDL. MSBuild/compiler processes were not stopped. No mixed API versions during migration.

## B. Backup / restore / rehearsal
Protected directory outside repository:
`%LOCALAPPDATA%/UniNetE2EBackups/combined-adjustment-20261123-151825/`.
ACL: current Windows principal and SYSTEM only. PostgreSQL custom-format `baseline.dump` SHA256:
`A78B3CF093EBFE83368D2772E6A087D84B073698D889D8BED456CEF8B7CFA71F`.
Local checksum matched Docker-side dump checksum. Every old table count + sorted full-row-content fingerprint captured; no secrets or rating contents printed.

Restored with pg_restore --exit-on-error --no-owner --no-privileges into separate PostgreSQL17 container `uninet-scoped-activation-release`, ID `e5702f6d5c18c2cabb0dcf98eef8cc607bd3e7e87e9b322912af6d2dd289ab27`, localhost55448, database `uninet_combined_rehearsal_20261123_1518`, rolepostgres. Before any rehearsal mutation identity verified; restored full-table fingerprints exactly matched backup baseline.

Exact reviewed [SQL](../migrations/GitContributionAdjustments.review.sql) SHA256:
`E24E505D39EBCB1D02FEA591676E45F86BEF80CE413A18429493B01E798E6AA7`.
Regenerated from current migration source, byte checksum matched. Execution copy adds only SET LOCAL lock_timeout=5s and statement_timeout=60s after START TRANSACTION. Execution checksum:
`87775A19E11B37A70E61DFD5727B4B554280CE6373B460FE035DF3F28F2A3D74`.

Rehearsal COMMIT PASS, reapply idempotent COMMIT PASS, all old business table fingerprints unchanged. Disposable rollback-only tests exercised real correlation FK scope insert, membership UPDATE rejection55000, header DELETE rejection55000, cross-project Git Snapshot rejection23514. PK/FK/unique/check/index/trigger/function catalogs captured. EF pending-model changes: none. No destructive restore over live FE database.

## C. Live scoped migration
Immediately before DDL verified listener absent, no DB clients/writers, no active jobs,105 applied, same full-table backup fingerprints. Applied exact execution copy using psql ON_ERROR_STOP=1 in one transaction. **COMMIT success**, no retry/rollback/blanket EF update/history stamping.

After: **106 applied /0 pending**, all source IDs match. New `GitContributionAdjustments` and `GitContributionAdjustmentRepositories` exist and both remain empty. Catalog exactly matches rehearsal, all FKs validated; `adjustment_guard` and `adjustment_scope_guard` enabled, function hash `53b3971f2c7864a96edc241d97f8d8a9`. Source receipts, frozen facts and old snapshots not backfilled/edited. All old business fingerprints unchanged immediately after DDL; only EF history changed.

## D. Backend rollout
Extended existing [safe launcher](../../scripts/start-github-frontend-e2e.ps1) with explicit `-AdjustmentExecution` switch. Default remains worker-off and now explicitly sets AdjustmentExecution=false; no editing of E2E config or root .env. New switch sets only master/adjustment flags needed for approved mode. Existing exact DB/history/PEM/port protections remain.

Run command:
```
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/start-github-frontend-e2e.ps1 -AdjustmentExecution
```
Launcher spawned hidden with protected stdout/stderr receipts, current source built into protected runtime artifacts. Current API **PID27672**, localhost5239, EnvironmentE2E, effective FE clone target verified. Health **HTTP200**. Runtime receipt `%LOCALAPPDATA%/UniNetGitHubFrontendE2E-runtime/runtime.json` records:
- BackgroundWorkers=true; GitHubE2E.DisableUnrelatedWorkers=true.
- GitHubSync.Enabled=false; ManualEnqueueEnabled=false.
- ScopedManual.Enabled=false, JobId empty.
- AdjustmentExecutionEnabled=true.
- Other explicit Assessor/Verification flags remain false.

Source startup registration in EnvironmentE2E registers only GitContributionAdjustmentWorker. No global GitSyncWorker, scheduling/webhook/reconciliation/activation or unrelated hosted workers. Coordinator invokes existing pinned GitSyncWorker only for persisted plan jobs; there are currently0 plans,0 plan items,0 active jobs. No automatic enqueue on startup. Bounded retry/lease/generation guards unchanged.

Safe stop: verify runtime.json/PID command line still matches the FE clone runtime and port5239 owner, then stop only that API. To restore worker-off, use the same launcher without -AdjustmentExecution after verified stop; no migration or data deletion. Do not terminate unidentified processes.

## E. Real FE verification / actual blocker
Opened actual Expo Web `http://localhost:8081/group/44b9e967-a830-4061-b33b-5d96ca41df07?tab=contribution` with integrated browser, no mocked API. Actual response401, UI showed expired session and asked to login. Browser now at `http://localhost:8081/login`. Initial React Native Web warnings: deprecated shadow/pointerEvents and useNativeDriver fallback; no new app crash observed.

Automatic approval review **rejected** the proposed shell action that would decrypt protected Creator credential and inject it into an automated browser process. Tool gave only `blocked by policy`, no more detailed reason. The rejected command did not execute; its temporary browser script was not written/launched. It was not retried. Switched to integrated shared browser session; that session is not authenticated. Asked user to log in Student A directly in browser, without posting password/JWT. No credential was printed, reset or copied to chat/clipboard/log.

Therefore actual guided panel/scoped real Git sync has **NOT been exercised** in this deployment task yet. Do not count prior mocked browser scenarios as live evidence. Unauthenticated new adjustment GET correctly returns401; route/source/build verified but Creator read success is still pending login.

## F. Live operation status
- Actual adjustment ID/RequestId: **none**.
- New Project A JobIds: **none**; no Manual POST sent.
- New observations/versions/checkpoints from this task: **none**.
- New Git Snapshot: **none**; eligible-change classification/readiness not evaluated for a new input.
- New Peer Cohort: **none**; no rating/close/snapshot/Draft action.
- New Draft v2 Run: **none**.
- ApproveAndPublish: **never invoked**.

Published v1 remains Run `00902016-dbf8-4fe6-9664-470d1bd2bc0b`, Version1, PublishedAt `2026-11-23T05:57:16.288952Z`, current A84.59/B15.41/C0. ResultHash `78c560d5a535174c06ab5f6a0c34d8d2e7c7604fdd9746423b29914e082ae82b`, InputHash `454ef88bb82855e96914a859ca84b44b1817656b110a6594657f03a0e5dd79ae`, unchanged.

Full old-table fingerprints checked after runtime/browser startup: all business records unchanged, including both projects' associations/evidence/snapshots/runs/Peer/identities/memberships. Only RefreshTokens125→127 and ChatDeviceSessions12→15 changed due to live B/C session activity; read-only attribution query found one new token each for Student B/C, not an Agent-submitted rating. No account membership, identity, scoring or historical result mutation. Session changes are not rolled back. No other drift.

## G. Tests and checks in this deployment
- Fresh protected backup/checksum/restore/full row match: PASS.
- Exact migration rehearsal, SQL reapply, real disposable guards: PASS.
- Live scoped COMMIT/catalog/history/business preservation: PASS.
- Backend safe-launcher current source build: PASS0errors (8 warnings).
- Focused Backend disposable tests: **16 PASS /0 FAIL /0 SKIP**. Existing implementation regression numbers are not recounted as new executions.
- EF pending-model changes: PASS; both diff checks PASS.
- Real FE authentication: BLOCKED expired session; no fabricated live workflow PASS.
- No new FE source changes were needed in deployment. Prior13 unrelated TS errors remain documented; not claimed fixed.

## H. Exact continuation after user login
Existing approval already permits the following Project A steps; do not re-ask migration/formula approval:
1. Verify shared browser /auth/me is Creator Student A via normal UI/session; open Project A → Đóng góp → Điều chỉnh Contribution → Cập nhật cả GitHub và Peer.
2. Verify exactly current two active A associations, no automatic mutation, original Published result. Confirm Đồng bộ GitHub once; capture actual RequestId/plan/jobs from real response/database; no GUID invention/repeated POST on ambiguous response.
3. Monitor exact two Manual JobIds under existing coordinator to durable Completed, checkpoints/observations persisted. No Project B job claim. Failures stop before capture; never force status.
4. Confirm Chốt dữ liệu GitHub mới via FE, verify exact plan link/policy2/coverage/readiness and EligibleActivitiesChanged/NoEligibleChanges/Unverified. Reconcile exact plan on timeout, no blind recapture.
5. Only Ready Git allows opening one new Peer round through FE. Verify effective roster A/B/C. Each independently authenticates and provides genuine own ratings; Agent chooses/submits none. Stop in active Peer stage if six directed ratings absent.
6. After genuine ReadyToClose, Creator can close; then create Draft using owned inputs. Preview/readback before any publication.
7. **Stop before ApproveAndPublish**; explicit further publication confirmation required. Keep v1 current until then.

**Deployment PASS. Live E2E continuation BLOCKED on authenticated Creator browser session; no live sync begun.**

## I. Live continuation after user authenticated — 2026-11-23 15:10–15:15 UTC
User confirmed login; the existing integrated browser session successfully displayed Creator controls and own current Published84.59%. No credential injection/decryption retry. Real API, no mocked responses. Exact target/runtime PID27672, health200,106 migrations and0 prior plans/active jobs revalidated. All business fingerprints still matched preflight baseline except authorized session metadata. Existing protected backup retained; no new migration or runtime restart in this continuation.

Opened Project A → Đóng góp → Điều chỉnh Contribution → Cập nhật cả GitHub và Peer. Both exact active repository names were visible, with separate authorization and old successful-sync information. Merely opening panel left plan/job counts0/26. Confirmed Đồng bộ GitHub once through actual FE confirmation.

### Actual durable correlation and synchronization
Adjustment **`42f0571a-26d7-4f18-93dd-c8139518f2de`**.
RequestId **`5a5dbb5f-f5ae-45fc-a61a-a3c76a127c56`**.
CreatedAt `2026-11-23T15:10:33.163183Z`, execution deadline `15:25:33.163183Z`.

| Repository | Association | Actual Manual Job | Final durable status | Checkpoints | New observations |
|---|---|---|---|---:|---:|
| trangtanduoc/uninet-github-app-e2e | 38135f80-f18e-48c9-a9f4-df73fe268d92 | 35eb2c97-eefa-4e0d-9cef-6a912cb59257 | Completed15:11:03.710380Z |23/23Completed|47|
| trangtanduoc/mln131-group4 | 5313bc39-8ffc-45a6-8e18-272cdc33a4a9 | 5261a59f-2a00-4bd6-8343-bf4af85f9022 | Completed15:11:11.662475Z |5/5Completed|2|

Both SyncType=Manual(3), RetryCount0, ErrorCode=null. Persisted plan/job joins match exact Project A associations/repositories. Worker processed sequentially; one Running then next, no fabricated Completed. Exactly2 new jobs, no unrelated enqueue or job mutation. No observed permission/token/rate-limit failure. No new frozen version content: all104 existing EvidenceVersions unchanged;49 new observations reuse versions correctly. No EP-06 logical duplicate group.

New observations in shared repository: branches3, commits21, pull-requests2, PR-commits5, PR-files3, issues4, issue-comments2, issue-events6, releases1. mln131-group4: branches1, commits1. Reviews0 with completed review checkpoints/verified absence; not an inferred provider failure. These are observations, not scoring counts.

### Actual capture/readiness
Confirmed Chốt dữ liệu GitHub mới once through FE, only after real job completion and checkpoint proof. New snapshot **`8b989a3c-67bd-4b1b-a27c-ace43fdeeed5`**, Project A, policy2, Finalized=true. Server observation cutoff `2026-11-23T15:11:43.186229Z`, later than both job CompletedAt values.

Persisted membership98entries,38distinctversions,2associations.56coverage receipts:23+5 for new jobs plus28retained prior completed receipts. All entries' association/project/job/repository relationships valid, cross-project mismatch count0. Existing writer versions/hash contents unchanged; actual snapshot reader/readiness succeeded with current Creator authorization (including private mln repository). New receipt membership is frozen; no cutoff-only reconstruction or historical rewrite.

Actual Backend/FE GitState **Ready**. Category readiness: CommitComplete4eligible, PRComplete2, ReviewNotApplicable0 with verified-empty coverage, IssueComplete5. Eligible change classification **NoEligibleChanges** relative to the adjustment baseline Published run. New observation count does not mean increased contribution. Readiness InputHash **`ab84233bb45c35fc037e2a0ecdafbfa5e74f1bb937b6520d93ea3f2a82a2ad64`**, read from Creator Git details UI. No new official GitScore or Final ContributionPercent asserted.

Limitations still shown: diagnostic-only, roster/history frozen with Run, partial patch overlap unsupported, historical force-push not fully proven. Timeline remains CommitterDate-range policy; no silent cutoff/policy change.

### Actual new Peer round and stop boundary
Confirmed Mở vòng đánh giá mới once through FE after GitReady. New cohort **`9eea2555-9223-4968-aba5-026950b6718b`**, statusOpen(0), exact effective roster:
- A `fef4ef4d-0049-4f7a-b629-fe9760bbbff9`.
- B `8791ff0a-38a4-44e2-97f3-3ef8fd0d2202`.
- C `ee920f44-7e77-4e2f-9f79-82c4c146d170`.

**0/6 submissions**, no prior ratings copied. Existing cohorts/ratings/Peer snapshots unchanged. Opened own A form only: B/C targets, no self target, no score chosen, both submit buttons initially disabled. Left that form ready for user. No evaluation PUT, close, new Peer Snapshot, new Draft or approval/publication call. Peer SnapshotId/RunId remain null on plan. Not ready for explicit Publish v2 yet.

Required human actions: A evaluates B/C, B evaluates A/C, C evaluates A/B independently, using genuine chosen1–5 ratings and optional comments. After6/6/ReadyToClose, Creator closes, then combined workflow creates Draft from exact owned Git/Peer inputs. Stop and show comparison before user decides publication. Existing approval does not permit Agent choosing or submitting ratings.

### Live UI defect fixed
Actual UI marked Peer step completed immediately on cohort creation while database wasOpen0/6. Backend correctly prohibited calculation; defect was display progress only. Minimal FE fix extracts `adjustmentStage`: cohort without PeerSnapshot stays step4 Đang thực hiện; only immutable PeerSnapshot advances to step5. Added two focused regressions; **38 Contribution unit PASS /0FAIL /0SKIP**, affected files ESLint clean. Hot reload verified actual DOM now shows Peer Đang thực hiện /Tính điểm mới Chưa bắt đầu /Duyệt và công bố Chưa bắt đầu. No backend/schema change or API restart for this fix.

### Preservation after real sync/capture/open
Compared live data against protected baseline restore with sorted full-row fingerprints:
- All original GitEvidenceVersions unchanged (104).
- Original jobs/observations unchanged, excluding precisely2approved new jobs and49new observations.
- Original six snapshots,2527entries and prior coverage unchanged, excluding exact new snapshot.
- All old cohorts/ratings/Peer snapshots unchanged, excluding exact new cohort.
- GitContributionRuns, OfficialContributionRuns/releases, legacy ContributionCycle/Results, identity/ownership histories and memberships unchanged.
- Project B comparison across every table with ProjectId plus Projects/jobs/checkpoints/observations: **exact match**. Shared repository mutable provider last-observed metadata can update during authorized A retrieval; B project-scoped historical data did not change.
- Published v1 remains release1/currentRun `00902016-dbf8-4fe6-9664-470d1bd2bc0b`; original result/input hashes remain as recorded above. A84.59/B15.41/C0 unchanged.
-106migrations,0activejobs; coordinator remains adjustment-only and has no eligible plan job while PeerOpen. Global/manual/legacy pinned remainoff. API PID27672 health200. No migrations, identity mutation, scoring/finalization or commit/push in continuation.

Browser had one project-hub GET aborted during normal navigation/refresh; subsequent project/contribution reads succeeded. No new runtime crash or unexpected mutation. Existing RN Web deprecation/native-driver warnings remain. Live operation used integrated browser controls, not prior Playwright mocked fixtures.

**REAL SCOPED GIT SYNC + POLICY2 CAPTURE + NEW PEER OPEN: PASS. STOPPED AT0/6 GENUINE PEER RATINGS. NOT READY TO PUBLISH.**

## J. Genuine Peer completion → Draft v2 preview (latest continuation)
User confirmed all evaluations completed. Read-only target identity and real FE session verified; cohort `9eea2555-9223-4968-aba5-026950b6718b` was ReadyToClose(2),6persisted ratings,6distinct directed pairs,0self-ratings. Agent did not choose/edit/submit any score or comment. Creator's own form displayed2/2 and overall6/6.

One live FE navigation defect reproduced: existing Published v1 early-return hid the close action while the owned combined cohort was ReadyToClose. Added focused failing regression (38PASS/1FAIL), then minimal Creator-only owned-cohort routing precedence in contributionNext. Green **39PASS/0FAIL/0SKIP**, affected ESLint clean. Real hot-reloaded FE exposed Kết thúc đánh giá; no Backend/schema/policy changes or restart. The fix does not expose Creator controls to members or select an unrelated cohort.

Clicked actual FE Kết thúc đánh giá → Xác nhận once. Cohort now Closed(3), immutable Peer Snapshot **`23e5816d-594c-4a1d-926c-677230498efe`**, InputHash **`3044fe9809f561fe31d75731679d201e58393515cd85991942e854af22e29c6a`**. Old Peer snapshots retained. Existing user evaluation revisions recorded in protected receipt without rating/comment disclosure.

Returned to exact combined attempt and confirmed Tính điểm mới once. New Official Run **`bc807db1-3bdc-4414-a621-3ce96050e913`**, stateDraft, revision0, CalculationStatusReady, policyExperimentalGitScoringV2 /GitPeerSeparate60_40V1. It references exact GitSnapshot `8b989a3c-67bd-4b1b-a27c-ace43fdeeed5` and new owned Peer Snapshot above. Plan RunId persisted atomically; only Created audit revision0 exists. InputHash **`b674679c9f4c3581e0246a03597d0dddead2bcf863548c33cfadd2f6c72ccc18`**; ResultHash **`bdef53b0346a0ac5f80b8bd0dddbe9fb9c200a1da5566f030fb37805e14e8c2e`**.

### Creator preview (official Backend values, not FE scoring)
| Member | GitShare | New PeerShare | Current v1 | Draft v2 | Change in percentage points |
|---|---:|---:|---:|---:|---:|
| A |92.50|70.00|84.59|83.50|-1.09|
| B |7.50|30.00|15.41|16.50|+1.09|
| C |0.00|0.00|0.00|0.00|0.00|

Frozen new Peer scores A87.5/B37.5/C0 normalize separately to70/30/0, then existing60%Git+40%Peer yields83.50/16.50/0. External0; total100.00. C's zero is an actual complete frozen calculation result, not a substitute for missing Peer input. No raw individual ratings/comments exposed in this handoff. Git eligible activities unchanged; score difference comes from genuine new Peer inputs. No scoring policy/weights/timeline changed.

Real Creator result dialog now shows Draft, current-v1 comparison for each member, category readiness, activity/exclusion/audit buttons, calculation policies/input+result hashes/revision. **Left preview open; did not click Duyệt và công bố.** Category Commit/PR/IssueComplete, ReviewNotApplicable with verified-empty coverage. Previously documented partial-overlap/force-push limitations remain applicable.

### Safety / final state
- Current publication still exactly one release, Version1, Run `00902016-dbf8-4fe6-9664-470d1bd2bc0b`. Original v1 ResultHash/InputHash unchanged as above; original DB Finalized state/revision3 unchanged.
- Project B full scoped fingerprints against baseline restore still match.
- No extra Git sync/capture/migration, no identity/membership or unrelated rating mutation.
- No approve/finalize/publication endpoint invoked for new Draft; audit contains only Created0, no release for newRun.
- Backend remains existing approved adjustment-only runtime; no global worker enabled. FE fix requires no Backend restart.
- Read-only verification initially queried singular audit table name and got42P01; corrected to actual OfficialContributionAudits. Read-only transaction aborted, no data mutation; final audit/pair/release checks passed.

**READY FOR CREATOR'S EXPLICIT PUBLICATION DECISION. v2 IS STILL DRAFT; v1 REMAINS CURRENT.** User may personally inspect and choose Duyệt và công bố. Agent must not publish unless separately explicitly authorized.
