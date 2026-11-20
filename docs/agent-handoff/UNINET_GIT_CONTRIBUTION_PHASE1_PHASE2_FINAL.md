# UNINET — FINAL PHASE 1 & 2 COMPLETION

## Superseding authorized E2E completion — 2026-11-20

**READY FOR PHASE3** for the experimental E2E flow. User subsequently explicitly authorized automated ratings A→B4/B→A5, commentnull, under their two controlled test accounts. Official independent sessions submitted once, cohort closed to PeerSnapshot `c6fe092b-ea1f-4e19-b04e-384a7663424b`; official V2 Draft Run#4 `ad2cf570-9592-465a-bdb0-c80c5112cdef` is Ready, final percentages A78.36/B21.64/External0=100.00%. No review/finalize/publication. See [full follow-up report](UNINET_AUTOMATED_PEER_EVALUATION_CONTRIBUTION_V2_20261120.md). The blocked/0-of-2 state below is the earlier historical read point; no longer the current E2E state. Do not describe these authorized automated test ratings as independent production-user evaluations.

Date: 2026-11-20. **BLOCKED BEFORE PHASE 3 — Git Ready; real Peer submissions pending.**

Exactly one approved Manual V5 sync and one conditionally approved policy-2 capture completed. Git calculations below are verified diagnostics, **not a Contribution Run or final allocation**. No member rating, cohort closure, Peer Snapshot, Run V2, review, finalize or publication was performed.

## A. Actual E2E operations

Target reverified: `127.0.0.1:55433/uninet_github_app_e2e`, role `uninet_test`, container `uninet-step2-concurrency` / `2fbe020661be4f7ae4a7fd6eac82524b6f3f33dad8ad74da7981e1ce3eb9f767`. Project `68f98a9f-d73d-4dcf-be5c-7099b29329c0`; association `25fe740d-dfbf-4859-8ce1-257247b4b3e7`; repository `trangtanduoc/uninet-github-app-e2e`. Initial data/catalog/history matched the previous deployment. Project Completed, A Creator/B active member, linked/unpaused Active association and unchanged verified identity histories.

Official live installation probe, repository-scoped installation token and repository read access passed. Installation Active, correct App/owner/repository, token scope exactly one requested repository; repository public/not archived. Token/private key were not printed or persisted. No GitHub activity was created.

### Manual sync

| Field | Actual result |
|---|---|
| Authenticated request | One Creator A POST to existing association `/sync` endpoint, HTTP **202** |
| Actual JobId | **`5007f7e3-23a5-423c-8d97-d2e43364f960`** |
| wasAlreadyQueued | false |
| Scope/type validation | Exact project/association/repository; Manual/Pending checked before consumer startup |
| Durable lifecycle | Pending → Running → **Completed** |
| Started / completed UTC | `2026-11-19T18:05:22.464993Z` / `2026-11-19T18:05:52.152082Z` |
| RetryCount / Error / NextAttempt | **0 / null / null** |
| Lease at completion | Released/null |
| Checkpoints / processed items | **23/23 Completed**, 47 items |
| New versions | **2**, both Complete issue-comment **V5** |
| New observations | **47**, 36 distinct referenced versions; all existing version bytes retained |

Enqueue-only PID29064 had no JobId and no claims. Consumer PID18444 was pinned to the actual response JobId in both environment and CLI; Global=false, unrelated workers disabled. Existing provider, fencing/heartbeat, atomic writer, page transactions and checkpoint/retry pipeline were used. Consumer recorded `Completed; persisted state Completed`. No fallback/global claim, scheduled enqueue, webhook consumption or reconciliation occurred.

### Frozen evidence and snapshot

- All **36 observed versions** passed the existing production validator's supported-contract, stable identity, canonical-byte and SHA-256 checks. No mutable payload was used for scoring.
- 21 commit observations over all three retrieved branches reference ten commit identities. Parent/tree facts and both pure merges remain available. PR #1/#3 have complete current-job commit relationships (2/3 respectively); relation sets were not unioned with stale jobs.
- Both fetched issue-comments now use V5 with actual numeric actor, creation time and positive parent scope. No body/login was added to frozen facts. Real-issue comment `6656546011` is eligible; the PR-shaped parent comment remains excluded from Issue scoring.
- The two null EventKind entries (`35232873798`, `36188343998`) were inspected: GitHub actually supplies **`merged`**. This is outside the Issue-event allowlist and both have PR parents. Their EventKind remains missing/Incomplete; PR-parent exclusion prevents Issue credit. This is not a missing provider response or a reason to fabricate an Issue event. PR merge facts are independently preserved in complete PR evidence.
- Review scopes for PR #1 and #3 both completed with zero items; no fake Review activity was inserted.

| Snapshot field | Persisted/read result |
|---|---|
| New SnapshotId | **`d56f2e2c-5b40-4508-8746-943ee15ae256`** |
| Policy / state | **2 / Finalized** |
| Persisted cutoff UTC | `2026-11-19T18:09:15.805048Z` |
| Membership | **693 observations / 102 distinct versions**, one association, no restricted entries |
| Observation schemas | V2=568; V3=45; V4=78; V5=2 |
| Reader statuses | **468 Valid / 225 Incomplete / 0 Unsupported / 0 Corrupt** |
| Frozen coverage | **470 actual completed job/checkpoint receipts**; latest enumeration/review receipts belong to the new Manual job |
| Review proof | **VerifiedEmptyReviewCoverage** |
| Membership/coverage hash | `a6c4646f9c9e3e14fa89107a149153790f1f5459551351e2c382b368cd208999` |
| Frozen diagnostic input hash | `0d3dd2aab3334923653fd68c0715b840d8705e608956bcbd15a9a8f70615418b` |

Creator authenticated via real login and `/auth/me`. There is no public capture endpoint; the bounded one-attempt tool invoked the existing internal CaptureAsync with the verified account, then used the official authorized reader. No direct SQL snapshot insertion. Same Repeatable Read capture froze exact membership and actual receipts atomically. Reopening the same SnapshotId and repeating the diagnostic reproduced both hashes. The snapshot entity has no hash column; these hashes are retained in the protected diagnostic artifact, not written back to a snapshot. Old policy-1 snapshots and their Incomplete facts were not promoted or backfilled.

## B. Git scoring — Ready for the existing experimental V2 contract

| Member | Commit | PR | Review | Issue | Raw GitScore | GitShare | PeerShare | FinalContributionPercent |
|---|---:|---:|---:|---:|---:|---:|---|---|
| Student A | 100 | 100 | 0 | 60 | **74.00** | **92.50%** | Pending | **Unavailable** |
| Student B | 0 | 0 | 0 | 40 | **6.00** | **7.50%** | Pending | **Unavailable** |

Independent recomputation from the ten scored frozen inputs agrees with the production diagnostic:

- A: `.35×100 + .30×100 + .20×0 + .15×60 = 74`.
- B: `.35×0 + .30×0 + .20×0 + .15×40 = 6`.
- Git mass=80; shares A=`74/80×100=92.50`, B=`6/80×100=7.50`; eligible External=0. **GitShare sum=100.00%**. This is not the final 60/40 allocation.
- Empty Review has zero category points and verified NotApplicable; its 20% category weight is not redistributed. Top-level Git/Peer weight transfer is separate and does not apply to Pending Peer.

Categories: Commit **3 eligible**, PR **2 eligible**, Issue **5 eligible**, all `EligibleForExperimentalMvp`; Review **NotApplicable** with frozen proof. GitState and GitShare status **Ready**. No Corrupt/Unsupported/Incomplete selected scoring input was awarded points.

### Ten scored activities

| Activity | Owner | Frozen evidence/rule |
|---|---|---|
| Commit `5b4d9083790247a18fc3996cc47676053515c90b` | A | Valid standalone/root commit |
| Commit `d72270306add827a239b9bf335a222b3ec2954aa` | A | Valid standalone commit |
| Commit `7f0aa3fa27fc007a88afc32bebfe87141d2f30cb` | A | Valid standalone commit |
| PR #1 `5170525942` | A | Merged; verified author with substantive exact author-commit evidence |
| PR #3 `5296699632` | A | Same rule; distinct final observed commit set |
| Issue #2 `6222025831` creation | A | Real Issue, not PR-shaped |
| Issue #4 `6372817014` creation | A | Real Issue |
| Issue #2 `35233105321` closed | B | Supported event, actor/time and real parent |
| Issue #4 `36188358372` closed | A | Supported event, actor/time and real parent |
| Issue #2 comment `6656546011` | B | New Complete V5; verified actor, creation timestamp, parent and timeline |

Each commit contributes 1/3 of Commit points; each PR 1/2 of PR points; each Issue activity 1/5 of Issue points. Category-weighted raw units, exact observation/version/hash/actor/time references are in [scored activity CSV](UNINET_PHASE12_FINAL_SCORED.csv). All scored event times lie within the frozen eligible normal-commit CommitterDate window, **2026-11-05T10:52:24Z–2026-11-16T15:49:36Z**. AuthorDate/ObservedAt were not used as substitutes.

### All 693 observation dispositions

| Reason | Count | Impact |
|---|---:|---|
| DuplicateObservation | 559 | Re-observations/branch scopes/history collapsed for scoring, retained in snapshot audit |
| AuditOnlyNonScoringCategory | 105 | Branch/release/file metadata retained, no invented points |
| Scored | 10 | The activities listed above |
| PullRequestRelationshipEvidence | 5 | Supporting SHA relationships; no separate activity points |
| AlreadyRepresentedByPullRequest | 5 | No standalone Commit credit for work represented by eligible merged PRs |
| PullRequestActivityExcludedFromIssue | 5 | PR-derived comments/events do not earn Issue credit |
| IssueIsPullRequest | 2 | PR-shaped Issue rows excluded |
| PureMergeCommit | 2 | Both result trees match a parent; no standalone merge credit |

[Full observation CSV](UNINET_PHASE12_FINAL_OBSERVATIONS.csv) provides every observation/job/version/association/hash, actual frozen-history resolver result, numeric actor, timestamp, timeline check, completeness/missing facts, dedup/exclusion reason and scoring unit. No private payload/body is included. Evidence selected for scoring passed integrity checks; 225 older/current Incomplete audit entries remain Incomplete and receive no inferred eligibility.

Historical attribution uses verified numeric IDs A=`156803598`, B=`180517531`, project-scoped frozen history and A+B roster. One verified owner per ID, no ownership conflicts or double attribution. New-job primary-actor checks: 29, A=23/B=6, including non-scoring audit activities. Earlier eligible activity may predate LinkedAt under the approved historical policy; no OAuth/link timestamps were backdated. The automation committer is not invented as an External author.

### Remaining retrieval/policy limits

Current fixture has no exact overlap between the two PR commit sets; five PR-represented commits are strictly deduplicated. Current-job relation selection prevents stale force-pushed SHA union. Existing pure-merge/PR-Issue/timeline rules are applied unchanged.

Hybrid patch-similarity thresholds/allocation and general post-merge force-push policy are still not quantitatively specified. Arbitrary partial-overlap splitting or historical force-push reconstruction was **not implemented or claimed**. Actual partially overlapping PR commit sets remain unsupported/excluded by the experimental policy. `PartialPatchOverlapNotSupportedInExperimentalV1` and `HistoricalForcePushNotProven` remain visible limitations. They are bounded limitations of this experimental Ready result, not evidence that a full official general-purpose overlap/force-push algorithm is complete. Future cases that require those decisions must stop for policy/evidence rather than receive guessed points.

## C. Real Peer Evaluation — BLOCKED on member action

- Existing cohort **`5f798512-6bce-4c66-b58d-c3d5b859fae7`** retained; frozen roster A Creator + B member unchanged.
- Final authenticated progress reads: **Open / CurrentCount=0 / RequiredCount=2**, revisions=0, no PeerSnapshotId. Both missing evaluators: A (`fef4ef4d-0049-4f7a-b629-fe9760bbbff9`) and B (`8791ff0a-38a4-44e2-97f3-3ef8fd0d2202`).
- A and B login/progress APIs passed. B used the newer `UniNetGitHubAppStep6E2E-StudentB/student-b.dpapi`; no password reset. B sees only permitted peer progress/outgoing data; Run #3 Draft access **403**, Creator A **200**. No rating/comment was printed.
- No submit, close or Peer Snapshot API was called. No missing PeerScore was converted to zero. Real members must independently submit A→B and B→A using [the existing guide](UNINET_PHASE2_PEER_SUBMISSION_GUIDE.md).
- Once genuine submissions satisfy ReadyToClose, unchanged roster/revisions/authorization can be checked and the conditionally approved Creator close executed through the official API. Zero peer mass must remain InsufficientData. No additional rating or cohort should be fabricated/replaced to reach the desired result.

## D. Contribution V2

**No Run V2 created.** Git is Ready, but Peer is Pending, not Complete/NotApplicable. There is no closed immutable Peer Snapshot and no valid PeerShare; hence no final allocation or 100.00% final-recipient assertion can be made. The already implemented `GitPeerSeparate60_40V1` formula and deterministic largest remainder were not changed.

After real Peer readiness, the existing conditionally authorized flow may create one idempotent Experimental V2 draft referencing the new Git snapshot, closed Peer Snapshot and A+B roster. Verify frozen input/result hashes and `0.60×GitShare + 0.40×PeerShare`, including any legitimate External allocation. Do not review/finalize/publish. The existing alternate-roster official-finalization guard remains unchanged; the immutable legacy cycle must not be edited to bypass it.

## E. Tests and implementation

**247 unique latest PASS / 0 FAIL / 0 SKIP**, including **89 real PostgreSQL cases**, on separate disposable PostgreSQL17 container `uninet-phase12-final-20261120-005800`, ID recorded in protected receipts, actual port **58624**, user postgres with explicit disposable database names. No destructive test targeted 55433.

The first regression invocation was **245 PASS / 2 FAIL**. One was fixture setup `3D000` (missing disposable upgrade database), corrected without production changes; its rerun passed. The genuine evidence-version conflict test observed **23505**, then an additional genuine PostgreSQL **40001** left the job correctly RetryScheduled with its configured 300-second backoff, beyond the test's 25-second wait. A second reproduction confirmed this. The only repository code change sets the **test recovery phase** backoff to 1 second after the genuine-23505 rollback assertions, allowing existing bounded worker retry to execute; the recovery test then passed. No assertion, trigger, production retry policy or readiness guard was weakened. Final counts replace earlier outcomes per test name; repeated executions are not additional tests.

Coverage includes V2–V5 canonical contracts, missing/future facts, typed V5 writer ingestion, merge/PR-Issue/dedup/timeline/attribution, policy1/2 capture/coverage/MVCC/forgery/immutability, real Peer service privacy/revisions/closure on disposable, normalization/External/zero-mass/largest remainder, V1 compatibility, scoped claim/timeout, authorization-generation/lease/atomic recovery. General unsupported partial-overlap cases remain explicit limitations, not fabricated algorithm tests.

API/Application/Infrastructure isolated build **PASS**; current EF model consistency **PASS**; `git diff --check` **PASS**. Existing compiler/analyzer/line-ending warnings remain warnings. No new migration; 86 applied and seven unrelated pending IDs unchanged.

Task changes:

- [Hosted worker regression fixture](../../UniNet.Tests/GitHubAppActivityWebhookPostgresTests.cs): recovery-phase test backoff only.
- This report, [693-row observation trace](UNINET_PHASE12_FINAL_OBSERVATIONS.csv), [scored trace](UNINET_PHASE12_FINAL_SCORED.csv), latest-agent pointer and Phase 2 follow-up.
- Bounded auth/sync/capture/audit/runtime scripts and backup receipts outside the repository. No production source, formula, authorization/ownership policy, old migration, launcher or watchdog change. Prior dirty work retained; no commit/push.

## F. Safety and durable end state

| Data | Before → after |
|---|---|
| Jobs | 23 → **24**, exactly the approved new Manual job, all Completed |
| Checkpoints | 447 → **470**, 23 added |
| Evidence versions | 100 → **102**, two new V5 |
| Observations | 646 → **693**, 47 added; EP-06 duplicates **0** |
| Snapshots | 4 → **5**, exactly one new finalized policy2 |
| Coverage receipts | 0 → **470**, genuine captured metadata |
| Runs | **3 → 3** |
| Peer | Same cohort Open, **0/2**, no revisions/snapshot |
| History | **86 → 86**, seven pending unchanged |

All **1,216 original evidence/observation/job/checkpoint rows** match their original per-ID full-row fingerprints. All four old snapshot headers/memberships and three Runs remain unchanged. Legacy Contribution, identities/ownership/history, memberships, App authorization generations and cohort data retain baseline fingerprints. Composite provenance mismatch count=0. Only ten expected tables changed: auth Accounts/RefreshTokens; scoped sync jobs/checkpoints/provider objects/versions/observations; new snapshot headers/membership/coverage. Every other application table and schema catalog is unchanged.

Worker-off restored: **PID20740**, Global=false, ManualEnqueue=false, Scoped=false, JobId absent/empty, unrelated workers off; one listener on5239, `/health` **200**. Broad readiness remains **503**, unchanged semantics. The consumer shutdown wrapper exited nonzero after the old process had stopped; process/listener absence was verified before starting the same verified binary in worker-off mode. No second consumer/job or unknown process kill was used to recover the API. Logs and runtime pointer identify the final instance.

Protected receipts: `C:/Users/ADMIN/AppData/Local/UniNetE2EBackups/phase1-phase2-final-20261120-005640/`. Both pre-operation and final backups imported exported read-only MVCC snapshots, transport SHA-256 verified, ACL-protected, and restored on disposable with exact **103-table / 86-history-ID** comparison:

- Pre-operation backup SHA-256: `FFADAEA7B17C66BB9774F69FC731C1E9116213CF43A3D3805CDAD93CBACDB878`.
- Final backup `postflight-backup/e2e-post-operations.dump`, SHA-256: `ec950014aacf5e64b6bae01f54b10056d73ec86f31ba4d0be19470460d4d7ff9`.

No restore over E2E, reset, unrelated migration, Neon/production access, fake GitHub activity, identity transfer or private token log. E2E immutability verification used persisted hashes/catalog and disposable write tests; no destructive UPDATE/DELETE test was attempted on live snapshots/versions/runs.

## G. Phase 3 readiness and next action

**BLOCKED BEFORE PHASE 3.** Git V5/coverage and the new frozen experimental Git input are ready. Final Phase 1+2 acceptance is not satisfied because:

| Blocker | Evidence / impact | Required action |
|---|---|---|
| Missing real evaluations | Current0/Required2, both A and B missing | Each member independently submits their genuine rating using the existing guide; agent cannot supply this data |
| No closed Peer Snapshot | Cohort still Open; PeerShare Pending | Verify genuine complete submissions, roster/revisions and close via approved Creator API |
| No Experimental Run V2/final percent | Readiness gate intentionally not invoked without Peer | After Git+Peer Ready, create and verify the conditionally approved draft; no finalize |

No new authorization is needed merely to perform the already conditionally approved close/run steps once their prerequisites truly hold. **The one-sync and one-capture budgets are consumed**; another sync/capture requires separate approval. General hybrid partial-patch allocation and post-merge force-push decisions remain documented policy limits for cases needing them; do not invent thresholds or silently relabel them implemented.

Stop before Phase 3 Review/Finalize/Publication/Frontend. Do not recreate the cohort, repeat sync/capture, convert Pending Peer into zero, or alter the old legacy cycle/runs to claim completion.
