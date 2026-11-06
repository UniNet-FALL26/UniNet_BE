namespace UniNet.Domain.Entities;

public sealed class GitCommitBranchReference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GitHubProviderObjectId { get; set; }
    public string BranchName { get; set; } = null!;
    public DateTimeOffset FirstObservedAt { get; set; }
}
