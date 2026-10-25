using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public static class AccountSessions
{
    public static Task<bool> IsValidAsync(UniNetDbContext db, Guid accountId, int version, CancellationToken ct) =>
        db.Accounts.AsNoTracking().AnyAsync(a => a.Id == accountId && a.Status == AccountStatus.Active && a.SessionVersion == version && version >= 0, ct);
}

public sealed class AdminUsersService(UniNetDbContext db)
{
    public async Task<AdminPage<AdminUserItem>> ListAsync(Guid actor, AdminUserQuery request, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, actor, NotificationPermissionPolicy.UsersView, ct);
        ValidatePage(request.Page, request.PageSize);
        if (request.Search?.Length > 255 || request.Role.HasValue && !Enum.IsDefined(request.Role.Value) || request.Status.HasValue && !Enum.IsDefined(request.Status.Value)) throw Invalid();
        var query = db.Accounts.AsNoTracking().Where(a => a.Status != AccountStatus.Deleted);
        if (request.StaffOnly) query = query.Where(a => a.Role == AccountRole.Admin || a.Role == AccountRole.Moderator || a.Role == AccountRole.Verificator);
        if (request.Role.HasValue) query = query.Where(a => a.Role == request.Role);
        if (request.Status.HasValue) query = query.Where(a => a.Status == request.Status);
        if (!string.IsNullOrWhiteSpace(request.Search)) {
            var text = request.Search.Trim().ToLowerInvariant(); var id = Guid.TryParse(text, out var parsed) ? parsed : Guid.Empty;
            query = query.Where(a => a.Id == id || a.Email.ToLower().Contains(text) || a.Profile != null &&
                (a.Profile.FullName.ToLower().Contains(text) || a.Profile.Nickname.ToLower().Contains(text) || a.Profile.OrganizationName != null && a.Profile.OrganizationName.ToLower().Contains(text)));
        }
        query = request.Verification switch {
            null or "" => query,
            "verified" => query.Where(a => a.Profile != null && a.Profile.IsVerified),
            "unverified" => query.Where(a => a.Profile == null || !a.Profile.IsVerified && !a.Profile.Verifications.OrderByDescending(v => v.SubmittedAt).ThenByDescending(v => v.Id).Take(1).Any(v => v.Status == VerificationStatus.Pending || v.Status == VerificationStatus.Rejected)),
            "pending" => query.Where(a => a.Profile != null && a.Profile.Verifications.OrderByDescending(v => v.SubmittedAt).ThenByDescending(v => v.Id).Select(v => (VerificationStatus?)v.Status).FirstOrDefault() == VerificationStatus.Pending),
            "rejected" => query.Where(a => a.Profile != null && !a.Profile.IsVerified && a.Profile.Verifications.OrderByDescending(v => v.SubmittedAt).ThenByDescending(v => v.Id).Select(v => (VerificationStatus?)v.Status).FirstOrDefault() == VerificationStatus.Rejected),
            _ => throw Invalid()
        };
        var total = await query.CountAsync(ct);
        var sorted = request.Sort switch {
            "createdAt" => request.Desc ? query.OrderByDescending(a => a.CreatedAt) : query.OrderBy(a => a.CreatedAt),
            "name" => request.Desc ? query.OrderByDescending(a => a.Profile == null ? a.Email : a.Profile.FullName) : query.OrderBy(a => a.Profile == null ? a.Email : a.Profile.FullName),
            "email" => request.Desc ? query.OrderByDescending(a => a.Email) : query.OrderBy(a => a.Email),
            _ => throw Invalid()
        };
        var rows = await sorted.ThenBy(a => a.Id).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(a => new AdminUserItem(a.Id, a.Profile == null ? null : a.Profile.Id, a.Profile == null ? a.Email : a.Profile.FullName,
                a.Profile == null ? "" : a.Profile.Nickname, a.Email, a.Profile == null ? null : a.Profile.AvatarUrl, a.Role, a.Status, a.CreatedAt,
                a.Profile != null && a.Profile.IsVerified, a.Profile == null ? null : a.Profile.Verifications.OrderByDescending(v => v.SubmittedAt).ThenByDescending(v => v.Id).Select(v => (VerificationStatus?)v.Status).FirstOrDefault())).ToListAsync(ct);
        return new(rows, request.Page, request.PageSize, total);
    }

    public async Task<AdminUserStats> StatsAsync(Guid actor, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, actor, NotificationPermissionPolicy.UsersView, ct);
        var query = db.Accounts.Where(a => a.Status != AccountStatus.Deleted);
        return new(await query.CountAsync(ct), await query.CountAsync(a => a.Status == AccountStatus.Active, ct), await query.CountAsync(a => a.Status == AccountStatus.Suspended, ct), await query.CountAsync(a => a.Profile != null && a.Profile.IsVerified, ct));
    }

    public async Task<AdminUserDetail> DetailAsync(Guid actor, Guid id, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, actor, NotificationPermissionPolicy.UsersDetail, ct);
        var account = await db.Accounts.AsNoTracking().Include(a => a.Profile).SingleOrDefaultAsync(a => a.Id == id && a.Status != AccountStatus.Deleted, ct) ?? throw Missing();
        var history = await db.UserVerifications.AsNoTracking().Where(v => v.User.AccountId == id).OrderByDescending(v => v.SubmittedAt).ThenByDescending(v => v.Id)
            .Select(v => new AdminVerificationHistory(v.Id, v.VerificationType, v.Status, v.SubmittedAt, v.ReviewedAt, v.RejectReason, v.ReviewedBy, v.Reviewer == null || v.Reviewer.Profile == null ? null : v.Reviewer.Profile.FullName)).ToListAsync(ct);
        var changes = await db.Set<AccountStatusChange>().AsNoTracking().Where(c => c.AccountId == id).OrderByDescending(c => c.CreatedAt)
            .Select(c => new AdminStatusHistory(c.Id, c.PreviousStatus, c.Status, c.Reason, c.ActorAccountId, db.Accounts.Where(a => a.Id == c.ActorAccountId).Select(a => a.Profile == null ? a.Email : a.Profile.FullName).First(), c.CreatedAt)).ToListAsync(ct);
        var p = account.Profile;
        return new(Item(account, history.FirstOrDefault()?.Status), p == null ? null : new(p.CoverUrl, p.Bio, p.Phone, p.UniversityName, p.Major, p.StudentCode, p.OrganizationName, p.PartnerType, p.Industry, p.Website, p.ContactEmail, p.Address), history, changes);
    }

    public async Task<AdminUserItem> ChangeStatusAsync(Guid actor, Guid id, bool locked, string? reason, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, actor, locked ? NotificationPermissionPolicy.UsersLock : NotificationPermissionPolicy.UsersUnlock, ct);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000) throw new AuthException("INVALID_REASON", "Lý do bắt buộc và tối đa 2000 ký tự.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var account = await db.Accounts.Include(a => a.Profile).SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw Missing();
        if (id == actor || account.Role is not (AccountRole.Student or AccountRole.Partner)) throw new AuthException("PROTECTED_ACCOUNT", "Không được thay đổi trạng thái tài khoản quản trị hoặc nhân viên.", 403);
        var expected = locked ? AccountStatus.Active : AccountStatus.Suspended;
        if (account.Status != expected) throw new AuthException("ACCOUNT_STATUS_CHANGED", "Trạng thái tài khoản đã thay đổi. Vui lòng tải lại.", 409);
        var status = locked ? AccountStatus.Suspended : AccountStatus.Active;
        var change = new AccountStatusChange { AccountId = id, ActorAccountId = actor, PreviousStatus = expected, Status = status, Reason = reason.Trim() };
        // Enqueue before suspension; delivery is private and remains persisted while the account is locked.
        if (locked) await Notice();
        var changed = await db.Accounts.Where(a => a.Id == id && a.Status == expected && a.SessionVersion == account.SessionVersion && (a.Role == AccountRole.Student || a.Role == AccountRole.Partner))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status).SetProperty(a => a.SessionVersion, a => a.SessionVersion + 1).SetProperty(a => a.UpdatedAt, change.CreatedAt), ct);
        if (changed != 1) throw new AuthException("ACCOUNT_STATUS_CHANGED", "Tài khoản vừa được người khác xử lý.", 409);
        await db.RefreshTokens.Where(t => t.AccountId == id && t.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, (DateTimeOffset?)change.CreatedAt), ct);
        await db.Entry(account).ReloadAsync(ct);
        if (!locked) await Notice();
        db.Set<AccountStatusChange>().Add(change); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return Item(account, null);

        Task Notice() => NotificationService.EnqueueAsync(db, [id], new(locked ? "account.locked" : "account.unlocked", "System",
            locked ? "Tài khoản đã bị khóa" : "Tài khoản đã được mở khóa", reason.Trim(), "User", id, EventKey: $"account-status:{change.Id:N}", SenderUserId: actor), ct);
    }

    internal static void ValidatePage(int page, int size) { if (page is < 1 or > 100000 || size is < 1 or > 100) throw Invalid(); }
    internal static AuthException Invalid() => new("INVALID_FILTER", "Bộ lọc, sắp xếp hoặc phân trang không hợp lệ.");
    private static AuthException Missing() => new("ACCOUNT_NOT_FOUND", "Không tìm thấy tài khoản.", 404);
    private static AdminUserItem Item(Account a, VerificationStatus? status) => new(a.Id, a.Profile?.Id, a.Profile?.FullName ?? a.Email, a.Profile?.Nickname ?? "", a.Email, a.Profile?.AvatarUrl, a.Role, a.Status, a.CreatedAt, a.Profile?.IsVerified ?? false, status);
}
