namespace UniNet.Application.Services;

internal sealed record GitContributionCategoryReadiness(string Category, string Status, IReadOnlyList<string> Reasons);
internal sealed record GitContributionReadiness(Guid ProjectId, Guid SnapshotId,
    IReadOnlyList<GitContributionCategoryReadiness> Categories);

// No scores or official run are produced. The snapshot reader enforces project and evidence access.
internal sealed class GitContributionReadinessService(ProjectGitEvidenceSnapshotReader reader)
{
    public async Task<GitContributionReadiness> AssessAsync(Guid projectId, Guid snapshotId, Guid accountId, CancellationToken ct)
    {
        var snapshot = await reader.ReadAsync(projectId, snapshotId, accountId, ct);
        var categories = new[] { "commits", "pull-requests", "pull-request-reviews", "issues" };
        return new(projectId, snapshotId, categories.Select(category =>
        {
            var reasons = new List<string> { "ScoringAlgorithmNotSpecified", "AttributionInputsNotFrozen" };
            if (snapshot.RestrictedCount != 0) reasons.Add("EvidenceAccessRestricted");
            if (snapshot.Entries.Any(x => x.Facts?.Category == category && x.Status != SnapshotEvidenceStatus.Valid))
                reasons.Add("EvidenceIncomplete");
            if (snapshot.Entries.Any(x => x.Status is SnapshotEvidenceStatus.Corrupt or SnapshotEvidenceStatus.Unsupported))
                reasons.Add("UnusableEvidencePresent");
            return new GitContributionCategoryReadiness(category, "NotReady", reasons);
        }).ToArray());
    }
}
