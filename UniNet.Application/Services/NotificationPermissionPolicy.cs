using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public static class NotificationPermissionPolicy
{
    public const string Manage = "notifications.manage";
    public const string StudentVerificationReview = "verification.student.review";
    public const string PartnerVerificationReview = "verification.partner.review";
    public const string ProjectReview = "project.review";
    public const string RecruitmentReview = "recruitment.review";
    public const string EvidenceReview = "verification.evidence.review";
    public const string CommunityReview = "community.review";
    public static readonly string[] All = [Manage, StudentVerificationReview, PartnerVerificationReview, ProjectReview, RecruitmentReview, EvidenceReview, CommunityReview];

    public static AccountRole[] Roles(string permission) => permission switch {
        Manage or StudentVerificationReview or PartnerVerificationReview => [AccountRole.Admin],
        EvidenceReview => [AccountRole.Admin, AccountRole.Verificator],
        ProjectReview or RecruitmentReview => [AccountRole.Admin, AccountRole.Moderator],
        CommunityReview => [AccountRole.Admin, AccountRole.Moderator],
        _ => []
    };

    public static IQueryable<Account> Recipients(UniNetDbContext db, string permission)
    {
        var roles = Roles(permission);
        if (!All.Contains(permission)) return db.Accounts.Where(a => false);
        return db.Accounts.Where(a => a.Status == AccountStatus.Active &&
            (a.Role == AccountRole.Admin || a.Role == AccountRole.Moderator || a.Role == AccountRole.Verificator) &&
            (db.Set<NotificationPermissionOverride>().Any(p => p.AccountId == a.Id && p.Permission == permission && p.IsGranted) ||
             roles.Contains(a.Role) && !db.Set<NotificationPermissionOverride>().Any(p => p.AccountId == a.Id && p.Permission == permission && !p.IsGranted)));
    }

    public static Task<bool> HasAsync(UniNetDbContext db, Guid accountId, string permission, CancellationToken ct = default) =>
        Recipients(db, permission).AnyAsync(a => a.Id == accountId, ct);

    public static async Task RequireAsync(UniNetDbContext db, Guid accountId, string permission, CancellationToken ct = default)
    {
        if (!await HasAsync(db, accountId, permission, ct))
            throw new ProjectVerificationException("FORBIDDEN", "Bạn không có quyền thực hiện thao tác này.", 403);
    }
}
