using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed record CreateNotificationBroadcast(string Title, string Message, string Audience, string? Role = null,
    Guid[]? RecipientAccountIds = null, string? TargetUrl = null, DateTimeOffset? ExpiresAt = null);
public sealed record NotificationBroadcastDto(Guid Id, string Title, string Message, string Audience, string? Role,
    Guid[] RecipientAccountIds, string? TargetUrl, DateTimeOffset? ExpiresAt, string Status, int RecipientCount, int ProcessedCount,
    DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt, DateTimeOffset? CompletedAt, string? LastError);
public sealed class NotificationBroadcastProcessingException(Guid broadcastId, Exception inner) : Exception("Broadcast batch failed.", inner)
{
    public Guid BroadcastId { get; } = broadcastId;
}

public sealed class NotificationBroadcastService(UniNetDbContext db)
{
    public async Task RequireAsync(Guid id, CancellationToken ct) {
        if (!await db.Accounts.AnyAsync(a => a.Id == id && a.Role == AccountRole.Admin && a.Status == AccountStatus.Active, ct))
            throw new ProjectVerificationException("FORBIDDEN", "Chỉ Admin được quản lý thông báo hệ thống.", 403);
        await NotificationPermissionPolicy.RequireAsync(db, id, NotificationPermissionPolicy.Manage, ct);
    }
    public async Task<NotificationBroadcastDto> CreateAsync(Guid sender, CreateNotificationBroadcast request, CancellationToken ct)
    {
        await RequireAsync(sender, ct);
        NotificationService.Validate(new("SystemAnnouncement", "system", request.Title, request.Message, TargetUrl: request.TargetUrl));
        if (request.Audience is not ("All" or "Role" or "Selected") ||
            request.Audience == "Role" && (!Enum.TryParse<AccountRole>(request.Role, out var parsed) || !Enum.IsDefined(parsed)) ||
            request.ExpiresAt <= DateTimeOffset.UtcNow)
            throw Invalid();
        var recipients = (request.RecipientAccountIds ?? []).Distinct().ToArray();
        if (request.Audience == "Selected" && (recipients.Length == 0 || recipients.Length > 500 || recipients.Contains(Guid.Empty))) throw Invalid();
        if (request.Audience == "Selected" && await db.Accounts.CountAsync(a => recipients.Contains(a.Id) && a.Status == AccountStatus.Active, ct) != recipients.Length) throw Invalid();
        var job = new NotificationBroadcast { SenderUserId = sender, Title = request.Title.Trim(), Message = request.Message.Trim(),
            Audience = request.Audience, Role = request.Audience == "Role" ? request.Role : null, TargetUrl = request.TargetUrl, ExpiresAt = request.ExpiresAt?.ToUniversalTime() };
        if (request.Audience == "Selected") job.Recipients = recipients.Select(id => new NotificationBroadcastRecipient { BroadcastId = job.Id, AccountId = id }).ToList();
        db.Set<NotificationBroadcast>().Add(job); await db.SaveChangesAsync(ct);
        return ToDto(job);
    }
    public async Task<List<NotificationBroadcastDto>> ListAsync(Guid sender, CancellationToken ct) {
        await RequireAsync(sender, ct);
        return (await db.Set<NotificationBroadcast>().Include(j => j.Recipients).AsNoTracking().OrderByDescending(j => j.CreatedAt).Take(100).ToListAsync(ct)).Select(ToDto).ToList();
    }
    public async Task<NotificationBroadcastDto> GetAsync(Guid sender, Guid id, CancellationToken ct) { await RequireAsync(sender, ct); return ToDto(await JobAsync(id, ct)); }
    public async Task<int> PreviewAsync(Guid sender, Guid id, CancellationToken ct) { await RequireAsync(sender, ct); return await Eligible(await JobAsync(id, ct)).CountAsync(ct); }
    public async Task<NotificationBroadcastDto> PublishAsync(Guid sender, Guid id, CancellationToken ct)
    {
        await RequireAsync(sender, ct); var job = await JobAsync(id, ct);
        if (job.Status is "Queued" or "Processing" or "Completed") return ToDto(job);
        if (job.Status is not ("Draft" or "Failed") || job.ExpiresAt <= DateTimeOffset.UtcNow) throw Invalid();
        job.PublishedAt ??= DateTimeOffset.UtcNow; job.Status = "Queued"; job.RetryAt = null; job.Attempts = 0; job.LastError = null;
        job.RecipientCount = await Eligible(job).CountAsync(ct); await db.SaveChangesAsync(ct); return ToDto(job);
    }
    public async Task<NotificationBroadcastDto> CancelAsync(Guid sender, Guid id, CancellationToken ct)
    {
        await RequireAsync(sender, ct); var job = await JobAsync(id, ct);
        if (job.Status == "Completed") throw new ProjectVerificationException("BROADCAST_COMPLETED", "Đợt gửi đã hoàn thành.", 409);
        job.Status = "Cancelled"; job.CompletedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct); return ToDto(job);
    }
    public async Task<bool> ProcessNextBatchAsync(CancellationToken ct)
    {
        // One short transaction per batch. Advisory lock coordinates app instances on PostgreSQL.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsNpgsql() && !await db.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_xact_lock(781246913) AS \"Value\"").SingleAsync(ct)) return false;
        var now = DateTimeOffset.UtcNow;
        var job = await db.Set<NotificationBroadcast>().Include(j => j.Recipients)
            .Where(j => (j.Status == "Queued" || j.Status == "Processing") && (j.RetryAt == null || j.RetryAt <= now))
            .OrderBy(j => j.CreatedAt).FirstOrDefaultAsync(ct);
        if (job == null) return false;
        try {
        if (!await NotificationPermissionPolicy.HasAsync(db, job.SenderUserId, NotificationPermissionPolicy.Manage, ct) ||
            !await db.Accounts.AnyAsync(a => a.Id == job.SenderUserId && a.Role == AccountRole.Admin, ct) || job.ExpiresAt <= now) {
            job.Status = "Cancelled"; job.CompletedAt = now;
        } else {
            job.Status = "Processing";
            var ids = await Eligible(job).Where(a => !db.Set<VerificationNotification>().Any(n => n.RecipientUserId == a.Id && n.BroadcastId == job.Id))
                .OrderBy(a => a.Id).Select(a => a.Id).Take(250).ToListAsync(ct);
            await NotificationService.EnqueueAsync(db, ids, new("SystemAnnouncement", "system", job.Title, job.Message,
                TargetUrl: job.TargetUrl, EventKey: $"broadcast:{job.Id:N}", Audience: job.Audience, SenderUserId: job.SenderUserId, ExpiresAt: job.ExpiresAt), ct);
            foreach (var n in db.Set<VerificationNotification>().Local.Where(n => n.EventKey == $"broadcast:{job.Id:N}")) n.BroadcastId = job.Id;
            job.ProcessedCount += ids.Count; job.LastError = null; job.RetryAt = null;
            if (ids.Count < 250) { job.Status = "Completed"; job.CompletedAt = now; }
        }
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return true;
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) { throw new NotificationBroadcastProcessingException(job.Id, error); }
    }
    public IQueryable<Account> Eligible(NotificationBroadcast job)
    {
        var query = db.Accounts.Where(a => a.Status == AccountStatus.Active);
        if (job.PublishedAt != null) query = query.Where(a => a.CreatedAt <= job.PublishedAt);
        if (job.Audience == "Role") { var role = Enum.Parse<AccountRole>(job.Role!); query = query.Where(a => a.Role == role); }
        if (job.Audience == "Selected") query = query.Where(a => db.Set<NotificationBroadcastRecipient>().Any(r => r.BroadcastId == job.Id && r.AccountId == a.Id));
        return query;
    }
    private async Task<NotificationBroadcast> JobAsync(Guid id, CancellationToken ct) => await db.Set<NotificationBroadcast>().Include(j => j.Recipients).SingleOrDefaultAsync(j => j.Id == id, ct)
        ?? throw new ProjectVerificationException("NOT_FOUND", "Không tìm thấy đợt gửi.", 404);
    private static ProjectVerificationException Invalid() => new("INVALID_BROADCAST", "Nội dung, nhóm nhận hoặc trạng thái đợt gửi không hợp lệ.", 400);
    public static NotificationBroadcastDto ToDto(NotificationBroadcast j) => new(j.Id, j.Title, j.Message, j.Audience, j.Role,
        j.Recipients.Select(r => r.AccountId).ToArray(), j.TargetUrl, j.ExpiresAt, j.Status, j.RecipientCount, j.ProcessedCount, j.CreatedAt, j.PublishedAt, j.CompletedAt, j.LastError);
}
