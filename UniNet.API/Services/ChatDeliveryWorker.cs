using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniNet.API.Hubs;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;

namespace UniNet.API.Services;

public sealed class ChatDeliveryWorker(IServiceScopeFactory scopes, ILogger<ChatDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested) {
            try { for (var count = 0; count < 50 && await ProcessOne(stoppingToken); count++) { } }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning(error, "Chat delivery failed; pending deliveries remain durable."); }
            await Task.Delay(750, stoppingToken);
        }
    }
    public async Task<bool> ProcessOne(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
        var chat = scope.ServiceProvider.GetRequiredService<MessagingService>(); var push = scope.ServiceProvider.GetRequiredService<IChatPushSender>();
        var now = DateTimeOffset.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Claim one delivery across all worker instances. Network failure never rolls back a saved chat message.
        var delivery = db.Database.IsNpgsql()
            ? await db.Set<NotificationDelivery>().FromSqlInterpolated($"SELECT * FROM \"NotificationDeliveries\" WHERE \"Reason\" LIKE 'chat.%' AND \"Reason\" <> 'chat.push.failed' AND \"DeliveredAt\" IS NULL AND (\"RetryAt\" IS NULL OR \"RetryAt\" <= {now}) ORDER BY \"CreatedAt\" LIMIT 1 FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(ct)
            : await db.Set<NotificationDelivery>().Where(d => d.Reason.StartsWith("chat.") && d.Reason != "chat.push.failed" && d.DeliveredAt == null && (d.RetryAt == null || d.RetryAt <= now)).OrderBy(d => d.CreatedAt).FirstOrDefaultAsync(ct);
        if (delivery == null) { await tx.CommitAsync(ct); return false; }
        var previousAttempts = delivery.Attempts; delivery.Attempts++; delivery.RetryAt = now.AddMinutes(1); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        try {
            var active = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == delivery.AccountId && a.Status == AccountStatus.Active, ct);
            if (active != null && delivery.ConversationId.HasValue) {
                var kind = delivery.Reason == "chat.push" ? "message" : delivery.Reason[5..];
                var update = await chat.EventFor(delivery.AccountId, delivery.ConversationId.Value, delivery.MessageId, kind, ct);
                if (delivery.Reason == "chat.push") {
                    var device = await db.PushDevices.SingleOrDefaultAsync(d => d.Id == delivery.PushDeviceId && d.IsActive, ct);
                    // Re-read acknowledgement after claiming: a foreground client may have processed this message in the meantime.
                    var acknowledged = await db.Set<NotificationDelivery>().AsNoTracking().AnyAsync(d => d.Id == delivery.Id && d.DeliveredAt != null, ct);
                    if (!acknowledged && device != null && device.AccountId == active.Id && device.SessionVersion == active.SessionVersion && update.Notify && update.Notification != null) {
                        var readNumber = await db.ConversationMembers.Where(m => m.ConversationId == delivery.ConversationId && m.AccountId == active.Id).Select(m => db.Messages.Where(last => last.Id == m.LastReadMessageId).Select(last => (long?)last.Number).FirstOrDefault() ?? 0).SingleAsync(ct);
                        // A delayed push is obsolete once the message has been read on any device.
                        if (update.Message != null && readNumber < update.Message.Number) {
                            if (!push.IsConfigured) { delivery.Attempts = previousAttempts; delivery.RetryAt = now.AddMinutes(5); await db.SaveChangesAsync(ct); return true; }
                            var result = await push.Send(device.Token, device.Platform, device.ShowPreview, update.Notification, ct);
                            if (result.InvalidToken) { device.IsActive = false; device.UpdatedAt = now; }
                            if (!result.Success && !result.InvalidToken) {
                                if (!result.Retryable || delivery.Attempts >= 12) delivery.Reason = "chat.push.failed";
                                delivery.RetryAt = now.AddSeconds(Math.Min(900, Math.Pow(2, Math.Min(delivery.Attempts + 1, 9))));
                                await db.SaveChangesAsync(ct); return true;
                            }
                        }
                    }
                } else await scope.ServiceProvider.GetRequiredService<IHubContext<MessagingHub>>().Clients.User(delivery.AccountId.ToString()).SendAsync("ChatChanged", update, ct);
            }
            delivery.DeliveredAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) {
            delivery.RetryAt = now.AddSeconds(Math.Min(600, Math.Pow(2, Math.Min(delivery.Attempts, 9)))); await db.SaveChangesAsync(ct);
            logger.LogWarning(error, "Chat delivery {DeliveryId} will retry.", delivery.Id);
        }
        return true;
    }
}
