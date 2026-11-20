namespace UniNet.Domain.Entities;

// FrozenFactsJson is a versioned allowlisted contract, never a raw provider payload.
public sealed class GitEvidenceVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GitHubProviderObjectId { get; set; }
    public Guid GitHubRepositoryId { get; set; }
    public int EvidenceSchemaVersion { get; set; }
    public int CanonicalizationVersion { get; set; }
    public string HashAlgorithm { get; set; } = "SHA-256";
    public string ContentHash { get; set; } = null!;
    public string FrozenFactsJson { get; set; } = null!;
    public string EvidenceStatus { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
