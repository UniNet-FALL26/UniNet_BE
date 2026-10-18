using UniNet.Domain.Entities;
using UniNet.Domain.Enums;

namespace UniNet.Application;

public static class ProjectVerificationMapping
{
    public static ProjectVerificationResponse ToResponse(ProjectVerification verification) => new(
        verification.Id, verification.ProjectId, verification.Project.Title, verification.Status,
        verification.SubmittedAt, verification.ScheduledStartAt, verification.ScheduledEndAt,
        verification.CreatedAt, verification.UpdatedAt);

    public static VerificationReportResponse ToReportResponse(VerificationReport report)
    {
        var latest = report.Versions.OrderByDescending(v => v.Version).FirstOrDefault();
        return latest == null
            ? new(report.Id, report.VerificationId, report.Summary, report.Findings, report.Recommendations, report.SubmittetAt)
            : new(latest.Id, report.VerificationId, latest.Summary, latest.Findings, latest.Recommendations, latest.SubmittedAt);
    }

    public static VerificationFinalResultResponse? ToFinalResponse(ProjectVerification verification)
    {
        var reports = verification.Assignments.Where(a => a.Report != null)
            .OrderBy(a => a.Report!.SubmittetAt).ThenBy(a => a.Id)
            .Select(a => ToReportResponse(a.Report!)).ToList();
        return reports.Count > 0 || verification.Status is ProjectVerificationStatus.Verified or ProjectVerificationStatus.Failed
            ? new(verification.Status, verification.FinalizedAt, verification.FinalizedByUserId, reports) : null;
    }

    public static ProjectVerificationDetailResponse ToDetailResponse(ProjectVerification verification) => new(
        ToResponse(verification), new(verification.ProjectId, verification.Project.Title,
            verification.Project.ProjectField, verification.Project.Description, verification.Project.Status,
            verification.Project.CreatorId), verification.PackageId,
        verification.EvidenceModeration == null ? null : EvidenceModerationMapping.ToResponse(verification.EvidenceModeration), verification.MeetingStartAt, verification.MeetingEndAt,
        verification.Assignments.OrderBy(a => a.AssignedAt).ThenBy(a => a.Id)
            .Select(a => new VerificationAssignmentResponse(a.Id, a.AssignedToUserId, a.Status, a.AssignedAt,
                a.Report == null ? null : ToReportResponse(a.Report))).ToList(),
        verification.VerificationTransactions.OrderBy(v => v.Transaction.CreatedAt).ThenBy(v => v.TransactionId)
            .Select(v => new VerificationPaymentResponse(v.TransactionId, v.Transaction.PackageId,
                v.Transaction.Status, v.Transaction.Amount, v.Transaction.PaidAt,
                v.Transaction.RefundTransactions.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
                    .Select(r => new VerificationRefundResponse(r.Id, r.Amount, r.Status, r.Reason,
                        r.Status == PaymentStatus.Success ? r.CompletedAt : null)).ToList())).ToList(),
        ToFinalResponse(verification), verification.CancellationReason, verification.CancelledAt);
}
