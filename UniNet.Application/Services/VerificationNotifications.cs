using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class VerificationNotifications(UniNetDbContext db)
{
    public async Task AddAsync(Guid verificationId, Guid recipientId, string key, string message, DateTimeOffset now, CancellationToken ct)
    {
        if (!await db.Set<VerificationNotification>().AnyAsync(n => n.RecipientUserId == recipientId && n.EventKey == key, ct))
            db.Set<VerificationNotification>().Add(new() { Id = Guid.NewGuid(), VerificationId = verificationId,
                RecipientUserId = recipientId, EventKey = key, Message = message, CreatedAt = now });
    }

    public async Task AdminsAsync(Guid verificationId, string key, string message, DateTimeOffset now, CancellationToken ct)
    {
        var admins = await db.UserProfiles.Where(p => p.Account.Role == AccountRole.Admin && p.Account.Status == AccountStatus.Active)
            .Select(p => p.Id).ToListAsync(ct);
        foreach (var admin in admins) await AddAsync(verificationId, admin, key, message, now, ct);
    }

    public async Task<List<VerificationNotification>> GetMineAsync(Guid accountId, int page, int pageSize, CancellationToken ct)
    {
        var profile = await db.UserProfiles.AsNoTracking().Include(p => p.Account).SingleOrDefaultAsync(p => p.AccountId == accountId, ct);
        if (profile == null || profile.Account.Status != AccountStatus.Active ||
            profile.Account.Role is not (AccountRole.Admin or AccountRole.Verificator))
            throw new ProjectVerificationException("FORBIDDEN", "Active verification staff access is required.", 403);
        page = Math.Max(page, 1); pageSize = Math.Clamp(pageSize, 1, 100);
        return await db.Set<VerificationNotification>().AsNoTracking().Where(n => n.RecipientUserId == profile.Id)
            .OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize).ToListAsync(ct);
    }
}
