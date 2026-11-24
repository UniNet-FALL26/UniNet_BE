using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UniNet.API.Services;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

// New write tests never inherit the legacy fixture's default E2E port.
internal static class ScopedManualPostgresEnvironment
{
    public static bool ExplicitDisposable =>
        Environment.GetEnvironmentVariable("UNINET_STEP4C_DISPOSABLE") == "1" &&
        int.TryParse(Environment.GetEnvironmentVariable("UNINET_STEP4C_TEST_PORT"), out var port) &&
        port is > 1024 and <= 65535 and not 55433 &&
        Environment.GetEnvironmentVariable("UNINET_STEP4C_TEST_DATABASE") is { } database &&
        database.StartsWith("uninet_step5b_scoped_manual_", StringComparison.Ordinal) &&
        database.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
public sealed class ScopedManualPostgresFactAttribute : FactAttribute
{
    public ScopedManualPostgresFactAttribute()
    { if (!ScopedManualPostgresEnvironment.ExplicitDisposable) Skip = "Requires explicit scoped disposable database and non-E2E port."; }
}
public sealed class ScopedManualPostgresTheoryAttribute : TheoryAttribute
{
    public ScopedManualPostgresTheoryAttribute()
    { if (!ScopedManualPostgresEnvironment.ExplicitDisposable) Skip = "Requires explicit scoped disposable database and non-E2E port."; }
}

// Reuses the existing fake provider, schema/authorization fixtures and guarded disposable connection.
public sealed partial class GitHubAppActivityWebhookPostgresTests
{
    private static GitSyncOptions ScopedSettings(Fixture f, Guid? id) => new()
    {
        ManualEnqueueEnabled = true, PollSeconds = 1, RetryBaseDelaySeconds = 1, MaxRetryAttempts = 2,
        ScopedManual = new() { Enabled = true, ProjectId = f.Projects[0].Id,
            AssociationId = f.Links[0].Id, JobId = id, ExecutionTimeoutSeconds = 30 }
    };

    private static async Task<GitSyncJob> ScopedJobAsync(Fixture f, int link = 0,
        GitSyncType type = GitSyncType.Manual, GitSyncJobStatus status = GitSyncJobStatus.Pending)
    {
        var job = new GitSyncJob { ProjectGitRepositoryId = f.Links[link].Id, GitHubRepositoryId = f.Repository.Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = f.Grant.Id,
            RequestedByProfileId = f.Owner.Profile!.Id, SyncType = type, Status = status };
        await using var db = Open(); db.Add(job); await db.SaveChangesAsync(); return job;
    }

    private sealed class UnexpectedBackgroundWork { public int Reconciliation; public int Webhook; }
    private static (ServiceProvider Root, UnexpectedBackgroundWork Background) ScopedServices(
        Fixture f, GitSyncOptions settings, params IInterceptor[] interceptors)
    {
        var background = new UnexpectedBackgroundWork();
        var services = new ServiceCollection();
        services.AddDbContext<UniNetDbContext>(o => o.UseNpgsql(Connection).AddInterceptors(interceptors));
        services.AddSingleton<IOptions<GitSyncOptions>>(Options.Create(settings));
        services.AddSingleton(f.App); services.AddSingleton<IGitHubTokenProtector>(f.Protection);
        services.AddSingleton<IGitHubAppInstallationTokenProvider>(f.Tokens);
        services.AddSingleton<IGitHubProviderClient>(f.Provider); services.AddScoped<GitHubCredentialService>();
        services.AddScoped<GitHubAppRepositoryActivationService>(); services.AddScoped<IGitSyncJobService, GitSyncJobService>(); services.AddScoped<IGitSyncJobProcessor, GitHubSyncJobProcessor>();
        services.AddScoped<GitHubAppInstallationReconciliationService>(_ => { background.Reconciliation++; throw new InvalidOperationException("Unexpected reconciliation"); });
        services.AddScoped<GitHubAppWebhookConsumer>(_ => { background.Webhook++; throw new InvalidOperationException("Unexpected webhook consumption"); });
        return (services.BuildServiceProvider(), background);
    }

    private static GitSyncWorker ScopedWorker(ServiceProvider root, Fixture f, GitSyncOptions settings) =>
        new(root.GetRequiredService<IServiceScopeFactory>(), Options.Create(settings), NullLogger<GitSyncWorker>.Instance, f.App);

    [ScopedManualPostgresTheory]
    [InlineData("scheduled")]
    [InlineData("project")]
    [InlineData("association")]
    [InlineData("other_job")]
    public async Task ScopedManualClaimRejectsWrongTargetBeforeAnyLeaseMutation(string mismatch)
    {
        await using var f = await Fixture.CreateAsync();
        var job = await ScopedJobAsync(f, type: mismatch == "scheduled" ? GitSyncType.Scheduled : GitSyncType.Manual);
        var settings = ScopedSettings(f, job.Id);
        if (mismatch == "project") settings.ScopedManual.ProjectId = f.Projects[1].Id;
        if (mismatch == "association") settings.ScopedManual.AssociationId = f.Links[1].Id;
        await using var db = Open(); var service = new GitSyncJobService(db, Options.Create(settings), f.App);
        if (mismatch == "other_job")
        {
            var other = await ScopedJobAsync(f, 1);
            var ex = await Assert.ThrowsAsync<GitHubIntegrationException>(() => service.ClaimScopedManualAsync(
                settings.ScopedManual.ProjectId, settings.ScopedManual.AssociationId, other.Id, TimeSpan.FromMinutes(1), default));
            Assert.Equal("GITHUB_SCOPED_MANUAL_TARGET_MISMATCH", ex.Code);
        }
        else Assert.Null(await service.ClaimScopedManualAsync(settings.ScopedManual.ProjectId,
            settings.ScopedManual.AssociationId, job.Id, TimeSpan.FromMinutes(1), default));
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => service.ClaimNextAsync(TimeSpan.FromMinutes(1), default));
        await using var check = Open(); var row = await check.GitSyncJobs.SingleAsync(x => x.Id == job.Id);
        Assert.Equal(GitSyncJobStatus.Pending, row.Status); Assert.Null(row.LeaseId); Assert.Null(row.StartedAt);
    }

    [ScopedManualPostgresFact]
    public async Task ScopedManualEnqueueRequiresManagerAndRetainsManualIdempotency()
    {
        await using var f = await Fixture.CreateAsync(); var settings = ScopedSettings(f, null);
        await using var db = Open(); var service = new GitSyncJobService(db, Options.Create(settings), f.App);
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => service.RequestManualSyncAsync(Guid.NewGuid(), f.Projects[0].Id, f.Links[0].Id, default));
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => service.RequestManualSyncAsync(f.Owner.Id, f.Projects[1].Id, f.Links[1].Id, default));
        Assert.Empty(await db.GitSyncJobs.Where(x => x.ProjectGitRepositoryId == f.Links[0].Id).ToArrayAsync());
        var first = await service.RequestManualSyncAsync(f.Owner.Id, f.Projects[0].Id, f.Links[0].Id, default);
        var second = await service.RequestManualSyncAsync(f.Owner.Id, f.Projects[0].Id, f.Links[0].Id, default);
        var row = await db.GitSyncJobs.SingleAsync(x => x.ProjectGitRepositoryId == f.Links[0].Id);
        Assert.Equal(GitSyncType.Manual, row.SyncType); Assert.True(second.WasAlreadyQueued);
        settings.ScopedManual.JobId = row.Id;
        var replay = await service.RequestManualSyncAsync(f.Owner.Id, f.Projects[0].Id, f.Links[0].Id, default);
        Assert.True(replay.WasAlreadyQueued); Assert.False(first.WasAlreadyQueued);
    }

    [ScopedManualPostgresFact]
    public async Task ScopedManualDoesNotReuseScheduledJobAtEnqueue()
    {
        await using var f = await Fixture.CreateAsync(); var scheduled = await ScopedJobAsync(f, type: GitSyncType.Scheduled);
        await using var db = Open(); var service = new GitSyncJobService(db, Options.Create(ScopedSettings(f, null)), f.App);
        var ex = await Assert.ThrowsAsync<GitHubIntegrationException>(() => service.RequestManualSyncAsync(f.Owner.Id, f.Projects[0].Id, f.Links[0].Id, default));
        Assert.Equal("GITHUB_SCOPED_MANUAL_JOB_CONFLICT", ex.Code);
        Assert.Equal(scheduled.Id, (await db.GitSyncJobs.SingleAsync(x => x.ProjectGitRepositoryId == f.Links[0].Id)).Id);
    }

    [ScopedManualPostgresFact]
    public async Task ScopedManualConcurrentClaimAndExpiredLeaseRecoveryAreFenced()
    {
        await using var f = await Fixture.CreateAsync(); var job = await ScopedJobAsync(f); var settings = ScopedSettings(f, job.Id);
        async Task<GitSyncJob?> Claim() { await using var db = Open(); return await new GitSyncJobService(db, Options.Create(settings), f.App)
            .ClaimScopedManualAsync(f.Projects[0].Id, f.Links[0].Id, job.Id, TimeSpan.FromMinutes(1), default); }
        var result = await Task.WhenAll(Claim(), Claim()); var first = Assert.Single(result, x => x is not null)!;
        await using var check = Open();
        await check.GitSyncJobs.Where(x => x.Id == job.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        var second = await Claim(); Assert.NotNull(second); Assert.NotEqual(first.LeaseId, second!.LeaseId);
        var service = new GitSyncJobService(check, Options.Create(settings), f.App);
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => service.CompleteAsync(job.Id, first.LeaseId!.Value, default));
        Assert.False(await service.RenewLeaseAsync(job.Id, first.LeaseId!.Value, TimeSpan.FromMinutes(1), default));
    }

    [ScopedManualPostgresFact]
    public async Task ScopedManualTwoHostedConsumersShareOneLeaseAndCommitOneLogicalObservation()
    {
        await using var f = await Fixture.CreateAsync(); var job = await ScopedJobAsync(f);
        var gate = new Gate(); f.Provider.BeforePageReturn = gate; f.Provider.GateOnlyFirstPage = true;
        var settings = ScopedSettings(f, job.Id); var (root, _) = ScopedServices(f, settings);
        await using (root)
        using (var first = ScopedWorker(root, f, settings))
        using (var second = ScopedWorker(root, f, settings))
        {
            await first.StartAsync(default); await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Guid lease;
            await using (var db = Open()) lease = (await db.GitSyncJobs.SingleAsync(x => x.Id == job.Id)).LeaseId!.Value;
            await second.StartAsync(default);
            await using (var db = Open()) Assert.Equal(lease, (await db.GitSyncJobs.SingleAsync(x => x.Id == job.Id)).LeaseId);
            gate.Release.TrySetResult(); await Task.WhenAll(first.ExecuteTask!,second.ExecuteTask!).WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal("Completed",first.ScopedStopReason); Assert.Equal("Completed",second.ScopedStopReason);
            await first.StopAsync(default); await second.StopAsync(default);
        }
        await using var verify = Open();
        var observations = await verify.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == job.Id).ToArrayAsync();
        Assert.NotEmpty(observations);
        Assert.All(observations.GroupBy(x => new {x.ProjectGitRepositoryId,x.GitSyncJobId,x.Category,x.ScopeKey,x.GitEvidenceVersionId}), x => Assert.Single(x));
        Assert.Equal(0, (await verify.GitSyncJobs.SingleAsync(x => x.Id == job.Id)).RetryCount);
    }

    [ScopedManualPostgresFact]
    public async Task ScopedManualFutureRetryDoesNotFetchOrFakeCompletionOnDeadline()
    {
        await using var f = await Fixture.CreateAsync(); var job = await ScopedJobAsync(f,status:GitSyncJobStatus.RetryScheduled);
        await using (var db = Open()) await db.GitSyncJobs.Where(x => x.Id == job.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAt,DateTimeOffset.UtcNow.AddHours(1)));
        var settings=ScopedSettings(f,job.Id);settings.ScopedManual.ExecutionTimeoutSeconds=1;
        var (root,_) = ScopedServices(f,settings);await using(root)
        using(var worker=ScopedWorker(root,f,settings))
        {
            await worker.StartAsync(default);await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("DeadlineExceeded",worker.ScopedStopReason);await worker.StopAsync(default);
            Assert.Equal(GitSyncJobStatus.RetryScheduled,worker.ScopedJobStatus);
        }
        await using var verify=Open();var row=await verify.GitSyncJobs.SingleAsync(x=>x.Id==job.Id);
        Assert.Equal(GitSyncJobStatus.RetryScheduled,row.Status);Assert.Null(row.LeaseId);Assert.Null(row.CompletedAt);
        Assert.Equal(0,f.Tokens.Calls);await AssertScopedPageEmptyAsync(f,job.Id);
    }

    [ScopedManualPostgresFact]
    public async Task ScopedManualWorkerCompletesOnlyPinnedJobWithNoAuxiliaryWorkAndPreservesHistory()
    {
        await using var f = await Fixture.CreateAsync(); var job = await ScopedJobAsync(f); var other = await ScopedJobAsync(f, 1);
        Guid snapshotId; Guid runId;
        await using (var db = Open())
        {
            snapshotId = await new ProjectGitEvidenceSnapshotService(db).CaptureAsync(f.Projects[0].Id, f.Owner.Id, DateTimeOffset.UtcNow, default);
            var run = new GitContributionRun { ProjectId = f.Projects[0].Id, GitEvidenceSnapshotId = snapshotId,
                RunNumber = 1, InputHash = new string('a',64), CalculatedById = f.Owner.Profile!.Id };
            runId = run.Id; db.Add(run); await db.SaveChangesAsync();
            await db.ProjectGitRepositories.Where(x => x.Id == f.Links[0].Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LinkedAt, DateTimeOffset.UtcNow.AddDays(-2)));
        }
        var settings = ScopedSettings(f, job.Id); var (root, background) = ScopedServices(f, settings);
        await using (root)
        using (var worker = ScopedWorker(root, f, settings))
        {
            await worker.StartAsync(default); await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(25));
            Assert.Equal("Completed", worker.ScopedStopReason); await worker.StopAsync(default);
        }
        await using var verify = Open();
        Assert.Equal(GitSyncJobStatus.Completed, (await verify.GitSyncJobs.SingleAsync(x => x.Id == job.Id)).Status);
        Assert.Equal(GitSyncJobStatus.Pending, (await verify.GitSyncJobs.SingleAsync(x => x.Id == other.Id)).Status);
        Assert.Equal(2, await verify.GitSyncJobs.CountAsync(x => x.ProjectGitRepositoryId == f.Links[0].Id || x.ProjectGitRepositoryId == f.Links[1].Id));
        Assert.NotEmpty(await verify.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == job.Id).ToArrayAsync());
        Assert.All(await verify.GitSyncCheckpoints.Where(x => x.SyncJobId == job.Id).ToArrayAsync(), x => Assert.Equal(GitSyncCheckpointStatus.Completed,x.Status));
        Assert.Empty(await verify.Set<ProjectGitEvidenceSnapshotEntry>().Where(x => x.SnapshotId == snapshotId).ToArrayAsync());
        Assert.True((await verify.Set<ProjectGitEvidenceSnapshot>().SingleAsync(x => x.Id == snapshotId)).Finalized);
        Assert.Equal(new string('a',64), (await verify.GitContributionRuns.SingleAsync(x => x.Id == runId)).InputHash);
        var pinnedReplay = await new GitSyncJobService(verify, Options.Create(settings), f.App)
            .RequestManualSyncAsync(f.Owner.Id, f.Projects[0].Id, f.Links[0].Id, default);
        Assert.True(pinnedReplay.WasAlreadyQueued); Assert.Equal(job.Id, pinnedReplay.Job.Id);
        Assert.Equal(0,background.Webhook); Assert.Equal(0,background.Reconciliation);
    }

    [ScopedManualPostgresTheory]
    [InlineData("revoke")]
    [InlineData("lease")]
    public async Task ScopedManualRejectsAuthorizationOrLeaseChangeAfterFetch(string change)
    {
        await using var f = await Fixture.CreateAsync(); var job = await ScopedJobAsync(f);
        var gate = new Gate(); f.Provider.BeforePageReturn = gate; f.Provider.GateOnlyFirstPage = true;
        var settings = ScopedSettings(f,job.Id); settings.ScopedManual.ExecutionTimeoutSeconds = 2;
        var (root, _) = ScopedServices(f,settings); await using (root)
        using (var worker = ScopedWorker(root,f,settings))
        {
            await worker.StartAsync(default); await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using (var db = Open())
                if(change=="revoke") await db.Set<GitHubAppRepositoryGrant>().Where(x=>x.Id==f.Grant.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));
                else await db.GitSyncJobs.Where(x=>x.Id==job.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.LeaseExpiresAt,DateTimeOffset.UtcNow.AddSeconds(-1)));
            gate.Release.TrySetResult(); await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10)); await worker.StopAsync(default);
        }
        await AssertScopedPageEmptyAsync(f,job.Id);
        await using var verify=Open(); var rejected = await verify.GitSyncJobs.SingleAsync(x=>x.Id==job.Id);
        Assert.NotEqual(GitSyncJobStatus.Completed, rejected.Status);
        if (change == "revoke")
        {
            Assert.Equal(GitSyncJobStatus.Failed, rejected.Status); Assert.NotNull(rejected.ErrorCode);
        }
        else
        {
            settings.ScopedManual.ExecutionTimeoutSeconds = 30;
            var (recovery, _) = ScopedServices(f, settings); await using (recovery)
            using (var worker = ScopedWorker(recovery, f, settings))
            {
                await worker.StartAsync(default); await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(15));
                Assert.Equal("Completed", worker.ScopedStopReason); await worker.StopAsync(default);
            }
            await verify.Entry(rejected).ReloadAsync(); Assert.Equal(GitSyncJobStatus.Completed, rejected.Status);
        }
    }

    [ScopedManualPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopedManualDeadlineOrShutdownDoesNotFakeCompletion(bool shutdown)
    {
        await using var f=await Fixture.CreateAsync(); var job=await ScopedJobAsync(f); var gate=new Gate();f.Provider.BeforePageReturn=gate;
        var settings=ScopedSettings(f,job.Id);settings.ScopedManual.ExecutionTimeoutSeconds=shutdown?30:1;
        var (root,_)=ScopedServices(f,settings); await using(root)
        using(var worker=ScopedWorker(root,f,settings))
        {
            await worker.StartAsync(default);await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if(shutdown)await worker.StopAsync(default);
            await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(shutdown?"Cancelled":"DeadlineExceeded",worker.ScopedStopReason);
            Assert.Equal(GitSyncJobStatus.Running,worker.ScopedJobStatus);
        }
        await AssertScopedPageEmptyAsync(f,job.Id);
        await using var verify=Open();var row=await verify.GitSyncJobs.SingleAsync(x=>x.Id==job.Id);
        Assert.Equal(GitSyncJobStatus.Running,row.Status);Assert.Null(row.CompletedAt);Assert.NotNull(row.LeaseExpiresAt);
    }

    [ScopedManualPostgresFact]
    public async Task ScopedManualRetryUsesWholePageAndBoundedExistingLifecycle()
    {
        await using var f=await Fixture.CreateAsync();var job=await ScopedJobAsync(f);f.Provider.TransientFailuresRemaining=1;
        var gate=new Gate();f.Provider.BeforePageReturn=gate;f.Provider.GateOnlyFirstPage=true;
        var settings=ScopedSettings(f,job.Id);var (root,_)=ScopedServices(f,settings);await using(root)
        using(var worker=ScopedWorker(root,f,settings))
        {
            await worker.StartAsync(default);await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await AssertScopedPageEmptyAsync(f,job.Id);
            gate.Release.TrySetResult();await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal("Completed",worker.ScopedStopReason);await worker.StopAsync(default);
        }
        await using var db=Open();var row=await db.GitSyncJobs.SingleAsync(x=>x.Id==job.Id);
        Assert.Equal(1,row.RetryCount);Assert.Equal(GitSyncJobStatus.Completed,row.Status);
    }

    [ScopedManualPostgresFact]
    public async Task ScopedManualPermanentProviderFailureStopsAtRetryLimit()
    {
        await using var f=await Fixture.CreateAsync();var job=await ScopedJobAsync(f);f.Provider.FailSync=true;
        var settings=ScopedSettings(f,job.Id);var (root,_)=ScopedServices(f,settings);await using(root)
        using(var worker=ScopedWorker(root,f,settings))
        {
            await worker.StartAsync(default);await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal("Failed",worker.ScopedStopReason);await worker.StopAsync(default);
        }
        await using var db=Open();var row=await db.GitSyncJobs.SingleAsync(x=>x.Id==job.Id);
        Assert.Equal(2,row.RetryCount);Assert.Null(row.NextAttemptAt);await AssertScopedPageEmptyAsync(f,job.Id);
    }

    private sealed class ScopedObservationFailureOnce : DbCommandInterceptor
    {
        public TaskCompletionSource Hit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Guid? FailedContext; public Guid? RecoveryContext;
        private int attempts;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"ProjectEvidenceObservations\"", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    FailedContext = data.Context!.ContextId.InstanceId; Hit.TrySetResult();
                    throw new InvalidOperationException("Scoped fixture observation persistence failure");
                }
                RecoveryContext = data.Context!.ContextId.InstanceId;
            }
            return ValueTask.FromResult(result);
        }
    }

    [ScopedManualPostgresFact]
    public async Task ScopedManualObservationFailureRollsBackThenRetriesInFreshContext()
    {
        await using var f = await Fixture.CreateAsync(); var job = await ScopedJobAsync(f);
        var fault = new ScopedObservationFailureOnce(); var settings = ScopedSettings(f, job.Id);
        settings.RetryBaseDelaySeconds = 300;
        var (root, _) = ScopedServices(f, settings, fault); await using(root)
        using(var worker = ScopedWorker(root,f,settings))
        {
            await worker.StartAsync(default); await fault.Hit.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while(true)
            {
                await using var check = Open();
                if(await check.GitSyncJobs.AnyAsync(x=>x.Id==job.Id&&x.Status==GitSyncJobStatus.RetryScheduled,deadline.Token)) break;
                await Task.Delay(25,deadline.Token);
            }
            await AssertScopedPageEmptyAsync(f,job.Id);
            await using(var db=Open()) await db.GitSyncJobs.Where(x=>x.Id==job.Id)
                .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.NextAttemptAt,DateTimeOffset.UtcNow.AddSeconds(-1)));
            await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal("Completed",worker.ScopedStopReason); await worker.StopAsync(default);
        }
        Assert.NotNull(fault.FailedContext); Assert.NotNull(fault.RecoveryContext); Assert.NotEqual(fault.FailedContext,fault.RecoveryContext);
        await using var verify=Open();Assert.NotEmpty(await verify.ProjectEvidenceObservations.Where(x=>x.GitSyncJobId==job.Id).ToArrayAsync());
        Assert.All(await verify.GitSyncCheckpoints.Where(x=>x.SyncJobId==job.Id).ToArrayAsync(),x=>Assert.Equal(GitSyncCheckpointStatus.Completed,x.Status));
    }

    private static async Task AssertScopedPageEmptyAsync(Fixture f,Guid jobId)
    {
        await using var db=Open();
        Assert.Empty(await db.ProjectEvidenceObservations.Where(x=>x.GitSyncJobId==jobId).ToArrayAsync());
        Assert.Empty(await db.GitSyncCheckpoints.Where(x=>x.SyncJobId==jobId).ToArrayAsync());
        Assert.Empty(await db.GitHubProviderObjects.Where(x=>x.GitHubRepositoryId==f.Repository.Id).ToArrayAsync());
        Assert.Empty(await db.GitEvidenceVersions.Where(x=>x.GitHubRepositoryId==f.Repository.Id).ToArrayAsync());
        Assert.Empty(await db.GitActivities.Where(x=>db.GitHubProviderObjects.Any(o=>o.Id==x.GitHubProviderObjectId&&o.GitHubRepositoryId==f.Repository.Id)).ToArrayAsync());
        Assert.Empty(await db.ProjectGitActivityGrants.Where(x=>x.ProjectGitRepositoryId==f.Links[0].Id).ToArrayAsync());
        Assert.Empty(await db.ProjectGitActivityVisibilities.Where(x=>x.ProjectGitRepositoryId==f.Links[0].Id).ToArrayAsync());
    }
    [ScopedManualPostgresFact]
    public async Task ScopedActivationUsesManualJobAndPreservesOtherProjectSharingRepository()
    {
        await using var f = await Fixture.CreateAsync();
        await using (var setup = Open())
        {
            await setup.ProjectGitRepositories.Where(x => x.Id == f.Links[0].Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.AccessStatus, ProjectGitRepositoryAccessStatus.PendingAuthorization).SetProperty(x => x.SyncPaused, true));
        }
        var settings = ScopedSettings(f, null);
        var (root, background) = ScopedServices(f, settings);
        await using var keepRoot = root;
        await using (var scope = root.CreateAsyncScope())
        {
            var jobs = scope.ServiceProvider.GetRequiredService<IGitSyncJobService>();
            var result = await jobs.RequestManualSyncAsync(f.Owner.Id, f.Projects[0].Id, f.Links[0].Id, default);
            var replay = await jobs.RequestManualSyncAsync(f.Owner.Id, f.Projects[0].Id, f.Links[0].Id, default);
            Assert.Equal(result.Job.Id, replay.Job.Id);
        }
        await using var db = Open();
        var job = Assert.Single(await db.GitSyncJobs.Where(x => x.ProjectGitRepositoryId == f.Links[0].Id).ToArrayAsync());
        Assert.Equal(GitSyncType.Manual, job.SyncType);
        Assert.Equal(ProjectGitRepositoryAccessStatus.Active, (await db.ProjectGitRepositories.AsNoTracking().SingleAsync(x=>x.Id==f.Links[0].Id)).AccessStatus);
        Assert.Empty(await db.GitSyncJobs.Where(x=>x.ProjectGitRepositoryId==f.Links[1].Id).ToArrayAsync());
        Assert.Equal(f.Links[1].Version,(await db.ProjectGitRepositories.AsNoTracking().SingleAsync(x=>x.Id==f.Links[1].Id)).Version);
        Assert.Equal(0,background.Reconciliation); Assert.Equal(0,background.Webhook);
        var repository = new GitHubRepository { ProviderRepositoryId = Random.Shared.NextInt64(10000000,long.MaxValue/2), ProviderOwnerId=9, OwnerLogin="fixture", Name="second", FullName="fixture/second", HtmlUrl="https://github.com/fixture/second" };
        var grant = new GitHubAppRepositoryGrant { InstallationId=f.Installation.Id, ProviderRepositoryId=repository.ProviderRepositoryId, VerifiedAt=DateTimeOffset.UtcNow };
        var second = new ProjectGitRepository { ProjectId=f.Projects[0].Id, GitHubRepositoryId=repository.Id, GitHubAppRepositoryGrantId=grant.Id, CredentialSource=GitRepositoryCredentialSource.GitHubApp, LinkedByProfileId=f.Owner.Profile!.Id, AccessStatus=ProjectGitRepositoryAccessStatus.PendingAuthorization, SyncPaused=true };
        db.AddRange(repository,grant,second); await db.SaveChangesAsync(); f.Links.Add(second);
        f.Repository=repository; settings.ScopedManual.AssociationId=second.Id;
        await using(var next=root.CreateAsyncScope())
            await next.ServiceProvider.GetRequiredService<IGitSyncJobService>().RequestManualSyncAsync(f.Owner.Id,f.Projects[0].Id,second.Id,default);
        var secondJob=Assert.Single(await db.GitSyncJobs.Where(x=>x.ProjectGitRepositoryId==second.Id).ToArrayAsync());
        Assert.Equal(GitSyncType.Manual,secondJob.SyncType); Assert.Equal(repository.Id,secondJob.GitHubRepositoryId);
    }    [ScopedManualPostgresTheory]
    [InlineData("grant")]
    [InlineData("installation")]
    [InlineData("unlink")]
    public async Task ScopedActivationRejectsAuthorizationChangeDuringProviderVerification(string change)
    {
        await using var f=await Fixture.CreateAsync();
        await using(var setup=Open()) await setup.ProjectGitRepositories.Where(x=>x.Id==f.Links[0].Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AccessStatus,ProjectGitRepositoryAccessStatus.PendingAuthorization).SetProperty(x=>x.SyncPaused,true));
        var settings=ScopedSettings(f,null); var gate=new Gate(); f.Provider.FirstRead=gate;
        await using var db=Open();
        var activation=new GitHubAppRepositoryActivationService(db,f.Tokens,f.Provider,Options.Create(settings),f.App);
        var attempt=activation.ActivateScopedManualAsync(f.Owner.Id,f.Projects[0].Id,f.Links[0].Id,default);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await using(var mutation=Open())
        {
            if(change=="grant") { await mutation.Set<GitHubAppRepositoryGrant>().Where(x=>x.Id==f.Grant.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow)); await mutation.Set<GitHubAppRepositoryGrant>().Where(x=>x.Id==f.Grant.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,(DateTimeOffset?)null)); }
            else if(change=="installation") { await mutation.Set<GitHubAppInstallation>().Where(x=>x.Id==f.Installation.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,GitHubAppInstallationStatus.Suspended)); await mutation.Set<GitHubAppInstallation>().Where(x=>x.Id==f.Installation.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,GitHubAppInstallationStatus.Active)); }
            else await mutation.ProjectGitRepositories.Where(x=>x.Id==f.Links[0].Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.UnlinkedAt,DateTimeOffset.UtcNow));
        }
        gate.Release.TrySetResult();
        var error=await Assert.ThrowsAsync<GitHubIntegrationException>(()=>attempt);
        Assert.Equal("GITHUB_SYNC_AUTHORIZATION_CHANGED",error.Code);
        await using var verify=Open();
        Assert.Empty(await verify.GitSyncJobs.Where(x=>x.ProjectGitRepositoryId==f.Links[0].Id).ToArrayAsync());
        var link=await verify.ProjectGitRepositories.AsNoTracking().SingleAsync(x=>x.Id==f.Links[0].Id);
        Assert.Equal(ProjectGitRepositoryAccessStatus.PendingAuthorization,link.AccessStatus); Assert.True(link.SyncPaused);
    }
    [ScopedManualPostgresFact]
    public async Task NormalActivationStillCreatesInitialWhileScopedModeIsOff()
    {
        await using var f=await Fixture.CreateAsync();
        await using(var setup=Open()) await setup.ProjectGitRepositories.Where(x=>x.Id==f.Links[0].Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AccessStatus,ProjectGitRepositoryAccessStatus.PendingAuthorization).SetProperty(x=>x.SyncPaused,true));
        await using var db=Open();
        Assert.True(await new GitHubAppRepositoryActivationService(db,f.Tokens,f.Provider,f.Sync,f.App).ActivateAsync(f.Links[0].Id,default));
        Assert.Equal(GitSyncType.Initial,Assert.Single(await db.GitSyncJobs.Where(x=>x.ProjectGitRepositoryId==f.Links[0].Id).ToArrayAsync()).SyncType);
    }}
