using UniNet.Domain.Enums;
using System.Text.Json.Serialization;

namespace UniNet.Application.DTOs;

public sealed record StartGitHubOAuthRequest(string ReturnTarget);
public sealed record GitHubOAuthStartResponse(Guid FlowId, string AuthorizationUrl, DateTimeOffset ExpiresAt);
public sealed record CompleteGitHubOAuthRequest(Guid FlowId);
public sealed record GitHubOAuthConnectionResponse(
    Guid Id,
    string ProviderAccountId,
    string Login,
    string? DisplayName,
    string? AvatarUrl,
    string[] GrantedScopes,
    GitHubOAuthConnectionStatus Status,
    DateTimeOffset? AccessTokenExpiresAt,
    DateTimeOffset? RefreshTokenExpiresAt,
    DateTimeOffset ConnectedAt,
    DateTimeOffset UpdatedAt);
public sealed record GitHubOAuthCallbackResult(Uri RedirectUri);
public sealed record GitHubRepositoryResponse(
    long ProviderRepositoryId,
    string OwnerLogin,
    string Name,
    string FullName,
    string HtmlUrl,
    string? Description,
    string? DefaultBranch,
    bool IsPrivate,
    bool IsArchived);
public sealed record ProjectGitRepositoryResponse(
    Guid Id,
    Guid ProjectId,
    GitHubRepositoryResponse Repository,
    ProjectGitRepositoryAccessStatus AccessStatus,
    bool SyncPaused,
    [property: JsonPropertyName("oauthConnectionId")] Guid OAuthConnectionId,
    DateTimeOffset LinkedAt,
    DateTimeOffset? UnlinkedAt,
    DateTimeOffset? LastAccessCheckedAt);
public sealed record LinkGitHubRepositoryRequest(
    long ProviderRepositoryId,
    [property: JsonPropertyName("oauthConnectionId")] Guid OAuthConnectionId);
public sealed record ReplaceGitHubRepositoryCredentialRequest(
    [property: JsonPropertyName("oauthConnectionId")] Guid OAuthConnectionId);
public sealed record GitHubRepositoryPage(IReadOnlyList<GitHubRepositoryResponse> Items, int Page, int PageSize);
public sealed record ProjectGitRepositoryPage(
    IReadOnlyList<ProjectGitRepositoryResponse> Items, int Page, int PageSize, int TotalCount);
public sealed record GrantProjectGitManagerRequest(Guid UserProfileId);
public sealed record ProjectGitManagerResponse(
    Guid Id,
    Guid UserProfileId,
    string FullName,
    string? AvatarUrl,
    DateTimeOffset GrantedAt,
    bool IsActive,
    DateTimeOffset? RevokedAt,
    Guid GrantedByProfileId,
    Guid? RevokedByProfileId);
public sealed record GitSyncCheckpointResponse(
    string Category,
    string ScopeKey,
    string? Cursor,
    int Status,
    long ItemsProcessed,
    DateTimeOffset? LastProcessedAt);
public sealed record GitSyncJobResponse(
    Guid Id,
    Guid ProjectGitRepositoryId,
    GitSyncType SyncType,
    GitSyncJobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? NextAttemptAt,
    int RetryCount,
    string? ErrorCode,
    string? ErrorMessage,
    IReadOnlyList<GitSyncCheckpointResponse> Checkpoints);
public sealed record GitSyncRequestResult(GitSyncJobResponse Job, bool WasAlreadyQueued);
public sealed record GitSyncJobPage(
    IReadOnlyList<GitSyncJobResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);
public sealed record GitSyncSettingsRequest(int SyncIntervalHours, bool? SyncPaused = null);
public sealed record ProjectGitSyncStatusResponse(
    Guid ProjectGitRepositoryId,
    Guid GitHubRepositoryId,
    string RepositoryFullName,
    ProjectGitRepositoryAccessStatus AccessStatus,
    bool SyncPaused,
    int SyncIntervalHours,
    bool RequiresAuthorization,
    DateTimeOffset? LastSuccessfulSyncAt,
    DateTimeOffset? NextScheduledSyncAt,
    GitSyncJobResponse? LatestJob);
public sealed record ProjectGitSyncStatusPage(IReadOnlyList<ProjectGitSyncStatusResponse> Items);
public sealed record GitActivityActorResponse(string Role, string? ExternalAccountId, string? Login);
public sealed record GitActivityResponse(
    Guid Id,
    string ActivityType,
    DateTimeOffset OccurredAt,
    string RepositoryFullName,
    string SourceCategory,
    string ExternalObjectId,
    IReadOnlyList<GitActivityActorResponse> Actors);
public sealed record GitActivityPage(
    IReadOnlyList<GitActivityResponse> Items, int Page, int PageSize, int TotalCount);
