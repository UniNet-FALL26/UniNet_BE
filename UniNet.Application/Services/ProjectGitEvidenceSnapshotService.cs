using System.Data;
using Microsoft.EntityFrameworkCore;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

// Internal capture only. The returned ID does not authorize reading private facts.
internal sealed class ProjectGitEvidenceSnapshotService(UniNetDbContext db)
{
    public async Task<Guid> CaptureAsync(Guid projectId, Guid accountId, DateTimeOffset observationCutoff, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) throw new InvalidOperationException("Snapshot capture requires PostgreSQL.");
        if (observationCutoff > DateTimeOffset.UtcNow) throw new ArgumentOutOfRangeException(nameof(observationCutoff));
        observationCutoff = observationCutoff.ToUniversalTime();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await ProjectGitAuthorization.RequireManagerAsync(db, projectId, accountId, ct);
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
        var snapshot = new ProjectGitEvidenceSnapshot { ProjectId = projectId, ObservationCutoff = observationCutoff };
        db.Add(snapshot);
        await db.SaveChangesAsync(ct);
        db.AddRange(entries.Select(x => new ProjectGitEvidenceSnapshotEntry { SnapshotId = snapshot.Id,
            ProjectId = projectId, ProjectEvidenceObservationId = x.Id, GitEvidenceVersionId = x.GitEvidenceVersionId,
            ProjectGitRepositoryId = x.ProjectGitRepositoryId }));
        await db.SaveChangesAsync(ct);
        snapshot.Finalized = true;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return snapshot.Id;
    }
}
