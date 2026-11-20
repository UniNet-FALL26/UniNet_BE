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
    public async Task RemovedTaskEndpointsReturnGoneThroughHttp()
    {
        await using var api = await TestApi.StartAsync();
        using var leader = api.Client(api.AccountIds[0]);
        using var response = await leader.GetAsync($"/api/projects/{api.ProjectId}/tasks");
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("PROJECT_FEATURE_RETIRED", body.GetProperty("code").GetString());
        var task = Guid.NewGuid(); var report = Guid.NewGuid();
        var root = $"/api/projects/{api.ProjectId}/tasks";
        var calls = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Get, $"{root}/{task}", null),
            (HttpMethod.Post, root, new { title = "Retired", description = "fixture", deadline = DateTimeOffset.UtcNow }),
            (HttpMethod.Put, $"{root}/{task}", new { title = "Retired" }),
            (HttpMethod.Patch, $"{root}/{task}/status", new { status = 1 }),
            (HttpMethod.Delete, $"{root}/{task}", null),
            (HttpMethod.Patch, $"{root}/{task}/assignments", Array.Empty<Guid>()),
            (HttpMethod.Post, $"{root}/{task}/reports", new { content = "Retired", evidence = Array.Empty<object>() }),
            (HttpMethod.Post, $"{root}/{task}/reports/drafts", new { content = "Retired", evidence = Array.Empty<object>() }),
            (HttpMethod.Get, $"{root}/{task}/reports", null),
            (HttpMethod.Get, $"{root}/{task}/reports/{report}", null),
            (HttpMethod.Put, $"{root}/{task}/reports/{report}", new { content = "Retired", evidence = Array.Empty<object>() }),
            (HttpMethod.Put, $"{root}/{task}/reports/{report}/draft", new { content = "Retired", evidence = Array.Empty<object>() }),
            (HttpMethod.Post, $"{root}/{task}/reports/{report}/submit", null),
            (HttpMethod.Post, $"{root}/{task}/reports/{report}/withdraw", null),
            (HttpMethod.Delete, $"{root}/{task}/reports/{report}", null),
            (HttpMethod.Post, $"{root}/{task}/reports/{report}/reviews", new { decision = 0, feedback = "fixture" })
        };
        foreach (var call in calls)
        {
            using var request = new HttpRequestMessage(call.Method, call.Path);
            if (call.Body is not null) request.Content = JsonContent.Create(call.Body);
            using var rejected = await leader.SendAsync(request);
            Assert.Equal(HttpStatusCode.Gone, rejected.StatusCode);
            var error = await rejected.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.Equal("PROJECT_FEATURE_RETIRED", error.GetProperty("code").GetString());
        }
        leader.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await leader.GetAsync(root)).StatusCode);
        await using var scope = api.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
        Assert.DoesNotContain(db.Model.GetEntityTypes(), x => x.GetTableName() is "ProjectTasks" or "TaskEvidence" or "TaskReports");
        Assert.Empty(await db.ContributionCalculationRuns.ToListAsync());
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
        public Task<bool> ReviewContentAsync(string title, string content, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<AiModerationResult> AnalyzeProjectAsync(
            ProjectModerationInput input, CancellationToken cancellationToken = default)
            => Task.FromResult(new AiModerationResult(
                "Approved", "Approved", false, false, false, 1m, "Test fixture", []));
    }
}
