using UniNet.Domain.Enums;

namespace UniNet.Application.DTOs;

public sealed record LinkProjectGitIdentityRequest(Guid ConnectionId);
public sealed record ReactivateProjectGitIdentityRequest(Guid ConnectionId);
public sealed record ProjectGitMemberIdentityResponse(
    Guid Id,
    Guid ProjectId,
    Guid UserProfileId,
    string MemberDisplayName,
    string Provider,
    long ExternalAccountId,
    string ExternalUsername,
    ProjectGitMemberIdentityStatus Status,
    DateTimeOffset LinkedAt,
    DateTimeOffset? UnlinkedAt,
    DateTimeOffset LastActivatedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
public sealed record ProjectGitMemberIdentityPage(
    IReadOnlyList<ProjectGitMemberIdentityResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);
public sealed record ProjectGitMemberIdentityHistoryResponse(
    Guid Id,
    Guid IdentityId,
    Guid ProjectId,
    Guid UserProfileId,
    string Provider,
    long ExternalAccountId,
    string ExternalUsername,
    string? PreviousExternalUsername,
    ProjectGitMemberIdentityHistoryAction Action,
    ProjectGitMemberIdentityStatus? PreviousStatus,
    ProjectGitMemberIdentityStatus? NewStatus,
    Guid PerformedByProfileId,
    Guid? VerifiedByConnectionId,
    DateTimeOffset OccurredAt);
public sealed record ProjectGitMemberIdentityHistoryPage(
    IReadOnlyList<ProjectGitMemberIdentityHistoryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);
