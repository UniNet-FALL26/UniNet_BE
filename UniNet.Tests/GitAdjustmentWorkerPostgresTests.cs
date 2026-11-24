using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using UniNet.API.Services;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using Xunit;
namespace UniNet.Tests;
public sealed partial class GitHubAppActivityWebhookPostgresTests
{
    [ScopedManualPostgresFact]
    public async Task AdjustmentCoordinatorExecutesOnlyDurablePlanJobsAndProducesFreshReceipt()
    {
        await using var f=await Fixture.CreateAsync();
        var options=new GitSyncOptions{AdjustmentExecutionEnabled=true,RetryBaseDelaySeconds=1,MaxRetryAttempts=2};
        CombinedAdjustmentResponse plan;
        await using(var db=Open())
        {
            await db.Projects.Where(x=>x.Id==f.Projects[0].Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,ProjectStatus.Completed).SetProperty(x=>x.CompletedAt,DateTimeOffset.UtcNow));
            Assert.Equal("OfficialV2",await db.Set<ProjectContributionWorkflow>().Where(x=>x.ProjectId==f.Projects[0].Id).Select(x=>x.Mode).SingleAsync());
            var capture=new ProjectGitEvidenceSnapshotService(db);var reader=new ProjectGitEvidenceSnapshotReader(db);
            var engine=new GitExperimentalContributionService(db,new ProjectGitContributionSnapshotAccess(capture,reader),TimeProvider.System);
            var official=new OfficialContributionService(db,engine,capture,reader,new(db,TimeProvider.System));
            plan=await new GitContributionAdjustmentService(db,official,Options.Create(options),f.App,new ConfigurationBuilder().Build())
                .StartAsync(f.Projects[0].Id,f.Owner.Id,new(Guid.NewGuid()),default);
        }
        var foreign=await ScopedJobAsync(f,1);
        var(root,unexpected)=ScopedServices(f,options);
        await using(root)
        using(var coordinator=new GitContributionAdjustmentWorker(root.GetRequiredService<IServiceScopeFactory>(),Options.Create(options),NullLoggerFactory.Instance))
        using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(25)))
        {
            await coordinator.StartAsync(deadline.Token);
            while(true)
            {
                await using var db=Open();
                var state=await db.GitSyncJobs.AsNoTracking().Where(x=>x.Id==plan.Repositories[0].JobId).Select(x=>x.Status).SingleAsync(deadline.Token);
                if(state is GitSyncJobStatus.Completed or GitSyncJobStatus.Failed){Assert.Equal(GitSyncJobStatus.Completed,state);break;}
                await Task.Delay(100,deadline.Token); // Bounded durable-status observation, not a timing race.
            }
            await coordinator.StopAsync(default);
        }
        await using var verify=Open();
        Assert.Equal(GitSyncJobStatus.Pending,(await verify.GitSyncJobs.SingleAsync(x=>x.Id==foreign.Id)).Status);
        Assert.NotEmpty(await verify.ProjectEvidenceObservations.Where(x=>x.GitSyncJobId==plan.Repositories[0].JobId).ToArrayAsync());
        Assert.Equal(0,unexpected.Reconciliation);Assert.Equal(0,unexpected.Webhook);
    }
    [ScopedManualPostgresFact]
    public async Task AdjustmentConsumerReusesScopedWorkerAndNeverClaimsOtherProjectOrScheduledJobs()
    {
        await using var f = await Fixture.CreateAsync();
        var pinned = await ScopedJobAsync(f);
        var foreign = await ScopedJobAsync(f, 1);
        var scheduled = await ScopedJobAsync(f, 2, GitSyncType.Scheduled);
        // Root configuration cannot claim anything. Only the per-plan scope supplies the real JobId.
        var settings = ScopedSettings(f, null);
        var (root, unexpected) = ScopedServices(f, settings);
        await using (root)
        {
            var reason = await GitContributionAdjustmentWorker.RunPinnedAsync(root.GetRequiredService<IServiceScopeFactory>(),
                GitContributionAdjustmentService.Scope(f.Projects[0].Id, f.Links[0].Id, pinned.Id, settings, 25), NullLoggerFactory.Instance, default);
            Assert.Equal("Completed", reason);
        }
        await using var db = Open();
        Assert.Equal(GitSyncJobStatus.Completed, (await db.GitSyncJobs.SingleAsync(x => x.Id == pinned.Id)).Status);
        Assert.Equal(GitSyncJobStatus.Pending, (await db.GitSyncJobs.SingleAsync(x => x.Id == foreign.Id)).Status);
        Assert.Equal(GitSyncJobStatus.Pending, (await db.GitSyncJobs.SingleAsync(x => x.Id == scheduled.Id)).Status);
        Assert.NotEmpty(await db.ProjectEvidenceObservations.Where(x => x.GitSyncJobId == pinned.Id).ToArrayAsync());
        Assert.Equal(0, unexpected.Webhook); Assert.Equal(0, unexpected.Reconciliation);
    }
}
