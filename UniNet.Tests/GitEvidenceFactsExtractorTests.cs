using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UniNet.Application.Services;
using Xunit;

namespace UniNet.Tests;

public sealed class GitEvidenceFactsExtractorTests
{
    [Fact]
    public void V2FreezesFetchedReviewAndPrMetadataWithoutRawSecrets()
    {
        using var review = JsonDocument.Parse("""{"id":9,"user":{"id":7},"state":"APPROVED","submitted_at":"2026-01-01T00:00:00Z","commit_id":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","token":"secret"}""");
        var facts = GitEvidenceFactsExtractor.ExtractV2("pull-request-reviews", "9", review.RootElement, "42");
        Assert.Equal(2, facts.SchemaVersion); Assert.Equal(42, facts.Details!.RelatedPullRequestNumber);
        Assert.Equal(new string('a', 40), facts.Details.ReviewCommitSha); Assert.Equal("7", Assert.Single(facts.Actors).ProviderAccountId);
        Assert.NotNull(facts.Details.SubmittedAt); Assert.Equal("Complete", facts.Completeness);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(facts));
        var bytes = UniNet.Domain.Entities.GitFrozenFactsCanonicalizer.Canonicalize(facts);
        Assert.DoesNotContain("FilePath", Encoding.UTF8.GetString(bytes.Bytes));
        var roundTrip = JsonSerializer.Deserialize<UniNet.Domain.Entities.GitFrozenFacts>(bytes.Bytes)!;
        Assert.Equal(bytes.Sha256, UniNet.Domain.Entities.GitFrozenFactsCanonicalizer.Canonicalize(roundTrip).Sha256);
    }
    [Fact]
    public void V2PullRequestAndIssueKeepOnlyRetrievedEvidence()
    {
        using var pr = JsonDocument.Parse("""{"id":400,"number":42,"user":{"id":8},"state":"closed","merged":true,"created_at":"2025-12-31T00:00:00Z","merged_at":"2026-01-01T00:00:00Z","base":{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"head":{"sha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}}""");
        var facts = GitEvidenceFactsExtractor.ExtractV2("pull-requests", "4", pr.RootElement, null);
        Assert.Equal(new string('a', 40), facts.Details!.BaseSha); Assert.Equal(new string('b', 40), facts.Details.HeadSha);
        Assert.True(facts.Details.Merged); Assert.Equal(42, facts.Details.RelatedPullRequestNumber); Assert.NotNull(facts.Details.MergedAt);
        Assert.Empty(facts.Details.MissingFacts); Assert.Equal("Complete", facts.Completeness);
        using var issue = JsonDocument.Parse("""{"user":{"id":10},"state":"open","created_at":"2026-01-01T00:00:00Z","pull_request":{}}""");
        var issueFacts = GitEvidenceFactsExtractor.ExtractV2("issues", "5", issue.RootElement, null);
        Assert.True(issueFacts.Details!.IsPullRequest); Assert.Equal("Complete", issueFacts.Completeness);
        Assert.Empty(issueFacts.Details.MissingFacts);
    }

    [Fact]
    public void V2DistinguishesOpenAndMergedPullRequestsAndOpenAndClosedIssues()
    {
        using var openPullRequest = JsonDocument.Parse("""{"number":42,"user":{"id":8},"state":"open","created_at":"2026-01-01T00:00:00Z","merged_at":null}""");
        var openPrFacts = GitEvidenceFactsExtractor.ExtractV2("pull-requests", "42", openPullRequest.RootElement, null);
        Assert.Equal("Complete", openPrFacts.Completeness);
        Assert.False(openPrFacts.Details!.Merged);
        Assert.Null(openPrFacts.Details.MergedAt);

        using var closedPullRequest = JsonDocument.Parse("""{"number":43,"user":{"id":8},"state":"closed","created_at":"2026-01-01T00:00:00Z","merged_at":null}""");
        var closedPrFacts = GitEvidenceFactsExtractor.ExtractV2("pull-requests", "43", closedPullRequest.RootElement, null);
        Assert.Equal("Complete", closedPrFacts.Completeness);
        Assert.False(closedPrFacts.Details!.Merged);

        using var openIssue = JsonDocument.Parse("""{"user":{"id":10},"state":"open","created_at":"2026-01-01T00:00:00Z","closed_at":null}""");
        var openIssueFacts = GitEvidenceFactsExtractor.ExtractV2("issues", "50", openIssue.RootElement, null);
        Assert.Equal("Complete", openIssueFacts.Completeness);
        Assert.Null(openIssueFacts.Details!.ClosedAt);

        using var closedIssue = JsonDocument.Parse("""{"user":{"id":10},"state":"closed","created_at":"2026-01-01T00:00:00Z","closed_at":"2026-01-02T00:00:00Z"}""");
        var closedIssueFacts = GitEvidenceFactsExtractor.ExtractV2("issues", "51", closedIssue.RootElement, null);
        Assert.Equal("Complete", closedIssueFacts.Completeness);
        Assert.NotNull(closedIssueFacts.Details!.ClosedAt);
    }

    [Fact]
    public void V2LeavesEvidenceIncompleteWhenRequiredLifecycleOrActorFactsAreMissing()
    {
        using var pr = JsonDocument.Parse("""{"number":42,"user":{"id":8},"created_at":"2026-01-01T00:00:00Z","merged_at":null}""");
        var prFacts = GitEvidenceFactsExtractor.ExtractV2("pull-requests", "42", pr.RootElement, null);
        Assert.Equal("Incomplete", prFacts.Completeness);
        Assert.Contains("PullRequestState", prFacts.Details!.MissingFacts);

        using var missingMergeState = JsonDocument.Parse("""{"number":43,"user":{"id":8},"state":"open","created_at":"2026-01-01T00:00:00Z"}""");
        var missingMergeFacts = GitEvidenceFactsExtractor.ExtractV2("pull-requests", "43", missingMergeState.RootElement, null);
        Assert.Equal("Incomplete", missingMergeFacts.Completeness);
        Assert.Contains("MergedState", missingMergeFacts.Details!.MissingFacts);

        using var issue = JsonDocument.Parse("""{"user":{"id":10},"state":"closed","created_at":"2026-01-01T00:00:00Z","closed_at":null}""");
        var issueFacts = GitEvidenceFactsExtractor.ExtractV2("issues", "51", issue.RootElement, null);
        Assert.Equal("Incomplete", issueFacts.Completeness);
        Assert.Contains("ClosedAt", issueFacts.Details!.MissingFacts);

        using var commit = JsonDocument.Parse("""{"commit":{"committer":{"date":"2026-01-01T00:00:00Z"}},"author":{"login":"unverified"}}""");
        Assert.Equal("Incomplete", GitEvidenceFactsExtractor.ExtractV2("commits", "abc", commit.RootElement, null).Completeness);
    }

    [Fact]
    public void V3PullRequestFileHashesPatchAndOmitsRawContent()
    {
        const string patch = "@@ -1 +1 @@\n-old\n+new";
        using var file = JsonDocument.Parse(
            """{"filename":"src/a.cs","previous_filename":"src/old.cs","status":"renamed","additions":1,"deletions":1,"changes":2,"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","patch":"@@ -1 +1 @@\n-old\n+new"}""");

        var externalId = GitEvidenceFactsExtractor.GetPullRequestFileExternalId("src/a.cs", "42");
        var facts = GitEvidenceFactsExtractor.ExtractV3("pull-request-files", externalId, file.RootElement, "42");
        var expectedPatchHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(patch))).ToLowerInvariant();

        Assert.Equal(3, facts.SchemaVersion);
        Assert.Equal("src/a.cs", facts.Details!.FilePath);
        Assert.Equal("src/old.cs", facts.Details.PreviousFilePath);
        Assert.Equal(42, facts.Details.RelatedPullRequestNumber);
        Assert.Equal(expectedPatchHash, facts.Details.PatchSha256);
        Assert.Equal("Complete", facts.Completeness);
        Assert.Throws<ArgumentException>(() =>
            UniNet.Domain.Entities.GitFrozenFactsCanonicalizer.Canonicalize(facts with { ExternalObjectId = "other" }));

        var sanitizedPayload = GitEvidenceFactsExtractor.SanitizeFetchedPayload("pull-request-files", file.RootElement);
        Assert.DoesNotContain("patch", sanitizedPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("-old", sanitizedPayload);
        Assert.Contains("src/a.cs", sanitizedPayload);
    }

    [Fact]
    public void V3PullRequestFileWithoutPatchStaysExplicitlyIncomplete()
    {
        using var file = JsonDocument.Parse("""{"filename":"assets/image.png","status":"modified","additions":0,"deletions":0,"changes":0}""");

        var facts = GitEvidenceFactsExtractor.ExtractV3(
            "pull-request-files", GitEvidenceFactsExtractor.GetPullRequestFileExternalId("assets/image.png", "42"), file.RootElement, "42");

        Assert.Null(facts.Details!.PatchSha256);
        Assert.Contains("PatchContentUnavailable", facts.Details.MissingFacts);
        Assert.Equal("Incomplete", facts.Completeness);
    }

    [Fact]
    public void PullRequestFileIdentityIncludesThePullRequestNumber()
    {
        Assert.NotEqual(
            GitEvidenceFactsExtractor.GetPullRequestFileExternalId("src/a.cs", "42"),
            GitEvidenceFactsExtractor.GetPullRequestFileExternalId("src/a.cs", "43"));
    }

    [Fact]
    public void FutureTimestampAndUnverifiedActorRemainIncomplete()
    {
        using var payload = JsonDocument.Parse("""{"commit":{"committer":{"date":"2999-01-01T00:00:00Z"}},"author":{"login":"unverified"}}""");
        var facts = GitEvidenceFactsExtractor.Extract("commits", "abc", payload.RootElement);
        Assert.Equal(2999, facts.CommitterDate!.Value.Year); Assert.Null(facts.AuthorDate);
        Assert.Empty(facts.Actors); Assert.Equal("Incomplete", facts.Completeness);
    }

    [Theory]
    [InlineData("pull-requests")]
    [InlineData("pull-request-reviews")]
    [InlineData("issues")]
    public void UnfetchedRelationshipsAreNotInvented(string category)
    {
        using var payload = JsonDocument.Parse("""{"user":{"login":"no-id"},"unsupported":"private"}""");
        var facts = GitEvidenceFactsExtractor.Extract(category, "42", payload.RootElement);
        Assert.Empty(facts.Actors); Assert.Null(facts.CommitterDate); Assert.Equal("Incomplete", facts.Completeness);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(facts));
    }
    [Fact]
    public void MissingCommitterNeverFallsBackToAuthorAndSecretsAreNotIncluded()
    {
        using var payload = JsonDocument.Parse("""{"sha":"abc","token":"do-not-store","commit":{"author":{"date":"2026-01-01T00:00:00Z"},"committer":{"date":"invalid"}},"author":{"id":7,"login":"example"}}""");
        var facts = GitEvidenceFactsExtractor.Extract("commits", "abc", payload.RootElement);
        Assert.NotNull(facts.AuthorDate); Assert.Null(facts.CommitterDate);
        Assert.Equal("Incomplete", facts.Completeness);
        Assert.Single(facts.Actors); Assert.Equal("7", facts.Actors[0].ProviderAccountId);
        Assert.DoesNotContain("do-not-store", JsonSerializer.Serialize(facts));
    }
}
