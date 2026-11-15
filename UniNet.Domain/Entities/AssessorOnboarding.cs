namespace UniNet.Domain;

public enum AssessorOnboardingStatus : short
{
    Invited = 0, ProfileSubmitted = 1, AwaitingSignature = 2, AwaitingApproval = 3, AwaitingPassword = 4,
    Activated = 5, Rejected = 6, Revoked = 7, Expired = 8, ApprovalConflict = 9
}

public sealed class AssessorOnboarding
{
    public Guid Revision { get; set; } = Guid.NewGuid();
    public string? ReadSessionHash { get; set; }
    public DateTimeOffset? ReadSessionExpiresAt { get; set; }
    public DateTimeOffset? ContractViewedAt { get; set; }
    public int GenerationAttempts { get; set; }
    public DateTimeOffset? GenerationRetryAt { get; set; }
    public string? GenerationErrorCode { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EmailNormalized { get; set; } = "";
    public string? FullName { get; set; }
    public string? SensitiveProfileCiphertext { get; set; }
    public Guid CreatedByAccountId { get; set; }
    public Guid? AccountId { get; set; }
    public AssessorOnboardingStatus Status { get; set; }
    public bool AutoApprove { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? AutoApproveAt { get; set; }
    public Guid? ReviewedByAccountId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewReason { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }

    public string ProfileTokenHash { get; set; } = "";
    public DateTimeOffset ProfileTokenExpiresAt { get; set; }
    public DateTimeOffset? ProfileTokenUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ContractTokenHash { get; set; }
    public DateTimeOffset? ContractTokenExpiresAt { get; set; }
    public string? SetPasswordTokenHash { get; set; }
    public DateTimeOffset? SetPasswordTokenExpiresAt { get; set; }
    public DateTimeOffset? SetPasswordTokenUsedAt { get; set; }

    public string? TemplateVersion { get; set; }
    public string? ContractStorageKey { get; set; }
    public string? ContractSha256 { get; set; }
    public string? SignedName { get; set; }
    public string? SignatureStatement { get; set; }
    public DateTimeOffset? SignedAt { get; set; }
    public DateTimeOffset? EmailConfirmedAt { get; set; }
    public string? OtpHash { get; set; }
    public string? OtpPurpose { get; set; }
    public DateTimeOffset? OtpExpiresAt { get; set; }
    public DateTimeOffset? OtpLastSentAt { get; set; }
    public int OtpFailedAttempts { get; set; }
}

public sealed class AssessorOnboardingEmail
{
    public Guid? LeaseId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CredentialHash { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OnboardingId { get; set; }
    public string Kind { get; set; } = "";
    public string DeduplicationKey { get; set; } = "";
    public string? PayloadCiphertext { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? LastErrorCode { get; set; }
}
