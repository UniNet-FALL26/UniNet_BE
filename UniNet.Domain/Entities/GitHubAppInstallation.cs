namespace UniNet.Domain.Entities;

public enum GitHubAppInstallationStatus : short { Active = 0, Suspended = 1, Uninstalled = 2 }
public enum GitHubAppAuthorizationStatus : short { Pending = 0, Completed = 1, Failed = 2, Processing = 3 }

public sealed class GitHubAppInstallation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long ProviderInstallationId { get; set; }
    public long OwnerAccountId { get; set; }
    public string OwnerLogin { get; set; } = null!;
    public string OwnerType { get; set; } = null!;
    public GitHubAppInstallationStatus Status { get; set; }
    public long AuthorizationGeneration { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastVerifiedAt { get; set; }
    public bool VerificationUnknown { get; set; }
    public DateTimeOffset? NextReconciliationAt { get; set; }
    public int ReconciliationFailures { get; set; }
}

public sealed class GitHubAppInstallationAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstallationId { get; set; }
    public Guid AccountId { get; set; }
    public long GitHubUserId { get; set; }
    public DateTimeOffset VerifiedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class GitHubAppRepositoryGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstallationId { get; set; }
    public long ProviderRepositoryId { get; set; }
    public long AuthorizationGeneration { get; set; } = 1;
    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset VerifiedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class GitHubAppAuthorizationFlow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid OAuthConnectionId { get; set; }
    public string StateHash { get; set; } = null!;
    public string ReturnTarget { get; set; } = null!;
    public GitHubAppAuthorizationStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class GitHubAppUserCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public long GitHubUserId { get; set; }
    public string Login { get; set; } = null!;
    public string AccessTokenEncrypted { get; set; } = null!;
    public string? RefreshTokenEncrypted { get; set; }
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }
    public DateTimeOffset? RefreshTokenExpiresAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
