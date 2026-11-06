using System.Text.Json;

namespace UniNet.Application.Interfaces;

public sealed record GitHubOAuthTokens(
    string AccessToken,
    string? RefreshToken,
    int? ExpiresInSeconds,
    int? RefreshTokenExpiresInSeconds,
    string GrantedScopes);

public sealed record GitHubUser(long Id, string Login, string? Name, string? AvatarUrl);

public sealed record GitHubRemoteRepository(
    long Id,
    long OwnerId,
    string OwnerLogin,
    string Name,
    string FullName,
    string HtmlUrl,
    string? Description,
    string? DefaultBranch,
    bool IsPrivate,
    bool IsArchived,
    bool CanRead);

public sealed record GitHubSyncPage(
    IReadOnlyList<JsonElement> Items,
    bool HasMore,
    int? RateLimitRemaining,
    DateTimeOffset? RateLimitResetAt);

public interface IGitHubProviderClient
{
    Task<GitHubOAuthTokens> ExchangeCodeAsync(string code, CancellationToken ct);
    Task<GitHubOAuthTokens> RefreshAsync(string refreshToken, CancellationToken ct);
    Task<GitHubUser> GetAuthenticatedUserAsync(string accessToken, CancellationToken ct);
    Task<IReadOnlyList<GitHubRemoteRepository>> GetRepositoriesAsync(string accessToken, int page, int pageSize, CancellationToken ct);
    Task<GitHubRemoteRepository> GetRepositoryAsync(string accessToken, long repositoryId, CancellationToken ct);
    Task<GitHubSyncPage> GetSyncPageAsync(
        string accessToken, string repositoryFullName, string category, string? scope,
        int page, int pageSize, CancellationToken ct);
}
