using UniNet.Domain;

namespace UniNet.Application.DTOs.Verification;

public sealed record StudentVerificationStatusResponse(Guid? Id, VerificationStatus? Status, bool IsVerified,
    string? FullName, string? SchoolName, string? StudentCode, DateTimeOffset? SubmittedAt,
    DateTimeOffset? ReviewedAt, string? RejectReason, string? DocumentName);
public sealed record SubmitStudentVerificationRequest(string FullName, string SchoolName, string StudentCode);
public sealed record StudentVerificationDocument(string FileName, string ContentType, byte[] Bytes);
public sealed record StudentVerificationQueueItem(Guid Id, Guid UserId, string Email, StudentVerificationStatusResponse Verification);
public sealed record StudentVerificationQueueResponse(int Page, int PageSize, int TotalItems, IReadOnlyList<StudentVerificationQueueItem> Items);
public sealed record RejectStudentVerificationRequest(string Reason);
