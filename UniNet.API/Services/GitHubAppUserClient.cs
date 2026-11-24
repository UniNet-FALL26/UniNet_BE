using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;

namespace UniNet.API.Services;

/// <summary>GitHub App user-token calls. These methods do not authorize a UniNet project.</summary>
public sealed class GitHubAppUserClient(HttpClient http, IOptions<GitHubAppOptions> options) : IGitHubAppUserClient
{
    public async Task<GitHubAppUserToken> ExchangeAsync(string code, CancellationToken ct)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret) ||
            !Uri.TryCreate(settings.CallbackUrl, UriKind.Absolute, out var callback) ||
            (callback.Scheme != Uri.UriSchemeHttps && !(callback.Scheme == Uri.UriSchemeHttp && callback.IsLoopback)) ||
            callback.UserInfo.Length != 0 || callback.Fragment.Length != 0 || callback.Query.Length != 0)
            throw Error("GITHUB_APP_NOT_CONFIGURED", "GitHub App user authorization is not configured.", 503);
        if (string.IsNullOrWhiteSpace(code) || code.Length > 4096) throw Error("GITHUB_APP_CODE_INVALID", "Invalid authorization code.", 400);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token");
        request.Headers.Accept.ParseAdd("application/json");
        request.Content = JsonContent.Create(new { client_id = settings.ClientId, client_secret = settings.ClientSecret, code, redirect_uri = settings.CallbackUrl });
        using var json = await SendAsync(request, ct);
        try
        {
            var root = json.RootElement;
            var token = root.GetProperty("access_token").GetString();
            if (string.IsNullOrWhiteSpace(token)) throw new JsonException();
            return new(token, OptionalString(root, "refresh_token"), OptionalInt(root, "expires_in"), OptionalInt(root, "refresh_token_expires_in"));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { throw Error("GITHUB_APP_INVALID_RESPONSE", "GitHub returned an invalid authorization response.", 502); }
    }

    public async Task<GitHubAppUserIdentity> GetUserAsync(string userToken, CancellationToken ct)
    {
        using var request = Api("https://api.github.com/user", userToken);
        using var json = await SendAsync(request, ct);
        try { var root = json.RootElement; return new(PositiveId(root, "id"), RequiredString(root, "login")); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { throw Error("GITHUB_APP_INVALID_RESPONSE", "GitHub returned an invalid user response.", 502); }
    }

    public async Task<GitHubAppUserPage<GitHubAppUserInstallation>> GetInstallationsAsync(string userToken, int page, CancellationToken ct)
    {
        ValidatePage(page);
        using var request = Api($"https://api.github.com/user/installations?per_page=100&page={page}", userToken);
        using var json = await SendAsync(request, ct);
        try
        {
            var items = json.RootElement.GetProperty("installations").EnumerateArray().Select(i =>
            {
                var owner = i.GetProperty("account");
                var type = RequiredString(owner, "type");
                if (type is not ("User" or "Organization")) throw new JsonException();
                var id = PositiveId(i, "id");
                return new GitHubAppUserInstallation(id, PositiveId(owner, "id"),
                    RequiredString(owner, "login"), type, i.TryGetProperty("suspended_at", out var suspended) && suspended.ValueKind != JsonValueKind.Null,
                    ConfigurationUrl(i, id));
            }).ToArray();
            return new(items, items.Length == 100);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { throw Error("GITHUB_APP_INVALID_RESPONSE", "GitHub returned invalid installation data.", 502); }
    }

    public async Task<GitHubAppUserPage<GitHubAppUserRepository>> GetRepositoriesAsync(string userToken, long installationId, int page, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(installationId);
        ValidatePage(page);
        using var request = Api($"https://api.github.com/user/installations/{installationId}/repositories?per_page=100&page={page}", userToken);
        using var json = await SendAsync(request, ct);
        try
        {
            var items = json.RootElement.GetProperty("repositories").EnumerateArray().Select(r =>
                new GitHubAppUserRepository(PositiveId(r, "id"), RequiredString(r, "full_name"), r.GetProperty("private").GetBoolean(),
                    r.TryGetProperty("permissions", out var p) && p.TryGetProperty("pull", out var pull) && pull.ValueKind == JsonValueKind.True,
                    r.TryGetProperty("permissions", out var adminPermissions) && adminPermissions.TryGetProperty("admin", out var admin) && admin.ValueKind == JsonValueKind.True,
                    PositiveId(r.GetProperty("owner"), "id"), RequiredString(r.GetProperty("owner"), "login"), RequiredString(r, "name"), RequiredString(r, "html_url"),
                    OptionalString(r, "description"), OptionalString(r, "default_branch"), r.TryGetProperty("archived", out var archived) && archived.ValueKind == JsonValueKind.True)).ToArray();
            return new(items, items.Length == 100);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { throw Error("GITHUB_APP_INVALID_RESPONSE", "GitHub returned invalid repository data.", 502); }
    }

    private static HttpRequestMessage Api(string url, string token)
    {
        if (string.IsNullOrWhiteSpace(token)) throw Error("GITHUB_APP_USER_AUTH_REQUIRED", "GitHub App user authorization is required.", 401);
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        GitHubAppAvailability.Require(options);
        request.Headers.UserAgent.ParseAdd("UniNet-GitHub-Integration");
        if (request.Headers.Accept.Count == 0) request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode switch { HttpStatusCode.Unauthorized => 401, HttpStatusCode.NotFound => 404,
                HttpStatusCode.Forbidden => 403, HttpStatusCode.TooManyRequests => 429, _ => 502 };
            throw Error("GITHUB_APP_PROVIDER_ERROR", "GitHub App request failed.", status);
        }
        try { await using var stream = await response.Content.ReadAsStreamAsync(ct); return await JsonDocument.ParseAsync(stream, cancellationToken: ct); }
        catch (JsonException) { throw Error("GITHUB_APP_INVALID_RESPONSE", "GitHub returned invalid JSON.", 502); }
    }
    private static void ValidatePage(int page) { if (page is < 1 or > 1000) throw Error("GITHUB_APP_INVALID_PAGE", "Invalid page.", 400); }
    private static string? ConfigurationUrl(JsonElement item, long installationId)
    {
        if (!item.TryGetProperty("html_url", out var value) || value.ValueKind != JsonValueKind.String) return null;
        var raw = value.GetString();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) || uri.Port != 443 ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) return null;
        var id = installationId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath,
            @"^/(?:settings/installations|organizations/[^/]+/settings/installations)/" + id + @"/?$") ? raw : null;
    }
    private static long PositiveId(JsonElement element, string property) { var id = element.GetProperty(property).GetInt64(); return id > 0 ? id : throw new JsonException(); }
    private static string RequiredString(JsonElement element, string property) => element.GetProperty(property).GetString() is { Length: > 0 } value ? value : throw new JsonException();
    private static string? OptionalString(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
    private static int? OptionalInt(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetInt32() : null;
    private static GitHubIntegrationException Error(string code, string message, int status) => new(code, message, status);
}
