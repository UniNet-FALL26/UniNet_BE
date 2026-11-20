using System.Text;
using System.Text.Json;
using UniNet.Domain.Entities;
using Xunit;

namespace UniNet.Tests;

public sealed class GitFrozenFactsTests
{
    [Fact]
    public void RoundTripPreservesCanonicalBytesAndNullCommitterDate()
    {
        var facts = new GitFrozenFacts(1, "commits", "abc", DateTimeOffset.Parse("2026-01-01T00:00:00Z"), null, null,
            [new("author", null)], "partial");
        var original = GitFrozenFactsCanonicalizer.Canonicalize(facts);
        var roundTrip = JsonSerializer.Deserialize<GitFrozenFacts>(original.Bytes)!;
        Assert.Null(roundTrip.CommitterDate);
        Assert.True(GitFrozenFactsCanonicalizer.SameContent(original, GitFrozenFactsCanonicalizer.Canonicalize(roundTrip)));
        Assert.Throws<ArgumentNullException>(() => GitFrozenFactsCanonicalizer.Canonicalize(null!));
        Assert.Throws<ArgumentException>(() => GitFrozenFactsCanonicalizer.Canonicalize(facts with { Actors = null! }));
    }
    [Fact]
    public void CanonicalizationOrdersActorsAndNormalizesUtc()
    {
        var time = DateTimeOffset.Parse("2026-01-01T07:00:00+07:00");
        var a = new GitFrozenFacts(1, "commits", "abc", null, time, null,
            [new("committer", "2"), new("author", "1")], "partial");
        var b = a with { CommitterDate = time.ToUniversalTime(), Actors = [new("author", "1"), new("committer", "2")] };
        Assert.True(GitFrozenFactsCanonicalizer.SameContent(GitFrozenFactsCanonicalizer.Canonicalize(a), GitFrozenFactsCanonicalizer.Canonicalize(b)));
        Assert.DoesNotContain("password", Encoding.UTF8.GetString(GitFrozenFactsCanonicalizer.Canonicalize(a).Bytes));
    }

    [Fact]
    public void DistinctFactsDifferAndUnsupportedVersionIsRejected()
    {
        var a = new GitFrozenFacts(1, "commits", "a", null, null, null, [], "partial");
        var first = GitFrozenFactsCanonicalizer.Canonicalize(a);
        var second = GitFrozenFactsCanonicalizer.Canonicalize(a with { ExternalObjectId = "b" });
        Assert.NotEqual(first.Sha256, second.Sha256);
        Assert.False(GitFrozenFactsCanonicalizer.SameContent(first, new(first.Bytes, second.Sha256)));
        Assert.Throws<ArgumentException>(() => GitFrozenFactsCanonicalizer.Canonicalize(a with { SchemaVersion = 2 }));
    }
}
