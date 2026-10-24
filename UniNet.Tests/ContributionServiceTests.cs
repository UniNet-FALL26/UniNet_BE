using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class ContributionServiceTests
{
    [Fact]
    public async Task PeerEvaluationHistoryRevisionAndFinalizationPreserveRunHistory()
    {
        await using var f = await Fixture.CreateAsync();
        var service = new ContributionService(f.Db);
        await f.AddCompletedTaskAsync();

        await service.UpsertPeerEvaluationAsync(f.Project.Id, f.Member.Id, f.LeaderAccount.Id,
            new(5, "private leader comment"), default);
        await service.UpsertPeerEvaluationAsync(f.Project.Id, f.Leader.Id, f.MemberAccount.Id,
            new(4, "private member comment"), default);
        Assert.Equal(2, await f.Db.ContributionPeerEvaluationHistory.CountAsync());

        var ownEvaluations = await service.GetMyPeerEvaluationsAsync(f.Project.Id, f.MemberAccount.Id, default);
        Assert.Equal("private member comment", Assert.Single(ownEvaluations).Comment);

        var firstRun = await service.CalculateAsync(f.Project.Id, f.LeaderAccount.Id, default);
        Assert.Equal(1, firstRun.RunNumber);
        Assert.Equal(ContributionCalculationRunStatus.PendingReview, firstRun.Status);
        await Assert.ThrowsAsync<ProjectException>(() => service.UpsertPeerEvaluationAsync(
            f.Project.Id, f.Leader.Id, f.MemberAccount.Id, new(5, null), default));
        await Assert.ThrowsAsync<ProjectException>(() => service.GetFinalResultsAsync(
            f.Project.Id, f.MemberAccount.Id, default));
        await Assert.ThrowsAsync<ProjectException>(() => service.GetRunAsync(
            f.Project.Id, firstRun.RunId, f.MemberAccount.Id, default));

        var missingFeedback = await Assert.ThrowsAsync<ProjectException>(() => service.ReviewRunAsync(
            f.Project.Id, firstRun.RunId, f.LeaderAccount.Id, new("RequestRevision", null), default));
        Assert.Equal("CONTRIBUTION_REVISION_FEEDBACK_REQUIRED", missingFeedback.Code);
        var revision = await service.ReviewRunAsync(f.Project.Id, firstRun.RunId, f.LeaderAccount.Id,
            new("RequestRevision", "Please reassess the peer ratings."), default);
        Assert.Equal(ContributionCycleStatus.CollectingEvaluations, revision.CycleStatus);
        var reviewedRun = await service.GetRunAsync(f.Project.Id, firstRun.RunId, f.LeaderAccount.Id, default);
        Assert.Equal("Please reassess the peer ratings.", Assert.Single(reviewedRun.ReviewHistory).Feedback);

        await service.UpsertPeerEvaluationAsync(f.Project.Id, f.Leader.Id, f.MemberAccount.Id,
            new(5, "updated private comment"), default);
        Assert.Equal(3, await f.Db.ContributionPeerEvaluationHistory.CountAsync());
        var secondRun = await service.CalculateAsync(f.Project.Id, f.LeaderAccount.Id, default);
        Assert.Equal(2, secondRun.RunNumber);
        await service.ReviewRunAsync(f.Project.Id, secondRun.RunId, f.LeaderAccount.Id, new("Confirm", null), default);
        Assert.Single((await service.GetRunAsync(f.Project.Id, secondRun.RunId, f.LeaderAccount.Id, default)).ReviewHistory);

        var finalResults = await service.GetFinalResultsAsync(f.Project.Id, f.MemberAccount.Id, default);
        Assert.Equal(2, finalResults.Results.Count);
        Assert.All(finalResults.Results, result => Assert.InRange(result.TotalScore, 0m, 100m));
        Assert.DoesNotContain("private", System.Text.Json.JsonSerializer.Serialize(finalResults), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ContributionCalculationRunStatus.RevisionRequested,
            (await f.Db.ContributionCalculationRuns.SingleAsync(r => r.Id == firstRun.RunId)).Status);
        Assert.Equal(ContributionCalculationRunStatus.Confirmed,
            (await f.Db.ContributionCalculationRuns.SingleAsync(r => r.Id == secondRun.RunId)).Status);
        await Assert.ThrowsAsync<ProjectException>(() => service.UpsertPeerEvaluationAsync(
            f.Project.Id, f.Leader.Id, f.MemberAccount.Id, new(3, null), default));
    }

    [Fact]
    public async Task CalculationRequiresCompletePeerEvaluationAndLeaderAuthorization()
    {
        await using var f = await Fixture.CreateAsync();
        var service = new ContributionService(f.Db);
        await f.AddCompletedTaskAsync();

        var forbidden = await Assert.ThrowsAsync<ProjectException>(() =>
            service.CalculateAsync(f.Project.Id, f.MemberAccount.Id, default));
        Assert.Equal(403, forbidden.Status);
        var incomplete = await Assert.ThrowsAsync<ProjectException>(() =>
            service.CalculateAsync(f.Project.Id, f.LeaderAccount.Id, default));
        Assert.Equal("CONTRIBUTION_PEER_EVALUATIONS_INCOMPLETE", incomplete.Code);
        Assert.Empty(await f.Db.ContributionCalculationRuns.ToListAsync());
        Assert.Equal(ContributionCycleStatus.CollectingEvaluations,
            (await f.Db.ContributionCycles.SingleAsync()).Status);
    }

    [Fact]
    public async Task SingleParticipantCycleCalculatesWithZeroPeerScore()
    {
        await using var f = await Fixture.CreateAsync();
        var cycle = await f.Db.ContributionCycles.Include(c => c.Participants).SingleAsync();
        f.Db.ContributionParticipants.Remove(cycle.Participants.Single(p => !p.IsLeader));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var run = await new ContributionService(f.Db).CalculateAsync(
            f.Project.Id, f.LeaderAccount.Id, default);

        var leader = Assert.Single(run.Results);
        Assert.True(leader.UserId != Guid.Empty);
        Assert.Equal(0m, leader.PeerEvaluationScore);
    }

    [Fact]
    public async Task CalculationLocksTaskAndReportMutationsUntilRevisionIsRequested()
    {
        await using var f = await Fixture.CreateAsync();
        var task = await f.AddCompletedTaskAsync();
        f.Db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = f.Project.Id,
            UserId = f.Member.Id,
            Role = "Member",
            Status = ProjectMemberStatus.Active,
            JoinedAt = f.Project.CreatedAt,
            CreatedAt = f.Project.CreatedAt,
            UpdatedAt = f.Project.CreatedAt
        });
        await f.Db.SaveChangesAsync();
        await f.AddPeerEvaluationsAsync();
        var contribution = new ContributionService(f.Db);
        var run = await contribution.CalculateAsync(f.Project.Id, f.LeaderAccount.Id, default);
        var duplicate = await Assert.ThrowsAsync<ProjectException>(() =>
            contribution.CalculateAsync(f.Project.Id, f.LeaderAccount.Id, default));
        Assert.Equal("CONTRIBUTION_CYCLE_NOT_OPEN", duplicate.Code);
        Assert.Single(await f.Db.ContributionCalculationRuns.ToListAsync());
        var taskService = new ProjectTaskService(
            f.Db,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        var taskError = await Assert.ThrowsAsync<ProjectException>(() => taskService.UpdateProjectTaskAsync(
            f.Project.Id,
            task.Id,
            f.LeaderAccount.Id,
            new UpdateProjectTaskRequest(Title: "Changed after calculation"),
            default));
        Assert.Equal("CONTRIBUTION_INPUTS_LOCKED", taskError.Code);

        var memberStatusError = await Assert.ThrowsAsync<ProjectException>(() => taskService.UpdateTaskStatusAsync(
            f.Project.Id,
            task.Id,
            f.MemberAccount.Id,
            ProjectTaskStatus.InProgress,
            default));
        Assert.Equal("CONTRIBUTION_INPUTS_LOCKED", memberStatusError.Code);

        var reportError = await Assert.ThrowsAsync<ProjectException>(() => taskService.SubmitTaskReportAsync(
            f.Project.Id,
            task.Id,
            f.MemberAccount.Id,
            new SubmitTaskReportRequest("Late report", []),
            default));
        Assert.Equal("CONTRIBUTION_INPUTS_LOCKED", reportError.Code);
        Assert.Equal(0, await f.Db.TaskReports.CountAsync());
        Assert.Equal("Build feature", (await f.Db.ProjectTasks.SingleAsync()).Title);

        await contribution.ReviewRunAsync(
            f.Project.Id, run.RunId, f.LeaderAccount.Id,
            new("RequestRevision", "Update the contribution inputs."), default);
        await taskService.UpdateProjectTaskAsync(
            f.Project.Id,
            task.Id,
            f.LeaderAccount.Id,
            new UpdateProjectTaskRequest(Title: "Changed during revision"),
            default);
    }

    [Fact]
    public async Task ConfirmationRejectsRunWhenCalculationInputsChangedOutsideMutationServices()
    {
        await using var f = await Fixture.CreateAsync();
        var task = await f.AddCompletedTaskAsync();
        await f.AddPeerEvaluationsAsync();
        var service = new ContributionService(f.Db);
        var run = await service.CalculateAsync(f.Project.Id, f.LeaderAccount.Id, default);

        task.Status = ProjectTaskStatus.InProgress;
        await f.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ProjectException>(() => service.ReviewRunAsync(
            f.Project.Id, run.RunId, f.LeaderAccount.Id, new("Confirm", null), default));

        Assert.Equal("CONTRIBUTION_INPUTS_CHANGED", error.Code);
        Assert.Equal(ContributionCalculationRunStatus.PendingReview,
            (await f.Db.ContributionCalculationRuns.SingleAsync(r => r.Id == run.RunId)).Status);
        Assert.Equal(ContributionCycleStatus.AwaitingReview,
            (await f.Db.ContributionCycles.SingleAsync()).Status);
    }

    [Fact]
    public async Task ConfirmationRejectsRunWhenReportApprovalChangesAfterCalculation()
    {
        await using var f = await Fixture.CreateAsync();
        await f.AddCompletedTaskAsync();
        await f.AddReportGroupAsync(f.Leader.Id, TaskReportStatus.Submitted);
        await f.AddPeerEvaluationsAsync();
        var service = new ContributionService(f.Db);
        var run = await service.CalculateAsync(f.Project.Id, f.LeaderAccount.Id, default);

        var report = await f.Db.TaskReports.SingleAsync();
        report.Status = TaskReportStatus.Approved;
        await f.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ProjectException>(() => service.ReviewRunAsync(
            f.Project.Id, run.RunId, f.LeaderAccount.Id, new("Confirm", null), default));

        Assert.Equal("CONTRIBUTION_INPUTS_CHANGED", error.Code);
    }

    [Fact]
    public async Task NonRosterMemberCannotAccessCycleSnapshot()
    {
        await using var f = await Fixture.CreateAsync();
        var cycle = await f.Db.ContributionCycles.Include(c => c.Participants).SingleAsync();
        f.Db.ContributionParticipants.Remove(cycle.Participants.Single(p => !p.IsLeader));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var error = await Assert.ThrowsAsync<ProjectException>(() =>
            new ContributionService(f.Db).GetCycleAsync(f.Project.Id, f.MemberAccount.Id, default));

        Assert.Equal(403, error.Status);
    }

    [Fact]
    public async Task TaskInputsCannotBeChangedWhileProjectIsNotActiveOrCollectingContribution()
    {
        await using var f = await Fixture.CreateAsync();
        var task = await f.AddCompletedTaskAsync();
        f.Project.Status = ProjectStatus.Pending;
        await f.Db.SaveChangesAsync();
        var taskService = new ProjectTaskService(
            f.Db,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        var error = await Assert.ThrowsAsync<ProjectException>(() => taskService.UpdateProjectTaskAsync(
            f.Project.Id,
            task.Id,
            f.LeaderAccount.Id,
            new UpdateProjectTaskRequest(Title: "Must remain blocked"),
            default));

        Assert.Equal("INVALID_PROJECT_STATE", error.Code);
        Assert.Equal("Build feature", (await f.Db.ProjectTasks.SingleAsync()).Title);
    }

    [Fact]
    public async Task CalculationCreditsLatestApprovedReportVersionsBySubmitterAndReportGroup()
    {
        await using var f = await Fixture.CreateAsync();
        var service = new ContributionService(f.Db);
        await f.AddCompletedTaskAsync();
        await f.AddReportGroupAsync(f.Leader.Id, TaskReportStatus.Approved);
        await f.AddReportGroupAsync(f.Member.Id, TaskReportStatus.Approved);
        await f.AddReportGroupAsync(f.Leader.Id, TaskReportStatus.Approved,
            (f.Member.Id, TaskReportStatus.Submitted, false));
        await f.AddReportGroupAsync(f.Leader.Id, TaskReportStatus.Approved,
            (f.Member.Id, TaskReportStatus.Submitted, true));
        await f.AddReportGroupAsync(f.Leader.Id, TaskReportStatus.Submitted,
            (f.Member.Id, TaskReportStatus.Approved, false));
        await f.AddPeerEvaluationsAsync();

        var run = await service.CalculateAsync(f.Project.Id, f.LeaderAccount.Id, default);
        var leader = Assert.Single(run.Results, result => result.UserId == f.Leader.Id);
        var member = Assert.Single(run.Results, result => result.UserId == f.Member.Id);
        Assert.Equal(50m, leader.ApprovedDeliverablesScore);
        Assert.Equal(100m, member.ApprovedDeliverablesScore);
    }

    [Fact]
    public async Task CompletedProjectWithoutCompletionSnapshotIsRejectedAsLegacy()
    {
        await using var f = await Fixture.CreateAsync();
        f.Project.CompletedAt = null;
        var cycle = await f.Db.ContributionCycles.Include(c => c.Participants).SingleAsync();
        f.Db.ContributionParticipants.RemoveRange(cycle.Participants);
        f.Db.ContributionCycles.Remove(cycle);
        await f.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ProjectException>(() =>
            new ContributionService(f.Db).GetCycleAsync(f.Project.Id, f.LeaderAccount.Id, default));

        Assert.Equal("CONTRIBUTION_LEGACY_PROJECT_REQUIRES_REVIEW", error.Code);
        Assert.Empty(await f.Db.ContributionCycles.ToListAsync());
    }

    private sealed class Fixture(SqliteConnection connection, UniNetDbContext db,
        Account leaderAccount, UserProfile leader, Account memberAccount, UserProfile member, Project project) : IAsyncDisposable
    {
        public UniNetDbContext Db { get; } = db;
        public Account LeaderAccount { get; } = leaderAccount;
        public UserProfile Leader { get; } = leader;
        public Account MemberAccount { get; } = memberAccount;
        public UserProfile Member { get; } = member;
        public Project Project { get; } = project;

        public async Task<ProjectTask> AddCompletedTaskAsync()
        {
            var task = new ProjectTask
            {
                ProjectId = Project.Id,
                CreatorId = Leader.Id,
                Title = "Build feature",
                Description = "Complete feature",
                Status = ProjectTaskStatus.Completed,
                Deadline = DateTimeOffset.UtcNow.AddDays(1)
            };
            Db.ProjectTasks.Add(task);
            Db.ProjectTaskAssignments.AddRange(
                new ProjectTaskAssignment { ProjectTaskId = task.Id, UserId = Leader.Id },
                new ProjectTaskAssignment { ProjectTaskId = task.Id, UserId = Member.Id });
            await Db.SaveChangesAsync();
            return task;
        }

        public async Task AddReportGroupAsync(
            Guid firstSubmitter,
            TaskReportStatus firstStatus,
            params (Guid Submitter, TaskReportStatus Status, bool Deleted)[] revisions)
        {
            var task = await Db.ProjectTasks.FirstAsync(t => t.ProjectId == Project.Id);
            var groupId = Guid.NewGuid();
            var submitters = new[] { (firstSubmitter, firstStatus) }
                .Concat(revisions.Select(revision => (revision.Submitter, revision.Status)))
                .ToArray();
            for (var index = 0; index < submitters.Length; index++)
            {
                var (submitter, status) = submitters[index];
                var deleted = index > 0 && revisions[index - 1].Deleted;
                Db.TaskReports.Add(new TaskReport
                {
                    ProjectTaskId = task.Id,
                    SubmittedById = submitter,
                    ReportGroupId = groupId,
                    ParentReportId = index == 0 ? null : Db.TaskReports.Local.Last().Id,
                    Version = index + 1,
                    Content = $"Report v{index + 1}",
                    Status = status,
                    SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(index),
                    UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(index),
                    IsDeleted = deleted,
                    DeletedAt = deleted ? DateTimeOffset.UtcNow : null,
                    DeletedById = deleted ? Leader.Id : null
                });
            }
            await Db.SaveChangesAsync();
        }

        public async Task AddPeerEvaluationsAsync()
        {
            var cycle = await Db.ContributionCycles.SingleAsync(c => c.ProjectId == Project.Id);
            Db.ContributionPeerEvaluations.AddRange(
                new ContributionPeerEvaluation
                {
                    CycleId = cycle.Id, EvaluatorUserId = Leader.Id, EvaluateeUserId = Member.Id, Score = 1
                },
                new ContributionPeerEvaluation
                {
                    CycleId = cycle.Id, EvaluatorUserId = Member.Id, EvaluateeUserId = Leader.Id, Score = 1
                });
            await Db.SaveChangesAsync();
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var leaderAccount = MakeAccount("leader");
            var memberAccount = MakeAccount("member");
            var leader = leaderAccount.Profile!;
            var member = memberAccount.Profile!;
            db.Accounts.AddRange(leaderAccount, memberAccount);
            var now = DateTimeOffset.UtcNow;
            var project = new Project
            {
                CreatorId = leader.Id,
                Title = "Contribution Project",
                ProjectField = "IT",
                Description = "Project under contribution evaluation",
                MemberTarget = 2,
                Status = ProjectStatus.Completed,
                CreatedAt = now.AddDays(-2),
                UpdatedAt = now,
                CompletedAt = now
            };
            db.Projects.Add(project);
            var cycle = new ContributionCycle
            {
                ProjectId = project.Id,
                OpenedById = leader.Id,
                OpenedAt = now,
                FormulaVersion = "1",
                Participants =
                [
                    new ContributionParticipant
                    {
                        UserId = leader.Id, RoleAtSnapshot = "Creator",
                        MembershipStatusAtSnapshot = ProjectMemberStatus.Active,
                        JoinedAt = project.CreatedAt, IsLeader = true
                    },
                    new ContributionParticipant
                    {
                        UserId = member.Id, RoleAtSnapshot = "Member",
                        MembershipStatusAtSnapshot = ProjectMemberStatus.Active,
                        JoinedAt = now.AddDays(-1)
                    }
                ]
            };
            db.ContributionCycles.Add(cycle);
            await db.SaveChangesAsync();
            return new(connection, db, leaderAccount, leader, memberAccount, member, project);
        }

        private static Account MakeAccount(string name)
        {
            var account = new Account
            {
                Email = $"{name}@contribution.test",
                Role = AccountRole.Student,
                Status = AccountStatus.Active,
                Profile = new UserProfile
                {
                    FullName = name,
                    Nickname = name,
                    IsVerified = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                }
            };
            account.Profile.AccountId = account.Id;
            return account;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
