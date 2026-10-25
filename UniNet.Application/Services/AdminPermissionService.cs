using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed record AdminCapabilities(IReadOnlyList<string> Permissions, int? PendingStudents, int? PendingPartners);

public sealed class AdminPermissionService(UniNetDbContext db)
{
    public async Task<AdminCapabilities> CapabilitiesAsync(Guid actor, CancellationToken ct)
    {
        if (!await db.Accounts.AnyAsync(a => a.Id == actor && a.Status == AccountStatus.Active && (a.Role == AccountRole.Admin || a.Role == AccountRole.Moderator || a.Role == AccountRole.Verificator), ct))
            throw new AuthException("FORBIDDEN", "Tài khoản không có quyền quản trị.", 403);
        var permissions = await NotificationPermissionPolicy.EffectiveAsync(db, actor, ct);
        Task<int> Pending(VerificationType type) => db.UserVerifications.CountAsync(v => v.VerificationType == type && v.Status == VerificationStatus.Pending, ct);
        return new(permissions, permissions.Contains(NotificationPermissionPolicy.StudentVerificationReview) ? await Pending(VerificationType.Student) : null,
            permissions.Contains(NotificationPermissionPolicy.PartnerVerificationReview) ? await Pending(VerificationType.Partner) : null);
    }

    public async Task<List<AdminPermissionEntry>> GetAsync(Guid actor, Guid target, CancellationToken ct)
    {
        await RequireManager(actor, ct);
        if (!await db.Accounts.AnyAsync(a => a.Id == target && (a.Role == AccountRole.Admin || a.Role == AccountRole.Moderator || a.Role == AccountRole.Verificator), ct)) throw new AuthException("INVALID_STAFF", "Không tìm thấy nhân viên.", 404);
        var overrides = await db.Set<NotificationPermissionOverride>().Where(p => p.AccountId == target).ToDictionaryAsync(p => p.Permission, p => p.IsGranted, ct);
        var result = new List<AdminPermissionEntry>();
        var effective = await NotificationPermissionPolicy.EffectiveAsync(db, target, ct);
        foreach (var permission in NotificationPermissionPolicy.All)
            result.Add(new(permission, effective.Contains(permission), overrides.TryGetValue(permission, out var value) ? value : null));
        return result;
    }

    public async Task SetAsync(Guid actor, Guid target, string permission, bool granted, CancellationToken ct)
    {
        await RequireManager(actor, ct);
        if (target == actor || !NotificationPermissionPolicy.All.Contains(permission)) throw new AuthException("INVALID_PERMISSION_CHANGE", "Không được thay đổi quyền bản thân hoặc quyền không tồn tại.", 403);
        if (granted && permission is NotificationPermissionPolicy.Manage or NotificationPermissionPolicy.StaffManage or NotificationPermissionPolicy.PermissionsManage)
            throw new AuthException("ADMIN_ONLY_PERMISSION", "Quyền này chỉ dành cho quản trị viên.", 403);
        if (!await db.Accounts.AnyAsync(a => a.Id == target && (a.Role == AccountRole.Moderator || a.Role == AccountRole.Verificator), ct))
            throw new AuthException("PROTECTED_ACCOUNT", "Chỉ được cấu hình quyền của nhân viên, không thay đổi quyền quản trị viên.", 403);
        var row = await db.Set<NotificationPermissionOverride>().FindAsync([target, permission], ct);
        if (row == null) db.Set<NotificationPermissionOverride>().Add(new() { AccountId = target, Permission = permission, IsGranted = granted });
        else row.IsGranted = granted;
        await db.SaveChangesAsync(ct);
    }
    public async Task SetManyAsync(Guid actor, Guid target, IReadOnlyList<PermissionAssignment> changes, CancellationToken ct)
    {
        await RequireManager(actor, ct);
        if (changes is null || changes.Count is < 1 or > 30 || changes.Any(c => c is null || string.IsNullOrWhiteSpace(c.Permission)) || changes.Select(c => c.Permission).Distinct().Count() != changes.Count) throw new AuthException("INVALID_PERMISSIONS", "Danh sách quyền không hợp lệ.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({target.ToString()}, 0))", ct);
        foreach (var change in changes) await SetAsync(actor, target, change.Permission, change.Granted, ct);
        await transaction.CommitAsync(ct);
    }
    private async Task RequireManager(Guid actor, CancellationToken ct)
    {
        if (!await db.Accounts.AnyAsync(a => a.Id == actor && a.Role == AccountRole.Admin && a.Status == AccountStatus.Active, ct)) throw new AuthException("FORBIDDEN", "Chỉ quản trị viên được cấu hình quyền.", 403);
        await NotificationPermissionPolicy.RequireAsync(db, actor, NotificationPermissionPolicy.PermissionsManage, ct);
    }
}
