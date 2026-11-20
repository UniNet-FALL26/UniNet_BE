using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class Step3cReconciliationFactAttribute : FactAttribute
{
    public Step3cReconciliationFactAttribute() { if (Environment.GetEnvironmentVariable("UNINET_RECON_DISPOSABLE") != "1") Skip = "Requires dedicated disposable reconciliation database."; }
}

[Collection("GitHub reconciliation PostgreSQL")]
public sealed class GitHubAppReconciliationPostgresTests
{
    private sealed class Progress : IGitHubWorkerProgress
    {
        public int Count;
        public void Completed(string loop) { if (loop == "reconciliation") Interlocked.Increment(ref Count); }
    }
    private static string Database => GitHubAppDisposableDatabase.Name("uninet_step3c_step4c_regression");
    private static string Connection => $"Host=127.0.0.1;Port=55433;Database={Database};Username=postgres;Timeout=5";
    private static UniNetDbContext Open() => new(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(Connection).Options);
    private sealed class Tokens : IGitHubAppInstallationTokenProvider
    {
        public Task<string> GetAsync(long installationId, long repositoryId, CancellationToken ct) => throw new NotSupportedException();
        public void InvalidateInstallation(long installationId) { }
    }
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Method == HttpMethod.Post ? "{\"token\":\"test\"}" :
                request.RequestUri!.AbsolutePath == "/installation/repositories" ? "{\"total_count\":0,\"repositories\":[]}" :
                $"{{\"id\":{request.RequestUri!.Segments.Last()},\"suspended_at\":null}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Step3cReconciliationFact]
    public async Task ConcurrentScansRevokeOneGrantAcrossProjectsWithoutDuplicateHistory()
    {
        await using (var check = new NpgsqlConnection(Connection))
        { await check.OpenAsync(); Assert.Equal(Database, check.Database); Assert.Equal("127.0.0.1", check.Host); }
        await using var db = Open(); await db.Database.EnsureCreatedAsync();
        var owner = new Account { Email = $"reconcile-{Guid.NewGuid():N}@example.test", Role = AccountRole.Student, Status = AccountStatus.Active,
            Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
        Project MakeProject() => new() { Creator = owner.Profile!, Title = "Reconciliation", ProjectField = "Engineering", Description = "Disposable",
            MemberTarget = 5, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Visibility = ProjectVisibility.Public,
            Status = ProjectStatus.Active, RecruitmentStatus = RecruitmentStatus.Open };
        var first = MakeProject(); var second = MakeProject();
        var installation = new GitHubAppInstallation { ProviderInstallationId = Random.Shared.NextInt64(10000000, long.MaxValue / 2), OwnerAccountId = 42, OwnerLogin = "example", OwnerType = "User" };
        var repo = new GitHubRepository { ProviderRepositoryId = Random.Shared.NextInt64(10000000, long.MaxValue / 2), ProviderOwnerId = 42, OwnerLogin = "example", Name = "repo", FullName = "example/repo", HtmlUrl = "https://github.com/example/repo" };
        var grant = new GitHubAppRepositoryGrant { InstallationId = installation.Id, ProviderRepositoryId = repo.ProviderRepositoryId, VerifiedAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
        ProjectGitRepository MakeLink(Project project) => new() { ProjectId = project.Id, GitHubRepositoryId = repo.Id, LinkedByProfileId = owner.Profile!.Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, AccessStatus = ProjectGitRepositoryAccessStatus.Active };
        var link1 = MakeLink(first); var link2 = MakeLink(second);
        var job = new GitSyncJob { ProjectGitRepositoryId = link1.Id, GitHubRepositoryId = repo.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp,
            GitHubAppRepositoryGrantId = grant.Id, SyncType = GitSyncType.Scheduled, Status = GitSyncJobStatus.Pending };
        db.AddRange(owner, first, second, installation, repo, grant, link1, link2, job); await db.SaveChangesAsync();
        using var rsa = RSA.Create(2048);
        var options = Options.Create(new GitHubAppOptions { Enabled = true, ClientId = "test-app", PrivateKeyPem = rsa.ExportRSAPrivateKeyPem() });
        var progress = new Progress();
        async Task Scan()
        {
            await using var context = Open(); using var http = new HttpClient(new Handler());
            await new GitHubAppInstallationReconciliationService(context, http, options, Options.Create(new GitSyncOptions { Enabled = true }), new Tokens(), progress).ReconcileAsync(default);
        }
        await Task.WhenAll(Scan(), Scan());
        Assert.True(progress.Count > 0);
        await using var verify = Open();
        Assert.NotNull((await verify.Set<GitHubAppRepositoryGrant>().SingleAsync(x => x.Id == grant.Id)).RevokedAt);
        Assert.Equal(2, await verify.ProjectGitRepositories.CountAsync(x => (x.Id == link1.Id || x.Id == link2.Id) && x.AccessStatus == ProjectGitRepositoryAccessStatus.AccessLost));
        Assert.Equal(2, await verify.ProjectGitRepositoryHistory.CountAsync(x => x.ProjectGitRepositoryId == link1.Id || x.ProjectGitRepositoryId == link2.Id));
        Assert.Equal(GitSyncJobStatus.Cancelled, (await verify.GitSyncJobs.SingleAsync(x => x.Id == job.Id)).Status);
        Assert.Equal(1, await verify.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == link1.Id));
        await verify.ProjectGitRepositories.Where(x => x.Id == link1.Id || x.Id == link2.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UnlinkedAt, DateTimeOffset.UtcNow));
    }

    private sealed class RateLimitedHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return Task.FromResult(response);
        }
    }

    [Step3cReconciliationFact]
    public async Task RateLimitDefersSubsequentReconciliationWithoutChangingAccess()
    {
        await using (var check = new NpgsqlConnection(Connection))
        { await check.OpenAsync(); Assert.Equal(Database, check.Database); Assert.Equal("127.0.0.1", check.Host); }
        await using var db = Open(); await db.Database.EnsureCreatedAsync();
        var installation = new GitHubAppInstallation { ProviderInstallationId = Random.Shared.NextInt64(1000000, 2000000000),
            OwnerAccountId = 42, OwnerLogin = "example", OwnerType = "User" };
        db.Add(installation); await db.SaveChangesAsync();
        using var rsa = RSA.Create(2048); var handler = new RateLimitedHandler(); using var http = new HttpClient(handler);
        var options = Options.Create(new GitHubAppOptions { Enabled = true, ClientId = "test-app", PrivateKeyPem = rsa.ExportRSAPrivateKeyPem() });
        var service = new GitHubAppInstallationReconciliationService(db, http, options, Options.Create(new GitSyncOptions { Enabled = true }), new Tokens());
        await service.ReconcileAsync(default); var calls = handler.Calls;
        Assert.True(calls >= 1);
        await service.ReconcileAsync(default);
        Assert.Equal(calls, handler.Calls);
        Assert.Equal(GitHubAppInstallationStatus.Active, (await db.Set<GitHubAppInstallation>().AsNoTracking().SingleAsync(x => x.Id == installation.Id)).Status);
    }
}
