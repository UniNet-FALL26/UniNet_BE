using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniNet.Application.Common;
using UniNet.Application.DTOs;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class GitHubAppAuthorizationTests
{
    private sealed class Protector : IGitHubTokenProtector
    {
        public string Protect(string token) => "encrypted:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(token));
        public string Unprotect(string value) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value[10..]));
    }
    private sealed class Provider : IGitHubAppUserClient
    {
        public long UserId = 123;
        public bool FailExchange;
        public bool FailUser;
        public TaskCompletionSource? Started;
        public TaskCompletionSource? Resume;
        public async Task<GitHubAppUserToken> ExchangeAsync(string code, CancellationToken ct)
        {
            Started?.TrySetResult();
            if (Resume is not null) await Resume.Task.WaitAsync(ct);
            if (FailExchange) throw new GitHubIntegrationException("PROVIDER", "failure", 502);
            return new("secret-access", "secret-refresh", 3600, 7200);
        }
        public Task<GitHubAppUserIdentity> GetUserAsync(string token, CancellationToken ct) => FailUser
            ? throw new GitHubIntegrationException("PROVIDER", "failure", 502) : Task.FromResult(new GitHubAppUserIdentity(UserId, "test-user"));
        public Task<GitHubAppUserPage<GitHubAppUserInstallation>> GetInstallationsAsync(string token, int page, CancellationToken ct) => throw new NotImplementedException();
        public Task<GitHubAppUserPage<GitHubAppUserRepository>> GetRepositoriesAsync(string token, long id, int page, CancellationToken ct) => throw new NotImplementedException();
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public SqliteConnection Connection = new("Data Source=:memory:");
        public UniNetDbContext Db = null!;
        public Account Owner = null!;
        public Project Project = null!;
        public GitHubOAuthConnection OAuth = null!;
        public Provider Provider = new();
        public GitHubAppOptions Options = new() { Enabled = true, ClientId = "test-client", ClientSecret = "test-secret", CallbackUrl = "https://api.example.test/api/github/app/authorization/callback", ReturnTargets = new() { ["web"] = "https://frontend.example.test/complete" } };
        public GitHubAppAuthorizationService Service(UniNetDbContext? db = null) => new(db ?? Db, Provider, new Protector(), Microsoft.Extensions.Options.Options.Create(Options));
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); await f.Connection.OpenAsync();
            f.Db = f.Open(); await f.Db.Database.EnsureCreatedAsync();
            f.Owner = new Account { Email = "app-auth@example.test", Role = AccountRole.Student, Status = AccountStatus.Active,
                Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
            f.Project = new Project { Creator = f.Owner.Profile, Title = "Project", ProjectField = "Engineering", Description = "Test", MemberTarget = 5, MinimumQuantity = 1,
                RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Visibility = ProjectVisibility.Public, Status = ProjectStatus.Active, RecruitmentStatus = RecruitmentStatus.Open };
            f.OAuth = new GitHubOAuthConnection { AccountId = f.Owner.Id, ProviderAccountId = "123", Login = "test-user", AccessTokenEncrypted = "encrypted", GrantedScopes = "repo" };
            f.Db.AddRange(f.Owner, f.Project, f.OAuth); await f.Db.SaveChangesAsync(); return f;
        }
        public UniNetDbContext Open() => new(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(Connection).Options);
        public async Task<(Guid Id, string State)> Start() { var result = await Service().StartAsync(Owner.Id, Project.Id, new(OAuth.Id, "web"), default); return (result.FlowId, QueryHelpers.ParseQuery(new Uri(result.AuthorizationUrl).Query)["state"].ToString()); }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }

    [Fact]
    public async Task ValidCallbackEncryptsCredentialsAndReplayIsRejected()
    {
        await using var f = await Fixture.Create(); var flow = await f.Start();
        var stored = await f.Db.Set<GitHubAppAuthorizationFlow>().SingleAsync(); Assert.DoesNotContain(flow.State, stored.StateHash);
        var result = await f.Service().CallbackAsync(flow.State, "code", null, default);
        Assert.Contains("githubAppStatus=completed", result.RedirectUri.ToString()); Assert.DoesNotContain("secret-access", result.RedirectUri.ToString());
        var credential = await f.Db.Set<GitHubAppUserCredential>().SingleAsync();
        Assert.DoesNotContain("secret-access", credential.AccessTokenEncrypted); Assert.Equal("secret-access", new Protector().Unprotect(credential.AccessTokenEncrypted));
        Assert.NotNull(credential.AccessTokenExpiresAt); Assert.NotNull(credential.RefreshTokenExpiresAt);
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => f.Service().CallbackAsync(flow.State, "code", null, default));
    }

    [Theory]
    [InlineData("exchange")]
    [InlineData("identity")]
    [InlineData("mismatch")]
    public async Task ProviderFailureOrIdentityMismatchFailsWithoutSavingCredentials(string mode)
    {
        await using var f = await Fixture.Create(); var flow = await f.Start();
        f.Provider.FailExchange = mode == "exchange"; f.Provider.FailUser = mode == "identity"; if (mode == "mismatch") f.Provider.UserId = 999;
        var result = await f.Service().CallbackAsync(flow.State, "code", null, default);
        Assert.Contains("githubAppStatus=failed", result.RedirectUri.ToString());
        Assert.Empty(await f.Db.Set<GitHubAppUserCredential>().ToListAsync());
        Assert.Equal(GitHubAppAuthorizationStatus.Failed, (await f.Db.Set<GitHubAppAuthorizationFlow>().SingleAsync()).Status);
    }

    [Fact]
    public async Task InvalidExpiredAndUnauthorizedRequestsAreRejected()
    {
        await using var f = await Fixture.Create(); var flow = await f.Start();
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => f.Service().CallbackAsync("invalid", "code", null, default));
        var record = await f.Db.Set<GitHubAppAuthorizationFlow>().SingleAsync(); record.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => f.Service().CallbackAsync(flow.State, "code", null, default));
        f.Options.ReturnTargets["web"] = "https://evil.example.test/#fragment";
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => f.Service().StartAsync(f.Owner.Id, f.Project.Id, new(f.OAuth.Id, "web"), default));
        var outsider = new Account { Email = "outsider@example.test", Role = AccountRole.Student, Profile = new UserProfile { FullName = "Other", Nickname = "Other" } };
        f.Db.Add(outsider); await f.Db.SaveChangesAsync();
        f.Options.ReturnTargets["web"] = "https://frontend.example.test/complete";
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => f.Service().StartAsync(outsider.Id, f.Project.Id, new(f.OAuth.Id, "web"), default));
    }

    [Fact]
    public async Task ConcurrentCallbackClaimsStateOnce()
    {
        await using var f = await Fixture.Create(); var flow = await f.Start();
        f.Provider.Started = new(); f.Provider.Resume = new();
        await using var firstDb = f.Open(); await using var secondDb = f.Open();
        var first = f.Service(firstDb).CallbackAsync(flow.State, "code", null, default);
        await f.Provider.Started.Task;
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => f.Service(secondDb).CallbackAsync(flow.State, "code", null, default));
        f.Provider.Resume.SetResult();
        Assert.Contains("githubAppStatus=completed", (await first).RedirectUri.ToString());
    }
}
