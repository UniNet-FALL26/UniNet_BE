using UniNet.Domain.Entities;

namespace UniNet.Application;

public static class AssignmentMapping
{
    public static AssignmentResponse ToResponse(VerificationAssignment a) => new(a.Id,
        a.ProjectVerificationId, a.AssignedToUserId, a.AdminId, a.Status, a.AssignedAt,
        a.AssignmentDueAt, a.ReportDueAt, a.ReplacedAt, a.ReplacementReason, a.ReplacedBy?.Id,
        a.CreatedAt, a.UpdatedAt, a.ProjectVerification.ScheduledStartAt,
        a.ProjectVerification.ScheduledEndAt, a.ProjectVerification.MeetingStartAt,
        a.ProjectVerification.MeetingEndAt, a.CompletedAt);

    public static AssignmentDetailResponse ToDetailResponse(VerificationAssignment a) => new(ToResponse(a),
        new(a.ProjectVerification.Project.Id, a.ProjectVerification.Project.Title,
            a.ProjectVerification.Project.ProjectField, a.ProjectVerification.Project.Description,
            a.ProjectVerification.Project.Status, ProjectIdentityMapping.LeaderId(a.ProjectVerification.Project),
            a.ProjectVerification.Project.ProjectSkills.OrderBy(s => s.Skill.Name).ThenBy(s => s.SkillId)
                .Select(s => new ProjectSkillInfo(s.SkillId, s.Skill.Name,
                    s.Skill.Category?.ToString() ?? string.Empty, s.Skill.IconUrl, null)).ToList(),
            a.ProjectVerification.Project.Members.OrderBy(m => m.JoinedAt).ThenBy(m => m.UserId)
                .Select(m => new ProjectMemberInfo(m.UserId, m.User.FullName, m.User.Nickname,
                    m.User.AvatarUrl, m.Role, m.Status, m.JoinedAt)).ToList(),
            ProjectIdentityMapping.Leader(a.ProjectVerification.Project), a.ProjectVerification.Project.Origin,
            a.ProjectVerification.Project.CreatorId, ProjectIdentityMapping.ExternalDetails(a.ProjectVerification.Project)),
        a.ProjectVerification.EvidenceModeration == null ? null : EvidenceModerationMapping.ToResponse(a.ProjectVerification.EvidenceModeration),
        a.Report == null ? null : ProjectVerificationMapping.ToReportResponse(a.Report),
        Services.ProjectAssessmentPolicy.ReadRubric(a.ProjectVerification.AssessmentRubricJson),
        new(a.AssignedToUserId, a.AssignedToUser.FullName, a.AssignedToUser.AvatarUrl, a.AssignedToUser.Nickname));
}
