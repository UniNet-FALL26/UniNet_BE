using UniNet.Domain.Entities;

namespace UniNet.Application;

public sealed record ExperimentalPeerOpenRequest(IReadOnlyList<Guid> ParticipantIds);
public sealed record ExperimentalPeerSubmitRequest(int Score, string? Comment, int? ExpectedRevision);
public sealed record ExperimentalPeerRating(Guid EvaluatorId, Guid EvaluateeId, int Score, string? Comment, int Revision, DateTimeOffset UpdatedAt);
public sealed record ExperimentalPeerHistory(Guid EvaluatorId, Guid EvaluateeId, int Revision, int? PreviousScore, int Score, string? PreviousComment, string? Comment, DateTimeOffset ChangedAt);
public sealed record ExperimentalPeerCohortResponse(Guid Id, Guid ProjectId, ExperimentalPeerCohortStatus Status,
    IReadOnlyList<Guid> ParticipantIds, int SubmittedCount, int RequiredCount, Guid? SnapshotId,
    IReadOnlyList<ExperimentalPeerRating> Ratings, IReadOnlyList<ExperimentalPeerHistory> History);
