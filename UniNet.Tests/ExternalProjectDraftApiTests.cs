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
using UniNet.Application.Interfaces;
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
    private sealed class Providers : IUploadedDocumentContentReader, IProjectInformationExtractor
    {
        public string Model => "fixture-model";
        public int ExtractionCalls;
        public bool TransientFailure;
        public Task<byte[]> ReadAsync(UploadedFile f, CancellationToken ct = default) => Task.FromResult(Encoding.UTF8.GetBytes("UniNet uses C#. Completed 2024."));
        public Task<ProjectInformationExtraction> ExtractAsync(IReadOnlyList<ProjectDocumentText> docs, CancellationToken ct = default)
        {
            ExtractionCalls++;
            if (TransientFailure) throw new ProjectExtractionException("EXTRACTION_AI_FAILED", "Fixture transient", true);
            var doc = docs[0];
            SourcedProjectText Field(string v) => new(v, [new(doc.FileId, 1, doc.Segments[0].Text)]);
            return Task.FromResult(new ProjectInformationExtraction
            {
                Title = Field("UniNet"), Description = Field("A completed project"), ProjectField = Field("IT"),
                StartDate = Field("2024"), EndDate = Field("2024"), Skills = [new(Field("C#"))]
            });
        }
    }

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

    private sealed class Api(WebApplication app, string database, Guid owner, Guid other, Guid file, Guid foreignFile, Guid skill) : IAsyncDisposable
    {
        private const string Key = "external-project-draft-tests-key-0123456789";
        public Guid Owner => owner;
        public Guid Other => other;
        public Guid File => file;
        public Guid ForeignFile => foreignFile;
        public Guid Skill => skill;
        public Providers Providers => app.Services.GetRequiredService<Providers>();
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
        public async Task RunQueue(DateTimeOffset? now = null)
        {
            await using var scope = Scope();
            await scope.ServiceProvider.GetRequiredService<ExternalProjectDraftTasks>().RunAsync(now ?? DateTimeOffset.UtcNow);
        }
        public static async Task<Api> Start()
        {
            var path = Path.Combine(Path.GetTempPath(), $"uninet-draft-{Guid.NewGuid():N}.sqlite");
            var b = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            b.Logging.ClearProviders(); b.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            b.Services.AddDbContext<UniNetDbContext>(o => o.UseSqlite($"Data Source={path}").ReplaceService<IModelCustomizer, TimeModel>());
            b.Services.AddScoped<ExternalProjectDraftService>(); b.Services.AddScoped<ExternalProjectDraftTasks>();
            b.Services.AddScoped<ExternalProjectDraftExtractionService>(); b.Services.AddSingleton<IProjectDocumentTextReader, ProjectDocumentTextReader>();
            b.Services.AddSingleton<Providers>();
            b.Services.AddSingleton<IUploadedDocumentContentReader>(s => s.GetRequiredService<Providers>());
            b.Services.AddSingleton<IProjectInformationExtractor>(s => s.GetRequiredService<Providers>());
            b.Configuration["Gemini:Model"] = "fixture-model";
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
            UploadedFile Upload(Account a) => new() { OwnerAccount = a, FileName = "report.txt", ResourceType = "raw", Format = "txt", SizeBytes = 30,
                PublicId = $"uninet/evidence/{a.Id:N}/{Guid.NewGuid():N}.txt", Url = "https://example.test/report.txt" };
            var file = Upload(owner); var foreign = Upload(other); var skill = new Skill { Name = "C#" };
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>(); await db.Database.EnsureCreatedAsync();
                db.UploadedFiles.AddRange(file, foreign); db.Skills.Add(skill); await db.SaveChangesAsync();
            }
            await app.StartAsync(); return new(app, path, owner.Id, other.Id, file.Id, foreign.Id, skill.Id);
        }
        public async ValueTask DisposeAsync()
        {
            await app.StopAsync(); await app.DisposeAsync(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); System.IO.File.Delete(database);
        }
    }

    private static async Task<ExternalProjectDraftResponse> Create(Api api, HttpClient client)
    {
        var response = await client.PostAsJsonAsync(Route, new CreateExternalProjectDraftRequest([api.File]));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); Assert.NotNull(response.Headers.Location);
        return (await response.Content.ReadFromJsonAsync<ExternalProjectDraftResponse>())!;
    }
    private static async Task<ExternalProjectDraftResponse> Ready(Api api, HttpClient client)
    {
        var draft = await Create(api, client); await api.RunQueue();
        return (await client.GetFromJsonAsync<ExternalProjectDraftResponse>($"{Route}/{draft.DraftId}"))!;
    }
    private static ExternalProjectDraftData Complete(ExternalProjectDraftResponse d) => d.Data with
    { SubmitterRole = "Backend Developer", SubmitterContribution = "Implemented the API", SubmitterIsLeader = false, LeaderName = "Actual external leader" };
    private static async Task<ExternalProjectDraftResponse> Patch(HttpClient client, ExternalProjectDraftResponse d, ExternalProjectDraftData data)
    {
        var response = await client.PatchAsJsonAsync($"{Route}/{d.DraftId}", new UpdateExternalProjectDraftRequest(d.Version, data)); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ExternalProjectDraftResponse>())!;
    }

    [Fact]
    public async Task HttpFlowCreatesExternalProjectOnceWithoutInitializingEvidenceReview()
    {
        await using var api = await Api.Start(); using var client = api.Client(api.Owner);
        var draft = await Ready(api, client);
        Assert.Equal(ExternalProjectDraftStatus.Ready, draft.Status); Assert.Contains("submitterRole", draft.MissingFields);
        draft = await Patch(client, draft, Complete(draft)); Assert.Empty(draft.MissingFields);
        var response = await client.PostAsJsonAsync($"{Route}/{draft.DraftId}/confirm", new ConfirmExternalProjectDraftRequest(draft.Version, true)); response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.TryGetProperty("evidenceModerationId", out _));
        var confirmed = (await response.Content.ReadFromJsonAsync<ConfirmExternalProjectDraftResponse>())!;
        var repeated = await client.PostAsJsonAsync($"{Route}/{draft.DraftId}/confirm", new ConfirmExternalProjectDraftRequest(draft.Version, true)); repeated.EnsureSuccessStatusCode();
        Assert.Equal(confirmed, await repeated.Content.ReadFromJsonAsync<ConfirmExternalProjectDraftResponse>());
        await using var scope = api.Scope(); var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
        var project = await db.Projects.Include(p => p.Creator).Include(p => p.EvidenceDocuments).SingleAsync();
        Assert.Equal(ProjectOrigin.External, project.Origin); Assert.Equal(ProjectStatus.Completed, project.Status);
        Assert.Equal(RecruitmentStatus.Closed, project.RecruitmentStatus); Assert.Null(project.MemberTarget); Assert.Null(project.MinimumQuantity); Assert.Null(project.RecruitmentDeadline);
        Assert.NotNull(project.SharingRightsConfirmedAt); Assert.Equal("2024", project.ImplementationStartDate);
        Assert.Equal(api.Owner, project.Creator.AccountId); Assert.Null(ProjectIdentityMapping.LeaderId(project));
        Assert.Equal("Actual external leader", ProjectIdentityMapping.Leader(project)!.FullName);
        Assert.Empty(await db.ContributionCycles.ToListAsync()); Assert.Empty(await db.ProjectTasks.ToListAsync()); Assert.Empty(await db.ProjectMembers.ToListAsync());
        Assert.Equal(api.File, Assert.Single(project.EvidenceDocuments).UploadedFileId);
        Assert.Empty(await db.ProjectModerations.ToListAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PatchAsJsonAsync($"{Route}/{draft.DraftId}", new UpdateExternalProjectDraftRequest(confirmed.Version, draft.Data))).StatusCode);
        var skill = await db.Skills.SingleAsync(); skill.Name = "C sharp"; await db.SaveChangesAsync();
        var replay = await client.PostAsJsonAsync($"{Route}/{draft.DraftId}/confirm", new ConfirmExternalProjectDraftRequest(draft.Version, true));
        replay.EnsureSuccessStatusCode(); Assert.Equal(confirmed, await replay.Content.ReadFromJsonAsync<ConfirmExternalProjectDraftResponse>());
    }

    [Fact]
    public async Task AuthorizationFileOwnershipAndProcessingLockAreEnforced()
    {
        await using var api = await Api.Start(); using var owner = api.Client(api.Owner); using var other = api.Client(api.Other); using var anonymous = api.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(Route, new CreateExternalProjectDraftRequest([api.File]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(Route, new CreateExternalProjectDraftRequest([api.ForeignFile]))).StatusCode);
        var draft = await Create(api, owner);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{Route}/{draft.DraftId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PatchAsJsonAsync($"{Route}/{draft.DraftId}", new UpdateExternalProjectDraftRequest(draft.Version, draft.Data))).StatusCode);
    }

    [Fact]
    public async Task StaleVersionMissingFieldsConsentAndForeignEvidenceCannotConfirm()
    {
        await using var api = await Api.Start(); using var client = api.Client(api.Owner);
        var draft = await Ready(api, client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{Route}/{draft.DraftId}/confirm", new ConfirmExternalProjectDraftRequest(draft.Version, true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync($"{Route}/{draft.DraftId}", new UpdateExternalProjectDraftRequest(draft.Version, Complete(draft) with { EvidenceFileIds = [api.ForeignFile] }))).StatusCode);
        var edited = await Patch(client, draft, Complete(draft));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PatchAsJsonAsync($"{Route}/{draft.DraftId}", new UpdateExternalProjectDraftRequest(draft.Version, Complete(draft)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{Route}/{draft.DraftId}/confirm", new ConfirmExternalProjectDraftRequest(edited.Version, false))).StatusCode);
        await using var scope = api.Scope(); Assert.Empty(await scope.ServiceProvider.GetRequiredService<UniNetDbContext>().Projects.ToListAsync());
    }

    [Fact]
    public async Task QueueRetriesTransientFailuresAndRecoversAnExpiredLease()
    {
        await using var api = await Api.Start(); using var client = api.Client(api.Owner);
        var draft = await Create(api, client); api.Providers.TransientFailure = true;
        await api.RunQueue();
        Assert.Equal(ExternalProjectDraftStatus.Processing, (await client.GetFromJsonAsync<ExternalProjectDraftResponse>($"{Route}/{draft.DraftId}"))!.Status);
        api.Providers.TransientFailure = false;
        await using (var scope = api.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>(); var stored = await db.ExternalProjectDrafts.SingleAsync();
            stored.LeaseToken = Guid.NewGuid(); stored.LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); stored.Version++; await db.SaveChangesAsync();
        }
        await api.RunQueue(DateTimeOffset.UtcNow.AddMinutes(3));
        Assert.Equal(ExternalProjectDraftStatus.Ready, (await client.GetFromJsonAsync<ExternalProjectDraftResponse>($"{Route}/{draft.DraftId}"))!.Status);
        Assert.Equal(2, api.Providers.ExtractionCalls);
    }

    [Fact]
    public async Task AttachmentInsertFailureRollsBackProjectAndDraftConfirmation()
    {
        await using var api = await Api.Start(); using var client = api.Client(api.Owner);
        var draft = await Ready(api, client); draft = await Patch(client, draft, Complete(draft));
        await using (var scope = api.Scope())
            await scope.ServiceProvider.GetRequiredService<UniNetDbContext>().Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_attachment BEFORE INSERT ON ProjectEvidenceDocuments BEGIN SELECT RAISE(ABORT, 'fixture failure'); END;");
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsJsonAsync($"{Route}/{draft.DraftId}/confirm", new ConfirmExternalProjectDraftRequest(draft.Version, true))).StatusCode);
        await using var check = api.Scope(); var db = check.ServiceProvider.GetRequiredService<UniNetDbContext>();
        Assert.Empty(await db.Projects.ToListAsync()); Assert.Empty(await db.ProjectEvidenceDocuments.ToListAsync());
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
