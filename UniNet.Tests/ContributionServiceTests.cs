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
    public async Task HistoricalFinalResultsRemainReadableWithoutTaskTables()
    {
        await using var f = await Fixture.CreateAsync();
        var cycle = await f.Db.ContributionCycles.SingleAsync();
        cycle.Status = ContributionCycleStatus.Finalized;
        cycle.FinalizedAt = DateTimeOffset.UtcNow;
        var run = new ContributionCalculationRun { Cycle = cycle, RunNumber = 1,
            CalculatedById = f.Leader.Id, Status = ContributionCalculationRunStatus.Confirmed };
        run.Results.Add(new() { CycleId = cycle.Id, UserId = f.Leader.Id,
            TaskExecutionScore = 80, ApprovedDeliverablesScore = 70, PeerEvaluationScore = 60, TotalScore = 73 });
        f.Db.ContributionCalculationRuns.Add(run);
        await f.Db.SaveChangesAsync();
        var result = await new ContributionService(f.Db).GetFinalResultsAsync(f.Project.Id, f.LeaderAccount.Id, default);
        Assert.Equal(73m, Assert.Single(result.Results).TotalScore);
    }
    [Fact]
    public async Task RemovedTaskInputsCannotProduceFabricatedContributionResults()
    {
        await using var f = await Fixture.CreateAsync();
        await f.AddPeerEvaluationsAsync();
        var error = await Assert.ThrowsAsync<ProjectException>(() =>
            new ContributionService(f.Db).CalculateAsync(f.Project.Id, f.LeaderAccount.Id, default));
        Assert.Equal("CONTRIBUTION_INPUTS_RETIRED", error.Code);
        Assert.Equal(410, error.Status);
        Assert.Empty(await f.Db.ContributionCalculationRuns.ToListAsync());
    }

    [Fact]
    public async Task CalculationStillRequiresLeaderAndReportsRetiredInputs()
    {
        await using var f = await Fixture.CreateAsync();
        var service = new ContributionService(f.Db);

        var forbidden = await Assert.ThrowsAsync<ProjectException>(() =>
            service.CalculateAsync(f.Project.Id, f.MemberAccount.Id, default));
        Assert.Equal(403, forbidden.Status);
        var incomplete = await Assert.ThrowsAsync<ProjectException>(() =>
            service.CalculateAsync(f.Project.Id, f.LeaderAccount.Id, default));
        Assert.Equal("CONTRIBUTION_INPUTS_RETIRED", incomplete.Code);
        Assert.Empty(await f.Db.ContributionCalculationRuns.ToListAsync());
        Assert.Equal(ContributionCycleStatus.CollectingEvaluations,
            (await f.Db.ContributionCycles.SingleAsync()).Status);
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
