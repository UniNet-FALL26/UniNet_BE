using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;
using System.Net;
using UniNet.API.Services;

namespace UniNet.Tests;

public sealed class Step3aPostgresFactAttribute : FactAttribute
{
    public Step3aPostgresFactAttribute() { if (Environment.GetEnvironmentVariable("UNINET_STEP3A_DISPOSABLE") != "1") Skip = "Requires the dedicated disposable Step 3A PostgreSQL database."; }
}

public sealed class GitHubAppActivationPostgresTests
{
    private sealed class InstallationHttp(long repoId, HttpStatusCode branchesStatus) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.Contains("/branches", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(branchesStatus) { Content = new StringContent("[]") });
            var json = System.Text.Json.JsonSerializer.Serialize(new { id = repoId, name = "repo", full_name = "example/repo", html_url = "https://github.com/example/repo", @private = true, archived = false, owner = new { id = 42, login = "example" }, permissions = new { pull = false } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
    private static string Database => GitHubAppDisposableDatabase.Name("uninet_step3a_activation");
    private static string Conn => $"Host=127.0.0.1;Port=55433;Database={Database};Username=postgres;Timeout=5";
    private static UniNetDbContext Open() => new(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(Conn).Options);

    [Step3aPostgresFact]
    public async Task PrivateInstallationReadProbeActivatesAndEnqueuesExactlyOneInitialJob()
    {
        await using var db = Open(); await db.Database.EnsureCreatedAsync();
        var owner = new Account { Email = $"probe-{Guid.NewGuid():N}@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
        var project = new Project { Creator = owner.Profile!, Title = "Probe", ProjectField = "IT", Description = "Disposable", MemberTarget = 2, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Status = ProjectStatus.Active };
        var installationId = Random.Shared.NextInt64(1000000, 2000000000); var repositoryId = Random.Shared.NextInt64(1000000, 2000000000);
        var installation = new GitHubAppInstallation { ProviderInstallationId = installationId, OwnerAccountId = 42, OwnerLogin = "example", OwnerType = "User" };
        var repo = new GitHubRepository { ProviderRepositoryId = repositoryId, ProviderOwnerId = 42, OwnerLogin = "example", Name = "repo", FullName = "example/repo", HtmlUrl = "https://github.com/example/repo", IsPrivate = true };
        var grant = new GitHubAppRepositoryGrant { InstallationId = installation.Id, ProviderRepositoryId = repositoryId, VerifiedAt = DateTimeOffset.UtcNow };
        var link = new ProjectGitRepository { ProjectId = project.Id, GitHubRepositoryId = repo.Id, LinkedByProfileId = owner.Profile!.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncPaused = true, AccessStatus = ProjectGitRepositoryAccessStatus.PendingAuthorization };
        db.AddRange(owner, project, installation, repo, grant, link); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        using var http = new HttpClient(new InstallationHttp(repositoryId, HttpStatusCode.OK));
        var provider = new GitHubProviderClient(http, Options.Create(new GitHubIntegrationOptions()));
        var service = new GitHubAppRepositoryActivationService(db, new Tokens(installationId, repositoryId), provider, Options.Create(new GitSyncOptions { Enabled = true }), Options.Create(new GitHubAppOptions { Enabled = true }));
        Assert.True(await service.ActivateAsync(link.Id, default)); db.ChangeTracker.Clear();
        Assert.True(await service.ActivateAsync(link.Id, default)); db.ChangeTracker.Clear();
        Assert.Equal(ProjectGitRepositoryAccessStatus.Active, (await db.ProjectGitRepositories.SingleAsync(x => x.Id == link.Id)).AccessStatus);
        var job = Assert.Single(await db.GitSyncJobs.Where(x => x.ProjectGitRepositoryId == link.Id).ToListAsync());
        Assert.Equal(GitSyncType.Initial, job.SyncType); Assert.Equal(GitSyncJobStatus.Pending, job.Status);
        Assert.Equal(grant.Id, job.GitHubAppRepositoryGrantId); Assert.Null(job.OAuthConnectionId);
    }
    private sealed class Tokens(long expectedInstallation, long expectedRepo) : IGitHubAppInstallationTokenProvider
    {
        public Task<string> GetAsync(long installation, long repo, CancellationToken ct) { Assert.Equal(expectedInstallation, installation); Assert.Equal(expectedRepo, repo); return Task.FromResult("installation-token"); }
        public void InvalidateInstallation(long id) { }
    }
    private sealed class Provider : IGitHubProviderClient
    {
        private int count; private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<GitHubRemoteRepository> GetRepositoryAsync(string token, long id, CancellationToken ct)
        {
            Assert.Equal("installation-token", token);
            if (Interlocked.Increment(ref count) == 2) gate.TrySetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            return new(id, 42, "example", "repo", "example/repo", "https://github.com/example/repo", null, "main", true, false, true);
        }
        public Task<GitHubOAuthTokens> ExchangeCodeAsync(string code, CancellationToken ct) => throw new NotImplementedException();
        public Task<GitHubOAuthTokens> RefreshAsync(string refreshToken, CancellationToken ct) => throw new NotImplementedException();
        public Task<GitHubUser> GetAuthenticatedUserAsync(string token, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<GitHubRemoteRepository>> GetRepositoriesAsync(string token, int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
        public Task<GitHubSyncPage> GetSyncPageAsync(string token, string repo, string category, string? scope, int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
    }

    [Step3aPostgresFact]
    public async Task ConcurrentActivationCreatesOneInitialAppJob()
    {
        await using (var check = new NpgsqlConnection(Conn)) { await check.OpenAsync(); Assert.Equal(Database, check.Database); Assert.Equal("127.0.0.1", check.Host); }
        await using var db = Open(); await db.Database.EnsureCreatedAsync();
        var owner = new Account { Email = $"step3a-{Guid.NewGuid():N}@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
        var project = new Project { Creator = owner.Profile!, Title = "Activation", ProjectField = "Engineering", Description = "Disposable", MemberTarget = 5, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Visibility = ProjectVisibility.Public, Status = ProjectStatus.Active, RecruitmentStatus = RecruitmentStatus.Open };
        var installationId = Random.Shared.NextInt64(1000000, 2000000000);
        var repositoryId = Random.Shared.NextInt64(1000000, 2000000000);
        var installation = new GitHubAppInstallation { ProviderInstallationId = installationId, OwnerAccountId = 42, OwnerLogin = "example", OwnerType = "User" };
        var repo = new GitHubRepository { ProviderRepositoryId = repositoryId, ProviderOwnerId = 42, OwnerLogin = "example", Name = "repo", FullName = "example/repo", HtmlUrl = "https://github.com/example/repo" };
        var grant = new GitHubAppRepositoryGrant { InstallationId = installation.Id, ProviderRepositoryId = repositoryId, VerifiedAt = DateTimeOffset.UtcNow };
        var link = new ProjectGitRepository { ProjectId = project.Id, GitHubRepositoryId = repo.Id, LinkedByProfileId = owner.Profile!.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncPaused = true, AccessStatus = ProjectGitRepositoryAccessStatus.PendingAuthorization };
        db.AddRange(owner, project, installation, repo, grant, link); await db.SaveChangesAsync();
        var provider = new Provider();
        async Task<string> Activate()
        {
            await using var context = Open();
            try { return await new GitHubAppRepositoryActivationService(context, new Tokens(installationId, repositoryId), provider, Options.Create(new GitSyncOptions { Enabled = true }), Options.Create(new GitHubAppOptions { Enabled = true })).ActivateAsync(link.Id, default) ? "active" : "pending"; }
            catch (GitHubIntegrationException ex) { return ex.Code; }
        }
        var results = await Task.WhenAll(Activate(), Activate());
        Assert.Contains("active", results);
        Assert.All(results, x => Assert.Contains(x, new[] { "active", "GITHUB_APP_ACTIVATION_CONFLICT" }));
        await using var verify = Open();
        var jobs = await verify.GitSyncJobs.Where(x => x.ProjectGitRepositoryId == link.Id).ToListAsync();
        var job = Assert.Single(jobs); Assert.Equal(GitRepositoryCredentialSource.GitHubApp, job.CredentialSource);
        Assert.Equal(grant.Id, job.GitHubAppRepositoryGrantId); Assert.Null(job.OAuthConnectionId);
        Assert.Equal(GitSyncType.Initial, job.SyncType); Assert.Equal(GitSyncJobStatus.Pending, job.Status);
    }
}
