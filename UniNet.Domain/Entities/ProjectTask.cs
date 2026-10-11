namespace UniNet.Domain.Entities;

using UniNet.Domain.Enums;

public sealed class ProjectTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid CreatorId { get; set; }
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public ProjectTaskStatus Status { get; set; }
    public DateTimeOffset Deadline { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Project Project { get; set; } = null!;
    public UserProfile Creator { get; set; } = null!;
    public List<ProjectTaskAssignment> Assignments { get; set; } = [];
    public List<TaskReport> Reports { get; set; } = [];
}
