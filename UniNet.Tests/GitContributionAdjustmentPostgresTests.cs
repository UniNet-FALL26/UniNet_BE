using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using UniNet.Application;
using UniNet.Application.Common;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;
public sealed partial class GitExperimentalContributionPostgresHttpTests
{
    private sealed class RejectAdjustmentLink : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData e, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken ct = default)
        {
            if(e.Context!.ChangeTracker.Entries<GitContributionAdjustment>().Any(x=>x.State==EntityState.Modified&&x.Entity.GitSnapshotId.HasValue))
                throw new InvalidOperationException("controlled adjustment link failure");
            return ValueTask.FromResult(result);
        }
    }
    private static GitContributionAdjustmentService Adjustments(UniNetDbContext db, bool enabled = true) =>
        new(db, OfficialService(db), Options.Create(new GitSyncOptions { Enabled = enabled }),
            Options.Create(new GitHubAppOptions { Enabled = true }), new ConfigurationBuilder().Build());

    [OfficialPostgresFact]
    public async Task CombinedMigrationUpgradePreservesExistingEvidenceAndHasCleanModel()
    {
        await using var db=OfficialDb("_adjustment_upgrade");
        await db.GetService<IMigrator>().MigrateAsync("20261123061945_AddOfficialContributionPublicationVersions");
        var f=await SeedProjectAsync(db);
        var facts=await db.GitEvidenceVersions.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.ContentHash,x.FrozenFactsJson}).ToArrayAsync();
        var obs=await db.ProjectEvidenceObservations.CountAsync();
        await db.Database.MigrateAsync();
        Assert.Equal(facts,await db.GitEvidenceVersions.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.ContentHash,x.FrozenFactsJson}).ToArrayAsync());
        Assert.Equal(obs,await db.ProjectEvidenceObservations.CountAsync());
        Assert.Empty(await db.Set<GitContributionAdjustment>().ToArrayAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [OfficialPostgresFact]
    public async Task CombinedStartIsProjectScopedIdempotentAndRejectsOverlapOrUnauthorizedCaller()
    {
        var f = await OfficialFixture();
        await using var db = OfficialDb();
        var foreign = await db.GitSyncJobs.AsNoTracking().Where(j => !db.ProjectGitRepositories.Any(l => l.Id == j.ProjectGitRepositoryId && l.ProjectId == f.Project)).Select(j => new { j.Id, j.Status }).ToArrayAsync();
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<GitHubIntegrationException>(() => Adjustments(db).StartAsync(f.Project, f.Accounts[1], new(Guid.NewGuid()), default))).Code);
        Assert.Equal("ADJUSTMENT_EXECUTION_DISABLED", (await Assert.ThrowsAsync<ProjectException>(() => Adjustments(db, false).StartAsync(f.Project, f.Accounts[0], new(Guid.NewGuid()), default))).Code);
        var request = new CombinedAdjustmentStartRequest(Guid.NewGuid());
        var first = await Adjustments(db).StartAsync(f.Project, f.Accounts[0], request, default);
        Assert.Equal(2, first.Repositories.Count); Assert.Equal("Syncing", first.State);
        Assert.All(first.Repositories, r => Assert.Equal("Pending", r.JobStatus));
        Assert.Equal(first.Id, (await Adjustments(db).StartAsync(f.Project, f.Accounts[0], request, default)).Id);
        Assert.Equal("ADJUSTMENT_ACTIVE", (await Assert.ThrowsAsync<ProjectException>(() => Adjustments(db).StartAsync(f.Project, f.Accounts[0], new(Guid.NewGuid()), default))).Code);
        Assert.Equal("ADJUSTMENT_SYNC_NOT_COMPLETE", (await Assert.ThrowsAsync<ProjectException>(() => Adjustments(db).CaptureAsync(f.Project, first.Id!.Value, f.Accounts[0], default))).Code);
        Assert.Equal(foreign, await db.GitSyncJobs.AsNoTracking().Where(j => !db.ProjectGitRepositories.Any(l => l.Id == j.ProjectGitRepositoryId && l.ProjectId == f.Project)).Select(j => new { j.Id, j.Status }).ToArrayAsync());
        Assert.Equal(2, await db.Set<GitContributionAdjustmentRepository>().CountAsync(x => x.AdjustmentId == first.Id));
        Assert.Equal("55000",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"GitContributionAdjustments\" SET \"RequestId\"={Guid.NewGuid()} WHERE \"Id\"={first.Id}"))).SqlState);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    // Explicit disposable fixture receipt construction. This test does not claim a provider fetch occurred.
    private static async Task CompleteAdjustmentFixture(UniNetDbContext db, CombinedAdjustmentResponse plan, bool observations)
    {
        foreach (var item in plan.Repositories)
        {
            var job = await db.GitSyncJobs.SingleAsync(x => x.Id == item.JobId);
            var source = await db.ProjectEvidenceObservations.AsNoTracking().Where(x => x.ProjectGitRepositoryId == item.AssociationId && x.GitSyncJobId != item.JobId).ToArrayAsync();
            if (observations) foreach (var group in source.GroupBy(x => new { x.GitEvidenceVersionId, x.Category, x.ScopeKey }))
            {
                var old = group.First();
                db.Add(new ProjectEvidenceObservation { ProjectGitRepositoryId = item.AssociationId, GitSyncJobId = job.Id,
                    GitHubRepositoryId = old.GitHubRepositoryId, GitEvidenceVersionId = old.GitEvidenceVersionId, Category = old.Category,
                    ScopeKey = old.ScopeKey, ObservedAt = DateTimeOffset.UtcNow, AuthorizationSource = "app", RetrievalCompleteness = old.RetrievalCompleteness });
            }
            var categories = new[] { (Category: "branches", ScopeKey: ""), ("pull-requests", ""), ("issues", ""), ("releases", "") }
                .Concat(observations ? source.Select(x => (x.Category, x.ScopeKey)) : []).Distinct().ToList();
            if (observations) foreach (var o in source.Where(x => x.Category is "pull-requests" or "issues"))
            {
                var v = await db.GitEvidenceVersions.SingleAsync(x => x.Id == o.GitEvidenceVersionId);
                var facts = System.Text.Json.JsonSerializer.Deserialize<GitFrozenFacts>(v.FrozenFactsJson)!;
                var number = facts.Category == "pull-requests" ? facts.Details?.RelatedPullRequestNumber : facts.Details?.RelatedIssueNumber;
                if (number is > 0) foreach (var category in facts.Category == "pull-requests" ? new[] { "pull-request-commits", "pull-request-files", "pull-request-reviews", "pull-request-review-comments" } : new[] { "issue-comments", "issue-events" }) categories.Add((category, number.ToString()!));
            }
            var at = DateTimeOffset.UtcNow;
            foreach (var key in categories.Distinct()) db.Add(new GitSyncCheckpoint { SyncJobId = job.Id, Category = key.Item1, ScopeKey = key.Item2, Status = GitSyncCheckpointStatus.Completed, LastProcessedAt = at });
            job.Status = GitSyncJobStatus.Completed; job.CompletedAt = at; job.UpdatedAt = at;
        }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
    }

    [OfficialPostgresFact]
    public async Task CombinedOldReceiptAndIncompleteNewCheckpointCannotAuthorizeCapture()
    {
        var f = await OfficialFixture(); await using var db = OfficialDb();
        var a = await Adjustments(db).StartAsync(f.Project, f.Accounts[0], new(Guid.NewGuid()), default);
        await db.GitSyncJobs.Where(x => x.Id == a.Repositories[0].JobId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, GitSyncJobStatus.Completed).SetProperty(x => x.CompletedAt, DateTimeOffset.UtcNow));
        var read = await Adjustments(db).ReadAsync(f.Project, a.Id!.Value, f.Accounts[0], default);
        Assert.Contains("ADJUSTMENT_CHECKPOINTS_INCOMPLETE", read.Blockers);
        Assert.Equal("ADJUSTMENT_SYNC_NOT_COMPLETE", (await Assert.ThrowsAsync<ProjectException>(() => Adjustments(db).CaptureAsync(f.Project, a.Id.Value, f.Accounts[0], default))).Code);
        Assert.Null((await db.Set<GitContributionAdjustment>().AsNoTracking().SingleAsync(x => x.Id == a.Id)).GitSnapshotId);
    }

    [OfficialPostgresFact]
    public async Task CombinedCaptureIsAtomicIdempotentAndNewPeerAndRunUseOnlyOwnedInputs()
    {
        var f = await OfficialFixture();
        var published = await OfficialCall(s => s.CreateAsync(f.Project, f.Accounts[0], new(Guid.NewGuid(), f.Snapshot, f.Peer), default));
        await OfficialCall(s => s.ApproveAndPublishAsync(f.Project, published.Id, f.Accounts[0], new(0, null), default));
        await using var db = OfficialDb();
        var a = await Adjustments(db).StartAsync(f.Project, f.Accounts[0], new(Guid.NewGuid()), default);
        await CompleteAdjustmentFixture(db, a, true);
        var read = await Adjustments(db).ReadAsync(f.Project, a.Id!.Value, f.Accounts[0], default);
        Assert.Equal("SyncCompleted", read.State); Assert.All(read.Repositories, r => Assert.Equal(0, r.NewVersionCount));
        a = await Adjustments(db).CaptureAsync(f.Project, a.Id.Value, f.Accounts[0], default);
        Assert.NotEqual(f.Snapshot, a.GitSnapshotId);
        Assert.Equal("NoEligibleChanges",a.EligibleChangeState);
        Assert.Equal(a.GitSnapshotId, (await Adjustments(db).CaptureAsync(f.Project, a.Id.Value, f.Accounts[0], default)).GitSnapshotId);
        Assert.True(await db.Set<ProjectGitEvidenceSnapshotCoverage>().AnyAsync(x => x.SnapshotId == a.GitSnapshotId));
        a = await Adjustments(db).OpenPeerAsync(f.Project, a.Id.Value, f.Accounts[0], default);
        var peer = await OfficialService(db).PeerAsync(f.Project, a.PeerCohortId!.Value, f.Accounts[0], default);
        Assert.Empty(peer.Ratings); Assert.Equal(3, peer.ParticipantIds.Count);
        Assert.Equal("PEER_SNAPSHOT_REQUIRED", (await Assert.ThrowsAsync<ProjectException>(() => Adjustments(db).CreateRunAsync(f.Project, a.Id.Value, f.Accounts[0], default))).Code);
        for (var from = 0; from < 3; from++) for (var to = 0; to < 3; to++) if (from != to)
            await OfficialService(db).SubmitPeerAsync(f.Project, peer.Id, f.Profiles[to], f.Accounts[from], new(4, null, null), default);
        var peerId = await OfficialService(db).ClosePeerAsync(f.Project, peer.Id, f.Accounts[0], default);
        a = await Adjustments(db).CreateRunAsync(f.Project, a.Id.Value, f.Accounts[0], default);
        var calc = await db.GitContributionRuns.AsNoTracking().SingleAsync(x => x.Id == a.RunId);
        Assert.Equal(a.GitSnapshotId, calc.GitEvidenceSnapshotId); Assert.Equal(peerId, calc.PeerSnapshotId);
        Assert.NotEqual(f.Peer, peerId); Assert.NotEqual(published.Id, a.RunId);
        Assert.Equal(published.Id, a.CurrentPublishedRunId);
        Assert.Equal(published.ResultHash, (await OfficialService(db).GetAsync(f.Project, published.Id, f.Accounts[0], default)).ResultHash);
        Assert.Equal(a.RunId, (await Adjustments(db).CreateRunAsync(f.Project, a.Id.Value, f.Accounts[0], default)).RunId);
        var draft=await OfficialService(db).GetAsync(f.Project,a.RunId!.Value,f.Accounts[0],default);
        await OfficialService(db).ApproveAndPublishAsync(f.Project,draft.Id,f.Accounts[0],new(draft.Revision,published.Id,"Disposable adjustment publication"),default);
        Assert.Equal(2,await db.Set<OfficialContributionRelease>().CountAsync(x=>x.ProjectId==f.Project));
        Assert.Equal(published.ResultHash,(await OfficialService(db).GetAsync(f.Project,published.Id,f.Accounts[0],default)).ResultHash);
        var successor=await Adjustments(db).StartAsync(f.Project,f.Accounts[0],new(Guid.NewGuid()),default);
        Assert.NotEqual(a.Id,successor.Id);
        Assert.Equal("55000", (await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"GitContributionAdjustmentRepositories\" WHERE \"AdjustmentId\"={a.Id}"))).SqlState);
    }

    [OfficialPostgresFact]
    public async Task CombinedCompletedEmptyReceiptIsNotFailedAndAuthorizationRevocationBlocksCapture()
    {
        var f = await OfficialFixture(); await using var db = OfficialDb();
        var a = await Adjustments(db).StartAsync(f.Project, f.Accounts[0], new(Guid.NewGuid()), default);
        await CompleteAdjustmentFixture(db, a, false);
        var empty = await Adjustments(db).ReadAsync(f.Project, a.Id!.Value, f.Accounts[0], default);
        Assert.Equal("SyncCompleted", empty.State); Assert.All(empty.Repositories, r => Assert.Equal(0, r.ObservationCount));
        await db.ProjectGitRepositories.Where(x => x.Id == a.Repositories[0].AssociationId).ExecuteUpdateAsync(s => s.SetProperty(x => x.SyncPaused, true));
        Assert.Equal("ADJUSTMENT_SYNC_NOT_COMPLETE", (await Assert.ThrowsAsync<ProjectException>(() => Adjustments(db).CaptureAsync(f.Project, a.Id.Value, f.Accounts[0], default))).Code);
    }

    [OfficialPostgresFact]
    public async Task CombinedCaptureFailureRollsBackSnapshotAndOwnedLinkAndFreshRetryRecovers()
    {
        var f=await OfficialFixture(); await using var db=OfficialDb();
        var a=await Adjustments(db).StartAsync(f.Project,f.Accounts[0],new(Guid.NewGuid()),default);
        await CompleteAdjustmentFixture(db,a,true);
        var before=await db.Set<ProjectGitEvidenceSnapshot>().CountAsync(x=>x.ProjectId==f.Project);
        await using(var failing=new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(db.Database.GetConnectionString()).AddInterceptors(new RejectAdjustmentLink()).Options))
            await Assert.ThrowsAsync<InvalidOperationException>(()=>Adjustments(failing).CaptureAsync(f.Project,a.Id!.Value,f.Accounts[0],default));
        Assert.Equal(before,await db.Set<ProjectGitEvidenceSnapshot>().CountAsync(x=>x.ProjectId==f.Project));
        Assert.Null((await db.Set<GitContributionAdjustment>().AsNoTracking().SingleAsync(x=>x.Id==a.Id)).GitSnapshotId);
        Assert.NotNull((await Adjustments(db).CaptureAsync(f.Project,a.Id!.Value,f.Accounts[0],default)).GitSnapshotId);
    }

    [OfficialPostgresFact]
    public async Task CombinedAbandonRefusesLiveLeaseAndRetainsHistoryBeforeExplicitNewAttempt()
    {
        var f=await OfficialFixture();await using var db=OfficialDb();
        var a=await Adjustments(db).StartAsync(f.Project,f.Accounts[0],new(Guid.NewGuid()),default);
        var id=a.Repositories[0].JobId;
        await db.GitSyncJobs.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,GitSyncJobStatus.Running).SetProperty(x=>x.LeaseId,Guid.NewGuid()).SetProperty(x=>x.LeaseExpiresAt,DateTimeOffset.UtcNow.AddMinutes(5)));
        db.ChangeTracker.Clear();
        Assert.Equal("ADJUSTMENT_LEASE_ACTIVE",(await Assert.ThrowsAsync<ProjectException>(()=>Adjustments(db).AbandonAsync(f.Project,a.Id!.Value,f.Accounts[0],default))).Code);
        await db.GitSyncJobs.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.LeaseExpiresAt,DateTimeOffset.UtcNow.AddSeconds(-1)));
        db.ChangeTracker.Clear();
        var stopped=await Adjustments(db).AbandonAsync(f.Project,a.Id!.Value,f.Accounts[0],default);
        Assert.True(stopped.CanRestart);Assert.All(stopped.Repositories,r=>Assert.Equal("Cancelled",r.JobStatus));
        var next=await Adjustments(db).StartAsync(f.Project,f.Accounts[0],new(Guid.NewGuid()),default);
        Assert.NotEqual(a.Id,next.Id);Assert.All(next.Repositories,r=>Assert.DoesNotContain(r.JobId,a.Repositories.Select(x=>x.JobId)));
    }
}
