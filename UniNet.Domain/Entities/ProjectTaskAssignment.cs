namespace UniNet.Domain.Entities;

public sealed class ProjectTaskAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectTaskId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;

    public ProjectTask ProjectTask { get; set; } = null!;
    public UserProfile User { get; set; } = null!;
}
