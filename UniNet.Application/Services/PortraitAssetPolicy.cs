using Microsoft.EntityFrameworkCore;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public static class PortraitAssetPolicy
{
    public static bool IsManaged(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Host.Equals("res.cloudinary.com", StringComparison.OrdinalIgnoreCase);

    public static async Task Retain(UniNetDbContext db, Guid accountId, string? url, string? previous, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Trim() == previous || !IsManaged(url)) return;
        var asset = await db.PortraitAssets.SingleOrDefaultAsync(x => x.ImageUrl == url.Trim() && x.AccountId == accountId && (x.State == 0 || x.State == 1), ct)
            ?? throw new AuthException("INVALID_PORTRAIT_ASSET", "Ảnh chân dung không thuộc tài khoản hoặc đã hết hạn.");
        asset.State = 1; // Same SaveChanges transaction as the profile; cleanup uses this concurrency token.
    }

    public static async Task RetainReference(UniNetDbContext db, Guid accountId, string? url, string? previous, CancellationToken ct)
    {
        if (IsManaged(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri) && Uri.UnescapeDataString(uri.AbsolutePath).Contains("/uninet/portfolio/portraits/", StringComparison.OrdinalIgnoreCase))
            await Retain(db, accountId, url, previous, ct);
    }
}
