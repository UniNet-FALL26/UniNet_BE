using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
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
    private const string FormulaVersion = "1";
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

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var locked = await db.ContributionCycles
            .Where(c => c.Id == cycle.Id && c.Status == ContributionCycleStatus.CollectingEvaluations)
            .ExecuteUpdateAsync(update => update.SetProperty(c => c.Status, c => c.Status), ct);
        Require(locked == 1,
            "CONTRIBUTION_CYCLE_NOT_OPEN", "The contribution cycle is not accepting a calculation.", 409);
        var trackedCycle = await db.ContributionCycles.SingleAsync(c => c.Id == cycle.Id, ct);

        var participants = await db.ContributionParticipants.AsNoTracking()
            .Include(p => p.User)
            .Where(p => p.CycleId == cycle.Id)
            .OrderBy(p => p.UserId)
            .ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var inputSnapshot = await CaptureInputSnapshotAsync(projectId, cycle.Id, now, ct);
        var taskInputs = inputSnapshot.Tasks.Select(task => new ContributionTaskInput(
            task.TaskId, task.EffectiveStatus, task.AssigneeIds)).ToArray();
        var deliverableInputs = inputSnapshot.LatestReportVersions
            .Where(r => !r.IsDeleted && r.Status == TaskReportStatus.Approved)
            .Select(r => new ContributionDeliverableInput(r.ReportGroupId, r.SubmittedById))
            .ToArray();
        var evaluations = inputSnapshot.PeerEvaluations;

        var calculated = ContributionCalculator.Calculate(inputSnapshot.ParticipantUserIds, taskInputs, deliverableInputs, evaluations);
        var runNumber = (await db.ContributionCalculationRuns.AsNoTracking()
            .Where(r => r.CycleId == cycle.Id)
            .Select(r => (int?)r.RunNumber)
            .MaxAsync(ct) ?? 0) + 1;
        var run = new ContributionCalculationRun
        {
            CycleId = cycle.Id,
            RunNumber = runNumber,
            FormulaVersion = cycle.FormulaVersion,
            InputSnapshot = JsonSerializer.Serialize(inputSnapshot),
            InputHash = HashInputSnapshot(inputSnapshot),
            WarningsJson = JsonSerializer.Serialize(calculated.Warnings),
            CalculatedById = actor.Id,
            CalculatedAt = now,
            Status = ContributionCalculationRunStatus.PendingReview
        };
        db.ContributionCalculationRuns.Add(run);
        foreach (var score in calculated.Results)
        {
            var memberTasks = taskInputs
                .Where(t => t.AssigneeIds.Contains(score.UserId) && t.EffectiveStatus != ProjectTaskStatus.Cancelled)
                .Select(t => new { t.TaskId, t.EffectiveStatus });
            var memberDeliverables = deliverableInputs
                .Where(d => d.SubmittedById == score.UserId)
                .Select(d => d.ReportGroupId);
            var receivedScores = evaluations
                .Where(e => e.EvaluateeUserId == score.UserId)
                .Select(e => e.Score)
                .ToArray();
            db.ContributionResults.Add(new ContributionResult
            {
                CycleId = cycle.Id,
                RunId = run.Id,
                UserId = score.UserId,
                TaskExecutionScore = score.TaskExecutionScore,
                ApprovedDeliverablesScore = score.ApprovedDeliverablesScore,
                PeerEvaluationScore = score.PeerEvaluationScore,
                TotalScore = score.TotalScore,
                CalculationDetails = JsonSerializer.Serialize(new
                {
                    Tasks = memberTasks,
                    ApprovedReportGroups = memberDeliverables,
                    AverageReceivedPeerRating = receivedScores.Length == 0 ? (decimal?)null : receivedScores.Average(score => (decimal)score)
                })
            });
        }

        trackedCycle.Status = ContributionCycleStatus.AwaitingReview;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToRunResponse(run, calculated.Results, participants);
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
            var currentSnapshot = await CaptureInputSnapshotAsync(
                projectId, cycle.Id, DateTimeOffset.UtcNow, ct);
            Require(
                HashInputSnapshot(currentSnapshot) == run.InputHash,
                "CONTRIBUTION_INPUTS_CHANGED",
                "Calculation inputs changed after this run was generated. Request a revision and calculate again.",
                409);
        }

        var now = DateTimeOffset.UtcNow;
        run.Status = decision == ContributionReviewDecision.Confirmed
            ? ContributionCalculationRunStatus.Confirmed
            : ContributionCalculationRunStatus.RevisionRequested;
        cycle.Status = decision == ContributionReviewDecision.Confirmed
            ? ContributionCycleStatus.Finalized
            : ContributionCycleStatus.CollectingEvaluations;
        cycle.FinalizedAt = decision == ContributionReviewDecision.Confirmed ? now : null;
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

    private static ContributionRunResponse ToRunResponse(
        ContributionCalculationRun run,
        IReadOnlyList<ContributionParticipantScore> scores,
        IReadOnlyCollection<ContributionParticipant> participants)
    {
        var participantById = participants.ToDictionary(p => p.UserId);
        return new(run.Id, run.CycleId, run.RunNumber, run.FormulaVersion, run.Status, run.CalculatedAt,
            JsonSerializer.Deserialize<string[]>(run.WarningsJson) ?? [],
            scores.OrderBy(s => s.UserId).Select(score =>
            {
                var participant = participantById[score.UserId];
                var details = run.Results.Single(r => r.UserId == score.UserId).CalculationDetails;
                return new ContributionRunResultResponse(
                    score.UserId, participant.User.FullName, score.TaskExecutionScore,
                    score.ApprovedDeliverablesScore, score.PeerEvaluationScore, score.TotalScore, details);
            }).ToArray(),
            []);
    }

    private async Task<ContributionInputSnapshot> CaptureInputSnapshotAsync(
        Guid projectId, Guid cycleId, DateTimeOffset now, CancellationToken ct)
    {
        var participantIds = await db.ContributionParticipants.AsNoTracking()
            .Where(p => p.CycleId == cycleId)
            .OrderBy(p => p.UserId)
            .Select(p => p.UserId)
            .ToArrayAsync(ct);
        var tasks = await db.ProjectTasks.AsNoTracking()
            .Where(t => t.ProjectId == projectId)
            .Include(t => t.Assignments)
            .OrderBy(t => t.Id)
            .ToListAsync(ct);
        var taskSnapshots = tasks.Select(task => new TaskInputSnapshot(
            task.Id,
            task.Status,
            task.Deadline,
            ProjectTaskStatusResolver.Resolve(task, now),
            task.Assignments.Select(a => a.UserId).Distinct().Order().ToArray())).ToArray();
        var reports = await db.TaskReports.AsNoTracking()
            .Where(r => r.ProjectTask.ProjectId == projectId)
            .OrderBy(r => r.ReportGroupId)
            .ThenByDescending(r => r.Version)
            .Select(r => new ReportVersionSnapshot(
                r.Id, r.ProjectTaskId, r.ReportGroupId, r.Version, r.SubmittedById, r.Status, r.IsDeleted))
            .ToListAsync(ct);
        var latestReports = reports
            .GroupBy(r => r.ReportGroupId)
            .Select(group => group.First())
            .OrderBy(r => r.ReportGroupId)
            .ToArray();
        var evaluations = await db.ContributionPeerEvaluations.AsNoTracking()
            .Where(e => e.CycleId == cycleId)
            .OrderBy(e => e.EvaluatorUserId)
            .ThenBy(e => e.EvaluateeUserId)
            .Select(e => new ContributionPeerEvaluationInput(e.EvaluatorUserId, e.EvaluateeUserId, e.Score))
            .ToArrayAsync(ct);
        return new(participantIds, taskSnapshots, latestReports, evaluations);
    }

    private static string HashInputSnapshot(ContributionInputSnapshot snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private sealed record ContributionInputSnapshot(
        Guid[] ParticipantUserIds,
        TaskInputSnapshot[] Tasks,
        ReportVersionSnapshot[] LatestReportVersions,
        ContributionPeerEvaluationInput[] PeerEvaluations);

    private sealed record TaskInputSnapshot(
        Guid TaskId,
        ProjectTaskStatus Status,
        DateTimeOffset Deadline,
        ProjectTaskStatus EffectiveStatus,
        Guid[] AssigneeIds);

    private sealed record ReportVersionSnapshot(
        Guid Id,
        Guid ProjectTaskId,
        Guid ReportGroupId,
        int Version,
        Guid SubmittedById,
        TaskReportStatus Status,
        bool IsDeleted);
}
