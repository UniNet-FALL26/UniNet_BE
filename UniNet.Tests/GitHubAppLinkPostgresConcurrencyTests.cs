using Microsoft.EntityFrameworkCore;
using Npgsql;
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

public sealed class Step2PostgresFactAttribute : FactAttribute
{
    public Step2PostgresFactAttribute() { if (Environment.GetEnvironmentVariable("UNINET_STEP2_DISPOSABLE") != "1") Skip = "Requires the dedicated disposable Step 2 PostgreSQL container."; }
}

public sealed class GitHubAppLinkPostgresConcurrencyTests
{
    private static string Database => GitHubAppDisposableDatabase.Name("uninet_step2_concurrency");
    private static string ConnectionString => $"Host=127.0.0.1;Port=55433;Database={Database};Username=postgres;Timeout=5";
    private static UniNetDbContext Open() => new(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(ConnectionString).Options);
    private sealed class Protector : IGitHubTokenProtector { public string Protect(string value) => value; public string Unprotect(string value) => value; }
    private sealed class Provider(int parties) : IGitHubAppUserClient
    {
        private int arrived; private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<GitHubAppUserToken> ExchangeAsync(string code, CancellationToken ct) => throw new NotImplementedException();
        public Task<GitHubAppUserIdentity> GetUserAsync(string token, CancellationToken ct) => Task.FromResult(new GitHubAppUserIdentity(123, "user"));
        public Task<GitHubAppUserPage<GitHubAppUserInstallation>> GetInstallationsAsync(string token, int page, CancellationToken ct) => Task.FromResult(new GitHubAppUserPage<GitHubAppUserInstallation>([new(42, 77, "org", "Organization", false), new(43, 77, "org", "Organization", false)], false));
        public async Task<GitHubAppUserPage<GitHubAppUserRepository>> GetRepositoriesAsync(string token, long id, int page, CancellationToken ct)
        {
            if (Interlocked.Increment(ref arrived) == parties) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            return new([new(99, "org/repo", true, true, true, 77, "org", "repo", "https://github.com/org/repo")], false);
        }
    }

    private sealed class RevokingProvider(long installation, long repository) : IGitHubAppUserClient
    {
        public Task<GitHubAppUserToken> ExchangeAsync(string code, CancellationToken ct) => throw new NotImplementedException();
        public Task<GitHubAppUserIdentity> GetUserAsync(string token, CancellationToken ct) => Task.FromResult(new GitHubAppUserIdentity(123, "user"));
        public Task<GitHubAppUserPage<GitHubAppUserInstallation>> GetInstallationsAsync(string token, int page, CancellationToken ct) => Task.FromResult(new GitHubAppUserPage<GitHubAppUserInstallation>([new(installation, 77, "org", "Organization", false)], false));
        public async Task<GitHubAppUserPage<GitHubAppUserRepository>> GetRepositoriesAsync(string token, long id, int page, CancellationToken ct)
        {
            await using var other = Open();
            await other.Set<GitHubAppInstallation>().Where(x => x.ProviderInstallationId == installation)
                .ExecuteUpdateAsync(x => x.SetProperty(i => i.Status, GitHubAppInstallationStatus.Suspended), ct);
            return new([new(repository, "org/revocation-test", true, true, true, 77, "org", "revocation-test", "https://github.com/org/revocation-test")], false);
        }
    }

    [Step2PostgresFact]
    public async Task SimultaneousLinksAreUniqueAndRetryIsSafe()
    {
        await RunRaceAsync(2, sameInstallation: true);
        await RunRaceAsync(6, sameInstallation: false);
    }

    private static async Task RunRaceAsync(int count, bool sameInstallation)
    {
        await using (var check = new NpgsqlConnection(ConnectionString))
        {
            await check.OpenAsync();
            Assert.Equal(Database, check.Database);
            Assert.Equal("127.0.0.1", check.Host);
        }
        await using var setup = Open(); await setup.Database.EnsureCreatedAsync();
        var owner = new Account { Email = $"step2-{Guid.NewGuid():N}@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
        var project = new Project { Creator = owner.Profile!, Title = "Concurrency", ProjectField = "Engineering", Description = "Disposable test", MemberTarget = 5, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Visibility = ProjectVisibility.Public, Status = ProjectStatus.Active, RecruitmentStatus = RecruitmentStatus.Open };
        var credential = new GitHubAppUserCredential { AccountId = owner.Id, GitHubUserId = 123, Login = "user", AccessTokenEncrypted = "fake", AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
        setup.AddRange(owner, project, credential); await setup.SaveChangesAsync();
        var provider = new Provider(count);
        async Task<string> Attempt(int index)
        {
            await using var db = Open();
            var service = new GitHubAppRepositoryLinkService(db, new Protector(), new GitHubAppUserGrantVerifier(provider, Microsoft.Extensions.Options.Options.Create(new GitHubAppOptions { Enabled = true })), appOptions: Microsoft.Extensions.Options.Options.Create(new GitHubAppOptions { Enabled = true }));
            try { var result = await service.LinkAsync(owner.Id, project.Id, new(credential.Id, sameInstallation || index % 2 == 0 ? 42 : 43, 99), default); return result.WasAlreadyLinked ? "existing" : "created"; }
            catch (GitHubIntegrationException ex) { return ex.Code; }
        }
        var outcomes = await Task.WhenAll(Enumerable.Range(0, count).Select(Attempt));
        Assert.Single(outcomes, x => x == "created");
        Assert.All(outcomes, x => Assert.Contains(x, new[] { "created", "existing", "GITHUB_APP_LINK_CONFLICT", "GITHUB_REPOSITORY_ALREADY_LINKED" }));
        await using var verify = Open();
        var links = await verify.ProjectGitRepositories.Where(x => x.ProjectId == project.Id && x.UnlinkedAt == null).ToListAsync();
        Assert.Single(links); Assert.True(links[0].SyncPaused); Assert.Equal(GitRepositoryCredentialSource.GitHubApp, links[0].CredentialSource);
        Assert.Equal(0, await verify.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == links[0].Id));
        var winningGrant = await verify.Set<GitHubAppRepositoryGrant>().SingleAsync(x => x.Id == links[0].GitHubAppRepositoryGrantId);
        var winner = await verify.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == winningGrant.InstallationId);
        var retry = await new GitHubAppRepositoryLinkService(verify, new Protector(), new GitHubAppUserGrantVerifier(new Provider(1), Microsoft.Extensions.Options.Options.Create(new GitHubAppOptions { Enabled = true })), appOptions: Microsoft.Extensions.Options.Options.Create(new GitHubAppOptions { Enabled = true }))
            .LinkAsync(owner.Id, project.Id, new(credential.Id, winner.ProviderInstallationId, 99), default);
        Assert.True(retry.WasAlreadyLinked);
    }

    [Step2PostgresFact]
    public async Task ExistingOAuthLinkCannotBeOverwritten()
    {
        await using var db = Open(); await db.Database.EnsureCreatedAsync();
        var owner = new Account { Email = $"step2-oauth-{Guid.NewGuid():N}@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
        var project = new Project { Creator = owner.Profile!, Title = "OAuth isolation", ProjectField = "Engineering", Description = "Disposable test", MemberTarget = 5, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Visibility = ProjectVisibility.Public, Status = ProjectStatus.Active, RecruitmentStatus = RecruitmentStatus.Open };
        var app = new GitHubAppUserCredential { AccountId = owner.Id, GitHubUserId = 123, Login = "user", AccessTokenEncrypted = "fake", AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
        var oauth = new GitHubOAuthConnection { AccountId = owner.Id, ProviderAccountId = "123", Login = "user", AccessTokenEncrypted = "fake", GrantedScopes = "repo" };
        var repository = await db.GitHubRepositories.SingleOrDefaultAsync(x => x.ProviderRepositoryId == 99);
        if (repository is null) { repository = new() { ProviderRepositoryId = 99, ProviderOwnerId = 77, OwnerLogin = "org", Name = "repo", FullName = "org/repo", HtmlUrl = "https://github.com/org/repo" }; db.Add(repository); }
        var link = new ProjectGitRepository { ProjectId = project.Id, GitHubRepositoryId = repository.Id, LinkedByProfileId = owner.Profile!.Id, OAuthConnectionId = oauth.Id };
        db.AddRange(owner, project, app, oauth, link); await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => new GitHubAppRepositoryLinkService(db, new Protector(), new GitHubAppUserGrantVerifier(new Provider(1), Microsoft.Extensions.Options.Options.Create(new GitHubAppOptions { Enabled = true })), appOptions: Microsoft.Extensions.Options.Options.Create(new GitHubAppOptions { Enabled = true })).LinkAsync(owner.Id, project.Id, new(app.Id, 42, 99), default));
        Assert.Equal("GITHUB_REPOSITORY_ALREADY_LINKED", error.Code);
        db.ChangeTracker.Clear();
        var persisted = await db.ProjectGitRepositories.SingleAsync(x => x.Id == link.Id);
        Assert.Equal(GitRepositoryCredentialSource.OAuth, persisted.CredentialSource); Assert.Equal(oauth.Id, persisted.OAuthConnectionId);
        Assert.Equal(0, await db.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == link.Id));
    }

    [Step2PostgresFact]
    public async Task LocallyRecordedRevocationBeforePersistenceIsRejected()
    {
        await using var db = Open(); await db.Database.EnsureCreatedAsync();
        var installationId = Random.Shared.NextInt64(1000000, 2000000000);
        var repositoryId = Random.Shared.NextInt64(1000000, 2000000000);
        var owner = new Account { Email = $"step2-revoke-{Guid.NewGuid():N}@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
        var project = new Project { Creator = owner.Profile!, Title = "Revocation", ProjectField = "Engineering", Description = "Disposable test", MemberTarget = 5, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Visibility = ProjectVisibility.Public, Status = ProjectStatus.Active, RecruitmentStatus = RecruitmentStatus.Open };
        var credential = new GitHubAppUserCredential { AccountId = owner.Id, GitHubUserId = 123, Login = "user", AccessTokenEncrypted = "fake", AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
        var installation = new GitHubAppInstallation { ProviderInstallationId = installationId, OwnerAccountId = 77, OwnerLogin = "org", OwnerType = "Organization", Status = GitHubAppInstallationStatus.Active };
        db.AddRange(owner, project, credential, installation); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var verifier = new GitHubAppUserGrantVerifier(new RevokingProvider(installationId, repositoryId), Microsoft.Extensions.Options.Options.Create(new GitHubAppOptions { Enabled = true }));
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => new GitHubAppRepositoryLinkService(db, new Protector(), verifier, appOptions: Microsoft.Extensions.Options.Options.Create(new GitHubAppOptions { Enabled = true }))
            .LinkAsync(owner.Id, project.Id, new(credential.Id, installationId, repositoryId), default));
        Assert.Equal("GITHUB_APP_INSTALLATION_UNAVAILABLE", error.Code);
        Assert.Empty(await db.ProjectGitRepositories.Where(x => x.ProjectId == project.Id).ToListAsync());
    }
}
