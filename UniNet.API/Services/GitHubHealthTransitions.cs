using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace UniNet.API.Services;

// Per-process transitions: a scrape does not generate repeated alerts.
public sealed class GitHubHealthTransitions(ILogger<GitHubHealthTransitions> logger)
{
    private static readonly Meter Meter = new("UniNet.GitHubHealth", "1.0.0");
    private static readonly Counter<long> Changes = Meter.CreateCounter<long>("uninet.github.health.transitions");
    private readonly object gate = new();
    private readonly Dictionary<string, HealthStatus> last = new(StringComparer.Ordinal);
    public HealthCheckResult Record(string component, HealthStatus status)
    {
        lock (gate)
        {
            if (!last.TryGetValue(component, out var previous) || previous != status)
            {
                last[component] = status;
                Changes.Add(1, new KeyValuePair<string, object?>("component", component), new KeyValuePair<string, object?>("status", status.ToString()));
                logger.LogInformation("GitHub health component {Component} changed to {Status}", component, status);
            }
        }
        return new HealthCheckResult(status);
    }
}
