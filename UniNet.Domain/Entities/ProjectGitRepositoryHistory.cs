using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class ProjectGitRepositoryHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectGitRepositoryId { get; set; }
    public Guid ActorProfileId { get; set; }
    public ProjectGitRepositoryHistoryAction Action { get; set; }
    public Guid? PreviousOAuthConnectionId { get; set; }
    public Guid? NewOAuthConnectionId { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
