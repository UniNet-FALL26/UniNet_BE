using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class GitHubAppGrantServiceTests
{
    private sealed class Protector : IGitHubTokenProtector
    {
        public string Protect(string token) => "protected:" + token;
        public string Unprotect(string token) => token[10..];
    }
    private sealed class Provider : IGitHubAppUserClient
    {
        public long User = 123;
        public bool Suspended;
        public bool Read = true;
        public bool Admin = true;
        public bool Fail;
        public bool AdditionalRepository;
        public bool Paginated;
        public List<int> RepositoryPages = [];
        public Task<GitHubAppUserToken> ExchangeAsync(string code, CancellationToken ct) => throw new NotImplementedException();
        public Task<GitHubAppUserIdentity> GetUserAsync(string token, CancellationToken ct) => Fail ? throw new GitHubIntegrationException("FAILED", "GitHub failed", 502) : Task.FromResult(new GitHubAppUserIdentity(User, "user"));
        public Task<GitHubAppUserPage<GitHubAppUserInstallation>> GetInstallationsAsync(string token, int page, CancellationToken ct) => Task.FromResult(new GitHubAppUserPage<GitHubAppUserInstallation>([new(42, 100, "org", "Organization", Suspended, "https://github.com/organizations/org/settings/installations/42")], false));
        public Task<GitHubAppUserPage<GitHubAppUserRepository>> GetRepositoriesAsync(string token, long id, int page, CancellationToken ct)
        {
            RepositoryPages.Add(page);
            GitHubAppUserRepository first = new(999, "org/private-repo", true, Read, Admin);
            GitHubAppUserRepository second = new(1000, "org/new-selected-repo", true, Read, Admin);
            return Task.FromResult(new GitHubAppUserPage<GitHubAppUserRepository>(
                Paginated ? page == 1 ? [first] : [second] : AdditionalRepository ? [first, second] : [first], Paginated && page == 1));
        }
    }

    [Fact]
    public async Task ReadableRepositoryWithoutAdminIsNotEligibleForLinking()
    {
        await using var f = await Fixture.Create();
        f.Provider.Admin = false;
        var result = await f.Service.GetRepositoriesAsync(f.Owner.Id, f.Project.Id, f.Credential.Id, 42, 1, default);
        var item = Assert.Single(result.Items);
        Assert.False(item.Eligible);
        Assert.Contains("administrator", item.IneligibilityReason);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public SqliteConnection Conn = new("Data Source=:memory:");
        public UniNetDbContext Db = null!;
        public Account Owner = null!;
        public Project Project = null!;
        public GitHubAppUserCredential Credential = null!;
        public Provider Provider = new();
        public GitHubAppGrantService Service => new(Db, Provider, new Protector(), Microsoft.Extensions.Options.Options.Create(new GitHubAppOptions { Enabled = true }));
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); await f.Conn.OpenAsync();
            f.Db = new(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(f.Conn).Options); await f.Db.Database.EnsureCreatedAsync();
            f.Owner = new() { Email = "owner@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
            f.Project = new() { Creator = f.Owner.Profile, Title = "Project", ProjectField = "Engineering", Description = "Test", MemberTarget = 5, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Visibility = ProjectVisibility.Public, Status = ProjectStatus.Active, RecruitmentStatus = RecruitmentStatus.Open };
            f.Credential = new() { AccountId = f.Owner.Id, GitHubUserId = 123, Login = "user", AccessTokenEncrypted = "protected:test-token", AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
            f.Db.AddRange(f.Owner, f.Project, f.Credential); await f.Db.SaveChangesAsync(); return f;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Conn.DisposeAsync(); }
    }

    [Fact]
    public async Task OrganizationPrivateRepositoryIsVerifiedAndRepeatedQueriesDoNotDuplicateGrants()
    {
        await using var f = await Fixture.Create();
        var installations = await f.Service.GetInstallationsAsync(f.Owner.Id, f.Project.Id, f.Credential.Id, 1, default);
        Assert.Equal("Organization", Assert.Single(installations.Items).OwnerType);
        Assert.Equal("https://github.com/organizations/org/settings/installations/42", installations.Items[0].ConfigurationUrl);
        var result = await f.Service.GetRepositoriesAsync(f.Owner.Id, f.Project.Id, f.Credential.Id, 42, 1, default);
        Assert.True(Assert.Single(result.Items).Eligible); Assert.True(result.Items[0].IsPrivate);
        await f.Service.GetRepositoriesAsync(f.Owner.Id, f.Project.Id, f.Credential.Id, 42, 1, default);
        Assert.Single(await f.Db.Set<GitHubAppInstallation>().ToListAsync());
        Assert.Single(await f.Db.Set<GitHubAppInstallationAccount>().ToListAsync());
        Assert.Single(await f.Db.Set<GitHubAppRepositoryGrant>().ToListAsync());
    }

    [Fact]
    public async Task ExplicitRediscoveryReadsNewlyGrantedRepositoriesWithoutWorkerOrProjectLink()
    {
        await using var f = await Fixture.Create();
        Assert.Single((await f.Service.GetRepositoriesAsync(f.Owner.Id, f.Project.Id, f.Credential.Id, 42, 1, default)).Items);
        f.Provider.AdditionalRepository = true;
        var refreshed = await f.Service.GetRepositoriesAsync(f.Owner.Id, f.Project.Id, f.Credential.Id, 42, 1, default);
        Assert.Equal(2, refreshed.Items.Count);
        Assert.Contains(refreshed.Items, x => x.RepositoryId == 1000 && x.Eligible);
        Assert.Equal(new[] { 1, 1 }, f.Provider.RepositoryPages);
        Assert.Equal(2, await f.Db.Set<GitHubAppRepositoryGrant>().CountAsync());
        Assert.Empty(await f.Db.ProjectGitRepositories.ToListAsync());
        Assert.Empty(await f.Db.GitSyncJobs.ToListAsync());
    }

    [Fact]
    public async Task RepositoryDiscoveryPreservesProviderPagination()
    {
        await using var f = await Fixture.Create(); f.Provider.Paginated = true;
        var first = await f.Service.GetRepositoriesAsync(f.Owner.Id, f.Project.Id, f.Credential.Id, 42, 1, default);
        var second = await f.Service.GetRepositoriesAsync(f.Owner.Id, f.Project.Id, f.Credential.Id, 42, 2, default);
        Assert.True(first.HasMore); Assert.False(second.HasMore);
        Assert.Equal(999, Assert.Single(first.Items).RepositoryId);
        Assert.Equal(1000, Assert.Single(second.Items).RepositoryId);
        Assert.Equal(2, second.Page);
    }

    [Theory]
    [InlineData("mismatch", "GITHUB_APP_USER_MISMATCH")]
    [InlineData("forged", "GITHUB_APP_INSTALLATION_UNAVAILABLE")]
    [InlineData("suspended", "GITHUB_APP_INSTALLATION_UNAVAILABLE")]
    [InlineData("expired", "GITHUB_APP_REAUTHORIZATION_REQUIRED")]
    [InlineData("provider", "FAILED")]
    public async Task FailsClosedWhenVerificationFails(string mode, string code)
    {
        await using var f = await Fixture.Create();
        if (mode == "mismatch") f.Provider.User = 321;
        if (mode == "suspended") f.Provider.Suspended = true;
        if (mode == "provider") f.Provider.Fail = true;
        if (mode == "expired") { f.Credential.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); await f.Db.SaveChangesAsync(); }
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => f.Service.GetRepositoriesAsync(f.Owner.Id, f.Project.Id, f.Credential.Id, mode == "forged" ? 43 : 42, 1, default));
        Assert.Equal(code, error.Code); Assert.Empty(await f.Db.Set<GitHubAppRepositoryGrant>().ToListAsync());
    }

    [Fact]
    public async Task UnreadableRepositoryIsNotGrantedAndAnotherProjectIsRejected()
    {
        await using var f = await Fixture.Create(); f.Provider.Read = false;
        var result = await f.Service.GetRepositoriesAsync(f.Owner.Id, f.Project.Id, f.Credential.Id, 42, 1, default);
        Assert.False(Assert.Single(result.Items).Eligible); Assert.Empty(await f.Db.Set<GitHubAppRepositoryGrant>().ToListAsync());
        var other = new Account { Email = "other@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Other", Nickname = "Other" } };
        var project = new Project { Creator = other.Profile, Title = "Other", ProjectField = "Engineering", Description = "Test", MemberTarget = 5, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Visibility = ProjectVisibility.Public, Status = ProjectStatus.Active, RecruitmentStatus = RecruitmentStatus.Open };
        f.Db.AddRange(other, project); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => f.Service.GetInstallationsAsync(f.Owner.Id, project.Id, f.Credential.Id, 1, default));
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => f.Service.GetInstallationsAsync(other.Id, f.Project.Id, f.Credential.Id, 1, default));
    }
}
