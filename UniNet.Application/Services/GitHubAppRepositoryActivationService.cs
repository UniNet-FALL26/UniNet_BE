using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Npgsql;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

/// <summary>Verifies a pending App link and atomically schedules its initial synchronization.</summary>
public sealed class GitHubAppRepositoryActivationService(UniNetDbContext db, IGitHubAppInstallationTokenProvider tokens,
    IGitHubProviderClient provider, IOptions<GitSyncOptions> options, IOptions<GitHubAppOptions>? appOptions = null, IGitHubWorkerProgress? progress = null,
    ILogger<GitHubAppRepositoryActivationService>? logger = null)
{
    public async Task<bool> ActivateAsync(Guid linkId, CancellationToken ct)
    {
        GitHubAppAvailability.Require(appOptions);
        if (!options.Value.Enabled) return false;
        var candidate = await FindAsync(linkId, ct);
        if (candidate is null) return false;
        if (candidate.Link.AccessStatus == ProjectGitRepositoryAccessStatus.Active && !candidate.Link.SyncPaused)
            return await db.GitSyncJobs.AsNoTracking().AnyAsync(x => x.ProjectGitRepositoryId == linkId && x.SyncType == GitSyncType.Initial && x.CredentialSource == GitRepositoryCredentialSource.GitHubApp, ct);
        if (candidate.Link.AccessStatus != ProjectGitRepositoryAccessStatus.PendingAuthorization || !candidate.Link.SyncPaused) return false;

        // The installation token is scoped to this immutable repository ID. No DB transaction spans provider I/O.
        var token = await tokens.GetAsync(candidate.Installation.ProviderInstallationId, candidate.Repository.ProviderRepositoryId, ct);
        var remote = await provider.GetInstallationRepositoryAsync(token, candidate.Repository.ProviderRepositoryId, ct);
        if (remote.Id != candidate.Repository.ProviderRepositoryId || !remote.CanRead)
            throw new GitHubIntegrationException("GITHUB_APP_REPOSITORY_UNAVAILABLE", "Installation cannot read this repository.", 403);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var current = await FindAsync(linkId, ct);
            if (current is null || current.Link.GitHubAppRepositoryGrantId != candidate.Link.GitHubAppRepositoryGrantId ||
                current.Repository.ProviderRepositoryId != remote.Id || current.Installation.ProviderInstallationId != candidate.Installation.ProviderInstallationId)
                return false;
            if (current.Link.AccessStatus == ProjectGitRepositoryAccessStatus.Active && !current.Link.SyncPaused)
                return await db.GitSyncJobs.AsNoTracking().AnyAsync(x => x.ProjectGitRepositoryId == linkId && x.SyncType == GitSyncType.Initial && x.CredentialSource == GitRepositoryCredentialSource.GitHubApp, ct);
            if (current.Link.AccessStatus != ProjectGitRepositoryAccessStatus.PendingAuthorization || !current.Link.SyncPaused) return false;
            if (await db.GitSyncJobs.AnyAsync(x => x.ProjectGitRepositoryId == linkId && x.SyncType == GitSyncType.Initial, ct)) return false;
            var link = await db.ProjectGitRepositories.SingleAsync(x => x.Id == linkId, ct);
            var now = DateTimeOffset.UtcNow;
            link.AccessStatus = ProjectGitRepositoryAccessStatus.Active;
            link.SyncPaused = false; link.LastAccessCheckedAt = now; link.UpdatedAt = now; link.Version = Guid.NewGuid();
            db.GitSyncJobs.Add(new GitSyncJob { ProjectGitRepositoryId = link.Id, GitHubRepositoryId = link.GitHubRepositoryId,
                CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = link.GitHubAppRepositoryGrantId,
                OAuthConnectionId = null, RequestedByProfileId = link.LinkedByProfileId, SyncType = GitSyncType.Initial,
                Status = GitSyncJobStatus.Pending, IdempotencyKey = "github-app-initial", CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            GitHubAppDiagnostics.JobEnqueued.Add(1);
            GitHubAppDiagnostics.InitialActivated.Add(1);
            logger?.LogInformation("GitHub App repository association {AssociationId} activated and initial sync queued", link.Id);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException || ex.GetBaseException() is PostgresException { SqlState: "40001" or "40P01" })
        { throw new GitHubIntegrationException("GITHUB_APP_ACTIVATION_CONFLICT", "Activation changed concurrently; retry.", 409); }
    }

    public async Task RetryPendingAsync(CancellationToken ct)
    {
        GitHubAppAvailability.Require(appOptions);
        if (!options.Value.Enabled) return;
        await MarkLocallyUnavailableAsync(ct);
        var ids = await db.ProjectGitRepositories.AsNoTracking().Where(x => x.CredentialSource == GitRepositoryCredentialSource.GitHubApp &&
            x.UnlinkedAt == null && (x.SyncPaused && x.AccessStatus == ProjectGitRepositoryAccessStatus.PendingAuthorization ||
                x.AccessStatus == ProjectGitRepositoryAccessStatus.AccessLost))
            .OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            try
            {
                var status = await db.ProjectGitRepositories.AsNoTracking().Where(x => x.Id == id).Select(x => x.AccessStatus).SingleAsync(ct);
                if (status == ProjectGitRepositoryAccessStatus.AccessLost) await RecoverAsync(id, ct);
                else await ActivateAsync(id, ct);
                progress?.Completed("reconciliation");
            }
            catch (GitHubIntegrationException) { db.ChangeTracker.Clear(); }
            catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }
            catch (Exception ex) when (ex.GetBaseException() is PostgresException { SqlState: "40001" or "40P01" or "23505" }) { db.ChangeTracker.Clear(); }
            catch (HttpRequestException) { db.ChangeTracker.Clear(); }
            catch (System.Security.Cryptography.CryptographicException) { db.ChangeTracker.Clear(); }
            catch (ArgumentException) { db.ChangeTracker.Clear(); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { db.ChangeTracker.Clear(); }
        }
    }

    private async Task MarkLocallyUnavailableAsync(CancellationToken ct)
    {
        var unavailable = await (
            from link in db.ProjectGitRepositories
            join repository in db.GitHubRepositories on link.GitHubRepositoryId equals repository.Id
            join grant in db.Set<GitHubAppRepositoryGrant>() on link.GitHubAppRepositoryGrantId equals grant.Id
            join installation in db.Set<GitHubAppInstallation>() on grant.InstallationId equals installation.Id
            where link.CredentialSource == GitRepositoryCredentialSource.GitHubApp && link.UnlinkedAt == null &&
                link.AccessStatus == ProjectGitRepositoryAccessStatus.Active &&
                (grant.RevokedAt != null || grant.ProviderRepositoryId != repository.ProviderRepositoryId || installation.Status != GitHubAppInstallationStatus.Active)
            select new { Link = link, installation.ProviderInstallationId }).ToListAsync(ct);
        foreach (var item in unavailable)
        {
            tokens.InvalidateInstallation(item.ProviderInstallationId);
            var now = DateTimeOffset.UtcNow;
            item.Link.AccessStatus = ProjectGitRepositoryAccessStatus.AccessLost;
            item.Link.LastAccessCheckedAt = now; item.Link.UpdatedAt = now; item.Link.Version = Guid.NewGuid();
            db.ProjectGitRepositoryHistory.Add(new ProjectGitRepositoryHistory { ProjectGitRepositoryId = item.Link.Id,
                ActorProfileId = item.Link.LinkedByProfileId, Action = ProjectGitRepositoryHistoryAction.AccessLost,
                Details = "GitHub App installation or repository grant is unavailable." });
            var queued = await db.GitSyncJobs.Where(x => x.ProjectGitRepositoryId == item.Link.Id &&
                (x.Status == GitSyncJobStatus.Pending || x.Status == GitSyncJobStatus.RetryScheduled)).ToListAsync(ct);
            foreach (var job in queued)
            {
                job.Status = GitSyncJobStatus.Cancelled; job.CompletedAt = now; job.NextAttemptAt = null;
                job.ErrorCode = "GITHUB_REPOSITORY_ACCESS_LOST"; job.ErrorMessage = "GitHub App access is unavailable."; job.UpdatedAt = now;
            }
        }
        if (unavailable.Count > 0) { await db.SaveChangesAsync(ct); GitHubAppDiagnostics.AccessLost.Add(unavailable.Count); }
    }

    private async Task<bool> RecoverAsync(Guid linkId, CancellationToken ct)
    {
        var candidate = await FindRecoveryAsync(linkId, ct);
        if (candidate is null || candidate.Link.AccessStatus != ProjectGitRepositoryAccessStatus.AccessLost ||
            candidate.Installation.Status != GitHubAppInstallationStatus.Active || candidate.Installation.VerificationUnknown || candidate.Grant.RevokedAt != null) return false;
        tokens.InvalidateInstallation(candidate.Installation.ProviderInstallationId);
        var token = await tokens.GetAsync(candidate.Installation.ProviderInstallationId, candidate.Repository.ProviderRepositoryId, ct);
        var remote = await provider.GetInstallationRepositoryAsync(token, candidate.Repository.ProviderRepositoryId, ct);
        if (remote.Id != candidate.Repository.ProviderRepositoryId || !remote.CanRead) return false;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var current = await FindRecoveryAsync(linkId, ct);
        if (current is null || current.Link.AccessStatus != ProjectGitRepositoryAccessStatus.AccessLost ||
            current.Link.Version != candidate.Link.Version || current.Link.GitHubAppRepositoryGrantId != candidate.Link.GitHubAppRepositoryGrantId ||
            current.Installation.ProviderInstallationId != candidate.Installation.ProviderInstallationId ||
            current.Installation.Status != candidate.Installation.Status || current.Installation.UpdatedAt != candidate.Installation.UpdatedAt ||
            current.Installation.VerificationUnknown ||
            current.Grant.RevokedAt != candidate.Grant.RevokedAt || current.Grant.VerifiedAt != candidate.Grant.VerifiedAt ||
            current.Repository.ProviderRepositoryId != remote.Id) return false;
        var link = await db.ProjectGitRepositories.SingleAsync(x => x.Id == linkId, ct);
        var grant = await db.Set<GitHubAppRepositoryGrant>().SingleAsync(x => x.Id == link.GitHubAppRepositoryGrantId, ct);
        var installation = await db.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == grant.InstallationId, ct);
        var now = DateTimeOffset.UtcNow;
        installation.Status = GitHubAppInstallationStatus.Active; installation.LastVerifiedAt = now; installation.UpdatedAt = now;
        grant.RevokedAt = null; grant.VerifiedAt = now;
        link.AccessStatus = ProjectGitRepositoryAccessStatus.Active; link.LastAccessCheckedAt = now; link.UpdatedAt = now; link.Version = Guid.NewGuid();
        db.ProjectGitRepositoryHistory.Add(new ProjectGitRepositoryHistory { ProjectGitRepositoryId = link.Id, ActorProfileId = link.LinkedByProfileId,
            Action = ProjectGitRepositoryHistoryAction.AccessRestored, Details = "GitHub App repository access was verified." });
        // A terminal Initial job retains checkpoints and must be explicitly recovered
        // as Initial; do not create a Scheduled job that blocks that recovery.
        var failedInitial = await db.GitSyncJobs.AnyAsync(x => x.ProjectGitRepositoryId == link.Id && x.SyncType == GitSyncType.Initial && x.Status == GitSyncJobStatus.Failed, ct) &&
            !await db.GitSyncJobs.AnyAsync(x => x.ProjectGitRepositoryId == link.Id && x.SyncType == GitSyncType.Initial && x.Status == GitSyncJobStatus.Completed, ct);
        var enqueued = false;
        if (!failedInitial && !link.SyncPaused && !await db.GitSyncJobs.AnyAsync(x => x.ProjectGitRepositoryId == link.Id &&
            (x.Status == GitSyncJobStatus.Pending || x.Status == GitSyncJobStatus.Running || x.Status == GitSyncJobStatus.RetryScheduled || x.Status == GitSyncJobStatus.Paused), ct))
        {
            db.GitSyncJobs.Add(new GitSyncJob { ProjectGitRepositoryId = link.Id, GitHubRepositoryId = link.GitHubRepositoryId,
                CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, OAuthConnectionId = null,
                RequestedByProfileId = link.LinkedByProfileId, SyncType = GitSyncType.Scheduled, Status = GitSyncJobStatus.Pending, CreatedAt = now, UpdatedAt = now });
            enqueued = true;
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        GitHubAppDiagnostics.AccessRestored.Add(1); if (enqueued) GitHubAppDiagnostics.JobEnqueued.Add(1);
        logger?.LogInformation("GitHub App repository association {AssociationId} access restored", link.Id);
        return true;
    }

    private async Task<Candidate?> FindRecoveryAsync(Guid id, CancellationToken ct) => await (
        from link in db.ProjectGitRepositories.AsNoTracking()
        join repository in db.GitHubRepositories.AsNoTracking() on link.GitHubRepositoryId equals repository.Id
        join grant in db.Set<GitHubAppRepositoryGrant>().AsNoTracking() on link.GitHubAppRepositoryGrantId equals grant.Id
        join installation in db.Set<GitHubAppInstallation>().AsNoTracking() on grant.InstallationId equals installation.Id
        where link.Id == id && link.UnlinkedAt == null && link.CredentialSource == GitRepositoryCredentialSource.GitHubApp &&
            link.OAuthConnectionId == null && grant.ProviderRepositoryId == repository.ProviderRepositoryId
        select new Candidate(link, repository, installation, grant)).SingleOrDefaultAsync(ct);

    private async Task<Candidate?> FindAsync(Guid id, CancellationToken ct) => await (
        from link in db.ProjectGitRepositories.AsNoTracking()
        join repository in db.GitHubRepositories.AsNoTracking() on link.GitHubRepositoryId equals repository.Id
        join grant in db.Set<GitHubAppRepositoryGrant>().AsNoTracking() on link.GitHubAppRepositoryGrantId equals grant.Id
        join installation in db.Set<GitHubAppInstallation>().AsNoTracking() on grant.InstallationId equals installation.Id
        where link.Id == id && link.UnlinkedAt == null && link.CredentialSource == GitRepositoryCredentialSource.GitHubApp &&
            link.OAuthConnectionId == null && grant.RevokedAt == null && grant.ProviderRepositoryId == repository.ProviderRepositoryId &&
            installation.Status == GitHubAppInstallationStatus.Active && !installation.VerificationUnknown
        select new Candidate(link, repository, installation, grant)).SingleOrDefaultAsync(ct);

    private sealed record Candidate(ProjectGitRepository Link, GitHubRepository Repository, GitHubAppInstallation Installation, GitHubAppRepositoryGrant Grant);
}
