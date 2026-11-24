using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;
using UniNet.Domain.Enums;

namespace UniNet.Application.Services;

// Internal capture only. The returned ID does not authorize reading private facts.
internal sealed class ProjectGitEvidenceSnapshotService(UniNetDbContext db)
{
    public async Task<Guid> CaptureAsync(Guid projectId, Guid accountId, DateTimeOffset observationCutoff, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) throw new InvalidOperationException("Snapshot capture requires PostgreSQL.");
        if (db.Database.CurrentTransaction is { } ambient && ambient.GetDbTransaction().IsolationLevel is not (IsolationLevel.RepeatableRead or IsolationLevel.Serializable))
            throw new InvalidOperationException("Snapshot capture requires a consistent transaction read point.");
        if (observationCutoff > DateTimeOffset.UtcNow) throw new ArgumentOutOfRangeException(nameof(observationCutoff));
        observationCutoff = observationCutoff.ToUniversalTime();
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct) : null;
        await ProjectGitAuthorization.RequireManagerAsync(db, projectId, accountId, ct);
        var coverageSchema = await db.Database.SqlQuery<int>($"""
            SELECT CASE WHEN to_regclass('public."ProjectGitEvidenceSnapshotCoverage"') IS NOT NULL THEN 1 ELSE 0 END AS "Value"
            """).SingleAsync(ct);
        // Older deployments retain policy-1 capture, explicitly without empty-coverage
        // proof. Do not break legacy capture/read APIs or pretend the new table exists.
        // Include retained historical associations for audit. This is not a scoring eligibility decision.
        var entries = await (from observation in db.ProjectEvidenceObservations.AsNoTracking()
            join association in db.ProjectGitRepositories.AsNoTracking() on observation.ProjectGitRepositoryId equals association.Id
            join job in db.GitSyncJobs.AsNoTracking() on observation.GitSyncJobId equals job.Id
            join version in db.GitEvidenceVersions.AsNoTracking() on observation.GitEvidenceVersionId equals version.Id
            where association.ProjectId == projectId && observation.ObservedAt <= observationCutoff
                && job.ProjectGitRepositoryId == association.Id && job.GitHubRepositoryId == association.GitHubRepositoryId
                && observation.GitHubRepositoryId == association.GitHubRepositoryId
                && version.GitHubRepositoryId == association.GitHubRepositoryId
            orderby observation.Id
            select new { observation.Id, observation.GitEvidenceVersionId, observation.ProjectGitRepositoryId }).ToListAsync(ct);
        var coverage = coverageSchema == 1 ? await (from checkpoint in db.GitSyncCheckpoints.AsNoTracking()
            join job in db.GitSyncJobs.AsNoTracking() on checkpoint.SyncJobId equals job.Id
            join association in db.ProjectGitRepositories.AsNoTracking() on job.ProjectGitRepositoryId equals association.Id
            where association.ProjectId == projectId && job.GitHubRepositoryId == association.GitHubRepositoryId &&
                job.Status == GitSyncJobStatus.Completed && job.CompletedAt != null && job.CompletedAt <= observationCutoff &&
                checkpoint.Status == GitSyncCheckpointStatus.Completed && checkpoint.LastProcessedAt != null &&
                checkpoint.LastProcessedAt <= job.CompletedAt
            select new { checkpoint.Id, checkpoint.Category, checkpoint.ScopeKey, checkpoint.ItemsProcessed,
                ScopeCompletedAt = checkpoint.LastProcessedAt!.Value, JobCompletedAt = job.CompletedAt!.Value,
                GitSyncJobId = job.Id, ProjectGitRepositoryId = association.Id }).ToListAsync(ct) : [];
        var snapshot = new ProjectGitEvidenceSnapshot { ProjectId = projectId, ObservationCutoff = observationCutoff, CapturePolicyVersion = coverageSchema == 1 ? 2 : 1 };
        db.Add(snapshot);
        await db.SaveChangesAsync(ct);
        db.AddRange(entries.Select(x => new ProjectGitEvidenceSnapshotEntry { SnapshotId = snapshot.Id,
            ProjectId = projectId, ProjectEvidenceObservationId = x.Id, GitEvidenceVersionId = x.GitEvidenceVersionId,
            ProjectGitRepositoryId = x.ProjectGitRepositoryId }));
        db.AddRange(coverage.Select(x => new ProjectGitEvidenceSnapshotCoverage { SnapshotId = snapshot.Id, ProjectId = projectId,
            ProjectGitRepositoryId = x.ProjectGitRepositoryId, GitSyncJobId = x.GitSyncJobId, GitSyncCheckpointId = x.Id,
            Category = x.Category, ScopeKey = x.ScopeKey, ItemsProcessed = x.ItemsProcessed,
            ScopeCompletedAt = x.ScopeCompletedAt, JobCompletedAt = x.JobCompletedAt }));
        await db.SaveChangesAsync(ct);
        snapshot.Finalized = true;
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return snapshot.Id;
    }
}
