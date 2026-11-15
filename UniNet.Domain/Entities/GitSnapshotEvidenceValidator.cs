using System.Text.Json;
using System.Text.Json.Serialization;

namespace UniNet.Domain.Entities;

// This validator checks a selected historical version; it does not authorize reads,
// choose observations, or decide contribution eligibility.
public enum GitSnapshotEvidenceValidation
{
    Valid, UnsupportedContract, UnsupportedHashAlgorithm, InvalidFacts,
    IdentityMismatch, HashMismatch
}

public static class GitSnapshotEvidenceValidator
{
    private static readonly JsonSerializerOptions StrictJson = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static GitSnapshotEvidenceValidation Validate(GitEvidenceVersion version, GitHubProviderObject source)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(source);
        if (version.GitHubProviderObjectId != source.Id || version.GitHubRepositoryId != source.GitHubRepositoryId)
            return GitSnapshotEvidenceValidation.IdentityMismatch;
        if (version.EvidenceSchemaVersion is not (1 or 2 or 3) || version.CanonicalizationVersion != GitFrozenFactsCanonicalizer.CanonicalizationVersion)
            return GitSnapshotEvidenceValidation.UnsupportedContract;
        if (!StringComparer.Ordinal.Equals(version.HashAlgorithm, "SHA-256"))
            return GitSnapshotEvidenceValidation.UnsupportedHashAlgorithm;
        try
        {
            var facts = JsonSerializer.Deserialize<GitFrozenFacts>(version.FrozenFactsJson, StrictJson);
            if (facts is null || facts.SchemaVersion != version.EvidenceSchemaVersion)
                return GitSnapshotEvidenceValidation.InvalidFacts;
            if (!StringComparer.Ordinal.Equals(facts.Category, source.Category) ||
                !StringComparer.Ordinal.Equals(facts.ExternalObjectId, source.ExternalObjectId))
                return GitSnapshotEvidenceValidation.IdentityMismatch;
            var canonical = GitFrozenFactsCanonicalizer.Canonicalize(facts);
            return StringComparer.Ordinal.Equals(canonical.Sha256, version.ContentHash)
                ? GitSnapshotEvidenceValidation.Valid : GitSnapshotEvidenceValidation.HashMismatch;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
        {
            return GitSnapshotEvidenceValidation.InvalidFacts;
        }
    }
}
