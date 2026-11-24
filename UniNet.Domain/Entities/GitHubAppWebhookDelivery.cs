namespace UniNet.Domain.Entities;

// App deliveries have a separate identity namespace from legacy repository webhooks.
public enum GitHubAppWebhookDeliveryStatus : short { PendingHandler = 0, AwaitingScope = 1, Unsupported = 2, Processing = 3, Processed = 4, RetryScheduled = 5, Failed = 6 }
public enum GitHubAppWebhookRoute : short { Unknown = 0, InstallationLifecycle = 1, RepositoryActivity = 2 }

public sealed class GitHubAppWebhookDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DeliveryId { get; set; } = null!;
    public string EventName { get; set; } = null!;
    public string? Action { get; set; }
    public long? ProviderInstallationId { get; set; }
    public long? ProviderRepositoryId { get; set; }
    public GitHubAppWebhookRoute Route { get; set; }
    public GitHubAppWebhookDeliveryStatus Status { get; set; }
    public string PayloadHash { get; set; } = null!;
    public string PayloadEncrypted { get; set; } = null!;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public int AttemptCount { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? LastErrorCode { get; set; }
}
