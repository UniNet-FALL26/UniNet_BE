using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class ContributionService(UniNetDbContext db) : IContributionService
{
    private const int MaximumCommentLength = 2000;

    public async Task<ContributionCycleResponse> GetCycleAsync(Guid projectId, Guid accountId, CancellationToken ct)
    {
        var actor = await GetActiveStudentAsync(accountId, ct);
        var (project, cycle) = await GetRequiredCycleAsync(projectId, ct);
        RequireParticipant(cycle, actor.Id);
        return ToCycleResponse(project, cycle);
    }

    public async Task<IReadOnlyList<ContributionPeerEvaluationTaskResponse>> GetMyPeerEvaluationsAsync(
        Guid projectId, Guid accountId, CancellationToken ct)
    {
        var actor = await GetActiveStudentAsync(accountId, ct);
        var (_, cycle) = await GetRequiredCycleAsync(projectId, ct);
        RequireParticipant(cycle, actor.Id);
        var current = await db.ContributionPeerEvaluations.AsNoTracking()
            .Where(e => e.CycleId == cycle.Id && e.EvaluatorUserId == actor.Id)
            .ToDictionaryAsync(e => e.EvaluateeUserId, ct);

        return cycle.Participants
            .Where(p => p.UserId != actor.Id)
            .OrderBy(p => p.UserId)
            .Select(p => current.TryGetValue(p.UserId, out var evaluation)
                ? new ContributionPeerEvaluationTaskResponse(
                    p.UserId, p.User.FullName, p.User.Nickname, evaluation.Score, evaluation.Comment,
                    evaluation.SubmittedAt, evaluation.UpdatedAt)
                : new ContributionPeerEvaluationTaskResponse(
                    p.UserId, p.User.FullName, p.User.Nickname, null, null, null, null))
            .ToArray();
    }

    public async Task<ContributionPeerEvaluationResponse> UpsertPeerEvaluationAsync(
        Guid projectId, Guid evaluateeUserId, Guid accountId, ContributionPeerEvaluationRequest request, CancellationToken ct)
    {
        var actor = await GetActiveStudentAsync(accountId, ct);
        Require(request.Score is >= 1 and <= 5,
            "CONTRIBUTION_INVALID_PEER_SCORE", "Peer evaluation score must be an integer from 1 to 5.");
        Require(request.Comment is null || request.Comment.Length <= MaximumCommentLength,
            "CONTRIBUTION_INVALID_PEER_COMMENT", "Peer evaluation comment cannot exceed 2000 characters.");
        Require(actor.Id != evaluateeUserId,
            "CONTRIBUTION_SELF_EVALUATION_FORBIDDEN", "Participants cannot evaluate themselves.", 403);
        var (project, cycle) = await GetRequiredCycleAsync(projectId, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var locked = await db.ContributionCycles
            .Where(c => c.Id == cycle.Id && c.Status == ContributionCycleStatus.CollectingEvaluations)
            .ExecuteUpdateAsync(update => update.SetProperty(c => c.Status, c => c.Status), ct);
        Require(locked == 1,
            "CONTRIBUTION_CYCLE_NOT_OPEN", "Peer evaluations can only be changed while the cycle is collecting evaluations.", 409);

        var participantIds = await db.ContributionParticipants.AsNoTracking()
            .Where(p => p.CycleId == cycle.Id)
            .Select(p => p.UserId)
            .ToListAsync(ct);
        Require(participantIds.Contains(actor.Id), "FORBIDDEN", "Only cycle participants can submit peer evaluations.", 403);
        Require(participantIds.Contains(evaluateeUserId), "CONTRIBUTION_PARTICIPANT_NOT_FOUND", "The evaluatee is not in this cycle.", 404);

        var now = DateTimeOffset.UtcNow;
        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        var evaluation = await db.ContributionPeerEvaluations.SingleOrDefaultAsync(
            e => e.CycleId == cycle.Id && e.EvaluatorUserId == actor.Id && e.EvaluateeUserId == evaluateeUserId, ct);
        var isNew = evaluation is null;
        var previousScore = evaluation?.Score;
        var previousComment = evaluation?.Comment;
        if (evaluation is null)
        {
            evaluation = new ContributionPeerEvaluation
            {
                CycleId = cycle.Id,
                EvaluatorUserId = actor.Id,
                EvaluateeUserId = evaluateeUserId,
                Score = request.Score,
                Comment = comment,
                SubmittedAt = now,
                UpdatedAt = now
            };
            db.ContributionPeerEvaluations.Add(evaluation);
        }
        else
        {
            evaluation.Score = request.Score;
            evaluation.Comment = comment;
            evaluation.UpdatedAt = now;
        }

        db.ContributionPeerEvaluationHistory.Add(new ContributionPeerEvaluationHistory
        {
            CycleId = cycle.Id,
            EvaluatorUserId = actor.Id,
            EvaluateeUserId = evaluateeUserId,
            ContributionPeerEvaluationId = evaluation.Id,
            PreviousScore = previousScore,
            PreviousComment = previousComment,
            NewScore = request.Score,
            NewComment = comment,
            Action = isNew ? ContributionPeerEvaluationAction.Created : ContributionPeerEvaluationAction.Updated,
            ActorUserId = actor.Id,
            ChangedAt = now
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new(cycle.Id, evaluateeUserId, request.Score, comment, evaluation.SubmittedAt, evaluation.UpdatedAt);
    }

    public async Task<ContributionRunResponse> CalculateAsync(Guid projectId, Guid accountId, CancellationToken ct)
    {
        var actor = await GetActiveStudentAsync(accountId, ct);
        var (project, cycle) = await GetRequiredCycleAsync(projectId, ct);
        Require(project.CreatorId == actor.Id, "FORBIDDEN", "Only the project leader can calculate contribution.", 403);
        Require(cycle.Status == ContributionCycleStatus.CollectingEvaluations,
            "CONTRIBUTION_CYCLE_NOT_OPEN", "The contribution cycle is not accepting a calculation.", 409);
        throw InputsRetired();
    }

    public async Task<ContributionRunResponse> GetRunAsync(Guid projectId, Guid runId, Guid accountId, CancellationToken ct)
    {
        var actor = await GetActiveStudentAsync(accountId, ct);
        var (project, cycle) = await GetRequiredCycleAsync(projectId, ct);
        Require(project.CreatorId == actor.Id, "FORBIDDEN", "Only the project leader can inspect calculation details.", 403);
        var run = await db.ContributionCalculationRuns.AsNoTracking()
            .Include(r => r.Results).ThenInclude(r => r.User)
            .Include(r => r.Reviews)
            .SingleOrDefaultAsync(r => r.Id == runId && r.CycleId == cycle.Id, ct)
            ?? throw new ProjectException("CONTRIBUTION_RUN_NOT_FOUND", "Calculation run not found.", 404);
        return ToRunResponse(run, cycle.Participants);
    }

    public async Task<ContributionReviewResponse> ReviewRunAsync(
        Guid projectId, Guid runId, Guid accountId, ContributionReviewRequest request, CancellationToken ct)
    {
        var actor = await GetActiveStudentAsync(accountId, ct);
        var (project, cycleInfo) = await GetRequiredCycleAsync(projectId, ct);
        Require(project.CreatorId == actor.Id, "FORBIDDEN", "Only the project leader can review contribution.", 403);
        var decision = request.Decision?.Trim().ToLowerInvariant() switch
        {
            "confirm" or "confirmed" => ContributionReviewDecision.Confirmed,
            "requestrevision" or "request_revision" or "revisionrequested" => ContributionReviewDecision.RevisionRequested,
            _ => throw new ProjectException("CONTRIBUTION_INVALID_REVIEW_DECISION", "Decision must be Confirm or RequestRevision.")
        };
        var feedback = string.IsNullOrWhiteSpace(request.Feedback) ? null : request.Feedback.Trim();
        Require(feedback is null || feedback.Length <= MaximumCommentLength,
            "CONTRIBUTION_INVALID_REVIEW_FEEDBACK", "Review feedback cannot exceed 2000 characters.");
        Require(decision != ContributionReviewDecision.RevisionRequested || feedback is not null,
            "CONTRIBUTION_REVISION_FEEDBACK_REQUIRED", "Feedback is required when requesting a revision.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var locked = await db.ContributionCycles
            .Where(c => c.Id == cycleInfo.Id && c.Status == ContributionCycleStatus.AwaitingReview)
            .ExecuteUpdateAsync(update => update.SetProperty(c => c.Status, c => c.Status), ct);
        Require(locked == 1, "CONTRIBUTION_RUN_NOT_REVIEWABLE", "The contribution cycle is not awaiting review.", 409);

        var cycle = await db.ContributionCycles.SingleAsync(c => c.Id == cycleInfo.Id, ct);
        var run = await db.ContributionCalculationRuns.SingleOrDefaultAsync(
            r => r.Id == runId && r.CycleId == cycle.Id && r.Status == ContributionCalculationRunStatus.PendingReview, ct)
            ?? throw new ProjectException("CONTRIBUTION_RUN_NOT_REVIEWABLE", "Calculation run is not pending review.", 409);
        var latestRunNumber = await db.ContributionCalculationRuns
            .Where(r => r.CycleId == cycle.Id)
            .MaxAsync(r => r.RunNumber, ct);
        Require(run.RunNumber == latestRunNumber, "CONTRIBUTION_RUN_NOT_REVIEWABLE", "Only the latest calculation run can be reviewed.", 409);
        if (decision == ContributionReviewDecision.Confirmed)
        {
            throw InputsRetired();
        }

        var now = DateTimeOffset.UtcNow;
        run.Status = ContributionCalculationRunStatus.RevisionRequested;
        cycle.Status = ContributionCycleStatus.CollectingEvaluations;
        cycle.FinalizedAt = null;
        db.ContributionReviews.Add(new ContributionReview
        {
            RunId = run.Id,
            ReviewerId = actor.Id,
            Decision = decision,
            Feedback = feedback,
            ReviewedAt = now
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(run.Id, decision, cycle.Status, now);
    }

    public async Task<ContributionFinalResultsResponse> GetFinalResultsAsync(
        Guid projectId, Guid accountId, CancellationToken ct)
    {
        var actor = await GetActiveStudentAsync(accountId, ct);
        var (_, cycle) = await GetRequiredCycleAsync(projectId, ct);
        RequireParticipant(cycle, actor.Id);
        Require(cycle.Status == ContributionCycleStatus.Finalized && cycle.FinalizedAt is not null,
            "CONTRIBUTION_RESULTS_NOT_FINALIZED", "Contribution results are not finalized.", 409);
        var run = await db.ContributionCalculationRuns.AsNoTracking()
            .Where(r => r.CycleId == cycle.Id && r.Status == ContributionCalculationRunStatus.Confirmed)
            .OrderByDescending(r => r.RunNumber)
            .Include(r => r.Results).ThenInclude(r => r.User)
            .FirstOrDefaultAsync(ct)
            ?? throw new ProjectException("CONTRIBUTION_FINAL_RUN_NOT_FOUND", "Finalized calculation run was not found.", 409);

        return new(
            cycle.Id,
            run.RunNumber,
            cycle.FinalizedAt.Value,
            run.Results.OrderBy(r => r.UserId)
                .Select(r => new ContributionFinalResultResponse(
                    r.UserId, r.User.FullName, r.User.Nickname, r.TotalScore))
                .ToArray());
    }

    private async Task<UserProfile> GetActiveStudentAsync(Guid accountId, CancellationToken ct)
    {
        var actor = await db.UserProfiles.AsNoTracking().Include(p => p.Account)
            .SingleOrDefaultAsync(p => p.AccountId == accountId, ct);
        Require(actor is not null && actor.Account.Role == AccountRole.Student && actor.Account.Status == AccountStatus.Active,
            "FORBIDDEN", "An active Student account is required.", 403);
        return actor!;
    }

    private async Task<(Project Project, ContributionCycle Cycle)> GetRequiredCycleAsync(Guid projectId, CancellationToken ct)
    {
        var hasWorkflowSchema = !db.Database.IsNpgsql() || await db.Database.SqlQuery<int>($"SELECT CASE WHEN to_regclass('public.\"ProjectContributionWorkflows\"') IS NULL THEN 0 ELSE 1 END AS \"Value\"").SingleAsync(ct) == 1;
        Require(!hasWorkflowSchema || !await db.Set<ProjectContributionWorkflow>().AsNoTracking().AnyAsync(x => x.ProjectId == projectId && x.Mode == "OfficialV2", ct),
            "CONTRIBUTION_USE_OFFICIAL_V2", "This project uses the official Contribution V2 workflow.", 409);
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new ProjectException("PROJECT_NOT_FOUND", "Project not found.", 404);
        Require(project.Status == ProjectStatus.Completed,
            "CONTRIBUTION_PROJECT_NOT_COMPLETED", "Contribution is available only after project completion.", 409);
        if (project.CompletedAt is null)
            throw LegacyCompletion();

        var cycle = await db.ContributionCycles.AsNoTracking()
            .Include(c => c.Participants).ThenInclude(p => p.User)
            .SingleOrDefaultAsync(c => c.ProjectId == projectId, ct);
        if (cycle is null)
            throw LegacyCompletion();
        return (project, cycle);
    }

    private static ProjectException LegacyCompletion() =>
        new("CONTRIBUTION_LEGACY_PROJECT_REQUIRES_REVIEW",
            "This project was completed before contribution snapshots were available and requires explicit data review.", 409);

    private static void Require(
        [DoesNotReturnIf(false)] bool condition,
        string code,
        string message,
        int status = 400)
    {
        if (!condition)
            throw new ProjectException(code, message, status);
    }

    private static void RequireParticipant(ContributionCycle cycle, Guid userId)
    {
        if (!cycle.Participants.Any(p => p.UserId == userId))
            throw new ProjectException("FORBIDDEN", "Only contribution cycle participants can access this resource.", 403);
    }

    private static ContributionCycleResponse ToCycleResponse(Project project, ContributionCycle cycle) =>
        new(cycle.Id, project.Id, cycle.Status, cycle.OpenedAt, cycle.FinalizedAt, cycle.FormulaVersion,
            cycle.Participants.OrderBy(p => p.UserId)
                .Select(p => new ContributionParticipantResponse(
                    p.UserId, p.User.FullName, p.User.Nickname, p.User.AvatarUrl, p.RoleAtSnapshot, p.IsLeader))
                .ToArray());

    private static ContributionRunResponse ToRunResponse(
        ContributionCalculationRun run,
        IReadOnlyCollection<ContributionParticipant> participants)
    {
        var participantById = participants.ToDictionary(p => p.UserId);
        return new(run.Id, run.CycleId, run.RunNumber, run.FormulaVersion, run.Status, run.CalculatedAt,
            JsonSerializer.Deserialize<string[]>(run.WarningsJson) ?? [],
            run.Results.OrderBy(r => r.UserId).Select(result =>
            {
                var participant = participantById[result.UserId];
                return new ContributionRunResultResponse(
                    result.UserId, participant.User.FullName, result.TaskExecutionScore,
                    result.ApprovedDeliverablesScore, result.PeerEvaluationScore, result.TotalScore,
                    result.CalculationDetails);
            }).ToArray(),
            run.Reviews.OrderBy(r => r.ReviewedAt)
                .Select(r => new ContributionRunReviewResponse(r.Decision, r.Feedback, r.ReviewedAt))
                .ToArray());
    }

    private static ProjectException InputsRetired() => new("CONTRIBUTION_INPUTS_RETIRED",
        "Task-based contribution calculation has been retired; historical results remain available.", 410);
}
