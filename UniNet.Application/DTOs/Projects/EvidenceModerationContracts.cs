using UniNet.Domain.Enums;

namespace UniNet.Application.DTOs.Projects;

public sealed record EvidenceFileSnapshot(Guid TaskEvidenceId, string FileUrl, string? PublicId,
    string? ResourceType, string? FileName, string? Format);
public sealed record EvidenceReportSnapshot(Guid TaskReportId, int Version, Guid SubmittedById,
    string Content, TaskReportStatus Status, DateTimeOffset SubmittedAt, List<EvidenceFileSnapshot> Evidence,
    Guid? ReportGroupId = null, Guid? ParentReportId = null, bool IsDeleted = false,
    DateTimeOffset? DeletedAt = null, Guid? DeletedById = null);
public sealed record EvidenceTaskSnapshot(Guid ProjectTaskId, ProjectTaskStatus Status, List<EvidenceReportSnapshot> Reports);
public sealed record EvidenceSourceSnapshot(Guid ProjectId, List<EvidenceTaskSnapshot> Tasks);

// The frozen policy deliberately excludes API keys. Missing settings fail in the worker, not completion.
public sealed record EvidencePolicySnapshot(string? Version, string? ContentInstructions, string[] ForbiddenTerms,
    string? TermMatchMode, string? TextNormalization, bool? CaseSensitive, string? GeminiModel,
    string[] ThreatTypes, string[] PlatformTypes, string Scope = "ReportTextAndUrls");

public sealed record EvidenceCheckResult(Guid ProjectTaskId, Guid TaskReportId, Guid? TaskEvidenceId,
    string Kind, string? Url, EvidenceCheckStatus Status, string Reason, string Provider,
    DateTimeOffset CheckedAt, string? Proof = null, string? ErrorCode = null, bool Transient = false);

public sealed record EvidenceModerationResponse(Guid Id, Guid ProjectId, int Version, string SourceHash,
    string PolicyHash, EvidenceModerationStatus Status, bool IsCurrent, DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt, string? Summary, string? LastErrorCode, string? LastError,
    EvidenceSourceSnapshot Source, EvidencePolicySnapshot Policy, List<EvidenceAttemptResponse> Attempts,
    string ScopeLimitation = "Only report text and URL reputation are checked; file contents are not read.");
public sealed record EvidenceAttemptResponse(int Number, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt,
    string? ErrorCode, string? Error, bool Transient, List<EvidenceCheckResult> Results);
