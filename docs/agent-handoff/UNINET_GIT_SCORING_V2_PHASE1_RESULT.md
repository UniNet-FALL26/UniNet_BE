# UNINET — Git Scoring V2 Phase 1 result

## Result

**Snapshot capture and real Git-only diagnostics completed. Git readiness remains Incomplete.** Known eligible activities produce provisional raw GitScores **Student A 76.25 / Student B 3.75**. Ready GitScore, GitShare and Final ContributionPercent are **unavailable**, not zero. No Peer Cohort/rating/snapshot, Contribution Run V2, scoring publication or finalization was created.

Final focused regression: **233 unique PASS / 0 FAIL / 0 SKIP**, including **86 PostgreSQL cases**. Five new unit tests were added and the existing PostgreSQL scoring flow was extended with read-only diagnostic assertions. Reruns are not additional tests.

## A. Snapshot and preflight

- Verified E2E target `127.0.0.1:55433/uninet_github_app_e2e`, user `uninet_test`, container `uninet-step2-concurrency` / `2fbe020661be4f7ae4a7fd6eac82524b6f3f33dad8ad74da7981e1ce3eb9f767`.
- Before capture, all 102 application-table fingerprints matched the completed V4 sync baseline. Project remains Completed, Creator A; association `25fe740d-dfbf-4859-8ce1-257247b4b3e7` is Active, linked, unpaused and points to `trangtanduoc/uninet-github-app-e2e`. App grant/installation and generations unchanged. No active sync job; approved Manual job `2d2a3ac5-9e77-4932-be3e-97c9ea15f44e` remains Completed, RetryCount=0.
- Exactly 85 applied migration IDs matched baseline; seven unrelated pending migrations unchanged. Pending enumeration compares actual migration IDs, excluding Designer/Archive helper files. No E2E migration operation.
- Creator A authenticated through the real Auth API and authenticated `/api/auth/me`, both 200; protected credential and JWT stayed in memory. There is **no standalone snapshot capture API** in the current source. The bounded local tool therefore invoked the existing internal [capture service](../../UniNet.Application/Services/ProjectGitEvidenceSnapshotService.cs) with that verified account and used the existing [authorized reader](../../UniNet.Application/Services/ProjectGitEvidenceSnapshotReader.cs). No new endpoint or direct SQL insertion.
- **One actual capture**, SnapshotId **`467894ae-e5ed-41c9-af7f-9095e581e5a4`**. Persisted cutoff **2026-11-19 15:54:46.221595 UTC**, policy 1, Finalized=true. Exact frozen membership: **646 observations / 100 versions**, one project association. Relation/cutoff mismatch count=0.
- Capture is Repeatable Read with atomic persisted membership and finalization. The service has **no request idempotency key**; calling CaptureAsync twice would intentionally create two snapshots. This execution used a durable one-attempt marker and saved the returned ID before any later read; it did not retry capture. PostgreSQL regression covers concurrent capture, late commits, rollback and immutability.
- A post-capture tool read initially failed SQLSTATE **25006** because the tool requested a PostgreSQL read-only transaction while the reader correctly acquires `FOR SHARE`. Capture had already committed and its receipt existed. The tool was corrected to allow authorization locks while rejecting DML commands; only reads/diagnostics were rerun on the same SnapshotId. No production lock was removed and no second capture occurred.

### Frozen membership / validation

| Schema | Observations | Versions |
|---|---:|---:|
| V2 | 564 | 65 |
| V3 | 42 | 6 |
| V4 | 40 | 29 |
| Total | 646 | 100 |

All 100 selected versions pass the reader's existing canonical/hash/identity validation. Across observations: **427 Valid, 219 Incomplete, 0 Unsupported, 0 Corrupt, 0 restricted**. Integrity-valid does not mean contribution-complete. V2/V3 historical records remain valid audit membership; scoring selects latest relevant evidence and does not backfill or promote them.

Computed frozen-membership SHA-256: `ab8ce4342fa945f59c34ba7016c4a78818fc0f0c4bce3a8b8b307c7d25f56833`.

Diagnostic input SHA-256: `7bf62b51ff877d67d3a8111f2a52708d0c364c0a82162f830c144f7475f1ee67`.

The snapshot entity has no persisted content-hash column. These hashes are retained in the diagnostic artifact: membership hash covers project/snapshot/cutoff/policy plus ordered observation/association/version IDs and immutable version hashes; input hash additionally covers read manifest, roster, identity history and candidate activities. Repeated current-source reads/diagnostics reproduce the hashes. Nothing was written back to the immutable snapshot.

Diagnostic roster is A=`fef4ef4d-0049-4f7a-b629-fe9760bbbff9` (Creator) and B=`8791ff0a-38a4-44e2-97f3-3ef8fd0d2202` (active member). Existing roster eligibility checks include Creator, active Student status, project membership and snapshot cutoff. The evidence snapshot itself does not freeze roster/identity ownership; this diagnostic artifact freezes that context, and a future authorized run must freeze its own inputs through the existing lifecycle.

## B. Actual Git-only scoring

These are the existing **experimental count-based** category calculations, with V2 merge/type/completeness/timeline filtering. They are not a newly invented official raw-work or patch-allocation formula.

| Member | Raw Commit | Raw PR | Raw Review* | Raw Issue* | Provisional raw GitScore | Ready GitScore / GitShare |
|---|---:|---:|---:|---:|---:|---|
| Student A | 100 | 100 | 0 | 75 | **76.25** | unavailable |
| Student B | 0 | 0 | 0 | 25 | **3.75** | unavailable |

`A = .35×100 + .30×100 + .20×0 + .15×75 = 76.25`.

`B = .35×0 + .30×0 + .20×0 + .15×25 = 3.75`.

*Review's arithmetic zero describes zero selected activities, **not** verified NotApplicable. Issue percentages describe only the currently proven eligible subset. Neither is a complete category result. No PeerScore was fabricated or passed to obtain these numbers; no TotalScore/Final ContributionPercent was computed. Sum 80 is not a member contribution percentage total and must not be redistributed or published as one.

### Nine scored activities

| Category | Evidence identity | Owner / applied rule |
|---|---|---|
| Commit | `5b4d9083790247a18fc3996cc47676053515c90b` | A; normal/root, not represented by a merged PR |
| Commit | `d72270306add827a239b9bf335a222b3ec2954aa` | A; normal standalone commit |
| Commit | `7f0aa3fa27fc007a88afc32bebfe87141d2f30cb` | A; normal standalone commit |
| PR #1 | `5170525942` | A; merged, verified author, two exact associated author-commit facts |
| PR #3 | `5296699632` | A; merged, verified author, three exact associated author-commit facts |
| Issue #2 creation | `6222025831` | A; IsPullRequest=false |
| Issue #4 creation | `6372817014` | A; IsPullRequest=false |
| Issue #2 closed | `35233105321` | B; supported event with actor/time and real-issue parent |
| Issue #4 closed | `36188358372` | A; supported event with actor/time and real-issue parent |

All scored timestamps fall within the selected normal-commit CommitterDate window **2026-11-05 10:52:24 through 2026-11-16 15:49:36 UTC** and snapshot cutoff. The added V2 guard now enforces that agreed commit-derived window; it does not substitute Project.CreatedAt, LinkedAt, membership JoinedAt, AuthorDate or ObservedAt for contribution time. Missing window evidence is NotReady. V1 retains its previous cutoff behavior.

### Attribution / External

- Actual attribution uses numeric actor IDs A=`156803598`, B=`180517531`, verified project identity history and the A+B roster. One verified historical owner for each, no ownership conflict, no identity mutation or backdated link. Older activity can predate verification under the approved historical policy.
- B's sole commit-author identity is pure merge `53e67b9706fbc6d257f74a04c4b7b3387ecf99ea`; it receives no standalone CommitScore. A's pure merge `9660540cd9d13d6c5e9746530e7bd7538c09aa15` is also excluded. Both have two parents, complete parent trees and matching result/parent tree.
- No eligible External activity was established in this snapshot's selected scoring inputs. GitHub automation committer `19864447` is not treated as an External author. Unknown/unverified actors are not automatically External. Focused tests retain conflict rejection and verified-outside-roster versus current-member distinctions; this is not a claim that the repository can never have External work.

### Traceable disposition of all 646 observations

[Every observation, exact job/version/association and reason](UNINET_GIT_SCORING_V2_PHASE1_OBSERVATIONS.csv); [nine scored activities with actor, time, observation/version/hash](UNINET_GIT_SCORING_V2_PHASE1_SCORED.csv).

| Disposition | Count |
|---|---:|
| DuplicateObservation | 519 |
| AlreadyRepresentedByPullRequest | 5 |
| PureMergeCommit | 2 |
| IssueIsPullRequest | 2 |
| PullRequestActivityExcludedFromIssue | 5 |
| IncompleteSourceEvidence | 1 |
| Scored | 9 |
| PullRequestRelationshipEvidence (not extra points) | 5 |
| AuditOnlyNonScoringCategory | 98 |
| **Total** | **646** |

- Five commits represented by PRs (`1e2dba…`, `6004ee…`, `1afd2b…`, `e970dd…`, `ee23b3…`) are not scored twice. PR #1/#3 SHA sets are disjoint; no partial-SHA overlap or exact-set tie is exercised by this live data. Focused tests cover both exclusions. Patch/content overlap remains unimplemented and explicitly documented; no claim of general hybrid-overlap proof.
- PR-shaped issue rows #1/#3, their four events and one PR comment receive no IssueScore. Two of those PR events (`35232873798`, `36188343998`) lack EventKind: facts remain missing, but exclusion already follows independently proven PR parent type. No event kind was inferred.
- **B's real-issue #2 comment `6656546011` is not scored**: it has an identified actor/time/parent, but its immutable version remains Incomplete (`CategoryCoverage`). This is a known evidence-contract limitation that can suppress legitimate work, not proof that the comment is invalid. It cannot be promoted by a scorer. A future explicitly authorized retrieval/contract improvement would need new evidence and a new snapshot; no such sync occurred.
- The 98 audit-only observations are branches=42, PR files=42, releases=14. All remain available for audit; no raw mutable payload reconstruction or automatic points.

## C. Readiness / remaining blockers

| Category | Current result | Blocker / meaning |
|---|---|---|
| Commit | Eligible experimental subset: 3 | Exact SHA/merge/timeline checks pass for this data; general hybrid patch/force-push limitations remain |
| PR | Eligible experimental subset: 2 | Author-commit condition passes; not full official multi-author/patch allocation |
| Review | **Incomplete**, eligible=0 | `EmptyCategoryCoverageNotFrozen` |
| Issue | **Incomplete**, eligible=4 | `IncompleteSourceEvidence` for the real-issue comment |
| Git component | **Incomplete** | Ready GitScore/GitShare null |
| Peer / Final | Not calculated | No closed real Peer Snapshot; Phase 1 does not create one |

Blocker classification:

1. **Data/contract limitation:** frozen real-issue comment remains Incomplete. Propose defining required comment facts/coverage, updating retrieval/extraction if warranted, and separately approving a new official sync/capture. Do not rewrite this snapshot or frozen facts.
2. **Readiness evidence limitation:** completed job has two zero-item review checkpoints for PR #1/#3, but snapshot membership does not freeze successful empty retrieval receipts. Live checkpoints/API absence alone are not an immutable snapshot-level proof of NotApplicable. A future design can freeze bounded coverage receipts tied to the exact association/job/category/scope and capture context. No schema was added to force Ready here.
3. **Policy/implementation boundary:** hybrid patch similarity thresholds, partial allocation, evidence-quality priority beyond the experimental exact-set rule, independently verified removed work, and post-merge force-push treatment are not fully specified/implemented. The live disjoint SHA sets do not resolve these general rules. Keep partial-patch/force-push limitations. The new same-job relationship fence prevents a demonstrable union of removed historical PR SHAs; it does not reconstruct historical force-push events or prove the API list is the merge-time historical list.

There is **no new attribution/security blocker preventing real Peer collection**. Git-only diagnostics are sufficient to explain today's known subset, not to declare Git Ready or publish percentages.

## D. Implementation and regression

Production changes made in this task:

- [ExperimentalGitScoringV1](../../UniNet.Application/Services/ExperimentalGitScoringV1.cs): extract shared CalculateGit arithmetic/overlap logic with no peer inputs or totals. Existing V1 Calculate delegates and retains its category/rounding/peer behavior.
- [ExperimentalGitScoringV2](../../UniNet.Application/Services/ExperimentalGitScoringV2.cs): reject incomplete/corrupt/unsupported selected evidence; classify proven PR-derived exclusions before completeness; fence PR relationships to the selected PR's association/job; require empty-category coverage proof; enforce agreed CommitterDate window and explicit missing-window state.
- [GitExperimentalContributionService](../../UniNet.Application/Services/GitExperimentalContributionService.cs): share V2 readiness/timeline guards in real run creation; unchanged closed-Peer guard, authorization, roster/finalization restrictions and immutable persistence. No run creation was invoked on E2E.
- [Internal diagnostic partial](../../UniNet.Application/Services/GitExperimentalContributionDiagnostics.cs): authorized project-leader Git-only diagnostic, same selection/attribution/scoring helpers, exact observation disposition, hashed frozen audit context, no DML/run/peer/final allocation. It is not a public endpoint.

Tests changed: [V2 policy](../../UniNet.Tests/ExperimentalGitScoringV2Tests.cs), [contribution service](../../UniNet.Tests/GitExperimentalContributionServiceTests.cs), [PostgreSQL contribution flow](../../UniNet.Tests/GitExperimentalContributionPostgresHttpTests.cs), [snapshot PostgreSQL](../../UniNet.Tests/ProjectGitEvidenceSnapshotPostgresTests.cs), [worker PostgreSQL](../../UniNet.Tests/GitHubAppActivityWebhookPostgresTests.cs).

- Two new regressions were first observed failing: incomplete work accepted by V2; old PR membership surviving a replacement retrieval. Both now pass. Added empty-review proof, CommitterDate timeline and peer-free/read-only diagnostic tests. The PG flow checks repeated diagnostic hashes, exact membership audit count and no run/cohort writes before its separate disposable lifecycle test.
- Final **233 unique PASS / 0 FAIL / 0 SKIP**, **86 PostgreSQL cases**, covering V1/formula/rounding, attribution/unknown/conflict, merge/exact/partial overlap, writer/replay/rollback/collision, OAuth/App ABA/lease fencing, scoped isolation, snapshot/authorization/immutability, Git Sync and Contribution v1.
- Final broad focused execution was 231 PASS / 2 FAIL; after correcting the remaining two fixture issues, targeted rerun was 2 PASS / 0 FAIL. Aggregation uses each test's latest outcome, not 235 tests. Earlier failing executions are retained in local TRX receipts, not hidden or counted as extra passes.
- Regression exposed obsolete fixtures: schema 3 is now supported (use 99 for Unsupported); expected writer schema 2 became V4; completed retrieval is distinct from incomplete facts; a fully supplied supported closed event is Valid. New diagnostic test roster initially included the fixture's deliberate nonmember; now uses the actual project roster. A lease-handoff fixture waited 25s while using the production 30s retry delay; only that fixture now selects 1s bounded retry. No worker, validator or production retry policy was weakened.
- Dedicated PostgreSQL 17 container `uninet-scoring-phase1-disposable`, ID `724f4624bbab39993877d0a9a05c22d5791da918da297d9af7a29df81d71c0ed`, loopback **53875**, user postgres, explicitly named disposable databases. Fresh chain=92; snapshot-upgrade baseline=88; old schema guard fixtures=84/85/88. No test write used E2E 55433. Only disposable snapshot fixtures were reset between their upgrade rehearsals, not E2E; no destructive Down was used.
- The verified disposable container was stopped after tests; E2E PostgreSQL and API remained running.
- API/Application/Infrastructure built from current source to isolated artifacts: **PASS, no errors**. EF `HasPendingModelChanges()` on the freshly built model: **false**. No entity/model/snapshot/migration diff introduced by this task. `git diff --check`: PASS.
- Warnings remain separate: existing Application nullability warnings, API unread parameter, test analyzer/raw-SQL warnings, LF/CRLF conversion notices. None is a failed acceptance assertion.

## E. Safety / deployment

- E2E post-checks: only **Accounts, RefreshTokens, snapshot headers and snapshot entries** changed. Other **98/102** tables unchanged, including all runs, source versions/observations, jobs/checkpoints, identities, members, association, authorization/grants and legacy Contribution data.
- Old three snapshots and their 1,139 membership entries retain exact row fingerprints. New totals: four snapshots / 1,785 memberships. Runs remain three, peer cohorts remain zero. Source evidence stays 100 versions / 646 observations; jobs 23 Completed / zero active; no new sync, migration or GitHub activity.
- API PID **24848** remains the verified previous binary, worker-off (Global=false, ManualEnqueue=false, Scoped=false, JobId=null), health200. Broad readiness503 remains unchanged. **This task did not restart or deploy the new scoring fixes into that running API.** Diagnostics executed current compiled source in the bounded local tool. A controlled deployment of the fixes is required before later API V2-run creation.
- No official/experimental run was created, modified, reviewed or finalized on E2E. No fake or real peer rating/cohort was created. No new public evidence API, identity mapping change, password/OAuth reset, blanket migration, production/Neon access, commit or push.
- Existing dirty worktree changes were retained. Added this report, two trace CSVs and a latest handoff note. Local receipts: `C:/Users/ADMIN/AppData/Local/UniNetE2EBackups/git-scoring-v2-phase1-20261119-224951/`; includes actual capture attempt/ID, authenticated-read result, full diagnostic, before/after fingerprints and per-test outcomes. No passwords/JWT/installation tokens/private keys in artifacts.

## F. Phase 2 handoff

1. **Actual Git V2 result?** Yes, reproducible provisional raw subset: A=76.25, B=3.75, nine scored activities, every one of 646 observations traceable.
2. **Enough basis for final GitScores?** No. Issue contract/coverage and immutable empty Review proof remain unresolved. Do not label the provisional values complete or replace missing components with official zero.
3. **Normalize GitShare?** Not yet; diagnostic returns null while Git is Incomplete. No hypothetical share or final percentage is published.
4. **Must all Git blockers be fixed before Peer collection?** No security/identity blocker prevents collecting real ratings. They must be resolved before a Ready V2 calculation/publication.
5. **What can Phase 2 begin?** After separate approval, deploy the reviewed source fixes in worker-off mode and prepare the experimental A+B Peer Cohort, real authenticated reciprocal submissions and leader closure. No fabricated evaluation. Preserve legacy cycle.
6. **Finalization restriction:** existing legacy cycle is A-only. Alternate-roster A+B runs remain protected by `RequireOfficialRosterAsync`; do not assume a closed experimental Peer Cohort removes this guard. Publication/finalization for an alternate cohort needs an explicit approved policy/path, without rewriting the legacy cycle or old runs.

**Phase 1 snapshot/diagnostic work is complete; Git readiness is PARTIAL / Incomplete. Stop before Peer Cohort and Run V2. No new acceptance phase is proposed.**
