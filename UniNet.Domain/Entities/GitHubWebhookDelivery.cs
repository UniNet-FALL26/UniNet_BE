namespace UniNet.Domain.Entities;

public sealed class GitHubWebhookDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DeliveryId { get; set; } = null!;
    public string EventName { get; set; } = null!;
    public long? ProviderRepositoryId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public int EnqueuedJobCount { get; set; }
    public bool IsSupportedEvent { get; set; }
}
