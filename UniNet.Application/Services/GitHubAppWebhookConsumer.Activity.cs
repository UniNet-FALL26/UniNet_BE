using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed partial class GitHubAppWebhookConsumer
{
    private sealed record ActivityPayload(long InstallationId, long RepositoryId);

    private async Task ProcessActivityAsync(GitHubAppWebhookDelivery delivery, CancellationToken ct)
    {
        var activity = DecodeActivity(delivery);
        if (appOptions.Value.AppId is not { } appId || appId <= 0) throw new DeliveryError("GITHUB_APP_ID_NOT_CONFIGURED");
        if (activityProvider is null) throw new DeliveryError("GITHUB_APP_WEBHOOK_PROVIDER_UNAVAILABLE");
        var installation = await db.Set<GitHubAppInstallation>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProviderInstallationId == activity.InstallationId, ct);
        if (installation is null || installation.Status != GitHubAppInstallationStatus.Active || installation.VerificationUnknown)
            throw new DeliveryError("GITHUB_APP_WEBHOOK_SCOPE_UNAVAILABLE");
        var grant = await db.Set<GitHubAppRepositoryGrant>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.InstallationId == installation.Id && x.ProviderRepositoryId == activity.RepositoryId && x.RevokedAt == null, ct);
        var repository = await db.GitHubRepositories.AsNoTracking().SingleOrDefaultAsync(x => x.ProviderRepositoryId == activity.RepositoryId, ct);
        if (grant is null || repository is null) throw new DeliveryError("GITHUB_APP_WEBHOOK_SCOPE_UNAVAILABLE");

        // No database transaction is held during provider verification.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(Math.Min(30, Math.Clamp(syncOptions.Value.WebhookLeaseSeconds, 60, 3600) - 10)));
        var verifiedApp = await provider.ProbeAppIdentityAsync(budget.Token);
        if (verifiedApp is null) throw new DeliveryError("GITHUB_APP_WEBHOOK_VERIFICATION_PENDING", false, provider.ProviderRetryAt);
        progress?.Completed("consumer");
        if (verifiedApp != appId) throw new DeliveryError("GITHUB_APP_WEBHOOK_APP_MISMATCH", true);
        var probe = await provider.ProbeInstallationAsync(activity.InstallationId, budget.Token);
        if (probe.Status != GitHubAppInstallationStatus.Active || probe.AppId != appId || probe.OwnerId != installation.OwnerAccountId)
            throw new DeliveryError("GITHUB_APP_WEBHOOK_VERIFICATION_PENDING", false, provider.ProviderRetryAt);
        progress?.Completed("consumer");
        GitHubRemoteRepository verifiedRepository;
        try
        {
            var token = await tokens.GetAsync(activity.InstallationId, activity.RepositoryId, budget.Token);
            verifiedRepository = await activityProvider.GetInstallationRepositoryAsync(token, activity.RepositoryId, budget.Token);
            progress?.Completed("consumer");
            if (verifiedRepository.Id != activity.RepositoryId || !verifiedRepository.CanRead)
            { tokens.InvalidateInstallation(activity.InstallationId); throw new DeliveryError("GITHUB_APP_WEBHOOK_SCOPE_UNAVAILABLE"); }
        }
        catch (GitHubIntegrationException ex)
        {
            if (ex.Code is "GITHUB_CREDENTIAL_INVALID" or "GITHUB_ACCESS_DENIED" or "GITHUB_RESOURCE_UNAVAILABLE" or "GITHUB_APP_ACCESS_DENIED" or "GITHUB_APP_INSTALLATION_UNAVAILABLE")
                tokens.InvalidateInstallation(activity.InstallationId);
            throw new DeliveryError("GITHUB_APP_WEBHOOK_VERIFICATION_PENDING", false, ex.RetryAfter is { } wait ? DateTimeOffset.UtcNow.Add(wait) : null);
        }

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockDeliveryAsync(delivery, ct);
        var locked = await db.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_xact_lock_shared(1920167523, hashtext({0})) AS \"Value\"", installation.Id.ToString()).SingleAsync(ct);
        if (!locked) throw new DeliveryError("GITHUB_APP_WEBHOOK_RECONCILIATION_BUSY");
        var current = await db.Set<GitHubAppInstallation>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == installation.Id, ct);
        var currentGrant = await db.Set<GitHubAppRepositoryGrant>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == grant.Id, ct);
        if (current is null || current.Status != GitHubAppInstallationStatus.Active || current.VerificationUnknown ||
            current.UpdatedAt != installation.UpdatedAt || current.ProviderInstallationId != activity.InstallationId ||
            currentGrant is null || currentGrant.InstallationId != current.Id || currentGrant.ProviderRepositoryId != activity.RepositoryId ||
            currentGrant.RevokedAt != null || currentGrant.VerifiedAt != grant.VerifiedAt)
            throw new DeliveryError("GITHUB_APP_WEBHOOK_CONCURRENT_CHANGE");
        var currentRepository = await db.GitHubRepositories.SingleOrDefaultAsync(x => x.Id == repository.Id, ct);
        if (currentRepository is null || currentRepository.ProviderRepositoryId != activity.RepositoryId || currentRepository.UpdatedAt != repository.UpdatedAt)
            throw new DeliveryError("GITHUB_APP_WEBHOOK_CONCURRENT_CHANGE");
        if (ProjectGitRepositoryService.MetadataChanged(currentRepository, verifiedRepository))
            ProjectGitRepositoryService.UpdateMetadata(currentRepository, verifiedRepository, DateTimeOffset.UtcNow);
        var enqueued = await GitSyncJobService.EnqueueAppWebhookAsync(db, grant.Id, repository.Id, delivery.DeliveryId, DateTimeOffset.UtcNow, ct);
        await CompleteAsync(delivery, GitHubAppWebhookDeliveryStatus.Processed, DateTimeOffset.UtcNow, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); db.ChangeTracker.Clear();
        if (enqueued > 0) GitHubAppDiagnostics.JobEnqueued.Add(enqueued);
        if (enqueued > 0) logger?.LogInformation("GitHub App delivery {DeliveryId} enqueued {JobCount} sync jobs", delivery.DeliveryId, enqueued);
    }

    private ActivityPayload DecodeActivity(GitHubAppWebhookDelivery delivery)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(protector.Unprotect(delivery.PayloadEncrypted)); }
        catch { throw new DeliveryError("GITHUB_APP_WEBHOOK_DECRYPT_FAILED"); }
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(delivery.PayloadHash)))
                throw new DeliveryError("GITHUB_APP_WEBHOOK_HASH_MISMATCH", true);
            using var document = JsonDocument.Parse(bytes); var root = document.RootElement;
            var installation = root.GetProperty("installation"); var repository = root.GetProperty("repository");
            var installationId = installation.GetProperty("id").GetInt64(); var repositoryId = repository.GetProperty("id").GetInt64();
            if (installationId <= 0 || repositoryId <= 0 || installationId != delivery.ProviderInstallationId || repositoryId != delivery.ProviderRepositoryId)
                throw new DeliveryError("GITHUB_APP_WEBHOOK_PAYLOAD_INVALID", true);
            if (installation.TryGetProperty("app_id", out var payloadApp) &&
                (!payloadApp.TryGetInt64(out var appId) || appOptions.Value.AppId != appId))
                throw new DeliveryError("GITHUB_APP_WEBHOOK_APP_MISMATCH", true);
            if (delivery.EventName != "push" && (!root.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(action.GetString()) || action.GetString() != delivery.Action))
                throw new DeliveryError("GITHUB_APP_WEBHOOK_PAYLOAD_INVALID", true);
            var valid = delivery.EventName switch
            {
                "push" => root.TryGetProperty("ref", out var reference) && reference.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(reference.GetString()),
                "pull_request" => Object(root, "pull_request"),
                "issues" => Object(root, "issue"),
                "issue_comment" => Object(root, "issue") && Object(root, "comment"),
                "pull_request_review" => Object(root, "pull_request") && Object(root, "review"),
                "pull_request_review_comment" => Object(root, "pull_request") && Object(root, "comment"),
                "release" => Object(root, "release"),
                _ => false
            };
            if (!valid) throw new DeliveryError("GITHUB_APP_WEBHOOK_PAYLOAD_INVALID", true);
            return new(installationId, repositoryId);
        }
        catch (DeliveryError) { throw; }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or OverflowException)
        { throw new DeliveryError("GITHUB_APP_WEBHOOK_PAYLOAD_INVALID", true); }
    }

    private static bool Object(JsonElement root, string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Object;
}
