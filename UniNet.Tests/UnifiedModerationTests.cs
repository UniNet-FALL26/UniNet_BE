using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using Xunit;

namespace UniNet.Tests;

public sealed partial class ProjectVerificationTests
{
    [Fact]
    public async Task DiscoveryUsesSubmissionDecisionWhenEvidenceIsNewer()
    {
        await using var f = await Fixture.CreateAsync();
        f.Project.Status = ProjectStatus.Active;
        f.Project.Visibility = ProjectVisibility.Public;
        f.Project.RecruitmentStatus = RecruitmentStatus.Open;
        f.Project.RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(5);
        var submission = new ProjectModeration { ProjectId = f.Project.Id, AttemptNumber = 1,
            Status = ProjectModerationStatus.Approved, ContentHash = "submission" };
        f.Db.ProjectModerations.Add(submission);
        await f.Db.SaveChangesAsync();
        var service = new ProjectService(f.Db, null!, f.Configuration);
        Assert.Equal(1, (await service.DiscoverPublicProjectsAsync()).TotalItems);
        submission.Status = ProjectModerationStatus.Rejected;
        await f.Db.SaveChangesAsync();
        Assert.Equal(0, (await service.DiscoverPublicProjectsAsync()).TotalItems);
    }

    [Fact]
    public async Task SubmissionQueueAndLatestIgnoreHigherEvidenceAttemptNumbers()
    {
        await using var f = await Fixture.CreateAsync();
        var submission = new ProjectModeration { ProjectId = f.Project.Id, AttemptNumber = 1,
            Status = ProjectModerationStatus.ReviewRequired, ContentHash = "submission" };
        f.Db.ProjectModerations.Add(submission);
        await f.Db.SaveChangesAsync();
        var service = new ProjectModerationService(new ModerationProbe(), f.Db, f.Configuration);
        var pending = await service.GetPendingProjectsAsync(1, 10, default);
        Assert.Equal(1, pending.TotalItems);
        Assert.Equal(submission.Id, (await service.GetLatestProjectModerationAsync(f.Project.Id, f.Owner.Id, default)).Id);
        Assert.Equal(ProjectStatus.Completed, (await f.Db.Projects.SingleAsync()).Status);
    }

    [Fact]
    public async Task EvidenceProcessingDoesNotBlockSubmissionOrBecomeSubmissionRetryWork()
    {
        await using var f = await Fixture.CreateAsync();
        f.Project.Status = ProjectStatus.Rejected;
        f.Configuration["ProjectModeration:AutoApproveConfidence"] = "0.9";
        f.Db.ProjectModerations.Add(new() { ProjectId = f.Project.Id, AttemptNumber = 1,
            ContentHash = "old", Status = ProjectModerationStatus.Rejected });
        await f.Db.SaveChangesAsync();
        var probe = new ModerationProbe();
        var service = new ProjectModerationService(probe, f.Db, f.Configuration);
        await service.RetryTransientErrorsAsync(DateTimeOffset.UtcNow, default);
        var result = await service.SubmitProjectAsync(f.Project.Id, f.Owner.Id, default);
        Assert.Equal(ProjectModerationStatus.Approved, result.Status);
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task CurrentSchemaRejectsRetiredEvidencePurpose()
    {
        await using var f = await Fixture.CreateAsync();
        f.Db.ProjectModerations.Add(new() { ProjectId = f.Project.Id, Purpose = ProjectModerationPurpose.ProjectEvidence });
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        var model = f.Db.Model;
        Assert.Null(model.FindEntityType(typeof(ProjectModeration))!.FindProperty("SnapshotJson"));
        Assert.Null(model.FindEntityType(typeof(ProjectVerification))!.FindProperty("EvidenceModerationId"));
        Assert.DoesNotContain(model.GetEntityTypes(), e => e.GetTableName() == "EvidenceModerationAttempts");
    }

    [Fact]
    public async Task NotificationsStoreAccountRecipientAndPolymorphicTargetWithoutBusinessForeignKeys()
    {
        await using var f = await Fixture.CreateAsync();
        var target = Guid.NewGuid();
        await NotificationService.EnqueueAsync(f.Db, [f.Owner.Id],
            new("verification.evidence.updated", "Verification", "Cập nhật minh chứng", "Có cập nhật minh chứng.",
                "EvidenceModeration", target, EventKey: "polymorphic-test"));
        await f.Db.SaveChangesAsync();
        var notice = await f.Db.Set<VerificationNotification>().SingleAsync();
        Assert.Equal(f.Owner.Id, notice.RecipientUserId);
        Assert.Equal(target, notice.TargetId);
        Assert.Equal("EvidenceModeration", notice.TargetType);
        Assert.DoesNotContain(f.Db.Model.FindEntityType(typeof(VerificationNotification))!.GetForeignKeys(),
            fk => fk.Properties.Any(p => p.Name == nameof(VerificationNotification.TargetId)));
    }

}
