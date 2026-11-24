namespace UniNet.Application.Services;

internal enum ExperimentalGitActivityCategory
{
    Commit,
    PullRequest,
    Review,
    Issue
}

internal sealed record ExperimentalGitActivity(
    ExperimentalGitActivityCategory Category,
    Guid RepositoryId,
    string ObjectId,
    Guid? UserId,
    DateTimeOffset ActivityAt,
    IReadOnlyCollection<string> RelatedCommitShas);

internal sealed record ExperimentalGitExcludedActivity(
    ExperimentalGitActivityCategory Category,
    Guid RepositoryId,
    string ObjectId,
    string Reason);

internal sealed record ExperimentalGitMemberScore(
    Guid UserId,
    decimal CommitScore,
    decimal PullRequestScore,
    decimal ReviewScore,
    decimal IssueScore,
    decimal GitScore,
    decimal PeerScore,
    decimal TotalScore);

internal sealed record ExperimentalGitScoringResult(
    IReadOnlyList<ExperimentalGitMemberScore> Results,
    IReadOnlyList<ExperimentalGitExcludedActivity> ExcludedActivities,
    IReadOnlyDictionary<ExperimentalGitActivityCategory, int> EligibleActivityCounts,
    IReadOnlyDictionary<ExperimentalGitActivityCategory, int> ExternalActivityCounts,
    IReadOnlyDictionary<Guid, decimal> UnroundedGitWeights,
    decimal ExternalGitWeight);

internal sealed record ExperimentalGitOnlyMemberScore(Guid UserId, decimal CommitScore, decimal PullRequestScore,
    decimal ReviewScore, decimal IssueScore, decimal GitScore);
internal sealed record ExperimentalGitOnlyScoringResult(
    IReadOnlyList<ExperimentalGitOnlyMemberScore> Results,
    IReadOnlyList<ExperimentalGitActivity> ScoredActivities,
    IReadOnlyList<ExperimentalGitExcludedActivity> ExcludedActivities,
    IReadOnlyDictionary<ExperimentalGitActivityCategory, int> EligibleActivityCounts,
    IReadOnlyDictionary<ExperimentalGitActivityCategory, int> ExternalActivityCounts,
    IReadOnlyDictionary<Guid, decimal> UnroundedGitWeights, decimal ExternalGitWeight);

internal static class ExperimentalGitScoringV1
{
    private const MidpointRounding Rounding = MidpointRounding.AwayFromZero;

    public static ExperimentalGitScoringResult Calculate(
        IReadOnlyCollection<Guid> roster,
        IReadOnlyCollection<ExperimentalGitActivity> activities,
        IReadOnlyDictionary<Guid, decimal> peerScores)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentNullException.ThrowIfNull(peerScores);

        var participants = roster.Distinct().Order().ToArray();
        if (participants.Length == 0 || participants.Any(x => x == Guid.Empty))
            throw new ArgumentException("The frozen roster must contain participants.", nameof(roster));
        if (participants.Any(id => !peerScores.TryGetValue(id, out var score) || score is < 0 or > 100))
            throw new ArgumentException("A valid peer score is required for every frozen participant.", nameof(peerScores));
        var git = CalculateGit(roster, activities);
        var results = git.Results.Select(x =>
        {
            var peer = decimal.Round(peerScores[x.UserId], 2, Rounding);
            return new ExperimentalGitMemberScore(x.UserId, x.CommitScore, x.PullRequestScore, x.ReviewScore,
                x.IssueScore, x.GitScore, peer, decimal.Round(.60m * x.GitScore + .40m * peer, 2, Rounding));
        }).ToArray();
        return new(results, git.ExcludedActivities, git.EligibleActivityCounts, git.ExternalActivityCounts,
            git.UnroundedGitWeights, git.ExternalGitWeight);
    }

    // Same category arithmetic and overlap policy, without fabricating any peer inputs.
    public static ExperimentalGitOnlyScoringResult CalculateGit(
        IReadOnlyCollection<Guid> roster, IReadOnlyCollection<ExperimentalGitActivity> activities)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(activities);
        var participants = roster.Distinct().Order().ToArray();
        if (participants.Length == 0 || participants.Any(x => x == Guid.Empty))
            throw new ArgumentException("The frozen roster must contain participants.", nameof(roster));
        if (activities.Any(x => string.IsNullOrWhiteSpace(x.ObjectId) ||
            x.RepositoryId == Guid.Empty || x.UserId is { } userId && !participants.Contains(userId) ||
            x.RelatedCommitShas is null ||
            x.Category == ExperimentalGitActivityCategory.PullRequest &&
            (x.RelatedCommitShas.Count == 0 || x.RelatedCommitShas.Any(sha =>
                sha.Length is not (40 or 64) || !sha.All(Uri.IsHexDigit)))))
            throw new ArgumentException("Experimental activities must have valid identifiers and roster attribution.", nameof(activities));

        var exclusions = new List<ExperimentalGitExcludedActivity>();
        var unique = Deduplicate(activities, exclusions);
        var partialOverlapPrs = FindPartialPullRequestOverlaps(unique);
        foreach (var pr in partialOverlapPrs)
            exclusions.Add(Exclude(pr, "PartialPullRequestOverlapUnsupported"));

        var pullRequests = unique
            .Where(x => x.Category == ExperimentalGitActivityCategory.PullRequest && !partialOverlapPrs.Contains(x))
            .GroupBy(x => string.Join("\u001f", x.RelatedCommitShas.Order(StringComparer.Ordinal)), StringComparer.Ordinal)
            .SelectMany(group =>
            {
                var candidates = group.OrderBy(x => x.ActivityAt).ThenBy(x => x.RepositoryId).ThenBy(x => x.ObjectId, StringComparer.Ordinal).ToArray();
                var earliest = candidates.Min(x => x.ActivityAt);
                var earliestCandidates = candidates.Where(x => x.ActivityAt == earliest).ToArray();
                if (earliestCandidates.Length > 1)
                {
                    foreach (var tied in candidates)
                        exclusions.Add(Exclude(tied, "ExactPullRequestOverlapTieNeedsReview"));
                    return [];
                }
                foreach (var duplicate in candidates.Where(x => !ReferenceEquals(x, earliestCandidates[0])))
                    exclusions.Add(Exclude(duplicate, "DuplicateExactPullRequestWork"));
                return earliestCandidates;
            })
            .ToArray();

        var allPullRequestCommits = unique
            .Where(x => x.Category == ExperimentalGitActivityCategory.PullRequest)
            .SelectMany(x => x.RelatedCommitShas)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var partialOverlapCommits = partialOverlapPrs
            .SelectMany(x => x.RelatedCommitShas)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var scored = unique.Where(x =>
        {
            if (x.Category != ExperimentalGitActivityCategory.Commit)
                return x.Category != ExperimentalGitActivityCategory.PullRequest || pullRequests.Contains(x);
            if (partialOverlapCommits.Contains(x.ObjectId))
            {
                exclusions.Add(Exclude(x, "PartialPullRequestOverlapUnsupported"));
                return false;
            }
            if (allPullRequestCommits.Contains(x.ObjectId))
            {
                exclusions.Add(Exclude(x, "CountedUnderMergedPullRequest"));
                return false;
            }
            return true;
        }).ToArray();

        var counts = Enum.GetValues<ExperimentalGitActivityCategory>()
            .ToDictionary(category => category, category => scored.Count(x => x.Category == category));
        var externalCounts = Enum.GetValues<ExperimentalGitActivityCategory>()
            .ToDictionary(category => category, category => scored.Count(x => x.Category == category && x.UserId is null));
        var categoryScores = Enum.GetValues<ExperimentalGitActivityCategory>()
            .ToDictionary(category => category, category => Normalize(category, scored, participants));

        var results = participants.Select(userId =>
        {
            var commit = categoryScores[ExperimentalGitActivityCategory.Commit][userId];
            var pullRequest = categoryScores[ExperimentalGitActivityCategory.PullRequest][userId];
            var review = categoryScores[ExperimentalGitActivityCategory.Review][userId];
            var issue = categoryScores[ExperimentalGitActivityCategory.Issue][userId];
            var git = decimal.Round(.35m * commit + .30m * pullRequest + .20m * review + .15m * issue, 2, Rounding);
            return new ExperimentalGitOnlyMemberScore(userId, commit, pullRequest, review, issue, git);
        }).ToArray();

        // Keep exact pre-rounding category weights for V2 normalization. Do not derive
        // external mass as 100 minus member GitScore: empty categories are not External.
        var weights = new Dictionary<ExperimentalGitActivityCategory, decimal>
        {
            [ExperimentalGitActivityCategory.Commit] = .35m,
            [ExperimentalGitActivityCategory.PullRequest] = .30m,
            [ExperimentalGitActivityCategory.Review] = .20m,
            [ExperimentalGitActivityCategory.Issue] = .15m
        };
        var gitWeights = participants.ToDictionary(id => id, id => weights.Sum(pair =>
            counts[pair.Key] == 0 ? 0m : pair.Value * scored.Count(x => x.Category == pair.Key && x.UserId == id) / counts[pair.Key]));
        var externalWeight = weights.Sum(pair => counts[pair.Key] == 0 ? 0m : pair.Value * externalCounts[pair.Key] / counts[pair.Key]);
        return new(results, scored, exclusions, counts, externalCounts, gitWeights, externalWeight);
    }

    private static IReadOnlyList<ExperimentalGitActivity> Deduplicate(
        IReadOnlyCollection<ExperimentalGitActivity> activities,
        ICollection<ExperimentalGitExcludedActivity> exclusions)
    {
        var result = new List<ExperimentalGitActivity>();
        foreach (var group in activities.GroupBy(DeduplicationKey, StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(x => x.ActivityAt).ThenBy(x => x.ObjectId, StringComparer.Ordinal).ToArray();
            var first = ordered[0];
            if (ordered.Any(x => x.UserId != first.UserId ||
                x.Category == ExperimentalGitActivityCategory.PullRequest &&
                !x.RelatedCommitShas.Order(StringComparer.Ordinal).SequenceEqual(
                    first.RelatedCommitShas.Order(StringComparer.Ordinal), StringComparer.Ordinal)))
            {
                foreach (var item in ordered)
                    exclusions.Add(Exclude(item, "ConflictingDuplicateActivity"));
                continue;
            }

            result.Add(first);
            foreach (var duplicate in ordered.Skip(1))
                exclusions.Add(Exclude(duplicate, "DuplicateActivity"));
        }

        return result;
    }

    private static IReadOnlySet<ExperimentalGitActivity> FindPartialPullRequestOverlaps(
        IReadOnlyCollection<ExperimentalGitActivity> activities)
    {
        var pullRequests = activities.Where(x => x.Category == ExperimentalGitActivityCategory.PullRequest).ToArray();
        var partial = new HashSet<ExperimentalGitActivity>();
        for (var left = 0; left < pullRequests.Length; left++)
        for (var right = left + 1; right < pullRequests.Length; right++)
        {
            var leftShas = pullRequests[left].RelatedCommitShas.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rightShas = pullRequests[right].RelatedCommitShas.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (leftShas.Overlaps(rightShas) && !leftShas.SetEquals(rightShas))
            {
                partial.Add(pullRequests[left]);
                partial.Add(pullRequests[right]);
            }
        }
        return partial;
    }

    private static Dictionary<Guid, decimal> Normalize(
        ExperimentalGitActivityCategory category,
        IReadOnlyCollection<ExperimentalGitActivity> activities,
        IReadOnlyCollection<Guid> participants)
    {
        var shares = GitContributionFormula.Normalize(activities
            .Where(x => x.Category == category)
            .Select(x => new RawShare(x.UserId, 1m))
            .ToArray());
        return participants.ToDictionary(id => id, id => decimal.Round(shares.GetValueOrDefault(id), 2, Rounding));
    }

    private static string DeduplicationKey(ExperimentalGitActivity activity) =>
        activity.Category == ExperimentalGitActivityCategory.Commit
            ? $"{activity.Category}:{activity.ObjectId.ToUpperInvariant()}"
            : $"{activity.Category}:{activity.RepositoryId:N}:{activity.ObjectId}";

    private static ExperimentalGitExcludedActivity Exclude(ExperimentalGitActivity activity, string reason) =>
        new(activity.Category, activity.RepositoryId, activity.ObjectId, reason);
}
