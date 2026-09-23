using Microsoft.EntityFrameworkCore;
using UniNet.Infrastructure.Data;

namespace UniNet.API;

public sealed class ExpiredTokenCleanup(IServiceScopeFactory scopes, ILogger<ExpiredTokenCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
                var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
                await db.RefreshTokens.Where(x => x.ExpiresAt < cutoff || (x.RevokedAt != null && x.RevokedAt < cutoff))
                    .ExecuteDeleteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Refresh token cleanup failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
