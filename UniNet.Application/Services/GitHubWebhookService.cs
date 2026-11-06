using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class GitHubWebhookService(
    UniNetDbContext db,
    IOptions<GitHubWebhookOptions> options) : IGitHubWebhookService
{
    private static readonly HashSet<string> SupportedEvents = new(StringComparer.Ordinal)
    {
        "push", "pull_request", "issues", "issue_comment",
        "pull_request_review", "pull_request_review_comment", "release"
    };

    public async Task<GitHubWebhookResult> HandleAsync(
        string deliveryId,
        string eventName,
        string signature,
        ReadOnlyMemory<byte> payload,
        CancellationToken ct)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Secret))
            throw Error("GITHUB_WEBHOOK_NOT_CONFIGURED", "GitHub webhook receiving is not configured.", 503);
        if (payload.Length == 0 || payload.Length > Math.Clamp(settings.MaximumPayloadBytes, 1, 10_485_760))
            throw Error("GITHUB_WEBHOOK_PAYLOAD_INVALID", "Webhook payload is empty or too large.", 413);
        if (!Guid.TryParse(deliveryId, out var deliveryGuid) ||
            string.IsNullOrWhiteSpace(eventName) || eventName.Length > 100)
            throw Error("GITHUB_WEBHOOK_HEADERS_INVALID", "Required webhook delivery headers are invalid.", 400);
        VerifySignature(settings.Secret, payload.Span, signature);

        var supported = SupportedEvents.Contains(eventName);
        long? repositoryId = null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException();
            if (document.RootElement.TryGetProperty("repository", out var repository) &&
                repository.TryGetProperty("id", out var id) &&
                id.TryGetInt64(out var parsedId) && parsedId > 0)
                repositoryId = parsedId;
        }
        catch (JsonException)
        {
            throw Error("GITHUB_WEBHOOK_PAYLOAD_INVALID", "Webhook payload must be a valid JSON object.", 400);
        }

        if (supported && repositoryId is null)
            throw Error("GITHUB_WEBHOOK_PAYLOAD_INVALID", "Supported repository events must identify a repository.", 400);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (await db.GitHubWebhookDeliveries.AnyAsync(x => x.DeliveryId == deliveryGuid.ToString("D"), ct))
        {
            await transaction.CommitAsync(ct);
            return new(true, supported, 0);
        }

        var delivery = new GitHubWebhookDelivery
        {
            DeliveryId = deliveryGuid.ToString("D"),
            EventName = eventName,
            ProviderRepositoryId = repositoryId,
            ReceivedAt = DateTimeOffset.UtcNow,
            IsSupportedEvent = supported
        };
        db.GitHubWebhookDeliveries.Add(delivery);

        var enqueued = 0;
        if (supported && repositoryId is { } providerRepositoryId)
        {
            var associations = await (
                from link in db.ProjectGitRepositories
                join repository in db.GitHubRepositories on link.GitHubRepositoryId equals repository.Id
                join credential in db.GitHubOAuthConnections on link.OAuthConnectionId equals credential.Id
                where repository.ProviderRepositoryId == providerRepositoryId &&
                    link.UnlinkedAt == null && !link.SyncPaused &&
                    link.AccessStatus == ProjectGitRepositoryAccessStatus.Active &&
                    credential.Status == GitHubOAuthConnectionStatus.Active
                select new
                {
                    link.Id,
                    link.GitHubRepositoryId,
                    link.OAuthConnectionId,
                    link.LinkedByProfileId
                }).ToListAsync(ct);

            foreach (var association in associations)
            {
                var existing = await db.GitSyncJobs.AnyAsync(x =>
                    x.ProjectGitRepositoryId == association.Id &&
                    x.IdempotencyKey == delivery.DeliveryId, ct);
                if (existing) continue;
                var activeSync = await db.GitSyncJobs.AnyAsync(x =>
                    x.ProjectGitRepositoryId == association.Id &&
                    (x.Status == GitSyncJobStatus.Pending ||
                     x.Status == GitSyncJobStatus.Running ||
                     x.Status == GitSyncJobStatus.RetryScheduled ||
                     x.Status == GitSyncJobStatus.Paused), ct);
                if (activeSync) continue;
                db.GitSyncJobs.Add(new GitSyncJob
                {
                    ProjectGitRepositoryId = association.Id,
                    GitHubRepositoryId = association.GitHubRepositoryId,
                    OAuthConnectionId = association.OAuthConnectionId,
                    RequestedByProfileId = association.LinkedByProfileId,
                    SyncType = GitSyncType.Webhook,
                    Status = GitSyncJobStatus.Pending,
                    IdempotencyKey = delivery.DeliveryId,
                    CreatedAt = delivery.ReceivedAt,
                    UpdatedAt = delivery.ReceivedAt
                });
                enqueued++;
            }
        }

        delivery.EnqueuedJobCount = enqueued;
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException exception) when (IsDeliveryConflict(exception))
        {
            await transaction.RollbackAsync(ct);
            return new(true, supported, 0);
        }
        return new(false, supported, enqueued);
    }

    private static void VerifySignature(string secret, ReadOnlySpan<byte> payload, string signature)
    {
        if (signature.Length != 71 || !signature.StartsWith("sha256=", StringComparison.Ordinal))
            throw Error("GITHUB_WEBHOOK_SIGNATURE_INVALID", "Webhook signature is invalid.", 401);
        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(signature[7..]);
        }
        catch (FormatException)
        {
            throw Error("GITHUB_WEBHOOK_SIGNATURE_INVALID", "Webhook signature is invalid.", 401);
        }

        var key = Encoding.UTF8.GetBytes(secret);
        var expected = HMACSHA256.HashData(key, payload);
        if (supplied.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(supplied, expected))
            throw Error("GITHUB_WEBHOOK_SIGNATURE_INVALID", "Webhook signature is invalid.", 401);
    }

    private static bool IsDeliveryConflict(DbUpdateException exception) =>
        exception.GetBaseException() is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_GitHubWebhookDeliveries_DeliveryId"
        };

    private static GitHubIntegrationException Error(string code, string message, int status) =>
        new(code, message, status);
}
