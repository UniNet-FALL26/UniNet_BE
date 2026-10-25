using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application.DTOs.Verification;
using UniNet.Application.Services;
using UniNet.Infrastructure.Data;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Partner"), Route("api/partner/verification")]
public sealed class PartnerVerificationController(PartnerVerificationService verifications) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        ? id : throw new UniNet.Application.AuthException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401);

    [HttpGet("me")]
    public Task<PartnerVerificationStatusResponse> MyStatus(CancellationToken ct)
        => verifications.GetMyStatusAsync(AccountId, ct);

    [HttpPost]
    public Task<PartnerVerificationStatusResponse> Submit(SubmitPartnerVerificationRequest request, CancellationToken ct)
        => verifications.SubmitAsync(AccountId, request, ct);

    [HttpPost("submit"), Consumes("multipart/form-data"), RequestSizeLimit(6 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<PartnerVerificationStatusResponse> SubmitForm([FromForm] PartnerVerificationForm form, CancellationToken ct)
    {
        PartnerVerificationFile? document = null;
        if (form.Document is not null)
        {
            if (form.Document.Length is < 1 or > PartnerVerificationService.MaximumDocumentBytes)
                throw new UniNet.Application.AuthException("INVALID_VERIFICATION_DOCUMENT", "Tài liệu phải có dung lượng từ 1 byte đến 5 MB.");
            await using var buffer = new MemoryStream();
            await form.Document.CopyToAsync(buffer, ct);
            document = new(form.Document.FileName, form.Document.ContentType, buffer.ToArray());
        }
        return await verifications.SubmitFormAsync(AccountId,
            new(form.TaxCode, form.RepresentativeName, form.Type, form.FileName, form.Url), document, ct);
    }

    [HttpPost("submit-multiple"), Consumes("multipart/form-data"), RequestSizeLimit(52 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 52 * 1024 * 1024)]
    public async Task<PartnerVerificationStatusResponse> SubmitMultiple([FromForm] PartnerVerificationMultipleForm form, CancellationToken ct)
    {
        List<PartnerVerificationUploadDocumentRequest> documents;
        try
        {
            documents = JsonSerializer.Deserialize<List<PartnerVerificationUploadDocumentRequest>>(form.DocumentsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch (JsonException)
        {
            throw new UniNet.Application.AuthException("INVALID_VERIFICATION_DOCUMENTS", "Danh sách tài liệu không hợp lệ.");
        }

        if (form.Files.Count > 10 || form.Files.Any(file => file.Length is < 1 or > PartnerVerificationService.MaximumDocumentBytes))
            throw new UniNet.Application.AuthException("INVALID_VERIFICATION_DOCUMENT", "Mỗi tệp phải có dung lượng từ 1 byte đến 5 MB; tối đa 10 tài liệu.");

        var files = new List<PartnerVerificationFile>(form.Files.Count);
        foreach (var file in form.Files)
        {
            await using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            files.Add(new(file.FileName, file.ContentType, buffer.ToArray()));
        }
        return await verifications.SubmitDocumentsAsync(AccountId, form.TaxCode, form.RepresentativeName, documents, files, ct);
    }
}

public sealed class PartnerVerificationForm
{
    public string? TaxCode { get; set; }
    public string? RepresentativeName { get; set; }
    public UniNet.Domain.VerificationDocumentType Type { get; set; }
    public string FileName { get; set; } = "";
    public string? Url { get; set; }
    public IFormFile? Document { get; set; }
}

public sealed class PartnerVerificationMultipleForm
{
    public string? TaxCode { get; set; }
    public string? RepresentativeName { get; set; }
    public string DocumentsJson { get; set; } = "[]";
    public List<IFormFile> Files { get; set; } = [];
}

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/partner-verifications")]
public sealed class AdminPartnerVerificationsController(PartnerVerificationService verifications, UniNetDbContext db) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        ? id : throw new UniNet.Application.AuthException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401);

    [HttpGet]
    public async Task<PartnerVerificationQueueResponse> Pending([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        await NotificationPermissionPolicy.RequireAsync(db, AccountId, NotificationPermissionPolicy.PartnerVerificationReview, ct);
        return await verifications.ListPendingAsync(page, pageSize, ct);
    }

    [HttpGet("{verificationId:guid}")]
    public async Task<PartnerVerificationDetailResponse> Detail(Guid verificationId, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, AccountId, NotificationPermissionPolicy.PartnerVerificationReview, ct);
        return await verifications.GetPendingAsync(verificationId, ct);
    }

    [HttpGet("{verificationId:guid}/documents/{documentIndex:int}")]
    public async Task<IActionResult> Document(Guid verificationId, int documentIndex, CancellationToken ct)
    {
        var document = await verifications.GetDocumentAsync(AccountId, verificationId, documentIndex, ct);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(document.Bytes, document.ContentType, document.FileName);
    }

    [HttpPost("{verificationId:guid}/approve")]
    public Task<PartnerVerificationDetailResponse> Approve(Guid verificationId, CancellationToken ct)
        => verifications.ApproveAsync(AccountId, verificationId, ct);

    [HttpPost("{verificationId:guid}/reject")]
    public Task<PartnerVerificationDetailResponse> Reject(Guid verificationId, RejectPartnerVerificationRequest request, CancellationToken ct)
        => verifications.RejectAsync(AccountId, verificationId, request.Reason, ct);
}
