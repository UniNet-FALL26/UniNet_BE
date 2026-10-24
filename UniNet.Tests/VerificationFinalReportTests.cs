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
    private static async Task<(Guid Case, Guid Assignment, UserProfile Staff)> PrepareFinalReportAsync(Fixture f)
    {
        f.Other.Role = AccountRole.Admin; await f.Db.SaveChangesAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        await f.PayAsync(id, f.Owner);
        var staff = await f.Db.UserProfiles.Include(p => p.Account).SingleAsync(p => p.Account.Role == AccountRole.Verificator);
        var assignment = await new AssignmentService(f.Db, f.Configuration).CreateAsync(f.Other.Id, new(id, staff.Id));
        var v = await f.Db.ProjectVerifications.SingleAsync();
        v.ScheduledStartAt = DateTimeOffset.UtcNow.AddHours(-3); v.ScheduledEndAt = DateTimeOffset.UtcNow.AddHours(-1);
        v.MeetingStartAt = DateTimeOffset.UtcNow.AddHours(-2); v.MeetingEndAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await f.Db.SaveChangesAsync();
        return (id, assignment.Id, staff);
    }

    private static SubmitReportRequest FinalReport(Guid assignment, ProjectVerificationStatus outcome = ProjectVerificationStatus.Verified)
        => new(assignment, "Final assessment", "Reviewed task evidence and meeting", "Recommended next steps", outcome);

    [Theory]
    [InlineData(ProjectVerificationStatus.Verified)]
    [InlineData(ProjectVerificationStatus.Failed)]
    public async Task ReportSubmitsOnceAndFinalizesCaseWithoutAdminReview(ProjectVerificationStatus outcome)
    {
        await using var f = await Fixture.CreateAsync(); var data = await PrepareFinalReportAsync(f);
        var service = new ReportService(f.Db);
        var result = await service.SubmitAsync(data.Staff.AccountId, FinalReport(data.Assignment, outcome));
        f.Db.ChangeTracker.Clear();
        var v = await f.Db.ProjectVerifications.SingleAsync(); var a = await f.Db.VerificationAssignments.SingleAsync();
        Assert.Equal(outcome, v.Status); Assert.NotNull(v.FinalizedAt); Assert.Equal(data.Staff.Id, v.FinalizedByUserId);
        Assert.Equal(VerificationAssignmentStatus.Completed, a.Status); Assert.NotNull(a.CompletedAt);
        Assert.Equal(data.Case, (await f.Db.VerificationReports.SingleAsync()).ProjectVerificationId);
        Assert.Equal(result.Id, (await service.GetCurrentAsync(data.Assignment, f.Owner.Id)).Id);
        Assert.Equal(result.Summary, (await service.GetDetailAsync(result.Id, f.Other.Id)).Summary);
        var audit = await f.Db.ReportAuditLogs.SingleAsync(); Assert.Equal("Submitted", audit.Action);
        Assert.DoesNotContain(f.Db.Model.GetEntityTypes(), e => e.ClrType.Name == "VerificationAdminAction");
        var error = await Assert.ThrowsAsync<ReportException>(() => service.SubmitAsync(data.Staff.AccountId, FinalReport(data.Assignment)));
        Assert.Equal("REPORT_EXISTS", error.Code); Assert.Single(await f.Db.VerificationReports.ToListAsync());
        Assert.Empty(await f.Db.RefundTransactions.ToListAsync());
    }

    [Fact]
    public async Task UnfinishedMeetingAndUnassignedStaffCannotSubmitReport()
    {
        await using var f = await Fixture.CreateAsync(); var data = await PrepareFinalReportAsync(f);
        (await f.Db.ProjectVerifications.SingleAsync()).MeetingEndAt = null; await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<ReportException>(() => new ReportService(f.Db).SubmitAsync(data.Staff.AccountId, FinalReport(data.Assignment)));
        Assert.Equal("MEETING_NOT_COMPLETED", error.Code);
        var stranger = Fixture.Account("Stranger", AccountRole.Verificator); f.Db.Accounts.Add(stranger); await f.Db.SaveChangesAsync();
        error = await Assert.ThrowsAsync<ReportException>(() => new ReportService(f.Db).SubmitAsync(stranger.Id, FinalReport(data.Assignment)));
        Assert.Equal("ASSIGNMENT_NOT_FOUND", error.Code);
        Assert.Empty(await f.Db.ReportAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task ReplacedStaffCannotSubmitButNewStaffCanFinishAfterMeeting()
    {
        await using var f = await Fixture.CreateAsync(); var data = await PrepareFinalReportAsync(f);
        var next = Fixture.Account("NewStaff", AccountRole.Verificator); f.Db.Accounts.Add(next); await f.Db.SaveChangesAsync();
        var replacement = await new AssignmentService(f.Db, f.Configuration).ReplaceVerificatorAsync(data.Assignment, f.Other.Id,
            new(next.Profile!.Id, "Reassign before report"));
        var error = await Assert.ThrowsAsync<ReportException>(() => new ReportService(f.Db).SubmitAsync(data.Staff.AccountId, FinalReport(data.Assignment)));
        Assert.Equal("ASSIGNMENT_LOCKED", error.Code);
        await new ReportService(f.Db).SubmitAsync(next.Id, FinalReport(replacement.Id));
        var original = await f.Db.VerificationAssignments.Include(a => a.ReplacedBy).SingleAsync(a => a.Id == data.Assignment);
        Assert.Equal(replacement.Id, original.ReplacedBy!.Id); Assert.Equal(f.Other.Profile!.Id, original.AdminId);
        Assert.Equal("Reassign before report", original.ReplacementReason);
    }

    [Fact]
    public async Task DatabaseRejectsSecondReportForSameCaseAndMismatchedCaseLinks()
    {
        await using var f = await Fixture.CreateAsync(); var data = await PrepareFinalReportAsync(f);
        await new ReportService(f.Db).SubmitAsync(data.Staff.AccountId, FinalReport(data.Assignment));
        var another = new VerificationAssignment { Id = Guid.NewGuid(), ProjectVerificationId = data.Case,
            AssignedToUserId = data.Staff.Id, AdminId = f.Other.Profile!.Id };
        f.Db.VerificationAssignments.Add(another); await f.Db.SaveChangesAsync();
        f.Db.VerificationReports.Add(new() { Id = Guid.NewGuid(), AssignmentId = another.Id,
            ProjectVerificationId = data.Case, Summary = "Duplicate" });
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear();
        var differentCase = (await f.CreateVerificationAsync()).Verification.Id;
        f.Db.VerificationReports.Add(new() { Id = Guid.NewGuid(), AssignmentId = another.Id,
            ProjectVerificationId = differentCase, Summary = "Wrong case association" });
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task SubmittedReportRemainsImmutableWithoutHistoricalEntities()
    {
        await using var f = await Fixture.CreateAsync(); var data = await PrepareFinalReportAsync(f);
        await new ReportService(f.Db).SubmitAsync(data.Staff.AccountId, FinalReport(data.Assignment));
        var stored = await f.Db.VerificationReports.SingleAsync(); stored.Summary = "Changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear(); stored = await f.Db.VerificationReports.SingleAsync(); f.Db.VerificationReports.Remove(stored);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
        Assert.DoesNotContain(f.Db.Model.GetEntityTypes(), e => e.ClrType.Name == "VerificationReportVersion");
    }

    [Fact]
    public async Task EvidenceWorkerPersistsFailureAndRetriesWithoutNotificationTable()
    {
        await using var f = await Fixture.CreateAsync(passed: false); await SeedEvidenceAsync(f); await CompleteAsync(f);
        await f.Db.Database.ExecuteSqlRawAsync("DROP TABLE VerificationNotification");
        var content = new ContentChecker { Error = new("Unavailable", true) }; var worker = EvidenceWorker(f, content, new());
        var now = DateTimeOffset.UtcNow;
        await worker.RunAsync(now, default); await worker.RunAsync(now.AddMinutes(2), default);
        Assert.Equal(2, content.Calls);
        var moderation = await f.Db.ProjectModerations.SingleAsync();
        Assert.Equal(ProjectModerationStatus.Error, moderation.Status); Assert.NotNull(moderation.NextRetryAt);
        Assert.Equal(2, await f.Db.EvidenceModerationAttempts.CountAsync());
    }

    [Fact]
    public async Task DeadlineAndRefundWorkersRunWithoutNotificationTableAndKeepSystemAudit()
    {
        await using var f = await Fixture.CreateAsync(); var id = (await f.CreateVerificationAsync()).Verification.Id;
        await f.PayAsync(id, f.Owner);
        var now = DateTimeOffset.UtcNow;
        (await f.Db.ProjectVerifications.SingleAsync()).AssignmentEligibleAt = now.AddHours(-24);
        await f.Db.SaveChangesAsync(); await f.Db.Database.ExecuteSqlRawAsync("DROP TABLE VerificationNotification");
        await new VerificationDeadlineTasks(f.Db, f.Configuration).RefundTimeoutsAsync(now, default);
        var refund = await f.Db.RefundTransactions.SingleAsync();
        Assert.Equal(RefundRequestSource.System, refund.RequestSource); Assert.Null(refund.RequestedByUserId);
        Assert.Equal(now, refund.RequestedAt);
        await new VerificationRefundTasks(f.Db, new ManualRefundProvider()).RunAsync(now, default);
        refund = await f.Db.RefundTransactions.SingleAsync();
        Assert.Equal(PaymentStatus.Pending, refund.Status); Assert.Equal("MANUAL_REFUND_REQUIRED", refund.FailureReason);
        Assert.Equal(RefundRequestSource.System, refund.RequestSource);
    }

    [Fact]
    public async Task OverdueEscalationAndReplacementRunWithoutNotificationTable()
    {
        await using var f = await Fixture.CreateAsync(); var id = (await f.CreateVerificationAsync()).Verification.Id;
        await f.PayAsync(id, f.Owner); var now = DateTimeOffset.UtcNow;
        await PrepareOverdueAsync(f, id, now.AddHours(-48));
        f.Db.Accounts.Add(Fixture.Account("Replacement", AccountRole.Verificator)); await f.Db.SaveChangesAsync();
        await f.Db.Database.ExecuteSqlRawAsync("DROP TABLE VerificationNotification");
        var tasks = new VerificationDeadlineTasks(f.Db, f.Configuration);
        await tasks.MarkOverdueAndEscalateAsync(now, default); await tasks.MarkOverdueAndEscalateAsync(now.AddHours(24), default);
        Assert.Equal(2, await f.Db.VerificationAssignments.CountAsync());
        var old = await f.Db.VerificationAssignments.SingleAsync(a => a.ReplacedAt != null);
        Assert.NotNull(old.OverdueMarkedAt); Assert.NotNull(old.EscalatedAt);
    }
}
