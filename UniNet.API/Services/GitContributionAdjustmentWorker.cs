using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniNet.Application.Services;
using UniNet.Application.Interfaces;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
namespace UniNet.API.Services;
// Scheduling only. Actual claim, fencing, heartbeat, page persistence and retry use GitSyncWorker unchanged.
public sealed class GitContributionAdjustmentWorker(IServiceScopeFactory scopes, IOptions<GitSyncOptions> options, ILoggerFactory logs) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.AdjustmentExecutionEnabled || options.Value.Enabled || options.Value.ScopedManual.Enabled) return;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>(); var now = DateTimeOffset.UtcNow;
                var candidate = await (from item in db.Set<GitContributionAdjustmentRepository>().AsNoTracking()
                                       join plan in db.Set<GitContributionAdjustment>().AsNoTracking() on item.AdjustmentId equals plan.Id
                                       join job in db.GitSyncJobs.AsNoTracking() on item.JobId equals job.Id
                                       where plan.State == "Syncing" && plan.ExecutionDeadline > now && job.SyncType == GitSyncType.Manual &&
                                           job.ProjectGitRepositoryId == item.AssociationId && job.GitHubRepositoryId == item.RepositoryId &&
                                           (job.Status == GitSyncJobStatus.Pending || job.Status == GitSyncJobStatus.Running || job.Status == GitSyncJobStatus.RetryScheduled)
                                       orderby plan.CreatedAt, item.AssociationId
                                       select new { item, plan.ExecutionDeadline }).FirstOrDefaultAsync(ct);
                if (candidate is not null)
                {
                    var pinned = GitContributionAdjustmentService.Scope(candidate.item.ProjectId, candidate.item.AssociationId, candidate.item.JobId, options.Value, (int)Math.Max(1, (candidate.ExecutionDeadline - now).TotalSeconds));
                    await RunPinnedAsync(scopes, pinned, logs, ct); continue;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception) { logs.CreateLogger<GitContributionAdjustmentWorker>().LogWarning("Combined Git adjustment deferred; inspect its durable job state."); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        }
    }
    // Public for isolated integration tests; this is not an HTTP endpoint or an unfenced processor call.
    public static async Task<string?> RunPinnedAsync(IServiceScopeFactory root, GitSyncOptions pinned, ILoggerFactory logs, CancellationToken ct)
    {
        await using var metadata = root.CreateAsyncScope(); var app = metadata.ServiceProvider.GetService<IOptions<GitHubAppOptions>>();
        using var worker = new GitSyncWorker(new PinnedScopes(root, pinned), Options.Create(pinned), logs.CreateLogger<GitSyncWorker>(), app);
        await worker.StartAsync(ct); if (worker.ExecuteTask is { } task) await task; await worker.StopAsync(CancellationToken.None); return worker.ScopedStopReason;
    }
    private sealed class PinnedScopes(IServiceScopeFactory root, GitSyncOptions pinned) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new PinnedScope(root.CreateScope(), pinned);
    }
    private sealed class PinnedScope(IServiceScope inner, GitSyncOptions pinned) : IServiceScope, IServiceProvider, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider => this;
        public object? GetService(Type type) => type == typeof(IGitSyncJobService) ? new GitSyncJobService(inner.ServiceProvider.GetRequiredService<UniNetDbContext>(), Options.Create(pinned), inner.ServiceProvider.GetService<IOptions<GitHubAppOptions>>()) : inner.ServiceProvider.GetService(type);
        public void Dispose() => inner.Dispose();
        public async ValueTask DisposeAsync() { if (inner is IAsyncDisposable a) await a.DisposeAsync(); else inner.Dispose(); }
    }
}
