using UniNet.Domain.Enums;

namespace UniNet.Application;

public sealed record ContributionParticipantResponse(
    Guid UserId,
    string FullName,
    string Nickname,
    string? AvatarUrl,
    string RoleAtSnapshot,
    bool IsLeader);

public sealed record ContributionCycleResponse(
    Guid CycleId,
    Guid ProjectId,
    ContributionCycleStatus Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? FinalizedAt,
    string FormulaVersion,
    IReadOnlyList<ContributionParticipantResponse> Participants);

public sealed record ContributionPeerEvaluationRequest(int Score, string? Comment);

public sealed record ContributionPeerEvaluationTaskResponse(
    Guid EvaluateeUserId,
    string FullName,
    string Nickname,
    int? Score,
    string? Comment,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? UpdatedAt);

public sealed record ContributionPeerEvaluationResponse(
    Guid CycleId,
    Guid EvaluateeUserId,
    int Score,
    string? Comment,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt);

public sealed record ContributionRunResultResponse(
    Guid UserId,
    string FullName,
    decimal TaskExecutionScore,
    decimal ApprovedDeliverablesScore,
    decimal PeerEvaluationScore,
    decimal TotalScore,
    string CalculationDetails);

public sealed record ContributionRunReviewResponse(
    ContributionReviewDecision Decision,
    string? Feedback,
    DateTimeOffset ReviewedAt);

public sealed record ContributionRunResponse(
    Guid RunId,
    Guid CycleId,
    int RunNumber,
    string FormulaVersion,
    ContributionCalculationRunStatus Status,
    DateTimeOffset CalculatedAt,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ContributionRunResultResponse> Results,
    IReadOnlyList<ContributionRunReviewResponse> ReviewHistory);

public sealed record ContributionReviewRequest(string Decision, string? Feedback);

public sealed record ContributionReviewResponse(
    Guid RunId,
    ContributionReviewDecision Decision,
    ContributionCycleStatus CycleStatus,
    DateTimeOffset ReviewedAt);

public sealed record ContributionFinalResultResponse(
    Guid UserId,
    string FullName,
    string Nickname,
    decimal TotalScore);

public sealed record ContributionFinalResultsResponse(
    Guid CycleId,
    int RunNumber,
    DateTimeOffset FinalizedAt,
    IReadOnlyList<ContributionFinalResultResponse> Results);
