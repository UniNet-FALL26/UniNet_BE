using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class GitHubOAuthAuthorizationFlow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public string StateHash { get; set; } = null!;
    public string ReturnTarget { get; set; } = null!;
    public GitHubOAuthFlowStatus Status { get; set; } = GitHubOAuthFlowStatus.AwaitingCallback;
    public string? PendingProviderAccountId { get; set; }
    public string? PendingLogin { get; set; }
    public string? PendingDisplayName { get; set; }
    public string? PendingAvatarUrl { get; set; }
    public string? PendingAccessTokenEncrypted { get; set; }
    public string? PendingRefreshTokenEncrypted { get; set; }
    public string? PendingGrantedScopes { get; set; }
    public DateTimeOffset? PendingAccessTokenExpiresAt { get; set; }
    public DateTimeOffset? PendingRefreshTokenExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
