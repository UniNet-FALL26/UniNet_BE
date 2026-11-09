using UniNet.Domain.Entities;
using UniNet.Domain.Enums;

namespace UniNet.Application;

public static class ProjectVerificationMapping
{
    public static ProjectVerificationResponse ToResponse(ProjectVerification verification) => new(
        verification.Id, verification.ProjectId, verification.Project.Title, verification.Status,
        verification.SubmittedAt, verification.ScheduledStartAt, verification.ScheduledEndAt,
        verification.CreatedAt, verification.UpdatedAt,
        verification.BookingStatus == VerificationBookingStatus.Held && verification.ReservedUntil <= DateTimeOffset.UtcNow
            ? VerificationBookingStatus.Expired : verification.BookingStatus,
        verification.ReservationId, verification.ReservedUntil, verification.BookingConfirmedAt, verification.AssignmentEligibleAt);

    public static VerificationReportResponse ToReportResponse(VerificationReport report) => new(
        report.Id, report.AssignmentId, report.Summary, report.Findings, report.Recommendations, report.SubmittetAt,
        report.Outcome, Services.ProjectAssessmentPolicy.ReadResult(report.AssessmentJson));

    public static VerificationFinalResultResponse? ToFinalResponse(ProjectVerification verification)
    {
        var reports = verification.Assignments.Where(a => a.Report != null)
            .OrderBy(a => a.Report!.SubmittetAt).ThenBy(a => a.Id)
            .Select(a => ToReportResponse(a.Report!)).ToList();
        return reports.Count > 0 || verification.Status is ProjectVerificationStatus.Verified or ProjectVerificationStatus.Failed or ProjectVerificationStatus.InsufficientEvidence
            ? new(verification.Status, verification.FinalizedAt, verification.FinalizedByUserId, reports) : null;
    }

    public static ProjectVerificationDetailResponse ToDetailResponse(ProjectVerification verification) => new(
        ToResponse(verification), new(verification.ProjectId, verification.Project.Title,
            verification.Project.ProjectField, verification.Project.Description, verification.Project.Status,
            ProjectIdentityMapping.LeaderId(verification.Project), verification.Project.Origin, verification.Project.CreatorId,
            ProjectIdentityMapping.Leader(verification.Project), ProjectIdentityMapping.ExternalDetails(verification.Project), SpecializationMapping.ToResponse(verification.Project.Specialization)), verification.PackageId,
        verification.MeetingStartAt, verification.MeetingEndAt,
        verification.Assignments.OrderBy(a => a.AssignedAt).ThenBy(a => a.Id)
            .Select(a => new VerificationAssignmentResponse(a.Id, a.AssignedToUserId, a.Status, a.AssignedAt,
                a.Report == null ? null : ToReportResponse(a.Report))).ToList(),
        verification.Transactions.OrderBy(v => v.CreatedAt).ThenBy(v => v.Id)
            .Select(v => new VerificationPaymentResponse(v.Id, v.PackageId,
                v.Status, v.Amount, v.PaidAt,
                v.RefundTransactions.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
                    .Select(r => new VerificationRefundResponse(r.Id, r.Amount, r.Status, r.Reason,
                        r.Status == PaymentStatus.Success ? r.CompletedAt : null, r.RequestedByUserId, r.RequestedAt, r.RequestSource)).ToList(),
                v.FailureReason is TransactionMapping.LatePaymentReview or TransactionMapping.BookingPaymentReview or
                    TransactionMapping.DuplicatePaymentReview or "PARTIAL_PAYMENT_REVIEW_REQUIRED", v.FailureReason)).ToList(),
        ToFinalResponse(verification), verification.CancellationReason, verification.CancelledAt,
        Services.ProjectAssessmentPolicy.ReadRubric(verification.AssessmentRubricJson));
}
