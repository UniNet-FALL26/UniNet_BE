using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using Xunit;

namespace UniNet.Tests;

public sealed partial class ProjectVerificationTests
{
    [Fact]
    public async Task ActiveMemberCanReadVerificationButCannotRegisterOrChangeBooking()
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        f.Db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = f.Project.Id, UserId = f.Other.Profile!.Id,
            Role = "Developer", Status = ProjectMemberStatus.Active
        });
        await f.Db.SaveChangesAsync();
        Assert.Equal(id, (await f.Service.GetDetailAsync(id, f.Other.Id)).Verification.Id);
        Assert.Equal(id, Assert.Single((await f.Service.GetMyVerificationsAsync(f.Other.Id)).Verifications).Id);
        Assert.Equal(id, Assert.Single((await f.Service.GetMyVerificationsAsync(f.Other.Id, f.Project.Id)).Verifications).Id);
        var verification = await f.Db.ProjectVerifications.SingleAsync();
        verification.Status = ProjectVerificationStatus.Verified;
        await f.Db.SaveChangesAsync();
        Assert.Equal(ProjectVerificationStatus.Verified, (await f.Service.GetFinalResultAsync(id, f.Other.Id)).Status);
        await Error("FORBIDDEN", () => f.Service.CreateAsync(f.Project.Id, f.Other.Id, new(f.Package.Id)));
        await Error("FORBIDDEN", () => f.Service.ScheduleMeetingAsync(id, f.Other.Id, Fixture.Slot()));
        await Error("FORBIDDEN", () => f.Service.CancelAsync(id, f.Other.Id, new("Cancel")));
    }

    [Theory]
    [InlineData(ProjectMemberStatus.Left, false)]
    [InlineData(ProjectMemberStatus.Kicked, false)]
    [InlineData(ProjectMemberStatus.Active, true)]
    public async Task FormerMemberCannotReadVerifications(ProjectMemberStatus status, bool hasLeftAt)
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        f.Db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = f.Project.Id, UserId = f.Other.Profile!.Id,
            Role = "Developer", Status = status,
            LeftAt = hasLeftAt ? DateTimeOffset.UtcNow : null
        });
        await f.Db.SaveChangesAsync();
        await Error("FORBIDDEN", () => f.Service.GetDetailAsync(id, f.Other.Id));
        await Error("FORBIDDEN", () => f.Service.GetFinalResultAsync(id, f.Other.Id));
        await Error("FORBIDDEN", () => f.Service.GetMyVerificationsAsync(f.Other.Id, f.Project.Id));
        Assert.Empty((await f.Service.GetMyVerificationsAsync(f.Other.Id)).Verifications);
    }

    [Theory]
    [InlineData("Developer")]
    [InlineData("Leader")]
    public async Task NonCreatorMemberCannotAccessMeetingEvenWhenLeader(string role)
    {
        await using var f = await Fixture.CreateAsync();
        var data = await PrepareMeetingAsync(f);
        f.Db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = f.Project.Id, UserId = f.Other.Profile!.Id,
            Role = role, Status = ProjectMemberStatus.Active
        });
        await f.Db.SaveChangesAsync();
        await data.Service.GetOrCreateRoomAsync(data.Assignment, f.Owner.Id);
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<MeetingException>(() =>
            data.Service.GetAsync(data.Assignment, f.Other.Id))).Code);
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<MeetingException>(() =>
            data.Service.GetOrCreateRoomAsync(data.Assignment, f.Other.Id))).Code);
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<MeetingException>(() =>
            data.Service.JoinAsync(data.Assignment, f.Other.Id))).Code);
    }
}
