namespace UniNet.Domain.Entities;

public sealed class GitHubProviderObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GitHubRepositoryId { get; set; }
    public string Category { get; set; } = null!;
    public string ExternalObjectId { get; set; } = null!;
    public DateTimeOffset? OccurredAt { get; set; }
    public DateTimeOffset FirstObservedAt { get; set; }
    public DateTimeOffset LastObservedAt { get; set; }
    public string PayloadJson { get; set; } = null!;
    public string RetrievalMetadataJson { get; set; } = "{}";
}
