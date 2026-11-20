using System.Diagnostics.Metrics;

namespace UniNet.Application.Services;

// No payload, account or repository labels: keep metrics low-cardinality.
internal static class GitHubAppDiagnostics
{
    private static readonly Meter Meter = new("UniNet.GitHubApp", "1.0.0");
    internal static readonly Counter<long> Received = Meter.CreateCounter<long>("uninet.github_app.delivery.received");
    internal static readonly Counter<long> Duplicate = Meter.CreateCounter<long>("uninet.github_app.delivery.duplicate");
    internal static readonly Counter<long> Claimed = Meter.CreateCounter<long>("uninet.github_app.delivery.claimed");
    internal static readonly Counter<long> HandlerCompleted = Meter.CreateCounter<long>("uninet.github_app.delivery.handler_completed");
    internal static readonly Counter<long> Retry = Meter.CreateCounter<long>("uninet.github_app.delivery.retry");
    internal static readonly Counter<long> Failed = Meter.CreateCounter<long>("uninet.github_app.delivery.failed");
    internal static readonly Counter<long> Requeued = Meter.CreateCounter<long>("uninet.github_app.delivery.requeued");
    internal static readonly Counter<long> InitialRecovered = Meter.CreateCounter<long>("uninet.github_app.sync.initial_recovered");
    internal static readonly Counter<long> JobEnqueued = Meter.CreateCounter<long>("uninet.github_app.sync.enqueued");
    internal static readonly Counter<long> JobClaimed = Meter.CreateCounter<long>("uninet.github_app.sync.claimed");
    internal static readonly Counter<long> JobCompleted = Meter.CreateCounter<long>("uninet.github.sync.completed");
    internal static readonly Counter<long> JobRetry = Meter.CreateCounter<long>("uninet.github_app.sync.retry");
    internal static readonly Counter<long> JobFailed = Meter.CreateCounter<long>("uninet.github_app.sync.failed");
    internal static readonly Counter<long> InitialActivated = Meter.CreateCounter<long>("uninet.github_app.sync.initial_activated");
    internal static readonly Counter<long> AccessLost = Meter.CreateCounter<long>("uninet.github_app.access.lost");
    internal static readonly Counter<long> AccessRestored = Meter.CreateCounter<long>("uninet.github_app.access.restored");
    internal static readonly Counter<long> ReconciliationDeferred = Meter.CreateCounter<long>("uninet.github_app.reconciliation.deferred");
}
