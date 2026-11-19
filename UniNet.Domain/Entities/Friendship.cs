namespace UniNet.Domain.Entities;

public enum FriendshipStatus : short { Pending, Accepted, Rejected, Cancelled, Removed }

public sealed class Friendship
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequesterAccountId { get; set; }
    public Guid ReceiverAccountId { get; set; }
    public Guid LowAccountId { get; private set; }
    public Guid HighAccountId { get; private set; }
    public FriendshipStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RespondedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public long Version { get; set; } = 1;
}
