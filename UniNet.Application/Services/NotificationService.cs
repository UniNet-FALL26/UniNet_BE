using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed record NotificationContent(string Type, string Category, string Title, string Message,
    string? TargetType = null, Guid? TargetId = null, string? TargetUrl = null, string? EventKey = null,
    string Audience = "Personal", string? RequiredPermission = null, Guid? SenderUserId = null, DateTimeOffset? ExpiresAt = null);
public sealed record NotificationPage(IReadOnlyList<NotificationDto> Items, int Page, int PageSize, int Total, int UnreadCount);

public sealed class NotificationService(UniNetDbContext db)
{
    public static async Task EnqueueAsync(UniNetDbContext db, IEnumerable<Guid> recipientAccountIds, NotificationContent content, CancellationToken ct = default)
    {
        Validate(content);
        var ids = recipientAccountIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return;
        var accounts = await db.Accounts.Where(a => ids.Contains(a.Id) && a.Status == AccountStatus.Active).Select(a => a.Id).ToListAsync(ct);
        if (content.RequiredPermission != null) {
            var permitted = await NotificationPermissionPolicy.Recipients(db, content.RequiredPermission).Where(a => ids.Contains(a.Id)).Select(a => a.Id).ToListAsync(ct);
            accounts = accounts.Intersect(permitted).ToList();
        }
        var existing = content.EventKey == null ? [] : await db.Set<VerificationNotification>()
            .Where(n => n.EventKey == content.EventKey && ids.Contains(n.RecipientUserId)).Select(n => n.RecipientUserId).ToListAsync(ct);
        foreach (var id in accounts.Except(existing)) {
            if (content.EventKey != null && db.Set<VerificationNotification>().Local.Any(n => n.RecipientUserId == id && n.EventKey == content.EventKey)) continue;
            db.Set<VerificationNotification>().Add(new() { Id = Guid.NewGuid(), RecipientUserId = id, SenderUserId = content.SenderUserId,
                Type = content.Type, Category = content.Category.ToLowerInvariant(), Title = content.Title.Trim(), Message = content.Message.Trim(), Audience = content.Audience,
                RequiredPermission = content.RequiredPermission, TargetType = content.TargetType, TargetId = content.TargetId,
                TargetUrl = content.TargetUrl, EventKey = content.EventKey, ExpiresAt = content.ExpiresAt?.ToUniversalTime(), CreatedAt = DateTimeOffset.UtcNow });
        }
    }

    public static async Task EnqueuePermissionAsync(UniNetDbContext db, string permission, NotificationContent content, CancellationToken ct = default) =>
        await EnqueueAsync(db, await NotificationPermissionPolicy.Recipients(db, permission).Select(a => a.Id).ToListAsync(ct),
            content with { Audience = "Permission", RequiredPermission = permission }, ct);

    public Task SendToUserAsync(Guid accountId, NotificationContent content, CancellationToken ct = default) => SendToUsersAsync([accountId], content, ct);
    public async Task SendToUsersAsync(IEnumerable<Guid> accountIds, NotificationContent content, CancellationToken ct = default)
    { await EnqueueAsync(db, accountIds, content, ct); await db.SaveChangesAsync(ct); }
    public async Task SendToRoleAsync(AccountRole role, NotificationContent content, CancellationToken ct = default) =>
        await SendToUsersAsync(await db.Accounts.Where(a => a.Role == role).Select(a => a.Id).ToListAsync(ct), content with { Audience = "Role" }, ct);
    public async Task SendToPermissionAsync(string permission, NotificationContent content, CancellationToken ct = default)
    { await EnqueuePermissionAsync(db, permission, content, ct); await db.SaveChangesAsync(ct); }
    public async Task<NotificationBroadcastDto> SendBroadcastAsync(Guid senderAccountId, CreateNotificationBroadcast request, CancellationToken ct = default)
    {
        var broadcasts = new NotificationBroadcastService(db);
        var draft = await broadcasts.CreateAsync(senderAccountId, request, ct);
        return await broadcasts.PublishAsync(senderAccountId, draft.Id, ct);
    }

    public async Task<Account> ActiveAccountAsync(Guid accountId, CancellationToken ct = default) =>
        await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == accountId && a.Status == AccountStatus.Active, ct)
        ?? throw new ProjectVerificationException("FORBIDDEN", "Tài khoản không còn hoạt động.", 403);

    private async Task<IQueryable<VerificationNotification>> MineAsync(Guid id, CancellationToken ct)
    {
        await ActiveAccountAsync(id, ct);
        var now = DateTimeOffset.UtcNow;
        var allowed = new List<string>();
        foreach (var permission in NotificationPermissionPolicy.All)
            if (await NotificationPermissionPolicy.HasAsync(db, id, permission, ct)) allowed.Add(permission);
        return db.Set<VerificationNotification>().Where(n => n.RecipientUserId == id &&
            (n.ExpiresAt == null || n.ExpiresAt > now) && (n.RequiredPermission == null || allowed.Contains(n.RequiredPermission)));
    }

    public async Task<NotificationPage> GetMineAsync(Guid id, int page = 1, int pageSize = 20, bool unreadOnly = false, string? category = null, CancellationToken ct = default)
    {
        page = Math.Clamp(page, 1, 100000); pageSize = Math.Clamp(pageSize, 1, 100);
        var mine = await MineAsync(id, ct);
        var unread = await mine.CountAsync(n => n.ReadAt == null, ct);
        if (unreadOnly) mine = mine.Where(n => n.ReadAt == null);
        if (!string.IsNullOrWhiteSpace(category)) { category = category.ToLowerInvariant(); mine = mine.Where(n => n.Category == category); }
        var total = await mine.CountAsync(ct);
        var rows = await mine.AsNoTracking().OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(rows.Select(NotificationDto.From).ToArray(), page, pageSize, total, unread);
    }

    public async Task<int> UnreadCountAsync(Guid id, CancellationToken ct = default) => await (await MineAsync(id, ct)).CountAsync(n => n.ReadAt == null, ct);
    public async Task ReadAsync(Guid accountId, Guid? notificationId, CancellationToken ct = default)
    {
        var mine = await MineAsync(accountId, ct);
        if (notificationId != null && !await mine.AnyAsync(n => n.Id == notificationId, ct))
            throw new ProjectVerificationException("NOT_FOUND", "Không tìm thấy thông báo.", 404);
        // A single set-based update also supports large inboxes.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var readAt = DateTimeOffset.UtcNow;
        await mine.Where(n => n.ReadAt == null && (notificationId == null || n.Id == notificationId)).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, readAt), ct);
        db.Set<NotificationDelivery>().Add(new() { AccountId = accountId, NotificationId = notificationId, Reason = notificationId == null ? "read-all" : "read" });
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }

    public async Task<VerificationNotification> GetOwnedAsync(Guid accountId, Guid notificationId, CancellationToken ct = default) =>
        await (await MineAsync(accountId, ct)).AsNoTracking().SingleOrDefaultAsync(n => n.Id == notificationId, ct)
        ?? throw new ProjectVerificationException("NOT_FOUND", "Thông báo không còn khả dụng.", 404);

    public static bool IsSafeUrl(string? url) => url == null || url.Length <= 500 && url.StartsWith('/') && !url.StartsWith("//") &&
        !url.Contains('\\') && !url.Any(char.IsControl) && !url.Contains('%') &&
        new[] { "/notifications", "/settings", "/opportunities", "/jobs", "/applicants", "/verification", "/group/", "/workspace/", "/admin/", "/moderator/", "/verificator/", "/(company)/", "/(tabs)/" }
            .Any(prefix => prefix.EndsWith('/') ? url.StartsWith(prefix, StringComparison.Ordinal) : url == prefix || url.StartsWith(prefix + "?", StringComparison.Ordinal) || url.StartsWith(prefix + "/", StringComparison.Ordinal));

    public static void Validate(NotificationContent c)
    {
        if (string.IsNullOrWhiteSpace(c.Title) || c.Title.Length > 200 || string.IsNullOrWhiteSpace(c.Message) || c.Message.Length > 10000 ||
            string.IsNullOrWhiteSpace(c.Type) || c.Type.Length > 60 || string.IsNullOrWhiteSpace(c.Category) || c.Category.Length > 40 ||
            string.IsNullOrWhiteSpace(c.Audience) || c.Audience.Length > 30 || c.EventKey?.Length > 200 || c.RequiredPermission?.Length > 100 || c.TargetType?.Length > 60 ||
            c.TargetId.HasValue != !string.IsNullOrWhiteSpace(c.TargetType) || !IsSafeUrl(c.TargetUrl))
            throw new ProjectVerificationException("INVALID_NOTIFICATION", "Nội dung hoặc đường dẫn thông báo không hợp lệ.", 400);
    }
}
