namespace UniNet.Domain.Entities;

// Transactional invalidation outbox, not a second notification inbox.
public sealed class NotificationDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid? NotificationId { get; set; }
    public string Reason { get; set; } = "created";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? RetryAt { get; set; }
    public int Attempts { get; set; }
}

public sealed class NotificationPermissionOverride
{
    public Guid AccountId { get; set; }
    public string Permission { get; set; } = "";
    public bool IsGranted { get; set; }
}
