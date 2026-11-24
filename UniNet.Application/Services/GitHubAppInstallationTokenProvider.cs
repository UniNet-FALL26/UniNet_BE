using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;

namespace UniNet.Application.Services;

/// <summary>Issues repository-scoped installation tokens for trusted backend callers only.</summary>
public sealed class GitHubAppInstallationTokenProvider(
    HttpClient http, IOptions<GitHubAppOptions> options, TimeProvider clock) : IGitHubAppInstallationTokenProvider, IDisposable
{
    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAt);
    private readonly Dictionary<(long Installation, long Repository), CachedToken> cache = [];
    private readonly Dictionary<long, long> generations = [];
    private readonly SemaphoreSlim issuance = new(1, 1);
    private readonly object sync = new();
    private static readonly TimeSpan SafetyWindow = TimeSpan.FromMinutes(1);

    // This method does not authorize the caller or verify a grant. Call only after those checks.
    public async Task<string> GetAsync(long installationId, long repositoryId, CancellationToken ct)
    {
        GitHubAppAvailability.Require(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(installationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(repositoryId);
        await issuance.WaitAsync(ct);
        try
        {
            long generation;
            lock (sync)
            {
                if (cache.TryGetValue((installationId, repositoryId), out var hit) &&
                    hit.ExpiresAt > clock.GetUtcNow().Add(SafetyWindow)) return hit.Token;
                cache.Remove((installationId, repositoryId));
                generation = generations.GetValueOrDefault(installationId);
            }

            var settings = options.Value;
            if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.PrivateKeyPem))
                throw Error("GITHUB_APP_NOT_CONFIGURED", "GitHub App credentials are not configured.", 503);
            var jwt = GitHubAppJwt.Create(settings.ClientId, settings.PrivateKeyPem, clock.GetUtcNow());
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://api.github.com/app/installations/{installationId}/access_tokens");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            request.Headers.UserAgent.ParseAdd("UniNet-GitHub-Integration");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            request.Content = JsonContent.Create(new { repository_ids = new[] { repositoryId } });
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                var rateLimited = response.StatusCode == HttpStatusCode.TooManyRequests ||
                    response.StatusCode == HttpStatusCode.Forbidden &&
                    (response.Headers.RetryAfter is not null || response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.Contains("0"));
                if (rateLimited)
                {
                    var delay = response.Headers.RetryAfter?.Delta;
                    if (delay is null && response.Headers.RetryAfter?.Date is { } retryAt)
                        delay = retryAt - clock.GetUtcNow();
                    if (delay is { } wait && wait < TimeSpan.Zero) delay = TimeSpan.Zero;
                    throw new GitHubIntegrationException("GITHUB_RATE_LIMITED", "GitHub rate limit reached.", 429, delay);
                }
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    throw Error("GITHUB_APP_AUTH_INVALID", "GitHub App authentication failed.", 503);
                if (response.StatusCode == HttpStatusCode.Forbidden)
                    throw Error("GITHUB_APP_ACCESS_DENIED", "GitHub denied installation access.", 403);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    throw Error("GITHUB_APP_INSTALLATION_UNAVAILABLE", "GitHub installation or repository is unavailable.", 404);
                throw Error("GITHUB_APP_TOKEN_REQUEST_FAILED", "GitHub could not issue an installation token.", 502);
            }

            string? token;
            DateTimeOffset expiry;
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var root = document.RootElement;
                token = root.GetProperty("token").GetString();
                expiry = root.GetProperty("expires_at").GetDateTimeOffset();
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                throw Error("GITHUB_APP_TOKEN_RESPONSE_INVALID", "GitHub returned an invalid installation token response.", 502);
            }
            if (string.IsNullOrWhiteSpace(token) || expiry <= clock.GetUtcNow().Add(SafetyWindow))
                throw Error("GITHUB_APP_TOKEN_RESPONSE_INVALID", "GitHub returned an invalid installation token response.", 502);
            lock (sync)
            {
                if (generations.GetValueOrDefault(installationId) != generation)
                    throw Error("GITHUB_APP_INSTALLATION_CHANGED", "Installation access changed during token issuance.", 409);
                if (cache.Count >= 1024) cache.Remove(cache.MinBy(x => x.Value.ExpiresAt).Key);
                cache[(installationId, repositoryId)] = new(token, expiry);
            }
            return token;
        }
        finally { issuance.Release(); }
    }

    public void InvalidateInstallation(long installationId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(installationId);
        lock (sync)
        {
            generations[installationId] = generations.GetValueOrDefault(installationId) + 1;
            foreach (var key in cache.Keys.Where(x => x.Installation == installationId).ToArray()) cache.Remove(key);
        }
    }

    public void Dispose() => issuance.Dispose();
    private static GitHubIntegrationException Error(string code, string message, int status) => new(code, message, status);
}
