using UniNet.Domain.Enums;

namespace UniNet.Application;

public record ProjectMemberResponse(
    Guid Id,
    Guid ProjectId,
    string ProjectTitle,
    Guid UserId,
    string FullName,
    string Nickname,
    string? AvatarUrl,
    string Role,
    ProjectMemberStatus Status,
    DateTimeOffset JoinedAt,
    DateTimeOffset? LeftAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
