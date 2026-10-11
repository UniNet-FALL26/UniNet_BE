using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Domain;
using UniNet.Infrastructure.Data;

namespace UniNet.API.Services;

public sealed record PortraitPreview(string SessionId, string PreviewUrl, bool HasTransparentBackground, DateTimeOffset ExpiresAt);
public sealed record PortraitUpload(string ImageUrl, string PublicId, bool HasTransparentBackground);
public sealed class PortraitSessions(IPortraitProcessor processor)
{
    private sealed class Session(Guid owner, byte[] png)
    {
        public Guid Owner { get; } = owner;
        public byte[] Png { get; } = png;
        public DateTimeOffset Expires { get; } = DateTimeOffset.UtcNow.AddMinutes(30);
        public SemaphoreSlim Gate { get; } = new(1);
    }
    private readonly ConcurrentDictionary<Guid, Session> sessions = new();
    private readonly SemaphoreSlim processGate = new(1);
    public void Expire()
    {
        foreach (var item in sessions.Where(pair => pair.Value.Expires < DateTimeOffset.UtcNow)) sessions.TryRemove(item.Key, out _);
    }
    public async Task<PortraitPreview> Process(Guid owner, IFormFile file, CancellationToken ct)
    {
        if (file.Length <= 0 || file.Length > ProfileImageStorage.MaxBytes) throw new AuthException("INVALID_IMAGE", "Vui lòng chọn ảnh JPG, PNG hoặc WebP tối đa 5 MB.");
        if (!await processGate.WaitAsync(0, ct)) throw new AuthException("PORTRAIT_BUSY", "Dịch vụ đang xử lý ảnh khác. Vui lòng thử lại.", 429);
        try
        {
            Expire();
            if (sessions.Count >= 4 || sessions.Values.Count(s => s.Owner == owner) >= 2)
                throw new AuthException("PORTRAIT_BUSY", "Vui lòng đóng lần chọn ảnh trước hoặc thử lại sau.", 429);
            await using var stream = file.OpenReadStream(); using var original = new MemoryStream();
            var chunk = new byte[81920]; int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                if (original.Length + read > ProfileImageStorage.MaxBytes) throw new AuthException("INVALID_IMAGE", "Ảnh vượt quá 5 MB.");
                await original.WriteAsync(chunk.AsMemory(0, read), ct);
            }
            var bytes = original.ToArray();
            if (ProfileImageStorage.Extension(bytes) is null) throw new AuthException("INVALID_IMAGE", "Định dạng ảnh không hợp lệ.");
            var png = await processor.Process(bytes, ct);
            if (png.Length > 20 * 1024 * 1024) throw new AuthException("PORTRAIT_TOO_LARGE", "Ảnh xử lý quá lớn. Vui lòng chọn ảnh nhỏ hơn.");
            ct.ThrowIfCancellationRequested();
            var id = Guid.NewGuid(); var session = new Session(owner, png); sessions[id] = session;
            return new(id.ToString(), "data:image/png;base64," + Convert.ToBase64String(png), true, session.Expires);
        }
        finally { processGate.Release(); }
    }
    public void Release(Guid owner, Guid id)
    {
        if (sessions.TryGetValue(id, out var session) && session.Owner == owner) sessions.TryRemove(id, out _);
    }
    public async Task<PortraitUpload> Confirm(Guid owner, Guid id, UniNetDbContext db, IPortraitCloudStorage cloud, CancellationToken ct)
    {
        // Durable successful confirmation supports retries even after process restart.
        var existing = await db.PortraitAssets.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.AccountId == owner && (a.State == 0 || a.State == 1), ct);
        if (existing is not null) return new(existing.ImageUrl, existing.PublicId, true);
        if (!sessions.TryGetValue(id, out var session) || session.Owner != owner || session.Expires < DateTimeOffset.UtcNow)
            throw new AuthException("PORTRAIT_SESSION_EXPIRED", "Phiên xử lý ảnh đã hết hạn. Vui lòng chọn ảnh lại.", 404);
        await session.Gate.WaitAsync(ct);
        try
        {
            existing = await db.PortraitAssets.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.AccountId == owner && (a.State == 0 || a.State == 1), ct);
            if (existing is not null) return new(existing.ImageUrl, existing.PublicId, true);
            var publicId = $"uninet/portfolio/portraits/{id:N}";
            var intent = await db.PortraitAssets.SingleOrDefaultAsync(a => a.Id == id, ct);
            if (intent is not null && (intent.AccountId != owner || intent.State == 2))
                throw new AuthException("PORTRAIT_SESSION_EXPIRED", "Phiên xử lý ảnh không hợp lệ.", 404);
            if (intent is null)
            {
                // Durable intent precedes the remote side effect: crashes/timeouts remain discoverable by cleanup.
                intent = new() { Id = id, AccountId = owner, PublicId = publicId, ImageUrl = $"pending:{id:N}", CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), State = 3 };
                db.PortraitAssets.Add(intent); await db.SaveChangesAsync(ct);
            }
            // Same random ID, overwrite=false: retry cannot create an additional remote asset.
            var url = await cloud.Upload(session.Png, publicId, ct);
            intent.ImageUrl = url; intent.State = 0;
            await db.SaveChangesAsync(ct);
            Release(owner, id);
            return new(url, publicId, true);
        }
        finally { session.Gate.Release(); }
    }
}
