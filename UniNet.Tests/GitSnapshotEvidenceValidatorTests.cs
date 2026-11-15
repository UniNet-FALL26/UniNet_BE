using System.Security.Cryptography;
using System.Text;
using UniNet.Application.Services;
using UniNet.Domain.Entities;
using Xunit;

namespace UniNet.Tests;

public sealed class GitSnapshotEvidenceValidatorTests
{
    private static (GitEvidenceVersion Version, GitHubProviderObject Object) Fixture()
    {
        var source = new GitHubProviderObject { GitHubRepositoryId = Guid.NewGuid(), Category = "commits", ExternalObjectId = "abc" };
        var facts = new GitFrozenFacts(1, "commits", "abc", null, null, null, [], "Incomplete");
        var canonical = GitFrozenFactsCanonicalizer.Canonicalize(facts);
        return (new GitEvidenceVersion { GitHubProviderObjectId = source.Id, GitHubRepositoryId = source.GitHubRepositoryId,
            EvidenceSchemaVersion = 1, CanonicalizationVersion = 1, HashAlgorithm = "SHA-256", ContentHash = canonical.Sha256,
            FrozenFactsJson = Encoding.UTF8.GetString(canonical.Bytes), EvidenceStatus = "Incomplete" }, source);
    }

    [Fact]
    public void ValidIncompleteFactsAreValidatedWithoutPromotingEligibility()
    {
        var (version, source) = Fixture();
        var result = GitSnapshotEvidenceValidator.Validate(version, source);
        Assert.Equal(GitSnapshotEvidenceValidation.Valid, result);
        Assert.Equal("Incomplete", version.EvidenceStatus);
    }

    [Theory]
    [InlineData("schema", GitSnapshotEvidenceValidation.UnsupportedContract)]
    [InlineData("canonicalization", GitSnapshotEvidenceValidation.UnsupportedContract)]
    [InlineData("algorithm", GitSnapshotEvidenceValidation.UnsupportedHashAlgorithm)]
    [InlineData("json", GitSnapshotEvidenceValidation.InvalidFacts)]
    [InlineData("hash", GitSnapshotEvidenceValidation.HashMismatch)]
    [InlineData("identity", GitSnapshotEvidenceValidation.IdentityMismatch)]
    [InlineData("unknown", GitSnapshotEvidenceValidation.InvalidFacts)]
    public void RejectsInvalidHistoricalEvidence(string change, GitSnapshotEvidenceValidation expected)
    {
        var (version, source) = Fixture();
        switch (change)
        {
            case "schema": version.EvidenceSchemaVersion = 4; break;
            case "canonicalization": version.CanonicalizationVersion = 2; break;
            case "algorithm": version.HashAlgorithm = "unsupported"; break;
            case "json": version.FrozenFactsJson = "{"; break;
            case "hash": version.ContentHash = new string('0', 64); break;
            case "identity": source.ExternalObjectId = "other"; break;
            case "unknown": version.FrozenFactsJson = version.FrozenFactsJson.TrimEnd('}') + ",\"secret\":\"x\"}"; break;
        }
        Assert.Equal(expected, GitSnapshotEvidenceValidator.Validate(version, source));
    }

    [Fact]
    public void ValidVersionThreePatchEvidenceIsAccepted()
    {
        var externalId = GitEvidenceFactsExtractor.GetPullRequestFileExternalId("src/a.cs", "42");
        var source = new GitHubProviderObject
        {
            GitHubRepositoryId = Guid.NewGuid(),
            Category = "pull-request-files",
            ExternalObjectId = externalId
        };
        var facts = new GitFrozenFacts(3, source.Category, externalId, null, null, null, [], "Incomplete",
            new GitFrozenDetails(null, null, null, null, null, null, null, null, null, null, 42, null, [],
                FilePath: "src/a.cs", FileStatus: "modified", Additions: 1, Deletions: 0, Changes: 1,
                BlobSha: new string('b', 40), PatchSha256: new string('a', 64)));
        var canonical = GitFrozenFactsCanonicalizer.Canonicalize(facts);
        var version = new GitEvidenceVersion
        {
            GitHubProviderObjectId = source.Id,
            GitHubRepositoryId = source.GitHubRepositoryId,
            EvidenceSchemaVersion = 3,
            CanonicalizationVersion = 1,
            HashAlgorithm = "SHA-256",
            ContentHash = canonical.Sha256,
            FrozenFactsJson = Encoding.UTF8.GetString(canonical.Bytes),
            EvidenceStatus = "Incomplete"
        };

        Assert.Equal(GitSnapshotEvidenceValidation.Valid, GitSnapshotEvidenceValidator.Validate(version, source));
    }

    [Fact]
    public void ExistingVersionThreePathOnlyIdentityRemainsReadable()
    {
        var path = "src/a.cs";
        var externalId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))).ToLowerInvariant();
        var source = new GitHubProviderObject
        {
            GitHubRepositoryId = Guid.NewGuid(), Category = "pull-request-files", ExternalObjectId = externalId
        };
        var facts = new GitFrozenFacts(3, source.Category, externalId, null, null, null, [], "Incomplete",
            new GitFrozenDetails(null, null, null, null, null, null, null, null, null, null, 42, null, [],
                FilePath: path, FileStatus: "modified", Additions: 1, Deletions: 0, Changes: 1,
                BlobSha: new string('b', 40), PatchSha256: new string('a', 64)));
        var canonical = GitFrozenFactsCanonicalizer.Canonicalize(facts);
        var version = new GitEvidenceVersion
        {
            GitHubProviderObjectId = source.Id,
            GitHubRepositoryId = source.GitHubRepositoryId,
            EvidenceSchemaVersion = 3,
            CanonicalizationVersion = 1,
            HashAlgorithm = "SHA-256",
            ContentHash = canonical.Sha256,
            FrozenFactsJson = Encoding.UTF8.GetString(canonical.Bytes),
            EvidenceStatus = "Incomplete"
        };

        Assert.Equal(GitSnapshotEvidenceValidation.Valid, GitSnapshotEvidenceValidator.Validate(version, source));
    }
}
