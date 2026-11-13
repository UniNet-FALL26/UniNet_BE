using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed record InitialSyncRecoveryResult(Guid JobId, bool AlreadyQueued);

// Reuses the original job ID so completed and partial checkpoints remain attached.
public sealed class GitHubAppInitialSyncRecoveryService(UniNetDbContext db, IOptions<GitHubAppOptions> app,
    IOptions<GitSyncOptions> sync, IGitHubAppInstallationTokenProvider tokens, IGitHubProviderClient provider,
    ILogger<GitHubAppInitialSyncRecoveryService> logger)
{
    public async Task<InitialSyncRecoveryResult> RecoverAsync(Guid actorId, Guid projectId, Guid linkId, Guid jobId, CancellationToken ct)
    {
        GitHubAppAvailability.Require(app);
        if (!sync.Value.Enabled) throw Error("GITHUB_SYNC_DISABLED", "GitHub synchronization is disabled.", 503);
        if (!db.Database.IsNpgsql()) throw new NotSupportedException("Initial sync recovery requires PostgreSQL transactions.");
        await ProjectGitAuthorization.RequireManagerAsync(db, projectId, actorId, ct);
        var snapshot = await EligibleAsync(projectId, linkId, jobId, ct);
        if (snapshot is null) throw Error("GITHUB_APP_INITIAL_RECOVERY_UNAVAILABLE", "Initial synchronization is not eligible for recovery.", 409);
        if (snapshot.Status is GitSyncJobStatus.Pending or GitSyncJobStatus.Running or GitSyncJobStatus.RetryScheduled)
            return new(jobId, true);
        if (snapshot.Status != GitSyncJobStatus.Failed) throw Error("GITHUB_APP_INITIAL_RECOVERY_UNAVAILABLE", "Initial synchronization is not failed.", 409);
        var token = await tokens.GetAsync(snapshot.InstallationProviderId, snapshot.RepositoryProviderId, ct);
        var remote = await provider.GetInstallationRepositoryAsync(token, snapshot.RepositoryProviderId, ct);
        if (remote.Id != snapshot.RepositoryProviderId || !remote.CanRead)
        {
            tokens.InvalidateInstallation(snapshot.InstallationProviderId);
            throw Error("GITHUB_APP_REPOSITORY_UNAVAILABLE", "Repository access could not be verified.", 403);
        }
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var locked = await db.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_xact_lock_shared(1920167523, hashtext({0})) AS \"Value\"", snapshot.InstallationId.ToString()).SingleAsync(ct);
            if (!locked) throw Error("GITHUB_SYNC_CONFLICT", "Authorization is being reconciled; retry.", 409);
            await ProjectGitAuthorization.RequireManagerAsync(db, projectId, actorId, ct);
            var current = await EligibleAsync(projectId, linkId, jobId, ct);
            if (current is null || current.GrantId != snapshot.GrantId || current.InstallationId != snapshot.InstallationId ||
                current.RepositoryProviderId != snapshot.RepositoryProviderId || current.InstallationUpdatedAt != snapshot.InstallationUpdatedAt ||
                current.GrantVerifiedAt != snapshot.GrantVerifiedAt)
                throw Error("GITHUB_APP_INITIAL_RECOVERY_UNAVAILABLE", "Repository authorization changed; retry after reconciliation.", 409);
            if (current.Status is GitSyncJobStatus.Pending or GitSyncJobStatus.Running or GitSyncJobStatus.RetryScheduled) return new(jobId, true);
            if (current.Status != GitSyncJobStatus.Failed || await db.GitSyncJobs.AnyAsync(x => x.ProjectGitRepositoryId == linkId && x.Id != jobId &&
                (x.Status == GitSyncJobStatus.Pending || x.Status == GitSyncJobStatus.Running || x.Status == GitSyncJobStatus.RetryScheduled || x.Status == GitSyncJobStatus.Paused), ct))
                throw Error("GITHUB_SYNC_CONFLICT", "Another synchronization is active.", 409);
            var now = DateTimeOffset.UtcNow;
            var updated = await db.GitSyncJobs.Where(x => x.Id == jobId && x.Status == GitSyncJobStatus.Failed && x.SyncType == GitSyncType.Initial && x.CredentialSource == GitRepositoryCredentialSource.GitHubApp)
                .ExecuteUpdateAsync(x => x.SetProperty(j => j.Status, GitSyncJobStatus.Pending).SetProperty(j => j.RetryCount, 0)
                    .SetProperty(j => j.CompletedAt, (DateTimeOffset?)null).SetProperty(j => j.NextAttemptAt, (DateTimeOffset?)null)
                    .SetProperty(j => j.LeaseId, (Guid?)null).SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(j => j.ErrorCode, (string?)null).SetProperty(j => j.ErrorMessage, (string?)null).SetProperty(j => j.UpdatedAt, now), ct);
            if (updated != 1) throw Error("GITHUB_SYNC_CONFLICT", "Synchronization changed concurrently.", 409);
            await tx.CommitAsync(ct);
            GitHubAppDiagnostics.InitialRecovered.Add(1);
            var priorCode = current.ErrorCode is { Length: > 0 and <= 100 } code && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? code : "REDACTED_OR_EMPTY";
            logger.LogInformation("Recovered initial Git sync job {JobId} for association {AssociationId} by actor {ActorId} at {RecoveredAt}; previous error {PreviousErrorCode}, retry count {PreviousRetryCount}", jobId, linkId, actorId, now, priorCode, current.RetryCount);
            return new(jobId, false);
        }
        catch (Exception ex) when (ex.GetBaseException() is PostgresException { SqlState: "40001" or "40P01" or "23505" })
        { throw Error("GITHUB_SYNC_CONFLICT", "Synchronization changed concurrently; retry.", 409); }
    }

    private Task<Candidate?> EligibleAsync(Guid project, Guid linkId, Guid jobId, CancellationToken ct) =>
        (from job in db.GitSyncJobs.AsNoTracking()
         join link in db.ProjectGitRepositories.AsNoTracking() on job.ProjectGitRepositoryId equals link.Id
         join repo in db.GitHubRepositories.AsNoTracking() on link.GitHubRepositoryId equals repo.Id
         join grant in db.Set<GitHubAppRepositoryGrant>().AsNoTracking() on link.GitHubAppRepositoryGrantId equals grant.Id
         join installation in db.Set<GitHubAppInstallation>().AsNoTracking() on grant.InstallationId equals installation.Id
         where link.ProjectId == project && link.Id == linkId && job.Id == jobId && link.UnlinkedAt == null && !link.SyncPaused &&
            link.AccessStatus == ProjectGitRepositoryAccessStatus.Active && link.CredentialSource == GitRepositoryCredentialSource.GitHubApp && link.OAuthConnectionId == null &&
            job.CredentialSource == GitRepositoryCredentialSource.GitHubApp && job.OAuthConnectionId == null && job.SyncType == GitSyncType.Initial &&
            job.GitHubRepositoryId == repo.Id && job.GitHubAppRepositoryGrantId == grant.Id && grant.RevokedAt == null && grant.ProviderRepositoryId == repo.ProviderRepositoryId &&
            installation.Status == GitHubAppInstallationStatus.Active && !installation.VerificationUnknown
         select new Candidate(job.Status, grant.Id, installation.Id, installation.ProviderInstallationId, repo.ProviderRepositoryId, installation.UpdatedAt, grant.VerifiedAt, job.ErrorCode, job.RetryCount)).SingleOrDefaultAsync(ct);
    private sealed record Candidate(GitSyncJobStatus Status, Guid GrantId, Guid InstallationId, long InstallationProviderId, long RepositoryProviderId, DateTimeOffset InstallationUpdatedAt, DateTimeOffset GrantVerifiedAt, string? ErrorCode, int RetryCount);
    private static GitHubIntegrationException Error(string code, string message, int status) => new(code, message, status);
}
