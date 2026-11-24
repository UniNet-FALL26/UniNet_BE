namespace UniNet.Application.Interfaces;

public sealed record GitHubAppUserToken(string AccessToken, string? RefreshToken, int? ExpiresIn, int? RefreshTokenExpiresIn);
public sealed record GitHubAppUserIdentity(long Id, string Login);
public sealed record GitHubAppUserInstallation(long Id, long OwnerId, string OwnerLogin, string OwnerType, bool Suspended, string? ConfigurationUrl = null);
public sealed record GitHubAppUserRepository(long Id, string FullName, bool IsPrivate, bool CanRead, bool CanAdmin = false,
    long OwnerId = 0, string? OwnerLogin = null, string? Name = null, string? HtmlUrl = null,
    string? Description = null, string? DefaultBranch = null, bool IsArchived = false);
public sealed record GitHubAppUserPage<T>(IReadOnlyList<T> Items, bool HasMore);

public interface IGitHubAppUserClient
{
    Task<GitHubAppUserToken> ExchangeAsync(string code, CancellationToken ct);
    Task<GitHubAppUserIdentity> GetUserAsync(string userToken, CancellationToken ct);
    Task<GitHubAppUserPage<GitHubAppUserInstallation>> GetInstallationsAsync(string userToken, int page, CancellationToken ct);
    Task<GitHubAppUserPage<GitHubAppUserRepository>> GetRepositoriesAsync(string userToken, long installationId, int page, CancellationToken ct);
}
