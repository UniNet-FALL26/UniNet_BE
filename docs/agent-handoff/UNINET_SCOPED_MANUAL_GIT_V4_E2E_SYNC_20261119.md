# UNINET — Approved Scoped Manual Git V4 E2E sync

## Result

**APPROVED SINGLE SYNC COMPLETED. Backend restored to worker-off.** No snapshot, Peer Cohort/rating, Contribution Run or scoring/finalization was performed. No migration, production code, configuration file, launcher or watchdog changed.

## A. Preflight and Manual Job

- Target verified from the isolated E2E configuration: `127.0.0.1:55433/uninet_github_app_e2e`, user `uninet_test`; container `uninet-step2-concurrency`, ID `2fbe020661be4f7ae4a7fd6eac82524b6f3f33dad8ad74da7981e1ce3eb9f767`, loopback binding, running.
- Project `68f98a9f-d73d-4dcf-be5c-7099b29329c0`; association `25fe740d-dfbf-4859-8ce1-257247b4b3e7`; repository `trangtanduoc/uninet-github-app-e2e` / provider ID `1446877283`. Association Active, linked and unpaused; App grant not revoked, installation Active/not VerificationUnknown; both authorization generations 1.
- Exactly 85 applied migration IDs matched the preparation baseline; seven unrelated pending IDs unchanged. No EF database update or migration SQL executed.
- All 102 application table fingerprints matched the prior preparation's final baseline. Before enqueue: 22 jobs, all Completed; no Pending/Running/RetryScheduled jobs; 71 versions (65 V2 + 6 V3), 599 observations, 424 checkpoints, three snapshots and three runs.
- Verified enqueue-only PID 41368, process start time, executable, assembly hashes, unique listener and unique API process. Global=false, ManualEnqueue=true, Scoped=true, bound JobId=null. No other API/global worker found.
- Repeated the official read-only installation probe and repository-scoped token/access probe: **4 PASS**. Token repository list contained exactly the approved repository; repository readable, public and not archived. No reconciliation service mutation was invoked. The separate expired App user-discovery credential was not used or reset.
- Creator A logged in using the existing protected credential. JWT/password remained in memory. Sent **exactly one** authenticated POST to `/api/projects/68f98a9f-d73d-4dcf-be5c-7099b29329c0/git/repositories/25fe740d-dfbf-4859-8ce1-257247b4b3e7/sync`.
- Response **202**, `wasAlreadyQueued=false`, actual response JobId **`2d2a3ac5-9e77-4932-be3e-97c9ea15f44e`**. A joined DB query verified that exact ID, Project/Association/repository, `SyncType=Manual (3)`, credential source App and expected grant, Pending status. No generated/chosen substitute JobId or repeat POST.

### Durable lifecycle

| Field | Result |
|---|---|
| Created | 2026-11-19 08:22:20.266299 UTC |
| Started | 2026-11-19 08:23:19.258830 UTC |
| Completed | 2026-11-19 08:23:49.900394 UTC |
| Observed states | Pending → Running → Completed |
| RetryCount / retries | 0 / none; one claim/execution |
| Final ErrorCode / NextAttemptAt | null / null |
| Final LeaseId / LeaseExpiresAt | null / null |
| Checkpoints | 23, all Completed; 47 items total |

Running state was observed through PostgreSQL while branch and commit checkpoints progressed. The final state is persisted Completed, independently confirmed by the scoped consumer's `Completed; persisted state Completed` stop log. No error-level consumer log entries, forced completion, checkpoint edits or retry outside the existing policy.

## B. Evidence V4 audit

**29 new immutable versions, all V4; 47 new association/job-scoped observations.** These reference 36 distinct versions: 29 new V4 plus seven reused V2/V3 versions. Final database totals: **100 versions, 646 observations, 447 checkpoints, 23 Completed jobs**.

| Category | Schema | Observations | Distinct versions | Frozen completeness |
|---|---:|---:|---:|---|
| branches | 2 | 3 | 3 | Incomplete |
| commits | 4 | 21 | 10 | Complete |
| pull-requests | 4 | 2 | 2 | Complete |
| pull-request-commits | 4 | 5 | 5 | Complete |
| pull-request-files | 3 | 3 | 3 | Complete |
| issues | 4 | 4 | 4 | Complete |
| issue-events | 4 | 6 | 6 | Four Complete, two Incomplete |
| issue-comments | 4 | 2 | 2 | Incomplete |
| releases | 2 | 1 | 1 | Incomplete |
| pull-request-reviews / review-comments | — | 0 / 0 | 0 / 0 | Both PR scopes retrieved; zero items |

- The 36 referenced versions passed the **existing production `GitSnapshotEvidenceValidator`** against frozen facts and stable source identity: **36 PASS / 0 FAIL**. This recomputed canonical SHA-256, checked supported schema/canonicalization/algorithm, and verified provider-object/repository/category/external identity. No mutable provider payload was used in the audit. The first export helper needed a JSONB-to-string transport correction; that was an audit tooling format error, not a stored-data or production defect. Final results above are not double-counted.
- Ten unique commit identities were observed over branches `feature/e2e-sync`, `main`, `webhook-e2e-push-test` (3/9/9 items). All have parent SHA arrays, tree SHA, author and committer IDs/dates. The root has zero parents; seven commits have one parent; two have two parents and both parent trees.
- Merge SHAs `53e67b9706fbc6d257f74a04c4b7b3387ecf99ea` and `9660540cd9d13d6c5e9746530e7bd7538c09aa15` each have a tree matching a parent tree. Thus the existing V2 filter has sufficient frozen facts to classify these as **PureMergeCommit**, without guessing or assigning merge-author credit.
- PR **#1** and **#3** are frozen as merged, with merge timestamp/SHA, base/head and respectively **2 / 3** commit relations. Three PR file facts remain valid schema V3. No relationship was fabricated.
- Issues include GitHub's PR-shaped issue rows: **#1/#3 have IsPullRequest=true; #2/#4 false**. Events/comments retain RelatedIssueNumber. The existing V2 filter can resolve parent type by repository/number and exclude PR-derived activity from Issue scoring. Both review APIs returned zero for PR #1 and #3; this is current retrieved coverage, not proof of all historical/deleted reviews.
- **EP-06 duplicate rows: 0.** The extra 11 commit observations are legitimate distinct branch scopes referencing the same ten versions, not duplicate logical keys. Any future scoring must continue its own activity deduplication. No cross-project/job/repository relationship mismatch found.

### Attribution / external readiness

Read-only invocation of the existing `GitHistoricalAttributionService.Resolve`, using the project's persisted verified identity history and the proposed A+B roster, resolved **29 primary-actor/version checks: A=23, B=6; no unresolved/conflicting owner**. These are readiness checks, not activity eligibility, a frozen calculation input, or points. In particular, the checks include incomplete and PR-shaped audit entries which are not thereby eligible.

- Verified IDs remain A=`156803598`, B=`180517531`; history contains one verified owner for each and no ownership transfer/conflict. Nothing was inferred from login, backdated or reassigned.
- Commit authors are A on nine identities and B on one (a pure merge). PR authors resolve to A. Primary actors in the retrieved issue/event/comment facts resolve to A/B.
- No primary actor resolves as `OwnerOutsideRoster` in this audit, so **no eligible external contributor is established by these fetched primary actors**. GitHub automation committer ID `19864447` also appears, but existing commit attribution uses author ID, not committer identity; it must not be invented as an External receiving credit. Final eligibility/timeline/dedup remain a future run's responsibility.

### Limitations retained

- Eight distinct referenced versions / eight observations remain Incomplete: branches (3), release (1), issue-comments (2), issue-events (2). `CategoryCoverage` remains on branches/release/comments; `EventKind` is missing on events `35232873798` and `36188343998`. No completeness promotion or unsupported-event points.
- All 23 retrieval checkpoints completed, but that is distinct from frozen evidence completeness and from exhaustive historical GitHub coverage.
- Patch/content partial-overlap allocation and historical force-push reconstruction are not proved by this fetch. Existing `EvidenceCoverageIncomplete` / partial-patch policy limitations must not be removed merely because the job completed.
- A later snapshot will retain older historical observations too; old V2/V3 records were not backfilled or made V4. Its reader/scoring policy must still validate and select them appropriately.

## C. Post-sync integrity

- Only seven of 102 application tables changed: Accounts/RefreshTokens from the authorized login, and GitSyncJobs, GitSyncCheckpoints, GitHubProviderObjects, GitEvidenceVersions, ProjectEvidenceObservations from this sync. The other **95 tables are unchanged**.
- Checked **1,116 pre-existing evidence/observation/job/checkpoint rows by ID and full-row fingerprint**: none changed or disappeared. Shared provider-object rows may legitimately update through the official writer; source frozen versions did not.
- Exactly one new Manual job and its 23 checkpoints/47 observations; all 22 old jobs and 424 old checkpoints unchanged. No scheduled enqueue, unrelated claim, webhook consumption, reconciliation/activation or other association observation.
- Run #1–#3, all three snapshot headers/memberships, legacy ContributionCycle and associated data, identities/ownership histories, memberships and App installation/grant state/generations retain their original table fingerprints.
- Peer cohort/submission/snapshot counts remain zero. Run/snapshot counts remain three. Migration history identical (85 applied, seven pending); no migration tooling writes.
- Post-restore repeat fingerprints were stable. No second job, sync, snapshot, peer action or calculation was performed.

## D. Runtime restoration

- Verified PID **41368** exited gracefully. Consumer PID **30076** ran the same verified current scoped binary; the empty enqueue-only CLI override was **replaced with the actual response JobId** before launch. Global=false, Scoped=true, ManualEnqueue=true, deadline=900; unrelated workers disabled. Exact pinned job/scope/type was DB-checked before launch.
- Consumer stopped its job loop with **Completed** well before the deadline. Then PID 30076 was gracefully stopped and Backend restarted as PID **24848**.
- Final effective configuration: **GitHubSync.Enabled=false, ManualEnqueueEnabled=false, ScopedManual.Enabled=false; JobId absent from environment and explicitly empty in CLI**. No global/scoped consumer is active. Unrelated workers remain disabled; generic local-env loading remains disabled. Only one API/listener owner is present on 5239.
- `/health` **200**. Broad `/health/github/readiness` **503**, intentionally unchanged: global sync disabled and seven pending migrations. No bypass or fake migration history.
- Binary hashes match the preparation build: API `BF9E2D4E5BB43D4ABDA2F4FADEBD1DD75ECB0DB5E381AF40F2EE2CFCC1DE7658`; Application `2BF929FDB770EDD418ED21C885B93DB476AD4F2B40A55860333C9E1CE2AFA3F7`. No source rebuild was needed for this operational execution. Historical 98-test regression is not counted as new execution evidence.
- `git diff --check`: PASS; existing LF/CRLF conversion warnings only. No production source or migration modified in this task. Added this report and updated the latest handoff note. Existing dirty worktree retained; no commit/push.

## E. Next approval

**Ready to request a new Project Evidence Snapshot capture** for this project, preserving old snapshot `7b8f784b-e69f-423f-9487-0cf48c4b03d9`. Capture has NOT been performed. The evidence is suitable for immutable audit capture, not a claim that every category is complete or Contribution-eligible.

Before an ExperimentalGitScoringV2 Run: explicitly approve capture and inspect the new snapshot's immutable selected inputs/limitations and A+B roster. Real Peer Cohort/submissions/closure remain separate approved actions; no valid closed Peer Snapshot currently exists. Do not fabricate PeerScore or publish Final ContributionPercent while Peer is pending/incomplete. No Run V2 was created or scored here.

### Local receipts

`C:/Users/ADMIN/AppData/Local/UniNetE2EBackups/scoped-v4-sync-20261119-152057/` holds preflight, the single-request marker and actual response, joined approved-job verification, before/final fingerprints, historical row hashes, migration IDs, final job/checkpoints, validator/attribution audit and nonsecret execution receipt. `Consumer/` and `WorkerOff/` hold runtime metadata and logs. Current runtime pointer now selects `WorkerOff/`. Passwords/JWT/installation tokens/private keys were not printed or saved in receipts.

**Stop here. Await approval for snapshot capture and subsequent Peer/V2 work.**
