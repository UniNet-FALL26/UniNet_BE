namespace UniNet.Domain.Entities;

// Each row is an immutable snapshot of one evidence submission, including resubmissions.
public sealed class ProjectVerificationEvidence
{
    public Guid Id { get; set; }
    public Guid ProjectVerificationId { get; set; }
    public int Version { get; set; }
    public Guid SubmittedByUserId { get; set; }
    public string EvidenceJson { get; set; } = "[]";
    public DateTimeOffset SubmittedAt { get; set; }
    public ProjectVerification ProjectVerification { get; set; } = null!;
    public UserProfile SubmittedByUser { get; set; } = null!;
}
