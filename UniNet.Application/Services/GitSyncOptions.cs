namespace UniNet.Application.Services;

public sealed class GitSyncOptions
{
    public const string SectionName = "GitHubSync";

    public bool Enabled { get; set; }
    public bool AdjustmentExecutionEnabled { get; set; }
    // Allows the authenticated manual API while unrestricted background execution remains off.
    public bool ManualEnqueueEnabled { get; set; }
    public GitScopedManualSyncOptions ScopedManual { get; set; } = new();
    public int DefaultIntervalHours { get; set; } = 6;
    public int WorkerConcurrency { get; set; } = 2;
    public int BatchSize { get; set; } = 100;
    public int RetryBaseDelaySeconds { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 8;
    public int JobLeaseDurationSeconds { get; set; } = 300;
    public int PollSeconds { get; set; } = 10;
    public int ReconciliationBatchSize { get; set; } = 25;
    public int ReconciliationIntervalMinutes { get; set; } = 15;
    public int ReconciliationTimeoutSeconds { get; set; } = 30;
    public int ReconciliationPollSeconds { get; set; } = 60;
    public int WebhookLeaseSeconds { get; set; } = 120;
    public int WebhookMaxAttempts { get; set; } = 8;
}

public sealed class GitScopedManualSyncOptions
{
    public bool Enabled { get; set; }
    public Guid ProjectId { get; set; }
    public Guid AssociationId { get; set; }
    // Leave absent in enqueue-only configuration. No consumer claims until explicitly pinned.
    public Guid? JobId { get; set; }
    public int ExecutionTimeoutSeconds { get; set; } = 900;

    public void Validate(bool globalEnabled)
    {
        if (!Enabled || globalEnabled || ProjectId == Guid.Empty || AssociationId == Guid.Empty ||
            JobId == Guid.Empty || ExecutionTimeoutSeconds is < 1 or > 86400)
            throw new InvalidOperationException("Scoped manual synchronization requires exclusive, valid project/association configuration and a bounded deadline.");
    }
}
