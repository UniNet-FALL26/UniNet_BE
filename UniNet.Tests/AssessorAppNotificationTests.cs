using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using Xunit;

namespace UniNet.Tests;

public sealed partial class ProjectVerificationTests
{
    [Fact]
    public async Task AssessorAssignmentNotificationOpensOwnedFeWorkAndNeverTheDashboard()
    {
        await using var f = await Fixture.CreateAsync();
        var meeting = await PrepareMeetingAsync(f);
        var assignment = await f.Db.VerificationAssignments.SingleAsync(a => a.Id == meeting.Assignment);
        var service = new NotificationService(f.Db); var targets = new NotificationTargets(f.Db, service);
        var own = await f.Db.Set<VerificationNotification>().FirstAsync(n => n.RecipientUserId == meeting.Staff.AccountId && n.TargetId == assignment.ProjectVerificationId);
        Assert.Equal($"/assessor-assessments/{meeting.Assignment}", await targets.ResolveAsync(meeting.Staff.AccountId, own.Id, default));
        var other = Fixture.Account("Unassigned expert", AccountRole.Assessor); f.Db.Add(other); await f.Db.SaveChangesAsync();
        await service.SendToUserAsync(other.Id, new("verification.assignment.created", "Verification", "Assignment", "Unassigned", "ProjectVerification", assignment.ProjectVerificationId, EventKey: "unassigned-expert"));
        var foreign = await f.Db.Set<VerificationNotification>().SingleAsync(n => n.RecipientUserId == other.Id);
        Assert.Equal(404, (await Assert.ThrowsAsync<ProjectVerificationException>(() => targets.ResolveAsync(other.Id, foreign.Id, default))).Status);
    }
}
