namespace UniNet.Domain.Enums;

public enum EvidenceModerationStatus : short
{
    Pending, Processing, Passed, Rejected, NeedsEvidence, Error
}

public enum EvidenceCheckStatus : short
{
    Passed, Rejected, Error, NotChecked
}
