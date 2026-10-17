using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.DTOs.Projects;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using Xunit;

namespace UniNet.Tests;

public sealed partial class ProjectVerificationTests
{
    private sealed class ContentChecker : IEvidenceContentReviewer
    {
        public int Calls;
        public Func<Task>? DuringCall;
        public VerificationAiException? Error;
        public bool Pass = true;
        public async Task<EvidenceContentVerdict> ReviewContentAsync(string text, EvidencePolicySnapshot policy, CancellationToken ct)
        {
            Calls++;
            if (DuringCall != null) await DuringCall();
            if (Error != null) throw Error;
            return new(Pass, Pass ? "Policy satisfied" : "Contextual violation", "{\"reviewed\":true}");
        }
    }

    private sealed class UrlChecker : IEvidenceUrlChecker
    {
        public List<string> Seen = [];
        public VerificationAiException? Error;
        public bool Pass = true;
        public Task<EvidenceUrlVerdict> CheckAsync(string url, EvidencePolicySnapshot policy, CancellationToken ct)
        {
            Seen.Add(url);
            if (Error != null) throw Error;
            return Task.FromResult(new EvidenceUrlVerdict(Pass, Pass ? "No list match" : "MALWARE match", "{}"));
        }
    }

    private static void ConfigureEvidence(Fixture f)
    {
        f.Configuration["EvidenceModeration:PolicyVersion"] = "test-v1";
        f.Configuration["EvidenceModeration:ContentInstructions"] = "Reject descriptions promoting illegal activity. Respect legitimate educational context.";
        f.Configuration["EvidenceModeration:ForbiddenTerms:0"] = "cấm";
        f.Configuration["EvidenceModeration:TermMatchMode"] = "WholeWord";
        f.Configuration["EvidenceModeration:TextNormalization"] = "FormC";
        f.Configuration["EvidenceModeration:CaseSensitive"] = "false";
        f.Configuration["SafeBrowsing:ThreatTypes:0"] = "MALWARE";
        f.Configuration["SafeBrowsing:PlatformTypes:0"] = "ANY_PLATFORM";
    }

    private static async Task<TaskReport> SeedEvidenceAsync(Fixture f, bool file = true, string text = "Completed work")
    {
        ConfigureEvidence(f);
        var task = new ProjectTask { ProjectId = f.Project.Id, CreatorId = f.Owner.Profile!.Id,
            Title = "Task", Description = "Task work", Status = ProjectTaskStatus.Cancelled, Deadline = DateTimeOffset.UtcNow.AddDays(1) };
        task.Assignments.Add(new() { UserId = f.Owner.Profile!.Id });
        var report = new TaskReport { ProjectTask = task, SubmittedById = f.Owner.Profile.Id, ReportGroupId = Guid.NewGuid(), Version = 1,
            Content = text, Status = TaskReportStatus.Submitted };
        if (file) report.Evidence.Add(new() { FileUrl = "https://example.com/proof.pdf", FileName = "proof.pdf", Format = "pdf" });
        f.Db.TaskReports.Add(report);
        f.Project.Status = ProjectStatus.Active;
        await f.Db.SaveChangesAsync();
        return report;
    }

    private static Task<ProjectStatusResponse> CompleteAsync(Fixture f) =>
        new ProjectService(f.Db, null!, f.Configuration).UpdateStatusAsync(f.Owner.Id, new(f.Project.Id, ProjectStatus.Completed), default);

    private static EvidenceModerationTasks EvidenceWorker(Fixture f, ContentChecker content, UrlChecker urls) =>
        new(f.Db, f.Configuration, new(content, urls), new(f.Db));

    [Fact]
    public async Task CompletionSnapshotsLatestReportPerGroupIncludingUnapprovedAndCancelledTasks()
    {
        await using var f = await Fixture.CreateAsync();
        var old = await SeedEvidenceAsync(f);
        var revised = new TaskReport { ProjectTaskId = old.ProjectTaskId, SubmittedById = old.SubmittedById,
            ReportGroupId = old.ReportGroupId, ParentReportId = old.Id,
            Version = 2, Content = "Latest content", Status = TaskReportStatus.RevisionRequested,
            Evidence = [new() { FileUrl = "https://example.com/latest" }] };
        var another = new TaskReport { ProjectTaskId = old.ProjectTaskId, SubmittedById = f.Other.Profile!.Id,
            ReportGroupId = Guid.NewGuid(), Version = 1, Content = "Other student's submission", Status = TaskReportStatus.Approved,
            Evidence = [new() { FileUrl = "https://example.com/other" }] };
        f.Db.TaskReports.AddRange(revised, another); await f.Db.SaveChangesAsync();
        await CompleteAsync(f);
        var m = await f.Db.ProjectEvidenceModerations.SingleAsync();
        Assert.Equal(EvidenceModerationStatus.Pending, m.Status);
        Assert.Equal(1, m.Version); Assert.Equal(64, m.SourceHash.Length);
        var snapshot = EvidenceModerationMapping.ToResponse(m).Source;
        var task = Assert.Single(snapshot.Tasks);
        Assert.Equal(ProjectTaskStatus.Cancelled, task.Status);
        Assert.Equal(2, task.Reports.Count);
        Assert.DoesNotContain(task.Reports, r => r.TaskReportId == old.Id);
        Assert.Contains(task.Reports, r => r.TaskReportId == revised.Id && r.Version == 2 && r.Status == TaskReportStatus.RevisionRequested);
        Assert.Equal(revised.Evidence[0].Id, task.Reports.Single(r => r.TaskReportId == revised.Id).Evidence[0].TaskEvidenceId);
        await Assert.ThrowsAsync<ProjectException>(() => CompleteAsync(f));
        await using var tx = await f.Db.Database.BeginTransactionAsync();
        await new EvidenceModerationSource(f.Db, f.Configuration).RefreshAsync(f.Project.Id, DateTimeOffset.UtcNow, default);
        await tx.CommitAsync();
        Assert.Single(await f.Db.ProjectEvidenceModerations.ToListAsync());
        Assert.Empty(await f.Db.ProjectVerifications.ToListAsync());
        Assert.Empty(await f.Db.Transactions.ToListAsync());
    }

    [Fact]
    public async Task SnapshotFailureRollsBackCompletedTransition()
    {
        await using var f = await Fixture.CreateAsync(); await SeedEvidenceAsync(f);
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailSnapshot BEFORE INSERT ON ProjectEvidenceModerations BEGIN SELECT RAISE(ABORT, 'snapshot failed'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => CompleteAsync(f));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(ProjectStatus.Active, (await f.Db.Projects.SingleAsync()).Status);
        Assert.Empty(await f.Db.ProjectEvidenceModerations.ToListAsync());
        Assert.Empty(await f.Db.ContributionCycles.ToListAsync());
        Assert.Null((await f.Db.Projects.SingleAsync()).CompletedAt);
    }

    [Fact]
    public async Task EmptyEvidenceNeverPassesOrCallsProviders()
    {
        await using var f = await Fixture.CreateAsync(); await SeedEvidenceAsync(f, file: false); await CompleteAsync(f);
        var content = new ContentChecker(); var urls = new UrlChecker();
        await EvidenceWorker(f, content, urls).RunAsync(DateTimeOffset.UtcNow, default);
        Assert.Equal(EvidenceModerationStatus.NeedsEvidence, (await f.Db.ProjectEvidenceModerations.SingleAsync()).Status);
        Assert.Equal(0, content.Calls); Assert.Empty(urls.Seen);
    }

    [Fact]
    public async Task PassedOnlyCoversTextAndUrlsWithExplicitFileExclusion()
    {
        await using var f = await Fixture.CreateAsync();
        var report = await SeedEvidenceAsync(f, text: "Work is at https://example.com/demo."); await CompleteAsync(f);
        var content = new ContentChecker(); var urls = new UrlChecker();
        await EvidenceWorker(f, content, urls).RunAsync(DateTimeOffset.UtcNow, default);
        var m = await f.Db.ProjectEvidenceModerations.Include(m => m.Attempts).SingleAsync();
        var dto = EvidenceModerationMapping.ToResponse(m); var attempt = Assert.Single(dto.Attempts);
        Assert.Equal(EvidenceModerationStatus.Passed, dto.Status);
        Assert.Equal(2, urls.Seen.Count); Assert.Equal(1, content.Calls);
        Assert.All(attempt.Results, r => Assert.Equal(report.Id, r.TaskReportId));
        Assert.Contains(attempt.Results, r => r.Kind == "FileContent" && r.Status == EvidenceCheckStatus.NotChecked);
        Assert.Contains("not inspected", dto.Summary!);
        await EvidenceWorker(f, content, urls).RunAsync(DateTimeOffset.UtcNow.AddHours(1), default);
        Assert.Equal(1, content.Calls);
    }

    [Theory]
    [InlineData("CẤM", "FormC", false, true)]
    [InlineData("ca\u0302\u0301m", "FormC", false, true)]
    [InlineData("cam", "StripDiacritics", false, true)]
    [InlineData("cam", "FormC", false, false)]
    [InlineData("CẤM", "FormC", true, false)]
    [InlineData("cấmabc", "FormC", false, false)]
    public async Task ForbiddenTermsFollowExplicitCaseAccentAndWordPolicy(string text, string normalization, bool caseSensitive, bool rejected)
    {
        await using var f = await Fixture.CreateAsync(); await SeedEvidenceAsync(f, text: text);
        f.Configuration["EvidenceModeration:TextNormalization"] = normalization;
        f.Configuration["EvidenceModeration:CaseSensitive"] = caseSensitive.ToString();
        await CompleteAsync(f);
        var content = new ContentChecker();
        await EvidenceWorker(f, content, new()).RunAsync(DateTimeOffset.UtcNow, default);
        var m = await f.Db.ProjectEvidenceModerations.SingleAsync();
        Assert.Equal(rejected ? EvidenceModerationStatus.Rejected : EvidenceModerationStatus.Passed, m.Status);
        Assert.Equal(rejected ? 0 : 1, content.Calls);
    }

    [Fact]
    public async Task UnsafeUrlAndContextualContentViolationsAreRecordedAgainstTheirSource()
    {
        await using var f = await Fixture.CreateAsync(); var report = await SeedEvidenceAsync(f); await CompleteAsync(f);
        await EvidenceWorker(f, new() { Pass = false }, new() { Pass = false }).RunAsync(DateTimeOffset.UtcNow, default);
        var m = await f.Db.ProjectEvidenceModerations.Include(m => m.Attempts).SingleAsync();
        Assert.Equal(EvidenceModerationStatus.Rejected, m.Status);
        var results = EvidenceModerationMapping.ToResponse(m).Attempts.Single().Results;
        Assert.Contains(results, r => r.Kind == "ReportText" && r.Status == EvidenceCheckStatus.Rejected);
        Assert.Contains(results, r => r.Kind == "Url" && r.Status == EvidenceCheckStatus.Rejected && r.TaskEvidenceId == report.Evidence.Single().Id);
    }

    [Fact]
    public async Task TransientProviderErrorsRetryWithHistoryAndNeverBecomePassedPrematurely()
    {
        await using var f = await Fixture.CreateAsync(); await SeedEvidenceAsync(f); await CompleteAsync(f);
        var content = new ContentChecker { Error = new("Timeout", true, "AI_TIMEOUT") }; var urls = new UrlChecker();
        var worker = EvidenceWorker(f, content, urls); var now = DateTimeOffset.UtcNow;
        await worker.RunAsync(now, default); await worker.RunAsync(now, default);
        Assert.Equal(1, content.Calls);
        var first = await f.Db.ProjectEvidenceModerations.SingleAsync();
        Assert.Equal(EvidenceModerationStatus.Error, first.Status); Assert.NotNull(first.NextRetryAt);
        content.Error = null; await worker.RunAsync(first.NextRetryAt!.Value.AddSeconds(1), default);
        var result = await f.Db.ProjectEvidenceModerations.Include(m => m.Attempts).SingleAsync();
        Assert.Equal(EvidenceModerationStatus.Passed, result.Status); Assert.Equal(2, result.Attempts.Count);
        Assert.Contains(result.Attempts, a => a.ErrorCode == "AI_TIMEOUT" && a.Transient);
    }

    [Fact]
    public async Task UrlProviderFailureLeavesSuccessfulContentCheckButBlocksPassing()
    {
        await using var f = await Fixture.CreateAsync(); await SeedEvidenceAsync(f); await CompleteAsync(f);
        await EvidenceWorker(f, new(), new() { Error = new("No URL credentials", false, "URL_NOT_CONFIGURED") })
            .RunAsync(DateTimeOffset.UtcNow, default);
        var m = await f.Db.ProjectEvidenceModerations.Include(m => m.Attempts).SingleAsync();
        Assert.Equal(EvidenceModerationStatus.Error, m.Status); Assert.Null(m.NextRetryAt);
        var results = EvidenceModerationMapping.ToResponse(m).Attempts.Single().Results;
        Assert.Contains(results, r => r.Kind == "ReportText" && r.Status == EvidenceCheckStatus.Passed);
        Assert.Contains(results, r => r.Kind == "Url" && r.Status == EvidenceCheckStatus.Error && r.ErrorCode == "URL_NOT_CONFIGURED");
    }

    [Theory]
    [InlineData("See www.example.com", "www.example.com")]
    [InlineData("[Proof](javascript:alert(1))", "javascript:alert(1)")]
    [InlineData("[Proof](relative-evidence.pdf)", "relative-evidence.pdf")]
    public async Task ExplicitUncheckableLinksAreNotSilentlyOmitted(string text, string expected)
    {
        await using var f = await Fixture.CreateAsync(); await SeedEvidenceAsync(f, text: text); await CompleteAsync(f);
        var urls = new UrlChecker { Error = new("Unsupported URL", false, "URL_NOT_CHECKABLE") };
        await EvidenceWorker(f, new(), urls).RunAsync(DateTimeOffset.UtcNow, default);
        Assert.Contains(expected, urls.Seen);
        Assert.Equal(EvidenceModerationStatus.Error, (await f.Db.ProjectEvidenceModerations.SingleAsync()).Status);
    }

    [Fact]
    public async Task MissingPolicyStopsAndNotifiesAdminsWithoutInventingDefaults()
    {
        await using var f = await Fixture.CreateAsync(); await SeedEvidenceAsync(f);
        f.Other.Role = AccountRole.Admin; await f.Db.SaveChangesAsync();
        f.Configuration["EvidenceModeration:PolicyVersion"] = null; await CompleteAsync(f);
        var content = new ContentChecker(); var worker = EvidenceWorker(f, content, new());
        await worker.RunAsync(DateTimeOffset.UtcNow, default); await worker.RunAsync(DateTimeOffset.UtcNow.AddDays(1), default);
        var m = await f.Db.ProjectEvidenceModerations.SingleAsync();
        Assert.Equal(EvidenceModerationStatus.Error, m.Status); Assert.Null(m.NextRetryAt); Assert.Equal(0, content.Calls);
        var notification = Assert.Single(await f.Db.Set<VerificationNotification>().ToListAsync());
        Assert.Equal(m.Id, notification.EvidenceModerationId); Assert.Null(notification.VerificationId);
        f.Configuration["EvidenceModeration:PolicyVersion"] = "fixed-v2";
        await worker.RunAsync(DateTimeOffset.UtcNow.AddDays(2), default);
        Assert.Equal(2, await f.Db.ProjectEvidenceModerations.CountAsync());
        Assert.Equal(EvidenceModerationStatus.Passed, (await f.Db.ProjectEvidenceModerations.SingleAsync(m => m.SupersededAt == null)).Status);
    }

    [Fact]
    public async Task RetryBudgetExhaustionNotifiesOnceAndPreservesAllAttempts()
    {
        await using var f = await Fixture.CreateAsync(); await SeedEvidenceAsync(f);
        f.Other.Role = AccountRole.Admin; await f.Db.SaveChangesAsync(); await CompleteAsync(f);
        var content = new ContentChecker { Error = new("Unavailable", true) }; var worker = EvidenceWorker(f, content, new());
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            await worker.RunAsync(now, default);
            var current = await f.Db.ProjectEvidenceModerations.SingleAsync();
            now = current.NextRetryAt?.AddSeconds(1) ?? now.AddDays(1);
        }
        await worker.RunAsync(now, default);
        Assert.Equal(5, content.Calls); Assert.Equal(5, await f.Db.EvidenceModerationAttempts.CountAsync());
        Assert.Single(await f.Db.Set<VerificationNotification>().ToListAsync());
        Assert.Null((await f.Db.ProjectEvidenceModerations.SingleAsync()).NextRetryAt);
    }

    [Fact]
    public async Task AbandonedProcessingIsRecoveredWithAuditAndBackoff()
    {
        await using var f = await Fixture.CreateAsync(); await SeedEvidenceAsync(f); await CompleteAsync(f);
        var now = DateTimeOffset.UtcNow; var m = await f.Db.ProjectEvidenceModerations.SingleAsync();
        m.Status = EvidenceModerationStatus.Processing; m.LeaseToken = Guid.NewGuid();
        m.LeaseExpiresAt = now.AddTicks(-1); m.AttemptCount = 1;
        f.Db.EvidenceModerationAttempts.Add(new() { ModerationId = m.Id, Number = 1, StartedAt = now.AddMinutes(-11) });
        await f.Db.SaveChangesAsync();
        var content = new ContentChecker(); var worker = EvidenceWorker(f, content, new());
        await worker.RunAsync(now, default);
        Assert.Equal(0, content.Calls);
        var recovered = await f.Db.ProjectEvidenceModerations.SingleAsync();
        Assert.Equal("WORKER_LEASE_EXPIRED", recovered.LastErrorCode);
        await worker.RunAsync(recovered.NextRetryAt!.Value.AddSeconds(1), default);
        Assert.Equal(1, content.Calls);
        Assert.Equal(2, await f.Db.EvidenceModerationAttempts.CountAsync());
    }

    [Fact]
    public async Task SourceChangeDuringProviderCallRetainsOldResultWithoutApplyingIt()
    {
        await using var f = await Fixture.CreateAsync(); var report = await SeedEvidenceAsync(f); await CompleteAsync(f);
        var content = new ContentChecker { DuringCall = async () =>
        {
            f.Db.TaskReports.Add(new() { ProjectTaskId = report.ProjectTaskId, SubmittedById = report.SubmittedById,
                ReportGroupId = report.ReportGroupId, ParentReportId = report.Id,
                Version = 2, Content = "Changed", Evidence = [new() { FileUrl = "https://example.com/new" }] });
            await f.Db.SaveChangesAsync();
        } };
        await EvidenceWorker(f, content, new()).RunAsync(DateTimeOffset.UtcNow, default);
        var old = await f.Db.ProjectEvidenceModerations.Include(m => m.Attempts).SingleAsync(m => m.Version == 1);
        var current = await f.Db.ProjectEvidenceModerations.SingleAsync(m => m.Version == 2);
        Assert.NotNull(old.SupersededAt); Assert.NotEqual(EvidenceModerationStatus.Passed, old.Status);
        Assert.Equal("SOURCE_SUPERSEDED", old.Attempts.Single().ErrorCode);
        Assert.Equal(EvidenceModerationStatus.Pending, current.Status);
        Assert.DoesNotContain("Changed", old.SnapshotJson); Assert.Contains("Changed", current.SnapshotJson);
    }

    [Fact]
    public async Task TaskReportRevisionAutomaticallyInvalidatesCompletedProjectSnapshot()
    {
        await using var f = await Fixture.CreateAsync(); var report = await SeedEvidenceAsync(f);
        report.Status = TaskReportStatus.RevisionRequested; await f.Db.SaveChangesAsync(); await CompleteAsync(f);
        await EvidenceWorker(f, new(), new()).RunAsync(DateTimeOffset.UtcNow, default);
        var oldJson = (await f.Db.ProjectEvidenceModerations.SingleAsync()).SnapshotJson;
        await new ProjectTaskService(f.Db, f.Configuration).ReviseTaskReportAsync(f.Project.Id, report.ProjectTaskId,
            report.Id, f.Owner.Id, new("Corrected", [new("https://example.com/corrected")]), default);
        var versions = await f.Db.ProjectEvidenceModerations.OrderBy(m => m.Version).ToListAsync();
        Assert.Equal(2, versions.Count); Assert.NotNull(versions[0].SupersededAt);
        Assert.Equal(oldJson, versions[0].SnapshotJson); Assert.Equal(EvidenceModerationStatus.Pending, versions[1].Status);
    }

    [Fact]
    public async Task DeletingTaskRetainsItsAuditSnapshotAndQueuesEmptySource()
    {
        await using var f = await Fixture.CreateAsync(); var report = await SeedEvidenceAsync(f); await CompleteAsync(f);
        var oldJson = (await f.Db.ProjectEvidenceModerations.SingleAsync()).SnapshotJson;
        await new ProjectTaskService(f.Db, f.Configuration).DeleteProjectTaskAsync(f.Project.Id, report.ProjectTaskId, f.Owner.Id);
        Assert.Equal(oldJson, (await f.Db.ProjectEvidenceModerations.SingleAsync(m => m.Version == 1)).SnapshotJson);
        Assert.Empty(await f.Db.TaskEvidence.ToListAsync());
        await EvidenceWorker(f, new(), new()).RunAsync(DateTimeOffset.UtcNow, default);
        Assert.Equal(EvidenceModerationStatus.NeedsEvidence, (await f.Db.ProjectEvidenceModerations.SingleAsync(m => m.SupersededAt == null)).Status);
    }

    [Fact]
    public async Task SnapshotColumnsCannotBeRewrittenThroughEf()
    {
        await using var f = await Fixture.CreateAsync(); await SeedEvidenceAsync(f); await CompleteAsync(f);
        var m = await f.Db.ProjectEvidenceModerations.SingleAsync();
        f.Db.Entry(m).Property(x => x.SnapshotJson).CurrentValue = "{}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task MergeKeepsEveryReportGroupIncludingDraftWithdrawnAndSoftDeletedEvidence()
    {
        await using var f = await Fixture.CreateAsync(); var report = await SeedEvidenceAsync(f);
        foreach (var state in new[] { TaskReportStatus.Draft, TaskReportStatus.Withdrawn, TaskReportStatus.Approved })
        {
            var deleted = state == TaskReportStatus.Approved;
            f.Db.TaskReports.Add(new() { ProjectTaskId = report.ProjectTaskId, SubmittedById = report.SubmittedById,
                ReportGroupId = Guid.NewGuid(), Version = 1, Content = state.ToString(), Status = state,
                IsDeleted = deleted, DeletedAt = deleted ? DateTimeOffset.UtcNow : null,
                DeletedById = deleted ? report.SubmittedById : null,
                Evidence = [new() { FileUrl = $"https://example.com/{state}" }] });
        }
        await f.Db.SaveChangesAsync(); await CompleteAsync(f);
        var m = await f.Db.ProjectEvidenceModerations.SingleAsync();
        var dto = EvidenceModerationMapping.ToResponse(m);
        var reports = dto.Source.Tasks.Single().Reports;
        Assert.Equal(4, reports.Count); Assert.Equal(4, reports.Select(r => r.ReportGroupId).Distinct().Count());
        Assert.Contains(reports, r => r.IsDeleted && r.DeletedById == report.SubmittedById && r.DeletedAt != null);
        var content = new ContentChecker(); var urls = new UrlChecker();
        await EvidenceWorker(f, content, urls).RunAsync(DateTimeOffset.UtcNow, default);
        Assert.Equal(4, content.Calls); Assert.Equal(4, urls.Seen.Count);
        Assert.Equal(m.CreatedAt, (await f.Db.ContributionCycles.SingleAsync()).OpenedAt);
        Assert.NotNull((await f.Db.Projects.AsNoTracking().SingleAsync()).CompletedAt);
    }
}
