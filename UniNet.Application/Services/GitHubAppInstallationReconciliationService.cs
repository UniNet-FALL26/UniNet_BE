using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

/// <summary>Reconciles authoritative installation/grant evidence with durable retry deadlines.</summary>
public sealed class GitHubAppInstallationReconciliationService(
    UniNetDbContext db, HttpClient http, IOptions<GitHubAppOptions> appOptions,
    IOptions<GitSyncOptions> syncOptions, IGitHubAppInstallationTokenProvider tokens, IGitHubWorkerProgress? progress = null,
    ILogger<GitHubAppInstallationReconciliationService>? logger = null)
{
    private DateTimeOffset? responseRetryAt;

    public async Task ReconcileAsync(CancellationToken ct)
    {
        GitHubAppAvailability.Require(appOptions);
        if (!syncOptions.Value.Enabled || string.IsNullOrWhiteSpace(appOptions.Value.ClientId) ||
            string.IsNullOrWhiteSpace(appOptions.Value.PrivateKeyPem) || !db.Database.IsNpgsql()) return;
        var now = DateTimeOffset.UtcNow;
        var candidates = await db.Set<GitHubAppInstallation>().AsNoTracking()
            .Where(x => x.NextReconciliationAt == null || x.NextReconciliationAt <= now)
            .OrderBy(x => x.NextReconciliationAt).ThenBy(x => x.Id)
            .Take(Math.Clamp(syncOptions.Value.ReconciliationBatchSize, 1, 100)).ToListAsync(ct);
        foreach (var candidate in candidates)
        {
            await db.Database.OpenConnectionAsync(ct);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock(1920167523, hashtext(@id))";
            var parameter = command.CreateParameter(); parameter.ParameterName = "id"; parameter.Value = candidate.Id.ToString(); command.Parameters.Add(parameter);
            var locked = false;
            try
            {
                locked = (bool)(await command.ExecuteScalarAsync(ct) ?? false);
                if (!locked) continue;
                var current = await db.Set<GitHubAppInstallation>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == candidate.Id, ct);
                if (current is null || current.NextReconciliationAt > DateTimeOffset.UtcNow) continue;
                var expectedGrants = await db.Set<GitHubAppRepositoryGrant>().AsNoTracking().Where(x => x.InstallationId == current.Id).ToListAsync(ct);
                responseRetryAt = null;
                GitHubAppInstallationStatus? status = null;
                HashSet<long>? repositories = null;
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(syncOptions.Value.ReconciliationTimeoutSeconds, 5, 300)));
                try
                {
                    status = await CheckInstallationAsync(current.ProviderInstallationId, budget.Token);
                    if (status == GitHubAppInstallationStatus.Active)
                    {
                        repositories = await ReadRepositoriesAsync(current.ProviderInstallationId, budget.Token);
                        if (repositories is null || await CheckInstallationAsync(current.ProviderInstallationId, budget.Token) != GitHubAppInstallationStatus.Active)
                            status = null;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or
                    System.Security.Cryptography.CryptographicException or ArgumentException or InvalidOperationException)
                { status = null; }
                await ApplyResultAsync(current, expectedGrants, status, repositories, ct);
                progress?.Completed("reconciliation");
            }
            catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }
            catch (Exception ex) when (ex.GetBaseException() is PostgresException { SqlState: "40001" or "40P01" })
            { db.ChangeTracker.Clear(); } // Another authorization writer won; leave this installation due for a later pass.
            finally
            {
                try
                {
                    if (locked)
                    {
                        command.CommandText = "SELECT pg_advisory_unlock(1920167523, hashtext(@id))";
                        await command.ExecuteScalarAsync(CancellationToken.None);
                    }
                }
                finally { await db.Database.CloseConnectionAsync(); }
            }
        }
    }

    private async Task ApplyResultAsync(GitHubAppInstallation expected, List<GitHubAppRepositoryGrant> expectedGrants,
        GitHubAppInstallationStatus? status, HashSet<long>? repositories, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var current = await db.Set<GitHubAppInstallation>().AsNoTracking().SingleAsync(x => x.Id == expected.Id, ct);
        if (current.UpdatedAt != expected.UpdatedAt || current.NextReconciliationAt != expected.NextReconciliationAt ||
            current.VerificationUnknown != expected.VerificationUnknown || current.Status != expected.Status) return;
        var grants = await db.Set<GitHubAppRepositoryGrant>().Where(x => x.InstallationId == expected.Id).ToListAsync(ct);
        var original = expectedGrants.ToDictionary(x => x.Id);
        // A user verification or revoke during the network calls supersedes this observation.
        if (expectedGrants.Any(x => !grants.Any(g => g.Id == x.Id && g.VerifiedAt == x.VerifiedAt && g.RevokedAt == x.RevokedAt)))
        { db.ChangeTracker.Clear(); return; }
        var failures = status is null ? Math.Min(16, expected.ReconciliationFailures + 1) : 0;
        var fallbackRetry = now.AddSeconds(Math.Min(21600, 60 * Math.Pow(2, Math.Min(Math.Max(0, failures - 1), 9))));
        var next = status is null
            ? responseRetryAt is { } providerRetry && providerRetry > fallbackRetry ? providerRetry : fallbackRetry
            : now.AddMinutes(Math.Clamp(syncOptions.Value.ReconciliationIntervalMinutes, 1, 1440));
        var lostLinks = new List<Guid>();
        await db.Set<GitHubAppInstallation>().Where(x => x.Id == expected.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.VerificationUnknown, status == null)
                .SetProperty(x => x.NextReconciliationAt, next).SetProperty(x => x.ReconciliationFailures, failures)
                .SetProperty(x => x.Status, status ?? expected.Status)
                .SetProperty(x => x.LastVerifiedAt, status == null ? expected.LastVerifiedAt : now)
                .SetProperty(x => x.UpdatedAt, now), ct);
        if (status is not null)
        {
            if (status == GitHubAppInstallationStatus.Active && repositories is not null)
                foreach (var grant in grants.Where(x => original.ContainsKey(x.Id)))
                {
                    if (repositories.Contains(grant.ProviderRepositoryId)) { grant.RevokedAt = null; grant.VerifiedAt = now; }
                    else grant.RevokedAt ??= now;
                }
            var unavailable = grants.Where(x => status != GitHubAppInstallationStatus.Active || x.RevokedAt != null).Select(x => x.Id).ToArray();
            if (unavailable.Length > 0)
            {
                var links = await db.ProjectGitRepositories.Where(x => x.CredentialSource == GitRepositoryCredentialSource.GitHubApp &&
                    x.UnlinkedAt == null && x.AccessStatus == ProjectGitRepositoryAccessStatus.Active &&
                    x.GitHubAppRepositoryGrantId != null && unavailable.Contains(x.GitHubAppRepositoryGrantId.Value)).ToListAsync(ct);
                foreach (var link in links)
                {
                    lostLinks.Add(link.Id);
                    link.AccessStatus = ProjectGitRepositoryAccessStatus.AccessLost; link.LastAccessCheckedAt = now;
                    link.UpdatedAt = now; link.Version = Guid.NewGuid();
                    db.ProjectGitRepositoryHistory.Add(new ProjectGitRepositoryHistory { ProjectGitRepositoryId = link.Id,
                        ActorProfileId = link.LinkedByProfileId, Action = ProjectGitRepositoryHistoryAction.AccessLost,
                        Details = "GitHub App installation or repository grant is unavailable." });
                }
                var ids = links.Select(x => x.Id).ToArray();
                var jobs = await db.GitSyncJobs.Where(x => ids.Contains(x.ProjectGitRepositoryId) &&
                    (x.Status == GitSyncJobStatus.Pending || x.Status == GitSyncJobStatus.RetryScheduled)).ToListAsync(ct);
                foreach (var job in jobs)
                {
                    job.Status = GitSyncJobStatus.Cancelled; job.CompletedAt = now; job.NextAttemptAt = null;
                    job.ErrorCode = "GITHUB_REPOSITORY_ACCESS_LOST"; job.ErrorMessage = "GitHub App access is unavailable."; job.UpdatedAt = now;
                }
            }
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        if (status is null) { GitHubAppDiagnostics.ReconciliationDeferred.Add(1); logger?.LogWarning("GitHub App installation {InstallationId} verification deferred until {RetryAt}", expected.ProviderInstallationId, next); }
        if (lostLinks.Count > 0) GitHubAppDiagnostics.AccessLost.Add(lostLinks.Count);
        foreach (var linkId in lostLinks) logger?.LogWarning("GitHub App repository association {AssociationId} lost access for installation {InstallationId}", linkId, expected.ProviderInstallationId);
        db.ChangeTracker.Clear();
        // Also flush on unknown/restoration; a cached token is not fresh authorization evidence.
        tokens.InvalidateInstallation(expected.ProviderInstallationId);
    }

    // An unrestricted installation token is used only to enumerate grants; it is never cached or used for sync.
    public async Task<HashSet<long>?> ReadRepositoriesAsync(long installationId, CancellationToken ct)
    {
        GitHubAppAvailability.Require(appOptions);
        var settings = appOptions.Value;
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.PrivateKeyPem)) return null;
        using var issue = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/app/installations/{installationId}/access_tokens");
        SetHeaders(issue, GitHubAppJwt.Create(settings.ClientId, settings.PrivateKeyPem, DateTimeOffset.UtcNow));
        issue.Content = JsonContent.Create(new { });
        using var issued = await http.SendAsync(issue, ct);
        if (!issued.IsSuccessStatusCode) { RecordRetry(issued); return null; }
        using var tokenDocument = await JsonDocument.ParseAsync(await issued.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!tokenDocument.RootElement.TryGetProperty("token", out var tokenValue) || tokenValue.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(tokenValue.GetString())) return null;
        var token = tokenValue.GetString()!;
        var ids = new HashSet<long>();
        int? total = null;
        for (var page = 1; page <= 1000; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/installation/repositories?per_page=100&page={page}");
            SetHeaders(request, token);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) { RecordRetry(response); return null; }
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = document.RootElement;
            if (!root.TryGetProperty("total_count", out var count) || !count.TryGetInt32(out var actualTotal) || actualTotal < 0 ||
                total is not null && total != actualTotal || !root.TryGetProperty("repositories", out var items) || items.ValueKind != JsonValueKind.Array)
                return null;
            total = actualTotal;
            var pageCount = 0;
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var id) || !id.TryGetInt64(out var value) || value <= 0 || !ids.Add(value)) return null;
                pageCount++;
            }
            if (ids.Count == total) return ids;
            if (ids.Count > total || pageCount == 0 || pageCount < 100) return null;
        }
        return null;
    }

    private static void SetHeaders(HttpRequestMessage request, string bearer)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.UserAgent.ParseAdd("UniNet-GitHub-Integration");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
    }

    public sealed record InstallationProbe(GitHubAppInstallationStatus? Status, bool NotFound, long? AppId, long? OwnerId);

    public async Task<long?> ProbeAppIdentityAsync(CancellationToken ct)
    {
        GitHubAppAvailability.Require(appOptions);
        var settings = appOptions.Value;
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.PrivateKeyPem)) return null;
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/app");
        SetHeaders(request, GitHubAppJwt.Create(settings.ClientId, settings.PrivateKeyPem, DateTimeOffset.UtcNow));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) { RecordRetry(response); return null; }
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return json.RootElement.TryGetProperty("id", out var id) && id.TryGetInt64(out var actual) && actual > 0 ? actual : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { return null; }
    }

    public async Task<GitHubAppInstallationStatus?> CheckInstallationAsync(long installationId, CancellationToken ct) =>
        (await ProbeInstallationAsync(installationId, ct)).Status;

    public async Task<InstallationProbe> ProbeInstallationAsync(long installationId, CancellationToken ct)
    {
        GitHubAppAvailability.Require(appOptions);
        var settings = appOptions.Value;
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.PrivateKeyPem))
            return new(null, false, null, null);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/app/installations/{installationId}");
        SetHeaders(request, GitHubAppJwt.Create(settings.ClientId, settings.PrivateKeyPem, DateTimeOffset.UtcNow));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            RecordRetry(response);
            return new(null, response.StatusCode == HttpStatusCode.NotFound, null, null);
        }
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = json.RootElement;
            if (!root.TryGetProperty("id", out var id) || !id.TryGetInt64(out var actual) || actual != installationId ||
                !root.TryGetProperty("suspended_at", out var suspended)) return new(null, false, null, null);
            var status = suspended.ValueKind switch
            {
                JsonValueKind.Null => GitHubAppInstallationStatus.Active,
                JsonValueKind.String when suspended.TryGetDateTimeOffset(out _) => GitHubAppInstallationStatus.Suspended,
                _ => (GitHubAppInstallationStatus?)null
            };
            long? appId = root.TryGetProperty("app_id", out var app) && app.TryGetInt64(out var a) && a > 0 ? a : null;
            long? ownerId = root.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object &&
                account.TryGetProperty("id", out var owner) && owner.TryGetInt64(out var o) && o > 0 ? o : null;
            return new(status, false, appId, ownerId);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        { return new(null, false, null, null); }
    }

    public DateTimeOffset? ProviderRetryAt => responseRetryAt;

    private void RecordRetry(HttpResponseMessage response)
    {
        var now = DateTimeOffset.UtcNow;
        var retryAt = response.Headers.RetryAfter?.Date;
        if (response.Headers.RetryAfter?.Delta is { } delay && delay > TimeSpan.Zero)
            retryAt = now.Add(delay);
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.Contains("0") &&
            response.Headers.TryGetValues("X-RateLimit-Reset", out var resets) && long.TryParse(resets.FirstOrDefault(), out var seconds))
        {
            try { var reset = DateTimeOffset.FromUnixTimeSeconds(seconds); if (retryAt is null || reset > retryAt) retryAt = reset; }
            catch (ArgumentOutOfRangeException) { }
        }
        if (retryAt is null || retryAt <= now)
            retryAt = now.AddMinutes(response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden ? 1 : 5);
        if (responseRetryAt is null || retryAt > responseRetryAt) responseRetryAt = retryAt;
    }
}
