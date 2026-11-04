namespace UniNet.Application.DTOs.Projects;

public sealed record DocumentTextSegment(int Number, string Text,
    int? PageNumber = null, int? ParagraphNumber = null, int? LineNumber = null);
public sealed record ProjectDocumentText(Guid FileId, string FileName, string Format, string Sha256,
    List<DocumentTextSegment> Segments);
public sealed record DocumentCitation(Guid FileId, int SegmentNumber, string Quote);
public sealed record SourcedProjectText(string Value, List<DocumentCitation> Sources);
public sealed record ExtractedProjectSkill(SourcedProjectText Name, Guid? SkillId = null, string? CatalogName = null);
// A document's mention of a person is not a UniNet membership or verified identity.
public sealed record SuggestedProjectMember(SourcedProjectText Name, SourcedProjectText? Role = null);
public sealed record ProjectInformationExtraction
{
    public SourcedProjectText? Title { get; init; }
    public SourcedProjectText? Description { get; init; }
    public SourcedProjectText? ProjectField { get; init; }
    // Preserve the precision actually documented: YYYY, YYYY-MM or YYYY-MM-DD.
    public SourcedProjectText? StartDate { get; init; }
    public SourcedProjectText? EndDate { get; init; }
    public SourcedProjectText? RepositoryUrl { get; init; }
    public SourcedProjectText? DemoUrl { get; init; }
    public List<SourcedProjectText> Features { get; init; } = [];
    public List<ExtractedProjectSkill> Skills { get; init; } = [];
    public List<SuggestedProjectMember> Members { get; init; } = [];
}
public sealed record ProjectExtractionWarning(Guid FileId, string Code);
public sealed record ProjectDraftExtractionResult(int SchemaVersion, string Model,
    DateTimeOffset ExtractedAt, ProjectInformationExtraction Information,
    List<ProjectDocumentText> Documents, List<ProjectExtractionWarning> Warnings);
public sealed record ProjectExtractionSkillCatalogItem(Guid Id, string Name);
