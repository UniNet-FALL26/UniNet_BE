using System.Text.Json;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
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

public sealed class GitHubAppInitialSyncRecoveryPostgresTests
{
    private sealed class RecoveryLogger : ILogger<GitHubAppInitialSyncRecoveryService>
    {
        public readonly ConcurrentQueue<string> Messages = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Enqueue(formatter(state, exception));
    }
    [Step5aPostgresFact]
    public async Task SharedRepositoryRecoveriesRemainProjectScopedAndDoNotAlterOtherCheckpoints()
    {
        await using var db = await GitHubAppMigrationUpgradePostgresTests.CreateAsync("multiproject"); await db.Database.MigrateAsync();
        Account Student(string name) => new() { Email = name + "@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = name, Nickname = name } };
        var a = Student("a"); var b = Student("b");
        Project ProjectFor(Account owner) => new() { Creator = owner.Profile!, Title = "Isolation", ProjectField = "IT", Description = "Fixture", MemberTarget = 1, Status = ProjectStatus.Active };
        var pa = ProjectFor(a); var pb = ProjectFor(b);
        var installation = new GitHubAppInstallation { ProviderInstallationId = 42, OwnerAccountId = 42, OwnerLogin = "example", OwnerType = "User" };
        var repo = new GitHubRepository { ProviderRepositoryId = 99, ProviderOwnerId = 42, OwnerLogin = "example", Name = "repo", FullName = "example/repo", HtmlUrl = "https://github.com/example/repo", IsPrivate = true };
        var grant = new GitHubAppRepositoryGrant { InstallationId = installation.Id, ProviderRepositoryId = 99, VerifiedAt = DateTimeOffset.UtcNow };
        ProjectGitRepository Link(Project p, Account owner) => new() { ProjectId = p.Id, GitHubRepositoryId = repo.Id, LinkedByProfileId = owner.Profile!.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, AccessStatus = ProjectGitRepositoryAccessStatus.Active };
        var la = Link(pa, a); var lb = Link(pb, b);
        GitSyncJob Job(ProjectGitRepository l, GitSyncJobStatus status) => new() { ProjectGitRepositoryId = l.Id, GitHubRepositoryId = repo.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncType = GitSyncType.Initial, Status = status, CompletedAt = DateTimeOffset.UtcNow, ErrorCode = status == GitSyncJobStatus.Failed ? "FIXTURE_FAILURE" : null };
        var ja = Job(la, GitSyncJobStatus.Failed); var jb = Job(lb, GitSyncJobStatus.Completed);
        var ca = new GitSyncCheckpoint { SyncJobId = ja.Id, Category = "issues", Cursor = "2", Status = GitSyncCheckpointStatus.InProgress, ItemsProcessed = 2 };
        var cb = new GitSyncCheckpoint { SyncJobId = jb.Id, Category = "issues", Cursor = "7", Status = GitSyncCheckpointStatus.Completed, ItemsProcessed = 7 };
        var obj = new GitHubProviderObject { GitHubRepositoryId = repo.Id, Category = "commits", ExternalObjectId = "private-b", PayloadJson = "{}", FirstObservedAt = DateTimeOffset.UtcNow, LastObservedAt = DateTimeOffset.UtcNow };
        var activity = new GitActivity { GitHubProviderObjectId = obj.Id, ActivityType = "commit.created", OccurredAt = DateTimeOffset.UtcNow };
        var visibility = new ProjectGitActivityVisibility { ProjectGitRepositoryId = lb.Id, GitActivityId = activity.Id, ObservedAt = DateTimeOffset.UtcNow };
        db.AddRange(a, b, pa, pb, installation, repo, grant, la, lb, ja, jb, ca, cb, obj, activity, visibility); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var app = Options.Create(new GitHubAppOptions { Enabled = true }); var sync = Options.Create(new GitSyncOptions { Enabled = true, WorkerConcurrency = 1, PollSeconds = 1 }); var tokens = new Tokens(); var provider = new Provider();
        GitHubAppInitialSyncRecoveryService Service(UniNetDbContext c) => new(c, app, sync, tokens, provider, NullLogger<GitHubAppInitialSyncRecoveryService>.Instance);
        Assert.Equal(403, (await Assert.ThrowsAsync<GitHubIntegrationException>(() => Service(db).RecoverAsync(a.Id, pb.Id, lb.Id, jb.Id, default))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<GitHubIntegrationException>(() => Service(db).RecoverAsync(a.Id, pa.Id, lb.Id, jb.Id, default))).Status);
        Assert.False((await Service(db).RecoverAsync(a.Id, pa.Id, la.Id, ja.Id, default)).AlreadyQueued);
        Assert.Equal(GitSyncJobStatus.Completed, (await db.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == jb.Id)).Status);
        Assert.Equal("7", (await db.GitSyncCheckpoints.AsNoTracking().SingleAsync(x => x.Id == cb.Id)).Cursor);
        Assert.False(await db.Set<ProjectGitActivityVisibility>().AnyAsync(x => x.ProjectGitRepositoryId == la.Id && x.GitActivityId == activity.Id));
        await db.GitSyncJobs.Where(x => x.Id == ja.Id || x.Id == jb.Id).ExecuteUpdateAsync(x => x.SetProperty(j => j.Status, GitSyncJobStatus.Failed).SetProperty(j => j.CompletedAt, DateTimeOffset.UtcNow));
        await db.GitSyncCheckpoints.Where(x => x.Id == cb.Id).ExecuteUpdateAsync(x => x.SetProperty(c => c.Cursor, "3").SetProperty(c => c.Status, GitSyncCheckpointStatus.InProgress));
        async Task Recover(Account actor, Project p, ProjectGitRepository link, GitSyncJob job)
        { await using var context = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(db.Database.GetConnectionString()).Options); try { await Service(context).RecoverAsync(actor.Id, p.Id, link.Id, job.Id, default); } catch (GitHubIntegrationException ex) when (ex.Code == "GITHUB_SYNC_CONFLICT") { await Service(context).RecoverAsync(actor.Id, p.Id, link.Id, job.Id, default); } }
        await Task.WhenAll(Recover(a, pa, la, ja), Recover(b, pb, lb, jb));
        Assert.Equal(2, await db.GitSyncJobs.CountAsync(x => (x.Id == ja.Id || x.Id == jb.Id) && x.Status == GitSyncJobStatus.Pending));
        Assert.Equal("2", (await db.GitSyncCheckpoints.AsNoTracking().SingleAsync(x => x.Id == ca.Id)).Cursor);
        Assert.Equal("3", (await db.GitSyncCheckpoints.AsNoTracking().SingleAsync(x => x.Id == cb.Id)).Cursor);
        Assert.True((await Service(db).RecoverAsync(a.Id, pa.Id, la.Id, ja.Id, default)).AlreadyQueued);
        Assert.Equal(1, await db.Set<ProjectGitActivityVisibility>().CountAsync(x => x.GitActivityId == activity.Id));
        var services = new ServiceCollection();
        services.AddDbContext<UniNetDbContext>(o => o.UseNpgsql(db.Database.GetConnectionString()));
        services.AddSingleton<IOptions<GitSyncOptions>>(sync); services.AddSingleton<IOptions<GitHubAppOptions>>(app);
        services.AddSingleton<IGitHubTokenProtector>(new Protector()); services.AddSingleton<IGitHubAppInstallationTokenProvider>(tokens);
        services.AddSingleton<IGitHubProviderClient>(provider); services.AddScoped<GitHubCredentialService>();
        using var http = new HttpClient(new RejectNetworkHandler());
        services.AddScoped(sp => new GitHubAppInstallationReconciliationService(sp.GetRequiredService<UniNetDbContext>(), http, app, sync, tokens));
        services.AddScoped<GitHubAppRepositoryActivationService>();
        services.AddScoped<IGitSyncJobService, GitSyncJobService>(); services.AddScoped<IGitSyncJobProcessor, GitHubSyncJobProcessor>();
        await using var container = services.BuildServiceProvider();
        using var worker = new GitSyncWorker(container.GetRequiredService<IServiceScopeFactory>(), sync, NullLogger<GitSyncWorker>.Instance, app);
        try
        {
            await worker.StartAsync(default);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (DateTimeOffset.UtcNow < deadline && await db.GitSyncJobs.AsNoTracking().CountAsync(x => (x.Id == ja.Id || x.Id == jb.Id) && x.Status == GitSyncJobStatus.Completed) != 2)
                await Task.Delay(100);
        }
        finally { await worker.StopAsync(default); }
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.GitSyncJobs.CountAsync(x => (x.Id == ja.Id || x.Id == jb.Id) && x.Status == GitSyncJobStatus.Completed));
        Assert.Equal("2", (await db.GitSyncCheckpoints.SingleAsync(x => x.Id == ca.Id && x.SyncJobId == ja.Id && x.Status == GitSyncCheckpointStatus.Completed)).Cursor);
        Assert.Equal("3", (await db.GitSyncCheckpoints.SingleAsync(x => x.Id == cb.Id && x.SyncJobId == jb.Id && x.Status == GitSyncCheckpointStatus.Completed)).Cursor);
        Assert.Contains(2, provider.IssuePages); Assert.Contains(3, provider.IssuePages);
        Assert.Equal(1, await db.Set<ProjectGitActivityVisibility>().CountAsync(x => x.GitActivityId == activity.Id && x.ProjectGitRepositoryId == lb.Id));
        Assert.False(await db.Set<ProjectGitActivityVisibility>().AnyAsync(x => x.GitActivityId == activity.Id && x.ProjectGitRepositoryId == la.Id));
        var issueVisibility = await (from v in db.ProjectGitActivityVisibilities
            join item in db.GitActivities on v.GitActivityId equals item.Id
            join objRow in db.GitHubProviderObjects on item.GitHubProviderObjectId equals objRow.Id
            where objRow.Category == "issues" && (objRow.ExternalObjectId == "8002" || objRow.ExternalObjectId == "8003")
            select new { v.ProjectGitRepositoryId, objRow.ExternalObjectId }).ToListAsync();
        Assert.Contains(issueVisibility, x => x.ProjectGitRepositoryId == la.Id && x.ExternalObjectId == "8002");
        Assert.Contains(issueVisibility, x => x.ProjectGitRepositoryId == lb.Id && x.ExternalObjectId == "8003");
        Assert.DoesNotContain(issueVisibility, x => x.ProjectGitRepositoryId == la.Id && x.ExternalObjectId == "8003");
        Assert.DoesNotContain(issueVisibility, x => x.ProjectGitRepositoryId == lb.Id && x.ExternalObjectId == "8002");
        Assert.Equal(2, await db.GitSyncJobs.CountAsync(x => x.GitHubRepositoryId == repo.Id && x.SyncType == GitSyncType.Initial));
        Assert.Equal(await db.GitActivities.CountAsync(), await db.GitActivities.Select(x => new { x.GitHubProviderObjectId, x.ActivityType }).Distinct().CountAsync());
    }
    private sealed class RejectNetworkHandler : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new InvalidOperationException("Unexpected network request in fixture"); }
    private sealed class Protector : IGitHubTokenProtector { public string Protect(string x) => throw new InvalidOperationException(); public string Unprotect(string x) => throw new InvalidOperationException("OAuth fallback"); }
    private sealed class Tokens : IGitHubAppInstallationTokenProvider
    { public Task<string> GetAsync(long installation, long repo, CancellationToken ct) { Assert.Equal(42, installation); Assert.Equal(99, repo); return Task.FromResult("SENSITIVE_TEST_TOKEN_MARKER"); } public void InvalidateInstallation(long id) { } }
    private sealed class Provider : IGitHubProviderClient
    {
        public int IssuePage;
        public readonly ConcurrentBag<int> IssuePages = new();
        public bool InterruptSecondIssuePage;
        public Task<GitHubRemoteRepository> GetRepositoryAsync(string token, long id, CancellationToken ct) => Task.FromResult(new GitHubRemoteRepository(id, 42, "example", "repo", "example/repo", "https://github.com/example/repo", null, "main", true, false, true));
        public Task<GitHubSyncPage> GetSyncPageAsync(string token, string repo, string category, string? scope, int page, int size, CancellationToken ct)
        {
            if (category == "issues") { IssuePage = page; IssuePages.Add(page); }
            if (category == "issues" && page == 2 && InterruptSecondIssuePage) { InterruptSecondIssuePage = false; throw new InvalidOperationException("injected interruption after page commit"); }
            var issueId = 8000 + page;
            using var doc = JsonDocument.Parse(category == "issues" ? $"[{{\"id\":{issueId},\"number\":{page + 7},\"state\":\"open\",\"created_at\":\"2026-11-04T10:00:00Z\",\"user\":{{\"id\":42,\"login\":\"user\"}}}}]" : "[]");
            return Task.FromResult(new GitHubSyncPage(doc.RootElement.EnumerateArray().Select(x => x.Clone()).ToArray(), category == "issues" && page == 1, 5000, null));
        }
        public Task<GitHubOAuthTokens> ExchangeCodeAsync(string x, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubOAuthTokens> RefreshAsync(string x, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubUser> GetAuthenticatedUserAsync(string x, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<GitHubRemoteRepository>> GetRepositoriesAsync(string x, int p, int s, CancellationToken ct) => throw new NotSupportedException();
    }

    [Step5aPostgresFact]
    public async Task FailedInitialIsRecoveredOnceAndResumesCheckpointAfterLeaseExpiry()
    {
        var metrics = new ConcurrentDictionary<string, long>(); var labels = new ConcurrentBag<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) => { if (instrument.Meter.Name == "UniNet.GitHubApp") meterListener.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => { metrics.AddOrUpdate(instrument.Name, value, (_, current) => current + value); foreach (var tag in tags) labels.Add(tag.Key); }); listener.Start();
        await using var db = await GitHubAppMigrationUpgradePostgresTests.CreateAsync("initial_recovery"); await db.Database.MigrateAsync();
        var owner = new Account { Email = "recovery@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
        var outsider = new Account { Email = "outside@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Outside", Nickname = "Outside" } };
        var project = new Project { Creator = owner.Profile!, Title = "Recovery", ProjectField = "IT", Description = "Fixture", MemberTarget = 1, Status = ProjectStatus.Active };
        var installation = new GitHubAppInstallation { ProviderInstallationId = 42, OwnerAccountId = 42, OwnerLogin = "example", OwnerType = "User" };
        var repo = new GitHubRepository { ProviderRepositoryId = 99, ProviderOwnerId = 42, OwnerLogin = "example", Name = "repo", FullName = "example/repo", HtmlUrl = "https://github.com/example/repo", IsPrivate = true };
        var grant = new GitHubAppRepositoryGrant { InstallationId = installation.Id, ProviderRepositoryId = 99, VerifiedAt = DateTimeOffset.UtcNow };
        var link = new ProjectGitRepository { ProjectId = project.Id, GitHubRepositoryId = repo.Id, LinkedByProfileId = owner.Profile!.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, AccessStatus = ProjectGitRepositoryAccessStatus.Active };
        var job = new GitSyncJob { ProjectGitRepositoryId = link.Id, GitHubRepositoryId = repo.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncType = GitSyncType.Initial, Status = GitSyncJobStatus.Failed, CompletedAt = DateTimeOffset.UtcNow, RetryCount = 8, ErrorCode = "GITHUB_TEMPORARY" };
        var checkpoint = new GitSyncCheckpoint { SyncJobId = job.Id, Category = "issues", ScopeKey = "", Cursor = "1", Status = GitSyncCheckpointStatus.InProgress, ItemsProcessed = 4 };
        var oldObject = new GitHubProviderObject { GitHubRepositoryId = repo.Id, Category = "commits", ExternalObjectId = "prior", PayloadJson = "{}", RetrievalMetadataJson = "{}", FirstObservedAt = DateTimeOffset.UtcNow, LastObservedAt = DateTimeOffset.UtcNow };
        var oldActivity = new GitActivity { GitHubProviderObjectId = oldObject.Id, ActivityType = "commit.created", OccurredAt = DateTimeOffset.UtcNow };
        var visibility = new ProjectGitActivityVisibility { ProjectGitRepositoryId = link.Id, GitActivityId = oldActivity.Id, ObservedAt = DateTimeOffset.UtcNow };
        db.AddRange(owner, outsider, project, installation, repo, grant, link, job, checkpoint, oldObject, oldActivity, visibility); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var app = Options.Create(new GitHubAppOptions { Enabled = true }); var sync = Options.Create(new GitSyncOptions { Enabled = true }); var tokens = new Tokens(); var provider = new Provider();
        var recoveryLogger = new RecoveryLogger();
        GitHubAppInitialSyncRecoveryService Service(UniNetDbContext context) => new(context, app, sync, tokens, provider, recoveryLogger);
        Assert.Equal(403, (await Assert.ThrowsAsync<GitHubIntegrationException>(() => Service(db).RecoverAsync(outsider.Id, project.Id, link.Id, job.Id, default))).Status);
        foreach (var state in new[] { "revoked", "suspended", "unknown" })
        {
            await db.Set<GitHubAppRepositoryGrant>().Where(x => x.Id == grant.Id).ExecuteUpdateAsync(x => x.SetProperty(g => g.RevokedAt, state == "revoked" ? DateTimeOffset.UtcNow : (DateTimeOffset?)null));
            await db.Set<GitHubAppInstallation>().Where(x => x.Id == installation.Id).ExecuteUpdateAsync(x => x.SetProperty(i => i.Status, state == "suspended" ? GitHubAppInstallationStatus.Suspended : GitHubAppInstallationStatus.Active).SetProperty(i => i.VerificationUnknown, state == "unknown"));
            Assert.Equal(409, (await Assert.ThrowsAsync<GitHubIntegrationException>(() => Service(db).RecoverAsync(owner.Id, project.Id, link.Id, job.Id, default))).Status);
        }
        await db.Set<GitHubAppInstallation>().Where(x => x.Id == installation.Id).ExecuteUpdateAsync(x => x.SetProperty(i => i.VerificationUnknown, false));
        await db.ProjectGitRepositories.Where(x => x.Id == link.Id).ExecuteUpdateAsync(x => x.SetProperty(l => l.AccessStatus, ProjectGitRepositoryAccessStatus.AccessLost));
        Assert.Equal(409, (await Assert.ThrowsAsync<GitHubIntegrationException>(() => Service(db).RecoverAsync(owner.Id, project.Id, link.Id, job.Id, default))).Status);
        await new GitHubAppRepositoryActivationService(db, tokens, provider, sync, app).RetryPendingAsync(default);
        Assert.Equal(ProjectGitRepositoryAccessStatus.Active, (await db.ProjectGitRepositories.AsNoTracking().SingleAsync(x => x.Id == link.Id)).AccessStatus);
        Assert.Equal(0, await db.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == link.Id && x.SyncType == GitSyncType.Scheduled));
        async Task<string> Recover()
        {
            await using var context = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(db.Database.GetConnectionString()).Options);
            try { return (await Service(context).RecoverAsync(owner.Id, project.Id, link.Id, job.Id, default)).AlreadyQueued ? "existing" : "recovered"; }
            catch (GitHubIntegrationException ex) when (ex.Code == "GITHUB_SYNC_CONFLICT") { return "conflict"; }
        }
        var outcomes = await Task.WhenAll(Recover(), Recover()); Assert.Single(outcomes, x => x == "recovered"); Assert.All(outcomes, x => Assert.Contains(x, new[] { "recovered", "existing", "conflict" }));
        Assert.True((await Service(db).RecoverAsync(owner.Id, project.Id, link.Id, job.Id, default)).AlreadyQueued);
        var noLease = new GitHubSyncJobProcessor(db, provider, new GitHubCredentialService(db, provider, new Protector()), sync, tokens, app);
        Assert.Equal("GITHUB_SYNC_LEASE_LOST", (await Assert.ThrowsAsync<GitHubIntegrationException>(() => noLease.ProcessAsync(job, default))).Code);
        var jobs = new GitSyncJobService(db, sync, app); var claimed = await jobs.ClaimNextAsync(TimeSpan.FromMinutes(1), default); Assert.Equal(job.Id, claimed!.Id);
        await db.GitSyncJobs.Where(x => x.Id == job.Id).ExecuteUpdateAsync(x => x.SetProperty(j => j.LeaseExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        var staleProcessor = new GitHubSyncJobProcessor(db, provider, new GitHubCredentialService(db, provider, new Protector()), sync, tokens, app);
        Assert.Equal("GITHUB_SYNC_LEASE_LOST", (await Assert.ThrowsAsync<GitHubIntegrationException>(() => staleProcessor.ProcessAsync(claimed, default))).Code);
        Assert.Equal("GITHUB_SYNC_LEASE_LOST", (await Assert.ThrowsAsync<GitHubIntegrationException>(() => jobs.CompleteAsync(job.Id, claimed.LeaseId!.Value, default))).Code);
        Assert.Equal("GITHUB_SYNC_LEASE_LOST", (await Assert.ThrowsAsync<GitHubIntegrationException>(() => jobs.FailAsync(job.Id, claimed.LeaseId!.Value, "FIXTURE", "fixture", DateTimeOffset.UtcNow, default))).Code);
        db.ChangeTracker.Clear(); var reclaimed = await jobs.ClaimNextAsync(TimeSpan.FromMinutes(1), default); Assert.Equal(job.Id, reclaimed!.Id); Assert.NotEqual(claimed.LeaseId, reclaimed.LeaseId);
        var processor = new GitHubSyncJobProcessor(db, provider, new GitHubCredentialService(db, provider, new Protector()), sync, tokens, app);
        Assert.Equal("GITHUB_SYNC_LEASE_LOST", (await Assert.ThrowsAsync<GitHubIntegrationException>(() => processor.ProcessAsync(claimed, default))).Code);
        provider.InterruptSecondIssuePage = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(reclaimed, default));
        db.ChangeTracker.Clear(); Assert.Equal("2", (await db.GitSyncCheckpoints.SingleAsync(x => x.Id == checkpoint.Id)).Cursor);
        await db.GitSyncJobs.Where(x => x.Id == job.Id).ExecuteUpdateAsync(x => x.SetProperty(j => j.LeaseExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        db.ChangeTracker.Clear(); var secondPageOwner = await jobs.ClaimNextAsync(TimeSpan.FromMinutes(1), default); Assert.Equal(job.Id, secondPageOwner!.Id);
        await processor.ProcessAsync(secondPageOwner, default);
        var activityCount = await db.GitActivities.CountAsync();
        // Simulate process loss after activity/checkpoint commit but before job completion.
        await db.GitSyncJobs.Where(x => x.Id == job.Id).ExecuteUpdateAsync(x => x.SetProperty(j => j.LeaseExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        db.ChangeTracker.Clear(); var replay = await jobs.ClaimNextAsync(TimeSpan.FromMinutes(1), default); Assert.Equal(job.Id, replay!.Id);
        await processor.ProcessAsync(replay, default); Assert.Equal(activityCount, await db.GitActivities.CountAsync());
        await jobs.CompleteAsync(job.Id, replay.LeaseId!.Value, default); db.ChangeTracker.Clear();
        Assert.Equal(2, provider.IssuePage); Assert.Equal(GitSyncJobStatus.Completed, (await db.GitSyncJobs.SingleAsync(x => x.Id == job.Id)).Status);
        Assert.Equal(GitSyncCheckpointStatus.Completed, (await db.GitSyncCheckpoints.SingleAsync(x => x.Id == checkpoint.Id)).Status);
        Assert.Contains(await db.GitActivities.ToListAsync(), x => x.ActivityType.StartsWith("issue."));
        Assert.True(await db.GitActivities.AnyAsync(x => x.Id == oldActivity.Id));
        Assert.True(await db.Set<ProjectGitActivityVisibility>().AnyAsync(x => x.Id == visibility.Id));
        Assert.Equal(1, await db.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == link.Id && x.SyncType == GitSyncType.Initial));
        var terminalJob = new GitSyncJob { ProjectGitRepositoryId = link.Id, GitHubRepositoryId = repo.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncType = GitSyncType.Manual };
        db.Add(terminalJob); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var terminalClaim = await jobs.ClaimNextAsync(TimeSpan.FromMinutes(1), default); Assert.Equal(terminalJob.Id, terminalClaim!.Id);
        Assert.False(await jobs.FailAsync(terminalJob.Id, terminalClaim.LeaseId!.Value, "GITHUB_DENIED", "Fixture denial", DateTimeOffset.UtcNow, default, retryable: false));
        db.ChangeTracker.Clear(); var manual = await jobs.RequestManualSyncAsync(owner.Id, project.Id, link.Id, default); Assert.False(manual.WasAlreadyQueued);
        db.ChangeTracker.Clear(); var retryClaim = await jobs.ClaimNextAsync(TimeSpan.FromMinutes(1), default); Assert.NotNull(retryClaim);
        Assert.True(await jobs.FailAsync(retryClaim!.Id, retryClaim.LeaseId!.Value, "GITHUB_TEMPORARY", "Fixture", DateTimeOffset.UtcNow.AddMinutes(1), default));
        Assert.Contains(recoveryLogger.Messages, message => message.Contains("GITHUB_TEMPORARY") && message.Contains(job.Id.ToString()));
        Assert.DoesNotContain(recoveryLogger.Messages, message => message.Contains("SENSITIVE_TEST_TOKEN_MARKER"));
        Assert.True(metrics.GetValueOrDefault("uninet.github_app.sync.initial_recovered") >= 1);
        Assert.True(metrics.GetValueOrDefault("uninet.github_app.access.restored") >= 1);
        Assert.True(metrics.GetValueOrDefault("uninet.github_app.sync.claimed") >= 1);
        Assert.True(metrics.GetValueOrDefault("uninet.github.sync.completed") >= 1);
        Assert.True(metrics.GetValueOrDefault("uninet.github_app.sync.failed") >= 1);
        Assert.True(metrics.GetValueOrDefault("uninet.github_app.sync.enqueued") >= 1);
        Assert.True(metrics.GetValueOrDefault("uninet.github_app.sync.retry") >= 1);
        Assert.Empty(labels);
    }
}
