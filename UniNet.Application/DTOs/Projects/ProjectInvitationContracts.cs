using UniNet.Domain.Enums;

namespace UniNet.Application;

// InviteeId identifies a UserProfile, as in ProjectInvitation.
public record CreateProjectInvitationRequest(Guid InviteeId, string Role, string? Message = null);

public record ProjectInvitationResponse(
    Guid Id,
    Guid ProjectId,
    string ProjectTitle,
    Guid InviterId,
    string InviterFullName,
    string? InviterAvatarUrl,
    Guid InviteeId,
    string InviteeFullName,
    string? InviteeAvatarUrl,
    string Role,
    string? Message,
    ProjectInvitationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record ProjectInvitationsResponse(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    List<ProjectInvitationResponse> Invitations);
