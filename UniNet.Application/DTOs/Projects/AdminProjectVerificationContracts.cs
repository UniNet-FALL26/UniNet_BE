using System.ComponentModel.DataAnnotations;

namespace UniNet.Application;

public record CreateVerificationRefundRequest(Guid TransactionId, decimal Amount,
    [Required, StringLength(2000, MinimumLength = 1)] string Reason);
public enum VerificationReviewDecision { Uphold, ReassessmentRequired }
public record ReviewVerificationRequest(VerificationReviewDecision Decision,
    [Required, StringLength(2000, MinimumLength = 1)] string Reason);
public record VerificationAdminActionResponse(Guid Id, Guid ActorUserId, string Decision, string Reason,
    Guid? RefundId, DateTimeOffset CreatedAt);
public record AdminVerificationDetailResponse(ProjectVerificationDetailResponse Detail,
    List<VerificationAdminActionResponse> Actions);
