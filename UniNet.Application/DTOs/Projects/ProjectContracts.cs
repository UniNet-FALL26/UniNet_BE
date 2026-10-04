using UniNet.Domain.Enums;

namespace UniNet.Application;

// ============ CREATE PROJECT ============
public record CreateProjectRequest(
    string Title,
    string ProjectField,
    string Description,
    int MemberTarget,
    DateTimeOffset RecruitmentDeadline,
    DateTimeOffset? ExpectedOutput,
    ProjectVisibility Visibility,
    List<string>? RoleRequirements = null,
    List<Guid>? SkillIds = null
    
);


// ============ UPDATE PROJECT ============
public record UpdateProjectRequest(
    string? Title = null,
    string? ProjectField = null,
    string? Description = null,
    int? MemberTarget = null,
    DateTimeOffset? RecruitmentDeadline = null,
    DateTimeOffset? ExpectedOutput = null,
    ProjectVisibility? Visibility = null,
    List<string>? RoleRequirements = null,
    List<Guid>? SkillIds = null
   
);

// ============ PROJECT DETAIL RESPONSE ============
public record ProjectDetailResponse(
    Guid Id,
    string Title,
    string ProjectField,
    string Description,
    int MemberTarget,
    int CurrentMemberCount,
    DateTimeOffset RecruitmentDeadline,
    DateTimeOffset? ExpectedOutput,
    ProjectVisibility Visibility,
    ProjectStatus Status,
    RecruitmentStatus RecruitmentStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    CreatorInfo Creator,
    List<RoleRequirementInfo> RoleRequirements,
    List<ProjectSkillInfo> Skills,
    bool IsCreator,
    bool IsMember,
    List<ProjectMemberInfo>? Members = null
    );

public record CreatorInfo(
    Guid Id,
    string FullName,
    string Nickname,
    string? AvatarUrl
);

public record RoleRequirementInfo(
    Guid Id,
    string RoleName,
    int Quantity
);

public record ProjectSkillInfo(
    Guid SkillId,
    string SkillName,
    string Category,
    string? IconUrl,
    int? MinLevel
);



// ============ PROJECT LIST RESPONSE ============
public record ProjectListItemResponse(
    Guid Id,
    string Title,
    string ProjectField,
    string Description,
    int MemberTarget,
    int CurrentMemberCount,
    DateTimeOffset RecruitmentDeadline,
    ProjectVisibility Visibility,
    ProjectStatus Status,
    RecruitmentStatus RecruitmentStatus,
    DateTimeOffset CreatedAt,
    CreatorInfo Creator
);

// ============ PROJECT MEMBER RESPONSE ============
public record ProjectMemberInfo(
    Guid UserId,
    string FullName,
    string Nickname,
    string? AvatarUrl,
    string Role,
    ProjectMemberStatus Status,
    DateTimeOffset JoinedAt
);

public record ProjectMembersListResponse(
    Guid ProjectId,
    string ProjectTitle,
    int TotalMembers,
    List<ProjectMemberInfo> Members
);

// ============ DISCOVERY RESPONSE ============
public record DiscoveryProjectResponse(
    Guid Id,
    string Title,
    string ProjectField,
    string Description,
    int MemberTarget,
    int CurrentMemberCount,
    DateTimeOffset RecruitmentDeadline,
    ProjectVisibility Visibility,
    RecruitmentStatus RecruitmentStatus,
    DateTimeOffset CreatedAt,
    CreatorInfo Creator,
    List<ProjectSkillInfo> Skills
);

public record DiscoveryProjectsResponse(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    List<DiscoveryProjectResponse> Projects
);
