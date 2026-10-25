namespace UniNet.Domain.Entities;

public sealed class WorkspaceMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChannelId { get; set; }
    public Guid SenderId { get; set; }
    public string Content { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public WorkspaceChannel Channel { get; set; } = null!;
    public UserProfile Sender { get; set; } = null!;
}
