using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class GitContributionRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid GitEvidenceSnapshotId { get; set; }
    public int RunNumber { get; set; }
    public string ScoringPolicyVersion { get; set; } = "ExperimentalGitScoringV1";
    public string InputSnapshotJson { get; set; } = "{}";
    public string InputHash { get; set; } = string.Empty;
    public string CategoryStatusJson { get; set; } = "{}";
    public string ResultsJson { get; set; } = "[]";
    public string ExcludedActivitiesJson { get; set; } = "[]";
    public string ReviewHistoryJson { get; set; } = "[]";
    public GitContributionRunStatus Status { get; set; } = GitContributionRunStatus.Draft;
    public Guid CalculatedById { get; set; }
    public DateTimeOffset CalculatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? ReviewedById { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewFeedback { get; set; }
    public Guid? FinalizedById { get; set; }
    public DateTimeOffset? FinalizedAt { get; set; }
    public Guid? PeerSnapshotId { get; set; }
    public string? CalculationContextHash { get; set; }
    public string? NormalizationPolicyVersion { get; set; }
    public string? CalculationStatus { get; set; }
    public string? NormalizedResultsJson { get; set; }
}
