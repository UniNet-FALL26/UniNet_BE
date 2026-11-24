namespace UniNet.Application.DTOs;

public sealed record GitHubAppInstallationItem(long InstallationId, string OwnerLogin, string OwnerType, string Status, string? ConfigurationUrl = null);
public sealed record GitHubAppCredentialItem(Guid CredentialId, long GitHubUserId, string Login, DateTimeOffset? AccessTokenExpiresAt, bool RequiresReauthorization);
public sealed record GitHubAppRepositoryItem(long RepositoryId, string FullName, bool IsPrivate, bool Eligible, string? IneligibilityReason);
public sealed record GitHubAppGrantPage<T>(IReadOnlyList<T> Items, int Page, bool HasMore);
