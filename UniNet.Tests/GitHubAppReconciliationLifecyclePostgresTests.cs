using System.Data.Common;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

public sealed class DurableReconciliationTheoryAttribute : TheoryAttribute
{
    public DurableReconciliationTheoryAttribute()
    { if (Environment.GetEnvironmentVariable("UNINET_RECON_DISPOSABLE") != "1") Skip = "Requires explicitly enabled disposable PostgreSQL."; }
}

[Collection("GitHub reconciliation PostgreSQL")]
public sealed class GitHubAppReconciliationLifecyclePostgresTests
{
    private static string Database => GitHubAppDisposableDatabase.Name("uninet_step3c_step4c_regression");
    private static string Connection => "Host=127.0.0.1;Port=55433;Database=" + Database + ";Username=postgres;Timeout=5";
    private static readonly IOptions<GitSyncOptions> Sync = Options.Create(new GitSyncOptions { Enabled = true });
    private static UniNetDbContext Open(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<UniNetDbContext>()
        .UseNpgsql(Connection).AddInterceptors(interceptors).Options);

    private sealed class Gate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task WaitAsync(CancellationToken ct)
        { Entered.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), ct); }
        public Task WaitForEntryAsync() => Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private sealed class CommandGate(string match, Gate gate) : DbCommandInterceptor
    {
        private int entered;
        private async Task WaitAsync(DbCommand command, CancellationToken ct)
        { if (command.CommandText.Contains(match, StringComparison.Ordinal) && Interlocked.CompareExchange(ref entered, 1, 0) == 0) await gate.WaitAsync(ct); }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { await WaitAsync(command, ct); return result; }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        { await WaitAsync(command, ct); return result; }
    }

    private sealed class ConcurrentWrites : DbCommandInterceptor
    {
        private int arrivals;
        private readonly TaskCompletionSource both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"GitSyncJobs\"", StringComparison.Ordinal))
            {
                var count = Interlocked.Increment(ref arrivals);
                if (count == 2) both.TrySetResult();
                if (count <= 2) await both.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            }
            return result;
        }
    }

    private sealed class Remote(Fixture f) : HttpMessageHandler
    {
        public bool Present;
        public bool Suspended;
        public HttpStatusCode? Failure;
        public bool Timeout;
        public Gate? FirstRequest;
        public Gate? FinalVerification;
        public RetryConditionHeaderValue? Retry;
        public long? Reset;
        public int Calls;
        private int installationReads;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Interlocked.Increment(ref Calls) == 1 && FirstRequest is { } first) await first.WaitAsync(ct);
            if (Timeout) throw new TaskCanceledException("Simulated provider timeout");
            if (Failure is { } failure)
            {
                var error = new HttpResponseMessage(failure);
                error.Headers.RetryAfter = Retry;
                if (Reset is { } reset)
                { error.Headers.Add("X-RateLimit-Remaining", "0"); error.Headers.Add("X-RateLimit-Reset", reset.ToString()); }
                return error;
            }
            string body;
            if (request.Method == HttpMethod.Post) body = "{\"token\":\"reconciliation-fixture\"}";
            else if (request.RequestUri!.AbsolutePath == "/installation/repositories")
                body = Present ? $"{{\"total_count\":1,\"repositories\":[{{\"id\":{f.Repository.ProviderRepositoryId}}}]}}" : "{\"total_count\":0,\"repositories\":[]}";
            else
            {
                Assert.Equal($"/app/installations/{f.Installation.ProviderInstallationId}", request.RequestUri!.AbsolutePath);
                if (Interlocked.Increment(ref installationReads) == 2 && FinalVerification is { } final) await final.WaitAsync(ct);
                body = $"{{\"id\":{f.Installation.ProviderInstallationId},\"suspended_at\":{(Suspended ? "\"2026-01-01T00:00:00Z\"" : "null")}}}";
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    private sealed class Tokens(Fixture f) : IGitHubAppInstallationTokenProvider
    {
        public int Invalidations;
        public Task<string> GetAsync(long installationId, long repositoryId, CancellationToken ct)
        { Assert.Equal(f.Installation.ProviderInstallationId, installationId); Assert.Equal(f.Repository.ProviderRepositoryId, repositoryId); return Task.FromResult("scoped-fixture"); }
        public void InvalidateInstallation(long installationId) { Interlocked.Increment(ref Invalidations); }
    }

    private sealed class Provider(Fixture f) : IGitHubProviderClient
    {
        public int Reads;
        public Gate? FirstRead;
        public bool Readable = true;
        public async Task<GitHubRemoteRepository> GetRepositoryAsync(string token, long id, CancellationToken ct)
        {
            Assert.Equal("scoped-fixture", token); Assert.Equal(f.Repository.ProviderRepositoryId, id);
            if (Interlocked.Increment(ref Reads) == 1 && FirstRead is { } gate) await gate.WaitAsync(ct);
            return new(id, 42, "example", "repo", "example/repo", "https://github.com/example/repo", null, "main", true, false, Readable);
        }
        public Task<GitHubOAuthTokens> ExchangeCodeAsync(string code, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubOAuthTokens> RefreshAsync(string token, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubUser> GetAuthenticatedUserAsync(string token, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<GitHubRemoteRepository>> GetRepositoriesAsync(string token, int page, int size, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubSyncPage> GetSyncPageAsync(string token, string repo, string category, string? scope, int page, int size, CancellationToken ct)
        {
            Assert.Equal("scoped-fixture", token);
            var payload = category switch
            {
                "branches" => """[{"name":"main","commit":{"sha":"new-fixture-commit"}}]""",
                "commits" => """[{"sha":"new-fixture-commit","commit":{"message":"fixture","committer":{"date":"2026-11-01T10:00:00Z"}},"author":{"id":42,"login":"example"}}]""",
                _ => "[]"
            };
            using var document = JsonDocument.Parse(payload);
            return Task.FromResult(new GitHubSyncPage(document.RootElement.EnumerateArray().Select(x => x.Clone()).ToArray(), false, 5000, null));
        }
    }
    private sealed class Protector : IGitHubTokenProtector
    {
        public string Protect(string token) => throw new NotSupportedException("OAuth must not be selected.");
        public string Unprotect(string token) => throw new NotSupportedException("OAuth must not be selected.");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Account Owner = null!;
        public Project Project = null!;
        public Project OtherProject = null!;
        public GitHubAppInstallation Installation = null!;
        public GitHubRepository Repository = null!;
        public GitHubAppRepositoryGrant Grant = null!;
        public ProjectGitRepository Link = null!;
        public ProjectGitRepository OtherLink = null!;
        public GitSyncJob Initial = null!;
        public Guid ActivityId;
        public Guid CheckpointId;
        public IOptions<GitHubAppOptions> App = null!;

        public static async Task<Fixture> CreateAsync()
        {
            await using var db = Open();
            await db.Database.OpenConnectionAsync();
            var connection = (Npgsql.NpgsqlConnection)db.Database.GetDbConnection();
            Assert.Equal(Database, connection.Database); Assert.Equal("127.0.0.1", connection.Host); Assert.Equal(55433, connection.Port);
            await db.Database.EnsureCreatedAsync();
            var f = new Fixture();
            f.Owner = new() { Email = $"durable-{Guid.NewGuid():N}@example.test", Role = AccountRole.Student, Status = AccountStatus.Active,
                Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
            Project MakeProject() => new() { Creator = f.Owner.Profile!, Title = "Disposable reconciliation", ProjectField = "Engineering", Description = "Test",
                MemberTarget = 5, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Visibility = ProjectVisibility.Private,
                Status = ProjectStatus.Active, RecruitmentStatus = RecruitmentStatus.Open };
            f.Project = MakeProject(); f.OtherProject = MakeProject();
            f.Installation = new() { ProviderInstallationId = Random.Shared.NextInt64(10000000, long.MaxValue / 2), OwnerAccountId = 42, OwnerLogin = "example", OwnerType = "User" };
            f.Repository = new() { ProviderRepositoryId = Random.Shared.NextInt64(10000000, long.MaxValue / 2), ProviderOwnerId = 42, OwnerLogin = "example", Name = "repo", FullName = "example/repo", HtmlUrl = "https://github.com/example/repo", IsPrivate = true };
            f.Grant = new() { InstallationId = f.Installation.Id, ProviderRepositoryId = f.Repository.ProviderRepositoryId, VerifiedAt = DateTimeOffset.UtcNow.AddMinutes(-5) };
            ProjectGitRepository MakeLink(Project project) => new() { ProjectId = project.Id, GitHubRepositoryId = f.Repository.Id, LinkedByProfileId = f.Owner.Profile!.Id,
                CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id, AccessStatus = ProjectGitRepositoryAccessStatus.Active, LinkedAt = DateTimeOffset.UtcNow.AddDays(-2) };
            f.Link = MakeLink(f.Project); f.OtherLink = MakeLink(f.OtherProject);
            f.Initial = f.NewJob(GitSyncType.Initial); f.Initial.Status = GitSyncJobStatus.Completed; f.Initial.CompletedAt = DateTimeOffset.UtcNow.AddDays(-1);
            f.Initial.UpdatedAt = f.Initial.CompletedAt.Value;
            var obj = new GitHubProviderObject { GitHubRepositoryId = f.Repository.Id, Category = "commits", ExternalObjectId = "fixture-commit",
                FirstObservedAt = DateTimeOffset.UtcNow.AddDays(-1), LastObservedAt = DateTimeOffset.UtcNow.AddDays(-1), PayloadJson = "{}" };
            var activity = new GitActivity { GitHubProviderObjectId = obj.Id, ActivityType = "commit.created", OccurredAt = DateTimeOffset.UtcNow.AddDays(-1) };
            f.ActivityId = activity.Id;
            var checkpoint = new GitSyncCheckpoint { SyncJobId = f.Initial.Id, Category = "commits", ScopeKey = "main", Status = GitSyncCheckpointStatus.Completed, ItemsProcessed = 1 };
            f.CheckpointId = checkpoint.Id;
            db.AddRange(f.Owner, f.Project, f.OtherProject, f.Installation, f.Repository, f.Grant, f.Link, f.OtherLink, f.Initial, obj, activity, checkpoint,
                new ProjectGitActivityGrant { ProjectGitRepositoryId = f.Link.Id, GitHubProviderObjectId = obj.Id, ObservedAt = obj.FirstObservedAt },
                new ProjectGitActivityVisibility { ProjectGitRepositoryId = f.Link.Id, GitActivityId = activity.Id, ObservedAt = obj.FirstObservedAt });
            await db.SaveChangesAsync();
            using var rsa = RSA.Create(2048);
            f.App = Options.Create(new GitHubAppOptions { Enabled = true, ClientId = "fixture-app", PrivateKeyPem = rsa.ExportRSAPrivateKeyPem() });
            return f;
        }
        public GitSyncJob NewJob(GitSyncType type = GitSyncType.Manual) => new() { ProjectGitRepositoryId = Link.Id, GitHubRepositoryId = Repository.Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = Grant.Id, SyncType = type, Status = GitSyncJobStatus.Pending };
        public async Task ScanAsync(Remote remote, params IInterceptor[] interceptors)
        {
            await using var db = Open(interceptors); using var http = new HttpClient(remote, disposeHandler: false);
            await new GitHubAppInstallationReconciliationService(db, http, App, Sync, new Tokens(this)).ReconcileAsync(default);
        }
        public async Task DueAsync()
        { await using var db = Open(); await db.Set<GitHubAppInstallation>().Where(x => x.Id == Installation.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextReconciliationAt, (DateTimeOffset?)null)); }
        public async Task RecoverAsync(Provider provider, params IInterceptor[] interceptors)
        { await using var db = Open(interceptors); await new GitHubAppRepositoryActivationService(db, new Tokens(this), provider, Sync, App).RetryPendingAsync(default); }
        public async Task AssertHistoryAsync()
        {
            await using var db = Open();
            Assert.True(await db.GitActivities.AnyAsync(x => x.Id == ActivityId));
            Assert.True(await db.GitSyncCheckpoints.AnyAsync(x => x.Id == CheckpointId && x.ItemsProcessed == 1));
            Assert.Equal(1, (await new ProjectGitActivityService(db).GetAsync(Owner.Id, Project.Id, 1, 50, null, null, null, default)).TotalCount);
            Assert.Equal(0, (await new ProjectGitActivityService(db).GetAsync(Owner.Id, OtherProject.Id, 1, 50, null, null, null, default)).TotalCount);
        }
        public async ValueTask DisposeAsync()
        {
            await using var db = Open();
            await db.Set<GitHubAppInstallation>().Where(x => x.Id == Installation.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextReconciliationAt, DateTimeOffset.UtcNow.AddYears(1)));
            await db.ProjectGitRepositories.Where(x => x.Id == Link.Id || x.Id == OtherLink.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UnlinkedAt, DateTimeOffset.UtcNow));
        }
    }

    [DurableReconciliationTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevokeWinsAgainstEnqueueWithOldSerializableSnapshot(bool scheduled)
    {
        await using var f = await Fixture.CreateAsync();
        var gate = new Gate();
        async Task Enqueue()
        {
            await using var db = Open(new CommandGate("INSERT INTO \"GitSyncJobs\"", gate));
            var jobs = new GitSyncJobService(db, Sync, Options.Create(new GitHubAppOptions { Enabled = true }));
            try
            {
                if (scheduled) await jobs.EnqueueDueScheduledJobsAsync(DateTimeOffset.UtcNow, default);
                else await jobs.RequestManualSyncAsync(f.Owner.Id, f.Project.Id, f.Link.Id, default);
            }
            catch (GitHubIntegrationException ex) { Assert.Contains(ex.Code, new[] { "GITHUB_SYNC_CONFLICT", "GITHUB_APP_PENDING_ACTIVATION" }); }
        }
        var enqueue = Enqueue();
        try { await gate.WaitForEntryAsync(); await f.ScanAsync(new Remote(f)); }
        finally { gate.Release.TrySetResult(); }
        await enqueue;
        await using var verify = Open();
        Assert.Equal(ProjectGitRepositoryAccessStatus.AccessLost, (await verify.ProjectGitRepositories.SingleAsync(x => x.Id == f.Link.Id)).AccessStatus);
        Assert.Equal(0, await verify.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == f.Link.Id && (x.Status == GitSyncJobStatus.Pending || x.Status == GitSyncJobStatus.Running)));
        Assert.Null(await new GitSyncJobService(verify, Sync, Options.Create(new GitHubAppOptions { Enabled = true })).ClaimNextAsync(TimeSpan.FromMinutes(1), default));
        await f.AssertHistoryAsync();
    }

    [Step3cReconciliationFact]
    public async Task ClaimRollsBackWhenReconciliationOwnsInstallationLock()
    {
        await using var f = await Fixture.CreateAsync();
        await using (var db = Open()) { db.Add(f.NewJob()); await db.SaveChangesAsync(); }
        var claimGate = new Gate(); var remoteGate = new Gate();
        async Task<GitSyncJob?> Claim()
        { await using var db = Open(new CommandGate("pg_try_advisory_xact_lock_shared", claimGate)); return await new GitSyncJobService(db, Sync, Options.Create(new GitHubAppOptions { Enabled = true })).ClaimNextAsync(TimeSpan.FromMinutes(1), default); }
        var claim = Claim(); await claimGate.WaitForEntryAsync();
        var scan = f.ScanAsync(new Remote(f) { FirstRequest = remoteGate });
        try
        {
            await remoteGate.WaitForEntryAsync(); claimGate.Release.TrySetResult();
            Assert.Null(await claim);
        }
        finally { claimGate.Release.TrySetResult(); remoteGate.Release.TrySetResult(); }
        await scan;
        await using var verify = Open();
        Assert.Equal(1, await verify.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == f.Link.Id && x.Status == GitSyncJobStatus.Cancelled));
        Assert.Null(await new GitSyncJobService(verify, Sync, Options.Create(new GitHubAppOptions { Enabled = true })).ClaimNextAsync(TimeSpan.FromMinutes(1), default));
        await f.AssertHistoryAsync();
    }

    [Step3cReconciliationFact]
    public async Task RevokeAfterProviderVerificationCannotBeOverwrittenByStaleRestoration()
    {
        await using var f = await Fixture.CreateAsync(); var gate = new Gate();
        var scan = f.ScanAsync(new Remote(f) { Present = true, FinalVerification = gate });
        try
        {
            await gate.WaitForEntryAsync();
            await using var db = Open();
            await db.Set<GitHubAppRepositoryGrant>().Where(x => x.Id == f.Grant.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
        }
        finally { gate.Release.TrySetResult(); }
        await scan;
        await using var verify = Open();
        Assert.NotNull((await verify.Set<GitHubAppRepositoryGrant>().SingleAsync(x => x.Id == f.Grant.Id)).RevokedAt);
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => new GitSyncJobService(verify, Sync, Options.Create(new GitHubAppOptions { Enabled = true })).RequestManualSyncAsync(f.Owner.Id, f.Project.Id, f.Link.Id, default));
        await f.AssertHistoryAsync();
    }

    [Step3cReconciliationFact]
    public async Task ConcurrentScansAndRecoveryPreserveHistoryAndCreateOneScheduledJobPerLink()
    {
        var counters = new ConcurrentDictionary<string, long>(); var labels = new ConcurrentBag<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => { if (instrument.Meter.Name == "UniNet.GitHubApp") l.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => { counters.AddOrUpdate(instrument.Name, value, (_, old) => old + value); foreach (var tag in tags) labels.Add(tag.Key); });
        listener.Start();
        await using var f = await Fixture.CreateAsync();
        await using (var db = Open()) { db.Add(f.NewJob()); await db.SaveChangesAsync(); }
        var gate = new Gate(); var remote = new Remote(f) { FirstRequest = gate };
        var first = f.ScanAsync(remote);
        try { await gate.WaitForEntryAsync(); await f.ScanAsync(new Remote(f)); }
        finally { gate.Release.TrySetResult(); }
        await first;
        await using (var db = Open())
        {
            Assert.Equal(2, await db.ProjectGitRepositoryHistory.CountAsync(x => (x.ProjectGitRepositoryId == f.Link.Id || x.ProjectGitRepositoryId == f.OtherLink.Id) && x.Action == ProjectGitRepositoryHistoryAction.AccessLost));
            Assert.Equal(1, await db.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == f.Link.Id && x.Status == GitSyncJobStatus.Cancelled));
        }
        await f.AssertHistoryAsync();
        await f.DueAsync(); await f.ScanAsync(new Remote(f) { Present = true });
        var recoveryGate = new Gate(); var provider = new Provider(f) { FirstRead = recoveryGate };
        var recovery = f.RecoverAsync(provider);
        try { await recoveryGate.WaitForEntryAsync(); await f.RecoverAsync(new Provider(f)); }
        finally { recoveryGate.Release.TrySetResult(); }
        await recovery; await f.RecoverAsync(new Provider(f));
        await using var verify = Open();
        Assert.Equal(2, await verify.ProjectGitRepositories.CountAsync(x => (x.Id == f.Link.Id || x.Id == f.OtherLink.Id) && x.AccessStatus == ProjectGitRepositoryAccessStatus.Active));
        Assert.Equal(1, await verify.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == f.Link.Id && x.SyncType == GitSyncType.Initial));
        Assert.Equal(1, await verify.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == f.Link.Id && x.SyncType == GitSyncType.Scheduled && x.Status == GitSyncJobStatus.Pending));
        Assert.Equal(1, await verify.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == f.OtherLink.Id && x.SyncType == GitSyncType.Scheduled && x.Status == GitSyncJobStatus.Pending));
        await f.AssertHistoryAsync();
        Assert.True(counters.GetValueOrDefault("uninet.github_app.access.lost") >= 1);
        Assert.True(counters.GetValueOrDefault("uninet.github_app.access.restored") >= 1);
        Assert.Empty(labels);
    }

    [DurableReconciliationTheory]
    [InlineData(401)] [InlineData(403)] [InlineData(404)] [InlineData(429)] [InlineData(500)] [InlineData(0)]
    public async Task UnknownDefersAuthorizationAndPersistsDeadlineAcrossServiceInstances(int status)
    {
        await using var f = await Fixture.CreateAsync();
        var pending = f.NewJob(); await using (var db = Open()) { db.Add(pending); await db.SaveChangesAsync(); }
        var before = DateTimeOffset.UtcNow;
        await f.ScanAsync(new Remote(f) { Failure = status == 0 ? null : (HttpStatusCode)status, Timeout = status == 0,
            Retry = status == 429 ? new RetryConditionHeaderValue(TimeSpan.FromHours(1)) : null });
        await using var verify = Open();
        var installation = await verify.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == f.Installation.Id);
        Assert.True(installation.VerificationUnknown); Assert.Equal(GitHubAppInstallationStatus.Active, installation.Status);
        Assert.True(installation.NextReconciliationAt > before.AddSeconds(50)); Assert.Equal(1, installation.ReconciliationFailures);
        if (status == 429) Assert.True(installation.NextReconciliationAt >= before.AddHours(1));
        Assert.Null((await verify.Set<GitHubAppRepositoryGrant>().SingleAsync(x => x.Id == f.Grant.Id)).RevokedAt);
        var jobs = new GitSyncJobService(verify, Sync, Options.Create(new GitHubAppOptions { Enabled = true }));
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => jobs.RequestManualSyncAsync(f.Owner.Id, f.Project.Id, f.Link.Id, default));
        Assert.Equal(0, await jobs.EnqueueDueScheduledJobsAsync(DateTimeOffset.UtcNow, default));
        Assert.Null(await jobs.ClaimNextAsync(TimeSpan.FromMinutes(1), default));
        Assert.Equal("VerificationPending", Assert.Single((await jobs.GetProjectStatusAsync(f.Owner.Id, f.Project.Id, default)).Items).SyncReadiness);
        var provider = new Provider(f);
        var processor = new GitHubSyncJobProcessor(verify, provider, new GitHubCredentialService(verify, provider, new Protector()), Sync, new Tokens(f), f.App);
        Assert.Equal("GITHUB_SYNC_LEASE_LOST", (await Assert.ThrowsAsync<GitHubIntegrationException>(() => processor.ProcessAsync(pending, default))).Code);
        Assert.Equal(0, provider.Reads);
        var freshRemote = new Remote(f) { Present = true }; await f.ScanAsync(freshRemote);
        Assert.Equal(0, freshRemote.Calls); // Fresh DbContext/HttpClient/service; no static cache participates.
        await f.AssertHistoryAsync();
        await f.DueAsync(); await f.ScanAsync(new Remote(f) { Present = true });
        Assert.False((await verify.Set<GitHubAppInstallation>().AsNoTracking().SingleAsync(x => x.Id == f.Installation.Id)).VerificationUnknown);
        Assert.True((await jobs.RequestManualSyncAsync(f.Owner.Id, f.Project.Id, f.Link.Id, default)).WasAlreadyQueued);
        Assert.Equal(pending.Id, (await jobs.ClaimNextAsync(TimeSpan.FromMinutes(1), default))!.Id);
        Assert.Equal(2, await verify.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == f.Link.Id));
    }

    [Step3cReconciliationFact]
    public async Task ExhaustedQuotaUsesLaterResetAndDoesNotBlockAnotherInstallation()
    {
        await using var f = await Fixture.CreateAsync();
        var reset = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds();
        await f.ScanAsync(new Remote(f) { Failure = HttpStatusCode.Forbidden, Retry = new RetryConditionHeaderValue(TimeSpan.FromMinutes(1)), Reset = reset });
        await using var other = await Fixture.CreateAsync();
        var available = new Remote(other) { Present = true }; await other.ScanAsync(available);
        Assert.True(available.Calls > 0);
        await using var verify = Open();
        var persisted = await verify.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == f.Installation.Id);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(reset), persisted.NextReconciliationAt);
        Assert.False((await verify.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == other.Installation.Id)).VerificationUnknown);
    }

    [Step3cReconciliationFact]
    public async Task ConcurrentRecoveryWritesAreSerializedAndReadDenialCannotRestoreAccess()
    {
        await using var f = await Fixture.CreateAsync();
        await f.ScanAsync(new Remote(f));
        await f.DueAsync(); await f.ScanAsync(new Remote(f) { Present = true });
        await f.RecoverAsync(new Provider(f) { Readable = false });
        await using (var db = Open())
        {
            Assert.Equal(ProjectGitRepositoryAccessStatus.AccessLost, (await db.ProjectGitRepositories.SingleAsync(x => x.Id == f.Link.Id)).AccessStatus);
            Assert.Equal(0, await db.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == f.Link.Id && x.SyncType == GitSyncType.Scheduled));
        }
        var barrier = new ConcurrentWrites();
        await Task.WhenAll(f.RecoverAsync(new Provider(f), barrier), f.RecoverAsync(new Provider(f), barrier));
        await f.RecoverAsync(new Provider(f));
        await using var verify = Open();
        foreach (var link in new[] { f.Link, f.OtherLink })
        {
            Assert.Equal(ProjectGitRepositoryAccessStatus.Active, (await verify.ProjectGitRepositories.SingleAsync(x => x.Id == link.Id)).AccessStatus);
            Assert.Equal(1, await verify.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == link.Id && x.SyncType == GitSyncType.Scheduled));
            Assert.Equal(1, await verify.ProjectGitRepositoryHistory.CountAsync(x => x.ProjectGitRepositoryId == link.Id && x.Action == ProjectGitRepositoryHistoryAction.AccessRestored));
        }
        Assert.Equal(1, await verify.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == f.Link.Id && x.SyncType == GitSyncType.Initial));
        await f.AssertHistoryAsync();
    }

    [Step3cReconciliationFact]
    public async Task ConfirmedSuspensionAndUnknownUninstallNeverDeleteHistoryOrActivatePendingLink()
    {
        await using var f = await Fixture.CreateAsync();
        await using (var db = Open()) { db.Add(f.NewJob()); await db.SaveChangesAsync(); }
        await f.ScanAsync(new Remote(f) { Suspended = true });
        await using (var db = Open())
        {
            Assert.Equal(GitHubAppInstallationStatus.Suspended, (await db.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == f.Installation.Id)).Status);
            Assert.Equal(1, await db.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == f.Link.Id && x.Status == GitSyncJobStatus.Cancelled));
            await db.ProjectGitRepositories.Where(x => x.Id == f.OtherLink.Id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.AccessStatus, ProjectGitRepositoryAccessStatus.PendingAuthorization).SetProperty(x => x.SyncPaused, true));
        }
        await f.DueAsync(); await f.ScanAsync(new Remote(f) { Failure = HttpStatusCode.NotFound });
        var provider = new Provider(f);
        await using (var db = Open())
        {
            var state = await db.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == f.Installation.Id);
            Assert.True(state.VerificationUnknown); Assert.Equal(GitHubAppInstallationStatus.Suspended, state.Status);
            Assert.False(await new GitHubAppRepositoryActivationService(db, new Tokens(f), provider, Sync, f.App).ActivateAsync(f.OtherLink.Id, default));
            Assert.Equal(0, provider.Reads);
            Assert.Equal(0, await db.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == f.OtherLink.Id));
        }
        await f.AssertHistoryAsync();
    }

    [Step3cReconciliationFact]
    public async Task RetryAfterHttpDateSurvivesFreshContext()
    {
        await using var f = await Fixture.CreateAsync();
        var date = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddHours(3).ToUnixTimeSeconds());
        await f.ScanAsync(new Remote(f) { Failure = HttpStatusCode.TooManyRequests, Retry = new RetryConditionHeaderValue(date) });
        await using var db = Open();
        Assert.Equal(date, (await db.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == f.Installation.Id)).NextReconciliationAt);
        var remote = new Remote(f) { Present = true }; await f.ScanAsync(remote); Assert.Equal(0, remote.Calls);
    }

    [Step3cReconciliationFact]
    public async Task AlreadyClaimedJobCannotFetchAfterCommittedRevoke()
    {
        await using var f = await Fixture.CreateAsync();
        await using var db = Open(); var pending = f.NewJob(); db.Add(pending); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var jobs = new GitSyncJobService(db, Sync, Options.Create(new GitHubAppOptions { Enabled = true })); var claimed = await jobs.ClaimNextAsync(TimeSpan.FromMinutes(1), default);
        Assert.NotNull(claimed); Assert.Equal(pending.Id, claimed.Id);
        await f.ScanAsync(new Remote(f));
        var provider = new Provider(f);
        var processor = new GitHubSyncJobProcessor(db, provider, new GitHubCredentialService(db, provider, new Protector()), Sync, new Tokens(f), f.App);
        var failure = await Assert.ThrowsAsync<GitHubIntegrationException>(() => processor.ProcessAsync(claimed, default));
        Assert.Equal("GITHUB_REPOSITORY_NOT_AUTHORIZED", failure.Code); Assert.Equal(0, provider.Reads);
        await jobs.FailAsync(claimed.Id, claimed.LeaseId!.Value, failure.Code, failure.Message, DateTimeOffset.UtcNow, default, retryable: false);
        await f.AssertHistoryAsync();
    }

    [Step3cReconciliationFact]
    public async Task HostedReconciliationRunsWhileSingleWorkerIsBusyAndManualJobCompletes()
    {
        await using var f = await Fixture.CreateAsync();
        var pending = f.NewJob();
        await using (var db = Open())
        {
            db.Add(pending); await db.SaveChangesAsync();
            await db.Set<GitHubAppInstallation>().Where(x => x.Id == f.Installation.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextReconciliationAt, DateTimeOffset.UtcNow.AddHours(1)));
            await db.ProjectGitRepositories.Where(x => x.Id == f.OtherLink.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SyncPaused, true));
        }
        var providerGate = new Gate(); var remoteGate = new Gate(); var provider = new Provider(f) { FirstRead = providerGate };
        var remote = new Remote(f) { Present = true, FirstRequest = remoteGate }; using var http = new HttpClient(remote);
        var options = Options.Create(new GitSyncOptions { Enabled = true, WorkerConcurrency = 1, PollSeconds = 1, ReconciliationPollSeconds = 1 });
        var services = new ServiceCollection();
        services.AddDbContext<UniNetDbContext>(o => o.UseNpgsql(Connection));
        services.AddSingleton<IOptions<GitSyncOptions>>(options);
        services.AddSingleton<IOptions<GitHubAppOptions>>(f.App);
        services.AddSingleton<IGitHubProviderClient>(provider); services.AddSingleton<IGitHubTokenProtector>(new Protector());
        services.AddSingleton<IGitHubAppInstallationTokenProvider>(new Tokens(f));
        services.AddScoped<GitHubCredentialService>(); services.AddScoped<GitHubAppRepositoryActivationService>();
        services.AddScoped<IGitSyncJobService, GitSyncJobService>(); services.AddScoped<IGitSyncJobProcessor, GitHubSyncJobProcessor>();
        services.AddScoped(sp => new GitHubAppInstallationReconciliationService(sp.GetRequiredService<UniNetDbContext>(), http, f.App, options, sp.GetRequiredService<IGitHubAppInstallationTokenProvider>()));
        await using var container = services.BuildServiceProvider();
        using var worker = new GitSyncWorker(container.GetRequiredService<IServiceScopeFactory>(), options, NullLogger<GitSyncWorker>.Instance, f.App);
        try
        {
            await worker.StartAsync(default); await providerGate.WaitForEntryAsync();
            await f.DueAsync(); await remoteGate.WaitForEntryAsync(); // No free job-processing slot exists here.
            remoteGate.Release.TrySetResult();
            var reconciliationDeadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < reconciliationDeadline)
            {
                await using var check = Open();
                if (await check.Set<GitHubAppInstallation>().AnyAsync(x => x.Id == f.Installation.Id && x.LastVerifiedAt != null)) break;
                await Task.Delay(50);
            }
            providerGate.Release.TrySetResult();
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await using var check = Open();
                if (await check.GitSyncJobs.AnyAsync(x => x.Id == pending.Id && x.Status == GitSyncJobStatus.Completed)) break;
                await Task.Delay(100);
            }
        }
        finally { remoteGate.Release.TrySetResult(); providerGate.Release.TrySetResult(); await worker.StopAsync(default); }
        await using var verify = Open();
        Assert.Equal(GitSyncJobStatus.Completed, (await verify.GitSyncJobs.SingleAsync(x => x.Id == pending.Id)).Status);
        Assert.True(await verify.Set<ProjectGitActivityVisibility>().CountAsync(x => x.ProjectGitRepositoryId == f.Link.Id) >= 2);
        Assert.True(await verify.GitActivities.AnyAsync(x => x.Id == f.ActivityId));
        Assert.Equal(0, (await new ProjectGitActivityService(verify).GetAsync(f.Owner.Id, f.OtherProject.Id, 1, 50, null, null, null, default)).TotalCount);
    }
}
