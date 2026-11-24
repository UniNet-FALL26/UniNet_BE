using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using Xunit;
namespace UniNet.Tests;
public sealed partial class GitExperimentalContributionPostgresHttpTests
{
    [OfficialPostgresFact]
    public async Task AtomicApprovalPublishesTwoVersionsAndPreservesPreviousResult()
    {
        var f=await OfficialFixture();
        var first=await OfficialCall(s=>s.CreateAsync(f.Project,f.Accounts[0],new(Guid.NewGuid(),f.Snapshot,f.Peer),default));
        var approval=new OfficialApprovalRequest(first.Revision,null,"Reviewed");
        var published=await OfficialCall(s=>s.ApproveAndPublishAsync(f.Project,first.Id,f.Accounts[0],approval,default));
        Assert.Equal("Published",published.State);
        Assert.Equal(published.ResultHash,(await OfficialCall(s=>s.ApproveAndPublishAsync(f.Project,first.Id,f.Accounts[0],approval,default))).ResultHash);
        Assert.Equal("FORBIDDEN",(await Assert.ThrowsAsync<ProjectException>(()=>OfficialCall(s=>s.ApproveAndPublishAsync(f.Project,first.Id,f.Accounts[1],approval,default)))).Code);
        var p=await OfficialCall(s=>s.OpenPeerAsync(f.Project,f.Accounts[0],new(f.Profiles.Take(3).ToArray()),default));
        Assert.Equal(0,p.SubmittedCount);Assert.Null(p.SnapshotId);
        await Assert.ThrowsAsync<ProjectException>(()=>OfficialCall(s=>s.ClosePeerAsync(f.Project,p.Id,f.Accounts[0],default)));
        for(var from=0;from<3;from++)for(var to=0;to<3;to++)if(from!=to)
            await OfficialCall(s=>s.SubmitPeerAsync(f.Project,p.Id,f.Profiles[to],f.Accounts[from],new(5,null,null),default));
        var peer2=await OfficialCall(s=>s.ClosePeerAsync(f.Project,p.Id,f.Accounts[0],default));
        var second=await OfficialCall(s=>s.CreateAsync(f.Project,f.Accounts[0],new(Guid.NewGuid(),f.Snapshot,peer2),default));
        Assert.Equal(first.Id,(await OfficialCall(s=>s.MyResultAsync(f.Project,f.Accounts[1],default))).RunId);        await using(var source=OfficialDb())
        await using(var failing=new UniNet.Infrastructure.Data.UniNetDbContext(new DbContextOptionsBuilder<UniNet.Infrastructure.Data.UniNetDbContext>().UseNpgsql(source.Database.GetConnectionString()).AddInterceptors(new RejectRelease()).Options))
            await Assert.ThrowsAsync<InvalidOperationException>(()=>OfficialService(failing).ApproveAndPublishAsync(f.Project,second.Id,f.Accounts[0],new(0,first.Id),default));
        Assert.Equal("Draft",(await OfficialCall(s=>s.GetAsync(f.Project,second.Id,f.Accounts[0],default))).State);
        Assert.Equal(first.Id,(await OfficialCall(s=>s.MyResultAsync(f.Project,f.Accounts[1],default))).RunId);
        Assert.Equal("OFFICIAL_PUBLICATION_CHANGED",(await Assert.ThrowsAsync<ProjectException>(()=>OfficialCall(s=>s.ApproveAndPublishAsync(f.Project,second.Id,f.Accounts[0],new(second.Revision,null),default)))).Code);
        Assert.Equal(first.Id,(await OfficialCall(s=>s.MyResultAsync(f.Project,f.Accounts[1],default))).RunId);
        await OfficialCall(s=>s.ApproveAndPublishAsync(f.Project,second.Id,f.Accounts[0],new(second.Revision,first.Id,"New evaluation"),default));
        Assert.Equal(second.Id,(await OfficialCall(s=>s.MyResultAsync(f.Project,f.Accounts[1],default))).RunId);
        var versions=await OfficialCall(s=>s.VersionsAsync(f.Project,f.Accounts[0],1,20,default));
        Assert.Equal(2,versions.Total);Assert.True(versions.Items[0].IsCurrent);Assert.Equal(second.Id,versions.Items[1].ReplacedByRunId);
        Assert.Equal(first.ResultHash,(await OfficialCall(s=>s.GetAsync(f.Project,first.Id,f.Accounts[0],default))).ResultHash);
        await using var db=OfficialDb();
        Assert.Equal("55000",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"OfficialContributionReleases\" WHERE \"RunId\"={first.Id}"))).SqlState);
        Assert.Equal("55000",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"OfficialContributionRuns\" SET \"ResultHash\"={new string('a',64)} WHERE \"Id\"={first.Id}"))).SqlState);
        Assert.Equal(2,await db.Set<ExperimentalPeerSnapshot>().CountAsync(x=>x.ProjectId==f.Project));
    }
    private sealed class RejectRelease : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData e,Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,CancellationToken ct=default)
        {
            if(e.Context!.ChangeTracker.Entries<OfficialContributionRelease>().Any(x=>x.State==EntityState.Added))throw new InvalidOperationException("controlled publication failure");
            return ValueTask.FromResult(result);
        }
    }
    [OfficialPostgresFact]
    public async Task AtomicApprovalRollbackConcurrentRetryAndStaleRevisionFailClosed()
    {
        var f=await OfficialFixture();var run=await OfficialCall(s=>s.CreateAsync(f.Project,f.Accounts[0],new(Guid.NewGuid(),f.Snapshot,f.Peer),default));
        Assert.Equal("OFFICIAL_REVISION_CONFLICT",(await Assert.ThrowsAsync<ProjectException>(()=>OfficialCall(s=>s.ApproveAndPublishAsync(f.Project,run.Id,f.Accounts[0],new(99,null),default)))).Code);
        await using(var source=OfficialDb())
        await using(var failing=new UniNet.Infrastructure.Data.UniNetDbContext(new DbContextOptionsBuilder<UniNet.Infrastructure.Data.UniNetDbContext>().UseNpgsql(source.Database.GetConnectionString()).AddInterceptors(new RejectRelease()).Options))
            await Assert.ThrowsAsync<InvalidOperationException>(()=>OfficialService(failing).ApproveAndPublishAsync(f.Project,run.Id,f.Accounts[0],new(0,null),default));
        Assert.Equal("Draft",(await OfficialCall(s=>s.GetAsync(f.Project,run.Id,f.Accounts[0],default))).State);
        await using(var db=OfficialDb()){Assert.Empty(await db.Set<OfficialContributionRelease>().Where(x=>x.ProjectId==f.Project).ToArrayAsync());Assert.Equal(1,await db.Set<OfficialContributionAudit>().CountAsync(x=>x.RunId==run.Id));}
        using var start=new Barrier(2);
        async Task<string> Publish()=>await Task.Run(async()=>{start.SignalAndWait(TimeSpan.FromSeconds(10));try{return (await OfficialCall(s=>s.ApproveAndPublishAsync(f.Project,run.Id,f.Accounts[0],new(0,null),default))).State;}catch(ProjectException e){return e.Code;}});
        var outcomes=await Task.WhenAll(Publish(),Publish());Assert.Contains("Published",outcomes);
        await using(var db=OfficialDb()){Assert.Single(await db.Set<OfficialContributionRelease>().Where(x=>x.ProjectId==f.Project).ToArrayAsync());Assert.Equal(5,await db.Set<OfficialContributionAudit>().CountAsync(x=>x.RunId==run.Id));}
        Assert.Equal("Published",(await OfficialCall(s=>s.ApproveAndPublishAsync(f.Project,run.Id,f.Accounts[0],new(0,null),default))).State);
        var other=await OfficialFixture();
        await Assert.ThrowsAsync<UniNet.Application.Common.GitHubIntegrationException>(()=>OfficialCall(s=>s.ApproveAndPublishAsync(other.Project,run.Id,f.Accounts[0],new(0,null),default)));
        var same=await OfficialCall(s=>s.CreateAsync(f.Project,f.Accounts[0],new(Guid.NewGuid(),f.Snapshot,f.Peer),default));Assert.Equal(run.Id,same.Id);
    }
    [OfficialPostgresFact]
    public async Task VersionMigrationImportsLegacyReceiptWithoutChangingFrozenRun()
    {
        var suffix="_versions_upgrade_"+Guid.NewGuid().ToString("N")[..8]; var f=await OfficialFixture(suffix);
        await using var db=OfficialDb(suffix);var svc=OfficialService(db);
        var run=await svc.CreateAsync(f.Project,f.Accounts[0],new(Guid.NewGuid(),f.Snapshot,f.Peer),default);
        run=await svc.TransitionAsync(f.Project,run.Id,f.Accounts[0],"submit",new(0),default);
        run=await svc.TransitionAsync(f.Project,run.Id,f.Accounts[0],"approve",new(1),default);
        run=await svc.TransitionAsync(f.Project,run.Id,f.Accounts[0],"finalize",new(2),default);
        db.Add(new OfficialContributionPublication{ProjectId=f.Project,RunId=run.Id,PublishedById=f.Profiles[0]});await db.SaveChangesAsync();
        var migration=db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        await migration.MigrateAsync("20261119190820_AddOfficialContributionV2Workflow");
        await migration.MigrateAsync();db.ChangeTracker.Clear();
        var release=Assert.Single(await db.Set<OfficialContributionRelease>().Where(x=>x.ProjectId==f.Project).ToArrayAsync());Assert.Equal(1,release.Version);Assert.Equal(run.Id,release.RunId);
        Assert.Equal(run.ResultHash,(await svc.GetAsync(f.Project,run.Id,f.Accounts[0],default)).ResultHash);
        Assert.Equal(run.Id,(await svc.MyResultAsync(f.Project,f.Accounts[1],default)).RunId);
        Assert.False(db.Database.HasPendingModelChanges());
        await Assert.ThrowsAsync<PostgresException>(()=>migration.MigrateAsync("20261119190820_AddOfficialContributionV2Workflow"));
    }    [OfficialPostgresFact]
    public async Task CompetingDifferentRunsCannotBothPublishAgainstSameCurrentVersion()
    {
        var f=await OfficialFixture();var r1=await OfficialCall(s=>s.CreateAsync(f.Project,f.Accounts[0],new(Guid.NewGuid(),f.Snapshot,f.Peer),default));
        var snapshot2=await OfficialCall(s=>s.CaptureAsync(f.Project,f.Accounts[0],new(DateTimeOffset.UtcNow),default));
        var r2=await OfficialCall(s=>s.CreateAsync(f.Project,f.Accounts[0],new(Guid.NewGuid(),snapshot2,f.Peer),default));
        Assert.NotEqual(r1.Id,r2.Id);
        using var gate=new Barrier(2);
        async Task<string> Attempt(Guid id)=>await Task.Run(async()=>{gate.SignalAndWait(TimeSpan.FromSeconds(15));try{return(await OfficialCall(s=>s.ApproveAndPublishAsync(f.Project,id,f.Accounts[0],new(0,null),default))).Id.ToString();}catch(ProjectException e){return e.Code;}});
        var outcomes=await Task.WhenAll(Attempt(r1.Id),Attempt(r2.Id));
        await using var db=OfficialDb();var current=Assert.Single(await db.Set<OfficialContributionRelease>().Where(x=>x.ProjectId==f.Project).ToArrayAsync());
        Assert.Equal(1,current.Version);Assert.Contains(current.RunId.ToString(),outcomes);
        var loser=current.RunId==r1.Id?r2.Id:r1.Id;
        Assert.Equal("Draft",(await db.Set<OfficialContributionRun>().AsNoTracking().SingleAsync(x=>x.Id==loser)).State);
        Assert.Equal("OFFICIAL_PUBLICATION_CHANGED",(await Assert.ThrowsAsync<ProjectException>(()=>OfficialCall(s=>s.ApproveAndPublishAsync(f.Project,loser,f.Accounts[0],new(0,null),default)))).Code);
    }}
