using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniNet.Application.Common;
using UniNet.Application.DTOs;
using UniNet.Application.Interfaces;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace UniNet.Application.Services;

public sealed class GitSyncJobService(UniNetDbContext db, IOptions<GitSyncOptions> options) : IGitSyncJobService
{
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
                INNER JOIN "GitHubOAuthConnections" AS credential
                    ON credential."Id" = association."OAuthConnectionId"
                WHERE candidate."OAuthConnectionId" = association."OAuthConnectionId"
                    AND association."UnlinkedAt" IS NULL
                    AND association."SyncPaused" = FALSE
                    AND association."AccessStatus" = {(short)ProjectGitRepositoryAccessStatus.Active}
                    AND credential."Status" = {(short)GitHubOAuthConnectionStatus.Active}
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
            """).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return jobs.SingleOrDefault();
    }

    public async Task CompleteAsync(Guid jobId, Guid leaseId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var updated = await db.GitSyncJobs
            .Where(x => x.Id == jobId && x.Status == GitSyncJobStatus.Running && x.LeaseId == leaseId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, GitSyncJobStatus.Completed)
                .SetProperty(x => x.CompletedAt, now)
                .SetProperty(x => x.LeaseId, (Guid?)null)
                .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(x => x.LastHeartbeatAt, now)
                .SetProperty(x => x.UpdatedAt, now), ct);
        if (updated != 1)
            throw Error("GITHUB_SYNC_LEASE_LOST", "The synchronization job lease expired or was replaced.", 409);
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

        var job = await db.GitSyncJobs
            .SingleOrDefaultAsync(x => x.Id == jobId && x.Status == GitSyncJobStatus.Running &&
                x.LeaseId == leaseId, ct);
        if (job is null)
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
        return job.Status == GitSyncJobStatus.RetryScheduled;
    }

    public async Task<int> EnqueueDueScheduledJobsAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (!options.Value.Enabled) return 0;
        var candidates = await (
            from association in db.ProjectGitRepositories.AsNoTracking()
            join credential in db.GitHubOAuthConnections.AsNoTracking()
                on association.OAuthConnectionId equals credential.Id
            where association.UnlinkedAt == null &&
                association.SyncPaused == false &&
                (association.AccessStatus == ProjectGitRepositoryAccessStatus.Active ||
                 association.AccessStatus == ProjectGitRepositoryAccessStatus.PendingAuthorization ||
                 association.AccessStatus == ProjectGitRepositoryAccessStatus.AccessLost) &&
                credential.Status == GitHubOAuthConnectionStatus.Active
            select new
            {
                association.Id,
                association.GitHubRepositoryId,
                association.OAuthConnectionId,
                association.LinkedByProfileId,
                association.LinkedAt,
                association.SyncIntervalHours,
                association.AccessStatus,
                association.LastAccessCheckedAt,
                ConnectionUpdatedAt = credential.UpdatedAt
            }).ToListAsync(ct);
        if (candidates.Count == 0) return 0;

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

        return created;
    }

    private static DateTimeOffset Max(DateTimeOffset first, DateTimeOffset second) =>
        first >= second ? first : second;

    public async Task<GitSyncRequestResult> RequestManualSyncAsync(
        Guid accountId, Guid projectId, Guid projectGitRepositoryId, CancellationToken ct)
    {
        if (!options.Value.Enabled)
            throw Error("GITHUB_SYNC_DISABLED", "GitHub synchronization is not enabled.", 503);
        var actorProfileId = await ProjectGitAuthorization.RequireManagerAsync(db, projectId, accountId, ct);
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
            OAuthConnectionId = link.Link.OAuthConnectionId,
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
                on repositoryLink.OAuthConnectionId equals connection.Id
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
                ConnectionStatus = connection.Status
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
                link.ConnectionStatus != GitHubOAuthConnectionStatus.Active,
                successTimes.GetValueOrDefault(link.Id),
                (successTimes.GetValueOrDefault(link.Id) ?? link.LinkedAt)
                    .AddHours(link.SyncIntervalHours),
                latest is null ? null : await ToResponseAsync(latest, ct)));
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
