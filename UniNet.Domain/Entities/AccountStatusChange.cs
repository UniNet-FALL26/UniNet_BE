namespace UniNet.Domain.Entities;

public sealed class AccountStatusChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid ActorAccountId { get; set; }
    public AccountStatus PreviousStatus { get; set; }
    public AccountStatus Status { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
