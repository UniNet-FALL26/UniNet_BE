using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using UniNet.API.Controllers;
using UniNet.API.Services;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class GitHubAppLinkHttpPostgresTests
{
    private sealed class Protector : IGitHubTokenProtector { public string Protect(string value) => value; public string Unprotect(string value) => value; }
    private sealed class Tokens : IGitHubAppInstallationTokenProvider
    { public Task<string> GetAsync(long installation, long repo, CancellationToken ct) => Task.FromResult("fixture"); public void InvalidateInstallation(long installation) { } }
    private sealed class AppProvider : IGitHubAppUserClient
    {
        public bool Admin = true, HasRepository = true, Suspended;
        public long Repo = 99;
        public Task<GitHubAppUserToken> ExchangeAsync(string code, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubAppUserIdentity> GetUserAsync(string token, CancellationToken ct) => Task.FromResult(new GitHubAppUserIdentity(123, "fixture"));
        public Task<GitHubAppUserPage<GitHubAppUserInstallation>> GetInstallationsAsync(string token, int page, CancellationToken ct) => Task.FromResult(new GitHubAppUserPage<GitHubAppUserInstallation>([new(42, 77, "org", "Organization", Suspended)], false));
        public Task<GitHubAppUserPage<GitHubAppUserRepository>> GetRepositoriesAsync(string token, long id, int page, CancellationToken ct) => Task.FromResult(new GitHubAppUserPage<GitHubAppUserRepository>(HasRepository ? [new(Repo, $"org/repo{Repo}", true, true, Admin, 77, "org", $"repo{Repo}", $"https://github.com/org/repo{Repo}")] : [], false));
    }
    private sealed class SyncProvider : IGitHubProviderClient
    {
        public Task<GitHubRemoteRepository> GetRepositoryAsync(string token, long id, CancellationToken ct) => Task.FromResult(new GitHubRemoteRepository(id, 77, "org", $"repo{id}", $"org/repo{id}", $"https://github.com/org/repo{id}", null, "main", true, false, true));
        public Task<GitHubOAuthTokens> ExchangeCodeAsync(string code, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubOAuthTokens> RefreshAsync(string token, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubUser> GetAuthenticatedUserAsync(string token, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<GitHubRemoteRepository>> GetRepositoriesAsync(string token, int page, int size, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubSyncPage> GetSyncPageAsync(string token, string repo, string category, string? scope, int page, int size, CancellationToken ct) => throw new NotSupportedException();
    }

    [Step5aPostgresFact]
    public async Task RealHttpLinkEnforcesProjectAndGitHubPermissionsAndCreatesOneInitialJob()
    {
        await using var db = await GitHubAppMigrationUpgradePostgresTests.CreateAsync("httpauth"); await db.Database.MigrateAsync();
        Account Student(string name) => new() { Email = name + "@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = name, Nickname = name } };
        var creator = Student("creator"); var manager = Student("manager"); var member = Student("member"); var outsider = Student("outsider"); var leader = Student("leader");
        var project = new Project { Creator = creator.Profile!, Title = "HTTP", ProjectField = "IT", Description = "Fixture", MemberTarget = 4, Status = ProjectStatus.Active };
        var now = DateTimeOffset.UtcNow;
        ProjectMember Membership(Account a, string role) => new() { Id = Guid.NewGuid(), ProjectId = project.Id, UserId = a.Profile!.Id, Role = role, Status = ProjectMemberStatus.Active, JoinedAt = now, CreatedAt = now, UpdatedAt = now };
        var c1 = new GitHubAppUserCredential { AccountId = creator.Id, GitHubUserId = 123, Login = "fixture", AccessTokenEncrypted = "fixture" };
        var c2 = new GitHubAppUserCredential { AccountId = manager.Id, GitHubUserId = 123, Login = "fixture", AccessTokenEncrypted = "fixture" };
        db.AddRange(creator, manager, member, outsider, leader, project, Membership(manager, "Member"), Membership(member, "Member"), Membership(leader, "Leader"), c1, c2,
            new ProjectGitManagerGrant { ProjectId = project.Id, UserProfileId = manager.Profile!.Id, GrantedByProfileId = creator.Profile!.Id }); await db.SaveChangesAsync();
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        const string key = "fixture-signing-key-at-least-32-bytes-long"; var appOptions = Options.Create(new GitHubAppOptions { Enabled = true }); var provider = new AppProvider();
        var builder = WebApplication.CreateSlimBuilder(); builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Services.AddDbContext<UniNetDbContext>(o => o.UseNpgsql(db.Database.GetConnectionString()));
        builder.Services.AddSingleton<IOptions<GitHubAppOptions>>(appOptions); builder.Services.AddSingleton<IOptions<GitSyncOptions>>(Options.Create(new GitSyncOptions { Enabled = true }));
        builder.Services.AddSingleton<IGitHubAppUserClient>(provider); builder.Services.AddSingleton<IGitHubTokenProtector, Protector>();
        builder.Services.AddSingleton<IGitHubAppInstallationTokenProvider, Tokens>(); builder.Services.AddSingleton<IGitHubProviderClient, SyncProvider>();
        builder.Services.AddScoped<GitHubAppUserGrantVerifier>(); builder.Services.AddScoped<GitHubAppRepositoryActivationService>();
        builder.Services.AddScoped<IGitHubAppRepositoryLinkService, GitHubAppRepositoryLinkService>();
        builder.Services.AddScoped<IGitHubAppAuthorizationService, GitHubAppAuthorizationService>(); builder.Services.AddScoped<IGitHubAppGrantService, GitHubAppGrantService>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => { o.MapInboundClaims = false; o.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidIssuer = "fixture", ValidateAudience = true, ValidAudience = "fixture", ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), ValidateLifetime = true, RoleClaimType = ClaimTypes.Role }; o.Events = new JwtBearerEvents { OnTokenValidated = async ctx => { if (!await AccountTokenValidation.ValidateAsync(ctx.Principal, ctx.HttpContext.RequestServices.GetRequiredService<UniNetDbContext>(), ctx.HttpContext.RequestAborted)) ctx.Fail("invalid"); } }; });
        builder.Services.AddAuthorization(); builder.Services.AddControllers().AddApplicationPart(typeof(GitHubAppAuthorizationController).Assembly);
        await using var host = builder.Build();
        host.Use(async (ctx, next) => { try { await next(); } catch (GitHubIntegrationException ex) { ctx.Response.StatusCode = ex.Status; await ctx.Response.WriteAsJsonAsync(new { code = ex.Code }); } });
        host.UseAuthentication(); host.UseAuthorization(); host.MapControllers(); await host.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            string Jwt(Account a) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("fixture", "fixture", [new Claim(JwtRegisteredClaimNames.Sub, a.Id.ToString()), new Claim(ClaimTypes.Role, "Student"), new Claim("session_version", "0")], expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)));
            async Task<HttpStatusCode> Link(Account? actor, Guid credential, long installation = 42, long repo = 99)
            { using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/projects/{project.Id}/git/app/repositories/link") { Content = JsonContent.Create(new { credentialId = credential, installationId = installation, repositoryId = repo }) }; if (actor is not null) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Jwt(actor)); using var response = await client.SendAsync(request); return response.StatusCode; }
            Assert.Equal(HttpStatusCode.Unauthorized, await Link(null, c1.Id));
            Assert.Equal(HttpStatusCode.Forbidden, await Link(member, c1.Id)); Assert.Equal(HttpStatusCode.Forbidden, await Link(outsider, c1.Id)); Assert.Equal(HttpStatusCode.Forbidden, await Link(leader, c1.Id));
            provider.Admin = false; Assert.Equal(HttpStatusCode.Forbidden, await Link(creator, c1.Id)); Assert.Equal(HttpStatusCode.Forbidden, await Link(manager, c2.Id));
            provider.Admin = true; Assert.Equal(HttpStatusCode.Forbidden, await Link(creator, c1.Id, installation: 999));
            provider.HasRepository = false; Assert.Equal(HttpStatusCode.Forbidden, await Link(creator, c1.Id)); provider.HasRepository = true;
            provider.Suspended = true; Assert.Equal(HttpStatusCode.Forbidden, await Link(creator, c1.Id)); provider.Suspended = false;
            Assert.False(await db.ProjectGitRepositories.AnyAsync(x => x.ProjectId == project.Id));
            Assert.False(await db.Set<GitHubAppRepositoryGrant>().AnyAsync());
            var installation = new GitHubAppInstallation { ProviderInstallationId = 42, OwnerAccountId = 77, OwnerLogin = "org", OwnerType = "Organization", VerificationUnknown = true };
            db.Add(installation); await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.Conflict, await Link(creator, c1.Id));
            installation.VerificationUnknown = false; var revoked = new GitHubAppRepositoryGrant { InstallationId = installation.Id, ProviderRepositoryId = 99, VerifiedAt = now, RevokedAt = now };
            db.Add(revoked); await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.Conflict, await Link(creator, c1.Id));
            Assert.False(await db.ProjectGitRepositories.AnyAsync(x => x.ProjectId == project.Id));
            revoked.RevokedAt = null; await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.OK, await Link(creator, c1.Id)); Assert.Equal(HttpStatusCode.OK, await Link(creator, c1.Id));
            provider.Repo = 100; Assert.Equal(HttpStatusCode.OK, await Link(manager, c2.Id, repo: 100));
            db.ChangeTracker.Clear(); var links = await db.ProjectGitRepositories.Where(x => x.ProjectId == project.Id).ToListAsync(); Assert.Equal(2, links.Count);
            Assert.All(links, l => { Assert.Equal(GitRepositoryCredentialSource.GitHubApp, l.CredentialSource); Assert.Null(l.OAuthConnectionId); Assert.NotNull(l.GitHubAppRepositoryGrantId); });
            var grantIds = links.Select(l => l.GitHubAppRepositoryGrantId!.Value).ToArray();
            var persistedGrants = await db.Set<GitHubAppRepositoryGrant>().Where(g => grantIds.Contains(g.Id)).OrderBy(g => g.ProviderRepositoryId).ToListAsync();
            Assert.Equal(new long[] { 99, 100 }, persistedGrants.Select(g => g.ProviderRepositoryId));
            Assert.All(persistedGrants, g => Assert.Equal(installation.Id, g.InstallationId));
            Assert.Equal(2, await db.GitSyncJobs.CountAsync(x => links.Select(l => l.Id).Contains(x.ProjectGitRepositoryId) && x.SyncType == GitSyncType.Initial));
        }
        finally { await host.StopAsync(); }
    }
}
