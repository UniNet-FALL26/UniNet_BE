using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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

public sealed class Step3bPostgresFactAttribute : FactAttribute
{
    public Step3bPostgresFactAttribute() { if (Environment.GetEnvironmentVariable("UNINET_STEP3B_DISPOSABLE") != "1") Skip = "Requires dedicated disposable Step 3B PostgreSQL database."; }
}

public sealed class GitHubAppWorkerPostgresTests
{
    private static string DatabaseName => GitHubAppDisposableDatabase.Name(Environment.GetEnvironmentVariable("UNINET_STEP3C_DISPOSABLE") == "1" ? "uninet_step3c_lifecycle" : "uninet_step3b_worker");
    private static IOptions<GitHubAppOptions> App => Options.Create(new GitHubAppOptions { Enabled = true });
    private static string Conn => $"Host=127.0.0.1;Port=55433;Database={DatabaseName};Username=postgres;Timeout=5";
    private static UniNetDbContext Open() => new(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(Conn).Options);
    private sealed class Protector : IGitHubTokenProtector { public string Protect(string token) => throw new InvalidOperationException("OAuth must not be used"); public string Unprotect(string token) => throw new InvalidOperationException("OAuth must not be used"); }
    private sealed class Tokens(long installation, long repository) : IGitHubAppInstallationTokenProvider
    {
        public int Calls;
        public int Invalidations;
        public Task<string> GetAsync(long installationId, long repositoryId, CancellationToken ct)
        {
            if (installationId != installation || repositoryId != repository)
                throw new GitHubIntegrationException("GITHUB_APP_ACCESS_DENIED", "Other fixture is unavailable.", 403);
            Calls++; return Task.FromResult("installation-fixture");
        }
        public void InvalidateInstallation(long id) { if (id == installation) Invalidations++; }
    }
    private sealed class Provider : IGitHubProviderClient
    {
        public GitHubIntegrationException? RepositoryFailure;
        public bool CanRead = true;
        public Task<GitHubRemoteRepository> GetRepositoryAsync(string token, long id, CancellationToken ct)
        { Assert.Equal("installation-fixture", token); if (RepositoryFailure is not null) throw RepositoryFailure; return Task.FromResult(new GitHubRemoteRepository(id, 42, "example", "repo", "example/repo", "https://github.com/example/repo", null, "main", true, false, CanRead)); }
        public Task<GitHubSyncPage> GetSyncPageAsync(string token, string repo, string category, string? scope, int page, int size, CancellationToken ct)
        {
            Assert.Equal("installation-fixture", token);
            var text = category switch {
                "branches" => """[{"name":"main","commit":{"sha":"abc123"}}]""",
                "commits" => """[{"sha":"abc123","commit":{"message":"change","committer":{"date":"2026-11-04T10:00:00Z"}},"author":{"id":42,"login":"user"}}]""",
                "pull-requests" => """[{"id":7001,"number":7,"state":"open","updated_at":"2026-11-04T10:00:00Z","user":{"id":42,"login":"user"}}]""",
                "issues" => """[{"id":8001,"number":8,"state":"open","created_at":"2026-11-04T10:00:00Z","user":{"id":42,"login":"user"}}]""",
                _ => "[]" };
            using var doc = JsonDocument.Parse(text);
            return Task.FromResult(new GitHubSyncPage(doc.RootElement.EnumerateArray().Select(x => x.Clone()).ToArray(), false, 5000, null));
        }
        public Task<GitHubOAuthTokens> ExchangeCodeAsync(string code, CancellationToken ct) => throw new NotImplementedException();
        public Task<GitHubOAuthTokens> RefreshAsync(string token, CancellationToken ct) => throw new NotImplementedException();
        public Task<GitHubUser> GetAuthenticatedUserAsync(string token, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<GitHubRemoteRepository>> GetRepositoriesAsync(string token, int page, int size, CancellationToken ct) => throw new NotImplementedException();
    }

    [Step3bPostgresFact]
    public async Task AppInitialJobClaimsProcessesAndPersistsActivitiesWithoutOAuth()
    {
        await using (var check = new NpgsqlConnection(Conn)) { await check.OpenAsync(); Assert.Equal(DatabaseName, check.Database); Assert.Equal("127.0.0.1", check.Host); }
        await using var db = Open(); await db.Database.EnsureCreatedAsync();
        var owner = new Account { Email = $"step3b-{Guid.NewGuid():N}@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
        var project = new Project { Creator = owner.Profile!, Title = "Worker", ProjectField = "Engineering", Description = "Disposable", MemberTarget = 5, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Visibility = ProjectVisibility.Public, Status = ProjectStatus.Active, RecruitmentStatus = RecruitmentStatus.Open };
        var installationId = Random.Shared.NextInt64(1000000, 2000000000); var repositoryId = Random.Shared.NextInt64(1000000, 2000000000);
        var installation = new GitHubAppInstallation { ProviderInstallationId = installationId, OwnerAccountId = 42, OwnerLogin = "example", OwnerType = "User" };
        var repository = new GitHubRepository { ProviderRepositoryId = repositoryId, ProviderOwnerId = 42, OwnerLogin = "example", Name = "repo", FullName = "example/repo", HtmlUrl = "https://github.com/example/repo", IsPrivate = true };
        var grant = new GitHubAppRepositoryGrant { InstallationId = installation.Id, ProviderRepositoryId = repositoryId, VerifiedAt = DateTimeOffset.UtcNow };
        var link = new ProjectGitRepository { ProjectId = project.Id, GitHubRepositoryId = repository.Id, LinkedByProfileId = owner.Profile!.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncPaused = false, AccessStatus = ProjectGitRepositoryAccessStatus.Active };
        var job = new GitSyncJob { ProjectGitRepositoryId = link.Id, GitHubRepositoryId = repository.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, OAuthConnectionId = null, SyncType = GitSyncType.Initial };
        var other = new Account { Email = $"other-{Guid.NewGuid():N}@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Other", Nickname = "Other" } };
        var otherProject = new Project { Creator = other.Profile!, Title = "Other", ProjectField = "Engineering", Description = "Disposable", MemberTarget = 5, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Visibility = ProjectVisibility.Public, Status = ProjectStatus.Active, RecruitmentStatus = RecruitmentStatus.Open };
        var otherLink = new ProjectGitRepository { ProjectId = otherProject.Id, GitHubRepositoryId = repository.Id, LinkedByProfileId = other.Profile!.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncPaused = false, AccessStatus = ProjectGitRepositoryAccessStatus.Active };
        db.AddRange(owner, project, installation, repository, grant, link, job, other, otherProject, otherLink); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var options = Options.Create(new GitSyncOptions { Enabled = true });
        var jobs = new GitSyncJobService(db, options, App);
        async Task<GitSyncJob?> Claim()
        {
            await using var workerDb = Open();
            return await new GitSyncJobService(workerDb, options, App).ClaimNextAsync(TimeSpan.FromMinutes(5), default);
        }
        var claims = await Task.WhenAll(Claim(), Claim());
        var claimed = Assert.Single(claims, x => x is not null)!;
        Assert.Equal(job.Id, claimed.Id); Assert.Equal(GitSyncJobStatus.Running, claimed.Status);
        var provider = new Provider(); var tokens = new Tokens(installationId, repositoryId);
        var processor = new GitHubSyncJobProcessor(db, provider, new GitHubCredentialService(db, provider, new Protector()), options, tokens, App);
        await processor.ProcessAsync(claimed, default);
        await jobs.CompleteAsync(claimed.Id, claimed.LeaseId!.Value, default);
        db.ChangeTracker.Clear();
        Assert.Equal(GitSyncJobStatus.Completed, (await db.GitSyncJobs.SingleAsync(x => x.Id == job.Id)).Status);
        Assert.Equal("SyncReady", Assert.Single((await jobs.GetProjectStatusAsync(owner.Id, project.Id, default)).Items).SyncReadiness);
        var activities = await (from activity in db.GitActivities join visibility in db.Set<ProjectGitActivityVisibility>() on activity.Id equals visibility.GitActivityId
            where visibility.ProjectGitRepositoryId == link.Id select activity).ToListAsync();
        Assert.Contains(activities, x => x.ActivityType == "commit.created");
        Assert.Contains(activities, x => x.ActivityType == "pull_request.opened");
        Assert.Contains(activities, x => x.ActivityType == "issue.opened");
        Assert.Equal(activities.Count, await db.Set<ProjectGitActivityVisibility>().CountAsync(x => x.ProjectGitRepositoryId == link.Id));
        Assert.Equal(0, (await new ProjectGitActivityService(db).GetAsync(other.Id, otherProject.Id, 1, 50, null, null, null, default)).TotalCount);
        Assert.True(tokens.Calls > 0);
        var beforeReplay = activities.Count;
        Assert.Equal("GITHUB_SYNC_LEASE_LOST", (await Assert.ThrowsAsync<GitHubIntegrationException>(() => processor.ProcessAsync(claimed, default))).Code);
        Assert.Equal(beforeReplay, await db.Set<ProjectGitActivityVisibility>().CountAsync(x => x.ProjectGitRepositoryId == link.Id));
        var failedJob = new GitSyncJob { ProjectGitRepositoryId = link.Id, GitHubRepositoryId = repository.Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncType = GitSyncType.Initial };
        db.Add(failedJob); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var failureClaim = await jobs.ClaimNextAsync(TimeSpan.FromMinutes(5), default);
        Assert.NotNull(failureClaim); Assert.Equal(failedJob.Id, failureClaim.Id);
        provider.RepositoryFailure = new GitHubIntegrationException("GITHUB_ACCESS_DENIED", "Denied", 403);
        var failure = await Assert.ThrowsAsync<GitHubIntegrationException>(() => processor.ProcessAsync(failureClaim, default));
        Assert.Equal("GITHUB_APP_ACCESS_UNVERIFIED", failure.Code); Assert.Equal(1, tokens.Invalidations);
        await jobs.FailAsync(failureClaim.Id, failureClaim.LeaseId!.Value, failure.Code, failure.Message, DateTimeOffset.UtcNow.AddDays(1), default, retryable: true);
        db.ChangeTracker.Clear();
        Assert.Equal(GitSyncJobStatus.RetryScheduled, (await db.GitSyncJobs.SingleAsync(x => x.Id == failedJob.Id)).Status);
        Assert.Equal("PendingWorker", Assert.Single((await jobs.GetProjectStatusAsync(owner.Id, project.Id, default)).Items).SyncReadiness);
        Assert.Equal(beforeReplay, await db.Set<ProjectGitActivityVisibility>().CountAsync(x => x.ProjectGitRepositoryId == link.Id));
    }

    [Step3bPostgresFact]
    public async Task WorkerDoesNotClaimSuspendedRevokedMismatchedOrUnlinkedAppJobs()
    {
        await using var db = Open(); await db.Database.EnsureCreatedAsync();
        var owner = new Account { Email = $"blocked-{Guid.NewGuid():N}@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
        var project = new Project { Creator = owner.Profile!, Title = "Blocked", ProjectField = "Engineering", Description = "Disposable", MemberTarget = 5, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Visibility = ProjectVisibility.Public, Status = ProjectStatus.Active, RecruitmentStatus = RecruitmentStatus.Open };
        db.AddRange(owner, project);
        var jobIds = new List<Guid>();
        foreach (var condition in new[] { "suspended", "revoked", "mismatch", "unlinked", "wrong-job-grant" })
        {
            var installationId = Random.Shared.NextInt64(1000000, 2000000000); var repositoryId = Random.Shared.NextInt64(1000000, 2000000000);
            var installation = new GitHubAppInstallation { ProviderInstallationId = installationId, OwnerAccountId = 42, OwnerLogin = "example", OwnerType = "User", Status = condition == "suspended" ? GitHubAppInstallationStatus.Suspended : GitHubAppInstallationStatus.Active };
            var repository = new GitHubRepository { ProviderRepositoryId = repositoryId, ProviderOwnerId = 42, OwnerLogin = "example", Name = "repo", FullName = "example/repo", HtmlUrl = "https://github.com/example/repo" };
            var grant = new GitHubAppRepositoryGrant { InstallationId = installation.Id, ProviderRepositoryId = condition == "mismatch" ? repositoryId + 1 : repositoryId, VerifiedAt = DateTimeOffset.UtcNow, RevokedAt = condition == "revoked" ? DateTimeOffset.UtcNow : null };
            var link = new ProjectGitRepository { ProjectId = project.Id, GitHubRepositoryId = repository.Id, LinkedByProfileId = owner.Profile!.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, AccessStatus = ProjectGitRepositoryAccessStatus.Active, SyncPaused = false, UnlinkedAt = condition == "unlinked" ? DateTimeOffset.UtcNow.AddSeconds(1) : null };
            var jobGrantId = grant.Id;
            if (condition == "wrong-job-grant")
            {
                var otherGrant = new GitHubAppRepositoryGrant { InstallationId = installation.Id, ProviderRepositoryId = repositoryId + 1, VerifiedAt = DateTimeOffset.UtcNow };
                db.Add(otherGrant); jobGrantId = otherGrant.Id;
            }
            var job = new GitSyncJob { ProjectGitRepositoryId = link.Id, GitHubRepositoryId = repository.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = jobGrantId, SyncType = GitSyncType.Initial };
            jobIds.Add(job.Id);
            db.AddRange(installation, repository, grant, link, job);
        }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var claim = await new GitSyncJobService(db, Options.Create(new GitSyncOptions { Enabled = true }), App).ClaimNextAsync(TimeSpan.FromMinutes(1), default);
        Assert.Null(claim);
        Assert.Equal(5, await db.GitSyncJobs.CountAsync(x => jobIds.Contains(x.Id) && x.Status == GitSyncJobStatus.Pending));
    }

    [Step3bPostgresFact]
    public async Task HostedWorkerProcessesAppManualJob()
    {
        await using var db = Open(); await db.Database.EnsureCreatedAsync();
        var owner = new Account { Email = $"hosted-{Guid.NewGuid():N}@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
        var project = new Project { Creator = owner.Profile!, Title = "Hosted", ProjectField = "Engineering", Description = "Disposable", MemberTarget = 5, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Visibility = ProjectVisibility.Public, Status = ProjectStatus.Active, RecruitmentStatus = RecruitmentStatus.Open };
        var installationId = Random.Shared.NextInt64(1000000, 2000000000); var repositoryId = Random.Shared.NextInt64(1000000, 2000000000);
        var installation = new GitHubAppInstallation { ProviderInstallationId = installationId, OwnerAccountId = 42, OwnerLogin = "example", OwnerType = "User" };
        var repository = new GitHubRepository { ProviderRepositoryId = repositoryId, ProviderOwnerId = 42, OwnerLogin = "example", Name = "repo", FullName = "example/repo", HtmlUrl = "https://github.com/example/repo", IsPrivate = true };
        var grant = new GitHubAppRepositoryGrant { InstallationId = installation.Id, ProviderRepositoryId = repositoryId, VerifiedAt = DateTimeOffset.UtcNow };
        var link = new ProjectGitRepository { ProjectId = project.Id, GitHubRepositoryId = repository.Id, LinkedByProfileId = owner.Profile!.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncPaused = false, AccessStatus = ProjectGitRepositoryAccessStatus.Active };
        var job = new GitSyncJob { ProjectGitRepositoryId = link.Id, GitHubRepositoryId = repository.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncType = GitSyncType.Manual };
        db.AddRange(owner, project, installation, repository, grant, link, job); await db.SaveChangesAsync();
        var options = Options.Create(new GitSyncOptions { Enabled = true, WorkerConcurrency = 1, PollSeconds = 1, JobLeaseDurationSeconds = 30 });
        var tokens = new Tokens(installationId, repositoryId);
        var services = new ServiceCollection();
        services.AddDbContext<UniNetDbContext>(o => o.UseNpgsql(Conn));
        services.AddSingleton<IOptions<GitSyncOptions>>(options);
        services.AddSingleton<IOptions<GitHubAppOptions>>(App);
        services.AddSingleton<IGitHubProviderClient>(new Provider()); services.AddSingleton<IGitHubTokenProtector>(new Protector());
        services.AddSingleton<IGitHubAppInstallationTokenProvider>(tokens);
        services.AddScoped<GitHubCredentialService>(); services.AddScoped<GitHubAppRepositoryActivationService>();
        services.AddScoped<IGitSyncJobService, GitSyncJobService>(); services.AddScoped<IGitSyncJobProcessor, GitHubSyncJobProcessor>();
        await using var container = services.BuildServiceProvider();
        using var worker = new GitSyncWorker(container.GetRequiredService<IServiceScopeFactory>(), options, NullLogger<GitSyncWorker>.Instance, App);
        try
        {
            await worker.StartAsync(default);
            var until = DateTimeOffset.UtcNow.AddSeconds(30);
            while (DateTimeOffset.UtcNow < until)
            {
                await using var check = Open();
                if (await check.GitSyncJobs.AnyAsync(x => x.Id == job.Id && x.Status == GitSyncJobStatus.Completed)) break;
                await Task.Delay(200);
            }
        }
        finally { await worker.StopAsync(default); }
        await using var verify = Open();
        Assert.Equal(GitSyncJobStatus.Completed, (await verify.GitSyncJobs.SingleAsync(x => x.Id == job.Id)).Status);
        Assert.True(await verify.Set<ProjectGitActivityVisibility>().CountAsync(x => x.ProjectGitRepositoryId == link.Id) >= 3);
        Assert.True(tokens.Calls > 0);
    }

    [Step3bPostgresFact]
    public async Task ConfirmedReadLossAndRecoveryPreserveHistoryOnPostgres()
    {
        await using var db = Open(); await db.Database.EnsureCreatedAsync();
        var owner = new Account { Email = $"recovery-{Guid.NewGuid():N}@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
        var project = new Project { Creator = owner.Profile!, Title = "Recovery", ProjectField = "Engineering", Description = "Disposable", MemberTarget = 5, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Visibility = ProjectVisibility.Public, Status = ProjectStatus.Active, RecruitmentStatus = RecruitmentStatus.Open };
        var installationId = Random.Shared.NextInt64(1000000, 2000000000); var repositoryId = Random.Shared.NextInt64(1000000, 2000000000);
        var installation = new GitHubAppInstallation { ProviderInstallationId = installationId, OwnerAccountId = 42, OwnerLogin = "example", OwnerType = "User" };
        var repository = new GitHubRepository { ProviderRepositoryId = repositoryId, ProviderOwnerId = 42, OwnerLogin = "example", Name = "repo", FullName = "example/repo", HtmlUrl = "https://github.com/example/repo", IsPrivate = true };
        var grant = new GitHubAppRepositoryGrant { InstallationId = installation.Id, ProviderRepositoryId = repositoryId, VerifiedAt = DateTimeOffset.UtcNow };
        var link = new ProjectGitRepository { ProjectId = project.Id, GitHubRepositoryId = repository.Id, LinkedByProfileId = owner.Profile!.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncPaused = false, AccessStatus = ProjectGitRepositoryAccessStatus.Active };
        var initial = new GitSyncJob { ProjectGitRepositoryId = link.Id, GitHubRepositoryId = repository.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncType = GitSyncType.Initial, Status = GitSyncJobStatus.Completed, CompletedAt = DateTimeOffset.UtcNow };
        var raw = new GitHubProviderObject { GitHubRepositoryId = repository.Id, Category = "commits", ExternalObjectId = "existing", PayloadJson = "{}", FirstObservedAt = DateTimeOffset.UtcNow, LastObservedAt = DateTimeOffset.UtcNow };
        var activity = new GitActivity { GitHubProviderObjectId = raw.Id, ActivityType = "commit.created", OccurredAt = DateTimeOffset.UtcNow };
        db.AddRange(owner, project, installation, repository, grant, link, initial, raw, activity); await db.SaveChangesAsync();
        var options = Options.Create(new GitSyncOptions { Enabled = true }); var tokens = new Tokens(installationId, repositoryId); var provider = new Provider { CanRead = false };
        var processor = new GitHubSyncJobProcessor(db, provider, new GitHubCredentialService(db, provider, new Protector()), options, tokens, App);
        var job = new GitSyncJob { ProjectGitRepositoryId = link.Id, GitHubRepositoryId = repository.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncType = GitSyncType.Manual };
        db.Add(job); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var jobService = new GitSyncJobService(db, options, App); var claimed = await jobService.ClaimNextAsync(TimeSpan.FromMinutes(1), default); Assert.NotNull(claimed);
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => processor.ProcessAsync(claimed!, default));
        Assert.Equal("GITHUB_REPOSITORY_ACCESS_LOST", error.Code); Assert.True(tokens.Invalidations > 0);
        await jobService.FailAsync(claimed!.Id, claimed.LeaseId!.Value, error.Code, error.Message, DateTimeOffset.UtcNow, default, retryable: false);
        db.ChangeTracker.Clear(); Assert.Equal(ProjectGitRepositoryAccessStatus.AccessLost, (await db.ProjectGitRepositories.SingleAsync(x => x.Id == link.Id)).AccessStatus);
        provider.CanRead = true; await new GitHubAppRepositoryActivationService(db, tokens, provider, options, App).RetryPendingAsync(default);
        db.ChangeTracker.Clear(); Assert.Equal(ProjectGitRepositoryAccessStatus.Active, (await db.ProjectGitRepositories.SingleAsync(x => x.Id == link.Id)).AccessStatus);
        Assert.Equal(1, await db.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == link.Id && x.SyncType == GitSyncType.Initial));
        Assert.Equal(1, await db.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == link.Id && x.SyncType == GitSyncType.Scheduled));
        Assert.True(await db.GitActivities.AnyAsync(x => x.Id == activity.Id));
        var cleanupJob = await db.GitSyncJobs.SingleAsync(x => x.ProjectGitRepositoryId == link.Id && x.SyncType == GitSyncType.Scheduled);
        cleanupJob.Status = GitSyncJobStatus.Cancelled; cleanupJob.CompletedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync();
    }

    [Step3bPostgresFact]
    public async Task ExpiredAppLeaseIsReclaimedAndTransientFailureCanRetry()
    {
        await using var db = Open(); await db.Database.EnsureCreatedAsync();
        var owner = new Account { Email = $"lease-{Guid.NewGuid():N}@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
        var project = new Project { Creator = owner.Profile!, Title = "Lease", ProjectField = "Engineering", Description = "Disposable", MemberTarget = 5, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Visibility = ProjectVisibility.Public, Status = ProjectStatus.Active, RecruitmentStatus = RecruitmentStatus.Open };
        var installationId = Random.Shared.NextInt64(1000000, 2000000000); var repositoryId = Random.Shared.NextInt64(1000000, 2000000000);
        var installation = new GitHubAppInstallation { ProviderInstallationId = installationId, OwnerAccountId = 42, OwnerLogin = "example", OwnerType = "User" };
        var repository = new GitHubRepository { ProviderRepositoryId = repositoryId, ProviderOwnerId = 42, OwnerLogin = "example", Name = "repo", FullName = "example/repo", HtmlUrl = "https://github.com/example/repo" };
        var grant = new GitHubAppRepositoryGrant { InstallationId = installation.Id, ProviderRepositoryId = repositoryId, VerifiedAt = DateTimeOffset.UtcNow };
        var link = new ProjectGitRepository { ProjectId = project.Id, GitHubRepositoryId = repository.Id, LinkedByProfileId = owner.Profile!.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncPaused = false, AccessStatus = ProjectGitRepositoryAccessStatus.Active };
        var oldLease = Guid.NewGuid();
        var job = new GitSyncJob { ProjectGitRepositoryId = link.Id, GitHubRepositoryId = repository.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncType = GitSyncType.Manual, Status = GitSyncJobStatus.Running, LeaseId = oldLease, LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1), StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2) };
        db.AddRange(owner, project, installation, repository, grant, link, job); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var jobs = new GitSyncJobService(db, Options.Create(new GitSyncOptions { Enabled = true }), App);
        var reclaimed = await jobs.ClaimNextAsync(TimeSpan.FromMinutes(2), default); Assert.NotNull(reclaimed); Assert.Equal(job.Id, reclaimed.Id); Assert.NotEqual(oldLease, reclaimed.LeaseId);
        Assert.False(await jobs.RenewLeaseAsync(job.Id, oldLease, TimeSpan.FromMinutes(2), default));
        Assert.True(await jobs.RenewLeaseAsync(job.Id, reclaimed.LeaseId!.Value, TimeSpan.FromMinutes(2), default));
        Assert.True(await jobs.FailAsync(job.Id, reclaimed.LeaseId.Value, "GITHUB_REQUEST_FAILED", "Temporary provider failure.", DateTimeOffset.UtcNow.AddSeconds(-1), default));
        db.ChangeTracker.Clear();
        var retried = await jobs.ClaimNextAsync(TimeSpan.FromMinutes(2), default); Assert.NotNull(retried); Assert.Equal(job.Id, retried.Id); Assert.Equal(1, retried.RetryCount);
        await jobs.FailAsync(job.Id, retried.LeaseId!.Value, "TEST_DONE", "Fixture complete.", DateTimeOffset.UtcNow, default, retryable: false);
    }
}
