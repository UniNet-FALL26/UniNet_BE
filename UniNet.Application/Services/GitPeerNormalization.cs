namespace UniNet.Application.Services;

// Versioned business conversion; raw scores and rounded percentages are separate outputs.
internal static class GitPeerNormalization
{
    public const string Version = "GitPeerSeparate60_40V1";

    public static ContributionAllocation Calculate(IReadOnlyDictionary<Guid, decimal> gitWeights, decimal externalGitWeight,
        ScoreState gitState, IReadOnlyDictionary<Guid, decimal> peerScores, ScoreState peerState)
    {
        if (!gitWeights.Keys.ToHashSet().SetEquals(peerScores.Keys) || gitWeights.Count == 0 ||
            gitWeights.Any(x => x.Key == Guid.Empty || x.Value < 0) || peerScores.Any(x => x.Value is < 0 or > 100) || externalGitWeight < 0 ||
            !Enum.IsDefined(gitState) || !Enum.IsDefined(peerState)) throw new ArgumentException("Invalid component inputs.");
        var gitTotal = gitWeights.Values.Sum() + externalGitWeight;
        var peerTotal = peerScores.Values.Sum();
        if (gitState == ScoreState.NotApplicable && gitTotal != 0 || peerState == ScoreState.NotApplicable && peerTotal != 0)
            throw new ArgumentException("NotApplicable components must not contain contribution weight.");
        if (gitState == ScoreState.NotReady || peerState == ScoreState.NotReady)
            return new("Incomplete", new Dictionary<Guid, decimal>(), null);
        if (gitTotal == 0 && peerTotal == 0)
            return new("InsufficientData", new Dictionary<Guid, decimal>(), null);
        // An applicable zero-mass component cannot donate its scoring weight to another component.
        if (gitState == ScoreState.Ready && gitTotal == 0 || peerState == ScoreState.Ready && peerTotal == 0)
            return new("InsufficientData", new Dictionary<Guid, decimal>(), null);
        var gitFactor = gitState == ScoreState.NotApplicable ? 0m : peerState == ScoreState.NotApplicable ? 1m : .60m;
        var peerFactor = peerState == ScoreState.NotApplicable ? 0m : gitState == ScoreState.NotApplicable ? 1m : .40m;
        var memberWeights = gitWeights.Keys.Order().ToDictionary(id => id,
            id => (gitTotal == 0 ? 0 : gitFactor * gitWeights[id] / gitTotal) +
                  (peerTotal == 0 ? 0 : peerFactor * peerScores[id] / peerTotal));
        var externalWeight = gitTotal == 0 ? 0 : gitFactor * externalGitWeight / gitTotal;
        return ContributionPercentAllocator.Allocate(memberWeights, externalWeight, true);
    }
}
