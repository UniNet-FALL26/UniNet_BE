using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.Common;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

internal enum SnapshotEvidenceStatus { Valid, Incomplete, Unsupported, Corrupt }
internal sealed record SnapshotEvidenceEntry(Guid AssociationId, Guid RepositoryId, Guid ObservationId,
    Guid JobId, Guid VersionId, string Category, string ScopeKey, string ExternalObjectId,
    DateTimeOffset ObservedAt, string ContentHash, SnapshotEvidenceStatus Status, string Reason, GitFrozenFacts? Facts);
internal sealed record ProjectGitEvidenceSnapshotRead(Guid SnapshotId, Guid ProjectId, DateTimeOffset CreatedAt,
    DateTimeOffset ObservationCutoff, int PolicyVersion, int RestrictedCount, IReadOnlyList<SnapshotEvidenceEntry> Entries,
    IReadOnlyList<ProjectGitEvidenceSnapshotCoverage>? Coverage = null);

// This is an internal boundary, not an endpoint or authorization to read a version by ID.
internal sealed class ProjectGitEvidenceSnapshotReader(UniNetDbContext db)
{
    public async Task<ProjectGitEvidenceSnapshotRead> ReadAsync(Guid projectId, Guid snapshotId, Guid accountId, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) throw new InvalidOperationException("Snapshot reads require PostgreSQL.");
        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : null;
        await ProjectGitAuthorization.RequireProjectViewerAsync(db, projectId, accountId, ct);
        var snapshot = await db.Set<ProjectGitEvidenceSnapshot>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == snapshotId && x.ProjectId == projectId && x.Finalized, ct)
            ?? throw new GitHubIntegrationException("SNAPSHOT_NOT_FOUND", "Snapshot not found.", 404);
        if (snapshot.CapturePolicyVersion is not (1 or 2))
            throw new GitHubIntegrationException("SNAPSHOT_POLICY_UNSUPPORTED", "Unsupported snapshot capture policy.", 409);
        var memberships = await db.Set<ProjectGitEvidenceSnapshotEntry>().AsNoTracking()
            .Where(x => x.SnapshotId == snapshotId && x.ProjectId == projectId)
            .OrderBy(x => x.ProjectGitRepositoryId).ThenBy(x => x.ProjectEvidenceObservationId).ToListAsync(ct);
        var authorized = new HashSet<Guid>();
        // Legacy policy-1 snapshots never query the additive coverage table.
        var coverage = snapshot.CapturePolicyVersion >= 2 ? await db.Set<ProjectGitEvidenceSnapshotCoverage>().AsNoTracking()
            .Where(x => x.SnapshotId == snapshotId && x.ProjectId == projectId)
            .OrderBy(x => x.GitSyncCheckpointId).ToListAsync(ct) : [];
        foreach (var id in memberships.Select(x => x.ProjectGitRepositoryId).Concat(coverage.Select(x => x.ProjectGitRepositoryId)).Distinct().Order())
        {
            // Lock local authorization through the read, in the same order as the sync writer.
            await db.Database.SqlQuery<int>($"""
                SELECT 1 AS "Value" FROM "ProjectGitRepositories" l
                JOIN "GitHubRepositories" r ON r."Id" = l."GitHubRepositoryId"
                JOIN "GitHubAppRepositoryGrants" g ON g."Id" = l."GitHubAppRepositoryGrantId"
                JOIN "GitHubAppInstallations" i ON i."Id" = g."InstallationId"
                WHERE l."Id" = {id} FOR SHARE OF l, r, g, i
                """).ToListAsync(ct);
            await db.Database.SqlQuery<int>($"""
                SELECT 1 AS "Value" FROM "ProjectGitRepositories" l
                JOIN "GitHubRepositories" r ON r."Id" = l."GitHubRepositoryId"
                JOIN "GitHubOAuthConnections" c ON c."Id" = l."OAuthConnectionId"
                WHERE l."Id" = {id} FOR SHARE OF l, r, c
                """).ToListAsync(ct);
            var app = await (from l in db.ProjectGitRepositories.AsNoTracking()
                join r in db.GitHubRepositories.AsNoTracking() on l.GitHubRepositoryId equals r.Id
                join g in db.Set<GitHubAppRepositoryGrant>().AsNoTracking() on l.GitHubAppRepositoryGrantId equals g.Id
                join i in db.Set<GitHubAppInstallation>().AsNoTracking() on g.InstallationId equals i.Id
                where l.Id == id && l.ProjectId == projectId && l.CredentialSource == GitRepositoryCredentialSource.GitHubApp &&
                    l.OAuthConnectionId == null && l.UnlinkedAt == null && l.AccessStatus == ProjectGitRepositoryAccessStatus.Active &&
                    g.RevokedAt == null && g.ProviderRepositoryId == r.ProviderRepositoryId && i.Status == GitHubAppInstallationStatus.Active && !i.VerificationUnknown
                select l.Id).AnyAsync(ct);
            var oauth = !app && await (from l in db.ProjectGitRepositories.AsNoTracking()
                join c in db.GitHubOAuthConnections.AsNoTracking() on l.OAuthConnectionId equals c.Id
                where l.Id == id && l.ProjectId == projectId && l.CredentialSource == GitRepositoryCredentialSource.OAuth &&
                    l.UnlinkedAt == null && l.AccessStatus == ProjectGitRepositoryAccessStatus.Active && c.Status == GitHubOAuthConnectionStatus.Active && c.RevokedAt == null
                select l.Id).AnyAsync(ct);
            if (app || oauth) authorized.Add(id);
        }
        var visible = memberships.Where(x => authorized.Contains(x.ProjectGitRepositoryId)).ToArray();
        var observationIds = visible.Select(x => x.ProjectEvidenceObservationId).ToArray();
        var rows = await (from entry in db.Set<ProjectGitEvidenceSnapshotEntry>().AsNoTracking()
            join observation in db.ProjectEvidenceObservations.AsNoTracking() on entry.ProjectEvidenceObservationId equals observation.Id
            join version in db.GitEvidenceVersions.AsNoTracking() on entry.GitEvidenceVersionId equals version.Id
            join source in db.GitHubProviderObjects.AsNoTracking() on version.GitHubProviderObjectId equals source.Id
            join job in db.GitSyncJobs.AsNoTracking() on observation.GitSyncJobId equals job.Id
            where entry.SnapshotId == snapshotId && entry.ProjectId == projectId && observationIds.Contains(entry.ProjectEvidenceObservationId)
                && entry.ProjectGitRepositoryId == observation.ProjectGitRepositoryId && entry.GitEvidenceVersionId == observation.GitEvidenceVersionId
                && job.ProjectGitRepositoryId == entry.ProjectGitRepositoryId && job.GitHubRepositoryId == observation.GitHubRepositoryId
                && version.GitHubRepositoryId == observation.GitHubRepositoryId
            select new { entry.ProjectGitRepositoryId, observation.Id, observation.GitSyncJobId, observation.GitHubRepositoryId,
                observation.Category, observation.ScopeKey, observation.ObservedAt, observation.RetrievalCompleteness,
                Version = version, Source = new GitHubProviderObject { Id = source.Id,
                    GitHubRepositoryId = source.GitHubRepositoryId, Category = source.Category, ExternalObjectId = source.ExternalObjectId } }).ToListAsync(ct);
        if (rows.Count != visible.Length) throw new GitHubIntegrationException("SNAPSHOT_PROVENANCE_INVALID", "Snapshot provenance is inconsistent.", 409);
        var entries = rows.Select(row =>
        {
            var validation = GitSnapshotEvidenceValidator.Validate(row.Version, row.Source);
            var status = validation switch {
                GitSnapshotEvidenceValidation.UnsupportedContract or GitSnapshotEvidenceValidation.UnsupportedHashAlgorithm => SnapshotEvidenceStatus.Unsupported,
                GitSnapshotEvidenceValidation.Valid => SnapshotEvidenceStatus.Valid,
                _ => SnapshotEvidenceStatus.Corrupt };
            if (!StringComparer.Ordinal.Equals(row.Category, row.Source.Category))
                status = SnapshotEvidenceStatus.Corrupt;
            GitFrozenFacts? facts = null;
            if (status == SnapshotEvidenceStatus.Valid)
            {
                facts = JsonSerializer.Deserialize<GitFrozenFacts>(row.Version.FrozenFactsJson)!;
                if (row.Version.EvidenceStatus != "Complete" || row.RetrievalCompleteness != "Complete" || facts.Completeness != "Complete")
                    status = SnapshotEvidenceStatus.Incomplete;
            }
            return new SnapshotEvidenceEntry(row.ProjectGitRepositoryId, row.GitHubRepositoryId, row.Id, row.GitSyncJobId,
                row.Version.Id, row.Category, row.ScopeKey, row.Source.ExternalObjectId, row.ObservedAt, row.Version.ContentHash,
                status, status == SnapshotEvidenceStatus.Incomplete ? "IncompleteSource" : validation.ToString(), facts);
        }).OrderBy(x => x.AssociationId).ThenBy(x => x.ObservationId).ToArray();
        if (tx is not null) await tx.CommitAsync(ct);
        return new(snapshot.Id, snapshot.ProjectId, snapshot.CreatedAt, snapshot.ObservationCutoff,
            snapshot.CapturePolicyVersion, memberships.Count - visible.Length + coverage.Count(x => !authorized.Contains(x.ProjectGitRepositoryId)),
            entries, coverage.Where(x => authorized.Contains(x.ProjectGitRepositoryId)).ToArray());
    }
}
