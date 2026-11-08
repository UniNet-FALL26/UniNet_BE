using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Enums;
using Xunit;

namespace UniNet.Tests;

public sealed partial class ProjectVerificationTests
{
    [Fact]
    public async Task CompletionAndTaskChangesDoNotCreateEvidenceReviews()
    {
        await using var f = await Fixture.CreateAsync();
        f.Project.Status = ProjectStatus.Active;
        await f.Db.SaveChangesAsync();
        await new ProjectService(f.Db, null!, f.Configuration).UpdateStatusAsync(f.Owner.Id,
            new(f.Project.Id, ProjectStatus.Completed), default);
        var tasks = new ProjectTaskService(f.Db, f.Configuration);
        var task = await tasks.CreateProjectTaskAsync(f.Project.Id, f.Owner.Id,
            new("Retained task", "Task changes must not queue retired L1 work.", DateTimeOffset.UtcNow.AddDays(3)));
        await tasks.UpdateProjectTaskAsync(f.Project.Id, task.Id, f.Owner.Id, new(Title: "Updated task"));
        Assert.Empty(await f.Db.ProjectModerations.ToListAsync());
    }

    [Fact]
    public async Task ProjectWithoutEvidenceCanScheduleAndReceiveAdminAssignmentAfterPayment()
    {
        await using var f = await Fixture.CreateAsync(passed: false);
        f.Other.Role = AccountRole.Admin;
        await f.Db.SaveChangesAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        var slot = Fixture.Slot();
        await f.Service.ScheduleMeetingAsync(id, f.Owner.Id, slot);
        var staff = await f.Db.UserProfiles.SingleAsync(p => p.Account.Role == AccountRole.Assessor);
        var assignments = new AssignmentService(f.Db, f.Configuration);
        var unpaid = await Assert.ThrowsAsync<AssignmentException>(() => assignments.CreateAsync(f.Other.Id, new(id, staff.Id)));
        Assert.Equal("BOOKING_NOT_CONFIRMED", unpaid.Code);
        await f.PayAsync(id, f.Owner);
        var assignment = await assignments.CreateAsync(f.Other.Id, new(id, staff.Id));
        Assert.Empty(await f.Db.ProjectModerations.ToListAsync());
    }

    [Theory]
    [InlineData("EvidenceModeration")]
    [InlineData("ProjectModeration")]
    public async Task HistoricalEvidenceNotificationsCannotOpenRetiredReview(string targetType)
    {
        await using var f = await Fixture.CreateAsync();
        var legacyId = Guid.NewGuid();
        var notifications = new NotificationService(f.Db);
        await notifications.SendToUserAsync(f.Owner.Id, new("legacy.review", "Verification", "Historical review",
            "This retired review remains in notification history.", targetType, legacyId));
        var notice = Assert.Single((await notifications.GetMineAsync(f.Owner.Id)).Items);
        var error = await Assert.ThrowsAsync<ProjectVerificationException>(() =>
            new NotificationTargets(f.Db, notifications).ResolveAsync(f.Owner.Id, notice.Id, default));
        Assert.Equal("TARGET_UNAVAILABLE", error.Code);
        Assert.Equal(404, error.Status);
    }

    [Fact]
    public async Task CompletedProjectRequirementStillAppliesWithoutEvidence()
    {
        await using var f = await Fixture.CreateAsync(passed: false);
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        f.Project.Status = ProjectStatus.Active;
        await f.Db.SaveChangesAsync();
        var slot = Fixture.Slot();
        await Error("PROJECT_NOT_COMPLETED", () => f.Service.GetAvailableMeetingSlotsAsync(id, f.Owner.Id, slot.StartAt, slot.EndAt));
        await Error("PROJECT_NOT_COMPLETED", () => f.Service.ScheduleMeetingAsync(id, f.Owner.Id, slot));
    }

    [Fact]
    public async Task LegacyEvidencePolicyDoesNotBlockOverdueReplacement()
    {
        await using var f = await Fixture.CreateAsync(passed: false);
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        await f.PayAsync(id, f.Owner);
        var now = DateTimeOffset.UtcNow;
        await PrepareOverdueAsync(f, id, now.AddHours(-48));
        f.Db.Accounts.Add(Fixture.Account("Replacement", AccountRole.Assessor));
        await f.Db.SaveChangesAsync();
        var tasks = new VerificationDeadlineTasks(f.Db, f.Configuration);
        await tasks.MarkOverdueAndEscalateAsync(now, default);
        f.Configuration["EvidenceModeration:PolicyVersion"] = "changed";
        await tasks.MarkOverdueAndEscalateAsync(now.AddHours(24), default);
        Assert.Equal(2, await f.Db.VerificationAssignments.CountAsync());
        Assert.Equal(PaymentStatus.Success, (await f.Db.Transactions.SingleAsync()).Status);
    }

}
