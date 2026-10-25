using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class NotificationTargets(UniNetDbContext db, NotificationService notifications)
{
    public async Task<string?> ResolveAsync(Guid accountId, Guid notificationId, CancellationToken ct)
    {
        var n = await notifications.GetOwnedAsync(accountId, notificationId, ct);
        var account = await notifications.ActiveAccountAsync(accountId, ct);
        var profileId = await db.UserProfiles.Where(p => p.AccountId == accountId).Select(p => (Guid?)p.Id).SingleOrDefaultAsync(ct);
        if (n.TargetId is not Guid id) return NotificationService.IsSafeUrl(n.TargetUrl) ? n.TargetUrl : null;
        var staff = account.Role is AccountRole.Admin or AccountRole.Moderator or AccountRole.Verificator;
        string? url = null;
        switch (n.TargetType) {
            case "ContentReport":
                if (await NotificationPermissionPolicy.HasAsync(db, accountId, NotificationPermissionPolicy.CommunityReview, ct) &&
                    await db.ContentReports.AnyAsync(r => r.Id == id && db.CommunityPosts.Any(p => p.Id == r.PostId), ct))
                    url = account.Role == AccountRole.Admin ? "/admin/community-posts" : "/moderator/community-posts";
                break;
            case "UserVerification": case "PartnerVerification":
                var verification = await db.UserVerifications.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, ct);
                if (verification == null) break;
                var partnerVerification = verification.VerificationType == VerificationType.Partner;
                if (verification.UserId == profileId) url = account.Role == AccountRole.Partner ? "/(company)/verification" : "/settings?account=1";
                else if (await NotificationPermissionPolicy.HasAsync(db, accountId,
                    partnerVerification ? NotificationPermissionPolicy.PartnerVerificationReview : NotificationPermissionPolicy.StudentVerificationReview, ct))
                    url = partnerVerification
                        ? account.Role == AccountRole.Admin ? "/admin/partner-verifications" : "/verificator/companies"
                        : account.Role == AccountRole.Admin ? "/admin/student-verifications" : "/verificator/students";
                break;
            case "JobApplication":
                var application = await db.JobApplications.Include(a => a.Job).AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);
                if (application == null) break;
                if (application.StudentUserId == profileId) url = $"/opportunities?tab=applications&jobId={application.JobId}";
                else if (application.Job.PartnerId == profileId) url = $"/(company)/applicants?jobId={application.JobId}";
                break;
            case "JobPosting":
                var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id, ct);
                if (job == null) break;
                if (job.PartnerId == profileId) url = "/(company)/jobs";
                else if (account.Role == AccountRole.Student &&
                    (job.Status == (short)PartnerJobStatus.Published && (job.StartAt == null || job.StartAt <= DateTimeOffset.UtcNow) &&
                        (job.Deadline == null || job.Deadline > DateTimeOffset.UtcNow) ||
                     await db.JobApplications.AnyAsync(a => a.StudentUserId == profileId && a.JobId == job.Id, ct))) url = $"/opportunities?jobId={id}";
                else if (await NotificationPermissionPolicy.HasAsync(db, accountId, NotificationPermissionPolicy.RecruitmentReview, ct))
                    url = account.Role == AccountRole.Admin ? "/admin/job-postings" : "/moderator/job-postings";
                break;
            case "ProjectInvitation":
                var invitation = await db.ProjectInvitations.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id && (i.InviteeId == profileId || i.InviterId == profileId), ct);
                if (invitation != null && await db.Projects.AnyAsync(p => p.Id == invitation.ProjectId, ct)) url = $"/group/{invitation.ProjectId}";
                break;
            case "Task":
                var task = await db.ProjectTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
                if (task != null && await IsMemberAsync(task.ProjectId, profileId, ct)) url = $"/workspace/{task.ProjectId}?taskId={id}";
                break;
            case "ProjectVerification":
                var projectVerification = await db.ProjectVerifications.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, ct);
                if (projectVerification == null) break;
                if (staff && await NotificationPermissionPolicy.HasAsync(db, accountId, NotificationPermissionPolicy.EvidenceReview, ct)) url = account.Role == AccountRole.Admin ? "/admin/projects" : "/verificator/projects";
                else if (await IsMemberAsync(projectVerification.ProjectId, profileId, ct)) url = $"/group/verification/{projectVerification.ProjectId}";
                break;
            case "EvidenceModeration": case "ProjectModeration":
                var moderation = await db.ProjectModerations.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, ct);
                if (moderation == null) break;
                var permission = n.TargetType == "EvidenceModeration" ? NotificationPermissionPolicy.EvidenceReview : NotificationPermissionPolicy.ProjectReview;
                if (staff && await NotificationPermissionPolicy.HasAsync(db, accountId, permission, ct)) url = account.Role == AccountRole.Admin ? "/admin/projects" : account.Role == AccountRole.Moderator ? "/moderator/projects" : "/verificator/projects";
                else if (await IsMemberAsync(moderation.ProjectId, profileId, ct)) url = $"/group/{moderation.ProjectId}";
                break;
            case "Project":
                if (await db.Projects.AnyAsync(p => p.Id == id, ct) &&
                    (await IsMemberAsync(id, profileId, ct) || await db.ProjectJoinRequests.AnyAsync(j => j.ProjectId == id && j.UserId == profileId, ct))) url = $"/group/{id}";
                break;
        }
        if (url == null) throw new ProjectVerificationException("TARGET_UNAVAILABLE", "Nội dung đã bị xóa hoặc bạn không còn quyền truy cập.", 404);
        return url;
    }

    private Task<bool> IsMemberAsync(Guid projectId, Guid? profileId, CancellationToken ct) => db.Projects.AnyAsync(p => p.Id == projectId &&
        (p.CreatorId == profileId || p.Members.Any(m => m.UserId == profileId && m.Status == ProjectMemberStatus.Active)), ct);
}
