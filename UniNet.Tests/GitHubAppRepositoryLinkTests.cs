using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniNet.API.Controllers;
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

public sealed class GitHubAppRepositoryLinkTests
{
    private sealed class Protector : IGitHubTokenProtector
    {
        public string Protect(string token) => "encrypted:" + token;
        public string Unprotect(string token) => token[10..];
    }
    private sealed class InstallationTokens : IGitHubAppInstallationTokenProvider
    {
        public bool Fail;
        public Task<string> GetAsync(long installationId, long repositoryId, CancellationToken ct)
        {
            Assert.Equal(42, installationId); Assert.Equal(99, repositoryId);
            if (Fail) throw new GitHubIntegrationException("GITHUB_APP_NOT_CONFIGURED", "Unavailable", 503);
            return Task.FromResult("installation-token");
        }
        public void InvalidateInstallation(long installationId) { }
    }
    private sealed class SyncProvider : IGitHubProviderClient
    {
        public Task<GitHubRemoteRepository> GetRepositoryAsync(string token, long id, CancellationToken ct)
        {
            Assert.Equal("installation-token", token);
            return Task.FromResult(new GitHubRemoteRepository(id, 77, "org", "repo", "org/repo", "https://github.com/org/repo", null, "main", true, false, true));
        }
        public Task<GitHubOAuthTokens> ExchangeCodeAsync(string code, CancellationToken ct) => throw new NotImplementedException();
        public Task<GitHubOAuthTokens> RefreshAsync(string refreshToken, CancellationToken ct) => throw new NotImplementedException();
        public Task<GitHubUser> GetAuthenticatedUserAsync(string accessToken, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<GitHubRemoteRepository>> GetRepositoriesAsync(string accessToken, int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
        public Task<GitHubSyncPage> GetSyncPageAsync(string token, string fullName, string category, string? scope, int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
    }
    private sealed class Provider : IGitHubAppUserClient
    {
        public long UserId = 123;
        public bool Suspended, DenyAdmin, MissingRepo, Fail;
        public Task<GitHubAppUserToken> ExchangeAsync(string code, CancellationToken ct) => throw new NotImplementedException();
        public Task<GitHubAppUserIdentity> GetUserAsync(string token, CancellationToken ct) => Fail ? throw new GitHubIntegrationException("GITHUB_APP_PROVIDER_ERROR", "Unavailable", 429) : Task.FromResult(new GitHubAppUserIdentity(UserId, "user"));
        public Task<GitHubAppUserPage<GitHubAppUserInstallation>> GetInstallationsAsync(string token, int page, CancellationToken ct) => Task.FromResult(new GitHubAppUserPage<GitHubAppUserInstallation>([new(42, 77, "org", "Organization", Suspended)], false));
        public Task<GitHubAppUserPage<GitHubAppUserRepository>> GetRepositoriesAsync(string token, long installation, int page, CancellationToken ct) => Task.FromResult(new GitHubAppUserPage<GitHubAppUserRepository>(MissingRepo ? [] : [new(99, "org/repo", true, true, !DenyAdmin, 77, "org", "repo", "https://github.com/org/repo")], false));
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public SqliteConnection Connection = new("Data Source=:memory:");
        public UniNetDbContext Db = null!;
        public Account Owner = null!; public Project Project = null!; public GitHubAppUserCredential Credential = null!;
        public Provider Provider = new();
        public GitHubAppRepositoryLinkService Service => new(Db, new Protector(), new GitHubAppUserGrantVerifier(Provider, Microsoft.Extensions.Options.Options.Create(new GitHubAppOptions { Enabled = true })), appOptions: Options.Create(new GitHubAppOptions { Enabled = true }));
        public LinkGitHubAppRepositoryRequest Request => new(Credential.Id, 42, 99);
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); await f.Connection.OpenAsync(); f.Db = new(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(f.Connection).Options); await f.Db.Database.EnsureCreatedAsync();
            f.Owner = new() { Email = "creator@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
            f.Project = new() { Creator = f.Owner.Profile, Title = "Project", ProjectField = "Engineering", Description = "Test", MemberTarget = 5, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Visibility = ProjectVisibility.Public, Status = ProjectStatus.Active, RecruitmentStatus = RecruitmentStatus.Open };
            f.Credential = new() { AccountId = f.Owner.Id, GitHubUserId = 123, Login = "user", AccessTokenEncrypted = "encrypted:token", AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
            f.Db.AddRange(f.Owner, f.Project, f.Credential); await f.Db.SaveChangesAsync(); return f;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }

    [Fact]
    public async Task LinksVerifiedAppRepositoryOnceAndCreatesNoJob()
    {
        await using var f = await Fixture.Create();
        var first = await f.Service.LinkAsync(f.Owner.Id, f.Project.Id, f.Request, default);
        var second = await f.Service.LinkAsync(f.Owner.Id, f.Project.Id, f.Request, default);
        Assert.False(first.WasAlreadyLinked); Assert.True(second.WasAlreadyLinked); Assert.Equal(first.ProjectGitRepositoryId, second.ProjectGitRepositoryId);
        var link = await f.Db.ProjectGitRepositories.SingleAsync(); var grant = await f.Db.Set<GitHubAppRepositoryGrant>().SingleAsync();
        Assert.Equal(grant.Id, link.GitHubAppRepositoryGrantId); Assert.Equal(99, grant.ProviderRepositoryId);
        Assert.Null(link.OAuthConnectionId); Assert.True(link.SyncPaused); Assert.Equal("PendingActivation", first.Readiness);
        Assert.Equal(GitRepositoryCredentialSource.GitHubApp, link.CredentialSource); Assert.Empty(await f.Db.GitSyncJobs.ToListAsync());
    }

    [Fact]
    public async Task UnknownInstallationDefersLinkEvenWhenUserVerificationSucceeds()
    {
        await using var f = await Fixture.Create();
        f.Db.Add(new GitHubAppInstallation { ProviderInstallationId = 42, OwnerAccountId = 77, OwnerLogin = "org", OwnerType = "Organization",
            Status = GitHubAppInstallationStatus.Active, VerificationUnknown = true, NextReconciliationAt = DateTimeOffset.UtcNow.AddHours(1) });
        await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => f.Service.LinkAsync(f.Owner.Id, f.Project.Id, f.Request, default));
        Assert.Equal("GITHUB_APP_VERIFICATION_PENDING", error.Code);
        Assert.Empty(await f.Db.ProjectGitRepositories.ToListAsync()); Assert.Empty(await f.Db.GitSyncJobs.ToListAsync());
    }

    [Fact]
    public async Task LinkAutomaticallyActivatesAndFailedActivationCanBeRetried()
    {
        await using var f = await Fixture.Create();
        var tokens = new InstallationTokens { Fail = true };
        var activation = new GitHubAppRepositoryActivationService(f.Db, tokens, new SyncProvider(), Options.Create(new GitSyncOptions { Enabled = true }), Options.Create(new GitHubAppOptions { Enabled = true }));
        var service = new GitHubAppRepositoryLinkService(f.Db, new Protector(), new GitHubAppUserGrantVerifier(f.Provider, Microsoft.Extensions.Options.Options.Create(new GitHubAppOptions { Enabled = true })), activation, Options.Create(new GitHubAppOptions { Enabled = true }));
        var link = await service.LinkAsync(f.Owner.Id, f.Project.Id, f.Request, default);
        Assert.Equal("PendingActivation", link.Readiness); Assert.Empty(await f.Db.GitSyncJobs.ToListAsync());
        tokens.Fail = false;
        await activation.RetryPendingAsync(default);
        var job = Assert.Single(await f.Db.GitSyncJobs.ToListAsync());
        Assert.Equal(GitRepositoryCredentialSource.GitHubApp, job.CredentialSource); Assert.Null(job.OAuthConnectionId);
        Assert.Equal(link.ProjectGitRepositoryId, job.ProjectGitRepositoryId);
        Assert.Equal(GitSyncType.Initial, job.SyncType); Assert.Equal(GitSyncJobStatus.Pending, job.Status);
        var again = await service.LinkAsync(f.Owner.Id, f.Project.Id, f.Request, default);
        Assert.True(again.WasAlreadyLinked); Assert.Equal("PendingWorker", again.Readiness);
        Assert.Single(await f.Db.GitSyncJobs.ToListAsync());
    }

    [Theory]
    [InlineData("user", "GITHUB_APP_USER_MISMATCH")]
    [InlineData("installation", "GITHUB_APP_INSTALLATION_UNAVAILABLE")]
    [InlineData("suspended", "GITHUB_APP_INSTALLATION_UNAVAILABLE")]
    [InlineData("repository", "GITHUB_APP_REPOSITORY_UNAVAILABLE")]
    [InlineData("admin", "GITHUB_APP_REPOSITORY_ADMIN_REQUIRED")]
    [InlineData("provider", "GITHUB_APP_PROVIDER_ERROR")]
    [InlineData("credential", "GITHUB_APP_AUTH_REQUIRED")]
    public async Task InvalidProofDoesNotCreateLinks(string mode, string code)
    {
        await using var f = await Fixture.Create();
        if (mode == "user") f.Provider.UserId = 456; if (mode == "suspended") f.Provider.Suspended = true;
        if (mode == "repository") f.Provider.MissingRepo = true; if (mode == "admin") f.Provider.DenyAdmin = true; if (mode == "provider") f.Provider.Fail = true;
        var request = f.Request with { InstallationId = mode == "installation" ? 43 : 42, CredentialId = mode == "credential" ? Guid.NewGuid() : f.Credential.Id };
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => f.Service.LinkAsync(f.Owner.Id, f.Project.Id, request, default));
        Assert.Equal(code, error.Code); Assert.Empty(await f.Db.ProjectGitRepositories.ToListAsync()); Assert.Empty(await f.Db.GitSyncJobs.ToListAsync());
    }

    [Fact]
    public async Task OtherStudentIsForbiddenAndEndpointRequiresStudent()
    {
        await using var f = await Fixture.Create(); var other = new Account { Email = "other@example.test", Role = AccountRole.Student, Profile = new UserProfile { FullName = "Other", Nickname = "Other" } };
        f.Db.Add(other); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => f.Service.LinkAsync(other.Id, f.Project.Id, f.Request, default));
        var method = typeof(GitHubAppAuthorizationController).GetMethod("Link")!;
        Assert.Equal("Student", method.GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>().Single().Roles);
        Assert.NotNull(method.GetCustomAttributes(typeof(HttpPostAttribute), false).SingleOrDefault());
    }

    [Fact]
    public async Task ExistingOAuthLinkIsNotOverwritten()
    {
        await using var f = await Fixture.Create();
        var oauth = new GitHubOAuthConnection { AccountId = f.Owner.Id, ProviderAccountId = "123", Login = "user", AccessTokenEncrypted = "encrypted:oauth", GrantedScopes = "repo" };
        var repo = new GitHubRepository { ProviderRepositoryId = 99, ProviderOwnerId = 77, OwnerLogin = "org", Name = "repo", FullName = "org/repo", HtmlUrl = "https://github.com/org/repo" };
        var existing = new ProjectGitRepository { ProjectId = f.Project.Id, GitHubRepositoryId = repo.Id, LinkedByProfileId = f.Owner.Profile!.Id, OAuthConnectionId = oauth.Id };
        f.Db.AddRange(oauth, repo, existing); await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => f.Service.LinkAsync(f.Owner.Id, f.Project.Id, f.Request, default));
        Assert.Equal("GITHUB_REPOSITORY_ALREADY_LINKED", error.Code);
        f.Db.ChangeTracker.Clear();
        var persisted = await f.Db.ProjectGitRepositories.SingleAsync();
        Assert.Equal(GitRepositoryCredentialSource.OAuth, persisted.CredentialSource); Assert.Equal(oauth.Id, persisted.OAuthConnectionId);
        Assert.Empty(await f.Db.GitSyncJobs.ToListAsync());
    }

    [Fact]
    public async Task SameVerifiedRepositoryCanBeLinkedToTwoProjectsIndependently()
    {
        await using var f = await Fixture.Create();
        var other = new Project { Creator = f.Owner.Profile!, Title = "Other", ProjectField = "Engineering", Description = "Test", MemberTarget = 5, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Visibility = ProjectVisibility.Public, Status = ProjectStatus.Active, RecruitmentStatus = RecruitmentStatus.Open };
        f.Db.Add(other); await f.Db.SaveChangesAsync();
        var first = await f.Service.LinkAsync(f.Owner.Id, f.Project.Id, f.Request, default);
        var second = await f.Service.LinkAsync(f.Owner.Id, other.Id, f.Request, default);
        Assert.NotEqual(first.ProjectGitRepositoryId, second.ProjectGitRepositoryId);
        Assert.Equal(2, await f.Db.ProjectGitRepositories.CountAsync());
        Assert.Equal(1, await f.Db.GitHubRepositories.CountAsync());
        Assert.Empty(await f.Db.GitSyncJobs.ToListAsync());
    }

    [Fact]
    public async Task DatabaseUniqueConstraintRejectsSecondActiveLink()
    {
        await using var f = await Fixture.Create();
        await f.Service.LinkAsync(f.Owner.Id, f.Project.Id, f.Request, default);
        var first = await f.Db.ProjectGitRepositories.AsNoTracking().SingleAsync();
        f.Db.Add(new ProjectGitRepository { ProjectId = first.ProjectId, GitHubRepositoryId = first.GitHubRepositoryId,
            LinkedByProfileId = first.LinkedByProfileId, CredentialSource = GitRepositoryCredentialSource.GitHubApp,
            GitHubAppRepositoryGrantId = first.GitHubAppRepositoryGrantId, SyncPaused = true,
            AccessStatus = ProjectGitRepositoryAccessStatus.PendingAuthorization });
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task ExistingSuspendedLocalInstallationFailsClosedEvenIfProviderReportsActive()
    {
        await using var f = await Fixture.Create();
        f.Db.Add(new GitHubAppInstallation { ProviderInstallationId = 42, OwnerAccountId = 77, OwnerLogin = "org", OwnerType = "Organization", Status = GitHubAppInstallationStatus.Suspended });
        await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => f.Service.LinkAsync(f.Owner.Id, f.Project.Id, f.Request, default));
        Assert.Equal("GITHUB_APP_INSTALLATION_UNAVAILABLE", error.Code);
        Assert.Empty(await f.Db.ProjectGitRepositories.ToListAsync());
    }
}
