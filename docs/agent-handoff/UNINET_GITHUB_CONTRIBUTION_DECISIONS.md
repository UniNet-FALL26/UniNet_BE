# UNINET — Git Contribution decisions

Updated 2026-11-15. Business rules below were explicitly restated by the user; the original conversation is the decision evidence when no repository ADR exists. Official scoring decisions remain distinct from the explicitly authorized experimental MVP policy.

## Confirmed formula and normalization

| Rule | User-confirmed policy | Current state |
| --- | --- | --- |
| Total | `0.60 × GitScore + 0.40 × PeerScore` | Used by the isolated experimental run; not an official Contribution result. |
| Git weights | Commit 35%, PR 30%, Code Review 20%, Issue 15%. | Used by the experimental MVP. |
| External, option B | External contributors do not join the roster or get UniNet results; permitted activity may remain evidence. | No official attribution/scoring. |
| Normalization, option C | Share of total **including External**; do not redistribute External's share. | Experimental MVP counts eligible activities per category and normalizes across roster members plus verified external owners. Unattributed actors are excluded, not treated as external. |
| Empty category | Score 0; do not redistribute its weight. | Experimental MVP assigns zero to a category with no eligible activity and reports its evidence status separately. |
| Rounding | Two decimal places on final result. | Experimental V1 uses `AwayFromZero`; official midpoint behavior remains open. |

`ExperimentalGitScoringV1` is a temporary, versioned MVP policy, not the official algorithm. It counts eligible commits, merged PRs, submitted reviews, and issue creation/events/comments; each category is normalized independently. It never credits an unresolved actor. PR author credit requires at least one exact PR-associated commit authored by that same verified provider actor. Exact commit SHAs represented by an eligible merged PR are not also scored as standalone commits. Identical commit sets across merged PRs are credited once to the uniquely earliest merge; if the earliest merge timestamp is tied, every tied PR is excluded for review rather than arbitrarily chosen. Partially overlapping PR commit sets are excluded as unsupported rather than split. Patch-level partial overlap is not calculated and is surfaced as a policy limitation. Official raw scoring, overlap allocation, midpoint rounding, and eligibility remain open.

## Identity, repository and roster

- GitHub OAuth and GitHub App; public and private repositories. A user may link multiple GitHub accounts under current policy. One GitHub account has at most one active owner **within one project**; ownership changes preserve history. Project scope and effective time matter. Login similarity is not identity proof.
- The user later requested attribution to the owner at activity time. The experimental MVP freezes the cycle roster and project identity history, then resolves only verified GitHub provider account IDs at the frozen activity timestamp and snapshot cutoff. Unattributed activity is excluded with a reason; verified owners outside the roster contribute only to the external denominator. The full official policy for work before linking and ownership transfer remains **OPEN BUSINESS DECISION**. Roster snapshot determines eligible participants; no automatic reduction by join date.
- Repository connection business decision: Project Creator or delegated Leader, with GitHub Admin permission, including inherited Organization permission; backend verifies authorization. No extra independent UniNet verifier just for connection. The previous repository-connection verifier rule is superseded, not the separate overlap dispute verification.
- **Source discrepancy to audit:** current [ProjectGitAuthorization](../../UniNet.Application/Services/ProjectGitAuthorization.cs) permits project creator or active delegated Git Manager, while [GitHubAppUserGrantVerifier](../../UniNet.Application/Services/GitHubAppUserGrantVerifier.cs) checks GitHub Admin for App linking. Do not equate Git Manager, Creator and Leader or assume the OAuth/App paths enforce identical authorization. Resolve any mismatch before modifying permissions.
- Project timeline: MIN/MAX eligible commit CommitterDate in UTC; keep AuthorDate and FirstObservedAt separately, never silently substitute missing CommitterDate. All retrievable branches are the synchronization goal. Eligibility, late arrivals and no eligible commits need explicit handling.
- Leader confirms results before publication. Finalized contribution runs remain immutable. Draft/final access must follow the approved role policy; current v1 source is not proof that Git v2 roles are fully implemented.

## PR, overlap and force-push

| Decision | Recovered content | Missing detail |
| --- | --- | --- |
| PR allocation B | Evidence-based; not all PR credit automatically goes to the author. | Experimental V1 counts a qualifying merged PR once for its author when exact PR-commit evidence includes at least one commit by that provider actor. Official allocation remains open. |
| PR author credit A | Credit only with substantive work evidence; creation alone is insufficient. | Experimental V1 uses the exact author-commit condition above; official qualification remains open. |
| PR overlap A | Strict deduplication; work represented by an eligible PR is not also counted as standalone commit for the same contribution. | Experimental V1 deduplicates exact SHA matches; no patch similarity is inferred. |
| Exact overlap D | Prefer higher-quality evidence; if equal, earlier merged PR; unresolved ties go to dispute verification. | Experimental V1 chooses a uniquely earlier `MergedAt` for identical commit sets; tied earliest timestamps exclude all those PRs for review. Dispute verification is not implemented. |
| Partial detection C | Hybrid commit plus patch/content; verifier fallback when confidence is insufficient; same file is not automatically same work. | Similarity method/thresholds: **OPEN BUSINESS DECISION**. |
| Partial attribution B | Cross-PR evidence-based attribution; no default equal split or full duplicate credit. | Ratio/algorithm: **OPEN BUSINESS DECISION**. |
| Force-push before merge C | Use final eligible snapshot; removed work counts only with accepted independent evidence or verification. | Evidence thresholds and practical implementation remain open. |
| Force-push after merge | **OPEN BUSINESS DECISION**. | Do not infer from pre-merge rule. |
| Force-push after Contribution Run | **OPEN BUSINESS DECISION**. | Finalized runs remain immutable; treatment of later runs needs decision. |

Where a future source identifies only an option letter without its original content and user approval, label it **DECISION RECORDED — EXACT SEMANTICS NEED VERIFICATION**, not confirmed. The above option definitions were explicitly supplied by the user; do not relabel those as unknown merely because the older documents are incomplete.

## Evidence / snapshot technical decisions

- EP-01 immutable evidence version plus project association observation; EP-02 retain logical observation history with idempotent replay; EP-03 allowlisted frozen facts and protected retention; EP-04 preserve audit after unlink/AccessLost with private read restrictions; EP-05 legacy grant without provenance is unverified.
- EP-06 observation identity: `(ProjectGitRepositoryId, GitSyncJobId, Category, ScopeKey, GitEvidenceVersionId)`; page/cursor is provenance, not identity. Normalize null/empty scope without changing meaningful branch names.
- Existing snapshot persists exact membership. It does not freeze every observation field, roster or identity history, and does not confer permission to disclose private facts. Use only valid project-scoped observations; never use a shared mutable payload as proof.
- The experimental run freezes roster, identity history, peer inputs, selected observation/version IDs and hashes, eligibility/exclusions, category results, the leader's explicit `ExperimentalGitScoringV1` acknowledgment and a SHA-256 input hash. Repeating create for the same project/snapshot/policy returns the existing immutable run. Run, review and result data are isolated from legacy Contribution v1; final results are served only from a finalized experimental run.
- Current experimental run review/finalize authorization treats the project creator as its leader; delegated project leadership has not been wired into this flow.
- Additive migration `20261114234836_AddGitExperimentalContributionRuns` includes a PostgreSQL trigger preventing update/delete of finalized runs. It was applied and the trigger was exercised on a fresh tmpfs-only disposable PostgreSQL instance; no shared/live database was changed.
- PR-file identity now scopes the path digest by PR number to avoid cross-PR collisions. The validator continues to accept the prior path-only V3 identity for read compatibility; new files use the scoped identity.
