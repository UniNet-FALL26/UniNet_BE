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
using Microsoft.Extensions.Logging;
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
public sealed class EvidenceHostedSchemaFactAttribute : FactAttribute
{
    public EvidenceHostedSchemaFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("UNINET_STEP4C_DISPOSABLE") != "1" || Environment.GetEnvironmentVariable("UNINET_EVIDENCE_SCHEMA_EXPECTED") is null)
            Skip = "Requires an explicitly configured disposable schema state.";
    }
}

[Collection("GitHub App activity PostgreSQL")]
public sealed partial class GitHubAppActivityWebhookPostgresTests
{
    private sealed class LeaseLossLogger : ILogger<GitSyncWorker>
    {
        public TaskCompletionSource Seen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        { if (formatter(state, error).Contains("GITHUB_SYNC_LEASE_LOST", StringComparison.Ordinal)) Seen.TrySetResult(); }
    }
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
    private static int Port
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("UNINET_STEP4C_TEST_PORT");
            if (value is null) return 55433;
            if (!int.TryParse(value, out var port) || port is <= 1024 or 55433 or > 65535 || !Database.StartsWith("uninet_step5b_", StringComparison.Ordinal))
                throw new InvalidOperationException("Override requires a dedicated disposable database and port.");
            return port;
        }
    }
    private static string Connection => "Host=127.0.0.1;Port=" + Port + ";Database=" + Database + ";Username=postgres;Timeout=5";
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
    private sealed class InterruptEvidenceWrite : DbCommandInterceptor
    {
        public bool Hit;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"GitEvidenceVersions\"", StringComparison.Ordinal))
            { Hit = true; throw new InvalidOperationException("injected evidence write failure"); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class InterruptSecondCommitPage : SaveChangesInterceptor
    {
        public bool Hit;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<ProjectEvidenceObservation>().Any(x => x.State == EntityState.Added && x.Entity.Category == "commits" && x.Entity.PageCursor == "2"))
            { Hit = true; throw new InvalidOperationException("injected second-page failure"); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class CaptureDatabaseFailure : DbCommandInterceptor
    {
        public PostgresException? Failure;
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData data, CancellationToken ct = default)
        {
            if (data.Exception.GetBaseException() is PostgresException error) Failure = error;
            return Task.CompletedTask;
        }
    }
    private sealed class EvidenceLookupGate(Gate gate) : DbCommandInterceptor
    {
        private int hit;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("FROM \"GitEvidenceVersions\"", StringComparison.Ordinal) && Interlocked.Exchange(ref hit, 1) == 0)
                await gate.WaitAsync(ct);
            return result;
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
        public int TransientFailuresRemaining;
        public bool AcceptReplacementToken;
        public long CommitAuthorId = 9;
        public bool DuplicateBranchAcrossPages;
        public bool CommitVersionAcrossPages;
        public bool IncludeContributionRelationships;
        public bool IncludeIssueComments;
        public string BranchName = "main";
        public string RepositoryName = "repo";
        public Gate? ConcurrentBranches;
        public Gate? BeforePageReturn;
        public bool GateOnlyFirstPage;
        private int gatedPages;
        private int branchReads;
        public async Task<GitHubRemoteRepository> GetRepositoryAsync(string token, long id, CancellationToken ct)
        {
            Assert.Equal("installation-scoped-fixture", token); Assert.Equal(f.Repository.ProviderRepositoryId, id);
            if (Interlocked.Increment(ref Reads) == 1 && FirstRead is { } gate) await gate.WaitAsync(ct);
            return new(id, 9, "fixture", RepositoryName, $"fixture/{RepositoryName}", $"https://github.com/fixture/{RepositoryName}", null, "main", true, false, true);
        }
        public async Task<GitHubSyncPage> GetSyncPageAsync(string token, string repo, string category, string? scope, int page, int size, CancellationToken ct)
        {
            Assert.True(token == "installation-scoped-fixture" || (AcceptReplacementToken && token == "replacement-fixture"));
            Assert.Equal($"fixture/{RepositoryName}", repo);
            if (FailSync) throw new GitHubIntegrationException("GITHUB_TEMPORARY", "Temporary fixture failure.", 503);
            if (TransientFailuresRemaining > 0 && Interlocked.Decrement(ref TransientFailuresRemaining) >= 0)
                throw new GitHubIntegrationException("GITHUB_TEMPORARY", "Temporary fixture failure.", 503);
            if (category == "branches" && ConcurrentBranches is { } gate)
            {
                if (Interlocked.Increment(ref branchReads) == 2) gate.Release.TrySetResult();
                await gate.WaitAsync(ct);
            }
            var value = category switch
            {
                "issue-comments" when IncludeIssueComments => """[{"id":8100,"user":{"id":9},"created_at":"2026-11-04T10:00:00Z","body":"private fixture text"}]""",
                "branches" => JsonSerializer.Serialize(new[] { new { name = BranchName, commit = new { sha = "activity-sha" } } }),
                "commits" => JsonSerializer.Serialize(new[] { new { sha = "activity-sha", commit = new { message = "fixture", committer = new { date = "2026-11-04T10:00:00Z" } }, author = new { id = CommitVersionAcrossPages && page > 1 ? CommitAuthorId + 1 : CommitAuthorId, login = "fixture" } } }),
                "pull-requests" => """[{"id":7001,"number":7,"state":"open","updated_at":"2026-11-04T10:00:00Z","user":{"id":9,"login":"fixture"}}]""",
                "pull-request-commits" when IncludeContributionRelationships => """[{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","author":{"id":9},"commit":{"committer":{"date":"2026-11-04T10:00:00Z"}}}]""",
                "issue-events" when IncludeContributionRelationships => """[{"id":9001,"event":"closed","created_at":"2026-11-04T10:00:00Z","actor":{"id":9}}]""",
                "issues" => """[{"id":8001,"number":8,"state":"open","created_at":"2026-11-04T10:00:00Z","user":{"id":9,"login":"fixture"}}]""",
                _ => "[]"
            };
            using var json = JsonDocument.Parse(value);
            if (category == "branches" && BeforePageReturn is { } pageGate && (!GateOnlyFirstPage || Interlocked.Increment(ref gatedPages) == 1)) await pageGate.WaitAsync(ct);
            return new GitHubSyncPage(json.RootElement.EnumerateArray().Select(x => x.Clone()).ToArray(), page == 1 && (category == "branches" && DuplicateBranchAcrossPages || category == "commits" && CommitVersionAcrossPages), 5000, null);
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
    private sealed class OAuthFixtureProtector : IGitHubTokenProtector
    {
        public string Protect(string token) => token;
        public string Unprotect(string token) => token;
    }
    private sealed class GrantRestoreClient(Fixture f) : IGitHubAppUserClient
    {
        public Task<GitHubAppUserToken> ExchangeAsync(string code, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubAppUserIdentity> GetUserAsync(string token, CancellationToken ct) => Task.FromResult(new GitHubAppUserIdentity(9, "fixture"));
        public Task<GitHubAppUserPage<GitHubAppUserInstallation>> GetInstallationsAsync(string token, int page, CancellationToken ct) =>
            Task.FromResult(new GitHubAppUserPage<GitHubAppUserInstallation>([new(f.Installation.ProviderInstallationId, 9, "fixture", "User", false)], false));
        public Task<GitHubAppUserPage<GitHubAppUserRepository>> GetRepositoriesAsync(string token, long id, int page, CancellationToken ct) =>
            Task.FromResult(new GitHubAppUserPage<GitHubAppUserRepository>([new(f.Repository.ProviderRepositoryId, "fixture/repo", true, true, true)], false));
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
            Assert.Equal(Database, connection.Database); Assert.Equal("127.0.0.1", connection.Host); Assert.Equal(Port, connection.Port);
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

    [Step4cPostgresTheory]
    [InlineData("lease")]
    [InlineData("unlink")]
    [InlineData("access_lost")]
    [InlineData("pending")]
    [InlineData("grant_revoked")]
    [InlineData("installation_suspended")]
    public async Task PersistedLeaseOrAuthorizationChangesBeforePageTransactionRejectWrites(string change)
    {
        await using var f = await Fixture.CreateAsync();
        var pending = new GitSyncJob { ProjectGitRepositoryId = f.Links[0].Id, GitHubRepositoryId = f.Repository.Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id, SyncType = GitSyncType.Initial };
        await using (var seed = Open()) { seed.GitSyncJobs.Add(pending); await seed.SaveChangesAsync(); }
        GitSyncJob job;
        await using (var claim = Open()) job = (await new GitSyncJobService(claim, f.Sync, f.App).ClaimNextAsync(TimeSpan.FromMinutes(2), default))!;
        Assert.Equal(pending.Id, job.Id);
        var gate = new Gate(); f.Provider.BeforePageReturn = gate;
        await using var processing = Open();
        var processor = new GitHubSyncJobProcessor(processing, f.Provider, new GitHubCredentialService(processing, f.Provider, new Protector()), f.Sync, f.Tokens, f.App);
        var run = processor.ProcessAsync(job, default);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await using var mutate = Open();
            switch (change)
            {
                case "lease": await mutate.GitSyncJobs.Where(x => x.Id == job.Id).ExecuteUpdateAsync(x => x.SetProperty(j => j.LeaseExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1))); break;
                case "unlink": await mutate.ProjectGitRepositories.Where(x => x.Id == f.Links[0].Id).ExecuteUpdateAsync(x => x.SetProperty(l => l.UnlinkedAt, DateTimeOffset.UtcNow.AddSeconds(1))); break;
                case "access_lost": await mutate.ProjectGitRepositories.Where(x => x.Id == f.Links[0].Id).ExecuteUpdateAsync(x => x.SetProperty(l => l.AccessStatus, ProjectGitRepositoryAccessStatus.AccessLost)); break;
                case "pending": await mutate.ProjectGitRepositories.Where(x => x.Id == f.Links[0].Id).ExecuteUpdateAsync(x => x.SetProperty(l => l.AccessStatus, ProjectGitRepositoryAccessStatus.PendingAuthorization).SetProperty(l => l.SyncPaused, true)); break;
                case "grant_revoked": await mutate.Set<GitHubAppRepositoryGrant>().Where(x => x.Id == f.Grant.Id).ExecuteUpdateAsync(x => x.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow)); break;
                case "installation_suspended": await mutate.Set<GitHubAppInstallation>().Where(x => x.Id == f.Installation.Id).ExecuteUpdateAsync(x => x.SetProperty(i => i.Status, GitHubAppInstallationStatus.Suspended)); break;
            }
        }
        finally { gate.Release.TrySetResult(); }
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => run);
        Assert.Equal(change == "lease" ? "GITHUB_SYNC_LEASE_LOST" : change == "pending" ? "GITHUB_APP_PENDING_ACTIVATION" : "GITHUB_REPOSITORY_NOT_AUTHORIZED", error.Code);
        await using var verify = Open();
        Assert.Empty(await verify.GitSyncCheckpoints.Where(x => x.SyncJobId == job.Id).ToListAsync());
        Assert.Empty(await verify.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == job.Id).ToListAsync());
        Assert.Empty(await verify.GitHubProviderObjects.Where(x => x.GitHubRepositoryId == f.Repository.Id).ToListAsync());
        Assert.Empty(await verify.ProjectGitActivityGrants.Where(x => x.ProjectGitRepositoryId == f.Links[0].Id).ToListAsync());
        Assert.Empty(await verify.ProjectGitActivityVisibilities.Where(x => x.ProjectGitRepositoryId == f.Links[0].Id).ToListAsync());
        Assert.Equal(GitSyncJobStatus.Running, (await verify.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id)).Status);
        if (change == "lease")
        {
            await using var reclaimDb = Open(); var jobs = new GitSyncJobService(reclaimDb, f.Sync, f.App);
            var reclaimed = await jobs.ClaimNextAsync(TimeSpan.FromMinutes(2), default);
            Assert.NotNull(reclaimed); Assert.Equal(job.Id, reclaimed.Id); Assert.NotEqual(job.LeaseId, reclaimed.LeaseId);
            await new GitHubSyncJobProcessor(reclaimDb, f.Provider, new GitHubCredentialService(reclaimDb, f.Provider, new Protector()), f.Sync, f.Tokens, f.App).ProcessAsync(reclaimed, default);
            await jobs.CompleteAsync(reclaimed.Id, reclaimed.LeaseId!.Value, default);
            Assert.Equal(GitSyncJobStatus.Completed, (await reclaimDb.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id)).Status);
            Assert.NotEmpty(await reclaimDb.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == job.Id).ToListAsync());
        }
    }

    [Step4cPostgresTheory]
    [InlineData("grant")]
    [InlineData("unlink")]
    [InlineData("access_lost")]
    [InlineData("pending")]
    [InlineData("suspend")]
    public async Task AuthorizationChangesWaitForInTransactionPageCommit(string change)
    {
        await using var f = await Fixture.CreateAsync();
        var pending = new GitSyncJob { ProjectGitRepositoryId = f.Links[0].Id, GitHubRepositoryId = f.Repository.Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id, SyncType = GitSyncType.Initial };
        await using (var seed = Open()) { seed.Add(pending); await seed.SaveChangesAsync(); }
        GitSyncJob job;
        await using (var claim = Open()) job = (await new GitSyncJobService(claim, f.Sync, f.App).ClaimNextAsync(TimeSpan.FromMinutes(2), default))!;
        Assert.Equal(pending.Id, job.Id);
        var gate = new Gate(); var barrier = new EvidenceLookupGate(gate);
        await using var processing = Open(barrier);
        var processor = new GitHubSyncJobProcessor(processing, f.Provider, new GitHubCredentialService(processing, f.Provider, new Protector()), f.Sync, f.Tokens, f.App);
        var run = processor.ProcessAsync(job, default);
        PostgresException? lockError = null;
        var (sql, rowId) = change switch
        {
            "grant" => ("UPDATE \"GitHubAppRepositoryGrants\" SET \"RevokedAt\" = now() WHERE \"Id\" = @id", f.Grant.Id),
            "unlink" => ("UPDATE \"ProjectGitRepositories\" SET \"UnlinkedAt\" = now() + interval '1 second' WHERE \"Id\" = @id", f.Links[0].Id),
            "access_lost" => ("UPDATE \"ProjectGitRepositories\" SET \"AccessStatus\" = 2 WHERE \"Id\" = @id", f.Links[0].Id),
            "pending" => ("UPDATE \"ProjectGitRepositories\" SET \"AccessStatus\" = 0, \"SyncPaused\" = true WHERE \"Id\" = @id", f.Links[0].Id),
            "suspend" => ("UPDATE \"GitHubAppInstallations\" SET \"Status\" = 1 WHERE \"Id\" = @id", f.Installation.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await using var other = new NpgsqlConnection(Connection); await other.OpenAsync();
            await using var tx = await other.BeginTransactionAsync();
            await using var command = new NpgsqlCommand("SET LOCAL lock_timeout = '500ms'; " + sql, other, tx);
            command.Parameters.AddWithValue("id", rowId);
            try { await command.ExecuteNonQueryAsync(); }
            catch (PostgresException error) { lockError = error; }
            await tx.RollbackAsync();
        }
        finally { gate.Release.TrySetResult(); }
        await run;
        Assert.NotNull(lockError); Assert.Equal("55P03", lockError!.SqlState);
        await using (var afterCommit = new NpgsqlConnection(Connection))
        {
            await afterCommit.OpenAsync(); await using var command = new NpgsqlCommand(sql, afterCommit);
            command.Parameters.AddWithValue("id", rowId); Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        await using var verify = Open();
        Assert.NotEmpty(await verify.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == job.Id).ToListAsync());
        Assert.True(await verify.GitSyncCheckpoints.AnyAsync(x => x.SyncJobId == job.Id && x.Status == GitSyncCheckpointStatus.Completed));
    }

    [Step4cPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OAuthCredentialReplacementIsRejectedOrWaitsForPageCommit(bool duringTransaction)
    {
        await using var f = await Fixture.CreateAsync();
        f.Provider.AcceptReplacementToken = true;
        var connection = new GitHubOAuthConnection { AccountId = f.Owner.Id, ProviderAccountId = "fixture-9", Login = "fixture",
            AccessTokenEncrypted = "installation-scoped-fixture", GrantedScopes = "repo" };
        var job = new GitSyncJob { ProjectGitRepositoryId = f.Links[0].Id, GitHubRepositoryId = f.Repository.Id,
            CredentialSource = GitRepositoryCredentialSource.OAuth, OAuthConnectionId = connection.Id, SyncType = GitSyncType.Initial };
        await using (var seed = Open())
        {
            seed.GitHubOAuthConnections.Add(connection); await seed.SaveChangesAsync();
            await seed.ProjectGitRepositories.Where(x => x.Id == f.Links[0].Id).ExecuteUpdateAsync(x => x
                .SetProperty(l => l.CredentialSource, GitRepositoryCredentialSource.OAuth)
                .SetProperty(l => l.GitHubAppRepositoryGrantId, (Guid?)null).SetProperty(l => l.OAuthConnectionId, connection.Id));
            seed.GitSyncJobs.Add(job); await seed.SaveChangesAsync();
        }
        GitSyncJob claimed;
        await using (var db = Open()) claimed = (await new GitSyncJobService(db, f.Sync, f.App).ClaimNextAsync(TimeSpan.FromMinutes(2), default))!;
        Assert.Equal(job.Id, claimed.Id);
        var gate = new Gate(); if (!duringTransaction) f.Provider.BeforePageReturn = gate;
        await using var processing = duringTransaction ? Open(new EvidenceLookupGate(gate)) : Open();
        var run = new GitHubSyncJobProcessor(processing, f.Provider, new GitHubCredentialService(processing, f.Provider, new OAuthFixtureProtector()), f.Sync, f.Tokens, f.App).ProcessAsync(claimed, default);
        PostgresException? lockError = null;
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            if (duringTransaction)
            {
                await using var other = new NpgsqlConnection(Connection); await other.OpenAsync(); await using var tx = await other.BeginTransactionAsync();
                await using var command = new NpgsqlCommand("SET LOCAL lock_timeout = '500ms'; UPDATE \"GitHubOAuthConnections\" SET \"Status\" = 1 WHERE \"Id\" = @id", other, tx);
                command.Parameters.AddWithValue("id", connection.Id);
                try { await command.ExecuteNonQueryAsync(); } catch (PostgresException pgError) { lockError = pgError; }
                await tx.RollbackAsync();
            }
            else
            {
                await using var replace = Open();
                await replace.GitHubOAuthConnections.Where(x => x.Id == connection.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.AccessTokenEncrypted, "replacement-fixture").SetProperty(x => x.Version, Guid.NewGuid()));
            }
        }
        finally { gate.Release.TrySetResult(); }
        if (duringTransaction)
        {
            await run; Assert.NotNull(lockError); Assert.Equal("55P03", lockError!.SqlState);
            return;
        }
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => run);
        Assert.Equal("GITHUB_SYNC_CREDENTIAL_CHANGED", error.Code);
        await using var verify = Open();
        Assert.Empty(await verify.GitSyncCheckpoints.Where(x => x.SyncJobId == job.Id).ToListAsync());
        Assert.Empty(await verify.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == job.Id).ToListAsync());
        Assert.Empty(await verify.GitHubProviderObjects.Where(x => x.GitHubRepositoryId == f.Repository.Id).ToListAsync());
    }

    [Step4cPostgresFact]
    public async Task HostedWorkersFenceExpiredLeaseForSameJob()
    {
        await using var f = await Fixture.CreateAsync();
        var pending = new GitSyncJob { ProjectGitRepositoryId = f.Links[0].Id, GitHubRepositoryId = f.Repository.Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id, SyncType = GitSyncType.Initial };
        await using (var seed = Open())
        {
            seed.Add(pending); await seed.SaveChangesAsync();
            await seed.ProjectGitRepositories.Where(x => x.Id == f.Links[3].Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SyncPaused, true));
            await seed.Set<GitHubAppInstallation>().Where(x => x.Id == f.Installation.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextReconciliationAt, DateTimeOffset.UtcNow.AddHours(1)));
        }
        var gate = new Gate(); f.Provider.BeforePageReturn = gate; f.Provider.GateOnlyFirstPage = true;
        // This test's 25s deadline must include a genuine transaction-conflict retry;
        // the production default backoff starts at 30s, beyond that deadline.
        var sync = Options.Create(new GitSyncOptions { Enabled = true, WorkerConcurrency = 1, PollSeconds = 1,
            ReconciliationPollSeconds = 300, JobLeaseDurationSeconds = 30, RetryBaseDelaySeconds = 1, MaxRetryAttempts = 3 });
        var services = new ServiceCollection(); using var http = new HttpClient(f.Http, false);
        services.AddDbContext<UniNetDbContext>(o => o.UseNpgsql(Connection));
        services.AddSingleton<IOptions<GitSyncOptions>>(sync); services.AddSingleton<IOptions<GitHubAppOptions>>(f.App);
        services.AddSingleton<IGitHubTokenProtector>(f.Protection); services.AddSingleton<IGitHubAppInstallationTokenProvider>(f.Tokens);
        services.AddSingleton<IGitHubProviderClient>(f.Provider); services.AddScoped<GitHubCredentialService>();
        services.AddScoped(sp => new GitHubAppInstallationReconciliationService(sp.GetRequiredService<UniNetDbContext>(), http, f.App, sync, f.Tokens));
        services.AddScoped<GitHubAppRepositoryActivationService>(); services.AddScoped<GitHubAppWebhookConsumer>();
        services.AddScoped<IGitSyncJobService, GitSyncJobService>(); services.AddScoped<IGitSyncJobProcessor, GitHubSyncJobProcessor>();
        await using var container = services.BuildServiceProvider();
        var leaseLogger = new LeaseLossLogger();
        using var first = new GitSyncWorker(container.GetRequiredService<IServiceScopeFactory>(), sync, leaseLogger, f.App);
        using var second = new GitSyncWorker(container.GetRequiredService<IServiceScopeFactory>(), sync, NullLogger<GitSyncWorker>.Instance, f.App);
        try
        {
            await first.StartAsync(default); await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Guid? oldLease;
            await using (var db = Open())
            {
                oldLease = (await db.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == pending.Id)).LeaseId; Assert.NotNull(oldLease);
                Assert.Equal(1, await db.GitSyncJobs.Where(x => x.Id == pending.Id && x.LeaseId == oldLease).ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1))));
            }
            await second.StartAsync(default); var deadline = DateTimeOffset.UtcNow.AddSeconds(25);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await using var check = Open(); if ((await check.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == pending.Id)).Status == GitSyncJobStatus.Completed) break;
                await Task.Delay(100);
            }
            await second.StopAsync(default);
            await using (var check = Open())
            {
                Assert.Equal(GitSyncJobStatus.Completed, (await check.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == pending.Id)).Status);
                Assert.Equal(1, await check.ProjectEvidenceObservations.CountAsync(x => x.GitSyncJobId == pending.Id && x.Category == "branches"));
            }
            gate.Release.TrySetResult();
            await leaseLogger.Seen.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally { gate.Release.TrySetResult(); await first.StopAsync(default); await second.StopAsync(default); }
        await using var verify = Open();
        Assert.Equal(GitSyncJobStatus.Completed, (await verify.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == pending.Id)).Status);
        Assert.Equal(1, await verify.ProjectEvidenceObservations.CountAsync(x => x.GitSyncJobId == pending.Id && x.Category == "branches"));
        Assert.Equal(1, await verify.GitSyncCheckpoints.CountAsync(x => x.SyncJobId == pending.Id && x.Category == "branches"));
        var branchId = await verify.GitHubProviderObjects.Where(x => x.GitHubRepositoryId == f.Repository.Id && x.Category == "branches").Select(x => x.Id).SingleAsync();
        Assert.Equal(1, await verify.GitEvidenceVersions.CountAsync(x => x.GitHubProviderObjectId == branchId));
    }

    [Step4cPostgresFact]
    public async Task RoutineGrantVerificationDuringFetchDoesNotChangeGenerationOrRejectPage()
    {
        await using var f = await Fixture.CreateAsync();
        var job = new GitSyncJob { ProjectGitRepositoryId = f.Links[0].Id, GitHubRepositoryId = f.Repository.Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id, SyncType = GitSyncType.Initial };
        long initial;
        await using (var seed = Open()) { seed.Add(job); await seed.SaveChangesAsync(); initial = (await seed.Set<GitHubAppRepositoryGrant>().AsNoTracking().SingleAsync(x => x.Id == f.Grant.Id)).AuthorizationGeneration; }
        GitSyncJob claimed;
        await using (var db = Open()) claimed = (await new GitSyncJobService(db, f.Sync, f.App).ClaimNextAsync(TimeSpan.FromMinutes(2), default))!;
        Assert.Equal(job.Id, claimed.Id);
        var gate = new Gate(); f.Provider.BeforePageReturn = gate;
        await using var processing = Open();
        var run = new GitHubSyncJobProcessor(processing, f.Provider, new GitHubCredentialService(processing, f.Provider, new Protector()), f.Sync, f.Tokens, f.App).ProcessAsync(claimed, default);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await using var mutate = Open();
            await mutate.Set<GitHubAppRepositoryGrant>().Where(x => x.Id == f.Grant.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.VerifiedAt, DateTimeOffset.UtcNow));
        }
        finally { gate.Release.TrySetResult(); }
        await run;
        await using var verify = Open();
        Assert.Equal(initial, (await verify.Set<GitHubAppRepositoryGrant>().AsNoTracking().SingleAsync(x => x.Id == f.Grant.Id)).AuthorizationGeneration);
        Assert.NotEmpty(await verify.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == job.Id).ToListAsync());
    }

    [Step4cPostgresFact]
    public async Task InstallationSuspendedAndRestoredDuringFetchRejectsStalePage()
    {
        await using var f = await Fixture.CreateAsync();
        var job = new GitSyncJob { ProjectGitRepositoryId = f.Links[0].Id, GitHubRepositoryId = f.Repository.Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id, SyncType = GitSyncType.Initial };
        long initial;
        await using (var seed = Open()) { seed.Add(job); await seed.SaveChangesAsync(); initial = (await seed.Set<GitHubAppInstallation>().AsNoTracking().SingleAsync(x => x.Id == f.Installation.Id)).AuthorizationGeneration; }
        GitSyncJob claimed;
        await using (var db = Open()) claimed = (await new GitSyncJobService(db, f.Sync, f.App).ClaimNextAsync(TimeSpan.FromMinutes(2), default))!;
        Assert.Equal(job.Id, claimed.Id);
        var gate = new Gate(); f.Provider.BeforePageReturn = gate;
        await using var processing = Open();
        var run = new GitHubSyncJobProcessor(processing, f.Provider, new GitHubCredentialService(processing, f.Provider, new Protector()), f.Sync, f.Tokens, f.App).ProcessAsync(claimed, default);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await using var mutate = Open();
            await mutate.Set<GitHubAppInstallation>().Where(x => x.Id == f.Installation.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, GitHubAppInstallationStatus.Suspended));
            await mutate.Set<GitHubAppInstallation>().Where(x => x.Id == f.Installation.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, GitHubAppInstallationStatus.Active));
        }
        finally { gate.Release.TrySetResult(); }
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => run); Assert.Equal("GITHUB_SYNC_AUTHORIZATION_CHANGED", error.Code);
        await using var verify = Open();
        Assert.Equal(initial + 2, (await verify.Set<GitHubAppInstallation>().AsNoTracking().SingleAsync(x => x.Id == f.Installation.Id)).AuthorizationGeneration);
        Assert.Empty(await verify.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == job.Id).ToListAsync());
        Assert.Empty(await verify.GitSyncCheckpoints.Where(x => x.SyncJobId == job.Id).ToListAsync());
    }

    [Step4cPostgresFact]
    public async Task UnrelatedUnsupportedEvidenceVersionsAreNotSelectedForReuse()
    {
        await using var f = await Fixture.CreateAsync();
        var branch = new GitHubProviderObject { GitHubRepositoryId = f.Repository.Id, Category = "branches", ExternalObjectId = "main",
            PayloadJson = "{\"name\":\"main\"}", RetrievalMetadataJson = "{}", FirstObservedAt = DateTimeOffset.UtcNow, LastObservedAt = DateTimeOffset.UtcNow };
        using var item = JsonDocument.Parse("""{"name":"main","commit":{"sha":"activity-sha"}}""");
        var canonical = GitFrozenFactsCanonicalizer.Canonicalize(GitEvidenceFactsExtractor.Extract("branches", "main", item.RootElement));
        GitEvidenceVersion Unsupported(int schema, int canonicalVersion) => new() { GitHubProviderObjectId = branch.Id, GitHubRepositoryId = f.Repository.Id,
            EvidenceSchemaVersion = schema, CanonicalizationVersion = canonicalVersion, ContentHash = canonical.Sha256,
            FrozenFactsJson = Encoding.UTF8.GetString(canonical.Bytes), EvidenceStatus = "Incomplete" };
        var unsupportedSchema = Unsupported(3, 1); var unsupportedCanonical = Unsupported(1, 2);
        await using (var seed = Open()) { seed.AddRange(branch, unsupportedSchema, unsupportedCanonical); await seed.SaveChangesAsync(); }
        await f.StageAsync("push"); Assert.True(await f.ConsumeAsync()); await f.ProcessJobsAsync();
        await using var verify = Open();
        var versions = await verify.GitEvidenceVersions.Where(x => x.GitHubProviderObjectId == branch.Id).ToListAsync(); Assert.Equal(3, versions.Count);
        var supported = Assert.Single(versions, x => x.EvidenceSchemaVersion == 2 && x.CanonicalizationVersion == 1);
        Assert.False(await verify.ProjectEvidenceObservations.AnyAsync(x => x.GitEvidenceVersionId == unsupportedSchema.Id || x.GitEvidenceVersionId == unsupportedCanonical.Id));
        Assert.Equal(2, await verify.ProjectEvidenceObservations.CountAsync(x => x.GitEvidenceVersionId == supported.Id));
    }

    [Step4cPostgresFact]
    public async Task FailedSecondPagePreservesFirstCheckpointAndRetryCompletesEvidence()
    {
        await using var f = await Fixture.CreateAsync(); f.Provider.CommitVersionAcrossPages = true;
        await f.StageAsync("push"); Assert.True(await f.ConsumeAsync());
        GitSyncJob job;
        await using (var db = Open()) job = (await new GitSyncJobService(db, f.Sync, f.App).ClaimNextAsync(TimeSpan.FromMinutes(2), default))!;
        Assert.NotNull(job); var injection = new InterruptSecondCommitPage();
        await using (var failing = Open(injection))
        {
            var processor = new GitHubSyncJobProcessor(failing, f.Provider, new GitHubCredentialService(failing, f.Provider, new Protector()), f.Sync, f.Tokens, f.App);
            await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(job, default));
        }
        Assert.True(injection.Hit);
        await using (var check = Open())
        {
            var cp = await check.GitSyncCheckpoints.SingleAsync(x => x.SyncJobId == job.Id && x.Category == "commits");
            Assert.Equal("2", cp.Cursor); Assert.Equal(GitSyncCheckpointStatus.InProgress, cp.Status);
            Assert.Single(await check.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == job.Id && x.Category == "commits").ToListAsync());
            var sourceId = await check.GitHubProviderObjects.Where(x => x.GitHubRepositoryId == f.Repository.Id && x.Category == "commits").Select(x => x.Id).SingleAsync();
            Assert.Single(await check.GitEvidenceVersions.Where(x => x.GitHubProviderObjectId == sourceId).ToListAsync());
        }
        await using (var retry = Open())
            await new GitHubSyncJobProcessor(retry, f.Provider, new GitHubCredentialService(retry, f.Provider, new Protector()), f.Sync, f.Tokens, f.App).ProcessAsync(job, default);
        await using var final = Open();
        Assert.Equal(2, await final.ProjectEvidenceObservations.CountAsync(x => x.GitSyncJobId == job.Id && x.Category == "commits"));
        Assert.Equal(GitSyncCheckpointStatus.Completed, (await final.GitSyncCheckpoints.SingleAsync(x => x.SyncJobId == job.Id && x.Category == "commits")).Status);
    }

    [Step4cPostgresFact]
    public async Task ChangedFactsAcrossPagesCreateTwoVersionsForSameJobScope()
    {
        await using var f = await Fixture.CreateAsync(); f.Provider.CommitVersionAcrossPages = true;
        await f.StageAsync("push"); Assert.True(await f.ConsumeAsync()); await f.ProcessJobsAsync();
        await using var db = Open();
        var source = await db.GitHubProviderObjects.SingleAsync(x => x.GitHubRepositoryId == f.Repository.Id && x.Category == "commits");
        var versions = await db.GitEvidenceVersions.Where(x => x.GitHubProviderObjectId == source.Id).ToListAsync(); Assert.Equal(2, versions.Count);
        Assert.All(versions, version => { Assert.Equal(4, version.EvidenceSchemaVersion);
            Assert.Equal(GitSnapshotEvidenceValidation.Valid, GitSnapshotEvidenceValidator.Validate(version, source));
            Assert.NotNull(JsonSerializer.Deserialize<GitFrozenFacts>(version.FrozenFactsJson)!.Details); });
        var jobs = await db.GitSyncJobs.Where(x => x.GitHubRepositoryId == f.Repository.Id).ToListAsync(); Assert.Equal(2, jobs.Count);
        foreach (var job in jobs)
        {
            var observations = await db.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == job.Id && x.Category == "commits" && x.ScopeKey == "main").ToListAsync();
            Assert.Equal(2, observations.Count); Assert.Equal(2, observations.Select(x => x.GitEvidenceVersionId).Distinct().Count());
            Assert.Equal("2", (await db.GitSyncCheckpoints.SingleAsync(x => x.SyncJobId == job.Id && x.Category == "commits")).Cursor);
        }
    }

    [Step4cPostgresFact]
    public async Task RepeatedItemAcrossPagesUsesOneObservationAndPreservesScope()
    {
        await using var f = await Fixture.CreateAsync(); f.Provider.DuplicateBranchAcrossPages = true; f.Provider.BranchName = " feature ";
        await f.StageAsync("push"); Assert.True(await f.ConsumeAsync()); await f.ProcessJobsAsync();
        await using var db = Open();
        var jobs = await db.GitSyncJobs.Where(x => x.GitHubRepositoryId == f.Repository.Id).ToListAsync(); Assert.Equal(2, jobs.Count);
        Assert.All(jobs, x => Assert.Equal(GitSyncJobStatus.Completed, x.Status));
        foreach (var job in jobs)
        {
            Assert.Single(await db.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == job.Id && x.Category == "branches" && x.ScopeKey == "").ToListAsync());
            Assert.Single(await db.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == job.Id && x.Category == "commits" && x.ScopeKey == " feature ").ToListAsync());
            Assert.Equal("2", (await db.GitSyncCheckpoints.SingleAsync(x => x.SyncJobId == job.Id && x.Category == "branches")).Cursor);
        }
        var branch = await db.GitHubProviderObjects.SingleAsync(x => x.GitHubRepositoryId == f.Repository.Id && x.Category == "branches");
        Assert.Single(await db.GitEvidenceVersions.Where(x => x.GitHubProviderObjectId == branch.Id).ToListAsync());
    }

    [Step4cPostgresFact]
    public async Task PullCommitAndIssueEventsFlowThroughImmutableProjectEvidence()
    {
        await using var f = await Fixture.CreateAsync(); f.Provider.IncludeContributionRelationships = true;
        await f.StageAsync("push"); Assert.True(await f.ConsumeAsync()); await f.ProcessJobsAsync();
        await using var db = Open();
        foreach (var (category, scope) in new[] { ("pull-request-commits", "7"), ("issue-events", "8") })
        {
            var source = await db.GitHubProviderObjects.SingleAsync(x => x.GitHubRepositoryId == f.Repository.Id && x.Category == category);
            var version = await db.GitEvidenceVersions.SingleAsync(x => x.GitHubProviderObjectId == source.Id && x.EvidenceSchemaVersion == 4);
            Assert.Equal(GitSnapshotEvidenceValidation.Valid, GitSnapshotEvidenceValidator.Validate(version, source));
            var facts = JsonSerializer.Deserialize<GitFrozenFacts>(version.FrozenFactsJson)!;
            Assert.Equal("9", Assert.Single(facts.Actors).ProviderAccountId);
            if (category == "pull-request-commits") { Assert.Equal(7, facts.Details!.RelatedPullRequestNumber); Assert.NotNull(facts.CommitterDate); }
            else { Assert.Equal(8, facts.Details!.RelatedIssueNumber); Assert.Equal("closed", facts.Details.EventKind); Assert.NotNull(facts.Details.CreatedAt); }
            var observations = await db.ProjectEvidenceObservations.Where(x => x.GitEvidenceVersionId == version.Id && x.ScopeKey == scope).ToListAsync();
            Assert.Equal(2, observations.Count); Assert.Equal(2, observations.Select(x => x.ProjectGitRepositoryId).Distinct().Count());
        }
        await using var captureDb = Open();
        var snapshotId = await new ProjectGitEvidenceSnapshotService(captureDb).CaptureAsync(f.Projects[0].Id, f.Owner.Id, DateTimeOffset.UtcNow, default);
        await using var readerDb = Open();
        var snapshot = await new ProjectGitEvidenceSnapshotReader(readerDb).ReadAsync(f.Projects[0].Id, snapshotId, f.Owner.Id, default);
        Assert.Contains(snapshot.Entries, x => x.Facts?.Category == "pull-request-commits" && x.Facts.Details!.RelatedPullRequestNumber == 7 && x.Status == SnapshotEvidenceStatus.Incomplete);
        Assert.Contains(snapshot.Entries, x => x.Facts?.Category == "issue-events" && x.Facts.Details!.RelatedIssueNumber == 8 &&
            x.Facts.Completeness == "Complete" && x.Status == SnapshotEvidenceStatus.Valid);
    }

    [Step4cPostgresFact]
    public async Task IssueCommentV5FlowsThroughAtomicWriterAndAuthorizedSnapshot()
    {
        await using var f = await Fixture.CreateAsync();
        f.Provider.IncludeContributionRelationships = true; f.Provider.IncludeIssueComments = true;
        await f.StageAsync("push"); Assert.True(await f.ConsumeAsync()); await f.ProcessJobsAsync();
        await using var db = Open();
        var source = await db.GitHubProviderObjects.SingleAsync(x => x.GitHubRepositoryId == f.Repository.Id && x.Category == "issue-comments");
        var version = await db.GitEvidenceVersions.SingleAsync(x => x.GitHubProviderObjectId == source.Id);
        Assert.Equal(5, version.EvidenceSchemaVersion); Assert.Equal("Complete", version.EvidenceStatus);
        Assert.Equal(GitSnapshotEvidenceValidation.Valid, GitSnapshotEvidenceValidator.Validate(version, source));
        Assert.DoesNotContain("private fixture text", version.FrozenFactsJson);
        Assert.Equal(2, await db.ProjectEvidenceObservations.CountAsync(x => x.GitEvidenceVersionId == version.Id));
        var id = await new ProjectGitEvidenceSnapshotService(db).CaptureAsync(f.Projects[0].Id, f.Owner.Id, DateTimeOffset.UtcNow, default);
        var read = await new ProjectGitEvidenceSnapshotReader(db).ReadAsync(f.Projects[0].Id, id, f.Owner.Id, default);
        var entry = Assert.Single(read.Entries, x => x.Category == "issue-comments");
        Assert.Equal(SnapshotEvidenceStatus.Valid, entry.Status);
        Assert.Equal(8, entry.Facts!.Details!.RelatedIssueNumber);
        Assert.All(read.Entries, x => Assert.Equal(f.Links[0].Id, x.AssociationId));
    }

    [Step4cPostgresFact]
    public async Task ConcurrentGrantRevokeRestoreAdvancesGenerationAndRollbackDoesNot()
    {
        await using var f = await Fixture.CreateAsync();
        long initial;
        await using (var db = Open()) initial = (await db.Set<GitHubAppRepositoryGrant>().AsNoTracking().SingleAsync(x => x.Id == f.Grant.Id)).AuthorizationGeneration;
        await using var first = new NpgsqlConnection(Connection); await first.OpenAsync();
        await using var second = new NpgsqlConnection(Connection); await second.OpenAsync();
        await using var tx1 = await first.BeginTransactionAsync(); await using var tx2 = await second.BeginTransactionAsync();
        await using (var revoke = new NpgsqlCommand("UPDATE \"GitHubAppRepositoryGrants\" SET \"RevokedAt\"=now() WHERE \"Id\"=@id", first, tx1))
        { revoke.Parameters.AddWithValue("id", f.Grant.Id); Assert.Equal(1, await revoke.ExecuteNonQueryAsync()); }
        await using var restore = new NpgsqlCommand("UPDATE \"GitHubAppRepositoryGrants\" SET \"RevokedAt\"=NULL WHERE \"Id\"=@id", second, tx2);
        restore.Parameters.AddWithValue("id", f.Grant.Id); var blocked = restore.ExecuteNonQueryAsync();
        try
        {
            await using var observer = new NpgsqlConnection(Connection); await observer.OpenAsync();
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10); var isBlocked = false;
            while (DateTimeOffset.UtcNow < deadline)
            {
                await using var check = new NpgsqlCommand("SELECT cardinality(pg_blocking_pids(@pid))", observer);
                check.Parameters.AddWithValue("pid", second.ProcessID);
                if ((int)(await check.ExecuteScalarAsync())! > 0) { isBlocked = true; break; }
                await Task.Delay(25);
            }
            Assert.True(isBlocked); await tx1.CommitAsync(); Assert.Equal(1, await blocked); await tx2.CommitAsync();
        }
        finally { if (!blocked.IsCompleted) { await tx1.RollbackAsync(); try { await blocked; } catch (PostgresException) { } } }
        await using (var db = Open())
        {
            var grant = await db.Set<GitHubAppRepositoryGrant>().AsNoTracking().SingleAsync(x => x.Id == f.Grant.Id);
            Assert.Null(grant.RevokedAt); Assert.Equal(initial + 2, grant.AuthorizationGeneration);
            await using var rollback = await db.Database.BeginTransactionAsync();
            await db.Set<GitHubAppRepositoryGrant>().Where(x => x.Id == f.Grant.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
            await rollback.RollbackAsync();
        }
        await using var verify = Open(); var final = await verify.Set<GitHubAppRepositoryGrant>().AsNoTracking().SingleAsync(x => x.Id == f.Grant.Id);
        Assert.Null(final.RevokedAt); Assert.Equal(initial + 2, final.AuthorizationGeneration);
    }

    [Step4cPostgresFact]
    public async Task SameGrantRevokedAndRestoredDuringFetchRejectsStalePage()
    {
        await using var f = await Fixture.CreateAsync(); await f.StageAsync("push"); Assert.True(await f.ConsumeAsync()); await f.ProcessJobsAsync();
        long originalGeneration;
        await using (var baseline = Open()) originalGeneration = (await baseline.Set<GitHubAppRepositoryGrant>().AsNoTracking().SingleAsync(x => x.Id == f.Grant.Id)).AuthorizationGeneration;
        var credential = new GitHubAppUserCredential { AccountId = f.Owner.Id, GitHubUserId = 9, Login = "fixture", AccessTokenEncrypted = "fixture-user-token" };
        var job = new GitSyncJob { ProjectGitRepositoryId = f.Links[0].Id, GitHubRepositoryId = f.Repository.Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id, SyncType = GitSyncType.Manual };
        Guid priorObservation; string priorFacts;
        await using (var seed = Open())
        {
            seed.AddRange(credential, job); await seed.SaveChangesAsync();
            var observation = await seed.ProjectEvidenceObservations.AsNoTracking().FirstAsync(x => x.ProjectGitRepositoryId == f.Links[0].Id);
            priorObservation = observation.Id;
            priorFacts = (await seed.GitEvidenceVersions.AsNoTracking().SingleAsync(x => x.Id == observation.GitEvidenceVersionId)).FrozenFactsJson;
        }
        GitSyncJob claimed;
        await using (var claim = Open()) claimed = (await new GitSyncJobService(claim, f.Sync, f.App).ClaimNextAsync(TimeSpan.FromMinutes(2), default))!;
        Assert.Equal(job.Id, claimed.Id);
        var gate = new Gate(); f.Provider.BeforePageReturn = gate;
        await using var processing = Open();
        var run = new GitHubSyncJobProcessor(processing, f.Provider, new GitHubCredentialService(processing, f.Provider, new Protector()), f.Sync, f.Tokens, f.App).ProcessAsync(claimed, default);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await using (var revoke = Open())
                Assert.Equal(1, await revoke.Set<GitHubAppRepositoryGrant>().Where(x => x.Id == f.Grant.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow)));
            await using var restore = Open();
            await new GitHubAppGrantService(restore, new GrantRestoreClient(f), new OAuthFixtureProtector(), f.App)
                .GetRepositoriesAsync(f.Owner.Id, f.Projects[0].Id, credential.Id, f.Installation.ProviderInstallationId, 1, default);
            var grant = await restore.Set<GitHubAppRepositoryGrant>().AsNoTracking().SingleAsync(x => x.Id == f.Grant.Id);
            Assert.Null(grant.RevokedAt);
            Assert.Equal(originalGeneration + 2, grant.AuthorizationGeneration);
        }
        finally { gate.Release.TrySetResult(); }
        GitHubIntegrationException? rejection = null;
        try { await run; } catch (GitHubIntegrationException error) { rejection = error; }
        await using var verify = Open();
        var old = await verify.ProjectEvidenceObservations.AsNoTracking().SingleAsync(x => x.Id == priorObservation);
        Assert.Equal(priorFacts, (await verify.GitEvidenceVersions.AsNoTracking().SingleAsync(x => x.Id == old.GitEvidenceVersionId)).FrozenFactsJson);
        Assert.Equal(0, await verify.ProjectEvidenceObservations.CountAsync(x => x.GitSyncJobId == job.Id));
        Assert.Equal(0, await verify.GitSyncCheckpoints.CountAsync(x => x.SyncJobId == job.Id));
        Assert.NotNull(rejection); Assert.Equal("GITHUB_SYNC_AUTHORIZATION_CHANGED", rejection!.Code);
        var versionsBeforeRetry = await verify.GitEvidenceVersions.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id);
        await using (var retry = Open())
        {
            var jobs = new GitSyncJobService(retry, f.Sync, f.App);
            await jobs.FailAsync(job.Id, claimed.LeaseId!.Value, rejection.Code, "Authorization changed; retry.", DateTimeOffset.UtcNow.AddSeconds(-1), default);
        }
        await using (var retry = Open())
        {
            var jobs = new GitSyncJobService(retry, f.Sync, f.App); var next = await jobs.ClaimNextAsync(TimeSpan.FromMinutes(2), default);
            Assert.NotNull(next); Assert.Equal(job.Id, next.Id);
            await new GitHubSyncJobProcessor(retry, f.Provider, new GitHubCredentialService(retry, f.Provider, new Protector()), f.Sync, f.Tokens, f.App).ProcessAsync(next, default);
            await jobs.CompleteAsync(next.Id, next.LeaseId!.Value, default);
        }
        await using var recovered = Open();
        Assert.Equal(GitSyncJobStatus.Completed, (await recovered.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id)).Status);
        Assert.True(await recovered.ProjectEvidenceObservations.AnyAsync(x => x.GitSyncJobId == job.Id && x.ProjectGitRepositoryId == f.Links[0].Id));
        Assert.True(await recovered.GitSyncCheckpoints.AnyAsync(x => x.SyncJobId == job.Id && x.Status == GitSyncCheckpointStatus.Completed));
        Assert.Equal(versionsBeforeRetry, await recovered.GitEvidenceVersions.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id));
    }

    [Step4cPostgresFact]
    public async Task UnlinkServiceWaitsForAuthorizedPageAndPreservesItsEvidence()
    {
        await using var f = await Fixture.CreateAsync();
        var pending = new GitSyncJob { ProjectGitRepositoryId = f.Links[0].Id, GitHubRepositoryId = f.Repository.Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id, SyncType = GitSyncType.Initial };
        await using (var seed = Open()) { seed.GitSyncJobs.Add(pending); await seed.SaveChangesAsync(); }
        GitSyncJob job;
        await using (var claim = Open()) job = (await new GitSyncJobService(claim, f.Sync, f.App).ClaimNextAsync(TimeSpan.FromMinutes(2), default))!;
        Assert.Equal(pending.Id, job.Id);
        var gate = new Gate(); await using var processing = Open(new EvidenceLookupGate(gate));
        var run = new GitHubSyncJobProcessor(processing, f.Provider, new GitHubCredentialService(processing, f.Provider, new Protector()), f.Sync, f.Tokens, f.App).ProcessAsync(job, default);
        Exception? blocked = null;
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await using var unlinkDb = Open(); await unlinkDb.Database.OpenConnectionAsync();
            await unlinkDb.Database.ExecuteSqlRawAsync("SET lock_timeout = '500ms'");
            try { await new ProjectGitRepositoryService(unlinkDb, f.Provider, new GitHubCredentialService(unlinkDb, f.Provider, new Protector())).UnlinkAsync(f.Owner.Id, f.Projects[0].Id, f.Links[0].Id, default); }
            catch (Exception error) { blocked = error; }
        }
        finally { gate.Release.TrySetResult(); }
        await run;
        Assert.NotNull(blocked); Assert.Equal("55P03", Assert.IsType<PostgresException>(blocked!.GetBaseException()).SqlState);
        await using (var unlinkDb = Open())
            await new ProjectGitRepositoryService(unlinkDb, f.Provider, new GitHubCredentialService(unlinkDb, f.Provider, new Protector())).UnlinkAsync(f.Owner.Id, f.Projects[0].Id, f.Links[0].Id, default);
        await using var verify = Open();
        Assert.NotNull((await verify.ProjectGitRepositories.AsNoTracking().SingleAsync(x => x.Id == f.Links[0].Id)).UnlinkedAt);
        Assert.NotEmpty(await verify.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == job.Id).ToListAsync());
        Assert.NotEmpty(await verify.GitSyncCheckpoints.Where(x => x.SyncJobId == job.Id).ToListAsync());
    }

    [Step4cPostgresFact]
    public async Task ConnectorDeparturePreservesCommittedEvidenceAndAppAssociation()
    {
        await using var f = await Fixture.CreateAsync(); await f.StageAsync("push"); Assert.True(await f.ConsumeAsync()); await f.ProcessJobsAsync();
        var connector = new Account { Email = $"depart-{Guid.NewGuid():N}@example.test", Role = AccountRole.Student, Status = AccountStatus.Active,
            Profile = new UserProfile { FullName = "Connector", Nickname = "Connector" } };
        var member = new ProjectMember { ProjectId = f.Projects[0].Id, User = connector.Profile!, Role = "Member", Status = ProjectMemberStatus.Active, JoinedAt = DateTimeOffset.UtcNow };
        var identity = new ProjectGitMemberIdentity { ProjectId = f.Projects[0].Id, UserProfileId = connector.Profile!.Id, Provider = "github", ExternalAccountId = 987654,
            ExternalUsername = "fixture-connector", Status = ProjectGitMemberIdentityStatus.Active };
        Guid observationId; Guid versionId; string frozen;
        await using (var seed = Open())
        {
            seed.AddRange(connector, member, identity); await seed.SaveChangesAsync();
            await seed.ProjectGitRepositories.Where(x => x.Id == f.Links[0].Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LinkedByProfileId, connector.Profile!.Id));
            var observation = await seed.ProjectEvidenceObservations.AsNoTracking().FirstAsync(x => x.ProjectGitRepositoryId == f.Links[0].Id);
            observationId = observation.Id; versionId = observation.GitEvidenceVersionId;
            frozen = (await seed.GitEvidenceVersions.AsNoTracking().SingleAsync(x => x.Id == versionId)).FrozenFactsJson;
            await new ProjectMemberService(seed).LeaveAsync(f.Projects[0].Id, connector.Id);
        }
        await using (var verify = Open())
        {
            var link = await verify.ProjectGitRepositories.AsNoTracking().SingleAsync(x => x.Id == f.Links[0].Id);
            Assert.Null(link.UnlinkedAt); Assert.Equal(ProjectGitRepositoryAccessStatus.Active, link.AccessStatus);
            Assert.True(await verify.ProjectEvidenceObservations.AnyAsync(x => x.Id == observationId && x.GitEvidenceVersionId == versionId));
            Assert.Equal(frozen, (await verify.GitEvidenceVersions.AsNoTracking().SingleAsync(x => x.Id == versionId)).FrozenFactsJson);
            Assert.Equal(ProjectGitMemberIdentityStatus.Inactive, (await verify.ProjectGitMemberIdentities.AsNoTracking().SingleAsync(x => x.Id == identity.Id)).Status);
            Assert.True(await verify.ProjectGitMemberIdentityHistory.AnyAsync(x => x.IdentityId == identity.Id && x.Action == ProjectGitMemberIdentityHistoryAction.DeactivatedByDeparture));
            Assert.False(await verify.ProjectGitMemberIdentities.AnyAsync(x => x.ProjectId == f.Projects[0].Id && x.ExternalAccountId == identity.ExternalAccountId && x.Status == ProjectGitMemberIdentityStatus.Active));
            verify.GitSyncJobs.Add(new GitSyncJob { ProjectGitRepositoryId = link.Id, GitHubRepositoryId = f.Repository.Id,
                CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id, SyncType = GitSyncType.Scheduled });
            await verify.SaveChangesAsync();
        }
        await f.ProcessJobsAsync();
        await using var final = Open();
        Assert.True(await final.GitSyncJobs.AnyAsync(x => x.ProjectGitRepositoryId == f.Links[0].Id && x.SyncType == GitSyncType.Scheduled && x.Status == GitSyncJobStatus.Completed));
        Assert.True(await final.ProjectEvidenceObservations.AnyAsync(x => x.Id == observationId));
    }

    [Step4cPostgresFact]
    public async Task AccessLossBeforePagePreservesAnotherProjectsEvidence()
    {
        await using var f = await Fixture.CreateAsync(); await f.StageAsync("push"); Assert.True(await f.ConsumeAsync());
        GitSyncJob first;
        await using (var db = Open())
        {
            var jobs = new GitSyncJobService(db, f.Sync, f.App); first = (await jobs.ClaimNextAsync(TimeSpan.FromMinutes(2), default))!;
            await new GitHubSyncJobProcessor(db, f.Provider, new GitHubCredentialService(db, f.Provider, new Protector()), f.Sync, f.Tokens, f.App).ProcessAsync(first, default);
            await jobs.CompleteAsync(first.Id, first.LeaseId!.Value, default);
        }
        Guid versionId; Guid observationId; string frozen; int versionCount; int activityCount; int objectCount;
        await using (var db = Open())
        {
            var observation = await db.ProjectEvidenceObservations.AsNoTracking().FirstAsync(x => x.GitSyncJobId == first.Id);
            observationId = observation.Id; versionId = observation.GitEvidenceVersionId;
            frozen = (await db.GitEvidenceVersions.AsNoTracking().SingleAsync(x => x.Id == versionId)).FrozenFactsJson;
            objectCount = await db.GitHubProviderObjects.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id);
            versionCount = await db.GitEvidenceVersions.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id);
            activityCount = await db.GitActivities.CountAsync(x => db.GitHubProviderObjects.Any(o => o.Id == x.GitHubProviderObjectId && o.GitHubRepositoryId == f.Repository.Id));
        }
        GitSyncJob second;
        await using (var db = Open()) second = (await new GitSyncJobService(db, f.Sync, f.App).ClaimNextAsync(TimeSpan.FromMinutes(2), default))!;
        Assert.NotEqual(first.ProjectGitRepositoryId, second.ProjectGitRepositoryId);
        var gate = new Gate(); f.Provider.BeforePageReturn = gate;
        await using var processing = Open();
        var run = new GitHubSyncJobProcessor(processing, f.Provider, new GitHubCredentialService(processing, f.Provider, new Protector()), f.Sync, f.Tokens, f.App).ProcessAsync(second, default);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await using var mutate = Open();
            await mutate.ProjectGitRepositories.Where(x => x.Id == second.ProjectGitRepositoryId).ExecuteUpdateAsync(s => s.SetProperty(x => x.AccessStatus, ProjectGitRepositoryAccessStatus.AccessLost));
        }
        finally { gate.Release.TrySetResult(); }
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => run); Assert.Equal("GITHUB_REPOSITORY_NOT_AUTHORIZED", error.Code);
        await using var verify = Open();
        Assert.Equal(frozen, (await verify.GitEvidenceVersions.AsNoTracking().SingleAsync(x => x.Id == versionId)).FrozenFactsJson);
        Assert.True(await verify.ProjectEvidenceObservations.AnyAsync(x => x.Id == observationId && x.GitEvidenceVersionId == versionId && x.GitSyncJobId == first.Id));
        Assert.Empty(await verify.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == second.Id).ToListAsync());
        Assert.Empty(await verify.GitSyncCheckpoints.Where(x => x.SyncJobId == second.Id).ToListAsync());
        Assert.Empty(await verify.ProjectGitActivityGrants.Where(x => x.ProjectGitRepositoryId == second.ProjectGitRepositoryId).ToListAsync());
        Assert.Empty(await verify.ProjectGitActivityVisibilities.Where(x => x.ProjectGitRepositoryId == second.ProjectGitRepositoryId).ToListAsync());
        Assert.Equal(objectCount, await verify.GitHubProviderObjects.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id));
        Assert.Equal(versionCount, await verify.GitEvidenceVersions.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id));
        Assert.Equal(activityCount, await verify.GitActivities.CountAsync(x => verify.GitHubProviderObjects.Any(o => o.Id == x.GitHubProviderObjectId && o.GitHubRepositoryId == f.Repository.Id)));
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
    public async Task EvidenceSchemaGuardRejectsBothOldMigrationStatesBeforeProcessing()
    {
        foreach (var (name, expected) in new[] {
            ("uninet_step5b_1b3_before", "GITHUB_SYNC_EVIDENCE_SCHEMA_REQUIRED"),
            ("uninet_step5b_1b3_between", "GITHUB_SYNC_EVIDENCE_SCHEMA_REQUIRED"),
            ("uninet_step5b_1b3_p0", "GITHUB_SYNC_LEASE_LOST") })
        {
            var cs = $"Host=127.0.0.1;Port={Port};Database={name};Username=postgres;Timeout=5";
            await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(cs).Options);
            await db.Database.OpenConnectionAsync();
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            Assert.Equal(name, connection.Database); Assert.Equal("127.0.0.1", connection.Host); Assert.Equal(Port, connection.Port);
            var processor = new GitHubSyncJobProcessor(db, null!, null!);
            var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => processor.ProcessAsync(new GitSyncJob(), default));
            Assert.Equal(expected, error.Code);
            if (name != "uninet_step5b_1b3_p0") Assert.Empty(await db.GitSyncCheckpoints.AsNoTracking().ToListAsync());
        }
    }

    [Step4cPostgresFact]
    public async Task EvidenceWriteFailureRollsBackPageAndCheckpointBeforeRetry()
    {
        await using var f = await Fixture.CreateAsync(); await f.StageAsync("push"); Assert.True(await f.ConsumeAsync());
        GitSyncJob job;
        await using (var claimDb = Open()) job = (await new GitSyncJobService(claimDb, f.Sync, f.App).ClaimNextAsync(TimeSpan.FromMinutes(2), default))!;
        Assert.NotNull(job);
        var injection = new InterruptEvidenceWrite();
        await using (var failing = Open(injection))
        {
            var processor = new GitHubSyncJobProcessor(failing, f.Provider, new GitHubCredentialService(failing, f.Provider, new Protector()), f.Sync, f.Tokens, f.App);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => processor.ProcessAsync(job, default));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
        Assert.True(injection.Hit);
        await using (var check = Open())
        {
            Assert.Equal(0, await check.GitHubProviderObjects.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id));
            Assert.Equal(0, await check.GitSyncCheckpoints.CountAsync(x => x.SyncJobId == job.Id));
            Assert.Equal(0, await check.ProjectEvidenceObservations.CountAsync(x => x.GitSyncJobId == job.Id));
            Assert.Equal(0, await check.ProjectGitActivityGrants.CountAsync(x => x.ProjectGitRepositoryId == job.ProjectGitRepositoryId));
        }
        await using (var retry = Open())
        {
            var processor = new GitHubSyncJobProcessor(retry, f.Provider, new GitHubCredentialService(retry, f.Provider, new Protector()), f.Sync, f.Tokens, f.App);
            await processor.ProcessAsync(job, default);
            Assert.True(await retry.ProjectEvidenceObservations.AnyAsync(x => x.GitSyncJobId == job.Id));
            Assert.True(await retry.GitSyncCheckpoints.AnyAsync(x => x.SyncJobId == job.Id));
        }
    }

    [Step4cPostgresFact]
    public async Task EvidenceVersionsRemainScopedUntilEachProjectObservesThem()
    {
        await using var f = await Fixture.CreateAsync();
        await f.StageAsync("push"); Assert.True(await f.ConsumeAsync());
        async Task<GitSyncJob> ProcessOne()
        {
            await using var db = Open(); var service = new GitSyncJobService(db, f.Sync, f.App);
            var job = Assert.IsType<GitSyncJob>(await service.ClaimNextAsync(TimeSpan.FromMinutes(2), default));
            await new GitHubSyncJobProcessor(db, f.Provider, new GitHubCredentialService(db, f.Provider, new Protector()), f.Sync, f.Tokens, f.App).ProcessAsync(job, default);
            await service.CompleteAsync(job.Id, job.LeaseId!.Value, default); return job;
        }
        var first = await ProcessOne();
        f.Provider.CommitAuthorId = 10;
        var second = await ProcessOne();
        await using var verify = Open();
        var source = await verify.GitHubProviderObjects.SingleAsync(x => x.GitHubRepositoryId == f.Repository.Id && x.Category == "commits");
        var versions = await verify.GitEvidenceVersions.Where(x => x.GitHubProviderObjectId == source.Id).ToListAsync();
        Assert.Equal(2, versions.Count);
        var v1 = Assert.Single(versions, x => x.FrozenFactsJson.Contains("\"9\""));
        var v2 = Assert.Single(versions, x => x.FrozenFactsJson.Contains("\"10\""));
        var observations = await verify.ProjectEvidenceObservations.Where(x => x.GitEvidenceVersionId == v1.Id || x.GitEvidenceVersionId == v2.Id).ToListAsync();
        Assert.Contains(observations, x => x.ProjectGitRepositoryId == first.ProjectGitRepositoryId && x.GitEvidenceVersionId == v1.Id);
        Assert.DoesNotContain(observations, x => x.ProjectGitRepositoryId == first.ProjectGitRepositoryId && x.GitEvidenceVersionId == v2.Id);
        Assert.Contains(observations, x => x.ProjectGitRepositoryId == second.ProjectGitRepositoryId && x.GitEvidenceVersionId == v2.Id);
        using (var payload = JsonDocument.Parse(source.PayloadJson)) Assert.Equal(10, payload.RootElement.GetProperty("author").GetProperty("id").GetInt32());
        var retry = new GitSyncJob { ProjectGitRepositoryId = first.ProjectGitRepositoryId, GitHubRepositoryId = f.Repository.Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id, SyncType = GitSyncType.Scheduled };
        verify.GitSyncJobs.Add(retry); await verify.SaveChangesAsync();
        var third = await ProcessOne(); Assert.Equal(retry.Id, third.Id);
        await using var final = Open();
        Assert.True(await final.ProjectEvidenceObservations.AnyAsync(x => x.ProjectGitRepositoryId == first.ProjectGitRepositoryId && x.GitEvidenceVersionId == v2.Id));
        Assert.Equal(2, await final.GitEvidenceVersions.CountAsync(x => x.GitHubProviderObjectId == source.Id));
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
        var observations = await verify.ProjectEvidenceObservations.Where(x => x.GitHubRepositoryId == f.Repository.Id).ToListAsync();
        Assert.NotEmpty(observations);
        Assert.All(observations, x => Assert.Contains(x.ProjectGitRepositoryId, f.Links.Take(2).Select(l => l.Id)));
        // End-of-scope retrieval is complete even when the item's frozen facts remain incomplete.
        Assert.All(observations, x => Assert.Equal("Complete", x.RetrievalCompleteness));
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
        var sourceId = await verify.GitHubProviderObjects.Where(x => x.GitHubRepositoryId == f.Repository.Id && x.Category == "commits").Select(x => x.Id).SingleAsync();
        var versionId = await verify.GitEvidenceVersions.Where(x => x.GitHubProviderObjectId == sourceId).Select(x => x.Id).SingleAsync();
        var evidence = await verify.ProjectEvidenceObservations.Where(x => x.GitEvidenceVersionId == versionId).ToListAsync();
        Assert.Equal(2, evidence.Count);
        Assert.Equal(2, evidence.Select(x => x.ProjectGitRepositoryId).Distinct().Count());
        Assert.All(evidence, x => Assert.Contains(x.ProjectGitRepositoryId, f.Links.Take(2).Select(l => l.Id)));
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

    [EvidenceHostedSchemaFact]
    public async Task HostedWorkerPersistsSchemaFailureOrProcessesReadySchema()
    {
        var expected = Environment.GetEnvironmentVariable("UNINET_EVIDENCE_SCHEMA_EXPECTED");
        Assert.Contains(expected, new[] { "missing", "ready" });
        await using var f = await Fixture.CreateAsync();
        var job = new GitSyncJob { ProjectGitRepositoryId = f.Links[0].Id, GitHubRepositoryId = f.Repository.Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id, SyncType = GitSyncType.Initial };
        await using (var db = Open())
        {
            db.Add(job); await db.SaveChangesAsync();
            await db.Set<GitHubAppInstallation>().Where(x => x.Id == f.Installation.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextReconciliationAt, DateTimeOffset.UtcNow.AddHours(1)));
        }
        var sync = Options.Create(new GitSyncOptions { Enabled = true, WorkerConcurrency = 1, PollSeconds = 1, ReconciliationPollSeconds = 300, RetryBaseDelaySeconds = 1 });
        var services = new ServiceCollection(); using var http = new HttpClient(f.Http, false);
        services.AddDbContext<UniNetDbContext>(o => o.UseNpgsql(Connection));
        services.AddSingleton<IOptions<GitSyncOptions>>(sync); services.AddSingleton<IOptions<GitHubAppOptions>>(f.App);
        services.AddSingleton<IGitHubTokenProtector>(f.Protection); services.AddSingleton<IGitHubAppInstallationTokenProvider>(f.Tokens);
        services.AddSingleton<IGitHubProviderClient>(f.Provider); services.AddScoped<GitHubCredentialService>();
        services.AddScoped(sp => new GitHubAppInstallationReconciliationService(sp.GetRequiredService<UniNetDbContext>(), http, f.App, sync, f.Tokens));
        services.AddScoped<GitHubAppRepositoryActivationService>(); services.AddScoped<GitHubAppWebhookConsumer>();
        services.AddScoped<IGitSyncJobService, GitSyncJobService>(); services.AddScoped<IGitSyncJobProcessor, GitHubSyncJobProcessor>();
        await using var container = services.BuildServiceProvider();
        using var worker = new GitSyncWorker(container.GetRequiredService<IServiceScopeFactory>(), sync, NullLogger<GitSyncWorker>.Instance, f.App);
        try
        {
            await worker.StartAsync(default);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(25);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await using var check = Open(); var state = await check.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id);
                if (state.Status is GitSyncJobStatus.Failed or GitSyncJobStatus.Completed) break;
                await Task.Delay(100);
            }
        }
        finally { await worker.StopAsync(default); }
        await using var verify = Open(); var result = await verify.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id);
        if (expected == "missing")
        {
            Assert.Equal(GitSyncJobStatus.Failed, result.Status); Assert.Equal("GITHUB_SYNC_EVIDENCE_SCHEMA_REQUIRED", result.ErrorCode);
            Assert.Equal(1, result.RetryCount); Assert.Null(result.NextAttemptAt);
            Assert.Empty(await verify.GitSyncCheckpoints.Where(x => x.SyncJobId == job.Id).ToListAsync());
        }
        else
        {
            Assert.Equal(GitSyncJobStatus.Completed, result.Status);
            Assert.NotEmpty(await verify.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == job.Id).ToListAsync());
        }
    }

    [Step4cPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostedWorkerDoesNotRetryCorruptEvidenceAsTransient(bool validDifferentFacts)
    {
        await using var f = await Fixture.CreateAsync();
        var branch = new GitHubProviderObject { GitHubRepositoryId = f.Repository.Id, Category = "branches", ExternalObjectId = "main",
            PayloadJson = "{\"name\":\"main\"}", RetrievalMetadataJson = "{}", FirstObservedAt = DateTimeOffset.UtcNow, LastObservedAt = DateTimeOffset.UtcNow };
        using var item = JsonDocument.Parse("""{"name":"main","commit":{"sha":"activity-sha"}}""");
        var frozen = GitFrozenFactsCanonicalizer.Canonicalize(GitEvidenceFactsExtractor.ExtractV2("branches", "main", item.RootElement, null));
        var different = GitEvidenceFactsExtractor.ExtractV2("branches", "different-object", item.RootElement, null);
        var storedFacts = validDifferentFacts ? JsonSerializer.Serialize(different) : "{}";
        var corrupt = new GitEvidenceVersion { GitHubProviderObjectId = branch.Id, GitHubRepositoryId = f.Repository.Id, EvidenceSchemaVersion = 2,
            CanonicalizationVersion = 1, ContentHash = frozen.Sha256, FrozenFactsJson = storedFacts, EvidenceStatus = "Incomplete" };
        var job = new GitSyncJob { ProjectGitRepositoryId = f.Links[0].Id, GitHubRepositoryId = f.Repository.Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id, SyncType = GitSyncType.Initial };
        await using (var db = Open())
        {
            db.AddRange(branch, corrupt, job); await db.SaveChangesAsync();
            await db.Set<GitHubAppInstallation>().Where(x => x.Id == f.Installation.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextReconciliationAt, DateTimeOffset.UtcNow.AddHours(1)));
        }
        var sync = Options.Create(new GitSyncOptions { Enabled = true, WorkerConcurrency = 1, PollSeconds = 1, ReconciliationPollSeconds = 300, RetryBaseDelaySeconds = 300 });
        var services = new ServiceCollection(); using var http = new HttpClient(f.Http, false);
        services.AddDbContext<UniNetDbContext>(o => o.UseNpgsql(Connection));
        services.AddSingleton<IOptions<GitSyncOptions>>(sync); services.AddSingleton<IOptions<GitHubAppOptions>>(f.App);
        services.AddSingleton<IGitHubTokenProtector>(f.Protection); services.AddSingleton<IGitHubAppInstallationTokenProvider>(f.Tokens);
        services.AddSingleton<IGitHubProviderClient>(f.Provider); services.AddScoped<GitHubCredentialService>();
        services.AddScoped(sp => new GitHubAppInstallationReconciliationService(sp.GetRequiredService<UniNetDbContext>(), http, f.App, sync, f.Tokens));
        services.AddScoped<GitHubAppRepositoryActivationService>(); services.AddScoped<GitHubAppWebhookConsumer>();
        services.AddScoped<IGitSyncJobService, GitSyncJobService>(); services.AddScoped<IGitSyncJobProcessor, GitHubSyncJobProcessor>();
        await using var container = services.BuildServiceProvider();
        using var worker = new GitSyncWorker(container.GetRequiredService<IServiceScopeFactory>(), sync, NullLogger<GitSyncWorker>.Instance, f.App);
        try
        {
            await worker.StartAsync(default); var deadline = DateTimeOffset.UtcNow.AddSeconds(25);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await using var check = Open(); var state = await check.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id);
                if (state.Status is GitSyncJobStatus.Failed or GitSyncJobStatus.RetryScheduled) break;
                await Task.Delay(100);
            }
        }
        finally { await worker.StopAsync(default); }
        await using var verify = Open(); var result = await verify.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id);
        Assert.Equal(GitSyncJobStatus.Failed, result.Status); Assert.Equal("GITHUB_EVIDENCE_HASH_CONFLICT", result.ErrorCode); Assert.Null(result.NextAttemptAt);
        Assert.Empty(await verify.GitSyncCheckpoints.Where(x => x.SyncJobId == job.Id).ToListAsync());
        Assert.Empty(await verify.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == job.Id).ToListAsync());
        var unchanged = (await verify.GitEvidenceVersions.AsNoTracking().SingleAsync(x => x.Id == corrupt.Id)).FrozenFactsJson;
        if (validDifferentFacts) Assert.Equal("different-object", JsonSerializer.Deserialize<GitFrozenFacts>(unchanged)!.ExternalObjectId);
        else Assert.Equal("{}", unchanged);
    }

    [Step4cPostgresFact]
    public async Task HostedWorkerRejectsObservationForeignKeyMismatchWithoutPageCommit()
    {
        await using var f = await Fixture.CreateAsync();
        var job = new GitSyncJob { ProjectGitRepositoryId = f.Links[0].Id, GitHubRepositoryId = f.Repository.Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id, SyncType = GitSyncType.Initial };
        var sentinelJob = new GitSyncJob { ProjectGitRepositoryId = f.Links[1].Id, GitHubRepositoryId = f.Repository.Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id, SyncType = GitSyncType.Manual,
            Status = GitSyncJobStatus.Completed, CompletedAt = DateTimeOffset.UtcNow };
        var sentinelObject = new GitHubProviderObject { GitHubRepositoryId = f.Repository.Id, Category = "sentinel", ExternalObjectId = "sentinel",
            PayloadJson = "{}", RetrievalMetadataJson = "{}", FirstObservedAt = DateTimeOffset.UtcNow, LastObservedAt = DateTimeOffset.UtcNow };
        var sentinelVersion = new GitEvidenceVersion { GitHubProviderObjectId = sentinelObject.Id, GitHubRepositoryId = f.Repository.Id,
            EvidenceSchemaVersion = 1, CanonicalizationVersion = 1, ContentHash = new string('a', 64), FrozenFactsJson = "{}", EvidenceStatus = "Incomplete" };
        var sentinelObservation = new ProjectEvidenceObservation { ProjectGitRepositoryId = f.Links[1].Id, GitSyncJobId = sentinelJob.Id,
            GitHubRepositoryId = f.Repository.Id, GitEvidenceVersionId = sentinelVersion.Id, Category = "sentinel", ScopeKey = "",
            AuthorizationSource = "GitHubApp", RetrievalCompleteness = "Incomplete" };
        await using (var db = Open())
        {
            db.AddRange(job, sentinelJob, sentinelObject, sentinelVersion, sentinelObservation); await db.SaveChangesAsync();
            await db.Set<GitHubAppInstallation>().Where(x => x.Id == f.Installation.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextReconciliationAt, DateTimeOffset.UtcNow.AddHours(1)));
            await db.ProjectGitRepositories.Where(x => x.Id == f.Links[3].Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.SyncPaused, true));
            await db.Database.ExecuteSqlRawAsync($"""
                CREATE FUNCTION uninet_test_bad_observation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN IF NEW."ProjectGitRepositoryId" = '{f.Links[0].Id}'::uuid THEN NEW."GitHubRepositoryId" := 'ffffffff-ffff-ffff-ffff-ffffffffffff'::uuid; END IF; RETURN NEW; END $$;
                CREATE TRIGGER uninet_test_bad_observation BEFORE INSERT ON "ProjectEvidenceObservations"
                FOR EACH ROW EXECUTE FUNCTION uninet_test_bad_observation();
                """);
        }
        var capture = new CaptureDatabaseFailure();
        var sync = Options.Create(new GitSyncOptions { Enabled = true, WorkerConcurrency = 1, PollSeconds = 1, ReconciliationPollSeconds = 300, RetryBaseDelaySeconds = 300 });
        var services = new ServiceCollection(); using var http = new HttpClient(f.Http, false);
        services.AddDbContext<UniNetDbContext>(o => o.UseNpgsql(Connection).AddInterceptors(capture));
        services.AddSingleton<IOptions<GitSyncOptions>>(sync); services.AddSingleton<IOptions<GitHubAppOptions>>(f.App);
        services.AddSingleton<IGitHubTokenProtector>(f.Protection); services.AddSingleton<IGitHubAppInstallationTokenProvider>(f.Tokens);
        services.AddSingleton<IGitHubProviderClient>(f.Provider); services.AddScoped<GitHubCredentialService>();
        services.AddScoped(sp => new GitHubAppInstallationReconciliationService(sp.GetRequiredService<UniNetDbContext>(), http, f.App, sync, f.Tokens));
        services.AddScoped<GitHubAppRepositoryActivationService>(); services.AddScoped<GitHubAppWebhookConsumer>();
        services.AddScoped<IGitSyncJobService, GitSyncJobService>(); services.AddScoped<IGitSyncJobProcessor, GitHubSyncJobProcessor>();
        await using var container = services.BuildServiceProvider();
        try
        {
            using var worker = new GitSyncWorker(container.GetRequiredService<IServiceScopeFactory>(), sync, NullLogger<GitSyncWorker>.Instance, f.App);
            try
            {
                await worker.StartAsync(default); var deadline = DateTimeOffset.UtcNow.AddSeconds(25);
                while (DateTimeOffset.UtcNow < deadline)
                {
                    await using var check = Open(); var state = await check.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id);
                    if (state.Status is GitSyncJobStatus.Failed or GitSyncJobStatus.RetryScheduled) break;
                    await Task.Delay(100);
                }
            }
            finally { await worker.StopAsync(default); }
            Assert.NotNull(capture.Failure); Assert.Equal("23503", capture.Failure!.SqlState);
            Console.WriteLine($"FK constraint: {capture.Failure.ConstraintName}");
            Assert.StartsWith("FK_ProjectEvidenceObservations_", capture.Failure.ConstraintName);
            await using var verify = Open(); var result = await verify.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id);
            Assert.Equal(GitSyncJobStatus.Failed, result.Status);
            Assert.Equal("GITHUB_SYNC_EVIDENCE_INTEGRITY", result.ErrorCode); Assert.Null(result.NextAttemptAt);
            Assert.Empty(await verify.GitSyncCheckpoints.Where(x => x.SyncJobId == job.Id).ToListAsync());
            Assert.Empty(await verify.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == job.Id).ToListAsync());
            Assert.Single(await verify.GitHubProviderObjects.Where(x => x.GitHubRepositoryId == f.Repository.Id).ToListAsync());
            Assert.True(await verify.ProjectEvidenceObservations.AnyAsync(x => x.Id == sentinelObservation.Id && x.GitEvidenceVersionId == sentinelVersion.Id));
            Assert.True(await verify.GitEvidenceVersions.AnyAsync(x => x.Id == sentinelVersion.Id && x.FrozenFactsJson == "{}"));
            Assert.Empty(await verify.ProjectGitActivityGrants.Where(x => x.ProjectGitRepositoryId == f.Links[0].Id).ToListAsync());
        }
        finally
        {
            await using var cleanup = Open();
            await cleanup.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS uninet_test_bad_observation ON \"ProjectEvidenceObservations\"; DROP FUNCTION IF EXISTS uninet_test_bad_observation();");
        }
    }

    [Step4cPostgresFact]
    public async Task HostedWorkerRetriesWholePageAfterObservationInsertFailure()
    {
        await using var f = await Fixture.CreateAsync();
        var capture = new CaptureDatabaseFailure();
        var job = new GitSyncJob { ProjectGitRepositoryId = f.Links[0].Id, GitHubRepositoryId = f.Repository.Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id, SyncType = GitSyncType.Initial };
        await using (var db = Open())
        {
            db.Add(job); await db.SaveChangesAsync();
            await db.Set<GitHubAppInstallation>().Where(x => x.Id == f.Installation.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextReconciliationAt, DateTimeOffset.UtcNow.AddHours(1)));
            await db.Database.ExecuteSqlRawAsync($"""
                CREATE FUNCTION uninet_test_observation_failure() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN IF NEW."ProjectGitRepositoryId" = '{f.Links[0].Id}'::uuid THEN RAISE EXCEPTION 'test serialization conflict' USING ERRCODE = '40001'; END IF; RETURN NEW; END $$;
                CREATE TRIGGER uninet_test_observation_failure BEFORE INSERT ON "ProjectEvidenceObservations"
                FOR EACH ROW EXECUTE FUNCTION uninet_test_observation_failure();
                """);
        }
        var sync = Options.Create(new GitSyncOptions { Enabled = true, WorkerConcurrency = 1, PollSeconds = 1, ReconciliationPollSeconds = 300, RetryBaseDelaySeconds = 300 });
        var services = new ServiceCollection(); using var http = new HttpClient(f.Http, false);
        services.AddDbContext<UniNetDbContext>(o => o.UseNpgsql(Connection).AddInterceptors(capture));
        services.AddSingleton<IOptions<GitSyncOptions>>(sync); services.AddSingleton<IOptions<GitHubAppOptions>>(f.App);
        services.AddSingleton<IGitHubTokenProtector>(f.Protection); services.AddSingleton<IGitHubAppInstallationTokenProvider>(f.Tokens);
        services.AddSingleton<IGitHubProviderClient>(f.Provider); services.AddScoped<GitHubCredentialService>();
        services.AddScoped(sp => new GitHubAppInstallationReconciliationService(sp.GetRequiredService<UniNetDbContext>(), http, f.App, sync, f.Tokens));
        services.AddScoped<GitHubAppRepositoryActivationService>(); services.AddScoped<GitHubAppWebhookConsumer>();
        services.AddScoped<IGitSyncJobService, GitSyncJobService>(); services.AddScoped<IGitSyncJobProcessor, GitHubSyncJobProcessor>();
        await using var container = services.BuildServiceProvider();
        async Task RunUntil(GitSyncJobStatus target)
        {
            using var worker = new GitSyncWorker(container.GetRequiredService<IServiceScopeFactory>(), sync, NullLogger<GitSyncWorker>.Instance, f.App);
            try
            {
                await worker.StartAsync(default); var deadline = DateTimeOffset.UtcNow.AddSeconds(25);
                while (DateTimeOffset.UtcNow < deadline)
                {
                    await using var check = Open();
                    var state = await check.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id);
                    if (state.Status == target) return;
                    if (target == GitSyncJobStatus.Completed && state.Status == GitSyncJobStatus.RetryScheduled && state.RetryCount > 1)
                        await check.GitSyncJobs.Where(x => x.Id == job.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
                    await Task.Delay(100);
                }
                Assert.Fail($"Worker did not reach expected state; SQLSTATE={capture.Failure?.SqlState}, constraint={capture.Failure?.ConstraintName}.");
            }
            finally { await worker.StopAsync(default); }
        }
        try
        {
            await RunUntil(GitSyncJobStatus.RetryScheduled);
            await using (var check = Open())
            {
                var state = await check.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id);
                Assert.Equal("GITHUB_SYNC_INTERNAL", state.ErrorCode); Assert.NotNull(state.NextAttemptAt);
                Assert.Empty(await check.GitSyncCheckpoints.Where(x => x.SyncJobId == job.Id).ToListAsync());
                Assert.Empty(await check.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == job.Id).ToListAsync());
                Assert.Empty(await check.GitHubProviderObjects.Where(x => x.GitHubRepositoryId == f.Repository.Id).ToListAsync());
                Assert.Empty(await check.ProjectGitActivityGrants.Where(x => x.ProjectGitRepositoryId == f.Links[0].Id).ToListAsync());
                await check.Database.ExecuteSqlRawAsync("DROP TRIGGER uninet_test_observation_failure ON \"ProjectEvidenceObservations\"; DROP FUNCTION uninet_test_observation_failure();");
                await check.GitSyncJobs.Where(x => x.Id == job.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
            }
            await RunUntil(GitSyncJobStatus.Completed);
            await using var verify = Open();
            Assert.NotEmpty(await verify.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == job.Id).ToListAsync());
            Assert.NotEmpty(await verify.GitSyncCheckpoints.Where(x => x.SyncJobId == job.Id && x.Status == GitSyncCheckpointStatus.Completed).ToListAsync());
        }
        finally
        {
            await using var cleanup = Open();
            await cleanup.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS uninet_test_observation_failure ON \"ProjectEvidenceObservations\"; DROP FUNCTION IF EXISTS uninet_test_observation_failure();");
        }
    }

    [Step4cPostgresFact]
    public async Task HostedWorkerRecoversRealEvidenceVersionUniqueConflict()
    {
        await using var f = await Fixture.CreateAsync();
        var branch = new GitHubProviderObject { GitHubRepositoryId = f.Repository.Id, Category = "branches", ExternalObjectId = "main",
            PayloadJson = "{\"name\":\"main\"}", RetrievalMetadataJson = "{}", FirstObservedAt = DateTimeOffset.UtcNow, LastObservedAt = DateTimeOffset.UtcNow };
        var job = new GitSyncJob { ProjectGitRepositoryId = f.Links[0].Id, GitHubRepositoryId = f.Repository.Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id, SyncType = GitSyncType.Initial };
        await using (var db = Open())
        {
            db.AddRange(branch, job); await db.SaveChangesAsync();
            await db.Set<GitHubAppInstallation>().Where(x => x.Id == f.Installation.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextReconciliationAt, DateTimeOffset.UtcNow.AddHours(1)));
            await db.ProjectGitRepositories.Where(x => x.Id == f.Links[3].Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.SyncPaused, true));
        }
        var gate = new Gate(); var capture = new CaptureDatabaseFailure(); var barrier = new EvidenceLookupGate(gate);
        var sync = Options.Create(new GitSyncOptions { Enabled = true, WorkerConcurrency = 1, PollSeconds = 1, ReconciliationPollSeconds = 300, RetryBaseDelaySeconds = 300 });
        var services = new ServiceCollection(); using var http = new HttpClient(f.Http, false);
        services.AddDbContext<UniNetDbContext>(o => o.UseNpgsql(Connection).AddInterceptors(barrier, capture));
        services.AddSingleton<IOptions<GitSyncOptions>>(sync); services.AddSingleton<IOptions<GitHubAppOptions>>(f.App);
        services.AddSingleton<IGitHubTokenProtector>(f.Protection); services.AddSingleton<IGitHubAppInstallationTokenProvider>(f.Tokens);
        services.AddSingleton<IGitHubProviderClient>(f.Provider); services.AddScoped<GitHubCredentialService>();
        services.AddScoped(sp => new GitHubAppInstallationReconciliationService(sp.GetRequiredService<UniNetDbContext>(), http, f.App, sync, f.Tokens));
        services.AddScoped<GitHubAppRepositoryActivationService>(); services.AddScoped<GitHubAppWebhookConsumer>();
        services.AddScoped<IGitSyncJobService, GitSyncJobService>(); services.AddScoped<IGitSyncJobProcessor, GitHubSyncJobProcessor>();
        await using var container = services.BuildServiceProvider();
        using var worker = new GitSyncWorker(container.GetRequiredService<IServiceScopeFactory>(), sync, NullLogger<GitSyncWorker>.Instance, f.App);
        try
        {
            await worker.StartAsync(default);
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            using var item = JsonDocument.Parse("""{"name":"main","commit":{"sha":"activity-sha"}}""");
            var frozen = GitFrozenFactsCanonicalizer.Canonicalize(GitEvidenceFactsExtractor.ExtractV2("branches", "main", item.RootElement, null));
            await using (var competing = Open())
            {
                competing.GitEvidenceVersions.Add(new GitEvidenceVersion { GitHubProviderObjectId = branch.Id, GitHubRepositoryId = f.Repository.Id,
                    EvidenceSchemaVersion = 2, CanonicalizationVersion = 1, ContentHash = frozen.Sha256,
                    FrozenFactsJson = Encoding.UTF8.GetString(frozen.Bytes), EvidenceStatus = "Incomplete" });
                await competing.SaveChangesAsync();
            }
            gate.Release.TrySetResult();
            var deadline = DateTimeOffset.UtcNow.AddSeconds(25);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await using var check = Open(); var state = await check.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id);
                if (state.Status == GitSyncJobStatus.RetryScheduled) break;
                await Task.Delay(100);
            }
            await worker.StopAsync(default);
            Assert.NotNull(capture.Failure); Assert.Equal("23505", capture.Failure!.SqlState);
            Assert.StartsWith("IX_GitEvidenceVersions_", capture.Failure.ConstraintName);
            Console.WriteLine($"Unique conflict constraint: {capture.Failure.ConstraintName}");
            await using (var check = Open())
            {
                var state = await check.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id);
                Assert.Equal(GitSyncJobStatus.RetryScheduled, state.Status); Assert.Equal("GITHUB_SYNC_INTERNAL", state.ErrorCode);
                Assert.Empty(await check.GitSyncCheckpoints.Where(x => x.SyncJobId == job.Id).ToListAsync());
                Assert.Empty(await check.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == job.Id).ToListAsync());
                Assert.Single(await check.GitEvidenceVersions.Where(x => x.GitHubProviderObjectId == branch.Id).ToListAsync());
                await check.GitSyncJobs.Where(x => x.Id == job.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
            }
            // The initial long delay pins the genuine 23505 for assertions above.
            // Recovery may also encounter PostgreSQL 40001; let the real bounded
            // worker retry it within this test's deadline instead of waiting 300s.
            sync.Value.RetryBaseDelaySeconds = 1;
            using var retryWorker = new GitSyncWorker(container.GetRequiredService<IServiceScopeFactory>(), sync, NullLogger<GitSyncWorker>.Instance, f.App);
            try
            {
                await retryWorker.StartAsync(default); deadline = DateTimeOffset.UtcNow.AddSeconds(25);
                while (DateTimeOffset.UtcNow < deadline)
                {
                    await using var check = Open(); if ((await check.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id)).Status == GitSyncJobStatus.Completed) break;
                    await Task.Delay(100);
                }
            }
            finally { await retryWorker.StopAsync(default); }
            await using var final = Open();
            Assert.Equal(GitSyncJobStatus.Completed, (await final.GitSyncJobs.SingleAsync(x => x.Id == job.Id)).Status);
            Assert.Single(await final.GitEvidenceVersions.Where(x => x.GitHubProviderObjectId == branch.Id).ToListAsync());
            Assert.Single(await final.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == job.Id && x.Category == "branches").ToListAsync());
            Assert.Equal(GitSyncCheckpointStatus.Completed, (await final.GitSyncCheckpoints.SingleAsync(x => x.SyncJobId == job.Id && x.Category == "branches")).Status);
            var secondJob = new GitSyncJob { ProjectGitRepositoryId = f.Links[1].Id, GitHubRepositoryId = f.Repository.Id,
                CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id, SyncType = GitSyncType.Initial };
            final.GitSyncJobs.Add(secondJob); await final.SaveChangesAsync();
            using var secondWorker = new GitSyncWorker(container.GetRequiredService<IServiceScopeFactory>(), sync, NullLogger<GitSyncWorker>.Instance, f.App);
            try
            {
                await secondWorker.StartAsync(default); deadline = DateTimeOffset.UtcNow.AddSeconds(25);
                while (DateTimeOffset.UtcNow < deadline)
                {
                    await using var check = Open(); if ((await check.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == secondJob.Id)).Status == GitSyncJobStatus.Completed) break;
                    await Task.Delay(100);
                }
            }
            finally { await secondWorker.StopAsync(default); }
            await using var both = Open();
            Assert.Equal(GitSyncJobStatus.Completed, (await both.GitSyncJobs.SingleAsync(x => x.Id == secondJob.Id)).Status);
            var version = await both.GitEvidenceVersions.SingleAsync(x => x.GitHubProviderObjectId == branch.Id);
            var observations = await both.ProjectEvidenceObservations.Where(x => x.GitEvidenceVersionId == version.Id).ToListAsync();
            Assert.Equal(2, observations.Count);
            Assert.Contains(observations, x => x.GitSyncJobId == job.Id && x.ProjectGitRepositoryId == f.Links[0].Id);
            Assert.Contains(observations, x => x.GitSyncJobId == secondJob.Id && x.ProjectGitRepositoryId == f.Links[1].Id);
        }
        finally { gate.Release.TrySetResult(); await worker.StopAsync(default); }
    }

    [Step4cPostgresFact]
    public async Task HostedWorkersRecoverConcurrentSharedEvidenceConflict()
    {
        await using var f = await Fixture.CreateAsync(); await f.StageAsync("push"); Assert.True(await f.ConsumeAsync());
        await using (var db = Open())
        {
            await db.Set<GitHubAppInstallation>().Where(x => x.Id == f.Installation.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextReconciliationAt, DateTimeOffset.UtcNow.AddHours(1)));
            await db.ProjectGitRepositories.Where(x => x.Id == f.Links[3].Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.SyncPaused, true));
        }
        f.Provider.ConcurrentBranches = new Gate();
        var sync = Options.Create(new GitSyncOptions { Enabled = true, WorkerConcurrency = 2, PollSeconds = 1, ReconciliationPollSeconds = 300, RetryBaseDelaySeconds = 1 });
        var services = new ServiceCollection(); using var http = new HttpClient(f.Http, false);
        services.AddDbContext<UniNetDbContext>(o => o.UseNpgsql(Connection));
        services.AddSingleton<IOptions<GitSyncOptions>>(sync); services.AddSingleton<IOptions<GitHubAppOptions>>(f.App);
        services.AddSingleton<IGitHubTokenProtector>(f.Protection); services.AddSingleton<IGitHubAppInstallationTokenProvider>(f.Tokens);
        services.AddSingleton<IGitHubProviderClient>(f.Provider); services.AddScoped<GitHubCredentialService>();
        services.AddScoped(sp => new GitHubAppInstallationReconciliationService(sp.GetRequiredService<UniNetDbContext>(), http, f.App, sync, f.Tokens));
        services.AddScoped<GitHubAppRepositoryActivationService>(); services.AddScoped<GitHubAppWebhookConsumer>();
        services.AddScoped<IGitSyncJobService, GitSyncJobService>(); services.AddScoped<IGitSyncJobProcessor, GitHubSyncJobProcessor>();
        await using var container = services.BuildServiceProvider();
        using var worker = new GitSyncWorker(container.GetRequiredService<IServiceScopeFactory>(), sync, NullLogger<GitSyncWorker>.Instance, f.App);
        try
        {
            await worker.StartAsync(default); var deadline = DateTimeOffset.UtcNow.AddSeconds(35);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await using var check = Open();
                if (await check.GitSyncJobs.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id && x.Status == GitSyncJobStatus.Completed) == 2) break;
                await Task.Delay(100);
            }
        }
        finally { f.Provider.ConcurrentBranches.Release.TrySetResult(); await worker.StopAsync(default); }
        await using var verify = Open();
        Assert.Equal(2, await verify.GitSyncJobs.CountAsync(x => x.GitHubRepositoryId == f.Repository.Id && x.Status == GitSyncJobStatus.Completed));
        var source = await verify.GitHubProviderObjects.SingleAsync(x => x.GitHubRepositoryId == f.Repository.Id && x.Category == "commits");
        var version = await verify.GitEvidenceVersions.SingleAsync(x => x.GitHubProviderObjectId == source.Id);
        var observations = await verify.ProjectEvidenceObservations.Where(x => x.GitEvidenceVersionId == version.Id).ToListAsync();
        Assert.Equal(2, observations.Count); Assert.Equal(2, observations.Select(x => x.ProjectGitRepositoryId).Distinct().Count());
        Assert.All(observations, x => Assert.Contains(x.ProjectGitRepositoryId, f.Links.Take(2).Select(y => y.Id)));
        Assert.Equal(2, await verify.GitSyncCheckpoints.CountAsync(x => observations.Select(o => o.GitSyncJobId).Contains(x.SyncJobId) && x.Category == "commits" && x.Status == GitSyncCheckpointStatus.Completed));
    }
}
