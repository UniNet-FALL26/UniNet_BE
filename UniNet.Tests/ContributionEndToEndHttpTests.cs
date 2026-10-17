using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using UniNet.API.Controllers;
using UniNet.Application;
using UniNet.Application.DTOs.Projects;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class ContributionEndToEndHttpTests
{
    [Fact]
    public async Task ContributionLifecycleRunsThroughHttpWithExpectedScoresAndHistory()
    {
        await using var api = await TestApi.StartAsync();
        var projectId = api.ProjectId;
        var participantProfileIds = api.ProfileIds.Take(4).ToArray();
        using var leader = api.Client(api.AccountIds[0]);
        using var memberA = api.Client(api.AccountIds[1]);
        using var memberB = api.Client(api.AccountIds[2]);
        using var memberC = api.Client(api.AccountIds[3]);
        using var outsider = api.Client(api.AccountIds[4]);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.GetAsync($"/api/projects/{projectId}/tasks")).StatusCode);

        var tasks = new[]
        {
            await CreateTaskAsync(leader, projectId, "L completed", ProjectTaskStatus.Completed, api.ProfileIds[0]),
            await CreateTaskAsync(leader, projectId, "L in progress", ProjectTaskStatus.InProgress, api.ProfileIds[0]),
            await CreateTaskAsync(leader, projectId, "A revision deliverable", ProjectTaskStatus.Completed, api.ProfileIds[1]),
            await CreateTaskAsync(leader, projectId, "A second deliverable", ProjectTaskStatus.Completed, api.ProfileIds[1]),
            await CreateTaskAsync(leader, projectId, "B completed", ProjectTaskStatus.Completed, api.ProfileIds[2]),
            await CreateTaskAsync(leader, projectId, "B overdue", ProjectTaskStatus.InProgress, api.ProfileIds[2],
                DateTimeOffset.UtcNow.AddDays(-1)),
            await CreateTaskAsync(leader, projectId, "C cancelled 1", ProjectTaskStatus.Cancelled, api.ProfileIds[3]),
            await CreateTaskAsync(leader, projectId, "C cancelled 2", ProjectTaskStatus.Cancelled, api.ProfileIds[3])
        };
        Assert.Equal(ProjectTaskStatus.Overdue, tasks[5].Status);

        var draft = await CreateDraftAsync(leader, projectId, tasks[0].Id,
            "Initial leader draft", "https://files.example.test/image.png");
        var updatedDraft = await UpdateDraftAsync(leader, projectId, tasks[0].Id, draft.Id,
            "Leader report ready", "https://files.example.test/video.mp4");
        Assert.Equal(TaskReportStatus.Draft, updatedDraft.Status);
        Assert.Equal("Leader report ready", updatedDraft.Content);
        Assert.Single(updatedDraft.Evidence);
        Assert.Equal("https://files.example.test/video.mp4", updatedDraft.Evidence[0].FileUrl);
        var submittedLeaderReport = await ReadAsync<TaskReportResponse>(await leader.PostAsync(
            $"/api/projects/{projectId}/tasks/{tasks[0].Id}/reports/{draft.Id}/submit", null), HttpStatusCode.OK);
        await ReviewReportAsync(leader, projectId, tasks[0].Id, submittedLeaderReport.Id,
            TaskReportReviewDecision.Approved);

        var firstAReport = await SubmitReportAsync(memberA, projectId, tasks[2].Id,
            "A version one", "https://files.example.test/report.docx");
        var requestedRevision = await ReviewReportAsync(leader, projectId, tasks[2].Id, firstAReport.Id,
            TaskReportReviewDecision.RevisionRequested, "Please clarify the result.");
        Assert.Equal(TaskReportStatus.RevisionRequested, requestedRevision.Status);
        var secondAReport = await ReviseReportAsync(memberA, projectId, tasks[2].Id, firstAReport.Id,
            "A version two", "https://files.example.test/revised.docx");
        Assert.Equal(firstAReport.ReportGroupId, secondAReport.ReportGroupId);
        Assert.Equal(firstAReport.Id, secondAReport.ParentReportId);
        Assert.Equal(2, secondAReport.Version);
        await ReviewReportAsync(leader, projectId, tasks[2].Id, secondAReport.Id, TaskReportReviewDecision.Approved);

        var secondAGroup = await SubmitReportAsync(memberA, projectId, tasks[3].Id,
            "A second approved deliverable", "https://files.example.test/diagram.png");
        await ReviewReportAsync(leader, projectId, tasks[3].Id, secondAGroup.Id, TaskReportReviewDecision.Approved);
        var bReport = await SubmitReportAsync(memberB, projectId, tasks[4].Id,
            "B approved deliverable", "https://files.example.test/demo.mp4");
        await ReviewReportAsync(leader, projectId, tasks[4].Id, bReport.Id, TaskReportReviewDecision.Approved);

        var completion = await ReadAsync<ProjectStatusResponse>(await PatchAsJsonAsync(
            leader, "/api/projects/status", new UpdateProjectStatusRequest(projectId, ProjectStatus.Completed)),
            HttpStatusCode.OK);
        Assert.Equal(ProjectStatus.Completed, completion.Status);

        var cycle = await ReadAsync<ContributionCycleResponse>(
            await leader.GetAsync($"/api/projects/{projectId}/contribution"), HttpStatusCode.OK);
        Assert.Equal(4, cycle.Participants.Count);
        Assert.Contains(cycle.Participants, participant =>
            participant.UserId == participantProfileIds[0] && participant.IsLeader && participant.RoleAtSnapshot == "Creator");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.GetAsync($"/api/projects/{projectId}/contribution")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await memberA.PostAsync($"/api/projects/{projectId}/contribution/calculations", null)).StatusCode);

        var reportHistory = await ReadAsync<TaskReportListResponse>(await memberA.GetAsync(
            $"/api/projects/{projectId}/tasks/{tasks[2].Id}/reports"), HttpStatusCode.OK);
        var versionOne = Assert.Single(reportHistory.Reports, report => report.Id == firstAReport.Id);
        var versionTwo = Assert.Single(reportHistory.Reports, report => report.Id == secondAReport.Id);
        Assert.Equal(TaskReportStatus.RevisionRequested, versionOne.Status);
        Assert.Equal(TaskReportStatus.Approved, versionTwo.Status);
        Assert.Single(versionOne.Reviews);
        Assert.Single(versionTwo.Evidence);

        var missingEvaluations = await leader.PostAsync(
            $"/api/projects/{projectId}/contribution/calculations", null);
        Assert.Equal(HttpStatusCode.Conflict, missingEvaluations.StatusCode);
        Assert.Equal("CONTRIBUTION_PEER_EVALUATIONS_INCOMPLETE", await ErrorCodeAsync(missingEvaluations));

        var ratings = new[,]
        {
            { 0, 4, 3, 2 },
            { 5, 0, 4, 3 },
            { 4, 4, 0, 4 },
            { 3, 4, 5, 0 }
        };
        var clients = new[] { leader, memberA, memberB, memberC };
        for (var evaluator = 0; evaluator < clients.Length; evaluator++)
        for (var evaluatee = 0; evaluatee < clients.Length; evaluatee++)
        {
            if (evaluator == evaluatee)
                continue;

            using var response = await clients[evaluator].PutAsJsonAsync(
                $"/api/projects/{projectId}/contribution/peer-evaluations/{api.ProfileIds[evaluatee]}",
                new ContributionPeerEvaluationRequest(ratings[evaluator, evaluatee], "E2E rating"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var leaderEvaluations = await ReadAsync<List<ContributionPeerEvaluationTaskResponse>>(
            await leader.GetAsync($"/api/projects/{projectId}/contribution/peer-evaluations/me"), HttpStatusCode.OK);
        Assert.Equal(3, leaderEvaluations.Count);
        Assert.DoesNotContain(leaderEvaluations, evaluation => evaluation.EvaluateeUserId == participantProfileIds[0]);

        var runOne = await ReadAsync<ContributionRunResponse>(
            await leader.PostAsync($"/api/projects/{projectId}/contribution/calculations", null), HttpStatusCode.OK);
        Assert.Equal(1, runOne.RunNumber);
        AssertScores(runOne.Results, participantProfileIds,
            task: [75m, 100m, 62.5m, 0m],
            deliverables: [50m, 100m, 50m, 0m],
            peers: [75m, 75m, 75m, 50m],
            total: [67.5m, 95m, 61.25m, 10m]);

        Assert.Equal(HttpStatusCode.Forbidden, (await memberA.GetAsync(
            $"/api/projects/{projectId}/contribution/calculations/{runOne.RunId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberA.PostAsJsonAsync(
            $"/api/projects/{projectId}/contribution/calculations/{runOne.RunId}/review",
            new ContributionReviewRequest("confirm", null))).StatusCode);

        var rejected = await ReadAsync<ContributionReviewResponse>(await leader.PostAsJsonAsync(
            $"/api/projects/{projectId}/contribution/calculations/{runOne.RunId}/review",
            new ContributionReviewRequest("requestRevision", "Recheck the contribution inputs.")),
            HttpStatusCode.OK);
        Assert.Equal(ContributionCycleStatus.CollectingEvaluations, rejected.CycleStatus);

        using (var updateEvaluation = await leader.PutAsJsonAsync(
                   $"/api/projects/{projectId}/contribution/peer-evaluations/{api.ProfileIds[1]}",
                   new ContributionPeerEvaluationRequest(4, "Reviewed for recalculation")))
            Assert.Equal(HttpStatusCode.OK, updateEvaluation.StatusCode);

        var runTwo = await ReadAsync<ContributionRunResponse>(
            await leader.PostAsync($"/api/projects/{projectId}/contribution/calculations", null), HttpStatusCode.OK);
        Assert.Equal(2, runTwo.RunNumber);
        AssertScores(runTwo.Results, participantProfileIds,
            task: [75m, 100m, 62.5m, 0m],
            deliverables: [50m, 100m, 50m, 0m],
            peers: [75m, 75m, 75m, 50m],
            total: [67.5m, 95m, 61.25m, 10m]);

        var updatedTask = await leader.PutAsJsonAsync(
            $"/api/projects/{projectId}/tasks/{tasks[0].Id}",
            new UpdateProjectTaskRequest(Title: "Must be locked while reviewing"));
        Assert.Equal(HttpStatusCode.Conflict, updatedTask.StatusCode);
        Assert.Equal("CONTRIBUTION_INPUTS_LOCKED", await ErrorCodeAsync(updatedTask));

        await api.SetTaskStatusAsync(tasks[0].Id, ProjectTaskStatus.ToDo);
        var staleConfirmation = await leader.PostAsJsonAsync(
            $"/api/projects/{projectId}/contribution/calculations/{runTwo.RunId}/review",
            new ContributionReviewRequest("confirm", null));
        Assert.Equal(HttpStatusCode.Conflict, staleConfirmation.StatusCode);
        Assert.Equal("CONTRIBUTION_INPUTS_CHANGED", await ErrorCodeAsync(staleConfirmation));
        await api.SetTaskStatusAsync(tasks[0].Id, ProjectTaskStatus.Completed);

        var confirmed = await ReadAsync<ContributionReviewResponse>(await leader.PostAsJsonAsync(
            $"/api/projects/{projectId}/contribution/calculations/{runTwo.RunId}/review",
            new ContributionReviewRequest("confirm", null)), HttpStatusCode.OK);
        Assert.Equal(ContributionCycleStatus.Finalized, confirmed.CycleStatus);

        var finalResults = await ReadAsync<ContributionFinalResultsResponse>(
            await memberA.GetAsync($"/api/projects/{projectId}/contribution/results"), HttpStatusCode.OK);
        Assert.Equal(2, finalResults.RunNumber);
        Assert.Equal(new[] { 67.5m, 95m, 61.25m, 10m },
            participantProfileIds.Select(id => finalResults.Results.Single(result => result.UserId == id).TotalScore));

        var duplicateConfirmation = await leader.PostAsJsonAsync(
            $"/api/projects/{projectId}/contribution/calculations/{runTwo.RunId}/review",
            new ContributionReviewRequest("confirm", null));
        Assert.Equal(HttpStatusCode.Conflict, duplicateConfirmation.StatusCode);

        var closedEvaluation = await memberA.PutAsJsonAsync(
            $"/api/projects/{projectId}/contribution/peer-evaluations/{api.ProfileIds[0]}",
            new ContributionPeerEvaluationRequest(4, null));
        Assert.Equal(HttpStatusCode.Conflict, closedEvaluation.StatusCode);

        await using var verificationScope = api.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<UniNetDbContext>();
        Assert.Equal(13, await db.ContributionPeerEvaluationHistory.CountAsync());
        Assert.Equal(2, await db.TaskReports.CountAsync(report => report.ReportGroupId == firstAReport.ReportGroupId));
        Assert.Equal(2, await db.ContributionCalculationRuns.CountAsync(run => run.CycleId == cycle.CycleId));
        var reviewDecisions = await db.ContributionReviews.Where(review => review.Run.CycleId == cycle.CycleId)
            .Select(review => review.Decision).ToListAsync();
        Assert.Contains(ContributionReviewDecision.RevisionRequested, reviewDecisions);
        Assert.Contains(ContributionReviewDecision.Confirmed, reviewDecisions);
    }

    private static async Task<ProjectTaskResponse> CreateTaskAsync(
        HttpClient client,
        Guid projectId,
        string title,
        ProjectTaskStatus status,
        Guid assigneeId,
        DateTimeOffset? deadline = null)
        => await ReadAsync<ProjectTaskResponse>(await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/tasks",
            new CreateProjectTaskRequest(title, $"Description for {title}", deadline ?? DateTimeOffset.UtcNow.AddDays(7),
                status, [assigneeId])), HttpStatusCode.Created);

    private static async Task<TaskReportResponse> CreateDraftAsync(
        HttpClient memberA,
        Guid projectId,
        Guid taskId,
        string content,
        string fileUrl)
        => await ReadAsync<TaskReportResponse>(await memberA.PostAsJsonAsync(
            $"/api/projects/{projectId}/tasks/{taskId}/reports/drafts", ReportRequest(content, fileUrl)),
            HttpStatusCode.Created);

    private static async Task<TaskReportResponse> UpdateDraftAsync(
        HttpClient client, Guid projectId, Guid taskId, Guid reportId, string content, string fileUrl)
        => await ReadAsync<TaskReportResponse>(await client.PutAsJsonAsync(
            $"/api/projects/{projectId}/tasks/{taskId}/reports/{reportId}/draft", ReportRequest(content, fileUrl)),
            HttpStatusCode.OK);

    private static async Task<TaskReportResponse> SubmitReportAsync(
        HttpClient client, Guid projectId, Guid taskId, string content, string fileUrl)
        => await ReadAsync<TaskReportResponse>(await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/tasks/{taskId}/reports", ReportRequest(content, fileUrl)),
            HttpStatusCode.Created);

    private static async Task<TaskReportResponse> ReviseReportAsync(
        HttpClient client, Guid projectId, Guid taskId, Guid reportId, string content, string fileUrl)
        => await ReadAsync<TaskReportResponse>(await client.PutAsJsonAsync(
            $"/api/projects/{projectId}/tasks/{taskId}/reports/{reportId}", ReportRequest(content, fileUrl)),
            HttpStatusCode.OK);

    private static async Task<TaskReportResponse> ReviewReportAsync(
        HttpClient client,
        Guid projectId,
        Guid taskId,
        Guid reportId,
        TaskReportReviewDecision decision,
        string? feedback = null)
        => await ReadAsync<TaskReportResponse>(await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/tasks/{taskId}/reports/{reportId}/reviews",
            new ReviewTaskReportRequest(decision, feedback)), HttpStatusCode.OK);

    private static SubmitTaskReportRequest ReportRequest(string content, string fileUrl)
        => new(content, [new TaskEvidenceRequest(fileUrl)]);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == expectedStatus,
                $"Expected {(int)expectedStatus}, got {(int)response.StatusCode}: {body}");
            return System.Text.Json.JsonSerializer.Deserialize<T>(body,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        }
    }

    private static Task<HttpResponseMessage> PatchAsJsonAsync<T>(HttpClient client, string path, T value)
        => client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, path) { Content = JsonContent.Create(value) });

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            return body.GetProperty("code").GetString();
        }
    }

    private static void AssertScores(
        IReadOnlyCollection<ContributionRunResultResponse> actual,
        IReadOnlyList<Guid> userIds,
        decimal[] task,
        decimal[] deliverables,
        decimal[] peers,
        decimal[] total)
    {
        Assert.Equal(userIds.Count, actual.Count);
        for (var index = 0; index < userIds.Count; index++)
        {
            var score = Assert.Single(actual, result => result.UserId == userIds[index]);
            Assert.Equal(task[index], score.TaskExecutionScore);
            Assert.Equal(deliverables[index], score.ApprovedDeliverablesScore);
            Assert.Equal(peers[index], score.PeerEvaluationScore);
            Assert.Equal(total[index], score.TotalScore);
        }
    }

    private sealed class TestApi(WebApplication app, string databasePath, Guid projectId,
        Guid[] accountIds, Guid[] profileIds) : IAsyncDisposable
    {
        private const string SigningKey = "isolated-contribution-http-key-0123456789";

        public Guid ProjectId => projectId;
        public Guid[] AccountIds => accountIds;
        public Guid[] ProfileIds => profileIds;

        public AsyncServiceScope CreateScope() => app.Services.CreateAsyncScope();

        public HttpClient Client(Guid accountId)
        {
            var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var token = new JwtSecurityToken(
                "contribution-http-tests",
                "contribution-http-tests",
                [
                    new Claim(JwtRegisteredClaimNames.Sub, accountId.ToString()),
                    new Claim(ClaimTypes.Role, "Student")
                ],
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                    SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            return client;
        }

        public async Task SetTaskStatusAsync(Guid taskId, ProjectTaskStatus status)
        {
            await using var scope = CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
            var task = await db.ProjectTasks.SingleAsync(item => item.Id == taskId);
            task.Status = status;
            task.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        public static async Task<TestApi> StartAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"uninet-contribution-http-{Guid.NewGuid():N}.sqlite");
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Services.AddDbContext<UniNetDbContext>(options => options
                .UseSqlite($"Data Source={databasePath}")
                .ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>());
            builder.Configuration["Cloudinary:CloudName"] = "test-cloud";
            builder.Services.AddScoped<AuthService>();
            builder.Services.AddScoped<ProjectModerationService>();
            builder.Services.AddScoped<IProjectService, ProjectService>();
            builder.Services.AddScoped<IProjectTaskService, ProjectTaskService>();
            builder.Services.AddScoped<IContributionService, ContributionService>();
            builder.Services.AddSingleton<IAIModerationService, FakeModerationService>();
            builder.Services.AddControllers().AddApplicationPart(typeof(ContributionController).Assembly);
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new()
                {
                    ValidateIssuer = true,
                    ValidIssuer = "contribution-http-tests",
                    ValidateAudience = true,
                    ValidAudience = "contribution-http-tests",
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                    ValidateLifetime = true,
                    RoleClaimType = ClaimTypes.Role
                };
            });
            builder.Services.AddAuthorization();
            var app = builder.Build();
            app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
            {
                var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
                if (error is ProjectException projectError)
                {
                    context.Response.StatusCode = projectError.Status;
                    await context.Response.WriteAsJsonAsync(new { code = projectError.Code, message = projectError.Message });
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "INTERNAL_ERROR",
                    message = error?.GetBaseException().Message
                });
            }));
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();

            Guid projectId;
            Guid[] accountIds;
            Guid[] profileIds;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
                await db.Database.EnsureCreatedAsync();
                var accounts = Enumerable.Range(0, 5).Select(index => new Account
                {
                    Email = $"contribution-http-{Guid.NewGuid():N}@example.test",
                    Role = AccountRole.Student,
                    Status = AccountStatus.Active,
                    Profile = new UserProfile
                    {
                        FullName = $"HTTP Participant {index}",
                        Nickname = $"Participant {index}"
                    }
                }).ToArray();
                var now = DateTimeOffset.UtcNow;
                var project = new Project
                {
                    CreatorId = accounts[0].Profile!.Id,
                    Title = "Contribution HTTP lifecycle",
                    ProjectField = "Software",
                    Description = "Isolated contribution lifecycle test",
                    MemberTarget = 4,
                    RecruitmentDeadline = now.AddDays(30),
                    ExpectedOutput = now.AddDays(60),
                    Visibility = ProjectVisibility.Private,
                    Status = ProjectStatus.Active,
                    RecruitmentStatus = RecruitmentStatus.Closed,
                    CreatedAt = now.AddDays(-30),
                    UpdatedAt = now.AddDays(-1)
                };
                db.Accounts.AddRange(accounts);
                db.Projects.Add(project);
                db.ProjectMembers.AddRange(accounts.Take(4).Select((account, index) => new ProjectMember
                {
                    ProjectId = project.Id,
                    UserId = account.Profile!.Id,
                    Role = index == 0 ? "Leader" : "Member",
                    Status = ProjectMemberStatus.Active,
                    JoinedAt = now.AddDays(-20),
                    CreatedAt = now.AddDays(-20),
                    UpdatedAt = now.AddDays(-20)
                }));
                await db.SaveChangesAsync();
                projectId = project.Id;
                accountIds = accounts.Select(account => account.Id).ToArray();
                profileIds = accounts.Select(account => account.Profile!.Id).ToArray();
            }

            await app.StartAsync();
            return new TestApi(app, databasePath, projectId, accountIds, profileIds);
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, databasePath + "-shm", databasePath + "-wal" })
                if (File.Exists(path))
                    File.Delete(path);
        }
    }

    private sealed class FakeModerationService : IAIModerationService
    {
        public Task<AiModerationResult> AnalyzeProjectAsync(
            ProjectModerationInput input, CancellationToken cancellationToken = default)
            => Task.FromResult(new AiModerationResult(
                "Approved", "Approved", false, false, false, 1m, "Test fixture", []));
    }
}
