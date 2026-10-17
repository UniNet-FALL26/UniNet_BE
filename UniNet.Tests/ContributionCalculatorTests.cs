using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using Xunit;

namespace UniNet.Tests;

public sealed class ContributionCalculatorTests
{
    [Fact]
    public void CalculateUsesFixedWeightsAndDoesNotRedistributeSingleParticipantPeerWeight()
    {
        var participantId = Guid.NewGuid();

        var result = ContributionCalculator.Calculate(
            [participantId],
            [new ContributionTaskInput(Guid.NewGuid(), ProjectTaskStatus.InProgress, [participantId])],
            [],
            []);

        var score = Assert.Single(result.Results);
        Assert.Equal(50.00m, score.TaskExecutionScore);
        Assert.Equal(0.00m, score.ApprovedDeliverablesScore);
        Assert.Equal(0.00m, score.PeerEvaluationScore);
        Assert.Equal(25.00m, score.TotalScore);
        Assert.Contains("PEER_EVALUATION_NOT_APPLICABLE", result.Warnings);
    }

    [Theory]
    [InlineData(ProjectTaskStatus.Completed, 100)]
    [InlineData(ProjectTaskStatus.InProgress, 50)]
    [InlineData(ProjectTaskStatus.Overdue, 25)]
    [InlineData(ProjectTaskStatus.ToDo, 0)]
    [InlineData(ProjectTaskStatus.Cancelled, 0)]
    public void CalculateMapsTaskStatusesToTheSpecifiedCoefficients(ProjectTaskStatus status, decimal expectedScore)
    {
        var participantId = Guid.NewGuid();
        var result = ContributionCalculator.Calculate(
            [participantId],
            [new ContributionTaskInput(Guid.NewGuid(), status, [participantId])],
            [],
            []);

        Assert.Equal(expectedScore, Assert.Single(result.Results).TaskExecutionScore);
    }

    [Fact]
    public void CalculateAppliesDeliverablePeerAndTaskWeightsWithoutRedistribution()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var result = ContributionCalculator.Calculate(
            [first, second],
            [new ContributionTaskInput(Guid.NewGuid(), ProjectTaskStatus.Completed, [first, second])],
            [
                new ContributionDeliverableInput(Guid.NewGuid(), first),
                new ContributionDeliverableInput(Guid.NewGuid(), first),
                new ContributionDeliverableInput(Guid.NewGuid(), second)
            ],
            [
                new ContributionPeerEvaluationInput(first, second, 5),
                new ContributionPeerEvaluationInput(second, first, 4)
            ]);

        var firstScore = Assert.Single(result.Results, score => score.UserId == first);
        var secondScore = Assert.Single(result.Results, score => score.UserId == second);
        Assert.Equal(100m, firstScore.ApprovedDeliverablesScore);
        Assert.Equal(50m, secondScore.ApprovedDeliverablesScore);
        Assert.Equal(75m, firstScore.PeerEvaluationScore);
        Assert.Equal(100m, secondScore.PeerEvaluationScore);
        Assert.Equal(95m, firstScore.TotalScore);
        Assert.Equal(85m, secondScore.TotalScore);
    }

    [Fact]
    public void CalculateBlocksWhenRequiredPeerEvaluationsAreMissing()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var error = Assert.Throws<ProjectException>(() => ContributionCalculator.Calculate(
            [first, second], [], [], []));

        Assert.Equal("CONTRIBUTION_PEER_EVALUATIONS_INCOMPLETE", error.Code);
        Assert.Equal(409, error.Status);
    }

    [Fact]
    public void CalculateBlocksNonCancelledTasksWithoutAssignees()
    {
        var error = Assert.Throws<ProjectException>(() => ContributionCalculator.Calculate(
            [Guid.NewGuid()],
            [new ContributionTaskInput(Guid.NewGuid(), ProjectTaskStatus.ToDo, [])],
            [],
            []));

        Assert.Equal("CONTRIBUTION_TASK_UNASSIGNED", error.Code);
    }

    [Fact]
    public void CalculateCountsATaskAtMostOncePerParticipant()
    {
        var participantId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var result = ContributionCalculator.Calculate(
            [participantId],
            [
                new ContributionTaskInput(taskId, ProjectTaskStatus.Completed, [participantId]),
                new ContributionTaskInput(taskId, ProjectTaskStatus.Completed, [participantId, participantId])
            ],
            [],
            []);

        Assert.Equal(100m, Assert.Single(result.Results).TaskExecutionScore);
    }

    [Fact]
    public void EffectiveOverdueStatusUsesTheSameDeadlineRuleAsTaskResponses()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new ProjectTask
        {
            Status = ProjectTaskStatus.InProgress,
            Deadline = now.AddTicks(-1)
        };

        Assert.Equal(ProjectTaskStatus.Overdue, ProjectTaskStatusResolver.Resolve(task, now));
        task.Status = ProjectTaskStatus.Completed;
        Assert.Equal(ProjectTaskStatus.Completed, ProjectTaskStatusResolver.Resolve(task, now));
    }
}
