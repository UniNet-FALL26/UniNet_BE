using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Infrastructure.Data;

namespace UniNet.API.Services;

public sealed class GitSyncWorker(
    IServiceScopeFactory scopes,
    IOptions<GitSyncOptions> options,
    ILogger<GitSyncWorker> logger,
    IOptions<GitHubAppOptions>? appOptions = null,
    GitHubWorkerProgress? progress = null) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return Task.CompletedTask;
        var concurrency = Math.Clamp(options.Value.WorkerConcurrency, 1, 16);
        var workers = Enumerable.Range(0, concurrency).Select(_ => RunWorkerAsync(stoppingToken));
        if (appOptions?.Value.Enabled == true)
        {
            workers = workers.Append(RunReconciliationAsync(stoppingToken)).Append(RunWebhookAsync(stoppingToken));
            if (progress is not null) workers = workers.Append(RunDurableHeartbeatAsync(stoppingToken));
        }
        return Task.WhenAll(workers);
    }

    private async Task RunDurableHeartbeatAsync(CancellationToken ct)
    {
        var nextCleanup = DateTimeOffset.UtcNow.AddHours(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
                var current = progress!.Snapshot(); var now = DateTimeOffset.UtcNow;
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "GitHubAppWorkerHeartbeats" ("Id", "UpdatedAt", "WorkerAt", "ConsumerAt", "ReconciliationAt")
                    VALUES ({progress.InstanceId}, {now}, {current.Worker}, {current.Consumer}, {current.Reconciliation})
                    ON CONFLICT ("Id") DO UPDATE SET "UpdatedAt" = EXCLUDED."UpdatedAt", "WorkerAt" = EXCLUDED."WorkerAt",
                        "ConsumerAt" = EXCLUDED."ConsumerAt", "ReconciliationAt" = EXCLUDED."ReconciliationAt"
                    """, ct);
                if (now >= nextCleanup)
                {
                    await db.Set<UniNet.Domain.Entities.GitHubAppWorkerHeartbeat>()
                        .Where(x => x.UpdatedAt < now.AddDays(-7)).ExecuteDeleteAsync(ct);
                    nextCleanup = now.AddHours(1);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogWarning("GitHub worker heartbeat persistence failed."); }
            try { await Task.Delay(TimeSpan.FromSeconds(15), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        }
    }

    private async Task RunWebhookAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
                if (!db.Database.IsNpgsql()) return;
                var consumer = scope.ServiceProvider.GetService<GitHubAppWebhookConsumer>();
                var processed = consumer is not null && await consumer.ProcessNextAsync(ct);
                progress?.Completed("consumer");
                if (processed) continue;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogWarning("GitHub App webhook consumption deferred; delivery state remains durable."); }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.Value.PollSeconds, 1, 300)), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        }
    }

    // Managed alongside job loops in the existing hosted worker, so long-running jobs cannot starve polling.
    private async Task RunReconciliationAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
                if (!db.Database.IsNpgsql()) return;
                await scope.ServiceProvider.GetRequiredService<GitHubAppInstallationReconciliationService>().ReconcileAsync(ct);
                await scope.ServiceProvider.GetRequiredService<GitHubAppRepositoryActivationService>().RetryPendingAsync(ct);
                progress?.Completed("reconciliation");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogWarning("GitHub App reconciliation deferred; durable state will be retried."); }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.Value.ReconciliationPollSeconds, 1, 300)), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        }
    }

    private async Task RunWorkerAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var pollDelay = TimeSpan.FromSeconds(Math.Clamp(settings.PollSeconds, 1, 300));
        var leaseDuration = TimeSpan.FromSeconds(Math.Clamp(settings.JobLeaseDurationSeconds, 30, 3600));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
                if (!db.Database.IsNpgsql())
                {
                    logger.LogError("Git synchronization worker requires PostgreSQL and will remain idle.");
                    return;
                }

                var jobs = scope.ServiceProvider.GetRequiredService<IGitSyncJobService>();
                var claimed = await jobs.ClaimNextAsync(leaseDuration, ct);
                progress?.Completed("worker");
                if (claimed is null)
                {
                    await jobs.EnqueueDueScheduledJobsAsync(DateTimeOffset.UtcNow, ct);
                    await Task.Delay(pollDelay, ct);
                    continue;
                }

                var leaseId = claimed.LeaseId
                    ?? throw new InvalidOperationException("Claimed Git sync job has no lease.");
                logger.LogInformation("Claimed Git sync job {JobId} for repository association {AssociationId} source {CredentialSource}",
                    claimed.Id, claimed.ProjectGitRepositoryId, claimed.CredentialSource);
                var processor = scope.ServiceProvider.GetRequiredService<IGitSyncJobProcessor>();
                try
                {
                    await ProcessWithHeartbeatAsync(processor, claimed, leaseId, leaseDuration, ct);
                    await jobs.CompleteAsync(claimed.Id, leaseId, ct);
                    logger.LogInformation("Completed Git sync job {JobId}", claimed.Id);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (GitHubIntegrationException exception)
                {
                    logger.LogWarning("Git sync job {JobId} deferred with code {ErrorCode}", claimed.Id, exception.Code);
                    await ScheduleFailureAsync(jobs, claimed.Id, leaseId,
                        claimed.RetryCount, exception.Code, exception.Message, exception.RetryAfter,
                        exception.Code is not ("GITHUB_REPOSITORY_ACCESS_LOST" or
                            "GITHUB_REAUTH_REQUIRED" or "GITHUB_SYNC_ASSOCIATION_CHANGED" or
                            "GITHUB_REPOSITORY_NOT_AUTHORIZED" or "GITHUB_SYNC_EVIDENCE_SCHEMA_REQUIRED" or
                            "GITHUB_EVIDENCE_HASH_CONFLICT"), ct);
                }
                catch (Exception exception) when (exception.GetBaseException() is PostgresException
                    { SqlState: "23503", ConstraintName: { } constraint } &&
                    (constraint.StartsWith("FK_ProjectEvidenceObservations_", StringComparison.Ordinal) ||
                     constraint.StartsWith("FK_GitEvidenceVersions_", StringComparison.Ordinal)))
                {
                    logger.LogError("Git sync job {JobId} failed evidence integrity validation.", claimed.Id);
                    await ScheduleFailureAsync(jobs, claimed.Id, leaseId, claimed.RetryCount,
                        "GITHUB_SYNC_EVIDENCE_INTEGRITY", "Synchronization failed evidence integrity validation.",
                        null, false, ct);
                }
                catch (Exception)
                {
                    logger.LogError("Git sync job {JobId} failed with an internal error.", claimed.Id);
                    await ScheduleFailureAsync(jobs, claimed.Id, leaseId, claimed.RetryCount,
                        "GITHUB_SYNC_INTERNAL", "Synchronization failed due to an internal processing error.",
                        null, true, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                logger.LogError("Git synchronization worker loop failed; durable jobs remain queued.");
                await Task.Delay(pollDelay, ct);
            }
        }
    }

    private async Task ProcessWithHeartbeatAsync(
        IGitSyncJobProcessor processor,
        UniNet.Domain.Entities.GitSyncJob job,
        Guid leaseId,
        TimeSpan leaseDuration,
        CancellationToken ct)
    {
        using var processingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var processing = processor.ProcessAsync(job, processingCts.Token);
        var heartbeatDelay = TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(1).Ticks, leaseDuration.Ticks / 3));
        try
        {
            while (await Task.WhenAny(processing, Task.Delay(heartbeatDelay, ct)) != processing)
            {
                await using var heartbeatScope = scopes.CreateAsyncScope();
                var heartbeatJobs = heartbeatScope.ServiceProvider.GetRequiredService<IGitSyncJobService>();
                if (!await heartbeatJobs.RenewLeaseAsync(job.Id, leaseId, leaseDuration, ct))
                    throw new GitHubIntegrationException(
                        "GITHUB_SYNC_LEASE_LOST", "The synchronization job lease expired or was replaced.", 409);
                progress?.Completed("worker");
            }
            await processing;
        }
        catch
        {
            processingCts.Cancel();
            try
            {
                await processing;
            }
            catch (OperationCanceledException) when (processingCts.IsCancellationRequested)
            {
            }
            throw;
        }
    }

    private async Task ScheduleFailureAsync(
        IGitSyncJobService jobs,
        Guid jobId,
        Guid leaseId,
        int retryCount,
        string errorCode,
        string safeMessage,
        TimeSpan? retryAfter,
        bool retryable,
        CancellationToken ct)
    {
        var baseDelay = Math.Clamp(options.Value.RetryBaseDelaySeconds, 1, 1800);
        var exponentialSeconds = Math.Min(3600, baseDelay * Math.Pow(2, Math.Clamp(retryCount, 0, 8)));
        var jitter = Random.Shared.NextDouble() * Math.Max(1, exponentialSeconds * 0.2);
        var delay = retryAfter is { } providerDelay && providerDelay > TimeSpan.Zero
            ? TimeSpan.FromSeconds(Math.Clamp(providerDelay.TotalSeconds, 1, 86400))
            : TimeSpan.FromSeconds(exponentialSeconds + jitter);
        await jobs.FailAsync(jobId, leaseId, errorCode, safeMessage,
            DateTimeOffset.UtcNow.Add(delay), ct, retryable);
    }
}
