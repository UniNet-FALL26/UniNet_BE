namespace UniNet.Domain.Entities;

public sealed class GitActivity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GitHubProviderObjectId { get; set; }
    public string ActivityType { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }
    public string MetadataJson { get; set; } = "{}";
}
