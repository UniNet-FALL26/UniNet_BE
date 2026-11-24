namespace UniNet.Domain.Entities;

public sealed class ProjectEvidenceObservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectGitRepositoryId { get; set; }
    public Guid GitSyncJobId { get; set; }
    public Guid GitEvidenceVersionId { get; set; }
    public Guid GitHubRepositoryId { get; set; }
    public string Category { get; set; } = null!;
    public string ScopeKey { get; set; } = "";
    public DateTimeOffset ObservedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? PageCursor { get; set; }
    public string AuthorizationSource { get; set; } = null!;
    public string RetrievalCompleteness { get; set; } = null!;
}
