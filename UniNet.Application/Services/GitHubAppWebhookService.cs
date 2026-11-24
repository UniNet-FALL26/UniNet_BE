using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

/// <summary>Authenticates and durably stages App deliveries; it does not execute lifecycle or activity handlers.</summary>
public sealed class GitHubAppWebhookService(UniNetDbContext db, IGitHubTokenProtector protector,
    IOptions<GitHubAppOptions> options, IOptions<GitHubWebhookOptions> legacyOptions, ILogger<GitHubAppWebhookService>? logger = null) : IGitHubAppWebhookService
{
    private static readonly HashSet<string> LifecycleEvents = new(StringComparer.Ordinal) { "installation", "installation_repositories" };
    private static readonly HashSet<string> RepositoryEvents = new(StringComparer.Ordinal)
    { "push", "pull_request", "issues", "issue_comment", "pull_request_review", "pull_request_review_comment", "release" };

    public async Task<GitHubAppWebhookResult> HandleAsync(string deliveryId, string eventName, string signature,
        ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        GitHubAppAvailability.Require(options);
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.WebhookSecret))
            throw Error("GITHUB_APP_WEBHOOK_NOT_CONFIGURED", "GitHub App webhook receiving is not configured.", 503);
        // A distinct secret is required to preserve source isolation, including when operators misconfigure an endpoint.
        if (!string.IsNullOrEmpty(legacyOptions.Value.Secret) && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(settings.WebhookSecret), Encoding.UTF8.GetBytes(legacyOptions.Value.Secret)))
            throw Error("GITHUB_APP_WEBHOOK_SECRET_CONFLICT", "App and repository webhook secrets must be different.", 503);
        if (payload.Length == 0 || payload.Length > Math.Clamp(settings.MaximumWebhookPayloadBytes, 1, 10_485_760))
            throw Error("GITHUB_WEBHOOK_PAYLOAD_INVALID", "Webhook payload is empty or too large.", 413);
        if (!Guid.TryParse(deliveryId, out var deliveryGuid) || string.IsNullOrWhiteSpace(eventName) || eventName.Length > 100)
            throw Error("GITHUB_WEBHOOK_HEADERS_INVALID", "Required webhook delivery headers are invalid.", 400);

        // Neither the body nor its identifiers are interpreted before authenticating its exact bytes.
        GitHubWebhookService.VerifySignature(settings.WebhookSecret, payload.Span, signature ?? string.Empty);
        var route = LifecycleEvents.Contains(eventName) ? GitHubAppWebhookRoute.InstallationLifecycle :
            RepositoryEvents.Contains(eventName) ? GitHubAppWebhookRoute.RepositoryActivity : GitHubAppWebhookRoute.Unknown;
        long? installationId = null, repositoryId = null;
        string? action = null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
            if (root.TryGetProperty("installation", out var installation) && installation.ValueKind == JsonValueKind.Object &&
                installation.TryGetProperty("id", out var iid) && iid.TryGetInt64(out var i) && i > 0) installationId = i;
            if (root.TryGetProperty("repository", out var repository) && repository.ValueKind == JsonValueKind.Object &&
                repository.TryGetProperty("id", out var rid) && rid.TryGetInt64(out var r) && r > 0) repositoryId = r;
            if (root.TryGetProperty("action", out var value))
            {
                if (value.ValueKind != JsonValueKind.String || (action = value.GetString()) is null || action.Length is 0 or > 100)
                    throw new JsonException();
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        { throw Error("GITHUB_WEBHOOK_PAYLOAD_INVALID", "Webhook payload must contain valid identifiers and action.", 400); }
        if (route != GitHubAppWebhookRoute.Unknown && installationId is null ||
            route == GitHubAppWebhookRoute.RepositoryActivity && repositoryId is null ||
            (route == GitHubAppWebhookRoute.InstallationLifecycle || route == GitHubAppWebhookRoute.RepositoryActivity && eventName != "push") && action is null)
            throw Error("GITHUB_WEBHOOK_PAYLOAD_INVALID", "Webhook event is missing required App identifiers or action.", 400);

        var normalizedId = deliveryGuid.ToString("D");
        var hash = Convert.ToHexString(SHA256.HashData(payload.Span));
        var existing = await db.GitHubAppWebhookDeliveries.AsNoTracking().SingleOrDefaultAsync(x => x.DeliveryId == normalizedId, ct);
        if (existing is not null) { var result = Duplicate(existing, eventName, hash); GitHubAppDiagnostics.Duplicate.Add(1); logger?.LogInformation("Duplicate verified GitHub App delivery {DeliveryId} event {EventName}", normalizedId, eventName); return result; }

        var matched = false;
        if (route == GitHubAppWebhookRoute.InstallationLifecycle)
            matched = await db.Set<GitHubAppInstallation>().AsNoTracking().AnyAsync(x => x.ProviderInstallationId == installationId, ct);
        else if (route == GitHubAppWebhookRoute.RepositoryActivity)
            matched = await (from installation in db.Set<GitHubAppInstallation>().AsNoTracking()
                join grant in db.Set<GitHubAppRepositoryGrant>().AsNoTracking() on installation.Id equals grant.InstallationId
                join link in db.ProjectGitRepositories.AsNoTracking() on grant.Id equals link.GitHubAppRepositoryGrantId
                join repository in db.GitHubRepositories.AsNoTracking() on link.GitHubRepositoryId equals repository.Id
                where installation.ProviderInstallationId == installationId && installation.Status == GitHubAppInstallationStatus.Active &&
                    !installation.VerificationUnknown && grant.RevokedAt == null && grant.ProviderRepositoryId == repositoryId &&
                    repository.ProviderRepositoryId == repositoryId && link.CredentialSource == GitRepositoryCredentialSource.GitHubApp &&
                    link.UnlinkedAt == null && !link.SyncPaused && link.AccessStatus == ProjectGitRepositoryAccessStatus.Active
                select link.Id).AnyAsync(ct);

        var status = route == GitHubAppWebhookRoute.Unknown ? GitHubAppWebhookDeliveryStatus.Unsupported :
            matched ? GitHubAppWebhookDeliveryStatus.PendingHandler : GitHubAppWebhookDeliveryStatus.AwaitingScope;
        string encrypted;
        try { encrypted = protector.Protect(Convert.ToBase64String(payload.Span)); }
        catch { throw Error("GITHUB_APP_WEBHOOK_STORAGE_UNAVAILABLE", "Webhook storage protection is unavailable.", 503); }
        var delivery = new GitHubAppWebhookDelivery { DeliveryId = normalizedId, EventName = eventName, Action = action,
            ProviderInstallationId = installationId, ProviderRepositoryId = repositoryId, Route = route, Status = status,
            PayloadHash = hash, PayloadEncrypted = encrypted, ReceivedAt = DateTimeOffset.UtcNow };
        db.GitHubAppWebhookDeliveries.Add(delivery);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.GetBaseException() is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_GitHubAppWebhookDeliveries_DeliveryId" })
        {
            db.Entry(delivery).State = EntityState.Detached;
            var winner = await db.GitHubAppWebhookDeliveries.AsNoTracking().SingleAsync(x => x.DeliveryId == normalizedId, ct);
            var result = Duplicate(winner, eventName, hash); GitHubAppDiagnostics.Duplicate.Add(1); return result;
        }
        GitHubAppDiagnostics.Received.Add(1);
        logger?.LogInformation("Verified GitHub App delivery {DeliveryId} event {EventName} staged as {Status}", normalizedId, eventName, status);
        return Result(delivery, false);
    }

    private static GitHubAppWebhookResult Duplicate(GitHubAppWebhookDelivery delivery, string eventName, string hash)
    {
        if (!string.Equals(delivery.EventName, eventName, StringComparison.Ordinal) ||
            !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(delivery.PayloadHash), Convert.FromHexString(hash)))
            throw Error("GITHUB_WEBHOOK_DELIVERY_CONFLICT", "Delivery identifier was already used for different content.", 409);
        return Result(delivery, true);
    }

    private static GitHubAppWebhookResult Result(GitHubAppWebhookDelivery delivery, bool duplicate) =>
        new(duplicate, delivery.Route != GitHubAppWebhookRoute.Unknown, delivery.Route.ToString(), delivery.Status.ToString());
    private static GitHubIntegrationException Error(string code, string message, int status) => new(code, message, status);
}
