namespace UniNet.Domain.Enums;

public enum GitSyncJobStatus : short
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    RetryScheduled = 4,
    Cancelled = 5,
    Paused = 6
}
