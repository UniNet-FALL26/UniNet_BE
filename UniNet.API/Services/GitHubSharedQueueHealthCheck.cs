using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using UniNet.Infrastructure.Data;

namespace UniNet.API.Services;

/// <summary>Inspects durable overdue work; it does not infer health from an individual instance.</summary>
public sealed class GitHubSharedQueueHealthCheck(IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider clock, GitHubHealthTransitions? transitions = null) : IHealthCheck
{
    private HealthCheckResult Report(HealthStatus status) => transitions?.Record("queue", status) ?? new HealthCheckResult(status);
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("GitHubApp:Enabled")) return Report(HealthStatus.Degraded);
        try
        {
            var warning = configuration.GetValue("GitHubApp:QueueHealth:WarningMinutes", 15);
            var critical = configuration.GetValue("GitHubApp:QueueHealth:CriticalMinutes", 60);
            if (warning < 1 || critical <= warning || critical > 10080) return Report(HealthStatus.Unhealthy);
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
            var now = clock.GetUtcNow();
            var heartbeatCutoff = now.AddMinutes(-2);
            if (!await HasClusterProgressAsync(db, heartbeatCutoff, now.AddMinutes(-critical), cancellationToken)) return Report(HealthStatus.Unhealthy);
            if (await HasOverdueAsync(db, now.AddMinutes(-critical), cancellationToken)) return Report(HealthStatus.Unhealthy);
            return !await HasClusterProgressAsync(db, heartbeatCutoff, now.AddMinutes(-warning), cancellationToken) ||
                await HasOverdueAsync(db, now.AddMinutes(-warning), cancellationToken)
                ? Report(HealthStatus.Degraded) : Report(HealthStatus.Healthy);
        }
        catch (Exception) { return Report(HealthStatus.Unhealthy); }
    }

    public static async Task<bool> HasClusterProgressAsync(UniNetDbContext db, DateTimeOffset heartbeatCutoff, DateTimeOffset progressCutoff, CancellationToken ct)
    {
        var heartbeats = db.Set<UniNet.Domain.Entities.GitHubAppWorkerHeartbeat>().AsNoTracking().Where(x => x.UpdatedAt >= heartbeatCutoff);
        return await heartbeats.AnyAsync(x => x.WorkerAt >= progressCutoff, ct) &&
            await heartbeats.AnyAsync(x => x.ConsumerAt >= progressCutoff, ct) &&
            await heartbeats.AnyAsync(x => x.ReconciliationAt >= progressCutoff, ct);
    }

    public static async Task<bool> HasOverdueAsync(UniNetDbContext db, DateTimeOffset cutoff, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandTimeout = 5;
            cmd.CommandText = """
                SELECT EXISTS (
                  SELECT 1 FROM "GitSyncJobs" j
                  JOIN "ProjectGitRepositories" l ON l."Id"=j."ProjectGitRepositoryId"
                  JOIN "GitHubAppRepositoryGrants" g ON g."Id"=l."GitHubAppRepositoryGrantId"
                  JOIN "GitHubAppInstallations" i ON i."Id"=g."InstallationId"
                  JOIN "GitHubRepositories" r ON r."Id"=l."GitHubRepositoryId"
                  WHERE j."CredentialSource"=1 AND l."CredentialSource"=1 AND j."GitHubAppRepositoryGrantId"=g."Id"
                    AND j."GitHubRepositoryId"=l."GitHubRepositoryId" AND g."ProviderRepositoryId"=r."ProviderRepositoryId"
                    AND l."UnlinkedAt" IS NULL AND NOT l."SyncPaused" AND l."AccessStatus"=1
                    AND g."RevokedAt" IS NULL AND i."Status"=0 AND NOT i."VerificationUnknown"
                    AND ((j."Status"=0 AND j."CreatedAt" < @cutoff)
                      OR (j."Status"=4 AND j."NextAttemptAt" IS NOT NULL AND j."NextAttemptAt" < @cutoff)
                      OR (j."Status"=1 AND j."LeaseExpiresAt" < @cutoff))
                    AND (j."Status"=1 OR NOT EXISTS (SELECT 1 FROM "GitSyncJobs" active
                      WHERE active."ProjectGitRepositoryId"=j."ProjectGitRepositoryId" AND active."Status"=1 AND active."LeaseExpiresAt" >= @cutoff))
                ) OR EXISTS (
                  SELECT 1 FROM "GitHubAppWebhookDeliveries" d
                  WHERE d."Route" IN (1,2) AND ((d."Status" IN (0,1) AND COALESCE(d."NextAttemptAt",d."ReceivedAt") < @cutoff)
                    OR (d."Status"=5 AND d."NextAttemptAt" < @cutoff)
                    OR (d."Status"=3 AND d."LeaseExpiresAt" < @cutoff))
                ) OR EXISTS (
                  SELECT 1 FROM "GitHubAppInstallations" i
                  WHERE i."NextReconciliationAt" IS NOT NULL AND i."NextReconciliationAt" < @cutoff
                    AND EXISTS (SELECT 1 FROM "GitHubAppRepositoryGrants" g
                      JOIN "ProjectGitRepositories" l ON l."GitHubAppRepositoryGrantId"=g."Id"
                      WHERE g."InstallationId"=i."Id" AND l."CredentialSource"=1 AND l."UnlinkedAt" IS NULL)
                )
                """;
            var parameter = cmd.CreateParameter(); parameter.ParameterName = "cutoff"; parameter.Value = cutoff; cmd.Parameters.Add(parameter);
            return (bool)(await cmd.ExecuteScalarAsync(ct))!;
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
