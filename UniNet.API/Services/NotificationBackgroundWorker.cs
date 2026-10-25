using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniNet.API.Hubs;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;

namespace UniNet.API.Services;

public sealed class NotificationBackgroundWorker(IServiceScopeFactory scopes, ILogger<NotificationBackgroundWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                await using (var scope = scopes.CreateAsyncScope()) {
                    var broadcasts = scope.ServiceProvider.GetRequiredService<NotificationBroadcastService>();
                    await broadcasts.ProcessNextBatchAsync(stoppingToken);
                }
                await DeliverAsync(stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (NotificationBroadcastProcessingException exception) {
                logger.LogWarning(exception, "Notification broadcast batch failed; retry scheduled.");
                await RetryBroadcastAsync(exception.BroadcastId, stoppingToken);
            }
            catch (Exception exception) {
                logger.LogWarning(exception, "Notification worker failed; persisted work will be retried.");
            }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task RetryBroadcastAsync(Guid broadcastId, CancellationToken ct)
    {
        try {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
            var job = await db.Set<NotificationBroadcast>().SingleOrDefaultAsync(j => j.Id == broadcastId && (j.Status == "Queued" || j.Status == "Processing"), ct);
            if (job == null) return;
            job.Attempts++; job.LastError = "Gửi thông báo tạm thời thất bại; hệ thống sẽ thử lại.";
            job.RetryAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(job.Attempts, 8))));
            if (job.Attempts >= 10) job.Status = "Failed";
            await db.SaveChangesAsync(ct);
        } catch (Exception error) { logger.LogWarning(error, "Could not update notification retry state."); }
    }

    private async Task DeliverAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
        var hub = scope.ServiceProvider.GetRequiredService<IHubContext<NotificationsHub>>();
        var now = DateTimeOffset.UtcNow;
        var deliveries = await db.Set<NotificationDelivery>().Where(d => d.DeliveredAt == null && (d.RetryAt == null || d.RetryAt <= now))
            .OrderBy(d => d.CreatedAt).Take(250).ToListAsync(ct);
        foreach (var delivery in deliveries) {
            try {
                // Only an invalidation is sent. API rechecks current permission/active status before returning content.
                if (await db.Accounts.AnyAsync(a => a.Id == delivery.AccountId && a.Status == AccountStatus.Active, ct))
                    await hub.Clients.User(delivery.AccountId.ToString()).SendAsync("NotificationChanged", new { delivery.NotificationId, delivery.Reason }, ct);
                delivery.DeliveredAt = now;
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error) {
                delivery.Attempts++; delivery.RetryAt = now.AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(delivery.Attempts, 8))));
                logger.LogWarning(error, "Notification invalidation delivery failed; retry scheduled.");
            }
        }
        await db.SaveChangesAsync(ct);
        // Bound operational outbox growth; recipient history remains in Notifications.
        await db.Set<NotificationDelivery>().Where(d => d.DeliveredAt < now.AddDays(-7)).ExecuteDeleteAsync(ct);
    }
}
