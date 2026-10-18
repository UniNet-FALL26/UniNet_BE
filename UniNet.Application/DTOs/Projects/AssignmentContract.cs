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
public record AssignmentDetailResponse(AssignmentResponse Assignment, VerificationProjectResponse Project,
    UniNet.Application.DTOs.Projects.EvidenceModerationResponse? EvidenceModeration, VerificationReportResponse? Report);
public record AssignmentsResponse(int Page, int PageSize, int TotalItems, int TotalPages,
    List<AssignmentResponse> Assignments);
public record SuitableVerificatorResponse(Guid UserId, string FullName);
