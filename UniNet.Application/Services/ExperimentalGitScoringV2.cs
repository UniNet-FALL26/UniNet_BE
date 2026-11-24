using System.Globalization;
using UniNet.Domain.Entities;

namespace UniNet.Application.Services;

internal static class ExperimentalGitScoringV2
{
    public const string PolicyVersion = "ExperimentalGitScoringV2";
    public static IReadOnlyList<SnapshotEvidenceEntry> Filter(IReadOnlyList<SnapshotEvidenceEntry> entries,
        ICollection<GitExperimentalExcludedActivityResponse> exclusions)
    {
        var result = new List<SnapshotEvidenceEntry>();
        var parentTypes = entries.Where(x => x.Facts?.Details is not null && x.Category is "issues" or "pull-requests")
            .Select(x => new { x.RepositoryId, Number = x.Category == "pull-requests" ? x.Facts!.Details!.RelatedPullRequestNumber : x.Facts!.Details!.RelatedIssueNumber,
                IsPr = x.Category == "pull-requests" || x.Facts!.Details!.IsPullRequest == true,
                Proven = x.Category == "pull-requests" || x.Facts!.Details!.IsPullRequest.HasValue })
            .Where(x => x.Number is not null && x.Proven).GroupBy(x => (x.RepositoryId, x.Number!.Value))
            .ToDictionary(x => x.Key, x => x.Select(y => y.IsPr).Distinct().ToArray());
        foreach (var entry in entries)
        {
            string? reason = null;
            var details = entry.Facts?.Details;
            if (entry.Status is SnapshotEvidenceStatus.Corrupt or SnapshotEvidenceStatus.Unsupported)
            {
                exclusions.Add(new(entry.Category, entry.RepositoryId, entry.ExternalObjectId,
                    entry.Status.ToString(), entry.Status == SnapshotEvidenceStatus.Corrupt ? "CorruptEvidence" : "UnsupportedEvidenceContract"));
                continue;
            }
            // A PR commit list is a retrieval, not an append-only set. Never union
            // removed SHAs from earlier jobs into a newer PR's final observed list.
            if (entry.Category == "pull-request-commits" && !entries.Any(pr =>
                pr.Category == "pull-requests" && pr.RepositoryId == entry.RepositoryId &&
                pr.AssociationId == entry.AssociationId && pr.JobId == entry.JobId &&
                pr.Facts?.Details?.RelatedPullRequestNumber == details?.RelatedPullRequestNumber))
            {
                exclusions.Add(new(entry.Category, entry.RepositoryId, entry.ExternalObjectId,
                    "Deduplicated", "StalePullRequestRelationship"));
                continue;
            }
            if (entry.Category == "commits")
            {
                if (details?.ParentShas is null) reason = "MergeCommitAttributionUncertain";
                else if (details.ParentShas.Count > 1)
                    reason = details.TreeSha is not null && details.ParentTreeShas?.Count == details.ParentShas.Count && details.ParentTreeShas.Contains(details.TreeSha, StringComparer.OrdinalIgnoreCase)
                        ? "PureMergeCommit" : "MergeCommitAttributionUncertain";
            }
            else if (entry.Category is "issue-comments" or "issue-events")
            {
                var number = details?.RelatedIssueNumber;
                if (number is null && long.TryParse(entry.ScopeKey, NumberStyles.None, CultureInfo.InvariantCulture, out var scope)) number = scope;
                if (number is null || !parentTypes.TryGetValue((entry.RepositoryId, number.Value), out var types) || types.Length != 1)
                    reason = "IssueParentTypeUnresolved";
                else if (types[0]) reason = "PullRequestActivityExcludedFromIssue";
            }
            else if (entry.Category == "issues" && details?.IsPullRequest == true) reason = "IssueIsPullRequest";
            else if (entry.Category == "pull-requests" && details?.Merged == false) reason = "UnmergedPullRequest";
            if (reason is null && (entry.Status != SnapshotEvidenceStatus.Valid || entry.Facts?.Completeness != "Complete"))
            {
                exclusions.Add(new(entry.Category, entry.RepositoryId, entry.ExternalObjectId,
                    "Incomplete", "IncompleteSourceEvidence"));
                continue;
            }
            if (reason is null) result.Add(entry);
            else exclusions.Add(new(entry.Category, entry.RepositoryId, entry.ExternalObjectId,
                reason is "MergeCommitAttributionUncertain" or "IssueParentTypeUnresolved" ? "Incomplete" : "PolicyExcluded", reason));
        }
        return result;
    }

    public static IReadOnlyList<GitExperimentalCategoryStatus> ApplyReadiness(
        IReadOnlyList<GitExperimentalCategoryStatus> categories,
        IReadOnlyList<SnapshotEvidenceEntry> entries,
        int restrictedCount,
        IReadOnlyCollection<GitExperimentalExcludedActivityResponse> exclusions,
        bool verifiedEmptyReviewCoverage = false) => categories.Select(category =>
    {
        bool Related(string name) => category.Category switch
        {
            "Commit" => name == "commits",
            "PR" => name is "pull-requests" or "pull-request-commits",
            "Review" => name == "pull-request-reviews",
            "Issue" => name is "issues" or "issue-events" or "issue-comments",
            _ => false
        };
        var reasons = exclusions.Where(x => Related(x.Category) && x.Reason != "DuplicateObservation" &&
                x.Classification is "Incomplete" or "Corrupt" or "Unsupported" or "Unattributed")
            .Select(x => x.Reason).ToList();
        if (restrictedCount > 0) reasons.Add("SomeRepositoryEvidenceRestricted");
        // Membership does not freeze successful empty-page checkpoint receipts.
        // A currently empty live API is not an immutable proof of NotApplicable.
        if (category.Category == "Review" && category.EligibleActivityCount == 0 && verifiedEmptyReviewCoverage && reasons.Count == 0)
            return category with { Status = "NotApplicable", Limitations = category.Limitations.Append("VerifiedEmptyReviewCoverage").ToArray() };
        if (category.EligibleActivityCount == 0 && !entries.Any(x => Related(x.Category)))
            reasons.Add("EmptyCategoryCoverageNotFrozen");
        return reasons.Count == 0 ? category : category with { Status = "Incomplete",
            Limitations = category.Limitations.Concat(reasons).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() };
    }).ToArray();

    public static bool HasEvidenceGap(IReadOnlyList<GitExperimentalCategoryStatus> categories,
        IReadOnlyList<SnapshotEvidenceEntry> entries, int restrictedCount,
        IReadOnlyCollection<GitExperimentalExcludedActivityResponse> exclusions) =>
        restrictedCount > 0 || categories.Any(x => x.Status is "Incomplete" or "Corrupt" or "Unsupported" or "Unattributed") ||
        entries.Any(x => x.Status != SnapshotEvidenceStatus.Valid) ||
        exclusions.Any(x => x.Reason != "DuplicateObservation" &&
            x.Classification is "Incomplete" or "Corrupt" or "Unsupported" or "Unattributed");

    public static IReadOnlyList<ExperimentalGitActivity> ApplyCommitTimeline(
        IReadOnlyList<ExperimentalGitActivity> activities, IReadOnlyList<SnapshotEvidenceEntry> entries,
        DateTimeOffset cutoff, ICollection<GitExperimentalExcludedActivityResponse> exclusions)
    {
        var dates = entries.Where(x => x.Category == "commits" && x.Status == SnapshotEvidenceStatus.Valid)
            .Select(x => x.Facts?.CommitterDate).Where(x => x is not null && x <= cutoff).Select(x => x!.Value).ToArray();
        var start = dates.Length == 0 ? (DateTimeOffset?)null : dates.Min();
        var end = dates.Length == 0 ? (DateTimeOffset?)null : dates.Max();
        return activities.Where(activity =>
        {
            if (start is not null && activity.ActivityAt >= start && activity.ActivityAt <= end) return true;
            var category = activity.Category switch { ExperimentalGitActivityCategory.Commit => "commits",
                ExperimentalGitActivityCategory.PullRequest => "pull-requests", ExperimentalGitActivityCategory.Review => "pull-request-reviews", _ => "issues" };
            exclusions.Add(new(category, activity.RepositoryId, activity.ObjectId,
                dates.Length == 0 ? "Incomplete" : "PolicyExcluded",
                dates.Length == 0 ? "CommitTimelineUnavailable" : "ActivityOutsideCommitTimeline"));
            return false;
        }).ToArray();
    }
}
