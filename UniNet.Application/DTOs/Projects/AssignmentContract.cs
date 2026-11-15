using UniNet.Domain.Enums;

namespace UniNet.Application;

public record CreateAssignmentRequest(Guid ProjectVerificationId, Guid AssignedToUserId);
public record ReplaceVerificatorRequest(Guid AssignedToUserId, string Reason);
public enum AssignmentAction { StartMeeting, EndMeeting, Complete }
public record UpdateAssignmentRequest(AssignmentAction Action);
public record AssignmentResponse(Guid Id, Guid ProjectVerificationId, Guid AssignedToUserId,
    Guid AdminId, VerificationAssignmentStatus Status, DateTimeOffset AssignedAt,
    DateTimeOffset? AssignmentDueAt, DateTimeOffset? ReportDueAt, DateTimeOffset? ReplacedAt,
    string? ReplacementReason, Guid? ReplacedById, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? ScheduledStartAt, DateTimeOffset? ScheduledEndAt,
    DateTimeOffset? MeetingStartAt, DateTimeOffset? MeetingEndAt, DateTimeOffset? CompletedAt = null);
public record AssignmentProjectResponse(Guid Id, string Title, string ProjectField, string Description,
    ProjectStatus Status, Guid? LeaderId, List<ProjectSkillInfo> Skills, List<ProjectMemberInfo> Members,
    ProjectLeaderInfo? Leader, ProjectOrigin Origin = ProjectOrigin.Internal, Guid? OwnerUserId = null,
    UniNet.Application.DTOs.Projects.ExternalProjectDraftData? ExternalDetails = null,
    SpecializationResponse? Specialization = null);
public record AssignmentAssigneeResponse(Guid UserId, string FullName, string? AvatarUrl, string Nickname);
public record AssignmentDetailResponse(AssignmentResponse Assignment, AssignmentProjectResponse Project,
    VerificationReportResponse? Report,
    AssessmentRubric? AssessmentRubric = null, AssignmentAssigneeResponse? AssignedToUser = null);
public record AssignmentsResponse(int Page, int PageSize, int TotalItems, int TotalPages,
    List<AssignmentResponse> Assignments);
public record SuitableVerificatorResponse(Guid UserId, string FullName);
