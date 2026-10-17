using System.Data;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.Services;
using UniNet.Infrastructure.Data;

namespace UniNet.API.Services;

// No Job table: each loop derives due work from business timestamps.
// Session advisory locks elect one worker per task across all API instances.
public sealed class VerificationBackgroundWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<VerificationBackgroundWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("VerificationWorker:Enabled", true)) return Task.CompletedTask;
        return Task.WhenAll(
            LoopAsync(70101, "Payment reconciliation", (s, now, ct) => s.GetRequiredService<TransactionService>().ReconcilePendingInBackgroundAsync(now, ct), stoppingToken),
            LoopAsync(70102, "Assignment and replacement timeouts", (s, now, ct) => s.GetRequiredService<VerificationDeadlineTasks>().RefundTimeoutsAsync(now, ct), stoppingToken),
            LoopAsync(70103, "Report deadline and escalation", (s, now, ct) => s.GetRequiredService<VerificationDeadlineTasks>().RemindAndEscalateAsync(now, ct), stoppingToken),
            LoopAsync(70104, "Refund reconciliation", (s, now, ct) => s.GetRequiredService<VerificationRefundTasks>().RunAsync(now, ct), stoppingToken),
            LoopAsync(70105, "Verification AI review retry", (s, now, ct) => s.GetRequiredService<VerificationAiTasks>().RunAsync(now, ct), stoppingToken),
            LoopAsync(70106, "Project moderation AI retry", (s, now, ct) => s.GetRequiredService<ProjectModerationService>().RetryTransientErrorsAsync(now, ct), stoppingToken),
            LoopAsync(70107, "Project evidence content and URL moderation", (s, now, ct) => s.GetRequiredService<EvidenceModerationTasks>().RunAsync(now, ct), stoppingToken));
    }

    private async Task LoopAsync(long lockKey, string name,
        Func<IServiceProvider, DateTimeOffset, CancellationToken, Task> action, CancellationToken ct)
    {
        var seconds = Math.Clamp(configuration.GetValue("VerificationWorker:PollSeconds", 60), 10, 3600);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
        try
        {
            do
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
                    // The production store is PostgreSQL. Do not run automatic financial work in test stores.
                    if (!db.Database.IsNpgsql()) return;
                    await db.Database.OpenConnectionAsync(ct);
                    var connection = db.Database.GetDbConnection();
                    var acquired = false;
                    try
                    {
                        await using var command = connection.CreateCommand();
                        command.CommandText = "SELECT pg_try_advisory_lock(@key)";
                        var key = command.CreateParameter(); key.ParameterName = "key"; key.Value = lockKey;
                        command.Parameters.Add(key);
                        acquired = (bool)(await command.ExecuteScalarAsync(ct))!;
                        if (acquired) await action(scope.ServiceProvider, DateTimeOffset.UtcNow, ct);
                    }
                    finally
                    {
                        try
                        {
                            if (acquired && connection.State == ConnectionState.Open)
                            {
                                await using var release = connection.CreateCommand();
                                release.CommandText = "SELECT pg_advisory_unlock(@key)";
                                var key = release.CreateParameter(); key.ParameterName = "key"; key.Value = lockKey;
                                release.Parameters.Add(key);
                                await release.ExecuteScalarAsync(CancellationToken.None);
                            }
                        }
                        finally { await db.Database.CloseConnectionAsync(); }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception error) { logger.LogError(error, "Background task {TaskName} failed; it will be checked again", name); }
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
