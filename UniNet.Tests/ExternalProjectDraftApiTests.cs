using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using UniNet.API.Controllers;
using UniNet.Application;
using UniNet.Application.DTOs.Projects;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class ExternalProjectDraftApiTests
{
    private const string Route = "api/external-project-drafts";
    public sealed class TimeModel(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder builder, DbContext context)
        {
            base.Customize(builder, context);
            var converter = new ValueConverter<DateTimeOffset, long>(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
            foreach (var entity in builder.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?)) property.SetValueConverter(converter);
        }
    }

    private sealed class Api(WebApplication app, string database, Guid owner, Guid other, Guid skill) : IAsyncDisposable
    {
        private const string Key = "external-project-draft-tests-key-0123456789";
        public Guid Owner => owner;
        public Guid Other => other;
        public Guid Skill => skill;
        public AsyncServiceScope Scope() => app.Services.CreateAsyncScope();
        public HttpClient Client(Guid? id = null, string role = "Student")
        {
            var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            if (id.HasValue)
            {
                var token = new JwtSecurityToken("draft-tests", "draft-tests", [new Claim(JwtRegisteredClaimNames.Sub, id.ToString()!), new Claim(ClaimTypes.Role, role)],
                    expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            }
            return client;
        }
        public static async Task<Api> Start()
        {
            var path = Path.Combine(Path.GetTempPath(), $"uninet-draft-{Guid.NewGuid():N}.sqlite");
            var b = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            b.Logging.ClearProviders(); b.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            b.Services.AddDbContext<UniNetDbContext>(o => o.UseSqlite($"Data Source={path}").ReplaceService<IModelCustomizer, TimeModel>());
            b.Services.AddScoped<ExternalProjectDraftService>();
            b.Services.AddScoped<SpecializationService>();
            b.Services.AddControllers().AddApplicationPart(typeof(ExternalProjectDraftsController).Assembly);
            b.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new() { ValidateIssuer = true, ValidIssuer = "draft-tests", ValidateAudience = true, ValidAudience = "draft-tests",
                    ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), ValidateLifetime = true, RoleClaimType = ClaimTypes.Role };
            });
            b.Services.AddAuthorization();
            var app = b.Build();
            app.UseExceptionHandler(e => e.Run(async context =>
            {
                var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
                context.Response.StatusCode = error is ProjectException p ? p.Status : 500;
                await context.Response.WriteAsJsonAsync(new { code = error is ProjectException ex ? ex.Code : "INTERNAL_ERROR" });
            }));
            app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
            var owner = new Account { Email = "owner@draft.test", Role = AccountRole.Student, Profile = new() { FullName = "Submitter", Nickname = "submitter" } };
            var other = new Account { Email = "other@draft.test", Role = AccountRole.Student, Profile = new() { FullName = "Other", Nickname = "other" } };
            var skill = new Skill { Name = "C#" };
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>(); await db.Database.EnsureCreatedAsync();
                db.Accounts.AddRange(owner, other); db.Skills.Add(skill); await db.SaveChangesAsync();
            }
            await app.StartAsync(); return new(app, path, owner.Id, other.Id, skill.Id);
        }
        public async ValueTask DisposeAsync()
        {
            await app.StopAsync(); await app.DisposeAsync(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); System.IO.File.Delete(database);
        }
    }

    private static async Task<ExternalProjectDraftResponse> Create(HttpClient client)
    {
        var response = await client.PostAsync(Route, null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.NotNull(response.Headers.Location);
        return (await response.Content.ReadFromJsonAsync<ExternalProjectDraftResponse>())!;
    }
    private static ExternalProjectDraftData Complete(Api api, ExternalProjectDraftResponse d) => d.Data with
    { Title = "UniNet", Description = "A completed project", ProjectField = "IT", StartDate = "2024", EndDate = "2024", SkillIds = [api.Skill], SubmitterRole = "Backend Developer", SubmitterContribution = "Implemented the API", SubmitterIsLeader = false, LeaderName = "Actual external leader",
      SpecializationId = Guid.Parse("a4900000-0000-0000-0000-000000000001") };

    [Fact]
    public async Task SpecializationHttpRoutesBindCatalogAssignmentAndMatchingWithAdminAuthorization()
    {
        await using var api = await Api.Start();
        using var anonymous = api.Client();
        var catalog = (await anonymous.GetFromJsonAsync<List<SpecializationResponse>>("api/specializations"))!;
        var software = catalog.Single(s => s.Code == "SOFTWARE_ENGINEERING");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/admin/specializations")).StatusCode);
        using var student = api.Client(api.Owner);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("api/admin/specializations")).StatusCode);
        Guid projectId, assessorId;
        await using (var scope = api.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
            var admin = await db.Accounts.Include(a => a.Profile).SingleAsync(a => a.Id == api.Owner);
            admin.Role = AccountRole.Admin; admin.Status = AccountStatus.Active;
            var assessor = await db.Accounts.Include(a => a.Profile).SingleAsync(a => a.Id == api.Other);
            assessor.Role = AccountRole.Assessor; assessor.Status = AccountStatus.Active;
            assessorId = assessor.Profile!.Id;
            var project = new Project
            {
                CreatorId = admin.Profile!.Id, Title = "Test", ProjectField = "IT", Description = "Test",
                MemberTarget = 3, MinimumQuantity = 2, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(7)
            };
            db.Projects.Add(project); projectId = project.Id;
            await db.SaveChangesAsync();
        }
        using var client = api.Client(api.Owner, "Admin");
        var assigned = await client.PutAsJsonAsync($"api/admin/assessors/{assessorId}/specializations",
            new SetAssessorSpecializationsRequest([software.Id]));
        assigned.EnsureSuccessStatusCode();
        var changed = await client.PutAsJsonAsync($"api/admin/projects/{projectId}/specialization",
            new SetProjectSpecializationRequest(software.Id));
        changed.EnsureSuccessStatusCode();
        var matches = (await client.GetFromJsonAsync<MatchingAssessorsResponse>(
            $"api/admin/projects/{projectId}/suitable-assessors?page=1&pageSize=10"))!;
        Assert.Equal(assessorId, Assert.Single(matches.Assessors).UserId);
        Assert.Equal(software.Id, matches.Specialization.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"api/admin/projects/{projectId}/suitable-assessors")).StatusCode);
    }
    [Fact]
    public async Task ProjectSpecializationAllowsCreatorButRejectsAnotherStudent()
    {
        await using var api = await Api.Start();
        using var creator = api.Client(api.Owner);
        using var other = api.Client(api.Other);
        var catalog = (await creator.GetFromJsonAsync<List<SpecializationResponse>>("api/specializations"))!;
        var specialization = catalog.Single(s => s.Code == "SOFTWARE_ENGINEERING");
        Guid projectId;
        await using (var scope = api.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
            var creatorProfile = await db.UserProfiles.SingleAsync(p => p.AccountId == api.Owner);
            var project = new Project
            {
                CreatorId = creatorProfile.Id, Title = "Test", ProjectField = "IT", Description = "Test",
                MemberTarget = 3, MinimumQuantity = 2, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(7)
            };
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            projectId = project.Id;
        }

        var route = $"api/admin/projects/{projectId}/specialization";
        var request = new SetProjectSpecializationRequest(specialization.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PutAsJsonAsync(route, request)).StatusCode);
        var updated = await creator.PutAsJsonAsync(route, request);
        updated.EnsureSuccessStatusCode();
        Assert.Equal(specialization.Id, (await updated.Content.ReadFromJsonAsync<SpecializationResponse>())!.Id);
    }

    private static async Task<ExternalProjectDraftResponse> Patch(HttpClient client, ExternalProjectDraftResponse d, ExternalProjectDraftData data)
    {
        var response = await client.PatchAsJsonAsync($"{Route}/{d.DraftId}", new UpdateExternalProjectDraftRequest(d.Version, data)); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ExternalProjectDraftResponse>())!;
    }

    [Fact]
    public async Task HttpFlowCreatesExternalProjectOnceWithoutInitializingEvidenceReview()
    {
        await using var api = await Api.Start(); using var client = api.Client(api.Owner);
        var draft = await Create(client);
        Assert.Equal(ExternalProjectDraftStatus.Ready, draft.Status); Assert.Contains("submitterRole", draft.MissingFields);
        Assert.Null(draft.Extraction);
        Assert.Empty(draft.Files);
        Assert.DoesNotContain("evidenceFileIds", draft.MissingFields);
        draft = await Patch(client, draft, Complete(api, draft)); Assert.Empty(draft.MissingFields);
        var response = await client.PostAsJsonAsync($"{Route}/{draft.DraftId}/confirm", new ConfirmExternalProjectDraftRequest(draft.Version, true)); response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.TryGetProperty("evidenceModerationId", out _));
        var confirmed = (await response.Content.ReadFromJsonAsync<ConfirmExternalProjectDraftResponse>())!;
        var repeated = await client.PostAsJsonAsync($"{Route}/{draft.DraftId}/confirm", new ConfirmExternalProjectDraftRequest(draft.Version, true)); repeated.EnsureSuccessStatusCode();
        Assert.Equal(confirmed, await repeated.Content.ReadFromJsonAsync<ConfirmExternalProjectDraftResponse>());
        await using var scope = api.Scope(); var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
        var project = await db.Projects.Include(p => p.Creator).SingleAsync();
        Assert.Equal(ProjectOrigin.External, project.Origin); Assert.Equal(ProjectStatus.Completed, project.Status);
        Assert.Equal(draft.Data.SpecializationId, project.SpecializationId);
        Assert.Equal("SOFTWARE_ENGINEERING", ProjectMapping.ToDetailResponse(project, null).Specialization!.Code);
        Assert.Equal(RecruitmentStatus.Closed, project.RecruitmentStatus); Assert.Null(project.MemberTarget); Assert.Null(project.MinimumQuantity); Assert.Null(project.RecruitmentDeadline);
        Assert.NotNull(project.SharingRightsConfirmedAt); Assert.Equal("2024", project.ImplementationStartDate);
        Assert.Equal(api.Owner, project.Creator.AccountId); Assert.Null(ProjectIdentityMapping.LeaderId(project));
        Assert.Equal("Actual external leader", ProjectIdentityMapping.Leader(project)!.FullName);
        Assert.Empty(await db.ContributionCycles.ToListAsync()); Assert.Empty(await db.ProjectTasks.ToListAsync()); Assert.Empty(await db.ProjectMembers.ToListAsync());
        Assert.Empty(await db.ProjectModerations.ToListAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PatchAsJsonAsync($"{Route}/{draft.DraftId}", new UpdateExternalProjectDraftRequest(confirmed.Version, draft.Data))).StatusCode);
        var skill = await db.Skills.SingleAsync(); skill.Name = "C sharp"; await db.SaveChangesAsync();
        var replay = await client.PostAsJsonAsync($"{Route}/{draft.DraftId}/confirm", new ConfirmExternalProjectDraftRequest(draft.Version, true));
        replay.EnsureSuccessStatusCode(); Assert.Equal(confirmed, await replay.Content.ReadFromJsonAsync<ConfirmExternalProjectDraftResponse>());
    }

    [Fact]
    public async Task OnlyTheDraftOwnerCanReadAndEdit()
    {
        await using var api = await Api.Start(); using var owner = api.Client(api.Owner); using var other = api.Client(api.Other); using var anonymous = api.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(Route, null)).StatusCode);
        var draft = await Create(owner);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{Route}/{draft.DraftId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PatchAsJsonAsync($"{Route}/{draft.DraftId}", new UpdateExternalProjectDraftRequest(draft.Version, draft.Data))).StatusCode);
    }

    [Fact]
    public async Task StaleVersionMissingFieldsAndMissingConsentCannotConfirm()
    {
        await using var api = await Api.Start(); using var client = api.Client(api.Owner);
        var draft = await Create(client);
        Assert.Contains("specializationId", draft.MissingFields);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync($"{Route}/{draft.DraftId}",
            new UpdateExternalProjectDraftRequest(draft.Version, Complete(api, draft) with { SpecializationId = Guid.NewGuid() }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{Route}/{draft.DraftId}/confirm", new ConfirmExternalProjectDraftRequest(draft.Version, true))).StatusCode);
        var edited = await Patch(client, draft, Complete(api, draft));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PatchAsJsonAsync($"{Route}/{draft.DraftId}", new UpdateExternalProjectDraftRequest(draft.Version, Complete(api, draft)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{Route}/{draft.DraftId}/confirm", new ConfirmExternalProjectDraftRequest(edited.Version, false))).StatusCode);
        await using var scope = api.Scope(); Assert.Empty(await scope.ServiceProvider.GetRequiredService<UniNetDbContext>().Projects.ToListAsync());
    }

    [Fact]
    public async Task DraftUpdateFailureRollsBackProjectAndDraftConfirmation()
    {
        await using var api = await Api.Start(); using var client = api.Client(api.Owner);
        var draft = await Create(client); draft = await Patch(client, draft, Complete(api, draft));
        await using (var scope = api.Scope())
            await scope.ServiceProvider.GetRequiredService<UniNetDbContext>().Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_confirmation BEFORE UPDATE ON ExternalProjectDrafts BEGIN SELECT RAISE(ABORT, 'fixture failure'); END;");
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsJsonAsync($"{Route}/{draft.DraftId}/confirm", new ConfirmExternalProjectDraftRequest(draft.Version, true))).StatusCode);
        await using var check = api.Scope(); var db = check.ServiceProvider.GetRequiredService<UniNetDbContext>();
        Assert.Empty(await db.Projects.ToListAsync());
        var stored = await db.ExternalProjectDrafts.SingleAsync(); Assert.Equal(ExternalProjectDraftStatus.Ready, stored.Status); Assert.Null(stored.ConfirmedProjectId);
    }

    [Fact]
    public async Task RetiredEvidenceReviewRoutesAreNotExposed()
    {
        await using var api = await Api.Start(); using var client = api.Client(api.Owner);
        var route = $"api/projects/{Guid.NewGuid()}/evidence-moderation";
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(route + "/retry", new { })).StatusCode);
    }
}
