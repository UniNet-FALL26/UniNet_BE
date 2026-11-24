namespace UniNet.Domain.Entities;

public sealed class GitHubAppWebhookRequeueAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeliveryId { get; set; }
    public Guid ActorAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Reason { get; set; } = null!;
    public string PreviousErrorCode { get; set; } = "";
}
