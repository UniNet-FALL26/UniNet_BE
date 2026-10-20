using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using Xunit;

namespace UniNet.Tests;

public sealed partial class ProjectVerificationTests
{
    [Theory]
    [InlineData(AccountRole.Student)]
    [InlineData(AccountRole.Verificator)]
    public async Task MeetingRejectsUnrelatedParticipantAcrossEveryOperation(AccountRole role)
    {
        await using var f = await Fixture.CreateAsync();
        var data = await PrepareMeetingAsync(f);
        f.Other.Role = role;
        await f.Db.SaveChangesAsync();
        await data.Service.GetOrCreateRoomAsync(data.Assignment, f.Owner.Id);
        Func<Task>[] operations =
        [
            () => data.Service.GetAsync(data.Assignment, f.Other.Id),
            () => data.Service.GetOrCreateRoomAsync(data.Assignment, f.Other.Id),
            () => data.Service.JoinAsync(data.Assignment, f.Other.Id),
            () => data.Service.StartAsync(data.Assignment, f.Other.Id),
            () => data.Service.EndAsync(data.Assignment, f.Other.Id)
        ];
        foreach (var operation in operations)
            Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<MeetingException>(operation)).Code);
    }

    [Fact]
    public async Task MeetingFormerAssigneeCannotAccessOldOrReplacementAssignment()
    {
        await using var f = await Fixture.CreateAsync();
        var data = await PrepareMeetingAsync(f);
        var room = await data.Service.GetOrCreateRoomAsync(data.Assignment, data.Staff.AccountId);
        var next = Fixture.Account("NextVerificator", AccountRole.Verificator);
        f.Db.Accounts.Add(next);
        await f.Db.SaveChangesAsync();
        var old = await f.Db.VerificationAssignments.SingleAsync();
        var replacement = new VerificationAssignment
        {
            Id = Guid.NewGuid(), ProjectVerificationId = old.ProjectVerificationId,
            AssignedToUserId = next.Profile!.Id, AdminId = old.AdminId,
            Status = VerificationAssignmentStatus.Assigned, AssignedAt = DateTimeOffset.UtcNow
        };
        f.Db.VerificationAssignments.Add(replacement);
        old.Status = VerificationAssignmentStatus.Cancelled;
        old.ReplacedAt = DateTimeOffset.UtcNow;
        old.ReplacedBy = replacement;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        foreach (var id in new[] { old.Id, replacement.Id })
        {
            var expected = id == old.Id ? "ASSIGNMENT_LOCKED" : "FORBIDDEN";
            Assert.Equal(expected, (await Assert.ThrowsAsync<MeetingException>(() =>
                data.Service.JoinAsync(id, data.Staff.AccountId))).Code);
        }
        Assert.Equal(room.JoinUrl, (await data.Service.JoinAsync(replacement.Id, next.Id)).JoinUrl);
    }

    [Theory]
    [InlineData(VerificationAssignmentStatus.Cancelled)]
    [InlineData(VerificationAssignmentStatus.Completed)]
    public async Task MeetingInactiveAssignmentCannotCreateOrJoin(VerificationAssignmentStatus status)
    {
        await using var f = await Fixture.CreateAsync();
        var data = await PrepareMeetingAsync(f);
        await data.Service.GetOrCreateRoomAsync(data.Assignment, f.Owner.Id);
        (await f.Db.VerificationAssignments.SingleAsync()).Status = status;
        await f.Db.SaveChangesAsync();
        Assert.Equal("ASSIGNMENT_LOCKED", (await Assert.ThrowsAsync<MeetingException>(() =>
            data.Service.GetOrCreateRoomAsync(data.Assignment, f.Owner.Id))).Code);
        Assert.Equal("ASSIGNMENT_LOCKED", (await Assert.ThrowsAsync<MeetingException>(() =>
            data.Service.JoinAsync(data.Assignment, data.Staff.AccountId))).Code);
    }

    [Fact]
    public async Task MeetingMetadataWithoutRoomDoesNotRequireJitsiConfiguration()
    {
        await using var f = await Fixture.CreateAsync();
        var data = await PrepareMeetingAsync(f);
        f.Configuration["Jitsi:BaseUrl"] = "";
        var meeting = await data.Service.GetAsync(data.Assignment, f.Owner.Id);
        Assert.Null(meeting.RoomName);
        Assert.Null(meeting.JoinUrl);
    }

    [Fact]
    public async Task MeetingCanEndAndReadEndedMetadataWithoutJitsiConfiguration()
    {
        await using var f = await Fixture.CreateAsync();
        var data = await PrepareMeetingAsync(f);
        await data.Service.GetOrCreateRoomAsync(data.Assignment, f.Owner.Id);
        await data.Service.StartAsync(data.Assignment, data.Staff.AccountId);
        f.Configuration["Jitsi:BaseUrl"] = "";
        var ended = await data.Service.EndAsync(data.Assignment, data.Staff.AccountId);
        Assert.NotNull(ended.MeetingEndAt);
        Assert.Null(ended.JoinUrl);
        Assert.Equal(ended.MeetingEndAt, (await data.Service.GetAsync(data.Assignment, f.Owner.Id)).MeetingEndAt);
        Assert.Equal(ended.MeetingEndAt, (await data.Service.EndAsync(data.Assignment, data.Staff.AccountId)).MeetingEndAt);
    }

    [Fact]
    public async Task MeetingJoinRequiresExistingRoomAndParticipantWithoutChangingLifecycle()
    {
        await using var f = await Fixture.CreateAsync();
        var data = await PrepareMeetingAsync(f);
        Assert.Equal("MEETING_ROOM_REQUIRED", (await Assert.ThrowsAsync<MeetingException>(() =>
            data.Service.JoinAsync(data.Assignment, f.Owner.Id))).Code);
        var room = await data.Service.GetOrCreateRoomAsync(data.Assignment, f.Owner.Id);
        var joined = await data.Service.JoinAsync(data.Assignment, data.Staff.AccountId);
        Assert.Equal(room.JoinUrl, joined.JoinUrl);
        Assert.Equal(room.ProjectVerificationId, joined.ProjectVerificationId);
        Assert.Equal(data.Assignment, joined.AssignmentId);
        Assert.Equal(joined.JoinUrl, (await data.Service.JoinAsync(data.Assignment, f.Owner.Id)).JoinUrl);
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<MeetingException>(() =>
            data.Service.JoinAsync(data.Assignment, f.Other.Id))).Code);
        Assert.Equal("ASSIGNMENT_NOT_FOUND", (await Assert.ThrowsAsync<MeetingException>(() =>
            data.Service.JoinAsync(Guid.NewGuid(), f.Owner.Id))).Code);
        var verification = await f.Db.ProjectVerifications.SingleAsync();
        Assert.Null(verification.MeetingStartAt);
        Assert.Null(verification.MeetingEndAt);
        await data.Service.StartAsync(data.Assignment, data.Staff.AccountId);
        await data.Service.EndAsync(data.Assignment, data.Staff.AccountId);
        Assert.Equal("INVALID_MEETING_STATE", (await Assert.ThrowsAsync<MeetingException>(() =>
            data.Service.JoinAsync(data.Assignment, f.Owner.Id))).Code);
    }

    private static async Task<(MeetingService Service, Guid Assignment, UserProfile Staff)> PrepareMeetingAsync(Fixture f)
    {
        var data = await PrepareFinalReportAsync(f);
        var verification = await f.Db.ProjectVerifications.SingleAsync();
        verification.MeetingStartAt = verification.MeetingEndAt = null;
        f.Db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = f.Project.Id, UserId = f.Owner.Profile!.Id,
            Role = "Leader", Status = ProjectMemberStatus.Active
        });
        f.Configuration["Jitsi:BaseUrl"] = "https://meet.example.com";
        await f.Db.SaveChangesAsync();
        return (new MeetingService(f.Db, f.Configuration), data.Assignment, data.Staff);
    }

    [Fact]
    public async Task MeetingRoomIsReusedAndLifecycleIsIdempotentWithoutCompletingAssignment()
    {
        await using var f = await Fixture.CreateAsync();
        var data = await PrepareMeetingAsync(f);
        Assert.Null((await data.Service.GetAsync(data.Assignment, f.Owner.Id)).JoinUrl);
        var room = await data.Service.GetOrCreateRoomAsync(data.Assignment, f.Owner.Id);
        Assert.Matches("^uninet-[a-f0-9]{64}$", room.RoomName!);
        Assert.Equal("https://meet.example.com/" + room.RoomName, room.JoinUrl);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(room.RoomName, (await data.Service.GetOrCreateRoomAsync(data.Assignment, data.Staff.AccountId)).RoomName);
        var start = await data.Service.StartAsync(data.Assignment, data.Staff.AccountId);
        Assert.Equal(start.MeetingStartAt, (await data.Service.StartAsync(data.Assignment, data.Staff.AccountId)).MeetingStartAt);
        var end = await data.Service.EndAsync(data.Assignment, data.Staff.AccountId);
        Assert.Equal(end.MeetingEndAt, (await data.Service.EndAsync(data.Assignment, data.Staff.AccountId)).MeetingEndAt);
        Assert.Null(end.JoinUrl);
        var assignment = await f.Db.VerificationAssignments.SingleAsync();
        Assert.Equal(end.MeetingEndAt!.Value.AddHours(48), assignment.ReportDueAt);
        Assert.Equal(VerificationAssignmentStatus.Assigned, assignment.Status);
        Assert.Null(assignment.CompletedAt);
        Assert.Equal("INVALID_MEETING_STATE", (await Assert.ThrowsAsync<MeetingException>(() =>
            data.Service.StartAsync(data.Assignment, data.Staff.AccountId))).Code);
    }

    [Fact]
    public async Task MeetingRejectsUnrelatedAccountsLeaderLifecycleAndLegacyAdminLifecycle()
    {
        await using var f = await Fixture.CreateAsync();
        var data = await PrepareMeetingAsync(f);
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<MeetingException>(() =>
            data.Service.GetOrCreateRoomAsync(data.Assignment, f.Other.Id))).Code);
        Assert.Null((await f.Db.ProjectVerifications.SingleAsync()).MeetingRoomName);
        await data.Service.GetOrCreateRoomAsync(data.Assignment, f.Owner.Id);
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<MeetingException>(() =>
            data.Service.StartAsync(data.Assignment, f.Owner.Id))).Code);
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<AssignmentException>(() =>
            new AssignmentService(f.Db, f.Configuration).UpdateAsync(data.Assignment, f.Other.Id,
                new(AssignmentAction.StartMeeting)))).Code);
        data.Staff.Account.Status = AccountStatus.Suspended;
        await f.Db.SaveChangesAsync();
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<MeetingException>(() =>
            data.Service.GetAsync(data.Assignment, data.Staff.AccountId))).Code);
    }

    [Fact]
    public async Task MeetingRejectsPrematureTransitionsAndReplacedAssignments()
    {
        await using var f = await Fixture.CreateAsync();
        var data = await PrepareMeetingAsync(f);
        Assert.Equal("MEETING_ROOM_REQUIRED", (await Assert.ThrowsAsync<MeetingException>(() =>
            data.Service.StartAsync(data.Assignment, data.Staff.AccountId))).Code);
        await data.Service.GetOrCreateRoomAsync(data.Assignment, f.Owner.Id);
        Assert.Equal("INVALID_MEETING_STATE", (await Assert.ThrowsAsync<MeetingException>(() =>
            data.Service.EndAsync(data.Assignment, data.Staff.AccountId))).Code);
        var verification = await f.Db.ProjectVerifications.SingleAsync();
        verification.ScheduledStartAt = DateTimeOffset.UtcNow.AddHours(1);
        verification.ScheduledEndAt = DateTimeOffset.UtcNow.AddHours(2);
        await f.Db.SaveChangesAsync();
        Assert.Equal("INVALID_MEETING_STATE", (await Assert.ThrowsAsync<MeetingException>(() =>
            data.Service.StartAsync(data.Assignment, data.Staff.AccountId))).Code);
        (await f.Db.VerificationAssignments.SingleAsync()).ReplacedAt = DateTimeOffset.UtcNow;
        await f.Db.SaveChangesAsync();
        Assert.Equal("ASSIGNMENT_LOCKED", (await Assert.ThrowsAsync<MeetingException>(() =>
            data.Service.GetAsync(data.Assignment, f.Owner.Id))).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://meet.example.com")]
    [InlineData("https://user:secret@meet.example.com")]
    [InlineData("https://meet.example.com?token=secret")]
    [InlineData("https://meet.example.com#room")]
    [InlineData("https://meet.example.com/path")]
    public async Task MeetingRejectsInvalidConfigurationBeforePersistingRoom(string baseUrl)
    {
        await using var f = await Fixture.CreateAsync();
        var data = await PrepareMeetingAsync(f);
        f.Configuration["Jitsi:BaseUrl"] = baseUrl;
        Assert.Equal("JITSI_NOT_CONFIGURED", (await Assert.ThrowsAsync<MeetingException>(() =>
            data.Service.GetOrCreateRoomAsync(data.Assignment, f.Owner.Id))).Code);
        Assert.Null((await f.Db.ProjectVerifications.SingleAsync()).MeetingRoomName);
    }
}
