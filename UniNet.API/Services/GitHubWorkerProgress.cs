using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace UniNet.API.Services;

// Local process heartbeat only; it must not stand in for shared queue health.
public sealed class GitHubWorkerProgress(TimeProvider? timeProvider = null) : UniNet.Application.Interfaces.IGitHubWorkerProgress
{
    private readonly object gate = new();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly DateTimeOffset started = (timeProvider ?? TimeProvider.System).GetUtcNow();
    private readonly Dictionary<string, DateTimeOffset> completed = new();
    public Guid InstanceId { get; } = Guid.NewGuid();
    public void Completed(string loop) { lock (gate) completed[loop] = clock.GetUtcNow(); }
    public (DateTimeOffset? Worker, DateTimeOffset? Consumer, DateTimeOffset? Reconciliation) Snapshot()
    {
        lock (gate) return (completed.GetValueOrDefault("worker") is var w && w != default ? w : null,
            completed.GetValueOrDefault("consumer") is var c && c != default ? c : null,
            completed.GetValueOrDefault("reconciliation") is var r && r != default ? r : null);
    }
    public bool IsHealthy(DateTimeOffset now, TimeSpan threshold)
    {
        lock (gate) return new[] { "worker", "consumer", "reconciliation" }.All(loop =>
            now - (completed.TryGetValue(loop, out var time) ? time : started) <= threshold);
    }
}

public sealed class GitHubWorkerInstanceHealthCheck(GitHubWorkerProgress progress, IConfiguration configuration, GitHubHealthTransitions? transitions = null) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("GitHubApp:Enabled") || !configuration.GetValue<bool>("GitHubSync:Enabled"))
            return Task.FromResult(transitions?.Record("instance", HealthStatus.Degraded) ?? HealthCheckResult.Degraded());
        var status = progress.IsHealthy(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(15)) ? HealthStatus.Healthy : HealthStatus.Unhealthy;
        return Task.FromResult(transitions?.Record("instance", status) ?? new HealthCheckResult(status));
    }
}
