using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class GitHubOAuthConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public string ProviderAccountId { get; set; } = null!;
    public string Login { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string AccessTokenEncrypted { get; set; } = null!;
    public string? RefreshTokenEncrypted { get; set; }
    public string GrantedScopes { get; set; } = "";
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }
    public DateTimeOffset? RefreshTokenExpiresAt { get; set; }
    public GitHubOAuthConnectionStatus Status { get; set; } = GitHubOAuthConnectionStatus.Active;
    public DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
