# UNINET — GitHub Contribution handoff

Updated 2026-11-15. Read alongside [decisions](UNINET_GITHUB_CONTRIBUTION_DECISIONS.md) and [next agent](UNINET_GITHUB_CONTRIBUTION_NEXT_AGENT.md). This is a source/worktree handoff, not proof of deployment. Earlier conversation reports are labeled as such.

## Project and architecture

UniNet is a university community platform. The backend uses ASP.NET Core, EF Core and PostgreSQL. GitHub OAuth and GitHub App integration support public/private repositories, webhook-triggered sync and scheduled polling. Git data is intended as evidence for project contribution.

The intended flow is GitHub API → [provider client](../../UniNet.API/Services/GitHubProviderClient.cs) → [worker](../../UniNet.API/Services/GitSyncWorker.cs) → [page processor](../../UniNet.Application/Services/GitHubSyncJobProcessor.cs) → [frozen facts](../../UniNet.Domain/Entities/GitFrozenFacts.cs) → [evidence version](../../UniNet.Domain/Entities/GitEvidenceVersion.cs) → [project observation](../../UniNet.Domain/Entities/ProjectEvidenceObservation.cs) → [project snapshot](../../UniNet.Domain/Entities/ProjectGitEvidenceSnapshot.cs) → historical attribution → isolated Experimental V1 run → leader review/finalization → member results. The experimental flow is implemented in source and focused SQLite tests, but has not been validated against PostgreSQL or live GitHub.

- **DONE — GitHub Integration main Live E2E flows:** earlier reports recorded OAuth/App authorization, link/activation, initial sync, push, redelivery/idempotency, PR and issue webhooks as PASS. Failure/recovery Live outage was **not tested**; the user stopped it. Do not infer deployment or current live health from old reports.
- **DONE — atomic evidence writer / Phase 1B.3:** page fetch precedes the transaction; writes and checkpoint commit together, with lease and App authorization-generation fencing. See processor and [evidence mapping](../../UniNet.Infrastructure/Data/GitEvidenceConfiguration.cs). Existing provider object payload is mutable and is not project-scoped historical evidence.
- **DONE — snapshot / Phase 1B.4:** [capture](../../UniNet.Application/Services/ProjectGitEvidenceSnapshotService.cs) persists exact observation/version membership under Repeatable Read; [reader](../../UniNet.Application/Services/ProjectGitEvidenceSnapshotReader.cs) checks project visibility and local repository authorization and validates selected evidence. Snapshot ID fixes membership. `ObservedAt` is assigned before page commit: a later independently created snapshot with the same cutoff can legitimately include a late-committed observation. Observation metadata beyond frozen IDs is not fully frozen. Read authorization cannot detect a remote GitHub revocation before it is observed locally. No public evidence endpoint exists.
- **DONE — Experimental Git Contribution MVP source flow:** [experimental scoring](../../UniNet.Application/Services/ExperimentalGitScoringV1.cs) calculates per-category normalized scores and 60/40 totals; [run service](../../UniNet.Application/Services/GitExperimentalContributionService.cs) reads a project snapshot, attributes verified actors using frozen roster/identity history, records excluded evidence and reasons, freezes inputs/hash, requires explicit policy acknowledgment, and implements leader review/finalization plus member-only finalized results. It is isolated from legacy [ContributionService](../../UniNet.Application/Services/ContributionService.cs); this is not the official scoring algorithm.
- **API:** `[POST] /api/projects/{projectId}/contribution/git-experimental/runs` calculates or returns the existing snapshot run; `[GET] .../runs/{runId}`, `/review` and `/finalize` currently require the project creator (delegated project leaders are not authorized); `[GET] .../results` serves finalized experimental results to project participants. No raw-evidence endpoint is exposed. These routes were exercised through a disposable PostgreSQL-backed HTTP test host.
- **Run immutability:** the additive [Git Contribution run migration](../../UniNet.Infrastructure/Data/Migrations/20261114234836_AddGitExperimentalContributionRuns.cs) adds isolated run storage and a PostgreSQL trigger preventing update/delete of finalized runs. Migration source/SQL was inspected but has not been applied to any database.

Repository links use [ProjectGitRepository](../../UniNet.Domain/Entities/ProjectGitRepository.cs); project-scoped identity and ownership history use [ProjectGitMemberIdentity](../../UniNet.Domain/Entities/ProjectGitMemberIdentity.cs) and [history](../../UniNet.Domain/Entities/ProjectGitMemberIdentityHistory.cs). Authorization uses [ProjectGitAuthorization](../../UniNet.Application/Services/ProjectGitAuthorization.cs). Business role decisions and current source differ in places: see decisions file.

## Frozen facts and retrieval

[GitFrozenFacts](../../UniNet.Domain/Entities/GitFrozenFacts.cs) supports V1, V2 and V3 under canonicalization version 1: deterministic actor/missing-fact ordering, UTC normalization, SHA-256, and V1/V2 serialization remains unchanged. V3 is limited to per-file PR evidence: path-derived object identity, PR number, file status/counts/blob SHA and patch SHA-256. Patch text is stripped before mutable provider payload persistence; V3 is complete only when required file facts and the patch hash are present. [validator](../../UniNet.Domain/Entities/GitSnapshotEvidenceValidator.cs) validates supported versions, object identity and canonical hash; it does not promote incomplete facts. The writer compares canonical contents for version reuse. V2 [extractor](../../UniNet.Application/Services/GitEvidenceFactsExtractor.cs) captures, when actually returned: commit author/committer dates and provider actor IDs; PR created/updated/closed/merged timestamps, state, merged flag, base/head SHA; review submitted time, state, commit SHA and PR number from retrieval scope; issue created/updated/closed state, PR marker; issue event created time, selected event kind, actor ID and issue number from scope. Complete PR lifecycle facts distinguish merged from unmerged using an explicit `merged` value or a present, valid `merged_at` field; missing merge metadata is not inferred. Open issues do not require a close timestamp; closed issues do. PR commit, PR file and issue-event coverage is represented by their own observations, not as a missing field on the parent record. Missing required facts remain explicit. SHA fields and event/state values are constrained; arbitrary raw payload and secrets are not frozen.

Provider/worker retrieval includes PR commits, PR files, reviews, review comments, issues, issue comments and issue events. These list endpoints use the existing page/checkpoint transaction; a PR-file page adds one request per PR and stores only patch hashes, never patch bodies. An observation's `RetrievalCompleteness` becomes `Complete` only after that endpoint scope reaches its final page; unfinished or failed scopes remain `Incomplete`. This is independent of per-version `EvidenceStatus`, which follows the frozen facts' required-field completeness. Branch scope is retained in observation/branch-reference structures, not an immutable proof of current reachability. Do not infer ownership from login, timestamps from other timestamps, or unobserved PR/patch relationships. GitHub PR commit/file-list limits, permission/rate-limit effects, unrecognized issue-event kinds and force-push semantics need direct validation.

## Blockers and next work

**PostgreSQL/API validation:** a fresh PostgreSQL 17 container with tmpfs-only storage, loopback-only random port `62563`, and database `uninet_git_mvp_disposable` was used; it was not E2E, Neon, or production. `MigrateAsync()` applied all 82 migrations including the Git Contribution migration. A focused HTTP integration test used actual PostgreSQL evidence versions, project observations, snapshot capture/reader, verified identities, peer inputs, and the API endpoints. Create/idempotent retry, score breakdown, leader review/finalize, member results, creator/leader/member/outsider/anonymous permissions, and the finalized-run trigger all passed. In the current policy, a project member whose role is `Leader` is not authorized to manage the run; only the project creator is. Live GitHub evidence and unobserved remote authorization revocation remain untested.

**Policy boundary:** `ExperimentalGitScoringV1` is temporary and not the official algorithm. Exact SHA overlap is deduplicated; partial PR overlap is excluded as unsupported; exact merged-time ties are excluded for review rather than arbitrarily selecting a PR. Patch-level overlap, force-push treatment, official eligibility/raw scoring and official midpoint rounding remain open. See [decisions](UNINET_GITHUB_CONTRIBUTION_DECISIONS.md).

**Do not reopen completed work:** do not redo GitHub integration, atomic writer, snapshot, or Experimental V1 run implementation. Do not mark source-level/focused-test completion as production readiness.

## Critical invariants

- Never infer verified Git identity from login/display name or assign unknown work to the creator/leader.
- Verified historical attribution uses the verified numeric provider account ID and project-scoped ownership history; do not require activity to be after LinkedAt or rewrite ownership timestamps. Reject ambiguous multi-owner history.
- Experimental alternate rosters are frozen per run and remain draft-only unless their participant IDs exactly match the immutable Contribution Cycle roster. Do not reuse legacy peer evaluations for a different cohort, and reject idempotency hits whose scoring-input hash differs.
- Do not substitute AuthorDate, ObservedAt or project dates for missing CommitterDate.
- Do not reconstruct project history from mutable shared provider payload; never bypass atomic evidence writer.
- Never update/delete immutable evidence versions, silently promote incomplete evidence, invent facts, or represent Experimental V1 as an official result.
- Enforce project association and authorization, including private evidence; a shared repository does not confer observations across projects.
- Preserve immutable finalized Contribution history; do not recalculate v1 as Git scoring.
- Observation cutoff and event timestamp are different; preserve distinct source timestamps and provenance.

## Verification

Previous report: **185 PASS / 0 FAIL / 1 SKIP** on disposable PostgreSQL; build and EF model consistency passed. Earlier focused evidence tests reported **20 PASS / 0 FAIL**. These historical results do not prove production migration, deployment, or live GitHub sync.

Current Experimental MVP regression: **54 PASS / 0 FAIL / 0 SKIP**, including scoring/tie behavior, service lifecycle/authorization, PostgreSQL HTTP flow, evidence extraction/attribution/validation, formula, and legacy Contribution calculator/service tests. The PostgreSQL test applied all **82 migrations**, including `20261114234836_AddGitExperimentalContributionRuns`, and exercised its immutability trigger. Build/test outputs were directed to a temporary artifacts directory because the existing API process held default build outputs. Earlier solution build passed with one existing `CS9113` warning in `ProjectsController.cs`; `git diff --check` passed. Only the newly created isolated tmpfs disposable database was migrated and written; it was stopped and removed after verification. No shared/live database was changed.

## CURRENT WORKTREE STATUS

The worktree remains dirty with tracked modifications and untracked evidence-pipeline, snapshot, contribution, test and handoff files. Preserve all of them. The current task added one PostgreSQL HTTP integration test and updated handoff status; it did not commit, push, migrate a shared/live DB, run live sync, or restart the backend. Re-run `git status --short --untracked-files=all` before any future edit; this handoff is not a clean-tree claim.

Tracked modified:

```text
UniNet.API/Services/GitHubProviderClient.cs
UniNet.API/Services/GitSyncWorker.cs
UniNet.Application/Services/GitHubSyncJobProcessor.cs
UniNet.Domain/Entities/GitHubAppInstallation.cs
UniNet.Infrastructure/Data/GitHubAppInstallationConfiguration.cs
UniNet.Infrastructure/Data/Migrations/UniNetDbContextModelSnapshot.cs
UniNet.Infrastructure/Data/UniNetDbContext.cs
UniNet.Tests/GitHubAppActivityWebhookPostgresTests.cs
UniNet.Tests/GitHubIntegrationServiceTests.cs
```

Untracked before this handoff:

```text
UniNet.Application/Properties/SnapshotTestVisibility.cs
UniNet.Application/Services/GitContributionFormula.cs
UniNet.Application/Services/GitContributionReadinessService.cs
UniNet.Application/Services/GitEvidenceFactsExtractor.cs
UniNet.Application/Services/GitHistoricalAttributionService.cs
UniNet.Application/Services/ProjectGitEvidenceSnapshotReader.cs
UniNet.Application/Services/ProjectGitEvidenceSnapshotService.cs
UniNet.Domain/Entities/GitEvidenceVersion.cs
UniNet.Domain/Entities/GitFrozenFacts.cs
UniNet.Domain/Entities/GitSnapshotEvidenceValidator.cs
UniNet.Domain/Entities/ProjectEvidenceObservation.cs
UniNet.Domain/Entities/ProjectGitEvidenceSnapshot.cs
UniNet.Infrastructure/Data/GitEvidenceConfiguration.cs
UniNet.Infrastructure/Data/Migrations/20261113165539_AddGitEvidenceVersioning.Designer.cs
UniNet.Infrastructure/Data/Migrations/20261113165539_AddGitEvidenceVersioning.cs
UniNet.Infrastructure/Data/Migrations/20261113232726_HardenGitEvidenceCrossRepositoryIntegrity.Designer.cs
UniNet.Infrastructure/Data/Migrations/20261113232726_HardenGitEvidenceCrossRepositoryIntegrity.cs
UniNet.Infrastructure/Data/Migrations/20261114070301_AddAppAuthorizationGeneration.Designer.cs
UniNet.Infrastructure/Data/Migrations/20261114070301_AddAppAuthorizationGeneration.cs
UniNet.Infrastructure/Data/Migrations/20261114151603_AddProjectGitEvidenceSnapshots.Designer.cs
UniNet.Infrastructure/Data/Migrations/20261114151603_AddProjectGitEvidenceSnapshots.cs
UniNet.Infrastructure/Data/ProjectGitEvidenceSnapshotConfiguration.cs
UniNet.Tests/GitContributionFormulaTests.cs
UniNet.Tests/GitEvidenceAcceptancePostgresTests.cs
UniNet.Tests/GitEvidenceFactsExtractorTests.cs
UniNet.Tests/GitFrozenFactsTests.cs
UniNet.Tests/GitHistoricalAttributionTests.cs
UniNet.Tests/GitSnapshotEvidenceValidatorTests.cs
UniNet.Tests/ProjectGitEvidenceSnapshotPostgresTests.cs
```

The four migration pairs above are untracked source, not evidence that any live database has been migrated. This handoff adds three additional untracked Markdown files. Do not reset, stash, overwrite or delete unrelated work.
