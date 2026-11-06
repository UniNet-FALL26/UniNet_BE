using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;

namespace UniNet.API.Services;

public sealed class GitHubProviderClient(HttpClient http, IOptions<GitHubIntegrationOptions> options) : IGitHubProviderClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GitHubIntegrationOptions settings = options.Value;

    public async Task<GitHubOAuthTokens> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        var result = await ExchangeTokenAsync(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = Required(settings.ClientId, "ClientId"),
            ["client_secret"] = Required(settings.ClientSecret, "ClientSecret"),
            ["redirect_uri"] = Required(settings.CallbackUrl, "CallbackUrl")
        }, ct, isRefresh: false);
        return result;
    }

    public Task<GitHubOAuthTokens> RefreshAsync(string refreshToken, CancellationToken ct) =>
        ExchangeTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = Required(settings.ClientId, "ClientId"),
            ["client_secret"] = Required(settings.ClientSecret, "ClientSecret")
        }, ct, isRefresh: true);

    public async Task<GitHubUser> GetAuthenticatedUserAsync(string accessToken, CancellationToken ct)
    {
        using var request = CreateApiRequest(HttpMethod.Get, "user", accessToken);
        var user = await SendJsonAsync<UserPayload>(request, ct);
        if (user.Id <= 0 || string.IsNullOrWhiteSpace(user.Login) || user.Login.Length > 255 ||
            user.Name?.Length > 255 || user.AvatarUrl?.Length > 2048 ||
            user.AvatarUrl is not null &&
            (!Uri.TryCreate(user.AvatarUrl, UriKind.Absolute, out var avatarUri) ||
             avatarUri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(avatarUri.UserInfo)))
            throw new GitHubIntegrationException("GITHUB_INVALID_RESPONSE", "GitHub returned an invalid account response.", 502);
        return new(user.Id, user.Login, user.Name, user.AvatarUrl);
    }

    public async Task<IReadOnlyList<GitHubRemoteRepository>> GetRepositoriesAsync(
        string accessToken, int page, int pageSize, CancellationToken ct)
    {
        var query = $"user/repos?visibility=all&affiliation=owner%2Ccollaborator%2Corganization&sort=full_name&direction=asc&per_page={pageSize}&page={page}";
        using var request = CreateApiRequest(HttpMethod.Get, query, accessToken);
        var repositories = await SendJsonAsync<List<RepositoryPayload>>(request, ct);
        return repositories.Select(MapRepository).ToArray();
    }

    public async Task<GitHubRemoteRepository> GetRepositoryAsync(string accessToken, long repositoryId, CancellationToken ct)
    {
        using var request = CreateApiRequest(HttpMethod.Get, $"repositories/{repositoryId}", accessToken);
        return MapRepository(await SendJsonAsync<RepositoryPayload>(request, ct));
    }

    public async Task<GitHubSyncPage> GetSyncPageAsync(
        string accessToken,
        string repositoryFullName,
        string category,
        string? scope,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        if (page is < 1 or > 10000 || pageSize is < 1 or > 100)
            throw new GitHubIntegrationException("GITHUB_INVALID_PAGINATION", "Invalid synchronization page.", 400);
        var repositoryParts = repositoryFullName.Split('/');
        if (repositoryParts.Length != 2 || repositoryParts.Any(string.IsNullOrWhiteSpace))
            throw new GitHubIntegrationException("GITHUB_INVALID_REPOSITORY", "Invalid repository identifier.", 400);
        var repositoryPath = string.Join('/', repositoryParts.Select(Uri.EscapeDataString));
        var path = category switch
        {
            "branches" when scope is null => $"repos/{repositoryPath}/branches",
            "commits" => $"repos/{repositoryPath}/commits" +
                (string.IsNullOrWhiteSpace(scope) ? "" : $"?sha={Uri.EscapeDataString(scope)}"),
            "pull-requests" when scope is null => $"repos/{repositoryPath}/pulls?state=all",
            "pull-request-reviews" => $"repos/{repositoryPath}/pulls/{RequiredNumber(scope)}/reviews",
            "issues" when scope is null => $"repos/{repositoryPath}/issues?state=all&filter=all",
            "issue-comments" => $"repos/{repositoryPath}/issues/{RequiredNumber(scope)}/comments",
            "pull-request-review-comments" => $"repos/{repositoryPath}/pulls/{RequiredNumber(scope)}/comments",
            "releases" when scope is null => $"repos/{repositoryPath}/releases",
            _ => throw new GitHubIntegrationException("GITHUB_INVALID_CATEGORY", "Unsupported synchronization category.", 400)
        };
        var separator = path.Contains('?') ? '&' : '?';
        using var request = CreateApiRequest(HttpMethod.Get,
            $"{path}{separator}per_page={pageSize}&page={page}", accessToken);
        using var response = await SendApiAsync(request, ct);
        List<JsonElement> items;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new JsonException();
            items = document.RootElement.EnumerateArray().Select(x => x.Clone()).ToList();
        }
        catch (JsonException)
        {
            throw new GitHubIntegrationException("GITHUB_INVALID_RESPONSE", "GitHub returned invalid synchronization data.", 502);
        }

        var hasMore = response.Headers.TryGetValues("Link", out var linkValues) &&
            linkValues.Any(value => value.Split(',').Any(link =>
                link.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase)));
        int? remaining = response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) &&
            int.TryParse(values.FirstOrDefault(), out var parsedRemaining) ? parsedRemaining : null;
        DateTimeOffset? resetAt = response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues) &&
            long.TryParse(resetValues.FirstOrDefault(), out var resetUnix) &&
            resetUnix is >= 0 and <= 253402300799
                ? DateTimeOffset.FromUnixTimeSeconds(resetUnix)
                : null;
        return new(items, hasMore, remaining, resetAt);
    }

    private async Task<GitHubOAuthTokens> ExchangeTokenAsync(
        Dictionary<string, string> values, CancellationToken ct, bool isRefresh)
    {
        EnsureHttpsEndpoint(settings.TokenEndpoint, "TokenEndpoint", "github.com");
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(values)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var payload = await SendJsonAsync<TokenPayload>(request, ct);
        if (!string.IsNullOrWhiteSpace(payload.Error) || string.IsNullOrWhiteSpace(payload.AccessToken))
            throw new GitHubIntegrationException(
                isRefresh ? "GITHUB_REAUTH_REQUIRED" : "GITHUB_OAUTH_FAILED",
                isRefresh ? "GitHub authorization must be renewed." : "GitHub authorization could not be completed.",
                isRefresh ? 401 : 502);
        if (!IsValidToken(payload.AccessToken) ||
            payload.RefreshToken is not null && !IsValidToken(payload.RefreshToken) ||
            payload.Scope?.Length > 1000 ||
            payload.ExpiresIn is 0 or < 0 or > 315360000 ||
            payload.RefreshTokenExpiresIn is 0 or < 0 or > 315360000)
            throw new GitHubIntegrationException("GITHUB_INVALID_RESPONSE", "GitHub returned invalid authorization data.", 502);
        return new(payload.AccessToken, payload.RefreshToken, payload.ExpiresIn,
            payload.RefreshTokenExpiresIn, payload.Scope ?? "");
    }

    private HttpRequestMessage CreateApiRequest(HttpMethod method, string path, string accessToken)
    {
        EnsureHttpsEndpoint(settings.ApiBaseUrl, "ApiBaseUrl", "api.github.com");
        var baseUri = new Uri(settings.ApiBaseUrl.EndsWith('/') ? settings.ApiBaseUrl : settings.ApiBaseUrl + '/', UriKind.Absolute);
        var request = new HttpRequestMessage(method, new Uri(baseUri, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("UniNet-Backend");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private async Task<T> SendJsonAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await SendApiAsync(request, ct);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
                ?? throw new GitHubIntegrationException("GITHUB_INVALID_RESPONSE", "GitHub returned an invalid response.", 502);
        }
        catch (JsonException)
        {
            throw new GitHubIntegrationException("GITHUB_INVALID_RESPONSE", "GitHub returned an invalid response.", 502);
        }
    }

    private async Task<HttpResponseMessage> SendApiAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException)
        {
            throw new GitHubIntegrationException("GITHUB_UNAVAILABLE", "GitHub is temporarily unavailable.", 502);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new GitHubIntegrationException("GITHUB_UNAVAILABLE", "GitHub request timed out.", 502);
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            var rateLimited = response.StatusCode == HttpStatusCode.TooManyRequests ||
                response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) &&
                remaining.FirstOrDefault() == "0" ||
                response.Headers.RetryAfter is not null;
            if (rateLimited)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta;
                if (retryAfter is null && response.Headers.RetryAfter?.Date is { } retryDate)
                    retryAfter = retryDate - DateTimeOffset.UtcNow;
                if (retryAfter is null && response.Headers.TryGetValues("X-RateLimit-Reset", out var reset) &&
                    long.TryParse(reset.FirstOrDefault(), out var resetUnix) &&
                    resetUnix is >= 0 and <= 253402300799)
                    retryAfter = DateTimeOffset.FromUnixTimeSeconds(resetUnix) - DateTimeOffset.UtcNow;
                response.Dispose();
                throw new GitHubIntegrationException("GITHUB_RATE_LIMITED", "GitHub rate limit reached.",
                    429, retryAfter is { } delay && delay > TimeSpan.Zero ? delay : null);
            }
        }
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            throw new GitHubIntegrationException("GITHUB_CREDENTIAL_INVALID", "GitHub authorization is no longer valid.", 401);
        }
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            response.Dispose();
            throw new GitHubIntegrationException("GITHUB_ACCESS_DENIED", "GitHub denied access to the requested resource.", 403);
        }
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            throw new GitHubIntegrationException("GITHUB_RESOURCE_UNAVAILABLE", "The GitHub resource is unavailable to this connection.", 404);
        }
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new GitHubIntegrationException("GITHUB_REQUEST_FAILED", "GitHub could not complete the request.", 502);
        }
        return response;
    }

    private static string RequiredNumber(string? scope) =>
        long.TryParse(scope, out var number) && number > 0
            ? number.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : throw new GitHubIntegrationException("GITHUB_INVALID_SCOPE", "A numeric parent item is required.", 400);

    private static GitHubRemoteRepository MapRepository(RepositoryPayload repository)
    {
        if (repository.Id <= 0 || repository.Owner is null || repository.Owner.Id <= 0 ||
            string.IsNullOrWhiteSpace(repository.Owner.Login) || repository.Owner.Login.Length > 255 ||
            string.IsNullOrWhiteSpace(repository.Name) || repository.Name.Length > 255 ||
            string.IsNullOrWhiteSpace(repository.FullName) || repository.FullName.Length > 511 ||
            repository.Description?.Length > 2000 || repository.DefaultBranch?.Length > 255 ||
            string.IsNullOrWhiteSpace(repository.HtmlUrl) || repository.HtmlUrl.Length > 2048 ||
            !Uri.TryCreate(repository.HtmlUrl, UriKind.Absolute, out var htmlUrl) ||
            htmlUrl.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(htmlUrl.IdnHost, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(htmlUrl.UserInfo))
            throw new GitHubIntegrationException("GITHUB_INVALID_RESPONSE", "GitHub returned an invalid repository response.", 502);
        return new(repository.Id, repository.Owner.Id, repository.Owner.Login, repository.Name, repository.FullName,
            repository.HtmlUrl, repository.Description, repository.DefaultBranch, repository.Private, repository.Archived,
            !repository.Private || repository.Permissions?.Pull == true);
    }

    private static string Required(string? value, string name) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw new GitHubIntegrationException(
            "GITHUB_NOT_CONFIGURED", $"GitHub OAuth setting '{name}' is not configured.", 503);

    private static bool IsValidToken(string token) =>
        token.Length is > 0 and <= 8192 && token.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~' or '+' or '/' or '=');

    private static void EnsureHttpsEndpoint(string? value, string name, string allowedHost)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.IdnHost, allowedHost, StringComparison.OrdinalIgnoreCase))
            throw new GitHubIntegrationException("GITHUB_NOT_CONFIGURED",
                $"GitHub OAuth setting '{name}' must use the official {allowedHost} HTTPS host.", 503);
    }

    private sealed record TokenPayload(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string? AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int? ExpiresIn,
        [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token_expires_in")] int? RefreshTokenExpiresIn,
        [property: System.Text.Json.Serialization.JsonPropertyName("scope")] string? Scope,
        [property: System.Text.Json.Serialization.JsonPropertyName("error")] string? Error);

    private sealed record UserPayload(
        long Id,
        string Login,
        string? Name,
        [property: System.Text.Json.Serialization.JsonPropertyName("avatar_url")] string? AvatarUrl);

    private sealed record RepositoryOwner(long Id, string Login);

    private sealed record RepositoryPayload(
        long Id,
        string Name,
        [property: System.Text.Json.Serialization.JsonPropertyName("full_name")] string FullName,
        [property: System.Text.Json.Serialization.JsonPropertyName("html_url")] string HtmlUrl,
        [property: System.Text.Json.Serialization.JsonPropertyName("private")] bool Private,
        bool Archived,
        RepositoryOwner? Owner,
        string? Description,
        [property: System.Text.Json.Serialization.JsonPropertyName("default_branch")] string? DefaultBranch,
        RepositoryPermissions? Permissions);

    private sealed record RepositoryPermissions(bool? Pull);
}
