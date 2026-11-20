namespace UniNet.Application.DTOs.Community;

public sealed record ReviewContentReportRequest(bool? IsViolation);

public sealed record ContentReportResponse(
    Guid Id, Guid PostId, Guid ReporterUserId, string ReporterName, string Reason,
    short Status, DateTimeOffset CreatedAt, short PostStatus, bool IsPostDeleted,
    Guid AuthorUserId, string AuthorName, string? Description, List<string> MediaUrls);
