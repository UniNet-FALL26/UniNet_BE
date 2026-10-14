using UniNet.Domain.Entities;

namespace UniNet.Application;

public static class AssignmentMapping
{
    public static AssignmentResponse ToResponse(VerificationAssignment a) => new(a.Id,
        a.ProjectVerificationId, a.AssignedToUserId, a.AdminId, a.Status, a.AssignedAt,
        a.AssignmentDueAt, a.ReportDueAt, a.ReplacedAt, a.ReplacementReason, a.ReplacedBy?.Id,
        a.CreatedAt, a.UpdatedAt, a.ProjectVerification.ScheduledStartAt,
        a.ProjectVerification.ScheduledEndAt, a.ProjectVerification.MeetingStartAt,
        a.ProjectVerification.MeetingEndAt);

    public static AssignmentDetailResponse ToDetailResponse(VerificationAssignment a) => new(ToResponse(a),
        new(a.ProjectVerification.Project.Id, a.ProjectVerification.Project.Title,
            a.ProjectVerification.Project.ProjectField, a.ProjectVerification.Project.Description,
            a.ProjectVerification.Project.Status, a.ProjectVerification.Project.CreatorId),
        a.ProjectVerification.EvidenceSubmissions.OrderBy(e => e.Version)
            .Select(ProjectVerificationMapping.ToEvidenceResponse).ToList(),
        a.Report == null ? null : new(a.Report.Id, a.Id, a.Report.Summary, a.Report.Findings,
            a.Report.Recommendations, a.Report.SubmittetAt));
}
