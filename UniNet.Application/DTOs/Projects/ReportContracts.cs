using System.ComponentModel.DataAnnotations;
using UniNet.Domain.Enums;

namespace UniNet.Application;

public record SubmitReportRequest(Guid AssignmentId,
    [Required, StringLength(10000)] string Summary,
    [Required, StringLength(30000)] string Findings,
    [Required, StringLength(10000)] string Recommendations,
    [Required] ProjectVerificationStatus? Outcome);

public record ReportResponse(Guid Id, Guid AssignmentId, Guid ProjectVerificationId,
    string Summary, string? Findings, string? Recommendations,
    ProjectVerificationStatus? Outcome, Guid SubmittedByUserId, DateTimeOffset SubmittedAt);
