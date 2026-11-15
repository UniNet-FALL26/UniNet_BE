using UniNet.Domain.Enums;

namespace UniNet.Application;

public sealed record GitExperimentalRunRequest(Guid? SnapshotId, bool AcknowledgeExperimentalPolicy);

public sealed record GitExperimentalReviewRequest(string Decision, string? Feedback);

public sealed record GitExperimentalCategoryStatus(
    string Category,
    string Status,
    int EligibleActivityCount,
    int ExternalActivityCount,
    int ExcludedActivityCount,
    IReadOnlyList<string> Limitations);

public sealed record GitExperimentalExcludedActivityResponse(
    string Category,
    Guid RepositoryId,
    string ObjectId,
    string Classification,
    string Reason);

public sealed record GitExperimentalMemberResult(
    Guid UserId,
    string FullName,
    decimal CommitScore,
    decimal PullRequestScore,
    decimal ReviewScore,
    decimal IssueScore,
    decimal GitScore,
    decimal PeerScore,
    decimal TotalScore);

public sealed record GitExperimentalReviewHistoryEntry(
    string Decision,
    string? Feedback,
    Guid ReviewerId,
    DateTimeOffset ReviewedAt);

public sealed record GitExperimentalRunResponse(
    Guid RunId,
    Guid ProjectId,
    Guid GitEvidenceSnapshotId,
    int RunNumber,
    string ScoringPolicyVersion,
    string InputHash,
    GitContributionRunStatus Status,
    DateTimeOffset CalculatedAt,
    DateTimeOffset? FinalizedAt,
    IReadOnlyList<GitExperimentalCategoryStatus> Categories,
    IReadOnlyList<GitExperimentalMemberResult> Results,
    IReadOnlyList<GitExperimentalExcludedActivityResponse> ExcludedActivities,
    IReadOnlyList<GitExperimentalReviewHistoryEntry> ReviewHistory);

public sealed record GitExperimentalFinalResultsResponse(
    Guid RunId,
    Guid GitEvidenceSnapshotId,
    string ScoringPolicyVersion,
    DateTimeOffset FinalizedAt,
    IReadOnlyList<GitExperimentalMemberResult> Results);
