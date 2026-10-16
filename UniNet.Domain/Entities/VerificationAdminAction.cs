namespace UniNet.Domain.Entities;

public sealed class VerificationAdminAction
{
    public Guid Id { get; set; }
    public Guid VerificationId { get; set; }
    public Guid ActorUserId { get; set; }
    public string Decision { get; set; } = null!;
    public string Reason { get; set; } = null!;
    public Guid? RefundId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
