using UniNet.Domain.Enums;

namespace UniNet.Application;

public record CreateProjectJoinRequestRequest(string Role, string? Message = null);

public record ProjectJoinRequestResponse(
    Guid Id,
    Guid ProjectId,
    string ProjectTitle,
    Guid UserId,
    string FullName,
    string? AvatarUrl,
    string Role,
    string? Message,
    ProjectJoinRequestStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record ProjectJoinRequestsResponse(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    List<ProjectJoinRequestResponse> Requests);
