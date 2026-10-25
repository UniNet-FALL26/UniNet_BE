using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class VerificationNotifications(UniNetDbContext db)
{
    public async Task<List<VerificationNotification>> GetMineAsync(Guid accountId, int page, int pageSize, CancellationToken ct)
    {
        var profile = await db.UserProfiles.AsNoTracking().Include(p => p.Account).SingleOrDefaultAsync(p => p.AccountId == accountId, ct);
        if (profile == null || profile.Account.Status != AccountStatus.Active ||
            profile.Account.Role is not (AccountRole.Admin or AccountRole.Verificator or AccountRole.Student))
            throw new ProjectVerificationException("FORBIDDEN", "Active verification staff access is required.", 403);
        page = Math.Max(page, 1); pageSize = Math.Clamp(pageSize, 1, 100);
        var now = DateTimeOffset.UtcNow;
        var allowed = new List<string>();
        foreach (var permission in NotificationPermissionPolicy.All)
            if (await NotificationPermissionPolicy.HasAsync(db, accountId, permission, ct)) allowed.Add(permission);
        return await db.Set<VerificationNotification>().AsNoTracking().Where(n => n.RecipientUserId == accountId && n.Category == "verification" &&
            (n.ExpiresAt == null || n.ExpiresAt > now) && (n.RequiredPermission == null || allowed.Contains(n.RequiredPermission)))
            .OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize).ToListAsync(ct);
    }

}
