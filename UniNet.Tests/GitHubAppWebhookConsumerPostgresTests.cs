using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using UniNet.API.Services;
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

public sealed class Step4bPostgresFactAttribute : FactAttribute
{
    public Step4bPostgresFactAttribute()
    { if (Environment.GetEnvironmentVariable("UNINET_STEP4B_DISPOSABLE") != "1") Skip = "Requires explicitly enabled disposable PostgreSQL."; }
}

[Collection("GitHub App lifecycle PostgreSQL")]
public sealed class GitHubAppWebhookConsumerPostgresTests
{
    private static string Database => GitHubAppDisposableDatabase.Name("uninet_step4b_step4c_regression");
    private static string Connection => "Host=127.0.0.1;Port=55433;Database=" + Database + ";Username=postgres;Timeout=5";
    private const string Secret = "app-webhook-test-fixture";
    private static UniNetDbContext Open() => new(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(Connection).Options);
    private static string Sign(byte[] payload) => "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), payload)).ToLowerInvariant();
    private sealed class Gate
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task WaitAsync(CancellationToken ct) { Entered.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), ct); }
    }
    private sealed class Remote(Fixture fixture) : HttpMessageHandler
    {
        public bool Missing, Suspended, Present = true;
        public HttpStatusCode? Failure;
        public long AppId = 42, OwnerId = 9;
        public Gate? AppGate;
        public int Requests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            if (request.RequestUri!.AbsolutePath == "/app" && AppGate is { } gate) { AppGate = null; await gate.WaitAsync(ct); }
            if (Failure is { } failure)
            {
                var error = new HttpResponseMessage(failure);
                if (failure == HttpStatusCode.TooManyRequests) error.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
                return error;
            }
            string body;
            if (request.RequestUri.AbsolutePath == "/app") body = $"{{\"id\":{AppId}}}";
            else if (request.Method == HttpMethod.Post) body = "{\"token\":\"installation-fixture\"}";
            else if (request.RequestUri.AbsolutePath == "/installation/repositories")
                body = Present ? $"{{\"total_count\":1,\"repositories\":[{{\"id\":{fixture.Repository.ProviderRepositoryId}}}]}}" : "{\"total_count\":0,\"repositories\":[]}";
            else
            {
                Assert.Equal($"/app/installations/{fixture.Installation.ProviderInstallationId}", request.RequestUri.AbsolutePath);
                if (Missing) return new HttpResponseMessage(HttpStatusCode.NotFound);
                body = $"{{\"id\":{fixture.Installation.ProviderInstallationId},\"app_id\":{AppId},\"account\":{{\"id\":{OwnerId}}},\"suspended_at\":{(Suspended ? "\"2026-01-01T00:00:00Z\"" : "null")}}}";
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }
    private sealed class Tokens(Fixture fixture) : IGitHubAppInstallationTokenProvider
    {
        public int Invalidations;
        public Task<string> GetAsync(long installationId, long repositoryId, CancellationToken ct)
        {
            if (installationId != fixture.Installation.ProviderInstallationId || repositoryId != fixture.Repository.ProviderRepositoryId)
                throw new GitHubIntegrationException("GITHUB_APP_ACCESS_DENIED", "Different fixture.", 403);
            return Task.FromResult("repository-scoped-fixture");
        }
        public void InvalidateInstallation(long installationId) { if (installationId == fixture.Installation.ProviderInstallationId) Invalidations++; }
    }
    private sealed class RepositoryProvider(Fixture f) : IGitHubProviderClient
    {
        public Task<GitHubRemoteRepository> GetRepositoryAsync(string token, long id, CancellationToken ct)
        { Assert.Equal("repository-scoped-fixture", token); Assert.Equal(f.Repository.ProviderRepositoryId, id); return Task.FromResult(new GitHubRemoteRepository(id, 9, "fixture", "repo", "fixture/repo", "https://github.com/fixture/repo", null, "main", true, false, true)); }
        public Task<GitHubOAuthTokens> ExchangeCodeAsync(string code, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubOAuthTokens> RefreshAsync(string token, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubUser> GetAuthenticatedUserAsync(string token, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<GitHubRemoteRepository>> GetRepositoriesAsync(string token, int page, int size, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubSyncPage> GetSyncPageAsync(string token, string repo, string category, string? scope, int page, int size, CancellationToken ct) => throw new NotSupportedException();
    }

    // Hold the unrelated sync-processing slot so the test observes only lifecycle and reconciliation work.
    private sealed class BlockedSyncClaims : IGitSyncJobService
    {
        public async Task<GitSyncJob?> ClaimNextAsync(TimeSpan leaseDuration, CancellationToken ct)
        { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return null; }
        public Task<bool> RenewLeaseAsync(Guid id, Guid lease, TimeSpan duration, CancellationToken ct) => throw new NotSupportedException();
        public Task CompleteAsync(Guid id, Guid lease, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> FailAsync(Guid id, Guid lease, string code, string message, DateTimeOffset retry, CancellationToken ct, bool retryable = true) => throw new NotSupportedException();
        public Task<int> EnqueueDueScheduledJobsAsync(DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitSyncRequestResult> RequestManualSyncAsync(Guid account, Guid project, Guid link, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProjectGitSyncStatusResponse> UpdateSyncSettingsAsync(Guid account, Guid project, Guid link, GitSyncSettingsRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitSyncJobPage> GetJobsAsync(Guid account, Guid project, Guid link, int page, int size, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProjectGitSyncStatusPage> GetProjectStatusAsync(Guid account, Guid project, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Account Owner = null!;
        public Project Project = null!, OtherProject = null!, OAuthProject = null!;
        public GitHubAppInstallation Installation = null!;
        public GitHubRepository Repository = null!;
        public GitHubAppRepositoryGrant Grant = null!;
        public ProjectGitRepository Link = null!, OtherLink = null!, OAuthLink = null!;
        public GitHubOAuthConnection OAuth = null!;
        public GitSyncJob Initial = null!, Pending = null!;
        public Guid ActivityId, CheckpointId;
        public readonly IGitHubTokenProtector Protector = new GitHubTokenProtector(new EphemeralDataProtectionProvider());
        public IOptions<GitHubAppOptions> App = null!;
        public IOptions<GitSyncOptions> Sync = Options.Create(new GitSyncOptions { Enabled = true, WebhookLeaseSeconds = 60, WebhookMaxAttempts = 3, RetryBaseDelaySeconds = 1 });
        public Remote Http = null!;
        public Tokens Tokens = null!;
        private readonly List<string> deliveries = [];

        public static async Task<Fixture> CreateAsync(bool persistInstallation = true)
        {
            await using var db = Open(); await db.Database.OpenConnectionAsync();
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            Assert.Equal(Database, connection.Database); Assert.Equal("127.0.0.1", connection.Host); Assert.Equal(55433, connection.Port);
            await db.Database.EnsureCreatedAsync();
            var f = new Fixture();
            f.Owner = new Account { Email = $"step4b-{Guid.NewGuid():N}@example.test", Role = AccountRole.Student, Status = AccountStatus.Active,
                Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
            Project MakeProject() => new() { Creator = f.Owner.Profile!, Title = "Webhook lifecycle", ProjectField = "Engineering", Description = "Test",
                MemberTarget = 5, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Visibility = ProjectVisibility.Private,
                Status = ProjectStatus.Active, RecruitmentStatus = RecruitmentStatus.Open };
            f.Project = MakeProject(); f.OtherProject = MakeProject(); f.OAuthProject = MakeProject();
            f.Installation = new() { ProviderInstallationId = Random.Shared.NextInt64(10000000, long.MaxValue / 2), OwnerAccountId = 9,
                OwnerLogin = "fixture", OwnerType = "User" };
            f.Repository = new() { ProviderRepositoryId = Random.Shared.NextInt64(10000000, long.MaxValue / 2), ProviderOwnerId = 9,
                OwnerLogin = "fixture", Name = "repo", FullName = "fixture/repo", HtmlUrl = "https://github.com/fixture/repo", IsPrivate = true };
            f.Grant = new() { InstallationId = f.Installation.Id, ProviderRepositoryId = f.Repository.ProviderRepositoryId, VerifiedAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
            ProjectGitRepository MakeLink(Project project) => new() { ProjectId = project.Id, GitHubRepositoryId = f.Repository.Id, LinkedByProfileId = f.Owner.Profile!.Id,
                CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id, AccessStatus = ProjectGitRepositoryAccessStatus.Active, LinkedAt = DateTimeOffset.UtcNow.AddDays(-2) };
            f.Link = MakeLink(f.Project); f.OtherLink = MakeLink(f.OtherProject);
            f.OAuth = new GitHubOAuthConnection { AccountId = f.Owner.Id, ProviderAccountId = Guid.NewGuid().ToString("N"), Login = "fixture", AccessTokenEncrypted = "test-encrypted" };
            f.OAuthLink = new ProjectGitRepository { ProjectId = f.OAuthProject.Id, GitHubRepositoryId = f.Repository.Id, LinkedByProfileId = f.Owner.Profile!.Id,
                CredentialSource = GitRepositoryCredentialSource.OAuth, OAuthConnectionId = f.OAuth.Id, AccessStatus = ProjectGitRepositoryAccessStatus.Active };
            f.Initial = new GitSyncJob { ProjectGitRepositoryId = f.Link.Id, GitHubRepositoryId = f.Repository.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp,
                GitHubAppRepositoryGrantId = f.Grant.Id, SyncType = GitSyncType.Initial, Status = GitSyncJobStatus.Completed, CompletedAt = DateTimeOffset.UtcNow.AddDays(-1) };
            f.Pending = new GitSyncJob { ProjectGitRepositoryId = f.Link.Id, GitHubRepositoryId = f.Repository.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp,
                GitHubAppRepositoryGrantId = f.Grant.Id, SyncType = GitSyncType.Manual, Status = GitSyncJobStatus.Pending };
            if (persistInstallation)
            {
                var obj = new GitHubProviderObject { GitHubRepositoryId = f.Repository.Id, Category = "commits", ExternalObjectId = "fixture-commit", PayloadJson = "{}",
                    FirstObservedAt = DateTimeOffset.UtcNow.AddDays(-1), LastObservedAt = DateTimeOffset.UtcNow.AddDays(-1) };
                var activity = new GitActivity { GitHubProviderObjectId = obj.Id, ActivityType = "commit.created", OccurredAt = DateTimeOffset.UtcNow.AddDays(-1) };
                var checkpoint = new GitSyncCheckpoint { SyncJobId = f.Initial.Id, Category = "commits", ScopeKey = "main", Status = GitSyncCheckpointStatus.Completed, ItemsProcessed = 1 };
                f.ActivityId = activity.Id; f.CheckpointId = checkpoint.Id;
                db.AddRange(f.Owner, f.Project, f.OtherProject, f.OAuthProject, f.Installation, f.Repository, f.Grant, f.Link, f.OtherLink, f.OAuth, f.OAuthLink, f.Initial, f.Pending, obj, activity, checkpoint,
                    new ProjectGitActivityGrant { ProjectGitRepositoryId = f.Link.Id, GitHubProviderObjectId = obj.Id, ObservedAt = obj.FirstObservedAt },
                    new ProjectGitActivityVisibility { ProjectGitRepositoryId = f.Link.Id, GitActivityId = activity.Id, ObservedAt = obj.FirstObservedAt });
                await db.SaveChangesAsync();
            }
            using var rsa = RSA.Create(2048);
            f.App = Options.Create(new GitHubAppOptions { Enabled = true, AppId = 42, ClientId = "app-fixture", PrivateKeyPem = rsa.ExportRSAPrivateKeyPem(), WebhookSecret = Secret });
            f.Http = new Remote(f); f.Tokens = new Tokens(f); return f;
        }
        public byte[] Body(string action, string eventName = "installation", long appId = 42, long ownerId = 9, long? installationId = null,
            long[]? added = null, long[]? removed = null)
        {
            var install = new { id = installationId ?? Installation.ProviderInstallationId, app_id = appId, account = new { id = ownerId, login = "fixture", type = "User" } };
            var body = eventName == "installation_repositories"
                ? JsonSerializer.Serialize(new { action, installation = install, repositories_added = (added ?? []).Select(x => new { id = x }), repositories_removed = (removed ?? []).Select(x => new { id = x }) })
                : JsonSerializer.Serialize(new { action, installation = install });
            return Encoding.UTF8.GetBytes(body);
        }
        public async Task<string> StageAsync(string action, string eventName = "installation", byte[]? bytes = null, string? deliveryId = null)
        {
            var id = deliveryId ?? Guid.NewGuid().ToString(); if (!deliveries.Contains(id)) deliveries.Add(id);
            bytes ??= Body(action, eventName);
            await using var db = Open();
            var service = new GitHubAppWebhookService(db, Protector, App, Options.Create(new GitHubWebhookOptions { Secret = "legacy-test-fixture" }));
            await service.HandleAsync(id, eventName, Sign(bytes), bytes, default); return id;
        }
        public GitHubAppWebhookConsumer Consumer(UniNetDbContext db, IGitHubTokenProtector? protector = null)
        {
            var http = new HttpClient(Http, disposeHandler: false);
            var reconciliation = new GitHubAppInstallationReconciliationService(db, http, App, Sync, Tokens);
            return new GitHubAppWebhookConsumer(db, protector ?? Protector, Tokens, reconciliation, App, Sync);
        }
        public async Task<bool> ProcessAsync(IGitHubTokenProtector? protector = null)
        { await using var db = Open(); return await Consumer(db, protector).ProcessNextAsync(default); }
        public async Task ReconcileAsync()
        { await using var db = Open(); using var http = new HttpClient(Http, false); await new GitHubAppInstallationReconciliationService(db, http, App, Sync, Tokens).ReconcileAsync(default); }
        public async Task RecoverAsync()
        { await using var db = Open(); await new GitHubAppRepositoryActivationService(db, Tokens, new RepositoryProvider(this), Sync, App).RetryPendingAsync(default); }
        public async Task<GitHubAppWebhookDelivery> GetAsync(string id)
        { await using var db = Open(); return await db.GitHubAppWebhookDeliveries.AsNoTracking().SingleAsync(x => x.DeliveryId == id); }
        public async Task MakeDueAsync(string id)
        { await using var db = Open(); await db.GitHubAppWebhookDeliveries.Where(x => x.DeliveryId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1))); }
        public async Task AssertHistoryAsync()
        {
            await using var db = Open(); Assert.True(await db.GitActivities.AnyAsync(x => x.Id == ActivityId));
            Assert.True(await db.GitSyncCheckpoints.AnyAsync(x => x.Id == CheckpointId && x.ItemsProcessed == 1));
            Assert.Equal(1, (await new ProjectGitActivityService(db).GetAsync(Owner.Id, Project.Id, 1, 50, null, null, null, default)).TotalCount);
            Assert.Equal(0, (await new ProjectGitActivityService(db).GetAsync(Owner.Id, OtherProject.Id, 1, 50, null, null, null, default)).TotalCount);
        }
        public async ValueTask DisposeAsync()
        {
            await using var db = Open();
            await db.Set<GitHubAppInstallation>().Where(x => x.ProviderInstallationId == Installation.ProviderInstallationId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextReconciliationAt, DateTimeOffset.UtcNow.AddYears(1)));
            await db.ProjectGitRepositories.Where(x => x.Id == Link.Id || x.Id == OtherLink.Id || x.Id == OAuthLink.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UnlinkedAt, DateTimeOffset.UtcNow));
            await db.GitHubAppWebhookDeliveries.Where(x => deliveries.Contains(x.DeliveryId) && x.Status != GitHubAppWebhookDeliveryStatus.Processed)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, GitHubAppWebhookDeliveryStatus.Unsupported)
                    .SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null));
            Http.Dispose();
        }
    }

    [Step4bPostgresFact]
    public async Task DeletedIsIdempotentAndPreservesSharedAndOAuthHistory()
    {
        await using var f = await Fixture.CreateAsync(); f.Http.Missing = true;
        var id = await f.StageAsync("deleted"); Assert.True(await f.ProcessAsync());
        var delivery = await f.GetAsync(id); Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, delivery.Status); Assert.NotNull(delivery.ProcessedAt);
        await f.StageAsync("deleted", deliveryId: id); Assert.False(await f.ProcessAsync());
        var repeated = await f.StageAsync("deleted"); Assert.True(await f.ProcessAsync()); Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(repeated)).Status);
        await using var db = Open();
        Assert.Equal(GitHubAppInstallationStatus.Uninstalled, (await db.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == f.Installation.Id)).Status);
        Assert.Equal(2, await db.ProjectGitRepositories.CountAsync(x => (x.Id == f.Link.Id || x.Id == f.OtherLink.Id) && x.AccessStatus == ProjectGitRepositoryAccessStatus.AccessLost));
        Assert.Equal(2, await db.ProjectGitRepositoryHistory.CountAsync(x => (x.ProjectGitRepositoryId == f.Link.Id || x.ProjectGitRepositoryId == f.OtherLink.Id) && x.Action == ProjectGitRepositoryHistoryAction.AccessLost));
        Assert.Equal(GitSyncJobStatus.Cancelled, (await db.GitSyncJobs.SingleAsync(x => x.Id == f.Pending.Id)).Status);
        Assert.Equal(ProjectGitRepositoryAccessStatus.Active, (await db.ProjectGitRepositories.SingleAsync(x => x.Id == f.OAuthLink.Id)).AccessStatus);
        Assert.Equal(GitHubOAuthConnectionStatus.Active, (await db.GitHubOAuthConnections.SingleAsync(x => x.Id == f.OAuth.Id)).Status);
        Assert.True(f.Tokens.Invalidations >= 1); await f.AssertHistoryAsync();
    }

    [Step4bPostgresFact]
    public async Task RemovedAndAddedRequireLiveEvidenceAndRecoverWithoutRepeatingInitial()
    {
        await using var f = await Fixture.CreateAsync(); f.Http.Present = false;
        var removed = await f.StageAsync("removed", "installation_repositories", f.Body("removed", "installation_repositories", removed: [f.Repository.ProviderRepositoryId]));
        Assert.True(await f.ProcessAsync()); Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(removed)).Status);
        var duplicate = await f.StageAsync("removed", "installation_repositories", f.Body("removed", "installation_repositories", removed: [f.Repository.ProviderRepositoryId]));
        Assert.True(await f.ProcessAsync()); Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(duplicate)).Status);
        await using (var db = Open())
        {
            Assert.NotNull((await db.Set<GitHubAppRepositoryGrant>().SingleAsync(x => x.Id == f.Grant.Id)).RevokedAt);
            Assert.Equal(2, await db.ProjectGitRepositoryHistory.CountAsync(x => (x.ProjectGitRepositoryId == f.Link.Id || x.ProjectGitRepositoryId == f.OtherLink.Id) && x.Action == ProjectGitRepositoryHistoryAction.AccessLost));
            Assert.Equal(GitSyncJobStatus.Cancelled, (await db.GitSyncJobs.SingleAsync(x => x.Id == f.Pending.Id)).Status);
        }
        await f.AssertHistoryAsync();
        f.Http.Present = true;
        var added = await f.StageAsync("added", "installation_repositories", f.Body("added", "installation_repositories", added: [f.Repository.ProviderRepositoryId]));
        Assert.True(await f.ProcessAsync()); Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(added)).Status);
        var addedAgain = await f.StageAsync("added", "installation_repositories", f.Body("added", "installation_repositories", added: [f.Repository.ProviderRepositoryId]));
        Assert.True(await f.ProcessAsync()); Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(addedAgain)).Status);
        await using (var db = Open())
        {
            Assert.True((await db.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == f.Installation.Id)).VerificationUnknown);
            Assert.Equal(ProjectGitRepositoryAccessStatus.AccessLost, (await db.ProjectGitRepositories.SingleAsync(x => x.Id == f.Link.Id)).AccessStatus);
        }
        await f.ReconcileAsync(); await f.RecoverAsync(); await f.RecoverAsync();
        await using var verify = Open();
        Assert.Equal(2, await verify.ProjectGitRepositories.CountAsync(x => (x.Id == f.Link.Id || x.Id == f.OtherLink.Id) && x.AccessStatus == ProjectGitRepositoryAccessStatus.Active));
        Assert.Equal(1, await verify.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == f.Link.Id && x.SyncType == GitSyncType.Initial));
        Assert.Equal(1, await verify.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == f.Link.Id && x.SyncType == GitSyncType.Scheduled && x.Status == GitSyncJobStatus.Pending));
        Assert.Equal(1, await verify.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == f.OtherLink.Id && x.SyncType == GitSyncType.Scheduled && x.Status == GitSyncJobStatus.Pending));
        await f.AssertHistoryAsync();
    }

    [Step4bPostgresFact]
    public async Task SuspensionAndUnsuspensionAreRevalidatedBeforeRecovery()
    {
        await using var f = await Fixture.CreateAsync(); f.Http.Suspended = true;
        var suspend = await f.StageAsync("suspend"); Assert.True(await f.ProcessAsync());
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(suspend)).Status);
        await using (var db = Open())
        {
            Assert.Equal(GitHubAppInstallationStatus.Suspended, (await db.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == f.Installation.Id)).Status);
            Assert.Equal(ProjectGitRepositoryAccessStatus.AccessLost, (await db.ProjectGitRepositories.SingleAsync(x => x.Id == f.Link.Id)).AccessStatus);
        }
        f.Http.Suspended = false; var unsuspend = await f.StageAsync("unsuspend"); Assert.True(await f.ProcessAsync());
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(unsuspend)).Status);
        await using (var db = Open()) Assert.True((await db.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == f.Installation.Id)).VerificationUnknown);
        await f.ReconcileAsync(); await f.RecoverAsync();
        await using var verify = Open();
        Assert.Equal(GitHubAppInstallationStatus.Active, (await verify.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == f.Installation.Id)).Status);
        Assert.Equal(ProjectGitRepositoryAccessStatus.Active, (await verify.ProjectGitRepositories.SingleAsync(x => x.Id == f.Link.Id)).AccessStatus);
        await f.AssertHistoryAsync();
    }

    [Step4bPostgresFact]
    public async Task CreatedValidatesAppAndDoesNotCreateProjectOwnershipOrLinks()
    {
        await using var f = await Fixture.CreateAsync(persistInstallation: false);
        var id = await f.StageAsync("created"); Assert.Equal(GitHubAppWebhookDeliveryStatus.AwaitingScope, (await f.GetAsync(id)).Status);
        Assert.True(await f.ProcessAsync()); Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(id)).Status);
        await using var db = Open(); var installation = await db.Set<GitHubAppInstallation>().SingleAsync(x => x.ProviderInstallationId == f.Installation.ProviderInstallationId);
        Assert.True(installation.VerificationUnknown); Assert.Equal(9, installation.OwnerAccountId);
        Assert.False(await db.Set<GitHubAppInstallationAccount>().AnyAsync(x => x.InstallationId == installation.Id));
        Assert.False(await db.Set<GitHubAppRepositoryGrant>().AnyAsync(x => x.InstallationId == installation.Id));
    }

    [Step4bPostgresFact]
    public async Task TwoWorkersAndExpiredLeaseHaveOneOwnerAndCommittedDeliveryIsNotReplayed()
    {
        await using var f = await Fixture.CreateAsync(); f.Http.Missing = true; var id = await f.StageAsync("deleted");
        async Task<GitHubAppWebhookDelivery?> Claim() { await using var db = Open(); return await f.Consumer(db).ClaimNextAsync(default); }
        var claims = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Claim()));
        var first = Assert.Single(claims, x => x is not null)!; Assert.Equal(1, first.AttemptCount);
        await using (var db = Open()) await db.GitHubAppWebhookDeliveries.Where(x => x.Id == first.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        var second = await Claim(); Assert.NotNull(second); Assert.Equal(first.Id, second.Id); Assert.NotEqual(first.LeaseId, second.LeaseId); Assert.Equal(2, second.AttemptCount);
        await using (var db = Open()) await Assert.ThrowsAnyAsync<Exception>(() => f.Consumer(db).ProcessClaimAsync(first, default));
        await using (var db = Open()) await f.Consumer(db).ProcessClaimAsync(second, default);
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(id)).Status); Assert.Null(await Claim());
        await using var verify = Open();
        Assert.Equal(2, await verify.ProjectGitRepositoryHistory.CountAsync(x => (x.ProjectGitRepositoryId == f.Link.Id || x.ProjectGitRepositoryId == f.OtherLink.Id) && x.Action == ProjectGitRepositoryHistoryAction.AccessLost));
        await f.AssertHistoryAsync();
    }

    [Step4bPostgresFact]
    public async Task RateLimitRetriesDurablyAndDoesNotBlockAnotherDelivery()
    {
        await using var f = await Fixture.CreateAsync(); f.Http.Failure = HttpStatusCode.TooManyRequests;
        var first = await f.StageAsync("unsuspend"); Assert.True(await f.ProcessAsync());
        var waiting = await f.GetAsync(first); Assert.Equal(GitHubAppWebhookDeliveryStatus.RetryScheduled, waiting.Status);
        Assert.True(waiting.NextAttemptAt >= DateTimeOffset.UtcNow.AddMinutes(59)); Assert.Equal("GITHUB_APP_WEBHOOK_VERIFICATION_PENDING", waiting.LastErrorCode);
        await using (var db = Open())
        {
            var installation = await db.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == f.Installation.Id);
            Assert.True(installation.VerificationUnknown); Assert.True(installation.NextReconciliationAt >= DateTimeOffset.UtcNow.AddMinutes(59));
        }
        f.Http.Failure = null; var sameInstallation = await f.StageAsync("unsuspend");
        Assert.False(await f.ProcessAsync()); // Another event must not evade the installation's rate-limit deadline.
        await using var other = await Fixture.CreateAsync();
        var second = await other.StageAsync("unsuspend"); Assert.True(await other.ProcessAsync());
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await other.GetAsync(second)).Status);
        Assert.Equal(GitHubAppWebhookDeliveryStatus.RetryScheduled, (await f.GetAsync(first)).Status);
        await f.MakeDueAsync(first);
        await using (var db = Open()) await db.Set<GitHubAppInstallation>().Where(x => x.Id == f.Installation.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextReconciliationAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        Assert.True(await f.ProcessAsync()); Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(first)).Status);
        Assert.True(await f.ProcessAsync()); Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(sameInstallation)).Status);
        await f.AssertHistoryAsync();
    }

    [Step4bPostgresFact]
    public async Task WrongAppAndUndecryptablePayloadCannotChangeState()
    {
        await using var f = await Fixture.CreateAsync();
        var mismatch = await f.StageAsync("deleted", bytes: f.Body("deleted", appId: 43)); Assert.True(await f.ProcessAsync());
        Assert.Equal("GITHUB_APP_WEBHOOK_APP_MISMATCH", (await f.GetAsync(mismatch)).LastErrorCode);
        var unreadable = await f.StageAsync("deleted");
        Assert.True(await f.ProcessAsync(new GitHubTokenProtector(new EphemeralDataProtectionProvider())));
        var delivery = await f.GetAsync(unreadable); Assert.Equal(GitHubAppWebhookDeliveryStatus.RetryScheduled, delivery.Status);
        Assert.Equal("GITHUB_APP_WEBHOOK_DECRYPT_FAILED", delivery.LastErrorCode); Assert.Null(delivery.ProcessedAt);
        await using var db = Open(); Assert.Equal(GitHubAppInstallationStatus.Active, (await db.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == f.Installation.Id)).Status);
        Assert.Equal(ProjectGitRepositoryAccessStatus.Active, (await db.ProjectGitRepositories.SingleAsync(x => x.Id == f.Link.Id)).AccessStatus);
        await f.AssertHistoryAsync();
    }

    [Step4bPostgresFact]
    public async Task OldRemovedEventCannotOverrideNewerProviderVerification()
    {
        await using var f = await Fixture.CreateAsync(); f.Http.Present = true; var gate = new Gate(); f.Http.AppGate = gate;
        var id = await f.StageAsync("removed", "installation_repositories", f.Body("removed", "installation_repositories", removed: [f.Repository.ProviderRepositoryId]));
        var processing = f.ProcessAsync();
        try { await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20)); await f.ReconcileAsync(); }
        finally { gate.Release.TrySetResult(); }
        Assert.True(await processing); var row = await f.GetAsync(id);
        Assert.Equal(GitHubAppWebhookDeliveryStatus.RetryScheduled, row.Status); Assert.Equal("GITHUB_APP_WEBHOOK_CONCURRENT_CHANGE", row.LastErrorCode);
        await using (var db = Open()) Assert.Null((await db.Set<GitHubAppRepositoryGrant>().SingleAsync(x => x.Id == f.Grant.Id)).RevokedAt);
        await f.MakeDueAsync(id); Assert.True(await f.ProcessAsync());
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(id)).Status);
        await using var verify = Open(); Assert.Null((await verify.Set<GitHubAppRepositoryGrant>().SingleAsync(x => x.Id == f.Grant.Id)).RevokedAt);
        Assert.Equal(ProjectGitRepositoryAccessStatus.Active, (await verify.ProjectGitRepositories.SingleAsync(x => x.Id == f.Link.Id)).AccessStatus);
        await f.AssertHistoryAsync();
    }

    [Step4bPostgresFact]
    public async Task ProcessCrashAfterFenceIsRecoveredByExpiredLeaseWithoutDuplicateEffects()
    {
        await using var f = await Fixture.CreateAsync(); f.Http.Missing = true; var gate = new Gate(); f.Http.AppGate = gate;
        var id = await f.StageAsync("deleted"); using var cancellation = new CancellationTokenSource();
        async Task<bool> Interrupted()
        { await using var db = Open(); return await f.Consumer(db).ProcessNextAsync(cancellation.Token); }
        var processing = Interrupted();
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        var abandoned = await f.GetAsync(id); Assert.Equal(GitHubAppWebhookDeliveryStatus.Processing, abandoned.Status);
        await using (var db = Open())
        {
            Assert.True((await db.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == f.Installation.Id)).VerificationUnknown);
            await db.GitHubAppWebhookDeliveries.Where(x => x.DeliveryId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }
        Assert.True(await f.ProcessAsync()); Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(id)).Status);
        Assert.False(await f.ProcessAsync());
        await using var verify = Open();
        Assert.Equal(2, await verify.ProjectGitRepositoryHistory.CountAsync(x => (x.ProjectGitRepositoryId == f.Link.Id || x.ProjectGitRepositoryId == f.OtherLink.Id) && x.Action == ProjectGitRepositoryHistoryAction.AccessLost));
        await f.AssertHistoryAsync();
    }

    [Step4bPostgresFact]
    public async Task StalePositiveGrantAndUnlinkedRepositoriesNeverGainAccess()
    {
        await using var f = await Fixture.CreateAsync(); f.Http.Present = false;
        var removed = await f.StageAsync("removed", "installation_repositories", f.Body("removed", "installation_repositories", removed: [f.Repository.ProviderRepositoryId]));
        Assert.True(await f.ProcessAsync()); Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(removed)).Status);
        var staleAdded = await f.StageAsync("added", "installation_repositories", f.Body("added", "installation_repositories", added: [f.Repository.ProviderRepositoryId]));
        Assert.True(await f.ProcessAsync()); Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(staleAdded)).Status);
        var unlinkedId = Random.Shared.NextInt64(10000000, long.MaxValue / 2);
        var unrelated = await f.StageAsync("removed", "installation_repositories", f.Body("removed", "installation_repositories", removed: [unlinkedId]));
        Assert.True(await f.ProcessAsync()); Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(unrelated)).Status);
        await using var verify = Open();
        Assert.NotNull((await verify.Set<GitHubAppRepositoryGrant>().SingleAsync(x => x.Id == f.Grant.Id)).RevokedAt);
        Assert.Equal(ProjectGitRepositoryAccessStatus.AccessLost, (await verify.ProjectGitRepositories.SingleAsync(x => x.Id == f.Link.Id)).AccessStatus);
        Assert.False(await verify.Set<GitHubAppRepositoryGrant>().AnyAsync(x => x.InstallationId == f.Installation.Id && x.ProviderRepositoryId == unlinkedId));
        await f.AssertHistoryAsync();
    }

    [Step4bPostgresFact]
    public async Task UnknownInstallationDeletionDoesNotCreateAssociations()
    {
        await using var f = await Fixture.CreateAsync(persistInstallation: false);
        var id = await f.StageAsync("deleted"); Assert.True(await f.ProcessAsync());
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(id)).Status);
        await using var verify = Open();
        Assert.False(await verify.Set<GitHubAppInstallation>().AnyAsync(x => x.ProviderInstallationId == f.Installation.ProviderInstallationId));
        Assert.False(await verify.ProjectGitRepositories.AnyAsync(x => x.Id == f.Link.Id || x.Id == f.OtherLink.Id));
    }

    [Step4bPostgresFact]
    public async Task PermanentAndRetryExhaustedFailuresNeverBecomeProcessed()
    {
        await using var f = await Fixture.CreateAsync(); f.Http.Failure = HttpStatusCode.ServiceUnavailable;
        var id = await f.StageAsync("unsuspend");
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Assert.True(await f.ProcessAsync()); var row = await f.GetAsync(id); Assert.Equal(attempt, row.AttemptCount);
            if (attempt < 3)
            {
                Assert.Equal(GitHubAppWebhookDeliveryStatus.RetryScheduled, row.Status);
                await f.MakeDueAsync(id);
                await using var db = Open();
                await db.Set<GitHubAppInstallation>().Where(x => x.Id == f.Installation.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextReconciliationAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
            }
            else { Assert.Equal(GitHubAppWebhookDeliveryStatus.Failed, row.Status); Assert.Null(row.ProcessedAt); }
        }
        Assert.False(await f.ProcessAsync()); await f.AssertHistoryAsync();
    }

    [Step4bPostgresFact]
    public async Task ExpiredClaimsCannotExceedAttemptLimit()
    {
        await using var f = await Fixture.CreateAsync(); var id = await f.StageAsync("unsuspend");
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await using var db = Open(); var claim = await f.Consumer(db).ClaimNextAsync(default);
            Assert.NotNull(claim); Assert.Equal(attempt, claim.AttemptCount);
            await db.GitHubAppWebhookDeliveries.Where(x => x.Id == claim.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }
        await using (var db = Open()) Assert.Null(await f.Consumer(db).ClaimNextAsync(default));
        var row = await f.GetAsync(id); Assert.Equal(GitHubAppWebhookDeliveryStatus.Failed, row.Status);
        Assert.Equal("GITHUB_APP_WEBHOOK_ATTEMPTS_EXHAUSTED", row.LastErrorCode); Assert.Null(row.ProcessedAt);
        await f.AssertHistoryAsync();
    }

    [Step4bPostgresFact]
    public async Task AmbiguousInstallation404BlocksAccessWithoutDeclaringUninstall()
    {
        await using var f = await Fixture.CreateAsync(); f.Http.Missing = true;
        var id = await f.StageAsync("unsuspend"); Assert.True(await f.ProcessAsync());
        var row = await f.GetAsync(id); Assert.Equal(GitHubAppWebhookDeliveryStatus.RetryScheduled, row.Status);
        await using var db = Open(); var installation = await db.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == f.Installation.Id);
        Assert.True(installation.VerificationUnknown); Assert.Equal(GitHubAppInstallationStatus.Active, installation.Status);
        Assert.Null((await db.Set<GitHubAppRepositoryGrant>().SingleAsync(x => x.Id == f.Grant.Id)).RevokedAt);
        Assert.Null(await new GitSyncJobService(db, f.Sync, Options.Create(new GitHubAppOptions { Enabled = true })).ClaimNextAsync(TimeSpan.FromMinutes(1), default));
        await f.AssertHistoryAsync();
    }

    [Step4bPostgresFact]
    public async Task WrongJwtAppFailsClosedAndUnsupportedActionDoesNotPretendToProcess()
    {
        await using var f = await Fixture.CreateAsync(); f.Http.AppId = 43;
        var mismatch = await f.StageAsync("deleted"); Assert.True(await f.ProcessAsync());
        var failed = await f.GetAsync(mismatch); Assert.Equal(GitHubAppWebhookDeliveryStatus.Failed, failed.Status);
        Assert.Equal("GITHUB_APP_WEBHOOK_APP_MISMATCH", failed.LastErrorCode); Assert.Null(failed.ProcessedAt);
        await using (var db = Open())
        {
            var installation = await db.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == f.Installation.Id);
            Assert.True(installation.VerificationUnknown); Assert.Equal(GitHubAppInstallationStatus.Active, installation.Status);
        }
        var unsupported = await f.StageAsync("future_action"); Assert.True(await f.ProcessAsync());
        var row = await f.GetAsync(unsupported); Assert.Equal(GitHubAppWebhookDeliveryStatus.Unsupported, row.Status); Assert.Null(row.ProcessedAt);
        await f.AssertHistoryAsync();
    }

    [Step4bPostgresFact]
    public async Task ExistingHostedWorkerProcessesLifecycleDelivery()
    {
        await using var f = await Fixture.CreateAsync(persistInstallation: false); var delivery = await f.StageAsync("created");
        var options = Options.Create(new GitSyncOptions { Enabled = true, WorkerConcurrency = 1, PollSeconds = 1, ReconciliationPollSeconds = 1, WebhookLeaseSeconds = 60 });
        var services = new ServiceCollection(); using var http = new HttpClient(f.Http, false);
        services.AddDbContext<UniNetDbContext>(o => o.UseNpgsql(Connection));
        services.AddSingleton<IOptions<GitSyncOptions>>(options); services.AddSingleton<IOptions<GitHubAppOptions>>(f.App);
        services.AddSingleton<IGitHubTokenProtector>(f.Protector); services.AddSingleton<IGitHubAppInstallationTokenProvider>(f.Tokens);
        services.AddSingleton<IGitHubProviderClient>(new RepositoryProvider(f)); services.AddScoped<GitHubCredentialService>();
        services.AddScoped(sp => new GitHubAppInstallationReconciliationService(sp.GetRequiredService<UniNetDbContext>(), http, f.App, options, f.Tokens));
        services.AddScoped<GitHubAppRepositoryActivationService>(); services.AddScoped<GitHubAppWebhookConsumer>();
        services.AddScoped<IGitSyncJobService, GitSyncJobService>(); services.AddScoped<IGitSyncJobProcessor, GitHubSyncJobProcessor>();
        await using var container = services.BuildServiceProvider();
        using var worker = new GitSyncWorker(container.GetRequiredService<IServiceScopeFactory>(), options, NullLogger<GitSyncWorker>.Instance, f.App);
        try
        {
            await worker.StartAsync(default); var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if ((await f.GetAsync(delivery)).Status == GitHubAppWebhookDeliveryStatus.Processed) break;
                await Task.Delay(100);
            }
        }
        finally { await worker.StopAsync(default); }
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(delivery)).Status);
        await using var db = Open(); Assert.True(await db.Set<GitHubAppInstallation>().AnyAsync(x => x.ProviderInstallationId == f.Installation.ProviderInstallationId));
    }

    [Step4bPostgresFact]
    public async Task ConsumerReloadsPersistedPayloadAndLeavesUnsupportedDeliveriesUnprocessed()
    {
        await using var f = await Fixture.CreateAsync(); f.Http.Missing = true;
        var activityBody = Encoding.UTF8.GetBytes("{\"zen\":\"fixture\"}");
        var activityId = await f.StageAsync("", "ping", activityBody);
        Assert.False(await f.ProcessAsync());
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Unsupported, (await f.GetAsync(activityId)).Status);
        var lifecycleId = await f.StageAsync("deleted");
        await using (var db = Open())
        {
            var consumer = f.Consumer(db); var claim = await consumer.ClaimNextAsync(default); Assert.NotNull(claim);
            claim.PayloadEncrypted = "untrusted caller data"; claim.Action = "unsuspend";
            await consumer.ProcessClaimAsync(claim, default);
        }
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(lifecycleId)).Status);
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Unsupported, (await f.GetAsync(activityId)).Status);
        await f.AssertHistoryAsync();
    }

    [Step4bPostgresFact]
    public async Task StaleDeletedEventCannotOverrideAnActiveInstallation()
    {
        await using var f = await Fixture.CreateAsync(); f.Http.Missing = false;
        var id = await f.StageAsync("deleted"); Assert.True(await f.ProcessAsync());
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(id)).Status);
        await using (var db = Open())
        {
            var installation = await db.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == f.Installation.Id);
            Assert.Equal(GitHubAppInstallationStatus.Active, installation.Status); Assert.True(installation.VerificationUnknown);
            Assert.Empty(await db.ProjectGitRepositoryHistory.Where(x => x.ProjectGitRepositoryId == f.Link.Id).ToListAsync());
        }
        await f.ReconcileAsync();
        await using var verify = Open();
        Assert.False((await verify.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == f.Installation.Id)).VerificationUnknown);
        Assert.Equal(ProjectGitRepositoryAccessStatus.Active, (await verify.ProjectGitRepositories.SingleAsync(x => x.Id == f.Link.Id)).AccessStatus);
        await f.AssertHistoryAsync();
    }

    [Step4bPostgresFact]
    public async Task AddedRepositoryWithoutProjectLinkDoesNotCreateMembershipOrLink()
    {
        await using var f = await Fixture.CreateAsync(persistInstallation: false);
        await f.StageAsync("created"); Assert.True(await f.ProcessAsync());
        var id = await f.StageAsync("added", "installation_repositories", f.Body("added", "installation_repositories", added: [f.Repository.ProviderRepositoryId]));
        Assert.True(await f.ProcessAsync()); Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(id)).Status);
        await using var verify = Open();
        var installation = await verify.Set<GitHubAppInstallation>().SingleAsync(x => x.ProviderInstallationId == f.Installation.ProviderInstallationId);
        var grant = Assert.Single(await verify.Set<GitHubAppRepositoryGrant>().Where(x => x.InstallationId == installation.Id).ToListAsync());
        Assert.False(await verify.Set<GitHubAppInstallationAccount>().AnyAsync(x => x.InstallationId == installation.Id));
        Assert.False(await verify.ProjectGitRepositories.AnyAsync(x => x.GitHubAppRepositoryGrantId == grant.Id));
    }

    [Step4bPostgresFact]
    public async Task NewPermissionsAcceptedSchedulesVerificationWithoutGrantingAccess()
    {
        await using var f = await Fixture.CreateAsync();
        var id = await f.StageAsync("new_permissions_accepted"); Assert.True(await f.ProcessAsync());
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(id)).Status);
        await using (var db = Open())
        {
            var installation = await db.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == f.Installation.Id);
            Assert.True(installation.VerificationUnknown); Assert.True(installation.NextReconciliationAt <= DateTimeOffset.UtcNow);
            Assert.Null(await new GitSyncJobService(db, f.Sync, Options.Create(new GitHubAppOptions { Enabled = true })).ClaimNextAsync(TimeSpan.FromMinutes(1), default));
        }
        await f.ReconcileAsync();
        await using var verify = Open(); Assert.False((await verify.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == f.Installation.Id)).VerificationUnknown);
        await f.AssertHistoryAsync();
    }

    [Step4bPostgresFact]
    public async Task AddedBeforeCreatedIsVerifiedAndNeverCreatesProjectLinks()
    {
        await using var f = await Fixture.CreateAsync(persistInstallation: false);
        var added = await f.StageAsync("added", "installation_repositories", f.Body("added", "installation_repositories", added: [f.Repository.ProviderRepositoryId]));
        Assert.True(await f.ProcessAsync()); Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(added)).Status);
        var created = await f.StageAsync("created"); Assert.True(await f.ProcessAsync());
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(created)).Status);
        await using var verify = Open();
        var installation = await verify.Set<GitHubAppInstallation>().SingleAsync(x => x.ProviderInstallationId == f.Installation.ProviderInstallationId);
        var grant = Assert.Single(await verify.Set<GitHubAppRepositoryGrant>().Where(x => x.InstallationId == installation.Id).ToListAsync());
        Assert.Equal(f.Repository.ProviderRepositoryId, grant.ProviderRepositoryId);
        Assert.False(await verify.Set<GitHubAppInstallationAccount>().AnyAsync(x => x.InstallationId == installation.Id));
        Assert.False(await verify.ProjectGitRepositories.AnyAsync(x => x.GitHubAppRepositoryGrantId == grant.Id));
    }

    [Step4bPostgresFact]
    public async Task WrongOwnerAndMissingAppIdDoNotApplyLifecycleChanges()
    {
        await using var f = await Fixture.CreateAsync();
        var mismatch = await f.StageAsync("deleted", bytes: f.Body("deleted", ownerId: 10));
        Assert.True(await f.ProcessAsync());
        Assert.Equal("GITHUB_APP_WEBHOOK_OWNER_MISMATCH", (await f.GetAsync(mismatch)).LastErrorCode);
        Assert.Equal(0, f.Http.Requests);
        var configured = f.App.Value;
        f.App = Options.Create(new GitHubAppOptions { Enabled = true, AppId = null, ClientId = configured.ClientId, PrivateKeyPem = configured.PrivateKeyPem, WebhookSecret = configured.WebhookSecret });
        var missing = await f.StageAsync("deleted"); Assert.True(await f.ProcessAsync());
        var delivery = await f.GetAsync(missing); Assert.Equal(GitHubAppWebhookDeliveryStatus.RetryScheduled, delivery.Status);
        Assert.Equal("GITHUB_APP_ID_NOT_CONFIGURED", delivery.LastErrorCode); Assert.Null(delivery.ProcessedAt);
        await using var verify = Open();
        Assert.Equal(GitHubAppInstallationStatus.Active, (await verify.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == f.Installation.Id)).Status);
        Assert.Equal(ProjectGitRepositoryAccessStatus.Active, (await verify.ProjectGitRepositories.SingleAsync(x => x.Id == f.Link.Id)).AccessStatus);
        await f.AssertHistoryAsync();
    }

    [Step4bPostgresFact]
    public async Task CorruptedStoredPayloadFailsWithoutCallingGitHubOrChangingLinks()
    {
        await using var f = await Fixture.CreateAsync(); var id = await f.StageAsync("deleted");
        await using (var db = Open()) await db.GitHubAppWebhookDeliveries.Where(x => x.DeliveryId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PayloadHash, new string('0', 64)));
        Assert.True(await f.ProcessAsync()); var row = await f.GetAsync(id);
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Failed, row.Status);
        Assert.Equal("GITHUB_APP_WEBHOOK_HASH_MISMATCH", row.LastErrorCode); Assert.Null(row.ProcessedAt); Assert.Equal(0, f.Http.Requests);
        await using var verify = Open();
        Assert.Equal(ProjectGitRepositoryAccessStatus.Active, (await verify.ProjectGitRepositories.SingleAsync(x => x.Id == f.Link.Id)).AccessStatus);
        await f.AssertHistoryAsync();
    }

    [Step4bPostgresFact]
    public async Task HostedWorkerAutomaticallyReconcilesRemovedThenAddedGrant()
    {
        await using var f = await Fixture.CreateAsync(); f.Http.Present = false;
        await using (var db = Open()) await db.Set<GitHubAppInstallation>().Where(x => x.Id == f.Installation.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextReconciliationAt, DateTimeOffset.UtcNow.AddHours(1)));
        var removed = await f.StageAsync("removed", "installation_repositories", f.Body("removed", "installation_repositories", removed: [f.Repository.ProviderRepositoryId]));
        var options = Options.Create(new GitSyncOptions { Enabled = true, WorkerConcurrency = 1, PollSeconds = 1,
            ReconciliationPollSeconds = 1, WebhookLeaseSeconds = 60, RetryBaseDelaySeconds = 1 });
        var services = new ServiceCollection(); using var http = new HttpClient(f.Http, false);
        services.AddDbContext<UniNetDbContext>(o => o.UseNpgsql(Connection));
        services.AddSingleton<IOptions<GitSyncOptions>>(options); services.AddSingleton<IOptions<GitHubAppOptions>>(f.App);
        services.AddSingleton<IGitHubTokenProtector>(f.Protector); services.AddSingleton<IGitHubAppInstallationTokenProvider>(f.Tokens);
        services.AddSingleton<IGitHubProviderClient>(new RepositoryProvider(f)); services.AddScoped<GitHubCredentialService>();
        services.AddScoped(sp => new GitHubAppInstallationReconciliationService(sp.GetRequiredService<UniNetDbContext>(), http, f.App, options, f.Tokens));
        services.AddScoped<GitHubAppRepositoryActivationService>(); services.AddScoped<GitHubAppWebhookConsumer>();
        services.AddScoped<IGitSyncJobService, BlockedSyncClaims>(); services.AddScoped<IGitSyncJobProcessor, GitHubSyncJobProcessor>();
        await using var container = services.BuildServiceProvider();
        using var worker = new GitSyncWorker(container.GetRequiredService<IServiceScopeFactory>(), options, NullLogger<GitSyncWorker>.Instance, f.App);
        try
        {
            await worker.StartAsync(default);
            var until = DateTimeOffset.UtcNow.AddSeconds(20);
            while (DateTimeOffset.UtcNow < until && (await f.GetAsync(removed)).Status != GitHubAppWebhookDeliveryStatus.Processed) await Task.Delay(100);
            Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(removed)).Status);
            await using (var check = Open()) Assert.Equal(GitSyncJobStatus.Cancelled, (await check.GitSyncJobs.SingleAsync(x => x.Id == f.Pending.Id)).Status);
            f.Http.Present = true;
            var added = await f.StageAsync("added", "installation_repositories", f.Body("added", "installation_repositories", added: [f.Repository.ProviderRepositoryId]));
            until = DateTimeOffset.UtcNow.AddSeconds(20);
            while (DateTimeOffset.UtcNow < until)
            {
                await using var check = Open();
                var recovered = await check.ProjectGitRepositories.CountAsync(x => (x.Id == f.Link.Id || x.Id == f.OtherLink.Id) && x.AccessStatus == ProjectGitRepositoryAccessStatus.Active);
                var scheduled = await check.GitSyncJobs.CountAsync(x => (x.ProjectGitRepositoryId == f.Link.Id || x.ProjectGitRepositoryId == f.OtherLink.Id) && x.SyncType == GitSyncType.Scheduled && x.Status == GitSyncJobStatus.Pending);
                if ((await f.GetAsync(added)).Status == GitHubAppWebhookDeliveryStatus.Processed && recovered == 2 && scheduled == 2) break;
                await Task.Delay(100);
            }
            Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await f.GetAsync(added)).Status);
        }
        finally { await worker.StopAsync(default); }
        await using var verify = Open();
        Assert.Equal(2, await verify.ProjectGitRepositories.CountAsync(x => (x.Id == f.Link.Id || x.Id == f.OtherLink.Id) && x.AccessStatus == ProjectGitRepositoryAccessStatus.Active));
        Assert.Equal(2, await verify.GitSyncJobs.CountAsync(x => (x.ProjectGitRepositoryId == f.Link.Id || x.ProjectGitRepositoryId == f.OtherLink.Id) && x.SyncType == GitSyncType.Scheduled && x.Status == GitSyncJobStatus.Pending));
        Assert.Equal(1, await verify.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == f.Link.Id && x.SyncType == GitSyncType.Initial));
        await f.AssertHistoryAsync();
    }
}
