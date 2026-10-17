using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using UniNet.Application.DTOs.Projects;
using UniNet.Application.Interfaces;

namespace UniNet.Application.Services;

// Sends URLs to Google's fixed lookup endpoint. Never requests or follows an evidence URL.
public sealed class GoogleSafeBrowsingUrlChecker(HttpClient http, IConfiguration configuration, IMemoryCache cache) : IEvidenceUrlChecker
{
    public async Task<EvidenceUrlVerdict> CheckAsync(string url, EvidencePolicySnapshot policy, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") ||
            string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new VerificationAiException("URL cannot be checked by Safe Browsing.", false, "URL_NOT_CHECKABLE");
        var key = configuration["SafeBrowsing:ApiKey"];
        if (string.IsNullOrWhiteSpace(key)) throw new VerificationAiException("Safe Browsing is not configured.", false, "URL_NOT_CONFIGURED");
        if (policy.ThreatTypes.Length == 0 || policy.PlatformTypes.Length == 0)
            throw new VerificationAiException("Safe Browsing threat/platform lists must be configured.", false, "URL_POLICY_NOT_CONFIGURED");
        var cacheKey = "evidence-url:" + EvidenceModerationSource.Hash(JsonSerializer.Serialize(new { url, policy.ThreatTypes, policy.PlatformTypes }));
        if (cache.TryGetValue<EvidenceUrlVerdict>(cacheKey, out var cached)) return cached!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://safebrowsing.googleapis.com/v4/threatMatches:find");
        request.Headers.Add("x-goog-api-key", key);
        request.Content = JsonContent.Create(new
        {
            client = new { clientId = "uninet", clientVersion = "1.0" },
            threatInfo = new { threatTypes = policy.ThreatTypes, platformTypes = policy.PlatformTypes,
                threatEntryTypes = new[] { "URL" }, threatEntries = new[] { new { url } } }
        });
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30)); // Same transport timeout as the existing AI provider.
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new VerificationAiException($"Safe Browsing HTTP {(int)response.StatusCode}.",
                    (int)response.StatusCode is 408 or 429 || (int)response.StatusCode >= 500, "URL_PROVIDER_ERROR");
            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            using var body = JsonDocument.Parse(json);
            if (body.RootElement.ValueKind != JsonValueKind.Object || body.RootElement.TryGetProperty("error", out _))
                throw new VerificationAiException("Invalid Safe Browsing response.", true, "URL_INVALID_RESPONSE");
            if (!body.RootElement.TryGetProperty("matches", out _) && body.RootElement.EnumerateObject().Any())
                throw new VerificationAiException("Unrecognized Safe Browsing response.", true, "URL_INVALID_RESPONSE");
            var reasons = new List<string>(); var durations = new List<TimeSpan>();
            if (body.RootElement.TryGetProperty("matches", out var matches))
            {
                foreach (var match in matches.EnumerateArray())
                {
                    if (match.GetProperty("threat").GetProperty("url").GetString() != url ||
                        !policy.ThreatTypes.Contains(match.GetProperty("threatType").GetString()) ||
                        match.GetProperty("threatEntryType").GetString() != "URL" ||
                        string.IsNullOrWhiteSpace(match.GetProperty("platformType").GetString()))
                        throw new VerificationAiException("Safe Browsing match does not identify the requested URL/list.", true, "URL_INVALID_RESPONSE");
                    reasons.Add(match.GetProperty("threatType").GetString()!);
                    if (match.TryGetProperty("cacheDuration", out var duration) && duration.GetString() is string value &&
                        value.EndsWith('s') && double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
                        double.IsFinite(seconds) && seconds > 0 && seconds < TimeSpan.MaxValue.TotalSeconds)
                        durations.Add(TimeSpan.FromSeconds(seconds));
                }
            }
            var result = new EvidenceUrlVerdict(reasons.Count == 0,
                reasons.Count == 0 ? "No match in configured Google Safe Browsing lists; file content was not inspected."
                    : "Google Safe Browsing threat match: " + string.Join(", ", reasons.Distinct()), json);
            // Honor Google's positive-match cache duration; no invented TTL for a negative response.
            if (reasons.Count > 0 && durations.Count == reasons.Count) cache.Set(cacheKey, result, durations.Min());
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new VerificationAiException("Safe Browsing timed out.", true, "URL_TIMEOUT"); }
        catch (HttpRequestException) { throw new VerificationAiException("Safe Browsing connection failed.", true, "URL_CONNECTION_ERROR"); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new VerificationAiException("Malformed Safe Browsing response.", true, "URL_INVALID_RESPONSE"); }
    }
}
