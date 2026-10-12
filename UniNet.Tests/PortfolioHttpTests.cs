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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using UniNet.API.Controllers;
using UniNet.API.Services;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

// Real HTTP controllers, JWT authorization and persistent SQLite; never uses a real account/database.
public sealed class PortfolioHttpTests
{
    private sealed class PortraitFixture : IPortraitProcessor, IPortraitCloudStorage
    {
        public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==");
        public Task<byte[]> Process(byte[] source, CancellationToken ct) => Task.FromResult(Png);
        public Task<string> Upload(byte[] png, string id, CancellationToken ct) => Task.FromResult($"https://res.cloudinary.com/fixture/image/upload/v1/{id}.png");
        public Task Destroy(string id, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class ImageFixture : ICloudImageStorage
    {
        public int Uploads;
        public ImageUploadRequest? LastUpload;
        public Task<ImageUploadResult> Upload(ImageUploadRequest request, CancellationToken ct)
        {
            Uploads++;
            LastUpload = request;
            if (request.ResourceType == "image")
                Assert.Equal(PortraitFixture.Png, request.Bytes);
            var suffix = request.ResourceType == "image" ? $".{request.Format}" : string.Empty;
            return Task.FromResult(new ImageUploadResult(
                $"https://res.cloudinary.com/fixture/{request.ResourceType}/upload/v1/{request.PublicId}{suffix}",
                request.PublicId,
                request.Format,
                request.ResourceType));
        }
        public Task Destroy(string publicId, CancellationToken ct) => Task.CompletedTask;
    }
    private static byte[] CreateOfficePackage(string mainPart)
    {
        using var stream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(
                   stream,
                   System.IO.Compression.ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            archive.CreateEntry("[Content_Types].xml");
            archive.CreateEntry(mainPart);
        }
        return stream.ToArray();
    }
    [Fact]
    public async Task SharedImageUploadWorksForActiveStudentAndPartnerWithoutChangingProfile()
    {
        await using var server = await TestApi.Start();
        using var owner = server.Client(server.OwnerId, "Student"); using var company = server.Client(server.CompanyId, "Partner");
        using var anonymous = server.Client(); using var unknown = server.Client(Guid.NewGuid());
        static MultipartFormDataContent File()
        {
            var body = new MultipartFormDataContent(); body.Add(new ByteArrayContent(PortraitFixture.Png), "file", "../../untrusted.html"); return body;
        }
        using var anonymousBody = File(); using var unknownBody = File();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("api/images", anonymousBody)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await unknown.PostAsync("api/images", unknownBody)).StatusCode);
        var publicIds = new HashSet<string>();
        foreach (var (client, id) in new[] { (owner, server.OwnerId), (company, server.CompanyId), (owner, server.OwnerId) })
        {
            using var body = File();
            var response = await client.PostAsync("api/images", body); response.EnsureSuccessStatusCode();
            var result = (await response.Content.ReadFromJsonAsync<ImageUploadResult>())!;
            Assert.Equal("png", result.Format); Assert.Matches($"^uninet/images/{id:N}/[a-f0-9]{{32}}$", result.PublicId);
            Assert.True(publicIds.Add(result.PublicId)); Assert.EndsWith($"/{result.PublicId}.png", result.ImageUrl);
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            Assert.Equal("image", result.ResourceType);
        }
        Assert.Equal(3, server.Uploads);
        Assert.Null((await owner.GetFromJsonAsync<PortfolioResponse>("api/profile/portfolio/me"))!.Profile.AvatarUrl);
        await using var scope = server.Scope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<UniNetDbContext>().PortraitAssets.ToArrayAsync());
    }

    [Fact]
    public async Task SharedUploadEndpointAcceptsPdfAndDocxAsRawResources()
    {
        await using var server = await TestApi.Start();
        using var owner = server.Client(server.OwnerId);
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nfixture\n%%EOF");
        using (var body = new MultipartFormDataContent())
        {
            var content = new ByteArrayContent(pdf);
            content.Headers.ContentType = new("application/pdf");
            body.Add(content, "file", "task-report.pdf");

            var response = await owner.PostAsync("api/images", body);
            response.EnsureSuccessStatusCode();
            var result = (await response.Content.ReadFromJsonAsync<ImageUploadResult>())!;

            Assert.Equal("raw", result.ResourceType);
            Assert.Equal("pdf", result.Format);
            Assert.Equal("task-report.pdf", result.FileName);
            Assert.Matches($"^uninet/evidence/{server.OwnerId:N}/[a-f0-9]{{32}}\\.pdf$", result.PublicId);
            Assert.StartsWith("https://res.cloudinary.com/fixture/raw/upload/", result.ImageUrl);
        }

        using (var body = new MultipartFormDataContent())
        {
            var content = new ByteArrayContent(CreateOfficePackage("word/document.xml"));
            content.Headers.ContentType = new("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
            body.Add(content, "file", "task-report.docx");

            var response = await owner.PostAsync("api/images", body);
            response.EnsureSuccessStatusCode();
            var result = (await response.Content.ReadFromJsonAsync<ImageUploadResult>())!;

            Assert.Equal("raw", result.ResourceType);
            Assert.Equal("docx", result.Format);
            Assert.Equal("task-report.docx", result.FileName);
            Assert.EndsWith(".docx", result.PublicId);
        }

        Assert.Equal(2, server.Uploads);
    }

    [Fact]
    public async Task SharedUploadEndpointRejectsDocumentWithMismatchedMimeBeforeCloudinary()
    {
        await using var server = await TestApi.Start();
        using var owner = server.Client(server.OwnerId);
        using var body = new MultipartFormDataContent();
        var content = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF"));
        content.Headers.ContentType = new("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        body.Add(content, "file", "task-report.pdf");

        var response = await owner.PostAsync("api/images", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, server.Uploads);
    }
    [Fact]
    public async Task SharedImageUploadRejectsInvalidFilesAndInactiveAccountsBeforeCloudinary()
    {
        await using var server = await TestApi.Start(); using var owner = server.Client(server.OwnerId);
        foreach (var bytes in new[] { Array.Empty<byte>(), Encoding.UTF8.GetBytes("<svg></svg>"), new byte[ProfileImageStorage.MaxBytes + 1] })
        {
            using var body = new MultipartFormDataContent(); body.Add(new ByteArrayContent(bytes), "file", "fake.png");
            var response = await owner.PostAsync("api/images", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            Assert.Equal("INVALID_IMAGE", error!["code"]);
        }
        await using (var scope = server.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
            var account = await db.Accounts.FindAsync(server.OwnerId); account!.Status = AccountStatus.Suspended; await db.SaveChangesAsync();
        }
        using var inactiveBody = new MultipartFormDataContent(); inactiveBody.Add(new ByteArrayContent(PortraitFixture.Png), "file", "image.png");
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.PostAsync("api/images", inactiveBody)).StatusCode);
        Assert.Equal(0, server.Uploads);
    }
    [Fact]
    public async Task PortraitHttpProcessConfirmOwnershipAndSaveRoundTrip()
    {
        await using var server = await TestApi.Start();
        using var owner = server.Client(server.OwnerId, "Student"); using var company = server.Client(server.CompanyId, "Partner"); using var anonymous = server.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("api/ai-images/confirm", new { sessionId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await company.PostAsJsonAsync("api/ai-images/confirm", new { sessionId = Guid.NewGuid() })).StatusCode);
        using var upload = new MultipartFormDataContent(); upload.Add(new ByteArrayContent(PortraitFixture.Png), "file", "portrait.png");
        var response = await owner.PostAsync("api/ai-images/remove-background", upload); response.EnsureSuccessStatusCode();
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("data").GetProperty("sessionId").GetString();
        Assert.Null((await owner.GetFromJsonAsync<PortfolioResponse>("api/profile/portfolio/me"))!.Profile.AvatarUrl);
        var confirmed = await owner.PostAsJsonAsync("api/ai-images/confirm", new { sessionId = id }); confirmed.EnsureSuccessStatusCode();
        using var result = System.Text.Json.JsonDocument.Parse(await confirmed.Content.ReadAsStringAsync());
        var url = result.RootElement.GetProperty("data").GetProperty("imageUrl").GetString();
        var forgedCompany = await company.PutAsJsonAsync("api/profile/partner", new PartnerProfileRequest("Company", url, null, null, PartnerType.Company, null, null, null, null, null, "Company"));
        Assert.Equal(HttpStatusCode.BadRequest, forgedCompany.StatusCode);
        Assert.Contains("INVALID_PORTRAIT_ASSET", await forgedCompany.Content.ReadAsStringAsync());
        var baseline = (await owner.GetFromJsonAsync<PortfolioResponse>("api/profile/portfolio/me"))!; Assert.Null(baseline.Profile.AvatarUrl);
        var save = await owner.PutAsJsonAsync("api/profile/portfolio/editor/me", new PortfolioEditorRequest(new("HTTP Owner", "Owner", url, null, null), baseline.Portfolio)); save.EnsureSuccessStatusCode();
        Assert.Equal(url, (await owner.GetFromJsonAsync<PortfolioResponse>("api/profile/portfolio/me"))!.Profile.AvatarUrl);
        var retry = await owner.PostAsJsonAsync("api/ai-images/confirm", new { sessionId = id }); retry.EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task HttpSaveReadDisplayAndAccessPoliciesRoundTrip()
    {
        await using var server = await TestApi.Start();
        using var owner = server.Client(server.OwnerId, "Student");
        using var visitor = server.Client(Guid.NewGuid(), "Student");
        using var company = server.Client(server.CompanyId, "Partner");
        using var anonymous = server.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync("api/profile/portfolio/display/me", new { showPortfolio = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await company.PutAsJsonAsync("api/profile/portfolio/display/me", new { showPortfolio = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("api/profile/portfolio/display/me", new { })).StatusCode);

        var original = await owner.GetFromJsonAsync<PortfolioResponse>("api/profile/portfolio/me");
        Assert.NotNull(original);
        foreach (var (template, theme) in new[] { ("developer-modern", "amber"), ("developer-showcase", "mint"), ("developer-landscape", "pink") })
        {
            var payload = new PortfolioEditorRequest(new("HTTP Owner", "Owner", null, null, null), original.Portfolio with {
                Headline = "Persistent content", Appearance = new() { Profile = new() { Template = template, Theme = theme } }
            });
            var write = await owner.PutAsJsonAsync("api/profile/portfolio/editor/me", payload); write.EnsureSuccessStatusCode();
            // A fresh HTTP request creates a fresh DbContext, verifying persisted data rather than tracked entities.
            var reread = await owner.GetFromJsonAsync<PortfolioResponse>("api/profile/portfolio/me");
            Assert.Equal(template, reread!.Portfolio.Appearance.Profile.Template); Assert.Equal(theme, reread.Portfolio.Appearance.Profile.Theme);
            Assert.Single(reread.Portfolio.Projects); Assert.Equal("Persistent content", reread.Portfolio.Headline);
        }
        var invalid = await owner.PutAsJsonAsync("api/profile/portfolio/editor/me", new PortfolioEditorRequest(new("Must not save", "Owner", null, null, null), original.Portfolio with { Appearance = new() { Profile = new() { Template = "developer-showcase", Theme = "blue" } } }));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("INVALID_APPEARANCE", await invalid.Content.ReadAsStringAsync());
        Assert.Equal("HTTP Owner", (await owner.GetFromJsonAsync<PortfolioResponse>("api/profile/portfolio/me"))!.Profile.FullName);

        var deniedWrite = await visitor.PutAsJsonAsync("api/profile/portfolio/display/me", new { showPortfolio = false, accountId = server.OwnerId });
        Assert.Equal(HttpStatusCode.NotFound, deniedWrite.StatusCode);
        var off = await owner.PutAsJsonAsync("api/profile/portfolio/display/me", new { showPortfolio = false }); off.EnsureSuccessStatusCode();
        var ownOff = await owner.GetFromJsonAsync<PortfolioResponse>("api/profile/portfolio/me");
        Assert.False(ownOff!.Portfolio.Appearance.Profile.ShowPortfolio); Assert.Single(ownOff.Portfolio.Projects); Assert.True(ownOff.Portfolio.IsPublic);
        var publicOff = await anonymous.GetFromJsonAsync<PortfolioResponse>($"api/profile/portfolio/{server.ProfileId}");
        Assert.False(publicOff!.IsOwner); Assert.Empty(publicOff.Portfolio.Projects); Assert.Null(publicOff.SkillCatalog);
        var on = await owner.PutAsJsonAsync("api/profile/portfolio/display/me", new { showPortfolio = true }); on.EnsureSuccessStatusCode();
        using var otherDevice = server.Client(server.OwnerId, "Student");
        var restored = await otherDevice.GetFromJsonAsync<PortfolioResponse>("api/profile/portfolio/me");
        Assert.True(restored!.Portfolio.Appearance.Profile.ShowPortfolio);
        Assert.Equal("developer-landscape", restored.Portfolio.Appearance.Profile.Template); Assert.Equal("pink", restored.Portfolio.Appearance.Profile.Theme);
        Assert.Single(restored.Portfolio.Projects);

        var appearance = await owner.PutAsJsonAsync("api/profile/appearance/me", restored.Portfolio.Appearance with { Profile = restored.Portfolio.Appearance.Profile with { HiddenSections = ["other-projects", "contact"] } }); appearance.EnsureSuccessStatusCode();
        var filtered = await anonymous.GetFromJsonAsync<PortfolioResponse>($"api/profile/portfolio/{server.ProfileId}");
        Assert.Empty(filtered!.Portfolio.Projects); Assert.Null(filtered.Portfolio.ContactEmail);
        Assert.DoesNotContain("private-code", System.Text.Json.JsonSerializer.Serialize(filtered));
        var privateSave = await owner.PutAsJsonAsync("api/profile/portfolio/editor/me", new PortfolioEditorRequest(new("HTTP Owner", "Owner", null, null, null), restored.Portfolio with { IsPublic = false })); privateSave.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"api/profile/portfolio/{server.ProfileId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"api/profile/portfolio/{server.ProfileId}")).StatusCode);
    }

    private sealed class TestApi(WebApplication app, string databasePath, Guid ownerId, Guid profileId, Guid companyId) : IAsyncDisposable
    {
        private const string Key = "isolated-portfolio-http-key-01234567890123456789";
        public Guid OwnerId => ownerId; public Guid ProfileId => profileId; public Guid CompanyId => companyId;
        public int Uploads => ((ImageFixture)app.Services.GetRequiredService<ICloudImageStorage>()).Uploads;
        public ImageUploadRequest? LastUpload => ((ImageFixture)app.Services.GetRequiredService<ICloudImageStorage>()).LastUpload;
        public AsyncServiceScope Scope() => app.Services.CreateAsyncScope();
        public HttpClient Client(Guid? accountId = null, string role = "Student")
        {
            var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            if (accountId.HasValue)
            {
                var token = new JwtSecurityToken("portfolio-tests", "portfolio-tests", [new Claim(JwtRegisteredClaimNames.Sub, accountId.Value.ToString()), new Claim(ClaimTypes.Role, role)], expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            }
            return client;
        }
        public static async Task<TestApi> Start()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"uninet-portfolio-{Guid.NewGuid():N}.sqlite");
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders(); builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Services.AddDbContext<UniNetDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
            builder.Configuration["Cloudinary:CloudName"] = "fixture";
            builder.Services.AddScoped<PortfolioService>(); builder.Services.AddScoped<ProfileService>(); builder.Services.AddScoped<AuthService>();
            builder.Services.AddSingleton<IPortraitProcessor, PortraitFixture>(); builder.Services.AddSingleton<IPortraitCloudStorage, PortraitFixture>(); builder.Services.AddSingleton<PortraitSessions>();
            builder.Services.AddSingleton<ICloudImageStorage, ImageFixture>();
            builder.Services.AddControllers().AddApplicationPart(typeof(ProfileController).Assembly);
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new() { ValidateIssuer = true, ValidIssuer = "portfolio-tests", ValidateAudience = true, ValidAudience = "portfolio-tests", ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), ValidateLifetime = true, RoleClaimType = ClaimTypes.Role };
            });
            builder.Services.AddAuthorization();
            var app = builder.Build();
            app.UseExceptionHandler(errors => errors.Run(async context => {
                var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
                context.Response.StatusCode = error is AuthException auth ? auth.Status : 500;
                await context.Response.WriteAsJsonAsync(new { code = error is AuthException exception ? exception.Code : "INTERNAL_ERROR" });
            }));
            app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
            Guid ownerId, profileId, companyId;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>(); await db.Database.EnsureCreatedAsync();
                var owner = new Account { Email = "http-owner@example.org", Role = AccountRole.Student, Profile = new UserProfile { FullName = "HTTP Owner", Nickname = "Owner", StudentCode = "private-code", CareerProfile = new CareerProfile { IsPublic = true, ProjectsJson = "[{\"title\":\"Preserved\",\"technologies\":[]}]" } } };
                var company = new Account { Email = "http-company@example.org", Role = AccountRole.Partner, Profile = new UserProfile { FullName = "Company", Nickname = "Company" } };
                db.Accounts.AddRange(owner, company); await db.SaveChangesAsync(); ownerId = owner.Id; profileId = owner.Profile.Id; companyId = company.Id;
            }
            await app.StartAsync(); return new(app, databasePath, ownerId, profileId, companyId);
        }
        public async ValueTask DisposeAsync()
        {
            await app.StopAsync(); await app.DisposeAsync(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }
}
