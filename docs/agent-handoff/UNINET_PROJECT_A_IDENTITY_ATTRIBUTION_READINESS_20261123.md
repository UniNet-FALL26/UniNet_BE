# UNINET — PROJECT A IDENTITY ATTRIBUTION & CONTRIBUTION READINESS AUDIT

Date: 2026-11-23. **Read-only audit; no snapshot, Peer action, Contribution Run, score publication or project-status change.**

## Environment and method

Verified FE clone `127.0.0.1:55433/uninet_github_fe_e2e_20261121`, role `uninet_test`, Docker `uninet-step2-concurrency`. Only `.env.github-app.e2e` was read and connection target explicitly overridden/checked. Root `.env`, original E2E and Neon were not accessed. API PID 19808, health 200, runtime receipt workers off. Both scoped jobs remain Completed, retry 0.

Before/after full-table fingerprints match exactly, including identities, memberships, snapshots/runs, Project B and migration history. No login/session mutation or provider request was needed. No production source change, migration, worker activation, enqueue or commit/push.

The official `DiagnoseGitAsync` requires Completed plus a persisted snapshot. Project A is Active and has no snapshot, so it was **not invoked or bypassed**. Instead a protected temporary helper loaded project-authorized observations/frozen facts/history in one PostgreSQL Repeatable Read READ ONLY transaction and invoked existing pure selection, attribution, timeline and overlap functions. It built an **in-memory audit projection only** (SnapshotId empty, no persisted membership or coverage receipts) and output activity counts/exclusions, not percentages. CalculateGit was used only to obtain its ScoredActivities/exclusions; raw/category/share values were not exported. These counts are conditional policy candidates, not official/frozen results.

OfficialContributionService.StatusAsync was separately invoked as the active Creator with a connection configured default_transaction_read_only=on. The first wrapper attempt inside an explicit read-only transaction encountered PostgreSQL 25006 because Actor requests FOR SHARE when an explicit transaction exists; the successful status read used normal implicit read-only queries. No write occurred in either attempt.

Audit point: `2026-11-23T04:00:15.6315954Z`. Receipt folder (protected current principal/SYSTEM): `%LOCALAPPDATA%/UniNetGitIdentityAudit/20261123-105830/`. It contains policy-preview JSON and before/after fingerprints; no credentials or tokens.

## 1. Identity verification

Project A `44b9e967-a830-4061-b33b-5d96ca41df07` has **2 Active project identities**:

| Member | Profile ID | Identity ID | GitHub numeric actor ID |
|---|---|---|---:|
| Student A | fef4ef4d-0049-4f7a-b629-fe9760bbbff9 | 6665e455-a04f-41b4-bb7d-c7a4c2071729 | 156803598 |
| Student B | 8791ff0a-38a4-44e2-97f3-3ef8fd0d2202 | 33f3e9dd-c1d3-4185-9acb-cd7b27fdb02a | 180517531 |

Both VerifiedByConnectionId references resolve to Active, non-revoked OAuth connections owned by the matching UniNet account and with identical provider numeric ID. Connections were established at A `2026-11-13T06:59:25.879625Z`, B `2026-11-18T13:56:47.840426Z`. Source [ProjectGitIdentityService](../../UniNet.Application/Services/ProjectGitIdentityService.cs) verifies account ownership, valid credential and authenticated provider user ID when linking/reactivating; it does not accept a manually asserted login as verification. This audit verifies persisted proof and the application path, not a new live consent/token refresh.

A linked at `2026-11-23T03:16:14.586105Z`, briefly unlinked, then reactivated. Its history contains Linked at 03:16:14.633425Z, Unlinked at 03:16:37.553996Z, Reactivated at 03:16:43.606436Z, all for the same owner. B linked at 03:22:54.008891Z, history at 03:22:54.010439Z. Current states are Active. No actor has multiple historical owners in A; no ambiguity/double attribution. No B-project identity/history was substituted.

### Historical cutoff and timeline

[GitHistoricalAttributionService](../../UniNet.Application/Services/GitHistoricalAttributionService.cs) filters history by project/provider/numeric ID and OccurredAt <= historyCutoff, fails closed on multiple historical owners, and requires a verified latest Active owner in the roster. Verified historical attribution intentionally permits earlier activity; it does not require activityAt >= LinkedAt/ConnectedAt and does not backdate either timestamp.

A future snapshot cutoff must include the identity history it intends to use: **choose a current valid cutoff after B's 03:22:54.010439Z history**, not merely the jobs' earlier 03:00 completion time. An old cutoff before verification would correctly lack that history.

[ExperimentalGitScoringV2.ApplyCommitTimeline](../../UniNet.Application/Services/ExperimentalGitScoringV2.cs) uses min/max valid frozen CommitterDate across project-selected repositories, bounded by observation cutoff. This is **not a Project.CreatedAt or member.JoinedAt event-time filter**. Roster eligibility checks current active membership/account and joined-by-cutoff separately. The mln131 commit from 2026-01-19 is retained under this existing historical policy, although Project A was created later. No new project-start eligibility rule was invented.

## 2. Raw evidence vs policy candidates

Current Project A membership in the read point: **49 observations / 38 distinct immutable versions**, from both authorized associations. This is an audit view, not an immutable Snapshot.

| Member | Commit candidates retained | PR | Review | Issue-category activities |
|---|---:|---:|---:|---:|
| A | 4 | 2 | 0 | 3 |
| B | 0 | 0 | 0 | 2 |
| C | 0 verified | 0 verified | 0 verified | 0 verified |

**Unattributed candidates: 0; verified external candidates: 0 in this evidence set.** This does not infer C has no Git activity outside collected/verified evidence. No unknown actor was assigned to C, Creator or Leader. No Contribution percentages were computed/reported.

A retained commit SHAs:
- `d87243e716a7696c46ad69a27a89f5f6c1034d02` — mln131-group4.
- `d72270306add827a239b9bf335a222b3ec2954aa` — uninet-github-app-e2e.
- `5b4d9083790247a18fc3996cc47676053515c90b` — uninet-github-app-e2e.
- `7f0aa3fa27fc007a88afc32bebfe87141d2f30cb` — uninet-github-app-e2e.

Both retained PRs belong to A: provider IDs `5170525942` (#1), `5296699632` (#3), merged and supported by frozen author/commit relationships. A Issue activities are issue creation `6222025831`, issue creation `6372817014`, issue event `36188358372`; B has issue event `35233105321` and comment `6656546011`.

B's commit `53e67b9706fbc6d257f74a04c4b7b3387ecf99ea` resolves to B by numeric author ID but is excluded as PureMergeCommit. A's `9660540cd9d13d6c5e9746530e7bd7538c09aa15` is also a pure merge. B's zero retained Commit count is therefore policy exclusion, not identity failure.

### Complete accounting of the 49 observations

| Group | Count | Meaning |
|---|---:|---|
| Audit-only | 8 | 4 branches, 3 PR files, 1 release; not standalone scored activities |
| DuplicateObservation | 11 | Same commit work across branch observations; retained as audit, no extra credit |
| PullRequestActivityExcludedFromIssue | 5 | PR-derived issue events/comments do not earn Issue credit |
| IssueIsPullRequest | 2 | GitHub issue-list PR rows do not become Issue activities |
| PureMergeCommit | 2 | One A and one B merge excluded by frozen tree/parent facts |
| CountedUnderMergedPullRequest | 5 | A commits already represented by PR; no Commit+PR double counting |
| PR relationship evidence | 5 | Supporting commit-list evidence, not extra activities |
| Retained candidate activities | 11 | A=9, B=2 as table above |

No partial-overlap, exact-overlap tie, ownership-conflict or timeline exclusion was exercised by this current set. Their policy limits remain; no blanket claim of full patch/force-push support.

## 3. Completeness and review coverage

Seven observations remain Incomplete: four branches and one release with CategoryCoverage missing, plus issue events `35232873798` and `36188343998` with EventKind missing. Frozen facts are unchanged. Branch/release entries are audit-only; both incomplete events are PR-derived and already excluded from Issue by V2, so they do **not currently invalidate the retained Commit/PR/Issue candidate facts**.

Review count is zero. Completed source checkpoints show:
- mln131 PR enumeration: 0 items, Completed.
- uninet-e2e PR enumeration: 2 items, Completed; review scopes #1 and #3 each 0 items, Completed.

These 28 completed checkpoints across both jobs are eligible **source rows for future policy-2 capture**. They are not frozen snapshot receipts yet. Review must not be declared officially NotApplicable solely from live emptiness. A policy-2 snapshot/readiness must verify the captured coverage; if it proves empty review coverage, existing policy can classify Review NotApplicable without fake zero evidence.

Both repositories contain technically Complete commit evidence suitable for project snapshot capture; uninet-e2e also has retained PR/Issue candidates. No additional Git sync is required by this audit alone. Authorization must still be revalidated at actual capture/read/calculation time.

## 4. Readiness and applicable service paths

Actual OfficialContributionService.StatusAsync returned:
- Mode **OfficialV2** (already selected).
- CanManage=true for Creator A.
- ProjectCompleted=false.
- Blockers: **ProjectNotCompleted**.
- No Git snapshot, Peer cohort/snapshot, official run or publication for A.

The status endpoint is a coarse workflow summary, not proof that missing frozen inputs can be skipped. Current official capture and diagnostic/run creation require Completed; create also requires policy-2 Git Snapshot + closed valid Peer Snapshot, exact effective roster, Ready calculation and integrity validation. A presently Active project cannot create an Official Run through this contract.

[GitContributionReadinessService](../../UniNet.Application/Services/GitContributionReadinessService.cs) is an older internal scaffold always returning NotReady with ScoringAlgorithmNotSpecified/AttributionInputsNotFrozen; it is not registered/called by the current official route. The actual official readiness path delegates to engine DiagnoseGitAsync and V2 readiness. Do not report the scaffold's generic missing-formula reasons as newly discovered official blockers.

Official code deliberately reuses ExperimentalGitScoringV2 and normalizes via GitPeerSeparate60_40V1; category weights 35/30/20/15 and separate Git 60% + Peer 40% remain. Names containing Experimental do not alone prove a new policy decision is needed, but partial patch overlap and historical force-push limitations must remain visible. No new scoring policy was introduced or approved in this audit.

## 5. Student C and Peer roster

A/B/C are all Active Student accounts and Active project members. C has no project Git identity. There is **no requirement that every member verify GitHub** before capture/readiness; the coarse status requires at least one verified identity and scoring only attributes proven numeric actor ownership. C is not automatically external and receives no fabricated Git activity/score.

Official Peer/run roster must include **all effective members including Creator**. With current roster A+B+C, future Peer completion requires **6 real directed evaluations**, no self-rating, not an A+B-only official cohort. C can participate in Peer without linking GitHub. Do not remove C or create ratings to satisfy readiness. A+B alternate roster would not satisfy the current official full-roster guard.

Official management/review/finalize/publication is Creator-authorized. Draft -> Review -> Approved -> Finalized -> explicit Published; no early result disclosure or finalized-input mutation. Delegated Git repository management does not automatically grant official Contribution management.

## 6. Blockers and exact next steps

| Item | Owner/type | Action |
|---|---|---|
| Project Active | User / project business lifecycle | Complete through normal application flow only when appropriate; audit did not change status |
| No frozen Git Snapshot | Next explicitly authorized E2E write | Creator captures policy-2 snapshot after identity history cutoff; freezes both associations and coverage |
| Review empty proof not frozen | Input dependency | Read captured policy-2 readiness; do not substitute live counters |
| No Peer inputs | User-authenticated evaluations | Open official A+B+C cohort when permitted; each member supplies real ratings; Creator closes it |
| No immutable roster/history calculation inputs | Run lifecycle | Use Git/Peer snapshots through Official service, which freezes roster/identity history and hashes |
| Partial overlap / force-push limits | Existing bounded policy limitation | Retain diagnostics; not observed as a current-set exclusion, not silently removed |

Suggested next approved sequence:
1. Creator completes Project A via its supported project lifecycle when it is genuinely ready for that state.
2. Explicitly authorize one policy-2 snapshot capture with a cutoff after A/B identity verification. Membership freezes 49 currently visible observations if no intervening data/state change; actual capture determines exact membership and receipts.
3. GET `/api/projects/{A}/contribution/v2/snapshots/{snapshotId}/readiness`; validate captured facts, attribution and Review coverage. This is the first point a supported official diagnostic can attest frozen inputs.
4. Prepare official A+B+C Peer cohort, collect six real evaluations and close through authorized APIs, only with separate authorization.
5. If Git and Peer are Ready, explicitly authorize creation of a Draft Official V2 Run referencing both snapshots. Review/approval/finalize/publish are later separate actions.

No Backend defect requiring a source fix was demonstrated. Source-only limitations are documented, especially commit-derived timeline versus project creation dates. No automatic completion, capture, consent, Peer activity or run was performed.

## Verification

- Identity proof/ownership consistency: PASS (persisted proof + source path; no new live provider consent check).
- Read-only policy preview: completed; candidate/exclusion accounting totals 49; no score/share output.
- Official workflow status: actual service returned ProjectNotCompleted; no readiness guard bypass.
- Before/after full-table fingerprints: PASS, exact match. Project B unchanged.
- API health 200; workers remain off; no new job or immutable record.
- Automated test suite: **NOT RUN** (no production behavior changed; temporary audit helper compiled and executed). Prior 59-test result belongs to the sync implementation, not this audit.

**Conclusion: A/B attribution is available for current evidence; official Contribution remains NOT READY because project completion and frozen Git/Peer inputs are outstanding.**
