namespace UniNet.Domain.Entities;

public sealed class ProjectGitActivityGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectGitRepositoryId { get; set; }
    public Guid GitHubProviderObjectId { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
}
