# UNINET — Experimental Run 3 scoring and peer readiness audit

Audit date: 2026-11-18. All timestamps below are UTC. Run `b64c65aa-cdbc-4ab9-b6dc-bb9dc565516e`, snapshot `7b8f784b-e69f-423f-9487-0cf48c4b03d9`, project `68f98a9f-d73d-4dcf-be5c-7099b29329c0`. Student A = `fef4ef4d-0049-4f7a-b629-fe9760bbbff9`, verified GitHub ID `156803598`; Student B = `8791ff0a-38a4-44e2-97f3-3ef8fd0d2202`, verified GitHub ID `180517531`.

## A. Scoring audit

Run remains Draft. Read-only PostgreSQL access used the explicitly verified environment: loopback port 55433, database `uninet_github_app_e2e`, configured role `uninet_test`, container `uninet-step2-concurrency`; every SQL session used `default_transaction_read_only=on`. No API calculation/review/finalize was invoked. No credentials are included here.

Independent decimal recomputation from frozen candidate activities, removing SHAs represented by merged PRs:

| Member | Commit | PR | Review | Issue | GitScore | PeerScore | Total |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| A | 4/5 = 80 | 2/2 = 100 | 0 | 4/8 = 50 | 65.50 | 0 | 39.30 |
| B | 1/5 = 20 | 0/2 = 0 | 0 | 4/8 = 50 | 14.50 | 0 | 8.70 |

`A Git = .35×80 + .30×100 + .20×0 + .15×50 = 65.50`; `B Git = .35×20 + .15×50 = 14.50`. Totals are respectively `.60×65.50 = 39.30` and `.60×14.50 = 8.70`. No External activity survives in these denominators. Both frozen results and ResultsJson match this recomputation, with no rounding ambiguity for these fractions.

This validates arithmetic under **ExperimentalGitScoringV1**, not the unimplemented official substantive-work algorithm. The scorer currently rounds category scores and GitScore before Total; this differs from a strict final-only-rounding interpretation, but causes no discrepancy for Run 3's exact fractions. No rounding policy was changed.

## B. Git activity attribution

The frozen inputs contain 20 pre-policy activity candidates: 10 commits, two PRs and eight Issue activities. Five commit candidates are then excluded under merged PRs, leaving 15 scored activities. Do not interpret all 20 frozen `eligibleActivities` as final scored activities.

### Five standalone eligible commits

| SHA | Owner / actor | CommitterDate | Snapshot branch scopes | Interpretation |
| --- | --- | --- | --- | --- |
| `5b4d9083790247a18fc3996cc47676053515c90b` | A / 156803598 | 2026-11-05 10:52:24 | feature/e2e-sync, main, webhook-e2e-push-test | Initial standalone work |
| `53e67b9706fbc6d257f74a04c4b7b3387ecf99ea` | B / 180517531 | 2026-11-05 11:10:36 | main, webhook-e2e-push-test | Merge commit for PR 1 |
| `d72270306add827a239b9bf335a222b3ec2954aa` | A / 156803598 | 2026-11-06 05:44:16 | main, webhook-e2e-push-test | Standalone commit |
| `9660540cd9d13d6c5e9746530e7bd7538c09aa15` | A / 156803598 | 2026-11-15 12:13:04 | main | Merge commit for PR 3 |
| `7f0aa3fa27fc007a88afc32bebfe87141d2f30cb` | A / 156803598 | 2026-11-16 15:49:36 | webhook-e2e-push-test | Standalone commit |

All ten unique SHAs observed across the three current GitHub branches are represented among the ten frozen commit candidates. No distinct commit was lost merely because it appeared on several branches or in several sync jobs. The other five are explicitly scored under PRs, not silently dropped.

The commit-derived timeline is 2026-11-05 10:52:24 through 2026-11-16 15:49:36. All selected commit, PR and Issue timestamps fall within it and before the snapshot cutoff, 2026-11-18 14:15:12.699776. The experimental source checks cutoff, not a separately frozen MIN/MAX timeline eligibility window. No exclusion in this run is due to join date/project creation time. Earlier work predating the project record and LinkedAt is not automatically ineligible under the approved policies.

### PRs

| PR / provider ID | Author | CreatedAt | ClosedAt = MergedAt | Base SHA | Head SHA |
| --- | --- | --- | --- | --- | --- |
| 1 / 5170525942 | A / 156803598 | 2026-11-05 11:09:04 | 2026-11-05 11:10:37 | `5b4d9083790247a18fc3996cc47676053515c90b` | `6004eece59bc4c0f436fb945d0e29cd473d968c0` |
| 3 / 5296699632 | A / 156803598 | 2026-11-13 13:54:30 | 2026-11-15 12:13:04 | `d72270306add827a239b9bf335a222b3ec2954aa` | `ee23b382b10c009fd0dffdc83fb6acb6a9c4001e` |

Both frozen and live states are closed/merged. GitHub GET confirmed exactly two PRs, with two commits in PR 1 and three in PR 3, without next-page links:

- PR 1: `1e2dba819ad3e423c5e6c5753c431ab1234c8a52`, `6004eece59bc4c0f436fb945d0e29cd473d968c0`.
- PR 3: `e970dddd0018e20630af681aeb62d2d082fd5e05`, `1afd2ba2fd4b26d4128aea0368870aca4c56c96e`, `ee23b382b10c009fd0dffdc83fb6acb6a9c4001e`.

All five PR-associated commits have A as the provider author. Experimental V1 requires at least one author-associated commit, then gives the PR one unit assigned to its author. B did not author either PR, so B receives no PR unit. This is a simplified experimental author-credit rule, not full evidence-based proportional allocation. B merged PR 1; current policy does not separately allocate PR author credit for being the merger.

The two commit sets are disjoint; no exact/partial SHA overlap or merge-time tie occurs here. GitHub file lists are also disjoint: PR 1 changes `README.md` and `test.txt`; PR 3 changes `webhook-e2e-push-test.txt`. No identical patch hash was found across those current file lists. This does not establish general content-level non-overlap or historical force-push absence. The scorer does not implement hybrid patch overlap, evidence-quality exact-overlap priority or a historical final merge tree proof. Preserve its limitations.

### Review

There are no frozen review entries or review exclusions. Latest local completed review checkpoints for scopes 1 and 3 report zero items. Independent GitHub GET returned an empty review array for each PR and no next-page Link. `NotApplicable` is supported for this repository at audit time; do not infer completeness for arbitrary snapshots from absent entries alone. Empty endpoint coverage is not frozen as an immutable receipt in the run, and deleted historical reviews cannot be disproved by a current GET.

### Eight scored Issue activities

| Evidence provider ID | Parent | Type | Actor / owner | CreatedAt |
| --- | --- | --- | --- | --- |
| 6222025831 | Issue 2 | Issue opened | 156803598 / A | 2026-11-05 11:12:03 |
| 6372817014 | Issue 4 | Issue opened | 156803598 / A | 2026-11-13 14:01:50 |
| 35232873981 | PR 1 | closed event | 180517531 / B | 2026-11-05 11:10:37 |
| 35233105321 | Issue 2 | closed event | 180517531 / B | 2026-11-05 11:13:18 |
| 36188344053 | PR 3 | closed event | 156803598 / A | 2026-11-15 12:13:04 |
| 36188358372 | Issue 4 | closed event | 156803598 / A | 2026-11-15 12:13:23 |
| 6656484093 | PR 1 | issue-API comment | 180517531 / B | 2026-11-05 11:10:04 |
| 6656546011 | Issue 2 | issue comment | 180517531 / B | 2026-11-05 11:13:15 |

Each has a distinct source object ID and one scored unit after observation dedup. However the source excludes PR parent records via `IssueIsPullRequest` while accepting their issue-API comments/events. Thus 50/50 includes three PR-child activities. This is a real semantic overlap to resolve, not a duplicate observation. Do not silently change V1's approved event policy during an audit. If Issue scoring is intended to exclude all PR children, a non-official sensitivity calculation removes three units: A 3/5 = 60, B 2/5 = 40; Git scores become 67 and 13, totals 40.20 and 7.80. These are proposals, not corrected stored results.

Two standalone commits are the generated merge commits themselves. Exact PR-list dedup excludes source commits but does not exclude the merge commit SHA (it is not in the PR's commit list). Crediting that merge plus the PR is another substantive-work weighting question. Do not remove B's only standalone commit without an approved merge-credit rule; current frozen commit facts do not store parent SHAs, although live GitHub confirms two parents.

## C. Evidence coverage and all exclusions

| Category | Reason | Count | Representative IDs | Effect |
| --- | --- | ---: | --- | --- |
| commits | DuplicateObservation | 254 | `7f0aa3fa27fc007a88afc32bebfe87141d2f30cb` | Repeated observations, not 254 unique lost commits |
| commits | CountedUnderMergedPullRequest | 5 | Five PR SHA lists above | Work represented in PR category |
| pull-requests | DuplicateObservation | 24 | 5170525942, 5296699632 | Latest observations retained |
| pull-request-commits | DuplicateObservation | 60 | `1e2dba819ad3e423c5e6c5753c431ab1234c8a52` | Relationship evidence, not separately scored |
| issues | DuplicateObservation | 48 | 6222025831 | Latest retained |
| issues | IssueIsPullRequest | 2 | 6221981059, 6372685425 | PR parents intentionally not counted as issue openings |
| issue-comments | DuplicateObservation | 24 | 6656484093, 6656546011 | Latest retained |
| issue-events | DuplicateObservation | 69 | 35232873981 | Latest retained |
| issue-events | UnsupportedIssueEvent | 2 | 35232873798, 36188343998 | Both are GitHub `merged` events; do not assign unsupported points |
| **Total** | | **488** | | |

479 repeated observations + five PR-covered commits + four unsupported records = 488. Category totals are Commit 259, PR 24, Issue 145, plus 60 PR-commit relationship exclusions. No frozen exclusion is due to unknown ownership, missing timestamp, corrupt facts or out-of-timeline activity in this run. DuplicateObservation is labeled `Incomplete` in the stored classification; that does not mean the current retained object has missing facts.

All 599 memberships are accounted for: 488 exclusions, 15 final scored activities, five retained PR relationships and 91 non-scoring auxiliary observations (39 branches, 39 PR files, 13 releases). There is no unexplained missing row in this accounting. Every frozen observation/version/hash reference matched snapshot membership; all 71 unique stored versions independently recanonicalized to their stored hash (71 pass, zero failures).

`EvidenceCoverageIncomplete` remains justified by historical incomplete versions and unsupported auxiliary facts in the frozen snapshot. Experimental V1 can use the required subset of an incomplete item, e.g. a comment's verified actor and created time; it must not be presented as the official strict-completeness algorithm. `PartialPatchOverlapNotSupportedInExperimentalV1` remains true even when current live files do not appear to overlap. Neither warning was removed.

## D. Security and integrity

Frozen identity history has one project owner per verified provider ID. A's identity LinkedAt is 2026-11-16 15:15:29.726582, with OAuth connection created 2026-11-13 06:59:25.879625. B's identity LinkedAt is 2026-11-18 13:56:48.552449, with OAuth connection created 2026-11-18 13:56:47.840426. Matching numeric provider IDs and account/profile links were checked. No timestamp was backdated. Earlier activity credit follows approved Verified Historical Attribution, not event-time ownership inference from login. The current resolver rejects a provider ID with more than one historical UniNet owner in the project before cutoff, including sequential owners; that protection remains unchanged. Run 3 contains no such conflict.

Snapshot membership and evidence versions are DB-protected. Run 3 is draft; the Git run trigger protects finalized runs, not arbitrary raw-SQL changes to draft input. No draft mutation API was invoked. Frozen source records and run state were compared again at audit completion. The current service allows only the creator to manage experimental runs; a delegated role named Leader is not automatically equivalent. Alternate roster review/finalize compares the frozen roster IDs to the immutable official cycle roster and rejects mismatch before modifying state.

Two independently reproduced backend defects were fixed in source only:

1. PR relationship dictionary used only PR number, so a PR could borrow commits from another repository in the same project. A failing service test showed PRScore 100 when it should be 0 for missing own-repository evidence. The key now includes repository ID. Run 3 has one repository and is unaffected.
2. Scoring-input equality depended on JSON object property order; a retry of equivalent PostgreSQL-jsonb-shaped input could return `GIT_CONTRIBUTION_INPUT_MISMATCH`. A failing service test reproduced this by reordering the stored object. Hash canonicalization now sorts object keys recursively and normalizes decimal numbers while preserving array order. Existing runs' InputHash and JSON are not rewritten. Newly created hashes use canonical bytes; retries compare both sides using the canonical procedure. This is a technical hash representation change, not a scoring policy change.

Other source risks, not changed without a policy decision: only cutoff is enforced rather than a separate frozen timeline window; PR relationship subset completeness is not proven by merely finding a matching authored commit; exact-overlap quality ranking, partial patches and force-push history are not fully executable. SHA set overlap handling can suppress work pending verification. Those branches do not explain any additional exclusion in this single-repository run.

## E. Peer evaluation proposal — design only

Current cycle `53b8b52f-b5d0-4d53-a170-012a147b1891` is CollectingEvaluations and contains only A. There are zero peer submissions. Run 3 freezes A+B from effectiveProjectMembership, so source deliberately uses no legacy evaluations and sets both peer scores to zero. Existing peer API is cycle-scoped, requires the evaluator/evaluatee to be cycle participants, rejects self-evaluation and scores outside 1–5, and has a unique `(CycleId, EvaluatorUserId, EvaluateeUserId)` constraint plus roster FKs. B cannot use it for this legacy cycle; do not mutate that cycle to make it work.

Recommend a small **experimental peer cohort**, independent of the legacy cycle and not keyed directly to a run:

- Cohort: project, policy/version, frozen A+B roster and roster hash, opening/closing state. Explicitly include the creator once even if not in ProjectMembers. Reference the existing cycle for lineage only, never add cycle participants implicitly.
- Cohort participants: unique `(CohortId, UserId)`, immutable after opening. Obtain current authenticated active Student/project authorization using existing effective membership rules, then require frozen roster membership.
- Submission: `(CohortId, EvaluatorId, EvaluateeId)` unique, score check 1–5, no-self check, composite roster FKs and audit history. Identity comes from JWT, never a request-supplied evaluator. Two members require exactly two genuine submissions, A→B and B→A. Do not generate either rating.
- Peer snapshot: seal both submissions and their IDs/versions atomically when complete; retain immutable frozen values/hash. Calculation uses this snapshot, not mutable current ratings. Existing formula is `PeerScore = 100 × (receivedMean − 1) / 4`; for one incoming rating 1/2/3/4/5 maps to 0/25/50/75/100. Missing ratings must be Pending/Incomplete, not a real zero rating.
- Create a new draft experimental run after genuine evaluation; never edit Run 3. A new optional peer-snapshot/cohort reference and calculation-context/idempotency identity are needed: current uniqueness `(ProjectId, EvidenceSnapshotId, PolicyVersion)` cannot represent a new peer snapshot against the same evidence snapshot. Propose a separate new-context key/partial unique indexes preserving legacy identities; no migration or run rewrite was performed.
- Keep alternate-roster runs draft-only under current review/finalize guard. Cohort completion grants no official finalization permission. Promoting a cohort to official use is a separate approval, not part of this proposal.
- Existing experimental management is creator-only in source. Use that boundary until delegated Leader authority is explicitly approved; do not silently conflate Creator/Leader/Git Manager. Final results/member privacy follow existing access rules; no raw peer comments or private evidence are made public.

Approval needed before implementation: cohort design and whether ratings may be edited before sealing; when to close/seal the cohort; visibility of individual ratings/comments. Separate policy decisions: PR-child events/comments in Issue scoring, merge-commit credit and substantive PR allocation. No fake ratings, schema changes or official-cycle changes were made.

## F. Implementation and verification

Changed only the scoped relationship/hash portions of [GitExperimentalContributionService](../../UniNet.Application/Services/GitExperimentalContributionService.cs), added regressions in [GitExperimentalContributionServiceTests](../../UniNet.Tests/GitExperimentalContributionServiceTests.cs), and this audit document. Pre-existing modified source, membership work and handoff changes were preserved. No migration, live sync, GitHub mutation, backend restart, review/finalize, account/member/identity creation or commit/push occurred.

Two new regressions were first observed failing. After the minimal fixes, focused scorer, service, historical attribution, formula and legacy calculator tests: **36 PASS / 0 FAIL / 0 SKIP**, from current source with isolated artifacts. API/Application/Infrastructure/Domain built as test dependencies; existing compiler/analyzer warnings remain. The 71-hash audit is an independent read-only verification, not added to the automated test total. Full suite/E2E was not rerun.

Live GitHub read-only verification: [repository](https://github.com/trangtanduoc/uninet-github-app-e2e), [PR 1](https://github.com/trangtanduoc/uninet-github-app-e2e/pull/1), [PR 3](https://github.com/trangtanduoc/uninet-github-app-e2e/pull/3), [Issue 2](https://github.com/trangtanduoc/uninet-github-app-e2e/issues/2), [Issue 4](https://github.com/trangtanduoc/uninet-github-app-e2e/issues/4). GET requests used public API access, no token or live sync job; current API state is corroboration, not a substitute for the frozen historical inputs.
