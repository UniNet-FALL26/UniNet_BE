using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using UniNet.Application;
using UniNet.Application.DTOs.Projects;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class ProjectHubTests
{
    [Fact]
    public async Task ExternalIsDefaultAndDoesNotCreateRecruitmentOrPretendOwnerIsLeader()
    {
        await using var f = await Fixture.Create();
        var result = await f.Service.CreateAsync(f.Owner.Id, new() { Title = "Game 2D", Description = "A real personal game", Technologies = ["Unity", "C#"] }, default);
        Assert.Equal(1, result.Origin);
        Assert.Equal("personal", result.Mode);
        Assert.Null(result.Recruitment);
        Assert.Empty(result.Members);
        Assert.Null(result.ChatRoomId);
        Assert.Empty(await f.Db.ProjectModerations.ToListAsync());
        Assert.Empty(await f.Db.ProjectVerifications.ToListAsync());
        Assert.Empty(await f.Db.ContributionCycles.ToListAsync());
        Assert.Equal(["Unity", "C#"], result.Technologies);
    }

    [Fact]
    public async Task PrivateProjectsAreScopedToOwnerAndActiveMembers()
    {
        await using var f = await Fixture.Create();
        var project = await f.Internal();
        Assert.True(project.IsOwner);
        Assert.Equal("Leader", Assert.Single(project.Members).Role);
        var denial = await Assert.ThrowsAsync<ProjectException>(() => f.Service.GetAsync(project.Id, f.Other.Id, default));
        Assert.Equal(404, denial.Status);
        Assert.Empty((await f.Service.MineAsync(f.Other.Id, 1, 12, null, default)).Items);
        var forbidden = await Assert.ThrowsAsync<ProjectException>(() => f.Service.UpdateAsync(project.Id, f.Other.Id,
            new() { Title = "Hijack" }, default));
        Assert.Equal(403, forbidden.Status);
    }

    [Fact]
    public async Task DetailsAndOwnReviewRoundTripAndDoNotLeakPrivateReview()
    {
        await using var f = await Fixture.Create();
        var p = await f.Internal();
        var updated = await f.Service.UpdateAsync(p.Id, f.Owner.Id, new() { Visibility = 1,
            Details = new() { Description = "Introduction", Features = ["Save game"],
                StartDate = new(2026, 11, 1), EndDate = new(2026, 12, 1),
                Results = new() { Problem = "Learning is difficult", Objectives = ["Play to learn"] },
                Links = [new("GitHub", "https://github.com/example/game")] } }, default);
        Assert.Equal("Introduction", updated.Details.Description);
        Assert.Equal("Save game", Assert.Single(updated.Details.Features));
        await f.Service.ReviewAsync(p.Id, f.Owner.Id, new("Private difficulty", "Private lesson"), default);
        f.Db.ChangeTracker.Clear();
        Assert.Equal("Private lesson", (await f.Service.GetAsync(p.Id, f.Owner.Id, default)).MyReview!.LessonsLearned);
        Assert.Null((await f.Service.GetAsync(p.Id, f.Other.Id, default)).MyReview);
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.ReviewAsync(p.Id, f.Other.Id, new("x", "y"), default));
    }

    [Fact]
    public async Task ExplicitRecruitmentCreatesCommunityAndAcceptedRequestHistoryIsPreserved()
    {
        await using var f = await Fixture.Create();
        var p = await f.Internal();
        var recruitment = new SaveHubRecruitmentRequest { Status = 1, Deadline = DateTimeOffset.UtcNow.AddDays(3),
            Positions = [new() { Id = "backend", Name = "Backend developer", Quantity = 1, Technologies = ["C#"] }] };
        var opened = await f.Service.RecruitmentAsync(p.Id, f.Owner.Id, recruitment, default);
        Assert.Equal("community", opened.Mode);
        Assert.Equal(1, opened.Visibility);
        Assert.Equal("backend", Assert.Single(opened.Recruitment!.Positions).Id);
        Assert.Single((await f.Service.DiscoverAsync(f.Other.Id, 1, 12, null, default)).Items);
        var request = await f.Service.RequestAsync(p.Id, f.Other.Id, new() { PositionId = "backend", Message = "I can help" }, default);
        var accepted = await f.Service.DecideAsync(request.Id, f.Owner.Id, new("accept"), default);
        Assert.Equal(1, accepted.Status);
        Assert.Equal(2, await f.Db.ProjectMembers.CountAsync());
        var member = await f.Db.ProjectMembers.SingleAsync(m => m.UserId == f.Other.Profile!.Id);
        Assert.Equal(request.Id, member.JoinRequestId);
        Assert.Equal("backend", member.PositionId);
        Assert.Equal(1, await f.Db.ProjectJoinRequests.CountAsync());
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.DecideAsync(request.Id, f.Owner.Id, new("accept"), default));
        var closed = await f.Service.RecruitmentAsync(p.Id, f.Owner.Id, recruitment with { Status = 2 }, default);
        Assert.Equal("community", closed.Mode);
        Assert.Empty((await f.Service.DiscoverAsync(f.Other.Id, 1, 12, null, default)).Items);
    }

    [Fact]
    public async Task InvitationCanOnlyBeAcceptedByRecipientAndCannotOverfillPosition()
    {
        await using var f = await Fixture.Create();
        var p = await f.Internal();
        await f.Service.RecruitmentAsync(p.Id, f.Owner.Id, new() { Status = 1,
            Positions = [new() { Id = "design", Name = "Designer", Quantity = 1 }] }, default);
        var invite = await f.Service.RequestAsync(p.Id, f.Owner.Id,
            new() { RequestType = 1, InvitedUserId = f.Other.Profile!.Id, PositionId = "design" }, default);
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.DecideAsync(invite.Id, f.Owner.Id, new("accept"), default));
        await f.Service.DecideAsync(invite.Id, f.Other.Id, new("accept"), default);
        var competing = await Assert.ThrowsAsync<ProjectException>(() => f.Service.RequestAsync(p.Id, f.Third.Id,
            new() { PositionId = "design" }, default));
        Assert.Equal("POSITION_FULL", competing.Code);
        Assert.Equal(2, await f.Db.ProjectMembers.CountAsync());
    }

    [Fact]
    public async Task CompletionClosesRecruitmentAndExternalCannotBecomeCommunity()
    {
        await using var f = await Fixture.Create();
        var external = await f.Service.CreateAsync(f.Owner.Id, new() { Title = "Outside", Description = "External" }, default);
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.RecruitmentAsync(external.Id, f.Owner.Id,
            new() { Status = 1, Positions = [new() { Id = "one", Name = "Developer", Quantity = 1 }] }, default));
        var p = await f.Internal();
        await f.Service.RecruitmentAsync(p.Id, f.Owner.Id, new() { Status = 1,
            Positions = [new() { Id = "one", Name = "Developer", Quantity = 1 }] }, default);
        var completed = await f.Service.CompleteAsync(p.Id, f.Owner.Id, default);
        Assert.Equal(2, completed.Status);
        Assert.NotNull(completed.Details.CompletedAt);
        Assert.Equal(2, completed.Recruitment!.Status);
        Assert.Empty(await f.Db.ContributionCycles.ToListAsync());
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.RecruitmentAsync(p.Id, f.Owner.Id,
            new() { Status = 1, Positions = [new() { Id = "one", Name = "Developer", Quantity = 1 }] }, default));
    }

    [Fact]
    public async Task RecruitmentDeadlineExpiresOnlyPendingRequestsAndPreservesAcceptedHistory()
    {
        await using var f = await Fixture.Create();
        var project = await f.Internal();
        var now = DateTimeOffset.UtcNow;
        await f.Service.RecruitmentAsync(project.Id, f.Owner.Id, new() { Status = 1,
            Deadline = now.AddHours(1), Positions = [new() { Id = "developer", Name = "Developer", Quantity = 2 }] }, default);
        var accepted = await f.Service.RequestAsync(project.Id, f.Other.Id, new() { PositionId = "developer" }, default);
        await f.Service.DecideAsync(accepted.Id, f.Owner.Id, new("accept"), default);
        var pending = await f.Service.RequestAsync(project.Id, f.Third.Id, new() { PositionId = "developer" }, default);
        await new ProjectRecruitmentTasks(f.Db).RunAsync(now.AddHours(2));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(ProjectJoinRequestStatus.Accepted, (await f.Db.ProjectJoinRequests.SingleAsync(r => r.Id == accepted.Id)).Status);
        Assert.Equal(ProjectJoinRequestStatus.Expired, (await f.Db.ProjectJoinRequests.SingleAsync(r => r.Id == pending.Id)).Status);
        Assert.Equal(2, await f.Db.ProjectMembers.CountAsync(m => m.Status == ProjectMemberStatus.Active));
        var closed = await f.Service.GetAsync(project.Id, f.Owner.Id, default);
        Assert.Equal("community", closed.Mode);
        Assert.Equal(2, closed.Recruitment!.Status);
    }

    [Fact]
    public async Task DangerousUrlsAndInvalidDatesAreRejectedBeforeSaving()
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.CreateAsync(f.Owner.Id,
            new() { Title = "Bad", Description = "Bad", LogoUrl = "javascript:alert(1)" }, default));
        Assert.Empty(await f.Db.Projects.ToListAsync());
        var p = await f.Internal();
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.UpdateAsync(p.Id, f.Owner.Id,
            new() { Details = new() { StartDate = new(2026, 12, 1), EndDate = new(2026, 11, 1) } }, default));
    }

    private sealed class Fixture(SqliteConnection connection, UniNetDbContext db, Account owner, Account other, Account third) : IAsyncDisposable
    {
        public UniNetDbContext Db => db;
        public Account Owner => owner;
        public Account Other => other;
        public Account Third => third;
        public ProjectHubService Service { get; } = new(db);
        public Task<HubProject> Internal() => Service.CreateAsync(owner.Id,
            new() { Title = "Personal", Description = "My project", Origin = 0, Status = 1 }, default);
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection)
                .ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>().Options);
            await db.Database.EnsureCreatedAsync();
            Account Make(string name) => new() { Email = name + "@hub.test", Role = AccountRole.Student,
                Profile = new() { FullName = name, Nickname = name } };
            var owner = Make("Owner"); var other = Make("Other"); var third = Make("Third");
            db.Accounts.AddRange(owner, other, third); await db.SaveChangesAsync();
            return new(connection, db, owner, other, third);
        }
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
