namespace UniNet.Application.Services;

public sealed class GitSyncOptions
{
    public const string SectionName = "GitHubSync";

    public bool Enabled { get; set; }
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
