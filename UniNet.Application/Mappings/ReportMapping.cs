using UniNet.Domain.Entities;

namespace UniNet.Application;

public static class ReportMapping
{
    public static ReportResponse ToResponse(VerificationReport report) => new(report.Id,
        report.AssignmentId, report.VerificationAssignment.ProjectVerificationId,
        report.Summary, report.Findings, report.Recommendations, report.Outcome,
        report.SubmittedByUserId ?? report.VerificationAssignment.AssignedToUserId, report.SubmittetAt);
}
