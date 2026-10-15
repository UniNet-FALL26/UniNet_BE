using UniNet.Domain.Entities;

namespace UniNet.Application;

public static class ReportMapping
{
    public static ReportResponse ToResponse(VerificationReport report) => new(report.Id,
        report.VerificationId, report.VerificationAssignment.ProjectVerificationId, 1,
        report.Summary, report.Findings, report.Recommendations, report.Outcome,
        report.SubmittedByUserId ?? report.VerificationAssignment.AssignedToUserId, report.SubmittetAt, null, report.Id);

    public static ReportResponse ToResponse(VerificationReportVersion version) => new(version.Id,
        version.Report.VerificationId, version.Report.VerificationAssignment.ProjectVerificationId,
        version.Version, version.Summary, version.Findings, version.Recommendations, version.Outcome,
        version.SubmittedByUserId, version.SubmittedAt, version.Reason, version.ReportId);
}
