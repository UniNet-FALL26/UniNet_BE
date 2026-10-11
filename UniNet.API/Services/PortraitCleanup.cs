using Microsoft.EntityFrameworkCore;
using UniNet.Infrastructure.Data;

namespace UniNet.API.Services;

public sealed class PortraitCleanup(IServiceScopeFactory scopes, PortraitSessions sessions, ILogger<PortraitCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            sessions.Expire();
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
                var cloud = scope.ServiceProvider.GetRequiredService<IPortraitCloudStorage>();
                var cutoff = DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeSeconds();
                var assets = await db.PortraitAssets.Where(a => a.State == 2 || ((a.State == 0 || a.State == 3) && a.CreatedAtUnix < cutoff)).Take(50).ToArrayAsync(stoppingToken);
                foreach (var asset in assets)
                {
                    if (await db.UserProfiles.AnyAsync(p => p.AvatarUrl == asset.ImageUrl || p.CoverUrl == asset.ImageUrl, stoppingToken)) { asset.State = 1; await db.SaveChangesAsync(stoppingToken); continue; }
                    asset.State = 2;
                    // Concurrency conflict with profile Save aborts cleanup before remote deletion.
                    await db.SaveChangesAsync(stoppingToken);
                    await cloud.Destroy(asset.PublicId, stoppingToken);
                    db.PortraitAssets.Remove(asset); await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning("Portrait cleanup will retry ({ErrorType})", error.GetType().Name); }
        }
    }
}
