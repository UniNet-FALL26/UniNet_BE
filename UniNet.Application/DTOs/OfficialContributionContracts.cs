namespace UniNet.Application;

public sealed record OfficialRunRequest(Guid RequestId, Guid GitSnapshotId, Guid PeerSnapshotId);
public sealed record OfficialTransitionRequest(int ExpectedRevision, string? Feedback = null);
public sealed record OfficialSnapshotRequest(DateTimeOffset ObservationCutoff);
public sealed record OfficialWorkflowResponse(string ContractVersion, Guid ProjectId, string Mode, bool CanManage,
    bool ProjectCompleted, IReadOnlyList<string> Blockers, Guid? PublishedRunId,
    Guid? LatestGitSnapshotId = null, Guid? LatestPeerCohortId = null, string? PeerState = null,
    int? PeerSubmittedCount = null, int? PeerRequiredCount = null, Guid? LatestPeerSnapshotId = null, Guid? LatestRunId = null);
public sealed record OfficialReadinessResponse(string ContractVersion, Guid SnapshotId, string GitState,
    string InputHash, IReadOnlyList<GitExperimentalCategoryStatus> Categories, IReadOnlyList<string> Limitations,
    IReadOnlyList<OfficialGitMemberSummary> Members, decimal? ExternalGitShare);
public sealed record OfficialGitMemberSummary(Guid UserId, decimal ProvisionalRawGitScore, decimal? GitScore, decimal? GitShare);
public sealed record OfficialRunResponse(string ContractVersion, Guid Id, Guid ProjectId, Guid RequestId, string State,
    int Revision, string ResultHash, GitExperimentalRunResponse Calculation,
    DateTimeOffset? FinalizedAt, DateTimeOffset? PublishedAt, bool ReusedInputs = false);
public sealed record OfficialPage<T>(string ContractVersion, int Page, int PageSize, int Total, IReadOnlyList<T> Items);
public sealed record OfficialAuditResponse(int Revision, string Action, Guid ActorId, DateTimeOffset At, string? Feedback);
public sealed record OfficialMemberResultResponse(string ContractVersion, Guid ProjectId, Guid RunId,
    DateTimeOffset PublishedAt, Guid UserId, decimal GitShare, decimal PeerShare, decimal ContributionPercent,
    string NormalizationPolicyVersion, string InputHash, string ResultHash);
public sealed record OfficialScoredActivity(string Category, Guid RepositoryId, string ObjectId, Guid? UserId,
    DateTimeOffset ActivityAt, Guid ObservationId, Guid VersionId, string VersionHash);
public sealed record OfficialRunListItem(Guid Id, string State, int Revision, DateTimeOffset CreatedAt,
    DateTimeOffset? FinalizedAt, DateTimeOffset? PublishedAt, bool ReusedInputs = false);
public sealed record OfficialSnapshotResponse(string ContractVersion, Guid SnapshotId, Guid ProjectId,
    DateTimeOffset CreatedAt, DateTimeOffset ObservationCutoff, int PolicyVersion, int RestrictedCount,
    int EntryCount, int VersionCount, int CoverageReceiptCount);
public sealed record OfficialSnapshotMember(Guid AssociationId, Guid RepositoryId, Guid ObservationId,
    Guid JobId, Guid VersionId, string Category, string ScopeKey, string ContentHash, string ValidationStatus, string Reason);
public sealed record OfficialPeerSnapshotResponse(string ContractVersion, Guid SnapshotId, Guid CohortId,
    string PolicyVersion, string InputHash, DateTimeOffset CreatedAt, int ParticipantCount,
    string State, IReadOnlyDictionary<Guid, decimal> Scores);

public sealed record OfficialApprovalRequest(int ExpectedRevision, Guid? ExpectedPublishedRunId, string? Reason = null);
public sealed record OfficialVersionResponse(int Version, Guid RunId, DateTimeOffset PublishedAt, bool IsCurrent,
    Guid? ReplacedByRunId, DateTimeOffset? SupersededAt, string? Reason);
public sealed record OfficialVersionCapabilities(bool Supported, bool CanAdjust, bool CanOpenPeerRound,
    Guid? CurrentPublishedRunId);
