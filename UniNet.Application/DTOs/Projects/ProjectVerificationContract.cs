using UniNet.Domain.Enums;

namespace UniNet.Application;

public record VerificationEvidenceRequest(string Type, string Title, string Url, string? Description = null);
public record CreateProjectVerificationRequest(Guid PackageId, List<VerificationEvidenceRequest>? Evidence = null);
public record SubmitVerificationEvidenceRequest(List<VerificationEvidenceRequest> Evidence);
public record ScheduleProjectVerificationRequest(DateTimeOffset StartAt, DateTimeOffset EndAt);
public record CancelProjectVerificationRequest(string Reason);

public record ProjectVerificationResponse(Guid Id, Guid ProjectId, string ProjectTitle,
    ProjectVerificationStatus Status, DateTimeOffset SubmittedAt, DateTimeOffset? ScheduledStartAt,
    DateTimeOffset? ScheduledEndAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public record ProjectVerificationsResponse(int Page, int PageSize, int TotalItems, int TotalPages,
    List<ProjectVerificationResponse> Verifications);
public record VerificationProjectResponse(Guid Id, string Title, string ProjectField, string Description,
    ProjectStatus Status, Guid LeaderId);
public record VerificationEvidenceSubmissionResponse(Guid Id, int Version, Guid SubmittedByUserId,
    DateTimeOffset SubmittedAt, List<VerificationEvidenceRequest> Evidence);
public record VerificationAiPreCheckResponse(bool HasResult, bool IsCurrent, int? EvidenceVersion,
    string? Summary, decimal? Score, DateTimeOffset? ReviewedAt, List<string> AdditionalEvidenceRequired);
public record VerificationReportResponse(Guid Id, Guid AssignmentId, string Summary, string? Findings,
    string? Recommendations, DateTimeOffset SubmittedAt);
public record VerificationAssignmentResponse(Guid Id, Guid AssignedToUserId, VerificationAssignmentStatus Status,
    DateTimeOffset AssignedAt, VerificationReportResponse? Report);
public record VerificationPaymentResponse(Guid TransactionId, Guid PackageId, PaymentStatus Status,
    decimal Amount, DateTimeOffset? PaidAt, List<VerificationRefundResponse> Refunds);
public record VerificationRefundResponse(Guid Id, decimal Amount, PaymentStatus Status, string Reason,
    DateTimeOffset? CompletedAt);
public record VerificationFinalResultResponse(ProjectVerificationStatus Status, DateTimeOffset? FinalizedAt,
    Guid? FinalizedByUserId, List<VerificationReportResponse> Reports);
public record ProjectVerificationDetailResponse(ProjectVerificationResponse Verification,
    VerificationProjectResponse Project, Guid PackageId,
    List<VerificationEvidenceSubmissionResponse> EvidenceSubmissions, VerificationAiPreCheckResponse AiPreCheck,
    DateTimeOffset? MeetingStartAt, DateTimeOffset? MeetingEndAt,
    List<VerificationAssignmentResponse> Assignments, List<VerificationPaymentResponse> Payments,
    VerificationFinalResultResponse? FinalResult, string? CancellationReason, DateTimeOffset? CancelledAt);
public record VerificationMeetingSlotResponse(DateTimeOffset StartAt, DateTimeOffset EndAt, int RemainingCapacity);
public record VerificationMeetingSlotsResponse(string TimeZoneId, List<VerificationMeetingSlotResponse> Slots);
