namespace UniNet.Domain.Entities;

public sealed class NotificationBroadcast
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SenderUserId { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Audience { get; set; } = "All";
    public string? Role { get; set; }
    public string? TargetUrl { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string Status { get; set; } = "Draft";
    public int RecipientCount { get; set; }
    public int ProcessedCount { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? RetryAt { get; set; }
    public string? LastError { get; set; }
    public List<NotificationBroadcastRecipient> Recipients { get; set; } = [];
}

public sealed class NotificationBroadcastRecipient
{
    public Guid BroadcastId { get; set; }
    public Guid AccountId { get; set; }
}
