using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Infrastructure.Data;

namespace UniNet.API.Services;

public sealed class GitSyncWorker(
    IServiceScopeFactory scopes,
    IOptions<GitSyncOptions> options,
    ILogger<GitSyncWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return Task.CompletedTask;
        var concurrency = Math.Clamp(options.Value.WorkerConcurrency, 1, 16);
        return Task.WhenAll(Enumerable.Range(0, concurrency)
            .Select(_ => RunWorkerAsync(stoppingToken)));
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
                if (claimed is null)
                {
                    await jobs.EnqueueDueScheduledJobsAsync(DateTimeOffset.UtcNow, ct);
                    await Task.Delay(pollDelay, ct);
                    continue;
                }

                var leaseId = claimed.LeaseId
                    ?? throw new InvalidOperationException("Claimed Git sync job has no lease.");
                var processor = scope.ServiceProvider.GetRequiredService<IGitSyncJobProcessor>();
                try
                {
                    await ProcessWithHeartbeatAsync(processor, claimed, leaseId, leaseDuration, ct);
                    await jobs.CompleteAsync(claimed.Id, leaseId, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (GitHubIntegrationException exception)
                {
                    await ScheduleFailureAsync(jobs, claimed.Id, leaseId,
                        claimed.RetryCount, exception.Code, exception.Message, exception.RetryAfter,
                        exception.Code is not ("GITHUB_REPOSITORY_ACCESS_LOST" or
                            "GITHUB_REAUTH_REQUIRED" or "GITHUB_SYNC_ASSOCIATION_CHANGED" or
                            "GITHUB_REPOSITORY_NOT_AUTHORIZED"), ct);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Git sync job {JobId} failed.", claimed.Id);
                    await ScheduleFailureAsync(jobs, claimed.Id, leaseId, claimed.RetryCount,
                        "GITHUB_SYNC_INTERNAL", "Synchronization failed due to an internal processing error.",
                        null, true, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Git synchronization worker loop failed; durable jobs remain queued.");
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
