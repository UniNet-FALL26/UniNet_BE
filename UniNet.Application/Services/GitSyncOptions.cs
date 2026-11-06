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
}
