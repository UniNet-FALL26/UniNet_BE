using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class TaskReportTests
{
    private sealed record Scenario(
        Account LeaderAccount,
        UserProfile LeaderProfile,
        Account AssignedAccount,
        UserProfile AssignedProfile,
        Account OtherAccount,
        UserProfile OtherProfile,
        Project Project);

    private static async Task<(SqliteConnection Connection, UniNetDbContext Db, ProjectTaskService Tasks)> Setup()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>()
            .UseSqlite(connection)
            .Options);
        await db.Database.EnsureCreatedAsync();
        return (connection, db, new ProjectTaskService(db, TestConfiguration()));
    }

    private static IConfiguration TestConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Cloudinary:CloudName"] = "fixture" })
            .Build();

    [Fact]
    public async Task PendingReportCannotBeRevisedUntilLeaderRequestsRevision()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var report = await tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report(), default);

            var error = await Assert.ThrowsAsync<ProjectException>(() => tasks.ReviseTaskReportAsync(
                scenario.Project.Id, task.Id, report.Id, scenario.AssignedAccount.Id, Report("Premature edit."), default));

            Assert.Equal("TASK_REPORT_REVISION_NOT_ALLOWED", error.Code);
        }
    }

    [Fact]
    public async Task OlderRevisionRequestedReportCannotBeRevisedAgain()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var original = await tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report("Version one."), default);
            await tasks.ReviewTaskReportAsync(
                scenario.Project.Id, task.Id, original.Id, scenario.LeaderAccount.Id,
                new ReviewTaskReportRequest(TaskReportReviewDecision.RevisionRequested, "Clarify the result."),
                default);
            var revision = await tasks.ReviseTaskReportAsync(
                scenario.Project.Id, task.Id, original.Id, scenario.AssignedAccount.Id,
                Report("Version two."), default);

            var error = await Assert.ThrowsAsync<ProjectException>(() => tasks.ReviseTaskReportAsync(
                scenario.Project.Id, task.Id, original.Id, scenario.AssignedAccount.Id,
                Report("Invalid stale revision."), default));
            var history = await tasks.GetTaskReportsAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, page: 1, pageSize: 10, default);

            Assert.Equal("TASK_REPORT_REVISION_NOT_ALLOWED", error.Code);
            Assert.Equal(2, revision.Version);
            Assert.Equal(2, history.TotalItems);
            Assert.Single(history.Reports.Single(item => item.Id == original.Id).Reviews);
        }
    }

    [Fact]
    public async Task CompetingReportStatusChangesUseConcurrencyToken()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var report = await tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report(), default);
            var original = await db.TaskReports.SingleAsync(item => item.Id == report.Id);

            await using var competingDb = new UniNetDbContext(
                new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
            var stale = await competingDb.TaskReports.SingleAsync(item => item.Id == report.Id);
            await tasks.ReviewTaskReportAsync(
                scenario.Project.Id, task.Id, report.Id, scenario.LeaderAccount.Id,
                new ReviewTaskReportRequest(TaskReportReviewDecision.Approved), default);

            stale.Status = TaskReportStatus.Draft;
            stale.UpdatedAt = DateTimeOffset.UtcNow.AddMilliseconds(2);
            stale.ConcurrencyToken = Guid.NewGuid();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => competingDb.SaveChangesAsync());

            Assert.Equal(TaskReportStatus.Approved,
                (await db.TaskReports.AsNoTracking().SingleAsync(item => item.Id == report.Id)).Status);
            Assert.Single(await db.TaskReportReviews.AsNoTracking().ToListAsync());
        }
    }

    private static async Task<Scenario> CreateScenarioAsync(UniNetDbContext db)
    {
        var leaderAccount = NewAccount("leader");
        var assignedAccount = NewAccount("assigned");
        var otherAccount = NewAccount("other");
        var leader = NewProfile(leaderAccount, "Project Leader");
        var assigned = NewProfile(assignedAccount, "Assigned Member");
        var other = NewProfile(otherAccount, "Other Student");
        var project = new Project
        {
            Creator = leader,
            CreatorId = leader.Id,
            Title = "Report project",
            ProjectField = "Software",
            Description = "Project used in report tests.",
            MemberTarget = 4,
            RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(30),
            ExpectedOutput = DateTimeOffset.UtcNow.AddDays(60),
            Visibility = ProjectVisibility.Public,
            Status = ProjectStatus.Active,
            RecruitmentStatus = RecruitmentStatus.Open,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.Accounts.AddRange(leaderAccount, assignedAccount, otherAccount);
        db.Projects.Add(project);
        db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = assigned.Id,
            Role = "Backend",
            Status = ProjectMemberStatus.Active,
            JoinedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return new(leaderAccount, leader, assignedAccount, assigned, otherAccount, other, project);
    }

    private static Account NewAccount(string suffix) => new()
    {
        Email = $"{suffix}@example.com",
        Role = AccountRole.Student,
        Status = AccountStatus.Active
    };

    private static UserProfile NewProfile(Account account, string fullName)
    {
        var profile = new UserProfile
        {
            Account = account,
            AccountId = account.Id,
            FullName = fullName,
            Nickname = fullName.Replace(" ", ""),
            IsVerified = true
        };
        account.Profile = profile;
        return profile;
    }

    private static Task<ProjectTaskResponse> CreateAssignedTaskAsync(
        ProjectTaskService tasks, Scenario scenario)
        => tasks.CreateProjectTaskAsync(
            scenario.Project.Id,
            scenario.LeaderAccount.Id,
            new CreateProjectTaskRequest(
                "Prepare release",
                "Complete and report release preparation.",
                DateTimeOffset.UtcNow.AddDays(4),
                AssignedUserIds: [scenario.AssignedProfile.Id]),
            default);

    private static SubmitTaskReportRequest Report(
        string content = "Completed the assigned work.",
        params string[] urls)
        => new(content, urls.Select(url => new TaskEvidenceRequest(url)).ToList());

    private static TaskEvidenceRequest CloudinaryEvidence(
        Guid accountId,
        string resourceType,
        string fileName,
        string format,
        string fileId)
    {
        var publicId = resourceType switch
        {
            "image" => $"uninet/images/{accountId:N}/{fileId}",
            "raw" => $"uninet/evidence/{accountId:N}/{fileId}.{format}",
            "video" => $"uninet/evidence/{accountId:N}/{fileId}",
            _ => throw new ArgumentOutOfRangeException(nameof(resourceType))
        };
        var deliveryPath = resourceType is "image" or "video" ? $"{publicId}.{format}" : publicId;
        return new(
            $"https://res.cloudinary.com/fixture/{resourceType}/upload/v123/{deliveryPath}",
            publicId,
            resourceType,
            fileName,
            format);
    }

    [Fact]
    public async Task ReportEvidenceAndReviewHaveEfCoreEntitiesAndConstraints()
    {
        var (connection, db, _) = await Setup();
        await using (connection)
        await using (db)
        {
            var reports = db.Model.FindEntityType(typeof(TaskReport))
                ?? throw new InvalidOperationException("TaskReport is not part of the EF model.");
            var evidence = db.Model.FindEntityType(typeof(TaskEvidence))
                ?? throw new InvalidOperationException("TaskEvidence is not part of the EF model.");
            var reviews = db.Model.FindEntityType(typeof(TaskReportReview))
                ?? throw new InvalidOperationException("TaskReportReview is not part of the EF model.");
            var concurrencyToken = reports.FindProperty(nameof(TaskReport.ConcurrencyToken))
                ?? throw new InvalidOperationException("TaskReport concurrency token is missing.");
            var updatedAt = reports.FindProperty(nameof(TaskReport.UpdatedAt))
                ?? throw new InvalidOperationException("TaskReport UpdatedAt is missing.");

            Assert.Contains(reports.GetIndexes(), index =>
                index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(["ReportGroupId", "Version"]));
            Assert.Contains(reports.GetForeignKeys(), foreignKey =>
                foreignKey.Properties.Select(property => property.Name).SequenceEqual(["ParentReportId"]) &&
                foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
            Assert.True(concurrencyToken.IsConcurrencyToken);
            Assert.False(updatedAt.IsConcurrencyToken);
            Assert.Contains(evidence.GetForeignKeys(), foreignKey =>
                foreignKey.PrincipalEntityType.ClrType == typeof(TaskReport) &&
                foreignKey.DeleteBehavior == DeleteBehavior.Cascade);
            Assert.Contains(reviews.GetIndexes(), index =>
                index.IsUnique &&
                index.Properties.Select(property => property.Name).SequenceEqual(["TaskReportId"]));
        }
    }

    [Fact]
    public async Task AssignedActiveMemberCanSubmitReportWithMultipleEvidenceUrls()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);

            var report = await tasks.SubmitTaskReportAsync(
                scenario.Project.Id,
                task.Id,
                scenario.AssignedAccount.Id,
                Report("Release preparation completed.", "https://example.com/commit/42", "https://example.com/build/91"),
                default);

            Assert.Equal(1, report.Version);
            Assert.Equal(TaskReportStatus.Submitted, report.Status);
            Assert.Equal(scenario.AssignedProfile.Id, report.SubmittedById);
            Assert.Equal(2, report.Evidence.Count);
            Assert.Empty(report.Reviews);
        }
    }

    [Fact]
    public async Task SubmitReportPersistsUploadResponseMetadataWithCloudinaryEvidence()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var evidence = CloudinaryEvidence(
                scenario.AssignedAccount.Id,
                "raw",
                "login-test.pdf",
                "pdf",
                "11111111111111111111111111111111");
            var videoEvidence = CloudinaryEvidence(
                scenario.AssignedAccount.Id,
                "video",
                "walkthrough.mp4",
                "mp4",
                "22222222222222222222222222222222");

            var report = await tasks.SubmitTaskReportAsync(
                scenario.Project.Id,
                task.Id,
                scenario.AssignedAccount.Id,
                new SubmitTaskReportRequest("Login test evidence attached.", [evidence, videoEvidence]),
                default);
            var savedEvidence = await db.TaskEvidence.OrderBy(item => item.Format).ToListAsync();

            Assert.Equal(2, savedEvidence.Count);
            var saved = savedEvidence.Single(item => item.Format == "pdf");
            Assert.Equal(evidence.FileUrl, saved.FileUrl);
            Assert.Equal(evidence.PublicId, saved.PublicId);
            Assert.Equal(evidence.ResourceType, saved.ResourceType);
            Assert.Equal(evidence.FileName, saved.FileName);
            Assert.Equal(evidence.Format, saved.Format);
            var savedVideo = savedEvidence.Single(item => item.Format == "mp4");
            Assert.Equal(videoEvidence.PublicId, savedVideo.PublicId);
            Assert.Equal(videoEvidence.ResourceType, savedVideo.ResourceType);
            Assert.Equal(videoEvidence.FileUrl, savedVideo.FileUrl);
            Assert.Equal(videoEvidence.FileName, savedVideo.FileName);
            Assert.Equal(videoEvidence.PublicId, report.Evidence.Single(item => item.Format == "mp4").PublicId);
        }
    }

    [Fact]
    public async Task RejectsCloudinaryEvidenceWhosePublicIdBelongsToAnotherAccount()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var evidence = CloudinaryEvidence(
                Guid.NewGuid(),
                "raw",
                "foreign.pdf",
                "pdf",
                "22222222222222222222222222222222");

            var error = await Assert.ThrowsAsync<ProjectException>(() => tasks.SubmitTaskReportAsync(
                scenario.Project.Id,
                task.Id,
                scenario.AssignedAccount.Id,
                new SubmitTaskReportRequest("Attempt to reference another account's file.", [evidence]),
                default));

            Assert.Equal("INVALID_TASK_EVIDENCE_METADATA", error.Code);
        }
    }

    [Fact]
    public async Task RejectsInsecureCloudinaryEvidenceAndPartialUploadMetadata()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var evidence = CloudinaryEvidence(
                scenario.AssignedAccount.Id,
                "raw",
                "insecure.pdf",
                "pdf",
                "33333333333333333333333333333333");
            var insecure = evidence with { FileUrl = evidence.FileUrl.Replace("https:", "http:", StringComparison.Ordinal) };
            var partial = evidence with { PublicId = null };

            var insecureError = await Assert.ThrowsAsync<ProjectException>(() => tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id,
                new SubmitTaskReportRequest("Insecure URL.", [insecure]), default));
            var partialError = await Assert.ThrowsAsync<ProjectException>(() => tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id,
                new SubmitTaskReportRequest("Incomplete metadata.", [partial]), default));

            Assert.Equal("INVALID_TASK_EVIDENCE_URL", insecureError.Code);
            Assert.Equal("INVALID_TASK_EVIDENCE_METADATA", partialError.Code);
        }
    }

    [Fact]
    public async Task MemberNotAssignedToTaskCannotSubmitReport()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest("Unassigned", "No assigned member.", DateTimeOffset.UtcNow.AddDays(2)),
                default);

            var ex = await Assert.ThrowsAsync<ProjectException>(() => tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report(), default));

            Assert.Equal("TASK_REPORT_SUBMISSION_DENIED", ex.Code);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/evidence")]
    [InlineData("http://example.com/insecure")]
    public async Task InvalidEvidenceUrlIsRejected(string url)
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);

            var ex = await Assert.ThrowsAsync<ProjectException>(() => tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report(urls: [url]), default));

            Assert.Equal("INVALID_TASK_EVIDENCE_URL", ex.Code);
        }
    }

    [Fact]
    public async Task ProjectLeaderCanApproveReportWithoutChangingTaskStatus()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var report = await tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report(), default);

            var reviewed = await tasks.ReviewTaskReportAsync(
                scenario.Project.Id,
                task.Id,
                report.Id,
                scenario.LeaderAccount.Id,
                new ReviewTaskReportRequest(TaskReportReviewDecision.Approved),
                default);

            Assert.Equal(TaskReportStatus.Approved, reviewed.Status);
            Assert.Equal(TaskReportReviewDecision.Approved, Assert.Single(reviewed.Reviews).Decision);
            Assert.Equal(ProjectTaskStatus.ToDo, (await db.ProjectTasks.SingleAsync(t => t.Id == task.Id)).Status);
        }
    }

    [Fact]
    public async Task RevisionRequestRequiresFeedbackAndRecordsReview()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var report = await tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report(), default);

            var ex = await Assert.ThrowsAsync<ProjectException>(() => tasks.ReviewTaskReportAsync(
                scenario.Project.Id,
                task.Id,
                report.Id,
                scenario.LeaderAccount.Id,
                new ReviewTaskReportRequest(TaskReportReviewDecision.RevisionRequested, "  "),
                default));
            Assert.Equal("TASK_REPORT_FEEDBACK_REQUIRED", ex.Code);

            var reviewed = await tasks.ReviewTaskReportAsync(
                scenario.Project.Id,
                task.Id,
                report.Id,
                scenario.LeaderAccount.Id,
                new ReviewTaskReportRequest(TaskReportReviewDecision.RevisionRequested, "Please include test evidence."),
                default);

            Assert.Equal(TaskReportStatus.RevisionRequested, reviewed.Status);
            Assert.Equal("Please include test evidence.", Assert.Single(reviewed.Reviews).Feedback);
        }
    }

    [Fact]
    public async Task RequestedRevisionCreatesNewVersionAndPreservesPriorReviewHistory()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var original = await tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report("First version."), default);
            await tasks.ReviewTaskReportAsync(
                scenario.Project.Id, task.Id, original.Id, scenario.LeaderAccount.Id,
                new ReviewTaskReportRequest(TaskReportReviewDecision.RevisionRequested, "Add evidence."),
                default);

            var revised = await tasks.ReviseTaskReportAsync(
                scenario.Project.Id,
                task.Id,
                original.Id,
                scenario.AssignedAccount.Id,
                Report("Second version.", "https://example.com/evidence"),
                default);
            var history = await tasks.GetTaskReportsAsync(
                scenario.Project.Id, task.Id, scenario.LeaderAccount.Id, page: 1, pageSize: 10, default);

            Assert.Equal(2, revised.Version);
            Assert.Equal(TaskReportStatus.Submitted, revised.Status);
            Assert.Equal(2, history.TotalItems);
            var previous = Assert.Single(history.Reports, item => item.Id == original.Id);
            Assert.Equal(TaskReportStatus.RevisionRequested, previous.Status);
            Assert.Equal("Add evidence.", Assert.Single(previous.Reviews).Feedback);
        }
    }

    [Fact]
    public async Task MemberCanWithdrawUnreviewedReportBackToDraftAndResubmitSameRecord()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var original = await tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report("Submitted by mistake."), default);

            var withdrawn = await tasks.WithdrawTaskReportAsync(
                scenario.Project.Id, task.Id, original.Id, scenario.AssignedAccount.Id, default);
            var reviewError = await Assert.ThrowsAsync<ProjectException>(() => tasks.ReviewTaskReportAsync(
                scenario.Project.Id,
                task.Id,
                original.Id,
                scenario.LeaderAccount.Id,
                new ReviewTaskReportRequest(TaskReportReviewDecision.Approved),
                default));
            var edited = await tasks.UpdateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, original.Id, scenario.AssignedAccount.Id,
                Report("Corrected report."), default);
            var replacement = await tasks.SubmitTaskReportDraftAsync(
                scenario.Project.Id, task.Id, original.Id, scenario.AssignedAccount.Id, default);
            var history = await tasks.GetTaskReportsAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, page: 1, pageSize: 10, default);

            Assert.Equal(original.Id, withdrawn.Id);
            Assert.Equal(TaskReportStatus.Draft, withdrawn.Status);
            Assert.Equal("Corrected report.", edited.Content);
            Assert.Equal("TASK_REPORT_NOT_REVIEWABLE", reviewError.Code);
            Assert.Equal(TaskReportStatus.Submitted, replacement.Status);
            Assert.Equal(original.Id, replacement.Id);
            Assert.Equal(original.Version, replacement.Version);
            Assert.Equal(1, history.TotalItems);
            Assert.Equal(TaskReportStatus.Submitted, history.Reports.Single(report => report.Id == original.Id).Status);
        }
    }

    [Fact]
    public async Task MemberCanCreateEditAndSubmitDraft()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var draft = await tasks.CreateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report("Draft"), default);

            var edited = await tasks.UpdateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, draft.Id, scenario.AssignedAccount.Id, Report("Final draft"), default);
            var memberReports = await tasks.GetTaskReportsAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, page: 1, pageSize: 10, default);
            var leaderReports = await tasks.GetTaskReportsAsync(
                scenario.Project.Id, task.Id, scenario.LeaderAccount.Id, page: 1, pageSize: 10, default);
            var hiddenDraft = await Assert.ThrowsAsync<ProjectException>(() => tasks.GetTaskReportAsync(
                scenario.Project.Id, task.Id, draft.Id, scenario.LeaderAccount.Id, default));
            var submitted = await tasks.SubmitTaskReportDraftAsync(
                scenario.Project.Id, task.Id, draft.Id, scenario.AssignedAccount.Id, default);

            Assert.Equal(TaskReportStatus.Draft, draft.Status);
            Assert.Equal("Final draft", edited.Content);
            Assert.Equal(TaskReportStatus.Submitted, submitted.Status);
            Assert.Equal("Final draft", submitted.Content);
            Assert.Single(memberReports.Reports);
            Assert.Empty(leaderReports.Reports);
            Assert.Equal("TASK_REPORT_NOT_FOUND", hiddenDraft.Code);

            var updateError = await Assert.ThrowsAsync<ProjectException>(() =>
                tasks.UpdateTaskReportDraftAsync(
                    scenario.Project.Id, task.Id, draft.Id, scenario.AssignedAccount.Id, Report(), default));
            Assert.Equal("TASK_REPORT_DRAFT_NOT_EDITABLE", updateError.Code);
        }
    }

    [Fact]
    public async Task DraftCreatedAfterWithdrawalCanBeEditedImmediately()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var submitted = await tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id,
                Report("Submitted then withdrawn", "https://example.com/withdrawn-evidence"), default);
            await tasks.WithdrawTaskReportAsync(
                scenario.Project.Id, task.Id, submitted.Id, scenario.AssignedAccount.Id, default);

            var draft = await tasks.CreateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id,
                Report("New draft", "https://example.com/old-draft-evidence"), default);
            var imageEvidence = CloudinaryEvidence(
                scenario.AssignedAccount.Id, "image", "screenshot.png", "png",
                "66666666666666666666666666666666");
            var rawEvidence = CloudinaryEvidence(
                scenario.AssignedAccount.Id, "raw", "results.docx", "docx",
                "77777777777777777777777777777777");
            var videoEvidence = CloudinaryEvidence(
                scenario.AssignedAccount.Id, "video", "walkthrough.mp4", "mp4",
                "88888888888888888888888888888888");
            var edited = await tasks.UpdateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, draft.Id, scenario.AssignedAccount.Id,
                new SubmitTaskReportRequest("Edited draft", [imageEvidence, rawEvidence, videoEvidence]), default);
            var evidence = await db.TaskEvidence.AsNoTracking()
                .Where(item => item.TaskReportId == submitted.Id || item.TaskReportId == draft.Id)
                .OrderBy(item => item.TaskReportId)
                .ToListAsync();

            Assert.Equal("Edited draft", edited.Content);
            Assert.Equal(1, edited.Version);
            Assert.Equal(TaskReportStatus.Draft, edited.Status);
            Assert.Equal(4, evidence.Count);
            Assert.Contains(evidence, item =>
                item.TaskReportId == submitted.Id && item.FileUrl == "https://example.com/withdrawn-evidence");
            Assert.Contains(evidence, item =>
                item.TaskReportId == draft.Id && item.ResourceType == "image" &&
                item.PublicId == imageEvidence.PublicId);
            Assert.Contains(evidence, item =>
                item.TaskReportId == draft.Id && item.ResourceType == "raw" &&
                item.PublicId == rawEvidence.PublicId);
            Assert.Contains(evidence, item =>
                item.TaskReportId == draft.Id && item.ResourceType == "video" &&
                item.PublicId == videoEvidence.PublicId);
        }
    }

    [Fact]
    public async Task ReadDraftCanBeUpdatedWhenNoOtherRequestHasChangedIt()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var draft = await tasks.CreateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report("Initial"), default);

            await using var requestDb = new UniNetDbContext(
                new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
            var requestTasks = new ProjectTaskService(requestDb, TestConfiguration());
            var read = await requestTasks.GetTaskReportAsync(
                scenario.Project.Id, task.Id, draft.Id, scenario.AssignedAccount.Id, default);
            var edited = await requestTasks.UpdateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, draft.Id, scenario.AssignedAccount.Id, Report("Updated"), default);

            Assert.Equal("Initial", read.Content);
            Assert.Equal("Updated", edited.Content);
            Assert.Equal(draft.Version, edited.Version);
        }
    }

    [Fact]
    public async Task OlderDraftCanBeEditedEvenWhenANewerDraftExists()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var older = new TaskReport
            {
                ProjectTaskId = task.Id,
                SubmittedById = scenario.AssignedProfile.Id,
                Version = 1,
                Content = "Older draft",
                Status = TaskReportStatus.Draft,
                ReportGroupId = Guid.NewGuid()
            };
            var newer = new TaskReport
            {
                ProjectTaskId = task.Id,
                SubmittedById = scenario.AssignedProfile.Id,
                Version = 1,
                Content = "Newer draft",
                Status = TaskReportStatus.Draft,
                ReportGroupId = Guid.NewGuid()
            };
            db.TaskReports.AddRange(older, newer);
            await db.SaveChangesAsync();

            var edited = await tasks.UpdateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, older.Id, scenario.AssignedAccount.Id, Report("Older draft edited"), default);

            Assert.Equal("Older draft edited", edited.Content);
            Assert.Equal(1, edited.Version);
            Assert.Equal("Newer draft",
                (await db.TaskReports.AsNoTracking().SingleAsync(report => report.Id == newer.Id)).Content);
        }
    }

    [Fact]
    public async Task OlderDraftCanBeSubmittedWhenANewerDraftExists()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var older = await tasks.CreateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report("Older"), default);
            var newer = await tasks.CreateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report("Newer"), default);

            var submitted = await tasks.SubmitTaskReportDraftAsync(
                scenario.Project.Id, task.Id, older.Id, scenario.AssignedAccount.Id, default);

            Assert.Equal(older.Id, submitted.Id);
            Assert.Equal(1, submitted.Version);
            Assert.Equal(TaskReportStatus.Submitted, submitted.Status);
            Assert.Equal(TaskReportStatus.Draft,
                (await db.TaskReports.AsNoTracking().SingleAsync(report => report.Id == newer.Id)).Status);
        }
    }

    [Fact]
    public async Task EligibleDraftCanBeSoftDeletedWithoutDeletingItsEvidence()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var draft = await tasks.CreateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id,
                Report("Keep history", "https://example.com/evidence"), default);

            await tasks.DeleteTaskReportDraftAsync(
                scenario.Project.Id, task.Id, draft.Id, scenario.AssignedAccount.Id, default);

            var deleted = await db.TaskReports.AsNoTracking().SingleAsync(report => report.Id == draft.Id);
            var evidence = await db.TaskEvidence.AsNoTracking()
                .SingleAsync(item => item.TaskReportId == draft.Id);
            var reports = await tasks.GetTaskReportsAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, page: 1, pageSize: 10, default);
            var editError = await Assert.ThrowsAsync<ProjectException>(() => tasks.UpdateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, draft.Id, scenario.AssignedAccount.Id, Report("No edit"), default));
            var submitError = await Assert.ThrowsAsync<ProjectException>(() => tasks.SubmitTaskReportDraftAsync(
                scenario.Project.Id, task.Id, draft.Id, scenario.AssignedAccount.Id, default));

            Assert.True(deleted.IsDeleted);
            Assert.Equal(scenario.AssignedProfile.Id, deleted.DeletedById);
            Assert.Equal(draft.Id, evidence.TaskReportId);
            Assert.Empty(reports.Reports);
            Assert.Equal("TASK_REPORT_NOT_FOUND", editError.Code);
            Assert.Equal("TASK_REPORT_NOT_FOUND", submitError.Code);
        }
    }

    [Fact]
    public async Task RevisionUsesItsSourceGroupAndLeavesSourceUnchangedWhenNewDraftExists()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var source = await tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id,
                Report("Needs revision", "https://example.com/source"), default);
            await tasks.ReviewTaskReportAsync(
                scenario.Project.Id, task.Id, source.Id, scenario.LeaderAccount.Id,
                new ReviewTaskReportRequest(TaskReportReviewDecision.RevisionRequested, "Revise this."), default);
            var unchangedBefore = await db.TaskReports.AsNoTracking()
                .SingleAsync(report => report.Id == source.Id);
            var unrelatedDraft = await tasks.CreateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report("Unrelated draft"), default);

            var revision = await tasks.ReviseTaskReportAsync(
                scenario.Project.Id, task.Id, source.Id, scenario.AssignedAccount.Id,
                Report("Revised"), default);
            var unchangedAfter = await db.TaskReports.AsNoTracking()
                .SingleAsync(report => report.Id == source.Id);
            var sourceEvidence = await db.TaskEvidence.AsNoTracking()
                .SingleAsync(item => item.TaskReportId == source.Id);
            var history = await tasks.GetTaskReportsAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, page: 1, pageSize: 10, default);

            Assert.Equal(source.ReportGroupId, revision.ReportGroupId);
            Assert.Equal(source.Id, revision.ParentReportId);
            Assert.Equal(source.Version + 1, revision.Version);
            Assert.Equal(TaskReportStatus.Submitted, revision.Status);
            Assert.Equal(TaskReportStatus.Draft, unrelatedDraft.Status);
            Assert.Equal(unchangedBefore.UpdatedAt, unchangedAfter.UpdatedAt);
            Assert.Equal(unchangedBefore.ConcurrencyToken, unchangedAfter.ConcurrencyToken);
            Assert.Equal(source.Id, sourceEvidence.TaskReportId);
            Assert.Equal(TaskReportStatus.RevisionRequested,
                history.Reports.Single(report => report.Id == source.Id).Status);
            Assert.Single(history.Reports.Single(report => report.Id == source.Id).Reviews);
        }
    }

    [Fact]
    public async Task RevisionCanTargetRequestedOlderChainWhenAnotherChainHasNewerVersion()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var sourceDraft = await tasks.CreateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report("First chain"), default);
            var unrelatedDraft = await tasks.CreateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report("Second chain"), default);
            var source = await tasks.SubmitTaskReportDraftAsync(
                scenario.Project.Id, task.Id, sourceDraft.Id, scenario.AssignedAccount.Id, default);
            await tasks.ReviewTaskReportAsync(
                scenario.Project.Id, task.Id, source.Id, scenario.LeaderAccount.Id,
                new ReviewTaskReportRequest(TaskReportReviewDecision.RevisionRequested, "Revise first chain."), default);
            var unrelatedSubmitted = await tasks.SubmitTaskReportDraftAsync(
                scenario.Project.Id, task.Id, unrelatedDraft.Id, scenario.AssignedAccount.Id, default);
            await tasks.ReviewTaskReportAsync(
                scenario.Project.Id, task.Id, unrelatedSubmitted.Id, scenario.LeaderAccount.Id,
                new ReviewTaskReportRequest(TaskReportReviewDecision.RevisionRequested, "Revise second chain."), default);
            var unrelatedRevision = await tasks.ReviseTaskReportAsync(
                scenario.Project.Id, task.Id, unrelatedSubmitted.Id, scenario.AssignedAccount.Id,
                Report("Second chain revision"), default);

            var revision = await tasks.ReviseTaskReportAsync(
                scenario.Project.Id, task.Id, source.Id, scenario.AssignedAccount.Id,
                Report("First chain revision"), default);

            Assert.Equal(source.ReportGroupId, revision.ReportGroupId);
            Assert.NotEqual(unrelatedRevision.ReportGroupId, revision.ReportGroupId);
            Assert.Equal(source.Id, revision.ParentReportId);
            Assert.Equal(2, revision.Version);
        }
    }

    [Fact]
    public async Task RepeatingRevisionAfterSuccessfulRevisionDoesNotCreateAnotherVersion()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var source = await tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report("Source"), default);
            await tasks.ReviewTaskReportAsync(
                scenario.Project.Id, task.Id, source.Id, scenario.LeaderAccount.Id,
                new ReviewTaskReportRequest(TaskReportReviewDecision.RevisionRequested, "Revise."), default);

            var revision = await tasks.ReviseTaskReportAsync(
                scenario.Project.Id, task.Id, source.Id, scenario.AssignedAccount.Id, Report("Revision"), default);
            var retryError = await Assert.ThrowsAsync<ProjectException>(() => tasks.ReviseTaskReportAsync(
                scenario.Project.Id, task.Id, source.Id, scenario.AssignedAccount.Id, Report("Duplicate"), default));

            Assert.Equal("TASK_REPORT_REVISION_NOT_ALLOWED", retryError.Code);
            Assert.Equal(2, await db.TaskReports.CountAsync(report => report.ReportGroupId == source.ReportGroupId));
            Assert.Equal(source.Id, revision.ParentReportId);
        }
    }

    [Fact]
    public async Task DraftUpdateSucceedsWhenStoredTimestampHasDatabasePrecision()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var draft = await tasks.CreateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id,
                Report("Initial", "https://example.com/original-evidence"), default);
            var tracked = await db.TaskReports.SingleAsync(report => report.Id == draft.Id);
            var ticks = DateTimeOffset.UtcNow.Ticks;
            var timestampWithSubMicrosecondTicks = new DateTimeOffset(
                ticks - ticks % TimeSpan.TicksPerMillisecond + 1234,
                TimeSpan.Zero);
            tracked.UpdatedAt = timestampWithSubMicrosecondTicks;
            await db.SaveChangesAsync();

            var databasePrecisionTimestamp = new DateTimeOffset(
                timestampWithSubMicrosecondTicks.Ticks -
                timestampWithSubMicrosecondTicks.Ticks % 10,
                TimeSpan.Zero);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"TaskReports\" SET \"UpdatedAt\" = {databasePrecisionTimestamp} WHERE \"Id\" = {draft.Id}");

            var edited = await tasks.UpdateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, draft.Id, scenario.AssignedAccount.Id, Report("Updated"), default);

            Assert.Equal("Updated", edited.Content);
        }
    }

    [Fact]
    public async Task ConcurrentDraftEditsRejectTheStaleRequest()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var draft = await tasks.CreateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report("Initial"), default);

            await using var firstDb = new UniNetDbContext(
                new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
            await using var secondDb = new UniNetDbContext(
                new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
            var firstTasks = new ProjectTaskService(firstDb, TestConfiguration());
            var secondTasks = new ProjectTaskService(secondDb, TestConfiguration());
            await secondTasks.GetTaskReportAsync(
                scenario.Project.Id, task.Id, draft.Id, scenario.AssignedAccount.Id, default);

            var firstEdit = await firstTasks.UpdateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, draft.Id, scenario.AssignedAccount.Id,
                Report("First edit", "https://example.com/first-evidence"), default);
            var staleError = await Assert.ThrowsAsync<ProjectException>(() => secondTasks.UpdateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, draft.Id, scenario.AssignedAccount.Id,
                Report("Stale edit", "https://example.com/stale-evidence"), default));

            Assert.Equal("TASK_REPORT_CONCURRENT_MODIFICATION", staleError.Code);
            Assert.Equal("First edit",
                (await firstDb.TaskReports.AsNoTracking().SingleAsync(report => report.Id == draft.Id)).Content);
            Assert.Equal("First edit", firstEdit.Content);
        }
    }

    [Fact]
    public async Task DraftUpdateRejectsReportFromDifferentTask()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var otherTask = await tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest("Other task", "Separate task.", DateTimeOffset.UtcNow.AddDays(2)),
                default);
            await tasks.AssignUsersAsync(
                scenario.Project.Id, otherTask.Id, scenario.LeaderAccount.Id, [scenario.AssignedProfile.Id], default);
            var draft = await tasks.CreateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report(), default);

            var error = await Assert.ThrowsAsync<ProjectException>(() => tasks.UpdateTaskReportDraftAsync(
                scenario.Project.Id, otherTask.Id, draft.Id, scenario.AssignedAccount.Id, Report("Wrong task"), default));

            Assert.Equal("TASK_REPORT_NOT_FOUND", error.Code);
        }
    }

    [Fact]
    public async Task DraftUpdateRejectsAnotherAssignedMembersReport()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            db.ProjectMembers.Add(new ProjectMember
            {
                ProjectId = scenario.Project.Id,
                UserId = scenario.OtherProfile.Id,
                Role = "QA",
                Status = ProjectMemberStatus.Active,
                JoinedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            await tasks.AssignUsersAsync(
                scenario.Project.Id,
                task.Id,
                scenario.LeaderAccount.Id,
                [scenario.AssignedProfile.Id, scenario.OtherProfile.Id],
                default);
            var draft = await tasks.CreateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report(), default);

            var error = await Assert.ThrowsAsync<ProjectException>(() => tasks.UpdateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, draft.Id, scenario.OtherAccount.Id, Report("Unauthorized"), default));

            Assert.Equal("TASK_REPORT_ACCESS_DENIED", error.Code);
        }
    }

    [Theory]
    [InlineData(TaskReportStatus.Submitted)]
    [InlineData(TaskReportStatus.Approved)]
    [InlineData(TaskReportStatus.RevisionRequested)]
    [InlineData(TaskReportStatus.Withdrawn)]
    public async Task DraftUpdateRejectsReportsInNonDraftStates(TaskReportStatus status)
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var report = await tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report(), default);

            if (status == TaskReportStatus.Approved || status == TaskReportStatus.RevisionRequested)
            {
                await tasks.ReviewTaskReportAsync(
                    scenario.Project.Id,
                    task.Id,
                    report.Id,
                    scenario.LeaderAccount.Id,
                    new ReviewTaskReportRequest(
                        status == TaskReportStatus.Approved
                            ? TaskReportReviewDecision.Approved
                            : TaskReportReviewDecision.RevisionRequested,
                        status == TaskReportStatus.RevisionRequested ? "Please revise." : null),
                    default);
            }
            else if (status == TaskReportStatus.Withdrawn)
            {
                var withdrawnReport = await db.TaskReports.SingleAsync(item => item.Id == report.Id);
                withdrawnReport.Status = TaskReportStatus.Withdrawn;
                await db.SaveChangesAsync();
            }

            var error = await Assert.ThrowsAsync<ProjectException>(() => tasks.UpdateTaskReportDraftAsync(
                scenario.Project.Id, task.Id, report.Id, scenario.AssignedAccount.Id, Report("Invalid edit"), default));

            Assert.Equal("TASK_REPORT_DRAFT_NOT_EDITABLE", error.Code);
        }
    }

    [Fact]
    public async Task MemberCannotWithdrawAnotherAssignedMembersReport()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            db.ProjectMembers.Add(new ProjectMember
            {
                ProjectId = scenario.Project.Id,
                UserId = scenario.OtherProfile.Id,
                Role = "QA",
                Status = ProjectMemberStatus.Active,
                JoinedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            await tasks.AssignUsersAsync(
                scenario.Project.Id,
                task.Id,
                scenario.LeaderAccount.Id,
                [scenario.AssignedProfile.Id, scenario.OtherProfile.Id],
                default);
            var report = await tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report(), default);

            var error = await Assert.ThrowsAsync<ProjectException>(() => tasks.WithdrawTaskReportAsync(
                scenario.Project.Id, task.Id, report.Id, scenario.OtherAccount.Id, default));

            Assert.Equal("TASK_REPORT_ACCESS_DENIED", error.Code);
        }
    }

    [Fact]
    public async Task MemberCannotWithdrawReportAfterLeaderReview()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var report = await tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report(), default);
            await tasks.ReviewTaskReportAsync(
                scenario.Project.Id, task.Id, report.Id, scenario.LeaderAccount.Id,
                new ReviewTaskReportRequest(TaskReportReviewDecision.RevisionRequested, "Add evidence."),
                default);

            var error = await Assert.ThrowsAsync<ProjectException>(() => tasks.WithdrawTaskReportAsync(
                scenario.Project.Id, task.Id, report.Id, scenario.AssignedAccount.Id, default));

            Assert.Equal("TASK_REPORT_WITHDRAW_NOT_ALLOWED", error.Code);
        }
    }

    [Fact]
    public async Task RevisedReportKeepsEachVersionEvidenceMetadataAndPreviousReview()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var originalEvidence = CloudinaryEvidence(
                scenario.AssignedAccount.Id, "raw", "first.pdf", "pdf", "44444444444444444444444444444444");
            var original = await tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id,
                new SubmitTaskReportRequest("First version.", [originalEvidence]), default);
            await tasks.ReviewTaskReportAsync(
                scenario.Project.Id, task.Id, original.Id, scenario.LeaderAccount.Id,
                new ReviewTaskReportRequest(TaskReportReviewDecision.RevisionRequested, "Add the missing evidence."),
                default);

            var revisedEvidence = CloudinaryEvidence(
                scenario.AssignedAccount.Id, "raw", "second.pdf", "pdf", "55555555555555555555555555555555");
            var revised = await tasks.ReviseTaskReportAsync(
                scenario.Project.Id, task.Id, original.Id, scenario.AssignedAccount.Id,
                new SubmitTaskReportRequest("Second version.", [revisedEvidence]), default);
            var history = await tasks.GetTaskReportsAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, page: 1, pageSize: 10, default);

            var oldVersion = Assert.Single(history.Reports, item => item.Id == original.Id);
            Assert.Equal(originalEvidence.PublicId, Assert.Single(oldVersion.Evidence).PublicId);
            Assert.Equal("Add the missing evidence.", Assert.Single(oldVersion.Reviews).Feedback);
            Assert.Equal(revisedEvidence.PublicId, Assert.Single(revised.Evidence).PublicId);
        }
    }

    [Fact]
    public async Task NonLeaderCannotReviewReport()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var report = await tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report(), default);

            var ex = await Assert.ThrowsAsync<ProjectException>(() => tasks.ReviewTaskReportAsync(
                scenario.Project.Id, task.Id, report.Id, scenario.AssignedAccount.Id,
                new ReviewTaskReportRequest(TaskReportReviewDecision.Approved), default));

            Assert.Equal("FORBIDDEN", ex.Code);
        }
    }

    [Fact]
    public async Task NonParticipantCannotReadReports()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            await tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report(), default);

            var ex = await Assert.ThrowsAsync<ProjectException>(() => tasks.GetTaskReportsAsync(
                scenario.Project.Id, task.Id, scenario.OtherAccount.Id, default));

            Assert.Equal("PROJECT_ACCESS_DENIED", ex.Code);
        }
    }

    [Fact]
    public async Task ReportFromDifferentTaskOrProjectIsNotFound()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var report = await tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report(), default);
            var otherTask = await tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest("Other task", "Separate task.", DateTimeOffset.UtcNow.AddDays(2)),
                default);

            var taskMismatch = await Assert.ThrowsAsync<ProjectException>(() => tasks.GetTaskReportAsync(
                scenario.Project.Id, otherTask.Id, report.Id, scenario.LeaderAccount.Id, default));
            Assert.Equal("TASK_REPORT_NOT_FOUND", taskMismatch.Code);

            var projectMismatch = await Assert.ThrowsAsync<ProjectException>(() => tasks.GetTaskReportAsync(
                Guid.NewGuid(), task.Id, report.Id, scenario.LeaderAccount.Id, default));
            Assert.Equal("PROJECT_NOT_FOUND", projectMismatch.Code);
        }
    }

    [Fact]
    public async Task ApprovedReportCannotBeRevisedOrReviewedAgain()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var report = await tasks.SubmitTaskReportAsync(
                scenario.Project.Id, task.Id, scenario.AssignedAccount.Id, Report(), default);
            await tasks.ReviewTaskReportAsync(
                scenario.Project.Id, task.Id, report.Id, scenario.LeaderAccount.Id,
                new ReviewTaskReportRequest(TaskReportReviewDecision.Approved), default);

            var revise = await Assert.ThrowsAsync<ProjectException>(() => tasks.ReviseTaskReportAsync(
                scenario.Project.Id, task.Id, report.Id, scenario.AssignedAccount.Id, Report("Try changing accepted report."), default));
            Assert.Equal("TASK_REPORT_REVISION_NOT_ALLOWED", revise.Code);

            var reviewAgain = await Assert.ThrowsAsync<ProjectException>(() => tasks.ReviewTaskReportAsync(
                scenario.Project.Id, task.Id, report.Id, scenario.LeaderAccount.Id,
                new ReviewTaskReportRequest(TaskReportReviewDecision.Approved), default));
            Assert.Equal("TASK_REPORT_NOT_REVIEWABLE", reviewAgain.Code);
        }
    }

    [Fact]
    public async Task AcceptsEvidenceFileNameWithUppercaseExtensionFromUploadResponse()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateScenarioAsync(db);
            var task = await CreateAssignedTaskAsync(tasks, scenario);
            var evidence = CloudinaryEvidence(
                scenario.AssignedAccount.Id, "raw", "results.PDF", "pdf",
                "55555555555555555555555555555555");

            var report = await tasks.SubmitTaskReportAsync(
                scenario.Project.Id,
                task.Id,
                scenario.AssignedAccount.Id,
                new SubmitTaskReportRequest("Uploaded report.", [evidence]),
                default);

            Assert.Equal("results.PDF", Assert.Single(report.Evidence).FileName);
        }
    }
}
