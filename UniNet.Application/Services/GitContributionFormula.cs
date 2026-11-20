namespace UniNet.Application.Services;

internal enum ScoreState { NotReady, NotApplicable, Ready }
internal sealed record CategoryScore(ScoreState State, decimal? Value);
internal sealed record RawShare(Guid? ParticipantId, decimal Value); // null is a verified external contributor, never an unknown actor.
internal sealed record GitFormulaResult(decimal? GitScore, decimal? Total);

// Arithmetic only. Callers must first establish eligibility, deduplication, attribution and raw scoring.
// This does not create or authorize an official Contribution Run.
internal static class GitContributionFormula
{
    public static GitFormulaResult Compute(CategoryScore commit, CategoryScore pullRequest, CategoryScore review,
        CategoryScore issue, decimal peerScore, MidpointRounding rounding)
    {
        if (peerScore is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(peerScore));
        var items = new[] { commit, pullRequest, review, issue };
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!Enum.IsDefined(item.State)) throw new ArgumentOutOfRangeException(nameof(item.State));
            if (item.State == ScoreState.Ready && (item.Value is null or < 0 or > 100)) throw new ArgumentOutOfRangeException(nameof(item.Value));
            if (item.State != ScoreState.Ready && item.Value is not null) throw new ArgumentException("Only ready categories carry a score.");
        }
        if (items.Any(x => x.State == ScoreState.NotReady)) return new(null, null);
        var git = .35m * (commit.Value ?? 0) + .30m * (pullRequest.Value ?? 0) +
                  .20m * (review.Value ?? 0) + .15m * (issue.Value ?? 0);
        return new(git, Math.Round(.60m * git + .40m * peerScore, 2, rounding));
    }

    public static IReadOnlyDictionary<Guid, decimal> Normalize(IReadOnlyCollection<RawShare> verifiedRawShares)
    {
        ArgumentNullException.ThrowIfNull(verifiedRawShares);
        if (verifiedRawShares.Any(x => x.Value < 0 || x.ParticipantId == Guid.Empty))
            throw new ArgumentOutOfRangeException(nameof(verifiedRawShares));
        var total = verifiedRawShares.Sum(x => x.Value);
        return verifiedRawShares.Where(x => x.ParticipantId.HasValue).GroupBy(x => x.ParticipantId!.Value)
            .ToDictionary(x => x.Key, x => total == 0 ? 0m : 100m * x.Sum(y => y.Value) / total);
    }
}
