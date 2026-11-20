using UniNet.Application.Services;
using Xunit;

namespace UniNet.Tests;

public sealed class ExperimentalGitScoringV1Tests
{
    [Fact]
    public void NormalizesEachCategoryIncludingExternalActivityAndAppliesGitAndTotalWeights()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var repository = Guid.NewGuid();
        var activities = new[]
        {
            Activity(ExperimentalGitActivityCategory.Commit, repository, "c1", first),
            Activity(ExperimentalGitActivityCategory.Commit, repository, "c2", second),
            Activity(ExperimentalGitActivityCategory.Commit, repository, "c3", null),
            Activity(ExperimentalGitActivityCategory.PullRequest, repository, "p1", first, [new string('f', 40)]),
            Activity(ExperimentalGitActivityCategory.Review, repository, "r1", first),
            Activity(ExperimentalGitActivityCategory.Issue, repository, "i1", first)
        };

        var result = ExperimentalGitScoringV1.Calculate(
            [first, second],
            activities,
            new Dictionary<Guid, decimal> { [first] = 50m, [second] = 25m });

        var firstResult = Assert.Single(result.Results, x => x.UserId == first);
        Assert.Equal(33.33m, firstResult.CommitScore);
        Assert.Equal(1, result.ExternalActivityCounts[ExperimentalGitActivityCategory.Commit]);
        Assert.Equal(100m, firstResult.PullRequestScore);
        Assert.Equal(100m, firstResult.ReviewScore);
        Assert.Equal(100m, firstResult.IssueScore);
        Assert.Equal(76.67m, firstResult.GitScore);
        Assert.Equal(66m, firstResult.TotalScore);

        var secondResult = Assert.Single(result.Results, x => x.UserId == second);
        Assert.Equal(33.33m, secondResult.CommitScore);
        Assert.Equal(11.67m, secondResult.GitScore);
        Assert.Equal(17m, secondResult.TotalScore);
    }

    [Fact]
    public void DeduplicatesCommitShaAndDoesNotCountCommitAgainWhenRepresentedByMergedPullRequest()
    {
        var member = Guid.NewGuid();
        var repository = Guid.NewGuid();
        var sha = new string('a', 40);
        var result = ExperimentalGitScoringV1.Calculate(
            [member],
            [
                Activity(ExperimentalGitActivityCategory.Commit, repository, sha, member),
                Activity(ExperimentalGitActivityCategory.Commit, repository, sha, member),
                Activity(ExperimentalGitActivityCategory.PullRequest, repository, "pr-1", member, [sha])
            ],
            new Dictionary<Guid, decimal> { [member] = 100m });

        var score = Assert.Single(result.Results);
        Assert.Equal(0m, score.CommitScore);
        Assert.Equal(100m, score.PullRequestScore);
        Assert.Contains(result.ExcludedActivities, x => x.ObjectId == sha && x.Reason == "CountedUnderMergedPullRequest");
        Assert.Contains(result.ExcludedActivities, x => x.Reason == "DuplicateActivity");
    }

    [Fact]
    public void MarksPartialPullRequestOverlapUnsupportedInsteadOfSplittingCredit()
    {
        var member = Guid.NewGuid();
        var repository = Guid.NewGuid();
        var shared = new string('a', 40);
        var result = ExperimentalGitScoringV1.Calculate(
            [member],
            [
                Activity(ExperimentalGitActivityCategory.Commit, repository, shared, member),
                Activity(ExperimentalGitActivityCategory.PullRequest, repository, "pr-1", member, [shared, new string('b', 40)]),
                Activity(ExperimentalGitActivityCategory.PullRequest, repository, "pr-2", member, [shared, new string('c', 40)])
            ],
            new Dictionary<Guid, decimal> { [member] = 100m });

        var score = Assert.Single(result.Results);
        Assert.Equal(0m, score.CommitScore);
        Assert.Equal(0m, score.PullRequestScore);
        Assert.Equal(3, result.ExcludedActivities.Count(x => x.Reason == "PartialPullRequestOverlapUnsupported"));
    }

    [Fact]
    public void DoesNotArbitrarilyChooseBetweenMergedPullRequestsWithAnExactTimestampTie()
    {
        var member = Guid.NewGuid();
        var repository = Guid.NewGuid();
        var sha = new string('a', 40);
        var mergedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var activities = new[]
        {
            Activity(ExperimentalGitActivityCategory.PullRequest, repository, "pr-1", member, [sha]) with { ActivityAt = mergedAt },
            Activity(ExperimentalGitActivityCategory.PullRequest, repository, "pr-2", member, [sha]) with { ActivityAt = mergedAt }
        };

        var result = ExperimentalGitScoringV1.Calculate(
            [member], activities, new Dictionary<Guid, decimal> { [member] = 50m });

        Assert.Equal(0m, Assert.Single(result.Results).PullRequestScore);
        Assert.Equal(2, result.ExcludedActivities.Count(x => x.Reason == "ExactPullRequestOverlapTieNeedsReview"));
    }

    private static ExperimentalGitActivity Activity(
        ExperimentalGitActivityCategory category,
        Guid repositoryId,
        string objectId,
        Guid? userId,
        IReadOnlyCollection<string>? relatedCommitShas = null)
        => new(category, repositoryId, objectId, userId, DateTimeOffset.UtcNow, relatedCommitShas ?? []);
}
