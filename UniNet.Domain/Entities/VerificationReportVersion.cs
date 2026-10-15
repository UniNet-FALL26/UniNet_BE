using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class VerificationReportVersion
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public VerificationReport Report { get; set; } = null!;
    public int Version { get; set; }
    public string Summary { get; set; } = null!;
    public string Findings { get; set; } = null!;
    public string Recommendations { get; set; } = null!;
    public ProjectVerificationStatus Outcome { get; set; }
    public string Reason { get; set; } = null!;
    public Guid SubmittedByUserId { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
}
