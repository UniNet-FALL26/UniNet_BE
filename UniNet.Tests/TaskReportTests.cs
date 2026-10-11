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
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Cloudinary:CloudName"] = "fixture" })
            .Build();
        return (connection, db, new ProjectTaskService(db, configuration));
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
        var publicId = resourceType == "image"
            ? $"uninet/images/{accountId:N}/{fileId}"
            : $"uninet/evidence/{accountId:N}/{fileId}.{format}";
        var deliveryPath = resourceType == "image" ? $"{publicId}.{format}" : publicId;
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

            Assert.Contains(reports.GetIndexes(), index =>
                index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(["ProjectTaskId", "SubmittedById", "Version"]));
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

            var report = await tasks.SubmitTaskReportAsync(
                scenario.Project.Id,
                task.Id,
                scenario.AssignedAccount.Id,
                new SubmitTaskReportRequest("Login test evidence attached.", [evidence]),
                default);
            var saved = Assert.Single(await db.TaskEvidence.ToListAsync());

            Assert.Equal(evidence.FileUrl, saved.FileUrl);
            Assert.Equal(evidence.PublicId, saved.PublicId);
            Assert.Equal(evidence.ResourceType, saved.ResourceType);
            Assert.Equal(evidence.FileName, saved.FileName);
            Assert.Equal(evidence.Format, saved.Format);
            Assert.Equal(evidence.PublicId, Assert.Single(report.Evidence).PublicId);
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
