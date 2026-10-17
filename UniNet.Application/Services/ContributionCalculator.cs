using UniNet.Domain.Enums;

namespace UniNet.Application.Services;

public sealed record ContributionTaskInput(
    Guid TaskId,
    ProjectTaskStatus EffectiveStatus,
    IReadOnlyCollection<Guid> AssigneeIds);

public sealed record ContributionDeliverableInput(Guid ReportGroupId, Guid SubmittedById);

public sealed record ContributionPeerEvaluationInput(
    Guid EvaluatorUserId,
    Guid EvaluateeUserId,
    int Score);

public sealed record ContributionParticipantScore(
    Guid UserId,
    decimal TaskExecutionScore,
    decimal ApprovedDeliverablesScore,
    decimal PeerEvaluationScore,
    decimal TotalScore);

public sealed record ContributionCalculationOutput(
    IReadOnlyList<ContributionParticipantScore> Results,
    IReadOnlyList<string> Warnings);

public static class ContributionCalculator
{
    private const MidpointRounding ScoreRounding = MidpointRounding.AwayFromZero;

    public static ContributionCalculationOutput Calculate(
        IReadOnlyCollection<Guid> participantIds,
        IReadOnlyCollection<ContributionTaskInput> tasks,
        IReadOnlyCollection<ContributionDeliverableInput> deliverables,
        IReadOnlyCollection<ContributionPeerEvaluationInput> peerEvaluations)
    {
        var participants = participantIds.Distinct().Order().ToArray();
        if (participants.Length == 0)
            throw new ProjectException("CONTRIBUTION_ROSTER_EMPTY", "The contribution cycle has no participants.", 409);

        var participantSet = participants.ToHashSet();
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        var taskTotals = participants.ToDictionary(id => id, _ => (Score: 0m, Count: 0));
        var uniqueTasks = tasks.GroupBy(task => task.TaskId).Select(group =>
        {
            var first = group.First();
            var firstAssignees = first.AssigneeIds.Distinct().Order().ToArray();
            if (group.Any(task => task.EffectiveStatus != first.EffectiveStatus ||
                !task.AssigneeIds.Distinct().Order().SequenceEqual(firstAssignees)))
            {
                throw new ProjectException("CONTRIBUTION_DUPLICATE_TASK_INPUT", "A task has conflicting calculation inputs.", 409);
            }

            return new ContributionTaskInput(first.TaskId, first.EffectiveStatus, firstAssignees);
        }).ToArray();
        var nonCancelledTasks = uniqueTasks.Where(t => t.EffectiveStatus != ProjectTaskStatus.Cancelled).ToArray();

        foreach (var task in nonCancelledTasks)
        {
            if (task.AssigneeIds.Count == 0)
                throw new ProjectException(
                    "CONTRIBUTION_TASK_UNASSIGNED",
                    "A non-cancelled project task has no assignee; resolve it before calculating contribution.",
                    409);

            var coefficient = task.EffectiveStatus switch
            {
                ProjectTaskStatus.Completed => 1.00m,
                ProjectTaskStatus.InProgress => 0.50m,
                ProjectTaskStatus.Overdue => 0.25m,
                ProjectTaskStatus.ToDo => 0.00m,
                _ => throw new ProjectException("CONTRIBUTION_INVALID_TASK_STATUS", "A task has an unsupported status.", 409)
            };

            var assignees = task.AssigneeIds.Distinct().ToArray();
            if (assignees.Any(assignee => !participantSet.Contains(assignee)))
                warnings.Add("TASK_ASSIGNEE_NOT_IN_CYCLE_ROSTER");

            foreach (var assigneeId in assignees.Where(participantSet.Contains))
            {
                var total = taskTotals[assigneeId];
                taskTotals[assigneeId] = (total.Score + coefficient, total.Count + 1);
            }
        }

        if (nonCancelledTasks.Length == 0 && uniqueTasks.Length > 0)
            warnings.Add("ALL_TASKS_CANCELLED");

        var deliverableCounts = participants.ToDictionary(id => id, _ => 0);
        foreach (var deliverable in deliverables.DistinctBy(d => d.ReportGroupId))
        {
            if (!participantSet.Contains(deliverable.SubmittedById))
            {
                warnings.Add("DELIVERABLE_SUBMITTER_NOT_IN_CYCLE_ROSTER");
                continue;
            }

            deliverableCounts[deliverable.SubmittedById]++;
        }

        var maxDeliverables = deliverableCounts.Values.Max();
        if (maxDeliverables == 0)
            warnings.Add("NO_APPROVED_DELIVERABLES");

        var peerScores = CalculatePeerScores(participants, peerEvaluations, warnings);
        var results = new List<ContributionParticipantScore>(participants.Length);

        foreach (var participantId in participants)
        {
            var taskTotal = taskTotals[participantId];
            var taskScore = taskTotal.Count == 0
                ? 0m
                : 100m * taskTotal.Score / taskTotal.Count;
            if (taskTotal.Count == 0)
                warnings.Add("PARTICIPANT_HAS_NO_ELIGIBLE_ASSIGNED_TASKS");

            var deliverableScore = maxDeliverables == 0
                ? 0m
                : 100m * deliverableCounts[participantId] / maxDeliverables;
            var peerScore = peerScores[participantId];
            var totalScore = 0.50m * taskScore + 0.30m * deliverableScore + 0.20m * peerScore;

            results.Add(new ContributionParticipantScore(
                participantId,
                decimal.Round(taskScore, 2, ScoreRounding),
                decimal.Round(deliverableScore, 2, ScoreRounding),
                decimal.Round(peerScore, 2, ScoreRounding),
                decimal.Round(totalScore, 2, ScoreRounding)));
        }

        return new ContributionCalculationOutput(results, warnings.Order(StringComparer.Ordinal).ToArray());
    }

    private static Dictionary<Guid, decimal> CalculatePeerScores(
        IReadOnlyCollection<Guid> participants,
        IReadOnlyCollection<ContributionPeerEvaluationInput> evaluations,
        ISet<string> warnings)
    {
        if (participants.Count == 1)
        {
            if (evaluations.Count != 0)
                throw new ProjectException("CONTRIBUTION_INVALID_PEER_EVALUATION", "A single-participant cycle cannot contain peer evaluations.");

            warnings.Add("PEER_EVALUATION_NOT_APPLICABLE");
            return new Dictionary<Guid, decimal> { [participants.Single()] = 0m };
        }

        var participantSet = participants.ToHashSet();
        var pairs = new HashSet<(Guid Evaluator, Guid Evaluatee)>();
        var receivedTotals = participants.ToDictionary(id => id, _ => (Total: 0, Count: 0));

        foreach (var evaluation in evaluations)
        {
            if (!participantSet.Contains(evaluation.EvaluatorUserId) ||
                !participantSet.Contains(evaluation.EvaluateeUserId) ||
                evaluation.EvaluatorUserId == evaluation.EvaluateeUserId ||
                evaluation.Score is < 1 or > 5 ||
                !pairs.Add((evaluation.EvaluatorUserId, evaluation.EvaluateeUserId)))
            {
                throw new ProjectException(
                    "CONTRIBUTION_INVALID_PEER_EVALUATION",
                    "Peer evaluations must be unique, between cycle participants, non-self, and rated from 1 to 5.",
                    409);
            }

            var received = receivedTotals[evaluation.EvaluateeUserId];
            receivedTotals[evaluation.EvaluateeUserId] = (received.Total + evaluation.Score, received.Count + 1);
        }

        var requiredCount = participants.Count * (participants.Count - 1);
        if (pairs.Count != requiredCount)
            throw new ProjectException(
                "CONTRIBUTION_PEER_EVALUATIONS_INCOMPLETE",
                "Every participant must evaluate every other participant before calculation.",
                409);

        return receivedTotals.ToDictionary(
            pair => pair.Key,
            pair =>
            {
                var average = (decimal)pair.Value.Total / pair.Value.Count;
                return 100m * (average - 1m) / 4m;
            });
    }
}
