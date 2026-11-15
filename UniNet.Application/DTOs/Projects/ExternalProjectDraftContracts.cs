using UniNet.Domain.Enums;

namespace UniNet.Application.DTOs.Projects;

public sealed record UpdateExternalProjectDraftRequest(int Version, ExternalProjectDraftData Data);
public sealed record ConfirmExternalProjectDraftRequest(int Version, bool ConfirmSharingRights);
public sealed record DeclaredProjectMember(string Name, string? Role);
public sealed record ExternalProjectDraftData
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? ProjectField { get; init; }
    public Guid? SpecializationId { get; init; }
    public string? StartDate { get; init; }
    public string? EndDate { get; init; }
    public string? RepositoryUrl { get; init; }
    public string? DemoUrl { get; init; }
    public string? SubmitterRole { get; init; }
    public string? SubmitterContribution { get; init; }
    public bool? SubmitterIsLeader { get; init; }
    public string? LeaderName { get; init; }
    public List<Guid> SkillIds { get; init; } = [];
    public List<Guid> EvidenceFileIds { get; init; } = [];
    public List<string> Features { get; init; } = [];
    public List<DeclaredProjectMember> Members { get; init; } = [];
}
public sealed record DraftUploadedFileResponse(Guid FileId, string FileName, string Url, string Format, string ResourceType, long SizeBytes);
public sealed record ExternalProjectDraftResponse(Guid DraftId, ExternalProjectDraftStatus Status, int Version,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset ExpiresAt, string? ErrorCode,
    Guid? ConfirmedProjectId, ExternalProjectDraftData Data, ProjectDraftExtractionResult? Extraction,
    List<string> MissingFields, List<DraftUploadedFileResponse> Files);
public sealed record ConfirmExternalProjectDraftResponse(Guid DraftId, Guid ProjectId,
    int Version, ProjectOrigin Origin = ProjectOrigin.External, ProjectStatus Status = ProjectStatus.Completed);
