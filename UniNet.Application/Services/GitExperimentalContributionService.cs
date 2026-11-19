using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

internal interface IGitContributionSnapshotAccess
{
    Task<Guid> CaptureAsync(Guid projectId, Guid accountId, DateTimeOffset cutoff, CancellationToken ct);
    Task<ProjectGitEvidenceSnapshotRead> ReadAsync(Guid projectId, Guid snapshotId, Guid accountId, CancellationToken ct);
}

internal sealed class ProjectGitContributionSnapshotAccess(
    ProjectGitEvidenceSnapshotService capture,
    ProjectGitEvidenceSnapshotReader reader) : IGitContributionSnapshotAccess
{
    public Task<Guid> CaptureAsync(Guid projectId, Guid accountId, DateTimeOffset cutoff, CancellationToken ct) =>
        capture.CaptureAsync(projectId, accountId, cutoff, ct);

    public Task<ProjectGitEvidenceSnapshotRead> ReadAsync(Guid projectId, Guid snapshotId, Guid accountId, CancellationToken ct) =>
        reader.ReadAsync(projectId, snapshotId, accountId, ct);
}

internal sealed partial class GitExperimentalContributionService(
    UniNetDbContext db,
    IGitContributionSnapshotAccess snapshots,
    TimeProvider timeProvider) : IGitExperimentalContributionService
{
    private const string PolicyVersion = "ExperimentalGitScoringV1";
    private const int MaxFeedbackLength = 2000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GitExperimentalRunResponse> CreateRunAsync(Guid projectId, Guid accountId, GitExperimentalRunRequest request, CancellationToken ct)
    {
        try { return await CreateRunCoreAsync(projectId, accountId, request, ct); }
        catch (Exception ex) when (request.ScoringPolicyVersion == ExperimentalGitScoringV2.PolicyVersion && ex.GetBaseException() is PostgresException { SqlState: "40001" or "40P01" or "23505" })
        { throw Error("GIT_CONTRIBUTION_RETRY_REQUIRED", "Retry the request with a fresh transaction.", 409); }
    }

    private async Task<GitExperimentalRunResponse> CreateRunCoreAsync(
        Guid projectId, Guid accountId, GitExperimentalRunRequest request, CancellationToken ct, bool official = false)
    {
        if (!request.AcknowledgeExperimentalPolicy)
            throw Error("GIT_EXPERIMENTAL_POLICY_ACK_REQUIRED",
                "Explicitly acknowledge ExperimentalGitScoringV1 before calculating a run.");
        Require(request.ScoringPolicyVersion is PolicyVersion or ExperimentalGitScoringV2.PolicyVersion,
            "GIT_CONTRIBUTION_POLICY_UNSUPPORTED", "Select a supported experimental policy.");
        var isV2 = request.ScoringPolicyVersion == ExperimentalGitScoringV2.PolicyVersion;
        Require(isV2 || request.PeerSnapshotId is null, "GIT_CONTRIBUTION_PEER_POLICY_MISMATCH", "Peer cohorts require policy V2.");
        Require(!isV2 || request.PeerSnapshotId is not null, "PEER_SNAPSHOT_REQUIRED",
            "Close a complete peer cohort before creating a V2 run; missing evaluations are not zero ratings.", 409);

        var actor = await GetActiveStudentAsync(accountId, ct);
        var project = await GetProjectAsync(projectId, ct);
        RequireLeader(project, actor.Id);
        Require(project.Status == ProjectStatus.Completed,
            "GIT_CONTRIBUTION_PROJECT_NOT_COMPLETED", "Git Contribution is available only for completed projects.", 409);

        var now = timeProvider.GetUtcNow();
        var snapshotId = request.SnapshotId ??
            await snapshots.CaptureAsync(projectId, accountId, now, ct);
        var snapshot = await snapshots.ReadAsync(projectId, snapshotId, accountId, ct);
        Require(snapshot.ProjectId == projectId,
            "GIT_CONTRIBUTION_SNAPSHOT_PROJECT_MISMATCH", "The evidence snapshot does not belong to this project.", 409);

        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var cycle = await db.ContributionCycles.AsNoTracking()
            .Include(x => x.Participants)
            .SingleOrDefaultAsync(x => x.ProjectId == projectId, ct);
        Require(official || cycle is not null, "CONTRIBUTION_CYCLE_NOT_FOUND", "The project has no contribution roster.", 409);
        var cycleParticipants = official ? [] : cycle!.Participants.OrderBy(x => x.UserId).ToArray();
        ExperimentalPeerSnapshot? peerSnapshot = null;
        Guid[]? requestedRoster = request.ParticipantUserIds?.ToArray();
        if (isV2 && request.PeerSnapshotId is { } peerId)
        {
            peerSnapshot = await db.Set<ExperimentalPeerSnapshot>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == peerId && x.ProjectId == projectId, ct)
                ?? throw Error("PEER_SNAPSHOT_NOT_FOUND", "A closed peer snapshot in this project is required.", 409);
            Require(ContributionCanonicalJson.Hash(peerSnapshot.InputJson) == peerSnapshot.InputHash,
                "PEER_SNAPSHOT_INTEGRITY_ERROR", "Frozen peer inputs do not match their hash.", 409);
            var cohort = await db.Set<ExperimentalPeerCohort>().AsNoTracking().SingleAsync(x => x.Id == peerSnapshot.CohortId && x.ProjectId == projectId, ct);
            Require(cohort.Status == ExperimentalPeerCohortStatus.Closed, "PEER_COHORT_NOT_CLOSED", "Close the peer cohort before calculating with it.", 409);
            var cohortRoster = ExperimentalPeerCohortService.Roster(cohort);
            Require(requestedRoster is null || requestedRoster.Append(project.CreatorId).ToHashSet().SetEquals(cohortRoster), "GIT_CONTRIBUTION_PEER_ROSTER_MISMATCH", "The run roster must match the frozen peer roster.", 409);
            requestedRoster = cohortRoster;
        }
        if (isV2 && requestedRoster is null) requestedRoster = await ProjectMembership.EffectiveMemberIds(db, projectId).ToArrayAsync(ct);
        var participants = requestedRoster is null
            ? cycleParticipants
            : await ResolveRequestedRosterAsync(project, requestedRoster, snapshot.ObservationCutoff, ct);
        var rosterMatchesCycle = participants.Select(x => x.UserId).Order()
            .SequenceEqual(cycleParticipants.Select(x => x.UserId).Order());
        var rosterSource = rosterMatchesCycle ? "contributionCycle" : "effectiveProjectMembership";
        if (rosterMatchesCycle)
            participants = cycleParticipants;
        var roster = participants.Select(x => x.UserId).ToArray();
        Require(roster.Length > 0, "CONTRIBUTION_ROSTER_EMPTY", "The contribution roster is empty.", 409);

        var peerInputs = !isV2 && rosterMatchesCycle
            ? await db.ContributionPeerEvaluations.AsNoTracking()
                .Where(x => x.CycleId == cycle!.Id)
                .Select(x => new ContributionPeerEvaluationInput(x.EvaluatorUserId, x.EvaluateeUserId, x.Score))
                .ToArrayAsync(ct)
            : [];
        var peerScores = !isV2 && rosterMatchesCycle
            ? ContributionCalculator.Calculate(roster, [], [], peerInputs).Results
                .ToDictionary(x => x.UserId, x => x.PeerEvaluationScore)
            : roster.ToDictionary(x => x, _ => 0m);
        if (peerSnapshot is not null)
        {
            using var peerDocument = JsonDocument.Parse(peerSnapshot.InputJson);
            var frozenPeerRoster = peerDocument.RootElement.GetProperty("roster").EnumerateArray().Select(x => x.GetGuid()).ToArray();
            Require(frozenPeerRoster.Length == roster.Length && frozenPeerRoster.ToHashSet().SetEquals(roster),
                "PEER_SNAPSHOT_INTEGRITY_ERROR", "Frozen peer inputs have a different participant roster.", 409);
            peerInputs = peerDocument.RootElement.GetProperty("ratings").EnumerateArray()
                .Select(x => new ContributionPeerEvaluationInput(x.GetProperty("evaluatorId").GetGuid(), x.GetProperty("evaluateeId").GetGuid(), x.GetProperty("score").GetInt32())).ToArray();
            peerScores = ContributionCalculator.Calculate(roster, [], [], peerInputs).Results.ToDictionary(x => x.UserId, x => x.PeerEvaluationScore);
        }

        var identityHistory = await db.ProjectGitMemberIdentityHistory.AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.OccurredAt <= snapshot.ObservationCutoff)
            .OrderBy(x => x.OccurredAt).ThenBy(x => x.IdentityId).ThenBy(x => x.Id)
            .ToListAsync(ct);
        var exclusions = new List<GitExperimentalExcludedActivityResponse>();
        var selectedEntries = SelectLatestEvidence(snapshot.Entries, exclusions);
        if (isV2) selectedEntries = ExperimentalGitScoringV2.Filter(selectedEntries, exclusions);
        var evidenceActivities = BuildActivities(projectId, snapshot, selectedEntries, roster, identityHistory, exclusions);
        if (isV2)
        {
            var inTimeline = ExperimentalGitScoringV2.ApplyCommitTimeline(evidenceActivities.Select(x => x.Activity).ToArray(),
                selectedEntries, snapshot.ObservationCutoff, exclusions);
            evidenceActivities = evidenceActivities.Where(x => inTimeline.Contains(x.Activity)).ToArray();
        }
        var effectiveMembers = isV2 ? (await ProjectMembership.EffectiveMemberIds(db, projectId).ToArrayAsync(ct)).Order().ToArray() : null;
        if (isV2)
        {
            evidenceActivities = evidenceActivities.Where(work =>
            {
                if (work.Activity.UserId is not null || !identityHistory.Any(h => h.ExternalAccountId == work.Input.ProviderActorId && effectiveMembers!.Contains(h.UserProfileId))) return true;
                exclusions.Add(new(CategoryName(work.Activity.Category), work.Activity.RepositoryId, work.Activity.ObjectId,
                    "Unattributed", "VerifiedMemberOutsideCohort"));
                return false;
            }).ToArray();
        }
        var scoring = ExperimentalGitScoringV1.Calculate(roster, evidenceActivities.Select(x => x.Activity).ToArray(), peerScores);
        exclusions.AddRange(scoring.ExcludedActivities.Select(x => new GitExperimentalExcludedActivityResponse(
            CategoryName(x.Category), x.RepositoryId, x.ObjectId, PolicyExclusionClassification(x.Reason),
            isV2 && x.Reason == "CountedUnderMergedPullRequest" ? "AlreadyRepresentedByPullRequest" : x.Reason)));

        var categories = BuildCategoryStatuses(isV2 ? selectedEntries : snapshot.Entries, snapshot.RestrictedCount,
            scoring.EligibleActivityCounts, scoring.ExternalActivityCounts, exclusions);
        if (isV2)
            categories = ExperimentalGitScoringV2.ApplyReadiness(categories, selectedEntries, snapshot.RestrictedCount, exclusions,
                GitSnapshotCoverageValidation.HasVerifiedEmptyReviewCoverage(snapshot));
        // No selected activities is not proof that Git is truly NotApplicable: the
        // snapshot currently has no frozen empty-scope coverage receipt.
        var gitAbsenceUnproven = isV2 && scoring.EligibleActivityCounts.Values.Sum() == 0;
        if (gitAbsenceUnproven)
            categories = categories.Select(x => x with { Status = "Incomplete",
                Limitations = x.Limitations.Append("GitAbsenceNotProven").Distinct(StringComparer.Ordinal).ToArray() }).ToArray();
        var v2EvidenceGap = isV2 && (gitAbsenceUnproven || ExperimentalGitScoringV2.HasEvidenceGap(
            categories, selectedEntries, snapshot.RestrictedCount, exclusions));
        var gitComponentState = v2EvidenceGap ? ScoreState.NotReady :
            scoring.EligibleActivityCounts.Values.Sum() == 0 ? ScoreState.NotApplicable : ScoreState.Ready;
        var peerComponentState = peerSnapshot is null ? ScoreState.NotReady : roster.Length == 1 ? ScoreState.NotApplicable : ScoreState.Ready;
        var normalization = isV2 ? GitPeerNormalization.Calculate(scoring.UnroundedGitWeights, scoring.ExternalGitWeight,
            gitComponentState, peerScores, peerComponentState) : null;
        var calculationStatus = normalization is null ? null : v2EvidenceGap ? "IncompleteGitEvidence" : normalization.Status;
        var activityInputs = evidenceActivities.Select(x => x.Input)
            .OrderBy(x => x.Category, StringComparer.Ordinal).ThenBy(x => x.RepositoryId).ThenBy(x => x.ObjectId, StringComparer.Ordinal)
            .ToArray();
        var frozenInput = new
        {
            PolicyVersion = request.ScoringPolicyVersion,
            ProjectId = projectId,
            snapshot.SnapshotId,
            snapshot.ObservationCutoff,
            snapshot.RestrictedCount,
            PolicyAcknowledgment = new { AcknowledgedBy = actor.Id, AcknowledgedAt = now, PolicyVersion = request.ScoringPolicyVersion },
            PeerSnapshot = peerSnapshot is null ? null : new { peerSnapshot.Id, peerSnapshot.InputHash, peerSnapshot.PolicyVersion },
            Coverage = isV2 ? snapshot.Coverage : null,
            EffectiveMembers = effectiveMembers,
            NormalizationPolicy = isV2 ? GitPeerNormalization.Version : null,
            NormalizationInputs = isV2 ? new {
                GitState = gitComponentState.ToString(), PeerState = peerComponentState.ToString(),
                GitWeights = scoring.UnroundedGitWeights.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value),
                scoring.ExternalGitWeight, PeerScores = peerScores.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value)
            } : null,
            Normalization = normalization,
            SnapshotPolicyVersion = snapshot.PolicyVersion,
            RosterSource = rosterSource,
            RosterEligibilityEvaluatedAt = now,
            Roster = participants.Select(x => new
            {
                x.UserId, x.RoleAtSnapshot, x.MembershipStatusAtSnapshot, x.JoinedAt, x.LeftAt, x.IsLeader
            }).ToArray(),
            IdentityHistory = identityHistory.Select(x => new
            {
                x.Id, x.IdentityId, x.UserProfileId, x.Provider, x.ExternalAccountId, x.OccurredAt, x.NewStatus, x.VerifiedByConnectionId
            }).ToArray(),
            PeerEvaluations = peerInputs.OrderBy(x => x.EvaluatorUserId).ThenBy(x => x.EvaluateeUserId).ToArray(),
            Evidence = snapshot.Entries.OrderBy(x => x.RepositoryId).ThenBy(x => x.Category, StringComparer.Ordinal)
                .ThenBy(x => x.ScopeKey, StringComparer.Ordinal).ThenBy(x => x.ExternalObjectId, StringComparer.Ordinal)
                .ThenBy(x => x.ObservedAt).Select(x => new
                {
                    x.ObservationId, x.VersionId, x.ContentHash, x.RepositoryId, x.Category, x.ScopeKey,
                    x.ExternalObjectId, x.ObservedAt, Status = x.Status.ToString(), x.Reason
                }).ToArray(),
            EligibleActivities = activityInputs,
            ExcludedActivities = exclusions.OrderBy(x => x.Category, StringComparer.Ordinal)
                .ThenBy(x => x.RepositoryId).ThenBy(x => x.ObjectId, StringComparer.Ordinal)
                .ThenBy(x => x.Reason, StringComparer.Ordinal).ToArray(),
            CategoryStatuses = categories,
            Results = scoring.Results.OrderBy(x => x.UserId).ToArray()
        };
        var inputSnapshotJson = JsonSerializer.Serialize(frozenInput, JsonOptions);
        var inputHash = ComputeScoringInputHash(inputSnapshotJson);

        var contextHash = isV2 ? inputHash : null;
        var existing = await db.GitContributionRuns.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && (isV2
                ? x.CalculationContextHash == contextHash
                : x.GitEvidenceSnapshotId == snapshotId && x.ScoringPolicyVersion == PolicyVersion), ct);
        if (existing is not null)
        {
            Require(!official || await db.Set<OfficialContributionRun>().AnyAsync(x => x.Id == existing.Id, ct),
                "EXPERIMENTAL_RUN_NOT_OFFICIAL", "Existing experimental runs cannot be promoted implicitly.", 409);
            Require(ComputeScoringInputHash(existing.InputSnapshotJson) == inputHash,
                "GIT_CONTRIBUTION_INPUT_MISMATCH",
                "A run already exists for this snapshot with different frozen scoring inputs.", 409);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return await MapRunAsync(existing, snapshot.Entries.Select(x => x.RepositoryId).ToHashSet(), ct);
        }

        var runNumber = (await db.GitContributionRuns.AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => (int?)x.RunNumber).MaxAsync(ct) ?? 0) + 1;
        var run = new GitContributionRun
        {
            ProjectId = projectId,
            GitEvidenceSnapshotId = snapshotId,
            RunNumber = runNumber,
            ScoringPolicyVersion = request.ScoringPolicyVersion,
            PeerSnapshotId = peerSnapshot?.Id,
            CalculationContextHash = contextHash,
            CalculationStatus = calculationStatus,
            NormalizationPolicyVersion = isV2 ? GitPeerNormalization.Version : null,
            NormalizedResultsJson = normalization is null ? null : JsonSerializer.Serialize(normalization, JsonOptions),
            InputSnapshotJson = inputSnapshotJson,
            InputHash = inputHash,
            CategoryStatusJson = JsonSerializer.Serialize(categories, JsonOptions),
            ResultsJson = JsonSerializer.Serialize(scoring.Results.OrderBy(x => x.UserId), JsonOptions),
            ExcludedActivitiesJson = JsonSerializer.Serialize(exclusions, JsonOptions),
            ReviewHistoryJson = "[]",
            Status = GitContributionRunStatus.Draft,
            CalculatedById = actor.Id,
            CalculatedAt = now
        };
        db.GitContributionRuns.Add(run);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return await MapRunAsync(run, snapshot.Entries.Select(x => x.RepositoryId).ToHashSet(), ct);
    }

    public async Task<GitExperimentalRunResponse> GetRunAsync(Guid projectId, Guid runId, Guid accountId, CancellationToken ct)
    {
        var actor = await GetActiveStudentAsync(accountId, ct);
        var project = await GetProjectAsync(projectId, ct);
        RequireLeader(project, actor.Id);
        var run = await GetRunEntityAsync(projectId, runId, ct);
        var readableSnapshot = await snapshots.ReadAsync(projectId, run.GitEvidenceSnapshotId, accountId, ct);
        return await MapRunAsync(run, readableSnapshot.Entries.Select(x => x.RepositoryId).ToHashSet(), ct);
    }

    public async Task<GitExperimentalRunResponse> ReviewRunAsync(
        Guid projectId, Guid runId, Guid accountId, GitExperimentalReviewRequest request, CancellationToken ct)
    {
        var actor = await GetActiveStudentAsync(accountId, ct);
        var project = await GetProjectAsync(projectId, ct);
        RequireLeader(project, actor.Id);
        var run = await GetRunEntityAsync(projectId, runId, ct);
        await RequireExperimentalLifecycleAsync(runId, ct);
        await RequireOfficialRosterAsync(projectId, run.InputSnapshotJson, ct);
        Require(run.ScoringPolicyVersion == PolicyVersion || run.CalculationStatus == "Ready",
            "GIT_CONTRIBUTION_NOT_READY", "Complete calculation inputs before review.", 409);
        var readableSnapshot = await snapshots.ReadAsync(projectId, run.GitEvidenceSnapshotId, accountId, ct);
        Require(run.Status == GitContributionRunStatus.Draft,
            "GIT_CONTRIBUTION_RUN_NOT_REVIEWABLE", "Only a draft experimental run can be reviewed.", 409);

        var decision = request.Decision?.Trim().ToLowerInvariant() switch
        {
            "approve" => GitContributionReviewDecision.Approve,
            "request_revision" => GitContributionReviewDecision.RequestRevision,
            _ => throw Error("GIT_CONTRIBUTION_REVIEW_DECISION_INVALID", "Decision must be approve or request_revision.")
        };
        var feedback = string.IsNullOrWhiteSpace(request.Feedback) ? null : request.Feedback.Trim();
        Require(feedback is null || feedback.Length <= MaxFeedbackLength,
            "GIT_CONTRIBUTION_REVIEW_FEEDBACK_INVALID", "Review feedback cannot exceed 2000 characters.");
        Require(decision != GitContributionReviewDecision.RequestRevision || feedback is not null,
            "GIT_CONTRIBUTION_REVIEW_FEEDBACK_REQUIRED", "Feedback is required when requesting a revision.");

        var reviewedAt = timeProvider.GetUtcNow();
        var history = Deserialize<List<GitExperimentalReviewHistoryEntry>>(run.ReviewHistoryJson);
        history.Add(new(decision.ToString(), feedback, actor.Id, reviewedAt));
        run.ReviewHistoryJson = JsonSerializer.Serialize(history, JsonOptions);
        run.Status = decision == GitContributionReviewDecision.Approve
            ? GitContributionRunStatus.Review
            : GitContributionRunStatus.RevisionRequested;
        run.ReviewedById = actor.Id;
        run.ReviewedAt = reviewedAt;
        run.ReviewFeedback = feedback;
        await db.SaveChangesAsync(ct);
        return await MapRunAsync(run, readableSnapshot.Entries.Select(x => x.RepositoryId).ToHashSet(), ct);
    }

    public async Task<GitExperimentalRunResponse> FinalizeRunAsync(Guid projectId, Guid runId, Guid accountId, CancellationToken ct)
    {
        var actor = await GetActiveStudentAsync(accountId, ct);
        var project = await GetProjectAsync(projectId, ct);
        RequireLeader(project, actor.Id);
        var run = await GetRunEntityAsync(projectId, runId, ct);
        await RequireExperimentalLifecycleAsync(runId, ct);
        Require(run.ScoringPolicyVersion == PolicyVersion || run.CalculationStatus == "Ready",
            "GIT_CONTRIBUTION_NOT_READY", "Complete all calculation inputs before finalizing.", 409);
        await RequireOfficialRosterAsync(projectId, run.InputSnapshotJson, ct);
        var readableSnapshot = await snapshots.ReadAsync(projectId, run.GitEvidenceSnapshotId, accountId, ct);
        Require(run.Status == GitContributionRunStatus.Review && run.ReviewedById == actor.Id,
            "GIT_CONTRIBUTION_RUN_NOT_FINALIZABLE", "The leader must approve the draft before finalizing it.", 409);
        run.Status = GitContributionRunStatus.Finalized;
        run.FinalizedById = actor.Id;
        run.FinalizedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return await MapRunAsync(run, readableSnapshot.Entries.Select(x => x.RepositoryId).ToHashSet(), ct);
    }

    public async Task<GitExperimentalFinalResultsResponse> GetFinalResultsAsync(Guid projectId, Guid accountId, CancellationToken ct)
    {
        var actor = await GetActiveStudentAsync(accountId, ct);
        var cycle = await db.ContributionCycles.AsNoTracking()
            .Include(x => x.Participants)
            .SingleOrDefaultAsync(x => x.ProjectId == projectId, ct)
            ?? throw Error("CONTRIBUTION_CYCLE_NOT_FOUND", "The project has no contribution roster.", 409);
        Require(cycle.Participants.Any(x => x.UserId == actor.Id),
            "FORBIDDEN", "Only contribution participants can view experimental results.", 403);
        var run = await db.GitContributionRuns.AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.Status == GitContributionRunStatus.Finalized)
            .OrderByDescending(x => x.RunNumber)
            .FirstOrDefaultAsync(ct)
            ?? throw Error("GIT_CONTRIBUTION_RESULTS_NOT_FOUND", "No finalized experimental Git result is available.", 404);
        var members = ApplyNormalization(run, await MapMembersAsync(run.ResultsJson, ct));
        var allocation = run.NormalizedResultsJson is null ? null : Deserialize<ContributionAllocation>(run.NormalizedResultsJson);
        return new(run.Id, run.GitEvidenceSnapshotId, run.ScoringPolicyVersion, run.FinalizedAt!.Value, members,
            run.NormalizationPolicyVersion, run.CalculationStatus, allocation?.ExternalPercent);
    }

    private async Task<IReadOnlyList<GitExperimentalMemberResult>> MapMembersAsync(
        string resultsJson, CancellationToken ct)
    {
        var scores = Deserialize<ExperimentalGitMemberScore[]>(resultsJson);
        var userIds = scores.Select(x => x.UserId).ToArray();
        var names = await db.UserProfiles.AsNoTracking().Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        Require(names.Count == userIds.Distinct().Count(),
            "GIT_CONTRIBUTION_RESULT_PROFILE_MISSING", "A frozen contribution participant profile is missing.", 409);
        return scores.OrderBy(x => x.UserId).Select(x => new GitExperimentalMemberResult(
            x.UserId,
            names[x.UserId],
            x.CommitScore,
            x.PullRequestScore,
            x.ReviewScore,
            x.IssueScore,
            x.GitScore,
            x.PeerScore,
            x.TotalScore)).ToArray();
    }

    private async Task<GitExperimentalRunResponse> MapRunAsync(
        GitContributionRun run, IReadOnlySet<Guid>? visibleRepositoryIds, CancellationToken ct)
    {
        var allocation = run.NormalizedResultsJson is null ? null : Deserialize<ContributionAllocation>(run.NormalizedResultsJson);
        return new(
            run.Id,
            run.ProjectId,
            run.GitEvidenceSnapshotId,
            run.RunNumber,
            run.ScoringPolicyVersion,
            run.InputHash,
            run.Status,
            run.CalculatedAt,
            run.FinalizedAt,
            Deserialize<GitExperimentalCategoryStatus[]>(run.CategoryStatusJson),
            ApplyNormalization(run, await MapMembersAsync(run.ResultsJson, ct)),
            Deserialize<GitExperimentalExcludedActivityResponse[]>(run.ExcludedActivitiesJson)
                .Where(x => visibleRepositoryIds is null || visibleRepositoryIds.Contains(x.RepositoryId)).ToArray(),
            Deserialize<GitExperimentalReviewHistoryEntry[]>(run.ReviewHistoryJson),
            run.PeerSnapshotId, run.NormalizationPolicyVersion, run.CalculationStatus, allocation?.ExternalPercent);
    }

    private static IReadOnlyList<GitExperimentalMemberResult> ApplyNormalization(GitContributionRun run, IReadOnlyList<GitExperimentalMemberResult> members)
    {
        if (run.ScoringPolicyVersion == PolicyVersion) return members;
        var allocation = run.NormalizedResultsJson is null ? null : Deserialize<ContributionAllocation>(run.NormalizedResultsJson);
        return members.Select(x => x with { RawTotalScore = x.TotalScore,
            ContributionPercent = allocation?.Status == "Ready" && allocation.Members.TryGetValue(x.UserId, out var percent) ? percent : null }).ToArray();
    }

    private async Task<GitContributionRun> GetRunEntityAsync(Guid projectId, Guid runId, CancellationToken ct) =>
        await db.GitContributionRuns.SingleOrDefaultAsync(x => x.Id == runId && x.ProjectId == projectId, ct)
        ?? throw Error("GIT_CONTRIBUTION_RUN_NOT_FOUND", "Git Contribution run not found.", 404);

    private async Task<UserProfile> GetActiveStudentAsync(Guid accountId, CancellationToken ct)
    {
        var actor = await db.UserProfiles.AsNoTracking().Include(x => x.Account)
            .SingleOrDefaultAsync(x => x.AccountId == accountId, ct);
        Require(actor is not null && actor.Account.Role == AccountRole.Student && actor.Account.Status == AccountStatus.Active,
            "FORBIDDEN", "An active Student account is required.", 403);
        return actor!;
    }

    private async Task<ContributionParticipant[]> ResolveRequestedRosterAsync(
        ContributionProject project, IReadOnlyList<Guid> requestedUserIds, DateTimeOffset snapshotCutoff, CancellationToken ct)
    {
        Require(!requestedUserIds.Contains(Guid.Empty),
            "GIT_CONTRIBUTION_ROSTER_INVALID", "The requested contribution roster contains an invalid user.", 409);
        Require(project.CreatedAt <= snapshotCutoff,
            "GIT_CONTRIBUTION_ROSTER_INVALID", "The project did not exist at the evidence snapshot cutoff.", 409);

        var userIds = requestedUserIds.Append(project.CreatorId).Distinct().Order().ToArray();
        var effectiveIds = await ProjectMembership.EffectiveMemberIds(db, project.Id)
            .Where(id => userIds.Contains(id))
            .ToArrayAsync(ct);
        Require(effectiveIds.Length == userIds.Length,
            "GIT_CONTRIBUTION_ROSTER_INVALID", "Every requested participant must be an active project member.", 409);

        var activeStudentIds = await db.UserProfiles.AsNoTracking()
            .Where(x => userIds.Contains(x.Id) &&
                x.Account.Role == AccountRole.Student && x.Account.Status == AccountStatus.Active)
            .Select(x => x.Id)
            .ToArrayAsync(ct);
        Require(activeStudentIds.Length == userIds.Length,
            "GIT_CONTRIBUTION_ROSTER_INVALID", "Every requested participant must have an active Student account.", 409);

        var memberIds = userIds.Where(id => id != project.CreatorId).ToArray();
        var memberships = (await db.ProjectMembers.AsNoTracking()
                .Where(x => x.ProjectId == project.Id && memberIds.Contains(x.UserId) &&
                    x.Status == ProjectMemberStatus.Active)
                .ToListAsync(ct))
            .Where(x => x.JoinedAt <= snapshotCutoff && (x.LeftAt is null || x.LeftAt > snapshotCutoff))
            .ToDictionary(x => x.UserId);
        Require(memberships.Count == memberIds.Length,
            "GIT_CONTRIBUTION_ROSTER_INVALID",
            "Every requested member must have joined by the evidence snapshot cutoff.", 409);

        return userIds.Select(id => id == project.CreatorId
            ? new ContributionParticipant
            {
                UserId = id,
                RoleAtSnapshot = "Creator",
                MembershipStatusAtSnapshot = ProjectMemberStatus.Active,
                JoinedAt = project.CreatedAt,
                IsLeader = true
            }
            : new ContributionParticipant
            {
                UserId = id,
                RoleAtSnapshot = memberships[id].Role,
                MembershipStatusAtSnapshot = memberships[id].Status,
                JoinedAt = memberships[id].JoinedAt,
                LeftAt = memberships[id].LeftAt,
                IsLeader = false
            }).ToArray();
    }

    private async Task RequireOfficialRosterAsync(Guid projectId, string inputSnapshotJson, CancellationToken ct)
    {
        var officialRoster = await db.ContributionParticipants.AsNoTracking()
            .Where(x => x.Cycle.ProjectId == projectId)
            .Select(x => x.UserId)
            .ToArrayAsync(ct);
        var frozenRoster = ReadFrozenRosterUserIds(inputSnapshotJson);
        Require(frozenRoster.Length == officialRoster.Distinct().Count() &&
                frozenRoster.ToHashSet().SetEquals(officialRoster),
            "GIT_CONTRIBUTION_ALTERNATE_ROSTER_NOT_OFFICIAL",
            "Only a run using the official Contribution Cycle roster can be reviewed or finalized.", 409);
    }

    private static Guid[] ReadFrozenRosterUserIds(string inputSnapshotJson)
    {
        using var document = JsonDocument.Parse(inputSnapshotJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("roster", out var roster) ||
            roster.ValueKind != JsonValueKind.Array)
            throw Error("GIT_CONTRIBUTION_RUN_DATA_CORRUPT", "The frozen contribution roster is invalid.", 409);

        var ids = new List<Guid>();
        foreach (var participant in roster.EnumerateArray())
        {
            if (participant.ValueKind != JsonValueKind.Object ||
                !participant.TryGetProperty("userId", out var userId) ||
                userId.ValueKind != JsonValueKind.String ||
                !userId.TryGetGuid(out var id))
                throw Error("GIT_CONTRIBUTION_RUN_DATA_CORRUPT", "The frozen contribution roster is invalid.", 409);
            ids.Add(id);
        }
        return ids.Distinct().Order().ToArray();
    }

    private static string ComputeScoringInputHash(string inputSnapshotJson)
    {
        using var document = JsonDocument.Parse(inputSnapshotJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw Error("GIT_CONTRIBUTION_RUN_DATA_CORRUPT", "Stored experimental contribution data is invalid.", 409);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                if (property.Name is "policyAcknowledgment" or "rosterEligibilityEvaluatedAt" or "rosterSource")
                    continue;
                if (property.Name == "peerSnapshot" && property.Value.ValueKind == JsonValueKind.Null) continue;
                if (property.Name == "effectiveMembers" && property.Value.ValueKind == JsonValueKind.Null) continue;
                if (property.Name == "coverage" && property.Value.ValueKind == JsonValueKind.Null) continue;
                if (property.Name == "normalizationPolicy" && property.Value.ValueKind == JsonValueKind.Null) continue;
                if (property.Name is "normalizationInputs" or "normalization" && property.Value.ValueKind == JsonValueKind.Null) continue;
                writer.WritePropertyName(property.Name);
                WriteCanonicalHashValue(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void WriteCanonicalHashValue(Utf8JsonWriter writer, JsonElement element)
    {
        // PostgreSQL jsonb changes object property order. Arrays retain business ordering.
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalHashValue(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonicalHashValue(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                if (!element.TryGetDecimal(out var number))
                    throw Error("GIT_CONTRIBUTION_RUN_DATA_CORRUPT", "Invalid calculation number.", 409);
                writer.WriteRawValue(number.ToString("G29", CultureInfo.InvariantCulture));
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private async Task<ContributionProject> GetProjectAsync(Guid projectId, CancellationToken ct) =>
        await db.Projects.AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => new ContributionProject(x.Id, x.CreatorId, x.Status, x.CreatedAt))
            .SingleOrDefaultAsync(ct)
        ?? throw Error("PROJECT_NOT_FOUND", "Project not found.", 404);

    private static void RequireLeader(ContributionProject project, Guid actorId) =>
        Require(project.CreatorId == actorId, "FORBIDDEN", "Only the project leader can manage experimental Git Contribution.", 403);

    private static IReadOnlyList<SnapshotEvidenceEntry> SelectLatestEvidence(
        IReadOnlyList<SnapshotEvidenceEntry> entries,
        ICollection<GitExperimentalExcludedActivityResponse> exclusions)
    {
        var result = new List<SnapshotEvidenceEntry>();
        var relevant = entries.Where(x => IsScoredEvidenceCategory(x.Category) || x.Category == "pull-request-commits");
        foreach (var group in relevant.GroupBy(x => x.Category == "pull-request-commits"
            ? $"{x.RepositoryId:N}:{x.Category}:{x.ScopeKey}:{x.ExternalObjectId}"
            : $"{x.RepositoryId:N}:{x.Category}:{x.ExternalObjectId}", StringComparer.Ordinal))
        {
            var latestAt = group.Max(x => x.ObservedAt);
            var latest = group.Where(x => x.ObservedAt == latestAt).OrderBy(x => x.ObservationId).ToArray();
            if (latest.Select(x => x.VersionId).Distinct().Count() > 1)
            {
                foreach (var entry in latest)
                    exclusions.Add(Excluded(entry, "Corrupt", "ConflictingDuplicateEvidence"));
                continue;
            }

            result.Add(latest[0]);
            foreach (var older in group.Where(x => x.ObservedAt < latestAt))
                exclusions.Add(Excluded(older, "Incomplete", "DuplicateObservation"));
        }
        return result;
    }

    private static IReadOnlyList<AttributedWork> BuildActivities(
        Guid projectId,
        ProjectGitEvidenceSnapshotRead snapshot,
        IReadOnlyCollection<SnapshotEvidenceEntry> entries,
        IReadOnlyCollection<Guid> roster,
        IReadOnlyCollection<ProjectGitMemberIdentityHistory> history,
        ICollection<GitExperimentalExcludedActivityResponse> exclusions)
    {
        var relations = BuildPullRequestCommitRelations(entries);
        var results = new List<AttributedWork>();
        foreach (var entry in entries.Where(x => x.Category != "pull-request-commits"))
        {
            if (entry.Status is SnapshotEvidenceStatus.Unsupported or SnapshotEvidenceStatus.Corrupt)
            {
                exclusions.Add(Excluded(entry,
                    entry.Status == SnapshotEvidenceStatus.Unsupported ? "Unsupported" : "Corrupt",
                    entry.Status == SnapshotEvidenceStatus.Unsupported ? "UnsupportedEvidenceContract" : "CorruptEvidence"));
                continue;
            }
            if (entry.Facts is null || entry.Facts.Category != entry.Category ||
                entry.Status == SnapshotEvidenceStatus.Incomplete && entry.Facts is null)
            {
                exclusions.Add(Excluded(entry, "Incomplete", "MissingFrozenFacts"));
                continue;
            }

            var facts = entry.Facts;
            var category = CategoryFor(entry.Category);
            if (category is null) continue;

            if (entry.Category == "pull-requests")
            {
                AddPullRequest(projectId, snapshot, entry, relations, roster, history, results, exclusions);
                continue;
            }

            if (!TryGetActivity(facts, entry.Category, out var actorId, out var activityAt, out var reason))
            {
                exclusions.Add(Excluded(entry, Classify(reason), reason));
                continue;
            }
            if (activityAt > snapshot.ObservationCutoff)
            {
                exclusions.Add(Excluded(entry, "Incomplete", "ActivityAfterSnapshotCutoff"));
                continue;
            }
            if (category == ExperimentalGitActivityCategory.Commit && !IsSha(entry.ExternalObjectId))
            {
                exclusions.Add(Excluded(entry, "Incomplete", "InvalidCommitSha"));
                continue;
            }
            if (!TryResolveOwner(projectId, actorId, activityAt, snapshot.ObservationCutoff, roster, history,
                out var userId, out var attributionReason))
            {
                exclusions.Add(Excluded(entry, "Unattributed", attributionReason));
                continue;
            }

            var activity = new ExperimentalGitActivity(category.Value, entry.RepositoryId, entry.ExternalObjectId,
                userId, activityAt, []);
            results.Add(new(activity, Input(entry, activity, actorId)));
        }
        return results;
    }

    private static void AddPullRequest(
        Guid projectId,
        ProjectGitEvidenceSnapshotRead snapshot,
        SnapshotEvidenceEntry entry,
        IReadOnlyDictionary<(Guid RepositoryId, long PrNumber), IReadOnlyList<PrCommitRelation>> relations,
        IReadOnlyCollection<Guid> roster,
        IReadOnlyCollection<ProjectGitMemberIdentityHistory> history,
        ICollection<AttributedWork> results,
        ICollection<GitExperimentalExcludedActivityResponse> exclusions)
    {
        var facts = entry.Facts!;
        var details = facts.Details!;
        var prNumber = details.RelatedPullRequestNumber;
        if (prNumber is null || details.Merged is not true || details.MergedAt is null)
        {
            exclusions.Add(Excluded(entry, "Incomplete", "MergedPullRequestFactsMissing"));
            return;
        }
        var actorId = ActorId(facts, "actor");
        var activityAt = details.MergedAt.Value;
        if (actorId is null)
        {
            exclusions.Add(Excluded(entry, "Unattributed", "MissingVerifiedProviderActorId"));
            return;
        }
        if (activityAt > snapshot.ObservationCutoff)
        {
            exclusions.Add(Excluded(entry, "Incomplete", "ActivityAfterSnapshotCutoff"));
            return;
        }
        if (!relations.TryGetValue((entry.RepositoryId, prNumber.Value), out var commits))
        {
            exclusions.Add(Excluded(entry, "Incomplete", "PullRequestCommitEvidenceMissing"));
            return;
        }
        var relatedShas = commits.Select(x => x.Sha).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!commits.Any(x => x.AuthorProviderAccountId == actorId.Value))
        {
            exclusions.Add(Excluded(entry, "Incomplete", "NoSubstantiveAuthorCommit"));
            return;
        }
        if (!TryResolveOwner(projectId, actorId.Value, activityAt, snapshot.ObservationCutoff, roster, history,
            out var userId, out var attributionReason))
        {
            exclusions.Add(Excluded(entry, "Unattributed", attributionReason));
            return;
        }

        var activity = new ExperimentalGitActivity(ExperimentalGitActivityCategory.PullRequest, entry.RepositoryId,
            entry.ExternalObjectId, userId, activityAt, relatedShas);
        results.Add(new(activity, Input(entry, activity, actorId.Value)));
    }

    private static IReadOnlyDictionary<(Guid RepositoryId, long PrNumber), IReadOnlyList<PrCommitRelation>> BuildPullRequestCommitRelations(
        IReadOnlyCollection<SnapshotEvidenceEntry> entries)
    {
        return entries.Where(x => x.Category == "pull-request-commits" && x.Status is SnapshotEvidenceStatus.Valid or SnapshotEvidenceStatus.Incomplete &&
                x.Facts is not null && IsSha(x.ExternalObjectId))
            .Select(x =>
            {
                var facts = x.Facts!;
                var prNumber = facts.Details?.RelatedPullRequestNumber ?? ParsePositiveLong(x.ScopeKey);
                return (x.RepositoryId, PrNumber: prNumber, Sha: x.ExternalObjectId.ToLowerInvariant(), AuthorId: ActorId(facts, "author"));
            })
            .Where(x => x.PrNumber is not null)
            .GroupBy(x => (x.RepositoryId, x.PrNumber!.Value))
            .ToDictionary(x => x.Key, x => (IReadOnlyList<PrCommitRelation>)x
                .Select(y => new PrCommitRelation(y.Sha, y.AuthorId)).ToArray());
    }

    private static bool TryGetActivity(
        GitFrozenFacts facts,
        string category,
        out long actorId,
        out DateTimeOffset activityAt,
        out string reason)
    {
        actorId = 0;
        activityAt = default;
        reason = "IncompleteActivityFacts";
        string role;
        DateTimeOffset? timestamp;
        switch (category)
        {
            case "commits":
                role = "author";
                timestamp = facts.CommitterDate;
                break;
            case "pull-request-reviews":
                role = "actor";
                timestamp = facts.Details?.SubmittedAt;
                if (facts.Details?.State is not ("APPROVED" or "CHANGES_REQUESTED" or "COMMENTED"))
                {
                    reason = "UnsupportedReviewState";
                    return false;
                }
                break;
            case "issues":
                role = "actor";
                timestamp = facts.Details?.CreatedAt;
                if (facts.Details?.IsPullRequest is not false)
                {
                    reason = facts.Details?.IsPullRequest is true ? "IssueIsPullRequest" : "MissingIssueType";
                    return false;
                }
                break;
            case "issue-events":
                role = "actor";
                timestamp = facts.Details?.CreatedAt;
                if (facts.Details?.EventKind is not ("closed" or "reopened" or "assigned" or "unassigned" or "labeled" or "unlabeled"))
                {
                    reason = "UnsupportedIssueEvent";
                    return false;
                }
                break;
            case "issue-comments":
                role = "actor";
                timestamp = facts.Details?.CreatedAt;
                break;
            default:
                reason = "UnsupportedActivityCategory";
                return false;
        }

        var parsedActorId = ActorId(facts, role);
        if (parsedActorId is null)
        {
            reason = "MissingVerifiedProviderActorId";
            return false;
        }
        if (timestamp is null)
        {
            reason = "MissingActivityTimestamp";
            return false;
        }
        actorId = parsedActorId.Value;
        activityAt = timestamp.Value;
        reason = "";
        return true;
    }

    private static bool TryResolveOwner(
        Guid projectId,
        long actorId,
        DateTimeOffset activityAt,
        DateTimeOffset cutoff,
        IReadOnlyCollection<Guid> roster,
        IReadOnlyCollection<ProjectGitMemberIdentityHistory> history,
        out Guid? userId,
        out string reason)
    {
        var attribution = GitHistoricalAttributionService.Resolve(projectId, actorId, activityAt, cutoff, roster, history);
        userId = attribution.UserId;
        reason = attribution.Reason;
        if (userId is not null) return true;
        return reason == "OwnerOutsideRoster";
    }

    private static ExperimentalGitActivityCategory? CategoryFor(string category) => category switch
    {
        "commits" => ExperimentalGitActivityCategory.Commit,
        "pull-requests" => ExperimentalGitActivityCategory.PullRequest,
        "pull-request-reviews" => ExperimentalGitActivityCategory.Review,
        "issues" or "issue-events" or "issue-comments" => ExperimentalGitActivityCategory.Issue,
        _ => null
    };

    private static bool IsScoredEvidenceCategory(string category) => CategoryFor(category) is not null;

    private static long? ActorId(GitFrozenFacts facts, string role)
    {
        var actors = facts.Actors.Where(x => x.Role == role).ToArray();
        return actors.Length == 1 && long.TryParse(actors[0].ProviderAccountId, NumberStyles.None,
            CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
    }

    private static bool IsSha(string value) =>
        value.Length is 40 or 64 && value.All(Uri.IsHexDigit);

    private static long? ParsePositiveLong(string value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : null;

    private static IReadOnlyList<GitExperimentalCategoryStatus> BuildCategoryStatuses(
        IReadOnlyList<SnapshotEvidenceEntry> entries,
        int restrictedCount,
        IReadOnlyDictionary<ExperimentalGitActivityCategory, int> eligibleCounts,
        IReadOnlyDictionary<ExperimentalGitActivityCategory, int> externalCounts,
        IReadOnlyCollection<GitExperimentalExcludedActivityResponse> exclusions)
    {
        var names = new Dictionary<ExperimentalGitActivityCategory, string>
        {
            [ExperimentalGitActivityCategory.Commit] = "Commit",
            [ExperimentalGitActivityCategory.PullRequest] = "PR",
            [ExperimentalGitActivityCategory.Review] = "Review",
            [ExperimentalGitActivityCategory.Issue] = "Issue"
        };
        return names.Select(pair =>
        {
            var relatedEntries = entries.Where(x => CategoryFor(x.Category) == pair.Key).ToArray();
            var relatedExclusions = exclusions.Where(x => CategoryFor(x.Category) == pair.Key)
                .ToArray();
            var eligible = eligibleCounts.GetValueOrDefault(pair.Key);
            var status = eligible > 0 ? "EligibleForExperimentalMvp" :
                restrictedCount > 0 ? "Incomplete" :
                relatedEntries.Length == 0 ? "NotApplicable" :
                relatedExclusions.Any(x => x.Classification == "Corrupt") ? "Corrupt" :
                relatedExclusions.Any(x => x.Classification == "Unsupported") ? "Unsupported" :
                relatedExclusions.Any(x => x.Classification == "Unattributed") ? "Unattributed" : "Incomplete";
            var limitations = new List<string>();
            if (restrictedCount > 0 || relatedEntries.Any(x => x.Status == SnapshotEvidenceStatus.Incomplete))
                limitations.Add("EvidenceCoverageIncomplete");
            if (restrictedCount > 0)
                limitations.Add("SomeRepositoryEvidenceRestricted");
            if (pair.Key == ExperimentalGitActivityCategory.Commit && relatedEntries.Any(x =>
                x.Facts?.Details?.MissingFacts.Contains("PrAndPatchRelationships", StringComparer.Ordinal) == true))
                limitations.Add("PartialPatchOverlapNotSupportedInExperimentalV1");
            if (pair.Key == ExperimentalGitActivityCategory.PullRequest && relatedEntries.Any(x =>
                x.Facts?.Details?.MissingFacts.Contains("PatchAndAllocationEvidence", StringComparer.Ordinal) == true))
                limitations.Add("PartialPatchOverlapNotSupportedInExperimentalV1");
            return new GitExperimentalCategoryStatus(
                pair.Value, status, eligible, externalCounts.GetValueOrDefault(pair.Key), relatedExclusions.Length, limitations);
        }).ToArray();
    }

    private static GitExperimentalExcludedActivityResponse Excluded(SnapshotEvidenceEntry entry, string classification, string reason) =>
        new(entry.Category, entry.RepositoryId, entry.ExternalObjectId, classification, reason);

    private static string Classify(string reason) =>
        reason == "MissingVerifiedProviderActorId" ? "Unattributed" :
        reason.StartsWith("Unsupported", StringComparison.Ordinal) || reason == "IssueIsPullRequest"
            ? "Unsupported"
            : "Incomplete";

    private static string CategoryName(ExperimentalGitActivityCategory category) => category switch
    {
        ExperimentalGitActivityCategory.Commit => "commits",
        ExperimentalGitActivityCategory.PullRequest => "pull-requests",
        ExperimentalGitActivityCategory.Review => "pull-request-reviews",
        ExperimentalGitActivityCategory.Issue => "issues",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    private static string PolicyExclusionClassification(string reason) =>
        reason is "PartialPullRequestOverlapUnsupported" or "ExactPullRequestOverlapTieNeedsReview"
            ? "Unsupported"
            : "Deduplicated";

    private static T Deserialize<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw Error("GIT_CONTRIBUTION_RUN_DATA_CORRUPT", "Stored experimental contribution data is invalid.", 409);

    private static EligibleActivityInput Input(SnapshotEvidenceEntry entry, ExperimentalGitActivity activity, long actorId) =>
        new(activity.Category.ToString(), entry.RepositoryId, entry.ExternalObjectId, activity.UserId, actorId,
            activity.ActivityAt, activity.RelatedCommitShas.Order(StringComparer.Ordinal).ToArray(),
            entry.ObservationId, entry.VersionId, entry.ContentHash);

    private static ProjectException Error(string code, string message, int status = 400) => new(code, message, status);

    private static void Require(bool condition, string code, string message, int status = 400)
    {
        if (!condition) throw Error(code, message, status);
    }

    private sealed record ContributionProject(Guid Id, Guid CreatorId, ProjectStatus Status, DateTimeOffset CreatedAt);
    private sealed record PrCommitRelation(string Sha, long? AuthorProviderAccountId);
    private sealed record EligibleActivityInput(
        string Category,
        Guid RepositoryId,
        string ObjectId,
        Guid? UserId,
        long ProviderActorId,
        DateTimeOffset ActivityAt,
        IReadOnlyList<string> RelatedCommitShas,
        Guid ObservationId,
        Guid VersionId,
        string VersionHash);
    private sealed record AttributedWork(ExperimentalGitActivity Activity, EligibleActivityInput Input);
}
