namespace UniNet.Domain.Entities;

public sealed class VerificationNotification
{
    public Guid Id { get; set; }
    public Guid? VerificationId { get; set; }
    public Guid? EvidenceModerationId { get; set; }
    public Guid RecipientUserId { get; set; }
    public string EventKey { get; set; } = null!;
    public string Message { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}
