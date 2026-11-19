using System.Text.Json;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain.Entities;
using Xunit;

namespace UniNet.Tests;

public sealed class GitSnapshotCoverageValidationTests
{
    [Fact]
    public void EmptyCoverageRequiresFrozenEnumerationAndEveryPrScope()
    {
        var snapshot = Snapshot();
        Assert.True(GitSnapshotCoverageValidation.HasVerifiedEmptyReviewCoverage(snapshot));
        Assert.False(GitSnapshotCoverageValidation.HasVerifiedEmptyReviewCoverage(snapshot with { PolicyVersion = 1 }));
        Assert.False(GitSnapshotCoverageValidation.HasVerifiedEmptyReviewCoverage(snapshot with { Coverage = null }));
        Assert.False(GitSnapshotCoverageValidation.HasVerifiedEmptyReviewCoverage(snapshot with { RestrictedCount = 1 }));
        Assert.False(GitSnapshotCoverageValidation.HasVerifiedEmptyReviewCoverage(snapshot with { Coverage = snapshot.Coverage!.Take(2).ToArray() }));
        Assert.False(GitSnapshotCoverageValidation.HasVerifiedEmptyReviewCoverage(snapshot with { Entries = snapshot.Entries.Append(
            snapshot.Entries[0] with { ObservationId = Guid.NewGuid(), JobId = Guid.NewGuid(), ObservedAt = snapshot.ObservationCutoff }).ToArray() }));
        var categories = ExperimentalGitScoringV2.ApplyReadiness([new("Review", "NotApplicable", 0, 0, 0, [])], [], 0, [], true);
        Assert.Equal("NotApplicable", categories[0].Status);
        Assert.Contains("VerifiedEmptyReviewCoverage", categories[0].Limitations);
    }

    [Theory]
    [InlineData("count")]
    [InlineData("job")]
    [InlineData("project")]
    [InlineData("future")]
    [InlineData("nonempty")]
    public void IncorrectOrNonemptyCoverageNeverProvesEmpty(string change)
    {
        var snapshot = Snapshot();
        var rows = snapshot.Coverage!.ToArray();
        switch (change)
        {
            case "count": rows[0].ItemsProcessed++; break;
            case "job": rows[1].GitSyncJobId = Guid.NewGuid(); break;
            case "project": rows[1].ProjectId = Guid.NewGuid(); break;
            case "future": rows[1].JobCompletedAt = snapshot.ObservationCutoff.AddSeconds(1); break;
            case "nonempty": rows[1].ItemsProcessed = 1; break;
        }
        Assert.False(GitSnapshotCoverageValidation.HasVerifiedEmptyReviewCoverage(snapshot));
    }

    [Fact]
    public void V5CommentDefinesNewCompleteFactsWithoutPromotingHistoricalV4()
    {
        using var item = JsonDocument.Parse("""{"id":99,"user":{"id":42,"login":"not-identity"},"created_at":"2026-01-01T00:00:00Z","body":"private comment","secret":"never-store"}""");
        var old = GitEvidenceFactsExtractor.ExtractV4("issue-comments", "99", item.RootElement, "2");
        var oldHash = GitFrozenFactsCanonicalizer.Canonicalize(old).Sha256;
        var current = GitEvidenceFactsExtractor.ExtractV5IssueComment("99", item.RootElement, "2", DateTimeOffset.Parse("2026-01-02T00:00:00Z"));
        Assert.Equal(5, current.SchemaVersion); Assert.Equal("Complete", current.Completeness);
        Assert.Equal(42.ToString(), Assert.Single(current.Actors).ProviderAccountId);
        Assert.Equal(2, current.Details!.RelatedIssueNumber);
        var json = System.Text.Encoding.UTF8.GetString(GitFrozenFactsCanonicalizer.Canonicalize(current).Bytes);
        Assert.DoesNotContain("private comment", json); Assert.DoesNotContain("never-store", json); Assert.DoesNotContain("not-identity", json);
        Assert.Equal("Incomplete", old.Completeness);
        Assert.Equal(oldHash, GitFrozenFactsCanonicalizer.Canonicalize(old).Sha256);
    }

    [Theory]
    [InlineData("{}", "2")]
    [InlineData("{\"user\":{\"id\":42},\"created_at\":\"bad\"}", "2")]
    [InlineData("{\"user\":{\"id\":42},\"created_at\":\"2026-01-01T00:00:00Z\"}", null)]
    [InlineData("{\"user\":{\"id\":42},\"created_at\":\"2027-01-01T00:00:00Z\"}", "2")]
    public void MissingOrFutureCommentFactsRemainIncomplete(string json, string? scope)
    {
        using var item = JsonDocument.Parse(json);
        var facts = GitEvidenceFactsExtractor.ExtractV5IssueComment("99", item.RootElement, scope, DateTimeOffset.Parse("2026-01-02T00:00:00Z"));
        Assert.Equal("Incomplete", facts.Completeness);
        Assert.NotEmpty(facts.Details!.MissingFacts);
        GitFrozenFactsCanonicalizer.Canonicalize(facts);
    }

    private static ProjectGitEvidenceSnapshotRead Snapshot()
    {
        var snapshot = Guid.NewGuid(); var project = Guid.NewGuid(); var association = Guid.NewGuid(); var repo = Guid.NewGuid(); var job = Guid.NewGuid();
        var time = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var entries = new[] { 1L, 3L }.Select(n => new SnapshotEvidenceEntry(association, repo, Guid.NewGuid(), job, Guid.NewGuid(),
            "pull-requests", "", n.ToString(), time, "hash", SnapshotEvidenceStatus.Valid, "Valid",
            new(4, "pull-requests", n.ToString(), null, null, null, [], "Complete",
                new(null, null, null, null, null, "open", false, null, null, null, n, null, [])))).ToArray();
        ProjectGitEvidenceSnapshotCoverage Receipt(string category, string scope, long count) => new()
        { SnapshotId = snapshot, ProjectId = project, ProjectGitRepositoryId = association, GitSyncJobId = job,
            GitSyncCheckpointId = Guid.NewGuid(), Category = category, ScopeKey = scope, ItemsProcessed = count,
            ScopeCompletedAt = time, JobCompletedAt = time.AddSeconds(1) };
        return new(snapshot, project, time.AddSeconds(2), time.AddSeconds(2), 2, 0, entries,
            [Receipt("pull-requests", "", 2), Receipt("pull-request-reviews", "1", 0), Receipt("pull-request-reviews", "3", 0)]);
    }
}
