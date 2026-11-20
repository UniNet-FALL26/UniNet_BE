using UniNet.Application.Services;
using UniNet.Domain.Entities;
using Xunit;

namespace UniNet.Tests;

public sealed class ExperimentalGitScoringV2Tests
{
    [Fact]
    public void IncompleteSourceCannotBecomeScoredWorkButPrChildrenRemainPolicyExcluded()
    {
        var repo = Guid.NewGuid();
        var issue = E(repo, "issues", "issue", D() with { RelatedIssueNumber = 2, IsPullRequest = false });
        var comment = E(repo, "issue-comments", "comment", D() with { RelatedIssueNumber = 2 }) with
        { Status = SnapshotEvidenceStatus.Incomplete };
        var pr = E(repo, "pull-requests", "pr", D() with { RelatedPullRequestNumber = 1 });
        var prEvent = E(repo, "issue-events", "event", D() with { RelatedIssueNumber = 1 }) with
        { Status = SnapshotEvidenceStatus.Incomplete };
        var excluded = new List<UniNet.Application.GitExperimentalExcludedActivityResponse>();
        var selected = ExperimentalGitScoringV2.Filter([issue, comment, pr, prEvent], excluded);
        Assert.DoesNotContain(comment, selected);
        Assert.Contains(excluded, x => x.ObjectId == "comment" && x.Classification == "Incomplete");
        Assert.Contains(excluded, x => x.ObjectId == "event" && x.Reason == "PullRequestActivityExcludedFromIssue");
    }

    [Fact]
    public void OldPrCommitMembershipDoesNotSurviveAReplacementRetrieval()
    {
        var repo = Guid.NewGuid();
        var pr = E(repo, "pull-requests", "pr", D() with { RelatedPullRequestNumber = 1 });
        var old = E(repo, "pull-request-commits", "removed-sha", D() with { RelatedPullRequestNumber = 1 }) with { ScopeKey = "1" };
        var current = E(repo, "pull-request-commits", "current-sha", D() with { RelatedPullRequestNumber = 1 }) with
        { ScopeKey = "1", JobId = pr.JobId, AssociationId = pr.AssociationId };
        var excluded = new List<UniNet.Application.GitExperimentalExcludedActivityResponse>();
        var selected = ExperimentalGitScoringV2.Filter([pr, old, current], excluded);
        Assert.DoesNotContain(old, selected);
        Assert.Contains(current, selected);
        Assert.Contains(excluded, x => x.ObjectId == "removed-sha" && x.Reason == "StalePullRequestRelationship");
    }
    [Fact]
    public void MergeGraphEvidenceSeparatesNormalPureAndUncertainCommits()
    {
        var repo = Guid.NewGuid();
        var normal = E(repo, "commits", "normal", D() with { ParentShas = [new string('a',40)] });
        var pure = E(repo, "commits", "merge", D() with { ParentShas = [new string('a',40),new string('b',40)], TreeSha = new string('c',40), ParentTreeShas = [new string('c',40),new string('d',40)] });
        var resolution = E(repo, "commits", "resolution", pure.Facts!.Details! with { TreeSha = new string('e',40) });
        var missing = E(repo, "commits", "legacy", D());
        var exclusions = new List<UniNet.Application.GitExperimentalExcludedActivityResponse>();
        var selected = ExperimentalGitScoringV2.Filter([normal,pure,resolution,missing],exclusions);
        Assert.Equal(normal, Assert.Single(selected));
        Assert.Contains(exclusions,x=>x.ObjectId=="merge" && x.Reason=="PureMergeCommit");
        Assert.Equal(2,exclusions.Count(x=>x.Reason=="MergeCommitAttributionUncertain"));
    }
    [Fact]
    public void IssueChildScopeIsRepositorySpecificAndCannotCreditPrWork()
    {
        var r=Guid.NewGuid(); var other=Guid.NewGuid();
        var pr=E(r,"pull-requests","pr",D() with { RelatedPullRequestNumber=1 });
        var issue=E(other,"issues","issue",D() with { RelatedIssueNumber=1,IsPullRequest=false });
        var prComment=E(r,"issue-comments","comment",D() with { RelatedIssueNumber=1 });
        var issueEvent=E(other,"issue-events","event",D() with { RelatedIssueNumber=1 });
        var missing=E(r,"issue-events","unknown",D() with { RelatedIssueNumber=2 });
        var exclusions=new List<UniNet.Application.GitExperimentalExcludedActivityResponse>();
        var selected=ExperimentalGitScoringV2.Filter([pr,issue,prComment,issueEvent,missing],exclusions);
        Assert.Contains(issueEvent,selected);Assert.DoesNotContain(prComment,selected);Assert.DoesNotContain(missing,selected);
        Assert.Contains(exclusions,x=>x.Reason=="IssueParentTypeUnresolved");
    }

    [Fact]
    public void ZeroObservedReviewsAreNotAnImmutableEmptyCoverageReceipt()
    {
        var categories = new[] { new UniNet.Application.GitExperimentalCategoryStatus("Review", "NotApplicable", 0, 0, 0, []) };
        var actual = ExperimentalGitScoringV2.ApplyReadiness(categories, [], 0, []);
        Assert.Equal("Incomplete", Assert.Single(actual).Status);
        Assert.Contains("EmptyCategoryCoverageNotFrozen", actual[0].Limitations);
        Assert.True(ExperimentalGitScoringV2.HasEvidenceGap(actual, [], 0, []));
    }

    [Fact]
    public void ActivityTimelineUsesCommitterDatesAndNeverObservationOrAuthorDate()
    {
        var repo = Guid.NewGuid(); var member = Guid.NewGuid(); var t = DateTimeOffset.Parse("2026-11-01T00:00:00Z");
        var entry = E(repo, "commits", "sha", D());
        entry = entry with { Facts = entry.Facts! with { AuthorDate = t.AddDays(-9), CommitterDate = t } };
        var atBoundary = new ExperimentalGitActivity(ExperimentalGitActivityCategory.Issue, repo, "valid", member, t, []);
        var tooEarly = atBoundary with { ObjectId = "early", ActivityAt = t.AddDays(-1) };
        var tooLate = atBoundary with { ObjectId = "late", ActivityAt = t.AddDays(1) };
        var excluded = new List<UniNet.Application.GitExperimentalExcludedActivityResponse>();
        Assert.Equal(atBoundary, Assert.Single(ExperimentalGitScoringV2.ApplyCommitTimeline([atBoundary, tooEarly, tooLate], [entry], t.AddDays(3), excluded)));
        Assert.All(excluded, x => Assert.Equal("ActivityOutsideCommitTimeline", x.Reason));
        excluded.Clear();
        Assert.Empty(ExperimentalGitScoringV2.ApplyCommitTimeline([atBoundary], [entry with { Facts = entry.Facts! with { CommitterDate = null } }], t.AddDays(3), excluded));
        Assert.Equal("CommitTimelineUnavailable", Assert.Single(excluded).Reason);
    }
    private static GitFrozenDetails D()=>new(null,null,null,null,null,null,null,null,null,null,null,null,[]);
    private static SnapshotEvidenceEntry E(Guid repo,string category,string id,GitFrozenDetails details)=>new(Guid.NewGuid(),repo,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),category,"",id,DateTimeOffset.UtcNow,"hash",SnapshotEvidenceStatus.Valid,"Valid",new(4,category,id,null,null,null,[],"Complete",details));
}
