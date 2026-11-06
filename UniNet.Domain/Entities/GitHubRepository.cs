namespace UniNet.Domain.Entities;

public sealed class GitHubRepository
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long ProviderRepositoryId { get; set; }
    public long ProviderOwnerId { get; set; }
    public string OwnerLogin { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string HtmlUrl { get; set; } = null!;
    public string? Description { get; set; }
    public string? DefaultBranch { get; set; }
    public bool IsPrivate { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset LastVerifiedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
