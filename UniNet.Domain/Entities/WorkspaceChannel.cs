namespace UniNet.Domain.Entities;

public sealed class WorkspaceChannel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid CreatedById { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Project Project { get; set; } = null!;
    public UserProfile CreatedBy { get; set; } = null!;
    public UserProfile? DeletedBy { get; set; }
    public List<WorkspaceMessage> Messages { get; set; } = [];
}
