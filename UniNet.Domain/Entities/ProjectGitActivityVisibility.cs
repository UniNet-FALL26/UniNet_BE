namespace UniNet.Domain.Entities;

public sealed class ProjectGitActivityVisibility
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectGitRepositoryId { get; set; }
    public Guid GitActivityId { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
}
