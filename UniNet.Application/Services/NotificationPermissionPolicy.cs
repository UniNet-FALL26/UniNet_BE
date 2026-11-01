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
    public const string UsersView = "users.view";
    public const string UsersDetail = "users.detail";
    public const string UsersLock = "users.lock";
    public const string UsersUnlock = "users.unlock";
    public const string StaffManage = "staff.manage";
    public const string PermissionsManage = "permissions.manage";
    public const string FinanceView = "finance.view";
    public const string WalletCredit = "finance.wallet.credit";
    public const string RefundCreate = "finance.refund.create";
    public const string RefundReview = "finance.refund.review";
    public const string WithdrawalReview = "finance.withdrawal.review";
    public const string PayoutCreate = "finance.payout.create";
    public const string PayoutConfirm = "finance.payout.confirm";
    public const string FinanceExport = "finance.report.export";
    public static readonly string[] All = [Manage, StudentVerificationReview, PartnerVerificationReview, ProjectReview, RecruitmentReview, EvidenceReview, CommunityReview,
        UsersView, UsersDetail, UsersLock, UsersUnlock, StaffManage, PermissionsManage,
        FinanceView, WalletCredit, RefundCreate, RefundReview, WithdrawalReview, PayoutCreate, PayoutConfirm, FinanceExport];

    public static AccountRole[] Roles(string permission) => permission switch {
        Manage or StaffManage or PermissionsManage => [AccountRole.Admin],
        StudentVerificationReview or PartnerVerificationReview => [AccountRole.Admin, AccountRole.Verifier],
        UsersView or UsersDetail => [AccountRole.Admin, AccountRole.Verifier, AccountRole.Moderator],
        UsersLock or UsersUnlock => [AccountRole.Admin, AccountRole.Moderator],
        FinanceView or WalletCredit or RefundCreate or RefundReview or WithdrawalReview or PayoutCreate or PayoutConfirm or FinanceExport => [AccountRole.Admin, AccountRole.Accountant],
        EvidenceReview => [AccountRole.Admin, AccountRole.Assessor],
        ProjectReview or RecruitmentReview => [AccountRole.Admin, AccountRole.Moderator],
        CommunityReview => [AccountRole.Admin, AccountRole.Moderator],
        _ => []
    };

    public static IQueryable<Account> Recipients(UniNetDbContext db, string permission)
    {
        var roles = Roles(permission);
        if (!All.Contains(permission)) return db.Accounts.Where(a => false);
        return db.Accounts.Where(a => a.Status == AccountStatus.Active &&
            roles.Contains(a.Role) &&
            (db.Set<NotificationPermissionOverride>().Any(p => p.AccountId == a.Id && p.Permission == permission && p.IsGranted) ||
             roles.Contains(a.Role) && !db.Set<NotificationPermissionOverride>().Any(p => p.AccountId == a.Id && p.Permission == permission && !p.IsGranted)));
    }

    public static Task<bool> HasAsync(UniNetDbContext db, Guid accountId, string permission, CancellationToken ct = default) =>
        Recipients(db, permission).AnyAsync(a => a.Id == accountId, ct);

    public static async Task<IReadOnlyList<string>> EffectiveAsync(UniNetDbContext db, Guid accountId, CancellationToken ct = default)
    {
        var account = await db.Accounts.AsNoTracking().Where(a => a.Id == accountId && a.Status == AccountStatus.Active)
            .Select(a => new { a.Role }).SingleOrDefaultAsync(ct);
        if (account == null || account.Role is AccountRole.Student or AccountRole.Partner) return [];
        var overrides = await db.Set<NotificationPermissionOverride>().AsNoTracking().Where(p => p.AccountId == accountId).ToDictionaryAsync(p => p.Permission, p => p.IsGranted, ct);
        return All.Where(permission => Roles(permission).Contains(account.Role) && (!overrides.TryGetValue(permission, out var granted) || granted)).ToArray();
    }

    public static async Task RequireAsync(UniNetDbContext db, Guid accountId, string permission, CancellationToken ct = default)
    {
        if (!await HasAsync(db, accountId, permission, ct))
            throw new ProjectVerificationException("FORBIDDEN", "Bạn không có quyền thực hiện thao tác này.", 403);
    }
}
