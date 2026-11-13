using System.Net;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
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

public sealed class Step4cPostgresFactAttribute : FactAttribute
{
    public Step4cPostgresFactAttribute() { if (Environment.GetEnvironmentVariable("UNINET_STEP4C_DISPOSABLE") != "1") Skip = "Requires dedicated disposable PostgreSQL."; }
}
public sealed class Step4cPostgresTheoryAttribute : TheoryAttribute
{
    public Step4cPostgresTheoryAttribute() { if (Environment.GetEnvironmentVariable("UNINET_STEP4C_DISPOSABLE") != "1") Skip = "Requires dedicated disposable PostgreSQL."; }
}

[Collection("GitHub App activity PostgreSQL")]
public sealed class GitHubAppActivityWebhookPostgresTests
{
    private static string Database
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("UNINET_STEP4C_TEST_DATABASE");
            if (value is null) return "uninet_step4c_webhook_20261109";
            if (!value.StartsWith("uninet_step5b_", StringComparison.Ordinal) || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
                throw new InvalidOperationException("The override must name a dedicated Step 5B disposable database.");
            return value;
        }
    }
    private static string Connection => "Host=127.0.0.1;Port=55433;Database=" + Database + ";Username=postgres;Timeout=5";
    private const string Secret = "activity-webhook-fixture";
    private static UniNetDbContext Open(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(Connection).AddInterceptors(interceptors).Options);
    private static string Sign(byte[] bytes) => "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), bytes)).ToLowerInvariant();

    private sealed class Gate
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task WaitAsync(CancellationToken ct) { Entered.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), ct); }
    }
    private sealed class CommandGate(Gate gate) : DbCommandInterceptor
    {
        private int reached;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal) &&
                command.CommandText.Contains("GitSyncJobs", StringComparison.Ordinal) && Interlocked.Exchange(ref reached, 1) == 0)
                await gate.WaitAsync(ct);
            return result;
        }
    }
    private sealed class InterruptBeforeEnqueueCommit : SaveChangesInterceptor
    {
        public bool Hit;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<GitSyncJob>().Any(x => x.State == EntityState.Added) &&
                data.Context.ChangeTracker.Entries<GitHubAppWebhookDelivery>().Any(x => x.Entity.Status == GitHubAppWebhookDeliveryStatus.Processed))
            { Hit = true; throw new InvalidOperationException("injected interruption before enqueue/delivery commit"); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class Remote(Fixture f) : HttpMessageHandler
    {
        public long AppId = 42;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.RequestUri!.AbsolutePath == "/app" ? $"{{\"id\":{AppId}}}" :
                $"{{\"id\":{f.Installation.ProviderInstallationId},\"app_id\":{AppId},\"account\":{{\"id\":9}},\"suspended_at\":null}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
    private sealed class Tokens(Fixture f) : IGitHubAppInstallationTokenProvider
    {
        public int Calls;
        public Task<string> GetAsync(long installationId, long repositoryId, CancellationToken ct)
        {
            Assert.Equal(f.Installation.ProviderInstallationId, installationId); Assert.Equal(f.Repository.ProviderRepositoryId, repositoryId);
            Interlocked.Increment(ref Calls); return Task.FromResult("installation-scoped-fixture");
        }
        public void InvalidateInstallation(long id) { }
    }
    private sealed class Provider(Fixture f) : IGitHubProviderClient
    {
        public Gate? FirstRead;
        public int Reads;
        public bool FailSync;
        public string RepositoryName = "repo";
        public Gate? ConcurrentBranches;
        private int branchReads;
        public async Task<GitHubRemoteRepository> GetRepositoryAsync(string token, long id, CancellationToken ct)
        {
            Assert.Equal("installation-scoped-fixture", token); Assert.Equal(f.Repository.ProviderRepositoryId, id);
            if (Interlocked.Increment(ref Reads) == 1 && FirstRead is { } gate) await gate.WaitAsync(ct);
            return new(id, 9, "fixture", RepositoryName, $"fixture/{RepositoryName}", $"https://github.com/fixture/{RepositoryName}", null, "main", true, false, true);
        }
        public async Task<GitHubSyncPage> GetSyncPageAsync(string token, string repo, string category, string? scope, int page, int size, CancellationToken ct)
        {
            Assert.Equal("installation-scoped-fixture", token);
            Assert.Equal($"fixture/{RepositoryName}", repo);
            if (FailSync) throw new GitHubIntegrationException("GITHUB_TEMPORARY", "Temporary fixture failure.", 503);
            if (category == "branches" && ConcurrentBranches is { } gate)
            {
                if (Interlocked.Increment(ref branchReads) == 2) gate.Release.TrySetResult();
                await gate.WaitAsync(ct);
            }
            var value = category switch
            {
                "branches" => """[{"name":"main","commit":{"sha":"activity-sha"}}]""",
                "commits" => """[{"sha":"activity-sha","commit":{"message":"fixture","committer":{"date":"2026-11-04T10:00:00Z"}},"author":{"id":9,"login":"fixture"}}]""",
                "pull-requests" => """[{"id":7001,"number":7,"state":"open","updated_at":"2026-11-04T10:00:00Z","user":{"id":9,"login":"fixture"}}]""",
                "issues" => """[{"id":8001,"number":8,"state":"open","created_at":"2026-11-04T10:00:00Z","user":{"id":9,"login":"fixture"}}]""",
                _ => "[]"
            };
            using var json = JsonDocument.Parse(value);
            return new GitHubSyncPage(json.RootElement.EnumerateArray().Select(x => x.Clone()).ToArray(), false, 5000, null);
        }
        public Task<GitHubOAuthTokens> ExchangeCodeAsync(string code, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubOAuthTokens> RefreshAsync(string token, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubUser> GetAuthenticatedUserAsync(string token, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<GitHubRemoteRepository>> GetRepositoriesAsync(string token, int page, int size, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Protector : IGitHubTokenProtector
    {
        public string Protect(string token) => throw new NotSupportedException("OAuth must not be used.");
        public string Unprotect(string token) => throw new NotSupportedException("OAuth must not be used.");
    }
    private sealed class Progress : IGitHubWorkerProgress
    {
        public int Consumer;
        public void Completed(string loop) { if (loop == "consumer") Interlocked.Increment(ref Consumer); }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public Account Owner = null!;
        public List<Project> Projects = [];
        public List<ProjectGitRepository> Links = [];
        public GitHubAppInstallation Installation = null!;
        public GitHubRepository Repository = null!;
        public GitHubAppRepositoryGrant Grant = null!;
        public IOptions<GitHubAppOptions> App = null!;
        public readonly IOptions<GitSyncOptions> Sync = Options.Create(new GitSyncOptions { Enabled = true, WebhookLeaseSeconds = 60, RetryBaseDelaySeconds = 1 });
        public readonly IGitHubTokenProtector Protection = new GitHubTokenProtector(new EphemeralDataProtectionProvider());
        public Remote Http = null!; public Tokens Tokens = null!; public Provider Provider = null!;
        public readonly Progress Progress = new();
        private readonly List<string> deliveries = [];

        public static async Task<Fixture> CreateAsync()
        {
            await using var db = Open(); await db.Database.OpenConnectionAsync(); var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            Assert.Equal(Database, connection.Database); Assert.Equal("127.0.0.1", connection.Host); Assert.Equal(55433, connection.Port);
            await db.Database.EnsureCreatedAsync(); var f = new Fixture();
            f.Owner = new Account { Email = $"step4c-{Guid.NewGuid():N}@example.test", Role = AccountRole.Student, Status = AccountStatus.Active,
                Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
            f.Installation = new() { ProviderInstallationId = Random.Shared.NextInt64(10000000, long.MaxValue / 2), OwnerAccountId = 9, OwnerLogin = "fixture", OwnerType = "User" };
            f.Repository = new() { ProviderRepositoryId = Random.Shared.NextInt64(10000000, long.MaxValue / 2), ProviderOwnerId = 9, OwnerLogin = "fixture", Name = "repo", FullName = "fixture/repo", HtmlUrl = "https://github.com/fixture/repo", IsPrivate = true };
            f.Grant = new() { InstallationId = f.Installation.Id, ProviderRepositoryId = f.Repository.ProviderRepositoryId, VerifiedAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
            for (var i = 0; i < 5; i++)
            {
                var project = new Project { Creator = f.Owner.Profile!, Title = "Activity webhook", ProjectField = "Engineering", Description = "Test",
                    MemberTarget = 5, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Visibility = ProjectVisibility.Private,
                    Status = ProjectStatus.Active, RecruitmentStatus = RecruitmentStatus.Open };
                var link = new ProjectGitRepository { ProjectId = project.Id, GitHubRepositoryId = f.Repository.Id, LinkedByProfileId = f.Owner.Profile!.Id,
                    CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id,
                    AccessStatus = i == 3 ? ProjectGitRepositoryAccessStatus.AccessLost : ProjectGitRepositoryAccessStatus.Active,
                    SyncPaused = i == 2, UnlinkedAt = i == 4 ? DateTimeOffset.UtcNow.AddSeconds(1) : null };
                f.Projects.Add(project); f.Links.Add(link); db.AddRange(project, link);
            }
            db.AddRange(f.Owner, f.Installation, f.Repository, f.Grant); await db.SaveChangesAsync();
            using var rsa = RSA.Create(2048); f.App = Options.Create(new GitHubAppOptions { Enabled = true, AppId = 42, ClientId = "fixture-app", PrivateKeyPem = rsa.ExportRSAPrivateKeyPem(), WebhookSecret = Secret });
            f.Http = new Remote(f); f.Tokens = new Tokens(f); f.Provider = new Provider(f); return f;
        }
        public byte[] Payload(string name, long? installationId = null, long? repositoryId = null, long? appId = null)
        {
            var install = new Dictionary<string, object> { ["id"] = installationId ?? Installation.ProviderInstallationId };
            if (appId.HasValue) install["app_id"] = appId.Value;
            var data = new Dictionary<string, object> { ["installation"] = install, ["repository"] = new { id = repositoryId ?? Repository.ProviderRepositoryId } };
            switch (name)
            {
                case "push": data["ref"] = "refs/heads/main"; data["before"] = "old"; data["after"] = "activity-sha"; break;
                case "pull_request": data["action"] = "opened"; data["number"] = 7; data["pull_request"] = new { id = 7001, number = 7 }; break;
                case "issues": data["action"] = "opened"; data["issue"] = new { id = 8001 }; break;
                case "issue_comment": data["action"] = "created"; data["issue"] = new { id = 8001 }; data["comment"] = new { id = 2 }; break;
                case "pull_request_review": data["action"] = "submitted"; data["pull_request"] = new { id = 7001 }; data["review"] = new { id = 3 }; break;
                case "pull_request_review_comment": data["action"] = "created"; data["pull_request"] = new { id = 7001 }; data["comment"] = new { id = 4 }; break;
                case "release": data["action"] = "published"; data["release"] = new { id = 5 }; break;
            }
            return JsonSerializer.SerializeToUtf8Bytes(data);
        }
        public async Task<(string Id, GitHubAppWebhookResult Result)> StageAsync(string name, byte[]? bytes = null, string? id = null, string? signature = null)
        {
            id ??= Guid.NewGuid().ToString(); if (!deliveries.Contains(id)) deliveries.Add(id); bytes ??= Payload(name);
            await using var db = Open();
            var result = await new GitHubAppWebhookService(db, Protection, App, Options.Create(new GitHubWebhookOptions { Secret = "legacy-fixture" }))
                .HandleAsync(id, name, signature ?? Sign(bytes), bytes, default);
            return (id, result);
        }
        public GitHubAppWebhookConsumer Consumer(UniNetDbContext db)
        {
            var http = new HttpClient(Http, false);
            var reconciliation = new GitHubAppInstallationReconciliationService(db, http, App, Sync, Tokens);
            return new GitHubAppWebhookConsumer(db, Protection, Tokens, reconciliation, App, Sync, Provider, Progress);
        }
        public async Task<bool> ConsumeAsync()
        { await using var db = Open(); return await Consumer(db).ProcessNextAsync(default); }
        public async Task<GitHubAppWebhookDelivery> DeliveryAsync(string id)
        { await using var db = Open(); return await db.GitHubAppWebhookDeliveries.AsNoTracking().SingleAsync(x => x.DeliveryId == id); }
        public async Task ProcessJobsAsync()
        {
            await using var db = Open(); var jobs = new GitSyncJobService(db, Sync, App);
            while (await jobs.ClaimNextAsync(TimeSpan.FromMinutes(2), default) is { } job)
            {
                var processor = new GitHubSyncJobProcessor(db, Provider, new GitHubCredentialService(db, Provider, new Protector()), Sync, Tokens, App);
                await processor.ProcessAsync(job, default); await jobs.CompleteAsync(job.Id, job.LeaseId!.Value, default); db.ChangeTracker.Clear();
            }
        }
        public async Task AssertVisibilityAsync(string type, int expected)
        {
            await using var db = Open(); var api = new ProjectGitActivityService(db);
            for (var i = 0; i < Projects.Count; i++)
                Assert.Equal(i < 2 ? expected : 0, (await api.GetAsync(Owner.Id, Projects[i].Id, 1, 50, type, null, null, default)).TotalCount);
        }
        public async ValueTask DisposeAsync()
        {
            await using var db = Open(); var ids = Links.Select(x => x.Id).ToArray();
            await db.ProjectGitRepositories.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.UnlinkedAt, DateTimeOffset.UtcNow.AddSeconds(1)));
            await db.GitHubAppWebhookDeliveries.Where(x => deliveries.Contains(x.DeliveryId) && x.Status != GitHubAppWebhookDeliveryStatus.Processed)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, GitHubAppWebhookDeliveryStatus.Unsupported).SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null));
            Http.Dispose();
        }
    }

    [Step4cPostgresFact]
    public async Task InterruptionBetweenEnqueueAndDeliveryCommitRollsBackThenReplaysOnce()
    {
        await using var f = await Fixture.CreateAsync(); var (id, _) = await f.StageAsync("push");
        GitHubAppWebhookDelivery claim;
        await using (var db = Open()) claim = (await f.Consumer(db).ClaimNextAsync(default))!;
        Assert.NotNull(claim); var injection = new InterruptBeforeEnqueueCommit();
        await using (var interrupted = Open(injection))
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Consumer(interrupted).ProcessClaimAsync(claim, default));
        Assert.True(injection.Hit);
        await using (var verify = Open())
        {
            Assert.Equal(GitHubAppWebhookDeliveryStatus.Processing, (await verify.GitHubAppWebhookDeliveries.SingleAsync(x => x.DeliveryId == id)).Status);
            Assert.Equal(0, await verify.GitSyncJobs.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id));
            await verify.GitHubAppWebhookDeliveries.Where(x => x.Id == claim.Id).ExecuteUpdateAsync(x => x.SetProperty(d => d.LeaseExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }
        Assert.True(await f.ConsumeAsync()); Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.DeliveryAsync(id)).Status);
        await using (var verify = Open()) Assert.Equal(2, await verify.GitSyncJobs.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id));
        await f.ProcessJobsAsync(); await f.AssertVisibilityAsync("commit.created", 1);
        await using var final = Open(); Assert.Equal(1, await final.GitActivities.CountAsync(x => x.ActivityType == "commit.created" && final.GitHubProviderObjects.Any(o => o.Id == x.GitHubProviderObjectId && o.GitHubRepositoryId == f.Repository.Id)));
    }

    [Step4cPostgresFact]
    public async Task VerifiedPushRoutesToEligibleProjectsAndExistingPipelinePersistsPrivateActivities()
    {
        await using var f = await Fixture.CreateAsync(); var (id, staged) = await f.StageAsync("push"); Assert.Equal("PendingHandler", staged.Status);
        Assert.True(await f.ConsumeAsync()); Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.DeliveryAsync(id)).Status);
        Assert.True(f.Progress.Consumer >= 3);
        await using (var db = Open())
        {
            var jobs = await db.GitSyncJobs.Where(x => x.GitHubRepositoryId == f.Repository.Id).ToListAsync(); Assert.Equal(2, jobs.Count);
            Assert.All(jobs, x => { Assert.Equal(GitSyncType.Webhook, x.SyncType); Assert.Equal(GitRepositoryCredentialSource.GitHubApp, x.CredentialSource); Assert.Null(x.OAuthConnectionId); Assert.Equal(f.Grant.Id, x.GitHubAppRepositoryGrantId); });
            Assert.All(jobs, x => Assert.Contains(x.ProjectGitRepositoryId, f.Links.Take(2).Select(l => l.Id)));
        }
        await f.ProcessJobsAsync(); await f.AssertVisibilityAsync("commit.created", 1); await f.AssertVisibilityAsync("pull_request.opened", 1);
        await using var verify = Open(); Assert.Equal(2, await verify.GitSyncJobs.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id && x.Status == GitSyncJobStatus.Completed));
        Assert.Equal(1, await verify.GitActivities.CountAsync(x => x.ActivityType == "commit.created" && verify.GitHubProviderObjects.Any(o => o.Id == x.GitHubProviderObjectId && o.GitHubRepositoryId == f.Repository.Id)));
        Assert.True(f.Tokens.Calls > 0);
    }

    [Step4cPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequeuedDeliveryCannotBypassRevokedGrantOrSuspendedInstallation(bool suspended)
    {
        await using var f = await Fixture.CreateAsync(); var (id, _) = await f.StageAsync("push");
        Account admin;
        await using (var db = Open())
        {
            admin = await db.Accounts.SingleOrDefaultAsync(x => x.Role == AccountRole.Admin)
                ?? new Account { Email = $"operator-{Guid.NewGuid():N}@example.test", Role = AccountRole.Admin, Status = AccountStatus.Active };
            if (db.Entry(admin).State == EntityState.Detached) db.Add(admin);
            var delivery = await db.GitHubAppWebhookDeliveries.SingleAsync(x => x.DeliveryId == id);
            delivery.Status = GitHubAppWebhookDeliveryStatus.Failed; delivery.LastErrorCode = "FIXTURE";
            if (suspended) (await db.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == f.Installation.Id)).Status = GitHubAppInstallationStatus.Suspended;
            else (await db.Set<GitHubAppRepositoryGrant>().SingleAsync(x => x.Id == f.Grant.Id)).RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            await new GitHubAppWebhookRecoveryService(db, f.App, NullLogger<GitHubAppWebhookRecoveryService>.Instance).RequeueAsync(admin.Id, id, "fixture recovery", default);
        }
        Assert.True(await f.ConsumeAsync());
        Assert.Equal(GitHubAppWebhookDeliveryStatus.RetryScheduled, (await f.DeliveryAsync(id)).Status);
        await using var verify = Open();
        Assert.Equal(0, await verify.GitSyncJobs.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id));
        Assert.Equal(0, await verify.GitHubProviderObjects.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id));
    }

    [Step4cPostgresFact]
    public async Task RequeuedPushUsesExistingPipelineWithoutDuplicateJobsOrActivities()
    {
        await using var f = await Fixture.CreateAsync(); var (id, _) = await f.StageAsync("push");
        await using (var db = Open())
        {
            var admin = await db.Accounts.SingleOrDefaultAsync(x => x.Role == AccountRole.Admin)
                ?? new Account { Email = $"operator-{Guid.NewGuid():N}@example.test", Role = AccountRole.Admin, Status = AccountStatus.Active };
            if (db.Entry(admin).State == EntityState.Detached) db.Add(admin);
            var delivery = await db.GitHubAppWebhookDeliveries.SingleAsync(x => x.DeliveryId == id);
            delivery.Status = GitHubAppWebhookDeliveryStatus.Failed; delivery.LastErrorCode = "FIXTURE";
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            await new GitHubAppWebhookRecoveryService(db, f.App, NullLogger<GitHubAppWebhookRecoveryService>.Instance).RequeueAsync(admin.Id, id, "fixture recovery", default);
        }
        Assert.True(await f.ConsumeAsync()); await f.ProcessJobsAsync(); await f.AssertVisibilityAsync("commit.created", 1);
        await using var verify = Open();
        Assert.Equal(2, await verify.GitSyncJobs.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id && x.SyncType == GitSyncType.Webhook));
        Assert.Equal(1, await verify.GitActivities.CountAsync(x => x.ActivityType == "commit.created" && verify.GitHubProviderObjects.Any(o => o.Id == x.GitHubProviderObjectId && o.GitHubRepositoryId == f.Repository.Id)));
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.DeliveryAsync(id)).Status);
    }

    [Step4cPostgresFact]
    public async Task PullRequestAndRedeliveryAreIdempotentAcrossRepeatedSyncs()
    {
        await using var f = await Fixture.CreateAsync(); var (id, _) = await f.StageAsync("pull_request");
        var redeliveries = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => f.StageAsync("pull_request", id: id)));
        Assert.All(redeliveries, x => Assert.True(x.Result.Duplicate));
        async Task<GitHubAppWebhookDelivery?> Claim() { await using var db = Open(); return await f.Consumer(db).ClaimNextAsync(default); }
        var claims = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Claim()));
        var claim = Assert.Single(claims, x => x is not null)!;
        await using (var db = Open()) await f.Consumer(db).ProcessClaimAsync(claim, default);
        Assert.Null(await Claim()); // Simulates a crash after commit but before a caller receives confirmation.
        await f.ProcessJobsAsync(); await f.AssertVisibilityAsync("pull_request.opened", 1);
        var second = await f.StageAsync("pull_request"); Assert.True(await f.ConsumeAsync());
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.DeliveryAsync(second.Id)).Status);
        await f.ProcessJobsAsync(); await f.AssertVisibilityAsync("pull_request.opened", 1);
        await using var verify = Open();
        Assert.Equal(1, await verify.GitActivities.CountAsync(x => x.ActivityType == "pull_request.opened" && verify.GitHubProviderObjects.Any(o => o.Id == x.GitHubProviderObjectId && o.GitHubRepositoryId == f.Repository.Id)));
    }

    [Step4cPostgresTheory]
    [InlineData(false)][InlineData(true)]
    public async Task RunningOrRetryingJobGetsOnlyOneFollowUpAcrossSeveralPushes(bool retrying)
    {
        await using var f = await Fixture.CreateAsync();
        var running = new GitSyncJob { ProjectGitRepositoryId = f.Links[0].Id, GitHubRepositoryId = f.Repository.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp,
            GitHubAppRepositoryGrantId = f.Grant.Id, SyncType = GitSyncType.Manual,
            Status = retrying ? GitSyncJobStatus.RetryScheduled : GitSyncJobStatus.Running,
            LeaseId = retrying ? null : Guid.NewGuid(), LeaseExpiresAt = retrying ? null : DateTimeOffset.UtcNow.AddMinutes(5),
            NextAttemptAt = retrying ? DateTimeOffset.UtcNow.AddHours(1) : null };
        await using (var db = Open()) { db.Add(running); await db.SaveChangesAsync(); }
        await f.StageAsync("push"); await f.StageAsync("push"); Assert.True(await f.ConsumeAsync()); Assert.True(await f.ConsumeAsync());
        await using var verify = Open();
        Assert.Equal(1, await verify.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == f.Links[0].Id && x.Status == GitSyncJobStatus.Pending));
        Assert.Equal(1, await verify.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == f.Links[0].Id && x.Status == running.Status));
        Assert.Equal(1, await verify.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == f.Links[1].Id && x.Status == GitSyncJobStatus.Pending));
        if (retrying)
            await verify.GitSyncJobs.Where(x => x.Id == running.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, GitSyncJobStatus.Completed)
                .SetProperty(x => x.CompletedAt, DateTimeOffset.UtcNow).SetProperty(x => x.NextAttemptAt, (DateTimeOffset?)null));
        else await new GitSyncJobService(verify, f.Sync, f.App).CompleteAsync(running.Id, running.LeaseId!.Value, default);
        await f.ProcessJobsAsync(); await f.AssertVisibilityAsync("commit.created", 1);
    }

    [Step4cPostgresFact]
    public async Task GrantRevokedDuringLiveVerificationNeverEnqueues()
    {
        await using var f = await Fixture.CreateAsync(); var gate = new Gate(); f.Provider.FirstRead = gate;
        var (id, _) = await f.StageAsync("push"); var consume = f.ConsumeAsync();
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await using var db = Open(); await db.Set<GitHubAppRepositoryGrant>().Where(x => x.Id == f.Grant.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
        }
        finally { gate.Release.TrySetResult(); }
        Assert.True(await consume); Assert.Equal(GitHubAppWebhookDeliveryStatus.RetryScheduled, (await f.DeliveryAsync(id)).Status);
        await using var verify = Open(); Assert.Equal(0, await verify.GitSyncJobs.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id));
    }

    [Step4cPostgresFact]
    public async Task WrongScopeAndSignatureNeverCreateJobs()
    {
        await using var f = await Fixture.CreateAsync();
        var wrongInstall = await f.StageAsync("push", f.Payload("push", installationId: f.Installation.ProviderInstallationId + 1));
        Assert.True(await f.ConsumeAsync()); Assert.Equal(GitHubAppWebhookDeliveryStatus.RetryScheduled, (await f.DeliveryAsync(wrongInstall.Id)).Status);
        var wrongRepo = await f.StageAsync("push", f.Payload("push", repositoryId: f.Repository.ProviderRepositoryId + 1));
        Assert.True(await f.ConsumeAsync()); Assert.Equal(GitHubAppWebhookDeliveryStatus.RetryScheduled, (await f.DeliveryAsync(wrongRepo.Id)).Status);
        var wrongApp = await f.StageAsync("push", f.Payload("push", appId: 43)); Assert.True(await f.ConsumeAsync());
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Failed, (await f.DeliveryAsync(wrongApp.Id)).Status);
        var bytes = f.Payload("push"); await Assert.ThrowsAsync<GitHubIntegrationException>(() => f.StageAsync("push", bytes, signature: "sha256=bad"));
        await using var db = Open(); Assert.Equal(0, await db.GitSyncJobs.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id));
    }

    [Step4cPostgresFact]
    public async Task JwtForDifferentAppCannotRouteAnOtherwiseValidDelivery()
    {
        await using var f = await Fixture.CreateAsync(); f.Http.AppId = 43;
        var (id, _) = await f.StageAsync("push"); Assert.True(await f.ConsumeAsync());
        var delivery = await f.DeliveryAsync(id); Assert.Equal(GitHubAppWebhookDeliveryStatus.Failed, delivery.Status);
        Assert.Equal("GITHUB_APP_WEBHOOK_APP_MISMATCH", delivery.LastErrorCode);
        await using var db = Open(); Assert.Equal(0, await db.GitSyncJobs.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id));
    }

    [Step4cPostgresFact]
    public async Task StableIdRefreshesRenamedRepositoryBeforeExistingWorkerFetches()
    {
        await using var f = await Fixture.CreateAsync(); f.Provider.RepositoryName = "renamed";
        var (id, _) = await f.StageAsync("push"); Assert.True(await f.ConsumeAsync());
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.DeliveryAsync(id)).Status);
        await using (var db = Open()) Assert.Equal("fixture/renamed", (await db.GitHubRepositories.SingleAsync(x => x.Id == f.Repository.Id)).FullName);
        await f.ProcessJobsAsync(); await f.AssertVisibilityAsync("commit.created", 1);
    }

    [Step4cPostgresFact]
    public async Task ConcurrentProjectWorkersRetryWithoutDuplicatingSharedPrivateActivity()
    {
        await using var f = await Fixture.CreateAsync(); await f.StageAsync("push"); Assert.True(await f.ConsumeAsync());
        async Task<GitSyncJob?> Claim() { await using var db = Open(); return await new GitSyncJobService(db, f.Sync, f.App).ClaimNextAsync(TimeSpan.FromMinutes(2), default); }
        var claims = await Task.WhenAll(Claim(), Claim()); Assert.Equal(2, claims.Count(x => x is not null));
        f.Provider.ConcurrentBranches = new Gate();
        async Task Run(GitSyncJob job)
        {
            try
            {
                await using var db = Open();
                var processor = new GitHubSyncJobProcessor(db, f.Provider, new GitHubCredentialService(db, f.Provider, new Protector()), f.Sync, f.Tokens, f.App);
                await processor.ProcessAsync(job, default); await new GitSyncJobService(db, f.Sync, f.App).CompleteAsync(job.Id, job.LeaseId!.Value, default);
            }
            catch (Exception ex) when (ex.GetBaseException() is PostgresException { SqlState: "40001" or "40P01" or "23505" })
            {
                await using var retryDb = Open();
                await new GitSyncJobService(retryDb, f.Sync, f.App).FailAsync(job.Id, job.LeaseId!.Value, "TEST_RETRY", "Synchronization will be retried.", DateTimeOffset.UtcNow.AddSeconds(-1), default);
            }
        }
        await Task.WhenAll(claims.Select(x => Run(x!))); f.Provider.ConcurrentBranches = null;
        await f.ProcessJobsAsync(); await f.AssertVisibilityAsync("commit.created", 1);
        await using var verify = Open();
        Assert.Equal(2, await verify.GitSyncJobs.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id && x.Status == GitSyncJobStatus.Completed));
        Assert.Equal(1, await verify.GitActivities.CountAsync(x => x.ActivityType == "commit.created" && verify.GitHubProviderObjects.Any(o => o.Id == x.GitHubProviderObjectId && o.GitHubRepositoryId == f.Repository.Id)));
    }

    [Step4cPostgresFact]
    public async Task PendingJobClaimRaceCannotDiscardNewWebhookTrigger()
    {
        await using var f = await Fixture.CreateAsync();
        var pending = new GitSyncJob { ProjectGitRepositoryId = f.Links[0].Id, GitHubRepositoryId = f.Repository.Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id,
            SyncType = GitSyncType.Manual, Status = GitSyncJobStatus.Pending };
        await using (var seed = Open()) { seed.Add(pending); await seed.SaveChangesAsync(); }
        var (deliveryId, _) = await f.StageAsync("push"); var gate = new Gate();
        async Task<bool> Route()
        { await using var db = Open(new CommandGate(gate)); return await f.Consumer(db).ProcessNextAsync(default); }
        var routing = Route();
        GitSyncJob? claimed;
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await using var worker = Open(); claimed = await new GitSyncJobService(worker, f.Sync, f.App).ClaimNextAsync(TimeSpan.FromMinutes(2), default);
            Assert.NotNull(claimed); Assert.Equal(pending.Id, claimed.Id);
        }
        finally { gate.Release.TrySetResult(); }
        Assert.True(await routing);
        var delivery = await f.DeliveryAsync(deliveryId);
        if (delivery.Status == GitHubAppWebhookDeliveryStatus.RetryScheduled)
        {
            await using (var db = Open()) await db.GitHubAppWebhookDeliveries.Where(x => x.DeliveryId == deliveryId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
            Assert.True(await f.ConsumeAsync());
        }
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.DeliveryAsync(deliveryId)).Status);
        await using (var db = Open())
        {
            Assert.Equal(1, await db.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == f.Links[0].Id && x.Status == GitSyncJobStatus.Pending));
            await new GitSyncJobService(db, f.Sync, f.App).CompleteAsync(claimed!.Id, claimed.LeaseId!.Value, default);
        }
        await f.ProcessJobsAsync(); await f.AssertVisibilityAsync("commit.created", 1);
    }

    [Step4cPostgresTheory]
    [InlineData("push")][InlineData("pull_request")][InlineData("issues")][InlineData("issue_comment")]
    [InlineData("pull_request_review")][InlineData("pull_request_review_comment")][InlineData("release")]
    public async Task SupportedEventsRouteThroughTheSameAppSyncJob(string name)
    {
        await using var f = await Fixture.CreateAsync(); var (id, _) = await f.StageAsync(name);
        Assert.True(await f.ConsumeAsync()); Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.DeliveryAsync(id)).Status);
        await using var db = Open();
        Assert.Equal(2, await db.GitSyncJobs.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id && x.SyncType == GitSyncType.Webhook));
    }

    [Step4cPostgresTheory]
    [InlineData("unknown")][InlineData("suspended")][InlineData("uninstalled")][InlineData("revoked")]
    public async Task UnverifiedOrRevokedScopeIsDeferredWithoutJobs(string state)
    {
        await using var f = await Fixture.CreateAsync();
        await using (var db = Open())
        {
            if (state == "revoked") await db.Set<GitHubAppRepositoryGrant>().Where(x => x.Id == f.Grant.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
            else if (state == "unknown") await db.Set<GitHubAppInstallation>().Where(x => x.Id == f.Installation.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.VerificationUnknown, true));
            else await db.Set<GitHubAppInstallation>().Where(x => x.Id == f.Installation.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, state == "suspended" ? GitHubAppInstallationStatus.Suspended : GitHubAppInstallationStatus.Uninstalled));
        }
        var (id, staged) = await f.StageAsync("push"); Assert.Equal("AwaitingScope", staged.Status);
        Assert.True(await f.ConsumeAsync()); Assert.Equal(GitHubAppWebhookDeliveryStatus.RetryScheduled, (await f.DeliveryAsync(id)).Status);
        await using var verify = Open(); Assert.Equal(0, await verify.GitSyncJobs.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id));
    }

    [Step4cPostgresFact]
    public async Task NoEligibleLinkCompletesWithoutJobsAndBranchEventsAreUnsupported()
    {
        await using var f = await Fixture.CreateAsync();
        await using (var db = Open())
        {
            var ids = f.Links.Take(2).Select(x => x.Id).ToArray();
            await db.ProjectGitRepositories.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.SyncPaused, true));
        }
        var (id, staged) = await f.StageAsync("push"); Assert.Equal("AwaitingScope", staged.Status);
        Assert.True(await f.ConsumeAsync()); Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.DeliveryAsync(id)).Status);
        var (_, branch) = await f.StageAsync("create"); Assert.False(branch.SupportedEvent); Assert.Equal("Unsupported", branch.Status);
        await using var verify = Open(); Assert.Equal(0, await verify.GitSyncJobs.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id));
    }

    [Step4cPostgresFact]
    public async Task ExpiredConsumerLeaseAndWorkerRetryDoNotDuplicateActivity()
    {
        await using var f = await Fixture.CreateAsync(); var (id, _) = await f.StageAsync("push");
        GitHubAppWebhookDelivery first;
        await using (var db = Open())
        {
            first = (await f.Consumer(db).ClaimNextAsync(default))!; Assert.NotNull(first);
            await db.GitHubAppWebhookDeliveries.Where(x => x.Id == first.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }
        await using (var db = Open())
        {
            var consumer = f.Consumer(db); var second = await consumer.ClaimNextAsync(default); Assert.NotNull(second);
            Assert.NotEqual(first.LeaseId, second.LeaseId);
            await Assert.ThrowsAnyAsync<Exception>(() => consumer.ProcessClaimAsync(first, default));
            await consumer.ProcessClaimAsync(second, default);
        }
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.DeliveryAsync(id)).Status);
        await using (var db = Open())
        {
            var jobs = new GitSyncJobService(db, f.Sync, f.App); var job = await jobs.ClaimNextAsync(TimeSpan.FromMinutes(2), default); Assert.NotNull(job);
            var processor = new GitHubSyncJobProcessor(db, f.Provider, new GitHubCredentialService(db, f.Provider, new Protector()), f.Sync, f.Tokens, f.App);
            f.Provider.FailSync = true; var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => processor.ProcessAsync(job, default));
            await jobs.FailAsync(job.Id, job.LeaseId!.Value, error.Code, error.Message, DateTimeOffset.UtcNow.AddSeconds(-1), default);
        }
        f.Provider.FailSync = false; await f.ProcessJobsAsync(); await f.AssertVisibilityAsync("commit.created", 1);
        await using var verify = Open();
        Assert.Equal(2, await verify.GitSyncJobs.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id && x.Status == GitSyncJobStatus.Completed));
        Assert.Equal(1, await verify.GitActivities.CountAsync(x => x.ActivityType == "commit.created" && verify.GitHubProviderObjects.Any(o => o.Id == x.GitHubProviderObjectId && o.GitHubRepositoryId == f.Repository.Id)));
    }

    [Step4cPostgresFact]
    public async Task HostedWorkerKeepsAppWorkWhileDisabledAndProcessesAfterRestart()
    {
        await using var f = await Fixture.CreateAsync();
        var (deliveryId, _) = await f.StageAsync("push");
        var pending = new GitSyncJob { ProjectGitRepositoryId = f.Links[0].Id, GitHubRepositoryId = f.Repository.Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id, SyncType = GitSyncType.Initial };
        await using (var db = Open())
        {
            db.Add(pending); await db.SaveChangesAsync();
            await db.Set<GitHubAppInstallation>().Where(x => x.Id == f.Installation.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextReconciliationAt, DateTimeOffset.UtcNow.AddHours(1)));
        }
        var sync = Options.Create(new GitSyncOptions { Enabled = true, WorkerConcurrency = 1, PollSeconds = 1, ReconciliationPollSeconds = 1, RetryBaseDelaySeconds = 1 });
        async Task RunAsync(bool enabled, Func<UniNetDbContext, Task<bool>> done)
        {
            var app = Options.Create(new GitHubAppOptions { Enabled = enabled, AppId = f.App.Value.AppId, ClientId = f.App.Value.ClientId, PrivateKeyPem = f.App.Value.PrivateKeyPem, WebhookSecret = Secret });
            var services = new ServiceCollection(); using var http = new HttpClient(f.Http, false);
            services.AddDbContext<UniNetDbContext>(o => o.UseNpgsql(Connection));
            services.AddSingleton<IOptions<GitSyncOptions>>(sync); services.AddSingleton<IOptions<GitHubAppOptions>>(app);
            services.AddSingleton<IGitHubTokenProtector>(f.Protection); services.AddSingleton<IGitHubAppInstallationTokenProvider>(f.Tokens);
            services.AddSingleton<IGitHubProviderClient>(f.Provider); services.AddScoped<GitHubCredentialService>();
            services.AddScoped(sp => new GitHubAppInstallationReconciliationService(sp.GetRequiredService<UniNetDbContext>(), http, app, sync, f.Tokens));
            services.AddScoped<GitHubAppRepositoryActivationService>(); services.AddScoped<GitHubAppWebhookConsumer>();
            services.AddScoped<IGitSyncJobService, GitSyncJobService>(); services.AddScoped<IGitSyncJobProcessor, GitHubSyncJobProcessor>();
            await using var container = services.BuildServiceProvider();
            using var worker = new GitSyncWorker(container.GetRequiredService<IServiceScopeFactory>(), sync, NullLogger<GitSyncWorker>.Instance, app);
            await worker.StartAsync(default);
            try
            {
                var deadline = DateTimeOffset.UtcNow.AddSeconds(enabled ? 25 : 2);
                while (DateTimeOffset.UtcNow < deadline) { await Task.Delay(200); await using var db = Open(); if (enabled && await done(db)) return; }
                if (enabled) Assert.Fail("Enabled worker did not finish durable App work.");
                await using var verify = Open(); Assert.True(await done(verify));
            }
            finally { await worker.StopAsync(default); }
        }
        await RunAsync(false, async db => (await db.GitSyncJobs.SingleAsync(x => x.Id == pending.Id)).Status == GitSyncJobStatus.Pending &&
            (await db.GitHubAppWebhookDeliveries.SingleAsync(x => x.DeliveryId == deliveryId)).Status == GitHubAppWebhookDeliveryStatus.PendingHandler);
        Assert.Equal(0, f.Tokens.Calls);
        await RunAsync(true, async db => (await db.GitSyncJobs.SingleAsync(x => x.Id == pending.Id)).Status == GitSyncJobStatus.Completed &&
            (await db.GitHubAppWebhookDeliveries.SingleAsync(x => x.DeliveryId == deliveryId)).Status == GitHubAppWebhookDeliveryStatus.Processed);
    }

    [Step4cPostgresFact]
    public async Task ExistingHostedWorkerConsumesWebhookAndCompletesBothProjectJobs()
    {
        await using var f = await Fixture.CreateAsync();
        await using (var db = Open()) await db.Set<GitHubAppInstallation>().Where(x => x.Id == f.Installation.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextReconciliationAt, DateTimeOffset.UtcNow.AddHours(1)));
        // The reconciliation loop may legitimately restore a lost grant; preserve an explicit pause
        // so this project remains ineligible throughout the hosted-worker assertion.
        await using (var db = Open()) await db.ProjectGitRepositories.Where(x => x.Id == f.Links[3].Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.SyncPaused, true));
        var (id, _) = await f.StageAsync("push");
        var options = Options.Create(new GitSyncOptions { Enabled = true, WorkerConcurrency = 1, PollSeconds = 1, ReconciliationPollSeconds = 1, WebhookLeaseSeconds = 60, RetryBaseDelaySeconds = 1 });
        var services = new ServiceCollection(); using var http = new HttpClient(f.Http, false);
        services.AddDbContext<UniNetDbContext>(o => o.UseNpgsql(Connection));
        services.AddSingleton<IOptions<GitSyncOptions>>(options); services.AddSingleton<IOptions<GitHubAppOptions>>(f.App);
        services.AddSingleton<IGitHubTokenProtector>(f.Protection); services.AddSingleton<IGitHubAppInstallationTokenProvider>(f.Tokens);
        services.AddSingleton<IGitHubProviderClient>(f.Provider); services.AddScoped<GitHubCredentialService>();
        services.AddScoped(sp => new GitHubAppInstallationReconciliationService(sp.GetRequiredService<UniNetDbContext>(), http, f.App, options, f.Tokens));
        services.AddScoped<GitHubAppRepositoryActivationService>(); services.AddScoped<GitHubAppWebhookConsumer>();
        services.AddScoped<IGitSyncJobService, GitSyncJobService>(); services.AddScoped<IGitSyncJobProcessor, GitHubSyncJobProcessor>();
        await using var container = services.BuildServiceProvider();
        var progress = new GitHubWorkerProgress();
        using var worker = new GitSyncWorker(container.GetRequiredService<IServiceScopeFactory>(), options, NullLogger<GitSyncWorker>.Instance, f.App, progress);
        try
        {
            await worker.StartAsync(default); var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await using var check = Open();
                if ((await f.DeliveryAsync(id)).Status == GitHubAppWebhookDeliveryStatus.Processed &&
                    await check.GitSyncJobs.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id && x.Status == GitSyncJobStatus.Completed) == 2 &&
                    await check.Set<GitHubAppWorkerHeartbeat>().AnyAsync(x => x.Id == progress.InstanceId && x.WorkerAt != null && x.ConsumerAt != null && x.ReconciliationAt != null)) break;
                await Task.Delay(100);
            }
        }
        finally { await worker.StopAsync(default); }
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.DeliveryAsync(id)).Status);
        await using var verify = Open(); Assert.Equal(2, await verify.GitSyncJobs.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id && x.Status == GitSyncJobStatus.Completed));
        Assert.True(await verify.Set<GitHubAppWorkerHeartbeat>().AnyAsync(x => x.Id == progress.InstanceId && x.WorkerAt != null && x.ConsumerAt != null && x.ReconciliationAt != null));
        await f.AssertVisibilityAsync("commit.created", 1);
    }
}
