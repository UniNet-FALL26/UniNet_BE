using Microsoft.EntityFrameworkCore;
using System.Data;
using Npgsql;
using UniNet.Application.Common;
using UniNet.Application.DTOs;
using UniNet.Application.Interfaces;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace UniNet.Application.Services;

public sealed class GitSyncJobService(UniNetDbContext db, IOptions<GitSyncOptions> options, IOptions<GitHubAppOptions>? appOptions = null, ILogger<GitSyncJobService>? logger = null) : IGitSyncJobService
{
    private bool AppEnabled => appOptions?.Value.Enabled == true;
    private static readonly GitSyncJobStatus[] ActiveStatuses =
    [
        GitSyncJobStatus.Pending,
        GitSyncJobStatus.Running,
        GitSyncJobStatus.RetryScheduled,
        GitSyncJobStatus.Paused
    ];

    public async Task<GitSyncJob?> ClaimNextAsync(TimeSpan leaseDuration, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql())
            throw new NotSupportedException("Durable Git sync job claiming requires PostgreSQL row-lock semantics.");
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));

        var now = DateTimeOffset.UtcNow;
        var leaseId = Guid.NewGuid();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var jobs = await db.GitSyncJobs.FromSqlInterpolated($"""
            UPDATE "GitSyncJobs" AS job
            SET "Status" = {(short)GitSyncJobStatus.Running},
                "LeaseId" = {leaseId},
                "LeaseExpiresAt" = {now.Add(leaseDuration)},
                "LastHeartbeatAt" = {now},
                "StartedAt" = COALESCE(job."StartedAt", {now}),
                "UpdatedAt" = {now}
            WHERE job."Id" = (
                SELECT candidate."Id"
                FROM "GitSyncJobs" AS candidate
                INNER JOIN "ProjectGitRepositories" AS association
                    ON association."Id" = candidate."ProjectGitRepositoryId"
                INNER JOIN "GitHubRepositories" AS repository ON repository."Id" = association."GitHubRepositoryId"
                LEFT JOIN "GitHubOAuthConnections" AS credential
                    ON credential."Id" = association."OAuthConnectionId"
                LEFT JOIN "GitHubAppRepositoryGrants" AS app_grant ON app_grant."Id" = association."GitHubAppRepositoryGrantId"
                LEFT JOIN "GitHubAppInstallations" AS installation ON installation."Id" = app_grant."InstallationId"
                WHERE candidate."GitHubRepositoryId" = association."GitHubRepositoryId" AND (
                    (association."CredentialSource" = 0 AND candidate."CredentialSource" = 0
                        AND candidate."GitHubAppRepositoryGrantId" IS NULL AND candidate."OAuthConnectionId" = association."OAuthConnectionId"
                        AND credential."Status" = {(short)GitHubOAuthConnectionStatus.Active}) OR
                    ({AppEnabled} AND association."CredentialSource" = 1 AND candidate."CredentialSource" = 1 AND candidate."SyncType" IN ({(short)GitSyncType.Initial}, {(short)GitSyncType.Manual}, {(short)GitSyncType.Scheduled}, {(short)GitSyncType.Webhook})
                        AND candidate."OAuthConnectionId" IS NULL AND association."OAuthConnectionId" IS NULL
                        AND candidate."GitHubAppRepositoryGrantId" = association."GitHubAppRepositoryGrantId"
                        AND app_grant."RevokedAt" IS NULL AND app_grant."ProviderRepositoryId" = repository."ProviderRepositoryId"
                        AND installation."Status" = {(short)GitHubAppInstallationStatus.Active} AND installation."VerificationUnknown" = FALSE))
                    AND association."UnlinkedAt" IS NULL
                    AND association."SyncPaused" = FALSE
                    AND association."AccessStatus" = {(short)ProjectGitRepositoryAccessStatus.Active}
                    AND (
                        candidate."Status" = {(short)GitSyncJobStatus.Running}
                        OR NOT EXISTS (
                            SELECT 1
                            FROM "GitSyncJobs" AS in_flight
                            WHERE in_flight."ProjectGitRepositoryId" = candidate."ProjectGitRepositoryId"
                                AND in_flight."Status" = {(short)GitSyncJobStatus.Running}
                        )
                    )
                    AND (
                        (candidate."Status" IN ({(short)GitSyncJobStatus.Pending}, {(short)GitSyncJobStatus.RetryScheduled})
                            AND (candidate."NextAttemptAt" IS NULL OR candidate."NextAttemptAt" <= {now}))
                        OR (candidate."Status" = {(short)GitSyncJobStatus.Running}
                            AND candidate."LeaseExpiresAt" <= {now})
                    )
                ORDER BY candidate."CreatedAt", candidate."Id"
                FOR UPDATE OF candidate SKIP LOCKED
                LIMIT 1
            )
            RETURNING job.*
            """).AsNoTracking().ToListAsync(ct);
        if (jobs.SingleOrDefault() is { CredentialSource: GitRepositoryCredentialSource.GitHubApp } appJob)
        {
            var installationId = await db.Set<GitHubAppRepositoryGrant>().AsNoTracking()
                .Where(x => x.Id == appJob.GitHubAppRepositoryGrantId).Select(x => x.InstallationId).SingleAsync(ct);
            // A fresh statement after the non-blocking lock avoids using the UPDATE's pre-revoke MVCC snapshot.
            var acquired = await db.Database.SqlQueryRaw<bool>(
                "SELECT pg_try_advisory_xact_lock_shared(1920167523, hashtext({0})) AS \"Value\"", installationId.ToString()).SingleAsync(ct);
            var eligible = acquired ? (await EligibleAppCandidatesAsync(null, appJob.ProjectGitRepositoryId, ct)).SingleOrDefault() : null;
            if (eligible is null || eligible.GrantId != appJob.GitHubAppRepositoryGrantId || eligible.RepositoryId != appJob.GitHubRepositoryId)
            {
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                return null;
            }
        }
        await transaction.CommitAsync(ct);
        if (jobs.SingleOrDefault() is { CredentialSource: GitRepositoryCredentialSource.GitHubApp }) GitHubAppDiagnostics.JobClaimed.Add(1);
        return jobs.SingleOrDefault();
    }

    public async Task CompleteAsync(Guid jobId, Guid leaseId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            var job = await db.GitSyncJobs.SingleOrDefaultAsync(x => x.Id == jobId && x.Status == GitSyncJobStatus.Running && x.LeaseId == leaseId, ct);
            if (job?.LeaseExpiresAt is not { } expiry || expiry <= now)
                throw Error("GITHUB_SYNC_LEASE_LOST", "The synchronization job lease expired or was replaced.", 409);
            job.Status = GitSyncJobStatus.Completed; job.CompletedAt = now; job.LeaseId = null; job.LeaseExpiresAt = null;
            job.LastHeartbeatAt = now; job.UpdatedAt = now; await db.SaveChangesAsync(ct);
            GitHubAppDiagnostics.JobCompleted.Add(1); return;
        }
        var updated = await db.GitSyncJobs
            .Where(x => x.Id == jobId && x.Status == GitSyncJobStatus.Running && x.LeaseId == leaseId && x.LeaseExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, GitSyncJobStatus.Completed)
                .SetProperty(x => x.CompletedAt, now)
                .SetProperty(x => x.LeaseId, (Guid?)null)
                .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(x => x.LastHeartbeatAt, now)
                .SetProperty(x => x.UpdatedAt, now), ct);
        if (updated != 1)
            throw Error("GITHUB_SYNC_LEASE_LOST", "The synchronization job lease expired or was replaced.", 409);
        // This count includes both credential sources; it has no high-cardinality labels.
        GitHubAppDiagnostics.JobCompleted.Add(1);
    }

    public async Task<bool> RenewLeaseAsync(
        Guid jobId, Guid leaseId, TimeSpan leaseDuration, CancellationToken ct)
    {
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        var now = DateTimeOffset.UtcNow;
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            var sqliteJob = await db.GitSyncJobs.SingleOrDefaultAsync(x =>
                x.Id == jobId && x.Status == GitSyncJobStatus.Running && x.LeaseId == leaseId, ct);
            if (sqliteJob?.LeaseExpiresAt is not { } sqliteExpiry || sqliteExpiry <= now) return false;
            sqliteJob.LeaseExpiresAt = now.Add(leaseDuration);
            sqliteJob.LastHeartbeatAt = now;
            sqliteJob.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return true;
        }

        var updated = await db.GitSyncJobs
            .Where(x => x.Id == jobId && x.Status == GitSyncJobStatus.Running &&
                x.LeaseId == leaseId && x.LeaseExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.LeaseExpiresAt, now.Add(leaseDuration))
                .SetProperty(x => x.LastHeartbeatAt, now)
                .SetProperty(x => x.UpdatedAt, now), ct);
        return updated == 1;
    }

    public async Task<bool> FailAsync(
        Guid jobId, Guid leaseId, string errorCode, string safeErrorMessage,
        DateTimeOffset retryAt, CancellationToken ct, bool retryable = true)
    {
        if (string.IsNullOrWhiteSpace(errorCode) || errorCode.Length > 100)
            throw new ArgumentException("A valid stable error code is required.", nameof(errorCode));
        if (string.IsNullOrWhiteSpace(safeErrorMessage) || safeErrorMessage.Length > 1000)
            throw new ArgumentException("A safe bounded error message is required.", nameof(safeErrorMessage));

        if (db.Database.IsNpgsql())
        {
            var at = DateTimeOffset.UtcNow; var max = Math.Max(1, options.Value.MaxRetryAttempts);
            var updated = await db.GitSyncJobs.Where(x => x.Id == jobId && x.Status == GitSyncJobStatus.Running && x.LeaseId == leaseId && x.LeaseExpiresAt > at)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.RetryCount, j => j.RetryCount + 1)
                    .SetProperty(j => j.ErrorCode, errorCode).SetProperty(j => j.ErrorMessage, safeErrorMessage)
                    .SetProperty(j => j.UpdatedAt, at).SetProperty(j => j.LastHeartbeatAt, at)
                    .SetProperty(j => j.LeaseId, (Guid?)null).SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(j => j.Status, j => !retryable || j.RetryCount + 1 >= max ? GitSyncJobStatus.Failed : GitSyncJobStatus.RetryScheduled)
                    .SetProperty(j => j.CompletedAt, j => !retryable || j.RetryCount + 1 >= max ? (DateTimeOffset?)at : null)
                    .SetProperty(j => j.NextAttemptAt, j => !retryable || j.RetryCount + 1 >= max ? null : (DateTimeOffset?)retryAt), ct);
            if (updated != 1) throw Error("GITHUB_SYNC_LEASE_LOST", "The synchronization job lease expired or was replaced.", 409);
            var result = await db.GitSyncJobs.AsNoTracking().Where(x => x.Id == jobId).Select(x => new { x.Status, x.CredentialSource }).SingleAsync(ct);
            if (result.CredentialSource == GitRepositoryCredentialSource.GitHubApp)
            {
                if (result.Status == GitSyncJobStatus.RetryScheduled) GitHubAppDiagnostics.JobRetry.Add(1); else GitHubAppDiagnostics.JobFailed.Add(1);
                logger?.LogWarning("Git sync job {JobId} changed to {Status} with code {ErrorCode}", jobId, result.Status, errorCode);
            }
            return result.Status == GitSyncJobStatus.RetryScheduled;
        }

        var job = await db.GitSyncJobs
            .SingleOrDefaultAsync(x => x.Id == jobId && x.Status == GitSyncJobStatus.Running &&
                x.LeaseId == leaseId, ct);
        if (job?.LeaseExpiresAt is not { } leaseExpiry || leaseExpiry <= DateTimeOffset.UtcNow)
            throw Error("GITHUB_SYNC_LEASE_LOST", "The synchronization job lease expired or was replaced.", 409);

        var now = DateTimeOffset.UtcNow;
        job.RetryCount++;
        job.ErrorCode = errorCode;
        job.ErrorMessage = safeErrorMessage;
        job.UpdatedAt = now;
        job.LeaseId = null;
        job.LeaseExpiresAt = null;
        job.LastHeartbeatAt = now;
        if (!retryable || job.RetryCount >= Math.Max(1, options.Value.MaxRetryAttempts))
        {
            job.Status = GitSyncJobStatus.Failed;
            job.CompletedAt = now;
            job.NextAttemptAt = null;
        }
        else
        {
            job.Status = GitSyncJobStatus.RetryScheduled;
            job.CompletedAt = null;
            job.NextAttemptAt = retryAt;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Error("GITHUB_SYNC_LEASE_LOST", "The synchronization job lease expired or was replaced.", 409);
        }
        if (job.CredentialSource == GitRepositoryCredentialSource.GitHubApp)
        {
            if (job.Status == GitSyncJobStatus.RetryScheduled) GitHubAppDiagnostics.JobRetry.Add(1);
            else GitHubAppDiagnostics.JobFailed.Add(1);
            logger?.LogWarning("Git sync job {JobId} changed to {Status} with code {ErrorCode}", job.Id, job.Status, job.ErrorCode);
        }
        return job.Status == GitSyncJobStatus.RetryScheduled;
    }

    public async Task<int> EnqueueDueScheduledJobsAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (!options.Value.Enabled) return 0;
        var candidates = await (
            from association in db.ProjectGitRepositories.AsNoTracking()
            join credential in db.GitHubOAuthConnections.AsNoTracking()
                on association.OAuthConnectionId equals credential.Id
            where association.CredentialSource == GitRepositoryCredentialSource.OAuth && association.UnlinkedAt == null &&
                association.SyncPaused == false &&
                (association.AccessStatus == ProjectGitRepositoryAccessStatus.Active ||
                 association.AccessStatus == ProjectGitRepositoryAccessStatus.PendingAuthorization ||
                 association.AccessStatus == ProjectGitRepositoryAccessStatus.AccessLost) &&
                credential.Status == GitHubOAuthConnectionStatus.Active
            select new
            {
                association.Id,
                association.GitHubRepositoryId,
                OAuthConnectionId = association.OAuthConnectionId!.Value,
                association.LinkedByProfileId,
                association.LinkedAt,
                association.SyncIntervalHours,
                association.AccessStatus,
                association.LastAccessCheckedAt,
                ConnectionUpdatedAt = credential.UpdatedAt
            }).ToListAsync(ct);
        if (candidates.Count == 0) return await EnqueueAppScheduledAsync(now, ct);

        var linkIds = candidates.Select(x => x.Id).ToArray();
        var jobs = await db.GitSyncJobs.AsNoTracking()
            .Where(x => linkIds.Contains(x.ProjectGitRepositoryId))
            .ToListAsync(ct);
        var latestSuccessfulSync = jobs
            .Where(x => x.Status == GitSyncJobStatus.Completed && x.CompletedAt != null)
            .GroupBy(x => x.ProjectGitRepositoryId)
            .ToDictionary(x => x.Key, x => x.Max(j => j.CompletedAt)!.Value);
        var latestJobAt = jobs.GroupBy(x => x.ProjectGitRepositoryId)
            .ToDictionary(x => x.Key, x => x.Max(j => j.UpdatedAt));
        var activeLinks = jobs.Where(x => ActiveStatuses.Contains(x.Status))
            .Select(x => x.ProjectGitRepositoryId)
            .ToHashSet();
        var replacementHistory = await db.ProjectGitRepositoryHistory.AsNoTracking()
            .Where(x => linkIds.Contains(x.ProjectGitRepositoryId) &&
                x.Action == ProjectGitRepositoryHistoryAction.CredentialReplaced)
            .ToListAsync(ct);
        var lastCredentialReplacement = replacementHistory
            .GroupBy(x => x.ProjectGitRepositoryId)
            .ToDictionary(group => group.Key, group => group.Max(x => x.CreatedAt));

        var created = 0;
        foreach (var association in candidates)
        {
            if (activeLinks.Contains(association.Id)) continue;
            var interval = TimeSpan.FromHours(association.SyncIntervalHours);
            var lastJobAt = latestJobAt.GetValueOrDefault(association.Id, DateTimeOffset.MinValue);
            if (association.AccessStatus == ProjectGitRepositoryAccessStatus.Active)
            {
                var replacedAt = lastCredentialReplacement.GetValueOrDefault(association.Id, DateTimeOffset.MinValue);
                if (replacedAt <= lastJobAt)
                {
                    var lastSync = latestSuccessfulSync.GetValueOrDefault(association.Id, association.LinkedAt);
                    if (lastSync + interval > now) continue;
                }
            }
            else
            {
                var lastCheck = association.LastAccessCheckedAt ?? association.LinkedAt;
                if (association.AccessStatus == ProjectGitRepositoryAccessStatus.PendingAuthorization &&
                    association.ConnectionUpdatedAt > lastCheck && lastJobAt < association.ConnectionUpdatedAt)
                {
                    // A reauthorized credential needs an immediate repository-access probe.
                }
                else
                {
                    var recoveryAt = association.AccessStatus == ProjectGitRepositoryAccessStatus.PendingAuthorization
                        ? Max(lastCheck, association.ConnectionUpdatedAt)
                        : lastCheck;
                    var retryFrom = lastJobAt > recoveryAt ? lastJobAt : recoveryAt;
                    if (retryFrom + interval > now) continue;
                }
            }

            var job = new GitSyncJob
            {
                ProjectGitRepositoryId = association.Id,
                GitHubRepositoryId = association.GitHubRepositoryId,
                OAuthConnectionId = association.OAuthConnectionId,
                RequestedByProfileId = association.LinkedByProfileId,
                SyncType = GitSyncType.Scheduled,
                Status = GitSyncJobStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.GitSyncJobs.Add(job);
            try
            {
                await db.SaveChangesAsync(ct);
                created++;
            }
            catch (DbUpdateException ex) when (IsActiveJobConflict(ex))
            {
                db.Entry(job).State = EntityState.Detached;
            }
        }

        return created + await EnqueueAppScheduledAsync(now, ct);
    }

    private async Task<List<AppCandidate>> EligibleAppCandidatesAsync(Guid? projectId, Guid? linkId, CancellationToken ct) => await (
        from link in db.ProjectGitRepositories.AsNoTracking()
        join repo in db.GitHubRepositories.AsNoTracking() on link.GitHubRepositoryId equals repo.Id
        join grant in db.Set<GitHubAppRepositoryGrant>().AsNoTracking() on link.GitHubAppRepositoryGrantId equals grant.Id
        join installation in db.Set<GitHubAppInstallation>().AsNoTracking() on grant.InstallationId equals installation.Id
        where link.CredentialSource == GitRepositoryCredentialSource.GitHubApp && link.OAuthConnectionId == null &&
            link.UnlinkedAt == null && !link.SyncPaused && link.AccessStatus == ProjectGitRepositoryAccessStatus.Active &&
            grant.RevokedAt == null && grant.ProviderRepositoryId == repo.ProviderRepositoryId &&
            installation.Status == GitHubAppInstallationStatus.Active && !installation.VerificationUnknown &&
            (projectId == null || link.ProjectId == projectId) && (linkId == null || link.Id == linkId)
        select new AppCandidate(link.Id, link.GitHubRepositoryId, grant.Id, link.LinkedByProfileId, link.LinkedAt, link.SyncIntervalHours)).ToListAsync(ct);

    private async Task<GitSyncRequestResult> RequestAppManualAsync(Guid accountId, Guid projectId, Guid linkId, Guid actor, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await ProjectGitAuthorization.RequireManagerAsync(db, projectId, accountId, ct);
        var link = (await EligibleAppCandidatesAsync(projectId, linkId, ct)).SingleOrDefault()
            ?? throw Error("GITHUB_APP_PENDING_ACTIVATION", "GitHub App repository access is not ready for synchronization.", 409);
        var existing = await FindActiveJobAsync(link.Id, ct);
        if (existing is not null) return new(await ToResponseAsync(existing, ct), true);
        var job = NewAppJob(link, GitSyncType.Manual, actor, DateTimeOffset.UtcNow);
        db.GitSyncJobs.Add(job);
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateException ex) when (IsActiveJobConflict(ex))
        {
            db.Entry(job).State = EntityState.Detached;
            await transaction.RollbackAsync(ct);
            await transaction.DisposeAsync();
            existing = await FindActiveJobAsync(link.Id, ct);
            if (existing is null) throw;
            return new(await ToResponseAsync(existing, ct), true);
        }
        catch (Exception ex) when (ex.GetBaseException() is PostgresException { SqlState: "40001" or "40P01" })
        { throw Error("GITHUB_SYNC_CONFLICT", "Synchronization state changed concurrently; retry.", 409); }
        GitHubAppDiagnostics.JobEnqueued.Add(1);
        logger?.LogInformation("Enqueued App manual sync job {JobId} for association {AssociationId}", job.Id, link.Id);
        return new(await ToResponseAsync(job, ct), false);
    }

    private async Task<int> EnqueueAppScheduledAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (!AppEnabled) return 0;
        var candidates = await EligibleAppCandidatesAsync(null, null, ct);
        if (candidates.Count == 0) return 0;
        var ids = candidates.Select(x => x.Id).ToArray();
        var jobs = await db.GitSyncJobs.AsNoTracking().Where(x => ids.Contains(x.ProjectGitRepositoryId)).ToListAsync(ct);
        var count = 0;
        foreach (var candidate in candidates)
        {
            var related = jobs.Where(x => x.ProjectGitRepositoryId == candidate.Id).ToArray();
            if (related.Any(x => x.SyncType == GitSyncType.Initial && x.Status == GitSyncJobStatus.Failed) &&
                !related.Any(x => x.SyncType == GitSyncType.Initial && x.Status == GitSyncJobStatus.Completed)) continue;
            if (related.Any(x => ActiveStatuses.Contains(x.Status))) continue;
            var lastAttempt = related.OrderByDescending(x => x.UpdatedAt).FirstOrDefault();
            if (lastAttempt is not null && lastAttempt.Status != GitSyncJobStatus.Completed && lastAttempt.UpdatedAt.AddHours(candidate.SyncIntervalHours) > now) continue;
            var last = related.Where(x => x.Status == GitSyncJobStatus.Completed && x.CompletedAt != null)
                .Select(x => x.CompletedAt!.Value).DefaultIfEmpty(candidate.LinkedAt).Max();
            if (last.AddHours(candidate.SyncIntervalHours) > now) continue;
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if ((await EligibleAppCandidatesAsync(null, candidate.Id, ct)).Count == 0 || await FindActiveJobAsync(candidate.Id, ct) is not null) continue;
            var job = NewAppJob(candidate, GitSyncType.Scheduled, candidate.LinkedByProfileId, now);
            db.GitSyncJobs.Add(job);
            try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); count++; GitHubAppDiagnostics.JobEnqueued.Add(1); logger?.LogInformation("Enqueued App scheduled sync job {JobId} for association {AssociationId}", job.Id, candidate.Id); }
            catch (DbUpdateException ex) when (IsActiveJobConflict(ex)) { db.Entry(job).State = EntityState.Detached; }
            catch (Exception ex) when (ex.GetBaseException() is PostgresException { SqlState: "40001" or "40P01" }) { db.Entry(job).State = EntityState.Detached; }
        }
        return count;
    }

    private static GitSyncJob NewAppJob(AppCandidate link, GitSyncType type, Guid actor, DateTimeOffset now) => new()
    {
        ProjectGitRepositoryId = link.Id, GitHubRepositoryId = link.RepositoryId, OAuthConnectionId = null,
        CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = link.GrantId,
        RequestedByProfileId = actor, SyncType = type, Status = GitSyncJobStatus.Pending, CreatedAt = now, UpdatedAt = now
    };

    // Caller holds the verified installation advisory lock and a serializable transaction.
    internal static async Task<int> EnqueueAppWebhookAsync(UniNetDbContext db, Guid grantId, Guid repositoryId,
        string deliveryId, DateTimeOffset now, CancellationToken ct)
    {
        var links = await db.ProjectGitRepositories.AsNoTracking().Where(x => x.CredentialSource == GitRepositoryCredentialSource.GitHubApp &&
            x.OAuthConnectionId == null && x.GitHubAppRepositoryGrantId == grantId && x.GitHubRepositoryId == repositoryId &&
            x.UnlinkedAt == null && !x.SyncPaused && x.AccessStatus == ProjectGitRepositoryAccessStatus.Active).OrderBy(x => x.Id).ToListAsync(ct);
        var created = 0;
        foreach (var link in links)
        {
            var idempotencyKey = "app-webhook:" + deliveryId;
            var prior = await db.GitSyncJobs.AnyAsync(x => x.ProjectGitRepositoryId == link.Id && x.IdempotencyKey == idempotencyKey, ct);
            if (prior) continue;
            // Serialize coalescing with job claim: a snapshot that saw Pending must not be
            // acknowledged after that job has already started fetching provider pages.
            var activeJobs = await db.GitSyncJobs.FromSqlInterpolated($"""
                SELECT * FROM "GitSyncJobs" WHERE "ProjectGitRepositoryId" = {link.Id}
                    AND "Status" IN ({(short)GitSyncJobStatus.Pending}, {(short)GitSyncJobStatus.Running},
                        {(short)GitSyncJobStatus.RetryScheduled}, {(short)GitSyncJobStatus.Paused})
                FOR UPDATE
                """).ToListAsync(ct);
            var active = activeJobs.Select(x => x.Status).ToList();
            // Pending has not run yet and covers this event. A running or retrying scan needs one follow-up.
            if (active.Contains(GitSyncJobStatus.Pending)) continue;
            if (active.Contains(GitSyncJobStatus.Paused)) throw Error("GITHUB_SYNC_CONFLICT", "Synchronization is temporarily paused.", 409);
            db.GitSyncJobs.Add(new GitSyncJob { ProjectGitRepositoryId = link.Id, GitHubRepositoryId = repositoryId,
                CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grantId,
                OAuthConnectionId = null, RequestedByProfileId = link.LinkedByProfileId, SyncType = GitSyncType.Webhook,
                Status = GitSyncJobStatus.Pending, IdempotencyKey = idempotencyKey, CreatedAt = now, UpdatedAt = now });
            created++;
        }
        return created;
    }
    private sealed record AppCandidate(Guid Id, Guid RepositoryId, Guid GrantId, Guid LinkedByProfileId, DateTimeOffset LinkedAt, int SyncIntervalHours);

    private static DateTimeOffset Max(DateTimeOffset first, DateTimeOffset second) =>
        first >= second ? first : second;

    public async Task<GitSyncRequestResult> RequestManualSyncAsync(
        Guid accountId, Guid projectId, Guid projectGitRepositoryId, CancellationToken ct)
    {
        if (!options.Value.Enabled)
            throw Error("GITHUB_SYNC_DISABLED", "GitHub synchronization is not enabled.", 503);
        var actorProfileId = await ProjectGitAuthorization.RequireManagerAsync(db, projectId, accountId, ct);
        if (await db.ProjectGitRepositories.AsNoTracking().AnyAsync(x => x.Id == projectGitRepositoryId && x.ProjectId == projectId && x.UnlinkedAt == null && x.CredentialSource == GitRepositoryCredentialSource.GitHubApp, ct))
        {
            if (!AppEnabled) throw Error("GITHUB_APP_DISABLED", "GitHub App integration is unavailable.", 503);
            return await RequestAppManualAsync(accountId, projectId, projectGitRepositoryId, actorProfileId, ct);
        }
        var link = await (
            from repositoryLink in db.ProjectGitRepositories.AsNoTracking()
            join repository in db.GitHubRepositories.AsNoTracking()
                on repositoryLink.GitHubRepositoryId equals repository.Id
            join connection in db.GitHubOAuthConnections.AsNoTracking()
                on repositoryLink.OAuthConnectionId equals connection.Id
            where repositoryLink.Id == projectGitRepositoryId &&
                repositoryLink.ProjectId == projectId && repositoryLink.UnlinkedAt == null
            select new
            {
                Link = repositoryLink,
                RepositoryId = repositoryLink.GitHubRepositoryId,
                ConnectionStatus = connection.Status,
                RepositoryName = repository.FullName
            })
            .SingleOrDefaultAsync(ct)
            ?? throw Error("GITHUB_REPOSITORY_LINK_NOT_FOUND", "Linked repository not found.", 404);

        if (link.Link.AccessStatus != ProjectGitRepositoryAccessStatus.Active)
            throw Error("GITHUB_REPOSITORY_NOT_AUTHORIZED", "Repository access must be restored before synchronization.", 409);
        if (link.Link.SyncPaused)
            throw Error("GITHUB_SYNC_PAUSED", "Synchronization is paused for this repository.", 409);
        if (link.ConnectionStatus != GitHubOAuthConnectionStatus.Active)
            throw Error("GITHUB_REAUTH_REQUIRED", "Reconnect the selected GitHub account before synchronization.", 409);

        var existing = await FindActiveJobAsync(projectGitRepositoryId, ct);
        if (existing is not null)
            return new(await ToResponseAsync(existing, ct), true);

        var now = DateTimeOffset.UtcNow;
        var job = new GitSyncJob
        {
            ProjectGitRepositoryId = projectGitRepositoryId,
            GitHubRepositoryId = link.RepositoryId,
            OAuthConnectionId = link.Link.OAuthConnectionId!.Value,
            RequestedByProfileId = actorProfileId,
            SyncType = GitSyncType.Manual,
            Status = GitSyncJobStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.GitSyncJobs.Add(job);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsActiveJobConflict(ex))
        {
            db.Entry(job).State = EntityState.Detached;
            existing = await FindActiveJobAsync(projectGitRepositoryId, ct);
            if (existing is null) throw;
            return new(await ToResponseAsync(existing, ct), true);
        }

        return new(await ToResponseAsync(job, ct), false);
    }

    public async Task<ProjectGitSyncStatusResponse> UpdateSyncSettingsAsync(
        Guid accountId, Guid projectId, Guid projectGitRepositoryId,
        GitSyncSettingsRequest request, CancellationToken ct)
    {
        await ProjectGitAuthorization.RequireManagerAsync(db, projectId, accountId, ct);
        if (request.SyncIntervalHours <= 0)
            throw Error("INVALID_SYNC_INTERVAL", "Sync interval must be greater than zero hours.", 400);

        var link = await db.ProjectGitRepositories
            .SingleOrDefaultAsync(x => x.Id == projectGitRepositoryId &&
                x.ProjectId == projectId && x.UnlinkedAt == null, ct)
            ?? throw Error("GITHUB_REPOSITORY_LINK_NOT_FOUND", "Linked repository not found.", 404);
        if (link.CredentialSource == GitRepositoryCredentialSource.GitHubApp &&
            link.AccessStatus != ProjectGitRepositoryAccessStatus.Active)
            throw Error("GITHUB_APP_PENDING_ACTIVATION", "GitHub App repository access is not ready.", 409);
        if (link.CredentialSource == GitRepositoryCredentialSource.GitHubApp && request.SyncPaused == false &&
            !await (from grant in db.Set<GitHubAppRepositoryGrant>().AsNoTracking()
                    join installation in db.Set<GitHubAppInstallation>().AsNoTracking() on grant.InstallationId equals installation.Id
                    join repository in db.GitHubRepositories.AsNoTracking() on link.GitHubRepositoryId equals repository.Id
                    where grant.Id == link.GitHubAppRepositoryGrantId && grant.RevokedAt == null &&
                        grant.ProviderRepositoryId == repository.ProviderRepositoryId && installation.Status == GitHubAppInstallationStatus.Active && !installation.VerificationUnknown
                    select grant.Id).AnyAsync(ct))
            throw Error("GITHUB_APP_PENDING_ACTIVATION", "GitHub App repository access is not ready.", 409);
        link.SyncIntervalHours = request.SyncIntervalHours;
        if (request.SyncPaused is { } paused) link.SyncPaused = paused;
        link.UpdatedAt = DateTimeOffset.UtcNow;
        link.Version = Guid.NewGuid();

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Error("GITHUB_SYNC_SETTINGS_CONFLICT", "Repository sync settings changed concurrently. Reload and retry.", 409);
        }

        return (await GetProjectStatusAsync(accountId, projectId, ct)).Items
            .Single(x => x.ProjectGitRepositoryId == projectGitRepositoryId);
    }

    public async Task<GitSyncJobPage> GetJobsAsync(
        Guid accountId, Guid projectId, Guid projectGitRepositoryId,
        int page, int pageSize, CancellationToken ct)
    {
        await ProjectGitAuthorization.RequireProjectViewerAsync(db, projectId, accountId, ct);
        ValidatePagination(page, pageSize);
        var associationExists = await db.ProjectGitRepositories.AsNoTracking()
            .AnyAsync(x => x.Id == projectGitRepositoryId && x.ProjectId == projectId, ct);
        if (!associationExists)
            throw Error("GITHUB_REPOSITORY_LINK_NOT_FOUND", "Repository integration not found.", 404);

        var query = db.GitSyncJobs.AsNoTracking()
            .Where(x => x.ProjectGitRepositoryId == projectGitRepositoryId);
        var total = await query.CountAsync(ct);
        List<GitSyncJob> jobs;
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            jobs = (await query.ToListAsync(ct))
                .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToList();
        }
        else
        {
            jobs = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        }
        return new(await ToResponsesAsync(jobs, ct), page, pageSize, total);
    }

    public async Task<ProjectGitSyncStatusPage> GetProjectStatusAsync(
        Guid accountId, Guid projectId, CancellationToken ct)
    {
        await ProjectGitAuthorization.RequireProjectViewerAsync(db, projectId, accountId, ct);
        var links = await (
            from repositoryLink in db.ProjectGitRepositories.AsNoTracking()
            join repository in db.GitHubRepositories.AsNoTracking()
                on repositoryLink.GitHubRepositoryId equals repository.Id
            join connection in db.GitHubOAuthConnections.AsNoTracking()
                on repositoryLink.OAuthConnectionId equals connection.Id into connections
            from connection in connections.DefaultIfEmpty()
            where repositoryLink.ProjectId == projectId && repositoryLink.UnlinkedAt == null
            select new
            {
                repositoryLink.Id,
                repositoryLink.GitHubRepositoryId,
                RepositoryFullName = repository.FullName,
                repositoryLink.AccessStatus,
                repositoryLink.SyncPaused,
                repositoryLink.SyncIntervalHours,
                repositoryLink.LinkedAt,
                repositoryLink.CredentialSource,
                AppVerificationUnknown = (from grant in db.Set<GitHubAppRepositoryGrant>()
                    join installation in db.Set<GitHubAppInstallation>() on grant.InstallationId equals installation.Id
                    where grant.Id == repositoryLink.GitHubAppRepositoryGrantId && installation.VerificationUnknown
                    select grant.Id).Any(),
                AppGrantReady = repositoryLink.CredentialSource != GitRepositoryCredentialSource.GitHubApp ||
                    (from grant in db.Set<GitHubAppRepositoryGrant>()
                     join installation in db.Set<GitHubAppInstallation>() on grant.InstallationId equals installation.Id
                     where grant.Id == repositoryLink.GitHubAppRepositoryGrantId && grant.RevokedAt == null &&
                         grant.ProviderRepositoryId == repository.ProviderRepositoryId && installation.Status == GitHubAppInstallationStatus.Active && !installation.VerificationUnknown
                     select grant.Id).Any(),
                ConnectionStatus = connection == null ? (GitHubOAuthConnectionStatus?)null : connection.Status
            })
            .OrderBy(x => x.RepositoryFullName)
            .ToListAsync(ct);
        if (links.Count == 0) return new([]);

        var linkIds = links.Select(x => x.Id).ToArray();
        var jobs = await db.GitSyncJobs.AsNoTracking()
            .Where(x => linkIds.Contains(x.ProjectGitRepositoryId))
            .ToListAsync(ct);
        jobs = jobs.OrderByDescending(x => x.CreatedAt).ToList();
        var latestJobs = jobs.GroupBy(x => x.ProjectGitRepositoryId)
            .ToDictionary(x => x.Key, x => x.First());
        var successTimes = jobs.Where(x => x.Status == GitSyncJobStatus.Completed && x.CompletedAt != null)
            .GroupBy(x => x.ProjectGitRepositoryId)
            .ToDictionary(x => x.Key, x => x.Max(j => j.CompletedAt));

        var response = new List<ProjectGitSyncStatusResponse>(links.Count);
        foreach (var link in links)
        {
            latestJobs.TryGetValue(link.Id, out var latest);
            response.Add(new(
                link.Id,
                link.GitHubRepositoryId,
                link.RepositoryFullName,
                link.AccessStatus,
                link.SyncPaused,
                link.SyncIntervalHours,
                link.CredentialSource == GitRepositoryCredentialSource.GitHubApp ?
                    link.AccessStatus != ProjectGitRepositoryAccessStatus.Active || !link.AppGrantReady : link.ConnectionStatus != GitHubOAuthConnectionStatus.Active,
                successTimes.GetValueOrDefault(link.Id),
                link.CredentialSource == GitRepositoryCredentialSource.GitHubApp && (link.AccessStatus != ProjectGitRepositoryAccessStatus.Active || link.SyncPaused || !link.AppGrantReady) ? null :
                    (successTimes.GetValueOrDefault(link.Id) ?? link.LinkedAt).AddHours(link.SyncIntervalHours),
                latest is null ? null : await ToResponseAsync(latest, ct))
            {
                CredentialSource = link.CredentialSource,
                SyncReadiness = link.CredentialSource == GitRepositoryCredentialSource.GitHubApp ?
                    (link.AppVerificationUnknown ? "VerificationPending" : link.AccessStatus == ProjectGitRepositoryAccessStatus.AccessLost ? "AccessLost" :
                     link.AccessStatus != ProjectGitRepositoryAccessStatus.Active ? "PendingActivation" : !link.AppGrantReady ? "AccessLost" :
                     link.SyncPaused ? "Paused" : latest?.Status switch
                    {
                        GitSyncJobStatus.Running => "Syncing",
                        GitSyncJobStatus.Completed => "SyncReady",
                        GitSyncJobStatus.Failed => "SyncFailed",
                        _ => "PendingWorker"
                    }) :
                    link.AccessStatus == ProjectGitRepositoryAccessStatus.AccessLost ? "AccessLost" :
                    link.SyncPaused ? "Paused" :
                    link.AccessStatus == ProjectGitRepositoryAccessStatus.Active && link.ConnectionStatus == GitHubOAuthConnectionStatus.Active ? "SyncReady" : "PendingAuthorization"
            });
        }

        return new(response);
    }

    private Task<GitSyncJob?> FindActiveJobAsync(Guid projectGitRepositoryId, CancellationToken ct) =>
        db.GitSyncJobs.AsNoTracking()
            .Where(x => x.ProjectGitRepositoryId == projectGitRepositoryId && ActiveStatuses.Contains(x.Status))
            .FirstOrDefaultAsync(ct);

    private async Task<IReadOnlyList<GitSyncJobResponse>> ToResponsesAsync(
        IReadOnlyList<GitSyncJob> jobs, CancellationToken ct)
    {
        var jobIds = jobs.Select(x => x.Id).ToArray();
        var checkpoints = await db.GitSyncCheckpoints.AsNoTracking()
            .Where(x => jobIds.Contains(x.SyncJobId))
            .OrderBy(x => x.Category).ThenBy(x => x.ScopeKey)
            .ToListAsync(ct);
        var byJob = checkpoints.GroupBy(x => x.SyncJobId).ToDictionary(x => x.Key, x => x.ToArray());
        return jobs.Select(job => Map(job, byJob.GetValueOrDefault(job.Id) ?? [])).ToArray();
    }

    private async Task<GitSyncJobResponse> ToResponseAsync(GitSyncJob job, CancellationToken ct)
    {
        var checkpoints = await db.GitSyncCheckpoints.AsNoTracking()
            .Where(x => x.SyncJobId == job.Id)
            .OrderBy(x => x.Category).ThenBy(x => x.ScopeKey)
            .ToArrayAsync(ct);
        return Map(job, checkpoints);
    }

    private static GitSyncJobResponse Map(GitSyncJob job, IReadOnlyList<GitSyncCheckpoint> checkpoints) =>
        new(job.Id, job.ProjectGitRepositoryId, job.SyncType, job.Status, job.CreatedAt,
            job.StartedAt, job.CompletedAt, job.NextAttemptAt, job.RetryCount,
            job.ErrorCode, job.ErrorMessage,
            checkpoints.Select(x => new GitSyncCheckpointResponse(
                x.Category, x.ScopeKey, x.Cursor, (int)x.Status, x.ItemsProcessed, x.LastProcessedAt)).ToArray());

    private static bool IsActiveJobConflict(DbUpdateException exception) =>
        exception.GetBaseException() is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_GitSyncJobs_ProjectGitRepositoryId_Status"
        };

    private static void ValidatePagination(int page, int pageSize)
    {
        if (page is < 1 or > 1000 || pageSize is < 1 or > 100)
            throw Error("INVALID_PAGINATION", "Page must be between 1 and 1000 and page size between 1 and 100.", 400);
    }

    private static GitHubIntegrationException Error(string code, string message, int status) =>
        new(code, message, status);
}
