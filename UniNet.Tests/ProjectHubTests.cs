using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
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
    [Theory]
    [InlineData("unsafe", 0.95, ProjectStatus.Rejected)]
    [InlineData("unsafe", 0.75, ProjectStatus.Pending)]
    [InlineData("suspicious", 0.95, ProjectStatus.Pending)]
    [InlineData("safe", 0.95, ProjectStatus.Pending)]
    [InlineData("error", 0, ProjectStatus.Pending)]
    public async Task CreationRoutesAiResultsToRejectionOrStaffReview(string result, double confidence, ProjectStatus status)
    {
        await using var f = await Fixture.Create();
        var ai = new HubModerationStub(result, (decimal)confidence);
        var config = new ConfigurationBuilder().Build();
        var hub = new ProjectHubService(f.Db, ai, config);
        var moderation = new ProjectModerationService(ai, f.Db, config);
        var project = await hub.CreateAsync(f.Owner.Id, new() { Title = "New project", Description = "Test" }, default);
        Assert.Equal((short)status, project.Status);
        await Assert.ThrowsAsync<ProjectException>(() => hub.GetAsync(project.Id, f.Other.Id, default));
        await Assert.ThrowsAsync<ProjectException>(() => hub.UpdateAsync(project.Id, f.Owner.Id, new() { Status = 1 }, default));
        var queue = await moderation.GetPendingProjectsAsync(1, 10, default, ProjectModerationPurpose.ProjectHubSubmission);
        Assert.Equal(status == ProjectStatus.Pending ? 1 : 0, queue.TotalItems);
        Assert.Empty((await moderation.GetPendingProjectsAsync(1, 10, default)).Projects);
    }

    [Theory]
    [InlineData(AccountRole.Admin, true, ProjectStatus.Active)]
    [InlineData(AccountRole.Moderator, false, ProjectStatus.Rejected)]
    public async Task StaffCanDecidePendingHubProjectOnce(AccountRole role, bool approve, ProjectStatus expected)
    {
        await using var f = await Fixture.Create();
        var staff = new Account { Email = "staff@hub.test", Role = role,
            Profile = new() { FullName = "Staff", Nickname = "staff" } };
        f.Db.Accounts.Add(staff);
        await f.Db.SaveChangesAsync();
        var project = await f.Service.CreateAsync(f.Owner.Id, new() { Title = "New project", Description = "Test" }, default);
        var moderation = new ProjectModerationService(new HubModerationStub(), f.Db, new ConfigurationBuilder().Build());
        const ProjectModerationPurpose purpose = ProjectModerationPurpose.ProjectHubSubmission;
        Task<ProjectModerationResponse> Decide() => approve
            ? moderation.ApproveProjectAsync(project.Id, staff.Id, new(), default, purpose)
            : moderation.RejectProjectAsync(project.Id, staff.Id, new("Banned content"), default, purpose);
        await Decide();
        f.Db.ChangeTracker.Clear();
        Assert.Equal((short)expected, (await f.Service.GetAsync(project.Id, f.Owner.Id, default)).Status);
        Assert.Empty((await moderation.GetPendingProjectsAsync(1, 10, default, purpose)).Projects);
        Assert.Equal(409, (await Assert.ThrowsAsync<ProjectException>(Decide)).Status);
    }

    [Theory]
    [InlineData(false, false, ProjectMemberStatus.Left)]
    [InlineData(false, true, ProjectMemberStatus.Kicked)]
    [InlineData(true, false, ProjectMemberStatus.Left)]
    [InlineData(true, true, ProjectMemberStatus.Kicked)]
    public async Task HubRejectsLeavingAndKicking(bool completed, bool asOwner, ProjectMemberStatus status)
    {
        await using var f = await Fixture.Create();
        var project = await f.Internal();
        var member = new ProjectMember
        {
            ProjectId = project.Id, UserId = f.Other.Profile!.Id,
            Role = "Developer", Status = ProjectMemberStatus.Active
        };
        f.Db.ProjectMembers.Add(member);
        await f.Db.SaveChangesAsync();
        if (completed)
            await f.Service.CompleteAsync(project.Id, f.Owner.Id, default);

        var error = await Assert.ThrowsAsync<ProjectException>(() => f.Service.MemberAsync(
            project.Id, member.UserId, asOwner ? f.Owner.Id : f.Other.Id, new(Status: (short)status), default));

        Assert.Equal("PROJECT_HUB_MEMBER_REMOVAL_DISABLED", error.Code);
        await f.Db.Entry(member).ReloadAsync();
        Assert.Equal(ProjectMemberStatus.Active, member.Status);
        Assert.Null(member.LeftAt);
    }

    [Theory]
    [InlineData("accept", "đã được chấp nhận")]
    [InlineData("reject", "đã bị từ chối")]
    [InlineData("cancel", "đã được hủy")]
    public async Task RequestDecisionNotificationNamesTheActualOutcome(string decision, string outcome)
    {
        await using var f = await Fixture.Create();
        var project = await f.Internal();
        await f.Service.RecruitmentAsync(project.Id, f.Owner.Id, new() { Status = 1,
            Positions = [new() { Id = "developer", Name = "Developer", Quantity = 1 }] }, default);
        var request = await f.Service.RequestAsync(project.Id, f.Other.Id, new() { PositionId = "developer" }, default);
        await f.Service.DecideAsync(request.Id, decision == "cancel" ? f.Other.Id : f.Owner.Id, new(decision), default);
        var notification = await f.Db.Set<VerificationNotification>().SingleAsync(n => n.Type == "project.join-request.reviewed");
        Assert.Equal($"Yêu cầu tham gia dự án {project.Title} {outcome}.", notification.Message);
        Assert.DoesNotContain("vừa được xử lý", notification.Message);
        Assert.Equal(decision == "cancel" ? f.Owner.Id : f.Other.Id, notification.RecipientUserId);
    }

    [Fact]
    public async Task DocumentMetadataIsRecoveredFromRegistryAndSurvivesRename()
    {
        await using var f = await Fixture.Create();
        var project = await f.Internal();
        var uploadedAt = DateTimeOffset.Parse("2026-11-17T09:04:00Z");
        var upload = new UploadedFile { OwnerAccountId = f.Other.Id, Url = "https://files.test/original.docx",
            PublicId = "original.docx", FileName = "original.docx", Format = "docx", ResourceType = "raw",
            SizeBytes = 2516582, CreatedAt = uploadedAt };
        f.Db.UploadedFiles.Add(upload); await f.Db.SaveChangesAsync();
        var result = await f.Service.UpdateAsync(project.Id, f.Owner.Id, new() {
            Details = project.Details with { Documents = [new(upload.Url, "Original.docx", "document")] }
        }, default);
        var file = Assert.Single(result.Details.Documents);
        Assert.Equal(upload.SizeBytes, file.SizeBytes); Assert.Equal(uploadedAt, file.UploadedAt);
        Assert.Equal(f.Other.Profile!.Id, file.UploadedBy!.Id); Assert.Equal("Other", file.UploadedBy.FullName);
        result = await f.Service.UpdateAsync(project.Id, f.Owner.Id, new() {
            Details = result.Details with { Documents = [file with { Name = "Renamed.docx" }] }
        }, default);
        f.Db.ChangeTracker.Clear();
        var reloaded = Assert.Single((await f.Service.GetAsync(project.Id, f.Owner.Id, default)).Details.Documents);
        Assert.Equal("Renamed.docx", reloaded.Name); Assert.Equal(upload.SizeBytes, reloaded.SizeBytes);
        Assert.Equal(uploadedAt, reloaded.UploadedAt); Assert.Equal(f.Other.Profile.Id, reloaded.UploadedBy!.Id);
        var listed = Assert.Single((await f.Service.MineAsync(f.Owner.Id, 1, 10, null, default)).Items);
        Assert.Equal(upload.SizeBytes, Assert.Single(listed.Details.Documents).SizeBytes);
    }

    [Fact]
    public async Task ClientCannotForgeDocumentUploadMetadata()
    {
        await using var f = await Fixture.Create();
        var project = await f.Internal();
        var upload = new UploadedFile { OwnerAccountId = f.Other.Id, Url = "https://files.test/trusted.pdf",
            PublicId = "trusted.pdf", FileName = "trusted.pdf", Format = "pdf", ResourceType = "raw", SizeBytes = 1024 };
        f.Db.UploadedFiles.Add(upload); await f.Db.SaveChangesAsync();
        var fake = new HubFile(upload.Url, "trusted.pdf", "document", 1, DateTimeOffset.MinValue,
            new(f.Owner.Profile!.Id, "Fake uploader", null));
        var result = await f.Service.UpdateAsync(project.Id, f.Owner.Id, new() {
            Details = project.Details with { Documents = [fake, fake with { Url = "https://files.test/unknown.pdf" }] }
        }, default);
        Assert.Equal(1024, result.Details.Documents[0].SizeBytes);
        Assert.Equal(upload.CreatedAt, result.Details.Documents[0].UploadedAt);
        Assert.Equal(f.Other.Profile!.Id, result.Details.Documents[0].UploadedBy!.Id);
        Assert.Null(result.Details.Documents[1].SizeBytes); Assert.Null(result.Details.Documents[1].UploadedAt);
        Assert.Null(result.Details.Documents[1].UploadedBy);
    }

    [Fact]
    public async Task NewProjectTimelineUsesCreationDayAndEditableEndDate()
    {
        await using var f = await Fixture.Create();
        var p = await f.CreateActive(new() { Title = "Timeline", Description = "Dates" });
        var start = DateOnly.FromDateTime(p.CreatedAt.ToOffset(TimeSpan.FromHours(7)).DateTime);
        Assert.Equal(start, p.Details.StartDate); Assert.True(p.Details.StartDateIsCreationDate);
        Assert.Null(p.Details.ActualEndDate);
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.UpdateAsync(p.Id, f.Other.Id,
            new() { Timeline = new(start, null) }, default));
        var revisedStart = start.AddYears(-2);
        var revised = await f.Service.UpdateAsync(p.Id, f.Owner.Id,
            new() { Timeline = new(revisedStart, null) }, default);
        Assert.Equal(revisedStart, revised.Details.StartDate);
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.UpdateAsync(p.Id, f.Owner.Id,
            new() { Timeline = new(start, start.AddDays(-1)) }, default));
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.UpdateAsync(p.Id, f.Owner.Id,
            new() { Timeline = new(start, start.AddDays(2)) }, default));
        var saved = await f.Service.UpdateAsync(p.Id, f.Owner.Id, new() { Timeline = new(revisedStart, start) }, default);
        Assert.Equal(start, saved.Details.ActualEndDate); Assert.Equal(1, saved.Status);
        Assert.Null((await f.Db.Projects.SingleAsync()).CompletedAt);
        f.Db.ChangeTracker.Clear();
        var reloaded = await f.Service.GetAsync(p.Id, f.Owner.Id, default);
        Assert.Equal(start, reloaded.Details.ActualEndDate);
        Assert.Equal(revisedStart, reloaded.Details.StartDate);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartDateCanBeCorrectedWithoutChangingCompletionDates(bool throughDetails)
    {
        await using var f = await Fixture.Create();
        var created = await f.Internal();
        var completed = await f.Service.CompleteAsync(created.Id, f.Owner.Id, default);
        var earlierStart = completed.Details.StartDate!.Value.AddYears(-3);
        var update = throughDetails
            ? new UpdateHubProjectRequest { Details = completed.Details with { StartDate = earlierStart } }
            : new UpdateHubProjectRequest { Timeline = new(earlierStart, completed.Details.ActualEndDate) };
        var saved = await f.Service.UpdateAsync(created.Id, f.Owner.Id, update, default);
        Assert.Equal(earlierStart, saved.Details.StartDate);
        Assert.Equal(completed.Details.ActualEndDate, saved.Details.ActualEndDate);
        Assert.Equal(completed.Details.CompletedAt, saved.Details.CompletedAt);
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.UpdateAsync(created.Id, f.Owner.Id,
            throughDetails ? new() { Details = saved.Details with { StartDate = completed.Details.ActualEndDate!.Value.AddDays(1) } }
                : new() { Timeline = new(completed.Details.ActualEndDate!.Value.AddDays(1), completed.Details.ActualEndDate) }, default));
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
        Assert.Null(legacy.Details.StartDate);
        Assert.Null(legacy.Details.ActualEndDate); Assert.False(legacy.Details.StartDateIsCreationDate);
        var start = new DateOnly(2020, 1, 1); var actual = start.AddMonths(3);
        var saved = await f.Service.UpdateAsync(p.Id, f.Owner.Id, new() { Timeline = new(start, actual) }, default);
        Assert.Equal(start, saved.Details.StartDate); Assert.Equal(actual, saved.Details.ActualEndDate);
        saved = await f.Service.UpdateAsync(p.Id, f.Owner.Id, new() { Title = "Changed" }, default);
        Assert.Equal(actual, saved.Details.ActualEndDate);
        saved = await f.Service.UpdateAsync(p.Id, f.Owner.Id, new() { Timeline = new(null, null) }, default);
        Assert.Null(saved.Details.StartDate); Assert.Null(saved.Details.ActualEndDate);
    }

    [Fact]
    public async Task CompletionCannotPrecedeCorrectedStartDate()
    {
        await using var f = await Fixture.Create();
        var created = await f.Internal();
        await f.Service.UpdateAsync(created.Id, f.Owner.Id, new() {
            Timeline = new(created.Details.StartDate!.Value.AddDays(10), null)
        }, default);
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.CompleteAsync(created.Id, f.Owner.Id, default));
        f.Db.ChangeTracker.Clear();
        var unchanged = await f.Service.GetAsync(created.Id, f.Owner.Id, default);
        Assert.Equal(1, unchanged.Status); Assert.Null(unchanged.Details.CompletedAt);
    }

    [Fact]
    public async Task ProjectContractsDoNotExposePlannedEndDates()
    {
        await using var f = await Fixture.Create();
        var project = await f.Internal();
        Assert.Null(typeof(CreateHubProjectRequest).GetProperty("ExpectedEndDate"));
        Assert.Null(typeof(HubTimeline).GetProperty("ExpectedEndDate"));
        Assert.Null(typeof(HubDetails).GetProperty("EndDate"));
        Assert.Null(project.Details.ActualEndDate);
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
    public async Task CreationWaitsForReviewRegardlessOfRequestedStatus(short requestedStatus)
    {
        await using var f = await Fixture.Create();
        var result = await f.Service.CreateAsync(f.Owner.Id,
            new() { Title = "New project", Description = "Initial status", Status = requestedStatus }, default);
        Assert.Equal((short)ProjectStatus.Pending, result.Status);
        Assert.Null(result.Details.CompletedAt);
        var stored = await f.Db.Projects.SingleAsync();
        Assert.Equal(ProjectStatus.Pending, stored.Status);
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
        var p = await f.CreateActive(new() { Title = "Existing", Description = "External" });
        await f.Befriend(f.Other);
        var invite = await f.Service.RequestAsync(p.Id, f.Owner.Id,
            new() { RequestType = 1, InvitedUserId = f.Other.Profile!.Id, Role = "Designer" }, default);
        if (patch) await f.Service.UpdateAsync(p.Id, f.Owner.Id, new() { Status = 2 }, default);
        else await f.Service.CompleteAsync(p.Id, f.Owner.Id, default);
        Assert.Equal(ProjectJoinRequestStatus.Pending, (await f.Db.ProjectJoinRequests.SingleAsync()).Status);
        await f.Service.DecideAsync(invite.Id, f.Other.Id, new("accept"), default);
    }

    [Fact]
    public async Task NewProjectsStartPendingWithOwnerAndWithoutRecruitment()
    {
        await using var f = await Fixture.Create();
        var result = await f.Service.CreateAsync(f.Owner.Id, new() { Title = "Game 2D", Description = "A real personal game", Technologies = ["Unity", "C#"] }, default);
        Assert.Equal(0, result.Status);
        Assert.Null(result.Recruitment);
        Assert.Equal("Leader", Assert.Single(result.Members).Role);
        Assert.Null(result.ChatRoomId);
        Assert.Equal(ProjectModerationStatus.ReviewRequired, (await f.Db.ProjectModerations.SingleAsync()).Status);
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
                StartDate = p.Details.StartDate,
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
            new() { Timeline = new(new(2026, 12, 1), new(2026, 11, 1)) }, default));
    }

    [Theory]
    [InlineData("document", false)]
    [InlineData("image", false)]
    [InlineData("video", false)]
    [InlineData("document", true)]
    [InlineData("image", true)]
    [InlineData("video", true)]
    public async Task ActiveProjectMembersCanAppendTheirUploadsWithoutChangingExistingResources(string type, bool asOwner)
    {
        await using var f = await Fixture.Create(); var p = await f.Internal();
        f.Db.ProjectMembers.Add(new() { ProjectId = p.Id, UserId = f.Other.Profile!.Id, Role = "Developer" });
        var uploader = asOwner ? f.Owner : f.Other;
        var upload = new UploadedFile { OwnerAccountId = uploader.Id, Url = $"https://files.test/new.{type}",
            PublicId = "new", FileName = "New file", Format = type, ResourceType = type == "document" ? "raw" : type, SizeBytes = 1234 };
        f.Db.UploadedFiles.Add(upload); await f.Db.SaveChangesAsync();
        p = await f.Service.UpdateAsync(p.Id, f.Owner.Id, new() { Details = p.Details with {
            Documents = [new("https://files.test/existing.pdf", "Existing document", "document")],
            Media = [new("https://files.test/existing.png", "Existing image", "image")] } }, default);
        var kind = type == "document" ? "documents" : "media";
        var fakeMetadata = new HubFile(upload.Url, "New file", type, 1, DateTimeOffset.MinValue, new(f.Third.Profile!.Id, "Fake", null));
        var result = await f.Service.AddResourceAsync(p.Id, uploader.Id, kind, fakeMetadata, default);
        result = await f.Service.AddResourceAsync(p.Id, uploader.Id, kind, fakeMetadata, default);
        Assert.Equal(type == "document" ? 2 : 1, result.Details.Documents.Length);
        Assert.Equal(type == "document" ? 1 : 2, result.Details.Media.Length);
        var added = Assert.Single(result.Details.Documents.Concat(result.Details.Media), item => item.Url == upload.Url);
        Assert.Equal(upload.SizeBytes, added.SizeBytes); Assert.Equal(uploader.Profile!.Id, added.UploadedBy!.Id);
        Assert.Equal(p.Title, result.Title); Assert.Equal(p.Details.Description, result.Details.Description);
        Assert.Equal(p.Details.StartDate, result.Details.StartDate);
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.UpdateAsync(p.Id, f.Other.Id,
            new() { Details = p.Details with { Documents = [] } }, default));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task OutsidersAndFormerMembersCannotAddProjectResources(int status)
    {
        await using var f = await Fixture.Create(); var p = await f.Internal();
        if (status >= 0) f.Db.ProjectMembers.Add(new() { ProjectId = p.Id, UserId = f.Other.Profile!.Id,
            Role = "Developer", Status = (ProjectMemberStatus)status });
        f.Db.UploadedFiles.Add(new() { OwnerAccountId = f.Other.Id, Url = "https://files.test/upload.pdf",
            PublicId = "upload", FileName = "upload.pdf", Format = "pdf", ResourceType = "raw", SizeBytes = 1024 });
        await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<ProjectException>(() => f.Service.AddResourceAsync(p.Id, f.Other.Id,
            "documents", new("https://files.test/upload.pdf", "File", "document"), default));
        Assert.Equal(403, error.Status);
        Assert.Empty((await f.Service.GetAsync(p.Id, f.Owner.Id, default)).Details.Documents);
    }

    [Fact]
    public async Task AppendingResourcesRejectsOtherPeoplesUploadsAndInvalidKinds()
    {
        await using var f = await Fixture.Create(); var p = await f.Internal();
        f.Db.ProjectMembers.Add(new() { ProjectId = p.Id, UserId = f.Other.Profile!.Id, Role = "Developer" });
        f.Db.UploadedFiles.Add(new() { OwnerAccountId = f.Owner.Id, Url = "https://files.test/owner.pdf",
            PublicId = "owner", FileName = "owner.pdf", Format = "pdf", ResourceType = "raw", SizeBytes = 1024 });
        await f.Db.SaveChangesAsync();
        var file = new HubFile("https://files.test/owner.pdf", "File", "document");
        var denied = await Assert.ThrowsAsync<ProjectException>(() => f.Service.AddResourceAsync(p.Id, f.Other.Id, "documents", file, default));
        Assert.Equal("PROJECT_UPLOAD_FORBIDDEN", denied.Code);
        var invalidKind = await Assert.ThrowsAsync<ProjectException>(() => f.Service.AddResourceAsync(p.Id, f.Owner.Id, "links", file, default));
        Assert.Equal(400, invalidKind.Status);
        var invalidType = await Assert.ThrowsAsync<ProjectException>(() => f.Service.AddResourceAsync(p.Id, f.Owner.Id, "media", file, default));
        Assert.Equal(400, invalidType.Status);
    }

    [Fact]
    public async Task MemberAppendRespectsResourceLimitWithoutReplacingExistingFiles()
    {
        await using var f = await Fixture.Create(); var p = await f.Internal();
        f.Db.ProjectMembers.Add(new() { ProjectId = p.Id, UserId = f.Other.Profile!.Id, Role = "Developer" });
        f.Db.UploadedFiles.Add(new() { OwnerAccountId = f.Other.Id, Url = "https://files.test/extra.pdf",
            PublicId = "extra", FileName = "extra.pdf", Format = "pdf", ResourceType = "raw", SizeBytes = 1024 });
        await f.Db.SaveChangesAsync();
        p = await f.Service.UpdateAsync(p.Id, f.Owner.Id, new() { Details = p.Details with {
            Documents = Enumerable.Range(0, 50).Select(i => new HubFile($"https://files.test/{i}.pdf", $"File {i}", "document")).ToArray() } }, default);
        var error = await Assert.ThrowsAsync<ProjectException>(() => f.Service.AddResourceAsync(p.Id, f.Other.Id,
            "documents", new("https://files.test/extra.pdf", "Extra", "document"), default));
        Assert.Equal(400, error.Status);
        f.Db.ChangeTracker.Clear();
        var stored = await f.Service.GetAsync(p.Id, f.Owner.Id, default);
        Assert.Equal(50, stored.Details.Documents.Length);
        Assert.DoesNotContain(stored.Details.Documents, file => file.Url.EndsWith("extra.pdf"));
    }

    private sealed class Fixture(SqliteConnection connection, UniNetDbContext db, Account owner, Account other, Account third) : IAsyncDisposable
    {
        public UniNetDbContext Db => db;
        public Account Owner => owner;
        public Account Other => other;
        public Account Third => third;
        public ProjectHubService Service { get; } = new(db, new HubModerationStub(), new ConfigurationBuilder().Build());
        public async Task Befriend(Account other)
        {
            db.Friendships.Add(new() { RequesterAccountId = owner.Id, ReceiverAccountId = other.Id, Status = FriendshipStatus.Accepted });
            await db.SaveChangesAsync();
        }
        public Task<HubProject> Internal() => CreateActive(new() { Title = "Personal", Description = "My project" });

        public async Task<HubProject> CreateActive(CreateHubProjectRequest request)
        {
            var draft = await Service.CreateAsync(owner.Id, request, default);
            var project = await db.Projects.SingleAsync(p => p.Id == draft.Id);
            project.Status = ProjectStatus.Active;
            project.Moderations.Single().Status = ProjectModerationStatus.Approved;
            await db.SaveChangesAsync();
            return await Service.GetAsync(draft.Id, owner.Id, default);
        }
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
