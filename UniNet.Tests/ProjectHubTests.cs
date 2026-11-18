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
    public async Task NewProjectTimelineUsesCreationDayAndSeparateEndDates()
    {
        await using var f = await Fixture.Create();
        var expected = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
        var p = await f.Service.CreateAsync(f.Owner.Id, new() { Title = "Timeline", Description = "Dates", ExpectedEndDate = expected }, default);
        var start = DateOnly.FromDateTime(p.CreatedAt.ToOffset(TimeSpan.FromHours(7)).DateTime);
        Assert.Equal(start, p.Details.StartDate); Assert.True(p.Details.StartDateIsCreationDate);
        Assert.Equal(expected, p.Details.EndDate); Assert.Null(p.Details.ActualEndDate);
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.UpdateAsync(p.Id, f.Other.Id,
            new() { Timeline = new(start, expected, null) }, default));
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.UpdateAsync(p.Id, f.Owner.Id,
            new() { Timeline = new(start.AddDays(-1), expected, null) }, default));
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.UpdateAsync(p.Id, f.Owner.Id,
            new() { Timeline = new(start, start.AddDays(-1), null) }, default));
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.UpdateAsync(p.Id, f.Owner.Id,
            new() { Timeline = new(start, expected, start.AddDays(2)) }, default));
        var saved = await f.Service.UpdateAsync(p.Id, f.Owner.Id, new() { Timeline = new(start, expected.AddDays(1), start) }, default);
        Assert.Equal(start, saved.Details.ActualEndDate); Assert.Equal(1, saved.Status);
        Assert.Null((await f.Db.Projects.SingleAsync()).CompletedAt);
        f.Db.ChangeTracker.Clear();
        var reloaded = await f.Service.GetAsync(p.Id, f.Owner.Id, default);
        Assert.Equal(expected.AddDays(1), reloaded.Details.EndDate); Assert.Equal(start, reloaded.Details.ActualEndDate);
    }

    [Fact]
    public async Task ExistingProjectsKeepManualDatesAndOtherPatchesPreserveTimeline()
    {
        await using var f = await Fixture.Create();
        var p = await f.Internal();
        var stored = await f.Db.Projects.Include(p => p.Details).SingleAsync();
        if (stored.Details != null) f.Db.ProjectDetails.Remove(stored.Details);
        stored.Details = null; await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var legacy = await f.Service.GetAsync(p.Id, f.Owner.Id, default);
        Assert.Null(legacy.Details.StartDate); Assert.Null(legacy.Details.EndDate);
        Assert.Null(legacy.Details.ActualEndDate); Assert.False(legacy.Details.StartDateIsCreationDate);
        var start = new DateOnly(2020, 1, 1); var expected = start.AddMonths(2); var actual = start.AddMonths(3);
        var saved = await f.Service.UpdateAsync(p.Id, f.Owner.Id, new() { Timeline = new(start, expected, actual) }, default);
        Assert.Equal(start, saved.Details.StartDate); Assert.Equal(expected, saved.Details.EndDate); Assert.Equal(actual, saved.Details.ActualEndDate);
        saved = await f.Service.UpdateAsync(p.Id, f.Owner.Id, new() { Title = "Changed" }, default);
        Assert.Equal(actual, saved.Details.ActualEndDate);
        saved = await f.Service.UpdateAsync(p.Id, f.Owner.Id, new() { Timeline = new(null, null, null) }, default);
        Assert.Null(saved.Details.StartDate); Assert.Null(saved.Details.EndDate); Assert.Null(saved.Details.ActualEndDate);
    }

    [Fact]
    public async Task NewProjectCannotHaveAnExpectedEndBeforeCreationDay()
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.CreateAsync(f.Owner.Id,
            new() { Title = "Bad dates", Description = "Dates", ExpectedEndDate = new(2000, 1, 1) }, default));
        Assert.Empty(await f.Db.Projects.ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompletionOnlyAutofillsActualDateForProjectsWithAutomaticStart(bool automatic)
    {
        await using var f = await Fixture.Create();
        var p = await f.Internal();
        (await f.Db.ProjectDetails.SingleAsync()).StartDateIsCreationDate = automatic;
        await f.Db.SaveChangesAsync();
        var completed = await f.Service.CompleteAsync(p.Id, f.Owner.Id, default);
        Assert.Equal(automatic ? completed.Details.StartDate : null, completed.Details.ActualEndDate);
        Assert.NotNull(completed.Details.CompletedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CreationAlwaysStartsActiveRegardlessOfRequestedStatus(short requestedStatus)
    {
        await using var f = await Fixture.Create();
        var result = await f.Service.CreateAsync(f.Owner.Id,
            new() { Title = "New project", Description = "Initial status", Status = requestedStatus }, default);
        Assert.Equal((short)ProjectStatus.Active, result.Status);
        Assert.Null(result.Details.CompletedAt);
        var stored = await f.Db.Projects.SingleAsync();
        Assert.Equal(ProjectStatus.Active, stored.Status);
        Assert.Null(stored.CompletedAt);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public async Task CompletedExistingProjectCanInviteFriendsWithoutReopening(short status, bool allowed)
    {
        await using var f = await Fixture.Create();
        var p = await f.Service.CreateAsync(f.Owner.Id, new() { Title = "Existing project", Description = "Completed work", Status = 2 }, default);
        (await f.Db.Projects.SingleAsync()).Status = (ProjectStatus)status;
        await f.Db.SaveChangesAsync(); await f.Befriend(f.Other);
        Task<HubRequest> Send() => f.Service.RequestAsync(p.Id, f.Owner.Id,
            new() { RequestType = 1, InvitedUserId = f.Other.Profile!.Id, Role = "Backend developer" }, default);
        if (!allowed) { await Assert.ThrowsAsync<ProjectException>(() => Send()); return; }
        var invite = await Send();
        Assert.False(await f.Db.ProjectMembers.AnyAsync(m => m.UserId == f.Other.Profile!.Id));
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.DecideAsync(invite.Id, f.Owner.Id, new("accept"), default));
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.RequestAsync(p.Id, f.Third.Id, new() { PositionId = "backend" }, default));
        await f.Service.DecideAsync(invite.Id, f.Other.Id, new("accept"), default);
        var member = await f.Db.ProjectMembers.SingleAsync(m => m.UserId == f.Other.Profile!.Id);
        Assert.Equal("Backend developer", member.Role); Assert.Equal(invite.Id, member.JoinRequestId);
        var saved = await f.Service.GetAsync(p.Id, f.Owner.Id, default);
        Assert.Equal(2, saved.Status);
        Assert.Equal(p.Details.CompletedAt, saved.Details.CompletedAt); Assert.Null(saved.Recruitment);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompletingExistingProjectPreservesFriendInvitation(bool patch)
    {
        await using var f = await Fixture.Create();
        var p = await f.Service.CreateAsync(f.Owner.Id, new() { Title = "Existing", Description = "External" }, default);
        await f.Befriend(f.Other);
        var invite = await f.Service.RequestAsync(p.Id, f.Owner.Id,
            new() { RequestType = 1, InvitedUserId = f.Other.Profile!.Id, Role = "Designer" }, default);
        if (patch) await f.Service.UpdateAsync(p.Id, f.Owner.Id, new() { Status = 2 }, default);
        else await f.Service.CompleteAsync(p.Id, f.Owner.Id, default);
        Assert.Equal(ProjectJoinRequestStatus.Pending, (await f.Db.ProjectJoinRequests.SingleAsync()).Status);
        await f.Service.DecideAsync(invite.Id, f.Other.Id, new("accept"), default);
    }

    [Fact]
    public async Task NewProjectsStartActiveWithOwnerAndWithoutRecruitment()
    {
        await using var f = await Fixture.Create();
        var result = await f.Service.CreateAsync(f.Owner.Id, new() { Title = "Game 2D", Description = "A real personal game", Technologies = ["Unity", "C#"] }, default);
        Assert.Equal(1, result.Status);
        Assert.Null(result.Recruitment);
        Assert.Equal("Leader", Assert.Single(result.Members).Role);
        Assert.Null(result.ChatRoomId);
        Assert.Empty(await f.Db.ProjectModerations.ToListAsync());
        Assert.Empty(await f.Db.ProjectVerifications.ToListAsync());
        Assert.Empty(await f.Db.ContributionCycles.ToListAsync());
        Assert.Equal(["Unity", "C#"], result.Technologies);
    }

    [Fact]
    public async Task LegacyPrivateProjectsAreReadableWithoutGrantingMembershipOrEditRights()
    {
        await using var f = await Fixture.Create();
        var project = await f.Internal();
        Assert.True(project.IsOwner);
        Assert.Equal("Leader", Assert.Single(project.Members).Role);
        (await f.Db.Projects.SingleAsync()).Visibility = ProjectVisibility.Private;
        await f.Db.SaveChangesAsync();
        var viewed = await f.Service.GetAsync(project.Id, f.Other.Id, default);
        Assert.False(viewed.IsOwner);
        Assert.False(viewed.IsMember);
        Assert.Empty((await f.Service.MineAsync(f.Other.Id, 1, 12, null, default)).Items);
        var forbidden = await Assert.ThrowsAsync<ProjectException>(() => f.Service.UpdateAsync(project.Id, f.Other.Id,
            new() { Title = "Hijack" }, default));
        Assert.Equal(403, forbidden.Status);
    }

    [Fact]
    public async Task ProjectEditsRefreshRecentUpdateAndReadsPreserveIt()
    {
        await using var f = await Fixture.Create();
        var project = await f.Internal();
        var previous = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        (await f.Db.Projects.SingleAsync()).UpdatedAt = previous;
        await f.Db.SaveChangesAsync();
        var saved = await f.Service.UpdateAsync(project.Id, f.Owner.Id,
            new() { Title = "Updated project" }, default);
        Assert.True(saved.UpdatedAt > previous);
        f.Db.ChangeTracker.Clear();
        var reloaded = await f.Service.GetAsync(project.Id, f.Owner.Id, default);
        Assert.Equal(saved.UpdatedAt, reloaded.UpdatedAt);
        Assert.Equal("Updated project", reloaded.Title);
    }

    [Fact]
    public async Task OwnerCanPatchOnlyTheImageAndItPersistsWithoutChangingProjectContent()
    {
        await using var f = await Fixture.Create();
        var project = await f.Internal();
        const string url = "https://res.cloudinary.com/test/image/upload/project.png";
        var denied = await Assert.ThrowsAsync<ProjectException>(() => f.Service.UpdateAsync(project.Id, f.Other.Id,
            new() { LogoUrl = url }, default));
        Assert.Equal(403, denied.Status);
        var saved = await f.Service.UpdateAsync(project.Id, f.Owner.Id, new() { LogoUrl = url }, default);
        Assert.Equal(project.Title, saved.Title);
        Assert.Equal(project.Description, saved.Description);
        Assert.Equal(project.Status, saved.Status);
        f.Db.ChangeTracker.Clear();
        var reloaded = await f.Service.GetAsync(project.Id, f.Owner.Id, default);
        Assert.Equal(url, reloaded.LogoUrl);
    }

    [Fact]
    public async Task DetailsAndOwnReviewRoundTripAndDoNotLeakPrivateReview()
    {
        await using var f = await Fixture.Create();
        var p = await f.Internal();
        var updated = await f.Service.UpdateAsync(p.Id, f.Owner.Id, new() { Visibility = 1,
            Details = new() { Description = "Introduction", Features = ["Save game"],
                StartDate = p.Details.StartDate, EndDate = p.Details.StartDate!.Value.AddDays(30),
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
        Assert.Empty((await f.Service.DiscoverAsync(f.Other.Id, 1, 12, null, default)).Items);
    }

    [Fact]
    public async Task InvitationCanOnlyBeAcceptedByRecipientAndCannotOverfillPosition()
    {
        await using var f = await Fixture.Create();
        var p = await f.Internal();
        await f.Befriend(f.Other);
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
    public async Task FriendInvitationNeedsNoRecruitmentAndOnlyRecipientAcceptanceCreatesMembership()
    {
        await using var f = await Fixture.Create();
        var p = await f.Internal();
        var denied = await Assert.ThrowsAsync<ProjectException>(() => f.Service.RequestAsync(p.Id, f.Owner.Id,
            new() { RequestType = 1, InvitedUserId = f.Other.Profile!.Id, Role = "UX researcher" }, default));
        Assert.Equal("FRIENDSHIP_REQUIRED", denied.Code);
        await f.Befriend(f.Other);
        var invite = await f.Service.RequestAsync(p.Id, f.Owner.Id,
            new() { RequestType = 1, InvitedUserId = f.Other.Profile!.Id, Role = " UX researcher " }, default);
        Assert.Null(invite.PositionId);
        Assert.Equal("UX researcher", invite.Role);
        Assert.Single(await f.Db.ProjectMembers.ToListAsync());
        Assert.Empty((await f.Service.MyRequestsAsync(f.Third.Id, default, p.Id)).Items);
        Assert.Single((await f.Service.MyRequestsAsync(f.Other.Id, default, p.Id)).Items);
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.DecideAsync(invite.Id, f.Owner.Id, new("accept"), default));
        var apply = await Assert.ThrowsAsync<ProjectException>(() => f.Service.RequestAsync(p.Id, f.Third.Id,
            new() { PositionId = "anything" }, default));
        Assert.Equal("RECRUITMENT_CLOSED", apply.Code);
        (await f.Db.Friendships.SingleAsync()).Status = FriendshipStatus.Removed;
        await f.Db.SaveChangesAsync();
        await f.Service.DecideAsync(invite.Id, f.Other.Id, new("accept"), default);
        var member = await f.Db.ProjectMembers.SingleAsync(m => m.UserId == f.Other.Profile!.Id);
        Assert.Equal("UX researcher", member.Role);
        Assert.Null(member.PositionId);
    }

    [Fact]
    public async Task FriendInvitationsSurviveRecruitmentClosureDeadlineAndCompletion()
    {
        await using var f = await Fixture.Create();
        var p = await f.Internal();
        await f.Befriend(f.Other);
        var recruitment = new SaveHubRecruitmentRequest { Status = 1, Deadline = DateTimeOffset.UtcNow.AddHours(1),
            Positions = [new() { Id = "dev", Name = "Developer", Quantity = 1 }] };
        await f.Service.RecruitmentAsync(p.Id, f.Owner.Id, recruitment, default);
        var invite = await f.Service.RequestAsync(p.Id, f.Owner.Id,
            new() { RequestType = 1, InvitedUserId = f.Other.Profile!.Id, Role = "Mentor" }, default);
        await f.Service.RecruitmentAsync(p.Id, f.Owner.Id, recruitment with { Status = 2 }, default);
        Assert.Equal(ProjectJoinRequestStatus.Pending, (await f.Db.ProjectJoinRequests.SingleAsync()).Status);
        await f.Service.RecruitmentAsync(p.Id, f.Owner.Id, recruitment, default);
        await new ProjectRecruitmentTasks(f.Db).RunAsync(DateTimeOffset.UtcNow.AddHours(2));
        Assert.Equal(ProjectJoinRequestStatus.Pending, (await f.Db.ProjectJoinRequests.SingleAsync()).Status);
        await f.Service.CompleteAsync(p.Id, f.Owner.Id, default);
        Assert.Equal(ProjectJoinRequestStatus.Pending, (await f.Db.ProjectJoinRequests.SingleAsync()).Status);
        await f.Service.DecideAsync(invite.Id, f.Other.Id, new("accept"), default);
    }

    [Fact]
    public async Task CompletionClosesRecruitmentAndBlocksReopening()
    {
        await using var f = await Fixture.Create();
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
        f.Db.ChangeTracker.Clear();
        await new ProjectRecruitmentTasks(f.Db).RunAsync(now.AddHours(2));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(ProjectJoinRequestStatus.Accepted, (await f.Db.ProjectJoinRequests.SingleAsync(r => r.Id == accepted.Id)).Status);
        Assert.Equal(ProjectJoinRequestStatus.Expired, (await f.Db.ProjectJoinRequests.SingleAsync(r => r.Id == pending.Id)).Status);
        Assert.Equal(2, await f.Db.ProjectMembers.CountAsync(m => m.Status == ProjectMemberStatus.Active));
        var closed = await f.Service.GetAsync(project.Id, f.Owner.Id, default);
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
        public async Task Befriend(Account other)
        {
            db.Friendships.Add(new() { RequesterAccountId = owner.Id, ReceiverAccountId = other.Id, Status = FriendshipStatus.Accepted });
            await db.SaveChangesAsync();
        }
        public Task<HubProject> Internal() => Service.CreateAsync(owner.Id,
            new() { Title = "Personal", Description = "My project", Status = 1 }, default);
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
