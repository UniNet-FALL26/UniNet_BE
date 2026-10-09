using UniNet.Domain.Enums;

namespace UniNet.Application;

public record RecommendedProjectMemberResponse(
    Guid UserId,
    string FullName,
    string? AvatarUrl,
    double MatchScore,
    List<string> MatchedSkills,
    List<string> MissingSkills,
    double SkillMatch,
    double DiversityScore,
    double CollaborationCount,
    double FinalScore);

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
