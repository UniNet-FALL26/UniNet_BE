using UniNet.Application.Services;

namespace UniNet.API.Services;

public sealed class GitHubOAuthFlowCleanupWorker(
    IServiceScopeFactory scopes,
    ILogger<GitHubOAuthFlowCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var cleanup = scope.ServiceProvider.GetRequiredService<GitHubOAuthFlowCleanupService>();
                while (await cleanup.ClearExpiredPendingCredentialsAsync(stoppingToken) > 0)
                {
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "GitHub OAuth flow cleanup failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
