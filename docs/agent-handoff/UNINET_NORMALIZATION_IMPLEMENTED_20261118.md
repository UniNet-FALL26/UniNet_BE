# Git/Peer separate normalization — implementation and migration review

The user confirmed option 1 after the [V2/peer implementation report](UNINET_GIT_V2_PEER_IMPLEMENTATION_20261118.md). This document supersedes that report's pending-normalization sections; it does not change old runs or formulas retroactively.

## Implemented policy: GitPeerSeparate60_40V1

For each eligible category `c`, `w[c]` is Commit .35, PR .30, Review .20, Issue .15. After scoring-policy filtering, exact deduplication and verified attribution:

`g[i] = Σ_c w[c] × count(i,c) / count(all eligible members + External,c)`.

An empty category contributes zero to every `g`, including External; it is never treated as external mass. The existing scorer now also returns these **unrounded** weights and the true external weight without altering V1's rounded score/result fields. Unknown/conflicting actors are not treated as External; their exclusions keep readiness incomplete. Members outside the cohort cannot be silently assigned to External. These are activity-count units under the existing experimental scoring, not a new official substantive-work formula.

`GitShare[i] = g[i] / (Σ members g + gExternal)` and `ExternalGitShare = gExternal / that same denominator`.

`PeerShare[i] = frozen PeerScore[i] / Σ members frozen PeerScore` (no external peer participant or fabricated peer score).

When both components are applicable and complete:

- Member final weight = `.60 × GitShare[i] + .40 × PeerShare[i]`.
- External final weight = `.60 × ExternalGitShare`.
- A genuinely NotApplicable component transfers its top-level weight to the other complete component. An applicable zero-mass component yields InsufficientData, not redistribution. Incomplete components return no final percentages. Both zero yields InsufficientData when completeness is established.
- Convert all member and External final weights together to 10,000 basis points with largest remainder; equal remainders use ordinal participant IDs and a stable external-bucket key. No remainder privilege for leader/highest score. Member + External = exactly 100.00%; External = zero means member total = exactly 100.00%.

Examples: Git A/B/External=60/20/20 and Peer A/B=75/25 → 66/22/12. Git member weights 65.5/14.5 with equal Peer → 69.13/30.87 after deterministic rounding, not normalization of the combined RawTotalScore. Git only 3/1 with Peer genuinely NotApplicable → 75/25. Peer only 3/1 with Git genuinely NotApplicable → 75/25. A single member with positive Git and a closed single-member peer snapshot → 100% member, unless eligible External exists.

## Integrated flow and readiness

[Normalizer](../../UniNet.Application/Services/GitPeerNormalization.cs) is called by [experimental service](../../UniNet.Application/Services/GitExperimentalContributionService.cs). It freezes normalization policy, exact weights, component states, peer scores and final allocation into run input/hash, and persists NormalizedResultsJson/CalculationStatus/NormalizationPolicyVersion. Member DTOs expose RawTotalScore separately from ContributionPercent. V1 default requests remain V1; V1 persisted JSON/hash/results are not rewritten. Previous V2 inputs used a different normalization context; a new request creates a new context rather than updating the old run. The peer snapshot frozen roster is checked against the run roster in addition to its hash.

V2 still requires a valid closed peer snapshot; missing peer evaluations return PEER_SNAPSHOT_REQUIRED rather than producing fake zero scores. A single-participant closed cohort establishes Peer NotApplicable. No final percentages are returned for known Git coverage gaps, restricted evidence, unresolved attribution, uncertain merge work or unresolved overlap. **No Git entries alone does not establish Git NotApplicable:** current snapshots lack immutable empty-scope coverage receipts. The service marks this case `GitAbsenceNotProven` / IncompleteGitEvidence, not 100% Peer. The arithmetic supports Peer-only when absence can be independently proven; the current production capture does not yet supply that proof. This is a conservative technical limitation, not a new business-policy question.

## Migration diff and compatibility

This normalization integration requires **no new migration**. It uses the previously prepared additive migrations:

1. [20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2](../../UniNet.Infrastructure/Data/Migrations/20261118151344_AddExperimentalPeerCohortsAndGitPolicyV2.cs): creates ExperimentalPeerCohorts/Submissions/Revisions/Snapshots; adds nullable PeerSnapshotId, CalculationContextHash, CalculationStatus, NormalizationPolicyVersion, NormalizedResultsJson to GitContributionRuns; adds scoped FKs, audit triggers and V2 context uniqueness; retains legacy V1 uniqueness through a filtered index. No historic inputs or results are backfilled.
2. [20261118152617_GuardGitV2FinalNormalizationReadiness](../../UniNet.Infrastructure/Data/Migrations/20261118152617_GuardGitV2FinalNormalizationReadiness.cs): rejects finalized V2 rows unless readiness and normalization fields exist.

Forward review SQL: [GitPolicyV2PeerCohorts.review.sql](../migrations/GitPolicyV2PeerCohorts.review.sql), generated idempotently from AddGitExperimentalContributionRuns through the second migration. This is a review artifact, not authorization to execute it. Review index replacement locks/table growth and verify E2E migration baseline before applying. Down of the cohort migration refuses populated peer/V2 audit history; do not run a destructive rollback. Old migration sources are unchanged. New backend requires this schema; coordinated deployment is required. No automatic migration is introduced and no E2E migration was applied.

## Verification

Tests include separate-component arithmetic, External preservation, deduplicated external activity, changed verified attribution in new inputs, zero/incomplete/NotApplicable handling, deterministic rounding and order independence; complete-data V2 service tests with and without External; actual PostgreSQL HTTP capture → closed peer snapshot → Ready V2 percentages/idempotent read with stored JSON; incomplete evidence remains null; V1 persisted hash/results remain unchanged. Synthetic ratings exist only in disposable test fixtures, not in the real E2E cohort.

Disposable target was verified as container `uninet-policyv2-20261118`, label `policyv2-disposable`, loopback port 52008, role postgres. PostgreSQL tests target `uninet_git_mvp_disposable`; no E2E database was accessed in this task. No migration was created or applied for normalization. Final current-source focused regression: **89 PASS / 0 FAIL / 0 SKIP**. Explicit backend build and EF pending-model-changes passed; `git diff --check` passed. Existing warnings and a successful copy retry during concurrent EF/build tooling were observed; no backend process was stopped. No real cohort evaluation, finalization, live sync, backend restart or commit/push occurred.
