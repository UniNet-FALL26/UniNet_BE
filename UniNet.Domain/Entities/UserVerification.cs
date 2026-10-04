namespace UniNet.Domain;

public sealed class UserVerification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public UserProfile User { get; set; } = null!;
    public VerificationType VerificationType { get; set; }
    public VerificationStatus Status { get; set; } = VerificationStatus.Pending;
    // Snapshot JSON is written by validated backend workflows, never public profile inputs.
    public string VerificationData { get; set; } = "{}";
    public string? DocumentUrls { get; set; }
    public string? RejectReason { get; set; }
    public Guid? ReviewedBy { get; set; }
    public Account? Reviewer { get; set; }
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
