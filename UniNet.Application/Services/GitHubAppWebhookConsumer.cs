using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Npgsql;
using UniNet.Application.Interfaces;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

/// <summary>Consumes only authenticated App lifecycle deliveries. Activity deliveries remain for Step 4C.</summary>
public sealed partial class GitHubAppWebhookConsumer(UniNetDbContext db, IGitHubTokenProtector protector,
    IGitHubAppInstallationTokenProvider tokens, GitHubAppInstallationReconciliationService provider,
    IOptions<GitHubAppOptions> appOptions, IOptions<GitSyncOptions> syncOptions, IGitHubProviderClient? activityProvider = null, IGitHubWorkerProgress? progress = null, ILogger<GitHubAppWebhookConsumer>? logger = null)
{
    private sealed record Payload(long InstallationId, long AppId, long OwnerId, string OwnerLogin, string OwnerType,
        string Action, IReadOnlyList<long> Added, IReadOnlyList<long> Removed);
    private sealed class DeliveryError(string code, bool permanent = false, DateTimeOffset? retryAt = null) : Exception(code)
    { public string Code { get; } = code; public bool Permanent { get; } = permanent; public DateTimeOffset? RetryAt { get; } = retryAt; }

    public async Task<GitHubAppWebhookDelivery?> ClaimNextAsync(CancellationToken ct)
    {
        GitHubAppAvailability.Require(appOptions);
        if (!db.Database.IsNpgsql()) throw new NotSupportedException("Webhook claiming requires PostgreSQL.");
        var now = DateTimeOffset.UtcNow;
        var max = Math.Clamp(syncOptions.Value.WebhookMaxAttempts, 1, 32);
        var lease = Guid.NewGuid();
        var expiry = now.AddSeconds(Math.Clamp(syncOptions.Value.WebhookLeaseSeconds, 60, 3600));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "GitHubAppWebhookDeliveries" SET "Status" = {(short)GitHubAppWebhookDeliveryStatus.Failed},
                "LeaseId" = NULL, "LeaseExpiresAt" = NULL, "NextAttemptAt" = NULL, "LastErrorCode" = {"GITHUB_APP_WEBHOOK_ATTEMPTS_EXHAUSTED"}
            WHERE "Route" IN ({(short)GitHubAppWebhookRoute.InstallationLifecycle}, {(short)GitHubAppWebhookRoute.RepositoryActivity}) AND "AttemptCount" >= {max}
                AND ("Status" IN ({(short)GitHubAppWebhookDeliveryStatus.PendingHandler}, {(short)GitHubAppWebhookDeliveryStatus.AwaitingScope}, {(short)GitHubAppWebhookDeliveryStatus.RetryScheduled})
                    OR ("Status" = {(short)GitHubAppWebhookDeliveryStatus.Processing} AND "LeaseExpiresAt" <= {now}))
            """, ct);
        var claimed = await db.GitHubAppWebhookDeliveries.FromSqlInterpolated($"""
            UPDATE "GitHubAppWebhookDeliveries" AS delivery
            SET "Status" = {(short)GitHubAppWebhookDeliveryStatus.Processing}, "LeaseId" = {lease}, "LeaseExpiresAt" = {expiry},
                "AttemptCount" = delivery."AttemptCount" + 1, "NextAttemptAt" = NULL
            WHERE delivery."Id" = (
                SELECT candidate."Id" FROM "GitHubAppWebhookDeliveries" AS candidate
                WHERE candidate."Route" IN ({(short)GitHubAppWebhookRoute.InstallationLifecycle}, {(short)GitHubAppWebhookRoute.RepositoryActivity}) AND candidate."AttemptCount" < {max}
                    AND NOT EXISTS (SELECT 1 FROM "GitHubAppInstallations" AS installation
                        WHERE installation."ProviderInstallationId" = candidate."ProviderInstallationId"
                            AND installation."VerificationUnknown" = TRUE AND installation."ReconciliationFailures" > 0
                            AND installation."NextReconciliationAt" > {now})
                    AND ((candidate."Status" IN ({(short)GitHubAppWebhookDeliveryStatus.PendingHandler}, {(short)GitHubAppWebhookDeliveryStatus.AwaitingScope}, {(short)GitHubAppWebhookDeliveryStatus.RetryScheduled})
                            AND (candidate."NextAttemptAt" IS NULL OR candidate."NextAttemptAt" <= {now}))
                        OR (candidate."Status" = {(short)GitHubAppWebhookDeliveryStatus.Processing} AND candidate."LeaseExpiresAt" <= {now}))
                ORDER BY candidate."ReceivedAt", candidate."Id" FOR UPDATE OF candidate SKIP LOCKED LIMIT 1)
            RETURNING delivery.*
            """).AsNoTracking().ToListAsync(ct);
        await tx.CommitAsync(ct); db.ChangeTracker.Clear();
        var result = claimed.SingleOrDefault();
        if (result is not null) { GitHubAppDiagnostics.Claimed.Add(1); logger?.LogInformation("Claimed GitHub App delivery {DeliveryId} attempt {Attempt}", result.DeliveryId, result.AttemptCount); }
        return result;
    }

    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var delivery = await ClaimNextAsync(ct);
        if (delivery is null) return false;
        try
        {
            await ProcessClaimAsync(delivery, ct);
            GitHubAppDiagnostics.HandlerCompleted.Add(1);
            logger?.LogInformation("Completed GitHub App delivery handler {DeliveryId}", delivery.DeliveryId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DeliveryError ex) { await FailAsync(delivery, ex.Code, ex.Permanent, ex.RetryAt, ct); }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Security.Cryptography.CryptographicException or JsonException or FormatException ||
            ex.GetBaseException() is PostgresException { SqlState: "40001" or "40P01" or "23505" })
        { await FailAsync(delivery, "GITHUB_APP_WEBHOOK_RETRY", false, provider.ProviderRetryAt, ct); }
        catch (Exception)
        { await FailAsync(delivery, "GITHUB_APP_WEBHOOK_PROCESSING_ERROR", false, null, ct); }
        return true;
    }

    public async Task ProcessClaimAsync(GitHubAppWebhookDelivery delivery, CancellationToken ct)
    {
        GitHubAppAvailability.Require(appOptions);
        // The lease is only a handle: always reload the verified, encrypted row before decoding it.
        delivery = await db.GitHubAppWebhookDeliveries.AsNoTracking().SingleOrDefaultAsync(x => x.Id == delivery.Id &&
            x.Status == GitHubAppWebhookDeliveryStatus.Processing && x.LeaseId == delivery.LeaseId &&
            x.LeaseExpiresAt > DateTimeOffset.UtcNow, ct) ?? throw new DeliveryError("GITHUB_APP_WEBHOOK_LEASE_LOST");
        if (delivery.Route == GitHubAppWebhookRoute.RepositoryActivity)
        {
            await ProcessActivityAsync(delivery, ct);
            return;
        }
        var payload = Decode(delivery);
        if (delivery.Route != GitHubAppWebhookRoute.InstallationLifecycle || delivery.LeaseId is null)
            throw new DeliveryError("GITHUB_APP_WEBHOOK_ROUTE_INVALID", true);
        var supported = delivery.EventName == "installation" && payload.Action is ("created" or "deleted" or "suspend" or "unsuspend" or "new_permissions_accepted") ||
            delivery.EventName == "installation_repositories" && payload.Action is ("added" or "removed");
        if (!supported) { await FinishUnsupportedAsync(delivery, ct); return; }
        if (appOptions.Value.AppId is not { } expectedApp || expectedApp <= 0)
            throw new DeliveryError("GITHUB_APP_ID_NOT_CONFIGURED");
        if (payload.AppId != expectedApp) throw new DeliveryError("GITHUB_APP_WEBHOOK_APP_MISMATCH", true);

        var expected = await FenceUnknownAsync(delivery, payload, ct);
        if (expected is null && delivery.EventName == "installation" && payload.Action == "deleted")
        { await FinishAbsentAsync(delivery, ct); return; }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(Math.Min(30, Math.Clamp(syncOptions.Value.WebhookLeaseSeconds, 60, 3600) - 10)));
        var verifiedApp = await provider.ProbeAppIdentityAsync(budget.Token);
        if (verifiedApp is null) throw new DeliveryError("GITHUB_APP_WEBHOOK_VERIFICATION_PENDING", false, provider.ProviderRetryAt);
        progress?.Completed("consumer");
        if (verifiedApp != expectedApp) throw new DeliveryError("GITHUB_APP_WEBHOOK_APP_MISMATCH", true);
        var probe = await provider.ProbeInstallationAsync(payload.InstallationId, budget.Token);
        if (probe.Status is not null || probe.NotFound) progress?.Completed("consumer");
        if (probe.Status is not null && (probe.AppId != expectedApp || probe.OwnerId != payload.OwnerId))
            throw new DeliveryError("GITHUB_APP_WEBHOOK_PROVIDER_MISMATCH", true);
        if (probe.Status is null && !(probe.NotFound && delivery.EventName == "installation" && payload.Action == "deleted"))
            throw new DeliveryError("GITHUB_APP_WEBHOOK_VERIFICATION_PENDING", false, provider.ProviderRetryAt);

        HashSet<long>? remoteRepositories = null;
        if (probe.Status == GitHubAppInstallationStatus.Active && delivery.EventName == "installation_repositories")
        {
            remoteRepositories = await provider.ReadRepositoriesAsync(payload.InstallationId, budget.Token);
            if (remoteRepositories is null) throw new DeliveryError("GITHUB_APP_WEBHOOK_VERIFICATION_PENDING", false, provider.ProviderRetryAt);
            var after = await provider.ProbeInstallationAsync(payload.InstallationId, budget.Token);
            if (after.Status != GitHubAppInstallationStatus.Active || after.AppId != expectedApp || after.OwnerId != payload.OwnerId)
                throw new DeliveryError("GITHUB_APP_WEBHOOK_VERIFICATION_PENDING", false, provider.ProviderRetryAt);
        }
        await ApplyVerifiedAsync(delivery, payload, expected, probe, remoteRepositories, ct);
    }

    private Payload Decode(GitHubAppWebhookDelivery delivery)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(protector.Unprotect(delivery.PayloadEncrypted)); }
        catch { throw new DeliveryError("GITHUB_APP_WEBHOOK_DECRYPT_FAILED"); }
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(delivery.PayloadHash)))
                throw new DeliveryError("GITHUB_APP_WEBHOOK_HASH_MISMATCH", true);
            using var json = JsonDocument.Parse(bytes); var root = json.RootElement;
            var installation = root.GetProperty("installation"); var account = installation.GetProperty("account");
            var id = installation.GetProperty("id").GetInt64(); var appId = installation.GetProperty("app_id").GetInt64();
            var ownerId = account.GetProperty("id").GetInt64(); var login = account.GetProperty("login").GetString();
            var type = account.GetProperty("type").GetString(); var action = root.GetProperty("action").GetString();
            if (id <= 0 || appId <= 0 || ownerId <= 0 || string.IsNullOrWhiteSpace(login) || login.Length > 255 ||
                type is not ("User" or "Organization") || action is null || action != delivery.Action || id != delivery.ProviderInstallationId)
                throw new DeliveryError("GITHUB_APP_WEBHOOK_PAYLOAD_INVALID", true);
            var added = delivery.EventName == "installation_repositories" ? ReadIds(root, "repositories_added") : [];
            var removed = delivery.EventName == "installation_repositories" ? ReadIds(root, "repositories_removed") : [];
            return new(id, appId, ownerId, login, type, action, added, removed);
        }
        catch (DeliveryError) { throw; }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or OverflowException)
        { throw new DeliveryError("GITHUB_APP_WEBHOOK_PAYLOAD_INVALID", true); }
    }

    private static long[] ReadIds(JsonElement root, string property)
    {
        var array = root.GetProperty(property);
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 10000) throw new DeliveryError("GITHUB_APP_WEBHOOK_PAYLOAD_INVALID", true);
        var ids = new HashSet<long>();
        foreach (var item in array.EnumerateArray())
        {
            var id = item.GetProperty("id").GetInt64();
            if (id <= 0 || !ids.Add(id)) throw new DeliveryError("GITHUB_APP_WEBHOOK_PAYLOAD_INVALID", true);
        }
        return ids.ToArray();
    }

    private async Task<GitHubAppInstallation?> FenceUnknownAsync(GitHubAppWebhookDelivery delivery, Payload payload, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockDeliveryAsync(delivery, ct);
        var installation = await db.Set<GitHubAppInstallation>().SingleOrDefaultAsync(x => x.ProviderInstallationId == payload.InstallationId, ct);
        if (installation is null) { await tx.CommitAsync(ct); db.ChangeTracker.Clear(); return null; }
        if (installation.OwnerAccountId != payload.OwnerId || installation.OwnerType != payload.OwnerType)
            throw new DeliveryError("GITHUB_APP_WEBHOOK_OWNER_MISMATCH", true);
        await LockInstallationAsync(installation.Id, ct);
        // PostgreSQL stores microseconds; retain exactly the value that will be read after commit.
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % 10));
        if (now <= installation.UpdatedAt) now = installation.UpdatedAt.AddTicks(10);
        installation.VerificationUnknown = true;
        if (installation.ReconciliationFailures == 0 || installation.NextReconciliationAt is null || installation.NextReconciliationAt <= now)
            installation.NextReconciliationAt = now;
        installation.UpdatedAt = now;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        tokens.InvalidateInstallation(payload.InstallationId);
        db.ChangeTracker.Clear(); return installation;
    }

    private async Task ApplyVerifiedAsync(GitHubAppWebhookDelivery delivery, Payload payload, GitHubAppInstallation? expected,
        GitHubAppInstallationReconciliationService.InstallationProbe probe, HashSet<long>? remote, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockDeliveryAsync(delivery, ct);
        var installation = await db.Set<GitHubAppInstallation>().SingleOrDefaultAsync(x => x.ProviderInstallationId == payload.InstallationId, ct);
        if (expected is null)
        {
            if (installation is not null) throw new DeliveryError("GITHUB_APP_WEBHOOK_CONCURRENT_CHANGE");
            if (probe.Status is null) throw new DeliveryError("GITHUB_APP_WEBHOOK_VERIFICATION_PENDING");
            installation = new GitHubAppInstallation { ProviderInstallationId = payload.InstallationId, OwnerAccountId = payload.OwnerId,
                OwnerLogin = payload.OwnerLogin, OwnerType = payload.OwnerType, Status = probe.Status.Value, VerificationUnknown = true,
                NextReconciliationAt = DateTimeOffset.UtcNow };
            db.Add(installation);
        }
        else
        {
            if (installation is null || installation.Id != expected.Id || installation.UpdatedAt != expected.UpdatedAt || !installation.VerificationUnknown)
                throw new DeliveryError("GITHUB_APP_WEBHOOK_CONCURRENT_CHANGE");
            await LockInstallationAsync(installation.Id, ct);
        }
        var now = DateTimeOffset.UtcNow;
        var unavailable = new List<Guid>();
        if (probe.NotFound && delivery.EventName == "installation" && payload.Action == "deleted")
            installation.Status = GitHubAppInstallationStatus.Uninstalled;
        else if (probe.Status == GitHubAppInstallationStatus.Suspended)
            installation.Status = GitHubAppInstallationStatus.Suspended;
        // Active positive or stale negative evidence is reconciled by the existing polling path before granting access.
        if (installation.Status is (GitHubAppInstallationStatus.Uninstalled or GitHubAppInstallationStatus.Suspended) &&
            (probe.NotFound || probe.Status == GitHubAppInstallationStatus.Suspended))
            unavailable.AddRange(await db.Set<GitHubAppRepositoryGrant>().Where(x => x.InstallationId == installation.Id).Select(x => x.Id).ToListAsync(ct));
        if (remote is not null)
        {
            var grants = expected is null ? [] : await db.Set<GitHubAppRepositoryGrant>().Where(x => x.InstallationId == installation.Id).ToListAsync(ct);
            foreach (var grant in grants)
            {
                if (payload.Removed.Contains(grant.ProviderRepositoryId) && !remote.Contains(grant.ProviderRepositoryId))
                { grant.RevokedAt ??= now; unavailable.Add(grant.Id); }
                if (payload.Added.Contains(grant.ProviderRepositoryId) && remote.Contains(grant.ProviderRepositoryId))
                { grant.RevokedAt = null; grant.VerifiedAt = now; }
            }
            foreach (var id in payload.Added.Where(remote.Contains).Where(id => grants.All(g => g.ProviderRepositoryId != id)))
                db.Add(new GitHubAppRepositoryGrant { InstallationId = installation.Id, ProviderRepositoryId = id, GrantedAt = now, VerifiedAt = now });
        }
        if (unavailable.Count > 0) await RevokeLinksAsync(unavailable, now, ct);
        installation.VerificationUnknown = true; installation.UpdatedAt = now;
        if (installation.ReconciliationFailures == 0 || installation.NextReconciliationAt is null || installation.NextReconciliationAt <= now)
            installation.NextReconciliationAt = now;
        await CompleteAsync(delivery, GitHubAppWebhookDeliveryStatus.Processed, now, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        tokens.InvalidateInstallation(payload.InstallationId); db.ChangeTracker.Clear();
    }

    private async Task RevokeLinksAsync(List<Guid> grants, DateTimeOffset now, CancellationToken ct)
    {
        var links = await db.ProjectGitRepositories.Where(x => x.CredentialSource == GitRepositoryCredentialSource.GitHubApp && x.UnlinkedAt == null &&
            x.GitHubAppRepositoryGrantId != null && grants.Contains(x.GitHubAppRepositoryGrantId.Value)).ToListAsync(ct);
        foreach (var link in links.Where(x => x.AccessStatus == ProjectGitRepositoryAccessStatus.Active))
        {
            link.AccessStatus = ProjectGitRepositoryAccessStatus.AccessLost; link.LastAccessCheckedAt = now; link.UpdatedAt = now; link.Version = Guid.NewGuid();
            db.ProjectGitRepositoryHistory.Add(new ProjectGitRepositoryHistory { ProjectGitRepositoryId = link.Id, ActorProfileId = link.LinkedByProfileId,
                Action = ProjectGitRepositoryHistoryAction.AccessLost, Details = "GitHub App installation or repository access was revoked." });
        }
        var ids = links.Select(x => x.Id).ToArray();
        var jobs = await db.GitSyncJobs.Where(x => ids.Contains(x.ProjectGitRepositoryId) &&
            (x.Status == GitSyncJobStatus.Pending || x.Status == GitSyncJobStatus.RetryScheduled)).ToListAsync(ct);
        foreach (var job in jobs)
        { job.Status = GitSyncJobStatus.Cancelled; job.CompletedAt = now; job.NextAttemptAt = null; job.ErrorCode = "GITHUB_REPOSITORY_ACCESS_LOST";
            job.ErrorMessage = "GitHub App access is unavailable."; job.UpdatedAt = now; }
    }

    private async Task LockDeliveryAsync(GitHubAppWebhookDelivery delivery, CancellationToken ct)
    {
        var count = await db.GitHubAppWebhookDeliveries.Where(x => x.Id == delivery.Id && x.Status == GitHubAppWebhookDeliveryStatus.Processing &&
            x.LeaseId == delivery.LeaseId && x.LeaseExpiresAt > DateTimeOffset.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastErrorCode, x => x.LastErrorCode), ct);
        if (count != 1) throw new DeliveryError("GITHUB_APP_WEBHOOK_LEASE_LOST");
    }

    private async Task LockInstallationAsync(Guid id, CancellationToken ct)
    {
        var acquired = await db.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_xact_lock(1920167523, hashtext({0})) AS \"Value\"", id.ToString()).SingleAsync(ct);
        if (!acquired) throw new DeliveryError("GITHUB_APP_WEBHOOK_RECONCILIATION_BUSY");
    }

    private async Task FinishUnsupportedAsync(GitHubAppWebhookDelivery delivery, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockDeliveryAsync(delivery, ct);
        await CompleteAsync(delivery, GitHubAppWebhookDeliveryStatus.Unsupported, DateTimeOffset.UtcNow, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); db.ChangeTracker.Clear();
    }
    private async Task FinishAbsentAsync(GitHubAppWebhookDelivery delivery, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockDeliveryAsync(delivery, ct);
        await CompleteAsync(delivery, GitHubAppWebhookDeliveryStatus.Processed, DateTimeOffset.UtcNow, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); db.ChangeTracker.Clear();
    }
    private async Task CompleteAsync(GitHubAppWebhookDelivery delivery, GitHubAppWebhookDeliveryStatus status, DateTimeOffset now, CancellationToken ct)
    {
        var row = await db.GitHubAppWebhookDeliveries.SingleAsync(x => x.Id == delivery.Id, ct);
        row.Status = status; row.LeaseId = null; row.LeaseExpiresAt = null; row.NextAttemptAt = null; row.LastErrorCode = null;
        row.ProcessedAt = status == GitHubAppWebhookDeliveryStatus.Processed ? now : null;
    }
    private async Task FailAsync(GitHubAppWebhookDelivery delivery, string code, bool permanent, DateTimeOffset? providerRetry, CancellationToken ct)
    {
        db.ChangeTracker.Clear(); var now = DateTimeOffset.UtcNow;
        var failed = permanent || delivery.AttemptCount >= Math.Clamp(syncOptions.Value.WebhookMaxAttempts, 1, 32);
        var fallback = now.AddSeconds(Math.Min(21600, Math.Clamp(syncOptions.Value.RetryBaseDelaySeconds, 1, 1800) * Math.Pow(2, Math.Min(delivery.AttemptCount - 1, 9))));
        var next = providerRetry is { } retry && retry > fallback ? retry : fallback;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var updated = await db.GitHubAppWebhookDeliveries.Where(x => x.Id == delivery.Id && x.Status == GitHubAppWebhookDeliveryStatus.Processing &&
            x.LeaseId == delivery.LeaseId && x.LeaseExpiresAt > now).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, failed ? GitHubAppWebhookDeliveryStatus.Failed : GitHubAppWebhookDeliveryStatus.RetryScheduled)
                .SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(x => x.NextAttemptAt, failed ? (DateTimeOffset?)null : next).SetProperty(x => x.LastErrorCode, code), ct);
        if (updated == 1 && !failed && code is ("GITHUB_APP_WEBHOOK_VERIFICATION_PENDING" or "GITHUB_APP_WEBHOOK_RETRY"))
            await db.Set<GitHubAppInstallation>().Where(x => x.ProviderInstallationId == delivery.ProviderInstallationId && x.VerificationUnknown &&
                    (x.NextReconciliationAt == null || x.NextReconciliationAt < next))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextReconciliationAt, next)
                    .SetProperty(x => x.ReconciliationFailures, x => x.ReconciliationFailures < 16 ? x.ReconciliationFailures + 1 : 16), ct);
        await tx.CommitAsync(ct);
        if (updated == 1) { if (failed) GitHubAppDiagnostics.Failed.Add(1); else GitHubAppDiagnostics.Retry.Add(1); logger?.LogWarning("GitHub App delivery {DeliveryId} error {ErrorCode}; permanent {Permanent}", delivery.DeliveryId, code, failed); }
    }
}
