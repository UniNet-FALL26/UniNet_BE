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
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

// Real HTTP controllers, JWT authorization and persistent SQLite; never uses a real account/database.
public sealed class PortfolioHttpTests
{
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
            builder.Services.AddScoped<PortfolioService>(); builder.Services.AddScoped<ProfileService>(); builder.Services.AddScoped<AuthService>();
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
