using UniNet.Domain;

namespace UniNet.Application.DTOs.Verification;

public sealed record PartnerVerificationDocumentRequest(VerificationDocumentType Type, string FileName, string Url);

public sealed record SubmitPartnerVerificationUploadRequest(
    string? TaxCode,
    string? RepresentativeName,
    VerificationDocumentType Type,
    string FileName,
    string? Url);

public sealed record PartnerVerificationUploadDocumentRequest(
    VerificationDocumentType Type,
    string FileName,
    string? Url,
    int? FileIndex);

public sealed record PartnerVerificationFile(string FileName, string ContentType, byte[] Bytes);
public sealed record PartnerVerificationFileContent(string FileName, string ContentType, byte[] Bytes);

public sealed record SubmitPartnerVerificationRequest(
    string? TaxCode,
    string? RepresentativeName,
    IReadOnlyList<PartnerVerificationDocumentRequest> Documents);

public sealed record PartnerVerificationStatusResponse(
    Guid? Id,
    VerificationStatus? Status,
    string? RejectReason,
    DateTimeOffset? SubmittedAt);

public sealed record PartnerVerificationQueueItem(
    Guid Id,
    Guid UserId,
    string Email,
    string OrganizationName,
    PartnerType? PartnerType,
    VerificationStatus Status,
    DateTimeOffset SubmittedAt);

public sealed record PartnerVerificationQueueResponse(
    int Page,
    int PageSize,
    int TotalItems,
    IReadOnlyList<PartnerVerificationQueueItem> Items);

public sealed record PartnerVerificationDetailResponse(
    Guid Id,
    Guid UserId,
    string Email,
    string OrganizationName,
    PartnerType? PartnerType,
    VerificationStatus Status,
    string VerificationData,
    string? DocumentUrls,
    string? RejectReason,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ReviewedAt);

public sealed record RejectPartnerVerificationRequest(string Reason);
