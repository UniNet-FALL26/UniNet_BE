using System.ComponentModel.DataAnnotations;
using UniNet.Domain.Enums;

namespace UniNet.Application;

public record SubmitReportRequest(Guid AssignmentId,
    [Required, StringLength(10000)] string Summary,
    [Required, StringLength(30000)] string Findings,
    [Required, StringLength(10000)] string Recommendations,
    [Required] ProjectVerificationStatus? Outcome);

public record CreateReportVersionRequest(
    [Required, StringLength(10000)] string Summary,
    [Required, StringLength(30000)] string Findings,
    [Required, StringLength(10000)] string Recommendations,
    [Required] ProjectVerificationStatus? Outcome,
    [Required, StringLength(2000)] string Reason);

public record ReportResponse(Guid Id, Guid AssignmentId, Guid ProjectVerificationId,
    int Version, string Summary, string? Findings, string? Recommendations,
    ProjectVerificationStatus? Outcome, Guid SubmittedByUserId, DateTimeOffset SubmittedAt,
    string? RevisionReason, Guid RootReportId);
