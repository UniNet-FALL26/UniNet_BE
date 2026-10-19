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
        var evidence = await f.Db.ProjectModerations.SingleAsync();
        evidence.AttemptNumber = 999;
        evidence.Status = ProjectModerationStatus.Rejected;
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
        evidence.Status = ProjectModerationStatus.Passed;
        await f.Db.SaveChangesAsync();
        Assert.Equal(0, (await service.DiscoverPublicProjectsAsync()).TotalItems);
    }

    [Fact]
    public async Task SubmissionQueueAndLatestIgnoreHigherEvidenceAttemptNumbers()
    {
        await using var f = await Fixture.CreateAsync();
        var evidence = await f.Db.ProjectModerations.SingleAsync();
        evidence.AttemptNumber = 999;
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
        var evidence = await f.Db.ProjectModerations.SingleAsync();
        evidence.Status = ProjectModerationStatus.Processing;
        evidence.UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1);
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
        Assert.Equal(ProjectModerationStatus.Processing, evidence.Status);
    }

    [Theory]
    [InlineData("attempt")]
    [InlineData("notification")]
    [InlineData("case")]
    public async Task DatabaseRejectsEvidenceReferencesToSubmission(string target)
    {
        await using var f = await Fixture.CreateAsync();
        var submission = new ProjectModeration { ProjectId = f.Project.Id, AttemptNumber = 1,
            Status = ProjectModerationStatus.Approved, ContentHash = "submission" };
        f.Db.ProjectModerations.Add(submission);
        await f.Db.SaveChangesAsync();
        if (target == "case")
        {
            await f.CreateVerificationAsync();
            (await f.Db.ProjectVerifications.SingleAsync()).EvidenceModerationId = submission.Id;
        }
        else if (target == "attempt")
            f.Db.EvidenceModerationAttempts.Add(new() { ModerationId = submission.Id, Number = 1, StartedAt = DateTimeOffset.UtcNow });
        else
            f.Db.Add(new VerificationNotification { Id = Guid.NewGuid(), RecipientUserId = f.Owner.Profile!.Id,
                EvidenceModerationId = submission.Id, EventKey = "test", Message = "test", CreatedAt = DateTimeOffset.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(ProjectModerationStatus.Pending, EvidenceModerationStatus.Pending)]
    [InlineData(ProjectModerationStatus.Processing, EvidenceModerationStatus.Processing)]
    [InlineData(ProjectModerationStatus.Passed, EvidenceModerationStatus.Passed)]
    [InlineData(ProjectModerationStatus.Rejected, EvidenceModerationStatus.Rejected)]
    [InlineData(ProjectModerationStatus.NeedsEvidence, EvidenceModerationStatus.NeedsEvidence)]
    [InlineData(ProjectModerationStatus.Error, EvidenceModerationStatus.Error)]
    public void EvidenceApiRetainsItsStatusContract(ProjectModerationStatus stored, EvidenceModerationStatus api)
    {
        Assert.Equal(api, EvidenceModerationMapping.ToEvidenceStatus(new() { Purpose = ProjectModerationPurpose.ProjectEvidence, Status = stored }));
        Assert.Throws<InvalidOperationException>(() => EvidenceModerationMapping.ToEvidenceStatus(new() { Status = stored }));
    }
}
