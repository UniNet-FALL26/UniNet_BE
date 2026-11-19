# UNINET — Git policy V2, experimental peer cohorts and percentage foundation

Date: 2026-11-18. **Status: PARTIAL — normalization conversion policy awaiting user selection.** Do not represent the rounding allocator as a completed Git/Peer/External normalization policy. No real E2E ratings were submitted and no E2E migration was applied.

## A. Git policy V2

[ExperimentalGitScoringV2](../../UniNet.Application/Services/ExperimentalGitScoringV2.cs) filters evidence before reusing the existing experimental count/weight arithmetic. V1 scorer semantics remain unchanged. Select V2 explicitly in the run request with `scoringPolicyVersion: ExperimentalGitScoringV2`; legacy requests default to V1.

- Commit graph evidence uses frozen facts schema 4: ordered parent SHAs, own tree SHA, fetched parent tree SHAs and optional PR merge SHA. New nullable fields are omitted for old contracts so V1–V3 hashes remain readable.
- [Provider](../../UniNet.API/Services/GitHubProviderClient.cs) fetches parent git-commit tree metadata for merge candidates outside the page transaction. It constructs parent requests only from validated SHA values, not provider URLs. [Extractor](../../UniNet.Application/Services/GitEvidenceFactsExtractor.cs) copies only allowlisted graph facts; the atomic writer then persists an immutable version and association observation as before.
- A commit whose tree is identical to a fully retrieved parent tree has no independent content beyond that parent and is excluded as `PureMergeCommit`. A multi-parent commit with a different tree is **not presumed pure**: it receives `MergeCommitAttributionUncertain` and no independent commit credit pending accepted evidence/policy for resolution work. Old evidence without parent identities is uncertain too. Commit messages are never used to classify merges.
- V2 retains exact SHA/PR dedup, translating `CountedUnderMergedPullRequest` to `AlreadyRepresentedByPullRequest`. Partial/tied overlap still requires review; it is not silently allocated.
- PR child comments/events are excluded from Issue using canonical parent type and `(repository ID, parent number)`. True Issue children remain eligible under the current experimental activity policy. Missing/conflicting parent evidence becomes `IssueParentTypeUnresolved`, not an Issue score. Issue number is now frozen in schema 4, including issue comments. Review comments are not introduced as a new scoring category.
- Incomplete, corrupt, unresolved or restricted evidence blocks final V2 readiness. Historical duplicate observations alone are not a missing-facts blocker. A verified current UniNet project member omitted from a cohort is not reclassified as External.

### Run 3 comparison boundary

Run 3 and its snapshot were not changed. The old snapshot lacks commit graph facts and some issue parent numbers, so it cannot be upgraded in place into a complete V2 calculation. A newly authorized observation/capture is required before an actual V2 result can establish those facts.

Sensitivity from the prior public-GitHub audit, **not a persisted V2 run**: if the two merge commits are established as pure and the three PR-child Issue units are removed, standalone Commit allocation becomes A 100 / B 0, PR remains 100 / 0, Issue becomes 60 / 40 and Review remains empty. GitScores would be A 74 / B 6 rather than V1 65.50 / 14.50. Without real peer evaluations those are not final contribution percentages. Uncertain independent merge changes must not be removed as proven pure merely to obtain this example.

## B. Experimental peer cohort V1

[Entities](../../UniNet.Domain/Entities/ExperimentalPeerCohort.cs), [service](../../UniNet.Application/Services/ExperimentalPeerCohortService.cs), [contracts](../../UniNet.Application/DTOs/ExperimentalPeerContracts.cs) and [controller](../../UniNet.API/Controllers/ExperimentalPeerCohortController.cs) are independent of the legacy cycle.

API prefix: `/api/projects/{projectId}/contribution/git-experimental/peer-cohorts`.

- `POST` opens a cohort with `participantIds`; the creator is added exactly once. All selected users must be effective project members with active Student accounts. Roster IDs/hash never change after creation.
- `GET /{cohortId}` returns progress. The existing experimental management boundary is the creator, currently treated as the managing leader; this change does not grant delegated Leader/Git Manager new authority.
- `PUT /{cohortId}/evaluations/{evaluateeId}` accepts score 1–5, optional comment up to 2000 chars, and `expectedRevision` for edits. Evaluator comes from JWT. Self/outside-roster submissions are rejected. Members read only their own outgoing submissions; leader reads all ratings/comments/history. No endpoint lets a leader impersonate an evaluator.
- Unique cohort/evaluator/evaluatee enforces one current rating. Revisions retain previous/new score and comment. States are Open, Collecting, ReadyToClose, Closed; completion does not auto-close.
- `POST /{cohortId}/close` requires the existing leader boundary, complete real submissions and still-eligible frozen participants. It creates an immutable peer snapshot and closes the cohort in one serializable transaction with a cohort row lock. Missing ratings are incomplete, not zero. The existing calculator aggregates received means on 1–5 into 0/25/50/75/100. A single-member cohort has no peer tasks and requires explicit leader closure.
- Concurrent edit/close returns either a valid serialized outcome or a safe 409 requiring a fresh request/transaction. No insert is retried inside an aborted transaction. A deterministic PostgreSQL test forced both requests to wait on the cohort lock and verified the snapshot score/revision matched committed submissions.
- PostgreSQL triggers protect the frozen roster, closed cohort/submissions, append-only history and peer snapshot. Snapshot insertion and cohort closure must commit atomically. Source also guards immutable peer entities in EF.
- On publication, member cohort responses omit ratings; public/member run results contain aggregates, not evaluator identities/comments. In a two-person cohort inference anonymity cannot be guaranteed, but another person's rating is not directly exposed.

## C. Normalization: implemented boundary and unresolved conversion

[ContributionPercentAllocator](../../UniNet.Application/Services/ContributionPercentAllocator.cs) implements deterministic largest-remainder rounding to 10,000 basis points. Stable ordinal participant-ID ordering breaks equal remainders; the external bucket has a stable key. It guarantees exactly 100.00% including the external bucket for positive complete input weights, never gives External's share to members, and returns no final percentages for Incomplete or InsufficientData. This is a tested rounding primitive, not yet an active business conversion formula.

One user question is pending: normalize Git over members + eligible External, Peer separately over members, combine 60/40; redistribute a truly NotApplicable top-level component to the other component, while zero-mass applicable or incomplete components block percentages. Under that proposal External with 20% Git receives 12% final share when Peer is applicable. The alternative is normalizing combined RawTotalScore, which yields different relative results. Do not select either silently. The principle that member total equals 100% without External, and member + External equals 100% with External, is already approved and is not being asked again.

Current V2 run status is `PendingNormalizationPolicy`, `IncompleteGitEvidence` or `InsufficientData` as appropriate. `ContributionPercent` and `ExternalContributionPercent` stay null. V2 creation requires a closed peer snapshot, so missing evaluations cannot be persisted as fake zero-rated peer results. Largest-remainder allocation is not wired to choose Git/Peer weights until the conversion decision arrives. No final percentages are published.

## D. Run integration and compatibility

[GitExperimentalContributionService](../../UniNet.Application/Services/GitExperimentalContributionService.cs) accepts `peerSnapshotId` for V2, enforces project and frozen-roster matching, validates peer snapshot hash, recomputes peer scores from its frozen ratings and freezes identity history and effective project-member IDs with calculation input. Different peer snapshots can produce different runs against the same evidence snapshot. Stable canonical context hashes provide idempotency; database uniqueness prevents duplicate contexts. Conflict requests return 409 and can retry with a fresh transaction.

New nullable run fields: PeerSnapshotId, CalculationContextHash, NormalizationPolicyVersion, CalculationStatus, NormalizedResultsJson. V1 persisted JSON/results/hashes are not backfilled or rewritten. V1's uniqueness remains in a filtered legacy index. V2 DTOs distinguish RawTotalScore from nullable ContributionPercent; existing TotalScore is never reinterpreted as percent. V2 inputs/results are DB-immutable even while draft, while review metadata can follow existing lifecycle. Finalization also has a DB readiness constraint. Alternate roster runs remain barred from review/finalize by the official roster guard, independently of cohort completion.

Migrations (new source only; old migrations unchanged):

1. [AddExperimentalPeerCohortsAndGitPolicyV2](../../UniNet.Infrastructure/Data/Migrations/20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2.cs): four new peer tables, nullable run fields, scoped uniqueness/FKs, audit triggers. Down refuses populated peer/V2 history without an archival/backup plan.
2. [GuardGitV2FinalNormalizationReadiness](../../UniNet.Infrastructure/Data/Migrations/20261118152617_GuardGitV2FinalNormalizationReadiness.cs): prevents DB finalization when V2 normalization is not Ready. Added after the first migration had already been exercised on disposable data; it does not rewrite that migration.

Deploy matching readers before writers produce schema 4. Do not run new APIs against an unmigrated database. Old workers may keep writing old evidence versions during a rollout; V2 must keep those incomplete rather than infer missing graph facts. No automatic migrations were introduced.

## E. Verification and E2E gates

Dedicated PostgreSQL 17 container `uninet-policyv2-20261118`, tmpfs storage, loopback port 52008, user postgres; databases `uninet_policyv2_disposable`, `uninet_git_mvp_disposable`, and `uninet_policyv2_fresh_disposable` were explicitly verified before use. Fresh migration chain and upgrade with existing disposable V1 runs were exercised. HTTP/JWT tests use synthetic test fixtures only, not the real E2E users' ratings. Existing E2E run 3 was compared read-only against the audit baseline and all fields were unchanged; no E2E data or snapshots were modified.

Focused tests cover V1 compatibility, graph classification, PR/Issue separation, creator roster inclusion, peer edit history/privacy, project/roster guards, cohort closure, raw-SQL immutability, V2 contexts/idempotency, alternate finalize protection, actual PostgreSQL edit/close conflict and percentage rounding. Final current-source focused run: **82 PASS / 0 FAIL / 0 SKIP**. Backend test dependencies built with isolated artifacts; EF pending-model-changes and `git diff --check` passed. Existing compiler/analyzer warnings remain. No full suite or real E2E was run.

E2E remains gated on: normalization conversion choice; explicit approval to apply the two new migrations to the local E2E target and deploy matching backend code; genuinely supplied/approved A→B and B→A ratings; newly authorized pipeline observation for missing graph/parent facts. Never fabricate ratings, retrofit old evidence, reset the cycle, finalize automatically or rewrite runs 1–3. Existing unrelated worktree changes are preserved. No commit/push was performed.
