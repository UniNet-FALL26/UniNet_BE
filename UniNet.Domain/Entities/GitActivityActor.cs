namespace UniNet.Domain.Entities;

public sealed class GitActivityActor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GitActivityId { get; set; }
    public string Role { get; set; } = null!;
    public string ActorKey { get; set; } = null!;
    public string? ExternalAccountId { get; set; }
    public string? Login { get; set; }
}
