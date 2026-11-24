namespace UniNet.Application;

// Intentionally excludes scoring components, identities, evidence and audit hashes.
public sealed record PublishedContributionMember(Guid UserProfileId, decimal ContributionPercent);
public sealed record PublishedContributionResponse(Guid ProjectId, Guid? PublishedRunId, int? Version,
    DateTimeOffset? PublishedAt, IReadOnlyList<PublishedContributionMember> Members);
