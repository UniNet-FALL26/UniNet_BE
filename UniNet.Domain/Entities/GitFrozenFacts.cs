using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UniNet.Domain.Entities;

// V1 is an allowlist. Never add credentials, raw payloads or arbitrary provider fields.
public sealed record GitFrozenActor(string Role, string? ProviderAccountId);
public sealed record GitFrozenFacts(int SchemaVersion, string Category, string ExternalObjectId,
    DateTimeOffset? AuthorDate, DateTimeOffset? CommitterDate, DateTimeOffset? FirstObservedAt,
    IReadOnlyList<GitFrozenActor> Actors, string Completeness,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GitFrozenDetails? Details = null);
public sealed record GitFrozenDetails(DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt,
    DateTimeOffset? ClosedAt, DateTimeOffset? MergedAt, DateTimeOffset? SubmittedAt,
    string? State, bool? Merged, string? BaseSha, string? HeadSha, string? ReviewCommitSha,
    long? RelatedPullRequestNumber, bool? IsPullRequest, IReadOnlyList<string> MissingFacts,
    long? RelatedIssueNumber = null, string? EventKind = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FilePath = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PreviousFilePath = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FileStatus = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Additions = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Deletions = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Changes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BlobSha = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PatchSha256 = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? ParentShas = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TreeSha = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? ParentTreeShas = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MergeCommitSha = null);
public sealed record CanonicalGitFrozenFacts(byte[] Bytes, string Sha256);

public static class GitFrozenFactsCanonicalizer
{
    public const int CanonicalizationVersion = 1;

    public static CanonicalGitFrozenFacts Canonicalize(GitFrozenFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.SchemaVersion is not (1 or 2 or 3 or 4 or 5) || (facts.SchemaVersion == 1 && facts.Details is not null) ||
            (facts.SchemaVersion is 2 or 3 or 4 or 5 && (facts.Details is null || facts.Details.MissingFacts is null)) || string.IsNullOrWhiteSpace(facts.Category) ||
            string.IsNullOrWhiteSpace(facts.ExternalObjectId) || string.IsNullOrWhiteSpace(facts.Completeness) || facts.Actors is null)
            throw new ArgumentException("Invalid frozen facts contract.", nameof(facts));
        if (facts.SchemaVersion == 5 && (facts.Category != "issue-comments" ||
            facts.Completeness == "Complete" && (facts.Details!.CreatedAt is null || facts.Details.RelatedIssueNumber is null or <= 0 ||
                facts.Details.MissingFacts.Count != 0 || facts.Actors.Count != 1 || facts.Actors[0].Role != "actor" ||
                !long.TryParse(facts.Actors[0].ProviderAccountId, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var actorId) || actorId <= 0)))
            throw new ArgumentException("Invalid issue comment facts.", nameof(facts));
        if (facts.SchemaVersion == 3 && !IsValidPullRequestFileFacts(facts))
            throw new ArgumentException("Invalid pull request file facts.", nameof(facts));
        if (facts.SchemaVersion == 4 && (facts.Details!.ParentShas?.Any(x => !IsOptionalSha(x, 40, 64) || x is null) == true ||
            facts.Details.ParentTreeShas?.Any(x => !IsOptionalSha(x, 40, 64) || x is null) == true ||
            !IsOptionalSha(facts.Details.TreeSha, 40, 64) || !IsOptionalSha(facts.Details.MergeCommitSha, 40, 64)))
            throw new ArgumentException("Invalid commit graph facts.", nameof(facts));
        if (facts.Actors.Any(x => x is null || string.IsNullOrWhiteSpace(x.Role)))
            throw new ArgumentException("Invalid actor.", nameof(facts));
        var normalized = new GitFrozenFacts(facts.SchemaVersion, facts.Category, facts.ExternalObjectId,
            facts.AuthorDate?.ToUniversalTime(), facts.CommitterDate?.ToUniversalTime(), facts.FirstObservedAt?.ToUniversalTime(),
            facts.Actors.OrderBy(x => x.Role, StringComparer.Ordinal).ThenBy(x => x.ProviderAccountId, StringComparer.Ordinal).ToArray(), facts.Completeness,
            facts.Details is null ? null : facts.Details with { CreatedAt = facts.Details.CreatedAt?.ToUniversalTime(),
                UpdatedAt = facts.Details.UpdatedAt?.ToUniversalTime(), ClosedAt = facts.Details.ClosedAt?.ToUniversalTime(),
                MergedAt = facts.Details.MergedAt?.ToUniversalTime(), SubmittedAt = facts.Details.SubmittedAt?.ToUniversalTime(),
                MissingFacts = facts.Details.MissingFacts.OrderBy(x => x, StringComparer.Ordinal).Distinct(StringComparer.Ordinal).ToArray() });
        var bytes = JsonSerializer.SerializeToUtf8Bytes(normalized);
        return new(bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    // Hash equality is not proof of equality; callers must compare canonical bytes before reuse.
    public static bool SameContent(CanonicalGitFrozenFacts left, CanonicalGitFrozenFacts right) =>
        StringComparer.Ordinal.Equals(left.Sha256, right.Sha256) && left.Bytes.AsSpan().SequenceEqual(right.Bytes);

    private static bool IsValidPullRequestFileFacts(GitFrozenFacts facts)
    {
        var details = facts.Details!;
        if (facts.Category != "pull-request-files" || details.FilePath is { Length: > 4096 } ||
            details.FilePath is not null && string.IsNullOrWhiteSpace(details.FilePath) ||
            details.PreviousFilePath is { Length: > 4096 } ||
            details.FileStatus is not (null or "added" or "removed" or "modified" or "renamed") ||
            details.RelatedPullRequestNumber is <= 0 || details.Additions < 0 ||
            details.Deletions < 0 || details.Changes < 0 ||
            !IsOptionalSha(details.BlobSha, 40, 64) || !IsOptionalSha(details.PatchSha256, 64, 64))
            return false;

        var identityMatches = details.FilePath is null || details.RelatedPullRequestNumber is null
            ? HasMissingFact(details, "FilePath") || HasMissingFact(details, "PullRequestScope")
            : StringComparer.Ordinal.Equals(facts.ExternalObjectId, Convert.ToHexString(
                SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                    $"{details.RelatedPullRequestNumber.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n{details.FilePath}"))).ToLowerInvariant()) ||
              StringComparer.Ordinal.Equals(facts.ExternalObjectId, Convert.ToHexString(
                SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(details.FilePath))).ToLowerInvariant());
        return identityMatches &&
            (details.RelatedPullRequestNumber is not null || HasMissingFact(details, "PullRequestScope")) &&
            (details.FileStatus is not null || HasMissingFact(details, "FileStatus")) &&
            (details.Additions is not null || HasMissingFact(details, "Additions")) &&
            (details.Deletions is not null || HasMissingFact(details, "Deletions")) &&
            (details.Changes is not null || HasMissingFact(details, "Changes")) &&
            (details.BlobSha is not null || HasMissingFact(details, "FileBlobSha")) &&
            (details.PatchSha256 is not null || HasMissingFact(details, "PatchContentUnavailable"));
    }

    private static bool HasMissingFact(GitFrozenDetails details, string name) =>
        details.MissingFacts.Contains(name, StringComparer.Ordinal);

    private static bool IsOptionalSha(string? value, int minimumLength, int maximumLength) =>
        value is null || (value.Length == minimumLength || maximumLength != minimumLength && value.Length == maximumLength) &&
        value.All(Uri.IsHexDigit);
}
