using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UniNet.Application.Common;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class GitHubAppWebhookRecoveryService(UniNetDbContext db, IOptions<GitHubAppOptions> appOptions,
    ILogger<GitHubAppWebhookRecoveryService> logger)
{
    private const int MaxOperatorRequeues = 3;

    public async Task RequeueAsync(Guid actorId, string deliveryId, string reason, CancellationToken ct)
    {
        GitHubAppAvailability.Require(appOptions);
        if (!db.Database.IsNpgsql()) throw new NotSupportedException("Delivery recovery requires PostgreSQL row locks.");
        if (!Guid.TryParse(deliveryId, out var parsed) || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 300)
            throw new GitHubIntegrationException("GITHUB_APP_REQUEUE_INVALID", "A valid delivery identifier and reason are required.", 400);
        var normalized = parsed.ToString("D");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Prevent a concurrent account-role change from racing the authorization check.
        var actor = (await db.Accounts.FromSqlInterpolated($"SELECT * FROM \"Accounts\" WHERE \"Id\" = {actorId} FOR SHARE").AsNoTracking().ToListAsync(ct)).SingleOrDefault();
        if (actor is null || actor.Role != AccountRole.Admin || actor.Status != AccountStatus.Active)
        {
            logger.LogWarning("GitHub App delivery recovery denied for actor {ActorId}", actorId);
            throw new GitHubIntegrationException("FORBIDDEN", "An active administrator is required.", 403);
        }
        var delivery = (await db.GitHubAppWebhookDeliveries.FromSqlInterpolated($"SELECT * FROM \"GitHubAppWebhookDeliveries\" WHERE \"DeliveryId\" = {normalized} FOR UPDATE").AsNoTracking().ToListAsync(ct)).SingleOrDefault()
            ?? throw new GitHubIntegrationException("GITHUB_APP_DELIVERY_NOT_FOUND", "Delivery not found.", 404);
        if (delivery.Status != GitHubAppWebhookDeliveryStatus.Failed || delivery.Route is not (GitHubAppWebhookRoute.InstallationLifecycle or GitHubAppWebhookRoute.RepositoryActivity))
        {
            logger.LogWarning("GitHub App delivery recovery rejected for {DeliveryId} by administrator {ActorId}: status {Status}", normalized, actorId, delivery.Status);
            throw new GitHubIntegrationException("GITHUB_APP_REQUEUE_NOT_ALLOWED", "Only failed supported deliveries may be requeued.", 409);
        }
        if (await db.Set<GitHubAppWebhookRequeueAudit>().CountAsync(x => x.DeliveryId == delivery.Id, ct) >= MaxOperatorRequeues)
        {
            logger.LogWarning("GitHub App delivery recovery limit reached for {DeliveryId} by administrator {ActorId}", normalized, actorId);
            throw new GitHubIntegrationException("GITHUB_APP_REQUEUE_LIMIT", "Delivery recovery limit reached.", 409);
        }
        db.Add(new GitHubAppWebhookRequeueAudit { DeliveryId = delivery.Id, ActorAccountId = actorId, CreatedAt = DateTimeOffset.UtcNow,
            Reason = reason.Trim(), PreviousErrorCode = delivery.LastErrorCode ?? "" });
        var changed = await db.GitHubAppWebhookDeliveries.Where(x => x.Id == delivery.Id && x.Status == GitHubAppWebhookDeliveryStatus.Failed)
            .ExecuteUpdateAsync(x => x.SetProperty(d => d.Status, GitHubAppWebhookDeliveryStatus.PendingHandler)
                .SetProperty(d => d.AttemptCount, 0).SetProperty(d => d.LeaseId, (Guid?)null)
                .SetProperty(d => d.LeaseExpiresAt, (DateTimeOffset?)null).SetProperty(d => d.NextAttemptAt, (DateTimeOffset?)null)
                .SetProperty(d => d.ProcessedAt, (DateTimeOffset?)null), ct);
        if (changed != 1) throw new GitHubIntegrationException("GITHUB_APP_REQUEUE_NOT_ALLOWED", "Delivery state changed.", 409);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        GitHubAppDiagnostics.Requeued.Add(1);
        logger.LogInformation("GitHub App delivery {DeliveryId} requeued by administrator {ActorId}", normalized, actorId);
    }
}
